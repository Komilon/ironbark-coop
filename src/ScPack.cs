using System;
using System.Collections.Generic;
using System.IO;

namespace IronbarkCoop
{
    // Survivor characters and animations read from the player's own Scratch Creek install, cached locally.
    // Plain data with no Unity types, so the same file builds the offline explorer and the mod. Never shipped.
    internal sealed class ScPack
    {
        // 2: adds the sleeping clips (a new number re-reads the player's Scratch Creek once).
        public const int FormatVersion = 2;
        public string Source = "";
        public List<ScCharacter> Characters = new List<ScCharacter>();
        public List<ScTexture> Textures = new List<ScTexture>();
        public List<ScClip> Clips = new List<ScClip>();

        public ScCharacter Find(string id) => Characters.Find(c => c.Id == id);
        public ScClip Clip(string name) => Clips.Find(c => c.Name == name);

        public void Write(Stream stream)
        {
            var w = new BinaryWriter(stream);
            w.Write(0x43534249); // "IBSC"
            w.Write(FormatVersion);
            w.Write(Source);
            w.Write(Textures.Count);
            foreach (var t in Textures) { w.Write(t.Name); w.Write(t.Format); w.Write(t.Width); w.Write(t.Height); w.Write(t.MipCount); w.Write(t.Linear); Bytes(w, t.Data); }
            w.Write(Clips.Count);
            foreach (var c in Clips) { w.Write(c.Name); w.Write(c.Length); w.Write(c.Loop); w.Write(c.SampleRate); w.Write(c.FrameCount); Floats(w, c.Data); }
            w.Write(Characters.Count);
            foreach (var ch in Characters)
            {
                w.Write(ch.Id); w.Write(ch.DisplayName); w.Write(ch.HumanScale);
                w.Write(ch.Bones.Count);
                foreach (var b in ch.Bones)
                {
                    w.Write(b.Name); w.Write(b.Parent); Floats(w, b.LocalPosition); Floats(w, b.LocalRotation); Floats(w, b.LocalScale);
                    w.Write(b.TPosePosition != null); if (b.TPosePosition != null) { Floats(w, b.TPosePosition); Floats(w, b.TPoseRotation); }
                }
                w.Write(ch.HumanBones.Count);
                foreach (var pair in ch.HumanBones) { w.Write(pair.Key); w.Write(pair.Value); }
                w.Write(ch.Meshes.Count);
                foreach (var m in ch.Meshes)
                {
                    w.Write(m.Name); w.Write(m.VertexCount); w.Write(m.Channels.Count);
                    foreach (var c in m.Channels) { w.Write(c.Stream); w.Write(c.Offset); w.Write(c.Format); w.Write(c.Dimension); }
                    Bytes(w, m.VertexData); w.Write(m.IndexFormat); Bytes(w, m.IndexData);
                    w.Write(m.SubMeshes.Count);
                    foreach (var s in m.SubMeshes) { w.Write(s.FirstByte); w.Write(s.IndexCount); w.Write(s.BaseVertex); w.Write(s.FirstVertex); w.Write(s.VertexCount); }
                    w.Write(m.BindPoses.Count); foreach (var p in m.BindPoses) Floats(w, p);
                    w.Write(m.BoneIndices.Length); foreach (var i in m.BoneIndices) w.Write(i);
                    w.Write(m.RootBone);
                    w.Write(m.Materials.Count);
                    foreach (var mat in m.Materials) { w.Write(mat.Name); w.Write(mat.MainTexture); Floats(w, mat.Color); w.Write(mat.Glossiness); w.Write(mat.Metallic); w.Write(mat.Cutoff); w.Write(mat.Cutout); }
                }
            }
            w.Flush();
        }

