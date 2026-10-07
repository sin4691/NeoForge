using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 드래그로 선택한 기계/벨트를 실제로 철거한다. 확정 후엔 배치와 마찬가지로 None 모드로
    // 돌아간다 — 계속 철거 모드로 남으면 실수로 기계를 더 지울 위험이 있다.
    public class DemolishConfirmButton : MonoBehaviour
    {
        [SerializeField] private DemolishTool demolishTool;
        [SerializeField] private BuildInputRouter router;
        [SerializeField] private Button button;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (!demolishTool.Confirm()) return;
            if (router != null) router.SetMode(BuildInputRouter.Mode.None);
        }
    }
}
