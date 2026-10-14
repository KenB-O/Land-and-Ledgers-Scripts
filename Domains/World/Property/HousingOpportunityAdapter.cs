using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass VIII Worker 2: housing discovery over REAL availability. This
    /// <see cref="IHousingOpportunitySource"/> implementation answers the
    /// search planner only from facts in the housing authority: vacant
    /// accommodation spaces become rental listings, and actively registered
    /// lodging with genuinely open beds becomes boarding vacancies. When
    /// nothing is vacant, the planner gets no options and fails loudly with
    /// reasons — discovery never invents a room, a bed, or a free house.
    ///
    /// Cross-system feeds (read-only, no sibling modifications):
    /// - migration: relocation/destination planning consumes RentalListings
    ///   and BoardingVacancies as real destinations.
    /// - employer recruitment: EmployerLodgingOpenings is answered by
    ///   scenario wiring; the adapter's boarding vacancies are the bed
    ///   facts underneath it.
    /// - construction demand: HousingSearchNeedRegistry open needs are the
    ///   unmet-demand signal the construction worker consumes (see
    ///   UnmetSleepingPlaces).
    /// </summary>
    public sealed class HousingOpportunityAdapter : IHousingOpportunitySource
    {
        private readonly HousingAuthority housing;
        private readonly LodgingRegistry lodging;
        private readonly Func<string, int> openBedsForRegistration;
        private readonly Func<AccommodationSpace, Tuple<string, string>> landlordResolver;
        private int sequence;

        /// <param name="housing">The accommodation authority — the only source of spaces.</param>
        /// <param name="lodging">Lodging registrations; null = no boarding vacancies offered.</param>
        /// <param name="openBedsForRegistration">
        /// Real open-bed counts per registration id (the bridge supplies this
        /// from the business room inventory); null = registrations offer
        /// their full bed count minus housing-side occupants.
        /// </param>
        /// <param name="landlordResolver">
        /// Optional: space to (provider display name, payee holder id). When
        /// null, rental options name the payee as unknown and the executor
        /// records the agreement without a payment schedule (loudly
        /// diagnosed) — scenario wiring should supply the real landlord.
        /// </param>
        public HousingOpportunityAdapter(
            HousingAuthority housing,
            LodgingRegistry lodging = null,
            Func<string, int> openBedsForRegistration = null,
            Func<AccommodationSpace, Tuple<string, string>> landlordResolver = null)
        {
            this.housing = housing;
            this.lodging = lodging;
            this.openBedsForRegistration = openBedsForRegistration;
            this.landlordResolver = landlordResolver;
        }

        /// <summary>
        /// Unmet demand in sleeping places across open search needs — the
        /// construction-demand signal the construction worker consumes:
        /// every open need is a household the current stock cannot house.
        /// </summary>
        public static int UnmetSleepingPlaces(HousingSearchNeedRegistry needs)
        {
            if (needs == null) return 0;
            var all = new List<HousingSearchNeed>();
            needs.ExportState(all);
            int total = 0;
            foreach (HousingSearchNeed need in all)
            {
                if (need != null && need.Status == HousingSearchNeedStatus.Open)
                    total += Math.Max(0, need.RequiredSleepingPlaces);
            }
            return total;
        }

        public List<HousingSearchOption> RentalListings(int requiredPlaces)
        {
            var options = new List<HousingSearchOption>();
            if (housing == null) return options;
            foreach (AccommodationSpace space in housing.VacantSpaces())
            {
                if (space == null || space.SleepingCapacity < Math.Max(1, requiredPlaces)) continue;
                string provider = "landlord (unregistered)";
                string payee = string.Empty;
                if (landlordResolver != null)
                {
                    try
                    {
                        Tuple<string, string> resolved = landlordResolver(space);
                        if (resolved != null)
                        {
                            if (!string.IsNullOrWhiteSpace(resolved.Item1)) provider = resolved.Item1;
                            if (!string.IsNullOrWhiteSpace(resolved.Item2)) payee = resolved.Item2;
                        }
                    }
                    catch (Exception) { /* resolver failure: fall back to unregistered, never invent */ }
                }
                options.Add(new HousingSearchOption
                {
                    OptionId = $"vac-{space.SpaceId}-{sequence++}",
                    PathKind = HousingSearchPathKind.Rental,
                    ProviderName = provider,
                    PayeeHolderId = payee,
                    Description = $"vacant space '{space.SpaceId}' ({space.SleepingCapacity} sleeping place(s))",
                    SpaceId = space.SpaceId,
                    BuildingId = space.BuildingId,
                    Arrangement = AccommodationArrangement.Rental,
                    PlacesProvided = space.SleepingCapacity,
                });
            }
            return options;
        }

        public List<HousingSearchOption> BoardingVacancies(int requiredPlaces)
        {
            var options = new List<HousingSearchOption>();
            if (housing == null || lodging == null) return options;
            foreach (LodgingRegistration registration in lodging.AllActiveRegistrations())
            {
                if (registration == null) continue;
                int openBeds = registration.BedCount - housing.OccupantCount(registration.SpaceId);
                if (openBedsForRegistration != null)
                {
                    try { openBeds = Math.Min(openBeds, Math.Max(0, openBedsForRegistration(registration.RegistrationId))); }
                    catch (Exception) { /* keep the housing-side count */ }
                }
                if (openBeds < Math.Max(1, requiredPlaces)) continue;
                options.Add(new HousingSearchOption
                {
                    OptionId = $"bdg-{registration.RegistrationId}-{sequence++}",
                    PathKind = HousingSearchPathKind.Boarding,
                    ProviderName = registration.BusinessInstanceId,
                    PayeeHolderId = "business:" + registration.BusinessInstanceId,
                    Description = $"boarding vacancy: '{registration.BusinessInstanceId}' room '{registration.RoomId}' ({openBeds} open bed(s))",
                    SpaceId = registration.SpaceId,
                    Arrangement = registration.Arrangement,
                    PlacesProvided = openBeds,
                    RecurringMonthlyCents = registration.RateCentsPerWeek * 4,
                });
            }
            return options;
        }

        // Kin, employer, shelter, purchase, finance, land and relocation
        // paths need social/credit/market data this adapter does not own.
        // They return no options here rather than invented ones; scenario
        // wiring composes this adapter with sources that do.
        public List<HousingSearchOption> KinOffers(int householdId) => new List<HousingSearchOption>();
        public List<HousingSearchOption> EmployerLodgingOpenings(int householdId) => new List<HousingSearchOption>();
        public List<HousingSearchOption> TemporaryShelters() => new List<HousingSearchOption>();
        public List<HousingSearchOption> PropertiesForSale(int requiredPlaces) => new List<HousingSearchOption>();
        public List<HousingSearchOption> SellerFinanceOffers() => new List<HousingSearchOption>();
        public List<HousingSearchOption> LenderOffers(int householdId) => new List<HousingSearchOption>();
        public List<HousingSearchOption> LandParcelsForSale() => new List<HousingSearchOption>();
        public List<HousingSearchOption> RelocationDestinations(int householdId) => new List<HousingSearchOption>();
    }
}
