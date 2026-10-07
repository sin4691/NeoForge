using UnityEngine;

namespace Seo.UI
{
    // 시뮬레이션 타입을 UI 컴포넌트에 그대로 노출하지 않기 위한 표시 전용 DTO.
    public readonly struct MachineInfoViewData
    {
        public readonly string Title;
        public readonly string Status;
        public readonly string Recipe;
        public readonly string Power;
        public readonly string Input;
        public readonly string Output;
        public readonly string Progress;
        public readonly string Ports;
        public readonly float Progress01;
        public readonly bool CanSelectRecipe;
        public readonly bool CanViewCoreResources;
        public readonly Color AccentColor;

        public MachineInfoViewData(
            string title,
            string status,
            string recipe,
            string power,
            string input,
            string output,
            string progress,
            string ports,
            float progress01,
            bool canSelectRecipe,
            Color accentColor,
            bool canViewCoreResources = false)
        {
            Title = title;
            Status = status;
            Recipe = recipe;
            Power = power;
            Input = input;
            Output = output;
            Progress = progress;
            Ports = ports;
            Progress01 = Mathf.Clamp01(progress01);
            CanSelectRecipe = canSelectRecipe;
            CanViewCoreResources = canViewCoreResources;
            AccentColor = accentColor;
        }
    }
}
