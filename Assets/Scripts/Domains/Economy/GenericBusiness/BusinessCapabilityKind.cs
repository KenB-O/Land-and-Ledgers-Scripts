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

}
