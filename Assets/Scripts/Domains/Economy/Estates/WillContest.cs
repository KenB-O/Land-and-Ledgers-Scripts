using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Property;
using LandLedgers.Economy.Businesses.Lawyer;
using UnityEngine;

namespace LandLedgers.Economy.Estates
{
    /// <summary>
    /// D4C: grounds for contesting a will, as DATA. Each entry is a recorded
    /// claim, never proof. Historical (1870s American practice): undue
    /// influence (the coercion doctrine imported from England and settled in
    /// American treatises by the 1849 Jarman edition), lack of testamentary
    /// capacity (the Banks v Goodfellow 1870 test: the testator must have
    /// understood they were making a will, the nature of their property, and
    /// who might expect to benefit), improper execution (the dated statutes
    /// required a signed writing attested by two or three credible
    /// witnesses), fraud or forgery, and the discovery of a later will (a
    /// later valid will revokes the earlier by inconsistency or expressly).
    /// </summary>
    public enum WillContestGround
    {
        Unspecified = 0,
        UndueInfluence = 1,      // coercion that overbore the testator's free will
        TestamentaryIncapacity = 2, // testator did not understand the act, property, or beneficiaries
        ImproperExecution = 3,   // missing signature, too few or disqualified witnesses
        FraudOrForgery = 4,       // trickery, or the instrument is not the testator's at all
        LaterWillDiscovered = 5, // a later will exists and must be named by id
    }

    /// <summary>
    /// D4C: who may contest. Standing is earned from the world, never
    /// assumed: heirs at law and next of kin come from the HF-3
    /// KinshipRegistry (real kin only), named beneficiaries from the will's
    /// own bequests, and creditors from debts recorded against the estate.
    /// </summary>
    public enum WillContestStanding
    {
        Unspecified = 0,
        HeirAtLaw = 1,        // spouse, child, parent, or sibling of the decedent per kinship
        NamedBeneficiary = 2, // takes under the contested will
        EstateCreditor = 3,   // holds a recorded claim against the estate the will would impair
    }

    /// <summary>
    /// D4C: contest lifecycle. Canon Part IX §9.1 lifecycle (claim/demand ->
    /// response -> negotiation/settlement -> formal proceeding where needed
    /// -> decision/judgment) and Canon §2.3 (proceedings consume ordinary
    /// world time): filing is a claim, the hearing is the formal proceeding,
    /// and the terminal states are how the claim ends. Nothing here decides
    /// the will by fiat — the outcome is recorded from the proceeding.
    /// </summary>
    public enum WillContestStatus
    {
        Unspecified = 0,
        Open = 1,         // filed; distribution paused; awaiting the proceeding
        HearingSet = 2,   // a hearing day is set; the proceeding is underway
        Withdrawn = 3,    // the contestant walked away (terminal)
        Settled = 4,      // the parties agreed terms (terminal)
        Adjudicated = 5,  // the proceeding ruled (terminal)
        Dismissed = 6,    // thrown out without reaching the merits (terminal)
    }

    /// <summary>
    /// D4C: what the resolution did to the will. Withdrawal, dismissal, and
    /// an upholding ruling all leave the will standing as probated.
    /// </summary>
    public enum WillContestOutcome
    {
        Unspecified = 0,
        WillUpheld = 1,            // the will stands as probated
        WillInvalidatedWhole = 2,  // the will conveys nothing; fallback to prior will or intestate
        WillInvalidatedInPart = 3, // named bequests voided; the rest of the will stands
    }

    /// <summary>D4C: which side counsel is recorded as appearing for.</summary>
    public enum WillContestCounselSide
    {
        Unspecified = 0,
        Contestant = 1, // appears for the contestant
        Estate = 2,     // appears for the estate / the will's defender (the executor)
    }

    /// <summary>
    /// D4C: how assessed costs are allocated. CALIBRATION — the canon holds
    /// "court procedure" and "estate/probate detail" as researched data (Tech
    /// X §10.1), so the rule is parameterized, not invented: the caller
    /// chooses, the record shows what was chosen.
    /// </summary>
    public enum ContestCostAllocation
    {
        Unspecified = 0,
        EachBearsOwn = 1,
        LoserPays = 2,
    }

