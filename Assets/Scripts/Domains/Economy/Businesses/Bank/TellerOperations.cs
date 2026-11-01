using System;
using System.Collections.Generic;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Skills;

namespace LandLedgers.Economy.Bank
{
    /// <summary>
    /// W7B: one teller window — a workstation with throughput. The window is
    /// where the bank's daily commerce happens: deposits taken on as
    /// liabilities, withdrawals paid out. Capability comes from the actual
    /// counter + cash drawer + scale (Tech X §3.5), worked by a named teller.
    /// Daily capacity is calibration (Canon Part XV), not canon.
    /// </summary>
    [Serializable]
    public sealed class TellerWindow
    {
        public string WindowId = string.Empty;
        public string DisplayName = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public string TellerName = string.Empty;
        public EntityId TellerPersonId = EntityId.Invalid;
        public bool IsOpen;
        public bool StationReady;
        public bool StationDerivedFromComponents;

        /// <summary>Calibration: customers served per window per day. Tuning, not canon.</summary>
        public int DailyCapacity = 40;
        public int ServedToday;
        public int ReceivedTodayCents;
        public int PaidTodayCents;

        public TellerWindow() { }

        public TellerWindow(string windowId, string businessInstanceId)
        {
            WindowId = windowId ?? string.Empty;
            BusinessInstanceId = businessInstanceId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(windowId) ? "Teller window" : $"Teller window {windowId}";
        }

        /// <summary>
        /// Derives window readiness from actual components against the
        /// WorkstationCatalog teller-window definition (Tech X §3.5). A
        /// banking-house room alone never grants the window.
        /// </summary>
        public string EstablishFromComponents(List<EquipmentAsset> assets, string spaceId,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var def = WorkstationCatalog.TellerWindowStation;
            var station = new WorkstationInstance
            {
                InstanceId = "teller-window-" + WindowId,
                WorkstationId = def.WorkstationId,
                BusinessInstanceId = BusinessInstanceId,
                SpaceId = spaceId ?? string.Empty,
            };
            var byId = new Dictionary<string, EquipmentAsset>(StringComparer.Ordinal);
            if (assets != null)
            {
                foreach (EquipmentAsset asset in assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) continue;
                    station.InstallComponent(asset.AssetId);
                    byId[asset.AssetId] = asset;
                }
            }
            string reason = station.EvaluateReady(def, id =>
            {
                if (!byId.TryGetValue(id, out EquipmentAsset asset) || asset == null)
                    return (WorkstationComponentView?)null;
                return new WorkstationComponentView
                {
                    AssetId = asset.AssetId,
                    Kind = asset.Kind,
                    Condition01 = asset.Condition01,
                    IsUsable = asset.IsUsable,
                };
            }, diagnostics);
            StationReady = reason == null;
            StationDerivedFromComponents = true;
            return reason;
        }

