using System.Collections.Generic;
using Factory.Building;
using Factory.Buildings;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Rendering
{
    // 화면에 안 보이는 벨트/기계 비주얼을 SetActive(false)로 꺼서 렌더링/스크립트 실행 비용을
    // 줄인다. 시뮬레이션(SimulationWorld.Tick)은 절대 안 건드린다 — 자원 흐름 계산은 화면
    // 밖에서도 계속 돌아야 게임이 말이 되므로(WorldGrid.cs 상단 주석: "시뮬레이션 계산 자체는
    // 전체 청크에 대해 계속 돈다... 이번 패스는 가시 청크 판정까지만 구현하고, 풀링 최적화는
    // 이후 과제로 남긴다" — 그 남겨둔 과제가 바로 이 스크립트다).
    //
    // Bae님의 FloorViewportCuller(Assets/Bae/Scripts/Optimization)와 같은 원리(카메라 화면
    // 네 모서리를 바닥에 레이캐스트해서 보이는 범위를 구함)를 그대로 따르되, 대상은 바닥
    // 타일이 아니라 벨트/기계 비주얼이고, 청크 판정은 새로 만들지 않고 WorldGrid의 기존
    // GetVisibleChunks를 그대로 쓴다.
    //
    // 벨트는 BeltDragTool.TryGetBeltVisual(segmentId)로, 기계/채굴기는
    // MachineVisualRegistry.TryGet(kind, index)로 실제 GameObject를 찾는다 — 둘 다 비활성
    // 오브젝트도 찾을 수 있는 딕셔너리 기반이라(GameObject.Find는 비활성을 못 찾음) 껐다가
    // 다시 켤 수 있다. 코어는 애초에 이 레지스트리에 안 들어가므로(FactorySaveBridge/
    // CoreSpawner가 안 넣음) 절대 안 꺼진다 — 늘 화면에 있어야 하는 특수 건물이라 의도된 예외.
    public class FactoryViewportCuller : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private BeltDragTool beltTool;
        // 화면 경계 바로 밖에서 미리 켜두는 여유분(칸 단위) — 카메라가 빠르게 패닝할 때 딱
        // 경계에서 팝인하는 게 눈에 띄지 않도록.
        [SerializeField] private float viewPaddingCells = 8f;
        [SerializeField] private float updateInterval = 0.1f;

        private readonly Dictionary<Vector2Int, Chunk> visibleChunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Dictionary<Vector2Int, Chunk> scratchChunks = new Dictionary<Vector2Int, Chunk>();
        // 아직 한 번도 첫 스윕을 안 돌았는지. 이게 필요한 이유: 이미 지어져 있던 벨트/기계는
        // 전부 기본적으로 켜진 채로 시작하는데, 매 업데이트의 "보이다가 안 보이게 된 것만
        // 끈다" 방식은 "처음부터 화면 밖이라 한 번도 visibleChunks에 들어온 적 없는 것"은
        // 절대 못 끈다(꺼야 할 목록 자체에 없으니) — 카메라가 그쪽을 지나가기 전까진 영원히
        // 안 꺼져서 "켰는데 아무 효과가 없다"처럼 보인다(사용자 보고). 그래서 켜지는 첫 순간
        // 딱 한 번, 지금 존재하는 모든 청크를 훑어서 화면 밖인 것들을 강제로 꺼준다.
        private bool didInitialSweep;
        private float timer;

        // 에디터 SerializedObject 없이(런타임/테스트에서) 직접 배선할 때 쓴다.
        public void Initialize(Camera targetCamera, SimulationDriver driver, BeltDragTool beltTool)
        {
            this.targetCamera = targetCamera;
            this.driver = driver;
            this.beltTool = beltTool;
        }

        // 성능 비교 전용(TestFactoryBuilder의 컬링 벤치마크). 컴포넌트를 그냥 끄기만 하면 이미 꺼둔
        // 화면 밖 비주얼이 꺼진 채로 남아 "컬링 없음" 상태가 안 되므로, 끌 때는 전부 다시 켜고,
        // 켤 때는 첫 스윕부터 다시 하게 상태를 초기화한다.
        public void SetCullingEnabled(bool on)
        {
            if (!on && driver != null && driver.World != null)
            {
                foreach (var chunk in driver.World.Grid.AllChunks) SetChunkVisualsActive(chunk, true);
            }
            visibleChunks.Clear();
            didInitialSweep = false;
            timer = updateInterval; // 켜자마자 다음 Update에서 바로 스윕
            enabled = on;
        }

        // Inspector에 안 물려있으면 씬에서 알아서 찾는다 — 빈 GameObject에 이 컴포넌트만
        // 얹어도 바로 동작하게(별도 배선 작업 없이 켜볼 수 있게).
        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (beltTool == null) beltTool = FindAnyObjectByType<BeltDragTool>();
        }

        private void Update()
        {
            if (targetCamera == null || driver == null || driver.World == null) return;

            timer += Time.deltaTime;
            if (timer < updateInterval) return;
            timer = 0f;

            if (CameraViewBounds.TryCompute(targetCamera, viewPaddingCells, out var bounds)) UpdateVisibility(bounds);
        }

        private void UpdateVisibility(RectInt cellBounds)
        {
            scratchChunks.Clear();
            foreach (var chunk in driver.World.Grid.GetVisibleChunks(cellBounds))
            {
                scratchChunks[chunk.Coord] = chunk;
            }

            if (!didInitialSweep)
            {
                foreach (var chunk in driver.World.Grid.AllChunks)
                {
                    if (!scratchChunks.ContainsKey(chunk.Coord)) SetChunkVisualsActive(chunk, false);
                }
                didInitialSweep = true;
            }

            // 새로 보이게 된 청크 -> 켠다.
            foreach (var kv in scratchChunks)
            {
                if (!visibleChunks.ContainsKey(kv.Key)) SetChunkVisualsActive(kv.Value, true);
            }
            // 더는 안 보이는 청크 -> 끈다.
            foreach (var kv in visibleChunks)
            {
                if (!scratchChunks.ContainsKey(kv.Key)) SetChunkVisualsActive(kv.Value, false);
            }

            visibleChunks.Clear();
            foreach (var kv in scratchChunks) visibleChunks[kv.Key] = kv.Value;
        }

        // Chunk.BuildingIds는 채굴기/기계 인스턴스 인덱스를 타입 구분 없이 섞어 담는다(같은
        // 숫자가 채굴기 쪽에도 기계 쪽에도 동시에 존재할 수 있음) — 그래서 이 목록을 그대로
        // 믿고 "채굴기랑 기계 둘 다 시도해보기"를 하면 전혀 다른 칸에 있는 엉뚱한 기계를 같이
        // 켰다 껐다 하는 사고가 난다. 대신 청크가 덮는 16x16 칸을 직접 훑어서 WorldGrid에
        // 실제로 뭐가 있는지(타입+인덱스 정확히) 물어본다 — 청크 경계를 넘나들 때만(카메라가
        // 그만큼 움직였을 때만) 도는 코드라 매 틱 비용이 아니라 비싸도 무방하다.
        private void SetChunkVisualsActive(Chunk chunk, bool active)
        {
            foreach (int segmentId in chunk.SegmentIds)
            {
                if (beltTool != null && beltTool.TryGetBeltVisual(segmentId, out var beltGo)) beltGo.SetActive(active);
            }

            var grid = driver.World.Grid;
            int baseX = chunk.Coord.x * WorldGrid.ChunkSize;
            int baseY = chunk.Coord.y * WorldGrid.ChunkSize;
            for (int dx = 0; dx < WorldGrid.ChunkSize; dx++)
            {
                for (int dy = 0; dy < WorldGrid.ChunkSize; dy++)
                {
                    var cell = new Vector2Int(baseX + dx, baseY + dy);
                    if (!grid.TryGetOccupant(cell, out var occupant) || occupant.Type == CellOccupantType.Belt) continue;

                    var kind = occupant.Type == CellOccupantType.Miner ? MachineInstanceKind.Miner : MachineInstanceKind.Processor;
                    if (MachineVisualRegistry.TryGet(kind, occupant.InstanceIndex, out var buildingGo)) buildingGo.SetActive(active);
                }
            }
        }
    }
}
