using System.Collections;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // The trail's end (the guard house, then the run to the RV), for two, after Scratch Creek's chase: the cultist comes
    // only once both players have reached the place where he shows up (whoever is there first waits at an invisible wall,
    // with the whole stretch before it as the zone, so nobody has to push against it); then both see him and run. He is
    // one for both (he runs in the story lead's world, the partner's is a copy), follows the closer player, and goes for
    // the kill on the closer one once near (the other hears it). A death is the team's: both start over at the trail's
    // end. The way out is the RV: the night ends when both are inside with the door shut.
    internal sealed class TrailEndSync
    {
        internal static TrailEndSync Instance;
        readonly Plugin plugin;
        public TrailEndSync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        static GameManager Gm => GameManager.instance;
        static TrailEndManager Manager => Gm ? Gm.trailEndManager : null;
        static readonly AccessTools.FieldRef<CultistTrailEnd, bool> FollowingPlayerOf = AccessTools.FieldRefAccess<CultistTrailEnd, bool>("followingPlayer");
        static readonly AccessTools.FieldRef<CultistTrailEnd, Transform> LookAtOf = AccessTools.FieldRefAccess<CultistTrailEnd, Transform>("lookAt");
        static readonly System.Reflection.MethodInfo SetDestination = AccessTools.Method(typeof(CultistTrailEnd), "SetDestination");
        static readonly AccessTools.FieldRef<TrailEndManager, bool> PlayerCaught = AccessTools.FieldRefAccess<TrailEndManager, bool>("playerCaught");
        internal static bool Applying;

        bool Here => Manager && Manager.cultist && SceneManager.GetActiveScene().name == "Trail End";
        bool Lead => plugin.Ready && plugin.LeadsStory;
        internal bool Copy => plugin.Ready && !plugin.LeadsStory && Here && !localHunt;

        bool started, localHunt, huntPartner, resetting, spawned, localInRv, partnerInRv;
        string target;
        float sceneAt, nextPose, nextChase, nextWallLine;
        Vector3 spawn, cultistSpawn; Quaternion cultistSpawnRotation; float spawnYaw, fov = -1f;
        float[] sprintAtStart;
        Bounds trigger; bool haveTrigger;
        GameObject wall;
        Transform partnerMark;
        Vector3 toPosition; Quaternion toRotation; int toAnim = int.MinValue; Vector3 toVelocity; float poseAt; bool havePose;

        public string Debug
        {
            get
            {
                if (!Here) return "-";
                var c = Manager.cultist;
                return (Lead ? "lead" : localHunt ? "hunted" : "copy") + "/" + (c.gameObject.activeSelf ? "on" : "off") + "/" + c.transform.position.x.ToString("F1", CultureInfo.InvariantCulture) + ";" + c.transform.position.z.ToString("F1", CultureInfo.InvariantCulture)
                    + (started ? "/started" : "/waiting") + (target != null ? "/target-" + target : "") + (huntPartner ? "/huntPartner" : "") + (wall && wall.activeSelf ? "/wall" : "") + (localInRv ? "/inRv" : "") + (partnerInRv ? "/partnerInRv" : "") + (resetting ? "/resetting" : "");
            }
        }

        public void SceneLoaded()
        {
            started = false; localHunt = false; huntPartner = false; resetting = false; spawned = false; localInRv = false; partnerInRv = false;
            target = null; haveTrigger = false; havePose = false; toAnim = int.MinValue; fov = -1f; sprintAtStart = null;
            sceneAt = Time.unscaledTime;
            if (partnerMark) Object.Destroy(partnerMark.gameObject);
            partnerMark = null; wall = null;
        }

        public void Tick()
        {
            if (!Here) return;
            var tm = Manager;
            if (!spawned && Gm.playerFP && Time.unscaledTime - sceneAt > 1.5f) FirstFrame(tm);
            if (!plugin.Ready || !spawned) return;
            // The RV only counts with both inside (the game's outro checks this player only).
            // Nor while he is getting one of them.
            tm.isPlayerInsideTheRV = localInRv && (partnerInRv || SharedChase.PartnerGhost) && target == null && !localHunt;
            // The game marks this player caught when the cultist only came near (its own kill check); here only the one he
            // goes for is caught (it would keep this player's RV from ending the night).
            if (!localHunt && target != "lead") PlayerCaught(tm) = false;
            Gate(tm);
            var c = tm.cultist;
            if (Lead)
            {
                if (started && !resetting) Hunt(tm, c);
                if (huntPartner) ChasePartner(c);
                if (Time.unscaledTime >= nextPose && started) { nextPose = Time.unscaledTime + 0.1f; SendPose(c); }
            }
            else if (Copy && c.navMeshAgent && c.navMeshAgent.enabled) c.navMeshAgent.enabled = false;
        }

        void FirstFrame(TrailEndManager tm)
        {
            spawned = true;
            foreach (var t in Object.FindObjectsOfType<OnTrigger>(true))
                if (t.triggerEvent.GetPersistentEventCount() > 0 && t.triggerEvent.GetPersistentMethodName(0) == "TriggerCultist" && t.TryGetComponent<Collider>(out var box)) { trigger = box.bounds; haveTrigger = true; }
            var c = tm.cultist;
            cultistSpawn = c.transform.position; cultistSpawnRotation = c.transform.rotation;
            var fpc = Gm.firstPersonController;
            if (fpc) { sprintAtStart = new float[fpc.GroundTypes.Count]; for (int i = 0; i < sprintAtStart.Length; i++) sprintAtStart[i] = fpc.GroundTypes[i].sprintSpeed; }
            if (Gm.fpCamera) fov = Gm.fpCamera.fieldOfView;
            var feet = Gm.playerFP.transform.position;
            var controller = Gm.playerFP.GetComponent<CharacterController>();
            if (controller) feet.y -= controller.height * 0.5f - controller.center.y;
            var toTrigger = haveTrigger ? trigger.center - feet : Vector3.forward; toTrigger.y = 0f;
            spawnYaw = Quaternion.LookRotation(toTrigger.sqrMagnitude > 0.01f ? toTrigger : Vector3.forward).eulerAngles.y;
            // Both players start at the very same spot: the partner who does not lead a step to the side.
            var side = Vector3.Cross(Vector3.up, toTrigger.normalized);
            spawn = plugin.Ready && plugin.FollowsStory ? SharedChase.Ground(feet + side * 1.3f) : feet;
            if (plugin.Ready && plugin.FollowsStory) SharedChase.PlacePlayer(spawn, spawnYaw);
            if (haveTrigger) wall = SharedChase.MakeWall("Coop wait wall (trail end)", new Vector3(trigger.min.x - 0.6f, trigger.center.y + 1.5f, trigger.center.z), new Vector3(0.6f, 6f, trigger.size.z + 14f));
            Plugin.Log("TRAILEND_READY trigger=" + (haveTrigger ? trigger.center.ToString("F1") : "-") + " spawn=" + spawn.ToString("F1"));
        }

        void Send(string code, bool active = false) => plugin.Send(new Packet { kind = "trailend", code = code, active = active, scene = SceneManager.GetActiveScene().name });

        // ---- waiting for each other ----
        // Right at the game's own trigger (a wider zone started him too early, before the place where he is seen).
        bool InZone(Vector3 at) => haveTrigger && at.x >= trigger.min.x - 1f && at.x <= trigger.max.x + 3f && Mathf.Abs(at.z - trigger.center.z) <= trigger.extents.z + 4f;

        void Gate(TrailEndManager tm)
        {
            bool closed = !started;
            if (wall && wall.activeSelf != closed) wall.SetActive(closed);
            var me = Gm.playerFP.transform.position;
            if (closed && wall && InZone(me) && Time.unscaledTime >= nextWallLine)
            {
                nextWallLine = Time.unscaledTime + 8f;
                string partner = string.IsNullOrEmpty(plugin.PartnerName) ? ConnectionPanel.L("напарника", "my partner") : plugin.PartnerName;
                Gm.subtitle.ShowSubAndClose(string.Format(ConnectionPanel.L("Подожду {0}, дальше пойдём вместе.", "I'll wait for {0}; we go on together."), partner), 4f);
            }
            if (started || !Lead || resetting) return;
            bool meIn = SharedChase.Ghost || InZone(me);
            bool partnerIn = SharedChase.PartnerGhost || (plugin.PartnerPresent && InZone(plugin.PartnerPosition));
            if (meIn && partnerIn) { Send("start"); StartChase(tm); }
        }

        void StartChase(TrailEndManager tm)
        {
            if (started) return;
            started = true;
            if (wall) wall.SetActive(false);
            Applying = true;
            try { tm.TriggerCultist(); }
            finally { Applying = false; }
            Plugin.Log("TRAILEND_CHASE_STARTS");
        }

        internal bool LetTrigger() => !plugin.Ready || Applying || !Here;

        // ---- whom he goes for ----
        void Hunt(TrailEndManager tm, CultistTrailEnd c)
        {
            if (target != null || !tm.cultistChasing) return;
            float me = SharedChase.Ghost || localInRv ? float.MaxValue : Vector3.Distance(c.transform.position, Gm.playerFP.transform.position);
            float partner = SharedChase.PartnerGhost || partnerInRv || !plugin.PartnerPresent ? float.MaxValue : Vector3.Distance(c.transform.position, plugin.PartnerPosition);
            if (Mathf.Min(me, partner) >= 5.5f) return;
            if (me <= partner)
            {
                target = "lead";
                Applying = true;
                try { tm.HuntTriggeredFar(); }
                finally { Applying = false; }
                Send("hunt-other");
            }
            else
            {
                target = "partner";
                tm.cultistChasing = false;
                huntPartner = true; nextChase = 0f;
                c.StopAllCoroutines();
                if (c.sickleOnBelt) c.sickleOnBelt.SetActive(false);
                if (c.sickleInHand) c.sickleInHand.SetActive(true);
                if (c.animator) c.animator.SetInteger("State", 10);
                c.UpdateHeadRig(c.head, 1f, 0.25f);
                Send("hunt-you");
                SharedChase.HearHunt(tm.hauntingAS, null);
            }
            Plugin.Log("TRAILEND_TARGET " + target + " me=" + me.ToString("F1") + " partner=" + partner.ToString("F1"));
        }

        // Before the kill he runs after whichever of them is closer (the game makes him follow this player only).
        internal void AfterCultistUpdate(CultistTrailEnd c)
        {
            if (!Lead || !Here || target != null || !Manager.cultistChasing || SharedChase.PartnerGhost || partnerInRv || !plugin.PartnerPresent) return;
            float me = SharedChase.Ghost || localInRv ? float.MaxValue : Vector3.Distance(c.transform.position, Gm.playerFP.transform.position);
            if (Vector3.Distance(c.transform.position, plugin.PartnerPosition) >= me) return;
            if (!partnerMark) partnerMark = new GameObject("Coop cultist target").transform;
            partnerMark.position = plugin.PartnerPosition;
            SetDestination.Invoke(c, new object[] { partnerMark, (bool?)true });
        }

        // The game's own "too close" (HuntTriggeredFar): the lead's world decides whom he kills.
        internal bool LetHunt()
        {
            if (!plugin.Ready || !Here || Applying) return true;
            Traverse.Create(Manager).Field("playerCaught").SetValue(false);
            return false;
        }

        void ChasePartner(CultistTrailEnd c)
        {
            if (!c.gameObject.activeInHierarchy || Time.unscaledTime < nextChase) return;
            nextChase = Time.unscaledTime + 0.25f;
            if (!partnerMark) partnerMark = new GameObject("Coop cultist target").transform;
            partnerMark.position = plugin.PartnerPosition;
            LookAtOf(c) = partnerMark;
            if (Vector3.Distance(c.transform.position, partnerMark.position) > 1.4f)
            {
                c.isRunning = true; FollowingPlayerOf(c) = true;
                SetDestination.Invoke(c, new object[] { partnerMark, (bool?)true });
                if (c.navMeshAgent) c.navMeshAgent.speed = 6f;
            }
            else { c.JumpScarePose(); c.faceTarget = true; }
        }

        void LocalHunt(TrailEndManager tm)
        {
            if (localHunt) return;
            localHunt = true;
            var c = tm.cultist;
            if (c.navMeshAgent) { c.navMeshAgent.enabled = true; c.navMeshAgent.Warp(c.transform.position); }
            Applying = true;
            try { tm.HuntTriggeredFar(); }
            finally { Applying = false; }
            Plugin.Log("TRAILEND_HUNTS_ME");
        }

        // ---- the copy ----
        void SendPose(CultistTrailEnd c)
        {
            plugin.Send(new Packet {
                kind = "trailend", code = "pose", scene = SceneManager.GetActiveScene().name, active = c.gameObject.activeSelf,
                position = c.transform.position, rotation = c.transform.rotation, arg = c.animator ? c.animator.GetInteger("State") : 0,
                build = (c.sickleInHand && c.sickleInHand.activeSelf ? "1" : "0") + "," + (c.runningOnGrassAS && c.runningOnGrassAS.isPlaying ? "1" : "0")
            });
        }

        internal bool LetUpdate(CultistTrailEnd c)
        {
            if (!Copy || c != (Manager ? Manager.cultist : null)) return true;
            if (!havePose) return false;
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            c.transform.position = Vector3.Lerp(c.transform.position, toPosition + toVelocity * Mathf.Min(Time.unscaledTime - poseAt, 0.25f), k);
            c.transform.rotation = Quaternion.Slerp(c.transform.rotation, toRotation, k);
            if (c.animator && toAnim != int.MinValue && c.animator.GetInteger("State") != toAnim) c.animator.SetInteger("State", toAnim);
            return false;
        }

        internal bool LetFollow(CultistTrailEnd c) => !Copy || c != Manager.cultist;

        // ---- the RV ----
        internal void InRv(bool inside)
        {
            localInRv = inside;
            if (plugin.Ready && Here) Send("rv", inside);
        }

        internal void Ending()
        {
            var tm = Manager;
            if (!Here || !tm) return;
            target = "ending"; huntPartner = false;
            tm.StopCultist();
            tm.cultistChasing = false; tm.cultistHaunting = false;
            foreach (var a in new[] { tm.chaseAS, tm.hauntingAS, tm.heartBeatAS }) if (a) a.Stop();
            var c = tm.cultist;
            if (c) { if (c.runningOnGrassAS) c.runningOnGrassAS.Stop(); c.StopAllCoroutines(); if (c.navMeshAgent && c.navMeshAgent.enabled) c.navMeshAgent.isStopped = true; c.gameObject.SetActive(false); }
            SharedChase.StopHearingHunt();
            Plugin.Log("TRAILEND_ENDING_STOPS_CHASE");
        }

        // ---- a death is the team's ----
        internal void KillStarts() { if (plugin.Ready && Here) Send("kill"); }

        internal bool TakeDeath()
        {
            if (!plugin.Ready || !Here || resetting) return false;
            Send("reset");
            Gm.StartCoroutine(StartOver(Manager));
            return true;
        }

        IEnumerator StartOver(TrailEndManager tm)
        {
            if (resetting) yield break;
            resetting = true;
            Plugin.Log("TRAILEND_START_OVER");
            yield return SharedChase.Fade(true);
            SharedChase.EndWitness();
            SharedChase.StopHearingHunt();
            SharedChase.Revive();
            foreach (var f in new Behaviour[] { tm.anomaly, tm.blurFocus, tm.distortionDream, tm.realVHS, tm.filmGrain, tm.bloodPlus }) if (f) f.enabled = false;
            if (tm.jumpscareLight) tm.jumpscareLight.SetActive(false);
            foreach (var a in new[] { tm.chaseAS, tm.hauntingAS, tm.heartBeatAS, tm.glitchBeforeQuitAS, tm.guyScreamAS }) if (a) a.Stop();
            Gm.audioMixer.SetFloat("Ambience", 0f);
            tm.cultistChasing = false; tm.cultistHaunting = false; tm.waitForFarScare = false;
            Traverse.Create(tm).Field("playerCaught").SetValue(false);
            Traverse.Create(tm).Field("seq8ShiftSubOn").SetValue(false);
            var c = tm.cultist;
            c.StopAllCoroutines();
            Traverse.Create(c).Field("jumpscareMovement").SetValue(false);
            Traverse.Create(c).Field("moving").SetValue(false);
            if (c.navMeshAgent && c.navMeshAgent.enabled) c.navMeshAgent.Warp(cultistSpawn);
            c.transform.SetPositionAndRotation(cultistSpawn, cultistSpawnRotation);
            if (c.sickleInHand) c.sickleInHand.SetActive(false);
            if (c.sickleOnBelt) c.sickleOnBelt.SetActive(true);
            if (c.runningOnGrassAS) c.runningOnGrassAS.Stop();
            if (c.animator) c.animator.SetInteger("State", 5);
            if (c.head) c.head.weight = 0f;
            c.gameObject.SetActive(false);
            var fpc = Gm.firstPersonController;
            if (fpc && sprintAtStart != null) for (int i = 0; i < sprintAtStart.Length && i < fpc.GroundTypes.Count; i++) fpc.GroundTypes[i].sprintSpeed = sprintAtStart[i];
            if (Gm.fpCamera && fov > 0f) Gm.fpCamera.fieldOfView = fov;
            SharedChase.PlacePlayer(spawn, spawnYaw);
            started = false; target = null; huntPartner = false; localHunt = false; havePose = false;
            yield return new WaitForSeconds(0.6f);
            yield return SharedChase.Fade(false);
            resetting = false;
            Plugin.Log("TRAILEND_STARTED_OVER");
        }

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name || !Here) return;
            var tm = Manager;
            var c = tm.cultist;
            switch (packet.code)
            {
                case "start":
                    StartChase(tm);
                    break;
                case "rv":
                    partnerInRv = packet.active;
                    break;
                case "hunt-you":
                    LocalHunt(tm);
                    break;
                case "hunt-other":
                    SharedChase.HearHunt(tm.hauntingAS, null);
                    break;
                case "kill":
                    SharedChase.StopHearingHunt();
                    if (c.knifeStabAS && c.gameObject.activeInHierarchy) c.knifeStabAS.Play();
                    SharedChase.Witness(new Behaviour[] { tm.anomaly, tm.blurFocus, tm.distortionDream, tm.realVHS, tm.filmGrain }, c.transform, tm.heartBeatAS);
                    Plugin.Log("TRAILEND_PARTNER_KILLED");
                    break;
                case "reset":
                    Gm.StartCoroutine(StartOver(tm));
                    break;
                case "pose":
                    if (plugin.LeadsStory || localHunt || resetting) return;
                    if (c.gameObject.activeSelf != packet.active) c.gameObject.SetActive(packet.active);
                    float gap = Time.unscaledTime - poseAt;
                    toVelocity = havePose && gap > 0.02f && gap < 0.5f ? Vector3.ClampMagnitude((packet.position - toPosition) / gap, 9f) : Vector3.zero;
                    poseAt = Time.unscaledTime;
                    toPosition = packet.position; toRotation = packet.rotation; toAnim = packet.arg;
                    if (!havePose) { c.transform.SetPositionAndRotation(toPosition, toRotation); havePose = true; }
                    var parts = (packet.build ?? "").Split(',');
                    if (parts.Length >= 2)
                    {
                        bool sickle = parts[0] == "1";
                        if (c.sickleInHand && c.sickleInHand.activeSelf != sickle) { c.sickleInHand.SetActive(sickle); if (c.sickleOnBelt) c.sickleOnBelt.SetActive(!sickle); }
                        bool run = parts[1] == "1";
                        if (c.runningOnGrassAS && c.runningOnGrassAS.isPlaying != run) { if (run) c.runningOnGrassAS.Play(); else c.runningOnGrassAS.Stop(); }
                    }
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(TrailEndManager), nameof(TrailEndManager.TriggerCultist))]
    internal static class TrailEndTriggerPatch
    {
        static bool Prefix() => TrailEndSync.Instance == null || TrailEndSync.Instance.LetTrigger();
    }

    [HarmonyPatch(typeof(TrailEndManager), nameof(TrailEndManager.HuntTriggeredFar))]
    internal static class TrailEndHuntPatch
    {
        static bool Prefix() => TrailEndSync.Instance == null || TrailEndSync.Instance.LetHunt();
    }

    [HarmonyPatch(typeof(TrailEndManager), "PerformJumpscareAtRun")]
    internal static class TrailEndKillPatch
    {
        static void Prefix() => TrailEndSync.Instance?.KillStarts();
    }

    [HarmonyPatch(typeof(TrailEndManager), nameof(TrailEndManager.PlayerRVEnter))]
    internal static class TrailEndRvEnterPatch
    {
        static void Postfix() => TrailEndSync.Instance?.InRv(true);
    }

    [HarmonyPatch(typeof(TrailEndManager), nameof(TrailEndManager.PlayerRVExit))]
    internal static class TrailEndRvExitPatch
    {
        static void Postfix() => TrailEndSync.Instance?.InRv(false);
    }

    [HarmonyPatch(typeof(CultistTrailEnd), "Update")]
    internal static class TrailEndCultistUpdatePatch
    {
        static bool Prefix(CultistTrailEnd __instance) => TrailEndSync.Instance == null || TrailEndSync.Instance.LetUpdate(__instance);
        static void Postfix(CultistTrailEnd __instance) => TrailEndSync.Instance?.AfterCultistUpdate(__instance);
    }

    [HarmonyPatch(typeof(CultistTrailEnd), nameof(CultistTrailEnd.FollowPlayer))]
    internal static class TrailEndFollowPatch
    {
        static bool Prefix(CultistTrailEnd __instance) => TrailEndSync.Instance == null || TrailEndSync.Instance.LetFollow(__instance);
    }
}

namespace IronbarkCoop
{
    // The ending (FadeOutWithOutro, also when it comes from the partner who reached the RV): the chase stops here too.
    // Before, the music and his running went on under the black screen of the ending (playtest).
    [HarmonyPatch(typeof(UIManager), nameof(UIManager.FadeOutWithOutro))]
    internal static class TrailEndOutroPatch
    {
        static void Prefix() => TrailEndSync.Instance?.Ending();
    }
}
