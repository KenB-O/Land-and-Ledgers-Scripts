using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Economy.Financing;

namespace LandLedgers.Economy.Bank
{
    /// <summary>D4A: kinds of interbank paper that can create a claim between banks.</summary>
    public enum InterbankPaperKind
    {
        Unspecified = 0,
        /// <summary>Drawn on a customer of the drawee bank — the drawer's account is debited at presentment.</summary>
        Check = 1,
        /// <summary>Drawn by the issuing bank on itself — the bank is the obligor, no customer account touched.</summary>
        BankDraft = 2,
        /// <summary>Issued by the bank (or post office) — the issuer is the obligor.</summary>
        MoneyOrder = 3,
    }

    /// <summary>D4A: lifecycle of one bank's claim on another bank.</summary>
    public enum InterbankClaimStatus
    {
        Unspecified = 0,
        /// <summary>Recorded, awaiting the next settlement cycle.</summary>
        Open = 1,
        /// <summary>Folded into a net settlement position — no longer individually payable.</summary>
        Netted = 2,
        /// <summary>Paid in full through its settlement position.</summary>
        Settled = 3,
    }

    /// <summary>D4A: lifecycle of a net settlement position between two banks.</summary>
    public enum InterbankPositionStatus
    {
        Unspecified = 0,
        /// <summary>Netted, awaiting settlement in specie or by offset.</summary>
        Open = 1,
        /// <summary>Paid in full — every comprised claim is settled.</summary>
        Settled = 2,
    }

    /// <summary>
    /// D4A: the depositing bank's asset view of money it holds at another
    /// bank (Canon §18.8, §18.11). This is the ASSET half of one account:
    /// the holding bank carries the matching LIABILITY as an ordinary
    /// DepositAccount on its BankDepositLedger (deposits are owed money —
    /// the NX-3B doctrine applies to banks as depositors too). The
    /// InterbankSettlement service is the single writer of both halves, so
    /// the two views cannot diverge.
    ///
    /// InterestAccruedCents mirrors the liability account's accrual (synced
    /// on every settlement cycle). Payout of accrued interest follows the
    /// liability account's own rules — the service never invents a credit.
    /// </summary>
    [Serializable]
    public sealed class InterbankDepositAsset
    {
        public string AssetId = string.Empty;      // == the account id on the holding bank's ledger
        public string DepositingBankId = string.Empty;
        public string HoldingBankId = string.Empty;
        public DepositKind Kind = DepositKind.Unspecified;
        public int BalanceCents;
        public int InterestAccruedCents;
        public int OpenedDayIndex;
        public int TermDays;                       // Term only
        public int MaturityDayIndex;               // Term only
        public int InterestRateBps;                // Term only: basis points per annum, as written

        /// <summary>Asset value the depositing bank carries: balance plus mirrored accrual.</summary>
        public int AssetValueCents() => BalanceCents + InterestAccruedCents;

        public InterbankDepositAsset() { }
    }

    /// <summary>
    /// D4A: one bank's recorded obligation to another bank — an explicit
    /// claim, never an invented balance (D4A hard constraint). A claim is
    /// born from a real event: a check/draft/money-order drawn on the debtor
    /// bank presented at the creditor bank, which honored it for the payee.
    /// The creditor bank paid real value (cash over the counter or a real
    /// deposit credit); this claim is the offsetting asset on its books and
    /// the liability on the debtor's.
    /// </summary>
    [Serializable]
    public sealed class InterbankClaim
    {
        public string ClaimId = string.Empty;
        public string DebtorBankId = string.Empty;    // the bank that must pay
        public string CreditorBankId = string.Empty;  // the bank that is owed
        public int AmountCents;
        public InterbankPaperKind PaperKind = InterbankPaperKind.Unspecified;
        public string DrawerAccountId = string.Empty; // checks: the debited account at the debtor; else empty
        public string PayeeName = string.Empty;
        public string PaperReference = string.Empty;
        public int OriginatedDayIndex;
        public int SettledDayIndex = -1;
        public InterbankClaimStatus Status = InterbankClaimStatus.Open;

        public InterbankClaim() { }
    }

    /// <summary>
    /// D4A: the net of all open claims between one pair of banks after a
    /// settlement cycle. Bilateral netting per pair (not a multilateral
    /// clearinghouse): with the handful of banks a town holds, pairwise
    /// netting is transparent and auditable. The position is settled in
    /// full — in specie lots with provenance, or by offset against an
    /// interbank deposit the debtor holds at the creditor. No partials:
    /// a shortfall is refused loudly, never papered over.
    /// </summary>
    [Serializable]
    public sealed class InterbankSettlementPosition
    {
        public string PositionId = string.Empty;
        public string DebtorBankId = string.Empty;
        public string CreditorBankId = string.Empty;
        public int NetAmountCents;
        public List<string> ComprisedClaimIds = new List<string>();
        public int CycleDayIndex;
        public int SettledDayIndex = -1;
        public InterbankPositionStatus Status = InterbankPositionStatus.Open;

        public InterbankSettlementPosition() { }
    }

