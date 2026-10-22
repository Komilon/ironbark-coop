using DG.Tweening;
using UnityEngine;

namespace IronbarkCoop
{
    // Scratch Creek's PlayerTalkingIcon behaviour: billboard 0.3 m above the head bone, pops in (scale 0->1, 0.1 s),
    // bobs (local Y 0.011, InOutElastic, 0.2 s yoyo) while talking, shrinks away (0.1 s) when silent.
    internal sealed class TalkingIcon : MonoBehaviour
    {
        const float AboveHead = 0.3f;
        Transform visual;
        Tween bob, scale;
        bool showing;
        internal static Sprite IconSprite;

        public static TalkingIcon Create(Transform parent)
        {
            var root = new GameObject("Talking icon");
            root.transform.SetParent(parent, false);
            var icon = root.AddComponent<TalkingIcon>();
            icon.visual = new GameObject("Visual").transform;
            icon.visual.SetParent(root.transform, false);
            var renderer = icon.visual.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = IconSprite ? IconSprite : DefaultSprite();
            // Muted so the game's bloom does not turn it into a glare.
            renderer.color = new Color(0.62f, 0.62f, 0.6f, 0.85f);
            icon.visual.localScale = Vector3.zero;
            return icon;
        }

        public void Place(Vector3 headPosition)
        {
            transform.position = headPosition + Vector3.up * AboveHead;
            var camera = Camera.main;
            if (camera) transform.LookAt(transform.position + (transform.position - camera.transform.position));
        }

        public void SetTalking(bool talking)
        {
            if (talking == showing) return;
            showing = talking;
            bob?.Kill(); scale?.Kill();
            if (talking)
            {
                visual.localPosition = Vector3.zero;
                scale = visual.DOScale(Vector3.one * 0.18f, 0.1f).SetUpdate(true);
                bob = visual.DOLocalMoveY(0.011f, 0.2f).SetEase(Ease.InOutElastic).From(0f).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
            }
            else scale = visual.DOScale(Vector3.zero, 0.1f).SetUpdate(true);
        }

        void OnDestroy() { bob?.Kill(); scale?.Kill(); }

        // Stand-in until the Scratch Creek icon texture is loaded: a speech bubble with three dots.
        static Sprite fallback;
        static Sprite DefaultSprite()
        {
            if (fallback) return fallback;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Rounded bubble body with a small tail at the bottom left.
                    float dx = (x - 32f) / 28f, dy = (y - 38f) / 20f;
                    bool body = dx * dx + dy * dy <= 1f;
                    bool tail = y >= 12 && y <= 22 && x >= 14 && x <= 14 + (22 - y);
                    bool dot = false;
                    foreach (int cx in new[] { 20, 32, 44 }) if ((x - cx) * (x - cx) + (y - 38) * (y - 38) <= 16) dot = true;
                    texture.SetPixel(x, y, (body || tail) && !dot ? new Color(0.95f, 0.95f, 0.92f, 0.9f) : clear);
                }
            texture.Apply();
            fallback = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return fallback;
        }
    }

    // Analytic two-bone IK (shoulder-elbow-hand), applied after animation like Scratch Creek's hand IK.
    internal static class ArmIK
    {
        public static void Reach(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 fallbackAxis)
        {
            if (!upper || !lower || !hand) return;
            Vector3 a = upper.position, b = lower.position, c = hand.position;
            float ab = (b - a).magnitude, bc = (c - b).magnitude;
            float at = Mathf.Clamp((target - a).magnitude, 0.01f, ab + bc - 0.002f);
            // Elbow: open or close to the angle that makes the arm exactly as long as the reach.
            float current = Vector3.Angle(a - b, c - b);
            float wanted = Mathf.Acos(Mathf.Clamp((at * at - ab * ab - bc * bc) / (-2f * ab * bc), -1f, 1f)) * Mathf.Rad2Deg;
            var axis = Vector3.Cross(a - b, c - b);
            axis = axis.sqrMagnitude < 1e-6f ? fallbackAxis : axis.normalized;
            lower.rotation = Quaternion.AngleAxis(wanted - current, axis) * lower.rotation;
            // Shoulder: swing the whole arm so the hand lands on the target.
            upper.rotation = Quaternion.FromToRotation(hand.position - a, target - a) * upper.rotation;
        }
    }

    // A visual copy of the game's own flashlight model for the partner's hand.
    internal static class TorchModel
    {
        public static GameObject Create(Transform parent)
        {
            var source = Source();
            if (!source) return null;
            var holder = new GameObject("Torch holder");
            holder.SetActive(false);
            var copy = Object.Instantiate(source.gameObject, holder.transform);
            for (int pass = 0; pass < 3; pass++)
                foreach (var component in copy.GetComponentsInChildren<Component>(true))
                    if (!(component is Transform || component is MeshFilter || component is MeshRenderer))
                        try { Object.DestroyImmediate(component); } catch { }
            // The source may be a stand-in the scene keeps switched off (the ranger hands it over later): show it all.
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            var renderers = copy.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var renderer in renderers) renderer.enabled = true;
            copy.name = "Partner torch";
            copy.transform.SetParent(parent, false);
            // Drawn like the avatar: the source may sit on a layer only the first-person view of its owner shows.
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
            copy.transform.localScale = source.transform.lossyScale;
            Object.Destroy(holder);
            Plugin.Log("AVATAR_TORCH from " + ItemSync.PathOf(source.transform) + " renderers=" + renderers.Length + " scale=" + source.transform.lossyScale.ToString("F3"));
            // Nothing to see: try again later rather than keep an invisible torch for the whole scene.
            if (renderers.Length == 0 || source.transform.lossyScale.sqrMagnitude < 1e-6f) { Object.Destroy(copy); return null; }
            return copy;
        }

        // The flashlight model to copy: preferably our own (in hand or handed out), else any with a visible model.
        internal static FlashLight Source()
        {
            var pickup = GameManager.instance ? GameManager.instance.itemPickUp : null;
            foreach (var go in new[] { pickup ? pickup.flashLightItem : null, pickup ? pickup.initialFlashLightInHand : null })
                if (go && go.TryGetComponent<FlashLight>(out var own) && go.GetComponentsInChildren<MeshRenderer>(true).Length > 0) return own;
            FlashLight fallback = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<FlashLight>())
            {
                if (!candidate.gameObject.scene.IsValid() || candidate.GetComponentsInChildren<MeshRenderer>(true).Length == 0) continue;
                if (candidate.gameObject.activeInHierarchy) return candidate;
                if (!fallback) fallback = candidate;
            }
            return fallback;
        }
    }
}
