// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F4 — lot identity and provenance keys (P9 plan
// section 2.4, section 4 Slice F4, directive Priority D). A READ-ONLY
// resolver over the five lot-id spaces: given a LotKey (reused from
// CustodyTransferLog, never redefined here), answer which lot that is —
// item, units, custodian, owner, and provenance key — by reading the
// owning authority's own records.
//
// The resolver CREATES NOTHING, MOVES NOTHING, and changes no ownership
// or custody. It holds no copy of any quantity between calls: every call
// re-reads its authorities, so two reads of the same state agree and a
// read can never drift from the authority it reports (the Slice 3 read
// model discipline). It consults ONLY existing authority records through
// their existing public surfaces; a space whose authority was not
// supplied (null) is unresolvable and Resolve returns null for keys in
// that space with the gap named in Diagnostics, never an exception.
//
// The five lot-id spaces and their authorities:
//   - household lots ("household-inventory", int LotId scoped per
//     household): HouseholdInventoryRegistry, via ExportState.
//   - retail lots ("business:<businessId>", string LotId scoped per
//     business): BusinessRuntimeState, via FindRetailItemLot /
//     RetailItemLots.
//   - Millbrook lots ("millbrook:<businessId>", LotId
//     "aidlot-<businessId>:<requestId>"): OffMapAidBusinessRuntime,
//     via Lots.
//   - named-SKU lots ("named-sku:<businessId>", LotId the procurement
//     lot id): OffMapNamedSkuProcurementService, via Lots. This sixth
//     optional constructor parameter is a deviation from the brief's
//     five-authority list, recorded in the Slice 4b checkpoint entry:
//     the named-SKU space is held ONLY by the procurement service, and
//     none of the five listed authorities can answer it.
//   - Bakery flour and bread lots ("bakery:<businessInstanceId>", LotId
//     the lot EntityId): BakeryShopRuntime, via FlourStock.Lots and
//     BreadShelf.
//   - In-transit freight custody ("freight:<shipmentId>", LotId the
//     shipment id): FreightShipmentService, via TrackedShipments. The
//     shipment itself is the identity: its cargo SKU, its planned
//     quantity, and the wagon holding it.
//
// Units are the owning authority's own quantity, in its own unit —
// never converted, never summed across spaces:
//   - household: HouseholdItemLot.QuantityUnits; retail:
//     BusinessRetailItemLotState.QuantityUnits; Millbrook:
//     OffMapAidCargoLot.InventoryUnits (pounds); named-SKU:
//     NamedSkuLot.RemainingUnits (live remaining, 0 once transferred);
//     Bakery flour: BakeryFlourLot.Units; Bakery bread:
//     BakeryBreadLot.UnitCount (0 once sold through — the lot stays on
//     the shelf); freight: LogisticsShipmentState.PlannedQuantityUnits
//     (the contracted quantity, not remaining/delivered, which move).
//
// Owner is the authority's own owner field as-is. Where the authority
// carries no owner field (Bakery flour and bread lots; freight
// shipments), Owner is empty — never invented. Custodian is likewise
// the authority's own holder field; where the authority carries no
// holder field, the business that holds the record is reported and the
// code says so (named-SKU lots: the procuring business; Millbrook
// cargo lots: the supplier of record — the cargo lot carries no
// current-holder field, so after receipt the custodian is still the
// supplier of record, not the Bakery).
//
// ProvenanceKey is the canonical key "authority:<authorityId>/lot:<lotId>"
// (see CanonicalProvenanceKey), read out of the lot's own provenance
// field where the creation site appended it (household SourceLabel,
// retail Provenance, bread InputProvenance). Where the authority
// carries no canonical key, ProvenanceKey is empty and ProvenanceNote
// names the gap — never an invented key. Gaps in this slice:
// Millbrook cargo lots carry no provenance field at all (nothing to
// append to; recorded in the checkpoint, site not restructured);
// Bakery flour lots carry free-text provenance (ProvenanceChain names
// the import order and origin) but no stored canonical key; named-SKU
// lots carry free-text provenance but the directive's append list
// covers only the four named sites, so no canonical key was appended
// at their creation; freight shipments carry no provenance field.
// Household lots created before Slice 4b, or restored from a save
// that predates it, likewise carry no key in their SourceLabel.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// One lot's identity, as its owning authority reports it. A fresh
    /// snapshot each call; the resolver keeps no copy between calls.
    /// </summary>
    public sealed class LotIdentity
    {
        public LotIdentity(
            LotKey key, string itemId, int units, string custodian, string owner,
            string provenanceKey, string provenanceNote, string sourceAuthority)
        {
            Key = key;
            ItemId = itemId ?? string.Empty;
            Units = units;
            Custodian = custodian ?? string.Empty;
            Owner = owner ?? string.Empty;
            ProvenanceKey = provenanceKey ?? string.Empty;
            ProvenanceNote = provenanceNote ?? string.Empty;
            SourceAuthority = sourceAuthority ?? string.Empty;
        }

        /// <summary>The lot's key: authority plus lot id, never a bare lot id.</summary>
        public LotKey Key { get; }

        /// <summary>The item id in the owning authority's own vocabulary.</summary>
        public string ItemId { get; }

        /// <summary>The lot's quantity in the owning authority's own unit (see the file header).</summary>
        public int Units { get; }

        /// <summary>Who holds the lot, as the owning authority reports it.</summary>
        public string Custodian { get; }

        /// <summary>
        /// Who owns the lot, as the owning authority reports it. Empty
        /// where the authority carries no owner field (Bakery lots,
        /// freight shipments) — never invented.
        /// </summary>
        public string Owner { get; }

        /// <summary>
        /// The canonical provenance key for this lot, when the lot's own
        /// provenance field carries it. Empty when the source authority
        /// carries no provenance key — see <see cref="ProvenanceNote"/>.
        /// </summary>
        public string ProvenanceKey { get; }

        /// <summary>
        /// Empty when <see cref="ProvenanceKey"/> is present; otherwise
        /// names why this lot carries no canonical provenance key.
        /// </summary>
        public string ProvenanceNote { get; }

        /// <summary>The authority id this identity was read from (the key's authority).</summary>
        public string SourceAuthority { get; }

        /// <summary>
        /// The canonical provenance key for a lot: "authority:&lt;authorityId&gt;/lot:&lt;lotId&gt;".
        /// For example "authority:household-inventory/lot:7" or
        /// "authority:business:general-store/lot:feed-42".
        /// </summary>
        public static string CanonicalProvenanceKey(string authorityId, string lotId) =>
            "authority:" + (authorityId ?? string.Empty) + "/lot:" + (lotId ?? string.Empty);

        /// <summary>
        /// Appends the canonical provenance key for a lot to a provenance
        /// field, never rewriting free text: the existing text is kept
        /// verbatim and the key is added once, separated by " | ". A field
        /// that already contains this lot's canonical key is returned
        /// unchanged (never appended a second time); an empty field
        /// becomes just the key.
        /// </summary>
        public static string AppendProvenanceKey(string existing, string authorityId, string lotId)
        {
            string canonical = CanonicalProvenanceKey(authorityId, lotId);
            if (string.IsNullOrWhiteSpace(existing)) return canonical;
            if (existing.Contains(canonical)) return existing;
            return existing + " | " + canonical;
        }

        /// <summary>
        /// Reads the canonical provenance key for a lot out of a provenance
        /// field: true with the key when the field contains exactly this
        /// lot's canonical key, false otherwise (the field may carry other
        /// lots' keys — a split child's SourceLabel, for example, carries
        /// its parent's key too — and only this lot's own key counts).
        /// </summary>
        public static bool TryExtractProvenanceKey(
            string provenanceText, string authorityId, string lotId, out string canonicalKey)
        {
            canonicalKey = string.Empty;
            if (string.IsNullOrWhiteSpace(provenanceText)) return false;
            string canonical = CanonicalProvenanceKey(authorityId, lotId);
            if (!provenanceText.Contains(canonical)) return false;
            canonicalKey = canonical;
            return true;
        }
    }

    /// <summary>Read-only lot identity resolution across the lot-id spaces.</summary>
    public interface ILotIdentityResolver
    {
        /// <summary>The identity of one lot, or null when no record exists for the key.</summary>
        LotIdentity Resolve(LotKey key);

        /// <summary>Every identity for an item across all supplied authorities, with no duplicates.</summary>
        IReadOnlyList<LotIdentity> ResolveAll(string itemId);
    }

    /// <summary>
    /// Read-only lot identity resolver. Constructed with the authorities
    /// it can read; any authority left null means its space is
    /// unresolvable and Resolve returns null for keys in that space with
    /// the gap named in Diagnostics, never an exception. No flow calls
    /// this resolver in this slice: it is constructed in tests against
    /// the real authorities (the P1 Slice 0/6 real-authority, test-side
    /// driver pattern).
    /// </summary>
    public sealed class LotIdentityResolver : ILotIdentityResolver
    {
        private readonly HouseholdInventoryRegistry households;
        private readonly BusinessRuntimeState business;
        private readonly BakeryShopRuntime bakery;
        private readonly OffMapAidBusinessRuntime millbrook;
        private readonly FreightShipmentService freight;
        private readonly OffMapNamedSkuProcurementService namedSku;
        private readonly List<string> diagnostics = new List<string>();

        /// <param name="households">Household-lot authority; null means household keys are unresolvable.</param>
        /// <param name="business">Retail-lot authority (one business); null means retail keys are unresolvable.</param>
        /// <param name="bakery">Bakery-lot authority; null means bakery keys are unresolvable.</param>
        /// <param name="millbrook">Millbrook-lot authority; null means Millbrook keys are unresolvable.</param>
        /// <param name="freight">Freight-shipment authority; null means freight keys are unresolvable.</param>
        /// <param name="namedSku">Named-SKU-lot authority; null means named-SKU keys are unresolvable.</param>
        public LotIdentityResolver(
            HouseholdInventoryRegistry households = null,
            BusinessRuntimeState business = null,
            BakeryShopRuntime bakery = null,
            OffMapAidBusinessRuntime millbrook = null,
            FreightShipmentService freight = null,
            OffMapNamedSkuProcurementService namedSku = null)
        {
            this.households = households;
            this.business = business;
            this.bakery = bakery;
            this.millbrook = millbrook;
            this.freight = freight;
            this.namedSku = namedSku;
        }

        /// <summary>Resolution gaps named in order (unknown keys, missing authorities, authority mismatches).</summary>
        public IReadOnlyList<string> Diagnostics => diagnostics.AsReadOnly();

        /// <summary>
        /// The identity of one lot, or null when no record exists for the
        /// key: an empty key, an unknown authority space, a lot id with no
        /// record in its authority, a key for a business this resolver
        /// does not hold, or a key in a space whose authority was not
        /// supplied. Every null names its gap in Diagnostics; a null is
        /// never an exception.
        /// </summary>
        public LotIdentity Resolve(LotKey key)
        {
            if (key.IsEmpty)
            {
                Note("LotIdentityResolver: empty lot key matches nothing — no lot is keyed empty.");
                return null;
            }

            string authority = key.AuthorityId ?? string.Empty;
            if (string.Equals(authority, CustodyTransferLog.HouseholdInventoryAuthority, StringComparison.Ordinal))
                return ResolveHousehold(key);
            if (authority.StartsWith("business:", StringComparison.Ordinal))
                return ResolveRetail(key);
            if (authority.StartsWith("millbrook:", StringComparison.Ordinal))
                return ResolveMillbrook(key);
            if (authority.StartsWith("named-sku:", StringComparison.Ordinal))
                return ResolveNamedSku(key);
            if (authority.StartsWith("bakery:", StringComparison.Ordinal))
                return ResolveBakery(key);
            if (authority.StartsWith("freight:", StringComparison.Ordinal))
                return ResolveFreight(key);

            Note($"LotIdentityResolver: authority '{authority}' is not a known lot-id space — no record can answer key {key}.");
            return null;
        }

        /// <summary>
        /// Every identity for an item across all supplied authorities,
        /// with no duplicates (deduplicated by LotKey). A null or empty
        /// item id returns an empty list, never an error. Authorities
        /// that were not supplied contribute nothing.
        /// </summary>
        public IReadOnlyList<LotIdentity> ResolveAll(string itemId)
        {
            var result = new List<LotIdentity>();
            if (string.IsNullOrWhiteSpace(itemId)) return result;
            var seen = new HashSet<LotKey>();

            if (households != null)
            {
                var states = new List<HouseholdInventoryState>();
                households.ExportState(states);
                foreach (HouseholdInventoryState state in states)
                {
                    if (state == null || state.Lots == null) continue;
                    foreach (HouseholdItemLot lot in state.Lots)
                    {
                        if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                        var key = new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, lot.LotId.ToString());
                        if (seen.Add(key)) result.Add(HouseholdIdentity(state, lot, key));
                    }
                }
            }

            if (business != null)
            {
                foreach (BusinessRetailItemLotState lot in business.RetailItemLots)
                {
                    if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = new LotKey(CustodyTransferLog.BusinessAuthority(business.BusinessId), lot.LotId);
                    if (seen.Add(key)) result.Add(RetailIdentity(lot, key));
                }
            }

            if (millbrook != null)
            {
                foreach (OffMapAidCargoLot lot in millbrook.Lots)
                {
                    if (lot == null || !string.Equals(lot.SkuId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = new LotKey(CustodyTransferLog.MillbrookAuthority(millbrook.BusinessId), lot.LotId);
                    if (seen.Add(key)) result.Add(MillbrookIdentity(lot, key));
                }
            }

            if (namedSku != null)
            {
                foreach (NamedSkuLot lot in namedSku.Lots)
                {
                    if (lot == null || !string.Equals(lot.SkuId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = new LotKey(CustodyTransferLog.NamedSkuAuthority(namedSku.BusinessId), lot.LotId);
                    if (seen.Add(key)) result.Add(NamedSkuIdentity(lot, key));
                }
            }

            if (bakery != null)
            {
                string authority = CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId);
                foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
                {
                    if (lot == null || !string.Equals(lot.FlourName, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = new LotKey(authority, lot.LotId.ToString());
                    if (seen.Add(key)) result.Add(BakeryFlourIdentity(lot, key));
                }
                foreach (BakeryBreadLot lot in bakery.BreadShelf)
                {
                    if (lot == null || !string.Equals(lot.ProductId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = new LotKey(authority, lot.LotId.ToString());
                    if (seen.Add(key)) result.Add(BakeryBreadIdentity(lot, key));
                }
            }

            if (freight != null)
            {
                foreach (LogisticsShipmentState shipment in freight.TrackedShipments)
                {
                    if (shipment == null || !string.Equals(shipment.cargoSkuId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = new LotKey(CustodyTransferLog.FreightAuthority(shipment.ShipmentId), shipment.ShipmentId);
                    if (seen.Add(key)) result.Add(FreightIdentity(shipment, key));
                }
            }

            return result;
        }

        private LotIdentity ResolveHousehold(LotKey key)
        {
            if (households == null)
            {
                Note($"LotIdentityResolver: no household inventory authority was supplied, so household lot {key.LotId} is unresolvable.");
                return null;
            }
            if (!int.TryParse(key.LotId, out int lotId))
            {
                Note($"LotIdentityResolver: household lot id '{key.LotId}' is not a household lot number — carried batches (carry-<day>-<seq>) have no registry record, so the key resolves to nothing.");
                return null;
            }
            var states = new List<HouseholdInventoryState>();
            households.ExportState(states);
            foreach (HouseholdInventoryState state in states)
            {
                if (state == null || state.Lots == null) continue;
                foreach (HouseholdItemLot lot in state.Lots)
                {
                    if (lot != null && lot.LotId == lotId) return HouseholdIdentity(state, lot, key);
                }
            }
            Note($"LotIdentityResolver: no household lot {lotId} exists in any household inventory.");
            return null;
        }

        private LotIdentity HouseholdIdentity(HouseholdInventoryState state, HouseholdItemLot lot, LotKey key)
        {
            string authority = CustodyTransferLog.HouseholdInventoryAuthority;
            string provenanceKey = string.Empty;
            string provenanceNote = string.Empty;
            if (!LotIdentity.TryExtractProvenanceKey(lot.SourceLabel, authority, lot.LotId.ToString(), out provenanceKey))
            {
                provenanceNote = $"Household lot {lot.LotId} carries no canonical provenance key in its SourceLabel " +
                    "— the lot predates the Slice 4b provenance appends, or was restored from a save that predates them; no key is invented.";
            }
            return new LotIdentity(
                key, lot.ItemId, lot.QuantityUnits,
                CustodyTransferLog.HouseholdCustodian(state.HouseholdId),
                lot.OwnerPersonId >= 0
                    ? CustodyTransferLog.PersonCustodian(lot.OwnerPersonId)
                    : CustodyTransferLog.HouseholdCustodian(state.HouseholdId),
                provenanceKey, provenanceNote, authority);
        }

        private LotIdentity ResolveRetail(LotKey key)
        {
            if (business == null)
            {
                Note($"LotIdentityResolver: no business runtime was supplied, so retail lot {key.LotId} is unresolvable.");
                return null;
            }
            string businessId = key.AuthorityId.Substring("business:".Length);
            if (!string.Equals(business.BusinessId, businessId, StringComparison.Ordinal))
            {
                Note($"LotIdentityResolver: this resolver holds business '{business.BusinessId}', not '{businessId}', so retail lot {key.LotId} is unresolvable here.");
                return null;
            }
            BusinessRetailItemLotState lot = business.FindRetailItemLot(key.LotId);
            if (lot == null)
            {
                Note($"LotIdentityResolver: no retail lot '{key.LotId}' exists in business '{businessId}'.");
                return null;
            }
            return RetailIdentity(lot, key);
        }

        private LotIdentity RetailIdentity(BusinessRetailItemLotState lot, LotKey key)
        {
            string authority = CustodyTransferLog.BusinessAuthority(business.BusinessId);
            string provenanceKey = string.Empty;
            string provenanceNote = string.Empty;
            if (!LotIdentity.TryExtractProvenanceKey(lot.Provenance, authority, lot.LotId, out provenanceKey))
            {
                provenanceNote = $"Retail lot '{lot.LotId}' carries no canonical provenance key in its provenance field " +
                    "— the lot predates the Slice 4b provenance appends, or was received before them; no key is invented.";
            }
            string custodian = string.IsNullOrWhiteSpace(lot.CustodianBusinessId)
                ? CustodyTransferLog.BusinessCustodian(business.BusinessId)
                : CustodyTransferLog.BusinessCustodian(lot.CustodianBusinessId);
            string owner = string.IsNullOrWhiteSpace(lot.OwnerBusinessId)
                ? CustodyTransferLog.BusinessCustodian(business.BusinessId)
                : CustodyTransferLog.BusinessCustodian(lot.OwnerBusinessId);
            return new LotIdentity(key, lot.ItemId, lot.QuantityUnits, custodian, owner, provenanceKey, provenanceNote, authority);
        }

        private LotIdentity ResolveMillbrook(LotKey key)
        {
            if (millbrook == null)
            {
                Note($"LotIdentityResolver: no Millbrook runtime was supplied, so Millbrook lot {key.LotId} is unresolvable.");
                return null;
            }
            string businessId = key.AuthorityId.Substring("millbrook:".Length);
            if (!string.Equals(millbrook.BusinessId, businessId, StringComparison.Ordinal))
            {
                Note($"LotIdentityResolver: this resolver holds Millbrook business '{millbrook.BusinessId}', not '{businessId}', so Millbrook lot {key.LotId} is unresolvable here.");
                return null;
            }
            foreach (OffMapAidCargoLot lot in millbrook.Lots)
            {
                if (lot != null && string.Equals(lot.LotId, key.LotId, StringComparison.Ordinal))
                    return MillbrookIdentity(lot, key);
            }
            Note($"LotIdentityResolver: no Millbrook lot '{key.LotId}' exists for '{businessId}'.");
            return null;
        }

        private LotIdentity MillbrookIdentity(OffMapAidCargoLot lot, LotKey key)
        {
            // OffMapAidCargoLot carries no provenance field at all, so no
            // canonical key can be appended or reported (Slice 4b gap,
            // recorded in the checkpoint; the site was not restructured).
            return new LotIdentity(
                key, lot.SkuId, lot.InventoryUnits,
                CustodyTransferLog.SupplierCustodian(lot.SupplierBusinessId),
                CustodyTransferLog.SupplierCustodian(lot.SupplierBusinessId),
                string.Empty,
                "Millbrook cargo lots carry no provenance field, so no canonical provenance key can be appended or reported; no key is invented.",
                CustodyTransferLog.MillbrookAuthority(millbrook.BusinessId));
        }

        private LotIdentity ResolveNamedSku(LotKey key)
        {
            if (namedSku == null)
            {
                Note($"LotIdentityResolver: no named-SKU procurement service was supplied, so named-SKU lot {key.LotId} is unresolvable.");
                return null;
            }
            string businessId = key.AuthorityId.Substring("named-sku:".Length);
            if (!string.Equals(namedSku.BusinessId, businessId, StringComparison.Ordinal))
            {
                Note($"LotIdentityResolver: this resolver holds named-SKU business '{namedSku.BusinessId}', not '{businessId}', so named-SKU lot {key.LotId} is unresolvable here.");
                return null;
            }
            foreach (NamedSkuLot lot in namedSku.Lots)
            {
                if (lot != null && string.Equals(lot.LotId, key.LotId, StringComparison.Ordinal))
                    return NamedSkuIdentity(lot, key);
            }
            Note($"LotIdentityResolver: no named-SKU lot '{key.LotId}' exists for '{businessId}'.");
            return null;
        }

        private LotIdentity NamedSkuIdentity(NamedSkuLot lot, LotKey key)
        {
            string authority = CustodyTransferLog.NamedSkuAuthority(namedSku.BusinessId);
            string provenanceKey = string.Empty;
            string provenanceNote = string.Empty;
            if (!LotIdentity.TryExtractProvenanceKey(lot.Provenance, authority, lot.LotId, out provenanceKey))
            {
                provenanceNote = $"Named-SKU lot '{lot.LotId}' carries free-text provenance but no canonical provenance key " +
                    "— the directive's Slice 4b append list covers only the four named creation sites, so no key was appended at its creation; no key is invented.";
            }
            // NamedSkuLot carries no owner or custodian field; the only
            // holder signal the authority carries is its BusinessId — the
            // procuring business holds the lot in its procurement Lots.
            string holder = CustodyTransferLog.BusinessCustodian(lot.BusinessId);
            return new LotIdentity(key, lot.SkuId, lot.RemainingUnits, holder, holder, provenanceKey, provenanceNote, authority);
        }

        private LotIdentity ResolveBakery(LotKey key)
        {
            if (bakery == null)
            {
                Note($"LotIdentityResolver: no Bakery runtime was supplied, so bakery lot {key.LotId} is unresolvable.");
                return null;
            }
            string instanceId = key.AuthorityId.Substring("bakery:".Length);
            if (!string.Equals(bakery.BusinessInstanceId, instanceId, StringComparison.Ordinal))
            {
                Note($"LotIdentityResolver: this resolver holds bakery '{bakery.BusinessInstanceId}', not '{instanceId}', so bakery lot {key.LotId} is unresolvable here.");
                return null;
            }
            foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.LotId.ToString(), key.LotId, StringComparison.Ordinal))
                    return BakeryFlourIdentity(lot, key);
            }
            foreach (BakeryBreadLot lot in bakery.BreadShelf)
            {
                if (lot != null && string.Equals(lot.LotId.ToString(), key.LotId, StringComparison.Ordinal))
                    return BakeryBreadIdentity(lot, key);
            }
            Note($"LotIdentityResolver: no bakery lot '{key.LotId}' exists in bakery '{instanceId}' (neither a flour lot nor a bread lot).");
            return null;
        }

        private LotIdentity BakeryFlourIdentity(BakeryFlourLot lot, LotKey key)
        {
            string authority = CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId);
            string provenanceKey = string.Empty;
            string provenanceNote = string.Empty;
            if (!LotIdentity.TryExtractProvenanceKey(lot.ProvenanceChain(), authority, lot.LotId.ToString(), out provenanceKey))
            {
                provenanceNote = $"Bakery flour lot {lot.LotId} carries no stored canonical provenance key; " +
                    "its ProvenanceChain names the import order and origin in free text, not a canonical key; no key is invented.";
            }
            // BakeryFlourLot carries no owner field; Owner stays empty.
            return new LotIdentity(
                key, lot.FlourName, lot.Units,
                CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId),
                string.Empty, provenanceKey, provenanceNote, authority);
        }

        private LotIdentity BakeryBreadIdentity(BakeryBreadLot lot, LotKey key)
        {
            string authority = CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId);
            string joined = lot.InputProvenance != null ? string.Join(" | ", lot.InputProvenance) : string.Empty;
            string provenanceKey = string.Empty;
            string provenanceNote = string.Empty;
            if (!LotIdentity.TryExtractProvenanceKey(joined, authority, lot.LotId.ToString(), out provenanceKey))
            {
                provenanceNote = $"Bakery bread lot {lot.LotId} carries no canonical provenance key in its InputProvenance " +
                    "— the lot was baked before the Slice 4b provenance append, or transferred in from a staged lot; no key is invented.";
            }
            // BakeryBreadLot carries no owner field; Owner stays empty.
            return new LotIdentity(
                key, lot.ProductId, lot.UnitCount,
                CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId),
                string.Empty, provenanceKey, provenanceNote, authority);
        }

        private LotIdentity ResolveFreight(LotKey key)
        {
            if (freight == null)
            {
                Note($"LotIdentityResolver: no freight shipment service was supplied, so freight key {key} is unresolvable.");
                return null;
            }
            string shipmentId = key.AuthorityId.Substring("freight:".Length);
            if (!string.Equals(key.LotId, shipmentId, StringComparison.Ordinal))
            {
                Note($"LotIdentityResolver: freight key {key} is malformed — the lot id must be the shipment id the authority names.");
                return null;
            }
            foreach (LogisticsShipmentState shipment in freight.TrackedShipments)
            {
                if (shipment != null && string.Equals(shipment.ShipmentId, shipmentId, StringComparison.Ordinal))
                    return FreightIdentity(shipment, key);
            }
            Note($"LotIdentityResolver: no freight shipment '{shipmentId}' is tracked by the freight service.");
            return null;
        }

        private LotIdentity FreightIdentity(LogisticsShipmentState shipment, LotKey key)
        {
            // A shipment carries no owner field and no provenance field:
            // Owner stays empty and the provenance note names the cargo
            // lot the shipment carries instead of inventing a key.
            string wagonId = string.IsNullOrWhiteSpace(shipment.physicalWagonAssetId)
                ? "unknown" : shipment.physicalWagonAssetId;
            return new LotIdentity(
                key, shipment.cargoSkuId, shipment.PlannedQuantityUnits,
                CustodyTransferLog.WagonCustodian(wagonId),
                string.Empty,
                string.Empty,
                $"Freight shipments carry no provenance field; this shipment carries cargo lot '{shipment.cargoLotId}' (planned {shipment.PlannedQuantityUnits} units of '{shipment.cargoSkuId}'); no key is invented.",
                key.AuthorityId);
        }

        private void Note(string message)
        {
            if (!string.IsNullOrWhiteSpace(message)) diagnostics.Add(message);
        }
    }
}
