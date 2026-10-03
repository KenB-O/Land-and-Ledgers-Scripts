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

        /// <summary>
        /// EQP-4 Group C: cattle handling pens. Handling/sorting cattle requires
        /// corrals/chutes — a HARD requirement; cattle cannot be worked safely
        /// without restraint infrastructure (Canon Part V: Ranch).
        /// </summary>
        public static WorkstationDefinition CattleHandlingPens
        {
            get
            {
                var def = Build("cattle-handling-pens", "Cattle Handling Pens", "ranch-yard",
                    "Canon Part V: Ranch; Tech X §3.5",
                    new[] { "handle-cattle", "sort-cattle" },
                    ("corral-fencing", 1), ("handling-chute", 1), ("sorting-gates", 1));
                return def;
            }
        }

        /// <summary>
        /// EQP-4 Group D: the store counter. Weighing/measuring goods requires
        /// scales — a store that can't weigh can't sell by weight (Canon 4.2).
        /// </summary>
        public static WorkstationDefinition StoreCounter
        {
            get
            {
                var def = Build("store-counter", "Store Counter", "general-store",
                    "Canon Part V: GeneralStore; Canon 4.2",
                    new[] { "sell-by-weight", "cash-control" },
                    ("counter", 1), ("scale-set", 1), ("cash-drawer", 1));
                return def;
            }
        }

        /// <summary>
        /// EQP-4 Group D: boarding-house kitchen. Cooking requires stove + cookware
        /// (Canon Part V: BoardingHouse).
        /// </summary>
        public static WorkstationDefinition BoardingKitchen
        {
            get
            {
                var def = Build("boarding-kitchen", "Boarding Kitchen", "boarding-house",
                    "Canon Part V: BoardingHouse (cook)",
                    new[] { "cook-meals" },
                    ("stove-range", 1), ("cookware-set", 1), ("pantry-bins", 1));
                def.SupportRequirements.Add(new SupportRequirement("fuel", "oven-wood", 2));
                return def;
            }
        }

        /// <summary>
        /// EQP-4 Group D: boarding-house laundry. Requires tubs + water + soap
        /// (Canon Part V: BoardingHouse).
        /// </summary>
        public static WorkstationDefinition LaundryStation
        {
            get
            {
                var def = Build("laundry-station", "Laundry Station", "boarding-house",
                    "Canon Part V: BoardingHouse (laundress)",
                    new[] { "do-laundry" },
                    ("wash-tubs", 1), ("washboard-set", 1), ("irons", 1));
                def.SupportRequirements.Add(new SupportRequirement("consumable", "soap-lye", 1));
                return def;
            }
        }

        /// <summary>
        /// EQP-5: tanning yard. Historical: tanyards needed pits/vats and RUNNING
        /// WATER, sited away from dense habitation. Tanning is a months-long
        /// process, not a task that hurries — the yard grants the capability,
        /// time does the work.
        /// </summary>
        public static WorkstationDefinition TanningYard
        {
            get
            {
                var def = Build("tanning-yard", "Tanning Yard", "tanyard",
                    "EQP-5 historical research (2026-10-03); Tech X §3.5",
                    new[] { "tan-hides" },
                    ("tanning-pits", 1), ("hide-racks", 1), ("bark-mill", 1));
                def.SupportRequirements.Add(new SupportRequirement("infrastructure", "running-water"));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "tanning"));
                return def;
            }
        }

        /// <summary>All workstation definitions, for catalog-driven UI and validation.</summary>
        public static List<WorkstationDefinition> All => new List<WorkstationDefinition>
        {
            ForgeStation, BakeOven, SaloonBar, GrainMillStation, ButcherBlock, SawmillSawLine, AssayBench,
            CattleHandlingPens, StoreCounter, BoardingKitchen, LaundryStation, TanningYard,
        };
    }
}
