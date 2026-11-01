using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Pass 2 (task system): a unified, console-first missed-work report.
    /// One place that says what was not done, what was not finished, and
    /// what a worker was not able to get done — including idle time and
    /// demand that balked — instead of scattering meal counts, farm
    /// overload queues, and diagnostic strings across systems. This is
    /// the report API for Codex/Unity on-screen UI and NPC messages:
    /// Unity work reads MissedItems and formats them; this pass does
    /// NOT build UI, and does NOT claim NPC messages exist.
    ///
    /// Hiring pressure is deliberately NOT derived here: that remains
    /// LaborDemandAssessor's job, from unmet task minutes, recurring
    /// balks, and queue pressure — never from a headcount quota, and
    /// never from idle minutes (see Pass2IdleActivity).
    /// </summary>
    public sealed class Pass2MissedWorkItem
    {
        public string TaskDefinitionId = string.Empty;
        public string OwnerDescription = string.Empty;
        public string Reason = string.Empty;
        public bool WasDemandBalked;

        public Pass2MissedWorkItem() { }
    }

    public sealed class Pass2MissedWorkReport
    {
        private readonly List<Pass2MissedWorkItem> items = new List<Pass2MissedWorkItem>();

        public IReadOnlyList<Pass2MissedWorkItem> Items => items;
        public int IdleMinutes { get; private set; }
        public int DemandBalkedCount { get; private set; }

        public void AddMissed(string taskDefinitionId, string ownerDescription, string reason)
        {
            items.Add(new Pass2MissedWorkItem
            {
                TaskDefinitionId = taskDefinitionId ?? string.Empty,
                OwnerDescription = ownerDescription ?? string.Empty,
                Reason = reason ?? "Not completed.",
                WasDemandBalked = false,
            });
        }

        public void AddBalkedDemand(string taskDefinitionId, string ownerDescription, string reason)
        {
            items.Add(new Pass2MissedWorkItem
            {
                TaskDefinitionId = taskDefinitionId ?? string.Empty,
                OwnerDescription = ownerDescription ?? string.Empty,
                Reason = reason ?? "Demand balked: no worker, no stock, or no seller available.",
                WasDemandBalked = true,
            });
            DemandBalkedCount++;
        }

        /// <summary>
        /// Idle minutes are REPORTED, never counted as missed work and
        /// never fed to hiring pressure. Standing by while no demand
        /// exists is correct behaviour, not a failure.
        /// </summary>
        public void RecordIdleMinutes(int minutes)
        {
            IdleMinutes += Math.Max(0, minutes);
        }

        public void AddFromSequenceDay(Pass2TaskSequenceDay day, string ownerDescription)
        {
            if (day == null) return;
            foreach (var entry in day.MissedEntries())
            {
                AddMissed(entry.TaskDefinitionId, ownerDescription, entry.MissedReason);
            }
        }

        /// <summary>Console-first rendering: plain lines, no UI dependency, no invented counts.</summary>
        public string RenderConsoleReport(string header)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(header) ? "Missed work report" : header);
            if (items.Count == 0)
            {
                sb.AppendLine("  Nothing missed: every sequenced task completed, and no demand balked.");
            }
            else
            {
                foreach (var item in items)
                {
                    string kind = item.WasDemandBalked ? "balked demand" : "missed task";
                    sb.AppendLine($"  - [{kind}] {item.TaskDefinitionId} ({item.OwnerDescription}): {item.Reason}");
                }
            }
            sb.AppendLine($"  Idle (standing by, not missed work): {IdleMinutes} minutes.");
            sb.AppendLine($"  Demand balked: {DemandBalkedCount} item(s).");
            return sb.ToString();
        }
    }
}
