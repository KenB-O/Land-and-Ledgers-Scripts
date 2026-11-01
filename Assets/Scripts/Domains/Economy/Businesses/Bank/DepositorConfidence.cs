using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Economy.Financing;

namespace LandLedgers.Economy.Bank
{
    /// <summary>
    /// D4B: classes of depositor a bank can owe money to. Canon §18.10:
    /// "Deposits should come from actual households, businesses, institutions
    /// and counterparties that choose where to keep money when local banking
    /// exists." Large depositors create concentration risk: "losing one mine
    /// payroll account or major merchant balance can matter more than many
    /// small withdrawals."
    /// </summary>
    public enum DepositorClass
    {
        Unspecified = 0,
        /// <summary>A person or family keeping savings / transaction balances.</summary>
        Household = 1,
        /// <summary>A local business — store, mill, mine payroll account, etc.</summary>
        Business = 2,
        /// <summary>An institution — church, lodge, municipality, school fund.</summary>
        Institution = 3,
        /// <summary>Another bank holding an interbank deposit (D4A).</summary>
        CounterpartyBank = 4,
    }

    /// <summary>
    /// D4B: the four panic-transmission channels, Canon §18.11: "During an
    /// outside panic, correspondent availability, withdrawal demand, asset
    /// saleability and new-loan appetite can all worsen through separate
    /// channels." Each channel is recorded independently — the transmission
    /// is two-way and channel-specific, never one national panic number.
    /// </summary>
    public enum PanicTransmissionChannel
    {
        Unspecified = 0,
        /// <summary>Can the bank draw on / settle through its correspondents?</summary>
        CorrespondentAvailability = 1,
        /// <summary>Are depositors and correspondents pulling funds faster?</summary>
        WithdrawalDemand = 2,
        /// <summary>Can the bank sell assets (paper, property) for cash at anything like book?</summary>
        AssetSaleability = 3,
        /// <summary>Is there outside appetite to take the bank's new loans / paper?</summary>
        NewLoanAppetite = 4,
    }

    /// <summary>
    /// D4B: confidence tier of one bank on one day. A SIGNAL for the operating
    /// layer, not an enacted mechanic: nothing in D4B suspends credit, closes
    /// a bank, or moves money. Canon §18.11's "a conservative local bank may
    /// remain open but tighten credit; an overextended bank may suspend or
    /// fail" is the operating layer's decision to make, reading this signal.
    /// </summary>
    public enum DepositorConfidenceTier
    {
        Unspecified = 0,
        Stable = 1,
        Watch = 2,
        Strained = 3,
        Critical = 4,
    }

    /// <summary>
    /// D4B: one recorded withdrawal attempt — a timed withdrawal event with
    /// amount, depositor class, and outcome. Canon §18.10: "A bank run is ...
    /// an extreme acceleration of a real liability, not a random event card.
    /// Rumor can matter only because depositors already have funds they can
    /// attempt to withdraw." Events are therefore recorded only from REAL
    /// withdrawal attempts executed against the bank's own ledger
    /// (DepositorConfidenceLedger.RecordWithdrawalAttempt) — never
    /// synthesized, never a rumor roll.
    /// </summary>
    [Serializable]
    public sealed class DepositorConfidenceWithdrawalEvent
    {
        public string EventId = string.Empty;
        public string BankInstanceId = string.Empty;
        public int DayIndex;
        public string AccountId = string.Empty;
        public string DepositorName = string.Empty;
        public DepositorClass Class = DepositorClass.Unspecified;
        public int AmountCents;
        public bool WasHonored;
        /// <summary>True when the refusal was the BANK's lack of cash, not the account's (a real run signal).</summary>
        public bool LiquidityRefusal;
        public string RefusalReason = string.Empty;
        public bool WasDemandAccount;

        public DepositorConfidenceWithdrawalEvent() { }
    }

    /// <summary>
    /// D4B: one recorded outside-support inflow — Canon §18.10's recovery
    /// channel "outside support" (a correspondent draw, emergency capital,
    /// a backstop line actually funded). Recorded only when real money
    /// arrives; a promised backstop that never funds is not support.
    /// </summary>
    [Serializable]
    public sealed class DepositorConfidenceSupportEvent
    {
        public string EventId = string.Empty;
        public string BankInstanceId = string.Empty;
        public int DayIndex;
        public int AmountCents;
        public string SourceNote = string.Empty;

        public DepositorConfidenceSupportEvent() { }
    }

    /// <summary>
    /// D4B: one dated reading of one panic-transmission channel (Canon
    /// §18.11). Pressure01 runs 0 (normal) to 1 (channel fully shut) —
    /// worsening only; a channel recovering is recorded as a lower reading.
    /// </summary>
    [Serializable]
    public sealed class PanicTransmissionReading
    {
        public string ReadingId = string.Empty;
        public string BankInstanceId = string.Empty;
        public int DayIndex;
        public PanicTransmissionChannel Channel = PanicTransmissionChannel.Unspecified;
        public double Pressure01;
        public string Notes = string.Empty;

