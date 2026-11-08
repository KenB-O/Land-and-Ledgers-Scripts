// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F1 — physical item specifications (P9 plan section 2.3,
// section 4 Slice F1). Plain data: what one unit of an item physically is
// (mass, volume, dimensions). This registry is DATA ONLY. It is not a lot,
// not a container, not a price, not a package size, not a meal-eligibility
// ruling, and not a production recipe. Registering a spec creates no stock,
// moves no stock, and changes no price. Nothing reads this registry yet:
// no behaviour change to any flow in this slice.
//
// Unknown-first rule: every physical field starts Unknown until real
// authored data is supplied. An Unknown measure is NEVER silently treated
// as zero — callers must check IsKnown (or TryGetValue) before using Value.
// No universal per-unit mass exists here: the 1 kg FreightCargoSpec default
// and every caller-set cargo mass remain caller parameters, not item truth
// (see P9 plan section 2.3 and the Slice 1 checkpoint entry).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Storage
{
    /// <summary>
    /// One physical measure (a mass, a volume, or a length). Mirrors the
    /// shape of <see cref="StorageCapacityLimit"/>: a measure remains
    /// Unknown until real authored data is supplied. A Known value must be
    /// finite and non-negative; anything else becomes Unknown, never a
    /// stored number. Value returns 0 when Unknown, so it is only meaningful
    /// when <see cref="IsKnown"/> is true — use <see cref="TryGetValue"/>
    /// when the distinction matters.
    /// </summary>
    [Serializable]
    public struct PhysicalMeasure
    {
        [SerializeField] private bool isKnown;
        [SerializeField, Min(0f)] private float value;

        public bool IsKnown => isKnown && IsFinite(value) && value >= 0f;
        public float Value => IsKnown ? value : 0f;

        public PhysicalMeasure(bool known, float value)
        {
            isKnown = known;
            this.value = known && IsFinite(value) && value >= 0f ? value : 0f;
            if (known && (!IsFinite(value) || value < 0f)) isKnown = false;
        }

        public static PhysicalMeasure Unknown => new PhysicalMeasure(false, 0f);
        public static PhysicalMeasure Known(float value) => new PhysicalMeasure(true, value);

        /// <summary>Returns false without a value when this measure is Unknown.</summary>
        public bool TryGetValue(out float knownValue)
        {
            knownValue = IsKnown ? value : 0f;
            return IsKnown;
        }

        private static bool IsFinite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);
    }

    /// <summary>
    /// Physical dimensions of one unit: length, width, height. Unknown as a
    /// whole when any axis is Unknown — a partially known box is not a known
    /// box, so no dimensional fit may be computed from it.
    /// </summary>
    [Serializable]
    public struct PhysicalDimensions
    {
        [SerializeField] private PhysicalMeasure length;
        [SerializeField] private PhysicalMeasure width;
        [SerializeField] private PhysicalMeasure height;

        public PhysicalMeasure Length => length;
        public PhysicalMeasure Width => width;
        public PhysicalMeasure Height => height;

        public bool IsKnown => length.IsKnown && width.IsKnown && height.IsKnown;

        public PhysicalDimensions(PhysicalMeasure length, PhysicalMeasure width, PhysicalMeasure height)
        {
            this.length = length;
            this.width = width;
            this.height = height;
        }

        public static PhysicalDimensions Unknown =>
            new PhysicalDimensions(PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown);

        public static PhysicalDimensions Known(float lengthMeters, float widthMeters, float heightMeters) =>
            new PhysicalDimensions(
                PhysicalMeasure.Known(lengthMeters),
                PhysicalMeasure.Known(widthMeters),
                PhysicalMeasure.Known(heightMeters));
    }

    /// <summary>
    /// The physical specification of one unit of an item: what the unit is
    /// called, what one unit physically weighs and occupies, and two data
    /// flags. ContainerCompatible and Perishable are data flags only — they
    /// carry no rule and no flow reads them. HandlingNote is free text, never
    /// a rule. There is deliberately no package size, no meal eligibility,
    /// and no price on a spec (owner decisions D-5/D-6 stay open).
    /// </summary>
    [Serializable]
    public sealed class PhysicalItemSpec
    {
        public PhysicalItemSpec(
            string itemId,
            string displayName,
            string unitLabel,
            PhysicalMeasure unitMassKg,
            PhysicalMeasure unitVolumeLiters,
            PhysicalDimensions dimensionsMeters,
            bool containerCompatible,
            bool perishable,
            string handlingNote)
        {
            ItemId = itemId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            UnitLabel = unitLabel ?? string.Empty;
            UnitMassKg = unitMassKg;
            UnitVolumeLiters = unitVolumeLiters;
            DimensionsMeters = dimensionsMeters;
            ContainerCompatible = containerCompatible;
            Perishable = perishable;
            HandlingNote = handlingNote ?? string.Empty;
        }

        public string ItemId { get; }
        public string DisplayName { get; }
        public string UnitLabel { get; }
        public PhysicalMeasure UnitMassKg { get; }
        public PhysicalMeasure UnitVolumeLiters { get; }
        public PhysicalDimensions DimensionsMeters { get; }
        public bool ContainerCompatible { get; }
        public bool Perishable { get; }
        public string HandlingNote { get; }
    }

    /// <summary>
    /// Append-only registry of physical item specifications. Mirrors the
    /// registration discipline of
    /// <see cref="HouseholdItemCatalog.RegisterRuntimeItem"/>: new ids are
    /// added, never renamed; an existing id can never be re-registered with
    /// a different spec (refusal, never overwrite); an identical
    /// re-registration is idempotent. A spec whose ItemId is a household item
    /// id must agree with the household catalog on UnitLabel (and on
    /// DisplayName where both exist) or registration is refused. A spec may
    /// exist for a non-household item id. The registry is data, not authority
    /// over stock: registering a spec creates no stock, moves no stock, and
    /// changes no price.
    ///
    /// Register returns a diagnostic on refusal, null on success (including
    /// an identical re-registration).
    /// </summary>
    public static class PhysicalItemSpecRegistry
    {
        private static readonly Dictionary<string, PhysicalItemSpec> ById =
            new Dictionary<string, PhysicalItemSpec>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<PhysicalItemSpec> Ordered = new List<PhysicalItemSpec>();

        static PhysicalItemSpecRegistry()
        {
            foreach (PhysicalItemSpec seed in SeedSpecs())
            {
                string refusal = Register(seed);
                if (refusal != null)
                {
                    throw new InvalidOperationException(
                        "Physical item seed spec refused: " + refusal);
                }
            }
        }

        public static int Count => ById.Count;

        /// <summary>Read view of every registered spec, in registration order.</summary>
        public static IReadOnlyList<PhysicalItemSpec> All => Ordered.AsReadOnly();

        public static bool TryGet(string itemId, out PhysicalItemSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            return ById.TryGetValue(itemId, out spec);
        }

        public static string Register(PhysicalItemSpec spec)
        {
            if (spec == null)
            {
                return "Physical item spec is null.";
            }

            if (string.IsNullOrWhiteSpace(spec.ItemId))
            {
                return "Physical item spec needs a non-empty item id.";
            }

            if (ById.TryGetValue(spec.ItemId, out PhysicalItemSpec existing))
            {
                if (SpecsEqual(existing, spec))
                {
                    return null;
                }

                return $"Physical item '{spec.ItemId}' is already registered with a different spec and cannot be overwritten.";
            }

            HouseholdItemDefinition catalog = HouseholdItemCatalog.Get(spec.ItemId);
            if (catalog != null)
            {
                if (!string.Equals(spec.UnitLabel, catalog.UnitLabel, StringComparison.Ordinal))
                {
                    return $"Physical item '{spec.ItemId}' disagrees with the household catalog on UnitLabel: " +
                        $"spec says '{spec.UnitLabel}', catalog says '{catalog.UnitLabel}'.";
                }

                if (!string.IsNullOrWhiteSpace(spec.DisplayName)
                    && !string.Equals(spec.DisplayName, catalog.DisplayName, StringComparison.Ordinal))
                {
                    return $"Physical item '{spec.ItemId}' disagrees with the household catalog on DisplayName: " +
                        $"spec says '{spec.DisplayName}', catalog says '{catalog.DisplayName}'.";
                }
            }

            ById[spec.ItemId] = spec;
            Ordered.Add(spec);
            return null;
        }

        private static bool SpecsEqual(PhysicalItemSpec left, PhysicalItemSpec right)
        {
            return string.Equals(left.ItemId, right.ItemId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
                && string.Equals(left.UnitLabel, right.UnitLabel, StringComparison.Ordinal)
                && left.UnitMassKg.Equals(right.UnitMassKg)
                && left.UnitVolumeLiters.Equals(right.UnitVolumeLiters)
                && left.DimensionsMeters.Equals(right.DimensionsMeters)
                && left.ContainerCompatible == right.ContainerCompatible
                && left.Perishable == right.Perishable
                && string.Equals(left.HandlingNote, right.HandlingNote, StringComparison.Ordinal);
        }

        /// <summary>
        /// One Unknown seed row: identity from the naming source, every
        /// physical field Unknown, flags at their unpopulated default (false
        /// is "not recorded", never an authored ruling), no handling note.
        /// </summary>
        private static PhysicalItemSpec UnknownSpec(string itemId, string displayName, string unitLabel) =>
            new PhysicalItemSpec(
                itemId, displayName, unitLabel,
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalDimensions.Unknown,
                containerCompatible: false, perishable: false, handlingNote: string.Empty);

        private static PhysicalItemSpec[] SeedSpecs()
        {
            return new[]
            {
                // Household catalog ids (HouseholdInventory.cs Definitions),
                // identity copied verbatim from the catalog. Every physical
                // field Unknown: no authored mass in source.
                UnknownSpec("flour", "Flour", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("bread", "Bread", "loaf"), // Unknown: no authored mass in source.
                UnknownSpec("potatoes", "Potatoes", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("preserved_food", "Preserved Food", "jar"), // Unknown: no authored mass in source.
                UnknownSpec("preserved_meat", "Preserved Meat", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("fuel_wood", "Fuel Wood", "bundle"), // Unknown: no authored mass in source.
                UnknownSpec("soap", "Soap", "bar"), // Unknown: no authored mass in source.
                UnknownSpec("clothing_textiles", "Clothing/Textiles", "yd"), // Unknown: no authored mass in source.
                UnknownSpec("salt", "Salt", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("sugar", "Sugar", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("coffee", "Coffee", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("tea", "Tea", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("candle", "Candle", "each"), // Unknown: no authored mass in source.
                UnknownSpec("lamp_oil", "Lamp Oil", "gal"), // Unknown: no authored mass in source.
                UnknownSpec("cookware", "Cookware", "each"), // Unknown: no authored mass in source.
                UnknownSpec("bedding", "Bedding", "set"), // Unknown: no authored mass in source.
                UnknownSpec("tool_hand", "Hand Tool", "each"), // Unknown: no authored mass in source.
                UnknownSpec("tableware", "Tableware", "set"), // Unknown: no authored mass in source.
                UnknownSpec("cigarettes", "Cigarettes", "pack"), // Unknown: no authored mass in source.
                UnknownSpec("tobacco", "Tobacco", "lb"), // Unknown: no authored mass in source.
                UnknownSpec("whiskey", "Whiskey", "bottle"), // Unknown: no authored mass in source.
                UnknownSpec("book", "Book", "each"), // Unknown: no authored mass in source.
                UnknownSpec("jewelry", "Jewelry", "each"), // Unknown: no authored mass in source.
                UnknownSpec("perfume", "Perfume", "bottle"), // Unknown: no authored mass in source.
                UnknownSpec("chocolate", "Chocolate", "bar"), // Unknown: no authored mass in source.
                UnknownSpec("silk_cloth", "Silk Cloth", "yd"), // Unknown: no authored mass in source.
                UnknownSpec("staple_food_unspecified", "Staple Food (composition unspecified)", "unit"), // Unknown: no authored mass in source.
                UnknownSpec("meat_unspecified", "Meat (category-level, composition unspecified)", "unit"), // Unknown: no authored mass in source.
                UnknownSpec("household_goods_unspecified", "Household Goods (category-level, item unspecified)", "unit"), // Unknown: no authored mass in source.
                UnknownSpec("clothing_unspecified", "Clothing (category-level, item unspecified)", "unit"), // Unknown: no authored mass in source.
                // Non-household ids the tree already names as tangible items.
                // Unit "lb" from BakeryBreadCatalog.FlourItemId ("bakery-flour", lb).
                UnknownSpec("bakery-flour", "Flour", "lb"), // Unknown: no authored mass in source.
                // No authored display name or unit label in source; "unit" is a
                // neutral identity label, not a physical claim.
                UnknownSpec("bakery-notions", "Bakery Notions", "unit"), // Unknown: no authored mass in source.
                // U-1 (F1 decision): seeded Unknown like everything else. The
                // provisional-exception branch is NOT taken in this slice, per
                // the Slice 1 directive. See the checkpoint entry.
                UnknownSpec("bakery_flour_sack", "Flour", "sack"), // Unknown (U-1): no adopted mass in this slice.
                // Unit "unit" per Review2ItemSkuMap ("bakery-fuelwood
                // (cordwood units)"); no authored mass in source.
                UnknownSpec("bakery-fuelwood", "Bakery Fuelwood", "unit"), // Unknown: no authored mass in source.
            };
        }
    }
}
