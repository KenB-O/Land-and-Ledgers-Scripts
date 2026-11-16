using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// DEF-011 (Decision 7): NPC construction commissioning — a legitimate
    /// reason is required, the commission is evaluated (affordability,
    /// benefit, alternatives, shipment feasibility) and recorded on the
    /// principal's record, a real supplier and at least one household
    /// member must exist, and the need evaluator queues explicit requests
    /// only for evidenced needs (overcrowding, household formation).
    /// </summary>
    [TestFixture]
    public sealed class NpcConstructionCommissioningDef011Tests
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

        private sealed class FakeProcurementPort : INpcConstructionProcurementPort
        {
            public string Refusal = "procurement refused, nothing sold.";
            public int Calls;
            public string ProcureAndStage(ConstructionProject project, WorkPackage package,
                NpcConstructionRecord record, int dayIndex, List<string> diag)
            {
                Calls++;
                return Refusal;
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
            public HousingAuthority Housing = new HousingAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<ConstructionProject> Projects = new List<ConstructionProject>();

            public Fixture()
            {
                Prices.Prices["fieldstone"] = 50;
                Prices.Prices["lumber"] = 40;
                Source.Stock["fieldstone"] = 1000;
                Source.Stock["lumber"] = 10000;
                Parcels.HolderByParcel["parcel-9"] = "household:7";
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
            }

            public static ConstructionMaterialRequirement Material(string name, int units, string unitLabel,
                ConstructionMaterialKind kind)
            {
                return new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-" + name,
                    MaterialKind = kind,
                    MaterialDisplayName = name,
                    RequiredUnits = units,
                    UnitLabel = unitLabel,
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                };
            }

            public void RegisterCabin()
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
                foundation.Materials.Add(Material("fieldstone", 40, "perch", ConstructionMaterialKind.Masonry));
                foundation.RequiredToolItemIds.Add("shovel");
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 1, LaborMinutes = 480, Notes = "log walls and roof",
                };
                framing.Materials.Add(Material("lumber", 600, "board feet", ConstructionMaterialKind.Lumber));
                framing.RequiredToolItemIds.Add("saw");
                design.Phases.Add(foundation);
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(design, Diag), string.Join("; ", Diag));
            }

            public void RegisterSmallCabin()
            {
                var design = new BuildingDesign
                {
                    DesignId = "cabin-small",
                    DisplayName = "Small cabin",
                    WorksKind = ConstructionWorksKind.Unspecified,
                    KindLabel = "cabin",
                };
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 0, LaborMinutes = 240, Notes = "log walls and roof",
                };
                framing.Materials.Add(Material("lumber", 200, "board feet", ConstructionMaterialKind.Lumber));
                framing.RequiredToolItemIds.Add("saw");
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(design, Diag), string.Join("; ", Diag));
            }

            public void RegisterHut()
            {
                var design = new BuildingDesign
                {
                    DesignId = "hut",
                    DisplayName = "Hut",
                    WorksKind = ConstructionWorksKind.Unspecified,
                    KindLabel = "cabin",
                };
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 0, LaborMinutes = 120, Notes = "log walls",
                };
                framing.Materials.Add(Material("lumber", 100, "board feet", ConstructionMaterialKind.Lumber));
                framing.RequiredToolItemIds.Add("saw");
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 2 });
                Assert.IsNull(Catalog.RegisterDesign(design, Diag), string.Join("; ", Diag));
            }

            public NpcConstructionDriver NewDriver(
                IConstructionMaterialSource source = null,
                Func<int, IReadOnlyList<int>> members = null,
                int? maxUnitsPerTrip = null)
            {
                var driver = new NpcConstructionDriver(Commissioner, Executor, Catalog, Projects,
                    Cash, Obligations, Ids, Housing, Prices, source ?? Source, "business:lumber-yard",
                    null, null,
                    members ?? (householdId => householdId == 7 ? new List<int> { 101 } : new List<int>()));
                driver.MaxUnitsPerTrip = maxUnitsPerTrip;
                return driver;
            }

            public NpcConstructionRequest CabinRequest(NpcConstructionReason reason)
            {
                return new NpcConstructionRequest
                {
                    HouseholdId = 7,
                    DesignId = "cabin-a",
                    ParcelId = "parcel-9",
                    MemberPersonIds = new List<int> { 101 },
                    Arrangement = AccommodationArrangement.OwnerOccupied,
                    Reason = reason,
                    ReasonNotes = "test fixture: overcrowded household",
                };
            }
        }

        // ----------------------------------------------------------------
        // Commission gates
        // ----------------------------------------------------------------

        [Test]
        public void Commission_ReasonUnspecified_Refused()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Unspecified), c.Parcels, 10, c.Diag);

            Assert.IsNull(project, string.Join("; ", c.Diag));
            Assert.AreEqual(0, c.Projects.Count);
            Assert.IsTrue(string.Join("; ", c.Diag).Contains("no reason"),
                "the refusal names the missing reason: " + string.Join("; ", c.Diag));
        }

        [Test]
        public void Commission_Overcrowding_CommissionsAndRecordsEvaluation()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.RegisterSmallCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);

            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record, "commissioning opens the principal's record");
            Assert.AreEqual(NpcConstructionReason.Overcrowding, record.Reason);
            Assert.AreEqual("test fixture: overcrowded household", record.ReasonNotes);
            // 40 fieldstone at 50c + 600 lumber at 40c = 26000c; margin 74000c.
            Assert.IsTrue(record.EvaluationSummary.Contains("26000c"),
                "the summary carries the estimate: " + record.EvaluationSummary);
            Assert.IsTrue(record.EvaluationSummary.Contains("margin 74000c"),
                "the summary carries the margin: " + record.EvaluationSummary);
            Assert.IsTrue(record.Decisions.Count >= 4, "evaluation lines precede the commission line");
            Assert.IsTrue(record.Decisions[0].Contains("evaluated 'cabin-a' for Overcrowding"),
                "the first decision is the evaluation: " + record.Decisions[0]);
            string allDecisions = string.Join(" | ", record.Decisions);
            Assert.IsTrue(allDecisions.Contains("cabin-small ~8000c (cheaper, sleeps 4)"),
                "the cheaper adequate alternative is recorded, not vetoed: " + allDecisions);
            Assert.IsTrue(allDecisions.Contains("no trip bound supplied"),
                "the skipped shipment check is recorded: " + allDecisions);
        }

        [Test]
        public void Commission_CannotFund_RefusedWithNumbers()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100);
            var driver = c.NewDriver();

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);

            Assert.IsNull(project, string.Join("; ", c.Diag));
            Assert.AreEqual(0, c.Projects.Count);
            string diag = string.Join("; ", c.Diag);
            Assert.IsTrue(diag.Contains("26000c") && diag.Contains("100c"),
                "the refusal carries estimate vs funds: " + diag);
        }

        [Test]
        public void Commission_NoSupplier_Refused()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            // No procurement port, no material source, no per-household
            // source, no named supplier owner — no supplier leg at all.
            var driver = new NpcConstructionDriver(c.Commissioner, c.Executor, c.Catalog, c.Projects,
                c.Cash, c.Obligations, c.Ids, c.Housing, c.Prices, null, null,
                null, null,
                householdId => householdId == 7 ? new List<int> { 101 } : new List<int>());

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);

            Assert.IsNull(project, string.Join("; ", c.Diag));
            Assert.IsTrue(string.Join("; ", c.Diag).Contains("no material supplier"),
                "the refusal names the missing leg: " + string.Join("; ", c.Diag));
        }

        [Test]
        public void Commission_NoMembers_Refused()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver(members: householdId => new List<int>());

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);

            Assert.IsNull(project, string.Join("; ", c.Diag));
            Assert.IsTrue(string.Join("; ", c.Diag).Contains("no active members"),
                "the refusal names the missing labour: " + string.Join("; ", c.Diag));
        }

        [Test]
        public void TripBound_BindsFromProductionWagon_28Units()
        {
            var c = new Fixture();
            var driver = c.NewDriver();
            var wagon = new EquipmentAsset
            {
                AssetId = "opening-freight-wagon", Kind = "wagon",
                PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f,
            };

            driver.BindTripBoundFromWagon(wagon, LumberCargoSpec.Spec);

            Assert.AreEqual(28, driver.MaxUnitsPerTrip,
                "the production wagon carries 28 lumber units per trip (mass binds: 450 kg / 16 kg)");
        }

        [Test]
        public void TripBound_NullWagonOrSpec_LeavesBoundUnset()
        {
            var c = new Fixture();
            var driver = c.NewDriver(maxUnitsPerTrip: 28);
            var wagon = new EquipmentAsset
            {
                AssetId = "opening-freight-wagon", Kind = "wagon",
                PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f,
            };

            driver.BindTripBoundFromWagon(null, LumberCargoSpec.Spec);
            Assert.IsNull(driver.MaxUnitsPerTrip, "no wagon means no bound can be computed");

            driver.BindTripBoundFromWagon(wagon, null);
            Assert.IsNull(driver.MaxUnitsPerTrip, "no cargo spec means no bound can be computed");

            var flatbed = new EquipmentAsset
            {
                AssetId = "wagon-0", Kind = "wagon",
                PayloadMassCapacityKg = 0f, CargoVolumeCapacityM3 = 0f,
            };
            driver.BindTripBoundFromWagon(flatbed, LumberCargoSpec.Spec);
            Assert.AreEqual(0, driver.MaxUnitsPerTrip,
                "a wagon that cannot carry the cargo yields a zero bound, not a skipped check");
        }

        [Test]
        public void Commission_Bound28_MultiTripPhaseCommissionsWithTripsRecorded()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver(maxUnitsPerTrip: 28);

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);

            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record);
            Assert.IsTrue(record.EvaluationSummary.Contains("28-unit"),
                "the summary names the bound: " + record.EvaluationSummary);
            Assert.IsFalse(record.EvaluationSummary.Contains("no trip bound supplied"),
                "the check evaluated, it was not skipped: " + record.EvaluationSummary);
            string allDecisions = string.Join(" | ", record.Decisions);
            Assert.IsTrue(allDecisions.Contains("'Foundation' 40 units in 2 trips"),
                "the foundation ships in 2 trips at the 28-unit bound: " + allDecisions);
            Assert.IsTrue(allDecisions.Contains("'Framing' 600 units in 22 trips"),
                "the framing ships in 22 trips at the 28-unit bound: " + allDecisions);
        }

        [Test]
        public void Commission_BoundZero_RefusesUnshippablePhase()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver(maxUnitsPerTrip: 0);

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);

            Assert.IsNull(project, string.Join("; ", c.Diag));
            string diag = string.Join("; ", c.Diag);
            Assert.IsTrue(diag.Contains("Foundation") && diag.Contains("40") && diag.Contains("cannot carry"),
                "the refusal names the phase, its units and the unshippable wagon: " + diag);
        }

        [Test]
        public void BlockedProcurement_ZeroMaterialRefusal_NamesRecordedReasonNotPaidForLot()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var procurement = new FakeProcurementPort
            {
                Refusal = "NpcConstructionProcurementAdapter: the freight plan factory returned no plan for package 'pkg-1' — procurement refused, nothing sold.",
            };
            var driver = new NpcConstructionDriver(c.Commissioner, c.Executor, c.Catalog, c.Projects,
                c.Cash, c.Obligations, c.Ids, c.Housing, c.Prices, c.Source, "business:lumber-yard",
                procurement, null,
                householdId => householdId == 7 ? new List<int> { 101 } : new List<int>());

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            WorkPackage package = project.WorkPackages[0];

            var day10 = new List<string>();
            Assert.AreEqual(0, driver.Drive(10, day10),
                "the refused package does not advance: " + string.Join("; ", day10));
            Assert.AreEqual(1, procurement.Calls, "the procurement leg ran once");
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);
            Assert.IsNotNull(funding, "the package has a funding line");
            Assert.IsTrue(funding.Stranded, "the refused package is stranded");
            Assert.IsTrue(funding.StrandedReason.Contains("nothing sold"),
                "the funding line records the zero-material refusal: " + funding.StrandedReason);

            var day11 = new List<string>();
            Assert.AreEqual(0, driver.Drive(11, day11),
                "the blocked package still does not advance: " + string.Join("; ", day11));
            Assert.AreEqual(1, procurement.Calls, "a blocked package is never re-procured");
            string blocked = string.Join("; ", day11);
            Assert.IsTrue(blocked.Contains("nothing sold"),
                "the blocked message names the recorded refusal: " + blocked);
            Assert.IsFalse(blocked.Contains("paid-for"),
                "the blocked message must not claim a paid-for lot when nothing was sold: " + blocked);
            Assert.IsTrue(blocked.Contains("retry is blocked until AllowProcurementRetry is called"),
                "the retry block is still stated: " + blocked);
        }

        [Test]
        public void Commission_NoBound_SkipsShipmentCheck()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver(maxUnitsPerTrip: null);

            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);

            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record);
            Assert.IsTrue(record.EvaluationSummary.Contains("no trip bound supplied"),
                "the skipped check is recorded: " + record.EvaluationSummary);
        }

        [Test]
        public void Commission_TooSmallDesign_Refused()
        {
            var c = new Fixture();
            c.RegisterHut();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver(
                members: householdId => householdId == 7
                    ? new List<int> { 101, 102, 103 } : new List<int>());
            var request = c.CabinRequest(NpcConstructionReason.Overcrowding);
            request.DesignId = "hut";
            request.MemberPersonIds = new List<int> { 101, 102, 103 };

            ConstructionProject project = driver.Commission(request, c.Parcels, 10, c.Diag);

            Assert.IsNull(project, string.Join("; ", c.Diag));
            Assert.IsTrue(string.Join("; ", c.Diag).Contains("sleeps 2"),
                "the refusal names the capacity shortfall: " + string.Join("; ", c.Diag));
        }

        // ----------------------------------------------------------------
        // Save / load
        // ----------------------------------------------------------------

        [Test]
        public void Record_ReasonAndEvaluation_SurviveSaveLoad()
        {
            var c = new Fixture();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(
                c.CabinRequest(NpcConstructionReason.Overcrowding), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));

            var restored = new NpcConstructionCommissioner();
            var loadDiag = new List<string>();
            restored.LoadFromSaveDto(c.Commissioner.CaptureSaveDto(), loadDiag);
            NpcConstructionRecord record = restored.FindRecordForProject(project.ProjectId);

            Assert.IsNotNull(record, "the record survives the round trip: " + string.Join("; ", loadDiag));
            Assert.AreEqual(NpcConstructionReason.Overcrowding, record.Reason);
            Assert.AreEqual("test fixture: overcrowded household", record.ReasonNotes);
            Assert.IsTrue(record.EvaluationSummary.Contains("26000c"),
                "the evaluation summary survives: " + record.EvaluationSummary);
            Assert.IsTrue(record.Decisions.Count >= 4, "the decision lines survive");
            Assert.IsTrue(record.Decisions[0].Contains("evaluated 'cabin-a' for Overcrowding"),
                "the evaluation lines survive in order: " + record.Decisions[0]);
        }

        // ----------------------------------------------------------------
        // Need evaluator
        // ----------------------------------------------------------------

        private sealed class EvaluatorFixture
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public FakePricePort Prices = new FakePricePort();
            public CreditCashBridge Cash = new CreditCashBridge();
            public HousingAuthority Housing = new HousingAuthority();
            public TitleAuthority Titles = new TitleAuthority();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public Dictionary<int, PersonState> People = new Dictionary<int, PersonState>();
            public Dictionary<int, List<int>> MembersByHousehold = new Dictionary<int, List<int>>();
            public bool LiveProject;
            public bool PendingRequest;

            public EvaluatorFixture()
            {
                Prices.Prices["fieldstone"] = 50;
                Prices.Prices["lumber"] = 40;
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
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

            public void RegisterCabinDesigns()
            {
                var diag = new List<string>();
                var cabin = new BuildingDesign
                {
                    DesignId = "cabin-a", DisplayName = "Settler cabin",
                    WorksKind = ConstructionWorksKind.Unspecified, KindLabel = "cabin",
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
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 1, LaborMinutes = 480,
                };
                framing.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Lumber,
                    MaterialDisplayName = "lumber", RequiredUnits = 600, UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                cabin.Phases.Add(foundation);
                cabin.Phases.Add(framing);
                cabin.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(cabin, diag), string.Join("; ", diag));

                var small = new BuildingDesign
                {
                    DesignId = "cabin-small", DisplayName = "Small cabin",
                    WorksKind = ConstructionWorksKind.Unspecified, KindLabel = "cabin",
                };
                var smallFraming = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 0, LaborMinutes = 240,
                };
                smallFraming.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Lumber,
                    MaterialDisplayName = "lumber", RequiredUnits = 200, UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                small.Phases.Add(smallFraming);
                small.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(small, diag), string.Join("; ", diag));
            }

            public void RegisterCabinOnly()
            {
                var diag = new List<string>();
                var cabin = new BuildingDesign
                {
                    DesignId = "cabin-a", DisplayName = "Settler cabin",
                    WorksKind = ConstructionWorksKind.Unspecified, KindLabel = "cabin",
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
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 1, LaborMinutes = 480,
                };
                framing.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Lumber,
                    MaterialDisplayName = "lumber", RequiredUnits = 600, UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                cabin.Phases.Add(foundation);
                cabin.Phases.Add(framing);
                cabin.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(cabin, diag), string.Join("; ", diag));
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

        [Test]
        public void Evaluator_Overcrowding_QueuesCheapestFittingDesign()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(1, requests.Count, string.Join("; ", f.Diag));
            Assert.AreEqual(7, requests[0].HouseholdId);
            Assert.AreEqual("cabin-small", requests[0].DesignId,
                "cabin-small is the cheapest adequate design and ships in 1 trip at the 400-unit bound, so it wins on price; cabin-a would also ship, in 2 trips, but costs more");
            Assert.AreEqual("parcel-9", requests[0].ParcelId);
            Assert.AreEqual(NpcConstructionReason.Overcrowding, requests[0].Reason);
            Assert.IsTrue(requests[0].ReasonNotes.Contains("overcrowded by 1"),
                "the notes carry the gap: " + requests[0].ReasonNotes);
        }

        [Test]
        public void Evaluator_NoOccupancyData_NeverQueuesAsOvercrowded()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count,
                "no occupancy data means unevaluated, never overcrowded: " + string.Join("; ", f.Diag));
        }

        [Test]
        public void Evaluator_Formation_OnlySmallestSharingHouseholdQueues()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102 };
            f.MembersByHousehold[8] = new List<int> { 103 };
            f.RegisterParcel("parcel-7", 7);
            f.RegisterParcel("parcel-8", 8);
            f.FundHousehold(7, 30000);
            f.FundHousehold(8, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState>
                {
                    EvaluatorFixture.Household(7, 5),
                    EvaluatorFixture.Household(8, 5),
                }, 10, 400, f.Diag);

            Assert.AreEqual(1, requests.Count, string.Join("; ", f.Diag));
            Assert.AreEqual(8, requests[0].HouseholdId,
                "the smaller offshoot household queues; the established household keeps the home");
            Assert.AreEqual(NpcConstructionReason.HouseholdFormation, requests[0].Reason);
            Assert.AreEqual("parcel-8", requests[0].ParcelId);
            Assert.AreEqual("cabin-small", requests[0].DesignId);
        }

        [Test]
        public void Evaluator_LiveProject_BlocksQueue()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            f.LiveProject = true;
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, string.Join("; ", f.Diag));
        }

        [Test]
        public void Evaluator_PendingRequest_BlocksQueue()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            f.PendingRequest = true;
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, string.Join("; ", f.Diag));
        }

        [Test]
        public void Evaluator_NoParcel_NoQueue()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, string.Join("; ", f.Diag));
        }

        [Test]
        public void Evaluator_CannotFund_NoQueue()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 100);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, string.Join("; ", f.Diag));
        }

        [Test]
        public void Evaluator_NoDesign_NoQueue()
        {
            var f = new EvaluatorFixture();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, string.Join("; ", f.Diag));
        }

        [Test]
        public void Evaluator_NullBound_AllowsOversizedDesign()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinOnly();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, null, f.Diag);

            Assert.AreEqual(1, requests.Count, string.Join("; ", f.Diag));
            Assert.AreEqual("cabin-a", requests[0].DesignId);
        }

        [Test]
        public void Evaluator_Bound28_MultiTripDesignQueues()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinOnly();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 28, f.Diag);

            Assert.AreEqual(1, requests.Count, string.Join("; ", f.Diag));
            Assert.AreEqual("cabin-a", requests[0].DesignId,
                "the 600-unit framing ships in 22 trips at the 28-unit bound, so the design queues");
        }

        [Test]
        public void Evaluator_BoundZero_BlocksUnshippableDesign()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinOnly();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 0, f.Diag);

            Assert.AreEqual(0, requests.Count,
                "the only adequate design cannot ship at all when the wagon carries nothing: "
                + string.Join("; ", f.Diag));
            Assert.IsTrue(string.Join("; ", f.Diag).Contains("shipment-blocked 1"),
                "the summary counts the shipment block: " + string.Join("; ", f.Diag));
        }
    }
}
