using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// OZEA 원본 프리팹으로부터 "게임용 프리팹 배리언트"를 만든다. 원본은 절대 안 건드린다.
// 배리언트 루트 트랜스폼에 시작값(90° yaw + footprint 맞춤 스케일 + 바닥 정렬)을 구워두고,
// 이후 방향/크기/높이 조정은 이 배리언트를 에디터에서 눈으로 편집한다(코드 X).
//
// 흐름:
//   1) Tools/Factory/Addressables/1. Generate Variants  ← 이 스크립트
//   2) Tools/Factory/Addressables/2. Setup All           ← 배리언트를 Addressable 로 등록 + JSON 베이크
//   3) Assets/Sin/Prefabs/Machines|Deposits 의 배리언트를 에디터에서 다듬기
public static class MachineVariantGenerator
{
    public const string MachineVariantDir = "Assets/Sin/Prefabs/Machines";
    public const string DepositVariantDir = "Assets/Sin/Prefabs/Deposits";
    public const string ItemVariantDir = "Assets/Sin/Prefabs/Items";

    // 벨트 위 아이템은 칸 footprint가 없다 — 항상 이 절대 크기(최대 변 기준, 월드 단위)로
    // 맞춘다. BeltItemRenderer의 기존 폴백 구 지름(0.25)과 비슷한 눈에 띄는 크기.
    private const float ItemTargetSize = 0.28f;

    public struct ItemEntry
    {
        public string ItemId;      // Bae님 ItemData.itemID
        public string Key;         // Addressables 키 / 배리언트 파일명
        public string OzeaPrefab;  // 원본 OZEA 프리팹 이름(없으면 매핑 안 됨 — 벨트에서 폴백 구+색 유지)
    }

    private static ItemEntry I(string itemId, string key, string ozea) => new ItemEntry { ItemId = itemId, Key = key, OzeaPrefab = ozea };

    public struct Entry
    {
        public string GameId;      // 기계면 machineID("Smelter"), 광맥이면 resourceId("IronOre")
        public string Key;         // Addressables 키 / 배리언트 파일명
        public string OzeaPrefab;  // 원본 OZEA 프리팹 이름
        public int Footprint;      // 칸 크기(스케일 맞춤용)
    }

    private static Entry E(string gameId, string key, string ozea, int footprint)
        => new Entry { GameId = gameId, Key = key, OzeaPrefab = ozea, Footprint = footprint };

    // ── 아트 매핑 단일 소스. 새 모델 추가/교체는 여기만 고치고 1→2 메뉴 다시 실행. ──
    // 기계: GameId = Bae님 machineID. Machines.json 에 그 machineID 항목이 있어야 함.
    public static readonly Entry[] Machines =
    {
        E("Core",        "Prefab_Core",        "SM_Core_Processor", 2),
        // 미니 코어: 메인 코어와 같은 모델을 재사용 — footprint=1이라 자동으로 더 작게 나온다
        // (BakeStartingTransform이 footprint 비례로 스케일을 잡음). "미니" 느낌이 자연스러움.
        E("MiniCore",    "Prefab_MiniCore",    "SM_Core_Processor", 1),
        E("Miner",       "Prefab_Miner",       "SM_Reactor",        1),
        E("Smelter",     "Prefab_Smelter",     "SM_Thermal_T1",     1),
        E("Former",      "Prefab_Former",      "SM_BioChem_T1",     1),
        E("Synthesizer", "Prefab_Synthesizer", "SM_Fusion_T1",      2),
        // 가공기: 제련로(Thermal_T1)/성형기(BioChem_T1)/합성기(Fusion_T1)랑 같은 급(_T1)
        // 시리즈 중 아직 안 쓴 Nuclear_T1 — 최고급 공정(60MW)에 어울리는 존재감.
        // footprint가 3x2(비정사각형)라 이 생성기의 단일 footprint 스케일 계산으론 완벽히
        // 안 맞을 수 있다 — 일단 큰 쪽(3) 기준으로 시작값만 잡고, 배리언트에서 직접 크기/
        // 위치를 마저 맞춘다(다른 배리언트도 다 그렇게 하는 방식).
        E("ProcessingMachine", "Prefab_ProcessingMachine", "SM_Nuclear_T1", 3),
        // 분류기/합류기: OZEA 원본 없이 손으로 만든 평면 Quad 배리언트를 쓴다
        // (Assets/Sin/Prefabs/Machines/Prefab_Splitter.prefab 등). OZEA 이름은 비워둠 → Setup All 이 배리언트 경로만 본다.
        E("Splitter", "Prefab_Splitter", "", 1),
        E("Merger",   "Prefab_Merger",   "", 1),
    };

