using System;

namespace LandLedgers.Population
{
    /// <summary>
    /// Campaign C (DEF-021): pure rule for the PopulationPathingDirector agent cap.
    /// PopulateManagedAgents walks state.people in order and stops creating visual
    /// agents once agents.Count reaches maxActiveAgents. The residents it never
    /// examines are the cut set: indices [examinedCount, peopleCount). This helper
    /// only names that set so the director can log it (count, cap value, resident
    /// ids); it does not change the cap value or the walk order, and it never
    /// invents a resident. Zero Unity dependencies so the rule is unit-testable
    /// in the focused Roslyn harness.
    /// </summary>
    public static class AgentCapTruncation
    {
        /// <summary>
        /// Number of residents cut by the cap: the people the walk never examined.
        /// Zero when the walk examined everyone, including when the agent count
        /// lands exactly on the cap with no one left unexamined. Out-of-range
        /// inputs clamp to zero, never a negative cut.
        /// </summary>
        public static int CutCount(int peopleCount, int examinedCount)
        {
            if (peopleCount <= 0) return 0;
            if (examinedCount < 0) examinedCount = 0;
            if (examinedCount >= peopleCount) return 0;
            return peopleCount - examinedCount;
        }

        /// <summary>
        /// Indices into the people list of the cut residents, in walk order.
        /// Empty when nothing was cut.
        /// </summary>
        public static int[] CutIndices(int peopleCount, int examinedCount)
        {
            int cut = CutCount(peopleCount, examinedCount);
            if (cut == 0) return Array.Empty<int>();
            if (examinedCount < 0) examinedCount = 0;
            int[] indices = new int[cut];
            for (int i = 0; i < cut; i++) indices[i] = examinedCount + i;
            return indices;
        }
    }
}
