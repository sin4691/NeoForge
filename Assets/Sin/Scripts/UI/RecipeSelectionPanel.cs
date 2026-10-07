using System.Collections.Generic;
using Factory.Data;
using Factory.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Factory.UI
{
    // 제련로/성형기/합성기를 탭했을 때 뜨는 레시피 선택 패널. 기계 종류에 맞는 레시피 버튼을 동적으로
    // 채우고, 고르면 그 프로세서 인스턴스에 RecipeId를 대입한다. 사용자가 설명한 대로 —
    // "레시피를 선택해서 지정하면 거기서 필요한 자원의 정보를 전달받는" 구조.
    public class RecipeSelectionPanel : MonoBehaviour
    {
        // 씬에 하나만 있는 패널이라 MachineView가 개별 배선 없이 바로 열 수 있게 static으로 노출.
        public static RecipeSelectionPanel Instance { get; private set; }

        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform buttonContainer;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private TMP_FontAsset fontAsset;

        private readonly List<GameObject> spawnedButtons = new List<GameObject>();
        private int targetProcessorIndex = -1;

        private void Awake()
        {
            Instance = this;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Open(int processorIndex, string machineId)
        {
            if (driver == null || driver.World == null) return;

            targetProcessorIndex = processorIndex;
            ClearButtons();

            var db = driver.World.Database;
            var recipeIds = db.GetRecipeIdsForMachine(machineId);
            for (int i = 0; i < recipeIds.Count; i++)
            {
                CreateRecipeButton(recipeIds[i], db.Recipes[recipeIds[i]].Key);
            }

            if (panelRoot != null) panelRoot.SetActive(true);
        }

        public void OpenGenerator(int processorIndex)
        {
            if (driver == null || driver.World == null || processorIndex < 0
                || processorIndex >= driver.World.Processors.Count) return;
            ProcessorInstance processor = driver.World.Processors[processorIndex];
            if (processor == null || !processor.IsGeneratorFuelPort) return;

            targetProcessorIndex = processorIndex;
            ClearButtons();
            CreateFuelButton(processor.CoalResourceId, "석탄");
            CreateFuelButton(processor.BatteryResourceId, "고용량 배터리");
            if (panelRoot != null) panelRoot.SetActive(true);
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            targetProcessorIndex = -1;
        }

        private void CreateRecipeButton(int recipeId, string label)
        {
            var go = new GameObject($"Recipe_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(buttonContainer, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 90f);
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 0.95f);

            var textGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textGO.transform.SetParent(go.transform, false);
            var rt = textGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = textGO.GetComponent<TextMeshProUGUI>();
            text.font = fontAsset != null ? fontAsset : TMP_Settings.defaultFontAsset;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = 28;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = label;

            go.GetComponent<Button>().onClick.AddListener(() => SelectRecipe(recipeId));
            spawnedButtons.Add(go);
        }

        private void CreateFuelButton(int resourceId, string label)
        {
            if (resourceId < 0) return;
            var go = new GameObject($"Fuel_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(buttonContainer, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 90f);
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 0.95f);

            var textGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textGO.transform.SetParent(go.transform, false);
            var rt = textGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var text = textGO.GetComponent<TextMeshProUGUI>();
            text.font = fontAsset != null ? fontAsset : TMP_Settings.defaultFontAsset;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = 28;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = label;

            go.GetComponent<Button>().onClick.AddListener(() => SelectFuel(resourceId));
            spawnedButtons.Add(go);
        }

        private void SelectFuel(int resourceId)
        {
            if (targetProcessorIndex >= 0 && targetProcessorIndex < driver.World.Processors.Count)
            {
                ProcessorInstance processor = driver.World.Processors[targetProcessorIndex];
                if (processor != null && processor.IsGeneratorFuelPort
                    && processor.SelectedFuelResourceId != resourceId)
                {
                    driver.World.FlushGeneratorFuel(targetProcessorIndex);
                    processor.SelectedFuelResourceId = resourceId;
                    processor.RecipeSetSequence = ProcessorInstance.NextRecipeSetSequence();
                }
            }
            Close();
        }

        private void SelectRecipe(int recipeId)
        {
            if (targetProcessorIndex >= 0 && targetProcessorIndex < driver.World.Processors.Count)
            {
                // 패널이 열려있는 동안 이 프로세서가 철거됐을 수 있다(SimulationWorld.RemoveProcessor
                // 참고 — 그 자리는 null로 비워짐).
                var processor = driver.World.Processors[targetProcessorIndex];
                if (processor != null && processor.RecipeId != recipeId)
                {
                    // 다른 레시피로 바꾸는 거면 안에 남아있던 재료/산출물을 코어로 비운다 —
                    // 안 그러면 옛 레시피 재료가 새 레시피랑 뒤섞이거나, 출력 벨트가 예전
                    // 산출물에 계속 묶여서 새 산출물을 영영 안 실어나른다(SimulationWorld.
                    // FlushProcessorBuffers 참고).
                    driver.World.FlushProcessorBuffers(targetProcessorIndex);
                    processor.RecipeId = recipeId;
                    processor.RecipeSetSequence = ProcessorInstance.NextRecipeSetSequence();
                }
            }
            Close();
        }

        private void ClearButtons()
        {
            for (int i = 0; i < spawnedButtons.Count; i++) Destroy(spawnedButtons[i]);
            spawnedButtons.Clear();
        }
    }
}
