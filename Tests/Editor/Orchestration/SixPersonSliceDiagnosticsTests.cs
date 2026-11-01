using System.Collections.Generic;
using LandLedgers.Orchestration.Player;
using NUnit.Framework;

/// <summary>
/// Pass XL vertical slice, Item 4: the six-person diagnostic line.
/// Plain C#, Roslyn-harness runnable. Proves the line carries every id
/// and every actual, and that an unknown value prints "unknown" rather
/// than a fake zero. The SimulationDrivers call site that produces this
/// line daily is Codex's wiring job (see SixPersonSliceDiagnostics doc).
/// </summary>
[TestFixture]
public sealed class SixPersonSliceDiagnosticsTests
{
    [Test]
    public void Format_CarriesAllIdsAndActuals()
    {
        var snapshot = new SixPersonSliceSnapshot
        {
            DayIndex = 13,
            PersonIds = new List<int> { 1, 2, 3, 4, 5, 6 },
            HouseholdIds = new List<int> { 1, 2, 3 },
            BusinessIds = new List<string> { "general-store-1", "bakery-1", "fuel-1" },
            LotIds = new List<string> { "lot-EXT-P-0000" },
            BusinessCashCents = 3750,
            HouseholdCashCents = 7000,
            MealsEaten = 11,
            MealsMissed = 1,
            TasksCompleted = 4,
            ShipmentsInTransit = 1,
        };

        string line = SixPersonSliceDiagnostics.Format(snapshot);

        Assert.IsTrue(line.Contains("day 13"));
        Assert.IsTrue(line.Contains("persons [1,2,3,4,5,6]"));
        Assert.IsTrue(line.Contains("households [1,2,3]"));
        Assert.IsTrue(line.Contains("businesses [general-store-1,bakery-1,fuel-1]"));
        Assert.IsTrue(line.Contains("lots [lot-EXT-P-0000]"));
        Assert.IsTrue(line.Contains("businessCash 3750c"));
        Assert.IsTrue(line.Contains("householdCash 7000c"));
        Assert.IsTrue(line.Contains("meals 11 eaten / 1 missed"));
        Assert.IsTrue(line.Contains("tasks 4 completed"));
        Assert.IsTrue(line.Contains("shipments 1 in transit"));
    }

    [Test]
    public void Format_UnknownValuesAreNotFakedAsZero()
    {
        var snapshot = new SixPersonSliceSnapshot { DayIndex = 0 };
        string line = SixPersonSliceDiagnostics.Format(snapshot);
        Assert.IsTrue(line.Contains("businessCash unknown"));
        Assert.IsTrue(line.Contains("meals unknown eaten / unknown missed"));
        Assert.IsFalse(line.Contains("businessCash 0c"));
    }

    [Test]
    public void Format_NullSnapshot_SaysSo()
    {
        Assert.IsTrue(SixPersonSliceDiagnostics.Format(null).Contains("no snapshot"));
    }
}
