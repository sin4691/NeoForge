using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Seo.UI
{
    // Fill the display while keeping the authored logo and menu together.
    internal sealed class TitleCompositionFitter : MonoBehaviour
    {
        private RectTransform composition;
        private RectTransform canvasRect;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name != "Title") return;
            var canvas = GameObject.Find("Canvas");
            if (canvas != null && canvas.GetComponent<TitleCompositionFitter>() == null)
                canvas.AddComponent<TitleCompositionFitter>();
        }

        private void Awake()
        {
            canvasRect = (RectTransform)transform;
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = SeoUIFactory.LandscapeReference;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            var children = new List<Transform>();
            foreach (Transform child in transform) children.Add(child);
            composition = new GameObject("SeoTitleComposition", typeof(RectTransform)).GetComponent<RectTransform>();
            composition.SetParent(transform, false);
            SeoUIFactory.SetRect(composition, Vector2.one * 0.5f, Vector2.one * 0.5f,
                Vector2.one * 0.5f, Vector2.zero, new Vector2(2152.366f, 1310f));
            foreach (var child in children) child.SetParent(composition, false);
            // Preserve the authored 1920-wide menu coordinates inside the wider artwork.
            foreach (var child in children)
            {
                if (!(child is RectTransform rect)) continue;
                rect.anchoredPosition -= new Vector2((rect.anchorMin.x - 0.5f) * 232.366f + 55.452f, 0f);
            }
            var background = composition.Find("Background")?.GetComponent<Image>();
            if (background != null && background.sprite != null)
            {
                // The scene's authored rectangle stretches the artwork vertically.
                // Use the sprite ratio and preserve the menu's relative vertical positions.
                float height = composition.sizeDelta.x * background.sprite.rect.height / background.sprite.rect.width;
                foreach (var child in children)
                {
                    if (child is RectTransform rect)
                        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                            rect.anchoredPosition.y * height / composition.sizeDelta.y);
                }
                composition.sizeDelta = new Vector2(composition.sizeDelta.x, height);
                SeoUIFactory.SetRect(background.rectTransform, Vector2.zero, Vector2.one,
                    Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            }
        }

        private void LateUpdate()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            float fit = Mathf.Max(canvasRect.rect.width / composition.sizeDelta.x,
                canvasRect.rect.height / composition.sizeDelta.y);
            composition.localScale = Vector3.one * fit;

            Vector2 size = composition.sizeDelta * fit;
            Rect safe = Screen.safeArea;
            Vector2 viewport = canvasRect.rect.size;
            Vector2 safeMin = new Vector2(safe.xMin / Screen.width * viewport.x,
                safe.yMin / Screen.height * viewport.y) - viewport * 0.5f;
            Vector2 safeMax = new Vector2(safe.xMax / Screen.width * viewport.x,
                safe.yMax / Screen.height * viewport.y) - viewport * 0.5f;

            // Normalized bounds of the baked-in logo and the four menu buttons,
            // including their decorative indicators and a small surrounding margin.
            composition.anchoredPosition = new Vector2(
                FitOffset(size.x, viewport.x, safeMin.x, safeMax.x, 0.04f, 0.47f),
                FitOffset(size.y, viewport.y, safeMin.y, safeMax.y, 0.20f, 0.84f));
        }

        private static float FitOffset(float size, float viewport, float safeMin, float safeMax,
            float contentMin, float contentMax)
        {
            float overflow = Mathf.Max(0f, (size - viewport) * 0.5f);
            float min = Mathf.Max(-overflow, safeMin - (contentMin - 0.5f) * size);
            float max = Mathf.Min(overflow, safeMax - (contentMax - 0.5f) * size);
            // Prefer the centered composition; only pan when essential content would be clipped.
            return min <= max ? Mathf.Clamp(0f, min, max)
                : Mathf.Clamp((min + max) * 0.5f, -overflow, overflow);
        }
    }

    public static class SeoUIFactory
    {
        public static readonly Vector2 LandscapeReference = new Vector2(1920f, 1080f);

        public static Image CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var image = go.GetComponent<Image>();
            ApplyPanel(image, color);
            return image;
        }

        public static TMP_Text CreateTMPText(Transform parent, string name, string value, int fontSize,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = SeoUITheme.Current.FontAsset;
            text.fontSize = fontSize;
            text.fontStyle = ToTmpStyle(style);
            text.alignment = ToTmpAlignment(alignment);
            text.color = SeoUITheme.Current.Text;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        public static Button CreateTMPButton(Transform parent, string name, string label, UnityAction action,
            Color? tint = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var button = go.GetComponent<Button>();
            ApplyButton(button, tint);
            CreateTMPText(go.transform, "Label", label, 19, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        public static Button CreateButton(Transform parent, string name, string label, UnityAction action,
            Color? tint = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var button = go.GetComponent<Button>();
            ApplyButton(button, tint);
            CreateText(go.transform, "Label", label, 19, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        public static void ApplyPanel(Image image, Color? color = null)
        {
            if (image == null) return;
            var theme = SeoUITheme.Current;
            image.sprite = theme.PanelSprite;
            image.type = theme.PanelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color ?? theme.ScreenPanel;
        }

        public static void ApplyButton(Button button, Color? tint = null)
        {
            if (button == null) return;
            var theme = SeoUITheme.Current;
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = theme.ButtonSprite;
                image.type = theme.ButtonSprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = tint ?? Color.white;
                button.targetGraphic = image;
            }

            button.transition = Selectable.Transition.SpriteSwap;
            var state = button.spriteState;
            state.highlightedSprite = theme.ButtonPressedSprite;
            state.pressedSprite = theme.ButtonPressedSprite;
            state.selectedSprite = theme.ButtonPressedSprite;
            button.spriteState = state;

            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.font = theme.FontAsset;
                label.color = theme.Text;
                label.fontStyle = FontStyles.Bold;
                label.raycastTarget = false;
                return;
            }

            var legacyLabel = button.GetComponentInChildren<Text>(true);
            if (legacyLabel != null)
            {
                legacyLabel.color = theme.Text;
                legacyLabel.fontStyle = FontStyle.Bold;
                legacyLabel.raycastTarget = false;
            }
        }

        public static void SetResearchLocked(Button button, bool locked)
        {
            if (button == null) return;
            button.interactable = !locked;
            var group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = !locked;
            group.blocksRaycasts = true;

            var badge = button.transform.Find("ResearchLockBadge");
            if (badge != null) badge.gameObject.SetActive(false);
            var visual = button.GetComponent<ResearchLockedVisual>();
            if (visual == null && locked) visual = button.gameObject.AddComponent<ResearchLockedVisual>();
            if (visual != null) visual.SetLocked(locked);
        }

        public static Text CreateText(Transform parent, string name, string value, int fontSize,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = SeoUITheme.Current.Text;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor alignment)
        {
            switch (alignment)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Left;
            }
        }

        private static FontStyles ToTmpStyle(FontStyle style)
        {
            switch (style)
            {
                case FontStyle.Bold: return FontStyles.Bold;
                case FontStyle.Italic: return FontStyles.Italic;
                case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
                default: return FontStyles.Normal;
            }
        }

        public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            if (rt == null) return;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
        }
    }

    internal sealed class ResearchLockedVisual : MonoBehaviour
    {
        private static Material grayscaleMaterial;
        private readonly Dictionary<Image, Material> originalMaterials = new Dictionary<Image, Material>();
        private readonly Dictionary<TMP_Text, Color> originalTextColors = new Dictionary<TMP_Text, Color>();
        private readonly List<Image> images = new List<Image>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();

        public void SetLocked(bool locked)
        {
            if (!locked)
            {
                foreach (var entry in originalMaterials)
                    if (entry.Key != null) entry.Key.material = entry.Value;
                foreach (var entry in originalTextColors)
                    if (entry.Key != null) entry.Key.color = entry.Value;
                originalMaterials.Clear();
                originalTextColors.Clear();
                return;
            }

            if (grayscaleMaterial == null)
            {
                var shader = Resources.Load<Shader>("ResearchGrayscale");
                if (shader != null)
                    grayscaleMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            GetComponentsInChildren(true, images);
            foreach (var image in images)
            {
                if (image.transform == transform || grayscaleMaterial == null) continue;
                if (!originalMaterials.ContainsKey(image)) originalMaterials.Add(image, image.material);
                image.material = grayscaleMaterial;
            }

            GetComponentsInChildren(true, labels);
            foreach (var label in labels)
            {
                if (!originalTextColors.ContainsKey(label)) originalTextColors.Add(label, label.color);
                label.color = new Color(0.55f, 0.55f, 0.55f, originalTextColors[label].a);
            }
        }
    }
}
