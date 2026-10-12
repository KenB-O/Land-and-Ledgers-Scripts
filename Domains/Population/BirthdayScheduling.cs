using System;
using System.Collections.Generic;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V (Worker 4): birthday scheduling on the hub DueEventScheduler.
    ///
    /// Contract (shared with Worker 2's P0-2 demographic work):
    /// - Payload convention "birthday:{personId}" via AgeAuthority.BirthdayEventId.
    /// - Scheduler-level event id "due-BirthdayDue-birthday:{personId}" (stable).
    /// - Scheduling the same person twice REPLACES the event (the scheduler is
    ///   idempotent by id), so birthdays never double-schedule across save/load.
    /// - Deceased persons are never scheduled; callers pass only living persons.
    /// - Off-map persons ARE scheduled (birthdays fire while unloaded/off-map).
    /// - Due time is absolute game seconds: absoluteDay * 86400.
    /// - When a birthday pops, the handler processes it (age derives from the
    ///   birth-date authority) and immediately schedules the NEXT birthday, so
    ///   the chain is exactly one event per living person at any time.
    /// </summary>
    public static class BirthdayScheduling
    {
        public const float SecondsPerDay = 86400f;

        /// <summary>
        /// The scheduler-level event id for a person's birthday. Stable across
        /// save/load and rescheduling: the same person always maps to the same id.
        /// </summary>
        public static string SchedulerEventId(int personId)
        {
            return "due-" + DueEventKind.BirthdayDue + "-" + AgeAuthority.BirthdayEventId(personId);
        }

        /// <summary>
        /// True when a due-event payload is a birthday payload.
        /// </summary>
        public static bool IsBirthdayPayload(string payloadRef)
        {
            return !string.IsNullOrEmpty(payloadRef) &&
                payloadRef.StartsWith(AgeAuthority.BirthdayEventIdPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Extracts the person id from a "birthday:{personId}" payload, or -1.
        /// </summary>
        public static int PersonIdFromPayload(string payloadRef)
        {
            if (!IsBirthdayPayload(payloadRef))
            {
                return -1;
            }
            string tail = payloadRef.Substring(AgeAuthority.BirthdayEventIdPrefix.Length);
            int personId;
            return int.TryParse(tail, out personId) ? personId : -1;
        }

        /// <summary>
        /// Schedules the person's next birthday on or after fromAbsoluteDay.
        /// Returns null on success, or a refusal reason. Idempotent: an existing
        /// birthday event for this person is replaced, never duplicated.
        /// </summary>
        public static string ScheduleNextBirthday(
            DueEventScheduler scheduler,
            int personId,
            BirthDate birth,
            int fromAbsoluteDay,
            int anchorYear,
            int anchorMonth,
            int anchorDay,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (scheduler == null)
            {
                return "BirthdayScheduling: a due-event scheduler is required.";
            }
            if (!birth.IsValid)
            {
                return $"BirthdayScheduling: P{personId} has no valid birth date; birthday not scheduled.";
            }

            int birthdayDay = AgeAuthority.NextBirthdayAbsoluteDay(
                birth, fromAbsoluteDay, anchorYear, anchorMonth, anchorDay);
            if (birthdayDay < 0)
            {
                return $"BirthdayScheduling: P{personId} has no computable next birthday.";
            }

            string eventId;
            string refusal = scheduler.Schedule(
                DueEventKind.BirthdayDue,
                birthdayDay * SecondsPerDay,
                AgeAuthority.BirthdayEventId(personId),
                out eventId);
            if (refusal != null)
            {
                return "BirthdayScheduling: " + refusal;
            }

            string expected = SchedulerEventId(personId);
            if (!string.Equals(eventId, expected, StringComparison.Ordinal))
            {
                diag.Add($"BirthdayScheduling: scheduler id '{eventId}' differs from convention '{expected}' " +
                    "(the scheduler owns id format; the payload convention is the stable contract).");
            }
            diag.Add($"BirthdayScheduling: P{personId} next birthday scheduled for day {birthdayDay} (event {eventId}).");
            return null;
        }

        /// <summary>
        /// Cancels a person's pending birthday event. No-op when none is scheduled
        /// (e.g. after death, or before the first schedule).
        /// </summary>
        public static void CancelBirthday(DueEventScheduler scheduler, int personId)
        {
            if (scheduler == null)
            {
                return;
            }
            scheduler.Cancel(SchedulerEventId(personId));
        }
    }
}
