using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Estates;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P1-2: one recorded pregnancy. Conception happens only between
    /// spouses (a real kinship relationship) — never to hit a population target
    /// and never from household composition alone. Persisted on the population
    /// save DTO; the birth driver delivers through the lifecycle authority.
    /// </summary>
    [Serializable]
    public sealed class PregnancyRecord
    {
        public int motherPersonId = -1;
        public int fatherPersonId = -1;
        public int householdId = -1;
        public int conceptionDayIndex = -1;
        public int dueDayIndex = -1;

        public PregnancyRecord() { }
    }

    /// <summary>
    /// Pass V P1-2: inputs for the demographic drivers. Plain data; the drivers
    /// themselves are stateless and deterministic (see DemographicRng): the same
    /// world seed + day + person state always yields the same decision, so
    /// save/load and tick-size changes cannot double-apply a demographic event.
    /// </summary>
    public sealed class DemographicDriverContext
    {
        public PopulationState Population;
        public KinshipRegistry Kinship;
        public HouseholdLifecycleManager Lifecycle;
        public HouseholdMembershipRegistry Memberships;
        public EstateService Estates;
        public EntityIdRegistry Ids;
        public IDueEventScheduler Scheduler;
        public HouseholdLedgerRegistry Ledgers;
        public EmploymentRelationshipRegistry Employments;
        public CalendarAnchor Anchor = CalendarAnchor.Default;
        public SimulationDate CurrentDate;
        public int CurrentDayIndex;
        public int WorldSeed;
        public string[] MaleNames = Array.Empty<string>();
        public string[] FemaleNames = Array.Empty<string>();
        public List<PregnancyRecord> Pregnancies = new List<PregnancyRecord>();
        public List<string> Diagnostics = new List<string>();
        public float GameSecondsPerDay = 86400f;

        /// <summary>
        /// Pass XL vertical slice: when false, MigrationDriver skips new
        /// migrant ARRIVALS only. Default true, so ordinary scenarios are
        /// byte-for-byte unchanged. Departures (MigrateOut) and births are
        /// NOT gated by this flag: Kennedy's slice request was "no
        /// immigration", not "no departures, no births", and births cannot
        /// occur inside a 30-day slice anyway (marriage plus 280-day
        /// gestation). Gating births/departures is a separate decision for
        /// Kennedy if a longer sealed run is ever wanted.
        /// PopulationManager sets this false for the six-person
        /// development fixture on New Game (see SuppressMigrantArrivals).
        /// Phase 2 persistence: PassXlSliceSaveCoordinator persists this
        /// as PassXlSliceSaveDto.suppressMigrantArrivals and restores it
        /// onto PopulationManager on load, so a Continue keeps the slice
        /// gate (an earlier revision of this comment said otherwise).
        /// </summary>
        public bool AllowNewArrivals = true;

        /// <summary>
        /// Overnight 2026-11-04: development-town mortality switch. Default
        /// true, so every existing scenario and test is unchanged. When false,
        /// MortalityDriver.RecordDeath suppresses the fatal transition
        /// (logging it once per person in <see cref="SuppressedDeathPersonIds"/>)
        /// instead of recording the death, ending employment, or opening an
        /// estate. Nonfatal consequences (hunger, illness, fatigue, hardship)
        /// are simulated elsewhere and are NOT suppressed by this flag.
        /// PopulationManager sets this from DevelopmentTownConfiguration for
        /// the development town; all other scenarios leave it true.
        /// </summary>
        public bool EnableMortality = true;

        /// <summary>
        /// Overnight 2026-11-04: development-town fertility switch. Default
        /// true, so every existing scenario and test is unchanged. When false,
        /// BirthDriver starts no new pregnancy and delivers no birth; due
        /// pregnancies are deferred (recorded once per mother in
        /// <see cref="DeferredBirthMotherIds"/>) and deliver once the flag is
        /// true again. PopulationManager sets this from
        /// DevelopmentTownConfiguration for the development town.
        /// </summary>
        public bool EnableFertility = true;

        /// <summary>
        /// Person ids for which a suppressed death has already been logged.
        /// Owned by PopulationManager (the context is rebuilt daily, so a
        /// set on the context alone could not dedupe across days). Null-safe:
        /// drivers treat a null set as "log this occurrence" within the call.
        /// Session-only by design: it is not persisted, so a restored game
        /// may log one further suppression for the same person.
        /// </summary>
        public HashSet<int> SuppressedDeathPersonIds = new HashSet<int>();

        /// <summary>
        /// Mother person ids for which a deferred birth has already been
        /// logged while fertility is disabled. Same ownership and
        /// session-only rules as <see cref="SuppressedDeathPersonIds"/>.
        /// </summary>
        public HashSet<int> DeferredBirthMotherIds = new HashSet<int>();

        /// <summary>
        /// Pass XL phase 4 (finding F09): when false, MigrationDriver
        /// skips departures (MigrateOut) as well as arrivals. Default
        /// true, so every existing scenario and test is byte-for-byte
        /// unchanged. This flag is the MECHANISM for the slice-departure
        /// gate only: PopulationManager does NOT set it from
        /// SuppressMigrantArrivals, because whether six-person slice
        /// residents may leave is a Kennedy decision (the new
        /// Outmigration channel says no departures in the slice; the old
        /// driver runs them). Wiring the slice gate to this flag is that
        /// one-line decision, not a silent change — see the phase 4
        /// findings (F09) and the Kennedy decision list.
        /// </summary>
        public bool AllowDepartures = true;
    }

    /// <summary>
    /// Pass V P1-2: deterministic per-decision randomness. Hash-based draws from
    /// (worldSeed, dayIndex, personId, salt) — no persisted RNG state, so driver
    /// decisions are stable across save/load and independent of tick size.
    /// </summary>
    public static class DemographicRng
    {
        public static double Draw01(int worldSeed, int dayIndex, int personId, string salt)
        {
            unchecked
            {
                int h = worldSeed;
                h = h * 31 + dayIndex;
                h = h * 31 + personId;
                h = h * 31 + StableHash(salt);
                uint x = (uint)h ^ 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return (x & 0x7fffffff) / (double)int.MaxValue;
            }
        }

        public static int StableHash(string value)
        {
            unchecked
            {
                int hash = 23;
                if (value != null)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        hash = hash * 31 + value[i];
                    }
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// Pass V P1-2: birth scheduling through the lifecycle authority.
    ///
    /// Conceptions: monthly evaluation over married (spouse-edge) co-resident
    /// couples, mother of childbearing age. Fecundity is a smooth biological
    /// curve by the mother's age — calibration, not a target: nothing here
    /// aims at a population count.
    ///
    /// Births: daily evaluation of due pregnancies through
    /// HouseholdLifecycleManager.RecordBirth with provenance Exact, kinship
    /// edges to both parents, and birthday scheduling for the newborn.
    /// </summary>
    public static class BirthDriver
    {
        /// <summary>Human gestation in days (biological constant, documented).</summary>
        public const int GestationDays = 280;

        /// <summary>Monthly conception probability by the mother's completed age.</summary>
        public static double MonthlyFecundity(int motherAge)
        {
            if (motherAge < 16 || motherAge > 44)
            {
                return 0d;
            }

            if (motherAge <= 19)
            {
                return 0.12;
            }

            if (motherAge <= 29)
            {
                return 0.22;
            }

            if (motherAge <= 34)
            {
                return 0.16;
            }

            if (motherAge <= 39)
            {
                return 0.09;
            }

            return 0.03;
        }

        /// <summary>
        /// Monthly pass (the caller gates on a fixed day-of-month). Creates
        /// PregnancyRecords for conceiving couples.
        /// </summary>
        public static void EvaluateConceptions(DemographicDriverContext ctx)
        {
            if (ctx == null || ctx.Population == null || ctx.Population.people == null || ctx.Kinship == null)
            {
                return;
            }

            // Overnight 2026-11-04: fertility disabled (development town) —
            // no new pregnancy begins. Existing pregnancies are untouched here;
            // EvaluateBirths defers their delivery while disabled.
            if (!ctx.EnableFertility)
            {
                return;
            }

            ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            for (int i = 0; i < ctx.Population.people.Count; i++)
            {
                PersonState wife = ctx.Population.people[i];
                if (wife == null || !wife.isFemale || wife.deathDayIndex >= 0 || wife.isOffMap)
                {
                    continue;
                }

                // Pass IX (A2-02): the current (living) spouse — a widow's
                // historical edge to her dead husband must not be picked.
                int husbandId = MarriageDriver.CurrentSpouseId(ctx, wife.id);
                if (husbandId < 0)
                {
                    continue;
                }

                PersonState husband = ctx.Population.GetPerson(husbandId);
                if (husband == null || husband.deathDayIndex >= 0 || husband.isOffMap)
                {
                    continue;
                }

                // Legitimate conception: a married couple sharing a household.
                // Pass IX (A2-01): co-residence reads the membership AUTHORITY
                // (HouseholdMembershipRegistry) — the legacy PersonState
                // .householdId field is never written by marriage or birth,
                // so gating on it made every post-generation marriage sterile
                // (and let sim-born children pass spuriously against id 0).
                if (ctx.Memberships == null)
                {
                    continue;
                }

                int wifeHouseholdId = ctx.Memberships.GetActiveHouseholdId(wife.id);
                int husbandHouseholdId = ctx.Memberships.GetActiveHouseholdId(husband.id);
                if (wifeHouseholdId < 0 || wifeHouseholdId != husbandHouseholdId)
                {
                    continue;
                }

                if (IsPregnant(ctx, wife.id))
                {
                    continue;
                }

                int motherAge = wife.GetTrueAge(year, month, day);
                double fecundity = MonthlyFecundity(motherAge);
                if (fecundity <= 0d)
                {
                    continue;
                }

                double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, wife.id, "conception");
                if (draw < fecundity)
                {
                    ctx.Pregnancies.Add(new PregnancyRecord
                    {
                        motherPersonId = wife.id,
                        fatherPersonId = husband.id,
                        householdId = wifeHouseholdId,
                        conceptionDayIndex = ctx.CurrentDayIndex,
                        dueDayIndex = ctx.CurrentDayIndex + GestationDays,
                    });
                    ctx.Diagnostics.Add(
                        $"BirthDriver: P{wife.id} (age {motherAge}) conceived; due in {GestationDays} days.");
                }
            }
        }

        /// <summary>Daily pass: delivers every due pregnancy as a live birth.</summary>
        public static void EvaluateBirths(DemographicDriverContext ctx)
        {
            if (ctx == null || ctx.Population == null || ctx.Lifecycle == null || ctx.Pregnancies == null)
            {
                return;
            }

            ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            for (int i = ctx.Pregnancies.Count - 1; i >= 0; i--)
            {
                PregnancyRecord pregnancy = ctx.Pregnancies[i];
                if (pregnancy == null || pregnancy.dueDayIndex > ctx.CurrentDayIndex)
                {
                    continue;
                }

                // Overnight 2026-11-04: fertility disabled (development town) —
                // a due birth is deferred, not delivered and not deleted. The
                // pregnancy stays in the list and delivers once fertility is
                // re-enabled (possibly past its due day, which is the honest
                // consequence of pausing births). Logged once per mother.
                if (!ctx.EnableFertility)
                {
                    var deferred = ctx.DeferredBirthMotherIds ?? new HashSet<int>();
                    if (deferred.Add(pregnancy.motherPersonId))
                    {
                        ctx.Diagnostics.Add(
                            $"BirthDriver: birth deferred for P{pregnancy.motherPersonId} (due day {pregnancy.dueDayIndex}, fertility disabled).");
                    }

                    continue;
                }

                ctx.Pregnancies.RemoveAt(i);
                PersonState mother = ctx.Population.GetPerson(pregnancy.motherPersonId);
                if (mother == null || mother.deathDayIndex >= 0)
                {
                    ctx.Diagnostics.Add(
                        $"BirthDriver: pregnancy for P{pregnancy.motherPersonId} lost (mother dead or gone); no birth recorded.");
                    continue;
                }

                // Pass IX (A2-01): when the father is dead or unknown by the
                // time of the birth, the child belongs to the MOTHER's
                // household (her current membership authority), never to a
                // stale household stamped for a father who is gone.
                int householdId = pregnancy.householdId;
                PersonState father = pregnancy.fatherPersonId >= 0
                    ? ctx.Population.GetPerson(pregnancy.fatherPersonId) : null;
                if ((father == null || father.deathDayIndex >= 0) && ctx.Memberships != null)
                {
                    int motherHouseholdId = ctx.Memberships.GetActiveHouseholdId(mother.id);
                    if (motherHouseholdId >= 0)
                    {
                        householdId = motherHouseholdId;
                    }
                }

                bool isFemale = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, mother.id, "birth-sex") < 0.5;
                string firstName = PickName(ctx, isFemale, mother.id);
                var birthDate = new BirthDate(year, month, day, BirthDateProvenance.Exact, "Live birth recorded by the birth driver.");
                PersonState child = ctx.Lifecycle.RecordBirth(
                    householdId, mother.id, pregnancy.fatherPersonId,
                    firstName, mother.lastName, ctx.CurrentDayIndex, birthDate);
                if (child == null)
                {
                    ctx.Diagnostics.Add($"BirthDriver: RecordBirth refused for P{mother.id}'s child.");
                    continue;
                }

                child.isFemale = isFemale;
                child.RefreshDerivedAge(year, month, day);
                string refusal = BirthdayService.EnsureScheduledForPerson(
                    child, ctx.Scheduler, ctx.Anchor, ctx.CurrentDayIndex, ctx.GameSecondsPerDay);
                if (!string.IsNullOrEmpty(refusal))
                {
                    ctx.Diagnostics.Add(refusal);
                }

                ctx.Diagnostics.Add(
                    $"BirthDriver: P{child.id} ({firstName} {child.lastName}) born to P{mother.id} on {year:D4}-{month:D2}-{day:D2}.");
            }
        }

        private static bool IsPregnant(DemographicDriverContext ctx, int motherId)
        {
            for (int i = 0; i < ctx.Pregnancies.Count; i++)
            {
                PregnancyRecord p = ctx.Pregnancies[i];
                if (p != null && p.motherPersonId == motherId)
                {
                    return true;
                }
            }

            return false;
        }

        private static string PickName(DemographicDriverContext ctx, bool isFemale, int salt)
        {
            string[] pool = isFemale ? ctx.FemaleNames : ctx.MaleNames;
            if (pool == null || pool.Length == 0)
            {
                return isFemale ? "Anna" : "Thomas";
            }

            double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, salt, "birth-name");
            return pool[(int)(draw * pool.Length) % pool.Length];
        }
    }

    /// <summary>
    /// Pass V P1-2: mortality from supported health and demographic conditions.
    ///
    /// Daily per-person death probability = an age-graded annual base rate
    /// (historically-flavored 1890s calibration: high infant mortality, rising
    /// old-age mortality) multiplied by the active health condition severity.
    /// Deaths flow through HouseholdLifecycleManager.RecordDeath (membership
    /// ends, the Person record and id are retained) and open an estate via
    /// EstateService with heirs from the real kinship registry.
    ///
    /// Explicitly NOT modeled: debt-threshold suicide or any economic-trigger
    /// death. Health and age are the only inputs.
    /// </summary>
    public static class MortalityDriver
    {
        /// <summary>Annual base death probability by completed age (1890s-flavored calibration).</summary>
        public static double AnnualBaseRate(int age)
        {
            if (age < 1)
            {
                return 0.12;
            }

            if (age < 5)
            {
                return 0.010;
            }

            if (age < 10)
            {
                return 0.004;
            }

            if (age < 18)
            {
                return 0.003;
            }

            if (age < 30)
            {
                return 0.004;
            }

            if (age < 45)
            {
                return 0.006;
            }

            if (age < 55)
            {
                return 0.012;
            }

            if (age < 65)
            {
                return 0.025;
            }

            if (age < 75)
            {
                return 0.060;
            }

            return Math.Min(0.32, 0.12 + (age - 75) * 0.02);
        }

        public static double HealthMultiplier(PersonState person)
        {
            if (person == null || person.health == null || !person.health.HasActiveCondition)
            {
                return 1d;
            }

            bool serious = person.health.severity == HealthConditionSeverity.Serious;
            bool illness = person.health.conditionKind == HealthConditionKind.Illness;
            if (serious)
            {
                return illness ? 5d : 4d;
            }

            return illness ? 1.5d : 1.3d;
        }

        public static double DailyDeathProbability(PersonState person, int year, int month, int day)
        {
            if (person == null || person.deathDayIndex >= 0)
            {
                return 0d;
            }

            double annual = AnnualBaseRate(person.GetTrueAge(year, month, day)) * HealthMultiplier(person);
            return 1d - Math.Pow(1d - Math.Min(0.99, annual), 1d / 365d);
        }

        /// <summary>Daily pass: rolls mortality for every living person.</summary>
        public static void EvaluateDay(DemographicDriverContext ctx)
        {
            if (ctx == null || ctx.Population == null || ctx.Population.people == null)
            {
                return;
            }

            ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            for (int i = 0; i < ctx.Population.people.Count; i++)
            {
                PersonState person = ctx.Population.people[i];
                if (person == null || person.deathDayIndex >= 0)
                {
                    continue;
                }

                double probability = DailyDeathProbability(person, year, month, day);
                if (probability <= 0d)
                {
                    continue;
                }

                double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, person.id, "mortality");
                if (draw < probability)
                {
                    RecordDeath(ctx, person, DescribeCause(person, year, month, day));
                }
            }
        }

        /// <summary>
        /// Records a death through the lifecycle authority, cancels the birthday
        /// event, and opens the estate. Public so tests and the household-split
        /// driver can route every death through the same path.
        /// Overnight 2026-11-04: when ctx.EnableMortality is false, the death
        /// is suppressed and logged instead (see the gate above); no side
        /// effect of a real death happens.
        /// </summary>
        public static void RecordDeath(DemographicDriverContext ctx, PersonState person, string cause)
        {
            if (ctx == null || person == null || person.deathDayIndex >= 0)
            {
                return;
            }

            // Overnight 2026-11-04: mortality disabled (development town).
            // This is the single production death funnel, so gating here
            // covers old age, illness, injury, and sudden events alike:
            // nothing is recorded, no estate opens, no employment ends,
            // ownership and membership are untouched, and the person stays
            // in the population. Nonfatal consequences continue to be
            // simulated by their own systems. The prevented death is logged
            // once per person (PopulationManager owns the set, because this
            // context is rebuilt daily) with the absolute simulation day
            // and calendar date, so a person in a continuously fatal
            // condition does not spam the log on every day boundary.
            if (!ctx.EnableMortality)
            {
                var suppressed = ctx.SuppressedDeathPersonIds ?? new HashSet<int>();
                if (suppressed.Add(person.id))
                {
                    ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int sYear, out int sMonth, out int sDay);
                    ctx.Diagnostics.Add(
                        $"[DevelopmentPopulation] Death suppressed Person=P{person.id} Cause={cause} Day={ctx.CurrentDayIndex} Date={sYear:D4}-{sMonth:D2}-{sDay:D2} Mortality disabled");
                }

                return;
            }

            bool recorded = ctx.Lifecycle != null && ctx.Lifecycle.RecordDeath(person.id, ctx.CurrentDayIndex, cause);
            BirthdayService.CancelForPerson(person.id, ctx.Scheduler);

            // Pass VI cohesion fix: death ends employment. Without this, payroll keeps
            // paying Active employments for the deceased (a money sink), because
            // WageSettlementService settles every Active employment with no death check.
            if (ctx.Employments != null)
            {
                int ended = ctx.Employments.EndEmploymentsForDeceased(person.id, ctx.CurrentDayIndex);
                if (ended > 0)
                {
                    ctx.Diagnostics.Add(
                        $"MortalityDriver: ended {ended} employment(s) for deceased P{person.id}.");
                }
            }

            if (ctx.Estates != null && ctx.Kinship != null)
            {
                Estate estate = ctx.Estates.OpenEstate(
                    ctx.Ids, person.id, person.DisplayName, ctx.Kinship, ctx.CurrentDayIndex, ctx.Diagnostics);
                if (estate != null)
                {
                    ctx.Diagnostics.Add(
                        $"MortalityDriver: estate {estate.EstateId} opened for {person.DisplayName} (P{person.id}) with {estate.Heirs.Count} heir entries.");
                }
            }

            ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            ctx.Diagnostics.Add(
                $"MortalityDriver: P{person.id} ({person.DisplayName}, age {person.GetTrueAge(year, month, day)}) died: {cause}." +
                (recorded ? string.Empty : " WARNING: lifecycle RecordDeath refused."));
        }

        private static string DescribeCause(PersonState person, int year, int month, int day)
        {
            if (person.health != null && person.health.HasActiveCondition)
            {
                string severity = person.health.severity == HealthConditionSeverity.Serious ? "serious" : "mild";
                string kind = person.health.conditionKind == HealthConditionKind.Injury ? "injury" : "illness";
                return $"{severity} {kind}";
            }

            int age = person.GetTrueAge(year, month, day);
            if (age < 1)
            {
                return "infant mortality";
            }

            if (age >= 70)
            {
                return "old age";
            }

            return "sudden illness";
        }
    }
}
