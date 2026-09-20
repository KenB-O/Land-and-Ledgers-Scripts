using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Rivals
{
    public sealed class MarketPressureEvaluator
    {
        private const float WatchDemandWeight01 = 0.62f;
        private const float DeferDemandWeight01 = 0.24f;
        private const float LocalOverlapContribution01 = 0.35f;
        private const float WatchOwnerRelevanceWeight01 = 0.72f;
        private const float DeferOwnerRelevanceWeight01 = 0.4f;
        private const float OwnerRelevanceScoreFloor01 = 0.65f;

        public MarketPressureSnapshot Evaluate(
            string marketId,
            RivalOpportunityKind kind,
            string categoryId,
            IReadOnlyList<RivalDecisionProposal> proposals,
            IReadOnlyList<RivalOwnerScaleSnapshot> scaleSnapshots)
        {
            float landDemand = 0f;
            float businessDemand = 0f;
            float expansionDemand = 0f;
            float bidCompetition = 0f;
            float activeRivalOverlap = 0f;
            int pressureContributingProposalCount = 0;
            int actionableProposalCount = 0;
            int speculativeProposalCount = 0;
            int committedBidCount = 0;

            // Market relevance and scale overlap are related but not identical. Small rivals with live interest
            // should count as market participants even when they are not large enough to sharpen major-rival overlap.
            HashSet<string> relevantOwnerIds = null;
            bool hasAnonymousRelevantRival = false;
            int activeOverlapContributorCount = 0;
            Dictionary<string, float> relevantOwnerWeights = null;

            if (proposals != null)
            {
                relevantOwnerWeights = new Dictionary<string, float>(StringComparer.Ordinal);
                relevantOwnerIds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < proposals.Count; i++)
                {
                    RivalDecisionProposal proposal = proposals[i];
                    if (!Matches(proposal.Opportunity, kind, categoryId) || !proposal.HasLiveCompetitiveInterest)
                    {
                        continue;
                    }

                    pressureContributingProposalCount++;
                    if (proposal.IsActionable)
                    {
                        actionableProposalCount++;
                    }
                    else if (proposal.IsSpeculativeSignal)
                    {
                        speculativeProposalCount++;
                    }

                    if (proposal.IsCommittedAction)
                    {
                        committedBidCount++;
                    }

                    float demand = proposal.Score01 * ActionPressureMultiplier(proposal.Action);
                    switch (proposal.Opportunity.kind)
                    {
                        case RivalOpportunityKind.Land:
                            landDemand += demand;
                            break;
                        case RivalOpportunityKind.BusinessAcquisition:
                            businessDemand += demand;
                            break;
                        case RivalOpportunityKind.BusinessExpansion:
                        case RivalOpportunityKind.MarketPosition:
                            expansionDemand += demand;
                            break;
                    }

                    if (proposal.IsCommittedAction)
                    {
                        bidCompetition += demand;
                    }

                    if (string.IsNullOrEmpty(proposal.OwnerId))
                    {
                        hasAnonymousRelevantRival = true;
                    }
                    else
                    {
                        relevantOwnerIds.Add(proposal.OwnerId);
                        float ownerWeight = OwnerMarketRelevanceWeight(proposal);
                        if (relevantOwnerWeights.TryGetValue(proposal.OwnerId, out float existingWeight))
                        {
                            relevantOwnerWeights[proposal.OwnerId] = Mathf.Max(existingWeight, ownerWeight);
                        }
                        else
                        {
                            relevantOwnerWeights.Add(proposal.OwnerId, ownerWeight);
                        }
                    }
                }
            }

            if (scaleSnapshots != null && relevantOwnerWeights != null && relevantOwnerWeights.Count > 0)
            {
                for (int i = 0; i < scaleSnapshots.Count; i++)
                {
                    RivalOwnerScaleSnapshot snapshot = scaleSnapshots[i];
                    if (!snapshot.IsActiveCompetitiveThreat)
                    {
                        continue;
                    }

                    if (!relevantOwnerWeights.TryGetValue(snapshot.OwnerId, out float ownerWeight))
                    {
                        continue;
                    }

                    activeRivalOverlap += snapshot.ThreatToPlayer01 * ownerWeight;
                    activeOverlapContributorCount++;
                }
            }

            int relevantRivalCount = (relevantOwnerIds?.Count ?? 0) + (hasAnonymousRelevantRival ? 1 : 0);
            float proposalDivisor = Mathf.Max(1f, pressureContributingProposalCount);
            float rivalDivisor = Mathf.Max(1f, activeOverlapContributorCount);
            float landPressure = SaturatePressure(landDemand / proposalDivisor);
            float businessPressure = SaturatePressure(businessDemand / proposalDivisor);
            float expansionPressure = SaturatePressure(expansionDemand / proposalDivisor);
            float bidPressure = SaturatePressure(bidCompetition / proposalDivisor);
            float overlapPressure = SaturatePressure(activeRivalOverlap / rivalDivisor);
            float localCompetition = SaturatePressure(
                (landDemand + businessDemand + expansionDemand) / proposalDivisor
                + overlapPressure * LocalOverlapContribution01);

            List<MarketPressureReasonCode> reasons = new List<MarketPressureReasonCode>(5);
            if (RivalEvaluationUtility.IsMaterialPressure(landPressure))
            {
                reasons.Add(MarketPressureReasonCode.RivalLandDemand);
            }

            if (RivalEvaluationUtility.IsMaterialPressure(businessPressure))
            {
                reasons.Add(MarketPressureReasonCode.RivalBusinessAcquisitionDemand);
            }

            if (RivalEvaluationUtility.IsMaterialPressure(expansionPressure))
            {
                reasons.Add(MarketPressureReasonCode.RivalExpansionDemand);
            }

            if (RivalEvaluationUtility.IsMaterialPressure(bidPressure))
            {
                reasons.Add(MarketPressureReasonCode.RivalBidCompetition);
            }

            if (RivalEvaluationUtility.IsMaterialPressure(overlapPressure))
            {
                reasons.Add(MarketPressureReasonCode.MajorRivalOverlap);
            }

            return new MarketPressureSnapshot(
                marketId,
                kind,
                categoryId,
                landPressure,
                businessPressure,
                expansionPressure,
                localCompetition,
                bidPressure,
                pressureContributingProposalCount,
                actionableProposalCount,
                speculativeProposalCount,
                committedBidCount,
                relevantRivalCount,
                reasons);
        }

        private static bool Matches(RivalOpportunitySignal opportunity, RivalOpportunityKind kind, string categoryId)
        {
            if (kind != RivalOpportunityKind.Unknown && opportunity.kind != kind)
            {
                return false;
            }

            return string.IsNullOrEmpty(categoryId) || string.Equals(opportunity.categoryId, categoryId, StringComparison.Ordinal);
        }

        private static float ActionPressureMultiplier(RivalDecisionAction action)
        {
            return action switch
            {
                RivalDecisionAction.Bid => 1f,
                RivalDecisionAction.Watch => WatchDemandWeight01,
                RivalDecisionAction.Defer => DeferDemandWeight01,
                _ => 0f
            };
        }

        private static float OwnerMarketRelevanceWeight(RivalDecisionProposal proposal)
        {
            float seriousnessWeight = proposal.Action switch
            {
                RivalDecisionAction.Bid => 1f,
                RivalDecisionAction.Watch => WatchOwnerRelevanceWeight01,
                RivalDecisionAction.Defer => DeferOwnerRelevanceWeight01,
                _ => 0f
            };

            // A rival only earns scale overlap if they are actually participating in this market slice.
            // Score shapes how forcefully that owner's broader scale should sharpen the local pressure read.
            return seriousnessWeight * Mathf.Lerp(OwnerRelevanceScoreFloor01, 1f, proposal.Score01);
        }

        private static float SaturatePressure(float rawPressure)
        {
            return Mathf.Clamp01(1f - Mathf.Exp(-Mathf.Max(0f, rawPressure)));
        }
    }
}
