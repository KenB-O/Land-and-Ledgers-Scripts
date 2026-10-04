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

        /// <summary>
        /// W6a: WheelwrightStation. Canon Part V: Wheelwright / wagon maker —
        /// wheel jig, workbench, woodworking hand tools, forge access for tire
        /// bending/shrinking. Wheel-making and wagon repair happen here; the
        /// shop building alone grants nothing (Tech X §3.5).
        /// </summary>
        public static WorkstationDefinition WheelwrightStation
        {
            get
            {
                var def = Build("wheelwright-station", "Wheelwright Station", "wheelwright-shop",
                    "Tech X §3.5; Canon Part V: Wheelwright / wagon maker",
                    new[] { "wheelwright-build", "wheelwright-repair-order" },
                    ("wheel-jig", 1), ("workbench", 1), ("wheelwright-hand-tools", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "wheelwrighting"));
                return def;
            }
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
        /// W2B: restaurant kitchen. Cooking requires stove + cookware
        /// (Canon §8.1B: commercial meal service needs kitchen labor and
        /// real ingredient economics) — the room is not enough, mirroring
        /// the Tech X §3.5 bake-oven gate. Same canonical hardware as the
        /// boarding-house kitchen; the workstation id is restaurant-scoped
        /// so the equipment gate resolves it to the eating house's own
        /// kitchens.
        /// </summary>
        public static WorkstationDefinition RestaurantKitchen
        {
            get
            {
                var def = Build("restaurant-kitchen", "Restaurant Kitchen", "restaurant",
                    "W2B; Canon §8.1B (eating-house meal service)",
                    new[] { "cook-meals" },
                    ("stove-range", 1), ("cookware-set", 1), ("pantry-bins", 1));
                // A stove with no fuel is not usable (Canon 5.2; mirrors the
                // boarding-house kitchen's fuel support requirement).
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

        /// <summary>
        /// NX-3A: PrintingPress. Tech X §3.5 names it; Canon Part V profiles the
        /// typesetter/compositor (movable type, cases, composing stick, galley)
        /// and the press operator (hand/platen/cylinder press, chases, ink
        /// rollers, ink, paper, drying/stacking space). Capabilities derive
        /// from the components — a pressroom alone never prints.
        /// </summary>
        public static WorkstationDefinition PrintingPress
        {
            get
            {
                var def = Build("printing-press", "Printing Press", "pressroom",
                    "Tech X §3.5; Canon Part V: Typesetter/Press operator",
                    new[] { "print-edition", "print-job-order", "sell-ad-space" },
                    ("press", 1), ("type-cases", 1), ("composing-stick", 1), ("ink-rollers", 1));
                def.SupportRequirements.Add(new SupportRequirement("consumable", "newsprint", 4));
                def.SupportRequirements.Add(new SupportRequirement("consumable", "printer-ink", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "typesetting"));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "presswork"));
                return def;
            }
        }

        /// <summary>W1B: barber chair station. Canon Part V barber profile: the barber
        /// chair is core equipment; "multiple chairs" is the scale column. One
        /// definition, many instances — the barbershop runtime pools chairs and
        /// assigns one per service, so concurrent customers never exceed ready
        /// chairs. Tech X §3.5 names the BarberStation/Kit requirement family.
        /// </summary>
        public static WorkstationDefinition BarberChairStation
        {
            get
            {
                var def = Build("barber-chair-station", "Barber Chair Station", "barber-shop",
                    "W1B; Tech X §3.5 (BarberStation/Kit); Canon Part V: Barber (barber chair; multiple chairs as scale)",
                    new[] { "barbering" },
                    ("barber-chair", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "barbering"));
                return def;
            }
        }

        /// <summary>
        /// D1B: barber bath station. Canon Part V barber profile: "bath tubs /
        /// hot-water capability" is the scale column — the bath service line
        /// exists because the shop invested in tubs, not because a chair is
        /// free. The tub is a component asset (condition-gated like any
        /// equipment); hot water is an infrastructure support requirement —
        /// period pattern: a laundry stove / boiler / kettle on the premises
        /// (the laundress profile). One definition, many instances — the shop
        /// runtime pools tubs and assigns one per bath, so concurrent baths
        /// never exceed ready tubs with hot water. Tech X §3.5 names the
        /// BarberStation/Kit requirement family.
        /// </summary>
        public static WorkstationDefinition BarberBathStation
        {
            get
            {
                var def = Build("barber-bath-station", "Barber Bath Station", "barber-shop",
                    "D1B; Tech X §3.5 (BarberStation/Kit); Canon Part V: Barber (bath tubs/hot-water capability as scale)",
                    new[] { "bathing" },
                    ("bath-tub", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "barbering"));
                def.SupportRequirements.Add(new SupportRequirement("infrastructure", "hot-water"));
                return def;
            }
        }

        /// <summary>
        /// W1C: tailor cutting table station. Canon Part V tailor profile: the
        /// cutting table is core equipment ("Ironing/CuttingTable", Tech X
        /// §3.5); "larger pressing table" and "stock of cloth/notions" are the
        /// scale column. One definition, many instances — the tailor shop
        /// runtime pools tables and assigns one per bench stage (cut/sew/press),
        /// so concurrent bench work never exceeds ready tables. Canon 4.7: the
        /// sewing machine is capacity only and never gates — it is not a
        /// component here.
        /// </summary>
        public static WorkstationDefinition TailorCuttingTableStation
        {
            get
            {
                var def = Build("tailor-cutting-table", "Tailor Cutting Table", "tailor-shop",
                    "W1C; Tech X §3.5 (TailorHandKit; Ironing/CuttingTable); Canon Part V: Tailor (cutting table; sewing machine capacity-only, never gates)",
                    new[] { "tailoring" },
                    ("cutting-table", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "tailoring"));
                return def;
            }
        }

        /// <summary>
        /// W7B: teller window. Canon §18.10/§18.12: deposits and withdrawals
        /// are the bank's daily commerce — the window (counter + cash drawer
        /// + scale) is where liabilities are taken on and paid out. A
        /// banking-house room alone grants nothing (Tech X §3.5).
        /// </summary>
        public static WorkstationDefinition TellerWindowStation
        {
            get
            {
                var def = Build("teller-window", "Teller Window", "banking-house",
                    "W7B; Tech X §3.5 (teller window as workstation); Canon §18.10/§18.12",
                    new[] { "take-deposits", "pay-withdrawals" },
                    ("teller-counter", 1), ("cash-drawer", 1), ("scale-set", 1));
                def.SupportRequirements.Add(new SupportRequirement("operator-skill", "teller-work"));
                return def;
            }
        }

        /// <summary>All workstation definitions, for catalog-driven UI and validation.</summary>
        public static List<WorkstationDefinition> All => new List<WorkstationDefinition>
        {
            ForgeStation, BakeOven, SaloonBar, GrainMillStation, ButcherBlock, SawmillSawLine, AssayBench,
            CattleHandlingPens, StoreCounter, BoardingKitchen, LaundryStation, TanningYard, PrintingPress,
            BarberChairStation, BarberBathStation, TailorCuttingTableStation, TellerWindowStation,
        };
    }
}
