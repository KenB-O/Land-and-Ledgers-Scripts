using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// Pass VIII (production networks): the extraction production function for
    /// the detailed mine system (W8A-W8D). Before this, the mine had land and
    /// mineral rights (shaft plan), development (stages), workers and equipment
    /// (labor register, hoists), ore lots (stockpile), transport (shipment
    /// orders), sales (graded settlement) and wages — but no function that turns
    /// worked break-ore labor into ore. Ore reached the books only through the
    /// shared abstract weekly path (category stock: no lots, no assay, no
    /// declared smelters, no in-transit delay), leaving the detailed chain
    /// inoperable end to end.
    ///
    /// RecordBreakOreShift closes the loop: completed break-ore work minutes
    /// become ore lots with shaft/level/vein provenance in the stockpile, the
    /// deposit depletes (remainingRichness01 — extraction without depletion is
    /// infinite ore), and the hoist rope wears with the tons hoisted.
    ///
    /// Person-time is real and external: the caller drives the minutes through
    /// the TaskAuthority break-ore task (MineLaborTaskCatalog.BreakOreTaskId);
    /// this records the outcome, exactly like the farm's CropChain outcome
    /// recorders. Gates, all loud: the level must be worked for production
    /// (InOre), the miner must hold an active crew assignment, the posture must
    /// allow production, and a serviceable hoist must stand on the shaft.
    /// </summary>
    public static class MineProductionCycle
    {
        /// <summary>
        /// Calibration: deposit richness depleted per ton broken (tuning, not
        /// canon). At 0.002/ton, 500 tons exhaust a fresh deposit — the dial
        /// the shared weekly path already depletes, now driven by real tons.
        /// </summary>
        public const float ExtractionDepletionPerTon01 = 0.002f;

        /// <summary>
        /// Records one miner's completed break-ore shift against a producing
        /// level. Returns the created ore lot now sitting in the mine's
        /// stockpile, or null with a diagnostic when any gate refuses.
        /// </summary>
        public static MineOreLot RecordBreakOreShift(
            Economy.MineRuntimeState mine,
            string levelId,
            EntityId minerPersonId,
            int breakOreMinutesWorked,
            float estimatedGradeValue,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (mine == null)
            {
                diagnostics.Add("MineProductionCycle: no mine runtime — ore needs a mine.");
                return null;
            }

            MineLevel level = mine.ShaftPlan.FindLevel(levelId);
            if (level == null)
            {
                diagnostics.Add($"MineProductionCycle: level '{levelId}' is not on the mine plan — no tracked working, no ore.");
                return null;
            }

            if (level.WorkingStatus != MineWorkingStatus.InOre)
            {
                diagnostics.Add($"MineProductionCycle: level '{levelId}' is {level.WorkingStatus}, " +
                    "not worked for production (InOre) — ore breaks only on producing fronts.");
                return null;
            }

            MineOperatingPosture posture = mine.OperatingPosture;
            if (posture == MineOperatingPosture.TemporarilyIdle || posture == MineOperatingPosture.Closed)
            {
                diagnostics.Add($"MineProductionCycle: mine is {MineOperatingPostureNames.GetDisplayName(posture)} — " +
                    "no production while idle or closed (Canon 21.7L).");
                return null;
            }

            bool minerAssigned = false;
            foreach (MineCrewAssignment assignment in mine.LaborRegister.ActiveAssignments())
            {
                if (assignment != null && assignment.PersonId.Equals(minerPersonId))
                {
                    minerAssigned = true;
                    break;
                }
            }
            if (!minerAssigned)
            {
                diagnostics.Add($"MineProductionCycle: P{minerPersonId.Id} holds no active crew assignment — " +
                    "no rostered miner, no broken ore.");
                return null;
            }

            if (breakOreMinutesWorked <= 0)
            {
                diagnostics.Add("MineProductionCycle: no break-ore minutes worked — the shift broke nothing.");
                return null;
            }

            int tons = breakOreMinutesWorked / MineLaborRegister.MineLaborTaskCatalog.BreakMinutesPerTon;
            if (tons <= 0)
            {
                diagnostics.Add($"MineProductionCycle: {breakOreMinutesWorked} break-ore minutes is less than " +
                    $"one ton of work ({MineLaborRegister.MineLaborTaskCatalog.BreakMinutesPerTon} min/ton) — no lot yet.");
                return null;
            }

            MineHoist hoist = mine.HoistRegister.HoistOnShaft(level.ShaftId);
            if (hoist == null)
            {
                diagnostics.Add($"MineProductionCycle: shaft '{level.ShaftId}' has no hoisting plant — " +
                    "broken ore cannot leave the workings (Canon Part V hoist-operator profile).");
                return null;
            }
            string hoistProblem = hoist.RecordHoistingWork(tons, diagnostics);
            if (hoistProblem != null)
            {
                diagnostics.Add("MineProductionCycle: " + hoistProblem);
                return null;
            }

            MineOreLot lot = MineOreLot.Create(
                idRegistry,
                mine.MineralKind,
                tons,
                Math.Max(0f, estimatedGradeValue),
                level.ShaftId,
                level.LevelId,
                level.OnVeinId,
                dayIndex,
                mine.StockpileCategoryId);
            string refusal = mine.OreStock.ReceiveLot(lot, diagnostics);
            if (refusal != null)
            {
                diagnostics.Add("MineProductionCycle: stockpile refused the broken ore — " + refusal);
                return null;
            }

            mine.ApplyExtractionDepletion(tons, ExtractionDepletionPerTon01);
            diagnostics.Add($"MineProductionCycle: P{minerPersonId.Id} broke {tons} tons on level '{levelId}' " +
                $"(lot {lot.LotId.Id}, {breakOreMinutesWorked} worked minutes) — deposit richness now {mine.RemainingRichness01:P1}.");
            return lot;
        }
    }
}
