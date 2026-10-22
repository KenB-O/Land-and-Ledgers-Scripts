using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Pass IX review R-05: named payables are part of the business
    /// runtime's persisted state. A save/load round-trip must preserve the
    /// creditor, amount, reason, day, ObligationId and the CashAlreadyDeducted
    /// escrow flag — and the restored record must still settle/flush exactly
    /// once.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixNamedPayablePersistenceTests
    {
        private static BusinessRuntimeState NewBusiness(string id, int cashCents)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cashCents);
            return runtime;
        }

        private static EmploymentRelationship NewEmployment(string businessId, int personId, int wageCents, string id)
        {
            return new EmploymentRelationship
            {
                Id = id,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                RoleDisplayName = "Hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(wageCents, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
        }

        [Test]
        public void EscrowedPayable_RoundTripsIntact_ThenFlushesAndSettlesExactlyOnce()
        {
            // The A3-10 case: payroll with unwired household ledgers escrows
            // the deducted wage as a named payable with CashAlreadyDeducted.
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-esc", 5, 800, "esc-emp-5"));
            BusinessRuntimeState runtime = NewBusiness("biz-esc", 10000);
            runtime.EmploymentRegistry = employments;
            runtime.PayrollDayIndex = 42;
            runtime.ResolveWeeklyPayroll();
            Assert.AreEqual(1, runtime.NamedPayables.Count);
            Assert.IsTrue(runtime.NamedPayables[0].CashAlreadyDeducted);

            BusinessRuntimeSaveDto dto = runtime.CaptureSaveDto();
            Assert.AreEqual(1, dto.namedPayables.Count, "the DTO itself must carry the record");
            BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, BusinessType.GeneralStore);

            Assert.AreEqual(9200, restored.CurrentCashCents);
            Assert.AreEqual(800, restored.AccruedLiabilityCents,
                "scalar liability total survives alongside the named records");
            Assert.AreEqual(1, restored.NamedPayables.Count);
            BusinessNamedPayableRecord record = restored.NamedPayables[0];
            Assert.AreEqual("person:5", record.Creditor);
            Assert.AreEqual(800, record.AmountCents);
            Assert.AreEqual(42, record.DayIndex);
            Assert.IsTrue(record.CashAlreadyDeducted, "the escrow flag survives — settlement must not deduct twice");
            Assert.AreEqual(800, restored.NamedPayableTotalCents);

            // Wiring arrives after the load: the restored record flushes once.
            restored.ObligationAuthority = new FinancialObligationAuthority();
            restored.ObligationIdRegistry = new EntityIdRegistry();
            Assert.AreEqual(1, restored.FlushNamedPayablesToAuthority());
            Assert.AreEqual(0, restored.FlushNamedPayablesToAuthority(), "flush stays idempotent after restore");
            string obligationId = restored.NamedPayables[0].ObligationId;
            Assert.IsFalse(string.IsNullOrWhiteSpace(obligationId));
            Assert.AreEqual(800, restored.ObligationAuthority.TotalOutstandingFor("biz-esc"));

            // And it settles exactly once, without a second cash deduction.
            Assert.IsTrue(restored.TrySettleNamedPayable(obligationId, 800, 43, out string diagnostic), diagnostic);
            Assert.AreEqual(9200, restored.CurrentCashCents, "escrowed settlement never deducts cash twice");
            Assert.AreEqual(0, restored.NamedPayables.Count);
            Assert.AreEqual(0, restored.AccruedLiabilityCents);
            Assert.AreEqual(0, restored.ObligationAuthority.TotalOutstandingFor("biz-esc"));
            Assert.IsFalse(restored.TrySettleNamedPayable(obligationId, 800, 44, out _),
                "a settled payable cannot be settled a second time");
        }

        [Test]
        public void OrdinaryPayable_ObligationIdSurvivesRoundTrip_SettleSpendsCashOnce()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            BusinessRuntimeState runtime = NewBusiness("biz-ord", 1000);
            runtime.ObligationAuthority = authority;
            runtime.ObligationIdRegistry = ids;
            runtime.PayrollDayIndex = 7;
            runtime.AccrueOperatingLiabilityCents(700, "supplier:acme", "unpaid supplier invoice");
            string obligationId = runtime.NamedPayables[0].ObligationId;
            Assert.IsFalse(string.IsNullOrWhiteSpace(obligationId));

            BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(
                runtime.CaptureSaveDto(), BusinessType.GeneralStore);

            Assert.AreEqual(1, restored.NamedPayables.Count);
            Assert.AreEqual(obligationId, restored.NamedPayables[0].ObligationId,
                "the restored record still points at the same authority obligation");
            Assert.AreEqual("supplier:acme", restored.NamedPayables[0].Creditor);
            Assert.AreEqual("unpaid supplier invoice", restored.NamedPayables[0].Reason);
            Assert.IsFalse(restored.NamedPayables[0].CashAlreadyDeducted);
            Assert.AreEqual(700, restored.AccruedLiabilityCents);

            restored.ObligationAuthority = authority;
            restored.ObligationIdRegistry = ids;
            Assert.AreEqual(0, restored.FlushNamedPayablesToAuthority(),
                "an already-posted record is never re-posted after restore");
            Assert.IsTrue(restored.TrySettleNamedPayable(obligationId, 700, 8, out string diagnostic), diagnostic);
            Assert.AreEqual(300, restored.CurrentCashCents, "ordinary settlement spends real cash exactly once");
            Assert.AreEqual(0, authority.TotalOutstandingFor("biz-ord"));
        }

        [Test]
        public void LegacyDtoWithoutNamedPayables_LoadsWithEmptyBook()
        {
            BusinessRuntimeState runtime = NewBusiness("biz-legacy", 500);
            BusinessRuntimeSaveDto dto = runtime.CaptureSaveDto();
            dto.namedPayables = null; // an old save has no such list at all

            BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, BusinessType.GeneralStore);

            Assert.IsNotNull(restored);
            Assert.AreEqual(0, restored.NamedPayables.Count);
            Assert.AreEqual(500, restored.CurrentCashCents);
        }
    }
}
