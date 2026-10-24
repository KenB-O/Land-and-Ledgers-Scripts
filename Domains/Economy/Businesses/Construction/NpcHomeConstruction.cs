using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Phase D (Real People): the material source a construction project
    /// consumes from. The runtime adapter implements this against the real
    /// lot systems (lumber-yard W4B, sawmill W4A, general store); tests fake
    /// it with finite stock. Provenance is always recorded — no synthetic
    /// stock, no invented lots.
    /// </summary>
    public interface IConstructionMaterialSource
    {
        int AvailableUnits(ConstructionMaterialRequirement requirement);
        /// <summary>
        /// Consumes real lots. Returns null on success (with the provenance
        /// label of the consumed lots), or a refusal — nothing is consumed
        /// on refusal.
        /// </summary>
        string TryConsume(ConstructionMaterialRequirement requirement, int units,
            string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel);
    }

    /// <summary>
    /// Phase D: tool custody for self-build labor — the commissioning
    /// household must actually hold the tools a design phase requires.
    /// The runtime adapter reads the household's real inventory.
    /// </summary>
    public interface IHouseholdToolCustody
    {
        bool HouseholdHoldsTool(int householdId, string toolItemId);
    }

    /// <summary>Phase D: work-package lifecycle. Append-only.</summary>
    public enum WorkPackageStatus
    {
        Draft = 0,
        Ready = 1,
        MaterialsStaged = 2,
        InProgress = 3,
        Complete = 4,
    }

    /// <summary>
    /// Phase D: one executable unit of construction work — the missing
    /// execution authority Phase A found absent (the contract book records
    /// money obligations but never consumes material or advances work).
    /// A package advances only in dependency order, with real staged
    /// materials and real committed person-time.
    /// </summary>
    [Serializable]
    public sealed class WorkPackage
    {
        public string PackageId = string.Empty;
        public string ProjectId = string.Empty;
        public string PhaseName = string.Empty;
        public int Sequence;
        public List<ConstructionMaterialRequirement> Materials = new List<ConstructionMaterialRequirement>();
        public List<string> MaterialProvenance = new List<string>();
        public int LaborMinutesRequired;
        public int LaborMinutesCommitted;
        public List<string> RequiredToolItemIds = new List<string>();
        public List<string> DependsOnPackageIds = new List<string>();
        public WorkPackageStatus Status = WorkPackageStatus.Draft;
        public int CompletedDayIndex = -1;
        /// <summary>
        /// Pass VIII Worker 3: the staged lines consumed for THIS package
        /// (a per-package view of the project's MaterialsConsumed, used for
        /// cancellation accounting — staged lines return to the source
        /// exactly once).
        /// </summary>
        public List<ConsumedMaterialLine> StagedMaterialLines = new List<ConsumedMaterialLine>();
        /// <summary>
        /// Pass VIII Worker 3: true once this package's staged lines were
        /// returned to the source on cancellation. Guards double-return.
        /// </summary>
        public bool StagedMaterialsReturned;

        public WorkPackage() { }
    }

    /// <summary>Phase D: project lifecycle. Append-only.</summary>
    public enum ConstructionProjectStatus
    {
        DesignSelected = 0,
        WorkPackagesCreated = 1,
        InProgress = 2,
        Complete = 3,
        Cancelled = 4,
        /// <summary>Phase F: paused by the commissioning NPC over an
        /// unresolved overrun or funding gap — funding halts until resumed.</summary>
        Paused = 5,
    }

    /// <summary>Phase D: one consumed material line with its provenance.</summary>
    [Serializable]
    public sealed class ConsumedMaterialLine
    {
        public string DisplayName = string.Empty;
        public int Units;
        public string UnitLabel = string.Empty;
        public string Provenance = string.Empty;

        public ConsumedMaterialLine() { }
    }

    /// <summary>
    /// Pass VIII Worker 3: binds one real labor WorkTask to the package it
    /// works. WorkCommittedLabor verifies the binding — minutes worked on
    /// another package's task can never credit this one.
    /// </summary>
    [Serializable]
    public sealed class LaborTaskBinding
    {
        public string TaskId = string.Empty;
        public string PackageId = string.Empty;

        public LaborTaskBinding() { }
    }

    /// <summary>
    /// Pass VIII Worker 3: one equipment hold taken for a package — the
    /// reservation key plus the package it serves, so completion and
    /// cancellation release exactly what they took.
    /// </summary>
    [Serializable]
    public sealed class EquipmentHold
    {
        public string PackageId = string.Empty;
        public string ReservationKey = string.Empty;

        public EquipmentHold() { }
    }

    /// <summary>
    /// Phase D: the construction project — contract → parcel + labor +
    /// materials + progress billing → completed structure. The project owns
    /// the staged execution the contract book lacks; it never creates a
    /// finished structure from a bare need flag.
    /// </summary>
    [Serializable]
    public sealed class ConstructionProject
    {
        public string ProjectId = string.Empty;
        public string DesignId = string.Empty;
        public int HouseholdId = -1;       // commissioning household
        public string ParcelId = string.Empty;
        public string ContractId = string.Empty; // written contract, when one exists
        public string BuildingId = string.Empty;
        public ConstructionProjectStatus Status = ConstructionProjectStatus.DesignSelected;
        public List<WorkPackage> WorkPackages = new List<WorkPackage>();
        public List<ConsumedMaterialLine> MaterialsConsumed = new List<ConsumedMaterialLine>();
        public int TotalLaborMinutesCommitted;
        public List<string> LaborReservationIds = new List<string>();
        /// <summary>
        /// Pass IV Worker 3: real WorkTask ids for committed construction labor.
        /// Labor is worked through TaskAuthority; minutes are credited to the
        /// package only as the task is worked (never at reserve time).
        /// </summary>
        public List<string> LaborTaskIds = new List<string>();
        /// <summary>
        /// Pass VIII Worker 3: which package each labor task works. Verified
        /// by WorkCommittedLabor — a task never credits the wrong package.
        /// </summary>
        public List<LaborTaskBinding> LaborTaskBindings = new List<LaborTaskBinding>();
        /// <summary>
        /// Pass VIII Worker 3: equipment holds taken for packages. Released
        /// on package completion or project cancellation — never leaked.
        /// </summary>
        public List<EquipmentHold> EquipmentHolds = new List<EquipmentHold>();
        /// <summary>
        /// True when the design needs a Unity prefab unavailable in this
        /// repo — the data construction is real, but visual placement is
        /// pending Codex's Unity integration (handoff filed).
        /// </summary>
        public bool VisualPlacementPending;
        public string PrefabHandoffRecordId = string.Empty;
        public int CompletedDayIndex = -1;

        public ConstructionProject() { }

        public WorkPackage FindPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;
            foreach (WorkPackage package in WorkPackages)
                if (package != null && string.Equals(package.PackageId, packageId, StringComparison.Ordinal))
                    return package;
            return null;
        }
    }

    /// <summary>
    /// Phase D: the project execution authority. Stages:
    /// design → work packages → materials (real lots, provenance) → labor
    /// (person-time, no double-booking) → package completion in dependency
    /// order → completed building → accommodation spaces → occupancy.
    /// Every stage can refuse; a refusal leaves the project exactly where
    /// it was — never half-built, never faked complete.
    /// </summary>
    public sealed class ConstructionProjectExecutor
    {
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Pass VIII Worker 3: optional equipment reservation authority.
        /// When set, CommitLabor reserves the package's required tools
        /// through the REAL reservation authority (no double-booking across
        /// packages/projects); holds release on package completion or
        /// project cancellation. Null = the pre-Pass-VIII behavior (tool
        /// custody only).
        /// </summary>
        public EquipmentReservationService EquipmentReservations { get; set; }

        /// <summary>
        /// Pass VIII Worker 3: equipment key for one required tool of one
        /// household's build. One key per (household, tool) — the reservation
        /// authority owns exclusivity.
        /// </summary>
        public static string EquipmentKeyFor(int householdId, string toolItemId)
        {
            return "construction-tool:" + householdId + ":" + (toolItemId ?? string.Empty);
        }

        /// <summary>
        /// Phase D: identifies the housing need's answer as a project —
        /// design + parcel + commissioning household, all real. No project
        /// is created from a bare NeedHousing flag: the caller passes the
        /// searched need's resolution (design + parcel ids).
        /// </summary>
        public ConstructionProject CreateProject(
            BuildingDesignCatalog catalog,
            int householdId,
            string designId,
            string parcelId,
            int dayIndex,
            List<string> diag)
        {
            return CreateProject(catalog, householdId, designId, parcelId, null, dayIndex, diag);
        }

        /// <summary>
        /// Pass VIII Worker 3: parcel-authorized project creation. The
        /// parcel authority (the runtime adapter reads the real
        /// TitleAuthority) verifies the parcel exists and the commissioning
        /// household holds it — a project is never created on a parcel
        /// nobody holds. Null authority = the Phase D behavior (unchecked).
        /// </summary>
        public ConstructionProject CreateProject(
            BuildingDesignCatalog catalog,
            int householdId,
            string designId,
            string parcelId,
            IConstructionParcelAuthority parcelAuthority,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (catalog == null)
                return Refuse<ConstructionProject>(diag, "no design catalog — designs are catalog data, never invented.");
            if (householdId < 0)
                return Refuse<ConstructionProject>(diag, "a project needs a real commissioning household.");
            if (string.IsNullOrWhiteSpace(parcelId))
                return Refuse<ConstructionProject>(diag, "a project needs a real, legally available parcel id.");
            BuildingDesign design = catalog.FindDesign(designId);
            if (design == null)
                return Refuse<ConstructionProject>(diag, $"unknown design '{designId}' — projects build catalog designs only.");
            if (parcelAuthority != null)
            {
                string authProblem = parcelAuthority.AuthorizeBuild(householdId, parcelId, dayIndex, diag);
                if (authProblem != null)
                    return Refuse<ConstructionProject>(diag, "parcel authorization refused: " + authProblem);
            }

            var project = new ConstructionProject
            {
                ProjectId = $"cproj-{sequence++}",
                DesignId = design.DesignId,
                HouseholdId = householdId,
                ParcelId = parcelId,
                Status = ConstructionProjectStatus.DesignSelected,
            };

            PrefabHandoffRecord handoff = catalog.RequestPrefabHandoff(design, dayIndex, diag);
            if (handoff != null)
            {
                project.VisualPlacementPending = true;
                project.PrefabHandoffRecordId = handoff.RecordId;
                diag.Add($"ConstructionProjectExecutor: project '{project.ProjectId}' proceeds as DATA; " +
                    $"visual placement pending Codex handoff '{handoff.RecordId}' (prefab '{handoff.RequiredPrefabId}').");
            }

            diag.Add($"ConstructionProjectExecutor: project '{project.ProjectId}' created — design '{design.DesignId}' on parcel '{parcelId}' for H{householdId}.");
            return project;
        }

        /// <summary>Phase D: one work package per design phase, in sequence order.</summary>
        public string CreateWorkPackages(ConstructionProject project, BuildingDesign design, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || design == null)
                return "ConstructionProjectExecutor.CreateWorkPackages: project and design required.";
            if (project.Status != ConstructionProjectStatus.DesignSelected)
                return $"ConstructionProjectExecutor.CreateWorkPackages: project '{project.ProjectId}' is {project.Status} — packages are created once.";
            if (project.WorkPackages.Count > 0)
                return $"ConstructionProjectExecutor.CreateWorkPackages: project '{project.ProjectId}' already has packages.";

            var ordered = new List<BuildingDesignPhase>(design.Phases ?? new List<BuildingDesignPhase>());
            ordered.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            string previousPackageId = null;
            foreach (BuildingDesignPhase phase in ordered)
            {
                if (phase == null) continue;
                var package = new WorkPackage
                {
                    PackageId = $"wpkg-{project.ProjectId}-{sequence++}",
                    ProjectId = project.ProjectId,
                    PhaseName = phase.PhaseName ?? string.Empty,
                    Sequence = phase.Sequence,
                    Materials = new List<ConstructionMaterialRequirement>(phase.Materials ?? new List<ConstructionMaterialRequirement>()),
                    LaborMinutesRequired = Math.Max(0, phase.LaborMinutes),
                    RequiredToolItemIds = new List<string>(phase.RequiredToolItemIds ?? new List<string>()),
                    Status = WorkPackageStatus.Ready,
                };
                if (previousPackageId != null) package.DependsOnPackageIds.Add(previousPackageId);
                project.WorkPackages.Add(package);
                previousPackageId = package.PackageId;
            }
            project.Status = ConstructionProjectStatus.WorkPackagesCreated;
            diag.Add($"ConstructionProjectExecutor: project '{project.ProjectId}' — {project.WorkPackages.Count} work package(s) created from design '{design.DesignId}'.");
            return null;
        }

        /// <summary>
        /// Phase D: stages a package's materials from REAL lots. All-or-nothing:
        /// when any requirement cannot be met in full, nothing is consumed.
        /// </summary>
        public string StagePackageMaterials(
            ConstructionProject project,
            WorkPackage package,
            IConstructionMaterialSource materialSource,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || package == null)
                return "ConstructionProjectExecutor.StagePackageMaterials: project and package required.";
            if (materialSource == null)
                return "ConstructionProjectExecutor.StagePackageMaterials: no material source — materials come from real lots, never thin air.";
            if (package.Status != WorkPackageStatus.Ready)
                return $"ConstructionProjectExecutor.StagePackageMaterials: package '{package.PackageId}' is {package.Status} — materials stage once from Ready.";

            foreach (ConstructionMaterialRequirement requirement in package.Materials)
            {
                if (requirement == null || !requirement.IsCoherent)
                    return $"ConstructionProjectExecutor.StagePackageMaterials: package '{package.PackageId}' has an incoherent material requirement — refusing.";
                int available = materialSource.AvailableUnits(requirement);
                if (available < requirement.RequiredUnits)
                    return $"ConstructionProjectExecutor.StagePackageMaterials: insufficient {requirement.MaterialDisplayName} " +
                        $"({available} available, {requirement.RequiredUnits} required) — nothing consumed, no synthetic stock.";
            }

            foreach (ConstructionMaterialRequirement requirement in package.Materials)
            {
                string provenance;
                string refusal = materialSource.TryConsume(requirement, requirement.RequiredUnits,
                    $"project {project.ProjectId}", dayIndex, diag, out provenance);
                if (refusal != null)
                    return $"ConstructionProjectExecutor.StagePackageMaterials: {refusal}";
                package.MaterialProvenance.Add(provenance ?? string.Empty);
                var line = new ConsumedMaterialLine
                {
                    DisplayName = requirement.MaterialDisplayName,
                    Units = requirement.RequiredUnits,
                    UnitLabel = requirement.UnitLabel ?? string.Empty,
                    Provenance = provenance ?? string.Empty,
                };
                project.MaterialsConsumed.Add(line);
                package.StagedMaterialLines.Add(line);
            }
            package.Status = WorkPackageStatus.MaterialsStaged;
            diag.Add($"ConstructionProjectExecutor: package '{package.PackageId}' materials staged ({package.Materials.Count} requirement(s), real lots).");
            return null;
        }

        /// <summary>
        /// Pass VIII Worker 3: stages a package's materials from the
        /// project's ON-SITE stockpile — the receiving end of the freight
        /// leg. Same all-or-nothing guarantees as staging from a source:
        /// when the stockpile cannot cover every requirement in full,
        /// nothing is consumed.
        /// </summary>
        public string StagePackageMaterialsFromStockpile(
            ConstructionProject project,
            WorkPackage package,
            ConstructionSiteStockpile stockpile,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || package == null)
                return "ConstructionProjectExecutor.StagePackageMaterialsFromStockpile: project and package required.";
            if (stockpile == null)
                return "ConstructionProjectExecutor.StagePackageMaterialsFromStockpile: no site stockpile — freighted materials land at the site, never in the package directly.";
            if (package.Status != WorkPackageStatus.Ready)
                return $"ConstructionProjectExecutor.StagePackageMaterialsFromStockpile: package '{package.PackageId}' is {package.Status} — materials stage once from Ready.";

            List<ConsumedMaterialLine> consumed;
            string consumeProblem = stockpile.ConsumeForPackage(package, dayIndex, diag, out consumed);
            if (consumeProblem != null)
                return "ConstructionProjectExecutor.StagePackageMaterialsFromStockpile: " + consumeProblem;
            foreach (ConsumedMaterialLine line in consumed ?? new List<ConsumedMaterialLine>())
            {
                if (line == null) continue;
                package.MaterialProvenance.Add(line.Provenance ?? string.Empty);
                project.MaterialsConsumed.Add(line);
                package.StagedMaterialLines.Add(line);
            }
            package.Status = WorkPackageStatus.MaterialsStaged;
            diag.Add($"ConstructionProjectExecutor: package '{package.PackageId}' staged from the on-site stockpile ({consumed?.Count ?? 0} line(s)).");
            return null;
        }

        /// <summary>
        /// Phase D (Pass IV Worker 3): commits person-time to a package via a
        /// real <see cref="WorkTask"/>. The <see cref="PersonScheduleTracker"/>
        /// owns the simultaneity guard — a double-booked person is refused
        /// LOUDLY here, never silently overlapped. The commissioning household
        /// must hold the phase's required tools. The task is assigned through
        /// <see cref="TaskAuthority"/>, which books the person's
        /// <see cref="WorkTimeBudgetStore"/> — a worker with no budget left is
        /// refused LOUDLY. Labor minutes are credited to the package ONLY as
        /// the task is worked (see <see cref="WorkCommittedLabor"/>), never at
        /// reserve time.
        /// </summary>
        public string CommitLabor(
            ConstructionProject project,
            WorkPackage package,
            int personId,
            PersonScheduleTracker scheduleTracker,
            IHouseholdToolCustody toolCustody,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            string label,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || package == null)
                return "ConstructionProjectExecutor.CommitLabor: project and package required.";
            if (scheduleTracker == null)
                return "ConstructionProjectExecutor.CommitLabor: no schedule tracker — person time is authoritative.";
            if (tasks == null)
                return "ConstructionProjectExecutor.CommitLabor: no TaskAuthority — labor must be a real WorkTask.";
            if (budgets == null)
                return "ConstructionProjectExecutor.CommitLabor: no WorkTimeBudgetStore — labor must respect the work budget.";
            if (package.Status != WorkPackageStatus.MaterialsStaged && package.Status != WorkPackageStatus.InProgress)
                return $"ConstructionProjectExecutor.CommitLabor: package '{package.PackageId}' is {package.Status} — stage materials before labor.";
            if (toolCustody != null)
            {
                foreach (string toolId in package.RequiredToolItemIds)
                {
                    if (!toolCustody.HouseholdHoldsTool(project.HouseholdId, toolId))
                        return $"ConstructionProjectExecutor.CommitLabor: H{project.HouseholdId} holds no '{toolId}' — equipment is real, not assumed.";
                }
            }

            string reservationLabel = $"{label} [{project.ProjectId}/{package.PackageId}]";

            // Pass VIII Worker 3: equipment first — the package's required
            // tools are reserved through the REAL reservation authority when
            // one is wired. A tool already held by another package/project
            // refuses LOUDLY here; the project never advances without its
            // equipment.
            List<string> equipmentKeys = null;
            if (EquipmentReservations != null
                && package.RequiredToolItemIds != null
                && package.RequiredToolItemIds.Count > 0)
            {
                equipmentKeys = new List<string>();
                foreach (string toolId in package.RequiredToolItemIds)
                {
                    if (string.IsNullOrWhiteSpace(toolId)) continue;
                    equipmentKeys.Add(EquipmentKeyFor(project.HouseholdId, toolId));
                }
                if (equipmentKeys.Count > 0)
                {
                    string reserveProblem = EquipmentReservations.ReserveAll(equipmentKeys,
                        $"project:{project.ProjectId}/package:{package.PackageId}",
                        reservationLabel, diag);
                    if (reserveProblem != null)
                    {
                        diag.Add($"ConstructionProjectExecutor: equipment refused — {reserveProblem}");
                        return $"ConstructionProjectExecutor.CommitLabor refused: equipment unavailable — {reserveProblem}";
                    }
                }
            }

            string refusal = scheduleTracker.TryReserve(
                personId, PersonActivityKind.Building, dayIndex, startMinuteOfDay, durationMinutes, reservationLabel);
            if (refusal != null)
            {
                ReleaseEquipmentKeys(EquipmentReservations, project, package.PackageId, diag);
                diag.Add($"ConstructionProjectExecutor: labor refused — {refusal}");
                return $"ConstructionProjectExecutor.CommitLabor refused: {refusal}";
            }
            string scheduleReservationId = FindReservationId(scheduleTracker, personId, dayIndex, reservationLabel);

            int committed = WorkTimeMath.ClampToWholeMinutes(durationMinutes);
            if (budgets.CurrentAbsoluteDayIndex >= 0)
                budgets.EnsureDay(dayIndex);
            string definitionId = $"construction-labor:{package.PhaseName}";
            if (tasks.GetDefinition(definitionId) == null)
            {
                string registrationFailure;
                if (!tasks.RegisterDefinition(new TaskDefinition(definitionId, $"Construction — {package.PhaseName}", committed), out registrationFailure))
                {
                    ReleaseScheduleAndEquipment(scheduleTracker, scheduleReservationId, project, package.PackageId, EquipmentReservations, diag);
                    return $"ConstructionProjectExecutor.CommitLabor refused: {registrationFailure}";
                }
            }

            WorkTask task = tasks.CreateTaskWithPlannedMinutes(
                definitionId,
                EntityId.For(EntityKind.Household, Math.Max(0, project.HouseholdId)),
                dayIndex,
                committed);
            string assignFailure;
            if (!tasks.AssignTask(task.TaskId, EntityId.For(EntityKind.Person, personId), budgets, dayIndex, out assignFailure))
            {
                ReleaseScheduleAndEquipment(scheduleTracker, scheduleReservationId, project, package.PackageId, EquipmentReservations, diag);
                return $"ConstructionProjectExecutor.CommitLabor refused: {assignFailure}";
            }
            string startFailure;
            if (!tasks.StartTask(task.TaskId, dayIndex, out startFailure))
            {
                ReleaseScheduleAndEquipment(scheduleTracker, scheduleReservationId, project, package.PackageId, EquipmentReservations, diag);
                return $"ConstructionProjectExecutor.CommitLabor refused: {startFailure}";
            }

            string taskId = task.TaskId.ToString();
            project.LaborTaskIds.Add(taskId);
            project.LaborTaskBindings.Add(new LaborTaskBinding { TaskId = taskId, PackageId = package.PackageId });
            if (equipmentKeys != null)
            {
                foreach (string key in equipmentKeys)
                    project.EquipmentHolds.Add(new EquipmentHold { PackageId = package.PackageId, ReservationKey = key });
            }
            package.Status = WorkPackageStatus.InProgress;

            if (!string.IsNullOrWhiteSpace(scheduleReservationId)
                && !project.LaborReservationIds.Contains(scheduleReservationId))
                project.LaborReservationIds.Add(scheduleReservationId);
            diag.Add($"ConstructionProjectExecutor: P{personId} committed {committed} min to package '{package.PackageId}' (day {dayIndex}).");
            return null;
        }

        private static string FindReservationId(PersonScheduleTracker scheduleTracker, int personId,
            int dayIndex, string label)
        {
            foreach (PersonActivityReservation reservation in scheduleTracker.ActiveFor(personId, dayIndex))
            {
                if (reservation != null && !reservation.Released
                    && string.Equals(reservation.Label, label, StringComparison.Ordinal))
                    return reservation.ReservationId;
            }
            return string.Empty;
        }

        private static void ReleaseScheduleAndEquipment(PersonScheduleTracker scheduleTracker,
            string scheduleReservationId, ConstructionProject project, string packageId,
            EquipmentReservationService equipment, List<string> diag)
        {
            if (scheduleTracker != null && !string.IsNullOrWhiteSpace(scheduleReservationId))
            {
                scheduleTracker.Release(scheduleReservationId);
                project.LaborReservationIds.Remove(scheduleReservationId);
            }
            ReleaseEquipmentKeys(equipment, project, packageId, diag);
        }

        private static void ReleaseEquipmentKeys(EquipmentReservationService equipment,
            ConstructionProject project, string packageId, List<string> diag)
        {
            if (equipment == null || project == null) return;
            var remaining = new List<EquipmentHold>();
            foreach (EquipmentHold hold in project.EquipmentHolds)
            {
                if (hold != null && string.Equals(hold.PackageId, packageId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(hold.ReservationKey))
                {
                    equipment.Release(hold.ReservationKey);
                    diag?.Add($"ConstructionProjectExecutor: released equipment '{hold.ReservationKey}' for package '{packageId}'.");
                }
                else if (hold != null)
                {
                    remaining.Add(hold);
                }
            }
            project.EquipmentHolds = remaining;
        }

        /// <summary>Compatibility overload for pre-Pass-IV callers.</summary>
        public string CommitLabor(
            ConstructionProject project,
            WorkPackage package,
            int personId,
            PersonScheduleTracker scheduleTracker,
            IHouseholdToolCustody toolCustody,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            string label,
            List<string> diag)
        {
            return CommitLabor(project, package, personId, scheduleTracker, toolCustody,
                null, null, dayIndex, startMinuteOfDay, durationMinutes, label, diag);
        }

        /// <summary>
        /// Applies worked minutes through the universal TaskAuthority. This is
        /// intentionally separate from reservation so construction cannot receive
        /// labor credit before the worker actually advances the task. The task
        /// must be bound to THIS package (see CommitLabor) — minutes worked
        /// on another package's task can never credit this one.
        /// </summary>
        public string WorkCommittedLabor(
            ConstructionProject project,
            WorkPackage package,
            string laborTaskId,
            int minutesToWork,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || package == null || tasks == null || budgets == null)
                return "ConstructionProjectExecutor.WorkCommittedLabor: project, package, TaskAuthority and budgets required.";
            if (string.IsNullOrWhiteSpace(laborTaskId))
                return "ConstructionProjectExecutor.WorkCommittedLabor: no labor task id — refusing phantom work.";
            bool bound = false;
            foreach (LaborTaskBinding binding in project.LaborTaskBindings)
            {
                if (binding != null
                    && string.Equals(binding.TaskId, laborTaskId, StringComparison.Ordinal)
                    && string.Equals(binding.PackageId, package.PackageId, StringComparison.Ordinal))
                {
                    bound = true;
                    break;
                }
            }
            if (!bound)
                return $"ConstructionProjectExecutor.WorkCommittedLabor: task '{laborTaskId}' is not bound to package '{package.PackageId}' — refusing cross-package credit.";
            WorkTask task = tasks.FindTask(laborTaskId);
            if (task == null)
                return $"ConstructionProjectExecutor.WorkCommittedLabor: no labor task '{laborTaskId}'.";
            int before = task.MinutesWorked;
            bool completed;
            string refusal;
            if (!tasks.RecordWorkProgress(task.TaskId, minutesToWork, budgets, dayIndex, out completed, out refusal))
                return refusal;
            int worked = Math.Max(0, task.MinutesWorked - before);
            package.LaborMinutesCommitted += worked;
            project.TotalLaborMinutesCommitted += worked;
            if (completed)
                tasks.CompleteTask(task.TaskId, TaskOutcome.Success, dayIndex, budgets);
            return null;
        }

        /// <summary>
        /// Phase D: advances a package to Complete. Requires staged materials,
        /// enough committed labor, and every dependency complete — stages are
        /// never skipped.
        /// </summary>
        public string AdvancePackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || package == null)
                return "ConstructionProjectExecutor.AdvancePackage: project and package required.";
            if (package.Status == WorkPackageStatus.Complete)
                return $"ConstructionProjectExecutor.AdvancePackage: package '{package.PackageId}' is already complete.";
            if (package.Status != WorkPackageStatus.MaterialsStaged && package.Status != WorkPackageStatus.InProgress)
                return $"ConstructionProjectExecutor.AdvancePackage: package '{package.PackageId}' is {package.Status} — materials and labor come first.";
            foreach (string dependencyId in package.DependsOnPackageIds)
            {
                WorkPackage dependency = project.FindPackage(dependencyId);
                if (dependency == null || dependency.Status != WorkPackageStatus.Complete)
                    return $"ConstructionProjectExecutor.AdvancePackage: package '{package.PackageId}' waits on '{dependencyId}' — stages are never skipped.";
            }
            if (package.LaborMinutesCommitted < package.LaborMinutesRequired)
                return $"ConstructionProjectExecutor.AdvancePackage: package '{package.PackageId}' has {package.LaborMinutesCommitted} of {package.LaborMinutesRequired} labor minutes — work is real.";
            package.Status = WorkPackageStatus.Complete;
            package.CompletedDayIndex = dayIndex;
            ReleaseEquipmentKeys(EquipmentReservations, project, package.PackageId, diag);
            if (project.Status == ConstructionProjectStatus.WorkPackagesCreated)
                project.Status = ConstructionProjectStatus.InProgress;
            diag.Add($"ConstructionProjectExecutor: package '{package.PackageId}' ('{package.PhaseName}') complete (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Pass VIII Worker 3: cancels the project. Staged (but not yet
        /// built-in) materials return to the source through the restock port
        /// — exactly once, all-or-nothing per package; the call is
        /// retryable and never double-returns. Unstaged site-stockpile units
        /// drain back the same way. Labor reservations and equipment holds
        /// release; open labor tasks are failed honestly through the
        /// TaskAuthority. Completed packages stay built (their materials are
        /// embodied in the building); a completed project cannot be
        /// cancelled — that would be demolition, not cancellation.
        /// </summary>
        public string CancelProject(
            ConstructionProject project,
            ConstructionSiteStockpile stockpile,
            IConstructionMaterialRestock restock,
            PersonScheduleTracker scheduleTracker,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null)
                return "ConstructionProjectExecutor.CancelProject: project required.";
            if (project.Status == ConstructionProjectStatus.Complete)
                return $"ConstructionProjectExecutor.CancelProject: project '{project.ProjectId}' is complete — cancellation is not demolition.";
            if (project.Status == ConstructionProjectStatus.Cancelled)
                return $"ConstructionProjectExecutor.CancelProject: project '{project.ProjectId}' is already cancelled.";

            // Collect staged-but-not-built-in lines. Completed packages keep
            // their materials (embodied in the building); returned packages
            // are skipped (never double-returned).
            var packagesToMark = new List<WorkPackage>();
            int stagedLineCount = 0;
            foreach (WorkPackage package in project.WorkPackages)
            {
                if (package == null) continue;
                if (package.Status == WorkPackageStatus.Complete) continue;
                if (package.StagedMaterialsReturned) continue;
                if (package.StagedMaterialLines.Count == 0) continue;
                packagesToMark.Add(package);
                stagedLineCount += package.StagedMaterialLines.Count;
            }
            // Drain the site stockpile exactly once — drained lines are real
            // units that must go back through the restock port below.
            List<ConsumedMaterialLine> drainedStockpile = stockpile != null
                ? stockpile.DrainAll($"project '{project.ProjectId}'", dayIndex)
                : new List<ConsumedMaterialLine>();
            if (stagedLineCount + drainedStockpile.Count > 0 && restock == null)
            {
                // The drain already happened — put the units back so the
                // refusal leaves the project exactly where it was.
                if (stockpile != null)
                {
                    foreach (ConsumedMaterialLine line in drainedStockpile)
                    {
                        if (line == null || line.Units <= 0) continue;
                        stockpile.Lines.Add(new ConstructionSiteStockpile.StockpileLine
                        {
                            DisplayName = line.DisplayName,
                            UnitLabel = line.UnitLabel,
                            Units = line.Units,
                            Provenance = line.Provenance,
                        });
                    }
                }
                return $"ConstructionProjectExecutor.CancelProject: {stagedLineCount + drainedStockpile.Count} staged line(s) must be accounted for, but no restock port was supplied — refusing rather than losing them.";
            }

            // All-or-nothing per package: a package is marked returned only
            // when every one of its lines went back. Retryable on refusal.
            foreach (WorkPackage package in packagesToMark)
            {
                bool allReturned = true;
                foreach (ConsumedMaterialLine line in package.StagedMaterialLines)
                {
                    if (line == null) continue;
                    string returnProblem = restock.TryReturn(line, $"project '{project.ProjectId}'", dayIndex, diag);
                    if (returnProblem != null)
                    {
                        diag.Add($"ConstructionProjectExecutor.CancelProject: restock refused for '{line.DisplayName}' x{line.Units}: {returnProblem} — package '{package.PackageId}' NOT marked returned; retry the cancellation.");
                        allReturned = false;
                        break;
                    }
                    diag.Add($"ConstructionProjectExecutor.CancelProject: returned '{line.DisplayName}' x{line.Units} to the source (package '{package.PackageId}').");
                }
                if (allReturned)
                    package.StagedMaterialsReturned = true;
                else
                    return $"ConstructionProjectExecutor.CancelProject: restock incomplete for package '{package.PackageId}' — nothing else touched; retry.";
            }
            foreach (ConsumedMaterialLine line in drainedStockpile)
            {
                if (line == null) continue;
                string returnProblem = restock.TryReturn(line, $"project '{project.ProjectId}'", dayIndex, diag);
                if (returnProblem != null)
                    return $"ConstructionProjectExecutor.CancelProject: restock refused for stockpile line '{line.DisplayName}' x{line.Units}: {returnProblem}.";
                diag.Add($"ConstructionProjectExecutor.CancelProject: returned stockpile '{line.DisplayName}' x{line.Units} to the source.");
            }

            // Release person-time.
            if (scheduleTracker != null)
            {
                foreach (string reservationId in project.LaborReservationIds)
                {
                    if (string.IsNullOrWhiteSpace(reservationId)) continue;
                    scheduleTracker.Release(reservationId);
                }
                project.LaborReservationIds.Clear();
            }
            // Fail open labor tasks honestly through the task authority.
            if (tasks != null)
            {
                foreach (LaborTaskBinding binding in project.LaborTaskBindings)
                {
                    if (binding == null || string.IsNullOrWhiteSpace(binding.TaskId)) continue;
                    WorkTask task = tasks.FindTask(binding.TaskId);
                    if (task != null && task.IsOpen)
                        tasks.CompleteTask(task.TaskId, TaskOutcome.SeriousFailure, dayIndex, budgets);
                }
            }
            // Release equipment.
            if (EquipmentReservations != null)
            {
                var remaining = new List<EquipmentHold>();
                foreach (EquipmentHold hold in project.EquipmentHolds)
                {
                    if (hold != null && !string.IsNullOrWhiteSpace(hold.ReservationKey))
                    {
                        EquipmentReservations.Release(hold.ReservationKey);
                        diag.Add($"ConstructionProjectExecutor.CancelProject: released equipment '{hold.ReservationKey}'.");
                    }
                    else if (hold != null)
                    {
                        remaining.Add(hold);
                    }
                }
                project.EquipmentHolds = remaining;
            }

            project.Status = ConstructionProjectStatus.Cancelled;
            diag.Add($"ConstructionProjectExecutor: project '{project.ProjectId}' CANCELLED (day {dayIndex}) — staged materials returned, reservations released.");
            return null;
        }

        /// <summary>
        /// Phase D: completes the project — all packages complete, then the
        /// building registers with the HousingAuthority and accommodation
        /// spaces are defined. No skipped stages, no faked completion.
        /// </summary>
        public string CompleteProject(
            ConstructionProject project,
            BuildingDesign design,
            HousingAuthority housing,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || design == null || housing == null)
                return "ConstructionProjectExecutor.CompleteProject: project, design and housing authority required.";
            if (project.Status == ConstructionProjectStatus.Complete)
                return $"ConstructionProjectExecutor.CompleteProject: project '{project.ProjectId}' is already complete.";
            foreach (WorkPackage package in project.WorkPackages)
            {
                if (package == null || package.Status != WorkPackageStatus.Complete)
                    return $"ConstructionProjectExecutor.CompleteProject: package '{package?.PackageId}' is not complete — no fake completions.";
            }
            if (project.WorkPackages.Count == 0)
                return $"ConstructionProjectExecutor.CompleteProject: project '{project.ProjectId}' has no work packages.";

            string provenance = $"constructed by H{project.HouseholdId} via project '{project.ProjectId}' " +
                $"(design '{design.DesignId}'): {project.MaterialsConsumed.Count} material line(s) from real lots, " +
                $"{project.TotalLaborMinutesCommitted} committed labor minutes.";
            string buildingId = $"bld-{project.ProjectId}";
            Building building = housing.RegisterBuilding(
                buildingId, project.ParcelId, design.KindLabel ?? "house", dayIndex, provenance, diag);
            if (building == null)
                return $"ConstructionProjectExecutor.CompleteProject: building registration failed — see diagnostics.";
            project.BuildingId = buildingId;

            foreach (BuildingDesignSpaceSpec spec in design.SpaceSpecs ?? new List<BuildingDesignSpaceSpec>())
            {
                if (spec == null) continue;
                housing.DefineSpace(buildingId, Math.Max(0, spec.SleepingCapacity), diag);
            }

            project.Status = ConstructionProjectStatus.Complete;
            project.CompletedDayIndex = dayIndex;
            diag.Add($"ConstructionProjectExecutor: project '{project.ProjectId}' complete — building '{buildingId}' " +
                $"({design.SpaceSpecs.Count} space(s)) on parcel '{project.ParcelId}'.");
            if (project.VisualPlacementPending)
                diag.Add($"ConstructionProjectExecutor: visual placement PENDING Codex handoff '{project.PrefabHandoffRecordId}' — data is real, visuals are not claimed.");
            return null;
        }

        /// <summary>
        /// Phase D: establishes residential occupancy in the completed home —
        /// the household that commissioned the build moves in.
        /// </summary>
        public string OccupyCompletedHome(
            ConstructionProject project,
            HousingAuthority housing,
            IReadOnlyList<int> memberPersonIds,
            AccommodationArrangement arrangement,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || housing == null)
                return "ConstructionProjectExecutor.OccupyCompletedHome: project and housing authority required.";
            if (project.Status != ConstructionProjectStatus.Complete || string.IsNullOrWhiteSpace(project.BuildingId))
                return $"ConstructionProjectExecutor.OccupyCompletedHome: project '{project?.ProjectId}' is not a completed building.";
            if (arrangement == AccommodationArrangement.Unspecified)
                return "ConstructionProjectExecutor.OccupyCompletedHome: the arrangement must be stated.";

            // Fill spaces in definition order up to their sleeping capacity.
            var remainingCapacity = new Dictionary<string, int>(StringComparer.Ordinal);
            var spaceOrder = new List<string>();
            foreach (AccommodationSpace space in housing.SpacesForBuilding(project.BuildingId))
            {
                remainingCapacity[space.SpaceId] = Math.Max(0, space.SleepingCapacity);
                spaceOrder.Add(space.SpaceId);
            }

            int placed = 0;
            if (memberPersonIds != null)
            {
                foreach (int personId in memberPersonIds)
                {
                    if (personId < 0) continue;
                    string chosen = null;
                    foreach (string spaceId in spaceOrder)
                    {
                        if (remainingCapacity[spaceId] > 0) { chosen = spaceId; break; }
                    }
                    if (chosen == null)
                    {
                        diag.Add($"ConstructionProjectExecutor: no remaining sleeping capacity in '{project.BuildingId}' for P{personId} — recorded as overcrowding risk, not blocked.");
                        chosen = spaceOrder.Count > 0 ? spaceOrder[0] : null;
                    }
                    if (chosen == null)
                        return $"ConstructionProjectExecutor.OccupyCompletedHome: building '{project.BuildingId}' has no accommodation spaces.";
                    housing.Occupy(personId, project.HouseholdId, chosen, arrangement, dayIndex, diag);
                    if (remainingCapacity.ContainsKey(chosen) && remainingCapacity[chosen] > 0)
                        remainingCapacity[chosen]--;
                    placed++;
                }
            }
            diag.Add($"ConstructionProjectExecutor: {placed} person(s) of H{project.HouseholdId} occupy '{project.BuildingId}' as {arrangement}.");
            return null;
        }

        /// <summary>
        /// Phase D: ties completed work to the contract's acceptance stages
        /// and the progress-billing milestones — the billing draws that the
        /// D4H service releases run on REAL accepted stages, never ahead of
        /// work.
        /// </summary>
        public string LinkCompletedPackagesToStages(
            ConstructionProject project,
            ConstructionContract contract,
            ConstructionProgressBillingService billing,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || contract == null || billing == null)
                return "ConstructionProjectExecutor.LinkCompletedPackagesToStages: project, contract and billing service required.";
            if (string.IsNullOrWhiteSpace(project.ContractId) || !string.Equals(project.ContractId, contract.ContractId, StringComparison.Ordinal))
                return $"ConstructionProjectExecutor.LinkCompletedPackagesToStages: project '{project.ProjectId}' is not bound to contract '{contract.ContractId}' — bind first.";

            var ordered = new List<WorkPackage>(project.WorkPackages);
            ordered.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            for (int i = 0; i < ordered.Count; i++)
            {
                WorkPackage package = ordered[i];
                if (package == null || package.Status != WorkPackageStatus.Complete) continue;
                if (contract.AcceptanceStages == null || i >= contract.AcceptanceStages.Count) continue;
                if (contract.PaymentTerms == null || contract.PaymentTerms.Milestones == null || i >= contract.PaymentTerms.Milestones.Count) continue;
                string stageId = contract.AcceptanceStages[i].StageId;
                string milestoneId = contract.PaymentTerms.Milestones[i].MilestoneId;
                string refusal = billing.RecordMilestoneStageLink(contract, milestoneId, stageId, dayIndex, diag);
                if (refusal != null)
                    return $"ConstructionProjectExecutor.LinkCompletedPackagesToStages: {refusal}";
                diag.Add($"ConstructionProjectExecutor: package '{package.PackageId}' ('{package.PhaseName}') linked to stage '{stageId}' / milestone '{milestoneId}'.");
            }
            return null;
        }

        /// <summary>Phase D: binds a written contract to the project (contract → project link).</summary>
        public string BindContract(ConstructionProject project, ConstructionContract contract, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null || contract == null)
                return "ConstructionProjectExecutor.BindContract: project and contract required.";
            if (!string.IsNullOrWhiteSpace(project.ContractId))
                return $"ConstructionProjectExecutor.BindContract: project '{project.ProjectId}' already bound to '{project.ContractId}'.";
            project.ContractId = contract.ContractId;
            diag.Add($"ConstructionProjectExecutor: project '{project.ProjectId}' bound to contract '{contract.ContractId}'.");
            return null;
        }

        private T Refuse<T>(List<string> diag, string message) where T : class
        {
            diag.Add($"ConstructionProjectExecutor: {message}");
            return null;
        }

        #region Save / Load
        [Serializable]
        public sealed class ConstructionProjectSaveDto
        {
            public List<ConstructionProject> Projects = new List<ConstructionProject>();
            /// <summary>
            /// Pass XI WS4 save/load fix (Gap 1): the executor's id sequence
            /// at capture. Additive; -1 means absent (an old save predating
            /// this field) — Load then recomputes from the restored ids.
            /// </summary>
            public int ExecutorSequence = -1;
        }

        public ConstructionProjectSaveDto CaptureSaveDto(List<ConstructionProject> projects)
        {
            var dto = new ConstructionProjectSaveDto();
            dto.ExecutorSequence = sequence;
            if (projects == null) return dto;
            foreach (ConstructionProject project in projects)
                if (project != null) dto.Projects.Add(project);
            return dto;
        }

        public List<ConstructionProject> LoadFromSaveDto(ConstructionProjectSaveDto dto, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var projects = new List<ConstructionProject>();
            if (dto?.Projects == null) return projects;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConstructionProject project in dto.Projects)
            {
                if (project == null || string.IsNullOrWhiteSpace(project.ProjectId)) continue;
                if (!seen.Add(project.ProjectId))
                {
                    diag.Add($"LoadFromSaveDto: duplicate project '{project.ProjectId}' skipped — no duplicated projects.");
                    continue;
                }
                var packageIds = new HashSet<string>(StringComparer.Ordinal);
                var deduped = new List<WorkPackage>();
                foreach (WorkPackage package in project.WorkPackages ?? new List<WorkPackage>())
                {
                    if (package == null || string.IsNullOrWhiteSpace(package.PackageId)) continue;
                    if (!packageIds.Add(package.PackageId))
                    {
                        diag.Add($"LoadFromSaveDto: duplicate package '{package.PackageId}' skipped.");
                        continue;
                    }
                    deduped.Add(package);
                }
                project.WorkPackages = deduped;
                projects.Add(project);
            }
            // Pass XI WS4 save/load fix (Gap 1): continue the id sequence
            // instead of restarting at 0 and re-minting an existing id.
            // The persisted sequence wins when it is ahead (e.g. the
            // captured list was a subset); the recomputed floor wins when
            // the field is absent (old save, -1) or behind the restored
            // ids, so an old save can never collide either.
            int maxSuffix = -1;
            foreach (ConstructionProject project in projects)
            {
                if (project == null) continue;
                maxSuffix = Math.Max(maxSuffix, TrailingNumber(project.ProjectId));
                foreach (WorkPackage package in project.WorkPackages ?? new List<WorkPackage>())
                    maxSuffix = Math.Max(maxSuffix, TrailingNumber(package.PackageId));
            }
            sequence = Math.Max(dto.ExecutorSequence, maxSuffix + 1);
            return projects;
        }

        /// <summary>Numeric suffix after the last '-' in an id, or -1 when there is none.</summary>
        private static int TrailingNumber(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int dash = id.LastIndexOf('-');
            if (dash < 0 || dash == id.Length - 1) return -1;
            int value;
            return int.TryParse(id.Substring(dash + 1), out value) ? value : -1;
        }
        #endregion
    }
}
