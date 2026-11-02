using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Population.TownGrowth;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Series 2 Pass 10 (final harness): Roslyn acceptance over the
    /// Pass 6-9 production driver/binding/save classes only. The
    /// ThreeBusinessSlice / SixPersonSliceRun orchestrators are
    /// regression references only and are never the system under
    /// test here. All prices, quantities, conversions, thresholds,
    /// and freight charges are caller/test parameters, never canon.
    ///
    /// SHAPE TESTS, NOT A FIXTURE: the expanded-shape tests below
    /// use generic caller-supplied test inputs (household ids,
    /// person ids, test business ids). They are NEVER named Ethan,
    /// Tia, or Shane, and they are NEVER presented as the slice
    /// fixture. The named expanded fixture stays BLOCKED on
    /// Kennedy inputs (see Series2Pass10HarnessReview.md); no name,
    /// age, cash amount, house, horse, or wagon is invented here.
    ///
    /// Unity compilation, Test Runner, PlayMode, and scene wiring
    /// are NOT claimed. SaveLoadManager wiring for the Pass 10
    /// state is Codex work; only the coordinator overloads are
    /// proven here.
    /// </summary>
    [TestFixture]
    public sealed class Series2Pass10HarnessTests
    {
        private sealed class RuntimeCashPort : IBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
            public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;
            public string TryDebit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                return runtime.TrySpendCents(amountCents) ? null : "insufficient business cash";
            }
            public string Credit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                runtime.AddCashCents(amountCents);
                return null;
            }
        }

        private sealed class FakeTradeCreditPort : ITradeCreditPort
        {
            public string ObligationIdToReturn = "obl-test-1";
            public string CreateTradeCredit(
                string debtor, string creditor, int amountCents, int dayIndex,
                string terms, string reason, string agreementId, string paymentId)
            {
                return ObligationIdToReturn;
            }
        }

        private sealed class FakeFoodSeller : IPass4FoodSeller
        {
            public string LocationId { get; set; } = "shop-1";
            public bool IsOpen { get; set; } = true;
            public bool HasFood { get; set; } = true;
            public int FoodPriceCents { get; set; } = 40;
            public bool OffersTradeCredit { get; set; } = false;
            public int TradeCreditLimitCents { get; set; } = 0;
        }

        private sealed class FakeLodging : IPass4Lodging
        {
            public string LocationId { get; set; } = "inn-1";
            public bool IsAvailable { get; set; } = true;
            public int NightPriceCents { get; set; } = 60;
            public bool OffersTradeCredit { get; set; } = false;
            public int TradeCreditLimitCents { get; set; } = 0;
        }

        private sealed class FakeRoute : IPass4RouteProvider
        {
            public int Minutes = 15;
            public bool TryRoute(string fromLocationId, string toLocationId, out int routeMinutes, out string reason)
            {
                routeMinutes = Minutes;
                reason = null;
                return true;
            }
        }

        private sealed class FakeMigrationHolder : IPassXlMigrationFlagHolder
        {
            public bool SuppressMigrantArrivals { get; set; }
        }

        private sealed class FakePlayerHolder : IPassXlPlayerFlagHolder
        {
            public bool TreatPlayerAsNpc { get; set; }
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
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

        // ---- shared bread setup (Pass 6 pattern) -------------------

        private sealed class BreadSetup
        {
            public List<string> Diag = new List<string>();
            public ExternalMarketAuthority Market = new ExternalMarketAuthority();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public OffMapNamedSkuProcurementService StoreService;
            public Series2FlourProcurementDriver Procurement;
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public EntityIdRegistry Ids = new EntityIdRegistry();
        }

        private static BreadSetup NewBreadSetup(int storeCash, int bakeryCash)
        {
            var s = new BreadSetup();
            s.StoreRuntime.BindBusinessIdentity("general-store-1");
            s.StoreRuntime.AddCashCents(storeCash);
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(bakeryCash);
            s.StoreService = new OffMapNamedSkuProcurementService(
                "general-store-1", s.Market, new RuntimeCashPort(s.StoreRuntime));
            s.Procurement = new Series2FlourProcurementDriver(s.StoreService);
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            return s;
        }

        private static Series2CounterpartyRegistration Registration(
            string supplierId, string skuId, int unitCost, int freight, int supply)
        {
            var reg = new Series2CounterpartyRegistration
            {
                SupplierId = supplierId,
                SupplierName = supplierId + " Co.",
                SkuId = skuId,
                UnitGoodsCostCents = unitCost,
                FreightSurchargeCents = freight,
                InitialBudgetCents = 50000,
                TransitDays = 1,
            };
            reg.SupplyUnits[skuId] = supply;
            return reg;
        }

        private static void OrderAndReceive(BreadSetup s, Series2CounterpartyRegistration reg, int units, int orderDay)
        {
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "counterparty registration should succeed");
            var notions = new Series2NotionsSupply(s.Procurement);
            NamedSkuOrder order;
            if (reg.SkuId == "notions")
                order = notions.PlaceNotionsOrder(reg, units, orderDay, NamedSkuPaymentMode.CashOnArrival, null, s.Diag);
            else
            {
                OffMapFreightQuote quote = Series2ProcurementQuoteBuilder.Build(
                    reg, units, FreightOptionKind.SellerFreight, s.Diag);
                Assert.IsNotNull(quote);
                order = s.Procurement.PlaceOrderFromQuote(
                    quote, orderDay, NamedSkuPaymentMode.CashOnArrival, null, s.Diag);
            }
            Assert.IsNotNull(order, "order should be placed; diag: " + string.Join("; ", s.Diag));
            List<NamedSkuLot> lots = s.Procurement.ReceiveArrivals(orderDay + 1, null, s.Diag);
            Assert.AreEqual(1, lots.Count, "one lot should arrive; diag: " + string.Join("; ", s.Diag));
        }

        private static void SeedBakeryInputs(BreadSetup s, int flourLb, int notions, int fuel)
        {
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = flourLb,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = notions,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = fuel,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
        }

        private static BakeryShopRuntime BakeOneBatch(int bakeryCash, int bakeDay, out BusinessRuntimeState bakeryRuntime, out EntityIdRegistry ids)
        {
            var diag = new List<string>();
            bakeryRuntime = new BusinessRuntimeState();
            bakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            bakeryRuntime.AddCashCents(bakeryCash);
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            ids = new EntityIdRegistry();
            Assert.IsNull(bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            var setup = new BreadSetup { Bakery = bakery, Ids = ids, Diag = diag };
            SeedBakeryInputs(setup, 24, 2, 2);
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, bakeDay, diag));
            Assert.AreEqual(1, driver.RunScheduledDay(
                bakeDay, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag, ids),
                "one batch should bake; diag: " + string.Join("; ", diag));
            return bakery;
        }

        // ---- (a) town gates ----------------------------------------

        [Test]
        public void TownGates_DefaultsAreLiveBehaviour_ArrivalsSuppressed_DeparturesOnLegacyRoll()
        {
            var gates = new Series2SliceTownGates();
            Assert.IsTrue(gates.SuppressSliceArrivals, "slice arrivals are suppressed by default");
            Assert.AreEqual(Pass5SliceDepartureGateDecision.Undecided, gates.DepartureGateDecision);
            Assert.IsFalse(gates.RetireLegacyRoll);
            Assert.IsNull(gates.Validate(), "the live defaults must be coherent");

            var inputs = new Pass5TownInputs
            {
                HousingVacantUnits = 2,
                HousingTotalUnits = 4,
                OpenJobSlots = 3,
                FilledJobSlots = 1,
                GrowthScore01 = 0.9,
                GrowthSupplied = true,
                OpinionScore01 = 0.9,
                OpinionSupplied = true,
                SnapshotDayIndex = 0,
                CurrentDayIndex = 1,
            };
            gates.ApplyTo(inputs);
            Pass5TownEvaluation eval = Pass5TownDriverModel.Evaluate(inputs);
            Assert.IsFalse(eval.ArrivalsAllowed,
                "conditions pass, but slice suppression must still block arrivals");
            Assert.IsTrue(eval.LegacyRollStillLive, "the legacy roll is never retired by Evaluate");
            StringAssert.Contains("UNDECIDED", eval.DepartureGateState,
                "undecided means departures stay live on the legacy roll");
            Assert.IsTrue(gates.Describe().Any(l => l.Contains("SUPPRESSED")));

            // The same conditions WITHOUT suppression would allow
            // arrivals, proving suppression is the switch, not the
            // conditions.
            var open = new Series2SliceTownGates { SuppressSliceArrivals = false };
            var openInputs = new Pass5TownInputs
            {
                HousingVacantUnits = 2,
                HousingTotalUnits = 4,
                OpenJobSlots = 3,
                FilledJobSlots = 1,
                GrowthScore01 = 0.9,
                GrowthSupplied = true,
                OpinionScore01 = 0.9,
                OpinionSupplied = true,
                SnapshotDayIndex = 0,
                CurrentDayIndex = 1,
            };
            open.ApplyTo(openInputs);
            Assert.IsTrue(Pass5TownDriverModel.Evaluate(openInputs).ArrivalsAllowed,
                "without suppression the passing conditions allow arrivals");

            // Legacy departures are preserved untracked, never
            // rewritten with an invented destination.
            Pass5DepartureRecord legacy = Pass5DepartureModel.PreserveLegacyDeparture(7);
            Assert.AreEqual(Pass5DepartureDestinationKind.LegacyUntracked, legacy.DestinationKind);
            Assert.AreEqual(string.Empty, legacy.DestinationName);
            // And the new channel refuses an untracked destination.
            Pass5DepartureRecord refused = Pass5DepartureModel.DepartNewChannel(
                7, Pass5DepartureDestinationKind.LegacyUntracked, null, false);
            Assert.IsTrue(refused.Reasons.Any(r => r.Contains("refused")),
                "a new-channel departure without a destination must refuse");

            // No newcomer arrives through an undriven channel with
            // no housing, no terms, and no explicit unemployed
            // plan flag.
            var readiness = Pass5SettlementReadinessModel.Check(new Pass5SettlementInputs());
            Assert.IsFalse(readiness.Ready, "no housing + no terms + no plan flag must refuse settlement");
            var withPlan = Pass5SettlementReadinessModel.Check(new Pass5SettlementInputs
            {
                HousingUnitId = "house-test-1",
                HousingVacancies = 1,
                ExplicitUnemployedWithPlan = true,
            });
            Assert.IsTrue(withPlan.EmploymentPending,
                "the explicit unemployed-with-plan flag is the only no-terms path, and it stays pending, never invented employment");
        }

        [Test]
        public void TownGates_ActivationIsKennedysOneLine_AndRetirementWithoutDecisionRefuses()
        {
            var half = new Series2SliceTownGates { RetireLegacyRoll = true };
            Assert.IsNotNull(half.Validate(),
                "retirement without a departure decision must refuse, never assume one");

            var activated = new Series2SliceTownGates
            {
                DepartureGateDecision = Pass5SliceDepartureGateDecision.ActivateGate,
                RetireLegacyRoll = true,
            };
            Assert.IsNull(activated.Validate(), "a decided activation must validate");
            var inputs = new Pass5TownInputs();
            activated.ApplyTo(inputs);
            Pass5TownEvaluation eval = Pass5TownDriverModel.Evaluate(inputs);
            StringAssert.Contains("ACTIVATED", eval.DepartureGateState);
            // Retirement itself is a separate driver step, never
            // applied by the gates object or by Evaluate.
            Assert.IsTrue(eval.LegacyRollStillLive,
                "the gates object never retires the roll by itself; the driver wires retirement only on Kennedy's decision");
            Pass5TownEvaluation retired = Pass5TownDriverModel.WithLegacyRollRetired(eval);
            Assert.IsFalse(retired.LegacyRollStillLive);
        }

        // ---- (b) Pass 5 save adoption ------------------------------

        private static Series2Pass10SaveState NewPass10StateWithSample()
        {
            var state = new Series2Pass10SaveState();
            var enriched = new Pass5EnrichedLead
            {
                ContactPersonId = 11,
                ContactName = "Test Contact",
                RecommenderPersonId = 3,
                RecommenderName = "Test Recommender",
                BusinessId = "general-store-1",
                RoleId = "clerk",
                RoleDisplayName = "Clerk",
                Location = new Pass5OffMapLocation { TownName = "Test Town", PostalDestinationId = "test-postal" },
                ProfessionId = "clerk",
                ProfessionName = "Clerk",
                Disposition = Pass5ContactDisposition.SeekingChange,
                HasCurrentWageCents = true,
                CurrentWageCents = 4200,
                DayIndex = 2,
            };
            state.Leads.Add(Pass5LeadSaveRecord.FromLead(enriched));
            state.Visits.Add(new Pass5TravelerVisitRecord
            {
                TravelerId = "traveler-2-0",
                TravelerName = string.Empty,
                LookingForStay = false,
                BoardingVacancies = 1,
                DayIndex = 2,
                StayEligible = false,
                NextStep = "visitor-only",
            });
            state.Chains.Add(new Pass5ChainRecord
            {
                LeadContactPersonId = 11,
                Step = Pass5ConversationStep.ReplyPending,
                DayIndex = 2,
                PostagePaid = true,
                LetterOrigin = "general-store-1",
                PostalDestination = "test-postal",
            });
            return state;
        }

        [Test]
        public void Pass10Save_LeadsVisitsChains_DoubleRestore_Idempotent_AndOldSaveRestoresEmpty()
        {
            var diag = new List<string>();
            Series2Pass10SaveState source = NewPass10StateWithSample();
            var eval = Pass5TownDriverModel.Evaluate(new Pass5TownInputs
            {
                HousingVacantUnits = 2,
                HousingTotalUnits = 4,
                OpenJobSlots = 3,
                FilledJobSlots = 1,
                GrowthScore01 = 0.9,
                GrowthSupplied = true,
                OpinionScore01 = 0.9,
                OpinionSupplied = true,
                SnapshotDayIndex = 0,
                CurrentDayIndex = 1,
                SliceSuppressArrivals = true,
            });
            source.Snapshot = Series2Pass10TownSnapshotDto.FromEvaluation(eval, 1);
            Series2Pass10SaveDto dto = source.Capture(diag);
            Assert.AreEqual(1, dto.Leads.Count);
            Assert.AreEqual(1, dto.Visits.Count);
            Assert.AreEqual(1, dto.Chains.Count);
            Assert.IsNotNull(dto.Snapshot);

            var target = new Series2Pass10SaveState();
            target.Restore(dto, diag);
            target.Restore(dto, diag);
            Assert.AreEqual(1, target.Leads.Count, "a second restore must not double leads");
            Assert.AreEqual(1, target.Visits.Count, "a second restore must not double visits");
            Assert.AreEqual(1, target.Chains.Count, "a second restore must not double chain records");
            Pass5LeadSaveRecord lead = target.Leads[0];
            Assert.AreEqual(11, lead.ContactPersonId);
            Assert.AreEqual("Test Contact", lead.ContactName);
            Assert.AreEqual("general-store-1", lead.BusinessId);
            Assert.AreEqual((int)Pass5ContactDisposition.SeekingChange, lead.Disposition);
            Assert.AreEqual(4200, lead.CurrentWageCents);
            Pass5EnrichedLead roundTripped = lead.ToLead();
            Assert.AreEqual("Test Town", roundTripped.Location.TownName);
            Assert.AreEqual("test-postal", roundTripped.Location.PostalDestinationId);
            Assert.AreEqual("traveler-2-0", target.Visits[0].TravelerId);
            Assert.AreEqual("visitor-only", target.Visits[0].NextStep);
            Assert.AreEqual(Pass5ConversationStep.ReplyPending, target.Chains[0].Step);
            Assert.IsTrue(target.Chains[0].PostagePaid, "caller-supplied chain state restores verbatim; nothing is invented");
            Assert.IsNotNull(target.Snapshot);
            Assert.IsFalse(target.Snapshot.ArrivalsAllowed, "the slice-suppressed snapshot restores suppressed");
            Assert.IsTrue(target.Snapshot.LegacyRollStillLive);

            // Coordinator wiring: the Pass 10 section rides the
            // slice DTO next to the untouched Pass 9 section.
            var pass9State = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() };
            PassXlSliceSaveDto sliceDto = PassXlSliceSaveCoordinator.Capture(
                new FakeMigrationHolder(), new FakePlayerHolder(), null, pass9State, source);
            Assert.IsNotNull(sliceDto.series2Pass10, "coordinator must carry the Pass 10 section");
            Assert.AreEqual(1, sliceDto.series2Pass10.Leads.Count);
            var coordTarget = new Series2Pass10SaveState();
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                sliceDto, new FakeMigrationHolder(), new FakePlayerHolder(), null,
                new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() }, null, coordTarget);
            Assert.AreEqual(1, coordTarget.Leads.Count);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("Series2Pass10Save: restore complete")));

            // An old save with no Pass 10 section restores empty,
            // diagnosed, never crashed, never inventing state.
            var oldTarget = NewPass10StateWithSample();
            oldTarget.Snapshot = Series2Pass10TownSnapshotDto.FromEvaluation(eval, 1);
            List<string> oldDiag = PassXlSliceSaveCoordinator.Restore(
                new PassXlSliceSaveDto(), new FakeMigrationHolder(), new FakePlayerHolder(), null,
                new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() }, null, oldTarget);
            Assert.AreEqual(0, oldTarget.Leads.Count);
            Assert.AreEqual(0, oldTarget.Visits.Count);
            Assert.AreEqual(0, oldTarget.Chains.Count);
            Assert.IsNull(oldTarget.Snapshot, "an old save has no snapshot; null is the honest state, never a placeholder score");
            Assert.IsTrue(oldDiag.Any(d => d.Contains("no Pass 10 section")));
        }

        [Test]
        public void Pass10Save_SnapshotNull_RestoresAsNoSnapshot_NotAPlaceholderScore()
        {
            var diag = new List<string>();
            var source = new Series2Pass10SaveState();
            Series2Pass10SaveDto dto = source.Capture(diag);
            Assert.IsNull(dto.Snapshot, "no caller-supplied snapshot means a null snapshot, never a default score");
            var target = new Series2Pass10SaveState
            {
                Snapshot = new Series2Pass10TownSnapshotDto { ReputationScore01 = 0.9 },
            };
            target.Restore(dto, diag);
            Assert.IsNull(target.Snapshot, "clear-then-load must clear a stale snapshot, never keep it");
        }

        // ---- (d) the missing Pass 3 double-restore assertion ------

        [Test]
        public void Pass3_DoubleRestore_NowAsserted_DraftsOrdersSettlementsDoNotDouble()
        {
            var diag = new List<string>();
            var catalog = new Pass3StoreCatalog();
            Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine
            {
                ProductId = "bread", DisplayName = "Bread", CostCents = 80, PriceCents = 100,
            }, diag));
            catalog.Find("bread").ShelfUnits = 5;
            var supplier = new Pass3SupplierOffer
            {
                SupplierId = "grain-1",
                SupplierName = "Grain Co.",
                DistanceMiles = 10,
                MinimumQuantityUnits = 1,
            };
            supplier.SkuOffers.Add(new Pass3SkuOffer { SkuId = "flour", UnitPriceCents = 10, UnitWeightLb = 10 });
            var draft = new Pass3OrderDraft
            {
                Supplier = supplier,
                FreightOption = Pass3FreightOption.SellerFreight,
                FreightCentsPerMile = 5,
            };
            Assert.IsTrue(draft.AddLine("flour", 20, diag));
            List<Pass3OrderRequest> placed = draft.Place(diag);
            Assert.IsNotNull(placed);
            var intake = new Pass3WholesaleIntake();
            Assert.IsNotNull(intake.Receive("lot-1", "bread", 10, 5, 0, 0, new Pass3StoreCatalog(),
                "general-store-1", "bakery-1", diag));
            var settled = new Pass3WholesaleSettlement
            {
                BakeryLotId = "lot-1", ProductId = "bread", Units = 10, UnitWholesalePriceCents = 50,
                PayerBusinessId = "bakery-1", PayeeBusinessId = "general-store-1",
                AmountCents = 500, Settled = true,
            };

            var source = new Series2Pass9SaveState { Catalog = catalog };
            source.Drafts.Add(draft);
            source.PlacedRequests.AddRange(placed);
            source.WholesaleIntake = intake;
            source.WholesaleSettlements.Add(settled);
            Series2Pass9SaveDto dto = source.Capture(diag);

            var target = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() };
            target.WholesaleIntake = new Pass3WholesaleIntake();
            target.Restore(dto, null, diag);
            target.Restore(dto, null, diag);
            Assert.AreEqual(1, target.Drafts.Count, "the second restore must not double drafts");
            Assert.AreEqual(1, target.PlacedRequests.Count, "the second restore must not double placed requests");
            Assert.AreEqual(1, target.WholesaleSettlements.Count, "the second restore must not double settlements");
            Assert.AreEqual(5, target.Catalog.Find("bread").ShelfUnits);
            Assert.IsNull(target.WholesaleIntake.Receive(
                "lot-1", "bread", 10, 5, 0, 0, new Pass3StoreCatalog(),
                "general-store-1", "bakery-1", diag),
                "the delivered set must still refuse a re-intake after the second restore");
        }

        // ---- bread chain -------------------------------------------

        [Test]
        public void BreadChain_OrderFeedBakeShelfSale_ReconcilesExactly()
        {
            var s = NewBreadSetup(storeCash: 5000, bakeryCash: 2000);
            OrderAndReceive(s, Registration("test-grain", "flour", 10, 250, 200), 20, 0);
            Assert.AreEqual(4550, s.StoreRuntime.CurrentCashCents, "store pays 20u x 10c + 250c freight on arrival");
            OrderAndReceive(s, Registration("test-notions", "notions", 10, 0, 50), 5, 0);
            Assert.AreEqual(4500, s.StoreRuntime.CurrentCashCents, "store pays 5u x 10c notions on arrival");

            // Caller-supplied flour-unit conversion, asserted, not
            // defaulted: 10 lb sacks, 2 store units = 20 lb.
            var conversion = new Series2FlourUnitConversion
            {
                LbPerStoreUnit = 10,
                StoreUnitDescription = "10 lb sack",
                ExpectedLbPerNamedUnit = 10,
            };
            Assert.IsNull(Series2StoreToBakeryFeed.FeedFlour(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "flour", 2, conversion, 20, 2, s.Diag), "flour feed should succeed");
            Assert.AreEqual(20, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
            Assert.AreEqual(4540, s.StoreRuntime.CurrentCashCents, "store gains 2u x 20c feed price");
            Assert.AreEqual(1960, s.BakeryRuntime.CurrentCashCents, "bakery pays 40c feed price");
            var notions = new Series2NotionsSupply(s.Procurement);
            Assert.IsNull(notions.FeedNotionsToBakery(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "notions", 1, 1, 10, 2, s.Diag), "notions feed should succeed");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId));

            // Fuel for the bake comes from the in-stock store here;
            // the dealer-fuelled bake is proven in the fuel test.
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            Assert.AreEqual(1, driver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids),
                "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count);
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "20 lb fed minus 12 lb baked");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "1 notion baked");

            // Shelf sale through the real adapter: fresh price 8c.
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new RuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 2;
            Assert.AreEqual(10, adapter.SellItem("bread", 10, 2,
                new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger }, s.Diag));
            Assert.AreEqual(1950 + 80, s.BakeryRuntime.CurrentCashCents, "bakery cash gains exactly 10 x 8c");
            Assert.AreEqual(14, s.Bakery.BreadShelf[0].UnitCount, "shelf loses exactly the units sold");
            Assert.AreEqual(4550, s.StoreRuntime.CurrentCashCents, "baking and bakery sale move no store cash");
        }

        // ---- DayIndex / pre-checks ---------------------------------

        [Test]
        public void DayIndex_PlannedDayRestoresVerbatim_AdapterDayDoesNot_EarlyRefuses_LateCatchUpDiagnosed()
        {
            var diag = new List<string>();
            BakeryShopRuntime bakery = BakeOneBatch(2000, 2, out BusinessRuntimeState bakeryRuntime, out EntityIdRegistry ids);
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));

            // Planned day restores verbatim through Pass 9 save.
            var planDriver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            Assert.IsNotNull(planDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, diag));
            var source = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog(), BakeryDriver = planDriver };
            Series2Pass9SaveDto dto = source.Capture(diag);
            Assert.AreEqual(2, dto.BakeryPlannedDayIndex);
            var target = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() };
            target.BakeryDriver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            target.Restore(dto, null, diag);
            Assert.AreEqual(2, target.BakeryDriver.PlannedDayIndex, "bakery PlannedDayIndex restores verbatim");
            Assert.AreEqual(1, target.BakeryDriver.PlannedBatchIds.Count);

            // The adapter DayIndex is deliberately NOT restored: a
            // fresh adapter refuses trading until the day driver
            // sets it at day start. That re-supply is a driver
            // obligation, not a silent default.
            var freshAdapter = new Series2BakerySupplierAdapter(
                bakery, new RuntimeCashPort(bakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            Assert.AreEqual(-1, freshAdapter.DayIndex, "a fresh adapter starts with no day set");
            Assert.AreEqual(0, freshAdapter.SellItem("bread", 5, 2,
                new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger }, diag),
                "with no day set, trading must refuse rather than price at a stale day");
            freshAdapter.DayIndex = 2;
            Assert.IsTrue(freshAdapter.ItemPricePerUnitCents("bread") > 0,
                "once the day driver sets the day, trading resumes");

            // Early-day refusal and late-day diagnosed catch-up.
            var earlyBakery = new BakeryShopRuntime("pierce-bakery-1");
            var earlyIds = new EntityIdRegistry();
            var earlyDiag = new List<string>();
            Assert.IsNull(earlyBakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, earlyDiag));
            var earlySetup = new BreadSetup { Bakery = earlyBakery, Ids = earlyIds, Diag = earlyDiag };
            SeedBakeryInputs(earlySetup, 24, 2, 2);
            var earlyDriver = new Series2BakeryProductionDriver(
                earlyBakery, new Series2InStockBakeryFuelSource(earlyBakery));
            Assert.IsNotNull(earlyDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 5, earlyDiag));
            Assert.AreEqual(-1, earlyDriver.RunScheduledDay(
                4, true, WorkstationCatalog.BakeOven, OvenFinder(), null, earlyDiag, earlyIds),
                "nothing bakes early");
            Assert.AreEqual(0, earlyBakery.BreadShelf.Count);

            var lateBakery = new BakeryShopRuntime("pierce-bakery-1");
            var lateIds = new EntityIdRegistry();
            var lateDiag = new List<string>();
            Assert.IsNull(lateBakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, lateDiag));
            var lateSetup = new BreadSetup { Bakery = lateBakery, Ids = lateIds, Diag = lateDiag };
            SeedBakeryInputs(lateSetup, 24, 2, 2);
            var lateDriver = new Series2BakeryProductionDriver(
                lateBakery, new Series2InStockBakeryFuelSource(lateBakery));
            Assert.IsNotNull(lateDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, lateDiag));
            Assert.AreEqual(1, lateDriver.RunScheduledDay(
                5, true, WorkstationCatalog.BakeOven, OvenFinder(), null, lateDiag, lateIds),
                "a late run is a diagnosed catch-up, not a refusal; diag: " + string.Join("; ", lateDiag));
            Assert.IsTrue(lateDiag.Any(d => d.Contains("catch-up")),
                "the late bake must be diagnosed as catch-up, never hidden");
            // No day-equality is asserted: the code does not
            // enforce baked-day == planned-day, and this harness
            // does not pretend it does.
        }

        [Test]
        public void Precheck_GatesFlourNotionsFuelOvenAndKit_Loudly()
        {
            var diag = new List<string>();
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            var ids = new EntityIdRegistry();
            Assert.IsNull(bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            var setup = new BreadSetup { Bakery = bakery, Ids = ids, Diag = diag };
            SeedBakeryInputs(setup, 24, 2, 2);
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, diag));

            Assert.IsNull(driver.Precheck(2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag),
                "all inputs present must pass the pre-check; diag: " + string.Join("; ", diag));
            Assert.IsNotNull(driver.Precheck(2, false, WorkstationCatalog.BakeOven, OvenFinder(), null, diag),
                "an unusable baker's kit must refuse loudly");
            var noOven = new BakeryShopRuntime("pierce-bakery-1");
            var noOvenDriver = new Series2BakeryProductionDriver(
                noOven, new Series2InStockBakeryFuelSource(noOven));
            Assert.IsNotNull(noOvenDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, diag));
            Assert.IsNotNull(noOvenDriver.Precheck(2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag),
                "no ready oven must refuse loudly");
        }

        // ---- fuel chain --------------------------------------------

        private static SlabOffcutFuelLot SawmillLot(EntityIdRegistry ids, string mill, int units, int day)
        {
            return new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = units,
                SourceMillBusinessId = mill,
                SourceLogLotId = "log-lot-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = day,
            };
        }

        [Test]
        public void FuelChain_IntakeFifoDispense_BakeryFiring_AndHouseholdReserve_Reconcile()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var intake = new FuelYardByproductIntake();
            var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
            Assert.IsNull(dealer.ReceiveSawmillLot(SawmillLot(ids, "mill-1", 10, 1), diag));
            Assert.IsNull(dealer.ReceiveSawmillLot(SawmillLot(ids, "mill-1", 10, 2), diag));
            Assert.AreEqual(20, dealer.StockUnits);
            Assert.IsTrue(diag.Any(d => d.Contains("no sawmill payment")),
                "sawmill receipt must diagnose that no payment was made, not price the byproduct at 0c");

            // FIFO with provenance through the real dispense API.
            List<FuelYardByproductIntake.FuelYardDispenseLine> lines =
                intake.TryDispenseUnits("fuel-dealer-1", 15, 3, diag);
            Assert.IsNotNull(lines);
            Assert.AreEqual(2, lines.Count, "FIFO across two lots");
            Assert.AreEqual(10, lines[0].UnitsTaken);
            Assert.AreEqual(5, lines[1].UnitsTaken);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(lines[0].ProvenanceChain), "provenance must survive the dispense");
            Assert.AreEqual(5, dealer.StockUnits);
            Assert.IsNull(intake.TryDispenseUnits("fuel-dealer-1", 6, 3, diag),
                "shortfall dispenses nothing, never a partial lot");
            Assert.AreEqual(5, dealer.StockUnits);
            Assert.IsNotNull(intake.TryDispenseUnits("fuel-dealer-1", 5, 3, diag));
            Assert.AreEqual(0, dealer.StockUnits, "emptied lots are gone, never re-sold");
            Assert.IsNull(intake.TryDispenseUnits("fuel-dealer-1", 1, 3, diag));

            // Dealer -> bakery lot, then a fired batch through the
            // real driver with the dealer fuel source.
            var dealer2 = new Series2FuelDealer(new FuelYardByproductIntake(), "fuel-dealer-1");
            Assert.IsNull(dealer2.ReceiveSawmillLot(SawmillLot(ids, "mill-1", 20, 1), diag));
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            var source = new Series2DealerBakeryFuelSource(dealer2, 1, null, ids);
            Assert.IsNull(source.SupplyTo(bakery, 6, 5, diag));
            Assert.AreEqual(6, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            Assert.AreEqual(14, dealer2.StockUnits);
            Assert.AreEqual("fuel-dealer-1", bakery.FuelStock.Lots[0].FuelDealerBusinessId);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(bakery.FuelStock.Lots[0].SourceFuelLotId),
                "dealer lot reference must survive into the bakery lot");
            Assert.IsNull(bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            var fuelSetup = new BreadSetup { Bakery = bakery, Ids = ids, Diag = diag };
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 24,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, diag));
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, diag));
            var fuelDriver = new Series2BakeryProductionDriver(bakery, source);
            Assert.IsNotNull(fuelDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 5, diag));
            Assert.AreEqual(1, fuelDriver.RunScheduledDay(
                5, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag, ids),
                "the dealer-fuelled batch must fire; diag: " + string.Join("; ", diag));
            Assert.AreEqual(5, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "6 fuel units minus 1 firing");

            // Separately, dealer stock -> household fuel_wood
            // reserve, cash settled through the production port.
            var dealerRuntime = new BusinessRuntimeState();
            dealerRuntime.BindBusinessIdentity("fuel-dealer-1");
            dealerRuntime.AddCashCents(0);
            var adapter = new Series2FuelDealerSupplierAdapter(
                dealer2.Intake, new Series2BusinessRuntimeCashPort(dealerRuntime),
                "fuel-dealer-1", "Fuel Dealer", "yard-1", 28);
            Assert.AreEqual(4, adapter.SellItem(HouseholdItemCatalog.FuelWoodId, 4, 7,
                new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger }, diag));
            Assert.AreEqual(112, dealerRuntime.CurrentCashCents, "4u x caller price 28c through the production port");
            Assert.AreEqual(10, dealer2.StockUnits, "adapter sale dispensed real intake stock");
            var household = new HouseholdState { id = 99, householdName = "Test Household" };
            household.EnsureHouseholdReservesInitialized();
            int credited = Series2HouseholdFuelReserveLink.CreditReserve(household, 4, 2, 7, diag);
            Assert.AreEqual(8, credited, "conversion factor 2 is a caller parameter, honoured here, not a game rate");
            Assert.AreEqual(8, household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId).currentUnits);
        }

        // ---- freight legs ------------------------------------------

        private sealed class CarrierSetup
        {
            public List<string> Diag = new List<string>();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public FreightShipmentService Service = new FreightShipmentService();
            public FreightProductionDriver Driver;
            public Series2FreightProductionCarrier Carrier;
            public FreightShipmentHooks Hooks;
            public EntityId Handler = EntityId.For(EntityKind.Person, 701);
            public EntityId DriverId = EntityId.For(EntityKind.Person, 702);
            public List<EntityId> Team = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            public Dictionary<string, int> RecipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            public BusinessRuntimeState CarrierRuntime = new BusinessRuntimeState();
        }

        private static CarrierSetup NewCarrierSetup()
        {
            var s = new CarrierSetup();
            s.Budgets.EnsureDay(42);
            s.Hooks = new FreightShipmentHooks
            {
                Tasks = s.Tasks,
                Budgets = s.Budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    string key = shipment.destinationBusinessInstanceId ?? "nowhere";
                    s.RecipientStock[key] = s.RecipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            s.Service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1",
                Kind = "farm-wagon",
                DisplayName = "wagon-1",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            });
            s.Driver = new FreightProductionDriver(s.Service);
            s.CarrierRuntime.BindBusinessIdentity("freight-co-1");
            s.Driver.EnsureCompany("freight-co-1", s.CarrierRuntime);
            s.Carrier = new Series2FreightProductionCarrier(s.Driver, "freight-co-1");
            return s;
        }

        private static void TickUntil(CarrierSetup s, Func<bool> done)
        {
            int guard = 0;
            while (!done() && guard++ < 60)
                s.Carrier.AdvanceTick(3600f, s.Hooks, s.Handler, s.Diag);
        }

        private static LogisticsShipmentState RunLeg(
            CarrierSetup s, Series2CarrierLeg leg, string recipientKey, int expectedUnits)
        {
            leg.DriverId = s.DriverId;
            leg.AnimalIds = s.Team;
            int before = s.RecipientStock.TryGetValue(recipientKey, out int have) ? have : 0;
            Series2CarrierLegResult created = s.Carrier.RequestLeg(leg, s.Hooks, s.Diag);
            Assert.AreEqual(string.Empty, created.Refusal, created.Refusal);
            Assert.IsNotNull(created.Shipment);
            TickUntil(s, () => created.Shipment.Status == LogisticsShipmentStatus.Completed);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, created.Shipment.Status, string.Join("; ", s.Diag));
            Assert.AreEqual(expectedUnits, created.Shipment.DeliveredQuantityUnits);
            Assert.AreEqual(before + expectedUnits, s.RecipientStock[recipientKey], "acceptance through the callback, exactly once (cumulative per recipient)");
            return created.Shipment;
        }

        [Test]
        public void FreightLegs_AllFiveSliceLegs_CompleteSettleOnce_AndSurviveSaveRestore()
        {
            var s = NewCarrierSetup();
            var shipments = new List<LogisticsShipmentState>
            {
                RunLeg(s, Series2SliceLegs.OffMapArrivalToBusiness(
                    "ship-arr-10", "freight-co-1", "offmap-flour-co", "general-store-1", "flour", 20, 250, "wagon-1"),
                    "general-store-1", 20),
                RunLeg(s, Series2SliceLegs.StoreToBakery(
                    "ship-sb-10", "freight-co-1", "general-store-1", "bakery-1", 5, 100, "wagon-1"),
                    "bakery-1", 5),
                RunLeg(s, Series2SliceLegs.BakeryToStoreWholesale(
                    "ship-ws-10", "freight-co-1", "bakery-1", "general-store-1", 24, 120, "wagon-1"),
                    "general-store-1", 24),
                RunLeg(s, Series2SliceLegs.DealerToBakery(
                    "ship-db-10", "freight-co-1", "fuel-dealer-1", "bakery-1", 10, 250, "wagon-1"),
                    "bakery-1", 10),
                // Household-leg freight payer is a CALLER PARAMETER
                // in this harness (the payer runtime below). The
                // dealer-borne vs priced-into-retail split is still
                // undecided, and this test asserts neither.
                RunLeg(s, Series2SliceLegs.DealerToHousehold(
                    "ship-dh-10", "freight-co-1", "fuel-dealer-1", 42, 5, 100, "wagon-1"),
                    "household-42", 5),
            };
            Assert.AreEqual(5, shipments.Count, "all five slice legs must complete");

            var settlement = new Series2CarrierSettlement();
            var carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity("freight-co-1");
            int[] charges = { 250, 100, 120, 250, 100 };
            for (int i = 0; i < shipments.Count; i++)
            {
                var payerRuntime = new BusinessRuntimeState();
                payerRuntime.BindBusinessIdentity("test-payer-" + i);
                payerRuntime.AddCashCents(10000);
                Series2CarrierSettlementResult settled = settlement.SettleOnAcceptance(
                    shipments[i], new RuntimeCashPort(payerRuntime),
                    new RuntimeCashPort(carrierRuntime), s.Diag);
                Assert.IsTrue(settled.Settled, settled.Refusal);
                Assert.AreEqual(10000 - charges[i], payerRuntime.CurrentCashCents);
                Assert.IsFalse(settlement.SettleOnAcceptance(
                    shipments[i], new RuntimeCashPort(payerRuntime),
                    new RuntimeCashPort(carrierRuntime), s.Diag).Settled,
                    "a second settlement of the same leg is refused");
            }
            Assert.AreEqual(250 + 100 + 120 + 250 + 100, carrierRuntime.CurrentCashCents,
                "carrier collects each leg's charge exactly once");

            // Mid-run save/restore of the freight driver state:
            // shipments, cargo, deliveryApplied, and
            // freightRevenueCollected round-trip with no
            // double-collection and no duplication.
            var source = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog(), FreightDriver = s.Driver };
            Series2Pass9SaveDto dto = source.Capture(s.Diag);
            var target = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog(), FreightDriver = new FreightProductionDriver() };
            target.Restore(dto, null, s.Diag);
            target.Restore(dto, null, s.Diag);
            List<LogisticsShipmentState> restored = target.FreightDriver.AllShipments();
            Assert.AreEqual(5, restored.Count, "a second restore must not duplicate shipments");
            foreach (LogisticsShipmentState shipment in restored)
            {
                Assert.IsTrue(shipment.deliveryApplied, "deliveryApplied must survive the round-trip");
                Assert.IsTrue(shipment.FreightRevenueCollected, "freightRevenueCollected must survive the round-trip");
            }
            var restoredSettlement = new Series2CarrierSettlement();
            restoredSettlement.RestoreFromShipments(restored);
            foreach (LogisticsShipmentState shipment in restored)
                Assert.IsTrue(restoredSettlement.AlreadySettled(shipment.shipmentId),
                    "a restored collected leg must refuse a second collection");
        }

        // ---- mid-run save/restore ----------------------------------

        private static Pass4PlayerDuty NewDuty(List<string> diag)
        {
            var duty = new Pass4PlayerDuty("player-1", "home-1", 600);
            Assert.IsTrue(duty.SetDuty(true, diag), "duty toggle must switch on; diag: " + string.Join("; ", diag));
            Assert.IsTrue(duty.AddDutyTask("test.sweep", 30, diag));
            Assert.IsTrue(duty.AddDutyTask("test.cook", 45, diag));
            return duty;
        }

        private static HouseholdLedger NewLedger(int cents)
        {
            var ledger = new HouseholdLedger(1);
            Assert.IsNull(ledger.RecordInflow(
                0, cents, HouseholdIncomeSource.WageEmployment,
                "emp-1", "test wage", "employer"), "test wage inflow must be accepted");
            return ledger;
        }

        [Test]
        public void MidRunSaveRestore_FullPass9PlusPass10State_RestoresTwiceWithoutDuplication()
        {
            var diag = new List<string>();
            var sequence = new Pass2TaskSequenceDay();
            sequence.AddTask("sweep", 30);
            sequence.MarkCompleted("sweep");
            sequence.AddTask("cook", 45);
            sequence.MarkMissed("cook", "no time");
            var report = new Pass2MissedWorkReport();
            report.AddMissed("cook", "player-1", "no time");
            report.RecordIdleMinutes(30);

            var duty = NewDuty(diag);
            Assert.IsTrue(duty.StartDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.CompleteDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.MissDutyTask("test.cook", "no time", diag));
            Assert.IsTrue(duty.AddDutyTask("test.errand", 20, diag));
            Assert.IsTrue(duty.OffloadTask("test.errand", "emp-1", "backlog 20 minutes", diag));
            duty.CashCents = 123;

            var autoEat = new Pass4AutoEat("home-1", 200);
            autoEat.SetToggle(true, diag);
            Assert.IsTrue(autoEat.Start("hungry", diag));
            Assert.IsTrue(autoEat.StepFindSeller(new FakeFoodSeller(), diag));
            Assert.IsTrue(autoEat.StepTravel(new FakeRoute(), diag));
            var autoSleep = new Pass4AutoSleep("home-1", 200);
            autoSleep.SetToggle(true, diag);
            Assert.IsTrue(autoSleep.Start("tired", diag));
            Assert.IsTrue(autoSleep.StepFindLodging(new FakeLodging(), diag));
            Assert.IsTrue(autoSleep.StepTravel(new FakeRoute(), diag));

            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            Assert.IsNull(bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            var bakeryDriver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            Assert.IsNotNull(bakeryDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, diag));

            var intake = new FuelYardByproductIntake();
            var ids = new EntityIdRegistry();
            Assert.IsNull(intake.ReceiveByproductLot("dealer-1", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 11),
                FuelWoodUnits = 30,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                ProducedDayIndex = 0,
            }, diag));
            Assert.IsNull(intake.ReceiveByproductLot("dealer-1", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 12),
                FuelWoodUnits = 20,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                ProducedDayIndex = 0,
            }, diag));
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "ship-1",
                state = LogisticsShipmentStatus.Completed,
                carrierBusinessInstanceId = "carrier-1",
                freightPayerBusinessInstanceId = "payer-1",
                freightChargeCents = 100,
                deliveryApplied = true,
                freightRevenueCollected = true,
            };
            var freightServiceDto = new FreightShipmentService.FreightServiceSaveDto();
            freightServiceDto.Shipments.Add(shipment.CaptureSaveDto());
            var freightDriver = new FreightProductionDriver();
            freightDriver.RestoreSaveDto(freightServiceDto, null, diag);
            var dealer = new Series2FuelDealer(intake, "dealer-1");

            var source9 = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() };
            source9.SequenceDay = sequence;
            source9.MissedReport = report;
            source9.Duty = duty;
            source9.AutoEat = autoEat;
            source9.AutoSleep = autoSleep;
            source9.BakeryDriver = bakeryDriver;
            source9.FuelIntake = intake;
            source9.FreightDriver = freightDriver;
            source9.FuelSource = new Series2DealerBakeryFuelSource(
                dealer, 2, (stock, units, day, registry, d) => null);
            Series2Pass10SaveState source10 = NewPass10StateWithSample();

            PassXlSliceSaveDto sliceDto = PassXlSliceSaveCoordinator.Capture(
                new FakeMigrationHolder(), new FakePlayerHolder(), null, source9, source10);
            Assert.IsNotNull(sliceDto.series2Pass9);
            Assert.IsNotNull(sliceDto.series2Pass10);

            var target9 = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() };
            target9.Duty = new Pass4PlayerDuty("player-1", "home-1", 600);
            target9.AutoEat = new Pass4AutoEat("home-1", 200);
            target9.AutoSleep = new Pass4AutoSleep("home-1", 200);
            target9.BakeryDriver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            target9.FuelIntake = new FuelYardByproductIntake();
            target9.FreightDriver = new FreightProductionDriver();
            var target10 = new Series2Pass10SaveState();
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                sliceDto, new FakeMigrationHolder(), new FakePlayerHolder(), null, target9, null, target10);
            PassXlSliceSaveCoordinator.Restore(
                sliceDto, new FakeMigrationHolder(), new FakePlayerHolder(), null, target9, null, target10);

            Assert.AreEqual(2, target9.SequenceDay.Entries.Count, "sequence must not double on the second restore");
            Assert.AreEqual(1, target9.MissedReport.Items.Count, "missed report must not double");
            Assert.AreEqual(30, target9.MissedReport.IdleMinutes);
            Assert.AreEqual(2, target9.Duty.Tasks.Count, "sweep + cook stay on the roster; errand moves to offloaded");
            Assert.AreEqual(1, target9.Duty.OffloadedTasks.Count);
            Assert.AreEqual(123, target9.Duty.CashCents);
            Assert.AreEqual(Pass4AutoEatState.AtSeller, target9.AutoEat.State);
            Assert.AreEqual(Pass4AutoSleepState.AtLodging, target9.AutoSleep.State);
            Assert.AreEqual(2, target9.BakeryDriver.PlannedDayIndex);
            Assert.AreEqual(1, target9.BakeryDriver.PlannedBatchIds.Count);
            Assert.AreEqual(50, target9.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"),
                "fuel lots must not double on the second restore");
            Assert.AreEqual(1, target9.FreightDriver.AllShipments().Count);
            Assert.IsTrue(target9.FreightDriver.AllShipments()[0].FreightRevenueCollected);
            Assert.AreEqual(1, target10.Leads.Count, "Pass 10 leads must not double");
            Assert.AreEqual(1, target10.Visits.Count);
            Assert.AreEqual(1, target10.Chains.Count);

            // Driver re-supply obligations are required and
            // diagnosed, never silently satisfied.
            Assert.IsNotNull(target9.RestoredFuelSourceParams,
                "fuel-source parameters must be recovered for the driver to rewire");
            Assert.AreEqual("dealer-1", target9.RestoredFuelSourceParams.DealerBusinessId);
            Assert.AreEqual(2, target9.RestoredFuelSourceParams.IntakeUnitsPerBakeryUnit);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("rewire")),
                "the off-map fuel delegate must be flagged for rewiring, never silently restored; diag: " + string.Join("; ", restoreDiag));
            Assert.IsFalse(target9.AutoEat.StepEat(diag),
                "a restored AtSeller state with no seller re-supplied must fail safely, never serve a free meal");
            Assert.AreEqual(200, target9.AutoEat.CashCents);
        }

        // ---- documented restore drops ------------------------------

        [Test]
        public void RestoreDrops_AreDocumentedBehaviour_NotBugs()
        {
            var diag = new List<string>();
            var dto = new Series2Pass9SaveDto
            {
                Duty = new Pass9DutyDto
                {
                    PlayerPersonId = "player-1",
                    CurrentLocationId = "home-1",
                    DailyBudgetMinutes = 600,
                    DutyRequested = true,
                    CurrentTaskId = "test.cook",
                    CashCents = 50,
                    TravelDestination = "shop-1",
                    TravelMinutesTotal = 30,
                    TravelMinutesElapsed = 10,
                    Tasks = new List<Pass9DutyTaskDto>
                    {
                        new Pass9DutyTaskDto { TaskDefinitionId = "test.cook", PlannedMinutes = 45, InProgress = true },
                    },
                },
                AutoEat = new Pass9AutoEatDto { ToggleOn = true, State = 999, CurrentLocationId = "home-1", CashCents = 200 },
            };
            var target = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() };
            target.Duty = new Pass4PlayerDuty("player-1", "home-1", 600);
            target.AutoEat = new Pass4AutoEat("home-1", 200);
            target.AutoSleep = new Pass4AutoSleep("home-1", 200);
            target.Restore(dto, null, diag);

            Assert.AreEqual("test.cook", target.Duty.CurrentTaskId,
                "an unfinished in-progress roster task IS kept as the current task");
            Assert.IsFalse(target.Duty.IsTravelling,
                "saved travel is dropped when a task is current; the model forbids being busy twice");
            Assert.AreEqual("home-1", target.Duty.CurrentLocationId,
                "the player stays at the saved location, never teleported to the travel destination");
            Assert.IsTrue(diag.Any(d => d.Contains("saved travel dropped")),
                "the travel drop must be diagnosed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(Pass4AutoEatState.Inactive, target.AutoEat.State,
                "an out-of-range auto-care state restores as Inactive, never as a made-up state");
            Assert.IsTrue(diag.Any(d => d.Contains("out of range")));

            // Second drop: a saved current task that is NOT an
            // unfinished in-progress roster task is dropped.
            var dto2 = new Series2Pass9SaveDto
            {
                Duty = new Pass9DutyDto
                {
                    PlayerPersonId = "player-1",
                    CurrentLocationId = "home-1",
                    DailyBudgetMinutes = 600,
                    CurrentTaskId = "test.sweep",
                    Tasks = new List<Pass9DutyTaskDto>
                    {
                        new Pass9DutyTaskDto { TaskDefinitionId = "test.sweep", PlannedMinutes = 30, Completed = true },
                    },
                },
            };
            var target2 = new Series2Pass9SaveState { Catalog = new Pass3StoreCatalog() };
            target2.Duty = new Pass4PlayerDuty("player-1", "home-1", 600);
            target2.AutoEat = new Pass4AutoEat("home-1", 200);
            target2.AutoSleep = new Pass4AutoSleep("home-1", 200);
            var diag2 = new List<string>();
            target2.Restore(dto2, null, diag2);
            Assert.AreEqual(string.Empty, target2.Duty.CurrentTaskId,
                "a completed task must never restore as the current task; no phantom tasks");
            Assert.IsTrue(diag2.Any(d => d.Contains("not an unfinished in-progress")),
                "the current-task drop must be diagnosed; diag: " + string.Join("; ", diag2));
        }

        // ---- missed work visible -----------------------------------

        [Test]
        public void MissedWork_VisibleViaPass2Api_ConsoleRenders_IdleIsStandingBy()
        {
            var diag = new List<string>();
            var authority = new TaskAuthority();
            string rejection;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep the store", 30), out rejection));
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.cook", "Cook a meal", 45), out rejection));
            var player = EntityId.For(EntityKind.Person, 1);
            var duty = NewDuty(diag);
            var binding = new Series2PlayerDutyBinding(duty, authority, player, new WorkTimeBudgetStore(), null);
            Assert.IsTrue(duty.StartDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.CompleteDutyTask("test.sweep", diag));
            Assert.IsTrue(duty.MissDutyTask("test.cook", "no time", diag));

            Pass2MissedWorkReport report = duty.BuildMissedWorkReport(0);
            Assert.AreEqual(1, report.Items.Count);
            Assert.AreEqual("test.cook", report.Items[0].TaskDefinitionId);
            string console = binding.RenderMissedWorkConsole(0);
            Assert.IsTrue(console.Contains("test.cook"),
                "console rendering must name the missed task; report: " + console);

            // Idle is standing by, never missed work or hiring
            // pressure: a report with idle minutes only has no
            // missed items, and the assessor sees no pressure.
            var idleReport = new Pass2MissedWorkReport();
            idleReport.RecordIdleMinutes(120);
            Assert.AreEqual(0, idleReport.Items.Count, "idle minutes alone are not missed work");
            var assessor = new LaborDemandAssessor();
            LaborDemandReport idleDemand = assessor.Assess(new LaborDemandInputs
            {
                BusinessInstanceId = "general-store-1",
                DayIndex = 0,
                AvailableEmployeeMinutes = 600,
            }, diag);
            Assert.IsFalse(idleDemand.HiringPressure, "idle time must not create hiring pressure");

            // Offloaded tasks are never missed work.
            var duty2 = NewDuty(diag);
            var binding2 = new Series2PlayerDutyBinding(duty2, authority, player, new WorkTimeBudgetStore(), null);
            Assert.IsTrue(binding2.IssueDutyTask("test.sweep", 0, diag));
            Assert.IsTrue(binding2.StartDutyTask("test.sweep", 0, diag));
            Assert.IsTrue(binding2.OffloadDutyTask("test.sweep", "emp-1", EntityId.For(EntityKind.Person, 2), "backlog 30 minutes", 0, diag));
            Pass2MissedWorkReport report2 = duty2.BuildMissedWorkReport(0);
            Assert.IsFalse(report2.Items.Any(i => i.TaskDefinitionId == "test.sweep"),
                "offloaded work must not appear in the missed-work report");
        }

        // ---- Shane-shaped buying: shortage -> loop -> adapter ------
        // SHAPE TESTS ONLY. The household below is a generic
        // single-person test household with no food/fuel and cash;
        // it is NOT Shane Bryan, and this is NOT the slice fixture.
        // The purchase path is the real shortage monitor, the real
        // shopping loop, and the real Pass 7 adapter. There is no
        // direct stock injection and no scripted Receive/purchase
        // call in these tests: if the loop or the adapter is
        // bypassed, the purchase does not happen and the test fails.

        private sealed class LoopFixture
        {
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel Journeys;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public FinancialObligationAuthority Obligations;
            public EntityIdRegistry EntityIds;
            public HouseholdShoppingLoop Loop;
            public Series2BakerySupplierAdapter BakeryAdapter;
            public Series2FuelDealerSupplierAdapter FuelAdapter;
            public BusinessRuntimeState BakeryRuntime;
            public BusinessRuntimeState DealerRuntime;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> Diagnostics = new List<string>();
            public int HouseholdId = 7;
            public int Day = 2;

            public static LoopFixture Create(bool registerBakery, bool registerFuel, bool withKnowledge)
            {
                var f = new LoopFixture();
                f.Population = new PopulationState();
                f.Population.people.Add(new PersonState
                {
                    id = 1, firstName = "Test", lastName = "Shopper", age = 34,
                    ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                });
                f.Population.households.Add(new HouseholdState { id = 7 });
                f.Membership = new HouseholdMembershipRegistry();
                Assert.IsNull(f.Membership.Register(new HouseholdMembership
                {
                    MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0,
                }));
                f.Ledgers = new HouseholdLedgerRegistry();
                f.Inventories = new HouseholdInventoryRegistry();
                f.Needs = new HouseholdNeedRegistry();
                f.Journeys = new JourneyModel();
                f.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("bakery", JourneyLocationKind.Store, "Pierce Bakery", 2f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("fuel-yard", JourneyLocationKind.Store, "Fuel Yard", 3f, 0f));
                f.Journeys.AddEdge("home", "bakery", 2.0f, "town road");
                f.Journeys.AddEdge("home", "fuel-yard", 3.0f, "yard road");
                f.Tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(f.Tasks);
                f.Budgets = new WorkTimeBudgetStore();
                f.Tracker = new PersonScheduleTracker();
                f.Knowledge = new ShopperSellerKnowledge();
                f.Obligations = new FinancialObligationAuthority();
                f.EntityIds = new EntityIdRegistry();
                f.Directory = new SupplierDirectory();

                BakeryShopRuntime bakery = BakeOneBatch(2000, 2, out f.BakeryRuntime, out EntityIdRegistry bakeIds);
                f.BakeryAdapter = new Series2BakerySupplierAdapter(
                    bakery, new RuntimeCashPort(f.BakeryRuntime),
                    "pierce-bakery-1", "Pierce Bakery", "bakery");
                f.BakeryAdapter.DayIndex = 2;
                if (registerBakery) Series2BakerySupplierAdapter.RegisterIn(f.Directory, f.BakeryAdapter);

                var intake = new FuelYardByproductIntake();
                var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
                var fuelDiag = new List<string>();
                Assert.IsNull(dealer.ReceiveSawmillLot(SawmillLot(f.EntityIds, "mill-1", 20, 1), fuelDiag));
                f.DealerRuntime = new BusinessRuntimeState();
                f.DealerRuntime.BindBusinessIdentity("fuel-dealer-1");
                f.DealerRuntime.AddCashCents(0);
                f.FuelAdapter = new Series2FuelDealerSupplierAdapter(
                    intake, new Series2BusinessRuntimeCashPort(f.DealerRuntime),
                    "fuel-dealer-1", "Fuel Dealer", "fuel-yard", 28);
                if (registerFuel) Series2FuelDealerSupplierAdapter.RegisterIn(f.Directory, f.FuelAdapter);

                if (withKnowledge)
                {
                    f.Knowledge.LearnSupplier(1, "pierce-bakery-1", "lives in the same town", 0);
                    f.Knowledge.LearnSupplier(1, "fuel-dealer-1", "lives in the same town", 0);
                }
                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds);
                f.PersonLocations[1] = "home";
                return f;
            }

            public ShoppingLoopOptions Options(bool allowTradeCredit)
            {
                var fixture = this;
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => fixture.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                    SetPersonLocation = (pid, loc) => fixture.PersonLocations[pid] = loc,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    Diagnostics = fixture.Diagnostics,
                    AllowTradeCredit = allowTradeCredit,
                };
            }

            public void Fund(int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public int Balance() => Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();

            public HouseholdPurchasingNeed OpenNeedViaShortage(string itemId, int dailyUse, bool coldSeason, bool hasCash)
            {
                var monitor = new HouseholdShortageMonitor(Needs);
                var household = new HouseholdState { id = HouseholdId };
                HouseholdInventory inventory = Inventories.GetOrCreate(HouseholdId);
                monitor.Evaluate(household, inventory,
                    new Dictionary<string, int> { { itemId, dailyUse } },
                    coldSeason,
                    new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = hasCash },
                    0, 0, Day);
                HouseholdPurchasingNeed need = Needs.GetOpenNeed(HouseholdId, itemId);
                Assert.IsNotNull(need, "the shortage monitor must open a real need for an empty household; diag: " + string.Join("; ", monitor.Diagnostics));
                return need;
            }
        }

        [Test]
        public void ShaneShapedBuying_Positive_BreadThroughLoopAndAdapter_ConservesEveryCent()
        {
            LoopFixture f = LoopFixture.Create(registerBakery: true, registerFuel: false, withKnowledge: true);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeedViaShortage(
                HouseholdItemCatalog.BreadId, 2, false, true);
            Assert.AreEqual(14, need.UnitsNeeded,
                "empty household at 2/day against the 7-day threshold needs 14 units; the monitor, not the test, sets this");

            int bakeryCashBefore = f.BakeryRuntime.CurrentCashCents;
            int shelfBefore = f.BakeryAdapter.ItemStockUnits(HouseholdItemCatalog.BreadId);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options(allowTradeCredit: false));
            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(1, result.ShopperPersonId);
            Assert.AreEqual(14, result.UnitsAcquired);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, result.ItemId);
            Assert.AreEqual("Pierce Bakery", result.Counterparty, "the purchase must go through the bakery adapter, not a scripted sale");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ReceiptId));
            Assert.AreEqual(1, f.Loop.TripHistory.Count, "exactly one loop trip must have run");
            Assert.AreEqual(14 * 8, result.BuyerOutflowCents, "14 fresh loaves at the 8c schedule price");
            Assert.AreEqual(result.BuyerOutflowCents, result.StoreCashInCents,
                "buyer outflow must equal business cash in, every cent");
            Assert.AreEqual(bakeryCashBefore + 112, f.BakeryRuntime.CurrentCashCents);
            Assert.AreEqual(shelfBefore - 14, f.BakeryAdapter.ItemStockUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(10000 - 112, f.Balance());
            Assert.AreEqual(14, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "goods enter household inventory only upon physical receipt through the loop");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.IsNull(f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.BreadId),
                "a fulfilled need must not reopen on its own");
        }

        [Test]
        public void ShaneShapedBuying_Positive_FuelThroughLoopAndAdapter_FillsReservePath()
        {
            LoopFixture f = LoopFixture.Create(registerBakery: false, registerFuel: true, withKnowledge: true);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeedViaShortage(
                HouseholdItemCatalog.FuelWoodId, 1, true, true);
            Assert.AreEqual(7, need.UnitsNeeded, "empty household at 1/day against the 7-day threshold needs 7 units");
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options(allowTradeCredit: false));
            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(7, result.UnitsAcquired);
            Assert.AreEqual("Fuel Dealer", result.Counterparty);
            Assert.AreEqual(7 * 28, result.BuyerOutflowCents, "7 units at the caller price 28c");
            Assert.AreEqual(10000 - 196, f.Balance());
            Assert.AreEqual(196, f.DealerRuntime.CurrentCashCents, "dealer cash gains exactly the sale, through the production port");
            Assert.AreEqual(7, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.FuelWoodId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void ShaneShapedBuying_Negative_NoCash_NeedStaysOpen_NoDebt_NoFreeStock()
        {
            LoopFixture f = LoopFixture.Create(registerBakery: true, registerFuel: false, withKnowledge: true);
            HouseholdPurchasingNeed need = f.OpenNeedViaShortage(
                HouseholdItemCatalog.BreadId, 2, false, false);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options(allowTradeCredit: false));
            Assert.IsFalse(result.Success, "a household with no cash and no credit must not complete a purchase");
            Assert.AreEqual(0, result.UnitsAcquired);
            Assert.AreEqual(0, f.Balance());
            Assert.AreEqual(0, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "no free stock, ever");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need stays open, diagnosed, never silently satisfied");
            Assert.IsNotNull(f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(2000, f.BakeryRuntime.CurrentCashCents, "no sale means no business cash moves");
        }

        [Test]
        public void ShaneShapedBuying_Negative_NoAdapterRegistered_NeedStaysOpen_NoPurchase()
        {
            LoopFixture f = LoopFixture.Create(registerBakery: false, registerFuel: false, withKnowledge: true);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeedViaShortage(
                HouseholdItemCatalog.BreadId, 2, false, true);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options(allowTradeCredit: false));
            Assert.IsFalse(result.Success, "with no adapter registered, the loop must not invent a seller");
            Assert.AreEqual(0, result.UnitsAcquired);
            Assert.AreEqual(10000, f.Balance(), "no purchase means no cash moves");
            Assert.AreEqual(0, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need stays open, diagnosed, never silently satisfied");
        }

        // ---- Shane-shaped hiring ------------------------------------
        // SHAPE TESTS ONLY. The candidate below is a generic test
        // person (id 21) in the test population; it is NOT Shane
        // Bryan, and no hiring target or headcount quota is
        // asserted anywhere: hiring follows sustained
        // unfinished-work evidence only.

        private static LaborDemandReport AssessDay(int day, int openMinutes, int availableMinutes, int waiting, int balks)
        {
            var diag = new List<string>();
            var assessor = new LaborDemandAssessor();
            var inputs = new LaborDemandInputs
            {
                BusinessInstanceId = "general-store-1",
                DayIndex = day,
                AvailableEmployeeMinutes = availableMinutes,
                WaitingCustomers = waiting,
                RecentBalkCount = balks,
            };
            inputs.OpenTaskMinutesByFamily["Production"] = openMinutes;
            return assessor.Assess(inputs, diag);
        }

        [Test]
        public void ShaneShapedHiring_Positive_SustainedEvidence_ThenEffortInquiryEmployment()
        {
            // Caller-supplied threshold (test parameters, never
            // canon): 3 days of pressure, each over 200 unmet
            // minutes. All three days must clear it before any
            // effort opens.
            const int thresholdDays = 3;
            const int thresholdMinutes = 200;
            var reports = new List<LaborDemandReport>
            {
                AssessDay(0, 300, 60, 6, 3),
                AssessDay(1, 320, 60, 7, 3),
                AssessDay(2, 300, 60, 6, 4),
            };
            int pressuredDays = 0;
            foreach (LaborDemandReport report in reports)
            {
                Assert.IsTrue(report.HiringPressure, "each evidence day must show hiring pressure");
                if (report.HiringPressure && report.TotalUnmetMinutes > thresholdMinutes) pressuredDays++;
            }
            Assert.AreEqual(thresholdDays, pressuredDays, "only a sustained pattern opens an effort");

            var diag = new List<string>();
            var population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 21, firstName = "Test", lastName = "Candidate", age = 28,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
            });
            var employment = new EmploymentRelationshipRegistry();
            var service = new RecruitmentService();
            RecruitmentEffort effort = service.OpenEffort(
                "general-store-1", "Shop Assistant", RecruitmentChannel.Handbill, 3, 42, diag);
            Assert.IsNotNull(effort, "the effort opens only after the sustained pattern; diag: " + string.Join("; ", diag));
            CandidateInquiry inquiry = service.ApplyForEffort(
                effort.EffortId, 21, population, employment, "saw the handbill", 3, diag);
            Assert.IsNotNull(inquiry, "the candidate applies through the real channel; diag: " + string.Join("; ", diag));
            Assert.IsNull(service.HireFromInquiry(
                inquiry.InquiryId, 5000, EmploymentKind.Permanent, 4, employment, diag),
                "hire must succeed with caller-supplied terms; diag: " + string.Join("; ", diag));
            Assert.AreEqual(1, employment.GetActiveByEmployee(21).Count);
            Assert.AreEqual("general-store-1", employment.GetActiveByEmployee(21)[0].EmployerBusinessId);
            Assert.AreEqual("Shop Assistant", employment.GetActiveByEmployee(21)[0].RoleDisplayName);
        }

        [Test]
        public void ShaneShapedHiring_Negative_SingleMissedTask_OrBelowThresholdDay_OpensNothing()
        {
            LaborDemandReport single = AssessDay(0, 45, 600, 0, 0);
            Assert.IsFalse(single.HiringPressure, "one missed task with ample cover is not hiring pressure");
            LaborDemandReport below = AssessDay(1, 150, 600, 0, 0);
            Assert.IsFalse(below.HiringPressure, "a below-threshold day is not hiring pressure");
            Assert.IsTrue(single.TotalUnmetMinutes < 200 && below.TotalUnmetMinutes < 200,
                "neither day clears the caller threshold, so no effort may open");
            var service = new RecruitmentService();
            Assert.AreEqual(0, service.OpenEffortsForBusiness("general-store-1").Count,
                "no effort is opened on insufficient evidence");
        }

        [Test]
        public void ShaneShapedHiring_Negative_AllTasksDone_NoHire_NoEffort_NoQuota()
        {
            var diag = new List<string>();
            var report = new Pass2MissedWorkReport();
            report.RecordIdleMinutes(60);
            Assert.AreEqual(0, report.Items.Count, "all tasks done means no missed work");
            LaborDemandReport demand = AssessDay(0, 0, 600, 0, 0);
            Assert.IsFalse(demand.HiringPressure);
            Assert.AreEqual(0, demand.TotalUnmetMinutes);
            var service = new RecruitmentService();
            Assert.AreEqual(0, service.OpenEffortsForBusiness("general-store-1").Count,
                "all tasks done opens no effort, and no headcount quota or target is asserted anywhere in this harness");
            Assert.IsFalse(diag.Any(d => d.Contains("quota")), "no quota language is produced");
        }

        // ---- expanded shape (NOT a fixture) ------------------------

        [Test]
        public void ExpandedShape_NinePersonsFiveHouseholds_AllChainsRun_ShapeOnlyNotAFixture()
        {
            // SHAPE TEST ONLY: nine generic test persons across five
            // generic test households, no names, no ages-as-identity,
            // no business names from the slice, no houses, horses,
            // wagons, or cash amounts invented for any real person.
            // This proves the production drivers accept a 9-person
            // shape; it does NOT author the expanded slice fixture,
            // which stays blocked on Kennedy inputs.
            var population = new PopulationState();
            var membership = new HouseholdMembershipRegistry();
            int personId = 100;
            for (int household = 1; household <= 5; household++)
            {
                population.households.Add(new HouseholdState { id = household });
                int members = household <= 4 ? 2 : 1;
                for (int m = 0; m < members; m++)
                {
                    population.people.Add(new PersonState
                    {
                        id = personId, firstName = "Test", lastName = "Person" + personId,
                        age = 30, ageBand = AgeBand.Adult18Plus,
                        laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    });
                    Assert.IsNull(membership.Register(new HouseholdMembership
                    {
                        MembershipId = personId, PersonId = personId, HouseholdId = household, StartDayIndex = 0,
                    }));
                    personId++;
                }
            }
            Assert.AreEqual(9, population.people.Count, "the shape is nine persons");
            Assert.AreEqual(5, population.households.Count, "the shape is five households");

            // The same production chains run against the shape:
            // a bake, a fuel dispense, a carrier leg, and a
            // missed-work report over the shape's persons.
            BakeryShopRuntime bakery = BakeOneBatch(2000, 2, out BusinessRuntimeState bakeryRuntime, out EntityIdRegistry ids);
            Assert.AreEqual(24, bakery.BreadShelf[0].UnitCount);
            var intake = new FuelYardByproductIntake();
            var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
            Assert.IsNull(dealer.ReceiveSawmillLot(SawmillLot(ids, "mill-1", 10, 1), new List<string>()));
            Assert.IsNotNull(dealer.DispenseUnits(4, 2, new List<string>()));
            Assert.AreEqual(6, dealer.StockUnits);
            var carrierSetup = NewCarrierSetup();
            LogisticsShipmentState leg = RunLeg(carrierSetup, Series2SliceLegs.DealerToBakery(
                "ship-shape-1", "freight-co-1", "fuel-dealer-1", "bakery-1", 4, 100, "wagon-1"),
                "bakery-1", 4);
            Assert.AreEqual(4, leg.DeliveredQuantityUnits);
            var dutyDiag = new List<string>();
            var duty = NewDuty(dutyDiag);
            Assert.IsTrue(duty.StartDutyTask("test.sweep", dutyDiag));
            Assert.IsTrue(duty.CompleteDutyTask("test.sweep", dutyDiag));
            Assert.IsTrue(duty.MissDutyTask("test.cook", "no time", dutyDiag));
            Pass2MissedWorkReport missed = duty.BuildMissedWorkReport(0);
            Assert.AreEqual(1, missed.Items.Count, "missed work is visible over the shape through the Pass 2 API");
            Assert.AreEqual("test.cook", missed.Items[0].TaskDefinitionId);
        }
    }
}
