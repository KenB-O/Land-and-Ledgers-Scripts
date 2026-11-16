using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

// C-9 (expanded campaign 2026-11-16): the end-to-end chain proof.
// PROOF slice, not a feature slice: fell -> haul -> saw -> transfer ->
// yard sale to a construction package -> freight to site, through the
// REAL production path at every handoff (the mill saw-line workstation
// established through the hub gate, the real freight chain, the real
// yard retail/delivery path, the real construction procurement path).
// No test-only shortcuts, no synthetic inventory, no free stock.
//
// The endpoint is construction procurement, NOT furniture manufacture:
// the furniture-manufacture leg is F1 part 2's (F branch, Don Millward's
// workshop). Toll sawing stays unwired (C-8 pins zero production
// callers); the FailTripDisposition never-departed pin (E cp98) does not
// fit this proof — the chain's freight leg succeeds by design, and the
// construction coordinator's never-departed refund is already pinned by
// FreightPricingDef016Tests (Coordinator_WagonBusyAtCreation and
// Coordinator_DispatchRefused) — so no fault-injection test is added
// here; that pin stays a follow-up for a later C slice.
//
// Chain quantities (all from the real calibrations, never invented):
// 5 white-pine logs (300 kg each, 1500 kg — TimberHarvest's felled-log
// mass, confirmed on disk) fell on day 210; the 1200 kg log wagon splits
// them 4 + 1 across two hauls on day 211; sawn on day 212 into 20 lumber
// + 10 fuel wood (150 saw minutes); transferred at the 32c wholesale
// cost; the yard retails at 40c (32c x 1.25 markup); the cabin package
// needs 16 lumber (640c) and freights 6 miles at the DEF-016 card (25c
// base + 1c/mile round trip = 37c, one trip, 28/trip). The 10% overrun
// tolerance on the 640c estimate (64c) covers the 37c freight. Four
// lumber stay at the yard: the proof's remainder.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class LumberChainEndToEndC9Tests
    {
        private const int FellDay = 210;
        private const int HaulDay = 211;
        private const int SawDay = 212;
        private const int CommissionDay = 213;
        private const int ProcureDay = 214;
        private const int YardMiles = 6;
        private const int YardOpeningCashCents = 10000;
        private const int MillOpeningCashCents = 500;
        private const int HouseholdOpeningCashCents = 100000;
        private static readonly EntityId Sawyer = EntityId.For(EntityKind.Person, 5002);
        private static readonly EntityId Feller = EntityId.For(EntityKind.Person, 3);

        /// <summary>
        /// One cash balance serving both the transfer driver's business
        /// cash port and the construction bridge's real cash store: the
        /// yard's money is one balance, not two. Mirrors the production
        /// Series2BusinessRuntimeCashPort contract on the transfer side
        /// and the PurseCashStore never-negative rule on the bridge side.
        /// </summary>
        private sealed class DualBusinessCashPort : IBusinessIdentityCashPort, IRealCashStore
        {
            private int balanceCents;

            public DualBusinessCashPort(string businessId, int openingBalanceCents)
            {
                BusinessId = businessId;
                balanceCents = openingBalanceCents;
            }

            public string BusinessId { get; }
            public string OwnerName => "business:" + BusinessId;
            public int BalanceCents => balanceCents;
            public int ReadBalanceCents() => balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents < 0) return "DualBusinessCashPort: negative debit.";
                if (balanceCents < amountCents)
                    return $"DualBusinessCashPort: insufficient funds ({balanceCents}c for {amountCents}c).";
                balanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (amountCents < 0) return "DualBusinessCashPort: negative credit.";
                balanceCents += amountCents;
                return null;
            }

            public string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty)
            {
                return TryDebit(amountCents, purpose);
            }

            public string CreditCents(int dayIndex, int amountCents, string sourceClass,
                string sourceReference, string reason, string counterparty)
            {
                return Credit(amountCents, reason);
            }
        }

        private sealed class World
        {
            public List<string> Diagnostics = new List<string>();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LogLotCustodyLedger Ledger;
            public LoggingStandRegistry Stands = new LoggingStandRegistry();
            public LoggingOperationRuntime Logging;
            public SawmillShopRuntime Mill;
            public LumberYardShopRuntime Yard;
            public SawmillProductionDriver SawDriver;
            public TaskAuthority SawTasks;
            public WorkTimeBudgetStore SawBudgets;
            public SawmillLabourCounters Counters;
            public SawmillToYardTransferDriver TransferDriver;
            public DualBusinessCashPort YardCash;
            public DualBusinessCashPort MillCash;
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public HouseholdLedger HouseholdLedger;
            public PurseCashStore CarrierPurse;
            public LumberYardConstructionPricePort PricePort;
            public LumberYardConstructionSaleAdapter SalePort;
            public LumberYardConstructionSupplier Supplier;
            public SupplierDirectory Directory = new SupplierDirectory();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public ConstructionProcurementService Procurement = new ConstructionProcurementService();
            public ConstructionMaterialFreightCoordinator FreightCoordinator = new ConstructionMaterialFreightCoordinator();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public FreightShipmentService FreightService = new FreightShipmentService();

            public ConstructionFreightPlan FreightPlan(ConstructionProject project, FreightServiceQuote quote)
            {
                return new ConstructionFreightPlan
                {
                    Freight = FreightService,
                    Wagon = new EquipmentAsset
                    {
                        AssetId = "opening-freight-wagon", Kind = "wagon", DisplayName = "farm wagon",
                        PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f, Condition01 = 1f,
                    },
                    DriverId = EntityId.For(EntityKind.Person, 41),
                    LoaderId = EntityId.For(EntityKind.Person, 42),
                    UnloaderId = EntityId.For(EntityKind.Person, 43),
                    Tasks = new TaskAuthority(),
                    Budgets = new WorkTimeBudgetStore(),
                    Tracker = new PersonScheduleTracker(),
                    SiteLabel = "site:" + project.ProjectId,
                    FreightChargeCents = quote.TotalPriceCents,
                    TripCount = quote.TripCount,
                    FreightChargeCentsPerTrip = quote.PerTripTotalCents,
                    CarrierPayeeLabel = "business:carrier",
                    Cash = Cash,
                    BuyerOwnerName = "household:7",
                    CargoSpec = LumberCargoSpec.Spec,
                };
            }
        }

        private static EquipmentTaskGate BuildGate()
        {
            var registry = new BusinessWorkstations("mill-1");
            var station = registry.GetOrCreate("sawmill-saw-line");
            station.SpaceId = "mill-saw-floor";
            station.InstallComponent("saw-line-1");
            station.InstallComponent("log-carriage-1");
            station.InstallComponent("drive-system-1");
            station.MotivePowerSources.Add(new MotivePowerSource(MotivePowerKind.Steam, "recorded steam engine"));
            return new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => registry,
                FindWorkstationDefinition = id => id == "sawmill-saw-line" ? WorkstationCatalog.SawmillSawLine : null,
                FindComponent = id => id == "saw-line-1"
                    ? new WorkstationComponentView { AssetId = id, Kind = "saw-line", Condition01 = 1f, IsUsable = true }
                    : id == "log-carriage-1"
                        ? new WorkstationComponentView { AssetId = id, Kind = "log-carriage", Condition01 = 1f, IsUsable = true }
                        : id == "drive-system-1"
                            ? new WorkstationComponentView { AssetId = id, Kind = "drive-system", Condition01 = 1f, IsUsable = true }
                            : (WorkstationComponentView?)null,
                SupportSupplier = () =>
                {
                    var support = new SupportContext();
                    support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = 1;
                    support.AvailableRepairCapabilities.Add("saw-filing");
                    return support;
                },
            };
        }

        private static EquipmentTaskGate BlockedGate()
        {
            // P8 pattern: a registry with NO sawmill-saw-line station.
            var emptyRegistry = new BusinessWorkstations("mill-1");
            return new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => emptyRegistry,
                FindWorkstationDefinition = id => id == "sawmill-saw-line" ? WorkstationCatalog.SawmillSawLine : null,
                FindComponent = id => (WorkstationComponentView?)null,
                SupportSupplier = () =>
                {
                    var support = new SupportContext();
                    support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = 1;
                    support.AvailableRepairCapabilities.Add("saw-filing");
                    return support;
                },
            };
        }

        private static EquipmentTaskGate FellingEquipmentGate()
        {
            return new EquipmentTaskGate
            {
                AssetsSupplier = () => new List<EquipmentAsset>
                {
                    new EquipmentAsset
                    {
                        AssetId = "test-felling-axe",
                        Kind = "felling-axe",
                        OwnerKind = "business",
                        OwnerId = "logging-1",
                        Condition01 = 1f,
                    },
                },
            };
        }

        private static JourneyModel LandingToMillJourney()
        {
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(
                "landing-1", JourneyLocationKind.Waypoint, "North Landing", 2f, 0f)));
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(
                "mill-1-yard", JourneyLocationKind.Mill, "Local Sawmill", 0f, 0f)
                { BusinessId = "mill-1" }));
            Assert.IsNull(model.AddEdge("landing-1", "mill-1-yard", 2f, "river road"));
            return model;
        }

        private static World NewWorld(bool withSawLine)
        {
            var world = new World();
            world.Ledger = new LogLotCustodyLedger(world.Diagnostics);
            world.Logging = new LoggingOperationRuntime("logging-1", world.Ids, world.Ledger);
            Assert.IsNull(world.Stands.RegisterStand(new TimberStand
            {
                StandId = "stand-1",
                DisplayName = "Test Pines",
                LocationId = "landing-1",
                TimberRightsHolderId = "",
                Species = "white pine",
                StandingTimberUnits = 10,
            }));
            Assert.IsNull(world.Stands.GrantTimberRights("stand-1", "logging-1", 200, "town-land-office"));
            world.Mill = new SawmillShopRuntime("mill-1", world.Ids);
            world.Yard = new LumberYardShopRuntime("lumber-yard", world.Ids);
            world.SawTasks = new TaskAuthority();
            world.SawBudgets = new WorkTimeBudgetStore();
            world.SawBudgets.EnsureDay(SawDay);
            world.Counters = new SawmillLabourCounters();
            EquipmentTaskGate gate = withSawLine ? BuildGate() : BlockedGate();
            world.SawDriver = new SawmillProductionDriver(world.Mill, world.SawTasks, world.SawBudgets,
                gate, world.Counters, world.Diagnostics, world.Ledger);
            world.TransferDriver = new SawmillToYardTransferDriver(world.Mill, world.Yard, world.Diagnostics);
            world.YardCash = new DualBusinessCashPort("lumber-yard", YardOpeningCashCents);
            world.MillCash = new DualBusinessCashPort("mill-1", MillOpeningCashCents);
            world.TransferDriver.YardCash = world.YardCash;
            world.TransferDriver.MillCash = world.MillCash;
            world.HouseholdLedger = new HouseholdLedger(7);
            Assert.IsNull(world.HouseholdLedger.RecordInflow(0, HouseholdOpeningCashCents,
                HouseholdIncomeSource.OwnerContribution, "test-seed", "test seed capital", "test"));
            Assert.IsNull(world.Cash.Register("business:lumber-yard", world.YardCash));
            Assert.IsNull(world.Cash.Register("household:7", new HouseholdCashStore(7, world.HouseholdLedger)));
            world.CarrierPurse = new PurseCashStore("business:carrier", 0, "test seed capital");
            Assert.IsNull(world.Cash.Register("business:carrier", world.CarrierPurse));
            world.PricePort = new LumberYardConstructionPricePort(world.Yard);
            world.SalePort = new LumberYardConstructionSaleAdapter(world.Yard);
            world.Supplier = new LumberYardConstructionSupplier(world.Yard, world.PricePort);
            world.Directory.Register(world.Supplier);
            return world;
        }

        private static ConstructionMaterialRequirement LumberRequirement(int units)
        {
            // Production shape (FirstLedgerSliceBootstrapper cabin design):
            // lowercase "lumber", board feet, AnyRealSource, yard named.
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-framing-lumber",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = units,
                UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            };
        }

        private static ConstructionProject CommissionCabin(World world, out NpcConstructionRecord record)
        {
            var design = new BuildingDesign
            {
                DesignId = "cabin-c9",
                DisplayName = "Settler cabin",
                WorksKind = ConstructionWorksKind.Unspecified,
                KindLabel = "cabin",
            };
            var framing = new BuildingDesignPhase
            {
                PhaseName = "Framing", Sequence = 0, LaborMinutes = 480, Notes = "log walls and roof",
            };
            framing.Materials.Add(LumberRequirement(16));
            framing.RequiredToolItemIds.Add("saw");
            design.Phases.Add(framing);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            if (world.Catalog.FindDesign("cabin-c9") == null)
            {
                Assert.IsNull(world.Catalog.RegisterDesign(design, world.Diagnostics),
                    string.Join("; ", world.Diagnostics));
            }
            ConstructionProject project = world.Commissioner.CommissionProject(world.Executor, world.Catalog,
                7, "cabin-c9", "parcel-9", world.PricePort, CommissionDay, world.Diagnostics, out record);
            Assert.IsNotNull(project, string.Join("; ", world.Diagnostics));
            Assert.IsNotNull(record);
            return project;
        }

        private static FreightServiceQuote QuoteFreight(int units)
        {
            // DEF-016 card, quoted by the real calculator: 25c base + 1c/mile
            // round trip over the production opening wagon (450 kg / 4 m3).
            Assert.IsTrue(FreightServiceQuoteCalculator.TryCalculateCargo(YardMiles, 25, 1, true,
                units, LumberCargoSpec.Spec, 450f, 4f, 0f, out FreightServiceQuote quote, out string reason),
                reason);
            return quote;
        }

        // ---- Chain drivers (actions with success asserts; the tests assert state) ----

        private static LogLot FellFive(World world)
        {
            LogLot lot = world.Logging.RunFelling(world.Stands, "stand-1", "logging-1", 5,
                Feller, FellDay, world.Diagnostics, FellingEquipmentGate());
            Assert.IsNotNull(lot, string.Join("; ", world.Diagnostics));
            return lot;
        }

        private static void HaulBothToMill(World world, LogLot lot)
        {
            LogHaul haul1 = world.Logging.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", HaulDay, world.Diagnostics);
            Assert.IsNotNull(haul1, string.Join("; ", world.Diagnostics));
            Assert.AreEqual(4, haul1.Lot.LogUnits, "the 1200 kg wagon takes 4 of the 5 logs first");
            Assert.AreEqual(1200f, haul1.Lot.MassKg, 0.01f);
            LogHaul haul2 = world.Logging.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", HaulDay, world.Diagnostics);
            Assert.IsNotNull(haul2, string.Join("; ", world.Diagnostics));
            Assert.AreEqual(1, haul2.Lot.LogUnits, "the last log follows on the second trip");
            Assert.AreEqual(300f, haul2.Lot.MassKg, 0.01f);
            world.Logging.AdvanceHauls(1000, world.Diagnostics, HaulDay);
            Assert.AreEqual(LogHaulStatus.Arrived, haul1.Status);
            Assert.AreEqual(LogHaulStatus.Arrived, haul2.Status);
            world.Logging.DeliverArrivedHauls(world.Mill.LogStock, world.Diagnostics, HaulDay);
            Assert.AreEqual(LogHaulStatus.Delivered, haul1.Status);
            Assert.AreEqual(LogHaulStatus.Delivered, haul2.Status);
        }

        private static void SawBoth(World world)
        {
            Assert.AreEqual(2, world.Mill.LogStock.Lots.Count);
            var logLotIds = new List<string>();
            var logUnits = new List<int>();
            foreach (LogLot logLot in world.Mill.LogStock.Lots)
            {
                logLotIds.Add(logLot.LotId.ToString());
                logUnits.Add(logLot.LogUnits);
            }
            for (int i = 0; i < logLotIds.Count; i++)
            {
                SawmillProductionJob job = world.SawDriver.BeginProduction(
                    logLotIds[i], Sawyer, logUnits[i], SawDay, out string beginRefusal);
                Assert.IsNotNull(job, beginRefusal);
                Assert.IsNull(beginRefusal);
                Assert.IsTrue(world.SawDriver.RunProduction(job.JobId, SawDay, out string runRefusal), runRefusal);
                Assert.IsNull(runRefusal);
                Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            }
        }

        private static void TransferBothToYard(World world)
        {
            Assert.AreEqual(2, world.Mill.LumberStock.Lots.Count);
            var lumberLotIds = new List<string>();
            foreach (SawmillLumberLot lumberLot in world.Mill.LumberStock.Lots)
                lumberLotIds.Add(lumberLot.LotId.ToString());
            foreach (string lumberLotId in lumberLotIds)
            {
                SawmillToYardTransferJob job = world.TransferDriver.BeginTransfer(
                    lumberLotId, "merchantable",
                    SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, SawDay, out string beginRefusal);
                Assert.IsNotNull(job, beginRefusal);
                Assert.IsNull(beginRefusal);
                Assert.IsTrue(world.TransferDriver.RunTransfer(job.JobId, SawDay, out string runRefusal), runRefusal);
                Assert.IsNull(runRefusal);
            }
        }

        // ---- T1: the full chain ----

        [Test]
        public void C9_FullChain_FellHaulSawTransferSellFreightStage_ConservesUnitsAndCash()
        {
            World world = NewWorld(true);

            // Day 210 — fell: 5 logs leave the stand, 5 standing timber remain.
            LogLot lot = FellFive(world);
            Assert.AreEqual(5, lot.LogUnits);
            Assert.AreEqual(1500f, lot.MassKg, 0.01f);
            Assert.AreEqual(5, world.Stands.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(5, world.Logging.LandingLogUnits);
            LogLotCustodyRecord felled = world.Ledger.GetLot(lot.LotId.ToString());
            Assert.IsNotNull(felled);
            Assert.AreEqual(LogLotCustodyState.Created, felled.State);
            Assert.AreEqual("landing-1", felled.HarvestLocationId);

            // Day 211 — haul: the 1200 kg log wagon splits 5 logs into 4 + 1.
            HaulBothToMill(world, lot);
            Assert.AreEqual(0, world.Logging.LandingLogUnits);
            Assert.AreEqual(5, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(2, world.Mill.LogStock.Lots.Count);
            int hauledUnits = 0;
            var deliveredLotIds = new List<string>();
            foreach (LogLot delivered in world.Mill.LogStock.Lots)
            {
                hauledUnits += delivered.LogUnits;
                deliveredLotIds.Add(delivered.LotId.ToString());
                LogLotCustodyRecord custodyRecord = world.Ledger.GetLot(delivered.LotId.ToString());
                Assert.IsNotNull(custodyRecord, "split cargo lot must be registered: " + delivered.LotId);
                Assert.AreEqual(LogLotCustodyState.Delivered, custodyRecord.State);
            }
            Assert.AreEqual(5, hauledUnits, "no log in two places, none lost on the road");

            // Day 212 — saw: 4 logs -> 16 lumber + 8 fuel, 1 log -> 4 lumber + 2 fuel.
            SawBoth(world);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(20, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(2, world.Mill.LumberStock.Lots.Count);
            int fuelUnits = 0;
            foreach (SlabOffcutFuelLot byproduct in world.Mill.ByproductStock)
                fuelUnits += byproduct.FuelWoodUnits;
            Assert.AreEqual(10, fuelUnits, "slab offcuts stay at the mill as fuel, never sold as lumber");
            Assert.AreEqual(150, world.Counters.TotalSawMinutes);
            Assert.AreEqual(20, world.Counters.TotalLumberUnits);
            Assert.AreEqual(2, deliveredLotIds.Count);
            foreach (string deliveredLotId in deliveredLotIds)
            {
                LogLotCustodyRecord processed = world.Ledger.GetLot(deliveredLotId);
                Assert.IsNotNull(processed, "sawn lot must stay on record: " + deliveredLotId);
                Assert.AreEqual(LogLotCustodyState.Processed, processed.State);
            }

            // Day 212 — transfer: 20u wholesale at 32c = 640c yard -> mill.
            TransferBothToYard(world);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(20, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(YardOpeningCashCents - 640, world.YardCash.BalanceCents);
            Assert.AreEqual(MillOpeningCashCents + 640, world.MillCash.BalanceCents);

            // Day 213 — commission: the yard holds real stock, so the price
            // port quotes the real retail (32c landed x 1.25 = 40c).
            Assert.AreEqual(40, world.PricePort.UnitPriceCents(LumberRequirement(16)));
            ConstructionProject project = CommissionCabin(world, out NpcConstructionRecord record);
            WorkPackage framing = project.WorkPackages[0];
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
            Assert.AreEqual(640, record.FindFunding(framing.PackageId).EstimatedMaterialCostCents);

            // Day 214 — freight quote from the real calculator, then procure.
            FreightServiceQuote quote = QuoteFreight(16);
            Assert.AreEqual(1, quote.TripCount);
            Assert.AreEqual(28, quote.UnitsPerTrip);
            Assert.AreEqual(37, quote.PerTripTotalCents);
            Assert.AreEqual(37, quote.TotalPriceCents);
            var adapter = new NpcConstructionProcurementAdapter(world.Commissioner, world.Procurement,
                world.SalePort, world.PricePort, world.Directory, world.FreightCoordinator,
                package => world.FreightPlan(project, quote), world.Executor, world.Cash,
                world.Obligations, world.Ids);
            Assert.IsNull(adapter.ProcureAndStage(project, framing, record, ProcureDay, world.Diagnostics),
                string.Join("; ", world.Diagnostics));

            // The package staged from the on-site stockpile, under the
            // requirement's own name, with yard-lot provenance.
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, framing.Status);
            Assert.AreEqual(1, framing.StagedMaterialLines.Count);
            Assert.AreEqual(16, framing.StagedMaterialLines[0].Units);
            Assert.AreEqual("lumber", framing.StagedMaterialLines[0].DisplayName);
            Assert.IsTrue(framing.StagedMaterialLines[0].Provenance.Contains("yard lot"),
                framing.StagedMaterialLines[0].Provenance);
            Assert.AreEqual(0, adapter.LastStockpile.AvailableUnits("lumber"));

            // Exact cash: 640c goods + 37c freight, paid once each.
            Assert.AreEqual(HouseholdOpeningCashCents - 640 - 37, world.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(YardOpeningCashCents - 640 + 640, world.YardCash.BalanceCents);
            Assert.AreEqual(MillOpeningCashCents + 640, world.MillCash.BalanceCents);
            Assert.AreEqual(37, world.CarrierPurse.ReadBalanceCents());
            Assert.AreEqual(37, world.FreightCoordinator.LastFreightPaidCents);
            Assert.AreEqual(640, record.FindFunding(framing.PackageId).ActualMaterialCostCents);
            Assert.AreEqual(37, record.FindFunding(framing.PackageId).FreightCostCents);
            int cashTotal = world.HouseholdLedger.GetBalanceCents() + world.YardCash.BalanceCents
                + world.MillCash.BalanceCents + world.CarrierPurse.ReadBalanceCents();
            Assert.AreEqual(HouseholdOpeningCashCents + YardOpeningCashCents + MillOpeningCashCents, cashTotal,
                "no cent created or destroyed across the whole chain");

            // Exact units: 20 sawn = 16 staged + 4 still at the yard.
            Assert.AreEqual(4, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1, world.FreightService.TrackedShipments.Count);
            Assert.AreEqual("cship-0", world.FreightService.TrackedShipments[0].ShipmentId);
        }

        // ---- T2: the negative companion — workstation unestablished ----

        [Test]
        public void C9_Negative_WorkstationUnestablished_ZeroLumberZeroSales()
        {
            World world = NewWorld(false);

            // Felling and hauling need no saw-line: the logs still arrive.
            LogLot lot = FellFive(world);
            HaulBothToMill(world, lot);
            Assert.AreEqual(5, world.Mill.LogStock.TotalLogUnits);

            // Sawing refuses at the unestablished workstation and consumes nothing.
            LogLot firstLot = world.Mill.LogStock.Lots[0];
            SawmillProductionJob job = world.SawDriver.BeginProduction(
                firstLot.LotId.ToString(), Sawyer, firstLot.LogUnits, SawDay, out string beginRefusal);
            Assert.IsNotNull(job, beginRefusal);
            bool ran = world.SawDriver.RunProduction(job.JobId, SawDay, out string refusal);
            Assert.IsFalse(ran);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("sawmill-saw-line") || refusal.Contains("workstation"),
                "the refusal must name its cause: " + refusal);
            Assert.AreEqual(5, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Counters.TotalSawMinutes);

            // Nothing to transfer: no lumber lot exists at the mill.
            SawmillToYardTransferJob transfer = world.TransferDriver.BeginTransfer(
                "lot-that-does-not-exist", "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, SawDay, out string transferRefusal);
            Assert.IsNull(transfer);
            Assert.IsNotNull(transferRefusal);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);

            // Nothing to price: the empty yard quotes -1, so commissioning
            // records the package unpriced and procurement refuses it.
            Assert.AreEqual(-1, world.PricePort.UnitPriceCents(LumberRequirement(16)));
            ConstructionProject project = CommissionCabin(world, out NpcConstructionRecord record);
            WorkPackage framing = project.WorkPackages[0];
            NpcBuildPackageFunding funding = record.FindFunding(framing.PackageId);
            Assert.AreEqual(0, funding.EstimatedMaterialCostCents);
            Assert.IsTrue(funding.Notes.Contains("unpriced"), funding.Notes);
            FreightServiceQuote quote = QuoteFreight(16);
            var adapter = new NpcConstructionProcurementAdapter(world.Commissioner, world.Procurement,
                world.SalePort, world.PricePort, world.Directory, world.FreightCoordinator,
                package => world.FreightPlan(project, quote), world.Executor, world.Cash,
                world.Obligations, world.Ids);
            string procureRefusal = adapter.ProcureAndStage(project, framing, record, ProcureDay, world.Diagnostics);
            Assert.IsNotNull(procureRefusal);
            Assert.IsTrue(procureRefusal.Contains("unpriced"), "the refusal must name its cause: " + procureRefusal);
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);

            // Zero lumber, zero sales, zero cash moved anywhere.
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(YardOpeningCashCents, world.YardCash.BalanceCents);
            Assert.AreEqual(MillOpeningCashCents, world.MillCash.BalanceCents);
            Assert.AreEqual(HouseholdOpeningCashCents, world.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(0, world.CarrierPurse.ReadBalanceCents());
        }

        // ---- T3: prices come from real stock, never invented ----

        [Test]
        public void C9_PricePortQuotesFromRealStock_EmptyYardIsUnpriced()
        {
            World world = NewWorld(true);

            // Empty yard: no stock, no quote, and the commissioner says so.
            Assert.AreEqual(-1, world.PricePort.UnitPriceCents(LumberRequirement(16)));
            ConstructionProject early = CommissionCabin(world, out NpcConstructionRecord earlyRecord);
            NpcBuildPackageFunding earlyFunding = earlyRecord.FindFunding(early.WorkPackages[0].PackageId);
            Assert.AreEqual(0, earlyFunding.EstimatedMaterialCostCents);
            Assert.IsTrue(earlyFunding.Notes.Contains("unpriced"), earlyFunding.Notes);

            // Run the chain: the same requirement now prices at the real retail.
            LogLot lot = FellFive(world);
            HaulBothToMill(world, lot);
            SawBoth(world);
            TransferBothToYard(world);
            Assert.AreEqual(40, world.PricePort.UnitPriceCents(LumberRequirement(16)));
            ConstructionProject late = CommissionCabin(world, out NpcConstructionRecord lateRecord);
            Assert.AreEqual(640, lateRecord.FindFunding(late.WorkPackages[0].PackageId).EstimatedMaterialCostCents);
        }
    }
}