    /// <summary>
    /// D4A: interbank balances between simulated banks (Canon §16.6, §18.8,
    /// §18.11). Three honest instruments, one hard constraint:
    ///
    /// - Interbank deposit accounts: Bank A holds money at Bank B. B books
    ///   an ordinary deposit LIABILITY (NX-3B doctrine — a bank depositor is
    ///   still a depositor); A carries the ASSET (InterbankDepositAsset).
    ///   Opening, adding, and drawing all move REAL specie lots A&lt;-&gt;B.
    /// - Interbank claims: paper drawn on Bank A presented at Bank B. B
    ///   honors it for the payee (cash or deposit credit); the claim is B's
    ///   asset and A's liability. Checks debit the drawer's account at A at
    ///   presentment — a bounced check is refused then, loudly, and creates
    ///   no claim. Drafts/money orders obligate the issuing bank directly.
    /// - Settlement: periodic bilateral netting into positions, settled in
    ///   full in specie lots (original lots keep their provenance) or by
    ///   offset against the debtor's interbank deposit at the creditor.
    ///
    /// HARD CONSTRAINT (D4A): no money creation anywhere. Every balance
    /// traces to real currency/specie movement or an explicit recorded
    /// claim; settlement moves only cash the debtor actually holds.
    /// Shortfalls are refused loudly — never invented, never advanced.
    ///
    /// The service is the single writer of both sides of every interbank
    /// account. Banks are registered live (their own save DTOs persist
    /// them); this service persists only the interbank instruments.
    /// Settlement-cycle timing (daily/weekly) is the operating layer's
    /// calibration decision — the service nets on demand.
    /// </summary>
    public sealed class InterbankSettlement
    {
        private readonly Dictionary<string, BankRuntime> banks =
            new Dictionary<string, BankRuntime>(StringComparer.Ordinal);
        private readonly Dictionary<string, InterbankDepositAsset> depositAssets =
            new Dictionary<string, InterbankDepositAsset>(StringComparer.Ordinal);
        private readonly Dictionary<string, InterbankClaim> claims =
            new Dictionary<string, InterbankClaim>(StringComparer.Ordinal);
        private readonly Dictionary<string, InterbankSettlementPosition> positions =
            new Dictionary<string, InterbankSettlementPosition>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        private int nextClaimNumber = 1;
        private int nextPositionNumber = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyCollection<InterbankDepositAsset> DepositAssets => depositAssets.Values;
        public IReadOnlyCollection<InterbankClaim> Claims => claims.Values;
        public IReadOnlyCollection<InterbankSettlementPosition> Positions => positions.Values;

        /// <summary>Registers a bank as a participant. Live reference — the bank's own DTOs persist it.</summary>
        public string RegisterBank(BankRuntime bank, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (bank == null) return "InterbankSettlement.RegisterBank: no bank.";
            if (string.IsNullOrWhiteSpace(bank.BusinessInstanceId))
                return "InterbankSettlement.RegisterBank: the bank needs a business instance id.";
            if (banks.ContainsKey(bank.BusinessInstanceId))
                return $"InterbankSettlement.RegisterBank: '{bank.BusinessInstanceId}' is already registered.";
            banks[bank.BusinessInstanceId] = bank;
            diag.Add($"InterbankSettlement: '{bank.BusinessName}' registered for interbank settlement.");
            return null;
        }

        public BankRuntime FindBank(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            banks.TryGetValue(businessInstanceId, out BankRuntime bank);
            return bank;
        }

        private string RequireTwoBanks(string firstId, string secondId, string op,
            out BankRuntime first, out BankRuntime second, List<string> diag)
        {
            first = FindBank(firstId);
            second = FindBank(secondId);
            if (first == null)
                return $"InterbankSettlement.{op}: unknown bank '{firstId}' — register it first.";
            if (second == null)
                return $"InterbankSettlement.{op}: unknown bank '{secondId}' — register it first.";
            if (string.Equals(firstId, secondId, StringComparison.Ordinal))
                return $"InterbankSettlement.{op}: a bank does not settle with itself.";
            return null;
        }

        private DepositAccount FindLiabilityAccount(BankRuntime holding, string accountId)
        {
            foreach (DepositAccount account in holding.Ledger.Accounts)
                if (string.Equals(account.AccountId, accountId, StringComparison.Ordinal))
                    return account;
            return null;
        }

        // ---------------- interbank deposit accounts ----------------

        /// <summary>
        /// Bank A opens an interbank deposit at Bank B. Real money moves:
        /// A's ledger cash out and A's vault lots out; B receives the lots
        /// and books an ordinary deposit liability to A (NX-3B doctrine).
        /// A carries the asset. Refused when A cannot fund it — the account
        /// is never opened on promises.
        /// </summary>
        public string OpenInterbankDeposit(string depositingBankId, string holdingBankId,
            string accountId, DepositKind kind, int amountCents, int dayIndex,
            int termDays = 0, int interestRateBps = 0, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            const string op = "OpenInterbankDeposit";
            string refused = RequireTwoBanks(depositingBankId, holdingBankId, op,
                out BankRuntime depositor, out BankRuntime holder, diag);
            if (refused != null) return refused;
            if (string.IsNullOrWhiteSpace(accountId))
                return $"InterbankSettlement.{op}: an account id is required.";
            if (depositAssets.ContainsKey(accountId))
                return $"InterbankSettlement.{op}: interbank account '{accountId}' already exists.";
            if (FindLiabilityAccount(holder, accountId) != null)
                return $"InterbankSettlement.{op}: '{holder.BusinessName}' already has an account '{accountId}'.";
            if (kind == DepositKind.Unspecified)
                return $"InterbankSettlement.{op}: demand or term must be stated.";
            if (amountCents <= 0)
                return $"InterbankSettlement.{op}: the deposit must be positive.";
            if (kind == DepositKind.Term && termDays <= 0)
                return $"InterbankSettlement.{op}: a term deposit needs a real term.";
            if (depositor.Ledger.CashOnHandCents < amountCents)
                return $"InterbankSettlement.{op}: '{depositor.BusinessName}' holds {depositor.Ledger.CashOnHandCents}c cash against a {amountCents}c deposit — refused, never advanced.";
            if (depositor.VaultSpecieTotalCents() < amountCents)
                return $"InterbankSettlement.{op}: '{depositor.BusinessName}' vault holds {depositor.VaultSpecieTotalCents()}c specie against a {amountCents}c deposit — count the money, do not promise it.";

            refused = depositor.Ledger.RecordOperatingOutflow(amountCents,
                $"interbank deposit at '{holder.BusinessName}', day {dayIndex}", diag);
            if (refused != null) return refused;

            List<SpecieLot> drawn = depositor.DrawVaultLots(amountCents, diag);
            if (drawn == null)
            {
                depositor.Ledger.RecordOperatingInflow(amountCents,
                    $"rollback: interbank deposit at '{holder.BusinessName}' — vault draw failed", diag);
                return $"InterbankSettlement.{op}: the vault could not produce {amountCents}c — the deposit was unwound, nothing moved.";
            }
            foreach (SpecieLot lot in drawn)
            {
                // The coins are the same coins: original lot ids, kinds and
                // sources travel with them — provenance, not paperwork.
                string lotRefused = holder.ReceiveVaultLot(lot, dayIndex, diag);
                if (lotRefused != null)
                {
                    foreach (SpecieLot back in drawn) depositor.ReceiveVaultLot(back, dayIndex, diag);
                    depositor.Ledger.RecordOperatingInflow(amountCents,
                        $"rollback: interbank deposit at '{holder.BusinessName}' — receipt failed", diag);
                    return $"InterbankSettlement.{op}: '{holder.BusinessName}' could not receive the specie — unwound. {lotRefused}";
                }
            }

            DepositAccount liability = holder.Ledger.OpenAccount(accountId,
                $"INTERBANK: {depositor.BusinessName}", kind, amountCents,
                dayIndex, termDays, interestRateBps, diag);
            if (liability == null)
            {
                foreach (SpecieLot back in drawn) depositor.ReceiveVaultLot(back, dayIndex, diag);
                depositor.Ledger.RecordOperatingInflow(amountCents,
                    $"rollback: interbank deposit at '{holder.BusinessName}' — liability account refused", diag);
                return $"InterbankSettlement.{op}: the liability account was refused — unwound, nothing moved (see diagnostics).";
            }

            depositAssets[accountId] = new InterbankDepositAsset
            {
                AssetId = accountId,
                DepositingBankId = depositingBankId,
                HoldingBankId = holdingBankId,
                Kind = kind,
                BalanceCents = amountCents,
                InterestAccruedCents = 0,
                OpenedDayIndex = dayIndex,
                TermDays = termDays,
                MaturityDayIndex = dayIndex + termDays,
                InterestRateBps = interestRateBps,
            };
            diag.Add($"InterbankSettlement: '{depositor.BusinessName}' opened interbank {kind} account '{accountId}' at '{holder.BusinessName}' — " +
                $"{amountCents}c moved in {drawn.Count} specie lots. Asset of the depositor, liability of the holder (Canon §18.8).");
            return null;
        }

