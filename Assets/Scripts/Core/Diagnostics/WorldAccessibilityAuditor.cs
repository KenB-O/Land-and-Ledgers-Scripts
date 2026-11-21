// Land & Ledgers — World Accessibility and Interaction Auditor core (Tool 06, Lane 7).
//
// The three destination regimes from Agent M §2 are the framing:
//   A — physical arrival at the item (domestic tasks, sleep, dining);
//   B — arrival at the building only, and only when an on-site Work
//       reservation overlaps and the task has no CustomerRef (two holes:
//       no reservation → no check; CustomerRef → no check);
//   C — timers with no destination (service, production, freight-handling,
//       construction, farming, service-business tasks).
//
// DATA SOURCE: the Lane 9c startup validation (B-7 / M-R13) —
// DevelopmentTownFurnishingInitializer.ValidateHomeAccess (per home:
// Stove, Cleaning, Firewood, Dining) and the business validation in
// DevelopmentBusinessInteriorInitializer (per business: Counter,
// Receiving), both built on StartupAccessValidator.ActivityAccessCheck
// and AppendAccessGaps, whose gaps are logged as "[StartupAccess] "
// warnings. This tool SURFACES that validation — it does not re-implement
// it: the window re-runs the same public resolver
// (BuildingFootprintAuthority.TryFindReachableActivityLocation) and maps
// the results into the snapshot records below; the core only classifies
// gaps and regimes. No coordinates are fabricated; the porch point is
// never aliased as Firewood (historical B-03).
//
// Every gap is classified with the Lane 1 refusal taxonomy
// (RefusalCauseClass): a missing point is DestinationMissing (class 1),
// an authored-but-unreachable point is DestinationInaccessible
// (class 2). Skill is never a cause here — no refusal path reads
// capability (M-05).
//
// Evidence taxonomy on every finding in this file: source-reviewed only.
// Nothing here is Unity-compiled or live-verified; the Roslyn harness is
// the only verification this pass can perform (see HARNESS_NOTES.md).
// No run exists on this HEAD.

