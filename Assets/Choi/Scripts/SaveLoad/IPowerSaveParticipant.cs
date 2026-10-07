namespace Choi.SaveLoad
{
    /// <summary>
    /// JSON 저장에 참여하는 전력 시스템의 공통 계약입니다.
    /// 별도의 코드를 작성하지 않으려면 PowerSaveTarget을 전력 컴포넌트 옆에 붙이면 됩니다.
    /// </summary>
    public interface IPowerSaveParticipant
    {
        string SaveId { get; }
        string SaveType { get; }
        int SaveOrder { get; }
        bool CanSave { get; }

        string CaptureStateJson();
        void RestoreStateJson(string json);
    }
}
