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
    /// Every design is lumber plus door hardware: lumber and hardware are
    /// the only materials the production price port can price and the yard
    /// can supply (LumberYardConstructionPricePort returns -1 for masonry,
    /// paint, roofing and anything else — glass is priced from the first
    /// stocked lot since F1). A design naming any other
    /// material refuses at evaluation — cost unknowable, never guessed —
    /// so no design here may invent a supplier or a price.
    ///
    /// D2 door hardware: the roof phase carries the openings (E1's openings
    /// labour was folded into the roof phase by E2, and the hardware rides
    /// with it; a later pass may split a dedicated openings phase once
    /// doors, hardware and glass exist as real materials). One "door
    /// hardware" requirement per design, one display name, supplied by the
    /// yard — the sale adapter requires one consistent hardware display
    /// name per package, and the yard fills the bucket nails-first from
    /// its real lots. Quantities are a provisional rule, for the owner to
    /// correct: per door 4 forged pieces (2 hinges, 1 latch, 1 handle) +
    /// 2 nails units; per window 2 forged pieces (1 hinge, 1 latch, for
    /// the shutter) + 1 nails unit. Settler cabin 1 door + 1 window = 9;
    /// family cabin 2 doors + 2 windows = 18; workshop 2 doors (shop +
    /// quarters) + 2 windows = 18. Wood and metal stay distinct: the
    /// lumber requirement is the door and frame timber, the hardware
    /// requirement is the iron that hangs and latches it — no door appears
    /// from a completion percentage while the hardware is unavailable,
    /// because the executor stages all-or-nothing from the site stockpile.
    ///
    /// Numbers are documented proposed defaults under the delegation
    /// doctrine (owner corrects what he disagrees with). The settler
    /// cabin is verbatim the DEF-011 production design. The family cabin
    /// is ~1.5x the settler cabin in lumber (960 vs 640) and labour
    /// (14,400 vs 5,400 person-minutes), a two-room cabin for a household
    /// the settler cabin cannot sleep. The carpenter's workshop is a
    /// frontier workshop with living quarters (the shopkeeper lives on the
    /// premises, as period shops did): the shop floor is a 0-sleeping
    /// work space, the quarters sleep 2. Business operation itself —
    /// equipment installation, trade from the building — is out of
    /// Phase 2 scope; this design is the building, not the business
    /// runtime. Estimates at the 40c yard retail lumber rate (C-4:
    /// 32c wholesale, 25% markup) plus door hardware at the 45c
    /// simple-hardware yard convention: cabin 26,005c, family cabin
    /// 39,210c, workshop 48,010c.
    ///
    /// E2 (2026-11-17, workstream E): labour recalibrated from the E1
    /// historical calibration report
    /// (Reports/Research/HISTORICAL_CONSTRUCTION_AND_BLACKSMITH_CALIBRATION_2026-11-17.md).
    /// All labour values are PROVISIONAL, ordinary preset, materials
    /// staged: settler cabin 600 to 5,400 person-minutes (90 ph),
    /// family cabin 900 to 14,400 (240 ph), carpenter's workshop 1,140
    /// to 28,800 (480 ph). The E1 phase allocations are FOLDED into the
    /// existing three phases here — site preparation into Sills, openings
    /// and interior work into Roof — so the phase count, materials and
    /// lumber totals are unchanged; totals are preserved exactly. A
    /// later H-lane may split site preparation, openings and interior
    /// into their own phases once the procurement leg handles labour-only
    /// phases (today it refuses a package spanning zero suppliers) and
    /// once doors, hardware and glass exist as real materials for the
    /// openings phase. LabourMinutes is TOTAL person-minutes, never
    /// elapsed minutes: one worker-day is 600 minutes, so a package is
    /// worked across as many days as its total requires (see
    /// ConstructionProjectExecutor.CommitLabor, which commits one day's
    /// chunk at a time). If E1 comes back NOT VERIFIED, retune ONLY the
    /// LaborMinutes numbers and the totals pinned in
    /// NpcConstructionDesignsDef011Tests — the phase structure, materials
    /// and lumber totals do not move with the calibration.
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
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 1200,
                Notes = "E2 PROVISIONAL: site preparation 600 + sills 600, folded (see class notes)",
            };
            sills.Materials.Add(LumberRequirement("REQ-sill-lumber", 40));
            sills.RequiredToolItemIds.Add("saw");
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Framing - walls", Sequence = 1, LaborMinutes = 2100,
                Notes = "log walls",
            };
            walls.Materials.Add(LumberRequirement("REQ-wall-lumber", 300));
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Framing - roof", Sequence = 2, LaborMinutes = 2100,
                Notes = "E2 PROVISIONAL: log roof 1500 + door and window openings 600, folded (see class notes)",
            };
            roof.Materials.Add(LumberRequirement("REQ-roof-lumber", 300));
            roof.Materials.Add(HardwareRequirement("REQ-roof-hardware", 9));
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
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 2400,
                Notes = "E2 PROVISIONAL: site preparation 1200 + sills 1200, folded (see class notes)",
            };
            sills.Materials.Add(LumberRequirement("REQ-sill-lumber", 60));
            sills.RequiredToolItemIds.Add("saw");
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Framing - walls", Sequence = 1, LaborMinutes = 5400,
                Notes = "log walls",
            };
            walls.Materials.Add(LumberRequirement("REQ-wall-lumber", 450));
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Framing - roof", Sequence = 2, LaborMinutes = 6600,
                Notes = "E2 PROVISIONAL: log roof 4200 + doors and windows 1200 + interior partition and finish 1200, folded (see class notes)",
            };
            roof.Materials.Add(LumberRequirement("REQ-roof-lumber", 450));
            roof.Materials.Add(HardwareRequirement("REQ-roof-hardware", 18));
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
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 6000,
                Notes = "E2 PROVISIONAL: site preparation 2400 + sills/foundation 3600, folded (see class notes)",
            };
            sills.Materials.Add(LumberRequirement("REQ-sill-lumber", 80));
            sills.RequiredToolItemIds.Add("saw");
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Framing - walls", Sequence = 1, LaborMinutes = 9600,
                Notes = "log walls",
            };
            walls.Materials.Add(LumberRequirement("REQ-wall-lumber", 600));
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Framing - roof", Sequence = 2, LaborMinutes = 13200,
                Notes = "E2 PROVISIONAL: log roof 7200 + doors and windows 2400 + interior fit-out 3600, folded (see class notes)",
            };
            roof.Materials.Add(LumberRequirement("REQ-roof-lumber", 500));
            roof.Materials.Add(HardwareRequirement("REQ-roof-hardware", 18));
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

        /// <summary>
        /// D2: one door-hardware requirement per design, in the roof phase
        /// with the openings. The yard is the supplier; the price port
        /// prices it from the first stocked yard hardware lot (30c nails
        /// or 45c simple hardware, whichever lot is first in FIFO order).
        /// </summary>
        private static ConstructionMaterialRequirement HardwareRequirement(string requirementId, int units)
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = requirementId,
                MaterialKind = ConstructionMaterialKind.Hardware,
                MaterialDisplayName = "door hardware",
                RequiredUnits = units,
                UnitLabel = "units",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            };
        }
    }
}
