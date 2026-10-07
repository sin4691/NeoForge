using System;
using System.Collections.Generic;
using Bae.Data;
using Factory.Building;
using Factory.Simulation;
using UnityEngine;

namespace Choi.SaveLoad
{
    public enum PowerNodeKind
    {
        Generator = 0,
        Cable = 1,
        TransmissionTower = 2,
        Junction = 3,
    }

    public sealed class PowerNodeRuntime
    {
        public int Id;
        public PowerNodeKind Kind;
        public Vector2Int Cell;
        public int FuelProcessorIndex = -1;
        // 발전기는 연료를 받는 면 하나만 가진다. Facing의 반대편이 입력 포트다.
        public Vector2Int Facing = Vector2Int.right;
        public float FuelSecondsRemaining;
        public int ActiveFuelResourceId = -1;
        public bool IsGenerating;
    }

    public sealed class PowerConnectionRuntime
    {
        public int Id;
        public int FromNodeId;
        public int ToNodeId;
        public List<Vector2Int> Path = new List<Vector2Int>();

        // 이 연결을 놓을 때 실제로 낸 구리선 양(PowerBuildController가 놓기 직전에 채워준다).
        // 철거 시 이만큼만 그대로 돌려준다 — 나중에 단가가 바뀌어도 이미 지어진 연결은
        // 자기가 실제로 낸 값을 기억한다(BeltSegment.ConcreteCost와 같은 원칙).
        public int CopperWireCost;
    }

