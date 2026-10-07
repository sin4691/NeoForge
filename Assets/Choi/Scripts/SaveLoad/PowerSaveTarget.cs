using System;
using UnityEngine;

namespace Choi.SaveLoad
{
    /// <summary>
    /// 기존 발전기/전력망/소비기/배터리 MonoBehaviour를 수정하지 않고 저장 대상으로 만드는 어댑터입니다.
    /// 같은 GameObject에 추가한 뒤 target에 실제 상태 컴포넌트를 지정하세요.
    /// JsonUtility 규칙에 따라 public 필드와 [SerializeField] 필드가 저장됩니다.
    /// </summary>
    public sealed class PowerSaveTarget : MonoBehaviour, IPowerSaveParticipant
    {
        [SerializeField] private string saveId;
        [SerializeField] private MonoBehaviour target;
        [SerializeField] private int restoreOrder;

        public string SaveId => saveId;
        public string SaveType => target == null ? string.Empty : target.GetType().FullName;
        public int SaveOrder => restoreOrder;
        public bool CanSave => target != null && target != this && !string.IsNullOrWhiteSpace(saveId);

        public void Configure(MonoBehaviour newTarget, string stableSaveId = null, int order = 0)
        {
            if (newTarget == null || newTarget == this)
            {
                throw new ArgumentException("A separate MonoBehaviour target is required.", nameof(newTarget));
            }

            target = newTarget;
            restoreOrder = order;
            if (!string.IsNullOrWhiteSpace(stableSaveId)) saveId = stableSaveId;
            EnsureSaveId();
        }

        private void Awake()
        {
            EnsureSaveId();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureSaveId();
        }
#endif

        [ContextMenu("Regenerate Save ID")]
        public void RegenerateSaveId()
        {
            saveId = Guid.NewGuid().ToString("N");
        }

        public string CaptureStateJson()
        {
            if (!CanSave)
            {
                throw new InvalidOperationException($"PowerSaveTarget '{name}' has no valid target or save ID.");
            }

            return JsonUtility.ToJson(target);
        }

        public void RestoreStateJson(string json)
        {
            if (!CanSave)
            {
                throw new InvalidOperationException($"PowerSaveTarget '{name}' has no valid target or save ID.");
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Saved state JSON is empty.", nameof(json));
            }

            JsonUtility.FromJsonOverwrite(json, target);
        }

        private void EnsureSaveId()
        {
            if (string.IsNullOrWhiteSpace(saveId))
            {
                saveId = Guid.NewGuid().ToString("N");
            }
        }
    }
}
