using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Dairy
{
    /// <summary>SWN-2: how far along a cheese wheel is.</summary>
    public enum CheeseAgeState
    {
        Unspecified = 0,
        Green = 1,    // just made; unsaleable
        Curing = 2,   // aging in storage; unsaleable until cured
        Aged = 3,     // cured; saleable, keeps well
        OverAged = 4, // past its prime; saleable at a loss, honestly
    }

    /// <summary>
    /// SWN-2: a cheese wheel with provenance. Cheese is the durable answer to
    /// perishable milk (Canon §9.5/§9.6) — but only after real curing time in
    /// real storage. Green cheese is never saleable: delayed sale is the rule
    /// (Canon §9.6), not an instant milk → money conversion.
    /// </summary>
    [Serializable]
    public sealed class CheeseWheel
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public List<EntityId> MilkLotIds = new List<EntityId>(); // provenance
        public string FarmId = string.Empty;
        public int Lbs;
        public int MadeDayIndex;
        public EntityId MadeBy = EntityId.Invalid; // worker (labor provenance)
        public string Equipment = string.Empty;    // e.g. "vat + press"
        public string RennetSource = string.Empty; // upstream provenance (rennet is real)
        public CheeseAgeState AgeState = CheeseAgeState.Green;
        public float Quality01 = 1f;               // storage conditions move this
        public bool Sold; // lots sell exactly once

        public CheeseWheel() { }
    }

    /// <summary>
    /// SWN-2: the cheese chain. Canon §9.6 is explicit: cheesemaking may require
    /// specialized knowledge, workspace, vats/vessels, rennet/ingredients, curd
    /// handling, pressing, curing/storage, and DELAYED sale. It must not be a
    /// one-click Milk → More Valuable Product conversion.
    ///
    /// Milk → (cheese-making task + rennet + vat/press) → green wheels →
    /// curing time in storage → aged wheels → sale. Whey byproduct feeds pigs
    /// (Canon §9.7). Calibration constants are tuning, not canon (Part XV).
    /// </summary>
    public sealed class CheeseChain
    {
        public const string MakeCheeseTaskId = "make-cheese";

        /// <summary>TTS-3 extension-path skill: cheese making (not a starter).</summary>
        public const string CheeseMakingSkillId = "cheese-making";

        /// <summary>Calibration: milk units per lb of cheese.</summary>
        public const int MilkUnitsPerCheeseLb = 10;
        /// <summary>Calibration: whey units per 10 milk units (→ pig feed, Canon §9.7).</summary>
        public const int WheyUnitsPer10Milk = 8;
        /// <summary>Calibration: minutes per 100 milk units at skill 1.</summary>
        public const int MakeCheeseMinutesPer100Milk = 90;
        /// <summary>Calibration: curing days before a wheel is saleable.</summary>
        public const int CureDays = 60;
        /// <summary>Calibration: aged cheese keeps this long past curing.</summary>
        public const int AgedKeepDays = 180;

        private readonly Dictionary<string, CheeseWheel> wheels = new Dictionary<string, CheeseWheel>();

        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("CheeseChain: no SkillService — cheese-making skill not registered.");
                return;
            }
            if (skillService.GetSkill(CheeseMakingSkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(CheeseMakingSkillId, "Cheese Making",
                        "Curd handling, pressing, and curing cheese. Registered by the cheese chain (TTS-3 extension path)."),
                    out rejection))
                {
                    diagnostics.Add("CheeseChain: cheese-making skill rejected: " + rejection);
                }
            }
        }

        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            var make = new TaskDefinition(MakeCheeseTaskId, "Make cheese", MakeCheeseMinutesPer100Milk);
            make.SetRequiredSkill(CheeseMakingSkillId, new[] { "cheesemaking" });
            authority.RegisterDefinition(make, out _);
        }

        /// <summary>
        /// Makes cheese from fresh milk lots. Requires the cheese-making task's
        /// labor (caller assigns it), vats/press equipment, and rennet from a
        /// NAMED source (rennet is a real input — usually the butcher; no
        /// orphan inputs). Returns the green wheels, or null with diagnostics.
        /// </summary>
        public List<CheeseWheel> MakeCheese(
            EntityIdRegistry idRegistry,
            List<MilkLot> milkLots,
            EntityId workerId,
            string farmId,
            int dayIndex,
            string equipment,
            string rennetSource,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (idRegistry == null) return null;

            if (milkLots == null || milkLots.Count == 0)
            {
                diagnostics.Add("CheeseChain.MakeCheese: no milk — cheese is not conjured.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(equipment))
            {
                diagnostics.Add("CheeseChain.MakeCheese: cheesemaking needs vats/vessels and a press (Canon §9.6) — name the equipment.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(rennetSource))
            {
                diagnostics.Add("CheeseChain.MakeCheese: rennet needs a named source (usually the butcher) — no orphan inputs.");
                return null;
            }

            int freshUnits = 0;
            var usedLotIds = new List<EntityId>();
            foreach (var lot in milkLots)
            {
                if (lot == null) continue;
                int saleable = lot.SaleableUnits(dayIndex);
                if (saleable <= 0)
                {
                    diagnostics.Add($"CheeseChain.MakeCheese: milk lot {lot.LotId} is spoiled — spoiled milk can never become cheese (Canon §9.1).");
                    return null;
                }
                freshUnits += saleable;
                usedLotIds.Add(lot.LotId);
            }
            if (freshUnits <= 0)
            {
                diagnostics.Add("CheeseChain.MakeCheese: no fresh milk units.");
                return null;
            }

            int totalLbs = freshUnits / MilkUnitsPerCheeseLb;
            if (totalLbs <= 0)
            {
                diagnostics.Add($"CheeseChain.MakeCheese: {freshUnits} milk units make no whole wheel — save it up.");
                return null;
            }

            var made = new List<CheeseWheel>();
            // One wheel per 10 lbs for traceability (calibration).
            const int lbsPerWheel = 10;
            int remaining = totalLbs;
            while (remaining > 0)
            {
                int wheelLbs = Math.Min(lbsPerWheel, remaining);
                var wheel = new CheeseWheel
                {
                    LotId = idRegistry.Allocate(EntityKind.Lot),
                    MilkLotIds = new List<EntityId>(usedLotIds),
                    FarmId = farmId ?? string.Empty,
                    Lbs = wheelLbs,
                    MadeDayIndex = dayIndex,
                    MadeBy = workerId,
                    Equipment = equipment,
                    RennetSource = rennetSource,
                    AgeState = CheeseAgeState.Green,
                };
                wheels[wheel.LotId.ToString()] = wheel;
                made.Add(wheel);
                remaining -= wheelLbs;
            }

            int wheyUnits = (freshUnits * WheyUnitsPer10Milk) / 10;

            foreach (var lot in milkLots)
            {
                if (lot != null) lot.QuantityUnits = 0; // consumed into the make
            }

            diagnostics.Add($"CheeseChain: made {made.Count} green wheel(s) ({totalLbs} lbs) from {freshUnits} milk units; " +
                $"{wheyUnits} whey units as byproduct (→ pig feed, Canon §9.7). Wheels are UNSALEABLE until cured ({CureDays} days).");
            return made;
        }

        /// <summary>
        /// Ages wheels: green → curing → aged once CureDays pass; aged wheels
        /// keep for AgedKeepDays, then go over-aged (saleable at a loss,
        /// honestly). Poor storage degrades quality. Returns wheels that
        /// changed state.
        /// </summary>
        public List<CheeseWheel> AgeWheels(int dayIndex, float storageQuality01, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var changed = new List<CheeseWheel>();
            float storage = Mathf.Clamp01(storageQuality01);

            foreach (var wheel in wheels.Values)
            {
                if (wheel.Sold) continue;
                int age = dayIndex - wheel.MadeDayIndex;

                if (wheel.AgeState == CheeseAgeState.Green && age >= 1)
                {
                    wheel.AgeState = CheeseAgeState.Curing;
                    changed.Add(wheel);
                }
                else if (wheel.AgeState == CheeseAgeState.Curing && age >= CureDays)
                {
                    wheel.AgeState = CheeseAgeState.Aged;
                    changed.Add(wheel);
                    diagnostics.Add($"CheeseChain: wheel {wheel.LotId} cured after {CureDays} days — now saleable.");
                }
                else if (wheel.AgeState == CheeseAgeState.Aged && age >= CureDays + AgedKeepDays)
                {
                    wheel.AgeState = CheeseAgeState.OverAged;
                    changed.Add(wheel);
                    diagnostics.Add($"CheeseChain: wheel {wheel.LotId} is over-aged — saleable at a loss.");
                }

                // Poor storage degrades quality while curing/aging.
                if ((wheel.AgeState == CheeseAgeState.Curing || wheel.AgeState == CheeseAgeState.Aged)
                    && storage < 0.5f)
                {
                    wheel.Quality01 = Mathf.Clamp01(wheel.Quality01 - 0.05f);
                }
            }
            return changed;
        }

        /// <summary>
        /// Sells an aged (or over-aged) wheel. Green/curing wheels are refused —
        /// delayed sale is the rule (Canon §9.6). Lots sell exactly once.
        /// </summary>
        public string SellCheese(
            CheeseWheel wheel, string buyerBusinessId, string buyerName,
            int pricePerLbCents, int dayIndex,
            HouseholdLedger sellerLedger, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (wheel == null || wheel.Lbs <= 0)
            {
                return "CheeseChain.SellCheese: no cheese to sell.";
            }
            if (wheel.AgeState != CheeseAgeState.Aged && wheel.AgeState != CheeseAgeState.OverAged)
            {
                return $"CheeseChain.SellCheese: wheel {wheel.LotId} is {wheel.AgeState} — green cheese is never saleable (Canon §9.6 delayed sale).";
            }
            if (wheel.Sold)
            {
                return $"CheeseChain.SellCheese: wheel {wheel.LotId} already sold — lots sell exactly once.";
            }
            if (string.IsNullOrWhiteSpace(buyerBusinessId))
            {
                return "CheeseChain.SellCheese: cheese needs a real buyer — no anonymous buyers.";
            }
            if (sellerLedger == null)
            {
                return "CheeseChain.SellCheese: no seller ledger — proceeds require provenance (Canon 13.2).";
            }

            float qualityFactor = wheel.AgeState == CheeseAgeState.OverAged ? 0.5f : Mathf.Clamp01(wheel.Quality01);
            int proceedsCents = Mathf.RoundToInt(wheel.Lbs * Math.Max(0, pricePerLbCents) * qualityFactor);
            wheel.Sold = true;

            string problem = sellerLedger.RecordInflow(
                dayIndex, proceedsCents,
                HouseholdIncomeSource.SaleProceeds,
                wheel.LotId.ToString(),
                $"cheese sale: {wheel.Lbs} lbs ({wheel.AgeState}, quality {wheel.Quality01:P0}) to {buyerName} ({buyerBusinessId})",
                buyerBusinessId);
            if (problem != null) diagnostics.Add("CheeseChain.SellCheese ledger note: " + problem);

            diagnostics.Add($"CheeseChain: sold {wheel.Lbs} lbs {wheel.AgeState} cheese to {buyerName} for {proceedsCents}c.");
            return null;
        }

        public CheeseWheel GetWheel(string lotId)
        {
            if (string.IsNullOrWhiteSpace(lotId)) return null;
            CheeseWheel wheel;
            return wheels.TryGetValue(lotId, out wheel) ? wheel : null;
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public CheeseChainSaveDto CaptureSaveDto()
        {
            return new CheeseChainSaveDto { wheels = new List<CheeseWheel>(wheels.Values) };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(CheeseChainSaveDto dto)
        {
            wheels.Clear();
            if (dto == null || dto.wheels == null) return;
            foreach (var wheel in dto.wheels)
            {
                if (wheel != null) wheels[wheel.LotId.ToString()] = wheel;
            }
        }
    }

    /// <summary>Save DTO for the cheese chain (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class CheeseChainSaveDto
    {
        public List<CheeseWheel> wheels = new List<CheeseWheel>();
    }
}