    /// <summary>
    /// 발전기-전선에 연결된 송전탑의 15x15 공급 범위 안에 있는 기계만 작동시킵니다.
    /// 기존 시뮬레이션 코드는 수정하지 않고 각 인스턴스의 SpeedMultiplier만 제어합니다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PowerGridSystem : MonoBehaviour
    {
        public const int GeneratorOutput = 30;
        public const int GeneratorFuelCapacity = 60;
        public const float CoalBurnSeconds = 10f;
        public const float BatteryBurnSeconds = 60f;
        public const int CoreOutput = 100;
        public const int CoreRangeSize = 12;

        private readonly List<PowerNodeRuntime> nodes = new List<PowerNodeRuntime>();
        private readonly Dictionary<Vector2Int, PowerNodeRuntime> nodeByCell = new Dictionary<Vector2Int, PowerNodeRuntime>();
        private readonly List<PowerConnectionRuntime> connections = new List<PowerConnectionRuntime>();
        // EvaluatePower 한 번 동안만 쓰는 "전선이 하나라도 물린 노드" 목록(HasConnection 반복 스캔 대체).
        private readonly HashSet<int> connectedNodeIds = new HashSet<int>();
        private readonly Dictionary<MinerInstance, float> minerBaseSpeed = new Dictionary<MinerInstance, float>();
        private readonly Dictionary<ProcessorInstance, float> processorBaseSpeed = new Dictionary<ProcessorInstance, float>();
        private readonly Dictionary<ProcessorInstance, int> processorDesiredRecipe = new Dictionary<ProcessorInstance, int>();
        private readonly Dictionary<string, GameObject> indicators = new Dictionary<string, GameObject>();

        private SimulationDriver driver;
        private float evaluationTimer;
        private int nextNodeId;
        private int nextConnectionId;

        public IReadOnlyList<PowerNodeRuntime> Nodes => nodes;
        public IReadOnlyList<PowerConnectionRuntime> Connections => connections;
        public int AvailablePower { get; private set; }
        public int RequestedPower { get; private set; }
        public int UsedPower { get; private set; }
        public int PoweredMachineCount { get; private set; }
        public int TotalMachineCount { get; private set; }
        public int ActiveTowerCount { get; private set; }
        public bool IsBlackout { get; private set; }

        private void Awake()
        {
            driver = FindAnyObjectByType<SimulationDriver>();
        }

        private void Update()
        {
            TickGeneratorFuel(Time.deltaTime);
            evaluationTimer -= Time.unscaledDeltaTime;
            if (evaluationTimer > 0f) return;
            evaluationTimer = 0.2f;
            EvaluatePower();
        }

        public bool TryAddNode(PowerNodeKind kind, Vector2Int cell, Vector2Int facing = default)
        {
            if (kind == PowerNodeKind.Cable || nodeByCell.ContainsKey(cell)) return false;

            if (facing == Vector2Int.zero) facing = Vector2Int.right;
            var node = new PowerNodeRuntime { Id = nextNodeId++, Kind = kind, Cell = cell, Facing = facing };
            nodes.Add(node);
            nodeByCell[cell] = node;
            if (kind == PowerNodeKind.Generator) CreateGeneratorFuelPort(node);
            evaluationTimer = 0f;
            return true;
        }

        public bool TryGetNode(Vector2Int cell, out PowerNodeRuntime node)
        {
            return nodeByCell.TryGetValue(cell, out node);
        }

        public bool TryAddConnection(PowerNodeRuntime from, PowerNodeRuntime to, List<Vector2Int> path, int copperWireCost = 0)
        {
            if (from == null || to == null
                || !nodes.Contains(from) || !nodes.Contains(to)
                || to.Kind == PowerNodeKind.Cable
                || from.Kind == PowerNodeKind.Cable
                || !CanConnect(from, to)
                || from.Id == to.Id) return false;

            for (int i = 0; i < connections.Count; i++)
            {
                PowerConnectionRuntime connection = connections[i];
                if ((connection.FromNodeId == from.Id && connection.ToNodeId == to.Id)
                    || (connection.FromNodeId == to.Id && connection.ToNodeId == from.Id)) return false;
            }

            connections.Add(new PowerConnectionRuntime
            {
                Id = nextConnectionId++,
                FromNodeId = from.Id,
                ToNodeId = to.Id,
                Path = NormalizePath(path, from.Cell, to.Cell),
                CopperWireCost = copperWireCost,
            });
            evaluationTimer = 0f;
            return true;
        }

        public bool CanConnect(PowerNodeRuntime from, PowerNodeRuntime to)
        {
            if (from == null || to == null || from.Id == to.Id) return false;
            PowerNodeRuntime tower = from.Kind == PowerNodeKind.TransmissionTower ? from
                : to.Kind == PowerNodeKind.TransmissionTower ? to : null;
            PowerNodeRuntime generator = from.Kind == PowerNodeKind.Generator ? from
                : to.Kind == PowerNodeKind.Generator ? to : null;
            return tower == null || generator == null || !HasDirectGeneratorConnection(tower.Id);
        }

        private bool HasDirectGeneratorConnection(int towerId)
        {
            for (int i = 0; i < connections.Count; i++)
            {
                PowerConnectionRuntime connection = connections[i];
                int otherId = connection.FromNodeId == towerId ? connection.ToNodeId
                    : connection.ToNodeId == towerId ? connection.FromNodeId : -1;
                if (otherId < 0) continue;
                PowerNodeRuntime other = FindNodeById(otherId);
                if (other != null && other.Kind == PowerNodeKind.Generator) return true;
            }
            return false;
        }

        public bool TryResolveConnectionPoint(Vector2Int cell, out PowerNodeRuntime node)
        {
            if (nodeByCell.TryGetValue(cell, out node) && node.Kind != PowerNodeKind.Cable) return true;

            for (int i = connections.Count - 1; i >= 0; i--)
            {
                if (!TryFindPathSegment(connections[i].Path, cell, out int segmentIndex)) continue;
                node = CreateJunctionAndSplit(i, segmentIndex, cell);
                return node != null;
            }

            node = null;
            return false;
        }

        private PowerNodeRuntime CreateJunctionAndSplit(int connectionIndex, int segmentIndex, Vector2Int cell)
        {
            PowerConnectionRuntime original = connections[connectionIndex];
            PowerNodeRuntime from = FindNodeById(original.FromNodeId);
            PowerNodeRuntime to = FindNodeById(original.ToNodeId);
            if (from == null || to == null) return null;
            if (cell == from.Cell) return from;
            if (cell == to.Cell) return to;

            var junction = new PowerNodeRuntime
            {
                Id = nextNodeId++,
                Kind = PowerNodeKind.Junction,
                Cell = cell,
            };
            nodes.Add(junction);
            nodeByCell[cell] = junction;

            List<Vector2Int> fullPath = new List<Vector2Int>(original.Path);
            if (fullPath[segmentIndex] != cell && fullPath[segmentIndex + 1] != cell)
                fullPath.Insert(segmentIndex + 1, cell);
            int splitIndex = fullPath.IndexOf(cell);
            List<Vector2Int> firstPath = fullPath.GetRange(0, splitIndex + 1);
            List<Vector2Int> secondPath = fullPath.GetRange(splitIndex, fullPath.Count - splitIndex);

            // 원래 연결이 낸 구리선 비용을 두 조각에 나눠서 물려준다(길이 비례) — 둘 중 하나만
            // 나중에 철거해도 합쳐서 원래 낸 만큼만 돌아오고, 증발하거나 두 배로 돌아가지 않는다.
            int firstCost = fullPath.Count > 1
                ? original.CopperWireCost * splitIndex / (fullPath.Count - 1)
                : original.CopperWireCost;
            int secondCost = original.CopperWireCost - firstCost;

            connections.RemoveAt(connectionIndex);
            connections.Add(new PowerConnectionRuntime
            {
                Id = nextConnectionId++, FromNodeId = from.Id, ToNodeId = junction.Id, Path = firstPath,
                CopperWireCost = firstCost,
            });
            connections.Add(new PowerConnectionRuntime
            {
                Id = nextConnectionId++, FromNodeId = junction.Id, ToNodeId = to.Id, Path = secondPath,
                CopperWireCost = secondCost,
            });
            evaluationTimer = 0f;
            return junction;
        }

        public bool RemoveNode(Vector2Int cell, out int refundedCopperWire)
        {
            refundedCopperWire = 0;
            if (!nodeByCell.TryGetValue(cell, out PowerNodeRuntime node)) return false;
            nodeByCell.Remove(cell);
            RemoveGeneratorFuelPort(node);
            nodes.Remove(node);
            // 이 노드에 물려 있던 전선도 같이 끊어지므로, 그만큼 낸 구리선도 잊지 말고 같이
            // 환불한다 — 안 그러면 송전탑/발전기를 철거할 때마다 조용히 증발한다.
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                if (connections[i].FromNodeId != node.Id && connections[i].ToNodeId != node.Id) continue;
                refundedCopperWire += connections[i].CopperWireCost;
                connections.RemoveAt(i);
            }
            evaluationTimer = 0f;
            return true;
        }

