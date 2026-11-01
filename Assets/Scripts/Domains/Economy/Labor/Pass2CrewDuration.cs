using System;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Pass 2 (task system): crew-aware duration, without a crew cap and
    /// without making solo work impossible. Kennedy's rule in substance:
    /// one person CAN pull a log, but very inefficiently; more hands make
    /// meaningful work, they do not unlock it.
    ///
    /// Model (tuning, NOT canon, NOT per-job caps): effective crew grows
    /// sublinearly, because extra hands coordinate, wait, and get in the
    /// way. Effective workers = 1 + 0.7 * (crew - 1). Adjusted minutes =
    /// ceil(base / effective). Solo therefore takes the full base time,
    /// a pair takes about 59% of it, a crew of four about 32%. No job
    /// refuses a solo worker, and no job caps its crew — a caller that
    /// needs a cap for a specific workstation must justify it in its own
    /// code and document it, not borrow one from here.
    ///
    /// This is a hook, not a replacement estimator: TaskAuthority's
    /// existing skill estimator still runs at assignment. A driver that
    /// wants crew scaling calls CrewAdjustedMinutes for the
    /// planned-minutes base before creating the task, exactly as freight
    /// already does for quantity-driven bases.
    /// </summary>
    public static class Pass2CrewDuration
    {
        /// <summary>TUNING: marginal value of each hand after the first (0.7 = coordination loss).</summary>
        public const double AdditionalHandEffectiveness = 0.7;

        /// <summary>Effective workers for a crew size. Crew &lt; 1 is treated as solo.</summary>
        public static double EffectiveWorkers(int crewSize)
        {
            if (crewSize <= 1) return 1.0;
            return 1.0 + AdditionalHandEffectiveness * (crewSize - 1);
        }

        /// <summary>Crew-adjusted whole minutes for a base duration. Never below 1 minute (current booking floor).</summary>
        public static int CrewAdjustedMinutes(int baseMinutes, int crewSize)
        {
            if (baseMinutes <= 0) return 1;
            double adjusted = baseMinutes / EffectiveWorkers(crewSize);
            return Math.Max(1, (int)Math.Ceiling(adjusted));
        }

        /// <summary>
        /// Worked example in code, so a reviewer can see the curve without
        /// trusting prose: a 90-minute solo fell becomes 53 with two hands
        /// (90 / 1.7 = 52.9, ceiling 53) and 30 with four (90 / 3.1 = 29.1, ceiling 30).
        /// </summary>
        public static string DescribeCrewEffect(int baseMinutes, int crewSize)
        {
            return $"Base {baseMinutes}m, crew {Math.Max(1, crewSize)}: {CrewAdjustedMinutes(baseMinutes, crewSize)}m "
                + $"(effective workers {EffectiveWorkers(crewSize):0.0}). Solo work is never refused; it is just slow.";
        }
    }
}
