using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// D4L: the organized second-hand equipment market — the town's used-equipment
    /// commerce as real records. Canon Part VI §50 "Used equipment markets":
    /// "KEEP uncertain used-asset condition, inspection, refurbishment, operation,
    /// resale and salvage. Cheap used equipment can be a bargain or
    /// deferred-maintenance trap." Canon §6.5I: "Used-equipment markets can
    /// therefore offer cheaper assets with uncertain condition; the player can
    /// inspect, refurbish, operate, resell or salvage them." Canon §7.2F: a shop
    /// may buy broken or worn equipment, repair it and resell it, or dismantle
    /// uneconomic assets for parts/scrap — commerce that grows out of the
    /// maintenance and repair systems (D4K's repair queue feeds this market:
    /// repaired items re-enter circulation through listings).
    ///
    /// This file owns the market's shared authority: condition grades derived
    /// from the EXISTING condition model, listings as real records, price
    /// GUIDANCE (never auto-pricing), inspection-gated sales, title transfer
    /// with provenance intact, and payment through real cash lots. The canon
    /// describes no equipment auction — there is none here; do not invent one.
    ///
    /// Every rate, window, and threshold below is CALIBRATION (Canon Part XV) —
    /// tuning, not canon. The qualitative rules (condition-weighted guidance,
    /// seller-set prices, recorded-not-resolved disputes, real-lot payment)
    /// are the canon shape.
    /// </summary>

    // ================= condition grades =================

    /// <summary>
    /// D4L: the condition grade recorded on a listing — a DISPLAY taxonomy
    /// derived from the existing condition model, not a valuation table.
    /// Bands anchor on existing thresholds: 0.05 is EquipmentAsset.IsUsable's
    /// boundary, 0.50 is BusinessEquipmentRegister's worn threshold (Canon
    /// §11.5). Canon §52: condition affects resale through actual repair cost,
    /// productive capability, downtime and salvage — NOT a universal Poor
    /// Condition percentage — so grades never set prices; guidance uses the
    /// real condition value (Canon §11.6).
    /// </summary>
    public enum UsedEquipmentConditionGrade
    {
        Unspecified = 0,
        /// <summary>Near-new: condition at or above 0.80 (calibration).</summary>
        Sound = 1,
        /// <summary>Serviceable working equipment: 0.50 to below 0.80.</summary>
        Serviceable = 2,
        /// <summary>Worn but usable: above 0.05 to below 0.50.</summary>
        Worn = 3,
        /// <summary>At or below the usable threshold (0.05): salvage/part-out only.</summary>
        Wrecked = 4,
    }

    /// <summary>D4L: derives the recorded grade from the existing condition model.</summary>
    public static class UsedEquipmentConditionGrades
    {
        /// <summary>
        /// Calibration: the Sound/Serviceable boundary. The 0.05 and 0.50
        /// anchors are existing-model thresholds, not tuning.
        /// </summary>
        public const float SoundBoundary01 = 0.80f;

        public static UsedEquipmentConditionGrade GradeFor(float condition01)
        {
            float c = Mathf.Clamp01(condition01);
            if (c <= 0.05f) return UsedEquipmentConditionGrade.Wrecked;
            if (c < 0.50f) return UsedEquipmentConditionGrade.Worn;
            if (c < SoundBoundary01) return UsedEquipmentConditionGrade.Serviceable;
            return UsedEquipmentConditionGrade.Sound;
        }
    }

    // ================= price guidance =================

    /// <summary>
    /// D4L: price GUIDANCE for one asset — a starting reference computed from
    /// the EQP valuation effects, never an auto-price. The condition-weighted
    /// value follows BusinessEquipmentRegister.TransferableValueCents (Canon
    /// §11.6: floor = replacement cost x condition); the renewal gap follows
    /// the ReplacementNeedCents shape (Canon §11.5: (1 - condition) x cost).
    /// The seller sets the asking price; the guidance is recorded on the
    /// listing for transparency. Unknown kinds (no replacement cost supplied)
    /// yield NO guidance — loudly — never a synthetic number.
    /// </summary>
    [Serializable]
    public sealed class UsedEquipmentPriceGuidance
    {
        public bool HasGuidance;
        public int ReplacementCostCents;
        public float Condition01;
        public int ConditionWeightedValueCents;
        public int RenewalGapCents;
        public string BasisNote = string.Empty;

        public UsedEquipmentPriceGuidance() { }
    }

    /// <summary>D4L: computes price guidance from the existing valuation service.</summary>
    public static class UsedEquipmentPriceGuide
    {
        /// <summary>
        /// Guidance for one asset. replacementCostCentsForKind is the CALLER's
        /// real price data (dealer price list, import catalog) — never invented
        /// here. A non-positive cost means no guidance (recorded, never guessed).
        /// </summary>
        public static UsedEquipmentPriceGuidance GuidanceFor(
            EquipmentAsset asset, Func<string, int> replacementCostCentsForKind)
        {
            var guidance = new UsedEquipmentPriceGuidance();
            if (asset == null) return guidance;

            float condition = Mathf.Clamp01(asset.Condition01);
            guidance.Condition01 = condition;

            int cost = replacementCostCentsForKind != null
                ? Math.Max(0, replacementCostCentsForKind(asset.Kind)) : 0;
            if (cost <= 0)
            {
                guidance.BasisNote =
                    $"UsedEquipmentPriceGuide: no replacement price data for kind '{asset.Kind}' — " +
                    "guidance unavailable; the seller sets the price with no reference.";
                return guidance;
            }

            guidance.ReplacementCostCents = cost;
            guidance.ConditionWeightedValueCents = (int)Math.Round(cost * condition);
            guidance.RenewalGapCents = (int)Math.Round(cost * (1f - condition));
            guidance.HasGuidance = true;
            guidance.BasisNote = asset.IsUsable
                ? $"UsedEquipmentPriceGuide: condition-weighted floor {guidance.ConditionWeightedValueCents}c " +
                  $"(Canon §11.6: {cost}c x {condition:P0}); renewal gap ~{guidance.RenewalGapCents}c " +
                  "(Canon §11.5 shape). Guidance only — the seller sets the price."
                : $"UsedEquipmentPriceGuide: below usable condition — floor value ~0c; " +
                  "salvage/part-out pricing is whatever a scrapper agrees to pay, recorded on the sale.";
            return guidance;
        }
    }

    // ================= listing + sale records =================

    /// <summary>D4L: lifecycle of one used-equipment listing.</summary>
    public enum UsedEquipmentListingStatus
    {
        Unspecified = 0,
        Active = 1,    // on the market
        Sold = 2,      // title transferred through Purchase
        Withdrawn = 3, // seller pulled it (misgrade correction, kept, repaired...)
    }

    /// <summary>
    /// D4L: one used-equipment listing as a REAL record. Item identity comes
    /// from the existing equipment registries (the EquipmentAsset object the
    /// seller's BusinessEquipmentRegister holds — Tech X §3.3 persistent
    /// identity via AssetId). Condition is snapshotted at listing; the recorded
    /// grade is what the seller claims. Provenance rides along: maker,
    /// material lots, maintenance history (Canon §51: records are evidence for
    /// diligence), and the ownership chain (D4L provenance extension).
    /// </summary>
    [Serializable]
    public sealed class UsedEquipmentListing
    {
        public EntityId ListingId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public string AssetId = string.Empty;
        public string Kind = string.Empty;
        public string DisplayName = string.Empty;

        public float Condition01AtListing;
        public UsedEquipmentConditionGrade RecordedGrade = UsedEquipmentConditionGrade.Unspecified;

        public int AskingPriceCents;             // seller-set; guidance is advisory only
        public int PriceGuidanceCents;           // recorded guidance value (-1 = unavailable)
        public string PriceGuidanceNote = string.Empty;

        public string SellerKind = string.Empty; // "business", "household"
        public string SellerId = string.Empty;
        public string SellerName = string.Empty;
        public string LocationId = string.Empty;

        public int ListedDayIndex;
        public UsedEquipmentListingStatus Status = UsedEquipmentListingStatus.Active;

        // Diligence snapshots (Canon §51): evidence, not guarantees.
        public List<string> MaintenanceHistorySnapshot = new List<string>();
        public List<EquipmentOwnershipRecord> OwnershipSnapshot = new List<EquipmentOwnershipRecord>();
        public string MakerBusinessName = string.Empty;

        public int OpenDisputeCount;

        public UsedEquipmentListing() { }
    }

    /// <summary>
    /// D4L: one completed used-equipment sale — title transferred, payment
    /// recorded. Provenance stays intact: the asset's OwnershipHistory gains
    /// the transfer (prior owners as history).
    /// </summary>
    [Serializable]
    public sealed class UsedEquipmentSale
    {
        public EntityId SaleId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public EntityId ListingId = EntityId.Invalid;
        public string AssetId = string.Empty;
        public string Kind = string.Empty;
        public string DisplayName = string.Empty;

        public string SellerKind = string.Empty;
        public string SellerId = string.Empty;
        public string SellerName = string.Empty;
        public string BuyerKind = string.Empty; // "business", "household"
        public string BuyerId = string.Empty;
        public string BuyerName = string.Empty;

        public int PriceCents;
        public int DayIndex;
        public string PaymentMemo = string.Empty;

        public UsedEquipmentSale() { }
    }

    // ================= cash port: real lots =================

    /// <summary>
    /// D4L: party keys for the used-equipment cash port — stable strings naming
    /// the real cash lot a payment moves out of and into (D4H port pattern).
    /// </summary>
    public static class UsedEquipmentPartyKeys
    {
        public static string PersonKey(int personId) => "person:" + personId;

        public static string BusinessKey(string businessInstanceId) =>
            "business:" + (businessInstanceId ?? string.Empty);

        public static string ForOwner(string ownerKind, string ownerId)
        {
            if (string.Equals(ownerKind, "business", StringComparison.Ordinal))
                return BusinessKey(ownerId);
            if (int.TryParse(ownerId, out int personId))
                return PersonKey(personId);
            return "household:" + (ownerId ?? string.Empty);
        }
    }

    /// <summary>
    /// D4L: the money-movement authority the market consumes (D4H port shape).
    /// The market never invents, mints, or assumes money: every buyer-to-seller
    /// payment goes through this port, and the port is the authority on whether
    /// the money exists.
    ///
    /// Contract for implementations:
    /// - MoveCash is ATOMIC: either the full amount moves buyer-to-seller or
    ///   nothing moves and a refusal explains why. No partial moves.
    /// - MoveCash moves only REAL lots: it refuses (loudly) when the buyer's
    ///   balance cannot cover the amount. The market treats any refusal as
    ///   fatal for that purchase and records nothing as sold.
    /// - Implementations must not fabricate a balance for an unknown party;
    ///   an unknown party is a refusal, not a zero.
    /// - The Unity-side production implementation adapts the real cash model
    ///   (person cash / BusinessRuntimeState cash lots); the EditMode tests use
    ///   an in-memory port holding real discrete balances.
    /// </summary>
    public interface IUsedEquipmentCashPort
    {
        /// <summary>
        /// Moves amountCents from fromPartyKey to toPartyKey as one atomic
        /// step. Returns the refusal text, or null when the move succeeded.
        /// A refusal means nothing moved — the caller must not treat the
        /// purchase as settled.
        /// </summary>
        string MoveCash(string fromPartyKey, string toPartyKey, int amountCents,
            int dayIndex, string memo, List<string> diagnostics);

        /// <summary>
        /// Reads a party's current cash balance. Returns the refusal text
        /// (unknown party, unreadable lots), or null with balanceCents set.
        /// </summary>
        string TryGetCashBalance(string partyKey, out int balanceCents);
    }

    // ================= market rules =================

    /// <summary>
    /// D4L: market-behavior calibration knobs. All tuning (Canon Part XV),
    /// not canon. Fork-adjacent choices are explicit knobs rather than
    /// hard-coded guesses.
    /// </summary>
    [Serializable]
    public sealed class UsedEquipmentMarketRules
    {
        /// <summary>
        /// Calibration: a purchase is refused while a misgrade dispute stands
        /// open — the dispute is never auto-resolved, the sale simply waits for
        /// an explicit resolution or withdrawal. Default on.
        /// </summary>
        public bool RefuseSaleOnOpenDispute = true;

        /// <summary>
        /// Calibration: how far the asset's live condition may drift from the
        /// listing snapshot before the sale refuses and the seller must relist
        /// (physical truth: the listing described a specific condition).
        /// </summary>
        public float ConditionDriftTolerance01 = 0.001f;

        public UsedEquipmentMarketRules() { }
    }

    // ================= the market book =================

    /// <summary>
    /// D4L: the town's used-equipment market book — listings, inspections,
    /// sales, and misgrade disputes as records. Shared authority (Tech X §6.4
    /// pattern): one book serves every seller and buyer rather than each shop
    /// keeping its own incompatible ledger.
    ///
    /// Record discipline throughout: nothing here auto-prices, auto-resolves,
    /// or auto-seizes. Disputes are recorded, never resolved by the book;
    /// sales are refused LOUDLY when money, ownership, condition, or consent
    /// do not line up.
    /// </summary>
    public sealed class UsedEquipmentMarketBook
    {
        public UsedEquipmentMarketRules Rules = new UsedEquipmentMarketRules();

        private readonly Dictionary<string, UsedEquipmentListing> listings =
            new Dictionary<string, UsedEquipmentListing>(StringComparer.Ordinal);
        private readonly Dictionary<string, UsedEquipmentSale> sales =
            new Dictionary<string, UsedEquipmentSale>(StringComparer.Ordinal);
        private readonly List<EquipmentInspectionRecord> inspections =
            new List<EquipmentInspectionRecord>();
        private readonly EquipmentMisgradeDisputeRegister disputes =
            new EquipmentMisgradeDisputeRegister();

        public IReadOnlyDictionary<string, UsedEquipmentListing> Listings => listings;
        public IReadOnlyDictionary<string, UsedEquipmentSale> Sales => sales;
        public IReadOnlyList<EquipmentInspectionRecord> Inspections => inspections;
        public EquipmentMisgradeDisputeRegister Disputes => disputes;

        public UsedEquipmentMarketBook() { }

        public UsedEquipmentListing FindListing(string listingKey)
        {
            if (string.IsNullOrWhiteSpace(listingKey)) return null;
            listings.TryGetValue(listingKey, out UsedEquipmentListing listing);
            return listing;
        }

        private static string ListingKeyOf(EntityId id) => id.Kind + ":" + id.Id;

        /// <summary>
        /// Lists a used asset for sale. The seller must OWN the asset right now
        /// (provenance discipline — no selling what you don't hold), the asset
        /// must not be reserved (Tech X §3.8 — no double-dealing), and the
        /// asking price must be positive. Guidance is computed from the
        /// caller's real price data and recorded alongside the seller's ask —
        /// advisory only.
        /// </summary>
        public UsedEquipmentListing ListForSale(
            EquipmentAsset asset,
            int askingPriceCents,
            string sellerKind,
            string sellerId,
            string sellerName,
            Func<string, int> replacementCostCentsForKind,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (asset == null) { diagnostics.Add("UsedEquipmentMarketBook: no asset supplied."); return null; }
            if (string.IsNullOrWhiteSpace(asset.AssetId))
            { diagnostics.Add("UsedEquipmentMarketBook: the asset has no identity — registry assets only."); return null; }
            if (askingPriceCents <= 0)
            { diagnostics.Add($"UsedEquipmentMarketBook: asking price must be positive ({askingPriceCents}c is not a price)."); return null; }
            if (string.IsNullOrWhiteSpace(sellerId))
            { diagnostics.Add("UsedEquipmentMarketBook: no anonymous sellers — every listing names its seller."); return null; }
            if (!string.Equals(asset.OwnerKind, sellerKind, StringComparison.Ordinal) ||
                !string.Equals(asset.OwnerId, sellerId, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: {sellerId} does not own {asset.AssetId} " +
                    $"(owner is {asset.OwnerKind}:{asset.OwnerId}) — no selling what you don't hold.");
                return null;
            }
            if (asset.IsReserved)
            {
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: {asset.AssetId} is reserved by {asset.ReservedBy} — " +
                    "no double-dealing (Tech X §3.8).");
                return null;
            }

            UsedEquipmentPriceGuidance guidance =
                UsedEquipmentPriceGuide.GuidanceFor(asset, replacementCostCentsForKind);

            var listing = new UsedEquipmentListing
            {
                ListingId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                AssetId = asset.AssetId,
                Kind = asset.Kind,
                DisplayName = asset.DisplayName,
                Condition01AtListing = Mathf.Clamp01(asset.Condition01),
                RecordedGrade = UsedEquipmentConditionGrades.GradeFor(asset.Condition01),
                AskingPriceCents = askingPriceCents,
                PriceGuidanceCents = guidance.HasGuidance ? guidance.ConditionWeightedValueCents : -1,
                PriceGuidanceNote = guidance.BasisNote,
                SellerKind = sellerKind ?? string.Empty,
                SellerId = sellerId,
                SellerName = sellerName ?? string.Empty,
                LocationId = asset.LocationId,
                ListedDayIndex = dayIndex,
                Status = UsedEquipmentListingStatus.Active,
                MakerBusinessName = asset.MadeByBusinessName,
            };
            listing.MaintenanceHistorySnapshot.AddRange(asset.MaintenanceLog);
            foreach (EquipmentOwnershipRecord record in asset.OwnershipHistory)
                if (record != null) listing.OwnershipSnapshot.Add(record.Clone());

            listings[ListingKeyOf(listing.ListingId)] = listing;
            diagnostics.Add(
                $"UsedEquipmentMarketBook: listed {asset.DisplayName} ({asset.AssetId}) at " +
                $"{listing.RecordedGrade} ({listing.Condition01AtListing:P0}) for {askingPriceCents}c " +
                $"by {sellerName ?? sellerId}" +
                (guidance.HasGuidance
                    ? $" (guidance {guidance.ConditionWeightedValueCents}c — advisory)."
                    : " (no price guidance available)."));
            return listing;
        }

        /// <summary>
        /// The seller pulls an active listing (kept, repaired, misgrade
        /// corrected by relisting...). Withdrawn listings never come back —
        /// correcting a misgrade means a FRESH listing with the corrected
        /// grade, so the audit trail stays honest.
        /// </summary>
        public string WithdrawListing(string listingKey, int dayIndex, string reason,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            UsedEquipmentListing listing = FindListing(listingKey);
            if (listing == null) return $"UsedEquipmentMarketBook: no listing '{listingKey}'.";
            if (listing.Status != UsedEquipmentListingStatus.Active)
                return $"UsedEquipmentMarketBook: listing {listingKey} is {listing.Status}, not active.";

            listing.Status = UsedEquipmentListingStatus.Withdrawn;
            diagnostics.Add(
                $"UsedEquipmentMarketBook: listing {listingKey} ({listing.DisplayName}) withdrawn on day {dayIndex}" +
                (string.IsNullOrWhiteSpace(reason) ? "." : $" — {reason}"));
            return null;
        }

        /// <summary>
        /// Records a pre-purchase inspection (Canon §50/§51: diligence combines
        /// records with inspection). The observed condition is graded with the
        /// SAME taxonomy as the listing; a mismatch flags the listing as
        /// MISGRADED and opens a dispute record — recorded, never auto-resolved.
        /// </summary>
        public EquipmentInspectionRecord RecordInspection(
            string listingKey,
            EquipmentAsset asset,
            string inspectorKind,
            string inspectorId,
            string inspectorName,
            float observedCondition01,
            int dayIndex,
            string notes,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            UsedEquipmentListing listing = FindListing(listingKey);
            if (listing == null) { diagnostics.Add($"UsedEquipmentMarketBook: no listing '{listingKey}'."); return null; }
            if (listing.Status != UsedEquipmentListingStatus.Active)
            { diagnostics.Add($"UsedEquipmentMarketBook: listing {listingKey} is {listing.Status} — inspections are pre-purchase."); return null; }
            if (asset == null || !string.Equals(asset.AssetId, listing.AssetId, StringComparison.Ordinal))
            { diagnostics.Add("UsedEquipmentMarketBook: inspection needs the listed asset itself."); return null; }
            if (string.IsNullOrWhiteSpace(inspectorId))
            { diagnostics.Add("UsedEquipmentMarketBook: inspections are signed — name the inspector."); return null; }

            float observed = Mathf.Clamp01(observedCondition01);
            UsedEquipmentConditionGrade observedGrade = UsedEquipmentConditionGrades.GradeFor(observed);
            bool matches = observedGrade == listing.RecordedGrade;

            var record = new EquipmentInspectionRecord
            {
                InspectionId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                ListingId = listing.ListingId,
                AssetId = listing.AssetId,
                InspectorKind = inspectorKind ?? string.Empty,
                InspectorId = inspectorId,
                InspectorName = inspectorName ?? string.Empty,
                DayIndex = dayIndex,
                ObservedCondition01 = observed,
                ObservedGrade = observedGrade,
                RecordedGrade = listing.RecordedGrade,
                Verdict = matches
                    ? EquipmentInspectionVerdict.MatchesRecordedGrade
                    : EquipmentInspectionVerdict.Misgraded,
                Notes = notes ?? string.Empty,
            };
            inspections.Add(record);

            if (!matches)
            {
                listing.OpenDisputeCount++;
                EquipmentMisgradeDispute dispute = disputes.OpenDispute(
                    listing.ListingId, listing.AssetId, listing.RecordedGrade,
                    observed, record.InspectionId,
                    inspectorKind, inspectorId, inspectorName ?? inspectorId,
                    dayIndex, idRegistry, diagnostics);
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: INSPECTION FLAG — {listing.DisplayName} listed as " +
                    $"{listing.RecordedGrade} but inspected as {observedGrade} ({observed:P0}). " +
                    $"Dispute {dispute.DisputeKey()} recorded — never auto-resolved.");
            }
            else
            {
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: inspection of {listing.DisplayName} confirms " +
                    $"{listing.RecordedGrade} ({observed:P0}) — matches the listing.");
            }
            return record;
        }

        /// <summary>
        /// Buys the listed asset. Refuses LOUDLY unless everything lines up:
        /// active listing, seller still owns the asset, asset not reserved,
        /// condition unchanged since listing (relist if it wore), no open
        /// dispute (calibration), buyer is not the seller, and the cash port
        /// moves REAL money buyer-to-seller. Title transfers with provenance
        /// intact: the asset's OwnershipHistory gains the transfer.
        /// </summary>
        public UsedEquipmentSale Purchase(
            string listingKey,
            EquipmentAsset asset,
            string buyerKind,
            string buyerId,
            string buyerName,
            IUsedEquipmentCashPort cashPort,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            UsedEquipmentListing listing = FindListing(listingKey);
            if (listing == null) { diagnostics.Add($"UsedEquipmentMarketBook: no listing '{listingKey}'."); return null; }
            if (listing.Status != UsedEquipmentListingStatus.Active)
            { diagnostics.Add($"UsedEquipmentMarketBook: listing {listingKey} is {listing.Status} — not for sale."); return null; }
            if (asset == null || !string.Equals(asset.AssetId, listing.AssetId, StringComparison.Ordinal))
            { diagnostics.Add("UsedEquipmentMarketBook: purchase needs the listed asset itself."); return null; }
            if (string.IsNullOrWhiteSpace(buyerId))
            { diagnostics.Add("UsedEquipmentMarketBook: no anonymous buyers — every sale names its buyer."); return null; }

            // The seller must still own it — the asset may have moved since listing.
            if (!string.Equals(asset.OwnerKind, listing.SellerKind, StringComparison.Ordinal) ||
                !string.Equals(asset.OwnerId, listing.SellerId, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: {listing.AssetId} is no longer held by {listing.SellerId} " +
                    $"(now {asset.OwnerKind}:{asset.OwnerId}) — stale listing, cannot sell.");
                return null;
            }
            if (asset.IsReserved)
            {
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: {asset.AssetId} is reserved by {asset.ReservedBy} — " +
                    "no double-dealing (Tech X §3.8).");
                return null;
            }
            // Physical truth: the listing described a specific condition.
            if (Math.Abs(asset.Condition01 - listing.Condition01AtListing) > Rules.ConditionDriftTolerance01)
            {
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: {asset.AssetId} wore from {listing.Condition01AtListing:P2} to " +
                    $"{asset.Condition01:P2} since listing — the listing no longer describes it. Withdraw and relist.");
                return null;
            }
            if (Rules.RefuseSaleOnOpenDispute && listing.OpenDisputeCount > 0)
            {
                diagnostics.Add(
                    $"UsedEquipmentMarketBook: listing {listingKey} carries {listing.OpenDisputeCount} open " +
                    "misgrade dispute(s) — resolve or withdraw first. Disputes are never auto-resolved.");
                return null;
            }
            if (string.Equals(buyerKind, listing.SellerKind, StringComparison.Ordinal) &&
                string.Equals(buyerId, listing.SellerId, StringComparison.Ordinal))
            {
                diagnostics.Add("UsedEquipmentMarketBook: the seller cannot buy their own listing.");
                return null;
            }
            if (cashPort == null)
            { diagnostics.Add("UsedEquipmentMarketBook: no cash port — the market never moves money it cannot see."); return null; }

            string fromKey = UsedEquipmentPartyKeys.ForOwner(buyerKind, buyerId);
            string toKey = UsedEquipmentPartyKeys.ForOwner(listing.SellerKind, listing.SellerId);
            string refusal = cashPort.MoveCash(fromKey, toKey, listing.AskingPriceCents, dayIndex,
                $"used-equipment purchase {listing.AssetId}", diagnostics);
            if (refusal != null)
            {
                diagnostics.Add($"UsedEquipmentMarketBook: purchase refused — {refusal} Nothing sold, nothing moved.");
                return null;
            }

            string transferRejection = asset.TransferOwnership(
                buyerKind, buyerId, $"used-equipment market purchase ({listingKey})", dayIndex);
            if (transferRejection != null)
            {
                // Money already moved — this is operator-territory: the transfer
                // cannot fail here (ownership was verified above), but the code
                // refuses to silently swallow it.
                diagnostics.Add($"UsedEquipmentMarketBook: TITLE TRANSFER FAILED after payment — {transferRejection}");
                return null;
            }

            var sale = new UsedEquipmentSale
            {
                SaleId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                ListingId = listing.ListingId,
                AssetId = listing.AssetId,
                Kind = listing.Kind,
                DisplayName = listing.DisplayName,
                SellerKind = listing.SellerKind,
                SellerId = listing.SellerId,
                SellerName = listing.SellerName,
                BuyerKind = buyerKind ?? string.Empty,
                BuyerId = buyerId,
                BuyerName = buyerName ?? string.Empty,
                PriceCents = listing.AskingPriceCents,
                DayIndex = dayIndex,
                PaymentMemo = $"cash-lot move {fromKey} -> {toKey}",
            };
            sales[ListingKeyOf(sale.SaleId)] = sale;
            listing.Status = UsedEquipmentListingStatus.Sold;

            diagnostics.Add(
                $"UsedEquipmentMarketBook: SOLD {listing.DisplayName} ({listing.AssetId}) to " +
                $"{buyerName ?? buyerId} for {listing.AskingPriceCents}c — title transferred, provenance intact.");
            return sale;
        }

        /// <summary>
        /// Explicit operator step: records the resolution of a misgrade
        /// dispute. The book never resolves disputes on its own — this is the
        /// human/operator ladder. A resolved dispute decrements the listing's
        /// open-dispute count so trade can resume (or the listing is withdrawn).
        /// </summary>
        public string RecordDisputeResolution(
            string disputeKey, EquipmentDisputeStatus resolution,
            string resolvedBy, string resolutionNote, int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (resolution != EquipmentDisputeStatus.Upheld &&
                resolution != EquipmentDisputeStatus.Dismissed)
                return "UsedEquipmentMarketBook: a dispute resolves to Upheld or Dismissed — nothing in between.";

            EquipmentMisgradeDispute dispute = disputes.Find(disputeKey);
            if (dispute == null) return $"UsedEquipmentMarketBook: no dispute '{disputeKey}'.";
            if (dispute.Status != EquipmentDisputeStatus.Recorded)
                return $"UsedEquipmentMarketBook: dispute {disputeKey} is already {dispute.Status}.";

            dispute.Status = resolution;
            dispute.ResolvedDayIndex = dayIndex;
            dispute.ResolvedBy = resolvedBy ?? string.Empty;
            dispute.ResolutionNote = resolutionNote ?? string.Empty;

            UsedEquipmentListing listing = FindListing(ListingKeyOf(dispute.ListingId));
            if (listing != null && listing.OpenDisputeCount > 0) listing.OpenDisputeCount--;

            diagnostics.Add(
                $"UsedEquipmentMarketBook: dispute {disputeKey} {resolution.ToString().ToLower()} by " +
                $"{resolvedBy ?? "operator"} on day {dayIndex}" +
                (string.IsNullOrWhiteSpace(resolutionNote) ? "." : $" — {resolutionNote}"));
            return null;
        }

        // ---------- save DTO (inside the owning book class) ----------

        [Serializable]
        public sealed class UsedEquipmentMarketBookDto
        {
            public UsedEquipmentMarketRules Rules = new UsedEquipmentMarketRules();
            public List<UsedEquipmentListing> Listings = new List<UsedEquipmentListing>();
            public List<UsedEquipmentSale> Sales = new List<UsedEquipmentSale>();
            public List<EquipmentInspectionRecord> Inspections = new List<EquipmentInspectionRecord>();
            public EquipmentMisgradeDisputeRegister.EquipmentMisgradeDisputeRegisterDto Disputes;
        }

        public UsedEquipmentMarketBookDto ToSaveDto()
        {
            var dto = new UsedEquipmentMarketBookDto { Rules = Rules ?? new UsedEquipmentMarketRules() };
            foreach (var kv in listings) dto.Listings.Add(kv.Value);
            foreach (var kv in sales) dto.Sales.Add(kv.Value);
            dto.Inspections.AddRange(inspections);
            dto.Disputes = disputes.ToSaveDto();
            return dto;
        }

        public void LoadFromSaveDto(UsedEquipmentMarketBookDto dto)
        {
            listings.Clear();
            sales.Clear();
            inspections.Clear();
            if (dto == null) { disputes.LoadFromSaveDto(null); return; }
            Rules = dto.Rules ?? new UsedEquipmentMarketRules();
            foreach (UsedEquipmentListing l in dto.Listings)
            {
                if (l == null || l.ListingId.Equals(EntityId.Invalid)) continue;
                listings[ListingKeyOf(l.ListingId)] = l;
            }
            foreach (UsedEquipmentSale s in dto.Sales)
            {
                if (s == null || s.SaleId.Equals(EntityId.Invalid)) continue;
                sales[ListingKeyOf(s.SaleId)] = s;
            }
            if (dto.Inspections != null) inspections.AddRange(dto.Inspections);
            disputes.LoadFromSaveDto(dto.Disputes);
        }
    }
}
