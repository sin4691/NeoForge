using System.Collections.Generic;
using UnityEngine;

namespace Factory.Simulation
{
    // 레시피 없는 벨트 라우팅 노드. None = 일반 기계/코어. Splitter는 입력 1개를 출력 여러
    // 벨트에 라운드로빈 분배, Merger는 입력 여러 벨트를 출력 1개로 병합한다(RoutingSystem 참고).
    public enum RoutingRole
    {
        None,
        Splitter,
        Merger,
    }

    // 제련로/성형기/합성기 등 "레시피를 소비해서 산출한다" 유형 기계 한 대의 런타임 상태.
    // 어떤 레시피인지는 RecipeId(데이터)로만 결정되고, 이 클래스와 ProcessorSystem은
    // 레시피별 분기를 두지 않는다 — 새 레시피 추가가 코드 무변경으로 동작하는 근거.
    //
    // 코어도 이 타입을 그대로 재사용한다 (RecipeId=-1이라 ProcessorSystem이 건드리지
    // 않고, 그냥 아무거나 받아서 쌓아두기만 하는 저장소가 됨) — UniversalPorts만 true로
    // 켜서 4면 다 입출력 가능하게 구분한다.
    public sealed class ProcessorInstance
    {
        public int MachineId;

        private int recipeId = -1;
        // 이 기계가 지금 원하는 레시피. 대입될 때마다 BeltTopologyVersion을 올린다 — 분류기
        // 입구 벨트가 "어느 갈래로 보낼지" 캐싱해두는 BeltRouting 캐시가, 레시피가 바뀌면
        // (연결 안 바뀌어도) 다시 계산하도록 하기 위해서다. 안 그러면 레시피 미지정이던
        // 기계에 레시피를 갓 지정해도 캐시가 "그 갈래는 아직 원하는 게 없다"는 옛 판단을
        // 계속 재사용하는 새 버그가 생긴다.
        public int RecipeId
        {
            get => recipeId;
            set { if (recipeId == value) return; recipeId = value; BeltTopologyVersion.Bump(); }
        }

        public float SpeedMultiplier = 1f;

        // 전력 유무는 여기 하나로만 판정한다. PowerGridSystem은 전력 없으면 SpeedMultiplier를
        // 0으로 깎아서(시뮬레이션 코드는 안 건드리는 설계) 끈다 — 그런데 "0인지"를 여기저기서
        // 직접 float 비교로 매번 새로 판단하면, 어딘가 하나 빠뜨렸을 때(예: 사이클 시작 체크에
        // 안 넣음, 연기 이펙트에 안 넣음) 조용히 새는 구멍이 생긴다(실제로 둘 다 겪음). 전력에
        // 반응해야 하는 코드는 전부 SpeedMultiplier를 직접 보지 말고 이 프로퍼티만 봐야 한다.
        //
        // 켜져 있을 때(IsPowered=true): 새 사이클 시작(재료 소비) 가능, Progress 진행, 연기 등
        //   "작동 중" 시각효과 재생.
        // 꺼져 있을 때(IsPowered=false): 새 사이클을 시작하지 않음(재료 안 먹음), 이미 진행
        //   중이던 사이클은 Progress가 그 자리에서 멈춘 채 대기(전력 들어오면 이어서 진행 —
        //   재료를 이미 냈으니 취소하지 않고 자연스럽게 이어가는 게 맞음), 시각효과 정지.
        public bool IsPowered => SpeedMultiplier > 0f;

        // 벨트 라우팅 노드(분류기/합류기)면 None이 아니다. RoutingSystem이 이 값으로 분기하고,
        // ProcessorSystem은 RecipeId<0라 어차피 건드리지 않는다. RoutingCursor는 라운드로빈
        // 위치(분류기 = 다음 출력 벨트 인덱스, 합류기 = 마지막으로 내보낸 자원 id).
        public RoutingRole RoutingRole = RoutingRole.None;
        public int RoutingCursor;

        // 라우팅 노드(분류기/합류기) 전용 캐시 — "이 노드에서 나가는, 실제로 요청하는 기계로
        // 이어지는 출력 벨트들" 목록(RoutingSystem.CollectOutputBelts). BeltTopologyVersion이
        // 안 바뀌었으면 매 틱 전체 세그먼트를 다시 훑지 않고 그대로 재사용한다(사용자 지적:
        // "벨트 많으면 렉"). 기본값 -1은 아직 한 번도 계산 안 한 상태.
        public List<BeltSegment> CachedOutputBelts;
        public int CachedOutputBeltsVersion = -1;

