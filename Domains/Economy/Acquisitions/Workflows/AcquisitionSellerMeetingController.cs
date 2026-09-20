using System;
using LandLedgers.Economy;
using LandLedgers.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LandLedgers.MVP
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(326)]
    public sealed class AcquisitionSellerMeetingController : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private AcquisitionMarketManager acquisitionMarket;
        [SerializeField] private AcquisitionSellerMeetingView view;

        private AcquisitionConversationState currentState;
        private string currentListingId = string.Empty;
        private bool listenersBound;
        private int selectedResponseIndex = -1;

        public event Action<string> ConversationStateChanged;
        public string CurrentListingId => currentListingId ?? string.Empty;

        private void Awake()
        {
            AutoWire();
            EnsureView();
            BindListeners();
        }

        private void OnDestroy()
        {
            UnbindListeners();
            ClearResponseButtonListeners();
        }

        private void Update()
        {
            if (view == null || !view.IsVisible)
            {
                return;
            }

            if (MvpKeyboardInput.WasPressedThisFrame(Key.Escape))
            {
                Close();
                return;
            }

            if (MvpKeyboardInput.WasAnyPressedThisFrame(Key.Enter, Key.NumpadEnter))
            {
                AdvanceDeal();
                return;
            }

            int shortcutIndex = ResolveShortcutIndex();
            if (shortcutIndex >= 0)
            {
                ActivateResponse(shortcutIndex);
            }
        }

        public void Configure(Canvas newCanvas, AcquisitionMarketManager newAcquisitionMarket)
        {
            canvas = newCanvas != null ? newCanvas : canvas;
            acquisitionMarket = newAcquisitionMarket != null ? newAcquisitionMarket : acquisitionMarket;
            EnsureView();
            BindListeners();
        }

        public void OpenForSelected(AcquisitionMarketSection section)
        {
            AutoWire();
            EnsureView();
            if (acquisitionMarket == null || !acquisitionMarket.TryBuildSelectedConversationState(section, out AcquisitionConversationState state))
            {
                if (!TryRenderUnavailableFromMarket(string.Empty, string.Empty, false))
                {
                    ClearConversationState(showView: true);
                }
                return;
            }

            OpenState(state);
        }

        public void OpenForListing(string listingId)
        {
            AutoWire();
            EnsureView();
            if (acquisitionMarket == null || !acquisitionMarket.TryBuildConversationState(listingId, out AcquisitionConversationState state))
            {
                if (!TryRenderUnavailableFromMarket(listingId, string.Empty, false))
                {
                    ClearConversationState(showView: true);
                }
                return;
            }

            OpenState(state);
        }

        public void Close()
        {
            view?.SetVisible(false);
            LLFeedbackService.Play(LLFeedbackKind.UIBack, "seller meeting close", canvas);
            selectedResponseIndex = -1;
            NotifyConversationStateChanged();
        }

        private void ClearConversationState(bool showView)
        {
            currentListingId = string.Empty;
            currentState = null;
            selectedResponseIndex = -1;
            view?.Render(null);
            ClearResponseButtonListeners();
            view?.SetVisible(showView);
            NotifyConversationStateChanged();
        }

        private void OpenState(AcquisitionConversationState state)
        {
            currentState = state;
            currentListingId = state != null ? state.ListingId : string.Empty;
            selectedResponseIndex = 0;
            RefreshView();
            view?.SetVisible(true);
            NotifyConversationStateChanged();
        }

        private void AdvanceDeal()
        {
            if (acquisitionMarket == null || string.IsNullOrWhiteSpace(currentListingId))
            {
                return;
            }

            string listingId = currentListingId;
            AcquisitionDealStage stageBeforeAdvance = currentState != null ? currentState.Stage : AcquisitionDealStage.None;
            bool succeeded = acquisitionMarket.TryAdvanceConversationDeal(listingId, out string message);
            LLFeedbackService.Play(
                succeeded ? ResolveDealAdvanceFeedbackKind(stageBeforeAdvance) : LLFeedbackKind.UIBack,
                succeeded ? ResolveDealAdvanceContext(stageBeforeAdvance) : "seller meeting advance blocked",
                canvas);

            if (acquisitionMarket.TryBuildConversationState(listingId, out AcquisitionConversationState state))
            {
                currentState = state;
                currentListingId = state != null ? state.ListingId : listingId;
                RefreshView();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    view?.SetResponseReadout(BuildAdvanceResultReadout(message, succeeded));
                }
            }
            else
            {
                currentState = null;
                currentListingId = succeeded ? string.Empty : listingId;
                if (!TryRenderUnavailableFromMarket(listingId, message, succeeded))
                {
                    RenderAdvanceUnavailable(message, succeeded, stageBeforeAdvance);
                }
            }

            NotifyConversationStateChanged();
        }

        private void RefreshView()
        {
            if (view == null)
            {
                return;
            }

            view.Render(currentState);
            BindResponseButtons();
            if (view.ResponseButtons.Count > 0)
            {
                selectedResponseIndex = Mathf.Clamp(selectedResponseIndex, 0, view.ResponseButtons.Count - 1);
                view.SetSelectedResponseIndex(selectedResponseIndex);
            }
            else
            {
                selectedResponseIndex = -1;
            }
        }

        private void BindListeners()
        {
            if (listenersBound || view == null)
            {
                return;
            }

            view.PrimaryActionButton?.onClick.AddListener(AdvanceDeal);
            view.CloseButton?.onClick.AddListener(Close);
            listenersBound = true;
        }

        private void UnbindListeners()
        {
            if (!listenersBound || view == null)
            {
                listenersBound = false;
                return;
            }

            view.PrimaryActionButton?.onClick.RemoveListener(AdvanceDeal);
            view.CloseButton?.onClick.RemoveListener(Close);
            listenersBound = false;
        }

        private void BindResponseButtons()
        {
            ClearResponseButtonListeners();
            if (view == null || currentState == null || currentState.Options == null)
            {
                return;
            }

            int count = GetVisibleResponseCount();
            for (int i = 0; i < count; i++)
            {
                Button button = view.ResponseButtons[i];
                AcquisitionConversationOption option = currentState.Options[i];
                if (button == null || option == null)
                {
                    continue;
                }

                int optionIndex = i;
                button.onClick.AddListener(() => ActivateResponse(optionIndex));
            }
        }

        private void ClearResponseButtonListeners()
        {
            if (view == null || view.ResponseButtons == null)
            {
                return;
            }

            // Seller meeting response buttons are recycled across file states; clear all of them so hidden rows cannot keep stale file actions.
            for (int i = 0; i < view.ResponseButtons.Count; i++)
            {
                view.ResponseButtons[i]?.onClick.RemoveAllListeners();
            }
        }

        private void ActivateResponse(int optionIndex)
        {
            if (view == null || currentState == null || currentState.Options == null)
            {
                return;
            }

            int count = GetVisibleResponseCount();
            if (optionIndex < 0 || optionIndex >= count)
            {
                return;
            }

            AcquisitionConversationOption option = currentState.Options[optionIndex];
            if (option == null)
            {
                return;
            }

            string listingIdBeforeResponse = currentListingId;
            selectedResponseIndex = optionIndex;
            bool applied = TryApplyResponseOption(option, out string fileNote);
            string responseReadout = BuildResponseReadout(option, fileNote, applied);
            bool renderedUnavailable = false;

            if (applied && acquisitionMarket != null && !string.IsNullOrWhiteSpace(listingIdBeforeResponse))
            {
                if (acquisitionMarket.TryBuildConversationState(listingIdBeforeResponse, out AcquisitionConversationState refreshedState))
                {
                    currentState = refreshedState;
                    currentListingId = refreshedState != null ? refreshedState.ListingId : listingIdBeforeResponse;
                    RefreshView();
                    selectedResponseIndex = view.ResponseButtons.Count > 0
                        ? Mathf.Clamp(optionIndex, 0, view.ResponseButtons.Count - 1)
                        : -1;
                }
                else
                {
                    renderedUnavailable = TryRenderUnavailableFromMarket(listingIdBeforeResponse, responseReadout, true, responseResultAlreadyFormatted: true);
                    if (!renderedUnavailable)
                    {
                        currentState = null;
                        currentListingId = string.Empty;
                        selectedResponseIndex = -1;
                    }
                }
            }

            if (!renderedUnavailable)
            {
                view.SetSelectedResponseIndex(selectedResponseIndex);
                view.SetResponseReadout(responseReadout);
            }

            LLFeedbackService.Play(applied ? LLFeedbackKind.UISelect : LLFeedbackKind.UIBack, applied ? "seller meeting response select" : "seller meeting response blocked", canvas);
            NotifyConversationStateChanged();
        }

        private int GetVisibleResponseCount()
        {
            return view == null || currentState == null || currentState.Options == null
                ? 0
                : Mathf.Min(view.ResponseButtons.Count, currentState.Options.Count);
        }

        private bool TryApplyResponseOption(AcquisitionConversationOption option, out string fileNote)
        {
            fileNote = string.Empty;
            if (option == null)
            {
                return false;
            }

            if (acquisitionMarket == null || string.IsNullOrWhiteSpace(currentListingId))
            {
                fileNote = "No acquisition file is available for a recorded meeting note.";
                return false;
            }

            return acquisitionMarket.TryApplyConversationOption(currentListingId, option.Kind, out fileNote);
        }

        private static string BuildResponseReadout(AcquisitionConversationOption option, string fileNote, bool fileNoteRecorded)
        {
            if (option == null)
            {
                return string.Empty;
            }

            string buyer = string.IsNullOrWhiteSpace(option.BuyerLine) ? string.Empty : $"Buyer line: {option.BuyerLine}";
            string reply = string.IsNullOrWhiteSpace(option.SellerReplyText) ? string.Empty : $"Seller reply: {option.SellerReplyText}";
            string outcome = string.IsNullOrWhiteSpace(option.OutcomeTag) ? string.Empty : $"Outcome: {option.OutcomeTag}";
            string detail = string.IsNullOrWhiteSpace(option.ResponseText) ? string.Empty : option.ResponseText;
            string meetingNote = string.IsNullOrWhiteSpace(fileNote)
                ? string.Empty
                : $"{(fileNoteRecorded ? "File note" : "File note blocked")}: {fileNote.Trim()}";

            return $"{buyer}\n{reply}\n{outcome}\n{detail}\n{meetingNote}".Trim();
        }

        private static int ResolveShortcutIndex()
        {
            if (MvpKeyboardInput.WasAnyPressedThisFrame(Key.Digit1, Key.Numpad1))
            {
                return 0;
            }

            if (MvpKeyboardInput.WasAnyPressedThisFrame(Key.Digit2, Key.Numpad2))
            {
                return 1;
            }

            if (MvpKeyboardInput.WasAnyPressedThisFrame(Key.Digit3, Key.Numpad3))
            {
                return 2;
            }

            if (MvpKeyboardInput.WasAnyPressedThisFrame(Key.Digit4, Key.Numpad4))
            {
                return 3;
            }

            if (MvpKeyboardInput.WasAnyPressedThisFrame(Key.Digit5, Key.Numpad5))
            {
                return 4;
            }

            if (MvpKeyboardInput.WasAnyPressedThisFrame(Key.Digit6, Key.Numpad6))
            {
                return 5;
            }

            return -1;
        }

        private static string BuildAdvanceResultReadout(string message, bool succeeded)
        {
            string prefix = succeeded ? "Formal action recorded:" : "Formal action blocked:";
            return string.IsNullOrWhiteSpace(message)
                ? prefix
                : $"{prefix}\n{message.Trim()}";
        }

        private bool TryRenderUnavailableFromMarket(string listingId, string resultMessage, bool succeeded, bool responseResultAlreadyFormatted = false)
        {
            if (view == null
                || acquisitionMarket == null
                || !acquisitionMarket.TryBuildUnavailableConversationReadout(listingId, out AcquisitionSellerMeetingUnavailableReadout readout)
                || readout == null)
            {
                return false;
            }

            currentState = null;
            currentListingId = string.Empty;
            selectedResponseIndex = -1;
            string responseReadout = readout.ResponseReadout;
            if (!string.IsNullOrWhiteSpace(resultMessage))
            {
                string result = responseResultAlreadyFormatted
                    ? resultMessage.Trim()
                    : BuildAdvanceResultReadout(resultMessage, succeeded);
                responseReadout = string.IsNullOrWhiteSpace(responseReadout)
                    ? result
                    : $"{result}\n\n{responseReadout}";
            }

            view.RenderUnavailable(
                readout.Title,
                readout.Stage,
                readout.SellerIdentity,
                readout.SelectedFile,
                readout.ProcessAction,
                readout.SellerLine,
                readout.DialoguePrompt,
                readout.Posture,
                readout.Readiness,
                responseReadout,
                readout.PrimaryActionLabel);
            ClearResponseButtonListeners();
            view.SetVisible(true);
            return true;
        }

        private void RenderAdvanceUnavailable(string message, bool succeeded, AcquisitionDealStage stageBeforeAdvance)
        {
            if (view == null)
            {
                return;
            }

            string result = BuildAdvanceResultReadout(message, succeeded);
            string title = succeeded && stageBeforeAdvance == AcquisitionDealStage.TentativeAgreement
                ? "Acquisition Transfer Recorded"
                : succeeded
                    ? "Seller Meeting Advanced"
                    : "Seller Meeting Paused";
            string stage = succeeded && stageBeforeAdvance == AcquisitionDealStage.TentativeAgreement
                ? "Stage: Closed\nNext move: return to the acquisition workflow for transfer and integration follow-through."
                : succeeded
                    ? "Stage: Updated\nNext move: return to the workflow and reopen the live file if the lead is still available."
                    : "Stage: Action blocked\nNext move: correct the file issue before trying to advance again.";
            string selectedFile = string.IsNullOrWhiteSpace(currentListingId)
                ? "Selected file: no live listing remains attached to this meeting."
                : $"Selected file: {currentListingId}";

            view.RenderUnavailable(
                title,
                stage,
                "Counterparty: file no longer available in the current market list | Seller posture: unavailable | Buyer footing: see result below",
                selectedFile,
                result,
                result,
                "The formal meeting cannot continue from this overlay state.",
                "Return to the acquisition workflow to review market status, integration, or the next live lead.",
                "No further meeting advance is available from this state.",
                result,
                "No Action");
            ClearResponseButtonListeners();
            selectedResponseIndex = -1;
        }

        private static LLFeedbackKind ResolveDealAdvanceFeedbackKind(AcquisitionDealStage stageBeforeAdvance)
        {
            return stageBeforeAdvance == AcquisitionDealStage.TentativeAgreement
                ? LLFeedbackKind.HeavyCash
                : LLFeedbackKind.StampApproval;
        }

        private static string ResolveDealAdvanceContext(AcquisitionDealStage stageBeforeAdvance)
        {
            return stageBeforeAdvance == AcquisitionDealStage.TentativeAgreement
                ? "property acquisition close"
                : "seller meeting deal advance";
        }

        private void NotifyConversationStateChanged()
        {
            ConversationStateChanged?.Invoke(currentListingId);
        }

        private void EnsureView()
        {
            if (view != null)
            {
                return;
            }

            canvas = canvas != null ? canvas : FindAnyObjectByType<Canvas>();
            view = AcquisitionSellerMeetingView.GetOrCreate(canvas);
        }

        private void AutoWire()
        {
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            canvas ??= FindAnyObjectByType<Canvas>();
        }
    }

    internal static class MvpKeyboardInput
    {
        public static bool WasPressedThisFrame(Key key)
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }

        public static bool WasAnyPressedThisFrame(params Key[] keys)
        {
            if (keys == null || keys.Length <= 0)
            {
                return false;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                if (keyboard[keys[i]].wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
