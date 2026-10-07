using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;

public class BeltSystemTests
{
    [Test]
    public void Tick_MovesItemAlongSegment()
    {
        var segment = new BeltSegment { Id = 0, Length = 2f, SpeedUnitsPerSecond = 1f };
        segment.Items.Add(new BeltItem(0, 0f));

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        system.Tick(0.5f, segments, processors, null);
        Assert.AreEqual(0.5f, segment.Items[0].Position, 0.001f);

        system.Tick(0.5f, segments, processors, null);
        Assert.AreEqual(1.0f, segment.Items[0].Position, 0.001f);
    }

    [Test]
    public void Tick_ItemsNeverOvertake_AndCompressFromBehindWhenBlocked()
    {
        var segment = new BeltSegment { Id = 0, Length = 5f, SpeedUnitsPerSecond = 10f, ItemSpacing = 0.3f };
        segment.Items.Add(new BeltItem(0, 0f));   // 뒤쪽(작은 position)
        segment.Items.Add(new BeltItem(0, 4.9f)); // 앞쪽(끝에 거의 도달)

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        // 다음 세그먼트도 목표 기계도 없는 막다른 벨트 -> 큰 델타를 줘도 끝에서 대기해야 함
        system.Tick(1f, segments, processors, null);
        Assert.AreEqual(5f, segment.Items[1].Position, 0.001f, "프론트 아이템은 막다른 끝에서 대기");
        Assert.AreEqual(5f - segment.ItemSpacing, segment.Items[0].Position, 0.001f, "뒤 아이템은 프론트를 추월하지 못하고 압축되어 대기");
    }

    [Test]
    public void Tick_ItemCarriesOverflowAcrossSegmentBoundary_NoStutter()
    {
        // 연결부위 멈칫 버그 회귀 테스트: 프론트 아이템이 한 틱에 경계를 넘어야 할 때,
        // 다음 세그먼트에서 position 0이 아니라 실제 overflow만큼 이어서 시작해야 한다.
        var segment0 = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, NextSegmentId = 1 };
        var segment1 = new BeltSegment { Id = 1, Length = 1f };
        segment0.Items.Add(new BeltItem(0, 0.95f));

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { segment0, segment1 };
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        system.Tick(0.1f, segments, processors, null); // desired = 0.95 + 2*0.1 = 1.15 -> overflow 0.15

