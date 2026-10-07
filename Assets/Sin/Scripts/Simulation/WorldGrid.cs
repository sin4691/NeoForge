using System.Collections.Generic;
using UnityEngine;

namespace Factory.Simulation
{
    public enum CellOccupantType
    {
        Belt,
        Miner,
        Processor,
    }

    public readonly struct CellOccupant
    {
        public readonly CellOccupantType Type;
        public readonly int InstanceIndex;

        public CellOccupant(CellOccupantType type, int instanceIndex)
        {
            Type = type;
            InstanceIndex = instanceIndex;
        }
    }

    // 월드를 고정 크기 청크로 나눠 관리하고, 셀 단위 점유 정보(무엇이 놓여 있는지)를 들고 있는다.
    // 시뮬레이션 계산 자체는 전체 청크에 대해 계속 돈다 (오프라인 진행 계산이 결국 안 보이던
    // 시간의 시뮬레이션을 재현해야 하므로 화면 밖이라고 계산을 끄면 안 된다). 컬링은 렌더링
    // 단계에서 "화면에 걸치는 청크만 갱신"하는 방식으로 적용한다 — 이번 패스는 가시 청크
    // 판정까지만 구현하고, 풀링 최적화는 이후 과제로 남긴다.
    public sealed class WorldGrid
    {
        public const int ChunkSize = 16;

        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Dictionary<Vector2Int, CellOccupant> occupants = new Dictionary<Vector2Int, CellOccupant>();

        // 지금까지 뭔가 한 번이라도 등록된 적 있는 청크 전부(빈 청크 포함 안 함 — Register*가
        // GetOrCreateChunk로 실제로 뭔가 넣을 때만 생긴다). FactoryViewportCuller가 켜지는
        // 첫 순간, "카메라가 지금 안 보는 곳에 있던 기존 건물들"을 한 번에 찾아서 꺼야 하는데,
        // GetVisibleChunks(RectInt)는 "보이는 범위"만 주지 "존재하는 전부"는 안 줘서 이게 필요하다.
        public IEnumerable<Chunk> AllChunks => chunks.Values;

        // 크로스 벨트(교차로) 전용 2번째 벨트 레이어 — 한 칸에 서로 직각으로 지나가는 벨트
        // 두 개가 동시에 있을 수 있게 한다(합류가 아니라 그냥 지나침, BeltDragTool.Crossing.cs
        // 참고). IsOccupied/TryGetOccupant(1번 레이어)는 일부러 이 레이어를 안 본다 — 기계
        // 배치나 다른 일반 겹침 판정은 여기 뭐가 있든 신경 쓸 필요가 없어야 하기 때문(원래
        // 벨트가 이미 occupants에 있어서 그쪽에서 겹침으로 걸러진다).
        private readonly Dictionary<Vector2Int, CellOccupant> crossingOccupants = new Dictionary<Vector2Int, CellOccupant>();

        // 건물 점유(occupants)와는 별개인 "땅" 레이어 — 채굴기는 광물 노드 위에 건물로 올라가야
        // 하므로 두 레이어가 같은 칸에 공존해야 한다(occupants에 넣으면 그 칸이 "점유됨"으로
        // 잡혀서 정작 채굴기를 못 짓게 된다).
        private readonly Dictionary<Vector2Int, int> oreDepositRuntimeIdByCell = new Dictionary<Vector2Int, int>();

        public bool IsOccupied(Vector2Int cell) => occupants.ContainsKey(cell);
        public bool TryGetOccupant(Vector2Int cell, out CellOccupant occupant) => occupants.TryGetValue(cell, out occupant);
        public bool TryGetCrossingOccupant(Vector2Int cell, out CellOccupant occupant) => crossingOccupants.TryGetValue(cell, out occupant);

