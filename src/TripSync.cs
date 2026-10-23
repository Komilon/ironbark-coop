using System.Collections;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // The day after night 2 Connor sends the team to the campers' smoke (the Lacey trail, the Camping level). One player
    // or both can go. Whoever reaches the trail head first is asked, in the game's dialogue panel, whether to wait for
    // the partner or go now. Going together needs one bear spray between the two. Going alone: the partner stays in the
    // tower: the trail is closed for them, they get a few chores and then lie down for a nap until the partner is back
    // (the campfire's end brings both into the evening of night 4). Being in different scenes meanwhile, they neither
    // hear nor touch each other.
    internal sealed class TripSync
    {
        internal static TripSync Instance;
        readonly Plugin plugin;
        public TripSync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        // This player stays in the tower while the partner is at the campfire.
        public bool StayingBehind { get; private set; }
        public bool Napping => napping;
        bool waiting, partnerWaiting, choosing, napping;
        Vector3 waitAt;
        ListenPanel panel;
        bool cursorWasShown, controllerWasEnabled;
        int chore;
        public string Debug => (StayingBehind ? "behind/chore" + chore + (napping ? "/napping" : "") : waiting ? "waiting" : choosing ? "choosing" : partnerWaiting ? "partnerWaiting" : "-");

        static GameManager Gm => GameManager.instance;
        static WatchTowerManager Tower => Gm ? Gm.watchTowerManager : null;
        string PartnerName => string.IsNullOrEmpty(plugin.PartnerName) ? ConnectionPanel.L("напарник", "your partner") : plugin.PartnerName;
        bool PartnerIsTessa => (plugin.PartnerCharacter ?? (plugin.MyCharacter == "tessa" ? "marcus" : "tessa")) == "tessa";
        string Went => PartnerIsTessa ? ConnectionPanel.L("ушла", "went") : ConnectionPanel.L("ушёл", "went");
        string WillBeBack => PartnerIsTessa ? ConnectionPanel.L("она вернётся", "she is back") : ConnectionPanel.L("он вернётся", "he is back");

        public void SceneLoaded()
        {
            ClosePanel();
            StayingBehind = false; waiting = false; partnerWaiting = false; choosing = false; napping = false; chore = 0;
        }

        // The partner's scene is not followed into the campfire while this player stays behind.
        internal bool HoldsScene(string scene) => StayingBehind && scene == "Camping";

        public void PartnerLeft()
        {
            partnerWaiting = false;
            if (waiting) StopWaiting(false);
            if (!StayingBehind) return;
            // Alone now: the trail is this player's again.
            StayingBehind = false;
            if (napping) plugin.StartCoroutine(WakeFromNap());
            Plugin.Log("TRIP_PARTNER_GONE");
        }

        // The tower's spray is a shared item once picked up; the one the morning hands out is personal (seen in the hand).
        bool PartnerHasSpray
        {
            get
            {
                var held = plugin.Items.RemoteHeldObject;
                if (held && held.CompareTag("Spray")) return true;
                var visual = plugin.PartnerHeldVisual;
                return visual && visual.name.IndexOf("Spray", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // The trail head (WatchTowerManager.FadeToNextScene, which loads the campfire). True: the game's own code runs.
        internal bool BeforeTrail(WatchTowerManager tower)
        {
            if (!plugin.Ready || tower.seq != WatchTowerManager.Seq.Seq3) return true;
            if (StayingBehind)
            {
                Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("{0} {1} к дыму один. Мне нужно остаться на вышке.", "{0} {1} to the smoke alone. I need to stay at the tower."), PartnerName, Went), 4f);
                return false;
            }
            if (choosing || waiting) return false;
            if (partnerWaiting)
            {
                // Together: one bear spray between the two is enough.
                if (!Gm.itemPickUp.inHandSpray && !PartnerHasSpray) { BearSprayFirst(); return false; }
                plugin.Send(new Packet { kind = "trip", code = "together", scene = SceneManager.GetActiveScene().name });
                Plugin.Log("TRIP_TOGETHER");
                Go();
                return false;
            }
            OpenChoice();
            return false;
        }

        static void BearSprayFirst() { Gm.subtitle.ShowSubAndClose(F2FLocalizedText.GetLocalizedText("ep4_subs", "BearSpray")); Plugin.Log("TRIP_NEEDS_SPRAY"); }

        // The game's own way down the trail, without its spray check (done above for one or both).
        static void Go()
        {
            PlayerPrefs.SetInt("InHandFlashlight", Gm.itemPickUp.inHandFlashlight ? 1 : 0);
            Gm.uIManager.FadeOut("Camping");
        }

        void OpenChoice()
        {
            panel = ListenPanel.Create();
            if (!panel.Alive) { panel = null; return; }
            choosing = true;
            controllerWasEnabled = Gm.firstPersonController && Gm.firstPersonController.enabled;
            if (Gm.firstPersonController) Gm.firstPersonController.enabled = false;
            cursorWasShown = Cursor.visible;
            Gm.ShowCursor();
            Gm.isConversing = true;
            panel.ShowLine("", string.Format(ConnectionPanel.L("Подождать {0} или идти к дыму сейчас?", "Wait for {0} or go to the smoke now?"), PartnerName));
            panel.ShowOptions(new[] { string.Format(ConnectionPanel.L("Подождать {0}", "Wait for {0}"), PartnerName), ConnectionPanel.L("Идти сейчас", "Go now") }, 3, Pick);
            panel.ShowHint(ConnectionPanel.L("[ПРОБЕЛ] Отойти", "[SPACE] Step back"));
            Plugin.Log("TRIP_CHOICE");
        }

        void ClosePanel()
        {
            if (choosing)
            {
                choosing = false;
                if (Gm && Gm.firstPersonController && controllerWasEnabled) Gm.firstPersonController.enabled = true;
                if (Gm) { Gm.isConversing = false; if (!cursorWasShown) Gm.HideCursor(); }
            }
            panel?.Destroy();
            panel = null;
        }

        void Pick(int index)
        {
            if (!choosing) return;
            ClosePanel();
            if (index == 0) StartWaiting();
            else GoAlone();
        }

        // The lab picks an answer like a click would.
        internal void LabPick(int index) { if (choosing) Pick(index); }

        void StartWaiting()
        {
            waiting = true;
            waitAt = Gm.playerFP.transform.position;
            plugin.Send(new Packet { kind = "trip", code = "wait", scene = SceneManager.GetActiveScene().name });
            Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("Подожду {0} здесь, у тропы.", "I'll wait for {0} here at the trail."), PartnerName), 3f);
            Plugin.Log("TRIP_WAITING");
        }

        void StopWaiting(bool tell)
        {
            waiting = false;
            if (Gm && Gm.uIManager) Gm.uIManager.controlsText.text = "";
            if (tell && plugin.Ready) plugin.Send(new Packet { kind = "trip", code = "unwait", scene = SceneManager.GetActiveScene().name });
        }

        void GoAlone()
        {
            if (!Gm.itemPickUp.inHandSpray) { BearSprayFirst(); return; }
            if (waiting) StopWaiting(false);
            plugin.Send(new Packet { kind = "trip", code = "alone", scene = SceneManager.GetActiveScene().name });
            Plugin.Log("TRIP_ALONE");
            Go();
        }

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name) return;
            switch (packet.code)
            {
                case "wait":
                    partnerWaiting = true;
                    Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("{0} ждёт меня у тропы к дыму.", "{0} is waiting for me at the trail to the smoke."), PartnerName), 4f);
                    break;
                case "unwait": partnerWaiting = false; break;
                case "together":
                    // The partner came: the campfire scene follows from their side.
                    if (waiting) StopWaiting(false);
                    Plugin.Log("TRIP_PARTNER_CAME");
                    break;
                case "alone":
                    partnerWaiting = false;
                    if (waiting) StopWaiting(false);
                    if (choosing) ClosePanel();
                    StayingBehind = true;
                    chore = 0;
                    choreAt = Time.unscaledTime + 2f;
                    Plugin.Log("TRIP_STAYING_BEHIND");
                    break;
            }
        }

        public void Tick()
        {
            if (choosing)
            {
                panel?.Tick();
                // Space steps back from the trail head without choosing.
                if (Input.GetKeyDown(KeyCode.Space) || (Gm.inputHandler && Gm.inputHandler.spaceInput)) { ClosePanel(); Plugin.Log("TRIP_CHOICE_CLOSED"); }
                return;
            }
            if (waiting)
            {
                var hint = string.Format(ConnectionPanel.L("Ждём {0} у тропы. [ПРОБЕЛ] Идти сейчас", "Waiting for {0} at the trail. [SPACE] Go now"), PartnerName);
                if (Gm.uIManager && Gm.uIManager.controlsText.text != hint && !Gm.uIManager.inTransition) Gm.uIManager.controlsText.text = hint;
                if (!plugin.Ready || (Gm.playerFP && Vector3.Distance(Gm.playerFP.transform.position, waitAt) > 8f)) { StopWaiting(true); Plugin.Log("TRIP_WAIT_LEFT"); }
                else if (Input.GetKeyDown(KeyCode.Space) || (Gm.inputHandler && Gm.inputHandler.spaceInput)) GoAlone();
                return;
            }
            if (StayingBehind) Chores();
        }

        // ---- the one who stays: a few chores, then a nap until the partner is back ----

        float choreAt;
        void Chores()
        {
            var tower = Tower;
            if (!tower || napping || Gm.uIManager.inTransition || Time.unscaledTime < choreAt) return;
            switch (chore)
            {
                case 0:
                    Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("{0} {1} проверить дым. Займусь пока делами на вышке, а потом подремлю, пока {2}.", "{0} {1} to check the smoke. I'll see to the tower meanwhile, then nap until {2}."), PartnerName, Went, WillBeBack), 6f);
                    Next(6.5f);
                    break;
                case 1:
                    Hint(ConnectionPanel.L("Дела: закрыть ставни на окнах", "Chores: close the window boards"));
                    if (tower.AllBlindsClosed()) { Gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Ставни закрыты.", "The boards are shut."), 3f); Next(3.5f); }
                    break;
                case 2:
                    Hint(ConnectionPanel.L("Дела: проверить генератор внизу", "Chores: check the generator downstairs"));
                    if (Clicked(tower.generator, 2.2f)) { Gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Генератор работает исправно.", "The generator runs fine."), 3f); Next(3.5f); }
                    break;
                case 3:
                    Hint(ConnectionPanel.L("Прилечь подремать на кровати", "Lie down on the bed for a nap"));
                    if (Clicked(tower.bed, 2.2f)) plugin.StartCoroutine(Nap());
                    break;
            }
        }

        void Next(float delay) { chore++; choreAt = Time.unscaledTime + delay; Hint(""); Plugin.Log("TRIP_CHORE " + chore); }

        static void Hint(string text) { if (Gm.uIManager && Gm.uIManager.controlsText.text != text) Gm.uIManager.controlsText.text = text; }

        static bool Clicked(GameObject target, float reach)
        {
            var gm = Gm;
            var tower = Tower;
            if (!target || !gm.inputHandler || !gm.inputHandler.mouseLeftClickDown || !Camera.main) return false;
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(gm.inputHandler.mousePosition), out var hit, 20f, tower ? tower.layerMask.value : ~0)) return false;
            return hit.distance < reach && (hit.collider.gameObject == target || hit.collider.transform.IsChildOf(target.transform));
        }

        // Not a night's sleep: a nap in the day, eyes closed until the partner is back.
        IEnumerator Nap()
        {
            var tower = Tower; var ui = Gm.uIManager;
            napping = true;
            Hint("");
            ui.inTransition = true;
            ui.transitionImage.gameObject.SetActive(true);
            var c = ui.transitionImage.color;
            ui.transitionImage.color = new Color(c.r, c.g, c.b, 0f);
            ui.transitionImage.DOColor(new Color(c.r, c.g, c.b, 1f), 1f);
            yield return new WaitForSeconds(1.1f);
            Gm.playerFP.SetActive(false);
            tower.playerInBed.SetActive(true);
            tower.state = WatchTowerManager.State.InBed;
            Plugin.Log("TRIP_NAP");
            Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("Подремлю немного, пока {0}...", "A short nap until {0}..."), WillBeBack), 5f);
            // Eyes closed: the room shows only faintly through the lids.
            ui.transitionImage.DOColor(new Color(c.r, c.g, c.b, 0.85f), 2f);
            while (napping && StayingBehind)
            {
                Hint(string.Format(ConnectionPanel.L("Дремлю... {0} ещё у костра.", "Napping... {0} is still at the campfire."), PartnerName));
                yield return new WaitForSeconds(1f);
            }
        }

        IEnumerator WakeFromNap()
        {
            napping = false;
            var ui = Gm.uIManager; var tower = Tower;
            Hint("");
            if (!ui || !tower) yield break;
            tower.playerInBed.SetActive(false);
            Gm.playerFP.SetActive(true);
            Gm.firstPersonController.enabled = true;
            tower.state = WatchTowerManager.State.Inside;
            var c = ui.transitionImage.color;
            ui.transitionImage.DOColor(new Color(c.r, c.g, c.b, 0f), 1f);
            yield return new WaitForSeconds(1f);
            ui.transitionImage.gameObject.SetActive(false);
            ui.inTransition = false;
        }
    }

    // The trail head to the campfire: wait for the partner, go alone, or (staying behind) not at all.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.FadeToNextScene))]
    internal static class TripTrailPatch
    {
        static bool Prefix(WatchTowerManager __instance) => TripSync.Instance == null || TripSync.Instance.BeforeTrail(__instance);
    }
}
