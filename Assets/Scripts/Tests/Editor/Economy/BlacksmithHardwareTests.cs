using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Trade;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D2 (brief §18 scenario V): basic construction hardware enters the
    /// legitimate supply chain. The smith forges one door hinge, iron latch
    /// or door handle at a time from bar stock — one quarter-iron plus one
    /// quarter-coal per piece, graded by capability exactly like a
    /// horseshoe (D1) — and the yard takes the forged pieces into its
    /// simple-hardware lots with the smith, the forged day and the iron
    /// order named. Nails are NOT forged here: the nail-forging rate is
    /// uncalibrated (E1), so nails arrive by legitimate import. No door
    /// hardware is conjured, and no smith recipe invents a price.
    /// </summary>
    [TestFixture]
    public sealed class BlacksmithHardwareTests
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

        private static SkillService SkilledSmith(EntityId smith, string skillId, int minutes)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(smith, skillId, minutes);
            return skills;
        }

        /// <summary>Novice smith capability: no practice recorded (capability 0.10).</summary>
        private static SkillService NoviceSmith(EntityId smith)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            return skills;
        }

        private static ConstructionMaterialRequirement HardwareRequirement()
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-test-hardware",
                MaterialKind = ConstructionMaterialKind.Hardware,
                MaterialDisplayName = "door hardware",
                RequiredUnits = 1,
                UnitLabel = "units",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            };
        }

        // One hinge is one actual item.
        [Test]
        public void Forge_OneHinge_CreatesOneActualItem()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;

            EquipmentAsset hinge = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes);

            Assert.IsNotNull(hinge, string.Join("; ", diagnostics));
            Assert.AreEqual("door-hinge", hinge.Kind);
            Assert.AreEqual("Door hinge", hinge.DisplayName);
            Assert.AreEqual(1, smithy.Assets.Count, "one hinge is one item in shop stock");
            Assert.AreEqual("smith-1", hinge.MadeByBusinessId);
            Assert.AreEqual(42, hinge.MadeDayIndex);
            // One quarter of each unit spent; the rest waits as cut quarters.
            Assert.AreEqual(100 - 1, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50 - 1, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(3, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(3, smithy.CoalShoeQuartersOnHand);
            Assert.AreEqual(28, minutes, "novice (capability 0.10) forges one hinge in 28 minutes");
            Assert.AreEqual(0.64f, hinge.Condition01);
        }

        // Each kind forges: hinge, latch and handle are distinct items.
        [Test]
        public void Forge_EachKind_CreatesItsOwnItem()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;

            EquipmentAsset hinge = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes);
            EquipmentAsset latch = smithy.ForgeHardware(BlacksmithRuntime.IronLatchKind, smith, 42, skills, diagnostics, out minutes);
            EquipmentAsset handle = smithy.ForgeHardware(BlacksmithRuntime.DoorHandleKind, smith, 42, skills, diagnostics, out minutes);

            Assert.IsNotNull(hinge, string.Join("; ", diagnostics));
            Assert.IsNotNull(latch, string.Join("; ", diagnostics));
            Assert.IsNotNull(handle, string.Join("; ", diagnostics));
            Assert.AreEqual("Door hinge", hinge.DisplayName);
            Assert.AreEqual("Iron latch", latch.DisplayName);
            Assert.AreEqual("Door handle", handle.DisplayName);
            Assert.AreEqual(3, smithy.Assets.Count, "three pieces, three items");
        }

        // Four pieces consume exactly one iron unit and one coal unit.
        [Test]
        public void Forge_FourPieces_ConsumeExactlyOneIronAndOneCoal()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            var ids = new HashSet<string>();

            string[] kinds =
            {
                BlacksmithRuntime.DoorHingeKind, BlacksmithRuntime.DoorHingeKind,
                BlacksmithRuntime.IronLatchKind, BlacksmithRuntime.DoorHandleKind,
            };
            foreach (string kind in kinds)
            {
                int minutes;
                EquipmentAsset piece = smithy.ForgeHardware(kind, smith, 42, skills, diagnostics, out minutes);
                Assert.IsNotNull(piece, string.Join("; ", diagnostics));
                Assert.IsTrue(ids.Add(piece.AssetId), "each piece is a distinct item");
            }

            Assert.AreEqual(4, smithy.Assets.Count, "four separate pieces, not one set");
            Assert.AreEqual(100 - 1, smithy.MaterialOnHand(ImportCatalog.IronStockId), "four pieces consume one iron unit");
            Assert.AreEqual(50 - 1, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "four pieces consume one coal unit");
            Assert.AreEqual(0, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(0, smithy.CoalShoeQuartersOnHand);
        }

        // Shoes and hardware draw from the same quarter pools: cut bar and
        // fired coal are not shoe-specific.
        [Test]
        public void Forge_ShoesAndHardware_ShareQuarterPools()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNotNull(smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes));
            Assert.IsNotNull(smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes));
            Assert.AreEqual(2, smithy.IronShoeQuartersOnHand, "two shoes leave two cut quarters");
            Assert.IsNotNull(smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes));
            Assert.IsNotNull(smithy.ForgeHardware(BlacksmithRuntime.IronLatchKind, smith, 42, skills, diagnostics, out minutes));

            Assert.AreEqual(4, smithy.Assets.Count, "two shoes and two hardware pieces, all distinct items");
            Assert.AreEqual(100 - 1, smithy.MaterialOnHand(ImportCatalog.IronStockId), "four quarter-forgings draw one iron unit between them");
            Assert.AreEqual(50 - 1, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(0, smithy.IronShoeQuartersOnHand);
            Assert.AreEqual(0, smithy.CoalShoeQuartersOnHand);
        }

        [Test]
        public void Forge_RefusesWithoutIron_ConsumesNothing()
        {
            var smithy = NewSmithy(iron: 0, coal: 50);
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes = 99;

            EquipmentAsset hinge = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes);

            Assert.IsNull(hinge);
            Assert.AreEqual(0, minutes, "no work done, no labour spent");
            Assert.AreEqual(50, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "no coal consumed on refusal");
            Assert.AreEqual(0, smithy.Assets.Count);
        }

        [Test]
        public void Forge_RefusesWithoutCoal_ConsumesNothing()
        {
            var smithy = NewSmithy(iron: 100, coal: 0);
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId), "no iron consumed on refusal");
            Assert.AreEqual(0, smithy.Assets.Count);
        }

        [Test]
        public void Forge_RefusesWithoutForgeStation()
        {
            var smithy = NewSmithy(forgeReady: false);
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 900);
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
        }

        [Test]
        public void Forge_RefusesWithoutSkillService()
        {
            var smithy = NewSmithy();
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, Person(1), 42, null, diagnostics, out minutes));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
        }

        // Nails have no smith recipe: the nail-forging rate is uncalibrated
        // (E1), so no forged-nails recipe may be invented here.
        [Test]
        public void Forge_NailsHaveNoSmithRecipe()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 900);
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHardware("nails", smith, 42, skills, diagnostics, out minutes));
            Assert.IsNull(BlacksmithRuntime.DefaultRecipes().Find(r => r.EquipmentKind == "nails"),
                "no nails recipe exists to be forged from");
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(0, smithy.Assets.Count);
        }

        // A whole-unit recipe is not hardware, and an unknown kind is not hardware.
        [Test]
        public void Forge_NonHardwareKind_Refuses()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 900);
            var diagnostics = new List<string>();
            int minutes;

            Assert.IsNull(smithy.ForgeHardware("plow", smith, 42, skills, diagnostics, out minutes));
            Assert.IsNull(smithy.ForgeHardware("hinge-set", smith, 42, skills, diagnostics, out minutes));
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(0, smithy.Assets.Count);
        }

        // Labour and skill affect production — novice is slow and never perfect.
        [Test]
        public void Forge_SkillAffectsMinutesAndQuality_Novice()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith); // capability 0.10
            var diagnostics = new List<string>();
            int minutes;

            EquipmentAsset hinge = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes);

            Assert.IsNotNull(hinge);
            Assert.AreEqual(28, minutes);
            Assert.AreEqual(0.64f, hinge.Condition01);
            Assert.IsTrue(hinge.Condition01 < 1f, "a low-skill attempt never produces a guaranteed perfect piece");
        }

        // Mid capability is faster and better; mastery is fastest and perfect.
        [Test]
        public void Forge_SkillAffectsMinutesAndQuality_MidAndMaster()
        {
            var smithy = NewSmithy();
            var diagnostics = new List<string>();

            EntityId mid = Person(2);
            var midSkills = SkilledSmith(mid, BlacksmithRuntime.SmithingSkillId, 400); // capability 4/9
            int midMinutes;
            EquipmentAsset midHinge = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, mid, 42, midSkills, diagnostics, out midMinutes);
            Assert.IsNotNull(midHinge, string.Join("; ", diagnostics));
            Assert.AreEqual(21, midMinutes);
            Assert.AreEqual(0.78f, midHinge.Condition01);

            EntityId master = Person(3);
            var masterSkills = SkilledSmith(master, BlacksmithRuntime.SmithingSkillId, 900); // capability 1.0
            int masterMinutes;
            EquipmentAsset masterHinge = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, master, 42, masterSkills, diagnostics, out masterMinutes);
            Assert.IsNotNull(masterHinge, string.Join("; ", diagnostics));
            Assert.AreEqual(10, masterMinutes);
            Assert.AreEqual(1f, masterHinge.Condition01);
        }

        // The generic whole-unit path cannot mint quarter-priced hardware.
        [Test]
        public void CraftEquipment_RefusesHardware_FractionalRecipe()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.SmithingSkillId, 900); // journeyman-level
            var diagnostics = new List<string>();

            EquipmentAsset hinge = smithy.CraftEquipment("door-hinge", smith, 42, skills, diagnostics);

            Assert.IsNull(hinge, "quarter-priced recipes are forged one at a time through ForgeHardware");
            Assert.AreEqual(100, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(50, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("ForgeHardware")));
        }

        // The D1 refusal still names ForgeHorseshoe for shoes.
        [Test]
        public void CraftEquipment_RefusesHorseshoe_StillNamesForgeHorseshoe()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = SkilledSmith(smith, BlacksmithRuntime.FarrierSkillId, 900);
            var diagnostics = new List<string>();

            Assert.IsNull(smithy.CraftEquipment("horseshoe", smith, 42, skills, diagnostics));
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("ForgeHorseshoe")));
        }

        [Test]
        public void TakeHardware_ReleasesOnePiece()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;
            EquipmentAsset first = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes);
            EquipmentAsset second = smithy.ForgeHardware(BlacksmithRuntime.IronLatchKind, smith, 42, skills, diagnostics, out minutes);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);

            EquipmentAsset taken = smithy.TakeHardware(first.AssetId, diagnostics);

            Assert.IsNotNull(taken);
            Assert.AreEqual(first.AssetId, taken.AssetId);
            Assert.AreEqual(1, smithy.Assets.Count);
            Assert.IsTrue(smithy.Assets.ContainsKey(second.AssetId), "the other piece stays in stock");
            Assert.IsNull(smithy.TakeHardware(first.AssetId, diagnostics), "a released piece cannot be taken twice");
        }

        [Test]
        public void TakeHardware_RefusesUnknownId_NonHardware_Reserved()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;
            EquipmentAsset hinge = smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, 42, skills, diagnostics, out minutes);
            Assert.IsNotNull(hinge);

            Assert.IsNull(smithy.TakeHardware("no-such-asset", diagnostics));
            Assert.AreEqual(1, smithy.Assets.Count);

            // A shoe is not hardware.
            EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);
            Assert.IsNotNull(shoe);
            Assert.IsNull(smithy.TakeHardware(shoe.AssetId, diagnostics));
            Assert.IsTrue(smithy.Assets.ContainsKey(shoe.AssetId));

            // A reserved hinge cannot be taken out from under its reservation.
            Assert.IsNull(hinge.Reserve("yard-runner-1", "delivery"));
            Assert.IsNull(smithy.TakeHardware(hinge.AssetId, diagnostics));
            Assert.IsTrue(smithy.Assets.ContainsKey(hinge.AssetId));
        }

        // Scenario V core: forged hardware enters the yard's legitimate
        // supply chain — the smith's pieces become one named yard lot with
        // the smith, the forged day and the iron order recorded, priced
        // from the lot's real unit cost. Nothing is duplicated: the pieces
        // leave the smith's stock when they enter the yard's.
        [Test]
        public void YardDelivery_ForgedHardwareEntersLegitimateSupply()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = NoviceSmith(smith);
            var diagnostics = new List<string>();
            int minutes;
            var pieces = new List<EquipmentAsset>();
            string[] kinds =
            {
                BlacksmithRuntime.DoorHingeKind, BlacksmithRuntime.DoorHingeKind,
                BlacksmithRuntime.DoorHingeKind, BlacksmithRuntime.DoorHingeKind,
                BlacksmithRuntime.IronLatchKind, BlacksmithRuntime.IronLatchKind,
                BlacksmithRuntime.DoorHandleKind, BlacksmithRuntime.DoorHandleKind,
            };
            foreach (string kind in kinds)
            {
                EquipmentAsset piece = smithy.ForgeHardware(kind, smith, 42, skills, diagnostics, out minutes);
                Assert.IsNotNull(piece, string.Join("; ", diagnostics));
                pieces.Add(piece);
            }
            Assert.AreEqual(8, smithy.Assets.Count);

            // The delivery hands each piece over: release from the smith,
            // then one yard lot for the whole delivery.
            foreach (EquipmentAsset piece in pieces)
                Assert.IsNotNull(smithy.TakeHardware(piece.AssetId, diagnostics), string.Join("; ", diagnostics));
            Assert.AreEqual(0, smithy.Assets.Count, "every delivered piece leaves the smith's stock");

            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.SimpleHardware, 8, "smith-1", 42, "IMP-ORD-1", 42, 45, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(8, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(1, yard.HardwareStock.Lots.Count);
            LumberYardHardwareLot lot = yard.HardwareStock.Lots[0];
            Assert.AreEqual(LumberYardHardwareKind.SimpleHardware, lot.HardwareKind);
            Assert.AreEqual("smith-1", lot.SourceBlacksmithBusinessId);
            Assert.AreEqual(42, lot.ForgedDayIndex);
            Assert.AreEqual("IMP-ORD-1", lot.IronImportOrderId);
            Assert.AreEqual(45, lot.UnitCostCents);
            // The delivery records which pieces the lot holds; the lot
            // itself is bucket-level (SimpleHardware), so the breakdown
            // rides the supplier note until kind-level lots exist.
            lot.SupplierNote = "4 door-hinge + 2 iron-latch + 2 door-handle, forged by smith-1 day 42";
            Assert.IsTrue(lot.ProvenanceChain().Contains("smith-1"));

            var pricePort = new LumberYardConstructionPricePort(yard);
            Assert.AreEqual(45, pricePort.UnitPriceCents(HardwareRequirement()),
                "hardware prices from the real lot cost, never an invented price");
        }

        // The yard refuses anonymous smith hardware: the forging smith must be named.
        [Test]
        public void YardDelivery_AnonymousSmith_Refused()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();

            Assert.IsNotNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.SimpleHardware, 4, string.Empty, 42, "IMP-ORD-1", 42, 45, diagnostics));
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits, "nothing enters on a refused delivery");
        }

        // Nails arrive by legitimate import, not from the forge: the import
        // order, origin and cost are named, and the price port reads the
        // lot's real cost.
        [Test]
        public void YardDelivery_ImportedNailsEnterLegitimateSupply()
        {
            var yard = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            var diagnostics = new List<string>();

            Assert.IsNull(yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 10, "IMP-ORD-N", "Pittsburgh ironworks, via railhead", 42, 30, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(10, yard.HardwareStock.TotalHardwareUnits);
            LumberYardHardwareLot lot = yard.HardwareStock.Lots[0];
            Assert.AreEqual(LumberYardHardwareKind.Nails, lot.HardwareKind);
            Assert.AreEqual("IMP-ORD-N", lot.ImportOrderId);
            Assert.AreEqual("Pittsburgh ironworks, via railhead", lot.OriginName);
            Assert.AreEqual(30, lot.UnitCostCents);

            var pricePort = new LumberYardConstructionPricePort(yard);
            Assert.AreEqual(30, pricePort.UnitPriceCents(HardwareRequirement()),
                "nails price from the import lot's real cost");
        }
    }
}
