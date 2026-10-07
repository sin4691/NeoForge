using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 기계 배치 중 방향(Facing)을 90도씩 돌린다. 방향 표시는 화면 글자가 아니라 기계 위에
    // 영구적으로 붙어있는 작은 화살표(OutputArrow, PrefabBuilder 참고) 하나로만 보여준다.
    public class RotatePlacementButton : MonoBehaviour
    {
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private Button button;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(RotatePlacement);
        }

        private void RotatePlacement()
        {
            // FactoryPrototype 어셈블리는 전력 시스템 어셈블리를 직접 참조하지 않으므로,
            // 발전기 도구가 켜져 있을 때만 이름 기반으로 회전 요청을 전달한다.
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour.GetType().FullName != "Choi.SaveLoad.PowerBuildController") continue;
                var method = behaviour.GetType().GetMethod("RotateGeneratorFacing");
                if (method?.Invoke(behaviour, null) is bool rotated && rotated) return;
            }
            machineTool?.RotateFacing();
        }
    }
}
