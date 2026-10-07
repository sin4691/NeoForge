using System;
using System.Collections.Generic;
using Factory.Building;
using Factory.Buildings;
using Factory.Rendering;
using Factory.Simulation;
using UnityEngine;

namespace Choi.SaveLoad
{
    /// <summary>SimulationWorld 전체와 전력 배치를 PowerSaveManager의 JSON 항목 하나로 저장합니다.</summary>
    public sealed class FactorySaveBridge : MonoBehaviour, IPowerSaveParticipant
    {
        private SimulationDriver driver;
        private PowerGridSystem powerGrid;
        private PowerBuildController powerBuild;

        public string SaveId => "factory-progress";
        public string SaveType => "Choi.FactoryProgress.v1";
        public int SaveOrder => 0;
        public bool CanSave => driver != null && driver.World != null && powerGrid != null;

        private void Awake()
        {
            ResolveReferences();
        }

        public string CaptureStateJson()
        {
            ResolveReferences();
            if (!CanSave) throw new InvalidOperationException("Factory world or power grid is not ready.");

            SimulationWorld world = driver.World;
            var data = new FactoryProgressData
            {
                coreProcessorIndex = world.CoreProcessorIndex,
                powerNodes = powerGrid.CaptureNodes(),
                powerConnections = powerGrid.CaptureConnections(),
            };

            Dictionary<(CellOccupantType type, int index), Vector2Int> cells = ScanOccupants(world);
            CaptureMiners(world, data, cells);
            CaptureProcessors(world, data);
            CaptureBelts(world, data, cells);
            return JsonUtility.ToJson(data);
        }

        public void RestoreStateJson(string json)
        {
            ResolveReferences();
            if (driver == null || driver.World == null) throw new InvalidOperationException("Factory world is not ready.");

            FactoryProgressData data = JsonUtility.FromJson<FactoryProgressData>(json);
            if (data == null) throw new InvalidOperationException("Factory save data is invalid.");

            SimulationWorld world = driver.World;
            GameObject coreVisual = GameObject.Find("Core");
            ClearCurrentFactory(world, coreVisual);
            powerGrid.ResetRuntimeTracking();

            RestoreMiners(world, data.miners);
            RestoreProcessors(world, data.processors, data.coreProcessorIndex, coreVisual);
            world.CoreProcessorIndex = data.coreProcessorIndex;
            RelinkMiniCores(world, data.processors);
            RestoreBelts(world, data.belts);
            powerGrid.ReplaceNodes(data.powerNodes);
            powerGrid.ReplaceConnections(data.powerConnections);
            powerBuild?.RebuildVisuals();
            powerGrid.EvaluatePower();
        }

        private void CaptureMiners(SimulationWorld world, FactoryProgressData data,
            Dictionary<(CellOccupantType type, int index), Vector2Int> cells)
        {
            for (int i = 0; i < world.Miners.Count; i++)
            {
                MinerInstance miner = world.Miners[i];
                if (miner == null)
                {
                    data.miners.Add(new MinerProgressData { exists = false });
                    continue;
                }

                // 위치는 그리드(진짜 기준)에서 먼저 읽는다 — GameObject.Find는 화면 밖이라 꺼진
                // (뷰포트 컬링) 채굴기를 못 찾아서, 멀리 있는 채굴기가 (0,0)으로 저장되는 원인이었다.
                Vector2Int anchor;
                if (!cells.TryGetValue((CellOccupantType.Miner, i), out anchor))
                {
                    GameObject minerVisual = GameObject.Find($"Miner_{i}");
                    if (minerVisual != null) anchor = GridUtility.WorldToCell(minerVisual.transform.position);
                }
                data.miners.Add(new MinerProgressData
                {
                    exists = true,
                    machineKey = world.Database.Machines[miner.MachineId].Key,
                    outputResourceKey = world.Database.Resources[miner.OutputResourceId].Key,
                    baseSpeed = powerGrid.GetBaseSpeed(miner),
                    oreDepositKey = miner.OreDepositId >= 0 && miner.OreDepositId < world.Database.OreDeposits.Count
                        ? world.Database.OreDeposits[miner.OreDepositId].Key : string.Empty,
                    mineIntervalSeconds = miner.MineIntervalSeconds,
                    yieldPerCycle = miner.YieldPerCycle,
                    progress = miner.Progress,
                    bufferedOutput = miner.BufferedOutput,
                    anchor = ToData(anchor),
                });
            }
        }

