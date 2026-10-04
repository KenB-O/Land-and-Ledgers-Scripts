using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Butcher
{
    /// <summary>
    /// BIZ-4: carcass products. GHOST-DES-010 — the carcass must balance.
    /// </summary>
    public enum CarcassProduct
    {
        Unspecified = 0,
        RetailCuts = 1,   // Meat for retail sale (perishable).
        Hide = 2,         // Hide for preservation/tanning.
        Tallow = 3,       // Rendered fat.
        Waste = 4,        // Bone, offal waste, shrink — accounted, never sold as meat.
    }

    /// <summary>
    /// BIZ-4: the yield of one slaughtered animal. Conservation is structural:
    /// the fractions sum to 1.0, so meat + hide + tallow + waste ALWAYS equals the
    /// whole animal (GHOST-DES-010). There is no free product.
    /// </summary>
    [Serializable]
    public sealed class CarcassYield
    {
        [SerializeField]
        private EntityId sourceAnimalId;

        [SerializeField]
        private AnimalSpecies species;

        [SerializeField, Min(0)]
        private int liveWeightLbs;

        [SerializeField]
        private List<CarcassProduct> products = new List<CarcassProduct>();

        [SerializeField]
        private List<float> fraction01 = new List<float>();

        [SerializeField]
        private List<int> weightLbs = new List<int>();

        public EntityId SourceAnimalId => sourceAnimalId;
        public AnimalSpecies Species => species;
        public int LiveWeightLbs => Mathf.Max(0, liveWeightLbs);

        public CarcassYield(EntityId sourceAnimalId, AnimalSpecies species, int liveWeightLbs,
            IReadOnlyList<(CarcassProduct product, float fraction)> yields)
        {
            this.sourceAnimalId = sourceAnimalId;
            this.species = species;
            this.liveWeightLbs = Mathf.Max(0, liveWeightLbs);
            products = new List<CarcassProduct>();
            fraction01 = new List<float>();
            weightLbs = new List<int>();

            if (yields != null)
            {
                foreach (var (product, fraction) in yields)
                {
                    products.Add(product);
                    fraction01.Add(Mathf.Clamp01(fraction));
                    weightLbs.Add(Mathf.RoundToInt(this.liveWeightLbs * Mathf.Clamp01(fraction)));
                }
            }
        }

        public int ProductCount => products != null ? products.Count : 0;

        public int WeightFor(CarcassProduct product)
        {
            for (int i = 0; i < ProductCount; i++)
            {
                if (products[i] == product)
                {
                    return weightLbs[i];
                }
            }

            return 0;
        }

        /// <summary>Sum of all output fractions — must be 1.0 (conservation).</summary>
        public float TotalFraction
        {
            get
            {
                float total = 0f;
                if (fraction01 != null)
                {
                    foreach (float f in fraction01)
                    {
                        total += f;
                    }
                }

                return total;
            }
        }

        /// <summary>Sum of all output weights — must equal live weight.</summary>
        public int TotalOutputWeightLbs
        {
            get
            {
                int total = 0;
                if (weightLbs != null)
                {
                    foreach (int w in weightLbs)
                    {
                        total += w;
                    }
                }

                return total;
            }
        }
    }

    /// <summary>
    /// BIZ-4: perishable lot condition. Lots age; spoiled lots are waste and can never
    /// be sold (Tech X Part IV — provenance without condition is insufficient).
    /// </summary>
    public enum LotCondition
    {
        Unspecified = 0,
        Fresh = 1,
        Aging = 2,
        MustSell = 3,  // Sell or lose it.
        Spoiled = 4,   // Waste. Never sold.
    }

    /// <summary>
    /// BIZ-4: a perishable lot with provenance (Tech X Part IV): lot id (HF-1),
    /// product, SOURCE ANIMAL, produced day, quantity, condition, and site
    /// (production vs retail may separate — Canon §3.3).
    /// </summary>
    [Serializable]
    public sealed class ButcherLot
    {
        [SerializeField]
        private string lotId = string.Empty;

        [SerializeField]
        private CarcassProduct product;

        [SerializeField]
        private string sourceAnimalKey = string.Empty;

        [SerializeField]
        private int producedDayIndex;

        [SerializeField, Min(0)]
        private int quantityLbs;

        [SerializeField]
        private LotCondition condition = LotCondition.Fresh;

        [SerializeField]
        private string siteId = string.Empty;

        [SerializeField]
        private int wholesalePricePerLbCents;

        [SerializeField]
        private int retailPricePerLbCents;

        public string LotId => lotId ?? string.Empty;
        public CarcassProduct Product => product;
        public string SourceAnimalKey => sourceAnimalKey ?? string.Empty;
        public int ProducedDayIndex => producedDayIndex;
        public int QuantityLbs => Mathf.Max(0, quantityLbs);
        public LotCondition Condition => condition;
        public string SiteId => siteId ?? string.Empty;

        public ButcherLot(string lotId, CarcassProduct product, EntityId sourceAnimal,
            int producedDayIndex, int quantityLbs, string siteId,
            int wholesalePricePerLbCents, int retailPricePerLbCents)
        {
            this.lotId = lotId ?? string.Empty;
            this.product = product;
            sourceAnimalKey = sourceAnimal.ToString();
            this.producedDayIndex = producedDayIndex;
            this.quantityLbs = Mathf.Max(0, quantityLbs);
            this.siteId = siteId ?? string.Empty;
            this.wholesalePricePerLbCents = Mathf.Max(0, wholesalePricePerLbCents);
            this.retailPricePerLbCents = Mathf.Max(0, retailPricePerLbCents);
        }

        /// <summary>Ages the lot. Spoiled lots stay on the books as waste — they are
        /// never sold, and their provenance is retained for audit.</summary>
        public void AgeToDay(int dayIndex, int freshDays, int agingDays)
        {
            int age = dayIndex - producedDayIndex;
            condition = age <= freshDays ? LotCondition.Fresh
                : age <= freshDays + agingDays ? LotCondition.Aging
                : age <= freshDays + agingDays + 1 ? LotCondition.MustSell
                : LotCondition.Spoiled;
        }

        public bool CanSell => condition != LotCondition.Spoiled && quantityLbs > 0;

        public int TakeLbs(int requestedLbs)
        {
            int taken = Mathf.Min(Mathf.Max(0, requestedLbs), quantityLbs);
            quantityLbs -= taken;
            return taken;
        }

        public int WholesaleValueCents(int lbs) => Mathf.Max(0, lbs) * wholesalePricePerLbCents;
        public int RetailValueCents(int lbs) => Mathf.Max(0, lbs) * retailPricePerLbCents;
    }

    /// <summary>
    /// BIZ-4: the butcher as a GoodsTransformer (Canon §3.3's controlling example):
    /// livestock buying → slaughter → carcass balance → hide/tallow byproducts →
    /// retail cuts, with production and retail sites separable. Every lot carries its
    /// source animal (provenance); the carcass always balances (GHOST-DES-010).
    /// </summary>
    [Serializable]
    public sealed class ButcherRuntime
    {
        [SerializeField]
        private string businessInstanceId = string.Empty;

        [SerializeField]
        private string productionSiteId = string.Empty;

        [SerializeField]
        private string retailSiteId = string.Empty;

        [SerializeField]
        private List<ButcherLot> lots = new List<ButcherLot>();

        [SerializeField, Min(0)]
        private int livestockSpendCents;

        [SerializeField, Min(0)]
        private int retailRevenueCents;

        [SerializeField, Min(0)]
        private int wholesaleRevenueCents;

        [SerializeField]
        private int nextLotNumber = 1;

        // EQP-2: butcher-block workstation (Tech X §3.5). Not serialized —
        // re-established from equipment assets on load. Businesses that never
        // establish one keep the legacy un-gated behavior.
        private WorkstationInstance butcherBlock;
        private Func<string, WorkstationComponentView?> butcherBlockFinder;

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;
        public IReadOnlyList<ButcherLot> Lots => lots;
        public int LivestockSpendCents => Mathf.Max(0, livestockSpendCents);
        public int RetailRevenueCents => Mathf.Max(0, retailRevenueCents);
        public int WholesaleRevenueCents => Mathf.Max(0, wholesaleRevenueCents);

        public ButcherRuntime(string businessInstanceId, string productionSiteId, string retailSiteId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.productionSiteId = productionSiteId ?? string.Empty;
            this.retailSiteId = retailSiteId ?? string.Empty;
        }

        /// <summary>
        /// Buys a live animal into the business (ownership → business). The animal must
        /// exist and be active; the spend is recorded with the animal as provenance.
        /// </summary>
        public bool TryBuyLivestock(
            AnimalRegistry registry,
            EntityId animalId,
            int priceCents,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (registry == null)
            {
                diagnostics.Add("No animal registry.");
                return false;
            }

            AnimalState animal = registry.GetAnimal(animalId);
            if (animal == null || !animal.IsActive)
            {
                diagnostics.Add($"Animal {animalId} is not available for purchase.");
                return false;
            }

            animal.OwnerKind = AnimalOwnerKind.Business;
            animal.OwnerId = businessInstanceId;
            animal.OwnershipHistory.Add(new AnimalOwnershipRecord
            {
                OwnerKind = AnimalOwnerKind.Business,
                OwnerId = businessInstanceId,
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
                Reason = $"purchased by butcher {businessInstanceId} for {priceCents}c",
            });

            livestockSpendCents += Mathf.Max(0, priceCents);
            diagnostics.Add($"Bought {animalId} ({animal.Species}) for {priceCents}c " +
                "(provenance: animal record).");
            return true;
        }

        /// <summary>
        /// EQP-2: establishes the butcher-block workstation from actual components
        /// (Tech X §3.5). Once established, Slaughter refuses when the block is
        /// not ready — a wrecked block awaits repair, never deletion (Tech X §3.9).
        /// </summary>
        public void EstablishButcherBlock(WorkstationInstance station, Func<string, WorkstationComponentView?> findComponent)
        {
            butcherBlock = station;
            butcherBlockFinder = findComponent;
        }

        /// <summary>
        /// EQP-2: null when the block is ready or not established (legacy path);
        /// the reason when established but not ready.
        /// </summary>
        public string CheckButcherBlock(List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (butcherBlock == null) return null;
            return butcherBlock.EvaluateReady(WorkstationCatalog.ButcherBlock, butcherBlockFinder, diagnostics);
        }

        /// <summary>
        /// Slaughters an owned animal: marks it Slaughtered (terminal, ID never reused)
        /// and produces a balancing carcass yield. The yield fractions always sum to 1.
        /// </summary>
        public CarcassYield Slaughter(
            AnimalRegistry registry,
            EntityId animalId,
            int liveWeightLbs,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (registry == null)
            {
                diagnostics.Add("No animal registry.");
                return null;
            }

            AnimalState animal = registry.GetAnimal(animalId);
            if (animal == null || !animal.IsActive)
            {
                diagnostics.Add($"Animal {animalId} cannot be slaughtered (unknown/inactive).");
                return null;
            }

            if (animal.OwnerKind != AnimalOwnerKind.Business || animal.OwnerId != businessInstanceId)
            {
                diagnostics.Add($"Animal {animalId} is not owned by this butcher.");
                return null;
            }

            // EQP-2: butcher-block workstation gate (Tech X §3.5, §3.9) — enforced
            // only once the business establishes its block.
            string blockReason = CheckButcherBlock(diagnostics);
            if (blockReason != null) return null;

            string problem = registry.RecordDisposition(
                animalId, AnimalCommercialStatus.Slaughtered, dayIndex,
                $"slaughtered by {businessInstanceId}");
            if (problem != null)
            {
                diagnostics.Add(problem);
                return null;
            }

            // GHOST-DES-010: standard 1870 beef yield fractions — they MUST sum to 1.
            // SWN-1: species-specific yield fractions. Cattle returns the exact
            // GHOST-DES-010 1870 beef fractions (behavior-preserving); pigs and
            // sheep get their own calibration fractions. Fractions always sum
            // to 1.0 — no free product, ever.
            var yields = CarcassYieldFractions.ForSpecies(animal.Species);

            var yield = new CarcassYield(animalId, animal.Species, liveWeightLbs, yields);
            diagnostics.Add($"Slaughtered {animalId}: {liveWeightLbs} lbs in → " +
                $"{yield.WeightFor(CarcassProduct.RetailCuts)} lbs cuts, " +
                $"{yield.WeightFor(CarcassProduct.Hide)} lbs hide, " +
                $"{yield.WeightFor(CarcassProduct.Tallow)} lbs tallow, " +
                $"{yield.WeightFor(CarcassProduct.Waste)} lbs waste " +
                $"(balance {yield.TotalFraction:P0} — GHOST-DES-010).");

            // Break the yield into provenance-carrying lots at the production site.
            lots ??= new List<ButcherLot>();
            AddLot(CarcassProduct.RetailCuts, animalId, dayIndex,
                yield.WeightFor(CarcassProduct.RetailCuts), productionSiteId, 18, 32);
            AddLot(CarcassProduct.Hide, animalId, dayIndex,
                yield.WeightFor(CarcassProduct.Hide), productionSiteId, 25, 0);
            AddLot(CarcassProduct.Tallow, animalId, dayIndex,
                yield.WeightFor(CarcassProduct.Tallow), productionSiteId, 8, 14);

            return yield;
        }

        /// <summary>
        /// Retail sale from the retail site. Only sellable lots; provenance retained.
        /// Returns revenue cents. Spoiled lots can never be sold.
        /// NX-1A: selling by weight requires a usable scale (Canon 4.1 — the
        /// butcher profile lists scales as core equipment). "counter-scale" is
        /// a recognized equipment-asset kind.
        /// </summary>
        public int SellRetail(string lotId, int requestedLbs, List<string> diagnostics,
            EquipmentTaskGate gate = null, int dayIndex = 0)
        {
            diagnostics ??= new List<string>();
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Asset("counter-scale") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return 0;
            }
            ButcherLot lot = FindLot(lotId);
            if (lot == null)
            {
                diagnostics.Add($"Unknown lot '{lotId}'.");
                return 0;
            }

            if (!lot.CanSell)
            {
                diagnostics.Add($"Lot '{lotId}' is {lot.Condition}; spoiled lots are waste, never sold.");
                return 0;
            }

            int taken = lot.TakeLbs(requestedLbs);
            int revenue = lot.RetailValueCents(taken);
            retailRevenueCents += revenue;
            diagnostics.Add($"Retail sale: {taken} lbs from lot '{lotId}' " +
                $"(source {lot.SourceAnimalKey}) for {revenue}c.");
            return revenue;
        }

        /// <summary>
        /// Wholesale to the general store's provisions (the vertical-slice link).
        /// </summary>
        public int SellWholesale(string lotId, int requestedLbs, string storeBusinessId,
            List<string> diagnostics, EquipmentTaskGate gate = null, int dayIndex = 0)
        {
            diagnostics ??= new List<string>();
            // NX-1A: wholesale is also by weight — same scale gate (Canon 4.1).
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Asset("counter-scale") },
                    "business", BusinessInstanceId, dayIndex, diagnostics);
                if (blocked != null) return 0;
            }
            ButcherLot lot = FindLot(lotId);
            if (lot == null)
            {
                diagnostics.Add($"Unknown lot '{lotId}'.");
                return 0;
            }

            if (!lot.CanSell)
            {
                diagnostics.Add($"Lot '{lotId}' is {lot.Condition}; spoiled lots are waste, never sold.");
                return 0;
            }

            int taken = lot.TakeLbs(requestedLbs);
            int revenue = lot.WholesaleValueCents(taken);
            wholesaleRevenueCents += revenue;
            diagnostics.Add($"Wholesale: {taken} lbs from lot '{lotId}' (source {lot.SourceAnimalKey}) " +
                $"to store {storeBusinessId} for {revenue}c.");
            return revenue;
        }

        /// <summary>Ages all lots to the given day. Spoiled lots remain as waste.</summary>
        public void AgeLotsToDay(int dayIndex, int freshDays = 2, int agingDays = 2)
        {
            if (lots == null)
            {
                return;
            }

            foreach (ButcherLot lot in lots)
            {
                lot.AgeToDay(dayIndex, freshDays, agingDays);
            }
        }

        /// <summary>
        /// CLN-1: captures butcher state for the save pipeline. Lots carry provenance
        /// (Tech X Part IV); the lot-number cursor persists so IDs are never reused.
        /// </summary>
        public ButcherRuntimeSaveDto CaptureSaveDto()
        {
            return new ButcherRuntimeSaveDto
            {
                businessInstanceId = BusinessInstanceId,
                productionSiteId = productionSiteId ?? string.Empty,
                retailSiteId = retailSiteId ?? string.Empty,
                lots = new List<ButcherLot>(lots ?? new List<ButcherLot>()),
            };
        }

        /// <summary>CLN-1: restores butcher state from the save pipeline.</summary>
        public void LoadFromSaveDto(ButcherRuntimeSaveDto dto)
        {
            lots.Clear();
            if (dto == null)
            {
                return;
            }

            if (dto.lots != null)
            {
                lots.AddRange(dto.lots);
            }

            // Advance the lot-number cursor past every restored lot so new lots never
            // reuse an ID.
            foreach (ButcherLot lot in lots)
            {
                if (lot == null || string.IsNullOrEmpty(lot.LotId) || lot.LotId.Length < 2
                    || lot.LotId[0] != 'L')
                {
                    continue;
                }

                if (int.TryParse(lot.LotId.Substring(1), out int number))
                {
                    nextLotNumber = Mathf.Max(nextLotNumber, number + 1);
                }
            }
        }

        private void AddLot(CarcassProduct product, EntityId sourceAnimal, int dayIndex,
            int weightLbs, string siteId, int wholesalePerLb, int retailPerLb)
        {
            if (weightLbs <= 0)
            {
                return;
            }

            string lotId = $"L{nextLotNumber++:000}";
            lots.Add(new ButcherLot(lotId, product, sourceAnimal, dayIndex, weightLbs,
                siteId, wholesalePerLb, retailPerLb));
        }

        private ButcherLot FindLot(string lotId)
        {
            if (lots == null || string.IsNullOrWhiteSpace(lotId))
            {
                return null;
            }

            foreach (ButcherLot lot in lots)
            {
                if (lot.LotId == lotId)
                {
                    return lot;
                }
            }

            return null;
        }
    }
}
