using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IronbarkCoop
{
    // The look picker's 3D view: the chosen body on a small lit stage far below the menu scene, drawn by its own camera
    // into a texture the menu shows. Drag turns it; the poses are the ones the partner sees in game.
    internal sealed class CharacterPreview : MonoBehaviour, IDragHandler
    {
        internal enum Pose { Stand, Walk, Run, Crouch, Sit, Lie }
        const int StageLayer = 31;
        static readonly Vector3 StageAt = new Vector3(0f, -4000f, 0f);

        GameObject stage, body;
        Camera view;
        RenderTexture target;
        ScPoseAnimator animator;
        string shown;
        float yaw = 20f;
        internal Pose Current = Pose.Stand;
        internal string Look;
        Vector3 focus;

        public static CharacterPreview Create(Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject("character preview", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<RawImage>();
            var preview = go.AddComponent<CharacterPreview>();
            preview.target = new RenderTexture(Mathf.RoundToInt(size.x * 2f), Mathf.RoundToInt(size.y * 2f), 24) { name = "Coop look preview", antiAliasing = 4 };
            image.texture = preview.target;
            return preview;
        }

        void BuildStage()
        {
            stage = new GameObject("Coop look stage");
            stage.transform.position = StageAt;
            var cameraGo = new GameObject("view");
            cameraGo.transform.SetParent(stage.transform, false);
            view = cameraGo.AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.035f, 0.04f, 0.05f, 1f);
            view.cullingMask = 1 << StageLayer;
            view.fieldOfView = 28f;
            view.nearClipPlane = 0.1f; view.farClipPlane = 20f;
            view.targetTexture = target;
            view.depth = -50;
            Light(new Vector3(35f, 150f, 0f), 1.15f, new Color(1f, 0.93f, 0.82f));
            Light(new Vector3(15f, -60f, 0f), 0.45f, new Color(0.65f, 0.75f, 1f));
            Light(new Vector3(-10f, 20f, 0f), 0.6f, new Color(0.85f, 0.9f, 1f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(1.4f, 0.01f, 1.4f);
            floor.layer = StageLayer;
            var material = new Material(Shader.Find("Standard")) { color = new Color(0.12f, 0.12f, 0.13f) };
            material.SetFloat("_Glossiness", 0.1f);
            floor.GetComponent<Renderer>().sharedMaterial = material;
        }

        void Light(Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject("light");
            go.transform.SetParent(stage.transform, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.cullingMask = 1 << StageLayer;
            light.shadows = LightShadows.None;
        }

        public void OnDrag(PointerEventData data) => yaw -= data.delta.x * 0.5f;

        void OnEnable() { if (view) view.enabled = true; }
        void OnDisable() { if (view) view.enabled = false; }

        void OnDestroy()
        {
            if (stage) Destroy(stage);
            if (target) target.Release();
        }

        void Update()
        {
            if (!stage) BuildStage();
            if (Look != shown) Show(Look);
            if (!body || animator == null) return;
            float dt = Time.unscaledDeltaTime;
            float speed = Current == Pose.Walk ? 1.45f : Current == Pose.Run ? 4.6f : 0f;
            animator.Update(dt, speed, Current == Pose.Crouch, Current == Pose.Sit, Current == Pose.Lie);
            body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            // Frame whatever the pose takes up (a lying body is long and low), a little from above.
            var bounds = new Bounds();
            bool any = false;
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!any) { bounds = renderer.bounds; any = true; } else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return;
            // On the stage in every pose: the lowest point of the body rests on the floor.
            body.transform.position += Vector3.up * Mathf.Clamp(StageAt.y - bounds.min.y, -0.05f, 0.05f);
            float fit = Mathf.Max(bounds.size.y / 2f, bounds.size.x / 2f * 1.6f, bounds.size.z / 2f * 1.2f, 0.5f);
            float distance = fit / Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            var from = bounds.center + new Vector3(0f, Current == Pose.Lie ? 0.75f : 0.22f, 1f).normalized * distance;
            focus = Vector3.Lerp(focus == Vector3.zero ? bounds.center : focus, bounds.center, dt * 5f);
            view.transform.position = Vector3.Lerp(view.transform.position == Vector3.zero ? from : view.transform.position, from, dt * 5f);
            view.transform.rotation = Quaternion.LookRotation(focus - view.transform.position);
        }

        void Show(string id)
        {
            shown = id;
            if (body) Destroy(body);
            body = null; animator = null;
            var template = ScRuntime.Template(id);
            if (!template) return;
            body = Instantiate(template, stage.transform, false);
            body.name = "look " + id;
            body.transform.localPosition = Vector3.zero;
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = StageLayer;
            body.SetActive(true);
            var unityAnimator = body.GetComponent<Animator>();
            if (!unityAnimator || !unityAnimator.avatar || !unityAnimator.avatar.isHuman) return;
            animator = new ScPoseAnimator(unityAnimator);
            bool female = ScRuntime.IsFemale(id);
            animator.Bind(ScPoseAnimator.Motion.Lie, female ? "sleeping tessa" : "Laying Sleeping");
            if (!animator.Has(ScPoseAnimator.Motion.Lie)) animator.Bind(ScPoseAnimator.Motion.Lie, female ? "Laying Sleeping" : "sleeping tessa");
            Plugin.Log("LOOK_PREVIEW " + id);
        }

        internal static string PoseName(Pose pose)
        {
            switch (pose)
            {
                case Pose.Walk: return ConnectionPanel.L("идёт", "walking");
                case Pose.Run: return ConnectionPanel.L("бежит", "running");
                case Pose.Crouch: return ConnectionPanel.L("присел", "crouching");
                case Pose.Sit: return ConnectionPanel.L("сидит", "sitting");
                case Pose.Lie: return ConnectionPanel.L("спит", "asleep");
                default: return ConnectionPanel.L("стоит", "standing");
            }
        }
    }
}
