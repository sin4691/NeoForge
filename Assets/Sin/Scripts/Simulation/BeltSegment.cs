using System.Collections.Generic;
using UnityEngine;

namespace Factory.Simulation
{
    // 벨트 1칸 단위. 다음 칸은 직접 참조가 아니라 id로 연결해서, 이후 분기/합류(분배기)를
    // 도입할 때 세그먼트 하나가 여러 후보 중 목적지를 선택하는 구조로 확장할 수 있게 한다.
    public sealed class BeltSegment
    {
        public int Id;

        private int? nextSegmentId;
        // 다음 세그먼트로의 연결. 대입될 때마다 BeltTopologyVersion을 올린다 — 이 값을 따라가는
        // BeltSystem의 처리 순서 캐시/BeltRouting의 목적지 캐시가 이 신호로 다시 계산할지 판단한다.
        public int? NextSegmentId
        {
            get => nextSegmentId;
            set { if (nextSegmentId == value) return; nextSegmentId = value; BeltTopologyVersion.Bump(); }
        }

        public float Length = 1f;
        public float SpeedUnitsPerSecond = SimulationConstants.DefaultBeltSpeed;
        public float ItemSpacing = SimulationConstants.DefaultItemSpacing;

        // 이 칸을 지을 때 낸 콘크리트 양(BeltDragTool.concreteCostPerTile) — 철거 시 이만큼
        // 그대로 돌려준다(SimulationWorld.RefundBeltCost). 나중에 비용이 바뀌어도 이미 지어진
        // 벨트는 자기가 실제로 낸 값을 기억하고 있어야 하므로 상수 참조가 아니라 값으로 저장.
        public int ConcreteCost;

        // 철거 시 이 세그먼트가 "기계 한 대분" 건설비(Machines.json의 해당 machineID)를 돌려줘야 하면
        // 그 machineID. 크로스 벨트는 일반 벨트와 달리 콘크리트가 아니라 자기 건설비(철 주괴 등)를
        // 내므로, 한 칸에 있는 두 세그먼트 중 주축 하나만 이걸 들고 있다가 철거될 때 한 번만 환불한다
        // (SimulationWorld.RemoveSegment). 비어있으면(일반 벨트) 위 ConcreteCost만 환불한다.
        public string RefundMachineKey;

        // "크로스" 팔레트 버튼(MachineGhostTool.CrossBeltMachineId)으로 놓은 세그먼트만 true.
        // 크로스 타일은 한 칸에 이런 세그먼트가 항상 "쌍"으로(주축 1개 + 그 수직축 1개) 있고,
        // 둘 다 IsCrossable=true다 — BeltDragTool.Crossing.cs의 PlaceCrossableTile 참고.
        public bool IsCrossable;

        // 이 세그먼트가 뻗는 방향(고스트에서 고른 Facing 그대로, 또는 그걸 90도 돌린 수직축).
        // TryGetOccupantForConnection(BeltDragTool.Ports.cs)이 "어느 방향에서 접근했느냐"를
        // 이 값과 비교해서, 크로스 타일의 두 축(1번 레이어 주축 vs 2번 레이어 수직축) 중
        // 실제로 어느 세그먼트를 말하는 건지 가려낸다.
        public Vector2Int CrossAxis;

        private int? sourceProcessorId;
        // 체인의 첫 세그먼트에만 하나가 설정됨: 기계 산출물을 이 세그먼트로 실어 나른다.
        // 채굴기는 원격 전송(코어로 직배송)이라 벨트 소스가 될 수 없다 — Processor만 있음.
        // 대입될 때마다 BeltTopologyVersion을 올린다(BeltTopologyVersion.cs 참고).
        public int? SourceProcessorId
        {
            get => sourceProcessorId;
            set { if (sourceProcessorId == value) return; sourceProcessorId = value; BeltTopologyVersion.Bump(); }
        }

        // 이 라인이 처음 실어 나른 자원으로 굳어진 값 — 한 번 정해지면 계속 그 자원만
        // 싣는다("벨트 하나당 한 종류"). 안 그러면 소스가 여러 자원을 갖고 있을 때(코어처럼)
        // 매번 아무거나 골라서 한 벨트에 섞어 올리게 된다(BeltSystem.LoadFromSource 참고).
        public int? LockedSourceResourceId;

        // LockedSourceResourceId를 굳힐 당시 목적지 프로세서의 RecipeId. 목적지 레시피가
        // 바뀌면(BeltSystem.LoadFromCore) 이 값이 최신 RecipeId와 달라지므로 잠금을 풀고
        // 새 레시피 기준으로 다시 담당 자원을 정한다 — 안 그러면 레시피를 바꿔도 벨트가
        // 예전 재료만 계속 실어 날라서 기계가 영구히 기아 상태에 빠진다.
        public int LockedForRecipeId = -1;

        private int? targetProcessorId;
        // 체인의 마지막 세그먼트(NextSegmentId == null)에만 설정됨: 도착한 아이템을 받는 기계.
        // 대입될 때마다 BeltTopologyVersion을 올린다(BeltTopologyVersion.cs 참고).
        public int? TargetProcessorId
        {
            get => targetProcessorId;
            set { if (targetProcessorId == value) return; targetProcessorId = value; BeltTopologyVersion.Bump(); }
        }

        // Position 오름차순 정렬 유지 (Items[0] = 세그먼트 시작에 가장 가까운 아이템).
        public List<BeltItem> Items = new List<BeltItem>();

        // BeltRouting.Resolve 캐시 — "이 세그먼트를 따라가면 결국 어느 기계로 도착하나"의
        // 계산 결과. 매 틱 전체 체인/분류기 갈래를 다시 훑던 비용을 없애려고, 위 세터들이
        // 자동으로 올리는 BeltTopologyVersion과 비교해서 안 바뀌었으면 재계산 없이 그대로
        // 쓴다(BeltTopologyVersion.cs 참고). CachedTerminalVersion 기본값 -1은 아직 한 번도
        // 계산 안 한 상태를 뜻한다(Current는 0부터 시작하므로 절대 우연히 같아질 일이 없음).
        public ProcessorInstance CachedTerminal;
        public int CachedTerminalVersion = -1;
    }
}
