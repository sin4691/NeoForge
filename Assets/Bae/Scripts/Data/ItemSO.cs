using UnityEngine;
using Bae.Data;

namespace Bae.SO
{
    [CreateAssetMenu(fileName = "NewItem", menuName = "Data/Item")]
    public class ItemSO : ScriptableObject
    {
        [Tooltip("고유 ID (띄어쓰기 금지)")]
        public string itemID;
        public string itemName;
        [TextArea]
        public string description;
        public string iconName; // Addressables UI 아이콘 키 값
        public string prefabName; // Addressables 3D 모델(벨트 위) 키 값

        // SO의 데이터를 순수 JSON용 데이터 객체로 변환
        public ItemData ToData()
        {
            return new ItemData
            {
                itemID = this.itemID,
                itemName = this.itemName,
                description = this.description,
                iconName = this.iconName,
                prefabName = this.prefabName
            };
        }
    }
}
