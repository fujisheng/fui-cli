using UnityEngine;
namespace FUI.Cli.Layout
{
    /// <summary>Keep controls in the device safe area. Wide screens retain the authored content
    /// width; short screens fit the minimum usable height instead of clipping bottom actions.</summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class WebSafeArea : MonoBehaviour
    {
        public float referenceWidth = 941;
        public float minimumHeight = 1672;
        void OnEnable() => Refresh();
        void LateUpdate() => Refresh();
        public void Refresh()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null || Screen.width <= 0 || Screen.height <= 0) return;
            Apply(Screen.safeArea, new Vector2(Screen.width, Screen.height), parent.rect.size);
        }
        // Explicit inputs also allow deterministic safe-area tests without faking Screen state.
        public void Apply(Rect safe, Vector2 screen, Vector2 parentSize)
        {
            if (screen.x <= 0 || screen.y <= 0 || parentSize.x <= 0 || parentSize.y <= 0) return;
            var available = Vector2.Scale(safe.size / screen, parentSize);
            float scale = Mathf.Min(1, available.x / referenceWidth, available.y / minimumHeight);
            if (scale <= 0) return;
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
            rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = Vector2.Scale(safe.center / screen - Vector2.one * .5f, parentSize);
            rect.sizeDelta = new Vector2(referenceWidth, available.y / scale);
            rect.localScale = Vector3.one * scale;
        }
    }
}