    /// <summary>D4C: one recorded ground claim. The statement is the contestant's claim, not a finding.</summary>
    [Serializable]
    public sealed class WillContestGroundClaim
    {
        public WillContestGround Ground = WillContestGround.Unspecified;
        public string ClaimStatement = string.Empty; // the contestant's claim, recorded verbatim
        public string LaterWillId = string.Empty;   // required when Ground == LaterWillDiscovered

        public WillContestGroundClaim() { }
    }

    /// <summary>D4C: one term of a settlement agreement between the parties, recorded as data.</summary>
    public enum ContestSettlementTermKind
    {
        Unspecified = 0,
        Voided = 1,    // the named bequest is voided by agreement
        Confirmed = 2, // the named bequest stands by agreement
    }

    [Serializable]
    public sealed class ContestSettlementTerm
    {
        public int BequestIndex = -1; // index into Will.Bequests
        public ContestSettlementTermKind TermKind = ContestSettlementTermKind.Unspecified;
        public string Note = string.Empty;

        public ContestSettlementTerm() { }
    }

    /// <summary>
    /// D4C: the cost schedule for a contest. CALIBRATION — exact period fees
    /// are a research hold (Tech X §10.1), so there is no invented default:
    /// a zero schedule means no fee schedule is on file. Assessed costs are
    /// RECORDED as amounts owed; this service never moves money.
    /// </summary>
    [Serializable]
    public sealed class WillContestCostSchedule
    {
        public int FilingFeeCents;
        public int HearingCostCents;

        public WillContestCostSchedule() { }

        public int TotalCents => Math.Max(0, FilingFeeCents) + Math.Max(0, HearingCostCents);
    }

    /// <summary>
    /// D4C: one will contest — a real record of a claim against a probated
    /// will. Filing pauses probate distribution through the NX-3C public
    /// API; the contest itself is resolved only through the proceeding
    /// (hearing/adjudication), withdrawal, settlement, or dismissal. A
    /// contest never auto-invalidates anything.
    /// </summary>
    [Serializable]
    public sealed class WillContest
    {
        public string ContestId = string.Empty;
        public string WillId = string.Empty;
        public string EstateId = string.Empty;
        public int TestatorPersonId = -1;
        public int ExecutorPersonId = -1;

        public int ContestantPersonId = -1;
        public string ContestantName = string.Empty;
        public WillContestStanding Standing = WillContestStanding.Unspecified;
        public string StandingDetail = string.Empty; // e.g. "child of decedent per kinship" or the debt id
        public List<WillContestGroundClaim> GroundClaims = new List<WillContestGroundClaim>();

        public int FiledDayIndex;
        public int HearingDayIndex = -1;
        public WillContestStatus Status = WillContestStatus.Unspecified;
        public WillContestOutcome Outcome = WillContestOutcome.Unspecified;
        public int ResolvedDayIndex = -1;

        // Resolution detail.
        public List<int> VoidedBequestIndices = new List<int>(); // settlement or in-part ruling
        public int SettlementCashCents;                          // recorded agreement between real parties; not executed here
        public int SettlementPayerPersonId = -1;
        public int SettlementPayeePersonId = -1;
        public string SettlementNote = string.Empty;
        public bool DismissedAsFrivolous;

        // Counsel: read-only links to W9 lawyer matters (recorded, never managed here).
        public string ContestantCounselMatterId = string.Empty;
        public string EstateCounselMatterId = string.Empty;

        // Costs: recorded as owed, never moved.
        public int AssessedCostsCents;
        public int AssessedAgainstPersonId = -1;
        public bool AssessedAgainstEstate;
        public ContestCostAllocation CostAllocation = ContestCostAllocation.Unspecified;

        public WillContest() { }
    }

