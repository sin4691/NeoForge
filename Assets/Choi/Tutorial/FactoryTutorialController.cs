using System.Collections.Generic;
using Choi.SaveLoad;
using Factory.Building;
using Factory.Buildings;
using Factory.Simulation;
using Seo.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Choi.Tutorial
{
    // 첫 생산 루프를 실제 게임 상태로 검증하는 런타임 튜토리얼.
    // 기존 건설/레시피/벨트 코드를 호출하거나 복제하지 않고, 플레이어가 만든 결과만 읽는다.
    [DefaultExecutionOrder(-1000)]
    public sealed class ChoiFactoryTutorialController : MonoBehaviour
    {
        public static ChoiFactoryTutorialController Instance { get; private set; }

        private enum Step
        {
            Waiting,
            Core,
            PickMiner,
            PlaceMiner,
            PickCoalMiner,
            PlaceCoalMiner,
            GatherStarterResources,
            PickSmelter,
            PlaceSmelter,
            InspectSmelter,
            PickRecipe,
            ConnectInput,
            ConnectOutput,
            PickGenerator,
            PlaceGenerator,
            InspectGenerator,
            PickCoalFuel,
            ConnectGeneratorFuel,
            PickTower,
            PlaceTower,
            PickCable,
            ConnectPower,
            ReturnToCore,
            OpenResearch,
            Complete,
        }

        // 현재 Main 맵의 시작 지점: 왼쪽 검은 광맥은 석탄, 오른쪽 붉은 광맥은 구리다.
        private static readonly Vector2Int CoalCell = new Vector2Int(-6, 4);
        private static readonly Vector2Int CopperCell = new Vector2Int(5, 4);
        // 코어 바로 아래. 기본 방향(+X)을 유지하면 입력/출력 벨트를 좌우로 분리할 수 있다.
        private static readonly Vector2Int SmelterCell = new Vector2Int(-1, -3);
        private static readonly Vector2Int GeneratorCell = new Vector2Int(3, 1);
        private static readonly Vector2Int TowerCell = new Vector2Int(3, 4);
        private static readonly Vector2Int[] InputBeltGuide =
        {
            new Vector2Int(-1, -1), new Vector2Int(-2, -1), new Vector2Int(-2, -2),
            new Vector2Int(-2, -3), SmelterCell,
        };
        private static readonly Vector2Int[] OutputBeltGuide =
        {
            SmelterCell, new Vector2Int(0, -3), new Vector2Int(0, -2),
            new Vector2Int(0, -1),
        };
        private static readonly Vector2Int[] GeneratorFuelBeltGuide =
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0),
            new Vector2Int(2, 1), GeneratorCell,
        };

        private SimulationDriver driver;
        private MachineGhostTool machineTool;
        private BuildInputRouter buildRouter;
        private FactoryHudController hud;
        private PowerBuildController powerBuild;
        private PowerGridSystem powerGrid;
        private Step step = Step.Waiting;
        private Text titleText;
        private Text bodyText;
        private Text progressText;
        private Button skipButton;
        private GameObject panelRoot;
        private GameObject worldHighlight;
        private GameObject placementPreview;
        private GameObject beltGuide;
        private GameObject fingerGuide;
        private RectTransform fingerCanvas;
        private RectTransform fingerUiTarget;
        private Vector3 fingerWorldTarget;
        private float fingerWorldRadius;
        private bool fingerTracksWorld;
        private Texture2D arrowTexture;
        private Sprite arrowSprite;
        private Outline uiOutline;
        private Graphic uiGraphic;
        private int copperOreId = -1;
        private int copperIngotId = -1;
        private int copperWireId = -1;
        private int coalId = -1;
        private int copperRecipeId = -1;
        private int smelterIndex = -1;
        private int generatorNodeId = -1;
        private int generatorFuelProcessorIndex = -1;
        private int towerNodeId = -1;
        private int initialCopperIngot;
        private float enteredAt;
        private TapInputManager legacyTapInput;
        private bool legacyTapWasEnabled;
        private readonly Dictionary<Button, bool> originalButtonStates = new Dictionary<Button, bool>();

        public static bool AllowsMachineSelection(MachineInstanceKind kind, int index)
        {
            if (Instance == null || !Instance.enabled) return true;
            switch (Instance.step)
            {
                case Step.Core:
                case Step.ReturnToCore:
                    return kind == MachineInstanceKind.Processor
                        && Instance.driver != null && Instance.driver.World != null
                        && index == Instance.driver.World.CoreProcessorIndex;
                case Step.InspectSmelter:
                    return kind == MachineInstanceKind.Processor && index == Instance.smelterIndex;
                case Step.InspectGenerator:
                case Step.PickCoalFuel:
                    return kind == MachineInstanceKind.Processor && index == Instance.generatorFuelProcessorIndex;
                default:
                    return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            SceneManager.sceneLoaded -= CreateRuntimeInstance;
            SceneManager.sceneLoaded += CreateRuntimeInstance;
        }

        private static void CreateRuntimeInstance(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main") return;
            if (FindFirstObjectByType<ChoiFactoryTutorialController>() != null) return;
            new GameObject("[Choi] Factory Tutorial").AddComponent<ChoiFactoryTutorialController>();
        }

        private void Awake()
        {
            DisableLegacyTutorial();
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreInput();
            if (arrowSprite != null) Destroy(arrowSprite);
            if (arrowTexture != null) Destroy(arrowTexture);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            DisableLegacyTutorial();
            if (!Discover()) return;
            if (step == Step.Waiting)
            {
                var saveManager = FindFirstObjectByType<PowerSaveManager>();
                if (saveManager != null && saveManager.HasLoadedSave)
                {
                    SkipTutorial();
                    return;
                }
                Begin();
            }

            AnimateHighlights();
            EvaluateStep();
        }

        private static void DisableLegacyTutorial()
        {
            Seo.UI.FactoryTutorialController legacy = FindFirstObjectByType<Seo.UI.FactoryTutorialController>();
            if (legacy != null) legacy.enabled = false;
        }

        private void LateUpdate()
        {
            if (step != Step.Complete) ApplyInputLocks();
        }

        private bool Discover()
        {
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (legacyTapInput == null)
            {
                legacyTapInput = FindFirstObjectByType<TapInputManager>();
                if (legacyTapInput != null)
                {
                    legacyTapWasEnabled = legacyTapInput.enabled;
                    legacyTapInput.enabled = false; // 선택은 잠금 판정이 있는 UIManager 한 경로만 사용한다.
                }
            }
            if (hud == null) hud = FindFirstObjectByType<FactoryHudController>();
            if (powerBuild == null) powerBuild = FindFirstObjectByType<PowerBuildController>();
            if (powerGrid == null) powerGrid = FindFirstObjectByType<PowerGridSystem>();
            if (driver == null || driver.World == null || machineTool == null || hud == null) return false;

            if (panelRoot == null)
            {
                var canvasObject = GameObject.Find("HUDCanvas");
                if (canvasObject == null) return false;
                BuildPanel(canvasObject.transform.Find("SafeArea") ?? canvasObject.transform);
            }
            return panelRoot != null;
        }

        private void Begin()
        {
            var db = driver.World.Database;
            if (!db.TryGetResourceId("CopperOre", out copperOreId)
                || !db.TryGetResourceId("CopperIngot", out copperIngotId)
                || !db.TryGetResourceId("CopperWire", out copperWireId)
                || !db.TryGetResourceId("Coal", out coalId)
                || !db.TryGetRecipeId("SmeltCopperIngot", out copperRecipeId))
            {
                Debug.LogWarning("[Tutorial] 구리 자원 또는 제련 레시피가 없어 튜토리얼을 시작하지 않습니다.");
                panelRoot.SetActive(false);
                enabled = false;
                return;
            }
            MachineGhostTool.PlacementPermission = AllowsPlacement;
            BeltDragTool.PathPermission = AllowsBeltPath;
            PowerBuildController.PlacementPermission = AllowsPowerPlacement;
            Enter(Step.Core);
        }

        private void EvaluateStep()
        {
            var world = driver.World;
            switch (step)
            {
                case Step.Core:
                    if (IsSelected(MachineInstanceKind.Processor, world.CoreProcessorIndex)) Enter(Step.PickMiner);
                    break;
                case Step.PickMiner:
                    if (machineTool.SelectedMachineId == "Miner") Enter(Step.PlaceMiner);
                    break;
                case Step.PlaceMiner:
                    if (TryGetOccupant(CopperCell, CellOccupantType.Miner, out _))
                        Enter(Step.PickCoalMiner);
                    break;
                case Step.PickCoalMiner:
                    if (machineTool.SelectedMachineId == "Miner") Enter(Step.PlaceCoalMiner);
                    break;
                case Step.PlaceCoalMiner:
                    if (TryGetOccupant(CoalCell, CellOccupantType.Miner, out _))
                        Enter(Step.GatherStarterResources);
                    break;
                case Step.GatherStarterResources:
                    if (CoreAmount(copperOreId) >= 10 && CoreAmount(coalId) >= 1)
                    {
                        GameObject resourcePanel = GameObject.Find("SeoResourceCard");
                        if (resourcePanel != null) resourcePanel.SetActive(false);
                        Enter(Step.PickSmelter);
                    }
                    break;
                case Step.PickSmelter:
                    if (machineTool.SelectedMachineId == "Smelter") Enter(Step.PlaceSmelter);
                    break;
                case Step.PlaceSmelter:
                    if (TryGetOccupant(SmelterCell, CellOccupantType.Processor, out int placed)
                        && IsMachine(placed, "Smelter"))
                    {
                        smelterIndex = placed;
                        Enter(Step.InspectSmelter);
                    }
                    break;
                case Step.InspectSmelter:
                    if (IsSelected(MachineInstanceKind.Processor, smelterIndex)) Enter(Step.PickRecipe);
                    break;
                case Step.PickRecipe:
                    // 레시피 창이 열린 뒤에는 설정 버튼 대신 정확한 구리 레시피를 빛낸다.
                    if (GameObject.Find("Recipe_SmeltCopperIngot") != null
                        && (uiOutline == null || uiOutline.gameObject.name != "Recipe_SmeltCopperIngot"))
                    {
                        ClearHighlights();
                        HighlightUI("Recipe_SmeltCopperIngot");
                    }
                    if (ValidProcessor(smelterIndex) && world.Processors[smelterIndex].RecipeId == copperRecipeId)
                        Enter(Step.ConnectInput);
                    break;
                case Step.ConnectInput:
                    if (HasBeltPath(world.CoreProcessorIndex, smelterIndex)) Enter(Step.ConnectOutput);
                    break;
                case Step.ConnectOutput:
                    if (HasBeltPath(smelterIndex, world.CoreProcessorIndex)
                        && CoreAmount(copperIngotId) > initialCopperIngot)
                        Enter(Step.PickGenerator);
                    break;
                case Step.PickGenerator:
                    if (powerBuild != null && powerBuild.Mode == PowerBuildMode.Generator)
                        Enter(Step.PlaceGenerator);
                    break;
                case Step.PlaceGenerator:
                    if (powerGrid != null && powerGrid.TryGetNode(GeneratorCell, out PowerNodeRuntime generator)
                        && generator.Kind == PowerNodeKind.Generator)
                    {
                        generatorNodeId = generator.Id;
                        generatorFuelProcessorIndex = generator.FuelProcessorIndex;
                        Enter(Step.InspectGenerator);
                    }
                    break;
                case Step.InspectGenerator:
                    if (IsSelected(MachineInstanceKind.Processor, generatorFuelProcessorIndex))
                        Enter(Step.PickCoalFuel);
                    break;
                case Step.PickCoalFuel:
                    if (GameObject.Find("Fuel_석탄") != null
                        && (uiOutline == null || uiOutline.gameObject.name != "Fuel_석탄"))
                    {
                        ClearHighlights();
                        HighlightUI("Fuel_석탄");
                    }
                    if (ValidProcessor(generatorFuelProcessorIndex)
                        && world.Processors[generatorFuelProcessorIndex].SelectedFuelResourceId == coalId)
                        Enter(Step.ConnectGeneratorFuel);
                    break;
                case Step.ConnectGeneratorFuel:
                    if (HasBeltPath(world.CoreProcessorIndex, generatorFuelProcessorIndex)
                        && powerGrid != null && powerGrid.IsGeneratorActive(generatorNodeId))
                        Enter(Step.PickTower);
                    break;
                case Step.PickTower:
                    if (powerBuild != null && powerBuild.Mode == PowerBuildMode.TransmissionTower)
                        Enter(Step.PlaceTower);
                    break;
                case Step.PlaceTower:
                    if (powerGrid != null && powerGrid.TryGetNode(TowerCell, out PowerNodeRuntime tower)
                        && tower.Kind == PowerNodeKind.TransmissionTower)
                    {
                        towerNodeId = tower.Id;
                        Enter(Step.PickCable);
                    }
                    break;
                case Step.PickCable:
                    if (powerBuild != null && powerBuild.Mode == PowerBuildMode.Cable)
                        Enter(Step.ConnectPower);
                    break;
                case Step.ConnectPower:
                    if (HasPowerConnection(generatorNodeId, towerNodeId)) Enter(Step.ReturnToCore);
                    break;
                case Step.ReturnToCore:
                    if (IsSelected(MachineInstanceKind.Processor, world.CoreProcessorIndex))
                        Enter(Step.OpenResearch);
                    break;
                case Step.OpenResearch:
                    if (GameObject.Find("ResearchButton") != null
                        && (uiOutline == null || uiOutline.gameObject.name != "ResearchButton"))
                    {
                        ClearHighlights();
                        HighlightUI("ResearchButton");
                    }
                    if (Choi.Research.ResearchController.Instance != null
                        && Choi.Research.ResearchController.Instance.IsOpen)
                    {
                        Choi.Research.ResearchController.Instance.ReleaseModalInputForTutorial();
                        Enter(Step.Complete);
                    }
                    break;
            }
        }

        private void Enter(Step next)
        {
            ClearHighlights();
            step = next;
            enteredAt = Time.unscaledTime;
            progressText.text = $"튜토리얼  {Mathf.Min((int)next, 23)} / 23";

            switch (next)
            {
                case Step.Core:
                    SetCopy("공장의 중심, 코어", "빛나는 코어를 클릭하세요. 코어는 채굴한 자원을 저장하고 생산 시설에 공급합니다.");
                    HighlightWorld(GameObject.Find("Core"), 2.8f);
                    break;
                case Step.PickMiner:
                    SetCopy("구리 채굴 준비", "빛나는 채굴기 버튼을 누르세요. 채굴기는 광맥 위에만 배치할 수 있습니다.");
                    hud.OpenProductionForTutorial();
                    HighlightUI("PaletteButton_Miner");
                    break;
                case Step.PlaceMiner:
                    SetCopy("구리 광맥에 배치", "빛나는 구리 광맥 위로 채굴기를 옮긴 뒤 배치 확정을 누르세요.");
                    HighlightCell(CopperCell, 1.25f);
                    break;
                case Step.PickCoalMiner:
                    CancelPlacementMode();
                    GrantTutorialMachineCost("Miner");
                    SetCopy("석탄 채굴 준비 · 건설 재료 지원", "발전기를 가동할 석탄도 필요합니다. 지원된 재료로 채굴기 버튼을 다시 누르세요.");
                    hud.OpenProductionForTutorial();
                    HighlightUI("PaletteButton_Miner");
                    break;
                case Step.PlaceCoalMiner:
                    SetCopy("석탄 광맥에 배치", "왼쪽의 빛나는 검은 석탄 광맥 위에 두 번째 채굴기를 설치하세요.");
                    HighlightCell(CoalCell, 1.25f);
                    break;
                case Step.GatherStarterResources:
                    CancelPlacementMode();
                    SetCopy("구리와 석탄 확보", "제련로용 구리 원석 10개와 발전기용 석탄이 모일 때까지 기다리세요. 자원 버튼에서 수량을 확인할 수 있습니다.");
                    HighlightUI("SeoCoreResourceButton");
                    break;
                case Step.PickSmelter:
                    SetCopy("제련로 건설", "빛나는 제련로 버튼을 누르세요.");
                    hud.OpenProductionForTutorial();
                    HighlightUI("PaletteButton_Smelter");
                    break;
                case Step.PlaceSmelter:
                    SetCopy("예시 위치에 배치", "반투명 제련로가 표시된 칸에 실제 제련로를 배치하세요.");
                    CreatePlacementPreview();
                    HighlightCell(SmelterCell, 1.4f);
                    break;
                case Step.InspectSmelter:
                    CancelPlacementMode();
                    SetCopy("제련로 설정", "배치한 제련로를 클릭하세요.");
                    HighlightMachine(MachineInstanceKind.Processor, smelterIndex, 1.6f);
                    break;
                case Step.PickRecipe:
                    SetCopy("구리 제련 레시피", "레시피 설정을 누르고 ‘구리 원석 → 구리 괴’를 선택하세요.");
                    HighlightNamedChild("MachineInfoPanel", "RecipeButton");
                    break;
                case Step.ConnectInput:
                    SetCopy("코어 → 제련로", "벨트를 선택한 뒤 ‘시작’에서 누르고 반투명 화살표를 따라 ‘도착’까지 드래그하세요. 우클릭 드래그/휠 또는 두 손가락으로 화면을 움직일 수 있습니다.");
                    hud.OpenLogisticsForTutorial();
                    HighlightUI("PaletteButton_Belt");
                    CreateBeltGuide(InputBeltGuide, "시작\n코어", "도착\n제련로 입력");
                    break;
                case Step.ConnectOutput:
                    initialCopperIngot = CoreAmount(copperIngotId);
                    SetCopy("제련로 → 코어", "‘시작’에서 누르고 반투명 화살표를 따라 코어의 ‘도착’까지 드래그하세요. 화면 이동과 줌도 사용할 수 있습니다.");
                    hud.OpenLogisticsForTutorial();
                    HighlightUI("PaletteButton_Belt");
                    CreateBeltGuide(OutputBeltGuide, "시작\n제련로 출력", "도착\n코어");
                    break;
                case Step.PickGenerator:
                    GrantPowerTutorialSupport();
                    SetCopy("전력망 건설 · 지원품 도착", "발전기·송전탑 건설 재료와 전선용 구리선 5개가 도착했습니다. 먼저 빛나는 ‘발전기’를 선택하세요.");
                    hud.OpenPowerForTutorial();
                    HighlightUI("PowerAction_발전기");
                    break;
                case Step.PlaceGenerator:
                    SetCopy("발전기 설치", "빛나는 위치로 발전기를 옮긴 뒤 배치 확정을 누르세요. 파란 화살표 쪽이 연료 입력 방향입니다.");
                    HighlightCell(GeneratorCell, 1.2f);
                    break;
                case Step.InspectGenerator:
                    powerBuild?.SetMode(PowerBuildMode.None);
                    SetCopy("발전기 연료 설정", "방금 설치한 발전기를 클릭하세요. 발전기는 선택한 연료를 입력받아 전력을 생산합니다.");
                    HighlightWorld(GameObject.Find($"PowerNode_{generatorNodeId}_Generator"), 1.5f);
                    break;
                case Step.PickCoalFuel:
                    SetCopy("석탄을 연료로 선택", "레시피 설정을 누른 뒤 빛나는 ‘석탄’을 선택하세요.");
                    HighlightNamedChild("MachineInfoPanel", "RecipeButton");
                    break;
                case Step.ConnectGeneratorFuel:
                    SetCopy("발전기에 석탄 투입", "벨트를 선택하고 코어에서 발전기 파란 입력 방향까지 드래그하세요. 석탄이 도착해 발전기가 켜지면 다음 단계로 진행합니다.");
                    hud.OpenLogisticsForTutorial();
                    HighlightUI("PaletteButton_Belt");
                    CreateBeltGuide(GeneratorFuelBeltGuide, "시작\n코어", "도착\n발전기 연료");
                    break;
                case Step.PickTower:
                    SetCopy("송전탑 건설", "발전기에서 만든 전력을 기계로 보내려면 송전탑이 필요합니다. 빛나는 ‘송전탑’을 선택하세요.");
                    hud.OpenPowerForTutorial();
                    HighlightUI("PowerAction_송전탑");
                    break;
                case Step.PlaceTower:
                    SetCopy("송전탑 설치", "빛나는 위치로 송전탑을 옮긴 뒤 배치 확정을 누르세요. 표시되는 범위 안의 기계에 전력이 공급됩니다.");
                    HighlightCell(TowerCell, 1.2f);
                    break;
                case Step.PickCable:
                    powerBuild?.SetMode(PowerBuildMode.None);
                    SetCopy("전선 연결 준비", "전력 설비에서 빛나는 ‘전선’을 선택하세요. 연결 한 번에 구리선 1개를 사용합니다.");
                    hud.OpenPowerForTutorial();
                    HighlightUI("PowerAction_전선");
                    break;
                case Step.ConnectPower:
                    SetCopy("발전기 → 송전탑 연결", "발전기를 누른 채 송전탑까지 드래그해 전선으로 연결하세요.");
                    CreateBeltGuide(new[] { GeneratorCell, TowerCell }, "시작\n발전기", "도착\n송전탑");
                    break;
                case Step.ReturnToCore:
                    powerBuild?.SetMode(PowerBuildMode.None);
                    SetCopy("연구소 확인", "빛나는 코어를 한 번 누르세요.");
                    HighlightWorld(GameObject.Find("Core"), 2.8f);
                    break;
                case Step.OpenResearch:
                    SetCopy("연구소 열기", "화살표가 가리키는 ‘연구소’ 버튼을 누르세요.");
                    HighlightUI("ResearchButton");
                    break;
                case Step.Complete:
                    SetCopy("공장과 전력망 완성!", "발전기에 석탄이 공급되고 송전탑까지 전선으로 연결되었습니다. 송전탑 범위 안의 기계가 발전 전력을 사용할 수 있습니다.\n연구소로 티어를 올리며 공장을 늘려보세요.");
                    progressText.text = "튜토리얼 완료";
                    skipButton.GetComponentInChildren<TMP_Text>().text = "닫기";
                    skipButton.onClick.RemoveAllListeners();
                    skipButton.onClick.AddListener(FinishTutorial);
                    RestoreInput();
                    break;
            }
        }

        private void BuildPanel(Transform parent)
        {
            var panel = SeoUIFactory.CreatePanel(parent, "FactoryTutorialPanel", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(720f, 170f),
                new Color(0.015f, 0.08f, 0.12f, 0.97f));
            panel.rectTransform.pivot = new Vector2(0.5f, 1f);
            panelRoot = panel.gameObject;

            progressText = SeoUIFactory.CreateTMPText(panel.transform, "Progress", "튜토리얼", 17,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(progressText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -12f), new Vector2(420f, 26f));
            progressText.color = SeoUITheme.Current.Primary;

            titleText = SeoUIFactory.CreateTMPText(panel.transform, "Title", string.Empty, 26,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -42f), new Vector2(550f, 38f));

            bodyText = SeoUIFactory.CreateTMPText(panel.transform, "Description", string.Empty, 19,
                TextAnchor.UpperLeft);
            SeoUIFactory.SetRect(bodyText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -84f), new Vector2(560f, 70f));

            skipButton = SeoUIFactory.CreateTMPButton(panel.transform, "Skip", "건너뛰기", SkipTutorial,
                SeoUITheme.Current.Danger);
            SeoUIFactory.SetRect(skipButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-18f, -18f), new Vector2(112f, 50f));
            panel.transform.SetAsLastSibling();
        }

        private void SetCopy(string title, string body)
        {
            titleText.text = title;
            bodyText.text = body;
        }

        private void SkipTutorial()
        {
            ClearHighlights();
            panelRoot.SetActive(false);
            RestoreInput();
            enabled = false;
        }

        private void FinishTutorial()
        {
            ClearHighlights();
            panelRoot.SetActive(false);
            RestoreInput();
            enabled = false;
        }

        private void ApplyInputLocks()
        {
            if (buildRouter != null)
            {
                // 튜토리얼 중에도 언제든 화면을 살펴볼 수 있어야 한다. 현재 단계에서 허용되지
                // 않은 건설 행동은 버튼 잠금과 Placement/PathPermission이 별도로 차단한다.
                buildRouter.enabled = true;
                buildRouter.CameraInputEnabled = true;
            }

            var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null || !button.gameObject.scene.IsValid()) continue;
                if (!originalButtonStates.ContainsKey(button)) originalButtonStates[button] = button.interactable;
                bool allowed = IsAllowedButton(button.gameObject.name);
                button.interactable = allowed && originalButtonStates[button];
            }
        }

        private bool IsAllowedButton(string buttonName)
        {
            if (buttonName == "Skip") return true;
            switch (step)
            {
                case Step.PickMiner: return buttonName == "PaletteButton_Miner";
                case Step.PickCoalMiner: return buttonName == "PaletteButton_Miner";
                case Step.PlaceMiner:
                case Step.PlaceCoalMiner:
                case Step.PlaceSmelter:
                case Step.PlaceGenerator:
                case Step.PlaceTower: return buttonName == "ConfirmButton";
                case Step.GatherStarterResources:
                    return buttonName == "SeoCoreResourceButton" || buttonName == "Close";
                case Step.PickSmelter: return buttonName == "PaletteButton_Smelter";
                case Step.PickRecipe:
                case Step.PickCoalFuel:
                    return buttonName == "RecipeButton" || buttonName == "Recipe_SmeltCopperIngot"
                        || buttonName == "Fuel_석탄";
                case Step.ConnectInput:
                case Step.ConnectOutput:
                case Step.ConnectGeneratorFuel: return buttonName == "PaletteButton_Belt";
                case Step.PickGenerator: return buttonName == "PowerAction_발전기";
                case Step.PickTower: return buttonName == "PowerAction_송전탑";
                case Step.PickCable: return buttonName == "PowerAction_전선";
                case Step.OpenResearch: return buttonName == "ResearchButton";
                case Step.Complete: return buttonName == "Close";
                default: return false;
            }
        }

        private void GrantPowerTutorialSupport()
        {
            int coreIndex = driver.World.CoreProcessorIndex;
            if (!ValidProcessor(coreIndex)) return;
            var core = driver.World.Processors[coreIndex];
            GrantMachineBuildCost(core, "Generator");
            GrantMachineBuildCost(core, "TransmissionTower");
            if (copperWireId >= 0)
                core.InputBuffer[copperWireId] = System.Math.Min(core.InputBuffer[copperWireId] + 5, core.Capacity);
        }

        private void GrantTutorialMachineCost(string machineKey)
        {
            int coreIndex = driver.World.CoreProcessorIndex;
            if (!ValidProcessor(coreIndex)) return;
            GrantMachineBuildCost(driver.World.Processors[coreIndex], machineKey);
        }

        private void GrantMachineBuildCost(ProcessorInstance core, string machineKey)
        {
            if (!driver.World.Database.TryGetMachineId(machineKey, out int machineId)) return;
            var costs = driver.World.Database.Machines[machineId].BuildCost;
            if (costs == null) return;
            for (int i = 0; i < costs.Length; i++)
            {
                int resourceId = costs[i].ResourceId;
                core.InputBuffer[resourceId] = System.Math.Min(
                    core.InputBuffer[resourceId] + costs[i].Amount, core.Capacity);
            }
        }

        private void RestoreInput()
        {
            foreach (var pair in originalButtonStates)
                if (pair.Key != null) pair.Key.interactable = pair.Value;
            originalButtonStates.Clear();

            if (buildRouter != null)
            {
                buildRouter.enabled = true;
                buildRouter.CameraInputEnabled = true;
            }
            if (legacyTapInput != null) legacyTapInput.enabled = legacyTapWasEnabled;
            if (MachineGhostTool.PlacementPermission == AllowsPlacement)
                MachineGhostTool.PlacementPermission = null;
            if (BeltDragTool.PathPermission == AllowsBeltPath)
                BeltDragTool.PathPermission = null;
            if (PowerBuildController.PlacementPermission == AllowsPowerPlacement)
                PowerBuildController.PlacementPermission = null;
        }

        private bool AllowsPlacement(string machineId, Vector2Int cell)
        {
            if (step == Step.PlaceMiner) return machineId == "Miner" && cell == CopperCell;
            if (step == Step.PlaceCoalMiner) return machineId == "Miner" && cell == CoalCell;
            if (step == Step.PlaceSmelter) return machineId == "Smelter" && cell == SmelterCell;
            return false;
        }

        private bool AllowsBeltPath(IReadOnlyList<Vector2Int> path)
        {
            if ((step != Step.ConnectInput && step != Step.ConnectOutput && step != Step.ConnectGeneratorFuel)
                || path == null || path.Count < 2)
                return false;
            var required = step == Step.ConnectInput ? InputBeltGuide
                : step == Step.ConnectOutput ? OutputBeltGuide : GeneratorFuelBeltGuide;
            return MatchesPath(path, required, false) || MatchesPath(path, required, true);
        }

        private bool AllowsPowerPlacement(PowerBuildMode mode, Vector2Int cell)
        {
            if (step == Step.PlaceGenerator) return mode == PowerBuildMode.Generator && cell == GeneratorCell;
            if (step == Step.PlaceTower) return mode == PowerBuildMode.TransmissionTower && cell == TowerCell;
            return false;
        }

        private bool HasPowerConnection(int firstNodeId, int secondNodeId)
        {
            if (powerGrid == null || firstNodeId < 0 || secondNodeId < 0) return false;
            for (int i = 0; i < powerGrid.Connections.Count; i++)
            {
                PowerConnectionRuntime connection = powerGrid.Connections[i];
                if ((connection.FromNodeId == firstNodeId && connection.ToNodeId == secondNodeId)
                    || (connection.FromNodeId == secondNodeId && connection.ToNodeId == firstNodeId)) return true;
            }
            return false;
        }

        private static bool MatchesPath(IReadOnlyList<Vector2Int> actual, IReadOnlyList<Vector2Int> expected, bool reverse)
        {
            if (actual.Count != expected.Count) return false;
            for (int i = 0; i < actual.Count; i++)
            {
                int expectedIndex = reverse ? expected.Count - 1 - i : i;
                if (actual[i] != expected[expectedIndex]) return false;
            }
            return true;
        }

        private void CancelPlacementMode()
        {
            if (machineTool != null) machineTool.CancelPlacement();
            if (buildRouter != null && buildRouter.CurrentMode == BuildInputRouter.Mode.PlaceMachine)
                buildRouter.SetMode(BuildInputRouter.Mode.None);
        }

        private void HighlightUI(string objectName)
        {
            var target = GameObject.Find(objectName);
            if (target == null) return;
            uiGraphic = target.GetComponent<Graphic>();
            if (uiGraphic == null) uiGraphic = target.GetComponentInChildren<Graphic>(true);
            if (uiGraphic == null) return;
            uiOutline = uiGraphic.gameObject.AddComponent<Outline>();
            uiOutline.effectDistance = new Vector2(6f, -6f);
            uiOutline.useGraphicAlpha = false;
            CreateFingerGuide(uiGraphic.rectTransform);
        }

        private void HighlightNamedChild(string rootName, string childName)
        {
            var root = GameObject.Find(rootName);
            if (root == null) return;
            var child = FindDeepChild(root.transform, childName);
            if (child != null) HighlightUI(child.gameObject.name);
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == childName) return all[i];
            return null;
        }

        private void HighlightMachine(MachineInstanceKind kind, int index, float radius)
        {
            HighlightWorld(GameObject.Find($"{kind}_{index}"), radius);
        }

        private void HighlightWorld(GameObject target, float radius)
        {
            if (target == null) return;
            worldHighlight = CreateRing(target.transform.position, radius);
            worldHighlight.transform.SetParent(target.transform, true);
            CreateFingerGuide(target.transform.position, radius);
        }

        private void HighlightCell(Vector2Int cell, float radius)
        {
            Vector3 position = GridUtility.CellToWorldCenter(cell, 0.06f);
            worldHighlight = CreateRing(position, radius);
            CreateFingerGuide(position, radius);
        }

        // 글을 읽지 않아도 목표를 찾을 수 있도록 UI 도형만으로 만든 큰 화살표를 표시한다.
        private void CreateFingerGuide(RectTransform target)
        {
            fingerUiTarget = target;
            fingerTracksWorld = false;
            CreateFingerVisual();
        }

        private void CreateFingerGuide(Vector3 worldPosition, float worldRadius)
        {
            fingerWorldTarget = worldPosition;
            fingerWorldRadius = worldRadius;
            fingerTracksWorld = true;
            CreateFingerVisual();
        }

        private void CreateFingerVisual()
        {
            if (fingerGuide != null) Destroy(fingerGuide);
            Canvas canvas = panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;
            fingerCanvas = canvas.transform as RectTransform;
            fingerGuide = new GameObject("TutorialArrowGuide", typeof(RectTransform), typeof(CanvasGroup),
                typeof(Image));
            RectTransform root = fingerGuide.GetComponent<RectTransform>();
            root.SetParent(canvas.transform, false);
            root.sizeDelta = new Vector2(92f, 120f);
            root.localRotation = Quaternion.Euler(0f, 0f, -135f);
            fingerGuide.GetComponent<CanvasGroup>().blocksRaycasts = false;
            var arrow = fingerGuide.GetComponent<Image>();
            arrow.sprite = GetArrowSprite();
            arrow.color = new Color(0.22f, 0.4f, 1f, 1f);
            arrow.preserveAspect = true;
            arrow.raycastTarget = false;
            var outline = fingerGuide.AddComponent<Outline>();
            outline.effectColor = new Color(0.03f, 0.08f, 0.2f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = false;
            root.SetAsLastSibling();
            UpdateFingerPosition();
        }

        private void HideArrowGuide()
        {
            if (fingerGuide != null) Destroy(fingerGuide);
            fingerGuide = null;
            fingerCanvas = null;
            fingerUiTarget = null;
        }

        private Sprite GetArrowSprite()
        {
            if (arrowSprite != null) return arrowSprite;
            const int width = 64;
            const int height = 96;
            arrowTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "TutorialChevronTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[width * height];
            var clear = new Color32(255, 255, 255, 0);
            var solid = new Color32(255, 255, 255, 255);
            Vector2[] polygon =
            {
                new Vector2(22f, 92f), new Vector2(42f, 92f), new Vector2(42f, 53f),
                new Vector2(60f, 53f), new Vector2(32f, 4f), new Vector2(4f, 53f),
                new Vector2(22f, 53f),
            };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = IsPointInsidePolygon(new Vector2(x + .5f, y + .5f), polygon)
                    ? solid : clear;
            arrowTexture.SetPixels32(pixels);
            arrowTexture.Apply(false, true);
            arrowSprite = Sprite.Create(arrowTexture, new Rect(0f, 0f, width, height), new Vector2(.5f, .5f), 100f);
            arrowSprite.name = "TutorialChevronSprite";
            return arrowSprite;
        }

        private static bool IsPointInsidePolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, previous = polygon.Count - 1; i < polygon.Count; previous = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[previous];
                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private void UpdateFingerPosition()
        {
            if (fingerGuide == null || fingerCanvas == null) return;
            Canvas canvas = fingerCanvas.GetComponent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            Vector2 screenPoint;
            float ringRadius = 0f;
            if (fingerTracksWorld)
            {
                Camera worldCamera = Camera.main;
                if (worldCamera == null) return;
                screenPoint = worldCamera.WorldToScreenPoint(fingerWorldTarget);
                Vector2 radiusPoint = worldCamera.WorldToScreenPoint(
                    fingerWorldTarget + worldCamera.transform.right * fingerWorldRadius);
                ringRadius = Vector2.Distance(screenPoint, radiusPoint);
            }
            else
            {
                if (fingerUiTarget == null || !fingerUiTarget.gameObject.activeInHierarchy)
                {
                    HideArrowGuide();
                    return;
                }
                screenPoint = RectTransformUtility.WorldToScreenPoint(canvasCamera, fingerUiTarget.TransformPoint(fingerUiTarget.rect.center));
            }
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(fingerCanvas, screenPoint, canvasCamera, out Vector2 local))
            {
                float canvasScale = fingerCanvas.lossyScale.x;
                if (canvasScale > 0.001f) ringRadius /= canvasScale;
                Vector2 diagonal = new Vector2(0.7071f, -0.7071f);
                // 화살표는 오른쪽 아래에 머물며 대각선 왼쪽 위로 원 테두리(또는 UI 중심)를 가리킨다.
                float bob = (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) * 14f;
                float distance = (fingerTracksWorld ? ringRadius : 0f) + 52f + bob;
                fingerGuide.GetComponent<RectTransform>().anchoredPosition = local + diagonal * distance;
            }
        }

        private static GameObject CreateRing(Vector3 position, float radius)
        {
            var go = new GameObject("TutorialWorldHighlight");
            go.transform.position = position;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 65;
            line.widthMultiplier = 0.09f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(0.1f, 1f, 0.9f, 0.95f);
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = Mathf.PI * 2f * i / (line.positionCount - 1);
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            return go;
        }

        private void CreatePlacementPreview()
        {
            placementPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placementPreview.name = "TutorialSmelterPreview";
            placementPreview.transform.position = GridUtility.CellToWorldCenter(SmelterCell, 0.5f);
            placementPreview.transform.localScale = new Vector3(0.88f, 1f, 0.88f);
            Destroy(placementPreview.GetComponent<Collider>());
            var renderer = placementPreview.GetComponent<Renderer>();
            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = new Color(0.15f, 0.95f, 1f, 0.28f);
            renderer.material = material;

            var label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(placementPreview.transform, false);
            label.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            label.transform.localScale = Vector3.one * 0.12f;
            label.font = SeoUITheme.Current.FontAsset;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 44;
            label.color = new Color(0.3f, 1f, 1f, 0.95f);
            label.text = "제련로\n배치 위치";
        }

        private void CreateBeltGuide(IReadOnlyList<Vector2Int> cells, string startLabel, string endLabel)
        {
            beltGuide = new GameObject("TutorialBeltGuide");
            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = new Color(0.1f, 0.95f, 1f, 0.52f);

            var line = beltGuide.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = cells.Count;
            line.widthMultiplier = 0.22f;
            line.material = material;
            line.startColor = line.endColor = new Color(0.1f, 0.95f, 1f, 0.48f);
            for (int i = 0; i < cells.Count; i++)
                line.SetPosition(i, GridUtility.CellToWorldCenter(cells[i], 0.13f));

            for (int i = 0; i < cells.Count - 1; i++)
            {
                Vector3 from = GridUtility.CellToWorldCenter(cells[i], 0.16f);
                Vector3 to = GridUtility.CellToWorldCenter(cells[i + 1], 0.16f);
                CreateArrowHead(beltGuide.transform, (from + to) * 0.5f, to - from, material);
            }

            var startRing = CreateRing(GridUtility.CellToWorldCenter(cells[0], 0.1f), 0.48f);
            startRing.name = "BeltGuideStart";
            startRing.transform.SetParent(beltGuide.transform, true);
            var endRing = CreateRing(GridUtility.CellToWorldCenter(cells[cells.Count - 1], 0.1f), 0.48f);
            endRing.name = "BeltGuideEnd";
            endRing.transform.SetParent(beltGuide.transform, true);
            CreateWorldLabel(beltGuide.transform, cells[0], startLabel, new Color(0.3f, 1f, 0.55f, 1f));
            CreateWorldLabel(beltGuide.transform, cells[cells.Count - 1], endLabel, new Color(1f, 0.78f, 0.2f, 1f));
        }

        private static void CreateArrowHead(Transform parent, Vector3 position, Vector3 direction, Material material)
        {
            direction.y = 0f;
            direction.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            var mesh = new Mesh { name = "TutorialArrow" };
            mesh.vertices = new[]
            {
                direction * 0.38f,
                -direction * 0.24f + right * 0.25f,
                -direction * 0.24f - right * 0.25f,
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };
            mesh.RecalculateBounds();
            var arrow = new GameObject("Arrow", typeof(MeshFilter), typeof(MeshRenderer));
            arrow.transform.SetParent(parent, false);
            arrow.transform.position = position;
            arrow.GetComponent<MeshFilter>().sharedMesh = mesh;
            arrow.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void CreateWorldLabel(Transform parent, Vector2Int cell, string value, Color color)
        {
            var label = new GameObject("GuideLabel").AddComponent<TextMeshPro>();
            label.transform.SetParent(parent, false);
            label.transform.position = GridUtility.CellToWorldCenter(cell, 0.28f);
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            label.transform.localScale = Vector3.one * 0.085f;
            label.font = SeoUITheme.Current.FontAsset;
            label.alignment = TextAlignmentOptions.Bottom;
            label.fontSize = 46;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.text = value;
        }

        private void AnimateHighlights()
        {
            UpdateFingerPosition();
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            if (uiOutline != null)
            {
                uiOutline.effectColor = Color.Lerp(new Color(0f, 0.65f, 1f, 0.65f), Color.white, pulse);
                uiOutline.effectDistance = Vector2.one * Mathf.Lerp(3f, 8f, pulse);
            }
            if (worldHighlight != null)
            {
                var line = worldHighlight.GetComponent<LineRenderer>();
                if (line != null)
                {
                    Color color = Color.Lerp(new Color(0f, 0.65f, 1f, 0.45f), new Color(0.2f, 1f, 0.85f, 1f), pulse);
                    line.startColor = line.endColor = color;
                    line.widthMultiplier = Mathf.Lerp(0.06f, 0.16f, pulse);
                }
            }
            if (placementPreview != null)
                placementPreview.transform.localScale = Vector3.one * Mathf.Lerp(0.86f, 0.94f, pulse);
        }

        private void ClearHighlights()
        {
            if (uiOutline != null) Destroy(uiOutline);
            if (worldHighlight != null) Destroy(worldHighlight);
            if (placementPreview != null) Destroy(placementPreview);
            if (beltGuide != null) Destroy(beltGuide);
            if (fingerGuide != null) Destroy(fingerGuide);
            uiOutline = null;
            uiGraphic = null;
            worldHighlight = null;
            placementPreview = null;
            beltGuide = null;
            fingerGuide = null;
            fingerCanvas = null;
            fingerUiTarget = null;
        }

        private bool IsSelected(MachineInstanceKind kind, int index)
        {
            var ui = UIManager.Instance;
            return ui != null && ui.HasSelection && ui.IsMachineInfoOpen
                && ui.SelectedKind == kind && ui.SelectedIndex == index;
        }

        private bool TryGetOccupant(Vector2Int cell, CellOccupantType type, out int index)
        {
            index = -1;
            if (!driver.World.Grid.TryGetOccupant(cell, out var occupant) || occupant.Type != type) return false;
            index = occupant.InstanceIndex;
            return true;
        }

        private bool IsMachine(int processorIndex, string machineKey)
        {
            if (!ValidProcessor(processorIndex)) return false;
            var processor = driver.World.Processors[processorIndex];
            return driver.World.Database.Machines[processor.MachineId].Key == machineKey;
        }

        private bool ValidProcessor(int index)
        {
            return index >= 0 && index < driver.World.Processors.Count && driver.World.Processors[index] != null;
        }

        private int CoreAmount(int resourceId)
        {
            int coreIndex = driver.World.CoreProcessorIndex;
            if (!ValidProcessor(coreIndex) || resourceId < 0) return 0;
            return driver.World.Processors[coreIndex].InputBuffer[resourceId];
        }

        private Vector2Int GetSmelterPort(bool output)
        {
            if (!ValidProcessor(smelterIndex)) return SmelterCell + (output ? Vector2Int.right : Vector2Int.left);
            var processor = driver.World.Processors[smelterIndex];
            return processor.Anchor + (output ? processor.Facing : -processor.Facing);
        }

        private bool HasBeltPath(int sourceProcessor, int targetProcessor)
        {
            var segments = driver.World.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                var start = segments[i];
                if (start == null || start.SourceProcessorId != sourceProcessor) continue;
                var visited = new HashSet<int>();
                var current = start;
                while (current != null && visited.Add(current.Id))
                {
                    if (current.TargetProcessorId == targetProcessor) return true;
                    if (!current.NextSegmentId.HasValue) break;
                    int next = current.NextSegmentId.Value;
                    current = next >= 0 && next < segments.Count ? segments[next] : null;
                }
            }
            return false;
        }
    }
}
