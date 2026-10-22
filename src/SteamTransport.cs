using System;
using System.Collections.Concurrent;
using System.Text;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace IronbarkCoop
{
    // Steam friends play through the game's own initialized Steam client: a private friends-only lobby for discovery
    // and invites, Steam P2P with Valve relay for traffic (no ports, no IP exchange). Main thread only.
    internal sealed class SteamTransport : ITransport
    {
        const int ControlChannel = 0, PoseChannel = 1, VoiceChannel = 2;
        const int UnreliableLimit = 1200;
        const string LobbyKey = "ironbark-coop";
        public ConcurrentQueue<string> Incoming { get; } = new ConcurrentQueue<string>();
        public ConcurrentQueue<string> Events { get; } = new ConcurrentQueue<string>();
        public ConcurrentQueue<string> Voice { get; } = new ConcurrentQueue<string>();
        public bool Connected => connected;
        public bool IsHost { get; }
        public bool HasLobby => lobby.HasValue;
        public ulong LobbyId => lobby.HasValue ? lobby.Value.Id.Value : 0;
        public string PartnerName => connected ? new Friend(peer).Name : "";
        readonly byte[] buffer = new byte[65536];
        readonly Action<SteamId> sessionRequest;
        readonly Action<SteamId, P2PSessionError> connectionFailed;
        Lobby? lobby;
        SteamId peer;
        bool connected, disposed;
        float lastReceive;
        string latestPose, latestWorld;

        public static bool Available
        {
            get { try { return SteamClient.IsValid && SteamClient.IsLoggedOn; } catch { return false; } }
        }

        public static string LocalName
        {
            get { try { return Available ? SteamClient.Name : ""; } catch { return ""; } }
        }

        SteamTransport(bool host)
        {
            IsHost = host;
            sessionRequest = SessionRequest;
            connectionFailed = (id, error) => { if (connected && id == peer) Drop("Steam connection failed: " + error); };
            SteamNetworking.OnP2PSessionRequest = sessionRequest;
            SteamNetworking.OnP2PConnectionFailed = connectionFailed;
            SteamMatchmaking.OnLobbyMemberLeave += MemberGone;
            SteamMatchmaking.OnLobbyMemberDisconnected += MemberGone;
            SteamNetworking.AllowP2PPacketRelay(true);
        }

        public static SteamTransport Host()
        {
            var transport = new SteamTransport(true);
            transport.CreateLobby();
            return transport;
        }

        public static SteamTransport Join(SteamId lobbyId)
        {
            var transport = new SteamTransport(false);
            transport.JoinLobby(lobbyId);
            return transport;
        }

        async void CreateLobby()
        {
            try
            {
                Events.Enqueue("Creating Steam lobby");
                var created = await SteamMatchmaking.CreateLobbyAsync(2);
                if (disposed) { created?.Leave(); return; }
                if (!created.HasValue) { Events.Enqueue("Steam lobby creation failed"); return; }
                var value = created.Value;
                value.SetFriendsOnly();
                value.SetJoinable(true);
                value.SetData(LobbyKey, "1");
                lobby = value;
                SteamFriends.SetRichPresence("connect", "+connect_lobby " + value.Id.Value);
                Events.Enqueue("Steam lobby ready: invite a friend");
            }
            catch (Exception ex) { Events.Enqueue("Steam lobby error: " + ex.Message); }
        }

        async void JoinLobby(SteamId lobbyId)
        {
            try
            {
                Events.Enqueue("Joining Steam lobby");
                var joined = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
                if (disposed) { joined?.Leave(); return; }
                if (!joined.HasValue) { Events.Enqueue("Could not join Steam lobby"); return; }
                var value = joined.Value;
                lobby = value;
                if (value.GetData(LobbyKey) != "1") { Events.Enqueue("That lobby is not an Ironbark Coop session"); return; }
                var owner = value.Owner.Id;
                if (owner == SteamClient.SteamId) { Events.Enqueue("Cannot join your own lobby"); return; }
                // Sending to the owner opens the P2P session; the host accepts only lobby members.
                peer = owner;
                connected = true;
                lastReceive = Time.unscaledTime;
                Events.Enqueue("Connected");
            }
            catch (Exception ex) { Events.Enqueue("Steam join error: " + ex.Message); }
        }

        public void Invite()
        {
            if (lobby.HasValue) SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
        }

        bool IsMember(SteamId id)
        {
            if (!lobby.HasValue) return false;
            foreach (var member in lobby.Value.Members) if (member.Id == id) return true;
            return false;
        }

        void SessionRequest(SteamId id)
        {
            if (disposed) return;
            if (!IsHost) { if (connected && id == peer) SteamNetworking.AcceptP2PSessionWithUser(id); return; }
            if (!IsMember(id) || (connected && id != peer)) return;
            SteamNetworking.AcceptP2PSessionWithUser(id);
        }

        void MemberGone(Lobby changed, Friend member)
        {
            if (!lobby.HasValue || changed.Id != lobby.Value.Id || !connected || member.Id != peer) return;
            Drop("Partner left the Steam lobby");
        }

        void Drop(string reason)
        {
            if (!connected) return;
            connected = false;
            SteamNetworking.CloseP2PSessionWithUser(peer);
            if (IsHost) peer = default;
            latestPose = latestWorld = null;
            while (Incoming.TryDequeue(out _)) { }
            while (Voice.TryDequeue(out _)) { }
            Events.Enqueue("Disconnected: " + reason);
        }

        public void Pump()
        {
            if (disposed) return;
            if (connected && Time.unscaledTime - lastReceive > 15f) Drop("timed out");
            for (int channel = 0; channel <= VoiceChannel; channel++)
            {
                for (int i = 0; i < 256 && SteamNetworking.IsP2PPacketAvailable(channel); i++)
                {
                    uint size = 0;
                    SteamId from = default;
                    if (!SteamNetworking.ReadP2PPacket(buffer, ref size, ref from, channel)) break;
                    Accept(from, size);
                }
            }
        }

        void Accept(SteamId from, uint size)
        {
            if (!connected)
            {
                // A host takes the first lobby member that talks to it as its partner.
                if (!IsHost || !IsMember(from)) return;
                peer = from;
                connected = true;
                Events.Enqueue("Connected");
            }
            if (from != peer || size == 0 || size > buffer.Length) return;
            lastReceive = Time.unscaledTime;
            string json = Encoding.UTF8.GetString(buffer, 0, (int)size);
            if (PacketKinds.IsPose(json)) latestPose = json;
            else if (PacketKinds.IsWorld(json)) latestWorld = json;
            else if (PacketKinds.IsVoice(json))
            {
                while (Voice.Count >= 8) Voice.TryDequeue(out _);
                Voice.Enqueue(json);
            }
            else if (Incoming.Count >= 128) Drop("receive queue overflow");
            else Incoming.Enqueue(json);
        }

        public string TakePose() { var value = latestPose; latestPose = null; return value; }
        public string TakeWorld() { var value = latestWorld; latestWorld = null; return value; }

        public void Send(string value)
        {
            if (!connected || disposed) return;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (PacketKinds.IsVoice(value))
            {
                if (bytes.Length <= UnreliableLimit) SteamNetworking.SendP2PPacket(peer, bytes, bytes.Length, VoiceChannel, P2PSend.UnreliableNoDelay);
                return;
            }
            if (PacketKinds.IsPose(value) && bytes.Length <= UnreliableLimit)
            {
                SteamNetworking.SendP2PPacket(peer, bytes, bytes.Length, PoseChannel, P2PSend.UnreliableNoDelay);
                return;
            }
            if (bytes.Length > buffer.Length) { Drop("message too large"); return; }
            if (!SteamNetworking.SendP2PPacket(peer, bytes, bytes.Length, ControlChannel, P2PSend.Reliable))
                Drop("Steam send failed");
        }

        public void Dispose()
        {
            if (disposed) return;
            if (connected) SteamNetworking.CloseP2PSessionWithUser(peer);
            connected = false;
            disposed = true;
            if (SteamNetworking.OnP2PSessionRequest == sessionRequest) SteamNetworking.OnP2PSessionRequest = null;
            if (SteamNetworking.OnP2PConnectionFailed == connectionFailed) SteamNetworking.OnP2PConnectionFailed = null;
            SteamMatchmaking.OnLobbyMemberLeave -= MemberGone;
            SteamMatchmaking.OnLobbyMemberDisconnected -= MemberGone;
            try { SteamFriends.ClearRichPresence(); } catch { }
            lobby?.Leave();
            lobby = null;
        }
    }
}
