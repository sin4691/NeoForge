using System;
using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 벨트 위 아이템 이동 + 적재/하차를 처리한다. 아이템은 GameObject가 아니라 각 세그먼트가
    // 들고 있는 BeltItem(struct) 리스트로 표현되고, 서로 추월할 수 없다는 규칙 하나로
    // 정체 시 뒤에서부터 압축되는 동작(back-pressure)이 자연스럽게 나온다.
    //
    // 세그먼트는 반드시 "하류(목적지 쪽)부터" 처리해야 한다 — 상류 세그먼트가 다음 세그먼트로
    // 넘어갈 자리가 있는지 판단하려면 그 다음 세그먼트가 이번 틱에 이미 전진을 마친 상태여야
    // 하기 때문. 예전에는 이걸 "호출자가 배열을 소스→목적지 순서로 넘겨준다"는 가정(+ 역순
    // 순회)으로 처리했는데, 벨트를 여러 번에 나눠 놓다가 기존 벨트의 "앞쪽"에 새 세그먼트를
    // 이어붙이면(새 세그먼트가 배열엔 나중에 추가되지만 실제로는 상류) 이 가정이 깨져서 그
    // 경계에서 한 틱씩 낡은 상태로 판단하는 버그가 있었다. 그래서 매 틱 NextSegmentId를 따라
    // 실제 연결 관계로 처리 순서를 다시 계산한다 (배열 순서에 의존하지 않음).
    public sealed class BeltSystem
    {
        private readonly Dictionary<int, BeltSegment> segmentsById = new Dictionary<int, BeltSegment>();
        private readonly List<BeltSegment> processingOrder = new List<BeltSegment>();
        private readonly HashSet<int> visited = new HashSet<int>();
        // BuildDownstreamFirstOrder가 마지막으로 계산했을 때의 BeltTopologyVersion. 벨트 연결이
        // 실제로 안 바뀌었으면(대부분의 틱) 매번 다시 훑지 않고 이전 결과를 그대로 쓴다(사용자
        // 지적: "벨트 많으면 렉"). -1은 아직 한 번도 계산 안 한 상태.
        private int cachedProcessingOrderVersion = -1;

        // 세그먼트가 새로 추가될 때마다 호출 (드문 이벤트라 매번 재구성해도 무방).
        public void Configure(List<BeltSegment> segments)
        {
            segmentsById.Clear();
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveSegment 참고).
                segmentsById[segments[i].Id] = segments[i];
            }
        }

        public void Tick(float deltaSeconds, List<BeltSegment> segments, List<ProcessorInstance> processors, GameDatabase database)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] == null) continue;
                LoadFromSource(segments[i], segments, processors, database);
            }

            if (cachedProcessingOrderVersion != BeltTopologyVersion.Current)
            {
                BuildDownstreamFirstOrder(segments);
                cachedProcessingOrderVersion = BeltTopologyVersion.Current;
            }
            for (int i = 0; i < processingOrder.Count; i++)
            {
                AdvanceSegment(processingOrder[i], deltaSeconds, processors);
            }
        }

        // NextSegmentId를 따라가는 후위 순회(post-order DFS): 한 세그먼트를 결과 리스트에
        // 넣기 전에 그 다음(하류) 세그먼트부터 먼저 넣으므로, 리스트 앞쪽이 항상 더 하류다.
        // 벨트 연결이 안 바뀐 틱에는 안 불린다(Tick의 버전 체크 참고) — 결과가 그대로 유효하다.
        private void BuildDownstreamFirstOrder(List<BeltSegment> segments)
        {
            processingOrder.Clear();
            visited.Clear();

            for (int i = 0; i < segments.Count; i++)
            {
                VisitDownstreamFirst(segments[i]);
            }
        }

        private void VisitDownstreamFirst(BeltSegment segment)
        {
            if (segment == null || !visited.Add(segment.Id)) return;

            if (segment.NextSegmentId.HasValue && segmentsById.TryGetValue(segment.NextSegmentId.Value, out var next))
            {
                VisitDownstreamFirst(next);
            }

            processingOrder.Add(segment);
        }

        private void LoadFromSource(BeltSegment segment, List<BeltSegment> segments, List<ProcessorInstance> processors, GameDatabase database)
        {
            bool headFree = segment.Items.Count == 0 || segment.Items[0].Position > segment.ItemSpacing;
            if (!headFree) return;

            if (!segment.SourceProcessorId.HasValue) return;
            var processor = processors[segment.SourceProcessorId.Value];

            // 분류기/합류기가 소스인 벨트는 RoutingSystem이 직접 적재한다 — 여기서 OutputBuffer를
            // 일반 배출하면 라운드로빈이 깨진다(라우팅 노드는 OutputBuffer를 안 씀).
            if (processor.RoutingRole != RoutingRole.None) return;

            if (processor.UniversalPorts)
            {
                LoadFromCore(segment, segments, processor, processors, database);
                return;
            }

            // 전력 없으면 이미 만들어둔 산출물도 안 내보낸다 — 꺼진 기계는 버퍼까지 완전히
            // 멈춘다(사용자 결정: 전력 끊기면 들어오는 것도 나가는 것도 다 멈춰야 함).
            if (!processor.IsPowered) return;

            // 코어가 "받는 쪽이 실제로 요청한 자원만 내준다"는 원칙(LoadFromCore)을 여기도
            // 그대로 적용한다 — 안 그러면 일반 기계는 만들어지는 대로 뭐든 무조건 벨트에
            // 실어버려서, 그 벨트 끝의 기계가 원하지 않는 자원이어도(예: 합성기는 금괴가
            // 필요한데 철괴가 옴) 계속 밀어넣다가 못 들어가고 벨트에 쌓이기만 한다(사용자
            // 보고 — "연결만 되어 있으면 자동으로 출력된다"는 게 문제).
            var target = BeltRouting.ResolveTerminal(segment, processors, segments);
            if (target == null) return; // 막다른 벨트 -> 받을 대상 없음, 안 내보낸다

            var buffer = processor.OutputBuffer;

            // 목적지 레시피가 잠금 당시와 달라졌으면(레시피 변경 등) 담당을 다시 정한다.
            if (segment.LockedSourceResourceId.HasValue && segment.LockedForRecipeId != target.RecipeId)
            {
                segment.LockedSourceResourceId = null;
            }

            if (!segment.LockedSourceResourceId.HasValue)
            {
                if (target.UniversalPorts)
                {
                    // 코어/미니 코어는 뭐든 받아준다 — 지금 버퍼에 있는 아무 자원이나 담당시킨다.
                    for (int r = 0; r < buffer.Length; r++)
                    {
                        if (buffer[r] > 0) { segment.LockedSourceResourceId = r; break; }
                    }
                }
                else if (target.RecipeId >= 0)
                {
                    AssignLaneResource(segment, segments, target, database.Recipes[target.RecipeId].Inputs, processors);
                }
                // target.RecipeId < 0(레시피 미지정)이면 뭘 원하는지 모르니 담당을 안 정한다
                // -> 아래에서 LockedSourceResourceId가 여전히 null이라 이번 틱은 대기.
                segment.LockedForRecipeId = target.RecipeId;
            }

            if (!segment.LockedSourceResourceId.HasValue) return;

            int lockedId = segment.LockedSourceResourceId.Value;
            if (buffer[lockedId] <= 0) return; // 담당 자원이 아직 안 만들어짐(또는 애초에 이 기계가 안 만드는 자원) -> 대기
            buffer[lockedId]--;
            segment.Items.Insert(0, new BeltItem(lockedId, 0f));
        }

        // 코어는 쌓아둔 걸 아무 벨트에나 무조건 흘려보내지 않는다 — 이 벨트 체인 끝에 실제로
        // 레시피를 지정받은 기계가 있고, 그 레시피가 필요로 하는 자원일 때만 내준다("먼저
        // 레시피를 지정해서 필요한 자원 정보를 전달받아야 준다"는 설계). 막다른 벨트, 코어
        // 자기 루프, 아직 레시피 미지정인 기계로는 아무것도 내주지 않는다.
        private void LoadFromCore(BeltSegment segment, List<BeltSegment> segments, ProcessorInstance core, List<ProcessorInstance> processors, GameDatabase database)
        {
            var target = BeltRouting.ResolveTerminal(segment, processors, segments);
            if (target == null) return; // 막다른 벨트 -> 요청하는 대상이 없음

            // 출발지와 도착지가 "같은 창고"인 자기 루프: 코어 내용물이 계속 흘러나와 제자리를
            // 도는 무의미한 순환이 된다(사용자가 보고). 미니 코어는 메인 코어랑 다른
            // ProcessorInstance 오브젝트라 ReferenceEquals(target, core)만으론 못 걸러진다 —
            // 실제로는 InputBuffer 배열 자체를 공유(LinkToMainCore)하니, 그 배열이 같은지로
            // 판정해야 "미니 코어↔메인 코어" 사이도 자기 루프로 잡힌다(실제로 겪은 버그: 다른
            // 기계가 코어 자원을 쓰고 있을 때 미니 코어를 코어에 연결하면 그 자원이 미니
            // 코어~코어 사이를 의미 없이 왕복 운반됨).
            if (ReferenceEquals(target.InputBuffer, core.InputBuffer)) return;

            if (target.UniversalPorts)
            {
                // 코어->다른 저장소(창고 재배치) 같은 특수 케이스는 레시피 개념이 없으니 있는 대로 내준다.
                DispenseAny(segment, core.InputBuffer);
                return;
            }

            if (target.IsGeneratorFuelPort)
            {
                int fuelId = target.SelectedFuelResourceId;
                if (fuelId < 0 || fuelId >= core.InputBuffer.Length || core.InputBuffer[fuelId] <= 0) return;
                core.InputBuffer[fuelId]--;
                segment.Items.Insert(0, new BeltItem(fuelId, 0f));
                return;
            }

            if (target.RecipeId < 0) return; // 아직 레시피 미지정 -> 뭐가 필요한지 모르니 안 줌

            // 전력 없으면 코어도 미리 내주지 않는다 — 여기서 안 막으면, 기계 자체는 (TryHandOff가
            // 막아서) 못 받더라도 코어에서 자원이 먼저 빠져나가 벨트 위에 쌓이는 게 눈에 보여서
            // "전력도 없는데 뭔가 움직인다"는 혼란을 준다(사용자 보고: 전력 없는 제련로에
            // 레시피만 골라도 코어에서 원석이 바로 튀어나옴).
            if (!target.IsPowered) return;

            // 목적지 레시피가 잠금 당시와 달라졌으면(사용자가 기계 탭해서 레시피를 바꿈)
            // 예전 재료로 굳은 잠금은 더 이상 유효하지 않다 — 풀어서 새 레시피 기준으로
            // 다시 담당 자원을 정하게 한다. 안 그러면 벨트가 예전 재료만 계속 실어 날라서
            // 기계가 새 레시피로는 영구히 재료를 못 받는다.
            if (segment.LockedSourceResourceId.HasValue && segment.LockedForRecipeId != target.RecipeId)
            {
                segment.LockedSourceResourceId = null;
            }

            // 이 라인이 담당할 자원은 "지금 재고가 있는지"와 무관하게 딱 한 번만 정해진다
            // ("이 라인은 철판 담당, 저 라인은 구리 담당"). 예전엔 재고 여부로 담당을 정해서,
            // 담당 자원(철판)이 코어에 아직 없는 잠깐 사이에 다른 라인이 이미 맡은 자원(구리)을
            // 대신 실어 날랐는데 — 구리는 이미 목적지 버퍼가 꽉 찰 만큼 계속 들어오다 보니
            // 정작 필요한 철판이 뒤늦게 와도 벨트가 구리로 막혀서 못 지나가는 정체가 실제로
            // 발생했다. 그래서 담당은 재고와 무관하게 즉시 정하고, 담당 자원이 코어에 없으면
            // 다른 자원을 대신 나르지 않고 그냥 빈 채로 기다린다.
            if (!segment.LockedSourceResourceId.HasValue)
            {
                AssignLaneResource(segment, segments, target, database.Recipes[target.RecipeId].Inputs, processors);
                segment.LockedForRecipeId = target.RecipeId;
            }

            if (!segment.LockedSourceResourceId.HasValue) return; // 레시피에 재료가 하나도 없는 등 방어적 처리

            int assignedId = segment.LockedSourceResourceId.Value;
            if (core.InputBuffer[assignedId] <= 0) return; // 담당 자원이 아직 코어에 없음 -> 대신 나르지 않고 대기
            core.InputBuffer[assignedId]--;
            segment.Items.Insert(0, new BeltItem(assignedId, 0f));
        }

        // 여러 라인이 코어에서 같은 목적지로 뻗어있을 때, 라인마다 서로 다른 재료를 담당하도록
        // "아직 아무도 안 맡은 재료"를 찾아 굳힌다(사용자가 실제로 겪은 버그 — 재고 기준으로
        // 정하면 다들 똑같은 순서로 시도해서 전부 하나로 몰렸다). 재료 종류보다 라인 수가 더
        // 많아서 전부 이미 다른 라인이 맡고 있으면, 남는 라인은 어쩔 수 없이 첫 재료를 중복으로
        // 맡는다(안 맡는 것보단 낫다).
        private void AssignLaneResource(BeltSegment segment, List<BeltSegment> segments, ProcessorInstance target, ResourceAmount[] inputs, List<ProcessorInstance> processors)
        {
            if (inputs.Length == 0) return;

            for (int i = 0; i < inputs.Length; i++)
            {
                int resourceId = inputs[i].ResourceId;
                if (IsClaimedByAnotherLane(segment, segments, target, resourceId, processors)) continue;
                segment.LockedSourceResourceId = resourceId;
                return;
            }

            segment.LockedSourceResourceId = inputs[0].ResourceId;
        }

        // 세그먼트 전체를 훑어서, "같은 목적지로 흘러가는 다른 라인"이 이미 이 자원으로
        // 굳어져 있는지 확인한다. 세그먼트 수가 적은 프로토타입 규모라 매번 O(N) 스캔이어도 무방.
        private bool IsClaimedByAnotherLane(BeltSegment self, List<BeltSegment> segments, ProcessorInstance target, int resourceId, List<ProcessorInstance> processors)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var other = segments[i];
                if (other == null || other == self) continue;
                if (other.LockedSourceResourceId != resourceId) continue;
                if (BeltRouting.ResolveTerminal(other, processors, segments) != target) continue;
                return true;
            }
            return false;
        }

        private static void DispenseAny(BeltSegment segment, int[] buffer)
        {
            for (int resourceId = 0; resourceId < buffer.Length; resourceId++)
            {
                if (buffer[resourceId] <= 0) continue;
                buffer[resourceId]--;
                segment.Items.Insert(0, new BeltItem(resourceId, 0f));
                return;
            }
        }

        private void AdvanceSegment(BeltSegment segment, float deltaSeconds, List<ProcessorInstance> processors)
        {
            var items = segment.Items;
            // 프론트 아이템은 이번 세그먼트 안에서 같은 세그먼트의 다른 아이템에 막히지 않는 한
            // 제약이 없어야 한다 — segment.Length로 캡을 걸면 경계를 넘는 이동분(overflow)이
            // 항상 0으로 잘려서 다음 세그먼트 진입 시 매번 position 0부터 다시 시작하는
            // "연결부위 멈칫" 현상이 생긴다. 경계 판정은 아래 reachedEnd/overflow에서 따로 한다.
            float nextMaxPos = float.PositiveInfinity;

            BeltSegment nextSegment = null;
            if (segment.NextSegmentId.HasValue)
            {
                segmentsById.TryGetValue(segment.NextSegmentId.Value, out nextSegment);
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                float desired = item.Position + segment.SpeedUnitsPerSecond * deltaSeconds;
                float maxPos = Math.Min(desired, nextMaxPos);
                bool reachedEnd = maxPos >= segment.Length - 0.0001f;

                if (reachedEnd)
                {
                    float overflow = Math.Max(0f, maxPos - segment.Length);
                    if (TryHandOff(segment, nextSegment, item, overflow, processors))
                    {
                        items.RemoveAt(i);
                        continue; // 다음(새) 프론트 아이템은 segment.Length까지 자유롭게 전진 가능
                    }
                    maxPos = segment.Length; // 넘어갈 자리가 없으면 끝에서 대기 (역압 전파)
                }

                item.Position = maxPos;
                items[i] = item;
                nextMaxPos = item.Position - segment.ItemSpacing;
            }
        }

        private static bool TryHandOff(BeltSegment segment, BeltSegment nextSegment, BeltItem item, float overflow, List<ProcessorInstance> processors)
        {
            if (nextSegment != null)
            {
                bool nextHeadFree = nextSegment.Items.Count == 0 || nextSegment.Items[0].Position > nextSegment.ItemSpacing;
                if (!nextHeadFree) return false;
                // overflow가 다음 세그먼트 길이보다 클 정도로 틱당 이동량이 크면(고속/저틱레이트)
                // 그 나머지는 다음 틱에서 이어서 처리되도록 다음 세그먼트 길이로 방어적으로 clamp.
                float startPos = Math.Min(overflow, nextSegment.Length);
                nextSegment.Items.Insert(0, new BeltItem(item.ResourceId, startPos));
                return true;
            }

            if (segment.TargetProcessorId.HasValue)
            {
                var processor = processors[segment.TargetProcessorId.Value];
                bool isRegularMachine = !processor.UniversalPorts && !processor.IsGeneratorFuelPort
                    && processor.RoutingRole == RoutingRole.None;
                if (isRegularMachine)
                {
                    // 레시피가 없는 기계 = 아무것도 요청하지 않았다. 벨트가 여기로 밀어넣으면
                    // 입력 버퍼에 죽은 재고로 쌓이기만 한다("기계만 놓으면 레시피 없이도 자동으로
                    // 빨려 들어간다"는 사용자 보고). 안 받는다 — 아이템은 벨트 끝에서 대기하고,
                    // 상류로 역압이 전파된다.
                    if (processor.RecipeId < 0) return false;

                    // 전력 없으면 입력도 안 받는다 — 꺼진 기계는 버퍼까지 완전히 멈춘다(사용자
                    // 결정). 코어/라우팅 노드는 전력 소모가 없는 설비라 여기 해당 안 됨.
                    if (!processor.IsPowered) return false;
                }
                return processor.TryAcceptInput(item.ResourceId, 1);
            }

            return false; // 막다른 벨트: 아이템을 잃지 않고 끝에서 대기시킨다.
        }
    }
}
