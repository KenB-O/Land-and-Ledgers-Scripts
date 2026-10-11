using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Worker C (Pass III): household financial pressure -> bounded decision.
    ///
    /// Canon: XXXI Parts I/V — the central design principle. A household seeks
    /// income because of real circumstances (declining reserves, rent due,
    /// dependents, job loss), never because the simulation needs another worker,
    /// never via a universal highest-wage rule, and never via an arbitrary
    /// job-seeking timer. A household with adequate resources rationally chooses
    /// NoAction.
    ///
    /// Follows the proven JobDecisionInput/PersonJobDecisions pattern from Pass II:
    /// pressure in, decision out. Pure and deterministic — the caller (autonomy
    /// driver / integrator) maps real state onto HouseholdPressureInput; this
    /// class never reads registries directly and never invents facts.
    ///
    /// Every decision carries a real reason naming the actual numbers. The
    /// decision itself is an evaluation; HouseholdDecisionDriver turns it into
    /// real operations (opportunity discovery, housing search, obligations).
    /// </summary>
    public sealed class HouseholdDecisionService : IHouseholdDecisionService
    {
        /// <summary>
        /// Pressure horizon in days: obligations due "in the next period" are
        /// compared against reserves plus this many days of expected income.
        /// Documented so integrators fill ObligationsDueCents consistently.
        /// </summary>
        public const int CoverageDays = 30;

        /// <summary>
        /// Reserves + expected income covering obligations with at least this
        /// ratio counts as adequate resources: the household may rationally do
        /// nothing (NoAction). Not a timer, not a quota — a cushion test.
        /// </summary>
        private const double ComfortableCushionRatio = 1.25;

        /// <summary>Bounded decision history per household (diagnostics + read models).</summary>
        private const int MaxHistoryPerHousehold = 32;

        private readonly List<HouseholdDecision> history = new List<HouseholdDecision>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HouseholdDecision> History => history;

        /// <summary>Most recent decision for a household, or null when none.</summary>
        public HouseholdDecision LastDecisionFor(string householdId)
        {
            if (string.IsNullOrEmpty(householdId)) return null;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (string.Equals(history[i].HouseholdId, householdId, StringComparison.Ordinal))
                    return history[i];
            }
            return null;
        }

        public HouseholdDecision Evaluate(HouseholdPressureInput input)
        {
            if (input == null)
            {
                return Record(new HouseholdDecision
                {
                    HouseholdId = string.Empty,
                    Day = -1,
                    Kind = HouseholdDecisionKind.NoAction,
                    Reason = "NoAction: no pressure information available",
                });
            }

            string hid = input.HouseholdId ?? string.Empty;
            long obligations = Math.Max(0, input.ObligationsDueCents);
            long reserves = input.ReservesCents; // may be negative: overdrawn is real pressure
            long expectedPerDay = Math.Max(0, input.ExpectedIncomeCentsPerDay);
            long coverage = reserves + (long)CoverageDays * expectedPerDay;

            // No obligations due: nothing to decide.
            if (obligations <= 0)
            {
                return Record(new HouseholdDecision
                {
                    HouseholdId = hid,
                    Day = input.Day,
                    Kind = HouseholdDecisionKind.NoAction,
                    Reason = string.Format(
                        "NoAction: no obligations due (reserves {0}c)", reserves),
                });
            }

            // Adequate resources: rational inaction. This is the anti-timer rule —
            // the household is NOT forced to seek work merely because time passed.
            if (coverage >= (long)(obligations * ComfortableCushionRatio))
            {
                string reason = string.Format(
                    "NoAction: reserves {0}c + expected income {1}c/day x {2}d = {3}c covers obligations {4}c with cushion",
                    reserves, expectedPerDay, CoverageDays, coverage, obligations);
                if (input.RecentJobLoss && !string.IsNullOrEmpty(input.JobLossNote))
                    reason += "; recent job loss (" + input.JobLossNote + ") but resources adequate — relying on household reserves";
                return Record(new HouseholdDecision
                {
                    HouseholdId = hid,
                    Day = input.Day,
                    Kind = HouseholdDecisionKind.NoAction,
                    Reason = reason,
                });
            }

            // Job loss with known opportunities: pursue income first. Without
            // known opportunities, fall through to the general ladder below.
            if (input.RecentJobLoss && input.KnownOpportunityCount > 0)
            {
                return Record(new HouseholdDecision
                {
                    HouseholdId = hid,
                    Day = input.Day,
                    Kind = HouseholdDecisionKind.SeekIncome,
                    Reason = string.Format(
                        "SeekIncome: job loss ({0}); {1} legitimately-known opportunitie(s); reserves {2}c insufficient cushion",
                        string.IsNullOrEmpty(input.JobLossNote) ? "employment ended" : input.JobLossNote,
                        input.KnownOpportunityCount, reserves),
                });
            }

            // Shelter risk dominates: losing accommodation is the worst outcome.
            // Structural deficit (even full expected income cannot cover
            // obligations) -> cheaper accommodation is the honest answer.
            // Temporary shortfall -> negotiate terms first, relocation is drastic.
            if (input.HasShelterRisk)
            {
                long incomeOverHorizon = (long)CoverageDays * expectedPerDay;
                if (incomeOverHorizon < obligations)
                {
                    return Record(new HouseholdDecision
                    {
                        HouseholdId = hid,
                        Day = input.Day,
                        Kind = HouseholdDecisionKind.RelocateCheaper,
                        Reason = string.Format(
                            "RelocateCheaper: shelter at risk and obligations {0}c structurally exceed {1}d expected income {2}c; cheaper accommodation needed",
                            obligations, CoverageDays, incomeOverHorizon),
                    });
                }
                return Record(new HouseholdDecision
                {
                    HouseholdId = hid,
                    Day = input.Day,
                    Kind = HouseholdDecisionKind.NegotiateTerms,
                    Reason = string.Format(
                        "NegotiateTerms: shelter at risk but shortfall is temporary (reserves {0}c, expected income {1}c/day); negotiate with landlord before relocating",
                        reserves, expectedPerDay),
                });
            }

            long shortfall = obligations - coverage;
            if (shortfall > 0)
            {
                // Real shortfall. Prefer known income opportunities over debt;
                // prefer searching legitimate contacts over austerity; prefer
                // legitimate credit over cutting spending. No highest-wage rule:
                // what matters is that the channel is legitimate, not its pay.
                if (input.KnownOpportunityCount > 0)
                {
                    return Record(new HouseholdDecision
                    {
                        HouseholdId = hid,
                        Day = input.Day,
                        Kind = HouseholdDecisionKind.SeekIncome,
                        Reason = string.Format(
                            "SeekIncome: shortfall {0}c (obligations {1}c vs coverage {2}c); {3} legitimately-known opportunitie(s) to pursue before borrowing",
                            shortfall, obligations, coverage, input.KnownOpportunityCount),
                    });
                }
                if (input.HasSearchableContacts)
                {
                    return Record(new HouseholdDecision
                    {
                        HouseholdId = hid,
                        Day = input.Day,
                        Kind = HouseholdDecisionKind.SeekIncome,
                        Reason = string.Format(
                            "SeekIncome: shortfall {0}c; no openings known yet but members have documented contacts to search",
                            shortfall),
                    });
                }
                if (input.AvailableCreditCents >= shortfall)
                {
                    return Record(new HouseholdDecision
                    {
                        HouseholdId = hid,
                        Day = input.Day,
                        Kind = HouseholdDecisionKind.Borrow,
                        Reason = string.Format(
                            "Borrow: shortfall {0}c with no known opportunities or searchable contacts; legitimate credit {1}c covers it",
                            shortfall, input.AvailableCreditCents),
                    });
                }
                return Record(new HouseholdDecision
                {
                    HouseholdId = hid,
                    Day = input.Day,
                    Kind = HouseholdDecisionKind.Economize,
                    Reason = string.Format(
                        "Economize: shortfall {0}c, no known opportunities, no searchable contacts, credit {1}c insufficient; cut spending to stretch reserves {2}c",
                        shortfall, input.AvailableCreditCents, reserves),
                });
            }

            // Thin cushion: obligations covered but no margin. Trim spending to
            // rebuild the cushion rather than seeking work or borrowing.
            return Record(new HouseholdDecision
            {
                HouseholdId = hid,
                Day = input.Day,
                Kind = HouseholdDecisionKind.Economize,
                Reason = string.Format(
                    "Economize: coverage {0}c meets obligations {1}c but cushion is thin; trim discretionary spending",
                    coverage, obligations),
            });
        }

        private HouseholdDecision Record(HouseholdDecision decision)
        {
            // Bounded history: drop the oldest decision for this household first.
            int countForHousehold = 0;
            for (int i = 0; i < history.Count; i++)
                if (string.Equals(history[i].HouseholdId, decision.HouseholdId, StringComparison.Ordinal))
                    countForHousehold++;
            if (countForHousehold >= MaxHistoryPerHousehold)
            {
                for (int i = 0; i < history.Count; i++)
                {
                    if (string.Equals(history[i].HouseholdId, decision.HouseholdId, StringComparison.Ordinal))
                    {
                        history.RemoveAt(i);
                        break;
                    }
                }
            }
            history.Add(decision);
            return decision;
        }

        /// <summary>Captures decision history for the save pipeline.</summary>
        public HouseholdDecisionServiceSaveDto CaptureSaveDto()
        {
            return new HouseholdDecisionServiceSaveDto
            {
                decisions = new List<HouseholdDecision>(history),
            };
        }

        /// <summary>Clear-then-load. Decisions are evaluations, never actions —
        /// reload restores them without re-executing anything.</summary>
        public void LoadFromSaveDto(HouseholdDecisionServiceSaveDto dto)
        {
            history.Clear();
            if (dto == null || dto.decisions == null) return;
            foreach (HouseholdDecision d in dto.decisions)
            {
                if (d == null) continue;
                history.Add(d);
            }
        }
    }

    /// <summary>Save data for the household decision service.</summary>
    [Serializable]
    public sealed class HouseholdDecisionServiceSaveDto
    {
        public List<HouseholdDecision> decisions = new List<HouseholdDecision>();
    }
}
