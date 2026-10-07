using System;
using System.Collections.Generic;
using Factory.Building;
using Factory.Buildings;
using Factory.Data;
using Factory.Simulation;
using Seo.Building;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Choi.SaveLoad
{
    public enum PowerBuildMode
    {
        None,
        Generator,
        Cable,
        TransmissionTower,
        Remove,
    }

    /// <summary>전력 배치 도구. 전선 제스처는 공용 라우터로 받아 카메라 조작을 함께 지원합니다.</summary>
    public sealed class PowerBuildController : MonoBehaviour, IBuildTool
    {
        public static Func<PowerBuildMode, Vector2Int, bool> PlacementPermission { get; set; }

        private const float CableHeight = 1.65f;
        private const float CableWidth = 0.035f;
        private const int TowerRangeRadius = 7;
        private static readonly Vector2Int NodePointerOffset = new Vector2Int(0, 2);

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly List<GameObject> visuals = new List<GameObject>();
        private readonly List<GameObject> placementPreview = new List<GameObject>();
        private readonly List<GameObject> towerSelectionPreview = new List<GameObject>();
        private readonly List<GameObject> coreRangePreview = new List<GameObject>();
        private readonly List<Vector2Int> cableDragPath = new List<Vector2Int>();

        private PowerGridSystem powerGrid;
        private PowerStructureVisualCatalog visualCatalog;
        private SimulationDriver driver;
        private BuildInputRouter buildRouter;
        private MachineGhostTool machineTool;
        private Camera targetCamera;
        private bool isCableDragging;
        private Vector2Int cableStartCell;
        private PowerNodeRuntime cableStartNode;
        private bool lastBlackoutState;
        private bool blackoutStateInitialized;
        private int selectedTowerId = -1;
        private bool isCoreRangeSelected;
        private GameObject nodePlacementGhost;
        private bool hasPendingNodePlacement;
        private bool isDraggingNodePlacement;
        private Vector2Int pendingNodeCell;
        private bool lastGhostValid = true;
        private Vector2Int generatorFacing = Vector2Int.right;

        public PowerBuildMode Mode { get; private set; }
        public string LastMessage { get; private set; } = "전력 도구 대기";
        public bool HasPendingNodePlacement => hasPendingNodePlacement
            && (Mode == PowerBuildMode.Generator || Mode == PowerBuildMode.TransmissionTower);

        private void Awake()
        {
            powerGrid = GetComponent<PowerGridSystem>() ?? FindAnyObjectByType<PowerGridSystem>();
            visualCatalog = Resources.Load<PowerStructureVisualCatalog>("PowerStructureVisualCatalog");
            driver = FindAnyObjectByType<SimulationDriver>();
            buildRouter = FindAnyObjectByType<BuildInputRouter>();
            machineTool = FindAnyObjectByType<MachineGhostTool>();
            targetCamera = Camera.main;
            BeltDragTool.ExternalCellBlocked = IsPowerStructureCell;
            DemolishTool.ExternalConfirm = RemovePowerInArea;
            DemolishTool.ExternalHasTargets = HasPowerInArea;
            GroupMoveTool.CaptureExternalPowerSelection = CapturePowerMoveSelection;
            GroupMoveTool.ValidateExternalPowerMove = ValidatePowerMove;
            GroupMoveTool.CommitExternalPowerMove = CommitPowerMove;
        }

        private void Start()
        {
            RebuildVisuals();
        }

        private void Update()
        {
            if (Choi.Research.ResearchController.Instance != null && Choi.Research.ResearchController.Instance.IsOpen)
                return;

            // 전선 입력은 라우터에서만 처리한다. 두 손가락 카메라 조작을 연결로 오인하지 않는다.
            if (Mode == PowerBuildMode.Cable) return;

            // 터치/드래그 이벤트가 없어도(가만히 놓여있는 동안도) 자원 상태가 바뀔 수 있으니
            // 매 프레임 가볍게 재확인한다 — 안 그러면 자원이 다시 채워져도 손을 한 번 더 대야
            // 초록으로 바뀐다(HandleNodePlacementDrag는 press/드래그 이벤트가 있을 때만 돈다).
            if (hasPendingNodePlacement) RefreshPlacementGhostValidity();

            if (!TryGetPointerState(out Vector2 screenPosition, out int? pointerId,
                    out bool pressed, out bool held, out bool released))
            {
                if (Mode != PowerBuildMode.Generator && Mode != PowerBuildMode.TransmissionTower)
                    ClearPlacementPreview();
                return;
            }

            if (IsOverUi(pointerId) && !isCableDragging)
            {
                if (Mode != PowerBuildMode.None && Mode != PowerBuildMode.Generator
                    && Mode != PowerBuildMode.TransmissionTower) ClearPlacementPreview();
                return;
            }

            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            if (!GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), groundPlane,
                    out Vector2Int cell))
            {
                ClearPlacementPreview();
                return;
            }

            if (Mode == PowerBuildMode.None)
            {
                if (pressed) SelectPowerRangeAt(cell);
                return;
            }

            if (Mode == PowerBuildMode.Generator)
            {
                HandleNodePlacementDrag(cell, pressed, held, released);
                return;
            }

            if (Mode == PowerBuildMode.TransmissionTower)
            {
                HandleNodePlacementDrag(cell, pressed, held, released);
                return;
            }
            if (Mode == PowerBuildMode.Remove)
            {
                ClearPlacementPreview();
                return; // 영역 선택과 확정은 공용 DemolishTool이 담당한다.
            }
            ClearPlacementPreview();

            if (pressed) ApplyAt(cell);
        }

        private void LateUpdate()
        {
            if (powerGrid == null) return;
            if (blackoutStateInitialized && lastBlackoutState == powerGrid.IsBlackout) return;
            blackoutStateInitialized = true;
            lastBlackoutState = powerGrid.IsBlackout;
            RebuildVisuals();
        }

        private void OnDisable()
        {
            CancelPlacementPreview();
            ClearTowerSelectionPreview();
            ClearCoreRangePreview();
            RestoreBuildRouter();
        }

        private void OnDestroy()
        {
            if (BeltDragTool.ExternalCellBlocked == IsPowerStructureCell)
                BeltDragTool.ExternalCellBlocked = null;
            if (DemolishTool.ExternalConfirm == RemovePowerInArea) DemolishTool.ExternalConfirm = null;
            if (DemolishTool.ExternalHasTargets == HasPowerInArea) DemolishTool.ExternalHasTargets = null;
            if (GroupMoveTool.CaptureExternalPowerSelection == CapturePowerMoveSelection)
                GroupMoveTool.CaptureExternalPowerSelection = null;
            if (GroupMoveTool.ValidateExternalPowerMove == ValidatePowerMove)
                GroupMoveTool.ValidateExternalPowerMove = null;
            if (GroupMoveTool.CommitExternalPowerMove == CommitPowerMove)
                GroupMoveTool.CommitExternalPowerMove = null;
        }

        private List<ExternalPowerMoveEntry> CapturePowerMoveSelection(RectInt bounds)
        {
            var result = new List<ExternalPowerMoveEntry>();
            if (powerGrid == null) return result;
            for (int i = 0; i < powerGrid.Nodes.Count; i++)
            {
                PowerNodeRuntime node = powerGrid.Nodes[i];
                if (!bounds.Contains(node.Cell) || (node.Kind != PowerNodeKind.Generator
                    && node.Kind != PowerNodeKind.TransmissionTower)) continue;
                var visual = GameObject.Find($"PowerNode_{node.Id}_{node.Kind}");
                result.Add(new ExternalPowerMoveEntry
                {
                    NodeId = node.Id,
                    Cell = node.Cell,
                    Facing = node.Facing,
                    Visual = visual != null ? visual.transform : null,
                });
            }
            return result;
        }

        private bool ValidatePowerMove(IReadOnlyList<ExternalPowerMoveEntry> entries,
            Func<Vector2Int, Vector2Int> transformCell, Func<Vector2Int, bool> movingFactoryCell,
            out string reason)
        {
            reason = null;
            if (powerGrid == null || driver == null || driver.World == null)
            { reason = "전력 시설 상태를 확인할 수 없습니다"; return false; }
            var selectedIds = new HashSet<int>();
            var targets = new HashSet<Vector2Int>();
            for (int i = 0; i < entries.Count; i++) selectedIds.Add(entries[i].NodeId);
            for (int i = 0; i < entries.Count; i++)
            {
                ExternalPowerMoveEntry entry = entries[i];
                if (!powerGrid.TryGetNode(entry.Cell, out PowerNodeRuntime node) || node.Id != entry.NodeId)
                { reason = "선택한 전력 시설의 위치가 바뀌었습니다. 다시 선택하세요"; return false; }
                Vector2Int target = transformCell(entry.Cell);
                if (!targets.Add(target)) { reason = "전력 시설끼리 겹칩니다"; return false; }
                if (powerGrid.TryGetNode(target, out PowerNodeRuntime other) && !selectedIds.Contains(other.Id))
                { reason = "다른 전력 시설과 겹칩니다"; return false; }
                if (driver.World.Grid.TryGetOccupant(target, out var gridOccupant) && !movingFactoryCell(target))
                {
                    bool ownGeneratorPort = false;
                    foreach (int selectedId in selectedIds)
                    {
                        PowerNodeRuntime selectedNode = powerGrid.FindNodeById(selectedId);
                        if (selectedNode != null && selectedNode.FuelProcessorIndex == gridOccupant.InstanceIndex
                            && gridOccupant.Type == CellOccupantType.Processor)
                        { ownGeneratorPort = true; break; }
                    }
                    if (!ownGeneratorPort)
                    { reason = "다른 기계·벨트와 겹칩니다"; return false; }
                }
                PowerBuildMode mode = node.Kind == PowerNodeKind.Generator
                    ? PowerBuildMode.Generator : PowerBuildMode.TransmissionTower;
                if (PlacementPermission != null && !PlacementPermission(mode, target))
                { reason = "현재 설치 제한으로 이동할 수 없는 위치입니다"; return false; }
            }
            return true;
        }

        private bool CommitPowerMove(IReadOnlyList<ExternalPowerMoveEntry> entries,
            Func<Vector2Int, Vector2Int> transformCell)
        {
            var targets = new Dictionary<int, Vector2Int>();
            var facings = new Dictionary<int, Vector2Int>();
            for (int i = 0; i < entries.Count; i++)
            {
                ExternalPowerMoveEntry entry = entries[i];
                Vector2Int facingEnd = transformCell(entry.Cell + entry.Facing);
                Vector2Int target = transformCell(entry.Cell);
                targets[entry.NodeId] = target;
                facings[entry.NodeId] = facingEnd - target;
            }
            if (!powerGrid.MoveNodes(targets, facings, out int refund)) return false;
            RefundCopperWire(refund);
            RebuildVisuals();
            powerGrid.EvaluatePower();
            LastMessage = refund > 0
                ? $"전력 시설 이동 · 끊어진 전선 구리선 {refund}개 반환"
                : "전력 시설 이동";
            return true;
        }

        public void ToggleMode(PowerBuildMode mode)
        {
            SetMode(Mode == mode ? PowerBuildMode.None : mode);
        }

        public void SetMode(PowerBuildMode mode)
        {
            CancelPlacementPreview();
            ClearPowerRangeSelection();
            Mode = mode;
            if (mode == PowerBuildMode.None)
            {
                RestoreBuildRouter();
                LastMessage = "전력 배치 종료";
                return;
            }

            machineTool?.CancelPlacement();
            if (buildRouter != null)
            {
                buildRouter.SetMode(BuildInputRouter.Mode.None);
                buildRouter.enabled = true;
                if (mode == PowerBuildMode.Cable) buildRouter.SetExternalTool(this);
            }
            if (mode == PowerBuildMode.Generator || mode == PowerBuildMode.TransmissionTower)
                BeginNodePlacement(mode);
            LastMessage = mode == PowerBuildMode.Generator ? "좌클릭 드래그로 발전기를 옮기고 확정하세요 · 우클릭 드래그는 화면 이동"
                : mode == PowerBuildMode.Cable ? "드래그로 전선 연결 · 우클릭 드래그/두 손가락으로 화면 이동"
                : mode == PowerBuildMode.TransmissionTower ? "좌클릭 드래그로 송전탑을 옮기고 확정하세요 · 우클릭 드래그는 화면 이동"
                : "철거할 발전기/전선/송전탑을 선택하세요";
        }

        public void RebuildVisuals()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i] != null) Destroy(visuals[i]);
            }
            visuals.Clear();

            if (powerGrid == null) return;
            for (int i = 0; i < powerGrid.Nodes.Count; i++)
            {
                PowerNodeRuntime node = powerGrid.Nodes[i];
                GameObject visual;
                if (node.Kind == PowerNodeKind.Generator)
                {
                    visual = CreateNodeVisual(node.Kind, node.Cell, node.Facing);
                }
                else if (node.Kind == PowerNodeKind.Cable || node.Kind == PowerNodeKind.Junction)
                {
                    // 전선은 칸마다 오브젝트를 보이지 않고, 인접 칸 사이의 얇은 선만 렌더링한다.
                    visual = null;
                }
                else
                {
                    visual = CreateNodeVisual(node.Kind, node.Cell);
                }

                if (visual != null)
                {
                    visual.name = $"PowerNode_{node.Id}_{node.Kind}";
                    if (node.Kind == PowerNodeKind.Generator)
                    {
                        var effect = visual.AddComponent<GeneratorElectricArcEffect>();
                        effect.Initialize(powerGrid, node.Id);
                        // 기존 콜라이더는 Destroy 대기 상태일 수 있으므로 선택용은 항상 새로 만든다.
                        var selectionCollider = visual.AddComponent<BoxCollider>();
                        selectionCollider.center = new Vector3(0f, 0.55f, 0f);
                        selectionCollider.size = new Vector3(0.9f, 1.1f, 0.9f);
                        var view = visual.AddComponent<MachineView>();
                        view.Initialize(MachineInstanceKind.Processor, node.FuelProcessorIndex, driver);
                    }
                    visuals.Add(visual);
                }

            }

            for (int i = 0; i < powerGrid.Connections.Count; i++)
            {
                PowerConnectionRuntime connection = powerGrid.Connections[i];
                CreateConnectionWire(connection.Path);
            }
        }

        private void ApplyAt(Vector2Int cell)
        {
            if (powerGrid == null) return;

            bool changed = false;
            switch (Mode)
            {
                case PowerBuildMode.Generator:
                    if (driver != null && driver.World != null && driver.World.Grid.IsOccupied(cell))
                    {
                        LastMessage = "발전기는 빈 칸에만 놓을 수 있습니다";
                        return;
                    }
                    if (!TryPayBuildCost("Generator"))
                    {
                        LastMessage = "자원이 부족합니다";
                        return;
                    }
                    changed = powerGrid.TryAddNode(PowerNodeKind.Generator, cell, generatorFacing);
                    if (!changed) RefundBuildCost("Generator"); // 놓을 자리 자체가 없었으면 뗀 자원 그대로 돌려준다.
                    LastMessage = changed ? $"발전기 설치: {cell}" : "이미 전력 시설이 있는 칸입니다";
                    break;
                case PowerBuildMode.Cable:
                    LastMessage = "발전기 또는 송전탑에서 드래그해 연결하세요";
                    break;
                case PowerBuildMode.TransmissionTower:
                    if (driver != null && driver.World != null && driver.World.Grid.IsOccupied(cell))
                    {
                        LastMessage = "송전탑은 빈 칸에만 놓을 수 있습니다";
                        return;
                    }
                    if (!TryPayBuildCost("TransmissionTower"))
                    {
                        LastMessage = "자원이 부족합니다";
                        return;
                    }
                    changed = powerGrid.TryAddNode(PowerNodeKind.TransmissionTower, cell);
                    if (!changed) RefundBuildCost("TransmissionTower");
                    LastMessage = changed ? $"송전탑 설치: {cell} · 공급 범위 15x15" : "이미 전력 시설이 있는 칸입니다";
                    break;
                case PowerBuildMode.Remove:
                    if (powerGrid.TryGetNode(cell, out PowerNodeRuntime removedNode))
                    {
                        changed = powerGrid.RemoveNode(cell, out int nodeRefund);
                        if (changed)
                        {
                            RefundBuildCost(NodeMachineKey(removedNode.Kind));
                            RefundCopperWire(nodeRefund);
                        }
                    }
                    else
                    {
                        changed = powerGrid.RemoveConnectionAt(cell, out int cableRefund);
                        if (changed) RefundCopperWire(cableRefund);
                    }
                    LastMessage = changed ? $"전력 시설 철거: {cell}" : "철거할 전력 시설이 없습니다";
                    break;
            }

            if (changed)
            {
                PowerBuildMode completedMode = Mode;
                string completedMessage = LastMessage;
                RebuildVisuals();
                powerGrid.EvaluatePower();
                // 발전기/송전탑 모두 취소할 때까지 같은 배치 모드를 유지한다.
                if (completedMode == PowerBuildMode.Generator || completedMode == PowerBuildMode.TransmissionTower)
                {
                    BeginNodePlacement(completedMode);
                    LastMessage = completedMessage + " · 계속 배치하거나 취소로 종료하세요";
                }
            }
        }

        public bool ConfirmPendingPlacement()
        {
            if (!HasPendingNodePlacement) return false;
            if (!IsNodePlacementValid(pendingNodeCell))
            {
                LastMessage = CanAffordBuildCost(ModeMachineKey(Mode))
                    ? "전력 시설은 비어 있는 칸에만 놓을 수 있습니다"
                    : "자원이 부족합니다";
                return false;
            }
            ApplyAt(pendingNodeCell);
            return true;
        }

        // 기존 회전 버튼이 발전기 배치 중에도 같은 동작을 호출할 수 있도록 공개한다.
        public bool RotateGeneratorFacing()
        {
            if (Mode != PowerBuildMode.Generator) return false;
            generatorFacing = new Vector2Int(-generatorFacing.y, generatorFacing.x);
            RebuildNodePlacementGhost(Mode);
            LastMessage = "발전기 연료 입력 방향 변경";
            return true;
        }

        private void BeginNodePlacement(PowerBuildMode mode)
        {
            if (targetCamera == null) targetCamera = Camera.main;
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (targetCamera != null
                && GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenCenter), groundPlane, out var centerCell))
                pendingNodeCell = centerCell + NodePointerOffset;
            else pendingNodeCell = Vector2Int.zero;
            hasPendingNodePlacement = true;
            isDraggingNodePlacement = false;
            RebuildNodePlacementGhost(mode);
        }

        private void HandleNodePlacementDrag(Vector2Int cell, bool pressed, bool held, bool released)
        {
            if (!hasPendingNodePlacement) BeginNodePlacement(Mode);

            // 고스트를 정확히 집을 필요 없이, 월드 아무 곳에서 좌클릭 드래그를 시작하면
            // 일반 기계 배치처럼 고스트가 포인터를 따라간다. 우클릭은 이 코드가 소비하지
            // 않으므로 BuildInputRouter의 카메라 팬으로 그대로 전달된다.
            if (pressed)
            {
                isDraggingNodePlacement = true;
                pendingNodeCell = cell + NodePointerOffset;
                RebuildNodePlacementGhost(Mode);
                if (buildRouter != null)
                {
                    buildRouter.SetMode(BuildInputRouter.Mode.None);
                    buildRouter.enabled = false;
                }
            }
            Vector2Int offsetCell = cell + NodePointerOffset;
            if (isDraggingNodePlacement && (held || released) && offsetCell != pendingNodeCell)
            {
                pendingNodeCell = offsetCell;
                RebuildNodePlacementGhost(Mode);
            }
            if (released && isDraggingNodePlacement)
            {
                isDraggingNodePlacement = false;
                RestoreBuildRouter();
            }
        }

        private void RebuildNodePlacementGhost(PowerBuildMode mode)
        {
            if (nodePlacementGhost != null) Destroy(nodePlacementGhost);
            bool valid = IsNodePlacementValid(pendingNodeCell);
            // 일반 기계 고스트(validColor/invalidColor)의 높은 알파값과 비슷하게 맞춰 밝은
            // 바닥에서도 형태와 유효 여부가 분명히 보이게 한다.
            Color color = valid ? new Color(0.3f, 0.9f, 0.4f, 0.85f) : new Color(0.9f, 0.2f, 0.2f, 0.85f);
            if (mode == PowerBuildMode.Generator)
            {
                nodePlacementGhost = CreateNodeVisual(PowerNodeKind.Generator, pendingNodeCell, generatorFacing);
                AddGeneratorGhostInputIndicator(nodePlacementGhost);
            }
            else
            {
                nodePlacementGhost = CreateNodeVisual(PowerNodeKind.TransmissionTower, pendingNodeCell);
                ClearPlacementPreview();
                CreateTowerRange(pendingNodeCell, placementPreview, new Color(0.35f, 0.9f, 1f));
            }
            nodePlacementGhost.name = "PowerNodePlacementGhost";
            RemoveColliders(nodePlacementGhost);
            TintRenderers(nodePlacementGhost, color);
            lastGhostValid = valid;
        }

        // RebuildNodePlacementGhost처럼 오브젝트를 통째로 다시 만들진 않고 색만 매 프레임
        // 다시 칠한다 — 미리보기 오브젝트 하나뿐이라 부담 없고, "바뀔 때만" 최적화를 없애서
        // 상태 비교 로직 자체의 버그 가능성을 원천적으로 없앤다.
        private void RefreshPlacementGhostValidity()
        {
            if (nodePlacementGhost == null) return;
            bool valid = IsNodePlacementValid(pendingNodeCell);
            lastGhostValid = valid;
            Color color = valid ? new Color(0.3f, 0.9f, 0.4f, 0.85f) : new Color(0.9f, 0.2f, 0.2f, 0.85f);
            TintRenderers(nodePlacementGhost, color);
        }

        private GameObject CreateNodeVisual(PowerNodeKind kind, Vector2Int cell, Vector2Int facing = default)
        {
            GameObject prefab = visualCatalog != null ? visualCatalog.GetPrefab(kind) : null;
            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab);
                instance.transform.position = GridUtility.CellToWorldCenter(cell, 0f);
                if (kind == PowerNodeKind.Generator)
                {
                    if (facing == Vector2Int.zero) facing = Vector2Int.right;
                    instance.transform.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);
                    RemoveOutputIndicators(instance);
                }
                RemoveColliders(instance);
                return instance;
            }
            bool generator = kind == PowerNodeKind.Generator;
            GameObject fallback = GameObject.CreatePrimitive(generator ? PrimitiveType.Cylinder : PrimitiveType.Capsule);
            fallback.transform.position = GridUtility.CellToWorldCenter(cell, generator ? 0.5f : 1.15f);
            fallback.transform.localScale = generator ? new Vector3(0.72f, 0.5f, 0.72f) : new Vector3(0.42f, 1.05f, 0.42f);
            BuildVisuals.Colorize(fallback, generator ? new Color(1f, 0.62f, 0.08f) : new Color(0.72f, 0.25f, 1f));
            RemoveColliders(fallback);
            return fallback;
        }

        private static void RemoveOutputIndicators(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root.transform && child.name.ToLowerInvariant().Contains("output")) Destroy(child.gameObject);
            }
        }

        private static void AddGeneratorGhostInputIndicator(GameObject root)
        {
            var badge = new GameObject("GeneratorGhostInput", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
            badge.transform.SetParent(root.transform, false);
            badge.transform.localPosition = Vector3.back * 0.72f + Vector3.up * 0.16f;
            badge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // 월드 캔버스의 픽셀 크기를 그대로 월드 단위로 쓰면 화면을 덮는다.
            badge.transform.localScale = Vector3.one * 0.0065f;
            var canvas = badge.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 40;
            badge.GetComponent<RectTransform>().sizeDelta = new Vector2(42f, 42f);
            badge.GetComponent<UnityEngine.UI.Image>().color = new Color(0.03f, 0.55f, 0.75f, 0.92f);

            var label = new GameObject("Arrow", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            label.transform.SetParent(badge.transform, false);
            var text = label.GetComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // 고스트의 기본 회전에서 입력 포트가 위쪽에 있으므로, 흐름은 기기 쪽(아래)을 향한다.
            // 캔버스가 고스트 회전을 상속하므로 이 기준 화살표도 회전할 때 함께 올바르게 돈다.
            text.text = "▲"; text.fontSize = 34; text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            label.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            label.GetComponent<RectTransform>().anchorMax = Vector2.one;
            label.GetComponent<RectTransform>().offsetMin = label.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        }

        private static void RemoveColliders(GameObject root)
        {
            if (root == null) return;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Destroy(colliders[i]);
        }

        private static void TintRenderers(GameObject root, Color color)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var block = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", color); block.SetColor("_Color", color);
                renderers[i].SetPropertyBlock(block);
            }
        }

        private bool IsNodePlacementValid(Vector2Int cell)
        {
            if (PlacementPermission != null && !PlacementPermission(Mode, cell)) return false;
            if (powerGrid != null && powerGrid.IsCoreCell(cell)) return false;
            if (powerGrid != null && powerGrid.TryGetNode(cell, out _)) return false;
            // 자원이 모자라면 칸 자체는 비어 있어도 배치 불가로 취급한다 — 일반 기계 고스트가
            // 자원 부족 시 빨갛게 뜨는 것과 같은 시각 피드백을 여기도 맞춰준다.
            if (!CanAffordBuildCost(ModeMachineKey(Mode))) return false;
            if (driver == null || driver.World == null) return true;
            // 광물 노드는 WorldGrid의 건물 점유와 별도 레이어지만, 색깔 있는 광맥 위에
            // 전력 시설이 겹치면 채굴기를 영원히 놓지 못하므로 배치 불가 칸으로 취급한다.
            if (driver.World.Grid.TryGetOreDeposit(cell, out _)) return false;
            return !driver.World.Grid.IsOccupied(cell);
        }

        private string ModeMachineKey(PowerBuildMode mode)
        {
            if (mode == PowerBuildMode.Generator) return "Generator";
            if (mode == PowerBuildMode.TransmissionTower) return "TransmissionTower";
            return null;
        }

        // 발전기/송전탑도 Bae님 스키마에 buildCostItems가 정의된 "기계"라 일반 기계 건설
        // 비용(MachineGhostTool.HasBuildResources/DeductBuildResources)과 같은 원칙을 그대로
        // 따른다 — 다만 이 둘은 MachineGhostTool이 아니라 이 컨트롤러가 직접 배치를 처리하므로
        // (PowerGridSystem.TryAddNode), 그쪽 코드를 못 타고 비용 체크가 통째로 빠져 있었다.
        private static string NodeMachineKey(PowerNodeKind kind)
        {
            if (kind == PowerNodeKind.Generator) return "Generator";
            if (kind == PowerNodeKind.TransmissionTower) return "TransmissionTower";
            return null;
        }

        // 실제 확인/차감/환불 계산은 BuildCostUtility(MachineGhostTool/SimulationWorld와 공유)에
        // 맡긴다 — 예전엔 여기 따로 구현하다가 고스트 색깔 판정과 실제 설치 판정 기준이
        // 어긋나는 버그가 났었다. 이 파일은 machineKey(문자열) → cost/core로 바꿔주는 것만 한다.
        private bool CanAffordBuildCost(string machineKey)
        {
            if (!TryGetBuildCost(machineKey, out ResourceAmount[] cost, out ProcessorInstance core)) return true;
            return BuildCostUtility.CanAfford(core, cost);
        }

        private bool TryPayBuildCost(string machineKey)
        {
            if (!TryGetBuildCost(machineKey, out ResourceAmount[] cost, out ProcessorInstance core)) return true;
            return BuildCostUtility.TryPay(core, cost);
        }

        // 전선은 Bae님 스키마의 "기계"가 아니라 연결 하나당 고정 비용이라(칸 수와 무관 —
        // 드래그 한 번에 완성되는 단일 조작이라 벨트처럼 칸당으로 셀 이유가 없다) 여기 따로
        // 상수로 둔다. 재료는 이미 생산되는 구리선(CopperWire) 재사용 — 전선용 아이템을
        // 새로 만들 필요 없이 바로 적용 가능해서.
        private const int CableCopperWireCost = 1;

        private bool TryPayCableCost(out int paidAmount)
        {
            paidAmount = 0;
            if (driver == null || driver.World == null) return true; // 코어가 없으면 확인할 창고가 없으니 무료 취급.
            if (!driver.World.Database.TryGetResourceId("CopperWire", out int resourceId)) return true;
            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count) return true;
            ProcessorInstance core = driver.World.Processors[coreIndex];
            if (core == null) return true;

            var cost = new[] { new ResourceAmount(resourceId, CableCopperWireCost) };
            if (!BuildCostUtility.TryPay(core, cost)) return false;
            paidAmount = CableCopperWireCost;
            return true;
        }

        private void RefundCopperWire(int amount)
        {
            if (amount <= 0) return;
            if (driver == null || driver.World == null) return;
            if (!driver.World.Database.TryGetResourceId("CopperWire", out int resourceId)) return;
            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count) return;
            ProcessorInstance core = driver.World.Processors[coreIndex];
            BuildCostUtility.Refund(core, new[] { new ResourceAmount(resourceId, amount) });
        }

        private void RefundBuildCost(string machineKey)
        {
            if (!TryGetBuildCost(machineKey, out ResourceAmount[] cost, out ProcessorInstance core)) return;
            BuildCostUtility.Refund(core, cost);
        }

        // cost/core를 못 구하면(코어가 아직 없다 등) true를 돌려주지 않는 쪽(TryPayBuildCost 등)이
        // "일단 통과시킨다"고 알아서 판단하도록, 여기서는 그냥 못 구했다는 사실만 알려준다.
        private bool TryGetBuildCost(string machineKey, out ResourceAmount[] cost, out ProcessorInstance core)
        {
            cost = null;
            core = null;
            if (string.IsNullOrEmpty(machineKey)) return false; // 전선/분기점처럼 대응하는 기계가 없는 종류.
            if (driver == null || driver.World == null) return false;
            var db = driver.World.Database;
            if (!db.TryGetMachineId(machineKey, out int machineId)) return false;
            cost = db.Machines[machineId].BuildCost;
            if (cost == null || cost.Length == 0) return false;
            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count) return false;
            core = driver.World.Processors[coreIndex];
            return core != null;
        }

        private bool IsPowerStructureCell(Vector2Int cell)
        {
            if (powerGrid == null || !powerGrid.TryGetNode(cell, out var node)) return false;
            return node.Kind == PowerNodeKind.Generator || node.Kind == PowerNodeKind.TransmissionTower;
        }

        private bool HasPowerInArea(RectInt bounds)
        {
            if (powerGrid == null) return false;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (powerGrid.TryGetNode(cell, out _)) return true;
                    for (int i = 0; i < powerGrid.Connections.Count; i++)
                    {
                        var path = powerGrid.Connections[i].Path;
                        if (ConnectionCrossesCell(path, cell)) return true;
                    }
                }
            }
            return false;
        }

        private bool RemovePowerInArea(RectInt bounds)
        {
            if (powerGrid == null) return false;
            bool changed = false;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (powerGrid.TryGetNode(cell, out PowerNodeRuntime node))
                    {
                        if (powerGrid.RemoveNode(cell, out int nodeRefund))
                        {
                            changed = true;
                            RefundBuildCost(NodeMachineKey(node.Kind));
                            RefundCopperWire(nodeRefund);
                        }
                    }
                    while (powerGrid.RemoveConnectionAt(cell, out int cableRefund))
                    {
                        changed = true;
                        RefundCopperWire(cableRefund);
                    }
                }
            }
            if (changed)
            {
                RebuildVisuals();
                powerGrid.EvaluatePower();
                LastMessage = "선택 영역의 전력 시설을 철거했습니다";
            }
            return changed;
        }

        private static bool ConnectionCrossesCell(IReadOnlyList<Vector2Int> path, Vector2Int cell)
        {
            if (path == null) return false;
            for (int i = 0; i < path.Count; i++)
                if (path[i] == cell) return true;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2Int from = path[i];
                Vector2Int to = path[i + 1];
                long dx = to.x - from.x;
                long dy = to.y - from.y;
                long px = cell.x - from.x;
                long py = cell.y - from.y;
                if (dx * py == dy * px
                    && cell.x >= Mathf.Min(from.x, to.x) && cell.x <= Mathf.Max(from.x, to.x)
                    && cell.y >= Mathf.Min(from.y, to.y) && cell.y <= Mathf.Max(from.y, to.y))
                    return true;
            }
            return false;
        }

        private void CreateWire(Vector2Int fromCell, Vector2Int toCell)
        {
            // 탑뷰에서 건물 위를 가로지르는 가는 전선처럼 보이게 한다.
            Vector3 from = GridUtility.CellToWorldCenter(fromCell, CableHeight);
            Vector3 to = GridUtility.CellToWorldCenter(toCell, CableHeight);
            GameObject wire = BuildVisuals.CreateStrip(from, to, CableWidth,
                new Color(0.03f, 0.58f, 0.82f), null, false);
            wire.name = "PowerWire";
            visuals.Add(wire);
        }

        private void CreateConnectionWire(List<Vector2Int> path)
        {
            if (path == null) return;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i - 1] != path[i]) CreateWire(path[i - 1], path[i]);
            }
        }

        void IBuildTool.OnPressBegin(Vector2 position) => HandleCablePointer(position, true, true, false);
        void IBuildTool.OnDrag(Vector2 position) => HandleCablePointer(position, false, true, false);
        void IBuildTool.OnReleased(Vector2 position) => HandleCablePointer(position, false, false, true);
        void IBuildTool.OnCancelled() => CancelPlacementPreview();

        private void HandleCablePointer(Vector2 position, bool pressed, bool held, bool released)
        {
            if (!isActiveAndEnabled || Mode != PowerBuildMode.Cable) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null || !GridUtility.TryRaycastToCell(
                    targetCamera.ScreenPointToRay(position), groundPlane, out Vector2Int cell))
            {
                if (released) CancelPlacementPreview();
                return;
            }
            HandleCablePlacement(cell, pressed, held, released);
        }

        private void HandleCablePlacement(Vector2Int cell, bool pressed, bool held, bool released)
        {
            if (pressed && !isCableDragging)
            {
                if (powerGrid == null || !powerGrid.TryResolveConnectionPoint(cell, out cableStartNode))
                {
                    LastMessage = "발전기, 송전탑 또는 기존 전선에서 드래그를 시작하세요";
                    ClearPlacementPreview();
                    return;
                }
                isCableDragging = true;
                cableStartCell = cell;
                cableDragPath.Clear();
                cableDragPath.Add(cell);
            }

            if (isCableDragging && (held || released))
            {
                AppendDragCell(cell);
                ShowCablePreview(cell);
            }
            if (!isCableDragging || !released) return;

            if (cableStartCell == cell || !powerGrid.TryResolveConnectionPoint(cell, out PowerNodeRuntime endNode))
            {
                isCableDragging = false;
                ClearPlacementPreview();
                cableDragPath.Clear();
                cableStartNode = null;
                LastMessage = "다른 발전기, 송전탑 또는 기존 전선에서 드래그를 끝내세요";
                return;
            }
            if (!powerGrid.CanConnect(cableStartNode, endNode))
            {
                isCableDragging = false;
                ClearPlacementPreview();
                cableDragPath.Clear();
                cableStartNode = null;
                LastMessage = "이 송전탑에는 이미 발전기 하나가 직접 연결되어 있습니다";
                return;
            }

            if (!TryPayCableCost(out int paidCopperWire))
            {
                isCableDragging = false;
                ClearPlacementPreview();
                cableDragPath.Clear();
                cableStartNode = null;
                LastMessage = "자원이 부족합니다";
                return;
            }

            List<Vector2Int> finalPath = IsGeneratorTowerPair(cableStartNode, endNode)
                ? new List<Vector2Int> { cableStartNode.Cell, endNode.Cell }
                : new List<Vector2Int>(cableDragPath);
            bool installed = powerGrid.TryAddConnection(cableStartNode, endNode, finalPath, paidCopperWire);
            if (!installed) RefundCopperWire(paidCopperWire); // 연결이 이미 있었던 것 등으로 무산되면 그대로 돌려준다.

            isCableDragging = false;
            ClearPlacementPreview();
            cableDragPath.Clear();
            cableStartNode = null;
            if (installed)
            {
                RebuildVisuals();
                powerGrid.EvaluatePower();
                // 발전기/송전탑 연속 배치와 동일하게 전선 도구도 취소할 때까지 유지한다.
                // 연결 제스처 상태는 위에서 이미 초기화했으므로 바로 다음 전선을 드래그할 수 있다.
                LastMessage = $"전력 시설 연결: {cableStartCell} → {cell} · 계속 연결하거나 취소로 종료하세요";
            }
            else
            {
                LastMessage = "두 전력 시설은 이미 직접 연결되어 있습니다";
            }
        }

        private void AppendDragCell(Vector2Int cell)
        {
            if (cableDragPath.Count == 0 || cableDragPath[cableDragPath.Count - 1] != cell)
                cableDragPath.Add(cell);
        }

        private void ShowCablePreview(Vector2Int currentCell)
        {
            ClearPlacementPreview();
            if (cableStartNode == null || cableStartCell == currentCell) return;

            // 드래그 경로의 흔들림과 관계없이 시작점-현재 지점을 한 직선으로 미리 보여준다.
            Vector3 from = GridUtility.CellToWorldCenter(cableStartCell, CableHeight + 0.02f);
            Vector3 to = GridUtility.CellToWorldCenter(currentCell, CableHeight + 0.02f);
            GameObject wire = BuildVisuals.CreateStrip(from, to, CableWidth * 1.7f,
                new Color(0.2f, 0.95f, 1f), null, false);
            wire.name = "PowerWirePreview";
            placementPreview.Add(wire);
        }

        private static bool IsGeneratorTowerPair(PowerNodeRuntime first, PowerNodeRuntime second)
        {
            if (first == null || second == null) return false;
            return (first.Kind == PowerNodeKind.Generator && second.Kind == PowerNodeKind.TransmissionTower)
                || (first.Kind == PowerNodeKind.TransmissionTower && second.Kind == PowerNodeKind.Generator);
        }

        private void ShowTowerRange(Vector2Int centerCell)
        {
            ClearPlacementPreview();
            CreateTowerRange(centerCell, placementPreview, new Color(0.35f, 0.9f, 1f));
        }

        private void SelectPowerRangeAt(Vector2Int cell)
        {
            if (powerGrid != null && powerGrid.IsCoreCell(cell))
            {
                if (isCoreRangeSelected)
                {
                    ClearCoreRangePreview();
                    LastMessage = "코어 전력 범위 표시 종료";
                    return;
                }

                ClearTowerSelectionPreview();
                ShowCoreRangePreview();
                LastMessage = $"코어 선택 · 공급 범위 {PowerGridSystem.CoreRangeSize}x{PowerGridSystem.CoreRangeSize}";
                return;
            }

            if (powerGrid == null
                || !powerGrid.TryGetNode(cell, out PowerNodeRuntime node)
                || node.Kind != PowerNodeKind.TransmissionTower)
            {
                ClearPowerRangeSelection();
                return;
            }

            if (selectedTowerId == node.Id && towerSelectionPreview.Count > 0)
            {
                ClearTowerSelectionPreview();
                LastMessage = "송전탑 범위 표시 종료";
                return;
            }

            ClearTowerSelectionPreview();
            ClearCoreRangePreview();
            selectedTowerId = node.Id;
            CreateTowerRange(cell, towerSelectionPreview, new Color(0.72f, 0.35f, 1f));
            LastMessage = $"송전탑 선택: {cell} · 공급 범위 15x15";
        }

        private void CreateTowerRange(Vector2Int centerCell, List<GameObject> target, Color color)
        {
            float minX = centerCell.x - TowerRangeRadius;
            float maxX = centerCell.x + TowerRangeRadius + 1f;
            float minZ = centerCell.y - TowerRangeRadius;
            float maxZ = centerCell.y + TowerRangeRadius + 1f;
            const float height = 0.09f;
            const float width = 0.075f;

            CreatePreviewStrip(new Vector3(minX, height, minZ), new Vector3(maxX, height, minZ), width, color, target);
            CreatePreviewStrip(new Vector3(maxX, height, minZ), new Vector3(maxX, height, maxZ), width, color, target);
            CreatePreviewStrip(new Vector3(maxX, height, maxZ), new Vector3(minX, height, maxZ), width, color, target);
            CreatePreviewStrip(new Vector3(minX, height, maxZ), new Vector3(minX, height, minZ), width, color, target);
        }

        private void ShowCoreRangePreview()
        {
            if (powerGrid == null || !powerGrid.TryGetCorePowerCenter(out Vector2 center))
            {
                ClearCoreRangePreview();
                return;
            }

            ClearCoreRangePreview();
            isCoreRangeSelected = true;

            float halfRange = PowerGridSystem.CoreRangeSize * 0.5f;
            // 2x2 코어 중심을 기준으로 12x12 영역의 네 변을 그리드 경계에 맞춘다.
            float minX = Mathf.Floor(center.x - halfRange);
            float maxX = minX + PowerGridSystem.CoreRangeSize;
            float minZ = Mathf.Floor(center.y - halfRange);
            float maxZ = minZ + PowerGridSystem.CoreRangeSize;
            const float height = 0.085f;
            const float width = 0.09f;
            Color color = new Color(0.18f, 1f, 0.52f, 0.9f);

            CreatePreviewStrip(new Vector3(minX, height, minZ), new Vector3(maxX, height, minZ), width, color, coreRangePreview);
            CreatePreviewStrip(new Vector3(maxX, height, minZ), new Vector3(maxX, height, maxZ), width, color, coreRangePreview);
            CreatePreviewStrip(new Vector3(maxX, height, maxZ), new Vector3(minX, height, maxZ), width, color, coreRangePreview);
            CreatePreviewStrip(new Vector3(minX, height, maxZ), new Vector3(minX, height, minZ), width, color, coreRangePreview);
            for (int i = 0; i < coreRangePreview.Count; i++) coreRangePreview[i].name = "CorePowerRange";
        }

        private void CreatePreviewStrip(Vector3 from, Vector3 to, float width, Color color, List<GameObject> target)
        {
            GameObject strip = BuildVisuals.CreateStrip(from, to, width, color, null, false);
            strip.name = "PowerPlacementPreview";
            target.Add(strip);
        }

        private void CancelPlacementPreview()
        {
            isCableDragging = false;
            isDraggingNodePlacement = false;
            hasPendingNodePlacement = false;
            cableStartNode = null;
            cableDragPath.Clear();
            if (nodePlacementGhost != null) Destroy(nodePlacementGhost);
            nodePlacementGhost = null;
            ClearPlacementPreview();
        }

        private void ClearPlacementPreview()
        {
            for (int i = 0; i < placementPreview.Count; i++)
            {
                if (placementPreview[i] != null) Destroy(placementPreview[i]);
            }
            placementPreview.Clear();
        }

        private void ClearTowerSelectionPreview()
        {
            for (int i = 0; i < towerSelectionPreview.Count; i++)
            {
                if (towerSelectionPreview[i] != null) Destroy(towerSelectionPreview[i]);
            }
            towerSelectionPreview.Clear();
            selectedTowerId = -1;
        }

        private void ClearCoreRangePreview()
        {
            for (int i = 0; i < coreRangePreview.Count; i++)
            {
                if (coreRangePreview[i] != null) Destroy(coreRangePreview[i]);
            }
            coreRangePreview.Clear();
            isCoreRangeSelected = false;
        }

        private void ClearPowerRangeSelection()
        {
            ClearTowerSelectionPreview();
            ClearCoreRangePreview();
        }

        private void RestoreBuildRouter()
        {
            if (buildRouter == null) return;
            if (ReferenceEquals(buildRouter.ExternalTool, this))
                buildRouter.SetMode(BuildInputRouter.Mode.None);
            buildRouter.enabled = true;
        }

        private static bool TryGetPointerState(out Vector2 position, out int? pointerId,
            out bool pressed, out bool held, out bool released)
        {
            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    bool touchPressed = touches[i].press.wasPressedThisFrame;
                    bool touchHeld = touches[i].press.isPressed;
                    bool touchReleased = touches[i].press.wasReleasedThisFrame;
                    if (!touchPressed && !touchHeld && !touchReleased) continue;
                    position = touches[i].position.ReadValue();
                    pointerId = touches[i].touchId.ReadValue();
                    pressed = touchPressed;
                    held = touchHeld;
                    released = touchReleased;
                    return true;
                }
            }

            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
                pointerId = null;
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                held = Mouse.current.leftButton.isPressed;
                released = Mouse.current.leftButton.wasReleasedThisFrame;
                return true;
            }

            position = default;
            pointerId = null;
            pressed = false;
            held = false;
            released = false;
            return false;
        }

        private static bool IsOverUi(int? pointerId)
        {
            if (EventSystem.current == null) return false;
            return pointerId.HasValue
                ? EventSystem.current.IsPointerOverGameObject(pointerId.Value)
                : EventSystem.current.IsPointerOverGameObject();
        }
    }
}
