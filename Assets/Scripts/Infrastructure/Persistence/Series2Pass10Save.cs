// Series 2 Pass 10 (Pass 5 save adoption): the Pass 10 save section
// for the slice DTO. Additive only, following the Pass 9 pattern
// exactly: one new field on PassXlSliceSaveDto (series2Pass10),
// coordinator overloads, and this state class. The Pass 9 section
// is untouched.
//
// WHAT IS ADOPTED (all three already existed as Pass 5 model
// records; none is invented here):
//   - Pass5LeadSaveRecord (FromLead/ToLead) — enriched leads.
//   - Pass5TravelerVisitRecord — traveler visits (visitors only;
//     a visit never settles a resident by itself).
//   - Pass5ChainRecord — conversation-chain progress, caller-
//     supplied only. Pass 5 has no chain save record in any DTO;
//     this section persists exactly the chain records the caller
//     hands it, never invented progress.
// SNAPSHOTS: no Pass 5 town-evaluation save record existed
// anywhere in the repo (searched: Pass5TownEvaluation appears
// only in the TownGrowth model files), so Pass 10 adds the shape
// only: Series2Pass10TownSnapshotDto mirrors Pass5TownEvaluation
// field for field, carries caller-supplied values verbatim, and
// is null when no snapshot is on file. No snapshot value is ever
// invented, estimated, or defaulted into a real-looking score.
// A null snapshot restores as "no snapshot on file", diagnosed.
//
// CONTRACT (same as Pass 9): clear-then-load, idempotent.
// Restore clears the live lists first, then loads the DTO, so a
// second restore yields the same lists, never doubles. A null DTO
// or a DTO with null lists restores empty with a diagnostic —
// the old-save default, not data loss: an old save never had
// Pass 10 state to lose.
using System;
using System.Collections.Generic;
using LandLedgers.Population.TownGrowth;

namespace LandLedgers.Persistence
{
    /// <summary>
    /// Series 2 Pass 10: shape-only snapshot of one Pass 5 town
    /// evaluation. Every field is caller-supplied; a snapshot is
    /// a record of an evaluation that already happened, never a
    /// fresh evaluation and never a guessed score.
    /// </summary>
    [Serializable]
    public sealed class Series2Pass10TownSnapshotDto
    {
        public double ReputationScore01;
        public bool SnapshotStale;
        public bool WorkPasses;
        public bool HousingPasses;
        public bool ReputationPasses;
        public bool ArrivalsAllowed;
        public bool LegacyRollStillLive = true;
        public string DepartureGateState = string.Empty;
        public int SnapshotDayIndex;
        public List<string> Reasons = new List<string>();

        public static Series2Pass10TownSnapshotDto FromEvaluation(
            Pass5TownEvaluation evaluation, int snapshotDayIndex)
        {
            if (evaluation == null) return null;
            return new Series2Pass10TownSnapshotDto
            {
                ReputationScore01 = evaluation.ReputationScore01,
                SnapshotStale = evaluation.SnapshotStale,
                WorkPasses = evaluation.WorkPasses,
                HousingPasses = evaluation.HousingPasses,
                ReputationPasses = evaluation.ReputationPasses,
                ArrivalsAllowed = evaluation.ArrivalsAllowed,
                LegacyRollStillLive = evaluation.LegacyRollStillLive,
                DepartureGateState = evaluation.DepartureGateState ?? string.Empty,
                SnapshotDayIndex = snapshotDayIndex,
                Reasons = new List<string>(evaluation.Reasons ?? new List<string>()),
            };
        }

        public Pass5TownEvaluation ToEvaluation()
        {
            return new Pass5TownEvaluation
            {
                ReputationScore01 = ReputationScore01,
                SnapshotStale = SnapshotStale,
                WorkPasses = WorkPasses,
                HousingPasses = HousingPasses,
                ReputationPasses = ReputationPasses,
                ArrivalsAllowed = ArrivalsAllowed,
                LegacyRollStillLive = LegacyRollStillLive,
                DepartureGateState = DepartureGateState ?? string.Empty,
                Reasons = new List<string>(Reasons ?? new List<string>()),
            };
        }
    }

    /// <summary>Series 2 Pass 10 save section.</summary>
    [Serializable]
    public sealed class Series2Pass10SaveDto
    {
        public List<Pass5LeadSaveRecord> Leads = new List<Pass5LeadSaveRecord>();
        public List<Pass5TravelerVisitRecord> Visits = new List<Pass5TravelerVisitRecord>();
        public List<Pass5ChainRecord> Chains = new List<Pass5ChainRecord>();
        public Series2Pass10TownSnapshotDto Snapshot;
    }

    /// <summary>
    /// Series 2 Pass 10 save state: the live lists a driver owns,
    /// plus the optional caller-supplied town snapshot. Capture
    /// deep-copies; Restore clear-then-loads.
    /// </summary>
    public sealed class Series2Pass10SaveState
    {
        public List<Pass5LeadSaveRecord> Leads = new List<Pass5LeadSaveRecord>();
        public List<Pass5TravelerVisitRecord> Visits = new List<Pass5TravelerVisitRecord>();
        public List<Pass5ChainRecord> Chains = new List<Pass5ChainRecord>();

        /// <summary>
        /// Caller-supplied town snapshot, or null when no snapshot
        /// is on file. Null is a real state ("no snapshot"), never
        /// a placeholder score.
        /// </summary>
        public Series2Pass10TownSnapshotDto Snapshot;

        public List<string> LastCaptureDiagnostics = new List<string>();

