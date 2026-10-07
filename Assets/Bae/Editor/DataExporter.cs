using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using Bae.SO;
using Bae.Data;

namespace Bae.EditorScripts
{
    public class DataExporter : Editor
    {
        [MenuItem("Tools/Data/Bake All SO to JSON")]
        public static void BakeDataToJSON()
        {
            // 1. 저장할 경로 생성 (Resources/JSON)
            string savePath = Application.dataPath + "/Bae/Resources/JSON";
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            // 2. 모든 SO 찾아오기 및 변환
            BakeItems(savePath);
            BakeMachines(savePath);
            BakeRecipes(savePath);

            AssetDatabase.Refresh(); // 에디터 새로고침 (파일 즉시 보이게)
            Debug.Log("<color=green>모든 데이터가 JSON으로 성공적으로 구워졌습니다!</color>");
        }

        private static void BakeItems(string savePath)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemSO");
            ItemDatabase db = new ItemDatabase();

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                ItemSO so = AssetDatabase.LoadAssetAtPath<ItemSO>(assetPath);
                if (so != null)
                {
                    db.items.Add(so.ToData());
                }
            }

            string json = JsonUtility.ToJson(db, true); // true = Pretty Print (줄바꿈)
            File.WriteAllText(savePath + "/Items.json", json);
            Debug.Log($"Items.json 구우기 완료 ({db.items.Count}개 아이템)");
        }

        private static void BakeMachines(string savePath)
        {
            string[] guids = AssetDatabase.FindAssets("t:MachineSO");
            MachineDatabase db = new MachineDatabase();

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                MachineSO so = AssetDatabase.LoadAssetAtPath<MachineSO>(assetPath);
                if (so != null)
                {
                    db.machines.Add(so.ToData());
                }
            }

            string json = JsonUtility.ToJson(db, true);
            File.WriteAllText(savePath + "/Machines.json", json);
            Debug.Log($"Machines.json 구우기 완료 ({db.machines.Count}개 기계)");
        }

        private static void BakeRecipes(string savePath)
        {
            string[] guids = AssetDatabase.FindAssets("t:RecipeSO");
            RecipeDatabase db = new RecipeDatabase();

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                RecipeSO so = AssetDatabase.LoadAssetAtPath<RecipeSO>(assetPath);
                if (so != null)
                {
                    db.recipes.Add(so.ToData());
                }
            }

            string json = JsonUtility.ToJson(db, true);
            File.WriteAllText(savePath + "/Recipes.json", json);
            Debug.Log($"Recipes.json 구우기 완료 ({db.recipes.Count}개 레시피)");
        }
    }
}
