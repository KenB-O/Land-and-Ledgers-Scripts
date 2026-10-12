// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice D tests: forge repair chain.
//
// Real reconciliation figures: coal/iron consumed, repair minutes (causal),
// quality deltas, rework tasks, forge reservations held/released.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Skills;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Generic;
using LandLedgers.Economy.Businesses.Blacksmith;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class ForgeRepairChainTests
    {
        #region Test doubles

        private sealed class TestDueEventScheduler : IDueEventScheduler
        {
            private readonly List<DueEvent> events = new List<DueEvent>();
            private int sequence;
            public string Schedule(DueEventKind kind, float dueGameSeconds, string payloadRef, out string eventId)
            {
                eventId = "due-" + (sequence++);
                events.Add(new DueEvent(eventId, kind, dueGameSeconds, payloadRef, sequence));
                return null;
            }
            public void Cancel(string eventId) => events.RemoveAll(e => e.EventId == eventId);
            public void CancelForPayload(string payloadRef) => events.RemoveAll(e => e.PayloadRef == payloadRef);
            public List<DueEvent> PopDue(float currentGameSeconds)
            {
                var popped = events.FindAll(e => e.DueGameSeconds <= currentGameSeconds);
                events.RemoveAll(e => e.DueGameSeconds <= currentGameSeconds);
                popped.Sort((a, b) => a.DueGameSeconds.CompareTo(b.DueGameSeconds));
                return popped;
            }
            public DueEvent PeekNext()
            {
                DueEvent next = null;
                foreach (DueEvent e in events)
                    if (next == null || e.DueGameSeconds < next.DueGameSeconds) next = e;
                return next;
            }
            public int ScheduledCount => events.Count;
            public int PendingCount => events.Count;
        }

        private sealed class TestReservationAuthority : IUnifiedReservationAuthority
        {
            private readonly Dictionary<string, ReservationHandle> live = new Dictionary<string, ReservationHandle>();
            private readonly Dictionary<string, float> capacities = new Dictionary<string, float>(StringComparer.Ordinal);
            private int sequence;
            public void SetCapacity(string key, float cap) => capacities[key] = Math.Max(0f, cap);
            public string TryReserveAll(ReservationRequest request, out ReservationHandle handle)
            {
                handle = null;
                if (request == null || request.Items.Count == 0)
                {
                    handle = new ReservationHandle("resv-" + sequence++, request?.OwnerTaskId ?? string.Empty,
                        new List<ReservationItem>());
                    live[handle.ReservationId] = handle;
                    return null;
                }
                foreach (ReservationItem item in request.Items)
                {
                    float cap = capacities.TryGetValue(item.ResourceKey, out float c) ? c : 1f;
                    float used = 0f;
                    foreach (ReservationHandle h in live.Values)
                        foreach (ReservationItem it in h.Items)
                            if (string.Equals(it.ResourceKey, item.ResourceKey, StringComparison.Ordinal)
                                && it.StartGameSeconds < item.EndGameSeconds && item.StartGameSeconds < it.EndGameSeconds)
                                used += it.Quantity;
                    if (used + item.Quantity > cap + 0.001f)
                        return "TestReservationAuthority: '" + item.ResourceKey + "' over capacity — refused loudly.";
                }
                handle = new ReservationHandle("resv-" + sequence++, request.OwnerTaskId,
                    new List<ReservationItem>(request.Items));
                live[handle.ReservationId] = handle;
                return null;
            }
            public void Release(string reservationId) => live.Remove(reservationId);
            public void ReleaseAllForTask(string ownerTaskId)
            {
                var doomed = new List<string>();
                foreach (var kvp in live)
                    if (string.Equals(kvp.Value.OwnerTaskId, ownerTaskId, StringComparison.Ordinal))
                        doomed.Add(kvp.Key);
                foreach (string id in doomed) live.Remove(id);
            }
            public bool IsReserved(string resourceKey, float atGameSeconds)
            {
                foreach (ReservationHandle h in live.Values)
                    foreach (ReservationItem it in h.Items)
                        if (string.Equals(it.ResourceKey, resourceKey, StringComparison.Ordinal)
                            && it.StartGameSeconds <= atGameSeconds && atGameSeconds < it.EndGameSeconds)
                            return true;
                return false;
            }
            public float AvailableCapacity(string resourceKey, float atGameSeconds)
            {
                float cap = capacities.TryGetValue(resourceKey, out float c) ? c : 1f;
                float used = 0f;
                foreach (ReservationHandle h in live.Values)
                    foreach (ReservationItem it in h.Items)
                        if (string.Equals(it.ResourceKey, resourceKey, StringComparison.Ordinal)
                            && it.StartGameSeconds <= atGameSeconds && atGameSeconds < it.EndGameSeconds)
                            used += it.Quantity;
                return Math.Max(0f, cap - used);
            }
            public List<ReservationHandle> ReservationsForTask(string ownerTaskId)
            {
                var out_ = new List<ReservationHandle>();
                foreach (ReservationHandle h in live.Values)
                    if (string.Equals(h.OwnerTaskId, ownerTaskId, StringComparison.Ordinal))
                        out_.Add(h);
                return out_;
            }
            public int LiveReservationCount => live.Count;
            public int LiveCount => live.Count;
        }

        #endregion

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private TestDueEventScheduler dueEvents;
        private TestReservationAuthority reservations;
        private ForgeRepairChain chain;
        private ForgeChainContext ctx;
        private float clock;
        private EntityId smith;
        private EntityId business;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            dueEvents = new TestDueEventScheduler();
            reservations = new TestReservationAuthority();
            reservations.SetCapacity("forge:forge-1", 1f);
            chain = new ForgeRepairChain();
            clock = 0f;
            smith = EntityId.For(EntityKind.Person, 601);
            business = EntityId.For(EntityKind.Business, 2);
            ctx = new ForgeChainContext
            {
                Tasks = tasks,
                Budgets = budgets,
                DueEvents = dueEvents,
                Reservations = reservations,
                ClockGameSeconds = () => clock,
                DayIndex = 42,
                BusinessId = business,
                ForgeId = "forge-1",
                Diagnostics = new List<string>(),
            };
        }

        private EquipmentAsset NewPlow(string assetId = "plow-7")
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "plow",
                DisplayName = "Walking plow",
                Condition01 = 0.25f, // broken enough to need repair
            };
        }

        private void WorkActiveToCompletion(ForgeRepairOrder order)
        {
            int guard = 0;
            while (order.Process.IsActive && guard++ < 40)
            {
                int stage = order.Process.CurrentStageIndex;
                if (order.Process.CurrentStageDefinition.Kind != ProductionStageKind.ActiveLabor)
                {
                    // Passive (cooling): pump the clock past the due event.
                    clock = order.Process.CurrentStageState.DueGameSeconds + 1f;
                    chain.PumpDueEvents(ctx);
                    chain.RetryParked(ctx);
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(order.Process.ParkedReason))
                {
                    chain.RetryParked(ctx);
                    if (!string.IsNullOrWhiteSpace(order.Process.ParkedReason))
                        Assert.Fail("order parked and retry did not clear it: " + order.Process.ParkedReason);
                    continue;
                }
                Assert.IsTrue(chain.WorkOrder(order, 10000, ctx, out string reason),
                    "WorkOrder failed at stage " + stage + ": " + reason);
                chain.RetryParked(ctx);
            }
        }

        [Test]
        public void ExpertSmith_FullChain_Succeeds_WithExactReconciliation()
        {
            EquipmentAsset plow = NewPlow();
            float coalBefore = chain.CoalUnitsOnHand;
            float ironBefore = chain.IronUnitsOnHand;
            // Expert smith: capability 0.95 — the causal outcome should succeed.
            ForgeRepairOrder order = chain.BeginRepair(plow, smith, 0.95, ctx, out string reason);
            Assert.IsNotNull(order, "begin: " + reason);

            Assert.AreEqual(coalBefore - 5f, chain.CoalUnitsOnHand, 0.001f, "5 coal dispensed once");
            Assert.AreEqual(ironBefore - 2f, chain.IronUnitsOnHand, 0.001f, "2 iron dispensed once");
            Assert.AreEqual(TaskOutcome.Success, order.ResolvedOutcome, "expert: causal outcome is Success");
            Assert.Greater(order.ResolvedQualityDelta, 0.0, "expert: positive quality delta");

            WorkActiveToCompletion(order);

            Assert.IsTrue(order.IsComplete, "order complete");
            Assert.IsFalse(order.ReworkNeeded, "no rework for the expert");
            Assert.AreEqual(0, reservations.LiveCount, "forge reservation released");
            Assert.AreEqual(6, order.Process.StagePlan.Stages.Count, "6 stages");
        }

        [Test]
        public void NoviceSmith_FailsRepair_ReworkQueuedAndCompletes()
        {
            EquipmentAsset plow = NewPlow("plow-8");
            // Novice smith (capability 0.05, P603): the deterministic causal
            // outcome is RecoverableFailure (failure risk 0.25) — rework must queue.
            EntityId novice = EntityId.For(EntityKind.Person, 603);
            ForgeRepairOrder order = chain.BeginRepair(plow, novice, 0.05, ctx, out string reason);
            Assert.IsNotNull(order, "begin: " + reason);
            Assert.AreNotEqual(TaskOutcome.Success, order.ResolvedOutcome,
                "novice: causal outcome is not Success (was " + order.ResolvedOutcome + ")");
            Assert.Greater(order.ResolvedFailureRisk01, 0.2, "novice: high failure risk");

            // Drive to the repair stage and through it.
            int guard = 0;
            while (order.Process.IsActive && order.Process.CurrentStageIndex < ForgeRepairChain.StageRepair && guard++ < 30)
            {
                Assert.IsTrue(chain.WorkOrder(order, 10000, ctx, out string wreason), "work: " + wreason);
                chain.RetryParked(ctx);
            }
            Assert.AreEqual(ForgeRepairChain.StageRepair, order.Process.CurrentStageIndex, "at the repair stage");

            // Work the repair: the causal failure must queue rework, not advance.
            guard = 0;
            while (order.Process.CurrentStageIndex == ForgeRepairChain.StageRepair
                && !order.ReworkTaskId.IsValid && guard++ < 20)
            {
                Assert.IsTrue(chain.WorkOrder(order, 10000, ctx, out string wreason), "repair work: " + wreason);
            }
            Assert.IsTrue(order.ReworkTaskId.IsValid, "rework task queued on failure");
            Assert.IsTrue(order.ReworkNeeded, "rework flagged");
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.FailureNote), "failure noted loudly");
            Assert.AreEqual(ForgeRepairChain.StageRepair, order.Process.CurrentStageIndex,
                "stage did NOT advance on failure");

            // Work the rework to completion; the chain then advances to cooling.
            WorkActiveToCompletion(order);
            Assert.IsTrue(order.IsComplete, "order completes after rework");
            Assert.IsFalse(order.ReworkTaskId.IsValid, "rework task id cleared");
        }

        [Test]
        public void ForgeContention_NailRunBlocksRepair_ParksAndRetries()
        {
            // Nail production grabs the forge first.
            NailProductionRun nails = chain.StartNailProduction(50, smith, ctx, out string nreason);
            Assert.IsNotNull(nails, "nail run started: " + nreason);
            Assert.AreEqual(1, reservations.LiveCount, "forge held by nail run");

            EquipmentAsset plow = NewPlow("plow-9");
            ForgeRepairOrder order = chain.BeginRepair(plow, smith, 0.9, ctx, out string reason);
            Assert.IsNotNull(order, "begin: " + reason);

            // Drive to the prepare stage (which wants the forge).
            int guard = 0;
            while (order.Process.IsActive
                && order.Process.CurrentStageIndex < ForgeRepairChain.StagePrepare
                && string.IsNullOrWhiteSpace(order.Process.ParkedReason)
                && guard++ < 30)
            {
                Assert.IsTrue(chain.WorkOrder(order, 10000, ctx, out string wreason), "work: " + wreason);
            }
            // The forge reservation refused -> parked at inspect (stage 1), waiting for prepare.
            chain.RetryParked(ctx);
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.Process.ParkedReason),
                "repair parks when the forge is held by nail production");

            // Finish the nails; the forge frees; the repair retries and proceeds.
            int nguard = 0;
            while (!nails.IsComplete && nguard++ < 10)
                Assert.IsTrue(chain.WorkNailRun(nails, 60, ctx, out string nr), "nails: " + nr);
            Assert.IsTrue(nails.IsComplete, "nail run done");
            Assert.AreEqual(50, nails.NailsMade, "50 nails made");
            chain.RetryParked(ctx);
            Assert.IsTrue(string.IsNullOrWhiteSpace(order.Process.ParkedReason), "park cleared after forge freed");

            WorkActiveToCompletion(order);
            Assert.IsTrue(order.IsComplete, "repair completes after contention");
        }

        [Test]
        public void NoCoal_RepairRefusedLoudly()
        {
            chain.CoalUnitsOnHand = 0f;
            ForgeRepairOrder order = chain.BeginRepair(NewPlow(), smith, 0.9, ctx, out string reason);
            Assert.IsNull(order, "refused without coal");
            StringAssert.Contains("coal", reason.ToLowerInvariant(), "reason names coal: " + reason);
        }

        [Test]
        public void PhantomAsset_RepairRefusedLoudly()
        {
            ForgeRepairOrder order = chain.BeginRepair(null, smith, 0.9, ctx, out string reason);
            Assert.IsNull(order, "refused without an asset");
            StringAssert.Contains("phantom", reason.ToLowerInvariant(), "reason names phantom: " + reason);
        }

        [Test]
        public void SaveLoad_MidRepair_ResumesWithoutDuplication()
        {
            // Save/load during the repair stage: the restored order resumes
            // from the same stage with materials already dispensed (no double
            // dispense) and completes exactly once.
            EquipmentAsset plow = NewPlow();
            ForgeRepairOrder order = chain.BeginRepair(plow, smith, 0.9, ctx, out string reason);
            Assert.IsNull(reason, "begin ok: " + reason);
            Assert.AreEqual(ForgeRepairChain.StageRepair, order.Process.CurrentStageIndex);

            // Save mid-repair; restore into a fresh chain.
            ForgeRepairChain.ForgeRepairChainSaveDto dto = chain.CaptureSaveDto();
            var restored = new ForgeRepairChain();
            var ctx2 = new ForgeChainContext
            {
                Tasks = tasks,
                Budgets = budgets,
                DueEvents = ctx.DueEvents,
                Reservations = ctx.Reservations,
                ClockGameSeconds = ctx.ClockGameSeconds,
                DayIndex = ctx.DayIndex,
                BusinessId = ctx.BusinessId,
                ForgeId = ctx.ForgeId,
                Diagnostics = new List<string>(),
            };
            restored.LoadFromSaveDto(dto, ctx2);

            Assert.AreEqual(1, restored.Orders.Count);
            ForgeRepairOrder rOrder = restored.Orders[0];
            Assert.AreEqual(order.OrderId, rOrder.OrderId);
            Assert.AreEqual(ForgeRepairChain.StageRepair, rOrder.Process.CurrentStageIndex);
            Assert.AreEqual(2f, rOrder.IronUnitsDispensed);
            Assert.AreEqual(5f, rOrder.CoalUnitsDispensed);
            // The restored chain does not re-dispense: stock matches the original.
            Assert.AreEqual(chain.CoalUnitsOnHand, restored.CoalUnitsOnHand);
            Assert.AreEqual(chain.IronUnitsOnHand, restored.IronUnitsOnHand);

            // Complete the repair on the restored chain.
            int guard = 0;
            while (rOrder.Process.CurrentStageIndex != ForgeRepairChain.StageReturn && guard++ < 20)
            {
                Assert.IsTrue(restored.WorkOrder(rOrder, 5, ctx2, out reason), "advance: " + reason);
            }
            Assert.AreEqual(ForgeRepairChain.StageReturn, rOrder.Process.CurrentStageIndex);
        }
    }
}
