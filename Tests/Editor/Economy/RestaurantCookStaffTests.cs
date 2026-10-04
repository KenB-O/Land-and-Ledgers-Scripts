using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1E: cook staff depth (Canon §8.1E) — named cooks, skill-driven
    /// capacity/quality/efficiency, and the cold-stove consequence when the
    /// last cook leaves.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantCookStaffTests
    {
        private static Func<string, WorkstationComponentView?> KitchenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "stove-1", "stove-range" },
                { "cookware-1", "cookware-set" },
                { "pantry-1", "pantry-bins" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static RestaurantShopRuntime ReadyShop(List<string> diag, int day = 300)
        {
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, day, diag);
            RestaurantFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.Null(runtime.AddKitchen("cookhouse", new List<string>
            {
                "stove-1", "cookware-1", "pantry-1",
            }, diag), "kitchen installation should succeed");
            return runtime;
        }

        private static int WorkDay(RestaurantShopRuntime runtime, int day, List<string> diag)
        {
            var registry = new EntityIdRegistry();
            return runtime.WorkDay(day, cookKitUsable: true,
                WorkstationCatalog.RestaurantKitchen, KitchenFinder(), diag, registry);
        }

        [Test]
        public void AssignCook_RefusesAnonymousCook()
        {
            var staff = new RestaurantCookStaff();
            var diag = new List<string>();

            string refusal = staff.AssignCook(0, 5, 300, diag);

            Assert.NotNull(refusal, "a cook needs a real person id");
            Assert.IsFalse(staff.HasRoster);
        }

        [Test]
        public void AssignCook_ClampsSkill_ReassignUpdates()
        {
            var staff = new RestaurantCookStaff();
            var diag = new List<string>();

            Assert.Null(staff.AssignCook(11, 99, 300, diag), "absurd skill clamps, not refused");
            Assert.AreEqual(10, staff.LeadingCookSkill(300));
            Assert.AreEqual(1, staff.Cooks.Count);

            Assert.Null(staff.AssignCook(11, 4, 301, diag), "re-naming an active cook updates them");
            Assert.AreEqual(1, staff.Cooks.Count, "no duplicate roster entry");
            Assert.AreEqual(4, staff.LeadingCookSkill(301));
        }

        [Test]
        public void EmptyRoster_ProprietorCooks_W2BFallback()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.IsFalse(runtime.CookStaff.HasRoster);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(1, cooked, "no named cook: the proprietor cooks (Canon §8.1E)");
            Assert.AreEqual(RestaurantMealQualityBand.House, runtime.MealShelf[0].QualityBand,
                "the implicit cook rates the house standard");
        }

        [Test]
        public void LastCookLeaves_KitchenStopsLoudly()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.AssignCook(11, 7, 300, diag));
            Assert.Null(runtime.RemoveCook(11, 300, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(0, cooked, "nobody is at the stove");
            Assert.AreEqual(RestaurantMealBatchStage.Planned, runtime.Batches[0].Stage);
            StringAssert.Contains("nobody is at the stove", string.Join("\n", diag));
        }

        [Test]
        public void CookMinutes_BindCapacity()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            // Skill-8 cook: prep runs at 0.8× → 36m + 60m cook = 96m per stew batch.
            Assert.Null(runtime.AssignCook(11, 8, 300, diag, minutesPerDay: 120));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 2, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(1, cooked, "120 minutes covers one 96-minute batch, not two");
            Assert.AreEqual(RestaurantMealBatchStage.Cooked, runtime.Batches[0].Stage);
            Assert.AreEqual(RestaurantMealBatchStage.Planned, runtime.Batches[1].Stage,
                "the second batch never started — its ingredients were never dispensed");
        }

        [Test]
        public void TwoCooks_MinutesSum_LeadingSkillSetsQuality()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.AssignCook(11, 3, 300, diag, minutesPerDay: 60));
            Assert.Null(runtime.AssignCook(12, 8, 300, diag, minutesPerDay: 60));

            Assert.AreEqual(120, runtime.CookStaff.TotalMinutesToday(300), "capacity is the sum of on-duty cooks");
            Assert.AreEqual(8, runtime.CookStaff.LeadingCookSkill(300), "quality follows the leading cook");

            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.AreEqual(RestaurantMealQualityBand.Fine, runtime.MealShelf[0].QualityBand);
        }

        [Test]
        public void EffectivePrepMinutes_SkillAdjustsLabor()
        {
            Assert.AreEqual(36, RestaurantCookStaff.EffectivePrepMinutes(45, 8), "a strong cook is faster");
            Assert.AreEqual(45, RestaurantCookStaff.EffectivePrepMinutes(45, 3));
            Assert.AreEqual(57, RestaurantCookStaff.EffectivePrepMinutes(45, 1), "a green cook is slower (ceil 56.25)");
            Assert.AreEqual(45, RestaurantCookStaff.EffectivePrepMinutes(45, 0), "unknown skill: no adjustment");
            Assert.AreEqual(0, RestaurantCookStaff.EffectivePrepMinutes(0, 8));
        }

        [Test]
        public void CookRoster_RoundTripsSaveLoad()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.AssignCook(11, 8, 300, diag, minutesPerDay: 200));

            var dto = runtime.CaptureSaveDto();
            var restored = new RestaurantShopRuntime("rest-biz-1");
            restored.LoadFromSaveDto(dto);

            Assert.IsTrue(restored.CookStaff.HasRoster, "the roster survives save/load");
            Assert.AreEqual(8, restored.CookStaff.LeadingCookSkill(300));
            Assert.AreEqual(200, restored.CookStaff.TotalMinutesToday(300));
        }
    }
}
