// Land & Ledgers — Supply Chain Dependency Explorer core (Tool 05, Lane 7).
//
// Follow one good from source to shelf through the dependency graph —
// flour, fuel, timber, bread — naming each authority and each break.
// The trace walks the chain in order and stops at the FIRST supported
// blocker: the first node whose observed state is missing, empty or
// refused, with the authority that owns it and the evidence for it.
//
// Chains (source-grounded):
//   Flour — Millbrook Grain Co. (off-map aid, weekly auto-refill to a
//     target reserve) → store procurement (StoreProcurementChain) →
//     bakery flour bin (BakeryShopRuntime.FlourStock) → bread. The
//     bakery→store leg carries the F1 pool/lot freeze: legacy pool-only
//     sales freeze bread/flour lots (latent in dev, live only in a
//     player-store session).
//   Fuel — sawmill byproduct (TransferSawmillByproductsToFuelDealer in
//     OnDayChanged) → FuelDealerByproductSettlement.Settle → dealer stock
//     (FuelDealerRuntime) → bakery paid fuel source
//     (TryBuildPaidBakeryFuelSource; the code reads the fuel-dealer
//     block's intake units per bakery unit — 4 — not the bakery block's
//     0, E-14 corrected) and household heating fuel
//     (ResolveDailyUse burns it). Breaks: W SF-7 (byproduct settlement
//     failed after custody moved — CRITICAL until a compensating path is
//     proven) and D-18 (no seller-knowledge seeding: households cannot
//     discover the dealer).
//   Timber — felling (LoggingCampSupplyService) → log haul → sawmill
//     saw-logs (SawmillShopRuntime workstation gate) →
//     SawmillToYardTransferDriver (lot-based) → lumber yard →
//     CarpentryLumberProcurement → carpentry production. Break: E-13 /
//     F7-2 — the support node writes category stock the lot-based yard
//     transfer never reads; the dev-town linkage is still open.
//   Bread — flour bin + fuel + notions → staged bakery batches
//     (DriveStagedBakeryDay; Lane 9a replaced the wholesale veto with
//     due-today netting) → wholesale (voluntary only since Lane 9a
//     removed the 24-loaf obligation and its demand-derived fallback)
//     / retail counter (8c fresh loaf via Series2BakerySupplierAdapter)
//     → household meals. Breaks: D-04 (a player-owned bakery can never
//     bake — no worker binding; owner decision owed) and the day-41 fuel
//     death (40u opening fuel, never replenished before Lane 9e/9b work).
//
// A clean trace means "no supported blocker found", never "the chain
// flows": a node can hold stock while the next leg never moves. The
// explorer names the first blocker it can support, and says plainly
// when it cannot support one.
//
// Evidence taxonomy on every finding in this file: source-reviewed only.
// Nothing here is Unity-compiled or live-verified; the Roslyn harness is
// the only verification this pass can perform (see HARNESS_NOTES.md).
// No run exists on this HEAD.

