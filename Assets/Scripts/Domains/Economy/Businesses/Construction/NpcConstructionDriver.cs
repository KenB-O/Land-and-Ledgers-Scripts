using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
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
        private readonly BuildingDesignCatalog catalog;
        private readonly IList<ConstructionProject> projects;
        private readonly CreditCashBridge cash;
        private readonly FinancialObligationAuthority obligations;
        private readonly EntityIdRegistry ids;
        private readonly HousingAuthority housing;
        private readonly IConstructionMaterialPricePort pricePort;
        private readonly IConstructionMaterialSource materialSource;
        private readonly string defaultSupplierOwnerName;
        private readonly INpcConstructionProcurementPort procurement;
        private readonly INpcConstructionLaborPort labor;
        private readonly Func<int, IReadOnlyList<int>> householdMembers;
        private readonly List<string> diagnostics = new List<string>();
        /// <summary>
        /// Packages whose procurement leg failed after the sale stood: the
        /// paid-for lot sits with the supplier (service semantic), so a
        /// blind retry would buy the materials a second time. A blocked
        /// package waits until <see cref="AllowProcurementRetry"/> is called
        /// explicitly, so the stranded lot is reconciled by an operator
        /// decision, never silently double-bought.
        /// </summary>
        private readonly HashSet<string> blockedProcurementPackages = new HashSet<string>(StringComparer.Ordinal);

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
        }

        /// <summary>
        /// Commissions one explicit request: the design must exist, sleep
        /// the household and be fundable from the household's real cash;
        /// the parcel authority must authorize the build; only then is the
        /// project created and added to the project list. Returns null on
        /// refusal (with the reason in diagnostics).
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
            int members = request.MemberPersonIds != null ? request.MemberPersonIds.Count : 0;
            BuildingDesign design = commissioner.SelectDesign(catalog, request.DesignId,
                Math.Max(1, members), pricePort, buyerStore.ReadBalanceCents(), diag);
            if (design == null)
            {
                diag.Add($"NpcConstructionDriver.Commission: design '{request.DesignId}' refused for H{request.HouseholdId} — see the selection refusal above.");
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

        /// <summary>Clears a procurement retry block for one package (explicit operator decision).</summary>
        public void AllowProcurementRetry(ConstructionProject project, WorkPackage package)
        {
            if (project == null || package == null) return;
            blockedProcurementPackages.Remove(project.ProjectId + "|" + package.PackageId);
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
                    return $"procurement previously failed for package '{package.PackageId}' — the paid-for lot sits with the supplier; retry is blocked until AllowProcurementRetry is called.";
                string problem = procurement.ProcureAndStage(project, package, record, dayIndex, diag);
                if (problem != null) blockedProcurementPackages.Add(key);
                return problem;
            }
            IConstructionMaterialSource source = MaterialSourceByHousehold != null
                ? MaterialSourceByHousehold(project.HouseholdId) : null;
            if (source == null) source = materialSource;
            if (source == null)
                return "no material source and no procurement port bound — materials come from real lots only.";
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
        /// </summary>
        public string Cancel(ConstructionProject project, IConstructionMaterialRestock restock,
            PersonScheduleTracker scheduleTracker, TaskAuthority tasks,
            WorkTimeBudgetStore budgets, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (executor == null || project == null)
                return "NpcConstructionDriver.Cancel: executor and project required.";
            return executor.CancelProject(project, null, restock, scheduleTracker, tasks, budgets, dayIndex, diag);
        }
    }
}
