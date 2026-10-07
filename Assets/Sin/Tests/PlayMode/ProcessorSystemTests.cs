using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;

public class ProcessorSystemTests
{
    [Test]
    public void Tick_ConsumesInputsImmediately_AndProducesOutputsAfterProcessTime()
    {
        var db = BuildFixtureDatabase(out int oreId, out int plateId, out int recipeId);
        var processor = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId };
        processor.InputBuffer[oreId] = 2;

        var system = new ProcessorSystem();

        var processors = new List<ProcessorInstance> { processor };

        system.Tick(0.5f, db, processors);
        Assert.IsTrue(processor.IsProcessing);
        Assert.AreEqual(0, processor.InputBuffer[oreId], "투입은 처리 시작 시점에 즉시 소모되어야 함");
        Assert.AreEqual(0, processor.OutputBuffer[plateId]);

        system.Tick(0.6f, db, processors); // 누적 1.1s >= processSeconds(1s)

        Assert.IsFalse(processor.IsProcessing);
        Assert.AreEqual(1, processor.OutputBuffer[plateId]);
    }

    [Test]
    public void Tick_DoesNotStart_WhenInputsInsufficient()
    {
        var db = BuildFixtureDatabase(out int oreId, out _, out int recipeId);
        var processor = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId };
        processor.InputBuffer[oreId] = 1; // 레시피는 2개 필요

        var system = new ProcessorSystem();
        system.Tick(1f, db, new List<ProcessorInstance> { processor });

        Assert.IsFalse(processor.IsProcessing);
        Assert.AreEqual(1, processor.InputBuffer[oreId]);
    }

    [Test]
    public void Tick_StopsConsumingInputs_WhenOutputBufferIsFull()
    {
        // 사용자 보고: 출력 연결이 없거나 막혀 OutputBuffer가 꽉 찼는데도 계속 가공해서
        // 입력만 소비되고 산출물은 Capacity에서 잘려 증발했다.
        var db = BuildFixtureDatabase(out int oreId, out int plateId, out int recipeId);
        var processor = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId, Capacity = 5 };
        processor.InputBuffer[oreId] = 100;
        processor.OutputBuffer[plateId] = 5; // 이미 꽉 참

        var system = new ProcessorSystem();
        var processors = new List<ProcessorInstance> { processor };
        for (int i = 0; i < 50; i++) system.Tick(1f, db, processors);

        Assert.AreEqual(100, processor.InputBuffer[oreId], "출력이 꽉 찼으면 입력을 소비하지 않아야 함");
        Assert.AreEqual(5, processor.OutputBuffer[plateId], "Capacity를 넘겨 증발하는 산출물이 없어야 함");
        Assert.IsFalse(processor.IsProcessing);

        // 출력을 비워주면 다시 정상 가공.
        processor.OutputBuffer[plateId] = 0;
        for (int i = 0; i < 3; i++) system.Tick(1f, db, processors);
        Assert.Greater(processor.OutputBuffer[plateId], 0, "출력 자리가 나면 다시 가공해야 함");
        Assert.Less(processor.InputBuffer[oreId], 100);
    }

    private static GameDatabase BuildFixtureDatabase(out int oreId, out int plateId, out int recipeId)
    {
        var ore = new ItemData { itemID = "IronOre" };
        var plate = new ItemData { itemID = "IronPlate" };
        var machine = new MachineData { machineID = "Smelter" };
        var recipe = new RecipeData
        {
            recipeID = "SmeltIron",
            machineID = "Smelter",
            timeToCraft = 1f,
            inputItems = new List<string> { "IronOre", "IronOre" },
            outputItems = new List<string> { "IronPlate" },
        };

        var db = GameDatabase.Build(new[] { ore, plate }, new[] { machine }, new[] { recipe });
        oreId = db.GetResourceId("IronOre");
        plateId = db.GetResourceId("IronPlate");
        recipeId = db.GetRecipeId("SmeltIron");

        return db;
    }
}
