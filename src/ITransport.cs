using System;
using System.Collections.Concurrent;

namespace IronbarkCoop
{
    // One partner connection. Poses and world snapshots are latest-value mailboxes; voice is a short lossy queue.
    internal interface ITransport : IDisposable
    {
        ConcurrentQueue<string> Incoming { get; }
        ConcurrentQueue<string> Events { get; }
        ConcurrentQueue<string> Voice { get; }
        bool Connected { get; }
        string TakePose();
        string TakeWorld();
        void Send(string value);
        // Called every frame on Unity's main thread (Steam reads packets here; TCP uses its own threads).
        void Pump();
        // Milliseconds since anything (data or a keep-alive) last came from the partner; 0 while not connected.
        int SilentMs { get; }
    }

    internal static class PacketKinds
    {
        public static bool IsPose(string json) => json.StartsWith("{\"kind\":\"pose\",", StringComparison.Ordinal);
        public static bool IsWorld(string json) => json.StartsWith("{\"kind\":\"world\",", StringComparison.Ordinal);
        public static bool IsVoice(string json) => json.StartsWith("{\"kind\":\"voice\",", StringComparison.Ordinal);
    }
}
