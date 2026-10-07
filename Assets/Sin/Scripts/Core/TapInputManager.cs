using Factory.Building;
using UnityEngine;
using UnityEngine.InputSystem;

// 모바일 터치 / 에디터 마우스 클릭을 동일하게 처리하는 탭 입력 매니저.
// New Input System의 Pointer는 Mouse와 Touchscreen을 모두 아우르는 공통 베이스라
// 플랫폼별 분기 없이 하나의 경로로 처리할 수 있다.
public class TapInputManager : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask interactableLayer = ~0;
    [SerializeField] private BuildInputRouter buildInputRouter;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        var pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame) return;

        // 벨트/배치 도구가 활성 상태면(팔레트에서 고른 직후 ~ 확정/취소 전) 이 프레스는 빌드
        // 제스처의 일부지 기계 정보를 확인하려는 탭이 아니다 — 안 그러면 벨트를 기계에서부터
        // 끌기 시작하거나 기계 위로 끌어다 놓을 때, BuildInputRouter와 완전히 별개로 도는 이
        // 레이캐스트가 같이 반응해서 레시피 선택창이 의도치 않게 함께 떠버린다.
        if (buildInputRouter != null && buildInputRouter.IsToolActive) return;

        // UI 위(레시피 선택 버튼, 팔레트 버튼 등)를 누른 거면 월드 레이캐스트를 쏘면 안 된다 —
        // 안 그러면 레시피 버튼을 누르는 순간 그 뒤에 있는(또는 화면상 같은 위치의) 기계도
        // 같이 탭된 걸로 처리돼서, 방금 연 레시피 패널이 다시 열리는 등 상태가 꼬인다.
        // BuildInputRouter에는 이미 있던 체크인데 여기엔 빠져 있었다.
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        HandleTap(pointer.position.ReadValue());
    }

    public void HandleTap(Vector2 screenPosition)
    {
        if (targetCamera == null) return;

        Ray ray = targetCamera.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, interactableLayer))
        {
            hit.collider.GetComponentInParent<IInteractable>()?.OnTap();
        }
    }
}