    // 광맥: GameId = resourceId. OreDepositSpawner 가 "Prefab_Deposit_" + resourceId 로 조회.
    public static readonly Entry[] Deposits =
    {
        E("IronOre",    "Prefab_Deposit_IronOre",    "SM_Iron_Node",    1),
        E("Coal",       "Prefab_Deposit_Coal",       "SM_Coal_Node",    1),
        E("CopperOre",  "Prefab_Deposit_CopperOre",  "SM_Copper_Node",  1),
        E("GoldOre",    "Prefab_Deposit_GoldOre",    "SM_Gold_Node",    1),
        E("QuartzOre",  "Prefab_Deposit_QuartzOre",  "SM_Quartz_Node",  1),
        E("UraniumOre", "Prefab_Deposit_UraniumOre", "SM_Uranium_Node", 1),
        // 콘크리트 노드: 딱 맞는 콘크리트 원석 에셋이 없어 아이템 쪽과 같은 SM_Bauxite_Node 재사용.
        E("Concrete",   "Prefab_Deposit_Concrete",   "SM_Bauxite_Node", 1),
    };

    // 아이템(벨트 위에 놓일 자원): GameId = Bae님 ItemData.itemID. OZEA에 딱 맞는 완제품
    // 모양이 없는 항목은 비슷한 원석류로 대충 대체한다(Concrete = 보크사이트 원석/노드).
    public static readonly ItemEntry[] Items =
    {
        I("IronOre",              "Prefab_Item_IronOre",              "Iron_Ore"),
        I("IronIngot",             "Prefab_Item_IronIngot",            "SM_Iron_Ingot"),
        I("IronPlate",             "Prefab_Item_IronPlate",            "SM_Iron_Plate"),
        I("Coal",                  "Prefab_Item_Coal",                 "Coal_Ore"),
        I("CopperOre",             "Prefab_Item_CopperOre",            "Copper_Ore"),
        I("CopperIngot",           "Prefab_Item_CopperIngot",          "SM_Copper_Ingot"),
        I("CopperPlate",           "Prefab_Item_CopperPlate",          "SM_Copper_Bar"),
        I("CopperWire",            "Prefab_Item_CopperWire",           "SM_Copper_Coil"),
        I("GoldOre",                "Prefab_Item_GoldOre",              "Gold_Ore"),
        I("GoldIngot",              "Prefab_Item_GoldIngot",            "SM_Gold_Ingot"),
        I("QuartzOre",              "Prefab_Item_QuartzOre",            "Quartz_Ore"),
        I("QuartzPlate",            "Prefab_Item_QuartzPlate",          "SM_Quartz_Glass"),
        I("RefinedQuartz",          "Prefab_Item_RefinedQuartz",        "SM_Sorted_Quartz"),
        I("UraniumOre",             "Prefab_Item_UraniumOre",           "Uranium_Ore"),
        I("RefinedUranium",         "Prefab_Item_RefinedUranium",       "SM_Uranium_Container"),
        I("UraniumPlate",           "Prefab_Item_UraniumPlate",         "SM_Uranium_Fuel_Rod"),
        I("Circuit",                "Prefab_Item_Circuit",              "SM_Circuit_Board"),
        I("Gear",                   "Prefab_Item_Gear",                 "SM_Gear"),
        I("EnergyCell",             "Prefab_Item_EnergyCell",           "SM_Energy_Cell"),
        I("HighCapacityBattery",    "Prefab_Item_HighCapacityBattery",  "SM_Battery_T3"),
        I("PlasmaCore",             "Prefab_Item_PlasmaCore",           "SM_Thorium_Core"),
        I("CarbonNanotube",         "Prefab_Item_CarbonNanotube",       "SM_Tube_Bundle"),
        I("IronBeam",               "Prefab_Item_IronBeam",             "SM_Support_Beam"),
        I("PowerCable",             "Prefab_Item_PowerCable",           "SM_Cable_Small_Straight"),
        I("CoalBundle",             "Prefab_Item_CoalBundle",           "SM_Coal_Bricks"),
        I("Concrete",               "Prefab_Item_Concrete",             "SM_Bauxite_Node"),
    };

