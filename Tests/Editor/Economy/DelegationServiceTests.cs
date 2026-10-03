using System.Collections.Generic;
using LandLedgers.Economy.Delegation;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// T2D: delegated operation. Routine acts proceed without owner clicks;
    /// sale/mortgage/major borrowing always escalate (Tech X §12.3).
    /// </summary>
    public sealed class DelegationServiceTests
    {
        private DelegationService service;
        private EntityIdRegistry ids;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            service = new DelegationService();
            ids = new EntityIdRegistry();
            diag = new List<string>();
        }

        private DelegatedAuthority GrantAll(int personId = 5)
        {
            return service.GrantAuthority(ids, "biz-1", personId, DelegationScope.AllRoutine,
                10000, "management", (pid, skill) => true, 0, diag);
        }

        [Test]
        public void RoutineActsArePermittedWithoutOwnerClicks()
        {
            GrantAll();
            Assert.AreEqual(AuthorityVerdict.Permitted,
                service.CheckAct("biz-1", 5, DelegatedAct.PurchaseSupplies, 5000, 1, diag));
            Assert.AreEqual(AuthorityVerdict.Permitted,
                service.CheckAct("biz-1", 5, DelegatedAct.ScheduleWork, 0, 1, diag));
            Assert.AreEqual(AuthorityVerdict.Permitted,
                service.CheckAct("biz-1", 5, DelegatedAct.HireStaff, 0, 1, diag));
        }

        [Test]
        public void ReservedActsAlwaysEscalate()
        {
            GrantAll(); // even the broadest grant cannot authorize these
            Assert.AreEqual(AuthorityVerdict.Escalated,
                service.CheckAct("biz-1", 5, DelegatedAct.SellBusiness, 0, 1, diag));
            Assert.AreEqual(AuthorityVerdict.Escalated,
                service.CheckAct("biz-1", 5, DelegatedAct.MortgageProperty, 0, 1, diag));
            Assert.AreEqual(AuthorityVerdict.Escalated,
                service.CheckAct("biz-1", 5, DelegatedAct.MajorBorrowing, 100000, 1, diag));
        }

        [Test]
        public void OverLimitPurchaseEscalates()
        {
            GrantAll();
            Assert.AreEqual(AuthorityVerdict.Escalated,
                service.CheckAct("biz-1", 5, DelegatedAct.PurchaseSupplies, 50000, 1, diag),
                "Above the 10000c delegated limit — the owner decides.");
        }

        [Test]
        public void ActsOutsideScopeAreDenied()
        {
            service.GrantAuthority(ids, "biz-1", 5, DelegationScope.Purchasing, 10000,
                "management", (pid, skill) => true, 0, diag);
            Assert.AreEqual(AuthorityVerdict.Denied,
                service.CheckAct("biz-1", 5, DelegatedAct.HireStaff, 0, 1, diag));
        }

        [Test]
        public void UnqualifiedDelegateIsRefusedTheGrant()
        {
            var authority = service.GrantAuthority(ids, "biz-1", 5, DelegationScope.AllRoutine,
                10000, "management", (pid, skill) => false, 0, diag);
            Assert.IsNull(authority, "The Autonomous Crown requires a QUALIFIED manager (Tech X §12.3).");
        }

        [Test]
        public void RevokedAuthorityDenies()
        {
            var authority = GrantAll();
            service.Revoke(authority.AuthorityId, 10, diag);
            Assert.AreEqual(AuthorityVerdict.Denied,
                service.CheckAct("biz-1", 5, DelegatedAct.PurchaseSupplies, 100, 11, diag));
        }

        [Test]
        public void EveryDecisionIsAudited()
        {
            GrantAll();
            service.CheckAct("biz-1", 5, DelegatedAct.PurchaseSupplies, 100, 1, diag);
            service.CheckAct("biz-1", 5, DelegatedAct.SellBusiness, 0, 1, diag);
            service.CheckAct("biz-1", 9, DelegatedAct.PurchaseSupplies, 100, 1, diag);

            Assert.AreEqual(3, service.Decisions.Count);
            Assert.AreEqual(AuthorityVerdict.Permitted, service.Decisions[0].Verdict);
            Assert.AreEqual(AuthorityVerdict.Escalated, service.Decisions[1].Verdict);
            Assert.AreEqual(AuthorityVerdict.Denied, service.Decisions[2].Verdict);
        }

        [Test]
        public void SaveLoadRoundTripsAuthoritiesAndDecisions()
        {
            GrantAll();
            service.CheckAct("biz-1", 5, DelegatedAct.PurchaseSupplies, 100, 1, diag);

            var dto = service.CaptureSaveDto();
            var restored = new DelegationService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(AuthorityVerdict.Permitted,
                restored.CheckAct("biz-1", 5, DelegatedAct.ScheduleWork, 0, 2, new List<string>()));
            Assert.AreEqual(2, restored.Decisions.Count);
        }
    }
}
