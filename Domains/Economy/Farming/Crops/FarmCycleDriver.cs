using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// Pass IX WS3 (A4-18, A4-19; PASS7-004 carried): the production driver
    /// for the detailed farm cycle. Before this driver, CropChain's
    /// PlantField / TendField / HarvestField had no production callers
    /// outside FarmSeedStore helpers and tests, while live farm output
    /// flowed through the abstract weekly resolver (staffing counts in,
    /// bare category units out, credited at weekly resolution time).
    ///
    /// This driver enumerates the chain's fields and drives each stage from
    /// COMPLETED TaskAuthority field-work tasks owned by the farm:
    /// - planting consumes named seed (provenance is mandatory — the
    ///   CropChain gate refuses sourceless seed, and so does this driver);
    /// - tending raises the field's realized-yield factor through the
    ///   field state;
    /// - harvest credits a provenance CropLot AT TASK COMPLETION (the drive
    ///   that observes the completed harvest task), never at weekly
    ///   resolution (A4-19).
    ///
    /// Single-credit rule (the mine reconciliation applied to farms): the
    /// harvest lot is the ONLY output of a driven field. This driver never
    /// also credits category stock for the same work, and a field with no
    /// completed tasks produces exactly nothing — growth time alone
    /// (AdvanceDay) ripens fields but creates no goods. Each completed task
    /// is consumed exactly once (saveable set), so re-driving can never
    /// double-produce.
    ///
    /// Task conventions (the production contract for field work):
    /// - DefinitionId: CropChain.PlantFieldTaskId / TendFieldTaskId /
    ///   HarvestFieldTaskId.
    /// - OwnerId: the farm business entity (MineProductionDriver's stable
    ///   business-entity convention is shared via BusinessEntityIdFor here).
    /// - CustomerRef: the field id.
    /// - TermsRef (plant): "crop:Wheat;seed:80;source:general store" —
    ///   seed units default to the calibration's per-acre need when absent;
    ///   the source is REQUIRED (no orphan inputs).
    /// - TermsRef (harvest): optionally "variance:&lt;0.5..1.5&gt;".
    /// </summary>
    public sealed class FarmCycleDriver
    {
        /// <summary>Process-wide driver used by the weekly shared-runtime delegation.</summary>
        public static readonly FarmCycleDriver Shared = new FarmCycleDriver();

        private readonly HashSet<string> consumedTaskIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Task ids whose field-work outcome has already been recorded.</summary>
        public IReadOnlyCollection<string> ConsumedTaskIds => consumedTaskIds;

        /// <summary>
        /// The business entity id that owns a farm's work tasks — the same
        /// stable-hash convention the production labor drivers use
        /// (SimulationDrivers.StableBusinessRuntimeId).
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

        /// <summary>Outcome of one drive over a farm's completed field-work tasks.</summary>
        public sealed class FarmDriveResult
        {
            public int TasksConsumed;
            public int FieldsPlanted;
            public int FieldsTended;
            public int FieldsHarvested;
            public int UnitsHarvested;
            public readonly List<CropLot> HarvestedLots = new List<CropLot>();
        }

        /// <summary>
        /// Advances field growth for the day, then records the outcome of
        /// every completed, not-yet-consumed field-work task owned by the
        /// farm. Refusals from the CropChain gates (wrong season, unprepared
        /// field, sourceless seed, unready field) stay loud in diagnostics
        /// and leave the task unconsumed for a later drive.
        /// </summary>
        public FarmDriveResult DriveCompletedFieldTasks(
            CropChain chain,
            string farmBusinessInstanceId,
            TaskAuthority tasks,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var result = new FarmDriveResult();
            if (chain == null)
            {
                diagnostics.Add("FarmCycleDriver: no CropChain — nothing to drive.");
                return result;
            }

            // Growth is time, not production: ripening creates no goods.
            chain.Fields.AdvanceDay(dayIndex, diagnostics);

            if (tasks == null)
            {
                diagnostics.Add("FarmCycleDriver: no TaskAuthority — field work runs from completed tasks or not at all.");
                return result;
            }
            if (idRegistry == null)
            {
                diagnostics.Add("FarmCycleDriver: no EntityIdRegistry — crop lots need real ids; nothing produced.");
                return result;
            }

            EntityId owner = BusinessEntityIdFor(farmBusinessInstanceId);
            List<WorkTask> allTasks = tasks.CaptureSaveDto()?.tasks;
            if (allTasks == null)
                return result;

            foreach (WorkTask task in allTasks)
            {
                if (task == null || task.Status != TaskStatus.Complete)
                    continue;
                if (task.OwnerId != owner)
                    continue;
                bool isPlant = string.Equals(task.DefinitionId, CropChain.PlantFieldTaskId, StringComparison.Ordinal);
                bool isTend = string.Equals(task.DefinitionId, CropChain.TendFieldTaskId, StringComparison.Ordinal);
                bool isHarvest = string.Equals(task.DefinitionId, CropChain.HarvestFieldTaskId, StringComparison.Ordinal);
                if (!isPlant && !isTend && !isHarvest)
                    continue;
                string taskKey = task.TaskId.ToString();
                if (consumedTaskIds.Contains(taskKey))
                    continue;
                if (task.Outcome != TaskOutcome.Success && task.Outcome != TaskOutcome.AcceptableWithVariation)
                {
                    consumedTaskIds.Add(taskKey);
                    diagnostics.Add($"FarmCycleDriver: field task {taskKey} ({task.DefinitionId}) ended {task.Outcome} — no outcome recorded.");
                    continue;
                }

                string fieldId = (task.CustomerRef ?? string.Empty).Trim();
                CropFieldState field = chain.Fields.GetField(fieldId);
                if (field == null)
                {
                    diagnostics.Add($"FarmCycleDriver: field task {taskKey} names unknown field '{fieldId}' — not credited.");
                    continue;
                }

                if (isPlant)
                {
                    if (!TryParsePlantTerms(chain, field, task.TermsRef, diagnostics,
                            out CropKind crop, out int seedUnits, out string seedSource))
                        continue;
                    string problem = chain.PlantField(fieldId, crop, seedUnits, seedSource,
                        task.AssigneeId, dayIndex, diagnostics);
                    if (problem != null)
                    {
                        diagnostics.Add(problem);
                        continue;
                    }
                    consumedTaskIds.Add(taskKey);
                    result.TasksConsumed++;
                    result.FieldsPlanted++;
                }
                else if (isTend)
                {
                    string problem = chain.TendField(fieldId, task.AssigneeId, dayIndex, diagnostics);
                    if (problem != null)
                    {
                        diagnostics.Add(problem);
                        continue;
                    }
                    consumedTaskIds.Add(taskKey);
                    result.TasksConsumed++;
                    result.FieldsTended++;
                }
                else
                {
                    float variance = ParseFloatTerm(task.TermsRef, "variance", 1f);
                    CropLot lot = chain.HarvestField(idRegistry, fieldId, task.AssigneeId,
                        dayIndex, variance, diagnostics);
                    if (lot == null)
                        continue; // refusal already logged; retry on a later drive
                    consumedTaskIds.Add(taskKey);
                    result.TasksConsumed++;
                    result.FieldsHarvested++;
                    result.UnitsHarvested += Math.Max(0, lot.QuantityUnits);
                    result.HarvestedLots.Add(lot);
                }
            }

            return result;
        }

        /// <summary>Weekly outcome for the shared-runtime delegation.</summary>
        public sealed class FarmWeeklyResult
        {
            public int TasksConsumed;
            public int FieldsPlanted;
            public int FieldsTended;
            public int FieldsHarvested;
            public int UnitsHarvested;
            public readonly List<CropLot> HarvestedLots = new List<CropLot>();
            public string BlockedReason = string.Empty;

            public string BuildSummary()
            {
                return $"crop harvest {UnitsHarvested} units from completed field tasks " +
                    $"({HarvestedLots.Count} lot(s)); planted {FieldsPlanted} field(s); " +
                    $"tended {FieldsTended} field(s); harvested {FieldsHarvested} field(s)";
            }
        }

        /// <summary>
        /// The weekly production entry the shared runtime delegates to
        /// (Pass IX farm integration, A4-18/A4-19 — the mine pattern in
        /// MineProductionDriver.DriveWeekly): drive completed field-work
        /// tasks and report the outcome. Produces nothing on its own — with
        /// no completed tasks it is a pure report, and the harvest lot
        /// credited inside <see cref="DriveCompletedFieldTasks"/> at task
        /// completion is the ONLY farm output: this entry never also
        /// credits category stock for the same work, so the reconciled
        /// weekly path can never double-produce.
        /// </summary>
        public FarmWeeklyResult DriveWeekly(
            CropChain chain,
            string farmBusinessInstanceId,
            TaskAuthority tasks,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var result = new FarmWeeklyResult();
            if (chain == null)
            {
                result.BlockedReason = "no crop chain — farm production runs from driven field tasks only";
                return result;
            }

            FarmDriveResult drive = DriveCompletedFieldTasks(
                chain, farmBusinessInstanceId, tasks, idRegistry, dayIndex, diagnostics);
            result.TasksConsumed = drive.TasksConsumed;
            result.FieldsPlanted = drive.FieldsPlanted;
            result.FieldsTended = drive.FieldsTended;
            result.FieldsHarvested = drive.FieldsHarvested;
            result.UnitsHarvested = drive.UnitsHarvested;
            result.HarvestedLots.AddRange(drive.HarvestedLots);
            if (result.TasksConsumed <= 0)
            {
                result.BlockedReason = tasks == null
                    ? "no TaskAuthority — field work runs from completed tasks or not at all"
                    : "no completed field tasks — production runs from completed field work only";
            }
            return result;
        }

        private static bool TryParsePlantTerms(
            CropChain chain, CropFieldState field, string termsRef, List<string> diagnostics,
            out CropKind crop, out int seedUnits, out string seedSource)
        {
            crop = CropKind.Unspecified;
            seedUnits = 0;
            seedSource = string.Empty;
            string cropName = TermValue(termsRef, "crop");
            if (!string.IsNullOrWhiteSpace(cropName))
                Enum.TryParse(cropName.Trim(), ignoreCase: true, out crop);
            if (crop == CropKind.Unspecified)
                crop = field.CurrentCrop; // a prepared field may already carry its intended crop

            string seedText = TermValue(termsRef, "seed");
            if (!string.IsNullOrWhiteSpace(seedText)
                && int.TryParse(seedText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSeed))
                seedUnits = Math.Max(0, parsedSeed);
            if (seedUnits <= 0)
            {
                CropCalibration calibration = chain.Fields.GetCalibration(crop);
                if (calibration != null)
                    seedUnits = Math.Max(1, (int)Math.Ceiling(field.Acres * calibration.SeedUnitsPerAcre));
            }

            seedSource = TermValue(termsRef, "source") ?? string.Empty;
            if (crop == CropKind.Unspecified)
            {
                diagnostics.Add("FarmCycleDriver: plant task names no crop (TermsRef 'crop:') and the field carries none — not planted.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(seedSource))
            {
                diagnostics.Add($"FarmCycleDriver: plant task for field '{field.FieldId}' names no seed source " +
                    "(TermsRef 'source:') — seed does not appear from nowhere; not planted.");
                return false;
            }
            return true;
        }

        private static string TermValue(string termsRef, string key)
        {
            if (string.IsNullOrWhiteSpace(termsRef) || string.IsNullOrWhiteSpace(key))
                return null;
            foreach (string part in termsRef.Split(';'))
            {
                string trimmed = part.Trim();
                int colon = trimmed.IndexOf(':');
                if (colon <= 0)
                    continue;
                if (string.Equals(trimmed.Substring(0, colon).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(colon + 1).Trim();
            }
            return null;
        }

        private static float ParseFloatTerm(string termsRef, string key, float fallback)
        {
            string text = TermValue(termsRef, key);
            if (!string.IsNullOrWhiteSpace(text)
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            return fallback;
        }

        // ---------- save / load (consumed-task set; additive DTO) ----------

        [Serializable]
        public sealed class FarmCycleDriverSaveDto
        {
            public List<string> consumedTaskIds = new List<string>();
        }

        public FarmCycleDriverSaveDto CaptureSaveDto()
        {
            return new FarmCycleDriverSaveDto
            {
                consumedTaskIds = new List<string>(consumedTaskIds),
            };
        }

        public void LoadFromSaveDto(FarmCycleDriverSaveDto dto)
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
