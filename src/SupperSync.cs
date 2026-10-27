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
            + "/mine" + (Tower && Tower.casserole && Tower.casserole == second ? "2" : "1")
            + "/mw" + (Tower && Tower.casseroleColliderInsideMicrowave ? (Tower.casseroleColliderInsideMicrowave.enabled ? "open" : "shut") + (Tower.seq6CasseroleInsideMicrowave ? "In" : "") + (Tower.seq6MicrowaveOn ? "On" : "") : "-")
            + "/stack" + (first && second && first.transform.parent == second.transform.parent && Tower && first.transform.parent == Tower.casseroleParentInsideFridge ? (second.transform.position.y - first.transform.position.y).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "-");

        public static void SceneLoaded() { KaylaStarted = false; lockedTray = null; PartnerAte = false; first = null; second = null; warmSent.Clear(); }

        static WatchTowerManager Tower => GameManager.instance ? GameManager.instance.watchTowerManager : null;
        internal const string SecondKey = "Casserole:coop-second-portion", FirstKey = "Casserole:coop-first-portion";

        // Whichever tray is in this player's hand is the game's dish here.
        public static void Tick()
        {
            var tower = Tower;
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            if (!tower || !pickup || !second) return;
            LockPartnersTray();
            Fridge(tower);
            Microwave(tower);
            // Not while the microwave runs here: the game warms whatever is "the dish" when its timer ends.
            if (!pickup.inHandCasserole || !pickup.currentItem || tower.seq6MicrowaveOn) return;
            if (!pickup.currentItem.TryGetComponent<Casserole>(out var held) || held == tower.casserole) return;
            tower.casserole = held;
            Plugin.Log("SUPPER_MY_TRAY " + (held == second ? "second" : "first"));
        }

        // The game puts the dish back in the fridge at one spot: the second tray went into the first (playtest). It
        // stands on top of the other one instead.
        const float Stacked = 0.075f;
        static void Fridge(WatchTowerManager tower)
        {
            var spot = tower.casseroleParentInsideFridge;
            if (!spot || !first || first.transform.parent != spot || second.transform.parent != spot) return;
            if ((first.transform.position - spot.position).sqrMagnitude < 1e-4f && (second.transform.position - (spot.position + Vector3.up * Stacked)).sqrMagnitude < 1e-4f) return;
            // Always the second portion on top, whichever went in last: both worlds come to the same stack.
            first.transform.position = spot.position; first.transform.rotation = spot.rotation;
            second.transform.position = spot.position + Vector3.up * Stacked;
            second.transform.rotation = spot.rotation;
            Plugin.Log("SUPPER_FRIDGE_STACKED");
        }

        // Night 6: one tray in the microwave at a time. The game shuts its spot after a tray goes in and never opens it
        // again (it knows one dish), and the partner's world does not see the tray go in at all: the spot is open here
        // exactly while no tray is in it and it is not running.
        static void Microwave(WatchTowerManager tower)
        {
            var spot = tower.casseroleColliderInsideMicrowave;
            if (tower.seq != WatchTowerManager.Seq.Seq6 || !spot) return;
            bool inside = (first && first.transform.parent == spot.transform && first.gameObject.activeInHierarchy) || (second && second.transform.parent == spot.transform && second.gameObject.activeInHierarchy);
            bool busy = inside || tower.seq6MicrowaveOn;
            if (tower.seq6CasseroleInsideMicrowave != inside && !tower.seq6MicrowaveOn) tower.seq6CasseroleInsideMicrowave = inside;
            if (spot.enabled == busy) spot.enabled = !busy;
            // Warmed up here: warm in the partner's world too (whoever eats it there, or takes it out).
            foreach (var tray in new[] { first, second })
            {
                if (!tray || tray.isCold || tray.transform.parent != spot.transform || warmSent.Contains(tray)) continue;
                warmSent.Add(tray);
                var plugin = Plugin.Instance;
                var key = plugin != null && plugin.Ready ? plugin.Items.KeyOf(tray.gameObject) : null;
                if (key != null) plugin.Send(new Packet { kind = "supper", code = "warm", name = key, scene = SceneManager.GetActiveScene().name });
                Plugin.Log("SUPPER_WARMED " + (tray == second ? "second" : "first"));
            }
        }

        static readonly System.Collections.Generic.HashSet<Casserole> warmSent = new System.Collections.Generic.HashSet<Casserole>();

        // Night 6's leftovers: the game takes its one dish out of the fridge cold and half eaten. Both trays are there,
        // the second standing on the first (a night 6 started from the menu has no second tray yet: it is made here,
        // in both worlds, as after baking); the game's dish is the first one in both worlds until someone takes one.
        internal static void Leftovers()
        {
            var plugin = Plugin.Instance;
            var tower = Tower;
            if (plugin == null || !plugin.Ready || !tower || !tower.casserole || !tower.casseroleParentInsideFridge) return;
            if (!first) first = tower.casserole == second ? null : tower.casserole;
            if (!first) return;
            if (!second)
            {
                var copy = Object.Instantiate(first.gameObject, first.transform.parent);
                copy.name = "Coop casserole (second portion)";
                second = copy.GetComponent<Casserole>();
            }
            plugin.Items.Rekey(first.gameObject, FirstKey);
            plugin.Items.Rekey(second.gameObject, SecondKey);
            var spot = tower.casseroleParentInsideFridge;
            foreach (var tray in new[] { first, second })
            {
                tray.gameObject.SetActive(true);
                tray.transform.SetParent(spot, false);
                tray.transform.localPosition = Vector3.zero; tray.transform.localEulerAngles = Vector3.zero;
                tray.HalfEaten();
                tray.isEating = false;
                tray.StopPS();
                if (tray.pointLight) tray.pointLight.SetActive(false);
                if (tray.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
            }
            second.transform.position = spot.position + Vector3.up * Stacked;
            tower.casserole = first;
            Plugin.Log("SUPPER_LEFTOVERS two trays in the fridge");
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
                case "warm":
                    var warmed = Plugin.Instance != null ? Plugin.Instance.Items.Find(packet.name) : null;
                    if (warmed && warmed.TryGetComponent<Casserole>(out var warm) && warm.isCold)
                    {
                        warm.isCold = false; warm.isPickable = true; warm.PlayPS(); warmSent.Add(warm);
                        Plugin.Log("SUPPER_PARTNER_WARMED");
                    }
                    break;
                case "bite":
                    // The partner's tray (by its shared key) shows what is left on it, with the sound of eating.
                    var plugin = Plugin.Instance;
                    var tray = plugin != null ? plugin.Items.Find(packet.name) : null;
                    if (tray && tray.TryGetComponent<Casserole>(out var dish)) ShowLeft(dish, packet.arg);
                    break;
            }
        }

        // The game lets any tray be picked up while this player's own one may be (ItemPickUp checks only "the dish"), so
        // the tray on the partner's lap could be taken from under them while they ate, and their controls stayed stuck on
        // the bed (playtest). While the partner sits eating, the tray beside them cannot be clicked here.
        static Casserole lockedTray;
        static void LockPartnersTray()
        {
            var plugin = Plugin.Instance;
            Casserole wanted = null;
            if (plugin != null && plugin.PartnerSeat == "bed-eat")
                foreach (var tray in new[] { first, second })
                    if (tray && tray != Tower.casserole && tray.gameObject.activeInHierarchy && Vector3.Distance(tray.transform.position, plugin.PartnerPosition) < 2f) { wanted = tray; break; }
            if (wanted == lockedTray) return;
            if (lockedTray) SetClickable(lockedTray, true);
            lockedTray = wanted;
            if (wanted) SetClickable(wanted, false);
            Plugin.Log("SUPPER_PARTNER_TRAY " + (wanted ? "locked" : "free"));
        }

        static void SetClickable(Casserole tray, bool on)
        {
            foreach (var collider in tray.GetComponentsInChildren<Collider>(true)) collider.enabled = on;
            if (!on && tray.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
        }

        static void ShowLeft(Casserole dish, int eaten)
        {
            if (dish.finishedGameobjects == null || dish.finishedGameobjects.Length == 0) return;
            dish.finishedInt = eaten;
            for (int i = 0; i < dish.finishedGameobjects.Length; i++) if (dish.finishedGameobjects[i]) dish.finishedGameobjects[i].SetActive(i == eaten);
            // The tray's own eating sound is a 2D one (the eater's): the partner hears it from the tray, only nearby.
            var sound = BiteSound(dish);
            if (sound && dish.gameObject.activeInHierarchy) { sound.Play(); dish.StartCoroutine(PauseLater(sound, 5f)); }
            Plugin.Log("SUPPER_PARTNER_BITE " + eaten);
        }

        static AudioSource BiteSound(Casserole dish)
        {
            if (!dish.eatingAS || !dish.eatingAS.clip) return null;
            var holder = dish.transform.Find("Coop bite sound");
            if (holder) return holder.GetComponent<AudioSource>();
            var go = new GameObject("Coop bite sound");
            go.transform.SetParent(dish.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = dish.eatingAS.clip; source.volume = dish.eatingAS.volume; source.outputAudioMixerGroup = dish.eatingAS.outputAudioMixerGroup;
            source.playOnAwake = false; source.loop = dish.eatingAS.loop;
            source.spatialBlend = 1f; source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 1f; source.maxDistance = 7f;
            return source;
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

        static readonly AccessTools.FieldRef<WatchTowerManager, bool> MicrowaveOpen = AccessTools.FieldRefAccess<WatchTowerManager, bool>("microwaveDoorOpen");

        // Lab: the held tray into the open microwave, under the game's own conditions for that click (WatchTowerManager.Seq6Update).
        internal static void LabIntoMicrowave()
        {
            var tower = Tower;
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            var spot = tower ? tower.casseroleColliderInsideMicrowave : null;
            if (!spot || !pickup || !spot.enabled || !MicrowaveOpen(tower) || !pickup.inHandCasserole || !tower.casserole.isCold)
            {
                Plugin.Log("LAB_MICROWAVE refused spot=" + (spot && spot.enabled) + " door=" + (tower && MicrowaveOpen(tower)) + " held=" + (pickup && pickup.inHandCasserole));
                return;
            }
            var casserole = tower.casserole;
            casserole.isPickable = false;
            casserole.transform.parent = spot.transform;
            casserole.transform.localPosition = Vector3.zero;
            casserole.transform.localEulerAngles = Vector3.zero;
            tower.seq6CasseroleInsideMicrowave = true;
            pickup.inHand = false; pickup.inHandCount--; pickup.inHandCasserole = false;
            spot.enabled = false;
            Plugin.Log("LAB_MICROWAVE in " + (casserole == second ? "second" : "first"));
        }

        // Lab: the held tray back into the fridge, as the game's click there does it (WatchTowerManager.Seq4Update).
        internal static void LabIntoFridge()
        {
            var tower = Tower;
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            if (!tower || !pickup || !pickup.inHandCasserole || !tower.casseroleParentInsideFridge) { Plugin.Log("LAB_FRIDGE refused"); return; }
            var casserole = tower.casserole;
            casserole.transform.parent = tower.casseroleParentInsideFridge;
            casserole.transform.localPosition = Vector3.zero;
            casserole.transform.localEulerAngles = Vector3.zero;
            pickup.inHand = false; pickup.inHandCount--; pickup.inHandCasserole = false;
            if (tower.casseroleColliderInsideFridge) tower.casseroleColliderInsideFridge.enabled = false;
            Plugin.Log("LAB_FRIDGE " + (casserole == second ? "second" : "first"));
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

    // Night 6 starts with the leftovers in the fridge: both trays.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq6Start))]
    internal static class SupperLeftoversPatch
    {
        static void Postfix() => SupperSync.Leftovers();
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
            // Night 4's supper (four helpings) and night 6's leftovers (from four to eight).
            if (plugin == null || !plugin.Ready || !tower || (tower.seq != WatchTowerManager.Seq.Seq4 && tower.seq != WatchTowerManager.Seq.Seq6)) return;
            if (__instance.finishedInt != __state) SupperSync.Bitten(__instance);
            int full = tower.seq == WatchTowerManager.Seq.Seq4 ? 4 : 8;
            if (__state < full && __instance.finishedInt >= full)
            {
                plugin.Send(new Packet { kind = "supper", code = "ate", scene = SceneManager.GetActiveScene().name });
                Plugin.Log("SUPPER_ATE");
            }
        }
    }
}