        /// <summary>
        /// Bank A adds to its interbank deposit at Bank B — more real specie
        /// moves A-&gt;B, both views rise together.
        /// </summary>
        public string AddToInterbankDeposit(string accountId, int amountCents,
            int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            const string op = "AddToInterbankDeposit";
            if (!depositAssets.TryGetValue(accountId, out InterbankDepositAsset asset))
                return $"InterbankSettlement.{op}: no interbank account '{accountId}'.";
            if (amountCents <= 0)
                return $"InterbankSettlement.{op}: the addition must be positive.";
            BankRuntime depositor = FindBank(asset.DepositingBankId);
            BankRuntime holder = FindBank(asset.HoldingBankId);
            if (depositor == null || holder == null)
                return $"InterbankSettlement.{op}: a party bank of '{accountId}' is no longer registered.";

            if (depositor.Ledger.CashOnHandCents < amountCents)
                return $"InterbankSettlement.{op}: '{depositor.BusinessName}' holds {depositor.Ledger.CashOnHandCents}c cash against +{amountCents}c — refused.";
            if (depositor.VaultSpecieTotalCents() < amountCents)
                return $"InterbankSettlement.{op}: '{depositor.BusinessName}' vault holds {depositor.VaultSpecieTotalCents()}c specie against +{amountCents}c — refused.";

            string refused = depositor.Ledger.RecordOperatingOutflow(amountCents,
                $"added to interbank account '{accountId}' at '{holder.BusinessName}', day {dayIndex}", diag);
            if (refused != null) return refused;

            List<SpecieLot> drawn = depositor.DrawVaultLots(amountCents, diag);
            if (drawn == null)
            {
                depositor.Ledger.RecordOperatingInflow(amountCents,
                    $"rollback: addition to interbank account '{accountId}' — vault draw failed", diag);
                return $"InterbankSettlement.{op}: the vault could not produce {amountCents}c — unwound, nothing moved.";
            }
            foreach (SpecieLot lot in drawn)
            {
                string lotRefused = holder.ReceiveVaultLot(lot, dayIndex, diag);
                if (lotRefused != null)
                {
                    foreach (SpecieLot back in drawn) depositor.ReceiveVaultLot(back, dayIndex, diag);
                    depositor.Ledger.RecordOperatingInflow(amountCents,
                        $"rollback: addition to interbank account '{accountId}' — receipt failed", diag);
                    return $"InterbankSettlement.{op}: receipt failed — unwound. {lotRefused}";
                }
            }

            refused = holder.Ledger.Deposit(accountId, amountCents, dayIndex, diag);
            if (refused != null)
            {
                foreach (SpecieLot back in drawn) depositor.ReceiveVaultLot(back, dayIndex, diag);
                depositor.Ledger.RecordOperatingInflow(amountCents,
                    $"rollback: addition to interbank account '{accountId}' — liability deposit refused", diag);
                return $"InterbankSettlement.{op}: the liability deposit was refused — unwound. {refused}";
            }

            asset.BalanceCents += amountCents;
            diag.Add($"InterbankSettlement: '{accountId}' +{amountCents}c — asset now {asset.BalanceCents}c; '{holder.BusinessName}' owes that much more.");
            return null;
        }

