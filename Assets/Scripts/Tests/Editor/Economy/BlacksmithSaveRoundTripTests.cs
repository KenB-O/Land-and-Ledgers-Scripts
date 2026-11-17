using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Trade;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// L1 (brief §18 X): the blacksmith runtime survives save/load. Until this
    /// package BlacksmithRuntime had no save surface at all — no DTO, no
    /// Capture, no section in any save file — so a Continue restored an empty
    /// smith: material, lot provenance, forged horseshoes/hardware, repair
    /// orders, quotes, pickups, liens, revenue, and the D1 quarter pools
    /// were all lost. These tests pin the runtime round-trip and the Pass 9
    /// coordinator wiring (bakery precedent).
    /// </summary>
    [TestFixture]
    public sealed class BlacksmithSaveRoundTripTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static BlacksmithRuntime NewSmithy(int iron = 100, int coal = 50, bool forgeReady = true)
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", forgeReady);
            var diag = new List<string>();
            if (iron > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-1", MaterialId = ImportCatalog.IronStockId,
                    MaterialName = "Iron stock", Units = iron,
                    OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-1",
                }, diag);
            }
            if (coal > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-2", MaterialId = ImportCatalog.ForgeCoalId,
                    MaterialName = "Forge coal", Units = coal,
                    OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-2",
                }, diag);
            }
            return smithy;
        }

        private static SkillService NoviceFarrier(EntityId smith)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            return skills;
        }

        private static SkillService SkilledSmith(EntityId smith)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(smith, BlacksmithRuntime.SmithingSkillId, 100000);
            return skills;
        }

        private static RepairWorkOrder OpenQuotedOrder(BlacksmithRuntime smithy, int day = 10)
        {
            var diag = new List<string>();
            RepairWorkOrder order = smithy.Repairs.Intake(
                "Test Customer", "biz-cust-1", "EQ-1", "Moldboard plow",
                "share cracked", "smithing", "loc-1", RepairUrgency.Routine, day, diag);
            smithy.Repairs.Diagnose(order.WorkOrderId, "cracked plowshare",
                4, ImportCatalog.IronStockId, 120, null);
            List<RepairQuotePartLine> lines = RepairPartsProvenance.SmithIronStockLines(
                smithy, order.MaterialUnitsNeeded, 45, diag, out string refusal);
            Assert.IsNull(refusal, "parts refusal: " + refusal);
            RepairQuoteOffer offer = smithy.RepairQuotes.IssueOffer(
                order, smithy.BusinessInstanceId, smithy.BusinessName, lines,
                RepairQuotePricing.DefaultShopRateCentsPerHour,
                RepairQuotePricing.DefaultPartsMarkup01, day, diag);
            Assert.IsNotNull(offer);
            string rejection = smithy.RepairQuotes.AcceptOffer(offer.OfferId, smithy.Repairs, day + 1, diag);
            Assert.IsNull(rejection, rejection);
            return order;
        }

        /// <summary>Populated smith: 1 forged shoe, 1 crafted plow, 1 open quoted order, 1 completed repair.</summary>
        private static BlacksmithRuntime PopulatedSmithy(out EquipmentAsset shoe, out EquipmentAsset plow)
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var farrierSkills = NoviceFarrier(smith);
            var smithSkills = SkilledSmith(smith);
            var diag = new List<string>();
            int minutes;

            shoe = smithy.ForgeHorseshoe(smith, 42, farrierSkills, diag, out minutes);
            Assert.IsNotNull(shoe, string.Join("; ", diag));
            plow = smithy.CraftEquipment("plow", smith, 42, smithSkills, diag);
            Assert.IsNotNull(plow, string.Join("; ", diag));

            OpenQuotedOrder(smithy);
            RepairWorkOrder done = OpenQuotedOrder(smithy);
            string blocked = smithy.CompleteRepair(done.WorkOrderId, null, smith, 12, smithSkills,
                null, diag, null, smithy.RepairLiens, smithy.RepairPickups);
            Assert.IsNull(blocked, blocked);
            return smithy;
        }

        private static RepairWorkOrder FindOrder(BlacksmithRuntime smithy, string workOrderId)
        {
            foreach (RepairWorkOrder o in smithy.Repairs.Orders)
                if (string.Equals(o.WorkOrderId, workOrderId, System.StringComparison.Ordinal)) return o;
            return null;
        }

        // X: the whole smith survives a save/load round-trip.
        [Test]
        public void RoundTrip_PreservesStockQuartersAssetsOrdersAndRevenue()
        {
            BlacksmithRuntime smithy = PopulatedSmithy(out EquipmentAsset shoe, out EquipmentAsset plow);
            RepairWorkOrder openBefore = null;
            RepairWorkOrder doneBefore = null;
            foreach (RepairWorkOrder o in smithy.Repairs.Orders)
            {
                if (o.Status == RepairOrderStatus.Quoted) openBefore = o;
                if (o.Status == RepairOrderStatus.Complete) doneBefore = o;
            }
            Assert.IsNotNull(openBefore, "one order stays open and quoted");
            Assert.IsNotNull(doneBefore, "one order is complete");

            BlacksmithRuntime.BlacksmithRuntimeSaveDto dto = smithy.CaptureSaveDto();

            var restored = new BlacksmithRuntime("smith-1", "Test Smithy", false);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual("smith-1", restored.BusinessInstanceId);
            Assert.AreEqual("Test Smithy", restored.BusinessName);
            Assert.IsTrue(restored.ForgeStationReady, "forge readiness survives");
            // Iron: 100 - 1 (shoe whole-unit draw) - 8 (plow) - 4 (completed repair) = 87.
            // Coal: 50 - 1 (shoe) - 2 (plow) = 47.
            Assert.AreEqual(87, restored.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(47, restored.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(87, restored.LotUnitsOnHand(ImportCatalog.IronStockId, "IMP-TEST-1"), "lot provenance survives");
            Assert.AreEqual(47, restored.LotUnitsOnHand(ImportCatalog.ForgeCoalId, "IMP-TEST-2"));
            // One shoe forged: 3 iron quarters + 3 coal quarters wait, with lot ids.
            Assert.AreEqual(3, restored.IronShoeQuartersOnHand, "cut-bar quarters survive");
            Assert.AreEqual(3, restored.CoalShoeQuartersOnHand, "fired-coal quarters survive");
            Assert.AreEqual(2, restored.Assets.Count, "shoe + plow are shop stock");
            Assert.IsTrue(restored.Assets.ContainsKey(shoe.AssetId), "the forged shoe keeps its id");
            Assert.AreEqual("horseshoe", restored.Assets[shoe.AssetId].Kind);
            Assert.AreEqual(0.64f, restored.Assets[shoe.AssetId].Condition01);
            Assert.IsTrue(restored.Assets[shoe.AssetId].MaterialLotIds.Contains("IMP-TEST-1"), "shoe keeps its iron lot");
            Assert.IsTrue(restored.Assets[shoe.AssetId].MaterialLotIds.Contains("IMP-TEST-2"), "shoe keeps its coal lot");
            Assert.IsTrue(restored.Assets.ContainsKey(plow.AssetId), "the crafted plow keeps its id");
            Assert.AreEqual("EQ-smith-1-001", plow.AssetId);
            Assert.AreEqual(2, restored.Repairs.Orders.Count);
            RepairWorkOrder openAfter = FindOrder(restored, openBefore.WorkOrderId);
            Assert.IsNotNull(openAfter);
            Assert.AreEqual(RepairOrderStatus.Quoted, openAfter.Status);
            Assert.IsTrue(openAfter.TermsAgreed);
            Assert.AreEqual(266, openAfter.AgreedPriceCents, "4 iron x 45c + 20% = 216c parts + 120 min = 50c labor");
            RepairWorkOrder doneAfter = FindOrder(restored, doneBefore.WorkOrderId);
            Assert.IsNotNull(doneAfter);
            Assert.AreEqual(RepairOrderStatus.Complete, doneAfter.Status);
            Assert.AreEqual(12, doneAfter.CompletedDayIndex);
            Assert.AreEqual(2, restored.RepairQuotes.ToSaveDto().Offers.Count, "both accepted offers survive");
            Assert.AreEqual(266, restored.RepairRevenueCents, "completed repair revenue survives");
            Assert.AreEqual(1, restored.RepairLiens.ToSaveDto().Claims.Count, "unpaid completion lien survives");
            Assert.AreEqual(1, restored.RepairPickups.ToSaveDto().Records.Count, "pickup notice survives");
            // The asset counter survives: the next crafted piece takes EQ-...-002, never reuses -001.
            EquipmentAsset next = restored.CraftEquipment("plow", Person(1), 43, SkilledSmith(Person(1)), new List<string>());
            Assert.IsNotNull(next);
            Assert.AreEqual("EQ-smith-1-002", next.AssetId);
        }

        // X: save/load is idempotent — a second round-trip changes nothing.
        [Test]
        public void RoundTrip_IsIdempotent()
        {
            BlacksmithRuntime smithy = PopulatedSmithy(out _, out _);
            BlacksmithRuntime.BlacksmithRuntimeSaveDto dto1 = smithy.CaptureSaveDto();

            var restored = new BlacksmithRuntime("smith-1", "Test Smithy", false);
            restored.LoadFromSaveDto(dto1);
            BlacksmithRuntime.BlacksmithRuntimeSaveDto dto2 = restored.CaptureSaveDto();

            Assert.AreEqual(dto1.BusinessInstanceId, dto2.BusinessInstanceId);
            Assert.AreEqual(dto1.MaterialOnHandCheck(), dto2.MaterialOnHandCheck());
            Assert.AreEqual(dto1.IronShoeQuarters, dto2.IronShoeQuarters);
            Assert.AreEqual(dto1.CoalShoeQuarters, dto2.CoalShoeQuarters);
            Assert.AreEqual(dto1.IronShoeQuarterLotId, dto2.IronShoeQuarterLotId);
            Assert.AreEqual(dto1.CoalShoeQuarterLotId, dto2.CoalShoeQuarterLotId);
            Assert.AreEqual(dto1.NextAssetNumber, dto2.NextAssetNumber);
            Assert.AreEqual(dto1.RepairRevenueCents, dto2.RepairRevenueCents);
            CollectionAssert.AreEquivalent(
                dto1.Assets.Select(a => a.AssetId).ToList(),
                dto2.Assets.Select(a => a.AssetId).ToList());
            CollectionAssert.AreEquivalent(
                dto1.RepairOrders.Orders.Select(o => o.WorkOrderId + ":" + o.Status).ToList(),
                dto2.RepairOrders.Orders.Select(o => o.WorkOrderId + ":" + o.Status).ToList());

            // Loading the same DTO twice does not duplicate anything.
            restored.LoadFromSaveDto(dto1);
            Assert.AreEqual(2, restored.Assets.Count, "second load does not duplicate shop stock");
            Assert.AreEqual(2, restored.Repairs.Orders.Count, "second load does not duplicate orders");
            Assert.AreEqual(87, restored.MaterialOnHand(ImportCatalog.IronStockId));
        }

        // A null DTO clears the runtime to empty (clear-then-load).
        [Test]
        public void LoadFromSaveDto_Null_ClearsToEmpty()
        {
            BlacksmithRuntime smithy = PopulatedSmithy(out _, out _);
            smithy.LoadFromSaveDto(null);

            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(0, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(0, smithy.CoalShoeQuartersOnHand);
            Assert.AreEqual(0, smithy.Assets.Count);
            Assert.AreEqual(0, smithy.Repairs.Orders.Count);
            Assert.AreEqual(0, smithy.RepairRevenueCents);
            Assert.IsTrue(!smithy.ForgeStationReady);
        }

        // X at the Pass 9 level: the coordinator captures the smith section and restores it.
        [Test]
        public void Coordinator_CaptureAndRestore_RoundTripsSmith()
        {
            BlacksmithRuntime smithy = PopulatedSmithy(out EquipmentAsset shoe, out _);
            var source = new Series2Pass9SaveState { BlacksmithRuntime = smithy };
            var diag = new List<string>();
            Series2Pass9SaveDto dto = source.Capture(diag);
            Assert.IsNotNull(dto.BlacksmithRuntime, "capture carries the smith section");
            Assert.IsTrue(!diag.Any(d => d.Contains("no live BlacksmithRuntime")), string.Join("; ", diag));

            var target = new Series2Pass9SaveState
            {
                BlacksmithRuntime = new BlacksmithRuntime("smith-1", "Test Smithy", false),
            };
            var restoreDiag = new List<string>();
            target.Restore(dto, null, restoreDiag);

            BlacksmithRuntime restored = target.BlacksmithRuntime;
            Assert.AreEqual(87, restored.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(3, restored.IronShoeQuartersOnHand);
            Assert.AreEqual(3, restored.CoalShoeQuartersOnHand);
            Assert.AreEqual(2, restored.Assets.Count);
            Assert.IsTrue(restored.Assets.ContainsKey(shoe.AssetId));
            Assert.AreEqual(266, restored.RepairRevenueCents);
        }

        // A section for another smith never overwrites the live one.
        [Test]
        public void Coordinator_Restore_IdentityMismatch_LeavesLiveSmithUntouched()
        {
            BlacksmithRuntime smithy = PopulatedSmithy(out _, out _);
            var source = new Series2Pass9SaveState { BlacksmithRuntime = smithy };
            Series2Pass9SaveDto dto = source.Capture(new List<string>());

            var live = new BlacksmithRuntime("smith-OTHER", "Other Smithy", true);
            live.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-OTHER-1", MaterialId = ImportCatalog.IronStockId,
                MaterialName = "Iron stock", Units = 5,
                OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-OTHER",
            }, new List<string>());
            var target = new Series2Pass9SaveState { BlacksmithRuntime = live };
            var restoreDiag = new List<string>();
            target.Restore(dto, null, restoreDiag);

            Assert.AreEqual(5, live.MaterialOnHand(ImportCatalog.IronStockId), "live smith not overwritten by another smith's section");
            Assert.AreEqual(0, live.Assets.Count);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("identity mismatch")), string.Join("; ", restoreDiag));
        }

        // Old save (no smith section) clears the live smith instead of leaving newer state in place.
        [Test]
        public void Coordinator_Restore_NoSection_ClearsLiveSmith()
        {
            BlacksmithRuntime smithy = PopulatedSmithy(out _, out _);
            var target = new Series2Pass9SaveState { BlacksmithRuntime = smithy };
            var restoreDiag = new List<string>();
            target.Restore(new Series2Pass9SaveDto(), null, restoreDiag);

            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(0, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(0, smithy.Assets.Count);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("no Blacksmith runtime section")), string.Join("; ", restoreDiag));
        }
    }

    internal static class BlacksmithSaveRoundTripDtoChecks
    {
        // Material stock as a sorted "id=units" string, so DTO comparisons do not depend on dictionary order.
        internal static string MaterialOnHandCheck(this BlacksmithRuntime.BlacksmithRuntimeSaveDto dto)
        {
            var parts = new List<string>();
            for (int i = 0; i < dto.MaterialIds.Count && i < dto.MaterialUnits.Count; i++)
                parts.Add(dto.MaterialIds[i] + "=" + dto.MaterialUnits[i]);
            parts.Sort();
            return string.Join(",", parts.ToArray());
        }
    }
}
