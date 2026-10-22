using System;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

namespace IronbarkCoop
{
    // Mono speech, 48 kHz, 20 ms frames. No game assets or Scratch Creek code.
    internal sealed class VoiceCodec : IDisposable
    {
        public const int SampleRate = 48000;
        public const int FrameSamples = 960;
        readonly OpusEncoder encoder;
        readonly OpusDecoder decoder;
        readonly byte[] encoded = new byte[512];

        public VoiceCodec()
        {
            encoder = new OpusEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            decoder = new OpusDecoder(SampleRate, 1);
            encoder.Bitrate = 24000;
            encoder.Complexity = 5;
        }

        public byte[] Encode(float[] samples)
        {
            if (samples == null || samples.Length != FrameSamples) throw new ArgumentException("Expected 20 ms mono frame");
            int length = encoder.Encode(samples, 0, FrameSamples, encoded, 0, encoded.Length);
            var result = new byte[length];
            Buffer.BlockCopy(encoded, 0, result, 0, length);
            return result;
        }

        public int Decode(byte[] packet, float[] samples)
        {
            if (packet == null || packet.Length == 0 || packet.Length > 512) throw new ArgumentException("Invalid voice packet");
            if (samples == null || samples.Length < FrameSamples) throw new ArgumentException("Output too small");
            return decoder.Decode(packet, 0, packet.Length, samples, 0, FrameSamples, false);
        }

        // Packet loss concealment: Opus synthesises a plausible 20 ms frame for a lost packet.
        public int Conceal(float[] samples)
        {
            if (samples == null || samples.Length < FrameSamples) throw new ArgumentException("Output too small");
            return decoder.Decode(null, 0, 0, samples, 0, FrameSamples, false);
        }

        public void Dispose()
        {
            encoder.Dispose();
            decoder.Dispose();
        }
    }
}
