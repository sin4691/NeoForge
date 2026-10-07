using System.Collections.Generic;
using Factory.Data;
using Unity.Profiling;

namespace Factory.Simulation
{
    // 한 판의 시뮬레이션 상태(기계 인스턴스, 벨트) + 매 틱 실행 순서를 들고 있는 컨테이너.
    // MonoBehaviour가 아닌 POCO라 씬 없이 유닛 테스트로 직접 구성/검증할 수 있다.
    //
    // 게임 시작 시 한 번에 채워지는 게 아니라 플레이 중 하나씩 놓이므로(터치 건설),
    // 배열이 아니라 List로 들고 있고 Add* 메서드로 점진적으로 늘어난다.
    public sealed class SimulationWorld
    {
        public readonly GameDatabase Database;
        public readonly WorldGrid Grid = new WorldGrid();
        public FactoryStatistics Statistics { get; }

        public List<MinerInstance> Miners { get; } = new List<MinerInstance>();
        public List<ProcessorInstance> Processors { get; } = new List<ProcessorInstance>();
        public List<BeltSegment> Segments { get; } = new List<BeltSegment>();

        // 채굴기가 원격 전송으로 곧장 넣어줄 코어의 인덱스(CoreSpawner가 설정). 아직 코어가
        // 없으면 -1이고, 그동안 채굴한 산출물은 MinerInstance.BufferedOutput에 대기한다.
        public int CoreProcessorIndex = -1;

        private readonly MinerSystem minerSystem = new MinerSystem();
        private readonly ProcessorSystem processorSystem = new ProcessorSystem();
        private readonly BeltSystem beltSystem = new BeltSystem();
        private readonly RoutingSystem routingSystem = new RoutingSystem();

        public SimulationWorld(GameDatabase database)
        {
            Database = database;
            Statistics = new FactoryStatistics(database.ResourceCount);
        }

        public int AddMiner(MinerInstance miner)
        {
            Miners.Add(miner);
            return Miners.Count - 1;
        }

        public int AddProcessor(ProcessorInstance processor)
        {
            Processors.Add(processor);
            return Processors.Count - 1;
        }

        // segments는 체인별로 소스→목적지 순서로 추가해야 한다 (BeltSystem 참고).
        public int AddBeltSegment(BeltSegment segment)
        {
            Segments.Add(segment);
            beltSystem.Configure(Segments);
            // 새 세그먼트 자체는 Source/Target/Next가 전부 null인 채로 추가될 수 있어서(예:
            // 아직 아무 데도 안 이어진 첫 조각) 그 세터들의 자동 Bump에 안 걸릴 수 있다 —
            // 그래도 Segments 리스트 자체가 늘어난 건 BuildDownstreamFirstOrder/CollectOutputBelts
            // 캐시가 알아야 하는 구조 변화라 여기서 명시적으로 올린다.
            BeltTopologyVersion.Bump();
            return Segments.Count - 1;
        }

        // 철거 전용 — 리스트 중간 아무 인덱스나 지울 수 있어야 한다(플레이어가 아무 기계나
        // 골라 지우므로, 항상 최근 것만 지우던 것과 다름). 그런데 InstanceIndex가 "리스트
        // 인덱스"와 같다는 전제가 WorldGrid/BeltSystem 곳곳에 깔려 있어서, 그냥 List.RemoveAt으로
        // 지우면 그 뒤 모든 occupant의 인덱스가 한 칸씩 밀려서 완전히 틀어진다. 그래서 실제로
        // 지우지 않고 그 자리를 null로 비워두는 "톰스톤" 방식을 쓴다 — 인덱스는 절대 안 바뀌고,
        // Tick 루프들만 null 슬롯을 건너뛰면 된다(MinerSystem/ProcessorSystem/BeltSystem 참고).
        //
        // 지우는 대상이 그 순간 들고 있던 자원(벨트 위 아이템, 기계 버퍼, 채굴기가 아직 코어로
        // 못 보낸 산출물)은 그냥 사라지면 안 되고 코어로 환불한다 — 안 그러면 철거를 타이밍
        // 나쁘게 쓸 때마다 자원이 조용히 증발한다.
        public void RemoveMiner(int index)
        {
            if (index < 0 || index >= Miners.Count || Miners[index] == null) return;

            RefundToCore(Miners[index].OutputResourceId, Miners[index].BufferedOutput);
            RefundBuildCost(Miners[index].MachineId);
            Miners[index] = null;
        }

