using UnityEngine;

namespace Seo.UI
{
    // 모든 UI 패널이 공유하는 최소 수명주기. UIManager는 구체 패널의 내부 구현을
    // 알 필요 없이 Open/Close만 호출한다.
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        private CanvasGroup canvasGroup;

        public bool IsOpen { get; private set; }

        protected virtual void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            Close();
        }

        public virtual void Open()
        {
            IsOpen = true;
            gameObject.SetActive(true);
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        public virtual void Close()
        {
            IsOpen = false;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }
    }
}