using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>Observed state of one chain node, as supplied by the caller.</summary>
    public enum SupplyNodeState
    {
        /// <summary>Nothing was observed for this node — the trace cannot judge it.</summary>
        Unknown = 0,
        /// <summary>The node is present and holds stock / is operating.</summary>
        Present = 1,
        /// <summary>The node holds no stock (empty bin, empty store).</summary>
        Empty = 2,
        /// <summary>The node is absent entirely (no registration, no runtime, no binding).</summary>
        Absent = 3,
        /// <summary>The node refused the leg (a named refusal, e.g. settlement failed after custody moved).</summary>
        Refused = 4,
    }

    /// <summary>One stage of one chain, with its owning authority.</summary>
    public sealed class SupplyChainNode
    {
        public SupplyChainNode(string id, string goodId, string stageLabel, string authority,
            string evidence, string knownBreak)
        {
            Id = id ?? string.Empty;
            GoodId = goodId ?? string.Empty;
            StageLabel = stageLabel ?? string.Empty;
            Authority = authority ?? string.Empty;
            Evidence = evidence ?? string.Empty;
            KnownBreak = knownBreak ?? string.Empty;
        }

        public string Id { get; }
        public string GoodId { get; }
        public string StageLabel { get; }
        public string Authority { get; }
        public string Evidence { get; }
        public string KnownBreak { get; }
    }

    /// <summary>One observed node state, supplied by the window or a test.</summary>
    public sealed class SupplyNodeObservation
    {
        public string NodeId = string.Empty;
        public SupplyNodeState State = SupplyNodeState.Unknown;
        public int StockUnits = -1;
        public string Note = string.Empty;
    }

    /// <summary>The first supported blocker on a chain, or the clean-trace verdict.</summary>
    public sealed class SupplyChainTrace
    {
        public SupplyChainTrace(string goodId, SupplyChainNode blockerNode, SupplyNodeObservation blockerObservation,
            string cause, string authority, string evidence, IncidentSeverity severity, bool clean)
        {
            GoodId = goodId ?? string.Empty;
            BlockerNode = blockerNode;
            BlockerObservation = blockerObservation;
            Cause = cause ?? string.Empty;
            Authority = authority ?? string.Empty;
            Evidence = evidence ?? string.Empty;
            Severity = severity;
            Clean = clean;
        }

        public string GoodId { get; }
        public SupplyChainNode BlockerNode { get; }
        public SupplyNodeObservation BlockerObservation { get; }
        public string Cause { get; }
        public string Authority { get; }
        public string Evidence { get; }
        public IncidentSeverity Severity { get; }
        /// <summary>True when no supported blocker was found — "no supported blocker", never "chain proven flowing".</summary>
        public bool Clean { get; }
    }

    /// <summary>
    /// The explorer. All methods are pure over caller-supplied
    /// observations: no statics read, no goods moved, no stock written.
    /// Diagnostics OBSERVE the supply authorities; this class never
    /// becomes a parallel source of truth about what any business holds.
    /// </summary>
    public static class SupplyChainDependencyExplorer
    {
        /// <summary>The four goods this tool traces (plan: flour, fuel, timber, bread).</summary>
        public static readonly string[] Goods = { "flour", "fuel", "timber", "bread" };

        /// <summary>Display label for a good id.</summary>
        public static string GoodLabel(string goodId)
        {
            switch ((goodId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "flour": return "Flour";
                case "fuel": return "Fuel (fuel_wood / heating fuel)";
                case "timber": return "Timber / lumber";
                case "bread": return "Bread (bake.bread-loaf)";
                default: return "Unknown good '" + goodId + "'";
            }
        }

        /// <summary>
        /// The four known chains, in source-to-shelf order. Each node
        /// names its authority and the break the evidence already
        /// supports on that leg (empty when none is known).
        /// </summary>
        public static IReadOnlyList<SupplyChainNode> KnownChain(string goodId)
        {
            switch ((goodId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "flour":
                    return new List<SupplyChainNode>
                    {
                        new SupplyChainNode("flour-millbrook", "flour", "Millbrook Grain Co. (off-map aid)",
                            "SimulationDrivers.DriveMillbrookAidTable / Millbrook aid table",
                            "Off-map aid: Millbrook Grain Co. only; weekly auto-refill to a target reserve.",
                            string.Empty),
                        new SupplyChainNode("flour-store-procurement", "flour", "Store procurement",
                            "StoreProcurementChain",
                            "Bakery flour is procured through the store (Millbrook/Redfield via the store).",
                            string.Empty),
                        new SupplyChainNode("flour-bakery-bin", "flour", "Bakery flour bin",
                            "BakeryShopRuntime.FlourStock",
                            "Flour is 48c/sack; 1 sack = 18 bakery units.",
                            string.Empty),
                        new SupplyChainNode("flour-to-bread", "flour", "Bakery bin → bread (via staged batches)",
                            "SimulationDrivers.DriveStagedBakeryDay / Series2BakerySupplierAdapter",
                            "The adapter maps bake.bread-loaf to the household's bread and requires a building: location id.",
                            "F1 pool/lot freeze: legacy pool-only sales freeze bread/flour lots (latent in dev, live only in a player-store session)."),
                    };
                case "fuel":
                    return new List<SupplyChainNode>
                    {
                        new SupplyChainNode("fuel-sawmill-byproduct", "fuel", "Sawmill byproduct",
                            "SimulationDrivers.TransferSawmillByproductsToFuelDealer (OnDayChanged)",
                            "Byproduct transfer runs in the independent daily hub (L-07).",
                            "W SF-7: byproduct settlement failed after custody moved (SimulationDrivers.cs:916) — CRITICAL until a compensating path is proven."),
                        new SupplyChainNode("fuel-dealer-settlement", "fuel", "Dealer byproduct settlement",
                            "FuelDealerByproductSettlement.Settle",
                            "Byproduct transfer IS paid (file unchanged since 7180a2f9).",
                            string.Empty),
                        new SupplyChainNode("fuel-dealer-stock", "fuel", "Fuel dealer stock",
                            "FuelDealerRuntime",
                            "Fuel 22c/bundle for Jacob; 1 household bundle = 10 intake units; 1 bakery fuel unit = 4 intake units.",
                            "D-18: no seller-knowledge seeding — households cannot discover the registered dealer as a fuel_wood seller (Lane 9e repair)."),
                        new SupplyChainNode("fuel-bakery-source", "fuel", "Bakery paid fuel source",
                            "SimulationDrivers.TryBuildPaidBakeryFuelSource / BakeryShopRuntime.FuelStock",
                            "The code reads the fuel-dealer block's intake units per bakery unit (4), not the bakery block's 0 (E-14 corrected).",
                            "Bakery dies day 41 of every 60-day run on its 40u opening fuel when the paid source does not supply it."),
                        new SupplyChainNode("fuel-household-heating", "fuel", "Household heating burn",
                            "HouseholdReserveEvaluator.ResolveDailyUse via SimulationDrivers.DriveHouseholdReserveDay (Lane 9b F-L1)",
                            "Heating burn runs daily in the dev town since Lane 9b; before that it ran ZERO times (L-01).",
                            string.Empty),
                    };
                case "timber":
                    return new List<SupplyChainNode>
                    {
                        new SupplyChainNode("timber-felling", "timber", "Felling (logging camp)",
                            "LoggingCampSupplyService / SimulationDrivers felling drive",
                            "Felling gate location still unknown (G4); timber regrowth is species- and site-dependent, eastern white pine ~80 years to merchantable sawlog maturity (provisional baseline).",
                            string.Empty),
                        new SupplyChainNode("timber-haul", "timber", "Log haul to sawmill",
                            "SimulationDrivers log-haul drive",
                            "Log hauls run in the independent daily hub (L-07).",
                            string.Empty),
                        new SupplyChainNode("timber-sawmill", "timber", "Sawmill (saw-logs)",
                            "SawmillShopRuntime (Canon 4.1 saw-line workstation gate) / EquipmentTaskGate",
                            "Sawing is refused without a workstation authority; the gate anchors the equipment, not the person (M-11).",
                            string.Empty),
                        new SupplyChainNode("timber-yard-transfer", "timber", "Sawmill → lumber yard transfer",
                            "SawmillToYardTransferDriver (lot-based)",
                            "Lot-based yard transfer; wholesale lumber is 32c and the driver is a real purchase with custody movement (E-05).",
                            "E-13 / F7-2: the support node writes category stock the lot-based yard transfer never reads; the dev-town linkage is still open."),
                        new SupplyChainNode("timber-carpentry-procurement", "timber", "Yard → carpentry procurement",
                            "CarpentryLumberProcurement",
                            "Carpentry lumber procurement reads yard stock.",
                            string.Empty),
                        new SupplyChainNode("timber-carpentry", "timber", "Carpentry production",
                            "CarpentryProduction (one phase/day/process)",
                            "Carpentry is one phase per day per process; f1-carpentry-phase has a workstation gate, no location (M-11).",
                            string.Empty),
                    };
                case "bread":
                    return new List<SupplyChainNode>
                    {
                        new SupplyChainNode("bread-flour", "bread", "Flour bin",
                            "BakeryShopRuntime.FlourStock",
                            "Flour 48c/sack; 1 sack = 18 bakery units.",
                            string.Empty),
                        new SupplyChainNode("bread-fuel", "bread", "Fuel bin",
                            "BakeryShopRuntime.FuelStock",
                            "Bakery fuel ~4 kg dry-wood per firing (12 lb flour batch).",
                            "Bakery dies day 41 of every 60-day run on its 40u opening fuel when the paid source does not supply it."),
                        new SupplyChainNode("bread-notions", "bread", "Notions bin",
                            "BakeryShopRuntime (notions dispense)",
                            "Notions: 10c starting quotation provisional; predictive reorder (demand over lead time + safety reserve); the 60-batch lifetime limit is removed.",
                            "Notions are BROKEN in the retained evidence (hard stop after 60 batches) — owner decision open; do not invent a rule."),
                        new SupplyChainNode("bread-staged-batches", "bread", "Staged bakery batches",
                            "SimulationDrivers.DriveStagedBakeryDay / ShouldBeginStagedBakeryBatch",
                            "Lane 9a: the wholesale veto is gone; due-today wholesale units are netted out of retail stock instead. Glenn makes 1 batch/day (24 loaves) vs ~42/day demand; capacity 4 batches/day, 75% idle.",
                            "D-04: a player-owned bakery can never bake — no worker binding; the fail-closed bake gate refuses every bake (owner decision owed, plan §9g)."),
                        new SupplyChainNode("bread-wholesale", "bread", "Wholesale (voluntary only)",
                            "BakeryShopRuntime.WholesaleAccounts / ScenarioOpeningWholesaleAccounts",
                            "Lane 9a: the 24-loaf daily obligation and its demand-derived fallback are removed. Voluntary wholesale via AddWholesaleAccount stays.",
                            string.Empty),
                        new SupplyChainNode("bread-retail", "bread", "Retail counter (8c fresh loaf)",
                            "Series2BakerySupplierAdapter / BakeryShopRuntime.RetailAllowanceFor",
                            "The bakery counter fresh loaf is 8c (5c is the store's retail price). Lane 9a default policy is ProtectRetailCounter.",
                            "Retail allowance 0 under ProtectWholesaleContracts: a due wholesale account takes every sellable loaf first (D-02)."),
                    };
                default:
                    return new List<SupplyChainNode>();
            }
        }

        /// <summary>
        /// Trace one good through its chain. Walks the nodes in order
        /// and stops at the FIRST node whose observed state is Empty,
        /// Absent or Refused — the first supported blocker, naming its
        /// authority. Unknown nodes are skipped, never treated as
        /// blockers: an unobserved node is not evidence of a break.
        /// A clean trace reports "no supported blocker found".
        /// </summary>
        public static SupplyChainTrace Trace(string goodId, IEnumerable<SupplyNodeObservation> observations)
        {
            IReadOnlyList<SupplyChainNode> chain = KnownChain(goodId);
            if (chain.Count == 0)
            {
                return new SupplyChainTrace(goodId, null, null,
                    "Unknown good '" + goodId + "'. This tool traces flour, fuel, timber and bread only.",
                    "SupplyChainDependencyExplorer.KnownChain",
                    "Good id '" + goodId + "' has no known chain.",
                    IncidentSeverity.Unknown, false);
            }

            var observed = new Dictionary<string, SupplyNodeObservation>(StringComparer.OrdinalIgnoreCase);
            if (observations != null)
            {
                foreach (SupplyNodeObservation observation in observations)
                {
                    if (observation == null || string.IsNullOrWhiteSpace(observation.NodeId))
                        continue;
                    observed[observation.NodeId] = observation;
                }
            }

            foreach (SupplyChainNode node in chain)
            {
                if (!observed.TryGetValue(node.Id, out SupplyNodeObservation observation))
                    continue;
                if (observation.State == SupplyNodeState.Unknown || observation.State == SupplyNodeState.Present)
                    continue;

                string cause;
                IncidentSeverity severity;
                switch (observation.State)
                {
                    case SupplyNodeState.Empty:
                        cause = "The '" + node.StageLabel + "' stage holds no stock, so the chain stops here.";
                        severity = IncidentSeverity.Warning;
                        break;
                    case SupplyNodeState.Absent:
                        cause = "The '" + node.StageLabel + "' stage is absent — no registration, runtime or binding — so the chain stops here.";
                        severity = IncidentSeverity.Warning;
                        break;
                    default: // Refused
                        cause = "The '" + node.StageLabel + "' stage refused the leg: "
                            + (string.IsNullOrWhiteSpace(observation.Note) ? "no refusal text observed." : observation.Note);
                        severity = IncidentSeverity.Critical;
                        break;
                }
                if (!string.IsNullOrWhiteSpace(node.KnownBreak))
                    cause += " Known break on this leg: " + node.KnownBreak;
                string evidence = "Node '" + node.Id + "' observed " + observation.State
                    + (observation.StockUnits >= 0 ? "; stock " + observation.StockUnits + "u" : "; stock unknown")
                    + (string.IsNullOrWhiteSpace(observation.Note) ? "." : "; note: " + observation.Note);
                return new SupplyChainTrace(goodId, node, observation, cause, node.Authority, evidence, severity, false);
            }

            return new SupplyChainTrace(goodId, null, null,
                "No supported blocker found on the " + GoodLabel(goodId) + " chain from the observed nodes. "
                + "This is NOT proof the chain flows — a node can hold stock while the next leg never moves; "
                + "it means no observed node is empty, absent or refused.",
                "SupplyChainDependencyExplorer.Trace",
                "Observed " + observed.Count + " of " + chain.Count + " nodes; none blocked.",
                IncidentSeverity.Information, true);
        }
    }
}
