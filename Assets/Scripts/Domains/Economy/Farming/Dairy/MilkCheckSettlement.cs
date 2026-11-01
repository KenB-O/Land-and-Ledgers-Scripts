using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Dairy
{
    /// <summary>
    /// W10B: one butterfat test against a patron's daily delivery. The creamery tests
    /// each patron's milk and settles on butterfat, not raw volume — the monthly
    /// milk-check model deferred from EQU/T1.
    ///
    /// Honest-seam note: the READING (Butterfat01) is recorded from the named tester's
    /// test event; the number itself is supplied by the game layer (herd model) as
    /// calibration input. The test/settlement math below is real: a test must name a
    /// real person as tester and reference a real accepted delivery.
    /// </summary>
    [Serializable]
    public sealed class ButterfatTest
    {
        public EntityId TestId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public EntityId DeliveryId = EntityId.Invalid; // CreameryDelivery.DeliveryId
        public string FarmId = string.Empty;
        public int DayIndex;
        public int UnitsTested;
        public float Butterfat01; // fraction, e.g. 0.038 = 3.8%
        public EntityId TesterWorkerId = EntityId.Invalid; // EntityKind.Person — the creamery's tester
        public string Notes = string.Empty;

        public ButterfatTest() { }

        /// <summary>Pounds of butterfat this test accounts for.</summary>
        public float ButterfatPounds => Math.Max(0, UnitsTested) * Mathf.Clamp01(Butterfat01);
    }

    /// <summary>W10B: one deduction line on a patron statement — named reason, real amount. Never invented.</summary>
    [Serializable]
    public sealed class PatronStatementDeduction
    {
        public string Reason = string.Empty;
        public int AmountCents;

        public PatronStatementDeduction() { }
    }

    /// <summary>
    /// W10B: one patron's monthly milk statement. Aggregates the creamery's daily
    /// deliveries for the month: tested milk settles on butterfat pounds, untested
    /// milk settles at the creamery's flat patron price (flagged, never silently
    /// upgraded to butterfat pricing). Deductions are explicit lines with reasons.
    /// </summary>
    [Serializable]
    public sealed class PatronMilkStatement
    {
        public EntityId StatementId = EntityId.Invalid; // EntityKind.Contract (HF-1)
        public string CreameryBusinessId = string.Empty;
        public string FarmId = string.Empty;
        public int MonthStartDayIndex;
        public int MonthEndDayIndex; // inclusive
        public List<EntityId> DeliveryIds = new List<EntityId>();
        public List<EntityId> TestIds = new List<EntityId>();
        public List<EntityId> UntestedDeliveryIds = new List<EntityId>(); // settled flat, flagged
        public int TotalAcceptedUnits;
        public int TotalRejectedUnits;
        public int TestedUnits;
        public int UntestedUnits;
        public float TotalButterfatPounds;
        public int GrossPayableCents;
        public List<PatronStatementDeduction> Deductions = new List<PatronStatementDeduction>();
        public int NetPayableCents;
        public int PricePerButterfatPoundCents; // the rate this statement settled at
        public string Notes = string.Empty;

        public PatronMilkStatement() { }

        public int TotalDeductionsCents
        {
            get
            {
                int total = 0;
                foreach (PatronStatementDeduction d in Deductions) total += Math.Max(0, d.AmountCents);
                return total;
            }
        }
    }

    /// <summary>W10B: lifecycle of a monthly milk check.</summary>
    public enum MilkCheckStatus
    {
        Issued = 0,
        Paid = 1,
        Voided = 2,
    }

    /// <summary>
    /// W10B: the monthly milk check — the creamery's payout instrument. Issuing the
    /// check books a REAL payable on the creamery (BusinessLiabilityLedger, the
    /// creamery as settlement authority); paying it settles that payable and records
    /// the patron's inflow with provenance. The check itself is the patron's
    /// receivable until paid.
    /// </summary>
    [Serializable]
    public sealed class MilkCheck
    {
        public EntityId CheckId = EntityId.Invalid; // EntityKind.Contract (HF-1)
        public string CreameryBusinessId = string.Empty;
        public string PatronFarmId = string.Empty;
        public EntityId StatementId = EntityId.Invalid;
        public int AmountCents;
        public int IssuedDayIndex;
        public int PaidDayIndex = -1;
        public MilkCheckStatus Status = MilkCheckStatus.Issued;
        public EntityId LiabilityId = EntityId.Invalid; // the BusinessLiability payable (EntityKind.Contract)

        public MilkCheck() { }
    }

    /// <summary>
    /// W10B: the creamery as settlement authority. Butterfat tests, patron statements,
    /// and milk checks — the monthly settlement cycle for patron milk deliveries.
    /// </summary>
    public sealed class MilkCheckService
    {
        /// <summary>
        /// Calibration: standard butterfat fraction the price-per-pound is anchored to.
        /// At exactly this fat, butterfat settlement equals the creamery's flat patron
        /// price — above it the patron earns a premium, below it a discount. (Canon
        /// Part XV: calibration, not canon.)
        /// </summary>
        public const float StandardButterfat01 = 0.038f;

        private readonly List<ButterfatTest> tests = new List<ButterfatTest>();
        private readonly List<PatronMilkStatement> statements = new List<PatronMilkStatement>();
        private readonly List<MilkCheck> checks = new List<MilkCheck>();

        // D3F: deliveries already covered by a built statement. A delivery settles
        // exactly once — overlapping month ranges must not pay the patron twice.
        private readonly List<EntityId> coveredDeliveryIds = new List<EntityId>();

        public IReadOnlyList<ButterfatTest> Tests => tests;
        public IReadOnlyList<PatronMilkStatement> Statements => statements;
        public IReadOnlyList<MilkCheck> Checks => checks;

        /// <summary>
        /// The butterfat-pound price that keeps settlement honest against the flat rate:
        /// price-per-pound = flat unit price / standard butterfat. Documented so the
        /// Unity-side price board can display the derivation.
        /// </summary>
        public static int PricePerButterfatPoundCents(Creamery creamery)
        {
            if (creamery == null) return 0;
            return (int)Math.Round(Math.Max(0, creamery.PricePerMilkUnitCents) / StandardButterfat01);
        }

        /// <summary>
        /// Records the tester's butterfat reading for one accepted delivery. Refusals
        /// are loud: unknown delivery, no accepted milk, unnamed tester, or a
        /// nonsensical reading never become a test record.
        /// </summary>
        public ButterfatTest RecordButterfatTest(
            Creamery creamery,
            CreameryDelivery delivery,
            float butterfat01,
            EntityId testerWorkerId,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (creamery == null || delivery == null)
            {
                diagnostics.Add("MilkCheckService: need a creamery and a delivery to test — no test without both.");
                return null;
            }

            bool known = false;
            foreach (CreameryDelivery d in creamery.Deliveries)
            {
                if (d != null && d.DeliveryId.Equals(delivery.DeliveryId)) { known = true; break; }
            }

            if (!known)
            {
                diagnostics.Add($"MilkCheckService: delivery {delivery.DeliveryId} is not on {creamery.CreameryName}'s books — untested.");
                return null;
            }

            if (delivery.AcceptedUnits <= 0)
            {
                diagnostics.Add($"MilkCheckService: delivery {delivery.DeliveryId} has no accepted milk — nothing to test.");
                return null;
            }

            if (!testerWorkerId.IsValid || testerWorkerId.Kind != EntityKind.Person)
            {
                diagnostics.Add("MilkCheckService: the butterfat test must name the tester (a real person) — anonymous tests are not tests.");
                return null;
            }

            if (!(butterfat01 > 0f) || butterfat01 >= 1f)
            {
                diagnostics.Add($"MilkCheckService: butterfat reading {butterfat01} is not a sane fraction — test refused.");
                return null;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("MilkCheckService: no id registry — the test is unrecorded, not free.");
                return null;
            }

            var test = new ButterfatTest
            {
                TestId = idRegistry.Allocate(EntityKind.Lot),
                DeliveryId = delivery.DeliveryId,
                FarmId = delivery.FarmId,
                DayIndex = dayIndex,
                UnitsTested = delivery.AcceptedUnits,
                Butterfat01 = butterfat01,
                TesterWorkerId = testerWorkerId,
            };
            tests.Add(test);
            diagnostics.Add($"MilkCheckService: butterfat test {test.TestId} — farm '{delivery.FarmId}' delivery {delivery.DeliveryId}: " +
                $"{butterfat01:P2} on {delivery.AcceptedUnits} units (tester {testerWorkerId}).");
            return test;
        }

        /// <summary>
        /// Builds one patron's monthly statement from the creamery's deliveries plus
        /// recorded tests. Tested milk settles on butterfat pounds; untested milk
        /// settles at the flat patron price and is flagged. Deductions reduce the net;
        /// a net that would go negative is floored at zero with the carried shortfall
        /// shouted in diagnostics — never silently negative.
        /// </summary>
        public PatronMilkStatement BuildPatronStatement(
            Creamery creamery,
            string farmId,
            int monthStartDayIndex,
            int monthEndDayIndex,
            List<PatronStatementDeduction> deductions,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (creamery == null || string.IsNullOrWhiteSpace(farmId) || idRegistry == null)
            {
                diagnostics.Add("MilkCheckService: need a creamery, a named patron farm, and an id registry — no statement built.");
                return null;
            }

            int butterfatPrice = PricePerButterfatPoundCents(creamery);
            int flatPrice = Math.Max(0, creamery.PricePerMilkUnitCents);

            var statement = new PatronMilkStatement
            {
                StatementId = idRegistry.Allocate(EntityKind.Contract),
                CreameryBusinessId = creamery.CreameryBusinessId,
                FarmId = farmId,
                MonthStartDayIndex = monthStartDayIndex,
                MonthEndDayIndex = monthEndDayIndex,
                PricePerButterfatPoundCents = butterfatPrice,
            };

            // Latest test per delivery wins; earlier ones are superseded (diagnosed).
            var latestTest = new Dictionary<EntityId, ButterfatTest>();
            foreach (ButterfatTest t in tests)
            {
                if (t == null || !string.Equals(t.FarmId, farmId, StringComparison.Ordinal)) continue;
                if (t.DayIndex < monthStartDayIndex || t.DayIndex > monthEndDayIndex) continue;
                if (latestTest.TryGetValue(t.DeliveryId, out ButterfatTest prior))
                {
                    diagnostics.Add($"MilkCheckService: superseded butterfat test {prior.TestId} for delivery {t.DeliveryId} (later test {t.TestId} stands).");
                }
                latestTest[t.DeliveryId] = t;
            }

            foreach (CreameryDelivery delivery in creamery.Deliveries)
            {
                if (delivery == null || !string.Equals(delivery.FarmId, farmId, StringComparison.Ordinal)) continue;
                if (delivery.DayIndex < monthStartDayIndex || delivery.DayIndex > monthEndDayIndex) continue;
                if (delivery.AcceptedUnits <= 0 && delivery.RejectedUnits <= 0) continue;

                // D3F: a delivery settles exactly once. If an earlier statement already
                // covered it (overlapping month ranges are a caller error), refuse
                // loudly rather than paying the patron twice.
                if (delivery.AcceptedUnits > 0 && coveredDeliveryIds.Contains(delivery.DeliveryId))
                {
                    diagnostics.Add($"MilkCheckService: delivery {delivery.DeliveryId} (farm '{farmId}', day {delivery.DayIndex}) " +
                        "is already covered by an earlier statement — no double settlement. Narrow the month range or void the prior statement first.");
                    return null;
                }

                statement.DeliveryIds.Add(delivery.DeliveryId);
                statement.TotalAcceptedUnits += delivery.AcceptedUnits;
                statement.TotalRejectedUnits += delivery.RejectedUnits;

                if (delivery.AcceptedUnits <= 0) continue;

                if (latestTest.TryGetValue(delivery.DeliveryId, out ButterfatTest test))
                {
                    float pounds = delivery.AcceptedUnits * Mathf.Clamp01(test.Butterfat01);
                    statement.TotalButterfatPounds += pounds;
                    statement.TestedUnits += delivery.AcceptedUnits;
                    statement.TestIds.Add(test.TestId);
                    statement.GrossPayableCents += (int)Math.Round(pounds * butterfatPrice);
                }
                else
                {
                    statement.UntestedUnits += delivery.AcceptedUnits;
                    statement.UntestedDeliveryIds.Add(delivery.DeliveryId);
                    statement.GrossPayableCents += delivery.AcceptedUnits * flatPrice;
                    diagnostics.Add($"MilkCheckService: delivery {delivery.DeliveryId} untested — " +
                        $"{delivery.AcceptedUnits} units settle at the flat patron price ({flatPrice}c), flagged on the statement.");
                }
            }

            if (deductions != null)
            {
                foreach (PatronStatementDeduction d in deductions)
                {
                    if (d == null || d.AmountCents <= 0) continue;
                    statement.Deductions.Add(new PatronStatementDeduction
                    {
                        Reason = d.Reason ?? string.Empty,
                        AmountCents = d.AmountCents,
                    });
                }
            }

            int net = statement.GrossPayableCents - statement.TotalDeductionsCents;
            if (net < 0)
            {
                diagnostics.Add($"MilkCheckService: deductions ({statement.TotalDeductionsCents}c) exceed gross ({statement.GrossPayableCents}c) " +
                    $"for farm '{farmId}' — net floored at 0; the {-net}c shortfall carries as an account note, not a negative check.");
                statement.Notes = $"carried shortfall {-net}c (deductions exceeded gross)";
                net = 0;
            }

            statement.NetPayableCents = net;
            statements.Add(statement);

            // D3F: mark every accepted delivery in this statement as settled so a
            // later overlapping build refuses instead of double-paying.
            foreach (EntityId deliveryId in statement.DeliveryIds)
            {
                if (deliveryId.IsValid && !coveredDeliveryIds.Contains(deliveryId))
                    coveredDeliveryIds.Add(deliveryId);
            }

            diagnostics.Add($"MilkCheckService: statement {statement.StatementId} — farm '{farmId}' month [{monthStartDayIndex}..{monthEndDayIndex}]: " +
                $"{statement.TotalAcceptedUnits} units accepted ({statement.TestedUnits} tested, {statement.UntestedUnits} untested), " +
                $"{statement.TotalButterfatPounds:F2} butterfat lb, gross {statement.GrossPayableCents}c, " +
                $"deductions {statement.TotalDeductionsCents}c, net {statement.NetPayableCents}c.");
            return statement;
        }

        /// <summary>
        /// Issues the monthly milk check: the creamery books a REAL payable to the
        /// patron (BusinessLiabilityLedger.BuyOnCredit — the creamery is the settlement
        /// authority). The issued check is the patron's receivable until paid.
        /// </summary>
        public MilkCheck IssueMilkCheck(
            PatronMilkStatement statement,
            string patronDisplayName,
            BusinessLiabilityLedger liabilityLedger,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (statement == null)
            {
                diagnostics.Add("MilkCheckService: no statement — no check without a settled statement.");
                return null;
            }

            if (statement.NetPayableCents <= 0)
            {
                diagnostics.Add($"MilkCheckService: statement {statement.StatementId} nets {statement.NetPayableCents}c — no check issued for a zero net.");
                return null;
            }

            if (liabilityLedger == null || idRegistry == null)
            {
                diagnostics.Add("MilkCheckService: need a liability ledger and id registry — the check is unissued, not free.");
                return null;
            }

            string counterparty = string.IsNullOrWhiteSpace(patronDisplayName) ? statement.FarmId : patronDisplayName;

            BusinessLiability liability = liabilityLedger.BuyOnCredit(
                idRegistry,
                statement.CreameryBusinessId,
                counterparty,
                statement.NetPayableCents,
                $"monthly milk check: statement {statement.StatementId}, farm '{statement.FarmId}', " +
                $"{statement.TotalAcceptedUnits} units / {statement.TotalButterfatPounds:F2} butterfat lb",
                dayIndex,
                diagnostics);

            if (liability == null)
            {
                diagnostics.Add("MilkCheckService: the payable could not be recorded — no check issued.");
                return null;
            }

            var check = new MilkCheck
            {
                CheckId = idRegistry.Allocate(EntityKind.Contract),
                CreameryBusinessId = statement.CreameryBusinessId,
                PatronFarmId = statement.FarmId,
                StatementId = statement.StatementId,
                AmountCents = statement.NetPayableCents,
                IssuedDayIndex = dayIndex,
                Status = MilkCheckStatus.Issued,
                LiabilityId = liability.LiabilityId,
            };
            checks.Add(check);

            diagnostics.Add($"MilkCheckService: milk check {check.CheckId} issued — {statement.CreameryBusinessId} owes " +
                $"{counterparty} {check.AmountCents}c (payable {liability.LiabilityId}).");
            return check;
        }

        /// <summary>
        /// Pays an issued milk check: settles the creamery's payable and records the
        /// patron's inflow with provenance (SaleProceeds against the check id). Both
        /// sides of the money are real ledger events.
        /// </summary>
        public string PayMilkCheck(
            MilkCheck check,
            BusinessLiabilityLedger liabilityLedger,
            HouseholdLedger creameryPayerLedger,
            HouseholdLedger patronLedger,
            string creameryName,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (check == null) return "MilkCheckService: no check to pay.";
            if (check.Status != MilkCheckStatus.Issued)
                return $"MilkCheckService: check {check.CheckId} is {check.Status} — only issued checks can be paid.";
            if (liabilityLedger == null) return "MilkCheckService: no liability ledger — the payable cannot be settled.";
            if (patronLedger == null) return "MilkCheckService: no patron ledger — the patron's receipt needs provenance (Canon 13.2).";

            string problem = liabilityLedger.Repay(
                check.LiabilityId.ToString(), check.AmountCents,
                dayIndex, creameryPayerLedger, diagnostics);
            if (problem != null) return $"MilkCheckService: {problem}";

            string inflowProblem = patronLedger.RecordInflow(
                dayIndex,
                check.AmountCents,
                HouseholdIncomeSource.SaleProceeds,
                check.CheckId.ToString(),
                $"milk check {check.CheckId}: monthly patron settlement from {creameryName}",
                creameryName ?? string.Empty);
            if (inflowProblem != null)
            {
                diagnostics.Add("MilkCheckService patron receipt note: " + inflowProblem);
            }

            check.Status = MilkCheckStatus.Paid;
            check.PaidDayIndex = dayIndex;
            diagnostics.Add($"MilkCheckService: milk check {check.CheckId} paid — {check.AmountCents}c to farm '{check.PatronFarmId}'.");
            return null;
        }

        /// <summary>Voids an issued (unpaid) check. Paid checks cannot be voided.</summary>
        public string VoidMilkCheck(MilkCheck check, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (check == null) return "MilkCheckService: no check to void.";
            if (check.Status == MilkCheckStatus.Paid)
                return $"MilkCheckService: check {check.CheckId} is already paid — paid money cannot be un-paid.";
            check.Status = MilkCheckStatus.Voided;
            diagnostics.Add($"MilkCheckService: check {check.CheckId} voided (was {MilkCheckStatus.Issued}). " +
                $"Note: the recorded payable {check.LiabilityId} must be settled or written off through the liability ledger — voiding the check does not erase the debt.");
            return null;
        }

        /// <summary>D3F: save support (CLN-1 pattern). Tests, statements, checks, and
        /// covered-delivery ids must survive a save — otherwise a reload would let an
        /// already-settled delivery settle again.</summary>
        public MilkCheckServiceSaveDto CaptureSaveDto()
        {
            return new MilkCheckServiceSaveDto
            {
                tests = new List<ButterfatTest>(tests),
                statements = new List<PatronMilkStatement>(statements),
                checks = new List<MilkCheck>(checks),
                coveredDeliveryIds = new List<EntityId>(coveredDeliveryIds),
            };
        }

        /// <summary>D3F: save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(MilkCheckServiceSaveDto dto)
        {
            tests.Clear();
            statements.Clear();
            checks.Clear();
            coveredDeliveryIds.Clear();
            if (dto == null) return;
            if (dto.tests != null) tests.AddRange(dto.tests);
            if (dto.statements != null) statements.AddRange(dto.statements);
            if (dto.checks != null) checks.AddRange(dto.checks);
            if (dto.coveredDeliveryIds != null) coveredDeliveryIds.AddRange(dto.coveredDeliveryIds);
        }
    }

    /// <summary>D3F: save DTO for the milk-check settlement service (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class MilkCheckServiceSaveDto
    {
        public List<ButterfatTest> tests = new List<ButterfatTest>();
        public List<PatronMilkStatement> statements = new List<PatronMilkStatement>();
        public List<MilkCheck> checks = new List<MilkCheck>();
        public List<EntityId> coveredDeliveryIds = new List<EntityId>();
    }

    /// <summary>
    /// W10B: renders a patron's monthly statement as readable text — the seam the
    /// Unity-side patron statement view will call.
    /// </summary>
    public static class PatronStatementRenderer
    {
        public static string ToText(PatronMilkStatement statement, string creameryName, string patronName)
        {
            var sb = new StringBuilder();
            string creamery = string.IsNullOrWhiteSpace(creameryName) ? "Creamery" : creameryName;
            string patron = string.IsNullOrWhiteSpace(patronName) ? (statement?.FarmId ?? "?") : patronName;

            sb.AppendLine($"{creamery} — Monthly Milk Statement");
            sb.AppendLine($"Patron: {patron}");
            if (statement == null)
            {
                sb.AppendLine("(no statement)");
                return sb.ToString();
            }

            sb.AppendLine($"Settlement month: days {statement.MonthStartDayIndex}–{statement.MonthEndDayIndex}");
            sb.AppendLine($"Statement {statement.StatementId}");
            sb.AppendLine();
            sb.AppendLine($"Milk accepted: {statement.TotalAcceptedUnits} units ({statement.TestedUnits} tested, {statement.UntestedUnits} untested at flat price)");
            sb.AppendLine($"Milk rejected (sour): {statement.TotalRejectedUnits} units");
            sb.AppendLine($"Butterfat settled: {statement.TotalButterfatPounds:F2} lb @ {statement.PricePerButterfatPoundCents}c/lb");
            sb.AppendLine($"Gross payable: {statement.GrossPayableCents}c");
            foreach (PatronStatementDeduction d in statement.Deductions)
            {
                sb.AppendLine($"  deduction: {d.Reason} — {d.AmountCents}c");
            }

            sb.AppendLine($"Total deductions: {statement.TotalDeductionsCents}c");
            sb.AppendLine($"NET PAYABLE (milk check): {statement.NetPayableCents}c");
            if (statement.UntestedDeliveryIds.Count > 0)
            {
                sb.AppendLine($"Note: {statement.UntestedDeliveryIds.Count} delivery(ies) untested — settled at flat patron price, flagged above.");
            }

            if (!string.IsNullOrWhiteSpace(statement.Notes))
            {
                sb.AppendLine($"Note: {statement.Notes}");
            }

            return sb.ToString();
        }
    }
}
