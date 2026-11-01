using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 3, item 5A: through-traveler immigration.
    ///
    /// A NEW arrival channel, distinct from conversation recruitment
    /// (item 3) and from natural immigration, and the INTENDED
    /// replacement for the old dice-roll in-migration this codebase
    /// historically carried: a 4% monthly roll creating a person into
    /// the smallest household with no job reason, no housing check, and
    /// no contact awareness. That roll is exactly what this channel must
    /// NOT do — but the old roll is still live in MigrationDriver
    /// (phase 4 finding F10) until Kennedy retires it, so this class is
    /// an additional channel today, not yet the replacement.
    ///
    /// The model, in canon order (Canon XXXI §3.4 and §7.4: awareness or
    /// referral -> opportunity evaluation -> acceptable work/income path
    /// -> available accommodation -> travel -> physical arrival ->
    /// residence stabilization; work/business opportunity -> feasible
    /// housing -> household migration -> relationship/word-of-mouth
    /// spread -> more workers/customers/businesses):
    ///   1. A traveler on a route (westbound in the frontier case) stops
    ///      in town, lodges first at a boarding house (BoardingHouseShopRuntime:
    ///      room inventory + boarder register + reservations exist in code;
    ///      this class does NOT fake a boarding house — the caller
    ///      supplies boarding vacancies from that runtime, or 0 if no
    ///      boarding house exists in the slice), visits the General Store
    ///      / livery / town services, and is recorded as a visit.
    ///   2. If the traveler is already looking for somewhere to stay,
    ///      they STAY only when the same item-3 conditions pass:
    ///      housing vacancies, open jobs (work is the biggest factor),
    ///      and the town reputation snapshot at or above a caller-supplied
    ///      threshold, with the six-person slice gate respected. A stay
    ///      does NOT by itself settle them: the production driver must
    ///      run ArrivalSettlement (item 5B.1) next, or the traveler
    ///      remains a visitor, never a silent resident.
    ///   3. A traveler who passes on is still a reputation channel:
    ///      they carry word of town conditions back along the route and
    ///      to other travelers. A good stop can therefore produce a later
    ///      arrival even when the traveler themselves does not stay.
    ///      That later arrival is a LEAD (a potential future traveler),
    ///      not a created person.
    ///
    /// Traveler frequency, boarding capacity rules, and reputation
    /// weighting are caller-supplied test parameters, never canon.
    /// This class creates no PersonState, no household, and no job.
    /// Plain C#, no Unity dependency.
    /// </summary>
    public sealed class ThroughTravelerVisit
    {
        public string TravelerId = string.Empty;
        public string TravelerName = string.Empty;
        public string RouteDirection = string.Empty; // e.g. "westbound"
        public bool LookingForStay;
        public int DayIndex;
        public int BoardingVacancies; // caller-supplied, from BoardingHouseShopRuntime room inventory, or 0
        public bool VisitedGeneralStore;
        public bool VisitedLivery;
    }

    public enum TravelerOutcomeKind
    {
        Unspecified = 0,
        Stays = 1, // subject to ArrivalSettlement, not settled by this class
        PassesOn = 2,
    }

    public sealed class TravelerOutcome
    {
        public TravelerOutcomeKind Outcome;
        public bool WordOfMouthSpread; // true for a good stop, even on PassesOn
        public double WordOfMouthReputationScore01;
        public List<string> Reasons = new List<string>();
    }

    public static class ThroughTravelerImmigration
    {
        /// <summary>
        /// Pass XL phase 5 (finding R04): the staleness-aware form of
        /// <see cref="EvaluateVisit"/>. ImmigrationConditions has long
        /// had EvaluateAtDay, which refuses a stale reputation snapshot,
        /// but the traveler channel read any snapshot regardless of age.
        /// This overload refuses a stale snapshot (passes on, no
        /// word-of-mouth spread from an untrusted score) and otherwise
        /// delegates to EvaluateVisit unchanged. A null snapshot is not
        /// stale — it delegates, and EvaluateVisit scores it 0. The
        /// original EvaluateVisit is unchanged, so existing callers
        /// and tests are unaffected; drivers that gate on reputation
        /// should prefer this overload.
        /// </summary>
        public static TravelerOutcome EvaluateVisitAtDay(
            ThroughTravelerVisit visit, TownReputationSnapshot reputation,
            int currentDayIndex, int maxReputationAgeDays,
            int housingVacancies, int openJobs, bool allowNewArrivals,
            ImmigrationThresholds thresholds)
        {
            if (reputation != null && reputation.IsStale(currentDayIndex, maxReputationAgeDays))
            {
                var stale = new TravelerOutcome
                {
                    Outcome = TravelerOutcomeKind.PassesOn,
                    WordOfMouthSpread = false,
                    WordOfMouthReputationScore01 = reputation.Score01,
                };
                stale.Reasons.Add(
                    $"ThroughTraveler: reputation snapshot from day {reputation.ComputedDayIndex} is stale at day {currentDayIndex} " +
                    $"(max age {maxReputationAgeDays} days); traveler passes on and no word of mouth is spread from a stale score. Recompute before gating.");
                return stale;
            }
            return EvaluateVisit(visit, reputation, housingVacancies, openJobs, allowNewArrivals, thresholds);
        }

        public static TravelerOutcome EvaluateVisit(
            ThroughTravelerVisit visit, TownReputationSnapshot reputation,
            int housingVacancies, int openJobs, bool allowNewArrivals,
            ImmigrationThresholds thresholds)
        {
            var outcome = new TravelerOutcome();
            if (visit == null)
            {
                outcome.Outcome = TravelerOutcomeKind.PassesOn;
                outcome.Reasons.Add("ThroughTraveler: null visit, passes on.");
                return outcome;
            }
            double score = reputation != null ? reputation.Score01 : 0;
            outcome.WordOfMouthReputationScore01 = score;
            // Word of mouth is reputation, not approval: a good stop
            // spreads good word, a bad stop spreads bad word. Only a
            // good stop (at or above threshold) is a positive channel.
            outcome.WordOfMouthSpread = reputation != null
                && thresholds != null && score >= thresholds.MinReputationScore01;

            if (!allowNewArrivals)
            {
                outcome.Outcome = TravelerOutcomeKind.PassesOn;
                outcome.Reasons.Add("ThroughTraveler: six-person slice suppression active; traveler passes on, no arrival.");
                outcome.Reasons.Add(outcome.WordOfMouthSpread
                    ? "ThroughTraveler: the visit was still a good stop, so word of mouth is recorded for later play (no arrival in the slice)."
                    : "ThroughTraveler: word of mouth is not positive at this reputation score.");
                return outcome;
            }
            if (!visit.LookingForStay)
            {
                outcome.Outcome = TravelerOutcomeKind.PassesOn;
                outcome.Reasons.Add("ThroughTraveler: traveler is not looking for somewhere to stay; they pass on after the stop.");
                return outcome;
            }
            if (visit.BoardingVacancies <= 0 && housingVacancies <= 0)
            {
                outcome.Outcome = TravelerOutcomeKind.PassesOn;
                outcome.Reasons.Add("ThroughTraveler: no boarding vacancy and no housing vacancy; a traveler cannot stay with nowhere to lodge.");
                return outcome;
            }
            if (openJobs < (thresholds?.MinOpenJobs ?? 1))
            {
                outcome.Outcome = TravelerOutcomeKind.PassesOn;
                outcome.Reasons.Add($"ThroughTraveler: no work — {openJobs} open jobs, need at least {thresholds?.MinOpenJobs ?? 1}; traveler passes on.");
                return outcome;
            }
            if (housingVacancies < (thresholds?.MinHousingVacancies ?? 1))
            {
                outcome.Outcome = TravelerOutcomeKind.PassesOn;
                outcome.Reasons.Add($"ThroughTraveler: no housing — {housingVacancies} vacancies, need at least {thresholds?.MinHousingVacancies ?? 1}; traveler passes on.");
                return outcome;
            }
            if (reputation == null || score < (thresholds?.MinReputationScore01 ?? 0))
            {
                outcome.Outcome = TravelerOutcomeKind.PassesOn;
                outcome.Reasons.Add("ThroughTraveler: town reputation below threshold; traveler passes on.");
                return outcome;
            }

            outcome.Outcome = TravelerOutcomeKind.Stays;
            outcome.Reasons.Add(
                "ThroughTraveler: traveler stays, SUBJECT TO ArrivalSettlement (housing + household + job/explicit plan). " +
                "This class does not settle them, create a person, or assign a job.");
            return outcome;
        }

        /// <summary>
        /// A later arrival lead from a passing traveler's word of mouth.
        /// Returns true only when the word was positive AND current
        /// conditions still pass; otherwise no lead is created. A lead
        /// is not a person: the production driver must still create a
        /// traveler visit and run EvaluateVisit + ArrivalSettlement.
        /// </summary>
        public static bool WordOfMouthProducesLaterArrival(
            TravelerOutcome priorOutcome, int housingVacancies, int openJobs,
            bool allowNewArrivals, ImmigrationThresholds thresholds)
        {
            if (priorOutcome == null || !priorOutcome.WordOfMouthSpread) return false;
            if (!allowNewArrivals) return false;
            if (thresholds == null) return false;
            return housingVacancies >= thresholds.MinHousingVacancies
                && openJobs >= thresholds.MinOpenJobs
                && priorOutcome.WordOfMouthReputationScore01 >= thresholds.MinReputationScore01;
        }
    }
}
