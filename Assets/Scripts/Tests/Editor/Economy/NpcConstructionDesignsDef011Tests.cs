using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D-5 (expanded campaign): second design + first business design.
    /// The production catalog now holds three designs — settler cabin,
    /// family cabin (two rooms, sleeps 6) and carpenter's workshop (shop
    /// floor plus living quarters) — all lumber-only, all priced by the
    /// production price port and supplied by the yard. These tests pin
    /// the production presets themselves (not fixture copies), prove the
    /// evaluator picks the cheapest adequate design once a second design
    /// exists, prove the workshop commissions with a business reason and
    /// builds end to end through the driver, and prove a design naming
    /// an unpriced material cannot be priced or commissioned.
    /// </summary>
    [TestFixture]
    public sealed class NpcConstructionDesignsDef011Tests
    {
        private sealed class FakeParcelAuthority : IConstructionParcelAuthority
        {
            public readonly Dictionary<string, string> HolderByParcel = new Dictionary<string, string>();
            public string AuthorizeBuild(int householdId, string parcelId, int dayIndex, List<string> diagnostics)
            {
                if (string.IsNullOrWhiteSpace(parcelId)) return "no parcel id.";
                if (!HolderByParcel.TryGetValue(parcelId, out string holder))
                    return $"parcel '{parcelId}' is not a registered parcel.";
                if (!string.Equals(holder, "household:" + householdId, StringComparison.Ordinal))
                    return $"parcel '{parcelId}' is held by '{holder}', not 'household:{householdId}'.";
                return null;
            }
        }

        private sealed class FakePricePort : IConstructionMaterialPricePort
        {
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int UnitPriceCents(ConstructionMaterialRequirement requirement)
            {
                if (requirement == null) return -1;
                return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
            }
        }

        private sealed class FakeMaterialSource : IConstructionMaterialSource
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
                if (have < units) return $"FakeMaterialSource: only {have} of '{requirement.MaterialDisplayName}'.";
                Stock[requirement.MaterialDisplayName] = have - units;
                provenanceLabel = $"lot '{requirement.MaterialDisplayName}' x{units} ({projectLabel}, day {dayIndex})";
                return null;
            }
        }

        private sealed class FakeToolCustody : IHouseholdToolCustody
        {
            private readonly HashSet<string> held = new HashSet<string>();
            public void Grant(int householdId, string toolId) => held.Add(householdId + ":" + toolId);
            public bool HouseholdHoldsTool(int householdId, string toolItemId) =>
                held.Contains(householdId + ":" + toolItemId);
        }

        private sealed class OwnLaborPort : INpcConstructionLaborPort
        {
            private readonly Fixture fixture;
            public OwnLaborPort(Fixture fixture) { this.fixture = fixture; }
            public string WorkPackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
            {
                NpcConstructionRecord record = fixture.Commissioner.FindRecordForProject(project.ProjectId);
                string commit = fixture.Commissioner.CommitOwnLabor(fixture.Executor, project, record, package,
                    101, fixture.Tracker, fixture.Custody, fixture.Tasks, fixture.Budgets,
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
                return fixture.Executor.WorkCommittedLabor(project, package, taskId,
                    package.LaborMinutesRequired, fixture.Tasks, fixture.Budgets, dayIndex, diag);
            }
        }

        private sealed class Fixture
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public FakeParcelAuthority Parcels = new FakeParcelAuthority();
            public FakePricePort Prices = new FakePricePort();
            public FakeMaterialSource Source = new FakeMaterialSource();
            public FakeToolCustody Custody = new FakeToolCustody();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public HousingAuthority Housing = new HousingAuthority();
            public TitleAuthority Titles = new TitleAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<ConstructionProject> Projects = new List<ConstructionProject>();
            public HouseholdLedger HouseholdLedger;
            public PurseCashStore YardPurse;
            public Dictionary<int, PersonState> People = new Dictionary<int, PersonState>();
            public Dictionary<int, List<int>> MembersByHousehold = new Dictionary<int, List<int>>();
            public bool LiveProject;
            public bool PendingRequest;

            public Fixture()
            {
                Prices.Prices["lumber"] = 40;
                Source.Stock["lumber"] = 10000;
                Custody.Grant(7, "saw");
                Parcels.HolderByParcel["parcel-9"] = "household:7";
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                HouseholdLedger = new HouseholdLedger(householdId);
                Assert.IsNull(HouseholdLedger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, HouseholdLedger)));
                YardPurse = new PurseCashStore("business:lumber-yard", 0, "test seed capital");
                Assert.IsNull(Cash.Register("business:lumber-yard", YardPurse));
            }

            public void RegisterPresets()
            {
                foreach (BuildingDesign design in ProductionDesignPresets.All())
                    Assert.IsNull(Catalog.RegisterDesign(design, Diag),
                        $"preset '{design.DesignId}' registers: " + string.Join("; ", Diag));
            }

            public NpcConstructionDriver NewDriver(int? maxUnitsPerTrip = null)
            {
                var driver = new NpcConstructionDriver(Commissioner, Executor, Catalog, Projects,
                    Cash, Obligations, Ids, Housing, Prices, Source, "business:lumber-yard",
                    null, new OwnLaborPort(this),
                    householdId => MembersByHousehold.TryGetValue(householdId, out List<int> members)
                        ? members : new List<int>());
                driver.MaxUnitsPerTrip = maxUnitsPerTrip;
                return driver;
            }

            public NpcConstructionRequest DesignRequest(string designId, NpcConstructionReason reason,
                List<int> memberPersonIds)
            {
                return new NpcConstructionRequest
                {
                    HouseholdId = 7,
                    DesignId = designId,
                    ParcelId = "parcel-9",
                    MemberPersonIds = new List<int>(memberPersonIds),
                    Arrangement = AccommodationArrangement.OwnerOccupied,
                    Reason = reason,
                    ReasonNotes = "test fixture: D-5 design request",
                };
            }

            public ConstructionProject DriveToComplete(NpcConstructionDriver driver,
                NpcConstructionRequest request, int startDay)
            {
                ConstructionProject project = driver.Commission(request, Parcels, startDay, Diag);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                int day = startDay;
                int guard = 0;
                while (project.Status != ConstructionProjectStatus.Complete && guard++ < 40)
                {
                    Budgets.EnsureDay(day);
                    driver.Drive(day, Diag);
                    day++;
                }
                Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status,
                    "the project completes: " + string.Join("; ", Diag));
                return project;
            }

            public void AddAdult(int personId)
            {
                People[personId] = new PersonState { id = personId, age = 30, deathDayIndex = -1 };
            }

            public void RegisterParcel(string parcelId, int householdId)
            {
                var pdiag = new List<string>();
                Assert.IsNotNull(Titles.RegisterParcel(parcelId, "lot", 1f,
                    "household:" + householdId, TitleBasis.Grant, 0, pdiag),
                    string.Join("; ", pdiag));
            }

            public void OccupyHousehold(int householdId, List<int> personIds, int sleepingCapacity)
            {
                var hdiag = new List<string>();
                Assert.IsNotNull(Housing.RegisterBuilding("bld-h" + householdId, "parcel-9",
                    "house", 0, "test", hdiag), string.Join("; ", hdiag));
                AccommodationSpace space = Housing.DefineSpace("bld-h" + householdId, sleepingCapacity, hdiag);
                Assert.IsNotNull(space, string.Join("; ", hdiag));
                foreach (int personId in personIds)
                    Assert.IsNotNull(Housing.Occupy(personId, householdId, space.SpaceId,
                        AccommodationArrangement.OwnerOccupied, 0, hdiag),
                        string.Join("; ", hdiag));
            }

            public NpcConstructionNeedEvaluator NewEvaluator()
            {
                return new NpcConstructionNeedEvaluator(
                    Commissioner, Catalog, Prices, Cash, Housing, Titles,
                    householdId => MembersByHousehold.TryGetValue(householdId, out List<int> members)
                        ? members : new List<int>(),
                    personId => People.TryGetValue(personId, out PersonState person) ? person : null,
                    householdId => LiveProject,
                    householdId => PendingRequest);
            }

            public static HouseholdState Household(int id, int homeBuildingId)
            {
                return new HouseholdState
                {
                    id = id,
                    homeBuildingId = homeBuildingId,
                    lifecycleState = HouseholdLifecycleState.Active,
                };
            }
        }

        private static int TotalLumber(BuildingDesign design)
        {
            int total = 0;
            foreach (BuildingDesignPhase phase in design.Phases ?? new List<BuildingDesignPhase>())
                foreach (ConstructionMaterialRequirement requirement in phase.Materials ?? new List<ConstructionMaterialRequirement>())
                    total += Math.Max(0, requirement != null ? requirement.RequiredUnits : 0);
            return total;
        }

        private static int TotalLabor(BuildingDesign design)
        {
            int total = 0;
            foreach (BuildingDesignPhase phase in design.Phases ?? new List<BuildingDesignPhase>())
                total += Math.Max(0, phase != null ? phase.LaborMinutes : 0);
            return total;
        }

        private static int TotalSleepingCapacity(BuildingDesign design)
        {
            int total = 0;
            foreach (BuildingDesignSpaceSpec spec in design.SpaceSpecs ?? new List<BuildingDesignSpaceSpec>())
                total += Math.Max(0, spec != null ? spec.SleepingCapacity : 0);
            return total;
        }

        private static void AssertLumberOnly(BuildingDesign design)
        {
            foreach (BuildingDesignPhase phase in design.Phases)
            {
                Assert.IsNotNull(phase, "every phase exists");
                foreach (ConstructionMaterialRequirement requirement in phase.Materials)
                {
                    Assert.IsNotNull(requirement, "every requirement exists");
                    Assert.AreEqual(ConstructionMaterialKind.Lumber, requirement.MaterialKind,
                        $"design '{design.DesignId}' phase '{phase.PhaseName}' uses lumber only, not {requirement.MaterialKind}");
                    Assert.AreEqual("lumber-yard", requirement.SupplierBusinessId,
                        "every requirement names the yard as its supplier");
                    Assert.AreEqual("lumber", requirement.MaterialDisplayName);
                }
                Assert.IsTrue(phase.RequiredToolItemIds.Contains("saw"),
                    $"design '{design.DesignId}' phase '{phase.PhaseName}' needs the saw");
            }
        }

        // ----------------------------------------------------------------
        // Preset data — the production designs themselves
        // ----------------------------------------------------------------

        [Test]
        public void Preset_SettlerCabin_MatchesDef011ProductionCabin()
        {
            BuildingDesign design = ProductionDesignPresets.SettlerCabin();

            Assert.AreEqual("settler-cabin", design.DesignId);
            Assert.AreEqual(ConstructionWorksKind.NewBuilding, design.WorksKind);
            Assert.AreEqual("cabin", design.KindLabel);
            Assert.AreEqual(3, design.Phases.Count);
            Assert.AreEqual(40, design.Phases[0].Materials[0].RequiredUnits);
            Assert.AreEqual(300, design.Phases[1].Materials[0].RequiredUnits);
            Assert.AreEqual(300, design.Phases[2].Materials[0].RequiredUnits);
            Assert.AreEqual(640, TotalLumber(design), "40 + 300 + 300 lumber, verbatim the DEF-011 design");
            Assert.AreEqual(600, TotalLabor(design), "120 + 240 + 240 labour minutes");
            Assert.AreEqual(4, TotalSleepingCapacity(design));
            Assert.AreEqual(1, design.SpaceSpecs.Count);
            Assert.IsTrue(string.IsNullOrWhiteSpace(design.RequiredPrefabId),
                "no prefab required — data-only structure");
            AssertLumberOnly(design);
        }

        [Test]
        public void Preset_FamilyCabin_SleepsSixLumberOnly()
        {
            BuildingDesign design = ProductionDesignPresets.FamilyCabin();

            Assert.AreEqual("family-cabin", design.DesignId);
            Assert.AreEqual(ConstructionWorksKind.NewBuilding, design.WorksKind);
            Assert.AreEqual("cabin", design.KindLabel);
            Assert.AreEqual(3, design.Phases.Count);
            Assert.AreEqual(60, design.Phases[0].Materials[0].RequiredUnits);
            Assert.AreEqual(450, design.Phases[1].Materials[0].RequiredUnits);
            Assert.AreEqual(450, design.Phases[2].Materials[0].RequiredUnits);
            Assert.AreEqual(960, TotalLumber(design), "60 + 450 + 450 lumber, ~1.5x the settler cabin");
            Assert.AreEqual(900, TotalLabor(design), "180 + 360 + 360 labour minutes");
            Assert.AreEqual(6, TotalSleepingCapacity(design), "main room 4 + second room 2");
            Assert.AreEqual(2, design.SpaceSpecs.Count);
            Assert.AreEqual("main room", design.SpaceSpecs[0].Label);
            Assert.AreEqual(4, design.SpaceSpecs[0].SleepingCapacity);
            Assert.AreEqual("second room", design.SpaceSpecs[1].Label);
            Assert.AreEqual(2, design.SpaceSpecs[1].SleepingCapacity);
            Assert.IsTrue(string.IsNullOrWhiteSpace(design.RequiredPrefabId),
                "no prefab required — data-only structure");
            AssertLumberOnly(design);
        }

        [Test]
        public void Preset_CarpentersWorkshop_ShopFloorPlusQuarters()
        {
            BuildingDesign design = ProductionDesignPresets.CarpentersWorkshop();

            Assert.AreEqual("carpenters-workshop", design.DesignId);
            Assert.AreEqual(ConstructionWorksKind.NewBuilding, design.WorksKind);
            Assert.AreEqual("workshop", design.KindLabel);
            Assert.AreEqual(3, design.Phases.Count);
            Assert.AreEqual(80, design.Phases[0].Materials[0].RequiredUnits);
            Assert.AreEqual(600, design.Phases[1].Materials[0].RequiredUnits);
            Assert.AreEqual(500, design.Phases[2].Materials[0].RequiredUnits);
            Assert.AreEqual(1180, TotalLumber(design), "80 + 600 + 500 lumber");
            Assert.AreEqual(1140, TotalLabor(design), "240 + 480 + 420 labour minutes");
            Assert.AreEqual(2, TotalSleepingCapacity(design), "the shop floor sleeps nobody; the quarters sleep 2");
            Assert.AreEqual(2, design.SpaceSpecs.Count);
            Assert.AreEqual("workshop floor", design.SpaceSpecs[0].Label);
            Assert.AreEqual(0, design.SpaceSpecs[0].SleepingCapacity);
            Assert.AreEqual("living quarters", design.SpaceSpecs[1].Label);
            Assert.AreEqual(2, design.SpaceSpecs[1].SleepingCapacity);
            Assert.IsTrue(string.IsNullOrWhiteSpace(design.RequiredPrefabId),
                "no prefab required — data-only structure");
            AssertLumberOnly(design);
        }

        [Test]
        public void Presets_AllThree_RegisterIntoOneCatalog()
        {
            var catalog = new BuildingDesignCatalog();
            var diag = new List<string>();

            foreach (BuildingDesign design in ProductionDesignPresets.All())
                Assert.IsNull(catalog.RegisterDesign(design, diag),
                    $"preset '{design.DesignId}' registers: " + string.Join("; ", diag));

            Assert.AreEqual(3, catalog.AllDesigns().Count, "settler cabin, family cabin, workshop");
            Assert.IsNotNull(catalog.FindDesign("settler-cabin"));
            Assert.IsNotNull(catalog.FindDesign("family-cabin"));
            Assert.IsNotNull(catalog.FindDesign("carpenters-workshop"));

            string duplicate = catalog.RegisterDesign(ProductionDesignPresets.SettlerCabin(), diag);
            Assert.IsNotNull(duplicate, "registering twice refuses the duplicate");
            Assert.IsTrue(duplicate.Contains("already registered"), "the refusal names the duplicate: " + duplicate);
            Assert.AreEqual(3, catalog.AllDesigns().Count, "the existing design stands");
        }

        [Test]
        public void Estimates_PriceAtProductionLumberRate()
        {
            var c = new Fixture();

            Assert.AreEqual(25600, NpcPropertyDecisionEngine.EstimateBuildCostCents(
                ProductionDesignPresets.SettlerCabin(), c.Prices), "640 lumber at 40c");
            Assert.AreEqual(38400, NpcPropertyDecisionEngine.EstimateBuildCostCents(
                ProductionDesignPresets.FamilyCabin(), c.Prices), "960 lumber at 40c");
            Assert.AreEqual(47200, NpcPropertyDecisionEngine.EstimateBuildCostCents(
                ProductionDesignPresets.CarpentersWorkshop(), c.Prices), "1180 lumber at 40c");
        }

        [Test]
        public void UnpricedMaterial_DesignCannotBePricedOrCommissioned()
        {
            var c = new Fixture();
            c.RegisterPresets();
            c.FundHousehold(7, 100000);
            // A design naming fieldstone, with a price port that prices
            // lumber only: the D-5 gate — a design commissions only when
            // every material is priced and supplied.
            var stoneDesign = new BuildingDesign
            {
                DesignId = "stone-cabin", DisplayName = "Stone cabin",
                WorksKind = ConstructionWorksKind.NewBuilding, KindLabel = "cabin",
            };
            var foundation = new BuildingDesignPhase
            {
                PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240,
            };
            foundation.Materials.Add(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-fieldstone", MaterialKind = ConstructionMaterialKind.Masonry,
                MaterialDisplayName = "fieldstone", RequiredUnits = 40, UnitLabel = "perch",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            });
            stoneDesign.Phases.Add(foundation);
            stoneDesign.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            Assert.IsNull(c.Catalog.RegisterDesign(stoneDesign, c.Diag), string.Join("; ", c.Diag));

            Assert.AreEqual(-1, NpcPropertyDecisionEngine.EstimateBuildCostCents(stoneDesign, c.Prices),
                "an unpriced material makes the cost unknowable, never guessed");

            var evaluation = c.Commissioner.EvaluateCommission(c.Catalog, "stone-cabin", 1,
                c.Prices, 100000, 28, NpcConstructionReason.Overcrowding, "test", 10, c.Diag);
            Assert.IsFalse(evaluation.IsViable, "the unpriced design is not viable");
            Assert.IsTrue(evaluation.RefusalReason.Contains("no price"),
                "the refusal names the unpriced material: " + evaluation.RefusalReason);
        }

        // ----------------------------------------------------------------
        // Evaluator — cheapest adequate design, once a second design exists
        // ----------------------------------------------------------------

        [Test]
        public void Evaluator_FiveMemberHousehold_SelectsFamilyCabin()
        {
            var f = new Fixture();
            f.RegisterPresets();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103); f.AddAdult(104); f.AddAdult(105);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103, 104, 105 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103, 104, 105 }, 4);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 100000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { Fixture.Household(7, 5) }, 10, 28, f.Diag);

            Assert.AreEqual(1, requests.Count, string.Join("; ", f.Diag));
            Assert.AreEqual("family-cabin", requests[0].DesignId,
                "the settler cabin sleeps 4 and the workshop sleeps 2, so the family cabin is the only adequate design for 5: " + string.Join("; ", f.Diag));
            Assert.AreEqual(NpcConstructionReason.Overcrowding, requests[0].Reason);
            Assert.IsTrue(requests[0].ReasonNotes.Contains("overcrowded by 1"),
                "the notes carry the gap: " + requests[0].ReasonNotes);
        }

        [Test]
        public void Evaluator_TwoMemberHousehold_SelectsCheapestCabinNotWorkshop()
        {
            var f = new Fixture();
            f.RegisterPresets();
            f.AddAdult(101); f.AddAdult(102);
            f.MembersByHousehold[7] = new List<int> { 101, 102 };
            f.OccupyHousehold(7, new List<int> { 101, 102 }, 1);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 100000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { Fixture.Household(7, 5) }, 10, 28, f.Diag);

            Assert.AreEqual(1, requests.Count, string.Join("; ", f.Diag));
            Assert.AreEqual("settler-cabin", requests[0].DesignId,
                "the workshop sleeps 2 and is adequate, but at 47200c it is dearer than the 25600c cabin — the evaluator never auto-commissions a business building for a housing need when a cheaper house exists: " + string.Join("; ", f.Diag));
        }

        // ----------------------------------------------------------------
        // Commission + drive end to end
        // ----------------------------------------------------------------

        [Test]
        public void Workshop_CommissionsWithNewEnterpriseReason()
        {
            var c = new Fixture();
            c.RegisterPresets();
            c.FundHousehold(7, 100000);
            c.MembersByHousehold[7] = new List<int> { 101, 102 };
            var driver = c.NewDriver(maxUnitsPerTrip: 28);

            ConstructionProject project = driver.Commission(
                c.DesignRequest("carpenters-workshop", NpcConstructionReason.NewEnterprise,
                    new List<int> { 101, 102 }), c.Parcels, 10, c.Diag);

            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record, "commissioning opens the principal's record");
            Assert.AreEqual(NpcConstructionReason.NewEnterprise, record.Reason);
            Assert.IsTrue(record.EvaluationSummary.Contains("47200c"),
                "the summary carries the estimate: " + record.EvaluationSummary);
            Assert.IsTrue(record.EvaluationSummary.Contains("sleeps 2/2"),
                "the summary carries the quarters capacity: " + record.EvaluationSummary);
            Assert.IsTrue(record.EvaluationSummary.Contains("22 trips"),
                "the 600-unit walls phase ships in 22 trips at the 28-unit bound: " + record.EvaluationSummary);
        }

        [Test]
        public void Workshop_DrivesToCompletion_RegistersWorkshopOccupiesQuarters()
        {
            var c = new Fixture();
            c.RegisterPresets();
            c.FundHousehold(7, 100000);
            c.MembersByHousehold[7] = new List<int> { 101, 102 };
            var driver = c.NewDriver(maxUnitsPerTrip: 28);

            ConstructionProject project = c.DriveToComplete(driver,
                c.DesignRequest("carpenters-workshop", NpcConstructionReason.NewEnterprise,
                    new List<int> { 101, 102 }), 10);

            Assert.IsFalse(string.IsNullOrWhiteSpace(project.BuildingId));
            Assert.AreEqual(3, project.WorkPackages.Count);
            foreach (WorkPackage package in project.WorkPackages)
                Assert.AreEqual(WorkPackageStatus.Complete, package.Status);
            // Materials 1180 lumber at 40c = 47200c, paid exactly once.
            Assert.AreEqual(100000 - 47200, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(47200, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(10000 - 1180, c.Source.Stock["lumber"]);
            // The building registers as a workshop, with a 0-sleeping shop
            // floor and 2-sleeping living quarters.
            List<Building> onParcel = c.Housing.BuildingsForParcel("parcel-9");
            Assert.AreEqual(1, onParcel.Count, "exactly one building on the parcel");
            Assert.AreEqual("workshop", onParcel[0].Kind,
                "the completed building registers as a workshop, not a house");
            Assert.AreEqual(project.BuildingId, onParcel[0].BuildingId);
            var spaces = new List<AccommodationSpace>(c.Housing.SpacesForBuilding(project.BuildingId));
            Assert.AreEqual(2, spaces.Count);
            AccommodationSpace quarters = null;
            foreach (AccommodationSpace space in spaces)
            {
                if (space.SleepingCapacity == 0) continue;
                quarters = space;
            }
            Assert.IsNotNull(quarters, "one space sleeps — the living quarters");
            Assert.AreEqual(2, quarters.SleepingCapacity);
            // Both members occupy the quarters; nobody sleeps on the shop floor.
            foreach (int personId in new[] { 101, 102 })
            {
                int occupancies = 0;
                foreach (ResidentialOccupancy occ in c.Housing.CurrentOccupanciesForPerson(personId))
                    if (string.Equals(occ.SpaceId, quarters.SpaceId, StringComparison.Ordinal)) occupancies++;
                Assert.AreEqual(1, occupancies,
                    $"P{personId} occupies the quarters exactly once");
            }

            // Extra drives change nothing: no double staging, no double occupancy, no double payment.
            for (int day = 40; day < 43; day++) driver.Drive(day, c.Diag);
            Assert.AreEqual(100000 - 47200, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(10000 - 1180, c.Source.Stock["lumber"]);
        }

        [Test]
        public void FamilyCabin_DrivesToCompletion_HousesFiveMembers()
        {
            var c = new Fixture();
            c.RegisterPresets();
            c.FundHousehold(7, 100000);
            c.MembersByHousehold[7] = new List<int> { 101, 102, 103, 104, 105 };
            var driver = c.NewDriver(maxUnitsPerTrip: 28);

            ConstructionProject project = c.DriveToComplete(driver,
                c.DesignRequest("family-cabin", NpcConstructionReason.Overcrowding,
                    new List<int> { 101, 102, 103, 104, 105 }), 10);

            Assert.IsFalse(string.IsNullOrWhiteSpace(project.BuildingId));
            Assert.AreEqual(3, project.WorkPackages.Count);
            foreach (WorkPackage package in project.WorkPackages)
                Assert.AreEqual(WorkPackageStatus.Complete, package.Status);
            // Materials 960 lumber at 40c = 38400c, paid exactly once.
            Assert.AreEqual(100000 - 38400, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(38400, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(10000 - 960, c.Source.Stock["lumber"]);
            List<Building> onParcel = c.Housing.BuildingsForParcel("parcel-9");
            Assert.AreEqual(1, onParcel.Count);
            Assert.AreEqual("cabin", onParcel[0].Kind);
            var spaces = new List<AccommodationSpace>(c.Housing.SpacesForBuilding(project.BuildingId));
            Assert.AreEqual(2, spaces.Count, "main room + second room");
            int capacity = 0;
            foreach (AccommodationSpace space in spaces) capacity += space.SleepingCapacity;
            Assert.AreEqual(6, capacity);
            var spaceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccommodationSpace space in spaces) spaceIds.Add(space.SpaceId);
            foreach (int personId in new[] { 101, 102, 103, 104, 105 })
            {
                int occupancies = 0;
                foreach (ResidentialOccupancy occ in c.Housing.CurrentOccupanciesForPerson(personId))
                    if (spaceIds.Contains(occ.SpaceId)) occupancies++;
                Assert.AreEqual(1, occupancies,
                    $"P{personId} occupies the completed family cabin exactly once");
            }
        }

        // ----------------------------------------------------------------
        // Alternatives + save / load
        // ----------------------------------------------------------------

        [Test]
        public void Evaluation_AlternativesListFamilyCabinNotWorkshop()
        {
            var c = new Fixture();
            c.RegisterPresets();

            var evaluation = c.Commissioner.EvaluateCommission(c.Catalog, "settler-cabin", 4,
                c.Prices, 100000, 28, NpcConstructionReason.Overcrowding, "test", 10, c.Diag);

            Assert.IsTrue(evaluation.IsViable, string.Join("; ", c.Diag));
            string allDecisions = string.Join(" | ", evaluation.Lines);
            Assert.IsTrue(allDecisions.Contains("family-cabin ~38400c (sleeps 6)"),
                "the dearer adequate alternative is recorded, not vetoed: " + allDecisions);
            Assert.IsFalse(allDecisions.Contains("carpenters-workshop"),
                "the workshop sleeps 2 and is not adequate for 4, so it is not an alternative: " + allDecisions);
        }

        [Test]
        public void Catalog_PresetDesignsSurviveSaveLoad()
        {
            var c = new Fixture();
            c.RegisterPresets();

            var restored = new BuildingDesignCatalog();
            restored.LoadFromSaveDto(c.Catalog.CaptureSaveDto());

            Assert.AreEqual(3, restored.AllDesigns().Count);
            BuildingDesign cabin = restored.FindDesign("settler-cabin");
            Assert.IsNotNull(cabin);
            Assert.AreEqual(3, cabin.Phases.Count);
            Assert.AreEqual(640, TotalLumber(cabin));
            Assert.AreEqual(4, TotalSleepingCapacity(cabin));
            BuildingDesign family = restored.FindDesign("family-cabin");
            Assert.IsNotNull(family);
            Assert.AreEqual(960, TotalLumber(family));
            Assert.AreEqual(6, TotalSleepingCapacity(family));
            BuildingDesign workshop = restored.FindDesign("carpenters-workshop");
            Assert.IsNotNull(workshop);
            Assert.AreEqual(1180, TotalLumber(workshop));
            Assert.AreEqual(2, TotalSleepingCapacity(workshop));
            Assert.AreEqual("workshop", workshop.KindLabel);
        }
    }
}