        public PanicTransmissionReading() { }
    }

    /// <summary>
    /// D4B: parameterized signal thresholds. CALIBRATION, not canon — the
    /// canon prescribes the mechanisms (§18.8–§18.11) but no numbers. These
    /// tune when the confidence SIGNAL moves between tiers; they enact
    /// nothing. The operating layer decides what a tier MEANS (tighten
    /// credit, suspend, fail — Canon §18.11).
    /// </summary>
    [Serializable]
    public sealed class DepositorConfidenceThresholds
    {
        /// <summary>Recent window for the withdrawal-acceleration numerator, days.</summary>
        public int RecentWindowDays = 3;
        /// <summary>Baseline window immediately before the recent window, days.</summary>
        public int BaselineWindowDays = 30;
        /// <summary>Liquidity coverage at/below this is Strained; strictly below the critical line is Critical.</summary>
        public double LiquidityCoverageWarnRatio = 0.25;
        public double LiquidityCoverageCriticalRatio = 0.10;
        /// <summary>Withdrawal-acceleration factor reaching this is Strained / Critical.</summary>
        public double AccelerationWarnFactor = 3.0;
        public double AccelerationCriticalFactor = 8.0;
        /// <summary>Largest single depositor's share of deposits owed reaching this is Watch / Strained (Canon §18.10 concentration risk).</summary>
        public double LargestDepositorWarnShare = 0.25;
        public double LargestDepositorCriticalShare = 0.50;
        /// <summary>Consecutive refused withdrawals reaching this is Critical.</summary>
        public int RefusedStreakCritical = 3;
        /// <summary>Window for the honored-withdrawal ratio and support totals, days.</summary>
        public int HonoredWindowDays = 30;

        public DepositorConfidenceThresholds() { }
    }

    /// <summary>
    /// D4B: the computed depositor-confidence signal for one bank on one day.
    /// Every component is a reported fact about real books and real recorded
    /// events — Canon §18.8: "Do not reduce this to one Reserve slider that
    /// directly grants profit or safety; policy should matter through the
    /// actual composition and timing of the bank's commitments." This signal
    /// REPORTS composition and timing; it grants nothing and moves nothing.
    /// Notably absent by canon design (§18.10): any reputation input — "a
    /// cosmetic reputation purchase should not stop a real run."
    /// </summary>
    [Serializable]
    public sealed class DepositorConfidenceSignal
    {
        public string BankInstanceId = string.Empty;
        public int DayIndex;
        /// <summary>Deposits withdrawable on demand today: demand balances + accrued, plus matured term balances + accrued (Canon §18.9 maturity timing).</summary>
        public int DemandableDepositsCents;
        /// <summary>Cash on hand plus drawable (demand-kind) interbank deposits at other banks — the liquid correspondent balances of Canon §18.8.</summary>
        public int LiquidCoverageCents;
        /// <summary>LiquidCoverageCents / DemandableDepositsCents; 1.0 when nothing is demandable.</summary>
        public double LiquidityCoverageRatio;
        /// <summary>Recent daily withdrawal volume / baseline daily volume; -1 when the baseline is empty (unknown, not zero).</summary>
        public double WithdrawalAccelerationFactor = -1.0;
        /// <summary>Largest single account's share of deposits owed (Canon §18.10 concentration risk).</summary>
        public double LargestDepositorShareRatio;
        /// <summary>Honored / (honored + refused) over the window; -1 when no attempts.</summary>
        public double HonoredWithdrawalRatio = -1.0;
        /// <summary>Consecutive refused withdrawals at the end of the event log.</summary>
        public int RefusedStreak;
        /// <summary>Days since the last refused withdrawal; -1 when none recorded.</summary>
        public int DaysSinceLastRefusal = -1;
        /// <summary>Recorded outside-support inflows over the window (Canon §18.10 recovery).</summary>
        public int OutsideSupportCents;
        /// <summary>Owner equity: cash on hand minus deposits owed (W7).</summary>
        public int EquityCents;
        public int VaultCashCents;
        public DepositorConfidenceTier Tier = DepositorConfidenceTier.Unspecified;

        public DepositorConfidenceSignal() { }
    }

