using System;
using System.Collections.Generic;
using EntityId = LandLedgers.Primitives.EntityId;
using UnityEngine;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using LandLedgers.Skills;

namespace LandLedgers.Economy.Wheelwright
{
    /// <summary>
    /// One wheel/wagon build recipe. Units and minutes are calibration
    /// (Canon Part XV), not canon; the SHAPE (lumber + blacksmith ironwork +
    /// journeyman wheelwright) is canon (Rev XXVII Part V: Wheelwright /
    /// wagon maker; iron tires bent and shrunk at forge access).
    /// </summary>
    [Serializable]
    public sealed class WheelwrightBuildRecipe
    {
        public string EquipmentKind = string.Empty;
        public string DisplayName = string.Empty;
        public int LumberUnits;
        public int IronworkParts; // blacksmith "wagon-part" assets consumed per build
        public int LaborMinutes;
        public string RequiredSkillId = WheelwrightRuntime.WheelwrightingSkillId;

        public WheelwrightBuildRecipe() { }

        public WheelwrightBuildRecipe(string kind, string displayName, int lumberUnits,
            int ironworkParts, int laborMinutes)
        {
            EquipmentKind = kind ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            LumberUnits = Math.Max(0, lumberUnits);
            IronworkParts = Math.Max(0, ironworkParts);
            LaborMinutes = Math.Max(0, laborMinutes);
        }
    }

    /// <summary>
    /// W6a: a real wheelwright business (BusinessType.Wheelwright = 17 exists;
    /// created through the BIZ-1 workflow, not a menu). Canon §7.2: the shop is a
    /// bundle of capabilities (wheel making, wagon building, wagon repair), not a
    /// hard class. It BUILDS wheels and wagons from lumber lots + blacksmith
    /// ironwork (upstream provenance: every input traces to a real lot/supplier)
    /// and REPAIRS the wagons the freight/draft economy wears out.
    ///
    /// Repair work goes through the SHARED work-order authority
    /// (Tech X §6.4): the Blacksmith namespace's RepairQueue, with
    /// RequiredCapability "wheelwright-work" — the same lifecycle the smith uses.
    ///
    /// Skill dependence is structural (Canon §7.2): without a qualified
    /// wheelwright (journeyman-equivalent), building and repair refuse — the
    /// shop and the wheel jig alone do not produce work.
    /// </summary>
    public sealed class WheelwrightRuntime
    {
        public const string BuildWagonTaskId = "wheelwright-build";
        public const string RepairWorkTaskId = "wheelwright-repair-order";

        /// <summary>TTS-3 extension-path skill.</summary>
        public const string WheelwrightingSkillId = "wheelwrighting";

        /// <summary>Capability code stamped on work orders (Tech X §6.4).</summary>
        public const string WheelwrightCapabilityCode = "wheelwright-work";

        /// <summary>Calibration: journeyman-equivalent floor. Tuning, not canon.</summary>
        public const int JourneymanLevelFloor = 2;

        private string businessInstanceId = string.Empty;
        private string businessName = string.Empty;

        /// <summary>
        /// Tech X §3.5: the wheel station (wheel jig + workbench + woodworking
        /// tools). A functional space alone does not grant it; BIZ-1 premises
        /// validation establishes it at creation via EstablishWheelStationFromComponents.
        /// </summary>
        private bool wheelStationReady;

        private readonly Dictionary<string, int> lumberStock =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> consumedLumberLotIds = new List<string>();
        private readonly List<EquipmentAsset> ironworkStock = new List<EquipmentAsset>();
        private readonly Dictionary<string, EquipmentAsset> builtAssets =
            new Dictionary<string, EquipmentAsset>(StringComparer.Ordinal);
        private readonly RepairQueue repairQueue = new RepairQueue();
        private int nextAssetNumber = 1;
        private int nextIronworkNumber = 1;
        private int buildRevenueCents;
        private int repairRevenueCents;

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;
        public string BusinessName => businessName ?? string.Empty;
        public bool WheelStationReady => wheelStationReady;
        /// <summary>EQP-2: true once readiness derives from actual components (Tech X §3.5).</summary>
        public bool WheelStationDerivedFromComponents { get; private set; }
        public RepairQueue Repairs => repairQueue;
        public int BuildRevenueCents => Mathf.Max(0, buildRevenueCents);
        public int RepairRevenueCents => Mathf.Max(0, repairRevenueCents);
        public IReadOnlyDictionary<string, EquipmentAsset> BuiltAssets => builtAssets;
        public int IronworkPartsOnHand => ironworkStock.Count;