    /// <summary>
    /// D4C: will contest mechanics. Integrates with the NX-3C
    /// <see cref="ProbateService"/> through its public API only (file ->
    /// ContestWill to pause; resolve -> LiftContestPause to resume) and
    /// reads the W9 <see cref="LawyerPracticeRuntime"/> through GetMatter
    /// only — lawyer logic is never changed here. Grounds are recorded
    /// claims; outcomes are recorded from the proceeding; costs are recorded
    /// as owed, never moved.
    /// </summary>
    public sealed class WillContestService
    {
        private readonly Dictionary<string, WillContest> contests =
            new Dictionary<string, WillContest>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Files a contest against a probated will. Validates standing from
        /// the world (kinship, the will's bequests, recorded estate debts),
        /// requires stated grounds (recorded as claims, not proof), then
        /// pauses probate distribution via the NX-3C public API. A claim is
        /// never self-proving: filing changes no one's rights.
        /// </summary>
        public WillContest FileContest(
            EntityIdRegistry ids, ProbateService probate, Will will, Estate estate,
            int contestantPersonId, string contestantName,
            WillContestStanding standing, string standingDebtInstrumentId,
            List<WillContestGroundClaim> groundClaims,
            KinshipRegistry kinship, PopulationState population, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (probate == null) { diag.Add("WillContestService.FileContest: the probate service is required."); return null; }
            if (will == null) { diag.Add("WillContestService.FileContest: a will is required."); return null; }
            if (estate == null) { diag.Add("WillContestService.FileContest: the estate must be opened first."); return null; }
            if (!will.Probated)
            {
                diag.Add($"WillContestService.FileContest: will {will.WillId} is not probated — contests challenge admitted wills.");
                return null;
            }
            if (will.Revoked)
            {
                diag.Add($"WillContestService.FileContest: will {will.WillId} was revoked — it conveys nothing; nothing to contest.");
                return null;
            }
            if (will.InvalidatedByContest)
            {
                diag.Add($"WillContestService.FileContest: will {will.WillId} was already invalidated by a contest.");
                return null;
            }
            if (will.TestatorPersonId != estate.DecedentPersonId)
            {
                diag.Add($"WillContestService.FileContest: will {will.WillId} is not the decedent's will.");
                return null;
            }
            if (HasOpenContest(will.WillId))
            {
                diag.Add($"WillContestService.FileContest: will {will.WillId} already has an open contest — one proceeding at a time.");
                return null;
            }
            if (contestantPersonId < 0)
            {
                diag.Add("WillContestService.FileContest: the contestant must be a real person.");
                return null;
            }
            if (population != null && population.GetPerson(contestantPersonId) == null)
            {
                diag.Add($"WillContestService.FileContest: contestant P{contestantPersonId} is not a known person.");
                return null;
            }
            if (contestantPersonId == will.TestatorPersonId)
            {
                diag.Add("WillContestService.FileContest: the testator cannot contest their own will.");
                return null;
            }

            string standingDetail = ValidateStanding(will, estate, contestantPersonId, standing,
                standingDebtInstrumentId, kinship, diag);
            if (standingDetail == null) return null; // diag carries the reason

            if (groundClaims == null || groundClaims.Count == 0)
            {
                diag.Add("WillContestService.FileContest: at least one ground must be stated — a claim without grounds is not filed.");
                return null;
            }
            foreach (WillContestGroundClaim claim in groundClaims)
            {
                if (claim == null || claim.Ground == WillContestGround.Unspecified)
                {
                    diag.Add("WillContestService.FileContest: every ground claim must name its ground.");
                    return null;
                }
                if (string.IsNullOrWhiteSpace(claim.ClaimStatement))
                {
                    diag.Add($"WillContestService.FileContest: the {claim.Ground} claim must state what is alleged — grounds are recorded claims.");
                    return null;
                }
                if (claim.Ground == WillContestGround.LaterWillDiscovered)
                {
                    if (string.IsNullOrWhiteSpace(claim.LaterWillId) || probate.Get(claim.LaterWillId) == null)
                    {
                        diag.Add("WillContestService.FileContest: a LaterWillDiscovered claim must name a real recorded will — no phantom instruments.");
                        return null;
                    }
                    if (string.Equals(claim.LaterWillId, will.WillId, StringComparison.Ordinal))
                    {
                        diag.Add("WillContestService.FileContest: the 'later will' cannot be the contested will itself.");
                        return null;
                    }
                }
            }

            // Pause probate distribution through the NX-3C public API. If the
            // pause is refused, nothing is filed.
            string groundsSummary = string.Join("; ", GroundSummaries(groundClaims));
            string paused = probate.ContestWill(will, contestantPersonId, groundsSummary, population, diag);
            if (paused != null)
            {
                diag.Add($"WillContestService.FileContest: probate refused the pause: {paused}");
                return null;
            }

            var contest = new WillContest
            {
                ContestId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                WillId = will.WillId,
                EstateId = estate.EstateId,
                TestatorPersonId = will.TestatorPersonId,
                ExecutorPersonId = will.ExecutorPersonId,
                ContestantPersonId = contestantPersonId,
                ContestantName = contestantName ?? string.Empty,
                Standing = standing,
                StandingDetail = standingDetail,
                FiledDayIndex = dayIndex,
                Status = WillContestStatus.Open,
            };
            contest.GroundClaims.AddRange(groundClaims);
            contests[contest.ContestId] = contest;
            diag.Add($"WillContestService: contest {contest.ContestId} FILED against will {will.WillId} by P{contestantPersonId} " +
                $"({standing}: {standingDetail}) — grounds recorded as claims, distribution paused.");
            return contest;
        }

