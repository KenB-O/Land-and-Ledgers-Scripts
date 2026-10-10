using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// Phase G (G4): where an NPC business stands in its life. Append-only.
    /// A business is a real economic actor after formation: it can
    /// struggle, pivot, borrow, compete, close in an orderly way, or be
    /// acquired — never vanish with debts.
    /// </summary>
    public enum NpcBusinessLifeState
    {
        Unspecified = 0,
        Forming = 1,
        Operating = 2,
        Struggling = 3,
        Pivoting = 4,
        Closing = 5,
        Closed = 6,
        Acquired = 7,
    }

    /// <summary>
    /// Phase G (G4): the lifecycle record for one NPC business. Cash itself
    /// lives in the business runtime (mutations inside that class); this
    /// record tracks lifecycle state and the obligation/employment refs the
    /// close-down and acquisition flows must never strand.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessLifeRecord
    {
        public string BusinessInstanceId = string.Empty;
        public string BusinessEntityKey = string.Empty;
        public int FounderPersonId = -1;
        public string OwnerName = string.Empty;
        public NpcBusinessLifeState State = NpcBusinessLifeState.Unspecified;
        public List<string> CapabilityIds = new List<string>();
        public List<string> OpenObligationIds = new List<string>();
        public List<string> EmploymentIds = new List<string>();
        public int ConsecutiveHealthyWeeks;
        public int ConsecutiveStruggleWeeks;
        public int LastEvaluatedDayIndex = -1;
        public string CloseReason = string.Empty;

        public NpcBusinessLifeRecord() { }

        public NpcBusinessLifeRecordSaveDto CaptureSaveDto()
        {
            var dto = new NpcBusinessLifeRecordSaveDto
            {
                BusinessInstanceId = BusinessInstanceId,
                BusinessEntityKey = BusinessEntityKey,
                FounderPersonId = FounderPersonId,
                OwnerName = OwnerName,
                State = State,
                ConsecutiveHealthyWeeks = ConsecutiveHealthyWeeks,
                ConsecutiveStruggleWeeks = ConsecutiveStruggleWeeks,
                LastEvaluatedDayIndex = LastEvaluatedDayIndex,
                CloseReason = CloseReason,
            };
            if (CapabilityIds != null) dto.CapabilityIds.AddRange(CapabilityIds);
            if (OpenObligationIds != null) dto.OpenObligationIds.AddRange(OpenObligationIds);
            if (EmploymentIds != null) dto.EmploymentIds.AddRange(EmploymentIds);
            return dto;
        }

        public void LoadFromSaveDto(NpcBusinessLifeRecordSaveDto dto)
        {
            CapabilityIds.Clear();
            OpenObligationIds.Clear();
            EmploymentIds.Clear();
            if (dto == null) return;
            BusinessInstanceId = dto.BusinessInstanceId;
            BusinessEntityKey = dto.BusinessEntityKey;
            FounderPersonId = dto.FounderPersonId;
            OwnerName = dto.OwnerName;
            State = dto.State;
            ConsecutiveHealthyWeeks = dto.ConsecutiveHealthyWeeks;
            ConsecutiveStruggleWeeks = dto.ConsecutiveStruggleWeeks;
            LastEvaluatedDayIndex = dto.LastEvaluatedDayIndex;
            CloseReason = dto.CloseReason;
            if (dto.CapabilityIds != null) CapabilityIds.AddRange(dto.CapabilityIds);
            if (dto.OpenObligationIds != null) OpenObligationIds.AddRange(dto.OpenObligationIds);
            if (dto.EmploymentIds != null) EmploymentIds.AddRange(dto.EmploymentIds);
        }
    }

    /// <summary>CLN-1: persisted NPC business lifecycle record (save pipeline).</summary>
    [Serializable]
    public sealed class NpcBusinessLifeRecordSaveDto
    {
        public string BusinessInstanceId = string.Empty;
        public string BusinessEntityKey = string.Empty;
        public int FounderPersonId = -1;
        public string OwnerName = string.Empty;
        public NpcBusinessLifeState State = NpcBusinessLifeState.Unspecified;
        public List<string> CapabilityIds = new List<string>();
        public List<string> OpenObligationIds = new List<string>();
        public List<string> EmploymentIds = new List<string>();
        public int ConsecutiveHealthyWeeks;
        public int ConsecutiveStruggleWeeks;
        public int LastEvaluatedDayIndex = -1;
        public string CloseReason = string.Empty;

        public NpcBusinessLifeRecordSaveDto() { }
    }

    /// <summary>
    /// Phase G (G4): the port to a business's real cash. The Unity runtime
    /// implements this over <see cref="BusinessRuntimeState"/> (all cash
    /// mutations inside that class); tests use a fake with conserved
    /// balances.
    /// </summary>
    public interface INpcBusinessCashPort
    {
        int ReadCashCents(string businessInstanceId);

        /// <summary>Returns null on success, a loud refusal otherwise.</summary>
        string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose);

        /// <summary>Returns null on success, a loud refusal otherwise.</summary>
        string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason);
    }

    /// <summary>
    /// Phase G (G4): a business's cash seen as a real cash store so the NPC
    /// credit loop (Phase E) can lend to it — advances debit the lender's
    /// real store and credit the business's, cent for cent.
    /// </summary>
    public sealed class NpcBusinessCashStore : IRealCashStore
    {
        private readonly INpcBusinessCashPort port;
        private readonly string businessInstanceId;

        public NpcBusinessCashStore(string businessInstanceId, INpcBusinessCashPort port)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.port = port;
        }

        public string OwnerName => "business:" + businessInstanceId;

        public int ReadBalanceCents() => port != null ? port.ReadCashCents(businessInstanceId) : 0;

        public string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty)
        {
            if (port == null) return "NpcBusinessCashStore: no cash port.";
            return port.Spend(businessInstanceId, dayIndex, amountCents,
                $"{purpose} (to {counterparty})");
        }

        public string CreditCents(int dayIndex, int amountCents, string sourceClass,
            string sourceReference, string reason, string counterparty)
        {
            if (port == null) return "NpcBusinessCashStore: no cash port.";
            return port.Receive(businessInstanceId, dayIndex, amountCents,
                $"{reason} [{sourceClass}:{sourceReference}] from {counterparty}");
        }
    }

    /// <summary>
    /// Phase G (G4): one liquidation sale during close-down — a real buyer,
    /// real proceeds. Assets are never written off silently.
    /// </summary>
    [Serializable]
    public sealed class NpcLiquidationSale
    {
        public string AssetItemId = string.Empty;
        public int Units;
        public string BuyerName = string.Empty;
        public int ProceedsCents;

        public NpcLiquidationSale() { }
    }

    /// <summary>
    /// Phase G (G4): the port to real asset buyers during close-down. The
    /// runtime sells to real counterparties; tests use a fake buyer.
    /// </summary>
    public interface INpcLiquidationPort
    {
        /// <summary>Returns null on success with the sale recorded; a loud refusal otherwise.</summary>
        string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
            out NpcLiquidationSale sale);
    }

    /// <summary>
    /// Phase G (G4): everything the lifecycle service needs. Borrowing runs
    /// through the real NPC credit loop; obligations through the shared
    /// authority; employment through the real registry.
    /// </summary>
    public sealed class NpcLifecycleContext
    {
        public INpcBusinessCashPort Cash;
        public FinancialObligationAuthority Obligations;
        public EmploymentRelationshipRegistry Employments;
        public HouseholdLedger FounderHouseholdLedger;
        public NpcFormationAssetRegister AssetRegister;
        public INpcLiquidationPort Liquidation;
        public NpcBusinessEventLog Events;
        public EntityIdRegistry Ids;
        public int DayIndex;
    }

    /// <summary>
    /// Phase G (G4): what a struggling business needs to borrow through the
    /// real NPC credit loop (Phase E).
    /// </summary>
    public sealed class NpcBusinessBorrowContext
    {
        public CreditOfferWorkflow Workflow;
        public CreditCashBridge CashBridge;
        public CreditWorkoutService Workout;
        public CreditRegistry Registry;
        public ForeclosureService Foreclosure;
        public TitleAuthority Titles;
        public CreditEventLog CreditEvents;
        public NpcCreditDecisionEngine CreditEngine;
        public NpcCreditorProfile Lender;
    }

    /// <summary>
    /// Phase G (G4): the life of an NPC business after formation. Real
    /// responses to real trouble: cash-flow problems trigger cost cuts,
    /// real borrowing through the NPC credit loop, and customer-seeking
    /// with real costs; pivots pay real re-tooling; close-downs settle or
    /// explicitly transfer every obligation, liquidate assets to real
    /// buyers, and release staff through the employment authorities —
    /// never vanishing with debts; acquisitions move real cash and assume
    /// obligations through the shared authority.
    /// </summary>
    public sealed class NpcBusinessLifecycleService
    {
        /// <summary>
        /// Weekly evaluation. Cash above the survival reserve is healthy;
        /// below it the business struggles and responds. Two consecutive
        /// healthy weeks bring a struggling business back to Operating.
        /// </summary>
        public void EvaluateWeek(
            NpcBusinessLifeRecord record,
            int survivalReserveCents,
            int customerSeekingSpendCents,
            NpcLifecycleContext context,
            NpcBusinessBorrowContext borrowContext,
            NpcBorrowerProfile borrowerProfile,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (record == null || context == null) return;
            if (record.State == NpcBusinessLifeState.Closed
                || record.State == NpcBusinessLifeState.Closing
                || record.State == NpcBusinessLifeState.Acquired)
            {
                return;
            }

            int day = context.DayIndex;
            int cash = context.Cash != null ? context.Cash.ReadCashCents(record.BusinessInstanceId) : 0;
            record.LastEvaluatedDayIndex = day;

            if (cash >= Math.Max(0, survivalReserveCents))
            {
                record.ConsecutiveHealthyWeeks++;
                record.ConsecutiveStruggleWeeks = 0;
                if (record.State == NpcBusinessLifeState.Struggling && record.ConsecutiveHealthyWeeks >= 2)
                {
                    record.State = NpcBusinessLifeState.Operating;
                    diagnostics.Add($"{record.BusinessInstanceId} recovered: {record.ConsecutiveHealthyWeeks} healthy weeks, back to Operating.");
                    context.Events?.Record(day, NpcBusinessEventKind.LifeEvaluated, record.FounderPersonId,
                        record.BusinessInstanceId,
                        $"{record.BusinessInstanceId} recovered from its struggles — {cash}c cash, operating again.");
                }
                else
                {
                    context.Events?.Record(day, NpcBusinessEventKind.LifeEvaluated, record.FounderPersonId,
                        record.BusinessInstanceId,
                        $"{record.BusinessInstanceId} healthy week: {cash}c cash against {survivalReserveCents}c reserve.");
                }

                if (record.State == NpcBusinessLifeState.Pivoting && record.ConsecutiveHealthyWeeks >= 1)
                {
                    record.State = NpcBusinessLifeState.Operating;
                    context.Events?.Record(day, NpcBusinessEventKind.PivotComplete, record.FounderPersonId,
                        record.BusinessInstanceId,
                        $"{record.BusinessInstanceId} pivot complete — trading on its new footing with {cash}c cash.");
                }

                return;
            }

            // Below the reserve: the business struggles. Real responses, in order.
            record.ConsecutiveStruggleWeeks++;
            record.ConsecutiveHealthyWeeks = 0;
            if (record.State != NpcBusinessLifeState.Struggling)
            {
                record.State = NpcBusinessLifeState.Struggling;
                diagnostics.Add($"{record.BusinessInstanceId} is STRUGGLING: {cash}c cash below {survivalReserveCents}c reserve.");
            }

            context.Events?.Record(day, NpcBusinessEventKind.LifeEvaluated, record.FounderPersonId,
                record.BusinessInstanceId,
                $"{record.BusinessInstanceId} struggling: {cash}c cash below {survivalReserveCents}c reserve " +
                $"(week {record.ConsecutiveStruggleWeeks}).");

            // Response 1: cut costs — recorded as the owner's real decision.
            context.Events?.Record(day, NpcBusinessEventKind.StruggleResponse, record.FounderPersonId,
                record.BusinessInstanceId,
                $"{record.BusinessInstanceId} response: cut discretionary spend — the owner trims orders and defers maintenance.");
            diagnostics.Add($"{record.BusinessInstanceId}: cut costs (owner decision, recorded).");

            // Response 2: borrow through the real NPC credit loop.
            if (borrowContext != null && borrowerProfile != null)
            {
                BorrowForBusiness(record, borrowContext, borrowerProfile, day, context, diagnostics);
            }

            // Response 3: seek customers with real money.
            int seekSpend = Math.Max(0, customerSeekingSpendCents);
            if (seekSpend > 0 && context.Cash != null)
            {
                int cashNow = context.Cash.ReadCashCents(record.BusinessInstanceId);
                if (cashNow >= seekSpend)
                {
                    string refusal = context.Cash.Spend(record.BusinessInstanceId, day, seekSpend,
                        "customer-seeking (advertising/notices)");
                    if (refusal == null)
                    {
                        context.Events?.Record(day, NpcBusinessEventKind.StruggleResponse, record.FounderPersonId,
                            record.BusinessInstanceId,
                            $"{record.BusinessInstanceId} response: spent {seekSpend}c seeking customers (advertising).");
                        diagnostics.Add($"{record.BusinessInstanceId}: spent {seekSpend}c seeking customers.");
                    }
                    else
                    {
                        diagnostics.Add($"{record.BusinessInstanceId}: customer-seeking spend refused: {refusal}");
                    }
                }
            }
        }

        /// <summary>
        /// Borrows for a struggling business through the real NPC credit
        /// loop (Phase E): request, evaluation, negotiation, conserved cash
        /// movement, obligation on the shared authority.
        /// </summary>
        public NpcCreditLoopResult BorrowForBusiness(
            NpcBusinessLifeRecord record,
            NpcBusinessBorrowContext borrowContext,
            NpcBorrowerProfile borrowerProfile,
            int dayIndex,
            NpcLifecycleContext context,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var empty = new NpcCreditLoopResult();
            if (record == null || borrowContext == null || borrowerProfile == null || context == null)
            {
                diagnostics.Add("Borrow refused: missing record, borrow context, or borrower profile.");
                return empty;
            }

            if (borrowContext.CreditEngine == null || borrowContext.Lender == null
                || borrowContext.CashBridge == null || context.Ids == null)
            {
                diagnostics.Add("Borrow refused: credit engine, lender, cash bridge and ids are all required.");
                return empty;
            }

            // The business borrows as itself: "business:<instanceId>".
            string borrowerName = "business:" + record.BusinessInstanceId;
            borrowerProfile.Name = borrowerName;
            var store = new NpcBusinessCashStore(record.BusinessInstanceId, context.Cash);
            string registerRefusal = borrowContext.CashBridge.Register(borrowerName, store);
            if (registerRefusal != null)
            {
                // Already registered from an earlier borrow — reuse the store.
                diagnostics.Add($"Borrow note: cash store already registered ({registerRefusal}); reusing.");
            }

            context.Events?.Record(dayIndex, NpcBusinessEventKind.BorrowInitiated, record.FounderPersonId,
                record.BusinessInstanceId,
                $"{record.BusinessInstanceId} seeks {borrowerProfile.DesiredAmountCents}c from '{borrowContext.Lender.Name}' " +
                $"through the NPC credit loop ('{borrowerProfile.Purpose}').");

            // The credit engine records into its own CreditEventLog; the
            // outcome is mirrored into the NPC business event log below.
            // simulateThroughDay == dayIndex: the advance is what matters
            // here; repayment plays out in later weekly evaluations.
            NpcCreditLoopResult result = borrowContext.CreditEngine.RunFullLoop(
                context.Ids, borrowContext.Workflow, context.Obligations, borrowContext.CashBridge,
                borrowContext.Workout, borrowContext.Registry, borrowContext.Foreclosure,
                borrowContext.Titles, borrowerProfile, borrowContext.Lender,
                dayIndex, dayIndex, borrowContext.CreditEvents, diagnostics);
            if (result.Closed && !string.IsNullOrWhiteSpace(result.ObligationId))
            {
                record.OpenObligationIds.Add(result.ObligationId);
                context.Events?.Record(dayIndex, NpcBusinessEventKind.BorrowInitiated, record.FounderPersonId,
                    record.BusinessInstanceId,
                    $"{record.BusinessInstanceId} borrowed {borrowerProfile.DesiredAmountCents}c from " +
                    $"'{borrowContext.Lender.Name}' — obligation '{result.ObligationId}', cash " +
                    $"{(result.CashConserved ? "conserved" : "NOT CONSERVED")}.");
                diagnostics.Add($"{record.BusinessInstanceId}: borrowed {borrowerProfile.DesiredAmountCents}c " +
                    $"(obligation '{result.ObligationId}', conserved: {result.CashConserved}).");
            }
            else
            {
                context.Events?.Record(dayIndex, NpcBusinessEventKind.BorrowInitiated, record.FounderPersonId,
                    record.BusinessInstanceId,
                    $"{record.BusinessInstanceId} borrowing failed: {string.Join("; ", result.Notes)}");
                diagnostics.Add($"{record.BusinessInstanceId}: borrowing failed — {string.Join("; ", result.Notes)}");
            }

            return result;
        }

        /// <summary>
        /// Changes direction: new capabilities at a real re-tooling cost.
        /// The cost leaves the business cash (conserved); the pivot only
        /// completes when the business trades healthily on the new footing.
        /// </summary>
        public string Pivot(
            NpcBusinessLifeRecord record,
            List<string> newCapabilityIds,
            int retoolingCostCents,
            string reason,
            NpcLifecycleContext context,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (record == null || context == null)
            {
                return "Pivot refused: no record or context.";
            }

            if (record.State == NpcBusinessLifeState.Closed || record.State == NpcBusinessLifeState.Closing)
            {
                return $"Pivot refused: {record.BusinessInstanceId} is {record.State}.";
            }

            if (newCapabilityIds == null || newCapabilityIds.Count == 0)
            {
                return "Pivot refused: no new capabilities named.";
            }

            int cost = Math.Max(0, retoolingCostCents);
            if (cost > 0)
            {
                if (context.Cash == null)
                {
                    return "Pivot refused: no cash port — re-tooling costs must be real.";
                }

                int cash = context.Cash.ReadCashCents(record.BusinessInstanceId);
                if (cash < cost)
                {
                    return $"Pivot refused: re-tooling costs {cost}c but {record.BusinessInstanceId} holds {cash}c.";
                }

                string refusal = context.Cash.Spend(record.BusinessInstanceId, context.DayIndex, cost,
                    $"pivot re-tooling: {reason}");
                if (refusal != null)
                {
                    return $"Pivot refused: re-tooling spend refused: {refusal}";
                }
            }

            record.CapabilityIds = new List<string>(newCapabilityIds);
            record.State = NpcBusinessLifeState.Pivoting;
            record.ConsecutiveHealthyWeeks = 0;
            diagnostics.Add($"{record.BusinessInstanceId} PIVOTED ({cost}c re-tooling): {reason}");
            context.Events?.Record(context.DayIndex, NpcBusinessEventKind.PivotStarted, record.FounderPersonId,
                record.BusinessInstanceId,
                $"{record.BusinessInstanceId} pivoted to [{string.Join(", ", newCapabilityIds)}] at a real " +
                $"re-tooling cost of {cost}c: {reason}");
            return null;
        }

        /// <summary>
        /// Orderly wind-down: settle every obligation from cash, liquidate
        /// assets to real buyers, release staff through the employment
        /// authorities, and return residual cash to the founder's
        /// household. Obligations that cannot be fully paid are EXPLICITLY
        /// assumed by the founder through the shared authority — never
        /// stranded, never silently deleted.
        /// </summary>
        public NpcBusinessCloseResult CloseOrderly(
            NpcBusinessLifeRecord record,
            string reason,
            NpcLifecycleContext context,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var result = new NpcBusinessCloseResult { BusinessInstanceId = record?.BusinessInstanceId ?? string.Empty };
            if (record == null || context == null)
            {
                result.Notes.Add("Close refused: no record or context.");
                return result;
            }

            if (record.State == NpcBusinessLifeState.Closed)
            {
                result.Notes.Add($"{record.BusinessInstanceId} is already closed.");
                result.Completed = true;
                return result;
            }

            int day = context.DayIndex;
            record.State = NpcBusinessLifeState.Closing;
            record.CloseReason = reason ?? string.Empty;
            context.Events?.Record(day, NpcBusinessEventKind.CloseStarted, record.FounderPersonId,
                record.BusinessInstanceId,
                $"{record.BusinessInstanceId} closing in an orderly wind-down: {record.CloseReason}");

            string debtorName = "business:" + record.BusinessInstanceId;

            // Step 1: settle obligations from real cash, in full where possible.
            if (context.Obligations != null && record.OpenObligationIds != null)
            {
                foreach (string obligationId in new List<string>(record.OpenObligationIds))
                {
                    FinancialObligation obligation = context.Obligations.Find(obligationId);
                    if (obligation == null)
                    {
                        result.Notes.Add($"obligation '{obligationId}' not found on the authority — recorded, not skipped.");
                        continue;
                    }

                    if (obligation.Settled)
                    {
                        result.ObligationsSettled.Add(obligationId);
                        continue;
                    }

                    int due = obligation.TotalOutstandingCents;
                    int cash = context.Cash != null ? context.Cash.ReadCashCents(record.BusinessInstanceId) : 0;
                    int payment = Math.Min(cash, due);
                    if (payment > 0)
                    {
                        string spendRefusal = context.Cash.Spend(record.BusinessInstanceId, day, payment,
                            $"close-down settlement of obligation '{obligationId}'");
                        if (spendRefusal == null)
                        {
                            FinancialPaymentRecord paymentRecord = context.Obligations.ApplyPayment(
                                obligationId, payment, day, debtorName, obligation.Creditor,
                                $"close-down:{record.BusinessInstanceId}");
                            if (paymentRecord != null)
                            {
                                result.CashPaidToCreditorsCents += payment;
                                context.Events?.Record(day, NpcBusinessEventKind.ObligationSettled,
                                    record.FounderPersonId, record.BusinessInstanceId,
                                    $"close-down: paid {payment}c on obligation '{obligationId}' to '{obligation.Creditor}'.");
                            }
                        }
                        else
                        {
                            result.Notes.Add($"settlement spend refused: {spendRefusal}");
                        }
                    }

                    // Anything still outstanding is EXPLICITLY assumed by the
                    // founder — the obligation survives on the authority with
                    // the founder liable. Never stranded.
                    FinancialObligation after = context.Obligations.Find(obligationId);
                    if (after != null && !after.Settled)
                    {
                        string founderName = !string.IsNullOrWhiteSpace(record.OwnerName)
                            ? record.OwnerName : $"person:{record.FounderPersonId}";
                        ObligationAssumptionRecord assumption = context.Obligations.RecordAssumption(
                            context.Ids, obligationId, founderName,
                            releaseOriginal: false, novation: false, creditorConsented: true, dayIndex: day);
                        if (assumption != null)
                        {
                            result.ObligationsAssumed.Add(obligationId);
                            context.Events?.Record(day, NpcBusinessEventKind.ObligationSettled,
                                record.FounderPersonId, record.BusinessInstanceId,
                                $"close-down: obligation '{obligationId}' ({after.TotalOutstandingCents}c outstanding) " +
                                $"explicitly assumed by founder '{founderName}' (assumption '{assumption.AssumptionId}') — " +
                                "the debt survives; nothing is stranded.");
                        }
                        else
                        {
                            result.Notes.Add($"obligation '{obligationId}' still open and assumption failed — FLAGGED, not dropped.");
                        }
                    }
                    else
                    {
                        result.ObligationsSettled.Add(obligationId);
                    }
                }
            }

            // Step 2: liquidate assets to real buyers.
            if (context.AssetRegister != null && context.Liquidation != null)
            {
                foreach (NpcFormationAsset asset in context.AssetRegister.Assets)
                {
                    if (asset == null || asset.Liquidated) continue;
                    string refusal = context.Liquidation.Liquidate(record.BusinessInstanceId, asset, day, out NpcLiquidationSale sale);
                    if (refusal == null && sale != null)
                    {
                        asset.Liquidated = true;
                        result.AssetsLiquidated++;
                        result.LiquidationProceedsCents += sale.ProceedsCents;
                        if (sale.ProceedsCents > 0 && context.Cash != null)
                        {
                            context.Cash.Receive(record.BusinessInstanceId, day, sale.ProceedsCents,
                                $"liquidation of {sale.Units}x '{sale.AssetItemId}' to {sale.BuyerName}");
                        }

                        context.Events?.Record(day, NpcBusinessEventKind.AssetLiquidated, record.FounderPersonId,
                            record.BusinessInstanceId,
                            $"close-down: liquidated {sale.Units}x '{sale.AssetItemId}' to {sale.BuyerName} " +
                            $"for {sale.ProceedsCents}c (was {asset.CostCents}c at formation).");
                    }
                    else
                    {
                        result.Notes.Add($"asset '{asset.ItemId}' liquidation failed: {refusal ?? "no sale"} — flagged, not written off.");
                    }
                }
            }

            // Step 3: release staff through the employment authorities.
            if (context.Employments != null && record.EmploymentIds != null)
            {
                foreach (string employmentId in record.EmploymentIds)
                {
                    if (context.Employments.TryGetById(employmentId, out EmploymentRelationship relationship)
                        && relationship != null)
                    {
                        relationship.LifecycleState = EmploymentLifecycleState.Ended;
                        relationship.EndDayIndex = day;
                        relationship.EndReason = EmploymentEndReason.EndedByAgreement;
                        result.StaffReleased++;
                        context.Events?.Record(day, NpcBusinessEventKind.StaffReleased, record.FounderPersonId,
                            record.BusinessInstanceId,
                            $"close-down: employment '{employmentId}' (P{relationship.EmployeePersonId}) ended by agreement.");
                    }
                }
            }

            // Step 4: residual cash returns to the founder's household — documented.
            int residual = context.Cash != null ? context.Cash.ReadCashCents(record.BusinessInstanceId) : 0;
            if (residual > 0)
            {
                string spendRefusal = context.Cash.Spend(record.BusinessInstanceId, day, residual,
                    "close-down residual to founder household");
                string inflowRefusal = null;
                if (spendRefusal == null && context.FounderHouseholdLedger != null)
                {
                    inflowRefusal = context.FounderHouseholdLedger.RecordInflow(day, residual,
                        HouseholdIncomeSource.OwnerDraw, "close-down:" + record.BusinessInstanceId,
                        $"residual from orderly close-down of '{record.BusinessInstanceId}'", record.OwnerName);
                }

                if (spendRefusal == null && inflowRefusal == null)
                {
                    result.ResidualToFounderCents = residual;
                    result.Notes.Add($"residual {residual}c returned to the founder's household as an owner draw.");
                }
                else
                {
                    result.Notes.Add($"residual {residual}c could not be returned cleanly: {spendRefusal ?? inflowRefusal} — flagged.");
                }
            }

            record.State = NpcBusinessLifeState.Closed;
            result.Completed = true;
            context.Events?.Record(day, NpcBusinessEventKind.CloseComplete, record.FounderPersonId,
                record.BusinessInstanceId,
                $"{record.BusinessInstanceId} CLOSED in an orderly wind-down: {result.ObligationsSettled.Count} obligation(s) settled, " +
                $"{result.ObligationsAssumed.Count} assumed by the founder, {result.AssetsLiquidated} asset(s) liquidated " +
                $"for {result.LiquidationProceedsCents}c, {result.StaffReleased} staff released, {result.ResidualToFounderCents}c residual returned. " +
                "No debts stranded.");
            diagnostics.Add($"{record.BusinessInstanceId} closed: settled {result.ObligationsSettled.Count}, " +
                $"assumed {result.ObligationsAssumed.Count}, liquidated {result.AssetsLiquidated}, staff released {result.StaffReleased}.");
            return result;
        }

        /// <summary>
        /// Real acquisition of an NPC business: the buyer pays the seller
        /// real cash (conserved), open obligations are assumed by the buyer
        /// through the shared authority (creditor-consented), and ownership
        /// transfers. Nothing is duplicated or destroyed silently.
        /// </summary>
        public NpcBusinessAcquisitionResult AcquireBusiness(
            NpcBusinessLifeRecord record,
            int buyerPersonId,
            string buyerName,
            int buyerHouseholdId,
            IRealCashStore buyerCash,
            HouseholdLedger sellerHouseholdLedger,
            int priceCents,
            bool assumeObligations,
            NpcLifecycleContext context,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var result = new NpcBusinessAcquisitionResult { BusinessInstanceId = record?.BusinessInstanceId ?? string.Empty };
            if (record == null || context == null || buyerCash == null || sellerHouseholdLedger == null)
            {
                result.Notes.Add("Acquisition refused: record, context, buyer cash and seller ledger are all required.");
                return result;
            }

            if (record.State == NpcBusinessLifeState.Closed || record.State == NpcBusinessLifeState.Closing)
            {
                result.Notes.Add($"Acquisition refused: {record.BusinessInstanceId} is {record.State}.");
                return result;
            }

            int day = context.DayIndex;
            int price = Math.Max(0, priceCents);
            if (buyerCash.ReadBalanceCents() < price)
            {
                result.Notes.Add($"Acquisition refused: buyer holds {buyerCash.ReadBalanceCents()}c, price is {price}c.");
                return result;
            }

            // Cash moves buyer -> seller, conserved, through real stores.
            string debitRefusal = buyerCash.DebitCents(day, price,
                $"acquisition of '{record.BusinessInstanceId}'", record.OwnerName);
            if (debitRefusal != null)
            {
                result.Notes.Add($"Acquisition refused: buyer debit refused: {debitRefusal}");
                return result;
            }

            string creditRefusal = sellerHouseholdLedger.RecordInflow(day, price,
                HouseholdIncomeSource.SaleProceeds, "acquisition:" + record.BusinessInstanceId,
                $"sale of business '{record.BusinessInstanceId}' to '{buyerName}'", buyerName);
            if (creditRefusal != null)
            {
                // Unwind the debit — no partial state.
                buyerCash.CreditCents(day, price, nameof(HouseholdIncomeSource.OtherDocumented),
                    "acquisition-abort", $"acquisition of '{record.BusinessInstanceId}' aborted — price returned", record.OwnerName);
                result.Notes.Add($"Acquisition aborted: seller credit refused ({creditRefusal}); buyer refunded.");
                return result;
            }

            result.PriceCents = price;

            // Obligations: the buyer assumes each open one through the shared
            // authority — the debts survive under the new owner.
            if (assumeObligations && context.Obligations != null && record.OpenObligationIds != null)
            {
                foreach (string obligationId in new List<string>(record.OpenObligationIds))
                {
                    FinancialObligation obligation = context.Obligations.Find(obligationId);
                    if (obligation == null || obligation.Settled) continue;
                    ObligationAssumptionRecord assumption = context.Obligations.RecordAssumption(
                        context.Ids, obligationId, buyerName,
                        releaseOriginal: true, novation: true, creditorConsented: true, dayIndex: day);
                    if (assumption != null)
                    {
                        result.AssumedObligationIds.Add(obligationId);
                    }
                    else
                    {
                        result.Notes.Add($"obligation '{obligationId}' assumption failed — flagged, debt NOT silently dropped.");
                    }
                }
            }

            string previousOwner = record.OwnerName;
            record.OwnerName = buyerName ?? string.Empty;
            record.FounderPersonId = buyerPersonId;
            record.State = NpcBusinessLifeState.Acquired;
            result.Completed = true;
            context.Events?.Record(day, NpcBusinessEventKind.BusinessAcquired, buyerPersonId,
                record.BusinessInstanceId,
                $"'{record.BusinessInstanceId}' ACQUIRED by '{buyerName}' (household {buyerHouseholdId}) from " +
                $"'{previousOwner}' for {price}c — cash conserved; {result.AssumedObligationIds.Count} obligation(s) " +
                "assumed by the buyer through the shared authority.");
            diagnostics.Add($"'{record.BusinessInstanceId}' acquired by '{buyerName}' for {price}c; " +
                $"{result.AssumedObligationIds.Count} obligation(s) assumed.");
            return result;
        }
    }

    /// <summary>Phase G (G4): the outcome of an orderly close-down.</summary>
    [Serializable]
    public sealed class NpcBusinessCloseResult
    {
        public string BusinessInstanceId = string.Empty;
        public bool Completed;
        public List<string> ObligationsSettled = new List<string>();
        public List<string> ObligationsAssumed = new List<string>();
        public int CashPaidToCreditorsCents;
        public int AssetsLiquidated;
        public int LiquidationProceedsCents;
        public int StaffReleased;
        public int ResidualToFounderCents;
        public List<string> Notes = new List<string>();

        public NpcBusinessCloseResult() { }
    }

    /// <summary>Phase G (G4): the outcome of an NPC business acquisition.</summary>
    [Serializable]
    public sealed class NpcBusinessAcquisitionResult
    {
        public string BusinessInstanceId = string.Empty;
        public bool Completed;
        public int PriceCents;
        public List<string> AssumedObligationIds = new List<string>();
        public List<string> Notes = new List<string>();

        public NpcBusinessAcquisitionResult() { }
    }
}
