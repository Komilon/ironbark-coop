using HarmonyLib;
using UnityEngine;

namespace IronbarkCoop
{
    // Crouch is held on Ctrl (as in Scratch Creek) instead of toggled with C.
    [HarmonyPatch(typeof(FirstPersonController), "ShouldCrouch", MethodType.Getter)]
    internal static class HoldCrouchPatch
    {
        static readonly AccessTools.FieldRef<FirstPersonController, bool> Crouching = AccessTools.FieldRefAccess<FirstPersonController, bool>("isCrouching");
        static readonly AccessTools.FieldRef<FirstPersonController, bool> Animating = AccessTools.FieldRefAccess<FirstPersonController, bool>("duringCrouchAnimation");
        static readonly AccessTools.FieldRef<FirstPersonController, CharacterController> Body = AccessTools.FieldRefAccess<FirstPersonController, CharacterController>("characterController");
        // Lab-only stand-in for holding the key.
        internal static bool LabHeld;

        static bool Prefix(FirstPersonController __instance, ref bool __result)
        {
            __result = false;
            bool held = LabHeld || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Animating(__instance) || held == Crouching(__instance)) return false;
            var body = Body(__instance);
            if (held) { __result = body && body.isGrounded; return false; }
            // Stand up only where there is headroom (under a table or bed the player stays down until clear).
            var camera = __instance.playerCamera ? __instance.playerCamera.transform.position : __instance.transform.position + Vector3.up;
            __result = !Physics.SphereCast(camera, 0.25f, Vector3.up, out _, 0.9f, ~0, QueryTriggerInteraction.Ignore);
            return false;
        }
    }

    // Unity reads raw mouse motion and buttons even while the window is in the background: with two copies side by
    // side, moving the mouse or clicking in one acted in the other too (turning, clicking, drinking the coffee). Only
    // the window with focus takes input. Runs before the lab's synthetic input, which is applied afterwards.
    [HarmonyPatch(typeof(InputHandler), "Update")]
    internal static class UnfocusedMousePatch
    {
        static readonly string[] Floats = { "mouseX", "mouseY", "horizontalInput", "verticalInput" };
        static readonly string[] Bools = { "mouseLeftClick", "mouseLeftClickUp", "mouseLeftClickDown", "mouseRightClick", "mouseRightClickUp", "mouseRightClickDown", "spaceInput", "gDown", "fDown" };
        static readonly System.Reflection.MethodInfo[] FloatSetters = System.Array.ConvertAll(Floats, n => AccessTools.PropertySetter(typeof(InputHandler), n));
        static readonly System.Reflection.MethodInfo[] BoolSetters = System.Array.ConvertAll(Bools, n => AccessTools.PropertySetter(typeof(InputHandler), n));
        internal static bool Focused => Application.isFocused;

        [HarmonyPriority(Priority.First)]
        static void Postfix(InputHandler __instance)
        {
            if (Focused) return;
            foreach (var setter in FloatSetters) setter?.Invoke(__instance, new object[] { 0f });
            foreach (var setter in BoolSetters) setter?.Invoke(__instance, new object[] { false });
        }
    }

    // Item scripts that read the mouse themselves (the morning coffee's sip, matches, bear spray, binoculars...).
    [HarmonyPatch]
    internal static class UnfocusedItemPatch
    {
        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var type in new[] { typeof(Mug), typeof(Matchbox), typeof(BearSpray), typeof(Binoculars), typeof(BinocularsSeq6), typeof(Compass), typeof(Anemometer), typeof(Gas) })
            {
                var method = AccessTools.Method(type, "Update");
                if (method != null) yield return method;
            }
        }

        static bool Prefix() => UnfocusedMousePatch.Focused || Plugin.Instance == null || !Plugin.Instance.Ready;
    }

    [HarmonyPatch(typeof(FirstPersonController), "HandleMouseLook")]
    internal static class UnfocusedLookPatch
    {
        static bool Prefix() => UnfocusedMousePatch.Focused;
    }
}
