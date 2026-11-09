// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P1a — hand authority core (plan section 8b).
// A plain domain authority that records which lot is in which hand for one
// person: two slots, left and right, each holding at most one LotKey plus
// its BatchId, or empty. Allocate, release, and transfer operations only.
// It creates no stock, moves no stock, and changes no price.
//
// Vocabulary warning (plan section 5): the word InHand already exists as
// a GoodsCustodyState value meaning a whole batch is in a shopper's
// custody. It is NOT reused here. Per-hand allocation uses HandSlot
// (Left / Right) and HandOccupancy, never the InHand label.
//
// Hand requirement is a caller-supplied parameter on each allocate call
// (coordinator decision on gap G1, 2026-11-08): refused when none is
// supplied. No requirement is derived from mass, name, or item category,
// and no authored hand-requirement table exists yet.
//
// Every refusal names the missing hand, the occupying lot, or the missing
// hand requirement, never a bare false. The hand causes live on
// CustodyTransferLog (moved there in P1b). Every successful operation records
// a CustodyTransferLog entry with a new hand cause ONLY when a log
// instance was supplied to the constructor AND a custody context was
// supplied to the call, so the authority stays usable in tests without a
// log. The log is an audit trail only and never decides anything.
//
// Category B on landing: production code, not yet wired into any live
// path. Save wiring waits for P6; the HandOccupancy DTO is plain data
// only and references lots by LotKey and BatchId, never duplicating lot
// contents.

using System;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;

namespace LandLedgers.Population
{
    /// <summary>Which hand a one-handed operation names. There are exactly two hands.</summary>
    public enum HandSlot
    {
        Left,
        Right
    }

    /// <summary>
    /// How many hands an item needs, supplied by the caller on each allocate
    /// call (gap G1). Unknown means no requirement was supplied and every
    /// allocate refuses. NoHandCarryable means the item cannot be carried in
    /// a hand at all. No value here is derived from mass, name, or category.
    /// </summary>
    public enum HandRequirement
    {
        Unknown,
        OneHanded,
        TwoHanded,
        NoHandCarryable
    }

    /// <summary>
    /// Plain hand-state DTO: which lot (by LotKey plus BatchId) sits in each
    /// hand of one person, or empty. IsTwoHandedHold is true only when both
    /// slots hold the same lot as one two-handed hold; two different lots in
    /// two hands is not a two-handed hold. Plain data only — no logic beyond
    /// derived free-hand reads, and no lot contents are duplicated here.
    /// </summary>
    public sealed class HandOccupancy
    {
        public HandOccupancy(
            int personId,
            LotKey leftLotKey, string leftBatchId,
            LotKey rightLotKey, string rightBatchId,
            bool isTwoHandedHold,
            HandRequirement leftRequirement = HandRequirement.Unknown,
            HandRequirement rightRequirement = HandRequirement.Unknown,
            string lastRefusalReason = "",
            string lastRefusalOperation = "")
        {
            PersonId = personId;
            LeftLotKey = leftLotKey;
            LeftBatchId = leftBatchId ?? string.Empty;
            RightLotKey = rightLotKey;
            RightBatchId = rightBatchId ?? string.Empty;
            IsTwoHandedHold = isTwoHandedHold;
            LeftRequirement = leftRequirement;
            RightRequirement = rightRequirement;
            LastRefusalReason = lastRefusalReason ?? string.Empty;
            LastRefusalOperation = lastRefusalOperation ?? string.Empty;
        }

        public static HandOccupancy Empty(int personId) =>
            new HandOccupancy(personId, LotKey.Empty, string.Empty, LotKey.Empty, string.Empty, false);

        public int PersonId { get; }
        public LotKey LeftLotKey { get; }
        public string LeftBatchId { get; }
        public LotKey RightLotKey { get; }
        public string RightBatchId { get; }
        public bool IsTwoHandedHold { get; }

