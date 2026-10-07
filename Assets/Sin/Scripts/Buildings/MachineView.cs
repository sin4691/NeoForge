using Factory.Simulation;
using Factory.UI;
using UnityEngine;

namespace Factory.Buildings
{
    public enum MachineInstanceKind
    {
        Miner,
        Processor,
    }

    // 그리드에 배치된 기계 하나의 얇은 뷰 셸. 실제 상태는 SimulationWorld의 배열이 갖고 있고,
    // 이 컴포넌트는 그 인스턴스를 씬 오브젝트/탭 입력과 연결하는 역할만 한다.
    public class MachineView : MonoBehaviour, IInteractable
    {
        [SerializeField] private MachineInstanceKind kind;
        [SerializeField] private int instanceIndex;
        [SerializeField] private SimulationDriver driver;

        public void Initialize(MachineInstanceKind kind, int instanceIndex, SimulationDriver driver)
        {
            this.kind = kind;
            this.instanceIndex = instanceIndex;
            this.driver = driver;
        }

        public void OnTap()
        {
            if (driver == null || driver.World == null) return;

            switch (kind)
            {
                case MachineInstanceKind.Miner:
                    var miner = driver.World.Miners[instanceIndex];
                    Debug.Log($"[MachineView] Miner buffered output: {miner.BufferedOutput}");
                    break;
                case MachineInstanceKind.Processor:
                    var processor = driver.World.Processors[instanceIndex];
                    // LogProcessorState는 RecipeId<0(코어 포함)이어도 안전하다("(미지정)"으로
                    // 찍음) — 예전엔 코어일 때 실제 버퍼 내용은 안 찍고 안내 문구만 띄워서,
                    // 정작 코어에 자원이 들어오고 있는지 확인할 방법이 없었다.
                    LogProcessorState(processor);

                    // 코어(UniversalPorts)는 레시피 개념이 없는 순수 저장소라 선택 UI는 안 연다.
                    if (processor.UniversalPorts) break;
                    if (processor.IsGeneratorFuelPort)
                    {
                        RecipeSelectionPanel.Instance?.OpenGenerator(instanceIndex);
                        break;
                    }

                    var machineId = driver.World.Database.Machines[processor.MachineId].Key;
                    RecipeSelectionPanel.Instance?.Open(instanceIndex, machineId);
                    break;
            }
        }

        // 벨트가 실제로 자원을 넣어주고 있는지, 레시피가 뭘 기다리는지 눈으로 바로 보이게
        // 탭할 때마다 InputBuffer/OutputBuffer 전체를 찍는다 — 연결은 됐는데 왜 안 만들어지는지
        // 알기 어려운 문제를 디버깅할 때 쓴다.
        private void LogProcessorState(ProcessorInstance processor)
        {
            var db = driver.World.Database;
            var inputParts = new System.Collections.Generic.List<string>();
            var outputParts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < db.Resources.Count; i++)
            {
                if (processor.InputBuffer[i] > 0) inputParts.Add($"{db.Resources[i].Key}={processor.InputBuffer[i]}");
                if (processor.OutputBuffer[i] > 0) outputParts.Add($"{db.Resources[i].Key}={processor.OutputBuffer[i]}");
            }

            string recipeInfo = processor.RecipeId < 0 ? "(미지정)" : db.Recipes[processor.RecipeId].Key;
            Debug.Log($"[MachineView] Processor idx={instanceIndex} recipe={recipeInfo} " +
                $"input=[{string.Join(", ", inputParts)}] output=[{string.Join(", ", outputParts)}] " +
                $"anchor={processor.Anchor} footprint={processor.Footprint} facing={processor.Facing}");
        }
    }
}
