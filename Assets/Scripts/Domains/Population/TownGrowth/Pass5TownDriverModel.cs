using System;
using System.Collections.Generic;

namespace LandLedgers.Population.TownGrowth
{
    /// <summary>
    /// Pass 5 (town growth): monthly town driver MODEL, not a
    /// production driver. Pass 1-4 consistently left production
    /// drivers to Codex; this pass does the same, honestly. The
    /// model below is what a driver would call, in what order,
    /// with what inputs, and what it must refuse - it does not
    /// call PopulationManager, MigrationDriver, TownReputation,
    /// ImmigrationConditions, ThroughTraveler, or
    /// ArrivalSettlementDriver itself, and it claims no wiring.
    ///
    /// Channel honesty, stated once and enforced in the model:
    ///   - LegacyRoll (the old 4% monthly roll, plus distress /
    ///     opportunity departure draws) is LIVE today. This model
    ///     does NOT retire it. LegacyRollStillLive defaults true.
    ///     Retiring it is a Kennedy decision; WithLegacyRollRetired
    ///     returns a copy with the flag false, documented as a
    ///     decision effect, never applied by default.
    ///   - This model uses Traveler + Recruitment channels only.
    ///     It never claims the three channels are unified, and it
    ///     never runs all three in parallel as if they were one.
    ///   - Slice departure gate: AllowDepartures is NOT wired to
    ///     SuppressMigrantArrivals today. This model carries the
    ///     gate decision as an explicit enum (Undecided by default,
    ///     meaning departures stay live on the legacy roll) and
    ///     never activates it silently.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public enum Pass5ArrivalChannel
    {
        LegacyRoll = 0,
        Traveler = 1,
        Recruitment = 2,
    }

    public enum Pass5SliceDepartureGateDecision
    {
        Undecided = 0, // departures stay live on the legacy roll; do not assume gated
        ActivateGate = 1, // Kennedy decision: wire AllowDepartures=false to SuppressMigrantArrivals
        LeaveLive = 2, // Kennedy decision: explicitly leave slice departures live
    }

    public sealed class Pass5TownInputs
    {
        public int HousingVacantUnits;
        public int HousingTotalUnits;
        public int OpenJobSlots;
        public int FilledJobSlots;
        // Caller-supplied 0-1 scores. There is NO production source
        // for growth or opinion today; a driver must supply them
        // from real registry state, or leave them at 0 and say so.
        // 0 here means "not supplied", never "town is not growing".
        public double GrowthScore01;
        public double OpinionScore01;
        public bool GrowthSupplied;
        public bool OpinionSupplied;
        public int SnapshotDayIndex;
        public int CurrentDayIndex;
        // Staleness threshold, caller-supplied test parameter (the
        // existing TownReputation helper defaults to 7 days).
        public int MaxSnapshotAgeDays = 7;
        // Caller-supplied weights, never canon. Normalised if they
        // do not sum to 1, mirroring TownReputationModel.
        public double WorkWeight = 0.5;
        public double HousingWeight = 0.3;
        public double GrowthWeight = 0.1;
        public double OpinionWeight = 0.1;
        // Condition thresholds, caller-supplied, never canon.
        public double MinWorkAvailability01 = 0.1;
        public int MinHousingVacancies = 1;
        public double MinReputation01 = 0.5;
        public bool SliceSuppressArrivals;
        public Pass5SliceDepartureGateDecision DepartureGateDecision = Pass5SliceDepartureGateDecision.Undecided;
    }

    public sealed class Pass5TownEvaluation
    {
        public double ReputationScore01;
        public bool SnapshotStale;
        public bool WorkPasses;
        public bool HousingPasses;
        public bool ReputationPasses;
        public bool ArrivalsAllowed;
        public bool LegacyRollStillLive = true;
        public string DepartureGateState = string.Empty;
        public List<string> Reasons = new List<string>();
        public List<string> Diagnostics = new List<string>();
    }

