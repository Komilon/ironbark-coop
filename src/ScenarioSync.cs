using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Playtest shortcuts that put both players back at the start of a scene to try it again (playtest request):
    //   Ctrl+F10  night 8, just before the binoculars: the opening card read, Connor's talks over, both up in the room;
    //   Ctrl+F11  the trail's end, before the cultist.
    // Either player presses it; both games go there together.
    internal static class ScenarioSync
    {
        static GameManager Gm => GameManager.instance;
        static string pending;
        static Coroutine running;

        internal static void Tick(Plugin plugin)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.F10)) Request(plugin, "binoculars");
            if (ctrl && Input.GetKeyDown(KeyCode.F11)) Request(plugin, "trailend");
        }

        static void Request(Plugin plugin, string name)
        {
            if (plugin.Ready) plugin.Send(new Packet { kind = "scenario", code = name });
            Go(plugin, name);
        }

        internal static void LabRequest(Plugin plugin, string name) => Request(plugin, name);

        internal static void Receive(Plugin plugin, Packet packet) => Go(plugin, packet.code);

        static void Go(Plugin plugin, string name)
        {
            Plugin.Log("SCENARIO " + name);
            if (running != null) { plugin.StopCoroutine(running); running = null; }
            if (name == "binoculars")
            {
                PlayerPrefs.SetInt("StartSeq", 8);
                pending = name;
                SceneManager.LoadScene("Watchtower");
            }
            else if (name == "trailend")
            {
                pending = null;
                SceneManager.LoadScene("Trail End");
            }
        }

        // After the scene has loaded: night 8 up to the binoculars.
        internal static void SceneLoaded(Plugin plugin)
        {
            if (pending == "binoculars" && SceneManager.GetActiveScene().name == "Watchtower") running = plugin.StartCoroutine(ToBinoculars(plugin));
            pending = null;
        }

        static IEnumerator ToBinoculars(Plugin plugin)
        {
            float until = Time.unscaledTime + 60f;
            while (Time.unscaledTime < until && !(Gm && Gm.watchTowerManager && Gm.watchTowerManager.seq == WatchTowerManager.Seq.Seq8)) yield return null;
            yield return new WaitForSecondsRealtime(6f);
            // The opening card, line by line (both games do it, so the "still reading" wait passes at once).
            until = Time.unscaledTime + 40f;
            while (Time.unscaledTime < until && Gm.uIManager.inTransition) { SkipCard(); yield return new WaitForSecondsRealtime(2f); }
            yield return new WaitForSecondsRealtime(2f);
            var tower = Gm.watchTowerManager;
            tower.seq8ConnorConvo1Done = true; tower.seq8ConnorConvo2Done = true;
            Gm.uIManager.EndDialogue();
            tower.ExitRadio();
            yield return new WaitForSecondsRealtime(2f);
            tower.state = WatchTowerManager.State.InBed;
            Gm.uIManager.StartCoroutine(Gm.uIManager.FadeToFPSFromBed());
            yield return new WaitForSecondsRealtime(4f);
            Gm.uIManager.controlsText.text = "";
            // Both get up at the same spot: Tessa a step away.
            if (plugin.Ready && plugin.FollowsStory && Gm.playerFP)
            {
                var feet = Gm.playerFP.transform.position;
                var controller = Gm.playerFP.GetComponent<CharacterController>();
                if (controller) feet.y -= controller.height * 0.5f - controller.center.y;
                SharedChase.PlacePlayer(SharedChase.Ground(feet + new Vector3(-1f, 0f, 0.6f)), Gm.playerFP.transform.eulerAngles.y);
            }
            Gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Ночь 8, перед биноклем. Ctrl+F10 — начать отсюда снова.", "Night 8, before the binoculars. Ctrl+F10 to start here again."), 5f);
            Plugin.Log("SCENARIO_READY binoculars");
            running = null;
        }

        // A night's opening card, line by line the way a key press moves it on.
        internal static int SkipCard()
        {
            var next = AccessTools.Method(typeof(IntroSubChainWatchtower), "NextText");
            int skipped = 0;
            for (int round = 0; round < 12; round++)
                foreach (var chain in Object.FindObjectsOfType<IntroSubChainWatchtower>())
                    if (Traverse.Create(chain).Field("textOpened").GetValue<bool>() && !Traverse.Create(chain).Field("nexted").GetValue<bool>()) { next.Invoke(chain, null); skipped++; }
            return skipped;
        }
    }
}
