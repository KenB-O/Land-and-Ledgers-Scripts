using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// B2 (universal business campaign 2026-11-17): calibration for
    /// commercial leases. Every value is PROVISIONAL, for the owner to
    /// correct — the 1500c monthly shop rent is the figure the codebase
    /// already uses for a rented shop (PhaseGNpcFormationTests "shop on
    /// Main", HouseholdVerticalSlicePhaseH); the deposit is one month's
    /// rent, the term is one year, rent falls due every 30 days, and a
    /// shop let fitted carries the standard fitted-shop set (counter,
    /// shelving, scale). A fitted shop is let WITH its fixtures: the
    /// fixtures are part of the leased premises, owned by the landlord,
    /// used by the tenant while the lease runs, and never the tenant's
    /// property to sell or liquidate.
    /// </summary>
    public static class CommercialLeaseCalibration
    {
        public const int MonthlyRentCents = 1500;
        public const int DepositCents = 1500;
        public const int TermMonths = 12;
        public const int RentPeriodDays = 30;

        /// <summary>
        /// The formation-asset item id for fixtures included in a lease.
        /// The itemized set lives on the listing (<see
        /// cref="CommercialLeaseListing.IncludedFixtures"/>); this id is
        /// the single evidence token the investigation carries.
        /// </summary>
        public const string LeasedFixturesItemId = "shop-fixtures";

        /// <summary>The provenance prefix for lease-included fixtures.</summary>
        public const string LeasedFixturesProvenancePrefix = "lease-listing:";

        /// <summary>The standard fitted-shop set, in listing order.</summary>
        public static readonly string[] FittedShopFixtures =
        {
            "counter",
            "shelving",
            "scale",
        };
    }

    /// <summary>
    /// B2: one commercial building offered (or offered once) as a lease.
    /// A building id plus plot id, never an invented plot.
    /// </summary>
    [Serializable]
    public readonly struct CommercialLeasePremisesRef
    {
        public readonly int BuildingId;
        public readonly int PlotId;

        public CommercialLeasePremisesRef(int buildingId, int plotId)
        {
            BuildingId = buildingId;
            PlotId = plotId;
        }
    }

    /// <summary>B2: the life of one commercial lease listing.</summary>
    public enum CommercialLeaseListingStatus
    {
        Unspecified = 0,
        Active = 1,
        Leased = 2,
        Withdrawn = 3,
    }

    /// <summary>
    /// B2: one shop (or other commercial building) listed for lease, with
    /// real terms recorded on it: rent, deposit, term, and what is
    /// included. The landlord is a real person ("person:&lt;id&gt;") or
    /// unresolved (-1 / empty holder): a listing with no landlord is
    /// recorded and visible, but never offered to a founder — no building
    /// is let on behalf of nobody. The listing id is the building id, so
    /// one building can hold one listing at a time.
    /// </summary>
    [Serializable]
    public sealed class CommercialLeaseListing
    {
        public string ListingId = string.Empty;
        public int BuildingId = -1;
        public int PlotId = -1;
        public int LandlordPersonId = -1;
        public string LandlordHolderId = string.Empty;
        public string LandlordName = string.Empty;
        public int MonthlyRentCents;
        public int DepositCents;
        public int TermMonths;
        public List<string> IncludedFixtures = new List<string>();
        public CommercialLeaseListingStatus Status = CommercialLeaseListingStatus.Unspecified;
        public int ListedDayIndex;

        public CommercialLeaseListing() { }

        /// <summary>
        /// Offerable only while Active with a real landlord. An
        /// unresolved-landlord listing is a record, not an offer.
        /// </summary>
        public bool IsOfferable =>
            Status == CommercialLeaseListingStatus.Active && LandlordPersonId >= 0;
    }

    /// <summary>B2: the life of one commercial lease agreement.</summary>
    public enum CommercialLeaseAgreementStatus
    {
        Unspecified = 0,
        Active = 1,
        Ended = 2,
    }

    /// <summary>
    /// B2: one signed commercial lease. The tenant is a real person and
    /// household; the business instance is set at formation. Rent falls
    /// due every <see cref="CommercialLeaseCalibration.RentPeriodDays"/>
    /// days from the start day; <see cref="LastSettledPeriodIndex"/> is
    /// the last period settled (paid in full or booked as arrears), -1
    /// when none is settled yet. The deposit is the landlord's security,
    /// refundable on a clean lease end — never rent, never the tenant's
    /// to spend.
    /// </summary>
    [Serializable]
    public sealed class CommercialLeaseAgreement
    {
        public string AgreementId = string.Empty;
        public string ListingId = string.Empty;
        public int BuildingId = -1;
        public int PlotId = -1;
        public int TenantPersonId = -1;
        public int TenantHouseholdId = -1;
        public string BusinessInstanceId = string.Empty;
        public string LandlordHolderId = string.Empty;
        public int LandlordPersonId = -1;
        public int MonthlyRentCents;
        public int DepositCents;
        public int DepositPaidCents;
        public int StartDayIndex;
        public int EndDayIndex = -1;
        public int TermMonths;
        public List<string> IncludedFixtures = new List<string>();
        public CommercialLeaseAgreementStatus Status = CommercialLeaseAgreementStatus.Unspecified;
        public int LastSettledPeriodIndex = -1;
        public List<string> RentPayableIds = new List<string>();

        public CommercialLeaseAgreement() { }
    }

    /// <summary>B2: the lease market's save record (clear-then-load).</summary>
    [Serializable]
    public sealed class CommercialLeaseMarketSaveDto
    {
        public List<CommercialLeaseListing> Listings = new List<CommercialLeaseListing>();
        public List<CommercialLeaseAgreement> Agreements = new List<CommercialLeaseAgreement>();
        public int AgreementSequence;
    }

    /// <summary>
    /// B2 (universal business campaign 2026-11-17): the commercial lease
    /// authority. A record authority, not a cash mover: it records
    /// listings and agreements, and the caller moves money (deposit and
    /// first rent at formation, monthly rent through <see
    /// cref="CommercialLeaseRentSettlement"/>). Listings come from the
    /// town's actual unassigned commercial buildings — never invented
    /// buildings or plots — and a listing is offerable only with a real
    /// landlord. The market gains a landlord when a building is released
    /// (a closing business's premises return to the market with the
    /// rights-holder as landlord); it never invents one.
    ///
    /// Fitted shops are let with their fixtures (counter, shelving,
    /// scale). The fixtures belong to the landlord, travel with the
    /// building across tenancies, and are never a formation asset of the
    /// tenant's business — so they never liquidate on close.
    /// </summary>
    public sealed class CommercialLeaseMarket
    {
        public List<CommercialLeaseListing> Listings = new List<CommercialLeaseListing>();
        public List<CommercialLeaseAgreement> Agreements = new List<CommercialLeaseAgreement>();
        public int AgreementSequence;

        /// <summary>
        /// Seeds one listing per unassigned commercial building that has
        /// no listing yet (any status). Seeded listings carry no landlord
        /// — no building owner is recorded for them — so they are
        /// recorded but never offered. Idempotent; re-seeding is a no-op.
        /// </summary>
        public void SeedFromPremises(IReadOnlyList<CommercialLeasePremisesRef> premises,
            int dayIndex, List<string> diag)
        {
            if (premises == null) return;
            foreach (CommercialLeasePremisesRef premisesRef in premises)
            {
                if (premisesRef.BuildingId < 0) continue;
                if (FindListingForBuilding(premisesRef.BuildingId) != null) continue;
                var listing = new CommercialLeaseListing
                {
                    ListingId = ListingIdForBuilding(premisesRef.BuildingId),
                    BuildingId = premisesRef.BuildingId,
                    PlotId = premisesRef.PlotId,
                    LandlordPersonId = -1,
                    LandlordHolderId = string.Empty,
                    LandlordName = string.Empty,
                    MonthlyRentCents = CommercialLeaseCalibration.MonthlyRentCents,
                    DepositCents = CommercialLeaseCalibration.DepositCents,
                    TermMonths = CommercialLeaseCalibration.TermMonths,
                    IncludedFixtures = new List<string>(CommercialLeaseCalibration.FittedShopFixtures),
                    Status = CommercialLeaseListingStatus.Active,
                    ListedDayIndex = dayIndex,
                };
                Listings.Add(listing);
                diag?.Add($"CommercialLeaseMarket: listed building {premisesRef.BuildingId} (plot {premisesRef.PlotId}) " +
                    "for lease with no landlord recorded — recorded, not offered.");
            }
        }

        /// <summary>
        /// Lists one building with a real landlord. Refuses (null) when
        /// an Active or Leased listing already exists for the building.
        /// A Withdrawn listing may be re-listed.
        /// </summary>
        public CommercialLeaseListing ListBuilding(int buildingId, int plotId,
            int landlordPersonId, string landlordName, int dayIndex, List<string> diag)
        {
            if (buildingId < 0)
            {
                diag?.Add("CommercialLeaseMarket: cannot list a building with no building id.");
                return null;
            }

            if (landlordPersonId < 0)
            {
                diag?.Add($"CommercialLeaseMarket: cannot list building {buildingId} — no landlord person id.");
                return null;
            }

            CommercialLeaseListing existing = FindListingForBuilding(buildingId);
            if (existing != null
                && (existing.Status == CommercialLeaseListingStatus.Active
                    || existing.Status == CommercialLeaseListingStatus.Leased))
            {
                diag?.Add($"CommercialLeaseMarket: building {buildingId} already has a {existing.Status} listing '{existing.ListingId}' — not re-listed.");
                return null;
            }

            var listing = new CommercialLeaseListing
            {
                ListingId = ListingIdForBuilding(buildingId),
                BuildingId = buildingId,
                PlotId = plotId,
                LandlordPersonId = landlordPersonId,
                LandlordHolderId = "person:" + landlordPersonId,
                LandlordName = landlordName ?? string.Empty,
                MonthlyRentCents = CommercialLeaseCalibration.MonthlyRentCents,
                DepositCents = CommercialLeaseCalibration.DepositCents,
                TermMonths = CommercialLeaseCalibration.TermMonths,
                IncludedFixtures = new List<string>(CommercialLeaseCalibration.FittedShopFixtures),
                Status = CommercialLeaseListingStatus.Active,
                ListedDayIndex = dayIndex,
            };
            Listings.Add(listing);
            diag?.Add($"CommercialLeaseMarket: listed building {buildingId} (plot {plotId}) for lease " +
                $"by '{listing.LandlordName}' (P{landlordPersonId}): {listing.MonthlyRentCents}c/month, " +
                $"{listing.DepositCents}c deposit, {listing.TermMonths} months, fitted " +
                $"({string.Join(", ", listing.IncludedFixtures)}).");
            return listing;
        }

        /// <summary>
        /// Withdraws an Active listing. A Leased listing cannot be
        /// withdrawn from under its tenant; withdrawal is refused.
        /// </summary>
        public bool WithdrawListing(string listingId, int dayIndex, List<string> diag)
        {
            CommercialLeaseListing listing = FindListingById(listingId);
            if (listing == null)
            {
                diag?.Add($"CommercialLeaseMarket: cannot withdraw listing '{listingId}' — not found.");
                return false;
            }

            if (listing.Status == CommercialLeaseListingStatus.Leased)
            {
                diag?.Add($"CommercialLeaseMarket: cannot withdraw listing '{listingId}' — building {listing.BuildingId} is leased.");
                return false;
            }

            if (listing.Status == CommercialLeaseListingStatus.Withdrawn)
            {
                diag?.Add($"CommercialLeaseMarket: listing '{listingId}' is already withdrawn.");
                return false;
            }

            listing.Status = CommercialLeaseListingStatus.Withdrawn;
            diag?.Add($"CommercialLeaseMarket: withdrew listing '{listingId}' (building {listing.BuildingId}) on day {dayIndex}.");
            return true;
        }

        /// <summary>
        /// Signs the lease: the listing becomes Leased and the agreement
        /// is recorded. <paramref name="firstRentPeriodPrepaid"/> is true
        /// when the first month's rent was paid at signing (the driver's
        /// deposit-plus-first-rent transfer), so period 0 is settled and
        /// the rent settlement starts at period 1. Returns null on
        /// refusal (listing not offerable, tenant ids invalid).
        /// </summary>
        public CommercialLeaseAgreement ActivateLease(CommercialLeaseListing listing,
            int tenantPersonId, int tenantHouseholdId, string businessInstanceId,
            int depositPaidCents, bool firstRentPeriodPrepaid, int dayIndex, List<string> diag)
        {
            if (listing == null)
            {
                diag?.Add("CommercialLeaseMarket: cannot activate a lease — no listing.");
                return null;
            }

            if (!listing.IsOfferable)
            {
                diag?.Add($"CommercialLeaseMarket: cannot activate a lease on listing '{listing.ListingId}' — " +
                    $"status {listing.Status}, landlord P{listing.LandlordPersonId} (must be Active with a real landlord).");
                return null;
            }

            if (tenantPersonId < 0 || tenantHouseholdId < 0 || string.IsNullOrWhiteSpace(businessInstanceId))
            {
                diag?.Add($"CommercialLeaseMarket: cannot activate a lease on listing '{listing.ListingId}' — " +
                    $"tenant P{tenantPersonId} / H{tenantHouseholdId} / business '{businessInstanceId}' is incomplete.");
                return null;
            }

            if (depositPaidCents < 0)
            {
                diag?.Add($"CommercialLeaseMarket: cannot activate a lease on listing '{listing.ListingId}' — deposit paid {depositPaidCents}c is negative.");
                return null;
            }

            AgreementSequence++;
            var agreement = new CommercialLeaseAgreement
            {
                AgreementId = "lease-" + AgreementSequence,
                ListingId = listing.ListingId,
                BuildingId = listing.BuildingId,
                PlotId = listing.PlotId,
                TenantPersonId = tenantPersonId,
                TenantHouseholdId = tenantHouseholdId,
                BusinessInstanceId = businessInstanceId,
                LandlordHolderId = listing.LandlordHolderId,
                LandlordPersonId = listing.LandlordPersonId,
                MonthlyRentCents = listing.MonthlyRentCents,
                DepositCents = listing.DepositCents,
                DepositPaidCents = depositPaidCents,
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
                TermMonths = listing.TermMonths,
                IncludedFixtures = new List<string>(listing.IncludedFixtures),
                Status = CommercialLeaseAgreementStatus.Active,
                LastSettledPeriodIndex = firstRentPeriodPrepaid ? 0 : -1,
            };
            Agreements.Add(agreement);
            listing.Status = CommercialLeaseListingStatus.Leased;
            diag?.Add($"CommercialLeaseMarket: lease '{agreement.AgreementId}' signed for building {listing.BuildingId} " +
                $"(P{tenantPersonId} tenant, business '{businessInstanceId}'): {agreement.MonthlyRentCents}c/month, " +
                $"deposit {agreement.DepositPaidCents}c paid, {agreement.TermMonths} months from day {dayIndex}" +
                (firstRentPeriodPrepaid ? ", first month prepaid." : "."));
            return agreement;
        }

        /// <summary>
        /// Ends an Active lease. The listing returns to Active with the
        /// same landlord and the same fixtures, so the building is
        /// offerable again. The deposit refund is the caller's job
        /// (the settlement's arrears check decides it).
        /// </summary>
        public bool EndLease(CommercialLeaseAgreement agreement, int dayIndex, List<string> diag)
        {
            if (agreement == null)
            {
                diag?.Add("CommercialLeaseMarket: cannot end a lease — no agreement.");
                return false;
            }

            if (agreement.Status != CommercialLeaseAgreementStatus.Active)
            {
                diag?.Add($"CommercialLeaseMarket: lease '{agreement.AgreementId}' is already {agreement.Status} — not ended again.");
                return false;
            }

            agreement.Status = CommercialLeaseAgreementStatus.Ended;
            agreement.EndDayIndex = dayIndex;
            CommercialLeaseListing listing = FindListingById(agreement.ListingId);
            if (listing != null && listing.Status == CommercialLeaseListingStatus.Leased)
            {
                listing.Status = CommercialLeaseListingStatus.Active;
                diag?.Add($"CommercialLeaseMarket: lease '{agreement.AgreementId}' ended on day {dayIndex}; " +
                    $"building {agreement.BuildingId} is listed again by the same landlord.");
            }
            else
            {
                diag?.Add($"CommercialLeaseMarket: lease '{agreement.AgreementId}' ended on day {dayIndex}; " +
                    $"its listing '{agreement.ListingId}' was not Leased, so no re-listing.");
            }

            return true;
        }

        public CommercialLeaseListing FindListingById(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId)) return null;
            foreach (CommercialLeaseListing listing in Listings)
            {
                if (listing != null && string.Equals(listing.ListingId, listingId, StringComparison.Ordinal))
                    return listing;
            }

            return null;
        }

        /// <summary>The listing for a building, any status, or null.</summary>
        public CommercialLeaseListing FindListingForBuilding(int buildingId)
        {
            foreach (CommercialLeaseListing listing in Listings)
            {
                if (listing != null && listing.BuildingId == buildingId)
                    return listing;
            }

            return null;
        }

        /// <summary>The offerable listing for a building, or null.</summary>
        public CommercialLeaseListing FindOfferableListingForBuilding(int buildingId)
        {
            CommercialLeaseListing listing = FindListingForBuilding(buildingId);
            return listing != null && listing.IsOfferable ? listing : null;
        }

        public CommercialLeaseAgreement FindAgreementById(string agreementId)
        {
            if (string.IsNullOrWhiteSpace(agreementId)) return null;
            foreach (CommercialLeaseAgreement agreement in Agreements)
            {
                if (agreement != null && string.Equals(agreement.AgreementId, agreementId, StringComparison.Ordinal))
                    return agreement;
            }

            return null;
        }

        /// <summary>The Active lease for a business, or null.</summary>
        public CommercialLeaseAgreement FindActiveLeaseForBusiness(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            foreach (CommercialLeaseAgreement agreement in Agreements)
            {
                if (agreement != null && agreement.Status == CommercialLeaseAgreementStatus.Active
                    && string.Equals(agreement.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
                    return agreement;
            }

            return null;
        }

        /// <summary>The Active lease for a building, or null.</summary>
        public CommercialLeaseAgreement FindActiveLeaseForBuilding(int buildingId)
        {
            foreach (CommercialLeaseAgreement agreement in Agreements)
            {
                if (agreement != null && agreement.Status == CommercialLeaseAgreementStatus.Active
                    && agreement.BuildingId == buildingId)
                    return agreement;
            }

            return null;
        }

        public static string ListingIdForBuilding(int buildingId) => "lease-listing:" + buildingId;

        public CommercialLeaseMarketSaveDto CaptureSaveDto()
        {
            var dto = new CommercialLeaseMarketSaveDto { AgreementSequence = AgreementSequence };
            foreach (CommercialLeaseListing listing in Listings)
            {
                if (listing != null) dto.Listings.Add(listing);
            }

            foreach (CommercialLeaseAgreement agreement in Agreements)
            {
                if (agreement != null) dto.Agreements.Add(agreement);
            }

            return dto;
        }

        /// <summary>Clear-then-load. A null DTO clears to an empty market (old saves had no leases).</summary>
        public void LoadFromSaveDto(CommercialLeaseMarketSaveDto dto)
        {
            Listings.Clear();
            Agreements.Clear();
            AgreementSequence = 0;
            if (dto == null) return;
            AgreementSequence = Math.Max(0, dto.AgreementSequence);
            if (dto.Listings != null)
            {
                foreach (CommercialLeaseListing listing in dto.Listings)
                {
                    if (listing != null) Listings.Add(listing);
                }
            }

            if (dto.Agreements != null)
            {
                foreach (CommercialLeaseAgreement agreement in dto.Agreements)
                {
                    if (agreement != null) Agreements.Add(agreement);
                }
            }
        }
    }
}
