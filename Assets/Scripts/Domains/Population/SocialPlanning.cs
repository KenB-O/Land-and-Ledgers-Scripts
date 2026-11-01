// Pass VII Workstream E: plannable social activity.
//
// Socializing is a real PersonActivityKind with real time cost, reserved
// through the PersonScheduleTracker like any other activity — one activity
// at a time, never double-booked. This file wires the minimal planning the
// flagship proof needs: a person visits a venue (e.g. the saloon) with a
// social motivation. The venue is an ordinary business; there is no
// separate saloon conversation engine.
using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Why a person seeks company. Social motivations are ordinary human
    /// reasons — never reduced to a transaction.
    /// </summary>
    public enum SocialVisitMotivation
    {
        Unspecified = 0,
        Companionship = 1,
        Friendship = 2,
        Family = 3,
        Recreation = 4,
        JobSeeking = 5,
        BusinessInterest = 6,
    }

    /// <summary>
    /// Plans social visits through the activity system. A visit reserves
    /// PersonActivityKind.Socializing at a venue business; the reservation
    /// label carries the venue and the motivation so conversation
    /// opportunities can be resolved from real schedule state.
    /// </summary>
    public static class SocialVisitPlanner
    {
        /// <summary>Builds the stable social reservation label.</summary>
        public static string SocialReservationLabel(string venueBusinessId, SocialVisitMotivation motivation)
        {
            return $"social:{venueBusinessId ?? string.Empty}:{motivation}";
        }

        /// <summary>
        /// Parses a social reservation label. Returns false when the label is
        /// not a social visit label.
        /// </summary>
        public static bool TryParseSocialLabel(string label, out string venueBusinessId, out SocialVisitMotivation motivation)
        {
            venueBusinessId = string.Empty;
            motivation = SocialVisitMotivation.Unspecified;
            if (string.IsNullOrWhiteSpace(label) || !label.StartsWith("social:", StringComparison.Ordinal))
                return false;
            string[] parts = label.Split(':');
            if (parts.Length < 3)
                return false;
            venueBusinessId = parts[1];
            if (string.IsNullOrWhiteSpace(venueBusinessId))
                return false;
            if (!Enum.TryParse(parts[2], out SocialVisitMotivation parsed))
                return false;
            motivation = parsed;
            return true;
        }

        /// <summary>
        /// Reserves a social visit for a real person at a venue business.
        /// Returns null on success, or the refusal reason (the tracker's own
        /// refusal when the person is already busy — Person time is
        /// authoritative).
        /// </summary>
        public static string PlanSocialVisit(
            int personId,
            string venueBusinessId,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            SocialVisitMotivation motivation,
            PersonScheduleTracker schedules,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (personId < 0)
                return "no real Person — only a real Person holds a schedule";
            if (string.IsNullOrWhiteSpace(venueBusinessId))
                return "a venue business is required";
            if (schedules == null)
                return "the schedule tracker is required";
            if (motivation == SocialVisitMotivation.Unspecified)
                return "a social motivation is required — visits are never motiveless";

            string refusal = schedules.TryReserve(
                personId, PersonActivityKind.Socializing, dayIndex,
                startMinuteOfDay, durationMinutes,
                SocialReservationLabel(venueBusinessId, motivation));
            if (refusal != null)
            {
                diagnostics.Add($"SocialVisitPlanner: P{personId} visit to {venueBusinessId} refused — {refusal}.");
                return refusal;
            }

            diagnostics.Add($"SocialVisitPlanner: P{personId} visits {venueBusinessId} on day {dayIndex} " +
                            $"[{startMinuteOfDay}-{startMinuteOfDay + durationMinutes}] for {motivation}.");
            return null;
        }
    }
}
