// Land & Ledgers — Economy and Inventory Integrity Auditor (Tool 02, Lane 3).
//
// Conservation checks over money, goods and ownership. This class OBSERVES
// the authoritative systems (BusinessRuntimeState, GeneralStoreRuntimeManager,
// BakeryShopRuntime, FuelDealerByproductSettlement, FreightServiceAgreements,
// StoreProcurementChain, SharedBusinessRuntimeManager, WageSettlementService,
// RetailUnitConversions, GeneralStoreServiceClerkPolicy) and never becomes a
// second ledger: it holds no balances, moves no cash, and writes no stock.
// Every check takes plain record structs (live snapshots or source text) and
// returns findings that name the owning authority and the evidence.
//
// Two input shapes:
//   - Model checks: pure functions over caller-supplied records. Tests feed
//     them fixture records; the Editor window feeds them live snapshots.
//   - Source checks: pure functions over SourceTextFile (relative path +
//     file text). Tests feed fixture text; the Editor window reads the repo
//     tree from disk and passes the real files in.
//
// Evidence taxonomy on every finding in this file: source-reviewed only.
// Nothing here is Unity-compiled or live-verified; the Roslyn harness is
// the only verification this pass can perform (see HARNESS_NOTES.md).

using System;
using System.Collections.Generic;
using System.Linq;

namespace LandLedgers.Diagnostics
{
    public enum EconomyFindingSeverity
    {
        Information,
        Warning,
        Critical,
        Unknown
    }

    public enum EconomyFindingStatus
    {
        /// <summary>The mechanism is confirmed in source at this HEAD.</summary>
        Confirmed,
        /// <summary>The mechanism exists in source but cannot fire in the dev town today.</summary>
        Latent,
        /// <summary>Confirmed open: no compensating path or ruling exists in source.</summary>
        Open,
        /// <summary>The behaviour follows from a config value, not from missing code.</summary>
        ConfigValue
    }

    /// <summary>One auditor finding. Never a balance, never a ledger entry.</summary>
    public sealed class EconomyIntegrityFinding
    {
        public EconomyIntegrityFinding(
            string id, string check, string title,
            EconomyFindingSeverity severity, EconomyFindingStatus status,
            string authority, string evidence)
        {
            Id = id;
            Check = check;
            Title = title;
            Severity = severity;
            Status = status;
            Authority = authority;
            Evidence = evidence;
        }

        public string Id { get; }
        public string Check { get; }
        public string Title { get; }
        public EconomyFindingSeverity Severity { get; }
        public EconomyFindingStatus Status { get; }
        public string Authority { get; }
        public string Evidence { get; }
    }

    /// <summary>One source file as read from disk (Editor) or a fixture (tests).</summary>
    public sealed class EconomySourceTextFile
    {
        public EconomySourceTextFile(string relativePath, string text)
        {
            RelativePath = relativePath ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string RelativePath { get; }
        public string Text { get; }
    }

    /// <summary>Per-file cash-writer inventory (E-01): the enumerated method-call writer list.</summary>
    public sealed class CashWriterInventory
    {
        public CashWriterInventory(IReadOnlyDictionary<string, int> writersByFile, int totalCallSites)
        {
            WritersByFile = writersByFile;
            TotalCallSites = totalCallSites;
        }

        /// <summary>Relative path -> call-site count, most writers first.</summary>
        public IReadOnlyDictionary<string, int> WritersByFile { get; }

        public int TotalCallSites { get; }
        public int FileCount => WritersByFile.Count;
    }

    /// <summary>One row of the lot/pool map (E-10): which stock form each subsystem uses.</summary>
    public sealed class LotPoolMapEntry
    {
        public LotPoolMapEntry(string subsystem, string stockForm, string status, string evidence)
        {
            Subsystem = subsystem;
            StockForm = stockForm;
            Status = status;
            Evidence = evidence;
        }

        public string Subsystem { get; }
        public string StockForm { get; }
        public string Status { get; }
        public string Evidence { get; }
    }

    public sealed class EconomyIntegrityReport
    {
        public EconomyIntegrityReport(
            List<EconomyIntegrityFinding> findings,
            CashWriterInventory cashWriterInventory,
            IReadOnlyList<LotPoolMapEntry> lotPoolMap)
        {
            Findings = findings;
            CashWriterInventory = cashWriterInventory;
            LotPoolMap = lotPoolMap;
        }

        public List<EconomyIntegrityFinding> Findings { get; }
        public CashWriterInventory CashWriterInventory { get; }
        public IReadOnlyList<LotPoolMapEntry> LotPoolMap { get; }

        public int CriticalCount => Findings.Count(f => f.Severity == EconomyFindingSeverity.Critical);
        public int WarningCount => Findings.Count(f => f.Severity == EconomyFindingSeverity.Warning);
    }

    // --- Input records -------------------------------------------------------

    /// <summary>One cash settlement posting. A second posting for the same
    /// business + period + kind is a double settlement (E-02 / DEF-067).</summary>
    public sealed class SettlementRecord
    {
        public SettlementRecord(string businessId, string periodKey, string kind, int amountCents, string source)
        {
            BusinessId = businessId ?? string.Empty;
            PeriodKey = periodKey ?? string.Empty;
            Kind = kind ?? string.Empty;
            AmountCents = amountCents;
            Source = source ?? string.Empty;
        }

        public string BusinessId { get; }
        public string PeriodKey { get; }
        public string Kind { get; }
        public int AmountCents { get; }
        public string Source { get; }
    }

    /// <summary>One custody-then-settle transfer, e.g. sawmill byproduct to
    /// the fuel dealer (W SF-7). Custody and payment are one transaction;
    /// custody moved without settlement and without compensation is a
    /// conservation violation.</summary>
    public sealed class CustodyTransferRecord
    {
        public CustodyTransferRecord(
            string transferId, int unitsMoved, bool custodyMoved, bool settled,
            int priceCents, bool compensated, string refusal)
        {
            TransferId = transferId ?? string.Empty;
            UnitsMoved = unitsMoved;
            CustodyMoved = custodyMoved;
            Settled = settled;
            PriceCents = priceCents;
            Compensated = compensated;
            Refusal = refusal ?? string.Empty;
        }

        public string TransferId { get; }
        public int UnitsMoved { get; }
        public bool CustodyMoved { get; }
        public bool Settled { get; }
        public int PriceCents { get; }
        public bool Compensated { get; }
        public string Refusal { get; }
    }

    /// <summary>One identified lot's custody state. F-02: a lot committed to
    /// a refused/cancelled agreement with no disposition and no un-commit
    /// path is an orphan.</summary>
    public sealed class CustodyLotRecord
    {
        public CustodyLotRecord(string lotId, string custodianId, string ownerId, string agreementState, bool hasDisposition)
        {
            LotId = lotId ?? string.Empty;
            CustodianId = custodianId ?? string.Empty;
            OwnerId = ownerId ?? string.Empty;
            AgreementState = agreementState ?? string.Empty;
            HasDisposition = hasDisposition;
        }

        public string LotId { get; }
        public string CustodianId { get; }
        public string OwnerId { get; }
        public string AgreementState { get; }
        public bool HasDisposition { get; }
    }

