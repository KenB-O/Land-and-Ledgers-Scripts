using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Trade;
using LandLedgers.Economy.Wheelwright;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// W6a: the wheelwright as a real business — wagon/wheel building from lumber
    /// lots + blacksmith ironwork with full upstream provenance, and wagon repair
    /// through the shared work-order authority. Canon §7.2: skill-dependent,
    /// queue-driven, priced labor + parts, never free.
    /// </summary>
    [TestFixture]
    public sealed class WheelwrightTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static EquipmentAsset BlacksmithPart(string makerId, string makerName, string kind = "wagon-part")
        {
            return new EquipmentAsset
            {
                AssetId = "EQ-smith-1-001",
                Kind = kind,
                DisplayName = "Wagon ironwork (tire/shoe)",
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = makerId,
                MadeByBusinessId = makerId,
                MadeByBusinessName = makerName,
                MadeDayIndex = 40,
                MaterialLotIds = new List<string> { "IMP-ORD-1-1" },
            };
        }

        private static WheelwrightRuntime NewShop(bool stationReady = true)
        {
            var shop = new WheelwrightRuntime("wheel-1", "Test Wheelwright", stationReady);
            // Stock: lumber lot (import catalog's honest stand-in) + blacksmith ironwork.
            shop.ReceiveLumberLot(new ImportLot
            {
                LotId = "IMP-TEST-L1", MaterialId = ImportCatalog.LumberId,
                MaterialName = "Lumber (boards)", Units = 200,
                OriginName = "Local sawmill, wagon delivery", OrderId = "IMP-ORD-9",
            }, new List<string>());
            Assert.IsNull(shop.ReceiveIronworkPart(
                BlacksmithPart("smith-1", "Test Smithy"), new List<string>()));
            Assert.IsNull(shop.ReceiveIronworkPart(
                BlacksmithPart("smith-1", "Test Smithy"), new List<string>()));
            Assert.IsNull(shop.ReceiveIronworkPart(
                BlacksmithPart("smith-1", "Test Smithy"), new List<string>()));
            return shop;
        }

        private static SkillService SkilledWheelwright(EntityId person, int minutes)
        {
            var skills = new SkillService();
            WheelwrightRuntime.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(person, WheelwrightRuntime.WheelwrightingSkillId, minutes);
            return skills;
        }

        [Test]
        public void Build_ProducesWagon_WithUpstreamProvenance()
        {
            var shop = NewShop();
            EntityId wright = Person(1);
            var skills = SkilledWheelwright(wright, 200); // journeyman
            var diagnostics = new List<string>();

            EquipmentAsset wagon = shop.BuildWagon("farm-wagon", wright, 42, skills, null, diagnostics);

            Assert.IsNotNull(wagon, string.Join("; ", diagnostics));
            Assert.AreEqual("farm-wagon", wagon.Kind);
            Assert.AreEqual("Farm wagon", wagon.DisplayName);
            Assert.AreEqual(1f, wagon.Condition01);
            Assert.AreEqual("wheel-1", wagon.MadeByBusinessId);
            Assert.AreEqual(42, wagon.MadeDayIndex);
            Assert.AreEqual(200 - 40, shop.TotalLumberOnHand(), "40 lumber consumed");
            Assert.AreEqual(3 - 2, shop.IronworkPartsOnHand, "2 ironwork parts consumed");
            CollectionAssert.Contains(wagon.MaterialLotIds, "IMP-TEST-L1", "lumber lot provenance");
            Assert.IsTrue(wagon.MaterialLotIds.Exists(id => id.StartsWith("blacksmith-part:")), "ironwork provenance");
            Assert.IsTrue(wagon.MaterialLotIds.Exists(id => id.Contains("smith-1")), "smith maker provenance");
        }

        [Test]
        public void Build_Refused_WithoutQualifiedWheelwright()
        {
            var shop = NewShop();
            EntityId novice = Person(2);
            var skills = SkilledWheelwright(novice, 10); // below journeyman
            var diagnostics = new List<string>();

            EquipmentAsset wagon = shop.BuildWagon("farm-wagon", novice, 42, skills, null, diagnostics);

            Assert.IsNull(wagon, "unskilled builder must not produce a wagon");
            Assert.AreEqual(200, shop.TotalLumberOnHand(), "no lumber consumed on refusal");
            Assert.AreEqual(3, shop.IronworkPartsOnHand, "no ironwork consumed on refusal");
        }

        [Test]
        public void Build_Refused_WithoutWheelStation()
        {
            var shop = NewShop(stationReady: false);
            EntityId wright = Person(3);
            var skills = SkilledWheelwright(wright, 200);
            var diagnostics = new List<string>();

            Assert.IsNull(shop.BuildWagon("farm-wagon", wright, 42, skills, null, diagnostics));
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("wheel station"));
        }

        [Test]
        public void Build_Refused_WithoutBlacksmithIronwork()
        {
            var shop = new WheelwrightRuntime("wheel-1", "Test Wheelwright", true);
            shop.ReceiveLumberLot(new ImportLot
            {
                LotId = "IMP-TEST-L2", MaterialId = ImportCatalog.LumberId,
                MaterialName = "Lumber (boards)", Units = 200,
                OriginName = "Local sawmill", OrderId = "IMP-ORD-9",
            }, new List<string>());
            EntityId wright = Person(4);
            var skills = SkilledWheelwright(wright, 200);
            var diagnostics = new List<string>();

            // No ironwork stock at all — the smith is upstream (FVS doctrine).
            Assert.IsNull(shop.BuildWagon("farm-wagon", wright, 42, skills, null, diagnostics));
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("ironwork"));
        }

        [Test]
        public void Build_Refused_WithoutLumber()
        {
            var shop = new WheelwrightRuntime("wheel-1", "Test Wheelwright", true);
            Assert.IsNull(shop.ReceiveIronworkPart(
                BlacksmithPart("smith-1", "Test Smithy"), new List<string>()));
            Assert.IsNull(shop.ReceiveIronworkPart(
                BlacksmithPart("smith-1", "Test Smithy"), new List<string>()));
            EntityId wright = Person(5);
            var skills = SkilledWheelwright(wright, 200);
            var diagnostics = new List<string>();

            Assert.IsNull(shop.BuildWagon("farm-wagon", wright, 42, skills, null, diagnostics));
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("lumber"));
        }

        [Test]
        public void Build_Refused_UnknownRecipe()
        {
            var shop = NewShop();
            EntityId wright = Person(6);
            var skills = SkilledWheelwright(wright, 200);
            var diagnostics = new List<string>();

            Assert.IsNull(shop.BuildWagon("steam-engine", wright, 42, skills, null, diagnostics));
        }

        [Test]
        public void IronworkPart_RequiresMakerProvenance()
        {
            var shop = new WheelwrightRuntime("wheel-1", "Test Wheelwright", true);
            var diagnostics = new List<string>();

            var noMaker = new EquipmentAsset { AssetId = "X", Kind = "wagon-part", MadeByBusinessId = "" };
            Assert.IsNotNull(shop.ReceiveIronworkPart(noMaker, diagnostics), "anonymous parts refused");

            var wrongKind = BlacksmithPart("smith-1", "Test Smithy", kind: "plow");
            Assert.IsNotNull(shop.ReceiveIronworkPart(wrongKind, diagnostics), "non-ironwork refused");

            Assert.AreEqual(0, shop.IronworkPartsOnHand);
        }

        [Test]
        public void WheelStation_DerivedFromComponents()
        {
            var shop = new WheelwrightRuntime("wheel-2", "Wheelwright Two", false);
            var diagnostics = new List<string>();

            var components = new List<EquipmentAsset>
            {
                new EquipmentAsset { AssetId = "jig-1", Kind = "wheel-jig", Condition01 = 0.9f },
                new EquipmentAsset { AssetId = "bench-1", Kind = "workbench", Condition01 = 0.9f },
                new EquipmentAsset { AssetId = "tools-1", Kind = "wheelwright-hand-tools", Condition01 = 0.9f },
            };
            Assert.IsNull(shop.EstablishWheelStationFromComponents(components, "shop-space-1", diagnostics));
            Assert.IsTrue(shop.WheelStationReady);
            Assert.IsTrue(shop.WheelStationDerivedFromComponents);

            var bare = new WheelwrightRuntime("wheel-3", "Wheelwright Three", false);
            string reason = bare.EstablishWheelStationFromComponents(
                new List<EquipmentAsset>
                {
                    new EquipmentAsset { AssetId = "bench-9", Kind = "workbench", Condition01 = 0.9f },
                }, "shop-space-1", diagnostics);
            Assert.IsNotNull(reason, "missing wheel jig must refuse the station");
            Assert.IsFalse(bare.WheelStationReady);
        }

        [Test]
        public void RepairFlow_Intake_Diagnose_Quote_Complete()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;

            RepairWorkOrder order = queue.Intake("Freight Co", "freight-1", "WG-freight-1-001",
                "Freight wagon", "wheel wobbles", WheelwrightRuntime.WheelwrightCapabilityCode,
                "freight-yard", RepairUrgency.Routine, 50, diagnostics);
            Assert.IsNull(queue.Diagnose(order.WorkOrderId, "two spokes cracked, tire loose",
                8, ImportCatalog.LumberId, 240));
            Assert.IsNull(queue.AgreeTerms(order.WorkOrderId, 620));

            var wagon = new EquipmentAsset { AssetId = "WG-freight-1-001", Kind = "freight-wagon", Condition01 = 0.4f };
            EntityId wright = Person(7);
            var skills = SkilledWheelwright(wright, 200);
            var customerLedger = new HouseholdLedger(11);
            customerLedger.RecordInflow(50, 2000, HouseholdIncomeSource.OtherDocumented,
                "test", "test funds", "test");

            string failure = shop.CompleteWagonRepair(order.WorkOrderId, wagon, wright, 51,
                skills, customerLedger, diagnostics);

            Assert.IsNull(failure, failure);
            Assert.AreEqual(RepairOrderStatus.Complete, order.Status);
            Assert.AreEqual(51, order.CompletedDayIndex);
            Assert.AreEqual(1f, wagon.Condition01, "repair restores the wagon");
            Assert.IsTrue(wagon.MaintenanceLog.Count > 0, "repair recorded in asset history");
            Assert.AreEqual(620, shop.RepairRevenueCents);
            Assert.AreEqual(2000 - 620, customerLedger.GetBalanceCents());
            Assert.AreEqual(200 - 8, shop.TotalLumberOnHand(), "8 lumber consumed by the repair");
        }

        [Test]
        public void Repair_Refused_WrongCapability()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;

            RepairWorkOrder order = queue.Intake("Farmer Jo", "farm-1", "EQ-farm-1-001",
                "Moldboard plow", "share chipped", "smithing", "farm-1", RepairUrgency.Routine, 50, diagnostics);
            Assert.IsNull(queue.Diagnose(order.WorkOrderId, "share chipped", 0, string.Empty, 60));
            Assert.IsNull(queue.AgreeTerms(order.WorkOrderId, 300));

            EntityId wright = Person(8);
            var skills = SkilledWheelwright(wright, 200);
            string failure = shop.CompleteWagonRepair(order.WorkOrderId,
                new EquipmentAsset { AssetId = "EQ-farm-1-001" }, wright, 51, skills, null, diagnostics);
            Assert.IsNotNull(failure, "smithing work does not belong at the wheelwright");
            Assert.AreEqual(RepairOrderStatus.Quoted, order.Status);
        }

        [Test]
        public void RepairQueue_Prioritizes_EmergencyOverRoutine()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;

            RepairWorkOrder routine = queue.Intake("A", "a-1", "WG-a", "Wagon A", "squeak",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Routine, 50, diagnostics);
            RepairWorkOrder emergency = queue.Intake("B", "b-1", "WG-b", "Wagon B", "axle cracked",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Emergency, 51, diagnostics);

            Assert.Less(emergency.QueuePosition, routine.QueuePosition, "emergency jumps the queue");
        }

        [Test]
        public void BuildSale_BooksRevenue_AndChargesBuyer()
        {
            var shop = NewShop();
            EntityId wright = Person(9);
            var skills = SkilledWheelwright(wright, 200);
            var diagnostics = new List<string>();

            EquipmentAsset wheel = shop.BuildWagon("wagon-wheel", wright, 42, skills, null, diagnostics);
            Assert.IsNotNull(wheel, string.Join("; ", diagnostics));

            var buyerLedger = new HouseholdLedger(12);
            buyerLedger.RecordInflow(42, 5000, HouseholdIncomeSource.OtherDocumented,
                "test", "test funds", "test");
            Assert.IsNull(shop.RecordBuildSale(wheel, 1500, 43, buyerLedger, diagnostics));
            Assert.AreEqual(1500, shop.BuildRevenueCents);
            Assert.AreEqual(5000 - 1500, buyerLedger.GetBalanceCents());
        }

        [Test]
        public void Save_RoundTrip_PreservesStock_Revenue_AndOpenOrders()
        {
            var shop = NewShop();
            EntityId wright = Person(10);
            var skills = SkilledWheelwright(wright, 200);
            var diagnostics = new List<string>();
            EquipmentAsset wagon = shop.BuildWagon("spring-wagon", wright, 42, skills, null, diagnostics);
            Assert.IsNotNull(wagon, string.Join("; ", diagnostics));

            var queue = shop.Repairs;
            RepairWorkOrder order = queue.Intake("C", "c-1", "WG-c", "Wagon C", "rim split",
                WheelwrightRuntime.WheelwrightCapabilityCode, "yard", RepairUrgency.Urgent, 52, diagnostics);

            WheelwrightRuntime.WheelwrightSaveDto dto = shop.ToSaveDto();
            var restored = new WheelwrightRuntime();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(shop.TotalLumberOnHand(), restored.TotalLumberOnHand());
            Assert.AreEqual(shop.IronworkPartsOnHand, restored.IronworkPartsOnHand);
            Assert.AreEqual(shop.BuildRevenueCents, restored.BuildRevenueCents);
            Assert.AreEqual(1, restored.BuiltAssets.Count);
            Assert.AreEqual(order.WorkOrderId, restored.Repairs.Orders[0].WorkOrderId, "open orders persist");
        }
    }
}
