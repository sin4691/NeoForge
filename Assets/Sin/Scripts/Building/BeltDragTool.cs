using System;
using System.Collections.Generic;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 프레스+드래그로 벨트 경로를 그리고(코너는 BeltPathBuilder가 자동 처리), 릴리즈 시
    // 실제 BeltSegment로 커밋한다. 채굴기/제련로는 고정된 입력면/출력면이 있어서(Facing),
    // 벨트가 정확히 그 면에 닿아야만 연결된다 — 어느 쪽이든 드래그 방향대로 연결되던
    // 예전 방식과 달리 모호함이 없다. 코어(UniversalPorts)와 기존 벨트는 예외적으로
    // 어느 쪽에 닿아도 되고, 드래그 시작/끝 위치로 소스/타겟이 갈린다.
    //
    // 파일이 너무 커져서(1000줄+) 역할별로 나눴다 — 전부 같은 MonoBehaviour의 partial
    // 조각이라 동작/직렬화(SerializeField, 프리팹 참조)는 전혀 안 바뀐다:
    //  - BeltDragTool.cs        (이 파일) 필드 + 입력 진입점
    //  - BeltDragTool.Preview.cs   드래그 중 미리보기 그리기
    //  - BeltDragTool.Commit.cs    릴리즈 시 실제 배선(Commit) + 기존 세그먼트 되그리기
    //  - BeltDragTool.Ports.cs     포트/역할 판정(ResolveEndpointRole, TryFindAdjacentOccupant 등)
    //  - BeltDragTool.Visuals.cs   순수 지오메트리 계산 + 스트립/코너 오브젝트 생성
    public partial class BeltDragTool : MonoBehaviour, IBuildTool
    {
        public static Func<IReadOnlyList<Vector2Int>, bool> PathPermission { get; set; }
        // 전력 시설처럼 WorldGrid 밖에서 관리되는 오브젝트의 점유 판정 확장점.
        public static Func<Vector2Int, bool> ExternalCellBlocked { get; set; }
        // Seo님의 BeltConnectionFeedback이 자기만의 판정을 새로 만드는 대신 이 타입/메서드들을
        // 그대로 가져다 쓴다 — 판정 로직이 두 곳에 따로 있으면 한쪽만 고쳤을 때 어긋나는
        // 버그가 난다(이미 두 번 겪음: 미리보기 색 깜빡임, 자원 부족 미반영).
        public enum EndpointRole
        {
            None,
            Source,
            Target,
        }

        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private float previewThickness = 0.5f;
        [SerializeField] private float committedThickness = 0.6f;
        // BeltItemVisual 프리팹의 반지름(지름 0.25의 절반)과 맞춰야 한다 — 벨트 위에 아이템이
        // "위에 얹힌" 것처럼 보이려면 벨트 두께의 절반 + 이 반지름만큼 띄워야 한다.
        [SerializeField] private float itemVisualRadius = 0.125f;
        [SerializeField] private Color previewColor = new Color(0.2f, 0.9f, 0.3f, 0.8f);
        [SerializeField] private Color invalidPreviewColor = new Color(0.95f, 0.2f, 0.15f, 0.8f);
        [SerializeField] private Color committedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        [SerializeField] private GameObject itemVisualPrefab;
        [SerializeField] private GameObject stripPrefab;
        // 코너(90도 꺾이는 칸) 프리팹. cornerPrefab = 우회전(진입 아래→이탈 오른쪽), cornerLeftPrefab = 좌회전.
        // 좌회전 그림은 우회전 PNG를 좌우 반전해서 만들면 된다. cornerLeftPrefab 이 없으면 대칭 타일로 간주해
        // cornerPrefab 을 양쪽에 쓴다(화살표 없는 코너용). 둘 다 없으면 직선 조각 2개로 폴백.
        [SerializeField] private GameObject cornerPrefab;
        [SerializeField] private GameObject cornerLeftPrefab;
        // 납작 벨트 Quad 가 놓이는 높이. 바닥 타일이 두께가 있어서 0 이면 파묻힌다 — 타일 윗면 위로 올린다.
        [SerializeField] private float beltSurfaceY = 0.06f;
        // 코너 Quad 를 살짝 키워 직선과의 이음새를 덮는다(1 = 그대로, 1.08 = 8% 크게).
        [SerializeField] private float cornerScale = 1.08f;
        // 벨트 한 칸 놓는 데 드는 콘크리트(건설 비용). 기계 건설 비용(MachineGhostTool)과
        // 같은 원리로 코어 창고에서 차감한다.
        [SerializeField] private int concreteCostPerTile = 3;

        private bool warnedNonConcreteBeltCost;

        // 벨트 한 칸 건설비. 기본은 Bae님 데이터(Machines.json의 "Belt" — SO Assets/Bae/Data/
        // Machines/Belt.asset)의 콘크리트 개수를 쓴다 — 그래야 설치 UI(HUD "설치 필요 자원")와
        // 실제 차감액이 같은 데이터에서 나온다. 데이터에 "Belt"가 없으면(테스트 DB 등) 위
        // concreteCostPerTile(Inspector 값)로 폴백한다. 철거 환불(SimulationWorld.RefundBeltCost)이
        // 콘크리트 한 종류만 다루는 구조라, Belt 건설비에 콘크리트 말고 다른 자원을 적어도 여기선
        // 무시한다(경고 로그).
        private int ConcreteCostPerTile
        {
            get
            {
                if (driver == null || driver.World == null) return concreteCostPerTile;
                var db = driver.World.Database;
                if (!db.TryGetMachineId("Belt", out int machineId)
                    || !db.TryGetResourceId("Concrete", out int concreteId))
                {
                    return concreteCostPerTile;
                }

                int concrete = 0;
                bool hasOther = false;
                var cost = db.Machines[machineId].BuildCost;
                for (int i = 0; i < cost.Length; i++)
                {
                    if (cost[i].ResourceId == concreteId) concrete += cost[i].Amount;
                    else hasOther = true;
                }
                if (hasOther && !warnedNonConcreteBeltCost)
                {
                    warnedNonConcreteBeltCost = true;
                    Debug.LogWarning("[BeltDragTool] Belt 건설비에 콘크리트 외 자원이 있지만 벨트는 콘크리트만 지원합니다(환불 구조). 콘크리트 개수만 적용합니다.");
                }
                return concrete;
            }
        }
        // 크로스 벨트(교차로)가 그 칸의 원래 벨트보다 얼마나 낮게 그려질지(BeltDragTool.Crossing.cs
        // 참고) — 순수 시각용, 배선/시뮬레이션엔 영향 없다.
        [SerializeField] private float crossingLoweredOffset = 0.03f;
        // 교차 지점(IsCrossable 위를 실제로 지나가는 순간)에 얹는 전용 표시 모델 — 밑에 깔리는
        // 벨트 스트립은 그대로 두고(BeltItemRenderer 앵커/아이템 이동에 계속 필요) 그 위에
        // 장식으로 덧놓는다. 없으면 그냥 안 놓는다(폴백 없음 — 순수 장식이라 없어도 기능엔 문제 없음).
        [SerializeField] private GameObject crosserVisualPrefab;

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly List<Vector2Int> path = new List<Vector2Int>();
        private readonly List<GameObject> previewStrips = new List<GameObject>();
        // 옆칸 자동연결 대상인 기존 벨트의 진짜 오브젝트 — 미리보기 중엔 잠깐 숨기고 대신
        // "이대로 이어지면 이렇게 휠 것"이라는 임시 조각(previewStrips)을 덧씌워 보여준다.
        // 진짜 데이터/모양은 전혀 안 건드리므로 취소해도 그대로 남는다. segmentId로 키를
        // 둬서 RerenderSegmentStrip이 "숨겨진 옛 오브젝트"를 확실히 찾아 지울 수 있게 한다 —
        // GameObject.Find는 비활성 오브젝트를 못 찾아서, 이름 검색만 믿으면 숨긴 옛 오브젝트가
        // 안 지워진 채 ClearPreview에서 도로 살아나 새로 그린 것과 겹쳐 보이는 버그가 났었다.
        private readonly Dictionary<int, GameObject> hiddenNeighborVisuals = new Dictionary<int, GameObject>();
        private bool dragging;

        // 벨트 개수가 많아지면 놓을 때마다/재배선될 때마다(RerenderSegmentStrip) GameObject를
        // 통째로 Destroy하고 새로 Instantiate하는 비용이 누적된다(사용자 지적) — segmentId로
        // 현재 살아있는 벨트 루트를 찾아 그 자리에서 다시 그리고(Geometry 자식만 교체),
        // 철거된 루트는 Destroy 대신 비활성화해서 풀에 보관했다가 다음 벨트 배치 때 재사용한다.
        // GameObject.Find는 비활성 오브젝트를 못 찾으므로(위 hiddenNeighborVisuals와 같은 이유)
        // 풀 재사용엔 이 딕셔너리가 반드시 필요하다. FactoryViewportCuller도 뷰포트 컬링 대상을
        // 찾을 때 이 딕셔너리를 그대로 쓴다(BeltDragTool.Visuals.cs의 TryGetBeltVisual 참고).
        private readonly Dictionary<int, GameObject> beltVisualRoots = new Dictionary<int, GameObject>();
        private readonly Stack<GameObject> pooledBeltVisuals = new Stack<GameObject>();

        // 성능 비교 전용 스위치(TestFactoryBuilder의 풀링 벤치마크 메뉴만 끈다). false면 반납 시
        // 풀에 넣지 않고 바로 Destroy하고, 새 벨트도 항상 새로 만든다 — 풀링 도입 전 동작과 같다.
        public static bool BeltVisualPoolingEnabled = true;

        // 에디터 SerializedObject 없이(런타임/테스트에서) 직접 배선할 때 쓴다.
        public void Initialize(Camera targetCamera, SimulationDriver driver)
        {
            this.targetCamera = targetCamera;
            this.driver = driver;
        }

        public void OnPressBegin(Vector2 screenPosition)
        {
            path.Clear();
            ClearPreview();
            dragging = true;

            if (TryScreenToCell(screenPosition, out var cell))
            {
                // 기계 칸에서는 드래그 자체를 시작하지 않는다 — 기계 옆 빈 칸에서 시작/끝내면
                // 옆칸 자동연결(TryFindAdjacentOccupant)이 알아서 붙여준다.
                if (IsMachineCell(cell))
                {
                    dragging = false;
                    return;
                }
                BeltPathBuilder.Extend(path, cell);
                RebuildPreview();
            }
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (!dragging) return;
            if (!TryScreenToCell(screenPosition, out var cell)) return;

            int before = path.Count;
            BeltPathBuilder.Extend(path, cell);
            TrimAtMachine();
            if (path.Count != before) RebuildPreview();
        }

        // 벨트는 기계 칸을 밟거나 통과할 수 없다(코어/분류기 등 Belt 아닌 점유 전부). 기계에서
        // 시작하거나 기계가 경로 중간에 끼면 미리보기/되그리기가 엉뚱한 이웃 벨트를 건드리는
        // 문제가 계속 났다 — 아예 경로가 기계 직전 칸에서 끊기게 해서 그 경우 자체를 없앤다.
        private bool IsMachineCell(Vector2Int cell)
        {
            if (driver == null || driver.World == null) return false;
            return driver.World.Grid.TryGetOccupant(cell, out var occupant) && occupant.Type != CellOccupantType.Belt;
        }

        private void TrimAtMachine()
        {
            for (int i = 0; i < path.Count; i++)
            {
                if (!IsMachineCell(path[i])) continue;
                path.RemoveRange(i, path.Count - i);
                return;
            }
        }

        public void OnReleased(Vector2 screenPosition)
        {
            if (!dragging) return;
            dragging = false;
            if (PathPermission == null || PathPermission(path)) Commit();
            path.Clear();
            ClearPreview();
        }

        public void OnCancelled()
        {
            dragging = false;
            path.Clear();
            ClearPreview();
        }

        private bool TryScreenToCell(Vector2 screenPosition, out Vector2Int cell)
        {
            cell = default;
            if (targetCamera == null) return false;

            return GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), groundPlane, out cell);
        }
    }
}
