using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Where a player whose walking body the game has hidden is sitting, so the partner sees them there.
    internal static class Seats
    {
        public const float BoothEyeHeight = 1.2f;
        const float StandingEyeHeight = 1.62f;

        public static void DescribeInactive(Packet packet, Vector3 lastFeet, bool hasLastFeet)
        {
            var gm = GameManager.instance;
            var diner = gm ? gm.dinerManager : null;
            if (diner && diner.dinerState == DinerManager.DinerState.PlayerSeated && diner.activeBooth)
            {
                var booth = diner.activeBooth;
                var eye = booth.camera ? booth.camera.transform : booth.movingCam ? booth.movingCam.transform : null;
                if (!eye) return;
                packet.active = true;
                packet.sit = true;
                packet.seat = "booth" + Array.IndexOf(diner.booths, booth);
                packet.position = DinerSync.SeatEye(booth) - Vector3.up * BoothEyeHeight;
                packet.eye = DinerSync.SeatEye(booth);
                packet.rotation = Quaternion.Euler(0f, eye.eulerAngles.y, 0f);
                packet.pitch = Mathf.DeltaAngle(0f, eye.eulerAngles.x);
                packet.steer = DinerSync.SeatYaw(booth);
                return;
            }
            var camera = Camera.main;
            var tower = gm ? gm.watchTowerManager : null;
            // Supper on the bed (night 4): sitting with the tray on the lap, not lying (playtest report).
            if (camera && tower && tower.playerInBedEating && tower.playerInBedEating.activeInHierarchy)
            {
                var eye = camera.transform;
                // The body faces the way the eating rig sits; looking around turns only the head.
                packet.active = true;
                packet.sit = true;
                packet.seat = "bed-eat";
                packet.eye = eye.position;
                packet.position = eye.position - Vector3.up * BoothEyeHeight;
                packet.rotation = Quaternion.Euler(0f, eye.eulerAngles.y, 0f);
                packet.pitch = Mathf.DeltaAngle(0f, eye.eulerAngles.x);
                packet.steer = tower.playerInBedEating.transform.eulerAngles.y;
                return;
            }
            // In the tower bed: lying, head on the pillow where the bed camera rests, feet toward the foot of the bed.
            if (camera && tower && tower.bed && BedSync.InBed(tower))
            {
                var eyePoint = camera.transform.position;
                var toFeet = tower.bed.transform.position - eyePoint; toFeet.y = 0f;
                packet.active = true;
                packet.sit = true;
                packet.seat = "bed";
                packet.eye = eyePoint;
                packet.position = eyePoint - Vector3.up * 0.5f;
                packet.rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);
                packet.pitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x);
                packet.steer = toFeet.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toFeet).eulerAngles.y : tower.bed.transform.eulerAngles.y;
                latchedYaw = float.NaN;
                return;
            }
            // Hiding under the bed (night 8): hidden from the partner too (a sitting body there stuck out of the bed).
            if (tower && tower.playerUnderBed && tower.playerUnderBed.activeInHierarchy) { latchedYaw = float.NaN; return; }
            // Any other seat the game moves the camera to near the body (computer chair, washroom): sit there.
            // Seats the game moves you to from anywhere (the morning coffee chair on the balcony) count wherever you stood.
            // Taking the photo of the sunrise after the coffee is done from the same spot (its own camera rig).
            bool knownSeat = tower && ((tower.playerCoffee && tower.playerCoffee.activeInHierarchy) || (tower.pictureMode && tower.pictureMode.cameraParent && tower.pictureMode.cameraParent.activeInHierarchy));
            if (tower && tower.pictureMode && tower.pictureMode.cameraParent && tower.pictureMode.cameraParent.activeInHierarchy) { var photo = tower.pictureMode.cameraParent.GetComponentInChildren<Camera>(); if (photo) camera = photo; }
            // The morning coffee is drunk standing at the rail: shown standing there, facing where the player looks.
            if (knownSeat && camera)
            {
                var eye = camera.transform;
                var standing = eye.position - Vector3.up * StandingEyeHeight;
                if (Physics.Raycast(eye.position - Vector3.up * 0.6f, Vector3.down, out var floor, 2f, ~0, QueryTriggerInteraction.Ignore)) standing = floor.point;
                packet.active = true;
                packet.sit = false;
                packet.position = standing;
                packet.rotation = Quaternion.Euler(0f, eye.eulerAngles.y, 0f);
                packet.pitch = Mathf.DeltaAngle(0f, eye.eulerAngles.x);
                latchedYaw = float.NaN;
                return;
            }
            if (!camera || (!hasLastFeet && !knownSeat) || gm && gm.isDead) { latchedYaw = float.NaN; return; }
            var view = camera.transform;
            // At the desk the computer screen has its own camera; the seat is where the chair's camera rests.
            bool chair = TowerSync.LocalChair();
            if (chair && gm.watchTowerManager.sittingCam) view = gm.watchTowerManager.sittingCam.transform;
            if (!knownSeat && (view.position - lastFeet).sqrMagnitude > 9f) { latchedYaw = float.NaN; return; }
            // The body faces the way the seat put the camera when sitting down; looking around turns only the head.
            if (float.IsNaN(latchedYaw)) latchedYaw = view.eulerAngles.y;
            packet.active = true;
            packet.sit = true;
            packet.seat = chair ? "tower-chair" : "cam";
            var feet = view.position - Vector3.up * BoothEyeHeight;
            if (!knownSeat) feet.y = Mathf.Max(feet.y, lastFeet.y - 0.3f);
            packet.position = feet;
            packet.eye = view.position;
            packet.rotation = Quaternion.Euler(0f, view.eulerAngles.y, 0f);
            packet.pitch = Mathf.DeltaAngle(0f, view.eulerAngles.x);
            packet.steer = latchedYaw;
        }

        static float latchedYaw = float.NaN;
    }

    // The diner, Scratch Creek style: the waitress serves one table. Her whole routine runs in the lead's world (Marcus)
    // and the partner sees her walk, talk and serve exactly as the lead does; the partner can still chat with her.
    // The washroom errand belongs to the partner (Tessa); the food arrives once she is back.
    // Every table has fixed sides: Marcus sits on the booth's own bench (where the waitress serves), Tessa across from
    // him; a click on any table while Marcus sits takes Tessa to the bench across from him. Each gets a tray: the
    // waitress brings Marcus's first, then fetches a second one for Tessa; Marcus starts eating once both are served.
    internal sealed class DinerSync
    {
        readonly Plugin plugin;
        float nextSend;
        int sentBooth = -2;
        bool peePrompted, lockKicked;
        string partnerSeat;
        bool movedOpposite;
        DinerBooth movedBooth;
        // Tessa's tray: a copy of the order table's tray turned to her side, hidden until the waitress brings it.
        FoodTray partnerTray;
        DinerBooth trayBooth;
        bool secondTrip, partnerTrayServed, sentTrayServed;
        float waitHintAt;
        public bool PartnerTrayServed => partnerTrayServed;
        public string TrayDebug => (partnerTray ? "copy" : "none") + (partnerTrayServed ? "/served" : "") + (secondTrip ? "/trip" : "")
            + (partnerTray ? "/ate" + (partnerTray.mainEaten ? 1 : 0) + (partnerTray.sideEaten ? 1 : 0) + (partnerTray.drinkDrunk ? 1 : 0) + "/on" + partnerTray.gameObject.activeSelf
                + "/dishes" + Array.FindAll(new[] { partnerTray.burgerCollider, partnerTray.sandwichCollider, partnerTray.meatballSubCollider }, d => d && d.activeInHierarchy).Length : "");
        public bool SeatedOpposite => movedOpposite;
        readonly Dictionary<string, Puppet> puppets = new Dictionary<string, Puppet>();

        sealed class Puppet
        {
            public Vector3 Position; public Quaternion Rotation; public bool Has; public int AnimState = -1;
        }

        public DinerSync(Plugin plugin) { this.plugin = plugin; }

        static DinerManager Manager => GameManager.instance ? GameManager.instance.dinerManager : null;

        bool sentMeal, leadMeal, followerLocked;
        float nextBoothRepeat, lockHintAt;

        // Space at a table the meal keeps us at: say why instead of silently ignoring the key.
        void LockHint(DinerManager diner)
        {
            var gm = GameManager.instance;
            if (diner.dinerState != DinerManager.DinerState.PlayerSeated || !diner.playerLockedToSeat || gm.isConversing || gm.uIManager.inTransition) return;
            if (!(gm.inputHandler.spaceInput || Input.GetKeyDown(KeyCode.Space)) || Time.unscaledTime < lockHintAt) return;
            lockHintAt = Time.unscaledTime + 3f;
            gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Сначала доедим и расплатимся.", "Let's finish the meal and pay first."), 3f);
        }

        public void SceneLoaded()
        {
            sentBooth = -2; peePrompted = false; lockKicked = false; partnerSeat = null; movedOpposite = false; movedBooth = null;
            sentMeal = leadMeal = followerLocked = false;
            partnerTray = null; trayBooth = null; secondTrip = partnerTrayServed = sentTrayServed = false;
            puppets.Clear();
        }

        public void PartnerSeatChanged(string seat) => partnerSeat = seat;

        // Tessa sits down: at the table where Marcus sits (or, once ordered, the order table), whatever table was clicked.
        internal void BeforeSit()
        {
            var diner = Manager;
            if (!diner || !plugin.Ready || !plugin.FollowsStory) return;
            DinerBooth target = null;
            if (partnerSeat != null && partnerSeat.StartsWith("booth") && int.TryParse(partnerSeat.Substring(5), out int index) && index >= 0 && index < diner.booths.Length)
                target = diner.booths[index];
            else if (diner.originalBooth) target = diner.originalBooth;
            if (!target || target == diner.activeBooth) return;
            Plugin.Log("DINER_SEAT_REDIRECT " + Array.IndexOf(diner.booths, diner.activeBooth) + " -> " + Array.IndexOf(diner.booths, target));
            diner.activeBooth = target;
        }

        // The booth camera rig: SeatEye/SeatYaw use the rig's resting pose, not where the player is looking.
        internal static Vector3 SeatEye(DinerBooth booth) => booth.camera ? booth.camera.transform.position : booth.playerInSeat.transform.position;
        internal static float SeatYaw(DinerBooth booth) => booth.playerInSeat.transform.eulerAngles.y;

        public void Tick()
        {
            var diner = Manager;
            if (!diner || !plugin.Ready)
            {
                RestoreRig();
                // Partner gone: a lock that only the partner's meal imposed must not keep us at the table.
                if (diner && followerLocked) { followerLocked = false; diner.playerLockedToSeat = false; }
                return;
            }
            LockHint(diner);
            if (plugin.LeadsStory) LeadTick(diner);
            else FollowTick(diner);
            SeatOpposite(diner);
        }

        void LeadTick(DinerManager diner)
        {
            if (Time.unscaledTime >= nextSend)
            {
                nextSend = Time.unscaledTime + 0.1f;
                if (diner.waitress) SendPuppet("waitress", diner.waitress.gameObject, diner.waitress.animator, diner.waitress.footsteps,
                    (diner.waitress.orderTray && diner.waitress.orderTray.activeSelf ? 1 : 0) | (diner.waitress.oldTray && diner.waitress.oldTray.activeSelf ? 2 : 0)
                    | (diner.waitress.arm && diner.waitress.arm.weight > 0.5f ? 4 : 0));
                if (diner.badGuy && diner.badGuy.gameObject.activeSelf) SendPuppet("badguy", diner.badGuy.gameObject, diner.badGuy.GetComponent<Animator>(), null, 0);
            }
            int booth = diner.originalBooth ? Array.IndexOf(diner.booths, diner.originalBooth) : -1;
            bool meal = diner.playerLockedToSeat && !diner.dinerDone;
            if (booth != sentBooth || meal != sentMeal || partnerTrayServed != sentTrayServed || Time.unscaledTime >= nextBoothRepeat)
            {
                sentBooth = booth; sentMeal = meal; sentTrayServed = partnerTrayServed; nextBoothRepeat = Time.unscaledTime + 2f;
                plugin.Send(new Packet { kind = "diner", code = "booth", arg = booth, active = meal, bits = partnerTrayServed ? 1 : 0, scene = SceneManager.GetActiveScene().name });
            }
            var waitress = diner.waitress;
            // Carrying Tessa's tray: the arm holds it up as it held Marcus's.
            if (secondTrip && waitress && waitress.arm && waitress.orderTray && waitress.orderTray.activeSelf) waitress.arm.weight = 1f;
            // The partner came back from the washroom while we wait at our table: the food can come now.
            if ((!lockKicked || secondTrip) && diner.playerPeed && diner.waitressOrderTaken && !diner.dinerDone && !diner.playerWaitingForCheck
                && diner.dinerState == DinerManager.DinerState.PlayerSeated && diner.activeBooth == diner.originalBooth
                && waitress && waitress.state == Waitress.State.Waiting && waitress.orderTray && waitress.orderTray.activeSelf)
            {
                lockKicked = true;
                diner.playerLockedToSeat = true;
                waitress.state = Waitress.State.ComeFromKitchen;
                GameManager.instance.uIManager.controlsText.text = "";
                Plugin.Log("DINER_FOOD_COMING");
            }
        }

        void SendPuppet(string id, GameObject go, Animator animator, AudioSource footsteps, int bits)
        {
            var t = go.transform;
            plugin.Send(new Packet { kind = "npc", code = id, scene = SceneManager.GetActiveScene().name, position = t.position, rotation = t.rotation,
                arg = animator && animator.enabled ? animator.GetInteger("State") : -1, active = go.activeSelf, torch = footsteps && footsteps.isPlaying, bits = bits });
        }

        void FollowTick(DinerManager diner)
        {
            // One rule for both: while the meal is on the order table (served until the bill is paid), whoever sits at
            // that table stays seated, exactly as the lead's own game keeps the lead there. Anyone elsewhere is free.
            bool atOrderTable = diner.dinerState == DinerManager.DinerState.PlayerSeated && diner.activeBooth && diner.activeBooth == diner.originalBooth;
            bool locked = leadMeal && atOrderTable && !diner.dinerDone;
            if (locked != diner.playerLockedToSeat || locked != followerLocked)
            {
                diner.playerLockedToSeat = locked; followerLocked = locked;
                Plugin.Log("DINER_FOLLOWER_LOCK " + locked);
            }
            if (!peePrompted && diner.waitressOrderTaken && !diner.playerPeed)
            {
                peePrompted = true;
                GameManager.instance.subtitle.ShowSubAndClose(F2FLocalizedText.GetLocalizedText("ep4_subs", "HabitPee"), 5f, 3f);
                diner.StartCoroutine(diner._PeeSub());
                Plugin.Log("DINER_PEE_PROMPT");
            }
            float k = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            Follow("waitress", diner.waitress ? diner.waitress.gameObject : null, k);
            Follow("badguy", diner.badGuy ? diner.badGuy.gameObject : null, k);
        }

        void Follow(string id, GameObject go, float k)
        {
            if (!go || !puppets.TryGetValue(id, out var puppet) || !puppet.Has) return;
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent && agent.enabled) { agent.updatePosition = false; agent.updateRotation = false; }
            var t = go.transform;
            if ((t.position - puppet.Position).sqrMagnitude > 25f) t.SetPositionAndRotation(puppet.Position, puppet.Rotation);
            else t.SetPositionAndRotation(Vector3.Lerp(t.position, puppet.Position, k), Quaternion.Slerp(t.rotation, puppet.Rotation, k));
            if (agent && agent.enabled) agent.nextPosition = t.position;
        }

        public void Receive(Packet packet)
        {
            var diner = Manager;
            if (!diner || packet.scene != SceneManager.GetActiveScene().name) return;
            if (packet.kind == "npc")
            {
                if (!plugin.FollowsStory) return;
                if (!puppets.TryGetValue(packet.code, out var puppet)) puppets[packet.code] = puppet = new Puppet();
                puppet.Position = packet.position; puppet.Rotation = packet.rotation.normalized; puppet.Has = true;
                Animator animator = null; AudioSource footsteps = null;
                if (packet.code == "waitress" && diner.waitress)
                {
                    animator = diner.waitress.animator; footsteps = diner.waitress.footsteps;
                    if (diner.waitress.orderTray && diner.waitress.orderTray.activeSelf != ((packet.bits & 1) != 0)) diner.waitress.orderTray.SetActive((packet.bits & 1) != 0);
                    if (diner.waitress.oldTray && diner.waitress.oldTray.activeSelf != ((packet.bits & 2) != 0)) diner.waitress.oldTray.SetActive((packet.bits & 2) != 0);
                    // The arm that holds a tray up comes down once the tray is on the table.
                    if (diner.waitress.arm) diner.waitress.arm.weight = (packet.bits & 4) != 0 ? 1f : 0f;
                }
                else if (packet.code == "badguy" && diner.badGuy) animator = diner.badGuy.GetComponent<Animator>();
                if (animator && packet.arg >= 0 && packet.arg != puppet.AnimState) { puppet.AnimState = packet.arg; animator.SetInteger("State", packet.arg); }
                if (footsteps && packet.torch != footsteps.isPlaying) { if (packet.torch) footsteps.Play(); else footsteps.Stop(); }
                return;
            }
            switch (packet.code)
            {
                case "booth":
                    if (!plugin.FollowsStory) return;
                    leadMeal = packet.active;
                    if ((packet.bits & 1) != 0 && !partnerTrayServed && packet.arg >= 0 && packet.arg < diner.booths.Length) ServePartnerTray(diner.booths[packet.arg]);
                    if (packet.arg < 0 || packet.arg >= diner.booths.Length || diner.originalBooth == diner.booths[packet.arg]) return;
                    var booth = diner.booths[packet.arg];
                    diner.originalBooth = booth;
                    // The lamp over the lead's table turns on, as it does in the lead's world.
                    if (booth.LightLod1) { booth.LightLod1.material = booth.turnOnMaterial; if (booth.LightLod2) booth.LightLod2.material = booth.turnOnMaterial; if (booth.lightTop) booth.lightTop.SetActive(true); }
                    Plugin.Log("DINER_BOOTH " + packet.arg);
                    break;
                case "eat":
                    var food = packet.name != null && packet.name.StartsWith(PartnerTrayPrefix)
                        ? (partnerTray ? FindRelative(partnerTray.transform, packet.name.Substring(PartnerTrayPrefix.Length)) : null)
                        : ItemSync.FindPath(packet.name);
                    if (food) plugin.StartCoroutine(Eaten(food.gameObject, packet.active, packet.arg, packet.pitch));
                    break;
            }
        }

        // What either player ate disappears (or empties, for drinks) in the partner's world too.
        IEnumerator Eaten(GameObject food, bool hide, int drink, float seconds)
        {
            if (food.TryGetComponent<Collider>(out var collider)) collider.enabled = false;
            yield return new WaitForSeconds(Mathf.Clamp(seconds, 0.5f, 15f));
            if (!food) yield break;
            if (hide) { food.SetActive(false); yield break; }
            var tray = food.GetComponentInParent<FoodTray>();
            if (!tray) yield break;
            if (drink == (int)FoodTray.DrinkOrder.Soda + 1 && tray.sodaFull) { tray.sodaFull.SetActive(false); tray.sodaEmpty.SetActive(true); }
            else if (drink == (int)FoodTray.DrinkOrder.Slush + 1 && tray.slushFull) { tray.slushFull.SetActive(false); tray.slushEmpty.SetActive(true); }
        }

        internal void Ate(FoodTray tray, AudioSource sfx, GameObject food, bool hide, FoodTray.DrinkOrder? drink)
        {
            if (!plugin.Ready || !food) return;
            // Tessa's tray exists in both worlds as a copy, so its food is named relative to the copy.
            string name = tray && tray == partnerTray ? PartnerTrayPrefix + RelativePath(partnerTray.transform, food.transform) : ItemSync.PathOf(food.transform);
            plugin.Send(new Packet { kind = "diner", code = "eat", scene = SceneManager.GetActiveScene().name, name = name,
                active = hide, arg = drink.HasValue ? (int)drink.Value + 1 : 0, pitch = sfx && sfx.clip ? sfx.clip.length : 3f });
        }

        const string PartnerTrayPrefix = "tray2:";

        static string RelativePath(Transform root, Transform t)
        {
            string path = "";
            for (; t && t != root; t = t.parent) path = t.name + "#" + t.GetSiblingIndex() + (path.Length > 0 ? "/" + path : "");
            return path;
        }

        static Transform FindRelative(Transform root, string path)
        {
            var current = root;
            if (string.IsNullOrEmpty(path)) return current;
            foreach (var part in path.Split('/'))
            {
                Transform next = null;
                foreach (Transform child in current) if (child.name + "#" + child.GetSiblingIndex() == part) { next = child; break; }
                if (!(current = next)) return null;
            }
            return current;
        }

        // ---- Tessa's tray ----

        // The order table's tray was just set (in the lead's world by the waitress, in the partner's by the mirror):
        // make Tessa's copy beside it now, while it is fresh, turned half round the table to her bench.
        internal void TraySet(FoodTray tray)
        {
            var diner = Manager;
            if (!diner || !plugin.Ready || partnerTray || !tray || tray == partnerTray) return;
            var booth = Array.Find(diner.booths, b => b && b.foodTray == tray);
            if (booth) MakePartnerTray(booth);
        }

        void MakePartnerTray(DinerBooth booth)
        {
            var tray = booth.foodTray;
            if (partnerTray || !tray) return;
            var center = TableCenter(booth);
            var half = Quaternion.Euler(0f, 180f, 0f);
            var copy = UnityEngine.Object.Instantiate(tray.gameObject, tray.transform.parent);
            copy.name = tray.gameObject.name + " (Tessa)";
            copy.transform.SetPositionAndRotation(center + half * (tray.transform.position - center), half * tray.transform.rotation);
            copy.SetActive(false);
            partnerTray = copy.GetComponent<FoodTray>();
            trayBooth = booth;
            var renderers = copy.GetComponentsInChildren<Renderer>(true);
            Plugin.Log("DINER_PARTNER_TRAY made booth=" + Array.IndexOf(Manager.booths, booth) + " at=" + copy.transform.position.ToString("F2") + " children=" + copy.transform.childCount + " renderers=" + renderers.Length);
        }

        void ServePartnerTray(DinerBooth booth)
        {
            if (!partnerTray && booth) MakePartnerTray(booth);
            if (!partnerTray || partnerTrayServed) return;
            partnerTrayServed = true;
            partnerTray.gameObject.SetActive(true);
            if (partnerTray.trayDownAudioSource) partnerTray.trayDownAudioSource.Play();
            Plugin.Log("DINER_PARTNER_TRAY served");
        }

        // The lead's waitress serves both: Marcus's tray first, then back to the kitchen for Tessa's.
        // A second trip already under way is finished the same way even if the partner has left meanwhile (the game's
        // own routine would set Marcus's tray down again, bringing back what he has eaten since).
        internal bool ServesTwo => secondTrip || (plugin.Ready && plugin.LeadsStory && !partnerTrayServed);

        internal IEnumerator Serve(Waitress waitress)
        {
            var diner = Manager;
            waitress.animator.SetInteger("State", 1);
            yield return new WaitForSeconds(1f);
            if (!diner) yield break;
            if (!secondTrip)
            {
                diner.activeBooth.foodTray.SetFoodTray();
                waitress.orderTray.SetActive(false);
                waitress.arm.weight = 0f;
                yield return new WaitForSeconds(0.5f);
                if (partnerTray)
                {
                    secondTrip = true;
                    waitress.puttingFoodDown = false;
                    waitress.StartGoToKitchen();
                    Plugin.Log("DINER_SECOND_TRAY_TRIP");
                    yield break;
                }
            }
            else
            {
                ServePartnerTray(trayBooth);
                nextBoothRepeat = 0f;
                waitress.orderTray.SetActive(false);
                waitress.arm.weight = 0f;
                secondTrip = false;
                yield return new WaitForSeconds(0.5f);
            }
            waitress.MoveAtRandom();
            waitress.state = Waitress.State.RandomWalking;
            waitress.footsteps.Play();
        }

        // Each eats from their own tray: Marcus once both trays are on the table, Tessa from hers.
        internal bool CanEat(FoodTray tray)
        {
            var diner = Manager;
            if (!plugin.Ready || !diner) return true;
            if (tray == partnerTray) return plugin.FollowsStory;
            if (plugin.FollowsStory)
            {
                if (partnerTray && partnerTrayServed && diner.activeBooth == trayBooth) partnerTray.EatFood();
                return false;
            }
            if (!partnerTray || partnerTrayServed) return true;
            var gm = GameManager.instance;
            if (gm.inputHandler.mouseLeftClickDown && Time.unscaledTime >= waitHintAt)
            {
                waitHintAt = Time.unscaledTime + 3f;
                gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Подожду, пока принесут и Тессе.", "I'll wait until Tessa's food comes too."), 2.5f);
            }
            return false;
        }

        // Lab: click the main dish on our own tray ("mine") or on the partner's ("other"), through the game's raycast.
        internal string LabEat(string which)
        {
            var diner = Manager;
            if (!diner || !diner.activeBooth) return "not seated";
            var own = plugin.FollowsStory && plugin.Ready ? partnerTray : diner.activeBooth.foodTray;
            var other = own == partnerTray ? diner.activeBooth.foodTray : partnerTray;
            var tray = which == "other" ? other : own;
            if (!tray) return "no tray";
            GameObject target = null;
            foreach (var candidate in new[] { tray.burgerCollider, tray.sandwichCollider, tray.meatballSubCollider, tray.friesCollider, tray.onionringsCollider, tray.nuggetsSubCollider })
                if (candidate && candidate.activeInHierarchy && candidate.TryGetComponent<Collider>(out var c) && c.enabled) { target = candidate; break; }
            var camera = Camera.main;
            if (!target || !camera) return "no dish";
            var point = camera.WorldToScreenPoint(target.GetComponent<Collider>().bounds.center);
            if (point.z <= 0f) return "behind camera";
            LabInput.ClickPoint = new Vector2(point.x, point.y);
            LabInput.ClickFrame = Time.frameCount + 2;
            return target.name + " on " + tray.name;
        }

        // Fixed sides: Tessa (the partner) always takes the bench across the table; Marcus keeps the booth's own.
        void SeatOpposite(DinerManager diner)
        {
            bool seated = diner.dinerState == DinerManager.DinerState.PlayerSeated && diner.activeBooth;
            if (!seated) { RestoreRig(); return; }
            if (movedOpposite || !plugin.Ready || !plugin.FollowsStory) return;
            string mine = "booth" + Array.IndexOf(diner.booths, diner.activeBooth);
            var booth = diner.activeBooth;
            var rig = booth.playerInSeat.transform;
            var center = TableCenter(booth);
            movedBooth = booth;
            rigParent = rig.parent;
            // Half a turn around the table's vertical axis puts the rig on the opposite bench, facing back. The seat
            // camera script rewrites the rig's local yaw every frame, so the turn is applied to a new parent instead.
            var half = Quaternion.Euler(0f, 180f, 0f);
            pivot = new GameObject("Coop opposite bench").transform;
            pivot.SetParent(rigParent, false);
            pivot.SetPositionAndRotation(center + half * ((rigParent ? rigParent.position : Vector3.zero) - center), half * (rigParent ? rigParent.rotation : Quaternion.identity));
            // The pivot takes the rig's place among the booth's children: the booth's other objects keep their indices,
            // which name them for the partner (the food tray the waitress sets down is found that way).
            rigIndex = rig.GetSiblingIndex();
            rig.SetParent(pivot, false);
            pivot.SetSiblingIndex(rigIndex);
            movedOpposite = true;
            Plugin.Log("DINER_SEAT_OPPOSITE " + mine + " center=" + center.ToString("F2") + " eye=" + SeatEye(booth).ToString("F2"));
        }

        Transform pivot, rigParent;
        int rigIndex;

        void RestoreRig()
        {
            if (!movedOpposite) return;
            movedOpposite = false;
            // Out of the hierarchy first: a destroyed object stays a child until the end of the frame.
            if (pivot) pivot.SetParent(null, false);
            if (movedBooth && movedBooth.playerInSeat) { movedBooth.playerInSeat.transform.SetParent(rigParent, false); movedBooth.playerInSeat.transform.SetSiblingIndex(rigIndex); }
            if (pivot) UnityEngine.Object.Destroy(pivot.gameObject);
            movedBooth = null; pivot = null;
        }

        // The booth's click collider is centred on the table (measured: ~0.98 m in front of every seat rig).
        internal static Vector3 TableCenter(DinerBooth booth)
        {
            var rig = booth.playerInSeat.transform.position;
            var forward = booth.playerInSeat.transform.forward; forward.y = 0f; forward.Normalize();
            var table = booth.seatCollider ? booth.seatCollider.transform.position : rig + forward * 0.98f;
            float along = Mathf.Clamp(Vector3.Dot(table - rig, forward), 0.6f, 1.2f);
            return rig + forward * along;
        }
    }

    // Lead/partner split of the waitress routine; nothing changes when playing alone.
    [HarmonyPatch]
    internal static class WaitressFollowerPatch
    {
        internal static bool Follower => Plugin.Instance != null && Plugin.Instance.FollowsStory && GameManager.instance && GameManager.instance.dinerManager;

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "StartApproachState", "StartTalkToPlayer", "StartConversation", "TakeOrder", "StartFinishEatingConversation",
                "StartBillConversation", "StartRandomWalk", "StartGoToKitchen", "StartGoFinishEating", "StartGetCheck", "MoveAtRandom" })
            { var method = AccessTools.Method(typeof(Waitress), name); if (method != null) yield return method; }
        }

        static bool Prefix() => !Follower;
    }

    [HarmonyPatch]
    internal static class WaitressFollowerRoutinePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "PlayerEntered", "PlayerSat", "ComebackTakeOrder", "GetCheckWait", "PutDownFood" })
            { var method = AccessTools.Method(typeof(Waitress), name); if (method != null) yield return method; }
        }

        static bool Prefix(ref IEnumerator __result)
        {
            if (!WaitressFollowerPatch.Follower) return true;
            __result = Empty();
            return false;
        }

        static IEnumerator Empty() { yield break; }
    }

    [HarmonyPatch(typeof(Waitress), "Update")]
    internal static class WaitressUpdatePatch
    {
        // The partner's waitress is a puppet of the lead's; she can still be clicked for a chat once she has said hello.
        static bool Prefix(Waitress __instance)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.FollowsStory) return true;
            var diner = GameManager.instance ? GameManager.instance.dinerManager : null;
            if (diner && diner.waitressIntroDone && !__instance.conversing) __instance.GeneralDialogue();
            return false;
        }
    }

    [HarmonyPatch(typeof(PeeTrigger), nameof(PeeTrigger.StartPee))]
    internal static class DinerPeePatch
    {
        // In the diner the washroom errand is the partner's (Tessa's) job.
        static bool Prefix()
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.LeadsStory || !GameManager.instance || !GameManager.instance.dinerManager) return true;
            GameManager.instance.subtitle.ShowSubAndClose(ConnectionPanel.L("Мне пока не нужно.", "I don't need to go."), 2.5f);
            return false;
        }
    }

    [HarmonyPatch(typeof(DinerManager), nameof(DinerManager._PeeSub))]
    internal static class DinerPeeReminderPatch
    {
        static bool Prefix(ref IEnumerator __result)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.LeadsStory) return true;
            __result = Empty();
            return false;
        }
        static IEnumerator Empty() { yield break; }
    }

    [HarmonyPatch(typeof(FoodTray), nameof(FoodTray.Cooldown))]
    internal static class DinerEatPatch
    {
        static void Prefix(FoodTray __instance, AudioSource sfx, GameObject gameObject, bool hide, FoodTray.DrinkOrder? drinkOrder)
            => Plugin.Instance?.Diner.Ate(__instance, sfx, gameObject, hide, drinkOrder);
    }

    // Each player eats from their own tray (see DinerSync.CanEat).
    [HarmonyPatch(typeof(FoodTray), nameof(FoodTray.EatFood))]
    internal static class DinerOwnTrayPatch
    {
        static bool Prefix(FoodTray __instance) => Plugin.Instance?.Diner.CanEat(__instance) ?? true;
    }

    // Tessa finishing her tray does not move the story: Marcus's meal does, in the lead's world.
    [HarmonyPatch(typeof(DinerManager), nameof(DinerManager.FinishEating))]
    internal static class DinerFinishPatch
    {
        static bool Prefix() => !WaitressFollowerPatch.Follower || !Plugin.Instance.Ready;
    }

    [HarmonyPatch(typeof(FoodTray), nameof(FoodTray.SetFoodTray))]
    internal static class DinerTraySetPatch
    {
        static void Postfix(FoodTray __instance) => Plugin.Instance?.Diner.TraySet(__instance);
    }

    [HarmonyPatch(typeof(Waitress), nameof(Waitress.PutDownFood))]
    internal static class WaitressServePatch
    {
        static bool Prefix(Waitress __instance, ref IEnumerator __result)
        {
            var diner = Plugin.Instance?.Diner;
            if (diner == null || !diner.ServesTwo) return true;
            __result = diner.Serve(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(UIManager), nameof(UIManager.FadeToDinerSeat))]
    internal static class DinerSeatRedirectPatch
    {
        static void Prefix() => Plugin.Instance?.Diner.BeforeSit();
    }

    // Lines that only make sense for one of the two: the washroom reminders (partner's errand) and "wrong booth".
    [HarmonyPatch]
    internal static class SubtitleFilterPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var method in typeof(Subtitle).GetMethods(BindingFlags.Instance | BindingFlags.Public))
                if (method.Name == nameof(Subtitle.ShowSubAndClose)) yield return method;
        }

        static bool Prefix(string content)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready || string.IsNullOrEmpty(content) || !GameManager.instance || !GameManager.instance.dinerManager) return true;
            string[] hidden = plugin.LeadsStory ? new[] { "HabitPee", "ReallyPee", "StillPee" } : new[] { "WrongBooth" };
            foreach (var key in hidden)
                if (content == F2FLocalizedText.GetLocalizedText("ep4_subs", key)) { Plugin.Log("SUBTITLE_SKIPPED " + key); return false; }
            return true;
        }
    }
}