        /// <summary>
        /// Sets the hearing day. Canon §2.3: proceedings consume ordinary
        /// world time — the hearing cannot predate the filing, and nothing is
        /// decided until the proceeding runs.
        /// </summary>
        public string ScheduleHearing(WillContest contest, int hearingDayIndex, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (contest == null) return "WillContestService.ScheduleHearing: a contest is required.";
            if (contest.Status != WillContestStatus.Open)
                return $"WillContestService.ScheduleHearing: contest {contest.ContestId} is {contest.Status} — hearings schedule only for open contests.";
            if (hearingDayIndex < contest.FiledDayIndex)
                return "WillContestService.ScheduleHearing: the hearing cannot predate the filing (Canon §2.3).";
            contest.HearingDayIndex = hearingDayIndex;
            contest.Status = WillContestStatus.HearingSet;
            diag.Add($"WillContestService: contest {contest.ContestId} — hearing set for day {hearingDayIndex}.");
            return null;
        }

        /// <summary>
        /// Records counsel for a side, linked read-only to a W9 lawyer
        /// matter. The matter must exist, be open, and belong to the right
        /// client (the contestant, or the executor defending the will).
        /// Recording counsel appears for a side; it advances nothing.
        /// </summary>
        public string RecordCounsel(
            WillContest contest, LawyerPracticeRuntime practice,
            string matterId, WillContestCounselSide side, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (contest == null) return "WillContestService.RecordCounsel: a contest is required.";
            if (contest.Status != WillContestStatus.Open && contest.Status != WillContestStatus.HearingSet)
                return $"WillContestService.RecordCounsel: contest {contest.ContestId} is {contest.Status} — counsel records only while it is live.";
            if (practice == null) return "WillContestService.RecordCounsel: the lawyer practice is required.";
            if (side == WillContestCounselSide.Unspecified)
                return "WillContestService.RecordCounsel: the side represented must be stated.";
            LegalMatter matter = practice.GetMatter(matterId);
            if (matter == null)
                return $"WillContestService.RecordCounsel: matter '{matterId}' is not held by the practice — counsel is not invented.";
            if (matter.Status != MatterStatus.Open)
                return $"WillContestService.RecordCounsel: matter '{matterId}' is {matter.Status} — representation records only for open matters.";

            if (side == WillContestCounselSide.Contestant)
            {
                if (matter.ClientPersonId != contest.ContestantPersonId)
                    return $"WillContestService.RecordCounsel: matter '{matterId}' belongs to P{matter.ClientPersonId}, not the contestant P{contest.ContestantPersonId} — sides must match.";
                contest.ContestantCounselMatterId = matterId;
            }
            else
            {
                if (matter.ClientPersonId != contest.ExecutorPersonId)
                    return $"WillContestService.RecordCounsel: matter '{matterId}' belongs to P{matter.ClientPersonId}, not the executor P{contest.ExecutorPersonId} defending the will — sides must match.";
                contest.EstateCounselMatterId = matterId;
            }
            diag.Add($"WillContestService: contest {contest.ContestId} — counsel recorded for the {side} side (matter '{matterId}'). Appearance only.");
            return null;
        }

        /// <summary>
        /// The contestant withdraws the claim. The will stands as probated;
        /// the pause lifts; nothing about the grounds is decided.
        /// </summary>
        public string WithdrawContest(WillContest contest, ProbateService probate, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (contest == null) return "WillContestService.WithdrawContest: a contest is required.";
            if (probate == null) return "WillContestService.WithdrawContest: the probate service is required.";
            if (!IsLive(contest.Status))
                return $"WillContestService.WithdrawContest: contest {contest.ContestId} is {contest.Status} — already ended.";
            contest.Status = WillContestStatus.Withdrawn;
            contest.Outcome = WillContestOutcome.WillUpheld; // the challenge ends; the probated will stands
            contest.ResolvedDayIndex = dayIndex;
            LiftPause(probate, probatedWill(probate, contest), "contest withdrawn", diag);
            diag.Add($"WillContestService: contest {contest.ContestId} WITHDRAWN day {dayIndex} — the will stands as probated; the grounds were never decided.");
            return null;
        }

