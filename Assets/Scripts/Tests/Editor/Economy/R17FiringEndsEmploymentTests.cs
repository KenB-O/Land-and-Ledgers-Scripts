// R17 (note 111 §2, reviewed in note 114): firing ends the employment
// record and settles the final wage at firing time. The two fire paths
// (SharedBusinessRuntimeManager and GeneralStoreRuntimeManager) are
// harness-excluded, so these tests pin the shared helper contract the
// call sites rely on (EmploymentFiringService); they do not execute the
// manager methods themselves. The helper runs in the harness and fails
// red at compile time before the slice (T3-T8 reference it; T1/T2/T9
// pin existing registry/recruitment behavior and pass at base).
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// R17 firing ends the employment record (Agent 8 round-3 C4, ruled by
    /// Agent 3 note 111 §2, plan reviewed in note 114). A fired worker's
    /// record stayed Active, which blocked re-hire everywhere; firing now
    /// ends the record (reason Dismissed, history retained) and settles the
    /// final wage at firing time through PayAgreedWage, in both fire paths,
    /// via the shared EmploymentFiringService helper (ensure, settle, end).
    /// Amount is full-or-nothing per week (Q1 ruled): the agreed weekly
    /// wage when the worker recorded real minutes this week, 0c otherwise.
    /// </summary>
    [TestFixture]
    public sealed class R17FiringEndsEmploymentTests
    {
        private sealed class Fixture
        {
            public BusinessInstanceState Business;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public EmploymentRelationship Employment;
            public WeeklyAttendanceAccumulator Attendance = new WeeklyAttendanceAccumulator();

            public Fixture(bool withRegistry, int cashCents, bool withEmploymentRecord)
            {
                BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                    BusinessType.Blacksmith, "Test Business");
                Business = BusinessInstanceState.Create(
                    "r17_firing", profile, 0, BusinessOwnerIdentity.Player());
                Business.RuntimeState.SetCurrentCashCents(cashCents);
                Business.RuntimeState.HouseholdLedgers = Ledgers;
                Business.RuntimeState.PersonHouseholdIdLookup = Memberships.GetActiveHouseholdId;
                if (withRegistry)
                {
                    Business.RuntimeState.EmploymentRegistry = Employments;
                }
                Employment = new EmploymentRelationship
                {
                    Id = "r17-emp-1",
                    EmployeePersonId = 1,
                    EmployerBusinessId = Business.RuntimeState.BusinessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(1000, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                };
                if (withRegistry && withEmploymentRecord)
                {
                    Assert.IsNull(Employments.Register(Employment));
                }
                Assert.IsNull(Memberships.Register(new HouseholdMembership
                {
                    MembershipId = 1001,
                    PersonId = 1,
                    HouseholdId = 11,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                    EndDayIndex = -1,
                    Source = HouseholdMembershipSource.Authored,
                }));
                Ledgers.GetOrCreate(11);
                Attendance.InstallOn(Business.RuntimeState, Employments);
            }

            public int HouseholdBalance()
            {
                return Ledgers.GetOrCreate(11).GetBalanceCents();
            }

            public void AddSlot(string workerId, int wageCents)
            {
                FieldInfo slotsField = typeof(BusinessRuntimeState).GetField(
                    "workerSlots", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(slotsField);
                var slots = (List<WorkerSlotState>)slotsField.GetValue(Business.RuntimeState);
                var slot = new WorkerSlotState("r17-slot", "Hand Slot", wageCents, true);
                slot.Assign(workerId, "Test Worker", wageCents);
                slots.Add(slot);
            }

            public void RecordMinutes(int personId, int dayIndex, int minutes)
            {
                Attendance.RecordWorkedMinutes(personId, dayIndex, minutes);
            }

            public string Fire(int dayIndex)
            {
                return EmploymentFiringService.EndEmploymentForFiring(
                    Business.RuntimeState, Employments, 1, dayIndex, new List<string>());
            }
        }

        [Test]
        public void T1_EndDismissedEndsActiveRecord()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: true);

            List<EmploymentRelationship> ended = f.Employments.EndEmploymentByReason(
                1, EmploymentEndReason.Dismissed, 10);

            Assert.AreEqual(1, ended.Count, "one Active record ends.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, f.Employment.LifecycleState);
            Assert.AreEqual(EmploymentEndReason.Dismissed, f.Employment.EndReason);
            Assert.AreEqual(10, f.Employment.EndDayIndex);
            Assert.AreEqual(1, f.Employments.GetByEmployee(1).Count,
                "history is retained, never deleted.");
            Assert.AreEqual(0, f.Employments.GetActiveByEmployee(1).Count);
        }

        [Test]
        public void T2_FiredWorkerIsHireableAgain()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: true);
            var population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 1,
                firstName = "Test",
                lastName = "Worker",
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                deathDayIndex = -1,
            });
            var recruitment = new RecruitmentService();
            var diag = new List<string>();
            RecruitmentEffort effort = recruitment.OpenEffort(
                f.Business.RuntimeState.BusinessId, "Hand", RecruitmentChannel.DirectContact, 10, 7, diag);
            Assert.NotNull(effort, "the effort opens on a real channel.");

            // Before the end: the record is still Active, so the direct
            // approach refuses on the employment gate.
            diag = new List<string>();
            CandidateInquiry refused = recruitment.DirectApproach(
                effort.EffortId, 1, population, f.Employments, 10, diag);
            Assert.IsNull(refused, "an actively-employed worker cannot be poached by fiat.");
            bool refusedOnEmployment = false;
            foreach (string line in diag)
                if (line.Contains("already employed")) refusedOnEmployment = true;
            Assert.IsTrue(refusedOnEmployment, "the refusal names the employment gate.");

            // After the end: the record is Ended, so the gate passes.
            List<EmploymentRelationship> ended = f.Employments.EndEmploymentByReason(
                1, EmploymentEndReason.Dismissed, 10);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(0, f.Employments.GetActiveByEmployee(1).Count,
                "the shared-manager candidate predicate now holds.");
            diag = new List<string>();
            recruitment.DirectApproach(effort.EffortId, 1, population, f.Employments, 10, diag);
            foreach (string line in diag)
                Assert.IsFalse(line.Contains("already employed"),
                    "after the end the employment gate no longer refuses: " + line);
        }

        [Test]
        public void T3_FinalWageSettledAtFiring_AttendedWeek()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: true);
            f.RecordMinutes(1, 10, 60);

            string outcome = f.Fire(10);

            Assert.AreEqual("ended", outcome);
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "the full agreed weekly wage leaves business cash.");
            Assert.AreEqual(1000, f.HouseholdBalance(),
                "the final wage reaches the worker's household.");
            int manualPayments = 0;
            foreach (ManualWagePaymentRecord record in f.Business.RuntimeState.ManualWagePayments)
                if (record != null && record.EmploymentId == "r17-emp-1" && record.CashPaidCents == 1000)
                    manualPayments++;
            Assert.AreEqual(1, manualPayments, "exactly one manual payment records the final wage.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, f.Employment.LifecycleState);
            Assert.AreEqual(EmploymentEndReason.Dismissed, f.Employment.EndReason);
        }

        [Test]
        public void T4_FinalWage_NoAttendance_Zero()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: true);

            string outcome = f.Fire(10);

            Assert.AreEqual("ended", outcome);
            Assert.AreEqual(10000, f.Business.RuntimeState.CurrentCashCents,
                "no wage is invented without work.");
            Assert.AreEqual(0, f.HouseholdBalance());
            Assert.AreEqual(0, f.Business.RuntimeState.ManualWagePayments.Count,
                "0c owed means no manual payment record is created.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, f.Employment.LifecycleState);
            Assert.AreEqual(EmploymentEndReason.Dismissed, f.Employment.EndReason);
        }

        [Test]
        public void T5_WeeklyRunDoesNotRepay()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: true);
            f.RecordMinutes(1, 10, 60);
            Assert.AreEqual("ended", f.Fire(10));
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(1000, f.HouseholdBalance());

            f.Business.RuntimeState.PayrollDayIndex = 10;
            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents,
                "the fired worker is paid 0c by the weekly run — the final wage is exactly-once.");
            Assert.AreEqual(1000, f.HouseholdBalance());
        }

        [Test]
        public void T6_RegistryUnwired_RefusesWithNoCashMovement()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: true);
            f.AddSlot("1", 1000);

            string outcome = EmploymentFiringService.EndEmploymentForFiring(
                f.Business.RuntimeState, null, 1, 10, new List<string>());

            Assert.IsNotNull(outcome, "a null registry refuses loudly.");
            Assert.IsTrue(outcome.Contains("refused"),
                "the refusal names itself: " + outcome);
            Assert.AreEqual(10000, f.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(0, f.HouseholdBalance());
            Assert.AreEqual(0, f.Business.RuntimeState.ManualWagePayments.Count);
            Assert.AreEqual(EmploymentLifecycleState.Active, f.Employment.LifecycleState,
                "no record ends on a refused firing.");
            Assert.IsTrue(f.Business.RuntimeState.WorkerSlots[0].IsFilled,
                "the slot stays filled — the manager's loud refusal depends on this.");
        }

        [Test]
        public void T7_EnsureProjectsRecord_NoResurrection()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: false);
            f.AddSlot("1", 1000);
            f.RecordMinutes(1, 10, 60);

            f.Business.RuntimeState.EnsureEmploymentRecordsFromSlots();
            Assert.AreEqual(1, f.Employments.GetByEmployee(1).Count,
                "the ensure projects one record from the filled slot.");
            EmploymentRelationship projected = f.Employments.GetByEmployee(1)[0];
            Assert.AreEqual(EmploymentSource.ProjectedFromWorkerSlot, projected.Source);
            Assert.AreEqual(1000, projected.Compensation.AgreedWeeklyWageCents,
                "the projected wage comes from the slot wage.");

            string outcome = f.Fire(10);
            Assert.AreEqual("ended", outcome);
            Assert.AreEqual(9000, f.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(1000, f.HouseholdBalance());

            f.Business.RuntimeState.EnsureEmploymentRecordsFromSlots();
            Assert.AreEqual(1, f.Employments.GetByEmployee(1).Count,
                "the ensure never resurrects an Ended record.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, f.Employments.GetByEmployee(1)[0].LifecycleState);
        }

        [Test]
        public void T8_ShortfallAtFiring_LiabilityPreserved()
        {
            var f = new Fixture(withRegistry: true, cashCents: 100, withEmploymentRecord: true);
            f.RecordMinutes(1, 10, 60);

            string outcome = f.Fire(10);

            Assert.AreEqual("ended-with-shortfall", outcome);
            Assert.AreEqual(100, f.Business.RuntimeState.CurrentCashCents,
                "no cash moves on a shortfall.");
            Assert.AreEqual(0, f.HouseholdBalance());
            Assert.AreEqual(1000, f.Business.RuntimeState.AccruedLiabilityCents,
                "the wage is preserved as an operating liability.");
            int shortfallRecords = 0;
            foreach (ManualWagePaymentRecord record in f.Business.RuntimeState.ManualWagePayments)
                if (record != null && record.EmploymentId == "r17-emp-1"
                    && record.CashPaidCents == 0 && record.AccruedCents == 1000)
                    shortfallRecords++;
            Assert.AreEqual(1, shortfallRecords,
                "one shortfall record: 0c cash, 1000c accrued.");
            Assert.AreEqual(EmploymentLifecycleState.Suspended, f.Employment.LifecycleState,
                "the shortfall corner leaves the record Suspended, not Ended (note 114 Q2).");
            Assert.AreEqual(0, f.Employments.GetActiveByEmployee(1).Count,
                "Suspended is not Active, so the worker is still hireable.");
        }

        [Test]
        public void T9_EndAllActiveEmployments()
        {
            var f = new Fixture(withRegistry: true, cashCents: 10000, withEmploymentRecord: true);
            var second = new EmploymentRelationship
            {
                Id = "r17-emp-2",
                EmployeePersonId = 1,
                EmployerBusinessId = "other-business",
                RoleDisplayName = "Hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(500, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
            Assert.IsNull(f.Employments.Register(second));

            List<EmploymentRelationship> ended = f.Employments.EndEmploymentByReason(
                1, EmploymentEndReason.Dismissed, 10);

            Assert.AreEqual(2, ended.Count,
                "EndEmploymentByReason ends every Active employment the person holds (Q5 end-all).");
            Assert.AreEqual(EmploymentLifecycleState.Ended, f.Employment.LifecycleState);
            Assert.AreEqual(EmploymentLifecycleState.Ended, second.LifecycleState);
        }
    }
}
