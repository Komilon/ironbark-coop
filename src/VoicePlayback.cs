using System;
using UnityEngine;

namespace IronbarkCoop
{
    internal sealed class VoicePlayback : IDisposable
    {
        readonly VoiceCodec codec = new VoiceCodec();
        readonly VoiceBuffer buffer = new VoiceBuffer();
        readonly float[] decoded = new float[VoiceCodec.FrameSamples];
        readonly GameObject emitter;
        readonly AudioSource source;
        readonly AudioClip clip;
        readonly VoiceAudioFilter filter;
        readonly AudioLowPassFilter lowPass;
        float nextOcclusion, occludedTarget = 22000f;
        public int ReceivedFrames { get; private set; }
        public long ConsumedSamples => buffer.Consumed;
        public double ConsumedEnergy => buffer.Energy;
        public double OutputEnergy => filter.OutputEnergy;
        public float LastSpeechTime { get; private set; } = -10f;
        public bool Occluded { get; private set; }
        public bool Speaking => Time.unscaledTime - LastSpeechTime < 0.25f;

        public VoicePlayback(float volume)
        {
            emitter = new GameObject("Coop voice");
            UnityEngine.Object.DontDestroyOnLoad(emitter);
            source = emitter.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            // Proximity chat: full voice up close, then an even fade (the same number of decibels per metre) until
            // silence at MaxDistance, so the voice thins out steadily instead of holding and then dropping off.
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff());
            source.minDistance = 1f;
            source.maxDistance = MaxDistance;
            source.dopplerLevel = 0f;
            source.loop = true;
            source.volume = baseVolume = Mathf.Clamp01(volume);
            // Filter order matters: speech is written first, then muffled by the low-pass when walls block it.
            filter = emitter.AddComponent<VoiceAudioFilter>();
            filter.Buffer = buffer;
            filter.Step = VoiceCodec.SampleRate / (double)AudioSettings.outputSampleRate;
            lowPass = emitter.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 22000f;
            clip = AudioClip.Create("Partner voice carrier", 48000, 1, 48000, false);
            var ones = new float[48000];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            clip.SetData(ones, 0);
            source.clip = clip;
            source.Play();
        }

        public const float MaxDistance = 12.5f;
        const float FullUntil = 1.5f, FadeDecibels = 30f;