    public static class Pass5TownDriverModel
    {
        public static Pass5TownEvaluation Evaluate(Pass5TownInputs inputs)
        {
            var result = new Pass5TownEvaluation();
            if (inputs == null)
            {
                result.Reasons.Add("Pass5TownDriver: no town inputs supplied; no arrival, no departure decision.");
                return result;
            }
            result.LegacyRollStillLive = true; // never retired by this model
            int age = inputs.CurrentDayIndex - inputs.SnapshotDayIndex;
            result.SnapshotStale = age > inputs.MaxSnapshotAgeDays;
            if (result.SnapshotStale)
            {
                result.Reasons.Add("Pass5TownDriver: reputation snapshot is stale (age " + age + " days > " + inputs.MaxSnapshotAgeDays + "); recompute before gating immigration.");
            }
            double housingAvail = inputs.HousingTotalUnits > 0
                ? (double)inputs.HousingVacantUnits / inputs.HousingTotalUnits : 0.0;
            double workAvail = (inputs.OpenJobSlots + inputs.FilledJobSlots) > 0
                ? (double)inputs.OpenJobSlots / (inputs.OpenJobSlots + inputs.FilledJobSlots) : 0.0;
            double wSum = inputs.WorkWeight + inputs.HousingWeight + inputs.GrowthWeight + inputs.OpinionWeight;
            if (wSum <= 0.0)
            {
                result.Diagnostics.Add("Pass5TownDriver: weights sum to zero; reputation defaults to 0, never invented.");
                result.ReputationScore01 = 0.0;
            }
            else
            {
                if (Math.Abs(wSum - 1.0) > 0.0001)
                {
                    result.Diagnostics.Add("Pass5TownDriver: weights normalised (sum " + wSum + " != 1); weights are caller parameters, not canon.");
                }
                result.ReputationScore01 =
                    (inputs.WorkWeight * workAvail
                    + inputs.HousingWeight * housingAvail
                    + inputs.GrowthWeight * inputs.GrowthScore01
                    + inputs.OpinionWeight * inputs.OpinionScore01) / wSum;
            }
            if (!inputs.GrowthSupplied) result.Diagnostics.Add("Pass5TownDriver: growth score not supplied; used as 0, not as evidence the town is not growing.");
            if (!inputs.OpinionSupplied) result.Diagnostics.Add("Pass5TownDriver: opinion score not supplied; used as 0, not as evidence residents dislike the town.");
            // Canon order: work first, then housing, then reputation.
            result.WorkPasses = workAvail >= inputs.MinWorkAvailability01;
            result.HousingPasses = inputs.HousingVacantUnits >= inputs.MinHousingVacancies;
            result.ReputationPasses = result.ReputationScore01 >= inputs.MinReputation01;
            if (!result.WorkPasses) result.Reasons.Add("Pass5TownDriver: work condition fails (availability " + workAvail + " < " + inputs.MinWorkAvailability01 + ").");
            if (!result.HousingPasses) result.Reasons.Add("Pass5TownDriver: housing condition fails (vacancies " + inputs.HousingVacantUnits + " < " + inputs.MinHousingVacancies + ").");
            if (!result.ReputationPasses) result.Reasons.Add("Pass5TownDriver: reputation condition fails (score " + result.ReputationScore01 + " < " + inputs.MinReputation01 + ").");
            if (inputs.SliceSuppressArrivals)
            {
                result.Reasons.Add("Pass5TownDriver: six-person slice suppression is active; no arrivals, regardless of conditions.");
            }
            result.ArrivalsAllowed = !result.SnapshotStale && result.WorkPasses && result.HousingPasses && result.ReputationPasses && !inputs.SliceSuppressArrivals;
            switch (inputs.DepartureGateDecision)
            {
                case Pass5SliceDepartureGateDecision.ActivateGate:
                    result.DepartureGateState = "Slice departure gate ACTIVATED by Kennedy decision: AllowDepartures=false wired to SuppressMigrantArrivals.";
                    break;
                case Pass5SliceDepartureGateDecision.LeaveLive:
                    result.DepartureGateState = "Slice departures explicitly LEFT LIVE by Kennedy decision: legacy monthly roll still departs residents.";
                    break;
                default:
                    result.DepartureGateState = "Slice departure gate UNDECIDED: departures stay live on the legacy monthly roll. Do not assume gated.";
                    break;
            }
            return result;
        }