        private void CaptureProcessors(SimulationWorld world, FactoryProgressData data)
        {
            for (int i = 0; i < world.Processors.Count; i++)
            {
                ProcessorInstance processor = world.Processors[i];
                if (processor == null)
                {
                    data.processors.Add(new ProcessorProgressData { exists = false });
                    continue;
                }

                var saved = new ProcessorProgressData
                {
                    exists = true,
                    machineKey = world.Database.Machines[processor.MachineId].Key,
                    recipeKey = powerGrid.GetDesiredRecipeId(processor) >= 0
                        ? world.Database.Recipes[powerGrid.GetDesiredRecipeId(processor)].Key : string.Empty,
                    activeRecipeKey = processor.ActiveRecipeId >= 0 ? world.Database.Recipes[processor.ActiveRecipeId].Key : string.Empty,
                    baseSpeed = processor.UniversalPorts ? processor.SpeedMultiplier : powerGrid.GetBaseSpeed(processor),
                    facing = ToData(processor.Facing),
                    anchor = ToData(processor.Anchor),
                    footprint = ToData(processor.Footprint),
                    universalPorts = processor.UniversalPorts,
                    routingCursor = processor.RoutingCursor,
                    generatorFuelPort = processor.IsGeneratorFuelPort,
                    ownerPowerNodeId = processor.OwnerPowerNodeId,
                    selectedFuelKey = processor.SelectedFuelResourceId >= 0
                        && processor.SelectedFuelResourceId < world.Database.Resources.Count
                        ? world.Database.Resources[processor.SelectedFuelResourceId].Key : string.Empty,
                    isProcessing = processor.IsProcessing,
                    progress = processor.Progress,
                    capacity = processor.Capacity,
                    input = CaptureStacks(processor.InputBuffer, world),
                    output = CaptureStacks(processor.OutputBuffer, world),
                };
                data.processors.Add(saved);
            }
        }

        private static void CaptureBelts(SimulationWorld world, FactoryProgressData data,
            Dictionary<(CellOccupantType type, int index), Vector2Int> cells)
        {
            // 벨트마다 씬 전체를 뒤지지 않게 한 번만 찾아둔다(GetBeltEndpoints 참고).
            BeltDragTool beltTool = FindAnyObjectByType<BeltDragTool>();
            for (int i = 0; i < world.Segments.Count; i++)
            {
                BeltSegment segment = world.Segments[i];
                if (segment == null)
                {
                    data.belts.Add(new BeltProgressData { exists = false });
                    continue;
                }

                cells.TryGetValue((CellOccupantType.Belt, i), out Vector2Int cell);
                GetBeltEndpoints(beltTool, i, cell, out Vector3 start, out Vector3 end);
                cell = GridUtility.WorldToCell((start + end) * 0.5f);
                var saved = new BeltProgressData
                {
                    exists = true,
                    id = i,
                    hasNext = segment.NextSegmentId.HasValue,
                    nextId = segment.NextSegmentId ?? -1,
                    length = segment.Length,
                    speed = segment.SpeedUnitsPerSecond,
                    itemSpacing = segment.ItemSpacing,
                    hasSourceProcessor = segment.SourceProcessorId.HasValue,
                    sourceProcessorIndex = segment.SourceProcessorId ?? -1,
                    hasTargetProcessor = segment.TargetProcessorId.HasValue,
                    targetProcessorIndex = segment.TargetProcessorId ?? -1,
                    hasLockedResource = segment.LockedSourceResourceId.HasValue,
                    lockedResourceKey = segment.LockedSourceResourceId.HasValue
                        ? world.Database.Resources[segment.LockedSourceResourceId.Value].Key : string.Empty,
                    lockedRecipeKey = segment.LockedForRecipeId >= 0
                        ? world.Database.Recipes[segment.LockedForRecipeId].Key : string.Empty,
                    cell = ToData(cell),
                    start = ToData(start),
                    end = ToData(end),
                    concreteCost = segment.ConcreteCost,
                    refundMachineKey = segment.RefundMachineKey ?? string.Empty,
                };

                // 크로스 벨트: 한 칸에 주축(1번 레이어)/수직축(2번 레이어) 세그먼트가 쌍으로 있다.
                // 어느 축인지(crossAxis)와 어느 레이어인지를 저장해야 불러올 때 같은 칸에 둘 다
                // 복원하고, 크로스 아이콘을 주축에만 올릴 수 있다.
                if (segment.IsCrossable)
                {
                    saved.isCrossable = true;
                    saved.crossAxis = ToData(segment.CrossAxis);
                    saved.isCrossingLayer = world.Grid.TryGetCrossingOccupant(cell, out var crossingHere)
                        && crossingHere.Type == CellOccupantType.Belt && crossingHere.InstanceIndex == i;
                }

                for (int j = 0; j < segment.Items.Count; j++)
                {
                    saved.items.Add(new BeltItemProgressData
                    {
                        resourceKey = world.Database.Resources[segment.Items[j].ResourceId].Key,
                        position = segment.Items[j].Position,
                    });
                }
                data.belts.Add(saved);
            }
        }

