using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Shared story progress for two independent single-player simulations, modelled on Scratch Creek:
    // either player can advance the story; personal moments (scares, chases, seats, camera effects) stay local.
    // Mirrors story-manager fields and the scene objects those managers reference, in both directions.
    internal sealed class StorySync
    {
        // Player-local state and scare machinery never leaves the instance that produced it.
        static readonly Regex PersonalField = new Regex(
            @"^(state|isPlayerInside|wasOutside|insideShack|pointingAtBed|inPee\w*|peed|seqPeed|seqCanPee|seq7Peed|seq7CanPee|blink\d+once|sips|clicks|seq3Next\w+|seq\dCanGetOff|seq5CanGetOutOfBed|mounted|canMount|inMountArea|isStairsFootstepsPlaying|seq8ShiftSubOn|seq8SeenUnderBed|seq6PlayerInConvoTrigger|seq6TalkingTo\w+" +
            // Where each player is and what they sit on: the diner door, booths, the washroom, conversations, the RV.
            @"|dinerState|playerLockedToSeat|playerSittingFirstTime|bagInhand|inConversation|isPlayerInsideTheRV|playerCaught)$" +
            @"|(?i)jump|haunt|farscare|closejump|chas|lookatcultist|cultist(walking|waiting|onscreen)|onscreen|cultseen|flashwentoff|skullseen",
            RegexOptions.Compiled);
        static readonly Regex PersonalObject = new Regex(
            @"(?i)jump|cultist|crowd|badguy|deadguy|scare|hunt|chase|blood|anomaly|blur|vhs|grain|distortion|dream|cam$|cam\d|camera|fov|shake|heart|glitch|scream|stab|underbed|rainpro|player|binocularsmodel|flashlight|^bag$|itemhere",
            RegexOptions.Compiled);
        static readonly HashSet<string> StatefulComponents = new HashSet<string> { "TowerSunBlind" };
        static readonly Regex PersonalTag = new Regex(@"(?i)lightswitchcollider|blind", RegexOptions.Compiled);
        static readonly Regex SharedFloat = new Regex(@"^(seq\dTemp|foodOrder|drinkOrder)$", RegexOptions.Compiled);
        static readonly HashSet<string> Transitions = new HashSet<string> { "FadeToSleep", "FadeOutSeq7", "FadeOutSeq8", "FadeOutWithOutro" };

        abstract class Entry
        {
            public string Key;
            public string Last;
            public abstract string Read();
            public abstract void Apply(string value);
        }

        sealed class FieldEntry : Entry
        {
            public object Owner; public FieldInfo Field;
            public override string Read()
            {
                var value = Field.GetValue(Owner);
                if (value is bool b) return b ? "1" : "0";
                if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
                if (value is Enum) return Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture);
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
            public override void Apply(string value)
            {
                var type = Field.FieldType;
                object parsed;
                if (type == typeof(bool)) parsed = value == "1";
                else if (type == typeof(int)) parsed = int.Parse(value, CultureInfo.InvariantCulture);
                else if (type == typeof(float)) parsed = float.Parse(value, CultureInfo.InvariantCulture);
                else if (type.IsEnum) parsed = Enum.ToObject(type, int.Parse(value, CultureInfo.InvariantCulture));
                else parsed = value;
                Field.SetValue(Owner, parsed);
            }
        }

        sealed class ActiveEntry : Entry
        {
            public GameObject Target;
            public override string Read() => Target ? (Target.activeSelf ? "1" : "0") : null;
            public override void Apply(string value) { if (Target && !StorySync.Instance.IsHeldItem(Target)) Target.SetActive(value == "1"); }
        }

        // Story code marks objects clickable or not (the generator after the power cut, the table while someone sits);
        // the interaction dot reads that tag, so the partner's copy needs the same one.
        sealed class TagEntry : Entry
        {
            public GameObject Target;
            public override string Read() => Target ? Target.tag : null;
            public override void Apply(string value) { if (Target && !string.IsNullOrEmpty(value) && Target.tag != value) Target.tag = value; }
        }

        // A story object's click collider switched on or off (the firewood pile once Connor asks for wood).
        sealed class ColliderEntry : Entry
        {
            public Collider Target;
            public override string Read() => Target ? (Target.enabled ? "1" : "0") : null;
            public override void Apply(string value) { if (Target) Target.enabled = value == "1"; }
        }

        sealed class PoseEntry : Entry
        {
            public Transform Target;
            public Vector3 TargetPosition; public Quaternion TargetRotation; public bool Moving;
            public override string Read()
            {
                if (!Target) return null;
                var p = Target.localPosition; var r = Target.localEulerAngles;
                // Rounded so idle physics jitter does not produce traffic.
                return string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},{3:F1},{4:F1},{5:F1}", p.x, p.y, p.z, r.x, r.y, r.z);
            }
            public override void Apply(string value)
            {
                if (!Target) return;
                var parts = value.Split(',');
                if (parts.Length != 6) return;
                float F(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture);
                TargetPosition = new Vector3(F(0), F(1), F(2));
                TargetRotation = Quaternion.Euler(F(3), F(4), F(5));
                Moving = true;
            }
        }

        sealed class MaterialEntry : Entry
        {
            public Material Target;
            public override string Read() => Target ? (Target.IsKeywordEnabled("_EMISSION") ? "1" : "0") : null;
            public override void Apply(string value)
            {
                if (!Target) return;
                if (value == "1") Target.EnableKeyword("_EMISSION"); else Target.DisableKeyword("_EMISSION");
            }
        }

        sealed class AudioEntry : Entry
        {
            public AudioSource Target;
            public override string Read() => Target ? (Target.isPlaying ? "1" : "0") : null;
            public override void Apply(string value)
            {
                if (!Target) return;
                if (value == "1" && !Target.isPlaying) Target.Play();
                else if (value == "0" && Target.isPlaying) Target.Stop();
            }
        }

        sealed class ParticleEntry : Entry
        {
            public ParticleSystem Target;
            public override string Read() => Target ? (Target.isEmitting ? "1" : "0") : null;
            public override void Apply(string value)
            {
                if (!Target) return;
                if (value == "1" && !Target.isEmitting) Target.Play(true);
                else if (value == "0" && Target.isEmitting) Target.Stop(true);
            }
        }

        internal static StorySync Instance;
        readonly Plugin plugin;
        readonly List<Entry> entries = new List<Entry>();
        readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>();
        readonly List<PoseEntry> poses = new List<PoseEntry>();
        string registeredScene;
        float registerAt = -1, nextTick;
        bool fullStatePending;
        internal bool ApplyingRemote;
        string lastTransition; float lastTransitionTime = -100f;
        public int SentChanges { get; private set; }
        public int AppliedChanges { get; private set; }
        public int Tracked => entries.Count;
        internal Func<GameObject, bool> HeldItemCheck;
        // Objects one player's moment uses for a while (Marcus's stair block, the flickering garage light): not mirrored
        // either way meanwhile; their final state goes over again once released.
        internal readonly HashSet<UnityEngine.Object> LocalOnly = new HashSet<UnityEngine.Object>();
        bool IsLocalOnly(Entry entry) => LocalOnly.Count > 0 && ((entry is ActiveEntry a && LocalOnly.Contains(a.Target)) || (entry is MaterialEntry m && LocalOnly.Contains(m.Target)));

        public StorySync(Plugin plugin) { this.plugin = plugin; Instance = this; }

        bool IsHeldItem(GameObject target) => HeldItemCheck != null && HeldItemCheck(target);

        public void SceneLoaded()
        {
            entries.Clear(); byKey.Clear(); poses.Clear(); LocalOnly.Clear();
            registeredScene = null;
            // Managers configure the scene in Start; register after that has settled.
            registerAt = Time.unscaledTime + 1.5f;
        }

        // Lab: the keys of the mirrored entries of one kind ("C:", "K:"...), with their current values.
        internal System.Collections.Generic.List<string> LabEntries(string prefix) { var list = new System.Collections.Generic.List<string>(); foreach (var e in entries) if (e.Key.StartsWith(prefix)) { string v; try { v = e.Read(); } catch { v = "?"; } list.Add(e.Key + "=" + v); } return list; }
        public void PartnerConnected() { if (plugin.IsHost) fullStatePending = true; }
        public void FullStateRequested(Packet packet) { if (plugin.IsHost && packet.scene == registeredScene) fullStatePending = true; }

        IEnumerable<MonoBehaviour> Managers()
        {
            var gm = GameManager.instance;
            if (!gm) yield break;
            if (gm.dinerManager) yield return gm.dinerManager;
            if (gm.trailStartManager) yield return gm.trailStartManager;
            if (gm.watchTowerManager) yield return gm.watchTowerManager;
            if (gm.campfireManager) yield return gm.campfireManager;
            if (gm.trailEndManager) yield return gm.trailEndManager;
        }

        void Register()
        {
            registeredScene = SceneManager.GetActiveScene().name;
            var seen = new HashSet<UnityEngine.Object>();
            foreach (var manager in Managers())
            {
                string prefix = manager.GetType().Name + ".";
                foreach (var field in manager.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) continue;
                    var type = field.FieldType;
                    string key = prefix + field.Name;
                    bool scalar = type == typeof(bool) || type == typeof(int) || type == typeof(string) || type.IsEnum
                        || (type == typeof(float) && SharedFloat.IsMatch(field.Name));
                    if (scalar)
                    {
                        if (!PersonalField.IsMatch(field.Name)) Add(new FieldEntry { Key = "F:" + key, Owner = manager, Field = field });
                        continue;
                    }
                    if (PersonalObject.IsMatch(field.Name)) continue;
                    object value;
                    try { value = field.GetValue(manager); } catch { continue; }
                    if (value is Array array)
                    {
                        for (int i = 0; i < array.Length; i++) AddObject(key + "[" + i + "]", array.GetValue(i) as UnityEngine.Object, seen);
                    }
                    else AddObject(key, value as UnityEngine.Object, seen);
                }
            }
            Plugin.Log("STORY_REGISTERED " + registeredScene + " entries=" + entries.Count);
            if (plugin.IsHost) fullStatePending = true;
            // A client that (re)loaded asks the host for everything it missed.
            else if (plugin.Ready) plugin.Send(new Packet { kind = "story-request", scene = registeredScene });
            PersonalMoments.Instance?.SceneRegistered();
        }

        void AddObject(string key, UnityEngine.Object value, HashSet<UnityEngine.Object> seen)
        {
            if (!value || !seen.Add(value)) return;
            if (value is Material material) { Add(new MaterialEntry { Key = "M:" + key, Target = material }); return; }
            if (value is ParticleSystem particles) { Add(new ParticleEntry { Key = "P:" + key, Target = particles }); return; }
            // Interactable components keep their own state flags (e.g. TowerSunBlind.isOpen); mirror those too.
            if (value is Component stateful && StatefulComponents.Contains(stateful.GetType().Name))
                foreach (var field in stateful.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (field.FieldType == typeof(bool)) Add(new FieldEntry { Key = "F:" + key + "." + field.Name, Owner = stateful, Field = field });
            if (value is AudioSource audio)
            {
                // World sounds only: 2D stingers are personal and must not reach a distant partner.
                if (audio.spatialBlend >= 0.5f && !PersonalObject.IsMatch(audio.name)) Add(new AudioEntry { Key = "A:" + key, Target = audio });
                return;
            }
            GameObject go = value as GameObject;
            if (!go && value is Component component && !(component is Transform) && IsWorldComponent(component)) go = component.gameObject;
            if (!go && value is Transform transform) go = transform.gameObject;
            if (!go || !go.scene.IsValid() || PersonalObject.IsMatch(go.name) || IsPlayerOwned(go.transform) || ItemSync.IsPickupTag(go.tag)) return;
            Add(new ActiveEntry { Key = "G:" + key, Target = go });
            bool animated = go.GetComponentInChildren<Animator>(true);
            // Sun blinds and the light switch are tagged by where the local player stands (inside or out), not by the story.
            if (!animated && go.GetComponent<Collider>() && !go.GetComponent<TowerSunBlind>() && !PersonalTag.IsMatch(key))
            {
                Add(new TagEntry { Key = "K:" + key, Target = go });
                if (go.TryGetComponent<Collider>(out var click) && !click.isTrigger) Add(new ColliderEntry { Key = "C:" + key, Target = click });
            }
            // Animated characters and physics bodies move on their own in each simulation.
            if (animated || (go.TryGetComponent<Rigidbody>(out var body) && !body.isKinematic)) return;
            var pose = new PoseEntry { Key = "T:" + key, Target = go.transform };
            Add(pose); poses.Add(pose);
        }

        // Story items and NPC behaviours are synchronised elsewhere (ownership) or stay local.
        static bool IsWorldComponent(Component component)
        {
            string name = component.GetType().Name;
            return !(component is Behaviour behaviour && (name.Contains("Cultist") || name.Contains("BadGuy") || name.Contains("NPC")))
                && !(component is Camera) && !(component is Animator);
        }

        static bool IsPlayerOwned(Transform transform)
        {
            var gm = GameManager.instance;
            if (!gm || !gm.playerFP) return false;
            return transform.IsChildOf(gm.playerFP.transform);
        }

        void Add(Entry entry)
        {
            if (byKey.ContainsKey(entry.Key)) return;
            entry.Last = entry.Read();
            entries.Add(entry); byKey[entry.Key] = entry;
        }

        public void Tick()
        {
            if (registerAt > 0 && Time.unscaledTime >= registerAt) { registerAt = -1; Register(); }
            foreach (var pose in poses)
            {
                if (!pose.Moving || !pose.Target) continue;
                float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
                pose.Target.localPosition = Vector3.Lerp(pose.Target.localPosition, pose.TargetPosition, k);
                pose.Target.localRotation = Quaternion.Slerp(pose.Target.localRotation, pose.TargetRotation, k);
                if ((pose.Target.localPosition - pose.TargetPosition).sqrMagnitude < 1e-6f && Quaternion.Angle(pose.Target.localRotation, pose.TargetRotation) < 0.1f)
                {
                    pose.Target.localPosition = pose.TargetPosition; pose.Target.localRotation = pose.TargetRotation;
                    pose.Moving = false; pose.Last = pose.Read();
                }
            }
            if (!plugin.Ready || registeredScene != SceneManager.GetActiveScene().name || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.1f;
            var changes = new List<string>();
            bool full = fullStatePending;
            fullStatePending = false;
            foreach (var entry in entries)
            {
                // Objects still easing toward a remote pose are not echoed back.
                if (entry is PoseEntry pose && pose.Moving) continue;
                if (IsLocalOnly(entry)) continue;
                string value;
                try { value = entry.Read(); } catch { continue; }
                if (value == null) continue;
                if (!full && value == entry.Last) continue;
                entry.Last = value;
                changes.Add(entry.Key + "=" + value);
            }
            if (changes.Count == 0) return;
            // Chunks keep each message far below transport limits.
            for (int start = 0; start < changes.Count; start += 150)
                plugin.Send(new Packet { kind = full ? "story-full" : "story", scene = registeredScene,
                    changes = changes.GetRange(start, Math.Min(150, changes.Count - start)).ToArray() });
            SentChanges += changes.Count;
        }

        public void Receive(Packet packet)
        {
            if (packet.changes == null || packet.scene != registeredScene) return;
            ApplyingRemote = true;
            try
            {
                foreach (var change in packet.changes)
                {
                    int split = change.IndexOf('=');
                    if (split <= 0 || !byKey.TryGetValue(change.Substring(0, split), out var entry) || IsLocalOnly(entry)) continue;
                    string value = change.Substring(split + 1);
                    try { entry.Apply(value); entry.Last = value; AppliedChanges++; AfterApply(entry.Key, value); }
                    catch (Exception ex) { Plugin.Log("STORY_APPLY_FAILED " + entry.Key + " " + ex.Message); }
                }
            }
            finally { ApplyingRemote = false; }
        }

        // Story effects that create objects only in the actor's world need an explicit counterpart here.
        static void AfterApply(string key, string value)
        {
            // The waitress greeted the lead: the partner, already inside, may now take a seat too.
            var diner = GameManager.instance ? GameManager.instance.dinerManager : null;
            if (diner && key == "F:DinerManager.waitressIntroDone" && value == "1" && diner.dinerState == DinerManager.DinerState.PlayerInside) diner.TurnOnSeats();
            if (key == "F:TrailStartManager.flashLightRecieved" && value == "1") TrailSync.FlashlightReceived();
            if (key == "F:TrailStartManager.KeysRecieved" && value == "1") KeysTakenPatch.ShowKeys();
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            // The partner unlocked the tower: the key leaves our screen too, as it does when we unlock it.
            if (tower && key == "F:WatchTowerManager.isLocked" && value == "0" && GameManager.instance.uIManager)
            {
                GameManager.instance.uIManager.screenSpaceUI.SetActive(false);
                GameManager.instance.uIManager.objectsParent.SetActive(false);
            }
            if (!tower || key != "F:WatchTowerManager.woodInStove") return;
            bool inStove = tower.wood && tower.woodStovePosition && tower.wood.transform.parent == tower.woodStovePosition && tower.wood.activeSelf;
            if (value == "1" && !inStove)
            {
                // Same result as ItemPickUp.WoodToStove without touching the partner's own hands.
                var prefab = GameManager.instance.itemPickUp ? GameManager.instance.itemPickUp.woodPilePrefab : null;
                if (!prefab) return;
                var wood = UnityEngine.Object.Instantiate(prefab, tower.woodStovePosition);
                wood.transform.localPosition = Vector3.zero;
                wood.transform.localEulerAngles = Vector3.zero;
                wood.tag = "Untagged";
                tower.wood = wood;
                tower.woodPrevSeq = wood;
            }
            else if (value == "0" && inStove) tower.wood.SetActive(false);
        }

        // Night transitions and the finale run for both players at once.
        internal bool BeforeTransition(string name, int argument)
        {
            if (!Transitions.Contains(name)) return true;
            // Marcus's night 4 errand comes before his sleep.
            if (name == "FadeToSleep" && !ApplyingRemote && NightErrand.Instance != null && !NightErrand.Instance.AllowsSleep(argument)) return false;
            string id = name + ":" + argument;
            if (ApplyingRemote) { lastTransition = id; lastTransitionTime = Time.unscaledTime; plugin.Sleep.NightEnds(); return true; }
            // The partner already started this transition for us; a second local start would replay it.
            if (lastTransition == id && Time.unscaledTime - lastTransitionTime < 8f) return false;
            // Going to bed first: lie down and wait for the partner; the night ends when both are in bed.
            if (name == "FadeToSleep") { if (plugin.Sleep.WaitForPartner(argument)) return false; plugin.Sleep.NightEnds(); }
            lastTransition = id; lastTransitionTime = Time.unscaledTime;
            if (plugin.Ready) plugin.Send(new Packet { kind = "transition", code = name, arg = argument, scene = SceneManager.GetActiveScene().name });
            Plugin.Log("TRANSITION_LOCAL " + id);
            return true;
        }

        public void ReceiveTransition(Packet packet)
        {
            var ui = GameManager.instance ? GameManager.instance.uIManager : null;
            if (!ui || packet.scene != SceneManager.GetActiveScene().name || !Transitions.Contains(packet.code)) return;
            var method = AccessTools.Method(typeof(UIManager), packet.code);
            if (method == null) return;
            ApplyingRemote = true;
            try
            {
                var routine = (IEnumerator)method.Invoke(ui, method.GetParameters().Length == 1 ? new object[] { packet.arg } : new object[0]);
                ui.StartCoroutine(routine);
            }
            finally { ApplyingRemote = false; }
            Plugin.Log("TRANSITION_REMOTE " + packet.code + ":" + packet.arg);
        }
    }

    [HarmonyPatch]
    internal static class TransitionPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "FadeToSleep", "FadeOutSeq7", "FadeOutSeq8", "FadeOutWithOutro" })
            {
                var method = AccessTools.Method(typeof(UIManager), name);
                if (method != null) yield return method;
            }
        }

        static bool Prefix(MethodBase __originalMethod, object[] __args, ref IEnumerator __result)
        {
            var sync = StorySync.Instance;
            if (sync == null || Plugin.Instance == null) return true;
            int argument = __args.Length > 0 && __args[0] is int value ? value : 0;
            if (sync.BeforeTransition(__originalMethod.Name, argument)) return true;
            __result = Empty();
            return false;
        }

        static IEnumerator Empty() { yield break; }
    }
}
