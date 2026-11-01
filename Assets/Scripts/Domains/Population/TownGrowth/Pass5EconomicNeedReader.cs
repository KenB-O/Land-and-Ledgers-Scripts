using System;
using System.Collections.Generic;

namespace LandLedgers.Population.TownGrowth
{
    /// <summary>
    /// Pass 5 (town growth): economic-need reader. How does the town
    /// know it needs a driver, a baker, a smith, without inventing
    /// a headcount quota?
    ///
    /// Answer, enforced by this file's shape: it never outputs a
    /// headcount. There is no Headcount field anywhere in this file.
    /// It outputs evidence and a rank: player-stated need opens an
    /// effort, task evidence ranks which roles to ask about first.
    /// LaborDemandAssessor and Pass2MissedWorkReport are the evidence
    /// sources; this reader does not replace them, does not hire,
    /// and does not assign anyone. It reads caller-supplied
    /// snapshots of those reports - it has no production caller
    /// feeding it live TaskAuthority state today, and it says so.
    ///
    /// Family-to-role mapping is caller-supplied, never invented.
    /// A task family with no mapping is reported as UnmappedFamily,
    /// not guessed into a role. Sustained-pattern thresholds are
    /// caller-supplied parameters, never canon constants.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public enum Pass5NeedSource
    {
        PlayerStated = 0,
        TaskEvidence = 1,
        Both = 2,
    }

    public sealed class Pass5TaskFamilyEvidence
    {
        public string Family = string.Empty;
        public int UnmetMinutes;
        public int BalkedDemandCount;
        public int SustainedDays;
    }

    public sealed class Pass5PlayerStatedNeed
    {
        public string BusinessId = string.Empty;
        public string RoleId = string.Empty;
        public string RoleDisplayName = string.Empty; // caller-supplied, from real business roles
    }

    public sealed class Pass5RoleNeedRank
    {
        public string RoleId = string.Empty;
        public string RoleDisplayName = string.Empty;
        public string Family = string.Empty;
        public int UnmetMinutes;
        public int BalkedDemandCount;
        public int SustainedDays;
        public bool MeetsSustainedThreshold;
        public Pass5NeedSource Source = Pass5NeedSource.PlayerStated;
        public bool IsUnmappedFamily;
        public List<string> Reasons = new List<string>();
    }

