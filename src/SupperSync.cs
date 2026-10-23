using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Night 4's supper for two. Kayla's recipe comes to Tessa only, whoever had the radio talk with Connor (it came to
    // the speaker only, and the listener never got it); the players cook from it together by talking. The casserole is
    // one dish while it is put together (either player can add an ingredient, both see every one in it) and baked;
    // once baked, each player has a whole one of their own in their own world: a portion each, eaten in bed side by side.
    internal static class SupperSync
    {
        internal static bool KaylaStarted;
        // Whether each player has eaten their portion (four helpings, the game's "full").
        internal static bool PartnerAte;
        public static string Debug => "kayla" + (KaylaStarted ? "1" : "0") + "/partnerAte" + (PartnerAte ? "1" : "0");

        public static void SceneLoaded() { KaylaStarted = false; PartnerAte = false; }

        static WatchTowerManager Tower => GameManager.instance ? GameManager.instance.watchTowerManager : null;

        internal static void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name) return;
            var tower = Tower;
            switch (packet.code)
            {
                case "kayla":
                    if (KaylaStarted || !tower) return;
                    Plugin.Log("SUPPER_KAYLA_FROM_PARTNER");
                    tower.StartCoroutine(tower.TriggerSeq4Texts());
                    break;
                case "ate":
                    PartnerAte = true;
                    Plugin.Log("SUPPER_PARTNER_ATE");
                    break;
            }
        }

        // A baked casserole is a portion for each: it stops being the one shared dish.
        internal static bool Baked(Casserole casserole) => casserole && casserole.finishedParent && casserole.finishedParent.activeSelf;

        internal static void AfterBaking(Casserole casserole)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !casserole) return;
            // Baked in the partner's oven (their timer): it can be taken out here too.
            if (RemoteCalls.Applying) casserole.isPickable = true;
            plugin.Items.MakePersonal(casserole.gameObject);
            Plugin.Log("SUPPER_BAKED portions for each");
        }

        internal static IEnumerator Nothing() { yield break; }
    }

    // Kayla's texts after Connor's night 4 talk (with the recipe): Tessa's.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.TriggerSeq4Texts))]
    internal static class SupperKaylaPatch
    {
        static bool Prefix(WatchTowerManager __instance, ref IEnumerator __result)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready) return true;
            if (plugin.FollowsStory)
            {
                if (SupperSync.KaylaStarted) { __result = SupperSync.Nothing(); return false; }
                SupperSync.KaylaStarted = true;
                Plugin.Log("SUPPER_KAYLA_TEXTS");
                return true;
            }
            plugin.Send(new Packet { kind = "supper", code = "kayla", scene = SceneManager.GetActiveScene().name });
            Plugin.Log("SUPPER_KAYLA_SENT_TO_PARTNER");
            __result = SupperSync.Nothing();
            return false;
        }
    }

    [HarmonyPatch(typeof(Casserole), nameof(Casserole.FinishBaking))]
    internal static class SupperBakedPatch
    {
        static void Postfix(Casserole __instance) => SupperSync.AfterBaking(__instance);
    }

    // A full portion eaten (night 4): the partner is told (Marcus's evening goes on once both have eaten).
    [HarmonyPatch(typeof(Casserole), nameof(Casserole.Eat))]
    internal static class SupperAtePatch
    {
        static void Prefix(Casserole __instance, out int __state) => __state = __instance.finishedInt;
        static void Postfix(Casserole __instance, int __state)
        {
            var plugin = Plugin.Instance;
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            if (plugin == null || !plugin.Ready || !tower || tower.seq != WatchTowerManager.Seq.Seq4) return;
            if (__state < 4 && __instance.finishedInt >= 4)
            {
                plugin.Send(new Packet { kind = "supper", code = "ate", scene = SceneManager.GetActiveScene().name });
                Plugin.Log("SUPPER_ATE");
            }
        }
    }
}
