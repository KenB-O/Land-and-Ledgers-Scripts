using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment.Workstations;
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

        /// <summary>TaskAuthority (or any definition lookup) for resolving ids. May be null if definitions are passed directly.</summary>
        public Func<string, TaskDefinition> FindDefinition;

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
            return CheckExecution(task.DefinitionId, definition, holderKind, holderId, dayIndex, diagnostics);
        }
    }
}
