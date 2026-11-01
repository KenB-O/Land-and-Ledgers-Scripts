using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3A: linen/soap lots carry upstream provenance; dispense is FIFO;
    /// shortfalls refuse loudly and never conjure stock; laundry-return
    /// lots recycle physical stock with named source chains.
    /// </summary>
    [TestFixture]
    public sealed class HotelConsumableLotTests
    {
        private static HotelConsumableLot ImportLot(EntityIdRegistry registry, string name, int units, int day)
        {
            return new HotelConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = name,
                Units = units,
                AcquiredDayIndex = day,
                ImportOrderId = "IMP-1870-077",
                OriginName = "Off-map dry-goods wholesaler, via railhead",
                SupplierNote = "Eastern dry-goods house",
            };
        }

        [Test]
        public void ReceiveLot_RefusesOrphanLot_NamesChainOrBootstrap()
        {
            var stock = new HotelConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var orphan = new HotelConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = HotelConsumableSupply.LinenMaterialId,
                Units = 10,
                AcquiredDayIndex = 1,
            };
            Assert.IsNotNull(stock.ReceiveLot(orphan, diag), "orphan lot refused");
            Assert.AreEqual(0, stock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
        }

        [Test]
        public void ReceiveLot_RefusesLaundryReturnWithoutSources()
        {
            var stock = new HotelConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var badReturn = new HotelConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = HotelConsumableSupply.LinenMaterialId,
                Units = 5,
                AcquiredDayIndex = 3,
                IsLaundryReturn = true,
                LaundryReturnSourceChains = string.Empty,
            };
            Assert.IsNotNull(stock.ReceiveLot(badReturn, diag), "laundry return without source chains refused");
        }

        [Test]
        public void TryDispenseUnits_FifoOldestFirst()
        {
            var stock = new HotelConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            stock.ReceiveLot(ImportLot(registry, HotelConsumableSupply.LinenMaterialId, 5, 10), diag);
            stock.ReceiveLot(ImportLot(registry, HotelConsumableSupply.LinenMaterialId, 5, 2), diag);

            var lines = stock.TryDispenseUnits(HotelConsumableSupply.LinenMaterialId, 7, 11, diag);
            Assert.IsNotNull(lines);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(5, lines[0].UnitsTaken, "oldest lot (day 2) dispenses first");
            Assert.AreEqual(2, lines[1].UnitsTaken);
            Assert.AreEqual(3, stock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
        }

        [Test]
        public void TryDispenseUnits_Shortfall_ReturnsNull_NeverConjures()
        {
            var stock = new HotelConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            stock.ReceiveLot(ImportLot(registry, HotelConsumableSupply.LinenMaterialId, 2, 1), diag);

            var lines = stock.TryDispenseUnits(HotelConsumableSupply.LinenMaterialId, 5, 1, diag);
            Assert.IsNull(lines);
            Assert.AreEqual(2, stock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId), "stock untouched by a failed dispense");
        }

        [Test]
        public void Bootstrap_Endowment_AppliesOnce_ExplicitlyMarked()
        {
            var stock = new HotelConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            HotelConsumableBootstrap.ApplyBootstrapEndowment(stock, registry, 4, diag);
            Assert.AreEqual(HotelConsumableBootstrap.BootstrapLinenUnits, stock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
            Assert.AreEqual(HotelConsumableBootstrap.BootstrapSoapUnits, stock.UnitsOnHand(HotelConsumableSupply.SoapMaterialId));
            foreach (var lot in stock.Lots)
                Assert.IsTrue(lot.IsBootstrapEndowment, "every opening lot is explicitly flagged");
            Assert.IsTrue(diag.Exists(d => d.Contains("BOOTSTRAP")));
        }

        [Test]
        public void ReceiveImportArrival_NeedsNamedOrder()
        {
            var stock = new HotelConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            Assert.IsNotNull(HotelConsumableSupply.ReceiveImportArrival(stock,
                HotelConsumableSupply.LinenMaterialId, 10, string.Empty,
                "Off-map dry-goods wholesaler, via railhead", 6, registry, diag));
            Assert.IsNull(HotelConsumableSupply.ReceiveImportArrival(stock,
                HotelConsumableSupply.LinenMaterialId, 10, "IMP-1870-078",
                "Off-map dry-goods wholesaler, via railhead", 6, registry, diag));
            Assert.AreEqual(10, stock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
        }

        [Test]
        public void EnsureHotelImportables_RegistersBothMaterials()
        {
            var diag = new List<string>();
            HotelConsumableSupply.EnsureHotelImportables(diag);
            Assert.IsNotNull(LandLedgers.Economy.Trade.ImportCatalog.Get(HotelConsumableSupply.LinenMaterialId));
            Assert.IsNotNull(LandLedgers.Economy.Trade.ImportCatalog.Get(HotelConsumableSupply.SoapMaterialId));
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesLots()
        {
            var stock = new HotelConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            stock.ReceiveLot(ImportLot(registry, HotelConsumableSupply.LinenMaterialId, 6, 3), diag);

            var dto = stock.CaptureSaveDto();
            var reloaded = new HotelConsumableStock();
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(6, reloaded.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
            Assert.AreEqual(1, reloaded.Lots.Count);
        }
    }
}
