using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Choi.SaveLoad;
using Factory.Simulation;
using UnityEditor;
using UnityEngine;

// 렉 측정용 대형 테스트 공장을 통째로 깔고 세이브까지 해주는 디버그 메뉴. 플레이 모드에서만 동작한다.
//   Tools > Factory > Debug > Build Test Factory Save
//
// 기계를 하나하나 건설 도구로 놓는 대신, 세이브 데이터(FactoryProgressData)를 직접 만들어서
// 게임의 실제 로드 경로(FactorySaveBridge.RestoreStateJson)로 복원한 뒤 PowerSaveManager.Save()로
// 저장한다 — 비주얼/그리드 등록/전력 평가가 전부 실제 로드와 같은 코드로 처리된다.
// 기존 세이브 파일은 덮어쓰기 전에 같은 폴더에 백업한다.
//
// 배치(코어 = (-1,-1) 2x2, x는 동쪽, y는 북쪽):
//  - 모듈 격자(ModulesX x ModulesY): 코어 동쪽에 15x10칸 간격으로 반복. 모듈 하나 =
//      미니 코어 → 분류기 4개 체인 → 제련로 8대(위아래) → 합류기 체인 2줄 → 미니 코어로 반환
//      미니 코어 → 분류기 → 발전기 3대(석탄) + 송전탑 4개
//    모듈마다 제련 레시피를 돌려가며 써서 벨트 위 자원 종류가 섞여 보이게 한다.
//  - 코어 남쪽: 성형기 + 구리 제련로 → 2x2 합성기(기어) — 다입력 기계 확인용
//  - 채굴기: 실제 광맥(OreDepositMarker) 위, 전력 범위 밖이면 송전탑을 추가로 세워 연결
public static class TestFactoryBuilder
{
    private const string MenuPath = "Tools/Factory/Debug/Build Test Factory Save";

    // 규모 조절값. 모듈 하나 = 제련로 8, 발전기 3, 분류기 5, 합류기 8, 벨트 약 45칸.
    private const int ModulesX = 6;
    private const int ModulesY = 6;
    private const int MinersPerResource = 2;
    private const float ReportDelaySeconds = 20f;

    private static readonly string[] ModuleRecipes =
    {
        "SmeltIronIngot", "SmeltCopperIngot", "SmeltRefinedQuartz",
        "SmeltGoldIngot", "SmeltRefinedUranium", "SmeltCoalBundle",
    };

    // 코어 용량이 사실상 무제한이라, 몇 시간 돌려도 바닥나지 않게 넉넉히 넣는다.
    private static readonly Dictionary<string, int> CoreStock = new Dictionary<string, int>
    {
        { "IronOre", 10000000 },
        { "CopperOre", 10000000 },
        { "QuartzOre", 10000000 },
        { "GoldOre", 10000000 },
        { "UraniumOre", 10000000 },
        { "Coal", 10000000 },
        { "IronIngot", 10000 },
        { "Concrete", 10000 },
    };

    [MenuItem(MenuPath, true)]
    private static bool ValidateBuild() => Application.isPlaying;

    [MenuItem(MenuPath)]
    private static void Build()
    {
        var driver = UnityEngine.Object.FindAnyObjectByType<SimulationDriver>();
        var bridge = UnityEngine.Object.FindAnyObjectByType<FactorySaveBridge>();
        var saveManager = UnityEngine.Object.FindAnyObjectByType<PowerSaveManager>();
        var powerGrid = UnityEngine.Object.FindAnyObjectByType<PowerGridSystem>();
        if (driver == null || driver.World == null || bridge == null || saveManager == null || powerGrid == null)
        {
            Debug.LogError("[TestFactory] Main 씬 플레이 중에만 쓸 수 있다 (SimulationDriver/FactorySaveBridge/PowerSaveManager/PowerGridSystem 필요).");
            return;
        }

        var layout = new Layout(driver.World.Database);
        try
        {
            layout.BuildAll();
            layout.Validate();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TestFactory] 배치 생성 실패 — 아무것도 바꾸지 않았다.\n{e}");
            return;
        }

        BackupExistingSave(saveManager);
        bridge.RestoreStateJson(JsonUtility.ToJson(layout.Data));
        saveManager.Save();

