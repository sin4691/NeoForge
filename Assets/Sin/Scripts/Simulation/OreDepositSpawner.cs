using System.Collections.Generic;
using Factory.Building;
using Factory.Data;
using Factory.Rendering;
using UnityEngine;

namespace Factory.Simulation
{
    // 씬에 놓은 OreDepositMarker들을 읽어서 광물 노드를 배치한다. 절차적 지형 생성은 스코프 밖이라
    // 노드는 전부 마커로 직접 지정한다 — 채굴기는 이 노드 위에 지어야만 그 노드가 정한
    // 자원/속도/산출량을 물려받는다(MachineGhostTool.Confirm() 참고).
    //
    // 노드가 수백~수천 칸이 될 수 있어서 데이터와 비주얼을 분리했다: 노드 "데이터"(어느 칸에 어떤
    // 자원이 있나)는 시작할 때 WorldGrid에 전부 한 번에 등록하지만(딕셔너리 항목이라 싸다), 노드
    // "비주얼"(GameObject + Addressables 모델)은 카메라 근처 청크에서만 프레임당 일정 개수씩
    // 만들고, 멀어지면 SetActive(false)로 꺼둔다. 안 그러면 시작하는 순간 GameObject/모델 로드가
    // 한꺼번에 몰려 버벅이고, 화면 밖 노드도 계속 렌더링/메모리 비용을 낸다(사용자 지적: 렉).
    public class OreDepositSpawner : MonoBehaviour
    {
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private GameObject oreDepositVisualPrefab;

        // 화면 경계 바깥으로 미리 만들어두는 여유분(칸 단위) — 카메라가 움직일 때 팝인 방지.
        [SerializeField] private float viewPaddingCells = 8f;
        [SerializeField] private float updateInterval = 0.1f;
        // 한 프레임에 새로 만들 비주얼 최대 개수 — 넓은 지역이 한꺼번에 화면에 들어와도 프레임이
        // 뚝 끊기지 않고 몇 프레임에 걸쳐 나눠서 채워진다.
        [SerializeField] private int visualsPerFrame = 20;

        private readonly Dictionary<Vector2Int, List<Vector2Int>> cellsByChunk = new Dictionary<Vector2Int, List<Vector2Int>>();
        private readonly Dictionary<Vector2Int, GameObject> visualByCell = new Dictionary<Vector2Int, GameObject>();
        private readonly HashSet<Vector2Int> visibleChunkCoords = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> scratchChunkCoords = new HashSet<Vector2Int>();
        private readonly Queue<Vector2Int> pendingVisualCells = new Queue<Vector2Int>();
        private float timer;

        private void Start()
        {
            if (driver == null || driver.World == null) return;
            if (targetCamera == null) targetCamera = Camera.main;

            var db = driver.World.Database;
            var grid = driver.World.Grid;

            // 씬에 직접 놓은 마커(OreDepositMarker) — 코드 수정/재컴파일 없이 Scene 뷰에서 배치.
            var markers = FindObjectsByType<OreDepositMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                // 마커 하나가 여러 칸을 덮을 수 있다 — 붓으로 칠한 칸이 있으면 그 모양, 없으면 size
                // 사각형(기본 1x1이면 한 칸). OreDepositMarker.Cells 참고.
                foreach (var cell in markers[i].Cells)
                {
                    TryRegister(cell, markers[i].depositId, db, grid);
                }
            }

            // 카메라를 못 찾으면(테스트 씬 등) 근처만 만드는 판단을 할 수 없으니 예전처럼 전부 만든다.
            if (targetCamera == null)
            {
                foreach (var pair in cellsByChunk)
                {
                    for (int i = 0; i < pair.Value.Count; i++) pendingVisualCells.Enqueue(pair.Value[i]);
                }
            }
        }

        private void Update()
        {
            if (driver == null || driver.World == null) return;

            if (targetCamera != null)
            {
                timer += Time.deltaTime;
                if (timer >= updateInterval)
                {
                    timer = 0f;
                    if (CameraViewBounds.TryCompute(targetCamera, viewPaddingCells, out var bounds)) UpdateVisibleChunks(bounds);
                }
            }

            BuildPendingVisuals();
        }

        // 노드 데이터만 등록한다(비주얼은 UpdateVisibleChunks가 필요할 때 만든다).
        private void TryRegister(Vector2Int cell, string depositId, GameDatabase db, WorldGrid grid)
        {
            if (string.IsNullOrEmpty(depositId)) return;
            if (!db.TryGetOreDepositId(depositId, out int depositRuntimeId)) return;
            if (grid.TryGetOreDeposit(cell, out _)) return; // 이미 있으면(겹치는 마커, 재실행 등) 건너뜀

            grid.RegisterOreDeposit(cell, depositRuntimeId);

            Vector2Int chunkCoord = WorldGrid.WorldCellToChunkCoord(cell);
            if (!cellsByChunk.TryGetValue(chunkCoord, out var cells))
            {
                cells = new List<Vector2Int>();
                cellsByChunk[chunkCoord] = cells;
            }
            cells.Add(cell);
        }