        public void RemoveProcessor(int index)
        {
            if (index < 0 || index >= Processors.Count || Processors[index] == null) return;

            var processor = Processors[index];

            // 미니 코어처럼 InputBuffer/OutputBuffer가 메인 코어의 배열을 그대로(참조로) 공유하는
            // 경우엔 환불하면 안 된다 — RefundToCore가 "지금 이 배열에 든 값"을 "같은 배열"에
            // 또 더하는 꼴이라 자원이 그대로 두 배로 뻥튀기된다. 창구(미니 코어)만 없어질 뿐
            // 창고(공유 배열) 내용물은 이미 코어 쪽에 고스란히 남아있으니 그냥 둔다.
            bool sharesCoreBuffer = CoreProcessorIndex >= 0 && CoreProcessorIndex < Processors.Count
                && Processors[CoreProcessorIndex] != null
                && ReferenceEquals(processor.InputBuffer, Processors[CoreProcessorIndex].InputBuffer);

            if (!sharesCoreBuffer)
            {
                for (int r = 0; r < processor.InputBuffer.Length; r++) RefundToCore(r, processor.InputBuffer[r]);
                for (int r = 0; r < processor.OutputBuffer.Length; r++) RefundToCore(r, processor.OutputBuffer[r]);
            }
            // 미니 코어처럼 버퍼를 공유하는 경우도 지을 때 자기 몫의 건설 비용은 따로
            // 냈으므로(MachineGhostTool.DeductBuildResources), 버퍼 환불 여부와 무관하게 항상 돌려준다.
            RefundBuildCost(processor.MachineId);
            Processors[index] = null;

            // 이 프로세서를 참조하던 벨트 세그먼트들의 연결을 끊는다 — 안 그러면 다음 틱에
            // 지금 null이 된 자리를 그대로 인덱싱해서 예외가 난다.
            for (int i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment == null) continue;
                if (segment.SourceProcessorId == index) segment.SourceProcessorId = null;
                if (segment.TargetProcessorId == index) segment.TargetProcessorId = null;
            }
        }

