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

        private string businessInstanceId = string.Empty;
        private string businessName = string.Empty;

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
                new SmithingRecipe("horseshoe-set", "Horseshoe set (4)", ImportCatalog.IronStockId, 2, 1, 60, FarrierSkillId),
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
            RepairPickupLedger pickupAnnounceLedger = null)
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