using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>The three destination regimes (Agent M §2).</summary>
    public enum AccessRegime
    {
        /// <summary>No regime assigned — the task family is not in the Agent M coverage table.</summary>
        Unknown = 0,
        /// <summary>Regime A — must arrive at the authored activity point before work accrues.</summary>
        PhysicalArrivalAtItem = 1,
        /// <summary>Regime B — must be AtWork at the workplace building, only when an on-site Work reservation overlaps and the task has no CustomerRef.</summary>
        BuildingArrivalOnly = 2,
        /// <summary>Regime C — no destination; minutes accrue wherever the worker is.</summary>
        TimerNoDestination = 3,
    }

    /// <summary>What is wrong with one required activity point, if anything.</summary>
    public enum AccessGapKind
    {
        /// <summary>The point is authored and reachable from main-entrance (or presence suffices).</summary>
        None = 0,
        /// <summary>Class 1 — no point of this kind is authored on the building.</summary>
        Missing = 1,
        /// <summary>Class 2 — the point is authored but no interior route reaches it from main-entrance.</summary>
        Unreachable = 2,
    }

    /// <summary>
    /// One required activity point on one building, as observed by the
    /// startup validation. Field-for-field the same semantics as
    /// StartupAccessValidator.ActivityAccessCheck (Found /
    /// ReachableFromMainEntrance / RequiresInteriorRoute); the core must
    /// not depend on that type because it is outside the harness closure.
    /// </summary>
    public sealed class AccessPointCheck
    {
        public string BuildingLabel = string.Empty;
        public int BuildingId = -1;
        public int HouseholdId = -1;
        public string BusinessKey = string.Empty;
        public string PointKind = string.Empty;
        public bool Found;
        public bool ReachableFromMainEntrance;
        public bool RequiresInteriorRoute = true;
        public string LocationId = string.Empty;
        public string Reason = string.Empty;
    }

    /// <summary>One access gap with its refusal class, authority and evidence.</summary>
    public sealed class AccessGap
    {
        public AccessGap(string id, string buildingLabel, string pointKind, AccessGapKind kind,
            RefusalCauseClass refusalClass, string detail, string authority, string evidence)
        {
            Id = id;
            BuildingLabel = buildingLabel ?? string.Empty;
            PointKind = pointKind ?? string.Empty;
            Kind = kind;
            RefusalClass = refusalClass;
            Detail = detail ?? string.Empty;
            Authority = authority ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        public string Id { get; }
        public string BuildingLabel { get; }
        public string PointKind { get; }
        public AccessGapKind Kind { get; }
        public RefusalCauseClass RefusalClass { get; }
        public string Detail { get; }
        public string Authority { get; }
        public string Evidence { get; }
    }

    /// <summary>One task family's destination regime (Agent M §3 coverage table).</summary>
    public sealed class TaskFamilyRegime
    {
        public TaskFamilyRegime(string taskFamily, AccessRegime regime, string destinationExpectation, string evidence)
        {
            TaskFamily = taskFamily ?? string.Empty;
            Regime = regime;
            DestinationExpectation = destinationExpectation ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        public string TaskFamily { get; }
        public AccessRegime Regime { get; }
        public string DestinationExpectation { get; }
        public string Evidence { get; }
    }

    /// <summary>One config-grounded inventory fact (Agent B / M-13 re-verified counts).</summary>
    public sealed class ConfigInventoryFact
    {
        public ConfigInventoryFact(string pointKind, int authoredCount, string note)
        {
            PointKind = pointKind ?? string.Empty;
            AuthoredCount = authoredCount;
            Note = note ?? string.Empty;
        }

        public string PointKind { get; }
        public int AuthoredCount { get; }
        public string Note { get; }
    }

    public sealed class WorldAccessibilityReport
    {
        public WorldAccessibilityReport(List<AccessPointCheck> checks, List<AccessGap> gaps)
        {
            Checks = checks;
            Gaps = gaps;
        }

        public List<AccessPointCheck> Checks { get; }
        public List<AccessGap> Gaps { get; }

        public int MissingCount
        {
            get
            {
                int count = 0;
                foreach (AccessGap gap in Gaps)
                {
                    if (gap != null && gap.Kind == AccessGapKind.Missing) count++;
                }
                return count;
            }
        }

        public int UnreachableCount
        {
            get
            {
                int count = 0;
                foreach (AccessGap gap in Gaps)
                {
                    if (gap != null && gap.Kind == AccessGapKind.Unreachable) count++;
                }
                return count;
            }
        }
    }

    /// <summary>
    /// The auditor. All methods are pure: no statics read, no routes
    /// changed, no points authored. Diagnostics OBSERVE the startup
    /// validation's results; this class never becomes a parallel source
    /// of truth about what is reachable.
    /// </summary>
    public static class WorldAccessibilityAuditor
    {
        /// <summary>Point kinds the home validation checks (Lane 9c, B-7).</summary>
        public static readonly string[] HomePointKinds = { "Stove", "Cleaning", "Firewood", "Dining" };

        /// <summary>Point kinds the business validation checks (M-R13).</summary>
        public static readonly string[] BusinessPointKinds = { "Counter", "Receiving" };

        /// <summary>
        /// The two holes in Regime B's building-arrival gate
        /// (Agent M §2): a business task with no overlapping on-site Work
        /// reservation accrues with no arrival check at all, and any task
        /// carrying a CustomerRef skips the gate entirely.
        /// </summary>
        public static readonly string[] BuildingGateHoles =
        {
            "No overlapping on-site Work reservation → no arrival check (HasArrivedAtBusinessForReservedWork returns true).",
            "Task carries a CustomerRef → no arrival check (TendCustomer always carries shop:{household}:{need}).",
        };

        /// <summary>
        /// Config inventory (Agent B / M-13, re-verified): Bed 33, Dining
        /// 19, Stove 12, Workshop 10, Oven 1, Counter 1
        /// (general-store-counter), Receiving 1
        /// (lumberyard-receiving-approach), Firewood 1
        /// (shared House Natural Wood prefab, instantiated by the eight
        /// development-town households); Cleaning 0,
        /// Porch 0, Storage 0, Delivery 0, Loading 0, Water 0,
        /// HitchCandidate 0. Firewood is one point on the shared home
        /// prefab; physical fuel-task arrival remains unverified.
        /// </summary>
        public static IReadOnlyList<ConfigInventoryFact> KnownConfigInventory()
        {
            return new List<ConfigInventoryFact>
            {
                new ConfigInventoryFact("Bed", 33, "Sleep routes via per-bed ids (B-15/B-16, M-05 — works)."),
                new ConfigInventoryFact("Dining", 19, "Dining routes via per-seat ids (B-15, M-05 — works)."),
                new ConfigInventoryFact("Stove", 12, "Stove-less households are Shane Bryan (runtime id 5) and Millward (id 8) only (M-13 correction of B's Sithole claim)."),
                new ConfigInventoryFact("Workshop", 10, "Workstation gates exist; the gate anchors the equipment, not the person (M-11)."),
                new ConfigInventoryFact("Oven", 1, "Bakery oven approach point exists in config with zero C# references (M-10)."),
                new ConfigInventoryFact("Counter", 1, "Only general-store-counter; the bakery and fuel dealer have no counter point (M-07/M-08)."),
                new ConfigInventoryFact("Receiving", 1, "Only lumberyard-receiving-approach; the \"receiving\" exact-id lookup fails on every building (M-15)."),
                new ConfigInventoryFact("Firewood", 1, "One distinct exterior firewood-handling point is authored on the shared House Natural Wood prefab used by all eight development-town homes; this is a route destination, not a stock grant."),
                new ConfigInventoryFact("Cleaning", 0, "Chores route to literal \"cleaning\"; config authors zero Cleaning locations (B-02)."),
                new ConfigInventoryFact("Porch", 0, "Chores route to literal \"porch-dropoff\" on alternate days; config authors zero Porch locations (B-02)."),
                new ConfigInventoryFact("Storage", 0, "No Storage-type locations authored (B-20)."),
                new ConfigInventoryFact("Delivery", 0, "No Delivery-type locations authored (B-20)."),
                new ConfigInventoryFact("Loading", 0, "No Loading-type locations authored (B-20)."),
                new ConfigInventoryFact("Water", 0, "No Water-type locations authored (B-20)."),
                new ConfigInventoryFact("HitchCandidate", 0, "No HitchCandidate-type locations authored (B-20)."),
            };
        }

        /// <summary>Gap kind for one check, with the same semantics as StartupAccessValidator.AppendAccessGaps.</summary>
        public static AccessGapKind ClassifyGap(AccessPointCheck check)
        {
            if (check == null)
                return AccessGapKind.Missing;
            if (!check.Found)
                return AccessGapKind.Missing;
            if (check.RequiresInteriorRoute && !check.ReachableFromMainEntrance)
                return AccessGapKind.Unreachable;
            return AccessGapKind.None;
        }

        /// <summary>Refusal class for one check (Lane 1 taxonomy; M-05: never skill).</summary>
        public static RefusalCauseClass RefusalClassFor(AccessPointCheck check)
        {
            switch (ClassifyGap(check))
            {
                case AccessGapKind.Missing: return RefusalCauseClass.DestinationMissing;
                case AccessGapKind.Unreachable: return RefusalCauseClass.DestinationInaccessible;
                default: return RefusalCauseClass.Unclassified;
            }
        }

        /// <summary>
        /// The "[StartupAccess] " gap line for one check, in the same
        /// wording StartupAccessValidator.AppendAccessGaps writes, so a
        /// gap surfaced here matches the startup log line for the same
        /// building.
        /// </summary>
        public static string GapLine(string buildingLabel, AccessPointCheck check)
        {
            if (check == null)
                return (buildingLabel ?? string.Empty) + " has no activity point authored.";
            string typeLabel = string.IsNullOrWhiteSpace(check.PointKind) ? "activity" : check.PointKind;
            if (!check.Found)
            {
                return (buildingLabel ?? string.Empty) + " has no " + typeLabel + " activity point authored"
                    + (string.IsNullOrWhiteSpace(check.Reason) ? "." : ": " + check.Reason);
            }
            if (check.RequiresInteriorRoute && !check.ReachableFromMainEntrance)
            {
                return (buildingLabel ?? string.Empty) + " has a " + typeLabel + " activity point ('"
                    + check.LocationId + "') that cannot be reached from main-entrance"
                    + (string.IsNullOrWhiteSpace(check.Reason) ? "." : ": " + check.Reason);
            }
            return string.Empty;
        }

        /// <summary>Full report over observed checks: one gap per failing check, never a gap without a class.</summary>
        public static WorldAccessibilityReport BuildReport(IEnumerable<AccessPointCheck> checks)
        {
            var checkList = checks != null ? new List<AccessPointCheck>(checks) : new List<AccessPointCheck>();
            var gaps = new List<AccessGap>();
            foreach (AccessPointCheck check in checkList)
            {
                if (check == null)
                    continue;
                AccessGapKind kind = ClassifyGap(check);
                if (kind == AccessGapKind.None)
                    continue;
                string id = (kind == AccessGapKind.Missing ? "B-MISSING-" : "B-UNREACHABLE-")
                    + (check.PointKind ?? "activity").ToUpperInvariant() + "-" + check.BuildingId;
                string detail = GapLine(check.BuildingLabel, check);
                string authority = "StartupAccessValidator.AppendAccessGaps / "
                    + "BuildingFootprintAuthority.TryFindReachableActivityLocation (Lane 9c startup validation)";
                string evidence = "Found=" + check.Found
                    + "; ReachableFromMainEntrance=" + check.ReachableFromMainEntrance
                    + "; RequiresInteriorRoute=" + check.RequiresInteriorRoute
                    + "; LocationId='" + check.LocationId + "'"
                    + (string.IsNullOrWhiteSpace(check.Reason) ? "" : "; Reason='" + check.Reason + "'");
                gaps.Add(new AccessGap(id, check.BuildingLabel, check.PointKind, kind,
                    RefusalClassFor(check), detail, authority, evidence));
            }
            return new WorldAccessibilityReport(checkList, gaps);
        }

        /// <summary>
        /// Regime for one task family, from the Agent M §3 coverage
        /// table. Unknown families return Unknown — the auditor never
        /// guesses a regime for a family the audit did not cover.
        /// </summary>
        public static TaskFamilyRegime ClassifyTaskFamily(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                return new TaskFamilyRegime(string.Empty, AccessRegime.Unknown,
                    "No task id supplied.", "Agent M §3 coverage table.");
            string id = taskId.Trim();

            // Regime A — physical arrival at the item.
            if (id.StartsWith("domestic.meal-prep", StringComparison.Ordinal)
                || id.StartsWith("domestic.chores", StringComparison.Ordinal)
                || id.StartsWith("domestic.fuel-handling", StringComparison.Ordinal)
                || id.StartsWith("domestic.animal-care", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.PhysicalArrivalAtItem,
                    "Must arrive at the authored activity point before work accrues. Meal preparation resolves an installed per-stove id; chores resolve an authored Cleaning point (then Porch); fuel handling uses the exterior firewood-handling point when authored. Live routes still require Main Scene verification. Animal-care has no resolver branch (M-04).",
                    "Current source: SimulationDrivers.ResolveDomesticActivityMovement; House Natural Wood prefab; DomesticLaborDriver RequiresPhysicalArrival; PopulationPathingDirector.TryReachDomesticActivity.");
            }
            if (id.StartsWith("sleep", StringComparison.Ordinal)
                || id.IndexOf("sleep", StringComparison.OrdinalIgnoreCase) >= 0 && id.StartsWith("domestic", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.PhysicalArrivalAtItem,
                    "Sleep routes via the per-bed id (33 beds authored); floor/bedroll counts as arrived at the building. Works (M-05).",
                    "Agent M §3.1 (M-05); SimulationDrivers.ResolveSleepMovement.");
            }
            if (id.StartsWith("dining", StringComparison.Ordinal)
                || id.IndexOf("meal", StringComparison.OrdinalIgnoreCase) >= 0 && id.StartsWith("domestic", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.PhysicalArrivalAtItem,
                    "Dining routes via the per-seat id (19 dining chairs authored). Works (M-05); struck hardest by origin-invalid (M-06).",
                    "Agent M §3.1 (M-05); SimulationDrivers.ResolveMealMovement.");
            }

            // Regime B — arrival at the building only.
            if (id.StartsWith("travel", StringComparison.Ordinal)
                || id.IndexOf("commute", StringComparison.OrdinalIgnoreCase) >= 0
                || id.StartsWith("fr.dispatch", StringComparison.Ordinal)
                || id.StartsWith("fr.receive", StringComparison.Ordinal)
                || id.StartsWith("freight-receiving", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.BuildingArrivalOnly,
                    "Completes on building arrival (ResolveArrivalDrivenTravelTasks). Freight receiving "
                    + "does an exact-id \"receiving\" lookup that fails on every building; the authored "
                    + "front-door fallback is the universal path, not the exception (M-15).",
                    "Agent M §2/§3.4/§3.5 (M-15, M-18, M-19); SimulationDrivers.ResolveArrivalDrivenTravelTasks.");
            }
            if (id.StartsWith("gs.", StringComparison.Ordinal)
                && (id.IndexOf("stock-shelves", StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf("clean-store", StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf("order-from-supplier", StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf("bookkeeping", StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf("handle-payment", StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf("move-shelf", StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf("move-back", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return new TaskFamilyRegime(id, AccessRegime.BuildingArrivalOnly,
                    "Back-of-shop work through AdvanceAutonomousWork: building-level AtWork gate at best, "
                    + "and only when a Work reservation overlaps and no CustomerRef is set. None names a "
                    + "shelf, back room, desk or counter (M-09).",
                    "Agent M §3.2 (M-09); SimulationDrivers.HasArrivedAtBusinessForReservedWork.");
            }

            // Regime C — no destination, minutes only.
            if (id.StartsWith("gs.tend-customer", StringComparison.Ordinal)
                || id.StartsWith("tend-customer", StringComparison.Ordinal)
                || id.IndexOf("tendcustomer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "No destination: completion is a due-event timer (TaskCompletionDue at +planned minutes). "
                    + "The clerk's position is never checked and the CustomerRef skips the Regime B gate too (M-06).",
                    "Agent M §3.2 (M-06); HouseholdShoppingLoop.TryStartCustomerService / CompleteServiceTask; ShoppingTripDriver.");
            }
            if (id.StartsWith("bakery.", StringComparison.Ordinal)
                || id.IndexOf("bake", StringComparison.OrdinalIgnoreCase) >= 0 && id.StartsWith("bakery", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "Bakery stages are abstract minutes against reservation only (oven 4 batches, proofing rack "
                    + "4 slots — class 3 working as designed); the approach points exist in config with zero C# references (M-10).",
                    "Agent M §3.3 (M-10); IUnifiedReservationAuthority.");
            }
            if (id.StartsWith("saw", StringComparison.Ordinal)
                || id.StartsWith("f1-carpentry", StringComparison.Ordinal)
                || id.IndexOf("carpentry-phase", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "Workstation gate only (EquipmentTaskGate: equipment availability, condition, reservations). "
                    + "It never checks where the worker is — a sawyer at home and at the saw line are indistinguishable (M-11).",
                    "Agent M §3.3 (M-11); EquipmentTaskGate; SawmillShopRuntime.");
            }
            if (id.StartsWith("fr.load-freight", StringComparison.Ordinal)
                || id.StartsWith("fr.unload-freight", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "Loading/unloading record work progress with no arrival or location check at any call site; "
                    + "five drivers call WorkLoading with a fixed 60 minutes (M-14). The transport leg is physical; the handling is abstract.",
                    "Agent M §3.4 (M-14); FreightShipmentService.WorkLoading / WorkUnloading.");
            }
            if (id.StartsWith("construction-labor", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "Construction labor credits minutes directly via RecordWorkProgress — no site arrival, no travel "
                    + "to the site, no building check (M-16). A builder can complete a phase from anywhere in town.",
                    "Agent M §3.4 (M-16); ConstructionProjectExecutor.CommitLabor / WorkCommittedLabor.");
            }
            if (id.StartsWith("furnishing.install", StringComparison.Ordinal)
                || id.StartsWith("furnishing.move", StringComparison.Ordinal)
                || id.StartsWith("furnishing.remove", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "NO TASK EXISTS in production (M-17, answered by Lane 9f's FurnishingInstallTaskDriver: paid/delivered "
                    + "furniture now installs only through a real furnishing.install task worked by an arrived household member).",
                    "Agent M §3.4 (M-17); Lane 9f FurnishingInstallTaskDriver.");
            }
            if (id.StartsWith("doc.", StringComparison.Ordinal)
                || id.StartsWith("barb.", StringComparison.Ordinal)
                || id.StartsWith("tail.", StringComparison.Ordinal)
                || id.StartsWith("rest.", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "Service-business catalogs are minute-based TaskDefinitions with no location/counter/activity "
                    + "reference in any catalog file (M-12). A doctor's house call routes nobody anywhere.",
                    "Agent M §3.3 (M-12).");
            }
            if (id.StartsWith("plow-field", StringComparison.Ordinal)
                || id.StartsWith("plant-field", StringComparison.Ordinal)
                || id.StartsWith("tend-field", StringComparison.Ordinal)
                || id.StartsWith("harvest-field", StringComparison.Ordinal)
                || id.IndexOf("field", StringComparison.OrdinalIgnoreCase) >= 0 && id.StartsWith("farm", StringComparison.Ordinal))
            {
                return new TaskFamilyRegime(id, AccessRegime.TimerNoDestination,
                    "Farming: field id is data, not a route target. Work is a real WorkTask scaled by acres with the "
                    + "plow reserved exclusively; no code routes the plowman to the field (M-13).",
                    "Agent M §3.3 (M-13); PlowWorkService; CropChain.");
            }

            return new TaskFamilyRegime(id, AccessRegime.Unknown,
                "This task family is not in the Agent M §3 coverage table. The auditor does not guess its regime.",
                "Agent M §3 coverage table.");
        }

        /// <summary>Regime label for display.</summary>
        public static string RegimeLabel(AccessRegime regime)
        {
            switch (regime)
            {
                case AccessRegime.PhysicalArrivalAtItem: return "A — physical arrival at the item";
                case AccessRegime.BuildingArrivalOnly: return "B — arrival at the building only";
                case AccessRegime.TimerNoDestination: return "C — timers, no destination";
                default: return "Unknown regime";
            }
        }
    }
}
