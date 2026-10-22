using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Scratch Creek rules for personal moments: a scare happens only for the player who triggered it, the partner
    // just sees and hears (3D, nearby only) that player flinch; a death restarts that player alone, silently.
    internal sealed class PersonalMoments
    {
        internal static PersonalMoments Instance;
        readonly Plugin plugin;
        float nextReaction;
        Vector3 sceneSpawn;
        bool hasSpawn;
        public int Respawns { get; private set; }
        public bool Respawning { get; private set; }

        public PersonalMoments(Plugin plugin) { this.plugin = plugin; Instance = this; }

        public void SceneRegistered()
        {
            var gm = GameManager.instance;
            hasSpawn = gm && gm.playerFP;
            if (hasSpawn) sceneSpawn = gm.playerFP.transform.position;
        }

        internal void LocalScare(string source)
        {
            if (!plugin.Ready || Time.unscaledTime < nextReaction) return;
            nextReaction = Time.unscaledTime + 2f;
            plugin.Send(new Packet { kind = "react", code = source, scene = SceneManager.GetActiveScene().name });
            Plugin.Log("SCARE_LOCAL " + source);
        }

        // Called instead of GameManager.Death while a partner is connected.
        internal void RespawnAlone()
        {
            if (Respawning) return;
            plugin.StartCoroutine(Respawn());
        }

        IEnumerator Respawn()
        {
            Respawning = true;
            var gm = GameManager.instance;
            var ui = gm.uIManager;
            Plugin.Log("RESPAWN_START " + SceneManager.GetActiveScene().name);
            if (ui && ui.transitionImage)
            {
                ui.transitionImage.gameObject.SetActive(true);
                var color = ui.transitionImage.color; color.a = 1f; ui.transitionImage.color = color;
            }
            yield return new WaitForSecondsRealtime(1.2f);
            var tower = gm.watchTowerManager;
            if (tower) ResetTowerScare(tower);
            gm.isDead = false;
            if (gm.phone) gm.phone.enabled = true;
            if (gm.cameraShake) gm.cameraShake.enabled = false;
            if (gm.playerFP && !gm.playerFP.activeSelf) gm.playerFP.SetActive(true);
            if (gm.firstPersonController) gm.firstPersonController.enabled = true;
            var target = tower && tower.woodStovePosition ? tower.woodStovePosition.position + Vector3.up * 1.1f : sceneSpawn;
            if (tower || hasSpawn) gm.playerFP.transform.position = target;
            if (tower) tower.PlayerInside();
            gm.audioMixer.SetFloat("Ambience", tower ? tower.ambienceLevelInside : 0f);
            yield return new WaitForSecondsRealtime(1.5f);
            if (ui && ui.transitionImage)
            {
                float t = 0f;
                while (t < 1.5f)
                {
                    t += Time.unscaledDeltaTime;
                    var color = ui.transitionImage.color; color.a = 1f - Mathf.Clamp01(t / 1.5f); ui.transitionImage.color = color;
                    yield return null;
                }
                ui.transitionImage.gameObject.SetActive(false);
            }
            Respawns++;
            Respawning = false;
            Plugin.Log("RESPAWN_DONE at " + target.ToString("F2"));
        }

        // Undo what PerformJumpscareAtRun / Seq8PerformJumpscare* switched on for this player only.
        static void ResetTowerScare(WatchTowerManager tower)
        {
            foreach (var name in new[] { "anomaly", "blurFocus", "distortionDream", "realVHS", "filmGrain", "bloodPlus",
                "anomalyUnderBed", "blurFocusUnderBed", "distortionDreamUnderBed", "realVHSUnderBed", "filmGrainUnderBed", "bloodPlusUnderBed" })
                if (AccessTools.Field(typeof(WatchTowerManager), name)?.GetValue(tower) is Behaviour effect && effect) effect.enabled = false;
            foreach (var name in new[] { "jumpscareLight", "jumpScareLightUnderBed", "playerUnderBed", "playerInBed", "playerInBedSeq5", "playerInBedEating" })
                if (AccessTools.Field(typeof(WatchTowerManager), name)?.GetValue(tower) is GameObject go && go) go.SetActive(false);
            foreach (var name in new[] { "heartBeatAS", "glitchBeforeQuitAS", "guyScreamAS" })
                if (AccessTools.Field(typeof(WatchTowerManager), name)?.GetValue(tower) is AudioSource audio && audio) audio.Stop();
            foreach (var name in new[] { "cultist", "cultistSeq8" })
                if (AccessTools.Field(typeof(WatchTowerManager), name)?.GetValue(tower) is Component npc && npc) npc.gameObject.SetActive(false);
            // Personal hunt state goes back to "not hunting" so the scare does not fire again immediately.
            foreach (var field in typeof(WatchTowerManager).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.FieldType == typeof(bool) && System.Text.RegularExpressions.Regex.IsMatch(field.Name, "(?i)haunt|chas|farscare|closejump|farjump|triggerjump|lookatcultist"))
                    field.SetValue(tower, false);
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Death))]
    internal static class SoloDeathPatch
    {
        static bool Prefix()
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready || PersonalMoments.Instance == null) return true;
            PersonalMoments.Instance.RespawnAlone();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ScarePatch
    {
        static readonly string[] Names = {
            "PerformJumpscareAtRun", "Seq8PerformJumpscareAtRun", "Seq8PerformJumpscareUnderBed", "Seq8PerformJumpscareAtRunCrowd",
            "BadGuyJumpscare", "CrowdJumpScare", "HuntTriggered", "HuntTriggeredNear", "HuntTriggeredFar", "HuntTriggeredCrouched",
            "Seq8HuntTriggeredFar", "Seq8HuntTriggeredClose", "Seq8HuntTriggeredCrowd", "Seq8TriggerChase", "Seq8CultSeen",
            "WolfSoundAndLight", "DoorSlam" };

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in Names)
            {
                var method = AccessTools.Method(typeof(WatchTowerManager), name);
                if (method != null) yield return method;
            }
        }

        static void Prefix(MethodBase __originalMethod)
        {
            if (StorySync.Instance != null && StorySync.Instance.ApplyingRemote) return;
            PersonalMoments.Instance?.LocalScare(__originalMethod.Name);
        }
    }
}
