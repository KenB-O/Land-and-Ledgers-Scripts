using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Economy.Financing;

namespace LandLedgers.Economy.Bank
{
    /// <summary>
    /// W7A: physical cash kinds held in the vault. Descriptive only — value
    /// lives on the lot in cents. Gold dust is real 1870s money in mining
    /// country; it is weighed and valued, never face-valued.
    /// </summary>
    public enum VaultSpecieKind
    {
        Unspecified = 0,
        GoldCoin = 1,
        SilverCoin = 2,
        Greenbacks = 3,
        GoldDust = 4,
    }

    /// <summary>
    /// W7A: one lot of physical vault cash. REAL lots (FVS doctrine): every
    /// cent in the vault traces to a named source and a received day. Cash
    /// is never created — lots only arrive through RecordOpeningCapital /
    /// ReceiveVaultLot and only leave through DrawVaultLots, each move paired
    /// with the matching BankDepositLedger call by the operating layer.
    /// </summary>
    [Serializable]
    public sealed class SpecieLot
    {
        public string LotId = string.Empty;
        public VaultSpecieKind Kind = VaultSpecieKind.Unspecified;
        public string KindName = string.Empty;
        public int AmountCents;
        public string SourceName = string.Empty;
        public int ReceivedDayIndex;

        public SpecieLot() { }
    }

    /// <summary>W7A: one bank staff slot (cashier / teller / clerk).</summary>
    [Serializable]
    public sealed class BankStaffSlot
    {
        public string SlotId = string.Empty;
        public string DisplayName = string.Empty;
        public int BaselineWeeklyWageCents;
        public bool RequiredForOpening;

        public BankStaffSlot() { }

        public BankStaffSlot(string slotId, string displayName, int weeklyWageCents, bool requiredForOpening)
        {
            SlotId = slotId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            BaselineWeeklyWageCents = Math.Max(0, weeklyWageCents);
            RequiredForOpening = requiredForOpening;
        }
    }

    /// <summary>
    /// W7A: bank staff roles. The cashier is the officer in charge of cash
    /// and the books (required for opening); tellers work the windows; the
    /// clerk keeps the ledgers and correspondence. Slot ids match the
    /// WorkerRoleFitEvaluator bank profiles ("cashier", "teller", "clerk").
    /// Wages are calibration (Canon Part XV), not canon.
    /// </summary>
    public static class BankStaffRoles
    {
        public const string CashierSlotId = "cashier";
        public const string TellerSlotId = "teller";
        public const string ClerkSlotId = "clerk";

        public static List<BankStaffSlot> DefaultSlots()
        {
            return new List<BankStaffSlot>
            {
                new BankStaffSlot(CashierSlotId, "Cashier", 1800, true),
                new BankStaffSlot(TellerSlotId, "Teller", 1200, false),
                new BankStaffSlot(ClerkSlotId, "Clerk", 1000, false),
            };
        }
    }

    /// <summary>
    /// W7A: bank premises requirements. A bank needs a banking-house space and
    /// a vault — either a vault room or an installed safe. The building alone
    /// grants nothing (Tech X §3.5): EstablishVault records the actual vault
    /// the BIZ-1 premises step validated.
    /// </summary>
    public sealed class BankPremisesRequirements
    {
        public const string BankingHouseSpaceKind = "banking-house";

