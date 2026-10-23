using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Night 4, after supper (Marcus's, while playing together): Marcus needs the porta-potty below the tower before he
    // can sleep. On his way back, a couple of seconds after leaving it, his first step on the stairs sets the light in
    // the garage flickering, with Tessa up in the tower: his view is turned to it the game's own way (the tower's
    // "talking" camera, which turns the view to the ranger's look point, given the garage light instead), a low tone
    // and a sting, and his thought. The stairs are closed by the game's own night-7 stair block (its trigger now gives
    // his thought) until he has looked into the garage; inside, the flickering stops at once. With Tessa down there
    // with him, the garage stays quiet. Tessa sees the light flicker from the tower.
    internal sealed class NightErrand
    {
        internal static NightErrand Instance;
        readonly Plugin plugin;
        public NightErrand(Plugin plugin) { this.plugin = plugin; Instance = this; }

        enum Step { None, NeedToGo, Relieving, Relieved, Flicker, Done }
        Step step;
        float stepAt, leftPottyAt = -1f;
        Coroutine flicker;
        bool flickerHere;
        public string Debug => step + (flickerHere ? "/flicker" : "");

        static GameManager Gm => GameManager.instance;
        static WatchTowerManager Tower => Gm ? Gm.watchTowerManager : null;

        public void SceneLoaded() { step = Step.None; flicker = null; flickerHere = false; leftPottyAt = -1f; stairs = null; }

        bool Mine => plugin.Ready && plugin.LeadsStory;
        static bool Ate { get { var tower = Tower; return tower && tower.casserole && tower.casserole.finishedInt >= 4; } }
        static bool Up { get { var tower = Tower; return tower && Gm.playerFP && Gm.playerFP.activeInHierarchy && !Gm.uIManager.inTransition && !Gm.isConversing; } }

        public void Tick()
        {
            var tower = Tower;
            if (!tower || tower.seq != WatchTowerManager.Seq.Seq4 || !Mine) return;
            switch (step)
            {
                case Step.None:
                    // Once both have eaten (or he goes to bed full while Tessa has not).
                    if (Ate && SupperSync.PartnerAte && Up && tower.state == WatchTowerManager.State.Inside) Begin(4f);
                    break;
                case Step.NeedToGo:
                    if (Time.unscaledTime < stepAt) break;
                    if (stepAt > 0f) { stepAt = 0f; Think(ConnectionPanel.L("Ох... После такого ужина надо бы сходить в туалет.", "Oh... After a supper like that I need the toilet."), 5f); }
                    Hint(ConnectionPanel.L("Сходить в биотуалет внизу", "Go to the porta-potty downstairs"));
                    var potty = tower.portaPotty;
                    if (potty && potty.isPlayerInside && !OuthouseSync.IsOpen(potty)) { step = Step.Relieving; stepAt = Time.unscaledTime + 0.8f; Hint(""); }
                    break;
                case Step.Relieving:
                    if (tower.portaPotty && (!tower.portaPotty.isPlayerInside || OuthouseSync.IsOpen(tower.portaPotty))) { step = Step.NeedToGo; break; }
                    if (Time.unscaledTime < stepAt) break;
                    plugin.StartCoroutine(Relieve(tower.portaPotty));
                    step = Step.Relieved; leftPottyAt = -1f; stepAt = float.MaxValue;
                    break;
                case Step.Relieved:
                    if (Time.unscaledTime < stepAt) break;
                    if (tower.portaPotty && tower.portaPotty.isPlayerInside) { leftPottyAt = -1f; break; }
                    if (leftPottyAt < 0f) leftPottyAt = Time.unscaledTime;
                    // A couple of seconds out, on the first steps up.
                    if (Time.unscaledTime - leftPottyAt < 2f || !OnTheStairs()) break;
                    if (PartnerInTower(tower)) plugin.StartCoroutine(Flicker(tower));
                    else { step = Step.Done; Plugin.Log("ERRAND_DONE quiet (Tessa is not in the tower)"); }
                    break;
            }
        }

        void Begin(float delay)
        {
            step = Step.NeedToGo; stepAt = Time.unscaledTime + delay;
            Plugin.Log("ERRAND_BEGIN");
        }

        static void Think(string text, float seconds) => Gm.subtitle.ShowSubAndClose(text, seconds);
        static void Hint(string text) { if (Gm.uIManager && Gm.uIManager.controlsText.text != text) Gm.uIManager.controlsText.text = text; }

        // Going to sleep (night 4 ends with FadeToSleep(5)) waits for the errand. False: not now.
        internal bool AllowsSleep(int nextSeq)
        {
            var tower = Tower;
            if (nextSeq != 5 || !tower || tower.seq != WatchTowerManager.Seq.Seq4 || !Mine || step == Step.Done) return true;
            if (step == Step.None)
            {
                if (!Ate) return true;
                Begin(0f);
                return false;
            }
            Think(step == Step.Flicker ? ConnectionPanel.L("Сначала проверю, что там в гараже.", "First I'll check what's going on in the garage.")
                : ConnectionPanel.L("Сначала схожу в туалет.", "The toilet first."), 3f);
            return false;
        }

        IEnumerator Relieve(PortaPotty potty)
        {
            Plugin.Log("ERRAND_RELIEVING");
            var clip = Clip("peeing toilet");
            AudioSource source = null;
            if (clip && potty)
            {
                source = new GameObject("Coop errand sound").AddComponent<AudioSource>();
                source.transform.position = potty.transform.position + Vector3.up;
                source.clip = clip; source.spatialBlend = 1f; source.minDistance = 1f; source.maxDistance = 8f; source.volume = 0.7f;
                source.outputAudioMixerGroup = potty.doorAS ? potty.doorAS.outputAudioMixerGroup : null;
                source.Play();
            }
            yield return new WaitForSeconds(6f);
            if (source) { for (float t = 0f; t < 1f && source; t += Time.deltaTime) { source.volume = Mathf.Lerp(0.7f, 0f, t); yield return null; } if (source) Object.Destroy(source.gameObject); }
            Think(ConnectionPanel.L("Так-то лучше. Теперь можно и спать.", "That's better. Now for bed."), 3f);
            stepAt = Time.unscaledTime;
            Plugin.Log("ERRAND_RELIEVED");
        }

        static AudioClip Clip(string name)
        {
            foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>()) if (clip.name == name) return clip;
            return null;
        }

        // The first flight of the stairs up from the porta-potty.
        Collider stairs;
        bool OnTheStairs()
        {
            if (!stairs)
                foreach (var t in Object.FindObjectsOfType<Transform>())
                    if (t.name == "Stairs Col Cube (3)" && t.TryGetComponent<Collider>(out var c)) { stairs = c; break; }
            if (!stairs || !Gm.playerFP) return false;
            var bounds = stairs.bounds; bounds.Expand(new Vector3(0.8f, 2f, 0.8f));
            return bounds.Contains(Gm.playerFP.transform.position);
        }

        bool PartnerInTower(WatchTowerManager tower)
        {
            if (!plugin.Ready || !plugin.PartnerPresent || !tower.bed) return false;
            var at = plugin.PartnerPosition; var bed = tower.bed.transform.position;
            return at.y > bed.y - 2f && new Vector2(at.x - bed.x, at.z - bed.z).magnitude < 8f;
        }

        // ---- the garage ----

        IEnumerator Flicker(WatchTowerManager tower)
        {
            step = Step.Flicker;
            Plugin.Log("ERRAND_FLICKER");
            plugin.Send(new Packet { kind = "errand", code = "flicker-on", scene = SceneManager.GetActiveScene().name });
            StartFlicker(tower);
            Sting();
            // The block and its trigger are the game's own (night 7's way up the stairs), for him only.
            Local(tower.peeStairsBlock, true); Local(tower.peeStairsTrigger, true);
            tower.peeStairsBlock.SetActive(true); tower.peeStairsTrigger.SetActive(true);
            yield return LookAt(tower, tower.shedLight ? tower.shedLight.transform.position : tower.transform.position, 2.8f);
            Think(ConnectionPanel.L("Что это?.. В гараже мигает свет. Надо проверить.", "What's that?.. The light in the garage is flickering. I should check."), 5f);
        }

        void Local(GameObject target, bool on)
        {
            var story = StorySync.Instance;
            if (story == null || !target) return;
            if (on) story.LocalOnly.Add(target); else story.LocalOnly.Remove(target);
        }

        static void Sting()
        {
            foreach (var name in new[] { "cultist_realization", "low_and_deep_horror_tone" })
            {
                var clip = Clip(name);
                if (!clip) continue;
                var source = new GameObject("Coop errand sting").AddComponent<AudioSource>();
                source.clip = clip; source.spatialBlend = 0f; source.volume = name.StartsWith("cultist") ? 0.9f : 0.7f;
                source.Play();
                Object.Destroy(source.gameObject, clip.length + 0.5f);
            }
        }

        // The game's own turn of the view in its talks: in the "talking" state the tower turns the camera toward the
        // ranger's look point (and narrows the view a little). The look point is borrowed for the garage light; after it,
        // the player's controller takes over from where the view now is.
        IEnumerator LookAt(WatchTowerManager tower, Vector3 point, float seconds)
        {
            var fps = Gm.firstPersonController;
            var ranger = tower.parkRanger;
            if (!fps || !ranger) yield break;
            var target = new GameObject("Coop look point").transform;
            target.position = point;
            var lookHere = ranger.lookHere;
            var before = tower.state;
            ranger.lookHere = target;
            tower.state = WatchTowerManager.State.Talking;
            fps.enabled = false;
            Plugin.Log("ERRAND_LOOK_AT " + point.ToString("F1"));
            yield return new WaitForSeconds(seconds);
            ranger.lookHere = lookHere;
            if (tower.state == WatchTowerManager.State.Talking) tower.state = before == WatchTowerManager.State.Talking ? WatchTowerManager.State.OutsideDownstairs : before;
            HandBack(fps);
            fps.enabled = true;
            Object.Destroy(target.gameObject);
        }

        static readonly AccessTools.FieldRef<FirstPersonController, float> CurrentFov = AccessTools.FieldRefAccess<FirstPersonController, float>("currentFov");

        // The controller sets its camera from its own yaw and pitch: give it where the view ended up, and let the narrowed
        // view widen back smoothly.
        static void HandBack(FirstPersonController fps)
        {
            var camera = fps.playerCamera;
            if (!camera) return;
            var view = camera.transform.rotation;
            float pitch = Mathf.DeltaAngle(0f, view.eulerAngles.x);
            fps.transform.rotation = Quaternion.Euler(0f, view.eulerAngles.y, 0f);
            fps.rotationX = Mathf.Clamp(pitch, -80f, 80f);
            camera.transform.localRotation = Quaternion.Euler(fps.rotationX, 0f, camera.transform.localEulerAngles.z);
            CurrentFov(fps) = camera.fieldOfView;
        }

        void StartFlicker(WatchTowerManager tower)
        {
            if (flickerHere || !tower.shedLight) return;
            flickerHere = true;
            Local(tower.shedLight, true);
            var story = StorySync.Instance;
            if (story != null && tower.shedLightMaterial) story.LocalOnly.Add(tower.shedLightMaterial);
            flicker = plugin.StartCoroutine(Flickering(tower));
        }

        IEnumerator Flickering(WatchTowerManager tower)
        {
            bool on = true;
            while (flickerHere && tower)
            {
                on = !on;
                tower.shedLight.SetActive(on);
                if (tower.shedLightMaterial) { if (on) tower.shedLightMaterial.EnableKeyword("_EMISSION"); else tower.shedLightMaterial.DisableKeyword("_EMISSION"); }
                yield return new WaitForSeconds(on ? Random.Range(0.04f, 0.35f) : Random.Range(0.05f, 0.25f));
            }
        }

        void StopFlicker(WatchTowerManager tower)
        {
            if (!flickerHere) return;
            flickerHere = false;
            if (flicker != null) plugin.StopCoroutine(flicker);
            flicker = null;
            if (!tower) return;
            // As it was: the generator runs, the light is on.
            tower.shedLight.SetActive(tower.isGeneratorOn);
            if (tower.shedLightMaterial) { if (tower.isGeneratorOn) tower.shedLightMaterial.EnableKeyword("_EMISSION"); else tower.shedLightMaterial.DisableKeyword("_EMISSION"); }
            Local(tower.shedLight, false);
            if (StorySync.Instance != null && tower.shedLightMaterial) StorySync.Instance.LocalOnly.Remove(tower.shedLightMaterial);
        }

        // Inside the garage: the flickering stops at once.
        internal void EnteredShack()
        {
            var tower = Tower;
            if (step != Step.Flicker || !tower) return;
            StopFlicker(tower);
            plugin.Send(new Packet { kind = "errand", code = "flicker-off", scene = SceneManager.GetActiveScene().name });
            tower.peeStairsBlock.SetActive(false); tower.peeStairsTrigger.SetActive(false);
            Local(tower.peeStairsBlock, false); Local(tower.peeStairsTrigger, false);
            step = Step.Done;
            Think(ConnectionPanel.L("Хм... Здесь ничего нет. Можно возвращаться.", "Hm... Nothing here. Back up then."), 4f);
            Plugin.Log("ERRAND_GARAGE_CHECKED");
        }

        // The stair block's own trigger: his thought instead of night 7's.
        internal bool StairsThought()
        {
            if (step != Step.Flicker) return false;
            Think(ConnectionPanel.L("Сначала надо проверить, что там в гараже.", "I need to check the garage first."), 3f);
            return true;
        }

        // Tessa's side: the light she sees from the tower.
        public void Receive(Packet packet)
        {
            var tower = Tower;
            if (packet.scene != SceneManager.GetActiveScene().name || !tower) return;
            if (packet.code == "flicker-on") { StartFlicker(tower); Plugin.Log("ERRAND_FLICKER_SEEN"); }
            else if (packet.code == "flicker-off") StopFlicker(tower);
        }

        public void PartnerLeft()
        {
            var tower = Tower;
            if (flickerHere && !Mine) StopFlicker(tower);
        }

        // Lab: straight to the errand (both have eaten).
        internal void LabBegin() { SupperSync.PartnerAte = true; if (step == Step.None) Begin(0f); }
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.InsideShack))]
    internal static class ErrandShackPatch
    {
        static void Postfix() => NightErrand.Instance?.EnteredShack();
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.PeeStairsSub))]
    internal static class ErrandStairsPatch
    {
        static bool Prefix() => NightErrand.Instance == null || !NightErrand.Instance.StairsThought();
    }
}
