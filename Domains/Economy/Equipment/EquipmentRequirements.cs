using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Tasks;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-3: the equipment-class convention for TaskDefinition.EquipmentClasses.
    /// Requirements attach to tasks as DATA (Canon 4.1 CANON LOCK, Tech X §3.10):
    ///   "kit:&#60;kit-id&#62;"       — a usable toolkit of that kit with legitimate access
    ///   "asset:&#60;kind&#62;"       — a usable EquipmentAsset of that kind with legitimate access
    ///   "workstation:&#60;id&#62;"   — an established, ready workstation instance
    /// Unknown prefixes are refused, never assumed. Task authors add codes via
    /// taskDef.EquipmentClasses.Add(EquipmentRequirementCodes.Kit("barber-kit")).
    /// </summary>
    public static class EquipmentRequirementCodes
    {
        public const string KitPrefix = "kit:";
        public const string AssetPrefix = "asset:";
        public const string WorkstationPrefix = "workstation:";
        public static string Kit(string kitId) => KitPrefix + kitId;
        public static string Asset(string kind) => AssetPrefix + kind;
        public static string Workstation(string workstationId) => WorkstationPrefix + workstationId;
    }

    /// <summary>
    /// EQP-3: the equipment a holder can legitimately field — owned assets/kits
    /// plus unexpired access grants, resolved BEFORE task checks. Build with
    /// ForHolder; the per-task evaluator then checks presence + usability only.
    /// </summary>
    [Serializable]
    public sealed class EquipmentAvailability
    {
        public string HolderKind = string.Empty;
        public string HolderId = string.Empty;
        public int DayIndex;
        public List<EquipmentAsset> Assets = new List<EquipmentAsset>();
        public List<ToolKitInstance> Kits = new List<ToolKitInstance>();
        public BusinessWorkstations Workstations; // may be null
        public Func<string, WorkstationDefinition> FindWorkstationDefinition; // may be null
        public Func<string, WorkstationComponentView?> FindComponent; // may be null
        /// <summary>
        /// NX-1A: support context for Canon 5.2 layering (fuel, consumables,
        /// operator skills, repair capability, infrastructure). May be null —
        /// requirements with no context refuse per Supportability.Evaluate.
        /// </summary>
        public SupportContext Support; // may be null
    }

    /// <summary>
    /// EQP-3: evaluates task equipment requirements against availability.
    /// Condition gates, never debuffs (Tech X §3.9). Access is resolved up front
    /// by ForHolder — this class never invents it (Tech X §3.7).
    /// </summary>
    public static class EquipmentRequirements
    {
        /// <summary>
        /// Builds availability for a holder: owned assets/kits plus kinds covered
        /// by unexpired access grants (Tech X §3.7).
        /// </summary>
        public static EquipmentAvailability ForHolder(
            string holderKind, string holderId, int dayIndex,
            List<EquipmentAsset> allAssets,
            List<ToolKitInstance> allKits,
            EquipmentAccessResolver accessResolver)
        {
            var availability = new EquipmentAvailability
            {
                HolderKind = holderKind ?? string.Empty,
                HolderId = holderId ?? string.Empty,
                DayIndex = dayIndex,
            };
            if (allAssets != null)
            {
                foreach (var asset in allAssets)
                {
                    if (asset == null) continue;
                    if (IsHeldBy(asset.OwnerKind, asset.OwnerId, holderKind, holderId) ||
                        HasKindAccess(accessResolver, holderKind, holderId, asset.Kind, dayIndex))
                        availability.Assets.Add(asset);
                }
            }
            if (allKits != null)
            {
                foreach (var kit in allKits)
                {
                    if (kit == null) continue;
                    if (IsHeldBy(kit.OwnerKind, kit.OwnerId, holderKind, holderId) ||
                        HasKindAccess(accessResolver, holderKind, holderId, kit.KitId, dayIndex))
                        availability.Kits.Add(kit);
                }
            }
            return availability;
        }

        private static bool IsHeldBy(string ownerKind, string ownerId, string holderKind, string holderId) =>
            !string.IsNullOrWhiteSpace(ownerId) &&
            string.Equals(ownerKind, holderKind, StringComparison.Ordinal) &&
            string.Equals(ownerId, holderId, StringComparison.Ordinal);

        private static bool HasKindAccess(EquipmentAccessResolver resolver,
            string holderKind, string holderId, string kind, int dayIndex)
        {
            if (resolver == null || string.IsNullOrWhiteSpace(kind)) return false;
            return resolver.HasAccess(holderKind, holderId, kind,
                (k, id) => new List<string>(), dayIndex, out _);
        }

        /// <summary>
        /// Checks a task's equipment classes. Returns null when every requirement
        /// is satisfied; otherwise the first refusal reason.
        /// </summary>
        public static string CheckTaskRequirements(
            TaskDefinition task, EquipmentAvailability availability, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (task == null) return "EquipmentRequirements: no task.";
            if (availability == null) return "EquipmentRequirements: no availability context.";
            foreach (string code in task.EquipmentClasses)
            {
                string refusal = CheckOne(code, availability);
                if (refusal != null)
                {
                    diagnostics.Add(refusal);
                    return refusal;
                }
            }
            return null;
        }

        /// <summary>
        /// NX-1A: a single equipment-classes entry. Alternatives separated by
        /// '|' are OR-ed (Canon 4.7: hand methods may remain physically possible
        /// at small scale while machinery changes throughput — e.g.
        /// "asset:scythe|asset:reaper-binder"). Every alternative still needs a
        /// valid prefix; the entry passes when ANY alternative is satisfied.
        /// </summary>
        private static string CheckOne(string code, EquipmentAvailability availability)
        {
            if (string.IsNullOrWhiteSpace(code)) return null; // ignore blanks
            if (code.IndexOf('|') >= 0)
            {
                var failures = new List<string>();
                foreach (string alt in code.Split('|'))
                {
                    string trimmed = alt.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    string altRefusal = CheckSingle(trimmed, availability);
                    if (altRefusal == null) return null; // one workable method suffices
                    failures.Add(altRefusal);
                }
                return $"Task needs one of [{code}] — none available to {availability.HolderId}: " +
                       string.Join(" ", failures);
            }
            return CheckSingle(code, availability);
        }

        private static string CheckSingle(string code, EquipmentAvailability availability)
        {
            if (string.IsNullOrWhiteSpace(code)) return null; // ignore blanks
            if (code.StartsWith(EquipmentRequirementCodes.KitPrefix, StringComparison.Ordinal))
            {
                string kitId = code.Substring(EquipmentRequirementCodes.KitPrefix.Length);
                foreach (var kit in availability.Kits)
                    if (kit.KitId == kitId && kit.IsUsable) return null;
                return $"Task needs tool kit '{kitId}' usable — none available to {availability.HolderId} (Tech X §3.4, §3.9).";
            }
            if (code.StartsWith(EquipmentRequirementCodes.AssetPrefix, StringComparison.Ordinal))
            {
                string kind = code.Substring(EquipmentRequirementCodes.AssetPrefix.Length);
                foreach (var asset in availability.Assets)
                    if (asset.Kind == kind && asset.IsUsable) return null;
                return $"Task needs equipment '{kind}' usable — none available to {availability.HolderId} (Tech X §3.3, §3.9).";
            }
            if (code.StartsWith(EquipmentRequirementCodes.WorkstationPrefix, StringComparison.Ordinal))
            {
                string wsId = code.Substring(EquipmentRequirementCodes.WorkstationPrefix.Length);
                if (availability.Workstations == null || availability.FindWorkstationDefinition == null)
                    return $"Task needs workstation '{wsId}' — no workstation registry supplied (Tech X §3.5).";
                var def = availability.FindWorkstationDefinition(wsId);
                if (def == null)
                    return $"Task needs workstation '{wsId}' — unknown workstation (Tech X §3.5).";
                var station = availability.Workstations.Get(wsId);
                if (station == null)
                    return $"Task needs workstation '{wsId}' — not established (Tech X §3.5).";
                var reasons = new List<string>();
                string notReady = station.EvaluateReady(def, availability.FindComponent, reasons); // null when ready
                if (notReady != null) return notReady;
                // NX-1A: supportability layered over condition (Canon 5.2) — a
                // repaired oven with no fuel is not usable. EvaluateReady does
                // not cover SupportRequirements, so the gate does it here.
                if (def.SupportRequirements != null && def.SupportRequirements.Count > 0)
                {
                    var support = Supportability.Evaluate(def.SupportRequirements, availability.Support);
                    if (!support.Satisfied)
                        return $"Task needs workstation '{wsId}' supportable — " +
                               string.Join(" ", support.Reasons) + " (Canon 5.2).";
                }
                return null;
            }
            return $"Task equipment class '{code}' has an unknown prefix — refused, never assumed (Tech X §3.10).";
        }
    }
}
