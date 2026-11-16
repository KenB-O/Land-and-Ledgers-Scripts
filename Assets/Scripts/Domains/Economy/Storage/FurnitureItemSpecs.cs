// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F0 (expanded campaign 2026-11-16): the first-wave
// furniture item specifications — the single authored table behind both the
// PhysicalItemSpec registry entries and the furniture cargo specs.
//
// Values are the plan's proposed numbers (plan sections 4.1 and 6.1),
// adopted under the standing delegation doctrine (O-F1: proceed on the
// proposed numbers; Kennedy corrects what he disagrees with). No price,
// no stock, no supplier and no production recipe lives here: this file is
// physical data only. Registering a spec creates no stock and moves no
// stock.
//
// Mass and volume are the plan's shipped-form figures (assembled furniture
// ships disassembled where that is historically normal: bed frame knocked
// down, mattress rolled). Dimensions are NOT in the plan; they are authored
// here as shipped-form bounding boxes whose box volume is at or just above
// the stated cargo volume, and are recorded as assumptions for the owner
// to correct. The registry stores volume in LITRES; the cargo spec uses
// cubic metres (1 L = 0.001 m3).
//
// Derivations (plan section 6.1):
// - bed, 40 kg / 0.50 m3: frame ~25 kg pine + straw mattress ~15 kg.
// - table, 25 kg / 0.35 m3: pine top + legs.
// - chair, 7 kg / 0.15 m3.
// - cabinet-small, 30 kg / 0.40 m3; cabinet-large, 45 kg / 0.60 m3.
// - shelf, 10 kg / 0.12 m3; crate (empty), 12 kg / 0.15 m3.
// - bedroll, 8 kg / 0.10 m3: rolled cloth + hay.
//
// Furniture ids (furniture.*) are NOT household-catalog ids, so the
// registry's catalog-agreement check does not apply to them; they are
// registered with Known mass/volume/dimensions directly, per the
// unknown-first rule's non-household path.

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Storage
{
    /// <summary>
    /// The first-wave furniture items (carpentry only, plan section 4.1).
    /// Stove (cast iron) and barrel (cooperage) are deliberately out of
    /// this wave; they are not carpentry products.
    /// </summary>
    public static class FurnitureItemSpecs
    {
        public const string BedId = "furniture.bed";
        public const string TableId = "furniture.table";
        public const string ChairId = "furniture.chair";
        public const string CabinetSmallId = "furniture.cabinet-small";
        public const string CabinetLargeId = "furniture.cabinet-large";
        public const string ShelfId = "furniture.shelf";
        public const string CrateId = "furniture.crate";
        public const string BedrollId = "furniture.bedroll";

        public static readonly string[] AllIds =
        {
            BedId, TableId, ChairId, CabinetSmallId, CabinetLargeId, ShelfId, CrateId, BedrollId,
        };

        /// <summary>
        /// One authored row of the furniture table. MassKg and VolumeLiters
        /// are the plan's proposed figures; LengthM/WidthM/HeightM are the
        /// shipped-form bounding box (see the file header). ContainerCompatible
        /// is a data flag only (cabinets, shelves and crates hold items);
        /// it carries no rule.
        /// </summary>
        public sealed class Row
        {
            public readonly string ItemId;
            public readonly string DisplayName;
            public readonly float MassKg;
            public readonly float VolumeLiters;
            public readonly float LengthM;
            public readonly float WidthM;
            public readonly float HeightM;
            public readonly bool ContainerCompatible;
            public readonly string HandlingNote;

            public Row(
                string itemId,
                string displayName,
                float massKg,
                float volumeLiters,
                float lengthM,
                float widthM,
                float heightM,
                bool containerCompatible,
                string handlingNote)
            {
                ItemId = itemId ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                MassKg = massKg;
                VolumeLiters = volumeLiters;
                LengthM = lengthM;
                WidthM = widthM;
                HeightM = heightM;
                ContainerCompatible = containerCompatible;
                HandlingNote = handlingNote ?? string.Empty;
            }
        }

        /// <summary>
        /// The single authored table. Both the registry seed (SeedSpecs) and
        /// the cargo specs (FurnitureCargoSpecs) read this table, so the two
        /// representations cannot drift apart.
        /// </summary>
        public static readonly Row[] Rows =
        {
            // Bed: knocked-down frame bundle + rolled straw mattress.
            // Box 2.00 x 0.60 x 0.42 = 0.504 m3 against 0.50 m3 cargo volume.
            new Row(BedId, "Bed", 40f, 500f, 2.00f, 0.60f, 0.42f,
                containerCompatible: false,
                "Bulky. Ships knocked down with the mattress rolled."),
            // Table: pine top + legs, shipped flat-ish.
            // Box 1.50 x 0.90 x 0.26 = 0.351 m3 against 0.35 m3 cargo volume.
            new Row(TableId, "Table", 25f, 350f, 1.50f, 0.90f, 0.26f,
                containerCompatible: false,
                "Flat top with legs; keep the top face protected."),
            // Chair: 0.50 x 0.50 x 0.60 = 0.15 m3 exactly.
            new Row(ChairId, "Chair", 7f, 150f, 0.50f, 0.50f, 0.60f,
                containerCompatible: false,
                "Stackable with like chairs where the joinery allows."),
            // Small cabinet: 1.00 x 0.50 x 0.80 = 0.40 m3 exactly.
            new Row(CabinetSmallId, "Small Cabinet", 30f, 400f, 1.00f, 0.50f, 0.80f,
                containerCompatible: true,
                "Closed storage; contents travel inside once installed, never loose."),
            // Large cabinet: 1.50 x 0.50 x 0.80 = 0.60 m3 exactly.
            new Row(CabinetLargeId, "Large Cabinet", 45f, 600f, 1.50f, 0.50f, 0.80f,
                containerCompatible: true,
                "Closed storage; contents travel inside once installed, never loose."),
            // Shelf: 1.00 x 0.30 x 0.40 = 0.12 m3 exactly.
            new Row(ShelfId, "Shelf", 10f, 120f, 1.00f, 0.30f, 0.40f,
                containerCompatible: true,
                "Open display storage; contents shown only when present."),
            // Crate (empty): 0.60 x 0.50 x 0.50 = 0.15 m3 exactly.
            new Row(CrateId, "Crate", 12f, 150f, 0.60f, 0.50f, 0.50f,
                containerCompatible: true,
                "Empty crate; a loaded crate's mass is the crate plus its contents."),
            // Bedroll: rolled cloth + hay, 2.00 x 0.25 x 0.20 = 0.10 m3 exactly.
            new Row(BedrollId, "Bedroll", 8f, 100f, 2.00f, 0.25f, 0.20f,
                containerCompatible: false,
                "Rolled; keep dry."),
        };

        public static bool TryGetRow(string itemId, out Row row)
        {
            row = null;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            for (int i = 0; i < Rows.Length; i++)
            {
                if (string.Equals(Rows[i].ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    row = Rows[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Registry seed rows: Known mass/volume/dimensions for every
        /// first-wave furniture item. Called by
        /// PhysicalItemSpecRegistry.SeedSpecs; the registry stays the one
        /// and only spec registry.
        /// </summary>
        public static PhysicalItemSpec[] SeedSpecs()
        {
            var specs = new List<PhysicalItemSpec>(Rows.Length);
            for (int i = 0; i < Rows.Length; i++)
            {
                Row row = Rows[i];
                specs.Add(new PhysicalItemSpec(
                    row.ItemId,
                    row.DisplayName,
                    "each",
                    PhysicalMeasure.Known(row.MassKg),
                    PhysicalMeasure.Known(row.VolumeLiters),
                    PhysicalDimensions.Known(row.LengthM, row.WidthM, row.HeightM),
                    row.ContainerCompatible,
                    perishable: false,
                    row.HandlingNote));
            }

            return specs.ToArray();
        }
    }
}
