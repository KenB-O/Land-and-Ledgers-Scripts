using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Phase F: the supplier price list — the same prices the player sees.
    /// The runtime adapter implements this against the real yard/sawmill lot
    /// systems; tests fake it with a finite price list. Returns -1 when the
    /// supplier names no price: costs are never guessed.
    /// </summary>
    public interface IConstructionMaterialPricePort
    {
        int UnitPriceCents(ConstructionMaterialRequirement requirement);
    }

    /// <summary>Phase F: who does the work on a commissioned package. Append-only.</summary>
    public enum NpcBuildLaborKind
    {
        Unspecified = 0,
        OwnLabor = 1,
        HiredWorker = 2,
        BuilderContract = 3,
    }

    /// <summary>Phase F: how the NPC answers a cost overrun. Append-only.</summary>
    public enum NpcBuildOverrunResolution
    {
        Unspecified = 0,
        InjectCash = 1,
        PauseProject = 2,
        ChangeOrder = 3,
    }

    /// <summary>Phase F: funding + labor record for one work package — the NPC principal's books.</summary>
    [Serializable]
    public sealed class NpcBuildPackageFunding
    {
        public string PackageId = string.Empty;
        public string PhaseName = string.Empty;
        public int EstimatedMaterialCostCents;
        public int ActualMaterialCostCents;
        public NpcBuildLaborKind LaborKind = NpcBuildLaborKind.Unspecified;
        public List<int> WorkerPersonIds = new List<int>();
        public List<string> EmploymentIds = new List<string>();
        public int WorkerMinutesCommitted;
        public int WagesPaidCents;
        public string Notes = string.Empty;

        public NpcBuildPackageFunding() { }
    }

    /// <summary>
    /// Phase F: a cost overrun the NPC discovered and how they answered it.
    /// Overruns are never silently absorbed — the record shows the estimate,
    /// the actual, and the resolution (or that it is still open, blocking
    /// further funding).
    /// </summary>
    [Serializable]
    public sealed class NpcBuildOverrunEvent
    {
        public int DayIndex;
        public string PackageId = string.Empty;
        public int EstimatedCents;
        public int ActualCents;
        public NpcBuildOverrunResolution Resolution = NpcBuildOverrunResolution.Unspecified;
        public string ResolutionNotes = string.Empty;
        public bool Resolved;

        public NpcBuildOverrunEvent() { }
    }

    /// <summary>
    /// Phase F: the NPC principal's record for one commissioned project —
    /// what THEY decided (design, funding, labor, overrun answers), distinct
    /// from the executor's physical record. §26 observability.
    /// </summary>
    [Serializable]
    public sealed class NpcConstructionRecord
    {
        public string RecordId = string.Empty;
        public string ProjectId = string.Empty;
        public int HouseholdId = -1;
        public string DesignId = string.Empty;
        public string ParcelId = string.Empty;
        public string ContractId = string.Empty;
        public int TotalEstimatedCostCents;
        public int TotalActualCostCents;
        public List<NpcBuildPackageFunding> PackageFundings = new List<NpcBuildPackageFunding>();
        public List<NpcBuildOverrunEvent> Overruns = new List<NpcBuildOverrunEvent>();
        /// <summary>Plain-language decisions the NPC made, in order.</summary>
        public List<string> Decisions = new List<string>();

        public NpcConstructionRecord() { }

        public NpcBuildPackageFunding FindFunding(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;
            foreach (NpcBuildPackageFunding funding in PackageFundings)
                if (funding != null && string.Equals(funding.PackageId, packageId, StringComparison.Ordinal))
                    return funding;
            return null;
        }

        public bool HasOpenOverrun()
        {
            foreach (NpcBuildOverrunEvent overrun in Overruns)
                if (overrun != null && !overrun.Resolved) return true;
            return false;
        }
    }

    /// <summary>
    /// Phase F (F3): the NPC as construction principal. The household
    /// COMMISSIONS the build — it selects the design from catalog data,
    /// funds material staging from real lots with real cash (its own or
    /// financed), commits labor (its own people, hired workers through the
    /// employment authorities with person-time respected, or a hired builder
    /// business under a real written contract), and answers cost overruns
    /// and delays explicitly. The <see cref="ConstructionProjectExecutor"/>
    /// does the physical staging; this class does the principal's decisions
    /// and books.
    /// </summary>
    public sealed class NpcConstructionCommissioner
    {
        /// <summary>
        /// TUNING: an overrun is raised when actual staging cost exceeds the
        /// estimate by more than this share. Calibration, not doctrine.
        /// </summary>
        public double OverrunTolerance01 { get; set; } = 0.10;

        /// <summary>
        /// TUNING: minutes in the standard work week used to pro-rate hired
        /// wages from an agreed weekly wage (5 days x 8 hours).
        /// </summary>
        public int StandardWorkWeekMinutes { get; set; } = 2400;

        private readonly List<string> diagnostics = new List<string>();
        private readonly Dictionary<string, NpcConstructionRecord> records =
            new Dictionary<string, NpcConstructionRecord>(StringComparer.Ordinal);
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        #region Design selection and commissioning

        /// <summary>
        /// The NPC selects a catalog design: it must exist, sleep the
        /// household, have a computable cost from the price list, and fit
        /// the funds the NPC actually holds. Returns null + refusal on any
        /// failure — the NPC never builds a design it cannot price or fund.
        /// </summary>
        public BuildingDesign SelectDesign(
            BuildingDesignCatalog catalog,
            string designId,
            int requiredSleepingPlaces,
            IConstructionMaterialPricePort pricePort,
            int fundsAvailableCents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (catalog == null) { diag.Add("NpcCommissioner.SelectDesign: no design catalog."); return null; }
            BuildingDesign design = catalog.FindDesign(designId);
            if (design == null)
            {
                diag.Add($"NpcCommissioner.SelectDesign: unknown design '{designId}' — the NPC builds catalog designs only.");
                return null;
            }
            int capacity = 0;
            foreach (BuildingDesignSpaceSpec spec in design.SpaceSpecs ?? new List<BuildingDesignSpaceSpec>())
                capacity += Math.Max(0, spec != null ? spec.SleepingCapacity : 0);
            if (capacity < Math.Max(1, requiredSleepingPlaces))
            {
                diag.Add($"NpcCommissioner.SelectDesign: design '{designId}' sleeps {capacity}, the household needs {requiredSleepingPlaces}.");
                return null;
            }
            int estimate = World.Property.NpcPropertyDecisionEngine.EstimateBuildCostCents(design, pricePort);
            if (estimate < 0)
            {
                diag.Add($"NpcCommissioner.SelectDesign: design '{designId}' has unpriced materials — cost unknowable, refused.");
                return null;
            }
            if (estimate > fundsAvailableCents)
            {
                diag.Add($"NpcCommissioner.SelectDesign: design '{designId}' costs ~{estimate}c against {fundsAvailableCents}c available — the NPC cannot fund it.");
                return null;
            }
            diag.Add($"NpcCommissioner.SelectDesign: H design '{designId}' selected (~{estimate}c materials, sleeps {capacity}).");
            return design;
        }

        /// <summary>
        /// Commissions the project: creates it through the executor, builds
        /// the work packages, and opens the principal's record with
        /// per-package estimates. The record is the NPC's books.
        /// </summary>
        public ConstructionProject CommissionProject(
            ConstructionProjectExecutor executor,
            BuildingDesignCatalog catalog,
            int householdId,
            string designId,
            string parcelId,
            IConstructionMaterialPricePort pricePort,
            int dayIndex,
            List<string> diag,
            out NpcConstructionRecord record)
        {
            diag = diag ?? diagnostics;
            record = null;
            if (executor == null || catalog == null)
            {
                diag.Add("NpcCommissioner.CommissionProject: executor and catalog required.");
                return null;
            }
            BuildingDesign design = catalog.FindDesign(designId);
            if (design == null)
            {
                diag.Add($"NpcCommissioner.CommissionProject: unknown design '{designId}'.");
                return null;
            }
            ConstructionProject project = executor.CreateProject(catalog, householdId, designId, parcelId, dayIndex, diag);
            if (project == null) return null;
            string packageProblem = executor.CreateWorkPackages(project, design, dayIndex, diag);
            if (packageProblem != null)
            {
                diag.Add("NpcCommissioner.CommissionProject: " + packageProblem);
                return null;
            }
            record = new NpcConstructionRecord
            {
                RecordId = $"npcb-{sequence++}",
                ProjectId = project.ProjectId,
                HouseholdId = householdId,
                DesignId = designId,
                ParcelId = parcelId,
            };
            foreach (WorkPackage package in project.WorkPackages)
            {
                if (package == null) continue;
                int estimate = EstimatePackageCostCents(package, pricePort);
                var funding = new NpcBuildPackageFunding
                {
                    PackageId = package.PackageId,
                    PhaseName = package.PhaseName,
                    EstimatedMaterialCostCents = Math.Max(0, estimate),
                    Notes = estimate < 0 ? "unpriced materials — staging will refuse until priced" : string.Empty,
                };
                record.PackageFundings.Add(funding);
                record.TotalEstimatedCostCents += Math.Max(0, estimate);
            }
            record.Decisions.Add($"day {dayIndex}: commissioned design '{designId}' on parcel '{parcelId}' " +
                $"(~{record.TotalEstimatedCostCents}c materials estimated, {project.WorkPackages.Count} package(s)).");
            records[record.RecordId] = record;
            diag.Add($"NpcCommissioner: H{householdId} commissioned project '{project.ProjectId}' ('{designId}') — record '{record.RecordId}'.");
            return project;
        }

        private static int EstimatePackageCostCents(WorkPackage package, IConstructionMaterialPricePort pricePort)
        {
            if (package == null || pricePort == null) return -1;
            int total = 0;
            foreach (ConstructionMaterialRequirement requirement in package.Materials ?? new List<ConstructionMaterialRequirement>())
            {
                if (requirement == null || !requirement.IsCoherent) return -1;
                int unitPrice = pricePort.UnitPriceCents(requirement);
                if (unitPrice < 0) return -1;
                total += unitPrice * Math.Max(0, requirement.RequiredUnits);
            }
            return total;
        }

        #endregion

        #region Self-managed build: funding, labor, overruns

        /// <summary>
        /// Funds and stages one package: prices every requirement from the
        /// real price list, checks the NPC's real cash, stages real lots
        /// through the executor, then pays the supplier in real cash. When
        /// the actual cost overruns the estimate beyond tolerance, the
        /// package is NOT staged — an overrun event is recorded and funding
        /// blocks until the NPC resolves it (inject cash, pause, change
        /// order). Never silently absorbed.
        /// </summary>
        public string FundAndStagePackage(
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            NpcConstructionRecord record,
            WorkPackage package,
            IConstructionMaterialSource materialSource,
            IConstructionMaterialPricePort pricePort,
            CreditCashBridge cash,
            string buyerOwnerName,
            string defaultSupplierOwnerName,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (executor == null || project == null || record == null || package == null)
                return "NpcCommissioner.FundAndStagePackage: executor, project, record and package required.";
            if (cash == null)
                return "NpcCommissioner.FundAndStagePackage: no cash bridge — materials are bought with real cash only.";
            if (project.Status == ConstructionProjectStatus.Paused)
                return $"NpcCommissioner.FundAndStagePackage: project '{project.ProjectId}' is paused — resolve or resume first.";
            if (record.HasOpenOverrun())
                return $"NpcCommissioner.FundAndStagePackage: record '{record.RecordId}' has an unresolved overrun — the NPC answers it before funding more work.";
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            if (funding == null)
                return $"NpcCommissioner.FundAndStagePackage: no funding line for package '{package.PackageId}'.";

            // Price every requirement — unpriced means unknowable, never guessed.
            int actual = 0;
            var supplierOf = new Dictionary<ConstructionMaterialRequirement, string>();
            foreach (ConstructionMaterialRequirement requirement in package.Materials ?? new List<ConstructionMaterialRequirement>())
            {
                if (requirement == null || !requirement.IsCoherent)
                    return $"NpcCommissioner.FundAndStagePackage: package '{package.PackageId}' has an incoherent material requirement — refusing.";
                int unitPrice = pricePort != null ? pricePort.UnitPriceCents(requirement) : -1;
                if (unitPrice < 0)
                    return $"NpcCommissioner.FundAndStagePackage: '{requirement.MaterialDisplayName}' is unpriced — cost unknowable, refusing.";
                actual += unitPrice * Math.Max(0, requirement.RequiredUnits);
                supplierOf[requirement] = !string.IsNullOrWhiteSpace(requirement.SupplierBusinessId)
                    ? "business:" + requirement.SupplierBusinessId
                    : (defaultSupplierOwnerName ?? string.Empty);
            }

            // Overrun check BEFORE anything moves.
            int tolerance = (int)Math.Ceiling(funding.EstimatedMaterialCostCents * Math.Max(0.0, OverrunTolerance01));
            if (actual > funding.EstimatedMaterialCostCents + tolerance)
            {
                var overrun = new NpcBuildOverrunEvent
                {
                    DayIndex = dayIndex,
                    PackageId = package.PackageId,
                    EstimatedCents = funding.EstimatedMaterialCostCents,
                    ActualCents = actual,
                };
                record.Overruns.Add(overrun);
                record.Decisions.Add($"day {dayIndex}: OVERRUN on '{package.PhaseName}' — estimated {overrun.EstimatedCents}c, actual {overrun.ActualCents}c. Funding blocked until resolved.");
                diag.Add($"NpcCommissioner: OVERRUN on package '{package.PackageId}' ('{package.PhaseName}'): " +
                    $"estimated {overrun.EstimatedCents}c, actual {overrun.ActualCents}c — nothing staged, nothing paid.");
                return $"cost overrun on '{package.PhaseName}': estimated {overrun.EstimatedCents}c, actual {overrun.ActualCents}c — resolve it (inject cash, pause, change order) before funding.";
            }

            IRealCashStore buyerStore = cash.FindStore(buyerOwnerName);
            if (buyerStore == null)
                return $"NpcCommissioner.FundAndStagePackage: '{buyerOwnerName}' has no real cash store.";
            if (buyerStore.ReadBalanceCents() < actual)
                return $"NpcCommissioner.FundAndStagePackage: '{buyerOwnerName}' holds {buyerStore.ReadBalanceCents()}c but staging costs {actual}c — refusing rather than spending what they lack.";

            string stageProblem = executor.StagePackageMaterials(project, package, materialSource, dayIndex, diag);
            if (stageProblem != null) return stageProblem;

            // Pay each supplier in real cash. If payment fails after lots
            // were consumed, the debt is booked as a REAL payable to the
            // supplier — never vanished, never faked as paid.
            foreach (ConstructionMaterialRequirement requirement in package.Materials ?? new List<ConstructionMaterialRequirement>())
            {
                int unitPrice = pricePort.UnitPriceCents(requirement);
                int lineCost = unitPrice * Math.Max(0, requirement.RequiredUnits);
                if (lineCost <= 0) continue;
                string supplier = supplierOf[requirement];
                if (string.IsNullOrWhiteSpace(supplier))
                {
                    diag.Add($"NpcCommissioner: '{requirement.MaterialDisplayName}' names no supplier — {lineCost}c of consumed materials has no payee; booking as unassigned payable is refused, staging rolled back is impossible — FAILING LOUDLY.");
                    return $"NpcCommissioner.FundAndStagePackage: '{requirement.MaterialDisplayName}' names no supplier to pay {lineCost}c to — refusing.";
                }
                string payProblem = MoveCash(cash, buyerOwnerName, supplier, lineCost, dayIndex,
                    $"materials for project '{project.ProjectId}' package '{package.PackageId}' ({requirement.MaterialDisplayName})", diag);
                if (payProblem != null)
                {
                    if (obligations != null && ids != null)
                    {
                        FinancialObligation payable = obligations.Create(ids, FinancialObligationKind.Payable,
                            buyerOwnerName, supplier, lineCost, dayIndex,
                            $"materials consumed but cash payment failed: {payProblem}",
                            $"materials: {requirement.MaterialDisplayName} x{requirement.RequiredUnits}");
                        diag.Add($"NpcCommissioner: {lineCost}c to '{supplier}' booked as REAL payable " +
                            $"'{(payable != null ? payable.ObligationId : "FAILED")}' — the debt stands, nothing faked as paid.");
                    }
                    else
                    {
                        diag.Add($"NpcCommissioner: INCONSISTENCY — {lineCost}c of materials consumed for '{supplier}' but payment failed and no obligation authority was supplied to book the debt: {payProblem}");
                        return $"NpcCommissioner.FundAndStagePackage: payment failed ({payProblem}) and no obligation authority to book the debt — failing loudly.";
                    }
                }
            }

            funding.ActualMaterialCostCents = actual;
            record.TotalActualCostCents += actual;
            record.Decisions.Add($"day {dayIndex}: funded '{package.PhaseName}' — {actual}c materials staged from real lots and paid.");
            diag.Add($"NpcCommissioner: package '{package.PackageId}' ('{package.PhaseName}') funded and staged — {actual}c paid from real cash.");
            return null;
        }

        /// <summary>
        /// The NPC's own people work the package — through the executor's
        /// CommitLabor, so person-time is reserved and double-booking is
        /// refused loudly. Recorded as own labor on the funding line.
        /// </summary>
        public string CommitOwnLabor(
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            NpcConstructionRecord record,
            WorkPackage package,
            int personId,
            PersonScheduleTracker tracker,
            IHouseholdToolCustody custody,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (executor == null || project == null || record == null || package == null)
                return "NpcCommissioner.CommitOwnLabor: executor, project, record and package required.";
            string problem = executor.CommitLabor(project, package, personId, tracker, custody,
                dayIndex, startMinuteOfDay, durationMinutes, "own labor", diag);
            if (problem != null) return problem;
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            if (funding != null)
            {
                funding.LaborKind = NpcBuildLaborKind.OwnLabor;
                if (!funding.WorkerPersonIds.Contains(personId)) funding.WorkerPersonIds.Add(personId);
                funding.WorkerMinutesCommitted += WorkTimeMath.ClampToWholeMinutes(durationMinutes);
            }
            record.Decisions.Add($"day {dayIndex}: P{personId} (own labor) committed {durationMinutes} min to '{package.PhaseName}'.");
            return null;
        }

        /// <summary>
        /// Hires a worker: registers the REAL employment relationship (agreed
        /// weekly wage lives there, per the employment authority), then
        /// commits their time through the executor so the schedule tracker
        /// guards against double-booking. Wages are paid on package
        /// completion via <see cref="PayWorkerForPackage"/>.
        /// </summary>
        public string HireWorker(
            EmploymentRelationshipRegistry employments,
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            NpcConstructionRecord record,
            WorkPackage package,
            int workerPersonId,
            int workerHouseholdId,
            string employerLabel,
            int agreedWeeklyWageCents,
            PersonScheduleTracker tracker,
            IHouseholdToolCustody custody,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (employments == null || executor == null || project == null || record == null || package == null)
                return "NpcCommissioner.HireWorker: employments, executor, project, record and package required.";
            var relationship = new EmploymentRelationship
            {
                Id = $"emp-{project.ProjectId}-{package.PackageId}-{workerPersonId}-{dayIndex}",
                EmployeePersonId = workerPersonId,
                EmployerBusinessId = employerLabel ?? $"household:{project.HouseholdId} construction",
                RoleDisplayName = "construction laborer",
                Kind = EmploymentKind.SeasonalOrCasual,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(Math.Max(0, agreedWeeklyWageCents),
                    $"hired for project '{project.ProjectId}' package '{package.PackageId}'"),
                StartDayIndex = dayIndex,
                Source = EmploymentSource.Manual,
            };
            string regProblem = employments.Register(relationship);
            if (regProblem != null) return "NpcCommissioner.HireWorker: " + regProblem;

            string commitProblem = executor.CommitLabor(project, package, workerPersonId, tracker, custody,
                dayIndex, startMinuteOfDay, durationMinutes, $"hired labor {relationship.Id}", diag);
            if (commitProblem != null)
            {
                diag.Add($"NpcCommissioner.HireWorker: P{workerPersonId} is hired (employment '{relationship.Id}') but their time was refused: {commitProblem}");
                return commitProblem;
            }
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            if (funding != null)
            {
                funding.LaborKind = NpcBuildLaborKind.HiredWorker;
                if (!funding.WorkerPersonIds.Contains(workerPersonId)) funding.WorkerPersonIds.Add(workerPersonId);
                if (!funding.EmploymentIds.Contains(relationship.Id)) funding.EmploymentIds.Add(relationship.Id);
                funding.WorkerMinutesCommitted += WorkTimeMath.ClampToWholeMinutes(durationMinutes);
                funding.Notes = $"wages to household:{workerHouseholdId} at {agreedWeeklyWageCents}c/week pro-rated";
            }
            record.Decisions.Add($"day {dayIndex}: hired P{workerPersonId} (employment '{relationship.Id}', {agreedWeeklyWageCents}c/week) for '{package.PhaseName}'.");
            diag.Add($"NpcCommissioner: P{workerPersonId} hired for '{package.PackageId}' ({agreedWeeklyWageCents}c/week, employment '{relationship.Id}').");
            return null;
        }

        /// <summary>
        /// Pays hired workers for a completed package from the NPC's real
        /// cash: pro-rated from the agreed weekly wage by committed minutes.
        /// </summary>
        public string PayWorkerForPackage(
            EmploymentRelationshipRegistry employments,
            NpcConstructionRecord record,
            WorkPackage package,
            CreditCashBridge cash,
            string payerOwnerName,
            Dictionary<int, int> workerHouseholdByPerson,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (record == null || package == null)
                return "NpcCommissioner.PayWorkerForPackage: record and package required.";
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            if (funding == null) return $"NpcCommissioner.PayWorkerForPackage: no funding line for '{package.PackageId}'.";
            if (funding.LaborKind != NpcBuildLaborKind.HiredWorker || funding.EmploymentIds.Count == 0)
                return null; // nothing hired — nothing to pay.

            int week = Math.Max(1, StandardWorkWeekMinutes);
            foreach (string employmentId in funding.EmploymentIds)
            {
                if (!employments.TryGetById(employmentId, out EmploymentRelationship relationship) || relationship == null)
                    return $"NpcCommissioner.PayWorkerForPackage: employment '{employmentId}' unknown — wages cannot be computed, refusing.";
                int weekly = Math.Max(0, relationship.Compensation.AgreedWeeklyWageCents);
                int minutes = Math.Max(0, funding.WorkerMinutesCommitted);
                int wage = (int)Math.Ceiling(weekly * (minutes / (double)week));
                if (wage <= 0) continue;
                int householdId = -1;
                if (workerHouseholdByPerson != null) workerHouseholdByPerson.TryGetValue(relationship.EmployeePersonId, out householdId);
                if (householdId < 0)
                    return $"NpcCommissioner.PayWorkerForPackage: P{relationship.EmployeePersonId} has no household to pay.";
                string payProblem = MoveCash(cash, payerOwnerName, "household:" + householdId, wage, dayIndex,
                    $"wages for '{package.PhaseName}' ({minutes} min at {weekly}c/week, employment '{employmentId}')", diag);
                if (payProblem != null) return "NpcCommissioner.PayWorkerForPackage: " + payProblem;
                funding.WagesPaidCents += wage;
                record.TotalActualCostCents += wage;
                record.Decisions.Add($"day {dayIndex}: paid {wage}c wages to H{householdId} for '{package.PhaseName}'.");
            }
            return null;
        }

        /// <summary>
        /// Answers an open overrun. InjectCash commits the extra from real
        /// cash (verified); PauseProject halts funding until resumed;
        /// ChangeOrder files a real contract change order (builder path).
        /// </summary>
        public string ResolveOverrun(
            NpcConstructionRecord record,
            ConstructionProject project,
            int overrunIndex,
            NpcBuildOverrunResolution resolution,
            string notes,
            CreditCashBridge cash,
            string buyerOwnerName,
            ConstructionContract contract,
            ConstructionContractSide changeOrderSide,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (record == null || project == null)
                return "NpcCommissioner.ResolveOverrun: record and project required.";
            if (overrunIndex < 0 || overrunIndex >= record.Overruns.Count)
                return "NpcCommissioner.ResolveOverrun: unknown overrun.";
            NpcBuildOverrunEvent overrun = record.Overruns[overrunIndex];
            if (overrun.Resolved)
                return $"NpcCommissioner.ResolveOverrun: overrun on '{overrun.PackageId}' is already resolved.";
            int extra = Math.Max(0, overrun.ActualCents - overrun.EstimatedCents);

            if (resolution == NpcBuildOverrunResolution.InjectCash)
            {
                IRealCashStore buyerStore = cash != null ? cash.FindStore(buyerOwnerName) : null;
                if (buyerStore == null)
                    return $"NpcCommissioner.ResolveOverrun: '{buyerOwnerName}' has no real cash store — cash cannot be injected from thin air.";
                if (buyerStore.ReadBalanceCents() < extra)
                    return $"NpcCommissioner.ResolveOverrun: injecting {extra}c needs {extra}c cash; '{buyerOwnerName}' holds {buyerStore.ReadBalanceCents()}c.";
                NpcBuildPackageFunding funding = record.FindFunding(overrun.PackageId);
                if (funding != null) funding.EstimatedMaterialCostCents = overrun.ActualCents;
                record.TotalEstimatedCostCents += extra;
                overrun.Resolution = resolution;
                overrun.ResolutionNotes = notes ?? $"injected {extra}c from real cash";
                overrun.Resolved = true;
                record.Decisions.Add($"day {dayIndex}: overrun on '{overrun.PackageId}' answered by INJECTING {extra}c cash. {overrun.ResolutionNotes}");
                diag.Add($"NpcCommissioner: overrun on '{overrun.PackageId}' resolved — {extra}c injected from real cash.");
                return null;
            }
            if (resolution == NpcBuildOverrunResolution.PauseProject)
            {
                project.Status = ConstructionProjectStatus.Paused;
                overrun.Resolution = resolution;
                overrun.ResolutionNotes = notes ?? "project paused pending funds";
                overrun.Resolved = true;
                record.Decisions.Add($"day {dayIndex}: overrun on '{overrun.PackageId}' answered by PAUSING the project. {overrun.ResolutionNotes}");
                diag.Add($"NpcCommissioner: project '{project.ProjectId}' PAUSED over an unresolved {extra}c overrun.");
                return null;
            }
            if (resolution == NpcBuildOverrunResolution.ChangeOrder)
            {
                if (contract == null)
                    return "NpcCommissioner.ResolveOverrun: a change order needs the builder contract.";
                string orderId = contract.ProposeChangeOrder(
                    $"overrun on '{overrun.PackageId}': estimated {overrun.EstimatedCents}c, actual {overrun.ActualCents}c. {notes}",
                    extra, 0, changeOrderSide, dayIndex, diag);
                if (string.IsNullOrWhiteSpace(orderId) || orderId.StartsWith("ConstructionContract:", StringComparison.Ordinal))
                    return "NpcCommissioner.ResolveOverrun: " + orderId;
                overrun.Resolution = resolution;
                overrun.ResolutionNotes = $"change order '{orderId}' proposed ({notes})";
                overrun.Resolved = true;
                record.Decisions.Add($"day {dayIndex}: overrun on '{overrun.PackageId}' answered by CHANGE ORDER '{orderId}' (+{extra}c). {notes}");
                diag.Add($"NpcCommissioner: overrun on '{overrun.PackageId}' → change order '{orderId}' on contract '{contract.ContractId}'.");
                return null;
            }
            return $"NpcCommissioner.ResolveOverrun: unknown resolution {resolution}.";
        }

        /// <summary>Resumes a paused project — an explicit NPC decision, recorded.</summary>
        public string ResumeProject(ConstructionProject project, NpcConstructionRecord record,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (project == null) return "NpcCommissioner.ResumeProject: project required.";
            if (project.Status != ConstructionProjectStatus.Paused)
                return $"NpcCommissioner.ResumeProject: project '{project.ProjectId}' is {project.Status}, not paused.";
            if (record != null && record.HasOpenOverrun())
                return $"NpcCommissioner.ResumeProject: record '{record.RecordId}' still has an open overrun — resolve it first.";
            project.Status = project.WorkPackages.Count > 0
                ? ConstructionProjectStatus.WorkPackagesCreated
                : ConstructionProjectStatus.DesignSelected;
            bool anyComplete = false, anyStarted = false;
            foreach (WorkPackage package in project.WorkPackages)
            {
                if (package == null) continue;
                if (package.Status == WorkPackageStatus.Complete) anyComplete = true;
                else if (package.Status == WorkPackageStatus.InProgress || package.Status == WorkPackageStatus.MaterialsStaged) anyStarted = true;
            }
            if (anyComplete || anyStarted) project.Status = ConstructionProjectStatus.InProgress;
            record?.Decisions.Add($"day {dayIndex}: project resumed.");
            diag.Add($"NpcCommissioner: project '{project.ProjectId}' resumed.");
            return null;
        }

        private static string MoveCash(CreditCashBridge cash, string payer, string payee, int amountCents,
            int dayIndex, string purpose, List<string> diag)
        {
            if (amountCents <= 0) return null;
            CreditCashAccount payerCash = cash.OpenWindow(payer, diag);
            CreditCashAccount payeeCash = cash.OpenWindow(payee, diag);
            if (payerCash == null || payeeCash == null)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"no real cash store for '{payer}' or '{payee}'.";
            }
            if (payerCash.BalanceCents < amountCents)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"'{payer}' holds {payerCash.BalanceCents}c, needs {amountCents}c.";
            }
            payerCash.BalanceCents -= amountCents;
            payeeCash.BalanceCents += amountCents;
            string p1 = cash.CommitWindow(payerCash, dayIndex, purpose, payee, diag);
            string p2 = cash.CommitWindow(payeeCash, dayIndex, purpose, payer, diag);
            return p1 ?? p2;
        }

        #endregion

        #region Hired-builder path

        /// <summary>
        /// The NPC hires a Builder business: a real bid request, a real bid,
        /// a real written contract (Executed), bound to the project. The
        /// builder's physical execution stays the builder's runtime job —
        /// this path never fakes their work; the NPC side owns the contract,
        /// staged acceptance, and gated billing.
        /// </summary>
        /// <param name="capacity">The builder's REAL crew reading (from their
        /// runtime) — capacity is never invented.</param>
        public ConstructionContract CommissionBuilderContract(
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            NpcConstructionRecord record,
            ConstructionContractBook contractBook,
            ConstructionContractCapacityReading capacity,
            int ownerPersonId,
            string ownerDisplayName,
            string builderBusinessId,
            string builderDisplayName,
            string worksDescription,
            int agreedPriceCents,
            ConstructionPaymentTerms paymentTerms,
            List<string> stageNames,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (executor == null || project == null || contractBook == null)
            {
                diag.Add("NpcCommissioner.CommissionBuilderContract: executor, project and contract book required.");
                return null;
            }
            var request = contractBook.OpenBidRequest(
                ConstructionContractParty.Person(ownerPersonId, ownerDisplayName ?? $"person:{ownerPersonId}"),
                ConstructionWorksKind.NewBuilding,
                worksDescription ?? $"build '{project.DesignId}' on parcel '{project.ParcelId}'",
                $"design '{project.DesignId}', parcel '{project.ParcelId}'",
                Math.Max(0, agreedPriceCents),
                dayIndex + 14,
                new List<string> { builderBusinessId },
                dayIndex, diag);
            if (request == null) return null;

            ConstructionBid bid = contractBook.SubmitBid(request.RequestId, builderBusinessId,
                builderDisplayName ?? builderBusinessId, ConstructionContractPriceBasis.LumpSum,
                Math.Max(0, agreedPriceCents), dayIndex, 60,
                paymentTerms ?? new ConstructionPaymentTerms(),
                new List<ConstructionBidEstimateLine>(), dayIndex, diag);
            if (bid == null) return null;

            ConstructionContract contract = contractBook.AcceptBid(bid.BidId, capacity,
                stageNames ?? new List<string>(), dayIndex, diag);
            if (contract == null) return null;

            string bindProblem = executor.BindContract(project, contract, diag);
            if (bindProblem != null)
            {
                diag.Add("NpcCommissioner.CommissionBuilderContract: " + bindProblem);
                return null;
            }
            if (record != null)
            {
                record.ContractId = contract.ContractId;
                record.Decisions.Add($"day {dayIndex}: hired builder '{builderBusinessId}' under contract '{contract.ContractId}' ({agreedPriceCents}c lump sum).");
            }
            diag.Add($"NpcCommissioner: H{project.HouseholdId} hired builder '{builderBusinessId}' — contract '{contract.ContractId}' bound to project '{project.ProjectId}'.");
            return contract;
        }

        /// <summary>
        /// The NPC accepts a builder-completed stage and releases the linked
        /// milestone draw. Draws run ONLY against accepted stages — the
        /// billing service refuses unlinked or unaccepted stages, and that
        /// refusal is surfaced, never bypassed.
        /// </summary>
        public string AcceptBuilderStageAndReleaseDraw(
            ConstructionContract contract,
            ConstructionProgressBillingService billing,
            string stageId,
            string milestoneId,
            string acceptedBy,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (contract == null || billing == null)
                return "NpcCommissioner.AcceptBuilderStageAndReleaseDraw: contract and billing service required.";
            string acceptProblem = contract.AcceptStage(stageId, dayIndex, acceptedBy ?? "owner", diag);
            if (acceptProblem != null) return acceptProblem;
            string linkProblem = billing.RecordMilestoneStageLink(contract, milestoneId, stageId, dayIndex, diag);
            if (linkProblem != null) return "NpcCommissioner: " + linkProblem;
            string drawProblem = billing.RecordMilestone(contract, milestoneId, dayIndex, diag);
            if (drawProblem != null) return "NpcCommissioner: " + drawProblem;
            diag.Add($"NpcCommissioner: stage '{stageId}' accepted; milestone '{milestoneId}' draw released to the builder.");
            return null;
        }

        #endregion

        #region Save / Load

        [Serializable]
        public sealed class NpcConstructionCommissionerSaveDto
        {
            public List<NpcConstructionRecord> Records = new List<NpcConstructionRecord>();
        }

        public NpcConstructionCommissionerSaveDto CaptureSaveDto()
        {
            var dto = new NpcConstructionCommissionerSaveDto();
            foreach (NpcConstructionRecord record in records.Values)
                if (record != null) dto.Records.Add(record);
            return dto;
        }

        public void LoadFromSaveDto(NpcConstructionCommissionerSaveDto dto, List<string> diag)
        {
            diag = diag ?? diagnostics;
            records.Clear();
            if (dto?.Records == null) return;
            foreach (NpcConstructionRecord record in dto.Records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.RecordId)) continue;
                if (records.ContainsKey(record.RecordId))
                {
                    diag.Add($"LoadFromSaveDto: duplicate construction record '{record.RecordId}' skipped — no duplicated records.");
                    continue;
                }
                records[record.RecordId] = record;
            }
        }

        public NpcConstructionRecord FindRecord(string recordId)
        {
            if (string.IsNullOrWhiteSpace(recordId)) return null;
            records.TryGetValue(recordId, out NpcConstructionRecord record);
            return record;
        }

        #endregion
    }
}
