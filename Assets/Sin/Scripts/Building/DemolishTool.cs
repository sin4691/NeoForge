using System;
using System.Collections.Generic;
using Factory.Buildings;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 드래그로 사각 영역을 훑어서 그 안의 기계/벨트를 한 번에 여러 개 선택하고, "철거 확정"
    // 버튼을 눌러야 실제로 지운다(터치 릴리즈만으로 바로 지우면 큰 영역을 잘못 훑었을 때
    // 되돌릴 방법이 없어서 오조작 피해가 큼 — 기계 배치가 확정 버튼을 거치는 것과 같은 이유).
    // 코어는 선택 대상에서 항상 제외한다 — 지워지면 게임이 통째로 망가진다.
    public class DemolishTool : MonoBehaviour, IBuildTool
    {
        public static Func<RectInt, bool> ExternalConfirm { get; set; }
        public static Func<RectInt, bool> ExternalHasTargets { get; set; }
        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private BuildInputRouter router;
        [SerializeField] private Color previewColor = new Color(0.9f, 0.15f, 0.15f, 0.4f);

        private static readonly Vector2Int[] FourDirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly HashSet<(CellOccupantType type, int index)> selected = new HashSet<(CellOccupantType, int)>();
        private BeltDragTool beltTool;

        private GameObject selectionBox;
        private Vector2Int startCell;
        private bool dragging;
        private RectInt selectionBounds;
        private bool hasSelectionArea;

        public bool HasSelection => selected.Count > 0
            || (hasSelectionArea && (ExternalHasTargets?.Invoke(selectionBounds) ?? false));
        public Camera TargetCamera => targetCamera;
        public SimulationDriver Driver => driver;
        public IEnumerable<(CellOccupantType type, int index)> Selected => selected;
        public bool HasSelectionArea => hasSelectionArea;
        public RectInt SelectionBounds => selectionBounds;

        public void Initialize(Camera targetCamera, SimulationDriver driver)
        {
            this.targetCamera = targetCamera;
            this.driver = driver;
        }

        private void OnEnable()
        {
            if (router != null) router.ModeChanged += HandleModeChanged;
        }

        private void OnDisable()
        {
            if (router != null) router.ModeChanged -= HandleModeChanged;
        }

        // 다른 도구로 전환되면(확정 안 하고 팔레트에서 다른 버튼 누름 등) 남아있던 빨간
        // 선택 박스가 화면에 계속 떠 있으면 안 되니 같이 지운다.
        private void HandleModeChanged(BuildInputRouter.Mode mode)
        {
            if (mode != BuildInputRouter.Mode.Demolish) ClearSelection();
        }

        public void OnPressBegin(Vector2 screenPosition)
        {
            if (!TryScreenToCell(screenPosition, out startCell)) return;
            dragging = true;
            RebuildSelection(startCell);
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (!dragging) return;
            if (!TryScreenToCell(screenPosition, out var cell)) return;
            RebuildSelection(cell);
        }

        public void OnReleased(Vector2 screenPosition)
        {
            // 릴리즈로 바로 지우지 않는다 — 선택 상태만 유지하고 "철거 확정" 버튼을 기다린다.
            dragging = false;
        }

        public void OnCancelled()
        {
            dragging = false;
            ClearSelection();
        }

        private bool TryScreenToCell(Vector2 screenPosition, out Vector2Int cell)
        {
            cell = default;
            if (targetCamera == null) return false;
            return GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), groundPlane, out cell);
        }

        private void RebuildSelection(Vector2Int currentCell)
        {
            selected.Clear();
            if (driver == null || driver.World == null) return;

            int minX = Mathf.Min(startCell.x, currentCell.x);
            int maxX = Mathf.Max(startCell.x, currentCell.x);
            int minY = Mathf.Min(startCell.y, currentCell.y);
            int maxY = Mathf.Max(startCell.y, currentCell.y);
            selectionBounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            hasSelectionArea = true;

            var grid = driver.World.Grid;
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (grid.TryGetOccupant(cell, out var occupant) && !IsCore(occupant))
                    {
                        selected.Add((occupant.Type, occupant.InstanceIndex));
                    }
                    // 크로스 벨트(2번째 벨트 레이어)도 같이 훑는다 — 안 그러면 교차해서
                    // 지나가는 벨트는 영영 선택/철거가 안 된다.
                    if (grid.TryGetCrossingOccupant(cell, out var crossing))
                    {
                        selected.Add((crossing.Type, crossing.InstanceIndex));
                    }
                }
            }

            UpdateSelectionBoxVisual(minX, maxX, minY, maxY);
        }

        private bool IsCore(CellOccupant occupant)
        {
            return occupant.Type == CellOccupantType.Processor && occupant.InstanceIndex == driver.World.CoreProcessorIndex;
        }

        private void UpdateSelectionBoxVisual(int minX, int maxX, int minY, int maxY)
        {
            if (selectionBox == null)
            {
                selectionBox = BuildVisuals.CreateBox(Vector3.zero, Vector3.one, previewColor, transform, withCollider: false);
            }

            float sizeX = (maxX - minX + 1) * GridUtility.CellSize;
            float sizeZ = (maxY - minY + 1) * GridUtility.CellSize;
            Vector3 center = new Vector3(
                (minX + maxX + 1) * 0.5f * GridUtility.CellSize,
                0.05f,
                (minY + maxY + 1) * 0.5f * GridUtility.CellSize);

            selectionBox.transform.position = center;
            selectionBox.transform.localScale = new Vector3(sizeX, 0.1f, sizeZ);
            selectionBox.SetActive(true);
        }

        private void ClearSelection()
        {
            selected.Clear();
            hasSelectionArea = false;
            if (selectionBox != null) selectionBox.SetActive(false);
        }

        // "철거 확정" 버튼에서 호출.
        public bool Confirm()
        {
            if (driver == null || driver.World == null) return false;
            bool externalChanged = hasSelectionArea && (ExternalConfirm?.Invoke(selectionBounds) ?? false);
            if (selected.Count == 0 && !externalChanged) return false;

            var world = driver.World;
            var grid = world.Grid;
            if (beltTool == null) beltTool = FindAnyObjectByType<BeltDragTool>();

            foreach (var (type, index) in selected)
            {
                switch (type)
                {
                    case CellOccupantType.Miner:
                        DestroyVisual($"{MachineInstanceKind.Miner}_{index}");
                        Factory.Rendering.MachineVisualRegistry.Unregister(MachineInstanceKind.Miner, index);
                        grid.UnregisterOccupant(type, index);
                        world.RemoveMiner(index);
                        break;
                    case CellOccupantType.Processor:
                        DestroyVisual($"{MachineInstanceKind.Processor}_{index}");
                        Factory.Rendering.MachineVisualRegistry.Unregister(MachineInstanceKind.Processor, index);
                        grid.UnregisterOccupant(type, index);
                        world.RemoveProcessor(index);
                        break;
                    case CellOccupantType.Belt:
                        grid.TryGetCellOf(CellOccupantType.Belt, index, out var removedCell);
                        // 통째로 Destroy하지 않고 풀에 반납한다 — 벨트를 놓았다 지웠다 반복해도
                        // GameObject가 계속 새로 생겼다 사라지는 대신, 다음 벨트 배치 때
                        // 그대로 재사용된다(BeltDragTool.GetOrCreateBeltRoot 참고, 사용자 지적:
                        // "벨트 많이 설치하면 렉").
                        if (beltTool != null) beltTool.ReturnBeltVisual(index);
                        else DestroyVisual($"Belt_{index}");
                        grid.UnregisterOccupant(type, index);
                        world.RemoveSegment(index);
                        RefreshNeighborBeltVisuals(grid, removedCell);
                        break;
                }
            }

            ClearSelection();
            return true;
        }

        // 벨트를 지우고 나면 그 옆에 남은 세그먼트는 스트립이 예전(지워지기 전) 이웃 방향
        // 그대로 남아있다 — RerenderSegmentStrip은 지어지거나 재배선될 때만 불렸지, 옆이
        // 잘려나갈 때는 아무도 다시 그려주지 않았다(코너가 이상하게 보이던 원인 중 하나).
        private void RefreshNeighborBeltVisuals(WorldGrid grid, Vector2Int removedCell)
        {
            if (beltTool == null) beltTool = FindAnyObjectByType<BeltDragTool>();
            if (beltTool == null) return;

            for (int d = 0; d < FourDirs.Length; d++)
            {
                if (!grid.TryGetOccupant(removedCell + FourDirs[d], out var occupant)) continue;
                if (occupant.Type != CellOccupantType.Belt) continue;
                beltTool.RerenderSegmentStrip(occupant.InstanceIndex);
            }
        }

        // 벨트/기계 스폰 시 지어진 이름 규칙(BeltDragTool.SpawnCommittedVisual, MachineGhostTool.
        // SpawnMachineVisual 참고)을 그대로 재사용해서 찾는다 — 인덱스→비주얼 역방향 레지스트리를
        // 따로 안 두려고 일부러 이렇게 함(철거는 드문 조작이라 이름 검색 비용도 무방).
        private static void DestroyVisual(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Destroy(go);
        }
    }
}
