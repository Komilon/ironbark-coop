using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace IronbarkCoop
{
    // Partner body: a stripped clone of a humanoid already loaded by the game (models stay in the user's install),
    // driven by our own idle/walk/run blend instead of the NPC's story controller.
    internal sealed class PartnerAvatar : IDisposable
    {
        // Story props some NPCs carry; the partner should not hold them.
        static readonly string[] Props = { "Box", "Rifle", "Gun", "Paper", "Clipboard", "Phone", "Mug", "Cup", "Supplies", "Axe", "Knife", "Broom", "Bowl" };
        static readonly string[] Preferred = { "ParkRanger", "Park Ranger", "BadGuy_Worker", "Ranger" };
        static GameObject template;
        static AnimationClip idleClip, walkClip, runClip;

        public readonly GameObject Root;
        public string Character { get; private set; } = "ranger";
        readonly AvatarDriver driver;

        // Called after every scene load; keeps the first suitable humanoid for the rest of the session.
        public static void CaptureTemplate()
        {
            if (template) return;
            Animator best = null;
            int bestRank = int.MaxValue;
            foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
            {
                if (!animator.gameObject.scene.IsValid() || !animator.avatar || !animator.avatar.isHuman) continue;
                var controller = animator.runtimeAnimatorController;
                if (!controller || Find(controller, "Idle") == null || Find(controller, "Walk") == null) continue;
                int rank = Array.FindIndex(Preferred, name => animator.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (rank < 0 || animator.name.IndexOf("Cultist", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (rank < bestRank) { best = animator; bestRank = rank; }
            }
            if (!best) return;
            var holder = new GameObject("Coop template holder");
            holder.SetActive(false);
            var clone = UnityEngine.Object.Instantiate(best.gameObject, holder.transform);
            Strip(clone);
            clone.name = "Coop partner template";
            clone.SetActive(false);
            clone.transform.SetParent(null, false);
            UnityEngine.Object.DontDestroyOnLoad(clone);
            UnityEngine.Object.Destroy(holder);
            var controllerOfBest = best.runtimeAnimatorController;
            idleClip = Find(controllerOfBest, "Idle");
            walkClip = Find(controllerOfBest, "Standard Walk") ?? Find(controllerOfBest, "Walk");
            runClip = Find(controllerOfBest, "Running") ?? Find(controllerOfBest, "run") ?? walkClip;
            template = clone;
            Plugin.Log("AVATAR_TEMPLATE " + best.name);
            SitClip = SampleClip(clone, Find(controllerOfBest, "Typing") ?? Find(controllerOfBest, "Sit_Chair"));
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                Plugin.Log("AVATAR_RENDERER " + renderer.GetType().Name + " " + renderer.name + " active=" + renderer.gameObject.activeSelf);
        }

        // Seated pose for the survivors: Scratch Creek only has a dozing-off clip, so the ranger's own seated "Typing"
        // (the guard at his desk) is played once on a hidden copy and recorded as muscle frames.
        internal static ScClip SitClip;
        internal static bool UseSampledSit = false;
        static ScClip SampleClip(GameObject source, AnimationClip clip)
        {
            if (!source || !clip) return null;
            var holder = new GameObject("Coop pose sampler");
            holder.transform.position = new Vector3(0f, -2000f, 0f);
            var copy = UnityEngine.Object.Instantiate(source, holder.transform, false);
            copy.SetActive(true);
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            var animator = copy.GetComponent<Animator>();
            var graph = PlayableGraph.Create("Coop pose sampler");
            try
            {
                if (!animator || !animator.avatar || !animator.avatar.isHuman) return null;
                animator.runtimeAnimatorController = null;
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                const float rate = 15f;
                int frames = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(clip.length, 8f) * rate) + 1, 2, 121);
                var data = new float[frames * ScClip.Stride];
                for (int f = 0; f < frames; f++)
                {
                    playable.SetTime(f / rate);
                    graph.Evaluate();
                    handler.GetHumanPose(ref pose);
                    int o = f * ScClip.Stride;
                    data[o] = pose.bodyPosition.x; data[o + 1] = pose.bodyPosition.y; data[o + 2] = pose.bodyPosition.z;
                    data[o + 3] = pose.bodyRotation.x; data[o + 4] = pose.bodyRotation.y; data[o + 5] = pose.bodyRotation.z; data[o + 6] = pose.bodyRotation.w;
                    for (int m = 0; m < 95 && m < pose.muscles.Length; m++) data[o + 7 + m] = pose.muscles[m];
                }
                handler.Dispose();
                var arms = new System.Text.StringBuilder();
                for (int m = 37; m < 56; m++) arms.Append(HumanTrait.MuscleName[m].Replace(" ", "")).Append('=').Append(data[7 + m].ToString("F2")).Append(' ');
                Plugin.Log("AVATAR_SIT_SAMPLED " + clip.name + " frames=" + frames + " bodyY=" + data[1].ToString("F2") + " " + arms);
                return new ScClip { Name = clip.name, Length = (frames - 1) / rate, SampleRate = rate, Loop = true, FrameCount = frames, Data = data };
            }
            catch (Exception ex) { Plugin.Log("AVATAR_SIT_SAMPLE_FAILED " + ex.Message); return null; }
            finally { if (graph.IsValid()) graph.Destroy(); UnityEngine.Object.Destroy(holder); }
        }

        static AnimationClip Find(RuntimeAnimatorController controller, string name)
        {
            foreach (var clip in controller.animationClips)
                if (clip && clip.humanMotion && clip.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return clip;
            return null;
        }

        // Remove NPC behaviour, physics, audio and AI so only visuals and the Animator remain.
        static void Strip(GameObject root)
        {
            for (int pass = 0; pass < 4; pass++)
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component is Transform || component is Animator || component is Renderer || component is MeshFilter) continue;
                    try { UnityEngine.Object.DestroyImmediate(component); } catch { }
                }
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child && child != root.transform && Array.Exists(Props, prop => child.name.IndexOf(prop, StringComparison.OrdinalIgnoreCase) >= 0))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        public PartnerAvatar(string name, string character = null)
        {
            Root = new GameObject("Coop Partner");
            GameObject body;
            // Scratch Creek survivor (from the local pack) when available; otherwise an Ironbark humanoid.
            var survivor = string.IsNullOrEmpty(character) ? null : ScRuntime.Template(character);
            if (survivor)
            {
                body = UnityEngine.Object.Instantiate(survivor, Root.transform, false);
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.SetActive(true);
                driver = Root.AddComponent<AvatarDriver>();
                driver.InitializeSurvivor(body, name);
                driver.BindLie(character);
                Character = character;
                return;
            }
            if (template)
            {
                body = UnityEngine.Object.Instantiate(template, Root.transform, false);
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.SetActive(true);
            }
            else body = MakeFallbackBody(Root.transform);
            driver = Root.AddComponent<AvatarDriver>();
            driver.Initialize(body, name, idleClip, walkClip, runClip);
        }

        static GameObject MakeFallbackBody(Transform parent)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Fallback body";
            body.transform.SetParent(parent, false);
            body.transform.localPosition = Vector3.up * 0.9f;
            body.transform.localScale = new Vector3(0.45f, 0.9f, 0.45f);
            UnityEngine.Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().material.color = new Color(0.32f, 0.36f, 0.28f);
            return body;
        }

        public void SetPose(Vector3 feet, Quaternion yaw, float pitch, bool crouch, bool light, float sentAt = 0f, bool torch = false, bool sit = false) { driver.SetPose(feet, yaw, pitch, crouch, light, sentAt); driver.SetTorch(torch || light); driver.Sitting = sit; }
        public void Anchor(Vector3 feet, Quaternion yaw, Transform wheel, Vector3? eye = null) => driver.Anchor(feet, yaw, wheel, eye);
        public GameObject HeldVisual => driver.HeldVisual;
        public float SpeedJitter => driver.SpeedJitter;
        public void SetSpeaking(bool speaking) => driver.Speaking = speaking;
        public bool Lying { get => driver.Lying; set => driver.Lying = value; }
        public Quaternion Look => driver.Look;
        public void SetHeldItem(GameObject source) => driver.SetHeldItem(source);
        public void React() => driver.React();
        public Vector3 HeadPosition => driver.HeadPosition;
        // How far the seated head is from the eye point it is anchored to in our world (-1 when not anchored).
        public float HeadToSeatEye => driver.HeadToSeatEye;
        public float Speed => driver.Speed;
        public string HeldInfo => driver.HeldInfo;
        public string TorchInfo => driver.TorchInfo;
        public bool LabelShown => driver.LabelShown;

        // The RV cab is drawn by its own overlay camera (layer "RV") on top of the world while driving; a partner
        // inside the RV must be drawn by that camera too, or the cab covers them.
        readonly System.Collections.Generic.Dictionary<GameObject, int> homeLayers = new System.Collections.Generic.Dictionary<GameObject, int>();
        bool inCab;
        public bool InCab => inCab;
        public void SetInCab(bool value)
        {
            if (value == inCab) return;
            inCab = value;
            int cab = LayerMask.NameToLayer("RV");
            if (cab < 0) return;
            foreach (var t in Root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (value) { if (!homeLayers.ContainsKey(go)) homeLayers[go] = go.layer; go.layer = cab; }
                else if (homeLayers.TryGetValue(go, out int home)) go.layer = home;
            }
            if (!value) homeLayers.Clear();
        }
        public bool Active { get => Root.activeSelf; set => Root.SetActive(value); }
        public void Dispose() => UnityEngine.Object.Destroy(Root);
    }

    internal sealed class AvatarDriver : MonoBehaviour
    {
        Animator animator;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        Transform head, rightHand, body, rightUpper, rightLower, leftUpper, leftLower, leftHand;
        Light flashlight;
        TextMesh label;
        TalkingIcon talkIcon;
        GameObject torch;
        Vector3 torchLongAxis = Vector3.forward;
        bool torchInHand;
        Vector3 targetFeet, lastFeet;
        Quaternion targetYaw = Quaternion.identity;
        float pitch, targetPitch, speed, crouchBlend, bodyYaw, headYaw;
        bool crouch, hasPose;
        internal bool Speaking;
        AudioSource gaspSource;
        float flinch;
        static AudioClip gaspClip;

        // Partner was scared in their own world: flinch back and gasp, audible only nearby.
        public void React()
        {
            flinch = 1f;
            if (!gaspClip)
                foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                    if (clip.name == "Gasp_instant") { gaspClip = clip; break; }
            if (!gaspClip) return;
            if (!gaspSource)
            {
                gaspSource = gameObject.AddComponent<AudioSource>();
                gaspSource.spatialBlend = 1f;
                gaspSource.rolloffMode = AudioRolloffMode.Linear;
                gaspSource.minDistance = 2f;
                gaspSource.maxDistance = 20f;
                gaspSource.dopplerLevel = 0f;
                gaspSource.playOnAwake = false;
            }
            gaspSource.transform.position = HeadPosition;
            gaspSource.PlayOneShot(gaspClip, 0.9f);
        }
        public float Speed => speed;
        public Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * (1.6f - 0.7f * crouchBlend);

        ScPoseAnimator survivor;
        internal bool Sitting;
        // Where the partner looks (body yaw, head turn and pitch), as of the last frame.
        internal Quaternion Look = Quaternion.identity;
        // In bed: Scratch Creek's sleeping pose, laid along the bed (the anchor yaw points from the head to the feet).
        internal bool Lying;
        Transform hips;
        Vector3 anchorFeet; Quaternion anchorYaw; Transform wheel; float anchoredUntil;
        Vector3? seatEye;

        // Seated: follow the seat exactly; the driver's hands go to the steering wheel. With the partner's eye point the
        // whole body is shifted after the sitting pose is applied so the head is where their camera is: the seat's
        // geometry then decides where the body goes, whatever the seat height or the pose's proportions.
        public void Anchor(Vector3 feet, Quaternion yaw, Transform steering, Vector3? eye = null)
        {
            anchorFeet = feet; anchorYaw = yaw; wheel = steering; anchoredUntil = Time.unscaledTime + 0.25f; seatEye = eye;
        }

        // Head bone sits a little below and behind the eyes.
        const float EyeAboveHead = 0.07f, EyeBeforeHead = 0.06f;
        public Vector3 SeatOffset { get; private set; }
        // Measured in the frame the head is placed (in a moving RV, a later frame has moved the seat on).
        public float HeadToSeatEye => seatEye.HasValue && head && Sitting && Time.unscaledTime < anchoredUntil ? headToSeatEye : -1f;
        float headToSeatEye = -1f;
        void AlignHeadToSeat()
        {
            SeatOffset = Vector3.zero;
            if (!seatEye.HasValue || !Sitting || !head || Time.unscaledTime >= anchoredUntil) return;
            var forward = Quaternion.Euler(0f, bodyYaw, 0f) * Vector3.forward;
            // Lying, the eyes look up from the pillow: the head is just below them.
            var wanted = Lying ? seatEye.Value - Vector3.up * 0.1f : seatEye.Value - Vector3.up * EyeAboveHead - forward * EyeBeforeHead;
            var delta = Vector3.ClampMagnitude(wanted - head.position, 1.2f);
            transform.position += delta;
            headToSeatEye = Vector3.Distance(head.position, seatEye.Value);
            SeatOffset = delta;
        }


        internal void BindLie(string character)
        {
            if (survivor == null) return;
            survivor.Bind(ScPoseAnimator.Motion.Lie, character == "tessa" ? "sleeping tessa" : "Laying Sleeping");
            if (!survivor.Has(ScPoseAnimator.Motion.Lie)) survivor.Bind(ScPoseAnimator.Motion.Lie, character == "tessa" ? "Laying Sleeping" : "sleeping tessa");
            Plugin.Log("AVATAR_LIE_CLIP " + survivor.Has(ScPoseAnimator.Motion.Lie));
        }

        // Turns the lying body about its head so it runs from the head toward the feet direction, whatever way the clip lies.
        void LayAlongBed()
        {
            if (!Lying || !head || !hips || Time.unscaledTime >= anchoredUntil) return;
            var along = hips.position - head.position; along.y = 0f;
            if (along.sqrMagnitude < 0.01f) return;
            var wanted = anchorYaw * Vector3.forward;
            float turn = Vector3.SignedAngle(along, wanted, Vector3.up);
            transform.RotateAround(head.position, Vector3.up, turn);
        }
        public void InitializeSurvivor(GameObject bodyObject, string playerName)
        {
            Initialize(bodyObject, playerName, null, null, null);
            animator = bodyObject.GetComponent<Animator>();
            if (animator && animator.avatar && animator.avatar.isHuman)
            {
                survivor = new ScPoseAnimator(animator);
                if (PartnerAvatar.SitClip != null && PartnerAvatar.UseSampledSit) survivor.Bind(ScPoseAnimator.Motion.Sit, PartnerAvatar.SitClip);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }
        }

        public void Initialize(GameObject bodyObject, string playerName, AnimationClip idle, AnimationClip walk, AnimationClip run)
        {
            body = bodyObject.transform;
            animator = bodyObject.GetComponent<Animator>();
            if (animator && idle && walk)
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("Coop partner");
                graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
                mixer = AnimationMixerPlayable.Create(graph, 3);
                var clips = new[] { idle, walk, run ? run : walk };
                for (int i = 0; i < 3; i++)
                {
                    var playable = AnimationClipPlayable.Create(graph, clips[i]);
                    playable.SetApplyFootIK(true);
                    graph.Connect(playable, 0, mixer, i);
                }
                mixer.SetInputWeight(0, 1f);
                AnimationPlayableOutput.Create(graph, "Animation", animator).SetSourcePlayable(mixer);
                graph.Play();
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }
            var lightObject = new GameObject("Partner flashlight");
            lightObject.transform.SetParent(transform, false);
            flashlight = lightObject.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = 22f;
            flashlight.spotAngle = 55f;
            flashlight.intensity = 2.2f;
            flashlight.color = new Color(1f, 0.95f, 0.82f);
            flashlight.shadows = LightShadows.Soft;
            flashlight.enabled = false;
            label = MakeText("Partner name", playerName, new Color(0.5f, 0.48f, 0.42f), 0.018f);
            talkIcon = TalkingIcon.Create(transform);
        }

        TextMesh MakeText(string objectName, string text, Color color, float size)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = size;
            mesh.fontSize = 64;
            mesh.color = color;
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font) { mesh.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            return mesh;
        }

        struct Snapshot { public float At; public Vector3 Feet; public float Yaw, Pitch; }
        readonly System.Collections.Generic.List<Snapshot> snapshots = new System.Collections.Generic.List<Snapshot>();
        // Partner is shown slightly in the past so uneven ping and lost packets interpolate instead of jumping.
        const float InterpolationDelay = 0.1f;
        float clockOffset;
        bool hasOffset;
        readonly float[] frameSpeeds = new float[60];
        int frameSpeedIndex;
        public float SpeedJitter { get; private set; }

        public void SetPose(Vector3 feet, Quaternion yaw, float pitchDegrees, bool crouching, bool lightOn, float sentAt)
        {
            if (!hasPose) { transform.position = lastFeet = feet; transform.rotation = yaw; bodyYaw = yaw.eulerAngles.y; hasPose = true; }
            targetFeet = feet; targetYaw = yaw; targetPitch = pitchDegrees; crouch = crouching;
            flashlight.enabled = lightOn;
            if (sentAt <= 0f) return;
            // Sender clock -> local clock: the lowest observed delay, slowly following drift and route changes.
            float sample = Time.unscaledTime - sentAt;
            if (!hasOffset || sample < clockOffset) { clockOffset = sample; hasOffset = true; }
            else clockOffset = Mathf.Lerp(clockOffset, sample, 0.01f);
            if (snapshots.Count > 0 && sentAt <= snapshots[snapshots.Count - 1].At) return;
            snapshots.Add(new Snapshot { At = sentAt, Feet = feet, Yaw = yaw.eulerAngles.y, Pitch = pitchDegrees });
            if (snapshots.Count > 40) snapshots.RemoveAt(0);
        }

        // Pose at the render time from the buffer; extrapolates at most 0.2 s when packets are missing.
        bool Interpolated(out Vector3 feet, out float yaw, out float pitchOut)
        {
            feet = default; yaw = pitchOut = 0f;
            if (!hasOffset || snapshots.Count == 0) return false;
            float renderAt = Time.unscaledTime - clockOffset - InterpolationDelay;
            int newer = snapshots.FindIndex(s => s.At > renderAt);
            if (newer > 0)
            {
                var a = snapshots[newer - 1]; var b = snapshots[newer];
                float t = Mathf.InverseLerp(a.At, b.At, renderAt);
                feet = Vector3.Lerp(a.Feet, b.Feet, t); yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t); pitchOut = Mathf.Lerp(a.Pitch, b.Pitch, t);
                if (newer > 2) snapshots.RemoveRange(0, newer - 2);
                return true;
            }
            var last = snapshots[snapshots.Count - 1];
            if (newer == -1 && snapshots.Count >= 2)
            {
                var previous = snapshots[snapshots.Count - 2];
                float ahead = Mathf.Min(renderAt - last.At, 0.2f);
                var velocity = (last.Feet - previous.Feet) / Mathf.Max(0.001f, last.At - previous.At);
                feet = last.Feet + velocity * ahead; yaw = last.Yaw; pitchOut = last.Pitch;
                return true;
            }
            feet = last.Feet; yaw = last.Yaw; pitchOut = last.Pitch;
            return true;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-15f * dt);
            float lookYaw, viewPitch;
            bool anchored = Time.unscaledTime < anchoredUntil;
            if (anchored) { transform.position = anchorFeet; lookYaw = anchorYaw.eulerAngles.y + Mathf.Clamp(Mathf.DeltaAngle(anchorYaw.eulerAngles.y, targetYaw.eulerAngles.y), -100f, 100f); viewPitch = targetPitch; bodyYaw = anchorYaw.eulerAngles.y; lastFeet = anchorFeet; }
            else if (Interpolated(out var feetNow, out lookYaw, out viewPitch)) transform.position = feetNow;
            else
            {
                transform.position = Vector3.Lerp(transform.position, targetFeet, k);
                lookYaw = targetYaw.eulerAngles.y;
                viewPitch = Mathf.Lerp(pitch, targetPitch, k);
            }
            // Scratch Creek feel: standing still, only the head follows the view until it turns past ~70 degrees.
            float offset = Mathf.DeltaAngle(bodyYaw, lookYaw);
            bool moving = speed > 0.3f && !anchored;
            if (!anchored && (moving || Mathf.Abs(offset) > 70f)) bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, lookYaw, (moving ? 360f : 220f) * dt);
            transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
            headYaw = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, lookYaw), -75f, 75f);
            pitch = viewPitch;
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouch ? 1f : 0f, dt * 4f);
            var delta = transform.position - lastFeet; delta.y = 0;
            lastFeet = transform.position;
            if (dt > 0)
            {
                float frameSpeed = delta.magnitude / dt;
                speed = Mathf.Lerp(speed, frameSpeed, 1f - Mathf.Exp(-8f * dt));
                frameSpeeds[frameSpeedIndex++ % frameSpeeds.Length] = frameSpeed;
                float mean = 0f; foreach (var v in frameSpeeds) mean += v; mean /= frameSpeeds.Length;
                float variance = 0f; foreach (var v in frameSpeeds) variance += (v - mean) * (v - mean);
                SpeedJitter = Mathf.Sqrt(variance / frameSpeeds.Length);
            }
            if (mixer.IsValid())
            {
                // Game speeds: walk 3 m/s, sprint 6 m/s, crouch 1.5 m/s.
                float walkWeight = Mathf.Clamp01(speed / 1.2f) * (1f - Mathf.Clamp01((speed - 3.5f) / 1.5f));
                float runWeight = Mathf.Clamp01((speed - 3.5f) / 1.5f);
                mixer.SetInputWeight(0, 1f - walkWeight - runWeight);
                mixer.SetInputWeight(1, walkWeight);
                mixer.SetInputWeight(2, runWeight);
                mixer.GetInput(1).SetSpeed(Mathf.Clamp(speed / 1.6f, 0.6f, 1.8f));
                mixer.GetInput(2).SetSpeed(Mathf.Clamp(speed / 5.5f, 0.8f, 1.4f));
            }
            // Survivors crouch with Scratch Creek's own animation; the Ironbark fallback body is squashed instead.
            if (survivor != null) survivor.Update(dt, Sitting || Lying ? 0f : speed, crouch, Sitting, Lying);
            else body.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.62f, Sitting ? 1f : crouchBlend), 1f);
            // Flinch: snap back ~16 degrees, then recover over ~0.8 s.
            flinch = Mathf.MoveTowards(flinch, 0f, dt * 1.25f);
            float lean = Mathf.Sin(Mathf.Clamp01(flinch) * Mathf.PI * 0.5f) * 16f;
            body.localRotation = Quaternion.Euler(-lean, 0f, 0f);
        }

        void LateUpdate()
        {
            LayAlongBed();
            LateUpdateBody();
        }

        void LateUpdateBody()
        {
            AlignHeadToSeat();
            var look = Quaternion.Euler(0f, bodyYaw + headYaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
            Look = look;
            if (head && !Lying)
            {
                head.rotation = Quaternion.AngleAxis(headYaw, Vector3.up) * head.rotation;
                head.rotation = Quaternion.AngleAxis(Mathf.Clamp(pitch, -60f, 60f) * 0.6f, look * Vector3.right) * head.rotation;
            }
            var lightAnchor = rightHand ? rightHand.position : transform.position + Vector3.up * 1.2f + transform.right * 0.25f;
            // Driving: both hands on the wheel (left and right of its centre).
            if (wheel && Time.unscaledTime < anchoredUntil)
            {
                ArmIK.Reach(rightUpper, rightLower, rightHand, wheel.position + wheel.right * 0.17f, -transform.up);
                ArmIK.Reach(leftUpper, leftLower, leftHand, wheel.position - wheel.right * 0.17f, -transform.up);
            }
            // Torch held at the right side of the chest, pointing where the partner looks; the beam leaves its tip.
            var torchChest = transform.position + Vector3.up * Mathf.Lerp(1.36f, 0.86f, crouchBlend);
            if (torch && torchInHand && !Sitting)
            {
                // The right hand reaches the grip point; the torch then sits in that hand.
                var grip = torchChest + look * new Vector3(0.17f, -0.06f, 0.32f);
                ArmIK.Reach(rightUpper, rightLower, rightHand, grip, -transform.up);
                var torchPosition = rightHand ? rightHand.position + look * Vector3.forward * 0.04f : grip;
                torch.transform.SetPositionAndRotation(torchPosition, Quaternion.FromToRotation(torch.transform.rotation * torchLongAxis, look * Vector3.forward) * torch.transform.rotation);
                flashlight.transform.SetPositionAndRotation(torchPosition + look * Vector3.forward * 0.12f, look);
            }
            else flashlight.transform.SetPositionAndRotation(lightAnchor + look * Vector3.forward * 0.15f, look);
            // Held items are presented the way players hold them: in front of the body, tilted up toward the face.
            // The item's flat side faces up toward the holder's eyes and outward, like a compass or mug held in front.
            // Anchored in front of the chest along the view (the idle arm may hang behind the turned head).
            var chest = transform.position + Vector3.up * Mathf.Lerp(1.3f, 0.82f, crouchBlend);
            Vector3 itemPoint = chest + look * new Vector3(torchInHand ? -0.14f : 0.12f, 0f, 0.36f);
            if (heldVisual && !Sitting)
            {
                // Left hand carries items while the right holds the torch; otherwise the right hand does.
                if (torchInHand) ArmIK.Reach(leftUpper, leftLower, leftHand, itemPoint - look * Vector3.forward * 0.05f, -transform.up);
                else ArmIK.Reach(rightUpper, rightLower, rightHand, itemPoint - look * Vector3.forward * 0.05f, -transform.up);
            }
            // Seated players rest their hands; what they carry is not waved in front of their face.
            if (heldVisual && heldVisual.activeSelf == Sitting) heldVisual.SetActive(!Sitting);
            if (heldVisual) heldVisual.transform.SetPositionAndRotation(itemPoint,
                Quaternion.FromToRotation(heldVisual.transform.rotation * heldThinAxis, look * Quaternion.Euler(-35f, 0f, 0f) * Vector3.back) * heldVisual.transform.rotation);
            var camera = Camera.main;
            float top = (head ? head.position.y - transform.position.y : 1.6f) + 0.5f;
            label.transform.position = transform.position + Vector3.up * top;
            talkIcon.Place(head ? head.position : transform.position + Vector3.up * 1.6f);
            talkIcon.SetTalking(Speaking);
            if (camera)
            {
                // Hide the name at range so it does not break the horror atmosphere.
                float distance = Vector3.Distance(camera.transform.position, transform.position);
                // The name is drawn on top of everything, so hide it whenever a wall or door is between us.
                if (Time.unscaledTime >= nextLabelCheck)
                {
                    nextLabelCheck = Time.unscaledTime + 0.1f;
                    var from = camera.transform.position; var to = label.transform.position - Vector3.up * 0.3f;
                    var direction = to - from; float length = direction.magnitude;
                    labelBlocked = length > 0.8f && Physics.Raycast(from + direction / length * 0.4f, direction, length - 0.6f, ~0, QueryTriggerInteraction.Ignore);
                }
                label.gameObject.SetActive(distance < 12f && !labelBlocked && Time.unscaledTime >= anchoredUntil);
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - camera.transform.position);
            }
        }

        float nextLabelCheck;
        bool labelBlocked;
        public bool LabelShown => label && label.gameObject.activeSelf;
        GameObject heldVisual;
        public GameObject HeldVisual => heldVisual;
        Vector3 heldThinAxis = Vector3.up;
        public string HeldInfo
        {
            get
            {
                if (!heldVisual) return "";
                var renderer = heldVisual.GetComponentInChildren<Renderer>();
                var camera = Camera.main;
                var lookNow = Quaternion.Euler(0f, bodyYaw + headYaw, 0f);
                return "root=" + transform.position.ToString("F2") + " hand=" + (rightHand ? rightHand.position.ToString("F2") : "none") + " bodyYaw=" + bodyYaw.ToString("F0") + " headYaw=" + headYaw.ToString("F0") + " lookFwd=" + (lookNow * Vector3.forward).ToString("F2") + " modelFwd=" + body.forward.ToString("F2") + " " + heldVisual.name + " at=" + heldVisual.transform.position.ToString("F2") + " layer=" + (renderer ? LayerMask.LayerToName(renderer.gameObject.layer) : "-")
                    + " visible=" + (renderer && renderer.isVisible) + " cullMask=" + (camera ? (camera.cullingMask & (1 << (renderer ? renderer.gameObject.layer : 0))) != 0 : false)
                    + " camDist=" + (camera ? Vector3.Distance(camera.transform.position, heldVisual.transform.position).ToString("F2") : "-")
                    + " all=[" + string.Join("; ", System.Array.ConvertAll(heldVisual.GetComponentsInChildren<Renderer>(true), r => r.name + "/" + r.GetType().Name + " en=" + r.enabled + " act=" + r.gameObject.activeInHierarchy + " vis=" + r.isVisible + " sh=" + (r.sharedMaterial ? r.sharedMaterial.shader.name : "none") + " shadow=" + r.shadowCastingMode + " forceOff=" + r.forceRenderingOff + " rlm=" + r.renderingLayerMask + " color=" + (r.sharedMaterial && r.sharedMaterial.HasProperty("_Color") ? r.sharedMaterial.color.ToString("F2") : "-") + " mode=" + (r.sharedMaterial && r.sharedMaterial.HasProperty("_Mode") ? r.sharedMaterial.GetFloat("_Mode").ToString() : "-") + " b=" + r.bounds.size.ToString("F2") + " mesh=" + (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh ? mf.sharedMesh.name : "none"))) + "]";
            }
        }

        // Visual-only copy of the item the partner carries, placed in the avatar's right hand.
        public void SetHeldItem(GameObject source)
        {
            if (heldVisual) Destroy(heldVisual);
            Plugin.Log("AVATAR_HELD_SET " + (source ? source.name : "none"));
            if (!source) return;
            var holder = new GameObject("Coop held holder");
            holder.SetActive(false);
            heldVisual = Instantiate(source, holder.transform);
            // Particles and sounds stay so the item's effects (bear spray) can be played on the copy.
            for (int pass = 0; pass < 3; pass++)
                foreach (var component in heldVisual.GetComponentsInChildren<Component>(true))
                    if (!(component is Transform || component is MeshFilter || component is MeshRenderer || component is ParticleSystem || component is ParticleSystemRenderer || component is AudioSource))
                        try { DestroyImmediate(component); } catch { }
            foreach (var particles in heldVisual.GetComponentsInChildren<ParticleSystem>(true)) { var main = particles.main; main.playOnAwake = false; particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            foreach (var audio in heldVisual.GetComponentsInChildren<AudioSource>(true))
            {
                audio.playOnAwake = false; audio.Stop();
                audio.spatialBlend = 1f; audio.rolloffMode = AudioRolloffMode.Linear; audio.minDistance = 1.5f; audio.maxDistance = 15f; audio.dopplerLevel = 0f;
            }
            var anchor = rightHand ? rightHand : transform;
            heldVisual.transform.SetParent(anchor, false);
            foreach (var t in heldVisual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = anchor.gameObject.layer;
            heldVisual.transform.localPosition = rightHand ? new Vector3(0f, -0.06f, 0.08f) : new Vector3(0.25f, 1.1f, 0.3f);
            heldVisual.transform.localRotation = Quaternion.identity;
            var lossy = source.transform.lossyScale; var parentScale = anchor.lossyScale;
            heldVisual.transform.localScale = new Vector3(lossy.x / parentScale.x, lossy.y / parentScale.y, lossy.z / parentScale.z);
            heldVisual.SetActive(true);
            Destroy(holder);
            heldThinAxis = ThinAxis(heldVisual.transform);
            var renderers = heldVisual.GetComponentsInChildren<Renderer>(true);
            Plugin.Log("AVATAR_HELD " + source.name + " renderers=" + renderers.Length + " active=" + heldVisual.activeInHierarchy + " at=" + heldVisual.transform.position.ToString("F2") + " scale=" + heldVisual.transform.lossyScale.ToString("F3") + (renderers.Length > 0 ? " bounds=" + renderers[0].bounds.size.ToString("F3") + " enabled=" + renderers[0].enabled : ""));
        }

        float nextTorchTry;
        // Lab: the torch in the partner's hand, as drawn.
        public string TorchInfo => !torch ? (torchInHand ? "missing" : "none") : (torch.activeInHierarchy ? "shown" : "hidden") + torch.GetComponentsInChildren<MeshRenderer>().Length;
        public void SetTorch(bool inHand)
        {
            torchInHand = inHand;
            if (inHand && !torch && Time.unscaledTime >= nextTorchTry)
            {
                nextTorchTry = Time.unscaledTime + 1f;
                torch = TorchModel.Create(transform);
                if (torch) torchLongAxis = Axis(torch.transform, true);
            }
            if (torch) torch.SetActive(inHand);
        }

        static Vector3 Axis(Transform root, bool longest)
        {
            var thin = ThinAxis(root);
            if (!longest) return thin;
            var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = root.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local); max = Vector3.Max(max, local);
                }
            }
            var size = max - min;
            if (float.IsInfinity(size.x)) return Vector3.forward;
            return size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        }

        // Thinnest axis of the item in its root space, from the meshes' own bounds.
        static Vector3 ThinAxis(Transform root)
        {
            var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = root.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local); max = Vector3.Max(max, local);
                }
            }
            var size = max - min;
            if (float.IsInfinity(size.x)) return Vector3.up;
            return size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
