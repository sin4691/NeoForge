using System.Collections.Generic;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 크로스 벨트(교차로) — 분류기/합류기처럼 한 칸짜리 노드지만, 서로 독립된 두 축(가로 한
    // 줄 + 세로 한 줄)을 동시에 가진다. 왼쪽에서 들어온 건 오른쪽으로, 아래에서 들어온 건
    // 위로 — 두 흐름은 절대 안 섞인다(사용자 요청). "크로스" 팔레트 버튼(다른 기계들과 완전히
    // 같은 고스트+회전+확정 조작, MachineGhostTool.CrossBeltMachineId)을 확정하는 순간
    // PlaceCrossableTile이 두 축 모두를 독립된 BeltSegment로 만든다 — 하나는 WorldGrid
    // 1번 레이어(주축, 고스트에서 고른 Facing 방향), 하나는 2번 레이어(수직축)에 등록해서
    // 한 칸에 공존시킨다. 시뮬레이션(BeltSystem/RoutingSystem)은 WorldGrid를 안 보고 세그먼트
    // 리스트만 보고 돌므로 이 레이어 구분은 순전히 "한 칸에 두 점유자가 있게" 하기 위한
    // 배치용 트릭일 뿐, 아이템이 섞일 걱정은 전혀 없다.
    //
    // 이후 다른 벨트/기계가 이 칸에 어느 방향에서 접근하느냐(주축과 나란한지 수직인지)에 따라
    // 실제로 말하는 세그먼트가 갈린다 — TryGetOccupantForConnection이 그 보정을 전담한다
    // (BeltDragTool.Ports.cs 참고). Commit()/HasValidEndpointPreview()/TryFindAdjacentOccupant/
    // BeltConnectionFeedback이 모두 grid.TryGetOccupant 대신 이걸 거쳐야 두 축이 서로 안 헷갈린다.
    public partial class BeltDragTool
    {
        // "크로스" 고스트 확정 시 호출. facing(=고스트가 고른 방향)이 주축, 그걸 90도 돌린
        // 방향이 수직축이다 — 두 축 다 "입력=-축, 출력=+축" 규칙으로 독립적으로 자동 연결한다
        // (Splitter/Merger가 배치 시 4방향을 자동 연결하는 것과 같은 원칙). 이미 있으면 잇고,
        // 없으면(허공) 그 방향은 막힌 채로 둔다 — 나중에 벨트로 이어붙이면 된다.
        public void PlaceCrossableTile(Vector2Int cell, Vector2Int facing)
        {
            Vector2Int perpendicular = new Vector2Int(-facing.y, facing.x);

            // 건설비는 MachineGhostTool.Confirm이 크로스 벨트 데이터("CrossBelt")대로 이미 냈다 —
            // 세그먼트 자체는 콘크리트를 안 들고(ConcreteCost 0), 주축 하나만 철거 시 그 기계 건설비를
            // 돌려받도록 표시한다(BeltSegment.RefundMachineKey).
            var primary = CreateCrossAxisSegment(cell, facing, 0);
            primary.RefundMachineKey = "CrossBelt";
            driver.World.Grid.RegisterSegment(cell, primary.Id);
            SpawnAxisVisual(cell, facing, primary.Id, isCrossing: false, showCrosser: true);

            var crossing = CreateCrossAxisSegment(cell, perpendicular, 0);
            driver.World.Grid.RegisterCrossingSegment(cell, crossing.Id);
            SpawnAxisVisual(cell, perpendicular, crossing.Id, isCrossing: true, showCrosser: false);
        }

        // 한 축(axis) 하나의 세그먼트를 만들고, 그 축의 양쪽 이웃(cell-axis=상류, cell+axis=하류)에
        // 자동 연결한다. 아직 세계에 등록(RegisterSegment/RegisterCrossingSegment)은 호출자가
        // 한다 — 어느 레이어에 넣을지는 이 함수가 몰라도 되게(축 계산에만 집중) 분리했다.
        private BeltSegment CreateCrossAxisSegment(Vector2Int cell, Vector2Int axis, int concreteCost)
        {
            var segment = new BeltSegment
            {
                Id = driver.World.Segments.Count,
                Length = 1f,
                ConcreteCost = concreteCost,
                IsCrossable = true,
                CrossAxis = axis,
            };

            Vector2Int upstreamCell = cell - axis;
            if (TryGetOccupantForConnection(upstreamCell, cell, out var upstream)
                && ResolveEndpointRole(upstream, cell, true, out _) == EndpointRole.Source)
            {
                if (upstream.Type == CellOccupantType.Processor)
                {
                    segment.SourceProcessorId = upstream.InstanceIndex;
                }
                else if (upstream.Type == CellOccupantType.Belt
                    && !driver.World.Segments[upstream.InstanceIndex].NextSegmentId.HasValue)
                {
                    driver.World.Segments[upstream.InstanceIndex].NextSegmentId = segment.Id;
                    RerenderSegmentStrip(upstream.InstanceIndex);
                }
            }

            Vector2Int downstreamCell = cell + axis;
            if (TryGetOccupantForConnection(downstreamCell, cell, out var downstream)
                && ResolveEndpointRole(downstream, cell, false, out _) == EndpointRole.Target)
            {
                if (downstream.Type == CellOccupantType.Processor)
                {
                    segment.TargetProcessorId = downstream.InstanceIndex;
                }
                else if (downstream.Type == CellOccupantType.Belt)
                {
                    segment.NextSegmentId = downstream.InstanceIndex;
                    RerenderSegmentStrip(downstream.InstanceIndex);
                }
            }

            driver.World.AddBeltSegment(segment);
            return segment;
        }

        // 세이브 복원용 — 세그먼트/그리드 등록은 세이브 코드가 이미 했고, 화면만 배치 때와 똑같이
        // 다시 그린다(주축이면 크로스 아이콘 + 방향 회전, 수직축이면 낮춰서 아무것도 안 그림).
        public void SpawnRestoredCrossVisual(Vector2Int cell, Vector2Int axis, int segmentId, bool isPrimaryAxis)
        {
            SpawnAxisVisual(cell, axis, segmentId, isCrossing: !isPrimaryAxis, showCrosser: isPrimaryAxis);
        }

        private void SpawnAxisVisual(Vector2Int cell, Vector2Int axis, int segmentId, bool isCrossing, bool showCrosser)
        {
            var renderPath = new List<Vector2Int> { cell - axis, cell, cell + axis };
            ComputeCellSpan(renderPath, 1, out Vector3 entry, out Vector3 exit, out Vector3? bend);
            // 크로스 타일의 두 축은 분류기/합류기처럼 "기계 안으로 들어갔다 나오는" 느낌이어야
            // 한다(사용자 요청) — 이 두 세그먼트 위에서는 아이템을 아예 안 그린다. 메쉬도 주축
            // (showCrosser)만 크로스 아이콘으로 그리고, 수직축은 아무것도 안 그린다(hideStrip) —
            // 안 그러면 아이콘 밑에 평범한 벨트 스트립이 따로 보여서 "벨트가 하나 더 설치된
            // 것처럼" 보인다(사용자 보고).
            SpawnCommittedVisual(entry, exit, bend, segmentId, isCrossing, showCrosser, hideItems: true, hideStrip: !showCrosser);
        }
    }
}
