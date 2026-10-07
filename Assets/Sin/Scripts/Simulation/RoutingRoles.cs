namespace Factory.Simulation
{
    // 분류기/합류기는 Bae님 데이터(MachineData)에 구분 필드가 없어서 machineID 문자열로 판정한다.
    // RoutingRole은 배치 시 ProcessorInstance에 심는 값이지만, machineID만 알면 언제든 다시
    // 유도할 수 있는 순수 함수다 — 그래서 세이브/로드는 이 값을 저장하지 않고 로드 때 여기서 다시 구한다.
    //
    // 배치 코드(MachineGhostTool)와 세이브 복원 코드(FactorySaveBridge)가 같은 규칙을 쓰도록
    // 여기 한 곳에만 둔다.
    public static class RoutingRoles
    {
        public const string SplitterMachineId = "Splitter";
        public const string MergerMachineId = "Merger";

        public static RoutingRole For(string machineId)
        {
            if (machineId == SplitterMachineId) return RoutingRole.Splitter;
            if (machineId == MergerMachineId) return RoutingRole.Merger;
            return RoutingRole.None;
        }
    }
}
