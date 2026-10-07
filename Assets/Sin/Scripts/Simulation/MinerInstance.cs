namespace Factory.Simulation
{
    // 채굴기 한 대의 런타임 상태. GameObject가 아니라 SimulationWorld가 배열로 들고 순회하는 순수 데이터.
    // 입출력 포트가 없다 — 캔 자원은 벨트 없이 곧바로 코어로 원격 전송된다(MinerSystem 참고).
    public sealed class MinerInstance
    {
        public int MachineId;
        public int OutputResourceId;
        public float SpeedMultiplier = 1f;

        // ProcessorInstance.IsPowered와 같은 목적 — 전력 판정을 한 곳으로 모아서, 여기저기서
        // SpeedMultiplier를 직접 비교하다 한 군데 빠뜨리는 실수를 막는다.
        public bool IsPowered => SpeedMultiplier > 0f;
        public float MineIntervalSeconds = SimulationConstants.DefaultMineIntervalSeconds;
        // 아래 밟고 있는 광물 노드(OreDepositDef)가 정한 사이클당 산출량.
        public int YieldPerCycle = 1;

        // 이 채굴기가 서 있는 매장지의 런타임 id(GameDatabase.OreDeposits 인덱스). -1이면
        // 매장지에 연결 안 됨(구버전 세이브 등) — 이땐 위 두 값을 그대로 신뢰한다.
        // MinerSystem.Tick이 매 틱 이 id로 최신 매장지 정의를 다시 읽어 위 두 값 +
        // OutputResourceId를 갱신한다 — 예전엔 건설 시점에 값을 복사해서 그대로 굳혀버려서,
        // 매장지 애셋의 밸런스를 나중에 패치해도 이미 지어진(또는 세이브에서 복원된) 채굴기는
        // 계속 옛날 속도로 남는 문제가 있었다(사용자 보고 — 긴급 밸런스 패치 시 신규/기존
        // 채굴기 속도가 뒤섞임).
        public int OreDepositId = -1;

        public float Progress;
        public int BufferedOutput;
    }
}
