using System.Collections.Generic;
using Factory.Building;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Choi.Tutorial
{
    /// <summary>
    /// 튜토리얼의 예시 화살표는 안내로만 남기고, 실제 벨트 경로는 자유롭게 설치하게 한다.
    /// BeltDragTool 자체의 포트/점유/연결 검사는 그대로 적용되며 튜토리얼 완료 여부는
    /// ChoiFactoryTutorialController가 실제 세그먼트 연결 관계로 판정한다.
    /// </summary>
    public sealed class TutorialFlexibleBeltPermission : MonoBehaviour
    {
        private static readonly System.Func<IReadOnlyList<Vector2Int>, bool> AllowPath = _ => true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main") return;
            new GameObject("[Choi] Flexible Tutorial Belt").AddComponent<TutorialFlexibleBeltPermission>();
        }

        private void Update()
        {
            ChoiFactoryTutorialController tutorial = FindFirstObjectByType<ChoiFactoryTutorialController>();
            if (tutorial != null && tutorial.enabled)
            {
                BeltDragTool.PathPermission = AllowPath;
                return;
            }

            if (BeltDragTool.PathPermission == AllowPath) BeltDragTool.PathPermission = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (BeltDragTool.PathPermission == AllowPath) BeltDragTool.PathPermission = null;
        }
    }
}
