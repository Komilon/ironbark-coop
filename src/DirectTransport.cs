using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace IronbarkCoop
{
    // Direct IP connection: TCP carries reliable story/control messages; UDP on the same port carries poses and voice,
    // so one lost packet never stalls the rest. Falls back to TCP for everything while UDP is unavailable.
    // Network threads never touch Unity objects. One peer, bounded frames and queues.
    internal sealed class DirectTransport : ITransport
    {
        const byte TypeHello = 1, TypeAck = 2, TypePose = 3, TypeVoice = 4, TypePing = 5, TypePong = 6;
        const int HeaderSize = 12, MaxDatagram = 1200;
        public ConcurrentQueue<string> Incoming { get; } = new ConcurrentQueue<string>();
        public ConcurrentQueue<string> Events { get; } = new ConcurrentQueue<string>();
        public ConcurrentQueue<string> Voice { get; } = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<byte[]> outgoingVoice = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<byte[]> outgoing = new ConcurrentQueue<byte[]>();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly Random random = new Random();
        volatile bool stopped, connected, udpReady, isHost;
        TcpListener listener;
        TcpClient peer;
        UdpClient udp;
        IPEndPoint udpRemote;
        uint token;
        // Per-type sequence numbers so gaps in one stream are not confused with other traffic.
        readonly int[] sendSeq = new int[8];
        uint lastPoseSeq;
        long lastUdpReceive;
        string latestPose, latestWorld;
        byte[] outgoingPose, outgoingWorld;

        // Lab-only packet loss for UDP sends, in percent.
        internal static int LabDropPercent;
        public bool Connected => connected;
        public bool UdpActive => udpReady;
        public int RttMs { get; private set; } = -1;
        public int UdpReceived, UdpSent, PoseGaps;
        public string TakePose() => Interlocked.Exchange(ref latestPose, null);
        public string TakeWorld() => Interlocked.Exchange(ref latestWorld, null);
        public void Pump() { }

        public void Host(int port)
        {
            isHost = true;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(1);
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            IgnoreConnectionReset(udp);
            Run(ReceiveUdp);
            Run(() => {
                while (!stopped)
                {
                    var client = listener.AcceptTcpClient();
                    if (connected) { client.Close(); continue; }
                    Serve(client);
                }
            });
        }

        public void Join(string address, int port)
        {
            Run(() => {
                var client = new TcpClient();
                peer = client;
                var task = client.ConnectAsync(address, port);
                if (!task.Wait(8000)) { client.Close(); throw new IOException("Connection timeout"); }
                udpRemote = new IPEndPoint(((IPEndPoint)client.Client.RemoteEndPoint).Address, port);
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                IgnoreConnectionReset(udp);
                Run(ReceiveUdp);
                Serve(client);
            });
        }

        // Windows reports ICMP "port unreachable" as a receive error on UDP sockets; ignore it.
        static void IgnoreConnectionReset(UdpClient client)
        {
            try { client.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); } catch { }
        }

        void Run(Action action)
        {
            new Thread(() => {
                try { action(); }
                catch (Exception ex) { if (!stopped) Events.Enqueue(ex.Message); }
            }) { IsBackground = true, Name = "IronbarkCoop transport" }.Start();
        }

        void Serve(TcpClient client)
        {
            peer = client;
            client.NoDelay = true;
            client.SendTimeout = 5000;
            udpReady = false;
            lastPoseSeq = 0;
            connected = true;
            Events.Enqueue("Connected");
            if (isHost)
            {
                // The token ties UDP datagrams to this TCP session; the client learns it before saying hello.
                token = (uint)random.Next(1, int.MaxValue);
                udpRemote = null;
                outgoing.Enqueue(Encoding.UTF8.GetBytes("{\"kind\":\"udp-token\",\"code\":\"" + token + "\"}"));
            }
            var stream = client.GetStream();
            Run(() => {
                try
                {
                    while (!stopped && connected && ReferenceEquals(peer, client))
                    {
                        if (!outgoing.TryDequeue(out var bytes))
                            bytes = Interlocked.Exchange(ref outgoingWorld, null);
                        if (bytes == null)
                            bytes = Interlocked.Exchange(ref outgoingPose, null);
                        if (bytes == null) outgoingVoice.TryDequeue(out bytes);
                        if (bytes == null) { signal.WaitOne(100); continue; }
                        var size = BitConverter.GetBytes(bytes.Length);
                        stream.Write(size, 0, 4);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
                finally { client.Close(); }
            });
            Run(() => Maintain(client));
            try
            {
                var header = new byte[4];
                while (!stopped)
                {
                    ReadExactly(stream, header);
                    int size = BitConverter.ToInt32(header, 0);
                    if (size < 1 || size > 65536) throw new IOException("Invalid frame size");
                    var body = new byte[size];
                    ReadExactly(stream, body);
                    string json = Encoding.UTF8.GetString(body);
                    if (json.StartsWith("{\"kind\":\"udp-token\",", StringComparison.Ordinal))
                    {
                        int start = json.IndexOf("\"code\":\"", StringComparison.Ordinal) + 8;
                        uint.TryParse(json.Substring(start, json.IndexOf('"', start) - start), out token);
                    }
                    else if (PacketKinds.IsPose(json))
                        Interlocked.Exchange(ref latestPose, json);
                    else if (PacketKinds.IsWorld(json))
                        Interlocked.Exchange(ref latestWorld, json);
                    else if (PacketKinds.IsVoice(json))
                        EnqueueVoice(json);
                    else
                    {
                        if (Incoming.Count >= 128) throw new IOException("Receive queue overflow");
                        Incoming.Enqueue(json);
                    }
                }
            }
            catch (Exception ex) { if (!stopped) Events.Enqueue("Disconnected: " + ex.Message); }
            finally
            {
                connected = false;
                udpReady = false;
                client.Close();
                while (outgoing.TryDequeue(out _)) { }
                while (Incoming.TryDequeue(out _)) { }
                while (Voice.TryDequeue(out _)) { }
                while (outgoingVoice.TryDequeue(out _)) { }
                Interlocked.Exchange(ref latestPose, null);
                Interlocked.Exchange(ref outgoingPose, null);
                Interlocked.Exchange(ref latestWorld, null);
                Interlocked.Exchange(ref outgoingWorld, null);
            }
        }

        // Client keeps knocking until the host acknowledges; both ping each second; silence drops back to TCP.
        void Maintain(TcpClient client)
        {
            while (!stopped && connected && ReferenceEquals(peer, client))
            {
                if (!isHost && !udpReady && token != 0 && udpRemote != null) SendDatagram(TypeHello, new byte[0], false);
                if (udpReady)
                {
                    SendDatagram(TypePing, BitConverter.GetBytes(DateTime.UtcNow.Ticks), false);
                    if (Environment.TickCount - Interlocked.Read(ref lastUdpReceive) > 4000)
                    {
                        udpReady = false;
                        Events.Enqueue("UDP lost, using TCP");
                    }
                }
                Thread.Sleep(udpReady ? 1000 : 250);
            }
        }

        void ReceiveUdp()
        {
            while (!stopped)
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;
                try { data = udp.Receive(ref from); }
                catch (SocketException) { if (stopped) return; continue; }
                catch (ObjectDisposedException) { return; }
                if (data.Length < HeaderSize || data[0] != (byte)'I' || data[1] != (byte)'B' || !connected) continue;
                if (BitConverter.ToUInt32(data, 4) != token || token == 0) continue;
                byte type = data[2];
                uint seq = BitConverter.ToUInt32(data, 8);
                if (isHost && type == TypeHello)
                {
                    udpRemote = from;
                    SendDatagram(TypeAck, new byte[0], false);
                }
                // Only the registered peer endpoint may feed game data.
                if (udpRemote == null || !from.Equals(udpRemote)) continue;
                Interlocked.Exchange(ref lastUdpReceive, Environment.TickCount);
                Interlocked.Increment(ref UdpReceived);
                if (!udpReady && (type == TypeAck || type == TypeHello || type == TypePose || type == TypeVoice || type == TypePing))
                {
                    udpReady = true;
                    Events.Enqueue("UDP active");
                }
                string json = data.Length > HeaderSize ? Encoding.UTF8.GetString(data, HeaderSize, data.Length - HeaderSize) : null;
                switch (type)
                {
                    case TypePose:
                        // Late or duplicate poses are useless; keep only the newest.
                        if (seq <= lastPoseSeq) break;
                        if (lastPoseSeq != 0 && seq > lastPoseSeq + 1) Interlocked.Add(ref PoseGaps, (int)(seq - lastPoseSeq - 1));
                        lastPoseSeq = seq;
                        Interlocked.Exchange(ref latestPose, json);
                        break;
                    case TypeVoice: if (json != null) EnqueueVoice(json); break;
                    case TypePing: SendDatagram(TypePong, Slice(data), false); break;
                    case TypePong:
                        if (data.Length >= HeaderSize + 8)
                            RttMs = (int)((DateTime.UtcNow.Ticks - BitConverter.ToInt64(data, HeaderSize)) / TimeSpan.TicksPerMillisecond);
                        break;
                }
            }
        }

        static byte[] Slice(byte[] data)
        {
            var payload = new byte[data.Length - HeaderSize];
            Buffer.BlockCopy(data, HeaderSize, payload, 0, payload.Length);
            return payload;
        }

        void EnqueueVoice(string json)
        {
            while (Voice.Count >= 16) Voice.TryDequeue(out _);
            Voice.Enqueue(json);
        }

        bool SendDatagram(byte type, byte[] payload, bool lossy)
        {
            var remote = udpRemote;
            if (udp == null || remote == null || HeaderSize + payload.Length > MaxDatagram) return false;
            // 100 behaves like a firewall blocking UDP entirely; lower values drop only game traffic.
            if (LabDropPercent >= 100 || (lossy && LabDropPercent > 0 && random.Next(100) < LabDropPercent)) { Interlocked.Increment(ref UdpSent); return true; }
            var datagram = new byte[HeaderSize + payload.Length];
            datagram[0] = (byte)'I'; datagram[1] = (byte)'B'; datagram[2] = type;
            Buffer.BlockCopy(BitConverter.GetBytes(token), 0, datagram, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)Interlocked.Increment(ref sendSeq[type])), 0, datagram, 8, 4);
            Buffer.BlockCopy(payload, 0, datagram, HeaderSize, payload.Length);
            try { udp.Send(datagram, datagram.Length, remote); Interlocked.Increment(ref UdpSent); return true; }
            catch { return false; }
        }

        static void ReadExactly(Stream stream, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }

        public void Send(string value)
        {
            if (!connected) return;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            bool voice = PacketKinds.IsVoice(value), pose = PacketKinds.IsPose(value);
            if ((voice || pose) && udpReady && SendDatagram(voice ? TypeVoice : TypePose, bytes, true)) return;
            if (voice)
            {
                if (bytes.Length > 4096) return;
                while (outgoingVoice.Count >= 8) outgoingVoice.TryDequeue(out _);
                outgoingVoice.Enqueue(bytes);
                signal.Set();
                return;
            }
            if (PacketKinds.IsWorld(value))
            {
                Interlocked.Exchange(ref outgoingWorld, bytes);
                signal.Set();
                return;
            }
            if (pose)
            {
                Interlocked.Exchange(ref outgoingPose, bytes);
                signal.Set();
                return;
            }
            if (bytes.Length > 65536 || outgoing.Count >= 128)
            {
                Events.Enqueue("Send queue limit exceeded");
                peer?.Close();
                return;
            }
            outgoing.Enqueue(bytes);
            signal.Set();
        }

        public void Dispose()
        {
            stopped = true;
            connected = false;
            udpReady = false;
            listener?.Stop();
            peer?.Close();
            try { udp?.Close(); } catch { }
            signal.Set();
        }
    }
}
