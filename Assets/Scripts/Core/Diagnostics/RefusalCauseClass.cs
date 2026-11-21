using System;

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// Lane 1 (§4/§17, Agent M M-R14) — the causal classification every task
    /// refusal / interrupt carries as a structured field, so the Incident
    /// Investigator can classify a stalled task without reconstructing the
    /// call chain. The five destination classes come from the assignment §3;
    /// the sixth (OriginInvalid) is Agent M's M-06: the actor's current
    /// position is outside the authored walkable floor area when the route is
    /// requested, so the failure is at the ORIGIN, not the destination.
    ///
    /// Skill is never a class here: no refusal path reads capability (M-05).
    /// A diagnostic that blames skill for a refusal is wrong by construction.
    /// "No worker exists at all" is likewise not one of these classes — it is
    /// the L8 no-worker ladder in TaskDurationDiagnostics, not a refusal.
    /// </summary>
    public enum RefusalCauseClass
    {
        /// <summary>No class assigned yet. The refusal string was not recognised
        /// by the classifier and no caller tagged it explicitly. Unclassified
        /// is a fact about the evidence, not a judgement — it must never be
        /// silently treated as any other class.</summary>
        Unclassified = 0,

        /// <summary>Class 1 — the destination does not exist: no authored
        /// activity point (e.g. a home with no Firewood point), no stove,
        /// no counter/service point, no receiving point.</summary>
        DestinationMissing = 1,

        /// <summary>Class 2 — the destination exists but is inaccessible:
        /// the target cell is not walkable, or no route reaches it although
        /// both endpoints are valid.</summary>
        DestinationInaccessible = 2,

        /// <summary>Class 3 — the destination is accessible but occupied or
        /// reserved: a reservation or occupancy refusal names the conflicting
        /// holder. The fully implemented class; the standard the other
        /// classes are brought up to.</summary>
        DestinationOccupiedOrReserved = 3,

        /// <summary>Class 4 — the worker cannot perform the task: no managed
        /// actor, off-map, dead, or Hidden presentation, so the work cannot
        /// physically happen. (Not skill: capability never gates a task.)</summary>
        WorkerCannotPerform = 4,

        /// <summary>Class 5 — the worker can perform the task but another
        /// legitimate obligation takes priority: the schedule tracker refuses
        /// an overlap with the activity the person is already doing, or a
        /// domestic activity is deferred while the resident does freight work.</summary>
        OtherObligationTakesPriority = 5,

        /// <summary>Class 6 (M-06) — origin invalid: the actor's current
        /// position is outside the authored walkable floor area when the
        /// route is requested (PathingFailureCategory.StartNotWalkable).
        /// The 2,878 StartNotWalkable warnings in the retained 60-day run
        /// are this class, across sleep (2,116), meal (729) and other
        /// domestic (33) routing attempts, plus 850 seller-route aborts
        /// under a separate signature that cite the same invalid start.</summary>
        OriginInvalid = 6,
    }

    /// <summary>
    /// One tagged refusal/interrupt: the causal class plus the refusal reason
    /// verbatim and the refusing authority method. The reason string is
    /// evidence; the class is the tag. Callers that know the class pass it
    /// explicitly; callers holding only a string use
    /// <see cref="RefusalCauseClassifier.Classify(string)"/>.
    /// </summary>
    public struct RefusalCauseTag
    {
        /// <summary>The causal class (Unclassified when unknown).</summary>
        public RefusalCauseClass CauseClass;

        /// <summary>The refusal reason verbatim, as the refusing authority wrote it.</summary>
        public string Reason;

        /// <summary>The refusing authority method ("AssignTask" / "StartTask" / "TryReserveAll" / ...).</summary>
        public string SourceMethod;

        public RefusalCauseTag(RefusalCauseClass causeClass, string reason, string sourceMethod)
        {
            CauseClass = causeClass;
            Reason = reason ?? string.Empty;
            SourceMethod = sourceMethod ?? string.Empty;
        }

        /// <summary>Builds a tag by classifying a refusal string (no explicit class known).</summary>
        public static RefusalCauseTag FromReason(string reason, string sourceMethod)
        {
            return new RefusalCauseTag(RefusalCauseClassifier.Classify(reason), reason, sourceMethod);
        }
    }

    /// <summary>
    /// Lane 1 (M-R14) — best-effort classification of the refusal strings
    /// that already exist in the tree, so legacy string-only refusal sites
    /// can be tagged without rewriting them. Every pattern below is a
    /// verbatim string observed in source at the Lane 1 HEAD:
    /// <list type="bullet">
    /// <item>OriginInvalid — PathingManager.TryFindPath / TryGetInteriorPointFailure:
    /// "Point is outside the authored walkable floor area on floor 0.",
    /// "Start cell {coord} is not walkable.", "Start cell {coord} is outside
    /// the grid."; the shopping abort "No physical route reaches seller
    /// location" cites the same invalid start (M-06).</item>
    /// <item>DestinationInaccessible — "Target cell {coord} is not walkable.",
    /// "Target cell {coord} is outside the grid.", "No path found from ...".</item>
    /// <item>DestinationOccupiedOrReserved — EquipmentReservationService:
    /// "Reservation refused: {key} is already reserved ({holder})";
    /// TaskAuthorityLifecycle: "{id} is already occupied by task {taskId}".</item>
    /// <item>OtherObligationTakesPriority — PersonScheduleTracker:
    /// "Refused: P{n} is already {activity} [{start}-{end}] ...";
    /// PopulationPathingDirector: "Domestic activity is deferred while ...".</item>
    /// <item>DestinationMissing — DomesticLaborDriver: "no authored Firewood
    /// location", MissingFirewoodStorageLocationId.</item>
    /// <item>WorkerCannotPerform — PopulationPathingDirector:
    /// "No visible on-map presentation".</item>
    /// </list>
    /// Anything else returns Unclassified — the caller must tag it explicitly
    /// rather than have the classifier guess. One ambiguity is documented,
    /// not hidden: the floor-area message ("Point is outside the authored
    /// walkable floor area ...") is produced for BOTH the start and the
    /// target cell (TryGetInteriorPointFailure is called for each), so a bare
    /// reason string cannot distinguish them. The classifier maps it to
    /// OriginInvalid, matching the observed run population (all 2,878
    /// retained-run occurrences are start failures); a caller holding the
    /// PathingFailureCategory must tag TargetNotWalkable explicitly as
    /// DestinationInaccessible instead of routing the bare string here.
    /// </summary>
    public static class RefusalCauseClassifier
    {
        /// <summary>Classifies a refusal reason string; Unclassified when no known pattern matches.</summary>
        public static RefusalCauseClass Classify(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return RefusalCauseClass.Unclassified;
            }

            // Origin-invalid first: the floor-area message is the dominant
            // observed refusal (2,878 in the retained 60-day run).
            if (reason.IndexOf("outside the authored walkable floor area", StringComparison.Ordinal) >= 0
                || reason.IndexOf("StartNotWalkable", StringComparison.Ordinal) >= 0
                || reason.IndexOf("No physical route reaches seller location", StringComparison.Ordinal) >= 0
                || (reason.IndexOf("Start cell", StringComparison.Ordinal) >= 0
                    && (reason.IndexOf("is not walkable", StringComparison.Ordinal) >= 0
                        || reason.IndexOf("is outside the grid", StringComparison.Ordinal) >= 0)))
            {
                return RefusalCauseClass.OriginInvalid;
            }

            // Destination-inaccessible: the target endpoint is the problem.
            if ((reason.IndexOf("Target cell", StringComparison.Ordinal) >= 0
                    && (reason.IndexOf("is not walkable", StringComparison.Ordinal) >= 0
                        || reason.IndexOf("is outside the grid", StringComparison.Ordinal) >= 0))
                || reason.IndexOf("No path found from", StringComparison.Ordinal) >= 0)
            {
                return RefusalCauseClass.DestinationInaccessible;
            }

            // Occupied / reserved (class 3): check before the schedule-overlap
            // pattern below, which also contains "is already".
            if (reason.IndexOf("is already reserved", StringComparison.Ordinal) >= 0
                || reason.IndexOf("is already occupied by task", StringComparison.Ordinal) >= 0)
            {
                return RefusalCauseClass.DestinationOccupiedOrReserved;
            }

            // Another legitimate obligation takes priority (class 5).
            if ((reason.IndexOf("Refused: P", StringComparison.Ordinal) == 0
                    && reason.IndexOf("is already", StringComparison.Ordinal) >= 0)
                || reason.IndexOf("is deferred while", StringComparison.Ordinal) >= 0)
            {
                return RefusalCauseClass.OtherObligationTakesPriority;
            }

            // Destination missing (class 1): no authored point at all.
            // The retained 60-day run's wording ("no firewood storage is
            // authored on the home.") carries neither "no authored" nor the
            // MissingFirewoodStorageLocationId id, so it is matched
            // explicitly (Lane 4 follow-up).
            if (reason.IndexOf("no authored", StringComparison.Ordinal) >= 0
                || reason.IndexOf("no firewood storage is authored", StringComparison.Ordinal) >= 0
                || reason.IndexOf("MissingFirewoodStorageLocationId", StringComparison.Ordinal) >= 0)
            {
                return RefusalCauseClass.DestinationMissing;
            }

            // Worker cannot perform (class 4): no physical presentation.
            if (reason.IndexOf("No visible on-map presentation", StringComparison.Ordinal) >= 0)
            {
                return RefusalCauseClass.WorkerCannotPerform;
            }

            return RefusalCauseClass.Unclassified;
        }

        /// <summary>Stable label for a cause class (log lines, export).</summary>
        public static string Label(RefusalCauseClass causeClass)
        {
            switch (causeClass)
            {
                case RefusalCauseClass.DestinationMissing: return "destination-missing";
                case RefusalCauseClass.DestinationInaccessible: return "destination-inaccessible";
                case RefusalCauseClass.DestinationOccupiedOrReserved: return "destination-occupied-or-reserved";
                case RefusalCauseClass.WorkerCannotPerform: return "worker-cannot-perform";
                case RefusalCauseClass.OtherObligationTakesPriority: return "other-obligation-takes-priority";
                case RefusalCauseClass.OriginInvalid: return "origin-invalid";
                default: return "unclassified";
            }
        }
    }
}
