using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Night 4's supper for two. Kayla's recipe comes to Tessa only, whoever had the radio talk with Connor (it came to
    // the speaker only, and the listener never got it); the players cook from it together by talking. The casserole is
    // one dish while it is put together (either player can add an ingredient, both see every one in it) and baked.
    // Baked, the oven holds two trays, a portion each: both are shared things like any other (taken out of the oven, in
    // a hand, thrown, put down: the same for both), and each player eats from the one they took, sitting on their own
    // end of the bed. The game knows one dish (WatchTowerManager.casserole): in each world it is pointed at the tray that
    // player took.
    internal static class SupperSync
    {
        internal static bool KaylaStarted;
        // Whether each player has eaten their portion (four helpings, the game's "full").
        internal static bool PartnerAte;
        static Casserole first, second;
        public static string Debug => "kayla" + (KaylaStarted ? "1" : "0") + "/partnerAte" + (PartnerAte ? "1" : "0") + "/trays" + (second ? 2 : first ? 1 : 0)
            + "/mine" + (Tower && Tower.casserole && Tower.casserole == second ? "2" : "1");

        public static void SceneLoaded() { KaylaStarted = false; PartnerAte = false; first = null; second = null; }

        static WatchTowerManager Tower => GameManager.instance ? GameManager.instance.watchTowerManager : null;
        internal const string SecondKey = "Casserole:coop-second-portion";

        // Whichever tray is in this player's hand is the game's dish here.
        public static void Tick()
        {
            var tower = Tower;
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            if (!tower || !pickup || !second || !pickup.inHandCasserole || !pickup.currentItem) return;
            if (!pickup.currentItem.TryGetComponent<Casserole>(out var held) || held == tower.casserole) return;
            tower.casserole = held;
            Plugin.Log("SUPPER_MY_TRAY " + (held == second ? "second" : "first"));
        }

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
                case "bite":
                    // The partner's tray (by its shared key) shows what is left on it, with the sound of eating.
                    var plugin = Plugin.Instance;
                    var tray = plugin != null ? plugin.Items.Find(packet.name) : null;
                    if (tray && tray.TryGetComponent<Casserole>(out var dish)) ShowLeft(dish, packet.arg);
                    break;
            }
        }

        static void ShowLeft(Casserole dish, int eaten)
        {
            if (dish.finishedGameobjects == null || dish.finishedGameobjects.Length == 0) return;
            dish.finishedInt = eaten;
            for (int i = 0; i < dish.finishedGameobjects.Length; i++) if (dish.finishedGameobjects[i]) dish.finishedGameobjects[i].SetActive(i == eaten);
            if (dish.eatingAS && dish.gameObject.activeInHierarchy) { dish.eatingAS.Play(); dish.StartCoroutine(PauseLater(dish.eatingAS, 5f)); }
            Plugin.Log("SUPPER_PARTNER_BITE " + eaten);
        }

        static IEnumerator PauseLater(AudioSource source, float seconds) { yield return new WaitForSeconds(seconds); if (source) source.Pause(); }

        // A baked casserole is a portion for each: it stops being the one shared dish.
        internal static bool Baked(Casserole casserole) => casserole && casserole.finishedParent && casserole.finishedParent.activeSelf;

        internal static void AfterBaking(Casserole casserole)
        {
            var plugin = Plugin.Instance;
            var tower = Tower;
            if (plugin == null || !casserole || !tower) return;
            // Baked in the partner's oven (their timer): it can be taken out here too.
            if (RemoteCalls.Applying) casserole.isPickable = true;
            if (!plugin.Ready || tower.seq != WatchTowerManager.Seq.Seq4 || second) return;
            // The second portion, a tray just like it, on the rack below toward the door (made in both worlds when the
            // dish is done); baked anywhere else, beside it.
            first = casserole;
            var copy = Object.Instantiate(casserole.gameObject, casserole.transform.parent);
            copy.name = "Coop casserole (second portion)";
            copy.transform.position = casserole.transform.position + SecondTrayOffset(tower, casserole.transform.position);
            copy.transform.rotation = casserole.transform.rotation;
            second = copy.GetComponent<Casserole>();
            second.isPickable = true; second.isEating = false;
            if (second.pointLight) second.pointLight.SetActive(false);
            if (copy.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
            plugin.Items.AddShared(copy, SecondKey);
            Plugin.Log("SUPPER_BAKED two portions");
        }

        static Vector3 SecondTrayOffset(WatchTowerManager tower, Vector3 at)
        {
            var oven = tower.insideStovePosition ? tower.insideStovePosition.transform.position : at;
            var outward = tower.ovenDoor ? tower.ovenDoor.transform.position - oven : Vector3.zero; outward.y = 0f;
            if ((at - oven).sqrMagnitude < 0.09f && outward.sqrMagnitude > 0.0001f) return Vector3.down * LowerRack + outward.normalized * TowardDoor;
            var side = outward.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, outward.normalized) : Vector3.right;
            return side * 0.45f;
        }

        internal static float LowerRack = 0.1f, TowardDoor = 0.08f;

        // Lab: the dish into the oven the way the game puts it there; a tray (1 the game's, 2 the second) into the hand
        // the way the game's pickup does.
        internal static void LabDishInOven()
        {
            var tower = Tower;
            if (!tower || !tower.casserole || !tower.insideStovePosition) return;
            var t = tower.casserole.transform;
            t.parent = null;
            t.position = tower.insideStovePosition.transform.position; t.rotation = tower.insideStovePosition.transform.rotation;
            Plugin.Log("LAB_DISH_IN_OVEN");
        }

        internal static void LabTake(int which)
        {
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            var tray = which == 2 ? second : first ? first : Tower ? Tower.casserole : null;
            if (!pickup || !tray || pickup.inHand) { Plugin.Log("LAB_TAKE_TRAY failed"); return; }
            var item = tray.gameObject;
            pickup.currentItem = item;
            item.transform.parent = pickup.itemHere;
            if (item.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
            item.transform.localPosition = new Vector3(-0.144f, 0.025f, 0.059f);
            item.transform.localRotation = Quaternion.Euler(new Vector3(-128.917f, 12.573f, -0.8220215f));
            pickup.currentItemType = ItemPickUp.CurrentItemType.Casserole;
            pickup.inHand = true; pickup.inHandCasserole = true; pickup.inHandCount = 1;
            Plugin.Log("LAB_TAKE_TRAY " + which);
        }

        // A bite of this player's own tray, for the partner to see.
        internal static void Bitten(Casserole dish)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready) return;
            var key = plugin.Items.KeyOf(dish.gameObject);
            if (key != null) plugin.Send(new Packet { kind = "supper", code = "bite", name = key, arg = dish.finishedInt, scene = SceneManager.GetActiveScene().name });
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
            if (__instance.finishedInt != __state) SupperSync.Bitten(__instance);
            if (__state < 4 && __instance.finishedInt >= 4)
            {
                plugin.Send(new Packet { kind = "supper", code = "ate", scene = SceneManager.GetActiveScene().name });
                Plugin.Log("SUPPER_ATE");
            }
        }
    }
}