        Assert.AreEqual(0, segment0.Items.Count, "경계를 넘은 아이템은 이번 세그먼트에서 사라져야 함");
        Assert.AreEqual(1, segment1.Items.Count);
        Assert.AreEqual(0.15f, segment1.Items[0].Position, 0.001f, "position 0이 아니라 overflow만큼 이어서 시작해야 함");
    }

    [Test]
    public void Tick_ProcessesDownstreamFirst_EvenWhenArrayOrderIsUpstreamFirst()
    {
        // 회귀 테스트: 기존 벨트 "앞쪽"에 나중에 이어붙이면, 새 세그먼트가 리스트엔 나중에
        // 추가되지만 실제로는 상류다 (배열 인덱스가 소스->목적지 순서와 반대). 예전엔 이 경우
        // 경계에서 낡은 상태로 판단해 아이템이 멈춰있는 것처럼 보이는 버그가 있었다.
        var downstream = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 5f }; // 배열상 먼저(index 0)지만 실제로는 하류
        var upstream = new BeltSegment { Id = 1, Length = 1f, SpeedUnitsPerSecond = 5f, NextSegmentId = 0 }; // 배열상 나중(index 1)이지만 실제로는 상류
        upstream.Items.Add(new BeltItem(0, 0.9f));

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { downstream, upstream }; // 일부러 소스->목적지 반대 순서로 전달
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        for (int i = 0; i < 20; i++)
        {
            system.Tick(0.05f, segments, processors, null);
        }

        Assert.AreEqual(0, upstream.Items.Count, "상류 세그먼트에서 아이템이 하류로 넘어갔어야 함");
        Assert.AreEqual(1, downstream.Items.Count);
        Assert.Greater(downstream.Items[0].Position, 0f, "하류로 넘어간 뒤에도 계속 전진해야 함 (제자리에 멈춰있으면 안 됨)");
    }

    [Test]
    public void Tick_LongRun_NoItemLossOrDuplication_AcrossProcessorBeltProcessorChain()
    {
        // 채굴기는 벨트 소스가 될 수 없으므로(원격 전송, MinerSystem 참고), 여기서는 일반
        // Processor를 소스로 써서 벨트 자체의 장시간 무유실/무중복 동작만 검증한다.
        var db = BuildMinimalDatabase(out int resourceId);

        var source = new ProcessorInstance(db.ResourceCount);
        source.OutputBuffer[resourceId] = 20;
        // 저장고(UniversalPorts)를 종착으로 둔다 — 레시피 없는 일반 기계는 이제 벨트 배달을
        // 거부하므로(요청 안 한 기계엔 안 넣음), 벨트 자체의 무유실/무중복만 보려면 코어형 종착이 필요.
        var target = new ProcessorInstance(db.ResourceCount) { UniversalPorts = true };

        var segment0 = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, NextSegmentId = 1 };
        var segment1 = new BeltSegment { Id = 1, Length = 1f, SpeedUnitsPerSecond = 2f, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segment0, segment1 };
        var processors = new List<ProcessorInstance> { source, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 500; i++)
        {
            system.Tick(0.05f, segments, processors, null);
        }

        int onBelt = segment0.Items.Count + segment1.Items.Count;
        int total = source.OutputBuffer[resourceId] + onBelt + target.InputBuffer[resourceId];

        Assert.AreEqual(20, total, "산출 대기+벨트 위+기계 투입분의 합은 항상 원래 산출량과 같아야 함 (유실/중복 없음)");
        Assert.AreEqual(0, source.OutputBuffer[resourceId], "충분히 오래 돌면 모두 벨트로 실려 나가야 함");
        Assert.AreEqual(20, target.InputBuffer[resourceId]);
    }

    [Test]
    public void CoreSource_DeadEndBelt_NeverDispenses()
    {
        // 사용자가 보고한 버그: 코어에서 아무 목적지도 없는(막다른) 벨트를 뽑아두면 그것만으로
        // 재고가 흘러나오면 안 된다 — 받으려는 기계가 없으면 코어가 내줄 이유가 없다.
        var db = BuildMinimalDatabase(out int oreId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 10;

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0 }; // NextSegmentId/TargetProcessorId 둘 다 없음(막다른 벨트)
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(0, segment.Items.Count, "막다른 벨트에는 코어가 아무것도 흘려보내면 안 됨");
        Assert.AreEqual(10, core.InputBuffer[oreId], "재고도 그대로 남아있어야 함");
    }

    [Test]
    public void CoreSource_SelfLoopBackToCore_NeverDispenses()
    {
        // 사용자가 보고한 버그: 코어에서 뽑은 벨트의 끝을 다시 같은 코어에 연결하면,
        // 레시피를 지정하지 않았는데도 코어 내용물(석탄 등)이 계속 흘러나와 제자리를 돌았다.
        // 받을 기계가 없는 자기 루프는 막다른 벨트와 똑같이 아무것도 내주면 안 된다.
        var db = BuildMinimalDatabase(out int oreId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 10;

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 0 };
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(0, segment.Items.Count, "코어 자기 루프에는 아무것도 흘려보내면 안 됨");
        Assert.AreEqual(10, core.InputBuffer[oreId], "재고도 그대로 남아있어야 함");
    }

    [Test]
    public void CoreSource_TargetWithoutRecipe_NeverDispenses()
    {
        // 목적지 기계는 있지만 아직 레시피를 지정 안 했으면("정보를 전달받기 전") 코어가
        // 뭘 원하는지 모르니 아무것도 내주면 안 된다.
        var db = BuildMinimalDatabase(out int oreId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 10;
        var target = new ProcessorInstance(db.ResourceCount); // RecipeId 기본값 -1(미지정)

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(0, segment.Items.Count, "레시피 미지정 기계로는 코어가 아무것도 흘려보내면 안 됨");
        Assert.AreEqual(10, core.InputBuffer[oreId]);
    }

    [Test]
    public void ItemsOnBelt_DoNotEnterRecipelessMachine_EvenWhenConnected()
    {
        // 사용자 보고: 벨트 끝에 아이템이 쌓여 있는데 그 끝에 기계를 놓아 연결되면, 레시피를
        // 지정 안 했는데도 아이템이 그냥 입력 버퍼로 빨려 들어간다. 요청 안 한 기계는 안 받아야 한다.
        var db = BuildMinimalDatabase(out int resourceId);
        var machine = new ProcessorInstance(db.ResourceCount); // RecipeId 기본값 -1(미지정)
        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, TargetProcessorId = 0 };
        for (int i = 0; i < 3; i++) segment.Items.Add(new BeltItem(resourceId, 1f - i * 0.3f)); // 벨트 위에 이미 얹혀 있는 아이템들

        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { machine };
        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(0, machine.InputBuffer[resourceId], "레시피 미지정 기계는 벨트 아이템을 받지 않아야 함");
        Assert.AreEqual(3, segment.Items.Count, "아이템은 벨트 끝에서 그대로 대기해야 함(유실 없음)");

        // 레시피를 지정하면 그때부터 정상적으로 받아들인다.
        machine.RecipeId = 0;
        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);
        Assert.AreEqual(3, machine.InputBuffer[resourceId], "레시피 지정 후에는 대기하던 아이템이 들어와야 함");
        Assert.AreEqual(0, segment.Items.Count);
    }

    [Test]
    public void CoreSource_TargetWithRecipe_DispensesOnlyTheNeededResource()
    {
        // 코어에 두 종류가 쌓여 있어도, 목적지 레시피가 필요로 하는 자원만 내줘야 한다
        // ("레시피를 지정하면 그 정보를 전달받아 필요한 것만 준다"는 원래 설계).
        var db = BuildDatabaseWithRecipe(out int oreId, out int scrapId, out int recipeId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 5;
        core.InputBuffer[scrapId] = 5; // 레시피와 무관한 자원도 같이 쌓여있음
        var target = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId }; // TestOre만 필요

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(5, core.InputBuffer[scrapId], "레시피가 필요로 하지 않는 자원은 그대로 남아있어야 함");
        Assert.Less(core.InputBuffer[oreId], 5, "레시피가 필요로 하는 자원은 실제로 빠져나가야 함");
    }

    [Test]
    public void CoreSource_TargetNeedsTwoResources_SingleLaneNeverMixesTypes()
    {
        // 사용자가 스크린샷으로 보고한 버그: 목적지가 서로 다른 두 자원을 필요로 하고 코어에
        // 둘 다 쌓여 있으면, 벨트 하나가 매 틱 아무거나 골라서 섞어 올렸다. 이제는 그 라인이
        // 처음 실어 나른 자원으로 끝까지 굳어져야 한다("벨트 하나당 한 종류").
        var db = BuildDatabaseWithTwoInputRecipe(out int oreId, out int scrapId, out int recipeId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 20;
        core.InputBuffer[scrapId] = 20;
        var target = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId }; // TestOre + TestScrap 둘 다 필요

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core, target };

        var system = new BeltSystem();
        system.Configure(segments);

        // 첫 로드로 라인이 뭘로 굳어졌는지 확인.
        system.Tick(0.1f, segments, processors, db);
        Assert.IsTrue(segment.LockedSourceResourceId.HasValue, "첫 아이템이 실린 순간 라인이 굳어져야 함");
        int lockedId = segment.LockedSourceResourceId.Value;

        for (int i = 0; i < 100; i++) system.Tick(0.1f, segments, processors, db);

        int otherId = lockedId == oreId ? scrapId : oreId;
        Assert.AreEqual(20, core.InputBuffer[otherId], "굳어진 자원이 아닌 쪽은 이 라인에서 전혀 안 빠져나가야 함(섞이면 안 됨)");
        Assert.Less(core.InputBuffer[lockedId], 20, "굳어진 자원은 계속 이 라인으로 빠져나가야 함");
    }

    [Test]
    public void CoreSource_TwoSeparateLanesToSameTarget_LockToDifferentResources()
    {
        // 사용자가 두 번째 스크린샷으로 보고한 버그: 벨트 두 줄을 각각 따로 그어서 코어 ->
        // 같은 조립기로 연결했더니, 둘 다 똑같이 "맨 앞 자원부터 시도"해서 결국 같은 자원
        // 하나로 몰렸다. 이제는 라인마다 시도 순서가 어긋나서 서로 다른 자원으로 갈라져야 한다.
        var db = BuildDatabaseWithTwoInputRecipe(out int oreId, out int scrapId, out int recipeId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 20;
        core.InputBuffer[scrapId] = 20;
        var target = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId };

        var segmentA = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segmentB = new BeltSegment { Id = 1, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segmentA, segmentB };
        var processors = new List<ProcessorInstance> { core, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.IsTrue(segmentA.LockedSourceResourceId.HasValue && segmentB.LockedSourceResourceId.HasValue,
            "두 라인 다 뭔가로 굳어져 있어야 함");
        Assert.AreNotEqual(segmentA.LockedSourceResourceId.Value, segmentB.LockedSourceResourceId.Value,
            "같은 목적지로 가는 두 라인은 서로 다른 자원으로 갈라져야 함(둘 다 같은 자원으로 몰리면 안 됨)");
    }

    [Test]
    public void CoreSource_SecondLaneAssignedScarceResource_WaitsIdleInsteadOfCarryingSiblingsResource()
    {
        // 실제로 재현된 버그: 코어에 아직 오레만 쌓여있고(스크랩은 나중에야 채워짐, 예: 제련로가
        // 아직 산출 전) 같은 목적지로 가는 라인이 둘이면, 담당을 "재고 있는지"로 정하는 예전
        // 방식은 A/B 둘 다 오레만 계속 실어 날랐다(스크랩 담당 라인도 재고 없다고 오레를 대신
        // 나름). 그러다 목적지의 오레 버퍼가 꽉 차면(용량 한계) 벨트에 오레가 계속 쌓여서
        // 뒤늦게 스크랩이 코어에 들어와도 물리적으로 못 지나가는 정체가 생겼다.
        // 이제는 담당을 재고와 무관하게 "아직 아무도 안 맡은 재료"로 즉시 정하므로, 스크랩
        // 담당 라인(B)은 스크랩 재고가 없는 동안 오레를 대신 나르지 않고 그냥 비어서 기다려야
        // 하고, 그래서 A가 나르는 오레만으로 목적지 버퍼가 꽉 찰 일이 없다.
        var db = BuildDatabaseWithTwoInputRecipe(out int oreId, out int scrapId, out int recipeId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 20; // scrapId는 아직 0 -> 아직 코어에 없음(제련로가 아직 안 만듦)
        var target = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId };

        var segmentA = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segmentB = new BeltSegment { Id = 1, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segmentA, segmentB };
        var processors = new List<ProcessorInstance> { core, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 20; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(oreId, segmentA.LockedSourceResourceId, "A는 아무도 안 맡은 오레를 즉시 담당해야 함");
        Assert.AreEqual(scrapId, segmentB.LockedSourceResourceId,
            "B는 재고가 없어도 스크랩 담당으로 즉시 정해져야 함(오레 담당으로 잘못 굳으면 안 됨)");
        Assert.AreEqual(0, segmentB.Items.Count, "담당 자원(스크랩) 재고가 없는 동안은 오레를 대신 나르지 않고 비어서 기다려야 함");
        Assert.AreEqual(0, core.InputBuffer[scrapId], "스크랩은 아직 안 들어왔으니 그대로 0이어야 함");

        core.InputBuffer[scrapId] = 20; // 이제서야 스크랩이 코어에 들어옴(제련로가 뒤늦게 산출)

        for (int i = 0; i < 20; i++) system.Tick(0.1f, segments, processors, db);

        Assert.Less(core.InputBuffer[scrapId], 20, "스크랩이 들어온 뒤엔 B가 곧바로 실어 날라야 함");
    }

    private static GameDatabase BuildDatabaseWithTwoInputRecipe(out int oreId, out int scrapId, out int recipeId)
    {
        var ore = new ItemData { itemID = "TestOre" };
        var scrap = new ItemData { itemID = "TestScrap" };

        var recipe = new RecipeData
        {
            recipeID = "TestTwoInputRecipe",
            machineID = "Assembler",
            timeToCraft = 1f,
            inputItems = new List<string> { "TestOre", "TestScrap" },
            outputItems = new List<string>(),
        };

        var db = GameDatabase.Build(new[] { ore, scrap }, System.Array.Empty<MachineData>(), new[] { recipe });
        oreId = db.GetResourceId("TestOre");
        scrapId = db.GetResourceId("TestScrap");
        recipeId = db.GetRecipeId("TestTwoInputRecipe");

        return db;
    }

    private static GameDatabase BuildDatabaseWithRecipe(out int oreId, out int scrapId, out int recipeId)
    {
        var ore = new ItemData { itemID = "TestOre" };
        var scrap = new ItemData { itemID = "TestScrap" };

        var recipe = new RecipeData
        {
            recipeID = "TestRecipe",
            machineID = "Smelter",
            timeToCraft = 1f,
            inputItems = new List<string> { "TestOre" },
            outputItems = new List<string>(),
        };

        var db = GameDatabase.Build(new[] { ore, scrap }, System.Array.Empty<MachineData>(), new[] { recipe });
        oreId = db.GetResourceId("TestOre");
        scrapId = db.GetResourceId("TestScrap");
        recipeId = db.GetRecipeId("TestRecipe");

        return db;
    }

    private static GameDatabase BuildMinimalDatabase(out int resourceId)
    {
        var ore = new ItemData { itemID = "TestOre" };

        var db = GameDatabase.Build(new[] { ore }, System.Array.Empty<MachineData>(), System.Array.Empty<RecipeData>());
        resourceId = db.GetResourceId("TestOre");

        return db;
    }
}
