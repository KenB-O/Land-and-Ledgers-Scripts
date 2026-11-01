namespace LandLedgers.Population
{
    public readonly struct PopulationGenerationResult
    {
        public PopulationGenerationResult(PopulationState state, string summary, bool hasErrors)
        {
            State = state;
            Summary = summary;
            HasErrors = hasErrors;
        }

        public PopulationState State { get; }
        public string Summary { get; }
        public bool HasErrors { get; }
    }
}
