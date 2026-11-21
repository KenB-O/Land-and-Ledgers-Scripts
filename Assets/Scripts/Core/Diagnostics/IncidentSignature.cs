using System;
using System.Text;

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// Lane 1 (§4/§17, W R-1/R-8) — stable signature ids and dedup keys for
    /// the incident infrastructure.
    ///
    /// Signature ids: the checklist's CI-* ids are reused verbatim wherever
    /// they exist (e.g. CI-C98D8F66BE for the StartNotWalkable family), so a
    /// Lane 1 incident and a checklist row are the same identity. Anything
    /// else derives a stable "SIG-" id from its normalised key with FNV-1a —
    /// deterministic across runs and processes (string.GetHashCode is not).
    ///
    /// Dedup keys: LogRefusalDayKeyed keys on the full refusal line, so
    /// volatile content (task ids, times, person ids) makes every distinct
    /// line a distinct key and ~54 refusal lines warn per day. Normalise
    /// strips volatile runs (digit runs, GUID-like hex runs) so one signature
    /// warns once per day with an occurrence count ("×N occurrences") while
    /// the incident record keeps the per-occurrence correlation ids.
    /// </summary>
    public static class IncidentSignature
    {
        /// <summary>Prefix of a checklist signature id (reused verbatim).</summary>
        public const string ChecklistPrefix = "CI-";

        /// <summary>Prefix of a derived signature id.</summary>
        public const string DerivedPrefix = "SIG-";

        /// <summary>
        /// Normalises a caller-supplied signature id. A CI-* id passes
        /// through unchanged; any other non-empty id is returned as-is when
        /// it already looks stable (letters, digits, '-', '_', '.' only),
        /// otherwise it is derived from its normalised form. Null/empty
        /// returns null — the caller must derive from the summary instead.
        /// </summary>
        public static string Normalize(string signatureId)
        {
            if (string.IsNullOrWhiteSpace(signatureId))
            {
                return null;
            }

            string trimmed = signatureId.Trim();
            if (trimmed.StartsWith(ChecklistPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            bool stable = true;
            foreach (char c in trimmed)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                {
                    stable = false;
                    break;
                }
            }

            return stable ? trimmed : DerivedPrefix + StableHashHex(NormalizeDedupKey(trimmed));
        }

        /// <summary>Derives a stable signature id from an owning system + summary (used when no explicit id exists).</summary>
        public static string DeriveFromSummary(string owningSystem, string summary)
        {
            string key = NormalizeDedupKey((owningSystem ?? string.Empty) + "|" + (summary ?? string.Empty));
            return DerivedPrefix + StableHashHex(key);
        }

        /// <summary>
        /// Normalises a raw refusal/warning line into a dedup key: lowercased,
        /// digit runs and GUID-like hex runs replaced with '#', whitespace
        /// collapsed. "task T9 refused day 7" and "task T10 refused day 8"
        /// normalise to the same key; distinct causes stay distinct.
        /// </summary>
        public static string NormalizeDedupKey(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(raw.Length);
            bool lastWasHash = false;
            bool lastWasSpace = false;
            int i = 0;
            while (i < raw.Length)
            {
                char c = raw[i];
                if (char.IsDigit(c))
                {
                    if (!lastWasHash)
                    {
                        builder.Append('#');
                        lastWasHash = true;
                    }

                    lastWasSpace = false;
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    lastWasSpace = true;
                    lastWasHash = false;
                    i++;
                    continue;
                }

                // GUID-like run: 8+ hex chars possibly with dashes — one volatile token.
                if (IsHex(c) && HexRunLength(raw, i) >= 8)
                {
                    int run = HexRunLength(raw, i);
                    if (!lastWasHash)
                    {
                        builder.Append('#');
                        lastWasHash = true;
                    }

                    lastWasSpace = false;
                    i += run;
                    continue;
                }

                builder.Append(char.ToLowerInvariant(c));
                lastWasHash = false;
                lastWasSpace = false;
                i++;
            }

            return builder.ToString().Trim();
        }

        /// <summary>FNV-1a 32-bit hash, 8 lowercase hex chars. Deterministic across runs, processes, and platforms.</summary>
        public static string StableHashHex(string value)
        {
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offsetBasis;
            if (!string.IsNullOrEmpty(value))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= prime;
                }
            }

            return hash.ToString("x8");
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static int HexRunLength(string value, int start)
        {
            int length = 0;
            for (int i = start; i < value.Length; i++)
            {
                char c = value[i];
                if (IsHex(c) || c == '-')
                {
                    length++;
                }
                else
                {
                    break;
                }
            }

            return length;
        }
    }
}
