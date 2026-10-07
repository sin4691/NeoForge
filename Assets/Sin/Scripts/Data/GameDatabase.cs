using System;
using System.Collections.Generic;
using System.Linq;
using Bae.Data;
using UnityEngine;

namespace Factory.Data
{
    public readonly struct ResourceRuntime
    {
        public readonly string Key;
        public readonly string DisplayName;
        public readonly Color Color;
        // Addressables 키(예: "Prefab_Item_CopperOre"). 비어 있으면 벨트에서 폴백(구+색)으로 그린다.
        public readonly string PrefabName;

        public ResourceRuntime(string key, string displayName, Color color, string prefabName)
        {
            Key = key;
            DisplayName = displayName;
            Color = color;
            PrefabName = prefabName;
        }
    }

    public readonly struct RecipeRuntime
    {
        public readonly string Key;
        public readonly ResourceAmount[] Inputs;
        public readonly ResourceAmount[] Outputs;
        public readonly float ProcessSeconds;
        // Bae님의 RecipeData.machineID를 그대로 쓴다 — enum 카테고리로 감싸면 새 기계 종류를
        // 추가할 때마다 이 enum에 값을 추가하는 코드 수정이 필요해져서, "데이터만 추가하면
        // 끝나야 한다"는 요구사항과 어긋난다.
        public readonly string RequiredMachineId;

        public RecipeRuntime(string key, ResourceAmount[] inputs, ResourceAmount[] outputs, float processSeconds, string requiredMachineId)
        {
            Key = key;
            Inputs = inputs;
            Outputs = outputs;
            ProcessSeconds = processSeconds;
            RequiredMachineId = requiredMachineId;
        }
    }

    public readonly struct MachineRuntime
    {
        public readonly string Key;
        public readonly Vector2Int Footprint;
        // Addressables 키(예: "Prefab_Smelter"). 비어 있으면 폴백 박스로 그린다.
        public readonly string PrefabName;
        // 설치(배치)에 드는 자원 — 코어에서 이만큼 차감해야 지을 수 있다. 비어있으면 무료.
        public readonly ResourceAmount[] BuildCost;

        public MachineRuntime(string key, Vector2Int footprint, string prefabName, ResourceAmount[] buildCost)
        {
            Key = key;
            Footprint = footprint;
            BuildCost = buildCost;
            PrefabName = prefabName;
        }
    }

    // 땅 위 광물 노드 하나의 런타임 정보 — 채굴기는 이 노드 위에 지어져야만 생기고, 이 노드의
    // 자원/속도/산출량을 그대로 물려받는다(OreDepositDef 참고). 지형은 Bae님 데이터 모델
    // 밖이라(팀 확인 완료) 여전히 제 ScriptableObject로 관리한다.
    public readonly struct OreDepositRuntime
    {
        public readonly string Key;
        public readonly int ResourceId;
        public readonly float MineIntervalSeconds;
        public readonly int YieldPerCycle;

        public OreDepositRuntime(string key, int resourceId, float mineIntervalSeconds, int yieldPerCycle)
        {
            Key = key;
            ResourceId = resourceId;
            MineIntervalSeconds = mineIntervalSeconds;
            YieldPerCycle = yieldPerCycle;
        }
    }

    // 레시피/자원/기계 정의 데이터베이스. 아이템/기계/레시피는 Bae님의 DataManager(JSON에서
    // 읽은 Dictionary)에서 가져오고, 광물 노드만 예외적으로 제 ScriptableObject를 쓴다.
    // 틱 루프는 이 클래스의 배열만 인덱싱하고, DataManager/ScriptableObject는 로드 시점에만 쓰인다.
    public sealed class GameDatabase
    {
        public IReadOnlyList<ResourceRuntime> Resources => resources;
        public IReadOnlyList<RecipeRuntime> Recipes => recipes;
        public IReadOnlyList<MachineRuntime> Machines => machines;
        public IReadOnlyList<OreDepositRuntime> OreDeposits => oreDeposits;

        public int ResourceCount => resources.Length;

        private readonly ResourceRuntime[] resources;
        private readonly RecipeRuntime[] recipes;
        private readonly MachineRuntime[] machines;
        private readonly OreDepositRuntime[] oreDeposits;

        private readonly Dictionary<string, int> resourceIdByKey;
        private readonly Dictionary<string, int> recipeIdByKey;
        private readonly Dictionary<string, int> machineIdByKey;
        private readonly Dictionary<string, int> oreDepositIdByKey;

