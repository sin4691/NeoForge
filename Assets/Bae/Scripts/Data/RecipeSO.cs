using System.Collections.Generic;
using UnityEngine;
using Bae.Data;

namespace Bae.SO
{
    [CreateAssetMenu(fileName = "NewRecipe", menuName = "Data/Recipe")]
    public class RecipeSO : ScriptableObject
    {
        public string recipeID;
        public string machineID; // 이 레시피를 실행할 기계 ID
        public float timeToCraft;
        public List<string> inputItems;
        public List<string> outputItems;

        public RecipeData ToData()
        {
            return new RecipeData
            {
                recipeID = this.recipeID,
                machineID = this.machineID,
                timeToCraft = this.timeToCraft,
                inputItems = new List<string>(this.inputItems),
                outputItems = new List<string>(this.outputItems)
            };
        }
    }
}
