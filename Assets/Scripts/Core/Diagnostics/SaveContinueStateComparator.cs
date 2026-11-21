// Land & Ledgers — Save/Continue State Comparator core (Tool 07, Lane 7).
//
// Compare a save's state against the live Continue state — what was
// lost, duplicated, or changed. Builds on Lane 9f's persistence repairs
// (F R-2: failed phase tasks no longer strand production after Continue
// — RestoreProcess / RestoreProcessesForBusiness restore or refuse
// loudly, never silently lose a batch or its consumed inputs; F R-3:
// RestorePresentations counts and diagnoses skipped bindings instead of
// silently continuing; F R-4: storage re-bind pinned by tests).
//
// The persistence table is Agent F §5 (DTO-verified at HEAD):
// PERSISTED — construction projects / commissioner / contract book /
// billing (SystemsSaveDto); furniture purchase orders, household
// furnishing registers, stove fuel stores, storage bindings
// (Series2Pass9SaveState); store, bakery and fuel-dealer customer
// queues (Pass 9 state); freight shipments (SaveDtos shipments +
// LogisticsShipmentSaveDto, restored incl. CarrierSettlement);
// tasks (TaskAuthoritySaveDto); worker assignments/employments
// (EmploymentRegistrySaveDto); reservations
// (EquipmentReservationAuthoritySaveDto); financial obligations
// (FinancialObligationSaveDto); shopping trips
// (PlannedShoppingTripSaveDto); business runtime state incl. retail
// lots, category stock, worker slots (BusinessRuntimeState).
// Current source has a business DTO path for
// BusinessInstanceState.GenericConfiguration.ActiveProcesses, and Lane
// 9f exercises process/task restoration in focused fixtures. The
// comparator must still CATCH an actual loss or loud refusal; neither
// source presence nor a fixture substitutes for a fresh-scene Main
// Scene Continue verification.
//
// Only meaningful changes are defects. Legitimate restoration
// transformations are defined by the existing authorities — e.g. Lane
// 9a: legacy saves seed unspecified staple_food lots flagged as
// unidentified historical stock that cannot be served until
// identified, never silently converted to an arbitrary product. A
// Continue that loses an in-progress batch or duplicates a settled
// lot is a defect the comparator must catch, not a transformation it
// accepts (F R-2, C-5, Lane 9f).
//
// Evidence taxonomy on every finding in this file: source-reviewed only.
// Nothing here is Unity-compiled or live-verified; the Roslyn harness is
// the only verification this pass can perform (see HARNESS_NOTES.md).
// No run exists on this HEAD — no Continue round-trip has been observed.

