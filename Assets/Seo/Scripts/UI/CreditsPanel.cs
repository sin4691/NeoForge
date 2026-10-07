using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Seo.UI
{
    public sealed class CreditsPanel : MonoBehaviour
    {
        private const float ScrollSpeed = 80f;
        private static CreditsPanel instance;
        private static int lastClosedFrame = -1;
        private readonly List<RectTransform> sections = new List<RectTransform>();
        private RectTransform viewport;
        private CanvasGroup fade;
        private Button closeButton;
        private GameObject previousSelection;
        private Action onFinished;
        private float elapsed;

        // Placeholder copy only; fill in the actual participants and assets later.
        private static readonly string[] Copy =
        {
            "<size=56>CREDITS</size>\n\n[NEOFORGE]\nTEAM [백 투게더]",
            "<size=36>TEAM</size>\n\nProject Manager / System Programmer | [배창현]\nLead Programmer | [신재윤]\nSub Programmer | [최태희]\nUI Programmer | [서동연]",
            "<size=36>에셋 정보</size>\n\n[SCI-FI UI Pack Pro]\n[OZEA_STUDIO_ULTIMATE]",
            "<size=36>SPECIAL THANKS</size>\n\n[링크즈] [이부현 대표님]\n[디벨로켓] [메타프로그래밍13기]\n[김인성] 강사님\n테스트에 참여해 준 모든 분들",
            "COPYRIGHT 2026.\nTEAM [백 투게더]\nALL RIGHTS RESERVED."
        };

        public static CreditsPanel Show(Action finished = null)
        {
            if (instance != null && instance.gameObject.activeSelf) return instance;
            if (instance == null)
            {
                var root = new GameObject("CreditsCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                instance = root.AddComponent<CreditsPanel>();
                instance.Build();
            }
            instance.previousSelection = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            instance.onFinished = finished;
            instance.gameObject.SetActive(true);
            instance.elapsed = 0f;
            instance.fade.alpha = 0f;
            Canvas.ForceUpdateCanvases();
            float top = -instance.viewport.rect.height;
            for (int i = 0; i < instance.sections.Count; i++)
            {
                var rect = instance.sections[i];
                float height = rect.GetComponent<TMP_Text>().GetPreferredValues(Copy[i], rect.rect.width, 0f).y + 40f;
                rect.sizeDelta = new Vector2(-160f, height);
                // Stack later sections below the first so they enter from the bottom in order.
                rect.anchoredPosition = new Vector2(0f, top);
                top -= height + 180f;
            }
            instance.closeButton.Select();
            return instance;
        }

        private void Build()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = SeoUIFactory.LandscapeReference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = SeoUIFactory.CreatePanel(transform, "BlackBackground", Vector2.zero,
                Vector2.one, Vector2.zero, Vector2.zero, Color.black);
            background.sprite = null;
            viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(transform, false);
            SeoUIFactory.SetRect(viewport, Vector2.zero, Vector2.one, Vector2.one * .5f,
                new Vector2(0f, 80f), new Vector2(0f, -160f));
            fade = viewport.gameObject.AddComponent<CanvasGroup>();
            foreach (string copy in Copy)
            {
                var text = SeoUIFactory.CreateTMPText(viewport, "CreditSection", copy, 28, TextAnchor.UpperCenter);
                text.color = Color.white;
                text.lineSpacing = 18f;
                SeoUIFactory.SetRect(text.rectTransform, new Vector2(0f, 1f), Vector2.one,
                    new Vector2(.5f, 1f), Vector2.zero, new Vector2(-160f, 400f));
                sections.Add(text.rectTransform);
            }
            closeButton = SeoUIFactory.CreateTMPButton(transform, "CloseCredits", "닫기 · ESC", Close,
                new Color(.12f, .12f, .12f));
            SeoUIFactory.SetRect((RectTransform)closeButton.transform, new Vector2(.5f, 0f),
                new Vector2(.5f, 0f), Vector2.one * .5f, new Vector2(0f, 80f), new Vector2(360f, 100f));
            closeButton.GetComponentInChildren<TMP_Text>().fontSize = 30f;
            closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                TryHandleBack();
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            fade.alpha = Mathf.Clamp01(elapsed / 1.5f);
            if (elapsed < 2f) return;
            foreach (var rect in sections)
                rect.anchoredPosition += Vector2.up * (ScrollSpeed * Time.unscaledDeltaTime);
            var lastSection = sections[sections.Count - 1];
            if (lastSection.anchoredPosition.y - lastSection.rect.height > 80f) Close();
        }

        public static bool TryHandleBack()
        {
            if (instance != null && instance.gameObject.activeSelf)
            {
                instance.Close();
                return true;
            }
            return lastClosedFrame == Time.frameCount;
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            lastClosedFrame = Time.frameCount;
            gameObject.SetActive(false);
            if (previousSelection != null && previousSelection.activeInHierarchy && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection);
            previousSelection = null;
            var finished = onFinished;
            onFinished = null;
            finished?.Invoke();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
