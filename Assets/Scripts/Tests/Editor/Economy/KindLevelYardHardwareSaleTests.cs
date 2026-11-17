using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D6 (F-D2-3): the yard hardware sale is kind-level, not bucket-level.
    /// A construction package's hardware requirement is fulfilled per kind
    /// (door hinge, iron latch, door handle, nails): the sale records one
    /// line per kind sold, the site stockpile stages per-kind lines, and
    /// the package consumes exactly the kinds it requires. A shortfall in
    /// one kind refuses that kind loudly — no silent substitution from
    /// another kind or from the mixed SimpleHardware bucket, and no charge
    /// for the unfilled kind. Kind lines are priced at the requirement's
    /// bucket price, so the money is unchanged by the split (settler roof:
    /// 9 hardware units at 45c = 405c, inside the 26,005c end-to-end pin).
    /// </summary>
    [TestFixture]
    public sealed class KindLevelYardHardwareSaleTests
    {
        private static LumberYardShopRuntime NewYard(out List<string> diag)
        {
            diag = new List<string>();
            return new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
        }

        private static ConstructionMaterialRequirement HardwareRequirement(int units,
            List<ConstructionHardwareKindLine> kindLines = null)
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-test-hardware",
                MaterialKind = ConstructionMaterialKind.Hardware,
                MaterialDisplayName = "door hardware",
                RequiredUnits = units,
                UnitLabel = "units",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
                HardwareKindLines = kindLines ?? new List<ConstructionHardwareKindLine>(),
            };
        }

        /// <summary>The settler roof's kind mix: 1 door + 1 window (D2 calibration).</summary>
        private static List<ConstructionHardwareKindLine> SettlerKindLines()
        {
            return new List<ConstructionHardwareKindLine>
            {
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHinge, Units = 3 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.IronLatch, Units = 2 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHandle, Units = 1 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.Nails, Units = 3 },
            };
        }

        private static List<LumberYardHardwareKindLine> YardKindLines(int hinges, int latches, int handles, int nails)
        {
            var lines = new List<LumberYardHardwareKindLine>();
            if (hinges > 0) lines.Add(new LumberYardHardwareKindLine { Kind = LumberYardHardwareKind.DoorHinge, Units = hinges });
            if (latches > 0) lines.Add(new LumberYardHardwareKindLine { Kind = LumberYardHardwareKind.IronLatch, Units = latches });
            if (handles > 0) lines.Add(new LumberYardHardwareKindLine { Kind = LumberYardHardwareKind.DoorHandle, Units = handles });
            if (nails > 0) lines.Add(new LumberYardHardwareKindLine { Kind = LumberYardHardwareKind.Nails, Units = nails });
            return lines;
        }

        /// <summary>Stocks the yard the settler roof needs: one lot per kind.</summary>
        private static void StockSettlerHardware(LumberYardShopRuntime yard, List<string> diag)
        {
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.DoorHinge, 3, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.IronLatch, 2, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.DoorHandle, 1, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 3, "IMP-ORD-N", "Pittsburgh ironworks, via railhead", 11, 30, diag), string.Join("; ", diag));
        }

        private static int UnitsOfKind(LumberYardSaleRecord record, LumberYardHardwareKind kind)
        {
            int total = 0;
            if (record == null) return 0;
            foreach (LumberYardMaterialSaleLine line in record.Lines)
            {
                if (line == null) continue;
                foreach (LumberYardHardwareDispenseLine dispense in line.HardwareLines)
                    if (dispense != null && dispense.HardwareKind == kind) total += dispense.UnitsTaken;
            }
            return total;
        }

        private static ConstructionMaterialSaleLine SaleLineFor(ConstructionMaterialSale sale, string displayName)
        {
            if (sale == null) return null;
            foreach (ConstructionMaterialSaleLine line in sale.Lines)
                if (line != null && string.Equals(line.DisplayName, displayName, StringComparison.Ordinal)) return line;
            return null;
        }

        // ---- The breakdown ----

        [Test]
        public void Breakdown_DerivedFromPairRule_SettlerNineUnits()
        {
            List<ConstructionHardwareKindLine> lines;
            Assert.IsNull(ConstructionHardwareKindBreakdown.TryBreakdown(HardwareRequirement(9), out lines));
            Assert.AreEqual(4, lines.Count, "one line per kind");
            Assert.AreEqual(3, lines[0].Units);
            Assert.AreEqual(LumberYardHardwareKindNames.DoorHinge, lines[0].DisplayName);
            Assert.AreEqual(2, lines[1].Units);
            Assert.AreEqual(LumberYardHardwareKindNames.IronLatch, lines[1].DisplayName);
            Assert.AreEqual(1, lines[2].Units);
            Assert.AreEqual(LumberYardHardwareKindNames.DoorHandle, lines[2].DisplayName);
            Assert.AreEqual(3, lines[3].Units);
            Assert.AreEqual(LumberYardHardwareKindNames.Nails, lines[3].DisplayName);
        }

        [Test]
        public void Breakdown_ExplicitLines_Accepted()
        {
            var kindLines = new List<ConstructionHardwareKindLine>
            {
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHinge, Units = 2 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.IronLatch, Units = 1 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHandle, Units = 1 },
            };
            List<ConstructionHardwareKindLine> lines;
            Assert.IsNull(ConstructionHardwareKindBreakdown.TryBreakdown(HardwareRequirement(4, kindLines), out lines));
            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual(2, lines[0].Units);
            Assert.AreEqual(1, lines[1].Units);
            Assert.AreEqual(1, lines[2].Units);
        }

        [Test]
        public void Breakdown_ExplicitLines_SumMismatch_Refuses()
        {
            var kindLines = new List<ConstructionHardwareKindLine>
            {
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHinge, Units = 2 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.IronLatch, Units = 2 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHandle, Units = 1 },
            };
            List<ConstructionHardwareKindLine> lines;
            string refusal = ConstructionHardwareKindBreakdown.TryBreakdown(HardwareRequirement(4, kindLines), out lines);
            Assert.IsNotNull(refusal, "kind lines summing to 5 for a 4-unit requirement refuse");
            Assert.IsTrue(refusal.Contains("5"), "the refusal names the mismatched sum: " + refusal);
        }

        [Test]
        public void Breakdown_NonMultipleBucket_Refuses()
        {
            List<ConstructionHardwareKindLine> lines;
            string refusal = ConstructionHardwareKindBreakdown.TryBreakdown(HardwareRequirement(4), out lines);
            Assert.IsNotNull(refusal, "4 units with no kind lines is not a whole number of door+window pairs");
            Assert.IsTrue(refusal.Contains("4"), "the refusal names the unresolvable total: " + refusal);
        }

        // ---- The yard sale ----

        [Test]
        public void YardSale_PerKind_LinesRecorded_TotalPreserved()
        {
            var yard = NewYard(out List<string> diag);
            StockSettlerHardware(yard, diag);

            var record = yard.TrySellToConstructionProject("project-test", 0, 0,
                YardKindLines(3, 2, 1, 3), 45, 10, diag);

            Assert.IsNotNull(record, "a covered kind-level sale must succeed: " + string.Join("; ", diag));
            Assert.AreEqual(9, record.UnitsSoldFor(ConstructionResourceKind.Nails));
            Assert.AreEqual(9 * 45, record.TotalCents, "kind lines price at the bucket price — the total is unchanged by the split");
            Assert.AreEqual(3, UnitsOfKind(record, LumberYardHardwareKind.DoorHinge));
            Assert.AreEqual(2, UnitsOfKind(record, LumberYardHardwareKind.IronLatch));
            Assert.AreEqual(1, UnitsOfKind(record, LumberYardHardwareKind.DoorHandle));
            Assert.AreEqual(3, UnitsOfKind(record, LumberYardHardwareKind.Nails));
            Assert.IsTrue(string.Join(" | ", record.AllProvenanceLines()).Contains("smith-1"),
                "every kind line carries its lot provenance");
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits, "all 9 units leave the yard");
            Assert.AreEqual(1, yard.SalesHistory.Count);
        }

        [Test]
        public void YardSale_KindShortfall_RefusesLoudly_NothingConsumed()
        {
            var yard = NewYard(out List<string> diag);
            // One hinge short of the settler mix.
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.DoorHinge, 2, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.IronLatch, 2, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.DoorHandle, 1, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 3, "IMP-ORD-N", "Pittsburgh ironworks, via railhead", 11, 30, diag), string.Join("; ", diag));

            var record = yard.TrySellToConstructionProject("project-test", 0, 0,
                YardKindLines(3, 2, 1, 3), 45, 10, diag);

            Assert.IsNull(record, "one hinge short refuses the whole sale");
            Assert.IsTrue(string.Join(" ", diag).Contains("door-hinge"),
                "the refusal names the short kind: " + string.Join("; ", diag));
            Assert.AreEqual(8, yard.HardwareStock.TotalHardwareUnits, "a refused sale consumes nothing");
            Assert.AreEqual(0, yard.SalesHistory.Count, "no sale is recorded for hardware that does not exist");
        }

        [Test]
        public void YardSale_WrongMix_NoSubstitution()
        {
            var yard = NewYard(out List<string> diag);
            // Six hinges and three nails, but no latch and no handle: the
            // bucket total (9) covers the settler roof, the kinds do not.
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.DoorHinge, 6, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 3, "IMP-ORD-N", "Pittsburgh ironworks, via railhead", 11, 30, diag), string.Join("; ", diag));

            var record = yard.TrySellToConstructionProject("project-test", 0, 0,
                YardKindLines(3, 2, 1, 3), 45, 10, diag);

            Assert.IsNull(record, "hinges never stand in for latches and handles");
            Assert.IsTrue(string.Join(" ", diag).Contains("iron-latch"),
                "the refusal names the missing kind: " + string.Join("; ", diag));
            Assert.AreEqual(9, yard.HardwareStock.TotalHardwareUnits, "a refused sale consumes nothing");
            Assert.AreEqual(0, yard.SalesHistory.Count);
        }

        [Test]
        public void YardSale_SimpleHardwareLot_CannotFillKind()
        {
            var yard = NewYard(out List<string> diag);
            // A mixed lot cannot prove it holds any kind, even though the
            // bucket total (9) covers the settler roof.
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.SimpleHardware, 6, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 3, "IMP-ORD-N", "Pittsburgh ironworks, via railhead", 11, 30, diag), string.Join("; ", diag));

            var record = yard.TrySellToConstructionProject("project-test", 0, 0,
                YardKindLines(3, 2, 1, 3), 45, 10, diag);

            Assert.IsNull(record, "an unitemized mixed lot proves no kind");
            Assert.AreEqual(9, yard.HardwareStock.TotalHardwareUnits, "a refused sale consumes nothing");
            Assert.AreEqual(0, yard.SalesHistory.Count);

            // The direct bucket entry is kept for direct yard callers: it
            // still sells the same 9 units from the mixed stock. The split
            // is deliberate — construction procurement goes through the
            // kind-level entry via the sale adapter.
            var bucketRecord = yard.TrySellToConstructionProject("project-test", 0, 0, 9, 45, 10, diag);
            Assert.IsNotNull(bucketRecord, "the bucket entry still serves direct yard callers");
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);
        }

        // ---- The adapter sale ----

        [Test]
        public void AdapterSale_PerKindLines_TotalPreserved()
        {
            var yard = NewYard(out List<string> diag);
            StockSettlerHardware(yard, diag);
            var prices = new LumberYardConstructionPricePort(yard);
            var adapter = new LumberYardConstructionSaleAdapter(yard);

            string refusal = adapter.TrySellToProject("lumber-yard", "project-test", "package-test",
                new List<ConstructionMaterialRequirement> { HardwareRequirement(9) },
                prices, 10, diag, out ConstructionMaterialSale sale);

            Assert.IsNull(refusal, "a covered kind-level sale must succeed: " + string.Join("; ", diag));
            Assert.IsNotNull(sale);
            Assert.AreEqual(4, sale.Lines.Count, "one sale line per kind sold");
            ConstructionMaterialSaleLine hinges = SaleLineFor(sale, LumberYardHardwareKindNames.DoorHinge);
            ConstructionMaterialSaleLine latches = SaleLineFor(sale, LumberYardHardwareKindNames.IronLatch);
            ConstructionMaterialSaleLine handles = SaleLineFor(sale, LumberYardHardwareKindNames.DoorHandle);
            ConstructionMaterialSaleLine nails = SaleLineFor(sale, LumberYardHardwareKindNames.Nails);
            Assert.IsNotNull(hinges, "the hinge line is recorded");
            Assert.IsNotNull(latches, "the latch line is recorded");
            Assert.IsNotNull(handles, "the handle line is recorded");
            Assert.IsNotNull(nails, "the nails line is recorded");
            Assert.AreEqual(3, hinges.Units);
            Assert.AreEqual(2, latches.Units);
            Assert.AreEqual(1, handles.Units);
            Assert.AreEqual(3, nails.Units);
            Assert.AreEqual(45, hinges.UnitPriceCents, "every kind line prices at the bucket price");
            Assert.AreEqual(45, nails.UnitPriceCents);
            Assert.AreEqual(9 * 45, sale.TotalCents, "the sale total is unchanged by the split");
            Assert.IsNull(SaleLineFor(sale, "door hardware"), "no bucket line remains in the sale");
            Assert.IsTrue(hinges.Provenance.Contains("smith-1"), "the hinge line carries its lot provenance");
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);
        }

        [Test]
        public void AdapterSale_MissingLatchKind_Refuses_NamesKind()
        {
            var yard = NewYard(out List<string> diag);
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.DoorHinge, 3, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.DoorHandle, 1, "smith-1", 10, "IMP-ORD-1", 10, 45, diag), string.Join("; ", diag));
            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 3, "IMP-ORD-N", "Pittsburgh ironworks, via railhead", 11, 30, diag), string.Join("; ", diag));
            var prices = new LumberYardConstructionPricePort(yard);
            var adapter = new LumberYardConstructionSaleAdapter(yard);

            string refusal = adapter.TrySellToProject("lumber-yard", "project-test", "package-test",
                new List<ConstructionMaterialRequirement> { HardwareRequirement(9) },
                prices, 10, diag, out ConstructionMaterialSale sale);

            Assert.IsNotNull(refusal, "a missing latch kind refuses the sale");
            Assert.IsNull(sale, "no sale is recorded for hardware that does not exist");
            Assert.IsTrue(string.Join(" ", diag).Contains("iron-latch"),
                "the refusal names the missing kind: " + string.Join("; ", diag));
            Assert.AreEqual(7, yard.HardwareStock.TotalHardwareUnits, "a refused sale consumes nothing");
            Assert.AreEqual(0, yard.SalesHistory.Count);
        }

        // ---- The site stockpile and the package ----

        private static ConstructionSiteStockpile StockpileFromSale(ConstructionMaterialSale sale, List<string> diag)
        {
            var stockpile = new ConstructionSiteStockpile();
            Assert.IsNull(stockpile.Receive(sale, diag), string.Join("; ", diag));
            return stockpile;
        }

        private static ConstructionMaterialSale KindSale(int hinges, int latches, int handles, int nails)
        {
            var sale = new ConstructionMaterialSale
            {
                SaleId = "sale-test",
                SupplierBusinessId = "lumber-yard",
                ProjectId = string.Empty,
                PackageId = "package-test",
                DayIndex = 10,
            };
            if (hinges > 0) sale.Lines.Add(new ConstructionMaterialSaleLine
                { DisplayName = LumberYardHardwareKindNames.DoorHinge, Units = hinges, UnitLabel = "units", UnitPriceCents = 45, SupplierBusinessId = "lumber-yard", Provenance = "yard lot test" });
            if (latches > 0) sale.Lines.Add(new ConstructionMaterialSaleLine
                { DisplayName = LumberYardHardwareKindNames.IronLatch, Units = latches, UnitLabel = "units", UnitPriceCents = 45, SupplierBusinessId = "lumber-yard", Provenance = "yard lot test" });
            if (handles > 0) sale.Lines.Add(new ConstructionMaterialSaleLine
                { DisplayName = LumberYardHardwareKindNames.DoorHandle, Units = handles, UnitLabel = "units", UnitPriceCents = 45, SupplierBusinessId = "lumber-yard", Provenance = "yard lot test" });
            if (nails > 0) sale.Lines.Add(new ConstructionMaterialSaleLine
                { DisplayName = LumberYardHardwareKindNames.Nails, Units = nails, UnitLabel = "units", UnitPriceCents = 45, SupplierBusinessId = "lumber-yard", Provenance = "yard lot test" });
            return sale;
        }

        [Test]
        public void Stockpile_ConsumesExactlyRequiredKinds()
        {
            var diag = new List<string>();
            var stockpile = StockpileFromSale(KindSale(3, 2, 1, 3), diag);
            var kindLines = new List<ConstructionHardwareKindLine>
            {
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHinge, Units = 2 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.IronLatch, Units = 1 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHandle, Units = 1 },
            };
            var package = new WorkPackage
            {
                PackageId = "wpkg-test",
                ProjectId = "project-test",
                Materials = new List<ConstructionMaterialRequirement> { HardwareRequirement(4, kindLines) },
            };

            Assert.IsTrue(stockpile.CoversAll(package.Materials), "2 hinges + 1 latch + 1 handle are on site");
            string refusal = stockpile.ConsumeForPackage(package, 10, diag, out List<ConsumedMaterialLine> consumed);
            Assert.IsNull(refusal, string.Join("; ", diag));
            Assert.AreEqual(1, consumed.Count, "one consumed line per requirement, under the requirement's own name");
            Assert.AreEqual("door hardware", consumed[0].DisplayName);
            Assert.AreEqual(4, consumed[0].Units);
            Assert.IsFalse(string.IsNullOrWhiteSpace(consumed[0].Provenance), "the consumed line carries provenance");
            Assert.AreEqual(1, stockpile.AvailableUnits(LumberYardHardwareKindNames.DoorHinge), "one hinge remains");
            Assert.AreEqual(1, stockpile.AvailableUnits(LumberYardHardwareKindNames.IronLatch), "one latch remains");
            Assert.AreEqual(0, stockpile.AvailableUnits(LumberYardHardwareKindNames.DoorHandle), "the handle is spent");
            Assert.AreEqual(3, stockpile.AvailableUnits(LumberYardHardwareKindNames.Nails), "nails untouched — this package needs none");
        }

        [Test]
        public void Stockpile_WrongMix_RefusesStaging_NothingConsumed()
        {
            var diag = new List<string>();
            var stockpile = StockpileFromSale(KindSale(4, 0, 1, 0), diag);
            var kindLines = new List<ConstructionHardwareKindLine>
            {
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHinge, Units = 2 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.IronLatch, Units = 1 },
                new ConstructionHardwareKindLine { DisplayName = LumberYardHardwareKindNames.DoorHandle, Units = 1 },
            };
            var package = new WorkPackage
            {
                PackageId = "wpkg-test",
                ProjectId = "project-test",
                Materials = new List<ConstructionMaterialRequirement> { HardwareRequirement(4, kindLines) },
            };

            Assert.IsFalse(stockpile.CoversAll(package.Materials), "four hinges cannot stand in for a latch");
            string refusal = stockpile.ConsumeForPackage(package, 10, diag, out List<ConsumedMaterialLine> consumed);
            Assert.IsNotNull(refusal, "staging refuses without the latch");
            Assert.AreEqual(0, consumed.Count, "a refused staging consumes nothing");
            Assert.AreEqual(4, stockpile.AvailableUnits(LumberYardHardwareKindNames.DoorHinge), "the hinges stay on site");
            Assert.AreEqual(1, stockpile.AvailableUnits(LumberYardHardwareKindNames.DoorHandle), "the handle stays on site");
        }

        [Test]
        public void Stockpile_BucketLine_DoesNotCoverKindNeed()
        {
            var diag = new List<string>();
            var sale = new ConstructionMaterialSale
            {
                SaleId = "sale-test",
                SupplierBusinessId = "lumber-yard",
                ProjectId = string.Empty,
                PackageId = "package-test",
                DayIndex = 10,
            };
            sale.Lines.Add(new ConstructionMaterialSaleLine
                { DisplayName = "door hardware", Units = 9, UnitLabel = "units", UnitPriceCents = 45, SupplierBusinessId = "lumber-yard", Provenance = "yard lot test" });
            var stockpile = StockpileFromSale(sale, diag);
            var package = new WorkPackage
            {
                PackageId = "wpkg-test",
                ProjectId = "project-test",
                Materials = new List<ConstructionMaterialRequirement> { HardwareRequirement(9) },
            };

            Assert.IsFalse(stockpile.CoversAll(package.Materials),
                "a bucket total on site never stands in for the kinds the package needs");
            string refusal = stockpile.ConsumeForPackage(package, 10, diag, out List<ConsumedMaterialLine> consumed);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, consumed.Count);
            Assert.AreEqual(9, stockpile.AvailableUnits("door hardware"), "the bucket line stays on site, unstaged");
        }
    }
}
