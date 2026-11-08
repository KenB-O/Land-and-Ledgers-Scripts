// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice 3 — personal and player inventory read and custody
// layer (P9 plan section 2.5, directive Priority B). A READ-ONLY query
// layer over the authorities that already hold the goods: the shopping
// loop's in-transit custody batches (via HouseholdShoppingReadModel),
// the household inventories (via HouseholdInventoryRegistry), and the
// recorded equipment-access grants (via EquipmentAccessResolver).
//
// The read model MUTATES NOTHING: it creates no stock, moves no stock,
// moves no cash, and changes no flow. It keeps no copy of any quantity —
// every call re-reads its authorities, so two reads of the same state
// agree, and a read can never drift from the authority it reports.
//
// Ownership and custody stay distinct in every answer. Ownership comes
// from the owning authority fields (HouseholdItemLot.OwnerPersonId, the
// equipment grant's holder record); custody comes from the holder fields
// (CarriedGoodsBatch.HolderPersonId and Custody, the owning household id
// on a carried batch). The two are never merged into one field: a carried
// batch names its holder person AND its owning household, and an owned
// lot names its owner person AND its household.
//
// Unknown-first rule (P9 plan section 2.4): where the underlying
// authority carries no condition, mass, or volume, the answer reports
// Unknown explicitly — never a default condition, never zero. In
// particular:
//   - No authored on-foot person mass or volume limit exists, so
//     RemainingCarryUnits answers in units only (the live shopping
//     carry cap, ShoppingLoopOptions.CarryCapacityUnits) and reports
//     mass and volume remaining as Unknown — the same Unknown the F2
//     OnFootPerson capacity query returns for the same reason.
//   - No portable containers (bags, baskets, pockets, lunch pails,
//     saddlebags) exist anywhere in the tree, so ItemsInCarriedContainers
//     always returns empty with that reason named. No container is
//     invented here.
//   - A HouseholdItemLot carries no condition field (its QualityBand is
//     a string facet, not a condition), so an owned lot's condition is
//     Unknown. A Bakery bread lot DOES carry a condition
//     (BakeryGoodsCondition Fresh/DayOld/Stale); FromBakeryCondition is
//     the 1:1 translation of that authority's vocabulary into this
//     layer's, for the slice that reads bakery lots (Slice 5).
//
// Priority V1: the shopping loop owns a read model as its production
// custody answer (HouseholdShoppingLoop.PersonInventory) and verifies
// each deposit through it. The read model is also constructed in
// tests against the real authorities (the P1 Slice 0/6 real-authority,
// test-side driver pattern). No new transfer mechanism lives here —
// transfers are Slice 4 (F3/F4). The purchase-to-household-storage
// sequence is driven through the existing shopping-loop receipt path.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Storage;

namespace LandLedgers.Population
{
    /// <summary>
    /// The condition of one item as an authority reports it. Unknown is
    /// the honest answer where the authority carries no condition — never
    /// a default Fresh. Fresh/DayOld/Stale are the Bakery bread-lot
    /// conditions (BakeryGoodsCondition), translated 1:1 by
    /// <see cref="PersonInventoryReadModel.FromBakeryCondition"/>.
    /// </summary>
    public enum PersonItemCondition
    {
        Unknown = 0,
        Fresh = 1,
        DayOld = 2,
        Stale = 3,
    }

    /// <summary>
    /// Priority B read model: what one person carries, owns, and holds —
    /// answered by reading the existing authorities only.
    /// </summary>
    public sealed class PersonInventoryReadModel
    {
        /// <summary>
        /// One batch of goods a person currently carries. Custody is the
        /// holder's state (InHand or Wagon — an ArrangedDelivery batch is
        /// with the delivery, not carried by the person, and a received
        /// batch has left custody); ownership is the owning household id,
        /// a different field from the holder person id on purpose.
        /// Condition is Unknown: a carried batch carries provenance and a
        /// receipt id, not a condition.
        /// </summary>
        public sealed class CarriedItem
        {
            public string ItemId { get; }
            public int Units { get; }
            public string BatchId { get; }
            public string ReceiptId { get; }
            public string Provenance { get; }
            public int PurchaseDayIndex { get; }
            public GoodsCustodyState Custody { get; }
            public int HolderPersonId { get; }
            public int OwningHouseholdId { get; }
            public int UnitPriceCents { get; }
            public PersonItemCondition Condition { get; }

