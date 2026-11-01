using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker D: wages and household income (Phase F). Money rules: business
    /// cash moves only through BusinessRuntimeState's single-writer path;
    /// household cash only through HouseholdLedger.RecordWagePayment. No
    /// second wage pathway; no wage income from status alone.
    /// </summary>
    [TestFixture]
    public sealed class WageSettlementTests
    {
        private sealed class Fixture
        {
            public BusinessRuntimeState Business;
            public EmploymentRelationshipRegistry Registry;
            public HouseholdLedgerRegistry Ledgers;
            public WageSettlementService Service;
            public List<string> Diag = new List<string>();

            public Fixture(string businessId, int cashCents)
            {
                Registry = new EmploymentRelationshipRegistry();
                Ledgers = new HouseholdLedgerRegistry();
                Business = new BusinessRuntimeState();
                Business.BindBusinessIdentity(businessId);
                Business.SetCurrentCashCents(cashCents);
                Business.EmploymentRegistry = Registry;
                Business.HouseholdLedgers = Ledgers;
                Business.PersonHouseholdIdLookup = pid => pid == 1 ? 101 : pid == 2 ? 102 : -1;
                Business.PayrollDayIndex = 410;
                Service = new WageSettlementService();
            }

            public EmploymentRelationship Hire(string id, int personId, string businessId, int weeklyWageCents)
            {
                var employment = new EmploymentRelationship
                {
                    Id = id,
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessId,
                    RoleDisplayName = "Clerk",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "agreed"),
                    StartDayIndex = 400
                };
                Assert.IsNull(Registry.Register(employment), "registration must succeed");
                return employment;
            }

            public int HouseholdBalance(int householdId) => Ledgers.GetOrCreate(householdId).GetBalanceCents();
        }

        [Test]
        public void OrdinaryWeeklyPayroll_ConservesCash()
        {
            var f = new Fixture("store-1", 10000);
            f.Hire("emp-1", 1, "store-1", 3000);
            f.Hire("emp-2", 2, "store-1", 2000);

            string outcome = f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 59, 410, f.Diag);

            Assert.AreEqual("settled", outcome);
            Assert.AreEqual(5000, f.Business.CurrentCashCents, "business cash -5000");
            Assert.AreEqual(3000, f.HouseholdBalance(101), "H101 +3000 exactly - one entry");
            Assert.AreEqual(2000, f.HouseholdBalance(102), "H102 +2000 exactly - one entry");
            Assert.AreEqual(1, f.Service.Events.Count);
            Assert.AreEqual(5000, f.Service.Events[0].TotalPaidCents);
            Assert.AreEqual(2, f.Service.Events[0].Entries.Count);
            Assert.IsTrue(f.Service.Events[0].Entries[0].Paid);
        }

        [Test]
        public void SettleWeeklyPayroll_IsIdempotent()
        {
            var f = new Fixture("store-1", 10000);
            f.Hire("emp-1", 1, "store-1", 3000);

            Assert.AreEqual("settled", f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 59, 410, f.Diag));
            Assert.AreEqual("already-settled", f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 59, 411, f.Diag));
            Assert.AreEqual(7000, f.Business.CurrentCashCents, "second call moves no cash");
            Assert.AreEqual(3000, f.HouseholdBalance(101), "second call credits no wage");
            Assert.AreEqual(1, f.Service.Events.Count, "one payroll event only");
            Assert.AreEqual(59, f.Business.LastPayrollSettledWeekIndex);
        }

        [Test]
        public void PartialPeriod_MidWeekStart_Prorated()
        {
            var f = new Fixture("store-1", 10000);
            EmploymentRelationship emp = f.Hire("emp-1", 1, "store-1", 3000);

            int prorated = WageSettlementService.ProrateWeeklyWage(3000, 3);
            Assert.AreEqual(1286, prorated, "ceil(3000*3/7) = 1286");

            string outcome = f.Service.SettlePartialPeriod(f.Business, emp, prorated, "partial week 59 (3 days)", 412, f.Diag);

            Assert.AreEqual("paid", outcome);
            Assert.AreEqual(10000 - 1286, f.Business.CurrentCashCents);
            Assert.AreEqual(1286, f.HouseholdBalance(101));
        }

        [Test]
        public void InsufficientCash_AccruesArrears_NoSecondPath()
        {
            var f = new Fixture("store-1", 100);
            EmploymentRelationship emp = f.Hire("emp-1", 1, "store-1", 3000);

            string outcome = f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 59, 410, f.Diag);

            Assert.AreEqual("settled-arrears", outcome);
            Assert.AreEqual(100, f.Business.CurrentCashCents, "no cash moved - there is no second payment path");
            Assert.AreEqual(0, f.HouseholdBalance(101), "worker credited nothing");
            Assert.AreEqual(3000, f.Business.AccruedLiabilityCents, "full wage accrued as operating liability");
            Assert.AreEqual(EmploymentLifecycleState.Suspended, emp.LifecycleState, "employment suspended on default");
            Assert.AreEqual(EmploymentSuspensionReason.EmployerPaymentDefault, emp.SuspensionReason);
            Assert.IsFalse(f.Service.Events[0].Entries[0].Paid, "event records the unpaid line");
        }

        [Test]
        public void NoEmployment_NoPay()
        {
            var f = new Fixture("store-1", 10000);

            string direct = f.Business.PayAgreedWage("emp-ghost", 3000, "weekly", 410, f.Diag);
            Assert.IsNotNull(direct, "unknown employment refused");

            string partial = f.Service.SettlePartialPeriod(f.Business, null, 3000, "weekly", 410, f.Diag);
            Assert.AreEqual("refused-no-employment", partial, "no wage income from status alone");

            Assert.AreEqual(10000, f.Business.CurrentCashCents, "nothing moved");
            Assert.AreEqual(0, f.Service.Events.Count);
        }

        [Test]
        public void Departure_FinalSettlement()
        {
            var f = new Fixture("store-1", 10000);
            EmploymentRelationship emp = f.Hire("emp-1", 1, "store-1", 3000);
            emp.LifecycleState = EmploymentLifecycleState.Ended;
            emp.EndReason = EmploymentEndReason.Resigned;
            emp.EndDayIndex = 412;

            int prorated = WageSettlementService.ProrateWeeklyWage(3000, 5);
            string outcome = f.Service.SettlePartialPeriod(f.Business, emp, prorated, "final settlement (5 days)", 412, f.Diag);

            Assert.AreEqual("paid", outcome);
            Assert.AreEqual(10000 - prorated, f.Business.CurrentCashCents);
            Assert.AreEqual(prorated, f.HouseholdBalance(101), "departed worker receives final settlement");
        }

        [Test]
        public void FamilyLaborAndOperator_Refused()
        {
            // PL-04: non-wage kinds never carry payroll wage terms (read-only guard).
            Assert.IsNotNull(WageSettlementService.ValidatePayableKind(WorkRelationshipKind.FamilyEnterpriseLabor, 500),
                "family labor refused");
            Assert.IsNotNull(WageSettlementService.ValidatePayableKind(WorkRelationshipKind.FarmOrBusinessOperator, 500),
                "operator refused");
            Assert.IsNotNull(WageSettlementService.ValidatePayableKind(WorkRelationshipKind.ContractOrServiceWork, 500),
                "contract work refused");
            Assert.IsNull(WageSettlementService.ValidatePayableKind(WorkRelationshipKind.PermanentEmployment, 500),
                "wage employment payable");
            Assert.IsNull(WageSettlementService.ValidatePayableKind(WorkRelationshipKind.SeasonalOrCasualEmployment, 500),
                "casual employment payable");
        }

        [Test]
        public void MultipleEmployments_TwoPayments_OnePerAgreement()
        {
            var store = new Fixture("store-1", 10000);
            var smith = new Fixture("smith-1", 10000);
            // One person, two compatible employments: the household ledger is shared per person lookup.
            EmploymentRelationship empA = store.Hire("emp-a", 1, "store-1", 3000);
            EmploymentRelationship empB = smith.Hire("emp-b", 1, "smith-1", 2000);
            smith.Business.PersonHouseholdIdLookup = pid => pid == 1 ? 101 : -1;
            smith.Business.HouseholdLedgers = store.Ledgers; // same household authority

            Assert.AreEqual("paid", store.Service.SettlePartialPeriod(store.Business, empA, 3000, "week 59", 410, store.Diag));
            Assert.AreEqual("paid", smith.Service.SettlePartialPeriod(smith.Business, empB, 2000, "week 59", 410, smith.Diag));

            Assert.AreEqual(5000, store.HouseholdBalance(101), "two agreements -> two payments, one household");
            Assert.AreEqual(7000, store.Business.CurrentCashCents);
            Assert.AreEqual(8000, smith.Business.CurrentCashCents);
        }

        [Test]
        public void SaveLoad_SettledWeekGuard_PreventsDoublePost()
        {
            var f = new Fixture("store-1", 10000);
            f.Hire("emp-1", 1, "store-1", 3000);
            Assert.AreEqual("settled", f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 59, 410, f.Diag));

            WageSettlementSaveDto dto = f.Service.CaptureSaveDto();
            var restored = new WageSettlementService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(59, restored.SettledWeekIndex("store-1"), "marker survives save/load");
            Assert.AreEqual(1, restored.Events.Count, "event history survives");

            // Fresh business runtime at the restored marker (hub re-stamps from the DTO).
            var f2 = new Fixture("store-1", 10000);
            f2.Hire("emp-1", 1, "store-1", 3000);
            f2.Business.LastPayrollSettledWeekIndex = 59;
            Assert.AreEqual("already-settled",
                restored.SettleWeeklyPayroll(f2.Business, f2.Registry, 59, 417, f2.Diag),
                "loaded guard prevents double-post");
            Assert.AreEqual(10000, f2.Business.CurrentCashCents, "no cash moved on replay");
            Assert.AreEqual(0, f2.HouseholdBalance(101), "no wage credited on replay");
        }

        [Test]
        public void SaveLoad_ArrearsSurvive()
        {
            var f = new Fixture("store-1", 100);
            f.Hire("emp-1", 1, "store-1", 3000);
            Assert.AreEqual("settled-arrears", f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 59, 410, f.Diag));

            var restored = new WageSettlementService();
            restored.LoadFromSaveDto(f.Service.CaptureSaveDto());

            Assert.AreEqual(1, restored.Events.Count);
            PayrollEntry entry = restored.Events[0].Entries[0];
            Assert.IsFalse(entry.Paid, "unpaid line survives");
            Assert.AreEqual(3000, entry.AmountCents, "arrears amount survives");
        }

        [Test]
        public void LoadFromSaveDto_ClearsThenLoads()
        {
            var f = new Fixture("store-1", 10000);
            f.Hire("emp-1", 1, "store-1", 3000);
            f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 59, 410, f.Diag);
            f.Service.SettleWeeklyPayroll(f.Business, f.Registry, 60, 417, f.Diag);

            WageSettlementSaveDto dto = f.Service.CaptureSaveDto();
            var other = new WageSettlementService();
            other.LoadFromSaveDto(dto);
            Assert.AreEqual(2, other.Events.Count);

            // Loading an empty DTO clears, never merges.
            other.LoadFromSaveDto(new WageSettlementSaveDto());
            Assert.AreEqual(0, other.Events.Count);
            Assert.AreEqual(-1, other.SettledWeekIndex("store-1"));
        }

        [Test]
        public void PartialPeriod_WrongBusiness_Refused()
        {
            var f = new Fixture("store-1", 10000);
            EmploymentRelationship emp = f.Hire("emp-1", 1, "smith-1", 3000); // belongs elsewhere

            string outcome = f.Service.SettlePartialPeriod(f.Business, emp, 1000, "week 59", 410, f.Diag);
            Assert.AreEqual("refused-wrong-business", outcome);
            Assert.AreEqual(10000, f.Business.CurrentCashCents);
        }
    }
}
