using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS3 (PASS9-001): an executed asset sale survives
    /// save/load as a record on the decision ledger, and re-execution
    /// is refused from that persisted record even if the request
    /// status was reset to Approved — title transfer already
    /// persisted, but the execution record itself was runtime-only.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3AssetSaleRecordPersistenceTests
    {
        private const int Day = 420;

        private sealed class Fixture
        {
            public readonly Dictionary<string, BusinessInstanceState> Businesses =
                new Dictionary<string, BusinessInstanceState>();
            public readonly Dictionary<string, EquipmentAsset> Equipment =
                new Dictionary<string, EquipmentAsset>();
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

            public void AddEquipment(string assetId, string ownerBusinessId, int bookCostCents)
            {
                var asset = new EquipmentAsset
                {
                    AssetId = assetId,
                    Kind = "plow",
                    DisplayName = "Plow",
                    Condition01 = 0.5f,
                    OwnerKind = "business",
                    OwnerId = ownerBusinessId,
                };
                Equipment[assetId] = asset;
                Businesses[ownerBusinessId].GenericConfiguration.AddEquipmentReference(assetId);
                Businesses[ownerBusinessId].GenericConfiguration.AddEquipmentProcurement(new EquipmentProcurementState
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

            public AssetSaleExecutionContext ExecutionContext()
            {
                return new AssetSaleExecutionContext
                {
                    FindBusiness = id => Businesses.TryGetValue(id, out BusinessInstanceState found) ? found : null,
                    AllBusinesses = () => Businesses.Values,
                    FindEquipmentAsset = id => Equipment.TryGetValue(id ?? string.Empty, out EquipmentAsset found) ? found : null,
                    DayIndex = Day,
                };
            }

            public ManagerPendingRequest AddRequest(string businessId, string assetId, int amountCents)
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
                    Status = "Approved",
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

        [Test]
        public void PersistedRecordAlone_RefusesReExecution_WithoutActionKeyOrStatusGate()
        {
            // The record gate must stand on its own: a ledger holding
            // ONLY the completed-sale record (no action key, request
            // Approved) still refuses, so the record is a real
            // authority, not a passenger on the action-key gate.
            var f = new Fixture();
            BusinessInstanceState seller = f.AddBusiness("store-1", 1000);
            BusinessInstanceState buyer = f.AddBusiness("store-2", 50000);
            f.AddEquipment("plow-1", "store-1", 10000);
            ManagerPendingRequest request = f.AddRequest("store-1", "plow-1", 5000);

            var recordOnlyLedger = new ManagerDecisionExecutionLedger();
            recordOnlyLedger.AddPending(request);
            recordOnlyLedger.RecordCompletedSale(new AssetSaleRecord
            {
                RequestId = request.RequestId,
                AssetId = "plow-1",
                SellerBusinessId = "store-1",
                BuyerBusinessId = "store-2",
                SalePriceCents = 5000,
                DayIndex = Day,
            });
            Assert.IsFalse(recordOnlyLedger.HasExecuted("SellAssetExecuted:" + request.RequestId),
                "this ledger deliberately carries no action key");

            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(0, execution.ExecuteApprovedSales(recordOnlyLedger, f.ExecutionContext(), f.Diag));
            Assert.AreEqual(1000, seller.RuntimeState.CurrentCashCents, "no seller leg without a real execution");
            Assert.AreEqual(50000, buyer.RuntimeState.CurrentCashCents, "no buyer leg without a real execution");
            Assert.AreEqual("store-1", f.Equipment["plow-1"].OwnerId, "no title moved");
            bool diagnosed = false;
            foreach (string line in f.Diag)
                if (line.Contains("persisted executed-sale record")) diagnosed = true;
            Assert.IsTrue(diagnosed, "the refusal names the persisted record");
        }

        [Test]
        public void ExecutedSale_RecordSurvivesLedgerSaveLoad_AndRefusesReExecutionEvenIfStatusReset()
        {
            var f = new Fixture();
            BusinessInstanceState seller = f.AddBusiness("store-1", 1000);
            BusinessInstanceState buyer = f.AddBusiness("store-2", 50000);
            f.AddEquipment("plow-1", "store-1", 10000);
            ManagerPendingRequest request = f.AddRequest("store-1", "plow-1", 5000);

            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(1, execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag));
            Assert.AreEqual("Executed", request.Status);
            Assert.AreEqual(1, f.Ledger.CompletedAssetSales.Count, "the ledger holds the record, not just the runtime service");
            AssetSaleRecord record = f.Ledger.CompletedAssetSales[0];
            Assert.AreEqual(request.RequestId, record.RequestId);
            Assert.AreEqual("plow-1", record.AssetId);
            Assert.AreEqual("store-1", record.SellerBusinessId);
            Assert.AreEqual("store-2", record.BuyerBusinessId);
            Assert.AreEqual(5000, record.SalePriceCents);
            Assert.AreEqual(Day, record.DayIndex);

            // Save/load round trip through the ledger DTO.
            ManagerDecisionExecutionSaveDto dto = f.Ledger.CaptureSaveDto();
            var restoredLedger = new ManagerDecisionExecutionLedger();
            restoredLedger.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restoredLedger.CompletedAssetSales.Count);
            Assert.AreEqual("store-2", restoredLedger.CompletedAssetSales[0].BuyerBusinessId);

            // Old saves carry no list: loading one starts empty, not null.
            var emptyLedger = new ManagerDecisionExecutionLedger();
            emptyLedger.LoadFromSaveDto(new ManagerDecisionExecutionSaveDto { CompletedAssetSales = null });
            Assert.AreEqual(0, emptyLedger.CompletedAssetSales.Count);

            // A fresh service (the drivers construct one every drive)
            // refuses re-execution from the persisted record alone, even
            // when the request status is forced back to Approved — the
            // record, not the status, is the second refusal authority.
            ManagerPendingRequest restoredRequest = null;
            foreach (ManagerPendingRequest pending in restoredLedger.PendingRequests)
                if (pending.RequestId == request.RequestId) restoredRequest = pending;
            Assert.IsNotNull(restoredRequest);
            restoredRequest.Status = "Approved";
            var freshExecution = new AssetSaleExecutionService();
            freshExecution.RestoreCompletedSalesFromLedger(restoredLedger);
            Assert.AreEqual(1, freshExecution.CompletedSales.Count, "the restored service reports the sale");
            Assert.AreEqual(0, freshExecution.ExecuteApprovedSales(restoredLedger, f.ExecutionContext(), f.Diag),
                "the persisted record refuses a second transfer");
            Assert.AreEqual(6000, seller.RuntimeState.CurrentCashCents, "no second seller leg");
            Assert.AreEqual(45000, buyer.RuntimeState.CurrentCashCents, "no second buyer leg");
            Assert.AreEqual("store-2", f.Equipment["plow-1"].OwnerId, "title stays with the first buyer");
            Assert.AreEqual(1, restoredLedger.CompletedAssetSales.Count, "no duplicate record was minted");
        }
    }
}
