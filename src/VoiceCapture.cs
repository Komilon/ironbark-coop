using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    internal enum VoiceMode { PushToTalk, VoiceActivation, Off }

    internal sealed class VoiceCapture : IDisposable
    {
        readonly Plugin plugin;
        readonly VoiceCodec codec = new VoiceCodec();
        readonly float[] frame = new float[VoiceCodec.FrameSamples];
        readonly List<float> pending = new List<float>(4800);
        AudioClip microphone;
        string device;
        int readPosition, toneFrame;
        float nextTone, gateUntil;
        double resamplePhase;
        float lastInput;
        public int SentFrames { get; private set; }
        int frameSeq;
        public bool Recording => microphone != null;
        public bool Transmitting { get; private set; }
        public float Level { get; private set; }
        public string Error { get; private set; }
        public VoiceCapture(Plugin plugin) { this.plugin = plugin; }
        public void TestTone() { toneFrame = 100; nextTone = Time.unscaledTime; }

        public void Tick(VoiceMode mode, KeyCode key, float threshold, string preferredDevice)
        {
            Transmitting = false;
            if (!plugin.Ready) { Stop(); toneFrame = 0; return; }
            if (toneFrame > 0)
            {
                if (Time.unscaledTime < nextTone) return;
                nextTone = Time.unscaledTime + 0.02f;
                for (int i = 0; i < frame.Length; i++)
                    frame[i] = 0.15f * Mathf.Sin(2f * Mathf.PI * 440f * ((100 - toneFrame) * frame.Length + i) / 48000f);
                Send();
                toneFrame--;
                Transmitting = true;
                return;
            }
            bool wanted = mode == VoiceMode.VoiceActivation || (mode == VoiceMode.PushToTalk && Input.GetKey(key));
            if (!wanted || plugin.Paused) { Stop(); return; }
            if (!microphone && !StartMicrophone(preferredDevice)) return;
            int position = Microphone.GetPosition(device);
            if (position < 0) return;
            int available = (position - readPosition + microphone.samples) % microphone.samples;
            // More than 200 ms behind (hitch or focus loss): drop stale input instead of sending late speech.
            if (available > microphone.frequency / 5) { readPosition = position; pending.Clear(); return; }
            if (available == 0) return;
            int channels = microphone.channels;
            var data = new float[available * channels];
            if (!microphone.GetData(data, readPosition)) { Error = "Microphone read failed"; Stop(); return; }
            readPosition = (readPosition + available) % microphone.samples;
            double step = microphone.frequency / (double)VoiceCodec.SampleRate;
            for (int sample = 0; sample < available; sample++)
            {
                float sum = 0;
                for (int channel = 0; channel < channels; channel++) sum += data[sample * channels + channel];
                float value = sum / channels;
                if (microphone.frequency == VoiceCodec.SampleRate) { pending.Add(value); continue; }
                // Linear resampling to 48 kHz between previous and current input sample.
                while (resamplePhase < 1.0)
                {
                    pending.Add(lastInput + (value - lastInput) * (float)resamplePhase);
                    resamplePhase += step;
                }
                resamplePhase -= 1.0;
                lastInput = value;
            }
            while (pending.Count >= frame.Length)
            {
                pending.CopyTo(0, frame, 0, frame.Length);
                pending.RemoveRange(0, frame.Length);
                float energy = 0;
                for (int i = 0; i < frame.Length; i++) energy += frame[i] * frame[i];
                Level = Mathf.Sqrt(energy / frame.Length);
                if (mode == VoiceMode.VoiceActivation)
                {
                    if (Level >= threshold) gateUntil = Time.unscaledTime + 0.35f;
                    if (Time.unscaledTime > gateUntil) continue;
                }
                Send();
                Transmitting = true;
            }
        }

        bool StartMicrophone(string preferredDevice)
        {
            var devices = Microphone.devices;
            if (devices.Length == 0) { Error = "No microphone"; return false; }
            device = Array.IndexOf(devices, preferredDevice) >= 0 ? preferredDevice : devices[0];
            Microphone.GetDeviceCaps(device, out int minRate, out int maxRate);
            int rate = minRate == 0 && maxRate == 0 ? VoiceCodec.SampleRate : Mathf.Clamp(VoiceCodec.SampleRate, minRate, maxRate);
            microphone = Microphone.Start(device, true, 2, rate);
            if (!microphone) { Error = "Microphone start failed"; return false; }
            readPosition = 0; resamplePhase = 0; lastInput = 0; pending.Clear();
            Error = null;
            return true;
        }

        void Send()
        {
            plugin.Send(new Packet { kind = "voice", scene = SceneManager.GetActiveScene().name, arg = ++frameSeq,
                audio = Convert.ToBase64String(codec.Encode(frame)) });
            SentFrames++;
        }

        void Stop()
        {
            if (!microphone) return;
            Microphone.End(device);
            UnityEngine.Object.Destroy(microphone);
            microphone = null;
            pending.Clear();
        }

        public void Dispose() { Stop(); codec.Dispose(); }
    }
}
