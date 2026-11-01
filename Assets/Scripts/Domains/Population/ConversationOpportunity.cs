// Pass VII Workstream E: legitimate conversation opportunities.
//
// Two real persons can converse only through a legitimate channel with
// overlapping availability and compatible activity:
//
//   Venue         — both socializing at the same venue business (e.g. the
//                   saloon), with overlapping reservation time.
//   Workplace     — both working for the same employer, overlapping time.
//   Household     — same household, overlapping free (non-work) time.
//   Correspondence— a delivered letter from speaker to listener.
//
// A person working a distant mine cannot converse in the saloon in the same
// interval: the venue channel needs BOTH at the venue. The saloon uses these
// same mechanics as every other venue — no special engine.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Postal;

namespace LandLedgers.Population
{
    /// <summary>The legitimate channel a conversation travels through.</summary>
    public enum ConversationChannelKind
    {
        Unspecified = 0,
        Venue = 1,
        Workplace = 2,
        Household = 3,
        Correspondence = 4,
    }

    /// <summary>One resolved conversation channel between two persons.</summary>
    public sealed class ConversationChannel
    {
        public ConversationChannelKind Kind = ConversationChannelKind.Unspecified;
        public string Detail = string.Empty; // venue/employer business id, household id, mail id
        public int OverlapStartMinute;
        public int OverlapEndMinute;
        public int OverlapMinutes => Math.Max(0, OverlapEndMinute - OverlapStartMinute);
    }

    /// <summary>
    /// Resolves legitimate conversation opportunities from real schedule,
    /// employment, household, venue and postal state. Implements
    /// IConversationOpportunity: null when legitimate, a reason when not.
    /// </summary>
    public sealed class ConversationOpportunityService : IConversationOpportunity
    {
        private readonly PersonScheduleTracker schedules;
        private readonly Func<int, PersonState> personById;
        private readonly EmploymentRelationshipRegistry employments;
        private readonly Func<string, BusinessInstanceState> businessById;
        private readonly PostalService postal;

        public ConversationOpportunityService(
            PersonScheduleTracker schedules,
            Func<int, PersonState> personById,
            EmploymentRelationshipRegistry employments,
            Func<string, BusinessInstanceState> businessById,
            PostalService postal = null)
        {
            this.schedules = schedules;
            this.personById = personById;
            this.employments = employments;
            this.businessById = businessById;
            this.postal = postal;
        }

        public string CheckOpportunity(int speakerId, int listenerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (TryGetChannel(speakerId, listenerId, dayIndex, out ConversationChannel channel, diagnostics))
            {
                diagnostics.Add($"ConversationOpportunity: P{speakerId} and P{listenerId} can converse via {channel.Kind} ({channel.Detail}).");
                return null;
            }

            return DescribeRefusal(speakerId, listenerId, dayIndex, diagnostics);
        }

        /// <summary>Resolves the channel, or returns false when none is legitimate.</summary>
        public bool TryGetChannel(
            int speakerId, int listenerId, int dayIndex,
            out ConversationChannel channel, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            channel = null;

            string personRefusal = CheckPersons(speakerId, listenerId, dayIndex);
            if (personRefusal != null)
            {
                diagnostics.Add("ConversationOpportunity: " + personRefusal + ".");
                return false;
            }

            // Delivered correspondence needs no shared time.
            if (TryCorrespondenceChannel(speakerId, listenerId, dayIndex, out channel, diagnostics))
                return true;

            List<PersonActivityReservation> speakerReservations = ActiveReservations(speakerId, dayIndex);
            List<PersonActivityReservation> listenerReservations = ActiveReservations(listenerId, dayIndex);

            foreach (PersonActivityReservation speakerReservation in speakerReservations)
            {
                foreach (PersonActivityReservation listenerReservation in listenerReservations)
                {
                    int start = Math.Max(speakerReservation.StartMinuteOfDay, listenerReservation.StartMinuteOfDay);
                    int end = Math.Min(speakerReservation.EndMinuteOfDay, listenerReservation.EndMinuteOfDay);
                    if (end - start < 5)
                        continue; // no meaningful overlap

                    if (TryVenueChannel(speakerReservation, listenerReservation, start, end, out channel))
                        return true;
                    if (TryWorkplaceChannel(speakerId, listenerId, speakerReservation, listenerReservation, start, end, out channel))
                        return true;
                    if (TryHouseholdChannel(speakerId, listenerId, speakerReservation, listenerReservation, start, end, out channel))
                        return true;
                }
            }

            return false;
        }

