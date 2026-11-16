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
        /// <summary>
        /// D-4: the construction loan funding this build, when one was
        /// drawn (null when the build is cash-funded). Plain serializable
        /// data only — the financing approval result itself is never stored
        /// (read-only properties, a SimulationDate). The draw fills
        /// DrawnDayIndex, ObligationId and Schedule in place, so the
        /// record keeps the loan it was commissioned with.
        /// </summary>
        public NpcConstructionLoanTerms LoanTerms;

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
        /// <summary>
        /// D-4: the approved construction loan funding this evaluation,
        /// when one was sought (null for a cash-only evaluation). The
        /// evaluation itself is not persisted; the driver copies this onto
        /// the record at commission time, before the draw fills it in.
        /// </summary>
        public NpcConstructionLoanTerms LoanTerms;

        public NpcConstructionEvaluation() { }
    }

    /// <summary>
    /// D-4: the flat, serializable terms of one NPC construction loan —
    /// the funding leg of a commissioned build. Copied from a
    /// <see cref="FinancingApprovalResult"/> at underwriting time by
    /// <see cref="NpcConstructionCommissioner.BuildLoanTerms"/> (the
    /// approval result itself is never stored: read-only properties and
    /// a SimulationDate make it unsuitable for a record). The draw fills
    /// DrawnDayIndex, ObligationId, SecurityInterestId and Schedule in
    /// place; servicing updates MissedPayments and the schedule's paid
    /// amounts. The loan covers the materials estimate only — labour and
    /// freight are not in the estimate, so hired wages come from the
    /// household's post-build cash and income, and a shortfall there is
    /// recorded loudly by the existing wage settlement. The down payment
    /// is the household's own cash committed to the build (recorded,
    /// never moved separately — staging draws on the household's cash,
    /// which the draw has topped up with the principal).
    /// </summary>
    [Serializable]
    public sealed class NpcConstructionLoanTerms
    {
        public string AgreementId = string.Empty;
        public string LenderId = string.Empty;
        public string LenderOwnerName = string.Empty;
        public LoanPurpose Purpose = LoanPurpose.Construction;
        public FinancingDecision Decision = FinancingDecision.Declined;
        /// <summary>True when the approval cannot fund immediately (conditional, or a quantified shortfall).</summary>
        public bool HasBlockingReadinessGap = true;
        public List<string> Conditions = new List<string>();
        /// <summary>FinancingReasonCode names, as strings — plain data, never the enum list.</summary>
        public List<string> ReasonCodes = new List<string>();
        /// <summary>The materials estimate the approval was underwritten against. A loan evaluated for one price never funds another.</summary>
        public int EvaluatedPriceCents;
        public int PrincipalCents;
        public int DownPaymentCents;
        public int AnnualInterestRateBps;
        public int TermMonths;
        public RepaymentFrequency RepaymentFrequency = RepaymentFrequency.Weekly;
        public int EstimatedPeriodicPaymentCents;
        public int OriginationFeeCents;
        public string CollateralId = string.Empty;
        public LoanCollateralKind CollateralKind = LoanCollateralKind.Unsecured;
        public int CollateralValueCents;
        /// <summary>-1 until drawn.</summary>
        public int DrawnDayIndex = -1;
        public string ObligationId = string.Empty;
        public string SecurityInterestId = string.Empty;
        /// <summary>The repayment plan, built at draw time from the obligation id. Null until drawn.</summary>
        public LoanPaymentSchedule Schedule;
        public int MissedPayments;
        /// <summary>-1 when no payment has ever been missed.</summary>
        public int LastMissedDayIndex = -1;
        /// <summary>-1 when no payment has ever been serviced.</summary>
        public int LastServicedDayIndex = -1;

        /// <summary>True once the principal has moved and the obligation exists — a loan is drawn exactly once.</summary>
        public bool IsDrawn => DrawnDayIndex >= 0 && !string.IsNullOrWhiteSpace(ObligationId);

        public NpcConstructionLoanTerms() { }
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

        #region Construction loans (D-4)

        /// <summary>
        /// D-4: underwrites a construction loan for one build through the
        /// real financing evaluator — the construction purpose policy
        /// (stricter rate, down payment, collateral support, cash-flow
        /// coverage and fee). The applicant, collateral and lender are
        /// caller-supplied, never invented here: no production source
        /// exists for an NPC household's reputation, trust, weekly cash
        /// flow or owned assets, and the collateral is the structure to
        /// be built, valued by its caller-supplied estimate. The down
        /// payment is the household's cash (all of it, up to the price) —
        /// exact-shortfall borrowing: the principal is the price minus
        /// the cash. Returns the evaluator's result, never null, except
        /// for caller errors (an incoherent price or cash figure, or no
        /// agreement id), which return null + a loud diagnostic. A
        /// Declined or ConditionallyApproved result is a real underwriting
        /// outcome, not an error — only a full approval with no blocking
        /// readiness gap ever funds.
        /// </summary>
        public FinancingApprovalResult EvaluateConstructionLoan(
            int estimateCents,
            int cashAvailableCents,
            FinancingApplicantProfile applicant,
            CollateralProfile collateral,
            LenderProfile lender,
            LoanTermStructure requestedTerm,
            string agreementId,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estimateCents <= 0)
            {
                diag.Add("NpcCommissioner.EvaluateConstructionLoan: the estimate must be a positive materials cost — a loan cannot be underwritten for an unpriced build.");
                return null;
            }
            if (cashAvailableCents < 0)
            {
                diag.Add("NpcCommissioner.EvaluateConstructionLoan: the household's cash cannot be negative.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(agreementId))
            {
                diag.Add("NpcCommissioner.EvaluateConstructionLoan: an agreement id is required — the loan is traceable to its commission.");
                return null;
            }
            var request = new FinancingApprovalRequest
            {
                agreementId = agreementId,
                purpose = LoanPurpose.Construction,
                exactPurchasePriceCents = estimateCents,
                exactDownPaymentCents = Math.Min(cashAvailableCents, estimateCents),
                applicant = applicant,
                collateral = collateral,
                lender = lender,
                requestedTerm = requestedTerm,
                approvalDate = CalendarAnchor.Default.BuildDate(dayIndex),
                daysPerMonth = 0,
            };
            FinancingApprovalResult result = new FinancingEvaluator().EvaluateApproval(request);
            string outcome = result.Decision == FinancingDecision.Approved
                ? $"APPROVED {result.EstimatedPrincipalCents}c at {result.Offer.termStructure.annualInterestRateBps} bps, ~{result.EstimatedPeriodicPaymentCents}c/{result.Offer.termStructure.repaymentFrequency}, fee {result.EstimatedOriginationFeeCents}c"
                : $"{result.Decision} ({string.Join(", ", result.ReasonCodes)})";
            diag.Add($"NpcCommissioner.EvaluateConstructionLoan: construction loan for ~{estimateCents}c (down {request.exactDownPaymentCents}c) — {outcome}.");
            if (result.HasConditions)
                diag.Add($"NpcCommissioner.EvaluateConstructionLoan: conditions — {string.Join("; ", result.RequiredConditions)}");
            if (result.HasQuantifiedShortfall)
                diag.Add($"NpcCommissioner.EvaluateConstructionLoan: shortfalls — down {result.AdditionalDownPaymentNeededCents}c, principal support {result.PrincipalSupportShortfallCents}c, weekly cash flow {result.WeeklyCashFlowShortfallCents}c, reputation {result.ReputationShortfall01:0.00}, lender trust {result.LenderTrustShortfall01:0.00}.");
            return result;
        }

        /// <summary>
        /// D-4: flattens an approval result into the plain loan terms a
        /// record can carry. Returns null when there is no offer to copy
        /// (a null result, or a result without an offer) — the caller
        /// treats that as no loan. The collateral is passed in because
        /// the result does not carry it.
        /// </summary>
        public static NpcConstructionLoanTerms BuildLoanTerms(
            FinancingApprovalResult result,
            CollateralProfile collateral,
            string lenderOwnerName,
            int evaluatedPriceCents)
        {
            if (result == null || result.Offer == null) return null;
            LoanTermStructure term = result.Offer.termStructure.Sanitized();
            var terms = new NpcConstructionLoanTerms
            {
                AgreementId = result.AgreementId ?? string.Empty,
                LenderId = result.Offer.lenderId ?? string.Empty,
                LenderOwnerName = lenderOwnerName ?? string.Empty,
                Purpose = result.Offer.purpose,
                Decision = result.Decision,
                HasBlockingReadinessGap = result.HasBlockingReadinessGap,
                EvaluatedPriceCents = evaluatedPriceCents,
                PrincipalCents = result.EstimatedPrincipalCents,
                DownPaymentCents = term.downPaymentCents,
                AnnualInterestRateBps = term.annualInterestRateBps,
                TermMonths = term.termMonths,
                RepaymentFrequency = term.repaymentFrequency,
                EstimatedPeriodicPaymentCents = result.EstimatedPeriodicPaymentCents,
                OriginationFeeCents = result.EstimatedOriginationFeeCents,
                CollateralId = collateral.collateralId ?? string.Empty,
                CollateralKind = collateral.kind,
                CollateralValueCents = collateral.estimatedValueCents,
            };
            if (result.RequiredConditions != null) terms.Conditions.AddRange(result.RequiredConditions);
            if (result.ReasonCodes != null)
                foreach (FinancingReasonCode code in result.ReasonCodes)
                    terms.ReasonCodes.Add(code.ToString());
            return terms;
        }

        /// <summary>
        /// D-4: the loan-funded commission evaluation — the same pricing,
        /// sleeping, alternatives and shipment checks as the cash-only
        /// evaluation, with the funding stack cash + approved loan
        /// principal instead of cash alone. Only a full approval with no
        /// blocking readiness gap funds: a conditional approval is refused
        /// loudly with its conditions named, and a loan evaluated for a
        /// different price never funds this design (stale approvals do not
        /// travel). The cash-only overload is unchanged — existing callers
        /// keep their behaviour.
        /// </summary>
        public NpcConstructionEvaluation EvaluateCommissionWithLoan(
            BuildingDesignCatalog catalog,
            string designId,
            int requiredSleepingPlaces,
            IConstructionMaterialPricePort pricePort,
            int cashAvailableCents,
            NpcConstructionLoanTerms loanTerms,
            int? maxUnitsPerTrip,
            NpcConstructionReason reason,
            string reasonNotes,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (loanTerms == null)
            {
                var noLoan = new NpcConstructionEvaluation
                {
                    ChosenDesignId = designId ?? string.Empty,
                    FundsCents = cashAvailableCents,
                    RequiredPlaces = Math.Max(1, requiredSleepingPlaces),
                    RefusalReason = "NpcCommissioner.EvaluateCommissionWithLoan: no loan terms supplied — use the cash-only evaluation when no loan is sought.",
                };
                diag.Add(noLoan.RefusalReason);
                return noLoan;
            }
            if (loanTerms.Decision != FinancingDecision.Approved || loanTerms.HasBlockingReadinessGap)
            {
                var notFundable = new NpcConstructionEvaluation
                {
                    ChosenDesignId = designId ?? string.Empty,
                    FundsCents = cashAvailableCents,
                    RequiredPlaces = Math.Max(1, requiredSleepingPlaces),
                    LoanTerms = loanTerms,
                    RefusalReason = $"NpcCommissioner.EvaluateCommissionWithLoan: the construction loan is {loanTerms.Decision} and cannot fund this build"
                        + (loanTerms.Conditions != null && loanTerms.Conditions.Count > 0
                            ? $" — conditions: {string.Join("; ", loanTerms.Conditions)}"
                            : ".")
                        + " Only a full approval with no blocking readiness gap funds a commission.",
                };
                diag.Add(notFundable.RefusalReason);
                return notFundable;
            }
            int funds = cashAvailableCents + Math.Max(0, loanTerms.PrincipalCents);
            NpcConstructionEvaluation evaluation = EvaluateCommission(catalog, designId,
                requiredSleepingPlaces, pricePort, funds, maxUnitsPerTrip,
                reason, reasonNotes, dayIndex, diag);
            evaluation.LoanTerms = loanTerms;
            if (!evaluation.IsViable) return evaluation;
            if (loanTerms.EvaluatedPriceCents != evaluation.EstimateCents)
            {
                evaluation.IsViable = false;
                evaluation.RefusalReason = $"NpcCommissioner.EvaluateCommissionWithLoan: the loan was underwritten for ~{loanTerms.EvaluatedPriceCents}c but design '{designId}' now prices at ~{evaluation.EstimateCents}c — a stale approval never funds a different price.";
                diag.Add(evaluation.RefusalReason);
                return evaluation;
            }
            evaluation.Lines.Add($"day {dayIndex}: funded by cash {cashAvailableCents}c + construction loan {loanTerms.PrincipalCents}c from '{loanTerms.LenderId}' at {loanTerms.AnnualInterestRateBps} bps, ~{loanTerms.EstimatedPeriodicPaymentCents}c/{loanTerms.RepaymentFrequency} over {loanTerms.TermMonths} months, fee {loanTerms.OriginationFeeCents}c, down payment {loanTerms.DownPaymentCents}c.");
            evaluation.Summary += $", funded cash {cashAvailableCents}c + loan {loanTerms.PrincipalCents}c ({loanTerms.LenderId}, {loanTerms.AnnualInterestRateBps} bps)";
            diag.Add($"NpcCommissioner.EvaluateCommissionWithLoan: '{designId}' funded by cash {cashAvailableCents}c + loan {loanTerms.PrincipalCents}c (margin {evaluation.MarginCents}c).");
            return evaluation;
        }

        /// <summary>
        /// D-4: draws an approved construction loan at commission time —
        /// moves the principal from the lender's real cash store to the
        /// household (finite capital, never conjured), creates the debt
        /// through the single debt authority with the offer's terms, builds
        /// the repayment schedule from the obligation id, and files a
        /// first lien on the parcel the structure stands on. Called AFTER
        /// CommissionProject, so the record exists to hold the loan; the
        /// driver pre-checks the lender's balance before creating the
        /// project, so a draw failure here is near-unreachable — and when
        /// it does happen the commission is refused, the project is
        /// dropped (CreateProject only returns the project, nothing leaks)
        /// and the failure is recorded on the record. On an obligation
        /// failure after the cash moved, the cash is refunded immediately
        /// and the failure is loud. A loan is drawn exactly once.
        /// Returns null on success, the refusal on failure.
        /// </summary>
        public string DrawConstructionLoan(
            NpcConstructionRecord record,
            NpcConstructionLoanTerms loanTerms,
            CreditCashBridge cash,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (record == null || loanTerms == null)
                return "NpcCommissioner.DrawConstructionLoan: record and loan terms required.";
            if (loanTerms.IsDrawn)
                return $"NpcCommissioner.DrawConstructionLoan: the loan for project '{record.ProjectId}' was already drawn on day {loanTerms.DrawnDayIndex} (obligation '{loanTerms.ObligationId}') — a loan is drawn exactly once.";
            if (loanTerms.Decision != FinancingDecision.Approved || loanTerms.HasBlockingReadinessGap)
                return $"NpcCommissioner.DrawConstructionLoan: the loan for project '{record.ProjectId}' is {loanTerms.Decision} and cannot be drawn — only a full approval with no blocking readiness gap funds a commission.";
            if (loanTerms.PrincipalCents <= 0)
                return $"NpcCommissioner.DrawConstructionLoan: the loan for project '{record.ProjectId}' has no principal to draw.";
            if (string.IsNullOrWhiteSpace(loanTerms.LenderOwnerName))
                return $"NpcCommissioner.DrawConstructionLoan: the loan for project '{record.ProjectId}' names no lender cash store — the principal cannot be drawn from nowhere.";
            if (cash == null || obligations == null || ids == null)
                return "NpcCommissioner.DrawConstructionLoan: cash bridge, obligation authority and id registry required.";
            string householdOwnerName = "household:" + record.HouseholdId;
            IRealCashStore lenderStore = cash.FindStore(loanTerms.LenderOwnerName);
            if (lenderStore == null)
                return $"NpcCommissioner.DrawConstructionLoan: lender '{loanTerms.LenderOwnerName}' has no real cash store — the {loanTerms.PrincipalCents}c principal cannot be drawn.";
            if (lenderStore.ReadBalanceCents() < loanTerms.PrincipalCents)
                return $"NpcCommissioner.DrawConstructionLoan: lender '{loanTerms.LenderOwnerName}' holds {lenderStore.ReadBalanceCents()}c, short of the {loanTerms.PrincipalCents}c principal — the bank lends finite capital only.";
            if (cash.FindStore(householdOwnerName) == null)
                return $"NpcCommissioner.DrawConstructionLoan: '{householdOwnerName}' has no real cash store — the principal has nowhere to go.";
            string moveProblem = ConstructionCashMover.MoveCash(cash, loanTerms.LenderOwnerName,
                householdOwnerName, loanTerms.PrincipalCents, dayIndex,
                $"construction loan draw for project '{record.ProjectId}'", diag);
            if (moveProblem != null)
                return $"NpcCommissioner.DrawConstructionLoan: the {loanTerms.PrincipalCents}c principal could not move — {moveProblem}";
            var paymentTerms = new FinancialPaymentTerms
            {
                AnnualInterestRateBps = loanTerms.AnnualInterestRateBps,
                Structure = FinancialPaymentStructure.PrincipalInstallments,
                PaymentIntervalDays = loanTerms.RepaymentFrequency == RepaymentFrequency.Monthly ? 30 : 7,
                InstallmentPrincipalCents = 0,
                AllowsEarlyPayoff = true,
            };
            FinancialObligation obligation = obligations.CreateWithTerms(ids,
                FinancialObligationKind.Loan, householdOwnerName, loanTerms.LenderOwnerName,
                loanTerms.PrincipalCents, dayIndex,
                $"construction loan for project '{record.ProjectId}' — {loanTerms.AnnualInterestRateBps} bps, {loanTerms.TermMonths} months, {loanTerms.RepaymentFrequency}",
                $"construction loan for project '{record.ProjectId}'",
                paymentTerms, loanTerms.AgreementId);
            if (obligation == null)
            {
                string refundProblem = ConstructionCashMover.MoveCash(cash, householdOwnerName,
                    loanTerms.LenderOwnerName, loanTerms.PrincipalCents, dayIndex,
                    $"refund of failed construction loan draw for project '{record.ProjectId}'", diag);
                return $"NpcCommissioner.DrawConstructionLoan: the obligation could not be created after the {loanTerms.PrincipalCents}c principal moved"
                    + (refundProblem != null ? $" — and the refund ALSO failed: {refundProblem} The money is stuck and this must be reconciled by hand." : " — the principal was refunded to the lender.")
                    + " Nothing was drawn.";
            }
            var termStructure = new LoanTermStructure
            {
                principalCents = loanTerms.PrincipalCents,
                downPaymentCents = loanTerms.DownPaymentCents,
                annualInterestRateBps = loanTerms.AnnualInterestRateBps,
                termMonths = loanTerms.TermMonths,
                amortizationMonths = loanTerms.TermMonths,
                repaymentFrequency = loanTerms.RepaymentFrequency,
                originationFeeCents = loanTerms.OriginationFeeCents,
                balloonPaymentCents = 0,
            };
            LoanPaymentSchedule schedule = new PaymentScheduleBuilder().Build(
                obligation.ObligationId, termStructure,
                CalendarAnchor.Default.BuildDate(dayIndex), 7, CalendarAnchor.Default);
            if (schedule == null || schedule.payments == null || schedule.payments.Count == 0)
            {
                string refundProblem = ConstructionCashMover.MoveCash(cash, householdOwnerName,
                    loanTerms.LenderOwnerName, loanTerms.PrincipalCents, dayIndex,
                    $"refund of failed construction loan draw for project '{record.ProjectId}'", diag);
                return $"NpcCommissioner.DrawConstructionLoan: no repayment schedule could be built for the {loanTerms.PrincipalCents}c loan — a debt with no repayment plan is never drawn"
                    + (refundProblem != null ? $" — and the refund ALSO failed: {refundProblem} The money is stuck and this must be reconciled by hand." : " — the principal was refunded to the lender.");
            }
            paymentTerms.InstallmentPrincipalCents = schedule.payments[0].principalCents;
            obligation.FeesCents = loanTerms.OriginationFeeCents;
            obligation.MaturityDayIndex = schedule.payments[schedule.payments.Count - 1].dueDayIndex;
            SecurityInterestRecord security = null;
            if (!string.IsNullOrWhiteSpace(record.ParcelId))
            {
                security = obligations.AttachSecurity(ids, obligation.ObligationId,
                    loanTerms.LenderOwnerName, householdOwnerName,
                    new[] { "parcel:" + record.ParcelId }, dayIndex, 1, false);
                if (security == null)
                    diag.Add($"NpcCommissioner.DrawConstructionLoan: the first lien on parcel '{record.ParcelId}' could not be filed — the loan and obligation stand, but the security filing is missing and must be reconciled.");
            }
            loanTerms.DrawnDayIndex = dayIndex;
            loanTerms.ObligationId = obligation.ObligationId;
            loanTerms.SecurityInterestId = security != null ? security.SecurityInterestId : string.Empty;
            loanTerms.Schedule = schedule;
            record.Decisions.Add($"day {dayIndex}: construction loan drawn — {loanTerms.PrincipalCents}c from '{loanTerms.LenderOwnerName}' at {loanTerms.AnnualInterestRateBps} bps, ~{loanTerms.EstimatedPeriodicPaymentCents}c/{loanTerms.RepaymentFrequency} over {loanTerms.TermMonths} months, fee {loanTerms.OriginationFeeCents}c, first payment due day {schedule.payments[0].dueDayIndex} (obligation '{obligation.ObligationId}').");
            diag.Add($"NpcCommissioner.DrawConstructionLoan: drew {loanTerms.PrincipalCents}c for project '{record.ProjectId}' from '{loanTerms.LenderOwnerName}' (obligation '{obligation.ObligationId}', first payment due day {schedule.payments[0].dueDayIndex}).");
            return null;
        }

        /// <summary>
        /// D-4: services one drawn construction loan for one day — pays
        /// the next scheduled installment when it is due, through the real
        /// cash and debt authorities. The schedule is the calendar and the
        /// plan; the obligation authority is the single debt truth. The
        /// amount paid is the schedule's remaining due for that payment,
        /// capped at the obligation's outstanding (the final payment's
        /// rounding residual never overpays); interest is accrued through
        /// the day first, and the origination fee rides with the first
        /// payment (fees first). When the household cannot cover the full
        /// installment, NOTHING is paid — no partial installments in D-4 —
        /// the miss is recorded loudly (missed count, decision line, the
        /// obligation marked delinquent, the schedule payment left unpaid
        /// for retry on a later drive). Returns null when nothing was due
        /// or the payment succeeded; a problem string when a payment was
        /// due but could not be made (the driver logs it; the build
        /// continues — a missed payment never blocks the house).
        /// </summary>
        public string ServiceConstructionLoan(
            NpcConstructionRecord record,
            CreditCashBridge cash,
            FinancialObligationAuthority obligations,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (record == null || record.LoanTerms == null || !record.LoanTerms.IsDrawn)
                return null;
            NpcConstructionLoanTerms loanTerms = record.LoanTerms;
            if (cash == null || obligations == null)
                return $"NpcCommissioner.ServiceConstructionLoan: no cash bridge or obligation authority — the loan for project '{record.ProjectId}' cannot be serviced.";
            FinancialObligation obligation = obligations.Find(loanTerms.ObligationId);
            if (obligation == null)
                return $"NpcCommissioner.ServiceConstructionLoan: the loan obligation '{loanTerms.ObligationId}' for project '{record.ProjectId}' is gone — the loan cannot be serviced and the loss must be reconciled.";
            if (obligation.Settled) return null;
            LoanPaymentDue duePayment = null;
            if (loanTerms.Schedule != null && loanTerms.Schedule.payments != null)
            {
                foreach (LoanPaymentDue payment in loanTerms.Schedule.payments)
                {
                    if (payment == null || payment.status == LoanPaymentStatus.Paid) continue;
                    if (payment.dueDayIndex > dayIndex) break;
                    duePayment = payment;
                    break;
                }
            }
            if (duePayment == null) return null;
            string householdOwnerName = "household:" + record.HouseholdId;
            string lenderOwnerName = loanTerms.LenderOwnerName;
            string accrueProblem = obligations.AccrueInterestThroughDay(loanTerms.ObligationId, dayIndex);
            if (accrueProblem != null)
                return $"NpcCommissioner.ServiceConstructionLoan: interest could not be accrued for the loan on project '{record.ProjectId}' — {accrueProblem}";
            int amount = Math.Max(0, duePayment.totalDueCents - duePayment.paidCents);
            if (amount <= 0)
            {
                duePayment.status = LoanPaymentStatus.Paid;
                return null;
            }
            int outstanding = obligation.TotalOutstandingCents;
            if (amount > outstanding) amount = outstanding;
            IRealCashStore householdStore = cash.FindStore(householdOwnerName);
            if (householdStore == null)
                return MissedLoanPayment(record, loanTerms, obligations, obligation, duePayment, dayIndex,
                    $"'{householdOwnerName}' has no real cash store — the {amount}c installment has nowhere to come from.", diag);
            if (householdStore.ReadBalanceCents() < amount)
                return MissedLoanPayment(record, loanTerms, obligations, obligation, duePayment, dayIndex,
                    $"'{householdOwnerName}' holds {householdStore.ReadBalanceCents()}c, short of the {amount}c installment (payment {duePayment.paymentNumber}, due day {duePayment.dueDayIndex}).", diag);
            string moveProblem = ConstructionCashMover.MoveCash(cash, householdOwnerName,
                lenderOwnerName, amount, dayIndex,
                $"construction loan installment {duePayment.paymentNumber} for project '{record.ProjectId}'", diag);
            if (moveProblem != null)
                return MissedLoanPayment(record, loanTerms, obligations, obligation, duePayment, dayIndex,
                    $"the {amount}c installment could not move — {moveProblem}", diag);
            FinancialPaymentRecord applied = obligations.ApplyPayment(loanTerms.ObligationId,
                amount, dayIndex, householdOwnerName, lenderOwnerName,
                "construction-loan:" + record.RecordId,
                Math.Min(amount, Math.Max(0, obligation.FeesCents)));
            if (applied == null)
            {
                string refundProblem = ConstructionCashMover.MoveCash(cash, lenderOwnerName,
                    householdOwnerName, amount, dayIndex,
                    $"refund of unapplied construction loan installment for project '{record.ProjectId}'", diag);
                return $"NpcCommissioner.ServiceConstructionLoan: the {amount}c installment moved but the debt authority would not apply it"
                    + (refundProblem != null ? $" — and the refund ALSO failed: {refundProblem} The money is stuck and this must be reconciled by hand." : " — the installment was refunded to the household.")
                    + " Nothing was paid.";
            }
            duePayment.paidCents += applied.AmountCents;
            duePayment.status = obligation.Settled || duePayment.paidCents >= duePayment.totalDueCents
                ? LoanPaymentStatus.Paid
                : LoanPaymentStatus.PartiallyPaid;
            loanTerms.LastServicedDayIndex = dayIndex;
            if (obligation.Settled)
            {
                record.Decisions.Add($"day {dayIndex}: construction loan for project '{record.ProjectId}' SETTLED — final installment {applied.AmountCents}c paid (obligation '{obligation.ObligationId}').");
                diag.Add($"NpcCommissioner.ServiceConstructionLoan: loan for project '{record.ProjectId}' settled on day {dayIndex} (final installment {applied.AmountCents}c).");
            }
            else
            {
                diag.Add($"NpcCommissioner.ServiceConstructionLoan: paid installment {duePayment.paymentNumber} ({applied.AmountCents}c) for project '{record.ProjectId}' — {obligation.TotalOutstandingCents}c outstanding.");
            }
            return null;
        }

        private string MissedLoanPayment(
            NpcConstructionRecord record,
            NpcConstructionLoanTerms loanTerms,
            FinancialObligationAuthority obligations,
            FinancialObligation obligation,
            LoanPaymentDue duePayment,
            int dayIndex,
            string reason,
            List<string> diag)
        {
            loanTerms.MissedPayments++;
            loanTerms.LastMissedDayIndex = dayIndex;
            duePayment.status = LoanPaymentStatus.Late;
            if (obligations != null && obligation != null)
                obligations.MarkDelinquent(obligation.ObligationId);
            string problem = $"NpcCommissioner.ServiceConstructionLoan: MISSED installment {duePayment.paymentNumber} for project '{record.ProjectId}' — {reason} Missed count is now {loanTerms.MissedPayments}; the payment stays due and is retried on a later drive.";
            record.Decisions.Add($"day {dayIndex}: MISSED construction loan installment {duePayment.paymentNumber} ({Math.Max(0, duePayment.totalDueCents - duePayment.paidCents)}c due, day {duePayment.dueDayIndex}) — {reason}");
            diag.Add(problem);
            return problem;
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
            RecordHiredCommit(record, package, workerPersonId, relationship.Id, durationMinutes);
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            if (funding != null)
                funding.Notes = $"wages to household:{workerHouseholdId} at {agreedWeeklyWageCents}c/week pro-rated";
            record.Decisions.Add($"day {dayIndex}: hired P{workerPersonId} (employment '{relationship.Id}', {agreedWeeklyWageCents}c/week) for '{package.PhaseName}'.");
            diag.Add($"NpcCommissioner: P{workerPersonId} hired for '{package.PackageId}' ({agreedWeeklyWageCents}c/week, employment '{relationship.Id}').");
            return null;
        }

        /// <summary>
        /// D-3: the funding-line bookkeeping shared by HireWorker (new
        /// employment) and CommitHiredLabor (existing employment): the
        /// package's labour kind, worker list, employment list and
        /// committed minutes. One bookkeeping authority — the two paths
        /// never drift apart. The wage/payee note stays with HireWorker,
        /// which is where the wage is agreed.
        /// </summary>
        private static void RecordHiredCommit(NpcConstructionRecord record, WorkPackage package,
            int workerPersonId, string employmentId, int durationMinutes)
        {
            if (record == null || package == null) return;
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            if (funding == null) return;
            funding.LaborKind = NpcBuildLaborKind.HiredWorker;
            if (!funding.WorkerPersonIds.Contains(workerPersonId)) funding.WorkerPersonIds.Add(workerPersonId);
            if (!string.IsNullOrWhiteSpace(employmentId) && !funding.EmploymentIds.Contains(employmentId))
                funding.EmploymentIds.Add(employmentId);
            funding.WorkerMinutesCommitted += WorkTimeMath.ClampToWholeMinutes(durationMinutes);
        }

        /// <summary>
        /// D-3: commits more time for a worker ALREADY hired on this
        /// package, under their existing employment — never registers a
        /// duplicate employment for the same worker and package. Used by
        /// the hired-labour fallback when a package needs more than one
        /// day of hired work. Wages are still paid once, on package
        /// completion, via <see cref="PayWorkerForPackage"/>.
        /// </summary>
        public string CommitHiredLabor(
            EmploymentRelationshipRegistry employments,
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            NpcConstructionRecord record,
            WorkPackage package,
            string employmentId,
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
            if (employments == null || executor == null || project == null || record == null || package == null
                || string.IsNullOrWhiteSpace(employmentId))
                return "NpcCommissioner.CommitHiredLabor: employments, executor, project, record, package and employment id required.";
            if (!employments.TryGetById(employmentId, out EmploymentRelationship relationship) || relationship == null)
                return $"NpcCommissioner.CommitHiredLabor: employment '{employmentId}' unknown — cannot commit time under it.";
            if (!relationship.IsActive)
                return $"NpcCommissioner.CommitHiredLabor: employment '{employmentId}' is not active — hire again instead of committing under an ended employment.";
            string commitProblem = executor.CommitLabor(project, package, relationship.EmployeePersonId, tracker, custody, tasks, budgets,
                dayIndex, startMinuteOfDay, durationMinutes, $"hired labor {employmentId}", diag);
            if (commitProblem != null)
            {
                diag.Add($"NpcCommissioner.CommitHiredLabor: P{relationship.EmployeePersonId} (employment '{employmentId}') time refused: {commitProblem}");
                return commitProblem;
            }
            RecordHiredCommit(record, package, relationship.EmployeePersonId, employmentId, durationMinutes);
            record.Decisions.Add($"day {dayIndex}: P{relationship.EmployeePersonId} (employment '{employmentId}') committed {durationMinutes} min more to '{package.PhaseName}'.");
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
                // D-4: the loan survives a save — terms, schedule and
                // missed-payment counts are copied, never shared with the
                // DTO's record objects.
                copy.LoanTerms = CopyLoanTerms(record.LoanTerms);
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

        /// <summary>D-4: deep copy of one loan's terms, including its repayment schedule. Null in, null out.</summary>
        private static NpcConstructionLoanTerms CopyLoanTerms(NpcConstructionLoanTerms source)
        {
            if (source == null) return null;
            var copy = new NpcConstructionLoanTerms
            {
                AgreementId = source.AgreementId ?? string.Empty,
                LenderId = source.LenderId ?? string.Empty,
                LenderOwnerName = source.LenderOwnerName ?? string.Empty,
                Purpose = source.Purpose,
                Decision = source.Decision,
                HasBlockingReadinessGap = source.HasBlockingReadinessGap,
                EvaluatedPriceCents = source.EvaluatedPriceCents,
                PrincipalCents = source.PrincipalCents,
                DownPaymentCents = source.DownPaymentCents,
                AnnualInterestRateBps = source.AnnualInterestRateBps,
                TermMonths = source.TermMonths,
                RepaymentFrequency = source.RepaymentFrequency,
                EstimatedPeriodicPaymentCents = source.EstimatedPeriodicPaymentCents,
                OriginationFeeCents = source.OriginationFeeCents,
                CollateralId = source.CollateralId ?? string.Empty,
                CollateralKind = source.CollateralKind,
                CollateralValueCents = source.CollateralValueCents,
                DrawnDayIndex = source.DrawnDayIndex,
                ObligationId = source.ObligationId ?? string.Empty,
                SecurityInterestId = source.SecurityInterestId ?? string.Empty,
                MissedPayments = source.MissedPayments,
                LastMissedDayIndex = source.LastMissedDayIndex,
                LastServicedDayIndex = source.LastServicedDayIndex,
            };
            if (source.Conditions != null) copy.Conditions.AddRange(source.Conditions);
            if (source.ReasonCodes != null) copy.ReasonCodes.AddRange(source.ReasonCodes);
            if (source.Schedule != null)
            {
                var schedule = new LoanPaymentSchedule
                {
                    loanId = source.Schedule.loanId ?? string.Empty,
                    principalCents = source.Schedule.principalCents,
                    totalPrincipalCents = source.Schedule.totalPrincipalCents,
                    totalInterestCents = source.Schedule.totalInterestCents,
                    totalPaymentCents = source.Schedule.totalPaymentCents,
                    originationFeeCents = source.Schedule.originationFeeCents,
                };
                if (source.Schedule.payments != null)
                {
                    foreach (LoanPaymentDue payment in source.Schedule.payments)
                    {
                        if (payment == null) continue;
                        schedule.payments.Add(new LoanPaymentDue
                        {
                            paymentNumber = payment.paymentNumber,
                            dueDate = payment.dueDate,
                            dueDayIndex = payment.dueDayIndex,
                            principalCents = payment.principalCents,
                            interestCents = payment.interestCents,
                            totalDueCents = payment.totalDueCents,
                            paidCents = payment.paidCents,
                            status = payment.status,
                        });
                    }
                }
                copy.Schedule = schedule;
            }
            return copy;
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
