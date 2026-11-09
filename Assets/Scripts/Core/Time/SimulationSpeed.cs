namespace LandLedgers.Time
{
    public enum SimulationSpeed
    {
        Paused = 0,
        Speed1x = 1,
        Speed2x = 2,
        Speed4x = 4,
        Speed5x = 5,
        Speed6x = 6,
        Speed10x = 10,
        Speed25x = 25,
        Speed50x = 50,
        Speed100x = 100,
        // Keep existing serialized speed values unchanged. RealTime is an
        // explicit clock mode whose multiplier is derived from the configured
        // accelerated-day length, not from this enum's numeric value.
        RealTime = 101,
        Speed250x = 102
    }
}