        /// <summary>
        /// Decision effect only. Returns an evaluation copy with the
        /// legacy roll retired. This method is never called by
        /// Evaluate, and no driver in this pass calls it. Kennedy
        /// retiring the roll means: stop calling MigrationDriver
        /// arrivals/departures once traveler + recruitment channels
        /// are driven, and accept that past legacy departures stay
        /// untracked (their destinations cannot be invented).
        /// </summary>
        public static Pass5TownEvaluation WithLegacyRollRetired(Pass5TownEvaluation evaluation)
        {
            if (evaluation == null) return new Pass5TownEvaluation { LegacyRollStillLive = false };
            evaluation.LegacyRollStillLive = false;
            evaluation.Diagnostics.Add("Pass5TownDriver: legacy 4% arrival roll retired by Kennedy decision; traveler + recruitment are the only arrival channels in this model.");
            return evaluation;
        }
    }

    public sealed class Pass5TravelerVisitRecord
    {
        public string TravelerId = string.Empty;
        public string TravelerName = string.Empty;
        public bool LookingForStay;
        public int BoardingVacancies;
        public int DayIndex;
        public bool StayEligible;
        // A stay never settles by itself. The driver must run
        // ArrivalSettlementDriver.SettleAndApply next, or the
        // traveler remains a visitor, never a silent resident.
        public string NextStep = "visitor-only";
        public List<string> Reasons = new List<string>();
    }

    public static class Pass5TravelerModel
    {
        /// <summary>
        /// Generates visit records at a caller-supplied frequency.
        /// No PersonState, household, or job is created here, ever.
        /// Frequency is a caller parameter, never canon. A visit
        /// with LookingForStay=false is word-of-mouth only.
        /// </summary>
        public static List<Pass5TravelerVisitRecord> GenerateVisits(
            int count, int boardingVacancies, Pass5TownEvaluation town, int dayIndex)
        {
            var visits = new List<Pass5TravelerVisitRecord>();
            for (int i = 0; i < count; i++)
            {
                visits.Add(new Pass5TravelerVisitRecord
                {
                    TravelerId = "traveler-" + dayIndex + "-" + i,
                    TravelerName = string.Empty, // caller supplies a real name, never invented here
                    LookingForStay = false, // caller sets this per visit from real traveler data
                    BoardingVacancies = boardingVacancies,
                    DayIndex = dayIndex,
                    StayEligible = false,
                    NextStep = "visitor-only",
                });
            }
            if (town != null && town.ArrivalsAllowed)
            {
                foreach (var v in visits)
                {
                    v.Reasons.Add("Pass5Traveler: town conditions allow arrivals; stay eligibility still requires LookingForStay, boarding vacancy, open jobs, and housing - see EvaluateStay.");
                }
            }
            return visits;
        }

        public static Pass5TravelerVisitRecord EvaluateStay(
            Pass5TravelerVisitRecord visit, Pass5TownEvaluation town, int openJobSlots, int housingVacancies)
        {
            if (visit == null) return null;
            visit.StayEligible = visit.LookingForStay
                && visit.BoardingVacancies > 0
                && openJobSlots > 0
                && housingVacancies > 0
                && town != null && town.ArrivalsAllowed;
            visit.NextStep = visit.StayEligible
                ? "run-ArrivalSettlementDriver.SettleAndApply"
                : "visitor-only";
            if (!visit.StayEligible)
            {
                visit.Reasons.Add("Pass5Traveler: stay refused (needs LookingForStay + boarding vacancy + open jobs + housing vacancies + allowed arrivals); traveler remains a visitor, word-of-mouth only.");
            }
            return visit;
        }
    }

