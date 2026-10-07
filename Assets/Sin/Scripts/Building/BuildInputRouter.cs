using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Factory.Building
{
    // 활성 터치 개수로 1터치 건설(BeltDragTool/MachineGhostTool)과 2터치 카메라(TouchCameraRig)를
    // 라우팅한다. 2터치가 시작되면 진행 중이던 1터치 제스처는 커밋 없이 취소한다 — 건설과
    // 카메라 조작이 서로를 막지 않으면서도 상태가 꼬이지 않게 하는 가장 단순한 규칙.
    public class BuildInputRouter : MonoBehaviour
    {
        public enum Mode
        {
            None,
            Belt,
            PlaceMachine,
            Demolish,
            External,
        }

        [SerializeField] private BeltDragTool beltTool;
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private DemolishTool demolishTool;
        [SerializeField] private TouchCameraRig cameraRig;

        private Mode mode = Mode.None;

        // 벨트/배치 도구가 활성 상태인지 — TapInputManager가 "지금 이 프레스는 빌드 제스처지
        // 기계 정보 확인 탭이 아니다"를 판단하는 데 쓴다.
        public bool IsToolActive => mode != Mode.None;
        public Mode CurrentMode => mode;
        public IBuildTool ExternalTool { get; private set; }

        // 외부 폴더의 도구를 공통 입력 인터페이스로 연결한다. 구체적인 기능은 호출자가 소유한다.
        public void SetExternalTool(IBuildTool tool)
        {
            SetMode(Mode.None);
            ExternalTool = tool;
            if (tool != null) SetMode(Mode.External);
        }
        // 튜토리얼처럼 건설 입력은 허용하되 카메라 이동/줌만 잠가야 하는 흐름에서 사용한다.
        public bool CameraInputEnabled { get; set; } = true;

        // 팔레트 버튼이 "지금 내가 선택된 도구인지" UI로 표시할 수 있게 모드가 바뀔 때마다 알림.
        public event Action<Mode> ModeChanged;

        private bool singleTouchActive;
        private Vector2? lastTwoTouchMidpoint;
        private float lastTwoTouchDistance;
        private Vector2? lastSingleTouchPosition;

        public void SetMode(Mode newMode)
        {
            if (singleTouchActive) CancelActiveTool();
            if (newMode != Mode.External) ExternalTool = null;
            mode = newMode;
            ModeChanged?.Invoke(mode);
        }

        private void Update()
        {
            // Touchscreen.current.touches는 "지금 눌린 터치들"이 아니라 고정 크기 터치 슬롯
            // 배열이라, 터치스크린 장치가 인식되기만 해도 Count > 0이 되어버린다 (마우스만
            // 쓰고 있어도). 실제로 눌려있거나 막 뗀 터치가 있을 때만 터치 경로를 타야 한다.
            if (HasRelevantTouch())
            {
                UpdateFromTouchscreen();
            }
            else
            {
                UpdateFromMouse();
            }
        }

        private static bool HasRelevantTouch()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return false;

            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].press.isPressed || touches[i].press.wasReleasedThisFrame) return true;
            }
            return false;
        }

        private void UpdateFromTouchscreen()
        {
            var touches = Touchscreen.current.touches;
            int activeCount = 0;
            TouchControl first = null;
            TouchControl second = null;

            for (int i = 0; i < touches.Count; i++)
            {
                // isPressed만 보면 뗀 그 프레임(wasReleasedThisFrame)이 빠져서 릴리즈가
                // 카운트되지 않고 else 분기(CancelActiveTool)로 새 버린다 — HasRelevantTouch와
                // 같은 기준으로 릴리즈 프레임도 유효한 터치로 센다.
                if (!touches[i].press.isPressed && !touches[i].press.wasReleasedThisFrame) continue;
                activeCount++;
                if (first == null) first = touches[i];
                else if (second == null) second = touches[i];
            }

            if (activeCount >= 2)
            {
                if (singleTouchActive) CancelActiveTool();
                HandleTwoTouch(first.position.ReadValue(), second.position.ReadValue());
            }
            else if (activeCount == 1)
            {
                lastTwoTouchMidpoint = null;
                bool overUI = IsOverUI(first.touchId.ReadValue());
                HandleSingleTouch(first.position.ReadValue(), first.press.wasPressedThisFrame, first.press.wasReleasedThisFrame, overUI);
            }
            else
            {
                lastTwoTouchMidpoint = null;
                if (singleTouchActive) CancelActiveTool();
            }
        }

        private void UpdateFromMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            lastTwoTouchMidpoint = null;
            Vector2 position = mouse.position.ReadValue();
            bool overUI = IsOverUI(null);
            HandleSingleTouch(position, mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame, overUI);

            // 오른쪽 버튼 드래그는 왼쪽 버튼(건설 제스처)과 별개로 항상 카메라를 이동시킨다 —
            // 터치의 두 손가락 팬에 대응하는 마우스 조작이라, 배치/벨트 모드 중에도 막히면 안 됨.
            if (CameraInputEnabled)
                HandleRightButtonPan(position, mouse.rightButton.wasPressedThisFrame, mouse.rightButton.wasReleasedThisFrame, overUI);

            float scroll = mouse.scroll.ReadValue().y;
            if (CameraInputEnabled && !overUI && Mathf.Abs(scroll) > 0.01f && cameraRig != null)
            {
                // 휠 한 칸(scroll notch)은 터치 핀치 델타보다 훨씬 작은 값이라, 같은
                // zoomSpeed를 그대로 곱하면 체감상 너무 느리다 — 휠 전용으로 크게 배율을 준다.
                cameraRig.Zoom(scroll * 40f); // 마우스 휠 = 핀치 줌에 대응
            }
        }

        private Vector2? lastRightButtonPosition;

        private void HandleRightButtonPan(Vector2 position, bool pressedThisFrame, bool releasedThisFrame, bool overUI)
        {
            if (pressedThisFrame)
            {
                lastRightButtonPosition = overUI ? (Vector2?)null : position;
                return;
            }

            if (lastRightButtonPosition.HasValue && cameraRig != null)
            {
                cameraRig.Pan(position - lastRightButtonPosition.Value);
                lastRightButtonPosition = position;
            }

            if (releasedThisFrame) lastRightButtonPosition = null;
        }

        private void HandleSingleTouch(Vector2 position, bool pressedThisFrame, bool releasedThisFrame, bool overUI)
        {
            var tool = ActiveTool();
            if (tool == null)
            {
                // 건설 도구가 선택 안 된 상태(None)에서는 한 손가락 드래그로 맵을 이동한다.
                if (CameraInputEnabled) HandleSingleTouchPan(position, pressedThisFrame, releasedThisFrame, overUI);
                return;
            }

            // 팔레트/확정 버튼 위에서 시작한 프레스는 UI 클릭이지 월드 건설 제스처가 아니다.
            // 한번 시작된 드래그가 도중에 UI 위로 지나가는 것까지는 막지 않는다.
            if (pressedThisFrame && overUI) return;

            if (pressedThisFrame)
            {
                singleTouchActive = true;
                tool.OnPressBegin(position);
            }
            else if (singleTouchActive)
            {
                tool.OnDrag(position);
            }

            if (releasedThisFrame && singleTouchActive)
            {
                singleTouchActive = false;
                tool.OnReleased(position);
            }
        }

        private void HandleSingleTouchPan(Vector2 position, bool pressedThisFrame, bool releasedThisFrame, bool overUI)
        {
            if (pressedThisFrame)
            {
                if (overUI) return;
                singleTouchActive = true;
                lastSingleTouchPosition = position;
                return;
            }

            if (singleTouchActive)
            {
                if (lastSingleTouchPosition.HasValue && cameraRig != null)
                {
                    cameraRig.Pan(position - lastSingleTouchPosition.Value);
                }
                lastSingleTouchPosition = position;
            }

            if (releasedThisFrame)
            {
                singleTouchActive = false;
                lastSingleTouchPosition = null;
            }
        }

        private static bool IsOverUI(int? pointerId)
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return false;
            return pointerId.HasValue ? eventSystem.IsPointerOverGameObject(pointerId.Value) : eventSystem.IsPointerOverGameObject();
        }

        private void HandleTwoTouch(Vector2 a, Vector2 b)
        {
            if (!CameraInputEnabled) return;
            Vector2 midpoint = (a + b) * 0.5f;
            float dist = Vector2.Distance(a, b);

            if (lastTwoTouchMidpoint.HasValue && cameraRig != null)
            {
                cameraRig.Pan(midpoint - lastTwoTouchMidpoint.Value);
                cameraRig.Zoom(dist - lastTwoTouchDistance);
            }

            lastTwoTouchMidpoint = midpoint;
            lastTwoTouchDistance = dist;
        }

        private void CancelActiveTool()
        {
            singleTouchActive = false;
            ActiveTool()?.OnCancelled();
        }

        private IBuildTool ActiveTool()
        {
            switch (mode)
            {
                case Mode.Belt: return beltTool;
                case Mode.PlaceMachine: return machineTool;
                case Mode.Demolish: return demolishTool;
                case Mode.External: return ExternalTool;
                default: return null;
            }
        }
    }
}
