using System;

namespace LandLedgers.Skills
{
    /// <summary>
    /// D1: one shared authority for capability-graded craft outcomes, so
    /// forging and fitting cannot drift apart into two different skill
    /// curves. Canon: skill changes quality, speed and failure risk for
    /// ordinary physically feasible work — it is never an arbitrary
    /// permission gate, and low skill never yields a guaranteed perfect
    /// product (Rev VII §1.1; XXXI §3.3).
    ///
    /// Quality runs 0.60 (novice) to 1.00 (master), linear in capability.
    /// Minutes run from a novice figure down to a master figure, also
    /// linear. Both are deterministic functions of the worker's recorded
    /// continuous capability — no hidden rolls, so a test can reproduce
    /// any outcome from the capability alone.
    /// </summary>
    public static class SkillOutcomeCurves
    {
        /// <summary>Lowest quality any attempted craft can have: usable, never perfect.</summary>
        public const double MinimumQuality01 = 0.60;

        /// <summary>Highest quality: only a master (capability 1.0) makes a perfect piece.</summary>
        public const double MaximumQuality01 = 1.00;

        public static double QualityForCapability(double capability)
        {
            double c = Math.Max(0.0, Math.Min(1.0, capability));
            return Math.Round(MinimumQuality01 + (MaximumQuality01 - MinimumQuality01) * c, 2);
        }

        /// <summary>
        /// Skill-scaled minutes for one unit of work. noviceMinutes is the
        /// time a complete novice needs; masterMinutes is the time a master
        /// needs. A mid-capability worker lands between them.
        /// </summary>
        public static int MinutesForCapability(int noviceMinutes, int masterMinutes, double capability)
        {
            double c = Math.Max(0.0, Math.Min(1.0, capability));
            int novice = Math.Max(1, noviceMinutes);
            int master = Math.Max(1, Math.Min(novice, masterMinutes));
            return master + (int)Math.Round((novice - master) * (1.0 - c));
        }
    }
}
