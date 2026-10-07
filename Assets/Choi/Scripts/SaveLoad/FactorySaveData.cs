using System;
using System.Collections.Generic;

namespace Choi.SaveLoad
{
    [Serializable]
    public sealed class FactoryProgressData
    {
        public int version = 1;
        public int coreProcessorIndex = -1;
        public List<MinerProgressData> miners = new List<MinerProgressData>();
        public List<ProcessorProgressData> processors = new List<ProcessorProgressData>();
        public List<BeltProgressData> belts = new List<BeltProgressData>();
        public List<PowerNodeData> powerNodes = new List<PowerNodeData>();
        public List<PowerConnectionData> powerConnections = new List<PowerConnectionData>();
    }

    [Serializable]
    public sealed class MinerProgressData
    {
        public bool exists;
        public string machineKey;
        public string outputResourceKey;
        public float baseSpeed = 1f;
        // 매장지 밸런스 패치 후 옛 세이브를 로드해도 최신 값을 따라가도록, 로드 시 이 키로
        // 현재 GameDatabase.OreDeposits에서 다시 찾아 MinerInstance.OreDepositId를 채운다
        // (FactorySaveBridge.RestoreMiners 참고). 아래 두 값은 그 조회가 실패했을 때만(구버전
        // 세이브, 매장지 정의 삭제 등) 쓰이는 폴백이다.
        public string oreDepositKey;
        public float mineIntervalSeconds;
        public int yieldPerCycle;
        public float progress;
        public int bufferedOutput;
        public Int2Data anchor;
    }

    [Serializable]
    public sealed class ProcessorProgressData
    {
        public bool exists;
        public string machineKey;
        public string recipeKey;
        public string activeRecipeKey;
        public float baseSpeed = 1f;
        public Int2Data facing;
        public Int2Data anchor;
        public Int2Data footprint;
        public bool universalPorts;
        public bool isProcessing;
        public float progress;
        public int capacity;
        // 분류기/합류기 라운드로빈 위치. RoutingRole은 machineKey로 다시 유도하지만(RoutingRoles.For),
        // 이 커서는 런타임에만 존재하는 값이라 저장해야 로드 후 분배 순서가 안 튄다.
        public int routingCursor;
        public bool generatorFuelPort;
        public int ownerPowerNodeId = -1;
        public string selectedFuelKey;
        public List<ResourceStackData> input = new List<ResourceStackData>();
        public List<ResourceStackData> output = new List<ResourceStackData>();
    }

    [Serializable]
    public sealed class BeltProgressData
    {
        public bool exists;
        public int id;
        public bool hasNext;
        public int nextId;
        public float length;
        public float speed;
        public float itemSpacing;
        public bool hasSourceProcessor;
        public int sourceProcessorIndex;
        public bool hasTargetProcessor;
        public int targetProcessorIndex;
        public bool hasLockedResource;
        public string lockedResourceKey;
        public string lockedRecipeKey;
        public Int2Data cell;
        public Int2Data facing;
        public Vector3Data start;
        public Vector3Data end;

        // 크로스 벨트 타일(한 칸에 축 두 개) 복원용. 옛 세이브에는 없어서 JsonUtility가 기본값(false/0)을
        // 채우므로 일반 벨트로 복원된다(하위 호환).
        public bool isCrossable;
        // 이 세그먼트가 뻗는 축 방향(BeltSegment.CrossAxis). 주축은 고스트에서 고른 Facing 그대로.
        public Int2Data crossAxis;
        // true면 WorldGrid 2번째 레이어(수직축)에 등록된 세그먼트 — false면 1번 레이어(주축).
        public bool isCrossingLayer;
        // 철거 환불 복원용(BeltSegment.ConcreteCost / RefundMachineKey).
        public int concreteCost;
        public string refundMachineKey;

        public List<BeltItemProgressData> items = new List<BeltItemProgressData>();
    }

    [Serializable]
    public sealed class BeltItemProgressData
    {
        public string resourceKey;
        public float position;
    }

    [Serializable]
    public sealed class ResourceStackData
    {
        public string resourceKey;
        public int amount;
    }

    [Serializable]
    public sealed class PowerNodeData
    {
        public int id;
        public int kind;
        public Int2Data cell;
        public Int2Data facing;
        public float fuelSecondsRemaining;
        public int activeFuelResourceId = -1;
    }

    [Serializable]
    public sealed class PowerConnectionData
    {
        public int id;
        public int fromNodeId;
        public int toNodeId;
        public List<Int2Data> path = new List<Int2Data>();
    }

    [Serializable]
    public struct Int2Data
    {
        public int x;
        public int y;

        public Int2Data(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [Serializable]
    public struct Vector3Data
    {
        public float x;
        public float y;
        public float z;

        public Vector3Data(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }
}
