using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// D1F: the kinds of conduct incidents a boarding house records. Canon
    /// §8.1G: reputation "should emerge from actual experiences such as ...
    /// theft/security problems" — so the house keeps the experiences as
    /// facts. The kinds are deliberately coarse; the canon describes no
    /// formal rulebook, so D1F records facts and a proprietor-set ejection
    /// policy, never an invented code of house rules.
    /// </summary>
    public enum BoardingConductIncidentKind
    {
        Disturbance = 0,
        Theft = 1,
        PropertyDamage = 2,
        UnpaidBill = 3,
        Other = 4,
    }

    /// <summary>D1F: one recorded conduct incident against a real person on a real day.</summary>
    [Serializable]
    public sealed class BoardingConductIncident
    {
        public string IncidentId = string.Empty;
        public int PersonId;
        public int DayIndex;
        public BoardingConductIncidentKind Kind = BoardingConductIncidentKind.Other;
        public string Note = string.Empty;

        public BoardingConductIncident() { }
    }

    /// <summary>
    /// D1F: the proprietor's conduct policy as data. Canon §8.1D: the owner
    /// sets policy (target customer mix, account tolerance); the ejection
    /// threshold is proprietor policy, settable per house. Every incident
    /// counts as one strike (TUNING-adjacent simplification, documented).
    /// </summary>
    [Serializable]
    public sealed class BoardingHouseConductPolicy
    {
        /// <summary>TUNING: strikes before the proprietor ejects a boarder.</summary>
        public int EjectAfterStrikes = 3;

        public BoardingHouseConductPolicy() { }
    }

    /// <summary>
    /// D1F: the house's conduct log — incident facts, never verdicts. The
    /// log answers "how many strikes does this person hold" and "has this
    /// person crossed the proprietor's ejection threshold"; the runtime
    /// performs the ejection (checkout). Incidents stay on the books as the
    /// fact input the later settlement-wide reputation pass consumes
    /// (Canon §8.1G) — D1F does not aggregate reputation itself.
    /// </summary>
    public sealed class BoardingHouseConductLog
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<BoardingConductIncident> incidents = new List<BoardingConductIncident>();
        private int incidentSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoardingConductIncident> Incidents => incidents;

        /// <summary>
        /// Records an incident fact against a real person. Returns the
        /// incident id, or null plus a loud refusal when the fact is unusable.
        /// </summary>
        public string RecordIncident(int personId, BoardingConductIncidentKind kind, int dayIndex, string note, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "BoardingHouseConductLog.RecordIncident: an incident needs a real person id — anonymous incidents are not recorded.";
            if (dayIndex < 0)
                return "BoardingHouseConductLog.RecordIncident: an incident needs a real day index.";

            var incident = new BoardingConductIncident
            {
                IncidentId = $"bh-incident-{incidentSequence++}",
                PersonId = personId,
                DayIndex = dayIndex,
                Kind = kind,
                Note = note ?? string.Empty,
            };
            incidents.Add(incident);
            diag.Add($"BoardingHouseConductLog: {kind} recorded against person {personId} (day {dayIndex}) — strike {StrikesFor(personId)}.");
            return incident.IncidentId;
        }

        /// <summary>Strikes held by one person (every incident counts as one).</summary>
        public int StrikesFor(int personId)
        {
            if (personId <= 0) return 0;
            int strikes = 0;
            for (int i = 0; i < incidents.Count; i++)
                if (incidents[i] != null && incidents[i].PersonId == personId)
                    strikes++;
            return strikes;
        }

        /// <summary>All incidents recorded against one person, oldest first.</summary>
        public List<BoardingConductIncident> IncidentsFor(int personId)
        {
            var found = new List<BoardingConductIncident>();
            for (int i = 0; i < incidents.Count; i++)
                if (incidents[i] != null && incidents[i].PersonId == personId)
                    found.Add(incidents[i]);
            return found;
        }

        /// <summary>True when the person's strikes reach the proprietor's ejection threshold.</summary>
        public bool ShouldEject(int personId, BoardingHouseConductPolicy policy)
        {
            int threshold = policy != null ? Math.Max(1, policy.EjectAfterStrikes) : 3;
            return StrikesFor(personId) >= threshold;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseConductLogSaveDto
        {
            public int IncidentSequence = 1;
            public List<BoardingConductIncident> Incidents = new List<BoardingConductIncident>();
        }

        public BoardingHouseConductLogSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseConductLogSaveDto { IncidentSequence = Math.Max(1, incidentSequence) };
            foreach (BoardingConductIncident incident in incidents)
            {
                if (incident == null || incident.PersonId <= 0) continue;
                dto.Incidents.Add(incident);
            }
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseConductLogSaveDto dto)
        {
            incidents.Clear();
            incidentSequence = 1;
            if (dto == null) return;
            incidentSequence = Math.Max(1, dto.IncidentSequence);
            if (dto.Incidents == null) return;
            foreach (BoardingConductIncident incident in dto.Incidents)
            {
                if (incident == null || incident.PersonId <= 0 || incident.DayIndex < 0) continue;
                incidents.Add(incident);
            }
        }
        #endregion
    }
}
