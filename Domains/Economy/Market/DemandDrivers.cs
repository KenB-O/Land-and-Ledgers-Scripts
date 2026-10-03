using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
using UnityEngine;

namespace LandLedgers.Economy.Market
{
    /// <summary>
    /// T2E: one scored business opportunity. Drivers produce opportunities —
    /// NEVER business counts. There is no BusinessCount = Population / X here
    /// (Canon §1.1 CANON LOCK); a score of 1.0 means "strongly supported by
    /// real demand", not "build N of these".
    /// </summary>
    [Serializable]
    public sealed class BusinessOpportunity
    {
        public BusinessType BusinessType;
        public float Score01;
        public List<string> Reasons = new List<string>();

        public BusinessOpportunity() { }
    }

    /// <summary>
    /// T2E: the real world-state a demand driver queries. The caller fills
    /// this from authored scenario state, the equipment register, the draft
    /// service, and the population views — every number traces to something
    /// real. Denominators below are CALIBRATION (Canon Part XV), not canon.
    /// </summary>
    [Serializable]
    public sealed class TerritoryCensus
    {
        public int EffectiveMarketPopulation;
        public int TownHouseholdCount;
        public int CatchmentFarmCount;
        public int CatchmentHouseholdCount;
        public float AvgCatchmentMiles;
        public int WagonsInTerritory;
        public int DraftTeamsInTerritory;
        public int ImplementsInTerritory;
        public int LivestockFarmsInCatchment;
        public int TransientVisitorsPerDay;
        public SettlementRole Role = SettlementRole.Unspecified;

        public TerritoryCensus() { }
    }

    /// <summary>T2E: a data-driven demand driver querying real world state (Tech X).</summary>
    public interface IDemandDriver
    {
        BusinessOpportunity Evaluate(TerritoryCensus census);
    }

    public abstract class DemandDriverBase : IDemandDriver
    {
        public abstract BusinessOpportunity Evaluate(TerritoryCensus census);

        protected static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        protected static BusinessOpportunity Make(BusinessType type, float score, params string[] reasons)
        {
            var opportunity = new BusinessOpportunity { BusinessType = type, Score01 = Clamp01(score) };
            opportunity.Reasons.AddRange(reasons);
            return opportunity;
        }
    }

    /// <summary>T2E: general-store demand from reachable provisioning need.</summary>
    public sealed class GeneralStoreDemandDriver : DemandDriverBase
    {
        public override BusinessOpportunity Evaluate(TerritoryCensus census)
        {
            float score = Clamp01(census.EffectiveMarketPopulation / 800f); // calibration
            if (census.Role == SettlementRole.AgriculturalServiceTown) score = Clamp01(score + 0.15f);
            return Make(BusinessType.GeneralStore, score,
                $"effective market {census.EffectiveMarketPopulation}",
                $"role {census.Role}");
        }
    }

    /// <summary>
    /// T2E: blacksmith demand from REAL repair demand — wagons, implements,
    /// draft teams in the territory (the EQU-2 repair economy, now measured).
    /// </summary>
    public sealed class BlacksmithDemandDriver : DemandDriverBase
    {
        public override BusinessOpportunity Evaluate(TerritoryCensus census)
        {
            int repairBase = census.WagonsInTerritory * 2 + census.ImplementsInTerritory + census.DraftTeamsInTerritory;
            float score = Clamp01(repairBase / 60f); // calibration
            return Make(BusinessType.Blacksmith, score,
                $"repair base: {census.WagonsInTerritory} wagons, {census.ImplementsInTerritory} implements, {census.DraftTeamsInTerritory} teams");
        }
    }

    /// <summary>T2E: butcher demand from livestock farms in the catchment.</summary>
    public sealed class ButcherDemandDriver : DemandDriverBase
    {
        public override BusinessOpportunity Evaluate(TerritoryCensus census)
        {
            float score = Clamp01(census.LivestockFarmsInCatchment / 12f); // calibration
            if (census.Role == SettlementRole.AgriculturalServiceTown) score = Clamp01(score + 0.1f);
            return Make(BusinessType.Butcher, score,
                $"{census.LivestockFarmsInCatchment} livestock farms in catchment");
        }
    }

    /// <summary>T2E: saloon demand from town life plus transient traffic.</summary>
    public sealed class SaloonDemandDriver : DemandDriverBase
    {
        public override BusinessOpportunity Evaluate(TerritoryCensus census)
        {
            float score = Clamp01((census.TownHouseholdCount / 120f + census.TransientVisitorsPerDay / 40f) / 2f); // calibration
            if (census.Role == SettlementRole.Railhead) score = Clamp01(score + 0.2f);
            return Make(BusinessType.Saloon, score,
                $"{census.TownHouseholdCount} town households, {census.TransientVisitorsPerDay} transient/day");
        }
    }

    /// <summary>T2E: bakery demand from town households (daily bread).</summary>
    public sealed class BakeryDemandDriver : DemandDriverBase
    {
        public override BusinessOpportunity Evaluate(TerritoryCensus census)
        {
            float score = Clamp01(census.TownHouseholdCount / 150f); // calibration
            return Make(BusinessType.Bakery, score,
                $"{census.TownHouseholdCount} town households needing daily bread");
        }
    }

    /// <summary>
    /// T2E: freight demand from farm-route economics (GHOST-DES-040) —
    /// catchment farms times their distance. Far farms generate more
    /// haulage, not less.
    /// </summary>
    public sealed class LiveryFreightDemandDriver : DemandDriverBase
    {
        public override BusinessOpportunity Evaluate(TerritoryCensus census)
        {
            float score = Clamp01(census.CatchmentFarmCount * census.AvgCatchmentMiles / 200f); // calibration
            return Make(BusinessType.LiveryFreight, score,
                $"{census.CatchmentFarmCount} catchment farms averaging {census.AvgCatchmentMiles:F1} mi");
        }
    }

    /// <summary>
    /// T2E: runs every registered driver over one census. The output list is
    /// the generator's input (see GeneratorSeam) — and a useful merchant
    /// diagnostic even while the generator is held.
    /// </summary>
    public sealed class DemandDriverSet
    {
        private readonly List<IDemandDriver> drivers = new List<IDemandDriver>
        {
            new GeneralStoreDemandDriver(),
            new BlacksmithDemandDriver(),
            new ButcherDemandDriver(),
            new SaloonDemandDriver(),
            new BakeryDemandDriver(),
            new LiveryFreightDemandDriver(),
        };

        public List<BusinessOpportunity> EvaluateAll(TerritoryCensus census)
        {
            var results = new List<BusinessOpportunity>(drivers.Count);
            foreach (IDemandDriver driver in drivers)
                results.Add(driver.Evaluate(census));
            results.Sort((a, b) => b.Score01.CompareTo(a.Score01));
            return results;
        }
    }
}