    /// <summary>One category's stock as seen by a sale: the category pool
    /// and the identified lots that back it. F1: a pool-only sale against a
    /// category with identified lots freezes those lots.</summary>
    public sealed class CategoryStockRecord
    {
        public CategoryStockRecord(
            string categoryId, int poolUnits, int identifiedLotUnits,
            bool poolAndLotsBothTracked, int saleUnits, int lotsTouchedBySale,
            bool legacyPathLive)
        {
            CategoryId = categoryId ?? string.Empty;
            PoolUnits = poolUnits;
            IdentifiedLotUnits = identifiedLotUnits;
            PoolAndLotsBothTracked = poolAndLotsBothTracked;
            SaleUnits = saleUnits;
            LotsTouchedBySale = lotsTouchedBySale;
            LegacyPathLive = legacyPathLive;
        }

        public string CategoryId { get; }
        public int PoolUnits { get; }
        public int IdentifiedLotUnits { get; }
        public bool PoolAndLotsBothTracked { get; }
        public int SaleUnits { get; }
        public int LotsTouchedBySale { get; }
        public bool LegacyPathLive { get; }
    }

    /// <summary>One production run and the inputs it consumed.</summary>
    public sealed class ProductionRecord
    {
        public ProductionRecord(string label, int outputUnits, string requiredInputKind, int inputUnitsRequired, int inputUnitsConsumed)
        {
            Label = label ?? string.Empty;
            OutputUnits = outputUnits;
            RequiredInputKind = requiredInputKind ?? string.Empty;
            InputUnitsRequired = inputUnitsRequired;
            InputUnitsConsumed = inputUnitsConsumed;
        }

        public string Label { get; }
        public int OutputUnits { get; }
        public string RequiredInputKind { get; }
        public int InputUnitsRequired { get; }
        public int InputUnitsConsumed { get; }
    }

    /// <summary>One goods transfer and the payment for it (E-05: lumber at 32c).</summary>
    public sealed class TransferPaymentRecord
    {
        public TransferPaymentRecord(string transferId, int goodsUnits, int unitPriceCents, int paidCents, string payerId, string payeeId)
        {
            TransferId = transferId ?? string.Empty;
            GoodsUnits = goodsUnits;
            UnitPriceCents = unitPriceCents;
            PaidCents = paidCents;
            PayerId = payerId ?? string.Empty;
            PayeeId = payeeId ?? string.Empty;
        }

        public string TransferId { get; }
        public int GoodsUnits { get; }
        public int UnitPriceCents { get; }
        public int PaidCents { get; }
        public string PayerId { get; }
        public string PayeeId { get; }
    }

    /// <summary>One shipment / outstanding order (E-18).</summary>
    public sealed class ShipmentRecord
    {
        public ShipmentRecord(
            string shipmentId, int createdDayIndex, int currentDayIndex,
            int plannedUnits, int deliveredUnits, bool isComplete,
            bool hasEtaField, bool hasOverdueRule)
        {
            ShipmentId = shipmentId ?? string.Empty;
            CreatedDayIndex = createdDayIndex;
            CurrentDayIndex = currentDayIndex;
            PlannedUnits = plannedUnits;
            DeliveredUnits = deliveredUnits;
            IsComplete = isComplete;
            HasEtaField = hasEtaField;
            HasOverdueRule = hasOverdueRule;
        }

        public string ShipmentId { get; }
        public int CreatedDayIndex { get; }
        public int CurrentDayIndex { get; }
        public int PlannedUnits { get; }
        public int DeliveredUnits { get; }
        public bool IsComplete { get; }
        public bool HasEtaField { get; }
        public bool HasOverdueRule { get; }
    }

    /// <summary>One supplier's cash ledger (E-19).</summary>
    public sealed class SupplierLedgerRecord
    {
        public SupplierLedgerRecord(string supplierId, int totalCreditsCents, int totalDebitsCents, bool hasDebitMethod)
        {
            SupplierId = supplierId ?? string.Empty;
            TotalCreditsCents = totalCreditsCents;
            TotalDebitsCents = totalDebitsCents;
            HasDebitMethod = hasDebitMethod;
        }

        public string SupplierId { get; }
        public int TotalCreditsCents { get; }
        public int TotalDebitsCents { get; }
        public bool HasDebitMethod { get; }
    }

    /// <summary>One business's opening cash as authored in config, profile and code default (E-20).</summary>
    public sealed class OpeningCashRecord
    {
        public OpeningCashRecord(string label, int configCents, int profileCents, int codeDefaultCents)
        {
            Label = label ?? string.Empty;
            ConfigCents = configCents;
            ProfileCents = profileCents;
            CodeDefaultCents = codeDefaultCents;
        }

        public string Label { get; }
        public int ConfigCents { get; }
        public int ProfileCents { get; }
        public int CodeDefaultCents { get; }
    }

    /// <summary>The auditor. All methods are pure: no statics read, no cash moved, no stock written.</summary>
    public static class EconomyIntegrityAudit
    {
        /// <summary>BusinessRuntimeState methods through which cash moves.
        /// Direct CurrentCashCents = assignments do not exist in production
        /// (E-01, corrected): the backing field is mutated only inside
        /// BusinessRuntimeState, and these five methods are the call surface.</summary>
        public static readonly string[] CashMutatingMethods =
        {
            "AddCashCents",
            "SetCurrentCashCents",
            "AdjustCashCents",
            "RecordWeeklyCashTransferIn",
            "RecordWeeklyCashTransferOut"
        };

        /// <summary>Diagnostic-only overdue threshold for shipments. This is
        /// an auditor threshold, not a game rule: no ETA/overdue field exists
        /// on LogisticsShipmentState (E-18), so the auditor can only flag age,
        /// never a missed deadline.</summary>
        public const int ShipmentOverdueDiagnosticAgeDays = 30;

        // --- E-01: cash writer inventory --------------------------------------

