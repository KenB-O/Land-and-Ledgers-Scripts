using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Postal;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Recruitment
{
    /// <summary>
    /// T2B: hiring channels. Canon Part VI §6.2: "Hire Worker should expose
    /// actual channels: contact a known Person, ask employees or associates
    /// for referrals, post handbills/notices, use local businesses/travel
    /// networks, send correspondence or buy newspaper advertising where
    /// available. These actions create awareness and possible inquiries from
    /// actual Persons over time. They do not spawn a candidate list on demand."
    /// GHOST-DES-051: hiring is a recruitment workflow, not an
    /// applicant-generation button.
    /// </summary>
    public enum RecruitmentChannel
    {
        Unspecified = 0,
        DirectContact = 1,   // approach a known person directly
        EmployeeReferral = 2, // ask employees/associates who they know (GHOST-DES-021)
        Handbill = 3,        // posted notices (costs printing)
        Correspondence = 4,  // letters — take real travel time (Canon §6.3)
        NewspaperAd = 5,     // paid advertising where available
        WordOfMouth = 6,     // passive awareness, no click (GHOST-DES-039)
    }

    public enum InquiryStatus
    {
        Unspecified = 0,
        Open = 1,
        Hired = 2,
        Declined = 3,
        Withdrawn = 4,
    }

    /// <summary>T2B: one opened hiring effort for a role at a business.</summary>
    [Serializable]
    public sealed class RecruitmentEffort
    {
        public string EffortId = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public string RoleDisplayName = string.Empty;
        public RecruitmentChannel Channel;
        public int OpenedDayIndex;
        public int CostCents;          // real cost of the channel (ads, printing)
        public string CostDescription = string.Empty;
        public bool Closed;
        public int Seed;               // deterministic inquiry arrivals

        public RecruitmentEffort() { }
    }

    /// <summary>
    /// T2B: an inquiry FROM an actual person. PersonId always references a
    /// real PersonState in the population — inquiries are never spawned.
    /// </summary>
    [Serializable]
    public sealed class CandidateInquiry
    {
        public string InquiryId = string.Empty;
        public string EffortId = string.Empty;
        public int PersonId = -1;
        public int ReceivedDayIndex;
        public string HowHeard = string.Empty;
        public string Note = string.Empty;
        public InquiryStatus Status = InquiryStatus.Open;

        public CandidateInquiry() { }
    }

    /// <summary>T2B: a letter in transit (Canon §6.3: letters take real travel time).</summary>
    [Serializable]
    public sealed class RecruitmentLetter
    {
        public string LetterId = string.Empty;
        public string EffortId = string.Empty;
        public int RecipientPersonId = -1;
        public int SentDayIndex;
        public int ArrivalDayIndex;
        public bool Resolved;
        /// <summary>NX-2A: when routed through the postal service, the MailItem id.
        /// Resolution is then gated on the item's real arrival, not the estimate.</summary>
        public string PostalMailId = string.Empty;

        public RecruitmentLetter() { }
    }

    /// <summary>
    /// T2B: recruitment as a workflow over time. Inquiries arrive from ACTUAL
    /// persons in the population — adults with labor access who are not
    /// currently employed. Nothing here creates people.
    /// </summary>
    public sealed class RecruitmentService
    {
        private readonly Dictionary<string, RecruitmentEffort> efforts =
            new Dictionary<string, RecruitmentEffort>(StringComparer.Ordinal);
        private readonly Dictionary<string, CandidateInquiry> inquiries =
            new Dictionary<string, CandidateInquiry>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecruitmentLetter> letters =
            new Dictionary<string, RecruitmentLetter>(StringComparer.Ordinal);
        private int sequence;

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public IReadOnlyCollection<RecruitmentEffort> Efforts => efforts.Values;
        public IReadOnlyCollection<CandidateInquiry> Inquiries => inquiries.Values;

        /// <summary>
        /// Opens a hiring effort. Paid channels record their real cost on the
        /// effort (the caller books the outflow through the normal expense
        /// flow — the cost is never silently absorbed).
        /// </summary>
        public RecruitmentEffort OpenEffort(
            string businessInstanceId, string roleDisplayName, RecruitmentChannel channel,
            int dayIndex, int seed, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(businessInstanceId) || string.IsNullOrWhiteSpace(roleDisplayName))
            {
                diag.Add("RecruitmentService.OpenEffort: business and role required.");
                return null;
            }
            if (channel == RecruitmentChannel.Unspecified)
            {
                diag.Add("RecruitmentService.OpenEffort: a real channel is required (Canon §6.2).");
                return null;
            }

            var effort = new RecruitmentEffort
            {
                EffortId = $"recruit-{dayIndex}-{sequence++}",
                BusinessInstanceId = businessInstanceId,
                RoleDisplayName = roleDisplayName,
                Channel = channel,
                OpenedDayIndex = dayIndex,
                Seed = seed,
            };
            switch (channel)
            {
                case RecruitmentChannel.Handbill:
                    effort.CostCents = 150; // printing — calibration
                    effort.CostDescription = "handbill printing";
                    break;
                case RecruitmentChannel.NewspaperAd:
                    effort.CostCents = 500; // placement — calibration
                    effort.CostDescription = "newspaper advertisement placement";
                    break;
                case RecruitmentChannel.Correspondence:
                    effort.CostCents = 25; // postage — calibration
                    effort.CostDescription = "postage";
                    break;
            }
            efforts[effort.EffortId] = effort;
            diag.Add($"RecruitmentService: effort {effort.EffortId} opened for '{roleDisplayName}' via {channel}" +
                     (effort.CostCents > 0 ? $" (cost {effort.CostCents}c: {effort.CostDescription} — book through expenses)." : "."));
            return effort;
        }

        /// <summary>
        /// T2B: advances one day. Open efforts accrue inquiries from actual
        /// persons over time — channel-dependent rates, deterministic per the
        /// effort's seed. Passive word-of-mouth (GHOST-DES-039) spreads slowly
        /// with no action at all.
        /// NX-2A: postal-routed letters resolve on the mail item's real arrival.
        /// </summary>
        public void AdvanceDay(
            PopulationState population, EmploymentRelationshipRegistry employment,
            int dayIndex, List<string> diag, PostalService postal = null)
        {
            diag = diag ?? diagnostics;
            if (population == null) return;

            foreach (RecruitmentEffort effort in efforts.Values)
            {
                if (effort.Closed || effort.Channel == RecruitmentChannel.DirectContact ||
                    effort.Channel == RecruitmentChannel.EmployeeReferral ||
                    effort.Channel == RecruitmentChannel.Correspondence)
                    continue; // these channels produce inquiries only through their actions

                var rng = new System.Random(effort.Seed * 7919 + dayIndex);
                double dailyRate = effort.Channel switch
                {
                    RecruitmentChannel.NewspaperAd => 0.35,
                    RecruitmentChannel.Handbill => 0.20,
                    RecruitmentChannel.WordOfMouth => 0.05,
                    _ => 0.0,
                };
                if (rng.NextDouble() < dailyRate)
                    TryAddInquiry(population, employment, effort, dayIndex,
                        effort.Channel.ToString(), "arrived on their own", diag);
            }

            ResolveLetters(population, employment, dayIndex, diag, postal);
        }

        private void TryAddInquiry(
            PopulationState population, EmploymentRelationshipRegistry employment,
            RecruitmentEffort effort, int dayIndex, string howHeard, string note,
            List<string> diag)
        {
            PersonState candidate = FindCandidate(population, employment, effort.Seed + dayIndex);
            if (candidate == null)
            {
                diag.Add($"RecruitmentService: no plausible candidate available for effort {effort.EffortId} on day {dayIndex} — no inquiry faked.");
                return;
            }
            // One open inquiry per person per effort — no duplicates.
            foreach (CandidateInquiry existing in inquiries.Values)
            {
                if (existing.EffortId == effort.EffortId && existing.PersonId == candidate.id &&
                    existing.Status == InquiryStatus.Open)
                    return;
            }
            var inquiry = new CandidateInquiry
            {
                InquiryId = $"inq-{effort.EffortId}-{candidate.id}-{dayIndex}",
                EffortId = effort.EffortId,
                PersonId = candidate.id,
                ReceivedDayIndex = dayIndex,
                HowHeard = howHeard,
                Note = note,
            };
            inquiries[inquiry.InquiryId] = inquiry;
            diag.Add($"RecruitmentService: inquiry {inquiry.InquiryId} — {candidate.firstName} {candidate.lastName} (P{candidate.id}) heard via {howHeard}.");
        }

        /// <summary>
        /// T2B: finds a plausible candidate among ACTUAL persons: adults with
        /// labor access who are not currently actively employed. Returns null
        /// when nobody qualifies — the caller must not invent one.
        /// </summary>
        private PersonState FindCandidate(PopulationState population, EmploymentRelationshipRegistry employment, int seed)
        {
            var pool = new List<PersonState>();
            foreach (PersonState p in population.people)
            {
                if (p == null || p.ageBand != AgeBand.Adult18Plus) continue;
                if (p.laborAccessLevel == LaborAccessLevel.None) continue;
                bool employed = false;
                if (employment != null)
                {
                    foreach (EmploymentRelationship rel in employment.GetActiveByEmployee(p.id))
                    { employed = true; break; }
                }
                if (!employed) pool.Add(p);
            }
            if (pool.Count == 0) return null;
            return pool[new System.Random(seed * 104729 + 7).Next(pool.Count)];
        }

        /// <summary>
        /// T2B: ask a contact whether they genuinely know someone (Canon §6.3,
        /// GHOST-DES-021/041). Family = household members; former coworkers =
        /// same profession. A relationship can produce a plausible lead OR NO
        /// LEAD — both are honest outcomes.
        /// </summary>
        public CandidateInquiry AskForReferral(
            string effortId, int contactPersonId,
            PopulationState population, EmploymentRelationshipRegistry employment,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!efforts.TryGetValue(effortId, out RecruitmentEffort effort) || effort.Closed)
            {
                diag.Add("RecruitmentService.AskForReferral: unknown or closed effort.");
                return null;
            }
            PersonState contact = population?.GetPerson(contactPersonId);
            if (contact == null)
            {
                diag.Add($"RecruitmentService.AskForReferral: P{contactPersonId} is not a known person.");
                return null;
            }

            var leads = new List<PersonState>();
            foreach (PersonState p in population.people)
            {
                if (p == null || p.id == contactPersonId || p.ageBand != AgeBand.Adult18Plus) continue;
                if (p.laborAccessLevel == LaborAccessLevel.None) continue;
                bool isFamily = p.householdId == contact.householdId && p.householdId >= 0;
                bool isFormerCoworker = !string.IsNullOrEmpty(p.professionId) && p.professionId == contact.professionId;
                if (!isFamily && !isFormerCoworker) continue;
                bool employed = false;
                if (employment != null)
                    foreach (EmploymentRelationship rel in employment.GetActiveByEmployee(p.id)) { employed = true; break; }
                if (!employed) leads.Add(p);
            }

            var rng = new System.Random(effort.Seed * 31 + contactPersonId * 17 + dayIndex);
            if (leads.Count == 0 || rng.NextDouble() < 0.35)
            {
                diag.Add($"RecruitmentService: P{contactPersonId} knows no one suitable right now — no lead. (An honest outcome per Canon §6.3.)");
                return null;
            }
            PersonState lead = leads[rng.Next(leads.Count)];
            var inquiry = new CandidateInquiry
            {
                InquiryId = $"inq-{effortId}-ref-{lead.id}-{dayIndex}",
                EffortId = effortId,
                PersonId = lead.id,
                ReceivedDayIndex = dayIndex,
                HowHeard = $"referred by P{contactPersonId}",
                Note = "referral",
            };
            inquiries[inquiry.InquiryId] = inquiry;
            diag.Add($"RecruitmentService: P{contactPersonId} referred {lead.firstName} {lead.lastName} (P{lead.id}).");
            return inquiry;
        }

        /// <summary>
        /// T2B: send a letter (Canon §6.3 — letters take real travel time).
        /// Arrival day comes from the journey model; no route = no letter.
        /// NX-2A: when a postal service is provided, the letter travels by post —
        /// posted at the nearest office, carried on real schedules with real
        /// handoff, and resolution waits on the item's actual arrival. Without a
        /// post office, the letter goes by foot messenger (the sender's errand).
        /// </summary>
        public RecruitmentLetter SendLetter(
            string effortId, int recipientPersonId,
            string fromLocationId, string toLocationId,
            JourneyModel journeys, int dayIndex, List<string> diag,
            PostalService postal = null)
        {
            diag = diag ?? diagnostics;
            if (!efforts.TryGetValue(effortId, out RecruitmentEffort effort) || effort.Closed)
            {
                diag.Add("RecruitmentService.SendLetter: unknown or closed effort.");
                return null;
            }
            if (journeys == null)
            {
                diag.Add("RecruitmentService.SendLetter: no journey model — letters take real travel time (Canon §6.3).");
                return null;
            }

            if (postal != null)
            {
                string fromOffice = postal.FindNearestOfficeId(fromLocationId, journeys, diag);
                string toOffice = postal.FindNearestOfficeId(toLocationId, journeys, diag);
                if (!string.IsNullOrEmpty(fromOffice) && !string.IsNullOrEmpty(toOffice))
                {
                    MailItem item = postal.PostItem(
                        MailKind.Letter,
                        $"hiring: {effort.RoleDisplayName} at {effort.BusinessInstanceId}",
                        recipientPersonId, $"P{recipientPersonId}",
                        fromOffice, toOffice, dayIndex, diag);
                    if (item != null)
                    {
                        int postalDays = Math.Max(1, postal.EstimateTransitDays(fromOffice, toOffice, journeys));
                        diag.Add($"RecruitmentService: letter routed by post ({fromOffice} → {toOffice}) — " +
                            "no instant delivery; resolution waits on real arrival (NX-2A).");
                        return FinishLetter(effortId, effort, recipientPersonId, dayIndex, postalDays, item.MailId, diag);
                    }
                    diag.Add("RecruitmentService.SendLetter: post office refused the letter — falling back to foot messenger.");
                }
                else
                {
                    diag.Add("RecruitmentService.SendLetter: no reachable post office — letter goes by foot messenger.");
                }
            }

            JourneyRoute route = journeys.FindRoute(fromLocationId, toLocationId, TravelMode.Foot);
            if (!route.Found)
            {
                diag.Add($"RecruitmentService.SendLetter: no route from '{fromLocationId}' to '{toLocationId}' — {route.Diagnostic}. Letter not sent.");
                return null;
            }
            int transitDays = Math.Max(1, (route.TotalMinutes + 1439) / 1440);
            return FinishLetter(effortId, effort, recipientPersonId, dayIndex, transitDays, string.Empty, diag);
        }

        private RecruitmentLetter FinishLetter(
            string effortId, RecruitmentEffort effort, int recipientPersonId, int dayIndex,
            int transitDays, string postalMailId, List<string> diag)
        {
            var letter = new RecruitmentLetter
            {
                LetterId = $"ltr-{effortId}-{recipientPersonId}-{dayIndex}",
                EffortId = effort.EffortId,
                RecipientPersonId = recipientPersonId,
                SentDayIndex = dayIndex,
                ArrivalDayIndex = dayIndex + transitDays,
                PostalMailId = postalMailId ?? string.Empty,
            };
            letters[letter.LetterId] = letter;
            diag.Add($"RecruitmentService: letter {letter.LetterId} sent — arrives day {letter.ArrivalDayIndex} ({transitDays}d travel).");
            return letter;
        }

        private void ResolveLetters(
            PopulationState population, EmploymentRelationshipRegistry employment,
            int dayIndex, List<string> diag, PostalService postal = null)
        {
            foreach (RecruitmentLetter letter in letters.Values)
            {
                if (letter.Resolved) continue;
                // NX-2A: postal-routed letters resolve on the mail item's REAL
                // arrival, not the estimate — no instant information transfer.
                if (!string.IsNullOrEmpty(letter.PostalMailId) && postal != null)
                {
                    MailItem item = postal.GetMailItem(letter.PostalMailId);
                    if (item == null)
                    {
                        diag.Add($"RecruitmentService: letter {letter.LetterId} has no postal record — no reply faked.");
                        letter.Resolved = true;
                        continue;
                    }
                    if (item.Status != MailStatus.Arrived && item.Status != MailStatus.Collected)
                        continue; // still traveling — the reply cannot exist yet
                    diag.Add($"RecruitmentService: letter {letter.LetterId} arrived by post on day {dayIndex} (posted day {letter.SentDayIndex}).");
                }
                else if (letter.ArrivalDayIndex > dayIndex)
                {
                    continue;
                }
                letter.Resolved = true;
                PersonState recipient = population.GetPerson(letter.RecipientPersonId);
                if (recipient == null)
                {
                    diag.Add($"RecruitmentService: letter {letter.LetterId} arrived but P{letter.RecipientPersonId} is unknown — no reply faked.");
                    continue;
                }
                if (!efforts.TryGetValue(letter.EffortId, out RecruitmentEffort effort) || effort.Closed) continue;

                // Recipients may reply, ignore, negotiate, or travel to inspect (Canon §6.3).
                var rng = new System.Random(letter.LetterId.GetHashCode() + dayIndex);
                double roll = rng.NextDouble();
                if (roll < 0.45)
                {
                    TryAddInquiry(population, employment, effort, dayIndex, "correspondence",
                        $"{recipient.firstName} {recipient.lastName} replied with interest", diag);
                }
                else if (roll < 0.60)
                {
                    TryAddInquiry(population, employment, effort, dayIndex, "correspondence",
                        $"{recipient.firstName} {recipient.lastName} wants to negotiate terms first", diag);
                }
                else if (roll < 0.70)
                {
                    diag.Add($"RecruitmentService: P{recipient.id} will travel to inspect before deciding (Canon §6.3) — no inquiry yet.");
                }
                else
                {
                    diag.Add($"RecruitmentService: P{recipient.id} ignored the letter. (An honest outcome per Canon §6.3.)");
                }
            }
        }

        /// <summary>
        /// T2B: hire from an open inquiry — creates a real EmploymentRelationship
        /// in the PKG-6 registry. Terms as negotiated; nothing is invented.
        /// </summary>
        public string HireFromInquiry(
            string inquiryId, int weeklyWageCents, EmploymentKind kind,
            int startDayIndex, EmploymentRelationshipRegistry employment,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!inquiries.TryGetValue(inquiryId, out CandidateInquiry inquiry))
                return $"RecruitmentService.HireFromInquiry: unknown inquiry '{inquiryId}'.";
            if (inquiry.Status != InquiryStatus.Open)
                return $"RecruitmentService.HireFromInquiry: inquiry '{inquiryId}' is {inquiry.Status}, not open.";
            if (!efforts.TryGetValue(inquiry.EffortId, out RecruitmentEffort effort))
                return $"RecruitmentService.HireFromInquiry: effort '{inquiry.EffortId}' unknown.";
            if (weeklyWageCents <= 0)
                return "RecruitmentService.HireFromInquiry: wage must be positive — unpaid employment is not employment.";

            var relationship = new EmploymentRelationship
            {
                Id = $"emp-{inquiryId}",
                EmployeePersonId = inquiry.PersonId,
                EmployerBusinessId = effort.BusinessInstanceId,
                RoleDisplayName = effort.RoleDisplayName,
                Kind = kind,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, $"hired via {effort.Channel}"),
                StartDayIndex = startDayIndex,
                Source = EmploymentSource.Manual,
                Notes = $"hired via {effort.Channel} (inquiry {inquiryId})",
            };
            string problem = employment.Register(relationship);
            if (problem != null) return problem;

            inquiry.Status = InquiryStatus.Hired;
            effort.Closed = true;
            diag.Add($"RecruitmentService: P{inquiry.PersonId} hired as '{effort.RoleDisplayName}' at {weeklyWageCents}c/week via {effort.Channel}.");
            return null;
        }

        /// <summary>
        /// T2B: approach a known person directly (Canon §6.2). They may show
        /// interest, decline, or want to negotiate — all honest outcomes, and
        /// the person is always real.
        /// </summary>
        public CandidateInquiry DirectApproach(
            string effortId, int personId,
            PopulationState population, EmploymentRelationshipRegistry employment,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!efforts.TryGetValue(effortId, out RecruitmentEffort effort) || effort.Closed)
            {
                diag.Add("RecruitmentService.DirectApproach: unknown or closed effort.");
                return null;
            }
            PersonState person = population?.GetPerson(personId);
            if (person == null)
            {
                diag.Add($"RecruitmentService.DirectApproach: P{personId} is not a known person.");
                return null;
            }
            if (person.ageBand != AgeBand.Adult18Plus || person.laborAccessLevel == LaborAccessLevel.None)
            {
                diag.Add($"RecruitmentService.DirectApproach: P{personId} cannot take the role.");
                return null;
            }
            if (employment != null)
                foreach (EmploymentRelationship rel in employment.GetActiveByEmployee(personId))
                {
                    diag.Add($"RecruitmentService.DirectApproach: P{personId} is already employed — no poaching by fiat.");
                    return null;
                }

            var rng = new System.Random(effort.Seed * 101 + personId * 13 + dayIndex);
            double roll = rng.NextDouble();
            if (roll < 0.55)
            {
                var inquiry = new CandidateInquiry
                {
                    InquiryId = $"inq-{effortId}-direct-{personId}-{dayIndex}",
                    EffortId = effortId,
                    PersonId = personId,
                    ReceivedDayIndex = dayIndex,
                    HowHeard = "direct approach",
                    Note = $"{person.firstName} {person.lastName} is interested",
                };
                inquiries[inquiry.InquiryId] = inquiry;
                diag.Add($"RecruitmentService: direct approach — P{personId} is interested.");
                return inquiry;
            }
            if (roll < 0.75)
            {
                diag.Add($"RecruitmentService: direct approach — P{personId} wants better terms first (negotiate).");
                return null;
            }
            diag.Add($"RecruitmentService: direct approach — P{personId} declined. (An honest outcome.)");
            return null;
        }

        public string CloseEffort(string effortId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!efforts.TryGetValue(effortId, out RecruitmentEffort effort))
                return $"RecruitmentService.CloseEffort: unknown effort '{effortId}'.";
            effort.Closed = true;
            diag.Add($"RecruitmentService: effort {effortId} closed.");
            return null;
        }
    }
}
