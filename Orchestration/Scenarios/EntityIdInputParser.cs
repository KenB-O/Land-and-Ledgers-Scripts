using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-2: parses puppet-master entity input ("P12", "H3", "BLD7") back into an
    /// <see cref="EntityId"/>. Self-contained in Scenarios so HF-1's catalog file stays
    /// untouched. Longest-prefix match first ("PL12" is Plot 12, not Person "L12").
    ///
    /// Custom extension kinds (values >= CustomBase) use the fallback "K&lt;value&gt;:&lt;id&gt;"
    /// form, e.g. "K1000:5". Documented limitation of this dev tool, not the framework.
    /// </summary>
    public static class EntityIdInputParser
    {
        public static bool TryParse(string input, out EntityId entityId)
        {
            entityId = EntityId.Invalid;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string trimmed = input.Trim();

            // Fallback form for custom kinds: K<value>:<id>
            if ((trimmed.StartsWith("K", StringComparison.OrdinalIgnoreCase)) && trimmed.Contains(":"))
            {
                return TryParseCustomForm(trimmed, out entityId);
            }

            // Longest-prefix match over first-class kinds.
            var candidates = new List<KeyValuePair<EntityKind, string>>();
            foreach (EntityKind kind in Enum.GetValues(typeof(EntityKind)))
            {
                if (kind == EntityKind.Unspecified || kind == EntityKind.CustomBase)
                {
                    continue;
                }

                if ((int)kind >= (int)EntityKind.CustomBase)
                {
                    continue;
                }

                if (EntityKindCatalog.TryGetPrefix(kind, out string prefix) && !string.IsNullOrEmpty(prefix))
                {
                    candidates.Add(new KeyValuePair<EntityKind, string>(kind, prefix));
                }
            }

            candidates.Sort((a, b) => b.Value.Length.CompareTo(a.Value.Length));

            foreach (KeyValuePair<EntityKind, string> candidate in candidates)
            {
                if (trimmed.StartsWith(candidate.Value, StringComparison.OrdinalIgnoreCase))
                {
                    string numeric = trimmed.Substring(candidate.Value.Length);
                    if (int.TryParse(numeric, out int id) && id >= 0)
                    {
                        entityId = EntityId.For(candidate.Key, id);
                        return true;
                    }

                    return false;
                }
            }

            return false;
        }

        private static bool TryParseCustomForm(string trimmed, out EntityId entityId)
        {
            entityId = EntityId.Invalid;

            string body = trimmed.Substring(1);
            string[] parts = body.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out int kindValue) || kindValue < (int)EntityKind.CustomBase)
            {
                return false;
            }

            if (!int.TryParse(parts[1], out int id) || id < 0)
            {
                return false;
            }

            entityId = EntityId.For((EntityKind)kindValue, id);
            return true;
        }
    }
}