        /// <summary>Enumerates the method-call cash writer list over the
        /// supplied production source files: per-file call-site counts for
        /// the five BusinessRuntimeState cash methods. Comment lines and
        /// method definitions are not call sites. Also flags any direct
        /// CurrentCashCents = assignment and any backing-field
        /// (currentCashCents =) write outside BusinessRuntimeState.cs.</summary>
        public static CashWriterInventory AuditCashWriters(
            IEnumerable<EconomySourceTextFile> productionFiles,
            List<EconomyIntegrityFinding> findings)
        {
            var writers = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            if (productionFiles != null)
            {
                foreach (EconomySourceTextFile file in productionFiles)
                {
                    if (file == null || string.IsNullOrWhiteSpace(file.Text))
                        continue;

                    int fileCount = 0;
                    bool isRuntimeState = file.RelativePath.EndsWith(
                        "BusinessRuntimeState.cs", StringComparison.Ordinal);
                    string[] lines = file.Text.Split('\n');
                    // Constructor bodies (for the snapshot/DTO exemption):
                    // a direct CurrentCashCents assignment inside a constructor
                    // only initialises a read-only snapshot struct — it cannot
                    // mutate live cash. Track brace depth so those lines are
                    // exempt; anything else is flagged.
                    int depth = 0;
                    var ctorBodyDepths = new Stack<int>();
                    bool pendingCtor = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = StripLineComment(lines[i]);
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        bool inCtor = ctorBodyDepths.Count > 0;
                        if (IsConstructorDeclaration(line))
                            pendingCtor = true;
                        int opens = CountChar(line, '{');
                        int closes = CountChar(line, '}');
                        if (pendingCtor && opens > 0)
                        {
                            ctorBodyDepths.Push(depth + 1);
                            pendingCtor = false;
                        }

                        depth += opens - closes;
                        while (ctorBodyDepths.Count > 0 && depth < ctorBodyDepths.Peek())
                            ctorBodyDepths.Pop();
                        if (pendingCtor && closes > 0)
                            pendingCtor = false;

                        foreach (string method in CashMutatingMethods)
                        {
                            if (IsCallSite(line, method))
                                fileCount++;
                        }

                        if (!isRuntimeState && ContainsBackingFieldWrite(line))
                        {
                            findings?.Add(new EconomyIntegrityFinding(
                                "E-01", "CashWriters",
                                "Backing-field cash write outside BusinessRuntimeState",
                                EconomyFindingSeverity.Critical, EconomyFindingStatus.Open,
                                "BusinessRuntimeState",
                                file.RelativePath + ":" + (i + 1)
                                + " writes the currentCashCents backing field outside"
                                + " BusinessRuntimeState.cs; cash must move through the"
                                + " BusinessRuntimeState methods."));
                        }

                        if (!inCtor && ContainsPropertyAssignment(line))
                        {
                            findings?.Add(new EconomyIntegrityFinding(
                                "E-01", "CashWriters",
                                "Direct CurrentCashCents assignment in production",
                                EconomyFindingSeverity.Critical, EconomyFindingStatus.Open,
                                "BusinessRuntimeState",
                                file.RelativePath + ":" + (i + 1)
                                + " assigns the CurrentCashCents property directly; no"
                                + " production code may assign the cash property outside a"
                                + " snapshot/DTO constructor."));
                        }
                    }

                    if (fileCount > 0)
                    {
                        writers[file.RelativePath] = fileCount;
                        total += fileCount;
                    }
                }
            }

            var ordered = writers
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            findings?.Add(new EconomyIntegrityFinding(
                "E-01", "CashWriters",
                "Cash writer inventory: " + total + " method-call sites across " + ordered.Count + " files",
                EconomyFindingSeverity.Information, EconomyFindingStatus.Confirmed,
                "BusinessRuntimeState",
                "Cash moves only through AddCashCents / SetCurrentCashCents /"
                + " AdjustCashCents / RecordWeeklyCashTransferIn / RecordWeeklyCashTransferOut."
                + " No single cash writer per domain; the per-file table is the inventory."));

            return new CashWriterInventory(ordered, total);
        }

        private static bool IsCallSite(string line, string method)
        {
            int index = line.IndexOf(method, StringComparison.Ordinal);
            if (index < 0)
                return false;
            // A definition declares the method (access modifier + return type
            // before the name); a call site does not.
            string before = line.Substring(0, index);
            if (before.Contains(" void ") || before.TrimEnd().EndsWith("void", StringComparison.Ordinal)
                || before.Contains(" public ") || before.Contains(" private ")
                || before.Contains(" internal ") || before.Contains(" protected "))
                return false;
            int after = index + method.Length;
            return after < line.Length && line[after] == '(';
        }

        private static bool ContainsBackingFieldWrite(string line)
        {
            int index = line.IndexOf("currentCashCents", StringComparison.Ordinal);
            if (index < 0)
                return false;
            string rest = line.Substring(index + "currentCashCents".Length).TrimStart();
            if (rest.StartsWith("==", StringComparison.Ordinal))
                return false;
            if (rest.StartsWith("=", StringComparison.Ordinal))
                return true;
            // Compound assignment (+= / -=) also mutates the backing field.
            if (rest.StartsWith("+=", StringComparison.Ordinal) || rest.StartsWith("-=", StringComparison.Ordinal))
                return true;
            return false;
        }

        private static bool ContainsPropertyAssignment(string line)
        {
            int index = line.IndexOf("CurrentCashCents", StringComparison.Ordinal);
            if (index < 0)
                return false;
            // The getter is "CurrentCashCents => ..."; only a bare "=" (not
            // "=>" and not "==") is an assignment.
            string rest = line.Substring(index + "CurrentCashCents".Length).TrimStart();
            if (rest.StartsWith("==", StringComparison.Ordinal) || rest.StartsWith("=>", StringComparison.Ordinal))
                return false;
            return rest.StartsWith("=", StringComparison.Ordinal);
        }

        private static int CountChar(string line, char c)
        {
            int count = 0;
            foreach (char ch in line)
            {
                if (ch == c)
                    count++;
            }

            return count;
        }

        /// <summary>True for a constructor declaration line: an access
        /// modifier followed by exactly one identifier and '('. A method
        /// always has a return type between the modifier and the name, so
        /// it has two tokens there and is not a constructor.</summary>
        private static bool IsConstructorDeclaration(string line)
        {
            string trimmed = line.TrimStart();
            string[] modifiers = { "public ", "internal ", "private ", "protected " };
            string rest = null;
            foreach (string modifier in modifiers)
            {
                if (trimmed.StartsWith(modifier, StringComparison.Ordinal))
                {
                    rest = trimmed.Substring(modifier.Length).TrimStart();
                    break;
                }
            }

            if (rest == null)
                return false;
            int paren = rest.IndexOf('(');
            if (paren <= 0)
                return false;
            string beforeParen = rest.Substring(0, paren).Trim();
            return beforeParen.Length > 0 && beforeParen.IndexOf(' ') < 0 && beforeParen.IndexOf('\t') < 0;
        }

        private static string StripLineComment(string line)
        {
            if (string.IsNullOrEmpty(line))
                return line;
            int index = line.IndexOf("//", StringComparison.Ordinal);
            return index >= 0 ? line.Substring(0, index) : line;
        }

        // --- E-02 / DEF-067: double settlement ---------------------------------

        /// <summary>Flags a second settlement posting for the same business +
        /// period + kind. A settlement is identified by (business, period,
        /// kind); the source (which loop posted it) is the evidence.</summary>
        public static void AuditDoubleSettlement(
            IEnumerable<SettlementRecord> records,
            List<EconomyIntegrityFinding> findings)
        {
            if (records == null || findings == null)
                return;

            var seen = new Dictionary<string, SettlementRecord>(StringComparer.Ordinal);
            foreach (SettlementRecord record in records)
            {
                if (record == null)
                    continue;
                string key = record.BusinessId + "|" + record.PeriodKey + "|" + record.Kind;
                if (seen.TryGetValue(key, out SettlementRecord first))
                {
                    findings.Add(new EconomyIntegrityFinding(
                        "E-02", "DoubleSettlement",
                        "Double settlement: " + record.Kind + " posted twice for "
                        + record.BusinessId + " (" + record.PeriodKey + ")",
                        EconomyFindingSeverity.Critical, EconomyFindingStatus.Confirmed,
                        "GeneralStoreRuntimeManager / WageSettlementService",
                        "First posting " + first.AmountCents + "c via " + first.Source
                        + "; second posting " + record.AmountCents + "c via " + record.Source
                        + ". DEF-067: the legacy loop and the hub loop must never both settle the same period."));
                }
                else
                {
                    seen[key] = record;
                }
            }
        }