        Debug.Log($"[TestFactory] 완료 — 모듈 {ModulesX}x{ModulesY}, 기계 {layout.Data.processors.Count}, " +
                  $"벨트 {layout.Data.belts.Count}칸, 채굴기 {layout.Data.miners.Count}, 전력 노드 {layout.Data.powerNodes.Count}, " +
                  $"예상 소비 {layout.ExpectedDemand} / 공급 {layout.ExpectedSupply}. 저장: {saveManager.SavePath}\n" +
                  $"{ReportDelaySeconds}초 뒤 동작 리포트를 출력한다.");
        ScheduleReport(driver, powerGrid);
    }

    private static void BackupExistingSave(PowerSaveManager saveManager)
    {
        if (!File.Exists(saveManager.SavePath)) return;
        string backup = saveManager.SavePath + ".before-testfactory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Copy(saveManager.SavePath, backup, true);
        Debug.Log($"[TestFactory] 기존 세이브 백업: {backup}");
    }

    // 복원 직후엔 전부 빈 벨트라 바로 확인하면 의미가 없다 — 잠깐 돌린 뒤 실제로 흐르는지 찍는다.
    private static void ScheduleReport(SimulationDriver driver, PowerGridSystem powerGrid)
    {
        double reportAt = EditorApplication.timeSinceStartup + ReportDelaySeconds;
        var startStock = (int[])driver.World.Processors[driver.World.CoreProcessorIndex].InputBuffer.Clone();

        void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            if (EditorApplication.timeSinceStartup < reportAt) return;
            EditorApplication.update -= Tick;
            Debug.Log(BuildReport(driver.World, powerGrid, startStock));
        }

        EditorApplication.update += Tick;
    }

    private static string BuildReport(SimulationWorld world, PowerGridSystem powerGrid, int[] startStock)
    {
        var db = world.Database;
        var sb = new StringBuilder();
        sb.AppendLine($"[TestFactory] {ReportDelaySeconds}초 후 리포트");
        sb.AppendLine($"전력: 공급 {powerGrid.AvailablePower} / 요청 {powerGrid.RequestedPower}, " +
                      $"가동 {powerGrid.PoweredMachineCount}/{powerGrid.TotalMachineCount}, 정전 {powerGrid.IsBlackout}");

        // 기계 종류별 (전체, 전력 있음, 일하는 중). 일하는 중 = 가공 중이거나 내보낼 산출물이 있음.
        var counts = new SortedDictionary<string, int[]>();
        var stalled = new List<string>();
        int emptyFuel = 0;
        for (int i = 0; i < world.Processors.Count; i++)
        {
            var p = world.Processors[i];
            if (p == null || p.UniversalPorts || p.RoutingRole != RoutingRole.None) continue;
            if (p.IsGeneratorFuelPort)
            {
                if (Sum(p.InputBuffer) == 0) emptyFuel++;
                continue;
            }
            string key = db.Machines[p.MachineId].Key;
            if (!counts.TryGetValue(key, out var c)) counts[key] = c = new int[3];
            bool working = p.IsProcessing || Sum(p.OutputBuffer) > 0;
            c[0]++;
            if (p.IsPowered) c[1]++;
            if (working) c[2]++;
            else if (stalled.Count < 15)
                stalled.Add($"#{i} {key}@{p.Anchor} 레시피 {(p.RecipeId >= 0 ? db.Recipes[p.RecipeId].Key : "없음")} 전력 {p.IsPowered} 입력 {Sum(p.InputBuffer)}");
        }
        foreach (var pair in counts)
            sb.AppendLine($"  {pair.Key}: {pair.Value[0]}대, 전력 {pair.Value[1]}, 작동 {pair.Value[2]}");
        sb.AppendLine($"  연료 빈 발전기: {emptyFuel}대");
        if (stalled.Count > 0) sb.AppendLine("  멈춘 기계(최대 15개):\n    " + string.Join("\n    ", stalled));

        int segments = 0, itemsOnBelts = 0;
        for (int i = 0; i < world.Segments.Count; i++)
        {
            if (world.Segments[i] == null) continue;
            segments++;
            itemsOnBelts += world.Segments[i].Items.Count;
        }
        sb.AppendLine($"벨트 {segments}칸, 벨트 위 아이템 {itemsOnBelts}개");

        var core = world.Processors[world.CoreProcessorIndex];
        foreach (string key in new[] { "IronIngot", "CopperIngot", "RefinedQuartz", "GoldIngot", "RefinedUranium", "CoalBundle", "IronPlate", "Gear" })
        {
            if (!db.TryGetResourceId(key, out int id)) continue;
            sb.AppendLine($"  코어 {key}: {startStock[id]} → {core.InputBuffer[id]} ({core.InputBuffer[id] - startStock[id]:+0;-0;0})");
        }
        return sb.ToString();
    }

    private static int Sum(int[] buffer)
    {
        int total = 0;
        for (int i = 0; i < buffer.Length; i++) total += buffer[i];
        return total;
    }

    private sealed class Layout
    {
        private const int CoreIndex = 0;
        private const int CoreCapacity = 99999999;
        private const int CorePower = 100;
        private const int GeneratorPower = 30;
        private const int SplittersPerModule = 4;
        private const int ModulePitchX = 15;
        private const int ModulePitchY = 10;

        public readonly FactoryProgressData Data = new FactoryProgressData();
        public int ExpectedDemand { get; private set; }
        public int ExpectedSupply { get; private set; }

        private readonly Factory.Data.GameDatabase db;
        private readonly Dictionary<Vector2Int, string> reserved = new Dictionary<Vector2Int, string>();
        private readonly List<int> towerNodeIds = new List<int>();
        private readonly List<(int belt, int source, int target)> chains = new List<(int, int, int)>();
        private int coalId;
        private int nextPowerNodeId;
        private int nextConnectionId;

        public Layout(Factory.Data.GameDatabase db)
        {
            this.db = db;
            Data.coreProcessorIndex = CoreIndex;
        }

        public void BuildAll()
        {
            if (!db.TryGetResourceId("Coal", out coalId)) throw new InvalidOperationException("Coal 자원이 없다.");

            int core = AddProcessor("Core", new Vector2Int(-1, -1), new Vector2Int(2, 2), Vector2Int.right, null);
            var coreData = Data.processors[core];
            coreData.universalPorts = true;
            coreData.capacity = CoreCapacity;
            foreach (var pair in CoreStock)
            {
                if (db.TryGetResourceId(pair.Key, out _))
                    coreData.input.Add(new ResourceStackData { resourceKey = pair.Key, amount = pair.Value });
            }

            BuildGearLine(core);

            // 모듈 격자. 모듈 로컬 범위가 x -5..8, y -2..6이라 원점을 (10, -25)부터 두면
            // 코어/남쪽 라인(x <= 1)과 겹치지 않고 카메라 시작 위치 바로 오른쪽부터 채워진다.
            int module = 0;
            for (int j = 0; j < ModulesY; j++)
            {
                for (int i = 0; i < ModulesX; i++)
                {
                    var origin = new Vector2Int(10 + i * ModulePitchX, -25 + j * ModulePitchY);
                    BuildModule(origin, ModuleRecipes[module % ModuleRecipes.Length]);
                    module++;
                }
            }

            PlaceMiners();

            int modules = ModulesX * ModulesY;
            ExpectedDemand = modules * SplittersPerModule * 2 * Power("Smelter")
                + Power("Smelter") + Power("Former") + Power("Synthesizer")
                + Data.miners.Count * Power("Miner");
            ExpectedSupply = CorePower + modules * 3 * GeneratorPower;
        }

        // 남쪽: 코어 → 성형기(0,-4) / 구리 제련로(-1,-4) → 합성기(-1,-7) 2x2 → (0,-8)→(1,-8)→(1,-1)→코어.
        // 전부 코어 전력 범위(12x12) 안이라 송전탑이 필요 없다.
        private void BuildGearLine(int core)
        {
            int copper = AddProcessor("Smelter", new Vector2Int(-1, -4), Vector2Int.one, Vector2Int.down, "SmeltCopperIngot");
            int former = AddProcessor("Former", new Vector2Int(0, -4), Vector2Int.one, Vector2Int.down, "FormIronIngot");
            int synth = AddProcessor("Synthesizer", new Vector2Int(-1, -7), new Vector2Int(2, 2), Vector2Int.down, "SynthesizGear");

            AddBelt(Path(V(0, -2), V(0, -3)), core, former, V(0, -1), V(0, -4));
            AddBelt(Path(V(-1, -2), V(-1, -3)), core, copper, V(-1, -1), V(-1, -4));
            // 합성기로 들어가는 두 라인은 담당 자원을 미리 잠가둔다 — 잠금은 "받는 쪽 레시피 재료 중
            // 아직 아무도 안 맡은 것"으로 정해지고 보내는 기계가 뭘 만드는지는 안 보기 때문에,
            // 구리 라인이 먼저 철판을 맡아버리면 둘 다 영영 못 보낸다. 같은 이유로 성형기 라인을 먼저 만든다
            // (정전 등으로 재잠금이 일어나도 세그먼트 순서상 성형기 라인이 첫 재료인 철판을 먼저 가져간다).
            AddBelt(Path(V(0, -5)), former, synth, V(0, -4), V(0, -6), "IronPlate", "SynthesizGear");
            AddBelt(Path(V(-1, -5)), copper, synth, V(-1, -4), V(-1, -6), "CopperIngot", "SynthesizGear");
            AddBelt(Path(V(0, -8), V(1, -8), V(1, -1)), synth, core, V(0, -7), V(0, -1));
        }

        // 모듈 하나(o = 미니 코어 위치 기준 오프셋 원점, 미니 코어는 o+(0,2)). 로컬 좌표:
        //   y=6  합류기 줄(서쪽으로)        y=-2 합류기 줄(서쪽으로)
        //   y=4  제련로(북향)               y=0  제련로(남향)
        //   y=2  미니 코어(0) → 분류기 줄(동쪽으로, x=2,4,6,8)
        //   서쪽: 미니 코어 → 분류기(-2,2) → 발전기 (-2,4)북향 / (-2,0)남향 / (-4,2)서향
        //   송전탑: (3,4)가 모듈 전체(±7칸)를 덮고, 발전기마다 옆에 하나씩 붙여 체인으로 잇는다.
        private void BuildModule(Vector2Int o, string recipe)
        {
            Vector2Int L(int x, int y) => o + new Vector2Int(x, y);
            int n = SplittersPerModule;

            int miniCore = AddProcessor("MiniCore", L(0, 2), Vector2Int.one, Vector2Int.right, null);
            Data.processors[miniCore].universalPorts = true;
            Data.processors[miniCore].capacity = CoreCapacity;

            var splitters = new int[n + 1];
            var northSmelters = new int[n + 1];
            var southSmelters = new int[n + 1];
            var northMergers = new int[n + 1];
            var southMergers = new int[n + 1];
            for (int k = 1; k <= n; k++)
            {
                int x = 2 * k;
                splitters[k] = AddProcessor("Splitter", L(x, 2), Vector2Int.one, Vector2Int.right, null);
                northSmelters[k] = AddProcessor("Smelter", L(x, 4), Vector2Int.one, Vector2Int.up, recipe);
                southSmelters[k] = AddProcessor("Smelter", L(x, 0), Vector2Int.one, Vector2Int.down, recipe);
                northMergers[k] = AddProcessor("Merger", L(x, 6), Vector2Int.one, Vector2Int.left, null);
                southMergers[k] = AddProcessor("Merger", L(x, -2), Vector2Int.one, Vector2Int.left, null);
            }

            // 광석 줄: 미니 코어 → 분류기 체인. 분류기는 위/아래/앞 3갈래로 라운드로빈.
            AddBelt(Path(L(1, 2)), miniCore, splitters[1], L(0, 2), L(2, 2));
            for (int k = 1; k < n; k++)
                AddBelt(Path(L(2 * k + 1, 2)), splitters[k], splitters[k + 1], L(2 * k, 2), L(2 * k + 2, 2));

            for (int k = 1; k <= n; k++)
            {
                int x = 2 * k;
                AddBelt(Path(L(x, 3)), splitters[k], northSmelters[k], L(x, 2), L(x, 4));
                AddBelt(Path(L(x, 5)), northSmelters[k], northMergers[k], L(x, 4), L(x, 6));
                AddBelt(Path(L(x, 1)), splitters[k], southSmelters[k], L(x, 2), L(x, 0));
                AddBelt(Path(L(x, -1)), southSmelters[k], southMergers[k], L(x, 0), L(x, -2));
            }

            // 합류기 체인 두 줄 → 미니 코어 위/아래 면으로 반환.
            for (int k = n; k > 1; k--)
            {
                AddBelt(Path(L(2 * k - 1, 6)), northMergers[k], northMergers[k - 1], L(2 * k, 6), L(2 * k - 2, 6));
                AddBelt(Path(L(2 * k - 1, -2)), southMergers[k], southMergers[k - 1], L(2 * k, -2), L(2 * k - 2, -2));
            }
            AddBelt(Path(L(1, 6), L(0, 6), L(0, 3)), northMergers[1], miniCore, L(2, 6), L(0, 2));
            AddBelt(Path(L(1, -2), L(0, -2), L(0, 1)), southMergers[1], miniCore, L(2, -2), L(0, 2));

            // 발전소: 미니 코어 서쪽 면 → 분류기 → 발전기 3대.
            int coalSplitter = AddProcessor("Splitter", L(-2, 2), Vector2Int.one, Vector2Int.left, null);
            var (genNorth, portNorth) = AddGenerator(L(-2, 4), Vector2Int.up);
            var (genSouth, portSouth) = AddGenerator(L(-2, 0), Vector2Int.down);
            var (genWest, portWest) = AddGenerator(L(-4, 2), Vector2Int.left);
            AddBelt(Path(L(-1, 2)), miniCore, coalSplitter, L(0, 2), L(-2, 2));
            AddBelt(Path(L(-2, 3)), coalSplitter, portNorth, L(-2, 2), L(-2, 4));
            AddBelt(Path(L(-2, 1)), coalSplitter, portSouth, L(-2, 2), L(-2, 0));
            AddBelt(Path(L(-3, 2)), coalSplitter, portWest, L(-2, 2), L(-4, 2));

            // 송전탑 하나에 발전기는 하나만 직접 물릴 수 있다(PowerGridSystem.CanConnect) — 발전기마다
            // 송전탑을 하나씩 두고, 모듈 전체를 덮는 중앙 송전탑(3,4)까지 체인으로 잇는다.
            int center = AddPowerNode(PowerNodeKind.TransmissionTower, L(3, 4), Vector2Int.right);
            int towerNorth = AddPowerNode(PowerNodeKind.TransmissionTower, L(-3, 4), Vector2Int.right);
            int towerSouth = AddPowerNode(PowerNodeKind.TransmissionTower, L(-3, 0), Vector2Int.right);
            int towerWest = AddPowerNode(PowerNodeKind.TransmissionTower, L(-5, 2), Vector2Int.right);
            AddConnection(genNorth, towerNorth, Path(L(-2, 4), L(-3, 4)));
            AddConnection(genSouth, towerSouth, Path(L(-2, 0), L(-3, 0)));
            AddConnection(genWest, towerWest, Path(L(-4, 2), L(-5, 2)));
            AddConnection(towerNorth, center, Path(L(-3, 4), L(3, 4)));
            AddConnection(towerSouth, towerNorth, Path(L(-3, 0), L(-3, 4)));
            AddConnection(towerWest, towerNorth, Path(L(-5, 2), L(-5, 4), L(-3, 4)));
        }

        // 발전기 = 전력 노드 + 연료 포트(ProcessorInstance). PowerGridSystem이 노드 id로 포트를 다시 찾아 묶는다
        // (BindGeneratorFuelPort). 첫 평가 때 바로 돌도록 석탄을 가득 채우고 한 덩이는 태우는 중으로 둔다.
        private (int node, int port) AddGenerator(Vector2Int cell, Vector2Int facing)
        {
            int node = AddPowerNode(PowerNodeKind.Generator, cell, facing);
            var nodeData = Data.powerNodes[Data.powerNodes.Count - 1];
            nodeData.fuelSecondsRemaining = PowerGridSystem.CoalBurnSeconds;
            nodeData.activeFuelResourceId = coalId;

            int port = AddProcessor("Generator", cell, Vector2Int.one, facing, null);
            var portData = Data.processors[port];
            portData.generatorFuelPort = true;
            portData.ownerPowerNodeId = node;
            portData.selectedFuelKey = "Coal";
            portData.capacity = PowerGridSystem.GeneratorFuelCapacity;
            portData.input.Add(new ResourceStackData { resourceKey = "Coal", amount = PowerGridSystem.GeneratorFuelCapacity });
            return (node, port);
        }

        // 실제 광맥 마커 위, 코어에서 가까운 순으로 자원별 MinersPerResource대. 전력 범위 밖이면
        // 바로 옆 빈칸에 송전탑을 세우고 가장 가까운 송전탑에 전선으로 잇는다.
        private void PlaceMiners()
        {
            var depositCells = new Dictionary<Vector2Int, int>();
            foreach (var marker in UnityEngine.Object.FindObjectsByType<OreDepositMarker>(FindObjectsSortMode.None))
            {
                if (!db.TryGetOreDepositId(marker.depositId, out int depositId)) continue;
                foreach (var cell in marker.Cells) depositCells[cell] = depositId;
            }

            foreach (string resourceKey in new[] { "IronOre", "CopperOre", "Coal" })
            {
                if (!db.TryGetResourceId(resourceKey, out int resourceId)) continue;
                var candidates = new List<Vector2Int>();
                foreach (var pair in depositCells)
                {
                    if (db.OreDeposits[pair.Value].ResourceId == resourceId && !reserved.ContainsKey(pair.Key)) candidates.Add(pair.Key);
                }
                candidates.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));

                int placed = 0;
                for (int i = 0; i < candidates.Count && placed < MinersPerResource; i++)
                {
                    var cell = candidates[i];
                    if (reserved.ContainsKey(cell)) continue;
                    var deposit = db.OreDeposits[depositCells[cell]];
                    Reserve(cell, "Miner");
                    Data.miners.Add(new MinerProgressData
                    {
                        exists = true,
                        machineKey = "Miner",
                        outputResourceKey = resourceKey,
                        baseSpeed = 1f,
                        oreDepositKey = deposit.Key,
                        mineIntervalSeconds = deposit.MineIntervalSeconds,
                        yieldPerCycle = deposit.YieldPerCycle,
                        anchor = new Int2Data(cell.x, cell.y),
                    });
                    if (!IsPowered(cell)) PowerMiner(cell, depositCells);
                    placed++;
                }
                if (placed < MinersPerResource)
                    Debug.LogWarning($"[TestFactory] {resourceKey} 광맥이 부족해서 채굴기를 {placed}대만 놓았다.");
            }
        }

        private void PowerMiner(Vector2Int minerCell, Dictionary<Vector2Int, int> depositCells)
        {
            Vector2Int towerCell = default;
            bool found = false;
            for (int radius = 1; radius <= 4 && !found; radius++)
            {
                for (int dx = -radius; dx <= radius && !found; dx++)
                {
                    for (int dy = -radius; dy <= radius && !found; dy++)
                    {
                        var c = minerCell + new Vector2Int(dx, dy);
                        if (reserved.ContainsKey(c) || depositCells.ContainsKey(c)) continue;
                        towerCell = c;
                        found = true;
                    }
                }
            }
            if (!found || towerNodeIds.Count == 0)
            {
                Debug.LogWarning($"[TestFactory] {minerCell} 채굴기 옆에 송전탑 자리가 없어서 전력 없이 둔다.");
                return;
            }

            int nearest = towerNodeIds[0];
            int best = int.MaxValue;
            foreach (int id in towerNodeIds)
            {
                var d = NodeCell(id) - towerCell;
                int dist = Mathf.Abs(d.x) + Mathf.Abs(d.y);
                if (dist < best) { best = dist; nearest = id; }
            }

            Vector2Int from = NodeCell(nearest);
            int tower = AddPowerNode(PowerNodeKind.TransmissionTower, towerCell, Vector2Int.right);
            AddConnection(nearest, tower, Path(from, new Vector2Int(towerCell.x, from.y), towerCell));
        }

        // PowerGridSystem.IsInCorePowerRange / FindSupplyingTowerComponent와 같은 규칙.
        private bool IsPowered(Vector2Int cell)
        {
            float cx = cell.x + 0.5f, cy = cell.y + 0.5f;
            float half = PowerGridSystem.CoreRangeSize * 0.5f;
            if (cx >= -half && cx < half && cy >= -half && cy < half) return true;
            foreach (int id in towerNodeIds)
            {
                var d = cell - NodeCell(id);
                if (Mathf.Abs(d.x) <= 7 && Mathf.Abs(d.y) <= 7) return true;
            }
            return false;
        }

        private int Power(string machineKey)
        {
            return Bae.Data.DataManager.Instance != null
                && Bae.Data.DataManager.Instance.machineDict.TryGetValue(machineKey, out var data)
                ? data.powerConsumption : 25;
        }

        private int AddProcessor(string machineKey, Vector2Int anchor, Vector2Int footprint, Vector2Int facing, string recipeKey)
        {
            if (!db.TryGetMachineId(machineKey, out _)) throw new InvalidOperationException($"기계 데이터에 {machineKey}가 없다.");
            if (recipeKey != null && !db.TryGetRecipeId(recipeKey, out _)) throw new InvalidOperationException($"레시피 {recipeKey}가 없다.");
            foreach (var cell in GridUtility.GetFootprintCells(anchor, footprint)) Reserve(cell, machineKey);

            Data.processors.Add(new ProcessorProgressData
            {
                exists = true,
                machineKey = machineKey,
                recipeKey = recipeKey ?? string.Empty,
                activeRecipeKey = string.Empty,
                baseSpeed = 1f,
                facing = new Int2Data(facing.x, facing.y),
                anchor = new Int2Data(anchor.x, anchor.y),
                footprint = new Int2Data(footprint.x, footprint.y),
                capacity = SimulationConstants.ResourceBufferCapacity,
                selectedFuelKey = string.Empty,
            });
            return Data.processors.Count - 1;
        }

        // cells를 한 칸짜리 세그먼트들로 쪼개 체인으로 잇는다. prevCell/nextCell은 체인 앞뒤에 붙은
        // 기계 칸 — 벨트 스트립을 칸 경계 중점끼리 잇는 데만 쓴다(코너는 시작/끝 축이 달라서 자동 판정됨).
        private void AddBelt(List<Vector2Int> cells, int source, int target, Vector2Int prevCell, Vector2Int nextCell,
            string lockedResource = null, string lockedRecipe = null)
        {
            int first = Data.belts.Count;
            for (int i = 0; i < cells.Count; i++)
            {
                Reserve(cells[i], "Belt");
                Vector2Int before = i == 0 ? prevCell : cells[i - 1];
                Vector2Int after = i == cells.Count - 1 ? nextCell : cells[i + 1];
                bool last = i == cells.Count - 1;
                var belt = new BeltProgressData
                {
                    exists = true,
                    id = first + i,
                    hasNext = !last,
                    nextId = last ? -1 : first + i + 1,
                    length = 1f,
                    speed = SimulationConstants.DefaultBeltSpeed,
                    itemSpacing = SimulationConstants.DefaultItemSpacing,
                    hasSourceProcessor = i == 0,
                    sourceProcessorIndex = i == 0 ? source : -1,
                    hasTargetProcessor = last,
                    targetProcessorIndex = last ? target : -1,
                    lockedResourceKey = string.Empty,
                    lockedRecipeKey = string.Empty,
                    cell = new Int2Data(cells[i].x, cells[i].y),
                    start = Edge(before, cells[i]),
                    end = Edge(cells[i], after),
                    concreteCost = 3,
                    refundMachineKey = string.Empty,
                };
                if (i == 0 && lockedResource != null)
                {
                    belt.hasLockedResource = true;
                    belt.lockedResourceKey = lockedResource;
                    belt.lockedRecipeKey = lockedRecipe;
                }
                Data.belts.Add(belt);
            }
            chains.Add((first, source, target));
        }

        private int AddPowerNode(PowerNodeKind kind, Vector2Int cell, Vector2Int facing)
        {
            // 발전기 칸은 연료 포트(AddProcessor)가 예약한다. 송전탑은 그리드에 안 올라가지만 겹치면 안 보이니 예약.
            if (kind == PowerNodeKind.TransmissionTower) Reserve(cell, "Tower");
            int id = nextPowerNodeId++;
            Data.powerNodes.Add(new PowerNodeData
            {
                id = id,
                kind = (int)kind,
                cell = new Int2Data(cell.x, cell.y),
                facing = new Int2Data(facing.x, facing.y),
            });
            if (kind == PowerNodeKind.TransmissionTower) towerNodeIds.Add(id);
            return id;
        }

        private void AddConnection(int from, int to, List<Vector2Int> path)
        {
            var saved = new PowerConnectionData { id = nextConnectionId++, fromNodeId = from, toNodeId = to };
            foreach (var cell in path) saved.path.Add(new Int2Data(cell.x, cell.y));
            Data.powerConnections.Add(saved);
        }

        private Vector2Int NodeCell(int nodeId)
        {
            foreach (var node in Data.powerNodes)
            {
                if (node.id == nodeId) return new Vector2Int(node.cell.x, node.cell.y);
            }
            throw new InvalidOperationException($"전력 노드 {nodeId}가 없다.");
        }

        private void Reserve(Vector2Int cell, string what)
        {
            if (reserved.TryGetValue(cell, out string existing))
                throw new InvalidOperationException($"{cell} 칸 겹침: {existing} / {what}");
            reserved[cell] = what;
        }

        // 저장 전에 벨트 양 끝이 게임의 포트 규칙(BeltDragTool.ResolveEndpointRole)과 맞는지 확인한다.
        // 규칙과 어긋난 배치는 시뮬레이션은 돌아도 나중에 플레이어가 그 자리를 다시 이을 때 어긋난다.
        public void Validate()
        {
            var errors = new StringBuilder();
            foreach (var (firstBelt, source, target) in chains)
            {
                int lastBelt = firstBelt;
                while (Data.belts[lastBelt].hasNext) lastBelt = Data.belts[lastBelt].nextId;
                var firstCell = ToVector(Data.belts[firstBelt].cell);
                var lastCell = ToVector(Data.belts[lastBelt].cell);
                if (!IsPort(source, firstCell, output: true))
                    errors.AppendLine($"벨트 {firstBelt} 시작 {firstCell}이 {Data.processors[source].machineKey}#{source}의 출력 포트가 아니다.");
                if (!IsPort(target, lastCell, output: false))
                    errors.AppendLine($"벨트 {lastBelt} 끝 {lastCell}이 {Data.processors[target].machineKey}#{target}의 입력 포트가 아니다.");
            }
            if (ExpectedDemand > ExpectedSupply)
                errors.AppendLine($"전력 부족: 소비 {ExpectedDemand} > 공급 {ExpectedSupply}");
            if (errors.Length > 0) throw new InvalidOperationException(errors.ToString());
        }

        private bool IsPort(int processorIndex, Vector2Int beltCell, bool output)
        {
            var p = Data.processors[processorIndex];
            var anchor = ToVector(p.anchor);
            var footprint = ToVector(p.footprint);
            var facing = ToVector(p.facing);

            bool adjacent = false;
            foreach (var cell in GridUtility.GetFootprintCells(anchor, footprint))
            {
                var d = beltCell - cell;
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) == 1) adjacent = true;
            }
            if (!adjacent) return false;
            if (p.universalPorts) return true;

            var role = RoutingRoles.For(p.machineKey);
            if (role != RoutingRole.None)
            {
                var dir = beltCell - anchor;
                bool inputFace = role == RoutingRole.Splitter ? dir == -facing : dir != facing;
                return output ? !inputFace : inputFace;
            }
            return GridUtility.GetPortCells(anchor, footprint, facing, output).Contains(beltCell);
        }

        private static Vector3Data Edge(Vector2Int a, Vector2Int b)
        {
            var p = (GridUtility.CellToWorldCenter(a, 0.745f) + GridUtility.CellToWorldCenter(b, 0.745f)) * 0.5f;
            return new Vector3Data(p.x, p.y, p.z);
        }

        private static Vector2Int ToVector(Int2Data v) => new Vector2Int(v.x, v.y);

        private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 꼭짓점들을 직선으로 이은 칸 목록(꼭짓점 포함, 중복 없음). 꼭짓점끼리는 가로나 세로로만 이어져야 한다.
        private static List<Vector2Int> Path(params Vector2Int[] points)
        {
            var result = new List<Vector2Int> { points[0] };
            for (int i = 1; i < points.Length; i++)
            {
                var from = points[i - 1];
                var to = points[i];
                if (from.x != to.x && from.y != to.y) throw new ArgumentException($"{from}→{to}는 직선이 아니다.");
                var step = new Vector2Int(Math.Sign(to.x - from.x), Math.Sign(to.y - from.y));
                for (var c = from + step; c != to + step; c += step) result.Add(c);
            }
            return result;
        }
    }
}
