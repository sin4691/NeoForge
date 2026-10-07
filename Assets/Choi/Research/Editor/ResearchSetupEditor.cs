#if UNITY_EDITOR
using System.Collections.Generic;
using Choi.Research;
using Bae.SO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ResearchSetupEditor
{
    private const string Root = "Assets/Choi/Research";
    private const string PrefabPath = Root + "/Prefabs/ResearchSystem.prefab";
    private const string ScenePath = "Assets/Scenes/Main.unity";

    static ResearchSetupEditor() => EditorApplication.delayCall += Install;

    [MenuItem("Factory/Research/Rebuild And Install Choi Research UI")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        EnsureFolders();
        var tiers = CreateTiers();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || prefab.transform.Find("ResearchCanvas/ResearchPanel/TierTabs") == null)
            prefab = CreatePrefab(tiers);
        else
            EnsureTierReferences(prefab, tiers);
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool temporary = !scene.IsValid() || !scene.isLoaded;
        if (temporary) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool exists = false;
        foreach (GameObject root in scene.GetRootGameObjects()) exists |= root.name == "ChoiResearchSystem";
        if (!exists)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "ChoiResearchSystem";
            EditorSceneManager.SaveScene(scene);
        }
        if (temporary) EditorSceneManager.CloseScene(scene, true);
    }

    private static List<ResearchTierAsset> CreateTiers()
    {
        return new List<ResearchTierAsset>
        {
            Tier("ResearchTier_01", 1, "기초 생산", 350,
                new[] { Goal("IronOre", 100), Goal("CopperOre", 100) },
                new[] { "Former", "Smelter" }, new[] { "FormIronIngot", "SmeltCopperIngot", "SmeltIronIngot" }),
            Tier("ResearchTier_02", 2, "자동화 확장", 500,
                new[] { Goal("IronPlate", 150), Goal("CopperWire", 150) },
                new[] { "Synthesizer", "MiniCore" }, new[] { "FormCopperPlate", "FormCopperWire", "SynthesizGear", "SynthesizEnergyCell" }),
            Tier("ResearchTier_03", 3, "첨단 산업", 700,
                new[] { Goal("Gear", 100), Goal("EnergyCell", 100) },
                new[] { "ProcessingMachine" }, new[] { "ProcessCircuit", "ProcessPlasmaCore", "SynthesizHighCapacityBattery" }),
            Tier("ResearchTier_04", 4, "자동화의 완성", 400,
                new[] { Goal("Circuit", 10000), Goal("CarbonNanotube", 10000), Goal("PlasmaCore", 10000) },
                new string[0], new string[0], true),
        };
    }

    private static ResearchTierAsset Tier(string file, int tier, string name, int mapSize,
        ResearchResourceGoal[] goals, string[] machines, string[] recipes, bool unlocksEnding = false)
    {
        string path = $"{Root}/Tiers/{file}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<ResearchTierAsset>(path);
        if (asset != null) return asset;
        if (asset == null) { asset = ScriptableObject.CreateInstance<ResearchTierAsset>(); AssetDatabase.CreateAsset(asset, path); }
        asset.tier = tier; asset.displayName = name; asset.description = "코어의 자원을 연구소에 누적 납품하세요."; asset.unlockedMapSize = mapSize;
        asset.unlocksEnding = unlocksEnding;
        if (unlocksEnding) asset.description = "가공기 생산품 3종을 각각 10,000개씩 누적 납품하면 엔딩이 해금됩니다.";
        asset.resourceGoals = new List<ResearchResourceGoal>(goals);
        asset.machineRewards = new List<ResearchMachineReward>();
        foreach (string id in machines) asset.machineRewards.Add(new ResearchMachineReward { machine = AssetDatabase.LoadAssetAtPath<MachineSO>($"Assets/Bae/Data/Machines/{id}.asset") });
        asset.recipeRewards = new List<RecipeSO>();
        foreach (string id in recipes) { var recipe = AssetDatabase.LoadAssetAtPath<RecipeSO>($"Assets/Bae/Data/Recipes/{id}.asset"); if (recipe != null) asset.recipeRewards.Add(recipe); }
        EditorUtility.SetDirty(asset); return asset;
    }

    private static ResearchResourceGoal Goal(string id, int amount) => new ResearchResourceGoal { resourceId = id, amount = amount };

    private static void EnsureTierReferences(GameObject prefab, List<ResearchTierAsset> tiers)
    {
        var controller = prefab.GetComponent<ResearchController>();
        if (controller == null) return;
        var serialized = new SerializedObject(controller);
        var references = serialized.FindProperty("tiers");
        bool changed = false;
        foreach (var tier in tiers)
        {
            bool exists = false;
            for (int i = 0; i < references.arraySize; i++)
                exists |= references.GetArrayElementAtIndex(i).objectReferenceValue == tier;
            if (exists) continue;
            references.arraySize++;
            references.GetArrayElementAtIndex(references.arraySize - 1).objectReferenceValue = tier;
            changed = true;
        }
        if (!changed) return;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SavePrefabAsset(prefab);
        AssetDatabase.SaveAssets();
    }

    private static GameObject CreatePrefab(List<ResearchTierAsset> tiers)
    {
        var root = new GameObject("ChoiResearchSystem");
        var controller = root.AddComponent<ResearchController>(); root.AddComponent<CoreResearchButtonBridge>();
        var canvasGo = Go("ResearchCanvas", root.transform, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
        var panel = Go("ResearchPanel", canvasGo.transform, typeof(Image));
        Rect(panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(720, 500));
        panel.GetComponent<Image>().color = new Color(.035f, .055f, .075f, .98f);
        Text title = Text(panel.transform, "Title", "연구소", 29, new Vector2(24, -18), new Vector2(672, 42));
        var tabs = Go("TierTabs", panel.transform, typeof(HorizontalLayoutGroup)); Rect(tabs, Vector2.up, Vector2.up, Vector2.up, new Vector2(24,-65), new Vector2(672,54));
        var tabPrefab = Button(tabs.transform, "TierTabTemplate", "TIER", Vector2.zero, new Vector2(150,50), new Color(.18f,.22f,.27f,1));
        Text desc = Text(panel.transform, "Description", "", 17, new Vector2(26, -126), new Vector2(668, 34));
        var goalsCard = Card(panel.transform, "ResourceArea", new Vector2(24, -170), new Vector2(310, 230), new Color(.03f, .16f, .22f, 1));
        Text(goalsCard.transform, "Label", "필요 자원 · 누적 납품", 19, new Vector2(16, -14), new Vector2(278, 32));
        Text goals = Text(goalsCard.transform, "Goals", "", 19, new Vector2(16, -58), new Vector2(278, 150));
        var unlockCard = Card(panel.transform, "UnlockArea", new Vector2(354, -170), new Vector2(342, 230), new Color(.12f, .1f, .055f, 1));
        Text unlocks = Text(unlockCard.transform, "Unlocks", "", 16, new Vector2(14, -12), new Vector2(314, 32));
        var rewardRoot = Go("RewardGrid", unlockCard.transform, typeof(HorizontalLayoutGroup)); Rect(rewardRoot, Vector2.up, Vector2.up, Vector2.up, new Vector2(14,-58), new Vector2(314,150));
        var rewardTemplate = Card(rewardRoot.transform, "RewardTemplate", Vector2.zero, new Vector2(96,130), new Color(.18f,.18f,.18f,1));
        var icon = Go("Icon", rewardTemplate.transform, typeof(Image)); Rect(icon, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-10), new Vector2(68,68));
        Text(rewardTemplate.transform, "Name", "기기", 15, new Vector2(8,-88), new Vector2(80,34)); rewardTemplate.SetActive(false);
        Button supply = Button(panel.transform, "SupplyButton", "선택 티어 해금", new Vector2(24, 22), new Vector2(310, 58), new Color(.05f, .5f, .62f, 1));
        Button close = Button(panel.transform, "CloseButton", "닫기", new Vector2(-24, 22), new Vector2(150, 58), new Color(.35f, .14f, .16f, 1), true);
        Set(controller, "panelRoot", panel); Set(controller, "titleText", title); Set(controller, "descriptionText", desc); Set(controller, "goalsText", goals); Set(controller, "unlocksText", unlocks); Set(controller, "supplyButton", supply); Set(controller, "closeButton", close); Set(controller, "tierTabRoot", tabs.transform); Set(controller, "rewardRoot", rewardRoot.transform); Set(controller, "tierTabPrefab", tabPrefab); Set(controller, "rewardCardPrefab", rewardTemplate); SetList(controller, "tiers", tiers);
        panel.SetActive(false);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); Object.DestroyImmediate(root); AssetDatabase.SaveAssets(); return prefab;
    }

    private static GameObject Card(Transform p, string n, Vector2 pos, Vector2 size, Color color) { var go = Go(n, p, typeof(Image)); Rect(go, Vector2.up, Vector2.up, Vector2.up, pos, size); go.GetComponent<Image>().color = color; return go; }
    private static GameObject Go(string n, Transform p, params System.Type[] types) { var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false); foreach (var t in types) if (go.GetComponent(t) == null) go.AddComponent(t); return go; }
    private static Text Text(Transform p, string n, string value, int size, Vector2 pos, Vector2 dimensions) { var go = Go(n, p, typeof(Text)); Rect(go, Vector2.up, Vector2.up, Vector2.up, pos, dimensions); var t = go.GetComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = size; t.color = Color.white; t.text = value; t.alignment = TextAnchor.UpperLeft; return t; }
    private static Button Button(Transform p, string n, string label, Vector2 pos, Vector2 size, Color color, bool right = false) { var go = Go(n, p, typeof(Image), typeof(Button)); Vector2 a = right ? Vector2.right : Vector2.zero; Rect(go, a, a, a, pos, size); go.GetComponent<Image>().color = color; var text = Text(go.transform, "Label", label, 21, Vector2.zero, Vector2.zero); Rect(text.gameObject, Vector2.zero, Vector2.one, new Vector2(.5f,.5f), Vector2.zero, Vector2.zero); return go.GetComponent<Button>(); }
    private static void Rect(GameObject go, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size) { var r = go.GetComponent<RectTransform>(); r.anchorMin=min; r.anchorMax=max; r.pivot=pivot; r.anchoredPosition=pos; r.sizeDelta=size; r.localScale=Vector3.one; }
    private static void Set(Object o, string n, Object v) { var so = new SerializedObject(o); so.FindProperty(n).objectReferenceValue=v; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetList(Object o, string n, List<ResearchTierAsset> v) { var so = new SerializedObject(o); var p=so.FindProperty(n); p.arraySize=v.Count; for(int i=0;i<v.Count;i++) p.GetArrayElementAtIndex(i).objectReferenceValue=v[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void EnsureFolders() { if(!AssetDatabase.IsValidFolder(Root)) { if(!AssetDatabase.IsValidFolder("Assets/Choi/Research")) AssetDatabase.CreateFolder("Assets/Choi", "Research"); } if(!AssetDatabase.IsValidFolder(Root+"/Prefabs")) AssetDatabase.CreateFolder(Root,"Prefabs"); if(!AssetDatabase.IsValidFolder(Root+"/Tiers")) AssetDatabase.CreateFolder(Root,"Tiers"); }
}
#endif
