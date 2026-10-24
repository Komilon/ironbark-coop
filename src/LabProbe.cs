using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronbarkCoop
{
    // Lab only: answers to "what is there in this scene" questions, written to probe.txt next to state.json.
    //   gas              the gas can's particle systems (the stream and the wet trail it leaves)
    //   named:<text>     every object whose name contains the text: path, position, active, components
    //   clips:<text>     loaded audio clips whose name contains the text
    //   bed-avatar       the partner's lying body: bones and what is under it
    //   intro            the night opening cards' texts
    //   ranger           the tower's ranger and the point the talking camera looks at
    //   casserole        the casserole's state
    internal static class LabProbe
    {
        public static void Run(string path, string code, Plugin plugin)
        {
            var lines = new List<string> { "probe " + code + " scene=" + SceneManager.GetActiveScene().name };
            try
            {
                if (code == "gas") Gas(lines);
                else if (code.StartsWith("named:")) Named(lines, code.Substring(6));
                else if (code.StartsWith("clips:")) Clips(lines, code.Substring(6));
                else if (code == "bed-avatar") BedAvatar(lines, plugin);
                else if (code == "intro") Intro(lines);
                else if (code == "ranger") Ranger(lines);
                else if (code == "casserole") Casserole(lines);
                else if (code == "triggers") Triggers(lines);
                else if (code == "fields" && GameManager.instance && GameManager.instance.watchTowerManager) { var tw = GameManager.instance.watchTowerManager; foreach (var f in typeof(WatchTowerManager).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)) if (f.FieldType == typeof(bool) && (f.Name.StartsWith("seq8") || f.Name.Contains("ount"))) lines.Add(f.Name + "=" + f.GetValue(tw)); lines.Add("state=" + tw.state + " area=" + tw.area + " pictureMode=" + (tw.seq8PictureMode ? tw.seq8PictureMode.gameObject.activeInHierarchy + "/" + tw.seq8PictureMode.inPictureMode : "-")); }
                else if (code.StartsWith("story:") && StorySync.Instance != null) lines.AddRange(StorySync.Instance.PoseKeysFor(code.Substring(6)));
                else if (code.StartsWith("loc:")) { var parts = code.Split(':'); for (int i = 2; i < parts.Length; i++) lines.Add(parts[1] + "/" + parts[i] + " = " + F2FLocalizedText.GetLocalizedText(parts[1], parts[i])); }
            }
            catch (System.Exception ex) { lines.Add("ERROR " + ex); }
            File.WriteAllLines(path, lines);
        }

        static string V(Vector3 v) => v.ToString("F2");

        static void Gas(List<string> lines)
        {
            foreach (var gas in Resources.FindObjectsOfTypeAll<Gas>())
            {
                if (!gas.gameObject.scene.IsValid()) continue;
                lines.Add("GAS " + ItemSync.PathOf(gas.transform) + " active=" + gas.gameObject.activeInHierarchy + " at=" + V(gas.transform.position) + " ps=" + (gas.gasPs ? gas.gasPs.name : "-") + " wet=" + (gas.gasPsWet ? ItemSync.PathOf(gas.gasPsWet.transform) : "-"));
                foreach (var ps in gas.GetComponentsInChildren<ParticleSystem>(true)) Particles(lines, ps);
                if (gas.gasPsWet && !gas.gasPsWet.transform.IsChildOf(gas.transform)) Particles(lines, gas.gasPsWet);
            }
        }

        static void Particles(List<string> lines, ParticleSystem ps)
        {
            var main = ps.main; var emission = ps.emission; var collision = ps.collision; var sub = ps.subEmitters;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            string subs = "";
            for (int i = 0; i < sub.subEmittersCount; i++) subs += sub.GetSubEmitterSystem(i) ? sub.GetSubEmitterSystem(i).name + ":" + sub.GetSubEmitterType(i) + " " : "";
            lines.Add("  PS " + ItemSync.PathOf(ps.transform) + " active=" + ps.gameObject.activeInHierarchy + " playing=" + ps.isPlaying + " count=" + ps.particleCount
                + " loop=" + main.loop + " life=" + main.startLifetime.constantMax.ToString("F1") + " space=" + main.simulationSpace + " max=" + main.maxParticles
                + " rate=" + emission.rateOverTime.constantMax + " collision=" + (collision.enabled ? collision.type + "/" + collision.collidesWith.value + "/msg" + collision.sendCollisionMessages : "off")
                + " subs=" + (sub.enabled ? subs : "off") + " layer=" + LayerMask.LayerToName(ps.gameObject.layer)
                + " material=" + (renderer && renderer.sharedMaterial ? renderer.sharedMaterial.name : "-") + " renderMode=" + (renderer ? renderer.renderMode.ToString() : "-"));
        }

        static void Named(List<string> lines, string text)
        {
            int n = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (!t.gameObject.scene.IsValid() || t.name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var parts = new List<string>();
                foreach (var c in t.GetComponents<Component>()) if (c && !(c is Transform)) parts.Add(c.GetType().Name + (c is Behaviour b && !b.enabled ? "(off)" : c is Collider k ? (k.isTrigger ? "(trigger)" : "") + "[" + V(k.bounds.center) + " size " + V(k.bounds.size) + "]" : ""));
                var light = t.GetComponent<Light>();
                if (t.TryGetComponent<OnTrigger>(out var onTrigger)) { var events = ""; for (int i = 0; i < onTrigger.triggerEvent.GetPersistentEventCount(); i++) events += onTrigger.triggerEvent.GetPersistentMethodName(i) + " "; parts.Add("once=" + onTrigger.onlyOnce + " enter=" + (onTrigger.enterGameObject ? onTrigger.enterGameObject.name : "-") + " events=" + events); }
                lines.Add(ItemSync.PathOf(t) + " at=" + V(t.position) + " active=" + t.gameObject.activeInHierarchy + " tag=" + t.tag + " layer=" + LayerMask.LayerToName(t.gameObject.layer) + " : " + string.Join(", ", parts)
                    + (light ? " light=" + light.type + "/" + light.intensity.ToString("F1") + "/" + light.range.ToString("F1") : ""));
                if (++n > 200) break;
            }
        }

        // Every scene trigger and what its events call (the story's wiring that lives in the scene, not in code).
        static void Triggers(List<string> lines)
        {
            string Events(UnityEngine.Events.UnityEventBase e)
            {
                var list = new List<string>();
                for (int i = 0; i < e.GetPersistentEventCount(); i++)
                {
                    var target = e.GetPersistentTarget(i);
                    list.Add((target is Component c ? c.GetType().Name + "@" + c.name : target ? target.name : "-") + "." + e.GetPersistentMethodName(i));
                }
                return string.Join(" ", list);
            }
            foreach (var t in Resources.FindObjectsOfTypeAll<OnTrigger>())
            {
                if (!t.gameObject.scene.IsValid()) continue;
                var box = t.GetComponent<Collider>();
                lines.Add(ItemSync.PathOf(t.transform) + " at=" + V(box ? box.bounds.center : t.transform.position) + (box ? " size=" + V(box.bounds.size) : "") + " active=" + t.gameObject.activeInHierarchy + " once=" + t.onlyOnce
                    + " enter=" + (t.enterGameObject ? t.enterGameObject.name : "-") + " | in: " + Events(t.triggerEvent) + " | out: " + Events(t.triggerEventExit) + " | stay: " + Events(t.triggerEventStay));
            }
            foreach (var t in Resources.FindObjectsOfTypeAll<SubTrigger>())
                if (t.gameObject.scene.IsValid()) lines.Add("SUB " + ItemSync.PathOf(t.transform) + " at=" + V(t.transform.position) + " active=" + t.gameObject.activeInHierarchy);
        }

        static void Clips(List<string> lines, string text)
        {
            foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (text == "*" || clip.name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0) lines.Add(clip.name + " len=" + clip.length.ToString("F1"));
        }

        static void BedAvatar(List<string> lines, Plugin plugin)
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            var root = plugin.PartnerRoot;
            lines.Add("partner root=" + (root ? V(root.transform.position) : "-") + " lying=" + plugin.PartnerLying);
            if (tower && tower.bed) lines.Add("bed at=" + V(tower.bed.transform.position) + " bedCam=" + (tower.bedCam ? V(tower.bedCam.transform.position) : "-"));
            if (!root) return;
            var animator = root.GetComponentInChildren<Animator>();
            if (animator && animator.isHuman)
                foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    var t = animator.GetBoneTransform(bone);
                    if (!t) continue;
                    string below = "";
                    foreach (var hit in Physics.RaycastAll(t.position + Vector3.up * 0.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.collider.transform.IsChildOf(root.transform)) below += hit.collider.name + "@" + hit.point.y.ToString("F2") + " ";
                    lines.Add("  " + bone + " at=" + V(t.position) + " below: " + below);
                }
            float lowest = float.MaxValue;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>()) lowest = Mathf.Min(lowest, renderer.bounds.min.y);
            lines.Add("  body lowest=" + lowest.ToString("F2"));
        }

        static void Intro(List<string> lines)
        {
            var ui = GameManager.instance ? GameManager.instance.uIManager : null;
            if (!ui) return;
            foreach (var field in typeof(UIManager).GetFields())
                if (field.FieldType == typeof(SceneIntroText) && field.GetValue(ui) is SceneIntroText intro && intro)
                {
                    var text = intro.GetComponent<TMPro.TMP_Text>();
                    lines.Add(field.Name + " = " + (text ? text.text.Replace("\n", " | ") : "-"));
                }
        }

        static void Ranger(List<string> lines)
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            if (!tower) return;
            lines.Add("parkRanger=" + (tower.parkRanger ? ItemSync.PathOf(tower.parkRanger.transform) + " active=" + tower.parkRanger.gameObject.activeInHierarchy : "-")
                + " lookHere=" + (tower.parkRanger && tower.parkRanger.lookHere ? V(tower.parkRanger.lookHere.position) : "-") + " lerpSpeed=" + AccessTools.Field(typeof(WatchTowerManager), "lerpSpeed").GetValue(tower)
                + " fov=" + (GameManager.instance.fpCamera ? GameManager.instance.fpCamera.fieldOfView.ToString("F1") : "-"));
            lines.Add("shedLight=" + (tower.shedLight ? ItemSync.PathOf(tower.shedLight.transform) + " at=" + V(tower.shedLight.transform.position) + " active=" + tower.shedLight.activeSelf : "-")
                + " shedOn=" + tower.isShedLightOn + " shedSwitch=" + (tower.lightSwitchShed ? V(tower.lightSwitchShed.transform.position) : "-"));
            lines.Add("portaPotty=" + (tower.portaPotty ? V(tower.portaPotty.transform.position) : "-") + " door=" + (tower.door ? V(tower.door.transform.position) : "-") + " generator=" + (tower.generator ? V(tower.generator.transform.position) : "-"));
        }

        static void Casserole(List<string> lines)
        {
            var tower = GameManager.instance ? GameManager.instance.watchTowerManager : null;
            var c = tower ? tower.casserole : null;
            if (!c) return;
            lines.Add("casserole " + ItemSync.PathOf(c.transform) + " at=" + V(c.transform.position) + " active=" + c.gameObject.activeInHierarchy + " in=" + c.ingredientsIn + " all=" + c.allIngredientsIn + " pickable=" + c.isPickable
                + " cheese=" + c.cheeseIn + " pasta=" + c.pastaIn + " pepperoni=" + c.pepperoniIn + " sauce=" + c.sauceIn + " tomato=" + c.tomatoIn + " finished=" + c.finishedInt + " eating=" + c.isEating + " cold=" + c.isCold
                + " tag=" + c.tag + " parent=" + (c.transform.parent ? c.transform.parent.name : "-"));
            lines.Add("seq4 convo=" + AccessTools.Field(typeof(WatchTowerManager), "seq4ConvoDone").GetValue(tower) + " texts=" + AccessTools.Field(typeof(WatchTowerManager), "seq4TextsDone").GetValue(tower)
                + " cooked=" + tower.seq4FoodCooked + " eaten=" + tower.seq4FoodEaten + " inStove=" + tower.seq4CasseroleInsideStove + " ovenLocked=" + tower.seq4OvenLocked + " report=" + tower.seq4ReportDone);
            foreach (var tray in Object.FindObjectsOfType<Casserole>(true))
                lines.Add("tray " + ItemSync.PathOf(tray.transform) + " at=" + V(tray.transform.position) + " active=" + tray.gameObject.activeInHierarchy + " finished=" + tray.finishedInt + " key=" + Plugin.Instance?.Items.KeyOf(tray.gameObject) + " mine=" + (tray == c));
            if (tower.casseroleParent) lines.Add("casseroleParent " + ItemSync.PathOf(tower.casseroleParent) + " at=" + V(tower.casseroleParent.position));
            var rig = tower.playerInBedEating;
            if (rig) { var cam = rig.GetComponentInChildren<Camera>(true); lines.Add("eatingRig " + ItemSync.PathOf(rig.transform) + " at=" + V(rig.transform.position) + " yaw=" + rig.transform.eulerAngles.y.ToString("F0") + " active=" + rig.activeInHierarchy + " cam=" + (cam ? V(cam.transform.position) + " camYaw=" + cam.transform.eulerAngles.y.ToString("F0") : "-")); }
            if (tower.bed) lines.Add("bed at=" + V(tower.bed.transform.position) + " yaw=" + tower.bed.transform.eulerAngles.y.ToString("F0") + " bounds=" + (tower.bed.TryGetComponent<Collider>(out var bc) ? bc.bounds.ToString() : "-") + " bedCam=" + (tower.bedCam ? V(tower.bedCam.transform.position) : "-"));
            if (tower.insideStovePosition) lines.Add("oven at=" + V(tower.insideStovePosition.transform.position));
            if (tower.casseroleParentInsideFridge) lines.Add("fridgeSpot at=" + V(tower.casseroleParentInsideFridge.position) + " rot=" + V(tower.casseroleParentInsideFridge.eulerAngles) + " children=" + tower.casseroleParentInsideFridge.childCount);
            if (tower.casseroleColliderInsideFridge) lines.Add("fridgeCollider " + ItemSync.PathOf(tower.casseroleColliderInsideFridge.transform) + " bounds=" + tower.casseroleColliderInsideFridge.bounds + " enabled=" + tower.casseroleColliderInsideFridge.enabled + " trigger=" + tower.casseroleColliderInsideFridge.isTrigger);
            if (tower.casseroleColliderInsideMicrowave) lines.Add("microwaveSpot " + ItemSync.PathOf(tower.casseroleColliderInsideMicrowave.transform) + " bounds=" + tower.casseroleColliderInsideMicrowave.bounds + " enabled=" + tower.casseroleColliderInsideMicrowave.enabled + " trigger=" + tower.casseroleColliderInsideMicrowave.isTrigger + " in=" + tower.seq6CasseroleInsideMicrowave + " on=" + tower.seq6MicrowaveOn);
            foreach (var tray in Object.FindObjectsOfType<Casserole>(true))
                foreach (var r in tray.GetComponentsInChildren<MeshRenderer>()) { lines.Add("  trayBounds " + tray.name + "/" + r.name + " " + r.bounds); break; }
            foreach (var hit in Physics.RaycastAll(tower.casseroleParentInsideFridge.position + Vector3.up * 0.6f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                lines.Add("  fridge column hit " + hit.collider.name + " y=" + hit.point.y.ToString("F3"));
            if (tower.playerUnderBed)
            {
                var cam = tower.playerUnderBed.GetComponentInChildren<Camera>(true);
                lines.Add("underBed " + ItemSync.PathOf(tower.playerUnderBed.transform) + " at=" + V(tower.playerUnderBed.transform.position) + " rot=" + V(tower.playerUnderBed.transform.eulerAngles) + " active=" + tower.playerUnderBed.activeInHierarchy
                    + " cam=" + (cam ? V(cam.transform.position) + " fwd=" + V(cam.transform.forward) : "-") + " jumpscareAt=" + (tower.underBedJumpscarePostion ? V(tower.underBedJumpscarePostion.position) : "-")
                    + " viewPoint=" + (tower.underBedJumpscareViewPoint ? V(tower.underBedJumpscareViewPoint.transform.position) : "-"));
            }
        }
    }
}
