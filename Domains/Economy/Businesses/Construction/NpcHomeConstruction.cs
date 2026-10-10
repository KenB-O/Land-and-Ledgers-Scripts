using System;
using System.Collections.Generic;
using LandLedgers.Population;
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
                project.MaterialsConsumed.Add(new ConsumedMaterialLine
                {
                    DisplayName = requirement.MaterialDisplayName,
                    Units = requirement.RequiredUnits,
                    UnitLabel = requirement.UnitLabel ?? string.Empty,
                    Provenance = provenance ?? string.Empty,
                });
            }
            package.Status = WorkPackageStatus.MaterialsStaged;
            diag.Add($"ConstructionProjectExecutor: package '{package.PackageId}' materials staged ({package.Materials.Count} requirement(s), real lots).");
            return null;
        }

        /// <summary>
        /// Phase D: commits person-time to a package. The
        /// <see cref="PersonScheduleTracker"/> owns the simultaneity guard —
        /// a double-booked person is refused LOUDLY here, never silently
        /// overlapped. The commissioning household must hold the phase's
        /// required tools.
        /// </summary>
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
            diag = diag ?? diagnostics;
            if (project == null || package == null)
                return "ConstructionProjectExecutor.CommitLabor: project and package required.";
            if (scheduleTracker == null)
                return "ConstructionProjectExecutor.CommitLabor: no schedule tracker — person time is authoritative.";
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
            string refusal = scheduleTracker.TryReserve(
                personId, PersonActivityKind.Building, dayIndex, startMinuteOfDay, durationMinutes, reservationLabel);
            if (refusal != null)
            {
                diag.Add($"ConstructionProjectExecutor: labor refused — {refusal}");
                return $"ConstructionProjectExecutor.CommitLabor refused: {refusal}";
            }

            int committed = WorkTimeMath.ClampToWholeMinutes(durationMinutes);
            package.LaborMinutesCommitted += committed;
            project.TotalLaborMinutesCommitted += committed;
            package.Status = WorkPackageStatus.InProgress;

            foreach (PersonActivityReservation reservation in scheduleTracker.ActiveFor(personId, dayIndex))
            {
                if (reservation != null && !reservation.Released
                    && string.Equals(reservation.Label, reservationLabel, StringComparison.Ordinal))
                {
                    project.LaborReservationIds.Add(reservation.ReservationId);
                    break;
                }
            }
            diag.Add($"ConstructionProjectExecutor: P{personId} committed {committed} min to package '{package.PackageId}' (day {dayIndex}).");
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
            if (project.Status == ConstructionProjectStatus.WorkPackagesCreated)
                project.Status = ConstructionProjectStatus.InProgress;
            diag.Add($"ConstructionProjectExecutor: package '{package.PackageId}' ('{package.PhaseName}') complete (day {dayIndex}).");
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
        }

        public ConstructionProjectSaveDto CaptureSaveDto(List<ConstructionProject> projects)
        {
            var dto = new ConstructionProjectSaveDto();
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
            return projects;
        }
        #endregion
    }
}
