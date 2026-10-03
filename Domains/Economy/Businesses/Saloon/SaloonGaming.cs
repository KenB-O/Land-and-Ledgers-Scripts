using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Saloon
{
    /// <summary>
    /// T2G: a faro bankroll with an EXPLICIT owner (Tech X §7.2). The premises
    /// owner does not automatically own the dealer/banker bankroll or the
    /// gambling P&L — the bankroll is a separate book by construction.
    /// </summary>
    [Serializable]
    public sealed class FaroBankroll
    {
        public string BankrollId = string.Empty;
        /// <summary>The dealer/banker who owns this bankroll (named person or business).</summary>
        public string OwnerName = string.Empty;
        public int BankrollCents;
        /// <summary>Cumulative net P&L to the bankroll owner (separate from the house).</summary>
        public int CumulativeProfitCents;

        public FaroBankroll() { }
    }

    /// <summary>
    /// T2G: a faro concession agreement (Tech X §7.3). Expresses premises/
    /// space, term, payment cadence, exclusivity, bankroll responsibility,
    /// revenue ownership, breach and termination — as written, never templated
    /// silently.
    /// </summary>
    [Serializable]
    public sealed class FaroConcessionAgreement
    {
        public string AgreementId = string.Empty;
        public string SaloonBusinessInstanceId = string.Empty;
        public string ConcessionaireName = string.Empty; // the faro dealer/banker
        public string SpaceDescription = string.Empty;
        public int StartDayIndex;
        public int EndDayIndex = -1;
        /// <summary>What the house is paid, and how often — the ONLY money that flows to the house.</summary>
        public int ConcessionPaymentCents;
        public string PaymentCadence = string.Empty; // e.g. "weekly"
        public bool Exclusive;
        public string BankrollResponsibility = string.Empty;
        public string RevenueOwnership = string.Empty; // who owns the gambling P&L
        public string BreachTerms = string.Empty;
        public bool Terminated;

        public FaroConcessionAgreement() { }
    }

    /// <summary>
    /// T2G: the faro concession. The dealer's P&L lives on the bankroll and
    /// NEVER flows into the house except through the explicit concession
    /// payment (Tech X §12.4 regression: "confirm dealer/banker P&L does not
    /// flow into Crown except through explicit concession/food-drink terms").
    /// </summary>
    public sealed class FaroConcession
    {
        private readonly FaroConcessionAgreement agreement;
        private readonly FaroBankroll bankroll;
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public FaroConcession(FaroConcessionAgreement agreement, FaroBankroll bankroll)
        {
            this.agreement = agreement;
            this.bankroll = bankroll;
        }

        /// <summary>
        /// T2G: settles one faro session. netToDealerCents > 0 means the dealer
        /// won. The P&L posts to the BANKROLL only. Returns the concession
        /// payment due to the house per the agreement's cadence (the caller
        /// books it as ordinary house revenue on the cadence day).
        /// </summary>
        public int SettleSession(int netToDealerCents, bool isCadenceDay, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (agreement == null || bankroll == null)
            {
                diag.Add("FaroConcession.SettleSession: no agreement or bankroll — no game.");
                return 0;
            }
            if (agreement.Terminated)
            {
                diag.Add("FaroConcession.SettleSession: concession terminated — no game.");
                return 0;
            }
            bankroll.BankrollCents += netToDealerCents;
            bankroll.CumulativeProfitCents += netToDealerCents;
            diag.Add($"FaroConcession: session settled {netToDealerCents}c to {bankroll.OwnerName}'s bankroll (now {bankroll.BankrollCents}c). House books nothing from the game itself.");

            if (isCadenceDay && agreement.ConcessionPaymentCents > 0)
            {
                diag.Add($"FaroConcession: concession payment {agreement.ConcessionPaymentCents}c due to the house ({agreement.PaymentCadence}) — the ONLY house money from faro.");
                return agreement.ConcessionPaymentCents;
            }
            return 0;
        }

        public string Terminate(string reason, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (agreement == null) return "FaroConcession.Terminate: no agreement.";
            agreement.Terminated = true;
            diag.Add($"FaroConcession: concession terminated — {reason}. Bankroll ({bankroll?.BankrollCents}c) remains {bankroll?.OwnerName}'s.");
            return null;
        }
    }

    /// <summary>
    /// T2G: free poker (Tech X §7.1). Games run with ZERO gaming-revenue
    /// transactions; the house earns only the ordinary food/drink the players
    /// order. The separation is structural: this class cannot move money to
    /// the house at all — it only counts linger.
    /// </summary>
    public sealed class FreePokerTable
    {
        private int gamesHosted;
        private int playerOrders; // food/drink orders attributed to poker players

        public int GamesHosted => gamesHosted;
        public int PlayerOrders => playerOrders;

        /// <summary>Hosts a game. No money changes hands on the game itself — ever.</summary>
        public void HostGame(int playersSeated, int ordersPlaced, List<string> diag)
        {
            gamesHosted++;
            playerOrders += Math.Max(0, ordersPlaced);
            if (diag != null)
                diag.Add($"FreePokerTable: game hosted for {playersSeated} players — gaming revenue 0c (structural); {ordersPlaced} food/drink orders to the house.");
        }
    }
}
