using Factory.Simulation;
using NUnit.Framework;

public sealed class FactoryStatisticsTests
{
    [Test]
    public void Rates_UseObservedTimeDuringWarmup()
    {
        var statistics = new FactoryStatistics(2);
        statistics.Advance(10f);
        statistics.RecordProduced(0, 5);
        statistics.RecordConsumed(1, 2);

        Assert.AreEqual(1800f, statistics.GetProducedPerHour(0), 0.01f);
        Assert.AreEqual(720f, statistics.GetConsumedPerHour(1), 0.01f);
    }

    [Test]
    public void Rates_DropEventsOlderThanSixtySeconds()
    {
        var statistics = new FactoryStatistics(1);
        statistics.RecordProduced(0, 10);
        statistics.Advance(61f);

        Assert.AreEqual(0f, statistics.GetProducedPerHour(0), 0.01f);
    }

    [Test]
    public void AveragePower_IsWeightedBySampleDuration()
    {
        var statistics = new FactoryStatistics(1);
        statistics.RecordPower(100f, 1f);
        statistics.RecordPower(200f, 3f);

        Assert.AreEqual(175f, statistics.GetAveragePower(), 0.01f);
    }
}
