using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P2-1: household errand coordination. The household decides what
    /// it needs; the coordinator batches open needs into <see cref="HouseholdErrandPlan"/>
    /// records (one trip serving several needs where feasible), assigns exactly
    /// one responsible Person per plan, and reserves that person's time through
    /// the shared PersonScheduleTracker.
    ///
    /// No duplicate purchases: a need claimed by an active plan is never
    /// re-planned; the need registry's one-open-need-per-item invariant is
    /// preserved; fulfillment decrements each need exactly once through the
    /// shared shopping-loop settlement unit.
    ///
    /// If the responsible member becomes unavailable (death, schedule conflict,
    /// lost reservation), <see cref="ReassignIfUnavailable"/> moves the plan to
    /// another available member: the old time reservation is released (no
    /// duplicated time) and already-fulfilled needs are dropped from the plan
    /// (no duplicated purchases). Mid-trip unavailability belongs to the
    /// real-time ShoppingTripDriver; this coordinator owns planned trips.
    /// </summary>
    public sealed class HouseholdErrandCoordinator
    {
        /// <summary>Everything the coordinator needs from the hosting game.</summary>
        public sealed class CoordinatorContext
        {
            public PopulationState Population;
            public HouseholdMembershipRegistry Memberships;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public PersonScheduleTracker Schedules;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public Func<int, EntityId> PersonEntityId;
        }

        private readonly HouseholdErrandPlanRegistry plans = new HouseholdErrandPlanRegistry();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public HouseholdErrandPlanRegistry Plans => plans;

        /// <summary>
        /// Plans one day of errands for a household: every open need that is
        /// not already claimed by an active plan goes into exactly one new
        /// plan. Returns the new plans (empty when nothing is open).
        /// Planning claims the needs; execution happens via ExecutePlan.
        /// </summary>
        public List<HouseholdErrandPlan> PlanDay(
            int householdId,
            int dayIndex,
            CoordinatorContext ctx,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var created = new List<HouseholdErrandPlan>();
            if (ctx == null || ctx.Needs == null)
            {
                diag.Add($"ErrandCoordinator: no context/needs for H{householdId} — nothing planned.");
                return created;
            }

            List<HouseholdPurchasingNeed> open = ctx.Needs.GetOpenNeeds(householdId);
            var unclaimed = new List<HouseholdPurchasingNeed>();
            foreach (HouseholdPurchasingNeed need in open)
            {
                if (need == null) continue;
                if (plans.IsNeedClaimed(householdId, need.NeedSequence)) continue;
                unclaimed.Add(need);
            }

            if (unclaimed.Count == 0) return created;

            HouseholdErrandPlan plan = plans.Create(householdId, Math.Max(0, dayIndex));
            var needIds = new List<string>();
            foreach (HouseholdPurchasingNeed need in unclaimed)
            {
                if (plans.Claim(plan.PlanId, householdId, need.NeedSequence))
                {
                    needIds.Add(need.NeedSequence.ToString());
                }
            }

            // Proposed until a responsible person is assigned at execution;
            // the claim (not the plan record) carries the need ids until then.
            diag.Add($"ErrandCoordinator: plan {plan.PlanId} proposed for H{householdId} " +
                $"covering {needIds.Count} need(s) ({string.Join(",", needIds)}).");
            created.Add(plan);
            return created;
        }

        /// <summary>
        /// Executes an assigned-or-proposed plan as ONE batched trip through the
        /// shopping loop. Assigns the responsible person (ShopperAssignmentPolicy)
        /// when the plan is still Proposed; reserves the trip's person-time
        /// window; the reservation id is stored on the plan. Needs the trip
        /// cannot serve stay open and are released from the claim.
        /// </summary>
        public HouseholdShoppingLoop.BatchShoppingResult ExecutePlan(
            HouseholdErrandPlan plan,
            int dayIndex,
            CoordinatorContext ctx,
            HouseholdShoppingLoop loop,
            ShoppingLoopOptions options,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (plan == null || ctx == null || loop == null)
            {
                diag.Add("ErrandCoordinator: cannot execute — plan, context, or loop missing.");
                return null;
            }

            if (plan.Status != HouseholdErrandPlanStatus.Proposed
                && plan.Status != HouseholdErrandPlanStatus.Assigned)
            {
                diag.Add($"ErrandCoordinator: plan {plan.PlanId} is {plan.Status} — not executed.");
                return null;
            }

            options ??= new ShoppingLoopOptions();
            options.Diagnostics = diag;

            // The claim set is the plan's need list until Assign binds it.
            var sequences = new List<int>(plans.ClaimedNeedSequences(plan.PlanId));
            if (sequences.Count == 0 && plan.NeedIds != null)
            {
                foreach (string needId in plan.NeedIds)
                {
                    if (int.TryParse(needId, out int sequence)) sequences.Add(sequence);
                }
            }

            var needs = new List<HouseholdPurchasingNeed>();
            foreach (int sequence in sequences)
            {
                HouseholdPurchasingNeed need = FindNeed(ctx, plan.HouseholdId, sequence);
                if (need != null && need.Status == PurchasingNeedStatus.Open && need.UnitsNeeded > 0)
                {
                    needs.Add(need);
                }
                else
                {
                    plans.ReleaseClaim(plan.PlanId, sequence);
                }
            }

            if (needs.Count == 0)
            {
                plan.MarkCompleted();
                plans.NoteTerminal(plan.PlanId);
                diag.Add($"ErrandCoordinator: plan {plan.PlanId} completed — no open needs left to serve.");
                return new HouseholdShoppingLoop.BatchShoppingResult
                {
                    PlanId = plan.PlanId,
                    HouseholdId = plan.HouseholdId,
                    DayIndex = Math.Max(0, dayIndex),
                    TripExecuted = false,
                };
            }

            HouseholdShoppingLoop.BatchShoppingResult batch =
                loop.ExecuteBatch(needs, dayIndex, options, plan.PlanId);
            if (batch == null)
            {
                plan.MarkFailed();
                return null;
            }

            // Release the claim on needs the trip could not serve; they stay
            // open for a later plan. Served needs are fulfilled already.
            foreach (HouseholdPurchasingNeed unbatched in batch.UnbatchedNeeds)
            {
                if (unbatched != null)
                {
                    plans.ReleaseClaim(plan.PlanId, unbatched.NeedSequence);
                }
            }

            // The plan record carries the still-claimed (served or servable)
            // need ids; the reservation id is the real person-time window.
            var finalIds = new List<string>();
            foreach (int sequence in plans.ClaimedNeedSequences(plan.PlanId))
            {
                finalIds.Add(sequence.ToString());
            }

            if (batch.ShopperPersonId >= 0)
            {
                plan.Assign(batch.ShopperPersonId, finalIds, batch.ReservationId ?? string.Empty);
                plans.NoteEstimate(plan.PlanId, batch.EstimatedMinutes, options.DepartureMinuteOfDay);
            }

            if (batch.TripExecuted && batch.ServedNeedCount > 0)
            {
                plan.MarkCompleted();
                plans.NoteTerminal(plan.PlanId);
                diag.Add($"ErrandCoordinator: plan {plan.PlanId} completed — " +
                    $"{batch.ServedNeedCount} need(s) served, {batch.UnitsAcquiredTotal}u acquired.");
            }
            else
            {
                plan.MarkFailed();
                plans.NoteTerminal(plan.PlanId);
                diag.Add($"ErrandCoordinator: plan {plan.PlanId} failed — {batch.FailureReason} Needs stay open, claims released.");
            }

            return batch;
        }

        /// <summary>
        /// Pass V P2-1: the responsible member became unavailable (or may have).
        /// Checks the plan's executor: deceased, lost reservation, or a schedule
        /// conflict over the plan's window. When the executor is still available
        /// this is a no-op. Otherwise the plan moves to another available member:
        /// the old reservation is released (no duplicated time), fulfilled needs
        /// are dropped from the plan (no duplicated purchases), and the new
        /// reservation id is stored on the plan. Returns null when no
        /// reassignment was needed, else a diagnostic line.
        /// </summary>
        public string ReassignIfUnavailable(
            string planId,
            int dayIndex,
            CoordinatorContext ctx,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(planId) || ctx == null)
            {
                return "ReassignIfUnavailable: no plan id or context.";
            }

            HouseholdErrandPlan plan = plans.Get(planId);
            if (plan == null)
            {
                return $"ReassignIfUnavailable: unknown plan '{planId}'.";
            }

            if (plan.Status != HouseholdErrandPlanStatus.Assigned
                && plan.Status != HouseholdErrandPlanStatus.Proposed)
            {
                return $"ReassignIfUnavailable: plan {planId} is {plan.Status} — terminal plans are not reassigned.";
            }

            int personId = plan.ResponsiblePersonId;
            string unavailability = CheckUnavailable(plan, personId, dayIndex, ctx);
            if (unavailability == null)
            {
                return null; // executor still available — no-op.
            }

            diag.Add($"ErrandCoordinator: plan {planId} executor P{personId} unavailable ({unavailability}) — reassigning.");

            // Release the old time first: person-time is exclusive, never double-held.
            if (!string.IsNullOrEmpty(plan.ReservationId) && ctx.Schedules != null)
            {
                ctx.Schedules.Release(plan.ReservationId);
                diag.Add($"ErrandCoordinator: released P{personId}'s reservation {plan.ReservationId}.");
            }

            // Drop fulfilled needs from the plan: never buy twice.
            var remainingIds = new List<string>();
            var claimedSequences = new List<int>(plans.ClaimedNeedSequences(planId));
            if (claimedSequences.Count == 0 && plan.NeedIds != null)
            {
                foreach (string needId in plan.NeedIds)
                {
                    if (int.TryParse(needId, out int sequence)) claimedSequences.Add(sequence);
                }
            }

            foreach (int sequence in claimedSequences)
            {
                HouseholdPurchasingNeed need = FindNeed(ctx, plan.HouseholdId, sequence);
                if (need != null && need.Status == PurchasingNeedStatus.Open && need.UnitsNeeded > 0)
                {
                    remainingIds.Add(sequence.ToString());
                }
                else
                {
                    plans.ReleaseClaim(planId, sequence);
                }
            }

            if (remainingIds.Count == 0)
            {
                plan.MarkCompleted();
                plans.NoteTerminal(planId);
                diag.Add($"ErrandCoordinator: plan {planId} completed on reassignment — nothing left to buy.");
                return $"plan {planId}: executor unavailable ({unavailability}); no open needs remained — plan completed.";
            }

            List<int> members = ctx.Memberships != null
                ? ctx.Memberships.GetActiveMembers(plan.HouseholdId)
                : new List<int>();
            members.Remove(personId);

            int estimate = Math.Max(30, plans.GetEstimateMinutes(planId));
            int departure = plans.GetDepartureMinute(planId);
            int shopperId = ShopperAssignmentPolicy.ChooseShopper(
                ctx.Population, members, dayIndex, departure, estimate,
                ctx.Schedules, ctx.Budgets,
                ctx.PersonEntityId ?? (pid => EntityId.For(EntityKind.Person, pid)),
                out string shopperReason);
            if (shopperId < 0)
            {
                plan.MarkFailed();
                plans.NoteTerminal(planId);
                diag.Add($"ErrandCoordinator: plan {planId} failed — no replacement shopper ({shopperReason}). Claims released.");
                return $"plan {planId}: executor unavailable ({unavailability}); no replacement shopper — plan failed, needs stay open.";
            }

            string refusal = ctx.Schedules != null
                ? ctx.Schedules.TryReserve(shopperId, PersonActivityKind.Shopping, dayIndex,
                    departure, estimate, $"reassigned errand plan {planId} for H{plan.HouseholdId}")
                : "no schedule tracker";
            if (refusal != null)
            {
                plan.MarkFailed();
                plans.NoteTerminal(planId);
                diag.Add($"ErrandCoordinator: plan {planId} failed — replacement window refused ({refusal}). Claims released.");
                return $"plan {planId}: executor unavailable ({unavailability}); replacement window refused — plan failed.";
            }

            string reservationId = FindReservationId(ctx.Schedules, shopperId, dayIndex);
            plan.Assign(shopperId, remainingIds, reservationId);
            diag.Add($"ErrandCoordinator: plan {planId} reassigned to P{shopperId} " +
                $"(reservation {reservationId}, {remainingIds.Count} need(s) remaining).");
            return $"plan {planId}: reassigned from P{personId} ({unavailability}) to P{shopperId}; " +
                $"{remainingIds.Count} need(s) carried, fulfilled needs dropped, old reservation released.";
        }

        private string CheckUnavailable(HouseholdErrandPlan plan, int personId, int dayIndex, CoordinatorContext ctx)
        {
            if (personId < 0)
            {
                return "no responsible person assigned yet";
            }

            PersonState person = ctx.Population != null ? ctx.Population.GetPerson(personId) : null;
            if (person == null)
            {
                return $"P{personId} is not a known person";
            }

            if (person.deathDayIndex >= 0)
            {
                return $"P{personId} is deceased";
            }

            if (ctx.Schedules == null)
            {
                return "no schedule tracker to verify against";
            }

            // The reservation must still be live.
            bool reservationLive = false;
            if (!string.IsNullOrEmpty(plan.ReservationId))
            {
                List<PersonActivityReservation> active = ctx.Schedules.ActiveFor(personId, dayIndex);
                foreach (PersonActivityReservation reservation in active)
                {
                    if (reservation != null && !reservation.Released
                        && string.Equals(reservation.ReservationId, plan.ReservationId, StringComparison.Ordinal))
                    {
                        reservationLive = true;
                        break;
                    }
                }
            }

            if (!reservationLive)
            {
                return $"reservation {plan.ReservationId} is no longer live";
            }

            int estimate = Math.Max(30, plans.GetEstimateMinutes(plan.PlanId));
            int departure = plans.GetDepartureMinute(plan.PlanId);
            int windowEnd = departure + estimate;
            List<PersonActivityReservation> dayReservations = ctx.Schedules.ActiveFor(personId, dayIndex);
            foreach (PersonActivityReservation reservation in dayReservations)
            {
                if (reservation == null || reservation.Released) continue;
                // The plan's own window does not conflict with itself.
                if (string.Equals(reservation.ReservationId, plan.ReservationId, StringComparison.Ordinal)) continue;
                int otherEnd = reservation.StartMinuteOfDay + Math.Max(1, reservation.DurationMinutes);
                bool overlaps = reservation.StartMinuteOfDay < windowEnd && departure < otherEnd;
                if (overlaps)
                {
                    return $"schedule conflict over the plan window ({reservation.Label})";
                }
            }

            return null;
        }

        private static string FindReservationId(PersonScheduleTracker schedules, int personId, int dayIndex)
        {
            if (schedules == null) return string.Empty;
            List<PersonActivityReservation> active = schedules.ActiveFor(personId, dayIndex);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] != null && active[i].Activity == PersonActivityKind.Shopping && !active[i].Released)
                {
                    return active[i].ReservationId;
                }
            }

            return string.Empty;
        }

        private HouseholdPurchasingNeed FindNeed(CoordinatorContext ctx, int householdId, int needSequence)
        {
            if (ctx.Needs == null) return null;
            foreach (HouseholdPurchasingNeed need in ctx.Needs.GetOpenNeeds(householdId))
            {
                if (need != null && need.NeedSequence == needSequence) return need;
            }

            return null;
        }
    }

    /// <summary>
    /// Pass V P2-1: the plan registry. One plan per household per day in the
    /// common case; needs are claimed by at most one active plan, so two plans
    /// never buy the same goods. Clear-then-load: reload restores plans and
    /// claims without re-executing anything.
    /// </summary>
    public sealed class HouseholdErrandPlanRegistry
    {
        [SerializeField] private List<HouseholdErrandPlan> plans = new List<HouseholdErrandPlan>();
        [SerializeField] private int nextPlanSequence;
        // planId -> "householdId:needSequence" claimed needs (active plans only).
        private readonly Dictionary<string, HashSet<string>> claims = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> estimateMinutes = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> departureMinutes = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> planDays = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<HouseholdErrandPlan> All => plans;

        public HouseholdErrandPlan Create(int householdId, int dayIndex)
        {
            var plan = new HouseholdErrandPlan($"errand-{householdId}-{Math.Max(0, dayIndex)}-{nextPlanSequence++}", householdId);
            plans.Add(plan);
            claims[plan.PlanId] = new HashSet<string>(StringComparer.Ordinal);
            planDays[plan.PlanId] = Math.Max(0, dayIndex);
            return plan;
        }

        public HouseholdErrandPlan Get(string planId)
        {
            if (string.IsNullOrWhiteSpace(planId)) return null;
            foreach (HouseholdErrandPlan plan in plans)
            {
                if (plan != null && string.Equals(plan.PlanId, planId, StringComparison.Ordinal)) return plan;
            }

            return null;
        }

        public List<HouseholdErrandPlan> ActiveFor(int householdId)
        {
            var result = new List<HouseholdErrandPlan>();
            foreach (HouseholdErrandPlan plan in plans)
            {
                if (plan == null || plan.HouseholdId != householdId) continue;
                if (plan.Status == HouseholdErrandPlanStatus.Proposed || plan.Status == HouseholdErrandPlanStatus.Assigned)
                {
                    result.Add(plan);
                }
            }

            return result;
        }

        /// <summary>Claims a need for a plan. Returns false when another active plan already claims it.</summary>
        public bool Claim(string planId, int householdId, int needSequence)
        {
            HouseholdErrandPlan plan = Get(planId);
            if (plan == null) return false;
            string key = householdId + ":" + needSequence;
            foreach (KeyValuePair<string, HashSet<string>> entry in claims)
            {
                if (string.Equals(entry.Key, planId, StringComparison.Ordinal)) continue;
                HouseholdErrandPlan other = Get(entry.Key);
                if (other == null) continue;
                if (other.Status != HouseholdErrandPlanStatus.Proposed && other.Status != HouseholdErrandPlanStatus.Assigned) continue;
                if (entry.Value.Contains(key)) return false;
            }

            if (!claims.TryGetValue(planId, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                claims[planId] = set;
            }

            set.Add(key);
            return true;
        }

        public bool IsNeedClaimed(int householdId, int needSequence)
        {
            string key = householdId + ":" + needSequence;
            foreach (KeyValuePair<string, HashSet<string>> entry in claims)
            {
                HouseholdErrandPlan plan = Get(entry.Key);
                if (plan == null) continue;
                if (plan.Status != HouseholdErrandPlanStatus.Proposed && plan.Status != HouseholdErrandPlanStatus.Assigned) continue;
                if (entry.Value.Contains(key)) return true;
            }

            return false;
        }

        /// <summary>Need sequences currently claimed by a plan (its working need list).</summary>
        public List<int> ClaimedNeedSequences(string planId)
        {
            var result = new List<int>();
            HouseholdErrandPlan plan = Get(planId);
            if (plan == null) return result;
            if (claims.TryGetValue(planId, out HashSet<string> set))
            {
                foreach (string key in set)
                {
                    string[] parts = key.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int householdId)
                        && householdId == plan.HouseholdId && int.TryParse(parts[1], out int sequence))
                    {
                        result.Add(sequence);
                    }
                }
            }

            result.Sort();
            return result;
        }

        /// <summary>Drops a terminal plan's claim set (Completed/Failed/Cancelled).</summary>
        public void NoteTerminal(string planId)
        {
            if (!string.IsNullOrWhiteSpace(planId)) claims.Remove(planId);
        }

        public void ReleaseClaim(string planId, int needSequence)
        {
            HouseholdErrandPlan plan = Get(planId);
            if (plan == null) return;
            if (claims.TryGetValue(planId, out HashSet<string> set))
            {
                set.Remove(plan.HouseholdId + ":" + needSequence);
            }
        }

        public void NoteEstimate(string planId, int estimatedMinutes, int departureMinute)
        {
            if (string.IsNullOrWhiteSpace(planId)) return;
            estimateMinutes[planId] = Math.Max(1, estimatedMinutes);
            departureMinutes[planId] = Math.Max(0, departureMinute);
        }

        public int GetEstimateMinutes(string planId)
        {
            return estimateMinutes.TryGetValue(planId, out int minutes) ? minutes : 60;
        }

        public int GetDepartureMinute(string planId)
        {
            return departureMinutes.TryGetValue(planId, out int minute) ? minute : 600;
        }

        public int GetPlanDay(string planId)
        {
            return planDays.TryGetValue(planId, out int day) ? day : 0;
        }

        /// <summary>Captures plans AND their claim sets for the save pipeline.</summary>
        public HouseholdErrandPlanRegistrySaveDto CaptureSaveDto()
        {
            var dto = new HouseholdErrandPlanRegistrySaveDto { NextPlanSequence = nextPlanSequence };
            dto.Plans.AddRange(plans);
            foreach (KeyValuePair<string, HashSet<string>> entry in claims)
            {
                if (entry.Value == null) continue;
                foreach (string needKey in entry.Value)
                {
                    dto.Claims.Add(new HouseholdErrandPlanClaimEntry { PlanId = entry.Key, NeedKey = needKey });
                }
            }
            foreach (KeyValuePair<string, int> entry in estimateMinutes)
            {
                dto.EstimateMinutes.Add(new HouseholdErrandPlanIntEntry { Key = entry.Key, Value = entry.Value });
            }

            foreach (KeyValuePair<string, int> entry in departureMinutes)
            {
                dto.DepartureMinutes.Add(new HouseholdErrandPlanIntEntry { Key = entry.Key, Value = entry.Value });
            }

            foreach (KeyValuePair<string, int> entry in planDays)
            {
                dto.PlanDays.Add(new HouseholdErrandPlanIntEntry { Key = entry.Key, Value = entry.Value });
            }

            return dto;
        }

        /// <summary>Clear-then-load: plans restore as records; claims rebuild
        /// from each active plan's need ids (no re-execution, no double claim).</summary>
        public void LoadFromSaveDto(HouseholdErrandPlanRegistrySaveDto dto)
        {
            plans.Clear();
            claims.Clear();
            estimateMinutes.Clear();
            departureMinutes.Clear();
            planDays.Clear();
            nextPlanSequence = 0;
            if (dto == null) return;
            nextPlanSequence = Math.Max(0, dto.NextPlanSequence);
            if (dto.Plans != null)
            {
                foreach (HouseholdErrandPlan plan in dto.Plans)
                {
                    if (plan == null) continue;
                    plans.Add(plan);
                }
            }

            // Claims restore from the persisted claim sets (the plan record's
            // need ids are only bound at Assign time; Proposed plans carry
            // their needs in the claim set).
            if (dto.Claims != null)
            {
                foreach (HouseholdErrandPlanClaimEntry entry in dto.Claims)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.PlanId)
                        || string.IsNullOrWhiteSpace(entry.NeedKey))
                    {
                        continue;
                    }

                    HouseholdErrandPlan plan = Get(entry.PlanId);
                    if (plan == null) continue;
                    if (plan.Status != HouseholdErrandPlanStatus.Proposed
                        && plan.Status != HouseholdErrandPlanStatus.Assigned)
                    {
                        continue;
                    }

                    if (!claims.TryGetValue(entry.PlanId, out HashSet<string> set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        claims[entry.PlanId] = set;
                    }

                    set.Add(entry.NeedKey);
                }
            }

            if (dto.EstimateMinutes != null)
            {
                foreach (HouseholdErrandPlanIntEntry entry in dto.EstimateMinutes)
                {
                    if (entry != null) estimateMinutes[entry.Key] = entry.Value;
                }
            }

            if (dto.DepartureMinutes != null)
            {
                foreach (HouseholdErrandPlanIntEntry entry in dto.DepartureMinutes)
                {
                    if (entry != null) departureMinutes[entry.Key] = entry.Value;
                }
            }

            if (dto.PlanDays != null)
            {
                foreach (HouseholdErrandPlanIntEntry entry in dto.PlanDays)
                {
                    if (entry != null) planDays[entry.Key] = entry.Value;
                }
            }
        }
    }

    [Serializable]
    public sealed class HouseholdErrandPlanIntEntry
    {
        public string Key = string.Empty;
        public int Value;
    }

    [Serializable]
    public sealed class HouseholdErrandPlanClaimEntry
    {
        public string PlanId = string.Empty;
        public string NeedKey = string.Empty; // "householdId:needSequence"
    }

    /// <summary>Save data for the errand plan registry.</summary>
    [Serializable]
    public sealed class HouseholdErrandPlanRegistrySaveDto
    {
        public int NextPlanSequence;
        public List<HouseholdErrandPlan> Plans = new List<HouseholdErrandPlan>();
        public List<HouseholdErrandPlanClaimEntry> Claims = new List<HouseholdErrandPlanClaimEntry>();
        public List<HouseholdErrandPlanIntEntry> EstimateMinutes = new List<HouseholdErrandPlanIntEntry>();
        public List<HouseholdErrandPlanIntEntry> DepartureMinutes = new List<HouseholdErrandPlanIntEntry>();
        public List<HouseholdErrandPlanIntEntry> PlanDays = new List<HouseholdErrandPlanIntEntry>();
    }
}
