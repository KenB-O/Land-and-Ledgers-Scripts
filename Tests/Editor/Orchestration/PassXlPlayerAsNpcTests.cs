using LandLedgers.Orchestration.Systems;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// UNITY ONLY - DO NOT ADD TO THE ROSLYN HARNESS CSPROJ.
/// SimulationDrivers is a MonoBehaviour and cannot be newed; this test
/// creates throwaway GameObjects and AddComponent&lt;SimulationDrivers&gt;(),
/// in the style of SimulationDriversSceneBindingTests, and proves the
/// serialized TreatPlayerAsNpc flag on the component gates the
/// ShouldAdvancePlayerWork rule in both modes. Run in Unity Test Runner
/// by Codex/Luna. It does not prove tick behaviour (no hub / TimeManager
/// is wired here); the tick gates are the three sites in
/// SimulationDrivers (AdvancePlayerWork, OnShortTick travel,
/// OnDayChanged / OnWeekChanged owner-labour accounting).
/// </summary>
[TestFixture]
public sealed class PassXlPlayerAsNpcTests
{
    [Test]
    public void SimulationDrivers_TreatPlayerAsNpc_DefaultOff_AdvancesPlayerWork()
    {
        var go = new GameObject("PassXlPlayerAsNpcProbe");
        var drivers = go.AddComponent<SimulationDrivers>();
        Assert.IsFalse(drivers.TreatPlayerAsNpc);
        Assert.IsTrue(drivers.ShouldAdvancePlayerWork());
        Object.DestroyImmediate(go);
    }

    [Test]
    public void SimulationDrivers_TreatPlayerAsNpc_On_StopsPlayerWorkAdvancement()
    {
        var go = new GameObject("PassXlPlayerAsNpcProbe");
        var drivers = go.AddComponent<SimulationDrivers>();
        drivers.TreatPlayerAsNpc = true;
        Assert.IsFalse(drivers.ShouldAdvancePlayerWork());
        drivers.TreatPlayerAsNpc = false;
        Assert.IsTrue(drivers.ShouldAdvancePlayerWork());
        Object.DestroyImmediate(go);
    }
}
