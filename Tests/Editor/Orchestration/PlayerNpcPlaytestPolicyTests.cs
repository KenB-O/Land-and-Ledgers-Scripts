using LandLedgers.Orchestration.Player;
using NUnit.Framework;

/// <summary>
/// Pass XL vertical slice, Item 1: the player-as-NPC playtest policy rule.
/// Plain C#, Roslyn-harness runnable. The SimulationDrivers wiring of the
/// same flag is covered by the Unity-only
/// Tests/Editor/Orchestration/PassXlPlayerAsNpcTests.cs (GameObject /
/// AddComponent), which must be run in Unity Test Runner by Codex/Luna
/// and is NOT in the Roslyn harness csproj.
/// </summary>
[TestFixture]
public sealed class PlayerNpcPlaytestPolicyTests
{
    [Test]
    public void Default_IsOff_AllPlayerPathsAdvance()
    {
        var policy = new PlayerNpcPlaytestPolicy();
        Assert.IsFalse(policy.TreatPlayerAsNpc);
        Assert.IsTrue(policy.ShouldAdvancePlayerWork());
        Assert.IsTrue(policy.ShouldAdvancePlayerTravel());
        Assert.IsTrue(policy.ShouldCountPlayerOwnerLabour());
    }

    [Test]
    public void FlagOn_AllPlayerOnlyPathsStop()
    {
        var policy = new PlayerNpcPlaytestPolicy { TreatPlayerAsNpc = true };
        Assert.IsFalse(policy.ShouldAdvancePlayerWork());
        Assert.IsFalse(policy.ShouldAdvancePlayerTravel());
        Assert.IsFalse(policy.ShouldCountPlayerOwnerLabour());
    }

    [Test]
    public void FlagOffAgain_AllPlayerPathsResume()
    {
        var policy = new PlayerNpcPlaytestPolicy { TreatPlayerAsNpc = true };
        policy.TreatPlayerAsNpc = false;
        Assert.IsTrue(policy.ShouldAdvancePlayerWork());
        Assert.IsTrue(policy.ShouldAdvancePlayerTravel());
        Assert.IsTrue(policy.ShouldCountPlayerOwnerLabour());
    }
}