        public void ResetDay()
        {
            ServedToday = 0;
            ReceivedTodayCents = 0;
            PaidTodayCents = 0;
        }
    }

    /// <summary>
    /// W7B: teller operations for a bank business. Every flow moves through
    /// the wrapped BankDepositLedger (T2A/NX-3B instrument) with the vault
    /// lots as the physical half:
    ///
    /// - TakeDeposit: cash across the counter becomes a deposit LIABILITY
    ///   (Canon §18.8). The lot is received first; if the ledger refuses the
    ///   deposit, the lot is voided and the cash handed back — the two halves
    ///   never diverge silently.
    /// - PayWithdrawal: the ledger refuses when the bank lacks the cash, and
    ///   the vault pre-check refuses when the specie is not there — a refused
    ///   withdrawal moves nothing and is never faked (Canon §18.10).
    /// - EndOfDayBalancing: the vault is counted against the ledger. A
    ///   shortage is booked as a REAL loss (Canon §18.12); an overage is
    ///   reported unresolved — unexplained cash is not income.
    /// </summary>
    public sealed class TellerOperations
    {
        /// <summary>TTS-3 extension-path skill.</summary>
        public const string TellerWorkSkillId = "teller-work";

        private readonly BankRuntime bank;
        private readonly Dictionary<string, TellerWindow> windows =
            new Dictionary<string, TellerWindow>(StringComparer.Ordinal);

        public BankRuntime Bank => bank;
        public IReadOnlyDictionary<string, TellerWindow> Windows => windows;

        public TellerOperations(BankRuntime bank)
        {
            this.bank = bank;
        }

        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("TellerOperations: no SkillService — teller-work skill not registered.");
                return;
            }
            if (skillService.GetSkill(TellerWorkSkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(TellerWorkSkillId, "Teller Work",
                        "Counter work: taking deposits, paying withdrawals, counting cash. (TTS-3 extension path.)"),
                    out rejection))
                {
                    diagnostics.Add($"TellerOperations: skill '{TellerWorkSkillId}' rejected: {rejection}");
                }
            }
        }

        public TellerWindow GetOrCreateWindow(string windowId)
        {
            if (bank == null || string.IsNullOrWhiteSpace(windowId)) return null;
            if (!windows.TryGetValue(windowId, out TellerWindow window))
            {
                window = new TellerWindow(windowId, bank.BusinessInstanceId);
                windows[windowId] = window;
            }
            return window;
        }

        /// <summary>Opens a window for the day under a named teller.</summary>
        public string OpenWindow(string windowId, string tellerName, EntityId tellerPersonId,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null) return "TellerOperations.OpenWindow: no bank.";
            TellerWindow window = GetOrCreateWindow(windowId);
            if (window == null) return "TellerOperations.OpenWindow: a window id is required.";
            if (!window.StationReady)
                return $"TellerOperations.OpenWindow: window '{windowId}' has no ready teller station — the room alone grants nothing (Tech X §3.5).";
            if (string.IsNullOrWhiteSpace(tellerName))
                return "TellerOperations.OpenWindow: the teller must be named — no anonymous hands in the drawer.";
            if (window.IsOpen)
                return $"TellerOperations.OpenWindow: window '{windowId}' is already open.";
            window.TellerName = tellerName;
            window.TellerPersonId = tellerPersonId;
            window.IsOpen = true;
            window.ResetDay();
            diagnostics.Add($"TellerOperations [{bank.BusinessName}]: window '{windowId}' open under {tellerName} (day {dayIndex}).");
            return null;
        }

        public string CloseWindow(string windowId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!windows.TryGetValue(windowId, out TellerWindow window) || !window.IsOpen)
                return $"TellerOperations.CloseWindow: window '{windowId}' is not open.";
            window.IsOpen = false;
            diagnostics.Add($"TellerOperations: window '{windowId}' closed — served {window.ServedToday}, +{window.ReceivedTodayCents}c in, -{window.PaidTodayCents}c out.");
            return null;
        }

        private TellerWindow RequireServingWindow(string windowId, List<string> diagnostics)
        {
            if (!windows.TryGetValue(windowId, out TellerWindow window) || !window.IsOpen)
            {
                diagnostics.Add($"TellerOperations: window '{windowId}' is not open — no counter business without an open window.");
                return null;
            }
            if (!window.StationReady)
            {
                diagnostics.Add($"TellerOperations: window '{windowId}' lost its station — refused.");
                return null;
            }
            if (window.ServedToday >= window.DailyCapacity)
            {
                diagnostics.Add($"TellerOperations: window '{windowId}' is at daily capacity ({window.DailyCapacity}) — the customer waits for tomorrow.");
                return null;
            }
            return window;
        }

        private bool AccountExists(string accountId)
        {
            if (bank == null || string.IsNullOrWhiteSpace(accountId)) return false;
            foreach (DepositAccount account in bank.Ledger.Accounts)
                if (string.Equals(account.AccountId, accountId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Takes a deposit across an open window. The specie lot is the
        /// physical half; the ledger entry is the accounting half. If the
        /// ledger refuses, the lot is voided and the cash handed back.
        /// </summary>
        public string TakeDeposit(string windowId, string accountId, string depositorName,
            DepositKind kind, SpecieLot cashLot, int dayIndex,
            int termDays = 0, int interestRateBps = 0, List<string> diagnostics = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null) return "TellerOperations.TakeDeposit: no bank.";
            TellerWindow window = RequireServingWindow(windowId, diagnostics);
            if (window == null) return $"TellerOperations.TakeDeposit: window '{windowId}' cannot serve.";

            string lotRefusal = bank.ReceiveVaultLot(cashLot, dayIndex, diagnostics);
            if (lotRefusal != null) return lotRefusal;

            if (!AccountExists(accountId))
            {
                DepositAccount account = bank.Ledger.OpenAccount(
                    accountId, depositorName, kind, cashLot.AmountCents,
                    dayIndex, termDays, interestRateBps, diagnostics);
                if (account == null)
                {
                    bank.VoidVaultLot(cashLot.LotId, diagnostics);
                    return "TellerOperations.TakeDeposit: the deposit was refused — the cash was handed back (see ledger diagnostics).";
                }
                diagnostics.Add($"TellerOperations [{bank.BusinessName}]: new {kind} account '{accountId}' for '{depositorName}' — {cashLot.AmountCents}c taken on as a liability.");
            }
            else
            {
                string refusal = bank.Ledger.Deposit(accountId, cashLot.AmountCents, dayIndex, diagnostics);
                if (refusal != null)
                {
                    bank.VoidVaultLot(cashLot.LotId, diagnostics);
                    return refusal;
                }
            }

            window.ServedToday++;
            window.ReceivedTodayCents += cashLot.AmountCents;
            return null;
        }

        /// <summary>
        /// Pays a withdrawal across an open window. Refused when the account
        /// cannot cover it, when the term locks it, or when the bank lacks
        /// the cash — loudly, never faked (Canon §18.10). Returns the specie
        /// lots the customer walks away with, or null on refusal (reason in
        /// diagnostics).
        /// </summary>
        public List<SpecieLot> PayWithdrawal(string windowId, string accountId, int amountCents,
            int dayIndex, bool earlyTermWithdrawalAccepted, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null)
            {
                diagnostics.Add("TellerOperations.PayWithdrawal: no bank.");
                return null;
            }
            TellerWindow window = RequireServingWindow(windowId, diagnostics);
            if (window == null) return null;
            if (amountCents <= 0)
            {
                diagnostics.Add("TellerOperations.PayWithdrawal: the withdrawal must be positive.");
                return null;
            }
            if (bank.VaultSpecieTotalCents() < amountCents)
            {
                diagnostics.Add($"TellerOperations [{bank.BusinessName}]: WITHDRAWAL REFUSED at the window — the vault holds {bank.VaultSpecieTotalCents()}c against a {amountCents}c demand. Refused, not faked (Canon §18.10).");
                return null;
            }

            string refusal = bank.Ledger.Withdraw(accountId, amountCents, dayIndex,
                earlyTermWithdrawalAccepted, diagnostics);
            if (refusal != null) return null;

            List<SpecieLot> paid = bank.DrawVaultLots(amountCents, diagnostics);
            if (paid == null)
            {
                diagnostics.Add($"TellerOperations [{bank.BusinessName}]: LEDGER/VAULT DIVERGENCE — the ledger paid {amountCents}c the vault cannot produce. Count the vault now.");
                return null;
            }

            window.ServedToday++;
            window.PaidTodayCents += amountCents;
            diagnostics.Add($"TellerOperations [{bank.BusinessName}]: window '{windowId}' paid {amountCents}c to '{accountId}'.");
            return paid;
        }

        /// <summary>
        /// End of day: every open window reports its tallies, the vault is
        /// counted against the ledger, shortages are booked as real losses,
        /// overages are reported unresolved (unexplained cash is not income),
        /// and window tallies reset for tomorrow.
        /// </summary>
        public string EndOfDayBalancing(int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null) return "TellerOperations.EndOfDayBalancing: no bank.";

            foreach (TellerWindow window in windows.Values)
            {
                if (!window.IsOpen) continue;
                diagnostics.Add($"TellerOperations [{bank.BusinessName}]: window '{window.WindowId}' ({window.TellerName}) — served {window.ServedToday}, +{window.ReceivedTodayCents}c in, -{window.PaidTodayCents}c out.");
            }

            int mismatch = bank.CheckVaultReconciliation(diagnostics);
            if (mismatch < 0)
            {
                string refusal = bank.Ledger.RecordCashShortage(-mismatch,
                    $"end-of-day count, day {dayIndex}", diagnostics);
                if (refusal != null)
                    diagnostics.Add($"TellerOperations [{bank.BusinessName}]: could not book the shortage — {refusal}");
                else
                    diagnostics.Add($"TellerOperations [{bank.BusinessName}]: shortage of {-mismatch}c booked as a real loss — books balance, equity is poorer.");
            }
            else if (mismatch > 0)
            {
                diagnostics.Add($"TellerOperations [{bank.BusinessName}]: OVERAGE of {mismatch}c — physical over ledger. NOT booked: unexplained cash is not income. Identify the source, then record it properly.");
            }
            else
            {
                diagnostics.Add($"TellerOperations [{bank.BusinessName}]: day {dayIndex} balances clean.");
            }

            foreach (TellerWindow window in windows.Values)
                window.ResetDay();
            return null;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        [Serializable]
        public sealed class TellerWindowState
        {
            public string WindowId = string.Empty;
            public string DisplayName = string.Empty;
            public string TellerName = string.Empty;
            public EntityId TellerPersonId = EntityId.Invalid;
            public bool IsOpen;
            public bool StationReady;
            public bool StationDerivedFromComponents;
            public int DailyCapacity = 40;
            public int ServedToday;
            public int ReceivedTodayCents;
            public int PaidTodayCents;
        }

        [Serializable]
        public sealed class TellerOperationsSaveDto
        {
            public List<TellerWindowState> Windows = new List<TellerWindowState>();
        }

        public TellerOperationsSaveDto ToSaveDto()
        {
            var dto = new TellerOperationsSaveDto();
            foreach (TellerWindow window in windows.Values)
            {
                dto.Windows.Add(new TellerWindowState
                {
                    WindowId = window.WindowId,
                    DisplayName = window.DisplayName,
                    TellerName = window.TellerName,
                    TellerPersonId = window.TellerPersonId,
                    IsOpen = window.IsOpen,
                    StationReady = window.StationReady,
                    StationDerivedFromComponents = window.StationDerivedFromComponents,
                    DailyCapacity = window.DailyCapacity,
                    ServedToday = window.ServedToday,
                    ReceivedTodayCents = window.ReceivedTodayCents,
                    PaidTodayCents = window.PaidTodayCents,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(TellerOperationsSaveDto dto)
        {
            windows.Clear();
            if (dto == null || bank == null) return;
            foreach (TellerWindowState state in dto.Windows)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.WindowId)) continue;
                var window = new TellerWindow(state.WindowId, bank.BusinessInstanceId)
                {
                    DisplayName = state.DisplayName,
                    TellerName = state.TellerName,
                    TellerPersonId = state.TellerPersonId,
                    IsOpen = state.IsOpen,
                    StationReady = state.StationReady,
                    StationDerivedFromComponents = state.StationDerivedFromComponents,
                    DailyCapacity = Math.Max(1, state.DailyCapacity),
                    ServedToday = Math.Max(0, state.ServedToday),
                    ReceivedTodayCents = Math.Max(0, state.ReceivedTodayCents),
                    PaidTodayCents = Math.Max(0, state.PaidTodayCents),
                };
                windows[window.WindowId] = window;
            }
        }
    }
}
