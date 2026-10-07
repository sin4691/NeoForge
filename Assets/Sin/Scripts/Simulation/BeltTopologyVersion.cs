namespace Factory.Simulation
{
    // 벨트 연결 그래프(BeltSegment의 Source/Target/NextSegmentId)나 그 판단에 쓰이는 레시피
    // 배정(ProcessorInstance.RecipeId)이 바뀔 때마다 자동으로 올라가는 전역 버전 번호.
    // BeltSystem/BeltRouting/RoutingSystem이 "예전에 계산해둔 결과가 아직 유효한가"를 이
    // 번호 하나로 판정한다(버전이 그대로면 캐시 재사용, 바뀌었으면 다시 계산) — 매 틱 전체
    // 벨트 그래프를 처음부터 다시 훑던 방식(사용자 지적: "벨트 많으면 렉")을 걷어내기 위한
    // 캐시 무효화 신호다.
    //
    // 값 자체엔 의미가 없다("몇 번째 변경인지"는 안 중요) — "예전에 본 값과 같은가 다른가"만
    // 쓴다. BeltSegment.SourceProcessorId/TargetProcessorId/NextSegmentId,
    // ProcessorInstance.RecipeId의 세터가 대입될 때마다 자동으로 Bump()를 부른다 — 벨트
    // 놓기/철거/재배선/자동연결/이동/레시피 변경 등 연결 상태를 바꾸는 코드가 여기저기(건설
    // 도구, 이동 도구, 레시피 패널) 흩어져 있는데, 호출자마다 "버전 올리는 거 잊지 않기"를
    // 챙기게 하면 하나라도 빠뜨렸을 때 캐시가 낡은 값을 계속 쓰는 새 버그가 생긴다. 그래서
    // 무효화 책임을 호출자가 아니라 데이터 자체(세터)에 심어뒀다.
    //
    // 단, 세터는 값이 실제로 바뀔 때만 올린다. PowerGridSystem.EvaluatePower가 0.2초마다 모든
    // 기계의 RecipeId를 (대부분 같은 값으로) 다시 대입하는데, 같은 값에도 올리던 시절엔 그때마다
    // 맵 전체 캐시가 날아가 대형 공장에서 그 재계산이 프레임을 크게 잡아먹었다.
    public static class BeltTopologyVersion
    {
        public static int Current { get; private set; }
        public static void Bump() => Current++;
    }
}
