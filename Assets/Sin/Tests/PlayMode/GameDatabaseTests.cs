using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using NUnit.Framework;

public class GameDatabaseTests
{
    [Test]
    public void Build_AssignsStableIdsAndResolvesRecipeReferences()
    {
        var ore = new ItemData { itemID = "IronOre", itemName = "Iron Ore" };
        var plate = new ItemData { itemID = "IronPlate", itemName = "Iron Plate" };
        var machine = new MachineData { machineID = "Smelter", gridWidth = 1, gridHeight = 1 };
        var recipe = new RecipeData
        {
            recipeID = "SmeltIron",
            machineID = "Smelter",
            timeToCraft = 1f,
            inputItems = new List<string> { "IronOre" },
            outputItems = new List<string> { "IronPlate" },
        };

        var db = GameDatabase.Build(new[] { ore, plate }, new[] { machine }, new[] { recipe });

        Assert.AreEqual(2, db.ResourceCount);
        Assert.AreEqual(1, db.Recipes.Count);
        Assert.AreEqual(1, db.Machines.Count);

        int ironOreId = db.GetResourceId("IronOre");
        int ironPlateId = db.GetResourceId("IronPlate");
        int recipeId = db.GetRecipeId("SmeltIron");

        var recipeRuntime = db.Recipes[recipeId];
        Assert.AreEqual(ironOreId, recipeRuntime.Inputs[0].ResourceId);
        Assert.AreEqual(ironPlateId, recipeRuntime.Outputs[0].ResourceId);
        Assert.AreEqual("Smelter", db.Machines[db.GetMachineId("Smelter")].Key);
        Assert.AreEqual("Smelter", recipeRuntime.RequiredMachineId);
    }

    [Test]
    public void Build_GroupsRepeatedInputItems_IntoResourceAmounts()
    {
        // Bae님 RecipeData.inputItems는 "IronOre"를 필요한 개수만큼 반복하는 문자열 리스트다
        // (제 ResourceAmount[]와 형식이 다름) — GameDatabase.Build가 이걸 개수로 묶어내야 한다.
        var ore = new ItemData { itemID = "IronOre" };
        var plate = new ItemData { itemID = "IronPlate" };
        var recipe = new RecipeData
        {
            recipeID = "SmeltIron",
            machineID = "Smelter",
            timeToCraft = 1f,
            inputItems = new List<string> { "IronOre", "IronOre" },
            outputItems = new List<string> { "IronPlate" },
        };

        var db = GameDatabase.Build(new[] { ore, plate }, System.Array.Empty<MachineData>(), new[] { recipe });

        var recipeRuntime = db.Recipes[db.GetRecipeId("SmeltIron")];
        Assert.AreEqual(1, recipeRuntime.Inputs.Length, "같은 자원이 반복되면 하나로 묶여야 함");
        Assert.AreEqual(2, recipeRuntime.Inputs[0].Amount);
    }
}