        /// <summary>
        /// Bank A draws down its interbank deposit at Bank B — real specie
        /// moves B-&gt;A, both views fall together. Term deposits honor the
        /// maturity lock: early draws need the operating layer's explicit
        /// acceptance (mirrors the liability account's own rule).
        /// </summary>
        public string DrawFromInterbankDeposit(string accountId, int amountCents,
            int dayIndex, bool earlyTermWithdrawalAccepted, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            const string op = "DrawFromInterbankDeposit";
            if (!depositAssets.TryGetValue(accountId, out InterbankDepositAsset asset))
                return $"InterbankSettlement.{op}: no interbank account '{accountId}'.";
            if (amountCents <= 0)
                return $"InterbankSettlement.{op}: the draw must be positive.";
            if (amountCents > asset.BalanceCents)
                return $"InterbankSettlement.{op}: '{accountId}' holds {asset.BalanceCents}c — cannot draw {amountCents}c.";
            BankRuntime depositor = FindBank(asset.DepositingBankId);
            BankRuntime holder = FindBank(asset.HoldingBankId);
            if (depositor == null || holder == null)
                return $"InterbankSettlement.{op}: a party bank of '{accountId}' is no longer registered.";

            // The liability account's own guards run first: balance, term
            // lock, and the holder's cash on hand. Refused means refused.
            string refused = holder.Ledger.Withdraw(accountId, amountCents, dayIndex,
                earlyTermWithdrawalAccepted, diag);
            if (refused != null) return refused;

            if (holder.VaultSpecieTotalCents() < amountCents)
            {
                // Defensive: the ledger guards cash on hand, and the vault
                // should track it — but never pay what the vault cannot show.
                // Roll the liability withdrawal back by re-depositing.
                holder.Ledger.Deposit(accountId, amountCents, dayIndex, diag);
                diag.Add($"InterbankSettlement: VAULT SHORT at '{holder.BusinessName}' — {amountCents}c draw on '{accountId}' unwound; the liability stands.");
                return $"InterbankSettlement.{op}: '{holder.BusinessName}' vault cannot produce {amountCents}c — draw refused, liability restored.";
            }

            List<SpecieLot> drawn = holder.DrawVaultLots(amountCents, diag);
            if (drawn == null)
            {
                holder.Ledger.Deposit(accountId, amountCents, dayIndex, diag);
                return $"InterbankSettlement.{op}: vault draw failed — the liability withdrawal was unwound, nothing moved.";
            }
            foreach (SpecieLot lot in drawn)
            {
                string lotRefused = depositor.ReceiveVaultLot(lot, dayIndex, diag);
                if (lotRefused != null)
                {
                    foreach (SpecieLot back in drawn) holder.ReceiveVaultLot(back, dayIndex, diag);
                    holder.Ledger.Deposit(accountId, amountCents, dayIndex, diag);
                    return $"InterbankSettlement.{op}: receipt failed — unwound, liability restored. {lotRefused}";
                }
            }
            refused = depositor.Ledger.RecordOperatingInflow(amountCents,
                $"drawn from interbank account '{accountId}' at '{holder.BusinessName}', day {dayIndex}", diag);
            if (refused != null)
            {
                // Cash physically arrived but the books refused it — park the
                // lots back at the holder and restore the liability rather
                // than leave the two halves diverged.
                foreach (SpecieLot back in drawn) holder.ReceiveVaultLot(back, dayIndex, diag);
                holder.Ledger.Deposit(accountId, amountCents, dayIndex, diag);
                return $"InterbankSettlement.{op}: the inflow was refused — specie returned, liability restored. {refused}";
            }

            asset.BalanceCents -= amountCents;
            diag.Add($"InterbankSettlement: '{accountId}' -{amountCents}c drawn home to '{depositor.BusinessName}' — asset now {asset.BalanceCents}c.");
            return null;
        }

        /// <summary>
        /// Asset-view interest accrual is a MIRROR of the liability account:
        /// the holding bank accrues on its own ledger (BankDepositLedger
        /// accrues for all its term accounts); the cycle syncs the asset
        /// view to it. Never accrued independently — the two views cannot
        /// disagree about what is owed.
        /// </summary>
        public void SyncAssetInterestViews(List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            foreach (InterbankDepositAsset asset in depositAssets.Values)
            {
                BankRuntime holder = FindBank(asset.HoldingBankId);
                if (holder == null) continue;
                DepositAccount liability = FindLiabilityAccount(holder, asset.AssetId);
                if (liability == null)
                {
                    diag.Add($"InterbankSettlement: WARNING — asset '{asset.AssetId}' has no matching liability at '{holder.BusinessName}'. The books disagree; investigate, do not edit.");
                    continue;
                }
                if (liability.InterestAccruedCents != asset.InterestAccruedCents)
                {
                    diag.Add($"InterbankSettlement: '{asset.AssetId}' interest view synced {asset.InterestAccruedCents}c -> {liability.InterestAccruedCents}c (liability is truth).");
                    asset.InterestAccruedCents = liability.InterestAccruedCents;
                }
            }
        }

        /// <summary>Total interbank deposits this bank holds at other banks — an asset total.</summary>
        public int InterbankAssetsCents(string bankId)
        {
            int total = 0;
            foreach (InterbankDepositAsset asset in depositAssets.Values)
                if (string.Equals(asset.DepositingBankId, bankId, StringComparison.Ordinal))
                    total += asset.AssetValueCents();
            return total;
        }

        // ---------------- presentment: paper becomes a claim ----------------

        /// <summary>
        /// Paper drawn on the drawee bank is presented at the presenting
        /// bank, which honors it for the payee — and gains an explicit
        /// interbank claim on the drawee for the amount. The presenting
        /// bank paid REAL value, so the claim is a real asset:
        ///
        /// - Payee paid in cash: the presenting bank's ledger cash and vault
        ///   specie leave; the drawn lots are returned for the payee's hand.
        /// - Payee credited: the presenting bank takes on a real deposit
        ///   liability to the payee. No cash arrived — the offsetting asset
        ///   is this claim, so the cash the Deposit call adds is immediately
        ///   backed out (the withdraw/inflow pair is the accounting identity:
        ///   cash never moved, the obligation migrated to the claim).
        ///
        /// Checks debit the drawer's account at the drawee bank AT
        /// PRESENTMENT: the drawee's cash does not move (the withdraw/inflow
        /// pair again), but the drawer's liability falls and the interbank
        /// obligation rises. A check the drawer cannot honor is refused
        /// here — loudly — and creates no claim. Bank drafts and money
        /// orders obligate the issuing bank directly: no customer account is
        /// touched, and naming one is refused.
        /// </summary>
        /// <returns>
        /// Null on success (cash-paid lots are NOT returned here — use the
        /// overload that returns them); else the refusal reason.
        /// </returns>
        public string PresentInterbankPaper(string presentingBankId, string draweeBankId,
            InterbankPaperKind paperKind, string drawerAccountId, string payeeAccountId,
            bool payPayeeInCash, string payeeName, int amountCents,
            string paperReference, int dayIndex, List<string> diag = null)
        {
            List<SpecieLot> ignored = null;
            return PresentInterbankPaper(presentingBankId, draweeBankId, paperKind,
                drawerAccountId, payeeAccountId, payPayeeInCash, payeeName,
                amountCents, paperReference, dayIndex, out ignored, diag);
        }