        // 현재 진행 중인 사이클이 실제로 재료를 소비한 레시피. RecipeId는 사용자가 언제든
        // (처리 도중에도) 바꿀 수 있지만, 이미 시작된 사이클은 끝까지 이 값 기준으로
        // 완료되어야 한다 — 안 그러면 옛 레시피 재료로 새 레시피 산출물을 공짜로 만들어내게 된다.
        public int ActiveRecipeId = -1;

        // 사용자가 레시피 선택 패널에서 실제로 레시피를 지정한 순서(RecipeSelectionPanel.
        // SelectRecipe에서만 찍음 — 코드가 기본값/초기화로 RecipeId를 대입하는 경우는 안 침).
        // 분류기 입구로 들어오는 벨트 하나가 여러 갈래 중 어디 취향에 맞출지 정할 때
        // (BeltRouting.Resolve), "가장 최근에 사용자가 레시피를 정해준 갈래"를 우선한다 —
        // 예전엔 갈래 id(먼저 만들어진 벨트) 순으로 정해서, 나중에 다른 갈래 레시피를 바꿔도
        // 계속 옛 갈래로만 자원이 쏠렸다(사용자 보고).
        public int RecipeSetSequence;
        private static int nextRecipeSetSequence = 1;
        public static int NextRecipeSetSequence() => nextRecipeSetSequence++;

        // 입력 포트 = 이 기계가 놓인 셀 - Facing, 출력 포트 = 놓인 셀 + Facing.
        public Vector2Int Facing = new Vector2Int(1, 0);
        // true면(코어) 고정 포트 대신 4면 전부 입출력 가능.
        public bool UniversalPorts;
        public bool IsGeneratorFuelPort;
        public int OwnerPowerNodeId = -1;
        public int CoalResourceId = -1;
        public int BatteryResourceId = -1;

        private int selectedFuelResourceId = -1;
        // 발전기가 지금 원하는 연료 종류. RecipeId와 똑같은 이유로 대입될 때마다
        // BeltTopologyVersion을 올려야 한다 — 이게 빠져있으면(실제로 빠져있던 버그) 연료를
        // 골라도 BeltRouting/RoutingSystem이 캐시해둔 "누가 요청 중인가" 판단이 갱신 안 돼서,
        // 우연히 다른 무언가(다른 벨트 공사 등)가 캐시를 무효화시켜줄 때까지 계속 예전 판단
        // 그대로 남는다 — 발전기 3대 중 하나만 연료를 골라도 반영이 안 되고, 결국 셋 다 골라야
        // (그리고 그 사이 다른 공사가 우연히 캐시를 갱신시켜줘야) 겨우 반영되는 것처럼 보였다
        // (사용자 보고).
        public int SelectedFuelResourceId
        {
            get => selectedFuelResourceId;
            set { if (selectedFuelResourceId == value) return; selectedFuelResourceId = value; BeltTopologyVersion.Bump(); }
        }

        // footprint가 1칸보다 큰 기계(예: 2x2 합성기)의 포트 계산 기준. 어느 footprint 칸을
        // 밟아서 연결하든 항상 이 앵커 기준으로 포트 위치를 계산한다(GridUtility.GetPortCells).
        public Vector2Int Anchor;
        public Vector2Int Footprint = Vector2Int.one;

        public bool IsProcessing;
        public float Progress;

        // 인스턴스별 버퍼 용량(기본은 일반 기계 값, 코어는 CoreSpawner에서 훨씬 크게 설정).
        public int Capacity = SimulationConstants.ResourceBufferCapacity;

        // 자원 id로 인덱싱되는 고정 크기 버퍼. GameDatabase.ResourceCount에 맞춰 1회 할당.
        public int[] InputBuffer;
        public int[] OutputBuffer;
        // Runtime-only signal for a status lamp when routing consumes an item immediately.
        public int InputReceiptVersion;

        public ProcessorInstance(int resourceCount)
        {
            InputBuffer = new int[resourceCount];
            OutputBuffer = new int[resourceCount];
        }

        public bool TryAcceptInput(int resourceId, int amount)
        {
            if (resourceId < 0 || resourceId >= InputBuffer.Length || amount <= 0) return false;
            if (IsGeneratorFuelPort)
            {
                if (resourceId != SelectedFuelResourceId) return false;
                int stored = 0;
                for (int i = 0; i < InputBuffer.Length; i++) stored += InputBuffer[i];
                if (stored + amount > Capacity) return false;
            }
            if (InputBuffer[resourceId] + amount > Capacity) return false;
            InputBuffer[resourceId] += amount;
            InputReceiptVersion++;
            return true;
        }
    }
}
