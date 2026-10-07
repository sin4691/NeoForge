using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

// 채굴기는 입출력 포트가 없다 — 캔 자원은 벨트 없이 코어(UniversalPorts 켜진 Processor)로
// 곧바로 "원격 전송"된다. MinerSystem이 그 배송 자체를 담당한다.
public class MinerSystemTests
{
    [Test]
    public void MinedOutput_DeliversDirectlyToCore_NoBeltNeeded()
    {
        var db = BuildMinimalDatabase(out int resourceId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        var processors = new List<ProcessorInstance> { core };
        var miner = new MinerInstance { OutputResourceId = resourceId, MineIntervalSeconds = 1f };
        var miners = new List<MinerInstance> { miner };

        var system = new MinerSystem();
        for (int i = 0; i < 10; i++) system.Tick(1f, miners, processors, coreProcessorIndex: 0, database: db);

        Assert.AreEqual(0, miner.BufferedOutput, "채굴한 만큼 매번 코어로 바로 빠져나가야 함");
        Assert.AreEqual(10, core.InputBuffer[resourceId]);
    }

    [Test]
    public void CoreFull_MinerKeepsBuffering_ThenFlushesOnceSpaceFrees()
    {
        var db = BuildMinimalDatabase(out int resourceId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[resourceId] = SimulationConstants.ResourceBufferCapacity; // 이미 가득 참
        var processors = new List<ProcessorInstance> { core };
        var miner = new MinerInstance { OutputResourceId = resourceId, BufferedOutput = 3, MineIntervalSeconds = float.MaxValue };
        var miners = new List<MinerInstance> { miner };

        var system = new MinerSystem();
        system.Tick(0.1f, miners, processors, coreProcessorIndex: 0, database: db);

        Assert.AreEqual(3, miner.BufferedOutput, "코어가 가득 차 있으면 잃지 않고 채굴기 쪽에 대기해야 함");

        core.InputBuffer[resourceId] -= 2; // 공간이 생기면
        system.Tick(0.1f, miners, processors, coreProcessorIndex: 0, database: db);

        Assert.AreEqual(1, miner.BufferedOutput, "생긴 공간만큼만 흘러들어가야 함");
        Assert.AreEqual(SimulationConstants.ResourceBufferCapacity, core.InputBuffer[resourceId]);
    }

    [Test]
    public void NoCoreRegistered_MinerJustBuffers_NoExceptionNoLoss()
    {
        var db = BuildMinimalDatabase(out int resourceId);
        var processors = new List<ProcessorInstance>();
        var miner = new MinerInstance { OutputResourceId = resourceId, MineIntervalSeconds = 1f };
        var miners = new List<MinerInstance> { miner };

        var system = new MinerSystem();
        Assert.DoesNotThrow(() => system.Tick(3f, miners, processors, coreProcessorIndex: -1, database: db));

        Assert.AreEqual(3, miner.BufferedOutput, "코어가 아직 없으면 잃지 않고 채굴기에 쌓여 있어야 함");
    }

    [Test]
    public void OreDepositIdSet_MinerAlwaysResyncsToCurrentDepositValues_NotStaleCachedOnes()
    {
        // 사용자 보고: 매장지 애셋(mineIntervalSeconds/yieldPerCycle)을 밸런스 패치해도, 건설
        // 시점에 값을 그대로 복사해서 굳혀버리던 예전 방식 때문에 이미 지어진(또는 세이브에서
        // 복원된) 채굴기는 계속 옛 속도로 남았다. 이제 OreDepositId만 있으면 매 틱 최신 정의로
        // 다시 채운다 — 채굴기 인스턴스에 남아있는 낡은 캐시값(여기선 일부러 틀리게 넣어둠)은
        // 무시되고 데이터베이스의 "지금" 값을 따라가야 한다.
        var db = BuildMinimalDatabase(out int resourceId, out int depositId, mineIntervalSeconds: 1f, yieldPerCycle: 5);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        var processors = new List<ProcessorInstance> { core };
        var miner = new MinerInstance
        {
            OutputResourceId = resourceId,
            OreDepositId = depositId,
            MineIntervalSeconds = 999f, // 낡은/잘못된 캐시값 — resync가 안 되면 이 값이 그대로 써짐.
            YieldPerCycle = 1,
        };
        var miners = new List<MinerInstance> { miner };

        var system = new MinerSystem();
        system.Tick(1f, miners, processors, coreProcessorIndex: 0, database: db);

        Assert.AreEqual(1f, miner.MineIntervalSeconds, "캐시값이 아니라 매장지 정의(현재 값)로 갱신되어야 함");
        Assert.AreEqual(5, miner.YieldPerCycle, "캐시값이 아니라 매장지 정의(현재 값)로 갱신되어야 함");
        Assert.AreEqual(5, core.InputBuffer[resourceId], "1초 만에 사이클 하나(산출 5개)가 돌아야 함(옛 999초 간격이 아니라)");
    }

    private static GameDatabase BuildMinimalDatabase(out int resourceId)
    {
        var ore = new ItemData { itemID = "TestOre" };

        var db = GameDatabase.Build(new[] { ore }, System.Array.Empty<MachineData>(), System.Array.Empty<RecipeData>());
        resourceId = db.GetResourceId("TestOre");

        return db;
    }

    private static GameDatabase BuildMinimalDatabase(out int resourceId, out int depositId, float mineIntervalSeconds, int yieldPerCycle)
    {
        var ore = new ItemData { itemID = "TestOre" };

        var deposit = ScriptableObject.CreateInstance<OreDepositDef>();
        deposit.depositId = "TestDeposit";
        deposit.resourceId = "TestOre";
        deposit.mineIntervalSeconds = mineIntervalSeconds;
        deposit.yieldPerCycle = yieldPerCycle;

        var db = GameDatabase.Build(new[] { ore }, System.Array.Empty<MachineData>(), System.Array.Empty<RecipeData>(),
            new[] { deposit });
        resourceId = db.GetResourceId("TestOre");
        depositId = db.GetOreDepositId("TestDeposit");

        return db;
    }
}
