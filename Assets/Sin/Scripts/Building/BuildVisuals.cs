using System.Collections.Generic;
using UnityEngine;

namespace Factory.Building
{
    // 에셋 없는 프로토타입용 런타임 시각 헬퍼. 건설 도구들이 프리뷰/확정 지오메트리를
    // 만들 때 공용으로 쓴다. 색상별로 머티리얼을 캐싱해서 재사용한다 — 안 그러면 고스트를
    // 매 프레임 다시 칠할 때마다(Colorize) 새 Material을 계속 만들어내게 된다.
    public static class BuildVisuals
    {
        private static readonly Dictionary<Color, Material> materialCache = new Dictionary<Color, Material>();

        // prefab 이 주어지면 항상 그 실제 모양(눕힌 Quad)으로 그린다 — 미리보기도 "실제로 놓일 모양"이어야
        // 유효/무효 색이 자연스럽게 보이기 때문(엉뚱한 큐브 미리보기 X).
        // keepPrefabMaterial=true 면 prefab 의 머티리얼(컨베이어 텍스처)을 그대로 두고 길이만큼 타일링만
        // 한다(확정된 벨트). false 면 그 위에 유효/무효 색을 반투명으로 덮어 칠한다(미리보기).
        public static GameObject CreateStrip(Vector3 from, Vector3 to, float thickness, Color color, Transform parent,
            bool withCollider = false, GameObject prefab = null, bool keepPrefabMaterial = false, float flatSurfaceY = 0.06f)
        {
            // prefab 이 있으면(미리보기든 확정이든) 실제 모양(바닥에 눕힌 Quad)으로 그린다 — 미리보기가
            // 엉뚱한 큐브가 아니라 "실제로 놓일 모양 + 유효/무효 색 반투명"으로 보이게 하기 위함.
            // prefab 자체가 없을 때(스트립 프리팹 미할당)만 옛 큐브 폴백을 쓴다.
            bool flat = prefab != null;

            GameObject go;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (!withCollider) Object.Destroy(go.GetComponent<Collider>());
            }

            Vector3 mid = (from + to) * 0.5f;
            float length = Mathf.Max(Vector3.Distance(from, to), 0.001f);
            go.transform.SetParent(parent, true);

            if (flat)
            {
                Vector3 travel = to - from;
                float yaw = Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
                go.transform.position = new Vector3(mid.x, flatSurfaceY, mid.z); // 코너 Quad 와 같은 높이
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation; // 프리팹 눕힌 자세 유지 + 진행방향
                // 폭(thickness)은 코너 텍스처 안의 "벨트 띠" 폭과 같아야 이음새에서 안 잘린다.
                // 둘 다 칸 중심선 기준 centered 이므로 이 폭만 맞추면 정렬됨.
                go.transform.localScale = new Vector3(thickness, length, 1f);

                if (keepPrefabMaterial)
                {
                    var renderer = go.GetComponentInChildren<Renderer>();
                    if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null)
                    {
                        var mat = renderer.material; // 스트립마다 개별 인스턴스(타일링이 서로 안 섞이게)
                        mat.mainTextureScale = new Vector2(mat.mainTextureScale.x, Mathf.Max(1f, Mathf.Round(length)));
                    }
                }
                else
                {
                    // 미리보기: 실제 텍스처(모양)는 유지하고 색조만 유효/무효 색으로 씌운다.
                    TintPreserveShape(go, color);
                }
                return go;
            }

