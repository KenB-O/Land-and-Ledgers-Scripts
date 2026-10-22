using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// Pass IX WS3 (PASS8-001 / A4-06, A4-07, A4-10, A4-19): the production
    /// driver for the detailed mine cycle. Before this driver,
    /// <see cref="MineProductionCycle.RecordBreakOreShift"/> had callers only
    /// in tests, while ordinary gameplay produced ore through the abstract
    /// weekly resolver in SharedBusinessRuntimeManager (staffing counts in,
    /// bare category units out, sold off-map the same tick, depleting the
    /// same RemainingRichness01 dial the detailed cycle depletes).
    ///
    /// This driver is now the ONE production path:
    /// - It enumerates the mine's shaft-plan levels and the COMPLETED
    ///   break-ore TaskAuthority tasks owned by the mine business, and turns
    ///   each completed task's worked minutes into assayed ore lots with
    ///   shaft/level/vein provenance via RecordBreakOreShift. Output is
    ///   credited from task completion (worked minutes), never invented at
    ///   weekly resolution time (A4-19): a week with no completed break-ore
    ///   task produces exactly zero tons and depletes exactly zero richness.
    /// - Each completed task is consumed exactly once (the consumed set is
    ///   saveable), so re-driving a week can never double-produce or
    ///   double-deplete (A4-07 regression surface).
    /// - Ore leaves the stockpile only as a declared smelter shipment order
    ///   (MineOreShipmentService): a named counterparty, a per-ton price and
    ///   real transit days. Revenue settles ONLY after the transit elapses,
    ///   on delivery — never instantly inside the production call (A4-10).
    ///
    /// Task conventions (the production contract for break-ore work):
    /// - DefinitionId: MineLaborRegister.MineLaborTaskCatalog.BreakOreTaskId.
    /// - OwnerId: the mine business entity (see BusinessEntityIdFor).
    /// - CustomerRef: the level id the shift was worked on.
    /// - TermsRef: optionally "grade:&lt;estimated grade value&gt;".
    /// </summary>
    public sealed class MineProductionDriver
    {
        /// <summary>Process-wide driver used by the weekly shared-runtime delegation.</summary>
        public static readonly MineProductionDriver Shared = new MineProductionDriver();

        private readonly HashSet<string> consumedTaskIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Task ids whose break-ore output has already been recorded.</summary>
        public IReadOnlyCollection<string> ConsumedTaskIds => consumedTaskIds;

        /// <summary>
        /// The business entity id that owns a business's work tasks. Mirrors
        /// SimulationDrivers.StableBusinessRuntimeId (the production task
        /// ownership convention) so tasks scheduled by the labor drivers are
        /// found here unchanged.
        /// </summary>
        public static EntityId BusinessEntityIdFor(string businessInstanceId)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in businessInstanceId ?? string.Empty)
                    hash = hash * 31 + character;
                return EntityId.For(EntityKind.Business, hash & 0x7fffffff);
            }
        }

        /// <summary>Outcome of one drive over a mine's completed break-ore work.</summary>
        public sealed class MineDriveResult
        {
            public int TasksConsumed;
            public int LotsCreated;
            public int TonsBroken;
            public readonly List<MineOreLot> Lots = new List<MineOreLot>();
        }

        /// <summary>
        /// Converts every completed, not-yet-consumed break-ore task owned by
        /// the mine business into ore lots. Returns the drive outcome; gates
        /// that refuse (level not InOre, unrostered miner, no hoist, mine
        /// idle/closed) leave the task unconsumed and loud in diagnostics —
        /// the work is retried on a later drive, never silently credited.
        /// </summary>
        public MineDriveResult DriveCompletedBreakOreTasks(
            MineRuntimeState mine,
            string businessInstanceId,
            TaskAuthority tasks,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var result = new MineDriveResult();
            if (mine == null)
            {
                diagnostics.Add("MineProductionDriver: no mine runtime — nothing to drive.");
                return result;
            }
            if (tasks == null)
            {
                diagnostics.Add("MineProductionDriver: no TaskAuthority — break-ore production " +
                    "runs from completed tasks or not at all; nothing produced.");
                return result;
            }
            if (idRegistry == null)
            {
                diagnostics.Add("MineProductionDriver: no EntityIdRegistry — ore lots need real ids; nothing produced.");
                return result;
            }

            EntityId owner = BusinessEntityIdFor(businessInstanceId);
            // The task save DTO is the authority's full task list (open and
            // completed); GetOpenTasks alone cannot see completed work.
            List<WorkTask> allTasks = tasks.CaptureSaveDto()?.tasks;
            if (allTasks == null)
                return result;

            foreach (WorkTask task in allTasks)
            {
                if (task == null || task.Status != TaskStatus.Complete)
                    continue;
                if (!string.Equals(task.DefinitionId,
                        MineLaborRegister.MineLaborTaskCatalog.BreakOreTaskId, StringComparison.Ordinal))
                    continue;
                if (task.OwnerId != owner)
                    continue;
                string taskKey = task.TaskId.ToString();
                if (consumedTaskIds.Contains(taskKey))
                    continue;
                if (task.Outcome != TaskOutcome.Success && task.Outcome != TaskOutcome.AcceptableWithVariation)
                {
                    // Failed work broke no ore. Consume it so the refusal is
                    // recorded once instead of re-litigated every drive.
                    consumedTaskIds.Add(taskKey);
                    diagnostics.Add($"MineProductionDriver: break-ore task {taskKey} ended {task.Outcome} — no ore credited.");
                    continue;
                }

                string levelId = ResolveLevelId(mine, task, diagnostics);
                if (string.IsNullOrWhiteSpace(levelId))
                    continue;

                float grade = ParseGrade(task.TermsRef);
                MineOreLot lot = MineProductionCycle.RecordBreakOreShift(
                    mine, levelId, task.AssigneeId, task.MinutesWorked, grade,
                    idRegistry, dayIndex, diagnostics);
                if (lot == null)
                    continue; // gate refusal already logged; retry on a later drive

                consumedTaskIds.Add(taskKey);
                result.TasksConsumed++;
                result.LotsCreated++;
                result.TonsBroken += lot.Tons;
                result.Lots.Add(lot);
            }

            return result;
        }

        /// <summary>
        /// The level a completed task was worked on: the task's CustomerRef
        /// names it. When the ref is empty and the mine has exactly ONE
        /// producing (InOre) level, that level is unambiguous; otherwise the
        /// task is left unconsumed with a loud diagnostic.
        /// </summary>
        private static string ResolveLevelId(MineRuntimeState mine, WorkTask task, List<string> diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(task.CustomerRef))
                return task.CustomerRef.Trim();

            string only = null;
            int producing = 0;
            foreach (MineLevel level in mine.ShaftPlan.Levels)
            {
                if (level != null && level.WorkingStatus == MineWorkingStatus.InOre)
                {
                    producing++;
                    only = level.LevelId;
                }
            }
            if (producing == 1)
                return only;

            diagnostics.Add($"MineProductionDriver: break-ore task {task.TaskId} names no level " +
                $"and the mine has {producing} producing levels — cannot attribute the shift; not credited.");
            return null;
        }

        private static float ParseGrade(string termsRef)
        {
            if (string.IsNullOrWhiteSpace(termsRef))
                return 0f;
            foreach (string part in termsRef.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("grade:", StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(trimmed.Substring("grade:".Length),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out float grade))
                    return Math.Max(0f, grade);
            }
            return 0f;
        }

        /// <summary>
        /// Ships the whole stockpile to the declared smelter for the mine's
        /// mineral kind. The tons leave the stockpile into a named
        /// counterparty order (in transit); NO cash moves here (A4-10).
        /// Returns the order, or null when there is nothing to ship or no
        /// declared link for the mineral kind.
        /// </summary>
        public MineOreShipmentOrder DispatchStockpileToSmelter(
            MineRuntimeState mine,
            string mineBusinessId,
            string mineBusinessName,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (mine == null || mine.OreStock.TotalTons <= 0)
                return null;

            MineSmelterLink link = null;
            foreach (MineSmelterLink candidate in MineSmelterCatalog.Links)
            {
                if (candidate != null && candidate.MineralKind == mine.MineralKind)
                {
                    link = candidate;
                    break;
                }
            }
            if (link == null)
            {
                diagnostics.Add($"MineProductionDriver: no declared smelter link for {mine.MineralKind} — " +
                    $"{mine.OreStock.TotalTons} tons stay in the stockpile; ore ships only on declared links.");
                return null;
            }

            string originShaftId = mine.OreStock.Lots.Count > 0 ? mine.OreStock.Lots[0].ShaftId : string.Empty;
            MineOreShipmentOrder order = mine.CreateShipmentService().PlaceShipment(
                mine.OreStock, mineBusinessId, mineBusinessName, link.LinkId,
                mine.OreStock.TotalTons, originShaftId, dayIndex);
            if (order == null)
            {
                diagnostics.Add("MineProductionDriver: smelter shipment refused — " +
                    string.Join("; ", mine.CreateShipmentService().Diagnostics));
            }
            return order;
        }

        /// <summary>
        /// Settles every in-transit order whose transit has elapsed, paying
        /// the mine's operating ledger through the detailed chain's own
        /// settlement (HouseholdLedger is the single cash writer; the smelter
        /// is the named counterparty). Exactly-once: delivered orders are
        /// never repaid. Returns total cents paid.
        /// </summary>
        public int SettleDueShipments(
            MineRuntimeState mine,
            HouseholdLedger businessCash,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (mine == null || businessCash == null)
                return 0;

            MineOreShipmentService service = mine.CreateShipmentService();
            int paid = 0;
            foreach (MineOreShipmentOrder order in new List<MineOreShipmentOrder>(service.InTransitOrders()))
            {
                if (dayIndex < order.ExpectedArrivalDayIndex)
                    continue;
                string problem = service.SettleDelivery(order.OrderId, businessCash, dayIndex, mine.CostLedger);
                if (problem != null)
                {
                    diagnostics.Add("MineProductionDriver: " + problem);
                    continue;
                }
                paid += order.TotalCents;
            }
            return paid;
        }

        /// <summary>
        /// Shared-runtime settlement leg: settles due orders into the
        /// business runtime's weekly transfer revenue, naming the smelter
        /// counterparty and the order on the revenue line. Used by the
        /// weekly delegation, whose cash authority is BusinessRuntimeState.
        /// Exactly-once via the order's delivered flag; never settles early.
        /// Returns total cents credited.
        /// </summary>
        public int SettleDueShipmentsToRuntime(
            MineRuntimeState mine,
            BusinessRuntimeState runtime,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (mine == null || runtime == null)
                return 0;

            int credited = 0;
            foreach (MineOreShipmentOrder order in new List<MineOreShipmentOrder>(mine.ShipmentOrders))
            {
                if (order == null || order.IsDelivered)
                    continue;
                if (dayIndex < order.ExpectedArrivalDayIndex)
                    continue;
                int cents = order.TotalCents;
                order.MarkDelivered(dayIndex);
                if (cents > 0)
                {
                    runtime.AddWeeklyLocalTransferRevenue(cents,
                        $"ore shipment {order.OrderId}: {order.Tons} tons {order.MineralKind} " +
                        $"delivered to {order.SmelterName} (counterparty) for {cents}c");
                    credited += cents;
                }
                diagnostics.Add($"MineProductionDriver: ore shipment {order.OrderId} settled on delivery " +
                    $"day {dayIndex}: {order.Tons} tons to {order.SmelterName}, {cents}c.");
            }
            return credited;
        }

        /// <summary>Weekly outcome for the shared-runtime delegation.</summary>
        public sealed class MineWeeklyResult
        {
            public int TonsBroken;
            public int LotsCreated;
            public int TonsShipped;
            public string ShippedOrderId = string.Empty;
            public int SettledCents;
            public int StockpileTonsAfter;
            public string BlockedReason = string.Empty;

            public string BuildSummary(MineRuntimeState mine)
            {
                string mineral = mine != null ? MineRuntimeState.GetMineralDisplayName(mine.MineralKind) : "Ore";
                string text = $"{mineral} ore {TonsBroken} tons broken from completed break-ore tasks " +
                    $"({LotsCreated} lot(s)); stockpile {StockpileTonsAfter} tons";
                if (TonsShipped > 0)
                    text += $"; shipped {TonsShipped} tons to declared smelter (order {ShippedOrderId}, in transit — paid on delivery)";
                if (SettledCents > 0)
                    text += $"; smelter settlements {SettledCents}c on delivered orders";
                if (mine != null)
                    text += $"; richness {mine.RemainingRichness01:P0}; confidence {mine.DepositConfidence01:P0}; " +
                        $"stage {MineRuntimeState.GetDevelopmentStageDisplayName(mine.DevelopmentStage)}";
                return text;
            }
        }

        /// <summary>
        /// The weekly production entry the shared runtime delegates to:
        /// drive completed break-ore tasks, settle due smelter deliveries,
        /// then dispatch the stockpile. Produces nothing on its own — with
        /// no completed tasks and no stockpile it is a pure report, and it
        /// NEVER depletes richness itself (depletion happens only inside
        /// RecordBreakOreShift, per real ton broken).
        /// </summary>
        public MineWeeklyResult DriveWeekly(
            BusinessInstanceState business,
            TaskAuthority tasks,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var result = new MineWeeklyResult();
            MineRuntimeState mine = business != null ? business.MineState : null;
            BusinessRuntimeState runtime = business != null ? business.RuntimeState : null;
            if (mine == null || runtime == null)
            {
                result.BlockedReason = "no mine runtime state";
                return result;
            }

            MineDriveResult drive = DriveCompletedBreakOreTasks(
                mine, business.InstanceId, tasks, idRegistry, dayIndex, diagnostics);
            result.TonsBroken = drive.TonsBroken;
            result.LotsCreated = drive.LotsCreated;

            result.SettledCents = SettleDueShipmentsToRuntime(mine, runtime, dayIndex, diagnostics);

            MineOreShipmentOrder order = DispatchStockpileToSmelter(
                mine, business.InstanceId, business.RuntimeDisplayName, dayIndex, diagnostics);
            if (order != null)
            {
                result.TonsShipped = order.Tons;
                result.ShippedOrderId = order.OrderId;
            }

            result.StockpileTonsAfter = mine.OreStock.TotalTons;
            if (result.TonsBroken <= 0 && tasks != null)
            {
                result.BlockedReason = "no completed break-ore tasks — production runs from worked shifts only";
            }
            return result;
        }

        // ---------- save / load (consumed-task set; additive DTO) ----------

        [Serializable]
        public sealed class MineProductionDriverSaveDto
        {
            public List<string> consumedTaskIds = new List<string>();
        }

        public MineProductionDriverSaveDto CaptureSaveDto()
        {
            return new MineProductionDriverSaveDto
            {
                consumedTaskIds = new List<string>(consumedTaskIds),
            };
        }

        public void LoadFromSaveDto(MineProductionDriverSaveDto dto)
        {
            consumedTaskIds.Clear();
            if (dto?.consumedTaskIds == null)
                return;
            foreach (string id in dto.consumedTaskIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    consumedTaskIds.Add(id);
            }
        }
    }
}
