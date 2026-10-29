using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-008, LL-PLAY-009): the source contract
    /// for the acquisition interface. Two defects from October 9:
    ///
    /// (a) Button clutter. The panel exposed the raw deal sequence
    /// (Open Inquiry, Commit Earnest, Continue Diligence repeatedly,
    /// Agree Terms, Close Acquisition) with no single "what do I do
    /// next". This model exposes exactly ONE next legitimate action
    /// (<see cref="NextLegitimateAction"/>) while keeping every real
    /// legal/financial state visible in <see cref="Stages"/>: financing,
    /// diligence, agreements, payments, and title transfer remain
    /// distinct states. Presentation is simplified; the transaction is
    /// NOT. The next-action labels mirror the manager's conversation
    /// action labels exactly (BuildConversationActionLabel), including
    /// the proof-of-funds and formal-diligence branches.
    ///
    /// (b) Information provenance. Every opportunity carries
    /// <see cref="InformationChannel"/>: how the player legitimately
    /// learned of it. The stage enums below mirror the manager's
    /// AcquisitionDealStage / AcquisitionLeadCategory values so a thin
    /// adapter can map them 1:1; this file deliberately does NOT
    /// reference the manager, so it compiles standalone. Mapping rule
    /// (audited against AcquisitionMarketManager.ResolveLeadCategory):
    /// PublicListing → PublicListing (the acquisition board itself is
    /// the public channel); RumorLead → Conversation (a rumor arrives
    /// by conversation); EstateSale → OtherEstablishedChannel (a public
    /// sale process); QuietOpportunity, DistressSale, StrategicHoldout,
    /// Unset → Unknown. An Unknown channel is flagged, NEVER upgraded
    /// to a plausible channel: the panel must show "Source not
    /// established — treat details as unverified" rather than inventing
    /// a seller, broker, or newspaper. A caller that holds a REAL
    /// established-channel record (e.g. a newspaper advertisement
    /// asset) may pass it as <c>establishedChannel</c>; that explicit
    /// record wins over the category mapping.
    ///
    /// <see cref="MarketContextLabel"/> is the listing's generation
    /// cause (owner turnover, pressure release, ...). It is market
    /// context, NOT the information channel, and the panel must label
    /// it as such — a generation cause does not explain how the player
    /// knew.
    /// </summary>
    public static class AcquisitionDealReadModel
    {
        /// <summary>Mirrors AcquisitionDealStage (manager) 1:1 by name.</summary>
        public enum DealStageKey
        {
            None = 0,
            Inquiry = 1,
            EarnestCommitted = 2,
            DiligenceComplete = 3,
            TentativeAgreement = 4,
            Closed = 5,
            Failed = 6,
        }

        /// <summary>Mirrors AcquisitionLeadCategory (manager) 1:1 by name.</summary>
        public enum LeadKindKey
        {
            Unset = 0,
            PublicListing = 1,
            QuietOpportunity = 2,
            RumorLead = 3,
            EstateSale = 4,
            DistressSale = 5,
            StrategicHoldout = 6,
        }

        /// <summary>Legitimate information channels (brief §5, LL-PLAY-009).</summary>
        public enum InformationChannel
        {
            Unknown = 0,
            Seller = 1,
            PublicListing = 2,
            Newspaper = 3,
            Advertisement = 4,
            Broker = 5,
            Conversation = 6,
            OtherEstablishedChannel = 7,
        }

        /// <summary>One real deal state, with its presentation state.</summary>
        [Serializable]
        public sealed class StageRow
        {
            public DealStageKey Stage;
            public string Label = string.Empty;
            /// <summary>Complete, Current, Upcoming, or Failed.</summary>
            public string State = string.Empty;
        }

        [Serializable]
        public sealed class Model
        {
            public string ListingId = string.Empty;
            public string Title = string.Empty;
            public string KindLabel = string.Empty;
            public DealStageKey Stage;
            public string StageLabel = string.Empty;
            public string FailedFromStageLabel = string.Empty;
            /// <summary>The single next legitimate action, or a terminal state.</summary>
            public string NextLegitimateAction = string.Empty;
            /// <summary>Why that action is next (blocker detail, or empty).</summary>
            public string NextActionDetail = string.Empty;
            public List<StageRow> Stages = new List<StageRow>();
            public InformationChannel Channel;
            public string ChannelDisplay = string.Empty;
            public bool IsChannelKnown;
            /// <summary>Market context (generation cause) — never presented as the channel.</summary>
            public string MarketContextLabel = string.Empty;
            /// <summary>Accessible deal details (price, earnest, diligence layers, financing).</summary>
            public List<string> DealDetails = new List<string>();
        }

        /// <summary>
        /// The next legitimate action for a stage. Mirrors the manager's
        /// conversation action labels, including the proof-of-funds and
        /// formal-diligence branches and the failed-deal reopen rule.
        /// </summary>
        public static string NextActionForStage(
            DealStageKey stage,
            bool needsProofOfFunds,
            bool formalDiligenceComplete,
            bool failedReadyToReopen)
        {
            switch (stage)
            {
                case DealStageKey.None: return "Open Inquiry";
                case DealStageKey.Failed: return failedReadyToReopen ? "Reopen Inquiry" : "Review Failure";
                case DealStageKey.Inquiry: return needsProofOfFunds ? "Present Proof of Funds" : "Commit Earnest";
                case DealStageKey.EarnestCommitted: return formalDiligenceComplete ? "Agree Terms" : "Continue Diligence";
                case DealStageKey.DiligenceComplete: return "Agree Terms";
                case DealStageKey.TentativeAgreement: return "Close Acquisition";
                case DealStageKey.Closed: return "Closed";
                default: return "Open Inquiry";
            }
        }

        /// <summary>Display label for a stage (manager's FormatDealStage equivalents).</summary>
        public static string StageLabelFor(DealStageKey stage)
        {
            switch (stage)
            {
                case DealStageKey.None: return "No deal";
                case DealStageKey.Inquiry: return "Inquiry";
                case DealStageKey.EarnestCommitted: return "Earnest committed";
                case DealStageKey.DiligenceComplete: return "Diligence complete";
                case DealStageKey.TentativeAgreement: return "Terms agreed";
                case DealStageKey.Closed: return "Closed";
                case DealStageKey.Failed: return "Failed";
                default: return "No deal";
            }
        }

        /// <summary>
        /// Resolves the information channel. An explicit established-channel
        /// record (Newspaper, Advertisement, Broker, Seller, Conversation,
        /// or a named other channel) always wins; otherwise the lead
        /// category decides, and anything unrecorded stays Unknown.
        /// </summary>
        public static InformationChannel ResolveChannel(
            LeadKindKey leadKind,
            string establishedChannel,
            out string display,
            out bool known)
        {
            if (!string.IsNullOrWhiteSpace(establishedChannel))
            {
                string normalized = establishedChannel.Trim();
                string lower = normalized.ToLowerInvariant();
                InformationChannel channel = InformationChannel.OtherEstablishedChannel;
                if (lower.Contains("newspaper")) channel = InformationChannel.Newspaper;
                else if (lower.Contains("advert")) channel = InformationChannel.Advertisement;
                else if (lower.Contains("broker")) channel = InformationChannel.Broker;
                else if (lower.Contains("seller")) channel = InformationChannel.Seller;
                else if (lower.Contains("conversation") || lower.Contains("rumor") || lower.Contains("rumour")) channel = InformationChannel.Conversation;
                display = normalized;
                known = true;
                return channel;
            }

            switch (leadKind)
            {
                case LeadKindKey.PublicListing:
                    display = "Public listing (acquisition board)";
                    known = true;
                    return InformationChannel.PublicListing;
                case LeadKindKey.RumorLead:
                    display = "Rumor / conversation";
                    known = true;
                    return InformationChannel.Conversation;
                case LeadKindKey.EstateSale:
                    display = "Estate sale (public sale process)";
                    known = true;
                    return InformationChannel.OtherEstablishedChannel;
                default:
                    display = "Source not established — treat details as unverified";
                    known = false;
                    return InformationChannel.Unknown;
            }
        }

        /// <summary>Market-context label for a listing generation cause (never a channel).</summary>
        public static string MarketContextFor(string listingSourceLabel)
        {
            if (string.IsNullOrWhiteSpace(listingSourceLabel)) return "Market context: not recorded";
            return "Market context: " + listingSourceLabel.Trim();
        }

        /// <summary>Builds the full model. Stages are always all five real states in order.</summary>
        public static Model Build(
            string listingId,
            string title,
            string kindLabel,
            DealStageKey stage,
            DealStageKey failedFromStage,
            bool needsProofOfFunds,
            bool formalDiligenceComplete,
            bool failedReadyToReopen,
            LeadKindKey leadKind,
            string establishedChannel,
            string listingSourceLabel,
            IReadOnlyList<string> dealDetails,
            string nextActionDetail)
        {
            var model = new Model
            {
                ListingId = listingId ?? string.Empty,
                Title = title ?? string.Empty,
                KindLabel = kindLabel ?? string.Empty,
                Stage = stage,
                StageLabel = StageLabelFor(stage),
                FailedFromStageLabel = stage == DealStageKey.Failed ? StageLabelFor(failedFromStage) : string.Empty,
                NextLegitimateAction = NextActionForStage(stage, needsProofOfFunds, formalDiligenceComplete, failedReadyToReopen),
                NextActionDetail = nextActionDetail ?? string.Empty,
                MarketContextLabel = MarketContextFor(listingSourceLabel),
            };
            model.Channel = ResolveChannel(leadKind, establishedChannel, out string display, out bool known);
            model.ChannelDisplay = display;
            model.IsChannelKnown = known;

            DealStageKey[] order =
            {
                DealStageKey.Inquiry, DealStageKey.EarnestCommitted,
                DealStageKey.DiligenceComplete, DealStageKey.TentativeAgreement,
                DealStageKey.Closed,
            };
            int currentIndex = -1;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == stage) currentIndex = i;
            }
            // Pass XII Worker F (D15): a Failed deal must not mark
            // stages it never reached as "Failed". Stages before the
            // failure stage are Complete, the failure stage itself is
            // Failed, and later stages are Upcoming (unreached). An
            // unknown failure stage falls back to Inquiry (the first
            // stage) so the deal still shows exactly one Failed row.
            int failedFromIndex = -1;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == failedFromStage) failedFromIndex = i;
            }
            if (stage == DealStageKey.Failed && failedFromIndex < 0) failedFromIndex = 0;
            for (int i = 0; i < order.Length; i++)
            {
                string state;
                if (stage == DealStageKey.Failed) state = i < failedFromIndex ? "Complete" : i == failedFromIndex ? "Failed" : "Upcoming";
                else if (stage == DealStageKey.Closed) state = "Complete";
                else if (currentIndex < 0) state = "Upcoming";
                else if (i < currentIndex) state = "Complete";
                else if (i == currentIndex) state = "Current";
                else state = "Upcoming";
                model.Stages.Add(new StageRow { Stage = order[i], Label = StageLabelFor(order[i]), State = state });
            }

            if (dealDetails != null)
            {
                foreach (string detail in dealDetails)
                {
                    if (!string.IsNullOrWhiteSpace(detail)) model.DealDetails.Add(detail);
                }
            }
            return model;
        }
    }
}