        /// <summary>
        /// Presentment overload that returns the specie lots the presenting
        /// bank paid out, so the operating layer can hand them to the payee.
        /// Null lots on refusal (reason returned) or on the credited path.
        /// </summary>
        public string PresentInterbankPaper(string presentingBankId, string draweeBankId,
            InterbankPaperKind paperKind, string drawerAccountId, string payeeAccountId,
            bool payPayeeInCash, string payeeName, int amountCents,
            string paperReference, int dayIndex, out List<SpecieLot> cashPaidLots,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            cashPaidLots = null;
            const string op = "PresentInterbankPaper";
            string refused = RequireTwoBanks(presentingBankId, draweeBankId, op,
                out BankRuntime presenter, out BankRuntime drawee, diag);
            if (refused != null) return refused;
            if (paperKind == InterbankPaperKind.Unspecified)
                return $"InterbankSettlement.{op}: the paper kind must be stated — check, draft, or money order.";
            if (amountCents <= 0)
                return $"InterbankSettlement.{op}: the paper amount must be positive.";
            if (string.IsNullOrWhiteSpace(paperReference))
                return $"InterbankSettlement.{op}: the paper must be identified — no anonymous claims.";
            if (string.IsNullOrWhiteSpace(payeeName))
                return $"InterbankSettlement.{op}: the payee must be named.";

            bool isCheck = paperKind == InterbankPaperKind.Check;
            if (isCheck && string.IsNullOrWhiteSpace(drawerAccountId))
                return $"InterbankSettlement.{op}: a check is drawn on a customer account — name the drawer's account at '{drawee.BusinessName}'.";
            if (!isCheck && !string.IsNullOrWhiteSpace(drawerAccountId))
                return $"InterbankSettlement.{op}: a {paperKind} obligates its issuing bank, not a customer account — leave the drawer empty.";
            if (!payPayeeInCash && string.IsNullOrWhiteSpace(payeeAccountId))
                return $"InterbankSettlement.{op}: crediting the payee needs the payee's account at '{presenter.BusinessName}'.";

            // Drawee side first: the obligation must be fundable before the
            // presenting bank pays out a cent on the strength of it.
            if (isCheck)
            {
                refused = drawee.Ledger.Withdraw(drawerAccountId, amountCents, dayIndex, false, diag);
                if (refused != null)
                {
                    diag.Add($"InterbankSettlement: CHECK DISHONORED — '{paperReference}' ({amountCents}c) could not clear against '{drawerAccountId}' at '{drawee.BusinessName}'. No claim created; '{payeeName}' was not paid.");
                    return $"InterbankSettlement.{op}: the check does not clear at '{drawee.BusinessName}' — {refused}";
                }
                // The drawer's liability fell; the interbank obligation rose.
                // Cash never moved at the drawee — the inflow backs the
                // withdraw's cash leg out, labeled as the claim it became.
                refused = drawee.Ledger.RecordOperatingInflow(amountCents,
                    $"interbank claim '{paperReference}' — '{drawerAccountId}' debited, proceeds owed to '{presenter.BusinessName}'", diag);
                if (refused != null)
                {
                    drawee.Ledger.Deposit(drawerAccountId, amountCents, dayIndex, diag);
                    return $"InterbankSettlement.{op}: the claim accounting failed — the drawer debit was unwound. {refused}";
                }
            }

            // Presenting side: real value to the payee, now.
            if (payPayeeInCash)
            {
                if (presenter.VaultSpecieTotalCents() < amountCents)
                {
                    if (isCheck) UnwindDrawerDebit(drawee, drawerAccountId, amountCents, paperReference, presenter, dayIndex, diag);
                    diag.Add($"InterbankSettlement: '{presenter.BusinessName}' cannot honor '{paperReference}' in cash — vault holds {presenter.VaultSpecieTotalCents()}c. Refused, not faked.");
                    return $"InterbankSettlement.{op}: '{presenter.BusinessName}' cannot pay {amountCents}c in cash (vault {presenter.VaultSpecieTotalCents()}c) — refused.";
                }
                refused = presenter.Ledger.RecordOperatingOutflow(amountCents,
                    $"honored {paperKind} '{paperReference}' for '{payeeName}' in cash, day {dayIndex}", diag);
                if (refused != null)
                {
                    if (isCheck) UnwindDrawerDebit(drawee, drawerAccountId, amountCents, paperReference, presenter, dayIndex, diag);
                    return refused;
                }
                List<SpecieLot> drawn = presenter.DrawVaultLots(amountCents, diag);
                if (drawn == null)
                {
                    presenter.Ledger.RecordOperatingInflow(amountCents,
                        $"rollback: honored {paperKind} '{paperReference}' — vault draw failed", diag);
                    if (isCheck) UnwindDrawerDebit(drawee, drawerAccountId, amountCents, paperReference, presenter, dayIndex, diag);
                    return $"InterbankSettlement.{op}: the vault could not produce the cash — unwound, the payee was not paid.";
                }
                cashPaidLots = drawn;
            }
            else
            {
                if (FindLiabilityAccount(presenter, payeeAccountId) == null)
                {
                    if (isCheck) UnwindDrawerDebit(drawee, drawerAccountId, amountCents, paperReference, presenter, dayIndex, diag);
                    return $"InterbankSettlement.{op}: '{presenter.BusinessName}' has no account '{payeeAccountId}' for '{payeeName}' — open it first, then present.";
                }
                refused = presenter.Ledger.Deposit(payeeAccountId, amountCents, dayIndex, diag);
                if (refused != null)
                {
                    if (isCheck) UnwindDrawerDebit(drawee, drawerAccountId, amountCents, paperReference, presenter, dayIndex, diag);
                    return refused;
                }
                // No cash crossed the counter — the claim is the offsetting
                // asset, so the Deposit's cash leg is backed straight out.
                refused = presenter.Ledger.RecordOperatingOutflow(amountCents,
                    $"interbank paper credit '{paperReference}' — offsetting asset is the claim on '{drawee.BusinessName}'", diag);
                if (refused != null)
                {
                    // Defensive: the deposit leg cannot be un-credited
                    // cleanly here; the books and the claim would diverge.
                    // This path should not trigger (amounts are positive).
                    diag.Add($"InterbankSettlement: CRITICAL — '{paperReference}' credited '{payeeAccountId}' but the offsetting outflow failed. Books need a human: {refused}");
                    return $"InterbankSettlement.{op}: claim accounting failed after the credit — halted for review. {refused}";
                }
            }

            var claim = new InterbankClaim
            {
                ClaimId = $"ICLAIM-{nextClaimNumber++:D4}",
                DebtorBankId = draweeBankId,
                CreditorBankId = presentingBankId,
                AmountCents = amountCents,
                PaperKind = paperKind,
                DrawerAccountId = isCheck ? drawerAccountId : string.Empty,
                PayeeName = payeeName,
                PaperReference = paperReference,
                OriginatedDayIndex = dayIndex,
                Status = InterbankClaimStatus.Open,
            };
            claims[claim.ClaimId] = claim;
            diag.Add($"InterbankSettlement: claim '{claim.ClaimId}' — '{drawee.BusinessName}' owes '{presenter.BusinessName}' {amountCents}c " +
                $"({paperKind} '{paperReference}' for '{payeeName}'{(payPayeeInCash ? ", paid in cash" : $", credited to '{payeeAccountId}'")}).");
            return null;
        }

