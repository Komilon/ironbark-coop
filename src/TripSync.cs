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
        ListenPanel panel;
        bool cursorWasShown, controllerWasEnabled;
        int chore;
        public string Debug => (Tower && Tower.laceyTrailTrigger && Tower.laceyTrailTrigger.TryGetComponent<OnTrigger>(out var trail) && !Entered(trail) ? "armed/" : "") + (StayingBehind ? "behind/chore" + chore + (napping ? "/napping" : "") : waiting ? "waiting" + (choosing ? "/asked" : "") : choosing ? "choosing" : partnerWaiting ? "partnerWaiting" : "-");

        static GameManager Gm => GameManager.instance;
        static WatchTowerManager Tower => Gm ? Gm.watchTowerManager : null;
        string PartnerName => string.IsNullOrEmpty(plugin.PartnerName) ? ConnectionPanel.L("напарник", "your partner") : plugin.PartnerName;
        bool PartnerIsTessa => (plugin.PartnerCharacter ?? (plugin.MyCharacter == "tessa" ? "marcus" : "tessa")) == "tessa";
        string Went => PartnerIsTessa ? ConnectionPanel.L("ушла", "went") : ConnectionPanel.L("ушёл", "went");
        string WillBeBack => PartnerIsTessa ? ConnectionPanel.L("она вернётся", "she is back") : ConnectionPanel.L("он вернётся", "he is back");

        // The partner is back from the campfire and the evening of night 4 follows: whoever napped wakes up in the bed
        // instead of being put at the trail beside the partner (the game's own night 4 start).
        internal static bool NapEndsInBed;

        // The napper's line on the evening card (the camper's own walk back in the dark is not theirs).
        internal string NapIntroLine()
        {
            bool tessa = plugin.MyCharacter == "tessa";
            return string.Format(ConnectionPanel.L("Я {0} почти до самого вечера. Когда я {1}, {2} уже {3} от костра.", "I slept almost until evening. When I woke up, {2} was already back from the campfire."),
                tessa ? "проспала" : "проспал", tessa ? "проснулась" : "проснулся", PartnerName, PartnerIsTessa ? "вернулась" : "вернулся");
        }

        internal void FollowingScene(string scene)
        {
            // Set on the first step (the evening's card); the tower that follows it keeps it until night 4 starts.
            if (!StayingBehind || scene == "Camping") return;
            NapEndsInBed = true;
            Plugin.Log("TRIP_NAP_ENDS_IN_BED " + scene);
        }

        public void SceneLoaded()
        {
            var scene = SceneManager.GetActiveScene().name;
            if (scene != "Seq 4 Intro" && scene != "Watchtower") NapEndsInBed = false;
            if (scene != "Camping" && scene != "Seq 4 Intro" && scene != "Watchtower") { BoardsShut = false; PartnerStayed = false; PartnerNapping = false; }
            campfireHold = false; campfireRelease = false;
            TaskList.Hide();
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
            TaskList.Hide();
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
        // The trail's trigger fires only once (OnTrigger.onlyOnce): whenever nobody leaves, it is armed again, so walking
        // back into it asks again.
        internal bool BeforeTrail(WatchTowerManager tower)
        {
            if (!plugin.Ready || tower.seq != WatchTowerManager.Seq.Seq3) return true;
            Rearm(tower);
            if (StayingBehind)
            {
                Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("{0} {1} к дыму один. Мне нужно остаться на вышке.", "{0} {1} to the smoke alone. I need to stay at the tower."), PartnerName, Went), 4f);
                return false;
            }
            if (choosing || waiting || opening) return false;
            if (partnerWaiting)
            {
                // Together: one bear spray between the two is enough.
                if (!Gm.itemPickUp.inHandSpray && !PartnerHasSpray) { BearSprayFirst(); return false; }
                plugin.Send(new Packet { kind = "trip", code = "together", scene = SceneManager.GetActiveScene().name });
                Plugin.Log("TRIP_TOGETHER");
                Go();
                return false;
            }
            // Not from inside the trigger's callback: Unity refuses to strip the copied dialogue panel there, and its
            // answers could not be clicked.
            opening = true;
            plugin.StartCoroutine(OpenNextFrame());
            return false;
        }

        bool opening;
        IEnumerator OpenNextFrame()
        {
            yield return null;
            opening = false;
            if (!choosing && !waiting && !StayingBehind && plugin.Ready) OpenChoice();
        }

        static readonly AccessTools.FieldRef<OnTrigger, bool> Entered = AccessTools.FieldRefAccess<OnTrigger, bool>("entered");
        static void Rearm(WatchTowerManager tower)
        {
            if (tower.laceyTrailTrigger && tower.laceyTrailTrigger.TryGetComponent<OnTrigger>(out var trigger)) Entered(trigger) = false;
        }

        static void BearSprayFirst() { Gm.subtitle.ShowSubAndClose(F2FLocalizedText.GetLocalizedText("ep4_subs", "BearSpray")); Plugin.Log("TRIP_NEEDS_SPRAY"); }

        // The game's own way down the trail, without its spray check (done above for one or both).
        static void Go()
        {
            PlayerPrefs.SetInt("InHandFlashlight", Gm.itemPickUp.inHandFlashlight ? 1 : 0);
            Gm.uIManager.FadeOut("Camping");
        }

        // At the trail head: waiting for the partner at once, in the game's dialogue panel, with one answer, "go now".
        // The partner reaching the trail head meanwhile takes both down it; Space steps back (no longer waiting).
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
            panel.ShowLine("", string.Format(ConnectionPanel.L("Подожду {0} здесь, у тропы. Или идти к дыму сразу?", "I'll wait for {0} here at the trail. Or go to the smoke right away?"), PartnerName));
            panel.ShowOptions(new[] { ConnectionPanel.L("Идти сразу", "Go right away") }, 1, Pick);
            panel.ShowHint(ConnectionPanel.L("[ПРОБЕЛ] Отойти", "[SPACE] Step back"));
            StartWaiting();
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
            // Going alone needs the bear spray in hand (the game's rule): the question stays open without it.
            if (!Gm.itemPickUp.inHandSpray) { BearSprayFirst(); return; }
            ClosePanel();
            GoAlone();
        }

        // The lab picks the answer like a click would.
        internal void LabPick(int index) { if (choosing) Pick(index); }

        void StartWaiting()
        {
            waiting = true;
            plugin.Send(new Packet { kind = "trip", code = "wait", scene = SceneManager.GetActiveScene().name });
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
            PartnerStayed = true; PartnerNapping = false;
            Go();
        }

        public void Receive(Packet packet)
        {
            // Between the tower and the campfire (different scenes).
            switch (packet.code)
            {
                case "boards": BoardsShut = true; Plugin.Log("TRIP_PARTNER_SHUT_BOARDS"); return;
                case "napping": PartnerNapping = true; Plugin.Log("TRIP_PARTNER_NAPPING"); return;
                case "camper-waiting":
                    if (StayingBehind && !napping)
                        Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("{0} уже {1} у костра и ждёт, пока я управлюсь с делами и прилягу.", "{0} is done at the campfire and waiting for me to finish the chores and lie down."), PartnerName, PartnerIsTessa ? "закончила" : "закончил"), 6f);
                    Plugin.Log("TRIP_PARTNER_WAITS_AT_CAMPFIRE");
                    return;
            }
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
                    if (choosing) ClosePanel();
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
                // Space steps back from the trail head: no longer waiting.
                bool away = Input.GetKeyDown(KeyCode.Space) || (Gm.inputHandler && Gm.inputHandler.spaceInput) || !plugin.Ready;
                if (away) { ClosePanel(); if (waiting) StopWaiting(plugin.Ready); Plugin.Log("TRIP_CHOICE_CLOSED"); }
                return;
            }
            if (waiting) StopWaiting(plugin.Ready);
            if (StayingBehind) Chores();
        }

        // ---- the one who stays: chores from the task list, then a nap until the partner is back ----
        // Stoke the stove (its fire is out, firewood from the pile below and a match), then shut the window boards; a
        // little after the stove is lit the generator stalls (the same stall as the old random chore) and has to be
        // restarted. Then the bed. A partner back from the campfire before that waits for the nap (CampfireEnd).

        float choreAt, stokedAt = -1f;
        int generator; // 0 running, 1 stalled, 2 restarted
        bool restarting, boardsTold;
        // Night 4 starts with the boards shut only when the one who stayed shut them (both worlds are told).
        internal static bool BoardsShut;
        // Set on the one who went alone: the partner stayed, and whether they are napping yet.
        internal static bool PartnerStayed, PartnerNapping;
        bool campfireHold, campfireRelease;

        void Chores()
        {
            var tower = Tower;
            if (!tower || napping) return;
            if (chore == 0)
            {
                if (Gm.uIManager.inTransition || Time.unscaledTime < choreAt) return;
                Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("{0} {1} проверить дым. Займусь пока делами на вышке, а потом подремлю, пока {2}.", "{0} {1} to check the smoke. I'll see to the tower meanwhile, then nap until {2}."), PartnerName, Went, WillBeBack), 6f);
                // The stove has burnt out since the morning.
                CoopDuties.PutOutStove(tower);
                CoopDuties.OpenWoodPile(tower);
                chore = 1; stokedAt = -1f; generator = 0; restarting = false; boardsTold = false;
                Plugin.Log("TRIP_CHORES_START");
            }
            bool boards = tower.AllBlindsClosed(), stove = tower.fireLit;
            if (boards && !boardsTold)
            {
                boardsTold = true; BoardsShut = true;
                plugin.Send(new Packet { kind = "trip", code = "boards", scene = SceneManager.GetActiveScene().name });
                Plugin.Log("TRIP_CHORE boards");
            }
            if (stove && stokedAt < 0f) stokedAt = Time.unscaledTime;
            if (generator == 0 && stokedAt > 0f && Time.unscaledTime - stokedAt > GeneratorAfter && !Gm.uIManager.inTransition && !Gm.isConversing)
            {
                CoopDuties.StallGenerator(tower);
                generator = 1;
                Gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Свет погас... Похоже, заглох генератор. Надо спуститься и завести его.", "The lights died... The generator must have stalled. I need to go down and restart it."), 5f);
                Plugin.Log("TRIP_CHORE generator stalled");
            }
            if (generator == 1 && !restarting && !tower.isGeneratorOn && !Gm.uIManager.inTransition && Clicked(tower.generator, 1.8f))
            {
                restarting = true;
                plugin.StartCoroutine(RestartGenerator(tower));
            }
            bool all = boards && stove && generator == 2;
            var tasks = new System.Collections.Generic.List<(string, bool)> {
                (ConnectionPanel.L("Подтопить печь", "Stoke the stove"), stove),
                (ConnectionPanel.L("Закрыть ставни на окнах", "Close the window boards"), boards) };
            if (generator > 0) tasks.Add((ConnectionPanel.L("Завести генератор у сарая", "Restart the generator by the shack"), generator == 2));
            if (all) tasks.Add((ConnectionPanel.L("Прилечь подремать на кровати", "Lie down on the bed for a nap"), false));
            TaskList.Show(tasks);
            if (all && !Gm.uIManager.inTransition && Clicked(tower.bed, 2.2f)) plugin.StartCoroutine(Nap());
        }

        internal static float GeneratorAfter = 20f;

        IEnumerator RestartGenerator(WatchTowerManager tower)
        {
            yield return CoopDuties.Restart(tower);
            restarting = false;
            if (tower.isGeneratorOn) { generator = 2; Plugin.Log("TRIP_CHORE generator restarted"); }
        }

        public string ChoresDebug => StayingBehind ? "boards" + (BoardsShut ? 1 : 0) + "/stove" + (Tower && Tower.fireLit ? 1 : 0) + "/gen" + generator + "/tasks" + TaskList.Debug : "-";

        // The partner who stayed is still busy when this player is done at the campfire: wait (screen dark) until they lie
        // down, then the evening of night 4 begins for both. True: the game's own end of the campfire runs.
        internal bool CampfireEnd(CampfireManager campfire)
        {
            if (campfireRelease || !plugin.Ready || !PartnerStayed || PartnerNapping) return true;
            if (!campfireHold) plugin.StartCoroutine(HoldAtCampfire(campfire));
            return false;
        }

        IEnumerator HoldAtCampfire(CampfireManager campfire)
        {
            campfireHold = true;
            var ui = Gm.uIManager;
            plugin.Send(new Packet { kind = "trip", code = "camper-waiting", scene = SceneManager.GetActiveScene().name });
            Plugin.Log("TRIP_CAMPER_WAITS");
            ui.inTransition = true;
            ui.transitionImage.gameObject.SetActive(true);
            var c = ui.transitionImage.color;
            ui.transitionImage.color = new Color(c.r, c.g, c.b, 0f);
            ui.transitionImage.DOColor(new Color(c.r, c.g, c.b, 1f), 1f);
            yield return new WaitForSeconds(1.1f);
            if (Gm.firstPersonController) Gm.firstPersonController.enabled = false;
            var hint = string.Format(ConnectionPanel.L("{0} ещё на вышке: ждём, пока закончит дела и приляжет...", "{0} is still at the tower: waiting until the chores are done and the nap begins..."), PartnerName);
            while (plugin.Ready && !PartnerNapping)
            {
                if (ui.controlsText.text != hint) ui.controlsText.text = hint;
                yield return new WaitForSeconds(0.5f);
            }
            ui.controlsText.text = "";
            campfireHold = false;
            campfireRelease = true;
            ui.inTransition = false;
            Plugin.Log("TRIP_CAMPER_RELEASED");
            try { campfire.FadeToNextScene(); } finally { campfireRelease = false; }
        }

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
            TaskList.Hide();
            plugin.Send(new Packet { kind = "trip", code = "napping", scene = SceneManager.GetActiveScene().name });
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

    // The evening of night 4 together. The stove is already burning (the night's own "light the stove" would keep Tessa
    // from bed while Marcus is at the porta-potty); the boards are shut only if whoever stayed in the tower shut them;
    // whoever napped wakes up in the bed.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq4Start))]
    internal static class Night4StartPatch
    {
        static void Postfix(WatchTowerManager __instance)
        {
            var plugin = Plugin.Instance;
            if (plugin != null && plugin.Ready)
            {
                StoveBurning(__instance);
                if (TripSync.BoardsShut) { foreach (var blind in __instance.towerSunBlinds) blind.CloseAtStart(); Plugin.Log("NIGHT4_BOARDS_SHUT"); }
            }
            TripSync.BoardsShut = false; TripSync.PartnerStayed = false; TripSync.PartnerNapping = false;
            if (!TripSync.NapEndsInBed) return;
            TripSync.NapEndsInBed = false;
            SleepSync.Instance?.WakeInBed();
            Plugin.Log("TRIP_WOKE_IN_BED");
        }

        static void StoveBurning(WatchTowerManager tower)
        {
            var pickup = GameManager.instance.itemPickUp;
            if (pickup && pickup.woodPilePrefab && tower.woodStovePosition && !tower.woodInStove)
            {
                var logs = Object.Instantiate(pickup.woodPilePrefab, tower.woodStovePosition);
                logs.transform.localPosition = Vector3.zero; logs.transform.localEulerAngles = Vector3.zero;
                logs.tag = "Untagged";
                if (logs.TryGetComponent<AudioSource>(out var thud)) thud.playOnAwake = false;
                tower.wood = logs;
            }
            tower.woodInStove = true;
            tower.gasOnWood = true;
            tower.LightFireInStove();
            Plugin.Log("NIGHT4_STOVE_BURNING");
        }
    }

    // The end of the campfire waits for the partner who stayed in the tower to lie down.
    [HarmonyPatch(typeof(CampfireManager), nameof(CampfireManager.FadeToNextScene))]
    internal static class CampfireEndPatch
    {
        static bool Prefix(CampfireManager __instance) => TripSync.Instance == null || TripSync.Instance.CampfireEnd(__instance);
    }

    // The trail head to the campfire: wait for the partner, go alone, or (staying behind) not at all.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.FadeToNextScene))]
    internal static class TripTrailPatch
    {
        static bool Prefix(WatchTowerManager __instance) => TripSync.Instance == null || TripSync.Instance.BeforeTrail(__instance);
    }
}