            Quaternion rotation = Quaternion.LookRotation(to - from);
            go.transform.position = mid + Vector3.up * (thickness * 0.2f); // 큐브 밑면이 바닥에 닿게
            go.transform.rotation = rotation;
            go.transform.localScale = new Vector3(thickness, thickness * 0.4f, length);
            Colorize(go, color);
            AttachDirectionArrow(go.transform.position + Vector3.up * (thickness * 0.2f + 0.03f), rotation, go.transform);
            return go;
        }

        private static Mesh beltArrowMesh;

        private static void AttachDirectionArrow(Vector3 position, Quaternion rotation, Transform stripTransform)
        {
            var arrowGO = new GameObject("DirectionArrow", typeof(MeshFilter), typeof(MeshRenderer));
            arrowGO.transform.position = position;
            arrowGO.transform.rotation = rotation;
            arrowGO.transform.localScale = Vector3.one * 0.6f;
            arrowGO.transform.SetParent(stripTransform, true);

            if (beltArrowMesh == null)
            {
                beltArrowMesh = new Mesh { name = "BeltDirectionArrow" };
                var vertices = new[]
                {
                    new Vector3(0f, 0f, 0.34f),
                    new Vector3(-0.16f, 0f, 0.06f),
                    new Vector3(0.16f, 0f, 0.06f),
                };
                var triangles = new[] { 0, 1, 2, 0, 2, 1 };
                var normals = new[] { Vector3.up, Vector3.up, Vector3.up };
                beltArrowMesh.vertices = vertices;
                beltArrowMesh.triangles = triangles;
                beltArrowMesh.normals = normals;
                beltArrowMesh.RecalculateBounds();
            }

            arrowGO.GetComponent<MeshFilter>().sharedMesh = beltArrowMesh;
            Colorize(arrowGO, Color.black);
        }

        public static GameObject CreateBox(Vector3 position, Vector3 scale, Color color, Transform parent, bool withCollider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!withCollider) Object.Destroy(go.GetComponent<Collider>());

            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = scale;

            Colorize(go, color);
            return go;
        }

        public static void Colorize(GameObject go, Color color) => Colorize(go.GetComponent<Renderer>(), color);

        // Colorize와 달리 머티리얼을 통째로 새 셰이더로 갈지 않는다 — 원본 셰이더/텍스처는 그대로
        // 두고 _BaseColor(또는 _Color)만 틴트로 바꾼다. 실제 배치된 오브젝트와 "같은 셰이더"를
        // 그대로 쓰는 거라 알파(모양/투명도)는 항상 원본과 똑같이 맞는다 — 대신 원본 색이 진하면
        // 틴트와 살짝 섞여 보일 수 있다(커스텀 실루엣 셰이더로 완전 단색화를 시도했으나 평면 Quad
        // 에서 알파를 못 읽는 문제가 있어 폐기했다).
        //
        // 두 가지를 다 처리해야 "일부만 안 바뀌는" 문제가 없다:
        //  1) GetComponentsInChildren(true) — 파츠가 여러 개인 모델(자식 렌더러 여럿)
        //  2) sharedMaterials(복수) — 렌더러 하나가 머티리얼을 여러 슬롯(서브메시별)에 물고 있는 경우.
        //     renderer.material(단수)은 슬롯 0만 바꾸고 나머지 슬롯은 원본 그대로 남는다 — 실제로
        //     겪은 버그(계기판 등 일부만 원래 색으로 남음).
        public static void TintPreserveShape(GameObject root, Color tint)
        {
            if (root == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                Material[] shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;

                var replaced = new Material[shared.Length];
                for (int m = 0; m < shared.Length; m++)
                {
                    Material original = shared[m];
                    if (original == null) continue;

                    var mat = new Material(original); // 슬롯별 인스턴스 — 원본 셰이더/텍스처 그대로
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                    else if (mat.HasProperty("_Color")) mat.color = tint;
                    // 원본 머티리얼이 Alpha Clipping(_AlphaClip=1)을 같이 쓰면, 클리핑 판정이
                    // "텍스처 알파 × _BaseColor 알파 > _Cutoff" 로 계산된다. 틴트에 반투명값(알파<1)을
                    // 주면 이 곱셈 때문에 원래는 다 보여야 할 픽셀까지 컷오프 밑으로 떨어져 통째로
                    // 잘려나간다(분류기/합류기가 희미하게만 보이던 원인, 벨트 코너 모양이 어긋나던 원인).
                    // 클리핑은 "텍스처 자체 모양"만 보고 판정하도록 컷오프를 거의 0으로 낮추고,
                    // 반투명 느낌은 블렌드(_BaseColor 알파)만으로 내게 분리한다.
                    if (mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0f && mat.HasProperty("_Cutoff"))
                        mat.SetFloat("_Cutoff", 0.01f);
                    replaced[m] = mat;
                }
                r.materials = replaced; // 슬롯 전체 한 번에 교체
            }
        }

        // GetComponent 없이 이미 갖고 있는 Renderer 참조로 바로 칠한다 — 매 프레임 여러 번
        // 호출되는 곳(예: BeltItemRenderer)에서 GetComponent 비용을 반복하지 않기 위함.
        public static void Colorize(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            renderer.sharedMaterial = GetOrCreateMaterial(color);
        }

        // 텍스처를 타일링해서 까는 전용 머티리얼 (바닥 격자 등). 색상 캐시 대상이 아니다 —
        // 텍스처마다 보통 하나씩만 쓰이므로 굳이 캐싱할 필요가 없다.
        public static Material CreateTiledMaterial(Texture2D texture, Vector2 tiling)
        {
            var mat = new Material(FindDefaultShader());
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            return mat;
        }

        private static Shader FindDefaultShader() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        private static Material GetOrCreateMaterial(Color color)
        {
            // "죽은 참조" 방어: Enter Play Mode Options에서 도메인 리로드를 꺼두면(흔한 성능
            // 설정), 이 static 캐시는 플레이 세션 사이에도 안 비워진다. 그런데 여기 든
            // Material은 Play 모드에서 만들어진 거라 Play를 멈추면 실제로는 파괴된다 — 그러면
            // 캐시엔 "파괴된 오브젝트를 가리키는" 참조만 남는데, 유니티의 오버로드된 ==
            // 덕분에 그 값은 여전히 null과 같다고 비교되지만 캐시 안에는 그대로 남아있어서,
            // 다음 플레이 세션에서 재사용하면 렌더러에 죽은 머티리얼이 배정돼 에러도 없이
            // 조용히 안 보이게 된다(실제로 겪은 버그: 벨트 위 아이템이 아무 표시 없이 사라짐).
            // cached != null로 죽은 참조를 걸러내고 다시 만들어서 스스로 복구되게 한다.
            if (materialCache.TryGetValue(color, out var cached) && cached != null) return cached;

            var mat = new Material(FindDefaultShader()) { color = color };

            if (color.a < 1f)
            {
                // URP Lit을 스크립트로 Transparent 서페이스로 전환할 때 필요한 최소 설정.
                mat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
                mat.SetFloat("_Blend", 0f); // Alpha blend
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            materialCache[color] = mat;
            return mat;
        }
    }
}
