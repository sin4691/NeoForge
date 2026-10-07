using Factory.Building;
using Factory.Buildings;
using Factory.Simulation;
using Factory.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Seo.UI
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] private float refreshInterval = 0.2f;
        [SerializeField] private float discoveryInterval = 0.5f;

        private SimulationDriver driver;
        private BuildInputRouter buildInputRouter;
        private MachineGhostTool machineGhostTool;
        private Camera targetCamera;
        private MachineInfoPanel machineInfoPanel;
        private GhostPortPreview ghostPortPreview;
        private MachineInstanceKind selectedKind;
        private int selectedIndex = -1;
        private float nextRefreshTime;
        private float nextDiscoveryTime;
        private bool selectionPending;

        public bool HasSelection => selectedIndex >= 0;
        public MachineInstanceKind SelectedKind => selectedKind;
        public int SelectedIndex => selectedIndex;
        public bool IsMachineInfoOpen => machineInfoPanel != null && machineInfoPanel.IsOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            SceneManager.sceneLoaded -= CreateRuntimeInstance;
            SceneManager.sceneLoaded += CreateRuntimeInstance;
        }

        private static void CreateRuntimeInstance(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main") return;
            if (FindFirstObjectByType<UIManager>() != null) return;
            new GameObject("[Seo] UIManager").AddComponent<UIManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DiscoverSceneContext();
            EnsurePanel();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextDiscoveryTime)
            {
                DiscoverSceneContext();
                AttachWorldIndicators();
                nextDiscoveryTime = Time.unscaledTime + discoveryInterval;
            }

            HandleMachineSelection();

            if (machineInfoPanel == null || !machineInfoPanel.IsOpen || Time.unscaledTime < nextRefreshTime) return;
            RefreshSelected();
            nextRefreshTime = Time.unscaledTime + refreshInterval;
        }

        private void LateUpdate()
        {
            if (!selectionPending) return;
            selectionPending = false;

            // 기존 MachineView는 프로세서 탭 시 레시피 창을 즉시 연다. 공동 코드를 수정하지
            // 않고 유지하되, 일반 탭에서는 상세 정보가 먼저 보이도록 같은 프레임 끝에 닫는다.
            RecipeSelectionPanel.Instance?.Close();
            RefreshSelected();
        }

        public void ShowMachine(MachineInstanceKind kind, int instanceIndex)
        {
            if (!FactoryTutorialController.AllowsMachineSelection(kind, instanceIndex)) return;
            selectedKind = kind;
            selectedIndex = instanceIndex;
            selectionPending = true;
        }

        public void CloseMachineInfo()
        {
            selectedIndex = -1;
            if (machineInfoPanel != null) machineInfoPanel.Close();
        }

        private void DiscoverSceneContext()
        {
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (buildInputRouter == null) buildInputRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineGhostTool == null) machineGhostTool = FindFirstObjectByType<MachineGhostTool>();
            if (targetCamera == null) targetCamera = Camera.main;

            if (machineGhostTool != null)
            {
                if (ghostPortPreview == null) ghostPortPreview = gameObject.AddComponent<GhostPortPreview>();
                ghostPortPreview.Initialize(machineGhostTool);
            }
        }

        private void HandleMachineSelection()
        {
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return;
            if (driver == null || driver.World == null || targetCamera == null) return;
            if (buildInputRouter != null && buildInputRouter.IsToolActive) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = targetCamera.ScreenPointToRay(pointer.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

            var view = hit.collider.GetComponentInParent<MachineView>();
            if (view == null || !MachineViewAdapter.TryRead(view, out var selection)) return;
            ShowMachine(selection.Kind, selection.InstanceIndex);
        }

        private void AttachWorldIndicators()
        {
            if (driver == null || driver.World == null) return;

            var views = FindObjectsByType<MachineView>(FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                var indicator = view.GetComponent<MachineWorldIndicator>();
                if (indicator != null && indicator.IsInitialized) continue;
                if (!MachineViewAdapter.TryRead(view, out var selection)) continue;

                if (indicator == null) indicator = view.gameObject.AddComponent<MachineWorldIndicator>();
                indicator.Initialize(selection.Kind, selection.InstanceIndex, selection.Driver);
            }
        }

        private void RefreshSelected()
        {
            EnsurePanel();
            if (!MachineInfoPresenter.TryBuild(driver, selectedKind, selectedIndex, out var data))
            {
                CloseMachineInfo();
                return;
            }

            machineInfoPanel.Render(data);
            machineInfoPanel.SetDemolitionAllowed(CanDemolishSelected());
            machineInfoPanel.Open();
        }

        private void EnsurePanel()
        {
            if (machineInfoPanel != null) return;

            Canvas canvas = null;
            var namedCanvas = GameObject.Find("HUDCanvas");
            if (namedCanvas != null) canvas = namedCanvas.GetComponent<Canvas>();

            if (canvas == null)
            {
                var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i].renderMode == RenderMode.WorldSpace) continue;
                    canvas = canvases[i];
                    break;
                }
            }

            if (canvas == null)
            {
                var canvasObject = new GameObject(
                    "[Seo] HUDCanvas",
                    typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler),
                    typeof(UnityEngine.UI.GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            // 런타임 생성 패널도 씬 HUD와 동일한 SafeArea 아래에 둔다. Canvas 바로 아래에
            // 붙이면 노치/홈 인디케이터가 있는 기기에서 우측 패널과 닫기 버튼이 잘릴 수 있다.
            Transform panelParent = canvas.transform;
            var safeArea = canvas.transform.Find("SafeArea");
            if (safeArea == null)
            {
                var safeAreaObject = new GameObject("SafeArea", typeof(RectTransform));
                safeAreaObject.transform.SetParent(canvas.transform, false);
                var safeRect = safeAreaObject.GetComponent<RectTransform>();
                safeRect.anchorMin = Vector2.zero;
                safeRect.anchorMax = Vector2.one;
                safeRect.offsetMin = Vector2.zero;
                safeRect.offsetMax = Vector2.zero;
                safeAreaObject.AddComponent<SafeAreaFitter>();
                safeArea = safeAreaObject.transform;
            }
            panelParent = safeArea;
            machineInfoPanel = MachineInfoPanel.CreateRuntime(panelParent);
            machineInfoPanel.CloseRequested += CloseMachineInfo;
            machineInfoPanel.RecipeRequested += OpenRecipeSelection;
            machineInfoPanel.CoreResourcesRequested += OpenCoreResources;
            machineInfoPanel.DemolishRequested += DemolishSelected;
        }

        private void OpenCoreResources()
        {
            if (driver == null || driver.World == null || selectedKind != MachineInstanceKind.Processor
                || selectedIndex != driver.World.CoreProcessorIndex) return;
            FindFirstObjectByType<FactoryHudController>()?.OpenCoreResourcePanel();
        }

        private bool CanDemolishSelected()
        {
            if (driver == null || driver.World == null || selectedIndex < 0) return false;
            if (selectedKind == MachineInstanceKind.Miner)
                return selectedIndex < driver.World.Miners.Count && driver.World.Miners[selectedIndex] != null;
            return selectedIndex < driver.World.Processors.Count
                && driver.World.Processors[selectedIndex] != null
                && !driver.World.Processors[selectedIndex].IsGeneratorFuelPort
                && selectedIndex != driver.World.CoreProcessorIndex;
        }

        private void DemolishSelected()
        {
            if (!CanDemolishSelected()) return;

            var world = driver.World;
            if (selectedKind == MachineInstanceKind.Miner)
            {
                DestroyMachineVisual($"{MachineInstanceKind.Miner}_{selectedIndex}");
                world.Grid.UnregisterOccupant(CellOccupantType.Miner, selectedIndex);
                world.RemoveMiner(selectedIndex);
            }
            else
            {
                DestroyMachineVisual($"{MachineInstanceKind.Processor}_{selectedIndex}");
                world.Grid.UnregisterOccupant(CellOccupantType.Processor, selectedIndex);
                world.RemoveProcessor(selectedIndex);
            }

            CloseMachineInfo();
        }

        private static void DestroyMachineVisual(string objectName)
        {
            var visual = GameObject.Find(objectName);
            if (visual != null) Destroy(visual);
        }

        private void OpenRecipeSelection()
        {
            if (driver == null || driver.World == null || selectedKind != MachineInstanceKind.Processor) return;
            if (selectedIndex < 0 || selectedIndex >= driver.World.Processors.Count) return;

            var processor = driver.World.Processors[selectedIndex];
            if (processor == null || processor.UniversalPorts) return;

            machineInfoPanel.Close();
            if (processor.IsGeneratorFuelPort)
            {
                RecipeSelectionPanel.Instance?.OpenGenerator(selectedIndex);
                return;
            }
            string machineKey = driver.World.Database.Machines[processor.MachineId].Key;
            RecipeSelectionPanel.Instance?.Open(selectedIndex, machineKey);
        }
    }
}
