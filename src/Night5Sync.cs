using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Night 5, one cultist for both (playtest choice "A"). His walk around the balcony, the ritual and his way down
    // run in the story lead's world only; the partner's cultist is a copy that follows it (pose, animation, his
    // sounds), as the waitress in the diner does. What the partner's game would ask of him (start walking once she
    // looked at the window, stop and wait or hide when she opens the door) is sent to the lead's cultist instead.
    // Scares stay personal, as in Scratch Creek: whoever makes him hunt is hunted in their own world (the copy is let
    // go for that), and in the lead's world he runs at the partner's body. After a death (a lone respawn) he is gone for
    // the night and leaves the skull behind, so the night can go on to Connor's talk and bed.
    internal sealed class Night5Sync
    {
        internal static Night5Sync Instance;
        readonly Plugin plugin;
        public Night5Sync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        static GameManager Gm => GameManager.instance;
        static WatchTowerManager Tower => Gm ? Gm.watchTowerManager : null;
        static readonly AccessTools.FieldRef<Cultist, Animator> AnimatorOf = AccessTools.FieldRefAccess<Cultist, Animator>("animator");
        static readonly AccessTools.FieldRef<Cultist, Transform> LookAtOf = AccessTools.FieldRefAccess<Cultist, Transform>("lookAt");
        static readonly System.Reflection.MethodInfo SetDestination = AccessTools.Method(typeof(Cultist), "SetDestination");

        bool Night5 { get { var tower = Tower; return tower && tower.seq == WatchTowerManager.Seq.Seq5 && tower.cultist; } }
        internal bool Lead => plugin.Ready && plugin.LeadsStory;
        // The partner's copy: driven by the lead's cultist, except during this player's own hunt.
        internal bool Copy => plugin.Ready && !plugin.LeadsStory && Night5 && !localHunt;
        internal static bool Applying;

        bool localHunt, huntPartner, gone, startedEarly, torchWasOn;
        float nextPose, nextChase;
        Transform partnerMark;
        // The copy's target pose.
        Vector3 toPosition; Quaternion toRotation; int toAnim = int.MinValue; float toHead = -1f; bool havePose;
        int soundBits;
        public string Debug
        {
            get
            {
                var tower = Tower;
                if (!Night5) return "-";
                var c = tower.cultist;
                return (Lead ? "lead" : localHunt ? "hunted" : "copy") + "/" + (c.gameObject.activeSelf ? "on" : "off") + "/" + c.area + "/" + c.state
                    + "/" + c.transform.position.x.ToString("F1", CultureInfo.InvariantCulture) + ";" + c.transform.position.z.ToString("F1", CultureInfo.InvariantCulture)
                    + (huntPartner ? "/huntPartner" : "") + (gone ? "/gone" : "") + (tower.seq5CultistGone ? "/away" : "") + (tower.cultistFlashlight && tower.cultistFlashlight.activeSelf ? "/torch" : "") + (tower.seq5HarpPlayed ? "/harp" : "") + (tower.seq5CanGetOutOfBed ? "/canGetUp" : "");
            }
        }

        public void SceneLoaded() { localHunt = false; huntPartner = false; gone = false; startedEarly = false; havePose = false; toAnim = int.MinValue; torchWasOn = false; soundBits = 0; if (partnerMark) Object.Destroy(partnerMark.gameObject); partnerMark = null; }

        public void Tick()
        {
            if (!Night5 || !plugin.Ready) return;
            var tower = Tower;
            var cultist = tower.cultist;
            // The flashlight in the window goes out for both when either looks at it from the bed.
            bool torch = tower.cultistFlashlight && tower.cultistFlashlight.activeSelf;
            if (torchWasOn && !torch && !Applying) Send("torch-off");
            torchWasOn = torch;
            if (Lead)
            {
                if (huntPartner) ChasePartner(cultist);
                if (Time.unscaledTime >= nextPose) { nextPose = Time.unscaledTime + 0.1f; SendPose(cultist); }
            }
            else if (Copy && cultist.navMeshAgent && cultist.navMeshAgent.enabled) cultist.navMeshAgent.enabled = false;
        }

        void Send(string code, string name = null) => plugin.Send(new Packet { kind = "cultist", code = code, name = name, scene = SceneManager.GetActiveScene().name });

        void SendPose(Cultist c)
        {
            var animator = AnimatorOf(c);
            int bits = (c.footsteps && c.footsteps.isPlaying ? 1 : 0) | (c.crackingAS && c.crackingAS.isPlaying ? 2 : 0) | (c.dropAnimalAS && c.dropAnimalAS.isPlaying ? 4 : 0) | (c.realizeAS && c.realizeAS.isPlaying ? 8 : 0);
            plugin.Send(new Packet {
                kind = "cultist", code = "pose", scene = SceneManager.GetActiveScene().name, active = c.gameObject.activeSelf,
                position = c.transform.position, rotation = c.transform.rotation,
                arg = animator ? animator.GetInteger("State") : 0, pitch = c.head ? c.head.weight : 0f,
                build = (int)c.area + "," + (int)c.state + "," + (c.ritualDone ? 1 : 0) + "," + (c.hidingAroundCorner ? 1 : 0) + "," + (c.walkDownstairs ? 1 : 0) + "," + bits
            });
        }

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name || !Night5) return;
            var tower = Tower;
            var cultist = tower.cultist;
            switch (packet.code)
            {
                case "pose":
                    if (plugin.LeadsStory) return;
                    if (cultist.gameObject.activeSelf != packet.active) cultist.gameObject.SetActive(packet.active);
                    toPosition = packet.position; toRotation = packet.rotation; toAnim = packet.arg; toHead = packet.pitch;
                    if (!havePose) { cultist.transform.SetPositionAndRotation(toPosition, toRotation); havePose = true; }
                    var parts = (packet.build ?? "").Split(',');
                    if (parts.Length >= 6 && !localHunt)
                    {
                        cultist.area = (Cultist.Area)int.Parse(parts[0]); cultist.state = (Cultist.State)int.Parse(parts[1]);
                        cultist.ritualDone = parts[2] == "1"; cultist.hidingAroundCorner = parts[3] == "1"; cultist.walkDownstairs = parts[4] == "1";
                        Sounds(cultist, int.Parse(parts[5]));
                    }
                    break;
                case "cmd":
                    if (plugin.LeadsStory) Command(cultist, packet.name);
                    break;
                case "hunt-partner":
                    if (!plugin.LeadsStory || gone) return;
                    huntPartner = true; nextChase = 0f;
                    cultist.faceTarget = false;
                    if (cultist.realizeAS) cultist.realizeAS.Play();
                    Plugin.Log("CULTIST_HUNTS_PARTNER");
                    break;
                case "gone":
                    if (plugin.LeadsStory) Gone(tower);
                    break;
                case "torch-off":
                    if (tower.cultistFlashlight && tower.cultistFlashlight.activeSelf)
                    {
                        Applying = true;
                        tower.cultistFlashlight.SetActive(false);
                        if (tower.cultistFlashlightOffAS) tower.cultistFlashlightOffAS.Play();
                        torchWasOn = false;
                        Applying = false;
                        Plugin.Log("CULTIST_TORCH_OFF_FROM_PARTNER");
                    }
                    break;
            }
        }

        // The copy's sounds start and stop with the lead's.
        void Sounds(Cultist c, int bits)
        {
            var sources = new[] { c.footsteps, c.crackingAS, c.dropAnimalAS, c.realizeAS };
            for (int i = 0; i < sources.Length; i++)
            {
                bool on = (bits & (1 << i)) != 0, was = (soundBits & (1 << i)) != 0;
                if (!sources[i] || on == was) continue;
                if (on) sources[i].Play(); else if (i == 0 || i == 1) sources[i].Stop();
            }
            soundBits = bits;
        }

        // The copy follows the lead's cultist (instead of the game's Update, which walks him on the navmesh).
        internal bool LetUpdate(Cultist c)
        {
            if (!Copy || c != (Tower ? Tower.cultist : null)) return true;
            if (!havePose) return false;
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            c.transform.position = Vector3.Lerp(c.transform.position, toPosition, k);
            c.transform.rotation = Quaternion.Slerp(c.transform.rotation, toRotation, k);
            var animator = AnimatorOf(c);
            if (animator && toAnim != int.MinValue && animator.GetInteger("State") != toAnim) animator.SetInteger("State", toAnim);
            if (c.head && toHead >= 0f) c.head.weight = toHead;
            return false;
        }

        // The partner's game asks its copy to move: the lead's cultist does it instead.
        static readonly HashSet<string> RouteCommands = new HashSet<string> { "StartWalkingEarly", "StartWalking", "StartWalking2", "HideAroundCorner", "StandAndWait", "WalkDownstairs" };
        internal bool LetCommand(Cultist c, string method)
        {
            if (Applying || !plugin.Ready || !Night5 || c != Tower.cultist) return true;
            if (Lead)
            {
                if (method == "StartWalkingEarly") startedEarly = true;
                return true;
            }
            if (!Copy) return true;
            if (RouteCommands.Contains(method)) { Send("cmd", method); Plugin.Log("CULTIST_CMD_SENT " + method); }
            return false;
        }

        void Command(Cultist c, string method)
        {
            if (gone || huntPartner || !c.gameObject.activeInHierarchy || !RouteCommands.Contains(method)) return;
            var tower = Tower;
            // Only what still makes sense where he is in his round.
            if (method == "StartWalkingEarly" && (startedEarly || tower.seq5CultistWalking)) return;
            if ((method == "StartWalking" || method == "HideAroundCorner") && c.ritualDone) return;
            Applying = true;
            try { AccessTools.Method(typeof(Cultist), method).Invoke(c, null); Plugin.Log("CULTIST_CMD " + method); }
            finally { Applying = false; }
            if (method == "StartWalkingEarly") startedEarly = true;
            if (method == "StartWalking") tower.seq5CultistWalking = true;
        }

        // This player's own hunt begins (the game's HuntTriggered*): the copy is theirs until it is over, and the lead's
        // cultist runs at their body.
        internal void LocalHuntStarts()
        {
            if (!plugin.Ready || !Night5 || plugin.LeadsStory || localHunt) return;
            localHunt = true;
            var c = Tower.cultist;
            if (c.navMeshAgent) { c.navMeshAgent.enabled = true; c.navMeshAgent.Warp(c.transform.position); }
            Send("hunt-partner");
            Plugin.Log("CULTIST_HUNTS_ME");
        }

        void ChasePartner(Cultist c)
        {
            if (!c.gameObject.activeInHierarchy || !plugin.PartnerPresent || Time.unscaledTime < nextChase) return;
            nextChase = Time.unscaledTime + 0.25f;
            if (!partnerMark) partnerMark = new GameObject("Coop cultist target").transform;
            partnerMark.position = plugin.PartnerPosition;
            LookAtOf(c) = partnerMark;
            if (Vector3.Distance(c.transform.position, partnerMark.position) > 1.4f) SetDestination.Invoke(c, new object[] { partnerMark, true });
            else { c.JumpScarePose(); c.faceTarget = true; }
        }

        // Someone died and respawned on their own: he is gone for the night and the skull lies on the balcony.
        internal void AfterRespawn()
        {
            if (!plugin.Ready || !Night5) return;
            localHunt = false;
            if (plugin.LeadsStory) Gone(Tower); else Send("gone");
        }

        void Gone(WatchTowerManager tower)
        {
            if (gone) return;
            gone = true; huntPartner = false;
            if (tower.cultist) { if (tower.cultist.footsteps) tower.cultist.footsteps.Stop(); tower.cultist.gameObject.SetActive(false); }
            if (tower.skullParent) tower.skullParent.SetActive(true);
            tower.Seq5CultistGone();
            if (tower.sickleInHand) tower.sickleInHand.SetActive(false);
            if (tower.sickleOnBelt) tower.sickleOnBelt.SetActive(true);
            if (plugin.Ready) SendPose(tower.cultist);
            Plugin.Log("CULTIST_GONE after a death");
        }
    }

    [HarmonyPatch(typeof(Cultist), "Update")]
    internal static class CultistCopyPatch
    {
        static bool Prefix(Cultist __instance) => Night5Sync.Instance == null || Night5Sync.Instance.LetUpdate(__instance);
    }

    [HarmonyPatch]
    internal static class CultistCommandPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "StartWalkingEarly", "StartWalking", "StartWalking2", "HideAroundCorner", "StandAndWait", "WalkDownstairs", "StopWalking", "StandUp", "JumpScarePose", "JumpscareMove", "FollowPlayer", "LookAtPlayer" })
            {
                var method = AccessTools.Method(typeof(Cultist), name);
                if (method != null) yield return method;
            }
        }

        static bool Prefix(Cultist __instance, System.Reflection.MethodBase __originalMethod) => Night5Sync.Instance == null || Night5Sync.Instance.LetCommand(__instance, __originalMethod.Name);
    }

    // The game's hunts of night 5 (the player made him notice them): the copy becomes this player's own for it.
    [HarmonyPatch]
    internal static class CultistHuntPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "HuntTriggered", "HuntTriggeredNear", "HuntTriggeredFar", "HuntTriggeredCrouched" })
            {
                var method = AccessTools.Method(typeof(WatchTowerManager), name);
                if (method != null) yield return method;
            }
        }

        static void Prefix() => Night5Sync.Instance?.LocalHuntStarts();
    }
}
