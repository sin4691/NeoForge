using System.Reflection;
using Factory.Building;
using Factory.Buildings;
using Factory.UI;
using Seo.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Choi.Research
{
    public sealed class CoreResearchButtonBridge : MonoBehaviour
    {
        private Button researchButton;
        private float nextRefresh;

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            EnsureCoreButton();
            RefreshUnlockButtons();
        }

        private void EnsureCoreButton()
        {
            UIManager ui = UIManager.Instance;
            bool coreOpen = ui != null && ui.IsMachineInfoOpen && ui.SelectedKind == MachineInstanceKind.Processor
                && IsSelectedCore(ui);
            if (researchButton == null)
            {
                GameObject panel = GameObject.Find("MachineInfoPanel");
                Transform footer = panel != null ? panel.transform.Find("ActionFooter") : null;
                if (footer == null) return;
                researchButton = SeoUIFactory.CreateButton(footer, "ResearchButton", "연구소", () => ResearchController.Instance?.Open(), new Color(.13f, .48f, .58f, 1f));
                SeoUIFactory.SetRect(researchButton.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(246f, 20f), new Vector2(126f, 58f));
            }
            researchButton.gameObject.SetActive(coreOpen);
        }

        private static bool IsSelectedCore(UIManager ui)
        {
            var field = typeof(UIManager).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);
            var driver = field?.GetValue(ui) as Factory.Simulation.SimulationDriver;
            return driver != null && driver.World != null && ui.SelectedIndex == driver.World.CoreProcessorIndex;
        }

        private static void RefreshUnlockButtons()
        {
            var research = ResearchController.Instance;
            if (research == null) return;
            // 카테고리 창이 닫혀 비활성화된 버튼도 포함해야 다음에 창을 열 때 해금 상태가 보인다.
            foreach (var palette in Resources.FindObjectsOfTypeAll<BuildPaletteButton>())
            {
                if (palette == null || !palette.gameObject.scene.IsValid()) continue;
                var idField = typeof(BuildPaletteButton).GetField("machineId", BindingFlags.Instance | BindingFlags.NonPublic);
                string id = idField?.GetValue(palette) as string;
                Button button = palette.GetComponent<Button>();
                if (button != null && !string.IsNullOrEmpty(id))
                {
                    bool unlocked = research.IsMachineUnlocked(id);
                    SeoUIFactory.SetResearchLocked(button, !unlocked);
                }
            }
            // FactoryHudController.EnsureRuntimeMachineButton으로 늦게 추가된 팔레트 버튼(가공기,
            // 크로스벨트 등)은 BuildPaletteButton 컴포넌트가 없어서 위 스캔에 안 잡힌다 — 그래서
            // 연구를 하나도 안 깨도 처음부터 항상 눌리는 상태로 남았다(사용자 보고: 가공기가
            // 티어 3을 해금해야 열려야 하는데 처음부터 열려있음). 이런 버튼도 이름
            // ("PaletteButton_<machineId>", EnsureRuntimeMachineButton이 그 규칙으로 짓는다)에서
            // machineId를 바로 뽑아 같은 방식으로 잠근다. GameObject.Find는 카테고리 창이 닫혀
            // 비활성화된 버튼을 못 찾으므로(BuildPaletteButton 스캔과 같은 이유) 여기서도
            // Resources.FindObjectsOfTypeAll을 쓴다.
            const string palettePrefix = "PaletteButton_";
            foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button == null || !button.gameObject.scene.IsValid()) continue;
                if (button.GetComponent<BuildPaletteButton>() != null) continue; // 위에서 이미 처리함.
                if (!button.name.StartsWith(palettePrefix)) continue;
                string id = button.name.Substring(palettePrefix.Length);
                SeoUIFactory.SetResearchLocked(button, !research.IsMachineUnlocked(id));
            }

            var recipePanel = RecipeSelectionPanel.Instance;
            if (recipePanel == null) return;
            foreach (Button button in recipePanel.GetComponentsInChildren<Button>(true))
            {
                if (!button.name.StartsWith("Recipe_")) continue;
                bool unlocked = research.IsRecipeUnlocked(button.name.Substring(7));
                SeoUIFactory.SetResearchLocked(button, !unlocked);
            }
        }
    }
}
