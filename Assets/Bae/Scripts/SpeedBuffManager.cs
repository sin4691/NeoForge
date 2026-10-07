using System.Collections;
using UnityEngine;

namespace Bae
{
    public class SpeedBuffManager : MonoBehaviour
    {
        public static SpeedBuffManager Instance;

        [Header("Status (Read Only)")]
        public float buffTimeRemaining = 0f;
        public bool isBuffActive = false;
        
        // 외부(예: SimulationDriver)에서 이 값을 가져다 씁니다.
        public float CurrentSpeedMultiplier { get; private set; } = 1f;

        private Coroutine buffCoroutine;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        // 광고 연동 코드에서 호출할 함수 (예: AdManager의 콜백)
        public void ActivateSpeedBuff(float durationInSeconds = 60f, float speedMultiplier = 2f)
        {
            // 기존에 버프가 진행중이었다면 타이머 리셋
            if (buffCoroutine != null)
            {
                StopCoroutine(buffCoroutine);
            }

            buffCoroutine = StartCoroutine(SpeedBuffRoutine(durationInSeconds, speedMultiplier));
        }

        private IEnumerator SpeedBuffRoutine(float duration, float multiplier)
        {
            isBuffActive = true;
            buffTimeRemaining = duration;
            
            // 배속 적용
            CurrentSpeedMultiplier = multiplier;
            Debug.Log($"버프 시작: {multiplier}배속 ({duration}초)");

            // 실제 시간(1배속) 기준으로 남은 시간 차감
            while (buffTimeRemaining > 0)
            {
                buffTimeRemaining -= Time.deltaTime;
                yield return null; 
            }

            // 버프 종료 시 원상복구
            DeactivateBuff();
        }

        public void DeactivateBuff()
        {
            if (buffCoroutine != null)
            {
                StopCoroutine(buffCoroutine);
                buffCoroutine = null;
            }

            CurrentSpeedMultiplier = 1f;
            buffTimeRemaining = 0;
            isBuffActive = false;
            Debug.Log("버프 종료: 원래 속도로 복구");
        }
    }
}