        // 역방향 조회: (타입, 인덱스)가 차지한 칸을 찾는다. 벨트 세그먼트는 정확히 한 칸만
        // 차지하므로 첫 번째 일치를 반환한다(멀티칸 건물엔 쓰지 않는다). 벨트를 재배선한 뒤
        // 그 세그먼트의 스트립을 실제 흐름 방향으로 다시 그릴 때 쓴다(BeltDragTool). 크로스
        // 벨트의 2번째 레이어에 등록된 세그먼트도 찾아야 하므로 두 레이어 다 본다.
        public bool TryGetCellOf(CellOccupantType type, int instanceIndex, out Vector2Int cell)
        {
            foreach (var kvp in occupants)
            {
                if (kvp.Value.Type != type || kvp.Value.InstanceIndex != instanceIndex) continue;
                cell = kvp.Key;
                return true;
            }
            foreach (var kvp in crossingOccupants)
            {
                if (kvp.Value.Type != type || kvp.Value.InstanceIndex != instanceIndex) continue;
                cell = kvp.Key;
                return true;
            }
            cell = default;
            return false;
        }

        // Collects the anchor cell (min x, then min y) of every occupant. No distance limit.
        public void CollectAnchorCells(Dictionary<(CellOccupantType type, int index), Vector2Int> result)
        {
            foreach (var kvp in occupants) IncludeAnchorCell(result, kvp.Value, kvp.Key);
            foreach (var kvp in crossingOccupants)
            {
                var key = (kvp.Value.Type, kvp.Value.InstanceIndex);
                if (!result.ContainsKey(key)) result[key] = kvp.Key;
            }
        }

        private static void IncludeAnchorCell(Dictionary<(CellOccupantType type, int index), Vector2Int> result, CellOccupant occupant, Vector2Int cell)
        {
            var key = (occupant.Type, occupant.InstanceIndex);
            if (!result.TryGetValue(key, out var best) || cell.x < best.x || (cell.x == best.x && cell.y < best.y))
                result[key] = cell;
        }

        public void RegisterOreDeposit(Vector2Int cell, int oreDepositRuntimeId) => oreDepositRuntimeIdByCell[cell] = oreDepositRuntimeId;
        public bool TryGetOreDeposit(Vector2Int cell, out int oreDepositRuntimeId) => oreDepositRuntimeIdByCell.TryGetValue(cell, out oreDepositRuntimeId);

        // footprint 전체 칸이 하나도 안 겹치는지(멀티칸 배치 유효성 검사용).
        public bool IsFootprintFree(IReadOnlyList<Vector2Int> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (IsOccupied(cells[i])) return false;
            }
            return true;
        }

        public static Vector2Int WorldCellToChunkCoord(Vector2Int cell)
        {
            return new Vector2Int(FloorDiv(cell.x, ChunkSize), FloorDiv(cell.y, ChunkSize));
        }

        public Chunk GetOrCreateChunk(Vector2Int chunkCoord)
        {
            if (!chunks.TryGetValue(chunkCoord, out var chunk))
            {
                chunk = new Chunk(chunkCoord);
                chunks[chunkCoord] = chunk;
            }
            return chunk;
        }

        public void RegisterSegment(Vector2Int cell, int segmentId)
        {
            GetOrCreateChunk(WorldCellToChunkCoord(cell)).SegmentIds.Add(segmentId);
            occupants[cell] = new CellOccupant(CellOccupantType.Belt, segmentId);
        }

        // 크로스 타일의 수직축 세그먼트를 2번 레이어에 얹는다 — 주축(1번 레이어)은 그대로
        // 두고 안 건드린다(BeltDragTool.Crossing.cs의 PlaceCrossableTile 참고).
        public void RegisterCrossingSegment(Vector2Int cell, int segmentId)
        {
            GetOrCreateChunk(WorldCellToChunkCoord(cell)).SegmentIds.Add(segmentId);
            crossingOccupants[cell] = new CellOccupant(CellOccupantType.Belt, segmentId);
        }

        public void RegisterBuilding(Vector2Int cell, CellOccupantType type, int instanceIndex)
        {
            GetOrCreateChunk(WorldCellToChunkCoord(cell)).BuildingIds.Add(instanceIndex);
            occupants[cell] = new CellOccupant(type, instanceIndex);
        }

