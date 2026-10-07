using System.Collections.Generic;
using Bae.Data;
using Factory.Building;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

// BeltDragTool/MachineGhostTool을 실제 화면 좌표(오소그래픽 탑다운 카메라 기준)로 조작해서
// 사용자가 보고한 시나리오(벨트에 붙여서 기계 설치, 떨어뜨려 설치 후 벨트로 연결, 벨트를
// 따로따로 그려서 잇기)를 그대로 재현하고 실제로 아이템이 끝까지 도착하는지 검증한다.
//
// 채굴기는 입출력 포트가 없다(원격 전송, MinerSystem 참고) — 그래서 벨트 체인 자체를
// 검증하는 테스트들은 "항상 존재하는 저장고"인 코어(UniversalPorts Processor)를 소스로 쓴다.
//
// 기계/자원/레시피 데이터는 Bae님의 데이터 클래스(ItemData/MachineData/RecipeData, 평범한
// C# 클래스)로 직접 구성한다 — ScriptableObject가 아니라서 CreateInstance/DestroyImmediate가
// 필요 없다.
public class BuildToolsIntegrationTests
{
    private static readonly Vector2 GhostScreenOffset = new Vector2(0f, 150f);

    // 채굴기는 MachineGhostTool이 실제 machineID "Miner"로만 알아본다(광맥 확인/원격 전송) —
    // 다른 이름을 쓰면 일반 기계로 지어져서 채굴 관련 테스트가 엉뚱하게 깨진다.
    private const string MinerMachineId = "Miner";
    private const string ProcessorMachineId = "TestProcessor";
    private const string AssemblerMachineId = "TestAssembler";

    private GameObject cameraGO;
    private GameObject driverGO;
    private GameObject toolsGO;
    private Camera cam;
    private SimulationDriver driver;
    private BeltDragTool beltTool;
    private MachineGhostTool machineTool;

    private OreDepositDef oreDepositDef;

