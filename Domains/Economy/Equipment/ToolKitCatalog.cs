using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-3: ToolKit definitions as DATA (Tech X §3.4, Canon 4.6) — the Group B
    /// toolkit trades from the canon equipment survey. Every kit carries its
    /// canonical contents; "ToolKit" never means "historically unspecified."
    ///
    /// Canon 4.7 case study honored throughout: capacity/scale equipment (the
    /// tailor's sewing machine, powered shop tools) is NEVER in a kit and NEVER
    /// gates the underlying task — hand methods remain physically possible.
    /// </summary>
    public static class ToolKitCatalog
    {
        private static ToolKitDefinition Kit(string id, string name, string trade,
            string source, params string[] contents) =>
            new ToolKitDefinition(id, name, trade, source, contents);

        /// <summary>Tech X §3.4's textbook example. Canon Part V: Builder.</summary>
        public static ToolKitDefinition CarpenterHandToolKit => Kit(
            "carpenter-hand-tool-kit", "Carpenter Hand Tool Kit", "builder",
            "Canon Part V: Builder (carpenter/joiner)",
            "rip saw", "crosscut saw", "backsaw", "hammer", "mallet",
            "plane", "chisels", "gouges", "brace and bits", "auger",
            "square", "bevel", "rule", "level", "marking gauge",
            "drawknife", "spokeshave", "hatchet", "adze", "clamps", "sharpening stone");

        /// <summary>Canon Part V: Builder (mason/bricklayer).</summary>
        public static ToolKitDefinition MasonKit => Kit(
            "mason-kit", "Mason's Kit", "builder",
            "Canon Part V: Builder (mason/bricklayer)",
            "trowels", "mortar hoe", "mixing box", "buckets", "brick hammer",
            "chisels", "line and pins", "level", "plumb bob", "straightedge",
            "wheelbarrow", "scaffold");

        /// <summary>
        /// Canon Part V: Tailor. The sewing machine is capacity/scale ONLY
        /// (Canon 4.7) — it is not in this kit and never gates tailoring.
        /// </summary>
        public static ToolKitDefinition TailorHandKit => Kit(
            "tailor-hand-kit", "Tailor's Hand Kit", "tailor",
            "Canon Part V: Tailor; Canon 4.7 (sewing machine = capacity only)",
            "shears", "needles", "thread", "measuring tape", "rule",
            "chalk", "thimble", "pins", "irons", "patterns");

        /// <summary>
        /// Canon Part V: Barber. The strop IS the razor maintenance loop —
        /// razors dull, the strop restores; it is part of the kit, not separate.
        /// </summary>
        public static ToolKitDefinition BarberKit => Kit(
            "barber-kit", "Barber's Kit", "barber",
            "Canon Part V: Barber",
            "straight razors", "strop", "clippers", "shears", "combs",
            "brushes", "shaving mugs", "towels", "wash basin", "mirror");

        /// <summary>
        /// Canon Part V: Doctor. Bandages/dressings/medicines are CONSUMABLES
        /// (Canon 4.2), not kit contents — the kit holds the durable instruments.
        /// </summary>
        public static ToolKitDefinition DoctorBag => Kit(
            "doctor-bag", "Doctor's Bag", "doctor",
            "Canon Part V: Doctor",
            "forceps", "scissors", "knives", "needles", "probe",
            "tourniquet", "thermometer", "stethoscope", "pocket case");

        /// <summary>Tech X §3.11 wagon/leather family. Canon Part V: Wheelwright.</summary>
        public static ToolKitDefinition WheelwrightKit => Kit(
            "wheelwright-kit", "Wheelwright's Kit", "wheelwright",
            "Canon Part V: Wheelwright",
            "rip saw", "crosscut saw", "backsaw", "planes", "chisels",
            "brace and bits", "drawknife", "spokeshave", "adze",
            "hammers", "mallets", "clamps", "measuring tools", "workbench");

        /// <summary>Canon Part V: LiveryFreight (stable hand / hostler).</summary>
        public static ToolKitDefinition StableGroomingKit => Kit(
            "stable-grooming-kit", "Stable Grooming Kit", "livery",
            "Canon Part V: LiveryFreight (stable hand)",
            "hoof pick", "curry comb", "brush", "mane comb",
            "sweat scraper", "bandages", "hoof dressing");

        /// <summary>Canon Part V: Mine (prospector/placer miner hand tools).</summary>
        public static ToolKitDefinition MinerHandKit => Kit(
            "miner-hand-kit", "Miner's Hand Kit", "miner",
            "Canon Part V: Mine (prospector)",
            "pick", "shovel", "gold pan", "hammer", "sample bags",
            "magnifying glass", "compass");

        /// <summary>Canon Part V: Blacksmith (farrier).</summary>
        public static ToolKitDefinition FarrierKit => Kit(
            "farrier-kit", "Farrier's Kit", "blacksmith",
            "Canon Part V: Blacksmith (farrier)",
            "shoeing hammer", "hoof knife", "rasp", "nippers",
            "clinching tools", "punch tools", "tongs", "shoeing box");

        /// <summary>
        /// EQP-4 Group C: ranch tack. Mounted work requires saddle/tack + horse;
        /// the horse itself is NOT equipment (Tech X §3.6) — the tack is.
        /// </summary>
        public static ToolKitDefinition RanchTackKit => Kit(
            "ranch-tack-kit", "Ranch Tack Kit", "ranch",
            "Canon Part V: Ranch",
            "saddle", "bridle", "reins", "rope/lariat", "halter",
            "grooming tools", "hoof tools", "saddle blankets");

        /// <summary>
        /// NX-1A: field hand-tool kit. Canon Part V: CropFarm core profile
        /// (hoe, spade/shovel, fork, rake). Tending at field scale without any
        /// tool is not a real method — the kit gates tend-field (Canon 4.1).
        /// </summary>
        public static ToolKitDefinition FieldHandKit => Kit(
            "field-hand-kit", "Field Hand Tool Kit", "crop-farm",
            "Canon Part V: CropFarm (hoe, spade/shovel, fork, rake)",
            "hoe", "spade", "shovel", "fork", "rake", "hand seed sacks",
            "knives", "sharpening file");

        /// <summary>All Group B kit definitions, for catalog-driven UI and validation.</summary>
        public static List<ToolKitDefinition> All => new List<ToolKitDefinition>
        {
            CarpenterHandToolKit, MasonKit, TailorHandKit, BarberKit, DoctorBag,
            WheelwrightKit, StableGroomingKit, MinerHandKit, FarrierKit, RanchTackKit,
            FieldHandKit,
        };

        public static ToolKitDefinition Get(string kitId)
        {
            foreach (var def in All)
                if (def.KitId == kitId) return def;
            return null;
        }
    }
}
