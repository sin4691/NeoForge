using UnityEngine;

namespace Factory.Building
{
    // ???먭?????+ ?移?以뚯쓣 泥섎━?섎뒗 移대찓??由ш렇. 移대찓?쇱쓽 湲곗〈 湲곗슱湲??뚯쟾)??洹몃?濡??먭퀬
    // ?쇰쿁(XZ ?됰㈃ ?????? + 嫄곕━留??吏곸뿬???꾨젅?대컢???좎??쒕떎.
    public class TouchCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        // ?먭렐 移대찓?쇱슜 ?쎌??????대룞?? ?ㅼ냼洹몃옒?쎌뿉?쒕뒗 ?꾨옒 Pan()???붾㈃-?붾뱶 蹂?섏쑝濡?
        // 1:1(?먭??쎌씠 吏싳? 吏?먯씠 ?먭??쎌쓣 ?곕씪?ㅻ뒗) ?쒕옒洹몃? 吏곸젒 怨꾩궛?섎?濡??곗씠吏 ?딅뒗??
        [SerializeField] private float panSpeed = 0.02f;
        // ?ㅼ냼洹몃옒??1:1 ?쒕옒洹몄뿉 怨깊븯??媛먮룄. 1 = ?뺥솗??1:1. ?덉쟾???쎌???怨좎젙 怨꾩닔(panSpeed)瑜?
        // ?⑥꽌 怨좏빐?곷룄 湲곌린?쇱닔濡?媛숈? ?ㅼ??댄봽媛 ?⑥뵮 ?ш쾶 ?吏곸???"?쒕옒洹멸? ?덈Т 鍮좊쫫" ?쇰뱶諛?.
        [SerializeField] private float panSensitivity = 1f;
        [SerializeField] private float zoomSpeed = 0.2f;
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float minOrthographicSize = 3f;
        [SerializeField] private float maxOrthographicSize = 20f;

        private float distance = 8f;
        private Vector3 pivot;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            Vector3 forward = targetCamera.transform.rotation * Vector3.forward;
            Vector3 pos = targetCamera.transform.position;
            distance = Mathf.Abs(forward.y) > 0.0001f ? Mathf.Clamp(-pos.y / forward.y, minDistance, maxDistance) : 8f;
            pivot = pos + forward * distance;
        }

        public void Pan(Vector2 screenDelta)
        {
            if (targetCamera == null) return;

            Vector3 right = targetCamera.transform.right;
            Vector3 flatForward = Vector3.Cross(right, Vector3.up);

            // ?쎌? ?명? -> ?붾뱶 ?명?. ?ㅼ냼洹몃옒?쎌씠硫??붾㈃ ?믪씠 ?꾩껜媛 怨?2*orthographicSize??
            // ?쎌????붾뱶 ?대룞?됱쓣 ?뺥솗??怨꾩궛?????덈떎(?댁긽?꽷룹쨲 臾닿? 1:1 ?쒕옒洹?. ?먭렐?대㈃
            // ??諛⑹떇?濡?怨좎젙 怨꾩닔 * 嫄곕━ 鍮꾨?濡?洹쇱궗?쒕떎.
            float worldPerPixel;
            if (targetCamera.orthographic && Screen.height > 0)
            {
                worldPerPixel = (2f * targetCamera.orthographicSize / Screen.height) * panSensitivity;
            }
            else
            {
                worldPerPixel = panSpeed * (distance / 8f);
            }

            pivot += (-right * screenDelta.x - flatForward * screenDelta.y) * worldPerPixel;
            ApplyTransform();
        }

        public void Zoom(float pinchDelta)
        {
            if (targetCamera == null) return;

            if (targetCamera.orthographic)
            {
                // ?ㅼ냼洹몃옒??移대찓?쇰뒗 ?꾩튂瑜???꺼???붾㈃???ш린媛 ??諛붾먮떎 ??orthographicSize媛 怨?以?
                targetCamera.orthographicSize = Mathf.Clamp(
                    targetCamera.orthographicSize - pinchDelta * zoomSpeed * 0.1f,
                    minOrthographicSize, maxOrthographicSize);
                ApplyTransform();
                return;
            }

            distance = Mathf.Clamp(distance - pinchDelta * zoomSpeed, minDistance, maxDistance);
            ApplyTransform();
        }

        public void FocusWorldPoint(Vector3 worldPoint)
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            // 移대찓?쇱쓽 ?꾩옱 媛곷룄? 以뚯? ?좎??섍퀬, ?붾㈃ 以묒떖 ?쇰쿁留?紐⑺몴 ?꾩튂濡???릿??
            pivot = new Vector3(worldPoint.x, 0f, worldPoint.z);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (targetCamera == null) return;
            
            // 移대찓?쇨? 留?寃쎄퀎?좎뿉 ?꾨떖?덉쓣 ???붾㈃ ?덈컲???덇났(留?諛???鍮꾩텛吏 ?딅룄濡??щ갚(Margin) ?ㅼ젙
            // 湲곌린 ?댁긽??醫낇슒鍮?? 理쒕? 以뚯븘???곹깭瑜?怨좊젮???щ갚?????볤쾶(40移? ?≪뒿?덈떎.
            float marginX, marginZ;
            if (targetCamera.orthographic)
            {
                marginZ = targetCamera.orthographicSize;
                marginX = marginZ * targetCamera.aspect;
            }
            else
            {
                marginZ = distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                marginX = marginZ * targetCamera.aspect;
            }
            
            marginZ *= 1.5f; 
            marginX *= 1.1f;
            
            pivot.x = Mathf.Clamp(pivot.x, -200f + marginX, 200f - marginX);
            pivot.z = Mathf.Clamp(pivot.z, -200f + marginZ, 200f - marginZ);

            Vector3 forward = targetCamera.transform.rotation * Vector3.forward;
            targetCamera.transform.position = pivot - forward * distance;
        }
    }
}