        /// <summary>
        /// Records a settlement between the parties. Voided bequests take
        /// effect on the will; cash terms are recorded as the parties'
        /// agreement — this service never moves money, and the caller
        /// executes transfers through the economy flows. The pause lifts.
        /// </summary>
        public string RecordSettlement(
            WillContest contest, ProbateService probate, Will will,
            List<ContestSettlementTerm> terms,
            int settlementCashCents, int payerPersonId, int payeePersonId,
            string settlementNote, PopulationState population, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (contest == null) return "WillContestService.RecordSettlement: a contest is required.";
            if (probate == null) return "WillContestService.RecordSettlement: the probate service is required.";
            if (will == null || !string.Equals(will.WillId, contest.WillId, StringComparison.Ordinal))
                return "WillContestService.RecordSettlement: the contested will is required.";
            if (!IsLive(contest.Status))
                return $"WillContestService.RecordSettlement: contest {contest.ContestId} is {contest.Status} — already ended.";
            terms = terms ?? new List<ContestSettlementTerm>();
            foreach (ContestSettlementTerm term in terms)
            {
                if (term == null || term.TermKind == ContestSettlementTermKind.Unspecified)
                    return "WillContestService.RecordSettlement: every settlement term must state its kind.";
                if (term.BequestIndex < 0 || term.BequestIndex >= will.Bequests.Count)
                    return $"WillContestService.RecordSettlement: term targets bequest {term.BequestIndex} — the will has {will.Bequests.Count} bequests.";
            }
            if (settlementCashCents < 0)
                return "WillContestService.RecordSettlement: a settlement payment cannot be negative.";
            if (settlementCashCents > 0)
            {
                if (payerPersonId < 0 || payeePersonId < 0 || payerPersonId == payeePersonId)
                    return "WillContestService.RecordSettlement: a cash term needs two distinct real parties.";
                if (population != null &&
                    (population.GetPerson(payerPersonId) == null || population.GetPerson(payeePersonId) == null))
                    return "WillContestService.RecordSettlement: the cash term names people who are not known.";
            }

            bool anyVoided = false;
            foreach (ContestSettlementTerm term in terms)
            {
                if (term.TermKind == ContestSettlementTermKind.Voided)
                {
                    will.Bequests[term.BequestIndex].InvalidatedByContest = true;
                    contest.VoidedBequestIndices.Add(term.BequestIndex);
                    anyVoided = true;
                }
            }
            contest.SettlementCashCents = settlementCashCents;
            contest.SettlementPayerPersonId = payerPersonId;
            contest.SettlementPayeePersonId = payeePersonId;
            contest.SettlementNote = settlementNote ?? string.Empty;
            contest.Status = WillContestStatus.Settled;
            contest.Outcome = anyVoided ? WillContestOutcome.WillInvalidatedInPart : WillContestOutcome.WillUpheld;
            contest.ResolvedDayIndex = dayIndex;
            LiftPause(probate, will, "contest settled", diag);
            diag.Add($"WillContestService: contest {contest.ContestId} SETTLED day {dayIndex} — " +
                $"{contest.VoidedBequestIndices.Count} bequest(s) voided by agreement" +
                (settlementCashCents > 0 ? $", {settlementCashCents}c recorded between P{payerPersonId} and P{payeePersonId} (caller executes)" : "") +
                ". The pause lifts.");
            return null;
        }

