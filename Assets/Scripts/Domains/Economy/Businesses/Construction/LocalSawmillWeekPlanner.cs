using System;

namespace LandLedgers.Economy
{
    /// <summary>CAMPAIGN-B (DEF-044), package 6: pure, zero-Unity-dependency
    /// planning rule for the local support-node weekly resolve in
    /// AcquisitionMarketManager.ResolveWeeklyConstructionSupportProduction.
    ///
    /// VERDICT — the hardware gate is NOT changed here, and no lumber is
    /// conjured: sawing stays gated on 1 unit/week of blacksmith hardware
    /// maintenance, the regional fallback (438c/unit) still requires a
    /// paying Sawmill business account, and in a town with no blacksmith
    /// and no Sawmill the gate still fails every week. Whether the dev
    /// scenario should form a Sawmill/LumberYard at all, and whether the
    /// blacksmith should be the sawing gate at all, are owner decisions
    /// (Check 8 Q1 and Q10; DEF-004 family) — this package answers neither.
    ///
    /// What WAS a mechanical defect, fixable without any of those
    /// decisions, is the ORDER: the old code cut standing timber FIRST
    /// and only then discovered sawing was blocked, so every week it
    /// converted 10 units of a finite resource (80 standing timber) into
    /// staged logs that have no consumer — staged logs in the local path
    /// are consumed only by ProcessLogsToLumber — while lumber output
    /// stayed zero from week one. The shared path (a real Sawmill
    /// business) does not have this failure mode: a failed maintenance
    /// gate there only halves saw capacity instead of blocking sawing
    /// outright, so a cut still leads to sawing. The local path's
    /// hard-block-plus-cut-first combination is what guaranteed zero
    /// output with resource destruction, every week, forever.
    ///
    /// Plan() computes the week's cut and saw quantities from PRE-CUT
    /// state using the exact arithmetic the resolve method already used
    /// (cut capacity, saw capacity, available labor, lumber storage
    /// room, lumber-per-log). The resolve method now plans first, checks
    /// the hardware gate only when the plan says there is something to
    /// saw, and cuts only when the gate passes — so a blocked week
    /// preserves the standing timber instead of piling up unusable logs.
    /// No capacity, price, gate rule, or block reason changes.</summary>
    public readonly struct LocalSawmillWeekPlan
    {
        public LocalSawmillWeekPlan(int cutLogs, int logsToProcess, int logsByStorage)
        {
            CutLogs = cutLogs;
            LogsToProcess = logsToProcess;
            LogsByStorage = logsByStorage;
        }

        /// <summary>Logs to cut from standing timber this week, once the
        /// hardware gate passes. The resolve method executes this cut only
        /// when LogsToProcess is positive and the gate passes; a blocked
        /// week cuts nothing.</summary>
        public int CutLogs { get; }

        /// <summary>Logs that could be sawn this week once the planned cut
        /// lands (before the hardware gate is checked).</summary>
        public int LogsToProcess { get; }

        /// <summary>Logs the lumber store has room for, from
        /// LumberStorageRoomUnits / LumberPerLog.</summary>
        public int LogsByStorage { get; }
    }

    public static class LocalSawmillWeekPlanner
    {
        /// <summary>Plans one week of local support-node production from
        /// pre-cut state. Mirrors the resolve method's arithmetic exactly:
        /// the cut is min(weekly cut capacity, available labor) clamped to
        /// standing timber, and the sawable count is min(staged stock after
        /// the planned cut, weekly saw capacity, available labor, storage
        /// room in logs). Inputs are clamped to zero the same way
        /// CutStandingTimberToLogs clamps its request, so a negative input
        /// can never plan a negative cut or a negative saw count.</summary>
        public static LocalSawmillWeekPlan Plan(
            int standingTimberUnits,
            int logStockUnits,
            int weeklyCutCapacityLogs,
            int weeklySawCapacityLogs,
            int lumberPerLog,
            int lumberStorageRoomUnits,
            int availableLabor)
        {
            standingTimberUnits = Math.Max(0, standingTimberUnits);
            logStockUnits = Math.Max(0, logStockUnits);
            weeklyCutCapacityLogs = Math.Max(0, weeklyCutCapacityLogs);
            weeklySawCapacityLogs = Math.Max(0, weeklySawCapacityLogs);
            lumberStorageRoomUnits = Math.Max(0, lumberStorageRoomUnits);
            availableLabor = Math.Max(0, availableLabor);
            int effectiveCutCapacity = Math.Min(weeklyCutCapacityLogs, availableLabor);
            int cutLogs = Math.Min(Math.Max(0, effectiveCutCapacity), standingTimberUnits);
            int logsByStorage = lumberPerLog > 0
                ? lumberStorageRoomUnits / lumberPerLog
                : 0;
            int logsToProcess = Math.Min(logStockUnits + cutLogs, weeklySawCapacityLogs);
            logsToProcess = Math.Min(logsToProcess, availableLabor);
            logsToProcess = Math.Min(logsToProcess, logsByStorage);
            return new LocalSawmillWeekPlan(cutLogs, logsToProcess, logsByStorage);
        }
    }
}
