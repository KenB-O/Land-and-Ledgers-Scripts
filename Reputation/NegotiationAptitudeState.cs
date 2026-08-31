using System;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public sealed class NegotiationAptitudeState
    {
        public float negotiationExperiencePoints;
        public int completedNegotiations;
        public int difficultNegotiationsCompleted;
        public int badFaithAttempts;
        public int cleanCountersAccepted;
        public int wellStructuredOffers;

        public NegotiationAptitudeState Clone()
        {
            NegotiationAptitudeState clone = new NegotiationAptitudeState
            {
                negotiationExperiencePoints = negotiationExperiencePoints,
                completedNegotiations = completedNegotiations,
                difficultNegotiationsCompleted = difficultNegotiationsCompleted,
                badFaithAttempts = badFaithAttempts,
                cleanCountersAccepted = cleanCountersAccepted,
                wellStructuredOffers = wellStructuredOffers
            };
            clone.Clamp();
            return clone;
        }

        public void Clamp()
        {
            negotiationExperiencePoints = Mathf.Max(0f, negotiationExperiencePoints);
            completedNegotiations = Mathf.Max(0, completedNegotiations);
            difficultNegotiationsCompleted = Mathf.Max(0, difficultNegotiationsCompleted);
            badFaithAttempts = Mathf.Max(0, badFaithAttempts);
            cleanCountersAccepted = Mathf.Max(0, cleanCountersAccepted);
            wellStructuredOffers = Mathf.Max(0, wellStructuredOffers);
        }

        public void RecordNegotiation(bool completed, bool difficult, bool cleanCounterAccepted, bool wellStructuredOffer, bool badFaith)
        {
            Clamp();

            if (badFaith)
            {
                badFaithAttempts++;
                Clamp();
                return;
            }

            if (completed)
            {
                completedNegotiations++;
                negotiationExperiencePoints += difficult ? 10f : 6f;
            }
            else
            {
                negotiationExperiencePoints += 2f;
            }

            if (difficult && completed)
            {
                difficultNegotiationsCompleted++;
            }

            if (cleanCounterAccepted)
            {
                cleanCountersAccepted++;
                negotiationExperiencePoints += 4f;
            }

            if (wellStructuredOffer)
            {
                wellStructuredOffers++;
                negotiationExperiencePoints += 3f;
            }

            Clamp();
        }
    }
}
