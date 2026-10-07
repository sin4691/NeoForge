using Factory.Building;
using Factory.Buildings;
using Factory.Rendering;
using UnityEngine;

namespace Factory.Simulation
{
    // 게임 시작 시 미리 놓여있는 중심 저장 거점("코어")을 만든다. 플레이어가 짓는 게 아니라
    // 처음부터 존재하는 마인더스트리류 코어 개념. 레시피를 배정하지 않은 ProcessorInstance라
    // 아무 자원이나 받아서 버퍼에 쌓아두기만 하고 아무것도 생산하지 않는다 — 별도 인스턴스
    // 타입 없이 기존 ProcessorInstance를 그대로 재사용하고, UniversalPorts만 켠다(4면 입출력).
    // 2x2 칸을 차지하며, 앵커를 (-1,-1)로 둬서 블록 중심이 정확히 월드 원점에 오게 한다
    // (카메라 초기 프레이밍이 원점 중심이라 자연스럽게 화면 가운데 놓임).
    public class CoreSpawner : MonoBehaviour
    {
        private static readonly Vector2Int Anchor = new Vector2Int(-1, -1);
        private static readonly Vector2Int Footprint = new Vector2Int(2, 2);

        [SerializeField] private SimulationDriver driver;
        [SerializeField] private GameObject corePrefab;

        // 테스트용 — 켜면 콘크리트 75개 대신 모든 자원을 아래 수량만큼 코어에 채워서 시작한다.
        // 밸런스 확인 없이 아무 기계나 바로 지어보고 싶을 때만 켜고, 실제 밸런스 테스트 시엔
        // 꺼야 한다(정식 시작 지급량은 아래 Start()의 콘크리트 75개 로직).
        [SerializeField] private bool debugGiveAllResources = false;
        [SerializeField] private int debugResourceAmount = 500;

        private void Start()
        {
            if (driver == null || driver.World == null) return;

            var db = driver.World.Database;
            if (!db.TryGetMachineId("Core", out int machineId)) return;

            var grid = driver.World.Grid;
            var cells = GridUtility.GetFootprintCells(Anchor, Footprint);
            if (!grid.IsFootprintFree(cells)) return; // 이미 있으면(재실행 등) 건너뜀

            var core = new ProcessorInstance(db.ResourceCount)
            {
                MachineId = machineId,
                RecipeId = -1,
                UniversalPorts = true,
                Anchor = Anchor,
                Footprint = Footprint,
                Capacity = 99999999, // 창고 역할이라 일반 기계 버퍼보다 훨씬 크게.
            };
            int index = driver.World.AddProcessor(core);
            driver.World.CoreProcessorIndex = index; // 채굴기 원격 전송(MinerSystem)이 참조하는 대상

            if (debugGiveAllResources)
            {
                for (int r = 0; r < core.InputBuffer.Length; r++) core.InputBuffer[r] = debugResourceAmount;
            }
            else if (db.TryGetResourceId("Concrete", out int concreteId))
            {
                // 시작 지급 — 채굴기 1대(콘크리트 15) + 벨트 20칸(칸당 3, 총 60)을 지을 수 있는
                // 정확히 그만큼(75)만 쥐여준다. 더도 덜도 말고 "일단 첫 채굴기 하나는 자력으로
                // 지을 수 있게" 하는 최소 부트스트랩 — 그 이후로는 채굴/제련한 걸로 스스로 굴려야 한다.
                core.InputBuffer[concreteId] = 75;
            }
            grid.RegisterBuildingFootprint(cells, CellOccupantType.Processor, index);

            Vector3 worldPos = GridUtility.GetFootprintCenter(Anchor, Footprint, 0.75f);
            string coreKey = db.Machines[machineId].PrefabName; // "Prefab_Core" (AddressablesSetup)

            GameObject go;
            if (string.IsNullOrEmpty(coreKey) && corePrefab != null)
            {
                go = Instantiate(corePrefab, worldPos, Quaternion.identity); // 임시 다리(직접 참조)
            }
            else
            {
                go = new GameObject();
                go.transform.position = worldPos;

                var boxCollider = go.AddComponent<BoxCollider>();
                boxCollider.center = new Vector3(0f, 0.5f, 0f);
                boxCollider.size = new Vector3(Footprint.x, 1f, Footprint.y);

                var placeholder = BuildVisuals.CreateBox(worldPos, new Vector3(Footprint.x, 1f, Footprint.y),
                    new Color(0.2f, 0.45f, 0.7f), go.transform, withCollider: false);
                placeholder.name = "Placeholder";

                go.AddComponent<AddressableModelMount>().Mount(coreKey, placeholder);
            }
            go.name = "Core";

            var view = go.GetComponent<MachineView>() ?? go.AddComponent<MachineView>();
            view.Initialize(MachineInstanceKind.Processor, index, driver);
        }
    }
}
