using Choi.SaveLoad;
using UnityEditor;
using UnityEngine;

namespace Choi.Editor
{
    public static class PowerStructurePrefabSetup
    {
        private const string OutputFolder = "Assets/Choi/Prefab";
        private const string ResourceFolder = "Assets/Resources";
        private const string GeneratorSource = "Assets/OZEA_STUDIO_ULTIMATE/OZEA_STUDIO_Bundle_Serie_D/OZEA_Studio_D_002/Prefabs/SM_Shield_Generator.prefab";
        private const string TowerSource = "Assets/OZEA_STUDIO_ULTIMATE/OZEA_STUDIO_Bundle_Serie_HS/OZEA_Studio_HS_001/Prefabs/SM_Pylonne.prefab";

        [InitializeOnLoadMethod]
        private static void GenerateWhenMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/PowerGenerator.prefab") == null
                    || AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/TransmissionTower.prefab") == null
                    || AssetDatabase.LoadAssetAtPath<PowerStructureVisualCatalog>(ResourceFolder + "/PowerStructureVisualCatalog.asset") == null)
                    Generate();
            };
        }

        [MenuItem("Tools/Factory/Generate Power Structure Prefabs")]
        public static void Generate()
        {
            EnsureFolder("Assets/Choi", "Prefab");
            EnsureFolder("Assets", "Resources");
            GameObject generator = CreateVisualPrefab("PowerGenerator", GeneratorSource, PrimitiveType.Cylinder, 0.9f);
            GameObject tower = CreateVisualPrefab("TransmissionTower", TowerSource, PrimitiveType.Capsule, 1f);
            string catalogPath = ResourceFolder + "/PowerStructureVisualCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<PowerStructureVisualCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PowerStructureVisualCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.Configure(generator, tower);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static GameObject CreateVisualPrefab(string name, string sourcePath, PrimitiveType fallback, float scale)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            GameObject root = new GameObject(name);
            if (source != null)
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.name = "Visual";
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = Vector3.one * scale;
            }
            else
            {
                GameObject model = GameObject.CreatePrimitive(fallback);
                Object.DestroyImmediate(model.GetComponent<Collider>());
                model.name = "Visual_Fallback";
                model.transform.SetParent(root.transform, false);
            }
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, OutputFolder + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
