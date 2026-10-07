using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Bae.Data
{
    // 이 스크립트 하나로 '재련로', '성형기', '합성기' 등 모든 설비가 동작합니다.
    public class GenericMachine : MonoBehaviour
    {
        public string myMachineID; // 예: "Smelter" (이 값만 외부에서 주입해 주면 됩니다)
        
        private MachineData myData;
        private RecipeData currentRecipe;

        // 인게임 인벤토리 (시뮬레이션 용도)
        public List<string> inputInventory = new List<string>();
        public List<string> outputInventory = new List<string>();

        private bool isProcessing = false;

        public void Initialize(string machineID)
        {
            myMachineID = machineID;
            
            // 1. 데이터 매니저에서 내 스펙을 가져옵니다.
            if (DataManager.Instance.machineDict.TryGetValue(machineID, out myData))
            {
                Debug.Log($"{myData.machineName} 기계가 정상적으로 초기화되었습니다.");

                // ==========================================
                // [Addressables 로드 예시]
                // 실무에서는 아래처럼 Addressables를 사용하여 3D 모델을 불러옵니다.
                // ==========================================
                /*
                UnityEngine.AddressableAssets.Addressables.InstantiateAsync(myData.prefabName, transform).Completed += handle => {
                    GameObject visualModel = handle.Result;
                    Debug.Log("프리팹 로드 완료: " + visualModel.name);
                };
                */
            }
            else
            {
                Debug.LogError($"'{machineID}' 에 해당하는 기계 데이터를 찾을 수 없습니다.");
            }
        }

        // 유저가 UI에서 레시피를 골랐을 때 호출되는 함수
        public void SetRecipe(string recipeID)
        {
            if (DataManager.Instance.recipeDict.TryGetValue(recipeID, out currentRecipe))
            {
                Debug.Log($"[{myData.machineName}] 레시피 설정 완료: {currentRecipe.recipeID}");
            }
        }

        private void Update()
        {
            // 실제 시뮬레이션에서는 Update 대신 별도의 Tick 시스템 매니저를 통해 구동하는 것이 좋습니다. (GC Alloc 0 유지)
            if (currentRecipe != null && !isProcessing)
            {
                CheckAndProcess();
            }
        }

        private void CheckAndProcess()
        {
            // 재료가 충분한지 확인 (단순 예시 로직)
            bool hasAllIngredients = true;
            foreach (var reqItem in currentRecipe.inputItems)
            {
                if (!inputInventory.Contains(reqItem))
                {
                    hasAllIngredients = false;
                    break;
                }
            }

            if (hasAllIngredients)
            {
                // 재료 소모
                foreach (var reqItem in currentRecipe.inputItems)
                {
                    inputInventory.Remove(reqItem);
                }

                // 가공 시작
                StartCoroutine(ProcessRoutine());
            }
        }

        private IEnumerator ProcessRoutine()
        {
            isProcessing = true;
            
            // currentRecipe.timeToCraft 만큼 대기
            yield return new WaitForSeconds(currentRecipe.timeToCraft);
            
            // 결과물 생성
            foreach (var outItem in currentRecipe.outputItems)
            {
                outputInventory.Add(outItem);
                Debug.Log($"[{myData.machineName}] 결과물 생성됨: {outItem}");
            }

            isProcessing = false;
        }
    }
}
