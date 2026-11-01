using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Pass IX review R-01/R-12: formation assets are owner-scoped.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixFormationOwnershipTests
    {
        private const int Day = 430;

        private sealed class FakeCash : INpcBusinessCashPort
        {
            private readonly Dictionary<string, BusinessInstanceState> b;
            public FakeCash(Dictionary<string, BusinessInstanceState> b) { this.b = b; }
            public int ReadCashCents(string id) => b.TryGetValue(id, out var x) ? x.RuntimeState.CurrentCashCents : 0;
            public string Spend(string id, int day, int amt, string p)
            {
                if (!b.TryGetValue(id, out var x)) return "unknown";
                if (x.RuntimeState.CurrentCashCents < amt) return "insufficient";
                x.RuntimeState.SpendCents(amt); return null;
            }
            public string Receive(string id, int day, int amt, string r)
            {
                if (!b.TryGetValue(id, out var x)) return "unknown";
                x.RuntimeState.AddCashCents(amt); return null;
            }
        }

        private sealed class Fixture
        {
            public readonly Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>();
            public readonly NpcFormationAssetRegister Formation = new NpcFormationAssetRegister();
            public readonly ManagerDecisionExecutionLedger Ledger = new ManagerDecisionExecutionLedger();
            public readonly List<string> Diag = new List<string>();

            public BusinessInstanceState AddBusiness(string id, int cash)
            {
                var business = new BusinessInstanceState();
                SetField(business, "instanceId", id);
                business.SetOwner(BusinessOwnerIdentity.Npc(42, "Owner", "Owner"));
                var runtime = new BusinessRuntimeState();
                runtime.BindBusinessIdentity(id);
                runtime.SetCurrentCashCents(cash);
                SetField(business, "runtimeState", runtime);
                Businesses[id] = business;
                return business;
            }

            public NpcFormationAsset AddAsset(string item, string owner, int cost)
            {
                var asset = new NpcFormationAsset
                {
                    AssetKind = "equipment", ItemId = item, OwnerBusinessId = owner,
                    Units = 1, CostCents = cost, Provenance = "purchase:test-1",
                };
                Assert.IsNull(Formation.Record(asset));
                return asset;
            }

            public AssetSaleAppraisalService Appraisal() => new AssetSaleAppraisalService { FormationAssets = Formation };

            public AssetSaleExecutionContext ExecutionContext() => new AssetSaleExecutionContext
            {
                FindBusiness = id => Businesses.TryGetValue(id, out var x) ? x : null,
                AllBusinesses = () => Businesses.Values,
                FindEquipmentAsset = id => null,
                FormationAssets = Formation,
                DayIndex = Day,
            };

            public ManagerPendingRequest AddRequest(string businessId, string assetId, int amount, string reqId)
            {
                var request = new ManagerPendingRequest
                {
                    RequestId = reqId, RequestKey = $"SellAsset:{businessId}:{assetId}:{amount}c",
                    BusinessInstanceId = businessId, Kind = ManagerActKinds.SellAsset,
                    AmountCents = amount, AssetId = assetId, CreatedDayIndex = Day,
                    ManagerPersonId = 7, Status = "Approved",
                };
                Ledger.AddPending(request);
                return request;
            }

            private static void SetField(object target, string name, object value)
            {
                FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(field); field.SetValue(target, value);
            }
        }

        [Test]
        public void ForeignFormationAsset_NotProposedNotAppraisedNotExecuted()
        {
            var f = new Fixture();
            BusinessInstanceState a = f.AddBusiness("biz-a", 1000);
            BusinessInstanceState b = f.AddBusiness("biz-b", 1000);
            f.AddBusiness("biz-c", 50000);
            NpcFormationAsset asset = f.AddAsset("anvil", "biz-a", 8000);

            // Appraisal for B: no evidence.
            Assert.IsFalse(f.Appraisal().Appraise("biz-b", "formation:anvil").HasEvidence);
            Assert.AreEqual(-1, f.Appraisal().AppraiseSaleCents(new ManagerPendingRequest
                { BusinessInstanceId = "biz-b", AssetId = "formation:anvil" }));

            // Proposal for distressed B: builder must not propose A's asset.
            var builder = new ManagerCycleInputsBuilder(f.Appraisal());
            ManagerCycleInputs inputs = builder.Build(new ManagerCycleLiveEvidence
            {
                Business = b,
                DemandReport = new LaborDemandReport { BusinessInstanceId = "biz-b", DayIndex = Day },
                Authority = new DelegatedAuthority { AuthorityId = "auth-b", BusinessInstanceId = "biz-b", DelegatePersonId = 7, Scopes = DelegationScope.AllRoutine },
                ManagerPersonId = 7, DayIndex = Day, SurvivalReserveCents = 20000,
                FormationAssets = f.Formation.Assets,
            }, f.Diag);
            Assert.IsTrue(string.IsNullOrEmpty(inputs.ProposedAssetId), "B gets no proposal for A's asset");

            // Execution for B: refused, nothing moves, owner unchanged.
            ManagerPendingRequest request = f.AddRequest("biz-b", "formation:anvil", 8000, "req-b-anvil");
            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(0, execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag));
            Assert.AreEqual("Approved", request.Status);
            Assert.AreEqual("biz-a", asset.OwnerBusinessId);
            Assert.IsFalse(asset.Liquidated);
            Assert.AreEqual(1000, a.RuntimeState.CurrentCashCents);
            Assert.AreEqual(1000, b.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void LegitimateSale_TransfersOwnershipToBuyer_AndConservesCash()
        {
            var f = new Fixture();
            BusinessInstanceState a = f.AddBusiness("biz-a", 1000);
            BusinessInstanceState buyer = f.AddBusiness("biz-b", 50000);
            NpcFormationAsset asset = f.AddAsset("anvil", "biz-a", 8000);
            int totalBefore = a.RuntimeState.CurrentCashCents + buyer.RuntimeState.CurrentCashCents;

            f.AddRequest("biz-a", "formation:anvil", 8000, "req-a-anvil");
            var execution = new AssetSaleExecutionService();
            Assert.AreEqual(1, execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag));

            Assert.AreEqual("biz-b", asset.OwnerBusinessId, "buyer receives a real register record (ownership)");
            Assert.IsFalse(asset.Liquidated, "a sale does not consume the asset");
            Assert.AreEqual(9000, a.RuntimeState.CurrentCashCents);
            Assert.AreEqual(42000, buyer.RuntimeState.CurrentCashCents);
            Assert.AreEqual(totalBefore, a.RuntimeState.CurrentCashCents + buyer.RuntimeState.CurrentCashCents);
            // Buyer can now be appraised as owner; seller no longer can.
            Assert.IsTrue(f.Appraisal().Appraise("biz-b", "formation:anvil").HasEvidence);
            Assert.IsFalse(f.Appraisal().Appraise("biz-a", "formation:anvil").HasEvidence);
        }

        [Test]
        public void DeferredSale_RefuseDoesNotAppendUnboundedDuplicateNotes()
        {
            var f = new Fixture();
            f.AddBusiness("biz-a", 1000);
            f.AddBusiness("biz-b", 100); // no solvent buyer for 8000
            f.AddAsset("anvil", "biz-a", 8000);
            ManagerPendingRequest request = f.AddRequest("biz-a", "formation:anvil", 8000, "req-deferred");
            var execution = new AssetSaleExecutionService();
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(0, execution.ExecuteApprovedSales(f.Ledger, f.ExecutionContext(), f.Diag));
            int occurrences = request.ResolutionNote.Split(new[] { "execution deferred" }, System.StringSplitOptions.None).Length - 1;
            Assert.AreEqual(1, occurrences, "same deferral reason recorded once, not per daily retry");
        }

        [Test]
        public void CloseOrderly_LiquidatesOnlyClosingBusinessAssets()
        {
            var f = new Fixture();
            BusinessInstanceState closing = f.AddBusiness("biz-a", 0);
            BusinessInstanceState other = f.AddBusiness("biz-b", 50000);
            BusinessInstanceState buyerBiz = f.AddBusiness("biz-c", 50000);
            NpcFormationAsset own = f.AddAsset("anvil", "biz-a", 10000);
            NpcFormationAsset foreign = f.AddAsset("loom", "biz-b", 10000);

            var cashPort = new FakeCash(f.Businesses);
            var port = new RegisterLiquidationPort(() => f.Businesses.Values, cashPort, f.Appraisal());
            var founderLedger = new HouseholdLedger(7);
            Assert.IsNull(founderLedger.RecordInflow(0, 1000, HouseholdIncomeSource.OwnerContribution, "seed", "seed", "test"));
            var record = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = "biz-a", BusinessEntityKey = "business:biz-a",
                FounderPersonId = 42, OwnerName = "Owner", State = NpcBusinessLifeState.Operating,
            };
            var service = new NpcBusinessLifecycleService();
            NpcBusinessCloseResult result = service.CloseOrderly(record, "retires", new NpcLifecycleContext
            {
                Cash = cashPort, Obligations = new FinancialObligationAuthority(),
                Employments = new EmploymentRelationshipRegistry(), FounderHouseholdLedger = founderLedger,
                AssetRegister = f.Formation, Liquidation = port, Events = new NpcBusinessEventLog(),
                Ids = new EntityIdRegistry(), DayIndex = Day,
            }, f.Diag);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(1, result.AssetsLiquidated, "only the closing business's own asset");
            Assert.IsTrue(own.Liquidated);
            Assert.IsFalse(foreign.Liquidated, "foreign asset untouched");
            Assert.AreEqual("biz-b", foreign.OwnerBusinessId);
            Assert.AreEqual(44000, other.RuntimeState.CurrentCashCents, "other business only paid as the liquidation BUYER (6000), its own asset was not sold");
        }
    }
}
