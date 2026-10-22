using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IronbarkCoop
{
    // Lab-only UI inspection: hierarchy dump and a render of overlay canvases (a camera render alone skips them).
    internal static class LabUi
    {
        public static void Dump(string path)
        {
            var roots = new JArray();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    if (canvas.isRootCanvas) roots.Add(Node(canvas.transform, 0));
            File.WriteAllText(path, roots.ToString());
        }

        static JObject Node(Transform transform, int depth)
        {
            var node = new JObject { ["name"] = transform.name, ["active"] = transform.gameObject.activeSelf };
            var types = new JArray();
            foreach (var component in transform.GetComponents<Component>())
            {
                if (!component) continue;
                string type = component.GetType().Name;
                types.Add(type);
                if (component is Text text) { node["text"] = text.text; node["font"] = text.font ? text.font.name : ""; node["size"] = text.fontSize; node["color"] = text.color.ToString(); }
                if (type.StartsWith("TextMeshPro"))
                {
                    var t = component.GetType();
                    node["text"] = t.GetProperty("text")?.GetValue(component) as string;
                    var font = t.GetProperty("font")?.GetValue(component) as Object;
                    node["font"] = font ? font.name : "";
                    node["size"] = System.Convert.ToSingle(t.GetProperty("fontSize")?.GetValue(component) ?? 0f);
                    node["color"] = t.GetProperty("color")?.GetValue(component)?.ToString();
                }
                if (component is Button button)
                {
                    var calls = new JArray();
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        calls.Add((button.onClick.GetPersistentTarget(i) ? button.onClick.GetPersistentTarget(i).GetType().Name : "?") + "." + button.onClick.GetPersistentMethodName(i));
                    node["onClick"] = calls;
                }
                if (component is UnityEngine.EventSystems.EventTrigger trigger)
                {
                    var entries = new JArray();
                    foreach (var entry in trigger.triggers)
                        for (int i = 0; i < entry.callback.GetPersistentEventCount(); i++)
                            entries.Add(entry.eventID + ":" + (entry.callback.GetPersistentTarget(i) ? entry.callback.GetPersistentTarget(i).name + "/" + entry.callback.GetPersistentTarget(i).GetType().Name : "?") + "." + entry.callback.GetPersistentMethodName(i));
                    node["trigger"] = entries;
                }                if (component is Image image) { node["sprite"] = image.sprite ? image.sprite.name : ""; node["imageColor"] = image.color.ToString(); }
                if (component is Canvas c) { node["renderMode"] = c.renderMode.ToString(); node["worldCamera"] = c.worldCamera ? c.worldCamera.name : ""; node["sortingOrder"] = c.sortingOrder; node["scaleFactor"] = c.scaleFactor; }
                if (component is RectTransform rect) { node["pos"] = rect.anchoredPosition.ToString("F0"); node["rect"] = rect.rect.size.ToString("F0"); }
            }
            node["components"] = types;
            if (depth < 14)
            {
                var children = new JArray();
                foreach (Transform child in transform) children.Add(Node(child, depth + 1));
                if (children.Count > 0) node["children"] = children;
            }
            return node;
        }

        // Sends the same PointerDown a real click produces to the first active UI object with this name.
        public static bool Press(string name)
        {
            foreach (var trigger in Object.FindObjectsOfType<UnityEngine.EventSystems.EventTrigger>())
            {
                if (trigger.name != name || !trigger.gameObject.activeInHierarchy) continue;
                var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                UnityEngine.EventSystems.ExecuteEvents.Execute(trigger.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                return true;
            }
            return false;
        }
        public static void Capture(string path)
        {
            // A screen-space-camera canvas is drawn by its own camera, which need not be Camera.main.
            Camera camera = null;
            foreach (var c in Object.FindObjectsOfType<Canvas>()) if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceCamera && c.worldCamera) { camera = c.worldCamera; break; }
            if (!camera) camera = Camera.main;
            if (!camera) return;
            var changed = new List<(Canvas canvas, RenderMode mode, Camera previous, float distance)>();
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                changed.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.05f;
            }
            Canvas.ForceUpdateCanvases();
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(Screen.width, Screen.height, 24);
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                foreach (var entry in changed) { entry.canvas.renderMode = entry.mode; entry.canvas.worldCamera = entry.previous; entry.canvas.planeDistance = entry.distance; }
                target.Release();
                Object.Destroy(target);
                Object.Destroy(texture);
            }
        }
    }
}