        private void RestoreMiners(SimulationWorld world, List<MinerProgressData> savedMiners)
        {
            if (savedMiners == null) return;
            for (int i = 0; i < savedMiners.Count; i++)
            {
                MinerProgressData saved = savedMiners[i];
                if (saved == null || !saved.exists)
                {
                    world.AddMiner(null);
                    continue;
                }

                if (!world.Database.TryGetMachineId(saved.machineKey, out int machineId)
                    || !world.Database.TryGetResourceId(saved.outputResourceKey, out int resourceId))
                {
                    world.AddMiner(null);
                    Debug.LogWarning($"[FactorySave] Miner slot {i} references missing data and was skipped.");
                    continue;
                }

                // 매장지 밸런스 패치 후 이 세이브를 로드해도 최신 값을 즉시 따라가도록 키로
                // 다시 찾는다(MinerSystem.Tick이 매 틱 이 id로 갱신). 키가 없는 구버전 세이브는
                // "이 자원을 캐는 매장지"로 유추해서 채운다(이 게임은 자원 하나당 매장지가
                // 하나뿐이라 안전한 매칭) — 그마저 실패하면(매장지 정의가 아예 삭제된 경우 등)
                // -1로 두고 세이브에 박제된 옛 값을 그대로 폴백으로 쓴다.
                int oreDepositId = -1;
                if (!string.IsNullOrEmpty(saved.oreDepositKey))
                {
                    world.Database.TryGetOreDepositId(saved.oreDepositKey, out oreDepositId);
                }
                if (oreDepositId < 0)
                {
                    for (int d = 0; d < world.Database.OreDeposits.Count; d++)
                    {
                        if (world.Database.OreDeposits[d].ResourceId != resourceId) continue;
                        oreDepositId = d;
                        break;
                    }
                }

                var miner = new MinerInstance
                {
                    MachineId = machineId,
                    OutputResourceId = resourceId,
                    SpeedMultiplier = saved.baseSpeed,
                    OreDepositId = oreDepositId,
                    MineIntervalSeconds = saved.mineIntervalSeconds,
                    YieldPerCycle = saved.yieldPerCycle,
                    Progress = saved.progress,
                    BufferedOutput = saved.bufferedOutput,
                };
                int index = world.AddMiner(miner);
                Vector2Int anchor = ToVector(saved.anchor);
                Vector2Int footprint = world.Database.Machines[machineId].Footprint;
                world.Grid.RegisterBuildingFootprint(GridUtility.GetFootprintCells(anchor, footprint), CellOccupantType.Miner, index);
                SpawnMachineVisual(world, saved.machineKey, anchor, footprint, Vector2Int.right, MachineInstanceKind.Miner, index, false);
            }
        }