        /// <summary>
        /// Adjudicates the contest after the hearing. The ruling is recorded
        /// from the proceeding — never assumed from the filing. Upheld:
        /// the will stands and the pause lifts. Invalidated in whole: the
        /// will conveys nothing; the estate falls back to the most recent
        /// prior valid will for the testator, else to intestate under the
        /// NX-3C rules. Invalidated in part: the named bequests are voided
        /// and the rest stands.
        /// </summary>
        public string AdjudicateContest(
            WillContest contest, ProbateService probate, Will will, Estate estate,
            WillContestOutcome outcome, List<int> voidedBequestIndices,
            TitleAuthority titles, WillContestCostSchedule costSchedule,
            ContestCostAllocation costAllocation, int dayIndex,
            PopulationState population, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (contest == null) return "WillContestService.AdjudicateContest: a contest is required.";
            if (probate == null) return "WillContestService.AdjudicateContest: the probate service is required.";
            if (will == null || !string.Equals(will.WillId, contest.WillId, StringComparison.Ordinal))
                return "WillContestService.AdjudicateContest: the contested will is required.";
            if (estate == null || !string.Equals(estate.EstateId, contest.EstateId, StringComparison.Ordinal))
                return "WillContestService.AdjudicateContest: the contested estate is required.";
            if (contest.Status != WillContestStatus.HearingSet)
                return $"WillContestService.AdjudicateContest: contest {contest.ContestId} is {contest.Status} — adjudication follows a set hearing (Canon §2.3).";
            if (contest.HearingDayIndex > dayIndex)
                return $"WillContestService.AdjudicateContest: the hearing is set for day {contest.HearingDayIndex} — it has not happened yet.";
            if (outcome == WillContestOutcome.Unspecified)
                return "WillContestService.AdjudicateContest: the ruling must state the outcome.";

            if (outcome == WillContestOutcome.WillInvalidatedInPart)
            {
                if (voidedBequestIndices == null || voidedBequestIndices.Count == 0)
                    return "WillContestService.AdjudicateContest: in-part invalidation must name the voided bequests.";
                foreach (int index in voidedBequestIndices)
                {
                    if (index < 0 || index >= will.Bequests.Count)
                        return $"WillContestService.AdjudicateContest: bequest {index} does not exist — the will has {will.Bequests.Count}.";
                }
            }

            switch (outcome)
            {
                case WillContestOutcome.WillUpheld:
                    LiftPause(probate, will, "contest adjudicated — will upheld", diag);
                    break;

                case WillContestOutcome.WillInvalidatedWhole:
                    will.InvalidatedByContest = true;
                    LiftPause(probate, will, "contest adjudicated — will invalidated in whole", diag);
                    FallBackAfterInvalidation(contest, probate, estate, titles, dayIndex, diag);
                    break;

                case WillContestOutcome.WillInvalidatedInPart:
                    foreach (int index in voidedBequestIndices)
                    {
                        will.Bequests[index].InvalidatedByContest = true;
                        contest.VoidedBequestIndices.Add(index);
                    }
                    LiftPause(probate, will, "contest adjudicated — will invalidated in part", diag);
                    break;
            }

            AssessCosts(contest, costSchedule ?? new WillContestCostSchedule(),
                costAllocation, outcome == WillContestOutcome.WillUpheld, diag);

            contest.Status = WillContestStatus.Adjudicated;
            contest.Outcome = outcome;
            contest.ResolvedDayIndex = dayIndex;
            diag.Add($"WillContestService: contest {contest.ContestId} ADJUDICATED day {dayIndex} — {outcome}.");
            return null;
        }

        /// <summary>
        /// Dismisses the contest without reaching the merits. The will
        /// stands. A frivolous dismissal assesses the scheduled costs
        /// against the contestant — filing fees are the price of a claim
        /// made without substance.
        /// </summary>
        public string DismissContest(
            WillContest contest, ProbateService probate,
            bool frivolous, WillContestCostSchedule costSchedule,
            int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (contest == null) return "WillContestService.DismissContest: a contest is required.";
            if (probate == null) return "WillContestService.DismissContest: the probate service is required.";
            if (!IsLive(contest.Status))
                return $"WillContestService.DismissContest: contest {contest.ContestId} is {contest.Status} — already ended.";

            contest.Status = WillContestStatus.Dismissed;
            contest.Outcome = WillContestOutcome.WillUpheld; // the challenge ends; the probated will stands
            contest.ResolvedDayIndex = dayIndex;
            contest.DismissedAsFrivolous = frivolous;
            if (frivolous)
            {
                contest.AssessedCostsCents = (costSchedule ?? new WillContestCostSchedule()).TotalCents;
                contest.AssessedAgainstPersonId = contest.ContestantPersonId;
                contest.AssessedAgainstEstate = false;
                contest.CostAllocation = ContestCostAllocation.LoserPays;
                diag.Add($"WillContestService: contest {contest.ContestId} dismissed as FRIVOLOUS — {contest.AssessedCostsCents}c assessed against the contestant (recorded, not moved).");
            }
            LiftPause(probate, probatedWill(probate, contest), "contest dismissed", diag);
            diag.Add($"WillContestService: contest {contest.ContestId} DISMISSED day {dayIndex} — the will stands as probated.");
            return null;
        }

