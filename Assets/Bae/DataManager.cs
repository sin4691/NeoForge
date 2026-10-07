using System.Collections.Generic;
using UnityEngine;
using System.IO;

namespace Bae.Data
{
    // [DefaultExecutionOrder]: Sin 쪽 SimulationDriver.Awake()가 이 데이터를 바로 필요로
    // 하는데, 유니티는 서로 다른 오브젝트의 Awake 순서를 기본적으로 보장 안 해준다 —
    // 그래서 이 Awake가 항상 제일 먼저 돌도록 순서만 강제한다(동작/로직은 그대로).
    [DefaultExecutionOrder(-1000)]
    public class DataManager : MonoBehaviour
    {
        public static DataManager Instance { get; private set; }

        // 빠른 검색을 위한 딕셔너리
        public Dictionary<string, ItemData> itemDict = new Dictionary<string, ItemData>();
        public Dictionary<string, MachineData> machineDict = new Dictionary<string, MachineData>();
        public Dictionary<string, RecipeData> recipeDict = new Dictionary<string, RecipeData>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LoadAllData();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        // 게임 시작 시 JSON 데이터를 읽어와서 딕셔너리에 세팅합니다.
        public void LoadAllData()
        {
            LoadItems();
            LoadMachines();
            LoadRecipes();
            Debug.Log("모든 JSON 데이터 로드 완료!");
        }

        private string GetJsonText(string fileName)
        {
            // 1. 모바일 기기(또는 PC)에서 접근 가능한 외부 폴더 경로
            string externalPath = Path.Combine(Application.persistentDataPath, fileName + ".json");

#if !UNITY_EDITOR
            // 2. 외부 폴더에 수정된 JSON 파일이 존재하면 그것을 최우선으로 읽어옵니다. (실시간 수정 기능)
            // 에디터에서는 이 분기를 아예 타지 않는다 — "사람이 일부러 넣어둔 패치"와
            // "앱이 3번에서 스스로 구워낸 캐시"를 File.Exists만으로는 구분할 수 없어서,
            // 한 번 캐시가 생기면 그 뒤로 Resources 원본(=Assets/Bae/Resources/JSON)을
            // 아무리 고쳐도 계속 옛날 캐시만 읽히는 문제가 있었다. 이 실시간 패치 기능은
            // 빌드된 앱에서만 의미가 있으므로 에디터에서는 완전히 끈다.
            if (File.Exists(externalPath))
            {
                return File.ReadAllText(externalPath);
            }
#endif

            // 3. 외부에 파일이 없다면, 앱(APK) 내부에 빌드된 기본 데이터를 읽어옵니다.
            TextAsset jsonAsset = Resources.Load<TextAsset>("JSON/" + fileName);
            if (jsonAsset != null)
            {
#if !UNITY_EDITOR
                // 다음번엔 외부에서 수정할 수 있도록 복사본을 밖으로 빼둡니다.
                File.WriteAllText(externalPath, jsonAsset.text);
#endif
                return jsonAsset.text;
            }

            Debug.LogError($"JSON 데이터를 찾을 수 없습니다: {fileName}");
            return null;
        }

        private void LoadItems()
        {
            string jsonText = GetJsonText("Items");
            if (!string.IsNullOrEmpty(jsonText))
            {
                ItemDatabase db = JsonUtility.FromJson<ItemDatabase>(jsonText);
                foreach (var item in db.items) itemDict[item.itemID] = item;
            }
        }

        private void LoadMachines()
        {
            string jsonText = GetJsonText("Machines");
            if (!string.IsNullOrEmpty(jsonText))
            {
                MachineDatabase db = JsonUtility.FromJson<MachineDatabase>(jsonText);
                foreach (var machine in db.machines) machineDict[machine.machineID] = machine;
                RestoreMissingBuildCosts();
            }
        }

        // Older installed builds exported machines without costs. Keep external data,
        // but fill missing costs before SimulationDriver constructs its database.
        private void RestoreMissingBuildCosts()
        {
            TextAsset bundledJson = Resources.Load<TextAsset>("JSON/Machines");
            if (bundledJson == null) return;
            MachineDatabase bundled = JsonUtility.FromJson<MachineDatabase>(bundledJson.text);
            if (bundled?.machines == null) return;

            foreach (var baseline in bundled.machines)
            {
                if (baseline.buildCostItems == null || baseline.buildCostItems.Count == 0) continue;
                if (!machineDict.TryGetValue(baseline.machineID, out var loaded)) continue;
                if (loaded.buildCostItems != null && loaded.buildCostItems.Count > 0) continue;

                loaded.buildCostItems = new List<string>(baseline.buildCostItems);
                Debug.LogWarning($"[DataManager] {baseline.machineID}: 외부 JSON에 누락된 설치 비용을 내장 데이터로 복원했습니다.");
            }
        }

        private void LoadRecipes()
        {
            string jsonText = GetJsonText("Recipes");
            if (!string.IsNullOrEmpty(jsonText))
            {
                RecipeDatabase db = JsonUtility.FromJson<RecipeDatabase>(jsonText);
                foreach (var recipe in db.recipes) recipeDict[recipe.recipeID] = recipe;
            }
        }

        // 특정 기계(machineID)에서 사용 가능한 모든 레시피를 가져오는 헬퍼 함수
        public List<RecipeData> GetRecipesForMachine(string machineID)
        {
            List<RecipeData> availableRecipes = new List<RecipeData>();
            foreach (var recipe in recipeDict.Values)
            {
                if (recipe.machineID == machineID)
                {
                    availableRecipes.Add(recipe);
                }
            }
            return availableRecipes;
        }
    }
}