            public CarriedItem(
                string itemId, int units, string batchId, string receiptId,
                string provenance, int purchaseDayIndex, GoodsCustodyState custody,
                int holderPersonId, int owningHouseholdId, int unitPriceCents,
                PersonItemCondition condition)
            {
                ItemId = itemId ?? string.Empty;
                Units = units;
                BatchId = batchId ?? string.Empty;
                ReceiptId = receiptId ?? string.Empty;
                Provenance = provenance ?? string.Empty;
                PurchaseDayIndex = purchaseDayIndex;
                Custody = custody;
                HolderPersonId = holderPersonId;
                OwningHouseholdId = owningHouseholdId;
                UnitPriceCents = unitPriceCents;
                Condition = condition;
            }
        }

        /// <summary>
        /// One household lot owned by a person (OwnerPersonId facet).
        /// AvailableUnits is the consumable pool (QuantityUnits minus
        /// HeldUnits); HeldUnits are reserved for a commitment and are
        /// NOT available for consumption. Condition is Unknown: a
        /// household lot carries no condition field.
        /// </summary>
        public sealed class OwnedHouseholdLot
        {
            public int LotId { get; }
            public int HouseholdId { get; }
            public string ItemId { get; }
            public int AvailableUnits { get; }
            public int HeldUnits { get; }
            public int QuantityUnits { get; }
            public string SourceLabel { get; }
            public int AcquiredDayIndex { get; }
            public int OwnerPersonId { get; }
            public PersonItemCondition Condition { get; }

            public OwnedHouseholdLot(
                int lotId, int householdId, string itemId, int availableUnits,
                int heldUnits, int quantityUnits, string sourceLabel,
                int acquiredDayIndex, int ownerPersonId, PersonItemCondition condition)
            {
                LotId = lotId;
                HouseholdId = householdId;
                ItemId = itemId ?? string.Empty;
                AvailableUnits = availableUnits;
                HeldUnits = heldUnits;
                QuantityUnits = quantityUnits;
                SourceLabel = sourceLabel ?? string.Empty;
                AcquiredDayIndex = acquiredDayIndex;
                OwnerPersonId = ownerPersonId;
                Condition = condition;
            }
        }

        /// <summary>
        /// One equipment access grant held by a person. Custody kind is
        /// the grant's own kind (Owned never appears here: ownership is
        /// answered by EquipmentAccessResolver.HasAccess through the
        /// caller's owned-kinds function, and the resolver holds no
        /// owned-kinds record to enumerate — see GrantsFor). ExpiryDayIndex
        /// -1 means no expiry. IsActive mirrors the grant's own rule.
        /// </summary>
        public sealed class HeldEquipment
        {
            public string GrantId { get; }
            public string EquipmentKind { get; }
            public string AssetId { get; }
            public EquipmentCustodyKind Custody { get; }
            public string Granter { get; }
            public int ExpiryDayIndex { get; }

            public HeldEquipment(
                string grantId, string equipmentKind, string assetId,
                EquipmentCustodyKind custody, string granter, int expiryDayIndex)
            {
                GrantId = grantId ?? string.Empty;
                EquipmentKind = equipmentKind ?? string.Empty;
                AssetId = assetId ?? string.Empty;
                Custody = custody;
                Granter = granter ?? string.Empty;
                ExpiryDayIndex = expiryDayIndex;
            }

            public bool IsActive(int dayIndex) => ExpiryDayIndex < 0 || dayIndex <= ExpiryDayIndex;
        }

        /// <summary>
        /// What a person can still carry: units only. Mass and volume
        /// remaining are Unknown — no authored person mass or volume limit
        /// exists (the F2 OnFootPerson query returns Unknown for the same
        /// reason), so no kg or litre person limit is computed here.
        /// UnitsRemaining never goes below zero.
        /// </summary>
        public struct PersonCarryRemaining
        {
            public int UnitsRemaining { get; }
            public PhysicalMeasure MassRemainingKg { get; }
            public PhysicalMeasure VolumeRemainingLiters { get; }
            public string Reason { get; }

            public PersonCarryRemaining(
                int unitsRemaining, PhysicalMeasure massRemainingKg,
                PhysicalMeasure volumeRemainingLiters, string reason)
            {
                UnitsRemaining = unitsRemaining;
                MassRemainingKg = massRemainingKg;
                VolumeRemainingLiters = volumeRemainingLiters;
                Reason = reason ?? string.Empty;
            }
        }

        /// <summary>
        /// The contents of a person's carried containers. Always empty in
        /// this slice: no portable containers exist in the tree, so
        /// <see cref="Reason"/> names that instead of inventing one.
        /// </summary>
        public sealed class CarriedContainerContents
        {
            public IReadOnlyList<CarriedItem> Items { get; }
            public string Reason { get; }

