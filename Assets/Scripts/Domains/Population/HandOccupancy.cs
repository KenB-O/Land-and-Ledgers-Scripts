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
// hand requirement, never a bare false. Every successful operation records
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
            bool isTwoHandedHold)
        {
            PersonId = personId;
            LeftLotKey = leftLotKey;
            LeftBatchId = leftBatchId ?? string.Empty;
            RightLotKey = rightLotKey;
            RightBatchId = rightBatchId ?? string.Empty;
            IsTwoHandedHold = isTwoHandedHold;
        }

        public static HandOccupancy Empty(int personId) =>
            new HandOccupancy(personId, LotKey.Empty, string.Empty, LotKey.Empty, string.Empty, false);

        public int PersonId { get; }
        public LotKey LeftLotKey { get; }
        public string LeftBatchId { get; }
        public LotKey RightLotKey { get; }
        public string RightBatchId { get; }
        public bool IsTwoHandedHold { get; }

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
    /// operation. A transfer to a container is refused in P1a because no
    /// container type exists yet (P2), and the refusal names that gap.
    /// </summary>
    public sealed class HandOccupancyAuthority
    {
        /// <summary>Hand causes for CustodyTransferLog entries. Additive; no existing cause is renamed.</summary>
        public const string CauseHandPickup = "hand-pickup";
        public const string CauseHandRelease = "hand-release";
        public const string CauseHandToContainer = "hand-to-container";
        public const string CauseContainerToHand = "container-to-hand";

        private readonly CustodyTransferLog log;

        private LotKey leftLotKey = LotKey.Empty;
        private string leftBatchId = string.Empty;
        private LotKey rightLotKey = LotKey.Empty;
        private string rightBatchId = string.Empty;
        private bool isTwoHandedHold;

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
            new HandOccupancy(PersonId, leftLotKey, leftBatchId, rightLotKey, rightBatchId, isTwoHandedHold);

        public bool IsFree(HandSlot hand) => hand == HandSlot.Left ? leftLotKey.IsEmpty : rightLotKey.IsEmpty;

        public int FreeHandCount => (leftLotKey.IsEmpty ? 1 : 0) + (rightLotKey.IsEmpty ? 1 : 0);

        /// <summary>True when either hand already holds this lot, as a one-handed or a two-handed hold.</summary>
        public bool HoldsLot(LotKey lotKey) =>
            !lotKey.IsEmpty && (leftLotKey.Equals(lotKey) || rightLotKey.Equals(lotKey));

        private string HandName(HandSlot hand) => hand == HandSlot.Left ? "left" : "right";

        private string DescribeLot(LotKey lotKey, string batchId) =>
            "lot " + lotKey + (string.IsNullOrEmpty(batchId) ? string.Empty : " (batch " + batchId + ")");

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
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate refused — no lot was supplied, so no hand can hold it.");
            }

            if (!requirement.HasValue || requirement.Value == HandRequirement.Unknown)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — no hand requirement was supplied; the hand requirement is a caller-supplied parameter and is never derived from mass, name, or item category.");
            }

            if (requirement.Value == HandRequirement.NoHandCarryable)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot cannot be carried in a hand.");
            }

            if (requirement.Value == HandRequirement.TwoHanded)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " one-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot requires two hands; use a two-handed allocate.");
            }

            if (HoldsLot(lotKey))
            {
                HandSlot heldHand = leftLotKey.Equals(lotKey) ? HandSlot.Left : HandSlot.Right;
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — the same lot is already held in the " + HandName(heldHand) + " hand; one lot cannot occupy both hands through separate one-handed allocates.");
            }

            if (!IsFree(hand))
            {
                LotKey occupying = hand == HandSlot.Left ? leftLotKey : rightLotKey;
                string occupyingBatch = hand == HandSlot.Left ? leftBatchId : rightBatchId;
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " to the " + HandName(hand) + " hand refused — the " + HandName(hand) + " hand is occupied by " + DescribeLot(occupying, occupyingBatch) + ".");
            }

            if (hand == HandSlot.Left)
            {
                leftLotKey = lotKey;
                leftBatchId = batchId ?? string.Empty;
            }
            else
            {
                rightLotKey = lotKey;
                rightBatchId = batchId ?? string.Empty;
            }

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
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate refused — no lot was supplied, so no hand can hold it.");
            }

            if (!requirement.HasValue || requirement.Value == HandRequirement.Unknown)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — no hand requirement was supplied; the hand requirement is a caller-supplied parameter and is never derived from mass, name, or item category.");
            }

            if (requirement.Value == HandRequirement.NoHandCarryable)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot cannot be carried in a hand.");
            }

            if (requirement.Value == HandRequirement.OneHanded)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " two-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the lot requires one hand; use a one-handed allocate.");
            }

            if (!leftLotKey.IsEmpty)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " two-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the left hand is occupied by " + DescribeLot(leftLotKey, leftBatchId) + "; a two-handed hold requires both hands free.");
            }

            if (!rightLotKey.IsEmpty)
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " two-handed allocate of " + DescribeLot(lotKey, batchId) + " refused — the right hand is occupied by " + DescribeLot(rightLotKey, rightBatchId) + "; a two-handed hold requires both hands free.");
            }

            leftLotKey = lotKey;
            leftBatchId = batchId ?? string.Empty;
            rightLotKey = lotKey;
            rightBatchId = batchId ?? string.Empty;
            isTwoHandedHold = true;

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
                return HandOperationResult.Refused(
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
            }
            else if (hand == HandSlot.Left)
            {
                leftLotKey = LotKey.Empty;
                leftBatchId = string.Empty;
            }
            else
            {
                rightLotKey = LotKey.Empty;
                rightBatchId = string.Empty;
            }

            RecordRelease(releasedLot, context);
            return HandOperationResult.Ok(
                wasTwoHanded
                    ? "HandOccupancy: person " + PersonId + " released " + DescribeLot(releasedLot, releasedBatch) + " from both hands."
                    : "HandOccupancy: person " + PersonId + " released " + DescribeLot(releasedLot, releasedBatch) + " from the " + HandName(hand) + " hand.",
                releasedLot, releasedBatch);
        }

        /// <summary>
        /// Moves the lot in one hand into a container. Refused in P1a: no
        /// container type exists yet (P2), so no container can be named as a
        /// destination. The hand keeps its lot; nothing moves.
        /// </summary>
        public HandOperationResult TransferToContainer(HandSlot hand, string containerId)
        {
            if (IsFree(hand))
            {
                return HandOperationResult.Refused(
                    "HandOccupancy: person " + PersonId + " hand-to-container transfer refused — the " + HandName(hand) + " hand is empty; there is nothing to transfer.");
            }

            LotKey heldLot = hand == HandSlot.Left ? leftLotKey : rightLotKey;
            string heldBatch = hand == HandSlot.Left ? leftBatchId : rightBatchId;
            return HandOperationResult.Refused(
                "HandOccupancy: person " + PersonId + " hand-to-container transfer of " + DescribeLot(heldLot, heldBatch) + " from the " + HandName(hand) + " hand refused — no container type exists yet, so "
                + (string.IsNullOrEmpty(containerId) ? "no container can be named as a destination" : "container '" + containerId + "' cannot be a destination")
                + "; hand-to-container transfers arrive with P2.");
        }

        private void RecordPickup(LotKey lotKey, HandCustodyContext context)
        {
            if (log == null || context == null) return;
            var record = new CustodyTransferRecord(
                context.DayIndex, context.ItemId, context.Units,
                LotKey.Empty, lotKey,
                CustodyTransferLog.PersonCustodian(PersonId),
                CustodyTransferLog.PersonCustodian(PersonId),
                context.CauseId, CauseHandPickup);
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
                context.CauseId, CauseHandRelease);
            string diagnostic;
            log.Record(record, out diagnostic);
            log.Note("HandOccupancy: hand-release for " + lotKey + " recorded with an empty destination key — no placement surface exists yet in P1a; the lot stays in the person's custody outside any hand.");
        }
    }
}