        private void RestoreProcessors(SimulationWorld world, List<ProcessorProgressData> savedProcessors,
            int coreProcessorIndex, GameObject coreVisual)
        {
            if (savedProcessors == null) return;
            for (int i = 0; i < savedProcessors.Count; i++)
            {
                ProcessorProgressData saved = savedProcessors[i];
                if (saved == null || !saved.exists)
                {
                    world.AddProcessor(null);
                    continue;
                }

                if (!world.Database.TryGetMachineId(saved.machineKey, out int machineId))
                {
                    world.AddProcessor(null);
                    Debug.LogWarning($"[FactorySave] Processor slot {i} references missing machine '{saved.machineKey}'.");
                    continue;
                }

                var processor = new ProcessorInstance(world.Database.ResourceCount)
                {
                    MachineId = machineId,
                    RecipeId = ResolveRecipe(world, saved.recipeKey),
                    ActiveRecipeId = ResolveRecipe(world, saved.activeRecipeKey),
                    SpeedMultiplier = saved.baseSpeed,
                    Facing = ToVector(saved.facing),
                    Anchor = ToVector(saved.anchor),
                    Footprint = ToVector(saved.footprint),
                    UniversalPorts = saved.universalPorts,
                    // 분류기/합류기 표시는 machineKey로 다시 유도한다 — 배치 코드(MachineGhostTool)와
                    // 같은 규칙(RoutingRoles.For). 안 그러면 로드 후 None이 되어 라우팅/포트 표시가 죽는다.
                    RoutingRole = RoutingRoles.For(saved.machineKey),
                    RoutingCursor = saved.routingCursor,
                    IsProcessing = saved.isProcessing,
                    Progress = saved.progress,
                    Capacity = saved.capacity,
                    IsGeneratorFuelPort = saved.generatorFuelPort,
                    OwnerPowerNodeId = saved.ownerPowerNodeId,
                };
                if (processor.IsGeneratorFuelPort)
                {
                    world.Database.TryGetResourceId("Coal", out processor.CoalResourceId);
                    world.Database.TryGetResourceId("HighCapacityBattery", out processor.BatteryResourceId);
                    if (!string.IsNullOrEmpty(saved.selectedFuelKey)
                        && world.Database.TryGetResourceId(saved.selectedFuelKey, out int selectedFuelId))
                        processor.SelectedFuelResourceId = selectedFuelId;
                }
                RestoreStacks(processor.InputBuffer, saved.input, world);
                RestoreStacks(processor.OutputBuffer, saved.output, world);

                int index = world.AddProcessor(processor);
                world.Grid.RegisterBuildingFootprint(
                    GridUtility.GetFootprintCells(processor.Anchor, processor.Footprint), CellOccupantType.Processor, index);
                bool isCore = index == coreProcessorIndex;
                if (isCore && coreVisual != null)
                {
                    RebindCoreVisual(coreVisual, processor.Anchor, processor.Footprint, processor.Facing, index);
                }
                else if (!processor.IsGeneratorFuelPort)
                {
                    SpawnMachineVisual(world, saved.machineKey, processor.Anchor, processor.Footprint, processor.Facing,
                        MachineInstanceKind.Processor, index, isCore);
                }
            }
        }

        // 미니 코어는 설치할 때(MachineGhostTool.LinkToMainCore) 메인 코어의 버퍼 배열을 그대로
        // 물려받아 같은 창고를 본다. RestoreProcessors는 모든 기계를 새 버퍼로 만들기 때문에, 여기서
        // 다시 이어주지 않으면 로드 후 미니 코어가 별개의 창고가 된다 — 게다가 저장 시점에 미니 코어
        // 몫으로 기록된 내용물은 사실 코어 내용물이라 그대로 복원하면 자원이 복제된다. 그래서 저장된
        // 미니 코어 버퍼는 버리고 코어 배열로 교체한다.
        private static void RelinkMiniCores(SimulationWorld world, List<ProcessorProgressData> savedProcessors)
        {
            if (savedProcessors == null) return;
            int coreIndex = world.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= world.Processors.Count || world.Processors[coreIndex] == null) return;
            ProcessorInstance core = world.Processors[coreIndex];

            for (int i = 0; i < savedProcessors.Count && i < world.Processors.Count; i++)
            {
                ProcessorProgressData saved = savedProcessors[i];
                ProcessorInstance processor = world.Processors[i];
                if (saved == null || processor == null || i == coreIndex || saved.machineKey != "MiniCore") continue;

                processor.UniversalPorts = true;
                processor.InputBuffer = core.InputBuffer;
                processor.OutputBuffer = core.OutputBuffer;
                processor.Capacity = core.Capacity;
            }
        }

