using System;
using System.Collections.Generic;
using UnityEngine;

namespace Factory.Building
{
    // Bae님 데이터(Assets/Bae)엔 실제 유니티 프리팹 참조가 없다 — MachineData.prefabName은
    // Addressables 키 문자열이라, Addressables를 실제로 연결하기 전까지는 그 문자열로 모델을
    // 못 가져온다. 그때까지 기계별 전용 외형을 유지하기 위한 임시 다리: machineId -> 프리팹
    // 직접 매핑. Addressables가 실제로 붙으면 이 애셋이랑 참조를 지우고 그쪽 로딩으로
    // 넘어가면 된다(그래서 MachineGhostTool 쪽 참조는 nullable로 다뤄서 없어도 폴백 박스로
    // 자연스럽게 넘어가게 해뒀다).
    [CreateAssetMenu(menuName = "Factory/Machine Visual Library", fileName = "MachineVisualLibrary")]
    public class MachineVisualLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string machineId;
            public GameObject prefab;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public bool TryGetPrefab(string machineId, out GameObject prefab)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].machineId != machineId) continue;
                prefab = entries[i].prefab;
                return prefab != null;
            }
            prefab = null;
            return false;
        }
    }
}
