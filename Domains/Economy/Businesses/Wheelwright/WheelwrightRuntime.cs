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

        /// <summary>
        /// Capability code stamped on work orders (Tech X §6.4).
        ///
        /// D3A reconciliation decision: "wheelwright-work" is the ONE canonical
        /// capability code for wagon/wheel work. The older "wagon-work" string
        /// appeared only in two Blacksmith-side comments (never stamped on a
        /// real work order anywhere in the repo) and meant the same thing, so
        /// it was retired to avoid two codes for one capability. Canon §7.2A/B:
        /// shops are bundles of capabilities, not hard classes — a blacksmith
        /// business that employs a qualified wheelwright can satisfy
        /// "wheelwright-work" orders too; the smith's own structural role here
        /// is upstream (ironwork parts, iron-tire overlap per §7.2B), not a
        /// second code.
        /// </summary>
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

        /// <summary>D3A: the shop's custom-build order book (intake → quote → agree → build).</summary>
        private readonly WheelwrightBuildOrderBook buildOrderBook = new WheelwrightBuildOrderBook();
        /// <summary>D3A: the spare-parts shelf — finished wheels/axles as real inventory.</summary>
        private readonly WheelwrightSparePartsShelf partsShelf = new WheelwrightSparePartsShelf();
        /// <summary>D3A: the used-wagon yard — bought wrecks awaiting refurbishment or dismantling.</summary>
        private readonly WheelwrightUsedWagonYard usedWagonYard = new WheelwrightUsedWagonYard();
        /// <summary>D3A: the customer account book — running tabs (Canon §7.2E book credit).</summary>
        private readonly WheelwrightAccountBook accountBook = new WheelwrightAccountBook();
        private int usedWagonRevenueCents;
        private int sparePartRevenueCents;
        private int salvageRevenueCents;

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;
        public string BusinessName => businessName ?? string.Empty;
        public bool WheelStationReady => wheelStationReady;
        /// <summary>EQP-2: true once readiness derives from actual components (Tech X §3.5).</summary>
        public bool WheelStationDerivedFromComponents { get; private set; }
        public RepairQueue Repairs => repairQueue;
        public int BuildRevenueCents => Mathf.Max(0, buildRevenueCents);
        public int RepairRevenueCents => Mathf.Max(0, repairRevenueCents);
        /// <summary>D3A: revenue from refurbished used-wagon resales.</summary>
        public int UsedWagonRevenueCents => Mathf.Max(0, usedWagonRevenueCents);
        /// <summary>D3A: revenue from over-the-counter spare-part sales.</summary>
        public int SparePartRevenueCents => Mathf.Max(0, sparePartRevenueCents);
        /// <summary>D3A: value recovered from dismantling uneconomic wagons.</summary>
        public int SalvageRevenueCents => Mathf.Max(0, salvageRevenueCents);
        public WheelwrightBuildOrderBook BuildOrders => buildOrderBook;
        public WheelwrightSparePartsShelf PartsShelf => partsShelf;
        public WheelwrightUsedWagonYard UsedWagons => usedWagonYard;
        public WheelwrightAccountBook Accounts => accountBook;
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

        /// <summary>
        /// Build recipes are data. Minutes/units are calibration (Canon Part XV).
        /// Wagon kinds follow Canon §9.3B (heavy freight, farm wagons, local
        /// drayage, light passenger/stage gear differ by job, no tech tree);
        /// "wagon-axle" is a stocked component recipe for the spare-parts shelf
        /// (D3A — the wooden parts the wheelwright fabricates; iron tires stay
        /// smith-supplied, Canon §7.2B).
        /// </summary>
        public static List<WheelwrightBuildRecipe> DefaultRecipes()
        {
            return new List<WheelwrightBuildRecipe>
            {
                new WheelwrightBuildRecipe("farm-wagon", "Farm wagon", 40, 2, 480),
                new WheelwrightBuildRecipe("freight-wagon", "Freight wagon", 60, 3, 720),
                new WheelwrightBuildRecipe("spring-wagon", "Spring wagon", 32, 2, 420),
                new WheelwrightBuildRecipe("dray", "Dray (local delivery cart)", 24, 1, 300),
                new WheelwrightBuildRecipe("wagon-wheel", "Wagon wheel (spare)", 6, 1, 120),
                new WheelwrightBuildRecipe("wagon-axle", "Wagon axle (spare)", 8, 1, 150),
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

        /// <summary>
        /// D3A: the workforce half of craft readiness — station + qualified
        /// wheelwright, no material check. Used by work that consumes no
        /// materials (e.g. dismantling a wreck for parts).
        /// </summary>
        private int VerifyWorkforceReady(string requiredSkillId, EntityId wheelwrightPersonId,
            SkillService skillService, List<string> diagnostics)
        {
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
            int level = skillService.GetLevel(wheelwrightPersonId, requiredSkillId);
            if (level < JourneymanLevelFloor)
            {
                diagnostics.Add(
                    $"WheelwrightRuntime: {wheelwrightPersonId} is level {level} in {requiredSkillId} " +
                    $"(needs {JourneymanLevelFloor}) — without a real journeyman the work collapses (Canon §7.2).");
                return -1;
            }
            return level;
        }

        private int VerifyCraftReady(WheelwrightBuildRecipe recipe, EntityId wheelwrightPersonId,
            SkillService skillService, List<string> diagnostics)
        {
            if (recipe == null) { diagnostics.Add("WheelwrightRuntime: no recipe supplied."); return -1; }
            int level = VerifyWorkforceReady(recipe.RequiredSkillId, wheelwrightPersonId, skillService, diagnostics);
            if (level < 0) return -1;
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
        /// D3A: consumes one recipe's worth of materials — lumber oldest-lots
        /// first (Tech X §6.1 lot discipline) + blacksmith ironwork FIFO.
        /// Caller must have passed VerifyCraftReady first. Consumed lot ids are
        /// appended to the shop's running provenance list AND reported per
        /// build via thisBuildLotIds.
        /// </summary>
        private void ConsumeBuildMaterials(WheelwrightBuildRecipe recipe,
            List<string> thisBuildLotIds, List<EquipmentAsset> consumedParts)
        {
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
                if (thisBuildLotIds != null && !thisBuildLotIds.Contains(lotId))
                    thisBuildLotIds.Add(lotId);
            }

            for (int i = 0; i < recipe.IronworkParts; i++)
            {
                EquipmentAsset part = ironworkStock[0];
                ironworkStock.RemoveAt(0);
                consumedParts.Add(part);
            }
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

            // Consume lumber (oldest lots first — Tech X §6.1 lot discipline)
            // + blacksmith ironwork parts (FIFO); provenance flows into the wagon.
            var consumedParts = new List<EquipmentAsset>();
            ConsumeBuildMaterials(recipe, null, consumedParts);

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
        ///
        /// D3A additions (all optional, defaults preserve W6 behavior):
        /// - quality: Proper restores fully; Temporary/Improvised (Canon §6.5C)
        ///   restore to a parameterized fraction and record a known defect
        ///   requiring proper shop follow-up — a field repair is not shop work.
        /// - consumeSparePartKind: when set (e.g. the order's diagnosed
        ///   ComponentKind), one finished part comes off the spare-parts shelf
        ///   instead of raw lumber (Canon §6.5G: parts turn delay into an
        ///   immediate repair).
        /// </summary>
        public string CompleteWagonRepair(
            string workOrderId,
            EquipmentAsset asset,
            EntityId wheelwrightPersonId,
            int dayIndex,
            SkillService skillService,
            LandLedgers.Population.HouseholdLedger customerLedger,
            List<string> diagnostics,
            EquipmentTaskGate gate = null,
            WheelwrightRepairQuality quality = WheelwrightRepairQuality.Proper,
            float temporaryRestore01 = DefaultTemporaryRestore01,
            string consumeSparePartKind = null)
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

            // D3A: repair quality — partial restoration for temporary/improvised work.
            float startCondition = asset != null ? asset.Condition01 : 0f;
            float targetCondition = 1f;
            int materialUnits = order.MaterialUnitsNeeded;
            if (quality != WheelwrightRepairQuality.Proper)
            {
                float baseTarget = Mathf.Clamp01(temporaryRestore01);
                targetCondition = quality == WheelwrightRepairQuality.Temporary
                    ? baseTarget
                    : baseTarget * 0.5f;
                targetCondition = Mathf.Max(startCondition, targetCondition);
                if (targetCondition <= startCondition + 0.001f)
                    return $"WheelwrightRuntime: {workOrderId} — asset already at {startCondition:P0}, " +
                           $"above the {quality} target {targetCondition:P0}; do a proper repair or re-quote.";
                // Partial job, partial materials: pro-rata to the condition gap closed.
                float fullGap = 1f - startCondition;
                materialUnits = fullGap > 0.001f
                    ? (int)Math.Ceiling(materialUnits * ((targetCondition - startCondition) / fullGap))
                    : 0;
            }

            SparePartBatch consumedBatch = null;
            if (!string.IsNullOrWhiteSpace(consumeSparePartKind))
            {
                if (!WheelwrightSparePartsShelf.IsSparePartKind(consumeSparePartKind))
                    return $"WheelwrightRuntime: '{consumeSparePartKind}' is not a stocked spare-part kind.";
                if (materialUnits > 0)
                {
                    consumedBatch = partsShelf.ConsumeOne(consumeSparePartKind);
                    if (consumedBatch == null)
                        return $"WheelwrightRuntime: no '{consumeSparePartKind}' on the shelf for {workOrderId} — fabricate or restock first.";
                }
            }

            if (consumedBatch == null && materialUnits > 0)
            {
                if (!string.Equals(order.MaterialId, ImportCatalog.LumberId, StringComparison.Ordinal))
                    return $"WheelwrightRuntime: {workOrderId} calls for '{order.MaterialId}' — this shop works lumber + ironwork only.";
                if (TotalLumberOnHand() < materialUnits)
                    return $"WheelwrightRuntime: short {materialUnits - TotalLumberOnHand()}x lumber for {workOrderId}.";
                int remaining = materialUnits;
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
            {
                asset.RepairTo(targetCondition);
                if (quality == WheelwrightRepairQuality.Proper)
                {
                    asset.RecordMaintenance($"wheelwright repair — {order.DiagnosedProblem} ({workOrderId}).", dayIndex);
                }
                else
                {
                    // Canon §6.5D: the known defect is recorded — the asset is
                    // NOT equivalent to shop work until it gets proper follow-up.
                    string partNote = consumedBatch != null
                        ? $" fitted {consumedBatch.DisplayName} from shelf ({consumedBatch.ProvenanceNote})"
                        : string.Empty;
                    asset.RecordMaintenance(
                        $"{quality.ToString().ToLowerInvariant()} wheelwright repair — {order.DiagnosedProblem} " +
                        $"({workOrderId}){partNote}; restored to {targetCondition:P0} — NEEDS PROPER SHOP FOLLOW-UP.",
                        dayIndex);
                }
            }
            if (consumedBatch != null)
            {
                diagnostics.Add(
                    $"WheelwrightRuntime: {workOrderId} consumed 1x {consumedBatch.DisplayName} from the shelf " +
                    $"(cost basis {consumedBatch.UnitCostCents}c) instead of raw lumber.");
            }

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
            diagnostics.Add(
                $"WheelwrightRuntime: {workOrderId} complete — {order.AssetDescription} repaired" +
                (quality == WheelwrightRepairQuality.Proper ? string.Empty : $" ({quality}, to {targetCondition:P0})") +
                $", {order.AgreedPriceCents}c booked.");
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

        // ================= D3A: custom build orders =================

        /// <summary>Calibration: default restored condition for a temporary repair. Tuning, not canon.</summary>
        public const float DefaultTemporaryRestore01 = 0.6f;

        /// <summary>Calibration: refurbishment consumes this fraction of the build recipe's lumber. Tuning, not canon.</summary>
        public const float RefurbMaterialFraction01 = 0.5f;

        /// <summary>Calibration: dismantling books this fraction of acquisition cost as scrap value. Tuning, not canon.</summary>
        public const float DismantleScrapFraction01 = 0.10f;

        /// <summary>D3A: a customer commissions a custom build (intake step of the order lifecycle).</summary>
        public WheelwrightBuildOrder RequestBuildOrder(
            string customerName, string customerBusinessId,
            string recipeKind, bool isRush, int dayIndex, List<string> diagnostics)
        {
            return buildOrderBook.RequestBuild(
                customerName, customerBusinessId, recipeKind, isRush, dayIndex, diagnostics);
        }

        /// <summary>
        /// D3A: puts the shop's ask on a build order — parts at cost + markup,
        /// labor at the shop rate, rush premium on labor (Canon §7.2D pricing
        /// freedom, §9.3 rush lever). Material prices are caller-supplied data
        /// (lumber catalog price, the smith's ironwork price), never canon.
        /// </summary>
        public string QuoteBuildOrder(
            string orderId,
            int lumberPricePerUnitCents,
            int ironworkPricePerPartCents,
            int dayIndex,
            List<string> diagnostics,
            int shopRateCentsPerHour = WheelwrightPricing.DefaultShopRateCentsPerHour,
            float partsMarkup = WheelwrightPricing.DefaultPartsMarkup)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightBuildOrder order = buildOrderBook.Find(orderId);
            if (order == null) return $"WheelwrightRuntime: unknown build order '{orderId}'.";
            WheelwrightBuildRecipe recipe = null;
            foreach (WheelwrightBuildRecipe r in DefaultRecipes())
                if (string.Equals(r.EquipmentKind, order.RecipeKind, StringComparison.Ordinal))
                { recipe = r; break; }
            if (recipe == null) return $"WheelwrightRuntime: no build recipe for '{order.RecipeKind}'.";

            int partsCost = recipe.LumberUnits * Math.Max(0, lumberPricePerUnitCents)
                + recipe.IronworkParts * Math.Max(0, ironworkPricePerPartCents);
            int parts = (int)Math.Round(partsCost * (1f + Math.Max(0f, partsMarkup)));
            int hours = (int)Math.Ceiling(Math.Max(0, recipe.LaborMinutes) / 60.0);
            int labor = hours * Math.Max(0, shopRateCentsPerHour);
            if (order.IsRush)
                labor = (int)Math.Round(labor * WheelwrightBuildOrderBook.RushLaborPremium);
            int quote = parts + labor;

            string rejection = buildOrderBook.QuoteBuild(orderId, quote, diagnostics);
            if (rejection == null)
                diagnostics.Add(
                    $"WheelwrightRuntime: {orderId} ask = {parts}c parts + {labor}c labor" +
                    (order.IsRush ? " (rush premium applied)" : "") + $" = {quote}c.");
            return rejection;
        }

        /// <summary>D3A: the customer accepts the ask — the order joins the bench queue with a promised day.</summary>
        public string AgreeBuildOrderTerms(
            string orderId, int agreedPriceCents, int dayIndex, List<string> diagnostics,
            int shopLaborMinutesPerDay = TurnaroundEstimator.DefaultShopLaborMinutesPerDay)
        {
            return buildOrderBook.AgreeBuildTerms(
                orderId, agreedPriceCents, dayIndex, shopLaborMinutesPerDay, diagnostics);
        }

        /// <summary>D3A: withdraw a build order before construction starts.</summary>
        public string CancelBuildOrder(string orderId, List<string> diagnostics)
        {
            return buildOrderBook.CancelBuild(orderId, diagnostics);
        }

        /// <summary>
        /// D3A: performs an agreed custom build — the fabrication counterpart
        /// to completing a repair work order. Builds the wagon through the
        /// normal BuildWagon gates (station, journeyman, materials, kit), links
        /// the asset to the order, and marks it complete. The SALE stays the
        /// operator's step via RecordBuildSale at the agreed price (cash now,
        /// or a tab via Accounts.ChargeToAccount — Canon §7.2E).
        /// A refused build requeues the order; restock and retry.
        /// </summary>
        public EquipmentAsset CompleteBuildOrder(
            string orderId,
            EntityId wheelwrightPersonId,
            int dayIndex,
            SkillService skillService,
            List<string> diagnostics,
            EquipmentTaskGate gate = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightBuildOrder order = buildOrderBook.Find(orderId);
            if (order == null)
            {
                diagnostics.Add($"WheelwrightRuntime: unknown build order '{orderId}'.");
                return null;
            }
            if (order.Status != WheelwrightBuildOrderStatus.Agreed || !order.TermsAgreed)
            {
                diagnostics.Add($"WheelwrightRuntime: {orderId} is {order.Status} — terms must be agreed first.");
                return null;
            }
            string rejection = buildOrderBook.MarkBuildStarted(orderId);
            if (rejection != null)
            {
                diagnostics.Add("WheelwrightRuntime: " + rejection);
                return null;
            }
            EquipmentAsset wagon = BuildWagon(
                order.RecipeKind, wheelwrightPersonId, dayIndex, skillService, gate, diagnostics);
            if (wagon == null)
            {
                buildOrderBook.RequeueBuild(orderId);
                diagnostics.Add($"WheelwrightRuntime: {orderId} could not be built — order requeued, restock and retry.");
                return null;
            }
            order.BuiltAssetId = wagon.AssetId;
            string completeRejection = buildOrderBook.MarkBuildComplete(orderId, dayIndex);
            if (completeRejection != null)
            {
                diagnostics.Add("WheelwrightRuntime: " + completeRejection);
                return wagon;
            }
            diagnostics.Add(
                $"WheelwrightRuntime: {orderId} complete — {wagon.DisplayName} ({wagon.AssetId}) built; " +
                $"sell via RecordBuildSale at the agreed {order.AgreedPriceCents}c.");
            return wagon;
        }

        // ================= D3A: spare-parts shelf =================

        /// <summary>
        /// D3A: fabricates finished components (wheels, axles) onto the
        /// spare-parts shelf — the "stocked standardized replacement parts"
        /// from the wagon-repairer occupation row. Consumes lumber + smith
        /// ironwork per the component recipe with full provenance; each batch
        /// carries its unit cost basis so cash tied up is real (Canon §6.5G).
        /// A refused unit stops the run; already-made units stay on the shelf.
        /// </summary>
        public string FabricateSparePart(
            string partKind,
            int units,
            EntityId wheelwrightPersonId,
            int dayIndex,
            SkillService skillService,
            int lumberPricePerUnitCents,
            int ironworkPricePerPartCents,
            List<string> diagnostics,
            EquipmentTaskGate gate = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!WheelwrightSparePartsShelf.IsSparePartKind(partKind))
                return $"WheelwrightRuntime: '{partKind}' is not a stocked spare-part kind.";
            if (units <= 0) return "WheelwrightRuntime: fabricate a positive number of parts.";
            WheelwrightBuildRecipe recipe = null;
            foreach (WheelwrightBuildRecipe r in DefaultRecipes())
                if (string.Equals(r.EquipmentKind, partKind, StringComparison.Ordinal))
                { recipe = r; break; }
            if (recipe == null) return $"WheelwrightRuntime: no build recipe for '{partKind}'.";
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Kit("wheelwright-kit") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return "WheelwrightRuntime: refused — " + blocked;
            }

            int made = 0;
            int totalCost = 0;
            var provenanceLots = new List<string>();
            var consumedPartIds = new List<string>();
            for (int i = 0; i < units; i++)
            {
                if (VerifyCraftReady(recipe, wheelwrightPersonId, skillService, diagnostics) < 0)
                {
                    diagnostics.Add(
                        $"WheelwrightRuntime: spare-part run stopped after {made} of {units} — restock and continue.");
                    break;
                }
                var lotIds = new List<string>();
                var consumedParts = new List<EquipmentAsset>();
                ConsumeBuildMaterials(recipe, lotIds, consumedParts);
                made++;
                totalCost += recipe.LumberUnits * Math.Max(0, lumberPricePerUnitCents)
                    + recipe.IronworkParts * Math.Max(0, ironworkPricePerPartCents);
                foreach (string lotId in lotIds)
                    if (!provenanceLots.Contains(lotId)) provenanceLots.Add(lotId);
                foreach (EquipmentAsset p in consumedParts) consumedPartIds.Add(p.AssetId);
            }
            if (made <= 0) return $"WheelwrightRuntime: no '{partKind}' fabricated — see diagnostics.";
            int unitCost = (int)Math.Round(totalCost / (double)made);
            partsShelf.ReceiveBatch(new SparePartBatch(
                partKind, recipe.DisplayName, made, unitCost, dayIndex,
                $"fabricated day {dayIndex} from {string.Join(",", provenanceLots)} + {string.Join(",", consumedPartIds)}"),
                diagnostics);
            return null;
        }

        /// <summary>
        /// D3A: over-the-counter spare-part sale. The buyer's payment is their
        /// ledger outflow; a failed payment puts the part back on the shelf.
        /// </summary>
        public string SellSparePart(
            string partKind,
            int priceCents,
            int dayIndex,
            LandLedgers.Population.HouseholdLedger buyerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!WheelwrightSparePartsShelf.IsSparePartKind(partKind))
                return $"WheelwrightRuntime: '{partKind}' is not a stocked spare-part kind.";
            if (priceCents <= 0) return "WheelwrightRuntime: sale price must be positive.";
            SparePartBatch batch = partsShelf.ConsumeOne(partKind);
            if (batch == null)
                return $"WheelwrightRuntime: no '{partKind}' on the shelf — fabricate first.";
            if (buyerLedger != null)
            {
                string rejection = buyerLedger.RecordOutflow(dayIndex, priceCents,
                    $"spare {batch.DisplayName} ({businessName})", businessName);
                if (rejection != null)
                {
                    partsShelf.ReceiveBatch(new SparePartBatch(batch.PartKind, batch.DisplayName,
                        1, batch.UnitCostCents, batch.ReceivedDayIndex, batch.ProvenanceNote), diagnostics);
                    return $"WheelwrightRuntime: buyer payment failed — {rejection}";
                }
            }
            sparePartRevenueCents += priceCents;
            diagnostics.Add(
                $"WheelwrightRuntime: sold 1x {batch.DisplayName} for {priceCents}c " +
                $"(cost basis {batch.UnitCostCents}c).");
            return null;
        }

        // ================= D3A: used-wagon commerce =================

        /// <summary>
        /// D3A: buys a worn wagon into the yard for flipping (Canon §7.2F).
        /// Cost basis is tracked for resale margin; the cash movement itself
        /// is the operator's step outside this runtime.
        /// </summary>
        public string AcquireWornWagon(
            EquipmentAsset asset,
            int acquisitionCents,
            string sellerName,
            int dayIndex,
            List<string> diagnostics)
        {
            return usedWagonYard.AcquireWornWagon(
                asset, acquisitionCents, sellerName, businessInstanceId, dayIndex, diagnostics);
        }

        /// <summary>
        /// D3A: refurbishment — "combines multiple repairs to restore a used
        /// asset for continued use or resale" (Canon §7.2F). Consumes a
        /// fraction of the build recipe's materials, needs the station and a
        /// journeyman, restores the wagon fully, and records the refurbishment
        /// in the asset's maintenance history (feeds diligence/resale, Tech X §3.3).
        /// </summary>
        public string RefurbishWagonForResale(
            string assetId,
            EntityId wheelwrightPersonId,
            int dayIndex,
            SkillService skillService,
            int lumberPricePerUnitCents,
            int ironworkPricePerPartCents,
            List<string> diagnostics,
            EquipmentTaskGate gate = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            UsedWagonRecord record = usedWagonYard.Find(assetId);
            if (record == null) return $"WheelwrightRuntime: '{assetId}' is not in the used-wagon yard.";
            if (record.RefurbishedForResale) return $"WheelwrightRuntime: {assetId} is already refurbished.";
            WheelwrightBuildRecipe buildRecipe = null;
            foreach (WheelwrightBuildRecipe r in DefaultRecipes())
                if (string.Equals(r.EquipmentKind, record.Asset.Kind, StringComparison.Ordinal))
                { buildRecipe = r; break; }
            if (buildRecipe == null)
                return $"WheelwrightRuntime: no build recipe for '{record.Asset.Kind}' — cannot estimate refurbishment.";
            var refurbRecipe = new WheelwrightBuildRecipe(
                buildRecipe.EquipmentKind,
                "Refurbish " + buildRecipe.DisplayName,
                Math.Max(1, (int)Math.Ceiling(buildRecipe.LumberUnits * RefurbMaterialFraction01)),
                1,
                Math.Max(60, buildRecipe.LaborMinutes / 2));
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Kit("wheelwright-kit") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return "WheelwrightRuntime: refused — " + blocked;
            }
            if (VerifyCraftReady(refurbRecipe, wheelwrightPersonId, skillService, diagnostics) < 0)
                return $"WheelwrightRuntime: {assetId} refurbishment refused — see diagnostics.";
            var lotIds = new List<string>();
            var consumedParts = new List<EquipmentAsset>();
            ConsumeBuildMaterials(refurbRecipe, lotIds, consumedParts);
            int cost = refurbRecipe.LumberUnits * Math.Max(0, lumberPricePerUnitCents)
                + refurbRecipe.IronworkParts * Math.Max(0, ironworkPricePerPartCents);
            record.Asset.RepairTo(1f);
            record.Asset.RecordMaintenance(
                $"refurbished for resale — {refurbRecipe.LumberUnits}x lumber + {refurbRecipe.IronworkParts}x ironwork (D3A).",
                dayIndex);
            record.RefurbishedForResale = true;
            record.RefurbCostCents = cost;
            record.RefurbDayIndex = dayIndex;
            diagnostics.Add($"WheelwrightRuntime: {assetId} refurbished for resale ({cost}c materials).");
            return null;
        }

        /// <summary>
        /// D3A: sells a REFURBISHED yard wagon. The yard sells restored wagons,
        /// not wrecks (Canon §7.2F: buy, repair, resell). Margin over the full
        /// cost basis (acquisition + refurbishment) is reported.
        /// </summary>
        public string SellUsedWagon(
            string assetId,
            string buyerBusinessId,
            int priceCents,
            int dayIndex,
            LandLedgers.Population.HouseholdLedger buyerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            UsedWagonRecord record = usedWagonYard.Find(assetId);
            if (record == null) return $"WheelwrightRuntime: '{assetId}' is not in the used-wagon yard.";
            if (!record.RefurbishedForResale)
                return $"WheelwrightRuntime: {assetId} is not refurbished — the yard sells restored wagons, not wrecks (Canon §7.2F).";
            if (priceCents <= 0) return "WheelwrightRuntime: sale price must be positive.";
            if (string.IsNullOrWhiteSpace(buyerBusinessId))
                return "WheelwrightRuntime: buyer business id is required.";
            if (buyerLedger != null)
            {
                string rejection = buyerLedger.RecordOutflow(dayIndex, priceCents,
                    $"used wagon {assetId}: {record.Asset.DisplayName} ({businessName})", businessName);
                if (rejection != null)
                    return $"WheelwrightRuntime: buyer payment failed — {rejection}";
            }
            string transferRejection = record.Asset.TransferOwnership(
                "business", buyerBusinessId, $"used-wagon sale {assetId}");
            if (transferRejection != null)
                return $"WheelwrightRuntime: {transferRejection}";
            usedWagonRevenueCents += priceCents;
            int margin = priceCents - record.TotalCostBasisCents;
            usedWagonYard.RemoveRecord(assetId);
            diagnostics.Add(
                $"WheelwrightRuntime: sold refurbished {record.Asset.DisplayName} ({assetId}) for {priceCents}c — " +
                $"margin {margin}c over {record.TotalCostBasisCents}c cost basis.");
            return null;
        }

        /// <summary>
        /// D3A: strips an uneconomic yard wagon for parts (Canon §7.2F:
        /// "dismantle uneconomic assets for parts/scrap"). Yields salvaged
        /// wheels + an axle onto the parts shelf at apportioned cost basis,
        /// and books a scrap fraction of the acquisition cost. The asset is
        /// gone — dismantling is one-way.
        /// </summary>
        public string DismantleWagonForParts(
            string assetId,
            EntityId wheelwrightPersonId,
            int dayIndex,
            SkillService skillService,
            List<string> diagnostics,
            EquipmentTaskGate gate = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            UsedWagonRecord record = usedWagonYard.Find(assetId);
            if (record == null) return $"WheelwrightRuntime: '{assetId}' is not in the used-wagon yard.";
            if (record.RefurbishedForResale)
                return $"WheelwrightRuntime: {assetId} is refurbished — sell it, don't strip it.";
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Kit("wheelwright-kit") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return "WheelwrightRuntime: refused — " + blocked;
            }
            if (VerifyWorkforceReady(WheelwrightingSkillId, wheelwrightPersonId, skillService, diagnostics) < 0)
                return $"WheelwrightRuntime: cannot dismantle {assetId} — workforce not ready.";
            int totalParts = WheelwrightUsedWagonYard.DismantleWheelYield
                + WheelwrightUsedWagonYard.DismantleAxleYield;
            int perPartBasis = record.AcquisitionCents / Math.Max(1, totalParts);
            partsShelf.ReceiveBatch(new SparePartBatch(
                "wagon-wheel", "Wagon wheel (salvaged)",
                WheelwrightUsedWagonYard.DismantleWheelYield, perPartBasis, dayIndex,
                $"salvaged from {assetId}"), diagnostics);
            partsShelf.ReceiveBatch(new SparePartBatch(
                "wagon-axle", "Wagon axle (salvaged)",
                WheelwrightUsedWagonYard.DismantleAxleYield, perPartBasis, dayIndex,
                $"salvaged from {assetId}"), diagnostics);
            int scrapValue = (int)Math.Round(record.AcquisitionCents * DismantleScrapFraction01);
            salvageRevenueCents += scrapValue;
            usedWagonYard.RemoveRecord(assetId);
            diagnostics.Add(
                $"WheelwrightRuntime: dismantled {assetId} — " +
                $"{WheelwrightUsedWagonYard.DismantleWheelYield}x wheels + " +
                $"{WheelwrightUsedWagonYard.DismantleAxleYield}x axle to the shelf, {scrapValue}c scrap booked.");
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
            // D3A state: build orders, parts shelf, used-wagon yard, account book.
            public WheelwrightBuildOrderBook.BuildOrderBookDto BuildOrders =
                new WheelwrightBuildOrderBook.BuildOrderBookDto();
            public WheelwrightSparePartsShelf.SparePartsShelfDto PartsShelf =
                new WheelwrightSparePartsShelf.SparePartsShelfDto();
            public WheelwrightUsedWagonYard.UsedWagonYardDto UsedWagons =
                new WheelwrightUsedWagonYard.UsedWagonYardDto();
            public WheelwrightAccountBook.AccountBookDto AccountBook =
                new WheelwrightAccountBook.AccountBookDto();
            public int UsedWagonRevenueCents;
            public int SparePartRevenueCents;
            public int SalvageRevenueCents;
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
                UsedWagonRevenueCents = usedWagonRevenueCents,
                SparePartRevenueCents = sparePartRevenueCents,
                SalvageRevenueCents = salvageRevenueCents,
            };
            dto.BuildOrders = buildOrderBook.ToSaveDto();
            dto.PartsShelf = partsShelf.ToSaveDto();
            dto.UsedWagons = usedWagonYard.ToSaveDto();
            dto.AccountBook = accountBook.ToSaveDto();
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
            usedWagonRevenueCents = Math.Max(0, dto.UsedWagonRevenueCents);
            sparePartRevenueCents = Math.Max(0, dto.SparePartRevenueCents);
            salvageRevenueCents = Math.Max(0, dto.SalvageRevenueCents);
            buildOrderBook.LoadFromSaveDto(dto.BuildOrders);
            partsShelf.LoadFromSaveDto(dto.PartsShelf);
            usedWagonYard.LoadFromSaveDto(dto.UsedWagons);
            accountBook.LoadFromSaveDto(dto.AccountBook);
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
