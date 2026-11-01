using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

/// <summary>
/// UNITY ONLY - DO NOT ADD TO THE ROSLYN HARNESS CSPROJ.
/// Its demographic closure (PersonState, PopulationState,
/// HouseholdLifecycle, BirthdayService, and everything they drag in)
/// expands to most of the repo, including MonoBehaviour, ScriptableObject,
/// and UI types with no Roslyn shim; a Roslyn attempt was abandoned after
/// it pulled in 200+ files and still needed Unity-only types. Run in
/// Unity Test Runner by Codex/Luna.
///
/// Pass XL vertical slice, Item 4: the six-person slice must not gain
/// newcomers through the monthly MigrationDriver.
///
/// The gate is DemographicDriverContext.AllowNewArrivals (default true,
/// so ordinary scenarios are unchanged). When false, MigrationDriver
/// skips MigrateIn arrivals only: departures and births are untouched,
/// by design (see the field's doc comment).
///
/// The test sweeps deterministic world seeds for one where the monthly
/// arrival fires cleanly (6 people become 7, no departure on the same
/// call), then re-runs that exact seed with arrivals suppressed and
/// asserts the count stays 6. Deterministic draws make the seed sweep
/// reproducible; a single fixed seed is not used because a 4% monthly
/// arrival probability means most seeds do not fire at all.
/// </summary>
[TestFixture]
public sealed class SixPersonNoImmigrationTests
{
    private sealed class SliceWorld
    {
        public PopulationState State;
        public HouseholdLifecycleManager Lifecycle;
    }

    private static SliceWorld BuildSixPersonWorld()
    {
        var state = new PopulationState();
        var memberships = new HouseholdMembershipRegistry();
        var kinship = new KinshipRegistry();
        var ids = new EntityIdRegistry();
        var lifecycle = new HouseholdLifecycleManager(state, memberships, kinship, ids);

        string[] surnames = { "Baldwin-Ooms", "Pierce", "Boniecki" };
        string[] firstNames = { "Kennedy", "Emma", "Glen", "Cassidy", "Jacob", "Grace" };
        for (int h = 0; h < 3; h++)
        {
            state.households.Add(new HouseholdState
            {
                id = h + 1,
                surname = surnames[h],
                homeBuildingId = 10 + h,
                lifecycleState = HouseholdLifecycleState.Active,
            });
        }

        for (int p = 0; p < 6; p++)
        {
            state.people.Add(new PersonState
            {
                id = p + 1,
                firstName = firstNames[p],
                lastName = surnames[p / 2],
                isFemale = p % 2 == 1,
                birthDate = new BirthDate(1995, 1, 1, BirthDateProvenance.GeneratedExact, "test"),
                householdId = p / 2 + 1,
                homeBuildingId = 10 + p / 2,
                scheduleState = PopulationScheduleState.AtHome,
                deathDayIndex = -1,
                laborUrgency01 = 0f,
            });
        }

        state.RestorePersonIdAllocator(7);
        return new SliceWorld { State = state, Lifecycle = lifecycle };
    }

    private static DemographicDriverContext BuildContext(
        SliceWorld world, int worldSeed, int dayIndex, bool allowNewArrivals)
    {
        return new DemographicDriverContext
        {
            Population = world.State,
            Lifecycle = world.Lifecycle,
            Anchor = CalendarAnchor.Default,
            CurrentDayIndex = dayIndex,
            WorldSeed = worldSeed,
            MaleNames = new[] { "Test" },
            FemaleNames = new[] { "Test" },
            Diagnostics = new List<string>(),
            AllowNewArrivals = allowNewArrivals,
        };
    }

    [Test]
    public void AllowNewArrivals_DefaultsToTrue()
    {
        Assert.IsTrue(new DemographicDriverContext().AllowNewArrivals);
    }

    [Test]
    public void SuppressedArrivals_KeepSixPeople_OnASeedWhereArrivalsFire()
    {
        const int dayIndex = 100;
        int firingSeed = -1;
        for (int seed = 0; seed < 2000; seed++)
        {
            SliceWorld probe = BuildSixPersonWorld();
            MigrationDriver.EvaluateMonth(BuildContext(probe, seed, dayIndex, true));
            if (probe.State.people.Count == 7)
            {
                firingSeed = seed;
                break;
            }
        }

        Assert.IsTrue(firingSeed >= 0,
            "No seed in 0..1999 produced a clean 6->7 arrival; the regression cannot be exercised.");

        SliceWorld suppressed = BuildSixPersonWorld();
        DemographicDriverContext ctx = BuildContext(suppressed, firingSeed, dayIndex, false);
        MigrationDriver.EvaluateMonth(ctx);

        Assert.AreEqual(6, suppressed.State.people.Count);
        bool sawSuppression = false;
        for (int i = 0; i < ctx.Diagnostics.Count; i++)
        {
            if (ctx.Diagnostics[i].Contains("arrivals suppressed"))
            {
                sawSuppression = true;
            }
        }

        Assert.IsTrue(sawSuppression, "Expected a suppression diagnostic on the gated run.");
    }
}
