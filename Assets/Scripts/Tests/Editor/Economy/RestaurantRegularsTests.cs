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
    /// D1E: the regulars book — visit facts only, never credit. Regular
    /// recognition, lapse signals, expected covers for batch planning, and
    /// the hard fork boundary: no tabs, no book credit, every meal settles
    /// at service.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantRegularsTests
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
        public void NoteVisit_RecognizesRegulars_TracksPreference()
        {
            var regulars = new RestaurantRegulars();
            var diag = new List<string>();

            Assert.Null(regulars.NoteVisit(5, RestaurantMealCatalog.BeefStewId, 300, diag));
            Assert.Null(regulars.NoteVisit(5, RestaurantMealCatalog.BeefStewId, 301, diag));
            Assert.IsFalse(regulars.IsRegular(5), "two visits is not yet a regular");

            Assert.Null(regulars.NoteVisit(5, RestaurantMealCatalog.RoastPlateId, 302, diag));
            Assert.Null(regulars.NoteVisit(5, RestaurantMealCatalog.BeefStewId, 303, diag));

            Assert.IsTrue(regulars.IsRegular(5), "four visits clears the threshold");
            var record = regulars.Find(5);
            Assert.AreEqual(4, record.VisitCount);
            Assert.AreEqual(300, record.FirstVisitDayIndex);
            Assert.AreEqual(303, record.LastVisitDayIndex);
            Assert.AreEqual(RestaurantMealCatalog.BeefStewId, record.PreferredMealId,
                "three stews beat one roast plate");
        }

        [Test]
        public void NoteVisit_RefusesAnonymousDiner()
        {
            var regulars = new RestaurantRegulars();
            var diag = new List<string>();

            Assert.NotNull(regulars.NoteVisit(0, RestaurantMealCatalog.BeefStewId, 300, diag),
                "regulars are real persons (Canon §2.11)");
            Assert.AreEqual(0, regulars.Records.Count);
        }

        [Test]
        public void LapsedRegulars_FlagsDinersGoneQuiet()
        {
            var regulars = new RestaurantRegulars { LapsedAfterDays = 14 };
            var diag = new List<string>();
            for (int i = 0; i < 3; i++) Assert.Null(regulars.NoteVisit(5, RestaurantMealCatalog.BeefStewId, 300 + i, diag));
            for (int i = 0; i < 3; i++) Assert.Null(regulars.NoteVisit(6, RestaurantMealCatalog.BeefStewId, 310 + i, diag));

            Assert.AreEqual(0, regulars.LapsedRegulars(305).Count, "recent visitors are not lapsed");
            var lapsed = regulars.LapsedRegulars(320);
            Assert.AreEqual(1, lapsed.Count, "only the quiet diner lapses");
            Assert.AreEqual(5, lapsed[0].PersonId);
        }

        [Test]
        public void ExpectedCovers_PlansAgainstActiveRegulars()
        {
            var regulars = new RestaurantRegulars { LapsedAfterDays = 14 };
            var diag = new List<string>();
            for (int i = 0; i < 3; i++) Assert.Null(regulars.NoteVisit(5, RestaurantMealCatalog.BeefStewId, 300 + i, diag));
            Assert.Null(regulars.NoteVisit(7, RestaurantMealCatalog.BeefStewId, 300, diag)); // one visit: not a regular

            Assert.AreEqual(1, regulars.ExpectedCovers(305), "the active regular promises one cover");
            Assert.AreEqual(0, regulars.ExpectedCovers(320), "a lapsed regular promises nothing");
        }

        [Test]
        public void ServeMeal_NotesTheVisit_Automatically()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            for (int i = 0; i < 3; i++)
                Assert.NotNull(runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 9, 300, diag));

            Assert.IsTrue(runtime.Regulars.IsRegular(9), "three served meals make a regular");
            Assert.AreEqual(3, runtime.Regulars.Find(9).MealsServedTotal);
            Assert.AreEqual(1, runtime.Regulars.ExpectedCovers(300));
        }

        [Test]
        public void NoCredit_EveryMealSettlesAtService()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 9, 300, diag);

            Assert.NotNull(record);
            Assert.Greater(record.PriceCents, 0, "the meal has a settled price — no tab, no deferred payment");
            // The fork boundary: the regulars book carries visits and meals,
            // never a balance. There is no balance field to assert on because
            // none exists — by design.
            Assert.IsTrue(runtime.Regulars.Find(9).MealsServedTotal == 1);
        }

        [Test]
        public void Regulars_RoundTripSaveLoad()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.NotNull(runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 9, 300, diag));
            Assert.NotNull(runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 9, 300, diag));

            var dto = runtime.CaptureSaveDto();
            var restored = new RestaurantShopRuntime("rest-biz-1");
            restored.LoadFromSaveDto(dto);

            var record = restored.Regulars.Find(9);
            Assert.NotNull(record, "the regulars book survives save/load");
            Assert.AreEqual(2, record.VisitCount);
            Assert.AreEqual(2, record.MealsServedTotal);
        }
    }
}
