using Bae.Data;

namespace Seo.UI
{
    // PowerGridSystem의 소비 규칙을 UI에서 읽기 위한 Seo 전용 표시 어댑터.
    // 팀 전력 코드가 공개 조회 API를 제공하게 되면 이 클래스의 구현만 교체하면 된다.
    public static class MachinePowerDisplay
    {
        public static int GetConsumption(string machineKey)
        {
            if (machineKey == "Splitter" || machineKey == "Merger") return 0;

            if (DataManager.Instance != null
                && DataManager.Instance.machineDict.TryGetValue(machineKey, out var data)
                && data.powerConsumption > 0)
            {
                return data.powerConsumption;
            }

            switch (machineKey)
            {
                case "Miner": return 20;
                case "Smelter": return 30;
                case "Former": return 25;
                case "Synthesizer": return 50;
                default: return 0;
            }
        }

        public static string Format(string machineKey)
        {
            int consumption = GetConsumption(machineKey);
            return consumption > 0 ? $"전력 소비 · {consumption} MW" : "전력 소비 · 없음";
        }
    }
}
