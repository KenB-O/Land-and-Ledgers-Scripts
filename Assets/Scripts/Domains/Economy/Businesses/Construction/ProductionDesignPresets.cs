using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D-5 (expanded campaign): the production building designs — the
    /// single authority for what the NPC construction trigger can build.
    /// The bootstrapper registers these into the design catalog at New
    /// Game; the tests below pin them directly, so a design can never
    /// drift from what production registers without a test failing.
    ///
    /// Every design is lumber-only: lumber is the only material the
    /// production price port can price and the yard can supply
    /// (LumberYardConstructionPricePort returns -1 for masonry, paint,
    /// glass, roofing and anything else). A design naming any other
    /// material refuses at evaluation — cost unknowable, never guessed —
    /// so no design here may invent a supplier or a price.
    ///
    /// Numbers are documented proposed defaults under the delegation
    /// doctrine (owner corrects what he disagrees with). The settler
    /// cabin is verbatim the DEF-011 production design. The family cabin
    /// is ~1.5x the settler cabin in lumber (960 vs 640) and labour
    /// (900 vs 600 min), a two-room cabin for a household the settler
    /// cabin cannot sleep. The carpenter's workshop is a frontier
    /// workshop with living quarters (the shopkeeper lives on the
    /// premises, as period shops did): the shop floor is a 0-sleeping
    /// work space, the quarters sleep 2. Business operation itself —
    /// equipment installation, trade from the building — is out of
    /// Phase 2 scope; this design is the building, not the business
    /// runtime. Estimates at the 40c yard retail lumber rate (C-4:
    /// 32c wholesale, 25% markup): cabin 25,600c, family cabin 38,400c,
    /// workshop 47,200c.
    /// </summary>
    public static class ProductionDesignPresets
    {
        public static BuildingDesign SettlerCabin()
        {
            var design = new BuildingDesign
            {
                DesignId = "settler-cabin",
                DisplayName = "Settler cabin",
                WorksKind = ConstructionWorksKind.NewBuilding,
                KindLabel = "cabin",
            };
            var sills = new BuildingDesignPhase
            {
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 120,
                Notes = "sill logs laid on grade",
            };
            sills.Materials.Add(LumberRequirement("REQ-sill-lumber", 40));
            sills.RequiredToolItemIds.Add("saw");
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Framing - walls", Sequence = 1, LaborMinutes = 240,
                Notes = "log walls",
            };
            walls.Materials.Add(LumberRequirement("REQ-wall-lumber", 300));
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Framing - roof", Sequence = 2, LaborMinutes = 240,
                Notes = "log roof",
            };
            roof.Materials.Add(LumberRequirement("REQ-roof-lumber", 300));
            roof.RequiredToolItemIds.Add("saw");
            design.Phases.Add(sills);
            design.Phases.Add(walls);
            design.Phases.Add(roof);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            return design;
        }

        public static BuildingDesign FamilyCabin()
        {
            var design = new BuildingDesign
            {
                DesignId = "family-cabin",
                DisplayName = "Family cabin",
                WorksKind = ConstructionWorksKind.NewBuilding,
                KindLabel = "cabin",
            };
            var sills = new BuildingDesignPhase
            {
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 180,
                Notes = "sill logs laid on grade",
            };
            sills.Materials.Add(LumberRequirement("REQ-sill-lumber", 60));
            sills.RequiredToolItemIds.Add("saw");
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Framing - walls", Sequence = 1, LaborMinutes = 360,
                Notes = "log walls",
            };
            walls.Materials.Add(LumberRequirement("REQ-wall-lumber", 450));
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Framing - roof", Sequence = 2, LaborMinutes = 360,
                Notes = "log roof",
            };
            roof.Materials.Add(LumberRequirement("REQ-roof-lumber", 450));
            roof.RequiredToolItemIds.Add("saw");
            design.Phases.Add(sills);
            design.Phases.Add(walls);
            design.Phases.Add(roof);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "second room", SleepingCapacity = 2 });
            return design;
        }

        public static BuildingDesign CarpentersWorkshop()
        {
            var design = new BuildingDesign
            {
                DesignId = "carpenters-workshop",
                DisplayName = "Carpenter's workshop",
                WorksKind = ConstructionWorksKind.NewBuilding,
                KindLabel = "workshop",
            };
            var sills = new BuildingDesignPhase
            {
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 240,
                Notes = "sill logs laid on grade",
            };
            sills.Materials.Add(LumberRequirement("REQ-sill-lumber", 80));
            sills.RequiredToolItemIds.Add("saw");
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Framing - walls", Sequence = 1, LaborMinutes = 480,
                Notes = "log walls",
            };
            walls.Materials.Add(LumberRequirement("REQ-wall-lumber", 600));
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Framing - roof", Sequence = 2, LaborMinutes = 420,
                Notes = "log roof",
            };
            roof.Materials.Add(LumberRequirement("REQ-roof-lumber", 500));
            roof.RequiredToolItemIds.Add("saw");
            design.Phases.Add(sills);
            design.Phases.Add(walls);
            design.Phases.Add(roof);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "workshop floor", SleepingCapacity = 0 });
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "living quarters", SleepingCapacity = 2 });
            return design;
        }

        /// <summary>All three production designs, in registration order.</summary>
        public static List<BuildingDesign> All()
        {
            return new List<BuildingDesign> { SettlerCabin(), FamilyCabin(), CarpentersWorkshop() };
        }

        private static ConstructionMaterialRequirement LumberRequirement(string requirementId, int units)
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = requirementId,
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = units,
                UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            };
        }
    }
}
