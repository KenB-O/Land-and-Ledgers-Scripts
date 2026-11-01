using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>Pass VIII Worker 2: the outcome of one absence sweep.</summary>
    [Serializable]
    public sealed class HousingAbsenceSweepResult
    {
        public int OccupanciesEnded;
        public int PersonsResolved;
        public List<int> ResolvedPersonIds = new List<int>();
        public List<string> EndedOccupancyIds = new List<string>();
        public List<int> ExemptedPersonIds = new List<int>();

        public HousingAbsenceSweepResult() { }
    }

    /// <summary>
    /// Pass VIII Worker 2: the absent-occupant boundary. An absent person
    /// (dead, or departed from the town roster) must not hold local
    /// accommodation indefinitely without a valid arrangement: the sweep
    /// ends their current occupancies, freeing each space for real
    /// occupants, and keeps the ended records as history.
    ///
    /// Boundary contract for the later autonomy worker:
    /// - "absent" is decided by the CALLER's isAbsent predicate over person
    ///   ids. The recommended wiring: dead (PersonState.deathDayIndex >= 0)
    ///   or departed (no longer in the town roster / settlement arrangement
    ///   Departed). The sweep never decides absence itself.
    /// - "valid arrangement" is decided by the CALLER's
    ///   hasValidAbsenceArrangement predicate over the occupancy. Null
    ///   means strict: no exemptions, every absent person's occupancy ends.
    ///   A lenient wiring can exempt e.g. a departed person whose prepaid
    ///   boarding agreement still stands — see AgreementStillStands.
    /// - onPersonVacated fires once per resolved person AFTER their
    ///   occupancies end, so the runtime can release non-housing holds on
    ///   the same person (boarding beds via the lodging bridge).
    ///
    /// The sweep moves no money and ends no agreements: arrears and
    /// tenancy records survive in their own authorities.
    /// </summary>
    public sealed class HousingAbsenceSweep
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Strict default: no arrangement exempts an absent person.
        /// </summary>
        public static bool StrictNoExemption(ResidentialOccupancy occupancy) => false;

        /// <summary>
        /// Lenient-policy helper: true when the occupancy names an
        /// agreement that still stands on dayIndex (not ended). The
        /// autonomy worker can pass occ => AgreementStillStands(housing,
        /// occ, day) as the exemption predicate when a live agreement
        /// should hold the space through a temporary absence.
        /// </summary>
        public static bool AgreementStillStands(HousingAuthority housing, ResidentialOccupancy occupancy, int dayIndex)
        {
            if (housing == null || occupancy == null) return false;
            if (string.IsNullOrWhiteSpace(occupancy.AgreementId)) return false;
            PropertyAgreement agreement = housing.FindAgreement(occupancy.AgreementId);
            if (agreement == null) return false;
            return agreement.EndDayIndex < 0 || agreement.EndDayIndex > dayIndex;
        }

        public HousingAbsenceSweepResult Sweep(
            HousingAuthority housing,
            Func<int, bool> isAbsent,
            int dayIndex,
            List<string> diag,
            Func<ResidentialOccupancy, bool> hasValidAbsenceArrangement = null,
            Action<int> onPersonVacated = null)
        {
            diag = diag ?? diagnostics;
            var result = new HousingAbsenceSweepResult();
            if (housing == null || isAbsent == null)
            {
                diag.Add("HousingAbsenceSweep.Sweep: housing authority and absence predicate are required — nothing swept, nothing assumed.");
                return result;
            }

            var byPerson = new Dictionary<int, List<ResidentialOccupancy>>();
            foreach (ResidentialOccupancy occ in housing.AllCurrentOccupancies())
            {
                if (occ == null || occ.PersonId < 0) continue;
                if (!byPerson.TryGetValue(occ.PersonId, out List<ResidentialOccupancy> list))
                {
                    list = new List<ResidentialOccupancy>();
                    byPerson[occ.PersonId] = list;
                }
                list.Add(occ);
            }

            foreach (KeyValuePair<int, List<ResidentialOccupancy>> entry in byPerson)
            {
                int personId = entry.Key;
                bool absent;
                try { absent = isAbsent(personId); }
                catch (Exception ex)
                {
                    diag.Add($"HousingAbsenceSweep: absence predicate threw for P{personId} ({ex.GetType().Name}) — person left alone, never assumed absent.");
                    continue;
                }
                if (!absent) continue;

                bool exempt = false;
                if (hasValidAbsenceArrangement != null)
                {
                    foreach (ResidentialOccupancy occ in entry.Value)
                    {
                        bool valid;
                        try { valid = hasValidAbsenceArrangement(occ); }
                        catch (Exception ex)
                        {
                            diag.Add($"HousingAbsenceSweep: arrangement predicate threw for P{personId} ({ex.GetType().Name}) — treated as no valid arrangement.");
                            valid = false;
                        }
                        if (valid) { exempt = true; break; }
                    }
                }
                if (exempt)
                {
                    result.ExemptedPersonIds.Add(personId);
                    diag.Add($"HousingAbsenceSweep: P{personId} is absent but holds a valid arrangement — occupancy stands.");
                    continue;
                }

                foreach (ResidentialOccupancy occ in entry.Value)
                {
                    occ.UntilDayIndex = dayIndex;
                    result.OccupanciesEnded++;
                    result.EndedOccupancyIds.Add(occ.OccupancyId);
                    diag.Add($"HousingAbsenceSweep: P{personId} absent — occupancy '{occ.OccupancyId}' in '{occ.SpaceId}' ended (day {dayIndex}); the space is free.");
                }
                result.PersonsResolved++;
                result.ResolvedPersonIds.Add(personId);
                try { onPersonVacated?.Invoke(personId); }
                catch (Exception ex)
                {
                    diag.Add($"HousingAbsenceSweep: onPersonVacated threw for P{personId} ({ex.GetType().Name}) — occupancy already ended, callback skipped.");
                }
            }

            diag.Add($"HousingAbsenceSweep: day {dayIndex} — {result.PersonsResolved} absent person(s) resolved, " +
                     $"{result.OccupanciesEnded} occupanc(ies) ended, {result.ExemptedPersonIds.Count} exempted by valid arrangement.");
            return result;
        }
    }
}