    public enum Pass5DepartureDestinationKind
    {
        LegacyUntracked = 0, // legacy roll departure: destination unknown, never invented
        FamilyContact = 1,
        PreviousHome = 2,
        SupplierTown = 3,
        RouteTown = 4,
        NamedNew = 5,
    }

    public sealed class Pass5DepartureRecord
    {
        public int PersonId = -1;
        public Pass5DepartureDestinationKind DestinationKind = Pass5DepartureDestinationKind.LegacyUntracked;
        public string DestinationName = string.Empty; // NamedNew only, caller-supplied, never randomised
        public bool MembershipClosed;
        public bool EmploymentClosed;
        // F08, still required: household dissolution, property
        // retain/transfer, and debt retention are NOT closed by
        // OutmigrationCloseout (membership + employment only).
        public bool HouseholdPropertyDebtStillRequired = true;
        public double ReputationPenalty01; // 0.2 unpaid wages, else 0.1, per existing code - calibration, not canon
        public List<string> Reasons = new List<string>();
    }

    public static class Pass5DepartureModel
    {
        /// <summary>
        /// New-channel departure: destination is mandatory. A
        /// LegacyUntracked destination is refused for the new
        /// channel - it is the legacy record shape, not a new
        /// departure. Legacy departures are never rewritten with
        /// an invented destination; they stay LegacyUntracked.
        /// Closeout here mirrors OutmigrationCloseout: membership
        /// + employment only. Household/property/debt remains open.
        /// </summary>
        public static Pass5DepartureRecord DepartNewChannel(
            int personId, Pass5DepartureDestinationKind destinationKind, string destinationName, bool unpaidWages)
        {
            var record = new Pass5DepartureRecord { PersonId = personId, DestinationKind = destinationKind };
            if (destinationKind == Pass5DepartureDestinationKind.LegacyUntracked)
            {
                record.Reasons.Add("Pass5Departure: new-channel departure refused - destination is mandatory, never 'off-map' alone, never randomised.");
                return record;
            }
            if (destinationKind == Pass5DepartureDestinationKind.NamedNew && string.IsNullOrWhiteSpace(destinationName))
            {
                record.Reasons.Add("Pass5Departure: NamedNew destination needs a caller-supplied name; refused, never invented.");
                return record;
            }
            record.DestinationName = destinationName ?? string.Empty;
            record.MembershipClosed = true;
            record.EmploymentClosed = true;
            record.HouseholdPropertyDebtStillRequired = true;
            record.ReputationPenalty01 = unpaidWages ? 0.2 : 0.1;
            record.Reasons.Add("Pass5Departure: new-channel departure recorded; membership + employment closed; household/property/debt handling still required (F08), owned by the town driver model, not done here.");
            return record;
        }

        /// <summary>
        /// Legacy departure, preserved as-is. No destination is
        /// invented, no record is rewritten, and FindDepartedAt
        /// Destination cannot find it (Phase 4 F12, still open).
        /// </summary>
        public static Pass5DepartureRecord PreserveLegacyDeparture(int personId)
        {
            return new Pass5DepartureRecord
            {
                PersonId = personId,
                DestinationKind = Pass5DepartureDestinationKind.LegacyUntracked,
                DestinationName = string.Empty,
                MembershipClosed = true,
                EmploymentClosed = true,
                HouseholdPropertyDebtStillRequired = true,
                Reasons = new List<string>
                {
                    "Pass5Departure: legacy departure preserved untracked; destination unknown and never invented. Unifying departures means routing FUTURE departures through DepartNewChannel, not rewriting past ones.",
                },
            };
        }
    }
}
