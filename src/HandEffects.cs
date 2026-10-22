using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // What a player does with the thing in their hand is seen by the partner:
    // - personal hand items (the tower's bear spray, the mug) are shown in the avatar's hand by their scene path;
    // - bear spray sprays (particles and hiss) from the partner's can;
    // - a struck match flies and burns out in the partner's world too (visual only, it lights nothing there);
    // - a dropped flashlight lies in both worlds; each dropped one is its own flashlight and anyone can pick any up.
    internal sealed class HandEffects
    {
        readonly Plugin plugin;
        bool spraying, matchboxOpen, pouring;
        int sprayFrom;

        // Dropped flashlights by id ("h3", "c1"): ours that we dropped and copies of the partner's.
        readonly Dictionary<string, GameObject> torches = new Dictionary<string, GameObject>();
        readonly Dictionary<GameObject, string> torchIds = new Dictionary<GameObject, string>();
        readonly Dictionary<string, Glide> glides = new Dictionary<string, Glide>();
        GameObject heldTorch;
        readonly HashSet<GameObject> copies = new HashSet<GameObject>();
        Vector3 handScale;
        int torchCounter;
        string movingTorch; float movingUntil, nextTorchPose;
        static readonly HashSet<int> mirroredMatches = new HashSet<int>();
        internal static bool Mirroring;
        public int TorchesInWorld
        {
            get
            {
                var player = GameManager.instance && GameManager.instance.playerFP ? GameManager.instance.playerFP.transform : null;
                int n = 0;
                foreach (var t in torches.Values) if (t && t.activeInHierarchy && !(player && t.transform.IsChildOf(player))) n++;
                return n;
            }
        }
        // Lab: the flashlight in our hand against our own flashlight's size there ("own", "ok", "off" or "-").
        public string HeldTorchScale
        {
            get
            {
                var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
                var held = pickup && pickup.inHandFlashlight ? pickup.flashLightItem : null;
                if (!held) return "-";
                if (!copies.Contains(held)) return "own";
                return (held.transform.localScale - handScale).sqrMagnitude < 1e-6f ? "ok" : "off" + held.transform.localScale.x.ToString("F2", CultureInfo.InvariantCulture);
            }
        }
        // Lab: where the first flashlight lying in the world is ("x;y;z").
        public string FirstTorchAt { get { foreach (var t in torches.Values) if (t && t.activeInHierarchy && !t.transform.IsChildOf(GameManager.instance.playerFP.transform)) { var p = t.transform.position; return p.x.ToString("F2", CultureInfo.InvariantCulture) + ";" + p.y.ToString("F2", CultureInfo.InvariantCulture) + ";" + p.z.ToString("F2", CultureInfo.InvariantCulture); } return "-"; } }
        public int MatchesMirrored { get; private set; }
        public int SpraysMirrored { get; private set; }

        sealed class Glide { public Vector3 Position; public Quaternion Rotation; }

        public HandEffects(Plugin plugin) { this.plugin = plugin; }

        public void SceneLoaded()
        {
            torches.Clear(); torchIds.Clear(); glides.Clear(); heldTorch = null; movingTorch = null; copies.Clear(); handScale = Vector3.zero;
            spraying = false; matchboxOpen = false; pouring = false; mirroredMatches.Clear();
        }

        // Scene path of a personal item in our hand (shared story items travel through ItemSync instead).
        internal const string WoodInHand = "#wood";
        public string LocalHandPath()
        {
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            if (!pickup || !pickup.inHand || !pickup.currentItem || plugin.Items.LocalHeld != "") return null;
            // Firewood is a fresh copy of the pile's log each time: it has no scene path the partner could find.
            if (pickup.inHandWood) return WoodInHand;
            return ItemSync.PathOf(pickup.currentItem.transform);
        }

        public void Tick()
        {
            var gm = GameManager.instance;
            var pickup = gm ? gm.itemPickUp : null;
            if (!pickup) return;
            foreach (var pair in glides)
            {
                if (!torches.TryGetValue(pair.Key, out var torch) || !torch) continue;
                float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
                torch.transform.position = Vector3.Lerp(torch.transform.position, pair.Value.Position, k);
                torch.transform.rotation = Quaternion.Slerp(torch.transform.rotation, pair.Value.Rotation, k);
            }
            if (!plugin.Ready) return;
            string scene = SceneManager.GetActiveScene().name;
            // Bear spray: the game's own "spraying" flag on the can in our hand.
            bool spray = pickup.inHandSpray && pickup.spray && pickup.spray.spraying;
            if (spray != spraying) { spraying = spray; plugin.Send(new Packet { kind = "fx", code = "spray", active = spray, scene = scene }); }
            // The gas can pouring (its stream onto the firewood): the partner sees and hears it from the can in our hand.
            bool pour = pickup.inHandGas && pickup.currentItem && pickup.currentItem.TryGetComponent<Gas>(out var gas) && gas.gasPs && gas.gasPs.isPlaying;
            if (pour != pouring) { pouring = pour; plugin.Send(new Packet { kind = "fx", code = "pour", active = pour, scene = scene }); }
            bool open = pickup.inHandMatchbox && pickup.currentItem && pickup.currentItem.TryGetComponent<Matchbox>(out var box) && !box.isClosed;
            if (open != matchboxOpen) { matchboxOpen = open; plugin.Send(new Packet { kind = "fx", code = "matchbox", active = open, scene = scene }); }
            Torches(pickup, scene);
            if (remoteSpray) AimRemoteSpray();
        }

        // ---- flashlights ----

        void Torches(ItemPickUp pickup, string scene)
        {
            var inHand = pickup.inHandFlashlight && pickup.flashLightItem ? pickup.flashLightItem : null;
            // Our own flashlight sits in the hand at the game's size; a picked-up copy of the partner's is held the same.
            if (inHand && inHand.transform.parent == pickup.itemHere)
            {
                if (!copies.Contains(inHand)) handScale = inHand.transform.localScale;
                else if (handScale != Vector3.zero && inHand.transform.localScale != handScale) inHand.transform.localScale = handScale;
            }
            if (inHand != heldTorch)
            {
                var dropped = heldTorch;
                heldTorch = inHand;
                // Thrown with G (the game makes it a falling body); story code that sets it down somewhere is not a drop.
                if (dropped && dropped.transform.parent == null && dropped.TryGetComponent<Rigidbody>(out var body) && !body.isKinematic)
                {
                    if (!torchIds.TryGetValue(dropped, out var id)) { id = (plugin.IsHost ? "h" : "c") + (++torchCounter); torchIds[dropped] = id; }
                    torches[id] = dropped;
                    glides.Remove(id);
                    SendTorch("torch-drop", id, dropped, scene);
                    movingTorch = id; movingUntil = Time.unscaledTime + 4f;
                    Plugin.Log("TORCH_DROP " + id + " at=" + dropped.transform.position.ToString("F2"));
                }
                if (inHand && torchIds.TryGetValue(inHand, out var takenId))
                {
                    glides.Remove(takenId);
                    if (movingTorch == takenId) movingTorch = null;
                    plugin.Send(new Packet { kind = "fx", code = "torch-take", name = takenId, scene = scene });
                    Plugin.Log("TORCH_TAKE " + takenId);
                }
            }
            // A thrown flashlight is followed while it falls and rolls.
            if (movingTorch != null && Time.unscaledTime >= nextTorchPose && torches.TryGetValue(movingTorch, out var moving) && moving)
            {
                nextTorchPose = Time.unscaledTime + 0.15f;
                SendTorch("torch-pose", movingTorch, moving, scene);
                if (Time.unscaledTime > movingUntil) movingTorch = null;
            }
        }

        void SendTorch(string code, string id, GameObject torch, string scene) =>
            plugin.Send(new Packet { kind = "fx", code = code, name = id, scene = scene, position = torch.transform.position, rotation = torch.transform.rotation });

        GameObject MakeTorchCopy()
        {
            // Our own flashlight is the model (any scene prop may be scaled differently).
            var template = TorchModel.Source();
            if (template && torchIds.ContainsKey(template.gameObject)) template = null;
            if (!template)
                foreach (var candidate in Resources.FindObjectsOfTypeAll<FlashLight>())
                    if (candidate.gameObject.scene.IsValid() && !torchIds.ContainsKey(candidate.gameObject)) { template = candidate; break; }
            if (!template) return null;
            var copy = UnityEngine.Object.Instantiate(template.gameObject);
            copies.Add(copy);
            copy.name = "Partner's flashlight";
            copy.transform.SetParent(null, true);
            copy.transform.localScale = template.transform.lossyScale;
            copy.tag = "Flashlight";
            var light = copy.GetComponent<FlashLight>();
            if (light) { light.inHand = false; light.ForceTurnOff(); }
            var body = copy.GetComponent<Rigidbody>() ?? copy.AddComponent<Rigidbody>();
            body.isKinematic = true;
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) collider.enabled = true;
            copy.SetActive(true);
            return copy;
        }

        void ReceiveTorch(Packet packet)
        {
            if (string.IsNullOrEmpty(packet.name)) return;
            torches.TryGetValue(packet.name, out var torch);
            switch (packet.code)
            {
                case "torch-drop":
                case "torch-pose":
                    if (!torch)
                    {
                        torch = MakeTorchCopy();
                        if (!torch) return;
                        torches[packet.name] = torch; torchIds[torch] = packet.name;
                        torch.transform.SetPositionAndRotation(packet.position, packet.rotation.normalized);
                        var shown = torch.GetComponentsInChildren<Renderer>(true);
                        Plugin.Log("TORCH_COPY " + packet.name + " at=" + packet.position.ToString("F2") + " layer=" + LayerMask.LayerToName(torch.layer) + " renderers=" + shown.Length + " enabledActive=" + System.Array.FindAll(shown, r => r.enabled && r.gameObject.activeInHierarchy).Length + " scale=" + torch.transform.lossyScale.ToString("F2"));
                    }
                    if (torch == heldTorch) return;
                    if (torch.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
                    if (!torch.activeSelf) { torch.SetActive(true); torch.transform.SetPositionAndRotation(packet.position, packet.rotation.normalized); }
                    glides[packet.name] = new Glide { Position = packet.position, Rotation = packet.rotation.normalized };
                    break;
                case "torch-take":
                    if (!torch) return;
                    glides.Remove(packet.name);
                    if (torch == heldTorch)
                    {
                        // Both grabbed it at once: the host keeps it.
                        if (plugin.IsHost) return;
                        var pickup = GameManager.instance.itemPickUp;
                        var light = torch.GetComponent<FlashLight>();
                        if (light) { light.inHand = false; light.ForceTurnOff(); }
                        pickup.inHandFlashlight = false;
                        heldTorch = null;
                        GameManager.instance.uIManager.controlsText.text = "";
                    }
                    torch.transform.SetParent(null, true);
                    torch.SetActive(false);
                    Plugin.Log("TORCH_TAKEN_BY_PARTNER " + packet.name);
                    break;
            }
        }

        // ---- spray, matches ----

        void ReceiveSpray(bool on)
        {
            var visual = plugin.PartnerHeldVisual;
            if (!visual) return;
            var particles = visual.GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length == 0 && on) particles = AddSprayParticles(visual);
            foreach (var ps in particles) { if (on) { ps.gameObject.SetActive(true); ps.Play(true); } else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            foreach (var audio in visual.GetComponentsInChildren<AudioSource>(true))
            {
                if (on && audio.clip && !audio.isPlaying) audio.Play();
                else if (!on) audio.Stop();
            }
            remoteSpray = on ? visual : null;
            if (on) { SpraysMirrored++; AimRemoteSpray(); }
            Plugin.Log("SPRAY_REMOTE " + on + " particles=" + particles.Length);
            if (on) foreach (var ps in particles) { var sh = ps.shape; var main = ps.main; var vel = ps.velocityOverLifetime; var force = ps.forceOverLifetime; Plugin.Log("SPRAY_PS " + ps.name + " shape=" + (sh.enabled ? sh.shapeType + "/rot" + sh.rotation + "/pos" + sh.position : "off") + " speed=" + main.startSpeed.constant + " space=" + main.simulationSpace + " gravity=" + main.gravityModifier.constant + " vel=" + (vel.enabled ? vel.x.constant + "," + vel.y.constant + "," + vel.z.constant + "/" + vel.space : "off") + " force=" + (force.enabled ? force.x.constant + "," + force.y.constant + "," + force.z.constant : "off") + " worldRot=" + ps.transform.rotation.eulerAngles + " lossy=" + ps.transform.lossyScale); }
        }

        GameObject remoteSpray;
        float nextSprayLog;

        // The cloud leaves the nozzle of the can in the partner's hand. The can's own cloud is placed for the first-person
        // view (relative to the camera, not the can), which put it beside the partner; it is moved onto the can here
        // every frame, flying the way the can points.
        void AimRemoteSpray()
        {
            var visual = remoteSpray;
            if (!visual) return;
            var bounds = new Bounds(); bool any = false;
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
                if (!any) { bounds = renderer.bounds; any = true; } else bounds.Encapsulate(renderer.bounds);
            if (!any) return;
            var look = plugin.PartnerLook;
            var forward = look * Vector3.forward;
            var nozzle = bounds.center + forward * Mathf.Max(0.05f, Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z))).magnitude);
            if (plugin.Lab && Time.unscaledTime >= nextSprayLog) { nextSprayLog = Time.unscaledTime + 1f; Plugin.Log("SPRAY_AIM nozzle=" + nozzle.ToString("F2") + " forward=" + forward.ToString("F2") + " can=" + bounds.center.ToString("F2") + " size=" + bounds.size.ToString("F2")); }
            foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform.parent && ps.transform.parent.GetComponentInParent<ParticleSystem>()) continue;
                ps.transform.position = nozzle;
            }
        }

        // The can in the partner's hand came without the spray cloud: borrow the cloud from a can in our world.
        static ParticleSystem[] AddSprayParticles(GameObject visual)
        {
            foreach (var can in Resources.FindObjectsOfTypeAll<BearSpray>())
            {
                if (!can.sprayPS || !can.gameObject.scene.IsValid()) continue;
                var cloud = UnityEngine.Object.Instantiate(can.sprayPS.gameObject, visual.transform);
                cloud.transform.localPosition = can.transform.InverseTransformPoint(can.sprayPS.transform.position);
                cloud.transform.localRotation = Quaternion.Inverse(can.transform.rotation) * can.sprayPS.transform.rotation;
                if (can.sprayAS && can.sprayAS.clip)
                {
                    var hiss = cloud.AddComponent<AudioSource>();
                    hiss.clip = can.sprayAS.clip; hiss.loop = can.sprayAS.loop; hiss.volume = can.sprayAS.volume;
                    hiss.spatialBlend = 1f; hiss.rolloffMode = AudioRolloffMode.Linear; hiss.minDistance = 1.5f; hiss.maxDistance = 15f; hiss.playOnAwake = false;
                }
                return cloud.GetComponentsInChildren<ParticleSystem>(true);
            }
            return new ParticleSystem[0];
        }


        // The stream from the partner's gas can (the can's own particles, which in our world wet nothing).
        void ReceivePour(bool on)
        {
            var visual = plugin.PartnerHeldVisual;
            if (!visual) return;
            foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true)) { if (on) { ps.gameObject.SetActive(true); ps.Play(true); } else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            foreach (var audio in visual.GetComponentsInChildren<AudioSource>(true)) { if (on && audio.clip && !audio.isPlaying) audio.Play(); else if (!on) audio.Stop(); }
            if (on) PoursMirrored++;
            Plugin.Log("POUR_REMOTE " + on);
        }
        public int PoursMirrored { get; private set; }
        void ReceiveMatchbox(bool open)
        {
            var visual = plugin.PartnerHeldVisual;
            var source = Resources.FindObjectsOfTypeAll<Matchbox>();
            if (!visual || source.Length == 0 || !source[0].innerBox) return;
            foreach (var child in visual.GetComponentsInChildren<Transform>(true))
                if (child.name == source[0].innerBox.name) { child.DOLocalMoveZ(open ? -0.67f : 0f, 0.3f); break; }
        }

        internal void LocalMatch(MatchStick match)
        {
            if (!plugin.Ready || Mirroring || !match) return;
            var camera = GameManager.instance.fpCamera ? GameManager.instance.fpCamera.transform : match.transform;
            var force = camera.forward * 150f + camera.right * -50f + camera.up * 50f;
            plugin.Send(new Packet { kind = "fx", code = "match", scene = SceneManager.GetActiveScene().name, position = match.transform.position, rotation = match.transform.rotation, remotePosition = force });
        }

        void ReceiveMatch(Packet packet)
        {
            Matchbox box = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Matchbox>()) if (candidate.matchStickPrefab && candidate.gameObject.scene.IsValid()) { box = candidate; break; }
            if (!box) return;
            Mirroring = true;
            try
            {
                var match = UnityEngine.Object.Instantiate(box.matchStickPrefab, packet.position, packet.rotation.normalized);
                var stick = match.GetComponent<MatchStick>();
                mirroredMatches.Add(match.GetInstanceID());
                if (stick.flameAS) { stick.flameAS.spatialBlend = 1f; stick.flameAS.Play(); }
                stick.flameFx.SetActive(true);
                match.transform.parent = null;
                var body = match.AddComponent<Rigidbody>();
                body.AddForce(packet.remotePosition);
                stick.StartCoroutine(stick.FlameEnd());
                MatchesMirrored++;
            }
            finally { Mirroring = false; }
        }

        internal static bool IsMirroredMatch(MatchStick match) => match && mirroredMatches.Contains(match.gameObject.GetInstanceID());

        public void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name) return;
            switch (packet.code)
            {
                case "spray": ReceiveSpray(packet.active); break;
                case "matchbox": ReceiveMatchbox(packet.active); break;
                case "pour": ReceivePour(packet.active); break;
                case "match": ReceiveMatch(packet); break;
                default: if (packet.code != null && packet.code.StartsWith("torch-")) ReceiveTorch(packet); break;
            }
        }
    }

    // Lab only: the mouse button "held" on the bear spray (the can stops spraying the moment the button is up).
    [HarmonyPatch(typeof(BearSpray), "Update")]
    internal static class LabSprayHoldPatch
    {
        internal static bool Held;
        static bool Prefix() => !Held;
    }

    // A struck match starts burning out right after it is thrown: that is when it is sent.
    [HarmonyPatch(typeof(MatchStick), nameof(MatchStick.FlameEnd))]
    internal static class MatchStruckPatch
    {
        static void Prefix(MatchStick __instance) => Plugin.Instance?.Hands.LocalMatch(__instance);
    }

    // The partner's match is a picture of theirs: it does not light our stove (their lit stove reaches us as story state).
    [HarmonyPatch(typeof(MatchStick), "OnCollisionEnter")]
    internal static class MirroredMatchPatch
    {
        static bool Prefix(MatchStick __instance) => !HandEffects.IsMirroredMatch(__instance);
    }
}
