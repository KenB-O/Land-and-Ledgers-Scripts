// Series 2 Pass 9 (player binding + save adoption): the pure-C#
// callables that bind the Pass 4 player models to the production
// systems. Codex hooks these up from the day driver; this file
// contains no scene code, no bootstrapper edit, and no UI.
//
// Three bindings, kept separate because their cash rules differ:
//
// 1. Duty -> TaskAuthority (Series2PlayerDutyBinding). A duty
//    task on the Pass 4 roster becomes a real WorkTask, issued
//    and advanced through TaskAuthority, and completed or missed
//    in BOTH places together, so the roster and the authority
//    can never disagree about whether the player did the work.
//    The binding never registers a task definition: an unknown
//    definition is refused, never conjured. It never changes
//    NPC-mode semantics (duty is effective only when DutyOn,
//    i.e. requested AND NPC mode off; NPC mode on refuses
//    issuance, advancement, and offload, roster preserved), and
//    it never truncates an over-budget roster — the caller-
//    supplied daily budget is reported by the Pass 4 model, and
//    WorkTimeBudgetStore booking is the authority's own rule.
//    No sub-minute work: minutes flow through the authority's
//    whole-minute clamp, as before.
//    Offload is exactly one task to exactly one employee, with
//    no double-counting: the employee WorkTask is created and
//    assigned FIRST; only then does the Pass 4 roster move the
//    task to offloaded, and only then is the player's WorkTask
//    cancelled. If the employee issuance fails, the player is
//    untouched. If the player cancellation fails after the
//    offload (it should not, in a single-threaded driver), the
//    failure is diagnosed loudly — the roster and the missed-
//    work report no longer count it as player work, but the
//    authority task is still open and the driver must cancel
//    it, never pretend it vanished.
//    The binding accepts a caller-supplied IBusinessCashPort
//    and stores it, because callers that settle business
//    payments through the same driver (wholesale, carrier) must
//    use that port. Duty issuance, advancement, and offload
//    themselves move NO business cash, and the port is NEVER
//    used for household meals or nights — household cash is
//    HouseholdLedger only (see the auto-care binding). A null
//    port is allowed; it does
//    not block duty work, because duty work is person-time,
//    not a payment.
//
// 2. Auto-care -> HouseholdLedger (Series2AutoCareLedgerBinding).
//    Pass 4's StepEat/StepSleep pay through a model CashCents
//    gate, which the Pass 4 review doc itself calls test-only:
//    production payment must go through HouseholdLedger, and
//    credit only through FinancialObligationAuthority. This
//    binding is that replacement, leg by leg, with no skipped
//    legs: the driver still runs StepFindSeller/StepTravel (or
//    StepFindLodging/StepTravel) first; only at the seller or
//    lodging does PayAndEat/PayAndSleep run. It re-checks open /
//    has-food / available immediately before paying (the same
//    checks StepEat/StepSleep will re-check), then pays through
//    Series2AutoCarePayment, then syncs the model's CashCents
//    to the price so StepEat/StepSleep's internal gate passes
//    trivially — the model cash is then a mirror of a payment
//    that already happened in the ledger, never the payment
//    itself. If StepEat/StepSleep then fails anyway (it should
//    not, given the pre-checks on the same objects in the same
//    tick), a cash payment is refunded through the ledger as
//    an OtherDocumented inflow with a refund reason, diagnosed
//    loudly if even the refund fails; a credit obligation
//    cannot be un-created through ITradeCreditPort, so that
//    residual is diagnosed loudly for the driver to cancel
//    through the authority directly — the pre-checks exist
//    precisely so this path is unreachable in normal driving,
//    and it is documented here rather than hidden.
//    A 0c price moves no ledger entry (RecordOutflow refuses
//    non-positive amounts) and is recorded as a free meal/night
//    at a 0c seller — the seller's price, not a conjured free
//    good. Failure leaves a known state: before travel at the
//    origin, after travel at the seller/lodging, never
//    teleported home, never a free meal or night.
//
// 3. Missed work: NO second system. BuildMissedWorkMessages
//    and RenderMissedWorkConsole delegate to Pass 4's
//    BuildMissedWorkReport and Pass4PlayerMessages (which read
//    the Pass 2 report). Offloaded tasks are never missed work;
//    idle is reported as standing by, never as missed work.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>Result of one auto-care payment through the ledger/credit path.</summary>
    public sealed class Series2AutoCarePaymentResult
    {
        public bool Paid;
        public string Mode = string.Empty; // Cash, TradeCredit, Free, or empty on failure
        public string ObligationId = string.Empty;
        public string Refusal = string.Empty;
    }

    /// <summary>
    /// Series 2 Pass 9: household payment for one auto-care meal
    /// or night. Cash first (HouseholdLedger outflow, balance-
    /// checked by the ledger itself), then offered trade credit
    /// within limit through ITradeCreditPort (production: the
    /// TradeCreditAuthorityPort over FinancialObligationAuthority
    /// — the one debt authority, never invented debt). No ledger
    /// entry is ever made for a business payment, and no business
    /// port is ever used for a household payment.
    /// </summary>
    public static class Series2AutoCarePayment
    {
        public static Series2AutoCarePaymentResult Pay(
            HouseholdLedger ledger, int dayIndex, int priceCents,
            string purpose, string counterparty,
            bool offersTradeCredit, int tradeCreditLimitCents,
            ITradeCreditPort tradeCreditPort,
            string debtor, string creditor, string agreementId, string sourceEventId,
            List<string> diag)
        {
            var result = new Series2AutoCarePaymentResult();
            if (priceCents < 0)
            {
                result.Refusal = "Series2AutoCarePayment: negative price; nothing charged, nothing eaten or slept.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }

            if (priceCents == 0)
            {
                result.Paid = true;
                result.Mode = "Free";
                if (diag != null) diag.Add("Series2AutoCarePayment: 0c price; no ledger entry (the seller's price, not a conjured free good).");
                return result;
            }

            if (ledger == null)
            {
                result.Refusal = "Series2AutoCarePayment: no household ledger; nothing charged, nothing eaten or slept.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }

            if (ledger.GetBalanceCents() >= priceCents)
            {
                string problem = ledger.RecordOutflow(dayIndex, priceCents, purpose ?? "auto-care", counterparty ?? string.Empty);
                if (problem == null)
                {
                    result.Paid = true;
                    result.Mode = "Cash";
                    return result;
                }

                result.Refusal = $"Series2AutoCarePayment: household ledger refused a {priceCents}c outflow ({problem}); nothing eaten or slept.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }

            if (offersTradeCredit && priceCents <= tradeCreditLimitCents && tradeCreditPort != null)
            {
                string obligationId = tradeCreditPort.CreateTradeCredit(
                    debtor ?? string.Empty, creditor ?? string.Empty, priceCents, dayIndex,
                    string.Empty, purpose ?? "auto-care", agreementId ?? string.Empty, sourceEventId ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(obligationId))
                {
                    result.Paid = true;
                    result.Mode = "TradeCredit";
                    result.ObligationId = obligationId;
                    return result;
                }

                result.Refusal = "Series2AutoCarePayment: trade-credit port returned no obligation; no debt invented, nothing eaten or slept.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }

            result.Refusal = $"Series2AutoCarePayment: cannot pay {priceCents}c (household balance {ledger.GetBalanceCents()}c, no sufficient offered trade credit); nothing eaten or slept, no debt invented.";
            if (diag != null) diag.Add(result.Refusal);
            return result;
        }

        /// <summary>
        /// Refund of a cash auto-care payment that was followed by
        /// a failed StepEat/StepSleep. Recorded as an
        /// OtherDocumented inflow with a refund reason and the
        /// original counterparty — a reversal of money that just
        /// moved, never new cash. Returns null on success.
        /// </summary>
        public static string RefundCash(
            HouseholdLedger ledger, int dayIndex, int priceCents, string counterparty, List<string> diag)
        {
            if (ledger == null || priceCents <= 0) return null;
            string problem = ledger.RecordInflow(
                dayIndex, priceCents, HouseholdIncomeSource.OtherDocumented,
                "auto-care-refund", "refund of failed auto-care payment", counterparty ?? string.Empty);
            if (problem != null && diag != null)
                diag.Add($"Series2AutoCarePayment: REFUND FAILED for {priceCents}c ({problem}) — money moved but no meal/night happened; driver must reconcile, never hide this.");
            return problem;
        }
    }

    /// <summary>
    /// Series 2 Pass 9: duty roster -> TaskAuthority binding.
    /// See the file header for the ordering guarantees and the
    /// cash-port separation. Budgets may be null (the authority
    /// then books no budget); production Codex supplies the real
    /// WorkTimeBudgetStore, and a null store is reported by the
    /// caller, never presented as budget enforcement.
    /// </summary>
    public sealed class Series2PlayerDutyBinding
    {
        private readonly Pass4PlayerDuty duty;
        private readonly TaskAuthority authority;
        private readonly EntityId playerWorker;
        private readonly WorkTimeBudgetStore budgets;
        private readonly IBusinessCashPort businessCashPort;
        private readonly Dictionary<string, EntityId> issuedTasks = new Dictionary<string, EntityId>(StringComparer.Ordinal);

        public Series2PlayerDutyBinding(
            Pass4PlayerDuty duty, TaskAuthority authority, EntityId playerWorker,
            WorkTimeBudgetStore budgets, IBusinessCashPort businessCashPort)
        {
            this.duty = duty;
            this.authority = authority;
            this.playerWorker = playerWorker;
            this.budgets = budgets;
            this.businessCashPort = businessCashPort;
        }

        public Pass4PlayerDuty Duty => duty;

        private Pass4DutyTask FindRosterTask(string taskDefinitionId)
        {
            if (duty == null) return null;
            foreach (Pass4DutyTask t in duty.Tasks)
            {
                if (t != null && string.Equals(t.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !t.Completed)
                    return t;
            }

            return null;
        }

        /// <summary>
        /// Issues one unfinished roster task through TaskAuthority:
        /// refuses when duty is not effective, when the task is
        /// not on the roster, or when the authority has no such
        /// definition. Reuses an already-open authority task for
        /// the same definition instead of creating a duplicate.
        /// </summary>
        public bool IssueDutyTask(string taskDefinitionId, int dayIndex, List<string> diag)
        {
            if (duty == null || authority == null)
            {
                if (diag != null) diag.Add("Series2PlayerDutyBinding: no duty model or no TaskAuthority; nothing issued.");
                return false;
            }

            if (!duty.DutyOn)
            {
                if (diag != null) diag.Add("Series2PlayerDutyBinding: duty is not effective (toggle off or NPC mode on); nothing issued, roster preserved.");
                return false;
            }

            Pass4DutyTask rosterTask = FindRosterTask(taskDefinitionId);
            if (rosterTask == null)
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: duty task '{taskDefinitionId}' is not an unfinished roster task; nothing issued.");
                return false;
            }

            if (authority.GetDefinition(taskDefinitionId) == null)
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: TaskAuthority has no definition '{taskDefinitionId}'; nothing issued, never conjured.");
                return false;
            }

            if (issuedTasks.TryGetValue(taskDefinitionId, out EntityId existingId))
            {
                WorkTask existing = authority.FindTask(existingId);
                if (existing != null && existing.IsOpen)
                {
                    if (diag != null) diag.Add($"Series2PlayerDutyBinding: duty task '{taskDefinitionId}' is already issued through TaskAuthority; reused, not duplicated.");
                    return true;
                }

                issuedTasks.Remove(taskDefinitionId);
            }

            // Dedupe across the player's whole open queue (not
            // just assigned tasks): a task left queued by a failed
            // Assign must be adopted, never duplicated by a second
            // creation on re-issue.
            foreach (WorkTask open in authority.GetOpenTasks(playerWorker))
            {
                if (open != null && open.IsOpen && string.Equals(open.DefinitionId, taskDefinitionId, StringComparison.Ordinal))
                {
                    issuedTasks[taskDefinitionId] = open.TaskId;
                    if (diag != null) diag.Add($"Series2PlayerDutyBinding: duty task '{taskDefinitionId}' already had an open authority task; adopted, not duplicated.");
                    return true;
                }
            }

            WorkTask created;
            try
            {
                created = authority.CreateTaskWithPlannedMinutes(taskDefinitionId, playerWorker, dayIndex, rosterTask.PlannedMinutes);
            }
            catch (Exception ex)
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: TaskAuthority refused to create '{taskDefinitionId}' ({ex.Message}); nothing issued.");
                return false;
            }

            string rejection;
            if (!authority.AssignTask(created.TaskId, playerWorker, budgets, out rejection))
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: TaskAuthority refused to assign '{taskDefinitionId}' ({rejection}); the queued task stays queued, the roster is untouched.");
                return false;
            }

            issuedTasks[taskDefinitionId] = created.TaskId;
            return true;
        }

        /// <summary>
        /// Starts an issued duty task in both places: the
        /// authority first (its status gate), then the roster.
        /// Refuses when duty is not effective or the task was
        /// never issued.
        /// </summary>
        public bool StartDutyTask(string taskDefinitionId, int dayIndex, List<string> diag)
        {
            if (duty == null || authority == null || !duty.DutyOn)
            {
                if (diag != null) diag.Add("Series2PlayerDutyBinding: cannot start a duty task while duty is not effective; nothing started.");
                return false;
            }

            if (!issuedTasks.TryGetValue(taskDefinitionId, out EntityId taskId))
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: duty task '{taskDefinitionId}' was never issued; issue it first, never start blind.");
                return false;
            }

            string rejection;
            if (!authority.StartTask(taskId, dayIndex, out rejection))
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: TaskAuthority refused to start '{taskDefinitionId}' ({rejection}); roster untouched.");
                return false;
            }

            if (!duty.StartDutyTask(taskDefinitionId, diag))
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: roster refused to start '{taskDefinitionId}' after the authority started it; driver must cancel the authority task — the two places disagree, and that disagreement is diagnosed, never hidden.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Records work in both places. When the authority
        /// reaches completion, the roster task completes too and
        /// the issued mapping is dropped. Minutes flow through
        /// the authority's whole-minute clamp.
        /// </summary>
        public bool AdvanceDutyTask(string taskDefinitionId, int minutes, int dayIndex, List<string> diag)
        {
            if (duty == null || authority == null || !duty.DutyOn)
            {
                if (diag != null) diag.Add("Series2PlayerDutyBinding: cannot advance a duty task while duty is not effective; nothing advanced.");
                return false;
            }

            if (!issuedTasks.TryGetValue(taskDefinitionId, out EntityId taskId))
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: duty task '{taskDefinitionId}' was never issued; issue and start it first.");
                return false;
            }

            bool reachedCompletion;
            string rejection;
            if (!authority.RecordWorkProgress(taskId, minutes, budgets, dayIndex, out reachedCompletion, out rejection))
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: TaskAuthority refused work on '{taskDefinitionId}' ({rejection}); roster untouched.");
                return false;
            }

            if (reachedCompletion)
            {
                authority.CompleteTask(taskId, TaskOutcome.Success, dayIndex, budgets);
                duty.CompleteDutyTask(taskDefinitionId, diag);
                issuedTasks.Remove(taskDefinitionId);
            }

            return true;
        }

        /// <summary>
        /// Marks an issued duty task missed in both places: the
        /// authority task is cancelled, the roster task carries
        /// the missed reason, and the Pass 2 report (via
        /// BuildMissedWorkReport) is the single source that says
        /// so. A task that was never issued is missed in the
        /// roster only, diagnosed as such.
        /// </summary>
        public bool MissDutyTask(string taskDefinitionId, string reason, List<string> diag)
        {
            if (duty == null)
            {
                if (diag != null) diag.Add("Series2PlayerDutyBinding: no duty model; nothing missed.");
                return false;
            }

            if (authority != null && issuedTasks.TryGetValue(taskDefinitionId, out EntityId taskId))
            {
                authority.CancelTask(taskId, budgets);
                issuedTasks.Remove(taskDefinitionId);
            }
            else if (diag != null)
            {
                diag.Add($"Series2PlayerDutyBinding: duty task '{taskDefinitionId}' had no issued authority task; missed in the roster only.");
            }

            return duty.MissDutyTask(taskDefinitionId, reason, diag);
        }

        /// <summary>
        /// Offloads exactly one unfinished roster task to exactly
        /// one employee, without double-counting. Employee
        /// issuance first; roster offload second; player
        /// cancellation third (see the file header for the
        /// residual if that third step fails). No headcount
        /// argument, no quota: the evidence string is required
        /// by the Pass 4 offload itself.
        /// </summary>
        public bool OffloadDutyTask(
            string taskDefinitionId, string employeeId, EntityId employeeWorker,
            string evidence, int dayIndex, List<string> diag)
        {
            if (duty == null || authority == null)
            {
                if (diag != null) diag.Add("Series2PlayerDutyBinding: no duty model or no TaskAuthority; nothing offloaded.");
                return false;
            }

            if (!duty.DutyOn)
            {
                if (diag != null) diag.Add("Series2PlayerDutyBinding: cannot offload while duty is not effective (toggle off or NPC mode on); roster preserved.");
                return false;
            }

            Pass4DutyTask rosterTask = FindRosterTask(taskDefinitionId);
            if (rosterTask == null)
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: duty task '{taskDefinitionId}' is not an unfinished roster task; nothing offloaded.");
                return false;
            }

            if (authority.GetDefinition(taskDefinitionId) == null)
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: TaskAuthority has no definition '{taskDefinitionId}'; employee issuance refused, player untouched.");
                return false;
            }

            WorkTask employeeTask;
            try
            {
                employeeTask = authority.CreateTaskWithPlannedMinutes(taskDefinitionId, employeeWorker, dayIndex, rosterTask.PlannedMinutes);
            }
            catch (Exception ex)
            {
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: employee task for '{taskDefinitionId}' could not be created ({ex.Message}); player untouched.");
                return false;
            }

            string rejection;
            if (!authority.AssignTask(employeeTask.TaskId, employeeWorker, budgets, out rejection))
            {
                authority.CancelTask(employeeTask.TaskId, budgets);
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: employee assignment for '{taskDefinitionId}' refused ({rejection}); queued employee task cancelled, player untouched.");
                return false;
            }

            if (!duty.OffloadTask(taskDefinitionId, employeeId, evidence, diag))
            {
                authority.CancelTask(employeeTask.TaskId, budgets);
                if (diag != null) diag.Add($"Series2PlayerDutyBinding: roster offload for '{taskDefinitionId}' refused after employee issuance; employee task cancelled, player untouched.");
                return false;
            }

            if (issuedTasks.TryGetValue(taskDefinitionId, out EntityId playerTaskId))
            {
                if (!authority.CancelTask(playerTaskId, budgets) && diag != null)
                    diag.Add($"Series2PlayerDutyBinding: RESIDUAL: player authority task for offloaded '{taskDefinitionId}' could not be cancelled; the roster and report no longer count it as player work, but the driver must cancel that authority task directly.");
                issuedTasks.Remove(taskDefinitionId);
            }

            return true;
        }

        /// <summary>Missed-work messages from the single source of truth (Pass 2 report via Pass 4). No second system.</summary>
        public List<Pass4PlayerMessage> BuildMissedWorkMessages(int idleMinutes)
        {
            if (duty == null) return new List<Pass4PlayerMessage>();
            Pass2MissedWorkReport report = duty.BuildMissedWorkReport(idleMinutes);
            return Pass4PlayerMessages.BuildFromReport(report, "player");
        }

        /// <summary>Console rendering of the missed-work messages. On-screen UI is Codex work reading the message list.</summary>
        public string RenderMissedWorkConsole(int idleMinutes)
        {
            return Pass4PlayerMessages.RenderConsoleLines(BuildMissedWorkMessages(idleMinutes), "Player missed work");
        }
    }

    /// <summary>
    /// Series 2 Pass 9: auto-care through the household ledger.
    /// The driver still runs the Find/Travel legs first; these
    /// methods run only the payment leg, at the seller/lodging.
    /// See the file header for the refund residual on the credit
    /// path and for why a 0c price moves no ledger entry.
    /// </summary>
    public sealed class Series2AutoCareLedgerBinding
    {
        public bool PayAndEat(
            Pass4AutoEat autoEat, IPass4FoodSeller seller,
            HouseholdLedger ledger, int dayIndex,
            string debtor, string creditor, ITradeCreditPort tradeCreditPort,
            List<string> diag)
        {
            if (autoEat == null || seller == null)
            {
                if (diag != null) diag.Add("Series2AutoCareLedgerBinding: no auto-eat model or no seller; nothing paid, nothing eaten.");
                return false;
            }

            if (autoEat.State != Pass4AutoEatState.AtSeller)
            {
                if (diag != null) diag.Add("Series2AutoCareLedgerBinding: auto-eat is not at a seller; run FindSeller and Travel first, never skip legs.");
                return false;
            }

            if (!seller.IsOpen)
            {
                if (diag != null) diag.Add("Series2AutoCareLedgerBinding: food seller is closed; nothing paid, player stays at the seller.");
                return false;
            }

            if (!seller.HasFood)
            {
                if (diag != null) diag.Add("Series2AutoCareLedgerBinding: food seller has no food; nothing paid, player stays at the seller.");
                return false;
            }

            int price = seller.FoodPriceCents;
            Series2AutoCarePaymentResult payment = Series2AutoCarePayment.Pay(
                ledger, dayIndex, price, "auto-eat meal", seller.LocationId,
                seller.OffersTradeCredit, seller.TradeCreditLimitCents, tradeCreditPort,
                debtor, creditor, string.Empty, string.Empty, diag);
            if (!payment.Paid)
            {
                return false;
            }

            // The model gate is now a mirror: the payment already
            // happened in the ledger (or on credit), so StepEat's
            // internal cash check passes trivially and cannot
            // move money a second time.
            autoEat.CashCents = price;
            if (!autoEat.StepEat(diag))
            {
                if (payment.Mode == "Cash")
                    Series2AutoCarePayment.RefundCash(ledger, dayIndex, price, seller.LocationId, diag);
                else if (payment.Mode == "TradeCredit" && diag != null)
                    diag.Add($"Series2AutoCareLedgerBinding: RESIDUAL: meal failed after trade-credit obligation '{payment.ObligationId}' was created; the obligation stands and must be cancelled through the debt authority directly — pre-checks exist so this path is unreachable in normal driving.");
                return false;
            }

            return true;
        }

        public bool PayAndSleep(
            Pass4AutoSleep autoSleep, IPass4Lodging lodging,
            HouseholdLedger ledger, int dayIndex,
            string debtor, string creditor, ITradeCreditPort tradeCreditPort,
            List<string> diag)
        {
            if (autoSleep == null || lodging == null)
            {
                if (diag != null) diag.Add("Series2AutoCareLedgerBinding: no auto-sleep model or no lodging; nothing paid, nothing slept.");
                return false;
            }

            if (autoSleep.State != Pass4AutoSleepState.AtLodging)
            {
                if (diag != null) diag.Add("Series2AutoCareLedgerBinding: auto-sleep is not at lodging; run FindLodging and Travel first, never skip legs.");
                return false;
            }

            if (!lodging.IsAvailable)
            {
                if (diag != null) diag.Add("Series2AutoCareLedgerBinding: lodging is not available; nothing paid, player stays at the lodging.");
                return false;
            }

            int price = lodging.NightPriceCents;
            Series2AutoCarePaymentResult payment = Series2AutoCarePayment.Pay(
                ledger, dayIndex, price, "auto-sleep night", lodging.LocationId,
                lodging.OffersTradeCredit, lodging.TradeCreditLimitCents, tradeCreditPort,
                debtor, creditor, string.Empty, string.Empty, diag);
            if (!payment.Paid)
            {
                return false;
            }

            autoSleep.CashCents = price;
            if (!autoSleep.StepSleep(diag))
            {
                if (payment.Mode == "Cash")
                    Series2AutoCarePayment.RefundCash(ledger, dayIndex, price, lodging.LocationId, diag);
                else if (payment.Mode == "TradeCredit" && diag != null)
                    diag.Add($"Series2AutoCareLedgerBinding: RESIDUAL: night failed after trade-credit obligation '{payment.ObligationId}' was created; the obligation stands and must be cancelled through the debt authority directly — pre-checks exist so this path is unreachable in normal driving.");
                return false;
            }

            return true;
        }
    }
}
