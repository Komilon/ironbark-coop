using UnityEngine;

namespace IronbarkCoop
{
    // Shared RV rides, Scratch Creek style. Seats are independent: whoever clicks the wheel drives (if it is free),
    // the passenger seat is clicked the same way, Space stands up again. While the partner drives, our RV follows
    // theirs; a passenger rides in the seat and a player standing inside the RV is carried along.
    internal sealed class Vehicles
    {
        readonly Plugin plugin;
        bool seated;
        float partnerDrivingSeen = -10f;
        Vector3 rvTargetPos, rvVelocity;
        Quaternion rvTargetRot = Quaternion.identity, wheelTarget = Quaternion.identity;
        bool hasRvTarget, hasWheel, partnerAccel, partnerHorn;
        float partnerIdle;
        bool wasCarrying; float nextTrace;
        Vector3 shakeBase, driverShake, partnerShake, shakeNow, driverTilt, partnerTilt, tiltNow; bool hasShakeBase;
        float baseY, basePitch, baseRoll; Quaternion shakeHolderRot;
        Transform shakeHolder; Vector3 shakeHolderBase;
        Transform originalParent;
        PassengerLook look;
        internal string PartnerSeat;
        public bool Seated => seated;
        public bool LookingAtSeat { get; private set; }
        public bool Carrying { get; private set; }
        public string Debug => "shake=" + shakeNow.magnitude.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "/" + driverShake.magnitude.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " tilt=" + tiltNow.magnitude.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "/" + driverTilt.magnitude.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " rvVel=" + (Rv && Rv.rigidbody ? Rv.rigidbody.velocity.ToString("F1").Replace(" ", "") : "-") + " target=" + rvTargetPos.ToString("F1") + " partnerDriving=" + PartnerDriving + " partnerSeat=" + (PartnerSeat ?? "-") + " carrying=" + Carrying + " kinematic=" + (Rv && Rv.rigidbody ? Rv.rigidbody.isKinematic.ToString() : "-")
            + " inRv=" + (Rv && GameManager.instance.playerFP ? Invariant(Rv.transform.InverseTransformPoint(GameManager.instance.playerFP.transform.position)) : "-");
        static string Invariant(Vector3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2};{1:F2};{2:F2}", v.x, v.y, v.z);
        public bool PartnerDriving => Time.unscaledTime - partnerDrivingSeen < 1.5f;

        public Vehicles(Plugin plugin) { this.plugin = plugin; }

        static RVController Rv => GameManager.instance ? GameManager.instance.rVController : null;

        public static bool LocalDriving
        {
            get
            {
                var gm = GameManager.instance;
                return gm && gm.rVController && gm.playerDrive && gm.playerDrive.activeInHierarchy;
            }
        }

        // Eye points: the driving camera's resting place in the driver rig, and its mirror across the RV for the passenger.
        // DrivingCam turns its body to look around; undo that turn so the driver looking sideways does not move the seats.
        public static Vector3 DriverEye(RVController rv)
        {
            var gm = GameManager.instance;
            if (gm && gm.driveCamera)
            {
                var eye = gm.driveCamera.transform.position;
                var body = rv.drivingCam ? rv.drivingCam.playerBody : null;
                if (!body || !gm.driveCamera.transform.IsChildOf(body)) return eye;
                var parentRotation = body.parent ? body.parent.rotation : Quaternion.identity;
                return body.position + parentRotation * (Quaternion.Inverse(body.rotation) * (eye - body.position));
            }
            return (rv.driverSeatCollider ? rv.driverSeatCollider.transform.position : rv.transform.position) + Vector3.up * 0.5f;
        }

        // The cab is not symmetric: the passenger seat ("RV Seat (1)") sits 0.94 m right of the RV's axis while the
        // driver's ("RV Seat") sits 0.54 m left, and a little further forward. The passenger's eye is the driver's eye
        // moved by the offset between the two seat models; mirroring it put the passenger 0.4 m left of the seat.
        public static Vector3 PassengerEye(RVController rv)
        {
            var local = rv.transform.InverseTransformPoint(DriverEye(rv));
            if (SeatOffset(rv) is Vector3 offset) local += offset; else local.x = -local.x;
            return rv.transform.TransformPoint(local);
        }

