using System.Collections.Generic;
using UnityEngine;
using Bae.Data;

namespace Bae.SO
{
    [CreateAssetMenu(fileName = "NewMachine", menuName = "Data/Machine")]
    public class MachineSO : ScriptableObject
    {
        public string machineID;
        public string machineName;
        public int powerConsumption;
        public int inputSlots;
        public int outputSlots;
        public int gridWidth = 1;
        public int gridHeight = 1;
        public string prefabName; // Addressables 키 값 (예: "Prefab_Smelter")
        // 건설 비용 — 자원 id를 필요한 개수만큼 반복(레시피 inputItems와 같은 방식).
        public List<string> buildCostItems = new List<string>();

        public MachineData ToData()
        {
            return new MachineData
            {
                machineID = this.machineID,
                machineName = this.machineName,
                powerConsumption = this.powerConsumption,
                inputSlots = this.inputSlots,
                outputSlots = this.outputSlots,
                gridWidth = this.gridWidth,
                gridHeight = this.gridHeight,
                prefabName = this.prefabName,
                buildCostItems = this.buildCostItems
            };
        }
    }
}
