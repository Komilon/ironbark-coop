using UnityEngine;

namespace IronbarkCoop
{
    // The tower bed sleeps two: the game lays every player in the same spot on the pillow, so each gets a half, as in
    // the diner: Marcus the side the game lays him on, Tessa the other. The player's own bed camera rig is moved
    // there while in bed and put back on getting up.
    internal sealed class BedSync
    {
        // From the middle of the bed to the middle of a half (the bed is about 1.3 m wide).
        const float HalfBed = 0.3f;
        readonly Plugin plugin;
        Transform shifted;
        Vector3 originalPosition;
        public bool Shifted => shifted;

        public BedSync(Plugin plugin) { this.plugin = plugin; }

        public static bool InBed(WatchTowerManager tower) =>
            tower.state == WatchTowerManager.State.InBed || tower.state == WatchTowerManager.State.OnRadioBed;

        public void SceneLoaded() { shifted = null; coffeeShifted = null; }

        public void Tick()
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            Coffee(tower);
            Transform rig = null;
            if (tower && tower.bed && plugin.Ready && InBed(tower))
                foreach (var go in new[] { tower.playerInBed, tower.playerInBedEating, tower.playerInBedSeq5 })
                    if (go && go.activeInHierarchy) { rig = go.transform; break; }
            if (rig == shifted) return;
            Restore();
            if (!rig) return;
            var camera = rig.GetComponentInChildren<Camera>();
            var eye = camera ? camera.transform.position : rig.position;
            var move = ToHalf(tower, eye, plugin.FollowsStory);
            if (move == Vector3.zero) return;
            originalPosition = rig.position;
            rig.position += move;
            shifted = rig;
            Plugin.Log("BED_OTHER_HALF " + rig.name + " moved " + move.magnitude.ToString("F2"));
        }

        // The move from where the game lays a sleeper to the middle of their half: the game lays you a little to one
        // side of the middle; Marcus keeps that side, Tessa takes the other half.
        internal static Vector3 ToHalf(WatchTowerManager tower, Vector3 eye, bool tessa)
        {
            var center = tower.bed.transform.position;
            var along = center - eye; along.y = 0f;
            if (along.sqrMagnitude < 0.01f) return Vector3.zero;
            var side = Vector3.Cross(Vector3.up, along.normalized);
            float now = Vector3.Dot(eye - center, side);
            float target = (now >= 0f ? 1f : -1f) * (tessa ? -HalfBed : HalfBed);
            return side * (target - now);
        }

        // Where a sleeper's eyes are on their half of the bed, from the bed camera's resting place.
        internal static Vector3? HalfEye(WatchTowerManager tower, bool tessa)
        {
            if (!tower || !tower.bed || !tower.bedCam) return null;
            var eye = tower.bedCam.transform.position;
            return eye + ToHalf(tower, eye, tessa);
        }


        // The morning coffee on the balcony: the game stands every player at the same spot by the rail. Tessa stands a
        // step along the rail from Marcus (whichever side has floor and no wall).
        Transform coffeeShifted;
        Vector3 coffeeOriginal;

        void Coffee(WatchTowerManager tower)
        {
            var rig = tower && tower.playerCoffee && tower.playerCoffee.activeInHierarchy && plugin.Ready && plugin.FollowsStory ? tower.playerCoffee.transform : null;
            if (rig == coffeeShifted) return;
            if (coffeeShifted) coffeeShifted.position = coffeeOriginal;
            coffeeShifted = null;
            if (!rig) return;
            var camera = rig.GetComponentInChildren<Camera>();
            var eye = camera ? camera.transform.position : rig.position + Vector3.up * 0.75f;
            var forward = camera ? camera.transform.forward : rig.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) return;
            var side = Vector3.Cross(Vector3.up, forward.normalized);
            foreach (float sign in new[] { 1f, -1f })
            {
                var move = side * (0.8f * sign);
                var spot = eye + move;
                if (Physics.CheckCapsule(spot - Vector3.up * 1.2f, spot, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(spot, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                coffeeOriginal = rig.position;
                rig.position += move;
                coffeeShifted = rig;
                Plugin.Log("COFFEE_BESIDE moved " + move.ToString("F2"));
                return;
            }
            // Nowhere to step aside: stay where the game put her (and do not look again every frame).
            coffeeOriginal = rig.position;
            coffeeShifted = rig;
            Plugin.Log("COFFEE_BESIDE no free spot");
        }

        void Restore()
        {
            if (shifted) shifted.position = originalPosition;
            shifted = null;
        }
    }
}