            public CarriedContainerContents(IReadOnlyList<CarriedItem> items, string reason)
            {
                Items = items ?? new List<CarriedItem>().AsReadOnly();
                Reason = reason ?? string.Empty;
            }
        }

        /// <summary>The reason ItemsInCarriedContainers is always empty.</summary>
        public const string NoCarriedContainersReason =
            "No portable containers exist in the tree: no bags, baskets, pockets, " +
            "lunch pails, or saddlebags are represented, so no item can be in a carried container.";

        private readonly HouseholdShoppingReadModel shoppingReadModel;
        private readonly HouseholdInventoryRegistry inventoryRegistry;
        private readonly EquipmentAccessResolver equipmentResolver;
        private readonly int carryCapacityUnits;

        /// <param name="shoppingReadModel">Custody authority (required).</param>
        /// <param name="inventoryRegistry">Household-lot authority (required).</param>
        /// <param name="equipmentResolver">Equipment-grant authority; null means no equipment answers (empty lists, never an error).</param>
        /// <param name="carryOptions">The live shopping-loop options carrying the carry cap; null means the live default cap (a fresh ShoppingLoopOptions).</param>
        public PersonInventoryReadModel(
            HouseholdShoppingReadModel shoppingReadModel,
            HouseholdInventoryRegistry inventoryRegistry,
            EquipmentAccessResolver equipmentResolver = null,
            ShoppingLoopOptions carryOptions = null)
        {
            this.shoppingReadModel = shoppingReadModel ?? throw new ArgumentNullException(nameof(shoppingReadModel));
            this.inventoryRegistry = inventoryRegistry ?? throw new ArgumentNullException(nameof(inventoryRegistry));
            this.equipmentResolver = equipmentResolver;
            carryCapacityUnits = carryOptions != null
                ? carryOptions.CarryCapacityUnits
                : new ShoppingLoopOptions().CarryCapacityUnits;
        }

        /// <summary>The live shopping carry cap this read model answers against (units).</summary>
        public int CarryCapacityUnits => carryCapacityUnits;

        /// <summary>
        /// Every batch the person currently carries (Custody InHand or
        /// Wagon). An ArrangedDelivery batch is with the delivery, not
        /// carried by the person, so it is not listed here. A person who
        /// carries nothing — including an unknown person id — gets an
        /// empty list, never an error. The returned list is a fresh
        /// snapshot each call; the underlying batches are the loop's.
        /// </summary>
        public IReadOnlyList<CarriedItem> CarriedItems(int personId)
        {
            var result = new List<CarriedItem>();
            foreach (CarriedGoodsBatch batch in shoppingReadModel.PersonCustody(personId))
            {
                if (batch == null) continue;
                if (batch.Custody != GoodsCustodyState.InHand && batch.Custody != GoodsCustodyState.Wagon) continue;
                result.Add(new CarriedItem(
                    batch.ItemId, batch.Units, batch.BatchId, batch.ReceiptId,
                    batch.Provenance, batch.PurchaseDayIndex, batch.Custody,
                    batch.HolderPersonId, batch.HouseholdId, batch.UnitPriceCents,
                    PersonItemCondition.Unknown));
            }

            return result;
        }

        /// <summary>
        /// The carried items as a physical load, resolved through the F1
        /// registry: Known totals only where every carried item's spec is
        /// Known on that axis, otherwise Unknown naming the first unknown
        /// item (bread resolves Unknown). An empty carried load resolves
        /// to zero units with no unknown item.
        /// </summary>
        public PhysicalLoad CarriedLoad(int personId)
        {
            var lines = new List<(string ItemId, int Units)>();
            foreach (CarriedItem item in CarriedItems(personId))
            {
                lines.Add((item.ItemId, item.Units));
            }

            return new PhysicalLoad(lines);
        }

        /// <summary>
        /// The live shopping carry cap minus the units currently carried,
        /// in units only. Mass and volume remaining are Unknown: no
        /// authored person mass or volume limit exists, so no kg or litre
        /// person limit is computed (the F2 OnFootPerson query returns
        /// Unknown for the same reason).
        /// </summary>
        public PersonCarryRemaining RemainingCarryUnits(int personId)
        {
            int carried = 0;
            foreach (CarriedItem item in CarriedItems(personId))
            {
                carried += Math.Max(0, item.Units);
            }

            return new PersonCarryRemaining(
                Math.Max(0, carryCapacityUnits - carried),
                PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown,
                $"Carry cap is the live shopping-loop cap of {carryCapacityUnits} units " +
                "(ShoppingLoopOptions.CarryCapacityUnits). No authored person mass or volume " +
                "limit exists, so mass and volume remaining are Unknown.");
        }

