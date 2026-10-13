using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Estates
{
    /// <summary>
    /// Pass VII Workstream C: the wiring the estate lifecycle driver needs.
    /// Every authority stays the record owner it already is: EstateService is
    /// the estate RECORD authority, the title authority owns parcels, the
    /// financial obligation authority owns debts, the household ledgers own
    /// household cash. Fields are optional individually; the driver uses what
    /// is present, skips what is absent, and says so in the diagnostics.
    /// Nothing is invented from an absent registry.
    /// </summary>
    public sealed class EstateLifecycleDriverContext
    {
        /// <summary>Required: the estate record authority (OpenEstate, RegisterDecedentParcel).</summary>
        public EstateService Estates;
        /// <summary>Required: the D4D legal and financial machinery (inventory, claims, accounts).</summary>
        public EstateAdministrationService Administration;
        public EntityIdRegistry Ids;
        public KinshipRegistry Kinship;
        public PopulationState Population;
        public CalendarAnchor Anchor = CalendarAnchor.Default;
        public TitleAuthority Titles;
        public FinancialObligationAuthority Obligations;
        /// <summary>T2A instrument index, for decedent-obligor debts and decedent-payee notes.</summary>
        public CreditRegistry Credit;
        /// <summary>Bank deposit books to search for the decedent's accounts.</summary>
        public List<BankDepositLedger> DepositBooks = new List<BankDepositLedger>();
        /// <summary>Livestock registry; person-owned animals marshal as personal property.</summary>
        public AnimalRegistry Animals;
        /// <summary>Business instances keyed by instance id; the person-keyed ownership seam.</summary>
        public Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>();
        public HouseholdMembershipRegistry Memberships;
        public HouseholdLedgerRegistry Ledgers;
        public WillContestService Contests;
        public ProbateService Probate;
        public int CurrentDayIndex;

        public EstateLifecycleDriverContext() { }
    }

    /// <summary>
    /// Pass VII Workstream C: one minor heir's preserved interest. The minor
    /// owns the interest; a lawful custodian (the estate fiduciary) administers
    /// it. Children never receive unrestricted contracting authority.
    /// </summary>
    [Serializable]
    public sealed class EstateMinorCustody
    {
        public int MinorPersonId = -1;
        public string AssetKind = string.Empty; // "parcel", "cash", "business-interest"
        public string AssetRef = string.Empty;
        public int ShareNumerator = 1;
        public int ShareDenominator = 1;
        public int CustodianPersonId = -1;
        public string Note = string.Empty;

        public EstateMinorCustody() { }
    }

    /// <summary>
    /// Pass VII Workstream C: one heir's undivided share of a business
    /// interest when several heirs take. The asset is never duplicated; the
    /// recorded interim manager operates it while ownership stays undivided.
    /// </summary>
    [Serializable]
    public sealed class EstateUndividedBusinessShare
    {
        public string BusinessInstanceId = string.Empty;
        public int HeirPersonId = -1;
        public int ShareNumerator = 1;
        public int ShareDenominator = 1;
        public bool IsInterimManager;

        public EstateUndividedBusinessShare() { }
    }

    /// <summary>
    /// Pass VII Workstream C: the driver's own per-estate progress record.
    /// This is what makes every stage idempotent across re-runs and across
    /// save/load: re-running a completed stage is a no-op, never a duplicate
    /// distribution.
    /// </summary>
    [Serializable]
    public sealed class EstateLifecycleProgress
    {
        public string EstateId = string.Empty;
        public EstateLifecycleStage Stage = EstateLifecycleStage.Opened;
        public int FiduciaryPersonId = -1;
        public int InventoriedDayIndex = -1;
        public List<string> CollectedDepositAccountIds = new List<string>();
        public List<string> DistributedParcelIds = new List<string>();
        public List<string> TransferredBusinessIds = new List<string>();
        public List<int> ResidueDisbursedHeirIds = new List<int>();
        public List<EstateMinorCustody> MinorCustodies = new List<EstateMinorCustody>();
        public List<EstateUndividedBusinessShare> UndividedBusinessShares = new List<EstateUndividedBusinessShare>();
        public List<string> LegalHolds = new List<string>();
        public int ClosedDayIndex = -1;

        public EstateLifecycleProgress() { }
    }

    /// <summary>
    /// Pass VII Workstream C: the runtime estate administration driver,
    /// implementing the contract in EstateLifecycleContracts.cs.
    ///
    /// EstateService remains the estate RECORD authority; this driver
    /// executes the lifecycle that was source-only: inventory, claims,
    /// distribution, closure. The authoritative legal and financial model is
    /// the D4D EstateAdministrationService: the hard order of claims, the
    /// inventory lines that cite real registries, the estate account that
    /// starts at zero and fills only through recorded receipts.
    ///
    /// Canon-held rules enforced throughout:
    /// - Death never erases debts; beneficiaries are never made personally
    ///   liable for estate debts.
    /// - Title changes flow only through the TitleAuthority with
    ///   TitleBasis.Inheritance.
    /// - An insolvent estate invents no funds: shortfalls are refused loudly
    ///   and recorded as legal holds.
    /// - Unresolved legal priority questions are explicit holds, never
    ///   universal rules invented by the driver.
    /// </summary>
    public sealed class EstateLifecycleDriver : IEstateLifecycleDriver
    {
        private const int MajorityAge = 18;

        private readonly EstateLifecycleDriverContext ctx;
        private readonly Dictionary<string, EstateLifecycleProgress> progress =
            new Dictionary<string, EstateLifecycleProgress>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public EstateLifecycleDriver(EstateLifecycleDriverContext context)
        {
            ctx = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region Stage plumbing

        public EstateLifecycleStage StageOf(string estateId)
        {
            EstateLifecycleProgress known = ProgressFor(estateId);
            if (known != null) return known.Stage;
            // A driver that never ran this estate reads the record authority's
            // own status as a conservative estimate.
            Estate estate = ctx.Estates != null ? ctx.Estates.Get(estateId) : null;
            if (estate == null) return EstateLifecycleStage.Opened;
            switch (estate.Status)
            {
                case EstateStatus.Closed: return EstateLifecycleStage.Closed;
                case EstateStatus.Distributing: return EstateLifecycleStage.ClaimsAdjudicated;
                case EstateStatus.DebtsSettling: return EstateLifecycleStage.Inventoried;
                default: return EstateLifecycleStage.Opened;
            }
        }

        /// <summary>Read model for tests and callers: the driver's per-estate progress.</summary>
        public EstateLifecycleProgress ProgressFor(string estateId)
        {
            if (string.IsNullOrWhiteSpace(estateId)) return null;
            return progress.TryGetValue(estateId, out EstateLifecycleProgress known) ? known : null;
        }

        private EstateLifecycleProgress Track(string estateId)
        {
            if (!progress.TryGetValue(estateId, out EstateLifecycleProgress known))
            {
                known = new EstateLifecycleProgress { EstateId = estateId };
                progress[estateId] = known;
            }
            return known;
        }

        private void AddHold(EstateLifecycleProgress prog, string hold, List<string> diag)
        {
            if (prog == null || string.IsNullOrWhiteSpace(hold)) return;
            if (!prog.LegalHolds.Contains(hold)) prog.LegalHolds.Add(hold);
            diag.Add($"EstateLifecycleDriver: LEGAL HOLD on estate {prog.EstateId} — {hold}");
        }

        private Estate RequireEstate(string estateId, List<string> diag)
        {
            if (ctx.Estates == null)
            {
                diag.Add("EstateLifecycleDriver: the estate service is required.");
                return null;
            }
            if (ctx.Administration == null)
            {
                diag.Add("EstateLifecycleDriver: the estate administration service is required.");
                return null;
            }
            Estate estate = ctx.Estates.Get(estateId);
            if (estate == null)
            {
                diag.Add($"EstateLifecycleDriver: unknown estate '{estateId}'.");
                return null;
            }
            return estate;
        }

        private string HeirName(int personId)
        {
            PersonState person = ctx.Population != null ? ctx.Population.GetPerson(personId) : null;
            if (person != null && !string.IsNullOrWhiteSpace(person.DisplayName)) return person.DisplayName;
            return $"P{personId}";
        }

        private bool IsAdult(int personId)
        {
            if (ctx.Population == null) return false;
            PersonState person = ctx.Population.GetPerson(personId);
            if (person == null) return false;
            if (person.deathDayIndex >= 0) return false;
            CalendarAnchor anchor = ctx.Anchor;
            anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            return person.GetTrueAge(year, month, day) >= MajorityAge;
        }

        private static string InstrumentObligationId(ICreditInstrument instrument)
        {
            if (instrument is PromissoryNote note) return note.ObligationId;
            if (instrument is SellerFinanceNote sellerNote) return sellerNote.ObligationId;
            if (instrument is MortgageDeed mortgage) return mortgage.ObligationId;
            if (instrument is PropertyLien lien) return lien.ObligationId;
            if (instrument is GuarantyAgreement guaranty) return guaranty.ObligationId;
            return string.Empty;
        }

        private bool IsPersonOwnerId(string ownerId, int personId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return false;
            return string.Equals(ownerId, personId.ToString(), StringComparison.Ordinal)
                || string.Equals(ownerId, "P" + personId, StringComparison.Ordinal);
        }

        #endregion

        #region Inventory

        /// <summary>
        /// Inventories the estate: appoints a lawful fiduciary, opens the
        /// estate account, enumerates the decedent's parcels through the title
        /// authority, records person-keyed business interests, marshals bank
        /// deposits, notes receivable, and person-owned livestock. Idempotent.
        /// </summary>
        public string InventoryEstate(string estateId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            Estate estate = RequireEstate(estateId, diag);
            if (estate == null) return $"EstateLifecycleDriver.InventoryEstate: unknown estate '{estateId}'.";
            if (estate.Status == EstateStatus.Closed)
                return $"EstateLifecycleDriver.InventoryEstate: estate {estateId} is closed — a closed estate is never re-inventoried.";
            EstateLifecycleProgress prog = Track(estateId);
            if (prog.Stage >= EstateLifecycleStage.Inventoried)
            {
                diag.Add($"EstateLifecycleDriver: estate {estateId} already inventoried (day {prog.InventoriedDayIndex}) — no-op.");
                return null;
            }

            int day = ctx.CurrentDayIndex;
            string refusal = AppointFiduciary(estate, prog, diag);
            if (refusal != null) return refusal;
            int fiduciary = prog.FiduciaryPersonId;

            ctx.Administration.OpenEstateAccount(estate, fiduciary, day, diag);

            InventoryParcels(estate, fiduciary, day, diag);
            InventoryBusinessInterests(estate, prog, fiduciary, day, diag);
            InventoryDeposits(estate, fiduciary, day, diag);
            InventoryReceivables(estate, fiduciary, day, diag);
            InventoryLivestock(estate, fiduciary, day, diag);

            prog.Stage = EstateLifecycleStage.Inventoried;
            prog.InventoriedDayIndex = day;
            diag.Add($"EstateLifecycleDriver: estate {estateId} inventoried — " +
                $"{estate.ParcelIds.Count} parcels, {estate.BusinessInstanceIds.Count} business interests, " +
                $"{ctx.Administration.InventoryFor(estateId).Count} inventory lines.");
            return null;
        }

        /// <summary>
        /// Chooses a lawful fiduciary from real kin: the will-named executor
        /// for a probated testate estate, else the surviving adult spouse,
        /// else the eldest adult child. Children and unknown persons are never
        /// appointed. A missing qualified adult is a legal hold, not a fiat
        /// appointment.
        /// </summary>
        private string AppointFiduciary(Estate estate, EstateLifecycleProgress prog, List<string> diag)
        {
            if (prog.FiduciaryPersonId >= 0)
            {
                diag.Add($"EstateLifecycleDriver: estate {estate.EstateId} already has fiduciary P{prog.FiduciaryPersonId} — no-op.");
                return null;
            }

            int candidate = -1;
            string candidateName = string.Empty;
            bool testate = !string.IsNullOrWhiteSpace(estate.TestateWillId);
            if (testate && ctx.Probate != null)
            {
                Will will = ctx.Probate.Get(estate.TestateWillId);
                if (will != null && will.Probated && !will.Contested && !will.InvalidatedByContest
                    && will.ExecutorPersonId >= 0 && will.ExecutorPersonId != estate.DecedentPersonId)
                {
                    if (IsAdult(will.ExecutorPersonId))
                    {
                        candidate = will.ExecutorPersonId;
                        candidateName = HeirName(candidate);
                        diag.Add($"EstateLifecycleDriver: estate {estate.EstateId} is testate — will {will.WillId} names executor P{candidate}.");
                    }
                    else
                    {
                        diag.Add($"EstateLifecycleDriver: will {will.WillId} names P{will.ExecutorPersonId} as executor, " +
                            "who is not a known living adult — the nomination cannot confer authority.");
                    }
                }
                else if (will != null)
                {
                    diag.Add($"EstateLifecycleDriver: estate {estate.EstateId} names will {estate.TestateWillId}, " +
                        "which is not probated or is contested — the executor's authority flows from probate, never from the paper alone.");
                }
            }

            if (candidate < 0)
            {
                candidate = ChooseIntestateFiduciary(estate, diag);
                candidateName = candidate >= 0 ? HeirName(candidate) : string.Empty;
            }

            if (candidate < 0)
            {
                AddHold(prog, "no qualified adult fiduciary among the heirs — the estate waits for letters of administration", diag);
                return $"EstateLifecycleDriver: estate {estate.EstateId} has no qualified adult fiduciary — held.";
            }

            if (testate && ctx.Probate != null)
            {
                Will will = ctx.Probate.Get(estate.TestateWillId);
                if (will != null && will.Probated && will.ExecutorPersonId == candidate)
                {
                    var appointment = ctx.Administration.AcceptExecutorAppointment(
                        ctx.Ids, estate, will, ctx.Estates, candidate, candidateName,
                        ctx.Population, ctx.CurrentDayIndex, diag);
                    if (appointment == null || !appointment.Accepted)
                        return $"EstateLifecycleDriver: executor appointment for P{candidate} on estate {estate.EstateId} did not take — held.";
                    prog.FiduciaryPersonId = candidate;
                    return null;
                }
            }

            var admin = ctx.Administration.GrantAdministrator(
                ctx.Ids, estate, ctx.Estates, candidate, candidateName,
                $"letters of administration recorded by the estate driver, day {ctx.CurrentDayIndex} (intestate)",
                ctx.Population, ctx.CurrentDayIndex, diag);
            if (admin == null || !admin.Accepted)
                return $"EstateLifecycleDriver: administrator grant for P{candidate} on estate {estate.EstateId} did not take — held.";
            prog.FiduciaryPersonId = candidate;
            return null;
        }

        private int ChooseIntestateFiduciary(Estate estate, List<string> diag)
        {
            int spouse = -1;
            int eldestChild = -1;
            foreach (HeirShare heir in estate.Heirs)
            {
                if (heir == null || heir.IsLifeInterest || heir.PersonId < 0) continue;
                if (!IsAdult(heir.PersonId)) continue;
                if (string.Equals(heir.Relation, "spouse", StringComparison.Ordinal))
                {
                    spouse = heir.PersonId;
                    break;
                }
                if (string.Equals(heir.Relation, "child", StringComparison.Ordinal))
                {
                    if (eldestChild < 0 || heir.PersonId < eldestChild) eldestChild = heir.PersonId;
                }
            }
            if (spouse >= 0)
            {
                diag.Add($"EstateLifecycleDriver: surviving spouse P{spouse} takes letters of administration.");
                return spouse;
            }
            if (eldestChild >= 0)
            {
                diag.Add($"EstateLifecycleDriver: eldest adult child P{eldestChild} takes letters of administration.");
                return eldestChild;
            }
            return -1;
        }

        private void InventoryParcels(Estate estate, int fiduciary, int day, List<string> diag)
        {
            if (ctx.Titles == null)
            {
                diag.Add("EstateLifecycleDriver: no title authority wired — parcel enumeration skipped (not invented).");
                return;
            }
            IReadOnlyList<string> held = ctx.Titles.ParcelsHeldBy(estate.DecedentName);
            foreach (string parcelId in held)
            {
                if (string.IsNullOrWhiteSpace(parcelId)) continue;
                string problem = ctx.Estates.RegisterDecedentParcel(estate, parcelId, ctx.Titles, diag);
                if (problem != null)
                {
                    diag.Add($"EstateLifecycleDriver: {problem}");
                    continue;
                }
            }
            foreach (string parcelId in new List<string>(estate.ParcelIds))
            {
                if (HasInventoryLine(estate.EstateId, EstateInventoryLineKind.Parcel, parcelId)) continue;
                string problem = ctx.Administration.MarshalParcel(
                    estate, fiduciary, parcelId, $"parcel '{parcelId}' held by the decedent per the title chain",
                    0, day, diag);
                if (problem != null) diag.Add($"EstateLifecycleDriver: {problem}");
            }
        }

        private bool HasInventoryLine(string estateId, EstateInventoryLineKind kind, string referenceId)
        {
            foreach (EstateInventoryLine line in ctx.Administration.InventoryFor(estateId))
            {
                if (line != null && line.Kind == kind
                    && string.Equals(line.ReferenceId, referenceId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The person-keyed ownership seam: a business instance whose owner
        /// identity is Npc with the decedent's person id is the decedent's
        /// business interest. It is recorded on the estate and marshalled;
        /// ownership moves only in the distribution stage.
        /// </summary>
        private void InventoryBusinessInterests(Estate estate, EstateLifecycleProgress prog, int fiduciary, int day, List<string> diag)
        {
            if (ctx.Businesses == null || ctx.Businesses.Count == 0)
            {
                diag.Add("EstateLifecycleDriver: no business instances wired — business-interest enumeration skipped (not invented).");
                return;
            }
            foreach (KeyValuePair<string, BusinessInstanceState> entry in ctx.Businesses)
            {
                BusinessInstanceState state = entry.Value;
                if (state == null || state.Owner == null) continue;
                if (state.Owner.OwnerKind != BusinessOwnerKind.Npc) continue;
                if (state.Owner.PersonId != estate.DecedentPersonId) continue;
                string instanceId = entry.Key;
                if (!estate.BusinessInstanceIds.Contains(instanceId))
                {
                    estate.BusinessInstanceIds.Add(instanceId);
                    diag.Add($"EstateLifecycleDriver: business interest '{instanceId}' recorded on estate {estate.EstateId} " +
                        $"(person-keyed owner P{estate.DecedentPersonId}).");
                }
                if (HasInventoryLine(estate.EstateId, EstateInventoryLineKind.BusinessInterest, instanceId)) continue;
                string problem = ctx.Administration.MarshalBusinessInterest(
                    estate, fiduciary, instanceId,
                    $"business interest '{instanceId}' owned by the decedent (person-keyed)",
                    0, day, diag);
                if (problem != null) diag.Add($"EstateLifecycleDriver: {problem}");
            }
        }

        private void InventoryDeposits(Estate estate, int fiduciary, int day, List<string> diag)
        {
            if (ctx.DepositBooks == null || ctx.DepositBooks.Count == 0)
            {
                diag.Add("EstateLifecycleDriver: no deposit books wired — deposit enumeration skipped (not invented).");
                return;
            }
            foreach (BankDepositLedger book in ctx.DepositBooks)
            {
                if (book == null) continue;
                foreach (DepositAccount account in book.Accounts)
                {
                    if (account == null) continue;
                    if (!string.Equals(account.DepositorName, estate.DecedentName, StringComparison.Ordinal)) continue;
                    if (HasInventoryLine(estate.EstateId, EstateInventoryLineKind.DepositAccount, account.AccountId)) continue;
                    string problem = ctx.Administration.MarshalDepositAccount(
                        estate, fiduciary, book, account.AccountId, day, diag);
                    if (problem != null) diag.Add($"EstateLifecycleDriver: {problem}");
                }
            }
        }

        private void InventoryReceivables(Estate estate, int fiduciary, int day, List<string> diag)
        {
            if (ctx.Credit == null)
            {
                diag.Add("EstateLifecycleDriver: no credit registry wired — receivables enumeration skipped (not invented).");
                return;
            }
            foreach (ICreditInstrument instrument in ctx.Credit.AllInstruments())
            {
                if (!(instrument is PromissoryNote note)) continue;
                if (instrument.Status == CreditInstrumentStatus.Satisfied
                    || instrument.Status == CreditInstrumentStatus.Released) continue;
                bool decedentIsPayee = string.Equals(note.PayeeName, estate.DecedentName, StringComparison.Ordinal);
                bool decedentIsHolder = !string.IsNullOrWhiteSpace(note.HolderName)
                    && string.Equals(note.HolderName, estate.DecedentName, StringComparison.Ordinal);
                if (!decedentIsPayee && !decedentIsHolder) continue;
                if (HasInventoryLine(estate.EstateId, EstateInventoryLineKind.NoteReceivable, instrument.InstrumentId.ToString())) continue;
                string problem = ctx.Administration.MarshalNoteReceivable(
                    estate, fiduciary, instrument, Math.Max(0, note.PrincipalCents), day, diag);
                if (problem != null) diag.Add($"EstateLifecycleDriver: {problem}");
            }
        }

        private void InventoryLivestock(Estate estate, int fiduciary, int day, List<string> diag)
        {
            if (ctx.Animals == null)
            {
                diag.Add("EstateLifecycleDriver: no animal registry wired — livestock enumeration skipped (not invented).");
                return;
            }
            foreach (AnimalState animal in ctx.Animals.ActiveAnimals)
            {
                if (animal == null || !animal.IsActive) continue;
                if (animal.OwnerKind != AnimalOwnerKind.Person) continue;
                if (!IsPersonOwnerId(animal.OwnerId, estate.DecedentPersonId)) continue;
                string description = $"{animal.Species} ({animal.Breed}), animal {animal.AnimalId}";
                if (HasInventoryLine(estate.EstateId, EstateInventoryLineKind.PersonalProperty, description)) continue;
                string problem = ctx.Administration.MarshalPersonalProperty(
                    estate, fiduciary, description, 0, day, diag);
                if (problem != null) diag.Add($"EstateLifecycleDriver: {problem}");
            }
        }

        #endregion

        #region Claims

        /// <summary>
        /// Adjudicates the estate's debts in the hard order: discovers the
        /// decedent's real obligations (T2A instruments where the decedent is
        /// the obligor), collects the decedent's bank deposits into the estate
        /// account, records validated creditor claims, and disburses them in
        /// rank order. Debts are never erased; shortfalls are never auto-paid;
        /// an insolvent estate invents no funds. Idempotent.
        /// </summary>
        public string AdjudicateClaims(string estateId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            Estate estate = RequireEstate(estateId, diag);
            if (estate == null) return $"EstateLifecycleDriver.AdjudicateClaims: unknown estate '{estateId}'.";
            if (estate.Status == EstateStatus.Closed)
                return $"EstateLifecycleDriver.AdjudicateClaims: estate {estateId} is closed — no claims on a closed estate.";
            EstateLifecycleProgress prog = Track(estateId);
            if (prog.Stage >= EstateLifecycleStage.ClaimsAdjudicated)
            {
                diag.Add($"EstateLifecycleDriver: estate {estateId} claims already adjudicated — no-op.");
                return null;
            }
            if (prog.Stage < EstateLifecycleStage.Inventoried)
            {
                string inventoryProblem = InventoryEstate(estateId, diag);
                if (inventoryProblem != null) return inventoryProblem;
            }

            int day = ctx.CurrentDayIndex;
            int fiduciary = prog.FiduciaryPersonId;
            DiscoverEstateDebts(estate, prog, diag);
            CollectDeposits(estate, prog, fiduciary, day, diag);
            RecordClaims(estate, prog, fiduciary, day, diag);
            DisburseClaims(estate, prog, fiduciary, day, diag);

            if (estate.EstateDebtIds.Count == 0 && AllClaimsSettled(estateId))
            {
                prog.Stage = EstateLifecycleStage.ClaimsAdjudicated;
                diag.Add($"EstateLifecycleDriver: estate {estateId} claims adjudicated — every recorded debt settled.");
                return null;
            }

            var pending = new List<string>();
            foreach (string debtId in estate.EstateDebtIds) pending.Add(debtId);
            foreach (EstateCreditorClaim claim in ctx.Administration.ClaimsFor(estateId))
            {
                if (claim != null && !claim.Settled && !pending.Contains(claim.DebtInstrumentId))
                    pending.Add(claim.DebtInstrumentId);
            }
            AddHold(prog, $"claims pending ({pending.Count}): {string.Join(", ", pending.ToArray())} — creditors before heirs; the estate waits",
                diag);
            return $"EstateLifecycleDriver.AdjudicateClaims: estate {estateId} held — {pending.Count} claim(s) unsettled.";
        }

        /// <summary>
        /// Discovers the decedent's real debts. T2A instruments where the
        /// decedent is the obligor become estate debts. Financial obligations
        /// with no T2A paper are recorded on the estate (debts are never
        /// erased) and held for instrument-backed adjudication — an explicit
        /// hold, never an invented rule.
        /// </summary>
        private void DiscoverEstateDebts(Estate estate, EstateLifecycleProgress prog, List<string> diag)
        {
            if (ctx.Credit == null && ctx.Obligations == null)
            {
                diag.Add("EstateLifecycleDriver: no credit registry and no obligation authority wired — debt discovery skipped (debts are not assumed absent).");
                AddHold(prog, "debt discovery had no registries — the estate cannot certify it found every debt", diag);
                return;
            }

            var paperDebtIds = new HashSet<string>(StringComparer.Ordinal);
            if (ctx.Credit != null)
            {
                foreach (ICreditInstrument instrument in ctx.Credit.AllInstruments())
                {
                    if (instrument == null) continue;
                    if (instrument.Status == CreditInstrumentStatus.Satisfied
                        || instrument.Status == CreditInstrumentStatus.Released) continue;
                    if (!IsDecedentObligor(instrument, estate.DecedentName)) continue;
                    string instrumentId = instrument.InstrumentId.ToString();
                    paperDebtIds.Add(instrumentId);
                    ctx.Estates.RecordEstateDebt(estate, instrumentId, diag);
                }
            }

            if (ctx.Obligations != null)
            {
                foreach (FinancialObligation obligation in ctx.Obligations.Obligations)
                {
                    if (obligation == null || obligation.Settled) continue;
                    if (!string.Equals(obligation.Debtor, estate.DecedentName, StringComparison.Ordinal)) continue;
                    bool hasPaper = false;
                    if (ctx.Credit != null)
                    {
                        foreach (ICreditInstrument instrument in ctx.Credit.AllInstruments())
                        {
                            if (instrument != null
                                && string.Equals(InstrumentObligationId(instrument), obligation.ObligationId, StringComparison.Ordinal))
                            { hasPaper = true; break; }
                        }
                    }
                    if (hasPaper) continue; // claimed through its T2A instrument, never twice
                    ctx.Estates.RecordEstateDebt(estate, obligation.ObligationId, diag);
                    if (!paperDebtIds.Contains(obligation.ObligationId))
                    {
                        AddHold(prog, $"obligation '{obligation.ObligationId}' ({obligation.Kind}, " +
                            $"{obligation.TotalOutstandingCents}c) has no T2A instrument — recorded on the estate, " +
                            "held for instrument-backed adjudication; beneficiaries are never made personally liable",
                            diag);
                    }
                }
            }
        }

        private bool IsDecedentObligor(ICreditInstrument instrument, string decedentName)
        {
            if (instrument is PromissoryNote note)
                return string.Equals(note.MakerName, decedentName, StringComparison.Ordinal);
            if (instrument is SellerFinanceNote sellerNote)
                return string.Equals(sellerNote.BuyerName, decedentName, StringComparison.Ordinal);
            if (instrument is MortgageDeed mortgage)
                return string.Equals(mortgage.BorrowerName, decedentName, StringComparison.Ordinal);
            if (instrument is PropertyLien lien)
                return string.Equals(lien.DebtorName, decedentName, StringComparison.Ordinal);
            if (instrument is GuarantyAgreement guaranty)
                return guaranty.Status == CreditInstrumentStatus.Called
                    && string.Equals(guaranty.GuarantorName, decedentName, StringComparison.Ordinal);
            return false;
        }

        /// <summary>
        /// Collects the decedent's marshalled bank deposits into the estate
        /// account through real withdrawals. The bank's own liquidity rules
        /// apply; a refused withdrawal is a hold, never a faked receipt.
        /// </summary>
        private void CollectDeposits(Estate estate, EstateLifecycleProgress prog, int fiduciary, int day, List<string> diag)
        {
            List<EstateInventoryLine> lines = ctx.Administration.InventoryFor(estate.EstateId);
            foreach (EstateInventoryLine line in lines)
            {
                if (line == null || line.Kind != EstateInventoryLineKind.DepositAccount) continue;
                if (prog.CollectedDepositAccountIds.Contains(line.ReferenceId)) continue;
                DepositAccount account = FindDepositAccount(line.ReferenceId);
                if (account == null)
                {
                    AddHold(prog, $"deposit '{line.ReferenceId}' not found in any wired deposit book — cannot collect", diag);
                    continue;
                }
                BankDepositLedger book = FindDepositBook(line.ReferenceId);
                int amount = Math.Max(0, account.BalanceCents);
                if (amount <= 0)
                {
                    prog.CollectedDepositAccountIds.Add(line.ReferenceId);
                    diag.Add($"EstateLifecycleDriver: deposit '{line.ReferenceId}' holds {amount}c — nothing to collect.");
                    continue;
                }
                string refusal = book.Withdraw(line.ReferenceId, amount, day, false, diag);
                if (refusal != null)
                {
                    AddHold(prog, $"deposit '{line.ReferenceId}' withdrawal refused ({refusal}) — the estate waits on the bank", diag);
                    continue;
                }
                refusal = ctx.Administration.RecordEstateReceipt(
                    estate, fiduciary, amount,
                    $"collected deposit '{line.ReferenceId}' of {estate.DecedentName} at {book.BankName}",
                    day, diag);
                if (refusal != null)
                {
                    AddHold(prog, $"deposit '{line.ReferenceId}' withdrawn but the receipt did not record ({refusal})", diag);
                    continue;
                }
                prog.CollectedDepositAccountIds.Add(line.ReferenceId);
            }
        }

        private DepositAccount FindDepositAccount(string accountId)
        {
            if (ctx.DepositBooks == null) return null;
            foreach (BankDepositLedger book in ctx.DepositBooks)
            {
                if (book == null) continue;
                foreach (DepositAccount account in book.Accounts)
                {
                    if (account != null && string.Equals(account.AccountId, accountId, StringComparison.Ordinal))
                        return account;
                }
            }
            return null;
        }

        private BankDepositLedger FindDepositBook(string accountId)
        {
            if (ctx.DepositBooks == null) return null;
            foreach (BankDepositLedger book in ctx.DepositBooks)
            {
                if (book == null) continue;
                foreach (DepositAccount account in book.Accounts)
                {
                    if (account != null && string.Equals(account.AccountId, accountId, StringComparison.Ordinal))
                        return book;
                }
            }
            return null;
        }

        private void RecordClaims(Estate estate, EstateLifecycleProgress prog, int fiduciary, int day, List<string> diag)
        {
            List<EstateCreditorClaim> existing = ctx.Administration.ClaimsFor(estate.EstateId);
            foreach (string debtId in new List<string>(estate.EstateDebtIds))
            {
                bool alreadyClaimed = false;
                foreach (EstateCreditorClaim prior in existing)
                {
                    if (prior != null && string.Equals(prior.DebtInstrumentId, debtId, StringComparison.Ordinal))
                    { alreadyClaimed = true; break; }
                }
                if (alreadyClaimed) continue;

                ICreditInstrument instrument = FindInstrument(debtId);
                if (instrument == null) continue; // paperless obligations are on hold, not claimable by fiat

                EstateClaimRank rank = instrument is MortgageDeed || instrument is PropertyLien
                    ? EstateClaimRank.SecuredDebt
                    : EstateClaimRank.UnsecuredCreditor;
                int amount = ClaimAmountCents(instrument);
                if (amount <= 0)
                {
                    diag.Add($"EstateLifecycleDriver: '{debtId}' records {amount}c — nothing to claim.");
                    ctx.Estates.MarkDebtSettled(estate, debtId, diag);
                    continue;
                }
                EstateCreditorClaim claim = ctx.Administration.RecordCreditorClaim(
                    estate, debtId, rank, amount, instrument, fiduciary, day, diag);
                if (claim == null)
                {
                    AddHold(prog, $"claim on '{debtId}' did not validate — held for review", diag);
                }
            }
        }

        private ICreditInstrument FindInstrument(string instrumentId)
        {
            if (ctx.Credit == null || string.IsNullOrWhiteSpace(instrumentId)) return null;
            foreach (ICreditInstrument instrument in ctx.Credit.AllInstruments())
            {
                if (instrument != null && string.Equals(instrument.InstrumentId.ToString(), instrumentId, StringComparison.Ordinal))
                    return instrument;
            }
            return null;
        }

        private int ClaimAmountCents(ICreditInstrument instrument)
        {
            if (instrument == null) return 0;
            string obligationId = InstrumentObligationId(instrument);
            if (ctx.Obligations != null && !string.IsNullOrWhiteSpace(obligationId))
            {
                FinancialObligation obligation = ctx.Obligations.Find(obligationId);
                if (obligation != null) return Math.Max(0, obligation.TotalOutstandingCents);
            }
            if (instrument is PromissoryNote note) return Math.Max(0, note.PrincipalCents);
            if (instrument is SellerFinanceNote sellerNote) return Math.Max(0, sellerNote.FinancedCents);
            if (instrument is MortgageDeed mortgage) return Math.Max(0, mortgage.PrincipalCents);
            if (instrument is PropertyLien lien) return Math.Max(0, lien.AmountCents);
            if (instrument is GuarantyAgreement guaranty) return Math.Max(0, guaranty.CalledAmountCents);
            return 0;
        }

        private bool AllClaimsSettled(string estateId)
        {
            foreach (EstateCreditorClaim claim in ctx.Administration.ClaimsFor(estateId))
            {
                if (claim != null && !claim.Settled) return false;
            }
            return true;
        }

        /// <summary>
        /// Disburses validated claims in the hard rank order. The estate
        /// account pays the creditor; the shared obligation is settled through
        /// the credit registry so the financial authority's books stay honest.
        /// Shortfalls are refused loudly — never auto-paid, never partial.
        /// </summary>
        private void DisburseClaims(Estate estate, EstateLifecycleProgress prog, int fiduciary, int day, List<string> diag)
        {
            List<EstateCreditorClaim> ordered = ctx.Administration.ClaimsFor(estate.EstateId);
            ordered.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            EstateAccount account = ctx.Administration.AccountFor(estate.EstateId);
            string accountId = account != null ? account.AccountId : $"estate-{estate.EstateId}";
            foreach (EstateCreditorClaim claim in ordered)
            {
                if (claim == null || claim.Settled) continue;
                string refusal = ctx.Administration.DisburseForClaim(
                    estate, ctx.Estates, claim, fiduciary, day, ctx.Contests, diag);
                if (refusal != null)
                {
                    diag.Add($"EstateLifecycleDriver: {refusal}");
                    continue;
                }
                if (ctx.Credit != null)
                {
                    FinancialPaymentRecord payment = ctx.Credit.ApplyInstrumentPayment(
                        claim.DebtInstrumentId, claim.PaidCents, day,
                        $"estate account {accountId}", claim.CreditorName, diag);
                    if (payment == null)
                    {
                        diag.Add($"EstateLifecycleDriver: note — the shared obligation for '{claim.DebtInstrumentId}' " +
                            "did not take the estate's payment; the estate record stands settled, the obligation book needs review.");
                    }
                }
            }
        }

        #endregion

        #region Distribution

        /// <summary>
        /// Distributes the estate after every debt is settled: parcels and
        /// business interests to heirs through the title and ownership
        /// authorities, residue cash to heirs' household ledgers. Sole-heir
        /// and multiple-heir cases never duplicate an asset. Minor heirs take
        /// through custodial arrangements, never with contracting authority.
        /// Idempotent.
        /// </summary>
        public string DistributeEstate(string estateId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            Estate estate = RequireEstate(estateId, diag);
            if (estate == null) return $"EstateLifecycleDriver.DistributeEstate: unknown estate '{estateId}'.";
            if (estate.Status == EstateStatus.Closed)
                return $"EstateLifecycleDriver.DistributeEstate: estate {estateId} is closed — a closed estate is never re-distributed.";
            EstateLifecycleProgress prog = Track(estateId);
            if (prog.Stage >= EstateLifecycleStage.Distributed)
            {
                diag.Add($"EstateLifecycleDriver: estate {estateId} already distributed — no-op.");
                return null;
            }
            if (prog.Stage < EstateLifecycleStage.ClaimsAdjudicated)
            {
                string claimsProblem = AdjudicateClaims(estateId, diag);
                if (claimsProblem != null) return claimsProblem;
            }
            if (estate.EstateDebtIds.Count > 0 || !AllClaimsSettled(estateId))
                return $"EstateLifecycleDriver.DistributeEstate: {estate.EstateDebtIds.Count} estate debts unsettled — creditors before heirs.";

            int day = ctx.CurrentDayIndex;
            int fiduciary = prog.FiduciaryPersonId;
            if (fiduciary < 0)
                return $"EstateLifecycleDriver.DistributeEstate: estate {estateId} has no fiduciary — held.";

            string problem;
            if (!string.IsNullOrWhiteSpace(estate.TestateWillId))
                problem = DistributeTestate(estate, prog, fiduciary, day, diag);
            else
                problem = DistributeIntestate(estate, prog, fiduciary, day, diag);
            if (problem != null) return problem;

            DistributeResidueCash(estate, prog, fiduciary, day, diag);

            EstateAccount account = ctx.Administration.AccountFor(estateId);
            if (account != null && account.BalanceCents > 0)
            {
                AddHold(prog, $"estate account still holds {account.BalanceCents}c after distribution — residue not fully placed", diag);
                return $"EstateLifecycleDriver.DistributeEstate: estate {estateId} held — residue cash unplaced.";
            }
            if (prog.LegalHolds.Count > 0)
            {
                diag.Add($"EstateLifecycleDriver: estate {estateId} distributed its assets but carries {prog.LegalHolds.Count} legal hold(s) — closure waits.");
                prog.Stage = EstateLifecycleStage.Distributed;
                return null;
            }
            prog.Stage = EstateLifecycleStage.Distributed;
            diag.Add($"EstateLifecycleDriver: estate {estateId} distributed.");
            return null;
        }

        private string DistributeTestate(Estate estate, EstateLifecycleProgress prog, int fiduciary, int day, List<string> diag)
        {
            if (ctx.Probate == null)
            {
                AddHold(prog, "testate estate but no probate service wired — bequests wait for probate", diag);
                return $"EstateLifecycleDriver.DistributeEstate: estate {estate.EstateId} is testate but cannot be probated here — held.";
            }
            Will will = ctx.Probate.Get(estate.TestateWillId);
            if (will == null || !will.Probated || will.Contested || will.Revoked || will.InvalidatedByContest)
            {
                AddHold(prog, $"will {estate.TestateWillId} is not available for distribution (missing, unprobated, contested, revoked, or invalidated)", diag);
                return $"EstateLifecycleDriver.DistributeEstate: estate {estate.EstateId} held — the will cannot convey today.";
            }
            foreach (Bequest bequest in will.Bequests)
            {
                if (bequest == null || bequest.Distributed || bequest.InvalidatedByContest || bequest.HeldForWitnessConflict)
                    continue;
                string problem = null;
                switch (bequest.Kind)
                {
                    case BequestKind.Parcel:
                        problem = ctx.Administration.AdministerBequestDistribution(
                            estate, ctx.Probate, will, bequest, fiduciary,
                            ctx.Ids, ctx.Titles, day, ctx.Contests, diag);
                        break;
                    case BequestKind.CashAmount:
                        problem = DistributeCashBequest(estate, prog, will, bequest, fiduciary, day, diag);
                        break;
                    case BequestKind.BusinessInterest:
                        problem = TransferBusinessInterest(estate, prog, bequest.BusinessInstanceId,
                            bequest.BeneficiaryPersonId, fiduciary, day, diag, true);
                        if (problem == null) bequest.Distributed = true;
                        break;
                    default:
                        AddHold(prog, $"{bequest.Kind} bequest to P{bequest.BeneficiaryPersonId} needs its custodian's transfer — held for physical delivery", diag);
                        problem = $"held: {bequest.Kind} bequest";
                        break;
                }
                if (problem != null)
                {
                    AddHold(prog, $"bequest to P{bequest.BeneficiaryPersonId} ({bequest.Kind}) refused: {problem}", diag);
                    return $"EstateLifecycleDriver.DistributeEstate: estate {estate.EstateId} held — a bequest could not distribute.";
                }
            }
            return null;
        }

        private string DistributeCashBequest(Estate estate, EstateLifecycleProgress prog, Will will, Bequest bequest,
            int fiduciary, int day, List<string> diag)
        {
            EstateAccount account = ctx.Administration.AccountFor(estate.EstateId);
            if (account == null || account.BalanceCents < bequest.CashCents)
            {
                return $"cash bequest of {bequest.CashCents}c to P{bequest.BeneficiaryPersonId} exceeds the estate account — never auto-paid";
            }
            string problem = CreditHeirHousehold(bequest.BeneficiaryPersonId, bequest.CashCents,
                $"cash bequest under will {will.WillId} (estate {estate.EstateId})", estate.EstateId, fiduciary, day, diag);
            if (problem != null) return problem;
            account.Entries.Add(new EstateAccountEntry
            {
                Kind = EstateAccountEntryKind.Disbursement,
                DayIndex = day,
                AmountCents = bequest.CashCents,
                Memo = $"cash bequest to P{bequest.BeneficiaryPersonId} ({bequest.BeneficiaryName}) under will {will.WillId}",
                RecordedByPersonId = fiduciary,
            });
            bequest.Distributed = true;
            ctx.Administration.RecordExecutorAct(estate, fiduciary,
                $"disbursed cash bequest {bequest.CashCents}c to P{bequest.BeneficiaryPersonId} under will {will.WillId}", day, diag);
            return null;
        }

        private string DistributeIntestate(Estate estate, EstateLifecycleProgress prog, int fiduciary, int day, List<string> diag)
        {
            var owners = new List<HeirShare>();
            foreach (HeirShare heir in estate.Heirs)
            {
                if (heir != null && !heir.IsLifeInterest && heir.PersonId >= 0) owners.Add(heir);
            }
            if (owners.Count == 0)
            {
                AddHold(prog, "no ownership heirs on record — the estate is held and flagged (escheat is a legal proceeding, never automatic)", diag);
                return $"EstateLifecycleDriver.DistributeEstate: estate {estate.EstateId} held — no heirs.";
            }

            // Parcels already distributed by an earlier partial run count toward
            // each heir's share, so a resumed distribution never double-places.
            var assignedParcels = new Dictionary<int, int>();
            foreach (HeirShare heir in owners) assignedParcels[heir.PersonId] = 0;
            foreach (string doneId in prog.DistributedParcelIds)
            {
                // The parcel left the estate's list; its taker is on the title chain.
                // Re-deriving the taker keeps the deficit honest across restarts.
                string holder = ctx.Titles != null ? ctx.Titles.CurrentHolder(doneId) : string.Empty;
                foreach (HeirShare heir in owners)
                {
                    if (string.Equals(HeirName(heir.PersonId), holder, StringComparison.Ordinal))
                    { assignedParcels[heir.PersonId]++; break; }
                }
            }

            foreach (string parcelId in new List<string>(estate.ParcelIds))
            {
                if (prog.DistributedParcelIds.Contains(parcelId)) continue;
                HeirShare taker = ChooseParcelTaker(owners, assignedParcels);
                string problem = ctx.Administration.AdministerParcelDistribution(
                    estate, ctx.Estates, parcelId, taker.PersonId, HeirName(taker.PersonId),
                    fiduciary, ctx.Ids, ctx.Titles, day, ctx.Contests, diag);
                if (problem != null)
                {
                    AddHold(prog, $"parcel '{parcelId}' distribution refused: {problem}", diag);
                    return $"EstateLifecycleDriver.DistributeEstate: estate {estate.EstateId} held — parcel distribution refused.";
                }
                prog.DistributedParcelIds.Add(parcelId);
                if (!assignedParcels.ContainsKey(taker.PersonId)) assignedParcels[taker.PersonId] = 0;
                assignedParcels[taker.PersonId]++;
                if (!IsAdult(taker.PersonId))
                {
                    prog.MinorCustodies.Add(new EstateMinorCustody
                    {
                        MinorPersonId = taker.PersonId,
                        AssetKind = "parcel",
                        AssetRef = parcelId,
                        ShareNumerator = taker.ShareNumerator,
                        ShareDenominator = taker.ShareDenominator,
                        CustodianPersonId = fiduciary,
                        Note = $"minor's parcel held in custodial arrangement of P{fiduciary} until majority",
                    });
                    diag.Add($"EstateLifecycleDriver: parcel '{parcelId}' vested in minor P{taker.PersonId} under custodial arrangement of P{fiduciary}.");
                }
            }

            foreach (string businessId in new List<string>(estate.BusinessInstanceIds))
            {
                if (prog.TransferredBusinessIds.Contains(businessId)) continue;
                string problem = TransferBusinessIntestate(estate, prog, owners, businessId, fiduciary, day, diag);
                if (problem != null) return problem;
                prog.TransferredBusinessIds.Add(businessId);
            }
            return null;
        }

        /// <summary>
        /// Picks the heir for the next whole parcel: the largest remaining
        /// deficit between their intestate share of the parcel count and what
        /// they have already been assigned. Parcels are indivisible in the
        /// title chain, so assignment is greedy and never duplicates. Ties
        /// break to the lowest person id — deterministic, never invented.
        /// </summary>
        private HeirShare ChooseParcelTaker(List<HeirShare> owners, Dictionary<int, int> assignedParcels)
        {
            int placed = 0;
            foreach (int count in assignedParcels.Values) placed += count;
            HeirShare best = owners[0];
            double bestDeficit = double.NegativeInfinity;
            foreach (HeirShare heir in owners)
            {
                double share = heir.ShareDenominator > 0
                    ? (double)heir.ShareNumerator / heir.ShareDenominator : 0d;
                int assigned = assignedParcels.TryGetValue(heir.PersonId, out int count) ? count : 0;
                double deficit = share * (placed + 1) - assigned;
                if (deficit > bestDeficit + 1e-9
                    || (Math.Abs(deficit - bestDeficit) <= 1e-9 && heir.PersonId < best.PersonId))
                {
                    best = heir;
                    bestDeficit = deficit;
                }
            }
            return best;
        }

        /// <summary>
        /// Business continuity: a sole heir takes the business outright. With
        /// several heirs the asset is never duplicated: the adult heir with
        /// the largest share operates it as interim manager while every
        /// heir's undivided share is recorded. A minor sole heir's interest
        /// is preserved in custody with the fiduciary managing. The business's
        /// cash, inventory, debts, employments, and obligations are untouched —
        /// death never erases them.
        /// </summary>
        private string TransferBusinessIntestate(Estate estate, EstateLifecycleProgress prog, List<HeirShare> owners,
            string businessId, int fiduciary, int day, List<string> diag)
        {
            if (owners.Count == 1)
            {
                HeirShare heir = owners[0];
                if (IsAdult(heir.PersonId))
                    return TransferBusinessInterest(estate, prog, businessId, heir.PersonId, fiduciary, day, diag, false);
                prog.UndividedBusinessShares.Add(new EstateUndividedBusinessShare
                {
                    BusinessInstanceId = businessId,
                    HeirPersonId = heir.PersonId,
                    ShareNumerator = heir.ShareNumerator,
                    ShareDenominator = heir.ShareDenominator,
                    IsInterimManager = false,
                });
                string problem = TransferBusinessInterest(estate, prog, businessId, fiduciary, fiduciary, day, diag, false);
                if (problem != null) return problem;
                prog.MinorCustodies.Add(new EstateMinorCustody
                {
                    MinorPersonId = heir.PersonId,
                    AssetKind = "business-interest",
                    AssetRef = businessId,
                    ShareNumerator = heir.ShareNumerator,
                    ShareDenominator = heir.ShareDenominator,
                    CustodianPersonId = fiduciary,
                    Note = $"minor's business interest managed by fiduciary P{fiduciary} until majority or a qualified replacement takes over",
                });
                AddHold(prog, $"business '{businessId}' vested in minor P{heir.PersonId} — the business may be unable to operate without a qualified replacement", diag);
                return null;
            }

            HeirShare manager = ChooseParcelTaker(owners, new Dictionary<int, int>());
            foreach (HeirShare heir in owners)
            {
                prog.UndividedBusinessShares.Add(new EstateUndividedBusinessShare
                {
                    BusinessInstanceId = businessId,
                    HeirPersonId = heir.PersonId,
                    ShareNumerator = heir.ShareNumerator,
                    ShareDenominator = heir.ShareDenominator,
                    IsInterimManager = heir.PersonId == manager.PersonId,
                });
            }
            string refused = TransferBusinessInterest(estate, prog, businessId, manager.PersonId, fiduciary, day, diag, false);
            if (refused != null) return refused;
            AddHold(prog, $"business '{businessId}' has {owners.Count} undivided heir interests — partition or buyout is a legal proceeding; " +
                $"P{manager.PersonId} operates as interim manager", diag);
            return null;
        }

        /// <summary>
        /// The person-keyed ownership path: the business instance's owner
        /// identity becomes the Npc identity of a real person. Everything else
        /// about the business — cash, inventory, debts, staff, customers,
        /// suppliers, premises — is preserved untouched.
        /// </summary>
        private string TransferBusinessInterest(Estate estate, EstateLifecycleProgress prog, string businessId,
            int newOwnerPersonId, int fiduciary, int day, List<string> diag, bool isTestate)
        {
            if (ctx.Businesses == null || !ctx.Businesses.TryGetValue(businessId, out BusinessInstanceState state) || state == null)
            {
                AddHold(prog, $"business '{businessId}' is not among the wired business instances — ownership cannot move by fiat", diag);
                return $"business '{businessId}' not found among wired instances";
            }
            if (state.Owner == null || state.Owner.OwnerKind != BusinessOwnerKind.Npc
                || state.Owner.PersonId != estate.DecedentPersonId)
            {
                AddHold(prog, $"business '{businessId}' is no longer person-keyed to the decedent — ownership cannot move by fiat", diag);
                return $"business '{businessId}' is not the decedent's person-keyed interest";
            }
            PersonState person = ctx.Population != null ? ctx.Population.GetPerson(newOwnerPersonId) : null;
            string displayName = person != null ? person.DisplayName : $"P{newOwnerPersonId}";
            string surname = person != null && !string.IsNullOrWhiteSpace(person.lastName) ? person.lastName : displayName;
            state.SetOwner(BusinessOwnerIdentity.Npc(newOwnerPersonId, displayName, surname));
            state.SetControlState(BusinessControlState.ManagerRun);
            ctx.Administration.RecordExecutorAct(estate, fiduciary,
                $"{(isTestate ? "testate" : "intestate")} transfer of business '{businessId}' to P{newOwnerPersonId} ({displayName}) — " +
                "assets, obligations, employments, and customer and supplier relations preserved; interim management engaged",
                day, diag);
            diag.Add($"EstateLifecycleDriver: business '{businessId}' now person-keyed to P{newOwnerPersonId} (ManagerRun).");
            return null;
        }

        /// <summary>
        /// Splits the estate account's residue among the ownership heirs by
        /// their intestate fractions. Every cent is placed: the account's own
        /// disbursement entries mirror each heir's household-ledger credit, so
        /// the books reconcile and no cash is invented. A minor heir's share
        /// credits the household they live in, with the minor's interest
        /// recorded in custody under the guardian's administration.
        /// </summary>
        private void DistributeResidueCash(Estate estate, EstateLifecycleProgress prog, int fiduciary, int day, List<string> diag)
        {
            EstateAccount account = ctx.Administration.AccountFor(estate.EstateId);
            if (account == null || account.BalanceCents <= 0) return;

            var owners = new List<HeirShare>();
            foreach (HeirShare heir in estate.Heirs)
            {
                if (heir != null && !heir.IsLifeInterest && heir.PersonId >= 0
                    && !prog.ResidueDisbursedHeirIds.Contains(heir.PersonId)) owners.Add(heir);
            }
            if (owners.Count == 0)
            {
                diag.Add($"EstateLifecycleDriver: estate {estate.EstateId} has residue {account.BalanceCents}c but no unpaid ownership heirs — held in the account.");
                return;
            }

            int balance = account.BalanceCents;
            var amounts = new Dictionary<int, int>();
            int placed = 0;
            HeirShare largest = ChooseParcelTaker(owners, new Dictionary<int, int>());
            foreach (HeirShare heir in owners)
            {
                int amount = heir.ShareDenominator > 0
                    ? (int)((long)balance * heir.ShareNumerator / heir.ShareDenominator) : 0;
                amounts[heir.PersonId] = amount;
                placed += amount;
            }
            amounts[largest.PersonId] = amounts[largest.PersonId] + (balance - placed);

            foreach (HeirShare heir in owners)
            {
                int amount = amounts[heir.PersonId];
                if (amount <= 0)
                {
                    prog.ResidueDisbursedHeirIds.Add(heir.PersonId);
                    continue;
                }
                string problem = CreditHeirHousehold(heir.PersonId, amount,
                    $"intestate residue share ({heir.ShareNumerator}/{heir.ShareDenominator}) from estate {estate.EstateId} of {estate.DecedentName}",
                    estate.EstateId, fiduciary, day, diag);
                if (problem != null)
                {
                    AddHold(prog, $"residue share {amount}c for P{heir.PersonId} could not be placed: {problem}", diag);
                    continue;
                }
                account.Entries.Add(new EstateAccountEntry
                {
                    Kind = EstateAccountEntryKind.Disbursement,
                    DayIndex = day,
                    AmountCents = amount,
                    Memo = $"intestate residue share ({heir.ShareNumerator}/{heir.ShareDenominator}) to P{heir.PersonId} ({HeirName(heir.PersonId)})",
                    RecordedByPersonId = fiduciary,
                });
                prog.ResidueDisbursedHeirIds.Add(heir.PersonId);
                if (!IsAdult(heir.PersonId))
                {
                    prog.MinorCustodies.Add(new EstateMinorCustody
                    {
                        MinorPersonId = heir.PersonId,
                        AssetKind = "cash",
                        AssetRef = $"estate account {account.AccountId}",
                        ShareNumerator = heir.ShareNumerator,
                        ShareDenominator = heir.ShareDenominator,
                        CustodianPersonId = fiduciary,
                        Note = $"minor's {amount}c placed in the household of P{heir.PersonId}; the interest is the minor's, under the guardian's administration",
                    });
                }
            }
        }

        /// <summary>
        /// Credits an heir's household ledger with provenance (source class +
        /// reason, per the ledger's rules). Refuses when the heir has no
        /// household — cash is never placed by fiat.
        /// </summary>
        private string CreditHeirHousehold(int heirPersonId, int amountCents, string reason,
            string estateId, int fiduciary, int day, List<string> diag)
        {
            if (ctx.Ledgers == null || ctx.Memberships == null)
                return "no household ledgers wired — cash cannot be placed";
            int householdId = ctx.Memberships.GetActiveHouseholdId(heirPersonId);
            if (householdId < 0)
                return $"P{heirPersonId} has no active household — cash cannot be placed by fiat";
            HouseholdLedger ledger = ctx.Ledgers.Get(householdId);
            if (ledger == null)
                return $"household H{householdId} has no ledger";
            string refusal = ledger.RecordInflow(day, amountCents, HouseholdIncomeSource.Remittance,
                $"estate:{estateId}", reason, $"estate of the decedent (fiduciary P{fiduciary})");
            if (refusal != null) return refusal;
            diag.Add($"EstateLifecycleDriver: {amountCents}c placed to H{householdId} for P{heirPersonId} ({reason}).");
            return null;
        }

        #endregion

        #region Closure

        /// <summary>
        /// Closes the estate when it is truly resolved: no live contest, every
        /// claim settled, the account empty, every parcel distributed. The
        /// record is retained — closure archives, never deletes, the decedent's
        /// identity and history.
        /// </summary>
        public string CloseEstate(string estateId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            Estate estate = RequireEstate(estateId, diag);
            if (estate == null) return $"EstateLifecycleDriver.CloseEstate: unknown estate '{estateId}'.";
            EstateLifecycleProgress prog = Track(estateId);
            if (prog.Stage >= EstateLifecycleStage.Closed)
            {
                diag.Add($"EstateLifecycleDriver: estate {estateId} already closed (day {prog.ClosedDayIndex}) — no-op.");
                return null;
            }
            if (prog.Stage < EstateLifecycleStage.Distributed)
                return $"EstateLifecycleDriver.CloseEstate: estate {estateId} is not distributed — closure waits.";
            if (prog.LegalHolds.Count > 0)
                return $"EstateLifecycleDriver.CloseEstate: estate {estateId} carries {prog.LegalHolds.Count} legal hold(s) — closure waits.";

            string refusal = ctx.Administration.CloseAdministration(
                estate, ctx.Estates, prog.FiduciaryPersonId, ctx.CurrentDayIndex, ctx.Contests, diag);
            if (refusal != null)
            {
                AddHold(prog, $"closure refused: {refusal}", diag);
                return $"EstateLifecycleDriver.CloseEstate: {refusal}";
            }
            prog.Stage = EstateLifecycleStage.Closed;
            prog.ClosedDayIndex = ctx.CurrentDayIndex;
            diag.Add($"EstateLifecycleDriver: estate {estateId} CLOSED — debts settled, titles transferred, " +
                "historical record retained. Archive the decedent via PersonArchive.");
            return null;
        }

        #endregion

        #region Save / Load

        /// <summary>Pass VII Workstream C: the driver's persisted progress (owning runtime class).</summary>
        [Serializable]
        public sealed class EstateLifecycleDriverSaveDto
        {
            public List<EstateLifecycleProgress> Progress = new List<EstateLifecycleProgress>();

            public EstateLifecycleDriverSaveDto() { }
        }

        public EstateLifecycleDriverSaveDto CaptureSaveDto()
        {
            var dto = new EstateLifecycleDriverSaveDto();
            foreach (EstateLifecycleProgress prog in progress.Values)
            {
                if (prog == null) continue;
                dto.Progress.Add(new EstateLifecycleProgress
                {
                    EstateId = prog.EstateId,
                    Stage = prog.Stage,
                    FiduciaryPersonId = prog.FiduciaryPersonId,
                    InventoriedDayIndex = prog.InventoriedDayIndex,
                    CollectedDepositAccountIds = new List<string>(prog.CollectedDepositAccountIds),
                    DistributedParcelIds = new List<string>(prog.DistributedParcelIds),
                    TransferredBusinessIds = new List<string>(prog.TransferredBusinessIds),
                    ResidueDisbursedHeirIds = new List<int>(prog.ResidueDisbursedHeirIds),
                    MinorCustodies = new List<EstateMinorCustody>(prog.MinorCustodies),
                    UndividedBusinessShares = new List<EstateUndividedBusinessShare>(prog.UndividedBusinessShares),
                    LegalHolds = new List<string>(prog.LegalHolds),
                    ClosedDayIndex = prog.ClosedDayIndex,
                });
            }
            return dto;
        }

        /// <summary>Clear-then-load: duplicate estate ids are replaced, never duplicated.</summary>
        public void LoadFromSaveDto(EstateLifecycleDriverSaveDto dto)
        {
            progress.Clear();
            if (dto == null || dto.Progress == null) return;
            foreach (EstateLifecycleProgress prog in dto.Progress)
            {
                if (prog == null || string.IsNullOrWhiteSpace(prog.EstateId)) continue;
                progress[prog.EstateId] = prog;
            }
        }

        #endregion
    }
}
