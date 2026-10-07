using Factory.Simulation;
using UnityEngine;

namespace Factory.Buildings
{
    // 기계가 실제로 작동 중일 때(레시피 처리 중인 Processor, 캐는 중인 Miner — 전력 있을
    // 때만) 연기를 뿜어서 "돌아가고 있다"는 걸 눈으로 바로 알 수 있게 한다.
    // 분류기/합류기/코어(RecipeId<0)는 IsProcessing이 절대 true가 안 돼서 자동으로 안 나온다.
    //
    // 실제 파티클 시스템은 안 만든다 — 기계마다 하나씩 만들면 대수만큼(100대=최대 100)
    // 드로우콜이 늘어나서, 모든 기계가 공유하는 MachineSmokeEmitter 하나에 위치/방향만
    // 넘겨서 쏘게 한다(드로우콜 1개로 고정).
    public class MachineActivityIndicator : MonoBehaviour
    {
        // 플레이 중 Hierarchy에서 이 기계를 선택하면 Inspector에서 바로 조절/미리보기 가능.
        // 마음에 드는 값 찾으면 여기 기본값을 그 값으로 바꿔달라고 하면 전체 기본값이 바뀐다.
        [SerializeField] private float smokeHeight = 1.5f;      // 연기 나오는 높이(기계 기준 로컬 y)
        [SerializeField] private float puffInterval = 1f;       // 몇 초마다 한 번 "뽕" 하고 나올지
        [SerializeField] private int puffParticleCount = 2;     // 한 번 "뽕" 할 때 몇 개씩(뭉게뭉게 보이게)
        [SerializeField] private Vector3 driftDirection = new Vector3(-0.7f, 0.5f, 0.7f); // 진행 방향(정규화 안 해도 됨) — 기본: 왼쪽 위 대각선
        [SerializeField] private float driftSpeed = 0.6f;       // 그 방향으로 퍼지는 속도

        private Transform target;
        private MachineInstanceKind kind;
        private int instanceIndex;
        private SimulationDriver driver;
        private float nextPuffTime;

        public void Initialize(Transform target, MachineInstanceKind kind, int instanceIndex, SimulationDriver driver)
        {
            this.target = target;
            this.kind = kind;
            this.instanceIndex = instanceIndex;
            this.driver = driver;
        }

        private void Update()
        {
            if (target == null || driver == null || driver.World == null) return;
            if (!IsActive()) return;
            if (Time.time < nextPuffTime) return;

            nextPuffTime = Time.time + puffInterval;

            Vector3 worldPosition = target.position + Vector3.up * smokeHeight;
            Vector3 dir = driftDirection.sqrMagnitude > 0.0001f ? driftDirection.normalized : Vector3.up;
            MachineSmokeEmitter.Puff(worldPosition, dir * driftSpeed, puffParticleCount);
        }

        // ProcessorInstance.IsPowered/MinerInstance.IsPowered — 전력 판정은 항상 그 프로퍼티로만
        // 한다(SpeedMultiplier를 여기서 직접 비교하지 않음). 마침 가공 사이클 도중에 전력이
        // 끊기면 IsProcessing=true인 채로 얼어붙어서(다시는 안 끝나니) 영원히 "작동 중"으로
        // 남는 것도 막아야 하므로 IsProcessing과 IsPowered를 둘 다 본다.
        private bool IsActive()
        {
            switch (kind)
            {
                case MachineInstanceKind.Miner:
                    if (instanceIndex < 0 || instanceIndex >= driver.World.Miners.Count) return false;
                    var miner = driver.World.Miners[instanceIndex];
                    return miner != null && miner.IsPowered;
                case MachineInstanceKind.Processor:
                    if (instanceIndex < 0 || instanceIndex >= driver.World.Processors.Count) return false;
                    var processor = driver.World.Processors[instanceIndex];
                    return processor != null && processor.IsProcessing && processor.IsPowered;
                default:
                    return false;
            }
        }
    }
}
