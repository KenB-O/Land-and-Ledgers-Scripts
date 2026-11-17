using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Trade;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D3 (F-D2-1, F-D2-2): the production drivers for the smith-to-yard
    /// hardware handover and the nails import order. The transfer driver
    /// moves forged pieces from smith shop stock into yard hardware lots
    /// with the piece breakdown in the lot's SupplierNote and the yard
    /// paying the smith the wholesale unit cost per piece; the import
    /// driver keeps the yard stocked with nails through paid import
    /// orders (Pittsburgh ironworks via railhead, 30c, 14 days) and
    /// never conjures stock. No free hardware, no phantom merchant, no
    /// local nail-making industry.
    /// </summary>
    [TestFixture]
    public sealed class HardwareDeliveryDriversTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static BlacksmithRuntime NewSmithy(int iron = 100, int coal = 50, bool forgeReady = true)
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", forgeReady);
            if (iron > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-1", MaterialId = ImportCatalog.IronStockId,
                    MaterialName = "Iron stock", Units = iron,
                    OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-1",
                }, new List<string>());
            }
            if (coal > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-2", MaterialId = ImportCatalog.ForgeCoalId,
                    MaterialName = "Forge coal", Units = coal,
                    OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-2",
                }, new List<string>());
            }
            return smithy;
        }

        private static SkillService NoviceSmith(EntityId smith)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            return skills;
        }

        /// <summary>Forges one piece per named kind on the given day; returns the asset ids.</summary>
        private static List<string> ForgePieces(BlacksmithRuntime smithy, string[] kinds, int dayIndex)
        {
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;
            var ids = new List<string>();
            foreach (string kind in kinds)
            {
                EquipmentAsset piece = smithy.ForgeHardware(kind, smith, dayIndex, skills, diagnostics, out minutes);
                Assert.IsNotNull(piece, string.Join("; ", diagnostics));
                ids.Add(piece.AssetId);
            }
            return ids;
        }

        private static readonly string[] SevenPieces =
        {
            BlacksmithRuntime.DoorHingeKind, BlacksmithRuntime.DoorHingeKind,
            BlacksmithRuntime.DoorHingeKind, BlacksmithRuntime.DoorHingeKind,
            BlacksmithRuntime.IronLatchKind, BlacksmithRuntime.IronLatchKind,
            BlacksmithRuntime.DoorHandleKind,
        };

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

        private static LumberYardHardwareImportDriver NewImportDriver(
            LumberYardShopRuntime yard, TestBusinessCashPort yardCash,
            BusinessLiabilityLedger liabilities, EntityIdRegistry ids)
        {
            var diagnostics = new List<string>();
            var driver = new LumberYardHardwareImportDriver(yard, diagnostics);
            driver.YardCash = yardCash;
            driver.LiabilityLedger = liabilities;
            driver.IdRegistry = ids;
            return driver;
        }

        // ---- F-D2-1: smith-to-yard hardware delivery ----

        // The full handover: pieces leave the smith, one yard lot per kind
        // holds them with the breakdown in its note, the yard pays 7 x 45c and
        // the smith is credited the same.
        [Test]
        public void Delivery_EndToEnd_SettlesCashAndCustody()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var materialLotIds = new HashSet<string>();
            foreach (string id in pieceIds)
            {
                foreach (string lotId in smithy.Assets[id].MaterialLotIds)
                    materialLotIds.Add(lotId);
            }
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var smithCash = new TestBusinessCashPort("smith-1", 0);
            driver.YardCash = yardCash;
            driver.SmithCash = smithCash;

            BlacksmithToYardHardwareTransferJob job = driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string beginRefusal);
            Assert.IsNull(beginRefusal, beginRefusal);
            Assert.IsNotNull(job);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Planned, job.State);
            Assert.AreEqual("1 door-handle, 4 door-hinge, 2 iron-latch", job.PieceBreakdown,
                "kinds sorted by name for a deterministic breakdown");
            Assert.IsTrue(job.SupplierNote.StartsWith("1 door-handle, 4 door-hinge, 2 iron-latch | material lots "),
                $"supplier note carries the breakdown plus the material lots: '{job.SupplierNote}'");

            Assert.IsTrue(driver.RunDelivery(job.JobId, 42, out string runRefusal), runRefusal);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Transferred, job.State);
            Assert.AreEqual(7 * 45, job.SettledPriceCents);
            Assert.AreEqual(0, smithy.Assets.Count, "every delivered piece leaves the smith's stock");
            Assert.AreEqual(7, yard.HardwareStock.TotalHardwareUnits);
            // D6: one yard lot per kind, not one mixed SimpleHardware lot —
            // a kind-level sale must be able to prove the yard holds the
            // hinges and latches a package needs.
            Assert.AreEqual(3, yard.HardwareStock.Lots.Count, "one yard lot per piece kind");
            var byKind = new Dictionary<LumberYardHardwareKind, LumberYardHardwareLot>();
            foreach (LumberYardHardwareLot lot in yard.HardwareStock.Lots)
            {
                Assert.IsFalse(byKind.ContainsKey(lot.HardwareKind), "one lot per kind");
                byKind[lot.HardwareKind] = lot;
            }
            Assert.AreEqual(1, byKind[LumberYardHardwareKind.DoorHandle].HardwareUnits);
            Assert.AreEqual(4, byKind[LumberYardHardwareKind.DoorHinge].HardwareUnits);
            Assert.AreEqual(2, byKind[LumberYardHardwareKind.IronLatch].HardwareUnits);
            foreach (LumberYardHardwareLot lot in yard.HardwareStock.Lots)
            {
                Assert.AreEqual("smith-1", lot.SourceBlacksmithBusinessId);
                Assert.AreEqual(42, lot.ForgedDayIndex);
                Assert.AreEqual(45, lot.UnitCostCents);
                Assert.AreEqual(string.Empty, lot.IronImportOrderId,
                    "the iron import order id is not recoverable from a forged piece — it stays empty ('when known'), never invented");
                Assert.AreEqual(job.SupplierNote, lot.SupplierNote, "every kind lot records the breakdown in its note");
                foreach (string lotId in materialLotIds)
                    Assert.IsTrue(lot.SupplierNote.Contains(lotId), $"note names material lot {lotId}");
            }
            Assert.AreEqual(3, job.YardLotIds.Count, "the job records every kind lot it created");
            Assert.AreEqual(byKind[LumberYardHardwareKind.DoorHandle].LotId, job.YardLotId,
                "YardLotId is the first kind lot in display-name order (door-handle)");
            Assert.AreEqual(job.YardLotIds[0], job.YardLotId);
            Assert.IsTrue(job.YardLotIds.Contains(byKind[LumberYardHardwareKind.DoorHinge].LotId));
            Assert.IsTrue(job.YardLotIds.Contains(byKind[LumberYardHardwareKind.IronLatch].LotId));
            Assert.AreEqual(10000 - 7 * 45, yardCash.BalanceCents, "the yard pays 7 pieces at 45c");
            Assert.AreEqual(7 * 45, smithCash.BalanceCents, "the smith is credited the same 315c");
        }

        // A yard that cannot pay keeps the hardware at the smithy: the
        // affordability gate runs before any custody moves.
        [Test]
        public void Delivery_YardCannotPay_NothingMoves()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);
            driver.YardCash = new TestBusinessCashPort("lumber-yard", 100);
            driver.SmithCash = new TestBusinessCashPort("smith-1", 0);

            BlacksmithToYardHardwareTransferJob job = driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string beginRefusal);
            Assert.IsNull(beginRefusal, beginRefusal);

            Assert.IsFalse(driver.RunDelivery(job.JobId, 42, out string runRefusal));
            Assert.IsNotNull(runRefusal);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Planned, job.State, "the job stays Planned for the next day's retry");
            Assert.AreEqual(7, smithy.Assets.Count, "no piece leaves the smithy");
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(100, driver.YardCash.BalanceCents, "no cash moves");
            Assert.AreEqual(0, driver.SmithCash.BalanceCents);
        }

        // A piece reserved after begin blocks the run but keeps the job
        // Planned: reservations are transient, the next day's drive
        // re-attempts.
        [Test]
        public void Delivery_ReservedPiece_RunRefusesJobStaysPlanned()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);
            driver.YardCash = new TestBusinessCashPort("lumber-yard", 10000);
            driver.SmithCash = new TestBusinessCashPort("smith-1", 0);

            BlacksmithToYardHardwareTransferJob job = driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string beginRefusal);
            Assert.IsNull(beginRefusal, beginRefusal);

            smithy.Assets[pieceIds[0]].ReservedBy = "someone-else";

            Assert.IsFalse(driver.RunDelivery(job.JobId, 42, out string runRefusal));
            Assert.IsNotNull(runRefusal);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Planned, job.State);
            Assert.AreEqual(7, smithy.Assets.Count);
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);

            smithy.Assets[pieceIds[0]].ReservedBy = string.Empty;
            Assert.IsTrue(driver.RunDelivery(job.JobId, 43, out runRefusal), runRefusal);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Transferred, job.State);
        }

        // A second begin for the same pieces is refused: one piece, one open job.
        [Test]
        public void Delivery_DoubleBegin_Refused()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);

            Assert.IsNotNull(driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string firstRefusal));
            Assert.IsNull(firstRefusal, firstRefusal);
            Assert.IsNull(driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string secondRefusal));
            Assert.IsNotNull(secondRefusal);
            Assert.AreEqual(1, driver.TransferJobCount);
        }

        // A zero unit cost is refused at begin: a delivery is a wholesale
        // purchase at a real price (the F6 defect).
        [Test]
        public void Delivery_Begin_ZeroCost_Refused()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);

            Assert.IsNull(driver.BeginDelivery(pieceIds, 0, 42, out string refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, driver.TransferJobCount);
        }

        // One yard lot carries one forged day: a mixed-day batch must be
        // split by the caller.
        [Test]
        public void Delivery_Begin_MixedForgedDays_Refused()
        {
            var smithy = NewSmithy();
            List<string> day42 = ForgePieces(smithy, new[] { BlacksmithRuntime.DoorHingeKind }, 42);
            List<string> day43 = ForgePieces(smithy, new[] { BlacksmithRuntime.DoorHingeKind }, 43);
            var mixed = new List<string>(day42);
            mixed.AddRange(day43);
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);

            Assert.IsNull(driver.BeginDelivery(
                mixed, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string refusal));
            Assert.IsNotNull(refusal);
        }

        // A yard that cannot take the lot (no id registry) fails the run
        // cleanly: the debit is refunded, the pieces go back to the
        // smithy, the job stays Planned.
        [Test]
        public void Delivery_NullRegistryYard_IntakeFailsDebitRefundedPiecesRestored()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var yard = new LumberYardShopRuntime("lumber-yard", null);
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            driver.YardCash = yardCash;
            driver.SmithCash = new TestBusinessCashPort("smith-1", 0);

            BlacksmithToYardHardwareTransferJob job = driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string beginRefusal);
            Assert.IsNull(beginRefusal, beginRefusal);

            Assert.IsFalse(driver.RunDelivery(job.JobId, 42, out string runRefusal));
            Assert.IsNotNull(runRefusal);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Planned, job.State);
            Assert.AreEqual(7, smithy.Assets.Count, "taken pieces are restored to smith stock");
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(10000, yardCash.BalanceCents, "the yard debit is refunded in full");
        }

        // Failing a job leaves the pieces at the smithy and frees them
        // for a fresh begin.
        [Test]
        public void Delivery_FailDelivery_PiecesStayAtSmithyFreshBeginAllowed()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);

            BlacksmithToYardHardwareTransferJob job = driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string beginRefusal);
            Assert.IsNull(beginRefusal, beginRefusal);

            Assert.IsTrue(driver.FailDelivery(job.JobId, 42, out string failRefusal), failRefusal);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Failed, job.State);
            Assert.AreEqual(7, smithy.Assets.Count);

            BlacksmithToYardHardwareTransferJob retry = driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 43, out string retryRefusal);
            Assert.IsNull(retryRefusal, retryRefusal);
            Assert.IsNotNull(retry);
        }

        // Save round trip: a Planned job persists and re-attaches; a
        // Transferred job does not persist (its yard lot already does).
        [Test]
        public void Delivery_SaveLoad_PlannedPersistsTransferredDoesNot()
        {
            var smithy = NewSmithy();
            List<string> pieceIds = ForgePieces(smithy, SevenPieces, 42);
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var driver = new BlacksmithToYardHardwareTransferDriver(smithy, yard, diagnostics);
            driver.YardCash = new TestBusinessCashPort("lumber-yard", 10000);
            driver.SmithCash = new TestBusinessCashPort("smith-1", 0);

            BlacksmithToYardHardwareTransferJob job = driver.BeginDelivery(
                pieceIds, BlacksmithToYardHardwareTransferDriver.WholesaleHardwareUnitCostCents, 42, out string beginRefusal);
            Assert.IsNull(beginRefusal, beginRefusal);

            var restored = new BlacksmithToYardHardwareTransferDriver(smithy, yard, new List<string>());
            restored.LoadFromSaveDto(driver.ToSaveDto());
            Assert.AreEqual(1, restored.TransferJobCount);
            BlacksmithToYardHardwareTransferJob restoredJob = restored.GetJob(job.JobId);
            Assert.IsNotNull(restoredJob);
            Assert.AreEqual(BlacksmithToYardHardwareTransferJobState.Planned, restoredJob.State);
            Assert.AreEqual(job.PieceBreakdown, restoredJob.PieceBreakdown);
            Assert.AreEqual(job.SupplierNote, restoredJob.SupplierNote);

            restored.YardCash = new TestBusinessCashPort("lumber-yard", 10000);
            restored.SmithCash = new TestBusinessCashPort("smith-1", 0);
            Assert.IsTrue(restored.RunDelivery(job.JobId, 42, out string runRefusal), runRefusal);
            Assert.AreEqual(0, restored.ToSaveDto().TransferJobLinks.Count,
                "a Transferred job is terminal and is not persisted");
        }

        // ---- F-D2-2: nails import order ----

        // The reorder policy: a covered stock at or below the threshold
        // fires, above it does not; outstanding units count toward cover.
        [Test]
        public void NailsPolicy_SignalFiresAtOrBelowThreshold_NoneAbove()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var policy = new LumberYardHardwareReorderPolicy();

            Assert.IsTrue(policy.Evaluate(yard, 0).ShouldOrder, "empty yard orders");
            Assert.AreEqual(0, policy.Evaluate(yard, 0).CoveredUnits);

            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 12, "IMP-ORD-SEED", "Pittsburgh ironworks, via railhead", 0, 30, diagnostics),
                string.Join("; ", diagnostics));
            Assert.IsTrue(policy.Evaluate(yard, 0).ShouldOrder, "12 on hand is at the threshold of 12");
            Assert.IsFalse(policy.Evaluate(yard, 1).ShouldOrder, "12 on hand + 1 outstanding is above the threshold");

            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 1, "IMP-ORD-SEED-2", "Pittsburgh ironworks, via railhead", 0, 30, diagnostics),
                string.Join("; ", diagnostics));
            Assert.IsFalse(policy.Evaluate(yard, 0).ShouldOrder, "13 on hand is above the threshold");
            Assert.AreEqual(13, policy.Evaluate(yard, 0).CoveredUnits);
        }

        // An empty yard orders 60 nails for cash at 30c, arriving day 14.
        [Test]
        public void Nails_DriveBelowThreshold_PlacesCashOrderAt30c()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNotNull(ImportCatalog.Get(LumberYardSupply.HardwareImportMaterialId),
                "the driver registers the nails import material at construction");

            string summary = driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count, summary);
            ImportOrder order = driver.Orders[0];
            Assert.AreEqual(LumberYardSupply.HardwareImportMaterialId, order.MaterialId);
            Assert.AreEqual(60, order.Units);
            Assert.AreEqual(30, order.PricePerUnitCents, "nails import at 30c per unit");
            Assert.AreEqual(14, order.ExpectedArrivalDayIndex, "14 days transit from Pittsburgh via the railhead");
            Assert.AreEqual("Pittsburgh ironworks, via railhead", order.OriginName);
            Assert.IsFalse(order.PaidOnCredit, "cash order");
            Assert.AreEqual(10000 - 60 * 30, yardCash.BalanceCents, "the yard pays 1800c from its own business cash");
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits, "nothing appears before arrival");
        }

        // The order is in transit for 14 days: nothing arrives early, and
        // no second order is placed while the first is outstanding.
        [Test]
        public void Nails_OrderInTransit_NoEarlyArrivalNoDoubleOrder()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            Assert.AreEqual(60, driver.OutstandingNailsUnits);

            driver.Drive(5);
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits, "nothing arrives before day 14");
            Assert.AreEqual(1, driver.Orders.Count, "no second order while the first is outstanding");

            driver.Drive(13);
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits, "day 13 is still too early");
            Assert.AreEqual(1, driver.Orders.Count);
        }

        // On day 14 the 60 nails land as one lot with the order id and
        // origin on it, and the covered stock (60) needs no new order.
        [Test]
        public void Nails_ArrivalDay_LandsAsProvenanceLot()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            ImportOrder order = driver.Orders[0];

            driver.Drive(14);
            Assert.AreEqual(60, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(1, yard.HardwareStock.Lots.Count);
            LumberYardHardwareLot lot = yard.HardwareStock.Lots[0];
            Assert.AreEqual(LumberYardHardwareKind.Nails, lot.HardwareKind);
            Assert.AreEqual(60, lot.HardwareUnits);
            Assert.AreEqual(order.OrderId, lot.ImportOrderId);
            Assert.AreEqual("Pittsburgh ironworks, via railhead", lot.OriginName);
            Assert.AreEqual(30, lot.UnitCostCents);
            Assert.AreEqual(ImportOrderStatus.Delivered, order.Status);
            Assert.AreEqual(0, driver.OutstandingNailsUnits);
            Assert.AreEqual(1, driver.Orders.Count, "60 on hand is above the threshold — no new order");
        }

        // A yard already stocked above the threshold orders nothing.
        [Test]
        public void Nails_StockAboveThreshold_NoOrder()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();
            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 20, "IMP-ORD-SEED", "Pittsburgh ironworks, via railhead", 0, 30, diagnostics),
                string.Join("; ", diagnostics));
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count);
            Assert.AreEqual(10000, yardCash.BalanceCents, "no cash moves when no order is placed");
        }

        // No cash and no credit: the order is refused and nothing is
        // created — no order, no lot, no payable.
        [Test]
        public void Nails_NoCashNoCredit_OrderRefusedNothingCreated()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var yardCash = new TestBusinessCashPort("lumber-yard", 0);
            var driver = NewImportDriver(yard, yardCash, null, new EntityIdRegistry());

            string summary = driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count, summary);
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(0, yardCash.BalanceCents);
        }

        // No cash but a liability ledger: the order is placed on honest
        // credit and the payable is recorded.
        [Test]
        public void Nails_NoCashWithCredit_OrderPlacedOnCreditPayableRecorded()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var yardCash = new TestBusinessCashPort("lumber-yard", 0);
            var liabilities = new BusinessLiabilityLedger();
            var ids = new EntityIdRegistry();
            var driver = NewImportDriver(yard, yardCash, liabilities, ids);

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            ImportOrder order = driver.Orders[0];
            Assert.IsTrue(order.PaidOnCredit);
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.LiabilityId), "the credit order names its payable");
            Assert.AreEqual(1, liabilities.All.Count, "one payable on the liability ledger");
            Assert.AreEqual(60 * 30, liabilities.All[0].PrincipalCents, "the payable is the full order price");
            Assert.AreEqual(0, yardCash.BalanceCents, "no cash moves on a credit order");
        }

        // The business-cash order path itself: unknown material, bad
        // units, and no cash with no credit all refuse with nothing
        // ordered.
        [Test]
        public void PlaceOrderForBusiness_Refusals()
        {
            var service = new ImportService();
            var diagnostics = new List<string>();
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);

            Assert.IsNull(service.PlaceOrderForBusiness(
                "lumber-yard", "Lumber Yard", "no-such-material", 10, 0,
                yardCash, null, new EntityIdRegistry(), diagnostics));
            Assert.IsNull(service.PlaceOrderForBusiness(
                "lumber-yard", "Lumber Yard", LumberYardSupply.HardwareImportMaterialId, 0, 0,
                yardCash, null, new EntityIdRegistry(), diagnostics));
            Assert.IsNull(service.PlaceOrderForBusiness(
                "lumber-yard", "Lumber Yard", LumberYardSupply.HardwareImportMaterialId, 10, 0,
                new TestBusinessCashPort("lumber-yard", 0), null, new EntityIdRegistry(), diagnostics));
            Assert.AreEqual(0, service.Orders.Count);
            Assert.AreEqual(10000, yardCash.BalanceCents);
        }

        // Save round trip: the order book persists, the next order number
        // is not reissued, and a due order still arrives after restore.
        [Test]
        public void NailsImport_SaveLoad_OrderBookPersistsNoReissueDueOrderArrives()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            string firstOrderId = driver.Orders[0].OrderId;

            var restored = new LumberYardHardwareImportDriver(yard, new List<string>());
            restored.YardCash = new TestBusinessCashPort("lumber-yard", 10000);
            restored.LiabilityLedger = new BusinessLiabilityLedger();
            restored.IdRegistry = new EntityIdRegistry();
            restored.LoadFromSaveDto(driver.ToSaveDto());
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual(firstOrderId, restored.Orders[0].OrderId);
            Assert.AreEqual(ImportOrderStatus.Ordered, restored.Orders[0].Status);

            ImportOrder second = restored.PlaceNailsOrder(60, 1);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(firstOrderId, second.OrderId, "the restored driver never reissues an order id");

            Assert.AreEqual(60, restored.ReceiveDueImports(14), "the restored order still arrives on day 14");
            Assert.AreEqual(60, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(ImportOrderStatus.Delivered, restored.Orders[0].Status);
        }
    }
}
