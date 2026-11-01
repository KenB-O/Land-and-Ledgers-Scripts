using System;
using System.Collections.Generic;

namespace LandLedgers.Population.TownGrowth
{
    /// <summary>
    /// Pass 5 (town growth): conversation chain MODEL for role-based
    /// recruitment. This extends the family-only recruitment system
    /// with job context and addresses; it does NOT replace it yet,
    /// and it does NOT build a friend graph.
    ///
    /// Chain, in order, with no step skippable:
    ///   AskWorkers -> BuildLetterRequest -> PostLetter ->
    ///   ReplyPending -> ReplyReceived / Negotiate / TravelToInspect
    ///   / Declined / Ignored -> ArrivalSettlement -> Settled
    /// Letter posting itself belongs to RecruitmentLetterPoster
    /// (registration already exists in production - this model does
    /// NOT re-register the General Store office). Settlement
    /// belongs to ArrivalSettlementDriver.SettleAndApply (see
    /// Pass5SettlementReadinessModel for its gates). This model is
    /// the caller, reply, and arrival glue between those existing
    /// pieces; it claims no wiring, no UI, and no production caller.
    ///
    /// Dialogue model: the question is "do you know anyone who
    /// would like to work at [business] as [role]?" Role comes from
    /// the business's real roles (caller-supplied list), never
    /// free text. The speaker must have a legitimate conversation
    /// channel (caller flag, mirroring ConversationOpportunity:
    /// shared venue, workplace, household, or delivered
    /// correspondence) and the business must be player-run (caller
    /// flag, mirroring RecruitmentConversation). This model does
    /// not fake the walk to the person.
    ///
    /// Friend graph: no friend graph exists. A Friend source is
    /// refused unless the caller supplies friend edges, each with
    /// non-empty SharedHistoryEvidence (shared venue, workplace,
    /// household, or correspondence history). Edges without that
    /// evidence are refused, never generated. FamilyOnly is the
    /// default and the honest bound until such edges are authored.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public enum Pass5ContactSource
    {
        FamilyOnly = 0,
        FriendEdgesSupplied = 1,
    }

    public sealed class Pass5FriendEdge
    {
        public int PersonId = -1;
        public int FriendPersonId = -1;
        public string SharedHistoryEvidence = string.Empty; // required, never empty
    }

    public enum Pass5ConversationStep
    {
        AskWorkers = 0,
        BuildLetterRequest = 1,
        PostLetter = 2,
        ReplyPending = 3,
        ReplyReceived = 4,
        Negotiate = 5,
        TravelToInspect = 6,
        Declined = 7,
        Ignored = 8,
        ArrivalSettlement = 9,
        Settled = 10,
        Refused = 11,
    }

    public sealed class Pass5DialogueQuestion
    {
        public int SpeakerPersonId = -1;
        public string BusinessId = string.Empty;
        public string RoleId = string.Empty;
        public string RoleDisplayName = string.Empty;
        public bool IsPlayerOwnedBusiness;
        public bool HasLegitimateChannel;
        public Pass5ContactSource ContactSource = Pass5ContactSource.FamilyOnly;
    }

    public sealed class Pass5ChainRecord
    {
        public int LeadContactPersonId = -1;
        public Pass5ConversationStep Step = Pass5ConversationStep.AskWorkers;
        public int DayIndex;
        public bool PostagePaid;
        public string LetterOrigin = string.Empty;
        public string PostalDestination = string.Empty; // enriched lead location, or "offmap"
        public List<string> Reasons = new List<string>();
    }

