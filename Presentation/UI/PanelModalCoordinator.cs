using System;
using System.Collections.Generic;

namespace LandLedgers.UI
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-007): the source-side panel-control
    /// contract for modal ownership. Audit finding: the seller-meeting
    /// surface (AcquisitionSellerMeetingController + View) already owns
    /// its own close behavior (Back button "SellerMeeting_Close",
    /// Escape closes, Enter advances), but NOTHING enforces that it is
    /// the single active modal over the management panel — a second
    /// modal can open alongside it and the two surfaces compete for
    /// focus, which is exactly the October 9 layering complaint. This
    /// pure coordinator is the contract Codex must enforce at scene
    /// level (see Docs/Pass12_PlayerExperience_Contracts.md):
    ///
    /// - Exactly one modal owner at a time. A second owner requesting
    ///   while another is active is REFUSED (returns false); the caller
    ///   must close the active modal first, never stack a second one
    ///   on top and hope sorting saves it.
    /// - Re-requesting by the active owner is idempotent (true).
    /// - Only the active owner may close; a close from anyone else is
    ///   refused and focus does not move.
    /// - Closing returns the focus-return target: the owner that had
    ///   focus before this modal opened (usually the management panel),
    ///   so focus returns instead of landing nowhere.
    /// - Double-close is safe (false, no state change).
    ///
    /// This class is deliberately NOT a MonoBehaviour: the rule must be
    /// testable in the Roslyn harness. Scene binding (which GameObject
    /// owns the single coordinator instance, Canvas sortingOrder, and
    /// EventSystem focus calls) is Codex work — the coordinator only
    /// decides; it does not touch the Canvas.
    /// </summary>
    public sealed class PanelModalCoordinator
    {
        private readonly List<string> focusStack = new List<string>();

        /// <summary>Current modal owner id, or empty when no modal is active.</summary>
        public string ActiveOwner { get; private set; } = string.Empty;

        public bool IsModalActive => !string.IsNullOrEmpty(ActiveOwner);

        /// <summary>
        /// Opens a modal for <paramref name="ownerId"/>, remembering
        /// <paramref name="previousFocusOwnerId"/> for focus return.
        /// Returns false (and changes nothing) when a different owner is
        /// already active, or when the owner id is empty.
        /// </summary>
        public bool RequestModal(string ownerId, string previousFocusOwnerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return false;
            if (IsModalActive)
            {
                return string.Equals(ActiveOwner, ownerId, StringComparison.Ordinal);
            }
            ActiveOwner = ownerId;
            focusStack.Add(previousFocusOwnerId ?? string.Empty);
            return true;
        }

        /// <summary>
        /// Closes the modal. Only the active owner may close. Returns the
        /// focus-return target (empty when none was recorded), or null
        /// when the close was refused.
        /// </summary>
        public string CloseModal(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return null;
            if (!IsModalActive) return null;
            if (!string.Equals(ActiveOwner, ownerId, StringComparison.Ordinal)) return null;
            ActiveOwner = string.Empty;
            if (focusStack.Count == 0) return string.Empty;
            string target = focusStack[focusStack.Count - 1];
            focusStack.RemoveAt(focusStack.Count - 1);
            return target ?? string.Empty;
        }

        /// <summary>Clears all modal state (scene teardown / new game).</summary>
        public void Reset()
        {
            ActiveOwner = string.Empty;
            focusStack.Clear();
        }
    }
}
