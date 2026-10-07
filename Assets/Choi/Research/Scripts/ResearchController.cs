using System;
using System.Collections.Generic;
using Choi.SaveLoad;
using Factory.Building;
using Factory.Simulation;
using Optimization;
using Seo.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Choi.Research
{
    public sealed class ResearchController : MonoBehaviour, IPowerSaveParticipant
    {
        public static ResearchController Instance { get; private set; }

        [Header("씬에서 수정 가능한 패널")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text goalsText;
        [SerializeField] private TMP_Text unlocksText;
        [SerializeField] private Button supplyButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform tierTabRoot;
        [SerializeField] private Transform rewardRoot;
        [SerializeField] private Button tierTabPrefab;
        [SerializeField] private GameObject rewardCardPrefab;
        [Header("티어 데이터")]
        [SerializeField] private List<ResearchTierAsset> tiers = new List<ResearchTierAsset>();
        [SerializeField] private int completedTier;
        [SerializeField] private List<int> suppliedAmounts = new List<int>();

        private SimulationDriver driver;
        private FloorSystemManager floor;
        private int viewedTier;
        private readonly List<Button> tierTabs = new List<Button>();
        private readonly List<GameObject> rewardCards = new List<GameObject>();
        private GameObject inputBlocker;
        private GameObject endingRoot;
        private Button continueButton;
        private CreditsPanel endingCredits;
        private Coroutine endingTransition;
        private CanvasGroup backgroundHud;
        private bool backgroundWasInteractable;
        private GameObject previousSelection;
        private readonly List<Behaviour> blockedInputs = new List<Behaviour>();
        public bool IsOpen => (panelRoot != null && panelRoot.activeInHierarchy)
            || (endingRoot != null && endingRoot.activeInHierarchy);
        public int CompletedTier => completedTier;
        public event Action UnlocksChanged;

        public string SaveId => "research-progress";
        public string SaveType => "Choi.ResearchProgress.v1";
        public int SaveOrder => 100; // 공장과 코어 재고를 복원한 뒤 연구 해금을 갱신한다.
        public bool CanSave => true;

        [Serializable]
        private sealed class ResearchProgressData
        {
            public int completedTier;
            public List<int> suppliedAmounts = new List<int>();
            public int unlockedMapSize;
        }

        public string CaptureStateJson()
        {
            Resolve();
            FloorChunkManager chunks = FindFirstObjectByType<FloorChunkManager>();
            return JsonUtility.ToJson(new ResearchProgressData
            {
                completedTier = completedTier,
                suppliedAmounts = new List<int>(suppliedAmounts),
                unlockedMapSize = floor != null ? floor.unlockedSize : chunks != null ? chunks.unlockedSize : 0,
            });
        }

        public void RestoreStateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Research save JSON is empty.", nameof(json));

            ResearchProgressData data = JsonUtility.FromJson<ResearchProgressData>(json);
            if (data == null) throw new InvalidOperationException("Research save data is invalid.");

            // 불러온 진행도 위에 이전 엔딩이 남지 않도록 닫는다. 완료 여부는 completedTier로 저장된다.
            if (endingRoot != null && endingRoot.activeSelf) Close();
            completedTier = Mathf.Clamp(data.completedTier, 0, tiers.Count);
            suppliedAmounts = data.suppliedAmounts ?? new List<int>();
            EnsureProgressSize(completedTier < tiers.Count ? tiers[completedTier].resourceGoals.Count : 0);
            for (int i = 0; i < suppliedAmounts.Count; i++)
                suppliedAmounts[i] = Mathf.Clamp(suppliedAmounts[i], 0, tiers[completedTier].resourceGoals[i].amount);

            Resolve();
            // 이전 저장을 불러오면 지도도 그 시점의 해금 범위로 되돌린다.
            int mapSize = data.unlockedMapSize;
            for (int i = 0; i < completedTier; i++)
                mapSize = Mathf.Max(mapSize, tiers[i].unlockedMapSize);
            if (mapSize > 0)
            {
                floor?.SetUnlockedSize(mapSize);
                FloorChunkManager chunks = FindFirstObjectByType<FloorChunkManager>();
                if (chunks != null)
                {
                    chunks.unlockedSize = mapSize;
                    chunks.RefreshAllActiveChunks();
                }
            }

            viewedTier = Mathf.Clamp(completedTier, 0, Mathf.Max(0, tiers.Count - 1));
            Refresh();
            UnlocksChanged?.Invoke();
        }

        private void Awake()
        {
            Instance = this;
            panelRoot?.SetActive(false);
            supplyButton?.onClick.AddListener(SupplyFromCore);
            closeButton?.onClick.AddListener(Close);
            BuildTierTabs();
        }

        private void OnDisable() => Close();

        private void OnDestroy()
        {
            Close();
            if (inputBlocker != null) Destroy(inputBlocker);
            if (endingRoot != null) Destroy(endingRoot);
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            // Also release input if another system hides the research panel directly.
            if (!IsOpen && inputBlocker != null && inputBlocker.activeSelf) Close();
        }

        public void Open()
        {
            if (panelRoot == null || IsOpen) return;
            OpenModal(panelRoot, closeButton);
            Refresh();
        }

        private void OpenModal(GameObject root, Button initialSelection)
        {
            if (inputBlocker == null)
            {
                inputBlocker = new GameObject("ResearchInputBlocker", typeof(RectTransform), typeof(Image));
                inputBlocker.transform.SetParent(panelRoot.transform.parent, false);
                var rect = inputBlocker.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var image = inputBlocker.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.45f);
                image.raycastTarget = true;
            }

            inputBlocker.transform.SetAsLastSibling();
            root.transform.SetAsLastSibling();
            inputBlocker.SetActive(true);
            root.SetActive(true);

            var hud = GameObject.Find("HUDCanvas");
            if (hud != null)
            {
                if (!hud.TryGetComponent<CanvasGroup>(out backgroundHud))
                    backgroundHud = hud.AddComponent<CanvasGroup>();
                backgroundWasInteractable = backgroundHud.interactable;
                backgroundHud.interactable = false;
            }
            foreach (var router in FindObjectsByType<BuildInputRouter>(FindObjectsSortMode.None)) BlockInput(router);
            foreach (var tap in FindObjectsByType<TapInputManager>(FindObjectsSortMode.None)) BlockInput(tap);

            if (EventSystem.current != null)
            {
                previousSelection = EventSystem.current.currentSelectedGameObject;
                EventSystem.current.SetSelectedGameObject(initialSelection != null ? initialSelection.gameObject : null);
            }
        }

        private void BlockInput(Behaviour input)
        {
            if (!input.enabled) return;
            blockedInputs.Add(input);
            input.enabled = false;
        }

        public void Close()
        {
            if (endingTransition != null) StopCoroutine(endingTransition);
            endingTransition = null;
            var credits = endingCredits;
            endingCredits = null;
            if (credits != null) credits.Close();
            panelRoot?.SetActive(false);
            endingRoot?.SetActive(false);
            if (inputBlocker != null) inputBlocker.SetActive(false);
            if (backgroundHud != null)
            {
                backgroundHud.interactable = backgroundWasInteractable;
                backgroundHud = null;
            }
            foreach (var input in blockedInputs)
                if (input != null) input.enabled = true;
            blockedInputs.Clear();
            if (previousSelection != null && previousSelection.activeInHierarchy && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection);
            previousSelection = null;
        }

        // 튜토리얼 마지막 단계에서는 연구소 화면을 보여 준 채 배경과 완료 버튼도 조작할 수 있게 한다.
        public void ReleaseModalInputForTutorial()
        {
            if (inputBlocker != null) inputBlocker.SetActive(false);
            if (backgroundHud != null)
            {
                backgroundHud.interactable = true;
                backgroundHud = null;
            }
            foreach (var input in blockedInputs)
                if (input != null) input.enabled = true;
            blockedInputs.Clear();
        }

        public bool IsMachineUnlocked(string id) => IsUnlocked(id, false);
        public bool IsRecipeUnlocked(string id) => IsUnlocked(id, true);

        private bool IsUnlocked(string id, bool recipe)
        {
            if (string.IsNullOrEmpty(id)) return true;
            bool gated = false;
            for (int i = 0; i < tiers.Count; i++)
            {
                if (recipe)
                {
                    for (int n = 0; n < tiers[i].recipeRewards.Count; n++)
                        if (tiers[i].recipeRewards[n] != null && tiers[i].recipeRewards[n].recipeID == id) { gated = true; if (i < completedTier) return true; }
                }
                else
                {
                    for (int n = 0; n < tiers[i].machineRewards.Count; n++)
                        if (tiers[i].machineRewards[n]?.machine != null && tiers[i].machineRewards[n].machine.machineID == id) { gated = true; if (i < completedTier) return true; }
                }
            }
            return !gated;
        }

        public void SupplyFromCore()
        {
            Resolve();
            if (completedTier >= tiers.Count || driver == null || driver.World == null) return;
            var tier = tiers[completedTier];
            EnsureProgressSize(tier.resourceGoals.Count);
            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count) return;
            var core = driver.World.Processors[coreIndex];

            for (int i = 0; i < tier.resourceGoals.Count; i++)
            {
                var goal = tier.resourceGoals[i];
                if (!driver.World.Database.TryGetResourceId(goal.resourceId, out int resourceId)) continue;
                int remaining = Mathf.Max(0, goal.amount - suppliedAmounts[i]);
                int moved = Mathf.Min(remaining, core.InputBuffer[resourceId]);
                core.InputBuffer[resourceId] -= moved;
                suppliedAmounts[i] += moved;
            }

            bool complete = true;
            for (int i = 0; i < tier.resourceGoals.Count; i++)
                complete &= suppliedAmounts[i] >= tier.resourceGoals[i].amount;
            if (complete)
            {
                completedTier++;
                suppliedAmounts.Clear();
                ApplyTerrainUnlock(tier.unlockedMapSize);
                UnlocksChanged?.Invoke();
                // 완료한 탭에 남아 빈 버튼처럼 보이지 않고 바로 다음 연구를 보여준다.
                viewedTier = Mathf.Min(completedTier, tiers.Count - 1);
            }
            Refresh();
            if (complete && tier.unlocksEnding) ShowEnding();
        }

        private void ShowEnding()
        {
            if (panelRoot == null) return;
            Close();
            if (endingRoot == null)
            {
                endingRoot = SeoUIFactory.CreatePanel(panelRoot.transform.parent, "ResearchEnding",
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    new Color(.025f, .045f, .065f, 1f)).gameObject;

                CreateEndingText("Chapter", "TIER 4 · 최종 연구 완료", 24, .73f, .83f,
                    new Color(.35f, .9f, .95f));
                CreateEndingText("Title", "자동화의 완성", 52, .55f, .72f, Color.white);
                CreateEndingText("Message",
                    "회로 · 탄소 나노 튜브 · 플라즈마 코어\n각각 10,000개 납품 완료\n\n당신의 공장은 마침내 모든 연구를 완성했습니다.\n플레이해 주셔서 감사합니다.",
                    26, .27f, .54f, new Color(.8f, .86f, .92f));
                continueButton = SeoUIFactory.CreateTMPButton(endingRoot.transform, "ContinueButton",
                    "크레딧 보기", BeginCredits, new Color(.08f, .45f, .52f));
                SeoUIFactory.SetRect((RectTransform)continueButton.transform,
                    new Vector2(.35f, .12f), new Vector2(.65f, .21f),
                    Vector2.one * .5f, Vector2.zero, Vector2.zero);
            }
            OpenModal(endingRoot, continueButton);
            endingTransition = StartCoroutine(ShowCreditsAfterEnding());
        }

        private System.Collections.IEnumerator ShowCreditsAfterEnding()
        {
            yield return new WaitForSecondsRealtime(6f);
            endingTransition = null;
            BeginCredits();
        }

        private void BeginCredits()
        {
            if (endingCredits != null) return;
            if (endingTransition != null) StopCoroutine(endingTransition);
            endingTransition = null;
            // Keep the ending modal's gameplay input lock until credits have finished.
            endingCredits = CreditsPanel.Show(() => { endingCredits = null; Close(); });
        }

        private void CreateEndingText(string name, string value, int fontSize, float bottom, float top, Color color)
        {
            TMP_Text text = SeoUIFactory.CreateTMPText(endingRoot.transform, name, value,
                fontSize, TextAnchor.MiddleCenter);
            SeoUIFactory.SetRect(text.rectTransform, new Vector2(.08f, bottom), new Vector2(.92f, top),
                Vector2.one * .5f, Vector2.zero, Vector2.zero);
            text.color = color;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12;
            text.fontSizeMax = fontSize;
        }

        private void Refresh()
        {
            Resolve();
            if (tiers.Count == 0) return;
            viewedTier = Mathf.Clamp(viewedTier, 0, tiers.Count - 1);
            var tier = tiers[viewedTier];
            if (viewedTier == completedTier) EnsureProgressSize(tier.resourceGoals.Count);
            if (titleText != null) titleText.text = $"TIER {tier.tier}  {tier.displayName}";
            if (descriptionText != null) descriptionText.text = tier.description;
            var lines = new List<string>();
            for (int i = 0; i < tier.resourceGoals.Count; i++)
                lines.Add($"{DisplayName(tier.resourceGoals[i].resourceId)}   {(viewedTier < completedTier ? tier.resourceGoals[i].amount : viewedTier == completedTier ? suppliedAmounts[i] : 0)} / {tier.resourceGoals[i].amount}");
            if (goalsText != null) goalsText.text = string.Join("\n", lines);
            if (unlocksText != null) unlocksText.text = viewedTier < completedTier
                ? (tier.unlocksEnding ? "최종 연구 완료 · 엔딩 해금" : "해금 완료")
                : viewedTier > completedTier ? "선행 티어를 먼저 해금하세요"
                : tier.unlocksEnding ? "납품 완료 보상 · 엔딩" : "요구 자원을 납품해 해금하세요";
            if (supplyButton != null) supplyButton.gameObject.SetActive(viewedTier == completedTier);
            RefreshRewards(tier);
        }

        private void BuildTierTabs()
        {
            if (tierTabRoot == null || tierTabPrefab == null) return;
            tierTabPrefab.gameObject.SetActive(false);
            for (int i = 0; i < tiers.Count; i++)
            {
                int index = i;
                Button tab = Instantiate(tierTabPrefab, tierTabRoot);
                tab.gameObject.SetActive(true);
                // Awake에서는 연구 패널이 닫혀 있어 비활성 자식의 라벨도 찾는다.
                tab.GetComponentInChildren<TMP_Text>(true).text = $"TIER {tiers[i].tier}";
                tab.onClick.AddListener(() => { viewedTier = index; Refresh(); });
                tierTabs.Add(tab);
            }
        }

        private void RefreshRewards(ResearchTierAsset tier)
        {
            for (int i = 0; i < rewardCards.Count; i++) Destroy(rewardCards[i]);
            rewardCards.Clear();
            if (rewardRoot == null || rewardCardPrefab == null) return;
            for (int i = 0; i < tier.machineRewards.Count; i++)
            {
                ResearchMachineReward reward = tier.machineRewards[i];
                if (reward?.machine == null) continue;
                GameObject card = Instantiate(rewardCardPrefab, rewardRoot); card.SetActive(true);
                TMP_Text label = card.GetComponentInChildren<TMP_Text>(true); if (label != null) label.text = reward.machine.machineName;
                Image image = card.transform.Find("Icon")?.GetComponent<Image>(); if (image != null) { image.sprite = reward.icon; image.enabled = reward.icon != null; }
                rewardCards.Add(card);
            }
        }

        private string DisplayName(string id)
        {
            if (driver != null && driver.World != null && driver.World.Database.TryGetResourceId(id, out int index))
                return driver.World.Database.Resources[index].DisplayName;
            return id;
        }

        private void EnsureProgressSize(int count)
        {
            while (suppliedAmounts.Count < count) suppliedAmounts.Add(0);
            while (suppliedAmounts.Count > count) suppliedAmounts.RemoveAt(suppliedAmounts.Count - 1);
        }

        private void Resolve()
        {
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (floor == null) floor = FindFirstObjectByType<FloorSystemManager>();
        }

        private void ApplyTerrainUnlock(int requestedSize)
        {
            Resolve();
            if (floor != null)
            {
                // 잘못 설정된 티어 값 때문에 기존 땅이 줄어들지 않게 확장만 허용한다.
                floor.SetUnlockedSize(Mathf.Max(floor.unlockedSize, requestedSize));
            }

            // FloorSystemManager 초기화 순서와 관계없이 현재 로드된 타일을 즉시 다시 칠한다.
            FloorChunkManager chunks = FindFirstObjectByType<FloorChunkManager>();
            if (chunks != null)
            {
                chunks.unlockedSize = Mathf.Max(chunks.unlockedSize, requestedSize);
                chunks.RefreshAllActiveChunks();
            }
        }
    }
}