        private string CheckPersons(int speakerId, int listenerId, int dayIndex)
        {
            if (speakerId < 0 || listenerId < 0)
                return "both parties must be real persons";
            if (speakerId == listenerId)
                return "a person cannot converse with themselves";
            if (personById == null)
                return "no person directory";

            PersonState speaker = personById(speakerId);
            PersonState listener = personById(listenerId);
            if (speaker == null || listener == null)
                return "both parties must be real persons";
            if (IsDead(speaker, dayIndex) || IsDead(listener, dayIndex))
                return "the dead do not converse";

            return null;
        }

        private static bool IsDead(PersonState person, int dayIndex)
        {
            return person != null && person.deathDayIndex >= 0 && person.deathDayIndex <= dayIndex;
        }

        private List<PersonActivityReservation> ActiveReservations(int personId, int dayIndex)
        {
            var result = new List<PersonActivityReservation>();
            if (schedules == null || personId < 0)
                return result;
            foreach (PersonActivityReservation reservation in schedules.ActiveFor(personId, dayIndex))
            {
                if (reservation != null && !reservation.Released)
                    result.Add(reservation);
            }
            return result;
        }

        private bool TryVenueChannel(
            PersonActivityReservation speakerReservation,
            PersonActivityReservation listenerReservation,
            int start, int end,
            out ConversationChannel channel)
        {
            channel = null;
            if (speakerReservation.Activity != PersonActivityKind.Socializing
                || listenerReservation.Activity != PersonActivityKind.Socializing)
                return false;

            if (!SocialVisitPlanner.TryParseSocialLabel(speakerReservation.Label, out string speakerVenue, out _))
                return false;
            if (!SocialVisitPlanner.TryParseSocialLabel(listenerReservation.Label, out string listenerVenue, out _))
                return false;
            if (!string.Equals(speakerVenue, listenerVenue, StringComparison.Ordinal))
                return false;

            // The venue must be a real business — no conversations at phantom venues.
            if (businessById == null || businessById(speakerVenue) == null)
                return false;

            channel = new ConversationChannel
            {
                Kind = ConversationChannelKind.Venue,
                Detail = speakerVenue,
                OverlapStartMinute = start,
                OverlapEndMinute = end,
            };
            return true;
        }

        private bool TryWorkplaceChannel(
            int speakerId, int listenerId,
            PersonActivityReservation speakerReservation,
            PersonActivityReservation listenerReservation,
            int start, int end,
            out ConversationChannel channel)
        {
            channel = null;
            if (employments == null)
                return false;
            if (speakerReservation.Activity != PersonActivityKind.Work
                || listenerReservation.Activity != PersonActivityKind.Work)
                return false;

            string speakerEmployer = ActiveEmployer(speakerId);
            string listenerEmployer = ActiveEmployer(listenerId);
            if (string.IsNullOrWhiteSpace(speakerEmployer)
                || !string.Equals(speakerEmployer, listenerEmployer, StringComparison.Ordinal))
                return false;

            channel = new ConversationChannel
            {
                Kind = ConversationChannelKind.Workplace,
                Detail = speakerEmployer,
                OverlapStartMinute = start,
                OverlapEndMinute = end,
            };
            return true;
        }

        private string ActiveEmployer(int personId)
        {
            foreach (EmploymentRelationship relationship in employments.GetActiveByEmployee(personId))
            {
                if (relationship != null && !string.IsNullOrWhiteSpace(relationship.EmployerBusinessId))
                    return relationship.EmployerBusinessId;
            }
            return string.Empty;
        }

