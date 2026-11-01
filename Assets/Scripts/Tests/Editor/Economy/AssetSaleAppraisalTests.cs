using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass IX WS5 (PASS8-003 + A3-08): the production appraisal source, the
    /// asset-sale execution step behind an approval, and the production
    /// liquidation port for orderly close-down.
    ///
    /// Rules under test: value kinds stay distinct (gross basis, estimated
    /// value, liquidation value, asking price, sale price); no register
    /// evidence means an explicit no-evidence appraisal and a DENIED sale,
    /// never an approval; an approved sale transfers the named asset and
    /// posts both cash legs exactly once (repeat execution refused);
    /// close-down liquidation pays the new liquidation value from a real
    /// buyer's cash.
    /// </summary>
    [TestFixture]
    public sealed class AssetSaleAppraisalTests
    {
        private const int Day = 420;
        private const int Reserve = 20000;

        private sealed class FakeBusinessCashPort : INpcBusinessCashPort
        {
            private readonly Dictionary<string, BusinessInstanceState> businesses;

            public FakeBusinessCashPort(Dictionary<string, BusinessInstanceState> businesses)
            {
                this.businesses = businesses;
            }

            public int ReadCashCents(string businessInstanceId)
            {
                return businesses.TryGetValue(businessInstanceId, out BusinessInstanceState business)
                    ? business.RuntimeState.CurrentCashCents : 0;
            }

            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                if (!businesses.TryGetValue(businessInstanceId, out BusinessInstanceState business))
                    return "unknown business";
                if (amountCents <= 0) return "amount must be positive";
                if (business.RuntimeState.CurrentCashCents < amountCents) return "insufficient cash";
                business.RuntimeState.SpendCents(amountCents);
                return null;
            }

            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                if (!businesses.TryGetValue(businessInstanceId, out BusinessInstanceState business))
                    return "unknown business";
                if (amountCents <= 0) return "amount must be positive";
                business.RuntimeState.AddCashCents(amountCents);
                return null;
            }
        }

        private sealed class Fixture
        {
            public readonly Dictionary<string, BusinessInstanceState> Businesses =
                new Dictionary<string, BusinessInstanceState>();
            public readonly Dictionary<string, EquipmentAsset> Equipment =
                new Dictionary<string, EquipmentAsset>();
            public readonly NpcFormationAssetRegister Formation = new NpcFormationAssetRegister();
            public readonly ManagerDecisionExecutionLedger Ledger = new ManagerDecisionExecutionLedger();
            public readonly List<string> Diag = new List<string>();

            public BusinessInstanceState AddBusiness(string id, int cashCents)
            {
                var business = new BusinessInstanceState();
                SetField(business, "instanceId", id);
                business.SetOwner(BusinessOwnerIdentity.Npc(42, "Marta Kessler", "Kessler"));
                var runtime = new BusinessRuntimeState();
                runtime.BindBusinessIdentity(id);
                runtime.SetCurrentCashCents(cashCents);
                SetField(business, "runtimeState", runtime);
                Businesses[id] = business;
                return business;
            }

            public EquipmentAsset AddEquipment(string assetId, string ownerBusinessId,
                float condition01, int bookCostCents)
            {
                var asset = new EquipmentAsset
                {
                    AssetId = assetId,
                    Kind = "plow",
                    DisplayName = "Plow",
                    Condition01 = condition01,
                    OwnerKind = "business",
                    OwnerId = ownerBusinessId,
                };
                Equipment[assetId] = asset;
                BusinessInstanceState owner = Businesses[ownerBusinessId];
                owner.GenericConfiguration.AddEquipmentReference(assetId);
                if (bookCostCents > 0)
                {
                    owner.GenericConfiguration.AddEquipmentProcurement(new EquipmentProcurementState
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

                return asset;
            }

            public AssetSaleAppraisalService Appraisal()
            {
                return new AssetSaleAppraisalService
                {
                    FindEquipmentAsset = id => Equipment.TryGetValue(id ?? string.Empty, out EquipmentAsset found) ? found : null,
                    FormationAssets = Formation,
                    EquipmentBookCostCents = (businessId, assetId) =>
                    {
                        if (!Businesses.TryGetValue(businessId, out BusinessInstanceState business)) return 0;
                        foreach (EquipmentProcurementState procurement in business.GenericConfiguration.EquipmentProcurements)
                        {
                            if (procurement != null && procurement.Order != null
                                && procurement.AssetId == assetId)
                                return procurement.Order.TotalCents;
                        }

                        return 0;
                    },
                };
            }

            public AssetSaleExecutionContext ExecutionContext()
            {
                return new AssetSaleExecutionContext
                {
                    FindBusiness = id => Businesses.TryGetValue(id, out BusinessInstanceState found) ? found : null,
                    AllBusinesses = () => Businesses.Values,
                    FindEquipmentAsset = id => Equipment.TryGetValue(id ?? string.Empty, out EquipmentAsset found) ? found : null,
                    FormationAssets = Formation,
                    DayIndex = Day,
                };
            }

            public ManagerPendingRequest AddRequest(string businessId, string assetId, int amountCents, string status)
            {
                var request = new ManagerPendingRequest
                {
                    RequestId = $"mgr-req:sellasset:{businessId}:d{Day}:{amountCents}c",
                    RequestKey = $"SellAsset:{businessId}:{assetId}:{amountCents}c",
                    BusinessInstanceId = businessId,
                    Kind = ManagerActKinds.SellAsset,
                    AmountCents = amountCents,
                    AssetId = assetId,
                    Detail = "test",
                    CreatedDayIndex = Day,
                    ManagerPersonId = 7,
                    Status = status,
                };
                Ledger.AddPending(request);
                return request;
            }

            private static void SetField(object target, string name, object value)
            {
                FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(field, "field " + name);
                field.SetValue(target, value);
            }
        }

        // ---------- Appraisal ----------

        [Test]
        public void Appraisal_Equipment_KeepsValueKindsDistinct()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000);
            f.AddEquipment("plow-1", "store-1", 0.5f, 10000);

            AssetAppraisalResult result = f.Appraisal().Appraise("store-1", "plow-1");

            Assert.IsTrue(result.HasEvidence);
            Assert.AreEqual(10000, result.GrossAssetValueCents, "gross = undiscounted book basis");
            Assert.AreEqual(5000, result.EstimatedValueCents, "estimated = book x condition");
            Assert.AreEqual(3000, result.LiquidationValueCents, "liquidation = 60% of estimated");
            Assert.AreEqual(5000, result.AskingPriceCents, "asking is its own kind, not the sale price");
            StringAssert.Contains("procurement record", result.BasisNote);
        }

        [Test]
        public void Appraisal_Formation_FromRecordedCostAndProvenance()
        {
            var f = new Fixture();
            f.Formation.Record(new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "counter-scales",
                OwnerBusinessId = "store-1",
                Units = 1,
                CostCents = 8000,
                Provenance = "purchase:smithy-1",
            });

            AssetAppraisalResult result = f.Appraisal().Appraise("store-1", "formation:counter-scales");

            Assert.IsTrue(result.HasEvidence);
            Assert.AreEqual(8000, result.GrossAssetValueCents);
            Assert.AreEqual(8000, result.EstimatedValueCents);
            Assert.AreEqual(4800, result.LiquidationValueCents);
        }

        [Test]
        public void Appraisal_NoRegisterEvidence_ReturnsNoEvidence_NeverANumber()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000);
            f.AddEquipment("plow-1", "store-1", 0.5f, 0); // register entry, NO procurement record

            AssetSaleAppraisalService appraisal = f.Appraisal();
            AssetAppraisalResult result = appraisal.Appraise("store-1", "plow-1");

            Assert.IsFalse(result.HasEvidence, "no book-cost evidence -> explicit no-evidence result");
            Assert.AreEqual(0, result.EstimatedValueCents);
            StringAssert.Contains("no book-cost evidence", result.BasisNote);
            Assert.AreEqual(-1, appraisal.AppraiseSaleCents(new ManagerPendingRequest
            {
                BusinessInstanceId = "store-1",
                AssetId = "plow-1",
                AmountCents = 4000,
            }), "the resolver seam reads -1 (unknown) — the owner never approves blind");
        }

        [Test]
        public void Appraisal_AssetHeldByAnotherBusiness_NoEvidence()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000);
            f.AddBusiness("store-2", 1000);
            f.AddEquipment("plow-1", "store-2", 0.5f, 10000);

            AssetAppraisalResult result = f.Appraisal().Appraise("store-1", "plow-1");

            Assert.IsFalse(result.HasEvidence, "another business's asset is not the seller's to appraise");
        }

        [Test]
        public void Appraisal_UnnamedAsset_NoEvidence()
        {
            var f = new Fixture();
            AssetAppraisalResult result = f.Appraisal().Appraise("store-1", string.Empty);
            Assert.IsFalse(result.HasEvidence);
            StringAssert.Contains("names no asset", result.BasisNote);
        }

        // ---------- Resolver denial without evidence ----------

        [Test]
        public void SaleDenied_WhenRegistersHoldNoEvidence_NeverApprovedBlind()
        {
            var f = new Fixture();
            BusinessInstanceState seller = f.AddBusiness("store-1", 1000); // distressed
            f.AddEquipment("plow-1", "store-1", 0.5f, 0); // no book evidence
            ManagerPendingRequest request = f.AddRequest("store-1", "plow-1", 4000, "Pending");

            var resolver = new NpcOwnerRequestResolver();
            resolver.ResolvePending(new NpcOwnerRequestResolver.OwnerResolutionContext
            {
                Ledger = f.Ledger,
                FindBusiness = id => f.Businesses.TryGetValue(id, out BusinessInstanceState found) ? found : null,
                SurvivalReserveCents = business => Reserve,
                AppraiseSaleCents = f.Appraisal().AppraiseSaleCents, // the production source
                DayIndex = Day,
            }, f.Diag);

            Assert.AreEqual("Rejected", request.Status, "no evidence -> denied, never approved blind");
            StringAssert.Contains("no appraisal", request.ResolutionNote);
            Assert.AreEqual(1000, seller.RuntimeState.CurrentCashCents, "nothing moved");
        }

        // ---------- Full chain: originate -> request -> approve -> execute ----------

        [Test]
        public void ApprovedNamedAssetSale_Executes_TransfersAndPostsBothLegsExactlyOnce()
        {
            var f = new Fixture();
            BusinessInstanceState seller = f.AddBusiness("store-1", 1000); // distressed
            BusinessInstanceState buyer = f.AddBusiness("store-2", 50000);
            EquipmentAsset asset = f.AddEquipment("plow-1", "store-1", 0.5f, 10000);

            // The resolver approves with the production appraisal (estimated 5000).
            ManagerPendingRequest request = f.AddRequest("store-1", "plow-1", 5000, "Pending");
            var resolver = new NpcOwnerRequestResolver();
            resolver.ResolvePending(new NpcOwnerRequestResolver.OwnerResolutionContext
            {
                Ledger = f.Ledger,
                FindBusiness = id => f.Businesses.TryGetValue(id, out BusinessInstanceState found) ? found : null,
                SurvivalReserveCents = business => Reserve,
                AppraiseSaleCents = f.Appraisal().AppraiseSaleCents,
                DayIndex = Day,
            }, f.Diag);
            Assert.AreEqual("Approved", request.Status, "distress + price at appraised value approves");

            var execution = new AssetSaleExecutionService();
            int executed = execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag);

            Assert.AreEqual(1, executed);
            Assert.AreEqual("Executed", request.Status);
            Assert.AreEqual("store-2", asset.OwnerId, "title transferred to the buyer");
            Assert.AreEqual(1, asset.OwnershipHistory.Count, "the transfer is on the ownership chain");
            Assert.AreEqual(6000, seller.RuntimeState.CurrentCashCents, "seller leg: +5000 sale price");
            Assert.AreEqual(45000, buyer.RuntimeState.CurrentCashCents, "buyer leg: -5000 sale price");
            Assert.IsFalse(SellerStillReferences(seller, "plow-1"), "seller no longer claims the asset");
            Assert.IsTrue(BuyerReferences(buyer, "plow-1"), "buyer now holds the asset reference");
            Assert.AreEqual(1, execution.CompletedSales.Count);
            Assert.AreEqual(5000, execution.CompletedSales[0].SalePriceCents,
                "the SALE PRICE is what transacted — distinct from asking/appraised kinds");

            // Repeat execution is refused: nothing posts a second time.
            int again = execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag);
            Assert.AreEqual(0, again, "an executed sale never re-executes");
            Assert.AreEqual(6000, seller.RuntimeState.CurrentCashCents);
            Assert.AreEqual(45000, buyer.RuntimeState.CurrentCashCents);
            Assert.AreEqual(1, execution.CompletedSales.Count);
        }

        [Test]
        public void Execution_PendingOrRejectedRequest_Refused()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000);
            f.AddBusiness("store-2", 50000);
            f.AddEquipment("plow-1", "store-1", 0.5f, 10000);
            ManagerPendingRequest request = f.AddRequest("store-1", "plow-1", 5000, "Pending");

            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(0, execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag),
                "only an APPROVED sale executes");
            Assert.AreEqual("store-1", f.Equipment["plow-1"].OwnerId);
        }

        [Test]
        public void Execution_NoSolventBuyer_StaysApprovedAndMovesNothing()
        {
            var f = new Fixture();
            BusinessInstanceState seller = f.AddBusiness("store-1", 1000);
            f.AddBusiness("store-2", 3000); // cannot cover the 5000 price
            f.AddEquipment("plow-1", "store-1", 0.5f, 10000);
            ManagerPendingRequest request = f.AddRequest("store-1", "plow-1", 5000, "Approved");

            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(0, execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag));

            Assert.AreEqual("Approved", request.Status, "the owner's consent stands; execution waits for a real buyer");
            StringAssert.Contains("no other business can pay", request.ResolutionNote);
            Assert.AreEqual(1000, seller.RuntimeState.CurrentCashCents, "no cash moved");
            Assert.AreEqual("store-1", f.Equipment["plow-1"].OwnerId, "no title moved");
        }

        [Test]
        public void Execution_FormationAsset_TransfersOwnershipToBuyer()
        {
            var f = new Fixture();
            BusinessInstanceState seller = f.AddBusiness("store-1", 1000);
            BusinessInstanceState buyer = f.AddBusiness("store-2", 50000);
            Assert.IsNull(f.Formation.Record(new NpcFormationAsset
            {
                AssetKind = "inventory",
                ItemId = "flour-stock",
                OwnerBusinessId = "store-1",
                Units = 20,
                CostCents = 8000,
                Provenance = "purchase:mill-1",
            }));
            ManagerPendingRequest request = f.AddRequest("store-1", "formation:flour-stock", 4800, "Approved");

            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(1, execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag));

            NpcFormationAsset asset = f.Formation.Assets[0];
            Assert.IsFalse(asset.Liquidated, "R-12: a sale is not a liquidation — the buyer receives the asset");
            Assert.AreEqual("store-2", asset.OwnerBusinessId, "R-01/R-12: ownership transfers to the buyer in the register");
            Assert.AreEqual(5800, seller.RuntimeState.CurrentCashCents);
            Assert.AreEqual(45200, buyer.RuntimeState.CurrentCashCents);
            Assert.AreEqual("store-2", execution.CompletedSales[0].BuyerBusinessId);
        }

        [Test]
        public void FullChain_LiveStateToExecutedSale_AssetIdRidesEveryPayload()
        {
            var f = new Fixture();
            BusinessInstanceState seller = f.AddBusiness("store-1", 1000); // distressed vs the 20000 reserve
            BusinessInstanceState buyer = f.AddBusiness("store-2", 50000);
            EquipmentAsset asset = f.AddEquipment("plow-1", "store-1", 0.6f, 10000);

            // 1. Origination: the inputs builder derives the sale from live state.
            var builder = new ManagerCycleInputsBuilder(f.Appraisal());
            ManagerCycleInputs inputs = builder.Build(new ManagerCycleLiveEvidence
            {
                Business = seller,
                DemandReport = new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                Authority = new DelegatedAuthority
                {
                    AuthorityId = "auth-1",
                    BusinessInstanceId = "store-1",
                    DelegatePersonId = 7,
                    Scopes = DelegationScope.AllRoutine,
                    PurchaseLimitCents = 20000,
                    GrantedDayIndex = 400,
                },
                ManagerPersonId = 7,
                DayIndex = Day,
                SurvivalReserveCents = Reserve,
                EquipmentAssets = f.Equipment.Values,
                JudgmentSeed = 42,
                JudgmentQuality01 = 1f,
            }, f.Diag);
            Assert.AreEqual("plow-1", inputs.ProposedAssetId);
            Assert.AreEqual(6000, inputs.ProposedAssetSaleCents, "asking = estimated 10000 x 0.6");

            // 2. The brain escalates the owner-reserved sale, asset id intact.
            ManagerDecisionResult decision = new ManagerBrain().WeeklyCycle(inputs, f.Diag);
            Assert.AreEqual(ManagerActKinds.SellAsset, decision.ActionKind);
            Assert.IsTrue(decision.Escalated);
            Assert.AreEqual("plow-1", decision.ProposedAssetId);

            // 3. The real SellAsset executor turns it into a pending request
            //    that carries the named asset.
            var executionService = new ManagerDecisionExecutionService();
            executionService.Configure(new ManagerExecutionServices
            {
                FindBusiness = id => f.Businesses.TryGetValue(id, out BusinessInstanceState found) ? found : null,
                AllBusinesses = () => f.Businesses.Values,
            });
            var authority = new DelegatedAuthority
            {
                AuthorityId = "auth-1",
                BusinessInstanceId = "store-1",
                DelegatePersonId = 7,
                Scopes = DelegationScope.AllRoutine,
                PurchaseLimitCents = 20000,
                GrantedDayIndex = 400,
            };
            ManagerExecutionOutcome outcome = executionService.ExecuteDecision(
                seller, authority, 7, inputs.DemandReport, decision, Day, f.Diag);
            Assert.IsTrue(outcome.Pending, "owner-reserved sale becomes a pending request");
            Assert.AreEqual(1, executionService.Ledger.PendingRequests.Count);
            ManagerPendingRequest request = executionService.Ledger.PendingRequests[0];
            Assert.AreEqual("plow-1", request.AssetId, "the pending request names the asset");
            Assert.AreEqual(6000, request.AmountCents);

            // 4. NPC owner resolution approves against the production appraisal.
            var resolver = new NpcOwnerRequestResolver();
            resolver.ResolvePending(new NpcOwnerRequestResolver.OwnerResolutionContext
            {
                Ledger = executionService.Ledger,
                FindBusiness = id => f.Businesses.TryGetValue(id, out BusinessInstanceState found) ? found : null,
                SurvivalReserveCents = business => Reserve,
                AppraiseSaleCents = f.Appraisal().AppraiseSaleCents,
                DayIndex = Day,
            }, f.Diag);
            Assert.AreEqual("Approved", request.Status);

            // 5. Execution: title transfers, both legs post exactly once.
            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(1, execution.ExecuteApprovedSales(
                executionService.Ledger, f.ExecutionContext(), f.Diag));
            Assert.AreEqual("Executed", request.Status);
            Assert.AreEqual("store-2", asset.OwnerId);
            Assert.AreEqual(7000, seller.RuntimeState.CurrentCashCents);
            Assert.AreEqual(44000, buyer.RuntimeState.CurrentCashCents);
        }

        // ---------- Liquidation port + CloseOrderly ----------

        [Test]
        public void CloseOrderly_WithProductionLiquidationPort_LiquidationValueFlows()
        {
            var f = new Fixture();
            BusinessInstanceState closing = f.AddBusiness("store-1", 0);
            BusinessInstanceState buyerBiz = f.AddBusiness("store-2", 50000);
            Assert.IsNull(f.Formation.Record(new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "counter-scales",
                OwnerBusinessId = "store-1",
                Units = 1,
                CostCents = 10000,
                Provenance = "purchase:smithy-1",
            }));

            var cashPort = new FakeBusinessCashPort(f.Businesses);
            var port = new RegisterLiquidationPort(() => f.Businesses.Values, cashPort, f.Appraisal());
            var founderLedger = new HouseholdLedger(7);
            Assert.IsNull(founderLedger.RecordInflow(0, 5000, HouseholdIncomeSource.OwnerContribution,
                "test-seed", "test seed capital", "test"));

            var record = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = "store-1",
                BusinessEntityKey = "business:store-1",
                FounderPersonId = 42,
                OwnerName = "Marta Kessler",
                State = NpcBusinessLifeState.Operating,
            };
            var service = new NpcBusinessLifecycleService();
            NpcBusinessCloseResult result = service.CloseOrderly(record, "the founder retires",
                new NpcLifecycleContext
                {
                    Cash = cashPort,
                    Obligations = new FinancialObligationAuthority(),
                    Employments = new EmploymentRelationshipRegistry(),
                    FounderHouseholdLedger = founderLedger,
                    AssetRegister = f.Formation,
                    Liquidation = port,
                    Events = new NpcBusinessEventLog(),
                    Ids = new EntityIdRegistry(),
                    DayIndex = Day,
                }, f.Diag);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(1, result.AssetsLiquidated);
            Assert.AreEqual(6000, result.LiquidationProceedsCents,
                "liquidation value = 60% of the 10000c recorded cost — the new liquidation value flows in");
            Assert.AreEqual(44000, buyerBiz.RuntimeState.CurrentCashCents,
                "the buyer business really paid the proceeds");
            Assert.AreEqual(0, closing.RuntimeState.CurrentCashCents,
                "proceeds arrived, then the residual left to the founder");
            Assert.AreEqual(11000, founderLedger.GetBalanceCents(),
                "founder received the 6000c residual (5000 seed + 6000)");
            Assert.AreEqual(6000, result.ResidualToFounderCents);
            Assert.AreEqual(NpcBusinessLifeState.Closed, record.State);
        }

        [Test]
        public void LiquidationPort_NoSolventBuyer_RefusesAndMovesNothing()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 0);
            BusinessInstanceState poor = f.AddBusiness("store-2", 100); // cannot cover 6000
            var asset = new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "counter-scales",
                OwnerBusinessId = "store-1",
                Units = 1,
                CostCents = 10000,
                Provenance = "purchase:smithy-1",
            };
            var cashPort = new FakeBusinessCashPort(f.Businesses);
            var port = new RegisterLiquidationPort(() => f.Businesses.Values, cashPort, f.Appraisal());

            string refusal = port.Liquidate("store-1", asset, Day, out NpcLiquidationSale sale);

            Assert.IsNotNull(refusal, "no solvent buyer -> loud refusal, not a fictional sale");
            StringAssert.Contains("no solvent buyer", refusal);
            Assert.IsNull(sale);
            Assert.AreEqual(100, poor.RuntimeState.CurrentCashCents, "nothing moved");
        }

        [Test]
        public void LiquidationPort_NoEvidence_Refuses()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 0);
            f.AddBusiness("store-2", 50000);
            var asset = new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "mystery-crate",
                OwnerBusinessId = "store-1",
                Units = 1,
                CostCents = 0, // no recorded cost — no evidence
                Provenance = "purchase:smithy-1",
            };
            var cashPort = new FakeBusinessCashPort(f.Businesses);
            var port = new RegisterLiquidationPort(() => f.Businesses.Values, cashPort, f.Appraisal());

            string refusal = port.Liquidate("store-1", asset, Day, out NpcLiquidationSale sale);

            Assert.IsNotNull(refusal);
            StringAssert.Contains("no liquidation without evidence", refusal);
            Assert.IsNull(sale);
        }

        private static bool SellerStillReferences(BusinessInstanceState business, string assetId)
        {
            foreach (string id in business.GenericConfiguration.EquipmentAssetIds)
            {
                if (id == assetId) return true;
            }

            return false;
        }

        private static bool BuyerReferences(BusinessInstanceState business, string assetId)
        {
            return SellerStillReferences(business, assetId);
        }
    }
}