        /// <summary>The hand requirement retained for the left hand's hold, Unknown when free.</summary>
        public HandRequirement LeftRequirement { get; }

        /// <summary>The hand requirement retained for the right hand's hold, Unknown when free.</summary>
        public HandRequirement RightRequirement { get; }

        /// <summary>Verbatim reason of the most recent refused operation, empty when the last operation succeeded (or none ran).</summary>
        public string LastRefusalReason { get; }

        /// <summary>Operation that produced <see cref="LastRefusalReason"/> (AllocateOneHanded, AllocateTwoHanded, Release, TransferToContainer, or TransferFromContainer), empty when none.</summary>
        public string LastRefusalOperation { get; }

        public bool IsLeftFree => LeftLotKey.IsEmpty;
        public bool IsRightFree => RightLotKey.IsEmpty;
        public int FreeHandCount => (IsLeftFree ? 1 : 0) + (IsRightFree ? 1 : 0);

        public bool IsFree(HandSlot hand) => hand == HandSlot.Left ? IsLeftFree : IsRightFree;

        public LotKey LotIn(HandSlot hand) => hand == HandSlot.Left ? LeftLotKey : RightLotKey;

        public string BatchIn(HandSlot hand) => hand == HandSlot.Left ? LeftBatchId : RightBatchId;
    }

    /// <summary>
    /// Plain custody context for one hand operation, supplied by the caller
    /// when the operation should be recorded in a CustodyTransferLog. The
    /// authority records nothing when this is null, even when a log was
    /// supplied. CauseId is the log's idempotency key for the transfer and
    /// must be unique per operation; DayIndex is
    /// CustodyTransferLog.UnknownDayIndex when the caller holds no day.
    /// </summary>
    public sealed class HandCustodyContext
    {
        public HandCustodyContext(int dayIndex, string itemId, int units, string causeId)
        {
            DayIndex = dayIndex;
            ItemId = itemId ?? string.Empty;
            Units = units;
            CauseId = causeId ?? string.Empty;
        }

        public int DayIndex { get; }
        public string ItemId { get; }
        public int Units { get; }
        public string CauseId { get; }
    }

    /// <summary>The outcome of one hand operation. A refusal always carries a named reason.</summary>
    public sealed class HandOperationResult
    {
        private HandOperationResult(bool succeeded, string reason, LotKey lotKey, string batchId)
        {
            Succeeded = succeeded;
            Reason = reason ?? string.Empty;
            LotKey = lotKey;
            BatchId = batchId ?? string.Empty;
        }

        public static HandOperationResult Ok(string reason, LotKey lotKey, string batchId) =>
            new HandOperationResult(true, reason, lotKey, batchId);

        public static HandOperationResult Ok(string reason) =>
            new HandOperationResult(true, reason, LotKey.Empty, string.Empty);

        public static HandOperationResult Refused(string reason) =>
            new HandOperationResult(false, reason, LotKey.Empty, string.Empty);

        public bool Succeeded { get; }
        public string Reason { get; }

        /// <summary>The lot the operation acted on (allocated, or released), or Empty when none.</summary>
        public LotKey LotKey { get; }

        /// <summary>The batch id of LotKey, or empty when none.</summary>
        public string BatchId { get; }
    }

    /// <summary>
    /// Per-person hand authority. One instance records the two hands of one
    /// person (player and NPC use the same class with different person ids).
    /// A one-handed allocate fills one named free hand. A two-handed allocate
    /// requires both hands free and fills both slots with the same lot. A
    /// release frees one hand, or both hands for a two-handed hold, in one
    /// operation. A transfer moves the lot in one hand into a worn container
    /// the person wears (P2a), or retrieves a contained lot back into a
    /// hand; the source is cleared in the same operation as the destination
    /// is filled, so the lot is in exactly one place at every step.
    /// </summary>
    public sealed class HandOccupancyAuthority
    {
        private readonly CustodyTransferLog log;

