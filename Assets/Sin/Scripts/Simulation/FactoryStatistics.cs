using System;

namespace Factory.Simulation
{
    // 최근 60초 동안 실제로 완료된 생산/소비만 모은다. 버퍼 증감을 비교하지 않으므로
    // 벨트 운송, 코어 이동, 철거 환불은 생산량이나 소비량으로 잘못 집계되지 않는다.
    public sealed class FactoryStatistics
    {
        public const int WindowSeconds = 60;

        private readonly int resourceCount;
        private readonly int[,] producedBuckets;
        private readonly int[,] consumedBuckets;
        private readonly int[] producedInWindow;
        private readonly int[] consumedInWindow;
        private readonly float[] powerEnergyBuckets;
        private readonly float[] powerSecondsBuckets;
        private float powerEnergyInWindow;
        private float powerSecondsInWindow;
        private int currentBucket;
        private float bucketElapsed;
        private float observedSeconds;

        public float ObservedSeconds => Math.Min(observedSeconds, WindowSeconds);

        public FactoryStatistics(int resourceCount)
        {
            this.resourceCount = Math.Max(0, resourceCount);
            producedBuckets = new int[WindowSeconds, this.resourceCount];
            consumedBuckets = new int[WindowSeconds, this.resourceCount];
            producedInWindow = new int[this.resourceCount];
            consumedInWindow = new int[this.resourceCount];
            powerEnergyBuckets = new float[WindowSeconds];
            powerSecondsBuckets = new float[WindowSeconds];
        }

        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            observedSeconds += deltaSeconds;
            bucketElapsed += deltaSeconds;
            while (bucketElapsed >= 1f)
            {
                bucketElapsed -= 1f;
                currentBucket = (currentBucket + 1) % WindowSeconds;
                ClearBucket(currentBucket);
            }
        }

        public void RecordProduced(int resourceId, int amount)
        {
            if (!IsValid(resourceId, amount)) return;
            producedBuckets[currentBucket, resourceId] += amount;
            producedInWindow[resourceId] += amount;
        }

        public void RecordConsumed(int resourceId, int amount)
        {
            if (!IsValid(resourceId, amount)) return;
            consumedBuckets[currentBucket, resourceId] += amount;
            consumedInWindow[resourceId] += amount;
        }

        public void RecordPower(float usedPower, float deltaSeconds)
        {
            if (usedPower < 0f || deltaSeconds <= 0f) return;
            float energy = usedPower * deltaSeconds;
            powerEnergyBuckets[currentBucket] += energy;
            powerSecondsBuckets[currentBucket] += deltaSeconds;
            powerEnergyInWindow += energy;
            powerSecondsInWindow += deltaSeconds;
        }

        public float GetAveragePower()
        {
            return powerSecondsInWindow > 0f ? powerEnergyInWindow / powerSecondsInWindow : 0f;
        }

        public float GetProducedPerHour(int resourceId)
        {
            return RatePerHour(producedInWindow, resourceId);
        }

        public float GetConsumedPerHour(int resourceId)
        {
            return RatePerHour(consumedInWindow, resourceId);
        }

        public void Clear()
        {
            Array.Clear(producedBuckets, 0, producedBuckets.Length);
            Array.Clear(consumedBuckets, 0, consumedBuckets.Length);
            Array.Clear(producedInWindow, 0, producedInWindow.Length);
            Array.Clear(consumedInWindow, 0, consumedInWindow.Length);
            Array.Clear(powerEnergyBuckets, 0, powerEnergyBuckets.Length);
            Array.Clear(powerSecondsBuckets, 0, powerSecondsBuckets.Length);
            powerEnergyInWindow = 0f;
            powerSecondsInWindow = 0f;
            currentBucket = 0;
            bucketElapsed = 0f;
            observedSeconds = 0f;
        }

        private float RatePerHour(int[] totals, int resourceId)
        {
            if (resourceId < 0 || resourceId >= resourceCount || ObservedSeconds <= 0f) return 0f;
            return totals[resourceId] * 3600f / ObservedSeconds;
        }

        private bool IsValid(int resourceId, int amount)
        {
            return resourceId >= 0 && resourceId < resourceCount && amount > 0;
        }

        private void ClearBucket(int bucket)
        {
            powerEnergyInWindow -= powerEnergyBuckets[bucket];
            powerSecondsInWindow -= powerSecondsBuckets[bucket];
            powerEnergyBuckets[bucket] = 0f;
            powerSecondsBuckets[bucket] = 0f;
            for (int resourceId = 0; resourceId < resourceCount; resourceId++)
            {
                producedInWindow[resourceId] -= producedBuckets[bucket, resourceId];
                consumedInWindow[resourceId] -= consumedBuckets[bucket, resourceId];
                producedBuckets[bucket, resourceId] = 0;
                consumedBuckets[bucket, resourceId] = 0;
            }
        }
    }
}
