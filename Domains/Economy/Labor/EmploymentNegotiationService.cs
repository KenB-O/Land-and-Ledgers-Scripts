// Real employment agreements (Autonomous Labor, Phase C).
// Mission brief §6: hiring is negotiated — compensation, duties, schedule,
// start date, trial/probation, location, duration, housing/board, authority,
// conditions, termination. The recipient may accept, refuse, counter, clarify,
// delay, abandon. The employer must have legal/contractual authority to make the
// offer. Uses EmploymentOffer + OfferState from LaborContracts.cs (not redefined).
// Delivery channels reuse RecruitmentService/PostalService semantics: letters take
// real travel time; this service never sends mail itself — the caller does.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Skills;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Negotiated-offer lifecycle authority. Hub-independent: the offering-person
    /// authority check arrives as a predicate (businessInstanceId, offeringPersonId,
    /// actName) → true when the person may extend offers for the business. The
    /// hub wires it as: business owner, or DelegationService HireStaff scope
    /// permitted. This service never touches DelegationService directly.
    /// </summary>
    public sealed class EmploymentNegotiationService
    {
        /// <summary>Days an offer stays open before it expires unanswered (calibration).</summary>
        public const int OfferValidityDays = 14;

        /// <summary>Act name passed to the authority predicate for offers.</summary>
        public const string HireStaffAct = "HireStaff";

        // GUARD (brief §6, assert in code): employment must NEVER grant ownership
        // or residential rights. Any offer whose stated authority scope claims such
        // rights is refused at MakeOffer time, and housing/board is recorded as a
        // linked agreement note only — never a property transfer.
        private static readonly string[] ForbiddenAuthorityScopeTokens =
        {
            "ownership", "residential", "residence", "property transfer",
            "sell the business", "grant ownership", "grant residence", "transfer the deed",
        };

        private readonly Dictionary<string, EmploymentOffer> offers =
            new Dictionary<string, EmploymentOffer>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> acceptedEmploymentByOffer =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private int sequence;
        private readonly Func<string, int, string, bool> offerAuthorityPredicate;
        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Pass III Worker D, ADDITIVE: optional observer-side capability
        /// evidence (Canon XXXI §§4.4-4.5). Lets the offering side consult what
        /// it legitimately knows about a candidate before/during negotiation.
        /// Null by default: the offer lifecycle works without it.
        /// </summary>
        public ICapabilityEvidenceReader CapabilityEvidence { get; set; }

        /// <summary>
        /// Reads what the offering person legitimately knows about a
        /// candidate's capability for a task class. Returns null when no
        /// evidence reader is wired or no evidence exists.
        /// Additive: the offer lifecycle is untouched.
        /// </summary>
        public CapabilityEvidenceReading GetCandidateEvidence(
            int candidatePersonId, string taskCapabilityId, int offeringPersonId)
        {
            if (CapabilityEvidence == null || candidatePersonId < 0 ||
                offeringPersonId < 0 || string.IsNullOrEmpty(taskCapabilityId))
            {
                return null;
            }

            return CapabilityEvidence.ReadCapability(
                offeringPersonId.ToString(),
                candidatePersonId.ToString(),
                taskCapabilityId);
        }

        /// <param name="offerAuthorityPredicate">
        /// (businessInstanceId, offeringPersonId, actName) → true when the offering
        /// person may extend offers for the business (owner, or delegated HireStaff
        /// scope). Null means "no authority wiring" — offers are refused. A
        /// missing authority cannot silently authorize a binding offer.
        /// </param>
        public EmploymentNegotiationService(Func<string, int, string, bool> offerAuthorityPredicate = null)
        {
            this.offerAuthorityPredicate = offerAuthorityPredicate;
        }

        /// <summary>
        /// Extends an offer to a real candidate. Returns the offer, or null when
        /// refused (validation failure, forbidden authority scope, or the offering
        /// person lacks authority). The offer starts in OfferState.Offered.
        /// </summary>
        public EmploymentOffer MakeOffer(
            string businessInstanceId, int candidatePersonId, int offeringPersonId,
            int wageCentsPerWeek, string duties, string scheduleText, int startDayIndex,
            int trialDays, string workLocation, int durationDays, string housingNote,
            string authorityScope, string conditions, int terminationNoticeDays,
            string deliveryChannel, string howHeard, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            { diag.Add("EmploymentNegotiationService.MakeOffer: a business is required."); return null; }
            if (candidatePersonId < 0)
            { diag.Add("EmploymentNegotiationService.MakeOffer: the candidate must be a real person."); return null; }
            if (offeringPersonId < 0)
            { diag.Add("EmploymentNegotiationService.MakeOffer: the offering person must be a real person."); return null; }
            if (wageCentsPerWeek <= 0)
            { diag.Add("EmploymentNegotiationService.MakeOffer: wage must be positive — unpaid employment is not employment."); return null; }
            if (string.IsNullOrWhiteSpace(duties))
            { diag.Add("EmploymentNegotiationService.MakeOffer: duties must be stated — a person cannot agree to unstated work."); return null; }
            if (startDayIndex < dayIndex)
            { diag.Add($"EmploymentNegotiationService.MakeOffer: start day {startDayIndex} is in the past (today is {dayIndex})."); return null; }
            if (trialDays < 0 || terminationNoticeDays < 0)
            { diag.Add("EmploymentNegotiationService.MakeOffer: trial and notice days must be non-negative."); return null; }
            if (string.IsNullOrWhiteSpace(deliveryChannel))
            { diag.Add("EmploymentNegotiationService.MakeOffer: a delivery channel is required (InPerson, Letter, Handbill, Newspaper, WordOfMouth)."); return null; }

            // GUARD: employment never grants ownership or residential rights.
            string scope = authorityScope ?? string.Empty;
            foreach (string token in ForbiddenAuthorityScopeTokens)
            {
                if (scope.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    diag.Add($"EmploymentNegotiationService.MakeOffer: refused — authority scope claims '{token}'. " +
                        "Employment NEVER grants ownership or residential rights (brief §6).");
                    return null;
                }
            }

            if (offerAuthorityPredicate != null)
            {
                bool permitted;
                try { permitted = offerAuthorityPredicate(businessInstanceId, offeringPersonId, HireStaffAct); }
                catch (Exception ex)
                {
                    diag.Add($"EmploymentNegotiationService.MakeOffer: authority check threw ({ex.GetType().Name}) — offer refused.");
                    return null;
                }
                if (!permitted)
                {
                    diag.Add($"EmploymentNegotiationService.MakeOffer: P{offeringPersonId} holds no offer authority for '{businessInstanceId}' " +
                        "(must be the business owner or hold the HireStaff delegation scope) — offer refused.");
                    return null;
                }
            }
            else
            {
                diag.Add("EmploymentNegotiationService.MakeOffer: no authority predicate wired — offer refused.");
                return null;
            }

            var offer = new EmploymentOffer
            {
                OfferId = $"offer-{dayIndex}-{sequence++}",
                BusinessInstanceId = businessInstanceId,
                CandidatePersonId = candidatePersonId,
                OfferingPersonId = offeringPersonId,
                WageCentsPerWeek = wageCentsPerWeek,
                Duties = duties,
                ScheduleText = scheduleText ?? string.Empty,
                StartDayIndex = startDayIndex,
                TrialDays = trialDays,
                WorkLocation = workLocation ?? string.Empty,
                DurationDays = Math.Max(0, durationDays),
                HousingNote = housingNote ?? string.Empty,
                AuthorityScope = scope,
                Conditions = conditions ?? string.Empty,
                TerminationNoticeDays = terminationNoticeDays,
                State = OfferState.Offered,
                CreatedDayIndex = dayIndex,
                ExpiresDayIndex = dayIndex + OfferValidityDays,
                DeliveryChannel = deliveryChannel,
                HowHeard = howHeard ?? string.Empty,
                CounterWageCentsPerWeek = 0,
                LastNote = $"day {dayIndex}: offered {wageCentsPerWeek}c/week via {deliveryChannel}.",
            };
            offers[offer.OfferId] = offer;
            diag.Add($"EmploymentNegotiationService: offer {offer.OfferId} — {businessInstanceId} offers P{candidatePersonId} " +
                $"{wageCentsPerWeek}c/week ('{duties}'), start day {startDayIndex}, expires day {offer.ExpiresDayIndex}.");
            return offer;
        }

        /// <summary>
        /// Records a candidate counter-offer. Returns an error string or null.
        /// Only an Offered or Countered offer can be countered; a lapsed offer is
        /// expired first (honest outcome — it cannot be revived by negotiating).
        /// </summary>
        public string RecordCounter(string offerId, int counterWageCentsPerWeek, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!offers.TryGetValue(offerId, out EmploymentOffer offer))
                return $"EmploymentNegotiationService.RecordCounter: unknown offer '{offerId}'.";
            if (offer.State != OfferState.Offered && offer.State != OfferState.Countered)
                return $"EmploymentNegotiationService.RecordCounter: offer '{offerId}' is {offer.State} — counters are only for open offers.";
            if (counterWageCentsPerWeek <= 0)
                return "EmploymentNegotiationService.RecordCounter: the counter wage must be positive.";
            if (dayIndex > offer.ExpiresDayIndex)
            {
                offer.State = OfferState.Expired;
                offer.LastNote = $"day {dayIndex}: lapsed unanswered past day {offer.ExpiresDayIndex} — expired.";
                diag.Add($"EmploymentNegotiationService: offer '{offerId}' expired before the counter arrived.");
                return $"EmploymentNegotiationService.RecordCounter: offer '{offerId}' expired on day {offer.ExpiresDayIndex}.";
            }

            offer.CounterWageCentsPerWeek = counterWageCentsPerWeek;
            offer.EmployerAcceptedCounterOffer = false;
            offer.State = OfferState.Countered;
            offer.LastNote = $"day {dayIndex}: candidate countered at {counterWageCentsPerWeek}c/week.";
            diag.Add($"EmploymentNegotiationService: offer '{offerId}' countered at {counterWageCentsPerWeek}c/week.");
            return null;
        }

        /// <summary>
        /// Accepts the candidate's revised terms on behalf of the employer.
        /// A counteroffer is only eligible for candidate acceptance after this
        /// authority-checked step; a worker cannot unilaterally bind the
        /// Business to a higher wage.
        /// </summary>
        public string AcceptCounterOffer(string offerId, int acceptingEmployerPersonId,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!offers.TryGetValue(offerId, out EmploymentOffer offer))
                return $"EmploymentNegotiationService.AcceptCounterOffer: unknown offer '{offerId}'.";
            if (offer.State != OfferState.Countered)
                return $"EmploymentNegotiationService.AcceptCounterOffer: offer '{offerId}' is {offer.State} — only a countered offer can be employer-accepted.";
            if (dayIndex > offer.ExpiresDayIndex)
            {
                offer.State = OfferState.Expired;
                return $"EmploymentNegotiationService.AcceptCounterOffer: offer '{offerId}' expired on day {offer.ExpiresDayIndex}.";
            }
            if (acceptingEmployerPersonId != offer.OfferingPersonId)
                return "EmploymentNegotiationService.AcceptCounterOffer: only the offering employer or its delegated authority may accept revised terms.";
            if (offerAuthorityPredicate == null)
                return "EmploymentNegotiationService.AcceptCounterOffer: no authority predicate wired — revised terms refused.";

            bool permitted;
            try
            {
                permitted = offerAuthorityPredicate(offer.BusinessInstanceId, acceptingEmployerPersonId, HireStaffAct);
            }
            catch (Exception ex)
            {
                return $"EmploymentNegotiationService.AcceptCounterOffer: authority check threw ({ex.GetType().Name}) — revised terms refused.";
            }
            if (!permitted)
                return $"EmploymentNegotiationService.AcceptCounterOffer: P{acceptingEmployerPersonId} lacks hiring authority for '{offer.BusinessInstanceId}'.";

            offer.EmployerAcceptedCounterOffer = true;
            offer.LastNote = $"day {dayIndex}: employer accepted candidate counter at {offer.CounterWageCentsPerWeek}c/week.";
            diag.Add($"EmploymentNegotiationService: employer accepted counter for '{offerId}' at {offer.CounterWageCentsPerWeek}c/week.");
            return null;
        }

        /// <summary>
        /// Records a candidate refusal. Returns an error string or null.
        /// Refusal is terminal; the offer is retained as history (brief §6).
        /// </summary>
        public string RecordRefusal(string offerId, string reason, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!offers.TryGetValue(offerId, out EmploymentOffer offer))
                return $"EmploymentNegotiationService.RecordRefusal: unknown offer '{offerId}'.";
            if (offer.State != OfferState.Offered && offer.State != OfferState.Countered)
                return $"EmploymentNegotiationService.RecordRefusal: offer '{offerId}' is {offer.State} — refusals are only for open offers.";

            offer.State = OfferState.Refused;
            offer.LastNote = $"day {dayIndex}: refused — {reason ?? "no reason given"}.";
            diag.Add($"EmploymentNegotiationService: offer '{offerId}' refused by P{offer.CandidatePersonId}: {reason}. (An honest outcome.)");
            return null;
        }

        /// <summary>
        /// Records the employer withdrawing an open offer. Returns an error string or null.
        /// </summary>
        public string RecordWithdraw(string offerId, string reason, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!offers.TryGetValue(offerId, out EmploymentOffer offer))
                return $"EmploymentNegotiationService.RecordWithdraw: unknown offer '{offerId}'.";
            if (offer.State != OfferState.Offered && offer.State != OfferState.Countered)
                return $"EmploymentNegotiationService.RecordWithdraw: offer '{offerId}' is {offer.State} — only open offers can be withdrawn.";

            offer.State = OfferState.Withdrawn;
            offer.LastNote = $"day {dayIndex}: withdrawn by employer — {reason ?? "no reason given"}.";
            diag.Add($"EmploymentNegotiationService: offer '{offerId}' withdrawn.");
            return null;
        }

        /// <summary>
        /// Accepts an open offer and registers the EmploymentRelationship in the
        /// PKG-6 registry. LifecycleState = PendingStart until StartDayIndex
        /// (AdvanceDay flips it Active on the start day). Full negotiated terms
        /// are recorded in LegacyNotes + Compensation; housing/board stays a
        /// linked agreement note (never a property transfer). Returns an error
        /// string or null. Reload never re-hires: an Accepted offer cannot be
        /// accepted again, and the registry is first-wins on employment ids.
        /// </summary>
        public string AcceptOffer(string offerId, int dayIndex, EmploymentRelationshipRegistry employmentRegistry, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!offers.TryGetValue(offerId, out EmploymentOffer offer))
                return $"EmploymentNegotiationService.AcceptOffer: unknown offer '{offerId}'.";
            if (offer.State != OfferState.Offered && offer.State != OfferState.Countered)
                return $"EmploymentNegotiationService.AcceptOffer: offer '{offerId}' is {offer.State} — only an open offer can be accepted.";
            if (offer.State == OfferState.Countered && !offer.EmployerAcceptedCounterOffer)
                return "EmploymentNegotiationService.AcceptOffer: employer has not accepted the candidate counteroffer; revised terms are not binding.";
            if (dayIndex > offer.ExpiresDayIndex)
            {
                offer.State = OfferState.Expired;
                diag.Add($"EmploymentNegotiationService: offer '{offerId}' expired before acceptance.");
                return $"EmploymentNegotiationService.AcceptOffer: offer '{offerId}' expired on day {offer.ExpiresDayIndex}.";
            }
            if (employmentRegistry == null)
                return "EmploymentNegotiationService.AcceptOffer: an employment registry is required — employment is a real registered record, never implied.";

            int finalWage = offer.CounterWageCentsPerWeek > 0 ? offer.CounterWageCentsPerWeek : offer.WageCentsPerWeek;
            string employmentId = $"emp-{offer.OfferId}";
            var relationship = new EmploymentRelationship
            {
                Id = employmentId,
                EmployeePersonId = offer.CandidatePersonId,
                EmployerBusinessId = offer.BusinessInstanceId,
                RoleDisplayName = offer.Duties,
                Kind = (offer.DurationDays > 0 && offer.DurationDays <= 90)
                    ? EmploymentKind.SeasonalOrCasual : EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.PendingStart,
                Compensation = CompensationTerms.FromWeeklyWage(finalWage, "negotiated employment terms"),
                StartDayIndex = offer.StartDayIndex,
                EndDayIndex = offer.DurationDays > 0 ? offer.StartDayIndex + offer.DurationDays : -1,
                Source = EmploymentSource.Manual,
                LegacyNotes = BuildTermsNotes(offer, finalWage),
            };
            string problem = employmentRegistry.Register(relationship);
            if (problem != null)
            {
                diag.Add($"EmploymentNegotiationService: offer '{offerId}' acceptance failed — {problem}");
                return problem;
            }

            acceptedEmploymentByOffer[offer.OfferId] = employmentId;
            offer.State = OfferState.Accepted;
            offer.LastNote = $"day {dayIndex}: accepted at {finalWage}c/week; employment {employmentId} registered PendingStart until day {offer.StartDayIndex}.";
            diag.Add($"EmploymentNegotiationService: offer '{offerId}' accepted — P{offer.CandidatePersonId} employed at " +
                $"{finalWage}c/week, starts day {offer.StartDayIndex} (PendingStart).");
            return null;
        }

        /// <summary>
        /// Advances one day: open offers past their expiry lapse to Expired, and
        /// PendingStart employments created by this service flip to Active once
        /// their StartDayIndex arrives. Never re-sends, never re-hires.
        /// </summary>
        public void AdvanceDay(int dayIndex, EmploymentRelationshipRegistry employmentRegistry, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (EmploymentOffer offer in offers.Values)
            {
                if ((offer.State == OfferState.Offered || offer.State == OfferState.Countered)
                    && dayIndex > offer.ExpiresDayIndex)
                {
                    offer.State = OfferState.Expired;
                    offer.LastNote = $"day {dayIndex}: lapsed unanswered past day {offer.ExpiresDayIndex} — expired.";
                    diag.Add($"EmploymentNegotiationService: offer '{offer.OfferId}' expired.");
                }
            }
            if (employmentRegistry == null) return;
            foreach (var pair in acceptedEmploymentByOffer)
            {
                if (employmentRegistry.TryGetById(pair.Value, out EmploymentRelationship rel)
                    && rel.LifecycleState == EmploymentLifecycleState.PendingStart
                    && dayIndex >= rel.StartDayIndex)
                {
                    rel.LifecycleState = EmploymentLifecycleState.Active;
                    diag.Add($"EmploymentNegotiationService: employment {pair.Value} active from day {rel.StartDayIndex}.");
                }
            }
        }

        public bool TryGetOffer(string offerId, out EmploymentOffer offer) =>
            offers.TryGetValue(offerId, out offer);

        public int OfferCount => offers.Count;

        private static string BuildTermsNotes(EmploymentOffer offer, int finalWage)
        {
            var terms = new List<string>
            {
                $"negotiated offer {offer.OfferId} (delivery: {offer.DeliveryChannel}; heard via: {offer.HowHeard})",
                $"duties: {offer.Duties}",
                $"schedule: {offer.ScheduleText}",
                $"wage: {finalWage}c/week (offered {offer.WageCentsPerWeek}c" +
                    (offer.CounterWageCentsPerWeek > 0 ? $"; countered {offer.CounterWageCentsPerWeek}c" : "") + ")",
                $"start day: {offer.StartDayIndex}; trial: {offer.TrialDays}d; duration: " +
                    (offer.DurationDays > 0 ? $"{offer.DurationDays}d" : "indefinite"),
                $"location: {offer.WorkLocation}",
                $"authority scope: {offer.AuthorityScope}",
                $"conditions: {offer.Conditions}",
                $"termination notice: {offer.TerminationNoticeDays}d",
            };
            if (!string.IsNullOrWhiteSpace(offer.HousingNote))
                terms.Add($"housing/board LINKED NOTE ONLY — no property transfer, no residential rights granted: {offer.HousingNote}");
            // GUARD restated on the record itself: anyone reading the employment
            // terms sees the boundary.
            terms.Add("Employment grants no ownership or residential rights (brief §6).");
            return string.Join(" | ", terms);
        }

        /// <summary>CLN-1: captures offers for the save pipeline.</summary>
        public EmploymentNegotiationSaveDto CaptureSaveDto()
        {
            var dto = new EmploymentNegotiationSaveDto
            {
                offers = new List<EmploymentOffer>(offers.Values),
                sequence = sequence,
            };
            foreach (var pair in acceptedEmploymentByOffer)
                dto.acceptedPairs.Add(new OfferEmploymentPair { offerId = pair.Key, employmentId = pair.Value });
            return dto;
        }

        /// <summary>
        /// CLN-1: clear-then-load. Terminal offers stay terminal — reload never
        /// re-sends an offer and never re-hires (AcceptOffer refuses terminal
        /// states; the registry is first-wins on employment ids).
        /// </summary>
        public void LoadFromSaveDto(EmploymentNegotiationSaveDto dto)
        {
            offers.Clear();
            acceptedEmploymentByOffer.Clear();
            if (dto == null) return;
            if (dto.offers != null)
                foreach (EmploymentOffer offer in dto.offers)
                    if (offer != null && !string.IsNullOrEmpty(offer.OfferId))
                        offers[offer.OfferId] = offer;
            if (dto.acceptedPairs != null)
                foreach (OfferEmploymentPair pair in dto.acceptedPairs)
                    if (pair != null && !string.IsNullOrEmpty(pair.offerId) && !string.IsNullOrEmpty(pair.employmentId))
                        acceptedEmploymentByOffer[pair.offerId] = pair.employmentId;
            sequence = Math.Max(0, dto.sequence);
        }
    }

    /// <summary>CLN-1: save data for the employment negotiation service.</summary>
    [Serializable]
    public sealed class OfferEmploymentPair
    {
        public string offerId = string.Empty;
        public string employmentId = string.Empty;
    }

    /// <summary>CLN-1: save data for the employment negotiation service.</summary>
    [Serializable]
    public sealed class EmploymentNegotiationSaveDto
    {
        public List<EmploymentOffer> offers = new List<EmploymentOffer>();
        // List-of-pairs instead of a Dictionary: the save pipeline serializes
        // via Unity JsonUtility, which does not serialize dictionaries.
        public List<OfferEmploymentPair> acceptedPairs = new List<OfferEmploymentPair>();
        public int sequence;
    }
}