        private LotKey leftLotKey = LotKey.Empty;
        private string leftBatchId = string.Empty;
        private LotKey rightLotKey = LotKey.Empty;
        private string rightBatchId = string.Empty;
        private bool isTwoHandedHold;
        private HandRequirement leftRequirement = HandRequirement.Unknown;
        private HandRequirement rightRequirement = HandRequirement.Unknown;
        private string lastRefusalReason = string.Empty;
        private string lastRefusalOperation = string.Empty;

        /// <summary>
        /// Creates the hand authority for one person. The log is optional:
        /// when null, operations complete without any log entry.
        /// </summary>
        public HandOccupancyAuthority(int personId, CustodyTransferLog log = null)
        {
            PersonId = personId;
            this.log = log;
        }

        public int PersonId { get; }

        /// <summary>A copy of the current hand state. The snapshot is plain data; mutating it changes nothing.</summary>
        public HandOccupancy Current =>
            new HandOccupancy(PersonId, leftLotKey, leftBatchId, rightLotKey, rightBatchId, isTwoHandedHold,
                leftRequirement, rightRequirement, lastRefusalReason, lastRefusalOperation);

        public bool IsFree(HandSlot hand) => hand == HandSlot.Left ? leftLotKey.IsEmpty : rightLotKey.IsEmpty;

        public int FreeHandCount => (leftLotKey.IsEmpty ? 1 : 0) + (rightLotKey.IsEmpty ? 1 : 0);

        /// <summary>True when either hand already holds this lot, as a one-handed or a two-handed hold.</summary>
        public bool HoldsLot(LotKey lotKey) =>
            !lotKey.IsEmpty && (leftLotKey.Equals(lotKey) || rightLotKey.Equals(lotKey));

        private string HandName(HandSlot hand) => hand == HandSlot.Left ? "left" : "right";

        private string DescribeLot(LotKey lotKey, string batchId) =>
            "lot " + lotKey + (string.IsNullOrEmpty(batchId) ? string.Empty : " (batch " + batchId + ")");

        /// <summary>
        /// Records the most recent refusal only (TaskDurationDiagnostics
        /// LastRefusal precedent: verbatim reason plus the operation that
        /// produced it, no history list) and returns the refused result.
        /// Every refusal in this authority is routed through here.
        /// </summary>
        private HandOperationResult Refuse(string operation, string reason)
        {
            lastRefusalReason = reason ?? string.Empty;
            lastRefusalOperation = operation ?? string.Empty;
            return HandOperationResult.Refused(reason);
        }

        /// <summary>Clears the last refusal: a stale refusal never survives a later success.</summary>
        private void ClearRefusal()
        {
            lastRefusalReason = string.Empty;
            lastRefusalOperation = string.Empty;
        }

