using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// D-9: the player selection API — the source contract for the future
    /// Codex UI. The player inspects a property's contents, chooses All /
    /// No Movable / Custom, toggles items, sees the contents value, and
    /// validates the selection — and every step of choosing runs through
    /// B7's presence contract: the player must be present at the property
    /// (or act through an authorized agent) before any selection work
    /// happens, and the confirmed selection is applied only through the
    /// contract's Execute, once Ready. Nothing here executes remotely.
    ///
    /// Session shape (one API instance per deal-in-progress):
    /// BeginSelection requests a SelectContents directed action through
    /// the contract — Ready when the player is already at the property
    /// (or an authorized agent acts), Travelling while real travel runs,
    /// Deferred while the player is off-map, Refused while the player is
    /// busy with non-routine work or the trip cannot be routed. The
    /// caller drives AdvancePending per tick until the session is active;
    /// list / mode / toggle / compute / validate all refuse while the
    /// session is not active. ConfirmSelection executes the pending
    /// action through the contract with the caller's executor, which is
    /// where the selection lands on the deal (the executor reads
    /// DraftSelection and sets deal.Contents — the API never touches the
    /// harness-excluded manager types itself).
    ///
    /// Valuation: the draft is built through B6's builders with the
    /// caller's replacement-cost function (Agent F's prices in
    /// production — D invents no price). The draft re-values from the
    /// live register on every rebuild; the agreed values freeze at
    /// confirmation, and D-6's Revalidate catches any drift between
    /// confirmation and close. An unpriced included item (no replacement
    /// cost, e.g. a stove under F6 pricing) is carried on the draft and
    /// refused by ValidateSelection with requirePriced — the player may
    /// still be choosing, but the selection may not be agreed until it
    /// is priced or the item is excluded.
    ///
    /// Agreed price (D-8 call convention): the agreed price is the
    /// caller's input — the price agreed in the deal, however it was
    /// reached. TryImpliedStructureValueCents derives the implied
    /// structure value from that agreed price (agreed minus contents),
    /// never from the F6 anchor; validate and close must use that SAME
    /// implied structure value (D-8 §16). CheckConservation pins
    /// price == structure + contents against it.
    ///
    /// What this API does NOT do: it does not resolve the building's
    /// location (the caller supplies targetLocationId — the parcel-side
    /// building binding is driver/G work, Building carries no int
    /// instance id); it does not mint TaskAuthority tasks (B7's
    /// documented boundary — the contract issues real travel through
    /// the driver, not tasks); it does not persist the session (no save
    /// seam, B7 precedent — a mid-travel Continue drops the pending
    /// directed action and the selection must be re-issued); it does
    /// not settle stove fuel (G's wiring, D-7); it does not check
    /// conservation in the player close (the manager's close leg
    /// consumes the implied structure value — see the manager seam).
    /// </summary>
    public sealed class PlayerContentsSelectionApi
    {
        private readonly PlayerFurnishingPresenceContract presenceContract;
        private readonly HouseholdFurnishingRegister sellerRegister;
        private readonly int buildingInstanceId;
        private readonly string sellerOwnerKind;
        private readonly string sellerOwnerId;
        private readonly Func<string, int> replacementCostCentsForKind;
        private readonly int dayIndex;
        private readonly string householdId;
        private readonly string targetLocationId;

        /// <summary>The draft selection under construction, or null before SetMode.</summary>
        public PropertyContentsSelection DraftSelection { get; private set; }

        /// <summary>The selection confirmed through the presence contract, or null before ConfirmSelection.</summary>
        public PropertyContentsSelection ConfirmedSelection { get; private set; }

        /// <summary>The directed SelectContents action this session requested, or null when no session is open.</summary>
        public DirectedFurnishingAction PendingAction { get; private set; }

        /// <summary>True once the presence contract has the session Ready — the player is at the property (or an authorized agent acts).</summary>
        public bool IsActive { get; private set; }

        /// <summary>True once a selection has been confirmed through the contract's Execute.</summary>
        public bool IsConfirmed => ConfirmedSelection != null;

        /// <summary>Contents value of the draft, 0 when no draft exists.</summary>
        public int TotalContentsValueCents => DraftSelection != null ? DraftSelection.TotalContentsValueCents : 0;

        /// <summary>Included-fixture value of the draft, 0 when no draft exists.</summary>
        public int IncludedFixtureValueCents => DraftSelection != null ? DraftSelection.IncludedFixtureValueCents : 0;

        /// <summary>Included-movable value of the draft, 0 when no draft exists.</summary>
        public int IncludedMovableValueCents => DraftSelection != null ? DraftSelection.IncludedMovableValueCents : 0;

        /// <summary>
        /// Builds the API. The presence contract, the seller's register
        /// and the replacement-cost function are required — a selection
        /// API with no presence gate, no seller authority, or no price
        /// data cannot value or gate anything, so it is not allowed to
        /// exist. The building instance id, seller authority, household
        /// and target location are fixed for the session and must be
        /// named — the API never invents them.
        /// </summary>
        public PlayerContentsSelectionApi(
            PlayerFurnishingPresenceContract presenceContract,
            HouseholdFurnishingRegister sellerRegister,
            int buildingInstanceId,
            string sellerOwnerKind,
            string sellerOwnerId,
            Func<string, int> replacementCostCentsForKind,
            int dayIndex,
            string householdId,
            string targetLocationId)
        {
            if (presenceContract == null)
                throw new ArgumentNullException(nameof(presenceContract));
            if (sellerRegister == null)
                throw new ArgumentNullException(nameof(sellerRegister));
            if (replacementCostCentsForKind == null)
                throw new ArgumentNullException(nameof(replacementCostCentsForKind));
            if (buildingInstanceId < 0)
                throw new ArgumentException("PlayerContentsSelectionApi: buildingInstanceId " + buildingInstanceId + " is not a building — the selection must name the building it covers.", nameof(buildingInstanceId));
            if (string.IsNullOrWhiteSpace(sellerOwnerKind) || string.IsNullOrWhiteSpace(sellerOwnerId))
                throw new ArgumentException("PlayerContentsSelectionApi: the seller must be named (owner kind and id) — the seller's authority is what the selection is validated against.", nameof(sellerOwnerId));
            if (string.IsNullOrWhiteSpace(householdId))
                throw new ArgumentException("PlayerContentsSelectionApi: the player's household must be named — the presence action belongs to it.", nameof(householdId));
            if (string.IsNullOrWhiteSpace(targetLocationId))
                throw new ArgumentException("PlayerContentsSelectionApi: the property's location must be named — the player travels there to select its contents.", nameof(targetLocationId));

            this.presenceContract = presenceContract;
            this.sellerRegister = sellerRegister;
            this.buildingInstanceId = buildingInstanceId;
            this.sellerOwnerKind = sellerOwnerKind;
            this.sellerOwnerId = sellerOwnerId;
            this.replacementCostCentsForKind = replacementCostCentsForKind;
            this.dayIndex = dayIndex;
            this.householdId = householdId;
            this.targetLocationId = targetLocationId;
        }

        /// <summary>
        /// Opens the selection session through B7's presence contract: a
        /// SelectContents directed action at the property. Ready means the
        /// session is active at once; Travelling and Deferred keep the
        /// action pending (drive AdvancePending / RetryDeferred); Refused
        /// opens nothing. A second Begin while a session is open refuses
        /// — cancel or confirm the open session first.
        /// </summary>
        public DirectedActionVerdict BeginSelection(int agentPersonId,
            out DirectedFurnishingAction action, out string reason)
        {
            action = null;
            reason = null;
            if (IsSessionOpen())
            {
                reason = "PlayerContentsSelectionApi.BeginSelection: a selection session is already open for household "
                    + householdId + " (action " + PendingAction.ActionId + ", status " + PendingAction.Status
                    + ") — cancel or confirm it before opening another.";
                return DirectedActionVerdict.Refused;
            }

            DirectedActionVerdict verdict = presenceContract.RequestDirectedAction(
                FurnishingActionKind.SelectContents, householdId, string.Empty,
                targetLocationId, agentPersonId, out action, out reason);
            if (verdict == DirectedActionVerdict.Ready
                || verdict == DirectedActionVerdict.Travelling
                || verdict == DirectedActionVerdict.Deferred)
            {
                PendingAction = action;
            }
            if (verdict == DirectedActionVerdict.Ready)
            {
                IsActive = true;
            }
            return verdict;
        }

        /// <summary>
        /// Advances a Travelling session one tick. True when the session
        /// is now active (the player arrived); false when there is no
        /// pending action, the action is not Travelling, or the player
        /// has not arrived yet. A player who left the map mid-trip drops
        /// the action to Deferred (retry it with RetryDeferred).
        /// </summary>
        public bool AdvancePending()
        {
            if (PendingAction == null) return false;
            bool ready = presenceContract.Advance(PendingAction);
            if (ready) IsActive = true;
            return ready;
        }

        /// <summary>
        /// Re-runs the presence gate for a Deferred session after the
        /// player returns to the map. Ready activates the session; a
        /// refused retry leaves the record Deferred.
        /// </summary>
        public DirectedActionVerdict RetryDeferred(out string reason)
        {
            reason = null;
            if (PendingAction == null)
            {
                reason = "PlayerContentsSelectionApi.RetryDeferred: no selection session is open — begin one first.";
                return DirectedActionVerdict.Refused;
            }
            DirectedActionVerdict verdict = presenceContract.RetryDeferred(PendingAction, out reason);
            if (verdict == DirectedActionVerdict.Ready) IsActive = true;
            return verdict;
        }

        /// <summary>
        /// Cancels the open session. The draft is abandoned with it — a
        /// new Begin starts fresh. Returns false when there is no open
        /// session or the action is already terminal.
        /// </summary>
        public bool CancelPending(out string reason)
        {
            reason = null;
            if (PendingAction == null)
            {
                reason = "PlayerContentsSelectionApi.CancelPending: no selection session is open — nothing to cancel.";
                return false;
            }
            bool cancelled = presenceContract.CancelPending(PendingAction, out reason);
            if (cancelled)
            {
                PendingAction = null;
                IsActive = false;
                DraftSelection = null;
            }
            return cancelled;
        }

        /// <summary>
        /// Lists the eligible contents of the building — installed items
        /// only, read from the seller's register through B6. Requires an
        /// active session: the player must be present at the property
        /// before its contents are listed for choosing.
        /// </summary>
        public List<FurnishingContentsItem> ListEligible(out string reason)
        {
            reason = null;
            if (!IsActive)
            {
                reason = "PlayerContentsSelectionApi.ListEligible: the selection session is not active — the player must be present at the property (BeginSelection) before its contents are listed.";
                return null;
            }
            return PropertyContentsSelection.ListEligibleContents(sellerRegister, buildingInstanceId);
        }

        /// <summary>
        /// Sets the selection mode and rebuilds the draft through B6.
        /// Switching to Custom preserves the current draft's included
        /// set (so All-then-Custom lets the player remove a few items);
        /// Custom with no draft starts empty. Refuses an unspecified
        /// mode, a building with no eligible contents (a bare sale has
        /// no selection to make), and a building carrying an
        /// unclassifiable item (nobody can agree what it is worth or
        /// whether it stays — D-8 precedent).
        /// </summary>
        public bool SetMode(ContentsSelectionMode mode, out string reason)
        {
            reason = null;
            if (!IsActive)
            {
                reason = "PlayerContentsSelectionApi.SetMode: the selection session is not active — the player must be present at the property before choosing its contents.";
                return false;
            }
            if (mode == ContentsSelectionMode.Unspecified)
            {
                reason = "PlayerContentsSelectionApi.SetMode: mode is unspecified — choose All, No Movable, or Custom.";
                return false;
            }

            List<FurnishingContentsItem> eligible =
                PropertyContentsSelection.ListEligibleContents(sellerRegister, buildingInstanceId);
            if (eligible.Count == 0)
            {
                reason = "PlayerContentsSelectionApi.SetMode: the building holds no eligible contents — this is a bare sale, not a contents selection.";
                return false;
            }
            foreach (FurnishingContentsItem item in eligible)
            {
                if (item.Kind == FurnishingFixtureKind.Unspecified)
                {
                    reason = "PlayerContentsSelectionApi.SetMode: asset " + item.AssetId + " (kind " + item.AssetKind
                        + ") in building " + buildingInstanceId
                        + " could not be classified fixture or movable — the contents cannot be selected until it is classified.";
                    return false;
                }
            }

            switch (mode)
            {
                case ContentsSelectionMode.All:
                    DraftSelection = PropertyContentsSelection.BuildAll(
                        eligible, sellerOwnerKind, sellerOwnerId, buildingInstanceId, dayIndex,
                        replacementCostCentsForKind);
                    break;
                case ContentsSelectionMode.NoMovable:
                    DraftSelection = PropertyContentsSelection.BuildNoMovable(
                        eligible, sellerOwnerKind, sellerOwnerId, buildingInstanceId, dayIndex,
                        replacementCostCentsForKind);
                    break;
                default:
                    DraftSelection = PropertyContentsSelection.BuildCustom(
                        eligible, IncludedIdsOf(DraftSelection), sellerOwnerKind, sellerOwnerId,
                        buildingInstanceId, dayIndex, replacementCostCentsForKind);
                    break;
            }
            return true;
        }

        /// <summary>
        /// Toggles one item in Custom mode and rebuilds the draft
        /// through B6. Only Custom allows per-item choice — All and
        /// No Movable are whole-building modes. Refuses an unknown id
        /// (not an eligible item for this building), an unclassifiable
        /// item, and any toggle before a draft exists (call SetMode
        /// first). Toggling an item already in the requested state is a
        /// no-op success.
        /// </summary>
        public bool ToggleItem(string assetId, bool included, out string reason)
        {
            reason = null;
            if (!IsActive)
            {
                reason = "PlayerContentsSelectionApi.ToggleItem: the selection session is not active — the player must be present at the property before choosing its contents.";
                return false;
            }
            if (DraftSelection == null)
            {
                reason = "PlayerContentsSelectionApi.ToggleItem: no draft selection exists — call SetMode first.";
                return false;
            }
            if (DraftSelection.Mode != ContentsSelectionMode.Custom)
            {
                reason = "PlayerContentsSelectionApi.ToggleItem: the draft is mode " + DraftSelection.Mode
                    + " — switch to Custom to choose items individually.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(assetId))
            {
                reason = "PlayerContentsSelectionApi.ToggleItem: no asset id was given.";
                return false;
            }

            List<FurnishingContentsItem> eligible =
                PropertyContentsSelection.ListEligibleContents(sellerRegister, buildingInstanceId);
            FurnishingContentsItem target = null;
            foreach (FurnishingContentsItem item in eligible)
            {
                if (item != null && item.AssetId == assetId) { target = item; break; }
            }
            if (target == null)
            {
                reason = "PlayerContentsSelectionApi.ToggleItem: asset " + assetId
                    + " is not an eligible item for building " + buildingInstanceId + " — only installed items can be chosen.";
                return false;
            }
            if (target.Kind == FurnishingFixtureKind.Unspecified)
            {
                reason = "PlayerContentsSelectionApi.ToggleItem: asset " + assetId + " (kind " + target.AssetKind
                    + ") could not be classified fixture or movable — it cannot be chosen until it is classified.";
                return false;
            }

            var wanted = new HashSet<string>(IncludedIdsOf(DraftSelection), StringComparer.Ordinal);
            if (included) wanted.Add(assetId);
            else wanted.Remove(assetId);
            DraftSelection = PropertyContentsSelection.BuildCustom(
                eligible, wanted, sellerOwnerKind, sellerOwnerId,
                buildingInstanceId, dayIndex, replacementCostCentsForKind);
            return true;
        }

        /// <summary>
        /// Validates the draft against the seller's register with
        /// requirePriced — the agreement-time gate (D-6). True when the
        /// selection may be agreed. An unpriced included item fails here:
        /// the player may still be choosing, but the selection may not
        /// be agreed until it is priced or the item is excluded.
        /// </summary>
        public bool ValidateSelection(out List<string> problems)
        {
            problems = new List<string>();
            if (!IsActive)
            {
                problems.Add("PlayerContentsSelectionApi.ValidateSelection: the selection session is not active — the player must be present at the property before the selection is validated.");
                return false;
            }
            if (DraftSelection == null)
            {
                problems.Add("PlayerContentsSelectionApi.ValidateSelection: no draft selection exists — call SetMode first.");
                return false;
            }
            problems = DraftSelection.Validate(sellerRegister, requirePriced: true);
            return problems.Count == 0;
        }

        /// <summary>
        /// The D-8 call convention for the player flow: the implied
        /// structure value is the agreed price minus the agreed contents
        /// value — derived from the AGREED price, never from the F6
        /// anchor. Validate and close must use this SAME implied value.
        /// Refuses when there is no draft, the price is negative, or
        /// the price does not cover the contents value (the implied
        /// structure would be negative).
        /// </summary>
        public bool TryImpliedStructureValueCents(int agreedPriceCents,
            out int impliedStructureCents, out string reason)
        {
            impliedStructureCents = 0;
            reason = null;
            if (DraftSelection == null)
            {
                reason = "PlayerContentsSelectionApi: no draft selection exists — the implied structure value needs an agreed contents value.";
                return false;
            }
            if (agreedPriceCents < 0)
            {
                reason = "PlayerContentsSelectionApi: the agreed price is " + agreedPriceCents + "c — a negative agreed price cannot anchor a sale.";
                return false;
            }
            if (agreedPriceCents < DraftSelection.TotalContentsValueCents)
            {
                reason = "PlayerContentsSelectionApi: the agreed price " + agreedPriceCents + "c does not cover the contents value "
                    + DraftSelection.TotalContentsValueCents + "c — the implied structure value would be negative.";
                return false;
            }
            impliedStructureCents = agreedPriceCents - DraftSelection.TotalContentsValueCents;
            return true;
        }

        /// <summary>
        /// Conservation (D-6, B6): price == structure + contents, cent
        /// for cent. Null when balanced, else the loud string naming all
        /// three numbers. The caller MUST pass the same implied structure
        /// value TryImpliedStructureValueCents produced — the F6 anchor
        /// is not a substitute (D-8 §16).
        /// </summary>
        public string CheckConservation(int agreedPriceCents, int structureValueCents)
        {
            if (DraftSelection == null)
                return "PlayerContentsSelectionApi.CheckConservation: no draft selection exists — there is no contents value to conserve.";
            return PropertyContentsSelection.CheckConservation(
                structureValueCents, DraftSelection.TotalContentsValueCents, agreedPriceCents);
        }

        /// <summary>
        /// Confirms the draft through the presence contract: the pending
        /// action must be Ready, and the caller's executor applies the
        /// selection (it reads DraftSelection and sets it on the deal —
        /// the API never touches the manager's deal types). On success
        /// the selection is confirmed and the session is complete; on
        /// executor refusal the action fails loudly and nothing is
        /// confirmed; a not-Ready action refuses without calling the
        /// executor. A null executor refuses without executing.
        /// </summary>
        public bool ConfirmSelection(FurnishingActionExecutor applyExecutor, out string reason)
        {
            reason = null;
            if (!IsActive || PendingAction == null)
            {
                reason = "PlayerContentsSelectionApi.ConfirmSelection: the selection session is not active — the player must be present at the property before the selection is confirmed.";
                return false;
            }
            if (DraftSelection == null)
            {
                reason = "PlayerContentsSelectionApi.ConfirmSelection: no draft selection exists — call SetMode first.";
                return false;
            }
            if (applyExecutor == null)
            {
                reason = "PlayerContentsSelectionApi.ConfirmSelection: no executor was supplied — the confirmed selection has nowhere to land.";
                return false;
            }

            string refusal = presenceContract.Execute(PendingAction, applyExecutor);
            if (refusal != null)
            {
                reason = refusal;
                return false;
            }
            ConfirmedSelection = DraftSelection;
            IsActive = false;
            return true;
        }

        private bool IsSessionOpen()
        {
            if (PendingAction == null) return false;
            switch (PendingAction.Status)
            {
                case DirectedActionStatus.Requested:
                case DirectedActionStatus.Travelling:
                case DirectedActionStatus.Ready:
                case DirectedActionStatus.Deferred:
                    return true;
                default:
                    return false;
            }
        }

        private static HashSet<string> IncludedIdsOf(PropertyContentsSelection selection)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (selection == null || selection.Lines == null) return ids;
            foreach (FurnishingContentsLine line in selection.Lines)
            {
                if (line != null && line.Included && !string.IsNullOrWhiteSpace(line.AssetId))
                    ids.Add(line.AssetId);
            }
            return ids;
        }
    }
}
