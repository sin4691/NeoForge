using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;

public class RoutingSystemTests
{
    [Test]
    public void Splitter_DistributesEvenlyAcrossThreeOutputs_RoundRobin()
    {
        var db = BuildDatabase(out int oreId, out _, out int sinkRecipeId);
        var splitter = new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter };
        splitter.InputBuffer[oreId] = 30;

        // 각 출력 벨트는 "요청하는 기계"(레시피 지정 + 그 레시피가 실제로 이 자원을 씀)로
        // 이어져야 분류기가 보낸다.
        var processors = new List<ProcessorInstance> { splitter, Sink(db, sinkRecipeId), Sink(db, sinkRecipeId), Sink(db, sinkRecipeId) };
        var outA = new BeltSegment { Id = 0, SourceProcessorId = 0, TargetProcessorId = 1 };
        var outB = new BeltSegment { Id = 1, SourceProcessorId = 0, TargetProcessorId = 2 };
        var outC = new BeltSegment { Id = 2, SourceProcessorId = 0, TargetProcessorId = 3 };

        var segments = new List<BeltSegment> { outA, outB, outC };
        var system = new RoutingSystem();

        int a = 0, b = 0, c = 0;
        for (int i = 0; i < 30; i++)
        {
            system.Tick(processors, segments, db);
            // 벨트가 곧바로 실어 갔다고 치고 입구를 비운다(다음 틱에 또 받을 수 있게).
            a += Drain(outA);
            b += Drain(outB);
            c += Drain(outC);
        }

