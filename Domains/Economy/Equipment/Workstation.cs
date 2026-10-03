using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-1: one required component of a workstation (Tech X §3.5). The kind
    /// matches <see cref="Blacksmith.EquipmentAsset"/>.Kind ("anvil", "bake-oven-chamber", ...).
    /// </summary>
    [Serializable]
    public sealed class WorkstationComponentRequirement
    {
        public string EquipmentKind = string.Empty;
        public int Count = 1;
        public float MinCondition01 = 0.05f;

        public WorkstationComponentRequirement() { }

        public WorkstationComponentRequirement(string equipmentKind, int count = 1, float minCondition01 = 0.05f)
        {
            EquipmentKind = equipmentKind ?? string.Empty;
            Count = Math.Max(1, count);
            MinCondition01 = minCondition01;
        }
    }

    /// <summary>
    /// EQP-1: workstation definition as DATA (Tech X §3.5, §3.10). A workstation is a
    /// fixed productive setup whose capabilities derive from actual component
    /// assets and space — never granted by the room alone.
    /// </summary>
    [Serializable]
    public sealed class WorkstationDefinition
    {
        public string WorkstationId = string.Empty; // "forge-station", "bake-oven"
        public string DisplayName = string.Empty;
        public List<WorkstationComponentRequirement> Components = new List<WorkstationComponentRequirement>();
        public string RequiredSpaceKind = string.Empty;  // "smithy", "bakehouse"; empty = any suitable space
        public string RequiredMotivePowerKind = string.Empty; // MotivePowerKind name; empty = none/human
        /// <summary>EQP-2: acceptable motive power kinds (Tech X §3.6) — e.g. water OR steam for a mill.</summary>
        public List<string> AcceptableMotivePowerKinds = new List<string>();
        public List<string> CapabilitiesGranted = new List<string>(); // task/capability ids
        /// <summary>EQP-2: supportability requirements layered over condition (Canon 5.2).</summary>
        public List<SupportRequirement> SupportRequirements = new List<SupportRequirement>();
        public string SourceNote = string.Empty; // e.g. "Tech X §3.5: BakeOven"

        public WorkstationDefinition() { }

        /// <summary>
        /// EQP-2: true when no motive power is constrained, or a recorded available
        /// source matches the required kind or one of the acceptable kinds.
        /// </summary>
        public bool MotivePowerSatisfied(List<MotivePowerSource> sources, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (AcceptableMotivePowerKinds != null && AcceptableMotivePowerKinds.Count > 0)
            {
                foreach (string kind in AcceptableMotivePowerKinds)
                {
                    if (MotivePower.IsAvailable(kind, sources, new List<string>())) return true;
                }
                diagnostics.Add($"Workstation {WorkstationId}: needs motive power {string.Join("/", AcceptableMotivePowerKinds)} — none available (Tech X §3.6).");
                return false;
            }
            return MotivePower.IsAvailable(RequiredMotivePowerKind, sources, diagnostics);
        }
    }

    /// <summary>
    /// EQP-1: a lightweight view of a component asset for workstation evaluation,
    /// so the Equipment domain does not take a hard dependency on the Blacksmith
    /// namespace's concrete asset class.
    /// </summary>
    public struct WorkstationComponentView
    {
        public string AssetId;
        public string Kind;
        public float Condition01;
        public bool IsUsable;
    }

    /// <summary>
    /// EQP-1: one workstation installed in a business's functional space.
    /// Readiness is DERIVED from components (Tech X §3.5) — never a bare flag.
    /// </summary>
    [Serializable]
    public sealed class WorkstationInstance
    {
        public string InstanceId = string.Empty;
        public string WorkstationId = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public string SpaceId = string.Empty; // the functional space it lives in
        public List<string> ComponentAssetIds = new List<string>();

        /// <summary>
        /// Derives readiness from actual components. Returns null when ready;
        /// otherwise a human-readable reason naming what's missing or unusable.
        /// A functional space alone never grants the workstation.
        /// </summary>
        public string EvaluateReady(
            WorkstationDefinition def,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (def == null)
                return "WorkstationInstance: no definition to evaluate against.";
            if (string.IsNullOrWhiteSpace(SpaceId))
                return $"Workstation {def.DisplayName}: no functional space assigned — a space alone does not grant the workstation (Tech X §3.5).";

            foreach (var req in def.Components)
            {
                int satisfied = 0;
                foreach (string assetId in ComponentAssetIds)
                {
                    var view = findComponent != null ? findComponent(assetId) : null;
                    if (!view.HasValue) continue;
                    if (!string.Equals(view.Value.Kind, req.EquipmentKind, StringComparison.Ordinal)) continue;
                    if (view.Value.Condition01 >= req.MinCondition01 && view.Value.IsUsable) satisfied++;
                }
                if (satisfied < req.Count)
                {
                    string reason = $"Workstation {def.DisplayName}: needs {req.Count}× {req.EquipmentKind} at condition ≥ {req.MinCondition01:0.00} — only {satisfied} present and usable (Tech X §3.5).";
                    diagnostics.Add(reason);
                    return reason;
                }
            }

            return null; // ready
        }

        public void InstallComponent(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId)) return;
            if (!ComponentAssetIds.Contains(assetId))
                ComponentAssetIds.Add(assetId);
        }

        public void RemoveComponent(string assetId)
        {
            ComponentAssetIds.Remove(assetId);
        }
    }
}