        private void UnwindDrawerDebit(BankRuntime drawee, string drawerAccountId,
            int amountCents, string paperReference, BankRuntime presenter,
            int dayIndex, List<string> diag)
        {
            // Reverse the withdraw/inflow pair: the inflow leg comes off
            // first, then the drawer is re-credited. Order matters — the
            // outflow guard refuses what the bank does not hold.
            drawee.Ledger.RecordOperatingOutflow(amountCents,
                $"unwind: '{paperReference}' not honored by '{presenter.BusinessName}'", diag);
            drawee.Ledger.Deposit(drawerAccountId, amountCents, dayIndex, diag);
            diag.Add($"InterbankSettlement: '{paperReference}' unwound at '{drawee.BusinessName}' — the drawer was not debited for paper nobody honored.");
        }

        /// <summary>Total open + netted claims where this bank is the creditor — interbank assets.</summary>
        public int InterbankClaimsOwedToBankCents(string bankId)
        {
            int total = 0;
            foreach (InterbankClaim claim in claims.Values)
                if (string.Equals(claim.CreditorBankId, bankId, StringComparison.Ordinal) &&
                    (claim.Status == InterbankClaimStatus.Open || claim.Status == InterbankClaimStatus.Netted))
                    total += claim.AmountCents;
            return total;
        }

        /// <summary>Total open + netted claims where this bank is the debtor — interbank liabilities.</summary>
        public int InterbankClaimsOwedByBankCents(string bankId)
        {
            int total = 0;
            foreach (InterbankClaim claim in claims.Values)
                if (string.Equals(claim.DebtorBankId, bankId, StringComparison.Ordinal) &&
                    (claim.Status == InterbankClaimStatus.Open || claim.Status == InterbankClaimStatus.Netted))
                    total += claim.AmountCents;
            return total;
        }

        // ---------------- periodic settlement netting ----------------

        private static string PairKey(string a, string b)
        {
            return string.Compare(a, b, StringComparison.Ordinal) < 0 ? a + "|" + b : b + "|" + a;
        }

        /// <summary>
        /// Periodic settlement: folds every open claim into bilateral net
        /// positions, one per bank pair. Netting moves no cash — it only
        /// computes who owes whom how much. Each resulting position settles
        /// in full afterwards (SettlePositionInSpecie or
        /// OffsetPositionAgainstDeposit). Pairs that net to zero retire
        /// their claims with no position. Asset interest views are synced
        /// first so the cycle sees what is actually owed.
        /// </summary>
        public List<InterbankSettlementPosition> RunSettlementCycle(int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            SyncAssetInterestViews(diag);

            var openByPair = new Dictionary<string, List<InterbankClaim>>(StringComparer.Ordinal);
            foreach (InterbankClaim claim in claims.Values)
            {
                if (claim.Status != InterbankClaimStatus.Open) continue;
                string key = PairKey(claim.DebtorBankId, claim.CreditorBankId);
                if (!openByPair.TryGetValue(key, out List<InterbankClaim> list))
                {
                    list = new List<InterbankClaim>();
                    openByPair[key] = list;
                }
                list.Add(claim);
            }

            var settled = new List<InterbankSettlementPosition>();
            foreach (KeyValuePair<string, List<InterbankClaim>> pair in openByPair)
            {
                List<InterbankClaim> pairClaims = pair.Value;
                // Canonical direction: the lexicographically-first bank is
                // "side A"; net > 0 means A owes B.
                string[] sides = pair.Key.Split('|');
                string sideA = sides[0];
                int netOwedByA = 0;
                foreach (InterbankClaim claim in pairClaims)
                {
                    if (string.Equals(claim.DebtorBankId, sideA, StringComparison.Ordinal))
                        netOwedByA += claim.AmountCents;
                    else
                        netOwedByA -= claim.AmountCents;
                }

                BankRuntime bankA = FindBank(sides[0]);
                BankRuntime bankB = FindBank(sides[1]);
                string nameA = bankA != null ? bankA.BusinessName : sides[0];
                string nameB = bankB != null ? bankB.BusinessName : sides[1];

                var comprised = new List<string>();
                foreach (InterbankClaim claim in pairClaims)
                {
                    claim.Status = InterbankClaimStatus.Netted;
                    comprised.Add(claim.ClaimId);
                }

                if (netOwedByA == 0)
                {
                    diag.Add($"InterbankSettlement: cycle day {dayIndex} — '{nameA}' vs '{nameB}': {pairClaims.Count} claims net to ZERO. Retired with no position.");
                    continue;
                }

                var position = new InterbankSettlementPosition
                {
                    PositionId = $"IPOS-{dayIndex:D4}-{nextPositionNumber++:D4}",
                    DebtorBankId = netOwedByA > 0 ? sideA : sides[1],
                    CreditorBankId = netOwedByA > 0 ? sides[1] : sideA,
                    NetAmountCents = Math.Abs(netOwedByA),
                    ComprisedClaimIds = comprised,
                    CycleDayIndex = dayIndex,
                    Status = InterbankPositionStatus.Open,
                };
                positions[position.PositionId] = position;
                settled.Add(position);
                BankRuntime debtor = FindBank(position.DebtorBankId);
                BankRuntime creditor = FindBank(position.CreditorBankId);
                diag.Add($"InterbankSettlement: cycle day {dayIndex} — position '{position.PositionId}': " +
                    $"'{(debtor != null ? debtor.BusinessName : position.DebtorBankId)}' owes " +
                    $"'{(creditor != null ? creditor.BusinessName : position.CreditorBankId)}' {position.NetAmountCents}c net ({comprised.Count} claims).");
            }
            if (settled.Count == 0)
                diag.Add($"InterbankSettlement: cycle day {dayIndex} — no open claims; nothing to net.");
            return settled;
        }

