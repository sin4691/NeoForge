using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 분류기/합류기(레시피 없는 벨트 라우팅 노드) 전용 처리. 벨트가 InputBuffer로 배달해준
    // 아이템을(BeltSystem.TryHandOff) 출력 벨트에 직접 얹는다. 라우팅 노드가 소스인 벨트는
    // BeltSystem.LoadFromSource가 건너뛰므로(OutputBuffer 일반 배출 안 함), 출력 벨트 적재는
    // 전적으로 여기서만 한다.
    //
    // 연결 관계는 세그먼트를 스캔해서 파악한다(SourceProcessorId == 노드 인덱스 → 출력 벨트).
    // 입력 벨트는 TargetProcessorId로 노드의 InputBuffer에 이미 배달되므로 따로 안 모은다.
    // 프로토타입 규모라 매 틱 O(세그먼트) 스캔이어도 무방하다(BeltSystem.IsClaimedByAnotherLane와 같은 수준).
    public sealed class RoutingSystem
    {
        public void Tick(List<ProcessorInstance> processors, List<BeltSegment> segments, GameDatabase database)
        {
            for (int i = 0; i < processors.Count; i++)
            {
                var node = processors[i];
                if (node == null) continue;

                if (node.RoutingRole == RoutingRole.Splitter) TickSplitter(node, i, processors, segments, database);
                else if (node.RoutingRole == RoutingRole.Merger) TickMerger(node, i, processors, segments, database);
            }
        }

        // 입력 벨트가 InputBuffer로 배달해준 것을, 연결된 출력 벨트에 라운드로빈으로 하나씩
        // 얹는다. 커서 벨트의 입구가 막혀 있거나 그 갈래 끝이 지금 당장 원하는 자원이 버퍼에
        // 없으면 건너뛰고 다음 벨트로 넘어간다(비율은 잠깐 깨지지만 전체가 멈추지 않음 — 설계
        // 결정). "그 갈래가 원하는 자원"은 ResolveDispatchResource가 갈래 끝 기계의 현재
        // 레시피를 보고 정한다 — 예전엔 그냥 버퍼에 있는 아무 자원이나 뱉어서, 갈래 끝 기계의
        // 레시피가 바뀌면 이제 필요 없는 자원도 계속 그 갈래로 밀어넣었다(사용자 보고).
        private void TickSplitter(ProcessorInstance splitter, int splitterIndex, List<ProcessorInstance> processors, List<BeltSegment> segments, GameDatabase database)
        {
            EnsureOutputBeltsCache(splitter, splitterIndex, processors, segments);
            var outputBelts = splitter.CachedOutputBelts;
            int n = outputBelts.Count;
            if (n == 0) return;

            int start = ((splitter.RoutingCursor % n) + n) % n; // 벨트 개수가 바뀌었을 수 있어 정규화
            for (int k = 0; k < n; k++)
            {
                int idx = (start + k) % n;
                var belt = outputBelts[idx];
                if (!HeadFree(belt)) continue;

                int resourceId = ResolveDispatchResource(splitter.InputBuffer, belt, processors, segments, database);
                if (resourceId < 0) continue; // 이 갈래가 지금 원하는 자원이 버퍼에 없음 — 다음 갈래로.

                belt.Items.Insert(0, new BeltItem(resourceId, 0f));
                splitter.InputBuffer[resourceId]--;
                splitter.RoutingCursor = (idx + 1) % n; // 다음 틱은 이 벨트 다음부터
                return; // 틱당 1개 — 벨트 입구 간격이 알아서 속도를 제한한다
            }
        }

        // 여러 입력 벨트가 InputBuffer로 배달해준 것을, 단일 출력 벨트에 얹는다. 자원 종류가
        // 섞여 있으면 종류를 번갈아 내보낸다(RoutingCursor = 마지막으로 내보낸 자원 id) — 한
        // 종류만 몰아 내보내면 다운스트림 2입력 기계가 한쪽 재료만 받아 굶는다. Splitter와 같은
        // 이유로, 출력 끝 기계가 지금 원하지 않는 자원은 버퍼에 있어도 건너뛴다.
        private void TickMerger(ProcessorInstance merger, int mergerIndex, List<ProcessorInstance> processors, List<BeltSegment> segments, GameDatabase database)
        {
            EnsureOutputBeltsCache(merger, mergerIndex, processors, segments);
            if (merger.CachedOutputBelts.Count == 0) return;

            var output = merger.CachedOutputBelts[0]; // 합류기는 출력 1개
            if (!HeadFree(output)) return;

            int resourceId = ResolveDispatchResource(merger.InputBuffer, output, processors, segments, database, merger.RoutingCursor);
            if (resourceId < 0) return;

            output.Items.Insert(0, new BeltItem(resourceId, 0f));
            merger.InputBuffer[resourceId]--;
            merger.RoutingCursor = resourceId;
        }

        // belt가 이어지는 갈래 끝 기계가 "지금 실제로 받을 수 있는" 자원을 buffer에서 찾는다:
        // 레시피를 지정했으면 그 레시피가 필요로 하는 자원 중 버퍼에 있는 것만, 저장고
        // (UniversalPorts)면 버퍼에 있는 아무거나. afterResourceId가 주어지면(합류기) 그 다음
        // id부터 한 바퀴 라운드로빈으로 찾아서 여러 자원을 번갈아 내보내는 동작을 유지하고,
        // 없으면(분류기) 그냥 맨 앞부터 찾는다 — 분류기는 갈래마다 필요한 자원이 보통 하나뿐이라
        // 순서를 안 따져도 된다.
        private static int ResolveDispatchResource(int[] buffer, BeltSegment belt, List<ProcessorInstance> processors,
            List<BeltSegment> segments, GameDatabase database, int afterResourceId = -1)
        {
            var target = BeltRouting.ResolveTerminal(belt, processors, segments);
            if (target == null) return -1;

            if (target.UniversalPorts)
            {
                return afterResourceId < 0 ? FirstNonEmpty(buffer) : NextNonEmptyRoundRobin(buffer, afterResourceId);
            }

            if (target.IsGeneratorFuelPort)
            {
                // 발전기 연료 포트는 레시피가 아니라 SelectedFuelResourceId 하나만 원한다
                // (BeltSystem.TickCoreOutgoing의 같은 분기 참고).
                int fuelId = target.SelectedFuelResourceId;
                if (fuelId < 0 || fuelId >= buffer.Length || buffer[fuelId] <= 0) return -1;
                return fuelId;
            }

            if (target.RecipeId < 0) return -1; // 레시피 미지정 — 뭘 원하는지 모름.

            var inputs = database.Recipes[target.RecipeId].Inputs;
            if (afterResourceId < 0)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    int r = inputs[i].ResourceId;
                    if (buffer[r] > 0) return r;
                }
                return -1;
            }

            int len = buffer.Length;
            for (int step = 1; step <= len; step++)
            {
                int r = (((afterResourceId + step) % len) + len) % len;
                if (buffer[r] <= 0) continue;
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (inputs[i].ResourceId == r) return r;
                }
            }
            return -1;
        }

        // node의 출력 벨트 목록을 node.CachedOutputBelts에 저장해둔다. BeltTopologyVersion이
        // 지난번 계산 때와 같으면(벨트를 아무것도 안 건드렸으면) 다시 훑지 않고 그대로 둔다 —
        // 매 틱 전체 세그먼트를 스캔하던 비용을 "실제로 뭔가 바뀐 틱"에만 내게 한다(사용자
        // 지적: "벨트 많으면 렉"). 캐시 무효화 신호(BeltTopologyVersion.Bump)는 BeltSegment의
        // Source/Target/NextSegmentId 세터와 ProcessorInstance.RecipeId 세터가 자동으로 낸다.
        private static void EnsureOutputBeltsCache(ProcessorInstance node, int nodeIndex, List<ProcessorInstance> processors, List<BeltSegment> segments)
        {
            if (node.CachedOutputBeltsVersion == BeltTopologyVersion.Current && node.CachedOutputBelts != null) return;

            if (node.CachedOutputBelts == null) node.CachedOutputBelts = new List<BeltSegment>();
            var outputBelts = node.CachedOutputBelts;
            outputBelts.Clear();
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                if (s == null || s.SourceProcessorId != nodeIndex) continue;

                // 이 갈래 끝에 "요청하는 기계"(레시피 지정 or 저장고)가 없으면 후보에서 제외한다.
                // 요청하지 않은 기계로는 아예 보내지 않는다 — 라운드로빈 비율에서도 빠지므로,
                // 나머지 갈래가 그만큼 더 받는다("가운데만 레시피 지정" 케이스에서 가운데가 100%).
                if (!BeltRouting.IsRequestingConsumer(BeltRouting.ResolveTerminal(s, processors, segments))) continue;

                // id 오름차순 삽입 정렬 — 실행마다 동일한 순서가 되도록(결정적 라운드로빈).
                // 목록이 최대 3개라 O(n^2)도 무의미하고, List.Sort(Comparison) 델리게이트
                // 할당을 피할 수 있다.
                int at = outputBelts.Count;
                while (at > 0 && outputBelts[at - 1].Id > s.Id) at--;
                outputBelts.Insert(at, s);
            }

            node.CachedOutputBeltsVersion = BeltTopologyVersion.Current;
        }

        private static bool HeadFree(BeltSegment belt)
        {
            return belt.Items.Count == 0 || belt.Items[0].Position > belt.ItemSpacing;
        }

        private static int FirstNonEmpty(int[] buffer)
        {
            for (int r = 0; r < buffer.Length; r++)
            {
                if (buffer[r] > 0) return r;
            }
            return -1;
        }

        // afterResourceId 다음 id부터 한 바퀴 돌며 재고가 있는 첫 자원. 없으면 -1.
        private static int NextNonEmptyRoundRobin(int[] buffer, int afterResourceId)
        {
            int len = buffer.Length;
            for (int step = 1; step <= len; step++)
            {
                int r = (((afterResourceId + step) % len) + len) % len;
                if (buffer[r] > 0) return r;
            }
            return -1;
        }
    }
}
