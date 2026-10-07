using System;
using System.Collections.Generic;
using Factory.Simulation;
using UnityEngine;

namespace Seo.Building
{
    // 이동 대상과 원래 위치만 기억한다. 미리보기 동안 월드에는 손대지 않으며,
    // 확정 직전에 전체 목적지를 검사한 후 점유 정보와 경계 연결을 한 번에 갱신한다.
    public sealed class FactoryMoveSelection
    {
        public sealed class Entry
        {
            public CellOccupantType Type { get; internal set; }
            public int Index { get; internal set; }
            public Vector2Int Anchor { get; internal set; }
            public Vector2Int Footprint { get; internal set; }
            public bool Crossing { get; internal set; }
            internal object Instance;
            public string VisualName => Type == CellOccupantType.Belt ? $"Belt_{Index}" : $"{Type}_{Index}";
        }

        private readonly SimulationWorld world;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly HashSet<(CellOccupantType type, int index)> members = new HashSet<(CellOccupantType, int)>();
        private bool committed;
        public IReadOnlyList<Entry> Entries => entries;
        public SimulationWorld World => world;
        public RectInt Bounds { get; private set; }
        public bool HasEntries => entries.Count > 0;

        public FactoryMoveSelection(SimulationWorld world, IEnumerable<(CellOccupantType type, int index)> selection)
        {
            this.world = world;
            if (world == null || selection == null) return;
            foreach (var selected in selection) Add(selected.type, selected.index);
            // 교차 벨트는 같은 칸의 두 축을 항상 함께 이동한다.
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Type != CellOccupantType.Belt) continue;
                if (world.Grid.TryGetOccupant(entry.Anchor, out var primary) && primary.Type == CellOccupantType.Belt)
                    Add(primary.Type, primary.InstanceIndex);
                if (world.Grid.TryGetCrossingOccupant(entry.Anchor, out var crossing)) Add(crossing.Type, crossing.InstanceIndex);
            }
            if (entries.Count == 0) return;
            Vector2Int min = entries[0].Anchor;
            Vector2Int max = min + entries[0].Footprint;
            foreach (var entry in entries)
            {
                min = Vector2Int.Min(min, entry.Anchor);
                max = Vector2Int.Max(max, entry.Anchor + entry.Footprint);
            }
            Bounds = new RectInt(min, max - min);
        }

        public void IncludeBounds(RectInt bounds)
        {
            if (bounds.width <= 0 || bounds.height <= 0) return;
            if (entries.Count == 0 || Bounds.width <= 0 || Bounds.height <= 0)
            {
                Bounds = bounds;
                return;
            }
            int xMin = Mathf.Min(Bounds.xMin, bounds.xMin);
            int yMin = Mathf.Min(Bounds.yMin, bounds.yMin);
            int xMax = Mathf.Max(Bounds.xMax, bounds.xMax);
            int yMax = Mathf.Max(Bounds.yMax, bounds.yMax);
            Bounds = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        public bool ContainsSourceCell(Vector2Int cell)
        {
            foreach (var entry in entries)
                foreach (var source in GridUtility.GetFootprintCells(entry.Anchor, entry.Footprint))
                    if (source == cell) return true;
            return false;
        }

        private object GetInstance(CellOccupantType type, int index)
        {
            if (index < 0) return null;
            switch (type)
            {
                case CellOccupantType.Belt: return index < world.Segments.Count ? world.Segments[index] : null;
                case CellOccupantType.Miner: return index < world.Miners.Count ? world.Miners[index] : null;
                case CellOccupantType.Processor: return index < world.Processors.Count ? world.Processors[index] : null;
                default: return null;
            }
        }

        private void Add(CellOccupantType type, int index)
        {
            if (members.Contains((type, index))) return;
            object instance = GetInstance(type, index);
            if (instance == null) return;
            if (instance is ProcessorInstance p && (index == world.CoreProcessorIndex || p.IsGeneratorFuelPort)) return;
            if (!world.Grid.TryGetCellOf(type, index, out var cell)) return;
            var entry = new Entry { Type = type, Index = index, Anchor = cell, Footprint = Vector2Int.one, Instance = instance };
            if (instance is ProcessorInstance processor)
            {
                entry.Anchor = processor.Anchor;
                entry.Footprint = processor.Footprint;
            }
            else if (instance is MinerInstance miner)
                entry.Footprint = world.Database.Machines[miner.MachineId].Footprint;
            else
                entry.Crossing = world.Grid.TryGetCrossingOccupant(cell, out var crossing) && crossing.InstanceIndex == index;
            members.Add((type, index));
            entries.Add(entry);
        }

        public static int NormalizeTurns(int quarterTurns) => ((quarterTurns % 4) + 4) % 4;

        public static Vector2Int RotateDirection(Vector2Int direction, int quarterTurns)
        {
            // 기존 기계 설치의 회전 버튼과 같은 방향으로 돌린다.
            for (int i = 0; i < NormalizeTurns(quarterTurns); i++)
                direction = new Vector2Int(-direction.y, direction.x);
            return direction;
        }

        public RectInt GetTargetBounds(Vector2Int offset, int quarterTurns)
        {
            var size = Bounds.size;
            if ((NormalizeTurns(quarterTurns) & 1) != 0) size = new Vector2Int(size.y, size.x);
            return new RectInt(Bounds.position + offset, size);
        }

        // 최소 모서리를 기준으로 회전된 사각형을 정렬한다. 홀수/짝수 크기 조합도 반 칸 어긋나지 않는다.
        public Vector2Int TransformCell(Vector2Int cell, Vector2Int offset, int quarterTurns)
        {
            var local = cell - Bounds.position;
            switch (NormalizeTurns(quarterTurns))
            {
                case 1: local = new Vector2Int(Bounds.height - 1 - local.y, local.x); break;
                case 2: local = new Vector2Int(Bounds.width - 1 - local.x, Bounds.height - 1 - local.y); break;
                case 3: local = new Vector2Int(local.y, Bounds.width - 1 - local.x); break;
            }
            return Bounds.position + offset + local;
        }

        public Vector3 TransformPosition(Vector3 position, Vector2Int offset, int quarterTurns)
        {
            var origin = new Vector3(Bounds.xMin, 0f, Bounds.yMin) * GridUtility.CellSize;
            var local = position - origin;
            float width = Bounds.width * GridUtility.CellSize;
            float height = Bounds.height * GridUtility.CellSize;
            switch (NormalizeTurns(quarterTurns))
            {
                case 1: local = new Vector3(height - local.z, local.y, local.x); break;
                case 2: local = new Vector3(width - local.x, local.y, height - local.z); break;
                case 3: local = new Vector3(local.z, local.y, width - local.x); break;
            }
            return origin + new Vector3(offset.x, 0f, offset.y) * GridUtility.CellSize + local;
        }

        public Vector2Int GetTargetAnchor(Entry entry, Vector2Int offset, int quarterTurns) => Vector2Int.Min(
            TransformCell(entry.Anchor, offset, quarterTurns),
            TransformCell(entry.Anchor + entry.Footprint - Vector2Int.one, offset, quarterTurns));

        public static Vector2Int GetTargetFootprint(Entry entry, int quarterTurns) =>
            (NormalizeTurns(quarterTurns) & 1) == 0 ? entry.Footprint : new Vector2Int(entry.Footprint.y, entry.Footprint.x);

        public bool Validate(Vector2Int offset, out string reason,
            Func<Vector2Int, bool> externallyBlocked = null, Func<string, Vector2Int, bool> placementAllowed = null,
            int quarterTurns = 0)
        {
            reason = null;
            if (committed || entries.Count == 0) { reason = "이동할 기계·벨트를 선택하세요 · 코어와 전력 시설 제외"; return false; }
            if (offset == Vector2Int.zero && NormalizeTurns(quarterTurns) == 0)
            { reason = "선택한 묶음을 드래그하거나 회전하세요"; return false; }
            foreach (var entry in entries)
            {
                if (!ReferenceEquals(entry.Instance, GetInstance(entry.Type, entry.Index)))
                { reason = "선택한 설치물이 바뀌었습니다. 다시 선택하세요"; return false; }
                foreach (var source in GridUtility.GetFootprintCells(entry.Anchor, entry.Footprint))
                {
                    CellOccupant occupant;
                    bool present = entry.Crossing ? world.Grid.TryGetCrossingOccupant(source, out occupant)
                        : world.Grid.TryGetOccupant(source, out occupant);
                    if (!present || occupant.Type != entry.Type || occupant.InstanceIndex != entry.Index)
                    { reason = "선택한 설치물의 위치가 바뀌었습니다. 다시 선택하세요"; return false; }
                    var target = TransformCell(source, offset, quarterTurns);
                    if ((world.Grid.TryGetOccupant(target, out occupant) && !members.Contains((occupant.Type, occupant.InstanceIndex)))
                        || (world.Grid.TryGetCrossingOccupant(target, out occupant) && !members.Contains((occupant.Type, occupant.InstanceIndex)))
                        || (externallyBlocked?.Invoke(target) ?? false))
                    { reason = "다른 기계·벨트 또는 전력 시설과 겹칩니다"; return false; }
                }
                Vector2Int anchor = GetTargetAnchor(entry, offset, quarterTurns);
                if (entry.Instance is MinerInstance miner)
                {
                    if (!world.Grid.TryGetOreDeposit(anchor, out int depositId)
                        || world.Database.OreDeposits[depositId].ResourceId != miner.OutputResourceId)
                    { reason = "채굴기는 같은 종류의 광물이 있는 칸으로 이동하세요"; return false; }
                }
                int machineId = entry.Instance is ProcessorInstance processor ? processor.MachineId
                    : entry.Instance is MinerInstance m ? m.MachineId : -1;
                string key = machineId >= 0 ? world.Database.Machines[machineId].Key
                    : entry.Instance is BeltSegment belt && belt.IsCrossable ? "CrossBelt" : null;
                if (key != null && placementAllowed != null && !placementAllowed(key, anchor))
                { reason = "현재 설치 제한으로 이동할 수 없는 위치입니다"; return false; }
            }
            return true;
        }

        public bool TryCommit(Vector2Int offset, out string reason,
            Func<Vector2Int, bool> externallyBlocked = null, Func<string, Vector2Int, bool> placementAllowed = null,
            int quarterTurns = 0)
        {
            if (!Validate(offset, out reason, externallyBlocked, placementAllowed, quarterTurns)) return false;
            // 묶음 내부의 연결은 그대로 보존하고, 경계를 가로지르는 연결만 해제한다.
            for (int i = 0; i < world.Segments.Count; i++)
            {
                var segment = world.Segments[i];
                if (segment == null) continue;
                bool moved = members.Contains((CellOccupantType.Belt, i));
                if (segment.NextSegmentId.HasValue && moved != members.Contains((CellOccupantType.Belt, segment.NextSegmentId.Value)))
                    segment.NextSegmentId = null;
                if (segment.SourceProcessorId.HasValue && moved != members.Contains((CellOccupantType.Processor, segment.SourceProcessorId.Value)))
                {
                    segment.SourceProcessorId = null;
                    segment.LockedSourceResourceId = null;
                    segment.LockedForRecipeId = -1;
                }
                if (segment.TargetProcessorId.HasValue && moved != members.Contains((CellOccupantType.Processor, segment.TargetProcessorId.Value)))
                    segment.TargetProcessorId = null;
            }
            // 먼저 전부 비워야 원래 선택 영역에 일부 겹쳐 놓는 이동도 가능하다.
            foreach (var entry in entries) world.Grid.UnregisterOccupant(entry.Type, entry.Index);
            foreach (var entry in entries)
            {
                Vector2Int anchor = GetTargetAnchor(entry, offset, quarterTurns);
                Vector2Int footprint = GetTargetFootprint(entry, quarterTurns);
                if (entry.Type == CellOccupantType.Belt)
                {
                    var belt = (BeltSegment)entry.Instance;
                    belt.CrossAxis = RotateDirection(belt.CrossAxis, quarterTurns);
                    if (entry.Crossing) world.Grid.RegisterCrossingSegment(anchor, entry.Index);
                    else world.Grid.RegisterSegment(anchor, entry.Index);
                }
                else
                {
                    if (entry.Instance is ProcessorInstance processor)
                    {
                        processor.Anchor = anchor;
                        processor.Footprint = footprint;
                        processor.Facing = RotateDirection(processor.Facing, quarterTurns);
                    }
                    if (entry.Instance is MinerInstance miner && world.Grid.TryGetOreDeposit(anchor, out int depositId))
                    {
                        var deposit = world.Database.OreDeposits[depositId];
                        miner.OreDepositId = depositId;
                        miner.OutputResourceId = deposit.ResourceId;
                        miner.MineIntervalSeconds = deposit.MineIntervalSeconds;
                        miner.YieldPerCycle = deposit.YieldPerCycle;
                    }
                    world.Grid.RegisterBuildingFootprint(GridUtility.GetFootprintCells(anchor, footprint), entry.Type, entry.Index);
                }
            }
            committed = true;
            return true;
        }
    }
}
