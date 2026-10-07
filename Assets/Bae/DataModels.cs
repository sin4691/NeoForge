using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bae.Data
{
    // 1. 아이템 데이터 모델
    [Serializable]
    public class ItemData
    {
        public string itemID;      // 예: "IronIngot"
        public string itemName;    // 예: "철 주괴"
        public string description; // 예: "철광석을 제련하여 만든 주괴"
        public string iconName;    // UI 로드용 아이콘 이름 (예: "Icon_IronIngot")
        public string prefabName;  // 벨트 위에 띄울 3D 모델(프리팹) 이름 (예: "Prefab_IronIngot")
    }

    // JSON 배열 파싱을 위한 래퍼 클래스
    [Serializable]
    public class ItemDatabase
    {
        public List<ItemData> items = new List<ItemData>();
    }

    // 2. 기계 데이터 모델
    [Serializable]
    public class MachineData
    {
        public string machineID;       // 예: "Molder"
        public string machineName;     // 예: "성형기"
        public int powerConsumption;   // 전력 소모 (MW)
        public int inputSlots;         // 입력 슬롯 수
        public int outputSlots;        // 출력 슬롯 수
        public int gridWidth = 1;      // 그리드 가로 칸 수 (기본값 1)
        public int gridHeight = 1;     // 그리드 세로 칸 수 (기본값 1)
        public string prefabName;      // 3D 프리팹 리소스 이름
        // 건설 비용 — inputItems/outputItems와 같은 컨벤션(자원 id를 필요한 개수만큼 반복).
        // 예: ["Concrete","Concrete","Concrete"] = 콘크리트 3개. 비워두면 무료로 설치 가능.
        public List<string> buildCostItems = new List<string>();
    }

    [Serializable]
    public class MachineDatabase
    {
        public List<MachineData> machines = new List<MachineData>();
    }

    // 3. 레시피 데이터 모델
    [Serializable]
    public class RecipeData
    {
        public string recipeID;        // 예: "Recipe_IronPlate"
        public string machineID;       // 이 레시피를 실행할 수 있는 기계 ID (예: "Molder")
        public float timeToCraft;      // 제작 소요 시간 (초)
        public List<string> inputItems = new List<string>();  // 필요 재료 (예: ["IronIngot", "IronIngot"])
        public List<string> outputItems = new List<string>(); // 결과물 (예: ["IronPlate"])
    }

    [Serializable]
    public class RecipeDatabase
    {
        public List<RecipeData> recipes = new List<RecipeData>();
    }
}
