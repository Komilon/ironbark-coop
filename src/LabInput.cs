using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IronbarkCoop
{
    // Lab-only synthetic left click (at screen centre, or at a chosen screen point), so tests interact through the
    // game's own raycasts. Inactive unless a bridge command armed it; never used in normal play.
    internal static class LabInput
    {
        internal static int ClickFrame = -1, SpaceFrame = -1, GFrame = -1;
        internal static Vector2? ClickPoint;
        static readonly MethodInfo SetClickDown = AccessTools.PropertySetter(typeof(InputHandler), "mouseLeftClickDown");
        static readonly MethodInfo SetSpace = AccessTools.PropertySetter(typeof(InputHandler), "spaceInput");
        static readonly MethodInfo SetG = AccessTools.PropertySetter(typeof(InputHandler), "gDown");
        static readonly MethodInfo SetMousePosition = AccessTools.PropertySetter(typeof(InputHandler), "mousePosition");
        internal static bool Active => ClickFrame >= 0 && (Time.frameCount == ClickFrame || Time.frameCount == ClickFrame + 1);
        static Vector2 Point => ClickPoint ?? new Vector2(Screen.width / 2f, Screen.height / 2f);

        [HarmonyPatch(typeof(InputHandler), "Update")]
        static class ClickPatch
        {
            static void Postfix(InputHandler __instance)
            {
                if (SpaceFrame >= 0 && Time.frameCount == SpaceFrame) { SetSpace.Invoke(__instance, new object[] { true }); SpaceFrame = -1; }
                if (GFrame >= 0 && Time.frameCount == GFrame) { SetG.Invoke(__instance, new object[] { true }); GFrame = -1; }
                if (ClickFrame < 0) return;
                if (Time.frameCount == ClickFrame)
                {
                    SetClickDown.Invoke(__instance, new object[] { true });
                    SetMousePosition.Invoke(__instance, new object[] { Point });
                    var camera = Camera.main;
                    if (camera && Physics.Raycast(camera.ScreenPointToRay(Point), out var hit, 20f))
                    {
                        var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
                        string game = tower && Physics.Raycast(camera.ScreenPointToRay(Point), out var gameHit, 20f, tower.layerMask) ? " game=" + gameHit.collider.name + "@" + gameHit.distance.ToString("F2") : "";
                        Plugin.Log("LAB_CLICK hit=" + hit.collider.name + " tag=" + hit.collider.tag + " distance=" + hit.distance.ToString("F2") + " camera=" + camera.name + game);
                    }
                    else Plugin.Log("LAB_CLICK no hit camera=" + (camera ? camera.name : "none"));
                }
                else if (Time.frameCount > ClickFrame + 1) { ClickFrame = -1; ClickPoint = null; }
            }
        }

        [HarmonyPatch(typeof(Camera), nameof(Camera.ScreenPointToRay), typeof(Vector3))]
        static class CentreRayPatch
        {
            static void Prefix(ref Vector3 pos)
            {
                if (Active) pos = Point;
            }
        }
    }
}
