using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Per-player inventory: whoever picks up a story item owns it until they drop or place it.
    // The partner's copy disappears from their world and is drawn in the partner avatar's hand instead.
    // Flashlight and firewood stay personal (each player has their own / endless supply). The bag is the team's one bag
    // in every scene: whoever carried it out of a scene still carries it in the next, and the story's "have the bag"
    // checks pass when either player holds it.
    internal sealed class ItemSync
    {
        static readonly HashSet<string> SharedTags = new HashSet<string> {
            "Anemometer", "Binoculars", "Box", "Bucket", "Casserole", "Compass", "Gas", "Matchbox", "Mug", "Spray", "FridgeFood",
            "Napkin", "Salt", "Pepper", "Ketchup", "Bag" };
        public static bool IsPickupTag(string tag) => SharedTags.Contains(tag) || tag == "Flashlight" || tag == "Wood";
        static bool IsShared(string tag, string scene) => SharedTags.Contains(tag);
        // Who carries the team's bag, kept across scene loads: "local", "remote" or "none".
        static string teamBag = "none";
        public static string TeamBag => teamBag;
        internal static bool TeamHasBag
        {
            get
            {
                var gm = GameManager.instance;
                if (gm && gm.itemPickUp && gm.itemPickUp.inHandBag) return true;
                var plugin = Plugin.Instance;
                var remote = plugin != null && plugin.Ready ? plugin.Items.RemoteHeldObject : null;
                return remote && remote.CompareTag("Bag");
            }
        }

        sealed class Item
        {
            public string Key;
            public GameObject Target;
            public bool HeldLocally, HeldRemotely;
            public string LastPose;
            // Throws and carries arrive ~7 times a second; the item glides between them.
            public Vector3 GlidePosition; public Quaternion GlideRotation; public bool Gliding;
            // In the RV: how long it has been a loose falling body, and whether the RV's own colliders ignore it.
            public float LooseSince = -1f; public bool IgnoringRv;
        }

        Collider[] rvColliders;

        // Something put down in the RV (the bag) rides along: once it has dropped onto the floor it is fixed to the RV and
        // the RV's own colliders stop touching it. A fixed body that still touched them (a copy placed from the partner's
        // world, or a bag wedged against the floor) pushed on the RV and made it crawl or stop.
        void RideAlong(Item item, RVController rv)
        {
            var target = item.Target;
            // Unity forgets ignored pairs when an object is switched off (the partner carrying it hides ours).
            if (target && (!target.activeInHierarchy || item.HeldRemotely)) { item.IgnoringRv = false; item.LooseSince = -1f; return; }
            if (!target || item.HeldLocally || IsInLocalHands(target.transform) || !target.TryGetComponent<Rigidbody>(out var body)) return;
            bool inRv = target.transform.IsChildOf(rv.transform);
            // Lying loose inside the cab (put down by the partner, or the game left it loose): it belongs to the RV
            // from now on, so it travels with it instead of standing in the road for the RV to hit.
            if (!inRv && target.transform.parent == null && InsideRv(rv, target.transform.position) && (body.isKinematic || body.IsSleeping()))
            {
                target.transform.SetParent(rv.transform, true);
                inRv = true;
            }
            if (!body.isKinematic)
            {
                if (item.IgnoringRv) IgnoreRv(item, rv, false);
                if (!inRv) { item.LooseSince = -1f; return; }
                if (item.LooseSince < 0f) item.LooseSince = Time.unscaledTime;
                if (Time.unscaledTime - item.LooseSince < 1.5f) return;
                body.isKinematic = true;
                Plugin.Log("ITEM_RIDES_IN_RV " + item.Key);
            }
            item.LooseSince = -1f;
            if (!item.IgnoringRv) IgnoreRv(item, rv, true);
        }


        // Lab: the bag as it behaved before RideAlong (fixed where the partner put it, in the world, touching the RV).
        internal static bool LabOldBag;
        internal static GameObject LabBag => Instance?.items.Find(i => i.Target && i.Target.CompareTag("Bag"))?.Target;
        static ItemSync Instance;
        internal void LabSetOldBag(bool on)
        {
            LabOldBag = on;
            var rv = GameManager.instance ? GameManager.instance.rVController : null;
            if (!on || !rv) return;
            foreach (var item in items)
            {
                if (!item.Target || !item.Target.CompareTag("Bag") || !item.Target.TryGetComponent<Rigidbody>(out var body)) continue;
                item.Target.transform.SetParent(null, true);
                body.isKinematic = true;
                IgnoreRv(item, rv, false);
                Plugin.Log("LAB_OLD_BAG " + item.Key);
            }
        }
        static bool InsideRv(RVController rv, Vector3 point)
        {
            if (rv.boundsCollider is BoxCollider box)
            {
                var local = box.transform.InverseTransformPoint(point) - box.center;
                var half = box.size * 0.5f;
                return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
            }
            return rv.boundsCollider && rv.boundsCollider.bounds.Contains(point);
        }

        void IgnoreRv(Item item, RVController rv, bool ignore)
        {
            item.IgnoringRv = ignore;
            if (rvColliders == null || rvColliders.Length == 0) rvColliders = rv.GetComponentsInChildren<Collider>(true);
            var own = item.Target.GetComponentsInChildren<Collider>(true);
            foreach (var mine in own)
                foreach (var other in rvColliders)
                    if (other && mine && !other.transform.IsChildOf(item.Target.transform)) Physics.IgnoreCollision(mine, other, ignore);
        }

        readonly Plugin plugin;
        readonly List<Item> items = new List<Item>();
        readonly Dictionary<string, Item> byKey = new Dictionary<string, Item>();
        string registeredScene;
        float registerAt = -1, nextTick;
        public string RemoteHeldKey { get; private set; }
        public GameObject RemoteHeldObject => RemoteHeldKey != null && byKey.TryGetValue(RemoteHeldKey, out var item) ? item.Target : null;
        public int Count => items.Count;
        public string LocalHeld { get { foreach (var item in items) if (item.HeldLocally) return item.Key; return ""; } }
        public event Action<GameObject> RemoteHeldChanged;

        public ItemSync(Plugin plugin)
        {
            this.plugin = plugin;
            Instance = this;
            if (StorySync.Instance != null) StorySync.Instance.HeldItemCheck = IsRemotelyHeld;
        }

        bool IsRemotelyHeld(GameObject target)
        {
            foreach (var item in items) if (item.Target == target) return item.HeldRemotely;
            return false;
        }

        public void PartnerConnected()
        {
            // Host re-announces where every item is so a joining partner starts from the same world.
            foreach (var item in items) { item.HeldLocally = false; if (plugin.IsHost) item.LastPose = null; }
        }

        public void SceneLoaded()
        {
            items.Clear(); byKey.Clear(); registeredScene = null; rvColliders = null;
            SetRemoteHeld(null);
            registerAt = Time.unscaledTime + 1.5f;
        }

        void Register()
        {
            var scene = SceneManager.GetActiveScene();
            registeredScene = scene.name;
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                var go = transform.gameObject;
                // Hand-held stand-ins under the player prefab are the game's local visuals, not world items; the bag the
                // scene starts in our hands is the team's bag, at the same path in both worlds.
                bool inHands = IsInLocalHands(transform);
                if (go.scene != scene || !IsShared(go.tag, scene.name) || (inHands && !(go.CompareTag("Bag") && go.activeInHierarchy))) continue;
                // A baked casserole is a portion for each player (night 6's leftovers; night 4's two trays are made shared).
                if (go.CompareTag("Casserole") && go.TryGetComponent<Casserole>(out var dish) && SupperSync.Baked(dish)) continue;
                // Only the dish itself: the places a dish is put (on the lap in bed, in the microwave) carry its tag too.
                if (go.CompareTag("Casserole") && !go.GetComponent<Casserole>()) continue;
                // The registration path is the identity; both players load the same scene layout.
                string key = go.tag + ":" + PathOf(transform);
                if (byKey.ContainsKey(key)) continue;
                var item = new Item { Key = key, Target = go, LastPose = Pose(go.transform) };
                items.Add(item); byKey[key] = item;
                if (inHands) StartWithBag(item);
            }
            Plugin.Log("ITEMS_REGISTERED " + registeredScene + " count=" + items.Count + " teamBag=" + teamBag);
        }

        // A scene that starts with the bag in hand starts it in both players' hands; only the one who carried it here
        // (or the host, when nobody did) keeps it.
        void StartWithBag(Item item)
        {
            bool keep = plugin.Ready ? (teamBag == "local" || (teamBag == "none" && plugin.IsHost)) : true;
            if (keep) { item.HeldLocally = true; if (plugin.Ready) plugin.Send(new Packet { kind = "item", code = "take", name = item.Key, scene = registeredScene }); return; }
            TakeOutOfMyHands(item);
            item.HeldRemotely = true;
            SetRemoteHeld(item.Key);
            Plugin.Log("ITEM_BAG_WITH_PARTNER " + item.Key);
        }

        // The partner has this item: it leaves our hands (and the game's idea of what we hold) without being thrown.
        static void TakeOutOfMyHands(Item item)
        {
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            item.HeldLocally = false;
            if (!item.Target) return;
            if (pickup)
            {
                bool wasCurrent = pickup.currentItem == item.Target;
                if (pickup.bag == item.Target) { pickup.bag = null; pickup.inHandBag = false; }
                if (wasCurrent)
                {
                    pickup.inHand = false; pickup.currentItem = null; pickup.inHandCount = Mathf.Max(0, pickup.inHandCount - 1);
                    if (GameManager.instance.uIManager) GameManager.instance.uIManager.controlsText.text = "";
                }
            }
            item.Target.transform.SetParent(null, true);
            if (item.Target.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
            item.Target.SetActive(false);
        }

        internal static string PathOf(Transform transform)
        {
            string path = transform.name + "#" + transform.GetSiblingIndex();
            while (transform.parent) { transform = transform.parent; path = transform.name + "#" + transform.GetSiblingIndex() + "/" + path; }
            return path;
        }

        internal static Transform FindPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Split('/');
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var current = root.transform;
                if (Segment(current) != parts[0]) continue;
                for (int i = 1; i < parts.Length && current; i++)
                {
                    Transform next = null;
                    foreach (Transform child in current) if (Segment(child) == parts[i]) { next = child; break; }
                    // Child order can differ between the worlds (something was moved or added at runtime): a name that
                    // is unique among the siblings still identifies the object.
                    if (!next)
                    {
                        string name = parts[i].Substring(0, Math.Max(0, parts[i].LastIndexOf('#')));
                        foreach (Transform child in current)
                            if (child.name == name) { if (next) { next = null; break; } next = child; }
                    }
                    current = next;
                }
                if (current) return current;
            }
            return null;
        }

        static string Segment(Transform transform) => transform.name + "#" + transform.GetSiblingIndex();

        // Where it lies relative to what holds it: something riding in the moving RV has not moved.
        static string Pose(Transform transform)
        {
            var p = transform.localPosition; var r = transform.localRotation;
            return string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},{3:F4},{4:F4},{5:F4},{6:F4},{7}", p.x, p.y, p.z, r.x, r.y, r.z, r.w, transform.parent ? transform.parent.GetInstanceID() : 0);
        }

        bool IsInLocalHands(Transform transform)
        {
            var gm = GameManager.instance;
            return gm && gm.playerFP && transform.IsChildOf(gm.playerFP.transform);
        }

        public void Tick()
        {
            if (registerAt > 0 && Time.unscaledTime >= registerAt) { registerAt = -1; Register(); }
            if (registeredScene != SceneManager.GetActiveScene().name) return;
            foreach (var item in items)
                if (item.Key.StartsWith("Bag:")) { teamBag = item.HeldLocally ? "local" : item.HeldRemotely ? "remote" : "none"; break; }
            float glide = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            foreach (var item in items)
            {
                // Story code may re-enable an object while the partner is carrying it.
                if (item.HeldRemotely && item.Target && item.Target.activeSelf) item.Target.SetActive(false);
                if (!item.Gliding || !item.Target) continue;
                var t = item.Target.transform;
                t.localPosition = Vector3.Lerp(t.localPosition, item.GlidePosition, glide);
                t.localRotation = Quaternion.Slerp(t.localRotation, item.GlideRotation, glide);
                if ((t.localPosition - item.GlidePosition).sqrMagnitude < 1e-6f) { t.localPosition = item.GlidePosition; t.localRotation = item.GlideRotation; item.Gliding = false; }
                item.LastPose = Pose(t) + (item.Target.activeSelf ? ",1" : ",0");
            }
            var rv = GameManager.instance ? GameManager.instance.rVController : null;
            if (rv && !LabOldBag) foreach (var item in items) RideAlong(item, rv);
            if (!plugin.Ready || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.15f;
            foreach (var item in items)
            {
                if (item.HeldRemotely || item.Gliding) continue;
                if (!item.Target)
                {
                    if (item.LastPose != "gone") { item.LastPose = "gone"; plugin.Send(new Packet { kind = "item", code = "gone", name = item.Key, scene = registeredScene }); }
                    continue;
                }
                bool held = IsInLocalHands(item.Target.transform);
                if (held != item.HeldLocally)
                {
                    item.HeldLocally = held;
                    if (held) { plugin.Send(new Packet { kind = "item", code = "take", name = item.Key, scene = registeredScene }); Plugin.Log("ITEM_TAKE " + item.Key); }
                    else { SendPlacement(item); Plugin.Log("ITEM_DROP " + item.Key); }
                    continue;
                }
                if (held) continue;
                // Follows throws, physics settling and story code that moves or hides the item.
                string pose = Pose(item.Target.transform) + (item.Target.activeSelf ? ",1" : ",0");
                if (pose != item.LastPose) SendPlacement(item);
            }
        }

        void SendPlacement(Item item)
        {
            var transform = item.Target.transform;
            item.LastPose = Pose(transform) + (item.Target.activeSelf ? ",1" : ",0");
            // On something only this player has in that place (the tray on the lap while eating in bed, where each sits
            // on their own end): where it is in the world, not the partner's copy of that rig.
            bool personal = transform.parent && OnPersonalRig(transform.parent);
            plugin.Send(new Packet {
                kind = "item", code = "place", name = item.Key, scene = registeredScene,
                build = transform.parent && !personal ? PathOf(transform.parent) : "",
                position = personal ? transform.position : transform.localPosition, rotation = personal ? transform.rotation : transform.localRotation, active = item.Target.activeSelf
            });
        }

        static bool OnPersonalRig(Transform parent)
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            if (!tower) return false;
            foreach (var rig in new[] { tower.playerInBedEating, tower.playerInBed, tower.playerInBedSeq5 })
                if (rig && parent.IsChildOf(rig.transform)) return true;
            return false;
        }

        public void Receive(Packet packet)
        {
            if (packet.scene != registeredScene || packet.name == null || !byKey.TryGetValue(packet.name, out var item)) return;
            switch (packet.code)
            {
                case "take":
                    // Both took it at once: the host keeps it, the guest's hands are emptied properly.
                    if (item.HeldLocally) { Plugin.Log("ITEM_CONFLICT " + item.Key); if (plugin.IsHost) return; TakeOutOfMyHands(item); }
                    item.HeldRemotely = true;
                    if (item.Target) item.Target.SetActive(false);
                    SetRemoteHeld(item.Key);
                    break;
                case "place":
                    if (!item.Target || item.HeldLocally) return;
                    item.HeldRemotely = false;
                    if (RemoteHeldKey == item.Key) SetRemoteHeld(null);
                    var parent = FindPath(packet.build);
                    var target = item.Target.transform;
                    bool wasVisible = item.Target.activeSelf;
                    var previous = target.position;
                    target.SetParent(parent, true);
                    // The sender's physics is authoritative for where it lands.
                    if (item.Target.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
                    item.Target.SetActive(packet.active);
                    var destination = parent ? parent.TransformPoint(packet.position) : packet.position;
                    if (wasVisible && packet.active && (destination - previous).sqrMagnitude < 16f)
                    {
                        item.GlidePosition = packet.position; item.GlideRotation = packet.rotation.normalized; item.Gliding = true;
                    }
                    else
                    {
                        item.Gliding = false;
                        target.localPosition = packet.position;
                        target.localRotation = packet.rotation;
                    }
                    item.LastPose = Pose(target) + (packet.active ? ",1" : ",0");
                    break;
                case "gone":
                    item.HeldRemotely = false;
                    if (RemoteHeldKey == item.Key) SetRemoteHeld(null);
                    if (item.Target) item.Target.SetActive(false);
                    item.LastPose = "gone";
                    break;
            }
        }

        // An object made at runtime in both worlds (night 4's second portion): shared under a key both sides agree on.
        internal void AddShared(GameObject target, string key)
        {
            if (!target || byKey.ContainsKey(key)) return;
            var item = new Item { Key = key, Target = target, LastPose = Pose(target.transform) + (target.activeSelf ? ",1" : ",0") };
            items.Add(item); byKey[key] = item;
            Plugin.Log("ITEM_SHARED " + key);
        }

        internal string KeyOf(GameObject target) => items.Find(i => i.Target == target)?.Key;
        internal GameObject Find(string key) => key != null && byKey.TryGetValue(key, out var item) ? item.Target : null;

        // From now on the object is each player's own (the partner has one of their own in their world).
        internal void MakePersonal(GameObject target)
        {
            var item = items.Find(i => i.Target == target);
            if (item == null) return;
            if (item.HeldRemotely && target) target.SetActive(true);
            items.Remove(item); byKey.Remove(item.Key);
            if (RemoteHeldKey == item.Key) SetRemoteHeld(null);
            Plugin.Log("ITEM_PERSONAL " + item.Key);
        }

        public void WriteList(string path)
        {
            var list = new Newtonsoft.Json.Linq.JArray();
            foreach (var item in items)
            {
                if (!item.Target) continue;
                var p = item.Target.transform.position;
                list.Add(new Newtonsoft.Json.Linq.JObject { ["key"] = item.Key, ["active"] = item.Target.activeInHierarchy,
                    ["x"] = p.x, ["y"] = p.y, ["z"] = p.z, ["held"] = item.HeldLocally, ["remote"] = item.HeldRemotely });
            }
            System.IO.File.WriteAllText(path, list.ToString());
        }

        void SetRemoteHeld(string key)
        {
            if (RemoteHeldKey == key) return;
            RemoteHeldKey = key;
            RemoteHeldChanged?.Invoke(RemoteHeldObject);
        }
    }

    // The trail up to the tower needs the bag: the team has it when either player carries it.
    [HarmonyLib.HarmonyPatch(typeof(TrailStartManager), nameof(TrailStartManager.FadeToNextScene))]
    internal static class TeamBagTrailPatch
    {
        static bool Prefix(TrailStartManager __instance)
        {
            var gm = GameManager.instance;
            if (Plugin.Instance == null || !Plugin.Instance.Ready || !__instance.flashLightRecieved || gm.itemPickUp.inHandBag || !ItemSync.TeamHasBag) return true;
            Plugin.Log("TEAM_BAG_TRAIL partner carries the bag");
            gm.uIManager.FadeOut("Watchtower");
            return false;
        }
    }

    // The footsteps behind the player on the trail start for the lead once the team has the bag and the flashlight.
    [HarmonyLib.HarmonyPatch(typeof(TrailStartManager), nameof(TrailStartManager.CheckFootSteps))]
    internal static class TeamBagFootstepsPatch
    {
        static bool Prefix(TrailStartManager __instance)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready) return true;
            if (plugin.FollowsStory) return false;
            if (__instance.flashLightRecieved && !GameManager.instance.itemPickUp.inHandBag && ItemSync.TeamHasBag) __instance.badGuyFootsteps.StartFootsteps();
            return !(!GameManager.instance.itemPickUp.inHandBag && ItemSync.TeamHasBag);
        }
    }

    // "I left the bag outside" is not true while the partner carries it in.
    [HarmonyLib.HarmonyPatch]
    internal static class TeamBagSubtitlePatch
    {
        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var method in typeof(Subtitle).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                if (method.Name == nameof(Subtitle.ShowSubAndClose)) yield return method;
        }

        static bool Prefix(string content)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Ready || string.IsNullOrEmpty(content) || !ItemSync.TeamHasBag) return true;
            foreach (var key in new[] { "BagOutside", "GetBag", "BagRV" })
                if (content == F2FLocalizedText.GetLocalizedText("ep4_subs", key)) { Plugin.Log("SUBTITLE_SKIPPED " + key); return false; }
            return true;
        }
    }
}
