using System.Collections.Generic;

namespace Factory.Simulation
{
    // 벨트 체인을 따라가 "최종 목적 기계"를 찾는 공용 로직. 두 시스템이 같은 규칙을 쓰도록 한 곳에 둔다:
    //  - BeltSystem: 코어가 이 벨트에 무슨 자원을 실을지 판단할 때
    //  - RoutingSystem: 분류기가 어느 출력 갈래로 아이템을 보낼지 판단할 때
    //
    // 핵심 규칙: "요청하지 않은 기계에는 보내지 않는다." 요청한다 = 레시피가 지정돼 있다(무엇이
    // 필요한지 앎) 또는 저장고(UniversalPorts)다. 레시피 미지정 기계는 벨트가 물건을 밀어넣어도
    // 입력 버퍼에 죽은 재고로 쌓이기만 하므로, 애초에 목적지로 치지 않는다.
    public static class BeltRouting
    {
        // 발전기는 IsGeneratorFuelPort 자체가 아니라 "연료 종류를 실제로 골랐는지"
        // (SelectedFuelResourceId >= 0)로 요청 여부를 판정해야 한다 — 레시피 미지정 기계와
        // 같은 이유다. 안 그러면 연료를 아직 안 고른 발전기도 "요청 중"으로 잡혀서, 분류기
        // 갈래가 3개(발전기 3대)일 때 그 중 하나만 연료를 골라도 우선순위 다툼에서 아직 연료를
        // 안 고른 다른 발전기가 이겨버릴 수 있다 — 그럼 입구 벨트가 그 발전기를 목적지로
        // 잠그는데 SelectedFuelResourceId가 -1이라 아무것도 못 내보내서, 결과적으로 셋 다
        // 연료를 골라야만(그래야 누가 이기든 유효함) 코어에서 석탄이 나오는 것처럼 보인다
        // (사용자 보고).
        public static bool IsRequestingConsumer(ProcessorInstance p)
            => p != null && (p.RecipeId >= 0 || p.UniversalPorts
                || (p.IsGeneratorFuelPort && p.SelectedFuelResourceId >= 0));

        // segment에서 NextSegmentId를 따라간 최종 목적 기계. 라우팅 노드(분류기/합류기)는 종착이
        // 아니라 통과 지점이라, 그 출력 갈래 중 "요청하는 기계"로 이어지는 갈래를 우선 따라간다.
        // 여러 갈래가 동시에 요청 중이면(=여러 갈래가 다 연결되어 있고 레시피도 있음) 그 중
        // 사용자가 가장 최근에 레시피를 지정해준 갈래를 우선한다(ProcessorInstance.
        // RecipeSetSequence) — 예전엔 갈래 id(먼저 만들어진 벨트) 순으로 뽑아서, 실제로는
        // 연결이 끊긴 갈래라도 레시피만 지정돼 있으면 계속 그쪽 자원만 실어 나르고 나중에 이어준
        // 다른 갈래는 영영 순서가 안 왔다(사용자 보고). 그런 갈래가 하나도 없으면 최저 id
        // 갈래로 폴백. 막다른 벨트면 null.
        public static ProcessorInstance ResolveTerminal(
            BeltSegment segment, List<ProcessorInstance> processors, List<BeltSegment> segments)
        {
            return Resolve(segment, processors, segments, segments.Count + 1);
        }

        // 세그먼트 하나당(BeltSegment.CachedTerminal) 결과를 저장해두고, BeltTopologyVersion이
        // 안 바뀌었으면 그대로 재사용한다 — 매 틱 벨트 체인/분류기 갈래를 처음부터 다시 훑던
        // 비용을 없앤다(사용자 지적: "벨트 많으면 렉"). 분류기 갈래 재귀 호출(아래 ResolveUncached
        // 안의 Resolve(branch, ...))도 이 캐시를 그대로 타므로, 갈래 하나가 여러 곳에서
        // 재귀적으로 다시 물어봐도 실제 계산은 토폴로지가 바뀔 때 딱 한 번만 일어난다.
        private static ProcessorInstance Resolve(
            BeltSegment segment, List<ProcessorInstance> processors, List<BeltSegment> segments, int guard)
        {
            if (segment.CachedTerminalVersion == BeltTopologyVersion.Current) return segment.CachedTerminal;

            var result = ResolveUncached(segment, processors, segments, guard);
            segment.CachedTerminal = result;
            segment.CachedTerminalVersion = BeltTopologyVersion.Current;
            return result;
        }

        private static ProcessorInstance ResolveUncached(
            BeltSegment segment, List<ProcessorInstance> processors, List<BeltSegment> segments, int guard)
        {
            var current = segment;
            while (current != null && guard-- > 0)
            {
                if (current.TargetProcessorId.HasValue)
                {
                    var proc = processors[current.TargetProcessorId.Value];
                    if (proc == null || proc.RoutingRole == RoutingRole.None) return proc;

                    ProcessorInstance wanted = null; int wantedSequence = -1; int wantedBranchId = int.MaxValue;
                    ProcessorInstance fallback = null; int fallbackBranchId = int.MaxValue;
                    for (int i = 0; i < segments.Count; i++)
                    {
                        var branch = segments[i];
                        if (branch == null || branch.SourceProcessorId != current.TargetProcessorId.Value) continue;
                        var resolved = Resolve(branch, processors, segments, guard);
                        if (resolved == null) continue; // 연결 안 됨(막다른 벨트) — 후보에서 아예 제외.
                        if (IsRequestingConsumer(resolved))
                        {
                            // 가장 최근에 레시피가 지정된 갈래 우선, 동률(둘 다 미지정 등)이면 id 오름차순.
                            if (resolved.RecipeSetSequence > wantedSequence
                                || (resolved.RecipeSetSequence == wantedSequence && branch.Id < wantedBranchId))
                            {
                                wanted = resolved; wantedSequence = resolved.RecipeSetSequence; wantedBranchId = branch.Id;
                            }
                        }
                        else if (branch.Id < fallbackBranchId)
                        {
                            fallback = resolved; fallbackBranchId = branch.Id;
                        }
                    }
                    return wanted ?? fallback;
                }
                if (!current.NextSegmentId.HasValue) return null;
                current = FindById(segments, current.NextSegmentId.Value);
            }
            return null;
        }

        private static BeltSegment FindById(List<BeltSegment> segments, int id)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] != null && segments[i].Id == id) return segments[i];
            }
            return null;
        }
    }
}
