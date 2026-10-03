using System;
using LandLedgers.Economy.Equipment;
using System.Collections.Generic;
using LandLedgers.Skills;
using UnityEngine;

namespace LandLedgers.Economy.Tannery
{
    /// <summary>
    /// EQP-5: the tannery as a distinct trade. Historical basis (researched
    /// 2026-10-03): the tanner stood between the butcher (hide supplier) and the
    /// leatherworker — saddlers, harness makers, shoemakers (NYSM Albany exhibit).
    /// Tanning needed hides, tannin (hemlock/oak bark in the 1800s US), tanning
    /// pits/vats, running water, and MONTHS; tanyards sat away from dense
    /// habitation. This closes the EQU hole-hunt flag: butcher hides now have an
    /// honest path to workable leather, unblocking the saddler/harness maker.
    ///
    /// Batches are task-driven (tanning skill, TTS-3 extension path) — never an
    /// abstract conversion. Every leather lot carries hide + bark provenance.
    /// </summary>
    public sealed class TanneryRuntime
    {
        /// <summary>TTS-3 extension-path skill: tanning.</summary>
        public const string TanningSkillId = "tanning";
        public const string TanHidesTaskId = "tan-hides";

        /// <summary>
        /// Calibration: a tanning batch takes 90 days. Historical: oak-bark tanning
        /// took 7–18 months; hemlock was faster. 90 days is gameplay calibration,
        /// not canon (Canon Part XV).
        /// </summary>
        public const int TanningDaysPerBatch = 90;

        /// <summary>Calibration: bark units per 10 lbs of hide.</summary>
        public const int BarkUnitsPer10HideLbs = 5;

        public string BusinessInstanceId = string.Empty;
        public string BusinessName = string.Empty;

        [Serializable]
        public sealed class TanningBatch
        {
            public string BatchId = string.Empty;
            public string HideLotId = string.Empty;
            public int HideLbs;
            public string HideSourceNote = string.Empty; // provenance: which animal/butcher
            public int BarkUnits;
            public string BarkSourceNote = string.Empty; // provenance: tanbark lot / import order
            public int StartDayIndex;
            public int DurationDays = TanningDaysPerBatch;
            public string StartedBy = string.Empty; // worker person id (labor provenance)
        }

        [Serializable]
        public sealed class LeatherLot
        {
            public string LotId = string.Empty;
            public int Lbs;
            public string HideSourceNote = string.Empty;
            public string BarkSourceNote = string.Empty;
            public string TanneryBusinessId = string.Empty;
            public string TanneryName = string.Empty;
            public int CompletedDayIndex;
        }

        private readonly List<TanningBatch> batches = new List<TanningBatch>();
        private readonly List<LeatherLot> leatherLots = new List<LeatherLot>();
        private int nextBatchNumber = 1;
        private int nextLotNumber = 1;

        public IReadOnlyList<TanningBatch> Batches => batches;
        public IReadOnlyList<LeatherLot> LeatherLots => leatherLots;

        public TanneryRuntime() { }

        public TanneryRuntime(string businessInstanceId, string businessName)
        {
            BusinessInstanceId = businessInstanceId ?? string.Empty;
            BusinessName = businessName ?? string.Empty;
        }

        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("TanneryRuntime: no SkillService — tanning skill not registered.");
                return;
            }
            if (skillService.GetSkill(TanningSkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(TanningSkillId, "Tanning",
                        "Turning hides into workable leather: liming, bating, bark-tanning, currying. (TTS-3 extension path.)"),
                    out rejection))
                {
                    diagnostics.Add("TanneryRuntime: tanning skill rejected: " + rejection);
                }
            }
        }

        /// <summary>
        /// Starts a tanning batch: hide lot + tanbark + worker. Refuses without
        /// hides, without bark, or without a named worker — nothing tans itself.
        /// </summary>
        public string StartBatch(
            string hideLotId, int hideLbs, string hideSourceNote,
            int barkUnits, string barkSourceNote,
            string workerPersonId, int dayIndex, List<string> diagnostics,
            EquipmentTaskGate gate = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            // NX-1A: Canon 4.1 — tanning needs the tanning-yard workstation
            // (EQP-5 built it as a real business type).
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Workstation("tanning-yard") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return blocked;
            }
            if (string.IsNullOrWhiteSpace(hideLotId) || hideLbs <= 0)
                return "TanneryRuntime: tanning needs a real hide lot — hides are not conjured.";
            int requiredBark = Math.Max(1, Mathf.CeilToInt(hideLbs / 10f * BarkUnitsPer10HideLbs));
            if (barkUnits < requiredBark)
                return $"TanneryRuntime: {hideLbs} lbs of hide needs {requiredBark} bark units — only {barkUnits} supplied.";
            if (string.IsNullOrWhiteSpace(workerPersonId))
                return "TanneryRuntime: tanning is task labor — a worker must start the batch.";

            var batch = new TanningBatch
            {
                BatchId = $"{BusinessInstanceId}-tan-{nextBatchNumber++}",
                HideLotId = hideLotId,
                HideLbs = hideLbs,
                HideSourceNote = hideSourceNote ?? string.Empty,
                BarkUnits = barkUnits,
                BarkSourceNote = barkSourceNote ?? string.Empty,
                StartDayIndex = dayIndex,
                DurationDays = TanningDaysPerBatch,
                StartedBy = workerPersonId,
            };
            batches.Add(batch);
            diagnostics.Add($"TanneryRuntime: batch {batch.BatchId} started — {hideLbs} lbs hide + {barkUnits} bark, due day {dayIndex + TanningDaysPerBatch}.");
            return null;
        }

        /// <summary>
        /// Completes a batch once its tanning time has elapsed. Refuses early
        /// completion — leather takes months, and no task hurries chemistry.
        /// </summary>
        public LeatherLot CompleteBatch(string batchId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var batch = batches.Find(b => b.BatchId == batchId);
            if (batch == null)
            {
                diagnostics.Add($"TanneryRuntime: no batch {batchId}.");
                return null;
            }
            int dueDay = batch.StartDayIndex + batch.DurationDays;
            if (dayIndex < dueDay)
            {
                diagnostics.Add($"TanneryRuntime: batch {batchId} is not done until day {dueDay} (day {dayIndex} now) — tanning takes months.");
                return null;
            }
            batches.Remove(batch);
            var lot = new LeatherLot
            {
                LotId = $"{BusinessInstanceId}-leather-{nextLotNumber++}",
                Lbs = batch.HideLbs, // weight preserved through tanning (calibration: hemlock weight gain noted historically; kept 1:1)
                HideSourceNote = batch.HideSourceNote,
                BarkSourceNote = batch.BarkSourceNote,
                TanneryBusinessId = BusinessInstanceId,
                TanneryName = BusinessName,
                CompletedDayIndex = dayIndex,
            };
            leatherLots.Add(lot);
            diagnostics.Add($"TanneryRuntime: batch {batchId} → {lot.Lbs} lbs workable leather ({lot.LotId}).");
            return lot;
        }
    }
}
