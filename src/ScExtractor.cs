using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace IronbarkCoop
{
    // Reads the survivor characters (Marcus, Tessa), the talking icon and the player animations from the user's own
    // Scratch Creek install. Read-only; the result is cached on the user's disk and never redistributed.
    internal static class ScExtractor
    {
        // Unity's internal human bone order (Avatar.m_HumanBoneIndex) and hand bone order, as HumanTrait names.
        static readonly string[] BodyBones = {
            "Hips", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot", "Spine", "Chest", "UpperChest",
            "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
            "LeftToes", "RightToes", "LeftEye", "RightEye", "Jaw" };
        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        static readonly string[] Phalanges = { "Proximal", "Intermediate", "Distal" };
        public static readonly string[] ClipNames = {
            "Idle_Stance_01", "Walk Player", "Tired Run Player", "CrouchFixed", "Crouch Walk Forward", "Jump", "Looking Marcus",
            "sit-dozeoff-e", "Talking", "Look Around", "Looking Behind", "StandingUp", "Laying Sleeping", "sleeping tessa" };
        const int RootT = 7, RootQ = 10, Muscles = 42, MuscleCount = 95;

        sealed class Context
        {
            public AssetsManager Manager;
            public string DataDir;
            public ScPack Pack;
            public Dictionary<string, int> Textures = new Dictionary<string, int>();
            public Action<string> Log;
        }

        public static ScPack Extract(string dataDir, string classPackage, Action<string> log)
        {
            var ctx = new Context { Manager = new AssetsManager(), DataDir = dataDir, Pack = new ScPack { Source = dataDir }, Log = log ?? (_ => { }) };
            var manager = ctx.Manager;
            manager.LoadClassPackage(classPackage);
            var level = manager.LoadAssetsFile(Path.Combine(dataDir, "level1"), true);
            manager.LoadClassDatabaseFromPackage(level.file.Metadata.UnityVersion);
            try
            {
                // The survivor selection scene holds both survivors fully rigged and textured.
                var marcus = FindGameObject(ctx, level, "MarcusReed");
                var tessa = FindGameObject(ctx, level, "Female");
                if (marcus != null) ctx.Pack.Characters.Add(Character(ctx, level, marcus, "marcus", "Marcus"));
                if (tessa != null) ctx.Pack.Characters.Add(Character(ctx, level, tessa, "tessa", "Tessa"));
                var shared = manager.LoadAssetsFile(Path.Combine(dataDir, "sharedassets0.assets"), true);
                foreach (var name in ClipNames)
                {
                    var info = FindByName(ctx, shared, AssetClassID.AnimationClip, name);
                    if (info == null) { ctx.Log("clip missing: " + name); continue; }
                    ctx.Pack.Clips.Add(Clip(manager.GetBaseField(shared, info)));
                }
                var icon = FindByName(ctx, shared, AssetClassID.Texture2D, "conversation quotation-mark");
                if (icon != null) TextureIndex(ctx, shared, icon.PathId, "talking-icon");
            }
            finally { manager.UnloadAll(true); }
            return ctx.Pack;
        }

        // Players without Scratch Creek: stand-ins read from Ironbark Lookout itself (every player owns it). The game's
        // own people: the waitress, the park ranger and the suited customer from the diner (all full Mixamo rigs).
        public static readonly (string level, string gameObject, string id, string display)[] IronbarkPeople = {
            ("level4", "Waitress", "ib-waitress", "Waitress"), ("level5", "ParkRanger", "ib-ranger", "Ranger"), ("level4", "SuitGuy", "ib-suitguy", "Suit guy") };
        // Ironbark's humanoid clips, stored under the Scratch Creek names the animator binds.
        static readonly (string file, string clip, string asName)[] IronbarkClips = {
            ("sharedassets1.assets", "Idle", "Idle_Stance_01"), ("sharedassets1.assets", "Standard Walk", "Walk Player"),
            ("sharedassets6.assets", "run", "Tired Run Player"), ("sharedassets5.assets", "Typing", "sit-dozeoff-e") };

        public static ScPack ExtractIronbark(string dataDir, string classPackage, Action<string> log)
        {
            var ctx = new Context { Manager = new AssetsManager(), DataDir = dataDir, Pack = new ScPack { Source = "ironbark:" + dataDir }, Log = log ?? (_ => { }) };
            var manager = ctx.Manager;
            manager.LoadClassPackage(classPackage);
            try
            {
                foreach (var (levelName, goName, id, display) in IronbarkPeople)
                {
                    var level = manager.LoadAssetsFile(Path.Combine(dataDir, levelName), true);
                    if (manager.ClassDatabase == null) manager.LoadClassDatabaseFromPackage(level.file.Metadata.UnityVersion);
                    var go = FindGameObject(ctx, level, goName);
                    if (go == null) { ctx.Log("character missing: " + goName); continue; }
                    ctx.Pack.Characters.Add(Character(ctx, level, go, id, display));
                }
                foreach (var (file, clipName, asName) in IronbarkClips)
                {
                    var inst = manager.LoadAssetsFile(Path.Combine(dataDir, file), true);
                    var info = FindByName(ctx, inst, AssetClassID.AnimationClip, clipName);
                    if (info == null) { ctx.Log("clip missing: " + clipName); continue; }
                    var clip = Clip(manager.GetBaseField(inst, info));
                    clip.Name = asName;
                    ctx.Pack.Clips.Add(clip);
                }
                Synthesize(ctx.Pack);
            }
            finally { manager.UnloadAll(true); }
            return ctx.Pack;
        }

        // Muscle indices (Unity's HumanTrait.MuscleName order).
        const int SpineFB = 0, ChestFB = 3, LUpperLegFB = 21, LLowerLeg = 24, LFoot = 26, RUpperLegFB = 29, RLowerLeg = 32, RFoot = 34;

        // Ironbark has no crouch or sleeping clips: crouches are the idle and the walk with bent knees and a lower body;
        // sleeping is the idle laid on its back.
        static void Synthesize(ScPack pack)
        {
            var idle = pack.Clip("Idle_Stance_01");
            var walk = pack.Clip("Walk Player");
            if (idle != null) pack.Clips.Add(Bent(idle, "CrouchFixed"));
            if (walk != null) pack.Clips.Add(Bent(walk, "Crouch Walk Forward"));
            if (idle != null) { pack.Clips.Add(Laid(idle, "Laying Sleeping")); pack.Clips.Add(Laid(idle, "sleeping tessa")); }
        }

        static ScClip Copy(ScClip source, string name) => new ScClip {
            Name = name, Length = source.Length, SampleRate = source.SampleRate, Loop = true, FrameCount = source.FrameCount, Data = (float[])source.Data.Clone() };

        static ScClip Bent(ScClip source, string name)
        {
            var clip = Copy(source, name);
            for (int f = 0; f < clip.FrameCount; f++)
            {
                int o = f * ScClip.Stride, m = o + 7;
                clip.Data[o + 1] -= 0.3f;
                clip.Data[m + SpineFB] += 0.25f; clip.Data[m + ChestFB] += 0.15f;
                foreach (var (upper, lower, foot) in new[] { (LUpperLegFB, LLowerLeg, LFoot), (RUpperLegFB, RLowerLeg, RFoot) })
                {
                    clip.Data[m + upper] = Math.Min(1f, clip.Data[m + upper] + 0.65f);
                    clip.Data[m + lower] = Math.Max(-1f, clip.Data[m + lower] - 0.9f);
                    clip.Data[m + foot] = Math.Min(1f, clip.Data[m + foot] + 0.35f);
                }
            }
            return clip;
        }

        static ScClip Laid(ScClip source, string name)
        {
            var clip = Copy(source, name);
            // Body turned 90 degrees back about its side axis (face up), legs and spine straight.
            float s = (float)Math.Sqrt(0.5);
            for (int f = 0; f < clip.FrameCount; f++)
            {
                int o = f * ScClip.Stride, m = o + 7;
                float x = clip.Data[o + 3], y = clip.Data[o + 4], z = clip.Data[o + 5], w = clip.Data[o + 6];
                // (-90 deg about X) * q
                float qx = -s, qw = s;
                clip.Data[o + 3] = qw * x + qx * w;
                clip.Data[o + 4] = qw * y - qx * z;
                clip.Data[o + 5] = qw * z + qx * y;
                clip.Data[o + 6] = qw * w - qx * x;
                clip.Data[m + SpineFB] = 0f; clip.Data[m + ChestFB] = 0f;
                clip.Data[m + LUpperLegFB] = 0f; clip.Data[m + RUpperLegFB] = 0f;
                clip.Data[m + LLowerLeg] = 1f; clip.Data[m + RLowerLeg] = 1f;
                clip.Data[o + 1] = 0.25f;
            }
            return clip;
        }

        static AssetFileInfo FindByName(Context ctx, AssetsFileInstance inst, AssetClassID type, string name)
        {
            foreach (var info in inst.file.GetAssetsOfType(type))
                if (AssetHelper.GetAssetNameFast(inst.file, ctx.Manager.ClassDatabase, info) == name) return info;
            return null;
        }

        static AssetTypeValueField FindGameObject(Context ctx, AssetsFileInstance inst, string name)
        {
            // First instance whose skinned mesh is enabled (the selection scene contains several copies).
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.GameObject))
            {
                if (AssetHelper.GetAssetNameFast(inst.file, ctx.Manager.ClassDatabase, info) != name) continue;
                return ctx.Manager.GetBaseField(inst, info);
            }
            return null;
        }

        static ScCharacter Character(Context ctx, AssetsFileInstance inst, AssetTypeValueField root, string id, string display)
        {
            var character = new ScCharacter { Id = id, DisplayName = display };
            var byTransform = new Dictionary<long, int>();
            var nodes = new List<(AssetTypeValueField go, AssetTypeValueField transform)>();
            Walk(ctx, inst, root, -1, character, byTransform, nodes);
            AssetTypeValueField animator = null;
            foreach (var (go, _) in nodes)
                foreach (var pair in go["m_Component.Array"].Children)
                {
                    var ext = ctx.Manager.GetExtAsset(inst, pair["component"]);
                    if (ext.info == null) continue;
                    if (ext.info.TypeId == (int)AssetClassID.Animator && animator == null) animator = ext.baseField;
                    if (ext.info.TypeId == (int)AssetClassID.SkinnedMeshRenderer && ext.baseField["m_Enabled"].AsBool)
                        character.Meshes.Add(Mesh(ctx, inst, ext.baseField, byTransform));
                }
            if (animator != null) Avatar(ctx, inst, animator["m_Avatar"], character);
            ctx.Log($"{display}: {character.Bones.Count} bones, {character.Meshes.Count} meshes, {character.HumanBones.Count} human bones");
            return character;
        }

        static void Walk(Context ctx, AssetsFileInstance inst, AssetTypeValueField go, int parent, ScCharacter character,
            Dictionary<long, int> byTransform, List<(AssetTypeValueField, AssetTypeValueField)> nodes)
        {
            AssetTypeValueField transform = null; long transformPath = 0;
            foreach (var pair in go["m_Component.Array"].Children)
            {
                var ext = ctx.Manager.GetExtAsset(inst, pair["component"]);
                if (ext.info != null && (ext.info.TypeId == (int)AssetClassID.Transform || ext.info.TypeId == (int)AssetClassID.RectTransform))
                { transform = ext.baseField; transformPath = ext.info.PathId; }
            }
            if (transform == null) return;
            int index = character.Bones.Count;
            character.Bones.Add(new ScBone {
                Name = go["m_Name"].AsString, Parent = parent,
                LocalPosition = V3(transform["m_LocalPosition"]), LocalRotation = Q(transform["m_LocalRotation"]), LocalScale = V3(transform["m_LocalScale"]) });
            byTransform[transformPath] = index;
            nodes.Add((go, transform));
            foreach (var child in transform["m_Children.Array"].Children)
            {
                var childTransform = ctx.Manager.GetExtAsset(inst, child);
                if (childTransform.baseField == null) continue;
                var childGo = ctx.Manager.GetExtAsset(inst, childTransform.baseField["m_GameObject"]);
                if (childGo.baseField != null) Walk(ctx, inst, childGo.baseField, index, character, byTransform, nodes);
            }
        }

        static ScMesh Mesh(Context ctx, AssetsFileInstance inst, AssetTypeValueField renderer, Dictionary<long, int> byTransform)
        {
            var meshExt = ctx.Manager.GetExtAsset(inst, renderer["m_Mesh"]);
            var m = meshExt.baseField;
            var mesh = new ScMesh { Name = m["m_Name"].AsString, VertexCount = (int)m["m_VertexData.m_VertexCount"].AsUInt };
            foreach (var c in m["m_VertexData.m_Channels.Array"].Children)
                mesh.Channels.Add(new ScChannel { Stream = c["stream"].AsByte, Offset = c["offset"].AsByte, Format = c["format"].AsByte, Dimension = (byte)(c["dimension"].AsByte & 0xF) });
            mesh.VertexData = m["m_VertexData.m_DataSize"].AsByteArray;
            var stream = m["m_StreamData"];
            if ((mesh.VertexData == null || mesh.VertexData.Length == 0) && !stream.IsDummy && stream["size"].AsUInt > 0)
                mesh.VertexData = ReadStream(ctx, meshExt.file, stream["path"].AsString, (long)stream["offset"].AsULong, (int)stream["size"].AsUInt);
            mesh.IndexFormat = m["m_IndexFormat"].AsInt;
            mesh.IndexData = m["m_IndexBuffer.Array"].AsByteArray;
            foreach (var s in m["m_SubMeshes.Array"].Children)
                mesh.SubMeshes.Add(new ScSubMesh { FirstByte = (int)s["firstByte"].AsUInt, IndexCount = (int)s["indexCount"].AsUInt, BaseVertex = (int)s["baseVertex"].AsUInt, FirstVertex = (int)s["firstVertex"].AsUInt, VertexCount = (int)s["vertexCount"].AsUInt });
            foreach (var p in m["m_BindPose.Array"].Children)
            {
                var matrix = new float[16];
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) matrix[r * 4 + c] = p["e" + r + c].AsFloat;
                mesh.BindPoses.Add(matrix);
            }
            var bones = renderer["m_Bones.Array"].Children;
            mesh.BoneIndices = new int[bones.Count];
            for (int i = 0; i < bones.Count; i++) mesh.BoneIndices[i] = byTransform.TryGetValue(bones[i]["m_PathID"].AsLong, out var b) ? b : -1;
            mesh.RootBone = byTransform.TryGetValue(renderer["m_RootBone.m_PathID"].AsLong, out var root) ? root : -1;
            foreach (var pptr in renderer["m_Materials.Array"].Children)
            {
                var matExt = ctx.Manager.GetExtAsset(inst, pptr);
                var mat = new ScMaterial();
                if (matExt.baseField != null)
                {
                    var f = matExt.baseField;
                    mat.Name = f["m_Name"].AsString;
                    foreach (var tex in f["m_SavedProperties.m_TexEnvs.Array"].Children)
                        if (tex["first"].AsString == "_MainTex" && tex["second.m_Texture.m_PathID"].AsLong != 0)
                        {
                            var texExt = ctx.Manager.GetExtAsset(matExt.file, tex["second.m_Texture"]);
                            if (texExt.info != null) mat.MainTexture = TextureIndex(ctx, texExt.file, texExt.info.PathId, null);
                        }
                    foreach (var color in f["m_SavedProperties.m_Colors.Array"].Children)
                        if (color["first"].AsString == "_Color")
                            mat.Color = new[] { color["second.r"].AsFloat, color["second.g"].AsFloat, color["second.b"].AsFloat, color["second.a"].AsFloat };
                    foreach (var value in f["m_SavedProperties.m_Floats.Array"].Children)
                    {
                        switch (value["first"].AsString)
                        {
                            case "_Glossiness": mat.Glossiness = value["second"].AsFloat; break;
                            case "_Metallic": mat.Metallic = value["second"].AsFloat; break;
                            case "_Cutoff": mat.Cutoff = value["second"].AsFloat; break;
                            case "_Mode": mat.Cutout = value["second"].AsFloat == 1f; break;
                        }
                    }
                }
                mesh.Materials.Add(mat);
            }
            return mesh;
        }

        static int TextureIndex(Context ctx, AssetsFileInstance inst, long pathId, string rename)
        {
            string key = inst.name + "#" + pathId;
            if (ctx.Textures.TryGetValue(key, out var existing)) return existing;
            var t = ctx.Manager.GetBaseField(inst, pathId);
            var texture = new ScTexture {
                Name = rename ?? t["m_Name"].AsString, Format = t["m_TextureFormat"].AsInt, Width = t["m_Width"].AsInt, Height = t["m_Height"].AsInt,
                MipCount = t["m_MipCount"].AsInt, Linear = t["m_ColorSpace"].AsInt == 0 };
            var stream = t["m_StreamData"];
            texture.Data = !stream.IsDummy && stream["size"].AsUInt > 0
                ? ReadStream(ctx, inst, stream["path"].AsString, (long)stream["offset"].AsULong, (int)stream["size"].AsUInt)
                : t["image data"].AsByteArray;
            ctx.Pack.Textures.Add(texture);
            ctx.Textures[key] = ctx.Pack.Textures.Count - 1;
            return ctx.Pack.Textures.Count - 1;
        }

        static byte[] ReadStream(Context ctx, AssetsFileInstance inst, string path, long offset, int size)
        {
            string file = Path.Combine(ctx.DataDir, Path.GetFileName(path.Replace("archive:/", "")));
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Position = offset;
                var data = new byte[size];
                int read = 0;
                while (read < size) { int n = fs.Read(data, read, size - read); if (n <= 0) throw new EndOfStreamException(file); read += n; }
                return data;
            }
        }

        static void Avatar(Context ctx, AssetsFileInstance inst, AssetTypeValueField pptr, ScCharacter character)
        {
            var ext = ctx.Manager.GetExtAsset(inst, pptr);
            if (ext.baseField == null) return;
            var a = ext.baseField;
            var paths = new Dictionary<uint, string>();
            foreach (var pair in a["m_TOS.Array"].Children) paths[pair["first"].AsUInt] = pair["second"].AsString;
            string Leaf(uint hash) => paths.TryGetValue(hash, out var p) ? p.Substring(p.LastIndexOf('/') + 1) : null;
            var human = a["m_Avatar.m_Human.data"];
            var ids = human["m_Skeleton.data.m_ID.Array"].Children.Select(c => c.AsUInt).ToList();
            var pose = human["m_SkeletonPose.data.m_X.Array"].Children;
            character.HumanScale = human["m_Scale"].AsFloat;
            var boneIndex = human["m_HumanBoneIndex.Array"].Children.Select(c => c.AsInt).ToList();
            for (int i = 0; i < boneIndex.Count && i < BodyBones.Length; i++)
                if (boneIndex[i] >= 0) Map(character, BodyBones[i], Leaf(ids[boneIndex[i]]));
            foreach (var (side, field) in new[] { ("Left", "m_LeftHand"), ("Right", "m_RightHand") })
            {
                var hand = human[field + ".data.m_HandBoneIndex.Array"].Children.Select(c => c.AsInt).ToList();
                for (int i = 0; i < hand.Count && i < 15; i++)
                    if (hand[i] >= 0) Map(character, side + " " + Fingers[i / 3] + " " + Phalanges[i % 3], Leaf(ids[hand[i]]));
            }
            // Reference pose for every bone the humanoid skeleton describes.
            for (int i = 0; i < ids.Count && i < pose.Count; i++)
            {
                string name = Leaf(ids[i]);
                var bone = character.Bones.Find(b => b.Name == name);
                if (bone == null) continue;
                bone.TPosePosition = V3(pose[i]["t"]);
                bone.TPoseRotation = Q(pose[i]["q"]);
            }
        }

        static void Map(ScCharacter character, string human, string bone)
        {
            if (!string.IsNullOrEmpty(bone) && character.Bones.Exists(b => b.Name == bone)) character.HumanBones[human] = bone;
        }

        // Decodes a humanoid clip (streamed Hermite segments + dense samples + constants) into per-frame poses.
        public static ScClip Clip(AssetTypeValueField clip)
        {
            var muscle = clip["m_MuscleClip"];
            var data = muscle["m_Clip.data"];
            var streamedWords = data["m_StreamedClip.data.Array"].Children.Select(c => c.AsUInt).ToArray();
            int streamedCount = data["m_StreamedClip.curveCount"].AsUShort;
            int denseFrames = data["m_DenseClip.m_FrameCount"].AsInt, denseCount = (int)data["m_DenseClip.m_CurveCount"].AsUInt;
            float denseRate = data["m_DenseClip.m_SampleRate"].AsFloat, denseBegin = data["m_DenseClip.m_BeginTime"].AsFloat;
            var dense = data["m_DenseClip.m_SampleArray.Array"].Children.Select(c => c.AsFloat).ToArray();
            var constants = data["m_ConstantClip.data.Array"].Children.Select(c => c.AsFloat).ToArray();
            var indexArray = muscle["m_IndexArray.Array"].Children.Select(c => c.AsInt).ToArray();
            float start = muscle["m_StartTime"].AsFloat, stop = muscle["m_StopTime"].AsFloat;
            var frames = Streamed(streamedWords);
            var result = new ScClip { Name = clip["m_Name"].AsString, Length = Math.Max(stop - start, 1f / 30f), Loop = muscle["m_LoopTime"].AsBool, SampleRate = 30f };
            result.FrameCount = (int)Math.Ceiling(result.Length * result.SampleRate) + 1;
            result.Data = new float[result.FrameCount * ScClip.Stride];
            var keys = new (float time, float[] coeff)[streamedCount];
            int nextFrame = 0;
            for (int f = 0; f < result.FrameCount; f++)
            {
                float t = Math.Min(start + f / result.SampleRate, stop);
                while (nextFrame < frames.Count && frames[nextFrame].time <= t)
                {
                    foreach (var key in frames[nextFrame].keys) if (key.index < streamedCount) keys[key.index] = (frames[nextFrame].time, key.coeff);
                    nextFrame++;
                }
                float Curve(int curve)
                {
                    if (curve < streamedCount)
                    {
                        var (time, c) = keys[curve];
                        if (c == null) return 0f;
                        float dt = float.IsInfinity(time) ? 0f : t - time;
                        return ((c[0] * dt + c[1]) * dt + c[2]) * dt + c[3];
                    }
                    curve -= streamedCount;
                    if (curve < denseCount)
                    {
                        if (denseFrames == 0) return 0f;
                        float position = Math.Max(0f, (t - denseBegin) * denseRate);
                        int a = Math.Min((int)position, denseFrames - 1), b = Math.Min(a + 1, denseFrames - 1);
                        float u = position - a;
                        return dense[a * denseCount + curve] * (1 - u) + dense[b * denseCount + curve] * u;
                    }
                    curve -= denseCount;
                    return curve < constants.Length ? constants[curve] : 0f;
                }
                float Value(int global, float fallback) => global < indexArray.Length && indexArray[global] >= 0 ? Curve(indexArray[global]) : fallback;
                int o = f * ScClip.Stride;
                for (int i = 0; i < 3; i++) result.Data[o + i] = Value(RootT + i, 0f);
                for (int i = 0; i < 4; i++) result.Data[o + 3 + i] = Value(RootQ + i, i == 3 ? 1f : 0f);
                for (int i = 0; i < MuscleCount; i++) result.Data[o + 7 + i] = Value(Muscles + i, 0f);
            }
            return result;
        }

        sealed class Frame { public float time; public List<(int index, float[] coeff)> keys = new List<(int, float[])>(); }

        static List<Frame> Streamed(uint[] words)
        {
            var bytes = new byte[words.Length * 4];
            Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
            var frames = new List<Frame>();
            using (var r = new BinaryReader(new MemoryStream(bytes)))
                while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
                {
                    var frame = new Frame { time = r.ReadSingle() };
                    int count = r.ReadInt32();
                    for (int k = 0; k < count; k++)
                    {
                        int index = r.ReadInt32();
                        frame.keys.Add((index, new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() }));
                    }
                    frames.Add(frame);
                }
            return frames;
        }

        static float[] V3(AssetTypeValueField v) => new[] { v["x"].AsFloat, v["y"].AsFloat, v["z"].AsFloat };
        static float[] Q(AssetTypeValueField q) => new[] { q["x"].AsFloat, q["y"].AsFloat, q["z"].AsFloat, q["w"].AsFloat };
    }
}