        static RVController offsetRv;
        static Vector3? seatOffset;
        static Vector3? SeatOffset(RVController rv)
        {
            if (offsetRv == rv) return seatOffset;
            offsetRv = rv; seatOffset = null;
            Renderer driver = null, passenger = null;
            foreach (var renderer in rv.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "RV Seat") driver = renderer;
                else if (renderer.name == "RV Seat (1)") passenger = renderer;
            }
            if (driver && passenger)
            {
                var offset = rv.transform.InverseTransformPoint(passenger.bounds.center) - rv.transform.InverseTransformPoint(driver.bounds.center);
                // Only a sideways move to the other seat is trusted; anything else means the models are not the seats.
                if (offset.x > 0.8f && offset.x < 2.2f && Mathf.Abs(offset.y) < 0.3f && Mathf.Abs(offset.z) < 0.5f) seatOffset = offset;
            }
            Plugin.Log("RV_SEAT_OFFSET " + (seatOffset.HasValue ? seatOffset.Value.ToString("F2") : "mirrored"));
            return seatOffset;
        }

        // Feet of a seated body below a seated eye point.
        public const float SeatedEyeHeight = 1.25f;
        // The seated body sits back from the eye (measured against the cab seats in the lab captures).
        const float SeatBack = 0.3f;
        public static Vector3 DriverSeat(RVController rv) => DriverEye(rv) - rv.transform.up * SeatedEyeHeight - rv.transform.forward * SeatBack;
        public static Vector3 PassengerSeat(RVController rv) => PassengerEye(rv) - rv.transform.up * SeatedEyeHeight - rv.transform.forward * SeatBack;

        public void SceneLoaded()
        {
            seated = false; Carrying = false; hasRvTarget = false; hasWheel = false; PartnerSeat = null; originalParent = null;
            partnerDrivingSeen = -10f;
        }