        /// <summary>
        /// 발전기/송전탑을 재배치하고 해당 노드에 연결된 전선을 모두 끊는다.
        /// 건물 자체는 유지하므로 건설 비용은 환불하지 않고, 끊어진 전선 비용만 반환한다.
        /// </summary>
        public bool MoveNode(int nodeId, Vector2Int targetCell, Vector2Int facing, out int refundedCopperWire)
        {
            return MoveNodes(new Dictionary<int, Vector2Int> { [nodeId] = targetCell },
                new Dictionary<int, Vector2Int> { [nodeId] = facing }, out refundedCopperWire);
        }

        public bool MoveNodes(IReadOnlyDictionary<int, Vector2Int> targets,
            IReadOnlyDictionary<int, Vector2Int> facings, out int refundedCopperWire)
        {
            refundedCopperWire = 0;
            if (targets == null || targets.Count == 0) return false;
            var moving = new Dictionary<int, PowerNodeRuntime>();
            var occupiedTargets = new HashSet<Vector2Int>();
            foreach (var pair in targets)
            {
                PowerNodeRuntime node = FindNodeById(pair.Key);
                if (node == null || (node.Kind != PowerNodeKind.Generator
                    && node.Kind != PowerNodeKind.TransmissionTower) || !occupiedTargets.Add(pair.Value)) return false;
                moving[pair.Key] = node;
            }
            foreach (var pair in targets)
                if (nodeByCell.TryGetValue(pair.Value, out PowerNodeRuntime occupant) && !moving.ContainsKey(occupant.Id))
                    return false;

            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            foreach (PowerNodeRuntime node in moving.Values)
            {
                nodeByCell.Remove(node.Cell);
                if (node.Kind == PowerNodeKind.Generator && node.FuelProcessorIndex >= 0
                    && driver != null && driver.World != null)
                    driver.World.Grid.UnregisterOccupant(CellOccupantType.Processor, node.FuelProcessorIndex);
            }
            foreach (var pair in targets)
            {
                PowerNodeRuntime node = moving[pair.Key];
                node.Cell = pair.Value;
                if (node.Kind == PowerNodeKind.Generator && facings != null
                    && facings.TryGetValue(pair.Key, out Vector2Int facing) && facing != Vector2Int.zero)
                    node.Facing = facing;
                nodeByCell[node.Cell] = node;
                if (node.Kind == PowerNodeKind.Generator && node.FuelProcessorIndex >= 0
                    && driver != null && driver.World != null
                    && node.FuelProcessorIndex < driver.World.Processors.Count)
                {
                    ProcessorInstance port = driver.World.Processors[node.FuelProcessorIndex];
                    if (port != null)
                    {
                        port.Anchor = node.Cell;
                        port.Facing = node.Facing;
                        driver.World.Grid.RegisterBuilding(node.Cell, CellOccupantType.Processor, node.FuelProcessorIndex);
                    }
                }
            }

            for (int i = connections.Count - 1; i >= 0; i--)
            {
                if (!moving.ContainsKey(connections[i].FromNodeId) && !moving.ContainsKey(connections[i].ToNodeId)) continue;
                refundedCopperWire += connections[i].CopperWireCost;
                connections.RemoveAt(i);
            }
            RemoveUnusedJunctions();
            evaluationTimer = 0f;
            return true;
        }

