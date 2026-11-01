using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// G-06: NPC-owner auto-resolution for manager pending requests.
    /// Escalated Borrow routes to lender underwriting instead of stalling;
    /// SellAsset approves only below the survival reserve at/above appraisal.
    /// Player-owned requests stay pending (future UI).
    /// </summary>
    [TestFixture]
    public sealed class NpcOwnerRequestResolverTests
    {
        private const int Day = 410;
        private const int Reserve = 20000;

        private sealed class FakeLender : IManagerBusinessLender
        {
            public string LenderName => "Test Lender";
            public int CapitalCents;

            public FakeLender(int capitalCents) { CapitalCents = capitalCents; }

            public FinancialObligation TryArrangeLoan(
                EntityIdRegistry ids, FinancialObligationAuthority obligations, string debtorBusinessId,
                int amountCents, int dayIndex, List<string> diagnostics, out string refusalReason)
            {
                refusalReason = string.Empty;
                if (amountCents <= 0) { refusalReason = "amount must be positive"; return null; }
                if (amountCents > CapitalCents) { refusalReason = "insufficient lender capital"; return null; }
                FinancialObligation obligation = obligations.Create(
                    ids, FinancialObligationKind.Loan, debtorBusinessId, LenderName,
                    amountCents, dayIndex, "12 months", "manager-authorized working capital");
                if (obligation == null) { refusalReason = "the obligation authority refused"; return null; }
                CapitalCents -= amountCents;
                return obligation;
            }
        }

        private sealed class Fixture
        {
            public readonly ManagerDecisionExecutionLedger Ledger = new ManagerDecisionExecutionLedger();
            public readonly Dictionary<string, BusinessInstanceState> Businesses =
                new Dictionary<string, BusinessInstanceState>();
            public readonly FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public readonly EntityIdRegistry Ids = new EntityIdRegistry();
            public readonly List<string> Diag = new List<string>();

            public BusinessInstanceState AddBusiness(string id, int cashCents, bool npcOwner)
            {
                var business = new BusinessInstanceState();
                SetField(business, "instanceId", id);
                business.SetOwner(npcOwner
                    ? BusinessOwnerIdentity.Npc(42, "Marta Kessler", "Kessler")
                    : BusinessOwnerIdentity.Player());
                var runtime = new BusinessRuntimeState();
                runtime.AddCashCents(cashCents);
                SetField(business, "runtimeState", runtime);
                Businesses[id] = business;
                return business;
            }

            public ManagerPendingRequest AddRequest(string kind, string businessId, int amountCents)
            {
                var request = new ManagerPendingRequest
                {
                    RequestId = $"mgr-req:{kind}:{businessId}:d{Day}:{amountCents}c",
                    RequestKey = $"{kind}:{businessId}:{amountCents}c",
                    BusinessInstanceId = businessId,
                    Kind = kind,
                    AmountCents = amountCents,
                    Detail = "test",
                    CreatedDayIndex = Day,
                    ManagerPersonId = 7,
                    Status = "Pending",
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

        private static NpcOwnerRequestResolver.OwnerResolutionContext Context(
            Fixture f, IManagerBusinessLender lender, int appraisedCents = -1)
        {
            return new NpcOwnerRequestResolver.OwnerResolutionContext
            {
                Ledger = f.Ledger,
                FindBusiness = id =>
                {
                    BusinessInstanceState found;
                    return f.Businesses.TryGetValue(id, out found) ? found : null;
                },
                SurvivalReserveCents = business => Reserve,
                AppraiseSaleCents = request => appraisedCents,
                BusinessLender = lender,
                Obligations = f.Obligations,
                Ids = f.Ids,
                DayIndex = Day,
            };
        }

        [Test]
        public void Borrow_NpcOwner_FundedLoan_ApprovedAndCashCredited()
        {
            var f = new Fixture();
            BusinessInstanceState business = f.AddBusiness("store-1", 10000, npcOwner: true);
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.Borrow, "store-1", 5000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, new FakeLender(100000)), f.Diag);

            Assert.AreEqual("Approved", request.Status);
            Assert.AreEqual(Day, request.ResolvedDayIndex);
            Assert.AreEqual(15000, business.RuntimeState.CurrentCashCents, "The funded loan reached the till.");
            Assert.AreEqual(1, f.Obligations.Obligations.Count, "Real obligation on the shared authority.");
            StringAssert.Contains("underwriting", request.ResolutionNote);
        }

        [Test]
        public void Borrow_NpcOwner_LenderRefuses_RejectedWithReason()
        {
            var f = new Fixture();
            BusinessInstanceState business = f.AddBusiness("store-1", 10000, npcOwner: true);
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.Borrow, "store-1", 5000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, new FakeLender(0)), f.Diag);

            Assert.AreEqual("Rejected", request.Status);
            StringAssert.Contains("insufficient lender capital", request.ResolutionNote);
            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents, "Nothing moved.");
            Assert.AreEqual(0, f.Obligations.Obligations.Count);
        }

        [Test]
        public void Borrow_PlayerOwned_StaysPending()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 10000, npcOwner: false);
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.Borrow, "store-1", 5000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, new FakeLender(100000)), f.Diag);

            Assert.AreEqual("Pending", request.Status, "Player-owned requests wait for the future UI.");
            Assert.AreEqual(-1, request.ResolvedDayIndex);
        }

        [Test]
        public void SellAsset_NpcOwner_DistressSale_Approved()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000, npcOwner: true); // cash < reserve
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.SellAsset, "store-1", 15000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, null, appraisedCents: 12000), f.Diag);

            Assert.AreEqual("Approved", request.Status, "Cash below reserve and price at/above appraisal.");
            Assert.AreEqual(Day, request.ResolvedDayIndex);
        }

        [Test]
        public void SellAsset_NpcOwner_CashAboveReserve_Rejected()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 50000, npcOwner: true); // cash >= reserve
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.SellAsset, "store-1", 15000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, null, appraisedCents: 12000), f.Diag);

            Assert.AreEqual("Rejected", request.Status);
            StringAssert.Contains("no distress sale", request.ResolutionNote);
        }

        [Test]
        public void SellAsset_NpcOwner_PriceBelowAppraised_Rejected()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000, npcOwner: true);
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.SellAsset, "store-1", 8000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, null, appraisedCents: 12000), f.Diag);

            Assert.AreEqual("Rejected", request.Status);
            StringAssert.Contains("below appraised value", request.ResolutionNote);
        }

        [Test]
        public void SellAsset_NpcOwner_NoAppraisal_RejectedNotBlind()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000, npcOwner: true);
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.SellAsset, "store-1", 15000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, null, appraisedCents: -1), f.Diag);

            Assert.AreEqual("Rejected", request.Status, "The owner does not approve blind sales.");
            StringAssert.Contains("no appraisal", request.ResolutionNote);
        }

        [Test]
        public void SellAsset_PlayerOwned_StaysPending()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 1000, npcOwner: false);
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.SellAsset, "store-1", 15000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, null, appraisedCents: 12000), f.Diag);

            Assert.AreEqual("Pending", request.Status, "Player-owned requests wait for the future UI.");
        }

        [Test]
        public void ResolvePending_IsIdempotent()
        {
            var f = new Fixture();
            f.AddBusiness("store-1", 10000, npcOwner: true);
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.Borrow, "store-1", 5000);
            var resolver = new NpcOwnerRequestResolver();
            var lender = new FakeLender(100000);

            resolver.ResolvePending(Context(f, lender), f.Diag);
            resolver.ResolvePending(Context(f, lender), f.Diag);

            Assert.AreEqual("Approved", request.Status);
            Assert.AreEqual(1, f.Obligations.Obligations.Count, "No duplicate obligation on re-run.");
            Assert.AreEqual(15000, f.Businesses["store-1"].RuntimeState.CurrentCashCents,
                "No double funding on re-run.");
        }

        [Test]
        public void UnknownBusiness_RejectedWithReason()
        {
            var f = new Fixture();
            ManagerPendingRequest request = f.AddRequest(ManagerActKinds.Borrow, "ghost-9", 5000);
            var resolver = new NpcOwnerRequestResolver();

            resolver.ResolvePending(Context(f, new FakeLender(100000)), f.Diag);

            Assert.AreEqual("Rejected", request.Status);
            StringAssert.Contains("unknown business", request.ResolutionNote);
        }
    }
}