    public static class Pass5EconomicNeedReader
    {
        /// <summary>
        /// Ranks roles by evidence. Player-stated needs always
        /// appear (they open an effort); task evidence ranks them
        /// and adds roles the player did not state, but only when
        /// the sustained pattern meets the caller threshold.
        /// A role with task evidence below threshold is reported
        /// as NotSustained, never as a need. No headcount is ever
        /// computed: unmet minutes and balks are evidence, not a
        /// staffing order.
        /// </summary>
        public static List<Pass5RoleNeedRank> Rank(
            List<Pass5TaskFamilyEvidence> taskEvidence,
            Dictionary<string, string> familyToRoleId,
            Dictionary<string, string> familyToRoleDisplayName,
            List<Pass5PlayerStatedNeed> playerStatedNeeds,
            int sustainedDaysThreshold,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var ranks = new List<Pass5RoleNeedRank>();
            var byRole = new Dictionary<string, Pass5RoleNeedRank>();

            if (playerStatedNeeds != null)
            {
                foreach (var need in playerStatedNeeds)
                {
                    if (need == null || string.IsNullOrWhiteSpace(need.RoleId)) continue;
                    var rank = new Pass5RoleNeedRank
                    {
                        RoleId = need.RoleId,
                        RoleDisplayName = need.RoleDisplayName ?? string.Empty,
                        Source = Pass5NeedSource.PlayerStated,
                    };
                    rank.Reasons.Add("Pass5Need: player-stated need opens an effort; it is not, by itself, proof the town lacks that role.");
                    byRole[need.RoleId] = rank;
                    ranks.Add(rank);
                }
            }

            if (taskEvidence != null)
            {
                foreach (var evidence in taskEvidence)
                {
                    if (evidence == null) continue;
                    string roleId = null;
                    string roleDisplay = null;
                    if (familyToRoleId != null) familyToRoleId.TryGetValue(evidence.Family ?? string.Empty, out roleId);
                    if (familyToRoleDisplayName != null) familyToRoleDisplayName.TryGetValue(evidence.Family ?? string.Empty, out roleDisplay);
                    if (string.IsNullOrWhiteSpace(roleId))
                    {
                        var unmapped = new Pass5RoleNeedRank
                        {
                            Family = evidence.Family ?? string.Empty,
                            UnmetMinutes = Math.Max(0, evidence.UnmetMinutes),
                            BalkedDemandCount = Math.Max(0, evidence.BalkedDemandCount),
                            SustainedDays = Math.Max(0, evidence.SustainedDays),
                            IsUnmappedFamily = true,
                            Source = Pass5NeedSource.TaskEvidence,
                        };
                        unmapped.Reasons.Add("Pass5Need: task family '" + (evidence.Family ?? string.Empty) + "' has no caller-supplied family-to-role mapping; role not guessed, no need emitted for it.");
                        ranks.Add(unmapped);
                        continue;
                    }
                    Pass5RoleNeedRank existing;
                    if (!byRole.TryGetValue(roleId, out existing))
                    {
                        existing = new Pass5RoleNeedRank { RoleId = roleId, RoleDisplayName = roleDisplay ?? string.Empty, Family = evidence.Family ?? string.Empty };
                        byRole[roleId] = existing;
                        ranks.Add(existing);
                    }
                    existing.UnmetMinutes += Math.Max(0, evidence.UnmetMinutes);
                    existing.BalkedDemandCount += Math.Max(0, evidence.BalkedDemandCount);
                    existing.SustainedDays = Math.Max(existing.SustainedDays, Math.Max(0, evidence.SustainedDays));
                    existing.Source = existing.Source == Pass5NeedSource.PlayerStated ? Pass5NeedSource.Both : Pass5NeedSource.TaskEvidence;
                }
            }

            foreach (var rank in ranks)
            {
                if (rank.IsUnmappedFamily) continue;
                rank.MeetsSustainedThreshold = rank.SustainedDays >= sustainedDaysThreshold
                    && (rank.UnmetMinutes > 0 || rank.BalkedDemandCount > 0);
                if (!rank.MeetsSustainedThreshold && rank.Source == Pass5NeedSource.TaskEvidence)
                {
                    rank.Reasons.Add("Pass5Need: task evidence for '" + rank.RoleId + "' does not meet the caller sustained threshold (" + sustainedDaysThreshold + " days); reported, not a need.");
                }
                if (rank.Source == Pass5NeedSource.Both)
                {
                    rank.Reasons.Add("Pass5Need: player-stated and task evidence agree for '" + rank.RoleId + "'; task evidence ranks it, player need opened it.");
                }
            }

            // Rank: sustained task-evidence needs first (most unmet
            // minutes), then player-stated needs, then the rest.
            // This is a display order, never a quota order.
            ranks.Sort(delegate(Pass5RoleNeedRank a, Pass5RoleNeedRank b)
            {
                int aScore = (a.MeetsSustainedThreshold ? 2 : 0) + (a.Source == Pass5NeedSource.Both ? 1 : 0);
                int bScore = (b.MeetsSustainedThreshold ? 2 : 0) + (b.Source == Pass5NeedSource.Both ? 1 : 0);
                if (bScore != aScore) return bScore.CompareTo(aScore);
                return b.UnmetMinutes.CompareTo(a.UnmetMinutes);
            });
            diagnostics.Add("Pass5Need: no production caller feeds live TaskAuthority state into this reader today; snapshots above are caller-supplied, and no headcount was computed.");
            return ranks;
        }
    }
}
