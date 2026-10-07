#if UNITY_EDITOR
using Seo.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.Editor
{
    public static class SeoUISetup
    {
        private const string ThemePath = "Assets/Seo/Resources/SeoUITheme.asset";
        private const string PrefabPath = "Assets/Seo/Prefabs/UI/SeoUIRoot.prefab";
        private const string SourceScenePath = "Assets/Scenes/Main.unity";
        private const string WorkbenchScenePath = "Assets/Seo/Scenes/UI_Workbench.unity";
        private const string VendorButtonRoot = "Assets/SCI-FI UI Pack Pro/SCI-FI UI Components/Textures/Button/";

        [MenuItem("Tools/Seo UI/Build UI Workbench")]
        public static void Build()
        {
            EnsureFolder("Assets/Seo/Resources");
            EnsureFolder("Assets/Seo/Prefabs");
            EnsureFolder("Assets/Seo/Prefabs/UI");
            EnsureFolder("Assets/Seo/Scenes");

            CreateOrUpdateTheme();
            GameObject prefab = CreateOrUpdateRootPrefab();

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(WorkbenchScenePath))
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, WorkbenchScenePath))
                    throw new System.InvalidOperationException("메인 씬을 Seo UI 작업 씬으로 복제하지 못했습니다.");
                AssetDatabase.Refresh();
            }

            var scene = EditorSceneManager.OpenScene(WorkbenchScenePath, OpenSceneMode.Single);
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject != null)
            {
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = SeoUIFactory.LandscapeReference;
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = 0.5f;
                }
            }

            var existing = GameObject.Find("SeoUIRoot");
            if (existing == null)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance != null) instance.name = "SeoUIRoot";
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Seo UI] UI_Workbench 씬, 테마, UI 루트 프리팹을 준비했습니다.");
        }

        private static void CreateOrUpdateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<SeoUITheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<SeoUITheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            theme.PanelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(VendorButtonRoot + "BasicPanel01_n.png");
            theme.ButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(VendorButtonRoot + "Btn01Cyan_n.png");
            theme.ButtonPressedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(VendorButtonRoot + "Btn01Cyan_p.png");
            EditorUtility.SetDirty(theme);
        }

        private static GameObject CreateOrUpdateRootPrefab()
        {
            var root = new GameObject("SeoUIRoot");
            root.AddComponent<UIManager>();
            root.AddComponent<FactoryHudController>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
