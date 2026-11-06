// W6 (2026-11-06): baker person-time SEAM ONLY. These tests pin the seam,
// not the booking: the owner-person link on the runtime, and the pure
// budget-reconciliation helper. No booking behaviour exists yet — it waits
// for the hunger→capacity ruling (Obj 45).
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class BakeryPersonTimeBudgetTests
    {
        // ---- The owner-person link ----

        [Test]
        public void BindOwnerPerson_NpcOwner_ResolvesPersonId()
        {
            var runtime = new BakeryShopRuntime("bakery-1");
            Assert.AreEqual(-1, runtime.OwnerPersonId);

            var diag = new List<string>();
            string refusal = runtime.BindOwnerPerson(BusinessOwnerIdentity.Npc(7, "Glen", "Pierce"), diag);

            Assert.IsNull(refusal);
            Assert.AreEqual(7, runtime.OwnerPersonId);
            Assert.AreEqual(1, diag.Count);
            Assert.IsTrue(diag[0].Contains("P7"), diag[0]);
        }

        [Test]
        public void BindOwnerPerson_NullOwner_RefusesLoudlyWithoutCrashing()
        {
            var runtime = new BakeryShopRuntime("bakery-1");
            var diag = new List<string>();

            string refusal = runtime.BindOwnerPerson(null, diag);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("no business owner supplied"), refusal);
            Assert.AreEqual(-1, runtime.OwnerPersonId, "The link stays unbound.");
            Assert.AreEqual(1, diag.Count);
        }

        [Test]
        public void BindOwnerPerson_PlayerOwner_RefusesLoudlyWithoutCrashing()
        {
            var runtime = new BakeryShopRuntime("bakery-1");
            var diag = new List<string>();

            // The Player owner has no person id (PersonId -1): there is no
            // person to book time against, so the link stays unbound.
            string refusal = runtime.BindOwnerPerson(BusinessOwnerIdentity.Player(), diag);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("has no person id"), refusal);
            Assert.AreEqual(-1, runtime.OwnerPersonId);
        }

        [Test]
        public void BindOwnerPerson_SamePersonTwice_IsANoopSuccess()
        {
            var runtime = new BakeryShopRuntime("bakery-1");
            Assert.IsNull(runtime.BindOwnerPerson(BusinessOwnerIdentity.Npc(7, "Glen", "Pierce"), null));
            Assert.IsNull(runtime.BindOwnerPerson(BusinessOwnerIdentity.Npc(7, "Glen", "Pierce"), null));
            Assert.AreEqual(7, runtime.OwnerPersonId);
        }

        // ---- The budget reconciliation ----

        [Test]
        public void PersonMinutesPerDay_ScalesSixHundredByCapacity()
        {
            Assert.AreEqual(600, BakeryPersonTimeBudget.PersonMinutesPerDay(1.00f));
            Assert.AreEqual(150, BakeryPersonTimeBudget.PersonMinutesPerDay(0.25f));
            Assert.AreEqual(0, BakeryPersonTimeBudget.PersonMinutesPerDay(0f));
            Assert.AreEqual(600, BakeryPersonTimeBudget.PersonMinutesPerDay(2f), "Capacity clamps at 1.");
            Assert.AreEqual(0, BakeryPersonTimeBudget.PersonMinutesPerDay(-0.5f), "Capacity clamps at 0.");
        }

        [Test]
        public void MaxBatches_PersonBudgetBindsAtFullCapacityForBreadLoaf()
        {
            // Bread-loaf: 180m/batch. Person 600m -> 3 batches; runtime
            // 840m -> 4 batches. The person budget binds.
            Assert.AreEqual(3, BakeryPersonTimeBudget.MaxBatchesByPersonBudget(180, 1.00f));
            Assert.AreEqual(4, BakeryPersonTimeBudget.MaxBatchesByRuntimeBudget(180));
            Assert.AreEqual("person", BakeryPersonTimeBudget.BindingBudget(180, 1.00f));
        }

        [Test]
        public void MaxBatches_AtQuarterCapacity_NoBreadLoafBatchFits()
        {
            // Capacity 0.25: person budget 150m — not even one 180m batch.
            // This is the hunger→capacity interaction: the baker cannot
            // complete a single batch today, however the runtime budget
            // looks (840m would fit 4).
            Assert.AreEqual(0, BakeryPersonTimeBudget.MaxBatchesByPersonBudget(180, 0.25f));
            Assert.AreEqual(4, BakeryPersonTimeBudget.MaxBatchesByRuntimeBudget(180));
            Assert.AreEqual("person", BakeryPersonTimeBudget.BindingBudget(180, 0.25f));
        }

        [Test]
        public void MaxBatches_RollsPieCheaperBatch_FitsFourByPersonBudget()
        {
            // Rolls/pie: 135m/batch. Person 600m -> 4 batches; runtime
            // 840m -> 6 batches. The person budget still binds.
            Assert.AreEqual(4, BakeryPersonTimeBudget.MaxBatchesByPersonBudget(135, 1.00f));
            Assert.AreEqual(6, BakeryPersonTimeBudget.MaxBatchesByRuntimeBudget(135));
            Assert.AreEqual("person", BakeryPersonTimeBudget.BindingBudget(135, 1.00f));
        }

        [Test]
        public void TryFitBatches_FitsBothBudgets_ReturnsNull()
        {
            Assert.IsNull(BakeryPersonTimeBudget.TryFitBatches(3, 180, 1.00f));
            Assert.IsNull(BakeryPersonTimeBudget.TryFitBatches(1, 135, 0.25f),
                "One 135m rolls batch fits a 150m person budget.");
        }

        [Test]
        public void TryFitBatches_FourthBreadLoaf_RefusesNamingPersonBudget()
        {
            string refusal = BakeryPersonTimeBudget.TryFitBatches(4, 180, 1.00f);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("person budget"), refusal);
            Assert.IsTrue(refusal.Contains("600m"), refusal);
            Assert.IsTrue(refusal.Contains("840m"), refusal);
        }

        [Test]
        public void TryFitBatches_SingleBatchAtQuarterCapacity_RefusesNamingPersonBudget()
        {
            string refusal = BakeryPersonTimeBudget.TryFitBatches(1, 180, 0.25f);

            Assert.IsNotNull(refusal, "One 180m batch does not fit a 150m person budget.");
            Assert.IsTrue(refusal.Contains("person budget"), refusal);
            Assert.IsTrue(refusal.Contains("150m"), refusal);
        }

        [Test]
        public void TryFitBatches_TiedBudgets_NamesRuntimeBudget()
        {
            // 300m/batch: person 600m -> 2, runtime 840m -> 2. A tie names
            // the runtime budget (deterministic; both bind equally).
            Assert.AreEqual("runtime", BakeryPersonTimeBudget.BindingBudget(300, 1.00f));
            string refusal = BakeryPersonTimeBudget.TryFitBatches(3, 300, 1.00f);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("runtime budget"), refusal);
        }
    }
}
