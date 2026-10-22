using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Recurring chores built from the game's own mechanics so the second player always has something to do and the
    // pair splits up: the generator by the shack stalls (ground), the tower stove burns out (needs firewood from below).
    // The host schedules; either player fixes; results mirror through StorySync.
    internal sealed class CoopDuties
    {
        internal enum Duty { None, Generator, Stove, Blinds }
        internal static CoopDuties Instance;
        readonly Plugin plugin;
        readonly System.Random random = new System.Random();
        float nextDutyAt;
        bool fixing;
        public Duty Active { get; private set; }
        public int Started { get; private set; }
        public int Completed { get; private set; }

        public CoopDuties(Plugin plugin) { this.plugin = plugin; Instance = this; }

        // Temporarily off: a stall during a radio talk left the generator unreachable (the talk's blockers stay up) and
        // the night stuck. Until it has a safe time and place, the lights only go out where the story cuts them.
        // The lab can still force one (Force("generator")).
        internal static bool GeneratorStalls = false;

        static WatchTowerManager Tower => GameManager.instance ? GameManager.instance.watchTowerManager : null;

        public void SceneLoaded() { Active = Duty.None; nextDutyAt = 0; fixing = false; }

        // Calm nights only: hunts (5, 8) and the scripted power cut (7) keep their original pacing; night 1 is the
        // first evening's chain of story tasks (a chore there undid them: the stove the story just lit, the blinds the
        // bed needs shut), night 2 is Tessa's while Marcus dreams, and the day after it (3) is daylight and the hike to the
        // campers (a cold stove or a stalled generator there made no sense).
        static bool CalmNight(WatchTowerManager tower) =>
            tower.seq == WatchTowerManager.Seq.Seq4 || tower.seq == WatchTowerManager.Seq.Seq6;

        public void Tick()
        {
            var tower = Tower;
            if (!tower || !plugin.Ready) return;
            HandleGeneratorClick(tower);
            if (!plugin.IsHost) return;
            var gm = GameManager.instance;
            if (Active != Duty.None)
            {
                bool done = Active == Duty.Generator ? tower.isGeneratorOn : Active == Duty.Stove ? tower.fireLit : tower.AllBlindsClosed();
                if (done) Finish(Active);
                return;
            }
            bool calm = CalmNight(tower) && plugin.PartnerPresent && !gm.isConversing && !gm.isDead && !(gm.uIManager && gm.uIManager.inTransition);
            if (!calm) return;
            if (nextDutyAt == 0) nextDutyAt = Time.unscaledTime + random.Next(150, 300);
            if (Time.unscaledTime >= nextDutyAt) StartRandom();
        }

        internal void StartRandom()
        {
            var tower = Tower;
            if (!tower || Active != Duty.None) return;
            var options = new List<Duty>();
            if (GeneratorStalls && tower.isGeneratorOn) options.Add(Duty.Generator);
            if (tower.fireLit) options.Add(Duty.Stove);
            if (tower.towerSunBlinds != null && tower.towerSunBlinds.Length > 1 && tower.AllBlindsClosed()) options.Add(Duty.Blinds);
            if (options.Count == 0) { nextDutyAt = Time.unscaledTime + 60; return; }
            Begin(options[random.Next(options.Count)], true);
        }

        internal void Force(string name)
        {
            if (Active != Duty.None) return;
            if (name == "generator") Begin(Duty.Generator, true);
            else if (name == "stove") Begin(Duty.Stove, true);
            else if (name == "blinds") Begin(Duty.Blinds, true);
        }

        void Begin(Duty duty, bool local)
        {
            var tower = Tower;
            if (!tower) return;
            Active = duty;
            fixing = false;
            if (local)
            {
                Started++;
                if (duty == Duty.Generator) StallGenerator(tower); else if (duty == Duty.Stove) PutOutStove(tower); else BlowBlindsOpen(tower);
                plugin.Send(new Packet { kind = "duty", code = duty.ToString(), arg = 1, scene = SceneManager.GetActiveScene().name });
            }
            if (duty == Duty.Stove) OpenWoodPile(tower);
            Notice(duty == Duty.Generator
                ? Text("Свет погас... похоже, заглох генератор. Кому-то нужно спуститься к сараю и завести его.",
                       "The lights died... the generator must have stalled. Someone has to restart it down by the shack.")
                : duty == Duty.Stove
                ? Text("Огонь в печи погас, становится холодно. Нужны дрова из поленницы внизу и спички.",
                       "The stove fire went out and it's getting cold. Bring firewood from the pile below and light it.")
                : Text("Ветер распахнул жалюзи на вышке. Закройте их снаружи, на балконе.",
                       "The wind threw the tower blinds open. Close them from the balcony outside."));
            Plugin.Log("DUTY_START " + duty);
        }

        void Finish(Duty duty)
        {
            Completed++;
            Active = Duty.None;
            nextDutyAt = Time.unscaledTime + random.Next(240, 420);
            plugin.Send(new Packet { kind = "duty", code = duty.ToString(), arg = 0, scene = SceneManager.GetActiveScene().name });
            Notice(DoneText(duty));
            Plugin.Log("DUTY_DONE " + duty);
        }

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name || !Enum.TryParse<Duty>(packet.code, out var duty)) return;
            if (packet.arg == 1) Begin(duty, false);
            else if (Active == duty)
            {
                Active = Duty.None;
                Notice(DoneText(duty));
                Plugin.Log("DUTY_DONE " + duty);
            }
        }

        // Power loss from the generator only; unlike PowerOut it does not seat or move the player.
        static void StallGenerator(WatchTowerManager tower)
        {
            tower.isGeneratorOn = false;
            tower.redLight.SetActive(true);
            tower.greenLight.SetActive(false);
            tower.generatorRunningAS.Stop();
            tower.poweroutBeep.Play();
            tower.generator.tag = "Clickable";
            tower.shedLight.SetActive(false);
            tower.shedLightMaterial.DisableKeyword("_EMISSION");
            tower.isShedLightOn = false;
            foreach (var light in tower.outsideLights) light.SetActive(false);
            tower.floodLight.DisableKeyword("_EMISSION");
            tower.lamp.SetActive(false);
            tower.lampMaterial.DisableKeyword("_EMISSION");
            tower.radioScreenMaterial.DisableKeyword("_EMISSION");
            tower.middleLight.SetActive(false);
            tower.tubeLight.SetActive(false);
            tower.tubeLightMaterial.DisableKeyword("_EMISSION");
        }

        static void PutOutStove(WatchTowerManager tower)
        {
            tower.fireStove.Stop(true);
            tower.fireAS.Stop();
            tower.fireLight.SetActive(false);
            tower.fireLit = false;
            tower.woodInStove = false;
            // The old logs are burnt; the gas step is skipped so only firewood and a match are needed.
            tower.gasOnWood = true;
            if (tower.wood) tower.wood.SetActive(false);
        }

        static string DoneText(Duty duty) => duty == Duty.Generator ? Text("Генератор снова работает.", "The generator is running again.")
            : duty == Duty.Stove ? Text("В печи снова горит огонь.", "The fire is burning again.") : Text("Жалюзи закрыты.", "The blinds are shut.");

        // Two or three blinds swing open with their own sound, as if the wind caught them.
        void BlowBlindsOpen(WatchTowerManager tower)
        {
            var blinds = new List<TowerSunBlind>(tower.towerSunBlinds);
            int count = Math.Min(blinds.Count, 2 + random.Next(2));
            for (int i = 0; i < count; i++)
            {
                int pick = random.Next(blinds.Count);
                blinds[pick].Open();
                blinds.RemoveAt(pick);
            }
        }

        static void OpenWoodPile(WatchTowerManager tower)
        {
            tower.canPickUpWood = true;
            if (tower.woodStorage && tower.woodStorage.TryGetComponent<BoxCollider>(out var collider)) collider.enabled = true;
        }

        // The game only lets nights 1 and 7 restart the generator by click; during a duty any night may.
        void HandleGeneratorClick(WatchTowerManager tower)
        {
            if (Active != Duty.Generator || fixing || tower.isGeneratorOn) return;
            if (tower.seq == WatchTowerManager.Seq.Seq1 || tower.seq == WatchTowerManager.Seq.Seq7) return;
            var gm = GameManager.instance;
            if (!gm.inputHandler || !gm.inputHandler.mouseLeftClickDown || gm.isPaused || gm.isConversing || (gm.uIManager && gm.uIManager.inTransition)) return;
            var camera = Camera.main;
            if (!camera || !Physics.Raycast(camera.ScreenPointToRay(gm.inputHandler.mousePosition), out var hit, 20f, tower.layerMask)) { Plugin.Log("DUTY_CLICK no hit"); return; }
            Plugin.Log("DUTY_CLICK hit=" + hit.collider.name + " generator=" + (tower.generator ? tower.generator.name : "null") + " distance=" + hit.distance.ToString("F2"));
            if (hit.collider.gameObject != tower.generator || hit.distance > 1.8f) return;
            fixing = true;
            plugin.StartCoroutine(RestartGenerator(tower));
        }

        IEnumerator RestartGenerator(WatchTowerManager tower)
        {
            var ui = GameManager.instance.uIManager;
            yield return ui.StartCoroutine(ui.FadeStartGenerator());
            // FadeStartGenerator brings back lamp and floodlights; the room lights follow the wall switch.
            if (tower.isLightOn)
            {
                tower.middleLight.SetActive(true);
                tower.tubeLight.SetActive(true);
                tower.tubeLightMaterial.EnableKeyword("_EMISSION");
            }
            fixing = false;
        }

        static void Notice(string text)
        {
            var subtitle = GameManager.instance ? GameManager.instance.subtitle : null;
            if (subtitle) subtitle.ShowSubAndClose(text, 6f);
        }

        static string Text(string russian, string english)
        {
            try
            {
                var locale = LocalizationSettings.SelectedLocale;
                if (locale != null && locale.Identifier.Code.StartsWith("ru")) return russian;
            }
            catch { }
            return english;
        }

        internal bool QuietRelight => Active == Duty.Stove;
    }

    // Relighting during the stove chore must not replay the night's story conversation.
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.LightFireInStove))]
    internal static class QuietRelightPatch
    {
        static bool Prefix(WatchTowerManager __instance)
        {
            if (CoopDuties.Instance == null || !CoopDuties.Instance.QuietRelight) return true;
            __instance.fireStove.Play(true);
            __instance.fireAS.Play();
            __instance.fireLit = true;
            __instance.fireLight.SetActive(true);
            return false;
        }
    }
}
