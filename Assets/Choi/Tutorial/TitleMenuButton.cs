using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class TitleMenuButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler,
    IPointerDownHandler, IPointerUpHandler,
    IPointerClickHandler, ISubmitHandler
{
    public enum MenuAction
    {
        None,
        StartGame,
        QuitGame,
        Settings,
        Credits
    }

    [SerializeField] private Image indicator;
    [SerializeField] private Image underline;
    [SerializeField] private MenuAction action;
    [SerializeField] private string sceneName = "Main";
    [SerializeField, Min(0f)] private float confirmGlowDuration = 0.5f;

    private static readonly Color FocusColor = new(0.04f, 0.78f, 0.87f, 1f);
    private static readonly Color ConfirmColor = new(1f, 0.48f, 0.08f, 1f);

    private Color idleColor;
    private bool pointerOver;
    private bool selected;
    private bool confirming;

    private void Awake()
    {
        idleColor = indicator != null ? indicator.color : Color.gray;
        SetVisual(idleColor, false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerOver = true;
        EventSystem.current?.SetSelectedGameObject(gameObject);
        RefreshIndicator();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerOver = false;
        RefreshIndicator();
    }

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        RefreshIndicator();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        RefreshIndicator();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!confirming)
            SetVisual(ConfirmColor, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        RefreshIndicator();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Confirm();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (!confirming)
            StartCoroutine(ConfirmThenRun());
    }

    private IEnumerator ConfirmThenRun()
    {
        confirming = true;
        SetVisual(ConfirmColor, true);
        yield return new WaitForSecondsRealtime(confirmGlowDuration);

        switch (action)
        {
            case MenuAction.Credits:
                confirming = false;
                Seo.UI.CreditsPanel.Show();
                RefreshIndicator();
                break;
            case MenuAction.Settings:
                confirming = false;
                Seo.UI.BgmSettingsPanel.Show();
                RefreshIndicator();
                break;
            case MenuAction.StartGame:
                SceneManager.LoadScene(sceneName);
                break;
            case MenuAction.QuitGame:
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
            default:
                confirming = false;
                RefreshIndicator();
                break;
        }
    }

    private void RefreshIndicator()
    {
        if (!confirming)
        {
            var focused = pointerOver || selected;
            SetVisual(focused ? FocusColor : idleColor, focused);
        }
    }

    private void SetVisual(Color color, bool showUnderline)
    {
        if (indicator != null)
            indicator.color = color;

        if (underline != null)
        {
            underline.color = color;
            underline.gameObject.SetActive(showUnderline);
        }
    }
}
