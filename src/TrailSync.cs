using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Trail start, same pattern as the diner: the ranger (his scare at the guard house, the keys, the gate, the
    // flashlight) runs in the lead's world (Marcus) and the partner watches him do it. The partner gets a flashlight
    // of their own when the lead receives one.
    internal sealed class TrailSync
    {
        readonly Plugin plugin;
        float nextSend;
        Vector3 position; Quaternion rotation; bool has; int animState = -1; float gateYaw = float.NaN;

        public TrailSync(Plugin plugin) { this.plugin = plugin; }

        static ParkRangerTrailStart Ranger => GameManager.instance && GameManager.instance.trailStartManager ? GameManager.instance.trailStartManager.parkRanger : null;
        static TrailStartManager Manager => GameManager.instance ? GameManager.instance.trailStartManager : null;

        // Marcus starts talking once he is this close to the ranger who scared the partner.
        const float TalkDistance = 8f;
        bool partnerScare;
        public bool PartnerScarePending => partnerScare;

        public void SceneLoaded() { has = false; animState = -1; gateYaw = float.NaN; partnerScare = false; }

        // ---- the guard house scare: whoever walks in first gets it; the talk that follows is Marcus's ----

        internal bool BeforeScare(TrailStartManager manager)
        {
            if (!plugin.Ready) return true;
            if (manager.jsDone) { Plugin.Log("TRAIL_SCARE_ALREADY_DONE"); return false; }
            manager.jsDone = true;
            if (plugin.LeadsStory) { Plugin.Log("TRAIL_SCARE_LEAD"); return true; }
            // The partner walked in: the scare is theirs (sound, gasp, heartbeat), the ranger jumps out in Marcus's world
            // (mirrored here) and waits there for Marcus to come and talk.
            if (manager.jumpscareAS) manager.jumpscareAS.Play();
            plugin.StartCoroutine(PartnerScareSounds(manager));
            plugin.Moments.LocalScare("guardhouse");
            plugin.Send(new Packet { kind = "trail", code = "scare", scene = SceneManager.GetActiveScene().name });
            Plugin.Log("TRAIL_SCARE_PARTNER");
            return false;
        }

        static IEnumerator PartnerScareSounds(TrailStartManager manager)
        {
            if (manager.nonFatalHeartbeatAS) manager.nonFatalHeartbeatAS.Play();
            yield return new WaitForSeconds(0.3f);
            if (manager && manager.gaspAS) manager.gaspAS.Play();
            yield return new WaitForSeconds(5.7f);
            if (manager && manager.nonFatalHeartbeatAS) manager.StartCoroutine(FadeAudioSource.FadeToZeroAndReset(manager.nonFatalHeartbeatAS, 1f));
        }

        public void ReceiveEvent(Packet packet)
        {
            var manager = Manager;
            if (!manager || packet.scene != SceneManager.GetActiveScene().name) return;
            if (packet.code == "scare" && plugin.LeadsStory && !partnerScare && manager.state != TrailStartManager.State.Jumpscare)
            {
                manager.jsDone = true;
                partnerScare = true;
                manager.state = TrailStartManager.State.Jumpscare;
                manager.invisibleWall.SetActive(false);
                manager.waitingForConvoTrigger = true;
                if (manager.parkRanger) manager.parkRanger.StartJumpScare();
                Plugin.Log("TRAIL_SCARE_FOR_PARTNER");
            }
        }

        // Marcus came close to the ranger who is waiting after the partner's scare: start the talk the game would start.
        void PartnerScareTalk()
        {
            var manager = Manager;
            var ranger = Ranger;
            if (!partnerScare || !manager || !ranger || !GameManager.instance.playerFP) return;
            if (!manager.waitingForConvoTrigger) { partnerScare = false; return; }
            if (ranger.state != ParkRangerTrailStart.State.JumpscareTalkToPlayer) return;
            if ((GameManager.instance.playerFP.transform.position - ranger.transform.position).sqrMagnitude > TalkDistance * TalkDistance) return;
            manager.trailStartConvoTrigger.isPlayerIn = true;
            partnerScare = false;
            Plugin.Log("TRAIL_SCARE_TALK_STARTS");
        }

        // While Marcus is still far away the waiting ranger must not pull Marcus's camera in (the game zooms the view
        // of the player the ranger talks to).
        internal bool RangerFarFromLead(ParkRangerTrailStart ranger)
        {
            if (!plugin.Ready || !plugin.LeadsStory || !partnerScare || ranger.state != ParkRangerTrailStart.State.JumpscareTalkToPlayer || !GameManager.instance.playerFP) return false;
            return (GameManager.instance.playerFP.transform.position - ranger.transform.position).sqrMagnitude > TalkDistance * TalkDistance;
        }

        public void Tick()
        {
            var ranger = Ranger;
            if (!ranger || !plugin.Ready) return;
            if (plugin.LeadsStory)
            {
                PartnerScareTalk();
                if (Time.unscaledTime < nextSend) return;
                nextSend = Time.unscaledTime + 0.1f;
                var t = ranger.transform;
                var animator = ranger.GetComponent<Animator>();
                plugin.Send(new Packet { kind = "npc", code = "ranger", scene = SceneManager.GetActiveScene().name, position = t.position, rotation = t.rotation,
                    arg = animator && animator.enabled ? animator.GetInteger("State") : -1, active = ranger.gameObject.activeSelf,
                    torch = ranger.gun && ranger.gun.activeSelf, pitch = ranger.gate ? ranger.gate.localEulerAngles.y : 0f });
                return;
            }
            if (!has) return;
            var agent = ranger.GetComponent<NavMeshAgent>();
            if (agent && agent.enabled) { agent.updatePosition = false; agent.updateRotation = false; }
            var rt = ranger.transform;
            float k = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            if ((rt.position - position).sqrMagnitude > 25f) rt.SetPositionAndRotation(position, rotation);
            else rt.SetPositionAndRotation(Vector3.Lerp(rt.position, position, k), Quaternion.Slerp(rt.rotation, rotation, k));
            if (agent && agent.enabled) agent.nextPosition = rt.position;
            if (ranger.gate && !float.IsNaN(gateYaw))
                ranger.gate.localRotation = Quaternion.Slerp(ranger.gate.localRotation, Quaternion.Euler(0f, gateYaw, 0f), k);
        }

        public void Receive(Packet packet)
        {
            var ranger = Ranger;
            if (!ranger || !plugin.FollowsStory || packet.scene != SceneManager.GetActiveScene().name) return;
            position = packet.position; rotation = packet.rotation.normalized; has = true; gateYaw = packet.pitch;
            var animator = ranger.GetComponent<Animator>();
            if (animator)
            {
                // The ranger walks by navigation in the lead's world; root motion here would fight the mirrored position.
                animator.applyRootMotion = false;
                if (packet.arg >= 0 && packet.arg != animState) { animState = packet.arg; animator.SetInteger("State", packet.arg); }
            }
            if (ranger.gun && ranger.gun.activeSelf != packet.torch) ranger.gun.SetActive(packet.torch);
            if (ranger.footsteps)
            {
                bool walking = (ranger.transform.position - position).sqrMagnitude > 0.0004f && packet.arg == 0;
                if (walking && !ranger.footsteps.isPlaying) ranger.footsteps.Play(); else if (!walking && ranger.footsteps.isPlaying) ranger.footsteps.Pause();
            }
        }

        // The lead was handed the flashlight: the partner takes their own.
        internal static void FlashlightReceived()
        {
            var plugin = Plugin.Instance;
            var items = GameManager.instance ? GameManager.instance.itemPickUp : null;
            if (plugin == null || !plugin.FollowsStory || !items || items.inHandFlashlight || !items.initialFlashLightInHand) return;
            items.TakeFlashLight();
            GameManager.instance.uIManager.controlsText.text = F2FLocalizedText.GetLocalizedText("ep4_controls", "FlashlightUse");
            Plugin.Log("TRAIL_FLASHLIGHT_GIVEN");
        }
    }

    // The partner's ranger is a puppet: his routine, scare and conversations belong to the lead.
    [HarmonyPatch]
    internal static class RangerFollowerPatch
    {
        internal static bool Follower => Plugin.Instance != null && Plugin.Instance.FollowsStory && GameManager.instance && GameManager.instance.trailStartManager;

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "StartTalkToPlayer", "StartJumpScare", "TalkToPlayer", "GetUp", "SitDown" })
            { var method = AccessTools.Method(typeof(ParkRangerTrailStart), name); if (method != null) yield return method; }
        }

        static bool Prefix() => !Follower;
    }

    [HarmonyPatch(typeof(ParkRangerTrailStart), "Update")]
    internal static class RangerUpdatePatch
    {
        static bool Prefix(ParkRangerTrailStart __instance)
        {
            if (RangerFollowerPatch.Follower) return false;
            var plugin = Plugin.Instance;
            return plugin == null || !plugin.Trail.RangerFarFromLead(__instance);
        }
    }

    [HarmonyPatch(typeof(TrailStartManager), nameof(TrailStartManager.TriggerJumpScare))]
    internal static class GuardHouseScarePatch
    {
        static bool Prefix(TrailStartManager __instance) => Plugin.Instance == null || Plugin.Instance.Trail.BeforeScare(__instance);
    }

    // Marcus took the keys: the key appears on the partner's screen too (the tower door opens for both anyway).
    [HarmonyPatch(typeof(ParkRangerTrailStart), nameof(ParkRangerTrailStart.TakeKeys))]
    internal static class KeysTakenPatch
    {
        static void Postfix() { var manager = GameManager.instance ? GameManager.instance.trailStartManager : null; if (manager) manager.KeysRecieved = true; }

        internal static void ShowKeys()
        {
            var ui = GameManager.instance ? GameManager.instance.uIManager : null;
            if (!ui || !ui.objectsParent || !ui.keys || ui.keys.gameObject.activeSelf && ui.objectsParent.activeSelf) return;
            ui.objectsParent.SetActive(true);
            ui.keys.gameObject.SetActive(true);
            Plugin.Log("TRAIL_KEYS_SHOWN");
        }
    }

    [HarmonyPatch]
    internal static class RangerFollowerRoutinePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "OpenGate", "SitCoroutine", "WaitForStandingConvo" })
            { var method = AccessTools.Method(typeof(ParkRangerTrailStart), name); if (method != null) yield return method; }
        }

        static bool Prefix(ref IEnumerator __result)
        {
            if (!RangerFollowerPatch.Follower) return true;
            __result = Empty();
            return false;
        }

        static IEnumerator Empty() { yield break; }
    }
}
