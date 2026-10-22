using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // The diner jukebox and the radios play the same song at the same moment for both players. The host's copy keeps
    // the clock (and picks the next song when one ends); either player can press the jukebox or switch a radio.
    internal sealed class MusicSync
    {
        static readonly FieldInfo SongIndex = AccessTools.Field(typeof(JukeBox), "songIndex");
        static readonly FieldInfo JukeSource = AccessTools.Field(typeof(JukeBox), "AS");
        readonly Plugin plugin;
        float nextSync;
        public int Corrections { get; private set; }

        public MusicSync(Plugin plugin) { this.plugin = plugin; }

        public void SceneLoaded() { nextSync = Time.unscaledTime + 3f; }

        public void Tick()
        {
            if (!plugin.Ready || !plugin.IsHost || Time.unscaledTime < nextSync) return;
            nextSync = Time.unscaledTime + 3f;
            foreach (var jukebox in Object.FindObjectsOfType<JukeBox>()) SendJukebox(jukebox);
            foreach (var radio in Object.FindObjectsOfType<Radio>())
            {
                if (!radio.audioSource) continue;
                plugin.Send(new Packet { kind = "music", code = "radio", name = ItemSync.PathOf(radio.transform), scene = SceneManager.GetActiveScene().name,
                    arg = radio.currentClip, pitch = radio.audioSource.time, active = !radio.isStopped });
            }
        }

        internal void SendJukebox(JukeBox jukebox)
        {
            var source = JukeSource.GetValue(jukebox) as AudioSource;
            if (!plugin.Ready || !source) return;
            plugin.Send(new Packet { kind = "music", code = "jukebox", name = ItemSync.PathOf(jukebox.transform), scene = SceneManager.GetActiveScene().name,
                arg = (int)SongIndex.GetValue(jukebox), pitch = source.time, active = source.isPlaying });
        }

        internal void SendJukeboxClick(JukeBox jukebox)
        {
            if (!plugin.Ready) return;
            plugin.Send(new Packet { kind = "music", code = "jukebox-click", name = ItemSync.PathOf(jukebox.transform), scene = SceneManager.GetActiveScene().name });
        }

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name) return;
            var target = ItemSync.FindPath(packet.name);
            if (!target) return;
            if (packet.code == "radio" && target.TryGetComponent<Radio>(out var radio)) { ApplyRadio(radio, packet); return; }
            if (!target.TryGetComponent<JukeBox>(out var jukebox)) return;
            var source = JukeSource.GetValue(jukebox) as AudioSource;
            if (!source) return;
            if (packet.code == "jukebox-click")
            {
                // The song-change click, heard from the box; the new song itself follows from the presser's side.
                jukebox.StopAllCoroutines();
                source.Stop();
                if (jukebox.jukeboxClick && jukebox.dinerSongChange) jukebox.jukeboxClick.PlayOneShot(jukebox.dinerSongChange);
                return;
            }
            if (packet.arg < 0 || packet.arg >= jukebox.songs.Length) return;
            bool otherSong = (int)SongIndex.GetValue(jukebox) != packet.arg || source.clip != jukebox.songs[packet.arg];
            if (otherSong)
            {
                jukebox.StopAllCoroutines();
                SongIndex.SetValue(jukebox, packet.arg);
                source.clip = jukebox.songs[packet.arg];
            }
            // Between songs (the 2 s click pause) the box is silent on both sides.
            if (!packet.active) { if (source.isPlaying) source.Stop(); return; }
            if (otherSong || !source.isPlaying) { source.Play(); Corrections++; }
            Seek(source, packet.pitch);
            // The guest picked a song: the host's clock restarts for it so the next song still comes on time.
            if (otherSong && plugin.IsHost)
                jukebox.StartCoroutine((IEnumerator)NextSong.Invoke(jukebox, null));
        }

        static readonly MethodInfo NextSong = AccessTools.Method(typeof(JukeBox), "NextSong");

        void ApplyRadio(Radio radio, Packet packet)
        {
            var source = radio.audioSource;
            if (!source || radio.radioClips == null || packet.arg < 0 || packet.arg >= radio.radioClips.Length) return;
            if (radio.currentClip != packet.arg || source.clip != radio.radioClips[packet.arg])
            {
                radio.currentClip = packet.arg;
                source.clip = radio.radioClips[packet.arg];
                source.Play();
                Corrections++;
            }
            Seek(source, packet.pitch);
            if (packet.active == radio.isStopped)
            {
                RemoteCalls.Applying = true;
                try { if (packet.active) radio.Play(); else radio.Stop(); }
                finally { RemoteCalls.Applying = false; }
            }
        }

        // Small drifts are left alone (a jump is more noticeable than 0.3 s of offset).
        void Seek(AudioSource source, float time)
        {
            if (!source.clip || time <= 0f || time >= source.clip.length - 0.2f) return;
            if (Mathf.Abs(source.time - time) > 0.35f) { source.time = time; Corrections++; }
        }
    }

    [HarmonyPatch(typeof(JukeBox), "NextSongWithWait")]
    internal static class JukeboxClickPatch
    {
        static void Prefix(JukeBox __instance) => Plugin.Instance?.Music.SendJukeboxClick(__instance);
    }

    [HarmonyPatch(typeof(JukeBox), "OnNextSong")]
    internal static class JukeboxSongPatch
    {
        static void Postfix(JukeBox __instance) => Plugin.Instance?.Music.SendJukebox(__instance);
    }

    // Only the host's jukebox advances by itself; the guest's follows the host's songs.
    [HarmonyPatch(typeof(JukeBox), "NextSong")]
    internal static class JukeboxAutoPatch
    {
        static bool Prefix(ref IEnumerator __result)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready || plugin.IsHost) return true;
            __result = Wait();
            return false;
        }
        static IEnumerator Wait() { yield break; }
    }
}