        private GameDatabase(ResourceRuntime[] resources, RecipeRuntime[] recipes, MachineRuntime[] machines, OreDepositRuntime[] oreDeposits,
            Dictionary<string, int> resourceIdByKey, Dictionary<string, int> recipeIdByKey, Dictionary<string, int> machineIdByKey, Dictionary<string, int> oreDepositIdByKey)
        {
            this.resources = resources;
            this.recipes = recipes;
            this.machines = machines;
            this.oreDeposits = oreDeposits;
            this.resourceIdByKey = resourceIdByKey;
            this.recipeIdByKey = recipeIdByKey;
            this.machineIdByKey = machineIdByKey;
            this.oreDepositIdByKey = oreDepositIdByKey;
        }

        public int GetResourceId(string key) => resourceIdByKey[key];
        public int GetRecipeId(string key) => recipeIdByKey[key];
        public int GetMachineId(string key) => machineIdByKey[key];
        public int GetOreDepositId(string key) => oreDepositIdByKey[key];

        public bool TryGetResourceId(string key, out int id) => resourceIdByKey.TryGetValue(key, out id);
        public bool TryGetRecipeId(string key, out int id) => recipeIdByKey.TryGetValue(key, out id);
        public bool TryGetMachineId(string key, out int id) => machineIdByKey.TryGetValue(key, out id);
        public bool TryGetOreDepositId(string key, out int id) => oreDepositIdByKey.TryGetValue(key, out id);

        // 레시피 선택 UI에서 이 기계(machineId)로 고를 수 있는 레시피 목록을 보여줄 때 쓴다.
        public List<int> GetRecipeIdsForMachine(string machineId)
        {
            var result = new List<int>();
            for (int i = 0; i < recipes.Length; i++)
            {
                if (recipes[i].RequiredMachineId == machineId) result.Add(i);
            }
            return result;
        }

        // 실제 게임이 쓰는 경로: Bae님의 DataManager(JSON 로드 완료된 싱글톤)에서 아이템/기계/
        // 레시피를 읽어온다. 광물 노드(OreDepositDef)만 Resources 폴더에서 직접 읽는다 —
        // 지형은 Bae님 데이터 모델이 다루지 않기로 팀에서 확인된 부분이라 그대로 유지.
        public static GameDatabase LoadFromBaeData(DataManager dataManager, string oreDepositRootPath = "GameData/OreDeposits")
        {
            var oreDepositDefs = UnityEngine.Resources.LoadAll<OreDepositDef>(oreDepositRootPath);
            return Build(dataManager.itemDict.Values, dataManager.machineDict.Values, dataManager.recipeDict.Values, oreDepositDefs);
        }

        public static GameDatabase Build(IEnumerable<ItemData> items, IEnumerable<MachineData> machineDataList, IEnumerable<RecipeData> recipeDataList, OreDepositDef[] oreDepositDefs = null)
        {
            oreDepositDefs ??= Array.Empty<OreDepositDef>();

            // 로드 순서는 보장 안 되므로 id 문자열로 정렬해 실행마다 동일한 int id가 배정되도록
            // 한다 (결정적 동작, 저장 데이터 안정성).
            var (resources, resourceIdByKey) = BuildIndexed(
                items, i => i.itemID,
                def => new ResourceRuntime(def.itemID, def.itemName, ColorFromKey(def.itemID), def.prefabName));

            var (machines, machineIdByKey) = BuildIndexed(
                machineDataList, m => m.machineID,
                def => new MachineRuntime(def.machineID, new Vector2Int(Mathf.Max(1, def.gridWidth), Mathf.Max(1, def.gridHeight)), def.prefabName,
                    ResolveIngredients(def.buildCostItems, resourceIdByKey)));

            // 레시피/광물노드는 resourceIdByKey가 먼저 완성되어 있어야 재료를 id로 풀 수 있다.
            var (recipes, recipeIdByKey) = BuildIndexed(
                recipeDataList, r => r.recipeID,
                def => new RecipeRuntime(
                    def.recipeID,
                    ResolveIngredients(def.inputItems, resourceIdByKey),
                    ResolveIngredients(def.outputItems, resourceIdByKey),
                    def.timeToCraft,
                    def.machineID));

            // resourceId가 비어있거나(옛 필드에서 이름이 바뀌면서 값이 안 옮겨진 애셋 등)
            // resourceIdByKey에 없는 값을 가리키면 게임 전체가 못 뜨는 대신, 그 노드만 건너뛰고
            // 경고를 남긴다 — 애셋 하나 잘못됐다고 시작조차 못 하는 것보단 낫다.
            var validOreDepositDefs = new List<OreDepositDef>();
            for (int i = 0; i < oreDepositDefs.Length; i++)
            {
                var def = oreDepositDefs[i];
                if (string.IsNullOrEmpty(def.resourceId) || !resourceIdByKey.ContainsKey(def.resourceId))
                {
                    Debug.LogWarning($"[GameDatabase] OreDepositDef '{def.depositId}'의 resourceId('{def.resourceId}')가 " +
                        "비어있거나 존재하지 않는 아이템을 가리켜서 건너뜁니다 — 애셋을 다시 확인하세요.");
                    continue;
                }
                validOreDepositDefs.Add(def);
            }

            var (oreDeposits, oreDepositIdByKey) = BuildIndexed(
                validOreDepositDefs, d => d.depositId,
                def => new OreDepositRuntime(def.depositId, resourceIdByKey[def.resourceId], def.mineIntervalSeconds, def.yieldPerCycle));

            return new GameDatabase(resources, recipes, machines, oreDeposits, resourceIdByKey, recipeIdByKey, machineIdByKey, oreDepositIdByKey);
        }

