using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass IX WS5 (A3-07): every one of the seven ManagerBrain decision
    /// kinds originates from LIVE-state fixtures through
    /// ManagerCycleInputsBuilder — the production origination path that was
    /// missing when DriveAutonomousLabor set no evidence fields and
    /// hard-coded MaxBorrowingCents = 0. A3-13's market-capture feed is
    /// covered here too.
    /// </summary>
    [TestFixture]
    public sealed class ManagerCycleInputsBuilderTests
    {
        private const int Day = 413; // a weekly-tick day boundary is irrelevant here; cycles are called directly
        private const int Reserve = 10000;

        private sealed class Fixture
        {
            public readonly BusinessInstanceState Business;
            public readonly List<EquipmentAsset> Equipment = new List<EquipmentAsset>();
            public readonly List<NpcFormationAsset> Formation = new List<NpcFormationAsset>();
            public readonly List<StandingInstruction> Instructions = new List<StandingInstruction>();
            public readonly List<string> Diag = new List<string>();
            public LaborDemandReport Demand = new LaborDemandReport
            {
                BusinessInstanceId = "store-1",
                DayIndex = Day,
                EvidenceSummary = "steady trade",
            };
            public bool HasMarketReadout;
            public float Capture01 = 0.9f;
            public float PriceIndex01 = 0.1f;
            public int Cash = 50000;

            public Fixture()
            {
                Business = MakeBusiness("store-1", Cash);
            }

            public AssetSaleAppraisalService Appraisal()
            {
                var byId = new Dictionary<string, EquipmentAsset>();
                foreach (EquipmentAsset asset in Equipment) byId[asset.AssetId] = asset;
                var register = new NpcFormationAssetRegister();
                foreach (NpcFormationAsset asset in Formation) register.Record(asset);
                return new AssetSaleAppraisalService
                {
                    FindEquipmentAsset = id => byId.TryGetValue(id ?? string.Empty, out EquipmentAsset found) ? found : null,
                    FormationAssets = register,
                    EquipmentBookCostCents = (businessId, assetId) =>
                    {
                        foreach (EquipmentProcurementState procurement in Business.GenericConfiguration.EquipmentProcurements)
                        {
                            if (procurement != null && procurement.Order != null
                                && procurement.AssetId == assetId)
                                return procurement.Order.TotalCents;
                        }

                        return 0;
                    },
                };
            }

            public ManagerCycleInputs Build()
            {
                var builder = new ManagerCycleInputsBuilder(Appraisal());
                return builder.Build(new ManagerCycleLiveEvidence
                {
                    Business = Business,
                    DemandReport = Demand,
                    Authority = new DelegatedAuthority
                    {
                        AuthorityId = "auth-1",
                        BusinessInstanceId = "store-1",
                        DelegatePersonId = 7,
                        Scopes = DelegationScope.AllRoutine,
                        PurchaseLimitCents = 20000,
                        GrantedDayIndex = 400,
                    },
                    Instructions = Instructions,
                    ManagerPersonId = 7,
                    DayIndex = Day,
                    SurvivalReserveCents = Reserve,
                    EquipmentAssets = Equipment,
                    FormationAssets = Formation,
                    HasMarketReadout = HasMarketReadout,
                    MarketCapture01 = Capture01,
                    PriceIndex01 = PriceIndex01,
                    JudgmentSeed = 42,
                    JudgmentQuality01 = 1f,
                }, Diag);
            }

            public void SetCash(int cents)
            {
                Business.RuntimeState.SetCurrentCashCents(cents);
            }

            public void AddEquipment(string assetId, float condition01, int bookCostCents)
            {
                Equipment.Add(new EquipmentAsset
                {
                    AssetId = assetId,
                    Kind = "plow",
                    DisplayName = "Plow",
                    Condition01 = condition01,
                    OwnerKind = "business",
                    OwnerId = "store-1",
                });
                if (bookCostCents > 0)
                {
                    Business.GenericConfiguration.AddEquipmentProcurement(new EquipmentProcurementState
                    {
                        AcquisitionId = "acq-" + assetId,
                        AssetId = assetId,
                        Order = new SupplierPurchaseOrder
                        {
                            OrderId = "ord-" + assetId,
                            Lines = new List<SupplierOrderLine>
                            {
                                new SupplierOrderLine { ProductId = "plow", Quantity = 1f, UnitPriceCents = bookCostCents },
                            },
                        },
                    });
                }

                Business.GenericConfiguration.AddEquipmentReference(assetId);
            }
        }

        private static BusinessInstanceState MakeBusiness(string id, int cashCents)
        {
            var business = new BusinessInstanceState();
            FieldInfo idField = typeof(BusinessInstanceState).GetField("instanceId",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(idField);
            idField.SetValue(business, id);
            business.SetOwner(BusinessOwnerIdentity.Npc(42, "Marta Kessler", "Kessler"));
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cashCents);
            FieldInfo runtimeField = typeof(BusinessInstanceState).GetField("runtimeState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(runtimeField);
            runtimeField.SetValue(business, runtime);
            return business;
        }

        // ---------- Procure ----------

        [Test]
        public void Procure_OriginatesFromLiveStockShortage()
        {
            var f = new Fixture();
            // Live stock: 10 of a 100-unit target (health 0.1 — the runtime's
            // "low/critical" posture), recorded unit cost 50c.
            f.Business.RuntimeState.EnsureCategoryStock("flour", 10, 100);
            f.Business.GenericConfiguration.AddInventory("flour", 10f, 50);

            ManagerCycleInputs inputs = f.Build();

            Assert.IsTrue(inputs.ShortageObserved, "stock vs reorder target is a shortage");
            Assert.AreEqual(90 * 50, inputs.ProposedProcurementCents,
                "missing units priced at the recorded average unit cost");

            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.IsTrue(decision.Acted);
            Assert.AreEqual(ManagerActKinds.Procure, decision.ActionKind,
                "the Procure kind originates from live state");
        }

        [Test]
        public void Procure_OriginatesFromRetailLineAtReorderPoint()
        {
            var f = new Fixture();
            f.Business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("bread", "storage", 20f, 30f, 5f, 100));
            f.Business.GenericConfiguration.AddInventory("bread", 3f, 40);

            ManagerCycleInputs inputs = f.Build();

            Assert.IsTrue(inputs.ShortageObserved);
            Assert.AreEqual(17 * 40, inputs.ProposedProcurementCents);
            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.AreEqual(ManagerActKinds.Procure, decision.ActionKind);
        }

        [Test]
        public void Shortage_WithoutRecordedCost_ProposesNoAmount()
        {
            var f = new Fixture();
            f.Business.RuntimeState.EnsureCategoryStock("flour", 0, 100);
            // No inventory position -> no recorded unit cost -> no confident amount.

            ManagerCycleInputs inputs = f.Build();

            Assert.IsFalse(inputs.ShortageObserved, "no evidence, no procurement proposal");
            Assert.AreEqual(0, inputs.ProposedProcurementCents);
        }

        // ---------- OrderMaintenance ----------

        [Test]
        public void Maintenance_OriginatesFromEquipmentCondition()
        {
            var f = new Fixture();
            f.AddEquipment("plow-1", 0.3f, 0); // worn, below the 0.5 threshold

            ManagerCycleInputs inputs = f.Build();

            Assert.IsTrue(inputs.MaintenanceNeeded, "condition below the worn threshold");
            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.IsTrue(decision.Acted);
            Assert.AreEqual(ManagerActKinds.OrderMaintenance, decision.ActionKind);
        }

        [Test]
        public void Maintenance_SoundEquipment_NoProposal()
        {
            var f = new Fixture();
            f.AddEquipment("plow-1", 0.9f, 0);

            Assert.IsFalse(f.Build().MaintenanceNeeded);
        }

        // ---------- PriceChange ----------

        [Test]
        public void Pricing_OriginatesFromWeakMarketCapture()
        {
            var f = new Fixture();
            f.HasMarketReadout = true;
            f.Capture01 = 0.3f; // weak capture from the market-capture computation

            ManagerCycleInputs inputs = f.Build();

            Assert.IsTrue(inputs.PricingAdjustmentRequested);
            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.IsTrue(decision.Acted);
            Assert.AreEqual(ManagerActKinds.PriceChange, decision.ActionKind);
        }

        [Test]
        public void Pricing_StrongCaptureAndLowPriceIndex_NoProposal()
        {
            var f = new Fixture();
            f.HasMarketReadout = true;
            f.Capture01 = 0.9f;
            f.PriceIndex01 = 0.1f;

            Assert.IsFalse(f.Build().PricingAdjustmentRequested);
        }

        // ---------- Borrow ----------

        [Test]
        public void Borrow_OriginatesFromCashReserveGap_WithRealMaxBorrowing()
        {
            var f = new Fixture();
            f.SetCash(4000); // 6000 below the 10000 reserve
            f.Instructions.Add(new StandingInstruction
            {
                InstructionId = "mj-borrow",
                BusinessInstanceId = "store-1",
                Kind = InstructionKind.ManagerJudgment,
                Objective = "keep a working-capital line",
                MaxBorrowingCents = 20000,
                CreatedByPersonId = 42,
                CreatedDayIndex = 400,
            });

            ManagerCycleInputs inputs = f.Build();

            Assert.AreEqual(6000, inputs.ProposedBorrowingCents, "exactly the cash-vs-reserve gap");
            Assert.AreEqual(20000, inputs.MaxBorrowingCents,
                "the delegation's real bound — not the old hard-coded 0");

            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.AreEqual(ManagerActKinds.Borrow, decision.ActionKind,
                "the Borrow kind originates from live state");
            Assert.IsTrue(decision.Escalated, "borrowing escalates for owner approval");
            StringAssert.Contains("6000c", decision.ResultingReference);
        }

        [Test]
        public void Borrow_NoInstructionAuthorizes_ZeroMaxBorrowing()
        {
            var f = new Fixture();
            f.SetCash(4000);

            ManagerCycleInputs inputs = f.Build();

            Assert.AreEqual(6000, inputs.ProposedBorrowingCents);
            Assert.AreEqual(0, inputs.MaxBorrowingCents, "no live instruction -> no borrowing authorized");
            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.IsTrue(decision.Escalated);
            StringAssert.Contains("max borrowing permission", decision.EscalationReason);
        }

        // ---------- SellAsset ----------

        [Test]
        public void SellAsset_OriginatesFromDistress_NamingTheSpecificAsset()
        {
            var f = new Fixture();
            f.SetCash(1000); // distressed: below the 10000 reserve
            f.AddEquipment("plow-1", 0.6f, 10000); // book 10000, condition 0.6 (serviceable — no maintenance candidate)

            ManagerCycleInputs inputs = f.Build();

            Assert.AreEqual("plow-1", inputs.ProposedAssetId,
                "the proposal names the specific register asset (coordinator decision 3)");
            // Estimated 10000 x 0.6 = 6000; the ask is the estimated value
            // (liquidation value 3600 is the distinct close-down basis).
            Assert.AreEqual(6000, inputs.ProposedAssetSaleCents,
                "the ask is the named asset's asking price (estimated value)");

            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.AreEqual(ManagerActKinds.SellAsset, decision.ActionKind,
                "SellAsset originates — it outranks Borrow on evidence");
            Assert.IsTrue(decision.Escalated, "asset sales are owner-reserved");
            Assert.AreEqual("plow-1", decision.ProposedAssetId,
                "the asset id survives into the decision payload");
        }

        [Test]
        public void SellAsset_HealthyCash_NoProposal()
        {
            var f = new Fixture();
            f.AddEquipment("plow-1", 0.4f, 10000);

            ManagerCycleInputs inputs = f.Build();

            Assert.AreEqual(0, inputs.ProposedAssetSaleCents, "no distress, no sale proposal");
            Assert.IsTrue(string.IsNullOrEmpty(inputs.ProposedAssetId));
        }

        [Test]
        public void SellAsset_DistressButNoEvidencedAsset_NoProposal()
        {
            var f = new Fixture();
            f.SetCash(1000);
            f.AddEquipment("plow-1", 0.4f, 0); // no procurement record -> no book evidence

            ManagerCycleInputs inputs = f.Build();

            Assert.AreEqual(0, inputs.ProposedAssetSaleCents,
                "an asset without register evidence is never proposed for sale");
            Assert.IsTrue(string.IsNullOrEmpty(inputs.ProposedAssetId));
        }

        // ---------- OpenRecruitment / Schedule (demand-derived, still live) ----------

        [Test]
        public void OpenRecruitment_OriginatesFromHiringPressure()
        {
            var f = new Fixture();
            f.Demand.HiringPressure = true;
            f.Demand.TotalUnmetMinutes = 900;

            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(f.Build(), f.Diag);

            Assert.IsTrue(decision.Acted);
            Assert.AreEqual(ManagerActKinds.OpenRecruitment, decision.ActionKind);
        }

        [Test]
        public void Schedule_OriginatesFromCoverageGap()
        {
            var f = new Fixture();
            f.Demand.CoverageGap = true;
            f.Demand.TotalUnmetMinutes = 300;

            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(f.Build(), f.Diag);

            Assert.IsTrue(decision.Acted);
            Assert.AreEqual(ManagerActKinds.Schedule, decision.ActionKind);
        }

        [Test]
        public void HealthyBusiness_WeeklyCycleActsOnNothing()
        {
            var f = new Fixture();
            f.Business.RuntimeState.EnsureCategoryStock("flour", 100, 100);
            f.Business.GenericConfiguration.AddInventory("flour", 100f, 50);
            f.AddEquipment("plow-1", 0.9f, 10000);
            f.HasMarketReadout = true;

            ManagerCycleInputs inputs = f.Build();

            Assert.IsFalse(inputs.ShortageObserved);
            Assert.IsFalse(inputs.MaintenanceNeeded);
            Assert.IsFalse(inputs.PricingAdjustmentRequested);
            Assert.AreEqual(0, inputs.ProposedBorrowingCents);
            Assert.AreEqual(0, inputs.ProposedAssetSaleCents);
            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.IsFalse(decision.Acted, "a healthy business needs no manager act");
        }

        // ---------- A3-13: market-capture feed ----------

        [Test]
        public void MarketCaptureFeed_SetsCompetitionPressureFromCapture()
        {
            var runtime = new BusinessRuntimeState();
            Assert.AreEqual(0f, runtime.CompetitionPressure01, "the scalar starts unfed at 0");

            MarketCaptureCompetitionFeed.FeedCompetitionPressure(runtime, 0.25f);

            Assert.AreEqual(0.75f, runtime.CompetitionPressure01, 0.0001f,
                "pressure is the market share the business does NOT capture");
            Assert.AreEqual(1f, runtime.Reliability01, "the feed touches nothing else");
        }
    }
}
