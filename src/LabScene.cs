using System;
using System.Collections;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace IronbarkCoop
{
    // Lab-only scene inspection and pokes for the bridge (never used without -coop-bridge).
    internal static class LabScene
    {
        static JObject V(Vector3 v) => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
        static JObject At(Component c) => c ? V(c.transform.position) : null;
        static JObject At(GameObject g) => g ? V(g.transform.position) : null;

        // Positions the First Scene tests click or walk to.
        public static void DumpFirstScene(string path)
        {
            var gm = GameManager.instance;
            var o = new JObject();
            var diner = gm ? gm.dinerManager : null;
            var rv = gm ? gm.rVController : null;
            if (diner)
            {
                o["dinerDoor"] = At(diner.dinerDoor);
                o["playerInside"] = At(diner.playerInside);
                o["playerOutside"] = At(diner.playerOutside);
                o["washroomDoor"] = At(diner.washroomDoorCollider);
                o["peeTrigger"] = At(diner.peeTrigger);
                o["waitress"] = At(diner.waitress);
                o["badGuy"] = At(diner.badGuy);
                var booths = new JArray();
                for (int i = 0; i < diner.booths.Length; i++)
                {
                    var b = diner.booths[i];
                    booths.Add(new JObject { ["i"] = i, ["seat"] = At(b.seatCollider), ["rig"] = At(b.playerInSeat), ["rigYaw"] = b.playerInSeat.transform.eulerAngles.y,
                        ["camera"] = At(b.camera), ["tray"] = At(b.foodTray), ["waitressHere"] = At(b.waitressHere), ["center"] = V(DinerSync.TableCenter(b)) });
                }
                o["booths"] = booths;
            }
            if (rv)
            {
                o["rv"] = At(rv);
                o["rvDoor"] = At(rv.doorCollider);
                o["driverSeat"] = At(rv.driverSeatCollider);
                o["radio"] = At(rv.radio);
                o["bathroomDoor"] = At(rv.bathroomDoor);
                o["passengerSeat"] = V(Vehicles.PassengerEye(rv) - rv.transform.up * 0.45f);
                o["driverEye"] = V(Vehicles.DriverEye(rv));
                o["fp"] = At(rv.fPTransform);
                o["bag"] = At(rv.bag);
            }
            var jukeboxes = new JArray();
            foreach (var j in UnityEngine.Object.FindObjectsOfType<JukeBox>()) jukeboxes.Add(new JObject { ["path"] = ItemSync.PathOf(j.transform), ["at"] = At(j) });
            o["jukeboxes"] = jukeboxes;
            var radios = new JArray();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Radio>()) radios.Add(new JObject { ["path"] = ItemSync.PathOf(r.transform), ["at"] = At(r) });
            o["radios"] = radios;
            File.WriteAllText(path, o.ToString());
        }

        public static void DumpAudio(string path)
        {
            var list = new JArray();
            foreach (var a in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (!a.gameObject.scene.IsValid() || !a.clip) continue;
                var components = new JArray();
                foreach (var c in a.GetComponents<MonoBehaviour>()) if (c) components.Add(c.GetType().Name);
                list.Add(new JObject { ["path"] = ItemSync.PathOf(a.transform), ["clip"] = a.clip.name, ["len"] = a.clip.length, ["playing"] = a.isPlaying, ["time"] = a.time,
                    ["loop"] = a.loop, ["awake"] = a.playOnAwake, ["blend"] = a.spatialBlend, ["max"] = a.maxDistance, ["at"] = V(a.transform.position), ["scripts"] = components });
            }
            File.WriteAllText(path, list.ToString());
        }

        public static void DumpCameras(string path)
        {
            var cams = new JArray();
            foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (!cam.gameObject.scene.IsValid()) continue;
                var layers = new JArray();
                for (int i = 0; i < 32; i++) if ((cam.cullingMask & (1 << i)) != 0) layers.Add(i + ":" + LayerMask.LayerToName(i));
                cams.Add(new JObject { ["path"] = ItemSync.PathOf(cam.transform), ["active"] = cam.isActiveAndEnabled, ["depth"] = cam.depth, ["clear"] = cam.clearFlags.ToString(),
                    ["main"] = cam == Camera.main, ["layers"] = layers });
            }
            var rv = GameManager.instance ? GameManager.instance.rVController : null;
            var rvLayers = new JObject();
            if (rv) foreach (var r in rv.GetComponentsInChildren<Renderer>(true)) { string key = r.gameObject.layer + ":" + LayerMask.LayerToName(r.gameObject.layer); rvLayers[key] = (int?)rvLayers[key] + 1 ?? 1; }
            File.WriteAllText(path, new JObject { ["cameras"] = cams, ["rvRendererLayers"] = rvLayers }.ToString());
        }

        // "Type.Method" on the first live object of that type (or the object at a path); coroutines are started.
        public static string Invoke(string code, string path, string argument = null)
        {
            int dot = code.IndexOf('.');
            var type = AccessTools.TypeByName(code.Substring(0, dot));
            var method = type != null ? AccessTools.Method(type, code.Substring(dot + 1)) : null;
            if (method == null) return "no method";
            var target = !string.IsNullOrEmpty(path) ? ItemSync.FindPath(path)?.GetComponent(type) as MonoBehaviour : UnityEngine.Object.FindObjectOfType(type) as MonoBehaviour;
            if (!target) return "no target";
            var arguments = new object[method.GetParameters().Length];
            if (arguments.Length == 1 && argument != null && method.GetParameters()[0].ParameterType == typeof(string)) arguments[0] = argument;
            var result = method.Invoke(target, arguments);
            if (result is IEnumerator routine) target.StartCoroutine(routine);
            return "ok";
        }

        // Wet patches as poured gas leaves them (the stream's collision sub-emitter): emitted into the first active can's
        // "wet" system on the floor around the player.
        public static string Wet(int count)
        {
            var gas = UnityEngine.Object.FindObjectOfType<Gas>();
            var player = GameManager.instance && GameManager.instance.playerFP ? GameManager.instance.playerFP.transform : null;
            if (!gas || !gas.gasPsWet || !player) return "no can";
            if (!Physics.Raycast(player.position, Vector3.down, out var floor, 5f, ~0, QueryTriggerInteraction.Ignore)) return "no floor";
            for (int i = 0; i < Math.Max(1, count); i++)
                gas.gasPsWet.Emit(new ParticleSystem.EmitParams { position = floor.point + Vector3.up * 0.01f + new Vector3(Mathf.Sin(i) * 0.6f, 0f, Mathf.Cos(i) * 0.6f), startSize = 0.3f, startLifetime = 5000000f, applyShapeToPosition = false }, 1);
            return "ok " + gas.gasPsWet.particleCount;
        }

        // "Type.field=value" on the first live object of that type.
        public static string Set(string code, string value)
        {
            int dot = code.IndexOf('.');
            var type = AccessTools.TypeByName(code.Substring(0, dot));
            var field = type != null ? AccessTools.Field(type, code.Substring(dot + 1)) : null;
            var target = type != null ? UnityEngine.Object.FindObjectOfType(type) : null;
            if (field == null || !target) return "missing";
            object parsed = field.FieldType == typeof(bool) ? (object)(value == "1" || value == "true")
                : field.FieldType == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture)
                : field.FieldType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture)
                : field.FieldType.IsEnum ? Enum.Parse(field.FieldType, value) : (object)value;
            field.SetValue(target, parsed);
            return "ok";
        }

        // The game's dialogue canvas: hierarchy, components, fonts and colours (to style our own panels like it).
        public static void DumpDialogue(string path)
        {
            var ui = GameManager.instance ? GameManager.instance.uIManager : null;
            var list = new JArray();
            var roots = new System.Collections.Generic.List<Transform>();
            if (path.EndsWith("computer.json")) { if (ui && ui.computerUI) roots.Add(ui.computerUI.transform); if (ui && ui.computerManager) roots.Add(ui.computerManager.transform); }
            else
            {
                if (ui && ui.dialogueCanvas) roots.Add(ui.dialogueCanvas.transform);
                if (ui && ui.dialogueRunner) roots.Add(ui.dialogueRunner.transform);
            }
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var entry = new JObject { ["path"] = ItemSync.PathOf(t), ["active"] = t.gameObject.activeSelf };
                    var types = new JArray();
                    foreach (var c in t.GetComponents<Component>()) if (c) types.Add(c.GetType().Name);
                    entry["components"] = types;
                    if (t is RectTransform rt) entry["rect"] = $"anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot} pos={rt.anchoredPosition} size={rt.sizeDelta}";
                    if (t.TryGetComponent<TMPro.TMP_Text>(out var text)) entry["text"] = $"font={(text.font ? text.font.name : "-")} size={text.fontSize} color={text.color} style={text.fontStyle} align={text.alignment} value={text.text}";
                    if (t.TryGetComponent<UnityEngine.UI.Image>(out var image)) entry["image"] = $"sprite={(image.sprite ? image.sprite.name : "-")} color={image.color} type={image.type}";
                    if (t.TryGetComponent<Canvas>(out var canvas)) entry["canvas"] = $"mode={canvas.renderMode} order={canvas.sortingOrder}";
                    if (t.TryGetComponent<UnityEngine.UI.CanvasScaler>(out var scaler)) entry["scaler"] = $"mode={scaler.uiScaleMode} ref={scaler.referenceResolution} match={scaler.matchWidthOrHeight}";
                    list.Add(entry);
                }
            if (ui && ui.dialogueRunner)
            {
                var views = new JArray();
                foreach (var view in ui.dialogueRunner.dialogueViews) if (view) views.Add(view.GetType().FullName + " @ " + ItemSync.PathOf(view.transform));
                list.Add(new JObject { ["views"] = views });
            }
            File.WriteAllText(path, list.ToString());
        }


        // Everything drawn or solid in the RV, in the RV's own frame (x right, y up, z forward), to find the seats.
        public static void DumpRv(string path)
        {
            var rv = GameManager.instance ? GameManager.instance.rVController : null;
            var list = new JArray();
            if (rv)
            {
                var root = rv.transform;
                string Local(Vector3 world) { var v = root.InverseTransformPoint(world); return v.x.ToString("F2", CultureInfo.InvariantCulture) + ";" + v.y.ToString("F2", CultureInfo.InvariantCulture) + ";" + v.z.ToString("F2", CultureInfo.InvariantCulture); }
                list.Add(new JObject { ["what"] = "eyes", ["driverEye"] = Local(Vehicles.DriverEye(rv)), ["passengerEye"] = Local(Vehicles.PassengerEye(rv)),
                    ["driverSeatCollider"] = rv.driverSeatCollider ? Local(rv.driverSeatCollider.transform.position) : "-", ["wheel"] = rv.steeringHandle ? Local(rv.steeringHandle.position) : "-",
                    ["fp"] = rv.fPTransform ? Local(rv.fPTransform.position) : "-", ["rootScale"] = root.lossyScale.ToString("F3") });
                foreach (var renderer in rv.GetComponentsInChildren<Renderer>(true))
                    list.Add(new JObject { ["r"] = ItemSync.PathOf(renderer.transform).Replace(ItemSync.PathOf(root), ""), ["c"] = Local(renderer.bounds.center), ["s"] = renderer.bounds.size.ToString("F2"), ["on"] = renderer.enabled && renderer.gameObject.activeInHierarchy });
                foreach (var collider in rv.GetComponentsInChildren<Collider>(true))
                    list.Add(new JObject { ["k"] = ItemSync.PathOf(collider.transform).Replace(ItemSync.PathOf(root), ""), ["c"] = Local(collider.bounds.center), ["s"] = collider.bounds.size.ToString("F2"), ["trigger"] = collider.isTrigger });
            }
            File.WriteAllText(path, list.ToString());
        }

        // The tower bed: its models and colliders, and where the game puts the sleeping player's camera.
        public static void DumpBed(string path)
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            var list = new JArray();
            if (tower && tower.bed)
            {
                var bed = tower.bed.transform;
                list.Add(new JObject { ["bed"] = V(bed.position), ["bedForward"] = V(bed.forward), ["bedRight"] = V(bed.right), ["bedScale"] = V(bed.lossyScale),
                    ["playerInBed"] = tower.playerInBed ? V(tower.playerInBed.transform.position) : null, ["playerInBedFwd"] = tower.playerInBed ? V(tower.playerInBed.transform.forward) : null,
                    ["bedCam"] = tower.bedCam ? V(tower.bedCam.transform.position) : null, ["bedCamFwd"] = tower.bedCam ? V(tower.bedCam.transform.forward) : null,
                    ["bedCamPath"] = tower.bedCam ? ItemSync.PathOf(tower.bedCam.transform) : "" });
                var root = bed.parent ? bed.parent : bed;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if ((renderer.bounds.center - bed.position).magnitude < 3f)
                        list.Add(new JObject { ["r"] = ItemSync.PathOf(renderer.transform), ["c"] = V(renderer.bounds.center), ["s"] = V(renderer.bounds.size) });
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    if ((collider.bounds.center - bed.position).magnitude < 3f)
                        list.Add(new JObject { ["k"] = ItemSync.PathOf(collider.transform), ["c"] = V(collider.bounds.center), ["s"] = V(collider.bounds.size) });
            }
            File.WriteAllText(path, list.ToString());
        }

        // A GameManager object's hierarchy with every component and whether it is on.
        public static void DumpObject(string path, string field)
        {
            var gm = GameManager.instance;
            var value = gm ? AccessTools.Field(typeof(GameManager), field)?.GetValue(gm) : null;
            var root = value is GameObject go ? go.transform : value is Component c ? c.transform : null;
            var lines = new System.Collections.Generic.List<string>();
            if (root)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var component in t.GetComponents<Component>())
                        if (component) parts.Add(component.GetType().Name + (component is Behaviour b && !b.enabled ? "(off)" : ""));
                    lines.Add(ItemSync.PathOf(t) + (t.gameObject.activeInHierarchy ? "" : " [inactive]") + " : " + string.Join(", ", parts));
                }
            File.WriteAllLines(path, lines);
        }
        // Compact First Scene facts for state.json.
        public static string Facts(Plugin plugin)
        {
            var gm = GameManager.instance;
            if (!gm) return "";
            var diner = gm.dinerManager;
            var rv = gm.rVController;
            string s = "";
            if (diner)
            {
                s += " diner=" + diner.dinerState + " booth=" + (diner.activeBooth ? Array.IndexOf(diner.booths, diner.activeBooth) : -1)
                    + " original=" + (diner.originalBooth ? Array.IndexOf(diner.booths, diner.originalBooth) : -1)
                    + " intro=" + diner.waitressIntroDone + " ordered=" + diner.waitressOrderTaken + " peed=" + diner.playerPeed + " locked=" + diner.playerLockedToSeat
                    + " washroomOpen=" + diner.washroomDoorOpen + " seatsOn=" + (diner.booths.Length > 0 && diner.booths[0].seatCollider.activeSelf)
                    + " order=" + diner.foodOrder.ToString(CultureInfo.InvariantCulture) + "/" + diner.drinkOrder.ToString(CultureInfo.InvariantCulture) + " tray2=" + plugin.Diner.TrayDebug + " opposite=" + plugin.Diner.SeatedOpposite + " ate=" + (diner.activeBooth && diner.activeBooth.foodTray ? diner.activeBooth.foodTray.mainEaten + "," + diner.activeBooth.foodTray.sideEaten + "," + diner.activeBooth.foodTray.drinkDrunk : "-") + " armW=" + (diner.waitress && diner.waitress.arm ? diner.waitress.arm.weight.ToString("F1") : "-");
                if (diner.waitress) s += " waitress=" + diner.waitress.transform.position.ToString("F1") + " wstate=" + diner.waitress.state + " wanim=" + (diner.waitress.animator ? diner.waitress.animator.GetInteger("State") : -1) + " orderTray=" + (diner.waitress.orderTray && diner.waitress.orderTray.activeSelf);
            }
            if (rv)
            {
                s += " rvDoorOpen=" + rv.isDoorOpen + " rvDoorYaw=" + rv.door.localEulerAngles.y.ToString("F0") + " bathOpen=" + rv.isBathroomDoorOpen + " jammed=" + rv.bathroomDoorJammed
                    + " radioOn=" + (rv.radio && !rv.radio.isStopped) + " radioClip=" + (rv.radio ? rv.radio.currentClip : -1) + " radioTime=" + (rv.radio && rv.radio.audioSource ? rv.radio.audioSource.time.ToString("F1") : "-")
                    + " lights=" + (rv.headLight1 && rv.headLight1.activeSelf) + " wheel=" + (rv.steeringHandle ? rv.steeringHandle.localEulerAngles.z.ToString("F0") : "-") + " rvState=" + rv.rvState;
                // The team's bag lying in the RV: fixed to it and out of the RV's own collisions.
                var bag = GameObject.FindGameObjectWithTag("Bag");
                if (bag && bag.transform.IsChildOf(rv.transform) && !(gm.playerFP && bag.transform.IsChildOf(gm.playerFP.transform)))
                {
                    var bagBody = bag.GetComponent<Rigidbody>();
                    int pairs = 0, ignored = 0;
                    foreach (var mine in bag.GetComponentsInChildren<Collider>())
                        foreach (var other in rv.GetComponentsInChildren<Collider>())
                            if (!other.transform.IsChildOf(bag.transform) && other.enabled && !other.isTrigger) { pairs++; if (Physics.GetIgnoreCollision(mine, other)) ignored++; }
                    s += " bagInRv=" + (bagBody && bagBody.isKinematic ? "fixed" : "loose") + "/" + ignored + "of" + pairs;
                }
                else s += " bagInRv=no";
                s += " bagHits=" + LabRvContacts.Hits + "/" + LabRvContacts.Impulse.ToString("F0", CultureInfo.InvariantCulture);
            }
            var trail = gm.trailStartManager;
            if (trail && trail.parkRanger)
                s += " ranger=" + trail.parkRanger.transform.position.ToString("F1", System.Globalization.CultureInfo.InvariantCulture).Replace(" ", "") + " rstate=" + trail.parkRanger.state + " guardDoor=" + trail.guardHouseDoorOpen
                    + " flashlight=" + (gm.itemPickUp && gm.itemPickUp.inHandFlashlight) + " flashGiven=" + trail.flashLightRecieved + " trailState=" + trail.state;
            foreach (var j in UnityEngine.Object.FindObjectsOfType<JukeBox>())
            {
                var source = AccessTools.Field(typeof(JukeBox), "AS").GetValue(j) as AudioSource;
                s += " jukebox=" + AccessTools.Field(typeof(JukeBox), "songIndex").GetValue(j) + "@" + (source ? source.time.ToString("F1") + (source.isPlaying ? "" : "(stopped)") : "-");
            }
            if (trail) s += " jsDone=" + trail.jsDone + " keysGiven=" + trail.KeysRecieved + " partnerScare=" + plugin.Trail.PartnerScarePending;
            var ui = gm.uIManager;
            if (ui && ui.objectsParent && ui.keys) s += " keyIcon=" + (ui.objectsParent.activeInHierarchy && ui.keys.gameObject.activeInHierarchy);
            var tower = gm.watchTowerManager;
            if (tower)
            {
                var pc = ui ? ui.computerManager : null;
                s += " tstate=" + tower.state + " stoveOpen=" + tower.isStoveOpen + " woodCheck=" + (tower.woodCheck && tower.woodCheck.enabled) + " inside=" + tower.isPlayerInside + " inRoom=" + RadioTalk.InTowerRoom(tower) + " chairTaken=" + TowerSync.ChairTaken + " powerCuts=" + TowerSync.PowerCuts + " genOn=" + tower.isGeneratorOn
                    + " genTag=" + (tower.generator ? tower.generator.tag : "-") + " towerRadio=" + (tower.radio && !tower.radio.isStopped) + " locked=" + tower.isLocked
                    + " pcPassword=" + (pc && pc.passwordEntered) + " pcUpdated=" + (pc && pc.consoleManager && pc.consoleManager.hasUpdated)
                    + " shots=" + plugin.Tower.ShotsSent + "/" + plugin.Tower.ShotsReceived + " pcScreen=" + (pc && pc.screen && pc.screen.activeSelf)
                    + " pcBoot=" + (pc && pc.bootUpDone) + " pcMap=" + (pc && pc.mapViewerWindow && pc.mapViewerWindow.activeSelf) + " onPc=" + gm.onComputer;
            }
            s += " torchAt=" + plugin.Hands.FirstTorchAt;
            s += " teamBag=" + ItemSync.TeamBag + " hasBag=" + (gm.itemPickUp && gm.itemPickUp.inHandBag) + " torches=" + plugin.Hands.TorchesInWorld + " matchesSeen=" + plugin.Hands.MatchesMirrored + " spraysSeen=" + plugin.Hands.SpraysMirrored
                + " headToEye=" + plugin.PartnerHeadToEye.ToString("F2", CultureInfo.InvariantCulture) + " partnerSeat=" + (plugin.PartnerSeat ?? "-") + " held=" + (plugin.PartnerHeldVisual ? plugin.PartnerHeldVisual.name.Replace(' ', '_') : "-")
                + " talk=" + plugin.Talk.Debug;
            if (gm.watchTowerManager) { var t3 = gm.watchTowerManager; string F3(string n) => (bool)AccessTools.Field(typeof(WatchTowerManager), n).GetValue(t3) ? "1" : "0"; s += " d3=U" + F3("seq3StandUp1Done") + "C" + F3("seq3CampersDone") + "K" + F3("seq3CheckSmoke") + "V" + F3("seq3StandUp2Done") + "X" + F3("seq3CheckCampersDone") + "S" + (t3.smoke && t3.smoke.activeSelf ? "1" : "0") + "T" + (t3.laceyTrailTrigger && t3.laceyTrailTrigger.activeSelf ? "1" : "0"); }
            if (gm.watchTowerManager) { var t = gm.watchTowerManager; string B(bool b) => b ? "1" : "0"; s += " n1=L" + B(t.isLocked) + "D" + B(t.isDoorOpen) + "G" + B(t.isGeneratorOn) + "R" + B(t.reportDone) + "C" + B(t.firstRadioConversationDone) + "S" + B((bool)AccessTools.Field(typeof(WatchTowerManager), "stoveConvoDone").GetValue(t)) + "W" + B(t.firewoodConvoOnce) + "P" + B(t.canPickUpWood) + "I" + B(t.woodInStove) + "g" + B(t.gasOnWood) + "F" + B(t.fireLit) + "A" + B(t.askAboutReport) + "U" + B(t.seq1UpstairsBlocker && t.seq1UpstairsBlocker.activeSelf) + "b" + System.Array.FindAll(t.towerSunBlinds, x => x.isOpen).Length; }
            if (gm.watchTowerManager && gm.watchTowerManager.woodStorage) s += " woodPile=" + (gm.watchTowerManager.woodStorage.TryGetComponent<Collider>(out var pile) && pile.enabled) + "/" + gm.watchTowerManager.canPickUpWood;
            if (gm.watchTowerManager && gm.watchTowerManager.casserole) { var dish = gm.watchTowerManager.casserole; s += " dish=" + dish.ingredientsIn + "/" + dish.finishedInt + "/" + (dish.isPickable ? 1 : 0) + "/" + (SupperSync.Baked(dish) ? 1 : 0); }
            if (gm.watchTowerManager && gm.watchTowerManager.peeStairsBlock) s += " stairBlock=" + gm.watchTowerManager.peeStairsBlock.activeSelf + " shedLight=" + (gm.watchTowerManager.shedLight && gm.watchTowerManager.shedLight.activeSelf);
            s += " cultist=" + plugin.Night5.Debug + " cultist8=" + plugin.Night8.Debug + " night6=" + plugin.Night6.Debug + " perchGap=" + plugin.PartnerPerchGap.ToString("F2", CultureInfo.InvariantCulture) + " errand=" + plugin.Errand.Debug + " seqPeed=" + (gm.watchTowerManager ? gm.watchTowerManager.seqPeed.ToString() : "-") + " supper=" + SupperSync.Debug + " trip=" + plugin.Trip.Debug + " chores=" + plugin.Trip.ChoresDebug + " gasTrail=" + plugin.Hands.TrailPatches + "/" + plugin.Hands.GasPatchesReceived + " potty=" + OuthouseSync.Sent + "/" + OuthouseSync.Applied + " hipsGap=" + plugin.PartnerHipsGap.ToString("F2", CultureInfo.InvariantCulture) + " dreamLight=" + plugin.Dream.LightDebug + " dreamRadius=" + plugin.Dream.Radius.ToString("F0", CultureInfo.InvariantCulture) + " echoes=" + plugin.Dream.Echoes
                + " voiceGain=" + plugin.VoiceGain.ToString("F2", CultureInfo.InvariantCulture) + " partnerDreaming=" + plugin.PartnerDreaming;
            s += " dream=" + plugin.Dream.Debug + " sleep=" + plugin.Sleep.Debug + " bedHalf=" + plugin.Beds.Shifted + " partnerLying=" + plugin.PartnerLying + " partnerTorch=" + plugin.PartnerTorch + " heldTorchScale=" + plugin.Hands.HeldTorchScale;
            if (Camera.main) s += " camEuler=" + Camera.main.transform.localEulerAngles.ToString("F0").Replace(" ", "");
            s += " calls=" + RemoteCalls.Sent + "/" + RemoteCalls.Applied + " musicFixes=" + plugin.Music.Corrections + " lead=" + plugin.IsStoryLead;
            return s;
        }
    }
}

namespace IronbarkCoop
{
    // Lab only: every contact the RV's body has with the team's bag, and how hard it pushed.
    internal sealed class LabRvContacts : MonoBehaviour
    {
        internal static int Hits;
        internal static float Impulse;
        void OnCollisionEnter(Collision collision) => Count(collision);
        void OnCollisionStay(Collision collision) => Count(collision);
        static void Count(Collision collision)
        {
            if (!collision.collider || !collision.collider.transform.root.CompareTag("Bag") && !collision.collider.CompareTag("Bag") && !(collision.collider.attachedRigidbody && collision.collider.attachedRigidbody.CompareTag("Bag"))) return;
            Hits++;
            Impulse += collision.impulse.magnitude;
        }
    }
}
