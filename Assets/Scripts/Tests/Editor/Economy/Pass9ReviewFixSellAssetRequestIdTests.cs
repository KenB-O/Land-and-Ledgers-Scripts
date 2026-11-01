using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Pass IX review R-06: SellAsset RequestId includes the asset id.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixSellAssetRequestIdTests
    {
        private const int Day = 431;

        private static BusinessInstanceState MakeBusiness(string id, int cash, Dictionary<string, BusinessInstanceState> map)
        {
            var business = new BusinessInstanceState();
            FieldInfo idField = typeof(BusinessInstanceState).GetField("instanceId", BindingFlags.NonPublic | BindingFlags.Instance);
            idField.SetValue(business, id);
            business.SetOwner(BusinessOwnerIdentity.Npc(42, "Owner", "Owner"));
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            FieldInfo rtField = typeof(BusinessInstanceState).GetField("runtimeState", BindingFlags.NonPublic | BindingFlags.Instance);
            rtField.SetValue(business, runtime);
            map[id] = business;
            return business;
        }

        [Test]
        public void TwoDifferentAssets_SameBusinessDayAmount_BothApproveAndBothExecute()
        {
            var businesses = new Dictionary<string, BusinessInstanceState>();
            BusinessInstanceState seller = MakeBusiness("store-1", 1000, businesses);
            BusinessInstanceState buyer = MakeBusiness("store-2", 50000, businesses);
            var equipment = new Dictionary<string, EquipmentAsset>();
            foreach (string assetId in new[] { "plow-1", "plow-2" })
            {
                equipment[assetId] = new EquipmentAsset
                {
                    AssetId = assetId, Kind = "plow", DisplayName = "Plow",
                    Condition01 = 0.8f, OwnerKind = "business", OwnerId = "store-1",
                };
                seller.GenericConfiguration.AddEquipmentReference(assetId);
            }

            var service = new ManagerDecisionExecutionService();
            service.Configure(new ManagerExecutionServices
            {
                FindBusiness = id => businesses.TryGetValue(id, out var x) ? x : null,
                AllBusinesses = () => businesses.Values,
            });
            var authority = new DelegatedAuthority
            {
                AuthorityId = "auth-1", BusinessInstanceId = "store-1", DelegatePersonId = 7,
                Scopes = DelegationScope.AllRoutine, PurchaseLimitCents = 20000, GrantedDayIndex = 400,
            };
            var demand = new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day };
            var diag = new List<string>();

            foreach (string assetId in new[] { "plow-1", "plow-2" })
            {
                var decision = new ManagerDecisionResult
                {
                    Escalated = true, ActionKind = ManagerActKinds.SellAsset,
                    ProposedAssetId = assetId, DayIndex = Day,
                    ResultingReference = $"intent:sellasset-request:store-1:d{Day}:5000c",
                };
                ManagerExecutionOutcome outcome = service.ExecuteDecision(seller, authority, 7, demand, decision, Day, diag);
                Assert.IsTrue(outcome.Pending, assetId);
            }

            Assert.AreEqual(2, service.Ledger.PendingRequests.Count);
            ManagerPendingRequest r1 = service.Ledger.PendingRequests[0];
            ManagerPendingRequest r2 = service.Ledger.PendingRequests[1];
            Assert.AreNotEqual(r1.RequestId, r2.RequestId, "R-06: distinct assets must not share a RequestId");
            StringAssert.Contains("plow-1", r1.RequestId);
            StringAssert.Contains("plow-2", r2.RequestId);

            r1.Status = "Approved";
            r2.Status = "Approved";

            var execution = new AssetSaleExecutionService();
            var ctx = new AssetSaleExecutionContext
            {
                FindBusiness = id => businesses.TryGetValue(id, out var x) ? x : null,
                AllBusinesses = () => businesses.Values,
                FindEquipmentAsset = id => equipment.TryGetValue(id ?? string.Empty, out var x) ? x : null,
                FormationAssets = null, DayIndex = Day,
            };
            int executed = execution.ExecuteApprovedSales(service.Ledger, ctx, diag);

            Assert.AreEqual(2, executed, "both approved sales execute — the second is not stranded by an execution-key collision");
            Assert.AreEqual("Executed", r1.Status);
            Assert.AreEqual("Executed", r2.Status);
            Assert.AreEqual("store-2", equipment["plow-1"].OwnerId);
            Assert.AreEqual("store-2", equipment["plow-2"].OwnerId);
            Assert.AreEqual(11000, seller.RuntimeState.CurrentCashCents, "seller receives 2 x 5000");
            Assert.AreEqual(40000, buyer.RuntimeState.CurrentCashCents, "buyer pays 2 x 5000 — cash conserved");
        }
    }
}
