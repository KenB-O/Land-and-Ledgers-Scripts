using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// NX-1B: NPC consequences. Missed meals degrade nutrition (Tech X §2.9 —
    /// derived from actual meal history); sustained undernourishment reduces
    /// usable work minutes. Scarcity allocates children → workers → others.
    /// </summary>
    public sealed class NpcConsequencesTests
    {
        [Test]
        public void MissedMealsDegradeNutrition()
        {
            var n = new PersonNutritionState();
            Assert.AreEqual(1f, n.nutrition01, 0.001f);
            n.ApplyDay(0, 2, 10); // missed both meals
            Assert.Less(n.nutrition01, 1f);
            Assert.AreEqual(1, n.consecutiveUndernourishedDays);
            Assert.IsTrue(n.IsUndernourished); // 1 - 2*0.18 = 0.64 < 0.70
        }

        [Test]
        public void FedDayRecoversNutrition()
        {
            var n = new PersonNutritionState();
            n.ApplyDay(0, 2, 10);
            float afterMiss = n.nutrition01;
            n.ApplyDay(2, 2, 11);
            Assert.Greater(n.nutrition01, afterMiss);
            Assert.AreEqual(0, n.consecutiveUndernourishedDays);
        }

        [Test]
        public void StarvationCollapsesWorkCapacity()
        {
            var n = new PersonNutritionState();
            for (int d = 0; d < 6; d++) n.ApplyDay(0, 2, d);
            Assert.AreEqual(0.25f, n.WorkCapacityMultiplier, 0.001f);
        }

        [Test]
        public void CapacityMultiplierReducesUsableMinutes()
        {
            var store = new WorkTimeBudgetStore();
            var budget = store.GetOrCreate(EntityId.For(EntityKind.Person, 5), 600);
            Assert.AreEqual(600, budget.MinutesRemaining);
            budget.SetCapacityMultiplier(0.5f);
            Assert.AreEqual(300, budget.MinutesRemaining);
        }

        [Test]
        public void MealAllocatorFeedsChildrenFirst()
        {
            var child = new PersonState { id = 1, age = 6, ageBand = AgeBand.Child0To9, laborAccessLevel = LaborAccessLevel.None };
            var worker = new PersonState { id = 2, age = 30, ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket };
            var other = new PersonState { id = 3, age = 70, ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.None };
            // Deliberately pass in reverse priority order.
            var members = new List<PersonState> { other, worker, child };

            // 2 meals available, 2 needed per person: only the child eats.
            Dictionary<int, int> alloc = MealAllocator.Allocate(members, 2, 2);
            Assert.AreEqual(2, alloc[1]);
            Assert.AreEqual(0, alloc[2]);
            Assert.AreEqual(0, alloc[3]);
        }

        [Test]
        public void MealAllocatorPrefersWorkersOverOtherAdults()
        {
            var worker = new PersonState { id = 2, age = 30, ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket };
            var other = new PersonState { id = 3, age = 70, ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.None };
            var members = new List<PersonState> { other, worker };

            Dictionary<int, int> alloc = MealAllocator.Allocate(members, 2, 2);
            Assert.AreEqual(2, alloc[2]);
            Assert.AreEqual(0, alloc[3]);
        }

        [Test]
        public void MealAllocatorCapsPerPerson()
        {
            var a = new PersonState { id = 1, age = 30, ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket };
            var b = new PersonState { id = 2, age = 32, ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket };
            var members = new List<PersonState> { a, b };

            // 10 meals for 2 people needing 2 each: capped, nothing wasted.
            Dictionary<int, int> alloc = MealAllocator.Allocate(members, 10, 2);
            Assert.AreEqual(2, alloc[1]);
            Assert.AreEqual(2, alloc[2]);
        }
    }
}
