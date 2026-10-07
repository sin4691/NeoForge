using System.Collections.Generic;
using Factory.Building;
using Factory.Data;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Rendering
{
    // 시뮬레이션 데이터(BeltSegment.Items)를 읽어 풀링된 오브젝트로 화면에 그린다.
    // 시뮬레이션 배열 자체는 건드리지 않으므로 틱 루프의 GC Alloc 0 유지에 영향을 주지 않는다.
    //
    // 자원별로 실제 모델(Addressables, ResourceRuntime.PrefabName)이 있으면 그걸 얹고, 없는
    // 자원(아직 매핑 안 된 것)은 예전처럼 구+색 폴백을 쓴다. 풀 슬롯은 재사용되며 프레임마다
    // 다른 자원을 표시할 수 있으니, 슬롯에 물린 자원이 바뀔 때만 모델을 다시 얹는다(매 프레임
    // Addressables 로드/파괴를 반복하지 않도록).
    public class BeltItemRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private int segmentId;
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField] private GameObject itemVisualPrefab; // 자원별 모델이 없을 때의 폴백(구) 모양
        // 코너 세그먼트일 때만 값 있음 — 꺾이는 지점. 이게 없으면(직선) start->end 직선 이동.
        [SerializeField] private Transform bendPoint;
        // 크로스 벨트의 두 축 세그먼트 전용 — 분류기/합류기처럼 "기계 안으로 들어갔다 반대편에서
        // 나오는" 느낌을 주려고, 이 구간을 지나는 동안은 아이템을 아예 안 그린다(사용자 요청:
        // 진짜 벨트라 원래는 위에서 굴러가는 게 보이는데, 그러면 안 됨). 시뮬레이션(Position
        // 이동 자체)은 전혀 안 건드리고 순수 렌더링만 끈다.
        [SerializeField] private bool hideItems;

        private sealed class Slot
        {
            public Transform Root;
            public int ResourceId = -1;
            public Renderer FallbackRenderer;
            public bool HasModel; // true면 실제 자원 모델을 얹은 상태 — 폴백은 로드될 때까지의 임시 표시일 뿐, 매 프레임 다시 칠할 필요 없음
        }

        private readonly List<Slot> pool = new List<Slot>();

        // 성능 비교 전용 스위치(TestFactoryBuilder의 아이템 슬롯 풀링 벤치마크만 끈다).
        public static bool ItemSlotPoolingEnabled = true;
        private BeltSegment segment;

        // 런타임에 벨트를 놓는 건설 도구가 에디터 SerializedObject 없이 직접 배선할 때 쓴다.
        public void Initialize(SimulationDriver driver, int segmentId, Transform startPoint, Transform endPoint, GameObject itemVisualPrefab = null, Transform bendPoint = null, bool hideItems = false)
        {
            this.driver = driver;
            this.segmentId = segmentId;
            this.startPoint = startPoint;
            this.endPoint = endPoint;
            if (itemVisualPrefab != null) this.itemVisualPrefab = itemVisualPrefab;
            this.bendPoint = bendPoint;
            this.hideItems = hideItems;
            segment = null;
        }

        private void LateUpdate()
        {
            if (hideItems)
            {
                for (int i = 0; i < pool.Count; i++) pool[i].Root.gameObject.SetActive(false);
                return;
            }

            if (segment == null)
            {
                segment = FindSegment();
                if (segment == null) return;
            }

            var database = driver.World.Database;
            EnsurePoolSize(segment.Items.Count);

            for (int i = 0; i < segment.Items.Count; i++)
            {
                var item = segment.Items[i];
                float t = segment.Length <= 0f ? 0f : item.Position / segment.Length;
                var slot = pool[i];

                Vector3 position;
                Vector3 direction;
                if (bendPoint != null)
                {
                    // 코너: start/bend/end를 2차 베지어 곡선의 세 점으로 써서 부드럽게 돈다.
                    // (직선 두 개를 이어 붙이면 꺾이는 지점이 뾰족한 직각이 돼서 안 좋음 —
                    // 베지어는 양 끝(t=0, t=1)에서 각각 직선 구간과 접선이 자연스럽게 이어진다.)
                    Vector3 p0 = startPoint.position, p1 = bendPoint.position, p2 = endPoint.position;
                    float u = 1f - t;
                    position = u * u * p0 + 2f * u * t * p1 + t * t * p2;
                    direction = 2f * u * (p1 - p0) + 2f * t * (p2 - p1); // 곡선의 접선(진행 방향)
                }
                else
                {
                    position = Vector3.Lerp(startPoint.position, endPoint.position, t);
                    direction = endPoint.position - startPoint.position;
                }

                slot.Root.position = position;
                // 진행 방향으로 회전시킨다 — 안 그러면(특히 실제 자원 모델이 얹힌 뒤로는) 방향
                // 없이 원래 배리언트 자세 그대로 고정돼서 벨트를 따라가는 느낌이 안 난다.
                if (direction.sqrMagnitude > 0.0001f) slot.Root.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                slot.Root.gameObject.SetActive(true);

                if (slot.ResourceId != item.ResourceId) MountResource(slot, item.ResourceId, database);
                // 폴백(구)을 쓰는 슬롯만 매 프레임 다시 칠한다 — 실제 모델은 자기 고유 재질을
                // 그대로 쓰고 색으로 구분할 필요가 없다.
                if (!slot.HasModel) BuildVisuals.Colorize(slot.FallbackRenderer, database.Resources[item.ResourceId].Color);
            }

            if (!ItemSlotPoolingEnabled)
            {
                // 비교용 "풀링 없음" — 남는 슬롯을 숨겨 두지 않고 바로 버린다. 아이템이 이 칸에
                // 새로 들어올 때마다 슬롯(+자원 모델)을 다시 만들게 되는, 풀링 도입 전과 같은 동작.
                for (int i = pool.Count - 1; i >= segment.Items.Count; i--)
                {
                    Destroy(pool[i].Root.gameObject);
                    pool.RemoveAt(i);
                }
                return;
            }

            for (int i = segment.Items.Count; i < pool.Count; i++)
            {
                pool[i].Root.gameObject.SetActive(false);
            }
        }

        private BeltSegment FindSegment()
        {
            if (driver == null || driver.World == null) return null;
            var segments = driver.World.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveSegment 참고).
                if (segments[i].Id == segmentId) return segments[i];
            }
            return null;
        }

        private void EnsurePoolSize(int count)
        {
            while (pool.Count < count)
            {
                var root = new GameObject("BeltItem").transform;
                root.SetParent(transform, false);
                pool.Add(new Slot { Root = root });
            }
        }

        // 슬롯에 표시할 자원이 바뀌었을 때만 호출된다. 기존 표시물을 지우고, 그 자원에 매핑된
        // Addressables 모델이 있으면 얹고(로드 전/실패 시엔 폴백 유지), 없으면 폴백만 남긴다.
        private void MountResource(Slot slot, int resourceId, GameDatabase database)
        {
            for (int i = slot.Root.childCount - 1; i >= 0; i--) Destroy(slot.Root.GetChild(i).gameObject);
            slot.ResourceId = resourceId;
            slot.HasModel = false;

            GameObject fallback = itemVisualPrefab != null ? Instantiate(itemVisualPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fallback.transform.SetParent(slot.Root, false);
            if (itemVisualPrefab == null)
            {
                fallback.transform.localScale = Vector3.one * 0.25f;
                var collider = fallback.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
            slot.FallbackRenderer = fallback.GetComponent<Renderer>();

            string prefabName = database.Resources[resourceId].PrefabName;
            if (!string.IsNullOrEmpty(prefabName))
            {
                slot.HasModel = true;
                // 실제 모델이 있으면 폴백(구)은 아예 안 보여준다 — 안 그러면 Addressables가
                // 비동기로 늦게 얹히는 한두 프레임 동안 엉뚱한 구 모양이 세상에 막 나온
                // 자원마다 잠깐씩 번쩍여서 눈에 띈다(사용자 보고 — 기계 고스트 박스 때와
                // 같은 원인). 로드 실패 시엔 어차피 이 프레임 이후로도 안 보이는 게 낫다
                // (틀린 색 구가 계속 떠 있는 것보단 조용히 안 보이는 편).
                fallback.SetActive(false);
                var mount = new GameObject("Model");
                mount.transform.SetParent(slot.Root, false);
                // alignToGround: false — 슬롯 루트가 이미 벨트 위 원하는 높이를 매 프레임 그대로
                // 따라가므로, 기계처럼 "지면(y=0)까지 내리는" 보정을 하면 오히려 파묻힌다.
                mount.AddComponent<AddressableModelMount>().Mount(prefabName, fallback, alignToGround: false);
            }
        }
    }
}