namespace IronbarkCoop
{
    // Going downstairs on day 3 without the bear spray reminds the player to take it for the trail; the one staying in
    // the tower while the partner is at the campfire is not going anywhere (playtest report: it came every time).
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.PlayerDownstairs))]
    internal static class StayingBehindSprayPatch
    {
        static bool Prefix(WatchTowerManager __instance)
        {
            if (TripSync.Instance == null || !TripSync.Instance.StayingBehind || __instance.seq != WatchTowerManager.Seq.Seq3) return true;
            __instance.state = WatchTowerManager.State.OutsideDownstairs;
            return false;
        }
    }
}

namespace IronbarkCoop
{
    // The evening card after the campfire ("as I walked back to my tower in the dark...") is the camper's: the one who
    // napped in the tower (playtest report) reads one line of their own instead, and the card ends after it.
    [HarmonyPatch(typeof(IntroSubChain), nameof(IntroSubChain.ShowText))]
    internal static class NapIntroPatch
    {
        static readonly AccessTools.FieldRef<IntroSubChain, GameObject> NextText = AccessTools.FieldRefAccess<IntroSubChain, GameObject>("nextText");
        static int replacedIn = -1;

        static void Prefix(IntroSubChain __instance)
        {
            if (!TripSync.NapEndsInBed || SceneManager.GetActiveScene().name != "Seq 4 Intro" || TripSync.Instance == null) return;
            int scene = SceneManager.GetActiveScene().handle;
            if (replacedIn == scene) return;
            replacedIn = scene;
            var text = __instance.GetComponent<TMPro.TextMeshProUGUI>();
            if (text) text.text = TripSync.Instance.NapIntroLine();
            NextText(__instance) = null;
            Plugin.Log("TRIP_NAP_INTRO " + (text ? text.text : "-"));
        }
    }
}