    [SetUp]
    public void SetUp()
    {
        var oreItem = new ItemData { itemID = "TestOre" };
        var outputItem = new ItemData { itemID = "TestOutput" };
        var gearItem = new ItemData { itemID = "TestGear" };

        var minerMachine = new MachineData { machineID = MinerMachineId, gridWidth = 1, gridHeight = 1 };
        var processorMachine = new MachineData { machineID = ProcessorMachineId, gridWidth = 1, gridHeight = 1 };
        // 조립기는 2x2에 입력 포트 2칸/출력 포트 2칸 — 서로 다른 두 자원을 각각 다른 벨트로 받는다.
        var assemblerMachine = new MachineData { machineID = AssemblerMachineId, gridWidth = 2, gridHeight = 2 };

        // 채굴기는 이제 하나뿐이고, 뭘 캐는지는 땅 위 광물 노드가 정한다(PlaceOreDeposit 참고).
        oreDepositDef = ScriptableObject.CreateInstance<OreDepositDef>();
        oreDepositDef.depositId = "TestOreDeposit";
        oreDepositDef.resourceId = "TestOre";

        // 출력을 둬서 "누적 생산량"으로 아이템이 끝까지 도착했는지 명확하게 판정한다
        // (InputBuffer는 소비되는 순간 0으로 돌아가서 타이밍에 따라 애매해짐).
        var recipe = new RecipeData
        {
            recipeID = "TestRecipe",
            machineID = ProcessorMachineId,
            timeToCraft = 0.1f,
            inputItems = new List<string> { "TestOre" },
            outputItems = new List<string> { "TestOutput" },
        };

        // 3단계 트리(광석 -> 철판 역할의 TestOutput -> 기어) 검증용 레시피 — 조립기답게
        // 서로 다른 두 자원(제련로 출력 TestOutput + 광석 TestOre 직접)을 동시에 소비한다.
        var gearRecipe = new RecipeData
        {
            recipeID = "TestGearRecipe",
            machineID = AssemblerMachineId,
            timeToCraft = 0.1f,
            inputItems = new List<string> { "TestOutput", "TestOre" },
            outputItems = new List<string> { "TestGear" },
        };

        var db = GameDatabase.Build(
            new[] { oreItem, outputItem, gearItem },
            new[] { minerMachine, processorMachine, assemblerMachine },
            new[] { recipe, gearRecipe },
            new[] { oreDepositDef });

        cameraGO = new GameObject("TestCamera");
        cam = cameraGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 8f;
        cameraGO.transform.position = new Vector3(0f, 15f, 0f);
        cameraGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        driverGO = new GameObject("TestDriver");
        driver = driverGO.AddComponent<SimulationDriver>();
        driver.Initialize(db); // DataManager/JSON 없이 이 테스트가 직접 구성한 db를 그대로 씀.

        toolsGO = new GameObject("TestTools");
        beltTool = toolsGO.AddComponent<BeltDragTool>();
        beltTool.Initialize(cam, driver);
        machineTool = toolsGO.AddComponent<MachineGhostTool>();
        machineTool.Initialize(cam, driver);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(cameraGO);
        Object.DestroyImmediate(driverGO);
        Object.DestroyImmediate(toolsGO);
        Object.DestroyImmediate(oreDepositDef);

        // 벨트 비주얼 루트는 씬 최상위에 만들어져서 위 오브젝트들과 같이 안 지워진다 — 남겨두면 다음
        // 테스트에서 같은 이름(Belt_N)을 가진 옛 오브젝트가 섞여 이름 기반 확인이 엉뚱하게 깨진다.
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go != null && go.transform.parent == null && go.name.StartsWith("Belt_")) Object.DestroyImmediate(go);
        }
    }

    private Vector2 ScreenPosForCell(Vector2Int cell)
    {
        return cam.WorldToScreenPoint(GridUtility.CellToWorldCenter(cell, 0.5f));
    }

    private void PlaceMachine(string machineId, Vector2Int cell)
    {
        // 채굴기는 이제 광물 노드가 있는 칸에만 지을 수 있다 — 테스트에서 매번 따로 챙기는
        // 대신, 채굴기를 놓을 때 그 칸에 자동으로 노드를 깔아준다(테스트 의도는 "채굴기가
        // 정상 동작하는가"이지 "노드 배치 자체"가 아니므로).
        if (machineId == MinerMachineId)
        {
            PlaceOreDeposit(cell);
        }

        machineTool.SelectMachine(machineId);
        machineTool.OnPressBegin(ScreenPosForCell(cell) - GhostScreenOffset);
        bool confirmed = machineTool.Confirm();
        Assert.IsTrue(confirmed, $"{machineId} 배치가 {cell}에서 실패함");
    }

    private void PlaceOreDeposit(Vector2Int cell)
    {
        int depositId = driver.World.Database.GetOreDepositId("TestOreDeposit");
        driver.World.Grid.RegisterOreDeposit(cell, depositId);
    }

    // 레시피는 이제 배치 시 자동 배정되지 않고 탭-선택 UI로 고른다 (RecipeSelectionPanel).
    // 이 테스트들은 벨트 연결 자체를 검증하는 거라 UI 클릭 대신 직접 대입해서 "플레이어가
    // 방금 고른 것"을 흉내낸다.
    private void AssignRecipeToNewestProcessor()
    {
        AssignRecipe(driver.World.Processors.Count - 1, "TestRecipe");
    }

    private void AssignRecipe(int processorIndex, string recipeKey)
    {
        int recipeId = driver.World.Database.GetRecipeId(recipeKey);
        driver.World.Processors[processorIndex].RecipeId = recipeId;
    }

    // CoreSpawner와 동일한 방식으로(RecipeId=-1, UniversalPorts=true) 코어를 흉내낸 Processor를
    // 등록한다. 채굴기가 원격 전송으로 넣어주는 대상이자, 벨트 체인 테스트의 "항상 있는 소스".
    private ProcessorInstance PlaceCoreLike(Vector2Int anchor, Vector2Int footprint)
    {
        var cells = GridUtility.GetFootprintCells(anchor, footprint);
        var core = new ProcessorInstance(driver.World.Database.ResourceCount) { MachineId = -1, RecipeId = -1, UniversalPorts = true };
        int index = driver.World.AddProcessor(core);
        driver.World.CoreProcessorIndex = index;
        driver.World.Grid.RegisterBuildingFootprint(cells, CellOccupantType.Processor, index);
        return core;
    }

    // 벨트는 기계 칸에서 시작/통과할 수 없다(BeltDragTool.IsMachineCell) — 예전 시나리오처럼
    // 기계 칸을 양 끝으로 넘겨도 실제 사용자 조작과 같게 기계 옆 칸부터 시작/끝나게 잘라낸다.
    private void DragBelt(Vector2Int from, Vector2Int to)
    {
        var raw = BeltPathBuilder.BuildOrthogonalPath(new[] { from, to });
        while (raw.Count > 0 && IsMachineCell(raw[0])) raw.RemoveAt(0);
        while (raw.Count > 0 && IsMachineCell(raw[raw.Count - 1])) raw.RemoveAt(raw.Count - 1);
        if (raw.Count == 0) return;

        from = raw[0];
        to = raw[raw.Count - 1];
        beltTool.OnPressBegin(ScreenPosForCell(from));
        beltTool.OnDrag(ScreenPosForCell(to));
        beltTool.OnReleased(ScreenPosForCell(to));
    }

    private bool IsMachineCell(Vector2Int cell) =>
        driver.World.Grid.TryGetOccupant(cell, out var occupant) && occupant.Type != CellOccupantType.Belt;

    private void RunTicks(int count, float delta = 0.05f)
    {
        for (int i = 0; i < count; i++) driver.World.Tick(delta);
    }

    [Test]
    public void ReturnBeltVisual_PoolsAndReusesTheSameGameObject_WhenANewBeltIsPlaced()
    {
        // 사용자 지적: "풀만 하고 꺼내서 쓰지는 않는 거 아니냐" — 벨트를 철거하면 GameObject를
        // 비활성화해서 풀에 넣어두기만 하고, 새 벨트를 놓을 때 정말로 그 오브젝트를 재사용하는지
        // (그냥 계속 새로 만들기만 하는 건 아닌지) 인스턴스 identity로 직접 확인한다.
        // 빈 땅에서 시작하는 벨트는 이제 설치가 거부되므로(시작점이 출력원이어야 함), 양쪽 벨트
        // 모두 코어 옆에서 시작시킨다.
        PlaceCoreLike(new Vector2Int(-1, 0), Vector2Int.one);
        PlaceCoreLike(new Vector2Int(4, 0), Vector2Int.one);
        // 오브젝트는 GameObject.Find 대신 도구의 레지스트리(TryGetBeltVisual)로 찾는다 — Find는 비활성
        // (풀에 반납된) 오브젝트를 못 찾고, 이름이 같은 다른 오브젝트를 잘못 집을 수도 있다.
        int countBefore = driver.World.Segments.Count;
        DragBelt(new Vector2Int(0, 0), new Vector2Int(1, 0));
        Assert.Greater(driver.World.Segments.Count, countBefore, "코어 옆에서 시작한 벨트는 설치되어야 함");
        int firstId = driver.World.Segments[driver.World.Segments.Count - 1].Id;
        Assert.IsTrue(beltTool.TryGetBeltVisual(firstId, out var firstVisual), "벨트를 놓으면 비주얼이 등록되어야 함");
        int pooledInstanceId = firstVisual.GetInstanceID();

        // DemolishTool.Confirm이 벨트 철거 시 실제로 부르는 것과 같은 호출.
        beltTool.ReturnBeltVisual(firstId);
        driver.World.RemoveSegment(firstId);

        Assert.IsFalse(beltTool.TryGetBeltVisual(firstId, out _), "반납된 벨트는 레지스트리에서 빠져야 함");
        Assert.IsFalse(firstVisual.activeSelf, "반납된 벨트는 비활성 상태로 풀에서 대기해야 함");
        Assert.AreEqual("Belt_Pooled", firstVisual.name);

        int secondStart = driver.World.Segments.Count;
        DragBelt(new Vector2Int(5, 0), new Vector2Int(6, 0)); // 전혀 다른 위치에 새 벨트.
        Assert.Greater(driver.World.Segments.Count, secondStart, "두 번째 벨트도 설치되어야 함");

        // 새 드래그로 생긴 세그먼트 중 하나가 풀에 있던 바로 그 오브젝트를 재사용해야 한다.
        bool reused = false;
        for (int id = secondStart; id < driver.World.Segments.Count; id++)
        {
            if (beltTool.TryGetBeltVisual(id, out var visual) && visual.GetInstanceID() == pooledInstanceId) reused = true;
        }
        Assert.IsTrue(reused, "새 벨트가 풀에 반납된 오브젝트를 재사용해야 함(새로 또 만들면 안 됨)");
        Assert.IsTrue(firstVisual.activeSelf, "재사용된 오브젝트는 다시 활성화되어야 함");
    }

    [Test]
    public void PlacingMachine_AdjacentToExistingDeadEndBelt_AutoConnects()
    {
        // "벨트를 먼저 뻗어두고 나중에 옆에 기계를 놓는" 시나리오. 소스는 코어(항상 존재하는
        // 저장고)로 — 채굴기는 벨트에 연결될 수 없다(원격 전송).
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;

        DragBelt(new Vector2Int(0, 0), new Vector2Int(2, 0)); // 코어 옆에서 (2,0)까지 뻗어놓고 방치(막다른 길)

        PlaceMachine(ProcessorMachineId, new Vector2Int(3, 0)); // 벨트 끝(2,0) 바로 옆에 나중에 배치
        AssignRecipeToNewestProcessor();

        RunTicks(400); // 20초 분량

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "코어 옆에서 뻗은 벨트가 나중에 놓인 제련로까지 실제로 아이템을 날라야 함");
    }

    [Test]
    public void PlacingAssembler_AdjacentToExistingDeadEndBelt_AutoConnects()
    {
        // 위 테스트와 같은 시나리오를 2x2 조립기(입력 포트 2칸)에 대해서도 검증한다 —
        // 사용자가 보고한 "미리 그려둔 벨트 모양에 맞게 조립기를 놓아도 연결이 안 된다"는
        // 문제가 실제로 재현되는지 확인.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;

        // 조립기를 (6,0)에 놓을 계획 -> Facing 기본값(1,0)이면 입력 포트는 (5,0),(5,1).
        DragBelt(new Vector2Int(0, 0), new Vector2Int(5, 0)); // 입력 포트 1(철판 자리) 미리 뻗어둠

        PlaceMachine(AssemblerMachineId, new Vector2Int(6, 0));

        var lastSegment = driver.World.Segments[driver.World.Segments.Count - 1];
        int assemblerIndex = driver.World.Processors.Count - 1;
        Assert.AreEqual(assemblerIndex, lastSegment.TargetProcessorId,
            "미리 뻗어둔 막다른 벨트가 나중에 놓인 조립기의 입력 포트에 자동으로 연결되어야 함");
    }

    [Test]
    public void ConnectingTwoSeparatelyDrawnBeltSegments_ItemsFlowThroughJoint()
    {
        // "컨베이어끼리 따로 그려서 연결" 시나리오: 한 번에 쭉 드래그하지 않고 두 번에 나눠서 그림.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;

        PlaceMachine(ProcessorMachineId, new Vector2Int(6, 0));
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0)); // 1차: 코어 -> 중간까지
        DragBelt(new Vector2Int(3, 0), new Vector2Int(6, 0)); // 2차: 이어서 -> 제련로까지 (별도 드래그)

        RunTicks(600); // 30초 분량

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "따로 그린 두 벨트 구간의 연결부에서 아이템이 막히지 않고 제련로까지 도착해야 함");
    }

    [Test]
    public void PlacingMachine_WithGap_ThenBridgingBelt_ConnectsAndTransports()
    {
        // 기계를 벨트와 떨어뜨려 설치한 뒤, 그 사이를 잇는 벨트를 그리는 시나리오.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;

        PlaceMachine(ProcessorMachineId, new Vector2Int(5, 0)); // 미리 좀 떨어진 곳에 배치
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(5, 0)); // 그 사이를 잇는 벨트

        RunTicks(500);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "떨어뜨려 설치한 기계 사이를 잇는 벨트로 아이템이 도착해야 함");
    }

    [Test]
    public void DraggingIntoMachineSideFace_DoesNotConnect()
    {
        // 고정 포트 회귀 테스트: 기본 Facing (1,0)인 제련로는 입력이 (-1,0) 방향, 출력이
        // (1,0) 방향뿐이다. 측면((0,1) 방향)에서 드래그해 붙여도 연결되면 안 된다.
        PlaceMachine(ProcessorMachineId, new Vector2Int(0, 0));

        DragBelt(new Vector2Int(0, 3), new Vector2Int(0, 0)); // 북쪽(측면)에서 기계 칸으로 바로 드래그

        Assert.AreEqual(0, driver.World.Segments.Count, "측면에는 포트가 없으니 아무 세그먼트도 만들어지면 안 됨");
    }

    [Test]
    public void PlacingBeltOntoMiner_FromAnySide_NeverConnects()
    {
        // 채굴기는 입출력 포트가 없다(원격 전송) — 어느 면에서 드래그해도 벨트가 연결되면 안 된다.
        PlaceMachine(MinerMachineId, new Vector2Int(0, 0));

        DragBelt(new Vector2Int(0, 0), new Vector2Int(1, 0)); // 동쪽으로
        DragBelt(new Vector2Int(0, 0), new Vector2Int(-1, 0)); // 서쪽으로
        DragBelt(new Vector2Int(0, 0), new Vector2Int(0, 1)); // 북쪽으로
        DragBelt(new Vector2Int(-1, 0), new Vector2Int(0, 0)); // 반대 방향(채굴기로 들어오는 드래그)

        Assert.AreEqual(0, driver.World.Segments.Count, "채굴기는 어느 면으로 드래그해도 벨트가 연결되면 안 됨");
    }

    [Test]
    public void PlacingMiner_WithoutOreDepositUnderneath_IsRejected()
    {
        // 채굴기는 이제 하나뿐이고, 광물 노드가 있는 땅에만 지을 수 있다 — 아무것도 없는
        // 빈 땅에는 배치 자체가 거부되어야 한다(PlaceMachine 헬퍼가 자동으로 깔아주는 노드
        // 없이, 직접 확인/배치를 호출해서 검증).
        machineTool.SelectMachine(MinerMachineId);
        machineTool.OnPressBegin(ScreenPosForCell(new Vector2Int(9, 9)) - GhostScreenOffset);
        bool confirmed = machineTool.Confirm();

        Assert.IsFalse(confirmed, "광물 노드가 없는 칸에는 채굴기를 배치할 수 없어야 함");
        Assert.AreEqual(0, driver.World.Miners.Count);
    }

    [Test]
    public void PlacingMiner_OnOreDeposit_InheritsResourceAndYield()
    {
        // 채굴기가 광물 노드의 자원/속도/산출량을 그대로 물려받는지 확인한다.
        int depositId = driver.World.Database.GetOreDepositId("TestOreDeposit");
        var deposit = driver.World.Database.OreDeposits[depositId];

        PlaceMachine(MinerMachineId, new Vector2Int(2, 2)); // PlaceMachine이 그 칸에 TestOreDeposit을 자동으로 깔아줌

        var miner = driver.World.Miners[0];
        Assert.AreEqual(deposit.ResourceId, miner.OutputResourceId, "채굴기가 노드의 자원을 그대로 물려받아야 함");
        Assert.AreEqual(deposit.YieldPerCycle, miner.YieldPerCycle, "채굴기가 노드의 사이클당 산출량을 그대로 물려받아야 함");
    }

    [Test]
    public void PlacingMachine_OnTopOfExistingBeltCell_IsRejected()
    {
        // 사용자가 보고한 버그: 이미 벨트가 깔린 칸에 제련로를 그냥 겹쳐 놓을 수 있으면 안 된다.
        // 소스는 코어로 — 채굴기는 포트가 없어서(원격 전송) 벨트 소스가 될 수 없다.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 10;
        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0)); // (1,0),(2,0),(3,0)에 벨트가 깔림
        Assert.AreEqual(3, driver.World.Segments.Count, "사전 조건: 벨트 3칸이 실제로 깔려있어야 함");

        machineTool.SelectMachine(ProcessorMachineId);
        machineTool.OnPressBegin(ScreenPosForCell(new Vector2Int(2, 0)) - GhostScreenOffset); // 벨트 칸 위
        bool confirmed = machineTool.Confirm();

        Assert.IsFalse(confirmed, "이미 벨트가 있는 칸에는 기계를 배치할 수 없어야 함");
        Assert.AreEqual(CellOccupantType.Belt, GetOccupantTypeAt(new Vector2Int(2, 0)),
            "그 칸은 여전히 벨트여야 함(제련로로 덮어씌워지면 안 됨)");
    }

    private CellOccupantType GetOccupantTypeAt(Vector2Int cell)
    {
        driver.World.Grid.TryGetOccupant(cell, out var occupant);
        return occupant.Type;
    }

    [Test]
    public void DraggingFromAlreadyConnectedBeltCell_DoesNotHijackItsExistingConnection()
    {
        // 사용자가 보고한 버그 원인: 이미 다른 곳으로 흐르고 있는 벨트 칸을 새 드래그의 시작점
        // 으로 삼으면, 예전엔 그 벨트의 NextSegmentId를 조용히 새 목적지로 덮어써서 원래
        // 목적지와의 연결이 몰래 끊기고 두 벨트가 뜻하지 않게 하나로 합쳐졌다. 지금은 거부되고
        // 원래 체인이 그대로 살아있어야 한다.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;
        PlaceMachine(ProcessorMachineId, new Vector2Int(5, 0));
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(5, 0)); // 코어 -> 제련로, (1,0)~(4,0) 4칸
        Assert.AreEqual(4, driver.World.Segments.Count, "사전 조건: 4칸짜리 체인이 만들어져 있어야 함");

        // 이미 체인 중간인 (2,0)을 시작점 삼아 완전히 다른 방향으로 드래그 시도 — 가로채기 시도.
        DragBelt(new Vector2Int(2, 0), new Vector2Int(2, 3));

        Assert.AreEqual(4, driver.World.Segments.Count, "가로채기 시도는 거부되어 세그먼트가 추가로 생기면 안 됨");

        RunTicks(400);
        int outputId = driver.World.Database.GetResourceId("TestOutput");
        Assert.Greater(driver.World.Processors[driver.World.Processors.Count - 1].OutputBuffer[outputId], 0,
            "가로채기 시도 이후에도 원래 체인(코어->제련로)은 그대로 살아서 동작해야 함");
    }

    [Test]
    public void FullLoop_MinerDeliversToCore_BeltCarriesToProcessor_NoMinerBelt()
    {
        // 실제 플레이 순서 그대로: 채굴기를 놓으면(벨트 없이) 자동으로 코어에 쌓이고, 코어에서
        // 뻗은 벨트가 제련로까지 실어 날라서 최종 산출물이 나와야 한다.
        PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        PlaceMachine(MinerMachineId, new Vector2Int(4, 4)); // 코어와 멀리 떨어진 곳 — 벨트로 안 이어도 됨
        PlaceMachine(ProcessorMachineId, new Vector2Int(3, 0));
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0));

        RunTicks(1000); // 채굴 + 원격 전송 + 벨트 이동 + 제련까지 충분한 시간

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "채굴 -> 코어 원격 전송 -> 벨트 -> 제련까지 전체 루프가 실제로 동작해야 함");
    }

    [Test]
    public void ThreeStageChain_MinerToCoreToSmelterAndAssembler_TwoDifferentInputsProduceGear()
    {
        // 미션 스펙의 예시 생산 트리(광석 -> 철판 -> 기어)를 그대로 재현하되, 조립기는 진짜
        // "조립"답게 서로 다른 두 자원을 서로 다른 벨트로 동시에 받는다:
        // 채굴기(원격 전송) -> 코어 -> (A) 벨트 -> 제련로 -> 벨트 -> 조립기 입력 포트 1(철판)
        //                        -> (B) 벨트 -----------------------> 조립기 입력 포트 2(광석 직접)
        // 코어를 세로로 2칸(footprint 1x2)으로 둬서 두 벨트가 각자 다른 줄에서 직선으로
        // 출발할 수 있게 한다(대각선 자동 라우팅이 조립기 자신의 다른 footprint 칸을 가로질러
        // 지나가면서 막히는 걸 피하기 위함).
        // 코어에 넉넉히 재고를 미리 쌓아둔다 — 채굴기 하나가 원격 전송으로 공급하는 속도만
        // 믿으면, 제련로 체인이 벨트 처리량만큼 항상 굶주려서(0.1초마다 1개 소비, 벨트가
        // 감당 가능한 최대치) 광석을 먼저 다 채가는 바람에 조립기 직행 벨트가 만성적으로
        // 굶는 경합이 생긴다(실제로 재현해서 확인함) — 재고를 넉넉히 두면 그 경합 없이
        // "서로 다른 두 벨트가 각자 다른 포트에 잘 연결되는가"만 순수하게 검증할 수 있다.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 2)); // (0,0),(0,1)
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 50;
        PlaceMachine(MinerMachineId, new Vector2Int(4, 4)); // 원격 전송이라 코어와 안 이어도 됨

        PlaceMachine(ProcessorMachineId, new Vector2Int(3, 0)); // 제련로: 코어와 같은 줄(y=0)
        AssignRecipe(driver.World.Processors.Count - 1, "TestRecipe");

        PlaceMachine(AssemblerMachineId, new Vector2Int(6, 0)); // 조립기(2x2): 입력면(서쪽) = (5,0),(5,1)
        AssignRecipe(driver.World.Processors.Count - 1, "TestGearRecipe");

        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0)); // 코어(y=0) -> 제련로
        DragBelt(new Vector2Int(3, 0), new Vector2Int(6, 0)); // 제련로 -> 조립기 입력 포트 1(철판, y=0)
        DragBelt(new Vector2Int(0, 1), new Vector2Int(6, 1)); // 코어(y=1) -> 조립기 입력 포트 2(광석, y=1)

        RunTicks(1500);

        int gearId = driver.World.Database.GetResourceId("TestGear");
        var assembler = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(assembler.OutputBuffer[gearId], 0,
            "채굴 -> 코어 -> (제련로 경유 철판 + 광석 직접)라는 서로 다른 두 자원이 각자 다른 벨트로 조립기까지 도착해서 조립되어야 함");
    }

    [Test]
    public void CoreLikeUniversalPortsProcessor_SuppliesStoredResource_ToRequestingProcessor()
    {
        // 코어 재현: RecipeId=-1(아무것도 생산 안 함) + UniversalPorts=true(4면 다 포트)인
        // ProcessorInstance를 2x2 칸에 등록해두고(CoreSpawner와 동일한 등록 방식), 그 칸에서
        // 시작하는 드래그로 벨트를 그으면 Source로 잡혀서 쌓아둔 자원을 실제로 넘겨줘야 한다.
        var core = PlaceCoreLike(new Vector2Int(-1, -1), new Vector2Int(2, 2));
        int coreIndex = driver.World.Processors.IndexOf(core);

        int oreId = driver.World.Database.GetResourceId("TestOre");
        core.InputBuffer[oreId] = 5; // 미리 쌓여있는 재고

        PlaceMachine(ProcessorMachineId, new Vector2Int(2, 0)); // 코어 동쪽 모서리(0,-1)/(0,0)에서 두 칸 떨어진 곳
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(2, 0)); // 코어 칸(0,0)에서 시작 -> 제련로 입력면까지

        Assert.AreEqual(1, driver.World.Segments.Count, "코어 칸에서 시작한 드래그는 벨트로 연결되어야 함");
        Assert.AreEqual(coreIndex, driver.World.Segments[0].SourceProcessorId, "코어가 벨트의 소스로 잡혀야 함");

        RunTicks(400);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "코어에 쌓인 재고가 벨트를 타고 제련로까지 도착해서 실제로 소비/가공되어야 함");
    }

    [Test]
    public void DraggingFromProcessorInputTowardCore_StillResolvesCoreAsSource()
    {
        // 실제 플레이에서 자연스러운 조작 순서: 코어에서부터가 아니라, 방금 놓은 제련로를
        // 먼저 누르고 거기서부터 코어 쪽으로 드래그하는 경우. 제련로 입력면 쪽을 만지므로
        // 그 끝은 Target으로 고정 판정되는데, 코어 쪽 끝은 예전 코드에서 isStart가 아니라는
        // 이유만으로 똑같이 Target으로 잡혀서(=둘 다 Target) 방향이 뒤바뀌어 버리던 버그.
        var coreCell = new Vector2Int(1, 0);
        var core = PlaceCoreLike(coreCell, new Vector2Int(1, 1));
        int coreIndex = driver.World.Processors.IndexOf(core);

        int oreId = driver.World.Database.GetResourceId("TestOre");
        core.InputBuffer[oreId] = 5;

        PlaceMachine(ProcessorMachineId, new Vector2Int(4, 0)); // Facing 기본값 (1,0) -> 입력면은 (3,0)
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(4, 0), coreCell); // 제련로에서 시작 -> 코어 쪽으로 드래그(역방향)

        Assert.AreEqual(2, driver.World.Segments.Count, "역방향으로 드래그해도 유효한 포트끼리면 연결되어야 함(코어와 제련로 사이 두 칸)");
        Assert.AreEqual(coreIndex, driver.World.Segments[0].SourceProcessorId, "코어가 소스로 잡혀야 함(제련로가 아니라)");

        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        int smelterIndex = driver.World.Processors.IndexOf(smelter);
        Assert.AreEqual(smelterIndex, driver.World.Segments[driver.World.Segments.Count - 1].TargetProcessorId, "제련로가 타겟으로 잡혀야 함");

        RunTicks(400);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "역방향 드래그로 이어도 코어의 재고가 실제로 제련로까지 도착해야 함");
    }

    [Test]
    public void ProcessorOutputBelt_IntoCore_ActuallyStoresProduceInCoreInputBuffer()
    {
        // 사용자가 기대하는 구조: 제련로에서 나온 산출물을 코어로 보내서 저장해뒀다가,
        // 나중에 코어에서 다시 다른 기계로 꺼내 쓴다("코어에 저장되어 있는 철판을 가져올거라고").
        // 지금까지의 테스트는 전부 "코어 -> 벨트 -> 기계" 방향(코어가 Source)만 검증했고,
        // 이 반대 방향("기계 -> 벨트 -> 코어", 코어가 Target)은 한 번도 자동 검증된 적이 없었다.
        var core = PlaceCoreLike(new Vector2Int(3, 0), new Vector2Int(1, 1));
        int coreIndex = driver.World.Processors.IndexOf(core);

        PlaceMachine(ProcessorMachineId, new Vector2Int(0, 0)); // Facing 기본값 (1,0) -> 출력면은 (1,0)
        AssignRecipeToNewestProcessor();
        int smelterIndex = driver.World.Processors.Count - 1;
        var smelter = driver.World.Processors[smelterIndex];
        smelter.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 50; // 벨트 없이 직접 원료 공급(제련 자체는 이 테스트 대상이 아님)

        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0)); // 제련로 출력면에서 시작 -> 코어 쪽으로

        Assert.AreEqual(smelterIndex, driver.World.Segments[0].SourceProcessorId, "제련로가 소스로 잡혀야 함");
        Assert.AreEqual(coreIndex, driver.World.Segments[driver.World.Segments.Count - 1].TargetProcessorId, "코어가 타겟으로 잡혀야 함");

        RunTicks(400);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        Assert.Greater(core.InputBuffer[outputId], 0,
            "제련로의 산출물이 벨트를 타고 코어의 InputBuffer에 실제로 쌓여야 함(코어를 창고로 쓰는 구조)");
    }
}