        public WillContest Get(string contestId)
        {
            return contests.TryGetValue(contestId, out WillContest contest) ? contest : null;
        }

        /// <summary>
        /// D4D: true when the estate has a live (open or hearing-set) contest.
        /// The D4D administration pipeline freezes the estate on this — no
        /// distributions, disbursements, or closure while a contest is live.
        /// Read-only.
        /// </summary>
        public bool HasLiveContestForEstate(string estateId)
        {
            if (string.IsNullOrWhiteSpace(estateId)) return false;
            foreach (WillContest contest in contests.Values)
            {
                if (contest != null && string.Equals(contest.EstateId, estateId, StringComparison.Ordinal)
                    && IsLive(contest.Status))
                    return true;
            }
            return false;
        }

        #region Save / Load

        [Serializable]
        public sealed class WillContestSaveDto
        {
            public List<WillContest> Contests = new List<WillContest>();
        }

        public WillContestSaveDto CaptureSaveDto()
        {
            var dto = new WillContestSaveDto();
            foreach (WillContest contest in contests.Values) dto.Contests.Add(contest);
            return dto;
        }

        public void LoadFromSaveDto(WillContestSaveDto dto)
        {
            contests.Clear();
            if (dto == null) return;
            foreach (WillContest contest in dto.Contests)
            {
                if (contest == null) continue;
                contests[contest.ContestId] = contest;
            }
        }

        #endregion

        #region Internals

        private bool HasOpenContest(string willId)
        {
            foreach (WillContest contest in contests.Values)
            {
                if (string.Equals(contest.WillId, willId, StringComparison.Ordinal) && IsLive(contest.Status))
                    return true;
            }
            return false;
        }

        private static bool IsLive(WillContestStatus status)
        {
            return status == WillContestStatus.Open || status == WillContestStatus.HearingSet;
        }

        private static Will probatedWill(ProbateService probate, WillContest contest)
        {
            return probate != null ? probate.Get(contest.WillId) : null;
        }

        private static void LiftPause(ProbateService probate, Will will, string reason, List<string> diag)
        {
            string lifted = probate.LiftContestPause(will, reason, diag);
            if (lifted != null) diag.Add($"WillContestService: note — {lifted}");
        }

        /// <summary>
        /// Validates standing against the world. Returns the standing detail
        /// string, or null (with diag) when the contestant has no standing.
        /// </summary>
        private static string ValidateStanding(
            Will will, Estate estate, int contestantPersonId,
            WillContestStanding standing, string standingDebtInstrumentId,
            KinshipRegistry kinship, List<string> diag)
        {
            switch (standing)
            {
                case WillContestStanding.HeirAtLaw:
                    if (kinship != null)
                    {
                        int decedent = estate.DecedentPersonId;
                        bool isKin =
                            kinship.GetRelated(decedent, KinshipRelation.Spouse).Contains(contestantPersonId) ||
                            kinship.GetRelated(decedent, KinshipRelation.Parent).Contains(contestantPersonId) || // decedent is parent of contestant
                            kinship.GetRelated(decedent, KinshipRelation.Child).Contains(contestantPersonId) ||  // decedent is child of contestant
                            kinship.GetRelated(decedent, KinshipRelation.Sibling).Contains(contestantPersonId);
                        if (isKin)
                            return $"heir at law of P{decedent} per the kinship registry";
                    }
                    diag.Add($"WillContestService.FileContest: P{contestantPersonId} is not an heir at law of the decedent — no standing.");
                    return null;

                case WillContestStanding.NamedBeneficiary:
                    foreach (Bequest bequest in will.Bequests)
                    {
                        if (bequest.BeneficiaryPersonId == contestantPersonId)
                            return $"named beneficiary under will {will.WillId}";
                    }
                    diag.Add($"WillContestService.FileContest: P{contestantPersonId} takes nothing under will {will.WillId} — no standing as beneficiary.");
                    return null;

                case WillContestStanding.EstateCreditor:
                    if (!string.IsNullOrWhiteSpace(standingDebtInstrumentId) &&
                        (estate.EstateDebtIds.Contains(standingDebtInstrumentId) ||
                         estate.SettledDebtIds.Contains(standingDebtInstrumentId)))
                        return $"creditor holding recorded estate debt '{standingDebtInstrumentId}'";
                    diag.Add($"WillContestService.FileContest: '{standingDebtInstrumentId ?? "(unnamed)"}' is not a recorded claim against estate {estate.EstateId} — no standing as creditor.");
                    return null;

                default:
                    diag.Add("WillContestService.FileContest: standing must be stated — heir at law, named beneficiary, or estate creditor.");
                    return null;
            }
        }