    // 없는 배리언트만 생성한다. 이미 있는 건 절대 안 건드린다(손으로 다듬어둔 값 보존).
    // 특정 배리언트를 원본 시작값으로 되돌리고 싶으면 그 .prefab 파일을 지우고 다시 실행.
    [MenuItem("Tools/Factory/Addressables/1. Generate Variants")]
    public static void GenerateAll()
    {
        EnsureDir(MachineVariantDir);
        EnsureDir(DepositVariantDir);
        EnsureDir(ItemVariantDir);

        int n = 0;
        foreach (var e in Machines) n += Generate(MachineVariantDir, e.Key, e.OzeaPrefab, e.Footprint) ? 1 : 0;
        foreach (var e in Deposits) n += Generate(DepositVariantDir, e.Key, e.OzeaPrefab, e.Footprint) ? 1 : 0;
        foreach (var e in Items) n += GenerateItem(e.Key, e.OzeaPrefab) ? 1 : 0;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[MachineVariantGenerator] 배리언트 {n}개 새로 생성. 다음: Tools/Factory/Addressables/2. Setup All");
    }

    private static bool Generate(string dir, string key, string ozeaName, int footprint)
    {
        string variantPath = $"{dir}/{key}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
        {
            return false; // 이미 있음 — 건너뜀
        }

        string ozeaPath = FindPrefabPath(ozeaName);
        if (ozeaPath == null)
        {
            Debug.LogWarning($"[MachineVariantGenerator] OZEA 프리팹 '{ozeaName}' 을 못 찾음 — {key} 건너뜀.");
            return false;
        }

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ozeaPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source); // 씬 인스턴스(=원본의 인스턴스)

        BakeStartingTransform(instance, footprint);