    /// <summary>
    /// D4B: per-bank depositor-confidence records and signal computation
    /// (Canon §18.8–§18.11, §18.14–§18.15).
    ///
    /// What this builds (exactly what the canon describes, no more):
    /// - Timed withdrawal-event records with amounts and depositor classes,
    ///   from REAL withdrawals executed through the bank's own ledger —
    ///   "an extreme acceleration of a real liability, not a random event
    ///   card" (§18.10).
    /// - Liquidity-coverage computation from the real books: ledger cash on
    ///   hand plus drawable interbank assets (D4A) against demandable
    ///   deposits (§18.8 composition, §18.9 maturity timing). A reported
    ///   signal, not a reserve slider that grants profit or safety (§18.8).
    /// - Panic-transmission channel readings, one per channel (§18.11):
    ///   correspondent availability, withdrawal demand, asset saleability,
    ///   new-loan appetite.
    /// - Parameterized tier thresholds — signals for the operating layer.
    ///
    /// What this deliberately does NOT do (canon leaves these to the
    /// operating layer / future behavior model): tighten credit, suspend or
    /// fail a bank, synthesize runs or rumors, or accept any reputation /
    /// cosmetic input as confidence ("a cosmetic reputation purchase should
    /// not stop a real run", §18.10).
    ///
    /// The ledger is the single recorder for its bank; the operating layer
    /// routes withdrawals through RecordWithdrawalAttempt so the pattern
    /// record can never diverge from what the books actually did.
    /// </summary>
    public sealed class DepositorConfidenceLedger
    {
        private string bankInstanceId = string.Empty;
        private readonly Dictionary<string, DepositorClass> classRegistry =
            new Dictionary<string, DepositorClass>(StringComparer.Ordinal);
        private readonly List<DepositorConfidenceWithdrawalEvent> withdrawalEvents =
            new List<DepositorConfidenceWithdrawalEvent>();
        private readonly List<DepositorConfidenceSupportEvent> supportEvents =
            new List<DepositorConfidenceSupportEvent>();
        private readonly List<PanicTransmissionReading> panicReadings =
            new List<PanicTransmissionReading>();
        private readonly List<string> diagnostics = new List<string>();
        private InterbankSettlement interbank;
        private int nextEventNumber = 1;
        private int nextSupportNumber = 1;
        private int nextReadingNumber = 1;

        public string BankInstanceId => bankInstanceId ?? string.Empty;
        public DepositorConfidenceThresholds Thresholds = new DepositorConfidenceThresholds();
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<DepositorConfidenceWithdrawalEvent> WithdrawalEvents => withdrawalEvents;
        public IReadOnlyList<DepositorConfidenceSupportEvent> SupportEvents => supportEvents;
        public IReadOnlyList<PanicTransmissionReading> PanicReadings => panicReadings;

        public DepositorConfidenceLedger() { }

        public DepositorConfidenceLedger(string bankInstanceId)
        {
            this.bankInstanceId = bankInstanceId ?? string.Empty;
        }

        /// <summary>Attaches the D4A interbank service so drawable interbank assets count toward liquid coverage and interbank depositors resolve to CounterpartyBank. Read-only use — never writes.</summary>
        public void AttachInterbankSettlement(InterbankSettlement settlement)
        {
            interbank = settlement;
        }

        /// <summary>
        /// Assigns a depositor class to an account (Canon §18.10: households,
        /// businesses, institutions choose where to keep money). The
        /// operating layer registers this from its own depositor knowledge;
        /// unregistered accounts resolve as Unspecified unless they are
        /// interbank deposits (CounterpartyBank via the D4A asset view).
        /// </summary>
        public string RegisterDepositorClass(string accountId, DepositorClass depositorClass,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(accountId))
                return "DepositorConfidenceLedger.RegisterDepositorClass: an account id is required.";
            if (depositorClass == DepositorClass.Unspecified)
                return "DepositorConfidenceLedger.RegisterDepositorClass: state the actual depositor class — household, business, institution, or counterparty bank.";
            classRegistry[accountId] = depositorClass;
            diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: '{accountId}' classified as {depositorClass}.");
            return null;
        }

        /// <summary>
        /// Resolves an account's depositor class: explicit registration first,
        /// then D4A interbank-deposit membership (CounterpartyBank), else
        /// Unspecified. Read-only.
        /// </summary>
        public DepositorClass ResolveDepositorClass(BankRuntime bank, string accountId)
        {
            if (!string.IsNullOrWhiteSpace(accountId) &&
                classRegistry.TryGetValue(accountId, out DepositorClass registered))
                return registered;
            if (interbank != null && bank != null)
            {
                foreach (InterbankDepositAsset asset in interbank.DepositAssets)
                {
                    if (string.Equals(asset.AssetId, accountId, StringComparison.Ordinal) &&
                        string.Equals(asset.HoldingBankId, bank.BusinessInstanceId, StringComparison.Ordinal))
                        return DepositorClass.CounterpartyBank;
                }
            }
            return DepositorClass.Unspecified;
        }

