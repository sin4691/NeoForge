using Choi.SaveLoad;
using Factory.Building;
using Factory.Data;
using Factory.Simulation;
using Factory.UI;
using Seo.Building;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Seo.UI
{
    // 팀의 배치/전력/저장 로직은 그대로 두고, 이미 존재하는 버튼을 Seo 전용 HUD로 재배치한다.
    public sealed class FactoryHudController : MonoBehaviour
    {
        private enum Category { Production, Logistics, Power, System }

        private sealed class CoreResourceEntry
        {
            public int ResourceId;
            public GameObject Root;
            public Text Name;
            public Image Icon;
            public Text Amount;
        }

        private sealed class PlacementCostEntry
        {
            public int ResourceId;
            public int Required;
            public GameObject Root;
            public Image Background;
            public Text Amount;
        }

        private Canvas canvas;
        private RectTransform safeRoot;
        private GameObject productionPage;
        private GameObject logisticsPage;
        private GameObject powerPage;
        private GameObject systemPage;
        private GameObject dockRoot;
        private GameObject sideMenuRoot;
        private Vector2 lastSafeSize;
        private float lastCanvasScale;
        private int lastNavigationCount;
        private Button productionTab;
        private Button logisticsTab;
        private Button powerTab;
        private Button systemTab;
        private Button editModeButton;
        private Text categoryTitle;
        private Category? openCategory;
        private GameObject rotateButton;
        private GameObject confirmButton;
        private GameObject demolishConfirmButton;
        private Button groupMoveButton;
        private Button groupMoveRotateButton;
        private GameObject cancelButton;
        private RectTransform contextBarRect;
        private float contextButtonStep = 232f;
        private const float ContextBottomMargin = 12f;
        private const float ContextPanelGap = 8f;
        private GameObject placementCostPanel;
        private Transform placementCostContent;
        private Text placementCostTitle;
        private Button coreResourceButton;
        private Button powerStatusButton;
        private Text rewardedAdLabel;
        private Button rewardedAdButton;
        private GameObject coreResourcePanel;
        private GameObject powerDetailPanel;
        private GameObject exitDialogRoot;
        private Text exitDialogTitle;
        private Text exitDialogConfirmLabel;
        private bool returnToTitle;
        private Transform coreResourceContent;
        private RectTransform coreResourceViewport;
        private ScrollRect coreResourceScroll;
        private Text coreResourceTitle;
        private RectTransform coreResourceClose;
        private RectTransform coreResourceScrollbar;
        private Text coreResourceEmptyText;
        private Text powerText;
        private Text toastText;
        private GameObject toastRoot;
        private BuildInputRouter buildRouter;
        private MachineGhostTool machineTool;
        // 매 프레임 씬 전체를 뒤지지 않도록 한 번 찾은 참조를 들고 있는다 — 공장이 커지면
        // FindFirstObjectByType/GameObject.Find 비용이 오브젝트 수에 비례해서 프레임을 크게 잡아먹었다.
        private PowerGridSystem powerGrid;
        private PowerBuildController powerController;
        private GameObject legacyPowerPanel;
        private GameObject legacyPowerPanelToggle;
        private float nextLegacyPanelSearch;
        private string lastPowerText;
        private SimulationDriver simulationDriver;
        private BeltDragTool beltTool;
        private string pendingPlacementMachineId;
        private string placementCostSignature;
        private bool editModeActive;
        private float toastUntil;
        private float nextCoreResourceRefresh;
        private float nextDiscovery;
        private bool built;
        private static readonly Color ToolCardIdleColor = new Color(0.10f, 0.18f, 0.20f, 1f);
        private static readonly FieldInfo BeltCostPerTileField = typeof(BeltDragTool).GetField(
            "concreteCostPerTile", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CableCostField = typeof(PowerBuildController).GetField(
            "CableCopperWireCost", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly Dictionary<Texture2D, Sprite> ToolTextureSprites =
            new Dictionary<Texture2D, Sprite>();
        private static readonly Dictionary<string, Sprite> NavigationIconSprites =
            new Dictionary<string, Sprite>();
        private readonly List<CoreResourceEntry> coreResourceEntries = new List<CoreResourceEntry>();
        private readonly List<PlacementCostEntry> placementCostEntries = new List<PlacementCostEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            SceneManager.sceneLoaded -= CreateRuntimeInstance;
            SceneManager.sceneLoaded += CreateRuntimeInstance;
        }

        private static void CreateRuntimeInstance(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main") return;
            if (FindFirstObjectByType<FactoryHudController>() != null) return;
            new GameObject("[Seo] Factory HUD").AddComponent<FactoryHudController>();
        }

        private void Update()
        {
            if (!built && Time.unscaledTime >= nextDiscovery)
            {
                built = TryBuild();
                nextDiscovery = Time.unscaledTime + 0.25f;
            }

            if (!built) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                HandleBackPressed();
            TrackActivePlacement();
            UpdatePlacementCostPanel();
            UpdateContextActions();
            UpdatePowerStatus();
            DecorateRecipePanel();
            HideLegacyPowerPanel();
            UpdateRewardedAdLabel();

            if (coreResourcePanel != null && coreResourcePanel.activeSelf
                && Time.unscaledTime >= nextCoreResourceRefresh)
            {
                RefreshCoreResourcePanel();
                nextCoreResourceRefresh = Time.unscaledTime + 0.25f;
            }

            if (toastRoot != null) toastRoot.SetActive(Time.unscaledTime < toastUntil);
        }

        private bool TryBuild()
        {
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject == null) return false;
            canvas = canvasObject.GetComponent<Canvas>();
            if (canvas == null) return false;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = SeoUIFactory.LandscapeReference;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            safeRoot = EnsureSafeRoot(canvas.transform);
            buildRouter = FindFirstObjectByType<BuildInputRouter>();
            machineTool = FindFirstObjectByType<MachineGhostTool>();
            BuildTopHud();
            BuildBottomDock();
            DecorateRecipePanel();
            return true;
        }

        private static RectTransform EnsureSafeRoot(Transform canvasTransform)
        {
            var existing = canvasTransform.Find("SafeArea");
            var go = existing != null ? existing.gameObject : new GameObject("SafeArea", typeof(RectTransform));
            if (existing == null) go.transform.SetParent(canvasTransform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (go.GetComponent<SafeAreaFitter>() == null) go.AddComponent<SafeAreaFitter>();
            return rt;
        }

        private void ShowRewardedAd()
        {
            if (Bae.SpeedBuffManager.Instance != null && Bae.SpeedBuffManager.Instance.isBuffActive) return;
            var adManager = FindFirstObjectByType<Bae.LevelPlayManager>();
            if (adManager == null || Bae.SpeedBuffManager.Instance == null)
            {
                ShowToast("광고 보상 매니저를 찾을 수 없습니다");
                return;
            }

            adManager.ShowSpeedBuffAd();
        }

        private void UpdateRewardedAdLabel()
        {
            if (rewardedAdLabel == null) return;
            var buff = Bae.SpeedBuffManager.Instance;
            bool active = buff != null && buff.isBuffActive;
            if (rewardedAdButton != null) rewardedAdButton.interactable = !active;
            rewardedAdLabel.color = active ? SeoUITheme.Current.Muted : Color.white;
            string label = active
                ? $"생산 {buff.CurrentSpeedMultiplier:0.#}배 적용 중\n남은 시간 {Mathf.CeilToInt(Mathf.Max(0f, buff.buffTimeRemaining))}초"
                : "광고 보기\n60초 동안 생산 2배";
            if (rewardedAdLabel.text != label) rewardedAdLabel.text = label;
        }

        private void BuildTopHud()
        {
            BuildNavigationRail();
            var line1 = GameObject.Find("HudLine1");
            var line2 = GameObject.Find("HudLine2");
            if (line1 != null) line1.SetActive(false);
            if (line2 != null) line2.SetActive(false);

            coreResourceButton = CreateRailButton(sideMenuRoot.transform, "SeoCoreResourceButton", "자원", "resource",
                0, ToggleCoreResourcePanel);
            powerStatusButton = CreateRailButton(sideMenuRoot.transform, "SeoPowerStatusButton", "전력 현황", "power_status",
                1, TogglePowerDetailPanel);
            CreateRailButton(sideMenuRoot.transform, "SeoFocusCoreButton", "코어 이동", "focus",
                2, FocusCore);
            rewardedAdButton = SeoUIFactory.CreateTMPButton(safeRoot, "SeoRewardedAdButton",
                "광고 보기\n60초 동안 생산 2배", ShowRewardedAd, ToolCardIdleColor);
            SeoUIFactory.SetRect(rewardedAdButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                Vector2.one, new Vector2(-28f, -24f), new Vector2(290f, 94f));
            rewardedAdButton.transition = Selectable.Transition.None;
            rewardedAdLabel = rewardedAdButton.GetComponentInChildren<Text>(true);
            rewardedAdLabel.fontSize = 22;
            rewardedAdLabel.color = Color.white;
            SeoUIFactory.SetRect(rewardedAdLabel.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-28f, -16f));

            var resourceCard = SeoUIFactory.CreatePanel(safeRoot, "SeoResourceCard", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 720f));
            resourceCard.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            coreResourcePanel = resourceCard.gameObject;
            CreateCardAccent(resourceCard.transform, SeoUITheme.Current.Primary);
            var resourceTitle = SeoUIFactory.CreateTMPText(resourceCard.transform, "Title", "코어 보유 자원", 24,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(resourceTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(28f, -12f), new Vector2(-120f, 42f));
            resourceTitle.color = SeoUITheme.Current.Primary;
            coreResourceTitle = resourceTitle;
            var resourceClose = SeoUIFactory.CreateTMPButton(resourceCard.transform, "Close", "×",
                ToggleCoreResourcePanel);
            SeoUIFactory.SetRect(resourceClose.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                Vector2.one, new Vector2(-18f, -16f), new Vector2(68f, 58f));
            var resourceCloseLabel = resourceClose.GetComponentInChildren<Text>(true);
            if (resourceCloseLabel != null) resourceCloseLabel.fontSize = 38;
            coreResourceClose = resourceClose.GetComponent<RectTransform>();
            var resourceViewport = new GameObject("ResourceViewport", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            resourceViewport.transform.SetParent(resourceCard.transform, false);
            coreResourceViewport = resourceViewport.GetComponent<RectTransform>();
            coreResourceViewport.anchorMin = Vector2.zero;
            coreResourceViewport.anchorMax = Vector2.one;
            coreResourceViewport.pivot = new Vector2(0.5f, 0.5f);
            coreResourceViewport.offsetMin = new Vector2(18f, 18f);
            coreResourceViewport.offsetMax = new Vector2(-42f, -66f);
            var viewportImage = resourceViewport.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

            var resourceContentObject = new GameObject("ResourceContent", typeof(RectTransform));
            resourceContentObject.transform.SetParent(resourceViewport.transform, false);
            var resourceContentRect = resourceContentObject.GetComponent<RectTransform>();
            resourceContentRect.anchorMin = new Vector2(0f, 1f);
            resourceContentRect.anchorMax = new Vector2(1f, 1f);
            resourceContentRect.pivot = new Vector2(0.5f, 1f);
            resourceContentRect.anchoredPosition = Vector2.zero;
            resourceContentRect.sizeDelta = Vector2.zero;
            coreResourceContent = resourceContentObject.transform;

            var scrollbarObject = new GameObject("ResourceScrollbar", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(resourceCard.transform, false);
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            coreResourceScrollbar = scrollbarRect;
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-30f, 18f);
            scrollbarRect.offsetMax = new Vector2(-18f, -66f);
            scrollbarObject.GetComponent<Image>().color = new Color(0.02f, 0.08f, 0.10f, 0.88f);

            var slidingArea = new GameObject("SlidingArea", typeof(RectTransform));
            slidingArea.transform.SetParent(scrollbarObject.transform, false);
            var slidingRect = slidingArea.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = Vector2.zero;
            slidingRect.offsetMax = Vector2.zero;

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handleObject.transform.SetParent(slidingArea.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handleObject.GetComponent<Image>().color = SeoUITheme.Current.Primary;

            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObject.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            coreResourceScroll = resourceViewport.GetComponent<ScrollRect>();
            coreResourceScroll.viewport = coreResourceViewport;
            coreResourceScroll.content = resourceContentRect;
            coreResourceScroll.horizontal = false;
            coreResourceScroll.vertical = true;
            coreResourceScroll.movementType = ScrollRect.MovementType.Clamped;
            coreResourceScroll.inertia = true;
            coreResourceScroll.decelerationRate = 0.12f;
            coreResourceScroll.scrollSensitivity = 38f;
            coreResourceScroll.verticalScrollbar = scrollbar;
            coreResourceScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            coreResourceScroll.verticalNormalizedPosition = 1f;

            coreResourceEmptyText = SeoUIFactory.CreateTMPText(resourceCard.transform, "Empty", "보유 자원이 없습니다", 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(coreResourceEmptyText.rectTransform, new Vector2(0f, 0f), Vector2.one,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(-40f, -70f));
            coreResourceEmptyText.color = SeoUITheme.Current.Muted;
            coreResourcePanel.SetActive(false);

            var powerCard = SeoUIFactory.CreatePanel(safeRoot, "SeoPowerCard", new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(166f, -118f), new Vector2(580f, 142f));
            powerCard.rectTransform.pivot = new Vector2(0f, 1f);
            powerDetailPanel = powerCard.gameObject;
            CreateCardAccent(powerCard.transform, SeoUITheme.Current.Warning);
            var powerTitle = SeoUIFactory.CreateTMPText(powerCard.transform, "PowerTitle", "공장 전력 현황", 23,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(powerTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -14f), new Vector2(500f, 34f));
            powerTitle.color = SeoUITheme.Current.Warning;

            powerText = SeoUIFactory.CreateTMPText(powerCard.transform, "PowerStatus", "전력 시스템 연결 중", 21,
                TextAnchor.UpperLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(powerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -54f), new Vector2(480f, 76f));
            powerText.richText = true;
            powerText.enableAutoSizing = true;
            powerText.fontSizeMin = 16;
            powerText.fontSizeMax = 21;
            powerText.textWrappingMode = TMPro.TextWrappingModes.Normal;
            powerText.overflowMode = TMPro.TextOverflowModes.Truncate;
            powerDetailPanel.SetActive(false);

            var toastPanel = SeoUIFactory.CreatePanel(safeRoot, "SeoToast", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(460f, 52f),
                new Color(0.02f, 0.12f, 0.18f, 0.96f));
            toastPanel.rectTransform.pivot = new Vector2(0.5f, 1f);
            toastRoot = toastPanel.gameObject;
            toastText = SeoUIFactory.CreateTMPText(toastPanel.transform, "Label", string.Empty, 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            toastRoot.SetActive(false);

            BuildExitDialog();
        }

        private void BuildNavigationRail()
        {
            var viewport = new GameObject("SeoNavigationViewport", typeof(RectTransform), typeof(Image),
                typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(safeRoot, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            SeoUIFactory.SetRect(viewportRect, Vector2.zero, new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(160f, -24f));
            viewport.GetComponent<Image>().color = Color.clear;
            var content = new GameObject("SeoToolRail", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            SeoUIFactory.SetRect(contentRect, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 8, 8);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            sideMenuRoot = content;
        }

        private void LateUpdate()
        {
            if (!built || safeRoot == null || dockRoot == null) return;
            var size = safeRoot.rect.size;
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            int count = sideMenuRoot.transform.childCount;
            if (size.x <= 0f || size.y <= 0f) return;
            if (size == lastSafeSize && Mathf.Approximately(scale, lastCanvasScale) && count == lastNavigationCount)
            {
                LayoutContextPanels();
                return;
            }
            lastSafeSize = size;
            lastCanvasScale = scale;
            lastNavigationCount = count;
            float railWidth = Mathf.Max(160f, 80f / scale + 8f);
            var viewport = (RectTransform)sideMenuRoot.transform.parent;
            viewport.sizeDelta = new Vector2(railWidth, -24f);
            foreach (Transform child in sideMenuRoot.transform)
            {
                LayoutNavigationContent(child, scale);
            }
            var dockRect = dockRoot.GetComponent<RectTransform>();
            float dockX = 28f + railWidth;
            dockRect.anchoredPosition = new Vector2(dockX, 0f);
            dockRect.sizeDelta = new Vector2(Mathf.Min(760f, size.x - dockX - 16f),
                Mathf.Min(760f, size.y - 32f));
            LayoutContextButtons(size, scale);
            LayoutContextPanels();
        }

        private void LayoutContextPanels()
        {
            if (contextBarRect == null) return;
            bool beltMode = buildRouter != null && buildRouter.CurrentMode == BuildInputRouter.Mode.Belt;
            float buttonHeight = Mathf.Max(96f, 56f / Mathf.Max(0.01f, canvas.scaleFactor));
            float messageHeight = beltMode ? 88f : 0f;
            // Keep touch actions at the safe area's bottom edge, with information stacked above.
            contextBarRect.anchoredPosition = new Vector2(0f, ContextBottomMargin);
            contextBarRect.sizeDelta = new Vector2(contextBarRect.sizeDelta.x, buttonHeight + 20f + messageHeight);
            if (placementCostPanel != null)
            {
                var costRect = placementCostPanel.GetComponent<RectTransform>();
                costRect.anchoredPosition = new Vector2(0f,
                    ContextBottomMargin + contextBarRect.sizeDelta.y + ContextPanelGap);
            }
            if (cancelButton != null && beltMode)
                cancelButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -messageHeight * 0.5f);
            var feedback = safeRoot.Find("SeoBeltFeedback") as RectTransform;
            if (feedback == null) return;
            // One background covers both the instruction and the cancel button.
            feedback.GetComponent<Image>().enabled = false;
            feedback.anchoredPosition = contextBarRect.anchoredPosition + new Vector2(0f, buttonHeight + 20f);
            feedback.sizeDelta = new Vector2(contextBarRect.sizeDelta.x, messageHeight);
        }

        private void LayoutContextButtons(Vector2 safeSize, float scale)
        {
            if (contextBarRect == null) return;
            float width = Mathf.Min(880f, safeSize.x - 32f);
            float buttonWidth = (width - 64f) / 3f;
            float buttonHeight = Mathf.Max(96f, 56f / scale);
            float fontSize = Mathf.Min(Mathf.Max(30f, 18f / scale), (buttonWidth - 32f) / 5f);
            contextButtonStep = buttonWidth + 16f;
            contextBarRect.sizeDelta = new Vector2(width, buttonHeight + 20f);
            foreach (Transform child in contextBarRect)
            {
                if (child.GetComponent<Button>() == null) continue;
                var rect = (RectTransform)child;
                rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
                rect.localScale = Vector3.one;
                var label = child.GetComponentInChildren<Text>(true);
                if (label == null) continue;
                label.font = SeoUITheme.Current.FontAsset;
                label.enableAutoSizing = false;
                label.fontSize = fontSize;
                label.fontStyle = TMPro.FontStyles.Bold;
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                SeoUIFactory.SetRect(label.rectTransform, Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-32f, -20f));
            }
            if (groupMoveRotateButton != null)
                SetActionButtonX(groupMoveRotateButton.gameObject, -232f);
        }

        private static void ApplyCardBackground(Button button)
        {
            var background = button.GetComponent<Image>();
            background.sprite = null;
            background.overrideSprite = null;
            background.type = Image.Type.Simple;
            background.color = ToolCardIdleColor;
            button.transition = Selectable.Transition.None;
            var palette = button.GetComponent<BuildPaletteButton>();
            if (palette != null) palette.SetBackgroundColors(ToolCardIdleColor, ToolCardIdleColor);
            if (button.GetComponent<RectMask2D>() == null) button.gameObject.AddComponent<RectMask2D>();
        }

        private static void LayoutNavigationContent(Transform button, float scale)
        {
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            scale = Mathf.Max(0.01f, scale);
            label.fontSizeMax = Mathf.Max(22f, 12f / scale);
            label.fontSizeMin = Mathf.Max(18f, 10f / scale);
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.overflowMode = TMPro.TextOverflowModes.Overflow;
            label.margin = Vector4.zero;
            float lineHeight = label.fontSizeMax * 1.5f;
            if (label.font != null)
            {
                var face = label.font.faceInfo;
                lineHeight = label.fontSizeMax * Mathf.Max(face.lineHeight, face.ascentLine - face.descentLine)
                    * face.scale / Mathf.Max(1f, face.pointSize);
            }
            float captionHeight = Mathf.Ceil(lineHeight) + 6f;
            SeoUIFactory.SetRect(label.rectTransform, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(-16f, captionHeight));
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null)
                layout.minHeight = layout.preferredHeight = Mathf.Max(104f, 56f / scale, captionHeight + 70f);
            var icon = button.Find("NavigationIcon") as RectTransform;
            if (icon != null)
            {
                icon.anchorMin = new Vector2(0.16f, 0f);
                icon.anchorMax = new Vector2(0.84f, 1f);
                icon.offsetMin = new Vector2(0f, captionHeight + 12f);
                icon.offsetMax = new Vector2(0f, -8f);
            }
            label.transform.SetAsLastSibling();
        }

        internal static void CreateNavigationIcon(Transform parent, string kind)
        {
            var sprite = GetNavigationIconSprite(kind);
            if (sprite == null)
            {
                CreateToolDiagram(parent, kind, true, SeoUITheme.Current.Primary);
                return;
            }

            var iconObject = new GameObject("NavigationIcon", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            iconObject.transform.SetParent(parent, false);
            var icon = iconObject.GetComponent<Image>();
            icon.sprite = sprite;
            icon.color = Color.white;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            SeoUIFactory.SetRect(iconObject.GetComponent<RectTransform>(), new Vector2(0.16f, 0.34f),
                new Vector2(0.84f, 0.94f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        private static Sprite GetNavigationIconSprite(string kind)
        {
            if (NavigationIconSprites.TryGetValue(kind, out var cachedSprite)) return cachedSprite;

            int column;
            int row;
            switch (kind)
            {
                case "resource": column = 0; row = 0; break;
                case "power_status": column = 1; row = 0; break;
                case "focus": column = 2; row = 0; break;
                case "production": column = 0; row = 1; break;
                case "logistics": column = 1; row = 1; break;
                case "power_build": column = 2; row = 1; break;
                case "edit": column = 0; row = 2; break;
                case "save": column = 1; row = 2; break;
                case "report": column = 2; row = 2; break;
                default: return null;
            }

            var atlas = Resources.Load<Texture2D>("NavigationIcons/FactoryNavigationIcons");
            if (atlas == null) return null;
            int cellWidth = atlas.width / 3;
            int cellHeight = atlas.height / 3;
            var rect = new Rect(column * cellWidth, (2 - row) * cellHeight, cellWidth, cellHeight);
            var sprite = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), 100f);
            NavigationIconSprites[kind] = sprite;
            return sprite;
        }

        private void ToggleCoreResourcePanel()
        {
            if (coreResourcePanel == null) return;
            if (coreResourcePanel.activeSelf)
            {
                coreResourcePanel.SetActive(false);
                return;
            }
            OpenCoreResourcePanel();
        }

        public void OpenCoreResourcePanel()
        {
            if (coreResourcePanel == null) return;
            coreResourcePanel.SetActive(true);
            if (powerDetailPanel != null) powerDetailPanel.SetActive(false);
            coreResourcePanel.transform.SetAsLastSibling();
            RefreshCoreResourcePanel();
        }

        private void TogglePowerDetailPanel()
        {
            if (powerDetailPanel == null) return;
            bool show = !powerDetailPanel.activeSelf;
            powerDetailPanel.SetActive(show);
            if (coreResourcePanel != null) coreResourcePanel.SetActive(false);
            if (show) powerDetailPanel.transform.SetAsLastSibling();
        }

        private float LayoutCoreResourcePanel()
        {
            // 작은 게임 화면에서도 이름 약 17px, 수량 약 19px 이상을 확보한다.
            float unit = Mathf.Max(1f, 0.6f / Mathf.Max(0.01f, canvas.scaleFactor));
            var panelRect = (RectTransform)coreResourcePanel.transform;
            panelRect.sizeDelta = new Vector2(Mathf.Min(1100f * unit, safeRoot.rect.width - 48f),
                Mathf.Min(720f * unit, safeRoot.rect.height - 48f));
            coreResourceTitle.fontSize = 32f * unit;
            coreResourceTitle.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            SeoUIFactory.SetRect(coreResourceTitle.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, 1f), new Vector2(28f, -16f) * unit, new Vector2(-140f, 52f) * unit);
            SeoUIFactory.SetRect(coreResourceClose, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-18f, -16f) * unit, new Vector2(68f, 58f) * unit);
            coreResourceClose.GetComponentInChildren<Text>(true).fontSize = 38f * unit;
            coreResourceViewport.offsetMin = new Vector2(24f, 24f) * unit;
            coreResourceViewport.offsetMax = new Vector2(-48f, -90f) * unit;
            coreResourceScrollbar.offsetMin = new Vector2(-34f, 24f) * unit;
            coreResourceScrollbar.offsetMax = new Vector2(-18f, -90f) * unit;
            coreResourceScroll.scrollSensitivity = 90f * unit;
            return unit;
        }

        private void RefreshCoreResourcePanel()
        {
            var driver = FindFirstObjectByType<Factory.Simulation.SimulationDriver>();
            if (driver == null || driver.World == null || coreResourceContent == null) return;

            float unit = LayoutCoreResourcePanel();
            float gap = 16f * unit;
            float availableWidth = ((RectTransform)coreResourcePanel.transform).sizeDelta.x - 72f * unit;
            int columns = availableWidth >= 896f * unit ? 2 : 1;
            float cardWidth = (availableWidth - gap * (columns - 1)) / columns;
            // Noto Sans KR은 28pt에서도 한 줄 높이가 약 41이다. 글자 크기만 기준으로
            // 높이를 잡으면 TMP의 세로 overflow 판정으로 이름 전체가 사라질 수 있다.
            float lineScale = 1.5f;
            var font = SeoUITheme.Current.FontAsset;
            if (font != null && font.faceInfo.pointSize > 0f)
            {
                var face = font.faceInfo;
                lineScale = Mathf.Max(lineScale, Mathf.Max(face.lineHeight, face.ascentLine - face.descentLine)
                    * face.scale / face.pointSize);
            }
            float nameHeight = (Mathf.Ceil(28f * lineScale) + 8f) * unit;
            float amountHeight = (Mathf.Ceil(32f * lineScale) + 8f) * unit;
            float cardHeight = nameHeight + amountHeight + 28f * unit;

            var resources = driver.World.Database.Resources;
            if (coreResourceEntries.Count == 0)
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    var resource = resources[i];
                    var card = SeoUIFactory.CreatePanel(coreResourceContent, "CoreResource_" + resource.Key,
                        new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(cardWidth, cardHeight),
                        new Color(0.035f, 0.10f, 0.14f, 0.96f));
                    card.rectTransform.pivot = new Vector2(0f, 1f);

                    var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer),
                        typeof(Image));
                    iconObject.transform.SetParent(card.transform, false);
                    var icon = iconObject.GetComponent<Image>();
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    RecipeResourceIconCache.Assign(icon, resource.Key, resource.PrefabName, resource.Color);

                    var name = SeoUIFactory.CreateTMPText(card.transform, "Name",
                        string.IsNullOrWhiteSpace(resource.DisplayName) ? resource.Key : resource.DisplayName, 28,
                        TextAnchor.MiddleLeft, FontStyle.Bold);
                    name.enableAutoSizing = false;
                    name.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                    name.overflowMode = TMPro.TextOverflowModes.Overflow;
                    name.color = SeoUITheme.Current.Text;

                    var amount = SeoUIFactory.CreateTMPText(card.transform, "Amount", "×0", 32,
                        TextAnchor.MiddleLeft, FontStyle.Bold);
                    amount.color = SeoUITheme.Current.Primary;
                    amount.enableAutoSizing = false;
                    amount.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                    coreResourceEntries.Add(new CoreResourceEntry
                    {
                        ResourceId = i,
                        Root = card.gameObject,
                        Name = name,
                        Icon = icon,
                        Amount = amount,
                    });
                }
            }

            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count
                || driver.World.Processors[coreIndex] == null)
            {
                for (int i = 0; i < coreResourceEntries.Count; i++) coreResourceEntries[i].Root.SetActive(false);
                coreResourceEmptyText.text = "코어를 찾을 수 없습니다";
                coreResourceEmptyText.gameObject.SetActive(true);
                return;
            }

            var core = driver.World.Processors[coreIndex];
            int visible = 0;
            for (int i = 0; i < coreResourceEntries.Count; i++)
            {
                CoreResourceEntry entry = coreResourceEntries[i];
                int count = core.InputBuffer[entry.ResourceId];
                bool hasResource = count > 0;
                entry.Root.SetActive(hasResource);
                if (!hasResource) continue;

                int column = visible % columns;
                int row = visible / columns;
                var cardRect = entry.Root.GetComponent<RectTransform>();
                cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);
                cardRect.anchoredPosition = new Vector2(column * (cardWidth + gap), -row * (cardHeight + gap));
                SeoUIFactory.SetRect(entry.Icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(16f, 0f) * unit, new Vector2(76f, 76f) * unit);
                SeoUIFactory.SetRect(entry.Name.rectTransform, new Vector2(0f, 1f), Vector2.one,
                    new Vector2(0f, 1f), new Vector2(108f, -12f) * unit, new Vector2(-124f * unit, nameHeight));
                SeoUIFactory.SetRect(entry.Amount.rectTransform, new Vector2(0f, 1f), Vector2.one,
                    new Vector2(0f, 1f), new Vector2(108f * unit, -16f * unit - nameHeight),
                    new Vector2(-124f * unit, amountHeight));
                entry.Name.fontSize = 28f * unit;
                entry.Amount.fontSize = 32f * unit;
                entry.Amount.text = "×" + count.ToString("N0");
                visible++;
            }

            var contentRect = coreResourceContent as RectTransform;
            if (contentRect != null)
            {
                int rowCount = Mathf.CeilToInt(visible / (float)columns);
                float viewportHeight = ((RectTransform)coreResourcePanel.transform).sizeDelta.y - 114f * unit;
                float contentHeight = rowCount > 0 ? rowCount * (cardHeight + gap) - gap : 0f;
                contentRect.sizeDelta = new Vector2(0f, Mathf.Max(viewportHeight, contentHeight));
            }

            coreResourceEmptyText.text = "보유 자원이 없습니다";
            coreResourceEmptyText.gameObject.SetActive(visible == 0);
        }

        private void FocusCore()
        {
            GameObject core = GameObject.Find("Core");
            var cameraRig = FindFirstObjectByType<TouchCameraRig>();
            if (core == null || cameraRig == null)
            {
                ShowToast("코어 위치를 찾을 수 없습니다");
                return;
            }

            cameraRig.FocusWorldPoint(core.transform.position);
            ShowToast("코어로 이동했습니다");
        }

        private void BuildExitDialog()
        {
            var panel = SeoUIFactory.CreatePanel(safeRoot, "SeoExitDialog", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 300f),
                new Color(0.015f, 0.04f, 0.06f, 0.99f));
            exitDialogRoot = panel.gameObject;
            var title = SeoUIFactory.CreateTMPText(panel.transform, "Title", "게임을 종료하시겠습니까?", 28,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            exitDialogTitle = title;
            SeoUIFactory.SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(-40f, 110f));

            var cancel = SeoUIFactory.CreateTMPButton(panel.transform, "Cancel", "계속하기",
                () => exitDialogRoot.SetActive(false));
            SeoUIFactory.SetRect(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-140f, 34f),
                new Vector2(250f, 88f));
            var exit = SeoUIFactory.CreateTMPButton(panel.transform, "Exit", "게임 종료", ConfirmLeaveGame,
                SeoUITheme.Current.Danger);
            exitDialogConfirmLabel = exit.GetComponentInChildren<Text>();
            exitDialogConfirmLabel.fontSize = 26f;
            cancel.GetComponentInChildren<Text>().fontSize = 26f;
            SeoUIFactory.SetRect(exit.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(140f, 34f),
                new Vector2(250f, 88f));
            exitDialogRoot.SetActive(false);
        }

        private void ShowExitDialog()
        {
            ShowLeaveDialog(false);
        }

        private void ShowLeaveDialog(bool toTitle)
        {
            if (exitDialogRoot == null) return;
            returnToTitle = toTitle;
            exitDialogTitle.text = toTitle
                ? "타이틀로 이동하시겠습니까?\n<size=20>저장하지 않은 진행 내용은 사라집니다.</size>"
                : "게임을 종료하시겠습니까?";
            exitDialogConfirmLabel.text = toTitle ? "타이틀로 이동" : "게임 종료";
            exitDialogRoot.SetActive(true);
            exitDialogRoot.transform.SetAsLastSibling();
        }

        private void ConfirmLeaveGame()
        {
            if (!returnToTitle)
            {
                QuitGame();
                return;
            }
            CancelCurrentInteraction();
            Time.timeScale = 1f;
            SceneManager.LoadScene("Title");
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleBackPressed()
        {
            if (CreditsPanel.TryHandleBack()) return;
            if (BgmSettingsPanel.TryHandleBack()) return;

            var research = Choi.Research.ResearchController.Instance;
            if (research != null && research.IsOpen)
            {
                research.Close();
                return;
            }

            if (exitDialogRoot != null && exitDialogRoot.activeSelf)
            {
                exitDialogRoot.SetActive(false);
                return;
            }

            var recipePanel = RecipeSelectionPanel.Instance;
            if (recipePanel != null && recipePanel.gameObject.activeSelf)
            {
                recipePanel.Close();
                return;
            }

            if (coreResourcePanel != null && coreResourcePanel.activeSelf)
            {
                coreResourcePanel.SetActive(false);
                return;
            }
            if (powerDetailPanel != null && powerDetailPanel.activeSelf)
            {
                powerDetailPanel.SetActive(false);
                return;
            }

            var statisticsPanel = GameObject.Find("FactoryStatisticsPanel");
            if (statisticsPanel != null && statisticsPanel.activeSelf)
            {
                statisticsPanel.SetActive(false);
                return;
            }

            var powerController = FindFirstObjectByType<PowerBuildController>();
            bool activeBuild = editModeActive
                || (buildRouter != null && buildRouter.CurrentMode != BuildInputRouter.Mode.None)
                || (powerController != null && powerController.Mode != PowerBuildMode.None);
            if (activeBuild)
            {
                CancelCurrentInteraction();
                return;
            }

            if (dockRoot != null && dockRoot.activeSelf)
            {
                CloseCategoryPanel(true);
                return;
            }

            ShowExitDialog();
        }

        private static void CreateCardAccent(Transform parent, Color color)
        {
            var accent = SeoUIFactory.CreatePanel(parent, "Accent", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 108f), color);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.raycastTarget = false;
        }

        private void BuildBottomDock()
        {
            var dock = SeoUIFactory.CreatePanel(safeRoot, "SeoToolFlyout", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(188f, 0f), new Vector2(760f, 760f));
            dock.sprite = null;
            dock.type = Image.Type.Simple;
            dock.rectTransform.pivot = new Vector2(0f, 0.5f);
            dockRoot = dock.gameObject;
            BuildSideMenu();

            categoryTitle = SeoUIFactory.CreateTMPText(dock.transform, "CategoryTitle", "생산 도구", 30,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(categoryTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(34f, -16f), new Vector2(-110f, 58f));
            categoryTitle.color = SeoUITheme.Current.Primary;

            productionPage = CreatePage(dock.transform, "ProductionPage");
            logisticsPage = CreatePage(dock.transform, "LogisticsPage");
            powerPage = CreatePage(dock.transform, "PowerPage");
            systemPage = CreatePage(dock.transform, "SystemPage");

            EnsureRuntimeMachineButton("PaletteButton_ProcessingMachine", "가공기", "ProcessingMachine");
            EnsureRuntimeMachineButton("PaletteButton_CrossBelt", "크로스벨트", "CrossBelt");

            MovePaletteButton("PaletteButton_Miner", productionPage.transform, 0, string.Empty, null, "miner");
            MovePaletteButton("PaletteButton_Smelter", productionPage.transform, 1, string.Empty, null, "smelter");
            MovePaletteButton("PaletteButton_Former", productionPage.transform, 2, string.Empty, null, "former");
            MovePaletteButton("PaletteButton_Synthesizer", productionPage.transform, 3, string.Empty, null,
                "synthesizer");
            MovePaletteButton("PaletteButton_ProcessingMachine", productionPage.transform, 4, string.Empty, null,
                "processing");

            // 물류 설비는 방향 하나만 그려서는 역할을 구분하기 어렵다. 실제 흐름 형태를
            // 축약한 도식으로 표시한다: 직선 / 1→3 분기 / 3→1 합류 / 저장 코어.
            MovePaletteButton("PaletteButton_Belt", logisticsPage.transform, 0, string.Empty, null, "belt");
            MovePaletteButton("PaletteButton_Splitter", logisticsPage.transform, 1, string.Empty, null, "splitter");
            MovePaletteButton("PaletteButton_Merger", logisticsPage.transform, 2, string.Empty, null, "merger");
            MovePaletteButton("PaletteButton_MiniCore", logisticsPage.transform, 3, string.Empty, null, "core");
            MovePaletteButton("PaletteButton_CrossBelt", logisticsPage.transform, 4, string.Empty, null, "crossbelt");
            var legacyDemolishButton = GameObject.Find("PaletteButton_Demolish");
            if (legacyDemolishButton != null) legacyDemolishButton.SetActive(false);

            BuildPowerButtons();
            BuildSystemButtons();
            BuildDockCloseButton(dock.transform);
            BuildContextBar(dock.transform);
            BuildPlacementCostPanel();
            CloseCategoryPanel(false);
        }

        private void BuildDockCloseButton(Transform dock)
        {
            var close = SeoUIFactory.CreateTMPButton(dock, "SeoDockClose", "×", () => CloseCategoryPanel(true));
            SeoUIFactory.SetRect(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                new Vector2(1f, 1f), new Vector2(-18f, -16f), new Vector2(68f, 58f));
            var label = close.GetComponentInChildren<Text>(true);
            if (label != null) label.fontSize = 38;
        }

        private void BuildSideMenu()
        {
            var menu = sideMenuRoot;

            productionTab = CreateTab(menu.transform, "생산", "production", 0, Category.Production);
            logisticsTab = CreateTab(menu.transform, "물류", "logistics", 1, Category.Logistics);
            powerTab = CreateTab(menu.transform, "전력 설비", "power_build", 2, Category.Power);
            editModeButton = CreateRailButton(menu.transform, "EditMode", "편집", "edit", 6, EnterEditMode,
                exitEditMode: false);
            systemTab = CreateTab(menu.transform, "저장", "save", 4, Category.System);
            sideMenuRoot.SetActive(true);
        }

        private Button CreateTab(Transform parent, string label, string diagramKind, int index, Category category)
        {
            return CreateRailButton(parent, "Tab_" + category, label, diagramKind, index + 3, () => ToggleCategory(category));
        }

        internal static Button CreateRailButton(Transform parent, string name, string label, string diagramKind, int index,
            UnityEngine.Events.UnityAction action, bool exitEditMode = true)
        {
            var button = SeoUIFactory.CreateTMPButton(parent, name, label, () =>
            {
                // 생산 보고서처럼 다른 Seo UI에서 추가한 메뉴도 같은 종료 처리를 거친다.
                if (exitEditMode) FindFirstObjectByType<FactoryHudController>()?.ExitEditMode();
                action?.Invoke();
            });
            ApplyCardBackground(button);
            button.transform.SetSiblingIndex(index);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 104f;
            layout.preferredHeight = 104f;
            var labelText = button.GetComponentInChildren<Text>(true);
            if (labelText != null)
            {
                labelText.fontSize = 22;
                labelText.enableAutoSizing = true;
                labelText.fontSizeMin = 18;
                labelText.fontSizeMax = 22;
                labelText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                labelText.overflowMode = TMPro.TextOverflowModes.Overflow;
            }
            CreateNavigationIcon(button.transform, diagramKind);
            var parentCanvas = button.GetComponentInParent<Canvas>();
            LayoutNavigationContent(button.transform, parentCanvas != null ? parentCanvas.scaleFactor : 1f);
            SetTabState(button, false);
            return button;
        }

        private static GameObject CreatePage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var rect = go.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(24f, 20f);
            rect.offsetMax = new Vector2(-24f, -88f);
            return go;
        }

        private void MovePaletteButton(string objectName, Transform page, int index, string icon, Color? tint = null,
            string diagramKind = null)
        {
            var go = GameObject.Find(objectName);
            if (go == null) return;
            go.transform.SetParent(page, false);
            var button = go.GetComponent<Button>();
            SeoUIFactory.ApplyButton(button, tint);
            var image = go.GetComponent<Image>();
            if (image != null) image.color = ToolCardIdleColor;
            if (button != null) button.transition = Selectable.Transition.None;
            if (button != null) button.onClick.AddListener(CollapseAfterToolSelection);
            LayoutToolCard(go, index, icon, diagramKind);
        }

        private void EnsureRuntimeMachineButton(string objectName, string label, string machineId)
        {
            if (GameObject.Find(objectName) != null) return;
            var button = SeoUIFactory.CreateTMPButton(safeRoot, objectName, label, () =>
            {
                if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
                if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
                if (machineTool == null || buildRouter == null) return;
                machineTool.SelectMachine(machineId);
                buildRouter.SetMode(BuildInputRouter.Mode.PlaceMachine);
                pendingPlacementMachineId = machineId;
            });
            button.gameObject.name = objectName;
        }

        private static void LayoutToolCard(GameObject go, int index, string icon, string diagramKind = null)
        {
            ApplyCardBackground(go.GetComponent<Button>());
            int column = index % 2;
            int row = index / 2;
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(column * 0.5f, 1f - (row + 1f) / 3f),
                new Vector2((column + 1f) * 0.5f, 1f - row / 3f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-16f, -16f));
            var label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.fontSize = 24;
                label.enableAutoSizing = true;
                label.fontSizeMin = 18;
                label.fontSizeMax = 24;
                label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                label.overflowMode = TMPro.TextOverflowModes.Truncate;
                SeoUIFactory.SetRect(label.rectTransform, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.24f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            }

            if (!string.IsNullOrEmpty(diagramKind))
            {
                if (!CreateToolAssetThumbnail(go.transform, diagramKind))
                    CreateToolDiagram(go.transform, diagramKind);
                if (label != null) label.transform.SetAsLastSibling();
                return;
            }

            var iconText = SeoUIFactory.CreateTMPText(go.transform, "ToolIcon", icon, 56,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(iconText.rectTransform, new Vector2(0.14f, 0.40f), new Vector2(0.86f, 0.80f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            iconText.color = SeoUITheme.Current.Primary;
            iconText.enableAutoSizing = true;
            iconText.fontSizeMin = 30;
            iconText.fontSizeMax = 56;
            iconText.lineSpacing = 0.72f;
        }

        private static bool CreateToolAssetThumbnail(Transform parent, string kind, bool compact = false)
        {
            string prefabKey = null;
            GameObject prefab = null;
            Sprite directSprite = null;
            switch (kind)
            {
                case "production": prefabKey = "Prefab_Smelter"; break;
                case "logistics":
                case "belt":
                    directSprite = GetToolTextureSprite(SeoUITheme.Current.BeltTexture);
                    break;
                case "miner": prefabKey = "Prefab_Miner"; break;
                case "smelter": prefabKey = "Prefab_Smelter"; break;
                case "former": prefabKey = "Prefab_Former"; break;
                case "synthesizer": prefabKey = "Prefab_Synthesizer"; break;
                case "processing": prefabKey = "Prefab_ProcessingMachine"; break;
                case "splitter": directSprite = GetToolTextureSprite(SeoUITheme.Current.SplitterTexture); break;
                case "merger": directSprite = GetToolTextureSprite(SeoUITheme.Current.MergerTexture); break;
                case "crossbelt": directSprite = GetToolTextureSprite(SeoUITheme.Current.CrossBeltTexture); break;
                case "core": prefabKey = "Prefab_Core"; break;
                case "generator": prefab = SeoUITheme.Current.GeneratorPreviewPrefab; break;
                case "cable": directSprite = Resources.Load<Sprite>("ResourceIcons/Prefab_Item_PowerCable"); break;
                case "tower": prefab = SeoUITheme.Current.TowerPreviewPrefab; break;
                case "save": directSprite = GetNavigationIconSprite("save"); break;
                case "load": directSprite = GetToolTextureSprite(Resources.Load<Texture2D>("NavigationIcons/LoadGame")); break;
                case "sound": directSprite = GetToolTextureSprite(Resources.Load<Texture2D>("NavigationIcons/SoundSettings")); break;
                case "title":
                case "exit": directSprite = GetToolTextureSprite(Resources.Load<Texture2D>("NavigationIcons/ExitGame")); break;
            }

            if (prefab == null && string.IsNullOrEmpty(prefabKey) && directSprite == null) return false;

            var iconObject = new GameObject("AssetThumbnail", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            iconObject.transform.SetParent(parent, false);
            var icon = iconObject.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            SeoUIFactory.SetRect(iconObject.GetComponent<RectTransform>(), new Vector2(0.12f, 0.30f),
                new Vector2(0.88f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            if (directSprite != null)
            {
                icon.sprite = directSprite;
                icon.color = Color.white;
                // Reuse the system menu artwork with a left-facing return arrow.
                if (kind == "title") icon.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            }
            else if (prefab != null) TopViewIconCache.Assign(icon, prefab, kind);
            else TopViewIconCache.Assign(icon, prefabKey);
            return true;
        }

        private static Sprite GetToolTextureSprite(Texture2D texture)
        {
            if (texture == null) return null;
            if (ToolTextureSprites.TryGetValue(texture, out var sprite)) return sprite;
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            ToolTextureSprites[texture] = sprite;
            return sprite;
        }

        private static void CreateToolDiagram(Transform parent, string kind, bool compact = false, Color? tint = null)
        {
            var root = new GameObject("ToolDiagram", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            float anchorY = compact ? 0.67f : 0.66f;
            SeoUIFactory.SetRect(root.GetComponent<RectTransform>(), new Vector2(0.5f, anchorY),
                new Vector2(0.5f, anchorY), new Vector2(0.5f, 0.5f), Vector2.zero,
                compact ? new Vector2(78f, 44f) : new Vector2(118f, 62f));
            root.transform.localScale = Vector3.one * (compact ? 0.48f : 1f);
            Color color = tint ?? SeoUITheme.Current.Primary;

            switch (kind)
            {
                case "resource":
                    CreateDiagramLine(root.transform, new Vector2(-32f, 28f), new Vector2(32f, 28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(32f, 28f), new Vector2(32f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(32f, -28f), new Vector2(-32f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-32f, -28f), new Vector2(-32f, 28f), 6f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-17f, 10f), new Vector2(16f, 16f), color);
                    CreateDiagramBlock(root.transform, new Vector2(5f, 10f), new Vector2(16f, 16f), color);
                    CreateDiagramBlock(root.transform, new Vector2(-6f, -12f), new Vector2(16f, 16f), color);
                    break;
                case "save":
                    CreateDiagramLine(root.transform, new Vector2(-32f, 28f), new Vector2(32f, 28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(32f, 28f), new Vector2(32f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(32f, -28f), new Vector2(-32f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-32f, -28f), new Vector2(-32f, 28f), 6f, color);
                    CreateDiagramBlock(root.transform, new Vector2(8f, 13f), new Vector2(30f, 14f), color);
                    CreateDiagramLine(root.transform, new Vector2(-18f, -17f), new Vector2(18f, -17f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-18f, -17f), new Vector2(-18f, 1f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(18f, -17f), new Vector2(18f, 1f), 5f, color);
                    break;
                case "report":
                    CreateDiagramLine(root.transform, new Vector2(-34f, 29f), new Vector2(34f, 29f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(34f, 29f), new Vector2(34f, -29f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(34f, -29f), new Vector2(-34f, -29f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-34f, -29f), new Vector2(-34f, 29f), 5f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-19f, -11f), new Vector2(10f, 22f), color);
                    CreateDiagramBlock(root.transform, new Vector2(0f, -4f), new Vector2(10f, 36f), color);
                    CreateDiagramBlock(root.transform, new Vector2(19f, 5f), new Vector2(10f, 54f), color);
                    break;
                case "power":
                    CreateDiagramLine(root.transform, new Vector2(10f, 32f), new Vector2(-15f, 4f), 7f, color);
                    CreateDiagramLine(root.transform, new Vector2(-15f, 4f), new Vector2(8f, 4f), 7f, color);
                    CreateDiagramLine(root.transform, new Vector2(8f, 4f), new Vector2(-12f, -32f), 7f, color);
                    break;
                case "generator":
                    CreateDiagramLine(root.transform, new Vector2(-30f, 27f), new Vector2(34f, 27f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(34f, 27f), new Vector2(34f, -27f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(34f, -27f), new Vector2(-30f, -27f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-30f, -27f), new Vector2(-30f, 27f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(7f, 19f), new Vector2(-9f, 2f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-9f, 2f), new Vector2(7f, 2f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(7f, 2f), new Vector2(-8f, -19f), 6f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-43f, -12f), new Vector2(13f, 13f), color);
                    CreateDiagramLine(root.transform, new Vector2(-37f, -12f), new Vector2(-30f, -12f), 5f, color);
                    break;
                case "focus":
                    CreateDiagramLine(root.transform, new Vector2(-34f, 0f), new Vector2(-12f, 0f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(12f, 0f), new Vector2(34f, 0f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, -30f), new Vector2(0f, -10f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, 10f), new Vector2(0f, 30f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-12f, 12f), new Vector2(12f, 12f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(12f, 12f), new Vector2(12f, -12f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(12f, -12f), new Vector2(-12f, -12f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(-12f, -12f), new Vector2(-12f, 12f), 4f, color);
                    CreateDiagramBlock(root.transform, Vector2.zero, new Vector2(8f, 8f), color);
                    break;
                case "production":
                    CreateDiagramLine(root.transform, new Vector2(-38f, 12f), new Vector2(-18f, 25f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-18f, 25f), new Vector2(0f, 12f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, 12f), new Vector2(18f, 25f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(18f, 25f), new Vector2(38f, 12f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-38f, 12f), new Vector2(-38f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(38f, 12f), new Vector2(38f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-38f, -28f), new Vector2(38f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(25f, 25f), new Vector2(25f, 38f), 8f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-18f, -10f), new Vector2(10f, 16f), color);
                    CreateDiagramBlock(root.transform, new Vector2(0f, -10f), new Vector2(10f, 16f), color);
                    CreateDiagramBlock(root.transform, new Vector2(18f, -10f), new Vector2(10f, 16f), color);
                    break;
                case "logistics":
                    CreateDiagramArrow(root.transform, new Vector2(-42f, 14f), new Vector2(42f, 14f), color);
                    CreateDiagramArrow(root.transform, new Vector2(42f, -14f), new Vector2(-42f, -14f), color);
                    break;
                case "edit":
                    CreateDiagramLine(root.transform, new Vector2(-28f, -24f), new Vector2(25f, 26f), 8f, color);
                    CreateDiagramLine(root.transform, new Vector2(-34f, -31f), new Vector2(-22f, -24f), 7f, color);
                    break;
                case "miner":
                    CreateDiagramLine(root.transform, new Vector2(-34f, -28f), new Vector2(0f, 30f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(34f, -28f), new Vector2(0f, 30f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-24f, -10f), new Vector2(24f, -10f), 5f, color);
                    CreateDiagramBlock(root.transform, new Vector2(0f, 8f), new Vector2(12f, 18f), color);
                    CreateDiagramArrow(root.transform, new Vector2(0f, 0f), new Vector2(0f, -31f), color);
                    CreateDiagramBlock(root.transform, new Vector2(-33f, -29f), new Vector2(12f, 8f), color);
                    CreateDiagramBlock(root.transform, new Vector2(33f, -29f), new Vector2(12f, 8f), color);
                    break;
                case "cable":
                    CreateDiagramLine(root.transform, new Vector2(-40f, 20f), new Vector2(-40f, -26f), 7f, color);
                    CreateDiagramLine(root.transform, new Vector2(40f, 20f), new Vector2(40f, -26f), 7f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-40f, 24f), new Vector2(15f, 10f), color);
                    CreateDiagramBlock(root.transform, new Vector2(40f, 24f), new Vector2(15f, 10f), color);
                    CreateDiagramLine(root.transform, new Vector2(-34f, 22f), new Vector2(0f, 8f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, 8f), new Vector2(34f, 22f), 5f, color);
                    break;
                case "tower":
                    CreateDiagramLine(root.transform, new Vector2(0f, 34f), new Vector2(-25f, -30f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, 34f), new Vector2(25f, -30f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-30f, 15f), new Vector2(30f, 15f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-39f, 2f), new Vector2(39f, 2f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-17f, -12f), new Vector2(17f, -12f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-29f, -30f), new Vector2(29f, -30f), 6f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-30f, 9f), new Vector2(7f, 7f), color);
                    CreateDiagramBlock(root.transform, new Vector2(30f, 9f), new Vector2(7f, 7f), color);
                    break;
                case "load":
                    CreateDiagramLine(root.transform, new Vector2(-32f, 28f), new Vector2(32f, 28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(32f, 28f), new Vector2(32f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(32f, -28f), new Vector2(-32f, -28f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-32f, -28f), new Vector2(-32f, 28f), 6f, color);
                    CreateDiagramArrow(root.transform, new Vector2(0f, 20f), new Vector2(0f, -13f), color);
                    CreateDiagramLine(root.transform, new Vector2(-18f, -18f), new Vector2(18f, -18f), 5f, color);
                    break;
                case "exit":
                    CreateDiagramLine(root.transform, new Vector2(-32f, 29f), new Vector2(12f, 29f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-32f, 29f), new Vector2(-32f, -29f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(-32f, -29f), new Vector2(12f, -29f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(12f, 29f), new Vector2(12f, 10f), 6f, color);
                    CreateDiagramLine(root.transform, new Vector2(12f, -10f), new Vector2(12f, -29f), 6f, color);
                    CreateDiagramArrow(root.transform, new Vector2(-3f, 0f), new Vector2(39f, 0f), color);
                    break;
                case "smelter":
                    // 용광로 몸체 + 굴뚝 + 내부 열선.
                    CreateDiagramLine(root.transform, new Vector2(-35f, 20f), new Vector2(31f, 20f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(31f, 20f), new Vector2(31f, -22f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(31f, -22f), new Vector2(-35f, -22f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-35f, -22f), new Vector2(-35f, 20f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(17f, 20f), new Vector2(17f, 31f), 7f, color);
                    CreateDiagramLine(root.transform, new Vector2(9f, 31f), new Vector2(25f, 31f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-20f, -9f), new Vector2(-20f, 10f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-3f, -9f), new Vector2(-3f, 10f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(14f, -9f), new Vector2(14f, 10f), 5f, color);
                    break;
                case "former":
                    // 위·아래 금형 사이로 내려오는 프레스 피스톤.
                    CreateDiagramLine(root.transform, new Vector2(-43f, 25f), new Vector2(43f, 25f), 7f, color);
                    CreateDiagramBlock(root.transform, new Vector2(0f, 10f), new Vector2(12f, 25f), color);
                    CreateDiagramArrow(root.transform, new Vector2(0f, 3f), new Vector2(0f, -18f), color);
                    CreateDiagramBlock(root.transform, new Vector2(0f, -24f), new Vector2(34f, 12f), color);
                    CreateDiagramLine(root.transform, new Vector2(-43f, -31f), new Vector2(43f, -31f), 7f, color);
                    break;
                case "synthesizer":
                    // 두 재료가 중앙 조립실로 들어가 하나의 결과물로 나오는 흐름.
                    CreateDiagramBlock(root.transform, new Vector2(-42f, 20f), new Vector2(12f, 12f), color);
                    CreateDiagramBlock(root.transform, new Vector2(-42f, -20f), new Vector2(12f, 12f), color);
                    CreateDiagramLine(root.transform, new Vector2(-34f, 20f), new Vector2(-14f, 7f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-34f, -20f), new Vector2(-14f, -7f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-14f, 16f), new Vector2(16f, 16f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(16f, 16f), new Vector2(16f, -16f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(16f, -16f), new Vector2(-14f, -16f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-14f, -16f), new Vector2(-14f, 16f), 5f, color);
                    CreateDiagramBlock(root.transform, Vector2.zero, new Vector2(12f, 12f), color);
                    CreateDiagramArrow(root.transform, new Vector2(16f, 0f), new Vector2(48f, 0f), color);
                    break;
                case "processing":
                    CreateDiagramArrow(root.transform, new Vector2(-48f, 0f), new Vector2(-22f, 0f), color);
                    CreateDiagramLine(root.transform, new Vector2(-22f, 23f), new Vector2(22f, 23f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(22f, 23f), new Vector2(22f, -23f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(22f, -23f), new Vector2(-22f, -23f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-22f, -23f), new Vector2(-22f, 23f), 5f, color);
                    CreateDiagramBlock(root.transform, Vector2.zero, new Vector2(17f, 17f), color);
                    CreateDiagramArrow(root.transform, new Vector2(22f, 0f), new Vector2(49f, 0f), color);
                    break;
                case "belt":
                    CreateDiagramLine(root.transform, new Vector2(-45f, 15f), new Vector2(25f, 15f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(-45f, -15f), new Vector2(25f, -15f), 4f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-30f, 0f), new Vector2(10f, 22f), color);
                    CreateDiagramBlock(root.transform, new Vector2(-8f, 0f), new Vector2(10f, 22f), color);
                    CreateDiagramBlock(root.transform, new Vector2(14f, 0f), new Vector2(10f, 22f), color);
                    CreateDiagramArrow(root.transform, new Vector2(25f, 0f), new Vector2(48f, 0f), color);
                    break;
                case "splitter":
                    CreateDiagramLine(root.transform, new Vector2(-48f, 0f), new Vector2(-8f, 0f), 5f, color);
                    CreateDiagramArrow(root.transform, new Vector2(-8f, 0f), new Vector2(44f, 22f), color);
                    CreateDiagramArrow(root.transform, new Vector2(-8f, 0f), new Vector2(44f, -22f), color);
                    break;
                case "merger":
                    CreateDiagramLine(root.transform, new Vector2(-44f, 22f), new Vector2(8f, 0f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-44f, -22f), new Vector2(8f, 0f), 5f, color);
                    CreateDiagramArrow(root.transform, new Vector2(8f, 0f), new Vector2(48f, 0f), color);
                    break;
                case "core":
                    CreateDiagramLine(root.transform, new Vector2(-24f, 20f), new Vector2(24f, 20f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(24f, 20f), new Vector2(24f, -20f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(24f, -20f), new Vector2(-24f, -20f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(-24f, -20f), new Vector2(-24f, 20f), 4f, color);
                    CreateDiagramBlock(root.transform, Vector2.zero, new Vector2(18f, 18f), color);
                    CreateDiagramLine(root.transform, new Vector2(-38f, 0f), new Vector2(-24f, 0f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(24f, 0f), new Vector2(38f, 0f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, 20f), new Vector2(0f, 29f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, -20f), new Vector2(0f, -29f), 4f, color);
                    break;
            }
        }

        private static void CreateDiagramArrow(Transform parent, Vector2 start, Vector2 end, Color color)
        {
            CreateDiagramLine(parent, start, end, 5f, color);
            Vector2 direction = (end - start).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 basePoint = end - direction * 13f;
            CreateDiagramLine(parent, end, basePoint + perpendicular * 7f, 5f, color);
            CreateDiagramLine(parent, end, basePoint - perpendicular * 7f, 5f, color);
        }

        private static void CreateDiagramLine(Transform parent, Vector2 start, Vector2 end, float thickness,
            Color color)
        {
            Vector2 delta = end - start;
            var line = CreateDiagramBlock(parent, (start + end) * 0.5f,
                new Vector2(delta.magnitude, thickness), color);
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private static Image CreateDiagramBlock(Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject("Part", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            return image;
        }

        private void BuildPowerButtons()
        {
            string[] labels = { "발전기", "전선", "송전탑" };
            string[] diagrams = { "generator", "cable", "tower" };
            PowerBuildMode[] modes = { PowerBuildMode.Generator, PowerBuildMode.Cable,
                PowerBuildMode.TransmissionTower };
            Color inactiveColor = ToolCardIdleColor;

            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                Color? color = inactiveColor;
                var button = SeoUIFactory.CreateTMPButton(powerPage.transform, "PowerAction_" + labels[i], labels[i], () =>
                {
                    var controller = FindFirstObjectByType<PowerBuildController>();
                    if (controller != null)
                    {
                        controller.ToggleMode(modes[captured]);
                        ShowToast(controller.Mode == modes[captured]
                            ? labels[captured] + " 모드"
                            : labels[captured] + " 모드 종료");
                        if (controller.Mode == modes[captured]) CollapseAfterToolSelection();
                    }
                }, color);
                button.transition = Selectable.Transition.None;
                LayoutToolCard(button.gameObject, i, string.Empty, diagrams[i]);
            }
        }

        private void BuildSystemButtons()
        {
            string[] labels = { "저장", "불러오기", "소리 설정", "게임 종료", "타이틀로 이동" };
            string[] diagrams = { "save", "load", "sound", "exit", "title" };
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                var button = SeoUIFactory.CreateTMPButton(systemPage.transform, "SystemAction_" + labels[i], labels[i],
                    () =>
                    {
                        if (captured == 2)
                        {
                            BgmSettingsPanel.Show();
                            return;
                        }
                        if (captured == 3)
                        {
                            ShowExitDialog();
                            return;
                        }
                        if (captured == 4)
                        {
                            ShowLeaveDialog(true);
                            return;
                        }

                        var save = FindFirstObjectByType<PowerSaveManager>();
                        if (save == null) return;
                        if (captured == 0)
                        {
                            save.Save();
                            ShowToast("공장이 저장되었습니다");
                        }
                        else ShowToast(save.Load() ? "공장을 불러왔습니다" : "저장 파일이 없습니다");
                    }, ToolCardIdleColor);
                button.transition = Selectable.Transition.None;
                LayoutToolCard(button.gameObject, i, string.Empty, diagrams[i]);
            }
        }

        private void BuildContextBar(Transform dock)
        {
            var bar = SeoUIFactory.CreatePanel(safeRoot, "SeoContextBar", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, ContextBottomMargin), new Vector2(710f, 76f));
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);
            contextBarRect = bar.rectTransform;

            rotateButton = MoveActionButton("RotateButton", bar.transform, -232f);
            var rotateLabel = rotateButton != null ? rotateButton.GetComponentInChildren<Text>(true) : null;
            if (rotateLabel != null) rotateLabel.text = "회전";
            confirmButton = MoveActionButton("ConfirmButton", bar.transform, 0f);
            demolishConfirmButton = MoveActionButton("DemolishConfirmButton", bar.transform, 116f, SeoUITheme.Current.Danger);
            var confirmAction = confirmButton != null ? confirmButton.GetComponent<Button>() : null;
            if (confirmAction != null)
            {
                confirmAction.onClick.AddListener(HandlePlacementConfirmed);
                confirmAction.onClick.AddListener(HandlePowerPlacementConfirmed);
            }
            var demolishAction = demolishConfirmButton != null ? demolishConfirmButton.GetComponent<Button>() : null;
            if (demolishAction != null) demolishAction.onClick.AddListener(HandleDemolitionConfirmed);

            groupMoveButton = SeoUIFactory.CreateTMPButton(bar.transform, "SeoGroupMove", "이동", HandleGroupMove);
            SeoUIFactory.SetRect(groupMoveButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(210f, 56f));
            groupMoveRotateButton = SeoUIFactory.CreateTMPButton(bar.transform, "SeoMoveRotate", "회전", RotateGroupMove);
            SeoUIFactory.SetRect(groupMoveRotateButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-232f, 0f), new Vector2(210f, 56f));
            groupMoveButton.gameObject.SetActive(false);
            groupMoveRotateButton.gameObject.SetActive(false);

            var cancel = SeoUIFactory.CreateTMPButton(bar.transform, "SeoBuildCancel", "취소", CancelCurrentInteraction);
            var confirmRt = confirmButton != null ? confirmButton.GetComponent<RectTransform>() : null;
            Vector2 actionSize = confirmRt != null ? confirmRt.sizeDelta : new Vector2(210f, 56f);
            SeoUIFactory.SetRect(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(232f, 0f), actionSize);
            var cancelLabel = cancel.GetComponentInChildren<Text>(true);
            var confirmLabel = confirmButton != null ? confirmButton.GetComponentInChildren<Text>(true) : null;
            if (cancelLabel != null && confirmLabel != null)
            {
                cancelLabel.font = confirmLabel.font;
                cancelLabel.fontSize = confirmLabel.fontSize;
                cancelLabel.fontStyle = confirmLabel.fontStyle;
                cancelLabel.color = confirmLabel.color;
            }
            cancelButton = cancel.gameObject;
            bar.gameObject.SetActive(false);
        }

        private void BuildPlacementCostPanel()
        {
            var panel = SeoUIFactory.CreatePanel(safeRoot, "SeoPlacementCostPanel", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), Vector2.zero, new Vector2(900f, 98f),
                new Color(0.015f, 0.055f, 0.075f, 0.97f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0f);
            panel.raycastTarget = false;
            placementCostPanel = panel.gameObject;
            var accent = SeoUIFactory.CreatePanel(panel.transform, "Accent", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 82f), SeoUITheme.Current.Warning);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.raycastTarget = false;

            placementCostTitle = SeoUIFactory.CreateTMPText(panel.transform, "Title", "설치 필요 자원", 18,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(placementCostTitle.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(154f, -16f));
            placementCostTitle.color = SeoUITheme.Current.Warning;

            var content = new GameObject("CostEntries", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.offsetMin = new Vector2(172f, 7f);
            contentRect.offsetMax = new Vector2(-16f, -7f);
            placementCostContent = content.transform;
            placementCostPanel.SetActive(false);
        }

        private void CancelCurrentInteraction()
        {
            if (GroupMoveTool.ActiveFor(buildRouter) != null)
            {
                CancelGroupMove();
                return;
            }
            editModeActive = false;
            SetTabState(editModeButton, false);
            pendingPlacementMachineId = null;
            CancelActiveBuildMode();
            var powerController = FindFirstObjectByType<PowerBuildController>();
            if (powerController != null && powerController.Mode != PowerBuildMode.None)
                powerController.SetMode(PowerBuildMode.None);
            ShowToast("현재 작업을 취소했습니다");
        }

        private void EnterEditMode()
        {
            CancelActiveBuildMode();
            var powerController = FindFirstObjectByType<PowerBuildController>();
            if (powerController != null) powerController.SetMode(PowerBuildMode.Remove);

            editModeActive = true;
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetSideMenuVisible(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            SetTabState(systemTab, false);
            SetTabState(editModeButton, true);
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (buildRouter != null)
            {
                buildRouter.enabled = true;
                buildRouter.SetMode(BuildInputRouter.Mode.Demolish);
            }
            ShowToast("영역을 드래그한 뒤 철거 또는 이동을 누르세요 · 전력 시설 이동 시 전선은 해제됩니다");
        }

        private void ExitEditMode()
        {
            if (!editModeActive) return;
            editModeActive = false;
            SetTabState(editModeButton, false);
            // None 전환으로 철거 선택과 묶음 이동 미리보기를 함께 취소한다.
            CancelActiveBuildMode();
            var powerController = FindFirstObjectByType<PowerBuildController>();
            if (powerController != null && powerController.Mode == PowerBuildMode.Remove)
                powerController.SetMode(PowerBuildMode.None);
            UpdateContextActions();
        }

        private void HandleGroupMove()
        {
            if (!editModeActive || buildRouter == null) return;
            if (buildRouter.CurrentMode == BuildInputRouter.Mode.Demolish)
            {
                GroupMoveTool.BeginSelectionMove(buildRouter, FindFirstObjectByType<DemolishTool>(), out string message);
                ShowToast(message);
            }
            else if (GroupMoveTool.ActiveFor(buildRouter) is GroupMoveTool moveTool)
            {
                bool moved = moveTool.Confirm();
                string message = moveTool.Status;
                if (moved) buildRouter.SetMode(BuildInputRouter.Mode.Demolish);
                ShowToast(message);
            }
        }

        private void RotateGroupMove()
        {
            GroupMoveTool.ActiveFor(buildRouter)?.Rotate();
        }

        private void CancelGroupMove()
        {
            if (buildRouter == null) return;
            buildRouter.SetMode(BuildInputRouter.Mode.Demolish);
            ShowToast("이동을 취소했습니다 · 영역을 다시 선택하세요");
        }

        private void HandleDemolitionConfirmed()
        {
            if (!editModeActive) return;
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (buildRouter == null) return;
            if (buildRouter.CurrentMode == BuildInputRouter.Mode.None)
                buildRouter.SetMode(BuildInputRouter.Mode.Demolish);
            ShowToast("편집 모드 유지 · 계속 철거하거나 취소로 종료");
        }

        private void TrackActivePlacement()
        {
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            if (buildRouter == null || machineTool == null) return;
            if (buildRouter.CurrentMode == BuildInputRouter.Mode.PlaceMachine
                && !string.IsNullOrEmpty(machineTool.SelectedMachineId))
            {
                pendingPlacementMachineId = machineTool.SelectedMachineId;
            }
        }

        private void HandlePlacementConfirmed()
        {
            string machineId = pendingPlacementMachineId;
            if (string.IsNullOrEmpty(machineId)) return;
            if (buildRouter == null || machineTool == null) return;
            // 기존 확정 처리에서 성공했을 때만 모드가 None으로 바뀐다. 배치가 불가능한 칸에서
            // 확정을 눌렀다면 기존 고스트를 그대로 유지하고 새 고스트를 만들지 않는다.
            if (buildRouter.CurrentMode != BuildInputRouter.Mode.None) return;

            machineTool.SelectMachine(machineId);
            buildRouter.SetMode(BuildInputRouter.Mode.PlaceMachine);
            pendingPlacementMachineId = machineId;
            ShowToast("연속 설치 모드 · 취소 버튼으로 종료");
        }

        private void HandlePowerPlacementConfirmed()
        {
            var controller = FindFirstObjectByType<PowerBuildController>();
            if (controller == null || !controller.HasPendingNodePlacement) return;
            if (controller.ConfirmPendingPlacement()) ShowToast("전력 시설을 설치했습니다");
            else ShowToast(controller.LastMessage);
        }

        private static GameObject MoveActionButton(string name, Transform parent, float x, Color? tint = null)
        {
            var go = GameObject.Find(name);
            if (go == null) return null;
            go.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(210f, 56f));
            SeoUIFactory.ApplyButton(go.GetComponent<Button>(), tint);
            return go;
        }

        private void ToggleCategory(Category category)
        {
            if (openCategory.HasValue && openCategory.Value == category)
            {
                CloseCategoryPanel(true);
                return;
            }

            SetCategory(category);
        }

        private void SetCategory(Category category)
        {
            ExitEditMode();
            CancelActiveBuildMode();
            openCategory = category;
            if (dockRoot != null) dockRoot.SetActive(true);
            SetSideMenuVisible(true);
            if (productionPage != null) productionPage.SetActive(category == Category.Production);
            if (logisticsPage != null) logisticsPage.SetActive(category == Category.Logistics);
            if (powerPage != null) powerPage.SetActive(category == Category.Power);
            if (systemPage != null) systemPage.SetActive(category == Category.System);
            if (categoryTitle != null)
                categoryTitle.text = category == Category.Production ? "생산 도구"
                    : category == Category.Logistics ? "물류 도구"
                    : category == Category.Power ? "전력 도구" : "저장·시스템";
            SetTabState(productionTab, category == Category.Production);
            SetTabState(logisticsTab, category == Category.Logistics);
            SetTabState(powerTab, category == Category.Power);
            SetTabState(systemTab, category == Category.System);
            SetTabState(editModeButton, false);
            var controller = FindFirstObjectByType<PowerBuildController>();
            if (category != Category.Power && controller != null && controller.Mode != PowerBuildMode.None)
                controller.SetMode(PowerBuildMode.None);
        }

        // 튜토리얼은 기존 버튼을 대신 만들지 않고 현재 HUD의 해당 페이지를 열어
        // 플레이어가 실제 게임 UI를 그대로 누르게 한다.
        public void OpenProductionForTutorial()
        {
            if (built) SetCategory(Category.Production);
        }

        public void OpenLogisticsForTutorial()
        {
            if (built) SetCategory(Category.Logistics);
        }

        public void OpenPowerForTutorial()
        {
            if (built) SetCategory(Category.Power);
        }

        private void CollapseAfterToolSelection()
        {
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            SetTabState(systemTab, false);
            SetSideMenuVisible(true);
        }

        private void SetSideMenuVisible(bool visible)
        {
            // 메인 카테고리 툴바는 자원/전력/코어 버튼 아래에 항상 유지한다.
            if (sideMenuRoot != null && !sideMenuRoot.activeSelf) sideMenuRoot.SetActive(true);
        }

        private void CloseCategoryPanel(bool cancelBuildMode)
        {
            if (cancelBuildMode) CancelActiveBuildMode();
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            SetTabState(systemTab, false);
            if (!editModeActive) SetTabState(editModeButton, false);

            var controller = FindFirstObjectByType<PowerBuildController>();
            if (controller != null && controller.Mode != PowerBuildMode.None)
                controller.SetMode(PowerBuildMode.None);
        }

        private void CancelActiveBuildMode()
        {
            pendingPlacementMachineId = null;
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            if (buildRouter != null && buildRouter.CurrentMode == BuildInputRouter.Mode.PlaceMachine)
                machineTool?.CancelPlacement();
            if (buildRouter != null && buildRouter.CurrentMode != BuildInputRouter.Mode.None)
                buildRouter.SetMode(BuildInputRouter.Mode.None);
        }

        private static void SetTabState(Button button, bool selected)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = ToolCardIdleColor;

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.color = Color.white;

            var icon = button.transform.Find("Icon")?.GetComponent<Text>();
            if (icon != null)
                icon.color = selected ? Color.white : SeoUITheme.Current.Primary;
        }

        private void UpdatePlacementCostPanel()
        {
            if (placementCostPanel == null) return;
            if (!TryGetActivePlacementCost(out string title, out ResourceAmount[] cost,
                    out SimulationWorld world, out ProcessorInstance core))
            {
                placementCostPanel.SetActive(false);
                placementCostSignature = null;
                return;
            }

            placementCostPanel.SetActive(true);
            string signature = title;
            for (int i = 0; i < cost.Length; i++)
                signature += "|" + cost[i].ResourceId + ":" + cost[i].Amount;

            if (placementCostSignature != signature)
            {
                placementCostSignature = signature;
                RebuildPlacementCostEntries(title, cost, world);
            }

            for (int i = 0; i < placementCostEntries.Count; i++)
            {
                PlacementCostEntry entry = placementCostEntries[i];
                int owned = core != null && entry.ResourceId >= 0 && entry.ResourceId < core.InputBuffer.Length
                    ? core.InputBuffer[entry.ResourceId]
                    : 0;
                bool enough = owned >= entry.Required;
                entry.Amount.text = "필요 " + entry.Required.ToString("N0")
                    + " · 보유 " + owned.ToString("N0");
                entry.Amount.color = enough ? SeoUITheme.Current.Success : SeoUITheme.Current.Danger;
                entry.Background.color = enough
                    ? new Color(0.035f, 0.16f, 0.18f, 0.97f)
                    : new Color(0.24f, 0.055f, 0.055f, 0.97f);
            }
        }

        private bool TryGetActivePlacementCost(out string title, out ResourceAmount[] cost,
            out SimulationWorld world, out ProcessorInstance core)
        {
            title = null;
            cost = null;
            world = null;
            core = null;

            if (simulationDriver == null) simulationDriver = FindFirstObjectByType<SimulationDriver>();
            if (simulationDriver == null || simulationDriver.World == null) return false;
            world = simulationDriver.World;
            int coreIndex = world.CoreProcessorIndex;
            if (coreIndex >= 0 && coreIndex < world.Processors.Count) core = world.Processors[coreIndex];

            if (powerController == null) powerController = FindFirstObjectByType<PowerBuildController>();
            if (powerController != null)
            {
                if (powerController.Mode == PowerBuildMode.Generator)
                    return TryGetMachineBuildCost(world, "Generator", "발전기 설치", out title, out cost);
                if (powerController.Mode == PowerBuildMode.TransmissionTower)
                    return TryGetMachineBuildCost(world, "TransmissionTower", "송전탑 설치", out title, out cost);
                if (powerController.Mode == PowerBuildMode.Cable
                    && world.Database.TryGetResourceId("CopperWire", out int wireId))
                {
                    int amount = CableCostField?.GetRawConstantValue() is int configuredCost
                        ? configuredCost
                        : 1;
                    title = "전선 연결\n1회 기준";
                    cost = new[] { new ResourceAmount(wireId, Mathf.Max(1, amount)) };
                    return true;
                }
            }

            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            if (buildRouter == null) return false;

            if (buildRouter.CurrentMode == BuildInputRouter.Mode.PlaceMachine && machineTool != null
                && !string.IsNullOrEmpty(machineTool.SelectedMachineId))
            {
                string machineId = machineTool.SelectedMachineId;
                return TryGetMachineBuildCost(world, machineId,
                    MachineInfoPresenter.GetMachineDisplayName(machineId) + " 설치", out title, out cost);
            }

            // 벨트도 다른 기계처럼 Bae님 데이터(Machines.json의 "Belt")의 건설비를 우선 보여준다 —
            // 실제 차감액(BeltDragTool.ConcreteCostPerTile)도 같은 데이터에서 나온다. 데이터에 없을
            // 때만 아래 예전 방식(BeltDragTool의 Inspector 값)으로 폴백한다.
            if (buildRouter.CurrentMode == BuildInputRouter.Mode.Belt
                && TryGetMachineBuildCost(world, "Belt", "벨트 설치\n1칸 기준", out title, out cost))
            {
                return true;
            }

            if (buildRouter.CurrentMode == BuildInputRouter.Mode.Belt
                && world.Database.TryGetResourceId("Concrete", out int concreteId))
            {
                if (beltTool == null) beltTool = FindFirstObjectByType<BeltDragTool>();
                int amount = 3;
                if (beltTool != null && BeltCostPerTileField?.GetValue(beltTool) is int configuredCost)
                    amount = configuredCost;
                title = "벨트 설치\n1칸 기준";
                cost = new[] { new ResourceAmount(concreteId, Mathf.Max(1, amount)) };
                return true;
            }

            return false;
        }

        private static bool TryGetMachineBuildCost(SimulationWorld world, string machineId, string label,
            out string title, out ResourceAmount[] cost)
        {
            title = null;
            cost = null;
            if (world == null || !world.Database.TryGetMachineId(machineId, out int id)) return false;
            cost = world.Database.Machines[id].BuildCost;
            if (cost == null || cost.Length == 0) return false;
            title = label;
            return true;
        }

        private void RebuildPlacementCostEntries(string title, ResourceAmount[] cost, SimulationWorld world)
        {
            for (int i = 0; i < placementCostEntries.Count; i++)
                if (placementCostEntries[i].Root != null) Destroy(placementCostEntries[i].Root);
            placementCostEntries.Clear();
            placementCostTitle.text = "설치 필요 자원\n" + title;

            float cardWidth = Mathf.Min(224f, 690f / Mathf.Max(1, cost.Length));
            for (int i = 0; i < cost.Length; i++)
            {
                ResourceAmount requirement = cost[i];
                if (requirement.ResourceId < 0 || requirement.ResourceId >= world.Database.Resources.Count) continue;
                ResourceRuntime resource = world.Database.Resources[requirement.ResourceId];
                var card = SeoUIFactory.CreatePanel(placementCostContent, "Cost_" + resource.Key,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(i * (cardWidth + 6f), 0f), new Vector2(cardWidth, 76f),
                    new Color(0.035f, 0.16f, 0.18f, 0.97f));
                card.rectTransform.pivot = new Vector2(0f, 0.5f);
                card.raycastTarget = false;

                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconObject.transform.SetParent(card.transform, false);
                SeoUIFactory.SetRect(iconObject.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(58f, 58f));
                var icon = iconObject.GetComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                RecipeResourceIconCache.Assign(icon, resource.Key, resource.PrefabName, resource.Color);

                var name = SeoUIFactory.CreateTMPText(card.transform, "Name", resource.DisplayName, 16,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                SeoUIFactory.SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(70f, -7f), new Vector2(-76f, 30f));
                name.enableAutoSizing = true;
                name.fontSizeMin = 12;
                name.fontSizeMax = 16;
                name.overflowMode = TMPro.TextOverflowModes.Truncate;

                var amount = SeoUIFactory.CreateTMPText(card.transform, "Amount", string.Empty, 14,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                SeoUIFactory.SetRect(amount.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(70f, 8f), new Vector2(-76f, 30f));
                amount.enableAutoSizing = true;
                amount.fontSizeMin = 11;
                amount.fontSizeMax = 14;

                placementCostEntries.Add(new PlacementCostEntry
                {
                    ResourceId = requirement.ResourceId,
                    Required = requirement.Amount,
                    Root = card.gameObject,
                    Background = card,
                    Amount = amount,
                });
            }
        }

        private void UpdateContextActions()
        {
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            var mode = buildRouter != null ? buildRouter.CurrentMode : BuildInputRouter.Mode.None;
            bool placingMachine = mode == BuildInputRouter.Mode.PlaceMachine;
            bool placingMiner = placingMachine && machineTool != null && machineTool.SelectedMachineId == "Miner";
            if (powerController == null) powerController = FindFirstObjectByType<PowerBuildController>();
            bool placingPower = powerController != null && powerController.HasPendingNodePlacement;
            bool placingGenerator = placingPower && powerController.Mode == PowerBuildMode.Generator;
            var moveTool = GroupMoveTool.ActiveFor(buildRouter);
            bool movingSelection = moveTool != null;

            // 발전기도 단일 연료 입력 방향을 정해야 하므로, 고스트를 놓는 동안 같은 회전
            // 버튼을 노출한다. RotatePlacementButton이 발전기 모드에서는 전력 도구로 전달한다.
            if (rotateButton != null)
            {
                rotateButton.SetActive((placingMachine && !placingMiner) || placingGenerator);
                SetActionButtonX(rotateButton, -232f);
            }
            if (confirmButton != null)
            {
                confirmButton.SetActive(placingMachine || placingPower);
                SetActionButtonX(confirmButton, placingMiner || (placingPower && !placingGenerator) ? -116f : 0f);
            }
            if (demolishConfirmButton != null)
            {
                demolishConfirmButton.SetActive(mode == BuildInputRouter.Mode.Demolish);
                if (mode == BuildInputRouter.Mode.Demolish) SetActionButtonX(demolishConfirmButton, -232f);
            }
            if (groupMoveButton != null)
            {
                groupMoveButton.gameObject.SetActive(editModeActive && (mode == BuildInputRouter.Mode.Demolish || movingSelection));
                groupMoveButton.GetComponentInChildren<Text>(true).text = movingSelection ? "이동 확정" : "이동";
                groupMoveButton.interactable = !movingSelection || moveTool.CanConfirm;
            }
            if (groupMoveRotateButton != null) groupMoveRotateButton.gameObject.SetActive(movingSelection);
            if (cancelButton != null)
            {
                cancelButton.SetActive(mode != BuildInputRouter.Mode.None || placingPower);
                float x = placingMachine ? (placingMiner ? 116f : 232f)
                    : placingGenerator ? 232f : placingPower ? 116f
                    : mode == BuildInputRouter.Mode.Demolish || movingSelection ? 232f : 0f;
                SetActionButtonX(cancelButton, x);
            }

            var parent = rotateButton != null ? rotateButton.transform.parent.gameObject
                : demolishConfirmButton != null ? demolishConfirmButton.transform.parent.gameObject : null;
            if (parent != null) parent.SetActive(mode != BuildInputRouter.Mode.None || placingPower);
        }

        private void SetActionButtonX(GameObject button, float x)
        {
            if (button == null) return;
            var rt = button.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(x * contextButtonStep / 232f, 0f);
        }

        private void UpdatePowerStatus()
        {
            if (powerText == null) return;
            if (powerGrid == null) powerGrid = FindFirstObjectByType<PowerGridSystem>();
            var grid = powerGrid;
            if (grid == null)
            {
                powerText.text = "전력 시스템 준비 중";
                return;
            }

            bool shortage = grid.RequestedPower > grid.AvailablePower;
            const string warningColor = "#FF3028";
            string warningLight = shortage ? $"<color={warningColor}>● 전력 부족</color>" : "<color=#5CD99A>● 전력 정상</color>";
            string powerAmount = shortage
                ? $"<color={warningColor}>{grid.RequestedPower} / {grid.AvailablePower}</color>"
                : $"{grid.RequestedPower} / {grid.AvailablePower}";

            powerText.color = SeoUITheme.Current.Text;
            // 같은 문자열을 매 프레임 다시 넣으면 Text가 매번 레이아웃/메시를 다시 만든다 — 바뀔 때만 넣는다.
            string text =
                $"{warningLight}   사용 / 공급  {powerAmount} MW\n" +
                $"가동 기계   {grid.PoweredMachineCount} / {grid.TotalMachineCount}대";
            if (text != lastPowerText)
            {
                powerText.text = text;
                lastPowerText = text;
            }

            if (powerStatusButton != null)
            {
                var image = powerStatusButton.GetComponent<Image>();
                if (image != null)
                    image.color = ToolCardIdleColor;
                var label = powerStatusButton.GetComponentInChildren<Text>(true);
                if (label != null) label.color = Color.white;
            }
        }

        private void DecorateRecipePanel()
        {
            var panel = RecipeSelectionPanel.Instance;
            if (panel == null) return;
            var root = panel.gameObject;
            if (root.transform.parent != safeRoot) root.transform.SetParent(safeRoot, false);
            // 닫혀 있는 동안엔 꾸밀 이유가 없다 — 예전엔 닫혀 있어도 매 프레임 레이아웃 강제 재계산까지 돌았다.
            if (!root.activeSelf) return;
            root.transform.SetAsLastSibling();
            var rt = root.GetComponent<RectTransform>();
            SeoUIFactory.SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 820f));
            var rootImage = root.GetComponent<Image>();
            SeoUIFactory.ApplyPanel(rootImage);
            if (rootImage != null) rootImage.raycastTarget = true;
            var canvasGroup = root.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            var container = root.transform.Find("RecipeButtonContainer") as RectTransform;
            if (container != null)
            {
                container.offsetMin = new Vector2(28f, 28f);
                container.offsetMax = new Vector2(-28f, -104f);
                var verticalLayout = container.GetComponent<VerticalLayoutGroup>();
                if (verticalLayout != null)
                {
                    verticalLayout.spacing = 10f;
                    verticalLayout.childControlHeight = false;
                    verticalLayout.childForceExpandHeight = false;
                }
                for (int i = 0; i < container.childCount; i++)
                {
                    var button = container.GetChild(i).GetComponent<Button>();
                    if (button == null) continue;
                    ApplyCardBackground(button);
                    var buttonImage = button.GetComponent<Image>();
                    if (buttonImage != null) buttonImage.raycastTarget = true;
                    button.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 104f);
                    UpdateRecipeButtonVisual(button);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            }

            if (root.transform.Find("SeoRecipeHeader") == null)
            {
                var header = SeoUIFactory.CreateTMPText(root.transform, "SeoRecipeHeader", "레시피 선택", 27,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                SeoUIFactory.SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(-16f, -14f), new Vector2(-120f, 62f));
                var close = SeoUIFactory.CreateTMPButton(root.transform, "SeoRecipeClose", "닫기", panel.Close,
                    SeoUITheme.Current.Danger);
                SeoUIFactory.SetRect(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                    new Vector2(-20f, -18f), new Vector2(100f, 48f));
                close.transform.SetAsLastSibling();
            }
            else
            {
                var close = root.transform.Find("SeoRecipeClose");
                if (close != null) close.SetAsLastSibling();
            }
        }

        // GameObject.Find는 활성 오브젝트 전체를 훑는다 — 매 프레임 두 번씩 부르면 벨트/기계가
        // 많은 공장에서 이것만으로 수십 ms가 나왔다. 한 번 찾은 건 들고 있다가 켜져 있을 때만 끄고,
        // 아직 못 찾은 건 1초에 한 번만 다시 찾는다(늦게 생성되는 경우 대비).
        private void HideLegacyPowerPanel()
        {
            if ((legacyPowerPanel == null || legacyPowerPanelToggle == null) && Time.unscaledTime >= nextLegacyPanelSearch)
            {
                nextLegacyPanelSearch = Time.unscaledTime + 1f;
                if (legacyPowerPanel == null) legacyPowerPanel = GameObject.Find("FactoryPowerPanel");
                if (legacyPowerPanelToggle == null) legacyPowerPanelToggle = GameObject.Find("FactoryPowerPanelToggle");
            }
            if (legacyPowerPanel != null && legacyPowerPanel.activeSelf) legacyPowerPanel.SetActive(false);
            if (legacyPowerPanelToggle != null && legacyPowerPanelToggle.activeSelf) legacyPowerPanelToggle.SetActive(false);
        }

        private static void UpdateRecipeButtonVisual(Button button)
        {
            const string prefix = "Recipe_";
            if (button == null || !button.name.StartsWith(prefix)) return;
            var driver = FindFirstObjectByType<Factory.Simulation.SimulationDriver>();
            if (driver == null || driver.World == null) return;
            string key = button.name.Substring(prefix.Length);
            if (!driver.World.Database.TryGetRecipeId(key, out int recipeId)) return;
            var recipe = driver.World.Database.Recipes[recipeId];
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            if (button.transform.Find("SeoRecipeVisual") != null) return;

            var visual = new GameObject("SeoRecipeVisual", typeof(RectTransform));
            visual.transform.SetParent(button.transform, false);
            SeoUIFactory.SetRect(visual.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, -10f));

            CreateRecipeCaption(visual.transform, "InputCaption", "필요", new Vector2(4f, 0f), 48f,
                SeoUITheme.Current.Warning);
            const float inputStart = 56f;
            const float inputAreaWidth = 310f;
            int inputCount = Mathf.Max(1, recipe.Inputs.Length);
            float inputTileWidth = inputAreaWidth / inputCount;
            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                var amount = recipe.Inputs[i];
                var resource = driver.World.Database.Resources[amount.ResourceId];
                CreateRecipeResourceTile(visual.transform, "Input_" + i, resource, amount.Amount,
                    new Vector2(inputStart + i * inputTileWidth, 0f), inputTileWidth - 4f);
            }

            var arrow = SeoUIFactory.CreateTMPText(visual.transform, "FlowArrow", "▶", 34,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRecipeElementRect(arrow.rectTransform, new Vector2(372f, 0f), new Vector2(38f, 82f));
            arrow.color = SeoUITheme.Current.Primary;

            CreateRecipeCaption(visual.transform, "OutputCaption", "생산", new Vector2(414f, 0f), 50f,
                SeoUITheme.Current.Success);
            const float outputStart = 468f;
            const float outputAreaWidth = 220f;
            int outputCount = Mathf.Max(1, recipe.Outputs.Length);
            float outputTileWidth = outputAreaWidth / outputCount;
            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                var amount = recipe.Outputs[i];
                var resource = driver.World.Database.Resources[amount.ResourceId];
                CreateRecipeResourceTile(visual.transform, "Output_" + i, resource, amount.Amount,
                    new Vector2(outputStart + i * outputTileWidth, 0f), outputTileWidth - 4f);
            }

            var time = SeoUIFactory.CreateTMPText(visual.transform, "ProcessTime",
                $"시간 {recipe.ProcessSeconds:0.#}초", 18, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRecipeElementRect(time.rectTransform, new Vector2(694f, 0f), new Vector2(124f, 82f));
            time.color = SeoUITheme.Current.Muted;
        }

        private static void CreateRecipeCaption(Transform parent, string name, string value, Vector2 position,
            float width, Color color)
        {
            var caption = SeoUIFactory.CreateTMPText(parent, name, value, 17, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRecipeElementRect(caption.rectTransform, position, new Vector2(width, 82f));
            caption.color = color;
        }

        private static void CreateRecipeResourceTile(Transform parent, string name, ResourceRuntime resource,
            int amount, Vector2 position, float width)
        {
            var tile = new GameObject(name, typeof(RectTransform));
            tile.transform.SetParent(parent, false);
            SetRecipeElementRect(tile.GetComponent<RectTransform>(), position, new Vector2(width, 82f));

            float iconSize = width >= 180f ? 68f : width >= 120f ? 58f : 46f;

            var iconObject = new GameObject("ResourceIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(tile.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            SetRecipeElementRect(iconRect, Vector2.zero, new Vector2(iconSize, iconSize));
            var icon = iconObject.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            RecipeResourceIconCache.Assign(icon, resource.Key, resource.PrefabName, resource.Color);

            var info = SeoUIFactory.CreateTMPText(tile.transform, "Info",
                resource.DisplayName + "\n×" + amount, 17, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRecipeElementRect(info.rectTransform, new Vector2(iconSize + 6f, 0f),
                new Vector2(Mathf.Max(30f, width - iconSize - 8f), 76f));
            info.enableAutoSizing = true;
            info.fontSizeMin = 11;
            info.fontSizeMax = 17;
            info.textWrappingMode = TMPro.TextWrappingModes.Normal;
            info.overflowMode = TMPro.TextOverflowModes.Truncate;
        }

        private static void SetRecipeElementRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            SeoUIFactory.SetRect(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), position, size);
        }

        private void ShowToast(string message)
        {
            if (toastText == null) return;
            toastText.text = message;
            toastUntil = Time.unscaledTime + 2.2f;
            toastRoot.SetActive(true);
        }
    }

    internal sealed class TopViewIconCache : MonoBehaviour
    {
        private static TopViewIconCache instance;
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<Image>> Waiting = new Dictionary<string, List<Image>>();

        public static void Assign(Image target, string prefabKey)
        {
            if (target == null || string.IsNullOrEmpty(prefabKey)) return;
            target.color = Color.white;
            if (Sprites.TryGetValue(prefabKey, out var sprite))
            {
                target.sprite = sprite;
                return;
            }

            if (Waiting.TryGetValue(prefabKey, out var targets))
            {
                targets.Add(target);
                return;
            }

            Waiting[prefabKey] = new List<Image> { target };
            EnsureInstance().StartCoroutine(LoadAndRender(prefabKey));
        }

        public static void Assign(Image target, GameObject prefab, string cacheKey)
        {
            if (target == null || prefab == null) return;
            target.color = Color.white;
            string key = "Direct_" + cacheKey;
            if (!Sprites.TryGetValue(key, out var sprite))
            {
                sprite = RecipeResourceIconCache.RenderPrefab(prefab, true);
                if (sprite != null) Sprites[key] = sprite;
            }
            target.sprite = sprite;
        }

        private static TopViewIconCache EnsureInstance()
        {
            if (instance != null) return instance;
            var go = new GameObject("[Seo] Top View Icon Cache");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TopViewIconCache>();
            return instance;
        }

        private static IEnumerator LoadAndRender(string prefabKey)
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(prefabKey);
            yield return handle;
            Sprite sprite = handle.Result != null
                ? RecipeResourceIconCache.RenderPrefab(handle.Result, true)
                : null;
            if (sprite != null) Sprites[prefabKey] = sprite;
            Addressables.Release(handle);

            if (!Waiting.TryGetValue(prefabKey, out var targets)) yield break;
            Waiting.Remove(prefabKey);
            if (sprite == null) yield break;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null) targets[i].sprite = sprite;
        }
    }

    // 게임에서 실제 사용하는 Addressables 자원 프리팹을 한 번씩 촬영해 레시피용 썸네일로 캐시한다.
    // 레시피 패널을 다시 열 때는 생성된 Sprite만 재사용하므로 프리팹 로드와 렌더 비용이 반복되지 않는다.
    internal sealed class RecipeResourceIconCache : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private const int TextureSize = 256;
        private static RecipeResourceIconCache instance;
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> FallbackSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<Image>> Waiting = new Dictionary<string, List<Image>>();

        public static void Assign(Image target, string resourceKey, string configuredPrefabKey, Color fallbackColor)
        {
            if (target == null) return;
            target.color = Color.white;
            string prefabKey = string.IsNullOrEmpty(resourceKey)
                ? configuredPrefabKey
                : "Prefab_Item_" + resourceKey;
            if (!string.IsNullOrEmpty(prefabKey))
            {
                var fixedIcon = Resources.Load<Sprite>("ResourceIcons/" + prefabKey);
                if (fixedIcon != null)
                {
                    target.sprite = fixedIcon;
                    return;
                }
            }
            if (!string.IsNullOrEmpty(prefabKey) && Sprites.TryGetValue(prefabKey, out var cached))
            {
                target.sprite = cached;
                return;
            }

            string fallbackKey = string.IsNullOrEmpty(prefabKey)
                ? ColorUtility.ToHtmlStringRGBA(fallbackColor)
                : prefabKey;
            if (!FallbackSprites.TryGetValue(fallbackKey, out var fallback))
            {
                fallback = CreateFallbackSprite(fallbackColor);
                FallbackSprites[fallbackKey] = fallback;
            }
            target.sprite = fallback;
            if (string.IsNullOrEmpty(prefabKey)) return;

            if (Waiting.TryGetValue(prefabKey, out var targets))
            {
                targets.Add(target);
                return;
            }

            Waiting[prefabKey] = new List<Image> { target };
            EnsureInstance().StartCoroutine(LoadAndRender(prefabKey));
        }

        private static RecipeResourceIconCache EnsureInstance()
        {
            if (instance != null) return instance;
            var go = new GameObject("[Seo] Recipe Resource Icon Cache");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RecipeResourceIconCache>();
            return instance;
        }

        private static IEnumerator LoadAndRender(string prefabKey)
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(prefabKey);
            yield return handle;

            Sprite sprite = handle.Result != null ? RenderPrefab(handle.Result, false) : null;
            if (sprite != null) Sprites[prefabKey] = sprite;
            Addressables.Release(handle);

            if (!Waiting.TryGetValue(prefabKey, out var targets)) yield break;
            Waiting.Remove(prefabKey);
            if (sprite == null) yield break;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null) targets[i].sprite = sprite;
            }
        }

        internal static Sprite RenderPrefab(GameObject prefab, bool topDown)
        {
            var previewRoot = new GameObject("RecipeIconPreview");
            previewRoot.transform.position = new Vector3(10000f, 10000f, 10000f);
            var model = Instantiate(prefab, previewRoot.transform);
            model.transform.localPosition = Vector3.zero;
            SetLayerRecursively(model, PreviewLayer);
            var behaviours = model.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) behaviours[i].enabled = false;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer) continue;
                if (!hasBounds) { bounds = renderers[i].bounds; hasBounds = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            if (!hasBounds)
            {
                previewRoot.SetActive(false);
                Destroy(previewRoot);
                return null;
            }

            var cameraObject = new GameObject("RecipeIconCamera", typeof(Camera));
            var previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.clear;
            previewCamera.orthographic = true;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            float extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            previewCamera.orthographicSize = Mathf.Max(0.2f, extent * 1.18f);
            float distance = Mathf.Max(2f, extent * 5f);
            if (topDown)
            {
                var gameCamera = Camera.main;
                if (gameCamera != null)
                {
                    previewCamera.transform.rotation = gameCamera.transform.rotation;
                    previewCamera.transform.position = bounds.center - previewCamera.transform.forward * distance;
                }
                else
                {
                    Vector3 viewDirection = new Vector3(1f, 1.35f, -1f).normalized;
                    previewCamera.transform.position = bounds.center + viewDirection * distance;
                    previewCamera.transform.LookAt(bounds.center);
                }
            }
            else
            {
                Vector3 viewDirection = new Vector3(1f, 0.85f, -1f).normalized;
                previewCamera.transform.position = bounds.center + viewDirection * distance;
                previewCamera.transform.LookAt(bounds.center);
            }
            Vector3 right = previewCamera.transform.right;
            Vector3 up = previewCamera.transform.up;
            Vector3 extents = bounds.extents;
            float projectedWidth = Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.y) * extents.y
                + Mathf.Abs(right.z) * extents.z;
            float projectedHeight = Mathf.Abs(up.x) * extents.x + Mathf.Abs(up.y) * extents.y
                + Mathf.Abs(up.z) * extents.z;
            previewCamera.aspect = 1f;
            previewCamera.orthographicSize = Mathf.Max(0.2f, Mathf.Max(projectedWidth, projectedHeight) * 1.18f);
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = distance * 3f;

            var lightObject = new GameObject("RecipeIconLight", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(0.9f, 0.96f, 1f);
            light.cullingMask = 1 << PreviewLayer;
            lightObject.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            var renderTexture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 4;
            previewCamera.targetTexture = renderTexture;
            var previous = RenderTexture.active;
            previewCamera.Render();
            RenderTexture.active = renderTexture;
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, TextureSize, TextureSize), 0, 0);
            texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear;
            RenderTexture.active = previous;
            previewCamera.targetTexture = null;
            renderTexture.Release();

            previewRoot.SetActive(false);
            previewCamera.enabled = false;
            light.enabled = false;
            Destroy(renderTexture);
            Destroy(lightObject);
            Destroy(cameraObject);
            Destroy(previewRoot);
            return Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), 100f);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        private static Sprite CreateFallbackSprite(Color color)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Abs(x - center.x) + Mathf.Abs(y - center.y);
                    float alpha = Mathf.Clamp01((size * 0.44f - distance) / 2f);
                    float highlight = Mathf.Lerp(0.65f, 1.2f, (float)y / size);
                    pixels[y * size + x] = new Color(
                        Mathf.Clamp01(color.r * highlight),
                        Mathf.Clamp01(color.g * highlight),
                        Mathf.Clamp01(color.b * highlight), alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear;
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
