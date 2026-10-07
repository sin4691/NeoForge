using Bae.Data;
using Factory.Data;
using Unity.Profiling;
using UnityEngine;

namespace Factory.Simulation
{
    // 고정 틱레이트로 SimulationWorld를 구동하는 MonoBehaviour 어댑터.
    // 렌더링(프레임률)과 시뮬레이션 정확성(틱레이트)을 분리해서, 프레임이 들쭉날쭉해도
    // 시뮬레이션 결과가 항상 동일하게 재현되도록 한다.
    public class SimulationDriver : MonoBehaviour
    {
        [SerializeField] private float tickRate = SimulationConstants.DefaultTickRate;

        public SimulationWorld World { get; private set; }

        private float accumulator;

        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("Sim.Tick");

        private void Awake()
        {
            Application.targetFrameRate = 30;

            // DataManager.Awake()가 JSON을 로드하는데, 유니티는 서로 다른 오브젝트의 Awake
            // 순서를 보장 안 해준다 — DataManager.cs에 [DefaultExecutionOrder]를 붙여서 항상
            // 이보다 먼저 돌게 해뒀다(Bae.Data.DataManager 참고). DataManager가 아예 없는
            // 상태(씬 구성 문제, 또는 Initialize(GameDatabase)로 직접 초기화할 테스트 코드)일
            // 수도 있어서 여기선 조용히 건너뛴다 — 실제 게임 씬에서 없으면 팔레트/기계 배치가
            // 전부 안 되는 걸로 바로 드러나니 별도 에러 로그 없이도 원인 찾기 쉽다.
            if (DataManager.Instance == null) return;

            // GameDatabase.Instance 같은 static 캐시로 "이미 있으면 재사용"하지 않고 매번 새로
            // 읽는다 — 그런 캐시를 쓰면 JSON을 다시 구워도(특히 Reload Domain을 꺼둔 경우)
            // 예전 값이 계속 남아있을 위험이 있는데, "빌드 없이 데이터만 바꿔서 반영"이 이
            // 파이프라인의 핵심 요구사항이라 그 위험을 파이프라인에 만들지 않음.
            var database = GameDatabase.LoadFromBaeData(DataManager.Instance);
            World = new SimulationWorld(database);
        }

        // 테스트 등에서 DataManager/JSON 없이 직접 구성한 GameDatabase로 초기화할 때 쓴다.
        public void Initialize(GameDatabase database)
        {
            World = new SimulationWorld(database);
        }

        private void Update()
        {
            if (World == null) return;

            float fixedDelta = 1f / tickRate;         
            float speedMultiplier = 1f;
            if (Bae.SpeedBuffManager.Instance != null)
            {
                speedMultiplier = Bae.SpeedBuffManager.Instance.CurrentSpeedMultiplier;
            }

            // 배속 변수를 곱해서 시간이 누적되는 속도를 조절
            accumulator += Time.deltaTime * speedMultiplier;

            while (accumulator >= fixedDelta)
            {
                // Profiler Hierarchy의 Calls 열이 "이 프레임에 틱이 몇 번 돌았나"가 된다.
                using (TickMarker.Auto()) World.Tick(fixedDelta);
                accumulator -= fixedDelta;
            }
        }
    }
}