        /// <summary>
        /// Records a REAL withdrawal attempt: executes it against the bank's
        /// own NX-3B ledger and the W7 vault (two-track cash model — the
        /// ledger leg and the physical lots move together), then records the
        /// timed event with amount, depositor class, and outcome. This is the
        /// ONLY path that writes withdrawal-pattern records, so the pattern
        /// can never diverge from what the books did (Canon §18.10).
        ///
        /// A withdrawal the bank's CASH cannot cover is refused before the
        /// account is touched and recorded as a liquidity refusal — the run
        /// signal. A withdrawal the ACCOUNT cannot cover (or a term lock) is
        /// refused by the ledger's own guards and recorded as refused, not a
        /// liquidity event.
        /// </summary>
        /// <returns>
        /// Null when the withdrawal was honored (cash lots are returned via
        /// cashPaidLots for the operating layer to hand to the depositor);
        /// otherwise the refusal reason. cashPaidLots is null on refusal.
        /// </returns>
        public string RecordWithdrawalAttempt(BankRuntime bank, string accountId,
            int amountCents, int dayIndex, bool earlyTermWithdrawalAccepted,
            out List<SpecieLot> cashPaidLots, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            cashPaidLots = null;
            const string op = "RecordWithdrawalAttempt";
            if (bank == null)
                return $"DepositorConfidenceLedger.{op}: a bank is required.";
            if (!string.Equals(bank.BusinessInstanceId, bankInstanceId, StringComparison.Ordinal))
                diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: WARNING — recording an attempt for a different bank '{bank.BusinessInstanceId}'. Check the wiring; the event is recorded anyway.");
            if (string.IsNullOrWhiteSpace(accountId))
                return $"DepositorConfidenceLedger.{op}: an account id is required.";
            if (amountCents <= 0)
                return $"DepositorConfidenceLedger.{op}: the withdrawal must be positive.";

            DepositAccount account = null;
            foreach (DepositAccount a in bank.Ledger.Accounts)
            {
                if (string.Equals(a.AccountId, accountId, StringComparison.Ordinal))
                {
                    account = a;
                    break;
                }
            }
            DepositorClass depositorClass = ResolveDepositorClass(bank, accountId);
            bool wasDemand = account != null && account.Kind == DepositKind.Demand;
            string depositorName = account != null ? account.DepositorName : string.Empty;

            // The bank's cash is the run constraint: short cash refuses
            // BEFORE the account is touched, and the refusal is flagged as
            // a liquidity event (Canon §18.10 — a run accelerates a real
            // liability the bank cannot currently meet).
            if (bank.Ledger.CashOnHandCents < amountCents)
            {
                string reason = $"the bank holds {bank.Ledger.CashOnHandCents}c cash against a {amountCents}c withdrawal — refused, not faked (Canon §18.10)";
                RecordEvent(accountId, depositorName, depositorClass, amountCents, false, true, reason, wasDemand, dayIndex, diag);
                diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: LIQUIDITY REFUSAL — '{accountId}' ({depositorName}) asked for {amountCents}c; {reason}.");
                return $"DepositorConfidenceLedger.{op}: {reason}.";
            }

            string refused = bank.Ledger.Withdraw(accountId, amountCents, dayIndex,
                earlyTermWithdrawalAccepted, diag);
            if (refused != null)
            {
                RecordEvent(accountId, depositorName, depositorClass, amountCents, false, false, refused, wasDemand, dayIndex, diag);
                return refused;
            }

            // Two-track cash model (W7): the physical lots leave with the
            // accounting leg. A vault that cannot produce what the ledger
            // just paid is a reconciliation break — unwind the ledger leg
            // and record the refusal rather than diverge.
            List<SpecieLot> drawn = bank.DrawVaultLots(amountCents, diag);
            if (drawn == null)
            {
                bank.Ledger.Deposit(accountId, amountCents, dayIndex, diag);
                string reason = $"vault could not produce {amountCents}c the ledger just paid — unwound, recorded as a liquidity refusal";
                RecordEvent(accountId, depositorName, depositorClass, amountCents, false, true, reason, wasDemand, dayIndex, diag);
                diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: VAULT SHORT on a honored ledger withdrawal — {reason}.");
                return $"DepositorConfidenceLedger.{op}: {reason}.";
            }

            cashPaidLots = drawn;
            RecordEvent(accountId, depositorName, depositorClass, amountCents, true, false, string.Empty, wasDemand, dayIndex, diag);
            return null;
        }

