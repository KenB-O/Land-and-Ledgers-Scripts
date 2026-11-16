// Campaign C (DEF-020): failed construction procurement strands the
// package — diagnostic slice only, Nov 15, 2026.
//
// DEF-020 as audited (C package 13): on a procurement failure FundAndStage
// marks the funding Stranded = true and adds the package to
// blockedProcurementPackages, after which every later drive returns
// "procurement previously failed … retry is blocked until
// AllowProcurementRetry is called" — and no production caller of
// AllowProcurementRetry exists. The strand is durable across save/load
// (DeriveBlockedProcurementPackages rebuilds the block from the persisted
// Stranded markers on every drive). The stall surfaces only as a
// per-project "waits" diagnostic string inside the daily construction
// diag dump, plus the unread BlockedProcurementPackageKeys property —
// no single line ever names every stranded package, how long each has
// been stranded, or why.
//
// This file is the diagnostic slice: a plain-C# tracker that remembers
// the first day each blocked key was seen and renders one day-keyed
// summary line naming every stranded package (identity + days stranded
// + recorded failure reason). It invents no retry, no cancellation, no
// escalation, and no disposition — that is owner question (r), and the
// real fix is sequenced after DEF-004. No UI is invented; a
// diagnostic/summary surface only, emitted into the driver's diag list,
// which DriveNpcConstruction logs once per day as
// "[SimulationDrivers] npc construction day N: ...".
//
// First-seen days are session-local: the driver instance survives a
// Continue, so ages persist across a Continue; on a fresh-scene load a
// new driver is constructed and a pre-load strand's first-seen day is
// the load day, so its reported age is a lower bound after a load. The
// identity and the fact of stranding are exact in both cases.
//
// Plain C#, no Unity dependency, so the Roslyn harness can execute it
// directly. The driver wiring (one TryBuildSummaryLine call in Drive
// plus the StrandedReasonForKey resolver) is thin and verified by parse
// check + structural tests; NpcConstructionDriver itself is not in the
// harness closure.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Diagnostic-only tracker for stranded construction procurement
    /// packages (DEF-020). Remembers the first day each blocked
    /// "projectId|packageId" key was seen and renders one day-keyed
    /// summary line per day. No retry, no cancellation, no disposition.
    /// </summary>
    public sealed class ConstructionProcurementStrandSummary
    {
        private readonly Dictionary<string, int> strandedSinceDay =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private int lastSummaryDayIndex = -1;

        /// <summary>
        /// Records first-seen days for blocked keys and drops keys that
        /// are no longer blocked. Idempotent; safe to call on every drive.
        /// A key blocked again later restarts its age at the new
        /// first-seen day.
        /// </summary>
        public void NoteBlockedPackages(IReadOnlyCollection<string> blockedKeys, int dayIndex)
        {
            if (blockedKeys == null) return;
            foreach (string key in blockedKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (!strandedSinceDay.ContainsKey(key))
                    strandedSinceDay[key] = dayIndex;
            }
            var live = new HashSet<string>(blockedKeys, StringComparer.Ordinal);
            var stale = new List<string>();
            foreach (string key in strandedSinceDay.Keys)
                if (!live.Contains(key)) stale.Add(key);
            foreach (string key in stale) strandedSinceDay.Remove(key);
        }

        /// <summary>
        /// Builds the once-per-day stranded-packages summary line, or
        /// returns false (line null) when nothing is blocked or the line
        /// was already built for this day. Keys are sorted so the line is
        /// byte-stable. Days stranded = dayIndex - first-seen day (0 on
        /// the first day). The reason segment is included only when the
        /// resolver returns a non-empty reason.
        /// </summary>
        public bool TryBuildSummaryLine(
            IReadOnlyCollection<string> blockedKeys,
            Func<string, string> reasonForKey,
            int dayIndex,
            out string line)
        {
            line = null;
            if (blockedKeys == null) return false;
            NoteBlockedPackages(blockedKeys, dayIndex);
            if (blockedKeys.Count == 0) return false;
            if (dayIndex == lastSummaryDayIndex) return false;
            var ordered = new List<string>(blockedKeys);
            ordered.Sort(StringComparer.Ordinal);
            var text = new StringBuilder();
            text.Append("[Construction] procurement-stranded day ");
            text.Append(dayIndex.ToString(CultureInfo.InvariantCulture));
            text.Append(": ");
            text.Append(ordered.Count.ToString(CultureInfo.InvariantCulture));
            text.Append(ordered.Count == 1 ? " package stranded: " : " packages stranded: ");
            bool first = true;
            foreach (string key in ordered)
            {
                if (!first) text.Append("; ");
                first = false;
                int sep = key.IndexOf('|');
                string projectId = sep >= 0 ? key.Substring(0, sep) : key;
                string packageId = sep >= 0 ? key.Substring(sep + 1) : string.Empty;
                int days = dayIndex - strandedSinceDay[key];
                text.Append("project '");
                text.Append(projectId);
                text.Append("' package '");
                text.Append(packageId);
                text.Append("' stranded ");
                text.Append(days.ToString(CultureInfo.InvariantCulture));
                text.Append(days == 1 ? " day" : " days");
                string reason = reasonForKey != null ? reasonForKey(key) : null;
                if (!string.IsNullOrEmpty(reason))
                {
                    text.Append(" (");
                    text.Append(reason);
                    text.Append(")");
                }
            }
            text.Append(". Retry is blocked until AllowProcurementRetry is called.");
            lastSummaryDayIndex = dayIndex;
            line = text.ToString();
            return true;
        }
    }
}
