// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F6 (expanded campaign 2026-11-16): property-contents
// valuation + the fixture/structure split Agent D's contents slices
// (D-6/D-7) consume as an interface.
//
// What this file is:
// - ReplacementCostCentsForKind: the replacement cost of one NEW item of
//   a kind — the F2 authored retail baselines (GeneralStoreBusinessDefinition
//   .asset baselineCents per SKU), mirrored here in code because the
//   harness has no asset access (the RegionalItemCategories precedent: the
//   code-side category table is copied from the asset for the same reason).
//   Verified against the asset on disk: bed 300, table 225, chair 125,
//   cabinet-small 325, cabinet-large 425, shelf 80, crate 75, bedroll 60.
// - UsedValueCents: the condition-weighted used value (Canon 11.6, the
//   same shape as BusinessEquipmentRegister.TransferableValueCents and
//   B6's PropertyContentsSelection.ValueCentsFor): round(replacement x
//   condition); 0 when unusable (condition at or below 0.05) or unpriced.
//   Implemented by delegating to B6's ValueCentsFor with this file's price
//   func, so a selection built with F6's prices and F6's used values can
//   never drift apart cent for cent.
// - SplitStructureAndContents: the fixture/structure split. The deal's
//   structural estimate is the as-standing estimate (it includes the value
//   of the fixed shelving, stove and counters already in the building);
//   the split returns structure-only = estimate MINUS the agreed value of
//   included FIXTURE items, so fixtures are counted once (in contents),
//   never twice. Movables never enter structural value at any point.
//   The result is a NEGOTIATION ANCHOR (AnchorTotalCents = structure-only
//   + contents), not an agreed price: the agreed price is the outcome of
//   the bounded-round negotiation (D-8), and this API takes no price in
//   and produces no agreed price out.
//
// What this file does NOT do (corrections to the plan's section 8, read
// against the real code before writing):
// - No salvage floor. The plan said "never below salvage value
//   (UsedEquipmentSalvage)", but UsedEquipmentSalvage records scrap /
//   part-out sales at RECORDED agreed prices — "this ledger never values
//   scrap" — and the one calibrated scrap fraction in the codebase (0.10
//   of acquisition cost) belongs to the wagon-only D3A dismantle path,
//   which the salvage ledger explicitly does not duplicate. There is no
//   computed salvage value for furniture anywhere, so none is invented:
//   a wrecked asset values at 0c, never negative, and is a salvage
//   candidate (a negotiated sale to a named buyer) rather than a valued
//   contents item.
// - No age adjustment. EquipmentAsset carries MadeDayIndex, but no
//   depreciation schedule exists anywhere in the codebase, and none is
//   invented: two assets of the same kind and condition value alike
//   whatever their age, and the derivation says so.
// - No stove / kitchen-counter / barrel price. Those kinds are out of the
//   carpentry wave (iron, built-in, cooperage) and no legitimate price
//   exists for them yet — the iron/cooperage lines or an authored regional
//   offer price them later. They are unpriced (0c) here, never guessed:
//   B6's contract records an unpriced included item and refuses it at
//   agreement (requirePriced), so a deal cannot silently carry one.

