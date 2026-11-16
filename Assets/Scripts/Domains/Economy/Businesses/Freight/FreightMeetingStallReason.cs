using System;

namespace LandLedgers.Economy.Freight
{
    /// <summary>CAMPAIGN-B (DEF-007), package 5: pure, zero-Unity-dependency
    /// wording for the two silent stalls in
    /// SimulationDrivers.TryScheduleFreightMeeting.
    ///
    /// The freight meeting gate itself is INTENTIONAL and is NOT changed
    /// here: the Nov 13 design decision settles that new agreements require
    /// the people to physically meet (management menus do not create
    /// agreements), and the register entry for DEF-007 records that the work
    /// is making the meeting reliably happen (scheduling, windows, retry),
    /// not removing the gate. The daily retry already exists — the
    /// negotiation driver re-creates Glen's contact travel every day and
    /// StartQueuedFreightMeetingTasks retries every AtMeeting request every
    /// tick — but the two returns below produced no signal at all, so a
    /// meeting that never schedules (Ethan never visually at the staging
    /// building at tick time, or no common window left today) went silent
    /// after Glen's arrival log. This class only names the reason; the
    /// driver logs it once per day per request through the existing
    /// day-keyed refusal logger, so a persistent stall stays visible
    /// instead of going silent after day 1 (Check 10 F-18 family).</summary>
    public static class FreightMeetingStallReason
    {
        /// <summary>Returns null when both people are present (no stall);
        /// otherwise a one-line reason naming who is missing from the
        /// meeting building. Never returns an empty string.</summary>
        public static string DescribePresenceStall(string requestId,
            int requestingPersonId, int carrierPersonId, int meetingBuildingId,
            bool requesterPresent, bool carrierPresent)
        {
            if (requesterPresent && carrierPresent) return null;
            if (!requesterPresent && !carrierPresent)
                return "[FreightAgreement] Meeting '" + requestId + "' cannot schedule: neither Glen P"
                    + requestingPersonId + " nor Ethan P" + carrierPersonId
                    + " is physically at the meeting building " + meetingBuildingId + ".";
            if (!requesterPresent)
                return "[FreightAgreement] Meeting '" + requestId + "' cannot schedule: Glen P"
                    + requestingPersonId + " is not physically at the meeting building "
                    + meetingBuildingId + " (Ethan P" + carrierPersonId + " is present).";
            return "[FreightAgreement] Meeting '" + requestId + "' cannot schedule: Ethan P"
                + carrierPersonId + " is not physically at the meeting building "
                + meetingBuildingId + " (Glen P" + requestingPersonId + " is present).";
        }

        /// <summary>Both people are present but no common window of the
        /// meeting duration remains today. Always returns a reason.</summary>
        public static string DescribeNoWindowStall(string requestId,
            int requestingPersonId, int carrierPersonId,
            int nowMinute, int durationMinutes)
        {
            return "[FreightAgreement] Meeting '" + requestId + "' cannot schedule: Glen P"
                + requestingPersonId + " and Ethan P" + carrierPersonId
                + " are both at the meeting location, but no common "
                + durationMinutes + "-minute window remains today (now=" + nowMinute + " min).";
        }
    }
}
