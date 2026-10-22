// Pass VII Workstream D: execution of ManagerBrain decisions through real
// production authorities.
//
// ManagerBrain decides (intents); this file EXECUTES. Every one of the seven
// ManagerActKinds has a real IManagerDecisionExecutor wired to a production
// authority:
//
//   OpenRecruitment  -> the existing recruitment path (RecruitmentService)
//   Schedule         -> the existing scheduler (WorkScheduler) + task start
//   Procure          -> GenericProcurementPlanner + GenericShipmentReceivingAuthority
//   PriceChange      -> GenericRetailProductLine.SetSellingPriceCents
//   OrderMaintenance -> TaskAuthority (real maintenance work orders)
//   Borrow           -> FinancialObligationAuthority (within delegated limits)
//   SellAsset        -> owner-approval pending request (owner-reserved)
//
// Rules honored throughout:
//   - Do Once, Manager Judgment, Owner Locked (checked by ManagerBrain; the
//     executors re-verify delegated scope before touching anything).
//   - A failed action never retries the same impossible act: the execution
//     ledger records every attempt key, so a repeat of the same decision in
//     the same day is refused instead of re-run.
//   - Retries are idempotent: no duplicate procurement orders, no duplicate
//     price changes, no double cash movement.
//   - A decision must execute, become a legitimate pending request, escalate,
//     or fail with a reason. Silent disappearance is not acceptable.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// One manager-executed (or attempted) action. The ActionKey is the
    /// idempotency key: kind + business + day + detail. A second attempt with
    /// the same key is refused, never re-executed.
    /// </summary>
    [Serializable]
    public sealed class ManagerExecutedActionRecord
    {
        public string ActionKey = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public int DayIndex = -1;
        public string Kind = string.Empty;
        public bool Succeeded;
        public string Reason = string.Empty;
        public string ResultingReference = string.Empty;
        public int ManagerPersonId = -1;
    }

    /// <summary>
    /// A manager decision that needs owner approval (borrowing escalations,
    /// owner-reserved asset sales). A legitimate pending state: visible,
    /// resolvable, never silently dropped, never duplicated.
    /// </summary>
    [Serializable]
    public sealed class ManagerPendingRequest
    {
        public string RequestId = string.Empty;
        public string RequestKey = string.Empty; // idempotency: kind+business+day+detail
        public string BusinessInstanceId = string.Empty;
        public string Kind = string.Empty; // ManagerActKinds.Borrow / SellAsset
        public int AmountCents;
        /// <summary>
        /// Pass IX WS5 (A3-08, coordinator decision 3): for SellAsset, the
        /// SPECIFIC asset the sale names (an EquipmentAsset id, or
        /// "formation:{ItemId}"), taken from the business's own asset
        /// registers at proposal time. The production appraisal appraises
        /// THIS asset and the execution step transfers THIS asset. Additive
        /// field; legacy requests carry none and can never be appraised.
        /// </summary>
        public string AssetId = string.Empty;
        public string Detail = string.Empty;
        public int CreatedDayIndex = -1;
        public int ManagerPersonId = -1;
        public string Status = string.Empty; // "Pending" | "Approved" | "Rejected"
        public string ResolutionNote = string.Empty;
        public int ResolvedDayIndex = -1;
    }

    /// <summary>
    /// One manager-placed procurement shipment in transit. Delivery is a
    /// separate, idempotent step through GenericShipmentReceivingAuthority.
    /// </summary>
    [Serializable]
    public sealed class ManagerProcurementShipment
    {
        public string ShipmentId = string.Empty;
        public string OrderId = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public string SupplierBusinessId = string.Empty;
        public string ProductId = string.Empty;
        public float Quantity;
        public int UnitCostCents;
        public int TotalCents;
        public int CreatedDayIndex = -1;
        public int ExpectedArrivalDayIndex = -1;
        public string Status = string.Empty; // "InTransit" | "Delivered" | "Failed"
        public string FailureReason = string.Empty;
    }

    [Serializable]
    public sealed class ManagerDecisionExecutionSaveDto
    {
        public List<ManagerExecutedActionRecord> Actions = new List<ManagerExecutedActionRecord>();
        public List<ManagerPendingRequest> PendingRequests = new List<ManagerPendingRequest>();
        public List<ManagerProcurementShipment> Shipments = new List<ManagerProcurementShipment>();
    }

    /// <summary>
    /// The durable execution ledger: idempotency keys for executed actions,
    /// owner-approval pending requests, and in-transit procurement shipments.
    /// Save DTO is the authoritative snapshot; reload replaces, never appends.
    /// </summary>
    public sealed class ManagerDecisionExecutionLedger
    {
        private readonly List<ManagerExecutedActionRecord> actions = new List<ManagerExecutedActionRecord>();
        private readonly List<ManagerPendingRequest> pendingRequests = new List<ManagerPendingRequest>();
        private readonly List<ManagerProcurementShipment> shipments = new List<ManagerProcurementShipment>();

        // Pass IX WS4 (A4-15c): the in-transit subset of `shipments`, in ledger
        // insertion order. The full list remains the save truth and audit
        // trail (completed shipments are never removed from it); this index
        // lets the daily AdvanceShipments pass and HasInTransitShipment scale
        // with ACTIVE shipments instead of total campaign history. Membership
        // is maintained at the only points a shipment can become or cease to
        // be in transit: AddShipment, LoadFromSaveDto, and
        // MarkShipmentNoLongerInTransit (called by AdvanceShipments when it
        // transitions a shipment to Delivered/Failed).
        private readonly List<ManagerProcurementShipment> inTransitShipments = new List<ManagerProcurementShipment>();

        public IReadOnlyList<ManagerExecutedActionRecord> Actions => actions;
        public IReadOnlyList<ManagerPendingRequest> PendingRequests => pendingRequests;
        public IReadOnlyList<ManagerProcurementShipment> Shipments => shipments;

        /// <summary>Live view of the in-transit index, in ledger order.
        /// Callers that mutate shipment status while iterating must copy it.</summary>
        public IReadOnlyList<ManagerProcurementShipment> InTransitShipments => inTransitShipments;

        /// <summary>Removes a shipment from the in-transit index after its
        /// status has transitioned away from "InTransit". The shipment itself
        /// stays in the full ledger history.</summary>
        public void MarkShipmentNoLongerInTransit(ManagerProcurementShipment shipment)
        {
            if (shipment == null) return;
            inTransitShipments.Remove(shipment);
        }

        public bool HasExecuted(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey)) return false;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && string.Equals(actions[i].ActionKey, actionKey, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public void RecordAction(ManagerExecutedActionRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.ActionKey)) return;
            if (HasExecuted(record.ActionKey)) return;
            actions.Add(record);
        }

        public ManagerPendingRequest FindPending(string requestKey)
        {
            if (string.IsNullOrWhiteSpace(requestKey)) return null;
            for (int i = 0; i < pendingRequests.Count; i++)
            {
                ManagerPendingRequest request = pendingRequests[i];
                if (request != null && string.Equals(request.RequestKey, requestKey, StringComparison.Ordinal))
                    return request;
            }
            return null;
        }

        /// <summary>Adds a pending request; an identical pending request is
        /// never duplicated (first wins, still Pending).</summary>
        public ManagerPendingRequest AddPending(ManagerPendingRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestKey)) return null;
            ManagerPendingRequest existing = FindPending(request.RequestKey);
            if (existing != null) return existing;
            pendingRequests.Add(request);
            return request;
        }

        public bool HasInTransitShipment(string businessInstanceId, string productId)
        {
            for (int i = 0; i < inTransitShipments.Count; i++)
            {
                ManagerProcurementShipment shipment = inTransitShipments[i];
                if (shipment != null
                    && string.Equals(shipment.Status, "InTransit", StringComparison.Ordinal)
                    && string.Equals(shipment.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal)
                    && string.Equals(shipment.ProductId, productId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void AddShipment(ManagerProcurementShipment shipment)
        {
            if (shipment == null || string.IsNullOrWhiteSpace(shipment.ShipmentId)) return;
            for (int i = 0; i < shipments.Count; i++)
            {
                if (shipments[i] != null && string.Equals(shipments[i].ShipmentId, shipment.ShipmentId, StringComparison.Ordinal))
                    return;
            }
            shipments.Add(shipment);
            if (string.Equals(shipment.Status, "InTransit", StringComparison.Ordinal))
                inTransitShipments.Add(shipment);
        }

        public ManagerDecisionExecutionSaveDto CaptureSaveDto()
        {
            return new ManagerDecisionExecutionSaveDto
            {
                Actions = new List<ManagerExecutedActionRecord>(actions),
                PendingRequests = new List<ManagerPendingRequest>(pendingRequests),
                Shipments = new List<ManagerProcurementShipment>(shipments),
            };
        }

        public void LoadFromSaveDto(ManagerDecisionExecutionSaveDto dto)
        {
            actions.Clear();
            pendingRequests.Clear();
            shipments.Clear();
            inTransitShipments.Clear();
            if (dto == null) return;
            if (dto.Actions != null) actions.AddRange(dto.Actions);
            if (dto.PendingRequests != null) pendingRequests.AddRange(dto.PendingRequests);
            if (dto.Shipments != null)
            {
                shipments.AddRange(dto.Shipments);
                for (int i = 0; i < shipments.Count; i++)
                {
                    ManagerProcurementShipment shipment = shipments[i];
                    if (shipment != null && string.Equals(shipment.Status, "InTransit", StringComparison.Ordinal))
                        inTransitShipments.Add(shipment);
                }
            }
        }
    }

    /// <summary>
    /// A real business lender for manager-authorized borrowing. The lender has
    /// finite capital; a refused or unfunded loan returns null with a reason.
    /// No phantom money: funding must move real cash.
    /// </summary>
    public interface IManagerBusinessLender
    {
        string LenderName { get; }
        FinancialObligation TryArrangeLoan(
            EntityIdRegistry ids,
            FinancialObligationAuthority obligations,
            string debtorBusinessId,
            int amountCents,
            int dayIndex,
            List<string> diagnostics,
            out string refusalReason);
    }

    /// <summary>
    /// The production authorities and lookups one manager-decision execution
    /// needs. Plain data holder; the router owns the ledger.
    /// </summary>
    public sealed class ManagerExecutionServices
    {
        public Func<string, BusinessInstanceState> FindBusiness;
        public Func<IEnumerable<BusinessInstanceState>> AllBusinesses;
        public Func<string, LaborDemandReport> DemandFor;
        public Func<string, EntityId> BusinessEntityFor;
        public Func<string, EquipmentAsset> FindAsset;
        public TaskAuthority Tasks;
        public WorkScheduler WorkScheduling;
        public PersonScheduleTracker PersonSchedules;
        public WorkTimeBudgetStore WorkTimeBudgets;
        public EmploymentRelationshipRegistry Employments;
        public FinancialObligationAuthority FinancialObligations;
        public EntityIdRegistry Ids;
        public RecruitmentService Recruiting;
        public ShopperSellerKnowledge SupplierKnowledge;
        public IManagerBusinessLender BusinessLender;
        /// <summary>
        /// Pass VIII worker 8: optional social-knowledge intake. When bound,
        /// the Procure executor consults what the manager person actually
        /// heard about candidate suppliers (corroborated trouble only) before
        /// choosing. Null by default: procurement works without it, and an
        /// unbound store never changes a supplier choice.
        /// </summary>
        public IKnowledgeTransmissionStore SocialKnowledge;
        /// <summary>
        /// The existing recruitment path (SimulationDrivers.ExecuteManagerRecruitment).
        /// Returns the opened effort id, or null when the path refuses.
        /// </summary>
        public Func<BusinessInstanceState, LaborDemandReport, ManagerDecisionResult, int, List<string>, string> OpenRecruitment;
    }

    /// <summary>
    /// Routes one ManagerBrain decision to its real production executor.
    /// Owns the execution ledger (idempotency, pending requests, shipments).
    /// </summary>
    public sealed class ManagerDecisionExecutionService
    {
        private readonly ManagerDecisionExecutionLedger ledger = new ManagerDecisionExecutionLedger();
        private Dictionary<string, IManagerDecisionExecutor> executors;
        private ManagerExecutionServices services;

        public ManagerDecisionExecutionLedger Ledger => ledger;
        public bool IsConfigured => services != null && executors != null;

        public void Configure(ManagerExecutionServices services)
        {
            this.services = services;
            if (services == null)
            {
                executors = null;
                return;
            }

            executors = new Dictionary<string, IManagerDecisionExecutor>(StringComparer.Ordinal)
            {
                { ManagerActKinds.OpenRecruitment, new ManagerOpenRecruitmentExecutor(services, ledger) },
                { ManagerActKinds.Schedule, new ManagerScheduleExecutor(services, ledger) },
                { ManagerActKinds.Procure, new ManagerProcureExecutor(services, ledger) },
                { ManagerActKinds.PriceChange, new ManagerPriceChangeExecutor(services, ledger) },
                { ManagerActKinds.OrderMaintenance, new ManagerOrderMaintenanceExecutor(services, ledger) },
                { ManagerActKinds.Borrow, new ManagerBorrowExecutor(services, ledger) },
                { ManagerActKinds.SellAsset, new ManagerSellAssetExecutor(services, ledger) },
            };
        }

        /// <summary>
        /// Executes one manager decision. The decision executes, becomes a
        /// legitimate pending request, or fails with a reason — never silently
        /// disappears. Delegated-authority context is rebuilt from the grant.
        /// </summary>
        public ManagerExecutionOutcome ExecuteDecision(
            BusinessInstanceState business,
            DelegatedAuthority authority,
            int managerPersonId,
            LaborDemandReport demand,
            ManagerDecisionResult decision,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var outcome = new ManagerExecutionOutcome();

            if (!IsConfigured)
            {
                outcome.Failed = true;
                outcome.Reason = "manager execution is not configured with production authorities";
                diagnostics.Add("ManagerDecisionExecution: " + outcome.Reason + ".");
                return outcome;
            }

            if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
            {
                outcome.Failed = true;
                outcome.Reason = "no business to execute against";
                diagnostics.Add("ManagerDecisionExecution: " + outcome.Reason + ".");
                return outcome;
            }

            if (decision == null || (!decision.Acted && !decision.Escalated) || string.IsNullOrWhiteSpace(decision.ActionKind))
            {
                outcome.Failed = true;
                outcome.Reason = "no manager act to execute";
                return outcome;
            }

            if (!executors.TryGetValue(decision.ActionKind, out IManagerDecisionExecutor executor) || executor == null)
            {
                outcome.Failed = true;
                outcome.Reason = $"no executor for decision kind '{decision.ActionKind}'";
                diagnostics.Add($"ManagerDecisionExecution: {outcome.Reason} — the decision was not dropped silently.");
                ledger.RecordAction(new ManagerExecutedActionRecord
                {
                    ActionKey = ActionKeyFor(decision.ActionKind, business.InstanceId, dayIndex, "unknown-kind"),
                    BusinessInstanceId = business.InstanceId,
                    DayIndex = dayIndex,
                    Kind = decision.ActionKind,
                    Succeeded = false,
                    Reason = outcome.Reason,
                    ManagerPersonId = managerPersonId,
                });
                return outcome;
            }

            var context = new ManagerExecutionContext
            {
                BusinessInstanceId = business.InstanceId,
                ManagerPersonId = managerPersonId,
                DayIndex = dayIndex,
                PurchaseLimitCents = authority != null ? authority.PurchaseLimitCents : 0,
                MaxBorrowingCents = 0,
                DelegatedScopes = ScopeNames(authority != null ? authority.Scopes : DelegationScope.None),
            };

            ManagerExecutionOutcome result = executor.Execute(decision, context, diagnostics);
            if (result == null)
            {
                result = new ManagerExecutionOutcome { Failed = true, Reason = "executor returned no outcome" };
            }

            // Exactly one of Executed / Pending / Failed must hold; repair a
            // sloppy executor rather than propagating an ambiguous outcome.
            int trueCount = (result.Executed ? 1 : 0) + (result.Pending ? 1 : 0) + (result.Failed ? 1 : 0);
            if (trueCount != 1)
            {
                diagnostics.Add($"ManagerDecisionExecution: executor for '{decision.ActionKind}' returned an ambiguous outcome — recorded as failed.");
                result = new ManagerExecutionOutcome { Failed = true, Reason = "ambiguous executor outcome" };
            }

            return result;
        }

        /// <summary>
        /// Advances in-transit procurement shipments: arrived shipments are
        /// received through GenericShipmentReceivingAuthority (idempotent).
        /// </summary>
        public void AdvanceShipments(int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!IsConfigured || services.FindBusiness == null) return;

            // Pass IX WS4 (A4-15c): iterate only the in-transit index (a copy,
            // since transitions below mutate it), in ledger order — the same
            // shipments, in the same order, with the same outcomes as the
            // pre-index full-history scan. Completed shipments stay recorded
            // in the ledger and are never rescanned.
            var active = new List<ManagerProcurementShipment>(ledger.InTransitShipments);
            foreach (ManagerProcurementShipment shipment in active)
            {
                if (shipment == null || !string.Equals(shipment.Status, "InTransit", StringComparison.Ordinal))
                    continue;
                if (shipment.ExpectedArrivalDayIndex > dayIndex)
                    continue;

                BusinessInstanceState business = services.FindBusiness(shipment.BusinessInstanceId);
                if (business == null)
                {
                    shipment.Status = "Failed";
                    shipment.FailureReason = "destination business no longer exists";
                    ledger.MarkShipmentNoLongerInTransit(shipment);
                    diagnostics.Add($"ManagerDecisionExecution: shipment {shipment.ShipmentId} failed — {shipment.FailureReason}.");
                    continue;
                }

                if (GenericShipmentReceivingAuthority.TryReceive(
                    business, shipment.ShipmentId, shipment.ProductId, shipment.Quantity,
                    shipment.UnitCostCents, null, dayIndex, out string reason))
                {
                    shipment.Status = "Delivered";
                    ledger.MarkShipmentNoLongerInTransit(shipment);
                    diagnostics.Add($"ManagerDecisionExecution: shipment {shipment.ShipmentId} delivered " +
                                    $"({shipment.Quantity:0.#} {shipment.ProductId} to {shipment.BusinessInstanceId}).");
                }
                else
                {
                    // Idempotent receive refused: already received. Mark
                    // delivered so we never retry the same shipment.
                    shipment.Status = "Delivered";
                    ledger.MarkShipmentNoLongerInTransit(shipment);
                    diagnostics.Add($"ManagerDecisionExecution: shipment {shipment.ShipmentId} already received ({reason}) — marked delivered.");
                }
            }
        }

        public ManagerDecisionExecutionSaveDto CaptureSaveDto() => ledger.CaptureSaveDto();

        public void LoadFromSaveDto(ManagerDecisionExecutionSaveDto dto) => ledger.LoadFromSaveDto(dto);

        internal static string ActionKeyFor(string kind, string businessId, int dayIndex, string detail)
        {
            return $"{kind}:{businessId}:{dayIndex}:{detail}";
        }

        private static List<string> ScopeNames(DelegationScope scopes)
        {
            var names = new List<string>();
            if (scopes.HasFlag(DelegationScope.Purchasing)) names.Add(ManagerScopeNames.Purchasing);
            if (scopes.HasFlag(DelegationScope.Scheduling)) names.Add(ManagerScopeNames.Scheduling);
            if (scopes.HasFlag(DelegationScope.Maintenance)) names.Add(ManagerScopeNames.Maintenance);
            if (scopes.HasFlag(DelegationScope.CashHandling)) names.Add(ManagerScopeNames.CashHandling);
            if (scopes.HasFlag(DelegationScope.Inventory)) names.Add(ManagerScopeNames.Inventory);
            if (scopes.HasFlag(DelegationScope.Staffing)) names.Add(ManagerScopeNames.Staffing);
            return names;
        }
    }

    /// <summary>
    /// Shared plumbing for the per-kind executors: idempotency keys, delegated
    /// scope checks, cash access. Every executor fails loudly with a reason
    /// instead of guessing.
    /// </summary>
    public abstract class ManagerDecisionExecutorBase : IManagerDecisionExecutor
    {
        protected readonly ManagerExecutionServices services;
        protected readonly ManagerDecisionExecutionLedger ledger;

        protected ManagerDecisionExecutorBase(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
        {
            this.services = services;
            this.ledger = ledger;
        }

        public abstract string DecisionKind { get; }

        public abstract ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics);

        protected ManagerExecutionOutcome Fail(string reason, List<string> diagnostics)
        {
            (diagnostics ?? new List<string>()).Add($"ManagerDecisionExecution[{DecisionKind}]: failed — {reason}.");
            return new ManagerExecutionOutcome { Failed = true, Reason = reason };
        }

        protected bool HasScope(ManagerExecutionContext context, string scope)
        {
            if (context == null || context.DelegatedScopes == null || string.IsNullOrWhiteSpace(scope))
                return false;
            foreach (string granted in context.DelegatedScopes)
            {
                if (string.Equals(granted, scope, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        protected BusinessInstanceState FindBusiness(ManagerExecutionContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.BusinessInstanceId)) return null;
            return services.FindBusiness != null ? services.FindBusiness(context.BusinessInstanceId) : null;
        }

        protected static int ReadCashCents(BusinessInstanceState business)
        {
            return business != null && business.RuntimeState != null ? business.RuntimeState.CurrentCashCents : 0;
        }

        protected static string AdjustCash(BusinessInstanceState business, int deltaCents)
        {
            if (business == null) return "no business";
            if (business.RuntimeState == null) return "the business has no cash ledger";
            if (deltaCents < 0 && business.RuntimeState.CurrentCashCents < -deltaCents)
                return $"insufficient cash ({business.RuntimeState.CurrentCashCents}c) for {deltaCents}c";
            business.RuntimeState.AdjustCashCents(deltaCents, recordNetDelta: true);
            return null;
        }

        protected void RecordAttempt(string actionKey, ManagerExecutionContext context, bool succeeded, string reason, string reference)
        {
            ledger.RecordAction(new ManagerExecutedActionRecord
            {
                ActionKey = actionKey,
                BusinessInstanceId = context != null ? context.BusinessInstanceId : string.Empty,
                DayIndex = context != null ? context.DayIndex : -1,
                Kind = DecisionKind,
                Succeeded = succeeded,
                Reason = reason ?? string.Empty,
                ResultingReference = reference ?? string.Empty,
                ManagerPersonId = context != null ? context.ManagerPersonId : -1,
            });
        }

        protected string ActionKey(ManagerExecutionContext context, string detail)
        {
            return ManagerDecisionExecutionService.ActionKeyFor(
                DecisionKind, context != null ? context.BusinessInstanceId : string.Empty,
                context != null ? context.DayIndex : -1, detail ?? string.Empty);
        }
    }

    /// <summary>
    /// OpenRecruitment: keeps the existing ExecuteManagerRecruitment path. The
    /// real work (RecruitmentService.OpenEffort) runs in the driver's existing
    /// method; this executor only routes and records.
    /// </summary>
    public sealed class ManagerOpenRecruitmentExecutor : ManagerDecisionExecutorBase
    {
        public ManagerOpenRecruitmentExecutor(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
            : base(services, ledger) { }

        public override string DecisionKind => ManagerActKinds.OpenRecruitment;

        public override ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BusinessInstanceState business = FindBusiness(context);
            if (business == null)
                return Fail("unknown business '" + (context != null ? context.BusinessInstanceId : "?") + "'", diagnostics);

            string actionKey = ActionKey(context, "recruitment");
            if (ledger.HasExecuted(actionKey))
                return Fail("recruitment already executed for this business today — refusing a duplicate effort", diagnostics);

            if (!HasScope(context, ManagerScopeNames.Staffing))
                return Fail("the manager does not hold the 'Staffing' scope", diagnostics);

            if (services.OpenRecruitment == null)
                return Fail("the recruitment path is not wired", diagnostics);

            LaborDemandReport demand = services.DemandFor != null ? services.DemandFor(business.InstanceId) : null;
            if (demand == null)
                return Fail("no labor demand report for '" + business.InstanceId + "'", diagnostics);

            string effortId = services.OpenRecruitment(business, demand, decision, context.DayIndex, diagnostics);
            if (string.IsNullOrWhiteSpace(effortId))
            {
                RecordAttempt(actionKey, context, false, "the recruitment path refused the effort", string.Empty);
                return Fail("the recruitment path refused the effort", diagnostics);
            }

            RecordAttempt(actionKey, context, true, string.Empty, effortId);
            diagnostics.Add($"ManagerDecisionExecution[OpenRecruitment]: opened {effortId} for {business.InstanceId}.");
            return new ManagerExecutionOutcome { Executed = true, ResultingReference = effortId };
        }
    }

    /// <summary>
    /// Schedule: real work scheduling through the existing WorkScheduler, with
    /// task assignment/start through TaskAuthority and PersonScheduleTracker.
    /// </summary>
    public sealed class ManagerScheduleExecutor : ManagerDecisionExecutorBase
    {
        public ManagerScheduleExecutor(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
            : base(services, ledger) { }

        public override string DecisionKind => ManagerActKinds.Schedule;

        public override ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BusinessInstanceState business = FindBusiness(context);
            if (business == null)
                return Fail("unknown business '" + (context != null ? context.BusinessInstanceId : "?") + "'", diagnostics);

            string actionKey = ActionKey(context, "work");
            if (ledger.HasExecuted(actionKey))
                return Fail("work already scheduled for this business today — refusing a duplicate pass", diagnostics);

            if (!HasScope(context, ManagerScopeNames.Scheduling))
                return Fail("the manager does not hold the 'Scheduling' scope", diagnostics);

            if (services.WorkScheduling == null || services.Tasks == null || services.BusinessEntityFor == null)
                return Fail("the scheduling authorities are not wired", diagnostics);

            EntityId businessEntity = services.BusinessEntityFor(business.InstanceId);
            List<WorkTask> openTasks = services.Tasks.GetOpenTasks(businessEntity);
            var schedulerWork = new List<SchedulerOpenWorkItem>();
            var taskByTextId = new Dictionary<string, WorkTask>(StringComparer.Ordinal);
            if (openTasks != null)
            {
                foreach (WorkTask task in openTasks)
                {
                    if (task == null || !task.IsOpen || task.Status != TaskStatus.Queued) continue;
                    TaskDefinition definition = services.Tasks.GetDefinition(task.DefinitionId);
                    string family = definition != null ? definition.DisplayName : null;
                    if (string.IsNullOrWhiteSpace(family)) family = task.DefinitionId;
                    schedulerWork.Add(new SchedulerOpenWorkItem
                    {
                        TaskId = task.TaskId.ToString(),
                        Family = family,
                        MinutesRequired = Math.Max(1, task.MinutesRemaining),
                        RequiredEquipment = definition != null && definition.EquipmentClasses.Count > 0
                            ? definition.EquipmentClasses[0] : string.Empty,
                        LocationId = task.LocationId,
                    });
                    taskByTextId[task.TaskId.ToString()] = task;
                }
            }

            if (schedulerWork.Count == 0)
            {
                RecordAttempt(actionKey, context, false, "no open queued work to schedule", string.Empty);
                return Fail("no open queued work to schedule", diagnostics);
            }

            var workers = new List<SchedulerAvailableWorker>();
            var seen = new HashSet<int>();
            EmploymentRelationshipRegistry registry = business.RuntimeState != null
                ? business.RuntimeState.EmploymentRegistry
                : null;
            registry = registry ?? services.Employments;
            if (registry != null)
            {
                foreach (EmploymentRelationship relationship in registry.GetActiveByEmployer(business.InstanceId))
                {
                    AddWorker(relationship != null ? relationship.EmployeePersonId : -1, seen, workers);
                }
            }
            if (business.Owner != null)
            {
                AddWorker(business.Owner.PersonId, seen, workers);
            }

            if (workers.Count == 0)
            {
                RecordAttempt(actionKey, context, false, "no available workers to schedule", string.Empty);
                return Fail("no available workers to schedule", diagnostics);
            }

            WorkAssignmentReport report = services.WorkScheduling.AssignDailyWork(new SchedulerInputs
            {
                BusinessInstanceId = business.InstanceId,
                DayIndex = context.DayIndex,
                OpenWork = schedulerWork,
                Workers = workers,
            }, diagnostics);

            int started = ManagerWorkSchedulerDrive.StartAssignedTasks(
                report, taskByTextId, services.Tasks, services.PersonSchedules, services.WorkTimeBudgets,
                context.DayIndex, diagnostics, ManagerWorkSchedulerDrive.DefaultTaskLabel, null);

            string reference = $"schedule:{business.InstanceId}:d{context.DayIndex}:{started}started";
            if (started <= 0)
            {
                RecordAttempt(actionKey, context, false, "the scheduler produced no startable assignments", reference);
                return Fail("the scheduler produced no startable assignments", diagnostics);
            }

            RecordAttempt(actionKey, context, true, string.Empty, reference);
            diagnostics.Add($"ManagerDecisionExecution[Schedule]: {started} task(s) scheduled and started for {business.InstanceId}.");
            return new ManagerExecutionOutcome { Executed = true, ResultingReference = reference };
        }

        private void AddWorker(int personId, HashSet<int> seen, List<SchedulerAvailableWorker> workers)
        {
            if (personId < 0 || !seen.Add(personId)) return;
            int minutes = 480;
            if (services.WorkTimeBudgets != null)
            {
                WorkTimeBudget budget = services.WorkTimeBudgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                if (budget != null) minutes = budget.MinutesRemaining;
            }
            workers.Add(new SchedulerAvailableWorker
            {
                PersonId = personId,
                AvailableMinutes = Math.Max(0, minutes),
                SkillEvidence = new Dictionary<string, float>(),
            });
        }
    }

    /// <summary>
    /// Procure: real procurement through the transaction authorities
    /// (GenericProcurementPlanner builds the order from a real supplier offer;
    /// GenericShipmentReceivingAuthority writes the stock on delivery).
    ///
    /// Checks, in order: delegated scope, idempotency, real need (retail
    /// reorder points), pending deliveries, a supplier with ACTUAL goods,
    /// supplier access, order build, cost vs cash and delegated spend cap,
    /// cash movement (buyer pays, supplier is paid — conserved), supplier
    /// inventory consumption, and shipment booking. Delivery is a separate
    /// idempotent step (AdvanceShipments).
    /// </summary>
    public sealed class ManagerProcureExecutor : ManagerDecisionExecutorBase
    {
        public ManagerProcureExecutor(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
            : base(services, ledger) { }

        public override string DecisionKind => ManagerActKinds.Procure;

        public override ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BusinessInstanceState business = FindBusiness(context);
            if (business == null)
                return Fail("unknown business '" + (context != null ? context.BusinessInstanceId : "?") + "'", diagnostics);

            if (!HasScope(context, ManagerScopeNames.Purchasing))
                return Fail("the manager does not hold the 'Purchasing' scope", diagnostics);

            if (services.AllBusinesses == null)
                return Fail("no business directory to find a supplier", diagnostics);

            // Real need: retail lines at or below their reorder point.
            string needProductId = null;
            float needQuantity = 0f;
            GenericRetailProductLine needLine = null;
            foreach (GenericInventoryPosition position in business.GenericConfiguration.Inventory)
            {
                if (position == null || string.IsNullOrWhiteSpace(position.ProductId)) continue;
                if (!business.GenericConfiguration.Retail.TryGetLine(position.ProductId, out GenericRetailProductLine line) || line == null)
                    continue;
                if (!line.CanReorder(position.AvailableQuantity)) continue;
                if (ledger.HasInTransitShipment(business.InstanceId, position.ProductId)) continue;
                needProductId = position.ProductId;
                needLine = line;
                needQuantity = Math.Max(1f, line.TargetStock - position.AvailableQuantity);
                break;
            }

            if (string.IsNullOrWhiteSpace(needProductId))
            {
                string key = ActionKey(context, "no-need");
                RecordAttempt(key, context, false, "no retail line at or below its reorder point", string.Empty);
                return Fail("no retail line at or below its reorder point — no procurement need", diagnostics);
            }

            string actionKey = ActionKey(context, needProductId);
            if (ledger.HasExecuted(actionKey))
                return Fail($"procurement of '{needProductId}' already executed for this business today — refusing a duplicate order", diagnostics);

            // Supplier with actual goods. Prefer suppliers the business already
            // knows (ShopperSellerKnowledge); never invent a supplier.
            BusinessInstanceState supplier = FindSupplier(business, needProductId, needQuantity, context.ManagerPersonId, diagnostics);
            if (supplier == null)
            {
                RecordAttempt(actionKey, context, false, $"no supplier with actual '{needProductId}' goods", string.Empty);
                return Fail($"no supplier with actual '{needProductId}' goods is reachable", diagnostics);
            }

            float supplierAvailable = supplier.GenericConfiguration.GetAvailableInventoryQuantity(needProductId);
            int supplierPriceCents = SupplierUnitPriceCents(supplier, needProductId);

            var offer = new SupplierOffer
            {
                OfferId = $"offer:{supplier.InstanceId}:{needProductId}:d{context.DayIndex}",
                SupplierId = supplier.InstanceId,
                Lines = new List<SupplierOfferLine>
                {
                    new SupplierOfferLine
                    {
                        ProductId = needProductId,
                        AvailableQuantity = supplierAvailable,
                        PackageQuantity = 1f,
                        PackagePriceCents = Math.Max(0, supplierPriceCents),
                    },
                },
                PaymentTerms = "cash on delivery",
            };

            var requested = new Dictionary<string, float> { { needProductId, Math.Min(needQuantity, supplierAvailable) } };
            if (!GenericProcurementPlanner.TryBuildOrder(offer, requested, out SupplierPurchaseOrder order, out string buildReason))
            {
                RecordAttempt(actionKey, context, false, "order build refused: " + buildReason, string.Empty);
                return Fail("order build refused: " + buildReason, diagnostics);
            }

            int totalCents = order.TotalCents;
            int cashCents = ReadCashCents(business);
            int spendCap = Math.Max(0, context.PurchaseLimitCents);
            if (totalCents > spendCap)
            {
                RecordAttempt(actionKey, context, false, $"order {totalCents}c exceeds the delegated spend cap ({spendCap}c)", string.Empty);
                return Fail($"order {totalCents}c exceeds the delegated spend cap ({spendCap}c) — escalate, do not bypass", diagnostics);
            }
            if (totalCents > cashCents)
            {
                RecordAttempt(actionKey, context, false, $"order {totalCents}c exceeds available cash ({cashCents}c)", string.Empty);
                return Fail($"order {totalCents}c exceeds available cash ({cashCents}c) — the manager cannot spend cash the business lacks", diagnostics);
            }

            // Cash movement: buyer pays, supplier is paid. Conserved, no
            // phantom money. Supplier goods leave their inventory for real.
            string debitReason = AdjustCash(business, -totalCents);
            if (debitReason != null)
            {
                RecordAttempt(actionKey, context, false, "cash debit refused: " + debitReason, string.Empty);
                return Fail("cash debit refused: " + debitReason, diagnostics);
            }

            GenericInventoryPosition supplierPosition = supplier.GenericConfiguration.GetOrCreateInventory(needProductId);
            if (!supplierPosition.TryConsume(order.TotalQuantity, out float consumed) || consumed < order.TotalQuantity - 0.001f)
            {
                // Roll the cash back: the goods were not actually available.
                AdjustCash(business, totalCents);
                RecordAttempt(actionKey, context, false, "supplier goods could not be consumed — cash rolled back", string.Empty);
                return Fail("supplier goods could not be consumed — cash rolled back, no phantom transfer", diagnostics);
            }

            string creditReason = AdjustCash(supplier, totalCents);
            if (creditReason != null)
            {
                // Supplier cannot take the cash: roll everything back.
                supplierPosition.Add(order.TotalQuantity, supplierPriceCents, null, "procurement rollback", context.DayIndex);
                AdjustCash(business, totalCents);
                RecordAttempt(actionKey, context, false, "supplier cash credit refused — rolled back", string.Empty);
                return Fail("supplier cash credit refused — the whole transfer was rolled back", diagnostics);
            }

            string shipmentId = $"mgr-ship:{business.InstanceId}:d{context.DayIndex}:{needProductId}";
            ledger.AddShipment(new ManagerProcurementShipment
            {
                ShipmentId = shipmentId,
                OrderId = order.OrderId,
                BusinessInstanceId = business.InstanceId,
                SupplierBusinessId = supplier.InstanceId,
                ProductId = needProductId,
                Quantity = order.TotalQuantity,
                UnitCostCents = order.Lines.Count > 0 ? order.Lines[0].UnitPriceCents : 0,
                TotalCents = totalCents,
                CreatedDayIndex = context.DayIndex,
                ExpectedArrivalDayIndex = context.DayIndex + 1,
                Status = "InTransit",
            });

            // The buyer learns the supplier (provenance for future choice).
            if (services.SupplierKnowledge != null && context.ManagerPersonId >= 0)
            {
                services.SupplierKnowledge.LearnSupplier(context.ManagerPersonId, supplier.InstanceId,
                    $"manager procurement {order.OrderId}", context.DayIndex);
            }

            string reference = $"procurement:{business.InstanceId}:d{context.DayIndex}:{needProductId}:{order.OrderId}";
            RecordAttempt(actionKey, context, true, string.Empty, reference);
            diagnostics.Add($"ManagerDecisionExecution[Procure]: ordered {order.TotalQuantity:0.#} {needProductId} " +
                            $"from {supplier.InstanceId} for {totalCents}c (order {order.OrderId}); shipment {shipmentId} in transit.");
            var outcome = new ManagerExecutionOutcome { Executed = true, ResultingReference = reference };
            return outcome;
        }

        private BusinessInstanceState FindSupplier(
            BusinessInstanceState buyer, string productId, float needQuantity, int managerPersonId, List<string> diagnostics)
        {
            BusinessInstanceState fallback = null;
            foreach (BusinessInstanceState candidate in services.AllBusinesses())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.InstanceId)) continue;
                if (string.Equals(candidate.InstanceId, buyer.InstanceId, StringComparison.Ordinal)) continue;
                if (candidate.AssignedBuildingId < 0) continue; // no premises: cannot trade
                float available = candidate.GenericConfiguration.GetAvailableInventoryQuantity(productId);
                if (available < needQuantity - 0.001f && available <= 0f) continue;
                if (available <= 0f) continue;

                // Pass VIII worker 8: the manager buys with what they
                // actually heard. Corroborated trouble (a firsthand telling
                // or two independent sources) steers the order to the next
                // known supplier with real goods. A single uncorroborated
                // rumor is noted and does not move the order: the supplier
                // is never punished on hearsay alone.
                if (services.SocialKnowledge != null && managerPersonId >= 0
                    && !string.IsNullOrWhiteSpace(candidate.InstanceId))
                {
                    SocialEvidenceAssessment trouble = SocialEvidenceEvaluator.Assess(
                        services.SocialKnowledge, managerPersonId,
                        SocialEvidenceEvaluator.SupplierTroubleSubject(candidate.InstanceId));
                    if (trouble.Corroborated)
                    {
                        diagnostics.Add($"ManagerDecisionExecution[Procure]: skipping supplier {candidate.InstanceId} — " +
                                        SocialEvidenceEvaluator.CiteEvidence(trouble) + ".");
                        continue;
                    }
                    if (trouble.PacketCount > 0)
                    {
                        diagnostics.Add($"ManagerDecisionExecution[Procure]: uncorroborated rumor about supplier " +
                                        $"{candidate.InstanceId} noted — not acting on it. " +
                                        SocialEvidenceEvaluator.CiteEvidence(trouble) + ".");
                    }
                }

                bool known = services.SupplierKnowledge != null && managerPersonId >= 0
                    && services.SupplierKnowledge.Knows(managerPersonId, candidate.InstanceId);
                if (known)
                {
                    diagnostics.Add($"ManagerDecisionExecution[Procure]: supplier {candidate.InstanceId} has {available:0.#} {productId} (known supplier).");
                    return candidate;
                }
                if (fallback == null) fallback = candidate;
            }

            if (fallback != null)
            {
                diagnostics.Add($"ManagerDecisionExecution[Procure]: supplier {fallback.InstanceId} has " +
                                $"{fallback.GenericConfiguration.GetAvailableInventoryQuantity(productId):0.#} {productId}.");
            }
            return fallback;
        }

        private static int SupplierUnitPriceCents(BusinessInstanceState supplier, string productId)
        {
            // A real price first: the supplier's own retail line. Otherwise
            // their average unit cost (at-cost transfer, terms noted).
            if (supplier.GenericConfiguration.Retail.TryGetLine(productId, out GenericRetailProductLine line) && line != null)
                return Math.Max(0, line.SellingPriceCents);
            return Math.Max(0, supplier.GenericConfiguration.GetInventoryAverageUnitCostCents(productId));
        }
    }

    /// <summary>
    /// PriceChange: real price updates on the business's goods. The direction
    /// comes from real stock signals: overstock (above target) nudges the
    /// price down to move goods; scarcity (at/below reorder) nudges it up.
    /// Bounded (±10%, at least 1c), one change per product per day, and never
    /// below zero. Future real sales record the new price with provenance.
    /// </summary>
    public sealed class ManagerPriceChangeExecutor : ManagerDecisionExecutorBase
    {
        /// <summary>Maximum single price move, as a fraction of the old price.</summary>
        public const float MaxMoveFraction01 = 0.10f;

        public ManagerPriceChangeExecutor(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
            : base(services, ledger) { }

        public override string DecisionKind => ManagerActKinds.PriceChange;

        public override ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BusinessInstanceState business = FindBusiness(context);
            if (business == null)
                return Fail("unknown business '" + (context != null ? context.BusinessInstanceId : "?") + "'", diagnostics);

            if (!HasScope(context, ManagerScopeNames.Inventory))
                return Fail("the manager does not hold the 'Inventory' scope", diagnostics);

            // Pick the line with the strongest real signal: the largest
            // over/under-stock imbalance.
            GenericRetailProductLine chosen = null;
            float chosenSignal = 0f;
            int direction = 0;
            foreach (GenericRetailProductLine line in business.GenericConfiguration.Retail.ProductLines)
            {
                if (line == null || !line.EnabledForSale || string.IsNullOrWhiteSpace(line.ProductId)) continue;
                float available = business.GenericConfiguration.GetAvailableInventoryQuantity(line.ProductId);
                if (available > line.TargetStock + 0.001f && line.TargetStock > 0f)
                {
                    float signal = available / line.TargetStock;
                    if (signal > chosenSignal) { chosen = line; chosenSignal = signal; direction = -1; }
                }
                else if (line.CanReorder(available))
                {
                    float signal = line.ReorderPoint <= 0f ? 2f : 1f + (line.ReorderPoint - available) / line.ReorderPoint;
                    if (signal > chosenSignal) { chosen = line; chosenSignal = signal; direction = 1; }
                }
            }

            if (chosen == null || direction == 0)
            {
                string key = ActionKey(context, "no-signal");
                RecordAttempt(key, context, false, "no retail line shows a real over/under-stock signal", string.Empty);
                return Fail("no retail line shows a real over/under-stock signal — no price to change", diagnostics);
            }

            string actionKey = ActionKey(context, chosen.ProductId);
            if (ledger.HasExecuted(actionKey))
                return Fail($"price for '{chosen.ProductId}' already changed today — refusing a duplicate change", diagnostics);

            int oldPrice = chosen.SellingPriceCents;
            int move = Math.Max(1, Mathf.RoundToInt(oldPrice * MaxMoveFraction01));
            int newPrice = direction < 0 ? Math.Max(0, oldPrice - move) : oldPrice + move;
            if (newPrice == oldPrice)
            {
                RecordAttempt(actionKey, context, false, "bounded move leaves the price unchanged", string.Empty);
                return Fail("the bounded price move leaves the price unchanged", diagnostics);
            }

            chosen.SetSellingPriceCents(newPrice);
            string why = direction < 0
                ? $"overstock ({business.GenericConfiguration.GetAvailableInventoryQuantity(chosen.ProductId):0.#} vs target {chosen.TargetStock:0.#})"
                : $"scarcity ({business.GenericConfiguration.GetAvailableInventoryQuantity(chosen.ProductId):0.#} at/below reorder {chosen.ReorderPoint:0.#})";
            string reference = $"price-change:{business.InstanceId}:d{context.DayIndex}:{chosen.ProductId}:{oldPrice}c->{newPrice}c";
            RecordAttempt(actionKey, context, true, string.Empty, reference);
            diagnostics.Add($"ManagerDecisionExecution[PriceChange]: {chosen.ProductId} {oldPrice}c -> {newPrice}c ({why}).");
            return new ManagerExecutionOutcome { Executed = true, ResultingReference = reference };
        }
    }

    /// <summary>
    /// The maintenance work-order task definition. Registered idempotently;
    /// TaskAuthority rejects duplicates deterministically anyway.
    /// </summary>
    public static class ManagerMaintenanceTaskCatalog
    {
        public const string MaintenanceWorkOrderId = "business.maintenance-work-order";
        public const string MaintenanceWorkOrderName = "Business maintenance work order";

        public static void EnsureRegistered(TaskAuthority tasks, List<string> diagnostics)
        {
            if (tasks == null) return;
            if (tasks.GetDefinition(MaintenanceWorkOrderId) != null) return;
            string rejection;
            tasks.RegisterDefinition(new TaskDefinition(
                MaintenanceWorkOrderId, MaintenanceWorkOrderName, 120), out rejection);
            if (!string.IsNullOrWhiteSpace(rejection))
                (diagnostics ?? new List<string>()).Add("ManagerDecisionExecution: maintenance definition not registered: " + rejection + ".");
        }
    }

    /// <summary>
    /// OrderMaintenance: a real maintenance work order through the task system.
    /// The order enters the business's queue; the existing scheduler assigns
    /// it like any other work. An already-open maintenance order is never
    /// duplicated.
    /// </summary>
    public sealed class ManagerOrderMaintenanceExecutor : ManagerDecisionExecutorBase
    {
        public ManagerOrderMaintenanceExecutor(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
            : base(services, ledger) { }

        public override string DecisionKind => ManagerActKinds.OrderMaintenance;

        public override ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BusinessInstanceState business = FindBusiness(context);
            if (business == null)
                return Fail("unknown business '" + (context != null ? context.BusinessInstanceId : "?") + "'", diagnostics);

            if (!HasScope(context, ManagerScopeNames.Maintenance))
                return Fail("the manager does not hold the 'Maintenance' scope", diagnostics);

            if (services.Tasks == null || services.BusinessEntityFor == null)
                return Fail("the task system is not wired", diagnostics);

            string actionKey = ActionKey(context, "maintenance");
            if (ledger.HasExecuted(actionKey))
                return Fail("a maintenance order was already placed for this business today — refusing a duplicate", diagnostics);

            EntityId businessEntity = services.BusinessEntityFor(business.InstanceId);
            WorkTask existing = services.Tasks.FindOpenTask(ManagerMaintenanceTaskCatalog.MaintenanceWorkOrderId, businessEntity, EntityId.Invalid);
            if (existing != null)
            {
                RecordAttempt(actionKey, context, false, "a maintenance work order is already open", existing.TaskId.ToString());
                return Fail("a maintenance work order is already open — it will be scheduled, not duplicated", diagnostics);
            }

            ManagerMaintenanceTaskCatalog.EnsureRegistered(services.Tasks, diagnostics);
            WorkTask order = services.Tasks.CreateTask(
                ManagerMaintenanceTaskCatalog.MaintenanceWorkOrderId, businessEntity, context.DayIndex);
            if (order == null)
            {
                RecordAttempt(actionKey, context, false, "the task system refused the work order", string.Empty);
                return Fail("the task system refused the maintenance work order", diagnostics);
            }

            string reference = $"maintenance:{business.InstanceId}:d{context.DayIndex}:{order.TaskId}";
            RecordAttempt(actionKey, context, true, string.Empty, reference);
            diagnostics.Add($"ManagerDecisionExecution[OrderMaintenance]: work order {order.TaskId} queued for {business.InstanceId}.");
            return new ManagerExecutionOutcome { Executed = true, ResultingReference = reference };
        }
    }

    /// <summary>
    /// Borrow: real borrowing through FinancialObligationAuthority within the
    /// manager's delegated limits. When the decision is escalated (the brain
    /// escalates all borrowing for owner approval), a legitimate pending
    /// owner-approval request is created instead — never silently bypassed.
    /// </summary>
    public sealed class ManagerBorrowExecutor : ManagerDecisionExecutorBase
    {
        public ManagerBorrowExecutor(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
            : base(services, ledger) { }

        public override string DecisionKind => ManagerActKinds.Borrow;

        public override ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BusinessInstanceState business = FindBusiness(context);
            if (business == null)
                return Fail("unknown business '" + (context != null ? context.BusinessInstanceId : "?") + "'", diagnostics);

            int amountCents = ParseAmountCents(decision);
            if (amountCents <= 0)
                return Fail("no borrowing amount on the decision", diagnostics);

            string actionKey = ActionKey(context, amountCents + "c");

            // The brain escalates all borrowing: the honest outcome is a
            // pending owner-approval request, never a silent bypass.
            if (decision.Escalated)
            {
                // The deduplication key excludes the day: retrying the same
                // blocked borrowing on a later day must find the already-pending
                // request, not create a duplicate.
                string requestKey = $"Borrow:{business.InstanceId}:{amountCents}c";
                ManagerPendingRequest existing = ledger.FindPending(requestKey);
                if (existing != null && string.Equals(existing.Status, "Pending", StringComparison.Ordinal))
                {
                    diagnostics.Add($"ManagerDecisionExecution[Borrow]: owner-approval request {existing.RequestId} already pending — not duplicated.");
                    return new ManagerExecutionOutcome { Pending = true, Reason = "owner approval already pending", ResultingReference = existing.RequestId };
                }

                var request = new ManagerPendingRequest
                {
                    RequestId = $"mgr-req:borrow:{business.InstanceId}:d{context.DayIndex}:{amountCents}c",
                    RequestKey = requestKey,
                    BusinessInstanceId = business.InstanceId,
                    Kind = ManagerActKinds.Borrow,
                    AmountCents = amountCents,
                    Detail = decision.EscalationReason ?? "owner approval required for borrowing",
                    CreatedDayIndex = context.DayIndex,
                    ManagerPersonId = context.ManagerPersonId,
                    Status = "Pending",
                };
                ledger.AddPending(request);
                RecordAttempt(actionKey, context, true, "escalated to owner", request.RequestId);
                diagnostics.Add($"ManagerDecisionExecution[Borrow]: borrowing {amountCents}c needs owner approval — pending request {request.RequestId} created.");
                return new ManagerExecutionOutcome { Pending = true, Reason = "owner approval required", ResultingReference = request.RequestId };
            }

            // Non-escalated borrowing: only within delegated limits, only with
            // a real funded lender.
            if (ledger.HasExecuted(actionKey))
                return Fail("this borrowing was already executed today — refusing a duplicate loan", diagnostics);

            if (!HasScope(context, ManagerScopeNames.CashHandling))
                return Fail("the manager does not hold the 'CashHandling' scope", diagnostics);

            if (amountCents > Math.Max(0, context.MaxBorrowingCents))
            {
                RecordAttempt(actionKey, context, false, $"borrowing {amountCents}c exceeds the owner's max borrowing permission ({context.MaxBorrowingCents}c)", string.Empty);
                return Fail($"borrowing {amountCents}c exceeds the owner's max borrowing permission ({context.MaxBorrowingCents}c)", diagnostics);
            }

            if (amountCents >= ManagerBrain.MajorBorrowingThresholdCents)
            {
                RecordAttempt(actionKey, context, false, $"borrowing {amountCents}c is major borrowing — owner-reserved", string.Empty);
                return Fail($"borrowing {amountCents}c is major borrowing (>= {ManagerBrain.MajorBorrowingThresholdCents}c) — owner-reserved", diagnostics);
            }

            if (services.FinancialObligations == null || services.Ids == null)
                return Fail("the financial obligation authority is not wired", diagnostics);

            if (services.BusinessLender == null)
            {
                RecordAttempt(actionKey, context, false, "no business lender arranged", string.Empty);
                return Fail("no business lender arranged — borrowing cannot be funded with real cash", diagnostics);
            }

            FinancialObligation obligation = services.BusinessLender.TryArrangeLoan(
                services.Ids, services.FinancialObligations, business.InstanceId, amountCents,
                context.DayIndex, diagnostics, out string refusalReason);
            if (obligation == null)
            {
                RecordAttempt(actionKey, context, false, "lender refused: " + refusalReason, string.Empty);
                return Fail("lender refused: " + refusalReason, diagnostics);
            }

            string fundReason = AdjustCash(business, amountCents);
            if (fundReason != null)
            {
                RecordAttempt(actionKey, context, false, "loan funded but cash credit refused: " + fundReason, obligation.ObligationId);
                return Fail("loan obligation recorded but the business cash credit was refused: " + fundReason, diagnostics);
            }

            string reference = $"borrow:{business.InstanceId}:d{context.DayIndex}:{obligation.ObligationId}";
            RecordAttempt(actionKey, context, true, string.Empty, reference);
            diagnostics.Add($"ManagerDecisionExecution[Borrow]: {services.BusinessLender.LenderName} lent {amountCents}c to {business.InstanceId} (obligation {obligation.ObligationId}).");
            return new ManagerExecutionOutcome { Executed = true, ResultingReference = reference };
        }

        private static int ParseAmountCents(ManagerDecisionResult decision)
        {
            // The brain encodes the amount in the resulting reference:
            // "intent:borrow-request:{business}:d{day}:{amount}c".
            if (decision == null || string.IsNullOrWhiteSpace(decision.ResultingReference))
                return 0;
            string reference = decision.ResultingReference;
            int lastColon = reference.LastIndexOf(':');
            if (lastColon < 0 || lastColon + 1 >= reference.Length)
                return 0;
            string tail = reference.Substring(lastColon + 1);
            if (tail.EndsWith("c", StringComparison.Ordinal))
                tail = tail.Substring(0, tail.Length - 1);
            return int.TryParse(tail, out int amount) ? Math.Max(0, amount) : 0;
        }
    }

    /// <summary>
    /// SellAsset: asset sales are owner-reserved — the brain always escalates
    /// them, and this executor honors that. The honest outcome is a legitimate
    /// pending owner-approval request naming the asset and price. The manager
    /// never sells business assets unilaterally.
    /// </summary>
    public sealed class ManagerSellAssetExecutor : ManagerDecisionExecutorBase
    {
        public ManagerSellAssetExecutor(ManagerExecutionServices services, ManagerDecisionExecutionLedger ledger)
            : base(services, ledger) { }

        public override string DecisionKind => ManagerActKinds.SellAsset;

        public override ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BusinessInstanceState business = FindBusiness(context);
            if (business == null)
                return Fail("unknown business '" + (context != null ? context.BusinessInstanceId : "?") + "'", diagnostics);

            int amountCents = ParseAmountCents(decision);
            // Pass IX WS5: the decision names the specific asset (coordinator
            // decision 3); it rides the request so the owner-resolution
            // appraisal and the execution step act on THAT asset.
            string assetId = decision != null ? decision.ProposedAssetId ?? string.Empty : string.Empty;
            // Day-independent deduplication key: retries find the pending request.
            string requestKey = string.IsNullOrWhiteSpace(assetId)
                ? $"SellAsset:{business.InstanceId}:{amountCents}c"
                : $"SellAsset:{business.InstanceId}:{assetId}:{amountCents}c";
            ManagerPendingRequest existing = ledger.FindPending(requestKey);
            if (existing != null && string.Equals(existing.Status, "Pending", StringComparison.Ordinal))
            {
                diagnostics.Add($"ManagerDecisionExecution[SellAsset]: owner-approval request {existing.RequestId} already pending — not duplicated.");
                return new ManagerExecutionOutcome { Pending = true, Reason = "owner approval already pending", ResultingReference = existing.RequestId };
            }

            var request = new ManagerPendingRequest
            {
                RequestId = $"mgr-req:sellasset:{business.InstanceId}:d{context.DayIndex}:{amountCents}c",
                RequestKey = requestKey,
                BusinessInstanceId = business.InstanceId,
                Kind = ManagerActKinds.SellAsset,
                AmountCents = amountCents,
                AssetId = assetId,
                Detail = "asset sale is owner-reserved — the manager cannot sell business assets unilaterally",
                CreatedDayIndex = context.DayIndex,
                ManagerPersonId = context.ManagerPersonId,
                Status = "Pending",
            };
            ledger.AddPending(request);
            RecordAttempt(ActionKey(context, amountCents + "c"), context, true, "escalated to owner", request.RequestId);
            diagnostics.Add($"ManagerDecisionExecution[SellAsset]: asset sale ({amountCents}c) is owner-reserved — pending request {request.RequestId} created.");
            return new ManagerExecutionOutcome { Pending = true, Reason = "asset sale is owner-reserved", ResultingReference = request.RequestId };
        }

        private static int ParseAmountCents(ManagerDecisionResult decision)
        {
            // "intent:SellAsset:{business}:d{day}" carries no amount in the
            // current brain; ProposedAssetSaleCents is the evidence source.
            // Parse defensively; 0 means "price to be set by the owner".
            if (decision == null || string.IsNullOrWhiteSpace(decision.ResultingReference))
                return 0;
            string reference = decision.ResultingReference;
            int lastColon = reference.LastIndexOf(':');
            if (lastColon < 0 || lastColon + 1 >= reference.Length)
                return 0;
            string tail = reference.Substring(lastColon + 1);
            if (tail.EndsWith("c", StringComparison.Ordinal))
                tail = tail.Substring(0, tail.Length - 1);
            if (tail.StartsWith("d", StringComparison.Ordinal))
                return 0;
            return int.TryParse(tail, out int amount) ? Math.Max(0, amount) : 0;
        }
    }

    /// <summary>
    /// Shared task-start drive: reserves person time, assigns and starts the
    /// scheduler's assignments through the real authorities. Used by the
    /// manager Schedule executor and by SimulationDrivers' blanket scheduling
    /// pass — one implementation, no drift.
    /// </summary>
    public static class ManagerWorkSchedulerDrive
    {
        /// <summary>Builds the stable task reservation label for a work task.</summary>
        public static string DefaultTaskLabel(WorkTask task)
        {
            return task == null ? string.Empty : $"task:{task.TaskId}|{task.DefinitionId}";
        }

        /// <summary>
        /// Starts the scheduler's assignments. Returns the number of tasks
        /// actually started. Refusals are reported in diagnostics, never
        /// thrown.
        /// </summary>
        public static int StartAssignedTasks(
            WorkAssignmentReport report,
            Dictionary<string, WorkTask> taskByTextId,
            TaskAuthority tasks,
            PersonScheduleTracker schedules,
            WorkTimeBudgetStore budgets,
            int dayIndex,
            List<string> diagnostics,
            Func<WorkTask, string> reservationLabel,
            Action<EntityId, string> onStarted)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (report == null || taskByTextId == null || tasks == null || schedules == null || budgets == null)
                return 0;

            var nextStartByPerson = new Dictionary<int, int>();
            int started = 0;
            foreach (WorkAssignment assignment in report.Assignments)
            {
                if (assignment == null || assignment.PersonId < 0 || !taskByTextId.TryGetValue(assignment.TaskId, out WorkTask task))
                    continue;

                int startMinute = nextStartByPerson.TryGetValue(assignment.PersonId, out int prior)
                    ? prior : 8 * 60;
                string label = reservationLabel != null ? reservationLabel(task) : DefaultTaskLabel(task);
                string reservation = schedules.TryReserve(
                    assignment.PersonId, PersonActivityKind.Work, dayIndex, startMinute,
                    assignment.MinutesCommitted, label);
                if (reservation != null)
                {
                    diagnostics.Add($"task {task.TaskId}: {reservation}");
                    continue;
                }

                // PersonScheduleTracker returns null on success (the return
                // value is reserved for a refusal reason). Resolve the real
                // persisted reservation that was just created so completion
                // can release it through the same authority.
                string reservationId = null;
                List<PersonActivityReservation> activeReservations = schedules.ActiveFor(assignment.PersonId, dayIndex);
                for (int i = activeReservations.Count - 1; i >= 0; i--)
                {
                    PersonActivityReservation candidate = activeReservations[i];
                    if (candidate != null &&
                        candidate.Activity == PersonActivityKind.Work &&
                        string.Equals(candidate.Label, label, StringComparison.Ordinal))
                    {
                        reservationId = candidate.ReservationId;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(reservationId))
                {
                    diagnostics.Add($"task {task.TaskId}: schedule reservation was not materialized");
                    continue;
                }

                EntityId person = EntityId.For(EntityKind.Person, assignment.PersonId);
                string assignReason = null;
                string startReason = null;
                bool assigned = tasks.AssignTask(task.TaskId, person, budgets, out assignReason);
                bool startedTask = assigned && tasks.StartTask(task.TaskId, dayIndex, out startReason);
                if (!assigned || !startedTask)
                {
                    schedules.Release(reservationId);
                    diagnostics.Add($"task {task.TaskId}: assignment refused ({assignReason ?? startReason})");
                    continue;
                }

                if (onStarted != null)
                    onStarted(task.TaskId, reservationId);
                started++;
                nextStartByPerson[assignment.PersonId] = startMinute + Math.Max(1, assignment.MinutesCommitted);
            }

            return started;
        }
    }
}
