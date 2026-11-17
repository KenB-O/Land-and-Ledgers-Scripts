using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using LandLedgers.Skills;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>
    /// EQU-2: a real blacksmith business (BusinessType.Blacksmith exists in the enum;
    /// created through the BIZ-1 workflow, not a menu). Canon §7.2 + Rev XI: the shop
    /// is a bundle of capabilities (smithing, farrier — plus "wheelwright-work" when
    /// it employs a qualified wheelwright, D3A), not a hard class.
    /// It both FABRICATES equipment from imported materials and REPAIRS the assets
    /// the freight/wagon/farm economy wears out — the repair demand already exists
    /// with zero suppliers until now.
    ///
    /// Skill dependence is structural (Canon §7.2): without a qualified smith
    /// (journeyman-equivalent), crafting and repair refuse — the parcel and tools
    /// alone do not produce work.
    ///
    /// D1 exception: horseshoe forging does NOT use the journeyman gate.
    /// Forging one shoe is ordinary physically feasible work, so any smith
    /// at a forge may attempt it; capability instead grades the result
    /// (ForgeHorseshoe: slower, lower-condition shoes at low capability,
    /// never a guaranteed perfect shoe). The gate still applies to every
    /// whole-unit recipe and to repair work.
    ///
    /// D2 extension: the same reasoning covers small forged construction
    /// hardware (door hinges, iron latches, door handles). One piece is
    /// one small forging from bar stock, graded by capability through
    /// ForgeHardware exactly like a shoe — slower and flawed-but-usable
    /// at low capability, never a guaranteed perfect piece. Nails are
    /// NOT forged here: the nail-forging rate is uncalibrated (E1), so
    /// nails arrive by legitimate import (LumberYardSupply registers
    /// "nails-hardware" at 30c) or from the yard's opening endowment,
    /// never from an invented smith recipe.
    /// </summary>
    public sealed class BlacksmithRuntime
    {
        public const string SmithEquipmentTaskId = "smith-equipment";
        public const string RepairWorkTaskId = "repair-work-order";
        public const string ShoeHorseTaskId = "shoe-horse";

        /// <summary>TTS-3 extension-path skills (not starters).</summary>
        public const string SmithingSkillId = "smithing";
        public const string FarrierSkillId = "farrier-work";

        /// <summary>Calibration: journeyman-equivalent floor. Tuning, not canon.</summary>
        public const int JourneymanLevelFloor = 2;

        /// <summary>D1: one horseshoe is the manufacturing and inventory unit.</summary>
        public const string HorseshoeKind = "horseshoe";

        /// <summary>
        /// D1: one material/fuel unit is four shoe-quarters. Calibration, from
        /// the recipes themselves and the historical record: one iron-stock
        /// unit is a ~5 lb bar (a plow takes 8 units for ~40 lb of iron, a
        /// hand tool 1 unit for a ~2-5 lb head, a plowshare 2 steel units
        /// for a ~10 lb share), and a working horseshoe is ~0.5-1.5 lb of
        /// iron with forging loss — four shoes per bar, ~1.25 lb of bar
        /// each. One forge-coal unit fires four shoe forgings (~1 hour of
        /// forge work). Four shoes therefore consume exactly one iron unit
        /// and one coal unit: the same totals as the retired four-shoe
        /// recipe, but each shoe is now individually produced, priced,
        /// and inventoried. A single-shoe replacement costs one quarter
        /// of each, never a four-shoe batch.
        /// </summary>
        public const int HorseshoeQuartersPerUnit = 4;

        /// <summary>D1: minutes to forge one shoe at capability 0 (complete novice).</summary>
        public const int HorseshoeNoviceMinutes = 30;

        /// <summary>D1: minutes to forge one shoe at capability 1.0 (master). A
        /// skilled farrier forges one basic shoe from bar stock in ~10-15
        /// minutes; the mid-capability figure lands at 20.</summary>
        public const int HorseshoeMasterMinutes = 10;

        /// <summary>D2: construction hardware kinds the smith forges one piece at a time.</summary>
        public const string DoorHingeKind = "door-hinge";
        public const string IronLatchKind = "iron-latch";
        public const string DoorHandleKind = "door-handle";

        /// <summary>D2: the forged construction-hardware kinds, in forge order.</summary>
        public static readonly string[] HardwareKinds = { DoorHingeKind, IronLatchKind, DoorHandleKind };

        /// <summary>D2: true when the kind is a forged construction-hardware piece.</summary>
        public static bool IsHardwareKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return false;
            foreach (string hardware in HardwareKinds)
                if (string.Equals(hardware, kind, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>D2: minutes to forge one hardware piece at capability 0 (complete novice).</summary>
        public const int HardwareNoviceMinutes = 30;

        /// <summary>D2: minutes to forge one hardware piece at capability 1.0 (master).</summary>
        public const int HardwareMasterMinutes = 10;

        private string businessInstanceId = string.Empty;
        private string businessName = string.Empty;

        // D1: cut-bar and fired-coal remainders for horseshoe forging. The
        // integer stock ledger stays the authority; a whole unit drawn from
        // stock yields four quarters, three of which wait here for the next
        // shoes. Session-scoped like materialStock — a future save pass
        // must persist these together with materialStock.
        private int ironShoeQuarters;
        private int coalShoeQuarters;
        private string ironShoeQuarterLotId = string.Empty;
        private string coalShoeQuarterLotId = string.Empty;

        /// <summary>D1: cut bar lengths on hand, waiting for the next shoe forgings (0..3).</summary>
        public int IronShoeQuartersOnHand => ironShoeQuarters;

        /// <summary>D1: fired coal quarters on hand (0..3).</summary>
        public int CoalShoeQuartersOnHand => coalShoeQuarters;

        // --- D1: capability-graded horseshoe forging ---------------------------------

        /// <summary>D1: minutes to forge one horseshoe at this capability.</summary>
        public static int HorseshoeMinutesForCapability(double capability)
            => SkillOutcomeCurves.MinutesForCapability(HorseshoeNoviceMinutes, HorseshoeMasterMinutes, capability);

        /// <summary>D1: condition of a horseshoe forged at this capability (0.60..1.00).</summary>
        public static double HorseshoeQualityForCapability(double capability)
            => SkillOutcomeCurves.QualityForCapability(capability);

        /// <summary>
        /// D1: forge ONE horseshoe. No journeyman gate — this is ordinary
        /// physically feasible work. Capability instead grades the outcome:
        /// a low-capability smith is slower and produces a flawed-but-usable
        /// shoe (Condition01 below 1.0), never a guaranteed perfect product.
        /// Each shoe consumes 1 quarter-iron + 1 quarter-coal from the
        /// pools (a whole unit is drawn from stock only when a pool is
        /// empty), so four shoes consume exactly one iron unit and one
        /// coal unit. The returned asset is shop stock; fitting is a
        /// separate call (FarrierService.FitShoes) and never happens here.
        /// Returns null and consumes nothing on any refusal.
        /// </summary>
        public EquipmentAsset ForgeHorseshoe(EntityId smithPersonId, int dayIndex, SkillService skillService, List<string> diagnostics, out int minutesUsed)
        {
            minutesUsed = 0;
            diagnostics = diagnostics ?? new List<string>();

            SmithingRecipe recipe = DefaultRecipes().Find(r => r.EquipmentKind == HorseshoeKind);
            if (recipe == null)
            {
                diagnostics.Add("BlacksmithRuntime: no horseshoe recipe configured.");
                return null;
            }

            if (!forgeStationReady)
            {
                diagnostics.Add("BlacksmithRuntime: forge station is not ready; cannot forge a horseshoe.");
                return null;
            }

            if (skillService == null)
            {
                diagnostics.Add("BlacksmithRuntime: no SkillService; cannot assess the smith's farrier skill.");
                return null;
            }

            // Sufficiency first: refuse without consuming anything.
            bool ironOk = ironShoeQuarters > 0 || MaterialOnHand(recipe.MaterialId) >= 1;
            bool coalOk = coalShoeQuarters > 0 || MaterialOnHand(ImportCatalog.ForgeCoalId) >= 1;
            if (!ironOk)
            {
                diagnostics.Add($"BlacksmithRuntime: no {recipe.MaterialId} for a horseshoe (no cut quarters, no whole unit in stock).");
                return null;
            }
            if (!coalOk)
            {
                diagnostics.Add($"BlacksmithRuntime: no {ImportCatalog.ForgeCoalId} for a horseshoe (no fired quarters, no whole unit in stock).");
                return null;
            }

            double capability = skillService.GetCapability(smithPersonId, recipe.RequiredSkillId);
            minutesUsed = HorseshoeMinutesForCapability(capability);
            double quality = HorseshoeQualityForCapability(capability);

            var lotNotes = new List<string>();
            if (!DrawForgeQuarter(recipe.MaterialId, ref ironShoeQuarters, ref ironShoeQuarterLotId, lotNotes, diagnostics)) return null;
            if (!DrawForgeQuarter(ImportCatalog.ForgeCoalId, ref coalShoeQuarters, ref coalShoeQuarterLotId, lotNotes, diagnostics)) return null;

            var asset = new EquipmentAsset
            {
                AssetId = Guid.NewGuid().ToString("N"),
                Kind = HorseshoeKind,
                DisplayName = "Horseshoe",
                Condition01 = (float)quality,
                OwnerKind = "business",
                OwnerId = businessInstanceId,
                MadeByBusinessId = businessInstanceId,
                MadeByBusinessName = businessName,
                MadeDayIndex = dayIndex,
                MaterialLotIds = new List<string>(),
            };
            if (!string.IsNullOrWhiteSpace(ironShoeQuarterLotId)) asset.MaterialLotIds.Add(ironShoeQuarterLotId);
            if (!string.IsNullOrWhiteSpace(coalShoeQuarterLotId)) asset.MaterialLotIds.Add(coalShoeQuarterLotId);
            assets[asset.AssetId] = asset;

            string lots = lotNotes.Count > 0 ? $" ({string.Join(", ", lotNotes)})" : string.Empty;
            diagnostics.Add($"BlacksmithRuntime: forged one horseshoe {asset.AssetId} (condition {quality:0.00}, {minutesUsed} min) by {smithPersonId}, capability {capability:0.00}.{lots}");
            return asset;
        }

        /// <summary>
        /// D1: release one forged horseshoe from shop stock to the caller
        /// (farrier fitting, retail sale, freight). Returns null on an
        /// unknown id, a non-horseshoe kind, or a reserved asset — the
        /// asset stays in stock in every refusal.
        /// </summary>
        public EquipmentAsset TakeHorseshoe(string assetId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(assetId))
            {
                diagnostics.Add("BlacksmithRuntime: no horseshoe id given to release.");
                return null;
            }

            if (!assets.TryGetValue(assetId, out EquipmentAsset asset) || asset == null)
            {
                diagnostics.Add($"BlacksmithRuntime: no asset '{assetId}' in shop stock.");
                return null;
            }

            if (!string.Equals(asset.Kind, HorseshoeKind, StringComparison.Ordinal))
            {
                diagnostics.Add($"BlacksmithRuntime: asset '{assetId}' is a {asset.Kind}, not a horseshoe.");
                return null;
            }

            if (asset.IsReserved)
            {
                diagnostics.Add($"BlacksmithRuntime: horseshoe '{assetId}' is reserved by {asset.ReservedBy}; release refused.");
                return null;
            }

            assets.Remove(assetId);
            diagnostics.Add($"BlacksmithRuntime: horseshoe '{assetId}' released from shop stock.");
            return asset;
        }

        // --- D2: capability-graded construction-hardware forging -------------------

        /// <summary>D2: minutes to forge one hardware piece at this capability.</summary>
        public static int HardwareMinutesForCapability(double capability)
            => SkillOutcomeCurves.MinutesForCapability(HardwareNoviceMinutes, HardwareMasterMinutes, capability);

        /// <summary>D2: condition of a hardware piece forged at this capability (0.60..1.00).</summary>
        public static double HardwareQualityForCapability(double capability)
            => SkillOutcomeCurves.QualityForCapability(capability);

        /// <summary>
        /// D2: forge ONE construction-hardware piece (door hinge, iron latch
        /// or door handle). No journeyman gate — the same D1 reasoning as a
        /// shoe: one small forging from bar stock is ordinary physically
        /// feasible work. Capability instead grades the outcome: a
        /// low-capability smith is slower and produces a flawed-but-usable
        /// piece (Condition01 below 1.0), never a guaranteed perfect
        /// product. Each piece consumes 1 quarter-iron + 1 quarter-coal
        /// from the same pools the shoes draw from (cut bar and fired coal
        /// are not shoe-specific), so four pieces consume exactly one iron
        /// unit and one coal unit — a hinge, latch or handle is ~0.5-1.5 lb
        /// of iron with forging loss, like a shoe. Calibration, provisional,
        /// for the owner to correct. The returned asset is shop stock; the
        /// yard takes it into its hardware lots through
        /// LumberYardShopRuntime.ReceiveHardwareFromBlacksmith, and the
        /// construction sale draws it from there. Nails are NOT forged
        /// here (see the class note). Returns null and consumes nothing on
        /// any refusal.
        /// </summary>
        public EquipmentAsset ForgeHardware(string hardwareKind, EntityId smithPersonId, int dayIndex, SkillService skillService, List<string> diagnostics, out int minutesUsed)
        {
            minutesUsed = 0;
            diagnostics = diagnostics ?? new List<string>();

            if (!IsHardwareKind(hardwareKind))
            {
                diagnostics.Add($"BlacksmithRuntime: '{hardwareKind}' is not a forged construction-hardware kind (door-hinge, iron-latch, door-handle).");
                return null;
            }

            SmithingRecipe recipe = DefaultRecipes().Find(r => r.EquipmentKind == hardwareKind);
            if (recipe == null)
            {
                diagnostics.Add($"BlacksmithRuntime: no recipe for '{hardwareKind}'.");
                return null;
            }

            if (!forgeStationReady)
            {
                diagnostics.Add($"BlacksmithRuntime: forge station is not ready; cannot forge a {recipe.DisplayName}.");
                return null;
            }

            if (skillService == null)
            {
                diagnostics.Add("BlacksmithRuntime: no SkillService; cannot assess the smith's smithing skill.");
                return null;
            }

            // Sufficiency first: refuse without consuming anything.
            bool ironOk = ironShoeQuarters > 0 || MaterialOnHand(recipe.MaterialId) >= 1;
            bool coalOk = coalShoeQuarters > 0 || MaterialOnHand(ImportCatalog.ForgeCoalId) >= 1;
            if (!ironOk)
            {
                diagnostics.Add($"BlacksmithRuntime: no {recipe.MaterialId} for a {recipe.DisplayName} (no cut quarters, no whole unit in stock).");
                return null;
            }
            if (!coalOk)
            {
                diagnostics.Add($"BlacksmithRuntime: no {ImportCatalog.ForgeCoalId} for a {recipe.DisplayName} (no fired quarters, no whole unit in stock).");
                return null;
            }

            double capability = skillService.GetCapability(smithPersonId, recipe.RequiredSkillId);
            minutesUsed = HardwareMinutesForCapability(capability);
            double quality = HardwareQualityForCapability(capability);

            var lotNotes = new List<string>();
            if (!DrawForgeQuarter(recipe.MaterialId, ref ironShoeQuarters, ref ironShoeQuarterLotId, lotNotes, diagnostics)) return null;
            if (!DrawForgeQuarter(ImportCatalog.ForgeCoalId, ref coalShoeQuarters, ref coalShoeQuarterLotId, lotNotes, diagnostics)) return null;

            var asset = new EquipmentAsset
            {
                AssetId = Guid.NewGuid().ToString("N"),
                Kind = hardwareKind,
                DisplayName = recipe.DisplayName,
                Condition01 = (float)quality,
                OwnerKind = "business",
                OwnerId = businessInstanceId,
                MadeByBusinessId = businessInstanceId,
                MadeByBusinessName = businessName,
                MadeDayIndex = dayIndex,
                MaterialLotIds = new List<string>(),
            };
            if (!string.IsNullOrWhiteSpace(ironShoeQuarterLotId)) asset.MaterialLotIds.Add(ironShoeQuarterLotId);
            if (!string.IsNullOrWhiteSpace(coalShoeQuarterLotId)) asset.MaterialLotIds.Add(coalShoeQuarterLotId);
            assets[asset.AssetId] = asset;

            string lots = lotNotes.Count > 0 ? $" ({string.Join(", ", lotNotes)})" : string.Empty;
            diagnostics.Add($"BlacksmithRuntime: forged one {recipe.DisplayName} {asset.AssetId} (condition {quality:0.00}, {minutesUsed} min) by {smithPersonId}, capability {capability:0.00}.{lots}");
            return asset;
        }

        /// <summary>
        /// D2: release one forged hardware piece from shop stock to the caller
        /// (yard delivery, retail sale, freight). Returns null on an unknown
        /// id, a non-hardware kind, or a reserved asset — the asset stays in
        /// stock in every refusal.
        /// </summary>
        public EquipmentAsset TakeHardware(string assetId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(assetId))
            {
                diagnostics.Add("BlacksmithRuntime: no hardware id given to release.");
                return null;
            }

            if (!assets.TryGetValue(assetId, out EquipmentAsset asset) || asset == null)
            {
                diagnostics.Add($"BlacksmithRuntime: no asset '{assetId}' in shop stock.");
                return null;
            }

            if (!IsHardwareKind(asset.Kind))
            {
                diagnostics.Add($"BlacksmithRuntime: asset '{assetId}' is a {asset.Kind}, not forged construction hardware.");
                return null;
            }

            if (asset.IsReserved)
            {
                diagnostics.Add($"BlacksmithRuntime: hardware '{assetId}' is reserved by {asset.ReservedBy}; release refused.");
                return null;
            }

            assets.Remove(assetId);
            diagnostics.Add($"BlacksmithRuntime: hardware '{assetId}' released from shop stock.");
            return asset;
        }

        /// <summary>
        /// D1/D2: draw one forge-quarter of a material. If quarters are on hand,
        /// take one. Otherwise draw one whole unit from stock (FIFO lot
        /// provenance), cut it into four quarters, and spend one of them.
        /// Returns false only when no quarter and no whole unit exist.
        /// </summary>
        private bool DrawForgeQuarter(string materialId, ref int quarters, ref string quarterLotId, List<string> lotNotes, List<string> diagnostics)
        {
            if (quarters > 0)
            {
                quarters--;
                return true;
            }

            if (MaterialOnHand(materialId) < 1)
            {
                diagnostics.Add($"BlacksmithRuntime: no {materialId} left to cut a forge-quarter from.");
                return false;
            }

            materialStock[materialId]--;
            var notes = new List<string>();
            RecordLotConsumption(materialId, 1, notes);
            string lotId = FirstLotId(notes);
            if (!string.IsNullOrWhiteSpace(lotId)) quarterLotId = lotId;
            lotNotes.AddRange(notes);
            quarters = HorseshoeQuartersPerUnit - 1;
            return true;
        }

        private static string FirstLotId(List<string> notes)
        {
            if (notes == null) return string.Empty;
            foreach (string note in notes)
            {
                if (string.IsNullOrWhiteSpace(note) || note.StartsWith("unallocated", StringComparison.Ordinal)) continue;
                int colon = note.IndexOf(':');
                if (colon > 0) return note.Substring(0, colon);
            }
            return string.Empty;
        }

        /// <summary>
        /// Tech X §3.5: the ForgeStation workstation — forge + anvil + tools.
        /// A functional space alone does not grant it; BIZ-1 premises validation
        /// establishes it at creation.
        /// </summary>
        private bool forgeStationReady;

        private readonly Dictionary<string, int> materialStock =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> consumedLotIds = new List<string>();
        /// <summary>
        /// D4K (additive): per-lot FIFO ledger shadowing materialStock — real
        /// lots in, real lots out, so every part consumed traces to a lot.
        /// Unit counts stay the authority; this ledger is provenance only.
        /// </summary>
        private readonly Dictionary<string, Queue<SmithMaterialLot>> materialLotLedger =
            new Dictionary<string, Queue<SmithMaterialLot>>(StringComparer.Ordinal);

        /// <summary>D4K: one received material lot, FIFO-consumed.</summary>
        [Serializable]
        private sealed class SmithMaterialLot
        {
            public string LotId = string.Empty;
            public string MaterialName = string.Empty;
            public string OriginName = string.Empty;
            public int Units;
        }
        private readonly Dictionary<string, EquipmentAsset> assets =
            new Dictionary<string, EquipmentAsset>(StringComparer.Ordinal);
        private readonly RepairQueue repairQueue = new RepairQueue();
        /// <summary>D4K: the shop's recorded quote book (accept/decline, never silent billing).</summary>
        private readonly RepairQuoteBook quoteBook = new RepairQuoteBook();
        /// <summary>D4K: the shop's pickup ledger (ready / picked up / delivered / unclaimed).</summary>
        private readonly RepairPickupLedger pickupLedger = new RepairPickupLedger();
        /// <summary>D4K: the shop's artisan-lien register (recorded claims on unpaid work).</summary>
        private readonly ArtisanLienRegister lienRegister = new ArtisanLienRegister();
        private int nextAssetNumber = 1;
        private int repairRevenueCents;

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;
        public string BusinessName => businessName ?? string.Empty;
        public bool ForgeStationReady => forgeStationReady;
        /// <summary>EQP-2: true once readiness derives from actual components (Tech X §3.5).</summary>
        public bool ForgeStationDerivedFromComponents { get; private set; }
        public RepairQueue Repairs => repairQueue;
        /// <summary>D4K: the shop's recorded quote book.</summary>
        public RepairQuoteBook RepairQuotes => quoteBook;
        /// <summary>D4K: the shop's pickup ledger.</summary>
        public RepairPickupLedger RepairPickups => pickupLedger;
        /// <summary>D4K: the shop's artisan-lien register.</summary>
        public ArtisanLienRegister RepairLiens => lienRegister;
        public int RepairRevenueCents => Mathf.Max(0, repairRevenueCents);
        public IReadOnlyDictionary<string, EquipmentAsset> Assets => assets;

        public BlacksmithRuntime() { }

        public BlacksmithRuntime(string businessInstanceId, string businessName, bool forgeStationReady)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.businessName = businessName ?? businessName ?? string.Empty;
            this.forgeStationReady = forgeStationReady;
        }

        /// <summary>
        /// EQP-2: derives forge-station readiness from actual components
        /// (Tech X §3.5), replacing the constructor flag. A building alone never
        /// grants the workstation. Returns null when ready, else the reason.
        /// Businesses that never call this keep the legacy constructor flag.
        /// </summary>
        public string EstablishForgeStationFromComponents(
            List<EquipmentAsset> assets, string spaceId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var def = WorkstationCatalog.ForgeStation;
            var station = new WorkstationInstance
            {
                InstanceId = "forge-station-" + businessInstanceId,
                WorkstationId = def.WorkstationId,
                BusinessInstanceId = businessInstanceId,
                SpaceId = spaceId ?? string.Empty,
            };
            var byId = new Dictionary<string, EquipmentAsset>(StringComparer.Ordinal);
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) continue;
                    station.InstallComponent(asset.AssetId);
                    byId[asset.AssetId] = asset;
                }
            }
            string reason = station.EvaluateReady(def, id =>
            {
                if (!byId.TryGetValue(id, out var asset) || asset == null)
                    return (WorkstationComponentView?)null;
                return new WorkstationComponentView
                {
                    AssetId = asset.AssetId,
                    Kind = asset.Kind,
                    Condition01 = asset.Condition01,
                    IsUsable = asset.IsUsable,
                };
            }, diagnostics);
            forgeStationReady = reason == null;
            ForgeStationDerivedFromComponents = true;
            return reason;
        }

        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("BlacksmithRuntime: no SkillService — smithing/farrier skills not registered.");
                return;
            }
            RegisterOne(skillService,
                new SkillDefinition(SmithingSkillId, "Smithing",
                    "Forge work: fabrication, tool/implement repair, wagon ironwork. (TTS-3 extension path.)"),
                diagnostics);
            RegisterOne(skillService,
                new SkillDefinition(FarrierSkillId, "Farrier work",
                    "Horseshoeing and hoof care. Recurring because the horse-powered economy wears shoes. (TTS-3 extension path.)"),
                diagnostics);
        }

        private static void RegisterOne(SkillService skillService, SkillDefinition def, List<string> diagnostics)
        {
            if (skillService.GetSkill(def.SkillId) != null) return;
            if (!skillService.RegisterSkill(def, out string rejection))
                diagnostics.Add($"BlacksmithRuntime: skill '{def.SkillId}' rejected: {rejection}");
        }

        /// <summary>Craft recipes are data. Minutes are calibration (Canon Part XV).</summary>
        public static List<SmithingRecipe> DefaultRecipes()
        {
            return new List<SmithingRecipe>
            {
                new SmithingRecipe("plow", "Moldboard plow", ImportCatalog.IronStockId, 8, 2, 240, SmithingSkillId),
                new SmithingRecipe("harrow", "Spike-tooth harrow", ImportCatalog.IronStockId, 6, 2, 180, SmithingSkillId),
                // One shoe per forging, priced in quarters (see HorseshoeQuartersPerUnit).
                // MinutesAtLevel1 is the novice (capability 0.10) baseline that
                // ForgeHorseshoe scales by capability.
                new SmithingRecipe(HorseshoeKind, "Horseshoe", ImportCatalog.IronStockId, 0, 0, 28, FarrierSkillId)
                { MaterialQuartersPerItem = 1, FuelQuartersPerItem = 1 },
                // D2: one hardware piece per forging, priced in quarters like a
                // shoe. MinutesAtLevel1 is the novice baseline that
                // ForgeHardware scales by capability. Smithing skill: hinges,
                // latches and handles are forge work, not farrier work.
                new SmithingRecipe(DoorHingeKind, "Door hinge", ImportCatalog.IronStockId, 0, 0, 30, SmithingSkillId)
                { MaterialQuartersPerItem = 1, FuelQuartersPerItem = 1 },
                new SmithingRecipe(IronLatchKind, "Iron latch", ImportCatalog.IronStockId, 0, 0, 30, SmithingSkillId)
                { MaterialQuartersPerItem = 1, FuelQuartersPerItem = 1 },
                new SmithingRecipe(DoorHandleKind, "Door handle", ImportCatalog.IronStockId, 0, 0, 30, SmithingSkillId)
                { MaterialQuartersPerItem = 1, FuelQuartersPerItem = 1 },
                new SmithingRecipe("wagon-part", "Wagon ironwork (tire/shoe)", ImportCatalog.IronStockId, 4, 1, 120, SmithingSkillId),
                new SmithingRecipe("hand-tool", "Hand tool", ImportCatalog.IronStockId, 1, 1, 45, SmithingSkillId),
                new SmithingRecipe("plowshare", "Replacement plowshare", ImportCatalog.SteelStockId, 2, 1, 90, SmithingSkillId),
            };
        }

        /// <summary>Receives imported material lots into the smith's stock.</summary>
        public void ReceiveMaterial(ImportLot lot, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (lot == null || lot.Units <= 0)
            {
                diagnostics.Add("BlacksmithRuntime: cannot receive an empty material lot.");
                return;
            }
            if (!materialStock.TryGetValue(lot.MaterialId, out int onHand))
                onHand = 0;
            materialStock[lot.MaterialId] = onHand + lot.Units;
            // D4K (additive): the lot joins the FIFO provenance ledger.
            if (!materialLotLedger.TryGetValue(lot.MaterialId, out Queue<SmithMaterialLot> lots))
            {
                lots = new Queue<SmithMaterialLot>();
                materialLotLedger[lot.MaterialId] = lots;
            }
            lots.Enqueue(new SmithMaterialLot
            {
                LotId = lot.LotId ?? string.Empty,
                MaterialName = lot.MaterialName ?? string.Empty,
                OriginName = lot.OriginName ?? string.Empty,
                Units = lot.Units,
            });
            diagnostics.Add($"BlacksmithRuntime: received {lot.Units}x {lot.MaterialName} ({lot.LotId}, {lot.OriginName}).");
        }

        public int MaterialOnHand(string materialId)
        {
            return materialStock.TryGetValue(materialId, out int units) ? units : 0;
        }

        /// <summary>
        /// D4K (additive): units of one LOT on hand — provenance read, never
        /// the authority for sufficiency (MaterialOnHand is).
        /// </summary>
        public int LotUnitsOnHand(string materialId, string lotId)
        {
            if (!materialLotLedger.TryGetValue(materialId, out Queue<SmithMaterialLot> lots))
                return 0;
            foreach (SmithMaterialLot lot in lots)
                if (string.Equals(lot.LotId, lotId, StringComparison.Ordinal))
                    return Math.Max(0, lot.Units);
            return 0;
        }

        /// <summary>D4K (additive): lot ids on hand for a material, FIFO order.</summary>
        public List<string> SmithLotIdsOnHand(string materialId)
        {
            var ids = new List<string>();
            if (!materialLotLedger.TryGetValue(materialId, out Queue<SmithMaterialLot> lots))
                return ids;
            foreach (SmithMaterialLot lot in lots)
                if (lot.Units > 0 && !ids.Contains(lot.LotId))
                    ids.Add(lot.LotId);
            return ids;
        }

        /// <summary>
        /// D4K (additive): records FIFO lot consumption against the ledger and
        /// reports the consumed lot ids. Never refuses — the caller already
        /// verified sufficiency against MaterialOnHand; this is provenance
        /// bookkeeping only. Units the ledger cannot trace are reported as
        /// "unallocated" (a data smell, never silent).
        /// </summary>
        public void RecordLotConsumption(string materialId, int units, List<string> outConsumedLotIds)
        {
            outConsumedLotIds = outConsumedLotIds ?? new List<string>();
            int remaining = Math.Max(0, units);
            if (materialLotLedger.TryGetValue(materialId, out Queue<SmithMaterialLot> lots))
            {
                foreach (SmithMaterialLot lot in lots)
                {
                    if (remaining <= 0) break;
                    if (lot.Units <= 0) continue;
                    int take = Math.Min(lot.Units, remaining);
                    lot.Units -= take;
                    remaining -= take;
                    consumedLotIds.Add(lot.LotId);
                    outConsumedLotIds.Add($"{lot.LotId}:{take}");
                }
            }
            if (remaining > 0)
                outConsumedLotIds.Add($"unallocated:{remaining}");
        }

        /// <summary>
        /// Forges one piece of equipment. Task-driven in spirit: the caller assigns
        /// the smith-equipment TTS task; this records the outcome. Requires the forge
        /// station, materials + fuel, and a qualified smith — never the building alone.
        /// </summary>
        public EquipmentAsset CraftEquipment(
            string equipmentKind,
            EntityId smithPersonId,
            int dayIndex,
            SkillService skillService,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            SmithingRecipe recipe = null;
            foreach (SmithingRecipe r in DefaultRecipes())
                if (string.Equals(r.EquipmentKind, equipmentKind, StringComparison.Ordinal))
                { recipe = r; break; }
            if (recipe == null)
            {
                diagnostics.Add($"BlacksmithRuntime: no recipe for '{equipmentKind}'.");
                return null;
            }
            // D1/D2: fractional (quarter-priced) recipes are forged one item
            // at a time through ForgeHorseshoe (shoes) or ForgeHardware
            // (hinges, latches, handles) — this whole-unit path cannot mint
            // them, or a caller could spend 0 whole units for a perfect shoe.
            if (recipe.MaterialQuartersPerItem > 0 || recipe.FuelQuartersPerItem > 0)
            {
                string forgeMethod = IsHardwareKind(equipmentKind) ? "ForgeHardware" : "ForgeHorseshoe";
                diagnostics.Add($"BlacksmithRuntime: '{equipmentKind}' is priced in quarters; forge one at a time with {forgeMethod}.");
                return null;
            }
            if (!forgeStationReady)
            {
                diagnostics.Add("BlacksmithRuntime: no ForgeStation — a building alone does not grant the workstation (Tech X §3.5).");
                return null;
            }
            if (skillService == null)
            {
                diagnostics.Add("BlacksmithRuntime: no SkillService — cannot verify the smith.");
                return null;
            }
            int level = skillService.GetLevel(smithPersonId, recipe.RequiredSkillId);
            if (level < JourneymanLevelFloor)
            {
                diagnostics.Add(
                    $"BlacksmithRuntime: smith {smithPersonId} is level {level} in {recipe.RequiredSkillId} " +
                    $"(needs {JourneymanLevelFloor}) — without a real journeyman the work collapses (Canon §7.2).");
                return null;
            }
            if (MaterialOnHand(recipe.MaterialId) < recipe.MaterialUnits)
            {
                diagnostics.Add($"BlacksmithRuntime: short {recipe.MaterialUnits - MaterialOnHand(recipe.MaterialId)}x {recipe.MaterialId} for '{equipmentKind}'.");
                return null;
            }
            if (MaterialOnHand(ImportCatalog.ForgeCoalId) < recipe.FuelUnits)
            {
                diagnostics.Add($"BlacksmithRuntime: short {recipe.FuelUnits - MaterialOnHand(ImportCatalog.ForgeCoalId)}x forge coal for '{equipmentKind}'.");
                return null;
            }

            materialStock[recipe.MaterialId] -= recipe.MaterialUnits;
            materialStock[ImportCatalog.ForgeCoalId] -= recipe.FuelUnits;

            // Pass VIII (production networks): the FIFO provenance ledger must
            // mirror the unit decrement — CompleteRepair already records lot
            // consumption, but CraftEquipment omitted it, so LotUnitsOnHand
            // kept reporting units that were already forged into equipment
            // (ledger divergence: provenance said the iron was still on hand).
            var lotNotes = new List<string>();
            RecordLotConsumption(recipe.MaterialId, recipe.MaterialUnits, lotNotes);
            var fuelNotes = new List<string>();
            RecordLotConsumption(ImportCatalog.ForgeCoalId, recipe.FuelUnits, fuelNotes);
            diagnostics.Add(
                $"BlacksmithRuntime: crafted {recipe.EquipmentKind} consumed lots " +
                $"[{string.Join(", ", lotNotes.ToArray())}] + fuel [{string.Join(", ", fuelNotes.ToArray())}].");

            var asset = new EquipmentAsset
            {
                AssetId = $"EQ-{businessInstanceId}-{nextAssetNumber++:D3}",
                Kind = recipe.EquipmentKind,
                DisplayName = recipe.DisplayName,
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = businessInstanceId,
                MadeByBusinessId = businessInstanceId,
                MadeByBusinessName = businessName,
                MadeDayIndex = dayIndex,
            };
            assets[asset.AssetId] = asset;
            diagnostics.Add(
                $"BlacksmithRuntime: forged {asset.DisplayName} ({asset.AssetId}) from " +
                $"{recipe.MaterialUnits}x {recipe.MaterialId} + {recipe.FuelUnits}x coal — provenance carries to the field.");
            return asset;
        }

        /// <summary>
        /// Completes agreed repair work: consumes materials, needs the skilled smith,
        /// restores the asset, books the revenue. The customer's payment is their
        /// ledger outflow with provenance — repair is priced labor + parts, never free.
        /// </summary>
        public string CompleteRepair(
            string workOrderId,
            EquipmentAsset asset,
            EntityId smithPersonId,
            int dayIndex,
            SkillService skillService,
            LandLedgers.Population.HouseholdLedger customerLedger,
            List<string> diagnostics,
            EquipmentTaskGate gate = null,
            // D4K (additive, default-off): when work completes UNPAID
            // (customerLedger == null), record the artisan's lien on the
            // shop's register; when supplied, announce the finished repair
            // on the pickup ledger.
            ArtisanLienRegister unpaidLienRegister = null,
            RepairPickupLedger pickupAnnounceLedger = null,
            // Pass VIII (production networks, additive, default-off): when
            // wired, the agreed repair price is credited to the shop's business
            // cash as transfer revenue with provenance (the one-cash-writer
            // rule: the price leaves the customer ledger above AND lands in
            // the business cash here — never one side alone). Callers that
            // post revenue through their own settlement path leave this null.
            LandLedgers.Economy.BusinessRuntimeState businessCash = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            // NX-1A: Canon 4.1 — repair work happens at the forge station
            // workstation (established from components, Tech X §3.5).
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Workstation("forge-station") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return blocked;
            }

            RepairWorkOrder order = null;
            foreach (RepairWorkOrder o in repairQueue.Orders)
                if (string.Equals(o.WorkOrderId, workOrderId, StringComparison.Ordinal))
                { order = o; break; }
            if (order == null) return $"BlacksmithRuntime: unknown work order '{workOrderId}'.";
            if (order.Status != RepairOrderStatus.Quoted || !order.TermsAgreed)
                return $"BlacksmithRuntime: {workOrderId} is {order.Status} — terms must be agreed first.";
            if (skillService == null) return "BlacksmithRuntime: no SkillService — cannot verify the smith.";

            string neededSkill = string.Equals(order.RequiredCapability, "farrier", StringComparison.OrdinalIgnoreCase)
                ? FarrierSkillId : SmithingSkillId;
            int level = skillService.GetLevel(smithPersonId, neededSkill);
            if (level < JourneymanLevelFloor)
                return $"BlacksmithRuntime: smith {smithPersonId} is level {level} in {neededSkill} (needs {JourneymanLevelFloor}).";

            if (!string.IsNullOrWhiteSpace(order.MaterialId) && order.MaterialUnitsNeeded > 0)
            {
                if (MaterialOnHand(order.MaterialId) < order.MaterialUnitsNeeded)
                    return $"BlacksmithRuntime: short {order.MaterialUnitsNeeded - MaterialOnHand(order.MaterialId)}x {order.MaterialId} for {workOrderId}.";
                materialStock[order.MaterialId] -= order.MaterialUnitsNeeded;
                // D4K (additive): provenance — the consumed units trace to real lots.
                var consumedLotNotes = new List<string>();
                RecordLotConsumption(order.MaterialId, order.MaterialUnitsNeeded, consumedLotNotes);
                diagnostics.Add(
                    $"BlacksmithRuntime: {workOrderId} consumed {order.MaterialUnitsNeeded}x {order.MaterialId} " +
                    $"from lots [{string.Join(", ", consumedLotNotes.ToArray())}].");
            }

            if (asset != null)
                asset.RepairTo(1f);

            if (customerLedger != null && order.AgreedPriceCents > 0)
            {
                string rejection = customerLedger.RecordOutflow(dayIndex, order.AgreedPriceCents,
                    $"repair {order.WorkOrderId}: {order.AssetDescription} ({order.DiagnosedProblem})", businessName);
                if (rejection != null)
                    return $"BlacksmithRuntime: customer payment failed — {rejection}";
                // Pass VIII: the price that just left the customer ledger lands
                // in the shop's business cash — conservation, both sides.
                if (businessCash != null)
                    businessCash.AddWeeklyLocalTransferRevenue(order.AgreedPriceCents,
                        $"repair {order.WorkOrderId}: {order.AssetDescription} ({order.DiagnosedProblem})");
            }

            order.Status = RepairOrderStatus.Complete;
            order.CompletedDayIndex = dayIndex;
            repairRevenueCents += order.AgreedPriceCents;
            diagnostics.Add($"BlacksmithRuntime: {workOrderId} complete — {order.AssetDescription} repaired, {order.AgreedPriceCents}c booked.");

            // D4K (additive, default-off): unpaid completion secures the bill
            // with a recorded artisan's lien; the finished item is announced
            // ready for pickup/delivery.
            if (customerLedger == null && unpaidLienRegister != null && order.AgreedPriceCents > 0)
                unpaidLienRegister.RecordClaim(order, BusinessInstanceId, BusinessName, dayIndex, diagnostics);
            if (pickupAnnounceLedger != null)
                pickupAnnounceLedger.NotifyReady(order, dayIndex, diagnostics);
            return null;
        }
    }
}