        // 처리 도중 레시피를 바꿀 때 호출(RecipeSelectionPanel). 기계는 안 지우고, 안에 남아있던
        // 입출력 재료만 코어로 돌려보내고 진행 중이던 사이클을 리셋한다 — 안 그러면 옛 레시피
        // 재료가 남아있는 채로 새 레시피가 섞이거나, 사이클이 반쯤 진행된 채 얼어붙는다
        // (사용자 보고: 석탄다발 만들다 구리로 바꾸면 새 재료는 들어오는데 안에서 뒤엉킴).
        //
        // 나가는 벨트의 "담당 자원 잠금"도 같이 풀어준다 — 안 그러면 예전 산출물(석탄다발)로
        // 굳어있던 라인이 그 자원이 더는 안 나오는데도 계속 그것만 기다리느라, 새 레시피
        // 산출물(구리괴)이 버퍼에 쌓여도 절대 안 실어나른다(제조는 되는데 출력이 안 나가는
        // 버그의 원인). 들어오는 벨트(코어→기계) 쪽은 BeltSystem.LoadFromCore가 대상의
        // RecipeId 변화를 이미 스스로 감지해서 다시 잠그므로 여기서 안 건드려도 된다.
        public void FlushProcessorBuffers(int index)
        {
            if (index < 0 || index >= Processors.Count || Processors[index] == null) return;
            var processor = Processors[index];

            for (int r = 0; r < processor.InputBuffer.Length; r++)
            {
                RefundToCore(r, processor.InputBuffer[r]);
                processor.InputBuffer[r] = 0;
            }
            for (int r = 0; r < processor.OutputBuffer.Length; r++)
            {
                RefundToCore(r, processor.OutputBuffer[r]);
                processor.OutputBuffer[r] = 0;
            }
            processor.IsProcessing = false;
            processor.Progress = 0f;
            processor.ActiveRecipeId = -1;

            for (int i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment != null && segment.SourceProcessorId == index)
                {
                    segment.LockedSourceResourceId = null;
                }
            }
        }

        public void FlushGeneratorFuel(int index)
        {
            if (index < 0 || index >= Processors.Count || Processors[index] == null) return;
            ProcessorInstance processor = Processors[index];
            FlushProcessorBuffers(index);

            // 선택을 바꿀 때 발전기로 이동 중이던 이전 연료도 코어로 되돌린다.
            for (int i = 0; i < Segments.Count; i++)
            {
                BeltSegment segment = Segments[i];
                if (segment == null
                    || !ReferenceEquals(BeltRouting.ResolveTerminal(segment, Processors, Segments), processor)) continue;
                for (int n = 0; n < segment.Items.Count; n++)
                    RefundToCore(segment.Items[n].ResourceId, 1);
                segment.Items.Clear();
                segment.LockedSourceResourceId = null;
                segment.LockedForRecipeId = -1;
            }
        }

        public void RemoveSegment(int id)
        {
            if (id < 0 || id >= Segments.Count || Segments[id] == null) return;

            var items = Segments[id].Items;
            for (int j = 0; j < items.Count; j++) RefundToCore(items[j].ResourceId, 1);
            RefundBeltCost(Segments[id].ConcreteCost);
            var refundKey = Segments[id].RefundMachineKey;
            if (!string.IsNullOrEmpty(refundKey) && Database.TryGetMachineId(refundKey, out int refundMachineId))
            {
                RefundBuildCost(refundMachineId);
            }
            Segments[id] = null;
            // 리스트 슬롯 자체를 비우는 건(세그먼트 객체의 프로퍼티 대입이 아니라) 세터의 자동
            // Bump에 안 걸리니 명시적으로 올린다 — AddBeltSegment의 주석과 같은 이유.
            BeltTopologyVersion.Bump();

            // 이 세그먼트로 흘러들던 상류 세그먼트의 연결을 끊는다(RemoveProcessor가
            // SourceProcessorId/TargetProcessorId를 끊어주는 것과 같은 취지). Tick 루프만 보면
            // segmentsById.TryGetValue가 못 찾아서 "다음 없음"으로 안전하게 넘어가지만, 건설
            // 도구는 NextSegmentId.HasValue를 직접 봐서 "이미 다른 곳으로 흐르는 벨트"로 판단해
            // 재연결을 거부한다(BeltDragTool.Commit / TryDirectLink) — 그래서 철거한 자리에 새
            // 벨트를 다시 못 잇는 버그가 있었다. dangling 참조를 실제로 지워야 한다.
            for (int i = 0; i < Segments.Count; i++)
            {
                if (Segments[i] != null && Segments[i].NextSegmentId == id) Segments[i].NextSegmentId = null;
            }

            beltSystem.Configure(Segments); // segmentsById 캐시에서 지워진 id를 빼서 다시 맞춘다.
        }

        // 코어 용량(9999)이 넉넉해서 사실상 항상 다 받아준다 — 실패해도 자원을 잃는 것보단
        // 넘치는 만큼만 잘리는 게 낫다(Math.Min으로 클램프, TryAcceptInput의 전부-거절 방식 아님).
        // 코어가 아직 없거나(이론상으로만 가능) 이미 null이면 돌려줄 곳이 없으니 그냥 버린다.
        private void RefundToCore(int resourceId, int amount)
        {
            if (amount <= 0) return;
            if (CoreProcessorIndex < 0 || CoreProcessorIndex >= Processors.Count) return;

            var core = Processors[CoreProcessorIndex];
            if (core == null) return;
            core.InputBuffer[resourceId] = System.Math.Min(core.InputBuffer[resourceId] + amount, core.Capacity);
        }

        // 기계를 지을 때 뗀 건설 비용(BuildCostUtility — MachineGhostTool/PowerBuildController와
        // 공유)을 철거 시 그대로 돌려준다 — 버퍼 환불(위)은 "짓고 나서 만들거나 실어나르던
        // 재료"고, 이건 "짓는 데 자체에 든 재료"라 별개다.
        private void RefundBuildCost(int machineId)
        {
            if (machineId < 0 || machineId >= Database.Machines.Count) return;
            if (CoreProcessorIndex < 0 || CoreProcessorIndex >= Processors.Count) return;
            BuildCostUtility.Refund(Processors[CoreProcessorIndex], Database.Machines[machineId].BuildCost);
        }

        // 벨트는 Bae님 스키마 밖의 별도 비용 체계라(BeltDragTool.concreteCostPerTile) 각
        // 세그먼트가 자기가 지어질 때 낸 콘크리트 양을 직접 들고 있다가 철거 시 그만큼만 돌려준다.
        private void RefundBeltCost(int concreteCost)
        {
            if (concreteCost <= 0) return;
            if (!Database.TryGetResourceId("Concrete", out int concreteId)) return;
            RefundToCore(concreteId, concreteCost);
        }

        // Profiler에서 Deep Profile 없이도 틱 비용을 시스템별로 나눠 보기 위한 마커.
        private static readonly ProfilerMarker MinerMarker = new ProfilerMarker("Sim.Miner");
        private static readonly ProfilerMarker ProcessorMarker = new ProfilerMarker("Sim.Processor");
        private static readonly ProfilerMarker BeltMarker = new ProfilerMarker("Sim.Belt");
        private static readonly ProfilerMarker RoutingMarker = new ProfilerMarker("Sim.Routing");

        public void Tick(float deltaSeconds)
        {
            Statistics.Advance(deltaSeconds);
            using (MinerMarker.Auto()) minerSystem.Tick(deltaSeconds, Miners, Processors, CoreProcessorIndex, Database, Statistics);
            using (ProcessorMarker.Auto()) processorSystem.Tick(deltaSeconds, Database, Processors, Statistics);
            using (BeltMarker.Auto()) beltSystem.Tick(deltaSeconds, Segments, Processors, Database);
            // 벨트가 이번 틱에 라우팅 노드 InputBuffer로 배달한 것을, 곧바로 출력 벨트에 분배/병합한다.
            using (RoutingMarker.Auto()) routingSystem.Tick(Processors, Segments, Database);
        }
    }
}
