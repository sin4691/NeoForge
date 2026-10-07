using Factory.Audio;
using UnityEditor;
using UnityEngine;

// Tools > Audio > Create BGM Library — BgmManager가 읽는 곡 표를 정해진 자리(Assets/Resources)와
// 정해진 이름(BgmLibrary)으로 만든다. 손으로 만들면 Resources 폴더 밖이나 다른 이름에 둬서 안 읽히는
// 실수가 나기 쉬워서 메뉴로 뺐다. 이미 있으면 새로 만들지 않고 그걸 선택해 준다.
public static class CreateBgmLibraryMenu
{
    private const string FolderPath = "Assets/Resources";
    private const string AssetPath = FolderPath + "/BgmLibrary.asset";

    [MenuItem("Tools/Audio/Create BGM Library")]
    public static void Create()
    {
        var existing = AssetDatabase.LoadAssetAtPath<BgmLibrary>(AssetPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log($"[BGM] 이미 있습니다: {AssetPath}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(FolderPath)) AssetDatabase.CreateFolder("Assets", "Resources");

        var library = ScriptableObject.CreateInstance<BgmLibrary>();
        // 빌드에 들어가는 씬 두 개를 미리 채워둔다 — 곡(clip)만 끼우면 된다.
        library.entries.Add(new BgmLibrary.Entry { sceneName = "Title" });
        library.entries.Add(new BgmLibrary.Entry { sceneName = "Main" });
        AssetDatabase.CreateAsset(library, AssetPath);
        AssetDatabase.SaveAssets();

        Selection.activeObject = library;
        EditorGUIUtility.PingObject(library);
        Debug.Log($"[BGM] 만들었습니다: {AssetPath} — Title/Main 항목의 Clip에 곡을 끼우세요.");
    }
}
