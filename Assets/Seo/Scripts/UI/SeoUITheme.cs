using TMPro;
using UnityEngine;

namespace Seo.UI
{
    [CreateAssetMenu(menuName = "Seo UI/Theme", fileName = "SeoUITheme")]
    public sealed class SeoUITheme : ScriptableObject
    {
        public Sprite PanelSprite;
        public Sprite ButtonSprite;
        public Sprite ButtonPressedSprite;
        public Texture2D BeltTexture;
        public Texture2D SplitterTexture;
        public Texture2D MergerTexture;
        public Texture2D CrossBeltTexture;
        public GameObject GeneratorPreviewPrefab;
        public GameObject TowerPreviewPrefab;
        public TMP_FontAsset FontAsset;

        public Color ScreenPanel = new Color(0.025f, 0.055f, 0.09f, 0.96f);
        public Color Card = new Color(0.04f, 0.1f, 0.15f, 0.96f);
        public Color Primary = new Color(0f, 0.82f, 0.95f, 1f);
        public Color Secondary = new Color(0.12f, 0.42f, 0.68f, 1f);
        public Color Muted = new Color(0.47f, 0.62f, 0.7f, 1f);
        public Color Text = new Color(0.93f, 0.98f, 1f, 1f);
        public Color Danger = new Color(0.95f, 0.24f, 0.22f, 1f);
        public Color Success = new Color(0.2f, 0.88f, 0.58f, 1f);
        public Color Warning = new Color(1f, 0.68f, 0.12f, 1f);

        private static SeoUITheme cached;

        public static SeoUITheme Current
        {
            get
            {
                if (cached == null) cached = Resources.Load<SeoUITheme>("SeoUITheme");
                if (cached == null) cached = CreateInstance<SeoUITheme>();
                return cached;
            }
        }
    }
}