        private void RecordEvent(string accountId, string depositorName,
            DepositorClass depositorClass, int amountCents, bool wasHonored,
            bool liquidityRefusal, string refusalReason, bool wasDemandAccount,
            int dayIndex, List<string> diag)
        {
            var evt = new DepositorConfidenceWithdrawalEvent
            {
                EventId = $"DCW-{nextEventNumber++:D4}",
                BankInstanceId = bankInstanceId,
                DayIndex = dayIndex,
                AccountId = accountId ?? string.Empty,
                DepositorName = depositorName ?? string.Empty,
                Class = depositorClass,
                AmountCents = amountCents,
                WasHonored = wasHonored,
                LiquidityRefusal = liquidityRefusal,
                RefusalReason = refusalReason ?? string.Empty,
                WasDemandAccount = wasDemandAccount,
            };
            withdrawalEvents.Add(evt);
            diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: withdrawal {(wasHonored ? "HONORED" : "REFUSED")} — " +
                $"'{evt.AccountId}' ({evt.DepositorName}, {depositorClass}) {amountCents}c, day {dayIndex}" +
                (wasHonored ? "." : $" — {refusalReason}"));
        }

        /// <summary>
        /// Records an outside-support inflow that REALLY arrived (Canon §18.10
        /// recovery channel: "outside support"). The operating layer pairs
        /// this with the actual cash movement; this record is the confidence
        /// input. A promised backstop that never funds is not support and
        /// must not be recorded.
        /// </summary>
        public string RecordOutsideSupport(int dayIndex, int amountCents, string sourceNote,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (amountCents <= 0)
                return "DepositorConfidenceLedger.RecordOutsideSupport: the support must be positive — record what arrived, not what was promised.";
            if (string.IsNullOrWhiteSpace(sourceNote))
                return "DepositorConfidenceLedger.RecordOutsideSupport: the support source must be named — no anonymous rescues.";
            supportEvents.Add(new DepositorConfidenceSupportEvent
            {
                EventId = $"DCS-{nextSupportNumber++:D4}",
                BankInstanceId = bankInstanceId,
                DayIndex = dayIndex,
                AmountCents = amountCents,
                SourceNote = sourceNote,
            });
            diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: outside support +{amountCents}c ({sourceNote}), day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// Records one dated reading of one panic-transmission channel
        /// (Canon §18.11). Pressure runs 0 (normal) to 1 (channel fully
        /// shut); values outside are refused, not clamped — a miscalibrated
        /// feed must be fixed, not silently squeezed into range.
        /// </summary>
        public string RecordPanicChannel(int dayIndex, PanicTransmissionChannel channel,
            double pressure01, string notes, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (channel == PanicTransmissionChannel.Unspecified)
                return "DepositorConfidenceLedger.RecordPanicChannel: name the channel — correspondent availability, withdrawal demand, asset saleability, or new-loan appetite.";
            if (double.IsNaN(pressure01) || pressure01 < 0.0 || pressure01 > 1.0)
                return $"DepositorConfidenceLedger.RecordPanicChannel: pressure {pressure01} is outside [0,1] — fix the feed, do not clamp it.";
            panicReadings.Add(new PanicTransmissionReading
            {
                ReadingId = $"DCP-{nextReadingNumber++:D4}",
                BankInstanceId = bankInstanceId,
                DayIndex = dayIndex,
                Channel = channel,
                Pressure01 = pressure01,
                Notes = notes ?? string.Empty,
            });
            diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: panic channel {channel} = {pressure01:F2}, day {dayIndex}{(string.IsNullOrWhiteSpace(notes) ? "." : $" ({notes}).")}");
            return null;
        }

        /// <summary>
        /// Latest recorded pressure for one channel at or before the given
        /// day; 0 when never recorded (normal until observed otherwise).
        /// Read-only.
        /// </summary>
        public double PanicPressure(int dayIndex, PanicTransmissionChannel channel)
        {
            double pressure = 0.0;
            int latestDay = int.MinValue;
            foreach (PanicTransmissionReading reading in panicReadings)
            {
                if (reading.Channel != channel || reading.DayIndex > dayIndex) continue;
                if (reading.DayIndex >= latestDay)
                {
                    latestDay = reading.DayIndex;
                    pressure = reading.Pressure01;
                }
            }
            return pressure;
        }

        /// <summary>
        /// Deposits withdrawable on demand today (Canon §18.9 maturity
        /// timing): every demand account's balance + accrued, plus term
        /// accounts at/after maturity with their accrued. Unmatured term
        /// deposits are commitments with timing — not demandable.
        /// Read-only.
        /// </summary>
        public int DemandableDepositsCents(BankRuntime bank, int dayIndex)
        {
            if (bank == null) return 0;
            int total = 0;
            foreach (DepositAccount account in bank.Ledger.Accounts)
            {
                if (account.Kind == DepositKind.Demand)
                    total += account.BalanceCents + account.InterestAccruedCents;
                else if (account.Kind == DepositKind.Term && dayIndex >= account.MaturityDayIndex)
                    total += account.BalanceCents + account.InterestAccruedCents;
            }
            return Math.Max(0, total);
        }

        /// <summary>
        /// Liquid coverage (Canon §18.8): ledger cash on hand plus DRAWABLE
        /// interbank assets — demand-kind D4A deposits this bank holds at
        /// other banks (liquid correspondent balances). Term interbank
        /// deposits are excluded by timing: they are not drawable today.
        /// Open claims are excluded: owed is not drawable. Read-only.
        /// </summary>
        public int LiquidCoverageCents(BankRuntime bank)
        {
            if (bank == null) return 0;
            int total = Math.Max(0, bank.Ledger.CashOnHandCents);
            if (interbank != null)
            {
                foreach (InterbankDepositAsset asset in interbank.DepositAssets)
                {
                    if (string.Equals(asset.DepositingBankId, bank.BusinessInstanceId, StringComparison.Ordinal) &&
                        asset.Kind == DepositKind.Demand)
                        total += Math.Max(0, asset.AssetValueCents());
                }
            }
            return total;
        }

        /// <summary>Liquid coverage over demandable deposits; 1.0 when nothing is demandable (no pressure). Read-only.</summary>
        public double LiquidityCoverageRatio(BankRuntime bank, int dayIndex)
        {
            int demandable = DemandableDepositsCents(bank, dayIndex);
            if (demandable <= 0) return 1.0;
            return (double)LiquidCoverageCents(bank) / demandable;
        }

        /// <summary>
        /// Withdrawal acceleration: recent-window daily honored-withdrawal
        /// volume over baseline-window daily volume (Canon §18.10 — a run
        /// is an EXTREME ACCELERATION of the real liability). Returns -1
        /// when the baseline window holds no honored withdrawals: unknown,
        /// not zero. Read-only.
        /// </summary>
        public double WithdrawalAccelerationFactor(int dayIndex)
        {
            int recentDays = Math.Max(1, Thresholds.RecentWindowDays);
            int baselineDays = Math.Max(1, Thresholds.BaselineWindowDays);
            int recentStart = dayIndex - recentDays;
            int baselineStart = recentStart - baselineDays;
            long recentTotal = 0;
            long baselineTotal = 0;
            foreach (DepositorConfidenceWithdrawalEvent evt in withdrawalEvents)
            {
                if (!evt.WasHonored) continue;
                if (evt.DayIndex > recentStart && evt.DayIndex <= dayIndex)
                    recentTotal += evt.AmountCents;
                else if (evt.DayIndex > baselineStart && evt.DayIndex <= recentStart)
                    baselineTotal += evt.AmountCents;
            }
            if (baselineTotal <= 0) return -1.0;
            double recentDaily = (double)recentTotal / recentDays;
            double baselineDaily = (double)baselineTotal / baselineDays;
            if (baselineDaily <= 0.0) return -1.0;
            return recentDaily / baselineDaily;
        }

        /// <summary>Largest single account's share of deposits owed (Canon §18.10 concentration risk); 0 when nothing is owed. Read-only.</summary>
        public double LargestDepositorShareRatio(BankRuntime bank)
        {
            if (bank == null) return 0.0;
            int owed = bank.Ledger.DepositsOwedCents();
            if (owed <= 0) return 0.0;
            int largest = 0;
            foreach (DepositAccount account in bank.Ledger.Accounts)
                largest = Math.Max(largest, account.BalanceCents + account.InterestAccruedCents);
            return (double)largest / owed;
        }

        /// <summary>Honored / (honored + refused) over the window; -1 when no attempts. Read-only.</summary>
        public double HonoredWithdrawalRatio(int dayIndex, int windowDays)
        {
            int honored = 0;
            int refused = 0;
            int start = dayIndex - Math.Max(1, windowDays);
            foreach (DepositorConfidenceWithdrawalEvent evt in withdrawalEvents)
            {
                if (evt.DayIndex <= start || evt.DayIndex > dayIndex) continue;
                if (evt.WasHonored) honored++;
                else refused++;
            }
            if (honored + refused == 0) return -1.0;
            return (double)honored / (honored + refused);
        }

        /// <summary>Consecutive refused withdrawals at the end of the event log. Read-only.</summary>
        public int RefusedStreakCount()
        {
            int streak = 0;
            for (int i = withdrawalEvents.Count - 1; i >= 0; i--)
            {
                if (!withdrawalEvents[i].WasHonored) streak++;
                else break;
            }
            return streak;
        }

        /// <summary>Days since the last refused withdrawal; -1 when none recorded. Read-only.</summary>
        public int DaysSinceLastRefusal(int dayIndex)
        {
            int latest = int.MinValue;
            foreach (DepositorConfidenceWithdrawalEvent evt in withdrawalEvents)
            {
                if (!evt.WasHonored && evt.DayIndex <= dayIndex && evt.DayIndex > latest)
                    latest = evt.DayIndex;
            }
            return latest == int.MinValue ? -1 : dayIndex - latest;
        }

        /// <summary>Recorded outside-support inflows over the window (Canon §18.10 recovery). Read-only.</summary>
        public int OutsideSupportCents(int dayIndex, int windowDays)
        {
            int total = 0;
            int start = dayIndex - Math.Max(1, windowDays);
            foreach (DepositorConfidenceSupportEvent evt in supportEvents)
                if (evt.DayIndex > start && evt.DayIndex <= dayIndex)
                    total += evt.AmountCents;
            return Math.Max(0, total);
        }

        /// <summary>
        /// Builds the full confidence signal for one bank on one day.
        /// Read-only: every input is computed from the bank's real books
        /// and this ledger's real records; nothing is mutated.
        /// </summary>
        public DepositorConfidenceSignal ComputeSignal(BankRuntime bank, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            var signal = new DepositorConfidenceSignal
            {
                BankInstanceId = bankInstanceId,
                DayIndex = dayIndex,
            };
            if (bank == null)
            {
                diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: ComputeSignal with no bank — empty signal.");
                return signal;
            }
            if (!string.Equals(bank.BusinessInstanceId, bankInstanceId, StringComparison.Ordinal))
                diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: WARNING — computing a signal for a different bank '{bank.BusinessInstanceId}'.");

            signal.DemandableDepositsCents = DemandableDepositsCents(bank, dayIndex);
            signal.LiquidCoverageCents = LiquidCoverageCents(bank);
            signal.LiquidityCoverageRatio = LiquidityCoverageRatio(bank, dayIndex);
            signal.WithdrawalAccelerationFactor = WithdrawalAccelerationFactor(dayIndex);
            signal.LargestDepositorShareRatio = LargestDepositorShareRatio(bank);
            signal.HonoredWithdrawalRatio = HonoredWithdrawalRatio(dayIndex, Thresholds.HonoredWindowDays);
            signal.RefusedStreak = RefusedStreakCount();
            signal.DaysSinceLastRefusal = DaysSinceLastRefusal(dayIndex);
            signal.OutsideSupportCents = OutsideSupportCents(dayIndex, Thresholds.HonoredWindowDays);
            signal.EquityCents = bank.OwnerEquityCents();
            signal.VaultCashCents = bank.VaultSpecieTotalCents();
            signal.Tier = ClassifyTier(signal);

            diag.Add($"DepositorConfidenceLedger [{bankInstanceId}]: signal day {dayIndex} — tier {signal.Tier}, " +
                $"coverage {signal.LiquidityCoverageRatio:F3} ({signal.LiquidCoverageCents}c / {signal.DemandableDepositsCents}c demandable), " +
                $"acceleration {(signal.WithdrawalAccelerationFactor < 0 ? "n/a" : signal.WithdrawalAccelerationFactor.ToString("F2") + "x")}, " +
                $"largest share {signal.LargestDepositorShareRatio:P1}, honored {(signal.HonoredWithdrawalRatio < 0 ? "n/a" : signal.HonoredWithdrawalRatio.ToString("P0"))}, " +
                $"refused streak {signal.RefusedStreak}, equity {signal.EquityCents}c.");
            return signal;
        }

        private DepositorConfidenceTier ClassifyTier(DepositorConfidenceSignal signal)
        {
            double accel = signal.WithdrawalAccelerationFactor;
            bool accelKnown = accel >= 0.0;

            if (signal.LiquidityCoverageRatio < Thresholds.LiquidityCoverageCriticalRatio ||
                (accelKnown && accel >= Thresholds.AccelerationCriticalFactor) ||
                signal.RefusedStreak >= Thresholds.RefusedStreakCritical)
                return DepositorConfidenceTier.Critical;

            if (signal.LiquidityCoverageRatio < Thresholds.LiquidityCoverageWarnRatio ||
                (accelKnown && accel >= Thresholds.AccelerationWarnFactor) ||
                signal.LargestDepositorShareRatio >= Thresholds.LargestDepositorCriticalShare)
                return DepositorConfidenceTier.Strained;

            if (signal.LargestDepositorShareRatio >= Thresholds.LargestDepositorWarnShare ||
                (signal.DaysSinceLastRefusal >= 0 && signal.DaysSinceLastRefusal <= 7) ||
                (signal.HonoredWithdrawalRatio >= 0.0 && signal.HonoredWithdrawalRatio < 1.0))
                return DepositorConfidenceTier.Watch;

            return DepositorConfidenceTier.Stable;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        [Serializable]
        public sealed class DepositorConfidenceLedgerSaveDto
        {
            public string BankInstanceId = string.Empty;
            public List<DepositorConfidenceWithdrawalEvent> WithdrawalEvents = new List<DepositorConfidenceWithdrawalEvent>();
            public List<DepositorConfidenceSupportEvent> SupportEvents = new List<DepositorConfidenceSupportEvent>();
            public List<PanicTransmissionReading> PanicReadings = new List<PanicTransmissionReading>();
            public List<DepositorClassRegistration> ClassRegistrations = new List<DepositorClassRegistration>();
            public DepositorConfidenceThresholds Thresholds = new DepositorConfidenceThresholds();
            public int NextEventNumber = 1;
            public int NextSupportNumber = 1;
            public int NextReadingNumber = 1;
        }

        [Serializable]
        public sealed class DepositorClassRegistration
        {
            public string AccountId = string.Empty;
            public DepositorClass Class = DepositorClass.Unspecified;

            public DepositorClassRegistration() { }
        }

        public DepositorConfidenceLedgerSaveDto ToSaveDto()
        {
            var dto = new DepositorConfidenceLedgerSaveDto
            {
                BankInstanceId = bankInstanceId,
                WithdrawalEvents = new List<DepositorConfidenceWithdrawalEvent>(withdrawalEvents),
                SupportEvents = new List<DepositorConfidenceSupportEvent>(supportEvents),
                PanicReadings = new List<PanicTransmissionReading>(panicReadings),
                Thresholds = new DepositorConfidenceThresholds
                {
                    RecentWindowDays = Thresholds.RecentWindowDays,
                    BaselineWindowDays = Thresholds.BaselineWindowDays,
                    LiquidityCoverageWarnRatio = Thresholds.LiquidityCoverageWarnRatio,
                    LiquidityCoverageCriticalRatio = Thresholds.LiquidityCoverageCriticalRatio,
                    AccelerationWarnFactor = Thresholds.AccelerationWarnFactor,
                    AccelerationCriticalFactor = Thresholds.AccelerationCriticalFactor,
                    LargestDepositorWarnShare = Thresholds.LargestDepositorWarnShare,
                    LargestDepositorCriticalShare = Thresholds.LargestDepositorCriticalShare,
                    RefusedStreakCritical = Thresholds.RefusedStreakCritical,
                    HonoredWindowDays = Thresholds.HonoredWindowDays,
                },
                NextEventNumber = Math.Max(1, nextEventNumber),
                NextSupportNumber = Math.Max(1, nextSupportNumber),
                NextReadingNumber = Math.Max(1, nextReadingNumber),
            };
            foreach (KeyValuePair<string, DepositorClass> entry in classRegistry)
                dto.ClassRegistrations.Add(new DepositorClassRegistration
                {
                    AccountId = entry.Key,
                    Class = entry.Value,
                });
            return dto;
        }

        public void LoadFromSaveDto(DepositorConfidenceLedgerSaveDto dto)
        {
            withdrawalEvents.Clear();
            supportEvents.Clear();
            panicReadings.Clear();
            classRegistry.Clear();
            diagnostics.Clear();
            if (dto == null) return;
            bankInstanceId = dto.BankInstanceId ?? string.Empty;
            if (dto.WithdrawalEvents != null)
                foreach (DepositorConfidenceWithdrawalEvent evt in dto.WithdrawalEvents)
                    if (evt != null) withdrawalEvents.Add(evt);
            if (dto.SupportEvents != null)
                foreach (DepositorConfidenceSupportEvent evt in dto.SupportEvents)
                    if (evt != null) supportEvents.Add(evt);
            if (dto.PanicReadings != null)
                foreach (PanicTransmissionReading reading in dto.PanicReadings)
                    if (reading != null) panicReadings.Add(reading);
            if (dto.ClassRegistrations != null)
                foreach (DepositorClassRegistration reg in dto.ClassRegistrations)
                    if (reg != null && !string.IsNullOrWhiteSpace(reg.AccountId) &&
                        reg.Class != DepositorClass.Unspecified)
                        classRegistry[reg.AccountId] = reg.Class;
            if (dto.Thresholds != null) Thresholds = dto.Thresholds;
            nextEventNumber = Math.Max(1, dto.NextEventNumber);
            nextSupportNumber = Math.Max(1, dto.NextSupportNumber);
            nextReadingNumber = Math.Max(1, dto.NextReadingNumber);
        }
    }

    /// <summary>
    /// D4B: read-only confidence views over the W7 bank-as-business runtime.
    /// These read BankRuntime's depositor state (ledger, vault, equity) and
    /// the D4B confidence records without changing ANY bank logic — no new
    /// writes, no policy, no mechanics. The operating layer calls Build to
    /// display or reason about confidence; acting on it stays the operating
    /// layer's job (Canon §18.11).
    /// </summary>
    public static class BankDepositorConfidenceView
    {
        /// <summary>
        /// Builds the confidence signal for a bank as a pure read: ledger
        /// state, vault cash, equity, recorded withdrawal patterns,
        /// panic-channel readings. Mutates nothing on the bank or the
        /// confidence ledger (beyond their own diagnostic lists).
        /// </summary>
        public static DepositorConfidenceSignal Build(BankRuntime bank,
            DepositorConfidenceLedger ledger, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? new List<string>();
            if (bank == null)
            {
                diag.Add("BankDepositorConfidenceView.Build: no bank — empty signal.");
                return new DepositorConfidenceSignal { DayIndex = dayIndex };
            }
            if (ledger == null)
            {
                diag.Add("BankDepositorConfidenceView.Build: no confidence ledger — a signal with no pattern history is not a signal. Returning empty.");
                return new DepositorConfidenceSignal
                {
                    BankInstanceId = bank.BusinessInstanceId,
                    DayIndex = dayIndex,
                };
            }
            DepositorConfidenceSignal signal = ledger.ComputeSignal(bank, dayIndex, diag);
            diag.Add($"BankDepositorConfidenceView: '{bank.BusinessName}' day {dayIndex} — read-only view, tier {signal.Tier} " +
                $"(cash {bank.Ledger.CashOnHandCents}c, owed {bank.Ledger.DepositsOwedCents()}c, equity {signal.EquityCents}c).");
            return signal;
        }
    }
}
