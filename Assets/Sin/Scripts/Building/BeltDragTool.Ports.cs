using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // "이 칸이 소스/타겟/무효 중 뭔지" 판정하는 포트 규칙 모음 — Preview와 Commit
    // 양쪽에서 그대로 가져다 쓴다(따로 구현하면 어긋나는 버그가 난다).
    public partial class BeltDragTool
    {
        // 기계 포트(고정 입력/출력면), 코어(4면 다 유효), 기존 벨트(방향대로) 각각의 규칙으로
        // 이 endpoint가 소스/타겟/무효 중 뭔지 판정한다.
        // touchingCell: 드래그 경로상 기계 칸 바로 옆 칸(이게 어느 포트에 해당하는지로 역할이 정해짐).
        // isStart: 이 endpoint가 드래그 시작 쪽인지 (코어/벨트처럼 방향 의존적인 경우에만 씀).
        // isFixed: true면 포트 방향(Facing)만으로 확정된 값이라 절대 안 바뀐다(채굴기/제련로).
        // false면 코어/벨트처럼 "어느 쪽에 이어붙이느냐"로만 정해지는 값이라, 반대쪽이 고정
        // 역할을 가지고 있으면 그걸 보고 나중에 뒤집힐 수 있다(둘 다 같은 역할로 겹치는 것 방지).
        public EndpointRole ResolveEndpointRole(CellOccupant occupant, Vector2Int touchingCell, bool isStart, out bool isFixed)
        {
            switch (occupant.Type)
            {
                case CellOccupantType.Miner:
                    // 채굴기는 입출력 포트가 없다 — 캔 자원은 벨트 없이 코어로 곧장 원격 전송된다
                    // (MinerSystem 참고). 그래서 어느 면에 닿아도 벨트 연결 대상이 될 수 없다.
                    isFixed = true;
                    return EndpointRole.None;
                case CellOccupantType.Processor:
                {
                    var processor = driver.World.Processors[occupant.InstanceIndex];

                    if (processor.IsGeneratorFuelPort)
                    {
                        isFixed = true;
                        var generatorInputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint,
                            processor.Facing, isOutputSide: false);
                        return generatorInputs.Contains(touchingCell) ? EndpointRole.Target : EndpointRole.None;
                    }

                    if (processor.RoutingRole != RoutingRole.None)
                    {
                        isFixed = true;
                        // touchingCell = 노드(1x1)에 딱 붙은 벨트 칸. 방향으로 어느 면인지 판정.
                        // 분류기: -Facing(뒤)면만 입력, 나머지 3면 출력. 합류기: +Facing(앞)면만 출력, 나머지 3면 입력.
                        Vector2Int dir = touchingCell - processor.Anchor;
                        bool inputFace = processor.RoutingRole == RoutingRole.Splitter
                            ? dir == -processor.Facing
                            : dir != processor.Facing;
                        return inputFace ? EndpointRole.Target : EndpointRole.Source;
                    }

                    if (processor.UniversalPorts)
                    {
                        isFixed = false;
                        return isStart ? EndpointRole.Source : EndpointRole.Target;
                    }
                    isFixed = true;
                    // footprint가 1칸보다 클 수 있어서(예: 2x2 합성기), 밟은 칸(machineCell)이
                    // 아니라 앵커 기준으로 포트 칸 목록을 계산한다 — 어느 footprint 칸에
                    // 닿았든 앵커만 같으면 같은 결과가 나온다.
                    var outputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, isOutputSide: true);
                    if (outputs.Contains(touchingCell)) return EndpointRole.Source;
                    var inputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, isOutputSide: false);
                    if (inputs.Contains(touchingCell)) return EndpointRole.Target;
                    return EndpointRole.None;
                }
                case CellOccupantType.Belt:
                {
                    var belt = driver.World.Segments[occupant.InstanceIndex];

                    // 크로스 타일의 축 세그먼트는 일반 벨트와 달리 방향이 고정이다(CrossAxis,
                    // 기계 포트와 같은 개념) — 아무 쪽에서나 이어붙일 수 있는 보통 벨트와 섞어
                    // 취급하면, 그 축의 입력 쪽에서 거꾸로 드래그를 시작해도 "출력"으로 잘못
                    // 받아들여져서 실제 축 방향과 반대로 벨트가 이어지는 버그가 난다(사용자 보고:
                    // "왜 방향과 반대로도 설치가 되냐"). +CrossAxis 쪽에서 시작(Source)하거나
                    // -CrossAxis 쪽에서 끝(Target)나는 것만 유효하고, 그 반대는 전부 무효다.
                    if (belt != null && belt.IsCrossable)
                    {
                        isFixed = true;
                        if (!driver.World.Grid.TryGetCellOf(CellOccupantType.Belt, occupant.InstanceIndex, out var segCell))
                            return EndpointRole.None;
                        Vector2Int dir = touchingCell - segCell;
                        if (isStart && dir == belt.CrossAxis) return EndpointRole.Source;
                        if (!isStart && dir == -belt.CrossAxis) return EndpointRole.Target;
                        return EndpointRole.None;
                    }

                    isFixed = false;
                    if (isStart) return EndpointRole.Source;

                    // 드래그가 기존 벨트 칸에서 끝나는 경우, 그 벨트가 아직 아무 데서도 안
                    // 먹여지고 있는 체인의 시작일 때만 새 상류를 붙일 수 있다. 이미 다른
                    // 세그먼트(또는 기계)가 먹이고 있는 벨트에 억지로 연결하면 그 벨트 고유의
                    // 방향대로 아이템이 흘러버려서, 반대 방향으로 지어진 막다른 벨트끼리
                    // 마주보고 있을 때 아이템이 반대쪽으로 순간이동한 것처럼 보이는 버그가 생긴다.
                    return MachineGhostTool.IsChainStart(driver.World.Segments, belt)
                        ? EndpointRole.Target
                        : EndpointRole.None;
                }
                default:
                    isFixed = false;
                    return EndpointRole.None;
            }
        }

        public static EndpointRole Opposite(EndpointRole role) => role == EndpointRole.Source ? EndpointRole.Target : EndpointRole.Source;

        // 그냥 grid.TryGetOccupant(cell)만 쓰면, 크로스 타일(IsCrossable)의 경우 항상 1번
        // 레이어(주축) 세그먼트만 나온다 — 그런데 크로스 타일은 사실 한 칸에 서로 독립된
        // 벨트 세그먼트가 두 개(주축=1번 레이어, 수직축=2번 레이어) 있는 거라, 어느 방향에서
        // 접근했느냐(touchingCell 반대쪽)에 따라 실제로 말하는 세그먼트가 다르다. 이 보정 없이
        // ResolveEndpointRole에 그냥 넘기면, 수직축 쪽에서 연결하려 해도 항상 주축 세그먼트로
        // 판정돼서 "이미 딴 데로 흐르고 있다"는 식으로 엉뚱하게 막힌다(사용자 보고: 이미
        // 동서로 흐르는 크로스에 남쪽에서 새로 이으려니 "빈 공간이라 시작 불가"로 잘못 뜸).
        // Commit/Preview/TryFindAdjacentOccupant/BeltConnectionFeedback이 전부 이걸 거쳐야
        // ResolveEndpointRole이 항상 "맞는 축"으로 판정한다.
        public bool TryGetOccupantForConnection(Vector2Int cell, Vector2Int touchingCell, out CellOccupant occupant)
        {
            var grid = driver.World.Grid;
            if (!grid.TryGetOccupant(cell, out occupant)) return false;
            if (occupant.Type != CellOccupantType.Belt) return true;

            var segment = driver.World.Segments[occupant.InstanceIndex];
            if (segment == null || !segment.IsCrossable) return true;

            Vector2Int approachAxis = cell - touchingCell;
            bool approachIsHorizontal = approachAxis.y == 0;
            bool segIsHorizontal = segment.CrossAxis.y == 0;
            if (approachIsHorizontal == segIsHorizontal) return true; // 같은 축 — 1번 레이어가 맞다.

            // 반대 축(수직축) — 2번 레이어의 진짜 세그먼트로 바꿔치기한다.
            if (grid.TryGetCrossingOccupant(cell, out var crossOccupant))
            {
                occupant = crossOccupant;
            }
            return true;
        }

        private static readonly Vector2Int[] FourDirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        // 드래그가 기계 칸에 직접 닿지 않고 그 바로 옆(빈 칸)에서 끝나도, 그 칸에 인접한
        // 유효 포트가 있으면 자동으로 그 기계에 연결한다 — "기계까지 드래그해야만 연결된다"는
        // 제약을 없애기 위함(사용자 요청). fromDir는 경로상 바로 이전 칸 방향이라 되돌아가는
        // 방향은 후보에서 뺀다(막 지나온 칸을 다시 "인접한 기계"로 착각하지 않게).
        // Seo님의 BeltConnectionFeedback이 "빈 칸에서 시작 가능한지" 판정할 때도 이걸 그대로
        // 물어봐야 한다 — 자기 나름으로 grid.IsOccupied만 보고 판단하면, 옆칸 자동연결이
        // 가능한데도 UI만 "빈 공간이라 시작 불가"라고 잘못 보여주는 불일치가 생긴다.
        public bool TryFindAdjacentOccupant(Vector2Int cell, Vector2Int fromCell, bool isStart,
            out CellOccupant occupant, out EndpointRole role, out bool isFixed, out Vector2Int neighborCell)
        {
            // 한 칸이 위 기계의 입력 칸이면서 아래 기계의 출력 칸인 경우처럼 유효한 이웃이 여러
            // 개면, 순서대로 첫 번째를 고르면 항상 같은 쪽(북쪽)으로 고정돼서 반대쪽으로는 못 붙는다.
            // 드래그 시작 칸은 출력(Source), 끝 칸은 입력(Target)이 되는 이웃을 우선하고, 그런
            // 이웃이 없을 때만 나머지(역방향 드래그 등)로 물러난다.
            EndpointRole preferred = isStart ? EndpointRole.Source : EndpointRole.Target;
            bool found = false;
            CellOccupant bestOccupant = default;
            EndpointRole bestRole = EndpointRole.None;
            bool bestFixed = false;
            Vector2Int bestNeighbor = default;

            for (int d = 0; d < FourDirs.Length; d++)
            {
                Vector2Int neighbor = cell + FourDirs[d];
                if (neighbor == fromCell) continue;
                if (!TryGetOccupantForConnection(neighbor, cell, out var occ)) continue;
                // 기존 벨트(코너 조각 등)도 옆칸 자동연결 대상에 포함한다 — ResolveEndpointRole의
                // Belt 분기는 touchingCell 기하와 무관하게 isStart/IsChainStart만 보므로 그대로
                // 재사용해도 안전하다. 예전엔 여기서 막아놨었는데, 그러면 "끊어진 벨트를 코너에
                // 직접 안 닿고 재연결"하는 흔한 시나리오가 아예 안 통했다(사용자 보고).
                // 이미 출력이 연결된 벨트(다음 벨트나 기계로 흐르는 중)는 자동연결 대상에서 뺀다 —
                // 시작점으로 잡히면 "이미 연결됨"으로 막히기만 하고, 옆에 있는 진짜 출력(기계 등)은
                // 후보에 못 오른다. 끝점 쪽(이미 먹여지는 벨트)은 ResolveEndpointRole이 이미 거른다.
                if (isStart && occ.Type == CellOccupantType.Belt)
                {
                    var neighborSegment = driver.World.Segments[occ.InstanceIndex];
                    if (neighborSegment != null
                        && (neighborSegment.NextSegmentId.HasValue || neighborSegment.TargetProcessorId.HasValue))
                        continue;
                }

                var resolved = ResolveEndpointRole(occ, cell, isStart, out bool fixedRole);
                if (resolved == EndpointRole.None) continue;

                if (!found || (bestRole != preferred && resolved == preferred))
                {
                    found = true;
                    bestOccupant = occ;
                    bestRole = resolved;
                    bestFixed = fixedRole;
                    bestNeighbor = neighbor;
                }
            }

            occupant = bestOccupant;
            role = bestRole;
            isFixed = bestFixed;
            neighborCell = bestNeighbor;
            return found;
        }

        // 한 칸짜리 벨트(드래그 없이 탭) — 두 기계 사이가 딱 한 칸일 때처럼, 그 빈 칸 옆에 출력(Source)과
        // 입력(Target)이 각각 따로 있을 때만 그 사이를 잇는 연결 벨트로 허용한다. 실수로 툭 눌러서
        // 기계 옆에 막다른 벨트가 깔리지 않게 양쪽이 다 잡힐 때만 인정한다. 끝쪽 탐색에서는 시작에
        // 쓴 이웃을 제외(fromCell)해서 같은 기계를 양쪽에 쓰지 않게 한다.
        public bool TryResolveSingleCell(out CellOccupant startOccupant, out Vector2Int startNeighbor,
            out CellOccupant endOccupant, out Vector2Int endNeighbor)
        {
            startOccupant = default;
            startNeighbor = default;
            endOccupant = default;
            endNeighbor = default;
            if (driver == null || driver.World == null || path.Count != 1) return false;

            Vector2Int cell = path[0];
            if (driver.World.Grid.IsOccupied(cell)) return false;

            if (!TryFindAdjacentOccupant(cell, cell, true, out startOccupant, out var startRole, out _, out startNeighbor)
                || startRole != EndpointRole.Source) return false;

            return TryFindAdjacentOccupant(cell, startNeighbor, false, out endOccupant, out var endRole, out _, out endNeighbor)
                && endRole == EndpointRole.Target;
        }

        // 이 세그먼트로 흐름이 들어오는 쪽 칸: 다른 벨트가 먹이면 그 벨트 칸, 아니면 소스 기계의
        // footprint 칸 중 이 세그먼트에 딱 붙은 칸(코어처럼 Facing 없는 기계까지 포함). 방향 계산에만
        // 쓰므로 정확한 포트 칸이 아니라 "인접한 몸통 칸"이면 충분하다. Facing 기계인데 벨트가 포트에서
        // 한 칸 떨어져 있는(인접 아님) 드문 경우는 못 찾고 false — 그럼 직선으로만 다시 그린다.
        private bool TryGetUpstreamCell(BeltSegment segment, Vector2Int segmentCell, out Vector2Int cell)
        {
            var segments = driver.World.Segments;
            var grid = driver.World.Grid;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] == null || segments[i].NextSegmentId != segment.Id) continue;
                if (grid.TryGetCellOf(CellOccupantType.Belt, i, out cell)) return true;
            }

            if (segment.SourceProcessorId.HasValue)
            {
                var proc = driver.World.Processors[segment.SourceProcessorId.Value];
                if (proc != null)
                {
                    var body = GridUtility.GetFootprintCells(proc.Anchor, proc.Footprint);
                    for (int i = 0; i < body.Count; i++)
                    {
                        if (Mathf.Abs(body[i].x - segmentCell.x) + Mathf.Abs(body[i].y - segmentCell.y) == 1)
                        {
                            cell = body[i];
                            return true;
                        }
                    }
                }
            }

            cell = default;
            return false;
        }

        // TryGetUpstreamCell의 하류판 — 다음 벨트(NextSegmentId)로 흐르면 그 벨트 칸, 아니면
        // 곧장 먹이는 기계(TargetProcessorId)의 footprint 칸 중 이 세그먼트에 딱 붙은 칸을
        // 찾는다. 방향 계산 전용이라 정확한 포트 칸일 필요는 없다. RerenderSegmentStrip과
        // PreviewOnBuildingBeltBend가 둘 다 이걸 쓴다 — 따로 구현하면 한쪽만 체인을 챙기고
        // 한쪽은 안 챙기는 식으로 또 어긋나는 버그가 난다.
        private bool TryGetDownstreamCell(BeltSegment segment, Vector2Int segmentCell, out Vector2Int cell)
        {
            if (segment.NextSegmentId.HasValue
                && driver.World.Grid.TryGetCellOf(CellOccupantType.Belt, segment.NextSegmentId.Value, out cell))
            {
                return true;
            }

            if (segment.TargetProcessorId.HasValue)
            {
                var proc = driver.World.Processors[segment.TargetProcessorId.Value];
                if (proc != null)
                {
                    var body = GridUtility.GetFootprintCells(proc.Anchor, proc.Footprint);
                    for (int i = 0; i < body.Count; i++)
                    {
                        if (Mathf.Abs(body[i].x - segmentCell.x) + Mathf.Abs(body[i].y - segmentCell.y) == 1)
                        {
                            cell = body[i];
                            return true;
                        }
                    }
                }
            }
            cell = default;
            return false;
        }
    }
}
