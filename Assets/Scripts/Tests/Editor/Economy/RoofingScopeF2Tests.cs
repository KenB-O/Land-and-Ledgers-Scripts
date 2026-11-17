using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// F2 (workstream F, roofing review): the roofing verdict, pinned.
    ///
    /// All three production designs specify a LOG ROOF with lumber only:
    /// the roof phase ("Framing - roof") consumes lumber (the board
    /// covering, folded into the same lumber quantity as the framing)
    /// plus door hardware for the openings, and no roofing material at
    /// all. Wood shingles are the documented future option (E1 §8/§9:
    /// 1,800-2,500 shingles per capable worker-day on straight work;
    /// "if a design later uses wood shingles, the §8 shingle rate
    /// applies and the roof package must consume shingles as a real
    /// item") — but no design uses them today, so no shingle supply
    /// chain is built in this lane: a good with no consuming design
    /// would be supply without demand. The system fails closed instead:
    /// a design naming a roofing material refuses at evaluation (the
    /// price port returns -1 for roofing), and no supplier can be
    /// discovered for it (no phantom supplier). These tests pin that
    /// boundary so a future shingled design — or a future shingle
    /// supply chain — must change them deliberately, not silently.
    /// </summary>
    [TestFixture]
    public sealed class RoofingScopeF2Tests
    {
        private sealed class FakePricePort : IConstructionMaterialPricePort
        {
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int UnitPriceCents(ConstructionMaterialRequirement requirement)
            {
                if (requirement == null) return -1;
                return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
            }
        }

        private static BuildingDesign ShingledSettler()
        {
            BuildingDesign design = ProductionDesignPresets.SettlerCabin();
            design.DesignId = "settler-cabin-shingled";
            design.DisplayName = "Settler cabin (shingled roof)";
            design.Phases[2].Materials.Add(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-roof-shingles",
                MaterialKind = ConstructionMaterialKind.Roofing,
                MaterialDisplayName = "wood shingles",
                // Illustrative quantity only — no authored shingle takeoff
                // exists. The refusal holds for any positive count; the
                // count is not a calibration.
                RequiredUnits = 1500,
                UnitLabel = "shingles",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            });
            return design;
        }

        [Test]
        public void PresetDesigns_RoofPhase_HasNoRoofingRequirement()
        {
            foreach (BuildingDesign design in ProductionDesignPresets.All())
            {
                foreach (BuildingDesignPhase phase in design.Phases)
                {
                    foreach (ConstructionMaterialRequirement requirement in phase.Materials)
                    {
                        Assert.AreNotEqual(ConstructionMaterialKind.Roofing, requirement.MaterialKind,
                            $"design '{design.DesignId}' phase '{phase.PhaseName}' must not require roofing " +
                            "— all three designs specify a log roof with lumber only (F2 interim scope)");
                    }
                }
            }
        }

        [Test]
        public void ShingledDesign_EvaluateCommission_RefusesOnUnpricedRoofing()
        {
            var catalog = new BuildingDesignCatalog();
            var diag = new List<string>();
            Assert.IsNull(catalog.RegisterDesign(ProductionDesignPresets.SettlerCabin(), diag),
                "test setup must register the plain settler: " + string.Join("; ", diag));
            Assert.IsNull(catalog.RegisterDesign(ShingledSettler(), diag),
                "test setup must register the shingled variant: " + string.Join("; ", diag));

            var commissioner = new NpcConstructionCommissioner();
            var prices = new FakePricePort();
            prices.Prices["lumber"] = 40;
            prices.Prices["door hardware"] = 45;

            NpcConstructionEvaluation plain = commissioner.EvaluateCommission(
                catalog, "settler-cabin", 1, prices, 100000, 28,
                NpcConstructionReason.Overcrowding, "test", 10, diag);
            Assert.IsTrue(plain.IsViable,
                "control: the plain log-roof settler evaluates viable with lumber and hardware priced: " +
                string.Join("; ", diag));

            NpcConstructionEvaluation shingled = commissioner.EvaluateCommission(
                catalog, "settler-cabin-shingled", 1, prices, 100000, 28,
                NpcConstructionReason.Overcrowding, "test", 10, diag);
            Assert.IsFalse(shingled.IsViable,
                "a shingled design with no priced roofing good is not viable");
            Assert.IsTrue(shingled.RefusalReason.Contains("no price"),
                "the refusal names the unpriced material: " + shingled.RefusalReason);
            Assert.IsTrue(shingled.RefusalReason.Contains("wood shingles"),
                "the refusal names the shingles (the material display name): " + shingled.RefusalReason);
        }

        [Test]
        public void RoofingRequirement_DiscoverSupplier_RefusesNoPhantomSupplier()
        {
            Assert.AreEqual("construction-roofing",
                ConstructionProcurementService.CategoryKeyFor(ConstructionMaterialKind.Roofing),
                "the freight layer knows the roofing category key");

            var requirement = new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-roof-shingles",
                MaterialKind = ConstructionMaterialKind.Roofing,
                MaterialDisplayName = "wood shingles",
                RequiredUnits = 1500,
                UnitLabel = "shingles",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            };
            var service = new ConstructionProcurementService();
            var directory = new SupplierDirectory();
            var diag = new List<string>();

            Assert.IsNull(service.DiscoverSupplier(requirement, directory, diag),
                "no real supplier exists for roofing — discovery must refuse, never invent one: " +
                string.Join("; ", diag));
            Assert.IsTrue(string.Join(" ", diag).Contains("no real supplier"),
                "the refusal says no real supplier was found: " + string.Join("; ", diag));
        }
    }
}
