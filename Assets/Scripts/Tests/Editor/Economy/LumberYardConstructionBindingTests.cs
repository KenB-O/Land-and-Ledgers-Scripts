using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Financing;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Slice C3: driver composition — the bound C1 price port prices a
    /// lumber-only design at the real yard quote, and the unbound driver
    /// refuses it as unpriced. Copied from the Pass11Ws2 Chain pattern,
    /// minus the procurement parts: FakeParcelAuthority, FundHousehold,
    /// a lumber-only design, and a driver built like Chain.NewDriver
    /// with the price port as the variable under test and null
    /// materialSource / procurement / labor. Commission touches none
    /// of those nulls.
    /// </summary>
    [TestFixture]
    public sealed class LumberYardConstructionBindingTests
    {
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
        }

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

        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units, string millId, string standId)
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                SourceLogLotId = "log-" + lotNumber,
                StandId = standId,
                Species = "white pine",
                MillBusinessId = millId,
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        private LumberYardShopRuntime StockedYard()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9401, 40, "mill-1", "stand-1"), "merchantable", 220, 85, diag),
                "test setup must stock merchantable lumber: " + string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9402, 30, "mill-2", "stand-2"), "clear", 230, 95, diag),
                "test setup must stock clear lumber: " + string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(LumberYardHardwareKind.Nails, 20, "smith-1", 218, "iron-order-7", 220, 30, diag),
                "test setup must stock nails: " + string.Join("; ", diag));
            return yard;
        }

        private sealed class Fixture
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public List<ConstructionProject> Projects = new List<ConstructionProject>();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public HousingAuthority Housing = new HousingAuthority();
            public FakeParcelAuthority Parcels = new FakeParcelAuthority();
            public HouseholdLedger HouseholdLedger;

            public Fixture()
            {
                Parcels.HolderByParcel["parcel-9"] = "household:7";
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                HouseholdLedger = new HouseholdLedger(householdId);
                Assert.IsNull(HouseholdLedger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, HouseholdLedger)));
            }

            public void RegisterLumberFrame()
            {
                var design = new BuildingDesign
                {
                    DesignId = "lumber-frame",
                    DisplayName = "Lumber frame",
                    WorksKind = ConstructionWorksKind.Unspecified,
                    KindLabel = "frame",
                };
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 0, LaborMinutes = 60, Notes = "lumber frame",
                };
                framing.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber",
                    MaterialKind = ConstructionMaterialKind.Lumber,
                    MaterialDisplayName = "lumber",
                    RequiredUnits = 10,
                    UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 1 });
                Assert.IsNull(Catalog.RegisterDesign(design, Diag), string.Join("; ", Diag));
            }

            public NpcConstructionDriver NewDriver(IConstructionMaterialPricePort pricePort)
            {
                return new NpcConstructionDriver(Commissioner, Executor, Catalog, Projects,
                    Cash, Obligations, Ids, Housing, pricePort, null, "business:yard-1",
                    null, null,
                    householdId => householdId == 7 ? new List<int> { 101 } : new List<int>());
            }

            public NpcConstructionRequest LumberFrameRequest()
            {
                return new NpcConstructionRequest
                {
                    HouseholdId = 7,
                    DesignId = "lumber-frame",
                    ParcelId = "parcel-9",
                    MemberPersonIds = new List<int> { 101 },
                    Arrangement = AccommodationArrangement.OwnerOccupied,
                    Reason = NpcConstructionReason.Overcrowding,
                    ReasonNotes = "test fixture: priced commission",
                };
            }
        }

        [Test]
        public void Driver_Commission_LumberOnlyDesign_PricedByRealPort()
        {
            var yard = StockedYard();
            var port = new LumberYardConstructionPricePort(yard);
            var fixture = new Fixture();
            fixture.FundHousehold(7, 100000);
            fixture.RegisterLumberFrame();
            var driver = fixture.NewDriver(port);

            ConstructionProject project = driver.Commission(fixture.LumberFrameRequest(), fixture.Parcels, 220, diag);

            Assert.IsNotNull(project, "the bound driver commissions the lumber-only design: " + string.Join("; ", diag));
            NpcConstructionRecord record = fixture.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record, "commissioning opens the principal's record");
            Assert.AreEqual(1120, record.TotalEstimatedCostCents,
                "10 lumber units at the real port price of 112c");
            Assert.IsTrue(string.Join("; ", diag).Contains("selected"),
                "the design is selected at the real price: " + string.Join("; ", diag));
        }

        [Test]
        public void Driver_Commission_NullPort_RefusesUnpriced()
        {
            var fixture = new Fixture();
            fixture.FundHousehold(7, 100000);
            fixture.RegisterLumberFrame();
            var driver = fixture.NewDriver(null);

            ConstructionProject project = driver.Commission(fixture.LumberFrameRequest(), fixture.Parcels, 220, diag);

            Assert.IsNull(project, "the unbound driver refuses the design as unpriced: " + string.Join("; ", diag));
            Assert.IsTrue(string.Join("; ", diag).Contains("unpriced"),
                "the refusal names the unpriced materials: " + string.Join("; ", diag));
        }
    }
}