        public bool RemoveConnectionAt(Vector2Int cell, out int refundedCopperWire)
        {
            refundedCopperWire = 0;
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                if (!TryFindPathSegment(connections[i].Path, cell, out _)) continue;
                refundedCopperWire = connections[i].CopperWireCost;
                connections.RemoveAt(i);
                RemoveUnusedJunctions();
                evaluationTimer = 0f;
                return true;
            }
            return false;
        }

        private static bool TryFindPathSegment(List<Vector2Int> path, Vector2Int cell, out int segmentIndex)
        {
            if (path != null)
            {
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2Int from = path[i];
                    Vector2Int to = path[i + 1];
                    long segmentX = to.x - from.x;
                    long segmentY = to.y - from.y;
                    long pointX = cell.x - from.x;
                    long pointY = cell.y - from.y;
                    bool onLine = segmentX * pointY == segmentY * pointX;
                    bool inBounds = cell.x >= Mathf.Min(from.x, to.x) && cell.x <= Mathf.Max(from.x, to.x)
                        && cell.y >= Mathf.Min(from.y, to.y) && cell.y <= Mathf.Max(from.y, to.y);
                    if (onLine && inBounds)
                    {
                        segmentIndex = i;
                        return true;
                    }
                }
            }
            segmentIndex = -1;
            return false;
        }

        private void RemoveUnusedJunctions()
        {
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                PowerNodeRuntime node = nodes[i];
                if (node.Kind != PowerNodeKind.Junction || HasConnection(node.Id)) continue;
                nodeByCell.Remove(node.Cell);
                nodes.RemoveAt(i);
            }
        }

        private static List<Vector2Int> NormalizePath(List<Vector2Int> requested, Vector2Int from, Vector2Int to)
        {
            var result = requested == null ? new List<Vector2Int>() : new List<Vector2Int>(requested);
            if (result.Count == 0 || result[0] != from) result.Insert(0, from);
            if (result[result.Count - 1] != to) result.Add(to);
            return result;
        }

        public bool IsGeneratorActive(int nodeId)
        {
            PowerNodeRuntime node = FindNodeById(nodeId);
            return node != null && node.Kind == PowerNodeKind.Generator && node.IsGenerating;
        }

        private void CreateGeneratorFuelPort(PowerNodeRuntime node)
        {
            if (node == null || node.Kind != PowerNodeKind.Generator) return;
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (driver == null || driver.World == null) return;
            SimulationWorld world = driver.World;
            if (!world.Database.TryGetMachineId("Generator", out int machineId)
                || !world.Database.TryGetResourceId("Coal", out int coalId)
                || !world.Database.TryGetResourceId("HighCapacityBattery", out int batteryId)) return;
            var port = new ProcessorInstance(world.Database.ResourceCount)
            {
                MachineId = machineId, RecipeId = -1, Anchor = node.Cell, Footprint = Vector2Int.one,
                Facing = node.Facing, Capacity = GeneratorFuelCapacity, IsGeneratorFuelPort = true,
                OwnerPowerNodeId = node.Id, CoalResourceId = coalId, BatteryResourceId = batteryId,
            };
            node.FuelProcessorIndex = world.AddProcessor(port);
            world.Grid.RegisterBuilding(node.Cell, CellOccupantType.Processor, node.FuelProcessorIndex);
        }

        private void BindGeneratorFuelPort(PowerNodeRuntime node)
        {
            if (node == null || node.Kind != PowerNodeKind.Generator) return;
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (driver == null || driver.World == null) return;
            for (int i = 0; i < driver.World.Processors.Count; i++)
            {
                ProcessorInstance p = driver.World.Processors[i];
                if (p == null || !p.IsGeneratorFuelPort || p.OwnerPowerNodeId != node.Id) continue;
                node.FuelProcessorIndex = i;
                p.Facing = node.Facing;
                return;
            }
            CreateGeneratorFuelPort(node);
        }

        private void RemoveGeneratorFuelPort(PowerNodeRuntime node)
        {
            if (node == null || node.FuelProcessorIndex < 0) return;
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (driver == null || driver.World == null) return;
            driver.World.Grid.UnregisterOccupant(CellOccupantType.Processor, node.FuelProcessorIndex);
            driver.World.RemoveProcessor(node.FuelProcessorIndex);
            node.FuelProcessorIndex = -1;
        }

        private void TickGeneratorFuel(float deltaSeconds)
        {
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (driver == null || driver.World == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                PowerNodeRuntime node = nodes[i];
                if (node.Kind != PowerNodeKind.Generator) continue;
                if (node.FuelProcessorIndex < 0) BindGeneratorFuelPort(node);
                ProcessorInstance port = node.FuelProcessorIndex >= 0 && node.FuelProcessorIndex < driver.World.Processors.Count
                    ? driver.World.Processors[node.FuelProcessorIndex] : null;
                if (port == null) { node.IsGenerating = false; continue; }
                if (node.FuelSecondsRemaining > 0f)
                    node.FuelSecondsRemaining = Mathf.Max(0f, node.FuelSecondsRemaining - Mathf.Max(0f, deltaSeconds));
                if (node.FuelSecondsRemaining <= 0f)
                {
                    node.ActiveFuelResourceId = -1;
                    int fuelId = port.SelectedFuelResourceId;
                    if (fuelId >= 0 && fuelId < port.InputBuffer.Length && port.InputBuffer[fuelId] > 0)
                    {
                        port.InputBuffer[fuelId]--;
                        node.ActiveFuelResourceId = fuelId;
                        node.FuelSecondsRemaining = fuelId == port.BatteryResourceId
                            ? BatteryBurnSeconds : CoalBurnSeconds;
                    }
                }
                node.IsGenerating = node.FuelSecondsRemaining > 0f;
            }
        }

        public void ReplaceNodes(List<PowerNodeData> savedNodes)
        {
            nodes.Clear();
            nodeByCell.Clear();
            connections.Clear();
            nextNodeId = 0;
            nextConnectionId = 0;

            if (savedNodes != null)
            {
                for (int i = 0; i < savedNodes.Count; i++)
                {
                    PowerNodeData saved = savedNodes[i];
                    if ((PowerNodeKind)saved.kind == PowerNodeKind.Cable) continue;
                    var cell = new Vector2Int(saved.cell.x, saved.cell.y);
                    if (nodeByCell.ContainsKey(cell)) continue;

                    var node = new PowerNodeRuntime
                    {
                        Id = saved.id,
                        Kind = (PowerNodeKind)saved.kind,
                        Cell = cell,
                        Facing = saved.facing.x == 0 && saved.facing.y == 0
                            ? Vector2Int.right : new Vector2Int(saved.facing.x, saved.facing.y),
                        FuelSecondsRemaining = Mathf.Max(0f, saved.fuelSecondsRemaining),
                        ActiveFuelResourceId = saved.activeFuelResourceId,
                    };
                    BindGeneratorFuelPort(node);
                    nodes.Add(node);
                    nodeByCell[cell] = node;
                    nextNodeId = Mathf.Max(nextNodeId, node.Id + 1);
                }
            }

            evaluationTimer = 0f;
        }

        public void ReplaceConnections(List<PowerConnectionData> savedConnections)
        {
            connections.Clear();
            nextConnectionId = 0;
            if (savedConnections == null) return;

            for (int i = 0; i < savedConnections.Count; i++)
            {
                PowerConnectionData saved = savedConnections[i];
                if (saved == null) continue;
                PowerNodeRuntime from = FindNodeById(saved.fromNodeId);
                PowerNodeRuntime to = FindNodeById(saved.toNodeId);
                if (from == null || to == null
                    || !CanConnect(from, to)) continue;
                connections.Add(new PowerConnectionRuntime
                {
                    Id = saved.id,
                    FromNodeId = saved.fromNodeId,
                    ToNodeId = saved.toNodeId,
                    Path = RestorePath(saved.path, saved.fromNodeId, saved.toNodeId),
                });
                nextConnectionId = Mathf.Max(nextConnectionId, saved.id + 1);
            }
            evaluationTimer = 0f;
        }

        public List<PowerNodeData> CaptureNodes()
        {
            var result = new List<PowerNodeData>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                result.Add(new PowerNodeData
                {
                    id = nodes[i].Id,
                    kind = (int)nodes[i].Kind,
                    cell = new Int2Data(nodes[i].Cell.x, nodes[i].Cell.y),
                    facing = new Int2Data(nodes[i].Facing.x, nodes[i].Facing.y),
                    fuelSecondsRemaining = nodes[i].FuelSecondsRemaining,
                    activeFuelResourceId = nodes[i].ActiveFuelResourceId,
                });
            }
            return result;
        }

        public List<PowerConnectionData> CaptureConnections()
        {
            var result = new List<PowerConnectionData>(connections.Count);
            for (int i = 0; i < connections.Count; i++)
            {
                result.Add(new PowerConnectionData
                {
                    id = connections[i].Id,
                    fromNodeId = connections[i].FromNodeId,
                    toNodeId = connections[i].ToNodeId,
                    path = CapturePath(connections[i].Path),
                });
            }
            return result;
        }

        private List<Vector2Int> RestorePath(List<Int2Data> savedPath, int fromNodeId, int toNodeId)
        {
            var path = new List<Vector2Int>();
            if (savedPath != null)
            {
                for (int i = 0; i < savedPath.Count; i++) path.Add(new Vector2Int(savedPath[i].x, savedPath[i].y));
            }
            PowerNodeRuntime from = FindNodeById(fromNodeId);
            PowerNodeRuntime to = FindNodeById(toNodeId);
            return from != null && to != null ? NormalizePath(path, from.Cell, to.Cell) : path;
        }

        private static List<Int2Data> CapturePath(List<Vector2Int> path)
        {
            var result = new List<Int2Data>();
            if (path == null) return result;
            for (int i = 0; i < path.Count; i++) result.Add(new Int2Data(path[i].x, path[i].y));
            return result;
        }

        public PowerNodeRuntime FindNodeById(int id)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Id == id) return nodes[i];
            }
            return null;
        }

        public float GetBaseSpeed(MinerInstance miner)
        {
            return miner != null && minerBaseSpeed.TryGetValue(miner, out float speed) ? speed : miner?.SpeedMultiplier ?? 1f;
        }

        public float GetBaseSpeed(ProcessorInstance processor)
        {
            return processor != null && processorBaseSpeed.TryGetValue(processor, out float speed) ? speed : processor?.SpeedMultiplier ?? 1f;
        }

        public int GetDesiredRecipeId(ProcessorInstance processor)
        {
            return processor != null && processorDesiredRecipe.TryGetValue(processor, out int recipeId)
                ? recipeId
                : processor?.RecipeId ?? -1;
        }

        public bool IsMachinePowered(CellOccupantType type, int index)
        {
            string key = IndicatorKey(type, index);
            return indicators.TryGetValue(key, out GameObject indicator) && indicator != null && indicator.name.EndsWith("_ON", StringComparison.Ordinal);
        }

        // 정전 중에는 Processor.RecipeId가 실행 차단을 위해 -1이 되므로, 보고서 UI는
        // 플레이어가 실제로 선택해 둔 레시피를 이 경로로 조회한다.
        public int GetConfiguredRecipeId(ProcessorInstance processor)
        {
            if (processor == null) return -1;
            return processorDesiredRecipe.TryGetValue(processor, out int recipeId)
                ? recipeId : processor.RecipeId;
        }

        public bool TryGetCorePowerCenter(out Vector2 center)
        {
            if (TryGetCore(out ProcessorInstance core))
            {
                center = new Vector2(core.Anchor.x + core.Footprint.x * 0.5f,
                    core.Anchor.y + core.Footprint.y * 0.5f);
                return true;
            }
            center = default;
            return false;
        }

        public bool IsCoreCell(Vector2Int cell)
        {
            if (!TryGetCore(out ProcessorInstance core)) return false;
            return cell.x >= core.Anchor.x && cell.x < core.Anchor.x + core.Footprint.x
                && cell.y >= core.Anchor.y && cell.y < core.Anchor.y + core.Footprint.y;
        }

        public void ResetRuntimeTracking()
        {
            minerBaseSpeed.Clear();
            processorBaseSpeed.Clear();
            processorDesiredRecipe.Clear();
            foreach (GameObject indicator in indicators.Values)
            {
                if (indicator != null) Destroy(indicator);
            }
            indicators.Clear();
            evaluationTimer = 0f;
        }

        public void EvaluatePower()
        {
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (driver == null || driver.World == null) return;

            // 송전탑 연결 여부를 기계마다 전선 목록 전체를 다시 훑어 판정하면(HasConnection)
            // 기계 × 송전탑 × 전선 수만큼 돌아서 대형 공장에서 평가 한 번이 수십 ms가 된다.
            // 이번 평가 동안은 연결이 안 바뀌므로 한 번만 모아두고 조회한다.
            connectedNodeIds.Clear();
            for (int i = 0; i < connections.Count; i++)
            {
                connectedNodeIds.Add(connections[i].FromNodeId);
                connectedNodeIds.Add(connections[i].ToNodeId);
            }

            BuildComponents(out Dictionary<int, int> componentByNodeId, out List<int> remainingByComponent);
            int coreComponent = AddCorePowerComponent(remainingByComponent);
            AvailablePower = 0;
            for (int i = 0; i < remainingByComponent.Count; i++) AvailablePower += remainingByComponent[i];
            int ratedCapacity = AvailablePower;
            IsBlackout = false;

            RequestedPower = 0;
            UsedPower = 0;
            PoweredMachineCount = 0;
            TotalMachineCount = 0;
            ActiveTowerCount = CountActiveTowers(componentByNodeId, remainingByComponent);
            var liveIndicatorKeys = new HashSet<string>();
            Dictionary<(CellOccupantType type, int index), Vector2Int> cells = ScanOccupants(driver.World);

            for (int i = 0; i < driver.World.Miners.Count; i++)
            {
                MinerInstance miner = driver.World.Miners[i];
                if (miner == null) continue;
                if (!minerBaseSpeed.ContainsKey(miner)) minerBaseSpeed[miner] = Mathf.Max(0.0001f, miner.SpeedMultiplier);

                string machineKey = driver.World.Database.Machines[miner.MachineId].Key;
                int demand = GetPowerConsumption(machineKey);
                // 그리드에서 위치를 읽는다 — GameObject.Find는 화면 밖이라 꺼진 채굴기를 못 찾고(그러면
                // 위치가 (0,0)이 돼 코어 근처로 잘못 전력 판정됨), 매번 씬 전체를 뒤지는 비용도 컸다.
                Vector2Int anchor;
                if (!cells.TryGetValue((CellOccupantType.Miner, i), out anchor))
                {
                    GameObject minerVisual = GameObject.Find($"Miner_{i}");
                    if (minerVisual != null) anchor = GridUtility.WorldToCell(minerVisual.transform.position);
                }
                int component = FindSupplyingPowerComponent(anchor, Vector2Int.one,
                    coreComponent, componentByNodeId, remainingByComponent);
                bool powered = component >= 0;
                miner.SpeedMultiplier = powered ? minerBaseSpeed[miner] : 0f;
                AccumulateMachineStatus(CellOccupantType.Miner, i, demand, powered, powered,
                    anchor, Vector2Int.one, liveIndicatorKeys);
            }

            for (int i = 0; i < driver.World.Processors.Count; i++)
            {
                ProcessorInstance processor = driver.World.Processors[i];
                // 코어와 분류기/합류기는 전력을 소비하지 않는 물류 설비다. 전력망 평가에
                // 포함하면 실제 라우팅은 계속되는데 상태 UI만 '전력 부족'으로 표시된다.
                if (processor == null || processor.UniversalPorts || processor.IsGeneratorFuelPort
                    || processor.RoutingRole != RoutingRole.None) continue;
                if (!processorBaseSpeed.ContainsKey(processor)) processorBaseSpeed[processor] = Mathf.Max(0.0001f, processor.SpeedMultiplier);
                if (!processorDesiredRecipe.TryGetValue(processor, out int desiredRecipe))
                {
                    desiredRecipe = processor.RecipeId;
                    processorDesiredRecipe[processor] = desiredRecipe;
                }
                else if (processor.RecipeId >= 0 && processor.RecipeId != desiredRecipe)
                {
                    // 전력 차단 중 UI에서 새 레시피를 골라도 선택값은 기억하고, 실제 실행만 막는다.
                    desiredRecipe = processor.RecipeId;
                    processorDesiredRecipe[processor] = desiredRecipe;
                }

                string machineKey = driver.World.Database.Machines[processor.MachineId].Key;
                int demand = GetPowerConsumption(machineKey);
                int component = FindSupplyingPowerComponent(processor.Anchor, processor.Footprint,
                    coreComponent, componentByNodeId, remainingByComponent);
                bool powered = component >= 0;
                processor.SpeedMultiplier = powered ? processorBaseSpeed[processor] : 0f;
                processor.RecipeId = powered ? desiredRecipe : -1;
                AccumulateMachineStatus(CellOccupantType.Processor, i, demand, powered, powered,
                    processor.Anchor, processor.Footprint, liveIndicatorKeys);
            }

            if (RequestedPower > ratedCapacity)
            {
                TriggerGlobalBlackout(liveIndicatorKeys);
            }

            RemoveDeadIndicators(liveIndicatorKeys);
        }

        private void TriggerGlobalBlackout(HashSet<string> liveIndicatorKeys)
        {
            IsBlackout = true;
            UsedPower = 0;
            PoweredMachineCount = 0;
            ActiveTowerCount = 0;

            for (int i = 0; i < driver.World.Miners.Count; i++)
            {
                MinerInstance miner = driver.World.Miners[i];
                if (miner != null) miner.SpeedMultiplier = 0f;
            }

            for (int i = 0; i < driver.World.Processors.Count; i++)
            {
                ProcessorInstance processor = driver.World.Processors[i];
                if (processor == null || processor.UniversalPorts || processor.IsGeneratorFuelPort
                    || processor.RoutingRole != RoutingRole.None) continue;
                processor.SpeedMultiplier = 0f;
                processor.RecipeId = -1;
            }

            foreach (string key in liveIndicatorKeys)
            {
                if (!indicators.TryGetValue(key, out GameObject indicator) || indicator == null) continue;
                indicator.name = key + "_OFF";
            }
        }

        private void BuildComponents(out Dictionary<int, int> componentByNodeId, out List<int> capacityByComponent)
        {
            componentByNodeId = new Dictionary<int, int>();
            capacityByComponent = new List<int>();
            var neighbors = new Dictionary<int, List<int>>();

            for (int i = 0; i < nodes.Count; i++) neighbors[nodes[i].Id] = new List<int>();
            for (int i = 0; i < connections.Count; i++)
            {
                PowerConnectionRuntime connection = connections[i];
                if (!neighbors.TryGetValue(connection.FromNodeId, out List<int> fromNeighbors)
                    || !neighbors.TryGetValue(connection.ToNodeId, out List<int> toNeighbors)) continue;
                fromNeighbors.Add(connection.ToNodeId);
                toNeighbors.Add(connection.FromNodeId);
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                int start = nodes[i].Id;
                if (componentByNodeId.ContainsKey(start)) continue;

                int component = capacityByComponent.Count;
                int capacity = 0;
                var queue = new Queue<int>();
                queue.Enqueue(start);
                componentByNodeId[start] = component;

                while (queue.Count > 0)
                {
                    int nodeId = queue.Dequeue();
                    PowerNodeRuntime node = FindNodeById(nodeId);
                    if (node == null) continue;
                    if (node.Kind == PowerNodeKind.Generator && node.IsGenerating) capacity += GeneratorOutput;

                    List<int> adjacent = neighbors[nodeId];
                    for (int d = 0; d < adjacent.Count; d++)
                    {
                        int neighborId = adjacent[d];
                        if (componentByNodeId.ContainsKey(neighborId)) continue;
                        componentByNodeId[neighborId] = component;
                        queue.Enqueue(neighborId);
                    }
                }

                capacityByComponent.Add(capacity);
            }
        }

        private int FindSupplyingTowerComponent(Vector2Int anchor, Vector2Int footprint,
            Dictionary<int, int> componentByNodeId, List<int> capacityByComponent)
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                PowerNodeRuntime tower = nodes[n];
                if (tower.Kind != PowerNodeKind.TransmissionTower
                    || !componentByNodeId.TryGetValue(tower.Id, out int component)
                    || component < 0 || component >= capacityByComponent.Count
                    || capacityByComponent[component] <= 0
                    || !connectedNodeIds.Contains(tower.Id))
                {
                    continue;
                }

                for (int x = 0; x < footprint.x; x++)
                {
                    for (int y = 0; y < footprint.y; y++)
                    {
                        Vector2Int occupied = new Vector2Int(anchor.x + x, anchor.y + y);
                        Vector2Int distance = occupied - tower.Cell;
                        if (Mathf.Abs(distance.x) <= 7 && Mathf.Abs(distance.y) <= 7) return component;
                    }
                }
            }
            return -1;
        }

        private int AddCorePowerComponent(List<int> capacityByComponent)
        {
            if (!TryGetCore(out _)) return -1;
            int component = capacityByComponent.Count;
            capacityByComponent.Add(CoreOutput);
            return component;
        }

        private int FindSupplyingPowerComponent(Vector2Int anchor, Vector2Int footprint, int coreComponent,
            Dictionary<int, int> componentByNodeId, List<int> capacityByComponent)
        {
            if (coreComponent >= 0 && IsInCorePowerRange(anchor, footprint)) return coreComponent;
            return FindSupplyingTowerComponent(anchor, footprint, componentByNodeId, capacityByComponent);
        }

        private bool IsInCorePowerRange(Vector2Int anchor, Vector2Int footprint)
        {
            if (!TryGetCore(out ProcessorInstance core)) return false;

            Vector2 coreCenter = new Vector2(core.Anchor.x + core.Footprint.x * 0.5f,
                core.Anchor.y + core.Footprint.y * 0.5f);
            float halfRange = CoreRangeSize * 0.5f;

            // 2x2 코어 전체의 기하학적 중심을 기준으로 정확히 12x12 영역에 공급한다.
            for (int x = 0; x < footprint.x; x++)
            {
                for (int y = 0; y < footprint.y; y++)
                {
                    Vector2Int occupied = new Vector2Int(anchor.x + x, anchor.y + y);
                    Vector2 occupiedCenter = new Vector2(occupied.x + 0.5f, occupied.y + 0.5f);
                    if (occupiedCenter.x >= coreCenter.x - halfRange
                        && occupiedCenter.x < coreCenter.x + halfRange
                        && occupiedCenter.y >= coreCenter.y - halfRange
                        && occupiedCenter.y < coreCenter.y + halfRange) return true;
                }
            }
            return false;
        }

        private bool TryGetCore(out ProcessorInstance core)
        {
            core = null;
            if (driver == null || driver.World == null) return false;
            int index = driver.World.CoreProcessorIndex;
            if (index < 0 || index >= driver.World.Processors.Count) return false;
            core = driver.World.Processors[index];
            return core != null;
        }

        private int CountActiveTowers(Dictionary<int, int> componentByNodeId, List<int> capacityByComponent)
        {
            int count = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                PowerNodeRuntime tower = nodes[i];
                if (tower.Kind != PowerNodeKind.TransmissionTower
                    || !componentByNodeId.TryGetValue(tower.Id, out int component)
                    || component < 0 || component >= capacityByComponent.Count
                    || capacityByComponent[component] <= 0
                    || !connectedNodeIds.Contains(tower.Id)) continue;
                count++;
            }
            return count;
        }

        private bool HasConnection(int nodeId)
        {
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i].FromNodeId == nodeId || connections[i].ToNodeId == nodeId) return true;
            }
            return false;
        }

        private void AccumulateMachineStatus(CellOccupantType type, int index, int demand, bool powered,
            bool countsTowardLoad,
            Vector2Int anchor, Vector2Int footprint, HashSet<string> liveKeys)
        {
            TotalMachineCount++;
            if (countsTowardLoad) RequestedPower += demand;
            if (powered)
            {
                PoweredMachineCount++;
                UsedPower += demand;
            }

            string key = IndicatorKey(type, index);
            liveKeys.Add(key);
            if (!indicators.TryGetValue(key, out GameObject indicator) || indicator == null)
            {
                // State marker for IsMachinePowered; the visible tri-color lamp is
                // owned by MachineWorldIndicator so power alone cannot look "running".
                indicator = new GameObject(key);
                indicators[key] = indicator;
            }

            indicator.name = key + (powered ? "_ON" : "_OFF");
            indicator.transform.position = GridUtility.GetFootprintCenter(anchor, footprint, 1.2f);
            // Hide a sphere retained across a Unity script reload.
            if (indicator.TryGetComponent<Renderer>(out var oldRenderer)) oldRenderer.enabled = false;
        }

        private void RemoveDeadIndicators(HashSet<string> liveKeys)
        {
            List<string> dead = null;
            foreach (var pair in indicators)
            {
                if (liveKeys.Contains(pair.Key)) continue;
                if (pair.Value != null) Destroy(pair.Value);
                (dead ??= new List<string>()).Add(pair.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) indicators.Remove(dead[i]);
        }

        private static string IndicatorKey(CellOccupantType type, int index) => $"PowerStatus_{type}_{index}";

        private static int GetPowerConsumption(string machineKey)
        {
            if (DataManager.Instance != null && DataManager.Instance.machineDict.TryGetValue(machineKey, out var data)
                && data.powerConsumption > 0)
            {
                return data.powerConsumption;
            }

            switch (machineKey)
            {
                case "Miner": return 20;
                case "Smelter": return 30;
                case "Former": return 25;
                case "Synthesizer": return 50;
                default: return 25;
            }
        }

        private static Dictionary<(CellOccupantType type, int index), Vector2Int> ScanOccupants(SimulationWorld world)
        {
            // 원점 ±64칸 스캔(범위 밖 기계는 (0,0)으로 평가됨) 대신 그리드 점유 정보를 그대로 읽는다.
            var result = new Dictionary<(CellOccupantType, int), Vector2Int>();
            world.Grid.CollectAnchorCells(result);
            return result;
        }
    }
}