        private void UpdateVisibleChunks(RectInt cellBounds)
        {
            Vector2Int minChunk = WorldGrid.WorldCellToChunkCoord(new Vector2Int(cellBounds.xMin, cellBounds.yMin));
            Vector2Int maxChunk = WorldGrid.WorldCellToChunkCoord(new Vector2Int(cellBounds.xMax, cellBounds.yMax));

            scratchChunkCoords.Clear();
            for (int x = minChunk.x; x <= maxChunk.x; x++)
            {
                for (int y = minChunk.y; y <= maxChunk.y; y++)
                {
                    var coord = new Vector2Int(x, y);
                    if (cellsByChunk.ContainsKey(coord)) scratchChunkCoords.Add(coord);
                }
            }

            // 새로 근처가 된 청크 -> 이미 만들어둔 비주얼은 다시 켜고, 없는 건 생성 대기열에 넣는다.
            foreach (var coord in scratchChunkCoords)
            {
                if (visibleChunkCoords.Contains(coord)) continue;
                var cells = cellsByChunk[coord];
                for (int i = 0; i < cells.Count; i++)
                {
                    if (visualByCell.TryGetValue(cells[i], out var existing)) existing.SetActive(true);
                    else pendingVisualCells.Enqueue(cells[i]);
                }
            }

            // 멀어진 청크 -> 껐다(파괴하지 않음 — 다시 돌아오면 모델 재로드 없이 바로 켠다).
            foreach (var coord in visibleChunkCoords)
            {
                if (scratchChunkCoords.Contains(coord)) continue;
                var cells = cellsByChunk[coord];
                for (int i = 0; i < cells.Count; i++)
                {
                    if (visualByCell.TryGetValue(cells[i], out var existing)) existing.SetActive(false);
                }
            }

            visibleChunkCoords.Clear();
            foreach (var coord in scratchChunkCoords) visibleChunkCoords.Add(coord);
        }

        private void BuildPendingVisuals()
        {
            var db = driver.World.Database;
            var grid = driver.World.Grid;

            int built = 0;
            while (pendingVisualCells.Count > 0 && built < visualsPerFrame)
            {
                Vector2Int cell = pendingVisualCells.Dequeue();
                if (visualByCell.ContainsKey(cell)) continue; // 대기열에 중복으로 들어간 경우

                // 대기 중에 카메라가 멀어져서 이 칸의 청크가 더 이상 근처가 아니면 지금은 안 만든다 —
                // 다시 가까워지면 UpdateVisibleChunks가 그때 다시 대기열에 넣는다.
                if (targetCamera != null && !visibleChunkCoords.Contains(WorldGrid.WorldCellToChunkCoord(cell))) continue;

                if (!grid.TryGetOreDeposit(cell, out int depositRuntimeId)) continue;
                visualByCell[cell] = SpawnVisual(cell, db.OreDeposits[depositRuntimeId]);
                built++;
            }
        }

        private GameObject SpawnVisual(Vector2Int cell, OreDepositRuntime deposit)
        {
            Vector3 worldPos = GridUtility.CellToWorldCenter(cell, 0f);
            var res = driver.World.Database.Resources[deposit.ResourceId];

            var root = new GameObject($"OreDeposit_{deposit.Key}");
            root.transform.position = worldPos;

            // 폴백: 자원 색 얇은 판. Addressables 모델(SM_*_Node)이 로드되면 숨겨진다.
            // oreDepositVisualPrefab(SceneBootstrapper가 넣어주던 판 프리팹)은 이제 안 쓴다 — Addressables 우선.
            GameObject placeholder = oreDepositVisualPrefab != null
                ? Instantiate(oreDepositVisualPrefab, worldPos, Quaternion.identity, root.transform)
                : BuildVisuals.CreateBox(worldPos, new Vector3(0.95f, 0.08f, 0.95f), res.Color, root.transform, withCollider: false);
            placeholder.name = "Placeholder";
            BuildVisuals.Colorize(placeholder, res.Color);

            // 광맥 모델은 resourceId 로 조회한다(컨벤션) — AddressablesSetup 의 Deposits 표.
            root.AddComponent<AddressableModelMount>().Mount("Prefab_Deposit_" + res.Key, placeholder);
            return root;
        }
    }
}