    public static class Pass5ConversationChain
    {
        public static bool ValidateQuestion(
            Pass5DialogueQuestion question, List<string> realBusinessRoles, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (question == null)
            {
                diagnostics.Add("Pass5Conversation: no dialogue question supplied; refused.");
                return false;
            }
            if (!question.IsPlayerOwnedBusiness)
            {
                diagnostics.Add("Pass5Conversation: business '" + question.BusinessId + "' is not player-run; the player may only recruit for a business they run.");
                return false;
            }
            if (!question.HasLegitimateChannel)
            {
                diagnostics.Add("Pass5Conversation: no legitimate conversation channel (player has not walked to them / no shared venue, workplace, household, or correspondence); conversation refused.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(question.RoleId) || string.IsNullOrWhiteSpace(question.RoleDisplayName))
            {
                diagnostics.Add("Pass5Conversation: role question needs a real business role id and display name; role must not be free-text invented.");
                return false;
            }
            if (realBusinessRoles == null || !realBusinessRoles.Contains(question.RoleId))
            {
                diagnostics.Add("Pass5Conversation: role '" + question.RoleId + "' is not in the business's real roles; refused, never invented.");
                return false;
            }
            return true;
        }

        public static List<Pass5FriendEdge> ValidateFriendEdges(
            List<Pass5FriendEdge> edges, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var accepted = new List<Pass5FriendEdge>();
            if (edges == null) return accepted;
            foreach (var edge in edges)
            {
                if (edge == null) continue;
                if (string.IsNullOrWhiteSpace(edge.SharedHistoryEvidence))
                {
                    diagnostics.Add("Pass5Conversation: friend edge " + edge.PersonId + "->" + edge.FriendPersonId + " refused - no shared-history evidence; friends are never generated.");
                    continue;
                }
                accepted.Add(edge);
            }
            return accepted;
        }

        /// <summary>
        /// Advances the chain by exactly one legal step. Skipping
        /// is refused: no settlement without a reply/arrival, no
        /// posting without a letter request, no letter without an
        /// ask. Postage must be paid before PostLetter is accepted
        /// (mirroring RecruitmentLetterPoster: postage debited
        /// before acceptance). Settled is terminal and requires
        /// ArrivalSettlement readiness (checked by the caller via
        /// Pass5SettlementReadinessModel, not re-checked here).
        /// </summary>
        public static Pass5ChainRecord Advance(Pass5ChainRecord record, Pass5ConversationStep next, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (record == null)
            {
                diagnostics.Add("Pass5Conversation: no chain record supplied; refused.");
                return new Pass5ChainRecord { Step = Pass5ConversationStep.Refused };
            }
            bool legal = false;
            switch (record.Step)
            {
                case Pass5ConversationStep.AskWorkers:
                    legal = next == Pass5ConversationStep.BuildLetterRequest || next == Pass5ConversationStep.Refused;
                    break;
                case Pass5ConversationStep.BuildLetterRequest:
                    legal = next == Pass5ConversationStep.PostLetter || next == Pass5ConversationStep.Refused;
                    break;
                case Pass5ConversationStep.PostLetter:
                    legal = next == Pass5ConversationStep.ReplyPending || next == Pass5ConversationStep.Refused;
                    break;
                case Pass5ConversationStep.ReplyPending:
                    legal = next == Pass5ConversationStep.ReplyReceived
                        || next == Pass5ConversationStep.Negotiate
                        || next == Pass5ConversationStep.TravelToInspect
                        || next == Pass5ConversationStep.Declined
                        || next == Pass5ConversationStep.Ignored
                        || next == Pass5ConversationStep.Refused;
                    break;
                case Pass5ConversationStep.ReplyReceived:
                case Pass5ConversationStep.Negotiate:
                case Pass5ConversationStep.TravelToInspect:
                    legal = next == Pass5ConversationStep.ArrivalSettlement || next == Pass5ConversationStep.Refused;
                    break;
                case Pass5ConversationStep.ArrivalSettlement:
                    legal = next == Pass5ConversationStep.Settled || next == Pass5ConversationStep.Refused;
                    break;
                default:
                    legal = false;
                    break;
            }
            if (!legal)
            {
                diagnostics.Add("Pass5Conversation: illegal chain step " + record.Step + " -> " + next + "; chain refused, step unchanged.");
                record.Reasons.Add("Illegal step refused: " + record.Step + " -> " + next);
                return record;
            }
            if (next == Pass5ConversationStep.PostLetter && !record.PostagePaid)
            {
                diagnostics.Add("Pass5Conversation: letter posting refused - postage must be paid before acceptance, never free carriage.");
                record.Reasons.Add("Postage unpaid; posting refused.");
                return record;
            }
            if (next == Pass5ConversationStep.PostLetter && string.IsNullOrWhiteSpace(record.LetterOrigin))
            {
                // Existing origin is "general-store"; a caller that
                // does not supply an origin has not registered one.
                diagnostics.Add("Pass5Conversation: letter posting refused - no registered origin supplied (existing origin is 'general-store').");
                record.Reasons.Add("No letter origin; posting refused.");
                return record;
            }
            record.Step = next;
            return record;
        }
    }
}
