using UnityEngine;

namespace Factory.UI
{
    // 노치/펀치홀/홈버튼바를 피해 UI를 Screen.safeArea 안으로 밀어넣는다. 캔버스를 꽉 채우는
    // 자식으로 이 오브젝트를 두고 모든 HUD를 그 아래에 두면, 각 UI의 앵커 좌표(팔레트 버튼 등)는
    // 그대로 두고도 전부 안전 영역 기준으로 배치된다.
    //
    // safeArea는 기기 회전·멀티태스킹·접는 화면 전개 등으로 바뀔 수 있어서 매 프레임 확인하되,
    // 실제로 값이 달라졌을 때만 RectTransform을 다시 만진다. 에디터 Game 뷰나 PC 빌드에서는
    // safeArea == 전체 화면이라 아무 효과가 없다(Device Simulator에서만 실제로 줄어듦).
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rt;
        private Rect appliedSafeArea = new Rect(0f, 0f, 0f, 0f);
        private Vector2Int appliedScreenSize;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            Apply();
        }

        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != appliedSafeArea
                || Screen.width != appliedScreenSize.x
                || Screen.height != appliedScreenSize.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (rt == null) rt = GetComponent<RectTransform>();
            if (rt == null || Screen.width <= 0 || Screen.height <= 0) return;

            appliedSafeArea = Screen.safeArea;
            appliedScreenSize = new Vector2Int(Screen.width, Screen.height);

            Rect safe = appliedSafeArea;
            Vector2 anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            Vector2 anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);

            // safeArea가 비정상이면(0 division, 뒤집힘 등) 전체 화면으로 폴백 — UI가 사라지는 것보단 낫다.
            bool sane = anchorMin.x >= 0f && anchorMin.y >= 0f
                && anchorMax.x <= 1f && anchorMax.y <= 1f
                && anchorMax.x - anchorMin.x > 0.5f && anchorMax.y - anchorMin.y > 0.5f;
            if (!sane)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
            }

            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
