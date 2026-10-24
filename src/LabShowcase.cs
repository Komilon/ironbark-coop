using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace IronbarkCoop
{
    // Lab only (showcase clips): Marcus and Tessa run a navmesh path past a fixed camera, the cultist after them.
    internal static class LabShowcase
    {
        static readonly List<Vector3> corners = new List<Vector3>();

        // The walkable way between two points (the cultist's own navmesh), kept for the run.
        internal static string Path(Vector3 from, Vector3 to)
        {
            corners.Clear();
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out var a, 3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 3f, NavMesh.AllAreas)) return "no navmesh";
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)) return "no path";
            corners.AddRange(path.corners);
            var text = new StringBuilder(path.status.ToString());
            foreach (var c in corners) text.Append(" ").Append(c.ToString("F1"));
            return text.ToString();
        }

        static Vector3 At(float distance, out Vector3 direction)
        {
            direction = Vector3.forward;
            for (int i = 0; i + 1 < corners.Count; i++)
            {
                var leg = corners[i + 1] - corners[i];
                float length = leg.magnitude;
                if (length > 0.001f) direction = leg / length;
                if (distance <= length) return corners[i] + direction * distance;
                distance -= length;
            }
            return corners.Count > 0 ? corners[corners.Count - 1] : Vector3.zero;
        }

        static float Length()
        {
            float total = 0f;
            for (int i = 0; i + 1 < corners.Count; i++) total += Vector3.Distance(corners[i], corners[i + 1]);
            return total;
        }

        internal static IEnumerator Run(float speed, float gap, float cultistDelay, float cultistSpeed, Vector3 side)
        {
            if (corners.Count < 2) yield break;
            var marcus = new PartnerAvatar("", "marcus", "marcus");
            var tessa = new PartnerAvatar("", "tessa", "tessa");
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            var cultist = tower ? tower.cultist : null;
            Animator animator = null;
            if (cultist)
            {
                cultist.gameObject.SetActive(true);
                if (cultist.navMeshAgent) cultist.navMeshAgent.enabled = false;
                cultist.enabled = false;
                animator = cultist.GetComponentInChildren<Animator>();
            }
            float length = Length(), start = Time.time;
            float end = Mathf.Max(length / speed + gap, length / cultistSpeed + cultistDelay) + 0.5f;
            Plugin.Log("LAB_SHOWCASE run " + length.ToString("F1") + " m");
            while (Time.time - start < end)
            {
                float t = Time.time - start;
                Place(marcus, t * speed, side);
                Place(tessa, Mathf.Max(0f, t - gap) * speed, -side);
                if (cultist)
                {
                    float d = Mathf.Max(0f, t - cultistDelay) * cultistSpeed;
                    var feet = At(d, out var direction);
                    direction.y = 0f;
                    cultist.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward));
                    if (animator) animator.SetInteger("State", t < cultistDelay || d >= length ? 0 : 5);
                }
                yield return null;
            }
            marcus.Dispose(); tessa.Dispose();
            Plugin.Log("LAB_SHOWCASE done");
        }

        static void Place(PartnerAvatar avatar, float distance, Vector3 side)
        {
            var feet = At(distance, out var direction);
            direction.y = 0f;
            var yaw = Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward);
            avatar.Active = true;
            avatar.SetPose(feet + yaw * side, yaw, 0f, false, false, Time.unscaledTime);
        }
    }
}
