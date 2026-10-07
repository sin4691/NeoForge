using System;
using System.Collections.Generic;
using Bae.SO;
using UnityEngine;

namespace Choi.Research
{
    [Serializable]
    public sealed class ResearchResourceGoal
    {
        public string resourceId;
        [Min(1)] public int amount = 100;
    }

    [Serializable]
    public sealed class ResearchMachineReward
    {
        [Tooltip("Assets/Bae/Data/Machines의 MachineSO")]
        public MachineSO machine;
        [Tooltip("연구 패널에 표시할 이미지. 비워두면 기본 아이콘을 사용합니다.")]
        public Sprite icon;
    }

    [CreateAssetMenu(menuName = "Factory/Research Tier", fileName = "ResearchTier")]
    public sealed class ResearchTierAsset : ScriptableObject
    {
        [Min(1)] public int tier = 1;
        public string displayName;
        [TextArea] public string description;
        [Min(1)] public int unlockedMapSize = 100;
        [Tooltip("이 티어의 납품을 완료하면 엔딩을 표시합니다.")]
        public bool unlocksEnding;
        public List<ResearchResourceGoal> resourceGoals = new List<ResearchResourceGoal>();
        public List<ResearchMachineReward> machineRewards = new List<ResearchMachineReward>();
        [Tooltip("Assets/Bae/Data/Recipes의 RecipeSO")]
        public List<RecipeSO> recipeRewards = new List<RecipeSO>();
    }
}
