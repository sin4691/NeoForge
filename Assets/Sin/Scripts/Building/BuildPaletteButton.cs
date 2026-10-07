using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 팔레트 버튼 1개 = 기계 하나 선택(고스트 배치 모드) 또는 벨트 모드 전환.
    // 기계 종류는 machineId 문자열(Bae님 MachineData.machineID와 같은 값) 하나로만 식별한다.
    // 벨트 버튼인지는 machineId가 비어있는지가 아니라 isBeltButton으로 명시적으로 구분한다 —
    // 예전엔 비어있으면 그냥 벨트 모드로 넘어갔는데, 그러면 데이터 연결을 깜빡한 기계 버튼도
    // 조용히 "벨트"처럼 동작해서 원인을 알기 어려웠다.
    public class BuildPaletteButton : MonoBehaviour
    {
        [SerializeField] private BuildInputRouter router;
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private string machineId;
        [SerializeField] private bool isBeltButton;
        [SerializeField] private bool isDemolishButton;
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Color normalColor = new Color(0.2f, 0.2f, 0.2f, 0.85f);
        [SerializeField] private Color selectedColor = new Color(0.25f, 0.75f, 0.35f, 0.95f);

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(HandleClick);
            if (background == null) background = GetComponent<Image>();
        }

        private void OnEnable()
        {
            if (router == null) return;
            router.ModeChanged += HandleModeChanged;
            HandleModeChanged(router.CurrentMode); // 이 오브젝트가 나중에 활성화된 경우에도 현재 상태를 바로 반영.
        }

        private void OnDisable()
        {
            if (router != null) router.ModeChanged -= HandleModeChanged;
        }

        public void SetBackgroundColors(Color idle, Color selected)
        {
            normalColor = idle;
            selectedColor = selected;
            if (background == null) background = GetComponent<Image>();
            if (router != null) HandleModeChanged(router.CurrentMode);
            else if (background != null) background.color = normalColor;
        }

        // 지금 이 버튼이 대표하는 도구/기계가 실제로 선택돼 있는지 색으로 보여준다 — 안 그러면
        // (실제로 겪은 문제) 벨트 모드가 계속 켜져 있는데도 겉보기엔 아무 표시가 없어서 왜
        // 기계 탭이 안 먹히는지 알 길이 없다.
        private void HandleModeChanged(BuildInputRouter.Mode mode)
        {
            if (background == null) return;

            bool selected = isBeltButton ? mode == BuildInputRouter.Mode.Belt
                : isDemolishButton ? mode == BuildInputRouter.Mode.Demolish
                : mode == BuildInputRouter.Mode.PlaceMachine && !string.IsNullOrEmpty(machineId) && machineTool.SelectedMachineId == machineId;

            background.color = selected ? selectedColor : normalColor;
        }

        private void HandleClick()
        {
            if (isBeltButton)
            {
                // 이미 벨트 모드면 다시 눌러서 해제(None으로) — 벨트 모드는 배치처럼 확정
                // 버튼으로 자동 해제되는 계기가 없어서, 안 그러면 한 번 벨트를 고르고 나면
                // 도구를 계속 쥔 채로 남아 기계를 눌러도(레시피 확인 등) 항상 빌드 제스처로만
                // 먹혀서 다시는 기계를 탭할 수 없게 된다.
                if (router.CurrentMode == BuildInputRouter.Mode.Belt)
                {
                    router.SetMode(BuildInputRouter.Mode.None);
                    return;
                }

                machineTool.CancelPlacement();
                router.SetMode(BuildInputRouter.Mode.Belt);
                return;
            }

            if (isDemolishButton)
            {
                // 벨트 버튼과 같은 토글 규칙.
                if (router.CurrentMode == BuildInputRouter.Mode.Demolish)
                {
                    router.SetMode(BuildInputRouter.Mode.None);
                    return;
                }

                machineTool.CancelPlacement();
                router.SetMode(BuildInputRouter.Mode.Demolish);
                return;
            }

            if (string.IsNullOrEmpty(machineId))
            {
                Debug.LogError($"[BuildPaletteButton] '{name}'에 machineId가 설정되어 있지 않습니다.");
                return;
            }

            // 이미 이 기계를 배치 모드로 고른 상태면 다시 눌러서 해제 — 벨트 버튼과 동일한 이유.
            if (router.CurrentMode == BuildInputRouter.Mode.PlaceMachine && machineTool.SelectedMachineId == machineId)
            {
                machineTool.CancelPlacement();
                router.SetMode(BuildInputRouter.Mode.None);
                return;
            }

            machineTool.SelectMachine(machineId);
            router.SetMode(BuildInputRouter.Mode.PlaceMachine);
        }
    }
}