        // Adds the RV state to an outgoing pose while this player drives.
        public bool Describe(Packet pose)
        {
            var rv = Rv;
            if (!LocalDriving || !rv) return false;
            pose.driving = true;
            pose.rvPos = rv.transform.position;
            pose.rvRot = rv.transform.rotation;
            pose.rvVel = rv.rigidbody ? rv.rigidbody.velocity : Vector3.zero;
            pose.steer = rv.currentTurnAngle;
            if (rv.steeringHandle) pose.rvWheel = rv.steeringHandle.localRotation;
            pose.rvIdle = rv.rvIdleAudioSource && rv.rvIdleAudioSource.isPlaying ? rv.rvIdleAudioSource.volume + 1f : 0f;
            pose.rvAccel = rv.rvAccelerateAudioSource && rv.rvAccelerateAudioSource.isPlaying;
            pose.shake = driverShake; pose.shakeTilt = driverTilt;
            pose.horn = rv.HornAudioSource && rv.HornAudioSource.isPlaying;
            // The driver's own body is hidden while driving; show the avatar on the driver seat instead.
            pose.active = true;
            pose.sit = true;
            pose.seat = "rv-d";
            pose.position = DriverSeat(rv);
            pose.rotation = Quaternion.Euler(0f, rv.transform.eulerAngles.y, 0f);
            var camera = GameManager.instance.driveCamera;
            if (camera) pose.pitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x);
            return true;
        }

        public void ReceivePose(Packet pose)
        {
            PartnerSeat = pose.active ? pose.seat : null;
            if (!pose.driving) return;
            partnerDrivingSeen = Time.unscaledTime;
            rvTargetPos = pose.rvPos; rvTargetRot = pose.rvRot.normalized; rvVelocity = pose.rvVel; hasRvTarget = true;
            if (pose.rvWheel.w != 0f || pose.rvWheel.x != 0f) { wheelTarget = pose.rvWheel.normalized; hasWheel = true; }
            partnerIdle = pose.rvIdle; partnerAccel = pose.rvAccel; partnerHorn = pose.horn;
            partnerShake = pose.shake; partnerTilt = pose.shakeTilt;
        }

        // The wheel is taken: the game must not seat us there too.
        internal bool DriverSeatTaken => plugin.Ready && PartnerDriving;
        bool PassengerSeatTaken => plugin.Ready && PartnerSeat == "rv-p";

        public void Tick()
        {
            var gm = GameManager.instance;
            var rv = Rv;
            LookingAtSeat = false;
            if (!rv || !gm.playerFP) { seated = false; Carrying = false; return; }
            if (!plugin.Ready) { if (seated) Unseat(true); Carrying = false; return; }
            // The ride's shake as the driver feels it: the cab bouncing and rocking on its suspension (the quick part of
            // its height, pitch and roll) and the driving rig's own bounce on its spring. The partner's copy of the RV
            // follows ours smoothly, so these would be lost on the way.
            if (LocalDriving)
            {
                float dt = Time.unscaledDeltaTime, k = 1f - Mathf.Exp(-6f * dt);
                var euler = rv.transform.eulerAngles;
                float y = rv.transform.position.y, pitchNow = Mathf.DeltaAngle(0f, euler.x), rollNow = Mathf.DeltaAngle(0f, euler.z);
                var spring = gm.playerDrive ? rv.transform.InverseTransformPoint(gm.playerDrive.transform.position) : Vector3.zero;
                if (!hasShakeBase) { baseY = y; basePitch = pitchNow; baseRoll = rollNow; shakeBase = spring; hasShakeBase = true; }
                baseY = Mathf.Lerp(baseY, y, k); basePitch = Mathf.LerpAngle(basePitch, pitchNow, k); baseRoll = Mathf.LerpAngle(baseRoll, rollNow, k);
                shakeBase = Vector3.Lerp(shakeBase, spring, k);
                driverShake = Vector3.ClampMagnitude(spring - shakeBase + Vector3.up * (y - baseY), 0.3f);
                driverTilt = new Vector3(Mathf.Clamp(Mathf.DeltaAngle(basePitch, pitchNow), -6f, 6f), 0f, Mathf.Clamp(Mathf.DeltaAngle(baseRoll, rollNow), -6f, 6f));
            }
            else { hasShakeBase = false; driverShake = Vector3.zero; driverTilt = Vector3.zero; }
            // Scenes that start at the wheel put both players there: the host keeps it, the guest takes the passenger seat.
            if (PartnerDriving && LocalDriving && !plugin.IsHost)
            {
                gm.playerDrive.SetActive(false);
                gm.playerFP.SetActive(true);
                rv.rvState = RVController.RVState.GotUp;
                gm.uIManager.controlsText.text = "";
                Plugin.Log("DRIVER_YIELDED to host");
                Seat(rv);
            }
            bool fpActive = gm.playerFP.activeInHierarchy;
            if (PartnerDriving && !LocalDriving)
            {
                if (rv.rigidbody) rv.rigidbody.isKinematic = true;
                var fp = gm.playerFP.transform;
                // Someone standing in the RV rides along instead of being left on the road.
                // Hysteresis: once riding, keep riding until clearly outside (the axis-aligned bounds wobble as the RV turns).
                var bounds = rv.boundsCollider ? rv.boundsCollider.bounds : new Bounds();
                if (Carrying) bounds.Expand(1f);
                Carrying = !seated && fpActive && rv.boundsCollider && bounds.Contains(fp.position);
                Vector3 localFeet = rv.transform.InverseTransformPoint(fp.position);
                float yawBefore = rv.transform.eulerAngles.y;
                if (hasRvTarget)
                {
                    // A velocity the RV cannot really have (a glitch on the driver's side) must not throw our copy of the RV.
                    var predicted = rvTargetPos + Vector3.ClampMagnitude(rvVelocity, 40f) * 0.05f;
                    float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
                    var position = Vector3.Lerp(rv.transform.position, predicted, k);
                    var rotation = Quaternion.Slerp(rv.transform.rotation, rvTargetRot, k);
                    // An interpolating rigidbody would pull the transform back; move the body itself as well.
                    if (rv.rigidbody) { rv.rigidbody.position = position; rv.rigidbody.rotation = rotation; }
                    rv.transform.SetPositionAndRotation(position, rotation);
                }
                if (Carrying)
                {
                    var controller = gm.playerFP.GetComponent<CharacterController>();
                    if (controller) controller.enabled = false;
                    fp.position = rv.transform.TransformPoint(localFeet);
                    fp.Rotate(0f, Mathf.DeltaAngle(yawBefore, rv.transform.eulerAngles.y), 0f, Space.World);
                    // The cab's colliders must be where the cab now is before the controller moves again, or the
                    // stale walls push the rider backwards through the RV.
                    Physics.SyncTransforms();
                    if (controller) controller.enabled = true;
                }
                if (plugin.Lab && (Carrying != wasCarrying || (Carrying && Time.unscaledTime >= nextTrace)))
                {
                    nextTrace = Time.unscaledTime + 0.2f;
                    Plugin.Log("RIDE_TRACE carrying=" + Carrying + " local=" + Invariant(rv.transform.InverseTransformPoint(fp.position)) + " before=" + Invariant(localFeet) + " rvSpeed=" + (rvVelocity.magnitude).ToString("F1") + " grounded=" + (gm.playerFP.GetComponent<CharacterController>() is CharacterController cc && cc.isGrounded));
                }
                wasCarrying = Carrying;
                if (hasWheel && rv.steeringHandle)
                    rv.steeringHandle.localRotation = Quaternion.Slerp(rv.steeringHandle.localRotation, wheelTarget, 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
                PartnerEngine(rv);
            }
            else Carrying = false;
        }

        // After every Update of the frame, so the game's InputHandler has already read this frame's click and Space.
        public void Interact()
        {
            var gm = GameManager.instance;
            var rv = Rv;
            LookingAtSeat = false;
            if (gm) ApplyShake(gm, rv);
            if (!rv || !gm.playerFP || !plugin.Ready || gm.isPaused || gm.uIManager.inTransition) return;
            if (seated)
            {
                if (gm.inputHandler.spaceInput || Input.GetKeyDown(KeyCode.Space)) Unseat(true);
                return;
            }
            // Passenger seat: clicked like the driver seat (the game itself has no collider there).
            if (!gm.playerFP.activeInHierarchy || gm.isConversing || !Camera.main) return;
            var ray = Camera.main.ScreenPointToRay(gm.inputHandler.mousePosition);
            var seat = PassengerEye(rv) - rv.transform.up * 0.45f;
            float along = Vector3.Dot(seat - ray.origin, ray.direction);
            float miss = (ray.origin + ray.direction * along - seat).magnitude;
            LookingAtSeat = along > 0f && along < 2.6f && miss < 0.42f;
            bool click = gm.inputHandler.mouseLeftClickDown || Input.GetMouseButtonDown(0);
            if (click && along > 0f && along < 4f && miss < 1f) Plugin.Log("SEAT_CLICK along=" + along.ToString("F2") + " miss=" + miss.ToString("F2"));
            if (!LookingAtSeat || !click) return;
            if (PassengerSeatTaken) { gm.subtitle.ShowSubAndClose(ConnectionPanel.L("Это место занято.", "That seat is taken."), 2f); return; }
            Seat(rv);
        }


        // Riding with the partner at the wheel (in the passenger seat or standing in the cab), our view shakes with
        // the driver's: their head's bounce on its spring, applied to our camera holder. Outside the RV, nothing.
        void ApplyShake(GameManager gm, RVController rv)
        {
            var camera = gm.firstPersonController ? gm.firstPersonController.playerCamera : null;
            var holder = camera ? camera.transform.parent : null;
            bool riding = rv && holder && plugin.Ready && PartnerDriving && (seated || Carrying);
            float follow = 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime);
            shakeNow = Vector3.Lerp(shakeNow, riding ? partnerShake : Vector3.zero, follow);
            tiltNow = Vector3.Lerp(tiltNow, riding ? partnerTilt : Vector3.zero, follow);
            if (!riding && shakeHolder && shakeNow.sqrMagnitude < 1e-6f && tiltNow.sqrMagnitude < 1e-4f)
            {
                shakeHolder.localPosition = shakeHolderBase; shakeHolder.localRotation = shakeHolderRot;
                shakeHolder = null;
                return;
            }
            if (!holder || !rv || (!riding && !shakeHolder)) return;
            if (shakeHolder != holder) { if (shakeHolder) { shakeHolder.localPosition = shakeHolderBase; shakeHolder.localRotation = shakeHolderRot; } shakeHolder = holder; shakeHolderBase = holder.localPosition; shakeHolderRot = holder.localRotation; }
            holder.localPosition = shakeHolderBase + holder.parent.InverseTransformVector(rv.transform.TransformVector(shakeNow));
            holder.localRotation = shakeHolderRot * Quaternion.Euler(tiltNow.x, 0f, tiltNow.z);
        }
        // Partner at the wheel: their engine and horn are heard in our copy of the RV.
        void PartnerEngine(RVController rv)
        {
            var idle = rv.rvIdleAudioSource;
            if (idle)
            {
                if (partnerIdle > 0f) { idle.volume = Mathf.MoveTowards(idle.volume, partnerIdle - 1f, Time.unscaledDeltaTime); if (!idle.isPlaying) idle.Play(); }
                else if (idle.isPlaying) idle.Stop();
            }
            var accel = rv.rvAccelerateAudioSource;
            if (accel && partnerAccel && !accel.isPlaying) accel.Play();
            var horn = rv.HornAudioSource;
            if (horn && partnerHorn != horn.isPlaying) { if (partnerHorn) horn.Play(); else horn.Stop(); }
        }

        void Seat(RVController rv)
        {
            var gm = GameManager.instance;
            if (seated) return;
            seated = true;
            if (gm.firstPersonController) gm.firstPersonController.enabled = false;
            var controller = gm.playerFP.GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
            originalParent = gm.playerFP.transform.parent;
            gm.playerFP.transform.SetParent(rv.transform, true);
            // Put the camera exactly at the passenger's eye point.
            var camera = gm.firstPersonController ? gm.firstPersonController.playerCamera.transform : Camera.main.transform;
            gm.playerFP.transform.rotation = Quaternion.Euler(0f, rv.transform.eulerAngles.y, 0f);
            gm.playerFP.transform.position += PassengerEye(rv) - camera.position;
            look = gm.playerFP.GetComponent<PassengerLook>() ?? gm.playerFP.AddComponent<PassengerLook>();
            look.enabled = true;
            look.Begin(camera);
            gm.uIManager.controlsText.text = F2FLocalizedText.GetLocalizedText("ep4_controls", "GetUp");
            Plugin.Log("PASSENGER_SEATED");
        }

        internal void Unseat(bool standInside)
        {
            var gm = GameManager.instance;
            if (!seated) return;
            seated = false;
            if (look) look.enabled = false;
            if (!gm || !gm.playerFP) return;
            gm.playerFP.transform.SetParent(originalParent, true);
            var rv = Rv;
            if (standInside && rv && rv.fPTransform) gm.playerFP.transform.position = rv.fPTransform.position;
            gm.playerFP.transform.rotation = Quaternion.Euler(0f, gm.playerFP.transform.eulerAngles.y, 0f);
            if (gm.firstPersonController)
            {
                gm.firstPersonController.playerCamera.transform.localRotation = Quaternion.identity;
                gm.firstPersonController.rotationX = 0f;
            }
            var controller = gm.playerFP.GetComponent<CharacterController>();
            if (controller) controller.enabled = true;
            if (gm.firstPersonController) gm.firstPersonController.enabled = true;
            if (gm.uIManager && gm.uIManager.controlsText.text == F2FLocalizedText.GetLocalizedText("ep4_controls", "GetUp")) gm.uIManager.controlsText.text = "";
            Plugin.Log("PASSENGER_LEFT");
        }
    }

    // The wheel cannot be taken while the partner drives; from the passenger seat it can when it is free.
    [HarmonyLib.HarmonyPatch(typeof(UIManager), nameof(UIManager.FadeToDrive))]
    internal static class DriverSeatPatch
    {
        static bool Prefix(ref System.Collections.IEnumerator __result)
        {
            var rides = Plugin.Instance?.Rides;
            if (rides == null) return true;
            if (rides.DriverSeatTaken)
            {
                GameManager.instance.subtitle.ShowSubAndClose(ConnectionPanel.L("За рулём уже сидят.", "Someone is already driving."), 2f);
                __result = Empty();
                return false;
            }
            if (rides.Seated) rides.Unseat(false);
            return true;
        }
        static System.Collections.IEnumerator Empty() { yield break; }
    }

    // Free look from the passenger seat while the regular controller is off.
    internal sealed class PassengerLook : MonoBehaviour
    {
        Transform view;
        float yaw, pitch;
        public float Pitch => pitch;
        public void Begin(Transform camera) { view = camera; yaw = 0f; pitch = 0f; }
        public void Turn(float toYaw, float toPitch) { yaw = toYaw; pitch = toPitch; }
        void LateUpdate()
        {
            if (!view || (GameManager.instance && GameManager.instance.isPaused)) return;
            if (UnfocusedMousePatch.Focused) yaw = Mathf.Clamp(yaw + Input.GetAxis("Mouse X") * 2f, -120f, 120f);
            if (UnfocusedMousePatch.Focused) pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2f, -60f, 60f);
            view.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
