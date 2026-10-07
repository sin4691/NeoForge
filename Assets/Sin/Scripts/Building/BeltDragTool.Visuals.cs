using System.Collections.Generic;
using Factory.Rendering;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 순수 지오메트리 계산과 실제 스트립/코너 오브젝트 생성 — 배선 데이터(BeltSegment 등)는
    // 안 건드리고, 주어진 좌표로 무엇을 만들지만 담당한다.
    public partial class BeltDragTool
    {
        // 세그먼트 하나가 정확히 자기 칸 안(경계~반대쪽 경계)에 들어차도록 진입/이탈 지점을
        // 계산한다. 진입/이탈 방향이 다르면(코너) 꺾이는 지점(bend)을 반환해서 두 조각으로 나눠 그린다.
        private static void ComputeCellSpan(List<Vector2Int> path, int k, out Vector3 entry, out Vector3 exit, out Vector3? bend)
        {
            Vector3 center = GridUtility.CellToWorldCenter(path[k], 0f); // 벨트는 바닥에 붙는다(납작 Quad)
            Vector3? inDir = k > 0 ? DirWorld(path[k] - path[k - 1]) : (Vector3?)null;
            Vector3? outDir = k < path.Count - 1 ? DirWorld(path[k + 1] - path[k]) : (Vector3?)null;

            if (inDir.HasValue && outDir.HasValue && inDir.Value != outDir.Value)
            {
                entry = center - inDir.Value * 0.5f;
                exit = center + outDir.Value * 0.5f;
                bend = center;
                return;
            }

            Vector3 dir = inDir ?? outDir ?? Vector3.forward;
            entry = center - dir * 0.5f;
            exit = center + dir * 0.5f;
            bend = null;
        }

        private static Vector3 DirWorld(Vector2Int step) => new Vector3(step.x, 0f, step.y);

        // isCrossing이면 크로스 벨트(WorldGrid 2번째 벨트 레이어)로 등록된 세그먼트라는 뜻 —
        // 같은 칸의 원래 벨트와 겹쳐 보이지 않도록 살짝 낮춰서 "밑으로 지나간다"는 느낌을 준다.
        // showCrosser는 별개다: "여기가 교차 가능한 지점"이라는 표시 모델을 이 세그먼트 위에
        // 얹을지 — IsCrossable로 놓은 크로스 타일의 주축 쪽에서만 켠다.
        // hideStrip은 크로스 타일의 수직축 전용 — 그쪽은 아무 메쉬도 안 그린다(앵커만 만들어서
        // BeltItemRenderer가 계속 쓸 수 있게). 안 그러면 주축의 크로스 아이콘 밑에 평범한
        // 회색 스트립이 따로 깔려서, 크로스 하나 놓았는데 "벨트가 하나 더 설치된 것처럼" 보인다
        // (사용자 보고) — 분류기/합류기처럼 한 덩어리로 보이려면 아이콘 하나가 유일한 표현이어야 한다.
        // 셋 다 실제 배선/시뮬레이션엔 영향 없는 순수 시각 처리.
        private void SpawnCommittedVisual(Vector3 from, Vector3 to, Vector3? bend, int segmentId, bool isCrossing = false, bool showCrosser = false, bool hideItems = false, bool hideStrip = false)
        {
            var root = GetOrCreateBeltRoot(segmentId);

            float surfaceY = beltSurfaceY - (isCrossing ? crossingLoweredOffset : 0f);

            // 아이템이 벨트 면(surfaceY) 위에 얹혀 굴러가는 것처럼 반지름만큼 띄운다.
            Vector3 itemHeightOffset = Vector3.up * (surfaceY + 0.01f + itemVisualRadius);

            // Start/End/Bend 앵커 + 스트립/코너/크로서 메쉬는 전부 이 전용 자식 하나
            // ("Geometry") 밑에 모아둔다 — 재배선(RerenderSegmentStrip)될 때 이 자식 하나만
            // 통째로 갈아끼우면 되고, BeltItemRenderer가 root 바로 밑에 만들어둔 아이템 풀
            // (BeltItem 슬롯들)은 이 자식과 형제 관계라 전혀 안 건드린다 — 재배선마다 아이템
            // 풀까지 같이 날리고 새로 만들면 풀링 이득이 없어진다.
            var oldGeometry = root.transform.Find("Geometry");
            if (oldGeometry != null) Destroy(oldGeometry.gameObject);
            var geometry = new GameObject("Geometry").transform;
            geometry.SetParent(root.transform, false);

            var startAnchor = new GameObject("Start").transform;
            startAnchor.SetParent(geometry);
            startAnchor.position = from + itemHeightOffset;

            var endAnchor = new GameObject("End").transform;
            endAnchor.SetParent(geometry);
            endAnchor.position = to + itemHeightOffset;

            // 코너면 꺾이는 지점도 앵커로 만들어 BeltItemRenderer에 넘긴다 — 안 그러면 아이템이
            // start->end를 그냥 직선(대각선)으로 가로질러서, 코너를 안 따라가고 벽을 뚫는 것처럼
            // 보인다. 진입 절반/이탈 절반 두 구간으로 나눠 가게 하려면 이 중간점이 필요하다.
            // (크로스 벨트는 정의상 항상 직선이라 bend가 있을 일이 없지만, 세이브 복원 등
            // 다른 경로로 넘어올 가능성을 막지 않기 위해 분기는 그대로 둔다.)
            Transform bendAnchor = null;
            if (bend.HasValue)
            {
                bendAnchor = new GameObject("Bend").transform;
                bendAnchor.SetParent(geometry);
                bendAnchor.position = bend.Value + itemHeightOffset;
            }

            // 앵커(Start/End/Bend)는 어느 쪽이든 항상 만든다 — BeltItemRenderer가 아이템 이동에
            // 계속 쓴다. 실제로 보이는 메쉬만 세 갈래로 갈린다: 크로스 아이콘(showCrosser),
            // 아무것도 안 그림(hideStrip), 평범한 스트립/코너(그 외 일반 벨트) 순으로 우선한다.
            if (hideStrip)
            {
                // 크로스 타일 수직축 — 일부러 아무것도 안 그린다.
            }
            else if (showCrosser && crosserVisualPrefab != null)
            {
                // 이 모델의 그림에는 입력/출력 화살표가 들어 있어서 방향(Facing)대로 돌려야 한다 —
                // 안 돌리면 고스트에서 고른 방향과 상관없이 확정 순간 항상 기본 모양(왼→오, 아래→위)
                // 으로 돌아간 것처럼 보인다(사용자 보고). 분류기/합류기는 MachineGhostTool이 확정
                // 모델을 FacingToRotation(facing)으로 돌려서 정상인데, 크로스 벨트만 벨트 그리기
                // 경로(이 함수)를 타면서 그 회전이 빠져 있었다. 고스트는 같은 프리팹을
                // FacingToRotation(facing) 아래 자식으로 두므로, 확정 모델도 정확히 같은 식
                // (LookRotation(facing) * 프리팹 자세)을 써야 안 어긋난다. 주축 방향은 이
                // 세그먼트의 진입→이탈 방향(from→to) 그대로가 Facing이다.
                Vector3 center = (from + to) * 0.5f;
                Vector3 axisDir = to - from;
                axisDir.y = 0f;
                var crosser = Instantiate(crosserVisualPrefab, geometry);
                crosser.transform.position = new Vector3(center.x, beltSurfaceY, center.z);
                if (axisDir.sqrMagnitude > 0.0001f)
                    crosser.transform.rotation = Quaternion.LookRotation(axisDir.normalized, Vector3.up) * crosserVisualPrefab.transform.rotation;
                crosser.name = "CrosserVisual";
            }
            else if (bend.HasValue)
            {
                if (cornerPrefab != null)
                {
                    SpawnBeltCorner(bend.Value, bend.Value - from, to - bend.Value, committedColor, geometry, keepMaterial: true, surfaceY: surfaceY);
                }
                else
                {
                    // 폴백: 진입 절반 + 이탈 절반 두 조각으로 나눠 그려 칸 전체를 덮는다.
                    BuildVisuals.CreateStrip(from, bend.Value, committedThickness, committedColor, geometry, prefab: stripPrefab, keepPrefabMaterial: true, flatSurfaceY: surfaceY);
                    BuildVisuals.CreateStrip(bend.Value, to, committedThickness, committedColor, geometry, prefab: stripPrefab, keepPrefabMaterial: true, flatSurfaceY: surfaceY);
                }
            }
            else
            {
                BuildVisuals.CreateStrip(from, to, committedThickness, committedColor, geometry, prefab: stripPrefab, keepPrefabMaterial: true, flatSurfaceY: surfaceY);
            }

            var itemRenderer = root.GetComponent<BeltItemRenderer>();
            if (itemRenderer == null) itemRenderer = root.AddComponent<BeltItemRenderer>();
            itemRenderer.Initialize(driver, segmentId, startAnchor, endAnchor, itemVisualPrefab, bendAnchor, hideItems);
        }

        // segmentId로 이미 등록된 벨트 루트가 있으면 그대로 재사용한다(재배선/재도색 케이스 —
        // SpawnCommittedVisual이 Geometry 자식만 새로 갈아끼운다). 없으면 풀에서 하나 꺼내
        // 재활용하고(비활성 상태로 보관돼있던 것), 풀도 비어있으면 그제서야 진짜 new GameObject를
        // 만든다. 벨트를 아무리 많이 짓고 부수고 해도, 동시에 존재하는 최대 개수만큼만 실제
        // GameObject가 생긴다.
        //
        // 어느 경로로 얻었든 반드시 SetActive(true)를 거친다 — "이미 등록된 루트" 쪽도 예외가
        // 아니다: 드래그 미리보기 중엔 실제 벨트가 잠깐 SetActive(false)로 숨겨졌다가
        // (SpawnHiddenNeighborPreview) 나중에 다시 그려지는데, 그때도 여전히 beltVisualRoots엔
        // 그 비활성 오브젝트가 "현재 루트"로 등록돼 있다. 여기서 활성화를 빼먹으면 재배선된
        // 벨트가 데이터상으로는 멀쩡히 연결됐는데 화면엔 아예 안 보이는 버그가 난다(사용자 보고
        // — 벨트 지웠다 다시 이으면 양옆 벨트가 안 보임).
        private GameObject GetOrCreateBeltRoot(int segmentId)
        {
            if (beltVisualRoots.TryGetValue(segmentId, out var existing) && existing != null)
            {
                existing.SetActive(true);
                return existing;
            }

            GameObject root;
            if (BeltVisualPoolingEnabled && pooledBeltVisuals.Count > 0)
            {
                root = pooledBeltVisuals.Pop();
                root.SetActive(true);
            }
            else
            {
                root = new GameObject();
            }

            root.name = $"Belt_{segmentId}";
            beltVisualRoots[segmentId] = root;
            return root;
        }

        // 벨트가 철거될 때(DemolishTool) Destroy 대신 이걸 부른다 — 통째로 없애는 대신
        // 비활성화해서 풀에 넣어두고, 다음에 벨트가 새로 놓일 때 GetOrCreateBeltRoot가 이
        // 오브젝트를 다시 꺼내 쓴다. 이름을 바꿔서 비활성 상태에서도 옛 segmentId로
        // GameObject.Find가 잘못 찾는 일이 없게 한다(비활성은 원래 Find로 안 잡히지만, 혹시
        // 다시 활성화되는 타이밍과 겹쳐도 안전하도록).
        public void ReturnBeltVisual(int segmentId)
        {
            if (!beltVisualRoots.TryGetValue(segmentId, out var root) || root == null)
            {
                beltVisualRoots.Remove(segmentId);
                // 이 딕셔너리에 없다는 건 이 벨트가 GetOrCreateBeltRoot를 거치지 않고 만들어진
                // 경우다(예: BeltDragTool이 아직 없을 때 세이브 로드가 쓰는 폴백 경로,
                // FactorySaveBridge.SpawnBeltVisual 참고) — 그래도 화면에 남아있으면 지워야
                // 하니 예전 방식(이름 검색)으로 한 번 더 찾아서 지운다.
                var orphan = GameObject.Find($"Belt_{segmentId}");
                if (orphan != null) Destroy(orphan);
                return;
            }

            beltVisualRoots.Remove(segmentId);
            // 여기서 Destroy는 프레임 끝에 처리되는데, 세이브 로드는 ClearCurrentFactory(반납)와
            // RestoreBelts(복원)를 같은 프레임에 연달아 돌린다 — 그러면 복원 쪽 SpawnCommittedVisual의
            // GetComponent<BeltItemRenderer>()가 "곧 파괴될 옛 컴포넌트"를 그대로 집어서 재사용하고,
            // 프레임 끝에 그 컴포넌트가 사라지면서 복원된 벨트에 아이템 렌더러가 없는 상태가 된다
            // (그래서 불러오기 한 번으론 벨트 위 자원이 안 보이고, 한 번 더 불러와야 보였다 — 사용자 보고).
            // 컴포넌트는 부모에서 뗄 수도 없으니 DestroyImmediate로 즉시 없앤다.
            var itemRenderer = root.GetComponent<BeltItemRenderer>();
            if (itemRenderer != null) DestroyImmediate(itemRenderer);
            // Geometry뿐 아니라 BeltItemRenderer가 만들어둔 아이템 풀 슬롯(BeltItem, root 바로
            // 밑의 형제)도 여기서 같이 치운다 — 컴포넌트만 Destroy하면 그 풀이 만든 자식
            // 오브젝트들은 안 지워지고 고아로 남아 계속 쌓인다(재사용 때마다 새 BeltItemRenderer가
            // 빈 풀로 다시 시작하므로 옛 자식들을 다시 쓸 일도 없다). 자식은 먼저 부모에서 떼고
            // 끈 다음 Destroy한다 — 같은 프레임 안에 재사용되면 root.transform.Find("Geometry")가
            // 파괴 예정인 옛 Geometry를 다시 집을 수 있어서, 눈에 안 띄게 root에서 완전히 분리한다.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                child.gameObject.SetActive(false);
                child.SetParent(null);
                Destroy(child.gameObject);
            }
            root.name = "Belt_Pooled";
            root.SetActive(false);
            if (!BeltVisualPoolingEnabled)
            {
                Destroy(root); // 벤치마크용 "풀링 없음" 모드 — 풀링 도입 전처럼 통째로 버린다.
                return;
            }
            pooledBeltVisuals.Push(root);
        }

        // FactoryViewportCuller가 뷰포트 컬링 대상을 찾을 때 쓴다 — 풀에 반납되어(비활성)
        // 있는 동안엔 beltVisualRoots에서 빠지므로(ReturnBeltVisual 참고) 컬링이 그 사이에
        // 실수로 다시 켜는 일은 없다.
        public bool TryGetBeltVisual(int segmentId, out GameObject go)
        {
            if (beltVisualRoots.TryGetValue(segmentId, out go) && go != null) return true;
            go = null;
            return false;
        }
        // 여기부터
        // 세이브 로드도 최초 배치와 완전히 같은 프리팹/높이/아이템 렌더러 경로를 사용한다.
        // 주의: 세이브 데이터가 아직 크로스 벨트(2번 레이어) 여부를 안 들고 있어서, 재로드
        // 직후엔 크로스 벨트도 일단 보통 높이로 복원된다(SaveLoad 쪽 별도 작업 필요 — 기능은
        // 그대로 살아있고 겹쳐 보이는 것만 다시 어긋난다).
        public void SpawnRestoredVisual(Vector3 from, Vector3 to, Vector3? bend, int segmentId)
        {
            SpawnCommittedVisual(from, to, bend, segmentId);
        }
        //여기까지
        // 코너 칸 중심에 코너 프리팹을 놓고 Y축으로만 돌린다(프리팹의 눕힌 자세는 유지).
        //
        // 우회전 프리팹 기준: 진입 = 아래(-Z) 변에서 위로(+Z) 들어와, 오른쪽(+X) 변으로 나간다.
        //  - 실제 진입 흐름방향(fin)에 프리팹 로컬 +Z 를 맞추면(yaw) 네 방향 우회전이 다 커버됨.
        //  - 좌회전은 거울상이라 회전만으론 화살표가 뒤집힘 → 좌회전 전용 프리팹(cornerLeftPrefab)을 쓴다.
        //    없으면 대칭 타일로 보고 우회전 프리팹을 두 변 사잇방향에 맞춰 돌린다(화살표 없는 코너용 폴백).
        private GameObject SpawnBeltCorner(Vector3 cellCenter, Vector3 dIn, Vector3 dOut, Color tint, Transform parent, bool keepMaterial, float surfaceY = float.NaN)
        {
            if (float.IsNaN(surfaceY)) surfaceY = beltSurfaceY;
            Vector3 fin = new Vector3(dIn.x, 0f, dIn.z).normalized;
            Vector3 fout = new Vector3(dOut.x, 0f, dOut.z).normalized;
            bool leftTurn = Vector3.Cross(fin, fout).y < 0f;

            GameObject prefab = (leftTurn && cornerLeftPrefab != null) ? cornerLeftPrefab : cornerPrefab;
            var go = Instantiate(prefab, parent);
            go.transform.position = new Vector3(cellCenter.x, surfaceY, cellCenter.z);

            // 이음새를 덮게 코너를 살짝 키운다(Quad 평면 축 = 로컬 X/Y, Z 는 노멀이라 유지).
            Vector3 baseScale = go.transform.localScale;
            go.transform.localScale = new Vector3(baseScale.x * cornerScale, baseScale.y * cornerScale, baseScale.z);

            float yaw;
            if (leftTurn && cornerLeftPrefab == null)
            {
                // 폴백: 대칭 타일 가정 — 두 변 사잇방향(우회전 프리팹은 135°)에 맞춤.
                Vector3 bisector = fout - fin;
                yaw = Mathf.Atan2(bisector.x, bisector.z) * Mathf.Rad2Deg - 135f;
            }
            else
            {
                // 진입 흐름방향(fin)에 로컬 +Z 를 맞춘다. 우/좌 전용 프리팹 둘 다 이 규칙.
                yaw = Mathf.Atan2(fin.x, fin.z) * Mathf.Rad2Deg;
            }
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;

            if (!keepMaterial) BuildVisuals.TintPreserveShape(go, tint);
            return go;
        }
    }
}
