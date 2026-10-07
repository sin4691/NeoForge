using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Choi.SaveLoad
{
    /// <summary>발전기/전선/송전탑 배치와 공장 전체 SAVE/LOAD를 확인하는 런타임 패널입니다.</summary>
    public sealed class FactoryPowerPanel : MonoBehaviour
    {
        private PowerSaveManager saveManager;
        private PowerGridSystem powerGrid;
        private PowerBuildController powerBuild;
        private Text statusText;
        private Image overloadLight;
        private Text overloadLabel;
        private GameObject panelObject;
        private Text toggleLabel;
        private string saveMessage = "저장 준비됨";
        private readonly Dictionary<PowerBuildMode, Button> modeButtons = new Dictionary<PowerBuildMode, Button>();
        private static readonly Color NormalButtonColor = new Color(0.11f, 0.43f, 0.68f, 1f);

        private void Start()
        {
            saveManager = GetComponent<PowerSaveManager>();
            powerGrid = GetComponent<PowerGridSystem>();
            powerBuild = GetComponent<PowerBuildController>();
            BuildPanel();
        }

        private void Update()
        {
            if (statusText == null || powerGrid == null || powerBuild == null) return;
            bool overloaded = powerGrid.IsBlackout;
            if (overloadLight != null)
            {
                float pulse = overloaded ? 0.65f + Mathf.PingPong(Time.unscaledTime * 0.7f, 0.35f) : 0.22f;
                overloadLight.color = overloaded
                    ? new Color(1f, 0.08f, 0.04f, pulse)
                    : new Color(0.18f, 0.3f, 0.34f, 0.65f);
            }
            if (overloadLabel != null)
            {
                overloadLabel.text = overloaded ? "OVERLOAD" : "NORMAL";
                overloadLabel.color = overloaded ? new Color(1f, 0.18f, 0.12f) : new Color(0.45f, 0.9f, 0.65f);
            }
            UpdateModeButtonHighlights();

            string usedPower = overloaded
                ? $"<color=#FF3028>{powerGrid.RequestedPower}</color>"
                : powerGrid.RequestedPower.ToString();
            statusText.text =
                $"총 전력량 {powerGrid.AvailablePower} / 사용 전력량 {usedPower}\n" +
                $"가동 기계 {powerGrid.PoweredMachineCount}/{powerGrid.TotalMachineCount} · 작동 송전탑 {powerGrid.ActiveTowerCount}\n" +
                $"모드: {ModeLabel(powerBuild.Mode)} · {powerBuild.LastMessage}\n{saveMessage}";
        }

        private void BuildPanel()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[FactoryPowerPanel] HUD Canvas를 찾지 못했습니다.");
                return;
            }

            Transform existing = canvas.transform.Find("FactoryPowerPanel");
            if (existing != null) Destroy(existing.gameObject);
            Transform existingToggle = canvas.transform.Find("FactoryPowerPanelToggle");
            if (existingToggle != null) Destroy(existingToggle.gameObject);

            var panel = new GameObject("FactoryPowerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject = panel;
            panel.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-18f, -66f);
            panelRect.sizeDelta = new Vector2(360f, 390f);
            panel.GetComponent<Image>().color = new Color(0.035f, 0.07f, 0.11f, 0.94f);

            CreateText(panel.transform, "Title", "POWER & FACTORY SAVE", new Vector2(0f, -15f), new Vector2(330f, 35f), 23);
            CreateOverloadIndicator(panel.transform);
            statusText = CreateText(panel.transform, "Status", string.Empty, new Vector2(0f, -52f), new Vector2(330f, 100f), 17);
            statusText.supportRichText = true;

            modeButtons[PowerBuildMode.Generator] = CreateButton(panel.transform, "GeneratorButton", "발전기 배치", new Vector2(-88f, -160f),
                () => powerBuild.ToggleMode(PowerBuildMode.Generator));
            modeButtons[PowerBuildMode.Cable] = CreateButton(panel.transform, "CableButton", "전선 배치", new Vector2(88f, -160f),
                () => powerBuild.ToggleMode(PowerBuildMode.Cable));
            modeButtons[PowerBuildMode.TransmissionTower] = CreateButton(panel.transform, "TowerButton", "송전탑 배치", new Vector2(-88f, -208f),
                () => powerBuild.ToggleMode(PowerBuildMode.TransmissionTower));
            modeButtons[PowerBuildMode.Remove] = CreateButton(panel.transform, "RemovePowerButton", "전력 철거", new Vector2(88f, -208f),
                () => powerBuild.ToggleMode(PowerBuildMode.Remove));
            CreateButton(panel.transform, "CancelPowerButton", "배치 종료", new Vector2(0f, -256f),
                () => powerBuild.SetMode(PowerBuildMode.None));
            CreateButton(panel.transform, "SaveFactoryButton", "SAVE", new Vector2(-88f, -316f), SaveFactory);
            CreateButton(panel.transform, "LoadFactoryButton", "LOAD", new Vector2(88f, -316f), LoadFactory);
            CreatePanelToggle(canvas.transform);
        }

        private void CreateOverloadIndicator(Transform parent)
        {
            var lightObject = new GameObject("OverloadLight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lightObject.transform.SetParent(parent, false);
            RectTransform lightRect = lightObject.GetComponent<RectTransform>();
            lightRect.anchorMin = new Vector2(0.5f, 1f);
            lightRect.anchorMax = new Vector2(0.5f, 1f);
            lightRect.pivot = new Vector2(0.5f, 0.5f);
            lightRect.anchoredPosition = new Vector2(-57f, -45f);
            lightRect.sizeDelta = new Vector2(16f, 16f);
            overloadLight = lightObject.GetComponent<Image>();
            overloadLight.raycastTarget = false;

            overloadLabel = CreateText(parent, "OverloadLabel", "NORMAL", new Vector2(14f, -34f), new Vector2(105f, 24f), 14);
            overloadLabel.alignment = TextAnchor.MiddleLeft;
        }

        private void CreatePanelToggle(Transform canvas)
        {
            var toggleObject = new GameObject("FactoryPowerPanelToggle", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            toggleObject.transform.SetParent(canvas, false);

            RectTransform rect = toggleObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-18f, -18f);
            rect.sizeDelta = new Vector2(170f, 40f);

            Image image = toggleObject.GetComponent<Image>();
            image.color = new Color(0.15f, 0.34f, 0.5f, 1f);
            Button button = toggleObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(TogglePanel);

            toggleLabel = CreateText(toggleObject.transform, "Label", "전력 UI 닫기", Vector2.zero, Vector2.zero, 17);
            RectTransform textRect = toggleLabel.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private void TogglePanel()
        {
            if (panelObject == null) return;
            bool show = !panelObject.activeSelf;
            panelObject.SetActive(show);
            if (toggleLabel != null) toggleLabel.text = show ? "전력 UI 닫기" : "전력 UI 열기";
        }

        private void SaveFactory()
        {
            if (saveManager == null) return;
            saveManager.Save();
            saveMessage = "공장 전체 SAVE 완료";
        }

        private void LoadFactory()
        {
            if (saveManager == null) return;
            saveMessage = saveManager.Load() ? "공장 전체 LOAD 완료" : "저장 파일이 없습니다";
        }

        private static string ModeLabel(PowerBuildMode mode)
        {
            switch (mode)
            {
                case PowerBuildMode.Generator: return "발전기";
                case PowerBuildMode.Cable: return "전선";
                case PowerBuildMode.TransmissionTower: return "송전탑";
                case PowerBuildMode.Remove: return "전력 철거";
                default: return "없음";
            }
        }

        private void UpdateModeButtonHighlights()
        {
            foreach (var pair in modeButtons)
            {
                if (pair.Value == null) continue;
                bool selected = powerBuild.Mode == pair.Key;
                Image image = pair.Value.GetComponent<Image>();
                if (image != null)
                {
                    image.color = selected
                        ? Color.Lerp(new Color(0.1f, 0.9f, 1f), Color.white, 0.78f)
                        : NormalButtonColor;
                }
                pair.Value.transform.localScale = selected ? Vector3.one * 1.06f : Vector3.one;
            }
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position,
            UnityEngine.Events.UnityAction action)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(160f, 40f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.11f, 0.43f, 0.68f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            Text text = CreateText(buttonObject.transform, "Label", label, Vector2.zero, Vector2.zero, 18);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static Text CreateText(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }
    }
}
