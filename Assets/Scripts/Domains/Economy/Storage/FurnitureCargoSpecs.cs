// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F0 (expanded campaign 2026-11-16): per-item furniture
// cargo specs — the freight parameters for the shipped form of each
// first-wave furniture item. Follows the LumberCargoSpec pattern: a
// caller-set freight parameter with a written derivation, NOT universal
// item truth. The numbers are the plan section 6.1 values (O-F1: proceed
// on the proposed numbers; Kennedy corrects what he disagrees with),
// read from the single FurnitureItemSpecs table, so the cargo spec and
// the registry spec cannot drift apart: the registry holds the same mass
// in kilograms and the same volume in litres (1 L = 0.001 m3).
//
// FreightCargoCapacity remains the single trip-math authority; this class
// never re-derives trip counts. The FreightCargoSpec default (1 kg /
// 0.01 m3) is a placeholder with no authored basis — furniture must never
// be shipped on it, and TryBuildFreightSpec is the guard that stops an
// Unknown registry measure from silently becoming a zero-mass cargo
// (a 0 per-unit measure does not bind in FreightCargoCapacity, so a
// zero-mass spec would price an unbounded load).

using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;

namespace LandLedgers.Economy.Storage
{
    public static class FurnitureCargoSpecs
    {
        /// <summary>
        /// A fresh cargo spec for one first-wave furniture item, from the
        /// authored table. Always a new instance: callers own their spec.
        /// Null when the id is not a first-wave furniture item.
        /// </summary>
        public static FreightCargoSpec SpecFor(string itemId)
        {
            if (!FurnitureItemSpecs.TryGetRow(itemId, out FurnitureItemSpecs.Row row))
            {
                return null;
            }

            return new FreightCargoSpec
            {
                MassKgPerUnit = row.MassKg,
                VolumeM3PerUnit = row.VolumeLiters / 1000f,
            };
        }

        /// <summary>
        /// Builds a freight cargo spec from the item-spec registry, refusing
        /// loudly when the registry spec is missing or has an Unknown mass
        /// or volume. An Unknown measure is never read as its 0-valued
        /// accessor default: a zero-mass cargo would not bind in
        /// FreightCargoCapacity and would price an unbounded load. Returns
        /// null on success and a refusal reason on failure; the caller owns
        /// the refusal. This is the seam F3's consolidated shipment uses.
        /// </summary>
        public static string TryBuildFreightSpec(string itemId, out FreightCargoSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return "FurnitureCargoSpecs: a freight spec needs a non-empty item id.";
            }

            if (!PhysicalItemSpecRegistry.TryGet(itemId, out PhysicalItemSpec registered))
            {
                return $"FurnitureCargoSpecs: no registered item spec for '{itemId}'; no cargo spec can be built.";
            }

            if (!registered.UnitMassKg.IsKnown)
            {
                return $"FurnitureCargoSpecs: item '{itemId}' has an Unknown mass; an Unknown measure is never shipped as zero.";
            }

            if (!registered.UnitVolumeLiters.IsKnown)
            {
                return $"FurnitureCargoSpecs: item '{itemId}' has an Unknown volume; an Unknown measure is never shipped as zero.";
            }

            spec = new FreightCargoSpec
            {
                MassKgPerUnit = registered.UnitMassKg.Value,
                VolumeM3PerUnit = registered.UnitVolumeLiters.Value / 1000f,
            };
            return null;
        }

        /// <summary>
        /// Trip-math passthrough: how many units of this cargo fit one wagon.
        /// FreightCargoCapacity is the single trip-math authority.
        /// </summary>
        public static int MaxUnitsPerTrip(FreightCargoSpec cargo, EquipmentAsset wagon)
        {
            return FreightCargoCapacity.MaxUnitsPerTrip(cargo, wagon);
        }
    }
}