        /// <summary>
        /// Fills one named free hand with one lot. Refuses when no lot is
        /// supplied, when no hand requirement is supplied, when the lot
        /// cannot be carried in a hand, when the lot needs two hands, when
        /// the lot is already held in either hand, or when the named hand is
        /// occupied (naming the occupying lot).
        /// </summary>
        public HandOperationResult AllocateOneHanded(
            LotKey lotKey, string batchId, HandRequirement? requirement, HandSlot hand,
            HandCustodyContext context = null)
        {
            if (lotKey.IsEmpty)
            {
                return Refuse("AllocateOneHanded", 
                    "HandOccupancy: person " + PersonId + " allocate refused — no lot was supplied, so no hand can hold it.");
            }

            if (!requirement.HasValue || requirement.Value == HandRequirement.Unknown)
            {
                return Refuse("AllocateOneHanded", 
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — no hand requirement was supplied; the hand requirement is a caller-supplied parameter and is never derived from mass, name, or item category.");
            }

            if (requirement.Value == HandRequirement.NoHandCarryable)
            {
                return Refuse("AllocateOneHanded", 
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot cannot be carried in a hand.");
            }

            if (requirement.Value == HandRequirement.TwoHanded)
            {
                return Refuse("AllocateOneHanded", 
                    "HandOccupancy: person " + PersonId + " one-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot requires two hands; use a two-handed allocate.");
            }

            if (HoldsLot(lotKey))
            {
                HandSlot heldHand = leftLotKey.Equals(lotKey) ? HandSlot.Left : HandSlot.Right;
                return Refuse("AllocateOneHanded", 
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — the same lot is already held in the " + HandName(heldHand) + " hand; one lot cannot occupy both hands through separate one-handed allocates.");
            }

            if (!IsFree(hand))
            {
                LotKey occupying = hand == HandSlot.Left ? leftLotKey : rightLotKey;
                string occupyingBatch = hand == HandSlot.Left ? leftBatchId : rightBatchId;
                return Refuse("AllocateOneHanded", 
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " to the " + HandName(hand) + " hand refused — the " + HandName(hand) + " hand is occupied by " + DescribeLot(occupying, occupyingBatch) + ".");
            }

            if (hand == HandSlot.Left)
            {
                leftLotKey = lotKey;
                leftBatchId = batchId ?? string.Empty;
                leftRequirement = requirement.Value;
            }
            else
            {
                rightLotKey = lotKey;
                rightBatchId = batchId ?? string.Empty;
                rightRequirement = requirement.Value;
            }

            ClearRefusal();
            RecordPickup(lotKey, context);
            return HandOperationResult.Ok(
                "HandOccupancy: person " + PersonId + " holds " + DescribeLot(lotKey, batchId) + " in the " + HandName(hand) + " hand.",
                lotKey, batchId ?? string.Empty);
        }

        /// <summary>
        /// Fills both hands with one lot as a single two-handed hold. Refuses
        /// when no lot or no hand requirement is supplied, when the lot
        /// cannot be carried in a hand, when the lot needs only one hand, or
        /// when either hand is occupied (naming the occupying lot and hand).
        /// </summary>
        public HandOperationResult AllocateTwoHanded(
            LotKey lotKey, string batchId, HandRequirement? requirement,
            HandCustodyContext context = null)
        {
            if (lotKey.IsEmpty)
            {
                return Refuse("AllocateTwoHanded", 
                    "HandOccupancy: person " + PersonId + " allocate refused — no lot was supplied, so no hand can hold it.");
            }

            if (!requirement.HasValue || requirement.Value == HandRequirement.Unknown)
            {
                return Refuse("AllocateTwoHanded", 
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — no hand requirement was supplied; the hand requirement is a caller-supplied parameter and is never derived from mass, name, or item category.");
            }

            if (requirement.Value == HandRequirement.NoHandCarryable)
            {
                return Refuse("AllocateTwoHanded", 
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot cannot be carried in a hand.");
            }

            if (requirement.Value == HandRequirement.OneHanded)
            {
                return Refuse("AllocateTwoHanded", 
                    "HandOccupancy: person " + PersonId + " two-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot requires one hand; use a one-handed allocate.");
            }

            if (!leftLotKey.IsEmpty)
            {
                return Refuse("AllocateTwoHanded", 
                    "HandOccupancy: person " + PersonId + " two-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the left hand is occupied by " + DescribeLot(leftLotKey, leftBatchId) + "; a two-handed hold requires both hands free.");
            }

            if (!rightLotKey.IsEmpty)
            {
                return Refuse("AllocateTwoHanded", 
                    "HandOccupancy: person " + PersonId + " two-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the right hand is occupied by " + DescribeLot(rightLotKey, rightBatchId) + "; a two-handed hold requires both hands free.");
            }

            leftLotKey = lotKey;
            leftBatchId = batchId ?? string.Empty;
            rightLotKey = lotKey;
            rightBatchId = batchId ?? string.Empty;
            isTwoHandedHold = true;
            leftRequirement = requirement.Value;
            rightRequirement = requirement.Value;

            ClearRefusal();
            RecordPickup(lotKey, context);
            return HandOperationResult.Ok(
                "HandOccupancy: person " + PersonId + " holds " + DescribeLot(lotKey, batchId) + " in both hands.",
                lotKey, batchId ?? string.Empty);
        }

        /// <summary>
        /// Frees one hand. When the hand holds part of a two-handed hold,
        /// both hands are freed in this one operation. Refuses when the named
        /// hand is already empty. Returns the released lot and batch id.
        /// </summary>
        public HandOperationResult Release(HandSlot hand, HandCustodyContext context = null)
        {
            if (IsFree(hand))
            {
                return Refuse("Release", 
                    "HandOccupancy: person " + PersonId + " release refused — the " + HandName(hand) + " hand is already empty; there is nothing to release.");
            }

            LotKey releasedLot = hand == HandSlot.Left ? leftLotKey : rightLotKey;
            string releasedBatch = hand == HandSlot.Left ? leftBatchId : rightBatchId;
            bool wasTwoHanded = isTwoHandedHold;

            if (wasTwoHanded)
            {
                leftLotKey = LotKey.Empty;
                leftBatchId = string.Empty;
                rightLotKey = LotKey.Empty;
                rightBatchId = string.Empty;
                isTwoHandedHold = false;
                leftRequirement = HandRequirement.Unknown;
                rightRequirement = HandRequirement.Unknown;
            }
            else if (hand == HandSlot.Left)
            {
                leftLotKey = LotKey.Empty;
                leftBatchId = string.Empty;
                leftRequirement = HandRequirement.Unknown;
            }
            else
            {
                rightLotKey = LotKey.Empty;
                rightBatchId = string.Empty;
                rightRequirement = HandRequirement.Unknown;
            }

            ClearRefusal();
            RecordRelease(releasedLot, context);
            return HandOperationResult.Ok(
                wasTwoHanded
                    ? "HandOccupancy: person " + PersonId + " released " + DescribeLot(releasedLot, releasedBatch) + " from both hands."
                    : "HandOccupancy: person " + PersonId + " released " + DescribeLot(releasedLot, releasedBatch) + " from the " + HandName(hand) + " hand.",
                releasedLot, releasedBatch);
        }

        /// <summary>
        /// Moves the lot in one hand into a worn container. Steps in fixed
        /// order: refuse when the named hand is empty, when no container
        /// was supplied, when the container is not worn by this person
        /// (naming the current wearer, or that it is unworn), or when the
        /// lot is already in the destination container; then call
        /// container Place, which runs the circular-nesting guard, its own
        /// duplicate guard, and the fit check. Only on Place success is
        /// the hand slot cleared — both slots when the hold is two-handed —
        /// and one log entry is recorded with CauseHandToContainer, from
        /// the person custodian to the container custodian. Every refusal
        /// happens before any state changes, so a refused transfer leaves
        /// the hand holding the lot and the container unchanged. The
        /// evaluator is built here with null authorities: the WornSatchel
        /// leg needs none of them. The item id and units come from the
        /// custody context, falling back to the SaveDto when no context was
        /// supplied; without either, the transfer refuses naming the gap.
        /// </summary>
        public HandOperationResult TransferToContainer(
            HandSlot hand, WornContainer container,
            HandCustodyContext context = null, PerishableLotState.SaveDto? saveDto = null)
        {
            if (IsFree(hand))
            {
                return Refuse("TransferToContainer",
                    "HandOccupancy: person " + PersonId + " hand-to-container transfer refused — the " + HandName(hand) + " hand is empty; there is nothing to transfer.");
            }

            LotKey heldLot = hand == HandSlot.Left ? leftLotKey : rightLotKey;
            string heldBatch = hand == HandSlot.Left ? leftBatchId : rightBatchId;
            bool wasTwoHanded = isTwoHandedHold;

            if (container == null)
            {
                return Refuse("TransferToContainer",
                    "HandOccupancy: person " + PersonId + " hand-to-container transfer of " + DescribeLot(heldLot, heldBatch) + " from the " + HandName(hand) + " hand refused — no container was supplied, so no container can be named as a destination.");
            }

            if (container.WornByPersonId == -1)
            {
                return Refuse("TransferToContainer",
                    "HandOccupancy: person " + PersonId + " hand-to-container transfer of " + DescribeLot(heldLot, heldBatch) + " from the " + HandName(hand) + " hand refused — satchel '" + container.ContainerId + "' is unworn; wear it first.");
            }

            if (container.WornByPersonId != PersonId)
            {
                return Refuse("TransferToContainer",
                    "HandOccupancy: person " + PersonId + " hand-to-container transfer of " + DescribeLot(heldLot, heldBatch) + " from the " + HandName(hand) + " hand refused — satchel '" + container.ContainerId + "' is worn by person " + container.WornByPersonId + ", not person " + PersonId + ".");
            }

            if (container.Contains(heldLot))
            {
                return Refuse("TransferToContainer",
                    "HandOccupancy: person " + PersonId + " hand-to-container transfer of " + DescribeLot(heldLot, heldBatch) + " from the " + HandName(hand) + " hand refused — satchel '" + container.ContainerId + "' already holds that lot; one lot cannot be placed twice.");
            }

            string itemId = context != null ? context.ItemId : (saveDto.HasValue ? saveDto.Value.ItemId : string.Empty);
            int units = context != null ? context.Units : (saveDto.HasValue ? saveDto.Value.Units : 0);
            if (string.IsNullOrWhiteSpace(itemId) || units <= 0)
            {
                return Refuse("TransferToContainer",
                    "HandOccupancy: person " + PersonId + " hand-to-container transfer of " + DescribeLot(heldLot, heldBatch) + " from the " + HandName(hand) + " hand refused — no item id and units were supplied, so fit cannot be checked; the custody context carries them.");
            }

            int dayIndex = context != null ? context.DayIndex : CustodyTransferLog.UnknownDayIndex;
            var evaluator = new PhysicalCapacityEvaluator(null, null, null, null);
            WornContainerOperationResult placed = container.Place(
                PersonId, heldLot, heldBatch, itemId, units, evaluator, dayIndex, saveDto);
            if (!placed.Succeeded)
            {
                return Refuse("TransferToContainer", placed.Reason);
            }

            if (wasTwoHanded)
            {
                leftLotKey = LotKey.Empty;
                leftBatchId = string.Empty;
                rightLotKey = LotKey.Empty;
                rightBatchId = string.Empty;
                isTwoHandedHold = false;
                leftRequirement = HandRequirement.Unknown;
                rightRequirement = HandRequirement.Unknown;
            }
            else if (hand == HandSlot.Left)
            {
                leftLotKey = LotKey.Empty;
                leftBatchId = string.Empty;
                leftRequirement = HandRequirement.Unknown;
            }
            else
            {
                rightLotKey = LotKey.Empty;
                rightBatchId = string.Empty;
                rightRequirement = HandRequirement.Unknown;
            }

            ClearRefusal();
            RecordTransfer(
                heldLot, CustodyTransferLog.PersonCustodian(PersonId),
                CustodyTransferLog.ContainerCustodian(container.ContainerId),
                CustodyTransferLog.CauseHandToContainer, context);
            return HandOperationResult.Ok(
                "HandOccupancy: person " + PersonId + " placed " + DescribeLot(heldLot, heldBatch) + " from the " + HandName(hand) + " hand into satchel '" + container.ContainerId + "'.",
                heldLot, heldBatch);
        }

        /// <summary>
        /// Retrieves a lot from a worn container back into a hand, the
        /// mirror of TransferToContainer. Steps in fixed order: refuse
        /// when no container was supplied, when the container is not worn
        /// by this person (naming the current wearer, or that it is
        /// unworn), when the container does not hold the lot, or when no
        /// hand requirement is supplied or the requirement is Unknown or
        /// NoHandCarryable; then, for a one-handed requirement, refuse
        /// when the named hand is occupied or the lot is already held in a
        /// hand, and for a two-handed requirement refuse when either hand
        /// is occupied. Only when every check passes is the container
        /// entry taken and the hand slot — or both slots for a two-handed
        /// retrieval — filled, and one log entry recorded with
        /// CauseContainerToHand, from the container custodian to the
        /// person custodian. A refused retrieval leaves the container
        /// entry intact.
        /// </summary>
        public HandOperationResult TransferFromContainer(
            WornContainer container, LotKey lotKey, HandSlot hand, HandRequirement? requirement,
            HandCustodyContext context = null, PerishableLotState.SaveDto? saveDto = null)
        {
            if (container == null)
            {
                return Refuse("TransferFromContainer",
                    "HandOccupancy: person " + PersonId + " container-to-hand transfer refused — no container was supplied, so no container can be named as a source.");
            }

            if (container.WornByPersonId == -1)
            {
                return Refuse("TransferFromContainer",
                    "HandOccupancy: person " + PersonId + " container-to-hand transfer of lot " + lotKey + " refused — satchel '" + container.ContainerId + "' is unworn; wear it first.");
            }

            if (container.WornByPersonId != PersonId)
            {
                return Refuse("TransferFromContainer",
                    "HandOccupancy: person " + PersonId + " container-to-hand transfer of lot " + lotKey + " refused — satchel '" + container.ContainerId + "' is worn by person " + container.WornByPersonId + ", not person " + PersonId + ".");
            }

            WornContainerEntry entry = container.EntryFor(lotKey);
            if (entry == null)
            {
                return Refuse("TransferFromContainer",
                    "HandOccupancy: person " + PersonId + " container-to-hand transfer of lot " + lotKey + " refused — satchel '" + container.ContainerId + "' does not hold that lot; there is nothing to retrieve.");
            }

            if (!requirement.HasValue || requirement.Value == HandRequirement.Unknown)
            {
                return Refuse("TransferFromContainer",
                    "HandOccupancy: person " + PersonId + " container-to-hand transfer of " + DescribeLot(lotKey, entry.BatchId) + " refused — no hand requirement was supplied; the hand requirement is a caller-supplied parameter and is never derived from mass, name, or item category.");
            }

            if (requirement.Value == HandRequirement.NoHandCarryable)
            {
                return Refuse("TransferFromContainer",
                    "HandOccupancy: person " + PersonId + " container-to-hand transfer of " + DescribeLot(lotKey, entry.BatchId) + " refused — the lot cannot be carried in a hand.");
            }

            if (requirement.Value == HandRequirement.OneHanded)
            {
                if (HoldsLot(lotKey))
                {
                    HandSlot heldHand = leftLotKey.Equals(lotKey) ? HandSlot.Left : HandSlot.Right;
                    return Refuse("TransferFromContainer",
                        "HandOccupancy: person " + PersonId + " container-to-hand transfer of " + DescribeLot(lotKey, entry.BatchId) + " refused — the same lot is already held in the " + HandName(heldHand) + " hand.");
                }

                if (!IsFree(hand))
                {
                    LotKey occupying = hand == HandSlot.Left ? leftLotKey : rightLotKey;
                    string occupyingBatch = hand == HandSlot.Left ? leftBatchId : rightBatchId;
                    return Refuse("TransferFromContainer",
                        "HandOccupancy: person " + PersonId + " container-to-hand transfer of " + DescribeLot(lotKey, entry.BatchId) + " to the " + HandName(hand) + " hand refused — the " + HandName(hand) + " hand is occupied by " + DescribeLot(occupying, occupyingBatch) + ".");
                }
            }
            else
            {
                if (!leftLotKey.IsEmpty)
                {
                    return Refuse("TransferFromContainer",
                        "HandOccupancy: person " + PersonId + " container-to-hand transfer of " + DescribeLot(lotKey, entry.BatchId) + " refused — the left hand is occupied by " + DescribeLot(leftLotKey, leftBatchId) + "; a two-handed hold requires both hands free.");
                }

                if (!rightLotKey.IsEmpty)
                {
                    return Refuse("TransferFromContainer",
                        "HandOccupancy: person " + PersonId + " container-to-hand transfer of " + DescribeLot(lotKey, entry.BatchId) + " refused — the right hand is occupied by " + DescribeLot(rightLotKey, rightBatchId) + "; a two-handed hold requires both hands free.");
                }
            }

            WornContainerOperationResult taken = container.Take(lotKey);
            if (!taken.Succeeded)
            {
                return Refuse("TransferFromContainer", taken.Reason);
            }

            if (requirement.Value == HandRequirement.TwoHanded)
            {
                leftLotKey = lotKey;
                leftBatchId = entry.BatchId;
                rightLotKey = lotKey;
                rightBatchId = entry.BatchId;
                isTwoHandedHold = true;
                leftRequirement = requirement.Value;
                rightRequirement = requirement.Value;
            }
            else if (hand == HandSlot.Left)
            {
                leftLotKey = lotKey;
                leftBatchId = entry.BatchId;
                leftRequirement = requirement.Value;
            }
            else
            {
                rightLotKey = lotKey;
                rightBatchId = entry.BatchId;
                rightRequirement = requirement.Value;
            }

            ClearRefusal();
            RecordTransfer(
                lotKey, CustodyTransferLog.ContainerCustodian(container.ContainerId),
                CustodyTransferLog.PersonCustodian(PersonId),
                CustodyTransferLog.CauseContainerToHand, context);
            return HandOperationResult.Ok(
                requirement.Value == HandRequirement.TwoHanded
                    ? "HandOccupancy: person " + PersonId + " retrieved " + DescribeLot(lotKey, entry.BatchId) + " from satchel '" + container.ContainerId + "' into both hands."
                    : "HandOccupancy: person " + PersonId + " retrieved " + DescribeLot(lotKey, entry.BatchId) + " from satchel '" + container.ContainerId + "' into the " + HandName(hand) + " hand.",
                lotKey, entry.BatchId);
        }

        private void RecordTransfer(
            LotKey lotKey, string fromCustodian, string toCustodian, string causeKind, HandCustodyContext context)
        {
            if (log == null || context == null) return;
            var record = new CustodyTransferRecord(
                context.DayIndex, context.ItemId, context.Units,
                lotKey, lotKey, fromCustodian, toCustodian, context.CauseId, causeKind);
            string diagnostic;
            log.Record(record, out diagnostic);
        }

        private void RecordPickup(LotKey lotKey, HandCustodyContext context)
        {
            if (log == null || context == null) return;
            var record = new CustodyTransferRecord(
                context.DayIndex, context.ItemId, context.Units,
                LotKey.Empty, lotKey,
                CustodyTransferLog.PersonCustodian(PersonId),
                CustodyTransferLog.PersonCustodian(PersonId),
                context.CauseId, CustodyTransferLog.CauseHandPickup);
            string diagnostic;
            log.Record(record, out diagnostic);
            log.Note("HandOccupancy: hand-pickup for " + lotKey + " recorded with an empty source key — the lot's prior custody is not a hand and is not named by this authority.");
        }

        private void RecordRelease(LotKey lotKey, HandCustodyContext context)
        {
            if (log == null || context == null) return;
            var record = new CustodyTransferRecord(
                context.DayIndex, context.ItemId, context.Units,
                lotKey, LotKey.Empty,
                CustodyTransferLog.PersonCustodian(PersonId),
                CustodyTransferLog.PersonCustodian(PersonId),
                context.CauseId, CustodyTransferLog.CauseHandRelease);
            string diagnostic;
            log.Record(record, out diagnostic);
            log.Note("HandOccupancy: hand-release for " + lotKey + " recorded with an empty destination key — no placement surface exists yet in P1a; the lot stays in the person's custody outside any hand.");
        }
    }
}
