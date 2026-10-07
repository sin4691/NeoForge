using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Seo.UI
{
    /// <summary>
    /// 공장에 기계와 벨트가 늘어날수록 실시간 그림자 패스가 함께 증가하므로,
    /// 씬이나 팀 공용 프리팹을 수정하지 않고 실행 시 프로젝트 그림자를 완전히 비활성화한다.
    /// 이후 생성되는 건물과 구매 에셋에도 동일하게 적용된다.
    /// </summary>
    public static class FactoryShadowOptimizer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DisableRealtimeShadows()
        {
            ApplyAll();
            if (Object.FindFirstObjectByType<FactoryShadowEnforcer>() != null) return;

            var root = new GameObject("[Seo] Shadow Optimizer");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<FactoryShadowEnforcer>();
        }

        internal static void ApplyAll()
        {
            QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;

            // URP에서는 QualitySettings만 바꿔도 이미 활성화된 Light/Renderer 설정과
            // SSAO가 화면에 그림자처럼 남을 수 있어 렌더링 주체도 함께 비활성화한다.
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++) lights[i].shadows = LightShadows.None;

            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }

            DisableAmbientOcclusion();
        }

        private static void DisableAmbientOcclusion()
        {
            var featureField = typeof(ScriptableRenderer).GetField(
                "rendererFeatures",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (featureField == null) return;

            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var cameraData = cameras[i].GetComponent<UniversalAdditionalCameraData>();
                if (cameraData == null || cameraData.scriptableRenderer == null) continue;
                var features = featureField.GetValue(cameraData.scriptableRenderer)
                    as List<ScriptableRendererFeature>;
                if (features == null) continue;

                for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
                {
                    var feature = features[featureIndex];
                    if (feature == null) continue;
                    string featureName = feature.GetType().Name;
                    if (featureName.Contains("ScreenSpaceAmbientOcclusion")
                        || feature.name.Contains("AmbientOcclusion"))
                    {
                        feature.SetActive(false);
                    }
                }
            }
        }
    }

    // 씬 로드와 런타임 생성 뒤에도 그림자가 다시 켜지지 않게 보정한다.
    internal sealed class FactoryShadowEnforcer : MonoBehaviour
    {
        private float nextCheck;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            FactoryShadowOptimizer.ApplyAll();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            FactoryShadowOptimizer.ApplyAll();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            FactoryShadowOptimizer.ApplyAll();
        }
    }
}
