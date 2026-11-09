using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Population;
using LandLedgers.Tasks;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// NX-1A: the reusable equipment execution gate. Implements Canon 4.1
    /// CANON LOCK for task execution: builds the holder's equipment
    /// availability (owned assets/kits + unexpired access grants, Tech X §3.7),
    /// runs EquipmentRequirements.CheckTaskRequirements (condition gates, never
    /// debuffs — Tech X §3.9), and layers supportability (Canon 5.2).
    ///
    /// Two uses:
    ///  (1) Installed on TaskAuthority via SetExecutionGate — the central TTS-2
    ///      choke point. Every task started through the authority is gated.
    ///  (2) Called directly by domain work-execution methods (MilkCow,
    ///      CompletePlowing, forge/repair, construction, ...) via CheckExecution.
    ///
    /// All suppliers are optional; a null supplier means that source is simply
    /// absent (no phantom equipment). Borrowed/rented equipment satisfies
    /// requirements only through recorded EquipmentAccessGrants (Tech X §3.7).
    /// </summary>
    public sealed class EquipmentTaskGate : ITaskExecutionGate
    {
        /// <summary>All equipment assets visible to the gate (aggregated across registers).</summary>
        public Func<List<EquipmentAsset>> AssetsSupplier;

        /// <summary>All toolkit instances visible to the gate.</summary>
        public Func<List<ToolKitInstance>> KitsSupplier;

        /// <summary>Resolves borrowed/rented/contracted access grants. May be null.</summary>
        public EquipmentAccessResolver AccessResolver;

        /// <summary>Workstations for a holder kind+id. May be null.</summary>
        public Func<string, string, BusinessWorkstations> WorkstationsFor;

        /// <summary>Workstation definition lookup. May be null.</summary>
        public Func<string, WorkstationDefinition> FindWorkstationDefinition;

        /// <summary>Component view lookup for workstation evaluation. May be null.</summary>
        public Func<string, WorkstationComponentView?> FindComponent;

        /// <summary>Support context supplier (fuel, consumables, skills...). May be null.</summary>
        public Func<SupportContext> SupportSupplier;

        /// <summary>
        /// Pass IV: the unified reservation authority for instance-level
        /// availability checks (one plow cannot satisfy N simultaneous tasks).
        /// May be null — without it the gate falls back to the asset/kit-level
        /// ReservedBy flags (kind+condition+asset-reserved).
        /// </summary>
        public Func<IUnifiedReservationAuthority> ReservationsSupplier;

        /// <summary>
        /// Pass IV: absolute game seconds (TimeManager timeline) for reservation
        /// overlap checks. May be null — the gate then uses
        /// dayIndex × 86400 (start of the task's day).
        /// </summary>
        public Func<float> NowGameSecondsSupplier;

        /// <summary>TaskAuthority (or any definition lookup) for resolving ids. May be null if definitions are passed directly.</summary>
        public Func<string, TaskDefinition> FindDefinition;

        /// <summary>
        /// Physical hands campaign P1c: hand occupancy snapshots by assignee
        /// person id, from HandOccupancyAuthority.Current, or null for an
        /// unknown person. May be null — without it the hand check is skipped
        /// entirely, never a refusal. The gate reads snapshots only; it never
        /// allocates or releases a hand.
        /// </summary>
        public Func<int, HandOccupancy> HandOccupancySupplier;

        public EquipmentTaskGate() { }

        /// <summary>
        /// Checks raw equipment-class codes without a task definition — for
        /// execution paths (sales by weight, repairs, batches) that are not
        /// TTS-2 tasks but still need Canon 4.1 gating. Returns null when the
        /// holder may proceed, otherwise the loud refusal.
        /// </summary>
        public string CheckCodes(
            List<string> codes,
            string holderKind,
            string holderId,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (codes == null || codes.Count == 0) return null;
            var availability = EquipmentRequirements.ForHolder(
                holderKind, holderId, dayIndex,
                AssetsSupplier != null ? AssetsSupplier() : null,
                KitsSupplier != null ? KitsSupplier() : null,
                AccessResolver);
            availability.Support = SupportSupplier != null ? SupportSupplier() : null;
            if (WorkstationsFor != null)
                availability.Workstations = WorkstationsFor(holderKind, holderId);
            availability.FindWorkstationDefinition = FindWorkstationDefinition;
            availability.FindComponent = FindComponent;
            WireReservations(availability, dayIndex);

            // Build a lightweight definition carrying just the codes.
            var stub = new TaskDefinition("nx1a-direct-check", "direct equipment check", 1);
            foreach (string code in codes)
                if (!string.IsNullOrWhiteSpace(code)) stub.EquipmentClasses.Add(code);
            return EquipmentRequirements.CheckTaskRequirements(stub, availability, diagnostics);
        }

        /// <summary>
        /// Direct check for domain work-execution methods. Returns null when the
        /// holder may execute, otherwise the loud refusal naming the missing
        /// requirement. holderKind uses the OwnerKind convention
        /// ("business", "household", "person").
        /// </summary>
        public string CheckExecution(
            string taskDefinitionId,
            TaskDefinition definition,
            string holderKind,
            string holderId,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (definition == null && FindDefinition != null)
                definition = FindDefinition(taskDefinitionId);
            if (definition == null)
            {
                string missing = $"EquipmentTaskGate: unknown task definition '{taskDefinitionId}' — refused, never assumed.";
                diagnostics.Add(missing);
                return missing;
            }

            var availability = EquipmentRequirements.ForHolder(
                holderKind, holderId, dayIndex,
                AssetsSupplier != null ? AssetsSupplier() : null,
                KitsSupplier != null ? KitsSupplier() : null,
                AccessResolver);
            availability.Support = SupportSupplier != null ? SupportSupplier() : null;
            if (WorkstationsFor != null)
                availability.Workstations = WorkstationsFor(holderKind, holderId);
            availability.FindWorkstationDefinition = FindWorkstationDefinition;
            availability.FindComponent = FindComponent;
            WireReservations(availability, dayIndex);

            string refusal = EquipmentRequirements.CheckTaskRequirements(definition, availability, diagnostics);

            // Task-level support requirements (beyond workstation ones), parsed
            // from the definition's string specs (Core keeps no Equipment dep).
            if (refusal == null)
            {
                var parsed = ParseSupportSpecs(definition.SupportRequirementSpecs);
                if (parsed.Count > 0)
                {
                    var support = Supportability.Evaluate(parsed, availability.Support);
                    if (!support.Satisfied)
                    {
                        refusal = $"Task '{definition.DefinitionId}' not supportable — " +
                                  string.Join(" ", support.Reasons) + " (Canon 5.2).";
                        diagnostics.Add(refusal);
                    }
                }
            }

            return refusal;
        }

        /// <summary>
        /// Pass IV: wires the unified reservation authority into an availability
        /// context. Absolute game seconds come from NowGameSecondsSupplier when
        /// set, else from the task's day index (start of day).
        /// </summary>
        private void WireReservations(EquipmentAvailability availability, int dayIndex)
        {
            availability.Reservations = ReservationsSupplier != null ? ReservationsSupplier() : null;
            availability.ReservationAtGameSeconds = NowGameSecondsSupplier != null
                ? NowGameSecondsSupplier()
                : (float)dayIndex * 86400f;
        }

        /// <summary>
        /// Parses "kind:detail:unitsPerDay" specs. Malformed entries refuse
        /// loudly — never assumed (Canon 5.2).
        /// </summary>
        private static List<SupportRequirement> ParseSupportSpecs(List<string> specs)
        {
            var result = new List<SupportRequirement>();
            if (specs == null) return result;
            foreach (string spec in specs)
            {
                if (string.IsNullOrWhiteSpace(spec)) continue;
                string[] parts = spec.Split(':');
                if (parts.Length < 2)
                    throw new ArgumentException(
                        $"EquipmentTaskGate: malformed support spec '{spec}' — expected 'kind:detail[:unitsPerDay]'.");
                int units = 0;
                if (parts.Length >= 3) int.TryParse(parts[2], out units);
                result.Add(new SupportRequirement(parts[0].Trim(), parts[1].Trim(), units));
            }
            return result;
        }

        /// <summary>
        /// ITaskExecutionGate: holder is the task's owner (usually the business).
        /// EntityKind maps to the OwnerKind convention ("business", ...).
        /// </summary>
        string ITaskExecutionGate.CheckStart(WorkTask task, TaskDefinition definition, int dayIndex)
        {
            if (task == null) return "EquipmentTaskGate: no task.";
            string holderKind = task.OwnerId.Kind.ToString().ToLowerInvariant();
            string holderId = task.OwnerId.Id.ToString();
            var diagnostics = new List<string>();
            string equipmentRefusal = CheckExecution(task.DefinitionId, definition, holderKind, holderId, dayIndex, diagnostics);
            if (equipmentRefusal != null) return equipmentRefusal;
            return CheckHands(task, definition);
        }

        /// <summary>
        /// Freight prefix covering the freight load and unload definition ids
        /// ("freight:load:" / "freight:unload:") plus the freight-service
        /// meeting and freight-dispatch assignment ids.
        /// </summary>
        private const string FreightDefinitionPrefix = "freight";

        /// <summary>
        /// General store unload-freight definition id
        /// (GeneralStoreTaskCatalog.UnloadFreightId), named here as a literal
        /// so this gate takes no dependency on the FirstLedger catalog.
        /// </summary>
        private const string UnloadFreightDefinitionId = "gs.unload-freight";

        /// <summary>
        /// Physical hands campaign P1c: check-only hand gate, run second after
        /// the equipment check. Reads the definition hand requirement spec and
        /// the assignee hand snapshot, and refuses loudly when the needed free
        /// hands are not available. Never allocates, never releases, never
        /// mutates hand state. Freight definitions are excluded outright, so a
        /// later P5 authoring cannot accidentally gate freight.
        /// </summary>
        private string CheckHands(WorkTask task, TaskDefinition definition)
        {
            if (definition == null) return null;
            string spec = definition.HandRequirementSpec;
            if (string.IsNullOrWhiteSpace(spec)) return null;
            if (IsFreightDefinition(definition)) return null;
            if (!task.HasAssignee) return null;
            if (HandOccupancySupplier == null) return null;
            HandOccupancy occupancy = HandOccupancySupplier(task.AssigneeId.Id);
            if (occupancy == null) return null;

            string trimmed = spec.Trim();
            int neededFreeHands;
            if (trimmed == "one-handed") neededFreeHands = 1;
            else if (trimmed == "two-handed") neededFreeHands = 2;
            else return "EquipmentTaskGate: task '" + definition.DefinitionId +
                "' has unknown hand requirement spec '" + spec +
                "' — refused, never assumed.";
            if (occupancy.FreeHandCount >= neededFreeHands) return null;

            string need = neededFreeHands == 1
                ? "needs one free hand — no hand is free"
                : "needs both hands free";
            return "EquipmentTaskGate: task '" + definition.DefinitionId +
                "' for person " + task.AssigneeId.Id + " " + need +
                ": " + DescribeOccupiedHands(occupancy) + " — refused.";
        }

        private static bool IsFreightDefinition(TaskDefinition definition)
        {
            string id = definition.DefinitionId ?? string.Empty;
            if (id.StartsWith(FreightDefinitionPrefix, StringComparison.Ordinal)) return true;
            if (id == UnloadFreightDefinitionId) return true;
            if (definition.DomainTags != null && definition.DomainTags.Contains("freight")) return true;
            return false;
        }

        private static string DescribeOccupiedHands(HandOccupancy occupancy)
        {
            var parts = new List<string>();
            if (!occupancy.IsLeftFree)
                parts.Add("left hand holds lot " + occupancy.LeftLotKey +
                    (string.IsNullOrEmpty(occupancy.LeftBatchId) ? string.Empty : " (batch " + occupancy.LeftBatchId + ")"));
            if (!occupancy.IsRightFree)
                parts.Add("right hand holds lot " + occupancy.RightLotKey +
                    (string.IsNullOrEmpty(occupancy.RightBatchId) ? string.Empty : " (batch " + occupancy.RightBatchId + ")"));
            return string.Join("; ", parts.ToArray());
        }
    }
}
