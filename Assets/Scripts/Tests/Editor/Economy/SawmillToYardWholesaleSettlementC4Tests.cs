using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Primitives;
using NUnit.Framework;

// C-4 (OD-C5, expanded campaign 2026-11-16): the mill-to-yard
// transfer is a wholesale PURCHASE. The F6 gift is gone: a transfer
// settles real cash at a real unit cost through the real financial
// authorities (caller-supplied business cash ports), the yard's retail
// price reflects the landed cost, the mill earns the wholesale margin,
// and no free lumber or phantom cash ever moves. A yard that cannot
// pay keeps the lumber at the mill; a transfer with no cash ports
// bound is refused, never moved unpaid.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class SawmillToYardWholesaleSettlementC4Tests
    {
        private const int Day = 210;
        private const int TransferDay = 220;

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
            var driver = new SawmillToYardTransferDriver(mill, yard, diagnostics);
            // The yard opens at 10000c and the mill at 500c, so a 40u
            // wholesale transfer (1280c) settles with exact, visible
            // balances on both sides.
            var yardCash = new TestBusinessCashPort("yard-1", 10000);
            var millCash = new TestBusinessCashPort("mill-1", 500);
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

        private static SawmillToYardTransferJob BeginWholesale(World world, int lotNumber, int units)
        {
            ReceiveMillLot(world, DocumentedMillLot(lotNumber, units));
            SawmillToYardTransferJob job = world.Driver.BeginTransfer(LotId(lotNumber), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);
            return job;
        }

        [Test]
        public void C4_WholesaleConstant_IsThirtyTwoCents()
        {
            // OD-C5 coherence default: 32c wholesale quotes 40c at
            // retail under the yard's 25% markup. Agent F calibration,
            // HOLD-011 open; the owner corrects the figure, and this
            // pin moves with it — it must never drift silently.
            Assert.AreEqual(32, SawmillToYardTransferDriver.WholesaleLumberUnitCostCents);
        }

        [Test]
        public void C4_WholesaleTransfer_SettlesCashExactly_AndRecordsSettlement()
        {
            World world = NewWorld();
            SawmillToYardTransferJob job = BeginWholesale(world, 9301, 40);

            bool ran = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(ran);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, job.State);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(8720, world.YardCash.BalanceCents, "The yard pays 40u x 32c = 1280c.");
            Assert.AreEqual(1780, world.MillCash.BalanceCents, "The mill earns the 1280c wholesale margin.");
            Assert.AreEqual(1280, job.SettledPriceCents, "The job is the one settlement record.");
            Assert.AreEqual(TransferDay, job.SettledDayIndex);
            Assert.AreEqual(32, world.Yard.LumberStock.Lots[0].UnitCostCents,
                "The yard lot carries the wholesale price as its cost basis.");
        }

        [Test]
        public void C4_WholesaleTransfer_RetailQuotesFortyCents_NotOneCent()
        {
            World world = NewWorld();
            SawmillToYardTransferJob job = BeginWholesale(world, 9301, 40);
            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal));
            Assert.IsNull(refusal);

            int quote = world.Yard.QuoteRetailLumberPrice(null, "merchantable", PricedPolicy(), world.Diagnostics);

            Assert.AreEqual(40, quote, "32c landed x 1.25 markup = 40c. The 1c floor is gone with the gift.");
        }

        [Test]
        public void C4_YardCannotPay_CustodyDoesNotMove_RetryAfterFundingSucceeds()
        {
            World world = NewWorld();
            world.YardCash.SetBalance(100);
            SawmillToYardTransferJob job = BeginWholesale(world, 9301, 40);

            bool ran = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State, "The job is retryable; nothing moved.");
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(100, world.YardCash.BalanceCents, "A refused transfer moves no cash.");
            Assert.AreEqual(500, world.MillCash.BalanceCents);
            Assert.AreEqual(-1, job.SettledPriceCents);

            world.YardCash.SetBalance(5000);
            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay + 1, out string retryRefusal));
            Assert.IsNull(retryRefusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, job.State);
            Assert.AreEqual(3720, world.YardCash.BalanceCents);
            Assert.AreEqual(1780, world.MillCash.BalanceCents);
            Assert.AreEqual(1280, job.SettledPriceCents);
        }

        [Test]
        public void C4_NoCashPorts_TransferRefused_NothingMoves()
        {
            var mill = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new SawmillToYardTransferDriver(mill, yard, diagnostics);
            Assert.IsNull(mill.LumberStock.ReceiveLot(DocumentedMillLot(9301, 40), diagnostics));
            SawmillToYardTransferJob job = driver.BeginTransfer(LotId(9301), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool ran = driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("no yard cash port"),
                "The refusal names the missing port: lumber never moves unpaid.");
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State);
            Assert.AreEqual(40, mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void C4_WrongPortIdentity_TransferRefused()
        {
            World world = NewWorld();
            // The ports are swapped: each names the other business.
            world.Driver.YardCash = new TestBusinessCashPort("mill-1", 10000);
            world.Driver.MillCash = new TestBusinessCashPort("yard-1", 500);
            SawmillToYardTransferJob job = BeginWholesale(world, 9301, 40);

            bool ran = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(string.Join(" ", world.Diagnostics).Contains("miswired"),
                "A miswired port never settles a sale.");
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(10000, world.YardCash.BalanceCents);
            Assert.AreEqual(500, world.MillCash.BalanceCents);
        }

        [Test]
        public void C4_DebitRefusedAfterPrecheck_NothingMoves()
        {
            World world = NewWorld();
            world.YardCash.RefuseDebit = true;
            SawmillToYardTransferJob job = BeginWholesale(world, 9301, 40);

            bool ran = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(10000, world.YardCash.BalanceCents);
            Assert.AreEqual(500, world.MillCash.BalanceCents);
        }

        [Test]
        public void C4_MillCreditRefused_CustodyMoved_DebitStands_SettlementUnset()
        {
            World world = NewWorld();
            world.MillCash.RefuseCredit = true;
            SawmillToYardTransferJob job = BeginWholesale(world, 9301, 40);

            bool ran = world.Driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            // The byproduct-settlement precedent: custody moved, the
            // debit stands, recorded loudly, never re-debited or
            // silently refunded. The job marks Transferred, but the
            // settlement stays unset so the unpaid mill is visible.
            Assert.IsTrue(ran);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("STANDS"), "The refusal names the standing debit.");
            Assert.AreEqual(SawmillToYardTransferJobState.Transferred, job.State);
            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(8720, world.YardCash.BalanceCents, "The yard paid.");
            Assert.AreEqual(500, world.MillCash.BalanceCents, "The mill was not credited.");
            Assert.AreEqual(-1, job.SettledPriceCents);
            Assert.AreEqual(-1, job.SettledDayIndex);
        }

        [Test]
        public void C4_IntakeRefusedAfterDebit_Refunded_NothingMoves()
        {
            var mill = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            var yard = new LumberYardShopRuntime("yard-1", null);
            var diagnostics = new List<string>();
            var driver = new SawmillToYardTransferDriver(mill, yard, diagnostics);
            var yardCash = new TestBusinessCashPort("yard-1", 10000);
            var millCash = new TestBusinessCashPort("mill-1", 500);
            driver.YardCash = yardCash;
            driver.MillCash = millCash;
            Assert.IsNull(mill.LumberStock.ReceiveLot(DocumentedMillLot(9301, 40), diagnostics));
            SawmillToYardTransferJob job = driver.BeginTransfer(LotId(9301), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool ran = driver.RunTransfer(job.JobId, TransferDay, out string refusal);

            Assert.IsTrue(!ran);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("refunded"),
                "The yard debit is refunded when the intake refuses.");
            Assert.AreEqual(SawmillToYardTransferJobState.Planned, job.State, "The job is retryable; nothing moved.");
            Assert.AreEqual(40, mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(10000, yardCash.BalanceCents, "The refund restores the yard balance exactly.");
            Assert.AreEqual(500, millCash.BalanceCents);
        }

        [Test]
        public void C4_Conservation_AcrossTransferAndSale()
        {
            World world = NewWorld();
            SawmillToYardTransferJob job = BeginWholesale(world, 9301, 40);
            Assert.IsTrue(world.Driver.RunTransfer(job.JobId, TransferDay, out string transferRefusal));
            Assert.IsNull(transferRefusal);

            Assert.AreEqual(0, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Yard.LumberStock.TotalLumberUnits);

            // The construction sale adapter charges the yard's real
            // price-port quote verbatim: 10u at the 40c retail quote.
            var pricePort = new LumberYardConstructionPricePort(world.Yard);
            var adapter = new LumberYardConstructionSaleAdapter(world.Yard);
            var requirement = new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-LUMBER",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "Merchantable lumber",
                RequiredUnits = 10,
                UnitLabel = "boards",
                SupplierBusinessId = "yard-1",
            };
            string saleRefusal = adapter.TrySellToProject("yard-1", "project-4", "package-frame",
                new List<ConstructionMaterialRequirement> { requirement }, pricePort, TransferDay,
                world.Diagnostics, out ConstructionMaterialSale sale);

            Assert.IsNull(saleRefusal);
            Assert.IsNotNull(sale);
            Assert.AreEqual(10, sale.Lines[0].Units);
            Assert.AreEqual(40, sale.Lines[0].UnitPriceCents, "The sale charges the 40c quote verbatim, never 1c.");
            Assert.AreEqual(400, sale.TotalCents);
            Assert.AreEqual(30, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits + world.Yard.LumberStock.TotalLumberUnits + sale.TotalUnits,
                "Mill stock + yard stock + sold units is constant across the transfer and the sale.");
        }

        [Test]
        public void C4_SaveRoundTrip_ZeroCostLinkDropped_PositiveCostLinkLoads()
        {
            World world = NewWorld();
            ReceiveMillLot(world, DocumentedMillLot(9301, 40));

            // A pre-C-4 zero-cost link is dropped at load: the lot
            // stays at the mill and a fresh begin at the wholesale
            // price is the recovery path.
            world.Driver.LoadFromSaveDto(LinkDto("yard-transfer:" + LotId(9301), LotId(9301), "merchantable", 0, 0));
            Assert.AreEqual(0, world.Driver.TransferJobCount);
            Assert.IsTrue(string.Join(" ", world.Diagnostics).Contains("not positive"),
                "The drop is recorded loudly.");
            Assert.AreEqual(40, world.Mill.LumberStock.TotalLumberUnits);

            world.Driver.LoadFromSaveDto(LinkDto("yard-transfer:" + LotId(9301), LotId(9301), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, 0));
            Assert.AreEqual(1, world.Driver.TransferJobCount);
            Assert.AreEqual(SawmillToYardTransferDriver.WholesaleLumberUnitCostCents,
                world.Driver.GetJob("yard-transfer:" + LotId(9301)).UnitCostCents);
        }
    }
}