        // Gain over distance as a fraction of MaxDistance: 0 dB to FullUntil, then -FadeDecibels at the edge, then 0.
        internal static AnimationCurve Rolloff()
        {
            var curve = new AnimationCurve();
            for (float d = 0f; d <= MaxDistance + 0.01f; d += 0.5f)
            {
                float fade = Mathf.Clamp01((d - FullUntil) / (MaxDistance - FullUntil));
                float gain = d >= MaxDistance ? 0f : Mathf.Pow(10f, -FadeDecibels * fade / 20f);
                // A short linear tail into silence so the last decibels do not click off.
                if (fade > 0.92f) gain *= (1f - fade) / 0.08f;
                curve.AddKey(new Keyframe(d / MaxDistance, gain));
            }
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        public static float GainAt(float distance) => Rolloff().Evaluate(Mathf.Clamp01(distance / MaxDistance));

        int lastSeq;
        public int ConcealedFrames { get; private set; }
        public int LateFrames { get; private set; }

        public void Receive(string base64, int seq)
        {
            if (base64 == null || base64.Length > 700) return;
            if (seq > 0 && lastSeq > 0)
            {
                // UDP may reorder or lose frames: drop late ones, conceal short gaps, restart after long silence.
                if (seq <= lastSeq && lastSeq - seq < 1000) { LateFrames++; return; }
                int missing = seq - lastSeq - 1;
                if (missing > 0 && missing <= 3)
                    for (int i = 0; i < missing; i++) { buffer.Push(decoded, codec.Conceal(decoded)); ConcealedFrames++; }
            }
            if (seq > 0) lastSeq = seq;
            var bytes = Convert.FromBase64String(base64);
            int count = codec.Decode(bytes, decoded);
            buffer.Push(decoded, count);
            ReceivedFrames++;
            LastSpeechTime = Time.unscaledTime;
        }


        // Asleep (the dream): the partner is heard without direction, far off and echoing, and only as loud as she would
        // be at the bed (fading out as she leaves the tower).
        AudioReverbFilter reverb;
        bool dream, sleepTalk;
        float dreamGain = 1f, gain = 1f;
        readonly float baseVolume;
        public float Gain => gain;
        // Off the map (the credits): the partner is simply heard, as on a call.
        bool flat;
        public void SetFlat(bool on)
        {
            if (on == flat) return;
            flat = on;
            if (!dream) source.spatialBlend = on ? 0f : 1f;
            // Nothing is between two people on a call: the wall muffling from the last scene must not carry over (playtest:
            // the partner was barely heard in the credits), and the voice is lifted over the credits music.
            if (on) { Occluded = false; occludedTarget = 22000f; lowPass.cutoffFrequency = 22000f; }
            filter.Boost = on ? CallBoost : 1f;
        }
        const float CallBoost = 2.2f;
        internal float Cutoff => lowPass.cutoffFrequency;
        internal float Boost => filter.Boost;
        // Lab: as if a wall had been between the players when the scene changed.
        internal void LabMuffle() { occludedTarget = 1400f; lowPass.cutoffFrequency = 1400f; Occluded = true; }

        public void SetDream(bool on, float hearing)
        {
            dreamGain = on ? Mathf.Clamp01(hearing) : 1f;
            if (on == dream) return;
            dream = on;
            source.spatialBlend = on || flat ? 0f : 1f;
            if (on && !reverb) reverb = emitter.AddComponent<AudioReverbFilter>();
            if (reverb) { reverb.reverbPreset = AudioReverbPreset.Psychotic; reverb.enabled = on; }
        }

        // The partner is the one dreaming: asleep in the bed here, they are not heard at all (playtest report: Tessa
        // heard the dreaming Marcus talk).
        public void SetSleepTalk(bool on) => sleepTalk = on;
        const float SleepTalkGain = 0f;
        public void Update(Vector3 headPosition, bool present)
        {
            emitter.transform.position = headPosition;
            source.mute = !present;
            if (!present) { buffer.Clear(); return; }
            if (!dream && !flat && Time.unscaledTime >= nextOcclusion)
            {
                nextOcclusion = Time.unscaledTime + 0.1f;
                var listener = Camera.main;
                if (listener)
                {
                    Vector3 from = listener.transform.position, to = headPosition;
                    Vector3 direction = to - from;
                    float distance = direction.magnitude;
                    // Skip the first 0.4 m so the local player's own collider does not count as a wall.
                    Occluded = distance > 0.8f && Physics.Raycast(from + direction / distance * 0.4f, direction, distance - 0.8f, ~0, QueryTriggerInteraction.Ignore);
                    occludedTarget = Occluded ? 1400f : 22000f;
                }
            }
            float cutoff = dream ? 900f : flat ? 22000f : occludedTarget;
            lowPass.cutoffFrequency = Mathf.Lerp(lowPass.cutoffFrequency, cutoff, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            float wanted = (dream ? dreamGain : 1f) * (sleepTalk ? SleepTalkGain : 1f);
            // Falling asleep (or waking) is not a fade: the dreamer is silent at once.
            gain = sleepTalk ? 0f : Mathf.MoveTowards(gain, wanted, Time.unscaledDeltaTime * 2f);
            source.volume = baseVolume * gain;
        }

        public void Dispose()
        {
            if (source) source.Stop();
            buffer.Clear();
            codec.Dispose();
            UnityEngine.Object.Destroy(emitter);
            UnityEngine.Object.Destroy(clip);
        }
    }
}
