using UnityEngine;

namespace Choi.SaveLoad
{
    [CreateAssetMenu(menuName = "Factory/Power Structure Visual Catalog")]
    public sealed class PowerStructureVisualCatalog : ScriptableObject
    {
        [SerializeField] private GameObject generatorPrefab;
        [SerializeField] private GameObject transmissionTowerPrefab;

        public GameObject GetPrefab(PowerNodeKind kind)
        {
            if (kind == PowerNodeKind.Generator) return generatorPrefab;
            if (kind == PowerNodeKind.TransmissionTower) return transmissionTowerPrefab;
            return null;
        }

        public void Configure(GameObject generator, GameObject tower)
        {
            generatorPrefab = generator;
            transmissionTowerPrefab = tower;
        }
    }
}
