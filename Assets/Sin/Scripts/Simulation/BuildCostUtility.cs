using Factory.Data;

namespace Factory.Simulation
{
    // 건설 비용(ResourceAmount[])을 코어 버퍼에서 확인/차감/환불하는 공용 로직. 예전엔
    // MachineGhostTool(일반 기계), PowerBuildController(발전기/송전탑), SimulationWorld(철거 환불)
    // 세 곳이 각자 따로 구현하고 있었다 — 그래서 한쪽만 고치고 한쪽을 깜빡하면 "고스트는
    // 초록인데 실제 설치는 거부된다" 같은 판정 불일치 버그가 났다. 이제 여기 하나만 고치면
    // 셋 다 같이 맞는다.
    public static class BuildCostUtility
    {
        // 소모하지 않고 "지금 지을 여유가 있는지"만 본다 — 고스트 색깔처럼 매 프레임 불러도
        // 부작용이 없어야 하는 곳에서 쓴다.
        public static bool CanAfford(ProcessorInstance core, ResourceAmount[] cost)
        {
            if (core == null || cost == null) return true;
            for (int i = 0; i < cost.Length; i++)
            {
                if (core.InputBuffer[cost[i].ResourceId] < cost[i].Amount) return false;
            }
            return true;
        }

        // 확인 + 차감을 한 번에 — 모자라면 하나도 안 깎고 false.
        public static bool TryPay(ProcessorInstance core, ResourceAmount[] cost)
        {
            if (!CanAfford(core, cost)) return false;
            if (core == null || cost == null) return true;
            for (int i = 0; i < cost.Length; i++)
            {
                core.InputBuffer[cost[i].ResourceId] -= cost[i].Amount;
            }
            return true;
        }

        public static void Refund(ProcessorInstance core, ResourceAmount[] cost)
        {
            if (core == null || cost == null) return;
            for (int i = 0; i < cost.Length; i++)
            {
                core.InputBuffer[cost[i].ResourceId] =
                    System.Math.Min(core.InputBuffer[cost[i].ResourceId] + cost[i].Amount, core.Capacity);
            }
        }
    }
}