        public WheelwrightRuntime() { }

        public WheelwrightRuntime(string businessInstanceId, string businessName, bool wheelStationReady)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.businessName = businessName ?? businessName ?? string.Empty;
            this.wheelStationReady = wheelStationReady;
        }

        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("WheelwrightRuntime: no SkillService — wheelwrighting skill not registered.");
                return;
            }
            if (skillService.GetSkill(WheelwrightingSkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(WheelwrightingSkillId, "Wheelwrighting",
                        "Wheel making, wagon building, and wagon repair. (TTS-3 extension path.)"),
                    out rejection))
                {
                    diagnostics.Add($"WheelwrightRuntime: skill '{WheelwrightingSkillId}' rejected: {rejection}");
                }
            }
        }

        /// <summary>
        /// EQP-2: derives wheel-station readiness from actual components
        /// (Tech X §3.5). A building alone never grants the workstation. Returns
        /// null when ready, else the reason. Businesses that never call this keep
        /// the legacy constructor flag.
        /// </summary>
        public string EstablishWheelStationFromComponents(
            List<EquipmentAsset> assets, string spaceId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var def = WorkstationCatalog.WheelwrightStation;
            var station = new WorkstationInstance
            {
                InstanceId = "wheelwright-station-" + businessInstanceId,
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
            wheelStationReady = reason == null;
            WheelStationDerivedFromComponents = true;
            return reason;
        }

        /// <summary>Build recipes are data. Minutes/units are calibration (Canon Part XV).</summary>
        public static List<WheelwrightBuildRecipe> DefaultRecipes()
        {
            return new List<WheelwrightBuildRecipe>
            {
                new WheelwrightBuildRecipe("farm-wagon", "Farm wagon", 40, 2, 480),
                new WheelwrightBuildRecipe("freight-wagon", "Freight wagon", 60, 3, 720),
                new WheelwrightBuildRecipe("spring-wagon", "Spring wagon", 32, 2, 420),
                new WheelwrightBuildRecipe("wagon-wheel", "Wagon wheel (spare)", 6, 1, 120),
            };
        }

        /// <summary>Receives a lumber lot into the shop's stock (provenance kept per lot).</summary>
        public void ReceiveLumberLot(ImportLot lot, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (lot == null || lot.Units <= 0)
            {
                diagnostics.Add("WheelwrightRuntime: cannot receive an empty lumber lot.");
                return;
            }
            if (!string.Equals(lot.MaterialId, ImportCatalog.LumberId, StringComparison.Ordinal))
            {
                diagnostics.Add($"WheelwrightRuntime: '{lot.MaterialId}' is not lumber — refused (Tech X §6.1: lots are typed).");
                return;
            }
            if (!lumberStock.TryGetValue(lot.LotId, out int onHand))
                onHand = 0;
            lumberStock[lot.LotId] = onHand + lot.Units;
            diagnostics.Add($"WheelwrightRuntime: received {lot.Units}x {lot.MaterialName} ({lot.LotId}, {lot.OriginName}).");
        }

        public int LumberOnHand(string lotId)
        {
            return lumberStock.TryGetValue(lotId, out int units) ? units : 0;
        }

        public int TotalLumberOnHand()
        {
            int total = 0;
            foreach (var kv in lumberStock) total += kv.Value;
            return total;
        }

        /// <summary>
        /// Receives a blacksmith-made wagon ironwork part ("wagon-part" recipe:
        /// tire/shoe) into the shop's stock. Upstream provenance: the part's maker
        /// and lot history flow into every wagon built with it. Refuses parts
        /// that are not wagon ironwork — no mystery hardware.
        /// </summary>
        public string ReceiveIronworkPart(EquipmentAsset part, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (part == null)
                return "WheelwrightRuntime: no ironwork part supplied.";
            if (!string.Equals(part.Kind, "wagon-part", StringComparison.Ordinal))
                return $"WheelwrightRuntime: part '{part.AssetId}' is kind '{part.Kind}', not wagon-part — refused.";
            if (string.IsNullOrWhiteSpace(part.MadeByBusinessId))
                return $"WheelwrightRuntime: part '{part.AssetId}' has no maker — upstream provenance required (FVS doctrine).";
            part.AssetId = $"IRONWORK-{businessInstanceId}-{nextIronworkNumber++:D3}";
            ironworkStock.Add(part);
            diagnostics.Add($"WheelwrightRuntime: received ironwork {part.AssetId} from {part.MadeByBusinessName} ({part.MadeByBusinessId}).");
            return null;
        }

        private int VerifyCraftReady(WheelwrightBuildRecipe recipe, EntityId wheelwrightPersonId,
            SkillService skillService, List<string> diagnostics)
        {
            if (recipe == null) { diagnostics.Add("WheelwrightRuntime: no recipe supplied."); return -1; }
            if (!wheelStationReady)
            {
                diagnostics.Add("WheelwrightRuntime: no wheel station — a building alone does not grant the workstation (Tech X §3.5).");
                return -1;
            }
            if (skillService == null)
            {
                diagnostics.Add("WheelwrightRuntime: no SkillService — cannot verify the wheelwright.");
                return -1;
            }
            int level = skillService.GetLevel(wheelwrightPersonId, recipe.RequiredSkillId);
            if (level < JourneymanLevelFloor)
            {
                diagnostics.Add(
                    $"WheelwrightRuntime: {wheelwrightPersonId} is level {level} in {recipe.RequiredSkillId} " +
                    $"(needs {JourneymanLevelFloor}) — without a real journeyman the work collapses (Canon §7.2).");
                return -1;
            }
            if (TotalLumberOnHand() < recipe.LumberUnits)
            {
                diagnostics.Add($"WheelwrightRuntime: short {recipe.LumberUnits - TotalLumberOnHand()}x lumber for '{recipe.EquipmentKind}'.");
                return -1;
            }
            if (ironworkStock.Count < recipe.IronworkParts)
            {
                diagnostics.Add($"WheelwrightRuntime: short {recipe.IronworkParts - ironworkStock.Count}x blacksmith ironwork parts for '{recipe.EquipmentKind}' — the smith is upstream (FVS doctrine).");
                return -1;
            }
            return level;
        }

        /// <summary>
        /// Builds one wheel/wagon. Consumes lumber (oldest lots first — Tech X §6.1
        /// lot discipline) + blacksmith ironwork parts. The built asset carries
        /// full provenance: lumber lot ids + ironwork part ids + their makers.
        /// Requires the wheel station, materials, and a qualified wheelwright —
        /// never the building alone.
        /// </summary>
        public EquipmentAsset BuildWagon(
            string equipmentKind,
            EntityId wheelwrightPersonId,
            int dayIndex,
            SkillService skillService,
            EquipmentTaskGate gate,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            WheelwrightBuildRecipe recipe = null;
            foreach (WheelwrightBuildRecipe r in DefaultRecipes())
                if (string.Equals(r.EquipmentKind, equipmentKind, StringComparison.Ordinal))
                { recipe = r; break; }
            if (recipe == null)
            {
                diagnostics.Add($"WheelwrightRuntime: no build recipe for '{equipmentKind}'.");
                return null;
            }

            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Kit("wheelwright-kit") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null)
                {
                    diagnostics.Add("WheelwrightRuntime: refused — " + blocked);
                    return null;
                }
            }

            if (VerifyCraftReady(recipe, wheelwrightPersonId, skillService, diagnostics) < 0)
                return null;

            // Consume lumber: oldest lots first.
            int remaining = recipe.LumberUnits;
            var sortedLots = new List<string>(lumberStock.Keys);
            sortedLots.Sort(StringComparer.Ordinal);
            foreach (string lotId in sortedLots)
            {
                if (remaining <= 0) break;
                int take = Math.Min(lumberStock[lotId], remaining);
                lumberStock[lotId] -= take;
                remaining -= take;
                consumedLumberLotIds.Add(lotId);
            }

            // Consume ironwork parts (FIFO); provenance flows into the wagon.
            var consumedParts = new List<EquipmentAsset>();
            for (int i = 0; i < recipe.IronworkParts; i++)
            {
                EquipmentAsset part = ironworkStock[0];
                ironworkStock.RemoveAt(0);
                consumedParts.Add(part);
            }

            var asset = new EquipmentAsset
            {
                AssetId = $"WG-{businessInstanceId}-{nextAssetNumber++:D3}",
                Kind = recipe.EquipmentKind,
                DisplayName = recipe.DisplayName,
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = businessInstanceId,
                MadeByBusinessId = businessInstanceId,
                MadeByBusinessName = businessName,
                MadeDayIndex = dayIndex,
            };
            var seenLots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string lotId in consumedLumberLotIds)
                if (seenLots.Add(lotId)) asset.MaterialLotIds.Add(lotId);
            foreach (EquipmentAsset part in consumedParts)
            {
                asset.MaterialLotIds.Add($"blacksmith-part:{part.AssetId}:{part.MadeByBusinessId}");
                foreach (string pl in part.MaterialLotIds)
                    if (seenLots.Add(pl)) asset.MaterialLotIds.Add(pl);
            }
            builtAssets[asset.AssetId] = asset;

            diagnostics.Add(
                $"WheelwrightRuntime: built {asset.DisplayName} ({asset.AssetId}) from " +
                $"{recipe.LumberUnits}x lumber + {recipe.IronworkParts}x blacksmith ironwork — provenance carries to the field.");
            return asset;
        }

        /// <summary>
        /// Completes agreed wagon repair work: consumes lumber + ironwork per the
        /// work order, needs the skilled wheelwright, restores the asset, books
        /// the revenue. The customer's payment is their ledger outflow —
        /// repair is priced labor + parts, never free (pricing policy: W6b).
        /// </summary>
        public string CompleteWagonRepair(
            string workOrderId,
            EquipmentAsset asset,
            EntityId wheelwrightPersonId,
            int dayIndex,
            SkillService skillService,
            LandLedgers.Population.HouseholdLedger customerLedger,
            List<string> diagnostics,
            EquipmentTaskGate gate = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            // Canon 4.1 — wagon repair happens at the wheel station workstation
            // (established from components, Tech X §3.5).
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Workstation("wheelwright-station") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return blocked;
            }

            RepairWorkOrder order = null;
            foreach (RepairWorkOrder o in repairQueue.Orders)
                if (string.Equals(o.WorkOrderId, workOrderId, StringComparison.Ordinal))
                { order = o; break; }
            if (order == null) return $"WheelwrightRuntime: unknown work order '{workOrderId}'.";
            if (order.Status != RepairOrderStatus.Quoted || !order.TermsAgreed)
                return $"WheelwrightRuntime: {workOrderId} is {order.Status} — terms must be agreed first.";
            if (!string.Equals(order.RequiredCapability, WheelwrightCapabilityCode, StringComparison.OrdinalIgnoreCase))
                return $"WheelwrightRuntime: {workOrderId} needs '{order.RequiredCapability}', not wheelwright work — refused.";
            if (skillService == null) return "WheelwrightRuntime: no SkillService — cannot verify the wheelwright.";

            int level = skillService.GetLevel(wheelwrightPersonId, WheelwrightingSkillId);
            if (level < JourneymanLevelFloor)
                return $"WheelwrightRuntime: {wheelwrightPersonId} is level {level} in {WheelwrightingSkillId} (needs {JourneymanLevelFloor}).";

            if (order.MaterialUnitsNeeded > 0)
            {
                if (!string.Equals(order.MaterialId, ImportCatalog.LumberId, StringComparison.Ordinal))
                    return $"WheelwrightRuntime: {workOrderId} calls for '{order.MaterialId}' — this shop works lumber + ironwork only.";
                if (TotalLumberOnHand() < order.MaterialUnitsNeeded)
                    return $"WheelwrightRuntime: short {order.MaterialUnitsNeeded - TotalLumberOnHand()}x lumber for {workOrderId}.";
                int remaining = order.MaterialUnitsNeeded;
                var sortedLots = new List<string>(lumberStock.Keys);
                sortedLots.Sort(StringComparer.Ordinal);
                foreach (string lotId in sortedLots)
                {
                    if (remaining <= 0) break;
                    int take = Math.Min(lumberStock[lotId], remaining);
                    lumberStock[lotId] -= take;
                    remaining -= take;
                    consumedLumberLotIds.Add(lotId);
                }
            }

            if (asset != null)
                asset.RepairTo(1f);
            if (asset != null)
                asset.RecordMaintenance($"wheelwright repair — {order.DiagnosedProblem} ({workOrderId}).", dayIndex);

            if (customerLedger != null && order.AgreedPriceCents > 0)
            {
                string rejection = customerLedger.RecordOutflow(dayIndex, order.AgreedPriceCents,
                    $"wagon repair {order.WorkOrderId}: {order.AssetDescription} ({order.DiagnosedProblem})", businessName);
                if (rejection != null)
                    return $"WheelwrightRuntime: customer payment failed — {rejection}";
            }

            order.Status = RepairOrderStatus.Complete;
            order.CompletedDayIndex = dayIndex;
            repairRevenueCents += order.AgreedPriceCents;
            diagnostics.Add($"WheelwrightRuntime: {workOrderId} complete — {order.AssetDescription} repaired, {order.AgreedPriceCents}c booked.");
            return null;
        }

        /// <summary>
        /// Records a build sale to a buyer (new wagons, not just repair —
        /// W6 depth). The buyer's payment is their ledger outflow.
        /// </summary>
        public string RecordBuildSale(
            EquipmentAsset asset,
            int priceCents,
            int dayIndex,
            LandLedgers.Population.HouseholdLedger buyerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (asset == null) return "WheelwrightRuntime: no asset to sell.";
            if (!builtAssets.ContainsKey(asset.AssetId))
                return $"WheelwrightRuntime: asset '{asset.AssetId}' was not built here.";
            if (priceCents <= 0) return "WheelwrightRuntime: sale price must be positive.";
            if (buyerLedger != null)
            {
                string rejection = buyerLedger.RecordOutflow(dayIndex, priceCents,
                    $"new wagon {asset.AssetId}: {asset.DisplayName}", businessName);
                if (rejection != null)
                    return $"WheelwrightRuntime: buyer payment failed — {rejection}";
            }
            buildRevenueCents += priceCents;
            diagnostics.Add($"WheelwrightRuntime: sold {asset.DisplayName} ({asset.AssetId}) for {priceCents}c.");
            return null;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        /// <summary>Save DTO for the wheelwright shop (Tech X persistence).</summary>
        [Serializable]
        public sealed class WheelwrightSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public string BusinessName = string.Empty;
            public bool WheelStationReady;
            public bool WheelStationDerivedFromComponents;
            public List<string> LumberLotIds = new List<string>();
            public List<int> LumberUnits = new List<int>();
            public List<string> ConsumedLumberLotIds = new List<string>();
            public int IronworkPartsOnHand;
            public int NextAssetNumber = 1;
            public int NextIronworkNumber = 1;
            public int BuildRevenueCents;
            public int RepairRevenueCents;
            public List<EquipmentAsset> BuiltAssets = new List<EquipmentAsset>();
            public List<RepairWorkOrder> OpenOrders = new List<RepairWorkOrder>();
        }

        public WheelwrightSaveDto ToSaveDto()
        {
            var dto = new WheelwrightSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                BusinessName = businessName,
                WheelStationReady = wheelStationReady,
                WheelStationDerivedFromComponents = WheelStationDerivedFromComponents,
                NextAssetNumber = nextAssetNumber,
                NextIronworkNumber = nextIronworkNumber,
                BuildRevenueCents = buildRevenueCents,
                RepairRevenueCents = repairRevenueCents,
            };
            foreach (var kv in lumberStock)
            {
                dto.LumberLotIds.Add(kv.Key);
                dto.LumberUnits.Add(kv.Value);
            }
            dto.ConsumedLumberLotIds.AddRange(consumedLumberLotIds);
            dto.IronworkPartsOnHand = ironworkStock.Count;
            foreach (var kv in builtAssets) dto.BuiltAssets.Add(kv.Value);
            foreach (RepairWorkOrder o in repairQueue.Orders)
                if (o.Status != RepairOrderStatus.Complete && o.Status != RepairOrderStatus.Paid)
                    dto.OpenOrders.Add(o);
            return dto;
        }

        public void LoadFromSaveDto(WheelwrightSaveDto dto)
        {
            if (dto == null) return;
            businessInstanceId = dto.BusinessInstanceId ?? string.Empty;
            businessName = dto.BusinessName ?? string.Empty;
            wheelStationReady = dto.WheelStationReady;
            WheelStationDerivedFromComponents = dto.WheelStationDerivedFromComponents;
            lumberStock.Clear();
            for (int i = 0; i < dto.LumberLotIds.Count && i < dto.LumberUnits.Count; i++)
                lumberStock[dto.LumberLotIds[i]] = dto.LumberUnits[i];
            consumedLumberLotIds.Clear();
            consumedLumberLotIds.AddRange(dto.ConsumedLumberLotIds);
            nextAssetNumber = Math.Max(1, dto.NextAssetNumber);
            nextIronworkNumber = Math.Max(1, dto.NextIronworkNumber);
            buildRevenueCents = Math.Max(0, dto.BuildRevenueCents);
            repairRevenueCents = Math.Max(0, dto.RepairRevenueCents);
            builtAssets.Clear();
            foreach (EquipmentAsset a in dto.BuiltAssets)
                if (a != null && !string.IsNullOrWhiteSpace(a.AssetId))
                    builtAssets[a.AssetId] = a;
            // Ironwork parts are EquipmentAssets whose ids were consumed at build;
            // stock counts are informational — parts themselves are re-received
            // from the smith at session start (no phantom stock).
        }
    }
}
