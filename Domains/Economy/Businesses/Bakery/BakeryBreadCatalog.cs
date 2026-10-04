using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// W2A: the bakery's retail price schedule as data. Bread prices are retail
    /// policy (Canon Part V bakery profile), NOT task execution fees — money
    /// still moves only through ledger authorities; this schedule is data the
    /// shop runtime and sales paths read. Day-old bread sells at a discount
    /// (GHOST-CAN-011: deterioration hurts price before total loss); stale
    /// bread is waste, never sold. All rates are calibration (Canon Part XV).
    /// </summary>
    [Serializable]
    public sealed class BakeryPriceSchedule
    {
        [SerializeField, Min(0)]
        private int breadLoafFreshCents = 8;

        [SerializeField, Min(0)]
        private int rollFreshCents = 2;

        [SerializeField, Min(0)]
        private int pieFreshCents = 25;

        [SerializeField, Range(0, 100)]
        private int dayOldDiscountPct = 50;

        public BakeryPriceSchedule() { }

        public BakeryPriceSchedule(int breadLoafFreshCents, int rollFreshCents, int pieFreshCents, int dayOldDiscountPct)
        {
            this.breadLoafFreshCents = Math.Max(0, breadLoafFreshCents);
            this.rollFreshCents = Math.Max(0, rollFreshCents);
            this.pieFreshCents = Math.Max(0, pieFreshCents);
            this.dayOldDiscountPct = Math.Max(0, Math.Min(100, dayOldDiscountPct));
        }

        public int BreadLoafFreshCents => Math.Max(0, breadLoafFreshCents);
        public int RollFreshCents => Math.Max(0, rollFreshCents);
        public int PieFreshCents => Math.Max(0, pieFreshCents);

        /// <summary>Percent of the fresh price day-old goods sell for (100 = no discount).</summary>
        public int DayOldDiscountPct => Math.Max(0, Math.Min(100, dayOldDiscountPct));
    }

    /// <summary>
    /// W2A: one baked product as data — flour and notions per dough batch,
    /// yield per batch, and the labor minutes of each production stage. All
    /// quantities are calibration (Canon Part XV). One dough batch = one oven
    /// firing's worth of product.
    /// </summary>
    public struct BakeryProductSpec
    {
        public string ProductId;
        public string DisplayName;
        public string UnitName; // "loaf", "roll", "pie"
        public int FlourUnitsPerBatch;   // lb of flour per dough batch
        public int NotionUnitsPerBatch;  // yeast/soda/salt/lard per dough batch
        public int YieldUnitsPerBatch;   // loaves/rolls/pies per baked batch
        public int PrepMinutes;          // mix + knead + shape (hands-on)
        public int BakeMinutes;          // oven time per batch
        public int ProofMinutes;         // D1D: rack time per batch (proofing stage)
    }

    /// <summary>
    /// W2A: bread/pastry product work as task-system data (W1C pattern, via
    /// BarberServiceCatalog/TailorGarmentCatalog). Canon Part III §3.1: a
    /// bakery generates meaningful tasks such as retrieve ingredients, measure,
    /// mix, knead, proof, shape, prepare oven, bake, unload, cool, package —
    /// collapsed here to two meaningful stages (prep, bake), not animation
    /// granularity. Tech X §3.5: baking REQUIRES the bake oven — the room is
    /// not enough — so the oven workstation is a hard gate on bake tasks.
    /// </summary>
    public static class BakeryBreadCatalog
    {
        public const string BreadLoafId = "bake.bread-loaf";
        public const string RollsId = "bake.rolls";
        public const string PieId = "bake.pie";

        /// <summary>W2A: item ids bake tasks declare as inputs. Resolved to real lots by the runtime.</summary>
        public const string FlourItemId = "bakery-flour";

        /// <summary>W2A: yeast/soda/salt/lard — the baker's notions (W1C pattern).</summary>
        public const string NotionsItemId = "bakery-notions";

        public const string BakingSkillId = "baking";

        /// <summary>W2A: the workstation id the bake stage requires. Defined in WorkstationCatalog (Tech X §3.5 BakeOven).</summary>
        public const string BakeOvenStationId = "bake-oven";

        /// <summary>W2A: the NX-1 teeth gate for the prep stage — peels, troughs, scales, knives. A bake oven is still required for the bake stage.</summary>
        public const string BakerHandKitId = "baker-hand-kit";

        /// <summary>TUNING: oven firings available per oven per day.</summary>
        public const int FiringsPerOvenPerDay = 4;

        /// <summary>TUNING: flour (lb) per wheat-loaf dough batch.</summary>
        public const int BreadFlourPerBatch = 12;

        /// <summary>TUNING: loaves per wheat-loaf dough batch.</summary>
        public const int BreadYieldPerBatch = 24;

        /// <summary>TUNING: flour (lb) per roll dough batch.</summary>
        public const int RollsFlourPerBatch = 6;

        /// <summary>TUNING: rolls per roll dough batch.</summary>
        public const int RollsYieldPerBatch = 48;

        /// <summary>TUNING: flour (lb) per pie dough batch.</summary>
        public const int PieFlourPerBatch = 4;

        /// <summary>TUNING: pies per pie dough batch.</summary>
        public const int PieYieldPerBatch = 12;

        /// <summary>TUNING: notions (yeast/soda/salt/lard) per dough batch, any product.</summary>
        public const int StandardNotionsPerBatch = 1;

        /// <summary>TUNING: prep (mix + knead + proof + shape) minutes per batch.</summary>
        public const int BreadPrepMinutes = 120;
        public const int RollsPrepMinutes = 90;
        public const int PiePrepMinutes = 90;

        /// <summary>TUNING: bake minutes per batch (one firing).</summary>
        public const int BreadBakeMinutes = 60;
        public const int RollsBakeMinutes = 45;
        public const int PieBakeMinutes = 45;

        /// <summary>
        /// D1D TUNING: proof minutes required per dough batch, by product.
        /// Proofing is a scheduled stage (Canon Part III §3.1 names proof as a
        /// meaningful bakery task; the canon equipment profile lists the
        /// proofing rack). Proof consumes rack time, not baker labor.
        /// </summary>
        public const int BreadProofMinutes = 120;
        public const int RollsProofMinutes = 90;
        public const int PieProofMinutes = 60;

        /// <summary>
        /// D1D TUNING: proof minutes one proofing-rack slot provides per day.
        /// Daily proof capacity = rack slots × this value, allocated FIFO.
        /// </summary>
        public const int ProofMinutesPerSlotPerDay = 120;

        /// <summary>
        /// D1D TUNING: default proofing-rack slots. The canon equipment profile
        /// lists the proofing rack but gives no slot count — this is
        /// calibration, settable per shop via
        /// <see cref="BakeryShopRuntime.SetProofingRackSlots"/>.
        /// </summary>
        public const int DefaultProofingRackSlots = 4;

        /// <summary>
        /// D1D: oven fuel — the firebox burns cordwood. Item id for fuel lots
        /// (canon equipment profile: "Oven/firebox"; Canon §8.1B meal service
        /// demands fuel; the logistics templates include fuel_dealer_to_bakery).
        /// </summary>
        public const string FuelItemId = "bakery-fuelwood";

        /// <summary>D1D TUNING: cordwood units burned per oven firing.</summary>
        public const int FuelUnitsPerFiring = 1;

        /// <summary>
        /// D1D TUNING: wholesale planning lead days — accounts due within this
        /// window drive automatic batch planning (Canon §7.3E: large recurring
        /// buyers improve throughput visibility).
        /// </summary>
        public const int WholesalePlanningLeadDays = 1;

        /// <summary>
        /// D1D TUNING: retail reference demand (units/day) used only by the
        /// proportional wholesale/retail sharing policy. Canon §7.3E names the
        /// sharing choice but gives no demand figure — calibration.
        /// </summary>
        public const int RetailReferenceDemandUnits = 24;

        /// <summary>Product specs as data, for the shop runtime's scheduler.</summary>
        public static BakeryProductSpec GetSpec(string productId)
        {
            if (string.Equals(productId, BreadLoafId, StringComparison.Ordinal))
                return new BakeryProductSpec
                {
                    ProductId = BreadLoafId, DisplayName = "Wheat bread loaf", UnitName = "loaf",
                    FlourUnitsPerBatch = BreadFlourPerBatch, NotionUnitsPerBatch = StandardNotionsPerBatch,
                    YieldUnitsPerBatch = BreadYieldPerBatch,
                    PrepMinutes = BreadPrepMinutes, BakeMinutes = BreadBakeMinutes,
                    ProofMinutes = BreadProofMinutes,
                };
            if (string.Equals(productId, RollsId, StringComparison.Ordinal))
                return new BakeryProductSpec
                {
                    ProductId = RollsId, DisplayName = "Bread roll", UnitName = "roll",
                    FlourUnitsPerBatch = RollsFlourPerBatch, NotionUnitsPerBatch = StandardNotionsPerBatch,
                    YieldUnitsPerBatch = RollsYieldPerBatch,
                    PrepMinutes = RollsPrepMinutes, BakeMinutes = RollsBakeMinutes,
                    ProofMinutes = RollsProofMinutes,
                };
            if (string.Equals(productId, PieId, StringComparison.Ordinal))
                return new BakeryProductSpec
                {
                    ProductId = PieId, DisplayName = "Fruit pie", UnitName = "pie",
                    FlourUnitsPerBatch = PieFlourPerBatch, NotionUnitsPerBatch = StandardNotionsPerBatch,
                    YieldUnitsPerBatch = PieYieldPerBatch,
                    PrepMinutes = PiePrepMinutes, BakeMinutes = PieBakeMinutes,
                    ProofMinutes = PieProofMinutes,
                };
            return default;
        }

        /// <summary>True for the three offered products; false for anything else (unknown ids never schedule).</summary>
        public static bool IsKnownProduct(string productId)
        {
            return !string.IsNullOrEmpty(GetSpec(productId).ProductId);
        }

        /// <summary>Reads the fresh retail price from the schedule. Unknown products price at zero, never guessed.</summary>
        public static int GetFreshPriceCents(string productId, BakeryPriceSchedule prices)
        {
            prices = prices ?? new BakeryPriceSchedule();
            if (string.Equals(productId, BreadLoafId, StringComparison.Ordinal)) return prices.BreadLoafFreshCents;
            if (string.Equals(productId, RollsId, StringComparison.Ordinal)) return prices.RollFreshCents;
            if (string.Equals(productId, PieId, StringComparison.Ordinal)) return prices.PieFreshCents;
            return 0;
        }

        /// <summary>Day-old retail price = fresh price × the schedule's day-old percent. Unknown products price at zero.</summary>
        public static int GetDayOldPriceCents(string productId, BakeryPriceSchedule prices)
        {
            int fresh = GetFreshPriceCents(productId, prices);
            prices = prices ?? new BakeryPriceSchedule();
            return fresh * prices.DayOldDiscountPct / 100;
        }

        /// <summary>Task id for a bake stage (e.g. "bake.prep.bake.bread-loaf").</summary>
        public static string StageTaskId(string stage, string productId)
        {
            return $"bake.{stage}.{productId}";
        }

        /// <summary>Registers the baking skill via the TTS-3 extension path (idempotent).</summary>
        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("BakeryBreadCatalog: no SkillService — baking skill not registered.");
                return;
            }

            if (skillService.GetSkill(BakingSkillId) != null) return;
            if (!skillService.RegisterSkill(new SkillDefinition(BakingSkillId, "Baking",
                "Bakery work: measuring, mixing, kneading, proofing, shaping, firing the oven, baking. (TTS-3 extension path; Canon Part V bakery.)"),
                out string rejection))
            {
                diagnostics.Add($"BakeryBreadCatalog: skill '{BakingSkillId}' rejected: {rejection}");
            }
        }

        /// <summary>Registers the bake task definitions. Safe to call once at boot.</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string ignored;

            // NX-1 teeth gate: every stage requires a usable baker's hand kit.
            string kit = EquipmentRequirementCodes.Kit(BakerHandKitId);
            // Oven gate as data: Tech X §3.5 BakeOven — baking requires the
            // oven, the room is not enough. The shop runtime pools ovens and
            // assigns firings per batch.
            string oven = EquipmentRequirementCodes.Workstation(BakeOvenStationId);

            foreach (string productId in new[] { BreadLoafId, RollsId, PieId })
            {
                BakeryProductSpec spec = GetSpec(productId);

                var prep = new TaskDefinition(StageTaskId("prep", productId),
                    $"Bakery prep ({spec.DisplayName})", Math.Max(1, spec.PrepMinutes));
                prep.SetRequiredSkill(BakingSkillId, new[] { BakingSkillId });
                prep.SetDefaultPriority(TaskPriority.Normal);
                prep.DomainTags.Add("food-production");
                prep.DomainTags.Add("bakery");
                prep.EquipmentClasses.Add(kit);
                prep.Inputs.Add(new TaskMaterial(FlourItemId, spec.FlourUnitsPerBatch));
                prep.Inputs.Add(new TaskMaterial(NotionsItemId, spec.NotionUnitsPerBatch));
                authority.RegisterDefinition(prep, out ignored);

                var bake = new TaskDefinition(StageTaskId("bake", productId),
                    $"Bakery bake ({spec.DisplayName})", Math.Max(1, spec.BakeMinutes));
                bake.SetRequiredSkill(BakingSkillId, new[] { BakingSkillId });
                bake.SetDefaultPriority(TaskPriority.Normal);
                bake.DomainTags.Add("food-production");
                bake.DomainTags.Add("bakery");
                bake.EquipmentClasses.Add(kit);
                bake.EquipmentClasses.Add(oven);
                authority.RegisterDefinition(bake, out ignored);
            }
        }
    }
}
