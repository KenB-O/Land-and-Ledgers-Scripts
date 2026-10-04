using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// D3C: ore classes per Canon 21.7E — mine output preserves enough
    /// classification to make treatment and future economics matter:
    /// high-value/special lots, normal mill feed, lower-grade stockpile,
    /// material requiring different treatment, and waste. Classification is
    /// parameterized (cutoffs are TUNING — the Canon marks exact geological
    /// implementation and period terminology a specialist research item), so
    /// no balance numbers are hardcoded here.
    /// </summary>
    public enum MineOreClass
    {
        Unspecified = 0,
        HighGradeSpecial = 1, // high-value/special lots — direct smelter feed
        NormalMillFeed = 2,   // ordinary treatable ore
        LowGradeStockpile = 3,// currently uneconomic — stockpile, may gain value later
        TreatmentSpecific = 4,// needs a different treatment route (declared by the caller)
        Waste = 5,            // not ore — never marketed
    }

    /// <summary>
    /// D3C: grade cutoffs for ore classification. All values are TUNING
    /// (calibration) — set by the caller/scenario, never balance-hardcoded.
    /// Grades use the lot's grade unit (oz/ton Au/Ag, pct-fe iron, btu-per-lb coal).
    /// </summary>
    [Serializable]
    public sealed class MineOreClassCutoffs
    {
        [SerializeField, Min(0)]
        private float highGradeThreshold = float.MaxValue;

        [SerializeField, Min(0)]
        private float millFeedThreshold;

        [SerializeField, Min(0)]
        private float lowGradeThreshold;

        public float HighGradeThreshold => Math.Max(0f, highGradeThreshold);
        public float MillFeedThreshold => Math.Max(0f, millFeedThreshold);
        public float LowGradeThreshold => Math.Max(0f, lowGradeThreshold);

        public MineOreClassCutoffs() { }

        public MineOreClassCutoffs(float highGradeThreshold, float millFeedThreshold, float lowGradeThreshold)
        {
            this.highGradeThreshold = Math.Max(0f, highGradeThreshold);
            this.millFeedThreshold = Math.Max(0f, millFeedThreshold);
            this.lowGradeThreshold = Math.Max(0f, lowGradeThreshold);
        }
    }

    /// <summary>
    /// D3C: classifies ore lots into Canon 21.7E classes. TreatmentSpecific is
    /// never inferred from grade — the caller declares it (metallurgical
    /// character, e.g. refractory/sulphide, is not a grade number).
    /// </summary>
    public static class MineOreClassifier
    {
        public static MineOreClass Classify(MineOreLot lot, MineOreClassCutoffs cutoffs,
            bool declaredTreatmentSpecific = false)
        {
            if (lot == null || cutoffs == null)
                return MineOreClass.Unspecified;
            if (declaredTreatmentSpecific)
                return MineOreClass.TreatmentSpecific;
            float grade = lot.GradeValue;
            if (grade >= cutoffs.HighGradeThreshold)
                return MineOreClass.HighGradeSpecial;
            if (grade >= cutoffs.MillFeedThreshold)
                return MineOreClass.NormalMillFeed;
            if (grade >= cutoffs.LowGradeThreshold)
                return MineOreClass.LowGradeStockpile;
            return MineOreClass.Waste;
        }

        /// <summary>Only non-waste classes may be marketed (shipped or sold).</summary>
        public static bool IsMarketable(MineOreClass oreClass)
        {
            return oreClass is MineOreClass.HighGradeSpecial
                or MineOreClass.NormalMillFeed
                or MineOreClass.LowGradeStockpile
                or MineOreClass.TreatmentSpecific;
        }

        public static string GetDisplayName(MineOreClass oreClass)
        {
            return oreClass switch
            {
                MineOreClass.HighGradeSpecial => "High-grade / special",
                MineOreClass.NormalMillFeed => "Mill feed",
                MineOreClass.LowGradeStockpile => "Low-grade stockpile",
                MineOreClass.TreatmentSpecific => "Treatment-specific",
                MineOreClass.Waste => "Waste",
                _ => "Unclassified",
            };
        }
    }

    /// <summary>
    /// D3C: the grade picture of one shipment, captured at placement from the
    /// FIFO dispense lines (Canon 21.7H: value depends on expected recovery
    /// and on confidence in the sample/assay). Assayed tons carry measured
    /// grades; unassayed tons carry estimates at lower confidence. Persisted
    /// on the order as the settlement audit trail.
    /// </summary>
    [Serializable]
    public sealed class MineShipmentGradeSummary
    {
        [SerializeField, Min(0)]
        private int tonsAssayed;

        [SerializeField, Min(0)]
        private float weightedAssayedGrade;

        [SerializeField, Min(0)]
        private int tonsUnassayed;

        [SerializeField, Min(0)]
        private float weightedEstimateGrade;

        public int TonsAssayed => Math.Max(0, tonsAssayed);
        public float WeightedAssayedGrade => Math.Max(0f, weightedAssayedGrade);
        public int TonsUnassayed => Math.Max(0, tonsUnassayed);
        public float WeightedEstimateGrade => Math.Max(0f, weightedEstimateGrade);
        public int TotalTons => TonsAssayed + TonsUnassayed;

        /// <summary>Share of tons with measured (assayed) grades — the confidence signal.</summary>
        public float AssayCoverage01 => TotalTons <= 0 ? 0f : Mathf.Clamp01((float)TonsAssayed / TotalTons);

        public MineShipmentGradeSummary() { }

        public MineShipmentGradeSummary(int tonsAssayed, float weightedAssayedGrade,
            int tonsUnassayed, float weightedEstimateGrade)
        {
            this.tonsAssayed = Math.Max(0, tonsAssayed);
            this.weightedAssayedGrade = Math.Max(0f, weightedAssayedGrade);
            this.tonsUnassayed = Math.Max(0, tonsUnassayed);
            this.weightedEstimateGrade = Math.Max(0f, weightedEstimateGrade);
        }

        /// <summary>
        /// Builds the summary from dispense lines against a lot lookup
        /// snapshotted BEFORE dispense (dispense removes emptied lots).
        /// </summary>
        public static MineShipmentGradeSummary Compute(
            List<MineOreDispenseLine> lines, Func<EntityId, MineOreLot> lotLookup)
        {
            long assayedTonGrades = 0;
            long estimateTonGrades = 0;
            int assayedTons = 0;
            int unassayedTons = 0;
            if (lines != null)
            {
                foreach (MineOreDispenseLine line in lines)
                {
                    if (line == null || line.TonsTaken <= 0)
                        continue;
                    MineOreLot lot = lotLookup != null ? lotLookup(line.LotId) : null;
                    if (lot == null)
                    {
                        // Lot left the stockpile without a snapshot — count its
                        // tons as unassayed with zero grade rather than inventing.
                        unassayedTons += line.TonsTaken;
                        continue;
                    }
                    if (lot.Assayed)
                    {
                        assayedTons += line.TonsTaken;
                        assayedTonGrades += (long)Math.Round(line.TonsTaken * (double)lot.GradeValue * 1000.0);
                    }
                    else
                    {
                        unassayedTons += line.TonsTaken;
                        estimateTonGrades += (long)Math.Round(line.TonsTaken * (double)lot.GradeValue * 1000.0);
                    }
                }
            }
            float assayedGrade = assayedTons > 0 ? assayedTonGrades / 1000f / assayedTons : 0f;
            float estimateGrade = unassayedTons > 0 ? estimateTonGrades / 1000f / unassayedTons : 0f;
            return new MineShipmentGradeSummary(assayedTons, assayedGrade, unassayedTons, estimateGrade);
        }

        public MineShipmentGradeSummarySaveDto CaptureSaveDto()
        {
            return new MineShipmentGradeSummarySaveDto
            {
                tonsAssayed = TonsAssayed,
                weightedAssayedGrade = WeightedAssayedGrade,
                tonsUnassayed = TonsUnassayed,
                weightedEstimateGrade = WeightedEstimateGrade,
            };
        }

        public static MineShipmentGradeSummary FromSaveDto(MineShipmentGradeSummarySaveDto dto)
        {
            if (dto == null)
                return new MineShipmentGradeSummary();
            return new MineShipmentGradeSummary(dto.tonsAssayed, dto.weightedAssayedGrade,
                dto.tonsUnassayed, dto.weightedEstimateGrade);
        }
    }

    /// <summary>D3C: save DTO for the shipment grade summary. Owned by the mine runtime.</summary>
    [Serializable]
    public sealed class MineShipmentGradeSummarySaveDto
    {
        public int tonsAssayed;
        public float weightedAssayedGrade;
        public int tonsUnassayed;
        public float weightedEstimateGrade;
    }

    /// <summary>
    /// D3C: optional grade-aware settlement terms on a smelter link (Canon
    /// 21.7H: "Value depends on expected recovery minus treatment/freight
    /// and on confidence in the sample/assay"). Null terms on a link keep the
    /// legacy flat per-ton price. Every field is TUNING (calibration) — the
    /// Canon keeps exact Black Hills ore-buying/settlement arrangements a
    /// targeted research hold, so no historical contract terms are invented.
    /// </summary>
    [Serializable]
    public sealed class MineSmelterSettlementTerms
    {
        [SerializeField, Min(0)]
        private int treatmentChargePerTonCents;

        [SerializeField, Min(0)]
        private int freightPerTonCents;

        /// <summary>Fraction of contained metal value the smelter pays for.</summary>
        [SerializeField, Range(0f, 1f)]
        private float recoveryRate01 = 1f;

        /// <summary>Cents per grade-unit per ton (e.g. cents per oz/ton).</summary>
        [SerializeField, Min(0)]
        private int metalPricePerGradeUnitCents;

        /// <summary>Discount applied to estimated (unassayed) grades — the confidence haircut.</summary>
        [SerializeField, Range(0f, 1f)]
        private float unassayedDiscount01 = 1f;

        [SerializeField, TextArea(1, 2)]
        private string termsNote = string.Empty;

        public int TreatmentChargePerTonCents => Math.Max(0, treatmentChargePerTonCents);
        public int FreightPerTonCents => Math.Max(0, freightPerTonCents);
        public float RecoveryRate01 => Mathf.Clamp01(recoveryRate01);
        public int MetalPricePerGradeUnitCents => Math.Max(0, metalPricePerGradeUnitCents);
        public float UnassayedDiscount01 => Mathf.Clamp01(unassayedDiscount01);
        public string TermsNote => termsNote ?? string.Empty;

        public MineSmelterSettlementTerms() { }

        public MineSmelterSettlementTerms(int treatmentChargePerTonCents, int freightPerTonCents,
            float recoveryRate01, int metalPricePerGradeUnitCents, float unassayedDiscount01, string termsNote)
        {
            this.treatmentChargePerTonCents = Math.Max(0, treatmentChargePerTonCents);
            this.freightPerTonCents = Math.Max(0, freightPerTonCents);
            this.recoveryRate01 = Mathf.Clamp01(recoveryRate01);
            this.metalPricePerGradeUnitCents = Math.Max(0, metalPricePerGradeUnitCents);
            this.unassayedDiscount01 = Mathf.Clamp01(unassayedDiscount01);
            this.termsNote = termsNote ?? string.Empty;
        }

        /// <summary>
        /// Settles one shipment: gross recovery value minus treatment and
        /// freight. Net is floored at zero — the smelter never invoices the
        /// mine through this path (a negative settlement is a refusal with a
        /// diagnostic, not a bill).
        /// </summary>
        public MineSettlementBreakdown ComputeSettlement(MineShipmentGradeSummary summary, int tons,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            tons = Math.Max(0, tons);
            double pricePerUnit = MetalPricePerGradeUnitCents;
            double recovery = RecoveryRate01;
            double gross = 0.0;
            if (summary != null)
            {
                gross += summary.TonsAssayed * (double)summary.WeightedAssayedGrade * pricePerUnit * recovery;
                gross += summary.TonsUnassayed * (double)summary.WeightedEstimateGrade
                    * pricePerUnit * recovery * UnassayedDiscount01;
            }
            long grossCents = (long)Math.Round(gross);
            long treatmentCents = (long)tons * TreatmentChargePerTonCents;
            long freightCents = (long)tons * FreightPerTonCents;
            long netCents = grossCents - treatmentCents - freightCents;
            if (netCents < 0)
            {
                diagnostics.Add("MineSmelterSettlementTerms.ComputeSettlement: treatment and freight exceed " +
                    $"recovery value ({grossCents}c gross vs {treatmentCents + freightCents}c charges) — net floored at zero, not invoiced.");
                netCents = 0;
            }
            return new MineSettlementBreakdown(tons, (int)Math.Min(grossCents, int.MaxValue),
                (int)Math.Min(treatmentCents, int.MaxValue), (int)Math.Min(freightCents, int.MaxValue),
                (int)Math.Min(netCents, int.MaxValue));
        }

        public MineSmelterSettlementTermsSaveDto CaptureSaveDto()
        {
            return new MineSmelterSettlementTermsSaveDto
            {
                treatmentChargePerTonCents = TreatmentChargePerTonCents,
                freightPerTonCents = FreightPerTonCents,
                recoveryRate01 = RecoveryRate01,
                metalPricePerGradeUnitCents = MetalPricePerGradeUnitCents,
                unassayedDiscount01 = UnassayedDiscount01,
                termsNote = TermsNote,
            };
        }

        public static MineSmelterSettlementTerms FromSaveDto(MineSmelterSettlementTermsSaveDto dto)
        {
            if (dto == null)
                return null;
            return new MineSmelterSettlementTerms(dto.treatmentChargePerTonCents, dto.freightPerTonCents,
                dto.recoveryRate01, dto.metalPricePerGradeUnitCents, dto.unassayedDiscount01, dto.termsNote);
        }
    }

    /// <summary>D3C: the settled arithmetic of one shipment — audit detail for the ledger memo.</summary>
    [Serializable]
    public sealed class MineSettlementBreakdown
    {
        public int Tons { get; }
        public int GrossCents { get; }
        public int TreatmentCents { get; }
        public int FreightCents { get; }
        public int NetCents { get; }

        public MineSettlementBreakdown(int tons, int grossCents, int treatmentCents, int freightCents, int netCents)
        {
            Tons = Math.Max(0, tons);
            GrossCents = Math.Max(0, grossCents);
            TreatmentCents = Math.Max(0, treatmentCents);
            FreightCents = Math.Max(0, freightCents);
            NetCents = Math.Max(0, netCents);
        }

        public string Describe()
        {
            return $"{Tons} tons: gross {GrossCents}c, treatment {TreatmentCents}c, freight {FreightCents}c, net {NetCents}c.";
        }
    }

    /// <summary>D3C: save DTO for smelter settlement terms. Owned by the mine runtime.</summary>
    [Serializable]
    public sealed class MineSmelterSettlementTermsSaveDto
    {
        public int treatmentChargePerTonCents;
        public int freightPerTonCents;
        public float recoveryRate01 = 1f;
        public int metalPricePerGradeUnitCents;
        public float unassayedDiscount01 = 1f;
        public string termsNote = string.Empty;
    }

    /// <summary>
    /// D3C: one aggregated shipment parcel (Canon 21.7H: "Aggregation can
    /// create value by combining economically compatible small lots into
    /// commercially useful shipments"). A parcel groups lot ids that share
    /// mineral kind and ore class; the parcel records the weighted grade and
    /// minimum grade confidence so the settlement keeps its provenance.
    /// Parcels are planning records — tons still leave the stockpile through
    /// the shipment service's FIFO dispense.
    /// </summary>
    [Serializable]
    public sealed class MineShipmentParcel
    {
        [SerializeField]
        private string parcelId = string.Empty;

        [SerializeField]
        private MineralResourceKind mineralKind;

        [SerializeField]
        private MineOreClass oreClass = MineOreClass.Unspecified;

        [SerializeField]
        private List<string> lotIds = new List<string>();

        [SerializeField, Min(0)]
        private int totalTons;

        [SerializeField, Min(0)]
        private float weightedGrade;

        [SerializeField, Range(0f, 1f)]
        private float minGradeConfidence01 = 1f;

        public string ParcelId => parcelId ?? string.Empty;
        public MineralResourceKind MineralKind => mineralKind;
        public MineOreClass OreClass => oreClass;
        public IReadOnlyList<string> LotIds => lotIds;
        public int TotalTons => Math.Max(0, totalTons);
        public float WeightedGrade => Math.Max(0f, weightedGrade);
        public float MinGradeConfidence01 => Mathf.Clamp01(minGradeConfidence01);

        public MineShipmentParcel() { }

        /// <summary>
        /// Builds a parcel from stockpile lots. Refuses mixed mineral kinds,
        /// mixed ore classes, waste lots, and unknown lot ids loudly.
        /// </summary>
        public static MineShipmentParcel TryBuild(string parcelId, List<MineOreLot> lots,
            MineOreClassCutoffs cutoffs, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(parcelId))
            {
                diagnostics.Add("MineShipmentParcel.TryBuild: parcel id is required.");
                return null;
            }
            if (lots == null || lots.Count == 0)
            {
                diagnostics.Add("MineShipmentParcel.TryBuild: at least one lot is required.");
                return null;
            }
            if (cutoffs == null)
            {
                diagnostics.Add("MineShipmentParcel.TryBuild: ore-class cutoffs are required.");
                return null;
            }

            MineralResourceKind kind = lots[0].MineralKind;
            MineOreClass oreClass = MineOreClassifier.Classify(lots[0], cutoffs);
            if (!MineOreClassifier.IsMarketable(oreClass))
            {
                diagnostics.Add($"MineShipmentParcel.TryBuild: lot {lots[0].LotId.Id} classifies as {MineOreClassifier.GetDisplayName(oreClass)} — not parcelable.");
                return null;
            }

            var parcel = new MineShipmentParcel
            {
                parcelId = parcelId,
                mineralKind = kind,
                oreClass = oreClass,
            };
            double tonGrades = 0;
            foreach (MineOreLot lot in lots)
            {
                if (lot == null)
                {
                    diagnostics.Add("MineShipmentParcel.TryBuild: unknown lot id — parcels hold real lots only.");
                    return null;
                }
                if (lot.MineralKind != kind)
                {
                    diagnostics.Add($"MineShipmentParcel.TryBuild: lot {lot.LotId.Id} is {lot.MineralKind}, parcel is {kind} — mixed parcels refused.");
                    return null;
                }
                MineOreClass lotClass = MineOreClassifier.Classify(lot, cutoffs);
                if (lotClass != oreClass)
                {
                    diagnostics.Add($"MineShipmentParcel.TryBuild: lot {lot.LotId.Id} classifies as {MineOreClassifier.GetDisplayName(lotClass)}, parcel is {MineOreClassifier.GetDisplayName(oreClass)} — mixed classes refused.");
                    return null;
                }
                parcel.lotIds.Add(lot.LotId.ToString());
                parcel.totalTons += lot.Tons;
                tonGrades += lot.Tons * (double)lot.GradeValue;
                parcel.minGradeConfidence01 = Math.Min(parcel.minGradeConfidence01, lot.GradeConfidence01);
            }
            parcel.weightedGrade = parcel.totalTons > 0 ? (float)(tonGrades / parcel.totalTons) : 0f;
            return parcel;
        }
    }
}
