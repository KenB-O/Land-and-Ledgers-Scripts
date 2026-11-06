using System;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Plan 10 slice E2 (Obj 45; afternoon direction item 2): the
    /// work-capacity investigation, landed as tests pinning the verified
    /// math. VERDICT: NO behaviour change — the trace found no genuine
    /// double-count and no misapplied penalty, so no production code is
    /// changed by this slice. The findings, traced in source at HEAD
    /// c421710 (all file/line references below are to that HEAD):
    ///
    /// 1. Nutrition bands (PersonNutritionState.WorkCapacityMultiplier):
    ///    nutrition01 &gt;= 0.70 → 1.00; &gt;= 0.40 → 0.75; &gt;= 0.15 → 0.50;
    ///    else 0.25. Recovery is +0.30 per fully-fed day
    ///    (NutritionRecoveryPerFedDay) and the cost is 0.18 per missed
    ///    meal — so the 0.25 floor is TRANSIENT, not sticky: from
    ///    nutrition01 0.00, three fed days recover to 0.90 (factor 1.00).
    /// 2. Health (PopulationHealthEvaluator.GetLaborCapacityPercent):
    ///    Illness mild 70 / serious 35; Injury mild 55 / serious 15;
    ///    else 100. HealthConditionKind has exactly three values — None,
    ///    Illness, Injury. NO malnutrition/starvation condition exists.
    ///    The only production ApplyCondition caller is
    ///    PopulationManager.TickExistingConditionsAndRollNewCases →
    ///    TryRollNewCondition, whose rolls depend on crowding / boarder /
    ///    heating pressures (illness) and profession exposure + durability
    ///    (injury) — never on nutrition01 or missed meals. No caller sets
    ///    a health condition from a nutrition state, and
    ///    laborCapacityPercent is written only inside PersonHealthState
    ///    from GetLaborCapacityPercent(kind, severity). Hunger therefore
    ///    penalizes capacity exactly once, through the nutrition band.
    /// 3. Single application (DailyNeedsService.WorkCapacityFactor =
    ///    nutrition × health, clamped 0..1): the only SetCapacityMultiplier
    ///    callers are DailyNeedsService lines 308 (reserve path) and 410
    ///    (lot path) — one call per person per day, in exactly one of the
    ///    two mutually exclusive meal paths — and WorkTimeBudget applies
    ///    it once (EffectiveBudgetMinutes = floor(600 × multiplier)).
    ///    The other GetLaborAvailability01 readers (store/SBRM worker-slot
    ///    availability, newcomer settlement readiness) are health-only
    ///    display/readiness figures that never touch a WorkTimeBudget.
    ///    The SimulationDrivers:4772 WorkCapacityFactor read is a
    ///    diagnostic log line, not an application.
    /// 4. Worked example (harness fixture values, nothing invented):
    ///    Glen fed (factor 1.00) → 600 person-minutes, a 180-minute
    ///    bread-loaf batch fits; Glen starved (factor 0.25) → 150
    ///    minutes, the bread batch does not fit but a 135-minute
    ///    rolls/pie batch does. That is the E3 gate's input math.
    /// </summary>
    [TestFixture]
    public sealed class WorkCapacityFactorTests
    {
        private static PersonState HealthyPerson(float nutrition01)
        {
            var person = new PersonState { id = 1, firstName = "Glen", lastName = "Test" };
            person.nutrition.nutrition01 = nutrition01;
            person.health.EnsureInitialized();
            return person;
        }

        private static PersonState PersonWithCondition(
            float nutrition01, HealthConditionKind kind, HealthConditionSeverity severity)
        {
            var person = HealthyPerson(nutrition01);
            person.health.ApplyCondition(kind, severity, 6, "test case");
            return person;
        }

        [Test]
        public void NutritionBands_BoundariesMatchAuthoredThresholds()
        {
            Assert.AreEqual(1.00f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(1.00f)), 0.0001f);
            Assert.AreEqual(1.00f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.70f)), 0.0001f, "0.70 is the top band");
            Assert.AreEqual(0.75f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.69f)), 0.0001f);
            Assert.AreEqual(0.75f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.40f)), 0.0001f, "0.40 is the second band");
            Assert.AreEqual(0.50f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.39f)), 0.0001f);
            Assert.AreEqual(0.50f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.15f)), 0.0001f, "0.15 is the third band");
            Assert.AreEqual(0.25f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.14f)), 0.0001f);
            Assert.AreEqual(0.25f, DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.00f)), 0.0001f, "starved floor is 0.25, never 0");
        }

        [Test]
        public void NutritionTimesHealth_CombinesTwoIndependentFactors()
        {
            // Nutrition 0.50 → band 0.75; mild illness → 70%: 0.75 × 0.70 = 0.525.
            Assert.AreEqual(0.525f,
                DailyNeedsService.WorkCapacityFactor(
                    PersonWithCondition(0.50f, HealthConditionKind.Illness, HealthConditionSeverity.Mild)),
                0.0001f);
            // Fed (1.00) × serious injury (15%) = 0.15.
            Assert.AreEqual(0.15f,
                DailyNeedsService.WorkCapacityFactor(
                    PersonWithCondition(1.00f, HealthConditionKind.Injury, HealthConditionSeverity.Serious)),
                0.0001f);
            // Starved (0.25) × serious illness (35%) = 0.0875 — the two
            // penalties multiply; neither is applied twice.
            Assert.AreEqual(0.0875f,
                DailyNeedsService.WorkCapacityFactor(
                    PersonWithCondition(0.10f, HealthConditionKind.Illness, HealthConditionSeverity.Serious)),
                0.0001f);
        }

        [Test]
        public void NoNutritionDrivenCondition_StarvationPenalizesOnceOnly()
        {
            // The condition enum has no malnutrition/starvation kind: the
            // only kinds are None, Illness, Injury.
            var kinds = (HealthConditionKind[])Enum.GetValues(typeof(HealthConditionKind));
            Assert.AreEqual(3, kinds.Length);
            CollectionAssert.AreEquivalent(
                new[] { HealthConditionKind.None, HealthConditionKind.Illness, HealthConditionKind.Injury }, kinds);
            // Every kind/severity maps through the evaluator only; None is
            // always 100% whatever the severity.
            foreach (HealthConditionSeverity severity in (HealthConditionSeverity[])Enum.GetValues(typeof(HealthConditionSeverity)))
                Assert.AreEqual(100, PopulationHealthEvaluator.GetLaborCapacityPercent(HealthConditionKind.None, severity));
            // A fully starved person with no condition has health factor
            // 1.0 — the 0.25 is the nutrition band alone, not nutrition
            // plus a hidden health penalty.
            PersonState starved = HealthyPerson(0.00f);
            Assert.IsFalse(starved.health.HasActiveCondition);
            Assert.AreEqual(100, starved.health.laborCapacityPercent);
            Assert.AreEqual(0.25f, DailyNeedsService.WorkCapacityFactor(starved), 0.0001f);
        }

        [Test]
        public void StarvedPersonRecoversToFullCapacityOnFedDays()
        {
            // From nutrition01 0.00, three fully-fed days recover to 0.90
            // (factor 1.00): the 0.25 floor is transient, not a sticky state.
            var person = HealthyPerson(0.00f);
            Assert.AreEqual(0.25f, DailyNeedsService.WorkCapacityFactor(person), 0.0001f);
            person.nutrition.ApplyDay(2, 2, 1);
            Assert.AreEqual(0.30f, person.nutrition.nutrition01, 0.0001f);
            Assert.AreEqual(0.50f, DailyNeedsService.WorkCapacityFactor(person), 0.0001f);
            person.nutrition.ApplyDay(2, 2, 2);
            Assert.AreEqual(0.60f, person.nutrition.nutrition01, 0.0001f);
            Assert.AreEqual(0.75f, DailyNeedsService.WorkCapacityFactor(person), 0.0001f);
            person.nutrition.ApplyDay(2, 2, 3);
            Assert.AreEqual(0.90f, person.nutrition.nutrition01, 0.0001f);
            Assert.AreEqual(1.00f, DailyNeedsService.WorkCapacityFactor(person), 0.0001f);
            Assert.AreEqual(WorkTimeBudget.DefaultDailyWorkMinutes,
                BakeryPersonTimeBudget.PersonMinutesPerDay(DailyNeedsService.WorkCapacityFactor(person)),
                "a recovered-fed person is back to the full 600-minute budget");
        }

        [Test]
        public void NullPersonOrState_DefaultsToFullCapacity()
        {
            Assert.AreEqual(1.00f, DailyNeedsService.WorkCapacityFactor(null), 0.0001f);
            var person = new PersonState { id = 2, firstName = "Null", lastName = "Test" };
            person.nutrition = null;
            person.health = null;
            Assert.AreEqual(1.00f, DailyNeedsService.WorkCapacityFactor(person), 0.0001f);
        }

        [Test]
        public void WorkedExample_FedGlenBakes_StarvedGlenCannotBakeBread()
        {
            // Fed Glen: 600 minutes; a 180-minute bread-loaf batch fits.
            float fedFactor = DailyNeedsService.WorkCapacityFactor(HealthyPerson(1.00f));
            Assert.AreEqual(600, BakeryPersonTimeBudget.PersonMinutesPerDay(fedFactor));
            Assert.IsNull(BakeryPersonTimeBudget.TryFitBatches(1, BakeryPersonTimeBudget.BreadLoafMinutesPerBatch, fedFactor));
            // Starved Glen: 150 minutes; the bread batch does not fit,
            // but a 135-minute rolls/pie batch does (per-product minutes).
            float starvedFactor = DailyNeedsService.WorkCapacityFactor(HealthyPerson(0.10f));
            Assert.AreEqual(150, BakeryPersonTimeBudget.PersonMinutesPerDay(starvedFactor));
            string breadRefusal = BakeryPersonTimeBudget.TryFitBatches(1, BakeryPersonTimeBudget.BreadLoafMinutesPerBatch, starvedFactor);
            Assert.IsNotNull(breadRefusal);
            Assert.IsTrue(breadRefusal.Contains("person budget"), "refusal names the person budget; refusal: " + breadRefusal);
            Assert.IsNull(BakeryPersonTimeBudget.TryFitBatches(1, BakeryPersonTimeBudget.RollsPieMinutesPerBatch, starvedFactor));
        }
    }
}