        // ---------------- settlement in specie ----------------

        /// <summary>
        /// Settles a net position IN FULL in specie: the debtor's vault
        /// produces real lots (oldest first, Tech X §6.1 lot discipline),
        /// the debtor's ledger cash leaves, and the creditor receives the
        /// lots — original lot ids, kinds and sources intact (provenance).
        /// The comprised claims settle with it.
        ///
        /// Refused LOUDLY when the debtor cannot cover the full amount —
        /// the position stands, the claims stay netted, nothing moves.
        /// No partials, no advances, no invented funds.
        /// </summary>
        public string SettlePositionInSpecie(string positionId, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            const string op = "SettlePositionInSpecie";
            if (!positions.TryGetValue(positionId, out InterbankSettlementPosition position))
                return $"InterbankSettlement.{op}: no position '{positionId}'.";
            if (position.Status != InterbankPositionStatus.Open)
                return $"InterbankSettlement.{op}: position '{positionId}' is {position.Status} — already resolved.";
            BankRuntime debtor = FindBank(position.DebtorBankId);
            BankRuntime creditor = FindBank(position.CreditorBankId);
            if (debtor == null || creditor == null)
                return $"InterbankSettlement.{op}: a party bank of '{positionId}' is no longer registered.";

            int amount = position.NetAmountCents;
            if (debtor.Ledger.CashOnHandCents < amount)
            {
                diag.Add($"InterbankSettlement: SETTLEMENT REFUSED — '{debtor.BusinessName}' holds {debtor.Ledger.CashOnHandCents}c cash against position '{positionId}' ({amount}c). The debt stands; the creditor waits.");
                return $"InterbankSettlement.{op}: '{debtor.BusinessName}' cannot cover {amount}c (cash on hand {debtor.Ledger.CashOnHandCents}c) — refused, never faked.";
            }
            if (debtor.VaultSpecieTotalCents() < amount)
            {
                diag.Add($"InterbankSettlement: SETTLEMENT REFUSED — '{debtor.BusinessName}' vault holds {debtor.VaultSpecieTotalCents()}c specie against position '{positionId}' ({amount}c). Count the vault, do not promise it.");
                return $"InterbankSettlement.{op}: '{debtor.BusinessName}' vault cannot produce {amount}c — refused.";
            }

            string refused = debtor.Ledger.RecordOperatingOutflow(amount,
                $"interbank settlement to '{creditor.BusinessName}', position '{positionId}', day {dayIndex}", diag);
            if (refused != null) return refused;

            List<SpecieLot> drawn = debtor.DrawVaultLots(amount, diag);
            if (drawn == null)
            {
                debtor.Ledger.RecordOperatingInflow(amount,
                    $"rollback: interbank settlement position '{positionId}' — vault draw failed", diag);
                return $"InterbankSettlement.{op}: the vault could not produce {amount}c — unwound, nothing moved.";
            }
            foreach (SpecieLot lot in drawn)
            {
                string lotRefused = creditor.ReceiveVaultLot(lot, dayIndex, diag);
                if (lotRefused != null)
                {
                    foreach (SpecieLot back in drawn) debtor.ReceiveVaultLot(back, dayIndex, diag);
                    debtor.Ledger.RecordOperatingInflow(amount,
                        $"rollback: interbank settlement position '{positionId}' — receipt failed", diag);
                    return $"InterbankSettlement.{op}: receipt failed — unwound. {lotRefused}";
                }
            }
            refused = creditor.Ledger.RecordOperatingInflow(amount,
                $"interbank settlement from '{debtor.BusinessName}', position '{positionId}', day {dayIndex}", diag);
            if (refused != null)
            {
                foreach (SpecieLot back in drawn) debtor.ReceiveVaultLot(back, dayIndex, diag);
                debtor.Ledger.RecordOperatingInflow(amount,
                    $"rollback: interbank settlement position '{positionId}' — creditor inflow refused", diag);
                return $"InterbankSettlement.{op}: the creditor could not book the inflow — specie returned, nothing settled. {refused}";
            }

            foreach (string claimId in position.ComprisedClaimIds)
            {
                if (claims.TryGetValue(claimId, out InterbankClaim claim) &&
                    claim.Status == InterbankClaimStatus.Netted)
                {
                    claim.Status = InterbankClaimStatus.Settled;
                    claim.SettledDayIndex = dayIndex;
                }
            }
            position.Status = InterbankPositionStatus.Settled;
            position.SettledDayIndex = dayIndex;
            diag.Add($"InterbankSettlement: position '{positionId}' SETTLED — {amount}c in {drawn.Count} specie lots from '{debtor.BusinessName}' to '{creditor.BusinessName}'. {position.ComprisedClaimIds.Count} claims retired.");
            return null;
        }

