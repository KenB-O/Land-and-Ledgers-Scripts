using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment.Workstations
{
    /// <summary>
    /// EQP-2: workstation definitions as DATA (Tech X §3.5, §3.10) — the Group A
    /// workstations from the canon equipment survey. Each workstation's
    /// capabilities derive from actual component assets + space + motive power;
    /// a functional space alone never grants the workstation.
    ///
    /// Component kinds are <see cref="Blacksmith.EquipmentAsset"/>.Kind strings.
    /// Wear rates and times are calibration (Canon Part XV); the qualitative
    /// gating rules are canon.
    /// </summary>
    public static class WorkstationCatalog
    {
        private static WorkstationDefinition Build(
            string id, string displayName, string spaceKind,
            string sourceNote, string[] capabilities,
            params (string kind, int count)[] components)
        {
            var def = new WorkstationDefinition
            {
                WorkstationId = id,
                DisplayName = displayName,
                RequiredSpaceKind = spaceKind,
                SourceNote = sourceNote,
            };
            foreach (var (kind, count) in components)
                def.Components.Add(new WorkstationComponentRequirement(kind, count));
            foreach (string capability in capabilities)
                def.CapabilitiesGranted.Add(capability);
            return def;
        }

        /// <summary>Tech X §3.5: ForgeStation. EQP-2 migrates the EQU-2 flag to derived readiness.</summary>
        public static WorkstationDefinition ForgeStation
        {
            get
            {
                var def = Build("forge-station", "Forge Station", "smithy",
                    "Tech X §3.5; Canon Part V: Blacksmith",
                    new[] { "smith-equipment", "repair-work-order", "shoe-horse" },
                    ("forge", 1), ("anvil", 1), ("smith-hand-tools", 1));
                def.SupportRequirements.Add(new SupportRequirement("fuel", "forge-coal", 2));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "smithing"));
                return def;
            }
        }

        /// <summary>Tech X §3.5: BakeOven. Baking requires the oven — the room is not enough.</summary>
        public static WorkstationDefinition BakeOven
        {
            get
            {
                var def = Build("bake-oven", "Bake Oven", "bakehouse",
                    "Tech X §3.5: BakeOven; Canon Part V: Bakery",
                    new[] { "bake-bread" },
                    ("oven-chamber", 1), ("kneading-table", 1), ("proofing-rack", 1), ("bake-peels", 1));
                def.SupportRequirements.Add(new SupportRequirement("fuel", "oven-wood", 3));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "baking"));
                return def;
            }
        }

        /// <summary>Tech X §3.5: SaloonBar. Faro requires the layout + casekeeping — the game can't run without the apparatus.</summary>
        public static WorkstationDefinition SaloonBar
        {
            get
            {
                var def = Build("saloon-bar", "Saloon Bar", "saloon",
                    "Tech X §3.5: SaloonBar; Canon Part V: Saloon",
                    new[] { "serve-drinks", "run-faro" },
                    ("bar-counter", 1), ("glassware-set", 1), ("faro-layout", 1));
                return def;
            }
        }

        /// <summary>
        /// Millstones + power. Milling requires stones AND motive power (both hard);
        /// millstone dressing is the maintenance loop. Either water or steam power
        /// satisfies the drive (Tech X §3.6).
        /// </summary>
        public static WorkstationDefinition GrainMillStation
        {
            get
            {
                var def = Build("grain-mill-station", "Millstones & Power", "mill",
                    "Tech X §3.5; Canon Part V: GrainMill",
                    new[] { "mill-grain" },
                    ("millstones", 1), ("hopper-feed", 1), ("sifter-bolter", 1));
                def.AcceptableMotivePowerKinds.Add("Water");
                def.AcceptableMotivePowerKinds.Add("Steam");
                def.SupportRequirements.Add(new SupportRequirement("consumable", "mill-lubrication", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "milling"));
                return def;
            }
        }

        /// <summary>
        /// Butcher block & rail. Slaughter requires the facility — and lawfully
        /// (premises/authority via BIZ-1); breakdown requires knives/saw/block.
        /// </summary>
        public static WorkstationDefinition ButcherBlock
        {
            get
            {
                var def = Build("butcher-block", "Butcher Block & Rail", "butcher-shop",
                    "Canon Part V: Butcher; Tech X §3.9",
                    new[] { "slaughter-livestock", "break-carcass" },
                    ("block-table", 1), ("meat-saw", 1), ("rail-hooks", 1), ("scale-set", 1));
                return def;
            }
        }

        /// <summary>
        /// Tech X §3.5: SawmillSawLine. The filer loop is the cleanest canon example
        /// of §3.9: a dull/broken saw gates ALL output until the filer works.
        /// </summary>
        public static WorkstationDefinition SawmillSawLine
        {
            get
            {
                var def = Build("sawmill-saw-line", "Sawmill Saw Line", "sawmill",
                    "Tech X §3.5: SawmillSawLine; Canon Part V: Sawmill",
                    new[] { "saw-lumber" },
                    ("saw-line", 1), ("log-carriage", 1), ("drive-system", 1));
                def.AcceptableMotivePowerKinds.Add("Steam");
                def.AcceptableMotivePowerKinds.Add("Water");
                def.SupportRequirements.Add(new SupportRequirement("consumable", "mill-lubrication", 1));
                def.SupportRequirements.Add(new SupportRequirement("repair-capability", "saw-filing"));
                return def;
            }
        }

        /// <summary>
        /// Tech X §3.5: AssayBench. Standalone service capability — assaying needs
        /// the bench + furnace, NOT a mine (survey §2.19).
        /// </summary>
        public static WorkstationDefinition AssayBench
        {
            get
            {
                var def = Build("assay-bench", "Assay Bench", "assay-office",
                    "Tech X §3.5: AssayBench; Canon Part V: Mine (assayer)",
                    new[] { "assay-ore" },
                    ("assay-furnace", 1), ("assay-balance", 1), ("crucible-set", 1));
                def.SupportRequirements.Add(new SupportRequirement("fuel", "charcoal", 1));
                def.SupportRequirements.Add(new SupportRequirement("consumable", "crucible", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "assaying"));
                return def;
            }
        }

        /// <summary>All Group A definitions, for catalog-driven UI and validation.</summary>
        public static List<WorkstationDefinition> All => new List<WorkstationDefinition>
        {
            ForgeStation, BakeOven, SaloonBar, GrainMillStation, ButcherBlock, SawmillSawLine, AssayBench,
        };
    }
}
