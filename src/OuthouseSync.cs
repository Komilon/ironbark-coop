using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // The porta-potty below the tower opens and closes its door (and lid) in the clicking player's world only: the game
    // reads the click itself (PortaPotty.DoorLogic). The partner's door and lid now move and sound the same; the
    // clicker's own side effects (the outdoor ambience level, the light and the see-through door from inside, the
    // finale's hiding) stay with the clicker.
    internal static class OuthouseSync
    {
        static readonly AccessTools.FieldRef<PortaPotty, bool> Opened = AccessTools.FieldRefAccess<PortaPotty, bool>("opened");
        static readonly AccessTools.FieldRef<PortaPotty, bool> LidOpened = AccessTools.FieldRefAccess<PortaPotty, bool>("lidOpened");
        static bool applying;
        public static int Sent { get; private set; }
        public static int Applied { get; private set; }

        // The finale's chase opens and slams it as part of a personal scare.
        static bool Personal(PortaPotty potty)
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            return tower && (tower.seq8JumpscareHaunting || tower.seq8JumpscareChasing);
        }

        internal static void Local(PortaPotty potty, string code)
        {
            var plugin = Plugin.Instance;
            if (applying || plugin == null || !plugin.Ready || !potty || Personal(potty)) return;
            plugin.Send(new Packet { kind = "potty", code = code, name = ItemSync.PathOf(potty.transform), scene = SceneManager.GetActiveScene().name });
            Sent++;
        }

        public static void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name) return;
            var target = ItemSync.FindPath(packet.name);
            var potty = target ? target.GetComponent<PortaPotty>() : null;
            if (!potty) potty = Object.FindObjectOfType<PortaPotty>();
            if (!potty) return;
            applying = true;
            // Sounds made here are the partner's (heard in a dream, too).
            RemoteCalls.Applying = true;
            try
            {
                var lid = potty.lid;
                var lidSound = lid ? lid.GetComponent<AudioSource>() : null;
                var lidAnimator = lid ? lid.GetComponent<Animator>() : null;
                switch (packet.code)
                {
                    case "open":
                        if (Opened(potty)) break;
                        potty.GetComponent<Animator>().Play("DoorOpenPortapotty");
                        Opened(potty) = true; LidOpened(potty) = false;
                        if (potty.doorAS && potty.openClip) potty.doorAS.PlayOneShot(potty.openClip);
                        break;
                    case "close":
                        if (!Opened(potty)) break;
                        potty.GetComponent<Animator>().Play("DoorClosePortapotty");
                        Opened(potty) = false; LidOpened(potty) = false;
                        if (potty.doorAS && potty.closeClip) potty.doorAS.PlayOneShot(potty.closeClip);
                        break;
                    case "lid-open":
                        if (lidAnimator) lidAnimator.Play("LitOpenPortaPotty");
                        if (lidSound && potty.lidOpen) lidSound.PlayOneShot(potty.lidOpen);
                        LidOpened(potty) = true;
                        break;
                    case "lid-close":
                        if (lidAnimator) lidAnimator.Play("LitClosePortaPotty");
                        if (lidSound && potty.lidClose) lidSound.PlayOneShot(potty.lidClose);
                        LidOpened(potty) = false;
                        break;
                }
                Applied++;
                Plugin.Log("POTTY_REMOTE " + packet.code);
            }
            finally { applying = false; RemoteCalls.Applying = false; }
        }

        internal static bool IsOpen(PortaPotty potty) => potty && Opened(potty);
    }

    [HarmonyPatch(typeof(PortaPotty), nameof(PortaPotty.Open))]
    internal static class OuthouseOpenPatch
    {
        static void Prefix(PortaPotty __instance, out bool __state) => __state = OuthouseSync.IsOpen(__instance);
        static void Postfix(PortaPotty __instance, bool __state) { if (!__state && OuthouseSync.IsOpen(__instance)) OuthouseSync.Local(__instance, "open"); }
    }

    [HarmonyPatch(typeof(PortaPotty), nameof(PortaPotty.Close))]
    internal static class OuthouseClosePatch
    {
        static void Prefix(PortaPotty __instance, out bool __state) => __state = OuthouseSync.IsOpen(__instance);
        static void Postfix(PortaPotty __instance, bool __state) { if (__state && !OuthouseSync.IsOpen(__instance)) OuthouseSync.Local(__instance, "close"); }
    }

    [HarmonyPatch(typeof(PortaPotty), nameof(PortaPotty.OpenLid))]
    internal static class OuthouseLidOpenPatch
    {
        static void Postfix(PortaPotty __instance) => OuthouseSync.Local(__instance, "lid-open");
    }

    [HarmonyPatch(typeof(PortaPotty), nameof(PortaPotty.CloseLid))]
    internal static class OuthouseLidClosePatch
    {
        static void Postfix(PortaPotty __instance) => OuthouseSync.Local(__instance, "lid-close");
    }
}
