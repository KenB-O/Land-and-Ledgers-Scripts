using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

// F7 Tier 1 (2026-11-13): SawmillToYardTransferDriver moves whole
// SawmillLumberLot units from the shared mill LumberStock into the yard
// LumberStock through the existing yard intake, with grade assigned at
// intake, provenance copied, and the transfer recorded exactly once.
// Tests are red-first: they fail until SawmillToYardTransferDriver.cs
// lands. Commit bar per Agent 3 note 93: 3252 passed / 43 failed,
// total 3295 (3227 + 25 new tests, zero flips).
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class SawmillToYardTransferDriverTests
    {
        private const int Day = 210;
        private const int TransferDay = 220;
        private static readonly EntityId Sawyer = EntityId.For(EntityKind.Person, 5002);

        private sealed class World
        {
            public SawmillShopRuntime Mill;
            public LumberYardShopRuntime Yard;
            public List<string> Diagnostics = new List<string>();
            public SawmillToYardTransferDriver Driver;
            public TestBusinessCashPort YardCash;
            public TestBusinessCashPort MillCash;
        }

        /// <summary>
        /// C-4: in-memory business cash port for the settlement tests.
        /// Mirrors the production Series2BusinessRuntimeCashPort contract:
        /// Credit cannot fail for a non-negative amount on a bound port;
        /// TryDebit is atomic (refuses when short, moving nothing) and
        /// returns null on success, a diagnostic on failure.
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
            var driver = new SawmillToYardTransferDriver(mill, yard, diagnostics);
            // C-4: the fixture binds real cash ports, as the production
            // caller does each drive. The yard opens flush (1,000,000c)
            // so the pre-C-4 transfer tests settle without an
            // affordability refusal; the mill opens at 0c and is only
            // ever credited. C-4 tests override the balances.
            var yardCash = new TestBusinessCashPort("yard-1", 1000000);
            var millCash = new TestBusinessCashPort("mill-1", 0);
            driver.YardCash = yardCash;
            driver.MillCash = millCash;
            return new World { Mill = mill, Yard = yard, Diagnostics = diagnostics, Driver = driver, YardCash = yardCash, MillCash = millCash };
        }

        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units,
            string millId = "mill-1", string standId = "stand-1")
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                OwnerId = millId,
                SourceLogLotId = "log-" + lotNumber,
                StandId = standId,
                Species = "white pine",
                MillBusinessId = millId,
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        private static string LotId(int lotNumber)
        {
            return EntityId.For(EntityKind.Lot, lotNumber).ToString();
        }

        private static void ReceiveMillLot(World world, SawmillLumberLot lot)
        {
            Assert.IsNull(world.Mill.LumberStock.ReceiveLot(lot, world.Diagnostics));
        }

        private static LumberYardGradePricePolicy PricedPolicy()
        {
            var policy = new LumberYardGradePricePolicy { MarkupRatio = 0.25f, SeasonalMultiplier = 1f };
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("clear", 1.5f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("merchantable", 1.0f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("cull", 0.4f));
            return policy;
        }

        private static SawmillToYardTransferDriver.SawmillToYardTransferDriverSaveDto LinkDto(
            string jobId, string millLotId, string gradeId, int unitCostCents, int state)
        {
            var dto = new SawmillToYardTransferDriver.SawmillToYardTransferDriverSaveDto();
            dto.TransferJobLinks.Add(new SawmillToYardTransferDriver.SawmillToYardTransferDriverSaveDto.SawmillToYardTransferJobLink
            {
                JobId = jobId,
                MillLotId = millLotId,
                MillBusinessId = "mill-1",
                YardBusinessId = "yard-1",
                YardGradeId = gradeId,
                UnitCostCents = unitCostCents,
                State = state,
                CreatedDayIndex = Day,
            });
            return dto;
        }

        // ---- F6 fixture (T4 only): saw one real 2-log white pine lot ----

        private static LogLot DocumentedLogLot(int lotNumber, int logs, string standId,
            string species = "white pine", int felledDayIndex = 200)
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
                FelledDayIndex = felledDayIndex,
            };
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

        private static string SawRealLumberLot(World world, int day = Day)
        {
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(day);
            var counters = new SawmillLabourCounters();
            var gate = BuildGate();
            var production = new SawmillProductionDriver(world.Mill, tasks, budgets, gate, counters, world.Diagnostics);
            Assert.IsNull(world.Mill.ReceiveLogLot(DocumentedLogLot(9101, 2, "stand-1"), world.Diagnostics));
            SawmillProductionJob productionJob = production.BeginProduction(LotId(9101), Sawyer, 2, day, out string beginRefusal);
            Assert.IsNotNull(productionJob);
            Assert.IsNull(beginRefusal);
            Assert.IsTrue(production.RunProduction(productionJob.JobId, day, out string runRefusal));
            Assert.IsNull(runRefusal);
            Assert.AreEqual(1, productionJob.SourceLumberLotIds.Count);
            return productionJob.SourceLumberLotIds[0];
        }

        // ---- BeginTransfer (8: B1-B8) ----

        [Test]
        public void B1_BeginTransfer_Refuses_BlankMillLotId()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(string.Empty, "merchantable", 85, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.TransferJobCount);
        }

        [Test]
        public void B2_BeginTransfer_Refuses_UnknownGrade()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "select-fancy", 85, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.TransferJobCount);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void B3_BeginTransfer_Refuses_LotAbsentFromMillYard()
        {
            World world = NewWorld();
            // The lot is built but never received into mill LumberStock.

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.TransferJobCount);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void B4_BeginTransfer_TwiceForSameLot_Refused()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            Assert.IsNotNull(world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string firstRefusal));
            Assert.IsNull(firstRefusal);

            SawmillToYardTransferJob second = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string refusal);

            Assert.IsNull(second);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(1, world.Driver.TransferJobCount);
        }

        [Test]
        public void B5_BeginTransfer_Refuses_WhenYardAlreadyHoldsSource()
        {
            World world = NewWorld();
            SawmillLumberLot lot = DocumentedMillLot(9301, 40);
            ReceiveMillLot(world, lot);
            Assert.IsNull(world.Yard.ReceiveLumberFromSawmill(lot, "merchantable", TransferDay, 85, world.Diagnostics));
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            // Mill ReceiveLot has no duplicate check, so a fresh descriptor
            // with the same LotId and 40 units is received into mill stock.
            SawmillLumberLot fresh = DocumentedMillLot(9301, 40);
            ReceiveMillLot(world, fresh);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.TransferJobCount);
            Assert.AreEqual(1, world.Yard.LumberStock.Lots.Count);
        }

        [Test]
        public void B6_BeginTransfer_Refuses_NegativeUnitCost()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", -5, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.TransferJobCount);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void B7_BeginTransfer_Success_RecordsJob()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string refusal);

            Assert.IsNotNull(job);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State);
            Assert.AreEqual("yard-transfer:" + LotId(9301), job.JobId);
            Assert.AreEqual("mill-1", job.MillBusinessId);
            Assert.AreEqual("yard-1", job.YardBusinessId);
            Assert.AreEqual("merchantable", job.YardGradeId);
            Assert.AreEqual(85, job.UnitCostCents);
            Assert.AreEqual(Day, job.CreatedDayIndex);
            Assert.AreEqual(0, job.LumberUnits);
            Assert.IsTrue(job.YardLotId == EntityId.Invalid, "No yard lot exists until the transfer.");
            Assert.AreEqual(-1, job.AcquiredDayIndex);
        }

        [Test]
        public void B8_BeginTransfer_ZeroCost_Refused_NeverQuotesOneCent()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 0, Day, out string refusal);

            Assert.IsNull(job, "C-4: a zero unit cost is refused at begin — a transfer is a wholesale purchase at a real price, never a free gift.");
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.TransferJobCount);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1000000, world.YardCash.BalanceCents, "No cash moved on a refused begin.");

            int quote = world.Yard.QuoteRetailLumberPrice(null, "merchantable", PricedPolicy(), world.Diagnostics);
            Assert.AreEqual(-1, quote, "A yard holding no lumber has no price to quote — never 1c for a free lot.");
        }

        // ---- RunTransfer success and conservation (6: T1-T6) ----

        [Test]
        public void T1_RunTransfer_MovesWholeLot_ExactlyOnce()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool ran = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(ran);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, job.State);
            Assert.AreEqual(40, job.LumberUnits);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1, world.Yard.LumberStock.Lots.Count);
            LumberYardLumberLot yardLot = world.Yard.LumberStock.Lots[0];
            Assert.AreEqual(LotId(9301), yardLot.SourceSawmillLotId);
            Assert.AreEqual("mill-1", yardLot.SourceMillBusinessId);
            Assert.AreEqual("log-9301", yardLot.SourceLogLotId);
            Assert.AreEqual("stand-1", yardLot.StandId);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 5002), yardLot.SawedBy);
            Assert.AreEqual(210, yardLot.SawedDayIndex);
            Assert.AreEqual("softwood-standard", yardLot.ConversionProfileId);
            Assert.AreEqual("white pine", yardLot.Species);
            Assert.AreEqual("merchantable", yardLot.YardGradeId);
            Assert.AreEqual(85, yardLot.UnitCostCents);
            Assert.AreEqual(TransferDay, yardLot.AcquiredDayIndex);
            Assert.AreEqual(LumberYardLumberSourceKind.MillDirect, yardLot.SourceKind);
            Assert.AreEqual(yardLot.LotId, job.YardLotId);
        }

        [Test]
        public void T2_BlankGrade_ResolvesToMerchantable_AtBegin()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), null, 85, Day, out string refusal);

            Assert.IsNotNull(job);
            Assert.IsNull(refusal);
            Assert.AreEqual("merchantable", job.YardGradeId);
            Assert.IsTrue(string.Join(" ", world.Diagnostics).Contains("merchantable"),
                "The default sort must be recorded loudly, not silently invented.");

            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string runRefusal));
            Assert.IsNull(runRefusal);
            Assert.AreEqual("merchantable", world.Yard.LumberStock.Lots[0].YardGradeId);
        }

        [Test]
        public void T3_CostPassthrough_PinsQuotes()
        {
            World world = NewWorld();
            // The StockedYard shape, with both lots naming the fixture mill:
            // mill ReceiveLot refuses foreign-mill lumber (note 93, condition 2).
            ReceiveMillLot(world, DocumentedMillLot(9301, 40, "mill-1", "stand-1"));
            ReceiveMillLot(world, DocumentedMillLot(9302, 30, "mill-1", "stand-2"));
            SawmillToYardTransferJob first = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string firstRefusal);
            Assert.IsNotNull(first);
            Assert.IsNull(firstRefusal);
            Assert.IsTrue(world.Driver.RunTransfer(first.JobId, TransferDay, out string firstRunRefusal));
            Assert.IsNull(firstRunRefusal);
            SawmillToYardTransferJob second = world.Driver.BeginTransfer(LotId(9302), "clear", 95, Day, out string secondRefusal);
            Assert.IsNotNull(second);
            Assert.IsNull(secondRefusal);
            Assert.IsTrue(world.Driver.RunTransfer(second.JobId, 230, out string secondRunRefusal));
            Assert.IsNull(secondRunRefusal);

            var policy = PricedPolicy();
            Assert.AreEqual(106, world.Yard.QuoteRetailLumberPrice(null, "merchantable", policy, world.Diagnostics),
                "85c x 1.25 = 106.25 -> 106.");
            Assert.AreEqual(178, world.Yard.QuoteRetailLumberPrice(null, "clear", policy, world.Diagnostics),
                "95c x 1.25 x 1.5 = 178.125 -> 178.");
            Assert.AreEqual(112, world.Yard.QuoteRetailLumberPrice(null, null, policy, world.Diagnostics),
                "((40x85 + 30x95) / 70) x 1.25 = 111.6 -> 112.");
        }

        [Test]
        public void T4_F6ProducedLot_TransfersEndToEnd()
        {
            World world = NewWorld();
            string lumberLotId = SawRealLumberLot(world);
            Assert.AreEqual(8, world.Mill.LumberStock.TotalLumberUnits);

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(lumberLotId, "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);
            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal));
            Assert.IsNull(refusal);

            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(8, world.Yard.LumberStock.TotalLumberUnits);
            LumberYardLumberLot yardLot = world.Yard.LumberStock.Lots[0];
            Assert.AreEqual(LotId(9101), yardLot.SourceLogLotId,
                "RunSawing records the source log lot id as the log lot's EntityId string.");
            Assert.AreEqual("softwood-standard", yardLot.ConversionProfileId);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 5002), yardLot.SawedBy);
        }

        [Test]
        public void T5_SecondRunTransfer_AfterSuccess_Refused()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);
            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string runRefusal));
            Assert.IsNull(runRefusal);

            bool second = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!second);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, job.State);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1, world.Yard.LumberStock.Lots.Count, "No duplicate yard lot.");
        }

        [Test]
        public void T6_FreshBeginTransfer_AfterSuccess_Refused()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);
            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string runRefusal));
            Assert.IsNull(runRefusal);

            SawmillToYardTransferJob fresh = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string refusal);

            Assert.IsNull(fresh);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(1, world.Driver.TransferJobCount);
            Assert.AreEqual(1, world.Yard.LumberStock.Lots.Count);
        }

        // ---- Refusals leave stock untouched (4: R1-R4) ----

        [Test]
        public void R1_NullRegistryYard_RefusesAtRunTime_MovesNothing()
        {
            var mill = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            var yard = new LumberYardShopRuntime("yard-1", null);
            var diagnostics = new List<string>();
            var driver = new SawmillToYardTransferDriver(mill, yard, diagnostics);
            // C-4: ports bound so the test reaches the intake refusal
            // (the null-registry yard), not the settlement gate.
            driver.YardCash = new TestBusinessCashPort("yard-1", 1000000);
            driver.MillCash = new TestBusinessCashPort("mill-1", 0);
            Assert.IsNull(mill.LumberStock.ReceiveLot(DocumentedMillLot(9301, 40), diagnostics));

            // BeginTransfer still succeeds — the registry is consumed only at intake.
            SawmillToYardTransferJob job = driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool ran = driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(40, mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State, "The job is retryable; nothing moved.");
        }

        [Test]
        public void R2_RunTransfer_UnknownJobId_Refused()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            bool ran = world.Driver.RunTransfer("yard-transfer:no-such-lot", TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void R3_RunTransfer_AfterLotLeavesMillStock_FailsJob()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);
            world.Mill.LumberStock.TryWithdrawUnits(40, world.Diagnostics);

            bool ran = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Failed, job.State);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);

            SawmillToYardTransferJob fresh = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string freshRefusal);
            Assert.IsNull(fresh, "A fresh begin for a lot absent from the mill yard is refused.");
            Assert.IsNotNull(freshRefusal);
        }

        [Test]
        public void R4_FailTransfer_LeavesLotUntouched_FreshBeginSucceeds()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool failed = world.Driver.FailTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(failed);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Failed, job.State);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);

            SawmillToYardTransferJob fresh = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string freshRefusal);
            Assert.IsNotNull(fresh, "A fresh begin is allowed because no open job remains.");
            Assert.IsNull(freshRefusal);
        }

        [Test]
        public void C5_UnaffordablePurchaseDoesNotQueueOrStrandLot_AndRecoversWhenCashReturns()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            const int fullPriceCents = 40 * 85;

            Assert.IsNull(world.YardCash.TryDebit(999000, "simulate yard cash shortage"));
            Assert.AreEqual(1000, world.YardCash.BalanceCents);
            SawmillToYardTransferJob unaffordable = world.Driver.BeginTransfer(
                LotId(9301), "merchantable", 85, Day, out string beginRefusal);

            Assert.IsNull(unaffordable);
            Assert.IsNotNull(beginRefusal);
            Assert.AreEqual(0, world.Driver.TransferJobCount, "An unpayable purchase must not become a persistent open job.");
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits, "The mill retains ownership while the yard cannot pay.");
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1000, world.YardCash.BalanceCents);
            Assert.AreEqual(0, world.MillCash.BalanceCents);

            Assert.IsNull(world.YardCash.Credit(fullPriceCents, "restore yard purchase cash"));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(
                LotId(9301), "merchantable", 85, Day, out string affordableBeginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(affordableBeginRefusal);

            // Cash can change between the attempt being created and its
            // settlement. A rejected payment closes this attempt without
            // moving lumber; it does not leave a daily retry behind.
            Assert.IsNull(world.YardCash.TryDebit(fullPriceCents, "cash became unavailable before settlement"));
            int cashBeforeRefusedRun = world.YardCash.BalanceCents;
            Assert.IsFalse(world.Driver.RunTransfer(job.JobId, TransferDay, out string runRefusal));
            Assert.IsNotNull(runRefusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Failed, job.State);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(cashBeforeRefusedRun, world.YardCash.BalanceCents);
            Assert.AreEqual(0, world.MillCash.BalanceCents);

            Assert.IsNull(world.YardCash.Credit(fullPriceCents, "yard later has funds"));
            SawmillToYardTransferJob recovered = world.Driver.BeginTransfer(
                LotId(9301), "merchantable", 85, TransferDay, out string recoveredBeginRefusal);
            Assert.IsNotNull(recovered);
            Assert.IsNull(recoveredBeginRefusal);
            Assert.IsTrue(world.Driver.RunTransfer(recovered.JobId, TransferDay, out string recoveredRunRefusal));
            Assert.IsNull(recoveredRunRefusal);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(fullPriceCents, world.MillCash.BalanceCents);
            Assert.AreEqual(1000, world.YardCash.BalanceCents,
                "The yard retains the 1,000c opening balance present before the successful retry.");
        }

        [Test]
        public void S5_SaveRestoredUnpayableAttemptClosesAndCanBeReofferedAfterCashRecovery()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            // Simulate a Planned purchase saved before the yard's balance
            // fell below the stored lot price. This is the recovery case
            // behind old long-run open-job backlogs.
            var restored = new SawmillToYardTransferDriver(
                world.Mill, world.Yard, new List<string>());
            restored.YardCash = world.YardCash;
            restored.MillCash = world.MillCash;
            restored.LoadFromSaveDto(LinkDto(
                "yard-transfer:" + LotId(9301), LotId(9301), "merchantable", 85, 0));
            SawmillToYardTransferJob job = restored.GetJob("yard-transfer:" + LotId(9301));
            Assert.IsNotNull(job);
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State);

            // The saved attempt remains pending while the business spends
            // its available cash. Recreate that current-world balance before
            // exercising recovery; the DTO intentionally stores no cash.
            Assert.IsNull(world.YardCash.TryDebit(999000, "simulate intervening yard expenses"));
            Assert.AreEqual(1000, world.YardCash.BalanceCents);

            Assert.IsFalse(restored.RunTransfer(job.JobId, TransferDay, out string refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Failed, job.State,
                "An unaffordable restored attempt must not remain in the daily retry set.");
            Assert.AreEqual(0, restored.JobsInState(SawmillToYardTransferJobState.Planned).Count);
            Assert.AreEqual(0, restored.ToSaveDto().TransferJobLinks.Count,
                "A failed attempt must not return as another open job after Save/Continue.");
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits,
                "The sawmill keeps ownership while the yard cannot pay.");
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1000, world.YardCash.BalanceCents);
            Assert.AreEqual(0, world.MillCash.BalanceCents);

            Assert.IsNull(world.YardCash.Credit(2400, "restore funds for the lumber purchase"));
            SawmillToYardTransferJob recovered = restored.BeginTransfer(
                LotId(9301), "merchantable", 85, TransferDay, out string beginRefusal);
            Assert.IsNotNull(recovered);
            Assert.IsNull(beginRefusal);
            Assert.IsTrue(restored.RunTransfer(recovered.JobId, TransferDay, out string runRefusal));
            Assert.IsNull(runRefusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, recovered.State);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(3400, world.MillCash.BalanceCents);
            Assert.AreEqual(0, world.YardCash.BalanceCents);
        }

        [Test]
        public void C6_LostSettlementAuthorityClosesAttemptWithoutMovingLot_AndCanRetry()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(
                LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            world.Driver.YardCash = null;
            Assert.IsFalse(world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Failed, job.State);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1000000, world.YardCash.BalanceCents);
            Assert.AreEqual(0, world.MillCash.BalanceCents);

            world.Driver.YardCash = world.YardCash;
            SawmillToYardTransferJob retry = world.Driver.BeginTransfer(
                LotId(9301), "merchantable", 85, TransferDay, out string retryRefusal);
            Assert.IsNotNull(retry, "A fresh purchase can be attempted after the cash authority is restored.");
            Assert.IsNull(retryRefusal);
            Assert.IsTrue(world.Driver.RunTransfer(retry.JobId, TransferDay, out string retryRunRefusal));
            Assert.IsNull(retryRunRefusal);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(3400, world.MillCash.BalanceCents);
            Assert.AreEqual(996600, world.YardCash.BalanceCents);
        }

        // ---- Save round trip (4: S1-S4) ----

        [Test]
        public void S1_SaveRoundTrip_PreservesPlannedJob_FinishesThroughFreshDriver()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            var dto = world.Driver.ToSaveDto();
            var freshDiagnostics = new List<string>();
            var fresh = new SawmillToYardTransferDriver(world.Mill, world.Yard, freshDiagnostics);
            // C-4: the fresh driver settles through the same ports.
            fresh.YardCash = world.YardCash;
            fresh.MillCash = world.MillCash;
            fresh.LoadFromSaveDto(dto);

            Assert.AreEqual(1, fresh.TransferJobCount);
            SawmillToYardTransferJob restored = fresh.GetJob(job.JobId);
            Assert.IsNotNull(restored);
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, restored.State);
            Assert.AreEqual("merchantable", restored.YardGradeId);
            Assert.AreEqual(85, restored.UnitCostCents);
            Assert.AreEqual(Day, restored.CreatedDayIndex);
            Assert.AreEqual(-1, restored.AcquiredDayIndex);

            Assert.IsTrue(fresh.RunTransfer(restored.JobId, TransferDay, out string runRefusal));
            Assert.IsNull(runRefusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, restored.State);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void S2_NullDto_ClearsToEmpty()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            Assert.IsNotNull(world.Driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal));
            Assert.IsNull(beginRefusal);

            world.Driver.LoadFromSaveDto(null);

            Assert.AreEqual(0, world.Driver.TransferJobCount);
        }

        [Test]
        public void S3_LoadWithDuplicateJobId_KeepsFirst()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            var dto = LinkDto("yard-transfer:" + LotId(9301), LotId(9301), "merchantable", 85, 0);
            dto.TransferJobLinks.Add(new SawmillToYardTransferDriver.SawmillToYardTransferDriverSaveDto.SawmillToYardTransferJobLink
            {
                JobId = "yard-transfer:" + LotId(9301),
                MillLotId = LotId(9301),
                MillBusinessId = "mill-1",
                YardBusinessId = "yard-1",
                YardGradeId = "clear",
                UnitCostCents = 95,
                State = 0,
                CreatedDayIndex = Day,
            });

            world.Driver.LoadFromSaveDto(dto);

            Assert.AreEqual(1, world.Driver.TransferJobCount);
            SawmillToYardTransferJob kept = world.Driver.GetJob("yard-transfer:" + LotId(9301));
            Assert.IsNotNull(kept);
            Assert.AreEqual("merchantable", kept.YardGradeId, "The first link is kept.");
        }

        [Test]
        public void S4_LoadDropsLink_WhoseSubjectIsGone()
        {
            World world = NewWorld();

            // (a) The mill lot id was never received into mill LumberStock.
            world.Driver.LoadFromSaveDto(LinkDto("yard-transfer:" + LotId(9301), LotId(9301), "merchantable", 85, 0));
            Assert.AreEqual(0, world.Driver.TransferJobCount);

            // (b) The lot was transferred into the yard directly, so the
            // yard already holds the source and the mill lot is a husk.
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            SawmillLumberLot lot = world.Mill.LumberStock.Lots[0];
            Assert.IsNull(world.Yard.ReceiveLumberFromSawmill(lot, "merchantable", TransferDay, 85, world.Diagnostics));
            world.Driver.LoadFromSaveDto(LinkDto("yard-transfer:" + LotId(9301), LotId(9301), "merchantable", 85, 0));
            Assert.AreEqual(0, world.Driver.TransferJobCount, "The transfer already happened by another path.");
        }

        // ---- Null handling (3: N1-N3) ----

        [Test]
        public void N1_NullMill_RefusesEveryMethod()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new SawmillToYardTransferDriver(null, yard, diagnostics);

            SawmillToYardTransferJob job = driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string beginRefusal);
            Assert.IsNull(job);
            Assert.IsNotNull(beginRefusal);
            Assert.IsTrue(!driver.RunTransfer("yard-transfer:" + LotId(9301), TransferDay, out string runRefusal));
            Assert.IsNotNull(runRefusal);
            Assert.IsTrue(!driver.FailTransfer("yard-transfer:" + LotId(9301), TransferDay, out string failRefusal));
            Assert.IsNotNull(failRefusal);
            Assert.AreEqual(0, driver.TransferJobCount);
        }

        [Test]
        public void N2_NullYard_RefusesAtBegin()
        {
            var mill = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new SawmillToYardTransferDriver(mill, null, diagnostics);
            Assert.IsNull(mill.LumberStock.ReceiveLot(DocumentedMillLot(9301, 40), diagnostics));

            SawmillToYardTransferJob job = driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("lumber yard"),
                "The refusal names the missing lumber yard runtime.");
            Assert.AreEqual(0, driver.TransferJobCount);
        }

        [Test]
        public void N3_MissingCashAuthoritiesRefuseBeforeCreatingPurchaseJob()
        {
            var mill = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var driver = new SawmillToYardTransferDriver(mill, yard, null);
            var diag = new List<string>();
            Assert.IsNull(mill.LumberStock.ReceiveLot(DocumentedMillLot(9301, 40), diag));

            Assert.IsNotNull(driver.Diagnostics);
            SawmillToYardTransferJob job = driver.BeginTransfer(LotId(9301), "merchantable", 85, Day, out string refusal);
            Assert.IsNull(job, "A purchase attempt without bound buyer/seller cash authority must not enter the persistent job queue.");
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, driver.TransferJobCount);
            Assert.AreEqual(40, mill.LumberStock.TotalLumberUnits, "Refusal preserves the mill-owned lot.");
        }

        // ---- F7-2 production terms (fix #4) ---------------------------------
        // The weekly sawmill support node cuts phantom timber into category
        // stock the yard transfer never reads (F7-2); the routed leg is
        // TransferSawmillCategoryLumberToYard (SimulationDrivers.cs:2241):
        //   units = min(available, affordable, SawmillToLumberYardWeeklyMaxUnits = 80)
        //   price = consumed x WholesaleLumberUnitCostCents (32c)
        //   yard debit -> yard.ReceiveLumberPurchase -> mill credit, with
        //   restore/refund on failure (SimulationDrivers.cs:2274-2310).
        // The driver and its runtimes are Unity-bound (UnityEngine, sawmill/
        // yard shop runtimes) and outside the Roslyn harness closure, so
        // these pins run under the Unity Test Runner, not the harness.
        // The 80/week cap constant lives in harness-excluded
        // SharedBusinessRuntimeManager.cs:86 (internal const, source-verified);
        // the driver-side terms below are what the tests pin.

        [Test]
        public void F7_ProductionTerms_WholesalePriceIs32Cents()
        {
            Assert.AreEqual(32, SawmillToYardTransferDriver.WholesaleLumberUnitCostCents,
                "The routed F7-2 leg settles at 32c/unit; a changed constant changes what the yard pays and the mill earns.");
        }

        [Test]
        public void F7_TransferAtProductionTerms_MovesWholeLotOnce_Settles32PerUnit()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));
            const int expectedPriceCents = 40 * 32;

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal));
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, job.State);
            Assert.AreEqual(40, job.LumberUnits, "the whole lot moves");
            Assert.AreEqual(expectedPriceCents, job.SettledPriceCents, "settled at 32c per unit");
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            LumberYardLumberLot yardLot = world.Yard.LumberStock.Lots[0];
            Assert.AreEqual(32, yardLot.UnitCostCents, "the yard lot carries the production price");
            Assert.AreEqual(1000000 - expectedPriceCents, world.YardCash.BalanceCents, "yard debited by the settled price");
            Assert.AreEqual(expectedPriceCents, world.MillCash.BalanceCents, "mill credited by the same amount");

            Assert.IsFalse(world.Driver.RunTransfer(job.JobId, TransferDay, out string secondRefusal),
                "a transferred job never transfers again");
            Assert.IsNotNull(secondRefusal);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits, "no second move");
            Assert.AreEqual(expectedPriceCents, world.MillCash.BalanceCents, "no second credit");
        }

        [Test]
        public void F7_WeeklyCapQuantity_TransfersInFull()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 80));
            const int expectedPriceCents = 80 * 32;

            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(9301), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal));
            Assert.IsNull(refusal);
            Assert.AreEqual(80, job.LumberUnits, "the weekly cap quantity (80u) transfers in full at the production terms");
            Assert.AreEqual(expectedPriceCents, job.SettledPriceCents);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(80, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1000000 - expectedPriceCents, world.YardCash.BalanceCents);
            Assert.AreEqual(expectedPriceCents, world.MillCash.BalanceCents);
        }
    }
}
