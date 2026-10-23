using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronbarkCoop
{
    // A short list of tasks in the top-left corner (playtest sketch): a box per task, ticked when done, in the font of
    // the game's own control hints. Used for the chores of whoever stays in the tower while the partner is away.
    internal static class TaskList
    {
        sealed class Row { public GameObject Root; public TextMeshProUGUI Text; public GameObject Tick; public string Shown; public bool Done; }

        static GameObject canvas;
        static RectTransform column;
        static readonly List<Row> rows = new List<Row>();
        const float RowHeight = 34f, Box = 18f, Line = 2f;

        public static bool Visible => canvas && canvas.activeSelf;
        public static string Debug { get { var parts = new List<string>(); foreach (var row in rows) if (row.Root.activeSelf) parts.Add((row.Done ? "x" : "o")); return parts.Count == 0 ? "-" : string.Join("", parts); } }

        // Shows the tasks (text, done); an empty list hides the panel.
        public static void Show(IList<(string text, bool done)> tasks)
        {
            if (tasks == null || tasks.Count == 0) { Hide(); return; }
            if (!canvas && !Build()) return;
            canvas.SetActive(true);
            while (rows.Count < tasks.Count) rows.Add(NewRow());
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool used = i < tasks.Count;
                row.Root.SetActive(used);
                if (!used) continue;
                var (text, done) = tasks[i];
                if (row.Shown != text || row.Done != done)
                {
                    row.Shown = text; row.Done = done;
                    row.Text.text = text;
                    row.Text.color = done ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.92f);
                    row.Tick.SetActive(done);
                }
            }
        }

        public static void Hide() { if (canvas) canvas.SetActive(false); }

        static bool Build()
        {
            var ui = GameManager.instance ? GameManager.instance.uIManager : null;
            if (!ui || !ui.controlsText) return false;
            canvas = new GameObject("Coop task list");
            Object.DontDestroyOnLoad(canvas);
            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 40;
            var scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            column = new GameObject("Tasks", typeof(RectTransform)).GetComponent<RectTransform>();
            column.SetParent(canvas.transform, false);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(48f, -44f);
            column.sizeDelta = new Vector2(620f, 200f);
            font = ui.controlsText.font;
            fontMaterial = ui.controlsText.fontSharedMaterial;
            rows.Clear();
            return true;
        }

        static TMP_FontAsset font;
        static Material fontMaterial;

        static Row NewRow()
        {
            var row = new Row();
            var root = new GameObject("Task " + rows.Count, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(column, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(0f, -RowHeight * rows.Count);
            root.sizeDelta = new Vector2(620f, RowHeight);
            row.Root = root.gameObject;
            // The box: four thin sides, with a soft shadow behind like the game's hint text.
            var box = new GameObject("Box", typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(root, false);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = new Vector2(0f, 0f);
            box.sizeDelta = new Vector2(Box, Box);
            Bar(box, new Vector2(0f, Box - Line), new Vector2(Box, Line));
            Bar(box, new Vector2(0f, 0f), new Vector2(Box, Line));
            Bar(box, new Vector2(0f, 0f), new Vector2(Line, Box));
            Bar(box, new Vector2(Box - Line, 0f), new Vector2(Line, Box));
            // The tick: two strokes.
            var tick = new GameObject("Tick", typeof(RectTransform)).GetComponent<RectTransform>();
            tick.SetParent(box, false);
            tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0f, 0f);
            tick.sizeDelta = new Vector2(Box, Box);
            Stroke(tick, new Vector2(4f, 9f), 8f, -45f);
            Stroke(tick, new Vector2(8f, 5f), 15f, 52f);
            row.Tick = tick.gameObject;
            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            var rect = text.rectTransform;
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0f, 0f); rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(Box + 12f, 0f); rect.offsetMax = Vector2.zero;
            if (font) text.font = font;
            if (fontMaterial) text.fontSharedMaterial = fontMaterial;
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            row.Text = text;
            return row;
        }

        static void Bar(RectTransform parent, Vector2 at, Vector2 size)
        {
            var image = new GameObject("Side", typeof(RectTransform)).AddComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0.9f);
            var r = image.rectTransform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
            r.anchoredPosition = at; r.sizeDelta = size;
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f); shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        static void Stroke(RectTransform parent, Vector2 from, float length, float angle)
        {
            var image = new GameObject("Stroke", typeof(RectTransform)).AddComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.white;
            var r = image.rectTransform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = from; r.sizeDelta = new Vector2(length, 3f);
            r.localEulerAngles = new Vector3(0f, 0f, angle);
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f); shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }
}
