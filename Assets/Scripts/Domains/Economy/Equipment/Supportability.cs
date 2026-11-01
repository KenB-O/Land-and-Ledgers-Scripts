using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-1: one supportability requirement layered over equipment condition
    /// (Canon 5.2). Equipment may be owned yet temporarily unusable because fuel,
    /// parts, consumables, compatible infrastructure, trained operators, or repair
    /// capability are unavailable. A fully-repaired oven with no fuel is not usable.
    /// </summary>
    [Serializable]
    public sealed class SupportRequirement
    {
        /// <summary>"fuel", "consumable", "operator-skill", "repair-capability", "infrastructure".</summary>
        public string Kind = string.Empty;
        /// <summary>Detail, e.g. "forge-coal", "smithing>=2", "water-power".</summary>
        public string Detail = string.Empty;
        public int UnitsPerDay;

        public SupportRequirement() { }

        public SupportRequirement(string kind, string detail, int unitsPerDay = 0)
        {
            Kind = kind ?? string.Empty;
            Detail = detail ?? string.Empty;
            UnitsPerDay = Math.Max(0, unitsPerDay);
        }
    }

    /// <summary>
    /// EQP-1: what the operator/business can actually field today — fuel and
    /// consumable stocks, trained operator skills, repair capabilities on hand,
    /// compatible infrastructure available.
    /// </summary>
    [Serializable]
    public sealed class SupportContext
    {
        /// <summary>"fuel:forge-coal" → units on hand. Key format "kind:detail".</summary>
        public Dictionary<string, int> ConsumableUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> AvailableOperatorSkills = new List<string>();
        public List<string> AvailableRepairCapabilities = new List<string>();
        public List<string> AvailableInfrastructure = new List<string>();

        public static string Key(string kind, string detail) => (kind ?? "") + ":" + (detail ?? "");
    }

    public sealed class SupportabilityReport
    {
        public bool Satisfied = true;
        public List<string> Reasons = new List<string>();
    }

    /// <summary>
    /// EQP-1: evaluates supportability (Canon 5.2). Layered OVER condition:
    /// callers should check asset condition first, then supportability.
    /// </summary>
    public static class Supportability
    {
        public static SupportabilityReport Evaluate(
            List<SupportRequirement> requirements,
            SupportContext context)
        {
            var report = new SupportabilityReport();
            if (requirements == null || requirements.Count == 0) return report;
            if (context == null)
            {
                report.Satisfied = false;
                report.Reasons.Add("Supportability: no support context — nothing is supportable without one (Canon 5.2).");
                return report;
            }

            foreach (var req in requirements)
            {
                string key = SupportContext.Key(req.Kind, req.Detail);
                switch (req.Kind)
                {
                    case "fuel":
                    case "consumable":
                        int onHand = 0;
                        context.ConsumableUnits.TryGetValue(key, out onHand);
                        if (onHand < Math.Max(1, req.UnitsPerDay))
                        {
                            report.Satisfied = false;
                            report.Reasons.Add($"Supportability: needs {req.Detail} ({req.Kind}) — {onHand} on hand (Canon 5.2).");
                        }
                        break;
                    case "operator-skill":
                        if (!context.AvailableOperatorSkills.Contains(req.Detail))
                        {
                            report.Satisfied = false;
                            report.Reasons.Add($"Supportability: no trained operator with '{req.Detail}' available (Canon 5.2).");
                        }
                        break;
                    case "repair-capability":
                        if (!context.AvailableRepairCapabilities.Contains(req.Detail))
                        {
                            report.Satisfied = false;
                            report.Reasons.Add($"Supportability: no repair capability '{req.Detail}' available (Canon 5.2).");
                        }
                        break;
                    case "infrastructure":
                        if (!context.AvailableInfrastructure.Contains(req.Detail))
                        {
                            report.Satisfied = false;
                            report.Reasons.Add($"Supportability: compatible infrastructure '{req.Detail}' unavailable (Canon 5.2).");
                        }
                        break;
                    default:
                        report.Satisfied = false;
                        report.Reasons.Add($"Supportability: unknown requirement kind '{req.Kind}' — refused, never assumed (Canon 5.2).");
                        break;
                }
            }

            return report;
        }

        /// <summary>
        /// Convenience: full operational check — condition AND supportability.
        /// </summary>
        public static bool IsOperational(
            bool conditionUsable,
            List<SupportRequirement> requirements,
            SupportContext context,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!conditionUsable)
            {
                diagnostics.Add("Equipment not operational: condition below usable threshold (Tech X §3.9).");
                return false;
            }
            var report = Evaluate(requirements, context);
            if (!report.Satisfied)
            {
                diagnostics.AddRange(report.Reasons);
                return false;
            }
            return true;
        }
    }
}
