using Factory.Building;
using Factory.Simulation;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // 배치 고스트의 현재 회전 방향을 기준으로 입력·출력 위치를 실시간 표시한다.
    public sealed class GhostPortPreview : MonoBehaviour
    {
        private MachineGhostTool tool;
        private Camera targetCamera;
        private GameObject autoBadge;
        private readonly List<GameObject> inputBadges = new List<GameObject>();
        private readonly List<GameObject> outputBadges = new List<GameObject>();
        private readonly List<GameObject> routingBadges = new List<GameObject>();

        private static readonly Vector2Int[] FourDirs =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
        };

        public void Initialize(MachineGhostTool machineGhostTool)
        {
            tool = machineGhostTool;
            if (targetCamera == null) targetCamera = Camera.main;
            EnsureBadges();
        }

        private void LateUpdate()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (!MachineGhostAdapter.TryRead(tool, out var selection))
            {
                SetPortVisibility(false);
                if (autoBadge != null) autoBadge.SetActive(false);
                SetRoutingVisibility(false);
                return;
            }

            Bounds bounds = CalculateBounds(selection.Ghost);
            Vector3 center = new Vector3(bounds.center.x, bounds.max.y + 0.15f, bounds.center.z);

            if (selection.MachineId == "Miner")
            {
                SetPortVisibility(false);
                autoBadge.SetActive(true);
                SetRoutingVisibility(false);
                SetBadgeTransform(autoBadge, center, 0.005f);
                return;
            }

            if (selection.MachineId == "Splitter" || selection.MachineId == "Merger"
                || selection.MachineId == "CrossBelt")
            {
                SetPortVisibility(false);
                autoBadge.SetActive(false);
                SetRoutingVisibility(true);
                bool splitter = selection.MachineId == "Splitter";
                bool crossBelt = selection.MachineId == "CrossBelt";
                // Match the two independent axes created by BeltDragTool.PlaceCrossableTile.
                Vector2Int crossAxis = new Vector2Int(-selection.Facing.y, selection.Facing.x);
                for (int i = 0; i < FourDirs.Length; i++)
                {
                    Vector2Int dir = FourDirs[i];
                    bool input = crossBelt
                        ? dir == -selection.Facing || dir == -crossAxis
                        : splitter ? dir == -selection.Facing : dir != selection.Facing;
                    Vector2Int flowDirection = input ? -dir : dir;
                    SetBadgeLabel(routingBadges[i], DirectionArrow(flowDirection), input);
                    float offset = dir.x != 0 ? bounds.extents.x + 0.22f : bounds.extents.z + 0.22f;
                    SetBadgeTransform(routingBadges[i], center + new Vector3(dir.x, 0f, dir.y) * offset, 0.0065f);
                }
                return;
            }

            if (selection.MachineId == "MiniCore")
            {
                SetPortVisibility(false);
                autoBadge.SetActive(false);
                SetRoutingVisibility(true);
                for (int i = 0; i < FourDirs.Length; i++)
                {
                    Vector2Int dir = FourDirs[i];
                    SetBadgeLabel(routingBadges[i], dir.x != 0 ? "↔" : "↕", true);
                    float offset = dir.x != 0 ? bounds.extents.x + 0.22f : bounds.extents.z + 0.22f;
                    SetBadgeTransform(routingBadges[i], center + new Vector3(dir.x, 0f, dir.y) * offset, 0.0065f);
                }
                return;
            }

            int inputCount = selection.Facing.x != 0 ? selection.Footprint.y : selection.Footprint.x;
            int outputCount = selection.MachineId == "Synthesizer" ? 1 : inputCount;
            EnsurePortBadgeCount(inputCount, outputCount);
            SetPortVisibility(true, inputCount, outputCount);
            autoBadge.SetActive(false);
            SetRoutingVisibility(false);
            Vector3 direction = new Vector3(selection.Facing.x, 0f, selection.Facing.y).normalized;
            string flowArrow = DirectionArrow(selection.Facing);
            float sideOffset = Mathf.Abs(direction.x) > 0.5f
                ? bounds.extents.x + 0.22f
                : bounds.extents.z + 0.22f;
            Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x);
            for (int i = 0; i < inputCount; i++)
            {
                SetBadgeLabel(inputBadges[i], flowArrow, true);
                float laneOffset = (i - (inputCount - 1) * 0.5f) * GridUtility.CellSize;
                SetBadgeTransform(inputBadges[i], center - direction * sideOffset
                    + perpendicular * laneOffset, 0.0065f);
            }
            for (int i = 0; i < outputCount; i++)
            {
                SetBadgeLabel(outputBadges[i], flowArrow, false);
                float laneOffset = (i - (outputCount - 1) * 0.5f) * GridUtility.CellSize;
                SetBadgeTransform(outputBadges[i], center + direction * sideOffset
                    + perpendicular * laneOffset, 0.0065f);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < inputBadges.Count; i++) if (inputBadges[i] != null) Destroy(inputBadges[i]);
            for (int i = 0; i < outputBadges.Count; i++) if (outputBadges[i] != null) Destroy(outputBadges[i]);
            if (autoBadge != null) Destroy(autoBadge);
            for (int i = 0; i < routingBadges.Count; i++) if (routingBadges[i] != null) Destroy(routingBadges[i]);
        }

        private void EnsureBadges()
        {
            EnsurePortBadgeCount(1, 1);
            if (autoBadge == null) autoBadge = CreateBadge("Ghost_AUTO", "AUTO → CORE", new Color(0.95f, 0.72f, 0.15f), new Vector2(130f, 30f), 15, false);
            if (routingBadges.Count == 0)
            {
                for (int i = 0; i < 4; i++)
                    routingBadges.Add(CreateBadge("Ghost_ROUTE_" + i, "↔", new Color(0.1f, 0.78f, 1f), new Vector2(52f, 48f), 24, true));
            }
            SetPortVisibility(false);
            autoBadge.SetActive(false);
            SetRoutingVisibility(false);
        }

        private void EnsurePortBadgeCount(int inputCount, int outputCount)
        {
            while (inputBadges.Count < inputCount)
                inputBadges.Add(CreateBadge("Ghost_IN_" + inputBadges.Count, "▶",
                    new Color(0.05f, 0.78f, 1f), new Vector2(52f, 48f), 24, true));
            while (outputBadges.Count < outputCount)
                outputBadges.Add(CreateBadge("Ghost_OUT_" + outputBadges.Count, "▶",
                    new Color(1f, 0.48f, 0.05f), new Vector2(52f, 48f), 24, true));
        }

        private void SetPortVisibility(bool visible, int inputCount = 0, int outputCount = 0)
        {
            for (int i = 0; i < inputBadges.Count; i++)
                inputBadges[i].SetActive(visible && i < inputCount);
            for (int i = 0; i < outputBadges.Count; i++)
                outputBadges[i].SetActive(visible && i < outputCount);
        }

        private void SetRoutingVisibility(bool visible)
        {
            for (int i = 0; i < routingBadges.Count; i++) routingBadges[i].SetActive(visible);
        }

        private static void SetBadgeLabel(GameObject badge, string label, bool input)
        {
            var text = badge.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = label;
            var image = badge.transform.Find("Background")?.GetComponent<Image>();
            Color color = input ? new Color(0.05f, 0.78f, 1f) : new Color(1f, 0.48f, 0.05f);
            if (image != null) image.color = new Color(color.r * 0.78f, color.g * 0.78f, color.b * 0.78f, 0.99f);
        }

        private static string DirectionArrow(Vector2Int direction)
        {
            if (direction == Vector2Int.right) return "▶";
            if (direction == Vector2Int.left) return "◀";
            if (direction == Vector2Int.up) return "▲";
            return "▼";
        }

        private void SetBadgeTransform(GameObject badge, Vector3 position, float scale)
        {
            badge.transform.position = position;
            if (targetCamera != null) badge.transform.rotation = targetCamera.transform.rotation;
            badge.transform.localScale = Vector3.one * scale;
        }

        private static Bounds CalculateBounds(GameObject ghost)
        {
            var renderers = ghost.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(ghost.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static GameObject CreateBadge(string objectName, string label, Color color, Vector2 size,
            int fontSize, bool highContrast)
        {
            var root = new GameObject(objectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            root.GetComponent<RectTransform>().sizeDelta = size;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            float colorStrength = highContrast ? 0.78f : 0.45f;
            background.GetComponent<Image>().color = new Color(color.r * colorStrength, color.g * colorStrength,
                color.b * colorStrength, highContrast ? 0.99f : 0.96f);

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = SeoUITheme.Current.FontAsset;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;
            if (highContrast)
            {
                var outline = textObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
            }
            return root;
        }
    }
}
