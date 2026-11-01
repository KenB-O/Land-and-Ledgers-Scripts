using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass V slice F: entrepreneurship on production authorities. The
    /// NpcEntrepreneurshipDriver runs observation -> recognition ->
    /// investigation -> formation with no quotas: businesses form only when a
    /// real person witnesses real unmet demand, investigates with real person
    /// time, and the evidence supports proceeding. Walk-aways (no capital, no
    /// premises, refused formation) create nothing.
    /// </summary>
    [TestFixture]
    public sealed class Pass5SliceFailedOpportunityTests
    {
        private sealed class FakeSupplier : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "mill-1";
            public string SupplierName { get; set; } = "Mill";
            public string LocationId { get; set; } = "mill";
            public int PriceCents = 100;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 1000;
            public int PricePerUnitCents(string categoryId) => PriceCents;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
            public bool OffersItem(string itemId) => itemId == HouseholdItemCatalog.FlourId;
            public string CategoryForItem(string itemId) => "staple";
            public int ItemStockUnits(string itemId) => 1000;
            public int ItemPricePerUnitCents(string itemId) => PriceCents;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public string LastReceiptId => string.Empty;
            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics) => 0;
        }

        private sealed class FakePort : INpcBusinessCreationPort
        {
            public readonly List<NpcBusinessFormationIntent> Intents = new List<NpcBusinessFormationIntent>();
            public bool Refuse;

            public string TryCreateBusiness(NpcBusinessFormationIntent intent, List<string> diagnostics,
                out NpcCreatedBusiness created)
            {
                Intents.Add(intent);
                if (Refuse)
                {
                    created = null;
                    return "fake port refused: no premises rights on file";
                }

                created = new NpcCreatedBusiness
                {
                    InstanceId = "biz-" + Intents.Count,
                    DisplayName = intent.DisplayName,
                    FounderPersonId = intent.FounderPersonId,
                    OwnerName = intent.FounderName,
                };
                return null;
            }
        }

        private sealed class Fixture
        {
            public const int PersonId = 11;
            public const int HouseholdId = 7;

            public readonly PopulationState Population = new PopulationState();
            public readonly HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public readonly NpcBusinessEventLog Events = new NpcBusinessEventLog();
            public readonly SupplierDirectory Suppliers = new SupplierDirectory();
            public readonly FakePort Port = new FakePort();
            public readonly NpcEntrepreneurshipDriver Driver = new NpcEntrepreneurshipDriver();
            public readonly List<string> Diag = new List<string>();
            public bool AlreadyOwns;

            public Fixture()
            {
                Population.people.Add(new PersonState
                {
                    id = PersonId, firstName = "Tomas", lastName = "Morrow", age = 34,
                    ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    householdId = HouseholdId,
                });
                Population.households.Add(new HouseholdState { id = HouseholdId, householdName = "Morrow" });
                Memberships.Register(new HouseholdMembership
                {
                    MembershipId = 1, PersonId = PersonId, HouseholdId = HouseholdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = 0,
                });
                Suppliers.Register(new FakeSupplier());
            }

            public void SeedSavings(int cents)
            {
                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, cents,
                    HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");
            }

            public void OpenOldNeed(string itemId, int units, int createdDay)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, createdDay);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                need.Urgency01 = 0.8f;
            }

            public NpcEntrepreneurshipDriver.EntrepreneurshipContext Context(
                System.Func<NpcOpportunity, NpcPremisesOption> premisesFinder,
                System.Func<NpcOpportunity, NpcEquipmentOption> equipmentFinder)
            {
                return new NpcEntrepreneurshipDriver.EntrepreneurshipContext
                {
                    Population = Population,
                    Memberships = Memberships,
                    Ledgers = Ledgers,
                    Needs = Needs,
                    Schedules = Schedules,
                    Events = Events,
                    CreationPort = Port,
                    Suppliers = Suppliers,
                    PersonAlreadyOwnsBusiness = pid => AlreadyOwns,
                    PremisesFinder = premisesFinder,
                    EquipmentFinder = equipmentFinder,
                    WorkingCapitalCents = 2000,
                };
            }

            public static NpcPremisesOption Premises()
            {
                return new NpcPremisesOption
                {
                    Description = "vacant shop on Mill Street",
                    Tenure = NpcPremisesTenure.Rented,
                    ParcelOrBuildingRef = "bldg-12",
                    AgreementRef = "lease-12",
                    UpfrontCostCents = 3000,
                    RecurringCostCents = 500,
                };
            }

            public static NpcEquipmentOption Equipment()
            {
                return new NpcEquipmentOption
                {
                    Description = "shop counter and scales",
                    ItemId = "shop_fittings",
                    CostCents = 1500,
                    Provenance = "used equipment market listing L-1",
                };
            }
        }

        [Test]
        public void F_RepeatedUnfulfilledNeed_IsRecognizedAsOpportunity()
        {
            var f = new Fixture();
            f.SeedSavings(0); // no capital: recognition happens, formation cannot
            f.OpenOldNeed(HouseholdItemCatalog.FlourId, 10, 90);
            var ctx = f.Context(_ => Fixture.Premises(), _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(100, ctx, f.Diag);
            f.Driver.EvaluateWeek(107, ctx, f.Diag);
            Assert.AreEqual(0, f.Port.Intents.Count, "no formation before recognition");

            f.Driver.EvaluateWeek(114, ctx, f.Diag);

            // Recognized from the person's own witnessed observations...
            bool recognized = false;
            foreach (string d in f.Diag)
                if (d.Contains("recognized opportunity")) recognized = true;
            Assert.IsTrue(recognized, "three weeks of witnessed unmet demand must recognize an opportunity");
            // ...but with no capital the founder does not investigate.
            Assert.AreEqual(0, f.Port.Intents.Count);
            bool cannotFund = false;
            foreach (string d in f.Diag)
                if (d.Contains("cannot fund")) cannotFund = true;
            Assert.IsTrue(cannotFund, "expected a cannot-fund note, got: " + string.Join(" | ", f.Diag));
        }

        [Test]
        public void F_FundedFounder_ProceedsAndFormsBusiness()
        {
            var f = new Fixture();
            f.SeedSavings(20000);
            f.OpenOldNeed(HouseholdItemCatalog.FlourId, 10, 90);
            var ctx = f.Context(_ => Fixture.Premises(), _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(100, ctx, f.Diag);
            f.Driver.EvaluateWeek(107, ctx, f.Diag);
            f.Driver.EvaluateWeek(114, ctx, f.Diag);

            Assert.AreEqual(1, f.Port.Intents.Count, "one Proceed verdict forms exactly one business");
            NpcBusinessFormationIntent intent = f.Port.Intents[0];
            Assert.AreEqual(Fixture.PersonId, intent.FounderPersonId);
            Assert.AreEqual("Tomas", intent.FounderName);
            Assert.AreEqual("Morrow", intent.FounderSurname);
            Assert.AreEqual(BusinessType.Generic, intent.Activities[0].BusinessType);
            Assert.IsFalse(string.IsNullOrWhiteSpace(intent.InvestigationId),
                "formation must cite the real Proceed investigation");
            Assert.AreEqual("bldg-12", intent.Premises.ParcelOrBuildingRef);
            Assert.AreEqual(1, intent.Assets.Count);
            Assert.AreEqual("used equipment market listing L-1", intent.Assets[0].Provenance);

            // No quota, no repeat: a later week does not form a second business
            // from the same opportunity.
            f.Driver.EvaluateWeek(121, ctx, f.Diag);
            Assert.AreEqual(1, f.Port.Intents.Count);
        }

        [Test]
        public void F_NoPremises_WalksAway_CreatesNothing()
        {
            var f = new Fixture();
            f.SeedSavings(20000);
            f.OpenOldNeed(HouseholdItemCatalog.FlourId, 10, 90);
            // No viable premises anywhere: the investigation must walk away.
            var ctx = f.Context(_ => null, _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(100, ctx, f.Diag);
            f.Driver.EvaluateWeek(107, ctx, f.Diag);
            f.Driver.EvaluateWeek(114, ctx, f.Diag);

            Assert.AreEqual(0, f.Port.Intents.Count);
            bool walkedAway = false;
            foreach (string d in f.Diag)
                if (d.Contains("walked away") && d.Contains("no premises")) walkedAway = true;
            Assert.IsTrue(walkedAway, "expected a premises walk-away, got: " + string.Join(" | ", f.Diag));
        }

        [Test]
        public void F_FormationRefused_Stands_CreatesNothing()
        {
            var f = new Fixture();
            f.SeedSavings(20000);
            f.OpenOldNeed(HouseholdItemCatalog.FlourId, 10, 90);
            f.Port.Refuse = true;
            var ctx = f.Context(_ => Fixture.Premises(), _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(100, ctx, f.Diag);
            f.Driver.EvaluateWeek(107, ctx, f.Diag);
            f.Driver.EvaluateWeek(114, ctx, f.Diag);

            Assert.AreEqual(1, f.Port.Intents.Count, "the intent was attempted");
            bool refused = false;
            foreach (string d in f.Diag)
                if (d.Contains("formation for P11 refused")) refused = true;
            Assert.IsTrue(refused, "the refusal must be recorded, got: " + string.Join(" | ", f.Diag));
            // The founder's savings are untouched: no cash moved on refusal.
            Assert.AreEqual(20000, f.Ledgers.Get(Fixture.HouseholdId).GetBalanceCents());
        }

        [Test]
        public void F_NoObservations_NoBusiness_NoQuota()
        {
            var f = new Fixture();
            f.SeedSavings(20000);
            // No needs at all: nothing observed, nothing recognized, nothing formed.
            var ctx = f.Context(_ => Fixture.Premises(), _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(100, ctx, f.Diag);
            f.Driver.EvaluateWeek(107, ctx, f.Diag);
            f.Driver.EvaluateWeek(114, ctx, f.Diag);

            Assert.AreEqual(0, f.Port.Intents.Count);
            Assert.AreEqual(0, f.Driver.TrackedPersonCount);
        }

        [Test]
        public void F_RecentNeed_IsNotTreatedAsUnfulfilled()
        {
            var f = new Fixture();
            f.SeedSavings(20000);
            // A need opened yesterday may still be served by this week's
            // shopping trip: it is not evidence of unmet demand.
            f.OpenOldNeed(HouseholdItemCatalog.FlourId, 10, 113);
            var ctx = f.Context(_ => Fixture.Premises(), _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(114, ctx, f.Diag);
            f.Driver.EvaluateWeek(121, ctx, f.Diag);

            Assert.AreEqual(0, f.Port.Intents.Count);
        }

        [Test]
        public void F_ExistingOwner_DoesNotFoundAgain()
        {
            var f = new Fixture();
            f.SeedSavings(20000);
            f.AlreadyOwns = true;
            f.OpenOldNeed(HouseholdItemCatalog.FlourId, 10, 90);
            var ctx = f.Context(_ => Fixture.Premises(), _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(100, ctx, f.Diag);
            f.Driver.EvaluateWeek(107, ctx, f.Diag);
            f.Driver.EvaluateWeek(114, ctx, f.Diag);

            Assert.AreEqual(0, f.Port.Intents.Count, "an existing owner is not eligible to found again");
            Assert.AreEqual(0, f.Driver.TrackedPersonCount);
        }

        [Test]
        public void F_Driver_SaveLoad_RestoresLogsWithoutReforming()
        {
            var f = new Fixture();
            f.SeedSavings(20000);
            f.OpenOldNeed(HouseholdItemCatalog.FlourId, 10, 90);
            var ctx = f.Context(_ => Fixture.Premises(), _ => Fixture.Equipment());

            f.Driver.EvaluateWeek(100, ctx, f.Diag);
            f.Driver.EvaluateWeek(107, ctx, f.Diag);

            NpcEntrepreneurshipDriver.NpcEntrepreneurshipDriverSaveDto dto = f.Driver.CaptureSaveDto();
            var restored = new NpcEntrepreneurshipDriver();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.TrackedPersonCount);

            // Continuing on the restored driver recognizes (week 3) and forms
            // exactly once — the restored log is not re-observed from scratch.
            restored.EvaluateWeek(114, ctx, f.Diag);
            Assert.AreEqual(1, f.Port.Intents.Count);
            restored.EvaluateWeek(121, ctx, f.Diag);
            Assert.AreEqual(1, f.Port.Intents.Count, "no duplicate formation after restore");

            // Clear-then-load with an empty DTO resets.
            restored.LoadFromSaveDto(new NpcEntrepreneurshipDriver.NpcEntrepreneurshipDriverSaveDto());
            Assert.AreEqual(0, restored.TrackedPersonCount);
        }
    }
}