        // id 문자열로 정렬 -> 배열 인덱스를 그 정렬 순서로 배정 -> 키→id 사전을 함께 만드는,
        // 자원/기계/레시피/광물노드 네 가지에서 반복되던 패턴 하나로 통합.
        private static (TRuntime[] runtime, Dictionary<string, int> idByKey) BuildIndexed<TDef, TRuntime>(
            IEnumerable<TDef> defs, Func<TDef, string> keyOf, Func<TDef, TRuntime> makeRuntime)
        {
            var sorted = defs.OrderBy(keyOf, StringComparer.Ordinal).ToArray();
            var idByKey = new Dictionary<string, int>(sorted.Length);
            var runtime = new TRuntime[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                idByKey[keyOf(sorted[i])] = i;
                runtime[i] = makeRuntime(sorted[i]);
            }
            return (runtime, idByKey);
        }

        // Bae님의 inputItems/outputItems는 "IronOre","IronOre"처럼 자원 id를 필요한 개수만큼
        // 반복하는 문자열 리스트다(제 ResourceAmount[]와 형식이 다름) — 같은 id를 묶어서 개수를 센다.
        private static ResourceAmount[] ResolveIngredients(List<string> itemIds, Dictionary<string, int> resourceIdByKey)
        {
            if (itemIds == null || itemIds.Count == 0) return Array.Empty<ResourceAmount>();

            var order = new List<string>(); // 최초 등장 순서 보존(결정적 출력).
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < itemIds.Count; i++)
            {
                string id = itemIds[i];
                if (!counts.ContainsKey(id))
                {
                    counts[id] = 0;
                    order.Add(id);
                }
                counts[id]++;
            }

            var resolved = new ResourceAmount[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                resolved[i] = new ResourceAmount(resourceIdByKey[order[i]], counts[order[i]]);
            }
            return resolved;
        }

        // Bae님 ItemData엔 색상 필드가 없다(아이콘/모델은 Addressables 붙기 전이라 아직 못 씀) —
        // 벨트 위 아이템을 구분해 보여줄 임시 색이 필요하다. 처음엔 문자열 해시로 아무 색이나
        // 뽑아냈는데, 그러다 보니 석탄처럼 우연히 벨트 색(짙은 회색)이랑 비슷한 색이 나와서
        // 눈에 안 띄는 문제가 실제로 생겼다 — 그래서 지금 쓰는 아이템은 알아보기 쉬운 색을
        // 직접 정하고, 목록에 없는(나중에 추가될) 아이템만 해시로 대충 정한다.
        private static readonly Dictionary<string, Color> KnownItemColors = new Dictionary<string, Color>
        {
            { "IronOre", new Color(0.55f, 0.38f, 0.28f) },   // 철광석: 붉은 갈색
            { "IronIngot", new Color(0.85f, 0.85f, 0.88f) }, // 철 주괴: 밝은 은회색(벨트의 짙은 회색과 대비되게)
            { "IronPlate", new Color(0.65f, 0.72f, 0.8f) },  // 철판: 옅은 청회색
            { "SteelIngot", new Color(0.32f, 0.36f, 0.44f) },// 강철 주괴: 짙은 청회색(철 주괴보다 어둡게 — 합금이라 티나게)
            { "Coal", new Color(0.95f, 0.35f, 0.1f) },       // 석탄: 실제 색(검정)은 벨트와 안 구분되니, 대신 눈에 띄는 주황으로
        };

        private static Color ColorFromKey(string key)
        {
            if (KnownItemColors.TryGetValue(key, out var known)) return known;

            int hash = 0;
            unchecked
            {
                for (int i = 0; i < key.Length; i++) hash = hash * 31 + key[i];
            }
            float hue = (Mathf.Abs(hash) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.65f, 0.9f);
        }
    }
}
