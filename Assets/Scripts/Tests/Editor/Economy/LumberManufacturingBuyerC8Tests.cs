using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

// C-8 (expanded campaign 2026-11-16): the lumber supply interface for
// manufacture. The yard's retail path ALREADY serves a manufacturing
// buyer — TrySellLumberRetail takes a species/grade filter, withdraws
// real lots FIFO under it, and records the sale with provenance; the C-6
// delivery leg then moves the lumber to the buyer's shop with real
// freight and per-trip fare settlement. These tests PROVE that interface
// for a furniture maker (the carpenter's shop, F1 part 2's consumer):
// species/grade selection, loud refusal on wrong species or shortfall,
// and the full purchase (goods settled buyer-side, F4 Pay pattern; fare
// settled by the delivery service). No new production code — the
// interface is the existing retail sale + delivery rails.
//
// Toll sawing (RunTollSawing) is the custom-sawing path: a customer
// brings their own logs, the mill saws them under explicit toll terms
// (cash per log and/or an in-kind share), the customer keeps the lumber
// minus the mill's toll, and the byproducts wait in the pickup register.
// It has NO production caller — it is documented here as the available
// custom-sawing path and left unwired unless F needs it (the scan test
// below pins that no production code calls it). The furniture recipes
// consume lumber UNITS as the commercial unit (bed 2, table 2, chair 1,
// cabinet-small 3, cabinet-large 4, shelf 1, crate 1; one unit is one
// milled white-pine board) — a maker buys units through this interface,
// never logs.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class LumberManufacturingBuyerC8Tests
    {
        private const int Day = 210;
        private const int DeliveryDay = 220;

        // The production card (DevelopmentTownConfiguration asset):
        // 25c base + 1c/mile, round trip charged. 28u on 6 miles is one
        // trip: ceil(6 x 2) = 12 chargeable miles x 1c + 25c = 37c.
        private const int SixMileTripChargeCents = 37;

        private sealed class World
        {
            public SawmillShopRuntime Mill;
            public LumberYardShopRuntime Yard;
            public List<string> Diagnostics = new List<string>();
            public SawmillToYardTransferDriver TransferDriver;
            public TestBusinessCashPort YardCash;
            public TestBusinessCashPort MillCash;
            public FreightShipmentService Freight = new FreightShipmentService();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public EquipmentAsset Wagon;
            public DevelopmentTownConfiguration.FreightServiceRateCard RateCard;
            public TestBusinessCashPort BuyerCash;
            public TestBusinessCashPort CarrierCash;
            public int ReceivedUnits;

            public int ReceiveShipment(LogisticsShipmentState shipment, int quantityUnits, out string reason)
            {
                reason = string.Empty;
                if (shipment == null || quantityUnits <= 0)
                {
                    reason = "no shipment or nothing to receive";
                    return 0;
                }
                ReceivedUnits += quantityUnits;
                return quantityUnits;
            }
        }

        /// <summary>
        /// C-4: in-memory business cash port. Mirrors the production
        /// Series2BusinessRuntimeCashPort contract: Credit cannot fail
        /// for a non-negative amount on a bound port; TryDebit is atomic
        /// (refuses when short, moving nothing) and returns null on
        /// success, a diagnostic on failure.
        /// </summary>
        private sealed class TestBusinessCashPort : IBusinessIdentityCashPort
        {
            public string BusinessId { get; }
            public int BalanceCents { get; private set; }
            public bool RefuseCredit { get; set; }
            public bool RefuseDebit { get; set; }

            public TestBusinessCashPort(string businessId, int openingBalanceCents)
            {
                BusinessId = businessId;
                BalanceCents = openingBalanceCents;
            }

            public void SetBalance(int balanceCents)
            {
                BalanceCents = balanceCents;
            }

            public string TryDebit(int amountCents, string reason)
            {
                if (RefuseDebit) return "TestBusinessCashPort: debit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative debit.";
                if (BalanceCents < amountCents) return $"TestBusinessCashPort: insufficient funds ({BalanceCents}c for {amountCents}c).";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (RefuseCredit) return "TestBusinessCashPort: credit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative credit.";
                BalanceCents += amountCents;
                return null;
            }
        }

        private static World NewWorld()
        {
            var mill = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var transferDriver = new SawmillToYardTransferDriver(mill, yard, diagnostics);
            var world = new World
            {
                Mill = mill,
                Yard = yard,
                Diagnostics = diagnostics,
                TransferDriver = transferDriver,
                YardCash = new TestBusinessCashPort("yard-1", 10000),
                MillCash = new TestBusinessCashPort("mill-1", 500),
                Wagon = new EquipmentAsset
                {
                    AssetId = "opening-freight-wagon",
                    Kind = "wagon",
                    DisplayName = "farm wagon",
                    PayloadMassCapacityKg = 450f,
                    CargoVolumeCapacityM3 = 4f,
                    Condition01 = 1f,
                },
                RateCard = new DevelopmentTownConfiguration.FreightServiceRateCard
                {
                    baseServiceFeeCents = 25,
                    ratePerMileCents = 1,
                    chargeRoundTrip = true,
                },
                BuyerCash = new TestBusinessCashPort("carpenter-shop", 10000),
                CarrierCash = new TestBusinessCashPort("freight-co", 0),
            };
            world.TransferDriver.YardCash = world.YardCash;
            world.TransferDriver.MillCash = world.MillCash;
            return world;
        }

        private static LumberYardGradePricePolicy PricedPolicy()
        {
            var policy = new LumberYardGradePricePolicy { MarkupRatio = 0.25f, SeasonalMultiplier = 1f };
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("clear", 1.5f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("merchantable", 1.0f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("cull", 0.4f));
            return policy;
        }

        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units)
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                OwnerId = "mill-1",
                SourceLogLotId = "log-" + lotNumber,
                StandId = "stand-1",
                Species = "white pine",
                MillBusinessId = "mill-1",
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        /// <summary>
        /// C-8: wholesale merchantable + clear white pine into the yard at
        /// 32c (the yard pays the mill, so the yard holds real stock it paid
        /// for). 40u merchantable + 20u clear = 60u for 1920c.
        /// </summary>
        private static void StockYardWithGrades(World world, int merchantableUnits = 40, int clearUnits = 20)
        {
            Assert.IsNull(world.Mill.LumberStock.ReceiveLot(DocumentedMillLot(9301, merchantableUnits), world.Diagnostics));
            SawmillToYardTransferJob merchantableJob = world.TransferDriver.BeginTransfer(
                EntityId.For(EntityKind.Lot, 9301).ToString(), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string merchantableRefusal);
            Assert.IsNotNull(merchantableJob);
            Assert.IsNull(merchantableRefusal);
            Assert.IsTrue(world.TransferDriver.RunTransfer(merchantableJob.JobId, Day + 10, out string merchantableRunRefusal));
            Assert.IsNull(merchantableRunRefusal);

            Assert.IsNull(world.Mill.LumberStock.ReceiveLot(DocumentedMillLot(9302, clearUnits), world.Diagnostics));
            SawmillToYardTransferJob clearJob = world.TransferDriver.BeginTransfer(
                EntityId.For(EntityKind.Lot, 9302).ToString(), "clear",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string clearRefusal);
            Assert.IsNotNull(clearJob);
            Assert.IsNull(clearRefusal);
            Assert.IsTrue(world.TransferDriver.RunTransfer(clearJob.JobId, Day + 10, out string clearRunRefusal));
            Assert.IsNull(clearRunRefusal);

            Assert.AreEqual(merchantableUnits + clearUnits, world.Yard.LumberStock.TotalLumberUnits);
        }

        private static LumberYardSaleRecord SellRetail(World world, int units, string speciesFilter, string gradeFilter)
        {
            int price = world.Yard.QuoteRetailLumberPrice(speciesFilter, gradeFilter, PricedPolicy(), world.Diagnostics);
            Assert.Greater(price, 0, "A priced grade under a real filter must quote — never a guessed price.");
            LumberYardSaleRecord sale = world.Yard.TrySellLumberRetail(
                "carpenter-shop", units, speciesFilter, gradeFilter, price, Day, world.Diagnostics);
            Assert.IsNotNull(sale, string.Join("; ", world.Diagnostics));
            Assert.AreEqual(units, sale.UnitsSoldFor(ConstructionResourceKind.Lumber));
            return sale;
        }

        private static LumberYardRetailDeliveryRequest MakeRequest(World world, LumberYardSaleRecord sale, int miles = 6)
        {
            return new LumberYardRetailDeliveryRequest
            {
                SaleId = sale.SaleId,
                DestinationLabel = "carpenter-shop",
                DestinationBusinessInstanceId = "carpenter-shop",
                Miles = miles,
                TransitGameSeconds = 10000f,
                ConditionMultiplier = 0f,
                YardDelivers = true,
                CarrierLabel = "freight-co",
                BuyerProvidesUnloading = false,
                BuyerBusinessId = "carpenter-shop",
                ReceiveShipment = world.ReceiveShipment,
            };
        }

        private static LumberYardRetailDeliveryExecution MakeExecution(World world)
        {
            return new LumberYardRetailDeliveryExecution
            {
                RateCard = world.RateCard,
                Wagon = world.Wagon,
                BuyerCash = world.BuyerCash,
                CarrierCash = world.CarrierCash,
                LoaderId = EntityId.For(EntityKind.Person, 41),
                DriverId = EntityId.For(EntityKind.Person, 42),
                DraftAnimalIds = new List<EntityId> { EntityId.For(EntityKind.Animal, 1) },
                UnloaderId = EntityId.For(EntityKind.Person, 43),
                Tasks = world.Tasks,
                Budgets = world.Budgets,
                Schedules = world.Schedules,
            };
        }

        private static LumberYardRetailDelivery NewService(World world)
        {
            return new LumberYardRetailDelivery(world.Yard, world.Freight, world.Diagnostics);
        }

        private static LumberYardDeliveryOrder OrderForSale(World world, LumberYardSaleRecord sale)
        {
            foreach (LumberYardDeliveryOrder order in world.Yard.DeliveryRegister.Orders)
            {
                if (order != null && order.SaleId.Equals(sale.SaleId)) return order;
            }
            return null;
        }

        [Test]
        public void C8_ManufacturingBuyer_ClearPineSale_SelectsOnlyClearGrade()
        {
            World world = NewWorld();
            StockYardWithGrades(world);

            Assert.AreEqual(60, world.Yard.QuoteRetailLumberPrice("white pine", "clear", PricedPolicy(), world.Diagnostics),
                "32c landed x 1.25 markup x 1.5 clear multiplier = 60c retail.");
            Assert.AreEqual(40, world.Yard.QuoteRetailLumberPrice("white pine", "merchantable", PricedPolicy(), world.Diagnostics),
                "32c landed x 1.25 markup x 1.0 merchantable multiplier = 40c retail.");

            LumberYardSaleRecord sale = SellRetail(world, 10, "white pine", "clear");

            Assert.AreEqual(600, sale.TotalCents, "10u clear pine at 60c.");
            Assert.AreEqual(1, sale.Lines.Count);
            Assert.AreEqual(10, sale.Lines[0].UnitsSold);
            foreach (LumberYardLumberDispenseLine line in sale.Lines[0].LumberLines)
            {
                Assert.Greater(line.UnitsTaken, 0);
                Assert.AreEqual("white pine", line.Species);
                Assert.AreEqual("clear", line.YardGradeId, "A clear-grade request never draws merchantable stock.");
            }
            Assert.AreEqual(10, world.Yard.LumberStock.AvailableUnitsForSale("white pine", "clear"),
                "20u clear minus 10u sold leaves 10u clear.");
            Assert.AreEqual(40, world.Yard.LumberStock.AvailableUnitsForSale("white pine", "merchantable"),
                "The merchantable stock is untouched by a clear-grade sale.");
            Assert.AreEqual(50, world.Yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void C8_ManufacturingBuyer_WrongSpecies_Refused_NothingConsumed()
        {
            World world = NewWorld();
            StockYardWithGrades(world);

            Assert.AreEqual(-1, world.Yard.QuoteRetailLumberPrice("oak", "clear", PricedPolicy(), world.Diagnostics),
                "No oak stock, no price — never a guessed price.");
            LumberYardSaleRecord sale = world.Yard.TrySellLumberRetail(
                "carpenter-shop", 10, "oak", "clear", 60, Day, world.Diagnostics);

            Assert.IsNull(sale, "A species the yard does not stock refuses loudly.");
            Assert.AreEqual(60, world.Yard.LumberStock.TotalLumberUnits, "A refused sale consumes nothing.");
            Assert.AreEqual(0, world.Yard.SalesHistory.Count);
        }

        [Test]
        public void C8_ManufacturingBuyer_ClearShortfall_Refused_NothingConsumed()
        {
            World world = NewWorld();
            StockYardWithGrades(world);

            LumberYardSaleRecord sale = world.Yard.TrySellLumberRetail(
                "carpenter-shop", 30, "white pine", "clear", 60, Day, world.Diagnostics);

            Assert.IsNull(sale, "30u clear against 20u clear refuses — the 40u merchantable beside it does not cover a clear-grade request.");
            Assert.AreEqual(60, world.Yard.LumberStock.TotalLumberUnits, "A refused sale consumes nothing.");
            Assert.AreEqual(0, world.Yard.SalesHistory.Count);
        }

        [Test]
        public void C8_ManufacturingBuyer_BedBatch_EndToEnd_DeliveredToShop()
        {
            World world = NewWorld();
            StockYardWithGrades(world);
            // Wholesale settlement: 60u at 32c — the yard paid the mill.
            Assert.AreEqual(10000 - 60 * 32, world.YardCash.BalanceCents);
            Assert.AreEqual(500 + 60 * 32, world.MillCash.BalanceCents);

            // A bed takes 2 lumber; 12u clear pine is a 6-bed batch.
            LumberYardSaleRecord sale = SellRetail(world, 12, "white pine", "clear");
            Assert.AreEqual(720, sale.TotalCents, "12u clear pine at 60c.");

            // Goods money is the buyer-side flow's job (the F4 Pay pattern):
            // settled explicitly here, never by the delivery service.
            Assert.IsNull(world.BuyerCash.TryDebit(720, "carpenter-shop pays the yard for 12u clear pine"));
            Assert.IsNull(world.YardCash.Credit(720, "yard receives goods payment for the carpenter-shop sale"));

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNull(refusal, string.Join("; ", world.Diagnostics));
            LumberYardDeliveryOrder order = OrderForSale(world, sale);
            Assert.IsNotNull(order);
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(SixMileTripChargeCents, order.ChargeCents, "The recorded charge IS the DEF-016 quote.");
            Assert.AreEqual(SixMileTripChargeCents, order.SettledDeliveryCents);
            Assert.AreEqual(1, order.ShipmentIds.Count);
            Assert.AreEqual("ydship-0", world.Freight.TrackedShipments[0].ShipmentId);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, world.Freight.TrackedShipments[0].Status);
            Assert.AreEqual(10000 - 720 - SixMileTripChargeCents, world.BuyerCash.BalanceCents,
                "The shop paid goods (720c) plus one delivery fare (37c), nothing else.");
            Assert.AreEqual(SixMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(10000 - 60 * 32 + 720, world.YardCash.BalanceCents, "The yard kept the goods payment on top of its wholesale cost.");
            Assert.AreEqual(500 + 60 * 32, world.MillCash.BalanceCents, "The mill's money does not move on a yard retail sale.");
            Assert.AreEqual(12, world.ReceivedUnits, "The shop took custody of exactly the batch.");
            Assert.AreEqual(48, world.Yard.LumberStock.TotalLumberUnits, "8u clear + 40u merchantable remain.");
            Assert.AreEqual(8, world.Yard.LumberStock.AvailableUnitsForSale("white pine", "clear"));
        }

        // ---- Toll sawing: the custom-sawing path (documented, unwired) ----

        private static LogLot DocumentedLogLot(int lotNumber, int logs, string standId, string species = "white pine")
        {
            return new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LogUnits = logs,
                MassKg = logs * 300f,
                OwnerId = "mill-1",
                StandId = standId,
                Species = species,
                FelledBy = EntityId.For(EntityKind.Person, 5001),
                FelledDayIndex = 200,
            };
        }

        private static SawmillShopRuntime MillWithLogs(int logs)
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9101, logs, "stand-1"), diag));
            return mill;
        }

        private sealed class SawTaskContext
        {
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EntityId TaskId;
            public EquipmentTaskGate Gate;
        }

        private static SawTaskContext StartSawTask(int logs, EntityId worker, int day,
            string species = "white pine", bool hasMotivePower = true, bool completeWork = true)
        {
            int minutes = logs * SawmillConversionData.DefaultForSpecies(species).SawMinutesPerLog;
            var tasks = new TaskAuthority();
            Sawmill.RegisterTaskDefinitions(tasks);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(day);
            var task = tasks.CreateTaskWithPlannedMinutes(Sawmill.SawLogsTaskId,
                EntityId.For(EntityKind.Business, 1), day, minutes);
            Assert.IsTrue(tasks.AssignTask(task.TaskId, worker, budgets, day, out var assignReason), assignReason);
            Assert.IsTrue(tasks.StartTask(task.TaskId, day, out var startReason), startReason);
            if (completeWork)
            {
                Assert.IsTrue(tasks.RecordWorkProgress(task.TaskId, minutes, budgets, day,
                    out bool reached, out var workReason), workReason);
                Assert.IsTrue(reached);
            }

            var workstations = new BusinessWorkstations("mill-1");
            var station = workstations.GetOrCreate("sawmill-saw-line");
            station.SpaceId = "mill-saw-floor";
            station.InstallComponent("saw-line");
            station.InstallComponent("log-carriage");
            station.InstallComponent("drive-system");
            if (hasMotivePower)
                station.MotivePowerSources.Add(new MotivePowerSource(MotivePowerKind.Steam, "recorded steam engine"));
            var gate = new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => workstations,
                FindWorkstationDefinition = id => id == "sawmill-saw-line" ? WorkstationCatalog.SawmillSawLine : null,
                FindComponent = id => id == "saw-line"
                    ? new WorkstationComponentView { AssetId = id, Kind = "saw-line", Condition01 = 1f, IsUsable = true }
                    : id == "log-carriage"
                        ? new WorkstationComponentView { AssetId = id, Kind = "log-carriage", Condition01 = 1f, IsUsable = true }
                        : id == "drive-system"
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
            return new SawTaskContext { Tasks = tasks, Budgets = budgets, TaskId = task.TaskId, Gate = gate };
        }

        private static SawmillTollSawingResult RunTollSawing(SawmillShopRuntime mill,
            string lotId, int logs, EntityId worker, int day, SawmillTollPolicy policy, List<string> diag,
            string species = "white pine")
        {
            var context = StartSawTask(logs, worker, day, species);
            return mill.RunTollSawing(lotId, logs, worker, day, policy, diag,
                context.Tasks, context.TaskId, context.Budgets, context.Gate);
        }

        [Test]
        public void C8_TollSawing_CustomerLogs_SplitByExplicitTerms()
        {
            // A furniture maker brings their own logs for custom sawing:
            // the mill saws them under explicit toll terms; the maker keeps
            // the lumber minus the mill's in-kind toll.
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 4, "stand-9"), "carpenter-shop", "Carpenter's Workshop", 209, diag));
            var policy = new SawmillTollPolicy(tollCashCentsPerLog: 5, tollInKindLumberShare01: 0.25f);

            var result = RunTollSawing(mill,
                EntityId.For(EntityKind.Lot, 9201).ToString(), 4,
                EntityId.For(EntityKind.Person, 5002), 210, policy, diag);

            Assert.NotNull(result);
            Assert.AreEqual(4, result.LogsSawn);
            Assert.AreEqual("carpenter-shop", result.CustomerId);
            Assert.AreEqual(12, result.CustomerLumberUnits, "16 lumber minus the 4-unit in-kind toll.");
            Assert.AreEqual(4, result.TollInKindLumberUnits);
            Assert.AreEqual(20, result.TollCashCents, "4 logs x 5c — returned for the ledger authority, never posted here.");
            Assert.AreEqual(8, result.CustomerFuelWoodUnits, "Slab/offcut byproducts belong to the customer.");
            Assert.AreEqual(120, result.SawMinutes, "4 logs x 30 saw-minutes/log.");

            Assert.AreEqual(4, mill.LumberStock.TotalLumberUnits, "The in-kind toll is the mill's lumber.");
            Assert.AreEqual(12, mill.TollPickup.LumberUnitsFor("carpenter-shop"), "The customer's lumber waits in custody, not mill inventory.");
            Assert.AreEqual(8, mill.TollPickup.FuelWoodUnitsFor("carpenter-shop"));
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits, "The mill's own log yard is untouched by toll work.");
            Assert.AreEqual(0, mill.TollLogStock.TotalCustodyLogUnits, "The toll lot was fully sawn.");
        }

        [Test]
        public void C8_TollSawing_NoTerms_Refused_NothingConsumed()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 4, "stand-9"), "carpenter-shop", "Carpenter's Workshop", 209, diag));
            var policy = new SawmillTollPolicy();

            var result = RunTollSawing(mill,
                EntityId.For(EntityKind.Lot, 9201).ToString(), 4,
                EntityId.For(EntityKind.Person, 5002), 210, policy, diag);

            Assert.IsNull(result, "Custom work without named terms is refused, never assumed.");
            Assert.AreEqual(4, mill.TollLogStock.TotalCustodyLogUnits, "Refused runs consume nothing.");
        }

        /// <summary>
        /// C-8: toll sawing is the documented custom-sawing path and is
        /// LEFT UNWIRED — no production code calls RunTollSawing (the
        /// SawmillToYardTransferDriver states this outright). If a future
        /// slice wires a driver for it, this test must be updated
        /// deliberately, with the driver named. Mirrors the Pass IX scan's
        /// comment-stripping so doc mentions do not count as callers.
        /// </summary>
        [Test]
        public void C8_TollSawing_HasNoProductionCaller_LeftUnwired()
        {
            string scriptsRoot = ResolveScriptsRoot();
            var callers = new List<string>();
            foreach (string file in System.IO.Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string rel = file.Substring(scriptsRoot.Length)
                    .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                if (rel.StartsWith("Tests/", StringComparison.Ordinal)) continue;
                if (rel.EndsWith("SawmillShopRuntime.cs", StringComparison.Ordinal)) continue;
                string text = StripComments(System.IO.File.ReadAllText(file));
                if (text.Contains("RunTollSawing")) callers.Add(rel);
            }
            Assert.IsEmpty(callers,
                "RunTollSawing must have no production caller — toll sawing is the documented custom-sawing path, left unwired unless F needs it. Found callers:\n"
                + string.Join("\n", callers));
        }

        private static string ResolveScriptsRoot()
        {
            var candidates = new List<string>();
            string fromEnv = System.Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                candidates.Add(System.IO.Path.GetFullPath(fromEnv));
            candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "Scripts")));
            candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets", "Scripts")));
            foreach (string candidate in candidates)
                if (System.IO.Directory.Exists(candidate)) return candidate;
            Assert.Fail("Scripts root not found in the tree under test. Tried:\n" + string.Join("\n", candidates));
            return null;
        }

        private static string StripComments(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int idx = lines[i].IndexOf("//", StringComparison.Ordinal);
                if (idx >= 0) lines[i] = lines[i].Substring(0, idx);
            }
            return string.Join("\n", lines);
        }
    }
}
