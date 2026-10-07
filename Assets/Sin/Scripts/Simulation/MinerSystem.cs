using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 채굴기들을 매 틱 순회하며 산출을 누적하고, 벨트 없이 곧바로 코어 저장고로 "원격 전송"한다.
    // 채굴기는 입출력 포트가 없다 — 캔 만큼 즉시 코어로 들어간다는 게 원래 기획 의도.
    // 코어가 가득 차 있으면 BufferedOutput에 대기시켰다가 다음 틱에 다시 시도한다(잃지 않음).
    public sealed class MinerSystem
    {
        public void Tick(float deltaSeconds, List<MinerInstance> miners, List<ProcessorInstance> processors,
            int coreProcessorIndex, GameDatabase database, FactoryStatistics statistics = null)
        {
            ProcessorInstance core = coreProcessorIndex >= 0 && coreProcessorIndex < processors.Count
                ? processors[coreProcessorIndex]
                : null;

            for (int i = 0; i < miners.Count; i++)
            {
                var miner = miners[i];
                if (miner == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveMiner 참고).

                // 매장지 정의(OreDepositDef)를 나중에 밸런스 패치해도 이미 지어진 채굴기가 즉시
                // 따라가도록, 캐시된 값을 신뢰하지 않고 매 틱 최신 값으로 다시 채운다. id가
                // 없으면(구버전 세이브 등) 원래 갖고 있던 값을 그대로 둔다.
                if (miner.OreDepositId >= 0 && miner.OreDepositId < database.OreDeposits.Count)
                {
                    var deposit = database.OreDeposits[miner.OreDepositId];
                    miner.MineIntervalSeconds = deposit.MineIntervalSeconds;
                    miner.YieldPerCycle = deposit.YieldPerCycle;
                    miner.OutputResourceId = deposit.ResourceId;
                }

                miner.Progress += deltaSeconds * miner.SpeedMultiplier;

                while (miner.Progress >= miner.MineIntervalSeconds)
                {
                    miner.Progress -= miner.MineIntervalSeconds;
                    int previousOutput = miner.BufferedOutput;
                    miner.BufferedOutput = System.Math.Min(previousOutput + miner.YieldPerCycle,
                        SimulationConstants.ResourceBufferCapacity);
                    statistics?.RecordProduced(miner.OutputResourceId, miner.BufferedOutput - previousOutput);
                }

                if (core == null) continue;

                while (miner.BufferedOutput > 0 && core.TryAcceptInput(miner.OutputResourceId, 1))
                {
                    miner.BufferedOutput--;
                }
            }
        }
    }
}
