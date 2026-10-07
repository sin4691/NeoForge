using System.Collections.Generic;
using Bae.EditorScripts;
using Bae.SO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

// 기계/아이템/광맥 모델을 Addressables로 붙이는 셋업. MachineVariantGenerator(1단계) 다음에 실행.
//  - Tools/Factory/Addressables/2. Setup All:
//    1) Addressables 설정/그룹(Machines/Items/Deposits) 생성 (없으면)
//    2) 각 키에 프리팹 등록 — Assets/Sin/Prefabs 의 배리언트 우선, 없으면 OZEA 원본
//    3) MachineSO / ItemSO 의 prefabName 을 그 키로 채우고 JSON 재베이크
//    4) Play Mode 데이터 빌더를 "Use Asset Database" 로 (콘텐츠 빌드 불필요)
//
// 기계/광맥/아이템 아트 매핑은 MachineVariantGenerator.Machines/Deposits/Items 가 단일 소스 —
// 새 모델 추가/교체는 거기만 고치고 1→2 메뉴 다시 실행.
public static class AddressablesSetup
{
    private const string MachineGroup = "Machines";
    private const string ItemGroup = "Items";
    private const string DepositGroup = "Deposits";

    [MenuItem("Tools/Factory/Addressables/2. Setup All")]
    public static void SetupAll()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null)
        {
            Debug.LogError("[AddressablesSetup] Addressables 설정을 만들 수 없습니다.");
            return;
        }

        AddressableAssetGroup machineGroup = EnsureGroup(settings, MachineGroup);
        AddressableAssetGroup itemGroup = EnsureGroup(settings, ItemGroup);
        AddressableAssetGroup depositGroup = EnsureGroup(settings, DepositGroup);

        int marked = 0;
        foreach (var e in MachineVariantGenerator.Machines)
            marked += Mark(settings, machineGroup, e.Key, e.OzeaPrefab, MachineVariantGenerator.MachineVariantDir);
        foreach (var e in MachineVariantGenerator.Deposits)
            marked += Mark(settings, depositGroup, e.Key, e.OzeaPrefab, MachineVariantGenerator.DepositVariantDir);
        foreach (var e in MachineVariantGenerator.Items)
            marked += Mark(settings, itemGroup, e.Key, e.OzeaPrefab, MachineVariantGenerator.ItemVariantDir);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);

        int filled = 0;
        filled += FillMachinePrefabNames();
        filled += FillItemPrefabNames();

        // 에디터에서 콘텐츠 빌드 없이 바로 로드되도록.
        SetUseAssetDatabasePlayMode(settings);

        AssetDatabase.SaveAssets();

        // 런타임은 Machines.json/Items.json 을 읽으므로(SO 직접 X), prefabName 을 채웠으면 다시 구워야 한다.
        DataExporter.BakeDataToJSON();

        Debug.Log($"[AddressablesSetup] 완료 — Addressable 등록 {marked}개, prefabName 채움 {filled}개, JSON 재생성. " +
                  "Play Mode Script = Use Asset Database (콘텐츠 빌드 불필요).");
    }

    private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string name)
    {
        AddressableAssetGroup group = settings.FindGroup(name);
        if (group != null) return group;

        group = settings.CreateGroup(name, setAsDefaultGroup: false, readOnly: false, postEvent: true,
            schemasToCopy: null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        Debug.Log($"[AddressablesSetup] 그룹 생성: {name}");
        return group;
    }

    // variantDir 이 주어지면 그 폴더의 "{key}.prefab"(배리언트)을 우선 등록하고, 없으면 OZEA 원본으로 폴백.
    private static int Mark(AddressableAssetSettings settings, AddressableAssetGroup group,
        string key, string ozeaPrefab, string variantDir)
    {
        string path = null;
        if (variantDir != null)
        {
            string variantPath = $"{variantDir}/{key}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null) path = variantPath;
        }
        path ??= FindPrefabPath(ozeaPrefab);

        if (path == null)
        {
            Debug.LogWarning($"[AddressablesSetup] '{key}' 에 쓸 프리팹(배리언트 또는 '{ozeaPrefab}')을 못 찾아 건너뜁니다.");
            return 0;
        }

        string guid = AssetDatabase.AssetPathToGUID(path);

        // 같은 주소를 쓰던 다른 엔트리 제거 — 이전 실행에서 OZEA 원본을 가리키던 것 등.
        // 중복 주소가 있으면 InstantiateAsync 가 엉뚱한(옛) 애셋을 잡아서, 배리언트 수정이 반영 안 된다.
        var stale = new List<string>();
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries)
                if (e.address == key && e.guid != guid) stale.Add(e.guid);
        }
        foreach (var sg in stale) settings.RemoveAssetEntry(sg);

        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
        entry.address = key;
        return 1;
    }

    private static int FillMachinePrefabNames()
    {
        int n = 0;
        foreach (var e in MachineVariantGenerator.Machines)
        {
            MachineSO so = LoadScriptable<MachineSO>(a => a.machineID == e.GameId);
            if (so == null) continue;
            if (so.prefabName != e.Key)
            {
                so.prefabName = e.Key;
                EditorUtility.SetDirty(so);
            }
            n++;
        }
        return n;
    }

    private static int FillItemPrefabNames()
    {
        int n = 0;
        foreach (var e in MachineVariantGenerator.Items)
        {
            ItemSO so = LoadScriptable<ItemSO>(a => a.itemID == e.ItemId);
            if (so == null) continue;
            if (so.prefabName != e.Key)
            {
                so.prefabName = e.Key;
                EditorUtility.SetDirty(so);
            }
            n++;
        }
        return n;
    }

    private static void SetUseAssetDatabasePlayMode(AddressableAssetSettings settings)
    {
        for (int i = 0; i < settings.DataBuilders.Count; i++)
        {
            if (settings.GetDataBuilder(i) is BuildScriptPackedPlayMode) continue;
            if (settings.GetDataBuilder(i) is BuildScriptFastMode)
            {
                settings.ActivePlayModeDataBuilderIndex = i;
                return;
            }
        }
    }

    private static string FindPrefabPath(string prefabName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{prefabName} t:GameObject"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == prefabName) return path;
        }
        return null;
    }

    private static T LoadScriptable<T>(System.Func<T, bool> match) where T : ScriptableObject
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            var so = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null && match(so)) return so;
        }
        return null;
    }
}
