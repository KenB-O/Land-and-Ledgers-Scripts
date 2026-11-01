using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 3, item 3: player recruitment through conversation.
    ///
    /// The player mechanic Kennedy asked for, in code form:
    ///   1. Click a person. WorldInteractionController already shows what
    ///      is KNOWN about them (person inspection panel, relations
    ///      included); this class never adds omniscient data.
    ///   2. Interact. The player must walk to them and find them wherever
    ///      they are. That walk is a Unity/journey step, NOT faked here:
    ///      the caller must supply a legitimate
    ///      <see cref="ConversationChannelKind"/> (from
    ///      ConversationOpportunityService, which checks shared venue,
    ///      workplace, household, or delivered correspondence). An
    ///      Unspecified channel is refused, with a diagnostic, because
    ///      a conversation with no legitimate channel is exactly the
    ///      kind of invented interaction this codebase forbids.
    ///   3. Ask. Two questions, both answered ONLY from the speaker's
    ///      real kinship (family tree, item 2): (a) do you know anyone
    ///      who would like to move to town, (b) do you know anyone who
    ///      would like to work at a specific business the player runs.
    ///      "Friends" are NOT modelled yet: no friend graph exists in
    ///      code, so a friend answer would be invented. That gap is
    ///      documented, not filled with a random name.
    ///   4. Letter. A positive answer returns a RecruitmentLead with a
    ///      real contact person id. The next step is a letter from the
    ///      General Store: <see cref="RecruitmentLetterRequest"/> names
    ///      the General Store as origin, because post goes out from
    ///      there. Posting itself belongs to PostalService.PostItem
    ///      (real postage, real office ids, real chain of custody); this
    ///      class does NOT post mail, it only builds the request. Since
    ///      Pass XL phase 4 the hand-off is RecruitmentLetterPoster
    ///      .PostLetter (payer overload, one letter per call, no free
    ///      carriage), which cross-checks the caller-supplied origin
    ///      against the request's Origin since phase 5. Registering the
    ///      General Store office is still a Codex bootstrap task
    ///      (phase 4 finding F04). No office id is invented here.
    ///
    /// Candidates are living, off-map, adult relatives (age 18+). A
    /// candidate who is already on map, deceased, or a child is never
    /// offered. Plain C#, no Unity dependency.
    /// </summary>
    public enum RecruitmentLeadKind
    {
        Unspecified = 0,
        MoveToTown = 1,
        WorkAtBusiness = 2,
    }

    public sealed class RecruitmentLead
    {
        public int ContactPersonId = -1;
        public string ContactName = string.Empty;
        public int RecommenderPersonId = -1;
        public string RecommenderName = string.Empty;
        public RecruitmentLeadKind Kind;
        public string BusinessId = string.Empty; // WorkAtBusiness only
        public int DayIndex;
    }

    public sealed class RecruitmentLetterRequest
    {
        public int SenderPersonId = -1;
        public string SenderName = string.Empty;
        public int RecipientPersonId = -1;
        public string RecipientName = string.Empty;
        public string BusinessId = string.Empty; // WorkAtBusiness only
        public string Origin = "general-store"; // post goes out from the General Store
        public string Purpose = string.Empty; // "move-to-town" or "work-at-business"
        public int DayIndex;
    }

    public static class RecruitmentConversation
    {
        public static List<RecruitmentLead> AskAboutMovers(
            int speakerId, PopulationState population, KinshipRegistry kinship,
            ConversationChannelKind channel, int dayIndex, List<string> diagnostics)
        {
            return Ask(speakerId, population, kinship, channel, RecruitmentLeadKind.MoveToTown,
                businessId: string.Empty, isPlayerOwnedBusiness: false, dayIndex, diagnostics);
        }

        public static List<RecruitmentLead> AskAboutWorkers(
            int speakerId, PopulationState population, KinshipRegistry kinship,
            ConversationChannelKind channel, string businessId, bool isPlayerOwnedBusiness,
            int dayIndex, List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(businessId))
            {
                (diagnostics ??= new List<string>()).Add("RecruitmentConversation: work question needs a business id.");
                return new List<RecruitmentLead>();
            }
            if (!isPlayerOwnedBusiness)
            {
                (diagnostics ??= new List<string>()).Add(
                    $"RecruitmentConversation: business '{businessId}' is not player-run; the player may only recruit for a business they run.");
                return new List<RecruitmentLead>();
            }
            return Ask(speakerId, population, kinship, channel, RecruitmentLeadKind.WorkAtBusiness,
                businessId, isPlayerOwnedBusiness: true, dayIndex, diagnostics);
        }

        private static List<RecruitmentLead> Ask(
            int speakerId, PopulationState population, KinshipRegistry kinship,
            ConversationChannelKind channel, RecruitmentLeadKind kind,
            string businessId, bool isPlayerOwnedBusiness, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var leads = new List<RecruitmentLead>();
            if (channel == ConversationChannelKind.Unspecified)
            {
                diagnostics.Add("RecruitmentConversation: no legitimate conversation channel (player has not walked to them / no shared venue, workplace, household, or correspondence); conversation refused.");
                return leads;
            }
            if (population == null || population.people == null || kinship == null)
            {
                diagnostics.Add("RecruitmentConversation: null population or kinship.");
                return leads;
            }
            PersonState speaker = population.GetPerson(speakerId);
            if (speaker == null || speaker.isOffMap || speaker.deathDayIndex >= 0)
            {
                diagnostics.Add($"RecruitmentConversation: speaker P{speakerId} is not a living on-map resident.");
                return leads;
            }

            var candidateIds = new List<int>();
            candidateIds.AddRange(kinship.GetParents(speakerId));
            candidateIds.AddRange(kinship.GetChildren(speakerId));
            candidateIds.AddRange(kinship.GetRelated(speakerId, KinshipRelation.Sibling));
            candidateIds.AddRange(kinship.GetSpouses(speakerId));
            // Grandparents are also real contacts for a move question.
            foreach (int parentId in kinship.GetParents(speakerId))
                candidateIds.AddRange(kinship.GetParents(parentId));

            var seen = new HashSet<int>();
            foreach (int candidateId in candidateIds)
            {
                if (!seen.Add(candidateId) || candidateId == speakerId) continue;
                PersonState candidate = population.GetPerson(candidateId);
                if (candidate == null) continue;
                if (!candidate.isOffMap) continue; // already here, not a recruit
                if (candidate.deathDayIndex >= 0) continue;
                if (candidate.age < 18) continue; // adults only, for move or work
                leads.Add(new RecruitmentLead
                {
                    ContactPersonId = candidate.id,
                    ContactName = candidate.DisplayName,
                    RecommenderPersonId = speaker.id,
                    RecommenderName = speaker.DisplayName,
                    Kind = kind,
                    BusinessId = kind == RecruitmentLeadKind.WorkAtBusiness ? businessId : string.Empty,
                    DayIndex = dayIndex,
                });
            }

            leads.Sort((a, b) => a.ContactPersonId.CompareTo(b.ContactPersonId));
            diagnostics.Add(
                $"RecruitmentConversation: P{speakerId} ({speaker.DisplayName}) via {channel} " +
                $"answered a {(kind == RecruitmentLeadKind.WorkAtBusiness ? "work" : "move")} question with {leads.Count} off-map adult relative(s). " +
                "Friends are not modelled yet (no friend graph in code); only family-tree relatives are offered.");
            return leads;
        }

        public static RecruitmentLetterRequest BuildLetterRequest(RecruitmentLead lead, int dayIndex)
        {
            if (lead == null) return null;
            return new RecruitmentLetterRequest
            {
                SenderPersonId = lead.RecommenderPersonId,
                SenderName = lead.RecommenderName,
                RecipientPersonId = lead.ContactPersonId,
                RecipientName = lead.ContactName,
                BusinessId = lead.BusinessId,
                Origin = "general-store",
                Purpose = lead.Kind == RecruitmentLeadKind.WorkAtBusiness ? "work-at-business" : "move-to-town",
                DayIndex = dayIndex,
            };
        }
    }
}
