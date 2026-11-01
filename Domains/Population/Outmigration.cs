using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 3, item 6: outmigration with destination tracking.
    /// The counterpart to immigration. People must also LEAVE town, and
    /// they must NOT leave to random places off-map and disappear.
    ///
    /// Triggers, checkable, not a dice roll: work drying up (no available
    /// work, business closure, job loss), unpaid wages / unpaid
    /// obligations, and other hardship conditions the existing systems
    /// already track (the caller supplies those as inputs; this class
    /// does not roll a town-wide percentage). A person with work and
    /// pay is never rolled out here: every trigger below must be true
    /// on the supplied evidence, or the departure is refused.
    ///
    /// Destination tracking, mandatory: every departure records a REAL
    /// destination, chosen in this order from destinations the game
    /// already knows: (1) a family contact / family-tree location
    /// (item 2: an off-map relative's location, supplied as a named
    /// destination by the caller), (2) a previous home town, (3) a
    /// known supplier town / off-map location from the items/freight
    /// work (a supplier id from OffMapSupplierCatalog, supplied by the
    /// caller), (4) a town along a travel route, or (5) a new named
    /// destination created with an identity and a reason. A departure
    /// with NO known destination is REJECTED, never silently
    /// randomised. "Off-map" alone is not a destination.
    ///
    /// No disappearance: this class does NOT delete anything. The
    /// existing MigrationDriver.MigrateOut already preserves identity,
    /// kinship, ownership, and debts, ends household membership with a
    /// reason, and ends employment with reason MigratedOut. This class
    /// adds the destination record on top of that pattern and, for the
    /// pure path used in tests, flips isOffMap the same way — but it
    /// does NOT create or delete HouseholdState, property, or
    /// obligation records. The production driver must handle those
    /// explicitly after this record: household (transfer, retain, or
    /// dissolve via HouseholdLifecycleManager), property (retain or
    /// transfer via the property authority, never silently dropped),
    /// and debts (retain as obligations via FinancialObligationAuthority,
    /// never written off by departure). Those three post-departure
    /// steps are named in the record's Reasons and are the exact
    /// missing link if the driver does not run them.
    ///
    /// Return path: a departed person remains contactable and
    /// recruitable in principle, because their record, kinship, and
    /// destination are preserved. <see cref="OutmigrationRecord"/> can
    /// be located by destination, and a return is the immigration path
    /// in reverse: contact via family tree / letter from the General
    /// Store (RecruitmentConversation), then ArrivalSettlement, if
    /// town conditions have improved. A return is NOT automatic and
    /// is NOT implemented as a timer here; the limit is that no
    /// production driver yet runs that return loop, and a departed
    /// person's recruitability window (how long they remain
    /// recruitable) is a Kennedy decision, supplied as a test
    /// parameter in tests, never canon.
    ///
    /// Reputation feed: a departure caused by unpaid work or work
    /// drying up must feed the reputation model. This class returns a
    /// reputation penalty signal (a negative opinion component, supplied
    /// to TownReputationModel by the driver as GoodOpinionScore01);
    /// it does NOT write the reputation model itself, because that
    /// model is a read model, not a writer.
    ///
    /// Slice gate, and a FLAGGED CONFLICT (not silently rewritten):
    /// this channel is condition-gated for later play and respects an
    /// AllowDepartures gate. The EXISTING MigrationDriver.EvaluateMonth
    /// still runs departures even when AllowNewArrivals is false
    /// (arrivals only are suppressed; see its comment). Since Pass XL
    /// phase 4 the driver DOES have an AllowDepartures mechanism
    /// (DemographicDriverContext.AllowDepartures, default true, phase 4
    /// finding F09) — but PopulationManager does NOT wire it to
    /// SuppressMigrantArrivals, because whether six-person slice
    /// residents may leave is a Kennedy decision. Until he activates
    /// it, this new tracked channel and the old driver disagree on
    /// slice departures, and that disagreement is stated here, not
    /// hidden. Since phase 5, OutmigrationCloseout closes the
    /// membership + employment legs after a departure on this channel;
    /// household dissolution, property, and debt handling remain the
    /// driver's explicit decisions (phase 4 finding F08).
    ///
    /// Historical grounding (v3.0, checked, no specialist table found):
    /// docpkg-v3.0's governing originals are unchanged (Canon XXXI /
    /// Tech XIV, 2026-10-07) and its specialist references contain no
    /// outmigration-destination table, so no specialist citation is
    /// invented for destinations. Grounding is Canon §7.1/§7.4 (people
    /// move for durable work/wages/opportunity, then housing decides
    /// feasibility; word-of-mouth spreads both ways) plus general
    /// frontier migration: work-seeking moves, return migration, and
    /// chain movement to known towns/kin, not random disappearance.
    /// Plain C#, no Unity dependency.
    /// </summary>
    public enum OutmigrationReason
    {
        Unspecified = 0,
        WorkDriedUp = 1,
        UnpaidWages = 2,
        BusinessClosed = 3,
        JobLost = 4,
        Hardship = 5,
    }

    public sealed class OutmigrationDestination
    {
        public string DestinationId = string.Empty; // supplier id, town id, or "family:<personId>"
        public string DestinationName = string.Empty; // named, never empty for a recorded departure
        public string DestinationKind = string.Empty; // "family-contact", "previous-home", "supplier-town", "route-town", "named-new"
        public string ReasonForChoice = string.Empty;
    }

    public sealed class OutmigrationRequest
    {
        public int PersonId = -1;
        public OutmigrationReason Reason;
        public bool HasWork; // false = work dried up / job lost / business closed
        public bool HasUnpaidWages; // true = unpaid wages/obligations evidence supplied
        public bool HasHardship; // other tracked hardship evidence supplied
        public OutmigrationDestination Destination;
        public int DayIndex;
        public bool AllowDepartures; // slice gate: false = no departures in the slice
    }

    public sealed class OutmigrationRecord
    {
        public bool Departed;
        public int PersonId = -1;
        public OutmigrationReason Reason;
        public OutmigrationDestination Destination;
        public int DayIndex;
        public double ReputationPenalty01; // driver feeds this into TownReputationModel opinion
        public List<string> Reasons = new List<string>();
        public List<string> PostDepartureHandlingRequired = new List<string>
        {
            "Household: transfer, retain, or dissolve via HouseholdLifecycleManager — never silently dropped.",
            "Property: retain or transfer via the property authority — never silently dropped.",
            "Debts: retain as obligations via FinancialObligationAuthority — never written off by departure.",
        };
    }

    public static class Outmigration
    {
        public static OutmigrationRecord Depart(
            PopulationState population, KinshipRegistry kinship,
            OutmigrationRequest request, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var record = new OutmigrationRecord
            {
                PersonId = request?.PersonId ?? -1,
                Reason = request?.Reason ?? OutmigrationReason.Unspecified,
                Destination = request?.Destination,
                DayIndex = request?.DayIndex ?? 0,
            };
            if (population == null || population.people == null || request == null)
            {
                record.Reasons.Add("Outmigration: null population or request; departure refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (!request.AllowDepartures)
            {
                record.Reasons.Add("Outmigration: departures are gated off for this play mode (six-person slice); departure refused, person stays.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            PersonState person = population.GetPerson(request.PersonId);
            if (person == null)
            {
                record.Reasons.Add($"Outmigration: P{request.PersonId} unknown; departure refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (person.isOffMap)
            {
                record.Reasons.Add($"Outmigration: P{request.PersonId} is already off map; no second departure recorded.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (person.deathDayIndex >= 0)
            {
                record.Reasons.Add($"Outmigration: P{request.PersonId} is deceased; departure refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            // Trigger check: a person with work and pay, and no hardship,
            // is never departed here. At least one checkable trigger
            // must match the stated reason.
            bool triggerMatches = request.Reason switch
            {
                OutmigrationReason.WorkDriedUp => !request.HasWork,
                OutmigrationReason.BusinessClosed => !request.HasWork,
                OutmigrationReason.JobLost => !request.HasWork,
                OutmigrationReason.UnpaidWages => request.HasUnpaidWages,
                OutmigrationReason.Hardship => request.HasHardship,
                _ => false,
            };
            if (!triggerMatches)
            {
                record.Reasons.Add(
                    $"Outmigration: stated reason {request.Reason} does not match supplied evidence " +
                    $"(hasWork={request.HasWork}, unpaidWages={request.HasUnpaidWages}, hardship={request.HasHardship}); " +
                    "a person with work and pay is not rolled out. Departure refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (request.Destination == null
                || string.IsNullOrWhiteSpace(request.Destination.DestinationName)
                || string.IsNullOrWhiteSpace(request.Destination.DestinationId))
            {
                record.Reasons.Add("Outmigration: no known destination supplied; departure REJECTED, never silently randomised to 'off-map'. " +
                    "Supply a family-contact, previous-home, supplier-town, route-town, or named-new destination.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }

            // Preserve identity/kinship/ownership/debts by NOT deleting
            // anything: only the off-map flag changes here. Household
            // membership ending, employment ending, and the three
            // post-departure handlings are the production driver's job,
            // mirroring MigrationDriver.MigrateOut, and are listed in
            // PostDepartureHandlingRequired.
            person.isOffMap = true;
            record.Departed = true;
            record.ReputationPenalty01 = request.Reason == OutmigrationReason.UnpaidWages ? 0.2 : 0.1;
            record.Reasons.Add(
                $"Outmigration: P{request.PersonId} ({person.DisplayName}) departed on day {request.DayIndex} " +
                $"for {request.Reason}, destination '{request.Destination.DestinationName}' " +
                $"({request.Destination.DestinationKind}, {request.Destination.DestinationId}). " +
                "Identity, kinship, ownership, and debts preserved; household/property/debt handling is the driver's explicit next step, listed in PostDepartureHandlingRequired. " +
                "Return path: this person can be located by destination and contacted via family tree / letter from the General Store, then settled via ArrivalSettlement if town conditions improve; no automatic return is scheduled.");
            diagnostics.AddRange(record.Reasons);
            return record;
        }

        /// <summary>Locate departed persons by destination id. A departed person is contactable in principle, not disappeared.</summary>
        public static List<PersonState> FindDepartedAtDestination(
            PopulationState population, List<OutmigrationRecord> records, string destinationId)
        {
            var result = new List<PersonState>();
            if (population == null || records == null || string.IsNullOrWhiteSpace(destinationId)) return result;
            foreach (OutmigrationRecord record in records)
            {
                if (record != null && record.Departed && record.Destination != null
                    && record.Destination.DestinationId == destinationId)
                {
                    PersonState person = population.GetPerson(record.PersonId);
                    if (person != null && person.isOffMap) result.Add(person);
                }
            }
            return result;
        }
    }
}