        Assert.AreEqual(30, a + b + c, "투입 30개가 전부 세 출력으로 나뉘어야 함");
        Assert.AreEqual(10, a);
        Assert.AreEqual(10, b);
        Assert.AreEqual(10, c);
        Assert.AreEqual(0, splitter.InputBuffer[oreId]);
    }

    [Test]
    public void Splitter_SkipsBlockedOutput_AndKeepsFlowingToTheRest()
    {
        var db = BuildDatabase(out int oreId, out _, out int sinkRecipeId);
        var splitter = new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter };
        splitter.InputBuffer[oreId] = 20;

        var processors = new List<ProcessorInstance> { splitter, Sink(db, sinkRecipeId), Sink(db, sinkRecipeId), Sink(db, sinkRecipeId) };
        var outA = new BeltSegment { Id = 0, SourceProcessorId = 0, TargetProcessorId = 1 };
        var outBlocked = new BeltSegment { Id = 1, SourceProcessorId = 0, TargetProcessorId = 2 };
        var outC = new BeltSegment { Id = 2, SourceProcessorId = 0, TargetProcessorId = 3 };
        // outBlocked 입구에 아이템을 박아두고 절대 안 비운다 -> 항상 HeadFree=false -> 건너뛰어야 함.
        outBlocked.Items.Add(new BeltItem(oreId, 0f));

        var segments = new List<BeltSegment> { outA, outBlocked, outC };
        var system = new RoutingSystem();

        int a = 0, c = 0;
        for (int i = 0; i < 20; i++)
        {
            system.Tick(processors, segments, db);
            a += Drain(outA);
            c += Drain(outC);
        }

        Assert.AreEqual(1, outBlocked.Items.Count, "막힌 출력에는 아무것도 새로 안 들어가야 함");
        Assert.AreEqual(20, a + c, "나머지 두 출력으로 전량이 흘러야 함(전체 정지 아님)");
        Assert.AreEqual(0, splitter.InputBuffer[oreId]);
    }

    [Test]
    public void Merger_CombinesInputsIntoOneOutput_AlternatingResourceTypes()
    {
        var db = BuildDatabase(out int oreId, out int coalId, out int sinkRecipeId);
        var merger = new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Merger };
        // 여러 입력 벨트가 배달해준 상태를 흉내 — InputBuffer에 두 종류가 쌓여 있다.
        merger.InputBuffer[oreId] = 15;
        merger.InputBuffer[coalId] = 15;

        // 합류기 출력을 받는 기계는 오레/석탄을 둘 다 쓰는 레시피여야 번갈아 내보내는 걸 확인할 수 있다.
        var processors = new List<ProcessorInstance> { merger, Sink(db, sinkRecipeId) };
        var output = new BeltSegment { Id = 0, SourceProcessorId = 0, TargetProcessorId = 1 };

        var segments = new List<BeltSegment> { output };
        var system = new RoutingSystem();

        int ore = 0, coal = 0;
        int alternations = 0;
        int lastResource = -1;
        for (int i = 0; i < 30; i++)
        {
            system.Tick(processors, segments, db);
            if (output.Items.Count == 0) continue;
            int r = output.Items[0].ResourceId;
            if (r == oreId) ore++;
            else coal++;
            if (lastResource != -1 && r != lastResource) alternations++;
            lastResource = r;
            output.Items.Clear();
        }

        Assert.AreEqual(30, ore + coal, "두 입력 재고 30개가 전부 단일 출력으로 병합되어야 함");
        Assert.AreEqual(15, ore);
        Assert.AreEqual(15, coal);
        Assert.GreaterOrEqual(alternations, 28, "한 종류만 몰아 내보내지 않고 번갈아 내보내야 함");
    }

    [Test]
    public void CoreToSplitterToMachine_DispensesOnlyWhatTheDownstreamMachineNeeds()
    {
        // 사용자 보고: 코어 -> 벨트 -> 분류기 -> 기계로 이으면 아무것도 안 나왔다.
        // FindTerminalTarget이 분류기(RecipeId<0)에서 멈춰 코어가 뭘 원하는지 판단 못 했기 때문.
        // 이제 분류기를 통과해 뒤의 실제 기계 레시피를 보고 그 자원만 실어준다.
        var db = BuildChainDatabase(out int ironId, out int coalId, out int plateId, out int formRecipeId, out _);
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[ironId] = 20;
        core.InputBuffer[coalId] = 20; // 레시피가 안 쓰는 자원 — 그대로 남아 있어야 함

        int splitterIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });
        var former = new ProcessorInstance(db.ResourceCount) { RecipeId = formRecipeId };
        int formerIndex = world.AddProcessor(former);

        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = splitterIndex });
        world.AddBeltSegment(new BeltSegment { Id = 1, SourceProcessorId = splitterIndex, TargetProcessorId = formerIndex });

        for (int i = 0; i < 600; i++) world.Tick(0.05f);

        Assert.Greater(former.OutputBuffer[plateId], 0, "코어 -> 분류기 -> 성형기 체인에서 철판이 나와야 함");
        Assert.AreEqual(20, core.InputBuffer[coalId], "레시피가 안 쓰는 석탄은 분류기 너머로도 안 실려야 함");
    }

    [Test]
    public void CoreToSplitter_DemolishedBranchLosesPriority_RemainingBranchStillGetsFed()
    {
        // 사용자 보고: 분류기 출력 3개 중 가운데(철 레시피)로 가던 벨트를 철거했는데도, 코어에서
        // 분류기로 들어가는 입구 벨트가 계속 가운데 쪽 자원(철)만 담당하려 해서 실제로 남아있는
        // 옆 갈래(석탄 레시피)로는 영영 아무것도 안 나왔다. 갈래 우선순위가 "먼저 만들어진 벨트
        // id" 기준이라, 철거로 끊긴 갈래라도 한 번 "선호 갈래"였으면 계속 그 자리를 차지했기
        // 때문 — BeltRouting.Resolve가 이제 "연결된 갈래만" 후보로 보고, 그 중 "가장 최근에
        // 레시피가 지정된 갈래"를 우선하도록 고쳤다.
        var db = BuildIronOrCoalRecipeDatabase(out int ironId, out int coalId, out int ironRecipeId, out int coalRecipeId);
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[ironId] = 20;
        core.InputBuffer[coalId] = 20;

        int splitterIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });
        // 플레이어가 옆(석탄)에 먼저, 가운데(철)에 나중에 레시피를 지정한 상황 — "가장 최근에 레시피가
        // 지정된 갈래 우선" 규칙상 철거 전엔 가운데가 우선이어야 철거 후 우선순위 이전을 검증할 수 있다.
        int sideSequence = ProcessorInstance.NextRecipeSetSequence();
        int middleSequence = ProcessorInstance.NextRecipeSetSequence();
        var middle = new ProcessorInstance(db.ResourceCount) { RecipeId = ironRecipeId, RecipeSetSequence = middleSequence };
        var side = new ProcessorInstance(db.ResourceCount) { RecipeId = coalRecipeId, RecipeSetSequence = sideSequence };
        int middleIndex = world.AddProcessor(middle);
        int sideIndex = world.AddProcessor(side);

        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = splitterIndex });
        // 가운데(id=1)가 옆(id=2)보다 먼저 만들어진 갈래 — 예전 코드였다면 계속 이쪽만 담당했을 것.
        var middleBelt = new BeltSegment { Id = 1, SourceProcessorId = splitterIndex, TargetProcessorId = middleIndex };
        world.AddBeltSegment(middleBelt);
        world.AddBeltSegment(new BeltSegment { Id = 2, SourceProcessorId = splitterIndex, TargetProcessorId = sideIndex });

        for (int i = 0; i < 50; i++) world.Tick(0.05f);
        Assert.Greater(middle.InputBuffer[ironId], 0, "철거 전에는 가운데(철)로 자원이 흘러야 함");

        world.RemoveSegment(middleBelt.Id); // 사용자가 가운데로 가던 벨트를 철거.

        for (int i = 0; i < 400; i++) world.Tick(0.05f);

        Assert.Greater(side.InputBuffer[coalId], 0, "가운데 갈래가 끊겼으면 남은 옆 갈래(석탄)로 반드시 흘러야 함");
    }

    [Test]
    public void CoreToSplitterToThreeGenerators_OnlyOneFuelSelected_StillDispensesToThatOne()
    {
        // 사용자 보고: 분류기 갈래에 발전기 3대를 물리고 하나만 연료(석탄)를 고르면 아무것도
        // 안 나오다가, 셋 다 골라야만 코어에서 석탄이 나왔다. 원인은 IsRequestingConsumer가
        // "연료를 실제로 골랐는지"가 아니라 "발전기이기만 하면" 요청 중으로 쳐서, 아직 연료를
        // 안 고른 발전기가 우선순위 다툼에서 이겨버리면 입구 벨트가 그 발전기(
        // SelectedFuelResourceId=-1)로 잠겨서 아무것도 못 내보냈기 때문 — 이제 연료를 실제로
        // 고른 것만 "요청 중"으로 쳐야 한다(BeltRouting.IsRequestingConsumer).
        var db = BuildDatabase(out _, out int coalId, out _);
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[coalId] = 20;

        int splitterIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });

        var gen1 = new ProcessorInstance(db.ResourceCount) { IsGeneratorFuelPort = true, SelectedFuelResourceId = coalId, RecipeSetSequence = ProcessorInstance.NextRecipeSetSequence() };
        var gen2 = new ProcessorInstance(db.ResourceCount) { IsGeneratorFuelPort = true }; // 연료 미선택
        var gen3 = new ProcessorInstance(db.ResourceCount) { IsGeneratorFuelPort = true }; // 연료 미선택
        int gen1Index = world.AddProcessor(gen1);
        int gen2Index = world.AddProcessor(gen2);
        int gen3Index = world.AddProcessor(gen3);

        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = splitterIndex });
        world.AddBeltSegment(new BeltSegment { Id = 1, SourceProcessorId = splitterIndex, TargetProcessorId = gen1Index });
        world.AddBeltSegment(new BeltSegment { Id = 2, SourceProcessorId = splitterIndex, TargetProcessorId = gen2Index });
        world.AddBeltSegment(new BeltSegment { Id = 3, SourceProcessorId = splitterIndex, TargetProcessorId = gen3Index });

        for (int i = 0; i < 200; i++) world.Tick(0.05f);

        Assert.Greater(gen1.InputBuffer[coalId], 0, "연료를 고른 발전기 하나만 있어도 코어에서 석탄이 나와야 함");
        Assert.AreEqual(0, gen2.InputBuffer[coalId], "연료 미선택 발전기는 여전히 아무것도 안 받아야 함");
        Assert.AreEqual(0, gen3.InputBuffer[coalId], "연료 미선택 발전기는 여전히 아무것도 안 받아야 함");
    }

    [Test]
    public void ChainedSplitters_ThreeInARow_ItemsReachTheFinalMachine()
    {
        // 사용자 질문: "분류기에 분류기 연결하고 분류기 연결하고 하면?" — 분류기 출력이 또 다른
        // 분류기 입력으로 여러 단 이어지는 체인이 실제로 작동하는지 확인. BeltRouting.Resolve가
        // 라우팅 노드를 만나면 재귀적으로 그 갈래를 또 훑으므로(BeltRouting.cs 참고) 이론상
        // 단수에 상관없이 되어야 하는데, 실제로 끝까지 뚫리는지 직접 검증한다.
        var db = BuildChainDatabase(out int ironId, out _, out int plateId, out int formRecipeId, out _);
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[ironId] = 30;

        int splitter1Index = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });
        int splitter2Index = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });
        int splitter3Index = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });
        var former = new ProcessorInstance(db.ResourceCount) { RecipeId = formRecipeId };
        int formerIndex = world.AddProcessor(former);

        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = splitter1Index });
        world.AddBeltSegment(new BeltSegment { Id = 1, SourceProcessorId = splitter1Index, TargetProcessorId = splitter2Index });
        world.AddBeltSegment(new BeltSegment { Id = 2, SourceProcessorId = splitter2Index, TargetProcessorId = splitter3Index });
        world.AddBeltSegment(new BeltSegment { Id = 3, SourceProcessorId = splitter3Index, TargetProcessorId = formerIndex });

        for (int i = 0; i < 1200; i++) world.Tick(0.05f); // 60초 분량 — 3단 전파 + 처리 시간 넉넉히.

        Assert.Greater(former.OutputBuffer[plateId], 0, "분류기 3개를 연달아 거쳐도 마지막 기계까지 자원이 도착해서 철판이 나와야 함");
        Assert.Less(core.InputBuffer[ironId], 30, "코어가 실제로 철 주괴를 내보냈어야 함");
    }

    [Test]
    public void CoreToSplitterToThreeMachines_OnlyMiddleHasRecipe_CoreStillDispenses()
    {
        // 사용자 보고: 분류기 출력 3개에 제련로 3대를 물리고 "가운데만" 레시피를 지정하면
        // 코어가 아무것도 안 나왔다 — FindTerminalTarget이 분류기 첫 출력 갈래(레시피 없음)만
        // 보고 멈췄기 때문. 이제 모든 갈래를 훑어 레시피 있는 갈래를 목적지로 삼는다.
        var db = BuildChainDatabase(out int ironId, out _, out int plateId, out int formRecipeId, out _);
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[ironId] = 30;

        int splitterIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });
        var top = new ProcessorInstance(db.ResourceCount) { RecipeId = -1 };       // 레시피 미지정
        var middle = new ProcessorInstance(db.ResourceCount) { RecipeId = formRecipeId };
        var bottom = new ProcessorInstance(db.ResourceCount) { RecipeId = -1 };    // 레시피 미지정
        int topIndex = world.AddProcessor(top);
        int middleIndex = world.AddProcessor(middle);
        int bottomIndex = world.AddProcessor(bottom);

        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = splitterIndex });
        world.AddBeltSegment(new BeltSegment { Id = 1, SourceProcessorId = splitterIndex, TargetProcessorId = topIndex });
        world.AddBeltSegment(new BeltSegment { Id = 2, SourceProcessorId = splitterIndex, TargetProcessorId = middleIndex });
        world.AddBeltSegment(new BeltSegment { Id = 3, SourceProcessorId = splitterIndex, TargetProcessorId = bottomIndex });

        for (int i = 0; i < 1200; i++) world.Tick(0.05f);

        Assert.Greater(middle.OutputBuffer[plateId], 0, "레시피를 지정한 가운데 제련로에서 철판이 나와야 함");
        Assert.Less(core.InputBuffer[ironId], 30, "코어가 철 주괴를 실제로 내보냈어야 함");
        Assert.AreEqual(0, top.InputBuffer[ironId], "레시피 미지정 제련로에는 아무것도 보내지 않아야 함");
        Assert.AreEqual(0, bottom.InputBuffer[ironId], "레시피 미지정 제련로에는 아무것도 보내지 않아야 함");
    }

    [Test]
    public void CoreToMergerToTwoInputMachine_CombinesBothMaterialsIntoOnePort()
    {
        // 합류기가 종류별로 섞어 한 벨트에 실은 걸, 입력 2개짜리 기계가 한 포트로 받아도
        // 두 재료가 InputBuffer에 쌓여 레시피가 돈다.
        var db = BuildChainDatabase(out int ironId, out int coalId, out _, out _, out int synthRecipeId);
        int steelId = db.GetResourceId("SteelIngot");
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[ironId] = 20;
        core.InputBuffer[coalId] = 20;

        int mergerIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Merger });
        var synth = new ProcessorInstance(db.ResourceCount) { RecipeId = synthRecipeId };
        int synthIndex = world.AddProcessor(synth);

        // 코어 -> 합류기 두 라인(각자 철/석탄 담당 자동 배정), 합류기 -> 합성기 한 라인.
        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = mergerIndex });
        world.AddBeltSegment(new BeltSegment { Id = 1, SourceProcessorId = coreIndex, TargetProcessorId = mergerIndex });
        world.AddBeltSegment(new BeltSegment { Id = 2, SourceProcessorId = mergerIndex, TargetProcessorId = synthIndex });

        for (int i = 0; i < 800; i++) world.Tick(0.05f);

        Assert.Greater(synth.OutputBuffer[steelId], 0, "합류기가 섞어 보낸 철+석탄을 합성기가 한 포트로 받아 강철 주괴를 만들어야 함");
    }

    // 라우팅 노드 출력 벨트의 "요청하는 종착 기계" 역할. RoutingSystem이 이제 레시피 내용까지
    // 보므로(이 갈래가 실제로 그 자원을 쓰는지), 버퍼에 들어올 수 있는 자원을 전부 받는
    // 레시피를 물려줘야 한다.
    private static ProcessorInstance Sink(GameDatabase db, int recipeId)
        => new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId };

    private static int Drain(BeltSegment belt)
    {
        int n = belt.Items.Count;
        belt.Items.Clear();
        return n;
    }

    // 철광석/철주괴/석탄/철판/강철주괴 + 성형(철주괴->철판) + 합성(철주괴+석탄->강철주괴).
    private static GameDatabase BuildChainDatabase(out int ironId, out int coalId, out int plateId, out int formRecipeId, out int synthRecipeId)
    {
        var items = new[]
        {
            new ItemData { itemID = "IronIngot" },
            new ItemData { itemID = "Coal" },
            new ItemData { itemID = "IronPlate" },
            new ItemData { itemID = "SteelIngot" },
        };
        var machines = new[]
        {
            new MachineData { machineID = "Former" },
            new MachineData { machineID = "Synthesizer" },
        };
        var recipes = new[]
        {
            new RecipeData
            {
                recipeID = "FormIronPlate", machineID = "Former", timeToCraft = 1f,
                inputItems = new List<string> { "IronIngot" }, outputItems = new List<string> { "IronPlate" },
            },
            new RecipeData
            {
                recipeID = "SynthesizeSteelIngot", machineID = "Synthesizer", timeToCraft = 1f,
                inputItems = new List<string> { "IronIngot", "Coal" }, outputItems = new List<string> { "SteelIngot" },
            },
        };

        var db = GameDatabase.Build(items, machines, recipes);
        ironId = db.GetResourceId("IronIngot");
        coalId = db.GetResourceId("Coal");
        plateId = db.GetResourceId("IronPlate");
        formRecipeId = db.GetRecipeId("FormIronPlate");
        synthRecipeId = db.GetRecipeId("SynthesizeSteelIngot");
        return db;
    }

    // 철만 쓰는 레시피 하나, 석탄만 쓰는 레시피 하나 — 분류기 갈래 두 개가 서로 다른 자원을
    // 원하는 상황(가운데=철, 옆=석탄)을 재현하기 위한 전용 DB.
    private static GameDatabase BuildIronOrCoalRecipeDatabase(out int ironId, out int coalId, out int ironRecipeId, out int coalRecipeId)
    {
        var items = new[] { new ItemData { itemID = "IronIngot" }, new ItemData { itemID = "Coal" } };
        var recipes = new[]
        {
            new RecipeData
            {
                recipeID = "NeedsIron", machineID = "IronUser", timeToCraft = 1f,
                inputItems = new List<string> { "IronIngot" }, outputItems = new List<string>(),
            },
            new RecipeData
            {
                recipeID = "NeedsCoal", machineID = "CoalUser", timeToCraft = 1f,
                inputItems = new List<string> { "Coal" }, outputItems = new List<string>(),
            },
        };

        var db = GameDatabase.Build(items, System.Array.Empty<MachineData>(), recipes);
        ironId = db.GetResourceId("IronIngot");
        coalId = db.GetResourceId("Coal");
        ironRecipeId = db.GetRecipeId("NeedsIron");
        coalRecipeId = db.GetRecipeId("NeedsCoal");
        return db;
    }

    private static GameDatabase BuildDatabase(out int oreId, out int coalId, out int sinkRecipeId)
    {
        var ore = new ItemData { itemID = "IronOre" };
        var coal = new ItemData { itemID = "Coal" };
        // 오레/석탄을 둘 다 받아주는 더미 레시피 — 테스트용 Sink 기계가 무엇이 흘러들어와도
        // "요청하는 기계"로 인식되게 한다.
        var sinkRecipe = new RecipeData
        {
            recipeID = "SinkRecipe",
            machineID = "Sink",
            timeToCraft = 1f,
            inputItems = new List<string> { "IronOre", "Coal" },
            outputItems = new List<string>(),
        };
        var db = GameDatabase.Build(new[] { ore, coal }, System.Array.Empty<MachineData>(), new[] { sinkRecipe });
        oreId = db.GetResourceId("IronOre");
        coalId = db.GetResourceId("Coal");
        sinkRecipeId = db.GetRecipeId("SinkRecipe");
        return db;
    }
}