        private static List<string> GroundSummaries(List<WillContestGroundClaim> claims)
        {
            var summaries = new List<string>();
            foreach (WillContestGroundClaim claim in claims) summaries.Add(claim.Ground.ToString());
            return summaries;
        }

        /// <summary>
        /// After whole invalidation: the most recent prior valid will for the
        /// testator is probated for the estate; with none, the estate falls
        /// back to intestate (the NX-3C rule: the heir shares computed at
        /// opening stand, and the caller distributes through
        /// EstateService.DistributeParcel).
        /// </summary>
        private static void FallBackAfterInvalidation(
            WillContest contest, ProbateService probate, Estate estate,
            TitleAuthority titles, int dayIndex, List<string> diag)
        {
            List<Will> priors = probate.FindTestatorWills(contest.TestatorPersonId);
            Will prior = null;
            foreach (Will candidate in priors)
            {
                if (string.Equals(candidate.WillId, contest.WillId, StringComparison.Ordinal)) continue;
                if (candidate.Revoked || candidate.InvalidatedByContest || candidate.Contested) continue;
                if (prior == null || candidate.SignedDayIndex > prior.SignedDayIndex) prior = candidate;
            }

            if (prior != null)
            {
                string refused = probate.ProbateWill(prior, estate, titles, dayIndex, diag);
                if (refused == null)
                {
                    diag.Add($"WillContestService: will {contest.WillId} invalidated in whole — estate {estate.EstateId} " +
                        $"falls back to prior valid will {prior.WillId} (signed day {prior.SignedDayIndex}).");
                    return;
                }
                diag.Add($"WillContestService: prior will {prior.WillId} could not probate ({refused}) — falling back to intestate.");
            }
            estate.TestateWillId = string.Empty;
            diag.Add($"WillContestService: will {contest.WillId} invalidated in whole — estate {estate.EstateId} falls back to " +
                "intestate succession (heir shares from opening stand; distribute through EstateService.DistributeParcel).");
        }

        /// <summary>
        /// Records assessed costs. Costs are amounts owed, never moved.
        /// On a LoserPays allocation the loser is the contestant when the
        /// will is upheld, and the estate when the will falls.
        /// </summary>
        private static void AssessCosts(
            WillContest contest, WillContestCostSchedule schedule,
            ContestCostAllocation allocation, bool willUpheld, List<string> diag)
        {
            contest.CostAllocation = allocation == ContestCostAllocation.Unspecified
                ? ContestCostAllocation.EachBearsOwn : allocation;
            if (contest.CostAllocation != ContestCostAllocation.LoserPays || schedule.TotalCents <= 0)
            {
                diag.Add($"WillContestService: contest {contest.ContestId} — costs: each side bears its own (allocation {contest.CostAllocation}).");
                return;
            }
            contest.AssessedCostsCents = schedule.TotalCents;
            if (willUpheld)
            {
                contest.AssessedAgainstPersonId = contest.ContestantPersonId;
                contest.AssessedAgainstEstate = false;
            }
            else
            {
                contest.AssessedAgainstPersonId = -1;
                contest.AssessedAgainstEstate = true;
            }
            diag.Add($"WillContestService: contest {contest.ContestId} — {contest.AssessedCostsCents}c assessed " +
                (contest.AssessedAgainstEstate ? "against the estate" : $"against P{contest.AssessedAgainstPersonId}") +
                " (recorded as owed; the caller moves money).");
        }

        #endregion
    }
}