        private bool TryHouseholdChannel(
            int speakerId, int listenerId,
            PersonActivityReservation speakerReservation,
            PersonActivityReservation listenerReservation,
            int start, int end,
            out ConversationChannel channel)
        {
            channel = null;
            if (personById == null)
                return false;

            PersonState speaker = personById(speakerId);
            PersonState listener = personById(listenerId);
            if (speaker == null || listener == null)
                return false;
            if (speaker.householdId < 0 || speaker.householdId != listener.householdId)
                return false;

            // Household talk happens in free time — not while working,
            // sleeping, traveling, or otherwise occupied.
            if (!IsFreeTimeActivity(speakerReservation.Activity) || !IsFreeTimeActivity(listenerReservation.Activity))
                return false;

            channel = new ConversationChannel
            {
                Kind = ConversationChannelKind.Household,
                Detail = $"household:{speaker.householdId}",
                OverlapStartMinute = start,
                OverlapEndMinute = end,
            };
            return true;
        }

        private static bool IsFreeTimeActivity(PersonActivityKind activity)
        {
            switch (activity)
            {
                case PersonActivityKind.Socializing:
                case PersonActivityKind.Resting:
                case PersonActivityKind.Eating:
                case PersonActivityKind.Cooking:
                    return true;
                default:
                    return false;
            }
        }

        private bool TryCorrespondenceChannel(
            int speakerId, int listenerId, int dayIndex,
            out ConversationChannel channel, List<string> diagnostics)
        {
            channel = null;
            if (postal == null)
                return false;

            foreach (MailItem item in postal.MailItems)
            {
                if (item == null) continue;
                if (item.SenderPersonId != speakerId || item.RecipientPersonId != listenerId) continue;
                if (item.Kind != MailKind.Letter && item.Kind != MailKind.Postcard) continue;
                // Delivered means it reached the destination office (Arrived)
                // or the recipient's hands (Collected). Lost, in-transit and
                // forwarded mail carries no conversation.
                if (item.Status != MailStatus.Arrived && item.Status != MailStatus.Collected) continue;
                if (item.DueArrivalDayIndex > dayIndex) continue;

                channel = new ConversationChannel
                {
                    Kind = ConversationChannelKind.Correspondence,
                    Detail = item.MailId,
                    OverlapStartMinute = 0,
                    OverlapEndMinute = 0,
                };
                diagnostics.Add($"ConversationOpportunity: delivered letter {item.MailId} from P{speakerId} to P{listenerId}.");
                return true;
            }

            return false;
        }

        private string DescribeRefusal(int speakerId, int listenerId, int dayIndex, List<string> diagnostics)
        {
            string personRefusal = CheckPersons(speakerId, listenerId, dayIndex);
            if (personRefusal != null)
            {
                diagnostics.Add("ConversationOpportunity refused: " + personRefusal + ".");
                return personRefusal;
            }

            string reason = $"no legitimate channel: P{speakerId} and P{listenerId} share no venue, workplace, household free time, or delivered correspondence on day {dayIndex}";
            diagnostics.Add("ConversationOpportunity refused: " + reason + ".");
            return reason;
        }
    }

    /// <summary>
    /// Runs one conversation: checks the opportunity, then records the
    /// information transmission. A conversation never transmits without a
    /// legitimate channel.
    /// </summary>
    public static class ConversationRunner
    {
        /// <summary>
        /// Returns null on success, or the refusal reason. On success the
        /// packet is recorded in the knowledge store.
        /// </summary>
        public static string Converse(
            ConversationOpportunityService opportunities,
            IKnowledgeTransmissionStore knowledge,
            int speakerId,
            int listenerId,
            int dayIndex,
            InformationPacket packet,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (opportunities == null) return "no conversation opportunity service";
            if (knowledge == null) return "no knowledge store";
            if (packet == null) return "no information to transmit";

            string refusal = opportunities.CheckOpportunity(speakerId, listenerId, dayIndex, diagnostics);
            if (refusal != null)
                return refusal;

            packet.SpeakerPersonId = speakerId;
            packet.RecipientPersonId = listenerId;
            packet.TransmittedDayIndex = dayIndex;
            string recordRefusal = knowledge.RecordTransmission(packet, diagnostics);
            if (recordRefusal != null)
                return recordRefusal;

            diagnostics.Add($"ConversationRunner: P{speakerId} told P{listenerId} '{packet.Subject}' (packet {packet.PacketId}).");
            return null;
        }
    }
}
