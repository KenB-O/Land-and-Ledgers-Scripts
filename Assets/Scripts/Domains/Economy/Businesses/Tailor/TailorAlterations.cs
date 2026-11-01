using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Tailor
{
    /// <summary>
    /// D1C: the lifecycle of an alteration order. Alterations are
    /// customer-owned garments (no cloth is ever reserved — the customer
    /// brings the garment), grounded in the Canon Part V tailor profile's
    /// "clothing service" alongside workwear and mending. Some alterations
    /// need a pinning fitting (a genuine step, like the W1C bespoke
    /// fittings); the order cannot pass it until
    /// <see cref="TailorShopRuntime.RecordPinningFitting"/> records the
    /// customer's visit.
    /// </summary>
    public enum TailorAlterationStage
    {
        Ordered = 0,
        Altered = 1,
        Delivered = 2,
        Cancelled = 3,
    }

    /// <summary>
    /// D1C: one alteration type as data — labor, fee, and notions. Alteration
    /// fees are flat service fees (not piece rates: there is no garment being
    /// made, and Canon Part XV holds the calibration). All quantities are
    /// TUNING.
    /// </summary>
    [Serializable]
    public sealed class TailorAlterationSpec
    {
        public string AlterationId = string.Empty;
        public string DisplayName = string.Empty;

        /// <summary>TUNING: hands-on labor minutes for the alteration.</summary>
        public int LaborMinutes;

        /// <summary>TUNING: flat service fee in cents.</summary>
        public int FeeCents;

        /// <summary>TUNING: notion units consumed (thread, buttons).</summary>
        public int NotionsUnits;

        /// <summary>True when the garment must be pinned on the customer before work starts.</summary>
        public bool NeedsPinningFitting;

        public TailorAlterationSpec() { }
    }

    /// <summary>
    /// D1C: the alteration catalog. Canon 4.7 holds: the sewing machine never
    /// gates tailoring — hand methods remain physically possible — so every
    /// alteration runs on the tailor's hand kit plus a cutting-table
    /// workstation for bench work, exactly like the W1C mending flow.
    /// </summary>
    public static class TailorAlterationCatalog
    {
        public const string HemId = "tailor.alt.hem";
        public const string ResizeWaistId = "tailor.alt.resize-waist";
        public const string ShortenSleevesId = "tailor.alt.sleeves";
        public const string ReplaceButtonsId = "tailor.alt.buttons";
        public const string RelineId = "tailor.alt.reline";

        /// <summary>TUNING: labor minutes per alteration type.</summary>
        public const int HemMinutes = 45;
        public const int ResizeWaistMinutes = 90;
        public const int ShortenSleevesMinutes = 60;
        public const int ReplaceButtonsMinutes = 30;
        public const int RelineMinutes = 150;

        /// <summary>TUNING: flat service fee (cents) per alteration type.</summary>
        public const int HemFeeCents = 35;
        public const int ResizeWaistFeeCents = 75;
        public const int ShortenSleevesFeeCents = 50;
        public const int ReplaceButtonsFeeCents = 25;
        public const int RelineFeeCents = 125;

        public static TailorAlterationSpec GetSpec(string alterationId)
        {
            if (string.Equals(alterationId, HemId, StringComparison.Ordinal))
                return new TailorAlterationSpec
                {
                    AlterationId = HemId, DisplayName = "Hem trousers or skirt",
                    LaborMinutes = HemMinutes, FeeCents = HemFeeCents, NotionsUnits = 0,
                    NeedsPinningFitting = false,
                };
            if (string.Equals(alterationId, ResizeWaistId, StringComparison.Ordinal))
                return new TailorAlterationSpec
                {
                    AlterationId = ResizeWaistId, DisplayName = "Take in / let out waist",
                    LaborMinutes = ResizeWaistMinutes, FeeCents = ResizeWaistFeeCents,
                    NotionsUnits = TailorGarmentCatalog.StandardNotionsUnits, NeedsPinningFitting = true,
                };
            if (string.Equals(alterationId, ShortenSleevesId, StringComparison.Ordinal))
                return new TailorAlterationSpec
                {
                    AlterationId = ShortenSleevesId, DisplayName = "Shorten sleeves",
                    LaborMinutes = ShortenSleevesMinutes, FeeCents = ShortenSleevesFeeCents, NotionsUnits = 0,
                    NeedsPinningFitting = false,
                };
            if (string.Equals(alterationId, ReplaceButtonsId, StringComparison.Ordinal))
                return new TailorAlterationSpec
                {
                    AlterationId = ReplaceButtonsId, DisplayName = "Replace buttons",
                    LaborMinutes = ReplaceButtonsMinutes, FeeCents = ReplaceButtonsFeeCents,
                    NotionsUnits = TailorGarmentCatalog.StandardNotionsUnits, NeedsPinningFitting = false,
                };
            if (string.Equals(alterationId, RelineId, StringComparison.Ordinal))
                return new TailorAlterationSpec
                {
                    AlterationId = RelineId, DisplayName = "Reline collar and cuffs",
                    LaborMinutes = RelineMinutes, FeeCents = RelineFeeCents,
                    NotionsUnits = TailorGarmentCatalog.StandardNotionsUnits, NeedsPinningFitting = true,
                };
            return new TailorAlterationSpec();
        }

        /// <summary>True for the five offered alteration types; false for anything else (unknown ids never schedule).</summary>
        public static bool IsKnownAlteration(string alterationId)
        {
            return !string.IsNullOrEmpty(GetSpec(alterationId).AlterationId);
        }
    }

    /// <summary>
    /// D1C: one alteration order. The garment is customer-owned: the shop
    /// holds NO cloth in custody, consumes only notions (dispensed with
    /// provenance), and charges the flat catalog fee. Unused notions on a
    /// cancelled order return to the shelf as a named lot — never silently
    /// absorbed.
    /// </summary>
    [Serializable]
    public sealed class TailorAlterationOrder
    {
        public string OrderId = string.Empty;
        public EntityId CustomerPersonId = EntityId.Invalid; // EntityKind.Person
        public string AlterationId = string.Empty; // tailor.alt.*
        public int OrderDayIndex;
        public TailorAlterationStage Stage = TailorAlterationStage.Ordered;

        public bool PinningFittingRecorded;
        public string PinningFittingNotes = string.Empty;

        /// <summary>Notions dispensed for this order, with provenance.</summary>
        public List<TailorClothDispenseLine> NotionsUsed = new List<TailorClothDispenseLine>();

        /// <summary>D1C: how many times the customer failed to come in for the pinning fitting. Data only — the Population domain owns customer behavior; the order stays parked.</summary>
        public int MissedFittings;

        public List<string> StageLog = new List<string>();

        public TailorAlterationOrder() { }

        public bool IsActive => Stage != TailorAlterationStage.Delivered && Stage != TailorAlterationStage.Cancelled;
    }

    /// <summary>
    /// D1C: one delivered alteration — the flat-fee record. The caller
    /// settles these records as ordinary ledger outflows; money moves only
    /// through ledger authorities.
    /// </summary>
    [Serializable]
    public sealed class TailorAlterationRecord
    {
        public string OrderId = string.Empty;
        public EntityId CustomerPersonId = EntityId.Invalid;
        public string AlterationId = string.Empty;
        public int DayIndex;
        public int FeeCents;
        public List<TailorClothDispenseLine> NotionsProvenance = new List<TailorClothDispenseLine>();

        public TailorAlterationRecord() { }
    }
}