using System;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// F6: the result of the fixture/structure split. A negotiation
    /// anchor, never an agreed price — the agreed price comes from the
    /// negotiation (D-8) and is recorded there.
    /// </summary>
    public sealed class StructureContentsSplit
    {
        /// <summary>False when the split could not be computed — see Problem. Never use the numbers then.</summary>
        public bool IsValid;
        /// <summary>Why the split is invalid (empty when valid).</summary>
        public string Problem = string.Empty;
        /// <summary>The structural estimate minus the agreed value of included fixtures.</summary>
        public int StructureOnlyCents;
        /// <summary>The selection's frozen agreed contents value (TotalContentsValueCents).</summary>
        public int ContentsValueCents;
        /// <summary>Agreed value of included fixtures (counted once, in contents).</summary>
        public int IncludedFixtureValueCents;
        /// <summary>Agreed value of included movables (never enters structure).</summary>
        public int IncludedMovableValueCents;
        /// <summary>The negotiation anchor: structure-only + contents. Equals the structural estimate plus the included movable value.</summary>
        public int AnchorTotalCents;
        /// <summary>How every number above was derived, in plain language.</summary>
        public string Derivation = string.Empty;
    }

    /// <summary>
    /// F6: property-contents valuation. Prices come from F2's authored
    /// retail baselines; the condition weighting is Canon 11.6; the split
    /// is what D-6/D-7 pass as the structure value. Nothing here invents a
    /// price, a supplier, or a depreciation schedule.
    /// </summary>
    public static class FurnitureValuation
    {
        /// <summary>
        /// F6: the replacement cost of one NEW item of the kind, in cents —
        /// the F2 authored retail baseline for the kind. 0 for every kind
        /// with no authored baseline (stove, kitchen counter, barrel,
        /// unknown kinds): unpriced, never guessed. The caller-supplied
        /// price func B6's contract takes is this method.
        /// </summary>
        public static int ReplacementCostCentsForKind(string itemKind)
        {
            if (string.IsNullOrWhiteSpace(itemKind)) return 0;
            switch (itemKind)
            {
                case FurnishingClassification.BedKind: return 300;
                case FurnishingClassification.TableKind: return 225;
                case FurnishingClassification.ChairKind: return 125;
                case FurnishingClassification.CabinetSmallKind: return 325;
                case FurnishingClassification.CabinetLargeKind: return 425;
                case FurnishingClassification.ShelfKind: return 80;
                case FurnishingClassification.CrateKind: return 75;
                case FurnishingClassification.BedrollKind: return 60;
                default: return 0;
            }
        }

        /// <summary>
        /// F6: the replacement value of one asset — its kind's baseline, or
        /// 0c when the kind is unpriced. A null asset has no value.
        /// </summary>
        public static int ReplacementValueCents(EquipmentAsset asset)
        {
            if (asset == null) return 0;
            return ReplacementCostCentsForKind(asset.Kind);
        }

        /// <summary>
        /// F6: the condition-weighted used value of one asset (Canon 11.6).
        /// Delegates to B6's ValueCentsFor with this file's price func, so
        /// the selection's agreed values and this value agree by
        /// construction: 0c when unusable (condition at or below 0.05) or
        /// unpriced, otherwise round(replacement x condition).
        /// </summary>
        public static int UsedValueCents(EquipmentAsset asset)
        {
            if (asset == null) return 0;
            return UsedValueCents(asset.Kind, asset.Condition01);
        }

        /// <summary>
        /// F6: the condition-weighted used value of one item kind at one
        /// condition (Canon 11.6). 0c when unusable or unpriced, otherwise
        /// round(replacement x condition), with the same banker's rounding
        /// B6 uses (Math.Round) so the two never differ by a cent.
        /// </summary>
        public static int UsedValueCents(string itemKind, float condition01)
        {
            return PropertyContentsSelection.ValueCentsFor(
                itemKind, condition01, ReplacementCostCentsForKind, out bool _);
        }

        /// <summary>
        /// F6: the fixture/structure split D-6/D-7 consume.
        ///
        /// structuralEstimateCents is the as-standing structural estimate:
        /// the valuer's estimate of the structure WITH its fixed shelving,
        /// stove and counters still in it. The split returns structure-only
        /// = estimate MINUS the agreed value of included FIXTURE items, so
        /// fixtures are counted once (in contents), never twice. Movables
        /// never enter the structural number: the same estimate with
        /// movables added or removed prices the structure identically.
        ///
        /// The contents value is the selection's frozen agreed total
        /// (TotalContentsValueCents) — the split never re-values; staleness
        /// is Revalidate's job (D-6). The anchor total is structure-only +
        /// contents, which equals the estimate plus the included movable
        /// value.
        ///
        /// Refuses loudly (IsValid false, Problem set, all numbers 0)
        /// when there is no selection, the estimate is negative, or the
        /// estimate does not even cover the included fixtures — a structure
        /// estimate lower than its own fixed shelving is wrong data, and
        /// clamping it to zero would let a broken deal pass conservation
        /// with a wrong price.
        /// </summary>
        public static StructureContentsSplit SplitStructureAndContents(
            int structuralEstimateCents, PropertyContentsSelection selection)
        {
            var split = new StructureContentsSplit();
            if (selection == null)
            {
                split.Problem = "FurnitureValuation: no contents selection supplied — the split needs the itemized selection; a deal with no contents terms has no split.";
                return split;
            }
            if (structuralEstimateCents < 0)
            {
                split.Problem = $"FurnitureValuation: structural estimate {structuralEstimateCents}c is negative — an estimate cannot be below zero.";
                return split;
            }
            int fixtureValue = Math.Max(0, selection.IncludedFixtureValueCents);
            int movableValue = Math.Max(0, selection.IncludedMovableValueCents);
            int contentsValue = Math.Max(0, selection.TotalContentsValueCents);
            if (structuralEstimateCents < fixtureValue)
            {
                split.Problem = $"FurnitureValuation: structural estimate {structuralEstimateCents}c does not cover the included fixtures ({fixtureValue}c) — the estimate is wrong; re-value the structure before splitting.";
                return split;
            }
            split.StructureOnlyCents = structuralEstimateCents - fixtureValue;
            split.ContentsValueCents = contentsValue;
            split.IncludedFixtureValueCents = fixtureValue;
            split.IncludedMovableValueCents = movableValue;
            split.AnchorTotalCents = split.StructureOnlyCents + split.ContentsValueCents;
            split.IsValid = true;
            split.Derivation =
                $"Anchor: structure estimate {structuralEstimateCents}c minus included fixtures {fixtureValue}c " +
                $"= {split.StructureOnlyCents}c structure-only; contents {contentsValue}c " +
                $"({fixtureValue}c fixtures + {movableValue}c movables); anchor total {split.AnchorTotalCents}c " +
                $"= structure-only + contents. Fixtures counted once, in contents; movables never enter structure. " +
                "Values are condition-weighted replacement (Canon 11.6); no age adjustment — no depreciation " +
                "schedule exists. This is a negotiation anchor, not an agreed price.";
            return split;
        }
    }
}
