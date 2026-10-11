// Known-contacts authority for contact-based recruitment (Autonomous Labor, Phase B).
// Canon §6.2 (mission brief §5): recruitment runs through real information
// channels — personal relationships, referrals, existing employees, prior
// correspondence — never an omniscient applicant list. A candidate appears only
// through a DOCUMENTED link: family via household membership, former coworkers
// via shared employment history, correspondents via recruitment inquiries and
// letters. This service owns the link graph; callers seed it from
// HouseholdMembershipRegistry, shared employment/profession history, and
// RecruitmentService inquiries/letters. It never creates persons and never
// invents skills or availability.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Why two persons are known to each other. Every kind must carry a
    /// provenance note naming the documented source (household id, shared
    /// employer, inquiry/letter id, effort id, witness context).
    /// </summary>
    public enum ContactLinkKind
    {
        HouseholdMember = 0,
        FormerCoworker = 1,
        Correspondent = 2,
        EmployeeReferral = 3,
        Witnessed = 4,
    }

    /// <summary>
    /// One directed known-contact edge: PersonId knows LinkedPersonId.
    /// Seeding helpers add the reverse edge so lookups read naturally.
    /// </summary>
    [Serializable]
    public sealed class ContactLink
    {
        public int PersonId;
        public int LinkedPersonId;
        public ContactLinkKind Kind;
        /// <summary>Documented source, e.g. "shared household 12", "worked together at store-1",
        /// "inquiry inq-…", "referred for effort recruit-… by P7". Never blank on a real link.</summary>
        public string Provenance = string.Empty;
    }

    /// <summary>
    /// Known-contacts authority. ReachableCandidates returns ONLY persons with a
    /// documented link from the caller's known set — a stranger with no link is
    /// unreachable, no matter how convenient. Evidence-free: ids only, no skill
    /// or availability data (the employer evaluates the visible profile separately).
    /// </summary>
    public sealed class ContactGraphService
    {
        private readonly Dictionary<string, ContactLink> links =
            new Dictionary<string, ContactLink>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        private static string EdgeKey(int personId, int linkedPersonId) =>
            personId + "->" + linkedPersonId;

        /// <summary>Adds one directed link. Idempotent on repeat (first provenance wins);
        /// returns an error string or null.</summary>
        public string AddLink(int personId, int linkedPersonId, ContactLinkKind kind, string provenance)
        {
            if (personId < 0 || linkedPersonId < 0)
                return $"ContactGraphService.AddLink: person ids must be non-negative (got {personId}, {linkedPersonId}).";
            if (personId == linkedPersonId)
                return $"ContactGraphService.AddLink: P{personId} cannot link to themselves.";
            if (string.IsNullOrWhiteSpace(provenance))
                return "ContactGraphService.AddLink: a documented provenance is required — undocumented links are not contacts.";

            string key = EdgeKey(personId, linkedPersonId);
            if (links.ContainsKey(key))
            {
                diagnostics.Add($"ContactGraphService: P{personId}→P{linkedPersonId} already documented — keeping first provenance.");
                return null;
            }
            links[key] = new ContactLink
            {
                PersonId = personId,
                LinkedPersonId = linkedPersonId,
                Kind = kind,
                Provenance = provenance,
            };
            return null;
        }

        /// <summary>
        /// Seeds family links from household membership: every member of the
        /// household knows every other member (both directions).
        /// </summary>
        public void SeedHouseholdLinks(int personId, int householdId, IEnumerable<int> memberPersonIds)
        {
            if (memberPersonIds == null) return;
            foreach (int memberId in memberPersonIds)
            {
                if (memberId == personId) continue;
                string provenance = $"shared household {householdId}";
                AddLink(personId, memberId, ContactLinkKind.HouseholdMember, provenance);
                AddLink(memberId, personId, ContactLinkKind.HouseholdMember, provenance);
            }
        }

        /// <summary>
        /// Seeds former-coworker links from shared employment history (both directions).
        /// </summary>
        public void SeedFormerCoworkerLinks(int personId, string employerBusinessId, IEnumerable<int> coworkerPersonIds)
        {
            if (coworkerPersonIds == null) return;
            foreach (int coworkerId in coworkerPersonIds)
            {
                if (coworkerId == personId) continue;
                string provenance = $"worked together at {employerBusinessId}";
                AddLink(personId, coworkerId, ContactLinkKind.FormerCoworker, provenance);
                AddLink(coworkerId, personId, ContactLinkKind.FormerCoworker, provenance);
            }
        }

        /// <summary>
        /// Seeds a correspondence link from a recruitment inquiry or letter
        /// (both directions — once letters were exchanged, both sides know each other).
        /// </summary>
        public void SeedCorrespondenceLink(int fromPersonId, int toPersonId, string inquiryOrLetterId)
        {
            string provenance = $"correspondence {inquiryOrLetterId}";
            AddLink(fromPersonId, toPersonId, ContactLinkKind.Correspondent, provenance);
            AddLink(toPersonId, fromPersonId, ContactLinkKind.Correspondent, provenance);
        }

        /// <summary>
        /// Seeds an employee-referral link (both directions): the referrer vouched
        /// for the referred person for a specific hiring effort.
        /// </summary>
        public void SeedEmployeeReferralLink(int referrerPersonId, int referredPersonId, string effortId)
        {
            string provenance = $"referred for effort {effortId} by P{referrerPersonId}";
            AddLink(referrerPersonId, referredPersonId, ContactLinkKind.EmployeeReferral, provenance);
            AddLink(referredPersonId, referrerPersonId, ContactLinkKind.EmployeeReferral, provenance);
        }

        /// <summary>Seeds a witnessed link: the person was observed in a documented context.</summary>
        public void SeedWitnessedLink(int personId, int witnessedPersonId, string contextNote)
        {
            AddLink(personId, witnessedPersonId, ContactLinkKind.Witnessed, contextNote);
        }

        /// <summary>
        /// Returns ONLY persons with a documented link from the caller's known set
        /// (one hop — friend-of-a-friend is not a documented contact). Sorted,
        /// distinct, and never containing a via-person themselves. Evidence-free:
        /// plain person ids; the employer must consult the visible worker profile
        /// separately (brief §5: the player must not automatically know every
        /// worker's exact skills).
        /// </summary>
        public List<int> ReachableCandidates(string businessInstanceId, IEnumerable<int> viaPersonIds)
        {
            var found = new HashSet<int>();
            var via = new HashSet<int>();
            if (viaPersonIds != null)
                foreach (int id in viaPersonIds)
                    via.Add(id);

            foreach (ContactLink link in links.Values)
            {
                if (via.Contains(link.PersonId) && !via.Contains(link.LinkedPersonId))
                    found.Add(link.LinkedPersonId);
            }
            var result = new List<int>(found);
            result.Sort();
            return result;
        }

        public bool HasLink(int personId, int linkedPersonId) =>
            links.ContainsKey(EdgeKey(personId, linkedPersonId));

        /// <summary>
        /// Human-readable provenance for a documented link, suitable for the
        /// offer HowHeard chain, e.g. "P7↔P5 HouseholdMember: shared household 12".
        /// Returns null when no link is documented — provenance is never invented.
        /// </summary>
        public string DescribeProvenance(int personId, int linkedPersonId)
        {
            if (!links.TryGetValue(EdgeKey(personId, linkedPersonId), out ContactLink link))
                return null;
            return $"P{link.PersonId}↔P{link.LinkedPersonId} {link.Kind}: {link.Provenance}";
        }

        public int LinkCount => links.Count;

        /// <summary>CLN-1: captures the link graph for the save pipeline.</summary>
        public ContactGraphServiceSaveDto CaptureSaveDto()
        {
            return new ContactGraphServiceSaveDto
            {
                links = new List<ContactLink>(links.Values),
            };
        }

        /// <summary>CLN-1: clear-then-load. Links are data, never actions — reload
        /// restores them without re-seeding, re-notifying, or re-hiring.</summary>
        public void LoadFromSaveDto(ContactGraphServiceSaveDto dto)
        {
            links.Clear();
            if (dto == null || dto.links == null) return;
            foreach (ContactLink link in dto.links)
            {
                if (link == null || link.PersonId < 0 || link.LinkedPersonId < 0) continue;
                links[EdgeKey(link.PersonId, link.LinkedPersonId)] = link;
            }
        }
    }

    /// <summary>CLN-1: save data for the contact graph service.</summary>
    [Serializable]
    public sealed class ContactGraphServiceSaveDto
    {
        public List<ContactLink> links = new List<ContactLink>();
    }
}
