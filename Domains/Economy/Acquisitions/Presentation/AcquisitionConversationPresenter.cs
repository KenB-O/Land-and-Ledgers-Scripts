using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{

    public static class AcquisitionConversationPresenter
    {
        // Retain the old entry-point name for UI callers already wired to this presenter.
        // The state built here is no longer placeholder copy; it is the consolidated workflow read for the file.
        public static AcquisitionConversationState BuildPlaceholderState(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            string formalActionLabel,
            string sellerMotiveLabel,
            string sellerPressureLabel,
            string buyerSeriousnessLabel,
            string readinessSummary,
            string leadQualitySummary = "",
            string proofOfFundsSummary = "",
            string diligenceSummary = "",
            string closingRiskSummary = "",
            string timingSummary = "",
            string nextStepSummary = "",
            string decisionSummary = "",
            string checklistSummary = "",
            string leadAccessSummary = "")
        {
            if (listing == null)
            {
                return null;
            }

            AcquisitionDealStage stage = deal != null ? deal.stage : AcquisitionDealStage.None;
            AcquisitionDealStage failedFromStage = deal != null ? deal.failedFromStage : AcquisitionDealStage.None;
            string actionLabel = string.IsNullOrWhiteSpace(formalActionLabel) ? "Open Inquiry" : formalActionLabel.Trim();
            string motive = string.IsNullOrWhiteSpace(sellerMotiveLabel) ? "Motive not opened yet." : sellerMotiveLabel.Trim();
            string pressure = string.IsNullOrWhiteSpace(sellerPressureLabel) ? "Pressure not opened yet." : sellerPressureLabel.Trim();
            string seriousness = string.IsNullOrWhiteSpace(buyerSeriousnessLabel) ? "exploring" : buyerSeriousnessLabel.Trim();
            string readiness = string.IsNullOrWhiteSpace(readinessSummary) ? "Funding readiness has not been read yet." : readinessSummary.Trim();
            string leadQuality = string.IsNullOrWhiteSpace(leadQualitySummary) ? "Lead quality has not been read yet." : leadQualitySummary.Trim();
            string proof = string.IsNullOrWhiteSpace(proofOfFundsSummary) ? "Proof position not yet established." : proofOfFundsSummary.Trim();
            string diligence = string.IsNullOrWhiteSpace(diligenceSummary) ? "Diligence has not been layered yet." : diligenceSummary.Trim();
            string closingRisk = string.IsNullOrWhiteSpace(closingRiskSummary) ? "Closing risk not yet established." : closingRiskSummary.Trim();
            string timing = string.IsNullOrWhiteSpace(timingSummary) ? "Timing: no live clock." : timingSummary.Trim();
            string nextStep = string.IsNullOrWhiteSpace(nextStepSummary) ? "review the file before committing more time" : nextStepSummary.Trim();
            string decision = string.IsNullOrWhiteSpace(decisionSummary) ? "Decision: buyer posture not yet framed." : decisionSummary.Trim();
            string checklist = string.IsNullOrWhiteSpace(checklistSummary) ? "Checklist: review the file before spending more diligence time." : checklistSummary.Trim();
            string leadAccess = string.IsNullOrWhiteSpace(leadAccessSummary) ? "Access: lead access has not been read yet." : leadAccessSummary.Trim();
            string failureBreakpointLabel = BuildFailureBreakpointLabel(failedFromStage);

            AcquisitionConversationState state = new()
            {
                ListingId = listing.listingId ?? string.Empty,
                Kind = listing.kind,
                Title = listing.title ?? "Acquisition Meeting",
                SellerDisplayName = BuildSellerDisplayName(listing),
                Stage = stage,
                FailedFromStage = failedFromStage,
                StageLabel = BuildStageLabel(stage, failedFromStage),
                FailureBreakpointLabel = failureBreakpointLabel,
                FormalActionLabel = actionLabel,
                CanAdvanceDeal = stage != AcquisitionDealStage.Closed,
                SellerPosture = BuildPlaceholderPosture(stage, sellerPressureLabel, sellerMotiveLabel),
                SellerMotiveLabel = motive,
                SellerPressureLabel = pressure,
                BuyerSeriousnessLabel = seriousness,
                OpeningLine = BuildOpeningLine(listing, stage, seriousness, failureBreakpointLabel),
                SellerLine = BuildSellerLine(listing, stage, motive, pressure),
                ProcessSummary = BuildProcessSummary(stage, failedFromStage, actionLabel, leadQuality, proof, diligence, closingRisk, timing, nextStep, leadAccess),
                DialoguePrompt = BuildDialoguePrompt(stage, listing.kind, failureBreakpointLabel),
                PostureSummary = BuildPostureSummary(motive, pressure, seriousness),
                ReadinessSummary = readiness,
                LeadQualitySummary = leadQuality,
                ProofOfFundsSummary = proof,
                DiligenceSummary = diligence,
                ClosingRiskSummary = closingRisk,
                TimingSummary = timing,
                NextStepSummary = nextStep,
                DecisionSummary = decision,
                ChecklistSummary = checklist,
                LeadAccessSummary = leadAccess,
                FollowUpSummary = BuildFollowUpSummary(stage, failedFromStage, proof, diligence, closingRisk, timing, nextStep, decision, leadAccess)
            };

            AddProcessSteps(state);
            AddOptions(state, listing.kind);
            return state;
        }

        private static string BuildSellerDisplayName(AcquisitionListing listing)
        {
            if (!string.IsNullOrWhiteSpace(listing.ownerDisplayName))
            {
                return listing.ownerDisplayName;
            }

            return listing.kind == AcquisitionListingKind.Business ? "Business seller" : "Land seller";
        }

        private static string BuildStageLabel(AcquisitionDealStage stage, AcquisitionDealStage failedFromStage)
        {
            return stage switch
            {
                AcquisitionDealStage.Inquiry => "Inquiry",
                AcquisitionDealStage.EarnestCommitted => "Earnest committed",
                AcquisitionDealStage.DiligenceComplete => "Diligence complete",
                AcquisitionDealStage.TentativeAgreement => "Tentative agreement",
                AcquisitionDealStage.Closed => "Closed",
                AcquisitionDealStage.Failed => string.IsNullOrWhiteSpace(BuildFailureBreakpointLabel(failedFromStage))
                    ? "Failed"
                    : $"Failed during {BuildFailureBreakpointLabel(failedFromStage).ToLowerInvariant()}",
                _ => "Not contacted"
            };
        }

        private static string BuildFailureBreakpointLabel(AcquisitionDealStage failedFromStage)
        {
            return failedFromStage switch
            {
                AcquisitionDealStage.Inquiry => "Inquiry",
                AcquisitionDealStage.EarnestCommitted => "Diligence",
                AcquisitionDealStage.DiligenceComplete => "Terms",
                AcquisitionDealStage.TentativeAgreement => "Closing",
                _ => string.Empty
            };
        }

        private static AcquisitionSellerPosture BuildPlaceholderPosture(
            AcquisitionDealStage stage,
            string sellerPressureLabel,
            string sellerMotiveLabel)
        {
            if (stage == AcquisitionDealStage.TentativeAgreement || stage == AcquisitionDealStage.Closed)
            {
                return AcquisitionSellerPosture.Formal;
            }

            if (!string.IsNullOrWhiteSpace(sellerPressureLabel)
                && sellerPressureLabel.IndexOf("pressure", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AcquisitionSellerPosture.Pressured;
            }

            if (!string.IsNullOrWhiteSpace(sellerMotiveLabel)
                && (sellerMotiveLabel.IndexOf("estate", StringComparison.OrdinalIgnoreCase) >= 0
                    || sellerMotiveLabel.IndexOf("debt", StringComparison.OrdinalIgnoreCase) >= 0
                    || sellerMotiveLabel.IndexOf("retire", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return AcquisitionSellerPosture.Pressured;
            }

            return stage == AcquisitionDealStage.None
                ? AcquisitionSellerPosture.Guarded
                : AcquisitionSellerPosture.Open;
        }

        private static string BuildOpeningLine(AcquisitionListing listing, AcquisitionDealStage stage, string seriousness, string failureBreakpointLabel)
        {
            string subject = listing.kind == AcquisitionListingKind.Business ? "business file" : "land file";
            return stage switch
            {
                AcquisitionDealStage.None => $"This first contact stays on grounded deal facts for the {subject}: motive, timing, proof, and whether the lead deserves serious time. Buyer posture currently reads {seriousness}.",
                AcquisitionDealStage.Inquiry => $"The inquiry is open. Keep the conversation on proof, earnest discipline, and whether the {subject} should advance into a protected file.",
                AcquisitionDealStage.EarnestCommitted => $"Earnest now holds the lane. The conversation should narrow to diligence exposure, value, and whether this {subject} is truly worth terms.",
                AcquisitionDealStage.DiligenceComplete => $"Formal diligence is in hand. The conversation shifts toward contingencies, price discipline, and a clean route to tentative terms.",
                AcquisitionDealStage.TentativeAgreement => $"Terms are framed. The practical question is whether funding, deadlines, and final review can still carry the deal to close.",
                AcquisitionDealStage.Closed => "The deal is no longer a live courtship. The useful read now is transfer, stabilization, and what ownership needs to do next.",
                AcquisitionDealStage.Failed when !string.IsNullOrWhiteSpace(failureBreakpointLabel) => $"This file failed during {failureBreakpointLabel.ToLowerInvariant()}. Treat it as a grounded postmortem before reopening anything similar.",
                AcquisitionDealStage.Failed => "This is no longer a live advance conversation. Treat it as a grounded failure read before reopening anything similar.",
                _ => $"Review the {subject} in practical terms before committing real time or money."
            };
        }

        private static string BuildSellerLine(
            AcquisitionListing listing,
            AcquisitionDealStage stage,
            string sellerMotiveLabel,
            string sellerPressureLabel)
        {
            string subject = listing.kind == AcquisitionListingKind.Business ? "the business" : "the parcel";
            return stage switch
            {
                AcquisitionDealStage.None => $"The seller keeps the talk on {subject}, timing, and whether the buyer is worth taking seriously. {sellerMotiveLabel} {sellerPressureLabel}",
                AcquisitionDealStage.Inquiry => $"The seller wants to know whether the buyer can truly perform before giving {subject} more of the file. {sellerMotiveLabel} {sellerPressureLabel}",
                AcquisitionDealStage.EarnestCommitted => $"With earnest in place, the seller becomes more formal about what the buyer may inspect and how long the lane stays open. {sellerPressureLabel}",
                AcquisitionDealStage.DiligenceComplete => $"The seller expects diligence to turn into real terms, not drift. {sellerMotiveLabel} {sellerPressureLabel}",
                AcquisitionDealStage.TentativeAgreement => $"The seller talks like a closing counterpart now: money, paper, deadlines, and whether anything can still break the file. {sellerPressureLabel}",
                AcquisitionDealStage.Closed => $"The seller now reads as a transfer counterparty rather than a live negotiator. {sellerMotiveLabel}",
                AcquisitionDealStage.Failed => $"The seller treats the file as cooled unless the buyer can explain what changes. {sellerMotiveLabel} {sellerPressureLabel}",
                _ => $"The seller keeps the meeting on {subject}, price, timing, and the practical reasons behind the file."
            };
        }

        private static string BuildPostureSummary(
            string sellerMotiveLabel,
            string sellerPressureLabel,
            string buyerSeriousnessLabel)
        {
            return $"Seller posture: {sellerMotiveLabel} {sellerPressureLabel} Buyer posture: {buyerSeriousnessLabel}.";
        }

        private static string BuildProcessSummary(
            AcquisitionDealStage stage,
            AcquisitionDealStage failedFromStage,
            string formalActionLabel,
            string leadQualitySummary,
            string proofOfFundsSummary,
            string diligenceSummary,
            string closingRiskSummary,
            string timingSummary,
            string nextStepSummary,
            string leadAccessSummary)
        {
            string failureBreakpointLabel = BuildFailureBreakpointLabel(failedFromStage);
            string stageRead = stage switch
            {
                AcquisitionDealStage.None => $"Process: lead not opened. {leadQualitySummary} {leadAccessSummary} Next formal step: {formalActionLabel}.",
                AcquisitionDealStage.Inquiry => $"Process: inquiry opened. {proofOfFundsSummary} {leadAccessSummary} Next formal step: {formalActionLabel}.",
                AcquisitionDealStage.EarnestCommitted => $"Process: earnest holds the lane. {diligenceSummary} Next formal step: {formalActionLabel}.",
                AcquisitionDealStage.DiligenceComplete => $"Process: diligence is complete and ready for terms. {closingRiskSummary} Next formal step: {formalActionLabel}.",
                AcquisitionDealStage.TentativeAgreement => $"Process: tentative terms are set. {closingRiskSummary} Next formal step: {formalActionLabel}.",
                AcquisitionDealStage.Closed => "Process: acquisition closed. Shift the read from winning the file to carrying the transfer cleanly.",
                AcquisitionDealStage.Failed when !string.IsNullOrWhiteSpace(failureBreakpointLabel) => $"Process: acquisition failed during {failureBreakpointLabel.ToLowerInvariant()}. {closingRiskSummary}",
                AcquisitionDealStage.Failed => $"Process: acquisition failed. {closingRiskSummary}",
                _ => $"Process: review the lead. Next formal step: {formalActionLabel}."
            };

            return stage switch
            {
                AcquisitionDealStage.Closed => stageRead,
                AcquisitionDealStage.Failed => CombineSummaries(stageRead, timingSummary),
                _ => CombineSummaries(stageRead, BuildNextStepLine(nextStepSummary), timingSummary)
            };
        }

        private static string BuildDialoguePrompt(AcquisitionDealStage stage, AcquisitionListingKind kind, string failureBreakpointLabel)
        {
            string subject = kind == AcquisitionListingKind.Business ? "business" : "land";
            return stage switch
            {
                AcquisitionDealStage.None => $"Choose a grounded first read for this {subject} lead: seller posture, funding credibility, or whether the file deserves a real inquiry.",
                AcquisitionDealStage.Inquiry => $"Choose the next practical pressure point: seller posture, proof, process discipline, diligence scope, or closing risk.",
                AcquisitionDealStage.EarnestCommitted => "Use the conversation surface to clear diligence exposure and confirm whether the file should survive into terms.",
                AcquisitionDealStage.DiligenceComplete => "Keep the conversation on terms, contingencies, and whether the closing runway is still credible.",
                AcquisitionDealStage.TentativeAgreement => "Focus on what could still break the file before close and what must happen next in order.",
                AcquisitionDealStage.Closed => "Review transfer and follow-through rather than reopening negotiation beats.",
                AcquisitionDealStage.Failed when !string.IsNullOrWhiteSpace(failureBreakpointLabel) => $"Read the {failureBreakpointLabel.ToLowerInvariant()} failure cleanly before reopening anything similar.",
                AcquisitionDealStage.Failed => "Read the failure cleanly before reopening anything similar.",
                _ => "Choose one practical line of inquiry before advancing the formal process."
            };
        }

        private static string BuildFollowUpSummary(
            AcquisitionDealStage stage,
            AcquisitionDealStage failedFromStage,
            string proofOfFundsSummary,
            string diligenceSummary,
            string closingRiskSummary,
            string timingSummary,
            string nextStepSummary,
            string decisionSummary,
            string leadAccessSummary)
        {
            string failureBreakpointLabel = BuildFailureBreakpointLabel(failedFromStage);
            string stageRead = stage switch
            {
                AcquisitionDealStage.Inquiry => $"Move only if proof and earnest posture are credible. {proofOfFundsSummary}",
                AcquisitionDealStage.EarnestCommitted => $"Finish diligence before agreeing terms. {diligenceSummary}",
                AcquisitionDealStage.DiligenceComplete => $"Convert diligence into price, contingencies, and closing runway. {closingRiskSummary}",
                AcquisitionDealStage.TentativeAgreement => $"Close only when funding and final review can still hold. {closingRiskSummary}",
                AcquisitionDealStage.Closed => "Shift attention to transfer quality, stabilization, and post-close execution.",
                AcquisitionDealStage.Failed when !string.IsNullOrWhiteSpace(failureBreakpointLabel) => $"Treat this as a failure review from the {failureBreakpointLabel.ToLowerInvariant()} breakpoint, not a live persuasion pass. {closingRiskSummary}",
                AcquisitionDealStage.Failed => $"Treat this as a failure review, not a live persuasion pass. {closingRiskSummary}",
                _ => $"Open the inquiry only when the lead merits serious time. {proofOfFundsSummary} {leadAccessSummary}"
            };

            return stage switch
            {
                AcquisitionDealStage.Closed => CombineSummaries(stageRead, decisionSummary),
                AcquisitionDealStage.Failed => CombineSummaries(stageRead, timingSummary, decisionSummary),
                _ => CombineSummaries(stageRead, BuildNextStepLine(nextStepSummary), timingSummary, decisionSummary)
            };
        }

        private static void AddProcessSteps(AcquisitionConversationState state)
        {
            if (state == null)
            {
                return;
            }

            AddProcessStep(state, BuildProcessStepLabel(state, "Inquiry", 1), ResolveProcessStatus(state.Stage, state.FailedFromStage, 1), BuildInquiryStepSummary(state));
            AddProcessStep(state, BuildProcessStepLabel(state, "Earnest", 2), ResolveProcessStatus(state.Stage, state.FailedFromStage, 2), BuildEarnestStepSummary(state));
            AddProcessStep(state, BuildProcessStepLabel(state, "Diligence", 3), ResolveProcessStatus(state.Stage, state.FailedFromStage, 3), BuildDiligenceStepSummary(state));
            AddProcessStep(state, BuildProcessStepLabel(state, "Terms", 4), ResolveProcessStatus(state.Stage, state.FailedFromStage, 4), BuildTermsStepSummary(state));
            AddProcessStep(state, BuildProcessStepLabel(state, "Close", 5), ResolveProcessStatus(state.Stage, state.FailedFromStage, 5), BuildCloseStepSummary(state));
        }

        private static string BuildProcessStepLabel(AcquisitionConversationState state, string baseLabel, int milestoneOrder)
        {
            if (state == null || state.Stage != AcquisitionDealStage.Failed)
            {
                return baseLabel;
            }

            return milestoneOrder == GetFailureBreakpointOrder(state.FailedFromStage)
                ? $"{baseLabel} (failed here)"
                : baseLabel;
        }

        private static string BuildInquiryStepSummary(AcquisitionConversationState state)
        {
            return CombineSummaries(
                "Open the file and test whether the lead deserves serious time.",
                state.LeadQualitySummary,
                state.LeadAccessSummary,
                state.PostureSummary);
        }

        private static string BuildEarnestStepSummary(AcquisitionConversationState state)
        {
            return CombineSummaries(
                "Hold the lane with money, timing discipline, and credible buyer posture.",
                state.ReadinessSummary,
                state.ProofOfFundsSummary);
        }

        private static string BuildDiligenceStepSummary(AcquisitionConversationState state)
        {
            return CombineSummaries(
                "Confirm condition, value, and operating exposure before terms.",
                state.DiligenceSummary,
                state.DecisionSummary);
        }

        private static string BuildTermsStepSummary(AcquisitionConversationState state)
        {
            return CombineSummaries(
                "Convert diligence into price, contingencies, and a practical close structure.",
                state.ClosingRiskSummary,
                BuildNextStepLine(state.NextStepSummary),
                state.TimingSummary);
        }

        private static string BuildCloseStepSummary(AcquisitionConversationState state)
        {
            return CombineSummaries(
                "Close the acquisition only when funding, paper, and timing all still hold.",
                state.FollowUpSummary,
                state.ChecklistSummary);
        }

        private static void AddProcessStep(
            AcquisitionConversationState state,
            string label,
            AcquisitionConversationProcessStatus status,
            string summary)
        {
            state.ProcessSteps.Add(new AcquisitionConversationProcessStep
            {
                Label = label,
                Status = status,
                Summary = summary
            });
        }

        private static void AddOptions(AcquisitionConversationState state, AcquisitionListingKind kind)
        {
            state.Options.Add(CreateOption(
                "seller-position",
                1,
                AcquisitionConversationOptionKind.SellerPosition,
                "Seller Position",
                "Test motive and timing",
                "Ask what matters most before this can move forward.",
                "The seller keeps returning to timing, pressure, and whether the buyer can truly perform.",
                "Posture",
                CombineSummaries(state.PostureSummary, state.LeadAccessSummary)));

            state.Options.Add(CreateOption(
                "funding-readiness",
                2,
                AcquisitionConversationOptionKind.Funding,
                "Funding",
                "Test proof position",
                "Review whether the buyer position looks credible enough to hold the lane.",
                "The seller wants confidence that earnest, terms, and closing money will not drift.",
                "Funding",
                CombineSummaries(state.ReadinessSummary, state.ProofOfFundsSummary)));

            state.Options.Add(CreateOption(
                "process-read",
                3,
                AcquisitionConversationOptionKind.Information,
                "Process",
                "Clarify the next formal step",
                "Ask what must be settled before the file should move one step deeper.",
                "The meeting narrows to what is actionable now, not abstract bargaining talk.",
                "Process",
                state.ProcessSummary));

            state.Options.Add(CreateOption(
                "diligence-plan",
                4,
                AcquisitionConversationOptionKind.Diligence,
                "Diligence",
                "Surface exposed risk",
                $"Review what still needs to be checked on this {(kind == AcquisitionListingKind.Business ? "business" : "site")} before terms.",
                "The answer stays practical: condition, books, frontage, staffing, or supplier exposure depending on the file.",
                "Diligence",
                state.DiligenceSummary));

            state.Options.Add(CreateOption(
                "closing-risk",
                5,
                AcquisitionConversationOptionKind.ClosingRisk,
                "Closing Risk",
                "Stress the break points",
                "Ask what could still kill the file even if the lead looks good on paper.",
                "The seller frames risk around timing, financing, title, and whether either side can still walk away.",
                "Risk",
                state.ClosingRiskSummary));

            state.Options.Add(CreateOption(
                "follow-up",
                6,
                AcquisitionConversationOptionKind.DealFollowUp,
                "Follow Up",
                "Frame the next grounded move",
                "Ask what has to happen next in order instead of treating the deal like open-ended talk.",
                "The discussion shifts toward follow-through: what to do now, what to defer, and when to stop spending time.",
                "Follow-up",
                CombineSummaries(state.FollowUpSummary, state.ChecklistSummary)));
        }

        private static string BuildNextStepLine(string nextStepSummary)
        {
            if (string.IsNullOrWhiteSpace(nextStepSummary))
            {
                return string.Empty;
            }

            string trimmed = nextStepSummary.Trim();
            if (trimmed.StartsWith("Next step:", StringComparison.OrdinalIgnoreCase))
            {
                return EnsureSentenceTerminator(trimmed);
            }

            return $"Next step: {EnsureSentenceTerminator(trimmed)}";
        }

        private static string EnsureSentenceTerminator(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (trimmed.EndsWith(".", StringComparison.Ordinal)
                || trimmed.EndsWith("!", StringComparison.Ordinal)
                || trimmed.EndsWith("?", StringComparison.Ordinal))
            {
                return trimmed;
            }

            return $"{trimmed}.";
        }

        private static AcquisitionConversationOption CreateOption(
            string optionId,
            int shortcutNumber,
            AcquisitionConversationOptionKind kind,
            string label,
            string intentText,
            string buyerLine,
            string sellerReplyText,
            string outcomeTag,
            string responseText)
        {
            return new AcquisitionConversationOption
            {
                OptionId = optionId,
                ShortcutLabel = shortcutNumber.ToString(),
                Kind = kind,
                Label = label,
                IntentText = intentText,
                BuyerLine = buyerLine,
                SellerReplyText = sellerReplyText,
                OutcomeTag = outcomeTag,
                ResponseText = responseText
            };
        }

        private static string CombineSummaries(params string[] parts)
        {
            if (parts == null || parts.Length <= 0)
            {
                return string.Empty;
            }

            string combined = string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                combined = string.IsNullOrWhiteSpace(combined)
                    ? part.Trim()
                    : $"{combined} {part.Trim()}";
            }

            return combined;
        }

        private static int GetFailureBreakpointOrder(AcquisitionDealStage failedFromStage)
        {
            return failedFromStage switch
            {
                AcquisitionDealStage.Inquiry => 1,
                AcquisitionDealStage.EarnestCommitted => 3,
                AcquisitionDealStage.DiligenceComplete => 4,
                AcquisitionDealStage.TentativeAgreement => 5,
                _ => 1
            };
        }

        private static AcquisitionConversationProcessStatus ResolveProcessStatus(
            AcquisitionDealStage stage,
            AcquisitionDealStage failedFromStage,
            int milestoneOrder)
        {
            int currentOrder = stage switch
            {
                AcquisitionDealStage.None => 1,
                AcquisitionDealStage.Inquiry => 2,
                AcquisitionDealStage.EarnestCommitted => 3,
                AcquisitionDealStage.DiligenceComplete => 4,
                AcquisitionDealStage.TentativeAgreement => 5,
                AcquisitionDealStage.Closed => 6,
                _ => 1
            };

            if (stage == AcquisitionDealStage.Closed)
            {
                return AcquisitionConversationProcessStatus.Completed;
            }

            if (stage == AcquisitionDealStage.Failed)
            {
                int failureOrder = GetFailureBreakpointOrder(failedFromStage);
                if (milestoneOrder < failureOrder)
                {
                    return AcquisitionConversationProcessStatus.Completed;
                }

                return milestoneOrder == failureOrder
                    ? AcquisitionConversationProcessStatus.Current
                    : AcquisitionConversationProcessStatus.Upcoming;
            }

            if (milestoneOrder < currentOrder)
            {
                return AcquisitionConversationProcessStatus.Completed;
            }

            return milestoneOrder == currentOrder
                ? AcquisitionConversationProcessStatus.Current
                : AcquisitionConversationProcessStatus.Upcoming;
        }
    }
}