        /// <summary>
        /// Returns null when the premises qualify, else the refusal reason.
        /// </summary>
        public string ValidatePremises(string spaceKind, bool hasVaultRoom, bool hasSafe,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!string.Equals(spaceKind, BankingHouseSpaceKind, StringComparison.OrdinalIgnoreCase))
                return $"BankPremisesRequirements: a bank needs a '{BankingHouseSpaceKind}' space, not '{spaceKind}'.";
            if (!hasVaultRoom && !hasSafe)
                return "BankPremisesRequirements: a bank needs a vault — a vault room or an installed safe. A counter alone is not a bank (Tech X §3.5).";
            diagnostics.Add("BankPremisesRequirements: banking-house space with a vault — premises qualify.");
            return null;
        }
    }

    /// <summary>
    /// W7A: the bank as an operating business (BusinessType.Bank = 26).
    ///
    /// Canon §18.8: the bank operates as a business with both assets and
    /// liabilities — deposits are money the bank owes, not free owner cash.
    /// This runtime WRAPS the T2A/NX-3B instruments and never bypasses them:
    /// every deposit, withdrawal, and loan moves through the owned
    /// BankDepositLedger, and every cent of vault cash is a real SpecieLot
    /// with provenance. The bank creates NO money (NX-3B hard constraint):
    /// owner equity is CashOnHand minus DepositsOwed, and lending can only
    /// move cash the ledger actually holds.
    ///
    /// Two-track cash model: the ledger is the accounting truth (cash on hand
    /// vs owed), the vault lots are the physical truth (specie in the safe).
    /// They move together by construction; CheckVaultReconciliation exposes
    /// any gap the next layer (teller balancing, W7B) must explain.
    /// </summary>
    public sealed class BankRuntime
    {
        private string businessInstanceId = string.Empty;
        private string businessName = string.Empty;
        private string ownerName = string.Empty;

        /// <summary>The wrapped T2A/NX-3B deposit instrument. Never bypassed.</summary>
        private readonly BankDepositLedger ledger;

        private readonly List<SpecieLot> vaultLots = new List<SpecieLot>();
        private int nextLotNumber = 1;

        private bool vaultEstablished;
        private string vaultDescription = string.Empty;

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;
        public string BusinessName => businessName ?? string.Empty;
        public string OwnerName => ownerName ?? string.Empty;
        public BankDepositLedger Ledger => ledger;
        public bool VaultEstablished => vaultEstablished;
        public string VaultDescription => vaultDescription ?? string.Empty;
        public IReadOnlyList<SpecieLot> VaultLots => vaultLots;

        /// <summary>Owner equity: what the bank holds minus what it owes.</summary>
        public int OwnerEquityCents() => ledger.CashOnHandCents - ledger.DepositsOwedCents();

        public BankRuntime() : this(string.Empty, string.Empty, string.Empty) { }

        public BankRuntime(string businessInstanceId, string businessName, string ownerName)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.businessName = businessName ?? string.Empty;
            this.ownerName = ownerName ?? string.Empty;
            ledger = new BankDepositLedger(businessName);
        }

        /// <summary>
        /// BIZ-1 premises step: records the actual vault (vault room or safe)
        /// the premises validation established. Refuses a vault-less bank.
        /// </summary>
        public string EstablishVault(string vaultDescription, bool hasVaultRoom, bool hasSafe,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!hasVaultRoom && !hasSafe)
                return "BankRuntime.EstablishVault: a bank needs a vault room or an installed safe — refused.";
            vaultEstablished = true;
            this.vaultDescription = string.IsNullOrWhiteSpace(vaultDescription)
                ? (hasVaultRoom ? "vault room" : "bank safe")
                : vaultDescription;
            diagnostics.Add($"BankRuntime [{businessName}]: vault established ({this.vaultDescription}).");
            return null;
        }

        /// <summary>
        /// BIZ-1 working-capital step: the owner's capital enters as physical
        /// specie lots with a named source. Lots must sum exactly to the
        /// declared amount — capital is counted, not asserted. The ledger
        /// records the inflow as owner capital (no liability created).
        /// </summary>
        public string RecordOpeningCapital(int amountCents, string sourceNote, List<SpecieLot> lots,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!vaultEstablished)
                return "BankRuntime.RecordOpeningCapital: establish the vault first — capital needs a safe to land in.";
            if (amountCents <= 0)
                return "BankRuntime.RecordOpeningCapital: opening capital must be positive.";
            if (string.IsNullOrWhiteSpace(sourceNote))
                return "BankRuntime.RecordOpeningCapital: the capital source must be named — capital is never conjured.";
            if (lots == null || lots.Count == 0)
                return "BankRuntime.RecordOpeningCapital: capital arrives as specie lots, not a bare number.";
            int lotTotal = 0;
            foreach (SpecieLot lot in lots)
            {
                if (lot == null || lot.AmountCents <= 0)
                    return "BankRuntime.RecordOpeningCapital: every lot must be a positive amount of specie.";
                if (string.IsNullOrWhiteSpace(lot.SourceName))
                    return $"BankRuntime.RecordOpeningCapital: lot '{lot.LotId}' has no source — upstream provenance required (FVS doctrine).";
                lotTotal += lot.AmountCents;
            }
            if (lotTotal != amountCents)
                return $"BankRuntime.RecordOpeningCapital: lots sum to {lotTotal}c but {amountCents}c was declared — count the money, do not assert it.";

            string refused = ledger.RecordCapitalInflow(amountCents, sourceNote, diagnostics);
            if (refused != null) return refused;

            foreach (SpecieLot lot in lots)
            {
                if (string.IsNullOrWhiteSpace(lot.LotId))
                    lot.LotId = $"SPECIE-{businessInstanceId}-{nextLotNumber++:D4}";
                if (string.IsNullOrWhiteSpace(lot.KindName))
                    lot.KindName = lot.Kind.ToString();
                lot.ReceivedDayIndex = dayIndex;
                vaultLots.Add(lot);
            }
            diagnostics.Add($"BankRuntime [{businessName}]: opening capital {amountCents}c in {lots.Count} specie lots ({sourceNote}).");
            return null;
        }

        /// <summary>
        /// Physical cash arrives (a depositor's deposit, a loan repayment).
        /// The caller pairs this with the matching ledger call — the lot is
        /// the physical half, the ledger entry is the accounting half.
        /// </summary>
        public string ReceiveVaultLot(SpecieLot lot, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (lot == null || lot.AmountCents <= 0)
                return "BankRuntime.ReceiveVaultLot: a positive specie lot is required.";
            if (string.IsNullOrWhiteSpace(lot.SourceName))
                return "BankRuntime.ReceiveVaultLot: the lot source must be named — no anonymous cash.";
            if (string.IsNullOrWhiteSpace(lot.LotId))
                lot.LotId = $"SPECIE-{businessInstanceId}-{nextLotNumber++:D4}";
            if (string.IsNullOrWhiteSpace(lot.KindName))
                lot.KindName = lot.Kind.ToString();
            lot.ReceivedDayIndex = dayIndex;
            vaultLots.Add(lot);
            diagnostics.Add($"BankRuntime [{businessName}]: vault +{lot.AmountCents}c {lot.KindName} ({lot.LotId}, from {lot.SourceName}).");
            return null;
        }

        /// <summary>
        /// Physical cash leaves (a withdrawal paid, a loan disbursed). Oldest
        /// lots first (Tech X §6.1 lot discipline). Refuses when the vault
        /// cannot cover the amount — cash is never faked. Returns the drawn
        /// lot portions, or null on refusal.
        /// </summary>
        public List<SpecieLot> DrawVaultLots(int amountCents, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (amountCents <= 0)
            {
                diagnostics.Add("BankRuntime.DrawVaultLots: the draw must be positive.");
                return null;
            }
            if (VaultSpecieTotalCents() < amountCents)
            {
                diagnostics.Add($"BankRuntime [{businessName}]: VAULT SHORT — asked {amountCents}c but the vault holds {VaultSpecieTotalCents()}c. Cash is never faked.");
                return null;
            }
            var ordered = new List<SpecieLot>(vaultLots);
            ordered.Sort((a, b) =>
            {
                int day = a.ReceivedDayIndex.CompareTo(b.ReceivedDayIndex);
                return day != 0 ? day : string.Compare(a.LotId, b.LotId, StringComparison.Ordinal);
            });
            var drawn = new List<SpecieLot>();
            int remaining = amountCents;
            foreach (SpecieLot lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(lot.AmountCents, remaining);
                drawn.Add(new SpecieLot
                {
                    LotId = lot.LotId,
                    Kind = lot.Kind,
                    KindName = lot.KindName,
                    AmountCents = take,
                    SourceName = lot.SourceName,
                    ReceivedDayIndex = lot.ReceivedDayIndex,
                });
                lot.AmountCents -= take;
                remaining -= take;
            }
            vaultLots.RemoveAll(l => l.AmountCents <= 0);
            diagnostics.Add($"BankRuntime [{businessName}]: vault -{amountCents}c in {drawn.Count} lot portions — {VaultSpecieTotalCents()}c remain.");
            return drawn;
        }

        public int VaultSpecieTotalCents()
        {
            int total = 0;
            foreach (SpecieLot lot in vaultLots) total += lot.AmountCents;
            return total;
        }

        /// <summary>
        /// Physical count vs accounting truth. Returns the mismatch in cents
        /// (vault total minus ledger cash on hand); 0 means balanced. A
        /// nonzero result is evidence for teller balancing (W7B) — it is
        /// reported, never silently corrected.
        /// </summary>
        public int CheckVaultReconciliation(List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            int mismatch = VaultSpecieTotalCents() - ledger.CashOnHandCents;
            if (mismatch != 0)
                diagnostics.Add($"BankRuntime [{businessName}]: VAULT MISMATCH — physical {VaultSpecieTotalCents()}c vs ledger {ledger.CashOnHandCents}c (off by {mismatch}c). Count it, do not edit it.");
            else
                diagnostics.Add($"BankRuntime [{businessName}]: vault reconciles — {VaultSpecieTotalCents()}c physical = {ledger.CashOnHandCents}c ledger.");
            return mismatch;
        }

        /// <summary>
        /// BIZ-1: the bank becomes openable when it has a named owner, an
        /// established vault, and counted opening capital that reconciles.
        /// Operating status still comes from actual commerce (Canon §3.2) —
        /// this only clears the business to try.
        /// </summary>
        public string ValidateForOpening(List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(ownerName))
                return "BankRuntime.ValidateForOpening: the bank must have a named owner.";
            if (!vaultEstablished)
                return "BankRuntime.ValidateForOpening: no vault — a bank without a safe is not openable.";
            if (vaultLots.Count == 0)
                return "BankRuntime.ValidateForOpening: no opening capital counted into the vault.";
            var quiet = new List<string>();
            int mismatch = CheckVaultReconciliation(quiet);
            if (mismatch != 0)
                return $"BankRuntime.ValidateForOpening: vault does not reconcile (off by {mismatch}c) — count the capital before opening.";
            diagnostics.Add($"BankRuntime [{businessName}]: openable — owner '{ownerName}', vault '{vaultDescription}', capital {VaultSpecieTotalCents()}c counted.");
            return null;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        [Serializable]
        public sealed class BankRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public string BusinessName = string.Empty;
            public string OwnerName = string.Empty;
            public bool VaultEstablished;
            public string VaultDescription = string.Empty;
            public int NextLotNumber = 1;
            public List<SpecieLot> VaultLots = new List<SpecieLot>();
            public BankDepositLedger.BankDepositLedgerSaveDto LedgerState;
        }

        public BankRuntimeSaveDto ToSaveDto()
        {
            return new BankRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                BusinessName = businessName,
                OwnerName = ownerName,
                VaultEstablished = vaultEstablished,
                VaultDescription = vaultDescription,
                NextLotNumber = Math.Max(1, nextLotNumber),
                VaultLots = new List<SpecieLot>(vaultLots),
                LedgerState = ledger.CaptureSaveDto(),
            };
        }

        public void LoadFromSaveDto(BankRuntimeSaveDto dto)
        {
            if (dto == null) return;
            businessInstanceId = dto.BusinessInstanceId ?? string.Empty;
            businessName = dto.BusinessName ?? string.Empty;
            ownerName = dto.OwnerName ?? string.Empty;
            vaultEstablished = dto.VaultEstablished;
            vaultDescription = dto.VaultDescription ?? string.Empty;
            nextLotNumber = Math.Max(1, dto.NextLotNumber);
            vaultLots.Clear();
            if (dto.VaultLots != null)
                foreach (SpecieLot lot in dto.VaultLots)
                    if (lot != null && lot.AmountCents > 0)
                        vaultLots.Add(lot);
            ledger.LoadFromSaveDto(dto.LedgerState);
        }
    }
}