        private static void RestoreBelts(SimulationWorld world, List<BeltProgressData> savedBelts)
        {
            if (savedBelts == null) return;
            for (int i = 0; i < savedBelts.Count; i++)
            {
                BeltProgressData saved = savedBelts[i];
                if (saved == null || !saved.exists)
                {
                    world.AddBeltSegment(null);
                    continue;
                }

                var segment = new BeltSegment
                {
                    Id = i,
                    NextSegmentId = saved.hasNext ? saved.nextId : (int?)null,
                    Length = saved.length,
                    SpeedUnitsPerSecond = saved.speed,
                    ItemSpacing = saved.itemSpacing,
                    SourceProcessorId = saved.hasSourceProcessor ? saved.sourceProcessorIndex : (int?)null,
                    TargetProcessorId = saved.hasTargetProcessor ? saved.targetProcessorIndex : (int?)null,
                    LockedForRecipeId = ResolveRecipe(world, saved.lockedRecipeKey),
                    ConcreteCost = saved.concreteCost,
                    RefundMachineKey = string.IsNullOrEmpty(saved.refundMachineKey) ? null : saved.refundMachineKey,
                    IsCrossable = saved.isCrossable,
                    CrossAxis = saved.isCrossable ? ToVector(saved.crossAxis) : Vector2Int.zero,
                };
                if (saved.hasLockedResource && world.Database.TryGetResourceId(saved.lockedResourceKey, out int lockedId))
                    segment.LockedSourceResourceId = lockedId;

                if (saved.items != null)
                {
                    for (int j = 0; j < saved.items.Count; j++)
                    {
                        if (world.Database.TryGetResourceId(saved.items[j].resourceKey, out int resourceId))
                            segment.Items.Add(new BeltItem(resourceId, saved.items[j].position));
                    }
                }

                int index = world.AddBeltSegment(segment);
                Vector2Int cell = ToVector(saved.cell);

                // 크로스 벨트는 저장된 레이어대로 등록하고(수직축은 2번 레이어), 전용 그리기 경로로
                // 복원한다 — 일반 벨트 경로(SpawnBeltVisual)로 그리면 크로스 아이콘/아이템 숨김/
                // 수직축 무메쉬 처리가 전부 빠져서 벨트 두 개가 겹쳐 보인다.
                if (saved.isCrossable)
                {
                    if (saved.isCrossingLayer) world.Grid.RegisterCrossingSegment(cell, index);
                    else world.Grid.RegisterSegment(cell, index);

                    BeltDragTool crossTool = FindAnyObjectByType<BeltDragTool>();
                    if (crossTool != null)
                    {
                        crossTool.SpawnRestoredCrossVisual(cell, segment.CrossAxis, index, !saved.isCrossingLayer);
                        continue;
                    }
                }
                else
                {
                    world.Grid.RegisterSegment(cell, index);
                }

                SpawnBeltVisual(driver: FindAnyObjectByType<SimulationDriver>(), segmentId: index,
                    cell: cell, start: ToVector(saved.start), end: ToVector(saved.end));
            }

            RepairLegacyStraightBelts(world, savedBelts);
        }

        // 옛 코드로 저장한 세이브 보정. 그때는 저장 시점에 벨트 비주얼의 Start/End를 못 찾으면
        // "칸 중심 기준 세로 직선"을 대신 저장했다(GetBeltEndpoints 폴백) — 그래서 그런 세이브는
        // 코드를 고친 뒤에 불러와도 모든 벨트가 세로로 복원된다. 그래도 칸 위치(cell)와 연결
        // (nextId/소스·타깃 기계)은 멀쩡히 저장돼 있으니, 세로 직선으로 저장된 벨트만 골라서
        // 게임 중 재배선 때 쓰는 RerenderSegmentStrip으로 이웃 연결에 맞게 방향/코너를 다시
        // 계산해 그린다. 진짜 세로 벨트도 같은 결과로 다시 그려지므로 구분할 필요가 없고, 이미
        // 코너/가로로 정상 저장된 벨트는 건드리지 않는다. 다른 벨트/기계가 다 복원된 뒤에 해야
        // 이웃을 찾을 수 있어서 RestoreBelts 맨 끝에서 부른다. 이웃이 하나도 없는 외톨이 1칸
        // 벨트는 방향을 알 방법이 없어 그대로 둔다(RerenderSegmentStrip이 알아서 건너뜀).
        private static void RepairLegacyStraightBelts(SimulationWorld world, List<BeltProgressData> savedBelts)
        {
            BeltDragTool beltTool = FindAnyObjectByType<BeltDragTool>();
            if (beltTool == null) return;

            for (int i = 0; i < savedBelts.Count; i++)
            {
                BeltProgressData saved = savedBelts[i];
                if (saved == null || !saved.exists || i >= world.Segments.Count || world.Segments[i] == null) continue;
                // 크로스 벨트는 축을 직접 저장하므로 이웃 연결로 다시 계산하면 안 된다.
                if (saved.isCrossable) continue;

                bool alongZ = Mathf.Abs(saved.start.x - saved.end.x) < 0.01f
                    && Mathf.Abs(saved.start.z - saved.end.z) > 0.01f;
                if (alongZ) beltTool.RerenderSegmentStrip(i);
            }
        }

