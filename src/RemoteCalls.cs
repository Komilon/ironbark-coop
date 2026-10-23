using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // World-changing game methods the field mirror cannot see (doors animated by tweens, lights, the radio, the
    // diner's tray): when either player's game runs one, the partner's game runs the same method on the same object.
    internal static class RemoteCalls
    {
        static readonly string[] Mirrored = {
            "RVController.OpenDoor", "RVController.CloseDoor", "RVController.OpenBathroomDoor", "RVController.CloseBathroomDoor",
            "RVController.TurnOnLights", "RVController.TurnOffLights",
            "Radio.Play", "Radio.Stop", "Radio.PlaySeq3",
            "DinerManager.OpenBathroomDoor", "DinerManager.CloseBathroomDoor", "DinerManager.OpenBarDoors", "DinerManager.CloseBarDoors",
            "DinerManager.BadGuyHides", "OptimizationFirstScene.LoadRoseburg",
            "TrailStartManager.OpenGuardHouseDoor", "TrailStartManager.CloseGuardHouseDoor",
            "FoodTray.SetFoodTray", "Waitress.SetFoodTray", "Waitress.WaitressPoint",
            // The generator is started by one player for the whole tower (night 1, and night 7 after the power cut).
            "WatchTowerManager.StartGenerator", "WatchTowerManager.PowerBack", "WatchTowerManager.FloodLightsBackOn",
            // Night 4's casserole: an ingredient either player puts in is in both worlds' dish; it bakes for both.
            "Casserole.SetCheese", "Casserole.SetPasta", "Casserole.SetPepperoni", "Casserole.SetSauce", "Casserole.SetTomato", "Casserole.FinishBaking",
        };

        internal static bool Applying;
        static int depth;
        public static int Sent { get; private set; }
        public static int Applied { get; private set; }

        internal static IEnumerable<MethodBase> Targets()
        {
            foreach (var name in Mirrored)
            {
                int dot = name.IndexOf('.');
                var type = AccessTools.TypeByName(name.Substring(0, dot));
                var method = type != null ? AccessTools.Method(type, name.Substring(dot + 1)) : null;
                if (method != null) yield return method;
                else Plugin.Log("REMOTE_CALL_MISSING " + name);
            }
        }

        // Nested mirrored calls (BadGuyHides opens the washroom door itself) travel as the outer call only.
        internal static void Before(MethodBase method, object instance)
        {
            depth++;
            if (depth > 1 || Applying) return;
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready || !(instance is Component component) || !component) return;
            var packet = new Packet { kind = "call", code = method.DeclaringType.Name + "." + method.Name, name = ItemSync.PathOf(component.transform), scene = SceneManager.GetActiveScene().name };
            // The radio starts where the partner's is, so both hear the same clip at the same moment.
            if (component is Radio radio && radio.audioSource)
            {
                packet.arg = radio.currentClip;
                packet.pitch = radio.audioSource.time;
            }
            plugin.Send(packet);
            Sent++;
        }

        internal static void After() { if (depth > 0) depth--; }

        public static void Receive(Packet packet)
        {
            if (packet.scene != SceneManager.GetActiveScene().name || string.IsNullOrEmpty(packet.code)) return;
            if (packet.code == "OnTrigger.Enter")
            {
                var trigger = ItemSync.FindPath(packet.name);
                if (trigger && trigger.TryGetComponent<OnTrigger>(out var road)) RoadTriggerPatch.Replay(road);
                return;
            }
            if (Array.IndexOf(Mirrored, packet.code) < 0) return;
            int dot = packet.code.IndexOf('.');
            var type = AccessTools.TypeByName(packet.code.Substring(0, dot));
            var method = type != null ? AccessTools.Method(type, packet.code.Substring(dot + 1)) : null;
            var target = ItemSync.FindPath(packet.name);
            var component = target && type != null ? target.GetComponent(type) as MonoBehaviour : null;
            if (method == null || !component) { Plugin.Log("REMOTE_CALL_UNRESOLVED " + packet.code + " " + packet.name); return; }
            if (component is Radio radio && radio.audioSource && radio.radioClips != null && packet.arg >= 0 && packet.arg < radio.radioClips.Length)
            {
                radio.currentClip = packet.arg;
                if (radio.audioSource.clip != radio.radioClips[packet.arg]) { radio.audioSource.clip = radio.radioClips[packet.arg]; radio.audioSource.Play(); }
                if (packet.pitch > 0f && packet.pitch < radio.audioSource.clip.length) radio.audioSource.time = packet.pitch;
            }
            Applying = true;
            try
            {
                var result = method.Invoke(component, new object[method.GetParameters().Length]);
                if (result is IEnumerator routine) component.StartCoroutine(routine);
                Applied++;
                Plugin.Log("REMOTE_CALL " + packet.code);
            }
            catch (Exception ex) { Plugin.Log("REMOTE_CALL_FAILED " + packet.code + " " + (ex.InnerException ?? ex).Message); }
            finally { Applying = false; }
        }
    }

    // Road triggers fire for the RV the driver steers (or the driving rig); the passenger's RV only follows, so the
    // driver's trigger events (subtitles, loading the town, slowing down near the diner) are replayed for the partner.
    [HarmonyPatch(typeof(OnTrigger), "OnTriggerEnter")]
    internal static class RoadTriggerPatch
    {
        static readonly AccessTools.FieldRef<OnTrigger, bool> Entered = AccessTools.FieldRefAccess<OnTrigger, bool>("entered");

        static bool IsRvTrigger(OnTrigger trigger)
        {
            var gm = GameManager.instance;
            var target = trigger.enterGameObject;
            if (!gm || !target || !gm.rVController) return false;
            return target.transform.IsChildOf(gm.rVController.transform) || (gm.playerDrive && target == gm.playerDrive);
        }

        static void Prefix(OnTrigger __instance, Collider other)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Ready || !Vehicles.LocalDriving || other.gameObject != __instance.enterGameObject || Entered(__instance) || !IsRvTrigger(__instance)) return;
            plugin.Send(new Packet { kind = "call", code = "OnTrigger.Enter", name = ItemSync.PathOf(__instance.transform), scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name });
        }

        internal static void Replay(OnTrigger trigger)
        {
            if (Entered(trigger)) return;
            Entered(trigger) = true;
            RemoteCalls.Applying = true;
            try { trigger.triggerEvent.Invoke(); Plugin.Log("ROAD_TRIGGER " + trigger.name); }
            catch (Exception ex) { Plugin.Log("ROAD_TRIGGER_FAILED " + trigger.name + " " + ex.Message); }
            finally { RemoteCalls.Applying = false; }
        }
    }

    [HarmonyPatch]
    internal static class RemoteCallPatch
    {
        static IEnumerable<MethodBase> TargetMethods() => RemoteCalls.Targets();
        static void Prefix(MethodBase __originalMethod, object __instance) => RemoteCalls.Before(__originalMethod, __instance);
        static Exception Finalizer(Exception __exception) { RemoteCalls.After(); return __exception; }
    }
}
