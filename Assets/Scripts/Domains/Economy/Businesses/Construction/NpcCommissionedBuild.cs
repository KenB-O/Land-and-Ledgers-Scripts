using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
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
        /// <summary>
        /// Pass VIII Worker 3: real freight charges paid to move this
        /// package's materials to the site (a real quoted charge, never
        /// invented). Tracked separately from material cost.
        /// </summary>
        public int FreightCostCents;
        public string Notes = string.Empty;
        /// <summary>
        /// Plan 26 (Q30b): the package's procurement leg failed — either
        /// after the sale stood, with the paid-for lot sitting with the
        /// supplier, or refused before anything was sold (for example a
        /// zero-material refusal, where nothing was paid). The recorded
        /// StrandedReason names the actual failure. The driver blocks a
        /// blind retry until AllowProcurementRetry is called. Persisted
        /// through the commissioner's deep-copy; the driver re-derives its
        /// retry-block set from this marker.
        /// </summary>
        public bool Stranded;
        public string StrandedReason = string.Empty;

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
        /// <summary>
        /// DEF-011 (Decision 7): the legitimate reason the build was
        /// commissioned for. Additive; old saves read Unspecified.
        /// </summary>
        public NpcConstructionReason Reason = NpcConstructionReason.Unspecified;
        /// <summary>Free-text evidence for the reason, as stated on the request.</summary>
        public string ReasonNotes = string.Empty;
        /// <summary>One-line affordability / benefit / shipment evaluation, as recorded at commission.</summary>
        public string EvaluationSummary = string.Empty;
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
    /// DEF-011 (Decision 7): the recorded evaluation behind one commission
    /// — affordability, benefit, alternatives and shipment feasibility,
    /// with the numbers. The evaluation itself is not persisted; its
    /// plain-language lines go onto the record's Decisions and its summary
    /// onto the record's EvaluationSummary at commission time. A non-viable
    /// evaluation carries the refusal reason instead of a design.
    /// </summary>
    public sealed class NpcConstructionEvaluation
    {
        public bool IsViable;
        public string RefusalReason = string.Empty;
        public BuildingDesign ChosenDesign;
        public string ChosenDesignId = string.Empty;
        public int EstimateCents;
        public int FundsCents;
        public int MarginCents;
        public int RequiredPlaces;
        public int CapacityPlaces;
        public string Benefit = string.Empty;
        public List<string> Alternatives = new List<string>();
        public bool ShipmentFeasible = true;
        public string ShipmentNotes = string.Empty;
        public List<string> Lines = new List<string>();
        public string Summary = string.Empty;

        public NpcConstructionEvaluation() { }
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
        /// DEF-011 (Decision 7): the full commission evaluation for one
        /// design — the decision behind the commission, with numbers. The
        /// design must exist, sleep the household, have a computable cost
        /// from the price list, and fit the funds the NPC actually holds;
        /// every package must be shippable by multi-trip freight at the
        /// single-trip bound when one is supplied (D-1: the wagon must be
        /// able to carry the cargo at all — the trip count is recorded,
        /// never a refusal by itself). Alternatives are priced and
        /// recorded, never vetoed: a funded preference for a dearer
        /// adequate design is legitimate.
        /// Returns a non-viable evaluation (never null) with the refusal
        /// reason on any failure — the NPC never builds a design it cannot
        /// price, fund, or physically receive.
        /// </summary>
        public NpcConstructionEvaluation EvaluateCommission(
            BuildingDesignCatalog catalog,
            string designId,
            int requiredSleepingPlaces,
            IConstructionMaterialPricePort pricePort,
            int fundsAvailableCents,
            int? maxUnitsPerTrip,
            NpcConstructionReason reason,
            string reasonNotes,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var evaluation = new NpcConstructionEvaluation
            {
                ChosenDesignId = designId ?? string.Empty,
                FundsCents = fundsAvailableCents,
                RequiredPlaces = Math.Max(1, requiredSleepingPlaces),
            };
            if (catalog == null)
            {
                evaluation.RefusalReason = "NpcCommissioner.EvaluateCommission: no design catalog.";
                diag.Add(evaluation.RefusalReason);
                return evaluation;
            }
            BuildingDesign design = catalog.FindDesign(designId);
            if (design == null)
            {
                evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommission: unknown design '{designId}' — the NPC builds catalog designs only.";
                diag.Add(evaluation.RefusalReason);
                return evaluation;
            }
            evaluation.ChosenDesign = design;
            int capacity = 0;
            foreach (BuildingDesignSpaceSpec spec in design.SpaceSpecs ?? new List<BuildingDesignSpaceSpec>())
                capacity += Math.Max(0, spec != null ? spec.SleepingCapacity : 0);
            evaluation.CapacityPlaces = capacity;
            if (capacity < evaluation.RequiredPlaces)
            {
                evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommission: design '{designId}' sleeps {capacity}, the household needs {evaluation.RequiredPlaces}.";
                diag.Add(evaluation.RefusalReason);
                return evaluation;
            }
            if (pricePort == null)
            {
                evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommission: design '{designId}' has no price port — its materials are unpriced, cost unknowable, refused.";
                diag.Add(evaluation.RefusalReason);
                return evaluation;
            }
            int estimate = 0;
            foreach (BuildingDesignPhase phase in design.Phases ?? new List<BuildingDesignPhase>())
            {
                if (phase == null) continue;
                foreach (ConstructionMaterialRequirement requirement in phase.Materials ?? new List<ConstructionMaterialRequirement>())
                {
                    if (requirement == null || !requirement.IsCoherent)
                    {
                        evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommission: design '{designId}' has an incoherent material requirement — cost unknowable, refused.";
                        diag.Add(evaluation.RefusalReason);
                        return evaluation;
                    }
                    int unitPrice = pricePort.UnitPriceCents(requirement);
                    if (unitPrice < 0)
                    {
                        evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommission: design '{designId}' material '{requirement.MaterialDisplayName}' has no price — cost unknowable, refused.";
                        diag.Add(evaluation.RefusalReason);
                        return evaluation;
                    }
                    estimate += unitPrice * Math.Max(0, requirement.RequiredUnits);
                }
            }
            evaluation.EstimateCents = estimate;
            evaluation.MarginCents = fundsAvailableCents - estimate;
            if (estimate > fundsAvailableCents)
            {
                evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommission: design '{designId}' costs ~{estimate}c against {fundsAvailableCents}c available — the NPC cannot fund it.";
                diag.Add(evaluation.RefusalReason);
                return evaluation;
            }
            foreach (BuildingDesign other in catalog.AllDesigns())
            {
                if (other == null || string.Equals(other.DesignId, designId, StringComparison.Ordinal)) continue;
                int otherCapacity = 0;
                foreach (BuildingDesignSpaceSpec spec in other.SpaceSpecs ?? new List<BuildingDesignSpaceSpec>())
                    otherCapacity += Math.Max(0, spec != null ? spec.SleepingCapacity : 0);
                if (otherCapacity < evaluation.RequiredPlaces) continue;
                int otherEstimate = World.Property.NpcPropertyDecisionEngine.EstimateBuildCostCents(other, pricePort);
                if (otherEstimate < 0) continue;
                evaluation.Alternatives.Add(otherEstimate < estimate
                    ? $"{other.DesignId} ~{otherEstimate}c (cheaper, sleeps {otherCapacity})"
                    : $"{other.DesignId} ~{otherEstimate}c (sleeps {otherCapacity})");
            }
            if (maxUnitsPerTrip.HasValue)
            {
                // D-1 (F-M1): multi-trip freight is real (DEF-016 split-load),
                // so a phase is feasible whenever the wagon can carry the cargo
                // at all — the trip count is recorded, never a refusal by itself.
                // Only a zero bound refuses: no trip can move the cargo.
                if (maxUnitsPerTrip.Value <= 0)
                {
                    foreach (BuildingDesignPhase phase in design.Phases ?? new List<BuildingDesignPhase>())
                    {
                        if (phase == null) continue;
                        int units = 0;
                        foreach (ConstructionMaterialRequirement requirement in phase.Materials ?? new List<ConstructionMaterialRequirement>())
                            if (requirement != null) units += Math.Max(0, requirement.RequiredUnits);
                        if (units <= 0) continue;
                        evaluation.ShipmentFeasible = false;
                        evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommission: design '{designId}' phase '{phase.PhaseName}' needs {units} units, but the wagon cannot carry this cargo at all ({maxUnitsPerTrip.Value}-unit single-trip bound) — no trip can move it, so the design cannot be commissioned as drawn.";
                        diag.Add(evaluation.RefusalReason);
                        return evaluation;
                    }
                    evaluation.ShipmentNotes = $"no materials to ship ({maxUnitsPerTrip.Value}-unit single-trip bound).";
                }
                else
                {
                    var tripNotes = new List<string>();
                    foreach (BuildingDesignPhase phase in design.Phases ?? new List<BuildingDesignPhase>())
                    {
                        if (phase == null) continue;
                        int units = 0;
                        foreach (ConstructionMaterialRequirement requirement in phase.Materials ?? new List<ConstructionMaterialRequirement>())
                            if (requirement != null) units += Math.Max(0, requirement.RequiredUnits);
                        int trips = units <= 0 ? 0 : (int)((units + (long)maxUnitsPerTrip.Value - 1) / maxUnitsPerTrip.Value);
                        tripNotes.Add($"'{phase.PhaseName}' {units} units in {trips} trip{(trips == 1 ? string.Empty : "s")}");
                    }
                    evaluation.ShipmentNotes = $"every package ships by multi-trip freight at the {maxUnitsPerTrip.Value}-unit single-trip bound — {string.Join("; ", tripNotes)}.";
                }
            }
            else
            {
                evaluation.ShipmentNotes = "no trip bound supplied — shipment feasibility not evaluated at commission; the shipment service still refuses over-capacity loads pre-load.";
            }
            evaluation.Benefit = BenefitFor(reason, reasonNotes);
            evaluation.Lines.Add($"day {dayIndex}: evaluated '{designId}' for {reason} — {evaluation.Benefit}.");
            evaluation.Lines.Add($"day {dayIndex}: estimate ~{estimate}c materials against {fundsAvailableCents}c available (margin {evaluation.MarginCents}c); sleeps {capacity} for {evaluation.RequiredPlaces} needed.");
            evaluation.Lines.Add(evaluation.Alternatives.Count > 0
                ? $"day {dayIndex}: alternatives considered — {string.Join("; ", evaluation.Alternatives)}."
                : $"day {dayIndex}: no other priced adequate design in the catalog.");
            evaluation.Lines.Add($"day {dayIndex}: shipment — {evaluation.ShipmentNotes}");
            evaluation.Summary = $"day {dayIndex}: '{designId}' ~{estimate}c vs {fundsAvailableCents}c (margin {evaluation.MarginCents}c), sleeps {capacity}/{evaluation.RequiredPlaces}, {reason}, {evaluation.ShipmentNotes}";
            evaluation.IsViable = true;
            diag.Add($"NpcCommissioner.EvaluateCommission: '{designId}' selected (~{estimate}c, margin {evaluation.MarginCents}c, sleeps {capacity}).");
            return evaluation;
        }

        private static string BenefitFor(NpcConstructionReason reason, string reasonNotes)
        {
            string notes = string.IsNullOrWhiteSpace(reasonNotes) ? "no further detail recorded" : reasonNotes.Trim();
            switch (reason)
            {
                case NpcConstructionReason.Overcrowding: return "relieves overcrowding — " + notes;
                case NpcConstructionReason.HouseholdFormation: return "houses a new household — " + notes;
                case NpcConstructionReason.BusinessExpansion: return "expands the business — " + notes;
                case NpcConstructionReason.Replacement: return "replaces a lost or damaged building — " + notes;
                case NpcConstructionReason.Relocation: return "houses the household at its new site — " + notes;
                case NpcConstructionReason.NewEnterprise: return "houses a new enterprise — " + notes;
                default: return notes;
            }
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
        /// Pass VIII Worker 3: the FULL material chain for one package —
        /// supplier discovery, real-lot sale, real-cash payment, real-chain
        /// freight to the site, then staging from the site stockpile. The
        /// overrun gate runs BEFORE anything moves (priced from the same
        /// price list the sale port must honor); the sale total must match
        /// the priced total or the leg refuses. Freight charges ride on the
        /// freight plan's real quoted charge and are recorded separately.
        /// </summary>
        public string ProcureAndStagePackage(
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            NpcConstructionRecord record,
            WorkPackage package,
            ConstructionProcurementService procurement,
            IConstructionMaterialSalePort salePort,
            IConstructionMaterialPricePort pricePort,
            Population.ISupplierDirectory suppliers,
            ConstructionMaterialFreightCoordinator freightCoordinator,
            ConstructionFreightPlan freightPlan,
            ConstructionSiteStockpile stockpile,
            CreditCashBridge cash,
            string buyerOwnerName,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (executor == null || project == null || record == null || package == null)
                return "NpcCommissioner.ProcureAndStagePackage: executor, project, record and package required.";
            if (procurement == null)
                return "NpcCommissioner.ProcureAndStagePackage: no procurement service.";
            if (cash == null)
                return "NpcCommissioner.ProcureAndStagePackage: no cash bridge — materials are bought with real cash only.";
            if (project.Status == ConstructionProjectStatus.Paused)
                return $"NpcCommissioner.ProcureAndStagePackage: project '{project.ProjectId}' is paused — resolve or resume first.";
            if (record.HasOpenOverrun())
                return $"NpcCommissioner.ProcureAndStagePackage: record '{record.RecordId}' has an unresolved overrun — the NPC answers it before funding more work.";
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            if (funding == null)
                return $"NpcCommissioner.ProcureAndStagePackage: no funding line for package '{package.PackageId}'.";

            // Price every requirement — unpriced means unknowable, never guessed.
            int priced = 0;
            foreach (ConstructionMaterialRequirement requirement in package.Materials ?? new List<ConstructionMaterialRequirement>())
            {
                if (requirement == null || !requirement.IsCoherent)
                    return $"NpcCommissioner.ProcureAndStagePackage: package '{package.PackageId}' has an incoherent material requirement — refusing.";
                int unitPrice = pricePort != null ? pricePort.UnitPriceCents(requirement) : -1;
                if (unitPrice < 0)
                    return $"NpcCommissioner.ProcureAndStagePackage: '{requirement.MaterialDisplayName}' is unpriced — cost unknowable, refusing.";
                priced += unitPrice * Math.Max(0, requirement.RequiredUnits);
            }

            // Overrun check BEFORE anything moves.
            int tolerance = (int)Math.Ceiling(funding.EstimatedMaterialCostCents * Math.Max(0.0, OverrunTolerance01));
            int freightCharge = Math.Max(0, freightPlan != null ? freightPlan.FreightChargeCents : 0);
            if (priced + freightCharge > funding.EstimatedMaterialCostCents + tolerance)
            {
                var overrun = new NpcBuildOverrunEvent
                {
                    DayIndex = dayIndex,
                    PackageId = package.PackageId,
                    EstimatedCents = funding.EstimatedMaterialCostCents,
                    ActualCents = priced + freightCharge,
                };
                record.Overruns.Add(overrun);
                record.Decisions.Add($"day {dayIndex}: OVERRUN on '{package.PhaseName}' — estimated {overrun.EstimatedCents}c, actual {overrun.ActualCents}c (incl. {freightCharge}c freight). Funding blocked until resolved.");
                diag.Add($"NpcCommissioner: OVERRUN on package '{package.PackageId}' ('{package.PhaseName}'): " +
                    $"estimated {overrun.EstimatedCents}c, actual {overrun.ActualCents}c — nothing procured, nothing paid.");
                return $"cost overrun on '{package.PhaseName}': estimated {overrun.EstimatedCents}c, actual {overrun.ActualCents}c — resolve it (inject cash, pause, change order) before funding.";
            }

            IRealCashStore buyerStore = cash.FindStore(buyerOwnerName);
            if (buyerStore == null)
                return $"NpcCommissioner.ProcureAndStagePackage: '{buyerOwnerName}' has no real cash store.";
            if (buyerStore.ReadBalanceCents() < priced + freightCharge)
                return $"NpcCommissioner.ProcureAndStagePackage: '{buyerOwnerName}' holds {buyerStore.ReadBalanceCents()}c but procurement costs {priced + freightCharge}c — refusing rather than spending what they lack.";

            ConstructionMaterialSale sale;
            string problem = procurement.ProcureFreightAndStage(executor, project, package,
                salePort, pricePort, suppliers, freightCoordinator, freightPlan, stockpile,
                cash, buyerOwnerName, obligations, ids, priced, dayIndex, diag, out sale);
            if (problem != null) return problem;

            funding.ActualMaterialCostCents = sale != null ? sale.TotalCents : priced;
            funding.FreightCostCents = freightCharge;
            record.TotalActualCostCents += funding.ActualMaterialCostCents + freightCharge;
            record.Decisions.Add($"day {dayIndex}: procured '{package.PhaseName}' — sale '{(sale != null ? sale.SaleId : "?")}' " +
                $"({funding.ActualMaterialCostCents}c materials + {freightCharge}c freight), freighted through the real shipment chain and staged.");
            diag.Add($"NpcCommissioner: package '{package.PackageId}' ('{package.PhaseName}') procured and staged — {funding.ActualMaterialCostCents}c materials, {freightCharge}c freight, all paid from real cash.");
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
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (executor == null || project == null || record == null || package == null)
                return "NpcCommissioner.CommitOwnLabor: executor, project, record and package required.";
            string problem = executor.CommitLabor(project, package, personId, tracker, custody, tasks, budgets,
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

        // Pass VIII Worker 3: the pre-universal-task compatibility overloads
        // were removed — they passed null TaskAuthority/WorkTimeBudgetStore,
        // which the executor rightly refuses ("labor must be a real
        // WorkTask"). Labor commits through the full overloads below only.

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
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
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

            string commitProblem = executor.CommitLabor(project, package, workerPersonId, tracker, custody, tasks, budgets,
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

        // Pass VIII Worker 3: see the note above — the pre-universal-task
        // HireWorker overload was removed for the same reason.

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
            return ConstructionCashMover.MoveCash(cash, payer, payee, amountCents, dayIndex, purpose, diag);
        }

        /// <summary>
        /// DEF-071 (Decision 14, D3): records unpaid hired wages as a
        /// FinancialObligationAuthority debt — a claim, never treated as
        /// paid. Returns false (and diagnoses loudly) when the debt cannot
        /// be recorded; the caller records the shortfall in diagnostics and
        /// proceeds — the cancel is never blocked by an unrecordable debt.
        /// </summary>
        private static bool RecordWageDebt(
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            string payerOwnerName,
            int workerHouseholdId,
            int amountCents,
            ConstructionProject project,
            WorkPackage package,
            int dayIndex,
            List<string> diag)
        {
            if (amountCents <= 0) return true;
            if (obligations == null || ids == null || string.IsNullOrWhiteSpace(payerOwnerName)
                || workerHouseholdId < 0)
            {
                diag.Add($"NpcCommissioner.SettleCancellationWages: {amountCents}c hired wages for package '{package?.PackageId}' could not be recorded as a debt (no obligation authority, id registry, payer, or worker household) — the unpaid amount is recorded here only, not as a claim.");
                return false;
            }
            FinancialObligation debt = obligations.Create(ids, FinancialObligationKind.Payable,
                payerOwnerName, "household:" + workerHouseholdId, amountCents, dayIndex,
                $"unpaid hired-worker wages on cancelled project '{project?.ProjectId}', package '{package?.PackageId}'",
                $"hired wages owed on cancellation (project '{project?.ProjectId}', package '{package?.PackageId}')");
            if (debt == null)
            {
                diag.Add($"NpcCommissioner.SettleCancellationWages: {amountCents}c hired wages for package '{package?.PackageId}' could not be recorded as a debt — the obligation authority refused it.");
                return false;
            }
            diag.Add($"NpcCommissioner.SettleCancellationWages: {amountCents}c hired wages for package '{package?.PackageId}' recorded as debt '{debt.ObligationId}' ({payerOwnerName} owes household:{workerHouseholdId}).");
            return true;
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

        /// <summary>
        /// DEF-071 (Decision 14, D3): settles hired-worker wages when a
        /// project is cancelled. For each INCOMPLETE package whose funding
        /// line hired workers and committed minutes, the agreed pro-rata
        /// wage is paid from the payer's real cash through
        /// PayWorkerForPackage (same formula, same cash bridge). When the
        /// payment is refused — the household cannot cover it — the unpaid
        /// remainder is recorded as a FinancialObligationAuthority debt
        /// (Payable, a claim, never treated as paid) and the cancel
        /// proceeds: refusing the whole cancel over a short household would
        /// brick the project, and the debt survives instead. Every listed
        /// employment is then ended EndedByAgreement on the cancel day,
        /// whether its wage was paid or recorded as a debt. Own-labour
        /// packages owe no wages. Returns null on success, INCLUDING
        /// converted-to-debt refusals; a non-null return is an argument
        /// error only, and the caller (the driver) logs it and proceeds
        /// with the material cancel regardless.
        /// </summary>
        public string SettleCancellationWages(
            EmploymentRelationshipRegistry employments,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            NpcConstructionRecord record,
            ConstructionProject project,
            CreditCashBridge cash,
            string payerOwnerName,
            Dictionary<int, int> workerHouseholdByPerson,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (employments == null || record == null || project == null)
                return "NpcCommissioner.SettleCancellationWages: employments, record and project required.";
            if (project.WorkPackages == null || record.PackageFundings == null)
                return null; // nothing to settle.
            foreach (WorkPackage package in project.WorkPackages)
            {
                if (package == null || package.Status == WorkPackageStatus.Complete)
                    continue;
                NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
                if (funding == null || funding.LaborKind != NpcBuildLaborKind.HiredWorker
                    || funding.WorkerMinutesCommitted <= 0 || funding.EmploymentIds == null
                    || funding.EmploymentIds.Count == 0)
                    continue;
                // Expected wage per employment, same formula as
                // PayWorkerForPackage: ceil(weekly wage x minutes /
                // standard week). Unresolvable employments have no
                // computable wage; they are named loudly and excluded from
                // the expected total rather than guessed.
                var expected = new List<(string EmploymentId, int HouseholdId, int WageCents)>();
                var unresolvable = new List<string>();
                int week = Math.Max(1, StandardWorkWeekMinutes);
                foreach (string employmentId in funding.EmploymentIds)
                {
                    if (!employments.TryGetById(employmentId, out EmploymentRelationship relationship)
                        || relationship == null)
                    {
                        unresolvable.Add(employmentId);
                        continue;
                    }
                    int weekly = Math.Max(0, relationship.Compensation.AgreedWeeklyWageCents);
                    int wage = (int)Math.Ceiling(weekly
                        * (Math.Max(0, funding.WorkerMinutesCommitted) / (double)week));
                    if (wage <= 0) continue;
                    int householdId = -1;
                    if (workerHouseholdByPerson != null)
                        workerHouseholdByPerson.TryGetValue(relationship.EmployeePersonId, out householdId);
                    if (householdId < 0)
                    {
                        unresolvable.Add(employmentId);
                        continue;
                    }
                    expected.Add((employmentId, householdId, wage));
                }
                int expectedTotal = 0;
                foreach (var line in expected) expectedTotal += line.WageCents;
                if (unresolvable.Count > 0)
                    diag.Add($"NpcCommissioner.SettleCancellationWages: package '{package.PackageId}' has {unresolvable.Count} employment(s) with no computable wage ({string.Join(", ", unresolvable)}) — their wages are not in the settlement total; the employments are still ended below.");
                bool attemptedPayment = funding.WagesPaidCents <= 0;
                string payProblem = null;
                if (attemptedPayment)
                {
                    payProblem = PayWorkerForPackage(employments, record, package, cash,
                        payerOwnerName, workerHouseholdByPerson, dayIndex, diag);
                    if (payProblem != null)
                        diag.Add($"NpcCommissioner.SettleCancellationWages: wage payment for package '{package.PackageId}' refused — {payProblem} The unpaid remainder is recorded as a debt, not treated as paid.");
                }
                // Unpaid remainder: expected minus what the funding line
                // records as paid. PayWorkerForPackage pays employments in
                // list order and stops at the first refusal, so the paid set
                // is the prefix whose wages sum to WagesPaidCents; the rest
                // are unpaid, in order.
                int paidCents = Math.Max(0, funding.WagesPaidCents);
                int unpaidTotal = Math.Max(0, expectedTotal - paidCents);
                if (unpaidTotal > 0)
                {
                    int running = 0;
                    int firstUnpaidIndex = expected.Count;
                    for (int i = 0; i < expected.Count; i++)
                    {
                        if (running == paidCents) { firstUnpaidIndex = i; break; }
                        running += expected[i].WageCents;
                        if (running == paidCents) { firstUnpaidIndex = i + 1; break; }
                    }
                    bool prefixMatches = running == paidCents;
                    if (prefixMatches)
                    {
                        for (int i = firstUnpaidIndex; i < expected.Count; i++)
                            RecordWageDebt(obligations, ids, payerOwnerName, expected[i].HouseholdId,
                                expected[i].WageCents, project, package, dayIndex, diag);
                    }
                    else
                    {
                        // Attribution uncertain: no prefix of the expected
                        // wages sums to what the funding line records as
                        // paid. One debt for the whole remainder to the first
                        // employment's household, named loudly — the claim
                        // survives, the uncertainty is on the record.
                        diag.Add($"NpcCommissioner.SettleCancellationWages: package '{package.PackageId}' paid-prefix reconstruction failed (paid {paidCents}c, expected {expectedTotal}c) — recording one debt for the {unpaidTotal}c remainder to the first employment's household.");
                        if (expected.Count > 0)
                            RecordWageDebt(obligations, ids, payerOwnerName, expected[0].HouseholdId,
                                unpaidTotal, project, package, dayIndex, diag);
                    }
                    record.Decisions.Add($"day {dayIndex}: cancelled '{package.PackageId}' — {unpaidTotal}c hired wages unpaid, recorded as a debt (see obligations).");
                }
                else if (attemptedPayment && payProblem == null && expectedTotal > 0)
                {
                    record.Decisions.Add($"day {dayIndex}: cancelled '{package.PackageId}' — hired wages {paidCents}c paid on cancellation.");
                }
                foreach (string employmentId in funding.EmploymentIds)
                {
                    if (!employments.EndEmployment(employmentId, dayIndex, EmploymentEndReason.EndedByAgreement))
                        diag.Add($"NpcCommissioner.SettleCancellationWages: employment '{employmentId}' for package '{package.PackageId}' was already ended or unknown — nothing to end.");
                }
            }
            return null;
        }

        /// <summary>
        /// DEF-071 (Decision 14, D4): cancels the builder contract bound to
        /// a project and settles it through the billing service — the mirror
        /// of CommissionBuilderContract / AcceptBuilderStageAndReleaseDraw,
        /// which take their authorities as parameters. The driver holds
        /// neither the contract book nor the billing service, so this stays
        /// a commissioner-level method for the production caller to invoke
        /// when it cancels a project; its existence (harness-tested) is the
        /// fix, the trigger — who decides to cancel — is a separate
        /// NPC-decision concern, same as the project-cancel trigger itself.
        /// Order: cancel first (the reason is recorded on the contract),
        /// settle second (SettleCancellation requires a Cancelled contract).
        /// If settlement is refused (no cash port, short owner cash), the
        /// contract stays Cancelled with its obligations unsettled — honest,
        /// and the settlement is retryable; the cancel itself is not
        /// rolled back.
        /// </summary>
        public string CancelBuilderContract(
            ConstructionContract contract,
            ConstructionProgressBillingService billing,
            NpcConstructionRecord record,
            string reason,
            string cancelledBy,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (contract == null || billing == null)
                return "NpcCommissioner.CancelBuilderContract: contract and billing service required.";
            if (record != null && !string.IsNullOrWhiteSpace(record.ContractId)
                && !string.Equals(record.ContractId, contract.ContractId, StringComparison.Ordinal))
                return $"NpcCommissioner.CancelBuilderContract: contract '{contract.ContractId}' is not the contract bound to record '{record.RecordId}' ('{record.ContractId}') — refusing to cancel the wrong contract.";
            string cancelProblem = contract.Cancel(dayIndex, diag, reason, cancelledBy);
            if (cancelProblem != null) return cancelProblem;
            string settleProblem = billing.SettleCancellation(contract, dayIndex, diag);
            if (settleProblem != null)
            {
                diag.Add($"NpcCommissioner.CancelBuilderContract: contract '{contract.ContractId}' is cancelled but its settlement was refused — {settleProblem} The obligations stay unsettled and the settlement can be retried.");
                if (record != null)
                    record.Decisions.Add($"day {dayIndex}: builder contract '{contract.ContractId}' cancelled ({reason ?? "no reason recorded"}) — settlement refused, obligations unsettled (retryable).");
                return settleProblem;
            }
            if (record != null)
                record.Decisions.Add($"day {dayIndex}: builder contract '{contract.ContractId}' cancelled ({reason ?? "no reason recorded"}) and settled.");
            diag.Add($"NpcCommissioner: builder contract '{contract.ContractId}' cancelled and settled on day {dayIndex}.");
            return null;
        }

        #endregion

        #region Save / Load

        [Serializable]
        public sealed class NpcConstructionCommissionerSaveDto
        {
            public List<NpcConstructionRecord> Records = new List<NpcConstructionRecord>();
            /// <summary>
            /// Plan 26 (Q30a): the commissioner's id sequence at capture.
            /// Additive; -1 means absent (an old save predating this field)
            /// — Load then recomputes from the restored ids. Mirrors the
            /// executor's ExecutorSequence (Pass XI WS4).
            /// </summary>
            public int CommissionerSequence = -1;
        }

        public NpcConstructionCommissionerSaveDto CaptureSaveDto()
        {
            var dto = new NpcConstructionCommissionerSaveDto();
            dto.CommissionerSequence = sequence;
            foreach (NpcConstructionRecord record in records.Values)
                if (record != null) dto.Records.Add(record);
            return dto;
        }

        /// <summary>
        /// Pass XII Worker F (copy-on-load, E-DEFECT-02 pattern):
        /// commissioning mutates records in place (ContractId binding
        /// and Decisions appends on the found record), so storing the
        /// DTO's record objects would let a run corrupt the caller's
        /// snapshot — a second restore would load an already-bound
        /// record with another run's decisions. Records are therefore
        /// deep-copied, including funding lists, overrun lists and
        /// decisions. Clear-then-load and duplicate handling are
        /// unchanged.
        /// </summary>
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
                var copy = new NpcConstructionRecord
                {
                    RecordId = record.RecordId,
                    ProjectId = record.ProjectId ?? string.Empty,
                    HouseholdId = record.HouseholdId,
                    DesignId = record.DesignId ?? string.Empty,
                    ParcelId = record.ParcelId ?? string.Empty,
                    ContractId = record.ContractId ?? string.Empty,
                    Reason = record.Reason,
                    ReasonNotes = record.ReasonNotes ?? string.Empty,
                    EvaluationSummary = record.EvaluationSummary ?? string.Empty,
                    TotalEstimatedCostCents = record.TotalEstimatedCostCents,
                    TotalActualCostCents = record.TotalActualCostCents,
                };
                if (record.PackageFundings != null)
                {
                    foreach (NpcBuildPackageFunding funding in record.PackageFundings)
                    {
                        if (funding == null) continue;
                        var fundingCopy = new NpcBuildPackageFunding
                        {
                            PackageId = funding.PackageId ?? string.Empty,
                            PhaseName = funding.PhaseName ?? string.Empty,
                            EstimatedMaterialCostCents = funding.EstimatedMaterialCostCents,
                            ActualMaterialCostCents = funding.ActualMaterialCostCents,
                            LaborKind = funding.LaborKind,
                            WorkerMinutesCommitted = funding.WorkerMinutesCommitted,
                            WagesPaidCents = funding.WagesPaidCents,
                            FreightCostCents = funding.FreightCostCents,
                            Notes = funding.Notes ?? string.Empty,
                            Stranded = funding.Stranded,
                            StrandedReason = funding.StrandedReason ?? string.Empty,
                        };
                        if (funding.WorkerPersonIds != null) fundingCopy.WorkerPersonIds.AddRange(funding.WorkerPersonIds);
                        if (funding.EmploymentIds != null) fundingCopy.EmploymentIds.AddRange(funding.EmploymentIds);
                        copy.PackageFundings.Add(fundingCopy);
                    }
                }
                if (record.Overruns != null)
                {
                    foreach (NpcBuildOverrunEvent overrun in record.Overruns)
                    {
                        if (overrun == null) continue;
                        copy.Overruns.Add(new NpcBuildOverrunEvent
                        {
                            DayIndex = overrun.DayIndex,
                            PackageId = overrun.PackageId ?? string.Empty,
                            EstimatedCents = overrun.EstimatedCents,
                            ActualCents = overrun.ActualCents,
                            Resolution = overrun.Resolution,
                            ResolutionNotes = overrun.ResolutionNotes ?? string.Empty,
                            Resolved = overrun.Resolved,
                        });
                    }
                }
                if (record.Decisions != null) copy.Decisions.AddRange(record.Decisions);
                records[copy.RecordId] = copy;
            }
            // Plan 26 (Q30a, note 102 C7): continue the id sequence instead
            // of restarting at 0 and re-minting an existing id. The persisted
            // sequence wins when it is ahead; the recomputed floor wins when
            // the field is absent (old save, -1) or behind the restored ids,
            // so an old save can never collide either. The scan covers the
            // DTO's records, so a duplicate-skipped record still counts
            // toward the floor (its id was in the DTO). Only RecordIds are
            // scanned (PackageIds are minted by the executor's sequence).
            int maxSuffix = -1;
            foreach (NpcConstructionRecord source in dto.Records)
            {
                if (source == null) continue;
                maxSuffix = Math.Max(maxSuffix, TrailingNumber(source.RecordId));
            }
            sequence = Math.Max(dto.CommissionerSequence, maxSuffix + 1);
        }

        public NpcConstructionRecord FindRecord(string recordId)
        {
            if (string.IsNullOrWhiteSpace(recordId)) return null;
            records.TryGetValue(recordId, out NpcConstructionRecord record);
            return record;
        }

        /// <summary>
        /// Pass XI WS2 (driver support, additive only): finds the principal's
        /// record for a project id, so the production driver can fund a
        /// project restored from a save without a caller-held record
        /// reference. Returns null when the project was never commissioned
        /// through this commissioner.
        /// </summary>
        public NpcConstructionRecord FindRecordForProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return null;
            foreach (NpcConstructionRecord record in records.Values)
                if (record != null && string.Equals(record.ProjectId, projectId, StringComparison.Ordinal))
                    return record;
            return null;
        }

        /// <summary>Numeric suffix after the last '-' in an id, or -1 when there is none. (Plan 26 Q30a, note 102 C7: duplicated from the executor — no cross-class coupling for a 9-line pure function.)</summary>
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
