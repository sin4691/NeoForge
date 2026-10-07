using Bae.Data;
using Factory.Building;
using Factory.Simulation;
using Factory.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 에셋 없는 프로토타입 씬을 기본 도형으로 자동 구성한다.
// Tools > Factory Prototype > Build Tech Tree Scene 메뉴로 실행하거나
// -executeMethod SceneBootstrapper.BuildPrototypeScene 로 배치 실행 가능.
// 실행 전에 Tools > Factory Prototype > Seed Sample Game Data로 예시 데이터를 먼저 만들어야 한다.
//
// 플레이 시작 시 그리드는 비어 있고, 하단 팔레트에서 채굴기/제련로/벨트를 골라 직접 놓아야 한다
// (예전처럼 하드코딩된 데모 라인이 자동으로 생기지 않음).
public static class SceneBootstrapper
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string PrefabsPath = "Assets/Sin/Prefabs";
    private const string VisualLibraryPath = "Assets/Sin/MachineVisualLibrary.asset";
    // 48dp 최소 터치 타겟은 유지하면서(캔버스 스케일 기준 대략 매칭) 예전(160x100)보다 작게.
    private static readonly Vector2 ButtonSize = new Vector2(120f, 72f);

    [MenuItem("Tools/Factory Prototype/Build Tech Tree Scene")]
    public static void BuildPrototypeScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        RemoveLegacyObjects();

        // Bae님의 DataManager(JSON 로드 싱글톤) — SimulationDriver보다 먼저 존재해야 한다
        // (DataManager.cs의 [DefaultExecutionOrder]가 Awake 순서를 보장해준다).
        var dataManagerGO = EnsureEmpty("DataManager", Vector3.zero);
        EnsureComponentOn<DataManager>(dataManagerGO);

        var driverGO = EnsureEmpty("SimulationDriver", Vector3.zero);
        var driver = EnsureComponentOn<SimulationDriver>(driverGO);

        var tapGO = EnsureEmpty("TapInputManager", Vector3.zero);
        var tapInput = EnsureComponentOn<TapInputManager>(tapGO);
        SetRef(tapInput, "targetCamera", Camera.main);

        var buildSystemGO = EnsureEmpty("BuildSystem", Vector3.zero);
        var beltTool = EnsureComponentOn<BeltDragTool>(buildSystemGO);
        var machineTool = EnsureComponentOn<MachineGhostTool>(buildSystemGO);
        var demolishTool = EnsureComponentOn<DemolishTool>(buildSystemGO);
        var cameraRig = EnsureComponentOn<TouchCameraRig>(buildSystemGO);
        var router = EnsureComponentOn<BuildInputRouter>(buildSystemGO);

        SetRef(beltTool, "targetCamera", Camera.main);
        SetRef(beltTool, "driver", driver);
        SetRef(machineTool, "targetCamera", Camera.main);
        SetRef(machineTool, "driver", driver);
        SetRef(demolishTool, "targetCamera", Camera.main);
        SetRef(demolishTool, "driver", driver);
        SetRef(cameraRig, "targetCamera", Camera.main);
        // zoomSpeed는 필드 기본값을 코드에서 올려도 이미 씬에 저장된 컴포넌트 인스턴스의
        // 직렬화된 값은 그대로 남는다 — 재실행해도 반영되도록 여기서 명시적으로 맞춰준다.
        SetFloatField(cameraRig, "zoomSpeed", 0.2f);
        SetRef(router, "beltTool", beltTool);
        SetRef(router, "machineTool", machineTool);
        SetRef(router, "demolishTool", demolishTool);
        SetRef(demolishTool, "router", router);
        SetRef(router, "cameraRig", cameraRig);
        SetRef(tapInput, "buildInputRouter", router);

        var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/BeltItemVisual.prefab");
        var corePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/CoreVisual.prefab");
        var ghostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/MachineGhost.prefab");
        var stripPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/BeltStripVisual.prefab");
        var cornerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/BeltCornerVisual.prefab");
        var cornerLeftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/BeltCornerVisualLeft.prefab");
        var crosserVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/BeltCrosserVisual.prefab");
        if (itemPrefab == null || corePrefab == null || ghostPrefab == null || stripPrefab == null)
        {
            Debug.LogWarning("[SceneBootstrapper] Prefab(s) not found — run Tools > Factory Prototype > Build Prefabs first.");
        }

        SetRef(beltTool, "itemVisualPrefab", itemPrefab);
        SetRef(beltTool, "stripPrefab", stripPrefab);
        SetRef(beltTool, "cornerPrefab", cornerPrefab);
        SetRef(beltTool, "cornerLeftPrefab", cornerLeftPrefab);
        SetRef(beltTool, "crosserVisualPrefab", crosserVisualPrefab);
        SetRef(machineTool, "ghostPrefab", ghostPrefab);

        // Bae님 데이터(MachineData.prefabName)는 Addressables 키 문자열이라 아직 실제 프리팹을
        // 못 가져온다(Addressables 미연결) — 그때까지 기계별 전용 외형을 유지하기 위한 임시
        // machineId->프리팹 매핑(MachineVisualLibrary 참고). Addressables 실제 연결되면 이 부분 걷어내면 됨.
        var visualLibrary = EnsureMachineVisualLibrary();
        SetRef(machineTool, "visualLibrary", visualLibrary);
        // 크로스 벨트(CrossBeltMachineId) 확정 시 실제 배치를 BeltDragTool에 위임하는 데 쓴다.
        SetRef(machineTool, "beltTool", beltTool);

        var coreSpawnerGO = EnsureEmpty("CoreSpawner", Vector3.zero);
        var coreSpawner = EnsureComponentOn<CoreSpawner>(coreSpawnerGO);
        SetRef(coreSpawner, "driver", driver);
        SetRef(coreSpawner, "corePrefab", corePrefab);

        var oreDepositVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/OreDepositVisual.prefab");
        var oreDepositSpawnerGO = EnsureEmpty("OreDepositSpawner", Vector3.zero);
        var oreDepositSpawner = EnsureComponentOn<OreDepositSpawner>(oreDepositSpawnerGO);
        SetRef(oreDepositSpawner, "driver", driver);
        SetRef(oreDepositSpawner, "oreDepositVisualPrefab", oreDepositVisualPrefab);

        // 기계 종류는 이제 애셋이 아니라 Bae님 JSON의 machineID 문자열로만 식별한다.
        BuildPalette(router, machineTool, demolishTool, "Miner", "Smelter", "Former", "Synthesizer", "Splitter", "Merger");
        BuildHud(driver);
        BuildRecipePanel(driver);
        BuildGround();
        FrameCamera();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneBootstrapper] Prototype scene built and saved: " + scene.path);
    }

    private static readonly string[] LegacyObjectNames =
    {
        "OreNode", "StorageDepot", "ResourceManager", "RawOreText", "RefinedOreText",
        "Miner", "Smelter", "BeltNodeA", "BeltNodeB", "BeltNodeC",
        "BeltStrip_Segment0", "BeltStrip_Segment1", "BeltRenderer_Segment0", "BeltRenderer_Segment1",
        "DemoSceneSetup",
        "PaletteButton_Assembler", // 조립기 -> 성형기+합성기로 분리되면서 없어짐. 이름이 바뀌어
                                   // EnsureButton이 새로 만들지 않고 예전 자리에 그대로 남으므로 명시적으로 지운다.
        "FacingLabel", // 예전 실행에서 캔버스 루트에 잘못 붙였던 버전을 지우고 버튼 자식으로 다시 만든다.
        "PaletteButton_CopperMiner", // 채굴기가 하나로 통합되면서 없어짐(광물 노드 태그로 대체).
        "UndoButton", // 되돌리기 기능을 철거로 교체하면서 없어짐 — UndoButton.cs 자체가 삭제돼서
                      // 이름만 같은 새 오브젝트를 만들어도 이 이름의 예전 오브젝트는 안 지워지고
                      // 스크립트만 깨진 채(Missing) 화면에 남아있었다.
        "PaletteButton_CrossBelt", // 크로스 벨트 임시 팔레트 버튼 제거 — 정식 UI가 정해지면
                                   // 다시 추가한다. MachineGhostTool.CrossBeltMachineId /
                                   // BeltDragTool.PlaceCrossableTile 등 기능 코드 자체는 그대로 남아있다.
    };

    private static void RemoveLegacyObjects()
    {
        foreach (var name in LegacyObjectNames)
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }
    }

    private const float GroundSize = 40f; // 월드 단위. 원점 중심이라 -20..20 범위를 덮음.

    private static void BuildGround()
    {
        var go = GameObject.Find("Ground");
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ground";
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        // 기본 Plane은 10x10, 원점 중심으로 -5..5 범위. 스케일 GroundSize/10을 곱하면
        // 왼쪽 가장자리가 -GroundSize/2(정수)에 오게 되어, 타일 경계가 GridUtility의
        // 셀 경계(정수 좌표)와 정확히 맞아떨어진다.
        go.transform.position = new Vector3(0f, -0.02f, 0f);
        go.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f);

        var texture = GridTextureFactory.CreateGridLineTexture(64, 2, new Color(0.30f, 0.30f, 0.33f), new Color(0.48f, 0.48f, 0.52f));
        var material = BuildVisuals.CreateTiledMaterial(texture, new Vector2(GroundSize, GroundSize));
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static T EnsureComponentOn<T>(GameObject go) where T : Component
    {
        return go.GetComponent<T>() ?? go.AddComponent<T>();
    }

    private static GameObject EnsureEmpty(string name, Vector3 position)
    {
        var go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            go.transform.position = position;
        }
        return go;
    }

    private static void FrameCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // 기획서상 장르가 "탑다운 시점"이고, 완전 수직으로 내려다보면 바닥 레이캐스트가
        // 항상 카메라 높이만큼의 고정 거리로 안정적이다 (비스듬한 각도에서 화면 위쪽/지평선
        // 근처를 클릭하면 광선이 바닥과 거의 평행해져 교차 거리가 튀는 문제가 있었음).
        cam.orthographic = true;
        cam.orthographicSize = 8f;
        cam.transform.position = new Vector3(0f, 15f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private static void BuildPalette(BuildInputRouter router, MachineGhostTool machineTool, DemolishTool demolishTool, string minerMachineId, string smelterMachineId, string formerMachineId, string synthesizerMachineId, string splitterMachineId, string mergerMachineId)
    {
        var canvasGO = GameObject.Find("HUDCanvas");
        if (canvasGO == null)
        {
            canvasGO = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
        }

        EnsureEventSystem();

        // 모든 HUD는 노치/홈바를 피하도록 SafeArea 루트 아래에 둔다 — 각 버튼의 앵커 좌표는
        // 그대로 두고도 안전 영역 기준으로 배치된다(SafeAreaFitter).
        var hudRoot = EnsureHudRoot(canvasGO).transform;

        var minerButton = EnsureButton(hudRoot, "PaletteButton_Miner", "채굴기", new Vector2(20, 40));
        WirePaletteButton(minerButton, router, machineTool, minerMachineId);

        var smelterButton = EnsureButton(hudRoot, "PaletteButton_Smelter", "제련로", new Vector2(150, 40));
        WirePaletteButton(smelterButton, router, machineTool, smelterMachineId);

        var formerButton = EnsureButton(hudRoot, "PaletteButton_Former", "성형기", new Vector2(280, 40));
        WirePaletteButton(formerButton, router, machineTool, formerMachineId);

        var synthesizerButton = EnsureButton(hudRoot, "PaletteButton_Synthesizer", "합성기", new Vector2(410, 40));
        WirePaletteButton(synthesizerButton, router, machineTool, synthesizerMachineId);

        var splitterButton = EnsureButton(hudRoot, "PaletteButton_Splitter", "분류기", new Vector2(540, 40));
        WirePaletteButton(splitterButton, router, machineTool, splitterMachineId);

        var mergerButton = EnsureButton(hudRoot, "PaletteButton_Merger", "합류기", new Vector2(670, 40));
        WirePaletteButton(mergerButton, router, machineTool, mergerMachineId);

        var beltButton = EnsureButton(hudRoot, "PaletteButton_Belt", "벨트", new Vector2(800, 40));
        WirePaletteButton(beltButton, router, machineTool, null, isBeltButton: true);

        var demolishButton = EnsureButton(hudRoot, "PaletteButton_Demolish", "철거", new Vector2(930, 40));
        WirePaletteButton(demolishButton, router, machineTool, null, isDemolishButton: true);

        // 팔레트(1행, y=40)와 같은 줄에 두면 기계 종류가 늘어날 때마다 배치/확정 버튼과
        // 자리다툼이 난다(실제로 조립기 추가하면서 회전 버튼과 겹쳤음) — 그래서 배치 액션
        // 버튼들은 팔레트 위 2행(y=140)에 따로 둬서 팔레트가 늘어나도 절대 안 겹치게 한다.
        // 방향 표시는 화면 글자 대신 기계 위 화살표(OutputArrow)만 쓰기로 해서 별도 라벨 없음.
        var rotateButton = EnsureButton(hudRoot, "RotateButton", "회전", new Vector2(-270, 140), rightAnchored: true);
        var rotate = rotateButton.gameObject.GetComponent<RotatePlacementButton>() ?? rotateButton.gameObject.AddComponent<RotatePlacementButton>();
        SetRef(rotate, "machineTool", machineTool);
        SetRef(rotate, "button", rotateButton);

        var confirmButton = EnsureButton(hudRoot, "ConfirmButton", "확정", new Vector2(-140, 140), rightAnchored: true);
        var confirm = confirmButton.gameObject.GetComponent<ConfirmPlacementButton>() ?? confirmButton.gameObject.AddComponent<ConfirmPlacementButton>();
        SetRef(confirm, "machineTool", machineTool);
        SetRef(confirm, "router", router);
        SetRef(confirm, "button", confirmButton);

        // 회전/확정과 같은 2행(y=140)이지만 왼쪽에 둬서 팔레트 확장과도, 배치 액션 버튼들과도 안 겹치게.
        var demolishConfirmButton = EnsureButton(hudRoot, "DemolishConfirmButton", "철거 확정", new Vector2(20, 140));
        var demolishConfirm = demolishConfirmButton.gameObject.GetComponent<DemolishConfirmButton>() ?? demolishConfirmButton.gameObject.AddComponent<DemolishConfirmButton>();
        SetRef(demolishConfirm, "demolishTool", demolishTool);
        SetRef(demolishConfirm, "router", router);
        SetRef(demolishConfirm, "button", demolishConfirmButton);
    }

    private static void BuildRecipePanel(SimulationDriver driver)
    {
        var canvasGO = GameObject.Find("HUDCanvas");
        if (canvasGO == null) return;

        var hudRoot = EnsureHudRoot(canvasGO).transform;

        var panelGO = GameObject.Find("RecipeSelectionPanel");
        if (panelGO == null)
        {
            panelGO = new GameObject("RecipeSelectionPanel", typeof(RectTransform), typeof(Image));
            panelGO.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.92f);
        }

        // 이미 있던 패널이 예전 실행에서 캔버스 루트에 붙어 있었을 수 있으니 매번 SafeArea 루트로
        // 옮기고 앵커/크기를 다시 맞춘다(EnsureButton/EnsureText와 같은 취지).
        if (panelGO.transform.parent != hudRoot) panelGO.transform.SetParent(hudRoot, false);
        var panelRt = panelGO.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(380f, 700f);

        var containerGO = GameObject.Find("RecipeButtonContainer");
        if (containerGO == null)
        {
            containerGO = new GameObject("RecipeButtonContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            containerGO.transform.SetParent(panelGO.transform, false);
            var rt = containerGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20f, 20f);
            rt.offsetMax = new Vector2(-20f, -20f);

            var layout = containerGO.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        var panelComponent = panelGO.GetComponent<RecipeSelectionPanel>() ?? panelGO.AddComponent<RecipeSelectionPanel>();
        SetRef(panelComponent, "panelRoot", panelGO);
        SetRef(panelComponent, "buttonContainer", containerGO.transform);
        SetRef(panelComponent, "driver", driver);
    }

    private static void WirePaletteButton(Button button, BuildInputRouter router, MachineGhostTool machineTool, string machineId, bool isBeltButton = false, bool isDemolishButton = false)
    {
        var paletteButton = button.gameObject.GetComponent<BuildPaletteButton>() ?? button.gameObject.AddComponent<BuildPaletteButton>();
        SetRef(paletteButton, "router", router);
        SetRef(paletteButton, "machineTool", machineTool);
        SetStringField(paletteButton, "machineId", machineId);
        SetBoolField(paletteButton, "isBeltButton", isBeltButton);
        SetBoolField(paletteButton, "isDemolishButton", isDemolishButton);
        SetRef(paletteButton, "button", button);
    }

    // Bae님 데이터의 machineID <-> 프리팹({id}Visual.prefab) 매핑을 다시 채운다. 재실행해도
    // 항상 최신 프리팹으로 다시 맞추도록, 매번 항목을 비우고 다시 채운다.
    private static readonly string[] KnownMachineIds = { "Miner", "Smelter", "Former", "Synthesizer", "Splitter", "Merger" };

    private static MachineVisualLibrary EnsureMachineVisualLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<MachineVisualLibrary>(VisualLibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<MachineVisualLibrary>();
            AssetDatabase.CreateAsset(library, VisualLibraryPath);
        }

        var so = new SerializedObject(library);
        var entries = so.FindProperty("entries");
        entries.ClearArray();

        for (int i = 0; i < KnownMachineIds.Length; i++)
        {
            string id = KnownMachineIds[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsPath}/{id}Visual.prefab");
            if (prefab == null) continue;

            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            var element = entries.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("machineId").stringValue = id;
            element.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        return library;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;

        var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    // 캔버스를 꽉 채우는 "SafeArea" 자식. 모든 HUD(팔레트/액션 버튼/자원 표시/레시피 패널)의
    // 부모로 써서 노치·펀치홀·홈버튼바를 피한다. 시작값만 전체 화면이고, 런타임에
    // SafeAreaFitter가 Screen.safeArea 기준으로 다시 맞춘다(에디터/PC에선 전체 화면 그대로).
    private static GameObject EnsureHudRoot(GameObject canvasGO)
    {
        var existing = canvasGO.transform.Find("SafeArea");
        GameObject go = existing != null ? existing.gameObject : new GameObject("SafeArea", typeof(RectTransform));
        if (existing == null) go.transform.SetParent(canvasGO.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        EnsureComponentOn<SafeAreaFitter>(go);
        return go;
    }

    private static Button EnsureButton(Transform parent, string name, string label, Vector2 anchoredPos, bool rightAnchored = false)
    {
        var existing = GameObject.Find(name);
        GameObject go;
        bool isNew = existing == null;

        if (isNew)
        {
            go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
        }
        else
        {
            go = existing;
            // 예전 실행에서 캔버스 루트에 붙어 있던 버튼을 SafeArea 루트로 옮긴다.
            if (go.transform.parent != parent) go.transform.SetParent(parent, false);
        }

        // 위치/앵커/크기는 순전히 코드가 결정하는 레이아웃이라, 이미 있는 버튼이어도 매번
        // 최신 값으로 맞춘다 — 안 그러면(예전엔 여기서 바로 return) 팔레트에 버튼을 새로
        // 추가할 때 기존 버튼이 옛 자리에 그대로 남아서 새 버튼과 겹쳐버린다.
        var rt = go.GetComponent<RectTransform>();
        if (rightAnchored)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
        }
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = ButtonSize;

        if (!isNew) return go.GetComponent<Button>();

        var image = go.GetComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.85f);

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(go.transform, false);
        var textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGO.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 22;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = 22;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;

        return go.GetComponent<Button>();
    }

    private static void BuildHud(SimulationDriver driver)
    {
        var canvasGO = GameObject.Find("HUDCanvas");
        if (canvasGO == null) return;

        var hudRoot = EnsureHudRoot(canvasGO).transform;

        var line1 = EnsureText(hudRoot, "HudLine1", new Vector2(40, -60), new Vector2(700f, 50f));
        // line2는 코어에 쌓인 자원 종류가 늘어날수록 길어지는 목록이라, line1보다 훨씬
        // 넉넉하게 잡는다 — 예전엔 500x60 고정 박스라 자원 종류가 몇 개만 넘어가도
        // (기본 verticalOverflow=Truncate라 에러 없이 조용히) 잘려서 안 보였다.
        var line2 = EnsureText(hudRoot, "HudLine2", new Vector2(40, -120), new Vector2(1000f, 200f));

        var hudGO = GameObject.Find("ResourceHUD") ?? new GameObject("ResourceHUD");
        var hud = EnsureComponentOn<ResourceHUD>(hudGO);
        SetRef(hud, "line1", line1);
        SetRef(hud, "line2", line2);

        var bridgeGO = GameObject.Find("SimulationHudBridge") ?? new GameObject("SimulationHudBridge");
        var bridge = EnsureComponentOn<SimulationHudBridge>(bridgeGO);
        SetRef(bridge, "driver", driver);
        SetRef(bridge, "hud", hud);
    }


    private static Text EnsureText(Transform parent, string name, Vector2 anchoredPos, Vector2 size)
    {
        var existing = GameObject.Find(name);
        bool isNew = existing == null;
        var go = isNew ? new GameObject(name, typeof(RectTransform), typeof(Text)) : existing;
        if (isNew) go.transform.SetParent(parent, false);
        else if (go.transform.parent != parent) go.transform.SetParent(parent, false); // 예전 실행에서 캔버스 루트에 붙어 있던 것을 SafeArea 루트로 옮긴다.

        // 버튼 배치와 마찬가지로 이미 있는 오브젝트여도 매번 최신 크기/위치로 맞춘다 —
        // 안 그러면 예전 씬에 남아있던 작은 박스 크기가 그대로 남아 재실행해도 안 고쳐진다.
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 32;
        // 코어 자원 목록처럼 길이가 늘어나는 텍스트가 고정 폰트 크기 탓에 박스 밖에서
        // 조용히 잘려 안 보이는 걸 막는다 — 잘리는 대신 줄어들거나(최소 14) 필요하면
        // 박스 밖으로도 넘쳐 그려지게(Overflow) 해서 최소한 화면에서 사라지진 않게 한다.
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 14;
        text.resizeTextMaxSize = 32;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;
        // Text는 기본적으로 Raycast Target이 켜져 있다 — 이 박스를 텍스트 안 잘리게 크게
        // 키우면서(최대 1000x200) 화면 위쪽 상당 부분이 "UI 위"로 잡혀서, 그 영역에서
        // 기계 배치/벨트 드래그가 전부 씹히는 원인이 됐다. 순수 표시용이라 꺼둔다.
        text.raycastTarget = false;
        if (isNew) text.text = name;
        return text;
    }

    private static void SetRef(Object target, string fieldName, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBoolField(Object target, string fieldName, bool value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloatField(Object target, string fieldName, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetStringField(Object target, string fieldName, string value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).stringValue = value ?? string.Empty;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