        /// <summary>Verifies the DEF-067 latency guards in the supplied
        /// GeneralStoreRuntimeManager source: ResolveDailySales and
        /// ResolveWeeklyPayrollAndReorder early-return with a status string
        /// when no player store is bound, so the legacy settlement loop is
        /// dead in the dev town only in effect, not by missing wiring.</summary>
        public static void VerifyLegacySalesEarlyReturn(
            string generalStoreRuntimeManagerSource,
            List<EconomyIntegrityFinding> findings)
        {
            if (findings == null)
                return;
            string source = generalStoreRuntimeManagerSource ?? string.Empty;
            bool dailyGuard = source.Contains("No player General Store is operating yet.");
            bool weeklyGuard = source.Contains("No player General Store has been formed yet.");
            if (dailyGuard && weeklyGuard)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-02", "DoubleSettlement",
                    "DEF-067 double-charge is latent in the dev town",
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Latent,
                    "GeneralStoreRuntimeManager",
                    "ResolveDailySales (:1629) and ResolveWeeklyPayrollAndReorder (:1754)"
                    + " early-return with a status string when no player store is bound;"
                    + " the legacy loop is dead in effect only. Binding a player store"
                    + " re-arms the double-charge unless the hub loop is the only settler."));
            }
            else
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-02", "DoubleSettlement",
                    "DEF-067 latency guards not found in GeneralStoreRuntimeManager source",
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Open,
                    "GeneralStoreRuntimeManager",
                    "Expected the 'No player General Store is operating yet.' and"
                    + " 'No player General Store has been formed yet.' early returns;"
                    + " without them the legacy settlement loop may be live — re-verify before trusting any sales total."));
            }
        }

        /// <summary>Verifies GeneralStoreServiceClerkPolicy agrees with
        /// WorkerSlotPersonId and creates no second settlement path: the
        /// policy file must contain no cash-mutating call and no sale
        /// settlement, and the SimulationDrivers call site must feed it the
        /// clerk id resolved by TryGetServiceClerkPersonId (which parses
        /// WorkerSlotPersonId from the clerk_helper slot).</summary>
        public static void VerifyClerkPolicy(
            string clerkPolicySource,
            string simulationDriversClerkCallSiteSource,
            List<EconomyIntegrityFinding> findings)
        {
            if (findings == null)
                return;
            string policy = clerkPolicySource ?? string.Empty;
            bool touchesCash = CashMutatingMethods.Any(m => policy.Contains(m))
                || policy.Contains("ResolveSettledSale")
                || policy.Contains("ChargeHouseholdCents")
                || policy.Contains("currentCashCents");
            string drivers = simulationDriversClerkCallSiteSource ?? string.Empty;
            bool fedBySlotParse = drivers.Contains("TryGetServiceClerkPersonId")
                && drivers.Contains("GeneralStoreServiceClerkPolicy.CandidatePersonIds");
            if (!touchesCash && fedBySlotParse)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-02", "ClerkPolicy",
                    "Clerk policy agrees with WorkerSlotPersonId; no second settlement path",
                    EconomyFindingSeverity.Information, EconomyFindingStatus.Confirmed,
                    "GeneralStoreServiceClerkPolicy",
                    "Policy is a pure static candidate list (paid clerk first, owner as"
                    + " owner-operator fallback); SimulationDrivers feeds it the clerk id"
                    + " from TryGetServiceClerkPersonId, which parses WorkerSlotPersonId"
                    + " from the clerk_helper WorkerSlot. No cash moves in the policy."));
            }
            else
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-02", "ClerkPolicy",
                    "Clerk policy verification failed",
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Open,
                    "GeneralStoreServiceClerkPolicy",
                    "touchesCash=" + touchesCash + ", fedBySlotParse=" + fedBySlotParse
                    + ". Re-read the policy and its SimulationDrivers call site before trusting counter service."));
            }
        }

        // --- W SF-7: byproduct settlement after custody moved -------------------

        /// <summary>Flags custody moved without settlement and without a
        /// compensating path. There is no compensating path in source: the
        /// SimulationDrivers caller only logs the warning and then records
        /// the purchase from the failed settlement's numbers.</summary>
        public static void AuditCustodySettlement(
            CustodyTransferRecord record,
            List<EconomyIntegrityFinding> findings)
        {
            if (record == null || findings == null)
                return;
            if (record.CustodyMoved && !record.Settled && !record.Compensated)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "SF-7", "CustodySettlement",
                    "Byproduct settlement failed after custody moved: " + record.TransferId,
                    EconomyFindingSeverity.Critical, EconomyFindingStatus.Open,
                    "FuelDealerByproductSettlement / SimulationDrivers",
                    record.UnitsMoved + "u moved, " + record.PriceCents
                    + "c unsettled; refusal: " + record.Refusal
                    + ". No compensating path exists in source (SimulationDrivers.cs:916"
                    + " logs only). Until one is proven, classify CRITICAL: the mill loses"
                    + " the byproduct value and the dealer gains stock unpaid (or cash is"
                    + " destroyed when the mill credit refuses after the dealer debit)."));
            }
            else if (record.CustodyMoved && record.Settled && record.UnitsMoved > 0 && record.PriceCents <= 0)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "SF-7", "CustodySettlement",
                    "Custody moved and settled at zero price: " + record.TransferId,
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Open,
                    "FuelDealerByproductSettlement",
                    record.UnitsMoved + "u moved for 0c. Verify this is an authored gift,"
                    + " not an unpaid transfer."));
            }
        }

        // --- E-12 / F-02: custody lots -------------------------------------------

        /// <summary>Duplicate custody (one lot, two custodians), orphan lots
        /// (agreement refused/cancelled, no disposition, no un-commit path
        /// in FreightServiceAgreements), and lots with no owner and no
        /// custodian (lost/phantom).</summary>
        public static void AuditCustodyLots(
            IEnumerable<CustodyLotRecord> lots,
            List<EconomyIntegrityFinding> findings)
        {
            if (lots == null || findings == null)
                return;

            var custodiansByLot = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (CustodyLotRecord lot in lots)
            {
                if (lot == null || string.IsNullOrWhiteSpace(lot.LotId))
                    continue;

                if (!custodiansByLot.TryGetValue(lot.LotId, out HashSet<string> custodians))
                {
                    custodians = new HashSet<string>(StringComparer.Ordinal);
                    custodiansByLot[lot.LotId] = custodians;
                }

                if (!string.IsNullOrWhiteSpace(lot.CustodianId))
                    custodians.Add(lot.CustodianId);

                bool agreementDead = string.Equals(lot.AgreementState, "Refused", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(lot.AgreementState, "Cancelled", StringComparison.OrdinalIgnoreCase);
                if (agreementDead && !lot.HasDisposition)
                {
                    findings.Add(new EconomyIntegrityFinding(
                        "F-02", "CustodyLots",
                        "Orphan lot: " + lot.LotId + " committed to a " + lot.AgreementState + " agreement with no disposition",
                        EconomyFindingSeverity.Critical, EconomyFindingStatus.Open,
                        "FreightServiceAgreements",
                        "No un-commit method exists in FreightServiceAgreements; Decision 13"
                        + " (preserve cargo/owner/custodian; physical return or lawful"
                        + " disposition) has no implemented disposition in this path."));
                }

                if (string.IsNullOrWhiteSpace(lot.OwnerId) && string.IsNullOrWhiteSpace(lot.CustodianId))
                {
                    findings.Add(new EconomyIntegrityFinding(
                        "E-12", "CustodyLots",
                        "Lost/phantom lot: " + lot.LotId + " has no owner and no custodian",
                        EconomyFindingSeverity.Warning, EconomyFindingStatus.Open,
                        "FreightServiceAgreements / StoreProcurementChain",
                        "A lot with no owner and no custodian is unaccounted goods;"
                        + " neither a duplicate nor a recorded loss."));
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in custodiansByLot)
            {
                if (pair.Value.Count > 1)
                {
                    findings.Add(new EconomyIntegrityFinding(
                        "E-12", "CustodyLots",
                        "Duplicate custody: lot " + pair.Key + " held by " + pair.Value.Count + " custodians",
                        EconomyFindingSeverity.Critical, EconomyFindingStatus.Open,
                        "FreightServiceAgreements / SawmillToYardTransferDriver",
                        "Custodians: " + string.Join(", ", pair.Value)
                        + ". One lot must have exactly one custodian; the same lot cannot transfer twice."));
                }
            }
        }

        // --- E-09 / E-10 / F1: pool/lot ------------------------------------------

        /// <summary>F1 pool/lot freeze: a pool-only sale against a category
        /// with identified lots decrements the pool without touching the
        /// lots, so the lots freeze. Pool/lot divergence when both are tracked.</summary>
        public static void AuditPoolLot(
            CategoryStockRecord record,
            List<EconomyIntegrityFinding> findings)
        {
            if (record == null || findings == null)
                return;

            if (record.SaleUnits > 0 && record.LotsTouchedBySale == 0 && record.IdentifiedLotUnits > 0)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "F1", "PoolLot",
                    "Pool-only sale froze identified lots: " + record.CategoryId,
                    EconomyFindingSeverity.Critical,
                    record.LegacyPathLive ? EconomyFindingStatus.Confirmed : EconomyFindingStatus.Latent,
                    "BusinessRuntimeState.ResolveSettledSale / GeneralStoreRuntimeManager",
                    record.SaleUnits + "u sold from the category pool; "
                    + record.IdentifiedLotUnits + "u sit in identified lots the sale never"
                    + " touched. Bread and flour freeze each other, including the bakery's"
                    + " own flour purchase. Live only in a player-store session (legacy"
                    + " path early-returns in the dev town)."));
            }

            if (record.PoolAndLotsBothTracked && record.PoolUnits != record.IdentifiedLotUnits)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-10", "PoolLot",
                    "Pool/lot divergence: " + record.CategoryId,
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Open,
                    "BusinessRuntimeState",
                    "Category pool holds " + record.PoolUnits + "u but identified lots sum to "
                    + record.IdentifiedLotUnits + "u. Pool == sum(lots) must hold wherever both are tracked."));
            }
        }

        /// <summary>The lot/pool map at this HEAD (E-10, finished here).
        /// Each row names the subsystem, the stock form it uses, and the
        /// source evidence. The sawmill support node row is E-13 (F7-2):
        /// the node writes category stock the lot-based yard transfer never
        /// reads; the dev-town linkage is still open.</summary>
        public static IReadOnlyList<LotPoolMapEntry> BuildLotPoolMap()
        {
            return new List<LotPoolMapEntry>
            {
                new LotPoolMapEntry(
                    "Fuel dealer byproduct / fuel stock", "Lot",
                    "Confirmed",
                    "Provenance-tracked lots, FIFO, 1:1 reserve (FuelDealerByproductSettlement; BakeryFuelStock/BakeryFlourStock provenance lots)."),
                new LotPoolMapEntry(
                    "Bakery staged production chain", "Lot",
                    "Confirmed",
                    "BakeryStagedProductionChain.cs:68-69 FlourStock/FuelStock are provenance lots (TryDispenseUnits); zero category-stock references in BakeryShopRuntime/StagedBakeryChainDriver/BakeryStagedProductionChain."),
                new LotPoolMapEntry(
                    "Carpentry production", "Lot",
                    "Confirmed",
                    "CarpentryProduction.cs tracks ConsumedInputLotIds / MaterialLotIds per process; identified-lot path."),
                new LotPoolMapEntry(
                    "Sawmill -> yard transfer", "Lot",
                    "Confirmed",
                    "SawmillToYardTransferDriver moves SawmillLumberLot custody; the same lot can never transfer again."),
                new LotPoolMapEntry(
                    "Freight shipments", "Lot",
                    "Confirmed",
                    "LogisticsShipmentState.cargoLotId + manifestLines (one line per source lot); lot-backed."),
                new LotPoolMapEntry(
                    "Off-map aid cargo", "Lot",
                    "Confirmed",
                    "OffMapAidCargoLot carries CreatedDayIndex only (OffMapAidBusinessRuntime.cs:21); lot with no ETA (see E-18)."),
                new LotPoolMapEntry(
                    "Household reserves", "Lot",
                    "Confirmed",
                    "SharedCategoryStockLedger StockProvenanceRecord per lot (LotId, OriginRef, SourceLotIds, Consumed flag)."),
                new LotPoolMapEntry(
                    "General Store legacy path", "Pool",
                    "Confirmed",
                    "ReceiveLocalSupply -> BusinessRuntimeState.AddCategoryStockUnits (pool only); TrySellReserveCategoryFromGeneralStore -> ResolveSettledSale (pool only). F1 freeze lives here."),
                new LotPoolMapEntry(
                    "General Store generic retail", "Quantity",
                    "Confirmed",
                    "GenericConfiguration inventory quantities (GetInventoryQuantity / GetAvailableInventoryQuantity); separate authority from the legacy category pool."),
                new LotPoolMapEntry(
                    "Sawmill support node (E-13 / F7-2)", "Pool (written) vs Lot (read)",
                    "Open",
                    "SharedBusinessRuntimeManager.SyncSawmillBusinessStockFromSupport writes CATEGORY stock (SetCategoryStockUnits, CategoryLumber); the lot-based yard transfer never reads it. E-13 CLOSED (Lane 9e, source-verified 2026-11-20): the dev-town linkage is AcquisitionMarketManager.OnWeekChanged -> ResolveWeeklyConstructionSupportProduction -> EnsureConstructionSupportNodesLoaded -> LinkSawmillSupportNodeToSharedRuntime -> SetSawmillConstructionSupportNode — the weekly market tick, not a player path (the only other SetSawmillConstructionSupportNode callers are the snapshot Rollback and the link helper itself). F7-2 ROUTED (Lane 9e): the weekly yard-transfer drive now moves support-node category lumber into yard purchase lots (paid wholesale, 80/week cap), and the node regrows per DEF-072 (white pine, 80 years). The unwired scalar TransferSawmillLumberToLumberYard stays unwired by design — its output is yard category stock, which no sale path reads; the driver leg supersedes it. Row stays Open until the 5-10 day 10x run verifies the route live."),
                new LotPoolMapEntry(
                    "Store procurement import (E-12)", "Unknown",
                    "Open",
                    "StoreProcurementChain.cs import hazard (Delivered-before-intake loss, 'UNWIND FAILED') still unread at this HEAD; the map row stays open rather than guessed."),
            };
        }

        // --- E-14: production without inputs --------------------------------------

        /// <summary>Output with a required input consumed at zero units is
        /// production without inputs. A zero requirement is not a violation.</summary>
        public static void AuditProduction(
            ProductionRecord record,
            List<EconomyIntegrityFinding> findings)
        {
            if (record == null || findings == null)
                return;
            if (record.OutputUnits > 0 && record.InputUnitsRequired > 0 && record.InputUnitsConsumed == 0)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-14", "ProductionWithoutInputs",
                    "Production without inputs: " + record.Label,
                    EconomyFindingSeverity.Critical, EconomyFindingStatus.Confirmed,
                    "BakeryShopRuntime / CarpentryProduction",
                    record.OutputUnits + "u of output with 0u of required input '"
                    + record.RequiredInputKind + "' consumed (" + record.InputUnitsRequired
                    + "u required). Goods appearing without legitimate production."));
            }
        }

        /// <summary>Bakery fuel is a CONFIG value (E-14): when
        /// fuelDealerIntakeUnitsPerBakeryUnit is 0, the paid dealer source
        /// returns null and the bakery runs on its 40u opening stock only;
        /// the day-41 death follows from the config, not from missing code.</summary>
        public static void AuditBakeryFuelConfig(
            int fuelDealerIntakeUnitsPerBakeryUnit,
            List<EconomyIntegrityFinding> findings)
        {
            if (findings == null)
                return;
            if (fuelDealerIntakeUnitsPerBakeryUnit <= 0)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-14", "BakeryFuelConfig",
                    "Bakery fuel is a config value of 0, not missing code",
                    EconomyFindingSeverity.Critical, EconomyFindingStatus.ConfigValue,
                    "DevelopmentTownConfiguration.asset",
                    "fuelDealerIntakeUnitsPerBakeryUnit: 0 (bakery blocks :1720, :1807);"
                    + " the paid dealer source (TryBuildPaidBakeryFuelSource,"
                    + " SimulationDrivers.cs:14364) returns null, so the live source falls"
                    + " back to in-stock fuel only (40u opening). Day-41 fuel death follows"
                    + " from this value. Fix is a config decision, not a code repair."));
            }
            else
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-14", "BakeryFuelConfig",
                    "Bakery fuel dealer source is configured (" + fuelDealerIntakeUnitsPerBakeryUnit + "u per bakery unit)",
                    EconomyFindingSeverity.Information, EconomyFindingStatus.ConfigValue,
                    "DevelopmentTownConfiguration.asset",
                    "Paid dealer source can build; re-verify the day-41 death is gone in a run."));
            }
        }

        /// <summary>Reproduces the 24-loaf wholesale known case (E-06):
        /// the obligation is authored in config (24 x bake.bread-loaf/day @
        /// 4c), not created by a driver. Under ProtectWholesaleContracts it
        /// drains retail stock; under ProtectRetailCounter it does not.</summary>
        public static void AuditWholesaleObligation(
            int loavesPerDay, int priceCentsPerLoaf, string source, string protectionPolicy,
            List<EconomyIntegrityFinding> findings)
        {
            if (findings == null)
                return;
            if (loavesPerDay <= 0)
                return;
            bool wholesaleFirst = string.Equals(protectionPolicy, "ProtectWholesaleContracts", StringComparison.Ordinal);
            findings.Add(new EconomyIntegrityFinding(
                "E-06", "WholesaleObligation",
                "24-loaf wholesale obligation: " + loavesPerDay + " loaves/day @ " + priceCentsPerLoaf + "c"
                + (wholesaleFirst ? " drains retail stock" : " (retail protected)"),
                wholesaleFirst ? EconomyFindingSeverity.Warning : EconomyFindingSeverity.Information,
                EconomyFindingStatus.Confirmed,
                "DevelopmentTownConfiguration.asset",
                "Obligation authored in config (" + source + "), first day 0;"
                + " protection policy is " + protectionPolicy + "."
                + (wholesaleFirst
                    ? " Under ProtectWholesaleContracts the wholesale account takes loaves before the retail counter; see Lane 9a for the removal."
                    : " Under ProtectRetailCounter the retail counter is protected; the obligation still books.")));
        }

        // --- E-05: unpaid transfers ------------------------------------------------

        /// <summary>Goods moved without full payment. E-05 verified: lumber
        /// transfers at 32c/unit are paid purchases with custody movement.</summary>
        public static void AuditTransferPayment(
            TransferPaymentRecord record,
            List<EconomyIntegrityFinding> findings)
        {
            if (record == null || findings == null)
                return;
            int owed = Math.Max(0, record.GoodsUnits) * Math.Max(0, record.UnitPriceCents);
            if (record.GoodsUnits > 0 && record.PaidCents < owed)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-05", "UnpaidTransfer",
                    "Unpaid transfer: " + record.TransferId,
                    EconomyFindingSeverity.Critical, EconomyFindingStatus.Confirmed,
                    "SawmillToYardTransferDriver / SharedBusinessRuntimeManager",
                    record.GoodsUnits + "u moved; " + record.PaidCents + "c paid of "
                    + owed + "c owed (" + record.UnitPriceCents + "c/u) from "
                    + record.PayerId + " to " + record.PayeeId + ". Goods moved unpaid."));
            }
        }

        // --- E-18: shipments, ETA, short delivery ---------------------------------

        /// <summary>Structural check on the LogisticsShipmentState field list:
        /// no ETA, no overdue and no created-day field means a shipment's age
        /// can never be judged by the system itself (E-18).</summary>
        public static void VerifyShipmentFields(
            IEnumerable<string> logisticsShipmentStateFields,
            List<EconomyIntegrityFinding> findings)
        {
            if (findings == null)
                return;
            var fields = new HashSet<string>(
                logisticsShipmentStateFields ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            bool hasEta = fields.Any(f => f.IndexOf("eta", StringComparison.OrdinalIgnoreCase) >= 0
                || f.IndexOf("dueDay", StringComparison.OrdinalIgnoreCase) >= 0
                || f.IndexOf("expectedDelivery", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasOverdue = fields.Any(f => f.IndexOf("overdue", StringComparison.OrdinalIgnoreCase) >= 0
                || f.IndexOf("delayCycles", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasCreated = fields.Any(f => f.IndexOf("createdDay", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!hasEta)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-18", "ShipmentFields",
                    "LogisticsShipmentState has no ETA field",
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Confirmed,
                    "LogisticsModels",
                    "No ETA / due-day / expected-delivery field on LogisticsShipmentState;"
                    + " OffMapAidCargoLot carries CreatedDayIndex only. No lost-shipment"
                    + " rule and no overdue recognition can exist until the field does."
                    + " delayCycles (DEF-071) counts failed attempts, it is not an ETA."));
            }

            if (!hasCreated)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-18", "ShipmentFields",
                    "LogisticsShipmentState has no created-day field",
                    EconomyFindingSeverity.Information, EconomyFindingStatus.Confirmed,
                    "LogisticsModels",
                    "Age cannot be computed from the shipment record itself; only"
                    + " OffMapAidCargoLot carries CreatedDayIndex."));
            }

            if (!hasOverdue)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-18", "ShipmentFields",
                    "No overdue recognition on shipments",
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Confirmed,
                    "LogisticsModels",
                    "No overdue flag or rule; a shipment can strand indefinitely without the system noticing."));
            }
        }

        /// <summary>Per-shipment completeness: every order must end in
        /// exactly one of delivered / cancelled-with-disposition /
        /// outstanding-with-age — never silently complete (E-18 / W-3).</summary>
        public static void AuditShipments(
            IEnumerable<ShipmentRecord> shipments,
            List<EconomyIntegrityFinding> findings)
        {
            if (shipments == null || findings == null)
                return;
            foreach (ShipmentRecord shipment in shipments)
            {
                if (shipment == null)
                    continue;
                if (shipment.IsComplete && shipment.DeliveredUnits < shipment.PlannedUnits)
                {
                    findings.Add(new EconomyIntegrityFinding(
                        "E-18", "Shipments",
                        "Short delivery recorded as complete: " + shipment.ShipmentId,
                        EconomyFindingSeverity.Critical, EconomyFindingStatus.Confirmed,
                        "BakeryShopRuntime / FreightShipmentService",
                        "Planned " + shipment.PlannedUnits + "u, delivered "
                        + shipment.DeliveredUnits + "u, marked complete; the shortfall is"
                        + " never made up and no warning is raised."));
                }

                int age = shipment.CurrentDayIndex - shipment.CreatedDayIndex;
                if (!shipment.IsComplete && !shipment.HasEtaField && !shipment.HasOverdueRule
                    && age > ShipmentOverdueDiagnosticAgeDays)
                {
                    findings.Add(new EconomyIntegrityFinding(
                        "E-18", "Shipments",
                        "Outstanding shipment with no ETA is " + age + " days old: " + shipment.ShipmentId,
                        EconomyFindingSeverity.Warning, EconomyFindingStatus.Open,
                        "LogisticsModels",
                        "Created day " + shipment.CreatedDayIndex + ", now day "
                        + shipment.CurrentDayIndex + "; no ETA field and no overdue rule,"
                        + " so the system cannot recognise it as lost. " + ShipmentOverdueDiagnosticAgeDays
                        + "d is an auditor threshold, not a game rule."));
                }
            }
        }

        // --- E-19: supplier balances ------------------------------------------------

        /// <summary>Supplier ledgers are credit-only sinks: no debit method
        /// exists in RegionalBusinessRegistry, so supplier cash is not a real
        /// economic quantity and balances are non-authoritative until a debit
        /// path exists.</summary>
        public static void AuditSupplierLedger(
            SupplierLedgerRecord record,
            List<EconomyIntegrityFinding> findings)
        {
            if (record == null || findings == null)
                return;
            if (!record.HasDebitMethod && record.TotalDebitsCents > 0)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-19", "SupplierLedger",
                    "Supplier ledger claims debits with no debit method: " + record.SupplierId,
                    EconomyFindingSeverity.Critical, EconomyFindingStatus.Open,
                    "RegionalBusinessRegistry",
                    record.TotalDebitsCents + "c of debits recorded against a ledger with"
                    + " no debit method. Either the debits are fictional or the ledger is not the authority."));
            }
            else if (!record.HasDebitMethod)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-19", "SupplierLedger",
                    "Supplier balance is credit-only and non-authoritative: " + record.SupplierId,
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Confirmed,
                    "RegionalBusinessRegistry",
                    "Credits total " + record.TotalCreditsCents + "c; no debit method exists"
                    + " in RegionalBusinessRegistry, so the balance can only grow and is not"
                    + " a real economic quantity. Surface it as non-authoritative until a debit path exists."));
            }
        }

        /// <summary>Source check for E-19: the RegionalBusinessRegistry
        /// source must contain no debit method.</summary>
        public static void VerifySupplierLedgerHasNoDebit(
            string regionalBusinessRegistrySource,
            List<EconomyIntegrityFinding> findings)
        {
            if (findings == null)
                return;
            string source = regionalBusinessRegistrySource ?? string.Empty;
            bool hasDebit = ContainsMethodNamed(source, "Debit");
            if (!hasDebit)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-19", "SupplierLedger",
                    "No debit method in RegionalBusinessRegistry (credit-only confirmed)",
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Confirmed,
                    "RegionalBusinessRegistry",
                    "No method named Debit in the registry source; supplier balances are credit-only sinks."));
            }
        }

        private static bool ContainsMethodNamed(string source, string methodName)
        {
            // A method declaration, not a passing mention: "<name>(" preceded
            // by a return type / modifier on the same line.
            string[] lines = source.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = StripLineComment(rawLine);
                int index = line.IndexOf(methodName, StringComparison.Ordinal);
                if (index < 0)
                    continue;
                int after = index + methodName.Length;
                if (after >= line.Length || line[after] != '(')
                    continue;
                string before = line.Substring(0, index);
                if (before.Contains("void") || before.Contains("int") || before.Contains("bool")
                    || before.Contains("string") || before.Contains("public") || before.Contains("private")
                    || before.Contains("internal") || before.Contains("protected"))
                    return true;
            }

            return false;
        }

        // --- E-20: opening cash -------------------------------------------------------

        /// <summary>Config governs at bootstrap (FirstLedgerSliceBootstrapper
        /// applies slot.openingCashCents); profiles that disagree are stale.
        /// Households: 8 x 40,000c in config.</summary>
        public static void AuditOpeningCash(
            IEnumerable<OpeningCashRecord> records,
            List<EconomyIntegrityFinding> findings)
        {
            if (records == null || findings == null)
                return;
            foreach (OpeningCashRecord record in records)
            {
                if (record == null)
                    continue;
                if (record.ConfigCents != record.ProfileCents || record.ConfigCents != record.CodeDefaultCents)
                {
                    findings.Add(new EconomyIntegrityFinding(
                        "E-20", "OpeningCash",
                        "Opening-cash discrepancy: " + record.Label,
                        EconomyFindingSeverity.Warning, EconomyFindingStatus.Open,
                        "DevelopmentTownConfiguration.asset / BusinessProfiles / BusinessDefinitions",
                        "Config " + record.ConfigCents + "c vs profile " + record.ProfileCents
                        + "c vs code default " + record.CodeDefaultCents
                        + "c. Config governs at bootstrap; the profile value is stale/wrong."
                        + " Owner decision OD-P2-9 still owed."));
                }
            }
        }

        // --- E-23: reservation coverage ----------------------------------------------

        /// <summary>The unified reservation authority EXISTS
        /// (IUnifiedReservationAuthority / EquipmentReservationService, used
        /// in 11+ production files) — but zero uses in GeneralStore,
        /// HouseholdShoppingLoop or ShoppingTripDriver, so the store
        /// shared-stock, yard quote/sale split and store flour-lot cases have
        /// no reservation. Record the coverage gap; do NOT build a second
        /// reservation system.</summary>
        public static void AuditReservationCoverage(
            IEnumerable<EconomySourceTextFile> retailFiles,
            IEnumerable<EconomySourceTextFile> authorityUseFiles,
            List<EconomyIntegrityFinding> findings)
        {
            if (findings == null)
                return;
            int retailUseCount = CountReservationUses(retailFiles);
            int authorityUseCount = CountFilesWithReservationUse(authorityUseFiles);
            if (retailUseCount == 0)
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-23", "ReservationCoverage",
                    "Unified reservation authority exists but has zero retail coverage",
                    EconomyFindingSeverity.Warning, EconomyFindingStatus.Confirmed,
                    "IUnifiedReservationAuthority / EquipmentReservationService",
                    "Authority is used in " + authorityUseCount + " production files"
                    + " (TaskAuthorityLifecycle, BakeryStagedProductionChain, FreightShipmentService, ...)"
                    + " and in zero GeneralStore / HouseholdShoppingLoop / ShoppingTripDriver"
                    + " files. Store shared-stock, yard quote/sale split and store flour-lot"
                    + " cases have no reservation / available-to-promise. Coverage gap, not a missing mechanism."));
            }
            else
            {
                findings.Add(new EconomyIntegrityFinding(
                    "E-23", "ReservationCoverage",
                    "Retail reservation coverage present (" + retailUseCount + " uses)",
                    EconomyFindingSeverity.Information, EconomyFindingStatus.Confirmed,
                    "IUnifiedReservationAuthority / EquipmentReservationService",
                    "Re-verify the three Nov 19 instances (store shared stock, yard quote/sale split, store flour lots) against the new uses."));
            }
        }

        private static int CountReservationUses(IEnumerable<EconomySourceTextFile> files)
        {
            int count = 0;
            if (files == null)
                return 0;
            foreach (EconomySourceTextFile file in files)
            {
                if (file == null)
                    continue;
                string[] lines = file.Text.Split('\n');
                foreach (string rawLine in lines)
                {
                    string line = StripLineComment(rawLine);
                    if (line.Contains("IUnifiedReservationAuthority") || line.Contains("EquipmentReservationService"))
                        count++;
                }
            }

            return count;
        }

        private static int CountFilesWithReservationUse(IEnumerable<EconomySourceTextFile> files)
        {
            int count = 0;
            if (files == null)
                return 0;
            foreach (EconomySourceTextFile file in files)
            {
                if (file == null)
                    continue;
                string[] lines = file.Text.Split('\n');
                foreach (string rawLine in lines)
                {
                    string line = StripLineComment(rawLine);
                    if (line.Contains("IUnifiedReservationAuthority") || line.Contains("EquipmentReservationService"))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        // --- Known cases, from the current tree ---------------------------------------

        /// <summary>Reproduces the lane's known cases from the current tree's
        /// verified evidence values (no run exists on this HEAD; these are
        /// source-reviewed values, not run outcomes). Callers that want live
        /// data feed the individual checks instead.</summary>
        public static EconomyIntegrityReport AuditKnownCases()
        {
            var findings = new List<EconomyIntegrityFinding>();

            // 24-loaf wholesale (E-06): config-authored, ProtectWholesaleContracts default.
            AuditWholesaleObligation(
                24, 4,
                "DevelopmentTownConfiguration.asset bakery wholesaleAgreements (24 x bake.bread-loaf/day @ 4c, first day 0)",
                "ProtectWholesaleContracts", findings);

            // DEF-067 double charge (E-02, §17 CRITICAL): the same weekly payroll
            // settled twice for one store and period — once by the wage service,
            // once by the legacy General Store loop. The audit flags any second
            // posting for the same business + period + kind; a single posting
            // (the E-05 paid case below) produces no finding.
            AuditDoubleSettlement(
                new[]
                {
                    new SettlementRecord("store:1", "week-12", "payroll", 500, "WageSettlementService"),
                    new SettlementRecord("store:1", "week-12", "payroll", 500, "GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder"),
                },
                findings);

            // Byproduct settlement (W SF-7): custody moved, settlement refused
            // (mill credit refused after dealer debit), no compensating path.
            AuditCustodySettlement(
                new CustodyTransferRecord(
                    "sawmill-byproduct/D-F3", 10, custodyMoved: true, settled: false,
                    priceCents: 10, compensated: false,
                    "mill credit refused after the dealer debit succeeded"),
                findings);

            // F-02 orphan: lot committed to a refused freight agreement, no disposition.
            AuditCustodyLots(
                new[]
                {
                    new CustodyLotRecord("freight-lot/F-02", "yard", "mill", "Refused", hasDisposition: false)
                },
                findings);

            // E-14: bakery fuel config value 0.
            AuditBakeryFuelConfig(0, findings);

            // E-19: supplier ledgers credit-only.
            AuditSupplierLedger(
                new SupplierLedgerRecord("regional-suppliers", 0, 0, hasDebitMethod: false),
                findings);

            // E-18: LogisticsShipmentState has no ETA / created-day field.
            VerifyShipmentFields(
                new[]
                {
                    "shipmentId", "transferAgreementId", "freightServiceAgreementId",
                    "shipmentKind", "cargoClass", "supplierClass", "state",
                    "sourceBusinessInstanceId", "destinationBusinessInstanceId",
                    "cargoLotId", "cargoSkuId", "requiresSourceLotCustody",
                    "manifestLines", "sourceLotDebitApplied", "plannedQuantityUnits",
                    "remainingQuantityUnits", "deliveredQuantityUnits",
                    "sellerUnitRevenueCents", "buyerUnitCostCents", "freightChargeCents",
                    "freightRevenueCollected", "delayCycles"
                },
                findings);

            // E-20: opening cash — config governs, profiles stale.
            AuditOpeningCash(
                new[]
                {
                    new OpeningCashRecord("Bakery", 35000, 15000, 25000),
                    new OpeningCashRecord("Fuel dealer", 20000, 13000, 25000)
                },
                findings);

            // E-05 paid case: 32c/unit lumber, paid in full — no finding expected.
            AuditTransferPayment(
                new TransferPaymentRecord("sawmill-lumber/32c", 10, 32, 320, "yard", "sawmill"),
                findings);

            // F1 latent: pool-only sale against identified lots, legacy path not live in dev.
            // PoolAndLotsBothTracked is false here: the pool and the lots are
            // different representations of the same category, not two counts of
            // one stockpile, so no divergence finding applies — only the freeze.
            AuditPoolLot(
                new CategoryStockRecord("staple_food", 100, 40, false, 24, 0, legacyPathLive: false),
                findings);

            return new EconomyIntegrityReport(findings, new CashWriterInventory(new Dictionary<string, int>(), 0), BuildLotPoolMap());
        }
    }
}
