using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Night 6 (the day of the ranger's visit and the second supper) for two:
    // - the morning: Tessa wakes at the binoculars on the balcony (the game's morning), Marcus in bed (as with the coffee
    //   after night 2, the other way round);
    // - Billy the ranger: whoever opens the door talks to him; the partner sees him hand over the supplies box and walk
    //   down the stairs at the same time (before, he stood on in the partner's world and later vanished);
    // - the stove after supper: firewood is fetched by Marcus (taking it from the pile below is when the stranger shows
    //   up); Tessa stays upstairs until the stove is lit.
    internal sealed class Night6Sync
    {
        internal static Night6Sync Instance;
        readonly Plugin plugin;
        bool holding;
        float nextWaitLine;

        public Night6Sync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        static GameManager Gm => GameManager.instance;
        static WatchTowerManager Tower => Gm ? Gm.watchTowerManager : null;
        bool Night6 { get { var tower = Tower; return tower && tower.seq == WatchTowerManager.Seq.Seq6; } }
        internal static bool Applying;

        public string Debug => Night6 ? (holding ? "holding" : "free") + "/eaten" + (Tower.seq6FoodEaten ? 1 : 0) + "/report" + (Tower.seq6ReportDone ? 1 : 0) + "/fire" + (Tower.seq6FireLit ? 1 : 0)
            + "/blocker" + (Tower.seq6DownstairsBlocker && Tower.seq6DownstairsBlocker.activeSelf ? 1 : 0) + "/ranger" + (Tower.parkRanger && Tower.parkRanger.gameObject.activeSelf ? (Tower.parkRanger.walkingDownstairs ? "walking" : "on") : "off") : "-";

        public void SceneLoaded() { holding = false; }

        public void Tick()
        {
            var tower = Tower;
            if (!tower) return;
            // Tessa waits upstairs for the firewood while the stove is not lit yet (only with Marcus there to fetch it).
            bool hold = Night6 && plugin.Ready && plugin.FollowsStory && !tower.seq6FireLit && tower.seq6DownstairsBlocker;
            // Her own game lets her down once she has eaten and reported: not yet.
            if (hold && holding && !tower.seq6DownstairsBlocker.activeSelf) tower.seq6DownstairsBlocker.SetActive(true);
            if (hold == holding) return;
            holding = hold;
            var blocker = tower.seq6DownstairsBlocker;
            var sync = StorySync.Instance;
            if (hold)
            {
                sync?.LocalOnly.Add(blocker);
                blocker.SetActive(true);
                Plugin.Log("NIGHT6_TESSA_WAITS_UPSTAIRS");
            }
            else if (blocker)
            {
                // The game has let everyone down by now (fed, reported, stove lit): her blocker goes the same way, so
                // nothing stale is sent back once it is shared again.
                if (tower.seq6FireLit || !Night6) blocker.SetActive(false);
                sync?.LocalOnly.Remove(blocker);
                Plugin.Log("NIGHT6_TESSA_FREE");
            }
        }

        // Her "not yet" at the top of the stairs: the game's lines while she is hungry or the report is not sent; after
        // that, she is waiting for the firewood.
        internal void AtBlocker()
        {
            var tower = Tower;
            if (!holding || !tower.seq6FoodEaten || !tower.seq6ReportDone || Time.unscaledTime < nextWaitLine) return;
            nextWaitLine = Time.unscaledTime + 6f;
            string partner = string.IsNullOrEmpty(plugin.PartnerName) ? ConnectionPanel.L("Маркус", "Marcus") : plugin.PartnerName;
            Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("{0} принесёт дрова снизу. Подожду наверху, у печки.", "{0} is bringing the firewood up. I'll wait upstairs by the stove."), partner), 5f);
        }

        void Send(string code) => plugin.Send(new Packet { kind = "night6", code = code, scene = SceneManager.GetActiveScene().name });

        // The ranger hands the box over (the game's TakeSupplies) / walks away (ExitParkRangerConvo after his talk).
        internal void SuppliesTaken() { if (plugin.Ready && !Applying && Night6) { Send("supplies"); Plugin.Log("NIGHT6_SUPPLIES_SENT"); } }
        internal void RangerLeaves() { if (plugin.Ready && !Applying && Night6) { Send("ranger-leaves"); Plugin.Log("NIGHT6_RANGER_LEAVES_SENT"); } }

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name || !Night6) return;
            var ranger = Tower.parkRanger;
            if (!ranger) return;
            Applying = true;
            try
            {
                switch (packet.code)
                {
                    case "supplies":
                        if (ranger.takeSuppliesAS) ranger.takeSuppliesAS.Play();
                        if (ranger.suppliesBox) ranger.suppliesBox.SetActive(false);
                        if (ranger.animator) ranger.animator.SetInteger("State", 1);
                        Plugin.Log("NIGHT6_PARTNER_TOOK_SUPPLIES");
                        break;
                    case "ranger-leaves":
                        if (!ranger.gameObject.activeInHierarchy || ranger.walkingDownstairs) break;
                        ranger.UpdateHeadRig(0f, 0.25f);
                        ranger.WalkDownstairs();
                        Plugin.Log("NIGHT6_RANGER_WALKS_DOWN");
                        break;
                }
            }
            finally { Applying = false; }
        }
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.TakeSupplies))]
    internal static class RangerSuppliesPatch
    {
        static void Prefix() => Night6Sync.Instance?.SuppliesTaken();
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.ExitParkRangerConvo))]
    internal static class RangerLeavesPatch
    {
        static void Prefix(WatchTowerManager __instance) { if (__instance.seq6TalkingToBilly) Night6Sync.Instance?.RangerLeaves(); }
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq6BlockerSub))]
    internal static class WaitForFirewoodPatch
    {
        static void Postfix() => Night6Sync.Instance?.AtBlocker();
    }

    // The morning of night 6 (Seq6Start puts the player at the binoculars on the balcony): Marcus, who leads the
    // story, wakes up in bed instead; Tessa has the binoculars.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq6Start))]
    internal static class BinocularsMorningPatch
    {
        static void Postfix()
        {
            var plugin = Plugin.Instance;
            if (plugin && plugin.Ready && plugin.LeadsStory) SleepSync.Instance?.WakeInBed();
        }
    }
}
