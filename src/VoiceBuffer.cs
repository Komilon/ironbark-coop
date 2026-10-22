using System;

namespace IronbarkCoop
{
    // Audio-thread-safe jitter buffer. Prime with 60 ms; discard stale audio above 200 ms.
    internal sealed class VoiceBuffer
    {
        readonly object gate = new object();
        readonly float[] samples = new float[9600];
        int read, count;
        bool primed;
        long consumed;
        double energy;
        public long Consumed { get { lock (gate) return consumed; } }
        public double Energy { get { lock (gate) return energy; } }
        public void Push(float[] frame, int length)
        {
            if (length < 0 || length > frame.Length || length > samples.Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            lock (gate)
            {
                if (count + length > samples.Length) { read = 0; count = 0; primed = false; }
                for (int i = 0; i < length; i++) samples[(read + count + i) % samples.Length] = frame[i];
                count += length;
            }
        }
        public void Read(float[] output) => Read(output, output.Length);
        public void Read(float[] output, int requested)
        {
            Array.Clear(output, 0, requested);
            lock (gate)
            {
                if (!primed && count < 2880) return;
                primed = true;
                int length = Math.Min(requested, count);
                for (int i = 0; i < length; i++)
                {
                    output[i] = samples[(read + i) % samples.Length];
                    energy += output[i] * output[i];
                }
                consumed += length;
                read = (read + length) % samples.Length;
                count -= length;
                if (length < requested) primed = false;
            }
        }
        public void Clear()
        {
            lock (gate) { read = count = 0; primed = false; }
        }
    }
}
