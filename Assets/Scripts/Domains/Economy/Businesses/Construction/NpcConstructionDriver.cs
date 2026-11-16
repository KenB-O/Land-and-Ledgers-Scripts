using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Pass XI WS2: one explicit NPC construction commission — the trigger
    /// a caller supplies, never an automatic quota. A project is commissioned
    /// only from a named household, a catalog design and a parcel the parcel
    /// authority authorizes. The commissioning trigger rule itself (which
    /// households commission, on what evidence) is a design decision owned
    /// outside this driver: the driver never invents a commission.
    /// </summary>
    public sealed class NpcConstructionRequest
    {
        public int HouseholdId = -1;
        public string DesignId = string.Empty;
        public string ParcelId = string.Empty;
        public List<int> MemberPersonIds = new List<int>();
        public AccommodationArrangement Arrangement = AccommodationArrangement.OwnerOccupied;
        /// <summary>
        /// DEF-011 (Decision 7): the legitimate reason for the build. A
        /// commission with Unspecified is refused — the driver never builds
        /// for no stated reason.
        /// </summary>
        public NpcConstructionReason Reason = NpcConstructionReason.Unspecified;
        /// <summary>Free-text evidence for the reason (the gap, the shared building, ...).</summary>
        public string ReasonNotes = string.Empty;
    }

    /// <summary>
    /// DEF-011 (Decision 7): the legitimate reasons a household or business
    /// may initiate construction. BusinessExpansion, Replacement, Relocation
    /// and NewEnterprise have no real signals in the tree yet, so the need
    /// evaluator does not auto-detect them — they are available as explicit
    /// requests with a stated reason.
    /// </summary>
    public enum NpcConstructionReason
    {
        Unspecified = 0,
        Overcrowding = 1,
        HouseholdFormation = 2,
        BusinessExpansion = 3,
        Replacement = 4,
        Relocation = 5,
        NewEnterprise = 6,
    }

    /// <summary>
    /// Pass XI WS2: the labor leg of one package, through the real task
    /// authorities. An implementation commits and works the package's full
    /// required labor (never credits minutes not worked) and returns null
    /// on success or a refusal. The driver never fakes labor minutes itself.
    /// </summary>
    public interface INpcConstructionLaborPort
    {
        string WorkPackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag);
    }

    /// <summary>
    /// Pass XI WS2: the full material leg of one package, through the real
    /// supplier chain (discovery, sale, freight, site staging). When this
    /// port is bound, the driver procures through it instead of staging
    /// directly from a material source. The plan factory supplies the
    /// freight plan for the package (wagon, crew, charge); the stockpile
    /// is transient within one drive step, so no units sit unpersisted
    /// across a save boundary.
    /// </summary>
    public interface INpcConstructionProcurementPort
    {
        string ProcureAndStage(ConstructionProject project, WorkPackage package,
            NpcConstructionRecord record, int dayIndex, List<string> diag);
    }

    /// <summary>
    /// Pass XI WS2: the production driver for NPC construction (audit
    /// finding A-03, RUN-002). Before this driver, the construction
    /// commissioner and executor had zero production callers: projects
    /// could be built in fixtures only. The driver advances each hub
    /// project by at most one step per day, in package sequence order:
    /// fund and stage materials from real lots (or procure through the
    /// real supplier chain when that port is bound), work real labor,
    /// advance the package, then complete the project into a registered
    /// building and occupy it with the commissioning household. A refusal
    /// at any step leaves the project exactly where it was and stops that
    /// project for the day: no progress without work, no free materials,
    /// no double staging after load (staging runs only from Ready, and
    /// staged lines persist on the project DTO). Paused projects are
    /// skipped, never auto-resumed: resuming is an explicit NPC decision
    /// through the commissioner. Cancelled and completed projects are
    /// skipped. Commissioning runs only from explicit requests queued by
    /// the caller; the driver never commissions on its own.
    /// </summary>
    public sealed class NpcConstructionDriver
    {
        private readonly NpcConstructionCommissioner commissioner;
        private readonly ConstructionProjectExecutor executor;
        private BuildingDesignCatalog catalog;
        private readonly IList<ConstructionProject> projects;
        private readonly CreditCashBridge cash;
        private readonly FinancialObligationAuthority obligations;
        private readonly EntityIdRegistry ids;
        private readonly HousingAuthority housing;
        private IConstructionMaterialPricePort pricePort;
        private IConstructionMaterialSource materialSource;
        private string defaultSupplierOwnerName;
        private INpcConstructionProcurementPort procurement;
        private INpcConstructionLaborPort labor;
        private Func<int, IReadOnlyList<int>> householdMembers;
        private readonly List<string> diagnostics = new List<string>();
        /// <summary>
        /// Packages whose procurement leg failed: the sale may have stood
        /// with the paid-for lot sitting with the supplier, or the
        /// procurement may have been refused before anything was sold (for
        /// example a zero-material refusal, where nothing was paid).
        /// Either way a blind retry is blocked — it could buy the materials
        /// a second time, or re-attempt a refused procurement. The recorded
        /// StrandedReason on the funding line names the actual failure. A
        /// blocked package waits until <see cref="AllowProcurementRetry"/>
        /// is called explicitly, so the strand is reconciled by an operator
        /// decision, never silently double-bought or silently retried.
        /// </summary>
        private readonly HashSet<string> blockedProcurementPackages = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// CAMPAIGN-C (DEF-020): first-seen days + once-per-day summary for
        /// stranded procurement packages. Diagnostic only; the driver never
        /// retries, cancels, or escalates a strand on its own.
        /// </summary>
        private readonly ConstructionProcurementStrandSummary strandSummary = new ConstructionProcurementStrandSummary();

        /// <summary>
        /// Plan 26 (Q30b): the procurement retry-block keys, for diagnostics
        /// and tests. Keys are "projectId|packageId". The set is the live
        /// authority within a session; its persisted mirror is the Stranded
        /// marker on the commissioner's funding lines.
        /// </summary>
        public IReadOnlyCollection<string> BlockedProcurementPackageKeys => blockedProcurementPackages;

        /// <summary>Projects the driver advances. Owned by the hub; the driver never replaces the list.</summary>
        public IList<ConstructionProject> Projects => projects;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Explicit commission requests waiting to be commissioned. Filled only by the caller.</summary>
        public List<NpcConstructionRequest> PendingRequests { get; } = new List<NpcConstructionRequest>();

        /// <summary>
        /// Optional per-household material source resolver (a yard runtime
        /// for one household's project, a household-inventory source for
        /// another). When set it wins over the single material source for
        /// that project's household; when unset the single source is used.
        /// </summary>
        public Func<int, IConstructionMaterialSource> MaterialSourceByHousehold { get; set; }

        /// <summary>
        /// DEF-011: the single-trip shipment bound in material units, when
        /// the caller can supply it (computed from the real wagon's mass and
        /// volume capacities and the cargo spec — never hardcoded here; the
        /// construction layer does not own the wagon). Null means no bound
        /// was supplied: the commission-time shipment check is skipped and
        /// the skip is recorded on the evaluation, while the shipment
        /// service still refuses over-capacity loads pre-load.
        /// </summary>
        public int? MaxUnitsPerTrip { get; set; }

        /// <summary>
        /// D-1 (F-M1): derives the commission-time single-trip bound from
        /// the real wagon and the cargo spec, so the commission-time
        /// shipment check evaluates against real capacity instead of being
        /// skipped. The wagon and the spec are the caller's — the
        /// construction layer does not own the wagon and never hardcodes
        /// the bound here. In production the caller passes the
        /// opening-freight-wagon and the lumber spec (28 units per trip
        /// at 450 kg / 4 m3). A null wagon or null spec means no bound
        /// can be computed, so the bound stays null and the check is
        /// skipped and recorded, as before. A wagon that cannot carry
        /// the cargo at all yields 0, and the check then refuses the
        /// design as unshippable. The shipment service remains the
        /// backstop: it refuses over-capacity loads pre-load regardless
        /// of this bound.
        /// </summary>
        public void BindTripBoundFromWagon(EquipmentAsset wagon, FreightCargoSpec cargo)
        {
            MaxUnitsPerTrip = wagon == null || cargo == null
                ? (int?)null
                : FreightCargoCapacity.MaxUnitsPerTrip(cargo, wagon);
        }

        public NpcConstructionDriver(
            NpcConstructionCommissioner commissioner,
            ConstructionProjectExecutor executor,
            BuildingDesignCatalog catalog,
            IList<ConstructionProject> projects,
            CreditCashBridge cash,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            HousingAuthority housing,
            IConstructionMaterialPricePort pricePort,
            IConstructionMaterialSource materialSource,
            string defaultSupplierOwnerName,
            INpcConstructionProcurementPort procurement,
            INpcConstructionLaborPort labor,
            Func<int, IReadOnlyList<int>> householdMembers)
        {
            this.commissioner = commissioner;
            this.executor = executor;
            this.catalog = catalog;
            this.projects = projects;
            this.cash = cash;
            this.obligations = obligations;
            this.ids = ids;
            this.housing = housing;
            this.pricePort = pricePort;
            this.materialSource = materialSource;
            this.defaultSupplierOwnerName = defaultSupplierOwnerName;
            this.procurement = procurement;
            this.labor = labor;
            this.householdMembers = householdMembers;
            DeriveBlockedProcurementPackages();
        }

        /// <summary>
        /// Refreshes runtime adapters while retaining this driver's project,
        /// pending-request, and failed-procurement state across daily drives.
        /// </summary>
        public void ConfigureRuntimePorts(BuildingDesignCatalog designCatalog,
            IConstructionMaterialPricePort prices, IConstructionMaterialSource materials,
            string supplierOwnerName, INpcConstructionProcurementPort procurementPort,
            INpcConstructionLaborPort laborPort, Func<int, IReadOnlyList<int>> members)
        {
            catalog = designCatalog;
            pricePort = prices;
            materialSource = materials;
            defaultSupplierOwnerName = supplierOwnerName;
            procurement = procurementPort;
            labor = laborPort;
            householdMembers = members;
        }

        /// <summary>
        /// Commissions one explicit request: the request must state a
        /// legitimate reason; the design must exist, sleep the household
        /// and be fundable from the household's real cash (the evaluation
        /// records affordability, benefit, alternatives and shipment
        /// feasibility on the record); a real material supplier and at
        /// least one active household member must exist; the parcel
        /// authority must authorize the build; only then is the project
        /// created and added to the project list. Returns null on refusal
        /// (with the reason in diagnostics).
        /// </summary>
        public ConstructionProject Commission(NpcConstructionRequest request,
            IConstructionParcelAuthority parcelAuthority, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (request == null)
            {
                diag.Add("NpcConstructionDriver.Commission: no request — commissions are never invented.");
                return null;
            }
            if (commissioner == null || executor == null)
            {
                diag.Add("NpcConstructionDriver.Commission: commissioner and executor required.");
                return null;
            }
            if (catalog == null)
            {
                diag.Add("NpcConstructionDriver.Commission: no design catalog bound — nothing can be commissioned.");
                return null;
            }
            if (parcelAuthority == null)
            {
                diag.Add("NpcConstructionDriver.Commission: parcel authorization required — a project is never created on an unauthorized parcel.");
                return null;
            }
            if (request.HouseholdId < 0 || string.IsNullOrWhiteSpace(request.DesignId)
                || string.IsNullOrWhiteSpace(request.ParcelId))
            {
                diag.Add("NpcConstructionDriver.Commission: a commission needs a real household, design and parcel.");
                return null;
            }
            string buyerOwnerName = "household:" + request.HouseholdId;
            IRealCashStore buyerStore = cash != null ? cash.FindStore(buyerOwnerName) : null;
            if (buyerStore == null)
            {
                diag.Add($"NpcConstructionDriver.Commission: '{buyerOwnerName}' has no real cash store — funding cannot be checked.");
                return null;
            }
            if (request.Reason == NpcConstructionReason.Unspecified)
            {
                diag.Add($"NpcConstructionDriver.Commission: H{request.HouseholdId} gave no reason — a commission needs a legitimate reason (overcrowding, household formation, business expansion, replacement, relocation, new enterprise).");
                return null;
            }
            int members = request.MemberPersonIds != null ? request.MemberPersonIds.Count : 0;
            NpcConstructionEvaluation evaluation = commissioner.EvaluateCommission(catalog, request.DesignId,
                Math.Max(1, members), pricePort, buyerStore.ReadBalanceCents(), MaxUnitsPerTrip,
                request.Reason, request.ReasonNotes, dayIndex, diag);
            if (evaluation == null || !evaluation.IsViable)
            {
                diag.Add($"NpcConstructionDriver.Commission: design '{request.DesignId}' refused for H{request.HouseholdId} — see the evaluation refusal above.");
                return null;
            }
            IConstructionMaterialSource householdSource = MaterialSourceByHousehold != null
                ? MaterialSourceByHousehold(request.HouseholdId) : null;
            if (procurement == null && materialSource == null && householdSource == null
                && string.IsNullOrWhiteSpace(defaultSupplierOwnerName))
            {
                diag.Add($"NpcConstructionDriver.Commission: H{request.HouseholdId} has no material supplier — no procurement port, no material source, no per-household source, no named supplier owner. A commission needs a real supplier before it starts.");
                return null;
            }
            IReadOnlyList<int> householdMemberIds = householdMembers != null
                ? householdMembers(request.HouseholdId) : null;
            if (householdMemberIds == null || householdMemberIds.Count == 0)
            {
                diag.Add($"NpcConstructionDriver.Commission: H{request.HouseholdId} has no active members — nobody to work the build.");
                return null;
            }
            string authProblem = parcelAuthority.AuthorizeBuild(request.HouseholdId, request.ParcelId, dayIndex, diag);
            if (authProblem != null)
            {
                diag.Add($"NpcConstructionDriver.Commission: parcel '{request.ParcelId}' refused for H{request.HouseholdId}: {authProblem}");
                return null;
            }
            NpcConstructionRecord record;
            ConstructionProject project = commissioner.CommissionProject(executor, catalog,
                request.HouseholdId, request.DesignId, request.ParcelId, pricePort, dayIndex, diag, out record);
            if (project == null || record == null)
            {
                diag.Add($"NpcConstructionDriver.Commission: commissioning '{request.DesignId}' for H{request.HouseholdId} failed — see diagnostics.");
                return null;
            }
            record.Reason = request.Reason;
            record.ReasonNotes = request.ReasonNotes ?? string.Empty;
            record.EvaluationSummary = evaluation.Summary ?? string.Empty;
            if (evaluation.Lines != null && evaluation.Lines.Count > 0)
                record.Decisions.InsertRange(0, evaluation.Lines);
            if (projects != null) projects.Add(project);
            diag.Add($"NpcConstructionDriver: H{request.HouseholdId} commissioned '{request.DesignId}' on parcel '{request.ParcelId}' (project '{project.ProjectId}').");
            return project;
        }

        /// <summary>Commissions every queued request, in order. Refused requests stay queued with their reason in diagnostics.</summary>
        public void CommissionPending(IConstructionParcelAuthority parcelAuthority, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var done = new List<NpcConstructionRequest>();
            foreach (NpcConstructionRequest request in PendingRequests)
            {
                if (request == null) { done.Add(request); continue; }
                if (Commission(request, parcelAuthority, dayIndex, diag) != null) done.Add(request);
            }
            foreach (NpcConstructionRequest request in done) PendingRequests.Remove(request);
        }

        /// <summary>
        /// Advances every project by at most one step. Returns the number
        /// of projects that moved forward this drive. Diagnostics name
        /// every project that waited and why.
        /// </summary>
        public int Drive(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (commissioner == null || executor == null || projects == null)
            {
                diag.Add("NpcConstructionDriver.Drive: commissioner, executor and project list required.");
                return 0;
            }
            DeriveBlockedProcurementPackages();
            int advanced = 0;
            var snapshot = new List<ConstructionProject>(projects);
            foreach (ConstructionProject project in snapshot)
            {
                if (project == null) continue;
                if (project.Status == ConstructionProjectStatus.Cancelled
                    || project.Status == ConstructionProjectStatus.Complete)
                {
                    if (project.Status == ConstructionProjectStatus.Complete
                        && TryOccupy(project, dayIndex, diag)) advanced++;
                    continue;
                }
                if (project.Status == ConstructionProjectStatus.Paused)
                {
                    diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' is paused — funding halts until the NPC resumes it explicitly.");
                    continue;
                }
                if (DriveProject(project, dayIndex, diag)) advanced++;
            }
            // CAMPAIGN-C (DEF-020): diagnostic slice only — one day-keyed
            // summary line naming every stranded procurement package
            // (identity + days stranded + recorded reason) into the daily
            // diag dump. No retry, no cancellation, no escalation: owner
            // question (r) owns the real fix.
            if (strandSummary.TryBuildSummaryLine(blockedProcurementPackages, StrandedReasonForKey, dayIndex, out string strandedLine))
                diag.Add(strandedLine);
            return advanced;
        }

        private bool DriveProject(ConstructionProject project, int dayIndex, List<string> diag)
        {
            WorkPackage next = NextIncompletePackage(project);
            if (next == null)
            {
                if (project.WorkPackages == null || project.WorkPackages.Count == 0)
                {
                    diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' has no work packages — commissioning never completed its packages.");
                    return false;
                }
                return CompleteAndOccupy(project, dayIndex, diag);
            }
            if (next.Status == WorkPackageStatus.Ready)
            {
                string problem = FundAndStage(project, next, dayIndex, diag);
                if (problem != null)
                {
                    diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' package '{next.PackageId}' waits — {problem}");
                    return false;
                }
                return true;
            }
            if (next.Status == WorkPackageStatus.MaterialsStaged || next.Status == WorkPackageStatus.InProgress)
            {
                if (next.LaborMinutesCommitted < next.LaborMinutesRequired)
                {
                    if (labor == null)
                    {
                        diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' package '{next.PackageId}' waits for labor — no labor port bound.");
                        return false;
                    }
                    string laborProblem = labor.WorkPackage(project, next, dayIndex, diag);
                    if (laborProblem != null)
                    {
                        diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' package '{next.PackageId}' labor refused — {laborProblem}");
                        return false;
                    }
                }
                string advanceProblem = executor.AdvancePackage(project, next, dayIndex, diag);
                if (advanceProblem != null)
                {
                    diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' package '{next.PackageId}' cannot advance — {advanceProblem}");
                    return false;
                }
                return true;
            }
            diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' package '{next.PackageId}' is {next.Status} — no drive step applies.");
            return false;
        }

        /// <summary>
        /// Plan 26 (Q30b, note 102 C3/C4): rebuilds the in-memory retry-block
        /// set from the persisted stranded markers on the commissioner's
        /// funding lines. The commissioner exposes no all-records accessor,
        /// so the derive enumerates the driver's live projects and resolves
        /// each record via FindRecordForProject. Records whose project is no
        /// longer live in the driver are not scanned — a block only matters
        /// while the project is live. Idempotent: the set is cleared and
        /// rebuilt on every call. Called at construction and at the top of
        /// every Drive (the driver instance survives a Continue, so a
        /// construction-only restore would never run after a load — the
        /// note-101 Finding 1 shape).
        /// </summary>
        private void DeriveBlockedProcurementPackages()
        {
            blockedProcurementPackages.Clear();
            if (commissioner == null || projects == null) return;
            foreach (ConstructionProject project in projects)
            {
                if (project == null) continue;
                NpcConstructionRecord record = commissioner.FindRecordForProject(project.ProjectId);
                if (record == null || record.PackageFundings == null) continue;
                foreach (NpcBuildPackageFunding funding in record.PackageFundings)
                {
                    if (funding != null && funding.Stranded
                        && !string.IsNullOrWhiteSpace(funding.PackageId))
                        blockedProcurementPackages.Add(project.ProjectId + "|" + funding.PackageId);
                }
            }
        }

        /// <summary>Clears a procurement retry block for one package (explicit operator decision).</summary>
        public void AllowProcurementRetry(ConstructionProject project, WorkPackage package)
        {
            if (project == null || package == null) return;
            blockedProcurementPackages.Remove(project.ProjectId + "|" + package.PackageId);
            if (commissioner != null)
            {
                NpcConstructionRecord record = commissioner.FindRecordForProject(project.ProjectId);
                NpcBuildPackageFunding funding = record != null ? record.FindFunding(package.PackageId) : null;
                if (funding != null)
                {
                    funding.Stranded = false;
                    funding.StrandedReason = string.Empty;
                }
            }
        }

        /// <summary>
        /// CAMPAIGN-C (DEF-020): the recorded failure reason for one blocked
        /// package key ("projectId|packageId"), or empty when the record or
        /// the funding line is gone. Read-only: never mutates the block set
        /// or the funding lines.
        /// </summary>
        private string StrandedReasonForKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            int sep = key.IndexOf('|');
            if (sep < 0) return string.Empty;
            NpcConstructionRecord record = commissioner != null
                ? commissioner.FindRecordForProject(key.Substring(0, sep))
                : null;
            NpcBuildPackageFunding funding = record != null
                ? record.FindFunding(key.Substring(sep + 1))
                : null;
            return funding != null && funding.StrandedReason != null
                ? funding.StrandedReason
                : string.Empty;
        }

        private string FundAndStage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
        {
            NpcConstructionRecord record = commissioner.FindRecordForProject(project.ProjectId);
            if (record == null)
                return $"no commissioner record for project '{project.ProjectId}' — funding cannot be booked.";
            string buyerOwnerName = "household:" + project.HouseholdId;
            if (procurement != null)
            {
                string key = project.ProjectId + "|" + package.PackageId;
                if (blockedProcurementPackages.Contains(key))
                {
                    // D-1 (F-M2): name the recorded failure, never assert a
                    // paid-for lot — the procurement may have been refused
                    // before anything was sold (a zero-material refusal pays
                    // nothing), and the old text was wrong for exactly that
                    // case. The funding line's StrandedReason is the record.
                    NpcBuildPackageFunding blockedFunding = record.FindFunding(package.PackageId);
                    string recordedReason = blockedFunding != null
                        && !string.IsNullOrWhiteSpace(blockedFunding.StrandedReason)
                        ? blockedFunding.StrandedReason
                        : "the procurement leg failed and no reason was recorded";
                    return $"procurement previously failed for package '{package.PackageId}' — {recordedReason}; retry is blocked until AllowProcurementRetry is called.";
                }
                string problem = procurement.ProcureAndStage(project, package, record, dayIndex, diag);
                if (problem != null)
                {
                    NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
                    if (funding != null)
                    {
                        funding.Stranded = true;
                        funding.StrandedReason = problem;
                    }
                    blockedProcurementPackages.Add(key);
                }
                return problem;
            }
            IConstructionMaterialSource source = MaterialSourceByHousehold != null
                ? MaterialSourceByHousehold(project.HouseholdId) : null;
            if (source == null) source = materialSource;
            if (source == null)
                return "no material source and no procurement port bound — materials come from real lots only.";
            if (source is IAlreadyOwnedConstructionMaterialSource)
            {
                // These lots are already in the commissioning household's custody.
                // Consume them at the authorized stage without charging a second
                // supplier payment. HouseholdInventory does not expose original
                // unit-cost basis, so do not invent an accounting amount.
                string ownedStockProblem = executor.StagePackageMaterials(
                    project, package, source, dayIndex, diag);
                if (ownedStockProblem != null) return ownedStockProblem;
                NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
                if (funding != null)
                    funding.Notes = "Staged from materials already owned by the commissioning household; prior acquisition cost basis is not exposed by household inventory.";
                record.Decisions.Add($"day {dayIndex}: staged '{package.PhaseName}' from household-owned lots; no new supplier payment was made.");
                diag.Add($"NpcConstructionDriver: package '{package.PackageId}' staged from household-owned inventory; no duplicate supplier payment.");
                return null;
            }
            return commissioner.FundAndStagePackage(executor, project, record, package,
                source, pricePort, cash, buyerOwnerName, defaultSupplierOwnerName,
                obligations, ids, dayIndex, diag);
        }

        private bool CompleteAndOccupy(ConstructionProject project, int dayIndex, List<string> diag)
        {
            if (catalog == null)
            {
                diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' has all packages complete but no catalog is bound — building registration waits.");
                return false;
            }
            BuildingDesign design = catalog.FindDesign(project.DesignId);
            if (design == null)
            {
                diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' names unknown design '{project.DesignId}' — completion refused.");
                return false;
            }
            if (housing == null)
            {
                diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' cannot complete — no housing authority.");
                return false;
            }
            string problem = executor.CompleteProject(project, design, housing, dayIndex, diag);
            if (problem != null)
            {
                diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' completion refused — {problem}");
                return false;
            }
            TryOccupy(project, dayIndex, diag);
            return true;
        }

        /// <summary>
        /// Occupies the completed home with the commissioning household's
        /// members, once: if every supplied member already occupies the new
        /// building, nothing is recorded twice. Returns true when an
        /// occupancy was newly recorded.
        /// </summary>
        private bool TryOccupy(ConstructionProject project, int dayIndex, List<string> diag)
        {
            if (housing == null || string.IsNullOrWhiteSpace(project.BuildingId)) return false;
            IReadOnlyList<int> members = householdMembers != null
                ? householdMembers(project.HouseholdId) : null;
            if (members == null || members.Count == 0) return false;
            var spaceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccommodationSpace space in housing.SpacesForBuilding(project.BuildingId))
                if (space != null) spaceIds.Add(space.SpaceId);
            bool alreadyThere = false;
            var toPlace = new List<int>();
            foreach (int personId in members)
            {
                if (personId < 0) continue;
                bool placed = false;
                foreach (ResidentialOccupancy occ in housing.CurrentOccupanciesForPerson(personId))
                {
                    if (occ != null && spaceIds.Contains(occ.SpaceId)) { placed = true; break; }
                }
                if (placed) alreadyThere = true;
                else toPlace.Add(personId);
            }
            if (toPlace.Count == 0) return false;
            string problem = executor.OccupyCompletedHome(project, housing, toPlace,
                AccommodationArrangement.OwnerOccupied, dayIndex, diag);
            if (problem != null)
            {
                diag.Add($"NpcConstructionDriver: project '{project.ProjectId}' occupancy refused — {problem}");
                return false;
            }
            return true;
        }

        private static WorkPackage NextIncompletePackage(ConstructionProject project)
        {
            if (project.WorkPackages == null) return null;
            var ordered = new List<WorkPackage>(project.WorkPackages);
            ordered.Sort((a, b) => (a != null ? a.Sequence : 0).CompareTo(b != null ? b.Sequence : 0));
            foreach (WorkPackage package in ordered)
                if (package != null && package.Status != WorkPackageStatus.Complete) return package;
            return null;
        }

        /// <summary>
        /// Cancels a project through the executor: staged materials return
        /// to the source exactly once through the restock port, completed
        /// packages stay built. A second cancel is refused by the executor.
        /// DEF-071 (Decision 14, D3): when an employment registry and a
        /// worker-household map are supplied, hired wages for incomplete
        /// packages are settled FIRST (paid from real cash, or recorded as
        /// a debt when the household cannot cover them) and the employments
        /// ended — before the executor fails the labour tasks. Without a
        /// registry the wage settlement cannot run; that is recorded loudly
        /// and the material cancel proceeds (the commissioner can settle
        /// them when a registry is available). The optional parameters keep
        /// existing callers compiling unchanged.
        /// </summary>
        public string Cancel(ConstructionProject project, IConstructionMaterialRestock restock,
            PersonScheduleTracker scheduleTracker, TaskAuthority tasks,
            WorkTimeBudgetStore budgets, int dayIndex, List<string> diag,
            EmploymentRelationshipRegistry employments = null,
            Dictionary<int, int> workerHouseholdByPerson = null)
        {
            diag = diag ?? diagnostics;
            if (executor == null || project == null)
                return "NpcConstructionDriver.Cancel: executor and project required.";
            // DEF-071 (Decision 14, D3): refuse a second cancel BEFORE
            // settling wages — otherwise a re-cancel would re-settle wages
            // before the executor refuses it. (The WagesPaidCents == 0 gate
            // in SettleCancellationWages is the backstop.)
            if (project.Status == ConstructionProjectStatus.Cancelled)
                return $"NpcConstructionDriver.Cancel: project '{project.ProjectId}' is already cancelled — nothing to cancel.";
            if (employments != null && workerHouseholdByPerson != null && commissioner != null)
            {
                NpcConstructionRecord record = commissioner.FindRecordForProject(project.ProjectId);
                if (record != null)
                {
                    string wageProblem = commissioner.SettleCancellationWages(employments, obligations, ids,
                        record, project, cash, "household:" + project.HouseholdId,
                        workerHouseholdByPerson, dayIndex, diag);
                    if (wageProblem != null)
                        diag.Add($"NpcConstructionDriver.Cancel: wage settlement reported a problem — {wageProblem} The material cancel proceeds regardless.");
                }
                else
                {
                    diag.Add($"NpcConstructionDriver.Cancel: no commissioner record for project '{project.ProjectId}' — hired wages could not be settled; the material cancel proceeds.");
                }
            }
            else
            {
                diag.Add($"NpcConstructionDriver.Cancel: no employment registry or worker-household map supplied for project '{project.ProjectId}' — hired wages could not be settled; the material cancel proceeds (the commissioner can settle them when a registry is available).");
            }
            return executor.CancelProject(project, null, restock, scheduleTracker, tasks, budgets, dayIndex, diag);
        }
    }
}
