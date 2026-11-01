namespace LandLedgers.Population
{
    public enum AgeBand
    {
        Child0To9 = 0,
        Helper10To12 = 1,
        JuniorWorker13To15 = 2,
        YoungWorker16To17 = 3,
        Adult18Plus = 4
    }

    public enum LaborAccessLevel
    {
        None = 0,
        HouseholdHelper = 1,
        JuniorLowTrust = 2,
        YoungWorker = 3,
        FullLaborMarket = 4
    }

    public enum ApprenticeshipStage
    {
        Helper = 0,
        Apprentice = 1,
        Worker = 2
    }

    public enum PopulationScheduleState
    {
        AtHome = 0,
        GoingToWork = 1,
        AtWork = 2,
        GoingShopping = 3,
        AtStore = 4,
        ReturningHome = 5
    }

    public enum HouseholdHeatRetentionTier
    {
        Basic = 0,
        Drafty = 1,
        Tight = 2
    }

    public enum HouseholdStoveTier
    {
        Basic = 0,
        Efficient = 1
    }

    public enum HouseholdFuelType
    {
        Wood = 0
    }
}
