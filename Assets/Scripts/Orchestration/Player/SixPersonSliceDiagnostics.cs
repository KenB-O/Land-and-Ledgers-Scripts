using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// Pass XL vertical slice, Item 4: honest daily diagnostics for the
    /// six-person slice only. Plain C#, no Unity dependency, so the
    /// format itself is testable in the Roslyn harness.
    ///
    /// WHAT THIS IS: a pure formatter. It takes a snapshot of ALREADY
    /// COMPUTED values (ids, cash, stock, meals, tasks, shipments) and
    /// returns one console line. It never invents, projects, or fills in
    /// a value: a caller that does not know a number passes -1 and the
    /// line prints "unknown" for it, rather than a fake zero.
    ///
    /// WHAT THIS IS NOT: a wired logger. SimulationDrivers cannot be
    /// newed in the Roslyn harness (MonoBehaviour), so the call site is
    /// Codex's job, and it is exactly one call (see below). Until Codex
    /// wires it, this formatter logs nothing on its own - do not claim
    /// daily six-person console output exists from this file alone.
    ///
    /// CODEX WIRING (one call, in SimulationDrivers.OnDayChanged, after
    /// the existing daily drivers have run): build a
    /// SixPersonSliceSnapshot from PopulationManager.State (person ids,
    /// household ids), SharedBusinessRuntimeManager (business ids and
    /// business cash), HouseholdLedgerRegistry (household cash),
    /// DailyNeedsService's day report (meals eaten/missed), TaskAuthority
    /// (tasks completed that day), and OffMapNamedSkuProcurementService
    /// orders in Ordered/InTransit stages (shipments in transit); then
    /// Debug.Log(Format(snapshot)) - but ONLY when
    /// populationManager.SuppressMigrantArrivals is true, so ordinary
    /// scenarios get no six-person line. Use the existing
    /// "[SimulationDrivers]" log prefix convention for that call.
    /// </summary>
    public sealed class SixPersonSliceSnapshot
    {
        public int DayIndex;
        public List<int> PersonIds = new List<int>();
        public List<int> HouseholdIds = new List<int>();
        public List<string> BusinessIds = new List<string>();
        public List<string> LotIds = new List<string>();
        public int BusinessCashCents = -1;
        public int HouseholdCashCents = -1;
        public int MealsEaten = -1;
        public int MealsMissed = -1;
        public int TasksCompleted = -1;
        public int ShipmentsInTransit = -1;
    }

    public static class SixPersonSliceDiagnostics
    {
        private static string IdList(List<int> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return "none";
            }

            return string.Join(",", ids);
        }

        private static string IdListStrings(List<string> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return "none";
            }

            return string.Join(",", ids);
        }

        private static string CountOrUnknown(int value)
        {
            return value < 0 ? "unknown" : value.ToString();
        }

        private static string CashOrUnknown(int cents)
        {
            return cents < 0 ? "unknown" : cents + "c";
        }

        public static string Format(SixPersonSliceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "[SixPerson] day unknown: no snapshot (nothing faked).";
            }

            return "[SixPerson] day " + snapshot.DayIndex +
                ": persons [" + IdList(snapshot.PersonIds) + "]" +
                " households [" + IdList(snapshot.HouseholdIds) + "]" +
                " businesses [" + IdListStrings(snapshot.BusinessIds) + "]" +
                " lots [" + IdListStrings(snapshot.LotIds) + "]" +
                " businessCash " + CashOrUnknown(snapshot.BusinessCashCents) +
                " householdCash " + CashOrUnknown(snapshot.HouseholdCashCents) +
                " meals " + CountOrUnknown(snapshot.MealsEaten) + " eaten / " +
                CountOrUnknown(snapshot.MealsMissed) + " missed" +
                " tasks " + CountOrUnknown(snapshot.TasksCompleted) + " completed" +
                " shipments " + CountOrUnknown(snapshot.ShipmentsInTransit) + " in transit";
        }
    }
}