        /// <summary>
        /// Household lots whose OwnerPersonId facet equals the person id,
        /// across every household in the registry. A lot the household
        /// owns with no person owner (OwnerPersonId -1, the default the
        /// shopping-loop receipt path books) belongs to no person and is
        /// never listed here. Condition is Unknown: a household lot
        /// carries no condition field.
        /// </summary>
        public IReadOnlyList<OwnedHouseholdLot> HouseholdLotsOwnedBy(int personId)
        {
            var result = new List<OwnedHouseholdLot>();
            // The registry's existing enumeration surface is its save
            // export (ExportState): the read model reads the exported
            // states and copies the matching lots' values into snapshots.
            // It writes nothing and keeps no copy of any quantity between
            // calls — a second read re-exports and must agree.
            var states = new List<HouseholdInventoryState>();
            inventoryRegistry.ExportState(states);
            foreach (HouseholdInventoryState state in states)
            {
                if (state == null || state.Lots == null) continue;
                foreach (HouseholdItemLot lot in state.Lots)
                {
                    if (lot == null || lot.OwnerPersonId != personId) continue;
                    result.Add(new OwnedHouseholdLot(
                        lot.LotId, state.HouseholdId, lot.ItemId,
                        lot.AvailableUnits, Math.Max(0, lot.HeldUnits),
                        Math.Max(0, lot.QuantityUnits), lot.SourceLabel,
                        lot.AcquiredDayIndex, lot.OwnerPersonId,
                        PersonItemCondition.Unknown));
                }
            }

            return result;
        }

        /// <summary>
        /// Every equipment access grant held by the person (HolderKind
        /// "person", HolderId the person id), with its custody kind,
        /// granter, and expiry. All grants are listed regardless of
        /// expiry — the expiry day is on the answer so the caller can
        /// judge it; use <see cref="EquipmentHeld(int,int)"/> for the
        /// active-only view. A null resolver, or a person with no grants
        /// (including an unknown person id), gives an empty list, never
        /// an error. Owned equipment is not listed: the resolver holds no
        /// owned-kinds record to enumerate.
        /// </summary>
        public IReadOnlyList<HeldEquipment> EquipmentHeld(int personId)
        {
            var result = new List<HeldEquipment>();
            if (equipmentResolver == null) return result;
            foreach (EquipmentAccessGrant grant in equipmentResolver.GrantsFor("person", personId.ToString()))
            {
                if (grant == null) continue;
                result.Add(new HeldEquipment(
                    grant.GrantId, grant.EquipmentKind, grant.AssetId,
                    grant.Custody, grant.GranterName, grant.ExpiryDayIndex));
            }

            return result;
        }

        /// <summary>
        /// The equipment grants held by the person that are active on the
        /// given day (expiry -1 means no expiry). Expired grants are
        /// excluded here; see <see cref="EquipmentHeld(int)"/> for the
        /// unfiltered view.
        /// </summary>
        public IReadOnlyList<HeldEquipment> EquipmentHeld(int personId, int dayIndex)
        {
            var result = new List<HeldEquipment>();
            foreach (HeldEquipment held in EquipmentHeld(personId))
            {
                if (held.IsActive(dayIndex)) result.Add(held);
            }

            return result;
        }

        /// <summary>
        /// The items in the person's carried containers. Always empty:
        /// no portable containers exist in the tree, so there is nothing
        /// to list and <see cref="CarriedContainerContents.Reason"/> names
        /// that. No container is invented. An unknown person id gets the
        /// same empty answer, never an error.
        /// </summary>
        public CarriedContainerContents ItemsInCarriedContainers(int personId)
        {
            return new CarriedContainerContents(
                new List<CarriedItem>().AsReadOnly(), NoCarriedContainersReason);
        }

        /// <summary>
        /// 1:1 translation of a Bakery bread lot's condition vocabulary
        /// (BakeryGoodsCondition) into this layer's: Fresh, DayOld and
        /// Stale map to themselves; Unspecified — the lot carries no
        /// condition — maps to Unknown. Translation only: it invents no
        /// condition for any lot.
        /// </summary>
        public static PersonItemCondition FromBakeryCondition(BakeryGoodsCondition condition)
        {
            switch (condition)
            {
                case BakeryGoodsCondition.Fresh: return PersonItemCondition.Fresh;
                case BakeryGoodsCondition.DayOld: return PersonItemCondition.DayOld;
                case BakeryGoodsCondition.Stale: return PersonItemCondition.Stale;
                default: return PersonItemCondition.Unknown;
            }
        }
    }
}
