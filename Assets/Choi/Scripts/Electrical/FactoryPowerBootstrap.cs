using UnityEngine;

namespace Choi.SaveLoad
{
    /// <summary>SampleScene 1의 PowerManager 하나에서 전력/저장 기능을 조립합니다.</summary>
    [RequireComponent(typeof(PowerSaveManager))]
    public sealed class FactoryPowerBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            GetOrAdd<PowerGridSystem>();
            GetOrAdd<PowerBuildController>();
            GetOrAdd<FactorySaveBridge>();
            GetOrAdd<FactoryPowerPanel>();
        }

        private T GetOrAdd<T>() where T : Component
        {
            T component = GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }
    }
}
