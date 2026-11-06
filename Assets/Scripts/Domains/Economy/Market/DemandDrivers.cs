using System;
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Market
{
    // W7 (2026-11-06): the dead demand system is gone. DemandDriverSet,
    // the seven drivers (DemandDriverBase + GeneralStore / Blacksmith /
    // Butcher / Saloon / Bakery / LiveryFreight), IDemandDriver, and the
    // TerritoryCensus input model were removed: zero production callers,
    // and the only tests pinning them (MarketTerritoryTests'
    // DriversScoreOpportunitiesNeverCounts + EmptyTerritoryScoresNothing,
    // and the whole SettlementDemandCalibrationTests file) existed only
    // for the removed types and were deleted with them. TerritoryCensus
    // went with the drivers — it was their sole input model with no other
    // caller — which is the one extension past the plan's W-7 list
    // (drivers + set + interface); see the session checkpoint.
    //
    // BusinessOpportunity STAYS: it is live — BusinessFormationAssessment
    // .Evaluate takes one as its input, and the generator seam
    // (GeneratorSeam.HeldReason) names BusinessOpportunity lists as the
    // held generator's input. The producers are gone; the opportunity
    // model is the seam the generator decision will plug into.

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
}