        public static ScPack Read(Stream stream)
        {
            var r = new BinaryReader(stream);
            if (r.ReadInt32() != 0x43534249) throw new InvalidDataException("Not a Scratch Creek pack");
            if (r.ReadInt32() != FormatVersion) throw new InvalidDataException("Old pack format");
            var pack = new ScPack { Source = r.ReadString() };
            for (int n = r.ReadInt32(); n > 0; n--)
                pack.Textures.Add(new ScTexture { Name = r.ReadString(), Format = r.ReadInt32(), Width = r.ReadInt32(), Height = r.ReadInt32(), MipCount = r.ReadInt32(), Linear = r.ReadBoolean(), Data = Bytes(r) });
            for (int n = r.ReadInt32(); n > 0; n--)
                pack.Clips.Add(new ScClip { Name = r.ReadString(), Length = r.ReadSingle(), Loop = r.ReadBoolean(), SampleRate = r.ReadSingle(), FrameCount = r.ReadInt32(), Data = Floats(r) });
            for (int n = r.ReadInt32(); n > 0; n--)
            {
                var ch = new ScCharacter { Id = r.ReadString(), DisplayName = r.ReadString(), HumanScale = r.ReadSingle() };
                for (int b = r.ReadInt32(); b > 0; b--)
                {
                    var bone = new ScBone { Name = r.ReadString(), Parent = r.ReadInt32(), LocalPosition = Floats(r), LocalRotation = Floats(r), LocalScale = Floats(r) };
                    if (r.ReadBoolean()) { bone.TPosePosition = Floats(r); bone.TPoseRotation = Floats(r); }
                    ch.Bones.Add(bone);
                }
                for (int h = r.ReadInt32(); h > 0; h--) ch.HumanBones[r.ReadString()] = r.ReadString();
                for (int m = r.ReadInt32(); m > 0; m--)
                {
                    var mesh = new ScMesh { Name = r.ReadString(), VertexCount = r.ReadInt32() };
                    for (int c = r.ReadInt32(); c > 0; c--) mesh.Channels.Add(new ScChannel { Stream = r.ReadByte(), Offset = r.ReadByte(), Format = r.ReadByte(), Dimension = r.ReadByte() });
                    mesh.VertexData = Bytes(r); mesh.IndexFormat = r.ReadInt32(); mesh.IndexData = Bytes(r);
                    for (int s = r.ReadInt32(); s > 0; s--) mesh.SubMeshes.Add(new ScSubMesh { FirstByte = r.ReadInt32(), IndexCount = r.ReadInt32(), BaseVertex = r.ReadInt32(), FirstVertex = r.ReadInt32(), VertexCount = r.ReadInt32() });
                    for (int p = r.ReadInt32(); p > 0; p--) mesh.BindPoses.Add(Floats(r));
                    mesh.BoneIndices = new int[r.ReadInt32()]; for (int i = 0; i < mesh.BoneIndices.Length; i++) mesh.BoneIndices[i] = r.ReadInt32();
                    mesh.RootBone = r.ReadInt32();
                    for (int k = r.ReadInt32(); k > 0; k--)
                        mesh.Materials.Add(new ScMaterial { Name = r.ReadString(), MainTexture = r.ReadInt32(), Color = Floats(r), Glossiness = r.ReadSingle(), Metallic = r.ReadSingle(), Cutoff = r.ReadSingle(), Cutout = r.ReadBoolean() });
                    ch.Meshes.Add(mesh);
                }
                pack.Characters.Add(ch);
            }
            return pack;
        }

        static void Bytes(BinaryWriter w, byte[] data) { w.Write(data?.Length ?? 0); if (data != null) w.Write(data); }
        static byte[] Bytes(BinaryReader r) => r.ReadBytes(r.ReadInt32());
        static void Floats(BinaryWriter w, float[] data) { w.Write(data?.Length ?? 0); if (data != null) foreach (var f in data) w.Write(f); }
        static float[] Floats(BinaryReader r) { var data = new float[r.ReadInt32()]; for (int i = 0; i < data.Length; i++) data[i] = r.ReadSingle(); return data; }
    }

    internal sealed class ScCharacter
    {
        public string Id = "", DisplayName = "";
        public float HumanScale = 1f;
        public List<ScBone> Bones = new List<ScBone>();
        // Unity HumanTrait bone name -> skeleton bone name.
        public Dictionary<string, string> HumanBones = new Dictionary<string, string>();
        public List<ScMesh> Meshes = new List<ScMesh>();
    }

    internal sealed class ScBone
    {
        public string Name = "";
        public int Parent = -1;
        public float[] LocalPosition, LocalRotation, LocalScale;
        // Humanoid reference (T) pose from the Scratch Creek avatar, so muscle space matches the original exactly.
        public float[] TPosePosition, TPoseRotation;
    }

    internal sealed class ScMesh
    {
        public string Name = "";
        public int VertexCount;
        public List<ScChannel> Channels = new List<ScChannel>();
        public byte[] VertexData;
        public int IndexFormat;
        public byte[] IndexData;
        public List<ScSubMesh> SubMeshes = new List<ScSubMesh>();
        public List<float[]> BindPoses = new List<float[]>();
        public int[] BoneIndices = new int[0];
        public int RootBone = -1;
        public List<ScMaterial> Materials = new List<ScMaterial>();
    }

    internal struct ScChannel { public byte Stream, Offset, Format, Dimension; }
    internal struct ScSubMesh { public int FirstByte, IndexCount, BaseVertex, FirstVertex, VertexCount; }

    internal sealed class ScMaterial
    {
        public string Name = "";
        public int MainTexture = -1;
        public float[] Color = { 1f, 1f, 1f, 1f };
        public float Glossiness, Metallic, Cutoff;
        public bool Cutout;
    }

    internal sealed class ScTexture
    {
        public string Name = "";
        public int Format, Width, Height, MipCount;
        public bool Linear;
        public byte[] Data;
    }

    // Humanoid clip sampled at SampleRate: per frame body position (3), body rotation (4), 95 muscles.
    internal sealed class ScClip
    {
        public const int Stride = 3 + 4 + 95;
        public string Name = "";
        public float Length, SampleRate = 30f;
        public bool Loop;
        public int FrameCount;
        public float[] Data;
    }
}
