using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    [Serializable]
    public sealed class Packet
    {
        public string kind, scene, code, build;
        public const int Protocol = 8;
        public int version = Protocol;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 remotePosition;
        public bool active;
        public WorldState world;
        public string audio, name;
        public float pitch, time;
        public bool crouch, light, torch, sit, driving;
        public Vector3 rvPos, rvVel;
        public Quaternion rvRot;
        public float steer;
        public string character;
        // How the player looks (ScRuntime look id); the story role stays in character.
        public string look;
        public int voiceSent, voiceReceived;
        public long voiceConsumed;
        public double voiceEnergy;
        public double voiceOutputEnergy;
        public bool voiceOccluded;
        public float partnerSpeed, partnerJitter;
        public string steam;
        public string[] changes;
        public int arg;
        public int storySent, storyApplied, storyTracked, items, respawns, voiceConcealed, voiceLate;
        public string storySeq, held, duty, addresses, statusText, code2, net;
        // Where a seated player sits: "rv-d", "rv-p", "booth<i>" or "cam" (any other seat the game puts the camera in).
        public string seat;
        public Quaternion rvWheel;
        // The driver's head bouncing on its spring in the cab (RV space): the partner riding along feels it too.
        public Vector3 shake;
        // The cab's own quick pitch and roll on its suspension (degrees, x pitch, z roll).
        public Vector3 shakeTilt;
        public float rvIdle;
        public bool rvAccel, horn;
        public int bits;
        // Seated partner: where their eyes are (the avatar's head is put there) and what their hand holds (scene path).
        public Vector3 eye;
        public string hand;
    }

    // The story sets the next night when a level ends (StartSeq; the campfire sets night 4): remembered by the mod and
    // sent to the partner, so both towers open on that night.
    [HarmonyPatch(typeof(PlayerPrefs), nameof(PlayerPrefs.SetInt))]
    internal static class StartSeqPatch
    {
        internal static bool Applying;
        static void Postfix(string key, int value)
        {
            if (Applying || key != "StartSeq" || value <= 1 || value > 8) return;
            Plugin.NextNight = value;
            // Our own story has moved on: a night remembered from following the partner earlier is out of date.
            Plugin.PendingNight = 0;
            Plugin.Log("NEXT_NIGHT " + value);
            if (Plugin.Instance && Plugin.Instance.Ready) Plugin.Instance.Send(new Packet { kind = "next-night", arg = value });
        }
    }

    // Applies the partner's night right before the tower reads it (PlayerPrefs may be rewritten while loading).
    [HarmonyPatch(typeof(WatchTowerManager), "Start")]
    internal static class PartnerNightPatch
    {
        static int applied;
        static void Prefix()
        {
            applied = Plugin.PendingNight;
            string from = "NIGHT_FROM_PARTNER ";
            // Otherwise the night the story set at the end of the last level (in either world: the player who put out
            // the campfire set it only in their own game, and two copies on one PC share the setting the tower clears).
            if (applied == 0) { applied = Plugin.NextNight; from = "NIGHT_FROM_STORY "; }
            Plugin.NextNight = 0;
            Plugin.PendingNight = 0;
            if (applied == 0) return;
            StartSeqPatch.Applying = true;
            PlayerPrefs.SetInt("StartSeq", applied);
            StartSeqPatch.Applying = false;
            Plugin.Log(from + applied);
        }

        // Nights 2 and 7 have no Start branch (the game only reaches them by sleeping); run their intro like FadeToSleep does.
        static void Postfix(WatchTowerManager __instance)
        {
            if (applied != 2 && applied != 7) return;
            var ui = GameManager.instance.uIManager;
            __instance.seq = applied == 2 ? WatchTowerManager.Seq.Seq2 : WatchTowerManager.Seq.Seq7;
            ui.StartCoroutine(applied == 2 ? ui.FadeInWithIntro2() : ui.FadeInWithIntro7());
            applied = 0;
        }
    }

    [BepInPlugin("astra.ironbark.coop", "Ironbark Cooperative", "0.7.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal StorySync Story;
        internal ItemSync Items;
        internal PersonalMoments Moments;
        internal CoopDuties Duties;
        internal Vehicles Rides;
        internal DinerSync Diner;
        internal MusicSync Music;
        internal TrailSync Trail;
        internal TowerSync Tower;
        internal HandEffects Hands;
        internal RadioTalk Talk;
        internal BedSync Beds;
        internal DreamSync Dream;
        internal TripSync Trip;
        internal NightErrand Errand;
        internal SleepSync Sleep;
        bool partnerSit;
        int sceneEpoch;
        internal bool Ready => authenticated;
        internal bool IsHost => host;
        internal bool PartnerPresent => avatar != null && avatar.Active;
        internal Vector3 PartnerPosition => targetPosition;
        internal bool Paused => GameManager.instance && GameManager.instance.isPaused;
        // Read/write surface for the in-menu multiplayer screen.
        internal string Status => status;
        internal bool Connecting => transport != null && !authenticated;
        internal bool Online => transport != null;
        internal string PartnerName => authenticated ? partnerName : null;
        internal string MyName => LocalName;
        internal string HostAddress { get => address; set => address = value; }
        internal bool UsingSteam => transport is SteamTransport;
        internal bool SteamHostWithLobby => transport is SteamTransport s && s.IsHost && s.HasLobby;
        internal bool LocalTalking => capture != null && capture.Transmitting;
        internal bool PartnerTalking => voice != null && voice.Speaking;
        internal string NetInfo => transport is DirectTransport d && authenticated ? (d.UdpActive ? "UDP" : "TCP") + (d.RttMs >= 0 ? " " + d.RttMs + " ms" : "") : "";
        internal void HostDirect() => Connect(true);
        internal void JoinDirect() => Connect(false);
        internal void HostSteam() => ConnectSteam(0);
        internal void InviteSteam() { if (transport is SteamTransport s) s.Invite(); }
        internal void Leave() => Disconnect();
        internal bool UpnpEnabled { get => upnpEnabled; set { upnpEnabled = value; Config.Bind("Network", "UPnP", false).Value = value; } }
        internal string UpnpState => upnp.State;
        // The code a host gives to a friend: router's external address when UPnP opened the port, else the best local one.
        internal string HostCode
        {
            get
            {
                string ip = upnp.State == "open" && !string.IsNullOrEmpty(upnp.ExternalAddress) ? upnp.ExternalAddress : ConnectionPanel.BestShareAddress();
                return ip != null && System.Net.IPAddress.TryParse(ip, out var parsed) ? JoinCode.Encode(parsed, Port) : null;
            }
        }
        VoiceMode voiceMode;
        KeyCode talkKey;
        float voiceThreshold, voiceVolume;
        string microphoneName;
        Harmony harmony;
        ITransport transport;
        readonly Upnp upnp = new Upnp();
        bool upnpEnabled;
        ulong pendingLobby;
        VoicePlayback voice;
        VoiceCapture capture;
        float voiceRetryAt;
        bool host, authenticated, menu = true;
        string address = "127.0.0.1", code = "", status = "Offline";
        const int Port = 25061;
        string build;
        PartnerAvatar avatar;
        string playerName, partnerName = "Partner", survivorSetting, partnerCharacter, lookSetting, partnerLook;
        bool partnerCrouch, partnerLight;
        float partnerPitch;
        static readonly System.Reflection.FieldInfo CrouchField = AccessTools.Field(typeof(FirstPersonController), "isCrouching");
        Vector3 targetPosition;
        Quaternion targetRotation;
        float nextPose, nextStatus, nextBridge, nextSceneSync;
        string bridgeDir;
        string loadingScene;
        Vector3? labWalkTarget;
        float labWalkUntil, labPushUntil;
        int labPushSpeed;
        bool towerStarted;
        internal static int PendingNight;
        // The night the story set for the next tower load (either world), until the tower reads it.
        internal static int NextNight;

        void Awake()
        {
            Instance = this;
            Story = new StorySync(this);
            Items = new ItemSync(this);
            Moments = new PersonalMoments(this);
            Duties = new CoopDuties(this);
            Rides = new Vehicles(this);
            Diner = new DinerSync(this);
            Music = new MusicSync(this);
            Trail = new TrailSync(this);
            Tower = new TowerSync(this);
            Hands = new HandEffects(this);
            Talk = new RadioTalk(this);
            Beds = new BedSync(this);
            Dream = new DreamSync(this);
            Trip = new TripSync(this);
            Errand = new NightErrand(this);
            Sleep = new SleepSync(this);
            Items.RemoteHeldChanged += held => RefreshHeld();
            harmony = new Harmony("astra.ironbark.coop");
            // One patch that no longer fits the game must not take the rest of the mod down with it.
            foreach (var type in typeof(Plugin).Assembly.GetTypes())
            {
                if (!type.IsDefined(typeof(HarmonyPatch), false)) continue;
                try { harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception ex) { Logger.LogError("PATCH_FAILED " + type.Name + ": " + (ex.InnerException ?? ex).Message); }
            }
            Application.runInBackground = true;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                build = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(GameManager).Assembly.Location))).Replace("-", "");
            code = Config.Bind("Session", "Code", "ironbark", "Use the same private session code on both players.").Value;
            address = Config.Bind("Session", "Address", "127.0.0.1", "Host address on LAN or private VPN.").Value;
            voiceMode = Config.Bind("Voice", "Mode", VoiceMode.PushToTalk, "PushToTalk, VoiceActivation or Off.").Value;
            talkKey = Config.Bind("Voice", "PushToTalkKey", KeyCode.V, "Hold to talk in PushToTalk mode.").Value;
            voiceThreshold = Config.Bind("Voice", "ActivationThreshold", 0.02f, "Microphone RMS level that opens voice activation.").Value;
            voiceVolume = Config.Bind("Voice", "PartnerVolume", 1f, "Partner voice volume, 0..1.").Value;
            microphoneName = Config.Bind("Voice", "Microphone", "", "Device name; empty uses the system default.").Value;
            playerName = Config.Bind("Session", "PlayerName", "", "Name shown above your character; empty uses your Steam name.").Value;
            // Steam passes +connect_lobby <id> when a friend accepts an invite before the game was running.
            ulong.TryParse(Arg("+connect_lobby"), out pendingLobby);
            SteamFriends.OnGameLobbyJoinRequested += LobbyJoinRequested;
            survivorSetting = Config.Bind("Character", "Survivor", "auto", "marcus, tessa or auto (host Marcus, guest Tessa). Uses your own Scratch Creek install if present.").Value;
            lookSetting = Config.Bind("Character", "Look", "auto", "How you look to your partner: auto (by role), marcus, tessa (Scratch Creek), ib-ranger, ib-waitress, ib-suitguy (Ironbark's own people). Chosen in the Multiplayer menu.").Value;
            ScRuntime.Begin(Config.Bind("Character", "ScratchCreekPath", "", "Scratch Creek data folder if it is not found automatically; none = do not use Scratch Creek.").Value);
            upnpEnabled = Config.Bind("Network", "UPnP", false, "Experimental: ask the router to open port 25061 (TCP+UDP) while hosting.").Value;
            bridgeDir = Arg("-coop-bridge");
            SceneManager.sceneLoaded += SceneLoaded;
            Logger.LogInfo("READY build=" + build);
            string role = Arg("-coop-role");
            if (role == "host" || role == "client") Connect(role == "host");
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return "";
        }

        void Connect(bool asHost)
        {
            Disconnect();
            host = asHost;
            var tcp = new DirectTransport();
            transport = tcp;
            try
            {
                if (host)
                {
                    tcp.Host(Port);
                    if (upnpEnabled) upnp.Open(Port, true);
                }
                else if (JoinCode.TryDecode(address, out var codeAddress, out var codePort) && JoinCode.LooksLikeCode(address)) tcp.Join(codeAddress, codePort);
                else tcp.Join(address.Trim(), Port);
                status = host ? "Waiting for partner" : "Connecting";
            }
            catch (Exception ex) { status = ex.Message; Disconnect(); }
        }

        void LobbyJoinRequested(Steamworks.Data.Lobby lobby, Steamworks.SteamId friend) => pendingLobby = lobby.Id.Value;

        void ConnectSteam(ulong lobbyId)
        {
            Disconnect();
            if (!SteamTransport.Available) { status = "Steam is not running or not logged in"; return; }
            host = lobbyId == 0;
            try { transport = host ? SteamTransport.Host() : SteamTransport.Join(lobbyId); }
            catch (Exception ex) { status = "Steam: " + ex.Message; Disconnect(); }
        }

        internal string MyCharacter => survivorSetting == "marcus" || survivorSetting == "tessa" ? survivorSetting : host ? "marcus" : "tessa";
        // NPCs who serve one customer (the diner waitress) run in the lead's world and are mirrored to the partner:
        // the lead is Marcus, or the host when both picked the same character.
        internal bool IsStoryLead => MyCharacter != partnerCharacter && partnerCharacter != null ? MyCharacter == "marcus" : host;
        internal bool FollowsStory => authenticated && !IsStoryLead;
        internal bool LeadsStory => authenticated && IsStoryLead;
        internal void SetCharacter(string id) { survivorSetting = id; Config.Bind("Character", "Survivor", "auto").Value = id; }
        internal string PartnerCharacter => partnerCharacter;
        internal string CharacterSetting => survivorSetting == "marcus" || survivorSetting == "tessa" ? survivorSetting : "auto";
        // The look this player shows: the pick, or by role ("auto"); sent to the partner, who builds it if they have it.
        internal string LookSetting => string.IsNullOrEmpty(lookSetting) ? "auto" : lookSetting;
        internal string MyLook => ScRuntime.ResolveLook(LookSetting, MyCharacter);
        internal string PartnerAppearance => partnerLook;
        internal string PartnerBody => avatar != null ? avatar.Character : null;
        internal void SetLook(string id)
        {
            lookSetting = id;
            Config.Bind("Character", "Look", "auto").Value = id;
            if (authenticated) Send(new Packet { kind = "look", look = LookSetting, character = MyCharacter });
        }

        string LocalName => !string.IsNullOrWhiteSpace(playerName) ? playerName
            : !string.IsNullOrEmpty(SteamTransport.LocalName) ? SteamTransport.LocalName : Environment.UserName;

        void Disconnect()
        {
            upnp.Close();
            capture?.Dispose(); capture = null;
            voice?.Dispose(); voice = null;
            transport?.Dispose(); transport = null; authenticated = false;
            avatar?.Dispose(); avatar = null;
            PartnerLeft();
            status = "Offline";
        }

        internal int LastPoseBytes;
        internal void Send(Packet packet)
        {
            if (transport == null) return;
            string json = WireJson.Write(packet);
            if (packet.kind == "pose") LastPoseBytes = json.Length;
            transport.Send(json);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) menu = !menu;
            if (pendingLobby != 0 && SteamTransport.Available) { ulong lobbyId = pendingLobby; pendingLobby = 0; ConnectSteam(lobbyId); }
            transport?.Pump();
            if (transport != null)
            {
                while (transport.Events.TryDequeue(out var message))
                {
                    Logger.LogInfo(message);
                    // UDP availability is a transport detail shown separately, not a connection status.
                    if (message.StartsWith("UDP ")) continue;
                    status = message;
                    if (message == "Connected")
                    {
                        authenticated = false;
                        Send(new Packet { kind = "hello", code = code, build = build, name = LocalName, character = MyCharacter, look = LookSetting });
                    }
                    else if (message.StartsWith("Disconnected")) { authenticated = false; if (avatar != null) avatar.Active = false; PartnerLeft(); }
                }
                for (int i = 0; i < 64 && transport != null && transport.Incoming.TryDequeue(out var json); i++)
                {
                    try { Receive(WireJson.Read(json)); }
                    catch (Exception ex) { Logger.LogWarning("Packet rejected: " + ex.Message); }
                }
                string pose = transport?.TakePose();
                if (pose != null)
                {
                    try { Receive(WireJson.Read(pose)); }
                    catch (Exception ex) { Logger.LogWarning("Pose rejected: " + ex.Message); }
                }
                string world = transport?.TakeWorld();
                if (world != null)
                {
                    try { Receive(WireJson.Read(world)); }
                    catch (Exception ex) { Logger.LogWarning("World snapshot rejected: " + ex.Message); }
                }
                for (int i = 0; i < 8 && transport != null && transport.Voice.TryDequeue(out var audioJson); i++)
                {
                    if (!authenticated) continue;
                    try { Receive(WireJson.Read(audioJson)); }
                    catch (Exception ex) { Logger.LogWarning("Voice rejected: " + ex.Message); }
                }
            }
            if (authenticated && Time.unscaledTime >= nextPose)
            {
                nextPose = Time.unscaledTime + 0.05f;
                var gm = GameManager.instance;
                bool active = gm && gm.playerFP && gm.playerFP.activeInHierarchy;
                Send(LocalPose(gm, active));
            }
            if (authenticated && Time.unscaledTime >= nextSceneSync)
            {
                nextSceneSync = Time.unscaledTime + 2f;
                SendScene();
            }
            if (avatar != null && avatar.Active) avatar.SetSpeaking(voice != null && voice.Speaking && !partnerDreaming);
            if (labWalkTarget.HasValue && GameManager.instance && GameManager.instance.playerFP)
            {
                // Lab-only scripted walk at the game's walk speed so partner animation can be measured.
                var controller = GameManager.instance.playerFP.GetComponent<CharacterController>();
                var offset = labWalkTarget.Value - GameManager.instance.playerFP.transform.position; offset.y = 0;
                if (offset.magnitude < 0.2f || !controller || Time.unscaledTime > labWalkUntil) labWalkTarget = null;
                else controller.Move(offset.normalized * 3f * Time.deltaTime + Vector3.down * 2f * Time.deltaTime);
            }
            if (!towerStarted && GameManager.instance && GameManager.instance.watchTowerManager && Time.timeSinceLevelLoad > 1f) towerStarted = true;
            if (Time.unscaledTime < labPushUntil && GameManager.instance && GameManager.instance.rVController && GameManager.instance.rVController.rigidbody && !GameManager.instance.rVController.rigidbody.isKinematic)
            {
                // Lab-only: drive the RV forward without keyboard input.
                var rvBody = GameManager.instance.rVController.rigidbody;
                // Only forward speed: the RV body keeps its height itself, and a carried-over vertical speed grew without bound.
                rvBody.velocity = rvBody.transform.forward * labPushSpeed;
            }
            Story.Tick();
            Items.Tick();
            Duties.Tick();
            Rides.Tick();
            Diner.Tick();
            Music.Tick();
            Trail.Tick();
            Tower.Tick();
            Hands.Tick();
            Talk.Tick();
            Beds.Tick();
            Dream.Tick();
            Trip.Tick();
            Errand.Tick();
            Sleep.Tick();
            AnchorSeatedPartner();
            if (authenticated && Time.unscaledTime >= voiceRetryAt)
            {
                try
                {
                    if (capture == null) capture = new VoiceCapture(this);
                    capture.Tick(voiceMode, talkKey, voiceThreshold, microphoneName);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Voice capture paused for 10 seconds: " + ex.Message);
                    capture?.Dispose();
                    capture = null;
                    voiceRetryAt = Time.unscaledTime + 10f;
                }
            }
            else if (capture != null) { capture.Dispose(); capture = null; }
            voice?.SetDream(Dream.Dreaming, Dream.Dreaming ? Dream.HearingAt(targetPosition + Vector3.up * 1.6f) : 1f);
            voice?.SetSleepTalk(partnerDreaming && PartnerPresent);
            voice?.Update(avatar != null ? avatar.HeadPosition : targetPosition + Vector3.up * 1.6f, authenticated && (PartnerPresent || Dream.Dreaming));
            Bridge();
        }

        // After the game's own InteractableObjectUI cleared it: our extra clickables (passenger seat) light the same dot.
        void LateUpdate()
        {
            Rides.Interact();
            if (!Rides.LookingAtSeat) return;
            if (!interactDot) { var ui = FindObjectOfType<InteractableObjectUI>(); if (ui) interactDot = ui.dot; }
            if (interactDot) interactDot.enabled = true;
        }
        UnityEngine.UI.Image interactDot;

        void Receive(Packet p)
        {
            if (p == null) return;
            // The partner picked another look in the menu: their body is rebuilt with the next pose.
            if (p.kind == "look") { partnerLook = p.look; avatar?.Dispose(); avatar = null; Logger.LogInfo("PARTNER_LOOK " + p.look); return; }
            if (p.kind == "next-night") { if (p.arg > 1 && p.arg <= 8) { NextNight = p.arg; Logger.LogInfo("NEXT_NIGHT_FROM_PARTNER " + p.arg); } return; }
            if (p.kind == "hello")
            {
                // Steam lobbies are friends-only, so membership replaces the shared session code there.
                bool codeOk = transport is SteamTransport || p.code == code;
                if (p.version != Packet.Protocol || !codeOk || p.build != build)
                {
                    Disconnect(); status = "Version, game build or session code mismatch"; return;
                }
                authenticated = true; status = "Partner connected";
                partnerCharacter = p.character == "marcus" || p.character == "tessa" ? p.character : (host ? "tessa" : "marcus");
                partnerLook = p.look;
                partnerName = string.IsNullOrWhiteSpace(p.name) ? "Partner" : p.name.Trim().Substring(0, Math.Min(24, p.name.Trim().Length));
                Logger.LogInfo("AUTHENTICATED");
                Story.PartnerConnected();
                Items.PartnerConnected();
                if (host) SendScene(true);
                return;
            }
            if (!authenticated) return;
            if (p.kind == "voice")
            {
                if (p.scene != SceneManager.GetActiveScene().name || !(PartnerPresent || Dream.Dreaming)) return;
                if (voice == null) voice = new VoicePlayback(voiceVolume);
                voice.Receive(p.audio, p.arg);
                return;
            }
            if (p.kind == "story" || p.kind == "story-full") { Story.Receive(p); return; }
            if (p.kind == "transition") { Story.ReceiveTransition(p); return; }
            if (p.kind == "story-request") { Story.FullStateRequested(p); Items.PartnerConnected(); return; }
            if (p.kind == "item") { Items.Receive(p); return; }
            if (p.kind == "duty") { Duties.Receive(p); return; }
            if (p.kind == "call") { RemoteCalls.Receive(p); return; }
            if (p.kind == "npc" && p.code == "ranger") { Trail.Receive(p); return; }
            if (p.kind == "npc" || p.kind == "diner") { Diner.Receive(p); return; }
            if (p.kind == "music") { Music.Receive(p); return; }
            if (p.kind == "trail") { Trail.ReceiveEvent(p); return; }
            if (p.kind == "tower" || p.kind == "screen") { Tower.Receive(p); return; }
            if (p.kind == "fx") { Hands.Receive(p); return; }
            if (p.kind == "dlg") { Talk.Receive(p); return; }
            if (p.kind == "potty") { OuthouseSync.Receive(p); return; }
            if (p.kind == "trip") { Trip.Receive(p); return; }
            if (p.kind == "supper") { SupperSync.Receive(p); return; }
            if (p.kind == "errand") { Errand.Receive(p); return; }
            if (p.kind == "react") { if (p.scene == SceneManager.GetActiveScene().name && avatar != null && avatar.Active) { avatar.React(); Logger.LogInfo("SCARE_REMOTE " + p.code); } return; }
            if (p.kind == "scene")
            {
                // Whoever advanced to a newer story scene leads; on connect the host's scene wins.
                // Staying in the tower while the partner is at the campfire: their scene is not followed there.
                if (Trip.HoldsScene(p.scene)) { sceneEpoch = Math.Max(sceneEpoch, p.arg); return; }
                bool newer = p.arg > sceneEpoch || p.code == "force";
                if (!newer) return;
                sceneEpoch = p.arg;
                // Rejoining in the same tower but on a different night: reload into the host's night.
                bool otherNight = p.code == "force" && !host && p.scene == "Watchtower" && p.scene == SceneManager.GetActiveScene().name
                    && int.TryParse(p.name, out int hostNight) && towerStarted && hostNight != CurrentNight();
                if (otherNight) Logger.LogInfo("NIGHT_MISMATCH reloading into night " + p.name);
                if ((p.scene != SceneManager.GetActiveScene().name || otherNight) && loadingScene != p.scene && Application.CanStreamedLevelBeLoaded(p.scene))
                {
                    loadingScene = p.scene;
                    // Join the partner's night, the same way the main menu's Continue does.
                    // The night matters for the tower (and the evening card before it). Followed elsewhere (the campfire),
                    // the partner reports whatever their tower left in the save (night 1), which must not be applied later.
                    if (p.scene != "Watchtower" && p.scene != "Seq 4 Intro") PendingNight = 0;
                    else if (int.TryParse(p.name, out int night) && night >= 1 && night <= 8) { StartSeqPatch.Applying = true; PlayerPrefs.SetInt("StartSeq", night); StartSeqPatch.Applying = false; PendingNight = night; }
                    PlayerPrefs.SetInt("FromMenu", 1);
                    Logger.LogInfo("SCENE_FOLLOW " + p.scene + " night=" + p.name + " epoch=" + p.arg + " StartSeq=" + PlayerPrefs.GetInt("StartSeq", -1));
                    SceneManager.LoadSceneAsync(p.scene);
                }
                return;
            }
            if (p.kind != "pose") return;
            // The partner is in another scene (one went to the campfire alone): not here, seen or heard.
            if (p.scene != SceneManager.GetActiveScene().name) { if (avatar != null && avatar.Active) { avatar.Active = false; partnerSeat = null; Logger.LogInfo("PARTNER_ELSEWHERE " + p.scene); } return; }
            if (!Finite(p.position.x) || !Finite(p.position.y) || !Finite(p.position.z)) return;
            if (!Finite(p.rotation.x) || !Finite(p.rotation.y) || !Finite(p.rotation.z) || !Finite(p.rotation.w)) return;
            if (!Finite(p.pitch)) return;
            if (avatar == null) { avatar = new PartnerAvatar(partnerName, partnerCharacter, partnerLook); heldShown = null; Logger.LogInfo("REMOTE_AVATAR_CREATED " + avatar.Character); }
            // The dreamer does not see the partner (only hears her, far away).
            avatar.Active = p.active && !Dream.Dreaming;
            targetPosition = p.position;
            targetRotation = p.rotation.normalized;
            partnerPitch = Mathf.Clamp(p.pitch, -90f, 90f); partnerCrouch = p.crouch; partnerLight = p.light;
            Rides.ReceivePose(p);
            partnerSit = p.sit; partnerSeat = p.active ? p.seat : null; partnerBodyYaw = p.steer; partnerEye = p.eye;
            partnerDreaming = p.code2 == "dream";
            if (partnerHand != p.hand || !heldShown) { partnerHand = p.hand; RefreshHeld(); }
            Diner.PartnerSeatChanged(partnerSeat);
            avatar.SetPose(targetPosition, targetRotation, partnerPitch, partnerCrouch, partnerLight, Finite(p.time) ? p.time : 0f, p.torch, p.sit);
            avatar.Lying = p.active && p.seat == "bed";
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) < 1000000;

        // Feet position from the capsule so crouching and standing both put the body on the ground.
        Packet LocalPose(GameManager gm, bool active)
        {
            var packet = new Packet { kind = "pose", scene = SceneManager.GetActiveScene().name, active = active, rotation = Quaternion.identity, time = Time.unscaledTime };
            if (!gm || !gm.playerFP || Rides.Describe(packet)) return packet;
            if (Dream.DescribeAsleep(packet)) return packet;
            // The game hides the walking body while seated (diner booth, computer, bed...): show the partner sitting there.
            packet.hand = Hands.LocalHandPath();
            if (!active) { Seats.DescribeInactive(packet, lastFeet, hasLastFeet); return packet; }
            var player = gm.playerFP.transform;
            var controller = gm.playerFP.GetComponent<CharacterController>();
            packet.position = controller
                ? player.position + Vector3.up * (controller.center.y - controller.height * 0.5f)
                : player.position - Vector3.up;
            packet.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            var fps = gm.firstPersonController;
            if (fps)
            {
                packet.pitch = fps.rotationX;
                packet.crouch = CrouchField != null && (bool)CrouchField.GetValue(fps);
            }
            var items = gm.itemPickUp;
            packet.light = items && items.inHandFlashlight && items.flashLight && items.flashLight.isOn;
            packet.torch = items && items.inHandFlashlight;
            if (Rides.Seated && gm.rVController)
            {
                packet.sit = true;
                packet.seat = "rv-p";
                packet.position = Vehicles.PassengerSeat(gm.rVController);
                packet.eye = Vehicles.PassengerEye(gm.rVController);
                var view = fps ? fps.playerCamera.transform : Camera.main ? Camera.main.transform : player;
                packet.rotation = Quaternion.Euler(0f, view.eulerAngles.y, 0f);
                packet.pitch = Mathf.DeltaAngle(0f, view.eulerAngles.x);
                return packet;
            }
            lastFeet = packet.position; hasLastFeet = true;
            // The desk chair is claimed from the click on, before the fade puts us on it, so two clicks at once are
            // compared by when they happened.
            if (TowerSync.LocalChair()) packet.seat = "tower-chair";
            return packet;
        }

        Vector3 lastFeet;
        bool hasLastFeet, partnerDreaming;
        internal bool PartnerDreaming => authenticated && partnerDreaming;
        internal float VoiceGain => voice != null ? voice.Gain : -1f;
        internal GameObject PartnerRoot => avatar != null ? avatar.Root : null;
        internal float PartnerHipsGap => avatar != null && avatar.Active ? avatar.LyingHipsGap : -1f;
        float partnerBodyYaw;
        string partnerSeat, partnerHand;
        Vector3 partnerEye;
        internal string PartnerSeat => authenticated ? partnerSeat : null;

        // The partner is gone: every seat they held is free again and nothing is left in their hands.
        void PartnerLeft()
        {
            partnerSeat = null; partnerSit = false; partnerHand = null; heldShown = null;
            Diner?.PartnerSeatChanged(null);
            Talk?.PartnerLeft();
            Trip?.PartnerLeft();
            Errand?.PartnerLeft();
        }

        // What the partner's avatar holds: a shared story item they took, or whatever their hand holds by scene path
        // (personal items such as the tower's bear spray exist in both worlds under each player's own body).
        GameObject heldShown;
        void RefreshHeld()
        {
            if (avatar == null) return;
            var held = Items.RemoteHeldObject;
            if (!held && partnerHand == HandEffects.WoodInHand && GameManager.instance && GameManager.instance.itemPickUp) held = GameManager.instance.itemPickUp.woodPilePrefab;
            else if (!held && !string.IsNullOrEmpty(partnerHand)) { var found = ItemSync.FindPath(partnerHand); if (found) held = found.gameObject; }
            if (held == heldShown) return;
            heldShown = held;
            avatar.SetHeldItem(held);
        }
        internal GameObject PartnerHeldVisual => avatar != null ? avatar.HeldVisual : null;
        internal Quaternion PartnerLook => avatar != null ? avatar.Look : Quaternion.identity;
        internal string PartnerTorch => avatar != null ? avatar.TorchInfo : "-";
        internal bool PartnerLying => avatar != null && avatar.Active && avatar.Lying;
        internal bool Lab => !string.IsNullOrEmpty(bridgeDir);
        // Lab evidence files next to state.json (nothing is written without -coop-bridge).
        internal void LabWrite(string name, byte[] data) { if (!string.IsNullOrEmpty(bridgeDir)) try { File.WriteAllBytes(Path.Combine(bridgeDir, name), data); } catch { } }
        // Lab: how far the seated avatar's head is from the partner's eye point (about 0.09 m when placed right).
        // RV seats are measured against the eye point in our copy of the RV (theirs may lag a few frames behind ours).
        internal float PartnerHeadToEye => avatar != null && avatar.Active && avatar.HeadToSeatEye >= 0f ? avatar.HeadToSeatEye
            : avatar != null && avatar.Active && partnerSit && partnerEye != Vector3.zero ? Vector3.Distance(avatar.HeadPosition, partnerEye) : -1f;

        // A seated partner is pinned to that seat in our world: RV seats follow our copy of the RV, other seats stay put.
        void AnchorSeatedPartner()
        {
            if (avatar == null) return;
            var rv = GameManager.instance ? GameManager.instance.rVController : null;
            avatar.SetInCab(rv && avatar.Active && (partnerSeat == "rv-d" || partnerSeat == "rv-p"
                || (rv.boundsCollider && rv.boundsCollider.bounds.Contains(avatar.Root.transform.position + Vector3.up))));
            if (!partnerSit || string.IsNullOrEmpty(partnerSeat)) return;
            if (partnerSeat == "rv-d" || partnerSeat == "rv-p")
            {
                if (!rv) return;
                bool driver = partnerSeat == "rv-d";
                avatar.Anchor(driver ? Vehicles.DriverSeat(rv) : Vehicles.PassengerSeat(rv), Quaternion.Euler(0f, rv.transform.eulerAngles.y, 0f), driver ? rv.steeringHandle : null,
                    driver ? Vehicles.DriverEye(rv) : Vehicles.PassengerEye(rv));
                return;
            }
            avatar.Anchor(targetPosition, Quaternion.Euler(0f, partnerBodyYaw, 0f), null, partnerEye != Vector3.zero ? partnerEye : (Vector3?)null);
        }

        internal static void Log(string message) => Instance?.Logger.LogInfo(message);
        internal static void LogFromThread(string message) { var plugin = Instance; plugin?.Logger.LogInfo(message); }
        static readonly string[] StoryScenes = { "EP4 Intro", "First Scene", "Trail Start", "Watchtower", "Seq 4 Intro", "Camping", "Trail End", "Credits Scene" };
        void SendScene(bool force = false)
        {
            string name = SceneManager.GetActiveScene().name;
            if (Array.IndexOf(StoryScenes, name) < 0) return;
            Send(new Packet { kind = "scene", scene = name, arg = sceneEpoch, code = force ? "force" : null, name = CurrentNight().ToString() });
        }

        // Night the tower runs: before the manager's Start it is still the save's StartSeq, afterwards its seq.
        int CurrentNight()
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            if (tower && towerStarted) return (int)tower.seq + 1;
            return NextNight > 0 ? NextNight : PlayerPrefs.GetInt("StartSeq", 1);
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            // Back in the menu a chapter is picked afresh (its button sets the night).
            if (scene.name == "Main Menu") NextNight = 0;
            bool fromPartner = loadingScene == scene.name;
            loadingScene = null;
            avatar?.Dispose(); avatar = null;
            PartnerAvatar.CaptureTemplate();
            if (scene.name == "Main Menu") StartCoroutine(MultiplayerMenu.InstallWhenReady());
            towerStarted = false;
            if (!fromPartner && Array.IndexOf(StoryScenes, scene.name) >= 0) sceneEpoch++;
            // Side-by-side testing on one PC (-coop-window WxH): the game re-applies its saved full-screen setting
            // when menus load, so the requested window is restored after every scene.
            if (Arg("-coop-window") is string window && window.Contains("x")
                && int.TryParse(window.Split('x')[0], out int windowWidth) && int.TryParse(window.Split('x')[1], out int windowHeight))
                StartCoroutine(KeepWindowed(windowWidth, windowHeight));
            Story.SceneLoaded();
            Items.SceneLoaded();
            Duties.SceneLoaded();
            Rides.SceneLoaded();
            Diner.SceneLoaded();
            Music.SceneLoaded();
            Trail.SceneLoaded();
            Tower.SceneLoaded();
            Hands.SceneLoaded();
            Talk.SceneLoaded();
            Beds.SceneLoaded();
            Dream.SceneLoaded();
            Trip.SceneLoaded();
            Errand.SceneLoaded();
            SupperSync.SceneLoaded();
            Sleep.SceneLoaded();
            hasLastFeet = false;
            if (authenticated) SendScene();
            Logger.LogInfo("SCENE " + scene.name);
        }

        System.Collections.IEnumerator KeepWindowed(int width, int height)
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSecondsRealtime(1f);
                if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width != width || Screen.height != height)
                    Screen.SetResolution(width, height, FullScreenMode.Windowed);
                var at = Arg("-coop-window-pos").Split(',');
                if (at.Length == 2 && int.TryParse(at[0], out int x) && int.TryParse(at[1], out int y)) WindowPlacer.Place(x, y);
            }
        }

        // Explicit lab flag only: file commands stay local and execute on Unity's main thread.
        void Bridge()
        {
            if (string.IsNullOrEmpty(bridgeDir) || Time.unscaledTime < nextBridge) return;
            nextBridge = Time.unscaledTime + 0.25f;
            Directory.CreateDirectory(bridgeDir);
            string path = Path.Combine(bridgeDir, "command.json");
            if (File.Exists(path))
            {
                try
                {
                    var p = WireJson.Read(File.ReadAllText(path));
                    File.Delete(path);
                    if (p.kind == "scene" && Application.CanStreamedLevelBeLoaded(p.scene)) SceneManager.LoadScene(p.scene);
                    if (p.kind == "capture") ScreenCapture.CaptureScreenshot(Path.Combine(bridgeDir, "capture.png"));
                    if (p.kind == "capture-camera") CaptureCamera(p.arg == 1);
                    if (p.kind == "quit") Application.Quit();
                    if (p.kind == "look-at") LookAt(p.position);
                    if (p.kind == "walk") { labWalkTarget = p.position; labWalkUntil = Time.unscaledTime + 6f; }
                    if (p.kind == "die" && GameManager.instance) GameManager.instance.Death();
                    if (p.kind == "tower-coroutine" && GameManager.instance && GameManager.instance.watchTowerManager)
                    {
                        var tower = GameManager.instance.watchTowerManager;
                        var method = AccessTools.Method(typeof(WatchTowerManager), p.code);
                        if (method != null && method.ReturnType == typeof(System.Collections.IEnumerator)) tower.StartCoroutine((System.Collections.IEnumerator)method.Invoke(tower, new object[0]));
                        else if (method != null && method.GetParameters().Length == 0) method.Invoke(tower, new object[0]);
                    }
                    if (p.kind == "scare") Moments.LocalScare("lab");
                    if (p.kind == "duty") Duties.Force(p.code);
                    if (p.kind == "prefs" && !string.IsNullOrEmpty(p.code)) PlayerPrefs.SetInt(p.code, p.arg);
                    if (p.kind == "disconnect") Disconnect();
                    if (p.kind == "net-drop") DirectTransport.LabDropPercent = Mathf.Clamp(p.arg, 0, 100);
                    if (p.kind == "join") Connect(false);
                    if (p.kind == "tower-positions" && GameManager.instance && GameManager.instance.watchTowerManager)
                    {
                        var tower = GameManager.instance.watchTowerManager;
                        var positions = new Newtonsoft.Json.Linq.JObject();
                        foreach (var name in new[] { "generator", "woodStorage", "stoveDoor", "woodStovePosition", "bed", "door", "lightSwitchShedCollider", "table", "chair", "radioCollider", "computer", "woodCheck" })
                        {
                            var value = AccessTools.Field(typeof(WatchTowerManager), name)?.GetValue(tower);
                            var target = value is GameObject go ? go.transform : value is Component component ? component.transform : null;
                            if (target) positions[name] = new Newtonsoft.Json.Linq.JObject { ["x"] = target.position.x, ["y"] = target.position.y, ["z"] = target.position.z };
                            // Where a click lands on it: the centre of its collider (a door's position is its hinge).
                            if (target && target.TryGetComponent<Collider>(out var hitBox)) positions[name + "Hit"] = new Newtonsoft.Json.Linq.JObject { ["x"] = hitBox.bounds.center.x, ["y"] = hitBox.bounds.center.y, ["z"] = hitBox.bounds.center.z };
                        }
                        File.WriteAllText(Path.Combine(bridgeDir, "positions.json"), positions.ToString());
                    }
                    if (p.kind == "item-list") Items.WriteList(Path.Combine(bridgeDir, "items.json"));
                    if (p.kind == "torch" && GameManager.instance && GameManager.instance.itemPickUp && GameManager.instance.itemPickUp.flashLight) { if (p.active) GameManager.instance.itemPickUp.flashLight.TurnFlashlightOn(); else GameManager.instance.itemPickUp.flashLight.TurnFlashlightOff(); }
                    if (p.kind == "crouch") HoldCrouchPatch.LabHeld = p.active;
                    if (p.kind == "rv-push") { labPushUntil = Time.unscaledTime + p.pitch; labPushSpeed = p.arg; }
                    if (p.kind == "passenger-look" && GameManager.instance && GameManager.instance.playerFP && GameManager.instance.playerFP.GetComponent<PassengerLook>() is PassengerLook passengerLook) passengerLook.Turn(p.pitch, 0f);
                    if (p.kind == "blinds-close" && GameManager.instance && GameManager.instance.watchTowerManager) foreach (var blind in GameManager.instance.watchTowerManager.towerSunBlinds) if (blind.isOpen) blind.Close();
                    if (p.kind == "join-code") { address = p.code; Connect(false); }
                    if (p.kind == "upnp-probe") upnp.Open(Port, false);
                    if (p.kind == "drop" && GameManager.instance && GameManager.instance.itemPickUp) GameManager.instance.itemPickUp.DropAllItems();
                    if (p.kind == "click")
                    {
                        // "screen": click where the point is on the current camera (seats have their own cameras).
                        if (p.code == "screen" && Camera.main) { var sp = Camera.main.WorldToScreenPoint(p.position); if (sp.z > 0f) LabInput.ClickPoint = new Vector2(sp.x, sp.y); }
                        else if (p.active) LookAt(p.position);
                        LabInput.ClickFrame = Time.frameCount + 2;
                    }
                    if (p.kind == "ui" && GameManager.instance && GameManager.instance.uIManager)
                    {
                        var method = AccessTools.Method(typeof(UIManager), p.code);
                        if (method != null && method.ReturnType == typeof(System.Collections.IEnumerator))
                            GameManager.instance.uIManager.StartCoroutine((System.Collections.IEnumerator)method.Invoke(GameManager.instance.uIManager,
                                method.GetParameters().Length == 1 ? new object[] { p.arg } : new object[0]));
                    }
                    if (p.kind == "steam-host") ConnectSteam(0);
                    if (p.kind == "steam-join" && ulong.TryParse(p.code, out var lobbyToJoin)) ConnectSteam(lobbyToJoin);
                    if (p.kind == "fs-dump") LabScene.DumpFirstScene(Path.Combine(bridgeDir, "firstscene.json"));
                    if (p.kind == "rv-dump") LabScene.DumpRv(Path.Combine(bridgeDir, "rv.json"));
                    if (p.kind == "bed-dump") LabScene.DumpBed(Path.Combine(bridgeDir, "bed.json"));
                    if (p.kind == "go-dump") LabScene.DumpObject(Path.Combine(bridgeDir, "go.txt"), p.code);
                    if (p.kind == "probe") LabProbe.Run(Path.Combine(bridgeDir, "probe.txt"), p.code ?? "", this);
                    if (p.kind == "lab-wet") Logger.LogInfo("LAB_WET " + LabScene.Wet(p.arg));
                    if (p.kind == "colliders-near") { var near = new System.Collections.Generic.List<string>(); var mask = GameManager.instance && GameManager.instance.watchTowerManager ? GameManager.instance.watchTowerManager.layerMask.value : ~0; foreach (var c in Physics.OverlapSphere(p.position, p.pitch > 0f ? p.pitch : 1.5f, ~0, QueryTriggerInteraction.Collide)) near.Add(ItemSync.PathOf(c.transform) + " type=" + c.GetType().Name + " layer=" + LayerMask.LayerToName(c.gameObject.layer) + " inMask=" + ((mask & (1 << c.gameObject.layer)) != 0) + " trigger=" + c.isTrigger + " enabled=" + c.enabled + " center=" + c.bounds.center.ToString("F2") + " size=" + c.bounds.size.ToString("F2")); File.WriteAllLines(Path.Combine(bridgeDir, "colliders.txt"), near); }
                    if (p.kind == "story-keys") File.WriteAllLines(Path.Combine(bridgeDir, "story-keys.txt"), Story.LabEntries(p.code ?? ""));
                    if (p.kind == "tower-collider" && GameManager.instance && GameManager.instance.watchTowerManager && AccessTools.Field(typeof(WatchTowerManager), p.code)?.GetValue(GameManager.instance.watchTowerManager) is GameObject colliderOwner && colliderOwner.TryGetComponent<Collider>(out var labCollider)) labCollider.enabled = p.active;
                    if (p.kind == "lab-old-bag") Items.LabSetOldBag(p.active);
                    if (p.kind == "rv-contacts" && GameManager.instance && GameManager.instance.rVController && GameManager.instance.rVController.rigidbody) { var body = GameManager.instance.rVController.rigidbody.gameObject; if (!body.GetComponent<LabRvContacts>()) body.AddComponent<LabRvContacts>(); LabRvContacts.Hits = 0; LabRvContacts.Impulse = 0f; }
                    if (p.kind == "lab-bag-away") { var labBag = ItemSync.LabBag; if (labBag) labBag.SetActive(!p.active); }
                    if (p.kind == "lab-eat") Logger.LogInfo("LAB_EAT " + Diner.LabEat(p.code));
                    if (p.kind == "cams") LabScene.DumpCameras(Path.Combine(bridgeDir, "cams.json"));
                    if (p.kind == "audio-dump") LabScene.DumpAudio(Path.Combine(bridgeDir, "audio.json"));
                    if (p.kind == "invoke") Logger.LogInfo("LAB_INVOKE " + p.code + " " + LabScene.Invoke(p.code, p.name, p.code2));
                    if (p.kind == "set") Logger.LogInfo("LAB_SET " + p.code + " " + LabScene.Set(p.code, p.name));
                    if (p.kind == "space") LabInput.SpaceFrame = Time.frameCount + 2;
                    if (p.kind == "key-g") LabInput.GFrame = Time.frameCount + 2;
                    if (p.kind == "rv-stand" && GameManager.instance && GameManager.instance.rVController) { p.kind = "teleport"; p.code = ""; p.position = GameManager.instance.rVController.fPTransform.position + Vector3.up * 0.1f; }
                    if (p.kind == "drive-look" && GameManager.instance && GameManager.instance.rVController && GameManager.instance.rVController.drivingCam)
                    { GameManager.instance.rVController.drivingCam.yRotation = p.pitch; GameManager.instance.rVController.drivingCam.xRotation = p.arg; }
                    if (p.kind == "ui-dump") LabUi.Dump(Path.Combine(bridgeDir, "ui.json"));
                    if (p.kind == "dlg-dump") LabScene.DumpDialogue(Path.Combine(bridgeDir, p.code == "computer" ? "computer.json" : "dialogue.json"));
                    if (p.kind == "dlg-pick") Talk.LabPick(p.arg);
                    if (p.kind == "trip-pick") Trip.LabPick(p.arg);
                    if (p.kind == "errand-begin") Errand.LabBegin();
                    if (p.kind == "lab-eat-dish" && GameManager.instance && GameManager.instance.watchTowerManager && GameManager.instance.watchTowerManager.casserole) { var dish = GameManager.instance.watchTowerManager.casserole; dish.isEating = false; dish.Eat(); Logger.LogInfo("LAB_EAT_DISH " + dish.finishedInt); }
                    if (p.kind == "lab-race") TowerChairPatch.LabRace = p.active;
                    if (p.kind == "lab-take-torch" && GameManager.instance && GameManager.instance.itemPickUp && !GameManager.instance.itemPickUp.inHandFlashlight)
                    {
                        // ItemPickUp's flashlight pick-up on the nearest lying flashlight.
                        var pickup = GameManager.instance.itemPickUp;
                        GameObject nearest = null; float best = p.code == "partner" ? 30f : 6f;
                        foreach (var light in FindObjectsOfType<FlashLight>())
                        {
                            if (!light.CompareTag("Flashlight") || light.transform.IsChildOf(GameManager.instance.playerFP.transform)) continue;
                            if (p.code == "partner" && light.name != "Partner's flashlight") continue;
                            float d = Vector3.Distance(light.transform.position, pickup.transform.position);
                            if (d < best) { best = d; nearest = light.gameObject; }
                        }
                        if (nearest)
                        {
                            pickup.flashLightItem = nearest; pickup.flashLight = nearest.GetComponent<FlashLight>();
                            nearest.transform.parent = pickup.itemHere;
                            nearest.transform.localPosition = new Vector3(-0.333f, 0.0803f, 0.0754f);
                            if (nearest.TryGetComponent<Rigidbody>(out var torchBody)) torchBody.isKinematic = true;
                            pickup.inHandFlashlight = true; pickup.flashLight.inHand = true;
                        }
                        Logger.LogInfo("LAB_TAKE_TORCH " + (nearest ? nearest.name + " d=" + best.ToString("F1") : "none"));
                    }
                    if (p.kind == "lab-take-bag" && GameManager.instance && GameManager.instance.itemPickUp)
                    {
                        // The same steps as ItemPickUp's click on a bag, for places the lab's centre click cannot reach.
                        var pickup = GameManager.instance.itemPickUp;
                        var bag = GameObject.FindGameObjectWithTag("Bag");
                        if (bag && !pickup.inHand)
                        {
                            pickup.currentItem = bag;
                            AccessTools.Method(typeof(ItemPickUp), "GetItemInHand").Invoke(pickup, new object[] { bag });
                            if (bag.TryGetComponent<Rigidbody>(out var bagBody)) bagBody.isKinematic = true;
                            bag.transform.localPosition = new Vector3(-0.11f, -0.067f, 0.15f);
                            pickup.currentItemType = ItemPickUp.CurrentItemType.Bag;
                            pickup.inHand = true; pickup.inHandBag = true; pickup.inHandCount = 1; pickup.bag = bag;
                        }
                    }
                    if (p.kind == "clips")
                    {
                        var lines = new System.Collections.Generic.List<string>();
                        foreach (var clip in Resources.FindObjectsOfTypeAll<AnimationClip>()) if (clip.humanMotion) lines.Add(clip.name + " len=" + clip.length.ToString("F1"));
                        foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
                            if (animator.runtimeAnimatorController && animator.isHuman) lines.Add("ANIMATOR " + animator.name + " -> " + string.Join(",", System.Array.ConvertAll(animator.runtimeAnimatorController.animationClips, c => c ? c.name : "-")));
                        File.WriteAllLines(Path.Combine(bridgeDir, "clips.txt"), lines);
                    }
                    if (p.kind == "dlg-start" && GameManager.instance && GameManager.instance.watchTowerManager)
                    {
                        // A radio talk at the desk without clicking: seated at the radio, then the named node.
                        var tower = GameManager.instance.watchTowerManager;
                        tower.state = WatchTowerManager.State.OnRadio;
                        GameManager.instance.isConversing = true;
                        GameManager.instance.ShowCursor();
                        tower.radioVoiceOver.radioVOAS.gameObject.SetActive(true);
                        GameManager.instance.uIManager.StartDialogue(p.code);
                    }
                    if (p.kind == "lab-match")
                    {
                        // The same throw Matchbox.Update makes on a click with the box open.
                        foreach (var box in Resources.FindObjectsOfTypeAll<Matchbox>())
                        {
                            if (!box.matchStickPrefab || !box.gameObject.scene.IsValid()) continue;
                            var camera = GameManager.instance.fpCamera.transform;
                            var match = Instantiate(box.matchStickPrefab, camera.position + camera.forward * 0.5f, camera.rotation);
                            var stick = match.GetComponent<MatchStick>();
                            stick.flameAS.Play(); stick.flameFx.SetActive(true);
                            match.AddComponent<Rigidbody>().AddForce(camera.forward * 150f + camera.right * -50f + camera.up * 50f);
                            stick.StartCoroutine(stick.FlameEnd());
                            break;
                        }
                    }
                    if (p.kind == "lab-spray" && GameManager.instance && GameManager.instance.itemPickUp && GameManager.instance.itemPickUp.spray)
                    {
                        var can = GameManager.instance.itemPickUp.spray;
                        can.spraying = p.active;
                        LabSprayHoldPatch.Held = p.active;
                        if (p.active) { can.sprayPS.Play(); can.sprayAS.Play(); } else { can.sprayPS.Stop(); can.sprayAS.Stop(); }
                    }
                    if (p.kind == "ui-press") Logger.LogInfo("LAB_PRESS " + p.code + " " + LabUi.Press(p.code));
                    if (p.kind == "capture-ui") LabUi.Capture(Path.Combine(bridgeDir, "ui.png"));
                    if (p.kind == "dump") LabDump.Write(Path.Combine(bridgeDir, "dump.json"));
                    if (p.kind == "voice-test" && capture != null) capture.TestTone();
                    if (p.kind == "interact" && GameManager.instance && GameManager.instance.watchTowerManager)
                    {
                        var manager = GameManager.instance.watchTowerManager;
                        switch (p.code)
                        {
                            case "OpenDoor": manager.OpenDoor(); break;
                            case "CloseDoor": manager.CloseDoor(); break;
                            case "OpenFridge": manager.OpenFridge(); break;
                            case "CloseFridge": manager.CloseFridge(); break;
                        }
                    }
                    if (p.kind == "teleport" && GameManager.instance && GameManager.instance.playerFP)
                    {
                        var position = p.position;
                        // "ground": drop onto the first collider below (cast from 3 m above, so roofs overhead are ignored).
                        if (p.code == "ground")
                        {
                            // Only surfaces the player actually stands on (some layers are set to pass through the player).
                            int playerLayer = GameManager.instance.playerFP.layer, solid = 0;
                            for (int layer = 0; layer < 32; layer++) if (!Physics.GetIgnoreLayerCollision(playerLayer, layer)) solid |= 1 << layer;
                            if (!Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out var hit, 80f, solid, QueryTriggerInteraction.Ignore))
                                throw new InvalidOperationException("No ground below " + position);
                            // The transform is the capsule's pivot: lift it so the capsule's bottom rests on the surface.
                            var body = GameManager.instance.playerFP.GetComponent<CharacterController>();
                            position = hit.point + Vector3.up * ((body ? body.height * 0.5f - body.center.y : 1f) + 0.1f);
                        }
                        // CharacterController overwrites transform moves made while it is enabled.
                        labWalkTarget = null;
                        var controller = GameManager.instance.playerFP.GetComponent<CharacterController>();
                        if (controller) controller.enabled = false;
                        GameManager.instance.playerFP.transform.position = position;
                        // A fall in progress keeps its speed across a teleport and would tunnel through the ground.
                        if (GameManager.instance.firstPersonController) AccessTools.Field(typeof(FirstPersonController), "moveDirection").SetValue(GameManager.instance.firstPersonController, Vector3.zero);
                        Physics.SyncTransforms();
                        if (controller) controller.enabled = true;
                    }
                }
                catch (Exception ex) { Logger.LogError(ex); }
            }
            if (Time.unscaledTime < nextStatus) return;
            nextStatus = Time.unscaledTime + 1;
            var gm = GameManager.instance;
            File.WriteAllText(Path.Combine(bridgeDir, "state.json"), WireJson.Write(new Packet {
                kind = status, scene = SceneManager.GetActiveScene().name, active = authenticated,
                position = gm && gm.playerFP ? gm.playerFP.transform.position : Vector3.zero,
                remotePosition = avatar != null ? avatar.Root.transform.position : Vector3.zero,
                world = WorldProbe.Snapshot(),
                respawns = Moments.Respawns,
                voiceConcealed = voice?.ConcealedFrames ?? 0, voiceLate = voice?.LateFrames ?? 0,
                storySent = Story.SentChanges, storyApplied = Story.AppliedChanges, storyTracked = Story.Tracked, items = Items.Count,
                arg = sceneEpoch,
                storySeq = GameManager.instance && GameManager.instance.watchTowerManager ? GameManager.instance.watchTowerManager.seq.ToString() : "",
                held = HeldSummary() + (Items.LocalHeld != "" ? " owns=" + Items.LocalHeld : ""),
                net = NetSummary(),
                addresses = string.Join(" | ", ConnectionPanel.Addresses()),
                statusText = ConnectionPanel.Explain(status),
                duty = Duties.Active + " started=" + Duties.Started + " done=" + Duties.Completed + (GameManager.instance && GameManager.instance.watchTowerManager ? " generator=" + GameManager.instance.watchTowerManager.isGeneratorOn + " fire=" + GameManager.instance.watchTowerManager.fireLit + " woodInStove=" + GameManager.instance.watchTowerManager.woodInStove + " blindsOpen=" + System.Array.FindAll(GameManager.instance.watchTowerManager.towerSunBlinds, b => b.isOpen).Length : ""),
                build = Flags(),
                voiceSent = capture?.SentFrames ?? 0,
                voiceReceived = voice?.ReceivedFrames ?? 0,
                voiceConsumed = voice?.ConsumedSamples ?? 0,
                voiceEnergy = voice?.ConsumedEnergy ?? 0,
                voiceOutputEnergy = voice?.OutputEnergy ?? 0,
                voiceOccluded = voice != null && voice.Occluded,
                partnerSpeed = avatar != null ? avatar.Speed : 0f,
                partnerJitter = avatar != null ? avatar.SpeedJitter : 0f,
                code2 = avatar != null ? avatar.HeldInfo : "",
                steam = (SteamTransport.Available ? "available " + SteamTransport.LocalName : "unavailable") + (transport is SteamTransport st ? " lobby=" + st.LobbyId + " host=" + st.IsHost + " connected=" + st.Connected : ""),
                rotation = avatar != null ? avatar.Root.transform.rotation : Quaternion.identity,
                code = avatar != null ? avatar.Root.transform.position.ToString("F3") : "no remote"
            }, true));
        }

        static void LookAt(Vector3 point)
        {
            var gm = GameManager.instance;
            if (!gm || !gm.firstPersonController || !gm.playerFP) return;
            var fps = gm.firstPersonController;
            var camera = fps.playerCamera ? fps.playerCamera.transform : Camera.main ? Camera.main.transform : null;
            if (!camera) return;
            var direction = point - camera.position;
            gm.playerFP.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 0f);
            fps.rotationX = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            camera.localRotation = Quaternion.Euler(fps.rotationX, 0f, 0f);
            Instance?.Logger.LogInfo("LAB_LOOK pitch=" + fps.rotationX.ToString("F1") + " cam=" + camera.name + " fpsEnabled=" + fps.enabled + " focused=" + Application.isFocused);
        }

        string NetSummary()
        {
            string extra = " upnp=" + upnp.State.Replace(' ', '_') + " external=" + upnp.ExternalAddress + " code=" + HostCode;
            if (transport is DirectTransport direct)
                return extra + " udp=" + direct.UdpActive + " poseBytes=" + LastPoseBytes + " rtt=" + direct.RttMs + " udpRx=" + direct.UdpReceived + " udpTx=" + direct.UdpSent + " poseGaps=" + direct.PoseGaps + " drop=" + DirectTransport.LabDropPercent;
            return extra + (transport is SteamTransport ? " steam" : " none");
        }

        static string HeldSummary()
        {
            var items = GameManager.instance ? GameManager.instance.itemPickUp : null;
            if (!items) return "";
            return (items.inHand && items.currentItem ? items.currentItem.name : "") + (items.inHandFlashlight ? "+flashlight" : "");
        }

        string Flags()
        {
            var gm = GameManager.instance;
            if (!gm) return "";
            var wtm = gm.watchTowerManager;
            string drive = "driving=" + Vehicles.LocalDriving + " passenger=" + Rides.Seated + " " + Rides.Debug + " rv=" + (gm.rVController ? gm.rVController.transform.position.ToString("F1") : "none")
                + " partnerActive=" + (avatar != null && avatar.Active) + " partnerSit=" + partnerSit + " label=" + (avatar != null && avatar.LabelShown) + " cab=" + (avatar != null && avatar.InCab) + " crouch=" + (gm.firstPersonController && CrouchField != null && (bool)CrouchField.GetValue(gm.firstPersonController)) + LabScene.Facts(this) + " ";
            string scareAudio = wtm ? "scareAudio=" + (wtm.heartBeatAS.isPlaying || wtm.guyScreamAS.isPlaying || wtm.glitchBeforeQuitAS.isPlaying) + " dead=" + gm.isDead + " fps=" + (gm.firstPersonController && gm.firstPersonController.enabled) + " " : "";
            return drive + scareAudio + "playerFP=" + ItemSync.PathOf(gm.playerFP.transform) + "@" + gm.playerFP.transform.position.ToString("F2") + " fps=" + (gm.firstPersonController ? ItemSync.PathOf(gm.firstPersonController.transform) + "@" + gm.firstPersonController.transform.position.ToString("F2") : "") + " pickup=" + (gm.itemPickUp ? ItemSync.PathOf(gm.itemPickUp.transform) : "") + " cam=" + (Camera.main ? ItemSync.PathOf(Camera.main.transform) + "@" + Camera.main.transform.position.ToString("F2") : "") + " conversing=" + gm.isConversing + " paused=" + gm.isPaused + " inHand=" + (gm.itemPickUp && gm.itemPickUp.inHand) + " pickupAt=" + (gm.itemPickUp ? gm.itemPickUp.transform.position.ToString("F2") : "") + " transition=" + (gm.uIManager && gm.uIManager.inTransition);
        }

        void CaptureCamera(bool lit = false)
        {
            var camera = Camera.main;
            if (!camera) return;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(960, 540, 24);
            var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            // Lab "lit" shots: a lamp at the lens so night-time evidence is readable.
            GameObject lamp = null;
            var ambient = RenderSettings.ambientLight;
            if (lit)
            {
                lamp = new GameObject("Lab lamp");
                lamp.transform.position = camera.transform.position;
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point; light.range = 8f; light.intensity = 1.1f; light.shadows = LightShadows.None;
                RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.25f);
            }
            try
            {
                camera.targetTexture = target;
                camera.Render();
                // Overlay cameras stacked on the main one (the RV cab camera) are drawn on top, as on screen.
                foreach (var overlay in Camera.allCameras)
                {
                    if (overlay == camera || overlay.depth <= camera.depth || overlay.clearFlags != CameraClearFlags.Depth || overlay.cullingMask == 1 << 5) continue;
                    var overlayTarget = overlay.targetTexture;
                    overlay.targetTexture = target;
                    overlay.Render();
                    overlay.targetTexture = overlayTarget;
                }
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(bridgeDir, "camera.png"), texture.EncodeToPNG());
            }
            finally
            {
                if (lamp) DestroyImmediate(lamp);
                RenderSettings.ambientLight = ambient;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Destroy(target);
                Destroy(texture);
            }
        }

        void OnGUI()
        {
            if (!menu) return;
            string L(string ru, string en) => ConnectionPanel.L(ru, en);
            var mine = ConnectionPanel.Addresses();
            float height = 360 + 20 * mine.Count;
            GUI.Box(new Rect(15, 15, 370, height), "Ironbark Coop");
            float y = 40;
            GUI.Label(new Rect(30, y, 340, 44), ConnectionPanel.Explain(status)); y += 46;
            var steam = transport as SteamTransport;
            GUI.Label(new Rect(30, y, 340, 22), SteamTransport.Available ? "Steam: " + SteamTransport.LocalName : L("Steam: недоступен (нужна версия из Steam)", "Steam: unavailable (needs the Steam version)")); y += 24;
            GUI.enabled = SteamTransport.Available;
            if (GUI.Button(new Rect(30, y, 160, 28), L("Хост через Steam", "Host via Steam"))) ConnectSteam(0);
            GUI.enabled = steam != null && steam.IsHost && steam.HasLobby;
            if (GUI.Button(new Rect(200, y, 170, 28), L("Пригласить друга", "Invite friend"))) steam.Invite();
            GUI.enabled = true; y += 38;
            GUI.Label(new Rect(30, y, 340, 22), L("Ваши адреса для друга:", "Your addresses for a friend:")); y += 20;
            foreach (var line in mine) { GUI.Label(new Rect(42, y, 330, 20), line); y += 20; }
            y += 6;
            GUI.Label(new Rect(30, y, 85, 25), L("IP хоста", "Host IP"));
            address = GUI.TextField(new Rect(120, y, 250, 25), address, 128); y += 30;
            GUI.Label(new Rect(30, y, 85, 25), L("Код", "Code"));
            code = GUI.TextField(new Rect(120, y, 250, 25), code, 64); y += 34;
            if (GUI.Button(new Rect(30, y, 110, 30), L("Хост (IP)", "Host (IP)"))) Connect(true);
            if (GUI.Button(new Rect(145, y, 110, 30), L("Войти (IP)", "Join (IP)"))) Connect(false);
            if (GUI.Button(new Rect(260, y, 110, 30), L("Отключиться", "Disconnect"))) Disconnect();
            y += 40;
            string hint = voiceMode == VoiceMode.PushToTalk ? L("Держите ", "Hold ") + talkKey + L(" — говорить", ": talk")
                : voiceMode == VoiceMode.VoiceActivation ? L("Голос по активации", "Voice activation") : L("Голос выключен", "Voice off");
            if (capture != null && capture.Transmitting) hint += L("  • вас слышно", "  • transmitting");
            GUI.Label(new Rect(30, y, 340, 40), capture != null && capture.Error != null ? capture.Error : "F8 " + L("— панель", ": panel") + " | " + hint);
        }
        void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SteamFriends.OnGameLobbyJoinRequested -= LobbyJoinRequested;
            harmony?.UnpatchSelf();
            Instance = null;
            Disconnect();
        }
    }
}
