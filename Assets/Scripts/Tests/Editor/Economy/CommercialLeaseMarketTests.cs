using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// B2 (universal business campaign 2026-11-17): the commercial lease
    /// authority — listings and agreements, monthly rent settlement, the
    /// entrepreneurship driver's lease formation (deposit + first rent
    /// before creation, lease signed after it, full refund on refusal),
    /// and the lifecycle driver's close hook (fires only on a close).
    /// The close-to-release-to-relist chain's production links
    /// (SharedBusinessRuntimeManager release, SimulationDrivers callback)
    /// are harness-excluded and Codex-verified; the market halves of that
    /// chain (EndLease re-lists, deposit refund rules) are pinned here.
    /// All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class CommercialLeaseMarketTests
    {
        private const int FounderPersonId = 42;
        private const int FounderHouseholdId = 7;
        private const int LandlordPersonId = 9;
        private const int LandlordHouseholdId = 9;
        private const int ShopBuildingId = 5;
        private const int ShopPlotId = 3;

        private sealed class FakeCreationPort : INpcBusinessCreationPort
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public NpcBusinessFormationIntent LastIntent;
            public int CreateCalls;
            public bool RefuseNext;
            public string RefuseReason = "fake port refused.";

            public string TryCreateBusiness(NpcBusinessFormationIntent intent, List<string> diagnostics,
                out NpcCreatedBusiness created)
            {
                created = null;
                CreateCalls++;
                LastIntent = intent;
                if (RefuseNext)
                {
                    diagnostics?.Add(RefuseReason);
                    return RefuseReason;
                }

                EntityId id = Ids.Allocate(EntityKind.Business);
                created = new NpcCreatedBusiness
                {
                    BusinessEntityKey = id.ToString(),
                    InstanceId = "biz-" + id.ToString(),
                    DisplayName = intent.DisplayName,
                    FounderPersonId = intent.FounderPersonId,
                };
                return null;
            }
        }

        private sealed class FakeItemSupplier : IItemResolvingSupplier
        {
            public string SupplierBusinessId => "mill";
            public string SupplierName => "Mill";
            public string LocationId => "town";
            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 100;
            public int PricePerUnitCents(string categoryId) => 480;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
            public bool OffersItem(string itemId) => itemId == "flour";
            public string CategoryForItem(string itemId) => "flour";
            public int ItemStockUnits(string itemId) => 100;
            public int ItemPricePerUnitCents(string itemId) => 480;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics) => 0;
            public string LastReceiptId => string.Empty;
        }

        private sealed class FakeCashPort : INpcBusinessCashPort
        {
            public Dictionary<string, int> Balances = new Dictionary<string, int>();

            public int ReadCashCents(string businessInstanceId)
            {
                return Balances.TryGetValue(businessInstanceId, out int balance) ? balance : 0;
            }

            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                if (amountCents <= 0) return "spend must be positive.";
                int balance = ReadCashCents(businessInstanceId);
                if (amountCents > balance) return $"insufficient business cash ({balance}c for {amountCents}c).";
                Balances[businessInstanceId] = balance - amountCents;
                return null;
            }

            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                if (amountCents <= 0) return "receive must be positive.";
                Balances[businessInstanceId] = ReadCashCents(businessInstanceId) + amountCents;
                return null;
            }
        }

        private sealed class FakeLiquidationPort : INpcLiquidationPort
        {
            public string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
                out NpcLiquidationSale sale)
            {
                sale = null;
                return null;
            }
        }

        // ---------- Market: listings ----------

        [Test]
        public void SeedFromPremises_CreatesUnresolvedListings_Idempotent()
        {
            var market = new CommercialLeaseMarket();
            var diag = new List<string>();
            var refs = new List<CommercialLeasePremisesRef>
            {
                new CommercialLeasePremisesRef(ShopBuildingId, ShopPlotId),
                new CommercialLeasePremisesRef(6, 4),
            };

            market.SeedFromPremises(refs, 0, diag);
            Assert.AreEqual(2, market.Listings.Count, "One listing per unassigned building.");
            foreach (CommercialLeaseListing listing in market.Listings)
            {
                Assert.AreEqual(-1, listing.LandlordPersonId, "Seeded listings carry no landlord — none is recorded.");
                Assert.IsFalse(listing.IsOfferable, "A listing with no landlord is recorded, never offered.");
                Assert.AreEqual(CommercialLeaseListingStatus.Active, listing.Status);
                Assert.AreEqual(CommercialLeaseCalibration.MonthlyRentCents, listing.MonthlyRentCents);
                Assert.AreEqual(CommercialLeaseCalibration.DepositCents, listing.DepositCents);
                Assert.AreEqual(CommercialLeaseCalibration.TermMonths, listing.TermMonths);
                Assert.AreEqual(3, listing.IncludedFixtures.Count, "A fitted shop lists counter, shelving, scale.");
            }

            market.SeedFromPremises(refs, 1, diag);
            Assert.AreEqual(2, market.Listings.Count, "Re-seeding is a no-op — no duplicate listings.");
        }

        [Test]
        public void ListBuilding_RequiresRealLandlord_AndRefusesDuplicates()
        {
            var market = new CommercialLeaseMarket();
            var diag = new List<string>();

            Assert.IsNull(market.ListBuilding(ShopBuildingId, ShopPlotId, -1, "Nobody", 0, diag),
                "No landlord, no listing — a building is never let on behalf of nobody.");

            CommercialLeaseListing listing = market.ListBuilding(ShopBuildingId, ShopPlotId,
                LandlordPersonId, "Landlord Name", 0, diag);
            Assert.IsNotNull(listing);
            Assert.IsTrue(listing.IsOfferable);
            Assert.AreEqual("person:" + LandlordPersonId, listing.LandlordHolderId);
            Assert.AreEqual(CommercialLeaseMarket.ListingIdForBuilding(ShopBuildingId), listing.ListingId);

            Assert.IsNull(market.ListBuilding(ShopBuildingId, ShopPlotId, LandlordPersonId, "Landlord Name", 1, diag),
                "A building with an Active listing is not re-listed.");
        }

        [Test]
        public void FindOfferableListingForBuilding_ReturnsNullWhenLandlordUnresolved()
        {
            var market = new CommercialLeaseMarket();
            market.SeedFromPremises(new List<CommercialLeasePremisesRef>
            {
                new CommercialLeasePremisesRef(ShopBuildingId, ShopPlotId),
            }, 0, new List<string>());
            Assert.IsNull(market.FindOfferableListingForBuilding(ShopBuildingId),
                "The seeded listing is not offerable until a landlord is known.");
        }

        // ---------- Market: agreements ----------

        [Test]
        public void ActivateLease_SignsAgreement_AndMarksListingLeased()
        {
            var market = new CommercialLeaseMarket();
            var diag = new List<string>();
            CommercialLeaseListing listing = market.ListBuilding(ShopBuildingId, ShopPlotId,
                LandlordPersonId, "Landlord Name", 0, diag);

            CommercialLeaseAgreement agreement = market.ActivateLease(listing,
                FounderPersonId, FounderHouseholdId, "biz-test", 1500, firstRentPeriodPrepaid: true, 10, diag);
            Assert.IsNotNull(agreement);
            Assert.AreEqual(CommercialLeaseAgreementStatus.Active, agreement.Status);
            Assert.AreEqual(1500, agreement.DepositPaidCents);
            Assert.AreEqual(0, agreement.LastSettledPeriodIndex, "The first month was prepaid at signing.");
            Assert.AreEqual(ShopBuildingId, agreement.BuildingId);
            Assert.AreEqual("person:" + LandlordPersonId, agreement.LandlordHolderId);
            Assert.AreEqual(CommercialLeaseListingStatus.Leased, listing.Status);

            Assert.IsNull(market.ActivateLease(listing, FounderPersonId, FounderHouseholdId, "biz-test-2", 1500, true, 11, diag),
                "A Leased listing cannot be leased again.");
            Assert.IsNull(market.ActivateLease(listing, -1, FounderHouseholdId, "biz-test-3", 1500, true, 11, diag),
                "A tenant with no person id cannot sign.");
        }

        [Test]
        public void EndLease_ReturnsBuildingToMarket_WithSameLandlord()
        {
            var market = new CommercialLeaseMarket();
            var diag = new List<string>();
            CommercialLeaseListing listing = market.ListBuilding(ShopBuildingId, ShopPlotId,
                LandlordPersonId, "Landlord Name", 0, diag);
            CommercialLeaseAgreement agreement = market.ActivateLease(listing,
                FounderPersonId, FounderHouseholdId, "biz-test", 1500, true, 10, diag);

            Assert.IsTrue(market.EndLease(agreement, 40, diag));
            Assert.AreEqual(CommercialLeaseAgreementStatus.Ended, agreement.Status);
            Assert.AreEqual(40, agreement.EndDayIndex);
            Assert.AreEqual(CommercialLeaseListingStatus.Active, listing.Status,
                "The building is offerable again, with the same landlord and the same fixtures.");
            Assert.AreEqual(LandlordPersonId, listing.LandlordPersonId);
            Assert.AreEqual(3, listing.IncludedFixtures.Count);

            Assert.IsFalse(market.EndLease(agreement, 41, diag), "Ending an ended lease is refused, not double-ended.");
        }

        [Test]
        public void WithdrawListing_RefusesLeasedListing()
        {
            var market = new CommercialLeaseMarket();
            var diag = new List<string>();
            CommercialLeaseListing listing = market.ListBuilding(ShopBuildingId, ShopPlotId,
                LandlordPersonId, "Landlord Name", 0, diag);
            market.ActivateLease(listing, FounderPersonId, FounderHouseholdId, "biz-test", 1500, true, 10, diag);

            Assert.IsFalse(market.WithdrawListing(listing.ListingId, 11, diag),
                "A Leased listing cannot be withdrawn from under its tenant.");
            Assert.AreEqual(CommercialLeaseListingStatus.Leased, listing.Status);
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesAgreementsAndSequence()
        {
            var market = new CommercialLeaseMarket();
            var diag = new List<string>();
            CommercialLeaseListing listing = market.ListBuilding(ShopBuildingId, ShopPlotId,
                LandlordPersonId, "Landlord Name", 0, diag);
            CommercialLeaseAgreement agreement = market.ActivateLease(listing,
                FounderPersonId, FounderHouseholdId, "biz-test", 1500, true, 10, diag);

            var restored = new CommercialLeaseMarket();
            restored.LoadFromSaveDto(market.CaptureSaveDto());
            Assert.AreEqual(1, restored.Listings.Count);
            Assert.AreEqual(1, restored.Agreements.Count);
            CommercialLeaseAgreement restoredAgreement = restored.Agreements[0];
            Assert.AreEqual(agreement.AgreementId, restoredAgreement.AgreementId);
            Assert.AreEqual(0, restoredAgreement.LastSettledPeriodIndex);
            Assert.AreEqual(1500, restoredAgreement.DepositPaidCents);
            Assert.AreEqual(CommercialLeaseListingStatus.Leased, restored.Listings[0].Status);

            CommercialLeaseListing second = restored.ListBuilding(6, 4, LandlordPersonId, "Landlord Name", 20, diag);
            CommercialLeaseAgreement secondAgreement = restored.ActivateLease(second,
                FounderPersonId, FounderHouseholdId, "biz-test-2", 1500, true, 20, diag);
            Assert.AreNotEqual(agreement.AgreementId, secondAgreement.AgreementId,
                "The agreement sequence survives the round-trip — no duplicate lease ids.");
        }

        // ---------- Rent settlement ----------

        private static CommercialLeaseMarket MarketWithActiveLease(out CommercialLeaseAgreement agreement,
            int startDay = 0)
        {
            var market = new CommercialLeaseMarket();
            CommercialLeaseListing listing = market.ListBuilding(ShopBuildingId, ShopPlotId,
                LandlordPersonId, "Landlord Name", startDay, new List<string>());
            agreement = market.ActivateLease(listing, FounderPersonId, FounderHouseholdId,
                "biz-test", 1500, firstRentPeriodPrepaid: true, startDay, new List<string>());
            return market;
        }

        [Test]
        public void SettleDue_PaysRentInFull_FromBusinessCash()
        {
            CommercialLeaseMarket market = MarketWithActiveLease(out CommercialLeaseAgreement agreement);
            var cash = new FakeCashPort();
            cash.Balances["biz-test"] = 10000;
            var landlordLedger = new HouseholdLedger(LandlordHouseholdId);
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            CommercialLeaseRentSettlementResult result = new CommercialLeaseRentSettlement().SettleDue(
                market, cash, personId => landlordLedger, obligations, ids, 30, new List<string>());

            Assert.AreEqual(1, result.PeriodsSettled);
            Assert.AreEqual(1, result.PeriodsPaidInFull);
            Assert.AreEqual(0, result.PeriodsInArrears);
            Assert.AreEqual(1500, result.TotalPaidCents);
            Assert.AreEqual(8500, cash.ReadCashCents("biz-test"), "Rent leaves the business till.");
            Assert.AreEqual(1500, landlordLedger.GetBalanceCents(), "Rent reaches the landlord's household.");
            Assert.AreEqual(1, agreement.LastSettledPeriodIndex);
            Assert.AreEqual(0, agreement.RentPayableIds.Count, "No debt when the rent is paid.");
        }

        [Test]
        public void SettleDue_IsIdempotent_WithinAPeriod()
        {
            CommercialLeaseMarket market = MarketWithActiveLease(out CommercialLeaseAgreement agreement);
            var cash = new FakeCashPort();
            cash.Balances["biz-test"] = 10000;
            var landlordLedger = new HouseholdLedger(LandlordHouseholdId);
            var settlement = new CommercialLeaseRentSettlement();
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            settlement.SettleDue(market, cash, personId => landlordLedger, obligations, ids, 30, new List<string>());
            CommercialLeaseRentSettlementResult second = settlement.SettleDue(
                market, cash, personId => landlordLedger, obligations, ids, 30, new List<string>());

            Assert.AreEqual(0, second.PeriodsSettled, "A second pass in the same period settles nothing twice.");
            Assert.AreEqual(8500, cash.ReadCashCents("biz-test"));
            Assert.AreEqual(1500, landlordLedger.GetBalanceCents());
        }

        [Test]
        public void SettleDue_PartialPayment_BooksRemainderAsPayable()
        {
            CommercialLeaseMarket market = MarketWithActiveLease(out CommercialLeaseAgreement agreement);
            var cash = new FakeCashPort();
            cash.Balances["biz-test"] = 500;
            var landlordLedger = new HouseholdLedger(LandlordHouseholdId);
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            CommercialLeaseRentSettlementResult result = new CommercialLeaseRentSettlement().SettleDue(
                market, cash, personId => landlordLedger, obligations, ids, 30, new List<string>());

            Assert.AreEqual(1, result.PeriodsSettled);
            Assert.AreEqual(0, result.PeriodsPaidInFull);
            Assert.AreEqual(1, result.PeriodsInArrears);
            Assert.AreEqual(500, result.TotalPaidCents, "What the business has, it pays.");
            Assert.AreEqual(1000, result.TotalArrearsCents, "The remainder is real debt, never informal.");
            Assert.AreEqual(0, cash.ReadCashCents("biz-test"));
            Assert.AreEqual(500, landlordLedger.GetBalanceCents());
            Assert.AreEqual(1, agreement.RentPayableIds.Count);
            FinancialObligation payable = obligations.Find(agreement.RentPayableIds[0]);
            Assert.IsNotNull(payable);
            Assert.AreEqual(1000, payable.OutstandingPrincipalCents);
            Assert.AreEqual("business:biz-test", payable.Debtor);
            Assert.AreEqual("person:" + LandlordPersonId, payable.Creditor);
            Assert.AreEqual(agreement.AgreementId, payable.AgreementId, "The arrears name the lease.");
            Assert.AreEqual(1, agreement.LastSettledPeriodIndex, "An arrears-booked period is settled, not retried.");
        }

        [Test]
        public void SettleDue_FullArrears_WhenBusinessHasNoCash()
        {
            CommercialLeaseMarket market = MarketWithActiveLease(out CommercialLeaseAgreement agreement);
            var cash = new FakeCashPort();
            cash.Balances["biz-test"] = 0;
            var landlordLedger = new HouseholdLedger(LandlordHouseholdId);
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            CommercialLeaseRentSettlementResult result = new CommercialLeaseRentSettlement().SettleDue(
                market, cash, personId => landlordLedger, obligations, ids, 30, new List<string>());

            Assert.AreEqual(1, result.PeriodsInArrears);
            Assert.AreEqual(1500, result.TotalArrearsCents);
            Assert.AreEqual(0, landlordLedger.GetBalanceCents());
            Assert.AreEqual(1, agreement.RentPayableIds.Count);
        }

        [Test]
        public void SettleDue_Arrears_WhenLandlordHasNoLedger()
        {
            CommercialLeaseMarket market = MarketWithActiveLease(out CommercialLeaseAgreement agreement);
            var cash = new FakeCashPort();
            cash.Balances["biz-test"] = 10000;
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            CommercialLeaseRentSettlementResult result = new CommercialLeaseRentSettlement().SettleDue(
                market, cash, personId => null, obligations, ids, 30, new List<string>());

            Assert.AreEqual(1, result.PeriodsInArrears, "Rent is booked as arrears, never paid into a void.");
            Assert.AreEqual(1500, result.TotalArrearsCents);
            Assert.AreEqual(10000, cash.ReadCashCents("biz-test"), "The business cash is untouched when no landlord ledger exists.");
        }

        [Test]
        public void SettleDue_DoesNotSettle_WhenArrearsUnbookable()
        {
            CommercialLeaseMarket market = MarketWithActiveLease(out CommercialLeaseAgreement agreement);
            var cash = new FakeCashPort();
            cash.Balances["biz-test"] = 0;
            var landlordLedger = new HouseholdLedger(LandlordHouseholdId);
            var settlement = new CommercialLeaseRentSettlement();

            CommercialLeaseRentSettlementResult first = settlement.SettleDue(
                market, cash, personId => landlordLedger, null, null, 30, new List<string>());
            Assert.AreEqual(0, first.PeriodsSettled, "Rent that cannot be booked as arrears is not silently dropped.");
            Assert.AreEqual(0, agreement.LastSettledPeriodIndex, "The period stays unsettled.");

            CommercialLeaseRentSettlementResult second = settlement.SettleDue(
                market, cash, personId => landlordLedger, null, null, 30, new List<string>());
            Assert.AreEqual(0, second.PeriodsSettled, "The next pass retries the same period instead of skipping past it.");
        }

        [Test]
        public void SettleDue_SettlesMultiplePeriods()
        {
            CommercialLeaseMarket market = MarketWithActiveLease(out CommercialLeaseAgreement agreement);
            var cash = new FakeCashPort();
            cash.Balances["biz-test"] = 10000;
            var landlordLedger = new HouseholdLedger(LandlordHouseholdId);
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            CommercialLeaseRentSettlementResult result = new CommercialLeaseRentSettlement().SettleDue(
                market, cash, personId => landlordLedger, obligations, ids, 95, new List<string>());

            Assert.AreEqual(3, result.PeriodsSettled, "Periods 1, 2, and 3 (due days 30, 60, 90) all settle.");
            Assert.AreEqual(4500, result.TotalPaidCents);
            Assert.AreEqual(3, agreement.LastSettledPeriodIndex);
            Assert.AreEqual(5500, cash.ReadCashCents("biz-test"));
            Assert.AreEqual(4500, landlordLedger.GetBalanceCents());
        }

        // ---------- Driver: formation through a lease ----------

        private sealed class DriverFixture
        {
            public CommercialLeaseMarket Market = new CommercialLeaseMarket();
            public CommercialLeaseListing Listing;
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public FakeCreationPort Port = new FakeCreationPort();
            public List<string> Diag = new List<string>();

            public DriverFixture()
            {
                Listing = Market.ListBuilding(ShopBuildingId, ShopPlotId,
                    LandlordPersonId, "Landlord Name", 0, new List<string>());
                HouseholdLedger founderLedger = Ledgers.GetOrCreate(FounderHouseholdId);
                Assert.IsNull(founderLedger.RecordInflow(0, 100000, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Ledgers.GetOrCreate(LandlordHouseholdId);
            }

            public NpcEntrepreneurshipDriver.EntrepreneurshipContext Context()
            {
                var population = new PopulationState();
                population.people.Add(new PersonState
                {
                    id = FounderPersonId,
                    firstName = "Test",
                    lastName = "Founder",
                    ageBand = AgeBand.Adult18Plus,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                });
                var memberships = new HouseholdMembershipRegistry();
                Assert.IsNull(memberships.Register(new HouseholdMembership
                {
                    MembershipId = 1,
                    PersonId = FounderPersonId,
                    HouseholdId = FounderHouseholdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                }));
                Assert.IsNull(memberships.Register(new HouseholdMembership
                {
                    MembershipId = 2,
                    PersonId = LandlordPersonId,
                    HouseholdId = LandlordHouseholdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                }));
                var needs = new HouseholdNeedRegistry();
                HouseholdPurchasingNeed need = needs.CreateNeed(FounderHouseholdId, "flour", 0);
                need.UnitsNeeded = 3;
                var suppliers = new SupplierDirectory();
                suppliers.Register(new FakeItemSupplier());
                string listingId = Listing.ListingId;
                return new NpcEntrepreneurshipDriver.EntrepreneurshipContext
                {
                    Population = population,
                    Memberships = memberships,
                    Ledgers = Ledgers,
                    Needs = needs,
                    Schedules = new PersonScheduleTracker(),
                    Events = new NpcBusinessEventLog(),
                    CreationPort = Port,
                    Suppliers = suppliers,
                    PersonAlreadyOwnsBusiness = personId => false,
                    PremisesFinder = opportunity => new NpcPremisesOption
                    {
                        Description = "leased shop building " + ShopBuildingId,
                        Tenure = NpcPremisesTenure.Rented,
                        ParcelOrBuildingRef = "building:" + ShopBuildingId,
                        AgreementRef = listingId,
                        UpfrontCostCents = CommercialLeaseCalibration.DepositCents + CommercialLeaseCalibration.MonthlyRentCents,
                        RecurringCostCents = CommercialLeaseCalibration.MonthlyRentCents,
                    },
                    EquipmentFinder = opportunity => new NpcEquipmentOption
                    {
                        Description = "fitted-shop fixtures included in the lease of building " + ShopBuildingId,
                        ItemId = CommercialLeaseCalibration.LeasedFixturesItemId,
                        CostCents = 0,
                        Provenance = CommercialLeaseCalibration.LeasedFixturesProvenancePrefix + listingId +
                            " (fixtures included: counter, shelving, scale)",
                    },
                    LeaseMarket = Market,
                    UnassignedCommercialPremises = () => new List<CommercialLeasePremisesRef>(),
                };
            }
        }

        [Test]
        public void Driver_FormsBusinessThroughLease_DepositAndFirstRentMove_LeaseSigned()
        {
            var f = new DriverFixture();
            var driver = new NpcEntrepreneurshipDriver();
            NpcEntrepreneurshipDriver.EntrepreneurshipContext ctx = f.Context();

            // Three weekly drives: the flour need is observed on three
            // distinct days before recognition (3 observations, 2 days).
            driver.EvaluateWeek(8, ctx, f.Diag);
            driver.EvaluateWeek(9, ctx, f.Diag);
            driver.EvaluateWeek(10, ctx, f.Diag);

            Assert.AreEqual(1, f.Port.CreateCalls, "The lease path makes FormBusiness reachable: " + string.Join(" | ", f.Diag));
            NpcBusinessFormationIntent intent = f.Port.LastIntent;
            Assert.AreEqual(f.Listing.ListingId, intent.Premises.AgreementId);
            Assert.AreEqual(3000, intent.Premises.UpfrontCostCents, "Deposit 1500c + first month 1500c.");
            Assert.AreEqual(1500, intent.Premises.RecurringCostCents);
            Assert.AreEqual(0, intent.Assets.Count,
                "The lease's included fixtures are part of the premises, not a formation asset — they never liquidate.");
            Assert.AreEqual(97000, f.Ledgers.Get(FounderHouseholdId).GetBalanceCents(),
                "Founder pays 3000c (deposit + first month) and keeps the rest.");
            Assert.AreEqual(3000, f.Ledgers.Get(LandlordHouseholdId).GetBalanceCents(),
                "The landlord receives the deposit and the first month's rent.");
            Assert.AreEqual(1, f.Market.Agreements.Count);
            CommercialLeaseAgreement agreement = f.Market.Agreements[0];
            Assert.AreEqual(CommercialLeaseAgreementStatus.Active, agreement.Status);
            Assert.AreEqual(1500, agreement.DepositPaidCents);
            Assert.AreEqual(0, agreement.LastSettledPeriodIndex, "The first month was prepaid at signing.");
            Assert.AreEqual(CommercialLeaseListingStatus.Leased, f.Listing.Status);
        }

        [Test]
        public void Driver_CreationRefusal_RefundsDepositAndFirstRentInFull()
        {
            var f = new DriverFixture();
            f.Port.RefuseNext = true;
            var driver = new NpcEntrepreneurshipDriver();
            NpcEntrepreneurshipDriver.EntrepreneurshipContext ctx = f.Context();

            driver.EvaluateWeek(8, ctx, f.Diag);
            driver.EvaluateWeek(9, ctx, f.Diag);
            driver.EvaluateWeek(10, ctx, f.Diag);

            Assert.AreEqual(1, f.Port.CreateCalls, "The creation port was called and refused.");
            Assert.AreEqual(0, f.Market.Agreements.Count, "No business, no lease.");
            Assert.AreEqual(CommercialLeaseListingStatus.Active, f.Listing.Status);
            Assert.AreEqual(100000, f.Ledgers.Get(FounderHouseholdId).GetBalanceCents(),
                "A refused formation refunds the deposit and first rent in full.");
            Assert.AreEqual(0, f.Ledgers.Get(LandlordHouseholdId).GetBalanceCents());
        }

        // ---------- Lifecycle driver: close hook ----------

        [Test]
        public void LifecycleDriver_OnBusinessClosed_NotInvoked_ForStrugglingBusiness()
        {
            // B1's 8-week close lives on its own unmerged branch; in this
            // tree EvaluateWeek never closes, so the hook must not fire
            // for a business that only struggles. The positive path
            // (close -> release -> re-list) is a post-B1-merge check.
            var record = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = "biz-x",
                BusinessEntityKey = "business:biz-x",
                FounderPersonId = FounderPersonId,
                OwnerName = "Test Founder",
                State = NpcBusinessLifeState.Struggling,
                ConsecutiveStruggleWeeks = 7,
            };
            var info = new NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo
            {
                BusinessInstanceId = "biz-x",
                BusinessType = BusinessType.Generic,
                OwnerPersonId = FounderPersonId,
                OwnerName = "Test Founder",
                Business = null,
            };
            var cash = new FakeCashPort();
            cash.Balances["biz-x"] = 0;
            bool callbackInvoked = false;
            var driver = new NpcBusinessLifecycleDriver();
            driver.EvaluateWeek(new NpcBusinessLifecycleDriver.LifecycleDriverContext
            {
                LifeRecords = new List<NpcBusinessLifeRecord> { record },
                NpcBusinesses = new List<NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo> { info },
                Cash = cash,
                Obligations = new FinancialObligationAuthority(),
                Employments = new EmploymentRelationshipRegistry(),
                FounderLedgerByPersonId = personId => new HouseholdLedger(FounderHouseholdId),
                AssetRegister = new NpcFormationAssetRegister(),
                Liquidation = new FakeLiquidationPort(),
                Events = new NpcBusinessEventLog(),
                Ids = new EntityIdRegistry(),
                DayIndex = 10,
                OnBusinessClosed = (closedInfo, closedRecord) => { callbackInvoked = true; },
            }, new List<string>());

            Assert.IsFalse(callbackInvoked, "The close hook fires only on a close, never on a struggle week.");
            Assert.AreEqual(NpcBusinessLifeState.Struggling, record.State);
            Assert.AreEqual(8, record.ConsecutiveStruggleWeeks);
        }
    }
}
