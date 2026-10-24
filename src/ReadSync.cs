using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IronbarkCoop
{
    // The story's text cards on black (the night openings, the scene intros, the ending): each player reads at their own
    // pace, and whoever finishes first waits on the last line with Scratch Creek's red "the other survivor is still
    // reading." until the partner has finished too; then both go on together (before, the faster reader went ahead and
    // the two stories drifted apart).
    internal static class ReadSync
    {
        static readonly HashSet<string> partnerDone = new HashSet<string>();
        static string waitingKey;
        static System.Action finish;
        static float waitingSince;
        static GameObject banner;
        internal static bool Bypass;
        internal static string Debug => waitingKey != null ? "waiting:" + waitingKey : "-";

        // The last line of a card was read here. True: go on now; false: wait for the partner (the card stays).
        internal static bool LastLineRead(string key, MonoBehaviour card, System.Action goOn)
        {
            var plugin = Plugin.Instance;
            if (Bypass || plugin == null || !plugin.Ready) return true;
            if (waitingKey == key) return false;
            plugin.Send(new Packet { kind = "read", code = "done", name = key, scene = SceneManager.GetActiveScene().name });
            if (partnerDone.Remove(key)) { Plugin.Log("READ_BOTH_DONE " + key); return true; }
            waitingKey = key; finish = goOn; waitingSince = Time.unscaledTime;
            Show(card);
            Plugin.Log("READ_WAITING " + key);
            return false;
        }

        internal static void Receive(Packet packet)
        {
            if (packet.code != "done" || string.IsNullOrEmpty(packet.name)) return;
            if (waitingKey == packet.name) { Plugin.Log("READ_PARTNER_DONE " + packet.name); GoOn(); }
            else partnerDone.Add(packet.name);
        }

        internal static void Tick()
        {
            if (waitingKey == null) return;
            var plugin = Plugin.Instance;
            // Alone again, or the partner never got this card: do not wait for ever.
            if (plugin == null || !plugin.Ready || Time.unscaledTime - waitingSince > 180f) { Plugin.Log("READ_STOP_WAITING " + waitingKey); GoOn(); }
        }

        static void GoOn()
        {
            var go = finish;
            waitingKey = null; finish = null;
            Hide();
            if (go == null) return;
            Bypass = true;
            try { go(); }
            finally { Bypass = false; }
        }

        internal static void SceneLoaded() { waitingKey = null; finish = null; Hide(); }

        // Scratch Creek's red line, in the card's own font.
        static void Show(MonoBehaviour card)
        {
            Hide();
            banner = new GameObject("Coop still reading");
            Object.DontDestroyOnLoad(banner);
            var canvas = banner.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = banner.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var textObject = new GameObject("Text");
            textObject.transform.SetParent(banner.transform, false);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            var own = card ? card.GetComponent<TMP_Text>() : null;
            if (own && own.font) text.font = own.font;
            text.text = ConnectionPanel.L("другой выживший всё ещё читает.", "the other survivor is still reading.");
            text.color = new Color(0.9f, 0.08f, 0.08f, 1f);
            text.fontSize = 64f;
            text.alignment = TextAlignmentOptions.Left;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.14f, 0.62f); rect.anchorMax = new Vector2(0.95f, 0.8f);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        static void Hide()
        {
            if (banner) Object.Destroy(banner);
            banner = null;
        }
    }

    // The night cards in the tower and the ending: the last line hands over to the game (introParent.isDone).
    [HarmonyPatch(typeof(IntroSubChainWatchtower), "NextText")]
    internal static class ReadTowerCardPatch
    {
        static readonly AccessTools.FieldRef<IntroSubChainWatchtower, GameObject> NextText = AccessTools.FieldRefAccess<IntroSubChainWatchtower, GameObject>("nextText");
        static readonly System.Reflection.MethodInfo Next = AccessTools.Method(typeof(IntroSubChainWatchtower), "NextText");

        static bool Prefix(IntroSubChainWatchtower __instance)
        {
            if (NextText(__instance)) return true;
            string key = "tower:" + (__instance.introParent ? __instance.introParent.name : __instance.name);
            return ReadSync.LastLineRead(key, __instance, () => Next.Invoke(__instance, null));
        }
    }

    // The scene intros (the first night's card, the evening after the campfire): the last line loads the scene.
    [HarmonyPatch(typeof(IntroSubChain), "NextText")]
    internal static class ReadSceneCardPatch
    {
        static readonly AccessTools.FieldRef<IntroSubChain, GameObject> NextText = AccessTools.FieldRefAccess<IntroSubChain, GameObject>("nextText");
        static readonly System.Reflection.MethodInfo Next = AccessTools.Method(typeof(IntroSubChain), "NextText");

        static bool Prefix(IntroSubChain __instance)
        {
            if (NextText(__instance)) return true;
            string key = "scene:" + SceneManager.GetActiveScene().name;
            return ReadSync.LastLineRead(key, __instance, () => Next.Invoke(__instance, null));
        }
    }
}