        bool ok = false;
        PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out ok); // 원본의 인스턴스라 배리언트로 저장됨
        Object.DestroyImmediate(instance);

        if (ok) Debug.Log($"[MachineVariantGenerator] {key}  <-  {ozeaName}  ({variantPath})");
        else Debug.LogError($"[MachineVariantGenerator] {key} 저장 실패");
        return ok;
    }

    // 아이템 배리언트 생성. 기계/광맥용 Generate()와 거의 같지만 footprint(칸) 개념이 없어서
    // 절대 크기(ItemTargetSize)로 맞추고, 방향(Facing) 개념도 없어서 원본 회전을 그대로 둔다.
    private static bool GenerateItem(string key, string ozeaName)
    {
        string variantPath = $"{ItemVariantDir}/{key}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null) return false; // 이미 있음

        string ozeaPath = FindPrefabPath(ozeaName);
        if (ozeaPath == null)
        {
            Debug.LogWarning($"[MachineVariantGenerator] OZEA 프리팹 '{ozeaName}' 을 못 찾음 — {key} 건너뜀.");
            return false;
        }

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ozeaPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);

        BakeItemStartingTransform(instance);

        bool ok = false;
        PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out ok);
        Object.DestroyImmediate(instance);

        if (ok) Debug.Log($"[MachineVariantGenerator] {key}  <-  {ozeaName}  ({variantPath})");
        else Debug.LogError($"[MachineVariantGenerator] {key} 저장 실패");
        return ok;
    }

    // 최대 변 기준 ItemTargetSize로 맞추고 밑면을 y=0에 둔다. 회전은 원본 그대로(아이템은
    // 기계처럼 놓는 방향이 없어서 90도 yaw를 구울 이유가 없다) — 어색하면 배리언트에서 직접 조정.
    private static void BakeItemStartingTransform(GameObject instance)
    {
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localScale = Vector3.one;

        if (!TryMeasureLocalBounds(instance.transform, out Bounds local))
        {
            Debug.LogWarning($"[MachineVariantGenerator] '{instance.name}' 메시를 못 찾아 스케일 자동조정 생략 — 배리언트에서 수동으로 맞추세요.");
            return;
        }

        float maxDim = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));
        if (maxDim < 0.0001f) return;

        float s = ItemTargetSize / maxDim;
        instance.transform.localScale = new Vector3(s, s, s);
        instance.transform.localPosition = new Vector3(0f, -local.min.y * s, 0f);
    }

    private static readonly Vector3[] Corners =
    {
        new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
        new Vector3(-1, -1, 1),  new Vector3(1, -1, 1),  new Vector3(-1, 1, 1),  new Vector3(1, 1, 1),
    };

    // 배리언트 루트 트랜스폼에 구워두는 시작값. 완벽할 필요 없다 — 사용자가 배리언트 열어 마저 맞춘다.
    // 원본 스케일/회전은 무시하고 새로 잡는다: footprint 칸의 90% 크기, 90° yaw, 밑면 y=0.
    //
    // 크기는 Renderer.bounds(인스턴스 직후엔 갱신 안 됨) 대신 메시 바운드를 루트 로컬 공간으로
    // 직접 합쳐서 잰다 — 그래야 원본이 어떤 스케일이든 일관되게 나온다.
    private static void BakeStartingTransform(GameObject instance, int footprint)
    {
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        if (!TryMeasureLocalBounds(instance.transform, out Bounds local))
        {
            Debug.LogWarning($"[MachineVariantGenerator] '{instance.name}' 메시를 못 찾아 스케일 자동조정 생략 — 배리언트에서 수동으로 맞추세요.");
            instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            return;
        }

        float maxXZ = Mathf.Max(local.size.x, local.size.z);
        if (maxXZ < 0.0001f) return;

        float s = 0.9f * Mathf.Max(1, footprint) / maxXZ;
        instance.transform.localScale = new Vector3(s, s, s);
        instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        instance.transform.localPosition = new Vector3(0f, -local.min.y * s, 0f); // 밑면을 y=0 으로
    }

    // 루트 스케일 1 / 회전 0 / 위치 0 상태에서, 모든 자식 메시의 바운드를 루트 로컬 공간으로 합친다.
    private static bool TryMeasureLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool has = false;

        var meshes = new List<(Mesh mesh, Transform t)>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) meshes.Add((mf.sharedMesh, mf.transform));
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) meshes.Add((smr.sharedMesh, smr.transform));

        foreach (var (mesh, t) in meshes)
        {
            Bounds mb = mesh.bounds;
            Matrix4x4 m = root.worldToLocalMatrix * t.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, Corners[i]));
                if (!has) { bounds = new Bounds(p, Vector3.zero); has = true; }
                else bounds.Encapsulate(p);
            }
        }
        return has;
    }

    private static string FindPrefabPath(string prefabName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{prefabName} t:GameObject"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == prefabName) return path;
        }
        return null;
    }

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureDir(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
