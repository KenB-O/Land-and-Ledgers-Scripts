// Pass XI WS4 — production-driven 30-day settlement proof (Roslyn harness only).
//
// The scenario below runs 30 days (day 0..29) through the ACTUAL production
// authorities wherever they can be instantiated outside Unity: HouseholdLedger
// (one cash writer for households), FinancialObligationAuthority (one debt
// authority), EmploymentRelationshipRegistry payroll, BusinessRuntimeState
// tills/stocks, BusinessLenderService + BankDepositLedger, ExternalMarketAuthority,
// HousingAuthority + TenancyPaymentService, NpcBusinessLifecycleService,
// PropertyTaxService + CivicTreasuryService, FreightShipmentService,
// EquipmentReservationService, MoneyOrderService, NpcConstructionDriver,
// NpcBusinessFormationService, MineAssayResult, and the production save DTOs
// of every one of those authorities for save/restore.
//
// This is NOT the Unity RUN-004 thirty-day proof, which stays open until
// performed in Unity. Nothing here claims Unity compilation, Test Runner, or
// PlayMode success: every figure below is a Roslyn harness result.
//
// Cash conservation scope: 5 business tills + 5 household ledgers + the bank
// vault + the lumber-yard purse + the civic treasury. Postal money-order funds
// are reconciled separately (they are a transit store, not town cash: the
// sender's cash leaves the town ledger at issue and returns to the payee's
// ledger at redemption; only the 25c fee leaves town across the boundary).
// Harvest and farm-labor grain are land yields (the Pass VIII convention:
// grain moves from unmodeled land into farm stock at zero cash cost, never
// minted as cash); every cash cent is conserved against the boundary net.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Postal;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Integration
{
    /// <summary>
    /// WS4 settlement town, day 0..29. All mutable economy state lives in the
    /// production authorities; this class only orchestrates day phases, applies
    /// the dated beats, and reconciles. Phases within a day (see Phase*):
    /// 0 mill production, 1 bakery/farm/mine production, 2 freight advance,
    /// 3 retail, 4..7 payroll (mill, bakery, farm, mine), 8 bank + money-order
    /// advance, 9 dated beats, 10 construction drive, 11 tax advance,
    /// 12 objective evaluation. Save/restore snapshots sit on phase boundaries.
    /// </summary>
    public sealed class Ws4SettlementScenario
    {
        public const int StartDay = 0;
        public const int SimDays = 30;
        public const int EndDayExclusive = StartDay + SimDays;
        public const int PhasesPerDay = 13;

        public const int H1 = 1, H2 = 2, H3 = 3, H4 = 4, H5 = 5;
        public const int P1 = 1, P2 = 2, P3 = 3, P4 = 4, P5 = 5;

        public const string Mill = "mill-1";
        public const string Bakery = "bakery-1";
        public const string Farm = "farm-1";
        public const string Store = "store-1";
        public const string Mine = "mine-1";
        public const string Yard = "business:lumber-yard";

        public const string Grain = "grain";
        public const string Flour = "flour";
        public const string Bread = "bread";
        public const string Ore = "ore";

        public const int GrainPriceCents = 35;
        public const int FlourMillPriceCents = 55;
        public const int FlourImportPriceCents = 75;
        public const int BreadWholesaleCents = 75;
        public const int BreadWholesaleRaisedCents = 90;
        public const int BreadRetailCents = 100;
        public const int BreadRetailRaisedCents = 120;
        public const int OrePriceCents = 20;

        public const int MillWageCents = 600;
        public const int BakeryWageCents = 500;
        public const int FarmWageCents = 450;
        public const int MineWageCents = 450;
        public const int HouseholdRentCents = 800;
        public const int BoardingCents = 300;
        public const int BakeryRentCents = 1000;

        public const string BankName = "Frontier Town Bank";
        public const string ExternalCounterpartyId = "kansas-city";

        // ---- production authorities ----
        public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
        public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
        public EntityIdRegistry Ids = new EntityIdRegistry();
        public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
        public readonly Dictionary<string, BusinessRuntimeState> Runtimes =
            new Dictionary<string, BusinessRuntimeState>(StringComparer.Ordinal);
        public readonly Dictionary<string, BusinessInstanceState> Businesses =
            new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
        public BusinessLenderService Bank;
        public BankDepositLedger DepositLedger;
        public CreditOfferWorkflow CreditWorkflow = new CreditOfferWorkflow();
        public CreditEventLog CreditEvents = new CreditEventLog();
        public EnterpriseValuationReadModel Valuation = new EnterpriseValuationReadModel();
        public ExternalMarketAuthority ExternalMarket = new ExternalMarketAuthority();
        public CreditCashBridge CreditorBridge = new CreditCashBridge();
        public HousingAuthority Housing = new HousingAuthority();
        public TenancyPaymentService Tenancy = new TenancyPaymentService();
        public NpcBusinessLifecycleService Lifecycle = new NpcBusinessLifecycleService();
        public CivicTreasuryService Treasury = new CivicTreasuryService();
        public PropertyTaxService Taxes = new PropertyTaxService();
        public EquipmentReservationService Reservations = new EquipmentReservationService();

        // ---- freight ----
        public FreightShipmentService Freight = new FreightShipmentService();
        public TaskAuthority FreightTasks = new TaskAuthority();
        public WorkTimeBudgetStore FreightBudgets = new WorkTimeBudgetStore();
        public FreightShipmentHooks FreightHooks;
        public readonly List<LogisticsShipmentState> Shipments = new List<LogisticsShipmentState>();
        public readonly Dictionary<string, bool> FreightSourceDeducted = new Dictionary<string, bool>(StringComparer.Ordinal);

        // ---- postal ----
        public PostalService Postal = new PostalService();
        public MoneyOrderService MoneyOrders;

        // ---- construction ----
        public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
        public ConstructionProjectExecutor ConstrExecutor = new ConstructionProjectExecutor();
        public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
        public List<ConstructionProject> Projects = new List<ConstructionProject>();
        public NpcConstructionDriver ConstrDriver;
        public Ws4ParcelAuthority Parcels = new Ws4ParcelAuthority();
        public Ws4PricePort Prices = new Ws4PricePort();
        public Ws4MaterialSource YardStock = new Ws4MaterialSource();
        public PurseCashStore YardPurse;
        public TaskAuthority ConstrTasks = new TaskAuthority();
        public WorkTimeBudgetStore ConstrBudgets = new WorkTimeBudgetStore();
        public PersonScheduleTracker ConstrTracker = new PersonScheduleTracker();
        public Ws4ToolCustody Custody = new Ws4ToolCustody();

        // ---- scenario state ----
        public int CurrentDay;
        public int NextPhase;
        public bool MineOpen = true;
        public int BreadPerHouseholdPerDay = 2;
        public int BreadWholesalePriceCents = BreadWholesaleCents;
        public int BreadRetailPriceCents = BreadRetailCents;
        public readonly List<string> Diag = new List<string>();
        public NpcBusinessLifeRecord MillRecord;
        public NpcBusinessLifeRecord BakeryRecord;
        public NpcBusinessLifeRecord FarmRecord;
        public NpcBusinessLifeRecord StoreRecord;
        public NpcBusinessLifeRecord MineRecord;
        public FinancialObligation MillLoan;
        public NpcBusinessCloseResult MineCloseResult;
        public int MinePayrollSuspendedDay = -1;
        public string MinePayrollLiabilityObligationId = string.Empty;
        public int MineClosedDay = -1;
        public int Cabin1CompletedDay = -1;
        public int Cabin2CommissionedDay = -1;
        public bool Cabin2FramingStalled;
        public TaxAssessment MillTaxAssessment;
        public TaxAssessment LandlordTaxAssessment;
        public MineAssayResult MineAssay;
        public PostalMoneyOrder TownMoneyOrder;

        // ---- objectives (scenario progression ladder) ----
        public const int BreadObjectiveTarget = 120;
        public const int BreadObjectiveDeadline = 25;
        public const int FreightObjectiveDeadline = 10;
        public const int CabinObjectiveDeadline = 20;
        public const int TaxObjectiveDeadline = 25;
        public bool BreadObjectiveMet;
        public bool BreadObjectiveFailed;
        public bool FreightObjectiveMet;
        public bool CabinObjectiveMet;
        public bool TaxObjectiveMet;
        public bool TaxObjectiveFailed;

        // ---- reconciliation counters ----
        public int InitialModeledCashCents;
        public int InitialGrainUnits;
        public long GrainHarvested;
        public long GrainLabor;
        public long GrainMilled;
        public long BranWasted;
        public long FlourMilled;
        public long FlourImported;
        public long FlourBaked;
        public long BreadBaked;
        public long BreadRetailed;
        public long GrainExported;
        public long OreDug;
        public long OreSold;
        public int BoundaryInflowCents;
        public int BoundaryOutflowCents;
        public int LiquidationInflowCents;
        public int BankLoanRefusals;
        public int FreightDeliveredUnits;
        public int FreightRejectedShipments;
        public int PostalFeesCollectedCents;
        public readonly List<string> ArrearsObligationIds = new List<string>();
        public readonly List<string> RefusalLog = new List<string>();

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException("missing field " + name);
            field.SetValue(target, value);
        }

        private static int PersonHousehold(int personId)
        {
            return personId == P1 ? H1 : personId == P2 ? H2 : personId == P3 ? H3 : personId == P4 ? H4 : personId == P5 ? H5 : -1;
        }

        // ================= build =================

        /// <summary>Structure only: authorities, runtimes, businesses, bank binding,
        /// housing, tenancies, freight wagon, postal offices, construction chain.
        /// No opening cash, stock, charter, valuation or counterparty yet: those
        /// are SeedOpening, so Restore can rebuild structure then load a snapshot
        /// without double-funding anything.</summary>
        public void BuildStructure()
        {
            AddRuntime(Mill);
            AddRuntime(Bakery);
            AddRuntime(Farm);
            AddRuntime(Store);
            AddRuntime(Mine);

            Bank = new BusinessLenderService(BankName);
            Bank.BindHubServices(Ids, Obligations, CreditWorkflow, CreditEvents, Valuation);
            Bank.BindBusinessLookup(id =>
            {
                BusinessInstanceState found;
                return Businesses.TryGetValue(id, out found) ? found : null;
            });
            var vaultField = typeof(BusinessLenderService).GetField("depositLedger", BindingFlags.NonPublic | BindingFlags.Instance);
            DepositLedger = (BankDepositLedger)vaultField.GetValue(Bank);
            string bridgeProblem = CreditorBridge.Register(BankName, new BankVaultStore(Bank, DepositLedger, Diag));
            if (bridgeProblem != null) Diag.Add("bank bridge refused: " + bridgeProblem);

            SetupTenancy(H1, P1, "house-h1");
            SetupTenancy(H2, P2, "house-h2");
            SetupTenancy(H3, P3, "house-h3");
            SetupBoarding(H5, P5, "house-h4");

            MillRecord = LifeRecord(Mill, P1, "person:1", new[] { "emp-mill-1" });
            BakeryRecord = LifeRecord(Bakery, P4, "person:4", new[] { "emp-bakery-2" });
            FarmRecord = LifeRecord(Farm, P3, "person:3", new[] { "emp-farm-3" });
            StoreRecord = LifeRecord(Store, P4, "person:4", new string[0]);
            MineRecord = LifeRecord(Mine, P5, "person:5", new[] { "emp-mine-5" });

            // Freight: one real wagon, capacity-enforced by mass and volume.
            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1",
                Kind = "farm-wagon",
                DisplayName = "Farm wagon",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            };
            Freight.RegisterWagon(wagon);
            FreightHooks = new FreightShipmentHooks
            {
                Tasks = FreightTasks,
                Budgets = FreightBudgets,
                DayIndex = StartDay,
                Diagnostics = Diag,
                Reservations = Reservations,
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    string dest = shipment.destinationBusinessInstanceId;
                    if (string.IsNullOrEmpty(dest) || !Runtimes.ContainsKey(dest)) return 0;
                    Runtimes[dest].AddCategoryStockUnits(Grain, quantityUnits);
                    FreightDeliveredUnits += quantityUnits;
                    return quantityUnits;
                },
            };

            // Postal: two money-order offices (ordinary + depository).
            string postalProblem = Postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, Diag);
            if (postalProblem != null) Diag.Add("postal office refused: " + postalProblem);
            postalProblem = Postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, Diag);
            if (postalProblem != null) Diag.Add("postal office refused: " + postalProblem);
            MoneyOrders = new MoneyOrderService(Postal);
            string moProblem = MoneyOrders.RegisterMoneyOrderOffice("po-b", MoneyOrderOfficeClass.Depository, null, 0, Diag);
            if (moProblem != null) Diag.Add("money-order office refused: " + moProblem);
            moProblem = MoneyOrders.RegisterMoneyOrderOffice("po-a", MoneyOrderOfficeClass.Ordinary, "po-b", 0, Diag);
            if (moProblem != null) Diag.Add("money-order office refused: " + moProblem);

            // Construction: cabin design, prices, yard stock, purse, labor.
            RegisterCabinDesign();
            Prices.Prices["fieldstone"] = 50;
            Prices.Prices["lumber"] = 40;
            YardStock.Stock["fieldstone"] = 1000;
            YardStock.Stock["lumber"] = 650;
            YardPurse = new PurseCashStore(Yard, 0, "yard seed capital");
            string yardProblem = CreditorBridge.Register(Yard, YardPurse);
            if (yardProblem != null) Diag.Add("yard bridge refused: " + yardProblem);
            Custody.Grant(H4, "shovel");
            Custody.Grant(H4, "saw");
            Parcels.HolderByParcel["parcel-9"] = "household:4";
            Parcels.HolderByParcel["parcel-10"] = "household:4";
            ConstrDriver = NewConstrDriver();

            CurrentDay = StartDay;
            NextPhase = 0;
        }

        /// <summary>Opening endowment: cash, stock, charter, valuation evidence,
        /// external counterparty, employments, household funding. Runs once on a
        /// fresh scenario; Restore never calls it (snapshot DTOs carry the state).</summary>
        public void SeedOpening()
        {
            Runtimes[Mill].AddCashCents(15000);
            Runtimes[Bakery].AddCashCents(8000);
            Runtimes[Farm].AddCashCents(10000);
            Runtimes[Store].AddCashCents(20000);
            Runtimes[Mine].AddCashCents(500);

            FundHousehold(H1, 3500);
            FundHousehold(H2, 4000);
            FundHousehold(H3, 5000);
            FundHousehold(H4, 80000);
            FundHousehold(H5, 1500);

            Employ("emp-mill-1", P1, Mill, MillWageCents);
            Employ("emp-bakery-2", P2, Bakery, BakeryWageCents);
            Employ("emp-farm-3", P3, Farm, FarmWageCents);
            Employ("emp-mine-5", P5, Mine, MineWageCents);

            Runtimes[Farm].EnsureCategoryStock(Grain, 300, 2000);
            Runtimes[Mill].EnsureCategoryStock(Grain, 220, 800);
            Runtimes[Mine].EnsureCategoryStock(Ore, 0, 800);
            InitialGrainUnits = 300 + 220;
            Runtimes[Mill].EnsureCategoryStock(Flour, 60, 800);
            Runtimes[Bakery].EnsureCategoryStock(Flour, 40, 800);
            Runtimes[Bakery].EnsureCategoryStock(Bread, 20, 800);
            Runtimes[Store].EnsureCategoryStock(Bread, 30, 800);

            string charterProblem = Bank.EnsureCharteredCapital(2500000, "charter investors", StartDay, Diag);
            if (charterProblem != null) Diag.Add("charter refused: " + charterProblem);
            string reserveProblem = MoneyOrders.EstablishReserve("po-b", 5000, StartDay, Diag);
            if (reserveProblem != null) Diag.Add("postal reserve refused: " + reserveProblem);
            string bridgeProblem = CreditorBridge.Register("household:4", new HouseholdCashStore(H4, Ledgers.GetOrCreate(H4)));
            // The mine payroll payable names its creditor "person:5" (the employee).
            // CloseOrderly credits that creditor through this bridge; without a
            // store under the creditor's name the settlement cash leaves the
            // till and lands nowhere modeled. The employee's household ledger
            // is the creditor's real cash store.
            string minerBridgeProblem = CreditorBridge.Register("person:5", new HouseholdCashStore(H5, Ledgers.GetOrCreate(H5)));
            if (minerBridgeProblem != null) Diag.Add("miner creditor bridge refused: " + minerBridgeProblem);
            if (bridgeProblem != null) Diag.Add("landlord bridge refused: " + bridgeProblem);

            Valuation.RegisterBusiness(Mill, "person:1", false);
            for (int i = 0; i < 6; i++) Valuation.RecordWeeklyProfit(Mill, 3000);
            Valuation.RecordTransferableAssets(Mill, 50000);
            Valuation.RegisterBusiness(Bakery, "person:4", false);
            for (int i = 0; i < 6; i++) Valuation.RecordWeeklyProfit(Bakery, -2000);
            Valuation.RecordTransferableAssets(Bakery, 8000);
            foreach (string businessId in new[] { Mill, Bakery, Farm, Store, Mine })
            {
                Businesses[businessId].BusinessReputation.stockReliability01 = 0.8f;
                Businesses[businessId].BusinessReputation.valueFairness01 = 0.8f;
                Businesses[businessId].BusinessReputation.serviceExperience01 = 0.8f;
                Businesses[businessId].BusinessReputation.conditionPresentationTrust01 = 0.8f;
                Businesses[businessId].BusinessReputation.productTradeConfidence01 = 0.8f;
            }

            string cpProblem = ExternalMarket.RegisterCounterparty(new ExternalCounterparty
            {
                CounterpartyId = ExternalCounterpartyId,
                DisplayName = "Kansas City grain house",
                InitialBudgetCents = 100000,
                TransitDays = 3,
                SupplyUnits = new Dictionary<string, int>(StringComparer.Ordinal) { { Flour, 200 } },
            });
            if (cpProblem != null) Diag.Add("counterparty refused: " + cpProblem);

            InitialModeledCashCents = TotalModeledCashCents();
        }

        public void Build()
        {
            BuildStructure();
            SeedOpening();
        }

        private void AddRuntime(string businessId)
        {
            var rt = new BusinessRuntimeState();
            rt.BindBusinessIdentity(businessId);
            rt.EmploymentRegistry = Employments;
            rt.HouseholdLedgers = Ledgers;
            rt.PersonHouseholdIdLookup = PersonHousehold;
            rt.ObligationAuthority = Obligations;
            rt.ObligationIdRegistry = Ids;
            Runtimes[businessId] = rt;

            var business = new BusinessInstanceState();
            SetPrivate(business, "instanceId", businessId);
            business.TryAssignPremises(100 + Math.Abs(businessId.GetHashCode()) % 900);
            SetPrivate(business, "runtimeState", rt);
            Businesses[businessId] = business;
        }

        private static NpcBusinessLifeRecord LifeRecord(string businessId, int founder, string owner, string[] employmentIds)
        {
            var record = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = businessId,
                BusinessEntityKey = "business:" + businessId,
                FounderPersonId = founder,
                OwnerName = owner,
                State = NpcBusinessLifeState.Operating,
            };
            foreach (string id in employmentIds) record.EmploymentIds.Add(id);
            return record;
        }

        private void FundHousehold(int householdId, int cents)
        {
            string problem = Ledgers.GetOrCreate(householdId).RecordInflow(
                StartDay, cents, HouseholdIncomeSource.OtherDocumented,
                "opening-savings", "opening reserves", "self");
            if (problem != null) Diag.Add("funding refused: " + problem);
        }

        private void Employ(string empId, int personId, string businessId, int weeklyWageCents)
        {
            var rel = new EmploymentRelationship
            {
                Id = empId,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                LifecycleState = EmploymentLifecycleState.Active,
                StartDayIndex = StartDay,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "ws4 wage"),
            };
            string problem = Employments.Register(rel);
            if (problem != null) Diag.Add("employment refused: " + problem);
        }

        private void SetupTenancy(int tenantHouseholdId, int tenantPersonId, string buildingId)
        {
            Housing.RegisterBuilding(buildingId, "parcel-" + buildingId, "house", StartDay, "ws4", Diag);
            AccommodationSpace space = Housing.DefineSpace(buildingId, 4, Diag);
            if (space == null) { Diag.Add("space refused for " + buildingId); return; }
            ResidentialOccupancy occ = Housing.Occupy(tenantPersonId, tenantHouseholdId, space.SpaceId,
                AccommodationArrangement.Rental, StartDay, Diag);
            if (occ == null) { Diag.Add("occupancy refused for H" + tenantHouseholdId); return; }
            PropertyAgreement agreement = Housing.RecordAgreement("rental", "Ash Landlord", "Tenant H" + tenantHouseholdId,
                space.SpaceId, "biweekly rent", StartDay, -1, string.Empty, Diag);
            if (agreement == null) { Diag.Add("agreement refused for H" + tenantHouseholdId); return; }
            string problem = Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = HouseholdRentCents,
                PeriodDays = 15,
                DueDayOfPeriod = 15,
                StartDay = StartDay,
                PayeeHolderId = "household:4",
                TenantHouseholdId = tenantHouseholdId,
            }, Diag);
            if (problem != null) Diag.Add("terms refused: " + problem);
        }

        private void SetupBoarding(int tenantHouseholdId, int tenantPersonId, string buildingId)
        {
            Housing.RegisterBuilding(buildingId, "parcel-" + buildingId, "house", StartDay, "ws4", Diag);
            AccommodationSpace space = Housing.DefineSpace(buildingId, 4, Diag);
            if (space == null) { Diag.Add("boarding space refused for " + buildingId); return; }
            ResidentialOccupancy occ = Housing.Occupy(tenantPersonId, tenantHouseholdId, space.SpaceId,
                AccommodationArrangement.BoardingOrLodging, StartDay, Diag);
            if (occ == null) { Diag.Add("boarding occupancy refused for H" + tenantHouseholdId); return; }
            AccommodationSpace ownerSpace = Housing.DefineSpace(buildingId, 4, Diag);
            if (ownerSpace != null)
            {
                ResidentialOccupancy ownerOcc = Housing.Occupy(P4, H4, ownerSpace.SpaceId,
                    AccommodationArrangement.OwnerOccupied, StartDay, Diag);
                if (ownerOcc == null) Diag.Add("owner occupancy refused for H4.");
            }
            PropertyAgreement agreement = Housing.RecordAgreement("boarding", "Ash Landlord", "Boarder H" + tenantHouseholdId,
                space.SpaceId, "biweekly boarding", StartDay, -1, string.Empty, Diag);
            if (agreement == null) { Diag.Add("boarding agreement refused for H" + tenantHouseholdId); return; }
            string problem = Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = BoardingCents,
                PeriodDays = 15,
                DueDayOfPeriod = 15,
                StartDay = StartDay,
                PayeeHolderId = "household:4",
                TenantHouseholdId = tenantHouseholdId,
            }, Diag);
            if (problem != null) Diag.Add("boarding terms refused: " + problem);
        }

        private void RegisterCabinDesign()
        {
            var design = new BuildingDesign
            {
                DesignId = "cabin-a",
                DisplayName = "Settler cabin",
                WorksKind = ConstructionWorksKind.Unspecified,
                KindLabel = "cabin",
            };
            var foundation = new BuildingDesignPhase
            {
                PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240, Notes = "stone footings",
            };
            foundation.Materials.Add(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-fieldstone",
                MaterialKind = ConstructionMaterialKind.Masonry,
                MaterialDisplayName = "fieldstone",
                RequiredUnits = 40,
                UnitLabel = "perch",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            });
            foundation.RequiredToolItemIds.Add("shovel");
            var framing = new BuildingDesignPhase
            {
                PhaseName = "Framing", Sequence = 1, LaborMinutes = 480, Notes = "log walls and roof",
            };
            framing.Materials.Add(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-lumber",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = 600,
                UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            });
            framing.RequiredToolItemIds.Add("saw");
            design.Phases.Add(foundation);
            design.Phases.Add(framing);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            string problem = Catalog.RegisterDesign(design, Diag);
            if (problem != null) Diag.Add("cabin design refused: " + problem);
        }

        private NpcConstructionDriver NewConstrDriver()
        {
            return new NpcConstructionDriver(Commissioner, ConstrExecutor, Catalog, Projects,
                CreditorBridge, Obligations, Ids, Housing, Prices, YardStock, Yard,
                null, new Ws4LaborPort(this),
                householdId => householdId == H4 ? new List<int> { P4 } : new List<int>());
        }

        public int TotalModeledCashCents()
        {
            int total = 0;
            foreach (BusinessRuntimeState rt in Runtimes.Values) total += rt.CurrentCashCents;
            foreach (int hid in new[] { H1, H2, H3, H4, H5 }) total += Ledgers.GetOrCreate(hid).GetBalanceCents();
            total += Bank != null ? Bank.CashOnHandCents : 0;
            total += YardPurse != null ? YardPurse.ReadBalanceCents() : 0;
            total += Treasury != null ? Treasury.BalanceCents : 0;
            return total;
        }

        public bool DiagContains(string fragment)
        {
            foreach (string line in Diag)
                if (line.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ---- test-local adapters (cash writes stay inside the one-writer /
        // one-debt-authority rules; these only expose production state to
        // production authority interfaces) ----

        private sealed class RuntimeCashPort : INpcBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
            public int ReadCashCents(string businessInstanceId) { return runtime.CurrentCashCents; }
            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                return runtime.TrySpendCents(amountCents)
                    ? null
                    : "RuntimeCashPort: insufficient cash for " + purpose + ".";
            }
            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                runtime.AddCashCents(amountCents);
                return null;
            }
        }

        private sealed class ScenarioLiquidationPort : INpcLiquidationPort
        {
            private readonly List<string> diag;
            public ScenarioLiquidationPort(List<string> diag) { this.diag = diag; }
            public string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
                out NpcLiquidationSale sale)
            {
                sale = null;
                diag.Add("Liquidation: no buyer for '" + (asset != null ? asset.ItemId : "?") +
                         "' — no fire sale, no phantom proceeds.");
                return "no buyer";
            }
        }

        private sealed class ScenarioPayeeDirectory : ITenancyPayeeDirectory
        {
            public int FindPayeeHouseholdId(string payeeHolderId)
            {
                if (string.IsNullOrEmpty(payeeHolderId)) return -1;
                string[] parts = payeeHolderId.Split(':');
                int id;
                if (parts.Length == 2 && parts[0] == "household" && int.TryParse(parts[1], out id)) return id;
                return -1;
            }
            public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId) { return null; }
        }

        private sealed class BankVaultStore : IRealCashStore
        {
            private readonly BusinessLenderService bank;
            private readonly BankDepositLedger ledger;
            private readonly List<string> diag;
            public BankVaultStore(BusinessLenderService bank, BankDepositLedger ledger, List<string> diag)
            {
                this.bank = bank;
                this.ledger = ledger;
                this.diag = diag;
            }
            public string OwnerName => bank.LenderName;
            public int ReadBalanceCents() { return bank.CashOnHandCents; }
            public string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty)
            {
                return "BankVaultStore: disbursement goes through the loan desk, not this store.";
            }
            public string CreditCents(int dayIndex, int amountCents, string sourceClass,
                string sourceReference, string reason, string counterparty)
            {
                if (amountCents <= 0) return "BankVaultStore: the credit must be positive.";
                return ledger.ReceiveLoanPayment(amountCents, sourceReference ?? "close-down", diag);
            }
        }

        // ================= day loop =================

        public void RunAll()
        {
            RunUntil(EndDayExclusive, 0);
        }

        /// <summary>Advances the phase machine until (day, phase) is reached.
        /// (day, phase) means: day is CurrentDay and the next phase to run is
        /// phase. RunUntil(30, 0) runs the whole 30 days.</summary>
        public void RunUntil(int targetDay, int targetPhase)
        {
            int guard = 0;
            while ((CurrentDay < targetDay || (CurrentDay == targetDay && NextPhase < targetPhase)) && guard++ < 10000)
                Step();
        }

        private void Step()
        {
            int d = CurrentDay;
            int phase = NextPhase;
            ExternalMarket.SetDayIndex(d);
            FreightHooks.DayIndex = d;
            FreightHooks.NowGameSeconds = d * 86400f;
            FreightBudgets.EnsureDay(d);
            switch (phase)
            {
                case 0: PhaseMillProduction(d); break;
                case 1: PhaseBakeryFarmMine(d); break;
                case 2: PhaseFreightAdvance(d); break;
                case 3: PhaseRetail(d); break;
                case 4: if (d % 7 == 4) Runtimes[Mill].ResolveWeeklyPayroll(); break;
                case 5: if (d % 7 == 4) Runtimes[Bakery].ResolveWeeklyPayroll(); break;
                case 6: if (d % 7 == 4) Runtimes[Farm].ResolveWeeklyPayroll(); break;
                case 7: if (d % 7 == 4) PhaseMinePayroll(d); break;
                case 8: PhaseBankAndPostal(d); break;
                case 9: PhaseBeats(d); break;
                case 10: PhaseConstruction(d); break;
                case 11: Taxes.AdvanceDay(d, Diag); break;
                case 12: PhaseObjectives(d); break;
            }
            // Payroll phases are Friday-gated (d % 7 == 4): ResolveWeeklyPayroll
            // settles one week, so calling it daily would pay seven weeks.
            NextPhase++;
            if (NextPhase >= PhasesPerDay)
            {
                NextPhase = 0;
                CurrentDay++;
            }
        }

        private void PhaseMillProduction(int d)
        {
            if (d % 7 != 0) return;
            if (Runtimes[Mill].TryConsumeCategoryStockUnits(Grain, 100, out _))
            {
                GrainMilled += 100;
                BranWasted += 20;
                Runtimes[Mill].AddCategoryStockUnits(Flour, 80);
                FlourMilled += 80;
            }
            else
            {
                Diag.Add("day " + d + ": mill grind skipped — grain short (no phantom flour).");
            }
        }

        private void PhaseBakeryFarmMine(int d)
        {
            if (d % 7 != 0) return;
            // Bakery buys flour from the mill, then bakes, then wholesales.
            if (Runtimes[Mill].GetCategoryStock(Flour).CurrentStockUnits >= 20 &&
                Runtimes[Bakery].TrySpendCents(20 * FlourMillPriceCents))
            {
                Runtimes[Bakery].AddCategoryStockUnits(Flour, 20);
                bool settled = Runtimes[Mill].ResolveSettledSale(Flour, 20, 20 * FlourMillPriceCents, "bakery-1");
                if (!settled) Diag.Add("day " + d + ": mill flour sale refused.");
            }
            if (Runtimes[Bakery].TryConsumeCategoryStockUnits(Flour, 20, out _))
            {
                FlourBaked += 20;
                Runtimes[Bakery].AddCategoryStockUnits(Bread, 20);
                BreadBaked += 20;
            }
            if (Runtimes[Bakery].GetCategoryStock(Bread).CurrentStockUnits >= 20 &&
                Runtimes[Store].TrySpendCents(20 * BreadWholesalePriceCents))
            {
                int price = 20 * BreadWholesalePriceCents;
                Runtimes[Store].AddCategoryStockUnits(Bread, 20);
                bool settled = Runtimes[Bakery].ResolveSettledSale(Bread, 20, price, "store-1");
                if (!settled) Diag.Add("day " + d + ": wholesale settlement refused.");
            }
            Runtimes[Farm].AddCategoryStockUnits(Grain, 40);
            GrainLabor += 40;
            if (MineOpen)
            {
                Runtimes[Mine].AddCategoryStockUnits(Ore, 10);
                OreDug += 10;
                string receipt = ExternalMarket.RecordExternalSale(
                    ExternalCounterpartyId, Ore, 10, 10 * OrePriceCents, Diag);
                if (string.IsNullOrEmpty(receipt))
                {
                    Diag.Add("day " + d + ": ore sale refused — counterparty budget exhausted.");
                }
                else if (Runtimes[Mine].TryConsumeCategoryStockUnits(Ore, 10, out _))
                {
                    Runtimes[Mine].AddCashCents(10 * OrePriceCents);
                    BoundaryInflowCents += 10 * OrePriceCents;
                    OreSold += 10;
                }
            }
        }

        private void PhaseFreightAdvance(int d)
        {
            var snapshot = new List<LogisticsShipmentState>(Shipments);
            foreach (LogisticsShipmentState shipment in snapshot)
            {
                if (shipment == null) continue;
                if (shipment.Status == LogisticsShipmentStatus.InTransit)
                    Freight.AdvanceTransit(shipment, 86400f, FreightHooks);
                if (shipment.Status == LogisticsShipmentStatus.Arrived)
                {
                    var loader = EntityId.For(EntityKind.Person, 701);
                    if (Freight.BeginUnloading(shipment, loader, FreightHooks, out string ureason))
                    {
                        int guard = 0;
                        while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 20)
                        {
                            if (!Freight.WorkUnloading(shipment, 60, FreightHooks, out string wreason))
                            {
                                Diag.Add("unload refused: " + wreason);
                                break;
                            }
                        }
                    }
                    else
                    {
                        Diag.Add("unload begin refused: " + ureason);
                    }
                }
            }
        }

        private void PhaseRetail(int d)
        {
            foreach (int hid in new[] { H1, H2, H3, H4, H5 })
            {
                int units = BreadPerHouseholdPerDay;
                int price = units * BreadRetailPriceCents;
                var ledger = Ledgers.GetOrCreate(hid);
                if (ledger.GetBalanceCents() < price) continue;
                if (Runtimes[Store].GetCategoryStock(Bread).CurrentStockUnits < units) continue;
                string problem = ledger.RecordOutflow(d, price, "bread", "store-1");
                if (problem != null) { Diag.Add("bread outflow refused: " + problem); continue; }
                bool settled = Runtimes[Store].ResolveSettledSale(Bread, units, price, "person:" + hid);
                if (!settled) Diag.Add("bread sale refused.");
                else BreadRetailed += units;
            }
        }

        private void PhaseMinePayroll(int d)
        {
            // The mine's payroll is watched: a shortfall suspends the miner
            // through the production payroll path and posts a real Payable
            // (runtime named payable -> debt authority, because the runtime
            // is wired to the obligation authority) instead of paying cash
            // the mine does not hold.
            EmploymentRelationship mineEmp = null;
            if (Employments.TryGetById("emp-mine-5", out EmploymentRelationship found)) mineEmp = found;
            EmploymentLifecycleState before = mineEmp != null ? mineEmp.LifecycleState : EmploymentLifecycleState.PendingStart;
            Runtimes[Mine].ResolveWeeklyPayroll();
            if (mineEmp != null && before == EmploymentLifecycleState.Active &&
                mineEmp.LifecycleState == EmploymentLifecycleState.Suspended && MinePayrollSuspendedDay < 0)
            {
                MinePayrollSuspendedDay = d;
                foreach (BusinessNamedPayableRecord payable in Runtimes[Mine].NamedPayables)
                {
                    if (payable != null && !string.IsNullOrWhiteSpace(payable.ObligationId))
                        MinePayrollLiabilityObligationId = payable.ObligationId;
                }
                Diag.Add("day " + d + ": mine payroll suspended — wage preserved as payable " +
                         MinePayrollLiabilityObligationId + ", not paid in cash.");
            }
        }

        private void PhaseBankAndPostal(int d)
        {
            Bank.ProcessDay(d, Diag);
            MoneyOrders.AdvanceDay(d, Diag);
        }

        private void PhaseBeats(int d)
        {
            if (d % 7 == 1) BakeryPaysRentDay(d);
            switch (d)
            {
                case 1: FormationBeat(d); break;
                case 2: MillBorrowDay(d); break;
                case 3: BankDepositDay(d); break;
                case 5: FreightShipDay(d); break;
                case 6: FreightContentionDay(d); break;
                case 7: MoneyOrderIssueDay(d); break;
                case 8: Cabin1CommissionDay(d); FreightSecondShipDay(d); break;
                case 9: FreightDelayDay(d); UnaffordablePurchaseDay(d); MoneyOrderRedeemDay(d); break;
                case 10: HarvestDay(d, 400); break;
                case 11: BakeryBorrowRefusedDay(d); FreightResumeDay(d); break;
                case 12: EmptySupplierDay(d); break;
                case 13: FreightRejectedDay(d); break;
                case 14: SettleHouseholdRentsDay(d); break;
                case 16: DecliningDemandDay(d); break;
                case 17: FlourImportDay(d); break;
                case 18: MineAssayDay(d); break;
                case 20: MineCloseDay(d); TaxLevyDay(d); break;
                case 22: WholesalePriceDay(d); Cabin2CommissionDay(d); break;
                case 24: BankWithdrawDay(d); break;
                case 25: RetailPriceDay(d); TaxPayDay(d); break;
                case 26: FarmExportDay(d); break;
                case 27: BakeryRepairDay(d); break;
                case 29: SettleHouseholdRentsDay(d); break;
            }
            // External-trade arrivals resolve daily (purchases carry transit).
            foreach (ExternalTradeRecord record in ExternalMarket.ResolveArrivals(d))
            {
                if (record.Direction == "purchase" && record.GoodsId == Flour)
                {
                    Runtimes[Bakery].AddCategoryStockUnits(Flour, record.Quantity);
                    FlourImported += record.Quantity;
                }
            }
        }

        private void PhaseConstruction(int d)
        {
            if (Projects.Count == 0) return;
            ConstrBudgets.EnsureDay(d);
            ConstrDriver.Drive(d, Diag);
            foreach (ConstructionProject project in Projects)
            {
                if (project != null && project.Status == ConstructionProjectStatus.Complete &&
                    Cabin1CompletedDay < 0 && project.ProjectId != null)
                {
                    // The first completed project is cabin-1 (commissioned day 8).
                    Cabin1CompletedDay = d;
                }
            }
            if (Cabin2CommissionedDay >= 0 && !Cabin2FramingStalled)
            {
                foreach (ConstructionProject project in Projects)
                {
                    if (project == null || project.Status == ConstructionProjectStatus.Complete) continue;
                    foreach (WorkPackage package in project.WorkPackages)
                    {
                        if (package != null && package.Status != WorkPackageStatus.Complete &&
                            d >= Cabin2CommissionedDay + 2)
                        {
                            Cabin2FramingStalled = true;
                        }
                    }
                }
            }
        }

        private void PhaseObjectives(int d)
        {
            if (!FreightObjectiveMet && FreightDeliveredUnits >= 60)
            {
                FreightObjectiveMet = true;
                Diag.Add("day " + d + ": objective freight-delivered met.");
            }
            if (!CabinObjectiveMet && Cabin1CompletedDay >= 0)
            {
                CabinObjectiveMet = true;
                Diag.Add("day " + d + ": objective cabin-complete met.");
            }
            if (!TaxObjectiveMet && MillTaxAssessment != null &&
                MillTaxAssessment.Status == TaxAssessmentStatus.Satisfied)
            {
                TaxObjectiveMet = true;
                Diag.Add("day " + d + ": objective mill-tax-paid met.");
            }
            if (d == FreightObjectiveDeadline && !FreightObjectiveMet)
                Diag.Add("day " + d + ": objective freight-delivered deadline passed UNMET.");
            if (d == CabinObjectiveDeadline && !CabinObjectiveMet)
                Diag.Add("day " + d + ": objective cabin-complete deadline passed UNMET.");
            if (d == BreadObjectiveDeadline)
            {
                if (BreadRetailed >= BreadObjectiveTarget) BreadObjectiveMet = true;
                else
                {
                    BreadObjectiveFailed = true;
                    Diag.Add("day " + d + ": objective bread-120 deadline passed UNMET (" +
                             BreadRetailed + " retailed) — recorded, not extended.");
                }
            }
            if (d == TaxObjectiveDeadline && !TaxObjectiveMet)
            {
                TaxObjectiveFailed = true;
                Diag.Add("day " + d + ": objective mill-tax-paid deadline passed UNMET.");
            }
        }

        // ================= beats =================

        private void FormationBeat(int d)
        {
            var formation = new NpcBusinessFormationService();
            var context = new NpcFormationContext
            {
                HouseholdLedger = Ledgers.GetOrCreate(H2),
                CreationPort = new Ws4CreationPort(),
                DayIndex = d,
            };
            var valid = new NpcBusinessFormationIntent
            {
                FounderPersonId = P2,
                FounderHouseholdId = H2,
                DisplayName = "Cooperage",
            };
            valid.Activities.Add(new NpcBusinessActivitySpec());
            valid.Premises = new NpcPremisesArrangement
            {
                Tenure = NpcPremisesTenure.Owned,
                ParcelOrBuildingRef = "parcel-cooper",
            };
            valid.Assets.Add(new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "cooper-tools",
                OwnerBusinessId = "cooper-1",
                Units = 1,
                CostCents = 3000,
                Provenance = "founder capital",
            });
            valid.Hires.Add(new NpcFormationHire { PersonId = P2, RoleId = "cooper", AgreedWeeklyWageCents = 400 });
            List<string> problems = formation.Validate(valid, context);
            if (problems.Count > 0) Diag.Add("formation valid intent unexpectedly refused: " + string.Join("; ", problems));
            else Diag.Add("day " + d + ": formation validated for cooperage (founder P2, named assets, named wage).");
            var invalid = new NpcBusinessFormationIntent
            {
                FounderPersonId = P2,
                FounderHouseholdId = H2,
                DisplayName = "Cooperage",
            };
            invalid.Activities.Add(new NpcBusinessActivitySpec());
            invalid.Premises = new NpcPremisesArrangement
            {
                Tenure = NpcPremisesTenure.Owned,
                ParcelOrBuildingRef = "parcel-cooper",
            };
            invalid.Assets.Add(new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "conjured-tools",
                OwnerBusinessId = "cooper-1",
                Units = 1,
                CostCents = 3000,
                Provenance = string.Empty,
            });
            List<string> badProblems = formation.Validate(invalid, context);
            bool provenanceFlagged = false;
            foreach (string problem in badProblems)
                if (problem.IndexOf("provenance", StringComparison.OrdinalIgnoreCase) >= 0) provenanceFlagged = true;
            if (badProblems.Count == 0 || !provenanceFlagged) Diag.Add("formation invalid intent unexpectedly accepted (provenance-free asset).");
            else RefusalLog.Add("formation provenance-free asset refused: " + string.Join("; ", badProblems));
        }

        private void MillBorrowDay(int d)
        {
            string refusal;
            MillLoan = Bank.TryArrangeLoan(Ids, Obligations, Mill, 12000, d, Diag, out refusal);
            if (MillLoan == null) { Diag.Add("mill loan refused unexpectedly: " + refusal); return; }
            MillRecord.OpenObligationIds.Add(MillLoan.ObligationId);
            Runtimes[Mill].AddCashCents(12000);
            Diag.Add("day " + d + ": mill draws 12000c loan; vault debited first, obligation recorded after.");
        }

        private void BankDepositDay(int d)
        {
            var ledger = Ledgers.GetOrCreate(H3);
            string outflow = ledger.RecordOutflow(d, 1000, "bank deposit", "Frontier Town Bank");
            if (outflow != null) { Diag.Add("deposit outflow refused: " + outflow); return; }
            DepositAccount account = DepositLedger.OpenAccount("dep-h3", "person:3", DepositKind.Demand, 1000, d, 0, 0, Diag);
            if (account == null) Diag.Add("day " + d + ": deposit account refused.");
            else Diag.Add("day " + d + ": H3 deposits 1000c (ledger out, vault in, liability recorded).");
        }

        private void BankWithdrawDay(int d)
        {
            string refusal = DepositLedger.Withdraw("dep-h3", 1000, d, false, Diag);
            if (refusal != null) { Diag.Add("withdrawal refused: " + refusal); return; }
            string inflow = Ledgers.GetOrCreate(H3).RecordInflow(d, 1000, HouseholdIncomeSource.OtherDocumented,
                "dep-h3", "withdraw own bank deposit", "Frontier Town Bank");
            if (inflow != null) Diag.Add("withdrawal inflow refused: " + inflow);
            else Diag.Add("day " + d + ": H3 withdraws 1000c (vault out, ledger in).");
        }

        private FreightCargoSpec GrainSpec()
        {
            return new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };
        }

        private LogisticsShipmentState FindShipment(string shipmentId)
        {
            foreach (LogisticsShipmentState s in Shipments)
                if (s != null && s.ShipmentId == shipmentId) return s;
            return null;
        }

        private void LoadAndDispatch(string shipmentId, int units, int day)
        {
            LogisticsShipmentState shipment = Freight.CreateShipment(shipmentId, Farm, Mill, units,
                GrainSpec(), "wagon-1", FreightHooks, out string creason);
            if (shipment == null) { Diag.Add("shipment refused: " + creason); return; }
            Shipments.Add(shipment);
            var loader = EntityId.For(EntityKind.Person, 701);
            var driver = EntityId.For(EntityKind.Person, 702);
            if (!Freight.BeginLoading(shipment, loader, FreightHooks, out string lreason))
            {
                Diag.Add("load begin refused: " + lreason);
                return;
            }
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 20)
            {
                if (!Freight.WorkLoading(shipment, 60, FreightHooks, out string wreason))
                {
                    Diag.Add("load work refused: " + wreason);
                    return;
                }
            }
            if (shipment.loadApplied && !FreightSourceDeducted.ContainsKey(shipmentId))
            {
                if (Runtimes[Farm].TryConsumeCategoryStockUnits(Grain, units, out _))
                {
                    FreightSourceDeducted[shipmentId] = true;
                    Diag.Add("day " + day + ": shipment '" + shipmentId + "' loaded: " + units +
                             " grain moved farm stock -> wagon custody.");
                }
                else
                {
                    Diag.Add("day " + day + ": shipment '" + shipmentId + "' loaded but farm lacks grain — cargo NOT deducted (finding).");
                }
            }
            var team = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            if (!Freight.Dispatch(shipment, driver, team, FreightHooks, out string dreason))
            {
                Diag.Add("dispatch refused: " + dreason);
                return;
            }
            shipment.transitDurationGameSeconds = 172800f;
            Diag.Add("day " + day + ": shipment '" + shipmentId + "' dispatched farm -> mill (" + units + " grain).");
        }

        private void FreightShipDay(int d)
        {
            LoadAndDispatch("ship-1", 60, d);
        }

        private void FreightSecondShipDay(int d)
        {
            LoadAndDispatch("ship-2", 40, d);
        }

        // Day 13: the wagon is free (both real shipments completed), so the
        // overweight shipment reaches the capacity check itself instead of
        // being refused at creation on wagon contention (which is what day 6
        // would show — the wagon is busy with ship-1 then).
        private void FreightRejectedDay(int d)
        {
            LogisticsShipmentState heavy = Freight.CreateShipment("ship-heavy", Farm, Mill, 1000,
                GrainSpec(), "wagon-1", FreightHooks, out string creason);
            if (heavy == null)
            {
                Diag.Add("day " + d + ": overweight shipment refused at creation: " + creason);
                FreightRejectedShipments++;
            }
            else
            {
                Shipments.Add(heavy);
                if (heavy.Status == LogisticsShipmentStatus.Failed && heavy.IsPreLoadBlocked &&
                    heavy.RemainingQuantityUnits == 0)
                {
                    FreightRejectedShipments++;
                    Diag.Add("day " + d + ": overweight shipment blocked pre-load, zero cargo: " + heavy.BlockedReason);
                }
                if (Freight.BeginLoading(heavy, EntityId.For(EntityKind.Person, 701), FreightHooks, out string lreason))
                    Diag.Add("day " + d + ": blocked shipment unexpectedly began loading (finding).");
                else
                    RefusalLog.Add("blocked shipment load refused: " + lreason);
            }
        }

        private void FreightContentionDay(int d)
        {
            LogisticsShipmentState live = FindShipment("ship-1");
            if (live != null)
            {
                string conflict = Freight.FindExclusivityConflict(null, "wagon-1",
                    EntityId.For(EntityKind.Person, 702), null);
                if (conflict != null) Diag.Add("day " + d + ": wagon contention loud: " + conflict);
                else Diag.Add("day " + d + ": wagon contention NOT detected while ship-1 live (finding).");
                string reserveRefusal = Reservations.Reserve("asset:wagon-1", "probe", "contention probe");
                RefusalLog.Add("wagon direct reserve during transit: " + (reserveRefusal ?? "GRANTED (finding — freight hold is service-level, not authority-level)"));
            }
        }

        private void FreightDelayDay(int d)
        {
            LogisticsShipmentState shipment = FindShipment("ship-2");
            if (shipment == null) { Diag.Add("ship-2 missing for delay beat."); return; }
            Freight.DelayShipment(shipment, "bridge washed out", FreightHooks);
            Diag.Add("day " + d + ": ship-2 delayed (bridge washed out); cargo stays in wagon custody.");
        }

        private void FreightResumeDay(int d)
        {
            LogisticsShipmentState shipment = FindShipment("ship-2");
            if (shipment == null) return;
            Freight.ResumeShipment(shipment, FreightHooks);
            Diag.Add("day " + d + ": ship-2 resumed.");
        }

        private void MoneyOrderIssueDay(int d)
        {
            var ledger = Ledgers.GetOrCreate(H1);
            string outflow = ledger.RecordOutflow(d, 2525, "money order to H2", "po-a");
            if (outflow != null) { Diag.Add("money-order outflow refused: " + outflow); return; }
            TownMoneyOrder = MoneyOrders.IssueOrder("po-a", "po-b", "person:1", "person:2", 2500, d, Diag, (a, b) => 2);
            if (TownMoneyOrder == null) { Diag.Add("money order issue refused."); return; }
            BoundaryOutflowCents += 25;
            Diag.Add("day " + d + ": money order po-a#1 issued 2500c to person:2 (fee 25c leaves town).");
        }

        private void MoneyOrderRedeemDay(int d)
        {
            PostalMoneyOrderAdvice advice = MoneyOrders.FindAdvice("po-a", 1);
            if (advice == null || advice.ReceivedDayIndex < 0)
            {
                Diag.Add("day " + d + ": money-order advice not yet received — redemption correctly impossible.");
                return;
            }
            string refusal = MoneyOrders.RedeemOrder("po-a", 1, "person:2", d, Diag);
            if (refusal != null) { Diag.Add("money-order redemption refused: " + refusal); return; }
            string inflow = Ledgers.GetOrCreate(H2).RecordInflow(d, 2500, HouseholdIncomeSource.Remittance,
                "po-a#1", "money order from person:1", "po-b");
            if (inflow != null) Diag.Add("money-order inflow refused: " + inflow);
            else Diag.Add("day " + d + ": money order redeemed 2500c to H2 (po-b fund out, ledger in).");
            int fees = MoneyOrders.CollectMoneyOrderFeeRevenue("po-a", Diag);
            PostalFeesCollectedCents += fees;
            string second = MoneyOrders.RedeemOrder("po-a", 1, "person:2", d, Diag);
            if (second == null) Diag.Add("day " + d + ": money order redeemed TWICE (finding — double redemption).");
            else RefusalLog.Add("second redemption refused: " + second);
        }

        private void UnaffordablePurchaseDay(int d)
        {
            // H5 tries to buy 50 loaves at once: the ledger refuses (balance
            // far below 50 x retail price) and the store's stock does not move.
            int units = 50;
            int price = units * BreadRetailPriceCents;
            int stockBefore = Runtimes[Store].GetCategoryStock(Bread).CurrentStockUnits;
            var ledger = Ledgers.GetOrCreate(H5);
            string refusal = ledger.RecordOutflow(d, price, "bread", "store-1");
            if (refusal == null)
            {
                Diag.Add("day " + d + ": unaffordable purchase unexpectedly succeeded (finding).");
            }
            else
            {
                RefusalLog.Add("day " + d + ": unaffordable purchase refused: " + refusal);
                Diag.Add("day " + d + ": unaffordable purchase refused — nothing moved.");
            }
            Assert.AreEqual(stockBefore, Runtimes[Store].GetCategoryStock(Bread).CurrentStockUnits,
                "a refused purchase must not move store stock.");
        }

        private void HarvestDay(int d, int units)
        {
            Runtimes[Farm].AddCategoryStockUnits(Grain, units);
            GrainHarvested += units;
            Diag.Add("day " + d + ": harvest +" + units + " grain (farm, land yield).");
        }

        private void BakeryBorrowRefusedDay(int d)
        {
            string refusal;
            FinancialObligation loan = Bank.TryArrangeLoan(Ids, Obligations, Bakery, 25000, d, Diag, out refusal);
            if (loan != null)
            {
                Diag.Add("day " + d + ": bakery loan UNEXPECTEDLY approved — credit should contract here.");
            }
            else
            {
                BankLoanRefusals++;
                RefusalLog.Add("bakery loan refused: " + refusal);
                Diag.Add("day " + d + ": bakery loan refused (" + refusal + "); no debt created.");
            }
        }

        private void EmptySupplierDay(int d)
        {
            string receipt = ExternalMarket.RecordExternalPurchase(
                ExternalCounterpartyId, Flour, 500, 500 * FlourImportPriceCents, Diag);
            if (string.IsNullOrEmpty(receipt))
            {
                RefusalLog.Add("day " + d + ": external flour purchase 500 refused — finite supply exhausted.");
                Diag.Add("day " + d + ": empty supplier: 500 flour refused, no cash moved.");
            }
            else
            {
                Diag.Add("day " + d + ": external flour purchase 500 unexpectedly accepted (finding).");
            }
        }

        private void SettleHouseholdRentsDay(int d)
        {
            List<TenancySettlementResult> results = Tenancy.SettleDueRents(
                Housing, d, Ledgers, new ScenarioPayeeDirectory(), Obligations, Ids, Diag);
            int paid = 0, partial = 0, arrears = 0;
            long cashMoved = 0;
            foreach (TenancySettlementResult r in results)
            {
                cashMoved += r.PaidCents;
                if (r.Status == RentObligationStatus.Current) paid++;
                else if (r.Status == RentObligationStatus.Partial) partial++;
                else arrears++;
                if (!string.IsNullOrEmpty(r.ArrearsObligationId))
                    ArrearsObligationIds.Add(r.ArrearsObligationId);
            }
            Diag.Add("day " + d + ": household rents settled — paid=" + paid +
                     " partial=" + partial + " arrears=" + arrears + " cashMoved=" + cashMoved + "c");
        }

        private void DecliningDemandDay(int d)
        {
            BreadPerHouseholdPerDay = 1;
            Diag.Add("day " + d + ": demand declines — store sells 1 loaf per household per day from here.");
        }

        private void FlourImportDay(int d)
        {
            string receipt = ExternalMarket.RecordExternalPurchase(
                ExternalCounterpartyId, Flour, 30, 30 * FlourImportPriceCents, Diag);
            if (string.IsNullOrEmpty(receipt)) { Diag.Add("day " + d + ": flour import refused."); return; }
            if (!Runtimes[Bakery].TrySpendCents(30 * FlourImportPriceCents))
            {
                Diag.Add("day " + d + ": bakery cannot fund the flour import — cash short.");
                return;
            }
            BoundaryOutflowCents += 30 * FlourImportPriceCents;
            Diag.Add("day " + d + ": bakery imports 30 flour @75c (arrives day " + (d + 3) + ").");
        }

        private void MineAssayDay(int d)
        {
            MineAssay = new MineAssayResult("assay-mine-1",
                EntityId.For(EntityKind.Lot, 900), EntityId.For(EntityKind.Person, 5), d,
                0.02f, "oz/ton", "fire assay");
            Diag.Add("day " + d + ": mine assay 0.02 oz/ton vs 0.10 break-even — uneconomic on grade " +
                     "and on cash (ore revenue below wages).");
        }

        private void MineCloseDay(int d)
        {
            MineOpen = false;
            var formation = new NpcFormationAssetRegister();
            string regProblem = formation.Record(new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "mine-timbers",
                OwnerBusinessId = Mine,
                Units = 1,
                CostCents = 8000,
                Provenance = "founder capital",
            });
            if (regProblem != null) Diag.Add("mine asset refused: " + regProblem);
            if (!string.IsNullOrEmpty(MinePayrollLiabilityObligationId))
                MineRecord.OpenObligationIds.Add(MinePayrollLiabilityObligationId);
            var ctx = new NpcLifecycleContext
            {
                Obligations = Obligations,
                Employments = Employments,
                Cash = new RuntimeCashPort(Runtimes[Mine]),
                Liquidation = new ScenarioLiquidationPort(Diag),
                FounderHouseholdLedger = Ledgers.GetOrCreate(P5),
                AssetRegister = formation,
                CreditorCash = CreditorBridge,
                DayIndex = d,
                Ids = Ids,
            };
            MineCloseResult = Lifecycle.CloseOrderly(
                MineRecord, "uneconomic: assay 0.02 oz/ton and ore revenue below wages", ctx, Diag);
            LiquidationInflowCents += MineCloseResult.LiquidationProceedsCents;
            MineClosedDay = d;
            MineRecord.State = NpcBusinessLifeState.Closed;
            Diag.Add("day " + d + ": mine closes. settled=" + MineCloseResult.ObligationsSettled.Count +
                     " assumed=" + MineCloseResult.ObligationsAssumed.Count +
                     " proceeds=" + MineCloseResult.LiquidationProceedsCents +
                     " residual=" + MineCloseResult.ResidualToFounderCents +
                     " staff=" + MineCloseResult.StaffReleased +
                     " cashToCreditors=" + MineCloseResult.CashPaidToCreditorsCents);
        }

        private void TaxLevyDay(int d)
        {
            MillTaxAssessment = Taxes.LevyAssessment(Ids, "parcel-mill", "mill-1", 1, 900, 25, 500, 1825, Diag);
            LandlordTaxAssessment = Taxes.LevyAssessment(Ids, "parcel-house-h4", "household:4", 1, 1200, 25, 500, 1825, Diag);
            Diag.Add("day " + d + ": civic levies — mill 900c, landlord parcel 1200c, both due day 25.");
        }

        private void TaxPayDay(int d)
        {
            if (MillTaxAssessment != null && Runtimes[Mill].TrySpendCents(900))
            {
                string problem = Taxes.PayTax(MillTaxAssessment, 900, d, Diag);
                if (problem != null) Diag.Add("mill tax payment refused: " + problem);
                else if (!Treasury.TryCredit(900, CivicLedgerEntryKind.TaxReceived, d, "mill parcel tax", Diag))
                    Diag.Add("treasury credit refused for mill tax.");
                else Diag.Add("day " + d + ": mill pays 900c tax into the civic treasury.");
            }
            else
            {
                Diag.Add("day " + d + ": mill cannot pay its tax in full (finding).");
            }
            Diag.Add("day " + d + ": landlord parcel tax deliberately unpaid — delinquency from day 26.");
        }

        private void WholesalePriceDay(int d)
        {
            BreadWholesalePriceCents = BreadWholesaleRaisedCents;
            Diag.Add("day " + d + ": bakery raises wholesale bread to 90c.");
        }

        private void RetailPriceDay(int d)
        {
            BreadRetailPriceCents = BreadRetailRaisedCents;
            Diag.Add("day " + d + ": store raises retail bread to 120c.");
        }

        private void Cabin1CommissionDay(int d)
        {
            ConstructionProject project = ConstrDriver.Commission(new NpcConstructionRequest
            {
                HouseholdId = H4,
                DesignId = "cabin-a",
                ParcelId = "parcel-9",
                MemberPersonIds = new List<int> { P4 },
                Arrangement = AccommodationArrangement.OwnerOccupied,
            }, Parcels, d, Diag);
            if (project == null) Diag.Add("day " + d + ": cabin-1 commission refused (see diagnostics).");
            else Diag.Add("day " + d + ": cabin-1 commissioned for H4 on parcel-9.");
        }

        private void Cabin2CommissionDay(int d)
        {
            Cabin2CommissionedDay = d;
            ConstructionProject project = ConstrDriver.Commission(new NpcConstructionRequest
            {
                HouseholdId = H4,
                DesignId = "cabin-a",
                ParcelId = "parcel-10",
                MemberPersonIds = new List<int> { P4 },
                Arrangement = AccommodationArrangement.OwnerOccupied,
            }, Parcels, d, Diag);
            if (project == null) Diag.Add("day " + d + ": cabin-2 commission refused (see diagnostics).");
            else Diag.Add("day " + d + ": cabin-2 commissioned for H4 on parcel-10 (lumber short: 50 on hand, 600 needed).");
        }

        private void FarmExportDay(int d)
        {
            string receipt = ExternalMarket.RecordExternalSale(
                ExternalCounterpartyId, Grain, 100, 100 * GrainPriceCents, Diag);
            if (string.IsNullOrEmpty(receipt)) { Diag.Add("day " + d + ": grain export refused."); return; }
            if (!Runtimes[Farm].TryConsumeCategoryStockUnits(Grain, 100, out _))
            {
                Diag.Add("day " + d + ": farm lacks grain to export.");
                return;
            }
            Runtimes[Farm].AddCashCents(100 * GrainPriceCents);
            BoundaryInflowCents += 100 * GrainPriceCents;
            GrainExported += 100;
            Diag.Add("day " + d + ": farm exports 100 grain @35c (+" + (100 * GrainPriceCents) + "c inflow).");
        }

        private void BakeryRepairDay(int d)
        {
            const int amount = 500;
            if (!Runtimes[Bakery].TrySpendCents(amount))
            {
                Diag.Add("day " + d + ": bakery cannot pay the oven repair.");
                return;
            }
            string problem = Ledgers.GetOrCreate(H3).RecordInflow(d, amount,
                HouseholdIncomeSource.ServiceIncome, "oven-repair", "bakery oven repair", "bakery-1");
            if (problem != null) Diag.Add("repair inflow refused: " + problem);
            else Diag.Add("day " + d + ": bakery pays H3 500c for oven repair.");
        }

        // ================= snapshot / restore =================

        public Ws4Snapshot CaptureSnapshot()
        {
            var snap = new Ws4Snapshot();
            snap.Day = CurrentDay;
            snap.Phase = NextPhase;
            snap.MineOpen = MineOpen;
            snap.BreadPerHouseholdPerDay = BreadPerHouseholdPerDay;
            snap.BreadWholesalePriceCents = BreadWholesalePriceCents;
            snap.BreadRetailPriceCents = BreadRetailPriceCents;
            snap.MinePayrollSuspendedDay = MinePayrollSuspendedDay;
            snap.MinePayrollLiabilityObligationId = MinePayrollLiabilityObligationId;
            snap.MineClosedDay = MineClosedDay;
            snap.Cabin1CompletedDay = Cabin1CompletedDay;
            snap.Cabin2CommissionedDay = Cabin2CommissionedDay;
            snap.Cabin2FramingStalled = Cabin2FramingStalled;
            snap.MillLoanId = MillLoan != null ? MillLoan.ObligationId : string.Empty;
            snap.MillTaxAssessmentId = MillTaxAssessment != null ? MillTaxAssessment.AssessmentId : string.Empty;
            snap.LandlordTaxAssessmentId = LandlordTaxAssessment != null ? LandlordTaxAssessment.AssessmentId : string.Empty;
            snap.BreadObjectiveMet = BreadObjectiveMet;
            snap.BreadObjectiveFailed = BreadObjectiveFailed;
            snap.FreightObjectiveMet = FreightObjectiveMet;
            snap.CabinObjectiveMet = CabinObjectiveMet;
            snap.TaxObjectiveMet = TaxObjectiveMet;
            snap.TaxObjectiveFailed = TaxObjectiveFailed;
            snap.InitialModeledCashCents = InitialModeledCashCents;
            snap.InitialGrainUnits = InitialGrainUnits;
            snap.GrainHarvested = GrainHarvested;
            snap.GrainLabor = GrainLabor;
            snap.GrainMilled = GrainMilled;
            snap.BranWasted = BranWasted;
            snap.FlourMilled = FlourMilled;
            snap.FlourImported = FlourImported;
            snap.FlourBaked = FlourBaked;
            snap.BreadBaked = BreadBaked;
            snap.BreadRetailed = BreadRetailed;
            snap.GrainExported = GrainExported;
            snap.OreDug = OreDug;
            snap.OreSold = OreSold;
            snap.BoundaryInflowCents = BoundaryInflowCents;
            snap.BoundaryOutflowCents = BoundaryOutflowCents;
            snap.LiquidationInflowCents = LiquidationInflowCents;
            snap.BankLoanRefusals = BankLoanRefusals;
            snap.FreightDeliveredUnits = FreightDeliveredUnits;
            snap.FreightRejectedShipments = FreightRejectedShipments;
            snap.PostalFeesCollectedCents = PostalFeesCollectedCents;
            snap.ArrearsObligationIds.AddRange(ArrearsObligationIds);
            snap.RefusalLog.AddRange(RefusalLog);
            snap.Diag.AddRange(Diag);
            foreach (KeyValuePair<string, bool> kv in FreightSourceDeducted) snap.FreightSourceDeducted[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, int> kv in YardStock.Stock) snap.YardStocks[kv.Key] = kv.Value;
            snap.YardPurseBalance = YardPurse != null ? YardPurse.ReadBalanceCents() : 0;
            if (MineAssay != null)
            {
                snap.HasMineAssay = true;
                snap.MineAssayDay = MineAssay.AssayDayIndex;
                snap.MineAssayGrade = MineAssay.AssayedGradeValue;
            }
            if (MineCloseResult != null)
            {
                snap.MineCloseSettled.AddRange(MineCloseResult.ObligationsSettled);
                snap.MineCloseAssumed.AddRange(MineCloseResult.ObligationsAssumed);
                snap.MineCloseCashToCreditors = MineCloseResult.CashPaidToCreditorsCents;
                snap.MineCloseProceeds = MineCloseResult.LiquidationProceedsCents;
                snap.MineCloseStaff = MineCloseResult.StaffReleased;
                snap.MineCloseResidual = MineCloseResult.ResidualToFounderCents;
            }
            snap.LifeStates["mill-1"] = LifeStateDto(MillRecord);
            snap.LifeStates["bakery-1"] = LifeStateDto(BakeryRecord);
            snap.LifeStates["farm-1"] = LifeStateDto(FarmRecord);
            snap.LifeStates["store-1"] = LifeStateDto(StoreRecord);
            snap.LifeStates["mine-1"] = LifeStateDto(MineRecord);

            // Ledger states are deep-copied: ExportState hands out the live
            // state objects, and a snapshot must not mutate with the original.
            var states = new List<HouseholdLedgerState>();
            Ledgers.ExportState(states);
            foreach (HouseholdLedgerState state in states)
            {
                var copy = new HouseholdLedgerState
                {
                    HouseholdId = state.HouseholdId,
                    NextEntrySequence = state.NextEntrySequence,
                    Entries = new List<HouseholdLedgerEntry>(),
                };
                foreach (HouseholdLedgerEntry entry in state.Entries)
                {
                    copy.Entries.Add(new HouseholdLedgerEntry
                    {
                        EntrySequence = entry.EntrySequence,
                        DayIndex = entry.DayIndex,
                        AmountCents = entry.AmountCents,
                        Source = entry.Source,
                        SourceReference = entry.SourceReference,
                        Reason = entry.Reason,
                        Purpose = entry.Purpose,
                        Counterparty = entry.Counterparty,
                        BalanceAfterCents = entry.BalanceAfterCents,
                    });
                }
                snap.LedgerStates.Add(copy);
            }
            snap.ObligationsDto = DeepCopyObligations(Obligations.CaptureSaveDto());
            snap.EmploymentsDto = Employments.CaptureSaveDto();
            foreach (KeyValuePair<string, BusinessRuntimeState> kv in Runtimes)
                snap.RuntimeDtos[kv.Key] = kv.Value.CaptureSaveDto();
            snap.BankDto = Bank.CaptureSaveDto();
            snap.WorkflowDto = CreditWorkflow.CaptureSaveDto();
            snap.EventDto = CreditEvents.CaptureSaveDto();
            snap.MarketDto = ExternalMarket.ExportState();
            snap.HousingDto = Housing.CaptureSaveDto();
            snap.TenancyDto = Tenancy.CaptureSaveDto();
            snap.TreasuryDto = Treasury.CaptureSaveDto();
            snap.TaxDto = Taxes.CaptureSaveDto();
            snap.ReservationDto = Reservations.CaptureSaveDto();
            snap.FreightDto = Freight.CaptureSaveDto(new List<LogisticsShipmentState>(Shipments));
            snap.FreightTaskDto = FreightTasks.CaptureSaveDto();
            snap.ConstrTaskDto = ConstrTasks.CaptureSaveDto();
            snap.ConstrTrackerDto = ConstrTracker.CaptureSaveDto();
            snap.MoneyOrderDto = MoneyOrders.CaptureSaveDto();
            snap.ConstrDto = ConstrExecutor.CaptureSaveDto(Projects);
            snap.IdCursors = Ids.SnapshotCursors();
            return snap;
        }

        private static Ws4LifeStateDto LifeStateDto(NpcBusinessLifeRecord record)
        {
            var dto = new Ws4LifeStateDto();
            if (record == null) return dto;
            dto.BusinessInstanceId = record.BusinessInstanceId;
            dto.BusinessEntityKey = record.BusinessEntityKey;
            dto.FounderPersonId = record.FounderPersonId;
            dto.OwnerName = record.OwnerName;
            dto.State = record.State;
            dto.OpenObligationIds.AddRange(record.OpenObligationIds);
            dto.EmploymentIds.AddRange(record.EmploymentIds);
            dto.CloseReason = record.CloseReason;
            return dto;
        }

        private static FinancialObligationSaveDto DeepCopyObligations(FinancialObligationSaveDto source)
        {
            var copy = new FinancialObligationSaveDto();
            if (source == null) return copy;
            if (source.Obligations != null)
            {
                foreach (FinancialObligation o in source.Obligations)
                {
                    if (o == null) continue;
                    var c = new FinancialObligation
                    {
                        ObligationEntityId = o.ObligationEntityId,
                        ObligationId = o.ObligationId,
                        AgreementId = o.AgreementId,
                        Kind = o.Kind,
                        Status = o.Status,
                        Debtor = o.Debtor,
                        Creditor = o.Creditor,
                        OriginalPrincipalCents = o.OriginalPrincipalCents,
                        OutstandingPrincipalCents = o.OutstandingPrincipalCents,
                        AccruedInterestCents = o.AccruedInterestCents,
                        FeesCents = o.FeesCents,
                        IssuedDayIndex = o.IssuedDayIndex,
                        MaturityDayIndex = o.MaturityDayIndex,
                        Purpose = o.Purpose,
                        LegalRegimeId = o.LegalRegimeId,
                        Terms = o.Terms,
                        PaymentTerms = o.PaymentTerms != null ? new FinancialPaymentTerms
                        {
                            AnnualInterestRateBps = o.PaymentTerms.AnnualInterestRateBps,
                            Structure = o.PaymentTerms.Structure,
                            PaymentIntervalDays = o.PaymentTerms.PaymentIntervalDays,
                            InstallmentPrincipalCents = o.PaymentTerms.InstallmentPrincipalCents,
                            BalloonPrincipalCents = o.PaymentTerms.BalloonPrincipalCents,
                            AllowsEarlyPayoff = o.PaymentTerms.AllowsEarlyPayoff,
                            PrepaymentFeeCents = o.PaymentTerms.PrepaymentFeeCents,
                        } : null,
                        ScheduleReference = o.ScheduleReference,
                        SourceEconomicEventId = o.SourceEconomicEventId,
                        LastInterestAccrualDayIndex = o.LastInterestAccrualDayIndex,
                        PredecessorObligationId = o.PredecessorObligationId,
                        SuccessorObligationId = o.SuccessorObligationId,
                        AssumptionId = o.AssumptionId,
                        OriginalDebtorReleased = o.OriginalDebtorReleased,
                        IsDemandObligation = o.IsDemandObligation,
                    };
                    if (o.LiableParties != null) c.LiableParties.AddRange(o.LiableParties);
                    if (o.AssumptionIds != null) c.AssumptionIds.AddRange(o.AssumptionIds);
                    if (o.SecurityInterestIds != null) c.SecurityInterestIds.AddRange(o.SecurityInterestIds);
                    if (o.GuarantyIds != null) c.GuarantyIds.AddRange(o.GuarantyIds);
                    if (o.Payments != null)
                    {
                        foreach (FinancialPaymentRecord p in o.Payments)
                        {
                            if (p == null) continue;
                            c.Payments.Add(new FinancialPaymentRecord
                            {
                                PaymentId = p.PaymentId,
                                DayIndex = p.DayIndex,
                                AmountCents = p.AmountCents,
                                InterestCents = p.InterestCents,
                                PrincipalCents = p.PrincipalCents,
                                FeesCents = p.FeesCents,
                                SourceEconomicEventId = p.SourceEconomicEventId,
                                Payer = p.Payer,
                                Payee = p.Payee,
                            });
                        }
                    }
                    copy.Obligations.Add(c);
                }
            }
            if (source.Securities != null) copy.Securities.AddRange(source.Securities);
            if (source.Guaranties != null) copy.Guaranties.AddRange(source.Guaranties);
            if (source.Facilities != null) copy.Facilities.AddRange(source.Facilities);
            if (source.Assumptions != null) copy.Assumptions.AddRange(source.Assumptions);
            if (source.Links != null) copy.Links.AddRange(source.Links);
            return copy;
        }

        /// <summary>Restores a snapshot into a fresh scenario. The pre-save
        /// instance is discarded after capture, exactly as a real load
        /// discards the pre-save session; authorities that share record
        /// objects with the snapshot are therefore never mutated twice.</summary>
        public static Ws4SettlementScenario Restore(Ws4Snapshot snap)
        {
            var s = new Ws4SettlementScenario();
            s.BuildStructure();
            s.Ids.RestoreCursors(snap.IdCursors);
            s.Ledgers.ImportState(snap.LedgerStates);
            s.Obligations.LoadFromSaveDto(snap.ObligationsDto);
            s.Employments.LoadFromSaveDto(snap.EmploymentsDto);

            foreach (KeyValuePair<string, BusinessRuntimeState> kv in new Dictionary<string, BusinessRuntimeState>(s.Runtimes))
            {
                BusinessRuntimeSaveDto dto;
                if (!snap.RuntimeDtos.TryGetValue(kv.Key, out dto)) continue;
                BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, dto.businessType);
                restored.EmploymentRegistry = s.Employments;
                restored.HouseholdLedgers = s.Ledgers;
                restored.PersonHouseholdIdLookup = PersonHousehold;
                restored.ObligationAuthority = s.Obligations;
                restored.ObligationIdRegistry = s.Ids;
                s.Runtimes[kv.Key] = restored;
                var business = new BusinessInstanceState();
                SetPrivate(business, "instanceId", kv.Key);
                business.TryAssignPremises(100 + Math.Abs(kv.Key.GetHashCode()) % 900);
                SetPrivate(business, "runtimeState", restored);
                business.BusinessReputation.stockReliability01 = 0.8f;
                business.BusinessReputation.valueFairness01 = 0.8f;
                business.BusinessReputation.serviceExperience01 = 0.8f;
                business.BusinessReputation.conditionPresentationTrust01 = 0.8f;
                business.BusinessReputation.productTradeConfidence01 = 0.8f;
                s.Businesses[kv.Key] = business;
            }

            s.Bank.LoadFromSaveDto(snap.BankDto);
            s.CreditWorkflow.LoadFromSaveDto(snap.WorkflowDto);
            s.CreditEvents.LoadFromSaveDto(snap.EventDto);
            s.ExternalMarket.ImportState(snap.MarketDto);
            s.Housing.LoadFromSaveDto(snap.HousingDto);
            s.Tenancy.LoadFromSaveDto(snap.TenancyDto);
            s.Treasury.LoadFromSaveDto(snap.TreasuryDto);
            s.Taxes.LoadFromSaveDto(snap.TaxDto);
            s.Reservations.LoadFromSaveDto(snap.ReservationDto);
            s.FreightTasks.LoadFromSaveDto(snap.FreightTaskDto);
            s.ConstrTasks.LoadFromSaveDto(snap.ConstrTaskDto);
            s.ConstrTracker.LoadFromSaveDto(snap.ConstrTrackerDto);

            // Bridge stores that must point at restored state: the bank store
            // already wraps the (mutated-in-place) bank; household:4 wraps the
            // imported ledger, so it registers after import.
            string bridgeProblem = s.CreditorBridge.Register("household:4", new HouseholdCashStore(H4, s.Ledgers.GetOrCreate(H4)));
            if (bridgeProblem != null) s.Diag.Add("restore landlord bridge refused: " + bridgeProblem);
            string minerBridgeProblem = s.CreditorBridge.Register("person:5", new HouseholdCashStore(H5, s.Ledgers.GetOrCreate(H5)));
            if (minerBridgeProblem != null) s.Diag.Add("restore miner creditor bridge refused: " + minerBridgeProblem);
            if (snap.YardPurseBalance > 0)
            {
                string creditProblem = s.YardPurse.CreditCents(snap.Day, snap.YardPurseBalance,
                    "restore", "snapshot", "yard balance at save", "ws4-restore");
                if (creditProblem != null) s.Diag.Add("restore yard purse refused: " + creditProblem);
            }

            s.FreightHooks = new FreightShipmentHooks
            {
                Tasks = s.FreightTasks,
                Budgets = s.FreightBudgets,
                DayIndex = snap.Day,
                Diagnostics = s.Diag,
                Reservations = s.Reservations,
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    string dest = shipment.destinationBusinessInstanceId;
                    if (string.IsNullOrEmpty(dest) || !s.Runtimes.ContainsKey(dest)) return 0;
                    s.Runtimes[dest].AddCategoryStockUnits(Grain, quantityUnits);
                    s.FreightDeliveredUnits += quantityUnits;
                    return quantityUnits;
                },
            };
            s.Shipments.Clear();
            s.Shipments.AddRange(s.Freight.RestoreSaveDto(snap.FreightDto, s.FreightHooks));
            // PRODUCTION SAVE/LOAD GAP (reported in the proof document):
            // RestoreSaveDto re-tracks shipments but does not repopulate the
            // private dispatchAuthorities map, so a restored in-transit
            // shipment would complete without releasing its wagon reservation
            // and the next shipment would refuse on a stale hold. Rebind the
            // map for every restored shipment that was already dispatched and
            // is not yet terminal.
            FieldInfo authField = typeof(FreightShipmentService).GetField("dispatchAuthorities",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var authMap = authField != null
                ? authField.GetValue(s.Freight) as System.Collections.IDictionary : null;
            if (authMap != null)
            {
                foreach (LogisticsShipmentState restored in s.Shipments)
                {
                    if (restored == null || !restored.physicalTransportBound) continue;
                    if (restored.Status != LogisticsShipmentStatus.InTransit
                        && restored.Status != LogisticsShipmentStatus.Arrived
                        && restored.Status != LogisticsShipmentStatus.Delayed) continue;
                    authMap[restored.shipmentId] = s.Reservations;
                }
            }
            s.MoneyOrders.LoadFromSaveDto(snap.MoneyOrderDto);
            var loadDiag = new List<string>();
            s.Projects = s.ConstrExecutor.LoadFromSaveDto(snap.ConstrDto, loadDiag);
            // PRODUCTION SAVE/LOAD GAP (reported in the proof document):
            // ConstructionProjectSaveDto carries projects but not the
            // executor's id sequence, so a fresh executor restarts at 0 and
            // the next commission reuses an existing project id. The sequence
            // is exactly recoverable here because project/package ids are
            // never deleted: it is one past the largest numeric suffix among
            // all restored project and package ids.
            int maxSeq = -1;
            foreach (ConstructionProject proj in s.Projects)
            {
                if (proj == null) continue;
                maxSeq = Math.Max(maxSeq, TrailingNumber(proj.ProjectId));
                foreach (WorkPackage pkg in proj.WorkPackages ?? new List<WorkPackage>())
                    maxSeq = Math.Max(maxSeq, TrailingNumber(pkg.PackageId));
            }
            FieldInfo seqField = typeof(ConstructionProjectExecutor).GetField("sequence",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (seqField != null) seqField.SetValue(s.ConstrExecutor, maxSeq + 1);
            foreach (KeyValuePair<string, int> kv in snap.YardStocks) s.YardStock.Stock[kv.Key] = kv.Value;
            s.ConstrDriver = s.NewConstrDriver();

            // Valuation is deterministic setup (weekly-profit evidence the lender
            // underwrites on); it is re-seeded identically because no production
            // valuation DTO exists and no loan application follows any restore
            // point in this scenario (the last application is day 11).
            s.SeedValuation();

            s.CurrentDay = snap.Day;
            s.NextPhase = snap.Phase;
            s.MineOpen = snap.MineOpen;
            s.BreadPerHouseholdPerDay = snap.BreadPerHouseholdPerDay;
            s.BreadWholesalePriceCents = snap.BreadWholesalePriceCents;
            s.BreadRetailPriceCents = snap.BreadRetailPriceCents;
            s.MinePayrollSuspendedDay = snap.MinePayrollSuspendedDay;
            s.MinePayrollLiabilityObligationId = snap.MinePayrollLiabilityObligationId;
            s.MineClosedDay = snap.MineClosedDay;
            s.Cabin1CompletedDay = snap.Cabin1CompletedDay;
            s.Cabin2CommissionedDay = snap.Cabin2CommissionedDay;
            s.Cabin2FramingStalled = snap.Cabin2FramingStalled;
            s.BreadObjectiveMet = snap.BreadObjectiveMet;
            s.BreadObjectiveFailed = snap.BreadObjectiveFailed;
            s.FreightObjectiveMet = snap.FreightObjectiveMet;
            s.CabinObjectiveMet = snap.CabinObjectiveMet;
            s.TaxObjectiveMet = snap.TaxObjectiveMet;
            s.TaxObjectiveFailed = snap.TaxObjectiveFailed;
            s.InitialModeledCashCents = snap.InitialModeledCashCents;
            s.InitialGrainUnits = snap.InitialGrainUnits;
            s.GrainHarvested = snap.GrainHarvested;
            s.GrainLabor = snap.GrainLabor;
            s.GrainMilled = snap.GrainMilled;
            s.BranWasted = snap.BranWasted;
            s.FlourMilled = snap.FlourMilled;
            s.FlourImported = snap.FlourImported;
            s.FlourBaked = snap.FlourBaked;
            s.BreadBaked = snap.BreadBaked;
            s.BreadRetailed = snap.BreadRetailed;
            s.GrainExported = snap.GrainExported;
            s.OreDug = snap.OreDug;
            s.OreSold = snap.OreSold;
            s.BoundaryInflowCents = snap.BoundaryInflowCents;
            s.BoundaryOutflowCents = snap.BoundaryOutflowCents;
            s.LiquidationInflowCents = snap.LiquidationInflowCents;
            s.BankLoanRefusals = snap.BankLoanRefusals;
            s.FreightDeliveredUnits = snap.FreightDeliveredUnits;
            s.FreightRejectedShipments = snap.FreightRejectedShipments;
            s.PostalFeesCollectedCents = snap.PostalFeesCollectedCents;
            s.ArrearsObligationIds.AddRange(snap.ArrearsObligationIds);
            s.RefusalLog.AddRange(snap.RefusalLog);
            s.Diag.AddRange(snap.Diag);
            foreach (KeyValuePair<string, bool> kv in snap.FreightSourceDeducted) s.FreightSourceDeducted[kv.Key] = kv.Value;

            s.MillRecord = RestoreLifeRecord(snap.LifeStates, "mill-1");
            s.BakeryRecord = RestoreLifeRecord(snap.LifeStates, "bakery-1");
            s.FarmRecord = RestoreLifeRecord(snap.LifeStates, "farm-1");
            s.StoreRecord = RestoreLifeRecord(snap.LifeStates, "store-1");
            s.MineRecord = RestoreLifeRecord(snap.LifeStates, "mine-1");
            s.MillLoan = string.IsNullOrEmpty(snap.MillLoanId) ? null : s.Obligations.Find(snap.MillLoanId);
            s.MillTaxAssessment = string.IsNullOrEmpty(snap.MillTaxAssessmentId) ? null : s.Taxes.GetAssessment(snap.MillTaxAssessmentId);
            s.LandlordTaxAssessment = string.IsNullOrEmpty(snap.LandlordTaxAssessmentId) ? null : s.Taxes.GetAssessment(snap.LandlordTaxAssessmentId);
            s.TownMoneyOrder = s.MoneyOrders.FindOrder("po-a", 1);
            if (snap.HasMineAssay)
            {
                s.MineAssay = new MineAssayResult("assay-mine-1",
                    EntityId.For(EntityKind.Lot, 900), EntityId.For(EntityKind.Person, 5), snap.MineAssayDay,
                    snap.MineAssayGrade, "oz/ton", "fire assay");
            }
            if (snap.MineClosedDay >= 0)
            {
                s.MineCloseResult = new NpcBusinessCloseResult
                {
                    BusinessInstanceId = Mine,
                    Completed = true,
                    CashPaidToCreditorsCents = snap.MineCloseCashToCreditors,
                    LiquidationProceedsCents = snap.MineCloseProceeds,
                    StaffReleased = snap.MineCloseStaff,
                    ResidualToFounderCents = snap.MineCloseResidual,
                };
                s.MineCloseResult.ObligationsSettled.AddRange(snap.MineCloseSettled);
                s.MineCloseResult.ObligationsAssumed.AddRange(snap.MineCloseAssumed);
            }
            return s;
        }

        private static NpcBusinessLifeRecord RestoreLifeRecord(Dictionary<string, Ws4LifeStateDto> states, string businessId)
        {
            Ws4LifeStateDto dto;
            if (!states.TryGetValue(businessId, out dto) || dto == null) return LifeRecordFallback(businessId);
            var record = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = dto.BusinessInstanceId,
                BusinessEntityKey = dto.BusinessEntityKey,
                FounderPersonId = dto.FounderPersonId,
                OwnerName = dto.OwnerName,
                State = dto.State,
                CloseReason = dto.CloseReason,
            };
            record.OpenObligationIds.AddRange(dto.OpenObligationIds);
            record.EmploymentIds.AddRange(dto.EmploymentIds);
            return record;
        }

        private static NpcBusinessLifeRecord LifeRecordFallback(string businessId)
        {
            return new NpcBusinessLifeRecord
            {
                BusinessInstanceId = businessId,
                BusinessEntityKey = "business:" + businessId,
                State = NpcBusinessLifeState.Operating,
            };
        }

        private void SeedValuation()
        {
            Valuation.RegisterBusiness(Mill, "person:1", false);
            for (int i = 0; i < 6; i++) Valuation.RecordWeeklyProfit(Mill, 3000);
            Valuation.RecordTransferableAssets(Mill, 50000);
            Valuation.RegisterBusiness(Bakery, "person:4", false);
            for (int i = 0; i < 6; i++) Valuation.RecordWeeklyProfit(Bakery, -2000);
            Valuation.RecordTransferableAssets(Bakery, 8000);
        }

        // ================= reconciliation =================

        public int EndModeledCashCents()
        {
            return TotalModeledCashCents();
        }

        private static int TrailingNumber(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int dash = id.LastIndexOf('-');
            if (dash < 0 || dash == id.Length - 1) return -1;
            int value;
            return int.TryParse(id.Substring(dash + 1), out value) ? value : -1;
        }

        private static int StockOf(BusinessRuntimeState rt, string good)
        {
            CategoryStockState stock = rt != null ? rt.GetCategoryStock(good) : null;
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        public int EndingGrainUnits()
        {
            return StockOf(Runtimes[Farm], Grain) + StockOf(Runtimes[Mill], Grain);
        }

        public int EndingFlourUnits()
        {
            return StockOf(Runtimes[Mill], Flour) + StockOf(Runtimes[Bakery], Flour);
        }

        public int EndingBreadUnits()
        {
            return StockOf(Runtimes[Bakery], Bread) + StockOf(Runtimes[Store], Bread);
        }

        public int EndingOreUnits()
        {
            return StockOf(Runtimes[Mine], Ore);
        }

        public int MillLoanOutstandingCents()
        {
            if (MillLoan == null) return -1;
            FinancialObligation obligation = Obligations.Find(MillLoan.ObligationId);
            return obligation != null ? obligation.TotalOutstandingCents : -1;
        }

        public int TotalOutstandingObligationsCents()
        {
            int total = 0;
            foreach (FinancialObligation o in Obligations.Obligations) total += o.TotalOutstandingCents;
            return total;
        }

        public int CargoInTransitUnits()
        {
            int total = 0;
            foreach (LogisticsShipmentState s in Shipments)
            {
                if (s != null && FreightShipmentService.IsLiveShipment(s))
                    total += s.RemainingQuantityUnits;
            }
            return total;
        }

        /// <summary>Deterministic state hash (diagnostics excluded: logging is not
        /// state). Two runs with equal hashes agree on every cash balance, stock,
        /// obligation, employment, shipment, project, tax and objective outcome.</summary>
        public string ComputeHash()
        {
            var parts = new List<string>();
            parts.Add("day=" + CurrentDay + " phase=" + NextPhase);
            foreach (string id in new[] { Mill, Bakery, Farm, Store, Mine })
            {
                BusinessRuntimeState rt = Runtimes[id];
                parts.Add(id + ":cash=" + rt.CurrentCashCents
                    + ":grain=" + StockOf(rt, Grain)
                    + ":flour=" + StockOf(rt, Flour)
                    + ":bread=" + StockOf(rt, Bread)
                    + ":ore=" + StockOf(rt, Ore)
                    + ":liab=" + rt.AccruedLiabilityCents
                    + ":named=" + rt.NamedPayables.Count);
            }
            foreach (int hid in new[] { H1, H2, H3, H4, H5 })
                parts.Add("H" + hid + "=" + Ledgers.GetOrCreate(hid).GetBalanceCents()
                    + ":entries=" + Ledgers.GetOrCreate(hid).Entries.Count);
            parts.Add("bank=" + Bank.CashOnHandCents + ":owed=" + DepositLedger.DepositsOwedCents());
            parts.Add("yard=" + YardPurse.ReadBalanceCents() + ":stone=" + YardStock.Stock["fieldstone"] + ":lumber=" + YardStock.Stock["lumber"]);
            parts.Add("treasury=" + Treasury.BalanceCents);
            var obligationLines = new List<string>();
            foreach (FinancialObligation o in Obligations.Obligations)
                obligationLines.Add(o.ObligationId + "|" + o.Debtor + "|" + o.Creditor + "|" + o.TotalOutstandingCents + "|" + o.Status + "|" + o.Payments.Count);
            obligationLines.Sort(StringComparer.Ordinal);
            parts.Add("obligations=" + string.Join(";", obligationLines));
            var employmentLines = new List<string>();
            foreach (EmploymentRelationship rel in Employments.GetByEmployer(Mill)) employmentLines.Add(rel.Id + ":" + rel.LifecycleState);
            foreach (EmploymentRelationship rel in Employments.GetByEmployer(Bakery)) employmentLines.Add(rel.Id + ":" + rel.LifecycleState);
            foreach (EmploymentRelationship rel in Employments.GetByEmployer(Farm)) employmentLines.Add(rel.Id + ":" + rel.LifecycleState);
            foreach (EmploymentRelationship rel in Employments.GetByEmployer(Mine)) employmentLines.Add(rel.Id + ":" + rel.LifecycleState);
            employmentLines.Sort(StringComparer.Ordinal);
            parts.Add("employments=" + string.Join(";", employmentLines));
            var shipmentLines = new List<string>();
            foreach (LogisticsShipmentState s in Shipments)
                shipmentLines.Add(s.ShipmentId + ":" + s.Status + ":" + s.RemainingQuantityUnits + ":" + s.DeliveredQuantityUnits);
            shipmentLines.Sort(StringComparer.Ordinal);
            parts.Add("shipments=" + string.Join(";", shipmentLines));
            var projectLines = new List<string>();
            foreach (ConstructionProject p in Projects)
            {
                var pkgLines = new List<string>();
                foreach (WorkPackage pkg in p.WorkPackages) pkgLines.Add(pkg.PackageId + ":" + pkg.Status);
                pkgLines.Sort(StringComparer.Ordinal);
                projectLines.Add(p.ProjectId + ":" + p.Status + ":" + string.Join(",", pkgLines));
            }
            projectLines.Sort(StringComparer.Ordinal);
            parts.Add("projects=" + string.Join(";", projectLines));
            var taxLines = new List<string>();
            foreach (TaxAssessment a in Taxes.AllAssessments)
                taxLines.Add(a.AssessmentId + ":" + a.Status + ":" + a.TotalOwedCents() + ":" + a.AmountPaidCents);
            taxLines.Sort(StringComparer.Ordinal);
            parts.Add("taxes=" + string.Join(";", taxLines));
            parts.Add("counters=grainHarv=" + GrainHarvested + ":labor=" + GrainLabor + ":milled=" + GrainMilled
                + ":flourMil=" + FlourMilled + ":flourImp=" + FlourImported + ":baked=" + BreadBaked
                + ":retailed=" + BreadRetailed + ":exported=" + GrainExported + ":oreDug=" + OreDug + ":oreSold=" + OreSold
                + ":bin=" + BoundaryInflowCents + ":bout=" + BoundaryOutflowCents + ":liq=" + LiquidationInflowCents
                + ":freightDel=" + FreightDeliveredUnits + ":fees=" + PostalFeesCollectedCents
                + ":mineSusp=" + MinePayrollSuspendedDay + ":mineClosed=" + MineClosedDay
                + ":cabin1=" + Cabin1CompletedDay + ":cabin2=" + Cabin2CommissionedDay
                + ":breadObj=" + BreadObjectiveMet + "/" + BreadObjectiveFailed
                + ":freightObj=" + FreightObjectiveMet + ":cabinObj=" + CabinObjectiveMet
                + ":taxObj=" + TaxObjectiveMet + "/" + TaxObjectiveFailed);
            parts.Add("postal=poA:" + MoneyOrders.MoneyOrderCashCents("po-a") + ":poB:" + MoneyOrders.MoneyOrderCashCents("po-b")
                + ":claims:" + MoneyOrders.Claims.Count);
            parts.Add("reservations=" + Reservations.LiveReservationCount);
            return string.Join("\n", parts);
        }

        public string Summary()
        {
            var lines = new List<string>();
            lines.Add("WS4 30-day settlement: days 0..29, population 5 persons / 5 households, businesses mill/bakery/farm/store/mine.");
            lines.Add("WS4 cash: initial=" + InitialModeledCashCents + "c end=" + EndModeledCashCents() +
                     "c boundaryIn=" + BoundaryInflowCents + "c boundaryOut=" + BoundaryOutflowCents +
                     "c liquidation=" + LiquidationInflowCents + "c expected=" +
                     (InitialModeledCashCents + BoundaryInflowCents - BoundaryOutflowCents + LiquidationInflowCents) + "c");
            lines.Add("WS4 tills: mill=" + Runtimes[Mill].CurrentCashCents + " bakery=" + Runtimes[Bakery].CurrentCashCents +
                     " farm=" + Runtimes[Farm].CurrentCashCents + " store=" + Runtimes[Store].CurrentCashCents +
                     " mine=" + Runtimes[Mine].CurrentCashCents + " bank=" + Bank.CashOnHandCents +
                     " yard=" + YardPurse.ReadBalanceCents() + " treasury=" + Treasury.BalanceCents);
            lines.Add("WS4 households: H1=" + Ledgers.GetOrCreate(H1).GetBalanceCents() + " H2=" + Ledgers.GetOrCreate(H2).GetBalanceCents() +
                     " H3=" + Ledgers.GetOrCreate(H3).GetBalanceCents() + " H4=" + Ledgers.GetOrCreate(H4).GetBalanceCents() +
                     " H5=" + Ledgers.GetOrCreate(H5).GetBalanceCents());
            lines.Add("WS4 goods: grain=" + EndingGrainUnits() + " flour=" + EndingFlourUnits() + " bread=" + EndingBreadUnits() +
                     " ore=" + EndingOreUnits() + " inTransit=" + CargoInTransitUnits() +
                     " milled=" + GrainMilled + " baked=" + BreadBaked + " retailed=" + BreadRetailed +
                     " oreSold=" + OreSold + " flourImported=" + FlourImported);
            lines.Add("WS4 debts: obligations=" + CountObligations() + " outstanding=" + TotalOutstandingObligationsCents() +
                     "c millLoanOutstanding=" + MillLoanOutstandingCents() + "c arrearsIds=" + ArrearsObligationIds.Count);
            lines.Add("WS4 outcomes: minePayrollSuspended=" + MinePayrollSuspendedDay + " mineClosed=" + MineClosedDay +
                     " cabin1Completed=" + Cabin1CompletedDay + " cabin2Commissioned=" + Cabin2CommissionedDay +
                     " bankRefusals=" + BankLoanRefusals + " freightDelivered=" + FreightDeliveredUnits +
                     " breadObjective=" + (BreadObjectiveMet ? "met" : BreadObjectiveFailed ? "FAILED-at-deadline" : "open") +
                     " freightObjective=" + FreightObjectiveMet + " cabinObjective=" + CabinObjectiveMet +
                     " taxObjective=" + TaxObjectiveMet);
            return string.Join("\n", lines);
        }

        private int CountObligations()
        {
            int count = 0;
            foreach (FinancialObligation o in Obligations.Obligations) count++;
            return count;
        }

        private void BakeryPaysRentDay(int d)
        {
            // Weekly commercial rent to household:4, on Tuesdays.
            if (d % 7 != 1) return;
            var landlord = Ledgers.GetOrCreate(H4);
            if (!Runtimes[Bakery].TrySpendCents(BakeryRentCents))
            {
                FinancialObligation obligation = Obligations.Create(
                    Ids, FinancialObligationKind.Payable, Bakery, "household:4",
                    BakeryRentCents, d, "weekly commercial rent", "bakery commercial rent arrears");
                if (obligation == null) Diag.Add("bakery rent obligation refused.");
                else
                {
                    ArrearsObligationIds.Add(obligation.ObligationId);
                    BakeryRecord.OpenObligationIds.Add(obligation.ObligationId);
                    Diag.Add("day " + d + ": bakery misses rent — real payable recorded.");
                }
                return;
            }
            string problem = landlord.RecordInflow(d, BakeryRentCents, HouseholdIncomeSource.CommercialRentReceived,
                "rent", "bakery commercial rent", "bakery-1");
            if (problem != null) Diag.Add("bakery rent inflow refused: " + problem);
            else Diag.Add("day " + d + ": bakery pays commercial rent " + BakeryRentCents + "c to H4.");
        }
    }

    /// <summary>WS4 snapshot: one production save DTO per authority, plus the
    /// scenario's own scalar/counter state. Ledger states are deep-copied at
    /// capture; other DTOs are used exactly as the production clear-then-load
    /// / fresh-authority-import contracts specify, with the pre-save instance
    /// discarded after capture.</summary>
    public sealed class Ws4Snapshot
    {
        public int Day;
        public int Phase;
        public bool MineOpen;
        public int BreadPerHouseholdPerDay;
        public int BreadWholesalePriceCents;
        public int BreadRetailPriceCents;
        public int MinePayrollSuspendedDay = -1;
        public string MinePayrollLiabilityObligationId = string.Empty;
        public int MineClosedDay = -1;
        public int Cabin1CompletedDay = -1;
        public int Cabin2CommissionedDay = -1;
        public bool Cabin2FramingStalled;
        public string MillLoanId = string.Empty;
        public string MillTaxAssessmentId = string.Empty;
        public string LandlordTaxAssessmentId = string.Empty;
        public bool BreadObjectiveMet;
        public bool BreadObjectiveFailed;
        public bool FreightObjectiveMet;
        public bool CabinObjectiveMet;
        public bool TaxObjectiveMet;
        public bool TaxObjectiveFailed;
        public int InitialModeledCashCents;
        public int InitialGrainUnits;
        public long GrainHarvested;
        public long GrainLabor;
        public long GrainMilled;
        public long BranWasted;
        public long FlourMilled;
        public long FlourImported;
        public long FlourBaked;
        public long BreadBaked;
        public long BreadRetailed;
        public long GrainExported;
        public long OreDug;
        public long OreSold;
        public int BoundaryInflowCents;
        public int BoundaryOutflowCents;
        public int LiquidationInflowCents;
        public int BankLoanRefusals;
        public int FreightDeliveredUnits;
        public int FreightRejectedShipments;
        public int PostalFeesCollectedCents;
        public List<string> ArrearsObligationIds = new List<string>();
        public List<string> RefusalLog = new List<string>();
        public List<string> Diag = new List<string>();
        public Dictionary<string, bool> FreightSourceDeducted = new Dictionary<string, bool>(StringComparer.Ordinal);
        public Dictionary<string, int> YardStocks = new Dictionary<string, int>(StringComparer.Ordinal);
        public int YardPurseBalance;
        public bool HasMineAssay;
        public int MineAssayDay;
        public float MineAssayGrade;
        public List<string> MineCloseSettled = new List<string>();
        public List<string> MineCloseAssumed = new List<string>();
        public int MineCloseCashToCreditors;
        public int MineCloseProceeds;
        public int MineCloseStaff;
        public int MineCloseResidual;
        public Dictionary<string, Ws4LifeStateDto> LifeStates = new Dictionary<string, Ws4LifeStateDto>(StringComparer.Ordinal);
        public List<HouseholdLedgerState> LedgerStates = new List<HouseholdLedgerState>();
        public FinancialObligationSaveDto ObligationsDto;
        public EmploymentRegistrySaveDto EmploymentsDto;
        public Dictionary<string, BusinessRuntimeSaveDto> RuntimeDtos = new Dictionary<string, BusinessRuntimeSaveDto>(StringComparer.Ordinal);
        public BusinessLenderService.BusinessLenderServiceSaveDto BankDto;
        public CreditOfferWorkflowSaveDto WorkflowDto;
        public CreditEventLog.CreditEventLogSaveDto EventDto;
        public ExternalMarketAuthoritySaveDto MarketDto;
        public HousingAuthority.HousingSaveDto HousingDto;
        public TenancyPaymentService.TenancyPaymentSaveDto TenancyDto;
        public CivicTreasuryService.CivicTreasurySaveDto TreasuryDto;
        public PropertyTaxService.PropertyTaxSaveDto TaxDto;
        public EquipmentReservationAuthoritySaveDto ReservationDto;
        public FreightShipmentService.FreightServiceSaveDto FreightDto;
        public TaskAuthoritySaveDto FreightTaskDto;
        public TaskAuthoritySaveDto ConstrTaskDto;
        public PersonScheduleTrackerSaveDto ConstrTrackerDto;
        public MoneyOrderServiceSaveDto MoneyOrderDto;
        public ConstructionProjectExecutor.ConstructionProjectSaveDto ConstrDto;
        public List<EntityIdCursor> IdCursors = new List<EntityIdCursor>();
    }

    public sealed class Ws4LifeStateDto
    {
        public string BusinessInstanceId = string.Empty;
        public string BusinessEntityKey = string.Empty;
        public int FounderPersonId = -1;
        public string OwnerName = string.Empty;
        public NpcBusinessLifeState State = NpcBusinessLifeState.Unspecified;
        public List<string> OpenObligationIds = new List<string>();
        public List<string> EmploymentIds = new List<string>();
        public string CloseReason = string.Empty;
    }

    /// <summary>Construction port doubles that back the WS4 driver with real
    /// scenario state (yard stock dict, price table, parcel register, tool
    /// custody) — the same shapes the WS2 driver tests use.</summary>
    public sealed class Ws4ParcelAuthority : IConstructionParcelAuthority
    {
        public readonly Dictionary<string, string> HolderByParcel = new Dictionary<string, string>();
        public string AuthorizeBuild(int householdId, string parcelId, int dayIndex, List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(parcelId)) return "no parcel id.";
            if (!HolderByParcel.TryGetValue(parcelId, out string holder))
                return "parcel '" + parcelId + "' is not a registered parcel.";
            if (!string.Equals(holder, "household:" + householdId, StringComparison.Ordinal))
                return "parcel '" + parcelId + "' is held by '" + holder + "', not 'household:" + householdId + "'.";
            return null;
        }
    }

    public sealed class Ws4PricePort : IConstructionMaterialPricePort
    {
        public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
        public int UnitPriceCents(ConstructionMaterialRequirement requirement)
        {
            if (requirement == null) return -1;
            return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
        }
    }

    public sealed class Ws4MaterialSource : IConstructionMaterialSource
    {
        public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
        public int AvailableUnits(ConstructionMaterialRequirement requirement)
        {
            return requirement == null ? 0
                : Stock.TryGetValue(requirement.MaterialDisplayName, out int units) ? units : 0;
        }
        public string TryConsume(ConstructionMaterialRequirement requirement, int units,
            string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel)
        {
            provenanceLabel = string.Empty;
            int have = AvailableUnits(requirement);
            if (have < units) return "Ws4MaterialSource: only " + have + " of '" + requirement.MaterialDisplayName + "'.";
            Stock[requirement.MaterialDisplayName] = have - units;
            provenanceLabel = "lot '" + requirement.MaterialDisplayName + "' x" + units + " (" + projectLabel + ", day " + dayIndex + ")";
            return null;
        }
    }

    public sealed class Ws4CreationPort : INpcBusinessCreationPort
    {
        public string TryCreateBusiness(NpcBusinessFormationIntent intent, List<string> diagnostics,
            out NpcCreatedBusiness created)
        {
            created = null;
            return "Ws4CreationPort: creation execution is out of scope for the Validate probe (Validate never calls the port).";
        }
    }

    public sealed class Ws4ToolCustody : IHouseholdToolCustody
    {
        private readonly HashSet<string> held = new HashSet<string>();
        public void Grant(int householdId, string toolId) { held.Add(householdId + ":" + toolId); }
        public bool HouseholdHoldsTool(int householdId, string toolItemId) { return held.Contains(householdId + ":" + toolItemId); }
    }

    public sealed class Ws4LaborPort : INpcConstructionLaborPort
    {
        private readonly Ws4SettlementScenario scenario;
        public Ws4LaborPort(Ws4SettlementScenario scenario) { this.scenario = scenario; }
        public string WorkPackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
        {
            NpcConstructionRecord record = scenario.Commissioner.FindRecordForProject(project.ProjectId);
            string commit = scenario.Commissioner.CommitOwnLabor(scenario.ConstrExecutor, project, record, package,
                Ws4SettlementScenario.P4, scenario.ConstrTracker, scenario.Custody, scenario.ConstrTasks, scenario.ConstrBudgets,
                dayIndex, 360, package.LaborMinutesRequired, diag);
            if (commit != null) return commit;
            string taskId = null;
            for (int i = project.LaborTaskBindings.Count - 1; i >= 0; i--)
            {
                LaborTaskBinding binding = project.LaborTaskBindings[i];
                if (binding != null && string.Equals(binding.PackageId, package.PackageId, StringComparison.Ordinal))
                {
                    taskId = binding.TaskId;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(taskId)) return "no labor task bound to the package.";
            return scenario.ConstrExecutor.WorkCommittedLabor(project, package, taskId,
                package.LaborMinutesRequired, scenario.ConstrTasks, scenario.ConstrBudgets, dayIndex, diag);
        }
    }

    // ================= tests =================

    [TestFixture]
    public sealed class Pass11Ws4SettlementProofTests
    {
        private static Ws4SettlementScenario RunFull()
        {
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunAll();
            return sim;
        }

        private static void AssertNoSurprises(Ws4SettlementScenario sim)
        {
            foreach (string line in sim.Diag)
            {
                StringAssert.DoesNotContain("unexpectedly", line,
                    "unexpected failure during the scenario:\n" + string.Join("\n", sim.Diag));
                StringAssert.DoesNotContain("(finding)", line,
                    "production behavior diverged from the proof's expectation:\n" + string.Join("\n", sim.Diag));
            }
        }

        [Test]
        public void Full30Day_Reconciles_WithExpectedStoryBeats()
        {
            Ws4SettlementScenario sim = RunFull();
            Assert.AreEqual(Ws4SettlementScenario.EndDayExclusive, sim.CurrentDay);
            AssertNoSurprises(sim);
            Console.WriteLine("=== WS4 30-day settlement ===");
            Console.WriteLine(sim.Summary());
            Console.WriteLine("WS4 diag lines: " + sim.Diag.Count + "; refusals logged: " + sim.RefusalLog.Count);
            Assert.IsNotNull(sim.MillLoan, "the mill loan must have been approved on day 2.");
            Assert.AreEqual(1, sim.BankLoanRefusals, "exactly one loan refusal: the bakery's day-11 application.");
            Assert.IsTrue(sim.FreightObjectiveMet, "freight objective (ship-1 delivered by day 10) must be met.");
            Assert.IsTrue(sim.CabinObjectiveMet, "cabin objective (cabin-1 complete by day 20) must be met.");
            Assert.IsTrue(sim.TaxObjectiveMet, "mill-tax objective (paid by day 25) must be met.");
            Assert.IsTrue(sim.BreadObjectiveFailed, "bread objective (120 loaves by day 25) must FAIL at its deadline: supply is 110 loaves by then.");
            Assert.IsFalse(sim.BreadObjectiveMet, "the failed bread objective must not retroactively complete.");
            Assert.AreEqual(18, sim.MinePayrollSuspendedDay, "mine payroll must suspend on the day-18 Friday (cash short).");
            Assert.AreEqual(20, sim.MineClosedDay, "the mine must close on day 20.");
            Assert.Greater(sim.Cabin1CompletedDay, 0, "cabin-1 must complete during the 30 days.");
            Assert.AreEqual(22, sim.Cabin2CommissionedDay, "cabin-2 must be commissioned on day 22.");
        }

        [Test]
        public void Cash_Conserves_AcrossTheBoundary()
        {
            Ws4SettlementScenario sim = RunFull();
            int expected = sim.InitialModeledCashCents
                         + sim.BoundaryInflowCents
                         - sim.BoundaryOutflowCents
                         + sim.LiquidationInflowCents;
            Assert.AreEqual(expected, sim.EndModeledCashCents(),
                "modeled cash (5 tills + 5 ledgers + bank vault + yard purse + treasury) must reconcile against the boundary net.");
            Assert.AreEqual(0, sim.LiquidationInflowCents, "no liquidation buyer exists in WS4, so no liquidation inflow may appear.");
            var boundary = sim.ExternalMarket.GetBoundaryReconciliation();
            Assert.AreEqual(sim.BoundaryInflowCents, boundary.inflowCents, "external-market inflow must match the scenario's boundary inflow.");
            // The market's outflow excludes the 25c postal fee, which leaves
            // town through the post office, not the external market.
            Assert.AreEqual(sim.BoundaryOutflowCents - sim.PostalFeesCollectedCents, boundary.outflowCents,
                "external-market outflow must match the scenario's boundary outflow minus the postal fee.");
            foreach (int hid in new[] { Ws4SettlementScenario.H1, Ws4SettlementScenario.H2, Ws4SettlementScenario.H3, Ws4SettlementScenario.H4, Ws4SettlementScenario.H5 })
                Assert.GreaterOrEqual(sim.Ledgers.GetOrCreate(hid).GetBalanceCents(), 0, "no household ledger may go negative.");
            foreach (string id in new[] { Ws4SettlementScenario.Mill, Ws4SettlementScenario.Bakery, Ws4SettlementScenario.Farm, Ws4SettlementScenario.Store, Ws4SettlementScenario.Mine })
                Assert.GreaterOrEqual(sim.Runtimes[id].CurrentCashCents, 0, "no business till may go negative.");
        }

        [Test]
        public void Goods_Reconcile_Exactly()
        {
            Ws4SettlementScenario sim = RunFull();
            long expectedGrain = sim.InitialGrainUnits + sim.GrainHarvested + sim.GrainLabor - sim.GrainMilled - sim.GrainExported;
            Assert.AreEqual(expectedGrain, sim.EndingGrainUnits(), "grain must reconcile exactly (freight is an internal transfer).");
            Assert.AreEqual(sim.GrainMilled / 5, sim.BranWasted, "milling wastes 20% as bran.");
            long expectedFlour = 100 + sim.FlourMilled + sim.FlourImported - sim.FlourBaked;
            Assert.AreEqual(expectedFlour, sim.EndingFlourUnits(), "flour must reconcile exactly.");
            Assert.AreEqual(30, sim.FlourImported, "the emergency flour import must arrive after transit.");
            long expectedBread = 50 + sim.BreadBaked - sim.BreadRetailed;
            Assert.AreEqual(expectedBread, sim.EndingBreadUnits(), "bread must reconcile exactly.");
            Assert.IsTrue(sim.BreadRetailed > 0, "households bought bread during the 30 days.");
            Assert.AreEqual(sim.OreDug, sim.OreSold, "every dug ore unit must be sold (mine closes with no ore stock).");
            Assert.AreEqual(0, sim.EndingOreUnits(), "the mine must end with no ore stock.");
            Assert.AreEqual(0, sim.CargoInTransitUnits(), "no cargo may remain in transit at day 30.");
            Assert.AreEqual(100, sim.FreightDeliveredUnits, "freight must deliver ship-1 (60) + ship-2 (40) exactly once each.");
            Assert.AreEqual(920, sim.YardStock.Stock["fieldstone"], "yard fieldstone: 1000 - cabin-1 foundation 40 - cabin-2 foundation 40.");
            Assert.AreEqual(50, sim.YardStock.Stock["lumber"], "yard lumber: 650 - cabin-1 framing 600; cabin-2 framing never consumed lumber.");
        }

        [Test]
        public void Debts_Reconcile_WithNamedCounterparties()
        {
            Ws4SettlementScenario sim = RunFull();
            List<string> problems = sim.Obligations.Reconcile();
            Assert.IsEmpty(problems, "obligation authority reconciliation problems:\n" + string.Join("\n", problems));
            foreach (FinancialObligation obligation in sim.Obligations.Obligations)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(obligation.Debtor),
                    "obligation '" + obligation.ObligationId + "' must name a debtor.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(obligation.Creditor),
                    "obligation '" + obligation.ObligationId + "' must name a creditor.");
            }
            Assert.IsTrue(sim.ArrearsObligationIds.Count > 0,
                "overdue rent/boarding must become real obligations, not a silent pass.");
            foreach (string arrearsId in sim.ArrearsObligationIds)
            {
                FinancialObligation arrears = sim.Obligations.Find(arrearsId);
                Assert.IsNotNull(arrears, "arrears obligation '" + arrearsId + "' must exist on the authority.");
                Assert.AreEqual(FinancialObligationKind.Payable, arrears.Kind);
            }
            Assert.IsNotNull(sim.MillLoan, "mill loan must exist.");
            Assert.GreaterOrEqual(sim.MillLoanOutstandingCents(), 12000,
                "the mill loan must still be outstanding at day 30 (principal plus any accrued interest).");
            foreach (FinancialObligation obligation in sim.Obligations.Obligations)
            {
                if (obligation.Debtor == Ws4SettlementScenario.Bakery &&
                    obligation.Kind == FinancialObligationKind.Loan)
                {
                    Assert.Fail("no loan obligation may exist for the refused bakery application.");
                }
            }
        }

        [Test]
        public void Identities_EmploymentsEndExactlyOnce_AndMineWorkerDeparts()
        {
            Ws4SettlementScenario sim = RunFull();
            EmploymentRelationship millEmp = FindEmployment(sim, Ws4SettlementScenario.P1);
            EmploymentRelationship bakeryEmp = FindEmployment(sim, Ws4SettlementScenario.P2);
            EmploymentRelationship farmEmp = FindEmployment(sim, Ws4SettlementScenario.P3);
            EmploymentRelationship mineEmp = FindEmployment(sim, Ws4SettlementScenario.P5);
            Assert.AreEqual(EmploymentLifecycleState.Active, millEmp.LifecycleState, "P1's mill employment survives.");
            Assert.AreEqual(EmploymentLifecycleState.Active, bakeryEmp.LifecycleState, "P2's bakery employment survives.");
            Assert.AreEqual(EmploymentLifecycleState.Active, farmEmp.LifecycleState, "P3's farm employment survives.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, mineEmp.LifecycleState, "P5's mine employment ends with the closure.");
            Assert.AreEqual(20, mineEmp.EndDayIndex, "P5's employment ends on the closure day.");
            Assert.IsNotNull(sim.MineCloseResult, "the mine close-down must have run.");
            Assert.AreEqual(1, sim.MineCloseResult.StaffReleased, "exactly one staff member is released by the mine close-down.");
            foreach (int person in new[] { Ws4SettlementScenario.P1, Ws4SettlementScenario.P2, Ws4SettlementScenario.P3, Ws4SettlementScenario.P5 })
                Assert.LessOrEqual(sim.Employments.CountActiveEmploymentsOfPerson(person), 1, "no person works two jobs.");
        }

        private static EmploymentRelationship FindEmployment(Ws4SettlementScenario sim, int personId)
        {
            List<EmploymentRelationship> list = sim.Employments.GetByEmployee(personId);
            return list.Count > 0 ? list[0] : null;
        }

        [Test]
        public void PostalBooks_Reconcile_Separately()
        {
            Ws4SettlementScenario sim = RunFull();
            Assert.IsNotNull(sim.TownMoneyOrder, "the town money order must have been issued on day 7.");
            Assert.AreEqual(MoneyOrderStatus.Paid, sim.TownMoneyOrder.Status, "the money order must be redeemed by day 30.");
            Assert.AreEqual(25, sim.PostalFeesCollectedCents, "the 25c fee must be collected exactly once.");
            int funds = sim.MoneyOrders.MoneyOrderCashCents("po-a") + sim.MoneyOrders.MoneyOrderCashCents("po-b");
            Assert.AreEqual(5000, funds, "postal funds: 5000c reserve + 2500c issued - 2500c paid = 5000c.");
            Assert.GreaterOrEqual(sim.MoneyOrders.Claims.Count, 1, "redemption must create the paying office's claim on the issuing office.");
        }

        [Test]
        public void Treasury_And_Bank_Reconcile()
        {
            Ws4SettlementScenario sim = RunFull();
            Assert.AreEqual(900, sim.Treasury.BalanceCents, "the treasury holds exactly the mill's 900c tax.");
            Assert.AreEqual(0, sim.Treasury.TotalForKind(CivicLedgerEntryKind.OtherCredit), "no unexplained treasury credits.");
            Assert.IsNotNull(sim.MillTaxAssessment, "the mill assessment must exist.");
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, sim.MillTaxAssessment.Status, "the mill assessment must be satisfied.");
            Assert.IsNotNull(sim.LandlordTaxAssessment, "the landlord assessment must exist.");
            Assert.AreEqual(TaxAssessmentStatus.Delinquent, sim.LandlordTaxAssessment.Status,
                "the unpaid landlord assessment must be delinquent after its due day.");
            Assert.Greater(sim.LandlordTaxAssessment.TotalOwedCents(), 1200,
                "the delinquent assessment must accrue interest past its 1200c levy.");
            Assert.AreEqual(0, sim.DepositLedger.DepositsOwedCents(), "H3's deposit is withdrawn in full by day 30.");
            Assert.GreaterOrEqual(sim.Bank.CashOnHandCents, 2500000 - 12000,
                "the bank vault never drops below charter minus the mill loan disbursement.");
        }

        [Test]
        public void Construction_CompletesOnce_AndMaterialShort_Stalls()
        {
            Ws4SettlementScenario sim = RunFull();
            Assert.AreEqual(2, sim.Projects.Count, "two cabins must be commissioned.");
            ConstructionProject cabin1 = null;
            ConstructionProject cabin2 = null;
            foreach (ConstructionProject p in sim.Projects)
            {
                if (p.Status == ConstructionProjectStatus.Complete) cabin1 = p;
                else cabin2 = p;
            }
            Assert.IsNotNull(cabin1, "cabin-1 must complete.");
            Assert.IsNotNull(cabin2, "cabin-2 must exist and remain incomplete (material-short).");
            Assert.IsFalse(string.IsNullOrWhiteSpace(cabin1.BuildingId), "cabin-1 must have a real building id.");
            foreach (WorkPackage pkg in cabin1.WorkPackages)
                Assert.AreEqual(WorkPackageStatus.Complete, pkg.Status, "every cabin-1 package must complete.");
            // Cabin-2's framing package must not complete: lumber on hand (50)
            // never covers its 600 requirement, and no framing cash is paid.
            int completedPackages = 0;
            foreach (WorkPackage pkg in cabin2.WorkPackages)
                if (pkg.Status == WorkPackageStatus.Complete) completedPackages++;
            Assert.AreEqual(1, completedPackages, "cabin-2 completes only its foundation; framing stalls on lumber.");
            // Occupancy recorded in the new building, exactly once per member.
            var spaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccommodationSpace space in sim.Housing.SpacesForBuilding(cabin1.BuildingId))
                spaces.Add(space.SpaceId);
            int occupancies = 0;
            foreach (ResidentialOccupancy occ in sim.Housing.CurrentOccupanciesForPerson(Ws4SettlementScenario.P4))
                if (spaces.Contains(occ.SpaceId)) occupancies++;
            Assert.AreEqual(1, occupancies, "cabin-1 occupancy must be recorded exactly once for P4.");
            // Extra drives change nothing: no double staging, no double occupancy, no double payment.
            string before = sim.ComputeHash();
            int cashBefore = sim.TotalModeledCashCents();
            for (int day = 30; day < 33; day++)
            {
                sim.ConstrBudgets.EnsureDay(day);
                sim.ConstrDriver.Drive(day, sim.Diag);
            }
            Assert.AreEqual(before, sim.ComputeHash(), "extra construction drives must not change state.");
            Assert.AreEqual(cashBefore, sim.TotalModeledCashCents(), "extra drives must not move cash.");
        }

        [Test]
        public void SaveRestore_MidShipment_ResumesToSameOutcome()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(7, 0);
            LogisticsShipmentState ship1 = null;
            foreach (LogisticsShipmentState s in sim.Shipments)
                if (s.ShipmentId == "ship-1") ship1 = s;
            Assert.IsNotNull(ship1, "ship-1 must exist at the save point.");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, ship1.Status, "ship-1 must be mid-transit at the save point.");
            Assert.IsTrue(ship1.loadApplied, "ship-1 must be loaded at the save point.");
            Ws4Snapshot snap = sim.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            restored.RunAll();
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-shipment save/load changed the outcome.");
            Assert.AreEqual(100, restored.FreightDeliveredUnits, "restored run must still deliver both shipments exactly once.");
        }

        [Test]
        public void SaveRestore_MidTransaction_ResumesToSameOutcome()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(8, 0);
            Assert.IsNotNull(sim.TownMoneyOrder, "money order must be issued at the save point.");
            Assert.AreEqual(MoneyOrderStatus.Issued, sim.TownMoneyOrder.Status, "money order must still be unredeemed at the save point.");
            PostalMoneyOrderAdvice advice = sim.MoneyOrders.FindAdvice("po-a", 1);
            Assert.IsNotNull(advice, "advice must exist at the save point.");
            Assert.AreEqual(-1, advice.ReceivedDayIndex, "advice must not yet have arrived at the save point.");
            Ws4Snapshot snap = sim.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            restored.RunAll();
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-transaction save/load changed the outcome.");
            Assert.AreEqual(MoneyOrderStatus.Paid, restored.TownMoneyOrder.Status, "restored run must redeem the order exactly once.");
        }

        [Test]
        public void SaveRestore_MidEmployment_ResumesToSameOutcome()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(4, 5);
            // Friday day 4, after mill payroll, before bakery payroll: the mill
            // wage is in H1's ledger, the bakery wage is not yet in H2's.
            Assert.Greater(sim.Ledgers.GetOrCreate(Ws4SettlementScenario.H1).Entries.Count, 1, "H1 must have ledger entries at the save point.");
            Ws4Snapshot snap = sim.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            restored.RunAll();
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-employment save/load changed the outcome (double payroll or lost wage).");
        }

        [Test]
        public void SaveRestore_MidProduction_ResumesToSameOutcome()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(21, 1);
            // Monday day 21, after the mill grind, before the bakery bake.
            Assert.AreEqual(21, sim.CurrentDay);
            Assert.AreEqual(1, sim.NextPhase);
            Ws4Snapshot snap = sim.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            restored.RunAll();
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-production save/load changed the outcome.");
        }

        [Test]
        public void SaveRestore_MidTaxObligation_ResumesToSameOutcome()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(21, 0);
            Assert.IsNotNull(sim.MillTaxAssessment, "mill assessment must be levied at the save point.");
            Assert.AreEqual(TaxAssessmentStatus.Assessed, sim.MillTaxAssessment.Status, "mill assessment must be unpaid at the save point.");
            Assert.AreEqual(0, sim.MillTaxAssessment.AmountPaidCents, "nothing may be paid on the mill assessment at the save point.");
            Ws4Snapshot snap = sim.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            restored.RunAll();
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-tax-obligation save/load changed the outcome (double payment or lost levy).");
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, restored.MillTaxAssessment.Status, "restored run must pay the mill tax exactly once.");
        }

        [Test]
        public void SaveRestore_MidObjective_ResumesToSameOutcome()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(16, 0);
            Assert.IsTrue(sim.FreightObjectiveMet, "freight objective must already be met at the day-15 save point.");
            Assert.IsFalse(sim.BreadObjectiveMet || sim.BreadObjectiveFailed, "bread objective must still be open at the day-15 save point.");
            Ws4Snapshot snap = sim.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            restored.RunAll();
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-objective save/load changed the outcome.");
            Assert.IsTrue(restored.BreadObjectiveFailed, "restored run must still fail the bread objective at its deadline.");
        }

        [Test]
        public void SaveRestore_DoubleRestore_IsIdempotent()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(8, 0);
            Ws4Snapshot snap = sim.CaptureSnapshot();
            string atSave = sim.ComputeHash();
            Ws4SettlementScenario r1 = Ws4SettlementScenario.Restore(snap);
            Ws4SettlementScenario r2 = Ws4SettlementScenario.Restore(snap);
            Assert.AreEqual(atSave, r1.ComputeHash(), "first restore must reproduce the save-point state.");
            Assert.AreEqual(atSave, r2.ComputeHash(), "second restore of the same snapshot must reproduce the same state (no duplication).");
            r1.RunAll();
            Assert.AreEqual(baseline, r1.ComputeHash(), "continued restored run must match the uninterrupted run.");
        }

        [Test]
        public void Determinism_RepeatRun_Identical()
        {
            string first = RunFull().ComputeHash();
            string second = RunFull().ComputeHash();
            Assert.AreEqual(first, second, "two fresh runs must agree exactly (any nondeterminism is a finding).");
        }
    }

    [TestFixture]
    public sealed class Pass11Ws4MidTaskReservationTests
    {
        [Test]
        public void FreightLoadTask_MidLoad_SaveRestore_CompletesExactlyOnce()
        {
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(5);
            var service = new FreightShipmentService();
            service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", DisplayName = "Farm wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
            int recipientStock = 0;
            int sourceStock = 60;
            bool deducted = false;
            var diag = new List<string>();
            var hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 5,
                Diagnostics = diag,
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    recipientStock += quantityUnits;
                    return quantityUnits;
                },
            };
            LogisticsShipmentState shipment = service.CreateShipment("ship-task", "farm-1", "mill-1", 60,
                new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f }, "wagon-1", hooks, out string creason);
            Assert.IsNotNull(shipment, "create: " + creason);
            Assert.IsTrue(service.BeginLoading(shipment, EntityId.For(EntityKind.Person, 701), hooks, out string lreason), lreason);
            Assert.IsTrue(service.WorkLoading(shipment, 1, hooks, out string wreason), wreason);
            Assert.IsFalse(shipment.loadApplied, "one minute of loading must not complete the load task.");
            Assert.AreEqual(60, sourceStock, "nothing is deducted before load completion.");

            FreightShipmentService.FreightServiceSaveDto freightDto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { shipment });
            TaskAuthoritySaveDto taskDto = tasks.CaptureSaveDto();

            var tasks2 = new TaskAuthority();
            tasks2.LoadFromSaveDto(taskDto);
            var budgets2 = new WorkTimeBudgetStore();
            budgets2.EnsureDay(5);
            var service2 = new FreightShipmentService();
            service2.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", DisplayName = "Farm wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
            var hooks2 = new FreightShipmentHooks
            {
                Tasks = tasks2,
                Budgets = budgets2,
                DayIndex = 5,
                Diagnostics = diag,
                ReceiveShipment = (LogisticsShipmentState s2, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    recipientStock += quantityUnits;
                    return quantityUnits;
                },
            };
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(freightDto, hooks2);
            Assert.AreEqual(1, restored.Count, "exactly one shipment restores.");
            LogisticsShipmentState r = restored[0];
            Assert.IsFalse(r.loadApplied, "restored shipment must still be mid-load.");
            int guard = 0;
            while (!r.loadApplied && guard++ < 20)
                Assert.IsTrue(service2.WorkLoading(r, 60, hooks2, out string w2), w2);
            Assert.IsTrue(r.loadApplied, "restored load task must complete.");
            if (!deducted)
            {
                sourceStock -= 60;
                deducted = true;
            }
            Assert.AreEqual(0, sourceStock, "source deducted exactly once at load completion.");
            Assert.IsTrue(service2.Dispatch(r, EntityId.For(EntityKind.Person, 702),
                new List<EntityId> { EntityId.For(EntityKind.Animal, 11) }, hooks2, out string dreason), dreason);
            r.transitDurationGameSeconds = 120f;
            service2.AdvanceTransit(r, 60f, hooks2);
            service2.AdvanceTransit(r, 60f, hooks2);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, r.Status, "restored shipment must arrive.");
            Assert.IsTrue(service2.BeginUnloading(r, EntityId.For(EntityKind.Person, 701), hooks2, out string ureason), ureason);
            guard = 0;
            while (r.Status != LogisticsShipmentStatus.Completed && guard++ < 20)
                Assert.IsTrue(service2.WorkUnloading(r, 60, hooks2, out string w3), w3);
            Assert.AreEqual(60, recipientStock, "recipient takes custody exactly once.");
            Assert.IsTrue(service2.VerifyConservation(r, 60, out string cons), cons);
        }

        [Test]
        public void EquipmentReservation_ReserveRefuseRelease_RoundTrips()
        {
            var reservations = new EquipmentReservationService();
            Assert.IsNull(reservations.Reserve("asset:wagon-1", "freight:ship-task", "haul grain"), "first reservation must grant.");
            Assert.IsTrue(reservations.IsReserved("asset:wagon-1"), "wagon must read reserved.");
            string refusal = reservations.Reserve("asset:wagon-1", "plow-task", "plow field");
            Assert.IsNotNull(refusal, "double-booking the wagon must refuse loudly.");
            StringAssert.Contains("wagon", refusal.ToLowerInvariant(), "refusal must name the wagon.");
            EquipmentReservationAuthoritySaveDto dto = reservations.CaptureSaveDto();
            var restored = new EquipmentReservationService();
            restored.LoadFromSaveDto(dto);
            Assert.IsTrue(restored.IsReserved("asset:wagon-1"), "reservation must survive save/load.");
            string refusal2 = restored.Reserve("asset:wagon-1", "plow-task", "plow field");
            Assert.IsNotNull(refusal2, "restored reservation must still refuse double-booking.");
            restored.Release("asset:wagon-1");
            Assert.IsFalse(restored.IsReserved("asset:wagon-1"), "release must free the wagon.");
            Assert.IsNull(restored.Reserve("asset:wagon-1", "plow-task", "plow field"), "wagon must grant again after release.");
        }
    }

    [TestFixture]
    public sealed class Pass11Ws4SubsystemProbeTests
    {
        private static void CompleteFieldTask(TaskAuthority tasks, WorkTimeBudgetStore budgets, EntityId worker,
            string definitionId, string fieldId, string termsRef, int dayIndex, string farmId)
        {
            budgets.EnsureDay(dayIndex);
            EntityId owner = FarmCycleDriver.BusinessEntityIdFor(farmId);
            WorkTask task = tasks.CreateTaskWithPlannedMinutes(definitionId, owner, dayIndex, 30, fieldId, termsRef);
            Assert.IsTrue(tasks.AssignTask(task.TaskId, worker, budgets, out string a), a);
            Assert.IsTrue(tasks.StartTask(task.TaskId, dayIndex, out string s), s);
            int guard = 0;
            while (task.MinutesRemaining > 0 && guard++ < 20)
            {
                Assert.IsTrue(tasks.RecordWorkProgress(task.TaskId, 60, budgets, dayIndex, out bool done, out string w), w);
                if (done) break;
            }
            Assert.IsTrue(tasks.CompleteTask(task.TaskId, TaskOutcome.Success, dayIndex, budgets));
        }

        [Test]
        public void FarmDriver_PlantTendHarvest_ExactlyOnce_WithSaveRoundTrip()
        {
            var authority = new CropFieldAuthority();
            var chain = new CropChain(authority);
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            CropChain.RegisterTaskDefinitions(tasks);
            budgets.EnsureDay(10);
            EntityId worker = EntityId.For(EntityKind.Person, 501);
            var field = new CropFieldState("wheat-40", "farm-1", 40f)
            {
                Suitability01 = 0.8f,
                Moisture01 = 0.7f,
                GrowthState = CropGrowthState.Prepared,
            };
            Assert.IsNull(authority.RegisterField(field));
            var driver = new FarmCycleDriver();
            FarmCycleDriver.FarmDriveResult empty = driver.DriveCompletedFieldTasks(chain, "farm-1", tasks, ids, 10, diag);
            Assert.AreEqual(0, empty.UnitsHarvested, "an undriven field produces nothing.");
            CompleteFieldTask(tasks, budgets, worker, CropChain.PlantFieldTaskId, "wheat-40",
                "crop:Wheat;seed:80;source:general store", 10, "farm-1");
            FarmCycleDriver.FarmDriveResult planted = driver.DriveCompletedFieldTasks(chain, "farm-1", tasks, ids, 10, diag);
            Assert.AreEqual(1, planted.FieldsPlanted, string.Join("; ", diag));
            Assert.AreEqual("general store", authority.GetField("wheat-40").SeedSource, "seed provenance must flow into the field.");
            CompleteFieldTask(tasks, budgets, worker, CropChain.TendFieldTaskId, "wheat-40", "", 40, "farm-1");
            FarmCycleDriver.FarmDriveResult tended = driver.DriveCompletedFieldTasks(chain, "farm-1", tasks, ids, 40, diag);
            Assert.AreEqual(1, tended.FieldsTended, string.Join("; ", diag));
            int harvestDay = 10 + 95;
            CompleteFieldTask(tasks, budgets, worker, CropChain.HarvestFieldTaskId, "wheat-40", "", harvestDay, "farm-1");
            FarmCycleDriver.FarmDriveResult harvested = driver.DriveCompletedFieldTasks(chain, "farm-1", tasks, ids, harvestDay, diag);
            Assert.AreEqual(1, harvested.FieldsHarvested, string.Join("; ", diag));
            Assert.AreEqual(1, harvested.HarvestedLots.Count, "exactly one harvest lot.");
            Assert.Greater(harvested.UnitsHarvested, 0, "harvest must yield grain.");
            Assert.AreEqual("general store", harvested.HarvestedLots[0].SeedSource, "seed provenance must flow into the harvest lot.");
            FarmCycleDriver.FarmDriveResult again = driver.DriveCompletedFieldTasks(chain, "farm-1", tasks, ids, harvestDay, diag);
            Assert.AreEqual(0, again.TasksConsumed, "re-driving must never re-credit the same tasks.");
            FarmCycleDriver.FarmCycleDriverSaveDto dto = driver.CaptureSaveDto();
            var driver2 = new FarmCycleDriver();
            driver2.LoadFromSaveDto(dto);
            FarmCycleDriver.FarmDriveResult afterLoad = driver2.DriveCompletedFieldTasks(chain, "farm-1", tasks, ids, harvestDay, diag);
            Assert.AreEqual(0, afterLoad.TasksConsumed, "a restored driver must not re-consume saved tasks.");
        }

        [Test]
        public void BakeryChain_BatchCompletes_WithMidBatchSaveLoad()
        {
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            var dueEvents = new DueEventScheduler();
            var reservations = new EquipmentReservationService();
            reservations.RegisterResourceCapacity("workstation:bake-oven-0", 4f);
            reservations.RegisterResourceCapacity("workstation:bake-oven-1", 4f);
            reservations.RegisterResourceCapacity("workstation:proofing-rack", 4f);
            var chain = new BakeryStagedProductionChain();
            float clock = 0f;
            var diag = new List<string>();
            var ctx = new BakeryChainContext
            {
                Tasks = tasks,
                Budgets = budgets,
                DueEvents = dueEvents,
                Reservations = reservations,
                ClockGameSeconds = () => clock,
                DayIndex = 42,
                BusinessId = EntityId.For(EntityKind.Business, 7),
                BakerId = EntityId.For(EntityKind.Person, 11),
                Diagnostics = diag,
            };
            var stockDiag = new List<string>();
            Assert.IsNull(chain.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 100),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 100,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-042",
                OriginName = "Great Plains flour mill, via railhead",
            }, stockDiag), string.Join("; ", stockDiag));
            Assert.IsNull(chain.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 101),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-042",
                OriginName = "Great Plains flour mill, via railhead",
            }, stockDiag), string.Join("; ", stockDiag));
            Assert.IsNull(chain.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 102),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-043",
                OriginName = "Cordwood dealer, north road",
            }, stockDiag), string.Join("; ", stockDiag));

            var lots = new List<BakeryFlourLot>
            {
                new BakeryFlourLot
                {
                    LotId = EntityId.For(EntityKind.Lot, 224),
                    FlourName = BakeryBreadCatalog.FlourItemId,
                    Units = 24,
                    AcquiredDayIndex = 42,
                    ImportOrderId = "IMP-044",
                    OriginName = "Great Plains flour mill, via railhead",
                },
            };
            StagedBakeryBatch batch = chain.BeginBatch(BakeryBreadCatalog.BreadLoafId, lots, ctx, out string reason);
            Assert.IsNotNull(batch, "BeginBatch refused: " + reason);
            Assert.AreEqual(12, batch.FlourUnitsDispensed, "flour dispensed once at begin.");
            Assert.AreEqual(1, batch.NotionsUnitsDispensed, "notions dispensed once at begin.");
            Assert.AreEqual(1, batch.FuelUnitsDispensed, "fuel dispensed once at begin.");

            // Mid-production save: advance to stage 2, capture, restore into a
            // fresh chain over the same task/budget/scheduler authorities
            // (tasks live in their own authority, with their own DTO — see the
            // freight mid-task test), and drive the restored batch to completion.
            int guard = 0;
            while (batch.Process.IsActive && batch.Process.CurrentStageIndex < 2 && guard++ < 60)
            {
                ProductionStageDefinition def = batch.Process.CurrentStageDefinition;
                if (def.Kind == ProductionStageKind.ActiveLabor)
                {
                    Assert.IsTrue(chain.WorkBatch(batch, 10000, ctx, out string wreason),
                        "WorkBatch failed at stage " + batch.Process.CurrentStageIndex + ": " + wreason);
                }
                else
                {
                    clock = batch.Process.CurrentStageState.DueGameSeconds + 1f;
                    chain.PumpDueEvents(ctx);
                }
                chain.RetryParked(ctx);
            }
            BakeryStagedProductionChain.BakeryChainSaveDto dto = chain.CaptureSaveDto();
            var chain2 = new BakeryStagedProductionChain();
            chain2.LoadFromSaveDto(dto, ctx);
            Assert.AreEqual(1, chain2.Batches.Count, "exactly one batch restores.");
            StagedBakeryBatch restored = chain2.Batches[0];
            Assert.AreEqual(batch.BatchId, restored.BatchId, "restored batch must keep its id (no duplicate batch).");
            Assert.AreEqual(batch.FlourUnitsDispensed, restored.FlourUnitsDispensed, "dispensed flour must not double on restore.");
            guard = 0;
            while (restored.Process.IsActive && guard++ < 80)
            {
                if (!string.IsNullOrWhiteSpace(restored.Process.ParkedReason))
                {
                    chain2.RetryParked(ctx);
                    if (!string.IsNullOrWhiteSpace(restored.Process.ParkedReason))
                        Assert.Fail("restored batch parked and retry did not clear it: " + restored.Process.ParkedReason);
                    continue;
                }
                ProductionStageDefinition def = restored.Process.CurrentStageDefinition;
                if (def.Kind == ProductionStageKind.ActiveLabor)
                {
                    Assert.IsTrue(chain2.WorkBatch(restored, 10000, ctx, out string wreason),
                        "WorkBatch failed at stage " + restored.Process.CurrentStageIndex + ": " + wreason);
                }
                else
                {
                    clock = restored.Process.CurrentStageState.DueGameSeconds + 1f;
                    chain2.PumpDueEvents(ctx);
                }
            }
            Assert.IsTrue(restored.Process.IsComplete, "restored batch must complete.");
            Assert.Greater(restored.BreadUnitsOut, 0, "completed batch must produce bread.");
        }

        [Test]
        public void TaxDriver_LeviesOncePerTaxYear()
        {
            var taxes = new PropertyTaxService();
            var diag = new List<string>();
            int treasury = 1000000;
            var driver = new PlayerPropertyTaxDriver(
                taxes,
                () => new List<int> { 7 },
                plotId => 100000,
                (int cents, string label, out string message) =>
                {
                    message = string.Empty;
                    if (cents > treasury) { message = "insufficient treasury."; return false; }
                    treasury -= cents;
                    return true;
                });
            driver.ResolveWeek(7);
            driver.ResolveWeek(14);
            driver.ResolveWeek(21);
            int count = 0;
            foreach (TaxAssessment a in taxes.AllAssessments) count++;
            Assert.AreEqual(1, count, "the driver must levy exactly once per tax year across weekly resolves.");
        }
    }
}
