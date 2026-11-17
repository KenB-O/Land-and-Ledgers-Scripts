using System.Collections.Generic;
using LandLedgers.Economy.AnimalServices;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D1 (brief §18 I-J): farrier fitting is a separate service from
    /// forging. A service order covers one, two, or a complete four-hoof
    /// shoeing; replacing one shoe never requires a four-shoe batch; a
    /// horse is never assumed to need all four shoes. Each hoof is
    /// inspected, trimmed or prepared, fitted with a real shoe from
    /// stock, secured, and fit-checked, and the work is charged.
    /// Plus end-to-end scenario 3: acquire material and fuel, forge one
    /// shoe, it enters inventory, fitting consumes that exact shoe with
    /// real labour.
    /// </summary>
    [TestFixture]
    public sealed class HorseshoeFittingTests
    {
        private const int PricePerShoeCents = 25;
        private const int LaborCentsPerHoof = 100;
        private const string ShoeSource = "blacksmith: Tom's Forge";

        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);
        private static EntityId Horse(int n) => EntityId.For(EntityKind.Animal, n);

        private static BlacksmithRuntime NewSmithy(int iron = 100, int coal = 50)
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", true);
            smithy.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-TEST-1", MaterialId = ImportCatalog.IronStockId,
                MaterialName = "Iron stock", Units = iron,
                OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-1",
            }, new List<string>());
            smithy.ReceiveMaterial(new ImportLot
            {
                LotId = "IMP-TEST-2", MaterialId = ImportCatalog.ForgeCoalId,
                MaterialName = "Forge coal", Units = coal,
                OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-2",
            }, new List<string>());
            return smithy;
        }

        private static SkillService FarrierSkills(EntityId farrier, int practiceMinutes)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            if (practiceMinutes > 0) skills.GrantPractice(farrier, BlacksmithRuntime.FarrierSkillId, practiceMinutes);
            return skills;
        }

        /// <summary>Forge n shoes and release them into the farrier's shoe stock.</summary>
        private static List<EquipmentAsset> ForgeShoes(BlacksmithRuntime smithy, EntityId smith, SkillService skills, int count)
        {
            var stock = new List<EquipmentAsset>();
            var diagnostics = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int minutes;
                EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);
                Assert.IsNotNull(shoe, string.Join("; ", diagnostics));
                EquipmentAsset taken = smithy.TakeHorseshoe(shoe.AssetId, diagnostics);
                Assert.IsNotNull(taken, string.Join("; ", diagnostics));
                stock.Add(taken);
            }
            return stock;
        }

        private static List<HoofNeed> FourHooves()
        {
            return new List<HoofNeed>
            {
                new HoofNeed("front-left", 0.80f, true, true, "worn shoe"),
                new HoofNeed("front-right", 0.75f, true, true, "worn shoe"),
                new HoofNeed("rear-left", 0.85f, true, false, "worn shoe"),
                new HoofNeed("rear-right", 0.70f, true, true, "worn shoe"),
            };
        }

        // I: four separate shoes assemble into a four-hoof service order.
        [Test]
        public void Fit_FourHoofOrder_ConsumesFourSeparateShoes()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0); // novice farrier, capability 0.10
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 4);
            var farrier = new FarrierService();
            var diagnostics = new List<string>();

            FarrierFittingResult result = farrier.FitShoes(
                Horse(301), FourHooves(), stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsNull(result.Failure, string.Join("; ", diagnostics));
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(4, result.Hooves.Count);
            Assert.AreEqual(4, result.ShoesConsumed);
            Assert.AreEqual(0, stock.Count, "every fitted shoe leaves the farrier's stock");
            Assert.AreEqual(4 * 21, result.LaborMinutes, "novice fits each hoof in 21 minutes");
            Assert.AreEqual(4 * PricePerShoeCents, result.MaterialCents);
            Assert.AreEqual(4 * LaborCentsPerHoof, result.LaborCents);
            Assert.AreEqual(4 * PricePerShoeCents + 4 * LaborCentsPerHoof, result.TotalCents);

            var shoeIds = new HashSet<string>();
            foreach (HoofFittingRecord record in result.Hooves)
            {
                Assert.IsTrue(record.Inspected, "every hoof is inspected");
                Assert.IsTrue(record.ShoeShaped, "every shoe is shaped to its hoof");
                Assert.IsTrue(record.Secured, "every shoe is secured");
                Assert.IsTrue(record.FitChecked, "every fit is checked");
                Assert.IsTrue(!string.IsNullOrWhiteSpace(record.ShoeAssetId));
                Assert.IsTrue(shoeIds.Add(record.ShoeAssetId), "each fitted shoe is a distinct item");
                Assert.AreEqual(0.64f, record.FitQuality01, "novice fit quality");
            }
        }

        // J: one-shoe replacement needs no batch.
        [Test]
        public void Fit_OneShoeReplacement_NeedsNoBatch()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0);
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 1);
            var farrier = new FarrierService();
            var diagnostics = new List<string>();
            var hooves = new List<HoofNeed>
            {
                new HoofNeed("front-left", 0.60f, true, true, "thrown shoe"),
                new HoofNeed("front-right", 0.90f, false, false, "sound"),
                new HoofNeed("rear-left", 0.90f, false, false, "sound"),
                new HoofNeed("rear-right", 0.90f, false, false, "sound"),
            };

            FarrierFittingResult result = farrier.FitShoes(
                Horse(302), hooves, stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsNull(result.Failure, string.Join("; ", diagnostics));
            Assert.AreEqual(1, result.Hooves.Count, "only the hoof that needs work is worked on");
            Assert.AreEqual("front-left", result.Hooves[0].HoofId);
            Assert.AreEqual(1, result.ShoesConsumed);
            Assert.AreEqual(0, stock.Count);
            Assert.AreEqual(21, result.LaborMinutes);
            Assert.AreEqual(PricePerShoeCents + LaborCentsPerHoof, result.TotalCents);
        }

        [Test]
        public void Fit_TwoHoofOrder_ConsumesTwoShoes()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0);
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 2);
            var farrier = new FarrierService();
            var diagnostics = new List<string>();
            var hooves = new List<HoofNeed>
            {
                new HoofNeed("front-left", 0.65f, true, false, "worn"),
                new HoofNeed("front-right", 0.65f, true, false, "worn"),
            };

            FarrierFittingResult result = farrier.FitShoes(
                Horse(303), hooves, stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsNull(result.Failure, string.Join("; ", diagnostics));
            Assert.AreEqual(2, result.Hooves.Count);
            Assert.AreEqual(2, result.ShoesConsumed);
            Assert.AreEqual(0, stock.Count);
        }

        [Test]
        public void Fit_RefusesWhenShoesShort_ConsumesNothing()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0);
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 1);
            var farrier = new FarrierService();
            var diagnostics = new List<string>();

            FarrierFittingResult result = farrier.FitShoes(
                Horse(304), FourHooves(), stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsNotNull(result.Failure, "short stock must refuse, not fake a shoeing");
            Assert.AreEqual(1, stock.Count, "the one real shoe stays in stock");
            Assert.AreEqual(0, result.Hooves.Count);
            Assert.AreEqual(0, result.ShoesConsumed);
            Assert.AreEqual(0, result.LaborMinutes);
        }

        [Test]
        public void Fit_TrimOnlyHoof_NoShoeConsumed()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0);
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 1);
            var farrier = new FarrierService();
            var diagnostics = new List<string>();
            var hooves = new List<HoofNeed>
            {
                new HoofNeed("rear-left", 0.70f, false, true, "overgrown hoof, shoe sound"),
            };

            FarrierFittingResult result = farrier.FitShoes(
                Horse(305), hooves, stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsNull(result.Failure, string.Join("; ", diagnostics));
            Assert.AreEqual(1, result.Hooves.Count);
            Assert.IsTrue(result.Hooves[0].Trimmed);
            Assert.AreEqual(0, result.ShoesConsumed);
            Assert.AreEqual(1, stock.Count, "the shoe was not needed and not consumed");
            Assert.AreEqual(11, result.LaborMinutes, "trim-only work is about half a fitting");
            Assert.AreEqual(0, result.MaterialCents);
            Assert.AreEqual(LaborCentsPerHoof, result.LaborCents);
        }

        [Test]
        public void Fit_SelectsBestShoeFirst()
        {
            var smithy = NewSmithy();
            EntityId novice = Person(1);
            var noviceSkills = FarrierSkills(novice, 0);
            EntityId master = Person(2);
            var masterSkills = FarrierSkills(master, 900);
            List<EquipmentAsset> stock = ForgeShoes(smithy, novice, noviceSkills, 1); // 0.64 shoe
            stock.AddRange(ForgeShoes(smithy, master, masterSkills, 1)); // 1.00 shoe
            EquipmentAsset best = stock[1];
            var farrier = new FarrierService();
            var diagnostics = new List<string>();

            FarrierFittingResult result = farrier.FitShoes(
                Horse(306), new List<HoofNeed> { new HoofNeed("front-left", 0.80f, true, false, "worn") },
                stock, ShoeSource, PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), noviceSkills, diagnostics);

            Assert.IsNull(result.Failure, string.Join("; ", diagnostics));
            Assert.AreEqual(best.AssetId, result.Hooves[0].ShoeAssetId, "the best usable shoe is fitted first");
            Assert.AreEqual(1, stock.Count);
            Assert.AreEqual(0.64f, stock[0].Condition01, "the lesser shoe remains in stock");
        }

        [Test]
        public void Fit_RefusesUnusableShoe_ConsumesNothing()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0);
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 1);
            stock[0].Condition01 = 0.05f; // worn past usability
            var farrier = new FarrierService();
            var diagnostics = new List<string>();

            FarrierFittingResult result = farrier.FitShoes(
                Horse(307), new List<HoofNeed> { new HoofNeed("front-left", 0.80f, true, false, "worn") },
                stock, ShoeSource, PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsNotNull(result.Failure);
            Assert.AreEqual(1, stock.Count);
            Assert.AreEqual(0, result.ShoesConsumed);
        }

        [Test]
        public void Fit_SkillAffectsFitQualityAndMinutes()
        {
            var smithy = NewSmithy();
            var diagnostics = new List<string>();
            var farrier = new FarrierService();

            EntityId novice = Person(1);
            var noviceSkills = FarrierSkills(novice, 0);
            List<EquipmentAsset> noviceStock = ForgeShoes(smithy, novice, noviceSkills, 1);
            FarrierFittingResult noviceResult = farrier.FitShoes(
                Horse(308), new List<HoofNeed> { new HoofNeed("front-left", 0.80f, true, false, "worn") },
                noviceStock, ShoeSource, PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), noviceSkills, diagnostics);
            Assert.IsNull(noviceResult.Failure, string.Join("; ", diagnostics));
            Assert.AreEqual(0.64f, noviceResult.Hooves[0].FitQuality01);
            Assert.AreEqual(21, noviceResult.LaborMinutes);

            EntityId master = Person(2);
            var masterSkills = FarrierSkills(master, 900);
            List<EquipmentAsset> masterStock = ForgeShoes(smithy, master, masterSkills, 1);
            FarrierFittingResult masterResult = farrier.FitShoes(
                Horse(309), new List<HoofNeed> { new HoofNeed("front-left", 0.80f, true, false, "worn") },
                masterStock, ShoeSource, PricePerShoeCents, LaborCentsPerHoof, 42, Person(2), masterSkills, diagnostics);
            Assert.IsNull(masterResult.Failure, string.Join("; ", diagnostics));
            Assert.AreEqual(1f, masterResult.Hooves[0].FitQuality01);
            Assert.AreEqual(10, masterResult.LaborMinutes);
        }

        [Test]
        public void Fit_PoorFit_AdvisedNotFailed()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0); // fit quality 0.64, below 0.70
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 1);
            var farrier = new FarrierService();
            var diagnostics = new List<string>();

            FarrierFittingResult result = farrier.FitShoes(
                Horse(310), new List<HoofNeed> { new HoofNeed("front-left", 0.80f, true, false, "worn") },
                stock, ShoeSource, PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsTrue(result.Succeeded, "a poor fit is recorded and advised, not failed");
            Assert.IsTrue(result.Hooves[0].Notes.Contains("poor fit"));
            Assert.AreEqual(1, result.ShoesConsumed);
        }

        [Test]
        public void Fit_RefusesInvalidHorse_NoAssessment_NoSource_NoSkillService()
        {
            var smithy = NewSmithy();
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 0);
            List<EquipmentAsset> stock = ForgeShoes(smithy, smith, skills, 1);
            var farrier = new FarrierService();
            var diagnostics = new List<string>();

            FarrierFittingResult badHorse = farrier.FitShoes(
                EntityId.Invalid, FourHooves(), stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);
            Assert.IsNotNull(badHorse.Failure);

            FarrierFittingResult noAssessment = farrier.FitShoes(
                Horse(311), new List<HoofNeed>(), stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);
            Assert.IsNotNull(noAssessment.Failure);

            FarrierFittingResult noSource = farrier.FitShoes(
                Horse(311), FourHooves(), stock, "",
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);
            Assert.IsNotNull(noSource.Failure);

            FarrierFittingResult noSkills = farrier.FitShoes(
                Horse(311), FourHooves(), stock, ShoeSource,
                PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), null, diagnostics);
            Assert.IsNotNull(noSkills.Failure);

            Assert.AreEqual(1, stock.Count, "no refusal consumed a shoe");
        }

        // Scenario 3, end to end: acquire material and fuel, forge one shoe,
        // it enters inventory, fitting consumes that exact shoe with real labour.
        [Test]
        public void Scenario3_AcquireForgeFit_OneShoeEndToEnd()
        {
            // Acquire: imported iron and coal lots arrive (EQU-1 delivery).
            var smithy = NewSmithy(iron: 2, coal: 1);
            EntityId smith = Person(1);
            var skills = FarrierSkills(smith, 900); // master farrier
            var diagnostics = new List<string>();
            int minutes;

            // Forge: one shoe.
            EquipmentAsset shoe = smithy.ForgeHorseshoe(smith, 42, skills, diagnostics, out minutes);
            Assert.IsNotNull(shoe, string.Join("; ", diagnostics));
            Assert.AreEqual(10, minutes, "master forges one shoe in 10 minutes");
            Assert.AreEqual(1f, shoe.Condition01);
            Assert.AreEqual(2 - 1, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(1 - 1, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));

            // It enters inventory — and forging alone does NOT fit anything.
            Assert.IsTrue(smithy.Assets.ContainsKey(shoe.AssetId), "the shoe is in shop inventory");
            Assert.AreEqual(1, smithy.Assets.Count);

            // Custody: the shoe is released to the farrier's stock.
            EquipmentAsset carried = smithy.TakeHorseshoe(shoe.AssetId, diagnostics);
            Assert.IsNotNull(carried);
            Assert.AreEqual(0, smithy.Assets.Count);
            var farrierStock = new List<EquipmentAsset> { carried };

            // Fit: the farrier consumes that exact shoe, with real labour.
            var farrier = new FarrierService();
            FarrierFittingResult result = farrier.FitShoes(
                Horse(312),
                new List<HoofNeed> { new HoofNeed("front-left", 0.75f, true, true, "thrown shoe") },
                farrierStock, ShoeSource, PricePerShoeCents, LaborCentsPerHoof, 42, Person(1), skills, diagnostics);

            Assert.IsNull(result.Failure, string.Join("; ", diagnostics));
            Assert.AreEqual(1, result.Hooves.Count);
            Assert.AreEqual(shoe.AssetId, result.Hooves[0].ShoeAssetId, "fitting consumed that exact shoe");
            Assert.AreEqual(1, result.ShoesConsumed);
            Assert.AreEqual(0, farrierStock.Count);
            Assert.AreEqual(10, result.LaborMinutes, "real labour, not a faked completion");
            Assert.AreEqual(PricePerShoeCents, result.MaterialCents);
            Assert.AreEqual(LaborCentsPerHoof, result.LaborCents);
            Assert.AreEqual(PricePerShoeCents + LaborCentsPerHoof, result.TotalCents);
            Assert.IsTrue(result.Hooves[0].Inspected && result.Hooves[0].Trimmed
                && result.Hooves[0].ShoeShaped && result.Hooves[0].Secured && result.Hooves[0].FitChecked);
        }
    }
}