        private static Pass5LeadSaveRecord CopyLead(Pass5LeadSaveRecord source)
        {
            if (source == null) return new Pass5LeadSaveRecord();
            return new Pass5LeadSaveRecord
            {
                ContactPersonId = source.ContactPersonId,
                ContactName = source.ContactName ?? string.Empty,
                RecommenderPersonId = source.RecommenderPersonId,
                RecommenderName = source.RecommenderName ?? string.Empty,
                BusinessId = source.BusinessId ?? string.Empty,
                RoleId = source.RoleId ?? string.Empty,
                RoleDisplayName = source.RoleDisplayName ?? string.Empty,
                LocationTownName = source.LocationTownName ?? string.Empty,
                LocationPostalDestinationId = source.LocationPostalDestinationId ?? string.Empty,
                ProfessionId = source.ProfessionId ?? string.Empty,
                ProfessionName = source.ProfessionName ?? string.Empty,
                Disposition = source.Disposition,
                HasCurrentWageCents = source.HasCurrentWageCents,
                CurrentWageCents = source.CurrentWageCents,
                DayIndex = source.DayIndex,
            };
        }

        private static Pass5TravelerVisitRecord CopyVisit(Pass5TravelerVisitRecord source)
        {
            if (source == null) return new Pass5TravelerVisitRecord();
            return new Pass5TravelerVisitRecord
            {
                TravelerId = source.TravelerId ?? string.Empty,
                TravelerName = source.TravelerName ?? string.Empty,
                LookingForStay = source.LookingForStay,
                BoardingVacancies = source.BoardingVacancies,
                DayIndex = source.DayIndex,
                StayEligible = source.StayEligible,
                NextStep = source.NextStep ?? string.Empty,
                Reasons = new List<string>(source.Reasons ?? new List<string>()),
            };
        }

        private static Pass5ChainRecord CopyChain(Pass5ChainRecord source)
        {
            if (source == null) return new Pass5ChainRecord();
            return new Pass5ChainRecord
            {
                LeadContactPersonId = source.LeadContactPersonId,
                Step = source.Step,
                DayIndex = source.DayIndex,
                PostagePaid = source.PostagePaid,
                LetterOrigin = source.LetterOrigin ?? string.Empty,
                PostalDestination = source.PostalDestination ?? string.Empty,
                Reasons = new List<string>(source.Reasons ?? new List<string>()),
            };
        }

        private static Series2Pass10TownSnapshotDto CopySnapshot(
            Series2Pass10TownSnapshotDto source)
        {
            if (source == null) return null;
            return new Series2Pass10TownSnapshotDto
            {
                ReputationScore01 = source.ReputationScore01,
                SnapshotStale = source.SnapshotStale,
                WorkPasses = source.WorkPasses,
                HousingPasses = source.HousingPasses,
                ReputationPasses = source.ReputationPasses,
                ArrivalsAllowed = source.ArrivalsAllowed,
                LegacyRollStillLive = source.LegacyRollStillLive,
                DepartureGateState = source.DepartureGateState ?? string.Empty,
                SnapshotDayIndex = source.SnapshotDayIndex,
                Reasons = new List<string>(source.Reasons ?? new List<string>()),
            };
        }

        public Series2Pass10SaveDto Capture(List<string> diag)
        {
            diag = diag ?? new List<string>();
            LastCaptureDiagnostics = diag;
            var dto = new Series2Pass10SaveDto();
            foreach (Pass5LeadSaveRecord lead in Leads ?? new List<Pass5LeadSaveRecord>())
                dto.Leads.Add(CopyLead(lead));
            foreach (Pass5TravelerVisitRecord visit in Visits ?? new List<Pass5TravelerVisitRecord>())
                dto.Visits.Add(CopyVisit(visit));
            foreach (Pass5ChainRecord chain in Chains ?? new List<Pass5ChainRecord>())
                dto.Chains.Add(CopyChain(chain));
            dto.Snapshot = CopySnapshot(Snapshot);
            diag.Add("Series2Pass10Save: captured " + dto.Leads.Count + " lead(s), " +
                dto.Visits.Count + " visit(s), " + dto.Chains.Count + " chain record(s)" +
                (dto.Snapshot != null ? ", plus a caller-supplied town snapshot." : ", no town snapshot on file."));
            return dto;
        }

        /// <summary>
        /// Clear-then-load restore. A null DTO, or a DTO with null
        /// lists, restores empty with a diagnostic (old-save
        /// default). Restoring twice yields the same state.
        /// Driver re-supply obligations (the Pass 5 driver itself,
        /// seller/lodging objects for any resumed chain step) are
        /// the driver's, not this class's: a restored chain record
        /// is data, never a resumed conversation.
        /// </summary>
        public void Restore(Series2Pass10SaveDto dto, List<string> diag)
        {
            diag = diag ?? new List<string>();
            Leads.Clear();
            Visits.Clear();
            Chains.Clear();
            Snapshot = null;
            if (dto == null)
            {
                diag.Add("Series2Pass10Save: no Pass 10 section in save; leads, visits, and chains restored as empty (old-save default). No snapshot on file.");
                return;
            }
            foreach (Pass5LeadSaveRecord lead in dto.Leads ?? new List<Pass5LeadSaveRecord>())
                Leads.Add(CopyLead(lead));
            foreach (Pass5TravelerVisitRecord visit in dto.Visits ?? new List<Pass5TravelerVisitRecord>())
                Visits.Add(CopyVisit(visit));
            foreach (Pass5ChainRecord chain in dto.Chains ?? new List<Pass5ChainRecord>())
                Chains.Add(CopyChain(chain));
            Snapshot = CopySnapshot(dto.Snapshot);
            diag.Add("Series2Pass10Save: restore complete (" + Leads.Count + " lead(s), " +
                Visits.Count + " visit(s), " + Chains.Count + " chain record(s)" +
                (Snapshot != null ? ", town snapshot restored." : ", no town snapshot on file.") + ")");
        }
    }
}