        // 코어처럼 여러 칸을 차지하는 건물용 — 같은 occupant를 footprint의 모든 칸에 등록한다.
        public void RegisterBuildingFootprint(IReadOnlyList<Vector2Int> cells, CellOccupantType type, int instanceIndex)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                RegisterBuilding(cells[i], type, instanceIndex);
            }
        }

        // 철거용 — RegisterSegment/RegisterBuilding(Footprint)이 등록한 칸(1번 레이어)을 그대로
        // 되돌린다. 크로스 벨트(2번 레이어)는 건드리지 않는다 — 이 칸에 뭐가 있든 어느 레이어
        // 것인지 모르고 지우면, 크로스 벨트 하나를 지웠는데 그 밑에 깔린 원래 벨트까지 같이
        // 지워지는(또는 그 반대) 사고가 난다. 레이어별로 정확히 지우려면 UnregisterCrossingCell을 쓴다.
        public void UnregisterCell(Vector2Int cell)
        {
            if (!occupants.TryGetValue(cell, out var occupant)) return;

            var chunk = GetOrCreateChunk(WorldCellToChunkCoord(cell));
            if (occupant.Type == CellOccupantType.Belt) chunk.SegmentIds.Remove(occupant.InstanceIndex);
            else chunk.BuildingIds.Remove(occupant.InstanceIndex);

            occupants.Remove(cell);
        }

        // UnregisterCell의 크로스 벨트(2번 레이어)판.
        public void UnregisterCrossingCell(Vector2Int cell)
        {
            if (!crossingOccupants.TryGetValue(cell, out var occupant)) return;
            GetOrCreateChunk(WorldCellToChunkCoord(cell)).SegmentIds.Remove(occupant.InstanceIndex);
            crossingOccupants.Remove(cell);
        }

        // 철거 대상 occupant가 차지한 칸 전부를 찾아 지운다. footprint가 몇 칸인지(멀티칸 건물)를
        // 호출자가 따로 알 필요 없게, occupants를 직접 훑어서 (type, instanceIndex)가 일치하는
        // 칸을 전부 찾는다 — 철거는 드문 조작이라 이 정도 스캔 비용은 무방하다. 크로스 벨트
        // 레이어도 같이 훑는다(segmentId는 두 레이어를 통틀어 겹치지 않으므로 안전).
        public void UnregisterOccupant(CellOccupantType type, int instanceIndex)
        {
            List<Vector2Int> matchingCells = null;
            foreach (var kvp in occupants)
            {
                if (kvp.Value.Type != type || kvp.Value.InstanceIndex != instanceIndex) continue;
                (matchingCells ??= new List<Vector2Int>()).Add(kvp.Key);
            }
            if (matchingCells != null)
            {
                for (int i = 0; i < matchingCells.Count; i++) UnregisterCell(matchingCells[i]);
            }

            // 두 레이어를 별도 리스트로 모으는 이유: 같은 칸이 양쪽 레이어에 다른 segmentId로
            // 동시에 있을 수 있어서, 한쪽만 찾았다고 그 칸에 UnregisterCell을 부르면(1번 레이어
            // 전용) 반대쪽 레이어의 다른 세그먼트를 못 지운다 — 레이어별로 정확히 자기 것만 지운다.
            List<Vector2Int> matchingCrossingCells = null;
            foreach (var kvp in crossingOccupants)
            {
                if (kvp.Value.Type != type || kvp.Value.InstanceIndex != instanceIndex) continue;
                (matchingCrossingCells ??= new List<Vector2Int>()).Add(kvp.Key);
            }
            if (matchingCrossingCells != null)
            {
                for (int i = 0; i < matchingCrossingCells.Count; i++) UnregisterCrossingCell(matchingCrossingCells[i]);
            }
        }

        // 카메라 시야(월드 셀 기준 사각형)와 겹치는 청크만 반환 — 렌더 갱신 대상 산정용.
        public IEnumerable<Chunk> GetVisibleChunks(RectInt viewCellBounds)
        {
            var minChunk = WorldCellToChunkCoord(new Vector2Int(viewCellBounds.xMin, viewCellBounds.yMin));
            var maxChunk = WorldCellToChunkCoord(new Vector2Int(viewCellBounds.xMax, viewCellBounds.yMax));

            for (int x = minChunk.x; x <= maxChunk.x; x++)
            {
                for (int y = minChunk.y; y <= maxChunk.y; y++)
                {
                    if (chunks.TryGetValue(new Vector2Int(x, y), out var chunk))
                    {
                        yield return chunk;
                    }
                }
            }
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if (a % b != 0 && (a < 0) != (b < 0)) q--;
            return q;
        }
    }
}
