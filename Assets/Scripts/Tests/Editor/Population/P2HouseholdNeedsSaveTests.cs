using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// P2 household &amp; daily needs journey: nutrition state and the nutrition
    /// work-capacity teeth must survive save/load. Before this pass,
    /// PersonSaveDto dropped PersonNutritionState entirely (loaded persons came
    /// back fully nourished) and WorkTimeBudget.capacityMultiplier01 was not
    /// serialized (malnourished workers regained full minutes on load).
    /// </summary>
    [TestFixture]
    public sealed class P2HouseholdNeedsSaveTests
    {
        private static PersonState MalnourishedAdult(int id, int householdId)
        {
            var person = new PersonState
            {
                id = id,
                firstName = "Hungry",
                lastName = "Test",
                age = 30,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
                nutrition = new PersonNutritionState
                {
                    nutrition01 = 0.32f,
                    consecutiveUndernourishedDays = 3,
                    mealsMissedTrailing7Days = 9,
                    lastUpdateDayIndex = 41,
                },
            };
            return person;
        }

        [Test]
        public void PopulationSaveLoadRoundTripsNutritionState()
        {
            var sourceObject = new GameObject("Nutrition Save Source");
            var targetObject = new GameObject("Nutrition Save Target");
            try
            {
                var source = sourceObject.AddComponent<PopulationManager>();
                source.State.people.Add(MalnourishedAdult(11, 7));
                source.State.households.Add(new HouseholdState { id = 7 });

                PopulationSaveDto dto = source.CaptureSaveDto();
                Assert.AreEqual(1, dto.people.Count);
                Assert.IsNotNull(dto.people[0].nutrition, "Capture must include nutrition.");
                Assert.AreEqual(0.32f, dto.people[0].nutrition.nutrition01, 0.0001f);

                var target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                PersonState restored = target.State.GetPerson(11);
                Assert.IsNotNull(restored);
                Assert.IsNotNull(restored.nutrition);
                Assert.AreEqual(0.32f, restored.nutrition.nutrition01, 0.0001f);
                Assert.AreEqual(3, restored.nutrition.consecutiveUndernourishedDays);
                Assert.AreEqual(9, restored.nutrition.mealsMissedTrailing7Days);
                Assert.AreEqual(41, restored.nutrition.lastUpdateDayIndex);
                Assert.IsTrue(restored.nutrition.IsUndernourished);
                Assert.AreEqual(0.50f, restored.nutrition.WorkCapacityMultiplier, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationSaveLoadKeepsDefaultNutritionForLegacySaves()
        {
            // A save captured before the nutrition field existed has null
            // nutrition on the DTO — load must not throw and must default.
            var dto = new PopulationSaveDto();
            dto.people.Add(new PersonSaveDto
            {
                id = 12,
                firstName = "Legacy",
                lastName = "Test",
                ageBand = AgeBand.Adult18Plus,
                nutrition = null,
            });

            var targetObject = new GameObject("Legacy Nutrition Target");
            try
            {
                var target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                PersonState restored = target.State.GetPerson(12);
                Assert.IsNotNull(restored);
                Assert.IsNotNull(restored.nutrition);
                Assert.AreEqual(1f, restored.nutrition.nutrition01, 0.0001f);
                Assert.IsFalse(restored.nutrition.IsUndernourished);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void WorkTimeBudgetRoundTripsCapacityMultiplier()
        {
            // NX-1B teeth: the capacity multiplier is today's derived work
            // capacity. A save/load must not silently restore full minutes for
            // a malnourished worker mid-day.
            var budget = new WorkTimeBudget(LandLedgers.Primitives.EntityId.For(
                LandLedgers.Primitives.EntityKind.Person, 11), 600);
            budget.SetCapacityMultiplier(0.5f);
            Assert.AreEqual(300, budget.EffectiveBudgetMinutes);

            string json = JsonUtility.ToJson(budget);
            var restored = JsonUtility.FromJson<WorkTimeBudget>(json);

            Assert.AreEqual(0.5f, restored.CapacityMultiplier01, 0.0001f);
            Assert.AreEqual(300, restored.EffectiveBudgetMinutes);
        }

        [Test]
        public void NutritionCloneIsIndependent()
        {
            var original = new PersonNutritionState { nutrition01 = 0.4f, consecutiveUndernourishedDays = 2 };
            PersonNutritionState clone = original.Clone();
            clone.nutrition01 = 0.9f;

            Assert.AreEqual(0.4f, original.nutrition01, 0.0001f);
            Assert.AreEqual(0.9f, clone.nutrition01, 0.0001f);
            Assert.AreEqual(2, clone.consecutiveUndernourishedDays);
        }
    }
}
