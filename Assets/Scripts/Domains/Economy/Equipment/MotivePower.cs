using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-1: motive power kinds that are genuine constraints (Tech X §3.6).
    /// "Represent motive power separately where it is a genuine constraint:
    /// human, horse, mule, ox, water, steam or electric." Only horse draft was
    /// modeled before (EQU-3); sawmill steam/water and mill water/steam had no
    /// representation until now.
    /// </summary>
    public enum MotivePowerKind
    {
        Human = 0,
        Horse = 1,
        Mule = 2,
        Ox = 3,
        Water = 4,
        Steam = 5,
        Electric = 6,
    }

    /// <summary>
    /// EQP-1: one motive-power source available to a business or workstation —
    /// a team of horses, a mill race, a steam engine. Availability is explicit;
    /// an unavailable source satisfies nothing.
    /// </summary>
    [Serializable]
    public sealed class MotivePowerSource
    {
        public MotivePowerKind Kind = MotivePowerKind.Human;
        public string Description = string.Empty; // "mill race", "team of 2 horses", "6hp steam engine"
        public bool Available = true;
        public string ProviderId = string.Empty; // who/what provides it (business, asset, animal team)

        public MotivePowerSource() { }

        public MotivePowerSource(MotivePowerKind kind, string description, bool available = true)
        {
            Kind = kind;
            Description = description ?? string.Empty;
            Available = available;
        }
    }

    /// <summary>
    /// EQP-1: resolves whether a required motive power kind is available.
    /// Human power is assumed available wherever a capable person is present;
    /// every other kind needs a recorded, available source.
    /// </summary>
    public static class MotivePower
    {
        public static bool IsAvailable(
            string requiredKindName,
            List<MotivePowerSource> sources,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(requiredKindName))
                return true; // no motive-power constraint
            if (string.Equals(requiredKindName, nameof(MotivePowerKind.Human), StringComparison.OrdinalIgnoreCase))
                return true; // the capable person IS the power

            if (sources != null)
            {
                foreach (var source in sources)
                {
                    if (source == null || !source.Available) continue;
                    if (string.Equals(source.Kind.ToString(), requiredKindName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            diagnostics.Add($"Motive power '{requiredKindName}' unavailable — no recorded, available source (Tech X §3.6).");
            return false;
        }
    }
}
