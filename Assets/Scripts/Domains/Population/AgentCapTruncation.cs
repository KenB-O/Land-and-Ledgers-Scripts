namespace LandLedgers.Population
{
    /// <summary>
    /// Pure eligibility rule for PopulationPathingDirector's visual-agent cap.
    /// A record is cap-truncated only if it is otherwise a local resident with
    /// a usable home entrance and no actor slot remains. Invalid or unanchored
    /// records are accounted for by their own rejection diagnostics.
    /// </summary>
    public static class AgentCapTruncation
    {
        public static bool IsEligibleResidentCut(
            bool isMapResident, bool hasHomeDoorAnchor, int activeAgentCount, int agentCap)
        {
            return isMapResident && hasHomeDoorAnchor && activeAgentCount >= agentCap;
        }
    }
}
