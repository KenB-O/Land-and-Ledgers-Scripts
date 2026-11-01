using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III foundation: the SINGLE non-title rights store.
    /// Canon: Tech XIV preserved Rev XII §2.2 — typed PropertyInterest/encumbrance records
    /// "can coexist without duplicate Parcel ownership flags".
    ///
    /// Title (who holds underlying ownership) lives in TitleAuthority's chain.
    /// Everything else — lease, easement, mortgage charge, public-land entry case,
    /// right-of-way, restriction, dispute claim, tax lien — lives here, exactly once.
    /// There is no other PropertyInterest class in the codebase (verified 2026-10-11).
    /// Do NOT create a second rights registry.
    /// ID convention: "interest-" + 32 lowercase hex chars.
    /// </summary>

    /// <summary>Kind of non-title legal interest. Append-only; do not renumber.</summary>
    public enum InterestKind
    {
        Unknown = 0,
        Lease = 1,
        Easement = 2,
        MortgageCharge = 3,
        PublicLandEntry = 4,
        RightOfWay = 5,
        UseRestriction = 6,
        DisputeClaim = 7,
        TaxLien = 8,
    }

    [Serializable]
    public class PropertyInterest
    {
        public string Id;
        public string ParcelId;
        public InterestKind Kind = InterestKind.Unknown;
        /// <summary>Person, household, business, or government id holding this interest.</summary>
        public string HolderId;
        /// <summary>Optional link to the agreement/obligation record carrying terms
        /// (e.g. a lease agreement, a FinancialObligationAuthority obligation id).</summary>
        public string TermsRef;
        public int StartDay;
        /// <summary>0 = indefinite / no recorded end.</summary>
        public int EndDay;
        public string Notes;
        public bool IsExtinguished;
    }

    /// <summary>
    /// Query contract the implementation workstream provides. Exactly one store
    /// answers "what non-title interests burden this parcel".
    /// </summary>
    public interface IPropertyInterestStore
    {
        void Record(PropertyInterest interest);
        void Extinguish(string interestId, int day, string reason);
        IReadOnlyList<PropertyInterest> ActiveForParcel(string parcelId);
        IReadOnlyList<PropertyInterest> ActiveForHolder(string holderId);
    }
}
