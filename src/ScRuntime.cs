using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace IronbarkCoop
{
    // Builds the partner's body inside Ironbark from local packs: Scratch Creek's survivors (from the player's own
    // Scratch Creek install, when there is one) and Ironbark's own people (from the game itself, always). Both are
    // read once on first run and cached on this computer; nothing is downloaded or shipped.
    internal static class ScRuntime
    {
        // Scratch Creek's pack (null without Scratch Creek) and Ironbark's own people.
        public static ScPack Pack { get; private set; }
        public static ScPack IronbarkPack { get; private set; }
        public static string State { get; private set; } = "not started";
        static readonly Dictionary<(ScPack, int), Texture2D> textures = new Dictionary<(ScPack, int), Texture2D>();
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        static volatile ScPack loaded, loadedIronbark;
        static volatile bool finished;
        static bool started;

        public static string CachePath => Path.Combine(BepInEx.Paths.CachePath, "IronbarkCoop", "scratch-creek-" + ScPack.FormatVersion + ".bin");
        static string IronbarkCachePath => Path.Combine(BepInEx.Paths.CachePath, "IronbarkCoop", "ironbark-" + ScPack.FormatVersion + ".bin");
        static string PluginDir => Path.GetDirectoryName(typeof(ScRuntime).Assembly.Location);

        // Every look a player can pick: id, name, female, from Scratch Creek.
        internal sealed class Look { public string Id, Russian, English; public bool Female, ScratchCreek; }
        internal static readonly Look[] AllLooks = {
            new Look { Id = "marcus", Russian = "Маркус", English = "Marcus", ScratchCreek = true },
            new Look { Id = "tessa", Russian = "Тесса", English = "Tessa", Female = true, ScratchCreek = true },
            new Look { Id = "ib-ranger", Russian = "Смотритель", English = "Ranger" },
            new Look { Id = "ib-waitress", Russian = "Официантка", English = "Waitress", Female = true },
            new Look { Id = "ib-suitguy", Russian = "Мужчина в костюме", English = "Man in a suit" } };

        // Starts loading (or extracting) on a worker thread; Unity objects are built later on the main thread.
        public static void Begin(string configuredPath)
        {
            if (started) return;
            started = true;
            string ironbarkData = Application.dataPath;
            new Thread(() =>
            {
                try { loadedIronbark = LoadPack(IronbarkCachePath, () => ScExtractor.ExtractIronbark(ironbarkData, Path.Combine(PluginDir, "classdata.tpk"), message => Plugin.LogFromThread("IB " + message))); }
                catch (Exception ex) { Plugin.LogFromThread("IB_PACK_FAILED " + ex); }
                try
                {
                    string data = FindScratchCreek(configuredPath);
                    if (configuredPath != "none" && (data != null || File.Exists(CachePath)))
                        loaded = LoadPack(CachePath, () => ScExtractor.Extract(data, Path.Combine(PluginDir, "classdata.tpk"), message => Plugin.LogFromThread("SC " + message)));
                    State = loaded != null ? "Scratch Creek + Ironbark people" : "Ironbark people (no Scratch Creek)";
                }
                catch (Exception ex) { State = "Scratch Creek failed: " + ex.Message; Plugin.LogFromThread("SC_FAILED " + ex); }
                finished = true;
            }) { IsBackground = true, Name = "IronbarkCoop character import" }.Start();
        }

        static ScPack LoadPack(string cache, Func<ScPack> extract)
        {
            if (File.Exists(cache)) using (var fs = File.OpenRead(cache)) return ScPack.Read(fs);
            var pack = extract();
            Directory.CreateDirectory(Path.GetDirectoryName(cache));
            using (var fs = File.Create(cache)) pack.Write(fs);
            return pack;
        }

        public static bool Ready
        {
            get
            {
                if (finished && Pack == null && IronbarkPack == null && (loaded != null || loadedIronbark != null))
                {
                    Pack = loaded;
                    IronbarkPack = loadedIronbark;
                    var icon = Pack != null ? Pack.Textures.FindIndex(t => t.Name == "talking-icon") : -1;
                    if (icon >= 0)
                    {
                        var texture = Texture(Pack, icon);
                        if (texture) TalkingIcon.IconSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
                    }
                    Plugin.Log("SC_READY " + State + " scratchCreek=" + (Pack != null ? Pack.Characters.Count + "/" + Pack.Clips.Count : "-")
                        + " ironbark=" + (IronbarkPack != null ? IronbarkPack.Characters.Count + "/" + IronbarkPack.Clips.Count : "-"));
                }
                return Pack != null || IronbarkPack != null;
            }
        }

        public static bool HasScratchCreek => Ready && Pack != null && Pack.Find("marcus") != null;

        // Looks this player can show (in the pick order).
        public static List<Look> Available()
        {
            var list = new List<Look>();
            if (!Ready) return list;
            foreach (var look in AllLooks)
                if ((look.ScratchCreek ? Pack : IronbarkPack)?.Find(look.Id) != null) list.Add(look);
            return list;
        }

        public static Look Info(string id) => Array.Find(AllLooks, l => l.Id == id);

        // The look to build for a player: what they picked if this computer has it, otherwise a look of the same kind
        // that it has (Scratch Creek's survivors first). "auto"/null follows the story role.
        public static string ResolveLook(string look, string role)
        {
            var available = Available();
            if (!string.IsNullOrEmpty(look) && look != "auto" && available.Exists(l => l.Id == look)) return look;
            bool female = !string.IsNullOrEmpty(look) && look != "auto" && Info(look) != null ? Info(look).Female : role == "tessa";
            foreach (var id in female ? new[] { "tessa", "ib-waitress" } : new[] { "marcus", "ib-ranger", "ib-suitguy" })
                if (available.Exists(l => l.Id == id)) return id;
            return available.Count > 0 ? available[0].Id : null;
        }

        public static bool IsFemale(string look) => Info(look)?.Female ?? false;

        // A clip by Scratch Creek name: Scratch Creek's own when the player has it (it animates any humanoid),
        // otherwise Ironbark's stand-in under the same name.
        public static ScClip Clip(string name) => Pack?.Clip(name) ?? IronbarkPack?.Clip(name);

        static string FindScratchCreek(string configured)
        {
            const string folder = "Fears to Fathom - Scratch Creek", data = "Fears to Fathom - Scratch Creek_Data";
            // "none": play with Ironbark's own people even when Scratch Creek is installed.
            if (configured == "none") return null;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(configured)) { candidates.Add(configured); candidates.Add(Path.Combine(configured, data)); }
            try
            {
                {
                    // Registry is in the game's Mono runtime but not in netstandard's compile surface.
                    var registry = Type.GetType("Microsoft.Win32.Registry, mscorlib");
                    var steam = registry?.GetMethod("GetValue", new[] { typeof(string), typeof(string), typeof(object) })
                        ?.Invoke(null, new object[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null }) as string;
                    if (steam != null)
                    {
                        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                        if (File.Exists(vdf))
                            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                                candidates.Add(Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", folder, data));
                    }
                }
            }
            catch { }
            foreach (var drive in DriveInfo.GetDrives())
                candidates.Add(Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common", folder, data));
            foreach (var path in candidates)
                if (File.Exists(Path.Combine(path, "level1")) && File.Exists(Path.Combine(path, "sharedassets0.assets"))) return path;
            return null;
        }

        public static Texture2D Texture(ScPack pack, int index)
        {
            if (index < 0 || pack == null || index >= pack.Textures.Count) return null;
            if (textures.TryGetValue((pack, index), out var cached)) return cached;
            var t = pack.Textures[index];
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(t.Width, t.Height, (TextureFormat)t.Format, t.MipCount > 1, t.Linear) { name = "SC " + t.Name, wrapMode = TextureWrapMode.Repeat };
                texture.LoadRawTextureData(t.Data);
                texture.Apply(false, true);
            }
            catch (Exception ex) { Plugin.Log("SC_TEXTURE_FAILED " + t.Name + " " + ex.Message); }
            textures[(pack, index)] = texture;
            return texture;
        }

        // A hidden, ready-to-clone body for a look id; null when this computer does not have it.
        public static GameObject Template(string id)
        {
            if (!Ready || string.IsNullOrEmpty(id)) return null;
            if (templates.TryGetValue(id, out var existing) && existing) return existing;
            var info = Info(id);
            var pack = info != null && !info.ScratchCreek ? IronbarkPack : Pack;
            var character = pack?.Find(id);
            if (character == null) return null;
            try
            {
                var root = Build(character, pack);
                root.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(root);
                templates[id] = root;
                return root;
            }
            catch (Exception ex) { Plugin.Log("SC_BUILD_FAILED " + id + " " + ex); return null; }
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
        static Quaternion Q(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);

        static GameObject Build(ScCharacter character, ScPack pack)
        {
            var bones = new Transform[character.Bones.Count];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = character.Bones[i];
                bones[i] = new GameObject(b.Name).transform;
                if (b.Parent >= 0) bones[i].SetParent(bones[b.Parent], false);
                bones[i].localPosition = i == 0 ? Vector3.zero : V(b.LocalPosition);
                bones[i].localRotation = i == 0 ? Quaternion.identity : Q(b.LocalRotation);
                bones[i].localScale = V(b.LocalScale);
            }
            var root = bones[0].gameObject;
            root.name = "SC " + character.DisplayName;
            foreach (var m in character.Meshes) Renderer(m, bones, pack);
            var animator = root.AddComponent<Animator>();
            animator.avatar = Avatar(character, root, bones);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Plugin.Log($"SC_BUILT {character.DisplayName} avatar valid={animator.avatar && animator.avatar.isValid} human={animator.avatar && animator.avatar.isHuman}");
            return root;
        }

        static void Renderer(ScMesh m, Transform[] bones, ScPack pack)
        {
            var mesh = new Mesh { name = "SC " + m.Name };
            var layout = new List<VertexAttributeDescriptor>();
            var streamStride = new int[4];
            for (int i = 0; i < m.Channels.Count; i++)
            {
                var c = m.Channels[i];
                if (c.Dimension == 0) continue;
                layout.Add(new VertexAttributeDescriptor((VertexAttribute)i, (VertexAttributeFormat)c.Format, c.Dimension, c.Stream));
                streamStride[c.Stream] = Math.Max(streamStride[c.Stream], c.Offset + FormatSize(c.Format) * c.Dimension);
            }
            mesh.SetVertexBufferParams(m.VertexCount, layout.ToArray());
            // Streams follow each other in the serialized buffer, each starting on a 16-byte boundary.
            int offset = 0;
            for (int s = 0; s < 4; s++)
            {
                if (streamStride[s] == 0) continue;
                int stride = (streamStride[s] + 3) & ~3;
                int length = stride * m.VertexCount;
                mesh.SetVertexBufferData(m.VertexData, offset, 0, length, s, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                offset = (offset + length + 15) & ~15;
            }
            bool wide = m.IndexFormat == 1;
            int indexCount = m.IndexData.Length / (wide ? 4 : 2);
            mesh.SetIndexBufferParams(indexCount, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
            if (wide) { var indices = new int[indexCount]; Buffer.BlockCopy(m.IndexData, 0, indices, 0, m.IndexData.Length); mesh.SetIndexBufferData(indices, 0, 0, indexCount); }
            else { var indices = new ushort[indexCount]; Buffer.BlockCopy(m.IndexData, 0, indices, 0, m.IndexData.Length); mesh.SetIndexBufferData(indices, 0, 0, indexCount); }
            mesh.subMeshCount = m.SubMeshes.Count;
            for (int i = 0; i < m.SubMeshes.Count; i++)
            {
                var s = m.SubMeshes[i];
                mesh.SetSubMesh(i, new SubMeshDescriptor(s.FirstByte / (wide ? 4 : 2), s.IndexCount) { baseVertex = s.BaseVertex, firstVertex = s.FirstVertex, vertexCount = s.VertexCount });
            }
            var bind = new Matrix4x4[m.BindPoses.Count];
            for (int i = 0; i < bind.Length; i++)
            {
                var p = m.BindPoses[i];
                var matrix = new Matrix4x4();
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) matrix[r, c] = p[r * 4 + c];
                bind[i] = matrix;
            }
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            var holder = new GameObject(m.Name);
            holder.transform.SetParent(bones[0], false);
            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = Array.ConvertAll(m.BoneIndices, i => i >= 0 ? bones[i] : bones[0]);
            renderer.rootBone = m.RootBone >= 0 ? bones[m.RootBone] : bones[0];
            renderer.updateWhenOffscreen = true;
            var standard = Shader.Find("Standard");
            renderer.sharedMaterials = m.Materials.ConvertAll(x =>
            {
                var material = new Material(standard) { name = "SC " + x.Name };
                var main = Texture(pack, x.MainTexture);
                if (main) material.mainTexture = main;
                material.color = new Color(x.Color[0], x.Color[1], x.Color[2], x.Color[3]);
                material.SetFloat("_Glossiness", x.Glossiness);
                material.SetFloat("_Metallic", x.Metallic);
                return material;
            }).ToArray();
        }

        static int FormatSize(int format)
        {
            switch ((VertexAttributeFormat)format)
            {
                case VertexAttributeFormat.Float32: case VertexAttributeFormat.UInt32: case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16: case VertexAttributeFormat.UNorm16: case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16: case VertexAttributeFormat.SInt16: return 2;
                default: return 1;
            }
        }

        static Avatar Avatar(ScCharacter character, GameObject root, Transform[] bones)
        {
            var human = new List<HumanBone>();
            foreach (var pair in character.HumanBones)
                human.Add(new HumanBone { humanName = pair.Key, boneName = pair.Value, limit = new HumanLimit { useDefaultValues = true } });
            var skeleton = new List<SkeletonBone>();
            for (int i = 0; i < bones.Length; i++)
            {
                var b = character.Bones[i];
                bool reference = b.TPosePosition != null && i > 0;
                skeleton.Add(new SkeletonBone {
                    name = bones[i].name,
                    position = reference ? V(b.TPosePosition) : bones[i].localPosition,
                    rotation = reference ? Q(b.TPoseRotation) : bones[i].localRotation,
                    scale = bones[i].localScale });
            }
            var description = new HumanDescription {
                human = human.ToArray(), skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false };
            var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            avatar.name = "SC " + character.DisplayName + " avatar";
            return avatar;
        }
    }

    // Plays Scratch Creek's player animations through Unity's humanoid muscle space, blending locomotion states
    // like Scratch Creek's PlayerVisual (idle, walk, tired run, crouch, crouch walk, sitting).
    internal sealed class ScPoseAnimator
    {
        public enum Motion { Idle, Walk, Run, Crouch, CrouchWalk, Sit, Lie }
        readonly HumanPoseHandler handler;
        readonly Dictionary<Motion, ScClip> clips = new Dictionary<Motion, ScClip>();
        readonly Dictionary<Motion, float> weights = new Dictionary<Motion, float>();
        readonly Dictionary<Motion, float> times = new Dictionary<Motion, float>();
        readonly Dictionary<ScClip, float> yawFix = new Dictionary<ScClip, float>();
        HumanPose pose;
        readonly float[] muscles = new float[95];
        readonly float[] upright = new float[95];

        public ScPoseAnimator(Animator animator)
        {
            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            handler.GetHumanPose(ref pose);
            Bind(Motion.Idle, "Idle_Stance_01");
            Bind(Motion.Walk, "Walk Player");
            Bind(Motion.Run, "Tired Run Player");
            Bind(Motion.Crouch, "CrouchFixed");
            Bind(Motion.CrouchWalk, "Crouch Walk Forward");
            Bind(Motion.Sit, "sit-dozeoff-e");
            foreach (Motion m in Enum.GetValues(typeof(Motion))) { weights[m] = m == Motion.Idle ? 1f : 0f; times[m] = 0f; }
        }

        internal void Bind(Motion motion, string name) => Bind(motion, ScRuntime.Clip(name));
        public bool Has(Motion motion) => clips.ContainsKey(motion);

        // A clip recorded from another humanoid (muscle space is shared between humanoid avatars).
        public void Bind(Motion motion, ScClip clip)
        {
            if (clip == null) return;
            clips[motion] = clip;
            // Clips may face any yaw at their first frame; play them facing the avatar's forward.
            var q = new Quaternion(clip.Data[3], clip.Data[4], clip.Data[5], clip.Data[6]);
            yawFix[clip] = q.eulerAngles.y;
        }

        // speed in m/s along the ground; crouch/sit from the partner's state.
        public void Update(float dt, float speed, bool crouch, bool sit, bool lie = false)
        {
            Motion target = lie && clips.ContainsKey(Motion.Lie) ? Motion.Lie : sit ? Motion.Sit : crouch ? (speed > 0.3f ? Motion.CrouchWalk : Motion.Crouch)
                : speed > 3.6f ? Motion.Run : speed > 0.3f ? Motion.Walk : Motion.Idle;
            foreach (Motion m in Enum.GetValues(typeof(Motion)))
                weights[m] = Mathf.MoveTowards(weights[m], m == target ? 1f : 0f, dt * 6f);
            // Locomotion cycles play in step with the actual ground speed (Scratch Creek speedFactor).
            float walkRate = Mathf.Clamp(speed / 1.45f, 0.5f, 2f), runRate = Mathf.Clamp(speed / 4.5f, 0.7f, 1.5f), crouchRate = Mathf.Clamp(speed / 1f, 0.5f, 1.8f);
            Advance(Motion.Idle, dt); Advance(Motion.Crouch, dt); Advance(Motion.Sit, dt); Advance(Motion.Lie, dt);
            Advance(Motion.Walk, dt * walkRate); Advance(Motion.Run, dt * runRate); Advance(Motion.CrouchWalk, dt * crouchRate);

            float total = 0f; Vector3 body = Vector3.zero; Vector4 rotation = Vector4.zero;
            Array.Clear(muscles, 0, muscles.Length);
            foreach (var pair in clips)
            {
                float w = weights[pair.Key];
                if (w <= 0.001f) continue;
                Sample(pair.Value, times[pair.Key], out var p, out var q, muscles, w);
                body += p * w;
                if (rotation != Vector4.zero && Vector4.Dot(rotation, new Vector4(q.x, q.y, q.z, q.w)) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rotation += new Vector4(q.x, q.y, q.z, q.w) * w;
                total += w;
            }
            if (total <= 0f) return;
            for (int i = 0; i < muscles.Length; i++) muscles[i] /= total;
            body /= total;
            // The only seated clip dozes off (back rounded, head down): sit with the standing idle's upright back,
            // neck and head (muscles 0-14: spine, chest, upper chest, neck, head) instead.
            float sitWeight = weights[Motion.Sit];
            if (sitWeight > 0.001f && clips.TryGetValue(Motion.Idle, out var idle))
            {
                Array.Clear(upright, 0, upright.Length);
                Sample(idle, times[Motion.Idle], out _, out _, upright, 1f);
                for (int i = 0; i <= 14; i++) muscles[i] = Mathf.Lerp(muscles[i], upright[i], sitWeight);
            }
            rotation.Normalize();
            // In place: the root transform carries the partner's motion; keep only the body height.
            pose.bodyPosition = new Vector3(0f, body.y, 0f);
            pose.bodyRotation = new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
            if (pose.muscles == null || pose.muscles.Length != muscles.Length) pose.muscles = new float[muscles.Length];
            Array.Copy(muscles, pose.muscles, muscles.Length);
            handler.SetHumanPose(ref pose);
        }

        void Advance(Motion m, float dt)
        {
            if (!clips.TryGetValue(m, out var clip)) return;
            float t = times[m] + dt;
            times[m] = clip.Loop ? Mathf.Repeat(t, clip.Length) : Mathf.Min(t, clip.Length);
        }

        void Sample(ScClip clip, float time, out Vector3 body, out Quaternion rotation, float[] accumulate, float weight)
        {
            float frame = Mathf.Clamp(time * clip.SampleRate, 0f, clip.FrameCount - 1);
            int a = (int)frame, b = Mathf.Min(a + 1, clip.FrameCount - 1);
            float u = frame - a;
            int oa = a * ScClip.Stride, ob = b * ScClip.Stride;
            var d = clip.Data;
            body = Vector3.Lerp(new Vector3(d[oa], d[oa + 1], d[oa + 2]), new Vector3(d[ob], d[ob + 1], d[ob + 2]), u);
            rotation = Quaternion.Slerp(new Quaternion(d[oa + 3], d[oa + 4], d[oa + 5], d[oa + 6]), new Quaternion(d[ob + 3], d[ob + 4], d[ob + 5], d[ob + 6]), u);
            rotation = Quaternion.Euler(0f, -yawFix[clip], 0f) * rotation;
            for (int i = 0; i < 95; i++) accumulate[i] += (d[oa + 7 + i] + (d[ob + 7 + i] - d[oa + 7 + i]) * u) * weight;
        }
    }
}