using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>Outcome of comparing one persisted field pre-save vs post-restore.</summary>
    public enum StateComparisonOutcome
    {
        /// <summary>Pre-save and post-restore agree.</summary>
        Equal = 0,
        /// <summary>Present pre-save, absent post-restore — a defect unless an authority defines the loss as legitimate.</summary>
        Lost = 1,
        /// <summary>Present once pre-save, present twice (or more) post-restore — a defect (e.g. a duplicated settled lot).</summary>
        Duplicated = 2,
        /// <summary>Present both sides with a different value.</summary>
        Changed = 3,
        /// <summary>Changed by a transformation an existing authority defines as legitimate (named in the detail).</summary>
        LegitimateTransformation = 4,
        /// <summary>Absent pre-save, present post-restore — new state the save did not contain; suspicious, never assumed legitimate.</summary>
        Appeared = 5,
        /// <summary>Lane 9f: the restore refused loudly (RestoreProcess / RestoreProcessesForBusiness) instead of silently losing the state. Flagged, never a silent pass — the state is not restored and the refusal must be resolved.</summary>
        RefusedLoudly = 6,
    }

    /// <summary>One persisted field's value on one side of a Continue.</summary>
    public sealed class PersistedField
    {
        public string System = string.Empty;
        public string Field = string.Empty;
        public string ValueSummary = string.Empty;
        public int Count = -1;
        public bool Present = true;
        /// <summary>True when the restore refused this field loudly (Lane 9f) instead of restoring or losing it silently.</summary>
        public bool RefusedLoudly;
        public string RefusalReason = string.Empty;
    }

    /// <summary>One field comparison, with its verdict and the authority that decides it.</summary>
    public sealed class StateFieldComparison
    {
        public StateFieldComparison(string system, string field, StateComparisonOutcome outcome,
            string detail, string authority, string evidence)
        {
            System = system ?? string.Empty;
            Field = field ?? string.Empty;
            Outcome = outcome;
            Detail = detail ?? string.Empty;
            Authority = authority ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        public string System { get; }
        public string Field { get; }
        public StateComparisonOutcome Outcome { get; }
        public string Detail { get; }
        public string Authority { get; }
        public string Evidence { get; }

        /// <summary>True when this comparison is a defect the owner must see (Lost / Duplicated / Changed / Appeared).</summary>
        public bool IsDefect
        {
            get
            {
                return Outcome == StateComparisonOutcome.Lost
                    || Outcome == StateComparisonOutcome.Duplicated
                    || Outcome == StateComparisonOutcome.Changed
                    || Outcome == StateComparisonOutcome.Appeared;
            }
        }
    }

    /// <summary>One row of the Agent F §5 persistence table.</summary>
    public sealed class PersistenceExpectation
    {
        public PersistenceExpectation(string system, string field, bool persisted, string authority, string note)
        {
            System = system ?? string.Empty;
            Field = field ?? string.Empty;
            Persisted = persisted;
            Authority = authority ?? string.Empty;
            Note = note ?? string.Empty;
        }

        public string System { get; }
        public string Field { get; }
        public bool Persisted { get; }
        public string Authority { get; }
        public string Note { get; }
    }

    public sealed class SaveContinueComparisonReport
    {
        public SaveContinueComparisonReport(List<StateFieldComparison> comparisons)
        {
            Comparisons = comparisons;
        }

        public List<StateFieldComparison> Comparisons { get; }

        public int DefectCount
        {
            get
            {
                int count = 0;
                foreach (StateFieldComparison comparison in Comparisons)
                {
                    if (comparison != null && comparison.IsDefect) count++;
                }
                return count;
            }
        }

        public int RefusedLoudlyCount
        {
            get
            {
                int count = 0;
                foreach (StateFieldComparison comparison in Comparisons)
                {
                    if (comparison != null && comparison.Outcome == StateComparisonOutcome.RefusedLoudly) count++;
                }
                return count;
            }
        }

        public int LegitimateTransformationCount
        {
            get
            {
                int count = 0;
                foreach (StateFieldComparison comparison in Comparisons)
                {
                    if (comparison != null && comparison.Outcome == StateComparisonOutcome.LegitimateTransformation) count++;
                }
                return count;
            }
        }
    }

    /// <summary>
    /// The comparator. All methods are pure over caller-supplied field
    /// records: no save files read, no state restored, no goods moved.
    /// Diagnostics OBSERVE the save and restore authorities; this class
    /// never becomes a parallel source of truth about what a save holds.
    /// </summary>
    public static class SaveContinueStateComparator
    {
        /// <summary>
        /// Legitimate transformations, each defined by an existing
        /// authority (system + field → the authority and its rule).
        /// Anything not listed here is NOT a legitimate transformation.
        /// </summary>
        public static readonly string[][] LegitimateTransformations =
        {
            new[]
            {
                "HouseholdInventory", "UnspecifiedStapleFoodLots",
                "Lane 9a: legacy saves seed unspecified staple_food lots flagged as unidentified historical stock that cannot be served until identified — never silently converted to an arbitrary product.",
            },
            new[]
            {
                "HouseholdFurnishing", "PresentationRebind",
                "Lane 9f F R-3: RestorePresentations rebinds a presentation to its authored activity location after Continue; a binding whose configured item is missing is counted and diagnosed (PresentationsSkipped), never silently dropped.",
            },
        };

        /// <summary>The Agent F §5 persistence table (DTO-verified at HEAD), as expectations the comparator checks.</summary>
        public static IReadOnlyList<PersistenceExpectation> KnownPersistence()
        {
            return new List<PersistenceExpectation>
            {
                new PersistenceExpectation("Construction", "Projects / commissioner / contract book / billing", true,
                    "SystemsSaveDto", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Furniture", "Purchase orders", true,
                    "Series2Pass9SaveState.FurniturePurchaseOrders", "Persisted (DTO-verified); durable Pass 9 register."),
                new PersistenceExpectation("Furniture", "Household furnishing registers", true,
                    "Series2Pass9SaveState.HouseholdFurnishingRegisters", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Household", "Stove fuel stores", true,
                    "Series2Pass9SaveState.HouseholdStoveFuelStores", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Household", "Storage bindings", true,
                    "Series2Pass9SaveState.HouseholdStorageBindings", "Persisted (DTO-verified); re-bind pinned by Lane 9f tests (F R-4)."),
                new PersistenceExpectation("Business", "Customer queues (store, bakery, fuel dealer)", true,
                    "Series2Pass9SaveState (Pass 9 state)", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Freight", "Shipments", true,
                    "SaveDtos.shipments / LogisticsShipmentSaveDto; restored incl. CarrierSettlement.RestoreFromShipments",
                    "Persisted (DTO-verified)."),
                new PersistenceExpectation("Tasks", "TaskAuthority tasks", true,
                    "TaskAuthoritySaveDto", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Labor", "Worker assignments / employments", true,
                    "EmploymentRegistrySaveDto", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Equipment", "Reservations", true,
                    "EquipmentReservationAuthoritySaveDto", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Money", "Financial obligations", true,
                    "FinancialObligationSaveDto", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Shopping", "Shopping trips", true,
                    "List<PlannedShoppingTripSaveDto>", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Business", "Runtime state (retail lots, category stock, worker slots)", true,
                    "BusinessRuntimeState", "Persisted (DTO-verified)."),
                new PersistenceExpectation("Production", "ActiveProcesses (in-flight generic production)", true,
                    "BusinessInstanceState.CaptureSaveDto / FromSaveDto -> GenericBusinessConfiguration.CaptureSaveDto / FromSaveDto",
                    "Source DTO capture/restore exists and Lane9fProductionRestoreTests covers process/task continuation. Main Scene fresh-scene Continue and consumed-input conservation remain unverified; the comparator still flags any actual loss or loud refusal."),
                new PersistenceExpectation("Production", "Bakery batches (in-progress)", false,
                    "BakeryShopRuntime / GenericProductionRuntimeRegistry",
                    "Production and delivery Save/Continue integrity (F R-2, C-5): an unfinished batch must survive Continue without disappearing or duplicating."),
                new PersistenceExpectation("Furniture", "Installed presentations", true,
                    "DevelopmentTownFurnishingInitializer.RestorePresentations (Lane 9f F R-3)",
                    "Restored where a configured item exists; purchased/manufactured pieces with no configured item are counted and diagnosed, never silently absent. No kind-to-prefab mapping exists anywhere in source."),
            };
        }

        /// <summary>True when the (system, field) pair has a legitimate transformation defined by an existing authority.</summary>
        public static bool IsLegitimateTransformation(string system, string field, out string rule)
        {
            rule = string.Empty;
            if (string.IsNullOrWhiteSpace(system) || string.IsNullOrWhiteSpace(field))
                return false;
            foreach (string[] entry in LegitimateTransformations)
            {
                if (entry.Length < 3)
                    continue;
                if (string.Equals(entry[0], system.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry[1], field.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    rule = entry[2];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Compare one field pre-save vs post-restore. A loud refusal
        /// (Lane 9f) is RefusedLoudly — flagged, never a pass. A value
        /// change on a (system, field) with a defined legitimate
        /// transformation is LegitimateTransformation, with the rule
        /// named; any other change is a defect.
        /// </summary>
        public static StateFieldComparison CompareField(PersistedField pre, PersistedField post)
        {
            string system = pre != null ? pre.System : (post != null ? post.System : string.Empty);
            string field = pre != null ? pre.Field : (post != null ? post.Field : string.Empty);
            string authority = "SaveLoadManager.CaptureSaveDtos / Series2Pass9Save restore (Agent F §5)";

            if (post != null && post.RefusedLoudly)
            {
                return new StateFieldComparison(system, field, StateComparisonOutcome.RefusedLoudly,
                    "Restore refused loudly instead of restoring or silently losing this state (Lane 9f: "
                    + "RestoreProcess / RestoreProcessesForBusiness restore or refuse loudly). The state is NOT "
                    + "restored — the refusal must be resolved, not filed as a pass. Reason: "
                    + (string.IsNullOrWhiteSpace(post.RefusalReason) ? "(no refusal reason recorded)" : post.RefusalReason),
                    authority,
                    "Pre-save present=" + (pre != null && pre.Present) + "; post-restore refused loudly.");
            }

            if ((pre == null || !pre.Present) && (post == null || !post.Present))
            {
                return new StateFieldComparison(system, field, StateComparisonOutcome.Equal,
                    "Absent on both sides.",
                    authority,
                    "Pre-save absent; post-restore absent.");
            }

            if (pre != null && pre.Present && (post == null || !post.Present))
            {
                return new StateFieldComparison(system, field, StateComparisonOutcome.Lost,
                    "Present in the save, absent after Continue. Only a meaningful loss counts — and this one is: "
                    + "the state is gone with no restoring authority and no legitimate transformation.",
                    authority,
                    "Pre-save: " + FieldEvidence(pre) + "; post-restore: absent.");
            }

            if ((pre == null || !pre.Present) && post != null && post.Present)
            {
                return new StateFieldComparison(system, field, StateComparisonOutcome.Appeared,
                    "Absent in the save, present after Continue. New state the save did not contain — suspicious, "
                    + "never assumed legitimate (a duplicated settled lot or a conjured obligation would arrive this way).",
                    authority,
                    "Pre-save: absent; post-restore: " + FieldEvidence(post) + ".");
            }

            // Both present.
            if (pre.Count >= 0 && post.Count >= 0 && post.Count > pre.Count)
            {
                return new StateFieldComparison(system, field, StateComparisonOutcome.Duplicated,
                    "Count grew across Continue (" + pre.Count + " → " + post.Count + "). A Continue that "
                    + "duplicates a settled lot or an in-progress batch is a defect, not a transformation (F R-2, C-5).",
                    authority,
                    "Pre-save: " + FieldEvidence(pre) + "; post-restore: " + FieldEvidence(post) + ".");
            }

            if (!string.Equals(pre.ValueSummary ?? string.Empty, post.ValueSummary ?? string.Empty, StringComparison.Ordinal)
                || (pre.Count >= 0 && post.Count >= 0 && pre.Count != post.Count))
            {
                string rule;
                if (IsLegitimateTransformation(system, field, out rule))
                {
                    return new StateFieldComparison(system, field, StateComparisonOutcome.LegitimateTransformation,
                        "Changed across Continue by a legitimate restoration transformation: " + rule,
                        authority,
                        "Pre-save: " + FieldEvidence(pre) + "; post-restore: " + FieldEvidence(post) + ".");
                }
                return new StateFieldComparison(system, field, StateComparisonOutcome.Changed,
                    "Value changed across Continue with no legitimate transformation defined by any existing "
                    + "authority. Only meaningful changes are defects — and this one is meaningful: the state "
                    + "that came back is not the state that was saved.",
                    authority,
                    "Pre-save: " + FieldEvidence(pre) + "; post-restore: " + FieldEvidence(post) + ".");
            }

            return new StateFieldComparison(system, field, StateComparisonOutcome.Equal,
                "Pre-save and post-restore agree.",
                authority,
                "Pre-save: " + FieldEvidence(pre) + "; post-restore: " + FieldEvidence(post) + ".");
        }

        private static string FieldEvidence(PersistedField field)
        {
            if (field == null)
                return "absent";
            string evidence = "present=" + field.Present;
            if (field.Count >= 0)
                evidence += "; count=" + field.Count;
            if (!string.IsNullOrWhiteSpace(field.ValueSummary))
                evidence += "; value='" + field.ValueSummary + "'";
            return evidence;
        }

        /// <summary>
        /// Compare two snapshots (matched by system + field). Fields
        /// present on only one side are compared against absence.
        /// Production integrity (F R-2, C-5) is checked first in the
        /// returned order: in-progress batches and freight obligations
        /// are the states a Continue must never lose or duplicate.
        /// </summary>
        public static SaveContinueComparisonReport Compare(
            IEnumerable<PersistedField> preSave, IEnumerable<PersistedField> postRestore)
        {
            var preByKey = new Dictionary<string, PersistedField>(StringComparer.OrdinalIgnoreCase);
            if (preSave != null)
            {
                foreach (PersistedField field in preSave)
                {
                    if (field == null) continue;
                    preByKey[KeyFor(field.System, field.Field)] = field;
                }
            }
            var postByKey = new Dictionary<string, PersistedField>(StringComparer.OrdinalIgnoreCase);
            if (postRestore != null)
            {
                foreach (PersistedField field in postRestore)
                {
                    if (field == null) continue;
                    postByKey[KeyFor(field.System, field.Field)] = field;
                }
            }

            var keys = new List<string>();
            foreach (string key in preByKey.Keys) keys.Add(key);
            foreach (string key in postByKey.Keys)
            {
                if (!preByKey.ContainsKey(key)) keys.Add(key);
            }
            // Production integrity first: in-progress work must never be lost or duplicated.
            keys.Sort((a, b) => ProductionPriority(a).CompareTo(ProductionPriority(b)));

            var comparisons = new List<StateFieldComparison>();
            foreach (string key in keys)
            {
                preByKey.TryGetValue(key, out PersistedField pre);
                postByKey.TryGetValue(key, out PersistedField post);
                comparisons.Add(CompareField(pre, post));
            }
            return new SaveContinueComparisonReport(comparisons);
        }

        private static int ProductionPriority(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 2;
            if (key.IndexOf("Production", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("Freight", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("Bakery", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0;
            if (key.IndexOf("Furniture", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1;
            return 2;
        }

        private static string KeyFor(string system, string field)
        {
            return (system ?? string.Empty).Trim() + "|" + (field ?? string.Empty).Trim();
        }

        /// <summary>Outcome label for display.</summary>
        public static string OutcomeLabel(StateComparisonOutcome outcome)
        {
            switch (outcome)
            {
                case StateComparisonOutcome.Equal: return "Equal";
                case StateComparisonOutcome.Lost: return "LOST — defect";
                case StateComparisonOutcome.Duplicated: return "DUPLICATED — defect";
                case StateComparisonOutcome.Changed: return "CHANGED — defect";
                case StateComparisonOutcome.LegitimateTransformation: return "Legitimate transformation";
                case StateComparisonOutcome.Appeared: return "APPEARED — defect";
                case StateComparisonOutcome.RefusedLoudly: return "Refused loudly (flagged, not restored)";
                default: return "Unknown";
            }
        }
    }
}
