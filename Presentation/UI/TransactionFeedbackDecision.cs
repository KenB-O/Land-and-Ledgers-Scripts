namespace LandLedgers.UI
{
    /// <summary>
    /// Pass XI WS5 (player-facing): the Unity-free decision core for
    /// transaction feedback. The rule, in one place: a success sound or
    /// stamp plays ONLY when the authoritative operation reported
    /// success. Before this pass several panel handlers discarded the
    /// operation's bool result and played the success kind anyway — a
    /// rejected loan played the approval stamp, a blocked cash transfer
    /// played the cash sound, a rejected threshold change played the
    /// confirm sound — while the status text correctly said "Blocked".
    /// Callers pass the operation's bool through here; the status text
    /// already comes from the operation's message. The cash thresholds
    /// ($100 heavy, $10 medium, below that small) mirror
    /// GeneralStorePanelController.ResolveMoneyFeedbackKind exactly.
    /// </summary>
    public static class TransactionFeedbackDecision
    {
        /// <summary>Success kind when the operation succeeded, Warning otherwise.</summary>
        public static LLFeedbackKind ForOperation(bool succeeded, LLFeedbackKind successKind)
        {
            return succeeded ? successKind : LLFeedbackKind.Warning;
        }

        /// <summary>Approval stamp only for a submitted loan application.</summary>
        public static LLFeedbackKind ForLoanSubmission(bool submitted)
        {
            return submitted ? LLFeedbackKind.StampApproval : LLFeedbackKind.Warning;
        }

        /// <summary>Cash sound sized to the completed amount; Warning when blocked.</summary>
        public static LLFeedbackKind ForCashTransfer(bool completed, int amountCents)
        {
            if (!completed) return LLFeedbackKind.Warning;
            int absolute = amountCents < 0 ? -amountCents : amountCents;
            if (absolute >= 10000) return LLFeedbackKind.HeavyCash;
            if (absolute >= 1000) return LLFeedbackKind.MediumCash;
            return LLFeedbackKind.SmallCash;
        }
    }
}
