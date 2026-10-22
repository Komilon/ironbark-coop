using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace IronbarkCoop
{
    // Night 2 for two, Scratch Creek style: the night's wake-up (the garbled radio in bed, the washroom, back to bed) is
    // Tessa's. Marcus sleeps through it and dreams: he walks a dark forest with a flashlight, the picture swimming and
    // blurred, and hears Tessa only as a far, echoing voice. In her world he lies asleep on his half of the bed. When she
    // goes back to sleep (or leaves the game), he wakes into the same morning; without a partner he plays the night himself.
    internal sealed class DreamSync
    {
        internal static DreamSync Instance;
        readonly Plugin plugin;
        public bool Dreaming { get; private set; }
        public string Debug => Dreaming ? "dreaming/" + Vector3.Distance(start, Player ? Player.position : start).ToString("F0") + "m" : "awake";

        bool pending;
        float beginAt, dreamTime, nextTrace;
        Vector3 start;
        Vector3 savedPlayer; Quaternion savedPlayerRot;
        bool savedFog; FogMode savedFogMode; float savedFogDensity; Color savedFogColor, savedAmbient;
        GameObject fx, torch;
        Transform hands;
        PostProcessVolume volume;
        LensDistortion lens; ChromaticAberration chroma; DepthOfField focus;
        PostProcessLayer layer; bool layerWasEnabled;

        public DreamSync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        static Transform Player => GameManager.instance && GameManager.instance.firstPersonController ? GameManager.instance.firstPersonController.transform : null;

        public void SceneLoaded() { Dreaming = false; pending = false; fx = null; torch = null; hands = null; }

        // Night 2 has just been set up (player in bed, the intro text on a black screen).
        internal void NightTwoStarted()
        {
            if (!plugin.Ready || !plugin.LeadsStory) return;
            pending = true;
            beginAt = Time.unscaledTime + 0.3f;
        }

        // The garbled radio in bed is Tessa's to hear; the dreamer does not get it.
        internal bool SkipBedRadio => Dreaming || pending;

        public void Tick()
        {
            if (pending && Time.unscaledTime >= beginAt) { pending = false; Begin(); }
            if (!Dreaming) return;
            var gm = GameManager.instance;
            var tower = gm ? gm.watchTowerManager : null;
            if (!tower) { Dreaming = false; return; }
            // Alone again: the night is his to play.
            if (!plugin.Ready) { Plugin.Log("DREAM_PARTNER_GONE"); End(true); return; }
            // The night's intro holds the controls until it has faded in.
            if (!gm.uIManager.inTransition && !gm.isConversing && gm.firstPersonController && !gm.firstPersonController.enabled) gm.firstPersonController.enabled = true;
            dreamTime += Time.deltaTime;
            Animate();
            // The dream has no far side: walking out of it brings you back where it began.
            var player = Player;
            if (player && (player.position - start).sqrMagnitude > DreamRadius * DreamRadius) { Place(start); if (lens) lens.intensity.value = -80f; Plugin.Log("DREAM_LOOPED"); }
            if (plugin.Lab && Time.unscaledTime >= nextTrace) { nextTrace = Time.unscaledTime + 2f; Plugin.Log("DREAM_AT " + (player ? player.position.ToString("F1") : "-")); }
        }

        const float DreamRadius = 32f;

        void Begin()
        {
            var gm = GameManager.instance;
            var tower = gm ? gm.watchTowerManager : null;
            if (!tower || !gm.firstPersonController) return;
            var spot = FindSpot(tower.transform.position);
            if (!spot.HasValue) { Plugin.Log("DREAM_NO_SPOT"); return; }
            Dreaming = true;
            dreamTime = 0f; walked = 0f;
            tower.state = WatchTowerManager.State.OutsideDownstairs;
            if (tower.playerInBed) tower.playerInBed.SetActive(false);
            var fp = gm.firstPersonController.gameObject;
            savedPlayer = fp.transform.position; savedPlayerRot = fp.transform.rotation;
            fp.SetActive(true);
            start = spot.Value;
            Place(start);
            lastPosition = Player ? Player.position : start;
            Effects(true);
            // Whatever he went to bed holding (the bag, his own flashlight) stays out of the dream.
            hands = gm.itemPickUp ? gm.itemPickUp.itemHere : null;
            if (hands) hands.gameObject.SetActive(false);
            Plugin.Log("DREAM_BEGIN at " + start.ToString("F1"));
        }

        // Out of the dream. When the night ends (Tessa lies down), he is up in the tower as the game expects a player
        // who has just gone to bed to be, and the morning sets him up from there. When the partner left, he is back in
        // bed to play the night himself.
        internal void WakeInTheDark()
        {
            if (!Dreaming) { End(); return; }
            plugin.StartCoroutine(WakeAfterFade());
        }

        System.Collections.IEnumerator WakeAfterFade()
        {
            yield return new WaitForSecondsRealtime(1.15f);
            End();
        }

        internal void End(bool backToBed = false)
        {
            if (pending) pending = false;
            if (!Dreaming) return;
            Dreaming = false;
            Effects(false);
            if (hands) hands.gameObject.SetActive(true);
            hands = null;
            var gm = GameManager.instance;
            var tower = gm ? gm.watchTowerManager : null;
            if (gm && gm.firstPersonController)
            {
                var player = gm.firstPersonController.transform;
                var controller = player.GetComponent<CharacterController>();
                if (controller) controller.enabled = false;
                player.SetPositionAndRotation(savedPlayer, savedPlayerRot);
                Physics.SyncTransforms();
                if (controller) controller.enabled = true;
                gm.firstPersonController.gameObject.SetActive(!backToBed);
            }
            if (tower)
            {
                if (tower.playerInBed) tower.playerInBed.SetActive(backToBed);
                tower.state = backToBed ? WatchTowerManager.State.InBed : WatchTowerManager.State.Inside;
            }
            Plugin.Log("DREAM_END " + (backToBed ? "back to bed" : "night over"));
        }

        static void Place(Vector3 at)
        {
            var player = Player;
            if (!player) return;
            var controller = player.GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
            player.position = at + Vector3.up * (controller ? controller.height * 0.5f - controller.center.y + 0.1f : 1f);
            Physics.SyncTransforms();
            if (controller) controller.enabled = true;
        }

        // Forest floor well away from the tower and from every scripted trigger, so nothing in the night's story can
        // be set off from the dream (tried on rings around the tower, the same order every time).
        static Vector3? FindSpot(Vector3 around)
        {
            foreach (float radius in new[] { 120f, 150f, 95f, 180f })
                for (int i = 0; i < 24; i++)
                {
                    var direction = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
                    var probe = around + direction * radius;
                    if (!Physics.Raycast(probe + Vector3.up * 300f, Vector3.down, out var hit, 600f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!(hit.collider is TerrainCollider) || hit.normal.y < 0.85f) continue;
                    bool quiet = true;
                    foreach (var collider in Physics.OverlapSphere(hit.point, DreamRadius + 25f, ~0, QueryTriggerInteraction.Collide))
                        if (collider.isTrigger) { quiet = false; break; }
                    if (!quiet) continue;
                    // Room to stand.
                    if (Physics.CheckCapsule(hit.point + Vector3.up * 0.5f, hit.point + Vector3.up * 1.7f, 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    return hit.point;
                }
            return null;
        }

        void Effects(bool on)
        {
            var gm = GameManager.instance;
            var camera = gm && gm.firstPersonController ? gm.firstPersonController.playerCamera : null;
            if (on)
            {
                savedFog = RenderSettings.fog; savedFogMode = RenderSettings.fogMode; savedFogDensity = RenderSettings.fogDensity; savedFogColor = RenderSettings.fogColor; savedAmbient = RenderSettings.ambientLight;
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.035f;
                // A pale haze the trees stand out against, rather than plain black.
                RenderSettings.fogColor = new Color(0.10f, 0.12f, 0.16f);
                RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.14f);
                fx = new GameObject("Coop dream");
                if (camera)
                {
                    // The flashlight: a beam from the hand and the game's own flashlight model in it.
                    var beam = new GameObject("Dream flashlight").AddComponent<Light>();
                    beam.transform.SetParent(camera.transform, false);
                    beam.transform.localPosition = new Vector3(0.2f, -0.2f, 0.3f);
                    beam.type = LightType.Spot; beam.range = 22f; beam.spotAngle = 50f; beam.intensity = 1.3f; beam.color = new Color(1f, 0.94f, 0.8f); beam.shadows = LightShadows.Soft;
                    beam.transform.SetParent(fx.transform, true);
                    beam.gameObject.AddComponent<FollowCamera>().Target = camera.transform;
                    torch = TorchModel.Create(camera.transform);
                    if (torch) { torch.transform.localPosition = new Vector3(0.22f, -0.24f, 0.42f); torch.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); }
                    layer = camera.GetComponent<PostProcessLayer>();
                    if (layer) { layerWasEnabled = layer.enabled; layer.enabled = true; }
                }
                // The picture: swimming, blurred at the edges, drained of colour, grainy.
                var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                lens = profile.AddSettings<LensDistortion>(); lens.enabled.Override(true); lens.intensity.Override(-30f); lens.scale.Override(1.05f);
                chroma = profile.AddSettings<ChromaticAberration>(); chroma.enabled.Override(true); chroma.intensity.Override(0.8f);
                var vignette = profile.AddSettings<Vignette>(); vignette.enabled.Override(true); vignette.intensity.Override(0.55f); vignette.smoothness.Override(0.8f); vignette.color.Override(Color.black);
                var grain = profile.AddSettings<Grain>(); grain.enabled.Override(true); grain.intensity.Override(0.7f); grain.size.Override(1.6f);
                var grading = profile.AddSettings<ColorGrading>(); grading.enabled.Override(true); grading.saturation.Override(-55f); grading.temperature.Override(-25f); grading.postExposure.Override(0.2f);
                focus = profile.AddSettings<DepthOfField>(); focus.enabled.Override(true); focus.focusDistance.Override(1.5f); focus.aperture.Override(0.1f); focus.focalLength.Override(200f);
                // Scratch Creek's dream glows (its Glow_Color filter): soft cold bloom on every light.
                var glow = profile.AddSettings<Bloom>(); glow.enabled.Override(true); glow.intensity.Override(2f); glow.threshold.Override(0.9f); glow.softKnee.Override(0.8f); glow.diffusion.Override(9f); glow.color.Override(new Color(0.75f, 0.85f, 1f));
                ambience = Ambience();
                volume = fx.AddComponent<PostProcessVolume>();
                volume.isGlobal = true; volume.priority = 500f; volume.weight = 1f; volume.sharedProfile = profile;
                if (layer) fx.layer = FirstLayer(layer.volumeLayer);
            }
            else
            {
                RenderSettings.fog = savedFog; RenderSettings.fogMode = savedFogMode; RenderSettings.fogDensity = savedFogDensity; RenderSettings.fogColor = savedFogColor; RenderSettings.ambientLight = savedAmbient;
                if (ambience) { ambience.Stop(); Object.Destroy(ambience.gameObject); }
                if (fx) Object.Destroy(fx);
                if (torch) Object.Destroy(torch);
                if (layer) layer.enabled = layerWasEnabled;
                fx = null; torch = null; volume = null; lens = null; chroma = null; focus = null; layer = null; ambience = null;
            }
        }

        static int FirstLayer(LayerMask mask)
        {
            for (int i = 0; i < 32; i++) if ((mask.value & (1 << i)) != 0) return i;
            return 0;
        }

        // Dreams are never still: the picture breathes and the focus drifts. As in Scratch Creek, it starts thickly
        // blurred and clears over the first steps (10 m walked), though never fully; the dream's sound swells in.
        void Animate()
        {
            float t = dreamTime;
            var player = Player;
            if (player)
            {
                var step = player.position - lastPosition; step.y = 0f;
                if (step.magnitude < 2f) walked += step.magnitude;
                lastPosition = player.position;
            }
            float clear = Mathf.Clamp01(walked / 10f);
            if (lens) lens.intensity.value = Mathf.MoveTowards(lens.intensity.value, -30f + Mathf.Sin(t * 0.6f) * 18f, Time.deltaTime * 60f);
            if (chroma) chroma.intensity.value = 0.6f + Mathf.Sin(t * 1.3f) * 0.3f;
            if (focus)
            {
                focus.aperture.value = Mathf.Lerp(0.1f, 1.6f, clear);
                focus.focalLength.value = Mathf.Lerp(200f, 70f, clear);
                focus.focusDistance.value = Mathf.Lerp(1.5f, 2.5f + Mathf.Sin(t * 0.4f) * 1.5f, clear);
            }
            if (ambience) ambience.volume = Mathf.MoveTowards(ambience.volume, AmbienceVolume, Time.deltaTime * AmbienceVolume / 5f);
        }

        const float AmbienceVolume = 0.55f;
        float walked; Vector3 lastPosition;
        AudioSource ambience;

        // A sound of the game's own for the dream (the first of these that is loaded), heard everywhere, fading in.
        static AudioSource Ambience()
        {
            AudioClip clip = null;
            foreach (var name in new[] { "eerie atmosphere", "Whistle Amb", "ambient_49__Bone_Drone_" })
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<AudioClip>()) if (candidate.name.StartsWith(name)) { clip = candidate; break; }
                if (clip) break;
            }
            if (!clip) { Plugin.Log("DREAM_NO_AMBIENCE"); return null; }
            var source = new GameObject("Coop dream sound").AddComponent<AudioSource>();
            source.clip = clip; source.loop = true; source.spatialBlend = 0f; source.volume = 0f; source.playOnAwake = false;
            source.Play();
            Plugin.Log("DREAM_AMBIENCE " + clip.name);
            return source;
        }

        // Asleep on his half of the bed, as Tessa sees him.
        internal bool DescribeAsleep(Packet packet)
        {
            if (!Dreaming) return false;
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            var eye = BedSync.HalfEye(tower, false);
            if (!eye.HasValue) return false;
            var toFeet = tower.bed.transform.position - eye.Value; toFeet.y = 0f;
            packet.active = true; packet.sit = true; packet.seat = "bed";
            packet.eye = eye.Value; packet.position = eye.Value - Vector3.up * 0.5f;
            packet.steer = toFeet.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toFeet).eulerAngles.y : 0f;
            packet.rotation = Quaternion.Euler(0f, packet.steer, 0f);
            packet.pitch = -60f;
            packet.hand = null; packet.torch = false; packet.light = false;
            return true;
        }

        sealed class FollowCamera : MonoBehaviour
        {
            public Transform Target;
            void LateUpdate() { if (Target) transform.SetPositionAndRotation(Target.TransformPoint(new Vector3(0.2f, -0.2f, 0.3f)), Target.rotation); }
        }
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq2Start))]
    internal static class DreamNightPatch
    {
        static void Postfix() => DreamSync.Instance?.NightTwoStarted();
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.StartBedConvo))]
    internal static class DreamBedRadioPatch
    {
        static bool Prefix(ref IEnumerator __result)
        {
            if (DreamSync.Instance == null || !DreamSync.Instance.SkipBedRadio) return true;
            __result = Empty();
            return false;
        }
        static IEnumerator Empty() { yield break; }
    }

    // Going to sleep (Tessa, after the night's errands) wakes the dreamer, once the night's fade has gone black (the
    // fade takes a second; ending the dream at its start showed the tower before the screen was dark).
    [HarmonyPatch(typeof(UIManager), nameof(UIManager.FadeToSleep))]
    internal static class DreamWakePatch
    {
        static void Prefix() => DreamSync.Instance?.WakeInTheDark();
    }
}
