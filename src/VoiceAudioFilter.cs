using System;
using UnityEngine;

namespace IronbarkCoop
{
    public sealed class VoiceAudioFilter : MonoBehaviour
    {
        internal VoiceBuffer Buffer;
        // Source samples (48 kHz) per output sample; set from AudioSettings.outputSampleRate on the main thread.
        internal double Step = 1.0;
        // The carrier clip is constant 1.0, so data already carries Unity's distance attenuation and panning.
        float[] mono = new float[0], source = new float[0], fresh = new float[0];
        double phase;
        float previous;
        readonly object gate = new object();
        double outputEnergy;
        // Energy after Unity's attenuation and panning, summed across channels.
        public double OutputEnergy { get { lock (gate) return outputEnergy; } }
        void OnAudioFilterRead(float[] data, int channels)
        {
            var buffer = Buffer;
            if (buffer == null || channels < 1) { Array.Clear(data, 0, data.Length); return; }
            int frames = data.Length / channels;
            if (mono.Length != frames) mono = new float[frames];
            if (Math.Abs(Step - 1.0) < 1e-9) buffer.Read(mono);
            else Resample(buffer, frames);
            double energy = 0;
            for (int frame = 0; frame < frames; frame++)
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = data[frame * channels + channel] * mono[frame];
                    data[frame * channels + channel] = value;
                    energy += value * value;
                }
            lock (gate) outputEnergy += energy;
        }

        // Linear interpolation between consecutive 48 kHz samples; `previous` carries continuity across callbacks.
        void Resample(VoiceBuffer buffer, int frames)
        {
            double end = phase + frames * Step;
            int needed = (int)Math.Floor(end);
            if (source.Length < needed + 1) { source = new float[needed + 64]; fresh = new float[needed + 64]; }
            source[0] = previous;
            buffer.Read(fresh, needed);
            Array.Copy(fresh, 0, source, 1, needed);
            for (int i = 0; i < frames; i++)
            {
                double position = phase + i * Step;
                int index = (int)position;
                float t = (float)(position - index);
                float a = source[Math.Min(index, needed)], b = source[Math.Min(index + 1, needed)];
                mono[i] = a + (b - a) * t;
            }
            previous = needed > 0 ? source[needed] : previous;
            phase = end - needed;
        }
    }
}
