using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Night 8 ("two weeks later"), one cultist for both, as night 5 (Night5Sync). Before, each world had its own night:
    // Marcus took the photo with the flash and his cultist came for him, Tessa's world stayed quiet (playtest).
    // - The flash is the team's: once either player has taken the photo, the camp scatters in both worlds, both may hide
    //   under the bed, and the trigger on the balcony that brings the cultist works for whoever walks into it first.
    // - The cultist runs in the story lead's world only (run to the door, the knocks, breaking in, searching the room,
    //   waiting downstairs, the chase): the partner's world shows a copy that follows his pose, animation and sounds,
    //   and gets the door slams and the music of his coming in. What the partner's game asks of him (the chase, when the
    //   partner is seen running down) is sent to the lead's cultist instead.
    // - Scares stay personal: whoever he notices is hunted in their own world (the copy is let go for that), and in the
    //   lead's world he runs at the partner. After a death (a lone respawn) he is gone for the night; the way out to the
    //   trail stays, and the end of the night takes both along.
    internal sealed class Night8Sync
    {
        internal static Night8Sync Instance;
        readonly Plugin plugin;
        public Night8Sync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        static GameManager Gm => GameManager.instance;
        static WatchTowerManager Tower => Gm ? Gm.watchTowerManager : null;
        static readonly AccessTools.FieldRef<CultistSeq8, Animator> AnimatorOf = AccessTools.FieldRefAccess<CultistSeq8, Animator>("animator");
        static readonly AccessTools.FieldRef<CultistSeq8, Transform> LookAtOf = AccessTools.FieldRefAccess<CultistSeq8, Transform>("lookAt");
        static readonly AccessTools.FieldRef<CultistSeq8, bool> FollowingPlayerOf = AccessTools.FieldRefAccess<CultistSeq8, bool>("followingPlayer");
        static readonly System.Reflection.MethodInfo SetDestination = AccessTools.Method(typeof(CultistSeq8), "SetDestination");

        bool Night8 { get { var tower = Tower; return tower && tower.seq == WatchTowerManager.Seq.Seq8 && tower.cultistSeq8; } }
        internal bool Lead => plugin.Ready && plugin.LeadsStory;
        internal bool Copy => plugin.Ready && !plugin.LeadsStory && Night8 && !localHunt;
        internal static bool Applying;

        bool on, localHunt, huntPartner, gone, fled, havePose;
        float nextPose, nextChase;
        Transform partnerMark;
        Vector3 toPosition; Quaternion toRotation; int toAnim = int.MinValue; float toHead = -1f;
        // He runs at 3.5 m/s and poses come 10 times a second: the copy runs on at his last speed between them.
        Vector3 toVelocity; float poseAt;
        int soundBits;

        public string Debug
        {
            get
            {
                if (!Night8) return "-";
                var tower = Tower; var c = tower.cultistSeq8;
                return (Lead ? "lead" : localHunt ? "hunted" : "copy") + "/" + (c.gameObject.activeSelf ? "on" : "off") + "/" + c.area + "/x" + (c.isGoingToX ? "g" : "") + (c.isAtX ? "a" : "") + (c.isChase ? "/chase" : "")
                    + "/" + c.transform.position.x.ToString("F1", CultureInfo.InvariantCulture) + ";" + c.transform.position.z.ToString("F1", CultureInfo.InvariantCulture)
                    + (huntPartner ? "/huntPartner" : "") + (gone ? "/gone" : "") + (tower.seq8FlashWentOff ? "/flash" : "") + (fled ? "/fled" : "") + "/me" + tower.area + "/door" + (tower.isDoorOpen ? 1 : 0);
            }
        }

        public void SceneLoaded() { Reset(); }

        internal void Reset()
        {
            on = false; localHunt = false; huntPartner = false; gone = false; fled = false; havePose = false; toAnim = int.MinValue; soundBits = 0;
            target = null; deaths = 0; askAt = 0f;
            if (partnerMark) Object.Destroy(partnerMark.gameObject);
            partnerMark = null;
        }

        public void Tick()
        {
            if (!Night8 || !plugin.Ready) return;
            var tower = Tower;
            var cultist = tower.cultistSeq8;
            // The partner took the photo: the camp scatters here too.
            if (tower.seq8FlashWentOff && !fled) CampFlees(tower);
            if (Lead)
            {
                if (huntPartner) ChasePartner(tower, cultist);
                if (Time.unscaledTime >= nextPose && (cultist.gameObject.activeSelf || on)) { nextPose = Time.unscaledTime + 0.1f; SendPose(cultist); }
            }
            else if (Copy && cultist.navMeshAgent && cultist.navMeshAgent.enabled) cultist.navMeshAgent.enabled = false;
        }

        void Send(string code, string name = null) => plugin.Send(new Packet { kind = "cultist8", code = code, name = name, scene = SceneManager.GetActiveScene().name });

        void CampFlees(WatchTowerManager tower)
        {
            fled = true;
            var photo = tower.seq8PictureMode;
            if (tower.cultistsInCamp != null && photo)
                foreach (var c in tower.cultistsInCamp)
                {
                    if (!c || !c.navMesh || !c.navMesh.isActiveAndEnabled || c.navMesh.hasPath) continue;
                    var to = c.isLeader ? photo.cultLeaderToPosAcrossLake : photo.cultToPosAcrossLake;
                    if (!to) continue;
                    c.navMesh.speed = Random.Range(3f, 5f);
                    c.navMesh.SetDestination(to.position);
                    if (c.animator) c.animator.SetInteger("State", 3);
                    c.facePlayer = false;
                }
            if (tower.seq8TriggerMount) tower.seq8TriggerMount.SetActive(false);
            tower.canMount = false;
            Plugin.Log("CULTIST8_CAMP_FLED");
        }

        // The balcony trigger after the photo (the game's Seq8CultistEnableAndLookAt): the first player to walk into it
        // brings him, and watches him come; the partner's world gets him too, without having the view turned.
        internal bool LetEnable()
        {
            if (!plugin.Ready || !Night8 || Applying) return true;
            if (on || gone) { Plugin.Log("CULTIST8_ALREADY_COMING"); return false; }
            on = true;
            Send("on");
            Plugin.Log("CULTIST8_ON_HERE");
            return true;
        }

        void ComeWithoutLooking(WatchTowerManager tower)
        {
            if (on || gone) return;
            on = true;
            if (tower.colliderDownstairs) tower.colliderDownstairs.SetActive(false);
            if (tower.cultNoticed) tower.cultNoticed.Stop();
            if (tower.scaryViolinAS) tower.scaryViolinAS.Stop();
            if (tower.seq8TriggerLookAtCultist) tower.seq8TriggerLookAtCultist.SetActive(false);
            tower.cultistSeq8.gameObject.SetActive(true);
            if (tower.pissedOffAS) tower.pissedOffAS.Play();
            tower.seq8LookAtCultistDone = true;
            Plugin.Log("CULTIST8_ON_FROM_PARTNER");
        }

        void SendPose(CultistSeq8 c)
        {
            var animator = AnimatorOf(c);
            var sources = Sources(c);
            int bits = 0;
            for (int i = 0; i < sources.Length; i++) if (sources[i] && sources[i].isPlaying) bits |= 1 << i;
            plugin.Send(new Packet {
                kind = "cultist8", code = "pose", scene = SceneManager.GetActiveScene().name, active = c.gameObject.activeSelf,
                position = c.transform.position, rotation = c.transform.rotation,
                arg = animator ? animator.GetInteger("State") : 0, pitch = c.head ? c.head.weight : 0f,
                build = (int)c.area + "," + (c.isGoingToX ? 1 : 0) + "," + (c.isAtX ? 1 : 0) + "," + (c.isChase ? 1 : 0) + "," + (c.flashLight && c.flashLight.activeSelf ? 1 : 0)
                    + "," + (c.sickle && c.sickle.activeSelf ? 1 : 0) + "," + bits + "," + (c.arm ? c.arm.weight : 0f).ToString("F2", CultureInfo.InvariantCulture)
            });
        }

        static AudioSource[] Sources(CultistSeq8 c) => new[] { c.footstepsStairs, c.footstepsInside, c.whistlingAS, c.realizeAS, c.runningOnStairsAS, c.runningOnGrassAS, c.grassFootsteps, c.woodCreakAS };

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name || !Night8) return;
            var tower = Tower;
            var cultist = tower.cultistSeq8;
            Applying = true;
            var sync = StorySync.Instance;
            if (sync != null) sync.ApplyingRemote = true;
            try
            {
                switch (packet.code)
                {
                    case "pose":
                        if (plugin.LeadsStory || gone) return;
                        if (packet.active && !on) ComeWithoutLooking(tower);
                        if (cultist.gameObject.activeSelf != packet.active) cultist.gameObject.SetActive(packet.active);
                        float gap = Time.unscaledTime - poseAt;
                        toVelocity = havePose && gap > 0.02f && gap < 0.5f ? Vector3.ClampMagnitude((packet.position - toPosition) / gap, 8f) : Vector3.zero;
                        poseAt = Time.unscaledTime;
                        toPosition = packet.position; toRotation = packet.rotation; toAnim = packet.arg; toHead = packet.pitch;
                        if (!havePose) { cultist.transform.SetPositionAndRotation(toPosition, toRotation); havePose = true; }
                        var parts = (packet.build ?? "").Split(',');
                        if (parts.Length >= 8 && !localHunt)
                        {
                            cultist.area = (CultistSeq8.Area)int.Parse(parts[0]);
                            cultist.isGoingToX = parts[1] == "1"; cultist.isAtX = parts[2] == "1"; cultist.isChase = parts[3] == "1";
                            if (cultist.flashLight && cultist.flashLight.activeSelf != (parts[4] == "1")) cultist.flashLight.SetActive(parts[4] == "1");
                            if (cultist.sickle && cultist.sickle.activeSelf != (parts[5] == "1")) cultist.sickle.SetActive(parts[5] == "1");
                            Sounds(cultist, int.Parse(parts[6]));
                            if (cultist.arm) cultist.arm.weight = float.Parse(parts[7], CultureInfo.InvariantCulture);
                        }
                        break;
                    case "on":
                        ComeWithoutLooking(tower);
                        break;
                    case "slam":
                        tower.DoorSlam();
                        Plugin.Log("CULTIST8_SLAM_FROM_LEAD");
                        break;
                    case "slam-open":
                        tower.DoorSlamOpen();
                        Plugin.Log("CULTIST8_DOOR_BROKEN_FROM_LEAD");
                        break;
                    case "inside":
                        ComeInsideMusic(tower, cultist);
                        break;
                    case "at-x":
                        Gm.StartCoroutine(FadeMixerGroup.StartFade(Gm.audioMixer, "Ambience", 1f, 0f));
                        if (tower.horrorHeartBeatAS) Gm.StartCoroutine(FadeAudioSource.FadeToZeroAndReset(tower.horrorHeartBeatAS, 1f));
                        break;
                    case "cmd":
                        if (plugin.LeadsStory) Command(tower, cultist, packet.name);
                        break;
                    case "hunt-partner":
                        if (!plugin.LeadsStory || gone) return;
                        huntPartner = true; nextChase = 0f;
                        cultist.StopAllCoroutines();
                        cultist.faceTarget = false;
                        if (cultist.realizeAS) cultist.realizeAS.Play();
                        if (cultist.sickle) cultist.sickle.SetActive(true);
                        Plugin.Log("CULTIST8_HUNTS_PARTNER");
                        break;
                    case "seen":
                        if (plugin.LeadsStory) Decide(tower, false, true);
                        break;
                    case "hunt-you":
                        // The cultist chose this player: the game's own hunt, here.
                        if (plugin.LeadsStory) return;
                        gone = false;
                        tower.Seq8JumpscareInitialize();
                        Plugin.Log("CULTIST8_CHOSE_ME");
                        break;
                    case "hunt-other":
                        // He went for the partner: whatever made him notice this player is forgotten (under the bed the
                        // game would otherwise keep this player from getting up).
                        if (plugin.LeadsStory) return;
                        tower.seq8SeenUnderBed = false;
                        askAt = 0f;
                        Plugin.Log("CULTIST8_CHOSE_PARTNER");
                        break;
                    case "kill":
                        // The partner's death, seen and heard from here (Scratch Creek: the witness hears the stab).
                        if (cultist.knifeStabAS && cultist.gameObject.activeInHierarchy) cultist.knifeStabAS.Play();
                        Plugin.Log("CULTIST8_PARTNER_KILLED");
                        break;
                    case "died":
                        if (plugin.LeadsStory) AfterDeath(tower, false);
                        break;
                    case "gone":
                        gone = true;
                        if (plugin.LeadsStory) Gone(tower);
                        break;
                }
            }
            finally { Applying = false; if (sync != null) sync.ApplyingRemote = false; }
        }

        void Sounds(CultistSeq8 c, int bits)
        {
            var sources = Sources(c);
            for (int i = 0; i < sources.Length; i++)
            {
                bool now = (bits & (1 << i)) != 0, was = (soundBits & (1 << i)) != 0;
                if (!sources[i] || now == was) continue;
                if (now) sources[i].Play(); else sources[i].Stop();
            }
            soundBits = bits;
        }

        // What CultistSeq8.ComeInside starts besides his walk: the music of him in the tower.
        static void ComeInsideMusic(WatchTowerManager tower, CultistSeq8 c)
        {
            Gm.audioMixer.SetFloat("Ambience", -80f);
            if (tower.pissedOffAS) tower.pissedOffAS.Stop();
            if (Gm.ambienceSeq8AudioSource2) Gm.ambienceSeq8AudioSource2.Play();
            if (tower.tensionAS) tower.tensionAS.Play();
            if (tower.tensionBeforeTheFallAS) tower.tensionBeforeTheFallAS.Play();
            if (tower.horrorHeartBeatAS)
            {
                tower.horrorHeartBeatAS.volume = 0f;
                tower.horrorHeartBeatAS.Play();
                Gm.StartCoroutine(FadeAudioSource.StartFade(tower.horrorHeartBeatAS, 5f, 0.5f));
            }
            Plugin.Log("CULTIST8_INSIDE_MUSIC");
        }

        // The copy follows the lead's cultist instead of walking the navmesh itself.
        internal bool LetUpdate(CultistSeq8 c)
        {
            if (!Copy || c != (Tower ? Tower.cultistSeq8 : null)) return true;
            if (!havePose) return false;
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            var ahead = toPosition + toVelocity * Mathf.Min(Time.unscaledTime - poseAt, 0.25f);
            c.transform.position = Vector3.Lerp(c.transform.position, ahead, k);
            c.transform.rotation = Quaternion.Slerp(c.transform.rotation, toRotation, k);
            var animator = AnimatorOf(c);
            if (animator && toAnim != int.MinValue && animator.GetInteger("State") != toAnim) animator.SetInteger("State", toAnim);
            if (c.head && toHead >= 0f) c.head.weight = toHead;
            return false;
        }

        // The lead's cultist reports what the partner's world needs to hear (door, coming in, waiting downstairs); the
        // partner's game asking its copy to run (the chase) asks the lead's cultist instead.
        static readonly HashSet<string> RouteCommands = new HashSet<string> { "GoToJ1Early", "GoToJ1" };
        internal bool LetCultistCall(CultistSeq8 c, string method)
        {
            if (Applying || !plugin.Ready || !Night8 || c != Tower.cultistSeq8) return true;
            if (Lead)
            {
                if (method == "ComeInside") Send("inside");
                else if (method == "ReachedX") Send("at-x");
                return true;
            }
            if (!Copy || !RouteCommands.Contains(method)) return true;
            Send("cmd", method);
            Plugin.Log("CULTIST8_CMD_SENT " + method);
            return false;
        }

        internal void DoorSlammed(bool open)
        {
            if (Applying || !Lead || !Night8) return;
            Send(open ? "slam-open" : "slam");
        }

        void Command(WatchTowerManager tower, CultistSeq8 c, string method)
        {
            if (gone || huntPartner || !c.gameObject.activeInHierarchy || c.isChase) return;
            // The partner was seen running down while he searched or waited downstairs: the chase (as the game's
            // Seq8TriggerChase does it to him).
            if (method == "GoToJ1Early" && !(c.isGoingToX || c.isAtX)) return;
            Applying = true;
            try
            {
                if (c.realizeAS) c.realizeAS.Play();
                c.isChase = true;
                if (method == "GoToJ1Early") c.GoToJ1Early(); else c.GoToJ1();
                c.UpdateHeadRig(c.head, 0f, 0.5f);
                if (c.runningOnStairsAS) c.runningOnStairsAS.Play();
                if (tower.seq8TriggerCulsistRunningSounds) tower.seq8TriggerCulsistRunningSounds.SetActive(true);
                c.isGoingToX = false; c.isAtX = false;
                Plugin.Log("CULTIST8_CMD " + method);
            }
            finally { Applying = false; }
        }

        // This player's own hunt (the game's Seq8HuntTriggered*): the copy is theirs until it is over, and the lead's
        // cultist runs at them.
        internal void LocalHuntStarts()
        {
            if (!plugin.Ready || !Night8 || plugin.LeadsStory || localHunt) return;
            localHunt = true;
            var c = Tower.cultistSeq8;
            if (c.navMeshAgent) { c.navMeshAgent.enabled = true; c.navMeshAgent.Warp(c.transform.position); }
            Send("hunt-partner");
            Plugin.Log("CULTIST8_HUNTS_ME");
        }

        void ChasePartner(WatchTowerManager tower, CultistSeq8 c)
        {
            if (!c.gameObject.activeInHierarchy || Time.unscaledTime < nextChase) return;
            nextChase = Time.unscaledTime + 0.25f;
            if (!partnerMark) partnerMark = new GameObject("Coop cultist target").transform;
            // Hidden under the bed, the partner has no body to run at: their spot under it.
            partnerMark.position = plugin.PartnerPresent ? plugin.PartnerPosition : UnderBedSpot(tower);
            LookAtOf(c) = partnerMark;
            if (Vector3.Distance(c.transform.position, partnerMark.position) > 1.4f)
            {
                c.isRunning = true; FollowingPlayerOf(c) = true;
                SetDestination.Invoke(c, new object[] { partnerMark, (bool?)true });
            }
            else { c.JumpScarePose(); c.faceTarget = true; }
        }

        static Vector3 UnderBedSpot(WatchTowerManager tower)
        {
            var spot = tower.underBedJumpscarePostion ? tower.underBedJumpscarePostion.position : tower.bed.transform.position;
            if (!tower.bed || !tower.bedCam) return spot;
            var along = tower.bed.transform.position - tower.bedCam.transform.position; along.y = 0f;
            return along.sqrMagnitude > 0.01f ? spot + along.normalized * BedSync.UnderBedAlong : spot;
        }

        // ---- whom he goes for (Scratch Creek's hunt: the authority picks the closest player when he is alerted, and
        // keeps that target until the kill; the other one sees and hears it) ----
        // Either world's game noticing its own player (Seq8JumpscareInitialize) only alerts him: the story lead's world
        // then chooses between both players, the seen one and whoever is not hiding (under the bed, in the porta-potty),
        // and the closer one is hunted in their own world.
        string target;
        int deaths;
        float askAt;

        internal bool LetJumpscare()
        {
            if (!plugin.Ready || !Night8 || Applying) return true;
            var tower = Tower;
            if (Lead) { Decide(tower, true, false); return false; }
            if (localHunt) return true;
            if (Time.unscaledTime >= askAt) { askAt = Time.unscaledTime + 1f; Send("seen"); Plugin.Log("CULTIST8_SAW_ME"); }
            return false;
        }

        static bool Hiding(WatchTowerManager tower) => tower.state == WatchTowerManager.State.UnderBed || (tower.portaPotty && tower.portaPotty.playerHiddenInside);

        void Decide(WatchTowerManager tower, bool leadSeen, bool partnerSeen)
        {
            var c = tower.cultistSeq8;
            if (gone || target != null || !c.gameObject.activeInHierarchy) return;
            float me = leadSeen || !Hiding(tower) ? Vector3.Distance(c.transform.position, Gm.playerFP.transform.position) : float.MaxValue;
            float partner = plugin.Ready && (partnerSeen || !plugin.PartnerHidden) ? Vector3.Distance(c.transform.position, plugin.PartnerPresent ? plugin.PartnerPosition : UnderBedSpot(tower)) : float.MaxValue;
            if (me == float.MaxValue && partner == float.MaxValue) return;
            if (me <= partner) HuntLead(tower);
            else HuntPartnerWorld();
            Plugin.Log("CULTIST8_TARGET " + target + " me=" + (me == float.MaxValue ? "hidden" : me.ToString("F1")) + " partner=" + (partner == float.MaxValue ? "hidden" : partner.ToString("F1")) + " seen=" + (leadSeen ? "lead" : "partner"));
        }

        void HuntLead(WatchTowerManager tower)
        {
            target = "lead";
            Applying = true;
            try { tower.Seq8JumpscareInitialize(); }
            finally { Applying = false; }
            if (plugin.Ready) Send("hunt-other");
        }

        void HuntPartnerWorld()
        {
            target = "partner";
            Send("hunt-you");
        }

        // The victim's death starts (the game's jumpscare in their own world): the partner hears it.
        internal void KillStarts() { if (plugin.Ready && Night8) Send("kill"); }

        // Someone died and respawned on their own. As in Scratch Creek he does not stop at one: if the other player is
        // not hiding he goes for them next; once both have died (or the other hides) he is gone for the night.
        internal void AfterRespawn()
        {
            if (!plugin.Ready || !Night8) return;
            localHunt = false; askAt = 0f;
            if (plugin.LeadsStory) AfterDeath(Tower, true);
            else Send("died");
        }

        void AfterDeath(WatchTowerManager tower, bool leadDied)
        {
            deaths++;
            huntPartner = false;
            target = null;
            var c = tower.cultistSeq8;
            bool otherInSight = leadDied ? !plugin.PartnerHidden : !Hiding(tower);
            if (deaths >= 2 || !otherInSight || gone) { Gone(tower); Send("gone"); return; }
            // The game's respawn put him away in the world of whoever died here: back where he stood.
            if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
            if (c.navMeshAgent) { c.navMeshAgent.enabled = true; c.navMeshAgent.Warp(c.transform.position); }
            if (leadDied) HuntPartnerWorld(); else HuntLead(tower);
            Plugin.Log("CULTIST8_NEXT " + target);
        }

        void Gone(WatchTowerManager tower)
        {
            if (gone) return;
            gone = true; huntPartner = false;
            var c = tower.cultistSeq8;
            foreach (var source in Sources(c)) if (source) source.Stop();
            c.gameObject.SetActive(false);
            if (tower.horrorHeartBeatAS) tower.horrorHeartBeatAS.Stop();
            if (tower.hauntingAS) tower.hauntingAS.Stop();
            Gm.audioMixer.SetFloat("Ambience", 0f);
            if (plugin.Ready) SendPose(c);
            Plugin.Log("CULTIST8_GONE after a death");
        }
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq8JumpscareInitialize))]
    internal static class Cultist8AlertPatch
    {
        static bool Prefix() => Night8Sync.Instance == null || Night8Sync.Instance.LetJumpscare();
    }

    [HarmonyPatch]
    internal static class Cultist8KillPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "Seq8PerformJumpscareAtRun", "Seq8PerformJumpscareUnderBed" })
            {
                var method = AccessTools.Method(typeof(WatchTowerManager), name);
                if (method != null) yield return method;
            }
        }

        static void Prefix() => Night8Sync.Instance?.KillStarts();
    }

    [HarmonyPatch(typeof(CultistSeq8), "Update")]
    internal static class Cultist8CopyPatch
    {
        static bool Prefix(CultistSeq8 __instance) => Night8Sync.Instance == null || Night8Sync.Instance.LetUpdate(__instance);
    }

    [HarmonyPatch]
    internal static class Cultist8CallPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "GoToJ1Early", "GoToJ1" })
            {
                var method = AccessTools.Method(typeof(CultistSeq8), name);
                if (method != null) yield return method;
            }
        }

        static bool Prefix(CultistSeq8 __instance, System.Reflection.MethodBase __originalMethod) => Night8Sync.Instance == null || Night8Sync.Instance.LetCultistCall(__instance, __originalMethod.Name);
    }

    // His own steps that only the lead's cultist takes (the copy's Update, which starts them, does not run): told to the
    // partner's world for its music.
    [HarmonyPatch]
    internal static class Cultist8StepPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "ComeInside", "ReachedX" })
            {
                var method = AccessTools.Method(typeof(CultistSeq8), name);
                if (method != null) yield return method;
            }
        }

        static void Prefix(CultistSeq8 __instance, System.Reflection.MethodBase __originalMethod) => Night8Sync.Instance?.LetCultistCall(__instance, __originalMethod.Name);
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq8CultistEnableAndLookAt))]
    internal static class Cultist8EnablePatch
    {
        static bool Prefix() => Night8Sync.Instance == null || Night8Sync.Instance.LetEnable();
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.DoorSlam))]
    internal static class Cultist8SlamPatch
    {
        static void Prefix() => Night8Sync.Instance?.DoorSlammed(false);
    }

    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.DoorSlamOpen))]
    internal static class Cultist8SlamOpenPatch
    {
        static void Prefix() => Night8Sync.Instance?.DoorSlammed(true);
    }

    [HarmonyPatch]
    internal static class Cultist8HuntPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "Seq8HuntTriggeredFar", "Seq8HuntTriggeredClose" })
            {
                var method = AccessTools.Method(typeof(WatchTowerManager), name);
                if (method != null) yield return method;
            }
        }

        static void Prefix() => Night8Sync.Instance?.LocalHuntStarts();
    }

    // Night 8 begins (in bed, Connor on the radio).
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq8Start))]
    internal static class Night8StartPatch
    {
        static void Postfix(WatchTowerManager __instance)
        {
            Night8Sync.Instance?.Reset();
            // Night 7's way out on the stairs is spent: it stayed armed in the world of the player who had not walked
            // through it, and restarted night 8 for both from there (playtest).
            if (__instance.Seq7FadeOutTrigger) __instance.Seq7FadeOutTrigger.SetActive(false);
        }
    }

    // Connor's second call, as the player stands up from bed: it comes once, to whoever gets there first (the trigger was
    // armed in both worlds and brought it again for the partner, at the door).
    [HarmonyPatch(typeof(WatchTowerManager), nameof(WatchTowerManager.Seq8StandUpConvoConnor))]
    internal static class Night8StandUpTalkPatch
    {
        static bool Prefix(WatchTowerManager __instance)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready || !__instance.seq8ConnorConvo2Done) return true;
            Plugin.Log("NIGHT8_STANDUP_TALK_ALREADY_DONE");
            return false;
        }
    }
}
