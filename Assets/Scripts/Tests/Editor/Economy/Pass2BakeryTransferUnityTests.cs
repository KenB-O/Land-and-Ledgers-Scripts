using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass 2 (task system): bakery staged-to-sellable transfer tests.
    /// UNITY-ONLY, AUTHORED, NOT RUN: BakeryShopRuntime and
    /// BakeryStagedProductionChain drag the full Generic/Equipment/
    /// BusinessDefinitions closure, which the small Roslyn harness does
    /// not compile. These tests must be run in Unity Test Runner
    /// (EditMode) by Codex / Unity AI. Until that run happens, report
    /// them as authored, not passed. Do NOT quote them as Roslyn results.
    /// </summary>
    [TestFixture]
    public sealed class Pass2BakeryTransferUnityTests
    {
        [Test]
        public void BakeryTransfer_MovesStagedToSellable_Once_AgingPreserved()
        {
            var chain = new BakeryStagedProductionChain();
            var shop = new BakeryShopRuntime("bakery");
            var diag = new List<string>();
            var registry = new EntityIdRegistry();

            chain.BreadShelf.Add(new BakeryStagedBreadLot
            {
                LotId = "bread-batch-1",
                BatchId = "staged-batch-0",
                ProductId = BakeryBreadCatalog.BreadLoafId,
                BakedDayIndex = 5,
                Units = 24,
                InputProvenance = new List<string> { "12x bakery-flour" },
            });

            var converted = Pass2BakeryStagedTransfer.Transfer(chain, shop, "bread-batch-1", 6, registry, diag);
            Assert.IsNotNull(converted);
            Assert.AreEqual(24, converted.UnitCount);
            Assert.AreEqual(5, converted.BakedDayIndex);
            Assert.AreEqual(BakeryGoodsCondition.DayOld, converted.Condition);
            Assert.AreEqual(0, chain.BreadShelf.Count);
            Assert.AreEqual(1, shop.BreadShelf.Count);

            Assert.IsNull(Pass2BakeryStagedTransfer.Transfer(chain, shop, "bread-batch-1", 6, registry, diag));
            var again = shop.ReceiveTransferredBreadLot(new BakeryStagedBreadLot
            {
                LotId = "bread-batch-1",
                BatchId = "staged-batch-0",
                ProductId = BakeryBreadCatalog.BreadLoafId,
                BakedDayIndex = 5,
                Units = 24,
            }, 6, registry, diag);
            Assert.IsNull(again);
            Assert.AreEqual(1, shop.BreadShelf.Count);

            var dto = shop.CaptureSaveDto();
            var restored = new BakeryShopRuntime("bakery");
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.BreadShelf.Count);
            Assert.AreEqual(1, restored.TransferredStagedLotIds.Count);
        }

        [Test]
        public void BakeryTransfer_StaleLotRefused_NotSold()
        {
            var chain = new BakeryStagedProductionChain();
            var shop = new BakeryShopRuntime("bakery");
            var diag = new List<string>();
            chain.BreadShelf.Add(new BakeryStagedBreadLot
            {
                LotId = "bread-batch-old",
                BatchId = "staged-batch-9",
                ProductId = BakeryBreadCatalog.BreadLoafId,
                BakedDayIndex = 5,
                Units = 24,
            });
            Assert.IsNull(Pass2BakeryStagedTransfer.Transfer(chain, shop, "bread-batch-old", 7, new EntityIdRegistry(), diag));
            Assert.AreEqual(0, shop.BreadShelf.Count);
            Assert.AreEqual(1, chain.BreadShelf.Count);
        }
    }
}
