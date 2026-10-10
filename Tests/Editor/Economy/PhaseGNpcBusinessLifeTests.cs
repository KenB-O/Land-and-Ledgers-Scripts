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
    /// Phase G (G4-G6): NPC business life — struggle → borrow → recover,
    /// pivot with real re-tooling, orderly close-down (no stranded debt),
    /// real acquisition, and real business demand. All run in the Roslyn
    /// harness against the real credit loop and obligation authority.
    /// </summary>
    [TestFixture]
    public sealed class PhaseGNpcBusinessLifeTests
    {
        private sealed class FakeBusinessCashPort : INpcBusinessCashPort
        {
            private readonly Dictionary<string, int> balances = new Dictionary<string, int>();

            public void Seed(string businessInstanceId, int cents)
            {
                balances[businessInstanceId] = cents;
            }

            public int ReadCashCents(string businessInstanceId)
            {
                return balances.TryGetValue(businessInstanceId, out int cents) ? cents : 0;
            }

            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                if (amountCents <= 0) return "Spend: amount must be positive.";
                int cash = ReadCashCents(businessInstanceId);
                if (amountCents > cash)
                {
                    return $"Spend refused: {businessInstanceId} holds {cash}c, cannot spend {amountCents}c ({purpose}).";
                }

                balances[businessInstanceId] = cash - amountCents;
                return null;
            }

            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                if (amountCents <= 0) return "Receive: amount must be positive.";
                balances[businessInstanceId] = ReadCashCents(businessInstanceId) + amountCents;
                return null;
            }
        }

        private sealed class FakeLiquidationPort : INpcLiquidationPort
        {
            public int RecoveryBps = 5000; // 50% of formation cost
            public List<NpcLiquidationSale> Sales = new List<NpcLiquidationSale>();

            public string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
                out NpcLiquidationSale sale)
            {
                sale = null;
                if (asset == null) return "no asset.";
                sale = new NpcLiquidationSale
                {
                    AssetItemId = asset.ItemId,
                    Units = asset.Units,
                    BuyerName = "second-hand dealer",
                    ProceedsCents = asset.CostCents * RecoveryBps / 10000,
                };
                Sales.Add(sale);
                return null;
            }
        }

        private sealed class FakeProcurementPort : INpcBusinessProcurementPort
        {
            private readonly HashSet<string> stockedItems;
            private readonly FakeBusinessCashPort cash;

            public FakeProcurementPort(FakeBusinessCashPort cash, params string[] stockedItems)
            {
                this.cash = cash;
                this.stockedItems = new HashSet<string>(stockedItems);
            }

            public string PurchaseForBusiness(string businessInstanceId, string itemId, int units,
                int maxPriceCentsPerUnit, int dayIndex, out NpcBusinessPurchase purchase)
            {
                purchase = null;
                // Only REAL suppliers carry stock: this fake stocks a fixed
                // list; anything else is refused — never invented.
                if (!stockedItems.Contains(itemId))
                {
                    return $"no supplier carries '{itemId}' — demand stays unmet.";
                }

                int unitPrice = maxPriceCentsPerUnit > 0 ? maxPriceCentsPerUnit : 100;
                int total = unitPrice * units;
                string refusal = cash.Spend(businessInstanceId, dayIndex, total, $"demand purchase {units}x '{itemId}'");
                if (refusal != null) return refusal;
                purchase = new NpcBusinessPurchase
                {
                    BusinessInstanceId = businessInstanceId,
                    ItemId = itemId,
                    Units = units,
                    TotalCostCents = total,
                    SupplierId = "general-store",
                    Provenance = "purchase:general-store-" + dayIndex,
                    DayIndex = dayIndex,
                };
                return null;
            }
        }

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditRegistry Registry = new CreditRegistry();
            public CreditCashBridge CashBridge = new CreditCashBridge();
            public CreditWorkoutService Workout = new CreditWorkoutService();
            public CreditEventLog CreditEvents = new CreditEventLog();
            public TitleAuthority Titles = new TitleAuthority();
            public NpcCreditDecisionEngine CreditEngine = new NpcCreditDecisionEngine();
            public FakeBusinessCashPort Cash = new FakeBusinessCashPort();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public NpcFormationAssetRegister Assets = new NpcFormationAssetRegister();
            public FakeLiquidationPort Liquidation = new FakeLiquidationPort();
            public NpcBusinessEventLog Events = new NpcBusinessEventLog();
            public HouseholdLedger FounderLedger;
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                Registry.AttachFinancialAuthority(Authority);
                FounderLedger = new HouseholdLedger(7);
                Assert.IsNull(FounderLedger.RecordInflow(0, 50000, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
            }

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(CashBridge.Register(owner, purse));
                return purse;
            }

            public NpcCreditorProfile PrivateCreditor(string name, int capitalCents)
            {
                var purse = RegisterPurse(name, capitalCents);
                var funds = new PrivateLenderFunds(name, capitalCents);
                return new NpcCreditorProfile
                {
                    Name = name,
                    PrivateLender = new PrivateLenderCreditParticipant(name, funds, purse),
                    Policy = CreditLenderPolicy.ForPrivateIndividual(),
                    RelationshipMatters = true,
                };
            }

            public NpcBusinessLifeRecord LifeRecord(string instanceId = "biz-1")
            {
                return new NpcBusinessLifeRecord
                {
                    BusinessInstanceId = instanceId,
                    BusinessEntityKey = "business:1",
                    FounderPersonId = 42,
                    OwnerName = "Marta Kessler",
                    State = NpcBusinessLifeState.Operating,
                    CapabilityIds = new List<string> { "bake-bread" },
                };
            }

            public NpcLifecycleContext Context(int day = 20)
            {
                return new NpcLifecycleContext
                {
                    Cash = Cash,
                    Obligations = Authority,
                    Employments = Employments,
                    FounderHouseholdLedger = FounderLedger,
                    AssetRegister = Assets,
                    Liquidation = Liquidation,
                    Events = Events,
                    Ids = Ids,
                    DayIndex = day,
                };
            }

            public NpcBusinessBorrowContext BorrowContext(NpcCreditorProfile lender)
            {
                return new NpcBusinessBorrowContext
                {
                    Workflow = Workflow,
                    CashBridge = CashBridge,
                    Workout = Workout,
                    Registry = Registry,
                    Foreclosure = null,
                    Titles = Titles,
                    CreditEvents = CreditEvents,
                    CreditEngine = CreditEngine,
                    Lender = lender,
                };
            }
        }

        [Test]
        public void Struggle_BorrowThroughRealCreditLoop_Recover()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 5000); // below the 20000 reserve

            NpcCreditorProfile lender = f.PrivateCreditor("Abel", 200000);
            var borrowCtx = f.BorrowContext(lender);
            var borrower = new NpcBorrowerProfile
            {
                DesiredAmountCents = 50000, MinimumAmountCents = 25000, MaxRateBps = 1500,
                Purpose = "working capital to survive a lean season", DesiredTermDays = 360,
            };

            int lenderBefore = lender.PrivateLender.Purse.ReadBalanceCents();
            service.EvaluateWeek(record, 20000, 0, f.Context(20), borrowCtx, borrower, f.Diag);

            Assert.AreEqual(NpcBusinessLifeState.Struggling, record.State);
            Assert.AreEqual(1, record.OpenObligationIds.Count, "The borrow created a real obligation.");
            FinancialObligation obligation = f.Authority.Find(record.OpenObligationIds[0]);
            Assert.IsNotNull(obligation);
            Assert.AreEqual("business:biz-1", obligation.Debtor, "The BUSINESS is the debtor — not the founder's household.");

            // Conservation: the advance debited the lender's real purse.
            int businessCash = f.Cash.ReadCashCents("biz-1");
            int lenderAfter = lender.PrivateLender.Purse.ReadBalanceCents();
            Assert.AreEqual(55000, businessCash, "5000 + 50000 advance.");
            Assert.AreEqual(lenderBefore - 50000, lenderAfter, "The lender's real cash funded the advance.");
            Assert.IsTrue(f.Events.CountKind(NpcBusinessEventKind.BorrowInitiated) > 0);

            // Two healthy weeks bring it back to Operating.
            service.EvaluateWeek(record, 20000, 0, f.Context(27), null, null, f.Diag);
            Assert.AreEqual(1, record.ConsecutiveHealthyWeeks);
            service.EvaluateWeek(record, 20000, 0, f.Context(34), null, null, f.Diag);
            Assert.AreEqual(NpcBusinessLifeState.Operating, record.State, "Recovered after 2 healthy weeks.");
        }

        [Test]
        public void Struggle_BorrowRefusedLenderHasNoCapital_NoFakeCash()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 5000);

            NpcCreditorProfile brokeLender = f.PrivateCreditor("Broke", 0);
            var borrowCtx = f.BorrowContext(brokeLender);
            var borrower = new NpcBorrowerProfile
            {
                DesiredAmountCents = 50000, MinimumAmountCents = 25000, MaxRateBps = 1500,
                Purpose = "working capital", DesiredTermDays = 360,
            };

            service.EvaluateWeek(record, 20000, 0, f.Context(20), borrowCtx, borrower, f.Diag);
            Assert.AreEqual(5000, f.Cash.ReadCashCents("biz-1"), "No cash invented when the lender cannot lend.");
            Assert.AreEqual(0, record.OpenObligationIds.Count);
        }

        [Test]
        public void Pivot_PaysRealRetoolingCost()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 40000);

            string refusal = service.Pivot(record, new List<string> { "sell-general-goods" },
                12000, "bread sales collapsed; the shop becomes a general store",
                f.Context(20), f.Diag);

            Assert.IsNull(refusal);
            Assert.AreEqual(NpcBusinessLifeState.Pivoting, record.State);
            Assert.AreEqual(28000, f.Cash.ReadCashCents("biz-1"), "Re-tooling cost really left the cash.");
            CollectionAssert.AreEquivalent(new[] { "sell-general-goods" }, record.CapabilityIds);
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.PivotStarted));

            // A healthy week on the new footing completes the pivot.
            service.EvaluateWeek(record, 20000, 0, f.Context(27), null, null, f.Diag);
            Assert.AreEqual(NpcBusinessLifeState.Operating, record.State);
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.PivotComplete));
        }

        [Test]
        public void CloseDown_Orderly_SettlesDebts_Liquidates_ReleasesStaff_NoStrandedDebt()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 30000);

            // One real obligation on the shared authority.
            FinancialObligation obligation = f.Authority.Create(f.Ids, FinancialObligationKind.Loan,
                "business:biz-1", "Abel", 20000, 0, "12% annual", "seed loan");
            Assert.IsNotNull(obligation);
            record.OpenObligationIds.Add(obligation.ObligationId);
            int obligationCountBefore = 0;
            foreach (FinancialObligation o in f.Authority.Obligations) obligationCountBefore++;

            // Assets with provenance + one hire.
            Assert.IsNull(f.Assets.Record(new NpcFormationAsset
                { AssetKind = "equipment", ItemId = "oven", Units = 1, CostCents = 15000, Provenance = "purchase:smith-12" }));
            Assert.IsNull(f.Assets.Record(new NpcFormationAsset
                { AssetKind = "inventory", ItemId = "flour", Units = 40, CostCents = 20000, Provenance = "lot:mill-9" }));
            var relationship = new EmploymentRelationship
            {
                Id = "emp-biz-1-43-baker", EmployeePersonId = 43, EmployerBusinessId = "biz-1",
                RoleDisplayName = "baker",
                Compensation = CompensationTerms.FromWeeklyWage(900, "test"),
                StartDayIndex = 10, LifecycleState = EmploymentLifecycleState.Active,
            };
            Assert.IsNull(f.Employments.Register(relationship));
            record.EmploymentIds.Add(relationship.Id);

            int founderBefore = f.FounderLedger.GetBalanceCents();
            NpcBusinessCloseResult result = service.CloseOrderly(record, "the founder retires", f.Context(30), f.Diag);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(NpcBusinessLifeState.Closed, record.State);
            Assert.AreEqual(1, result.ObligationsSettled.Count);
            Assert.AreEqual(0, result.ObligationsAssumed.Count, "Cash covered the debt — nothing to assume.");
            Assert.IsTrue(f.Authority.Find(obligation.ObligationId).Settled, "The obligation is settled on the authority.");
            Assert.AreEqual(20000, result.CashPaidToCreditorsCents);

            // No obligation was deleted: the authority still holds every obligation.
            int obligationCountAfter = 0;
            foreach (FinancialObligation o in f.Authority.Obligations) obligationCountAfter++;
            Assert.AreEqual(obligationCountBefore, obligationCountAfter, "Close-down never deletes obligations.");

            // Assets liquidated to a real buyer (50% recovery): 7500 + 10000 = 17500.
            Assert.AreEqual(2, result.AssetsLiquidated);
            Assert.AreEqual(17500, result.LiquidationProceedsCents);
            Assert.AreEqual(2, f.Liquidation.Sales.Count);

            // Staff released through the employment authorities.
            Assert.AreEqual(1, result.StaffReleased);
            Assert.IsTrue(f.Employments.TryGetById(relationship.Id, out EmploymentRelationship after));
            Assert.AreEqual(EmploymentLifecycleState.Ended, after.LifecycleState);

            // Residual: 30000 - 20000 settled + 17500 liquidation = 27500 to the founder.
            Assert.AreEqual(27500, result.ResidualToFounderCents);
            Assert.AreEqual(founderBefore + 27500, f.FounderLedger.GetBalanceCents());
            Assert.AreEqual(0, f.Cash.ReadCashCents("biz-1"), "Nothing left behind in the business.");

            // §26: the whole wind-down is observable.
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.CloseStarted));
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.CloseComplete));
            Assert.IsTrue(f.Events.CountKind(NpcBusinessEventKind.ObligationSettled) > 0);
            Assert.AreEqual(2, f.Events.CountKind(NpcBusinessEventKind.AssetLiquidated));
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.StaffReleased));
        }

        [Test]
        public void CloseDown_UnpayableDebt_ExplicitlyAssumedByFounder_NeverStranded()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 10000);

            FinancialObligation obligation = f.Authority.Create(f.Ids, FinancialObligationKind.Loan,
                "business:biz-1", "Abel", 100000, 0, "12% annual", "expansion loan");
            record.OpenObligationIds.Add(obligation.ObligationId);

            NpcBusinessCloseResult result = service.CloseOrderly(record, "debts exceed assets", f.Context(30), f.Diag);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(1, result.ObligationsAssumed.Count, "The shortfall is explicitly assumed — never stranded.");
            Assert.AreEqual(0, result.ObligationsSettled.Count);

            FinancialObligation after = f.Authority.Find(obligation.ObligationId);
            Assert.IsNotNull(after, "The obligation still exists on the authority — not deleted.");
            Assert.IsFalse(after.Settled);
            Assert.IsTrue(after.LiableParties.Contains("Marta Kessler"), "The founder is now liable, on the record.");
            Assert.AreEqual(10000, result.CashPaidToCreditorsCents, "What cash existed went to the creditor.");
        }

        [Test]
        public void Acquisition_RealCash_AssumesObligations_ThroughSharedAuthority()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 12000);

            FinancialObligation obligation = f.Authority.Create(f.Ids, FinancialObligationKind.Loan,
                "business:biz-1", "Abel", 15000, 0, "12% annual", "seed loan");
            record.OpenObligationIds.Add(obligation.ObligationId);

            var buyerPurse = new PurseCashStore("Petra", 100000, "test seed capital");
            var sellerLedger = new HouseholdLedger(7);
            Assert.IsNull(sellerLedger.RecordInflow(0, 20000, HouseholdIncomeSource.OwnerContribution,
                "test-seed", "test seed capital", "test"));

            NpcBusinessAcquisitionResult result = service.AcquireBusiness(
                record, 77, "Petra Novak", 8, buyerPurse, sellerLedger,
                40000, true, f.Context(40), f.Diag);

            Assert.IsTrue(result.Completed, string.Join("; ", result.Notes));
            Assert.AreEqual(40000, result.PriceCents);
            Assert.AreEqual(60000, buyerPurse.ReadBalanceCents(), "Buyer paid real cash.");
            Assert.AreEqual(60000, sellerLedger.GetBalanceCents(), "Seller received the price as sale proceeds.");
            Assert.AreEqual(1, result.AssumedObligationIds.Count);

            FinancialObligation after = f.Authority.Find(obligation.ObligationId);
            Assert.IsNotNull(after);
            Assert.AreEqual("Petra Novak", after.Debtor, "Novation: the buyer is now the debtor, creditor-consented.");
            Assert.AreEqual(NpcBusinessLifeState.Acquired, record.State);
            Assert.AreEqual("Petra Novak", record.OwnerName);
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.BusinessAcquired));
        }

        [Test]
        public void BusinessDemand_RealPurchases_UnmetStaysVisible()
        {
            var events = new NpcBusinessEventLog();
            var diag = new List<string>();
            var cash = new FakeBusinessCashPort();
            cash.Seed("biz-1", 50000);
            var procurement = new FakeProcurementPort(cash, "flour", "yeast"); // only these are really stocked
            var planner = new NpcBusinessDemandPlanner();

            var standingNeeds = new List<NpcBusinessInputNeed>
            {
                new NpcBusinessInputNeed { BusinessInstanceId = "biz-1", Purpose = NpcBusinessDemandPurpose.Inputs, ItemId = "flour", UnitsPerWeek = 40, MaxPriceCentsPerUnit = 480, DerivedFromActivity = "bake-bread" },
                new NpcBusinessInputNeed { BusinessInstanceId = "biz-1", Purpose = NpcBusinessDemandPurpose.Inputs, ItemId = "sugar", UnitsPerWeek = 10, MaxPriceCentsPerUnit = 300, DerivedFromActivity = "bake-bread" },
            };

            List<NpcBusinessInputNeed> planned = planner.PlanWeek("biz-1", standingNeeds, events, 20, diag);
            Assert.AreEqual(2, planned.Count);

            NpcBusinessDemandResult result = planner.ExecutePurchases(planned, procurement, events, 20, diag);
            Assert.AreEqual(1, result.Purchases.Count);
            Assert.AreEqual("flour", result.Purchases[0].ItemId);
            Assert.AreEqual(40 * 480, result.Purchases[0].TotalCostCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Purchases[0].Provenance));
            Assert.AreEqual(50000 - 19200, cash.ReadCashCents("biz-1"), "Real cash left the business.");

            // The sugar need is NOT manufactured into existence — it stays unmet and visible.
            Assert.AreEqual(1, result.UnmetNeeds.Count);
            Assert.AreEqual("sugar", result.UnmetNeeds[0].ItemId);
            Assert.AreEqual(1, events.CountKind(NpcBusinessEventKind.DemandPlanned));
            Assert.AreEqual(1, events.CountKind(NpcBusinessEventKind.DemandPurchased));
        }

        [Test]
        public void SaveLoad_LifeRecord_AndEventLog_NoDuplication()
        {
            var record = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = "biz-1",
                BusinessEntityKey = "business:1",
                FounderPersonId = 42,
                OwnerName = "Marta Kessler",
                State = NpcBusinessLifeState.Struggling,
                CapabilityIds = new List<string> { "bake-bread" },
                OpenObligationIds = new List<string> { "obl-1" },
                EmploymentIds = new List<string> { "emp-1" },
                ConsecutiveStruggleWeeks = 3,
            };

            var restored = new NpcBusinessLifeRecord();
            restored.LoadFromSaveDto(record.CaptureSaveDto());
            Assert.AreEqual("biz-1", restored.BusinessInstanceId);
            Assert.AreEqual(NpcBusinessLifeState.Struggling, restored.State);
            Assert.AreEqual(3, restored.ConsecutiveStruggleWeeks);
            CollectionAssert.AreEquivalent(new[] { "obl-1" }, restored.OpenObligationIds);

            // Loading twice does not duplicate obligation/employment refs.
            restored.LoadFromSaveDto(record.CaptureSaveDto());
            Assert.AreEqual(1, restored.OpenObligationIds.Count);
            Assert.AreEqual(1, restored.EmploymentIds.Count);
        }
    }
}
