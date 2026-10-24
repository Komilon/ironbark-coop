using System.Collections;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace IronbarkCoop
{
    // Going to sleep together, as in Scratch Creek: whoever goes to bed first lies down on their half and waits (Space
    // gets up again); the night ends when the second one lies down too. Before, the first click on the bed ended the
    // night for both, wherever the partner was. Two players lying down at the same moment are both waiting: the host
    // then ends the night. A player left alone sleeps on their own.
    internal sealed class SleepSync
    {
        internal static SleepSync Instance;
        readonly Plugin plugin;
        public bool Waiting { get; private set; }
        int nextNight;
        float bothInBedSince = -1f;
        bool proceeding, moving;
        Vector3 standAt; Quaternion standRot;
        WatchTowerManager.State standState;

        public SleepSync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        static GameManager Gm => GameManager.instance;
        static WatchTowerManager Tower => Gm ? Gm.watchTowerManager : null;
        bool PartnerInBed => plugin.PartnerSeat == "bed";

        public string Debug => Waiting ? "waiting" + nextNight : "-";

        public void SceneLoaded() { Waiting = false; proceeding = false; moving = false; bothInBedSince = -1f; MorningInBed = false; }

        // The game is about to end the night here (FadeToSleep). True: lie down and wait for the partner instead.
        internal bool WaitForPartner(int night)
        {
            if (proceeding || !plugin.Ready || Waiting || moving) return false;
            var tower = Tower;
            if (!tower || !tower.playerInBed || PartnerInBed) return false;
            nextNight = night;
            plugin.StartCoroutine(LieDown());
            return true;
        }

        // The night ends (here or from the partner): nothing to wait for any more.
        // A player waiting in bed is stood up first: the game expects whoever goes to sleep to be up (it sets each
        // night's morning from there and would leave the bed camera on).
        internal void NightEnds()
        {
            if (Waiting) plugin.StartCoroutine(StandUpInTheDark());
            Waiting = false; proceeding = false; bothInBedSince = -1f;
        }

        // The night's fade is black after a second; the next night is set up later (3.5 s in).
        IEnumerator StandUpInTheDark()
        {
            yield return new WaitForSeconds(1.2f);
            StandUp();
        }

        void StandUp()
        {
            var gm = Gm; var tower = Tower;
            if (!gm || !tower) return;
            tower.playerInBed.SetActive(false);
            gm.playerFP.SetActive(true);
            gm.playerFP.transform.SetPositionAndRotation(standAt, standRot);
            tower.state = standState;
            gm.uIManager.controlsText.text = "";
        }

        public void Tick()
        {
            MorningTick();
            WaitTick();
        }

        void WaitTick()
        {
            if (!Waiting) return;
            // Alone now: sleep.
            if (!plugin.Ready) { Plugin.Log("SLEEP_ALONE"); Sleep(); return; }
            // Both lay down at once and both wait: the host ends the night.
            if (PartnerInBed && plugin.IsHost)
            {
                if (bothInBedSince < 0f) bothInBedSince = Time.unscaledTime;
                if (Time.unscaledTime - bothInBedSince > 1.5f) { Plugin.Log("SLEEP_BOTH_WAITING"); Sleep(); }
            }
            else bothInBedSince = -1f;
            var hint = ConnectionPanel.L("[ПРОБЕЛ] Встать. Ждём, пока ляжет партнёр.", "[SPACE] Get up. Waiting for your partner to lie down.");
            if (Gm.uIManager && Gm.uIManager.controlsText.text != hint && !Gm.uIManager.inTransition) Gm.uIManager.controlsText.text = hint;
        }

        void Sleep()
        {
            plugin.StartCoroutine(StandUpInTheDark());
            Waiting = false;
            proceeding = true;
            Gm.uIManager.controlsText.text = "";
            Gm.uIManager.StartCoroutine(Gm.uIManager.FadeToSleep(nextNight));
        }

        // The morning after night 2: Marcus wakes up on the balcony with his coffee (the game's morning), Tessa wakes
        // up still in bed and gets up when she likes (Space), into the tower. Her own coffee scene does not run.
        // The morning of night 6 the other way round: Tessa at the binoculars, Marcus in bed (Night6Sync).
        public bool MorningInBed { get; private set; }

        internal void WakeInBed()
        {
            var gm = Gm; var tower = Tower;
            if (!gm || !tower || !tower.playerInBed) return;
            if (tower.playerCoffee) tower.playerCoffee.SetActive(false);
            if (tower.mugInHand) tower.mugInHand.gameObject.SetActive(false);
            // The morning also puts the coffee mug in the hand (ItemPickUp.MugInHand): she has none, and the partner
            // was shown it in her hand all day.
            var pickup = gm.itemPickUp;
            if (pickup && pickup.inHandMug && pickup.currentItem == (tower.mugInHand ? tower.mugInHand.gameObject : null))
            {
                pickup.inHand = false; pickup.inHandMug = false; pickup.currentItem = null; pickup.mug = null;
            }
            if (tower.pictureMode) tower.pictureMode.gameObject.SetActive(false);
            // The morning of night 6 starts at the binoculars on the balcony (in hand, as the game's own get-off from them
            // gives them): Tessa's.
            if (tower.seq == WatchTowerManager.Seq.Seq6)
            {
                if (tower.playerBino) tower.playerBino.SetActive(false);
                if (pickup && pickup.inHandBinoculars && (pickup.binocularsModel == tower.seq6Binoculars || pickup.binocularsModel == tower.binoculars))
                {
                    pickup.inHand = false; pickup.inHandBinoculars = false; pickup.binocularsModel = null;
                    if (pickup.currentItem == tower.binoculars) pickup.currentItem = null;
                }
                // Space would otherwise run the game's get-off from the binoculars later (binoculars in hand, anywhere).
                tower.seq6CanGetOff = false;
                tower.state = WatchTowerManager.State.InBed;
            }
            gm.playerFP.SetActive(false);
            tower.playerInBed.SetActive(true);
            tower.playerInBed.transform.localEulerAngles = new Vector3(0f, 30f, 0f);
            if (tower.bedMovingCam) { tower.bedMovingCam.enabled = true; tower.bedMovingCam.xRotation = 0f; tower.bedMovingCam.yRotation = 0f; }
            tower.state = WatchTowerManager.State.InBed;
            MorningInBed = true;
            Plugin.Log("MORNING_IN_BED");
        }

        void MorningTick()
        {
            if (!MorningInBed || !Gm || !Gm.uIManager || Gm.uIManager.inTransition) return;
            var hint = ConnectionPanel.L("[ПРОБЕЛ] Встать", "[SPACE] Get up");
            if (Gm.uIManager.controlsText.text != hint) Gm.uIManager.controlsText.text = hint;
        }

        IEnumerator GetUpInTheMorning()
        {
            var gm = Gm; var tower = Tower; var ui = gm.uIManager;
            moving = true;
            MorningInBed = false;
            ui.controlsText.text = "";
            yield return Fade(ui, true);
            tower.playerInBed.SetActive(false);
            gm.playerFP.SetActive(true);
            var fp = gm.playerFP.transform;
            var controller = gm.playerFP.GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
            fp.position = BesideTheBed(tower) + Vector3.up * (controller ? controller.height * 0.5f - controller.center.y + 0.05f : 1f);
            var away = tower.door ? tower.door.transform.position - tower.bed.transform.position : -tower.bed.transform.forward; away.y = 0f;
            fp.rotation = Quaternion.LookRotation(away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward);
            Physics.SyncTransforms();
            if (controller) controller.enabled = true;
            gm.firstPersonController.enabled = true;
            tower.state = WatchTowerManager.State.Inside;
            yield return Fade(ui, false);
            moving = false;
            Plugin.Log("MORNING_GOT_UP at " + fp.position.ToString("F2"));
        }

        // Floor between the bed and the door, where there is room to stand.
        internal static Vector3 BesideTheBed(WatchTowerManager tower)
        {
            var bed = tower.bed.transform.position;
            var door = tower.door ? tower.door.transform.position : bed - tower.bed.transform.forward * 2f;
            foreach (float t in new[] { 0.45f, 0.3f, 0.6f, 0.75f })
            {
                var spot = Vector3.Lerp(bed, door, t);
                if (!Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.CheckCapsule(floor.point + Vector3.up * 0.35f, floor.point + Vector3.up * 1.6f, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                return floor.point;
            }
            return Vector3.Lerp(bed, door, 0.45f);
        }

        // Space while waiting: up again (the game's own Space in bed would run a different night's get-up).
        internal bool TakeSpace()
        {
            if (MorningInBed)
            {
                if (moving || Gm.uIManager.inTransition || Gm.isConversing || !Gm.inputHandler.spaceInput) return false;
                plugin.StartCoroutine(GetUpInTheMorning());
                return true;
            }
            if (!Waiting || moving || Gm.uIManager.inTransition || !Gm.inputHandler.spaceInput) return false;
            plugin.StartCoroutine(GetUp());
            return true;
        }

        IEnumerator LieDown()
        {
            var gm = Gm; var tower = Tower; var ui = gm.uIManager;
            moving = true;
            yield return Fade(ui, true);
            standAt = gm.playerFP.transform.position; standRot = gm.playerFP.transform.rotation; standState = tower.state;
            gm.playerFP.SetActive(false);
            tower.playerInBed.SetActive(true);
            tower.playerInBed.transform.localEulerAngles = new Vector3(0f, 30f, 0f);
            if (tower.bedMovingCam) { tower.bedMovingCam.enabled = true; tower.bedMovingCam.xRotation = 0f; tower.bedMovingCam.yRotation = 0f; }
            tower.state = WatchTowerManager.State.InBed;
            Waiting = true;
            yield return Fade(ui, false);
            moving = false;
            gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Нужно дождаться, пока ляжет и напарник.", "Need to wait for the partner to lie down too."), 3f);
            Plugin.Log("SLEEP_WAITING night " + nextNight);
        }

        IEnumerator GetUp()
        {
            var gm = Gm; var tower = Tower; var ui = gm.uIManager;
            moving = true;
            Waiting = false;
            ui.controlsText.text = "";
            yield return Fade(ui, true);
            tower.playerInBed.SetActive(false);
            gm.playerFP.SetActive(true);
            gm.playerFP.transform.SetPositionAndRotation(standAt, standRot);
            gm.firstPersonController.enabled = true;
            tower.state = standState;
            yield return Fade(ui, false);
            moving = false;
            Plugin.Log("SLEEP_GOT_UP");
        }

        static IEnumerator Fade(UIManager ui, bool toBlack)
        {
            ui.inTransition = true;
            ui.transitionImage.gameObject.SetActive(true);
            var c = ui.transitionImage.color;
            ui.transitionImage.color = new Color(c.r, c.g, c.b, toBlack ? 0f : 1f);
            ui.transitionImage.DOColor(new Color(c.r, c.g, c.b, toBlack ? 1f : 0f), 0.8f);
            yield return new WaitForSeconds(0.9f);
            if (!toBlack) ui.transitionImage.gameObject.SetActive(false);
            ui.inTransition = toBlack;
        }
    }

    // Space in bed while waiting for the partner belongs to the wait, not to the game's own get-up of that night.
    [HarmonyPatch(typeof(WatchTowerManager), "Update")]
    internal static class SleepSpacePatch
    {
        static readonly System.Reflection.MethodInfo SetSpace = AccessTools.PropertySetter(typeof(InputHandler), "spaceInput");

        static void Prefix()
        {
            var sleep = SleepSync.Instance;
            if (!GameManager.instance) return;
            // Napping in the day while the partner is at the campfire: Space would run night 4's get-up from supper.
            if (TripSync.Instance != null && TripSync.Instance.Napping) { if (GameManager.instance.inputHandler.spaceInput) SetSpace.Invoke(GameManager.instance.inputHandler, new object[] { false }); return; }
            if (sleep == null || !(sleep.Waiting || sleep.MorningInBed)) return;
            if (sleep.TakeSpace()) SetSpace.Invoke(GameManager.instance.inputHandler, new object[] { false });
            else if (GameManager.instance.inputHandler.spaceInput) SetSpace.Invoke(GameManager.instance.inputHandler, new object[] { false });
        }
    }
}

namespace IronbarkCoop
{
    // The morning after night 2 (Seq3Start sets up the coffee on the balcony): the partner who does not lead the story
    // wakes up in bed instead.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq3Start))]
    internal static class MorningInBedPatch
    {
        static void Postfix()
        {
            var plugin = Plugin.Instance;
            if (plugin && plugin.Ready && plugin.FollowsStory) SleepSync.Instance?.WakeInBed();
        }
    }
}
