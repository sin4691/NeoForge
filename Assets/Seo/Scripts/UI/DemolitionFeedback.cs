using System.Collections.Generic;
using System.Reflection;
using Factory.Building;
using Factory.Buildings;
using Factory.Simulation;
using Seo.Building;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Seo.UI
{
    // 공용 DemolishTool의 선택 결과를 읽어 철거 전 대상과 위험도를 보여주는 UI 어댑터.
    // 실제 삭제와 코어 보호 규칙은 기존 철거 도구가 그대로 담당한다.
    public sealed class DemolitionFeedback : MonoBehaviour
    {
        private static readonly FieldInfo SelectedField = typeof(DemolishTool).GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SelectionBoxField = typeof(DemolishTool).GetField("selectionBox", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DriverField = typeof(DemolishTool).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<(CellOccupantType type, int index)> currentSelection = new List<(CellOccupantType, int)>();
        private readonly List<(CellOccupantType type, int index)> lastSelection = new List<(CellOccupantType, int)>();
        private readonly Dictionary<Renderer, MaterialPropertyBlock> originalBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();

        private BuildInputRouter router;
        private DemolishTool demolishTool;
        private SimulationDriver driver;
        private GameObject panelRoot;
        private Text summaryText;
        private Button confirmButton;
        private LineRenderer selectionOutline;
        private bool wasDemolishMode;
        private float nextDiscovery;
        private float toastUntil;
        private string toastMessage;
        private int selectionSignature = int.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            SceneManager.sceneLoaded -= CreateRuntimeInstance;
            SceneManager.sceneLoaded += CreateRuntimeInstance;
        }

        private static void CreateRuntimeInstance(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main") return;
            if (FindFirstObjectByType<DemolitionFeedback>() != null) return;
            new GameObject("[Seo] Demolition Feedback").AddComponent<DemolitionFeedback>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextDiscovery)
            {
                Discover();
                nextDiscovery = Time.unscaledTime + 0.4f;
            }

            if (router == null || demolishTool == null || driver == null || driver.World == null || !EnsureUI()) return;

            bool demolishMode = router.CurrentMode == BuildInputRouter.Mode.Demolish;
            if (GroupMoveTool.ActiveFor(router) is GroupMoveTool moveTool)
            {
                if (wasDemolishMode) HandleDemolishModeEnded();
                wasDemolishMode = false;
                RestoreHighlights();
                SetOutlineVisible(false);
                if (confirmButton != null) confirmButton.interactable = false;
                panelRoot.SetActive(true);
                SetSummary(moveTool.Status, moveTool.CanConfirm ? SeoUITheme.Current.Success : SeoUITheme.Current.Warning);
                return;
            }
            if (!demolishMode)
            {
                if (wasDemolishMode) HandleDemolishModeEnded();
                wasDemolishMode = false;
                RestoreHighlights();
                SetOutlineVisible(false);
                if (confirmButton != null) confirmButton.interactable = false;

                bool showingToast = Time.unscaledTime < toastUntil;
                panelRoot.SetActive(showingToast);
                if (showingToast) SetSummary(toastMessage, SeoUITheme.Current.Success);
                return;
            }

            wasDemolishMode = true;
            panelRoot.SetActive(true);
            ReadSelection();
            UpdateOutline();
            UpdateSelectionUI();
        }

        private void Discover()
        {
            if (router == null) router = FindFirstObjectByType<BuildInputRouter>();
            if (demolishTool == null) demolishTool = FindFirstObjectByType<DemolishTool>();
            if (driver == null && demolishTool != null) driver = DriverField?.GetValue(demolishTool) as SimulationDriver;
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
        }

        private bool EnsureUI()
        {
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject == null) return false;
            Transform parent = canvasObject.transform.Find("SafeArea") ?? canvasObject.transform;

            if (panelRoot == null)
            {
                var panel = SeoUIFactory.CreatePanel(parent, "SeoDemolitionFeedback", new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(0f, 340f), new Vector2(920f, 112f),
                    new Color(0.13f, 0.025f, 0.025f, 0.97f));
                panel.rectTransform.pivot = new Vector2(0.5f, 0f);
                panelRoot = panel.gameObject;
                summaryText = SeoUIFactory.CreateTMPText(panel.transform, "Summary", string.Empty, 24,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                summaryText.rectTransform.offsetMin = new Vector2(24f, 8f);
                summaryText.rectTransform.offsetMax = new Vector2(-24f, -8f);
                panelRoot.SetActive(false);
            }

            var actionBar = parent.Find("SeoContextBar") as RectTransform;
            var safeRect = parent as RectTransform;
            if (actionBar != null && safeRect != null)
            {
                var summaryRect = (RectTransform)panelRoot.transform;
                summaryRect.anchoredPosition = new Vector2(0f,
                    actionBar.anchoredPosition.y + actionBar.rect.height + 12f);
                summaryRect.sizeDelta = new Vector2(Mathf.Min(920f, safeRect.rect.width - 32f), 112f);
            }

            var confirmObject = GameObject.Find("DemolishConfirmButton");
            if (confirmObject != null)
            {
                confirmButton = confirmObject.GetComponent<Button>();
                StyleActionButton(confirmButton, "철거 확정");
            }

            return panelRoot != null;
        }

        private static void StyleActionButton(Button button, string labelText)
        {
            if (button == null) return;
            SeoUIFactory.ApplyButton(button);
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            label.text = labelText;
            label.fontStyle = TMPro.FontStyles.Bold;
            label.color = SeoUITheme.Current.Text;
        }

        private void ReadSelection()
        {
            currentSelection.Clear();
            var selected = SelectedField?.GetValue(demolishTool) as IEnumerable<(CellOccupantType type, int index)>;
            if (selected == null) return;
            foreach (var entry in selected) currentSelection.Add(entry);
        }

        private void UpdateSelectionUI()
        {
            bool hasTargets = demolishTool != null && demolishTool.HasSelection;
            if (confirmButton != null) confirmButton.interactable = hasTargets;

            int signature = currentSelection.Count;
            for (int i = 0; i < currentSelection.Count; i++)
                signature = unchecked(signature * 397 ^ ((int)currentSelection[i].type * 1000003 + currentSelection[i].index));

            if (signature != selectionSignature)
            {
                selectionSignature = signature;
                RestoreHighlights();
                ApplyHighlights();
                lastSelection.Clear();
                lastSelection.AddRange(currentSelection);
            }

            if (!hasTargets)
            {
                SetSummary("영역을 드래그한 뒤 철거 또는 이동을 누르세요\n이동: 기계·벨트만 · 코어와 전력 시설 제외", SeoUITheme.Current.Warning);
                return;
            }

            if (currentSelection.Count == 0)
            {
                SetSummary("철거 예정 · 전력 시설/전선 포함\n철거 확정을 누르면 선택 영역의 전력 시설을 제거합니다",
                    SeoUITheme.Current.Danger);
                return;
            }

            int machineCount = 0;
            int beltCount = 0;
            var names = new Dictionary<string, int>();
            for (int i = 0; i < currentSelection.Count; i++)
            {
                var entry = currentSelection[i];
                if (entry.type == CellOccupantType.Belt)
                {
                    beltCount++;
                    continue;
                }

                machineCount++;
                string name = ResolveMachineName(entry);
                names.TryGetValue(name, out int count);
                names[name] = count + 1;
            }

            var nameParts = new List<string>();
            foreach (var pair in names) nameParts.Add(pair.Key + " " + pair.Value);
            string detail = nameParts.Count > 0 ? string.Join(" · ", nameParts) : "기계 없음";
            SetSummary($"선택 · 기계 {machineCount}개 · 벨트 {beltCount}개\n{detail} · 이동 시 전력 시설 제외",
                SeoUITheme.Current.Danger);
        }

        private string ResolveMachineName((CellOccupantType type, int index) entry)
        {
            if (entry.type == CellOccupantType.Miner)
            {
                if (entry.index < 0 || entry.index >= driver.World.Miners.Count || driver.World.Miners[entry.index] == null)
                    return "채굴기";
                return MachineInfoPresenter.GetMachineDisplayName(driver.World,
                    driver.World.Miners[entry.index].MachineId);
            }

            if (entry.index < 0 || entry.index >= driver.World.Processors.Count || driver.World.Processors[entry.index] == null)
                return "기계";
            return MachineInfoPresenter.GetMachineDisplayName(driver.World,
                driver.World.Processors[entry.index].MachineId);
        }

        private void ApplyHighlights()
        {
            for (int i = 0; i < currentSelection.Count; i++)
            {
                var entry = currentSelection[i];
                string objectName = entry.type == CellOccupantType.Belt
                    ? $"Belt_{entry.index}"
                    : entry.type == CellOccupantType.Miner
                        ? $"{MachineInstanceKind.Miner}_{entry.index}"
                        : $"{MachineInstanceKind.Processor}_{entry.index}";
                var root = GameObject.Find(objectName);
                if (root == null) continue;

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null || originalBlocks.ContainsKey(renderer)) continue;

                    var original = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original);
                    originalBlocks[renderer] = original;

                    var highlight = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(highlight);
                    highlight.SetColor("_BaseColor", new Color(1f, 0.08f, 0.06f, 1f));
                    highlight.SetColor("_Color", new Color(1f, 0.08f, 0.06f, 1f));
                    renderer.SetPropertyBlock(highlight);
                }
            }
        }

        private void RestoreHighlights()
        {
            foreach (var pair in originalBlocks)
            {
                if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value);
            }
            originalBlocks.Clear();
            selectionSignature = int.MinValue;
        }

        private void UpdateOutline()
        {
            var box = SelectionBoxField?.GetValue(demolishTool) as GameObject;
            if (box == null || !box.activeInHierarchy)
            {
                SetOutlineVisible(false);
                return;
            }

            EnsureOutline();
            selectionOutline.gameObject.SetActive(true);
            Vector3 center = box.transform.position;
            Vector3 size = box.transform.lossyScale;
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            float y = center.y + 0.08f;
            selectionOutline.SetPosition(0, new Vector3(center.x - halfX, y, center.z - halfZ));
            selectionOutline.SetPosition(1, new Vector3(center.x + halfX, y, center.z - halfZ));
            selectionOutline.SetPosition(2, new Vector3(center.x + halfX, y, center.z + halfZ));
            selectionOutline.SetPosition(3, new Vector3(center.x - halfX, y, center.z + halfZ));
        }

        private void EnsureOutline()
        {
            if (selectionOutline != null) return;
            var root = new GameObject("SeoDemolitionOutline", typeof(LineRenderer));
            selectionOutline = root.GetComponent<LineRenderer>();
            selectionOutline.loop = true;
            selectionOutline.useWorldSpace = true;
            selectionOutline.positionCount = 4;
            selectionOutline.startWidth = 0.08f;
            selectionOutline.endWidth = 0.08f;
            selectionOutline.startColor = SeoUITheme.Current.Danger;
            selectionOutline.endColor = SeoUITheme.Current.Danger;
            selectionOutline.shadowCastingMode = ShadowCastingMode.Off;
            selectionOutline.receiveShadows = false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null) selectionOutline.material = new Material(shader) { color = SeoUITheme.Current.Danger };
            root.SetActive(false);
        }

        private void SetOutlineVisible(bool visible)
        {
            if (selectionOutline != null) selectionOutline.gameObject.SetActive(visible);
        }

        private void HandleDemolishModeEnded()
        {
            int removedMachines = 0;
            int removedBelts = 0;
            for (int i = 0; i < lastSelection.Count; i++)
            {
                var entry = lastSelection[i];
                if (!WasRemoved(entry)) continue;
                if (entry.type == CellOccupantType.Belt) removedBelts++;
                else removedMachines++;
            }

            if (removedMachines > 0 || removedBelts > 0)
            {
                toastMessage = $"철거 완료 · 기계 {removedMachines}개 · 벨트 {removedBelts}개";
                toastUntil = Time.unscaledTime + 2f;
            }

            currentSelection.Clear();
            lastSelection.Clear();
            selectionSignature = int.MinValue;
        }

        private bool WasRemoved((CellOccupantType type, int index) entry)
        {
            switch (entry.type)
            {
                case CellOccupantType.Belt:
                    return entry.index < 0 || entry.index >= driver.World.Segments.Count
                        || driver.World.Segments[entry.index] == null;
                case CellOccupantType.Miner:
                    return entry.index < 0 || entry.index >= driver.World.Miners.Count
                        || driver.World.Miners[entry.index] == null;
                case CellOccupantType.Processor:
                    return entry.index < 0 || entry.index >= driver.World.Processors.Count
                        || driver.World.Processors[entry.index] == null;
                default:
                    return false;
            }
        }

        private void SetSummary(string message, Color color)
        {
            if (summaryText == null) return;
            summaryText.text = message;
            summaryText.color = color;
        }

        private void OnDestroy()
        {
            RestoreHighlights();
            if (selectionOutline != null)
            {
                if (selectionOutline.material != null) Destroy(selectionOutline.material);
                Destroy(selectionOutline.gameObject);
            }
        }
    }
}
