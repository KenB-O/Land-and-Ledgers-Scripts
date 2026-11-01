// Generic Business Reconstruction, Pass 1 (new series), section 2-3.
// Player-facing capability kinds. These are LABELS and grouping keys only:
// they never grant an operation by themselves. Whether a business can
// actually perform an operation is decided at execution time from its
// real equipment, workspace, premises right, people, materials, skills
// and legal authority (see GenericBusinessCapabilitySet feasibility).
// This file is dependency-free plain C# so it can be verified outside
// Unity; Unity types live only in the adapter layer (see
// GenericBusinessUnityAdapter.cs).
using System;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>
    /// Player-facing capability kinds, as named in the owner's pass prompt.
    /// Internal technical archetypes (BusinessArchetype / BusinessThroughputMode
    /// on BusinessProfileDefinition) may continue to exist; these kinds are
    /// the understandable labels Unity AI should show in the UI.
    /// Values are append-only: never renumber an existing kind, because
    /// saved capability sets may reference it by name.
    /// </summary>
    public enum BusinessCapabilityKind
    {
        Unspecified = 0,
        SellGoods = 1,
        MakeProducts = 2,
        OfferServices = 3,
        TransportGoodsOrPeople = 4,
        ProvideLodging = 5,
        PerformConstruction = 6,
    }

    public static class BusinessCapabilityKindLabels
    {
        public static string DisplayName(BusinessCapabilityKind kind)
        {
            switch (kind)
            {
                case BusinessCapabilityKind.SellGoods: return "Sell Goods";
                case BusinessCapabilityKind.MakeProducts: return "Make Products";
                case BusinessCapabilityKind.OfferServices: return "Offer Services";
                case BusinessCapabilityKind.TransportGoodsOrPeople: return "Transport Goods or People";
                case BusinessCapabilityKind.ProvideLodging: return "Provide Lodging";
                case BusinessCapabilityKind.PerformConstruction: return "Perform Construction";
                default: return "Unspecified";
            }
        }

        /// <summary>
        /// Stable capability ids used by the existing capability registry
        /// (BusinessCapabilityRegistry) where a matching id already exists.
        /// New ids introduced here are descriptive only; registration never
        /// grants permission to act.
        /// </summary>
        public static string SuggestedCapabilityId(BusinessCapabilityKind kind)
        {
            switch (kind)
            {
                case BusinessCapabilityKind.SellGoods: return "retail";
                case BusinessCapabilityKind.MakeProducts: return "production";
                case BusinessCapabilityKind.OfferServices: return "services";
                case BusinessCapabilityKind.TransportGoodsOrPeople: return "freight";
                case BusinessCapabilityKind.ProvideLodging: return "lodging";
                case BusinessCapabilityKind.PerformConstruction: return "construction";
                default: return string.Empty;
            }
        }
    }
}