        private static List<ResourceStackData> CaptureStacks(int[] buffer, SimulationWorld world)
        {
            var result = new List<ResourceStackData>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == 0) continue;
                result.Add(new ResourceStackData { resourceKey = world.Database.Resources[i].Key, amount = buffer[i] });
            }
            return result;
        }

        private static void RestoreStacks(int[] buffer, List<ResourceStackData> stacks, SimulationWorld world)
        {
            if (stacks == null) return;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (world.Database.TryGetResourceId(stacks[i].resourceKey, out int resourceId))
                    buffer[resourceId] = Mathf.Max(0, stacks[i].amount);
            }
        }

        private static int ResolveRecipe(SimulationWorld world, string recipeKey)
        {
            return !string.IsNullOrEmpty(recipeKey) && world.Database.TryGetRecipeId(recipeKey, out int id) ? id : -1;
        }

        private static void ClearCurrentFactory(SimulationWorld world, GameObject preservedCoreVisual)
        {
            for (int i = 0; i < world.Miners.Count; i++) world.Grid.UnregisterOccupant(CellOccupantType.Miner, i);
            for (int i = 0; i < world.Processors.Count; i++) world.Grid.UnregisterOccupant(CellOccupantType.Processor, i);
            // 화면 밖 벨트는 뷰포트 컬링이 꺼둔(SetActive(false)) 상태라 GameObject.Find로는 못
            // 찾는다 — 그대로 두면 불러온 뒤에도 옛 벨트가 남아 새로 복원된 벨트와 겹친다. 레지스트리
            // 기반인 ReturnBeltVisual은 비활성 벨트도 처리하고, 파괴 대신 풀에 넣어 복원 때 재사용된다.
            BeltDragTool beltTool = FindAnyObjectByType<BeltDragTool>();
            for (int i = 0; i < world.Segments.Count; i++)
            {
                world.Grid.UnregisterOccupant(CellOccupantType.Belt, i);
                if (beltTool != null)
                {
                    beltTool.ReturnBeltVisual(i);
                }
                else
                {
                    GameObject belt = GameObject.Find($"Belt_{i}");
                    if (belt != null) Destroy(belt);
                }
            }

            MachineView[] views = FindObjectsByType<MachineView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null && views[i].gameObject != preservedCoreVisual) Destroy(views[i].gameObject);
            }
            // 방금 지운 것들을 가리키던 묵은 항목을 레지스트리에서 비운다(TryGet이 알아서
            // 죽은 참조를 걸러주긴 하지만, 로드/저장을 반복할수록 딕셔너리가 계속 커지는 건
            // 막는다) — FactoryViewportCuller가 이 레지스트리로 컬링 대상을 찾는다.
            MachineVisualRegistry.Clear();

            world.Miners.Clear();
            world.Processors.Clear();
            world.Segments.Clear();
            world.CoreProcessorIndex = -1;
        }

        private static void RebindCoreVisual(GameObject coreVisual, Vector2Int anchor, Vector2Int footprint,
            Vector2Int facing, int index)
        {
            // 회전은 건드리지 않는다 — 코어는 CoreSpawner가 Facing을 안 정해서 ProcessorInstance
            // 기본값(동쪽 (1,0))이 그대로 저장되는데, 그걸 LookRotation으로 되돌리면 불러올 때마다
            // 씬에 놓인 코어가 90도 돌아간다(사용자 보고). 코어는 플레이어가 회전시킬 수 없고 4면
            // 전부 입출력이라(UniversalPorts) Facing이 의미도 없다.
            coreVisual.transform.position = GridUtility.GetFootprintCenter(anchor, footprint, 0.75f);
            coreVisual.name = "Core";

            MachineView view = coreVisual.GetComponent<MachineView>() ?? coreVisual.AddComponent<MachineView>();
            view.Initialize(MachineInstanceKind.Processor, index, FindAnyObjectByType<SimulationDriver>());
        }

        private static void SpawnMachineVisual(SimulationWorld world, string machineKey, Vector2Int anchor,
            Vector2Int footprint, Vector2Int facing, MachineInstanceKind kind, int index, bool isCore)
        {
            GameObject prefab = FindMachinePrefab(machineKey);
            string addressableKey = world.Database.TryGetMachineId(machineKey, out int machineId)
                ? world.Database.Machines[machineId].PrefabName
                : string.Empty;
            Vector3 position = GridUtility.GetFootprintCenter(anchor, footprint, isCore ? 0.75f : 0.5f);
            // 코어는 저장된 Facing(기본값이라 의미 없음)을 따르지 않고 항상 기본 자세로 만든다.
            Quaternion rotation = isCore || facing == Vector2Int.zero
                ? Quaternion.identity
                : Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);

            GameObject visual;
            Transform animatedVisual = null;
            if (!string.IsNullOrEmpty(addressableKey))
            {
                visual = new GameObject();
                visual.transform.SetPositionAndRotation(position, rotation);

                var collider = visual.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.5f, 0f);
                collider.size = new Vector3(footprint.x, 1f, footprint.y);

                animatedVisual = new GameObject("Visual").transform;
                animatedVisual.SetParent(visual.transform, false);

                Color color = kind == MachineInstanceKind.Miner
                    ? new Color(0.55f, 0.4f, 0.25f)
                    : new Color(0.6f, 0.15f, 0.1f);
                GameObject placeholder = BuildVisuals.CreateBox(
                    position, new Vector3(footprint.x, 1f, footprint.y), color, animatedVisual, withCollider: false);
                placeholder.name = "Placeholder";
                animatedVisual.gameObject.AddComponent<AddressableModelMount>().Mount(addressableKey, placeholder);
            }
            else if (prefab != null)
            {
                visual = Instantiate(prefab, position, rotation);
            }
            else
            {
                Color color = kind == MachineInstanceKind.Miner
                    ? new Color(0.55f, 0.4f, 0.25f)
                    : new Color(0.6f, 0.15f, 0.1f);
                visual = BuildVisuals.CreateBox(position, Vector3.one, color, null);
                visual.transform.rotation = rotation;
            }

            visual.name = isCore ? "Core" : $"{kind}_{index}";
            // 코어는 뷰포트 컬링 대상이 아니라 레지스트리에 안 넣는다(항상 화면에 있어야 하는
            // 특수 건물이라 굳이 껐다 켰다 할 이유가 없음) — Factory.Rendering.
            // MachineVisualRegistry, FactoryViewportCuller 참고.
            if (!isCore) MachineVisualRegistry.Register(kind, index, visual);
            if (!isCore && animatedVisual == null)
            {
                Vector3 baseScale = visual.transform.localScale;
                visual.transform.localScale = new Vector3(baseScale.x * footprint.x, baseScale.y, baseScale.z * footprint.y);
            }
            MachineView view = visual.GetComponent<MachineView>() ?? visual.AddComponent<MachineView>();
            SimulationDriver simulationDriver = FindAnyObjectByType<SimulationDriver>();
            view.Initialize(kind, index, simulationDriver);
            if (animatedVisual != null)
                visual.AddComponent<MachineActivityIndicator>().Initialize(animatedVisual, kind, index, simulationDriver);
        }

        private static GameObject FindMachinePrefab(string machineKey)
        {
            MachineVisualLibrary[] libraries = Resources.FindObjectsOfTypeAll<MachineVisualLibrary>();
            for (int i = 0; i < libraries.Length; i++)
            {
                if (libraries[i] != null && libraries[i].TryGetPrefab(machineKey, out GameObject prefab)) return prefab;
            }
            return null;
        }

        private static void SpawnBeltVisual(SimulationDriver driver, int segmentId, Vector2Int cell, Vector3 start, Vector3 end)
        {
            if (driver == null) return;
            if ((start - end).sqrMagnitude < 0.001f)
            {
                Vector3 center = GridUtility.CellToWorldCenter(cell, 0.5f);
                start = center - Vector3.forward * 0.5f;
                end = center + Vector3.forward * 0.5f;
            }

            BeltDragTool beltTool = FindAnyObjectByType<BeltDragTool>();
            if (beltTool != null)
            {
                Vector3 groundStart = new Vector3(start.x, 0f, start.z);
                Vector3 groundEnd = new Vector3(end.x, 0f, end.z);
                bool restoredCorner = !Mathf.Approximately(groundStart.x, groundEnd.x)
                    && !Mathf.Approximately(groundStart.z, groundEnd.z);
                Vector3? bend = restoredCorner ? GridUtility.CellToWorldCenter(cell, 0f) : (Vector3?)null;
                beltTool.SpawnRestoredVisual(groundStart, groundEnd, bend, segmentId);
                return;
            }

            Vector3 stripStart = new Vector3(start.x, 0.5f, start.z);
            Vector3 stripEnd = new Vector3(end.x, 0.5f, end.z);
            Vector3 centerPoint = GridUtility.CellToWorldCenter(cell, 0.5f);
            bool corner = !Mathf.Approximately(stripStart.x, stripEnd.x) && !Mathf.Approximately(stripStart.z, stripEnd.z);

            var root = new GameObject($"Belt_{segmentId}");
            Transform startAnchor = new GameObject("Start").transform;
            startAnchor.SetParent(root.transform);
            startAnchor.position = new Vector3(start.x, 0.745f, start.z);
            Transform endAnchor = new GameObject("End").transform;
            endAnchor.SetParent(root.transform);
            endAnchor.position = new Vector3(end.x, 0.745f, end.z);

            Color color = new Color(0.15f, 0.15f, 0.15f, 1f);
            if (corner)
            {
                BuildVisuals.CreateStrip(stripStart, centerPoint, 0.6f, color, root.transform);
                BuildVisuals.CreateStrip(centerPoint, stripEnd, 0.6f, color, root.transform);
            }
            else
            {
                BuildVisuals.CreateStrip(stripStart, stripEnd, 0.6f, color, root.transform);
            }

            var itemRenderer = root.AddComponent<BeltItemRenderer>();
            itemRenderer.Initialize(driver, segmentId, startAnchor, endAnchor, FindLoadedPrefab("BeltItemVisual"));
        }

        private static GameObject FindLoadedPrefab(string prefabName)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && !objects[i].scene.IsValid() && objects[i].name == prefabName) return objects[i];
            }
            return null;
        }

        private static Dictionary<(CellOccupantType type, int index), Vector2Int> ScanOccupants(SimulationWorld world)
        {
            // 예전엔 원점 ±64칸만 훑어서, 그 밖에 지은 기계는 위치가 (0,0)으로 저장됐다.
            // 그리드가 들고 있는 점유 정보를 그대로 읽으므로 거리 제한이 없다.
            var result = new Dictionary<(CellOccupantType, int), Vector2Int>();
            world.Grid.CollectAnchorCells(result);
            return result;
        }

        private static void GetBeltEndpoints(BeltDragTool beltTool, int index, Vector2Int cell, out Vector3 start, out Vector3 end)
        {
            // 벨트 비주얼은 두 가지 이유로 예전 방식(GameObject.Find + 루트 바로 밑 Start/End)으로는
            // 못 찾는다: (1) Start/End/Bend 앵커가 루트 바로 밑이 아니라 "Geometry" 자식 밑으로 들어갔고
            // (BeltDragTool.SpawnCommittedVisual), (2) 화면 밖 벨트는 뷰포트 컬링이 SetActive(false)로
            // 꺼두는데 GameObject.Find는 비활성 오브젝트를 못 찾는다. 못 찾으면 아래 폴백(전부 같은
            // 방향의 직선)이 되어서, 저장한 뒤 불러오면 모든 벨트가 코너/방향이 사라진 똑같은
            // 모양으로 복원됐다(사용자 보고). 그래서 BeltDragTool의 레지스트리로 먼저 찾는다.
            GameObject root = null;
            if (beltTool != null) beltTool.TryGetBeltVisual(index, out root);
            if (root == null) root = GameObject.Find($"Belt_{index}");

            Transform startTransform = null;
            Transform endTransform = null;
            if (root != null)
            {
                startTransform = root.transform.Find("Geometry/Start") ?? root.transform.Find("Start");
                endTransform = root.transform.Find("Geometry/End") ?? root.transform.Find("End");
            }
            if (startTransform != null && endTransform != null)
            {
                start = startTransform.position;
                end = endTransform.position;
                return;
            }

            Vector3 center = GridUtility.CellToWorldCenter(cell, 0.745f);
            start = center - Vector3.forward * 0.5f;
            end = center + Vector3.forward * 0.5f;
        }

        private void ResolveReferences()
        {
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (powerGrid == null) powerGrid = GetComponent<PowerGridSystem>() ?? FindAnyObjectByType<PowerGridSystem>();
            if (powerBuild == null) powerBuild = GetComponent<PowerBuildController>() ?? FindAnyObjectByType<PowerBuildController>();
        }

        private static Int2Data ToData(Vector2Int value) => new Int2Data(value.x, value.y);
        private static Vector2Int ToVector(Int2Data value) => new Vector2Int(value.x, value.y);
        private static Vector3Data ToData(Vector3 value) => new Vector3Data(value.x, value.y, value.z);
        private static Vector3 ToVector(Vector3Data value) => new Vector3(value.x, value.y, value.z);
    }
}
