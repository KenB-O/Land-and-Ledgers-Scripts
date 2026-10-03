using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Livestock
{
    /// <summary>SWN-1: one pig under management.</summary>
    [Serializable]
    public sealed class PigRecord
    {
        public EntityId AnimalId = EntityId.Invalid;
        public int BirthDayIndex;
        public float Condition01 = 1f; // feeding + tending keep this up; neglect drops it

        public PigRecord() { }

        public PigRecord(EntityId animalId, int birthDayIndex)
        {
            AnimalId = animalId;
            BirthDayIndex = birthDayIndex;
        }
    }

    /// <summary>SWN-1: one sheep under management.</summary>
    [Serializable]
    public sealed class SheepRecord
    {
        public EntityId AnimalId = EntityId.Invalid;
        public int BirthDayIndex;
        public int LastShornDayIndex = -1;
        public float Condition01 = 1f;

        public SheepRecord() { }

        public SheepRecord(EntityId animalId, int birthDayIndex)
        {
            AnimalId = animalId;
            BirthDayIndex = birthDayIndex;
        }
    }

    /// <summary>
    /// SWN-1: a wool lot with provenance. Wool is shorn from LIVE sheep —
    /// it is never part of the carcass (the butcher's sheep fractions carry no
    /// wool). Sold to the tailor or the general store as a trade good.
    /// </summary>
    [Serializable]
    public sealed class WoolLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public EntityId SheepId = EntityId.Invalid;
        public int Lbs;
        public string FarmId = string.Empty;
        public int ShornDayIndex;
        public EntityId ShornBy = EntityId.Invalid; // worker (labor provenance)
        public bool Sold; // lots sell exactly once

        public WoolLot() { }
    }

    /// <summary>
    /// SWN-1: pigs and sheep in the FVS-2 pattern — task-driven, provenance
    /// lots, no auto-production. Pigs: feed + tend → market weight → butcher
    /// (pork via species-specific carcass fractions). Sheep: shear annually
    /// for wool, cull to the butcher for mutton. Both eat through the FVS-4
    /// feed loop; dairy byproducts (buttermilk/whey) can feed pigs (Canon §9.7).
    ///
    /// Calibration constants are tuning, not canon (Canon Part XV holds).
    /// </summary>
    public sealed class PigSheepChain
    {
        public const string TendSwineTaskId = "tend-swine";
        public const string ShearSheepTaskId = "shear-sheep";

        /// <summary>Calibration: days from birth to market weight (pigs).</summary>
        public const int PigMarketReadyDays = 180;
        /// <summary>Calibration: shearing interval (annual).</summary>
        public const int ShearIntervalDays = 365;
        /// <summary>Calibration: wool lbs per shearing.</summary>
        public const int WoolLbsPerShearing = 8;
        public const int TendSwineMinutesPerHead = 10;
        public const int ShearMinutesPerHead = 20;

        private readonly Dictionary<EntityId, PigRecord> pigs = new Dictionary<EntityId, PigRecord>();
        private readonly Dictionary<EntityId, SheepRecord> sheep = new Dictionary<EntityId, SheepRecord>();
        private readonly Dictionary<string, WoolLot> woolLots = new Dictionary<string, WoolLot>();

        /// <summary>Registers swine/sheep task definitions (TTS-2, AnimalHusbandry skill).</summary>
        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;

            var tend = new TaskDefinition(TendSwineTaskId, "Tend swine", TendSwineMinutesPerHead);
            tend.SetRequiredSkill(SkillIds.AnimalHusbandry, new[] { "swine-care" });
            authority.RegisterDefinition(tend, out _);

            var shear = new TaskDefinition(ShearSheepTaskId, "Shear sheep", ShearMinutesPerHead);
            shear.SetRequiredSkill(SkillIds.AnimalHusbandry, new[] { "shearing" });
            authority.RegisterDefinition(shear, out _);
        }

        public string RegisterPig(AnimalRegistry registry, EntityId animalId, int dayIndex)
        {
            if (registry == null) return "PigSheepChain: no animal registry.";
            AnimalState animal = registry.GetAnimal(animalId);
            if (animal == null || !animal.IsActive) return $"PigSheepChain: unknown animal {animalId}.";
            if (animal.Species != AnimalSpecies.Pig) return $"PigSheepChain: {animalId} is {animal.Species}, not a pig.";
            pigs[animalId] = new PigRecord(animalId, dayIndex);
            return null;
        }

        public string RegisterSheep(AnimalRegistry registry, EntityId animalId, int dayIndex)
        {
            if (registry == null) return "PigSheepChain: no animal registry.";
            AnimalState animal = registry.GetAnimal(animalId);
            if (animal == null || !animal.IsActive) return $"PigSheepChain: unknown animal {animalId}.";
            if (animal.Species != AnimalSpecies.Sheep) return $"PigSheepChain: {animalId} is {animal.Species}, not a sheep.";
            sheep[animalId] = new SheepRecord(animalId, dayIndex);
            return null;
        }

        public PigRecord GetPig(EntityId animalId)
        {
            PigRecord record;
            return pigs.TryGetValue(animalId, out record) ? record : null;
        }

        public SheepRecord GetSheep(EntityId animalId)
        {
            SheepRecord record;
            return sheep.TryGetValue(animalId, out record) ? record : null;
        }

        public bool IsPigMarketReady(EntityId animalId, int dayIndex)
        {
            PigRecord record = GetPig(animalId);
            return record != null && (dayIndex - record.BirthDayIndex) >= PigMarketReadyDays;
        }

        /// <summary>
        /// Tending swine: real care labor that maintains condition. Condition
        /// scales the market price — neglected pigs are worth less, honestly.
        /// The caller assigns the tend-swine task through TaskAuthority.
        /// </summary>
        public string TendSwine(EntityId animalId, EntityId workerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            PigRecord record = GetPig(animalId);
            if (record == null) return $"PigSheepChain.TendSwine: pig {animalId} not registered.";
            record.Condition01 = Mathf.Clamp01(record.Condition01 + 0.2f);
            diagnostics.Add($"PigSheepChain: tended pig {animalId} (condition now {record.Condition01:P0}).");
            return null;
        }

        /// <summary>Degrades all pig/sheep condition (feed shortfalls call this).</summary>
        public void DegradeCondition(float amount)
        {
            foreach (var record in pigs.Values)
            {
                record.Condition01 = Mathf.Clamp01(record.Condition01 - amount);
            }
            foreach (var record in sheep.Values)
            {
                record.Condition01 = Mathf.Clamp01(record.Condition01 - amount);
            }
        }

        /// <summary>
        /// Shears a sheep: annual task producing a provenance wool lot. Wool
        /// comes off the live animal — shearing a recently-shorn sheep is
        /// refused. The caller assigns the shear-sheep task through TaskAuthority.
        /// </summary>
        public WoolLot ShearSheep(
            AnimalRegistry registry,
            EntityIdRegistry idRegistry,
            EntityId sheepId, EntityId workerId, string farmId,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (registry == null || idRegistry == null)
            {
                diagnostics.Add("PigSheepChain.ShearSheep: registry missing.");
                return null;
            }
            SheepRecord record = GetSheep(sheepId);
            if (record == null)
            {
                diagnostics.Add($"PigSheepChain.ShearSheep: sheep {sheepId} not registered.");
                return null;
            }
            AnimalState animal = registry.GetAnimal(sheepId);
            if (animal == null || !animal.IsActive)
            {
                diagnostics.Add($"PigSheepChain.ShearSheep: sheep {sheepId} unknown or inactive.");
                return null;
            }
            if (record.LastShornDayIndex >= 0 && dayIndex - record.LastShornDayIndex < ShearIntervalDays)
            {
                diagnostics.Add($"PigSheepChain.ShearSheep: sheep {sheepId} was shorn {dayIndex - record.LastShornDayIndex} days ago — wool has not regrown.");
                return null;
            }

            var lot = new WoolLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                SheepId = sheepId,
                Lbs = Mathf.RoundToInt(WoolLbsPerShearing * Mathf.Clamp01(record.Condition01)),
                FarmId = farmId ?? string.Empty,
                ShornDayIndex = dayIndex,
                ShornBy = workerId,
            };
            woolLots[lot.LotId.ToString()] = lot;
            record.LastShornDayIndex = dayIndex;

            diagnostics.Add($"PigSheepChain: shorn sheep {sheepId} → {lot.Lbs} lbs wool (lot {lot.LotId}).");
            return lot;
        }

        /// <summary>
        /// Sells a wool lot to a buyer (tailor, general store). Lots sell
        /// exactly once; proceeds carry provenance (Canon XIII §13.2).
        /// </summary>
        public string SellWool(
            WoolLot lot, string buyerBusinessId, string buyerName,
            int pricePerLbCents, int dayIndex,
            HouseholdLedger sellerLedger, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (lot == null || lot.Lbs <= 0)
            {
                return "PigSheepChain.SellWool: no wool to sell.";
            }
            if (lot.Sold)
            {
                return $"PigSheepChain.SellWool: wool lot {lot.LotId} already sold — lots sell exactly once.";
            }
            if (string.IsNullOrWhiteSpace(buyerBusinessId))
            {
                return "PigSheepChain.SellWool: wool needs a real buyer — no anonymous buyers.";
            }
            if (sellerLedger == null)
            {
                return "PigSheepChain.SellWool: no seller ledger — proceeds require provenance (Canon 13.2).";
            }

            int proceedsCents = lot.Lbs * Math.Max(0, pricePerLbCents);
            lot.Sold = true;

            string problem = sellerLedger.RecordInflow(
                dayIndex, proceedsCents,
                HouseholdIncomeSource.SaleProceeds,
                lot.LotId.ToString(),
                $"wool sale: {lot.Lbs} lbs to {buyerName} ({buyerBusinessId})",
                buyerBusinessId);
            if (problem != null) diagnostics.Add("PigSheepChain.SellWool ledger note: " + problem);

            diagnostics.Add($"PigSheepChain: sold {lot.Lbs} lbs wool to {buyerName} for {proceedsCents}c.");
            return null;
        }

        public WoolLot GetWoolLot(string lotId)
        {
            if (string.IsNullOrWhiteSpace(lotId)) return null;
            WoolLot lot;
            return woolLots.TryGetValue(lotId, out lot) ? lot : null;
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public PigSheepChainSaveDto CaptureSaveDto()
        {
            return new PigSheepChainSaveDto
            {
                pigs = new List<PigRecord>(pigs.Values),
                sheep = new List<SheepRecord>(sheep.Values),
                woolLots = new List<WoolLot>(woolLots.Values),
            };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(PigSheepChainSaveDto dto)
        {
            pigs.Clear();
            sheep.Clear();
            woolLots.Clear();
            if (dto == null) return;
            if (dto.pigs != null)
            {
                foreach (var pig in dto.pigs)
                {
                    if (pig != null && pig.AnimalId.IsValid) pigs[pig.AnimalId] = pig;
                }
            }
            if (dto.sheep != null)
            {
                foreach (var s in dto.sheep)
                {
                    if (s != null && s.AnimalId.IsValid) sheep[s.AnimalId] = s;
                }
            }
            if (dto.woolLots != null)
            {
                foreach (var lot in dto.woolLots)
                {
                    if (lot != null) woolLots[lot.LotId.ToString()] = lot;
                }
            }
        }
    }

    /// <summary>Save DTO for the pig/sheep chain (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class PigSheepChainSaveDto
    {
        public List<PigRecord> pigs = new List<PigRecord>();
        public List<SheepRecord> sheep = new List<SheepRecord>();
        public List<WoolLot> woolLots = new List<WoolLot>();
    }
}