        /// <summary>
        /// Settles a net position IN FULL by offset: the debtor holds an
        /// interbank deposit at the creditor, so the deposit and the
        /// position cancel each other. No specie moves — the liability the
        /// creditor owed the debtor simply absorbs the claim the debtor
        /// owed the creditor. The asset view and the liability account fall
        /// together. Requires the deposit to cover the whole position;
        /// term deposits honor the maturity lock (explicit acceptance).
        /// </summary>
        public string OffsetPositionAgainstDeposit(string positionId, string accountId,
            int dayIndex, bool earlyTermWithdrawalAccepted, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            const string op = "OffsetPositionAgainstDeposit";
            if (!positions.TryGetValue(positionId, out InterbankSettlementPosition position))
                return $"InterbankSettlement.{op}: no position '{positionId}'.";
            if (position.Status != InterbankPositionStatus.Open)
                return $"InterbankSettlement.{op}: position '{positionId}' is {position.Status} — already resolved.";
            if (!depositAssets.TryGetValue(accountId, out InterbankDepositAsset asset))
                return $"InterbankSettlement.{op}: no interbank account '{accountId}'.";
            if (!string.Equals(asset.DepositingBankId, position.DebtorBankId, StringComparison.Ordinal) ||
                !string.Equals(asset.HoldingBankId, position.CreditorBankId, StringComparison.Ordinal))
                return $"InterbankSettlement.{op}: '{accountId}' is not the debtor's deposit at the creditor — offset needs the debtor's money at the creditor's bank.";
            if (asset.BalanceCents < position.NetAmountCents)
                return $"InterbankSettlement.{op}: '{accountId}' holds {asset.BalanceCents}c against a {position.NetAmountCents}c position — offset covers the whole position or nothing. Settle the rest in specie first.";

            BankRuntime debtor = FindBank(position.DebtorBankId);
            BankRuntime creditor = FindBank(position.CreditorBankId);
            if (debtor == null || creditor == null)
                return $"InterbankSettlement.{op}: a party bank of '{positionId}' is no longer registered.";

            int amount = position.NetAmountCents;
            // The liability account's own guards run first (balance, term
            // lock, the holder's cash). The withdraw's cash leg is backed
            // straight out: no cash moved in an offset, only obligations.
            string refused = creditor.Ledger.Withdraw(accountId, amount, dayIndex,
                earlyTermWithdrawalAccepted, diag);
            if (refused != null) return refused;
            refused = creditor.Ledger.RecordOperatingInflow(amount,
                $"offset: interbank deposit '{accountId}' applied against settlement position '{positionId}', day {dayIndex}", diag);
            if (refused != null)
            {
                creditor.Ledger.Deposit(accountId, amount, dayIndex, diag);
                return $"InterbankSettlement.{op}: the offset accounting failed — the deposit withdrawal was unwound. {refused}";
            }

            asset.BalanceCents -= amount;
            foreach (string claimId in position.ComprisedClaimIds)
            {
                if (claims.TryGetValue(claimId, out InterbankClaim claim) &&
                    claim.Status == InterbankClaimStatus.Netted)
                {
                    claim.Status = InterbankClaimStatus.Settled;
                    claim.SettledDayIndex = dayIndex;
                }
            }
            position.Status = InterbankPositionStatus.Settled;
            position.SettledDayIndex = dayIndex;
            diag.Add($"InterbankSettlement: position '{positionId}' OFFSET — {amount}c of '{debtor.BusinessName}' deposit '{accountId}' at '{creditor.BusinessName}' canceled the claim. No specie moved; both obligations fell together.");
            return null;
        }

        // ---------------- save / load (inside the owning runtime class) ----------------

        [Serializable]
        public sealed class InterbankSettlementSaveDto
        {
            public int NextClaimNumber = 1;
            public int NextPositionNumber = 1;
            public List<InterbankDepositAsset> DepositAssets = new List<InterbankDepositAsset>();
            public List<InterbankClaim> Claims = new List<InterbankClaim>();
            public List<InterbankSettlementPosition> Positions = new List<InterbankSettlementPosition>();
        }

        /// <summary>
        /// Captures the interbank instruments. Party banks are NOT captured —
        /// they persist through their own DTOs and are re-registered live.
        /// </summary>
        public InterbankSettlementSaveDto ToSaveDto()
        {
            return new InterbankSettlementSaveDto
            {
                NextClaimNumber = Math.Max(1, nextClaimNumber),
                NextPositionNumber = Math.Max(1, nextPositionNumber),
                DepositAssets = new List<InterbankDepositAsset>(depositAssets.Values),
                Claims = new List<InterbankClaim>(claims.Values),
                Positions = new List<InterbankSettlementPosition>(positions.Values),
            };
        }

        public void LoadFromSaveDto(InterbankSettlementSaveDto dto)
        {
            depositAssets.Clear();
            claims.Clear();
            positions.Clear();
            diagnostics.Clear();
            if (dto == null) return;
            nextClaimNumber = Math.Max(1, dto.NextClaimNumber);
            nextPositionNumber = Math.Max(1, dto.NextPositionNumber);
            if (dto.DepositAssets != null)
                foreach (InterbankDepositAsset asset in dto.DepositAssets)
                    if (asset != null && !string.IsNullOrWhiteSpace(asset.AssetId))
                        depositAssets[asset.AssetId] = asset;
            if (dto.Claims != null)
                foreach (InterbankClaim claim in dto.Claims)
                    if (claim != null && !string.IsNullOrWhiteSpace(claim.ClaimId))
                        claims[claim.ClaimId] = claim;
            if (dto.Positions != null)
                foreach (InterbankSettlementPosition position in dto.Positions)
                    if (position != null && !string.IsNullOrWhiteSpace(position.PositionId))
                        positions[position.PositionId] = position;
        }
    }
}
