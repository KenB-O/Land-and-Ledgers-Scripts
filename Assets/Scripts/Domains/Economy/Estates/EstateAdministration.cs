using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Property;
using LandLedgers.Economy.Financing;
using UnityEngine;

namespace LandLedgers.Economy.Estates
{
    /// <summary>
    /// D4D: how the estate's fiduciary got the job. An executor is named in a
    /// probated will; a co-executor is an additional will-named fiduciary
    /// (the will's nomination is cited on the record — never assumed); an
    /// administrator is granted letters of administration by the court for an
    /// intestate estate. Appointment is a RECORD; authority follows acceptance.
    /// </summary>
    public enum EstateAppointmentKind
    {
        Unspecified = 0,
        Executor = 1,      // named in the probated will
        CoExecutor = 2,    // additional fiduciary named in the will; nomination cited
        Administrator = 3, // court-granted letters of administration; intestate estates
    }

    /// <summary>
    /// D4D: the HARD ORDER of payment. Historical (1870s American practice):
    /// expenses of administration first (including funeral costs, which the
    /// period statutes ranked with administration expenses and last-illness
    /// costs), then secured debts, then debts and taxes given preference
    /// under law, then all other (unsecured) claims. Bequests and legacies
    /// come ONLY after every creditor claim is satisfied; the residue is
    /// last. The canon holds probate procedure as researched data (Tech X
    /// §10.1) and introduces inheritance taxation only in 1905 (Canon 17.10),
    /// so the PreferredTaxes rank covers the decedent's recorded tax
    /// obligations, not an estate tax, during the normal campaign.
    /// </summary>
    public enum EstateClaimRank
    {
        Unspecified = 0,
        AdministrationCosts = 1, // funeral + administration expenses first
        FuneralCosts = 2,
        SecuredDebt = 3,
        PreferredTaxes = 4,      // taxes given preference under law (see note)
        UnsecuredCreditor = 5,
    }

    /// <summary>D4D: what an estate inventory line points at. Every line cites a real record.</summary>
    public enum EstateInventoryLineKind
    {
        Unspecified = 0,
        Parcel = 1,            // T2F parcel inventoried on the estate
        BusinessInterest = 2,  // business instance id recorded on the estate
        DepositAccount = 3,     // NX-3B deposit account held by the decedent
        NoteReceivable = 4,    // T2A instrument where the decedent is payee/holder
        PersonalProperty = 5,  // described movables (recorded description)
    }

    /// <summary>D4D: abatement class of a bequest. Common-law order (research):
    /// the residue abates first, general bequests second, specific and
    /// demonstrative devises last, pro rata within a class.</summary>
    public enum EstateAbatementClass
    {
        Unspecified = 0,
        Residuary = 1,
        General = 2,   // cash sums (BequestKind.CashAmount)
        Specific = 3,  // parcels, business interests, described movables
    }

    /// <summary>
    /// D4D: which abatement order the estate uses. CALIBRATION — exact
    /// territorial statute detail is a research hold (Tech X §10.1), so the
    /// order is parameterized, not invented. Only the common-law historical
    /// order is implemented; any other order is refused loudly.
    /// </summary>
    public enum EstateAbatementOrder
    {
        Unspecified = 0,
        ResiduaryFirstCommonLaw = 1, // residue, then general, then specific last, pro rata
    }

    /// <summary>D4D: one estate account ledger entry. Receipts and disbursements on a separate track.</summary>
    public enum EstateAccountEntryKind
    {
        Unspecified = 0,
        Receipt = 1,
        Disbursement = 2,
    }

    /// <summary>
    /// D4D: the executor's/administrator's appointment as a record.
    /// Appointment without acceptance grants nothing: the fiduciary acts ONLY
    /// after accepting, and only within the recorded scope. Authority is
    /// never assumed from being named — it is granted by probate (letters
    /// testamentary) or by the court's letters of administration, and this
    /// record is the trail.
    /// </summary>
    [Serializable]
    public sealed class EstateAdministratorAppointment
    {
        public string AppointmentId = string.Empty;
        public string EstateId = string.Empty;
        public int PersonId = -1;
        public string Name = string.Empty;
        public EstateAppointmentKind Kind = EstateAppointmentKind.Unspecified;
        public bool Accepted;
        public int AppointedDayIndex;
        public int AcceptedDayIndex = -1;
        public string SourceWillId = string.Empty;   // executor/co-executor: the probated will
        public string NominationCitedIn = string.Empty; // co-executor: where the will names them
        public string CourtGrantNote = string.Empty;  // administrator: the letters of administration
        public string AuthorityScope = string.Empty;  // recorded scope or limitations, if any

        public EstateAdministratorAppointment() { }
    }

    /// <summary>
    /// D4D: one marshalled asset — the estate's inventory as a real record.
    /// Every line cites the registry it came from; nothing is invented.
    /// Parcels come from the estate's T2F-scoped inventory, business
    /// interests from the estate record, deposit accounts from the bank's
    /// deposit book (depositor must be the decedent), notes receivable from
    /// T2A instruments where the decedent is payee or holder, and personal
    /// property from recorded descriptions.
    /// </summary>
    [Serializable]
    public sealed class EstateInventoryLine
    {
        public string EstateId = string.Empty;
        public EstateInventoryLineKind Kind = EstateInventoryLineKind.Unspecified;
        public string ReferenceId = string.Empty; // parcel id, account id, instrument id, description key
        public string Description = string.Empty;
        public int EstimatedValueCents;
        public string Source = string.Empty; // the registry or record this line cites
        public int MarshalledDayIndex;

        public EstateInventoryLine() { }
    }

    /// <summary>
    /// D4D: one creditor claim against the estate, validated against recorded
    /// debts. The debt instrument id must be recorded on the estate
    /// (EstateService.RecordEstateDebt names real T2A/SWN-3 debts); when the
    /// actual instrument object is supplied, its identity, status, obligor,
    /// rank consistency, and amount are cross-checked. Amounts are RECORDED
    /// as owed and paid — this service never moves money itself.
    /// </summary>
    [Serializable]
    public sealed class EstateCreditorClaim
    {
        public string ClaimId = string.Empty;
        public string EstateId = string.Empty;
        public string DebtInstrumentId = string.Empty;
        public string CreditorName = string.Empty;
        public EstateClaimRank Rank = EstateClaimRank.Unspecified;
        public int AmountCents;
        public string InstrumentKindName = string.Empty; // e.g. "MortgageDeed"; empty for estate obligations
        public bool Validated;
        public string ValidatedDetail = string.Empty;
        public int PaidCents;
        public bool Settled;
        public int SettledDayIndex = -1;

        public EstateCreditorClaim() { }
    }

    /// <summary>D4D: one entry in the estate's separate accounting track.</summary>
    [Serializable]
    public sealed class EstateAccountEntry
    {
        public EstateAccountEntryKind Kind = EstateAccountEntryKind.Unspecified;
        public int DayIndex;
        public int AmountCents;
        public string Memo = string.Empty;
        public string RelatedClaimId = string.Empty; // the claim this disburses, if any
        public int RecordedByPersonId = -1;          // the fiduciary who recorded it

        public EstateAccountEntry() { }
    }

    /// <summary>
    /// D4D: the estate's own account — a separate accounting track so the
    /// executor's handling is auditable. Receipts (marshalled cash, sale
    /// proceeds, collected notes) in; disbursements (creditor payments,
    /// administration costs, residue) out. The balance is computed from the
    /// entries, never stored separately; disbursements are refused when the
    /// balance is short — never auto-paid.
    /// </summary>
    [Serializable]
    public sealed class EstateAccount
    {
        public string AccountId = string.Empty;
        public string EstateId = string.Empty;
        public int OpenedDayIndex;
        public List<EstateAccountEntry> Entries = new List<EstateAccountEntry>();

        public int BalanceCents
        {
            get
            {
                int balance = 0;
                foreach (EstateAccountEntry entry in Entries)
                {
                    if (entry == null) continue;
                    if (entry.Kind == EstateAccountEntryKind.Receipt) balance += Math.Max(0, entry.AmountCents);
                    else if (entry.Kind == EstateAccountEntryKind.Disbursement) balance -= Math.Max(0, entry.AmountCents);
                }
                return balance;
            }
        }

        public EstateAccount() { }
    }

    /// <summary>
    /// D4D: one recorded act of the fiduciary. Executor malfeasance is NOT
    /// auto-detected here — acts are recorded with their actor and day, and
    /// future systems judge them. The ledger is the audit trail.
    /// </summary>
    [Serializable]
    public sealed class EstateExecutorAct
    {
        public string EstateId = string.Empty;
        public int ActorPersonId = -1;
        public string ActDescription = string.Empty;
        public int DayIndex;

        public EstateExecutorAct() { }
    }

    /// <summary>
    /// D4D: executor compensation. CALIBRATION — the canon does not describe
    /// executor compensation and 1870s territorial fee schedules are a
    /// research hold (Tech X §10.1), so there is no invented default: a
    /// zero-rate schedule means no compensation schedule is on file, and
    /// compensation cannot be computed until the caller sets one. When set,
    /// the fee becomes an AdministrationCosts claim through the normal
    /// rank-order pipeline.
    /// </summary>
    [Serializable]
    public sealed class ExecutorCompensationSchedule
    {
        public int BasisPoints;   // of the estate value handled; 0 = unset
        public int MinimumCents;
        public int MaximumCents;  // 0 = no cap
        public string Note = string.Empty;

        public ExecutorCompensationSchedule() { }
    }

    /// <summary>D4D: one abatement entry — a bequest reduced or eliminated because the estate is insolvent.</summary>
    [Serializable]
    public sealed class EstateAbatementRecord
    {
        public string EstateId = string.Empty;
        public int BequestIndex = -1; // -1 = the residue itself
        public EstateAbatementClass AbatementClass = EstateAbatementClass.Unspecified;
        public int ReducedCents;      // cash bequests: the reduction; residue: 0 (eliminated)
        public string Reason = string.Empty;

        public EstateAbatementRecord() { }
    }

    /// <summary>
    /// D4D: estate administration — the executor's job as a real process.
    ///
    /// Integrates with the NX-3C <see cref="EstateService"/> and
    /// <see cref="ProbateService"/> through their public APIs only
    /// (appointments also recorded on the estate record; disbursements flow
    /// through the existing debt/distribution guards), and with the D4C
    /// <see cref="WillContestService"/> through its public API — an open
    /// contest FREEZES the estate: no distributions, no disbursements, no
    /// closure while a contest is live.
    ///
    /// Authority rules (no fiat):
    /// - The fiduciary acts ONLY within the granted authority: an accepted
    ///   appointment on the estate record. Co-executors each hold their own
    ///   accepted appointment; every act records its actor.
    /// - Intestate estates use the SAME pipeline with an administrator
    ///   (court-granted), not an executor.
    /// - Creditors are paid in the HARD ORDER before any bequest, legacy, or
    ///   residue. A claim is validated against the estate's recorded debts;
    ///   a shortfall is refused loudly, never auto-paid, and partial
    ///   payments are never invented.
    /// - Nothing distributes without the recorded authority chain.
    /// </summary>
    public sealed class EstateAdministrationService
    {
        private readonly Dictionary<string, EstateAdministratorAppointment> appointments =
            new Dictionary<string, EstateAdministratorAppointment>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<EstateInventoryLine>> inventories =
            new Dictionary<string, List<EstateInventoryLine>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<EstateCreditorClaim>> claims =
            new Dictionary<string, List<EstateCreditorClaim>>(StringComparer.Ordinal);
        private readonly Dictionary<string, EstateAccount> accounts =
            new Dictionary<string, EstateAccount>(StringComparer.Ordinal);
        private readonly List<EstateExecutorAct> acts = new List<EstateExecutorAct>();
        private readonly Dictionary<string, ExecutorCompensationSchedule> compensationSchedules =
            new Dictionary<string, ExecutorCompensationSchedule>(StringComparer.Ordinal);
        private readonly List<EstateAbatementRecord> abatements = new List<EstateAbatementRecord>();
        private readonly EstateAbatementOrder configuredAbatementOrder = EstateAbatementOrder.ResiduaryFirstCommonLaw;
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        #region Executor authority

        /// <summary>
        /// The will-named executor accepts the appointment. Authority requires
        /// the probated will (letters testamentary flow from probate — the
        /// executor cannot act on an unprobated will) and a real person who
        /// is not the decedent. Also records the appointment on the estate
        /// through the NX-3C public API.
        /// </summary>
        public EstateAdministratorAppointment AcceptExecutorAppointment(
            EntityIdRegistry ids, Estate estate, Will will, EstateService estateService,
            int personId, string name, PopulationState population, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) { diag.Add("EstateAdministration: an estate is required."); return null; }
            if (will == null) { diag.Add("EstateAdministration: the will naming the executor is required."); return null; }
            if (!will.Probated)
            {
                diag.Add($"EstateAdministration: will {will.WillId} is not probated — the executor's authority flows from probate, never from the paper alone.");
                return null;
            }
            if (will.ExecutorPersonId != personId)
            {
                diag.Add($"EstateAdministration: will {will.WillId} names P{will.ExecutorPersonId} as executor, not P{personId} — authority is never assumed.");
                return null;
            }
            string refusal = CheckPerson(estate, personId, population, diag);
            if (refusal != null) return null;
            if (FindAppointment(estate.EstateId, personId) != null)
            {
                diag.Add($"EstateAdministration: P{personId} already holds an appointment on estate {estate.EstateId}.");
                return FindAppointment(estate.EstateId, personId);
            }

            string appointed = estateService != null
                ? estateService.AppointExecutor(estate, personId, diag) : null;
            if (appointed != null) { diag.Add($"EstateAdministration: {appointed}"); return null; }

            var appointment = NewAppointment(ids, estate, personId, name,
                EstateAppointmentKind.Executor, dayIndex, diag);
            appointment.SourceWillId = will.WillId;
            appointment.Accepted = true;
            appointment.AcceptedDayIndex = dayIndex;
            diag.Add($"EstateAdministration: P{personId} ACCEPTED the executorship of estate {estate.EstateId} day {dayIndex} (will {will.WillId}, probated) — acts only within this authority.");
            return appointment;
        }

        /// <summary>
        /// A co-executor accepts. The will must name them as an additional
        /// fiduciary — the nomination is cited on the record (which clause or
        /// codicil), never assumed from a bare assertion.
        /// </summary>
        public EstateAdministratorAppointment AcceptCoExecutorAppointment(
            EntityIdRegistry ids, Estate estate, Will will, EstateService estateService,
            int personId, string name, string nominationCitedIn,
            PopulationState population, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) { diag.Add("EstateAdministration: an estate is required."); return null; }
            if (will == null) { diag.Add("EstateAdministration: the will naming the co-executor is required."); return null; }
            if (!will.Probated)
            {
                diag.Add($"EstateAdministration: will {will.WillId} is not probated — co-executor authority flows from probate too.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(nominationCitedIn))
            {
                diag.Add("EstateAdministration: a co-executor appointment must cite where the will names them — nominations are never assumed.");
                return null;
            }
            string refusal = CheckPerson(estate, personId, population, diag);
            if (refusal != null) return null;
            if (FindAppointment(estate.EstateId, personId) != null)
            {
                diag.Add($"EstateAdministration: P{personId} already holds an appointment on estate {estate.EstateId}.");
                return FindAppointment(estate.EstateId, personId);
            }

            var appointment = NewAppointment(ids, estate, personId, name,
                EstateAppointmentKind.CoExecutor, dayIndex, diag);
            appointment.SourceWillId = will.WillId;
            appointment.NominationCitedIn = nominationCitedIn;
            appointment.Accepted = true;
            appointment.AcceptedDayIndex = dayIndex;
            diag.Add($"EstateAdministration: P{personId} ACCEPTED as co-executor of estate {estate.EstateId} day {dayIndex} (will {will.WillId}; nominated {nominationCitedIn}).");
            if (estateService != null && estate.ExecutorPersonId < 0)
                estateService.AppointExecutor(estate, personId, diag);
            return appointment;
        }

        /// <summary>
        /// Intestate administration: the court grants letters of
        /// administration to a real person. Requires the estate to be
        /// intestate (no probated will) and the recorded court grant. The
        /// administrator reuses the SAME pipeline as an executor.
        /// </summary>
        public EstateAdministratorAppointment GrantAdministrator(
            EntityIdRegistry ids, Estate estate, EstateService estateService,
            int personId, string name, string courtGrantNote,
            PopulationState population, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) { diag.Add("EstateAdministration: an estate is required."); return null; }
            if (!string.IsNullOrWhiteSpace(estate.TestateWillId))
            {
                diag.Add($"EstateAdministration: estate {estate.EstateId} is testate (will {estate.TestateWillId}) — an administrator is granted only for intestate estates.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(courtGrantNote))
            {
                diag.Add("EstateAdministration: letters of administration must be recorded — the court grant is the authority, never a bare claim.");
                return null;
            }
            string refusal = CheckPerson(estate, personId, population, diag);
            if (refusal != null) return null;
            if (FindAppointment(estate.EstateId, personId) != null)
            {
                diag.Add($"EstateAdministration: P{personId} already holds an appointment on estate {estate.EstateId}.");
                return FindAppointment(estate.EstateId, personId);
            }

            string appointed = estateService != null
                ? estateService.AppointExecutor(estate, personId, diag) : null;
            if (appointed != null) { diag.Add($"EstateAdministration: {appointed}"); return null; }

            var appointment = NewAppointment(ids, estate, personId, name,
                EstateAppointmentKind.Administrator, dayIndex, diag);
            appointment.CourtGrantNote = courtGrantNote;
            appointment.Accepted = true;
            appointment.AcceptedDayIndex = dayIndex;
            diag.Add($"EstateAdministration: P{personId} ACCEPTED letters of administration for intestate estate {estate.EstateId} day {dayIndex} ({courtGrantNote}) — the same pipeline as an executor.");
            return appointment;
        }

        /// <summary>
        /// The authority gate: null means the person may act for the estate
        /// today. Anything else is a loud refusal — no fiat acts.
        /// </summary>
        public string RequireAuthority(Estate estate, int personId, string actDescription, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration: an estate is required.";
            if (estate.Status == EstateStatus.Closed)
                return $"EstateAdministration: estate {estate.EstateId} is closed — no one acts for a closed estate.";
            EstateAdministratorAppointment appointment = FindAppointment(estate.EstateId, personId);
            if (appointment == null)
                return $"EstateAdministration: P{personId} holds no appointment on estate {estate.EstateId} — '{actDescription ?? "the act"}' refused. Authority is granted, never assumed.";
            if (!appointment.Accepted)
                return $"EstateAdministration: P{personId} was appointed but never accepted — '{actDescription ?? "the act"}' refused until acceptance.";
            return null;
        }

        /// <summary>
        /// Records one act of the fiduciary in the audit ledger. Malfeasance
        /// is NOT auto-detected — the ledger records, future systems judge.
        /// </summary>
        public string RecordExecutorAct(Estate estate, int personId, string description, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.RecordExecutorAct: an estate is required.";
            string refusal = RequireAuthority(estate, personId, description, diag);
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(description))
                return "EstateAdministration.RecordExecutorAct: the act must be described — the ledger records what happened.";
            acts.Add(new EstateExecutorAct
            {
                EstateId = estate.EstateId,
                ActorPersonId = personId,
                ActDescription = description,
                DayIndex = dayIndex,
            });
            diag.Add($"EstateAdministration: act recorded — P{personId} on estate {estate.EstateId} day {dayIndex}: {description} (recorded, not judged).");
            return null;
        }

        public EstateAdministratorAppointment FindAppointment(string estateId, int personId)
        {
            foreach (EstateAdministratorAppointment appointment in appointments.Values)
            {
                if (appointment != null && string.Equals(appointment.EstateId, estateId, StringComparison.Ordinal)
                    && appointment.PersonId == personId)
                    return appointment;
            }
            return null;
        }

        public List<EstateExecutorAct> ActsFor(string estateId)
        {
            var matches = new List<EstateExecutorAct>();
            foreach (EstateExecutorAct act in acts)
            {
                if (act != null && string.Equals(act.EstateId, estateId, StringComparison.Ordinal))
                    matches.Add(act);
            }
            return matches;
        }

        #endregion

        #region Marshalling (inventory)

        /// <summary>
        /// Marshals a T2F parcel already inventoried on the estate
        /// (EstateService.RegisterDecedentParcel is the gate — the title
        /// authority stays the source of truth). The line cites the estate
        /// inventory; nothing is invented.
        /// </summary>
        public string MarshalParcel(
            Estate estate, int appointmentPersonId, string parcelId,
            string description, int estimatedValueCents, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            string refusal = GuardAct(estate, appointmentPersonId, $"marshal parcel '{parcelId}'", diag);
            if (refusal != null) return refusal;
            if (!estate.ParcelIds.Contains(parcelId))
                return $"EstateAdministration.MarshalParcel: '{parcelId}' is not inventoried on estate {estate.EstateId} — inventory assembles from the registries, never invented.";
            AddInventoryLine(estate.EstateId, new EstateInventoryLine
            {
                Kind = EstateInventoryLineKind.Parcel,
                ReferenceId = parcelId,
                Description = description ?? string.Empty,
                EstimatedValueCents = Math.Max(0, estimatedValueCents),
                Source = $"estate inventory (T2F-scoped by EstateService.RegisterDecedentParcel)",
                MarshalledDayIndex = dayIndex,
            }, diag);
            return null;
        }

        /// <summary>
        /// Marshals a business interest recorded on the estate. The
        /// BusinessInstanceState keys OwnerKind rather than person, so the
        /// estate record is the scope (a documented seam in the T3C code).
        /// </summary>
        public string MarshalBusinessInterest(
            Estate estate, int appointmentPersonId, string businessInstanceId,
            string description, int estimatedValueCents, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            string refusal = GuardAct(estate, appointmentPersonId, $"marshal business interest '{businessInstanceId}'", diag);
            if (refusal != null) return refusal;
            if (!estate.BusinessInstanceIds.Contains(businessInstanceId))
                return $"EstateAdministration.MarshalBusinessInterest: '{businessInstanceId}' is not recorded on estate {estate.EstateId} — never invented.";
            AddInventoryLine(estate.EstateId, new EstateInventoryLine
            {
                Kind = EstateInventoryLineKind.BusinessInterest,
                ReferenceId = businessInstanceId,
                Description = description ?? string.Empty,
                EstimatedValueCents = Math.Max(0, estimatedValueCents),
                Source = "estate business-interest record",
                MarshalledDayIndex = dayIndex,
            }, diag);
            return null;
        }

        /// <summary>
        /// Marshals a bank deposit account: the account must exist in the
        /// bank's deposit book and the depositor must be the decedent. The
        /// bank's ledger stays the authority for the balance.
        /// </summary>
        public string MarshalDepositAccount(
            Estate estate, int appointmentPersonId, BankDepositLedger deposits,
            string accountId, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            string refusal = GuardAct(estate, appointmentPersonId, $"marshal deposit account '{accountId}'", diag);
            if (refusal != null) return refusal;
            if (deposits == null) return "EstateAdministration.MarshalDepositAccount: the bank's deposit book is required.";
            DepositAccount found = null;
            foreach (DepositAccount account in deposits.Accounts)
            {
                if (account != null && string.Equals(account.AccountId, accountId, StringComparison.Ordinal))
                { found = account; break; }
            }
            if (found == null)
                return $"EstateAdministration.MarshalDepositAccount: account '{accountId}' is not held by '{deposits.BankName}' — no invented accounts.";
            if (!string.Equals(found.DepositorName, estate.DecedentName, StringComparison.Ordinal))
                return $"EstateAdministration.MarshalDepositAccount: account '{accountId}' belongs to '{found.DepositorName}', not the decedent — not marshalled.";
            AddInventoryLine(estate.EstateId, new EstateInventoryLine
            {
                Kind = EstateInventoryLineKind.DepositAccount,
                ReferenceId = accountId,
                Description = $"{found.Kind} account at {deposits.BankName}",
                EstimatedValueCents = Math.Max(0, found.BalanceCents),
                Source = $"bank deposit book ({deposits.BankName})",
                MarshalledDayIndex = dayIndex,
            }, diag);
            return null;
        }

        /// <summary>
        /// Marshals a note receivable: a T2A instrument where the decedent is
        /// the payee (or the current holder by endorsement). The instrument
        /// must be active — satisfied or released paper is not an asset.
        /// </summary>
        public string MarshalNoteReceivable(
            Estate estate, int appointmentPersonId, ICreditInstrument instrument,
            int estimatedValueCents, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            string refusal = GuardAct(estate, appointmentPersonId, "marshal note receivable", diag);
            if (refusal != null) return refusal;
            if (instrument == null)
                return "EstateAdministration.MarshalNoteReceivable: the instrument is required — receivables are real paper, never invented.";
            if (instrument.Status == CreditInstrumentStatus.Satisfied || instrument.Status == CreditInstrumentStatus.Released)
                return $"EstateAdministration.MarshalNoteReceivable: {instrument.InstrumentId} is {instrument.Status} — discharged paper is not an asset.";
            if (!(instrument is PromissoryNote note))
                return $"EstateAdministration.MarshalNoteReceivable: {instrument.InstrumentId} is {instrument.Kind} — only promissory notes marshal as receivables here.";
            bool decedentIsPayee = string.Equals(note.PayeeName, estate.DecedentName, StringComparison.Ordinal);
            bool decedentIsHolder = !string.IsNullOrWhiteSpace(note.HolderName) &&
                string.Equals(note.HolderName, estate.DecedentName, StringComparison.Ordinal);
            if (!decedentIsPayee && !decedentIsHolder)
                return $"EstateAdministration.MarshalNoteReceivable: note {instrument.InstrumentId} is payable to '{note.PayeeName}', not the decedent — not the estate's asset.";
            AddInventoryLine(estate.EstateId, new EstateInventoryLine
            {
                Kind = EstateInventoryLineKind.NoteReceivable,
                ReferenceId = instrument.InstrumentId.ToString(),
                Description = $"promissory note: {note.MakerName} promises {note.PayeeName} {note.PrincipalCents}c ({note.Terms})",
                EstimatedValueCents = Math.Max(0, estimatedValueCents),
                Source = "T2A credit instrument (decedent is payee/holder)",
                MarshalledDayIndex = dayIndex,
            }, diag);
            return null;
        }

        /// <summary>
        /// Marshals described movables — furniture, tools, livestock — from a
        /// recorded description. The description is the record; this service
        /// does not appraise by fiat beyond the stated estimate.
        /// </summary>
        public string MarshalPersonalProperty(
            Estate estate, int appointmentPersonId, string description,
            int estimatedValueCents, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            string refusal = GuardAct(estate, appointmentPersonId, "marshal personal property", diag);
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(description))
                return "EstateAdministration.MarshalPersonalProperty: the movables must be described — the description is the record.";
            AddInventoryLine(estate.EstateId, new EstateInventoryLine
            {
                Kind = EstateInventoryLineKind.PersonalProperty,
                ReferenceId = description,
                Description = description,
                EstimatedValueCents = Math.Max(0, estimatedValueCents),
                Source = "recorded description of the decedent's movables",
                MarshalledDayIndex = dayIndex,
            }, diag);
            return null;
        }

        public List<EstateInventoryLine> InventoryFor(string estateId)
        {
            if (inventories.TryGetValue(estateId, out List<EstateInventoryLine> lines))
                return new List<EstateInventoryLine>(lines);
            return new List<EstateInventoryLine>();
        }

        #endregion

        #region Estate accounts

        /// <summary>
        /// Opens the estate's separate accounting track. The balance starts
        /// at zero — cash enters only through recorded receipts (marshalled
        /// funds, sale proceeds, collected notes), never by fiat.
        /// </summary>
        public EstateAccount OpenEstateAccount(
            Estate estate, int appointmentPersonId, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) { diag.Add("EstateAdministration.OpenEstateAccount: an estate is required."); return null; }
            string refusal = RequireAuthority(estate, appointmentPersonId, "open the estate account", diag);
            if (refusal != null) { diag.Add(refusal); return null; }
            if (accounts.TryGetValue(estate.EstateId, out EstateAccount existing))
            {
                diag.Add($"EstateAdministration: estate {estate.EstateId} already has an account — one account per estate.");
                return existing;
            }
            var account = new EstateAccount
            {
                AccountId = $"estate-{estate.EstateId}",
                EstateId = estate.EstateId,
                OpenedDayIndex = dayIndex,
            };
            accounts[estate.EstateId] = account;
            RecordExecutorAct(estate, appointmentPersonId,
                $"opened the estate account (balance 0c — receipts only through recorded entries)", dayIndex, diag);
            diag.Add($"EstateAdministration: estate account opened for estate {estate.EstateId} — the fiduciary's handling is now auditable.");
            return account;
        }

        /// <summary>
        /// Records a receipt into the estate account — marshalled cash, sale
        /// proceeds, a collected note. The memo states the source; the
        /// source must be real (the caller ties it to an inventory line or
        /// recorded sale).
        /// </summary>
        public string RecordEstateReceipt(
            Estate estate, int appointmentPersonId, int amountCents, string memo,
            int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.RecordEstateReceipt: an estate is required.";
            string refusal = GuardAct(estate, appointmentPersonId, "record an estate receipt", diag);
            if (refusal != null) return refusal;
            if (!accounts.TryGetValue(estate.EstateId, out EstateAccount account))
                return $"EstateAdministration.RecordEstateReceipt: estate {estate.EstateId} has no account — open it first.";
            if (amountCents <= 0)
                return "EstateAdministration.RecordEstateReceipt: a receipt must be positive.";
            if (string.IsNullOrWhiteSpace(memo))
                return "EstateAdministration.RecordEstateReceipt: the receipt must state its source — unaudited cash never enters.";
            account.Entries.Add(new EstateAccountEntry
            {
                Kind = EstateAccountEntryKind.Receipt,
                DayIndex = dayIndex,
                AmountCents = amountCents,
                Memo = memo,
                RecordedByPersonId = appointmentPersonId,
            });
            diag.Add($"EstateAdministration: estate {estate.EstateId} receipt {amountCents}c ({memo}) — balance now {account.BalanceCents}c.");
            return null;
        }

        public EstateAccount AccountFor(string estateId)
        {
            return accounts.TryGetValue(estateId, out EstateAccount account) ? account : null;
        }

        #endregion

        #region Debts before bequests — the hard order

        /// <summary>
        /// Records a creditor claim against the estate, validated against the
        /// estate's recorded debts. The debt instrument id must be recorded
        /// on the estate (EstateService.RecordEstateDebt names real debts);
        /// when the instrument object is supplied, its identity, status,
        /// obligor, rank consistency, and amount are cross-checked against
        /// the T2A instrument — no proxy claims, no invented amounts.
        /// </summary>
        public EstateCreditorClaim RecordCreditorClaim(
            Estate estate, string debtInstrumentId, EstateClaimRank rank,
            int claimedAmountCents, ICreditInstrument instrument,
            int appointmentPersonId, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) { diag.Add("EstateAdministration.RecordCreditorClaim: an estate is required."); return null; }
            string refusal = RequireAuthority(estate, appointmentPersonId, $"record claim on '{debtInstrumentId}'", diag);
            if (refusal != null) { diag.Add(refusal); return null; }
            if (string.IsNullOrWhiteSpace(debtInstrumentId))
            {
                diag.Add("EstateAdministration.RecordCreditorClaim: the debt instrument must be named.");
                return null;
            }
            if (!estate.EstateDebtIds.Contains(debtInstrumentId))
            {
                diag.Add($"EstateAdministration.RecordCreditorClaim: '{debtInstrumentId}' is not a recorded debt of estate {estate.EstateId} — claims are validated against recorded debts, never invented.");
                return null;
            }
            if (estate.SettledDebtIds.Contains(debtInstrumentId))
            {
                diag.Add($"EstateAdministration.RecordCreditorClaim: '{debtInstrumentId}' is already settled — no double claims.");
                return null;
            }
            if (rank == EstateClaimRank.Unspecified)
            {
                diag.Add("EstateAdministration.RecordCreditorClaim: the claim's rank in the hard order must be stated.");
                return null;
            }
            if (claimedAmountCents <= 0)
            {
                diag.Add("EstateAdministration.RecordCreditorClaim: the claimed amount must be positive.");
                return null;
            }
            foreach (EstateCreditorClaim existing in ClaimsFor(estate.EstateId))
            {
                if (string.Equals(existing.DebtInstrumentId, debtInstrumentId, StringComparison.Ordinal))
                {
                    diag.Add($"EstateAdministration.RecordCreditorClaim: '{debtInstrumentId}' already has claim {existing.ClaimId} — one claim per debt.");
                    return null;
                }
            }

            string instrumentKindName = string.Empty;
            string creditorName = string.Empty;
            string validatedDetail = string.Empty;
            string validation = ValidateInstrumentAgainstEstate(estate, debtInstrumentId, rank,
                claimedAmountCents, instrument, ref instrumentKindName, ref creditorName, ref validatedDetail);
            if (validation != null) { diag.Add(validation); return null; }

            var claim = new EstateCreditorClaim
            {
                ClaimId = Guid.NewGuid().ToString("N"),
                EstateId = estate.EstateId,
                DebtInstrumentId = debtInstrumentId,
                CreditorName = creditorName,
                Rank = rank,
                AmountCents = claimedAmountCents,
                InstrumentKindName = instrumentKindName,
                Validated = true,
                ValidatedDetail = validatedDetail,
            };
            if (!claims.TryGetValue(estate.EstateId, out List<EstateCreditorClaim> list))
            {
                list = new List<EstateCreditorClaim>();
                claims[estate.EstateId] = list;
            }
            list.Add(claim);
            estate.Status = EstateStatus.DebtsSettling;
            diag.Add($"EstateAdministration: claim {claim.ClaimId} recorded — {rank} '{debtInstrumentId}' {claimedAmountCents}c to '{creditorName}'. Creditors before heirs.");
            return claim;
        }

        /// <summary>
        /// Records a funeral obligation as an estate debt (rank FuneralCosts).
        /// The obligation id is recorded on the estate through the NX-3C
        /// public API, then claimed through the normal pipeline.
        /// </summary>
        public EstateCreditorClaim RecordFuneralObligation(
            Estate estate, EstateService estateService, int amountCents, string description,
            int appointmentPersonId, int dayIndex, List<string> diag = null)
        {
            return RecordEstateObligation(estate, estateService, EstateClaimRank.FuneralCosts,
                $"funeral-{estate.EstateId}", amountCents, description,
                appointmentPersonId, dayIndex, diag);
        }

        /// <summary>
        /// Records a preferred tax obligation (rank PreferredTaxes). Canon
        /// 17.10: inheritance taxation arrives in 1905, outside the normal
        /// campaign — this rank covers the decedent's recorded tax
        /// obligations, not an estate tax.
        /// </summary>
        public EstateCreditorClaim RecordTaxObligation(
            Estate estate, EstateService estateService, int amountCents, string description,
            int appointmentPersonId, int dayIndex, List<string> diag = null)
        {
            return RecordEstateObligation(estate, estateService, EstateClaimRank.PreferredTaxes,
                $"tax-{estate.EstateId}-{dayIndex}", amountCents, description,
                appointmentPersonId, dayIndex, diag);
        }

        /// <summary>
        /// The lowest claim rank with an unsettled claim — the rank that must
        /// be paid next. Returns null when every claim is settled.
        /// </summary>
        public EstateClaimRank? NextUnpaidRank(string estateId)
        {
            EstateClaimRank? next = null;
            foreach (EstateCreditorClaim claim in ClaimsFor(estateId))
            {
                if (claim == null || claim.Settled) continue;
                if (next == null || claim.Rank < next) next = claim.Rank;
            }
            return next;
        }

        /// <summary>
        /// Disburses one validated claim IN FULL from the estate account, in
        /// the hard rank order. Refuses loudly when: the claim's rank is not
        /// the next unpaid rank (earlier ranks wait for no one later), the
        /// account balance is short (shortfalls are never auto-paid, and
        /// partial payments are never invented), or a D4C contest is live
        /// (an open contest freezes the estate). On success the disbursement
        /// is recorded on the account, the claim is marked settled, and the
        /// estate's debt record is cleared through the NX-3C public API.
        /// </summary>
        public string DisburseForClaim(
            Estate estate, EstateService estateService, EstateCreditorClaim claim,
            int appointmentPersonId, int dayIndex,
            WillContestService contestService = null, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.DisburseForClaim: an estate is required.";
            if (estateService == null) return "EstateAdministration.DisburseForClaim: the estate service is required.";
            if (claim == null) return "EstateAdministration.DisburseForClaim: a claim is required.";
            if (!string.Equals(claim.EstateId, estate.EstateId, StringComparison.Ordinal))
                return $"EstateAdministration.DisburseForClaim: claim {claim.ClaimId} belongs to another estate.";
            if (!claim.Validated) return $"EstateAdministration.DisburseForClaim: claim {claim.ClaimId} is not validated.";
            if (claim.Settled) return $"EstateAdministration.DisburseForClaim: claim {claim.ClaimId} is already settled.";
            string refusal = GuardAct(estate, appointmentPersonId, $"disburse claim {claim.ClaimId}", diag);
            if (refusal != null) return refusal;
            refusal = RequireNoLiveContest(estate, contestService, "disburse creditor claims", diag);
            if (refusal != null) return refusal;

            EstateClaimRank? next = NextUnpaidRank(estate.EstateId);
            if (next != null && claim.Rank > next)
                return $"EstateAdministration.DisburseForClaim: rank {claim.Rank} waits for rank {next} — the hard order is never skipped.";
            if (!accounts.TryGetValue(estate.EstateId, out EstateAccount account))
                return $"EstateAdministration.DisburseForClaim: estate {estate.EstateId} has no account — claims disburse from the estate account only.";

            int remaining = claim.AmountCents - claim.PaidCents;
            if (account.BalanceCents < remaining)
                return $"EstateAdministration.DisburseForClaim: SHORTFALL — claim {claim.ClaimId} needs {remaining}c but the estate account holds {account.BalanceCents}c. Refused loudly; never auto-paid, never partial.";

            account.Entries.Add(new EstateAccountEntry
            {
                Kind = EstateAccountEntryKind.Disbursement,
                DayIndex = dayIndex,
                AmountCents = remaining,
                Memo = $"paid {claim.Rank} claim '{claim.DebtInstrumentId}' to '{claim.CreditorName}'",
                RelatedClaimId = claim.ClaimId,
                RecordedByPersonId = appointmentPersonId,
            });
            claim.PaidCents += remaining;
            claim.Settled = true;
            claim.SettledDayIndex = dayIndex;
            string settled = estateService.MarkDebtSettled(estate, claim.DebtInstrumentId, diag);
            if (settled != null) diag.Add($"EstateAdministration: note — {settled}");
            RecordExecutorAct(estate, appointmentPersonId,
                $"disbursed {remaining}c for {claim.Rank} claim '{claim.DebtInstrumentId}' (claim {claim.ClaimId})", dayIndex, diag);
            diag.Add($"EstateAdministration: claim {claim.ClaimId} SETTLED — {remaining}c to '{claim.CreditorName}' ({claim.Rank}). Account balance now {account.BalanceCents}c.");
            return null;
        }

        public List<EstateCreditorClaim> ClaimsFor(string estateId)
        {
            if (claims.TryGetValue(estateId, out List<EstateCreditorClaim> list))
                return new List<EstateCreditorClaim>(list);
            return new List<EstateCreditorClaim>();
        }

        #endregion

        #region Executor compensation (parameterized)

        /// <summary>
        /// Sets the executor compensation schedule for an estate. The rate is
        /// parameterized — 1870s territorial fee schedules are a research
        /// hold (Tech X §10.1), so no default is invented here.
        /// </summary>
        public string SetCompensationSchedule(
            Estate estate, ExecutorCompensationSchedule schedule,
            int appointmentPersonId, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.SetCompensationSchedule: an estate is required.";
            string refusal = GuardAct(estate, appointmentPersonId, "set the compensation schedule", diag);
            if (refusal != null) return refusal;
            if (schedule == null)
                return "EstateAdministration.SetCompensationSchedule: a schedule is required.";
            if (schedule.BasisPoints < 0)
                return "EstateAdministration.SetCompensationSchedule: the rate cannot be negative.";
            compensationSchedules[estate.EstateId] = schedule;
            diag.Add($"EstateAdministration: estate {estate.EstateId} compensation schedule set — {schedule.BasisPoints} bps" +
                (schedule.MinimumCents > 0 ? $", min {schedule.MinimumCents}c" : "") +
                (schedule.MaximumCents > 0 ? $", max {schedule.MaximumCents}c" : "") +
                $" ({schedule.Note}).");
            return null;
        }

        /// <summary>
        /// Computes the fiduciary's fee against the stated estate value.
        /// Returns -1 when no schedule is on file (unset, never invented).
        /// </summary>
        public int ComputeCompensationCents(
            string estateId, int estateValueCents, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (!compensationSchedules.TryGetValue(estateId, out ExecutorCompensationSchedule schedule)
                || schedule.BasisPoints <= 0)
            {
                diag.Add($"EstateAdministration: no compensation schedule on file for estate {estateId} — the fee is unset (research hold), never invented.");
                return -1;
            }
            long fee = (long)Math.Max(0, estateValueCents) * schedule.BasisPoints / 10000L;
            if (schedule.MinimumCents > 0) fee = Math.Max(fee, schedule.MinimumCents);
            if (schedule.MaximumCents > 0) fee = Math.Min(fee, schedule.MaximumCents);
            diag.Add($"EstateAdministration: compensation computed for estate {estateId} — {fee}c on {estateValueCents}c at {schedule.BasisPoints} bps.");
            return (int)fee;
        }

        /// <summary>
        /// Records the computed compensation as an AdministrationCosts claim
        /// through the normal rank-order pipeline. The obligation id is
        /// recorded on the estate through the NX-3C public API first.
        /// </summary>
        public EstateCreditorClaim RecordCompensationClaim(
            Estate estate, EstateService estateService, int estateValueCents,
            int appointmentPersonId, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) { diag.Add("EstateAdministration.RecordCompensationClaim: an estate is required."); return null; }
            int fee = ComputeCompensationCents(estate.EstateId, estateValueCents, diag);
            if (fee < 0) return null;
            if (fee == 0)
            {
                diag.Add($"EstateAdministration: compensation computes to 0c for estate {estate.EstateId} — no claim recorded.");
                return null;
            }
            return RecordEstateObligation(estate, estateService, EstateClaimRank.AdministrationCosts,
                $"compensation-{estate.EstateId}", fee, "fiduciary compensation per the filed schedule",
                appointmentPersonId, dayIndex, diag);
        }

        #endregion

        #region Insolvent estates — abatement

        /// <summary>
        /// Abatement for insolvent estates: when the estate account cannot
        /// cover the cash bequests after all creditor claims are settled,
        /// bequests abate in the configured order (common-law historical:
        /// the residue is eliminated first, general bequests abate pro rata
        /// second, specific devises last — a specific devise encumbered by a
        /// settled secured claim is flagged for sale with proceeds pro
        /// rata). Refuses loudly when debts are still unsettled (debts
        /// before bequests — always) or when the configured order is not the
        /// implemented one. Intestate estates have no bequests to abate.
        /// </summary>
        public string AssessAndRecordAbatement(
            Estate estate, Will will, int appointmentPersonId, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.AssessAndRecordAbatement: an estate is required.";
            string refusal = GuardAct(estate, appointmentPersonId, "assess abatement", diag);
            if (refusal != null) return refusal;
            if (configuredAbatementOrder != EstateAbatementOrder.ResiduaryFirstCommonLaw)
                return $"EstateAdministration.AssessAndRecordAbatement: order {configuredAbatementOrder} is not implemented — abatement orders are parameterized, never guessed.";
            if (will == null)
            {
                diag.Add($"EstateAdministration: estate {estate.EstateId} is intestate — heirs take the residue; no bequests to abate.");
                return null;
            }
            EstateClaimRank? next = NextUnpaidRank(estate.EstateId);
            if (next != null)
                return $"EstateAdministration.AssessAndRecordAbatement: rank {next} claims unsettled — debts before bequests, always. Settle claims first.";
            if (!accounts.TryGetValue(estate.EstateId, out EstateAccount account))
                return $"EstateAdministration.AssessAndRecordAbatement: estate {estate.EstateId} has no account.";

            var generalIndices = new List<int>();
            int generalTotal = 0;
            for (int i = 0; i < will.Bequests.Count; i++)
            {
                Bequest bequest = will.Bequests[i];
                if (bequest == null || bequest.Distributed || bequest.InvalidatedByContest || bequest.HeldForWitnessConflict)
                    continue;
                if (bequest.Kind == BequestKind.CashAmount)
                {
                    generalIndices.Add(i);
                    generalTotal += Math.Max(0, bequest.CashCents);
                }
            }

            int cashAvailable = account.BalanceCents;
            if (cashAvailable >= generalTotal)
            {
                diag.Add($"EstateAdministration: estate {estate.EstateId} is solvent for its legacies — {cashAvailable}c covers {generalTotal}c in general bequests. No abatement.");
                return null;
            }

            // 1. The residue abates first — eliminated while legacies go unpaid.
            abatements.Add(new EstateAbatementRecord
            {
                EstateId = estate.EstateId,
                BequestIndex = -1,
                AbatementClass = EstateAbatementClass.Residuary,
                ReducedCents = 0,
                Reason = "residuary eliminated — the estate is insolvent; residue abates first",
            });
            diag.Add($"EstateAdministration: estate {estate.EstateId} INSOLVENT — {cashAvailable}c cannot cover {generalTotal}c in general bequests. Residue eliminated first.");

            // 2. General bequests abate pro rata to fit the available cash.
            int shortfall = generalTotal - cashAvailable;
            int allocated = 0;
            for (int n = 0; n < generalIndices.Count; n++)
            {
                int index = generalIndices[n];
                int share = will.Bequests[index].CashCents;
                int reduction = n == generalIndices.Count - 1
                    ? shortfall - allocated
                    : (int)((long)share * shortfall / generalTotal);
                allocated += reduction;
                abatements.Add(new EstateAbatementRecord
                {
                    EstateId = estate.EstateId,
                    BequestIndex = index,
                    AbatementClass = EstateAbatementClass.General,
                    ReducedCents = Math.Max(0, reduction),
                    Reason = $"pro rata abatement — insolvent estate; legacy reduced by {reduction}c",
                });
            }
            diag.Add($"EstateAdministration: {generalIndices.Count} general bequest(s) abated pro rata — {shortfall}c of {generalTotal}c eliminated; {cashAvailable}c remains for the reduced legacies.");

            // 3. Specific devises: last to abate. A specific devise stands
            // unless a settled secured claim encumbers that exact asset —
            // then the asset is flagged for sale and the proceeds go pro
            // rata among the specific devisees (recorded; the caller sells).
            foreach (EstateCreditorClaim claim in ClaimsFor(estate.EstateId))
            {
                if (claim == null || !claim.Settled || claim.Rank != EstateClaimRank.SecuredDebt) continue;
                for (int i = 0; i < will.Bequests.Count; i++)
                {
                    Bequest bequest = will.Bequests[i];
                    if (bequest == null || bequest.Kind != BequestKind.Parcel || bequest.Distributed
                        || bequest.InvalidatedByContest || bequest.HeldForWitnessConflict) continue;
                    if (string.IsNullOrWhiteSpace(bequest.ParcelId)) continue;
                    if (claim.ValidatedDetail == null || !claim.ValidatedDetail.Contains(bequest.ParcelId)) continue;
                    abatements.Add(new EstateAbatementRecord
                    {
                        EstateId = estate.EstateId,
                        BequestIndex = i,
                        AbatementClass = EstateAbatementClass.Specific,
                        ReducedCents = 0,
                        Reason = $"abated in place — settled secured claim '{claim.DebtInstrumentId}' encumbers '{bequest.ParcelId}'; asset flagged for sale, proceeds pro rata (caller executes)",
                    });
                    diag.Add($"EstateAdministration: specific devise '{bequest.ParcelId}' abated in place — encumbered by settled secured claim '{claim.DebtInstrumentId}'.");
                }
            }
            return null;
        }

        public List<EstateAbatementRecord> AbatementsFor(string estateId)
        {
            var matches = new List<EstateAbatementRecord>();
            foreach (EstateAbatementRecord record in abatements)
            {
                if (record != null && string.Equals(record.EstateId, estateId, StringComparison.Ordinal))
                    matches.Add(record);
            }
            return matches;
        }

        #endregion

        #region Administration-guarded distributions

        /// <summary>
        /// An open D4C contest FREEZES the estate: no distributions, no
        /// disbursements, no residue, no closure while a contest is live.
        /// Passing no contest service skips the check with a loud note — the
        /// caller that has the service must pass it.
        /// </summary>
        public string RequireNoLiveContest(
            Estate estate, WillContestService contestService, string actDescription,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration: an estate is required.";
            if (contestService == null)
            {
                diag.Add($"EstateAdministration: contest-freeze check SKIPPED for '{actDescription ?? "the act"}' — no contest service supplied. The caller must supply one where contests can exist.");
                return null;
            }
            if (contestService.HasLiveContestForEstate(estate.EstateId))
                return $"EstateAdministration: estate {estate.EstateId} is FROZEN — an open will contest blocks '{actDescription ?? "the act"}'. Distributions resume only when the proceeding ends.";
            return null;
        }

        /// <summary>
        /// Distributes one parcel to an intestate heir through the
        /// administration pipeline: the acting fiduciary's authority is
        /// checked, the contest freeze is enforced, then the NX-3C
        /// distribution guard (creditors first, real heirs only) runs.
        /// </summary>
        public string AdministerParcelDistribution(
            Estate estate, EstateService estateService, string parcelId,
            int heirPersonId, string heirName, int appointmentPersonId,
            EntityIdRegistry ids, TitleAuthority titles, int dayIndex,
            WillContestService contestService = null, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.AdministerParcelDistribution: an estate is required.";
            if (estateService == null) return "EstateAdministration.AdministerParcelDistribution: the estate service is required.";
            string refusal = GuardAct(estate, appointmentPersonId, $"distribute parcel '{parcelId}'", diag);
            if (refusal != null) return refusal;
            refusal = RequireNoLiveContest(estate, contestService, "parcel distribution", diag);
            if (refusal != null) return refusal;
            refusal = estateService.DistributeParcel(estate, parcelId, heirPersonId, heirName,
                ids, titles, dayIndex, diag);
            if (refusal != null) return refusal;
            RecordExecutorAct(estate, appointmentPersonId,
                $"distributed parcel '{parcelId}' to heir P{heirPersonId} ({heirName}) by inheritance", dayIndex, diag);
            return null;
        }

        /// <summary>
        /// Distributes one parcel bequest through the administration
        /// pipeline: fiduciary authority, contest freeze, then the NX-3C
        /// probate guards (probated, uncontested, debts settled, real
        /// devise). Cash and personal-property bequests are recorded by the
        /// caller through their custodians; this service guards only the
        /// parcel path it can verify against the T2F chain.
        /// </summary>
        public string AdministerBequestDistribution(
            Estate estate, ProbateService probateService, Will will, Bequest bequest,
            int appointmentPersonId, EntityIdRegistry ids, TitleAuthority titles,
            int dayIndex, WillContestService contestService = null, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.AdministerBequestDistribution: an estate is required.";
            if (probateService == null) return "EstateAdministration.AdministerBequestDistribution: the probate service is required.";
            string refusal = GuardAct(estate, appointmentPersonId, "distribute a bequest", diag);
            if (refusal != null) return refusal;
            refusal = RequireNoLiveContest(estate, contestService, "bequest distribution", diag);
            if (refusal != null) return refusal;
            refusal = probateService.DistributeBequest(will, estate, bequest, ids, titles, dayIndex, diag);
            if (refusal != null) return refusal;
            RecordExecutorAct(estate, appointmentPersonId,
                $"distributed {bequest.Kind} bequest to beneficiary P{bequest.BeneficiaryPersonId} ({bequest.BeneficiaryName}) under will {will.WillId}", dayIndex, diag);
            return null;
        }

        /// <summary>
        /// Disburses the residue — what remains after every claim is settled
        /// and every bequest distributed — to the named residuary taker.
        /// Refused while any claim is unsettled, while undistributed
        /// bequests remain, or while a contest is live.
        /// </summary>
        public string RecordResidueDisbursement(
            Estate estate, Will will, int beneficiaryPersonId, string beneficiaryName,
            int appointmentPersonId, int dayIndex,
            WillContestService contestService = null, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.RecordResidueDisbursement: an estate is required.";
            string refusal = GuardAct(estate, appointmentPersonId, "disburse the residue", diag);
            if (refusal != null) return refusal;
            refusal = RequireNoLiveContest(estate, contestService, "residue disbursement", diag);
            if (refusal != null) return refusal;
            EstateClaimRank? next = NextUnpaidRank(estate.EstateId);
            if (next != null)
                return $"EstateAdministration.RecordResidueDisbursement: rank {next} claims unsettled — the residue is last, always.";
            if (will != null)
            {
                foreach (Bequest bequest in will.Bequests)
                {
                    if (bequest == null || bequest.Distributed || bequest.InvalidatedByContest || bequest.HeldForWitnessConflict)
                        continue;
                    if (EstateAbatementClassFor(bequest.Kind) == EstateAbatementClass.General)
                        return $"EstateAdministration.RecordResidueDisbursement: general bequest to P{bequest.BeneficiaryPersonId} undistributed — bequests before residue.";
                }
            }
            if (!accounts.TryGetValue(estate.EstateId, out EstateAccount account))
                return $"EstateAdministration.RecordResidueDisbursement: estate {estate.EstateId} has no account.";
            if (account.BalanceCents <= 0)
                return $"EstateAdministration.RecordResidueDisbursement: nothing remains — the residue is {account.BalanceCents}c.";
            if (beneficiaryPersonId < 0)
                return "EstateAdministration.RecordResidueDisbursement: the residuary taker must be a real person — no conjured beneficiaries.";
            account.Entries.Add(new EstateAccountEntry
            {
                Kind = EstateAccountEntryKind.Disbursement,
                DayIndex = dayIndex,
                AmountCents = account.BalanceCents,
                Memo = $"residue to P{beneficiaryPersonId} ({beneficiaryName})",
                RecordedByPersonId = appointmentPersonId,
            });
            RecordExecutorAct(estate, appointmentPersonId,
                $"disbursed the residue to P{beneficiaryPersonId} ({beneficiaryName})", dayIndex, diag);
            diag.Add($"EstateAdministration: residue disbursed to P{beneficiaryPersonId} ({beneficiaryName}) — account balance now {account.BalanceCents}c.");
            return null;
        }

        /// <summary>
        /// Closes the administration: no live contest, every claim settled,
        /// the account empty, then the NX-3C closure guard (debts settled,
        /// parcels distributed) runs. The decedent is archived by the caller
        /// via the T2H PersonArchive — this only verifies readiness.
        /// </summary>
        public string CloseAdministration(
            Estate estate, EstateService estateService, int appointmentPersonId,
            int dayIndex, WillContestService contestService = null, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateAdministration.CloseAdministration: an estate is required.";
            if (estateService == null) return "EstateAdministration.CloseAdministration: the estate service is required.";
            string refusal = GuardAct(estate, appointmentPersonId, "close the administration", diag);
            if (refusal != null) return refusal;
            refusal = RequireNoLiveContest(estate, contestService, "closure", diag);
            if (refusal != null) return refusal;
            EstateClaimRank? next = NextUnpaidRank(estate.EstateId);
            if (next != null)
                return $"EstateAdministration.CloseAdministration: rank {next} claims unsettled.";
            if (accounts.TryGetValue(estate.EstateId, out EstateAccount account) && account.BalanceCents > 0)
                return $"EstateAdministration.CloseAdministration: the estate account still holds {account.BalanceCents}c — disburse the residue first.";
            refusal = estateService.CloseEstate(estate, diag);
            if (refusal != null) return refusal;
            RecordExecutorAct(estate, appointmentPersonId, "closed the administration", dayIndex, diag);
            return null;
        }

        #endregion

        #region Save / Load

        [Serializable]
        public sealed class EstateAdministrationSaveDto
        {
            public List<EstateAdministratorAppointment> Appointments = new List<EstateAdministratorAppointment>();
            public List<EstateInventoryLine> InventoryLines = new List<EstateInventoryLine>();
            public List<EstateCreditorClaim> Claims = new List<EstateCreditorClaim>();
            public List<EstateAccount> Accounts = new List<EstateAccount>();
            public List<EstateExecutorAct> Acts = new List<EstateExecutorAct>();
            public List<EstateCompensationScheduleEntry> CompensationSchedules = new List<EstateCompensationScheduleEntry>();
            public List<EstateAbatementRecord> Abatements = new List<EstateAbatementRecord>();
        }

        [Serializable]
        public sealed class EstateCompensationScheduleEntry
        {
            public string EstateId = string.Empty;
            public ExecutorCompensationSchedule Schedule = new ExecutorCompensationSchedule();

            public EstateCompensationScheduleEntry() { }
        }

        public EstateAdministrationSaveDto CaptureSaveDto()
        {
            var dto = new EstateAdministrationSaveDto();
            dto.Appointments.AddRange(appointments.Values);
            foreach (List<EstateInventoryLine> lines in inventories.Values) dto.InventoryLines.AddRange(lines);
            foreach (List<EstateCreditorClaim> list in claims.Values) dto.Claims.AddRange(list);
            dto.Accounts.AddRange(accounts.Values);
            dto.Acts.AddRange(acts);
            foreach (KeyValuePair<string, ExecutorCompensationSchedule> kvp in compensationSchedules)
                dto.CompensationSchedules.Add(new EstateCompensationScheduleEntry { EstateId = kvp.Key, Schedule = kvp.Value });
            dto.Abatements.AddRange(abatements);
            return dto;
        }

        public void LoadFromSaveDto(EstateAdministrationSaveDto dto)
        {
            appointments.Clear();
            inventories.Clear();
            claims.Clear();
            accounts.Clear();
            acts.Clear();
            compensationSchedules.Clear();
            abatements.Clear();
            if (dto == null) return;
            foreach (EstateAdministratorAppointment appointment in dto.Appointments)
            {
                if (appointment == null) continue;
                appointments[appointment.AppointmentId] = appointment;
            }
            foreach (EstateInventoryLine line in dto.InventoryLines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.EstateId)) continue;
                if (!inventories.TryGetValue(line.EstateId, out List<EstateInventoryLine> lines))
                {
                    lines = new List<EstateInventoryLine>();
                    inventories[line.EstateId] = lines;
                }
                lines.Add(line);
            }
            foreach (EstateCreditorClaim claim in dto.Claims)
            {
                if (claim == null) continue;
                if (!claims.TryGetValue(claim.EstateId, out List<EstateCreditorClaim> list))
                {
                    list = new List<EstateCreditorClaim>();
                    claims[claim.EstateId] = list;
                }
                list.Add(claim);
            }
            foreach (EstateAccount account in dto.Accounts)
            {
                if (account == null) continue;
                accounts[account.EstateId] = account;
            }
            foreach (EstateExecutorAct act in dto.Acts)
            {
                if (act == null) continue;
                acts.Add(act);
            }
            foreach (EstateCompensationScheduleEntry entry in dto.CompensationSchedules)
            {
                if (entry == null || entry.Schedule == null) continue;
                compensationSchedules[entry.EstateId] = entry.Schedule;
            }
            foreach (EstateAbatementRecord record in dto.Abatements)
            {
                if (record == null) continue;
                abatements.Add(record);
            }
        }

        #endregion

        #region Internals

        private EstateAdministratorAppointment NewAppointment(
            EntityIdRegistry ids, Estate estate, int personId, string name,
            EstateAppointmentKind kind, int dayIndex, List<string> diag)
        {
            var appointment = new EstateAdministratorAppointment
            {
                AppointmentId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                EstateId = estate.EstateId,
                PersonId = personId,
                Name = name ?? string.Empty,
                Kind = kind,
                AppointedDayIndex = dayIndex,
            };
            appointments[appointment.AppointmentId] = appointment;
            return appointment;
        }

        private string CheckPerson(Estate estate, int personId, PopulationState population, List<string> diag)
        {
            if (personId < 0)
            {
                diag.Add("EstateAdministration: the fiduciary must be a real person.");
                return "refused";
            }
            if (personId == estate.DecedentPersonId)
            {
                diag.Add("EstateAdministration: the decedent cannot administer their own estate.");
                return "refused";
            }
            if (population != null && population.GetPerson(personId) == null)
            {
                diag.Add($"EstateAdministration: P{personId} is not a known person.");
                return "refused";
            }
            return null;
        }

        private string GuardAct(Estate estate, int personId, string actDescription, List<string> diag)
        {
            return RequireAuthority(estate, personId, actDescription, diag);
        }

        private void AddInventoryLine(string estateId, EstateInventoryLine line, List<string> diag)
        {
            line.EstateId = estateId;
            if (!inventories.TryGetValue(estateId, out List<EstateInventoryLine> lines))
            {
                lines = new List<EstateInventoryLine>();
                inventories[estateId] = lines;
            }
            lines.Add(line);
            diag.Add($"EstateAdministration: estate {estateId} inventory — {line.Kind} '{line.ReferenceId}' ({line.Description}) ≈ {line.EstimatedValueCents}c [{line.Source}].");
        }

        private EstateCreditorClaim RecordEstateObligation(
            Estate estate, EstateService estateService, EstateClaimRank rank,
            string obligationId, int amountCents, string description,
            int appointmentPersonId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) { diag.Add("EstateAdministration: an estate is required."); return null; }
            string refusal = RequireAuthority(estate, appointmentPersonId, $"record {rank} obligation", diag);
            if (refusal != null) { diag.Add(refusal); return null; }
            if (amountCents <= 0)
            {
                diag.Add("EstateAdministration: an estate obligation must be positive.");
                return null;
            }
            foreach (EstateCreditorClaim existing in ClaimsFor(estate.EstateId))
            {
                if (string.Equals(existing.DebtInstrumentId, obligationId, StringComparison.Ordinal))
                {
                    diag.Add($"EstateAdministration: '{obligationId}' already has claim {existing.ClaimId} — one claim per debt.");
                    return null;
                }
            }
            if (estateService != null)
                estateService.RecordEstateDebt(estate, obligationId, diag);

            var claim = new EstateCreditorClaim
            {
                ClaimId = Guid.NewGuid().ToString("N"),
                EstateId = estate.EstateId,
                DebtInstrumentId = obligationId,
                CreditorName = description ?? rank.ToString(),
                Rank = rank,
                AmountCents = amountCents,
                InstrumentKindName = string.Empty,
                Validated = true,
                ValidatedDetail = $"estate obligation recorded on the estate (no pre-existing instrument; rank {rank})",
            };
            if (!claims.TryGetValue(estate.EstateId, out List<EstateCreditorClaim> list))
            {
                list = new List<EstateCreditorClaim>();
                claims[estate.EstateId] = list;
            }
            list.Add(claim);
            estate.Status = EstateStatus.DebtsSettling;
            diag.Add($"EstateAdministration: {rank} obligation {claim.ClaimId} recorded — '{obligationId}' {amountCents}c ({description}).");
            return claim;
        }

        /// <summary>
        /// Cross-checks the supplied instrument against the estate: the
        /// object must BE the named debt (id equality), must be live paper,
        /// must name the decedent as the obligor, must rank consistently
        /// with its kind, and the claimed amount cannot exceed the recorded
        /// obligation. Estate-obligation ranks (administration, funeral,
        /// taxes) take no instrument — those are estate debts, not
        /// pre-existing paper.
        /// </summary>
        private static string ValidateInstrumentAgainstEstate(
            Estate estate, string debtInstrumentId, EstateClaimRank rank,
            int claimedAmountCents, ICreditInstrument instrument,
            ref string instrumentKindName, ref string creditorName, ref string validatedDetail)
        {
            bool isEstateObligationRank =
                rank == EstateClaimRank.AdministrationCosts ||
                rank == EstateClaimRank.FuneralCosts ||
                rank == EstateClaimRank.PreferredTaxes;
            if (isEstateObligationRank)
            {
                if (instrument != null)
                    return $"EstateAdministration.RecordCreditorClaim: rank {rank} is an estate obligation — it takes no pre-existing instrument. Record it through the obligation path.";
                creditorName = rank.ToString();
                validatedDetail = $"estate obligation recorded on estate {estate.EstateId} (no pre-existing instrument; rank {rank})";
                return null;
            }
            if (instrument == null)
                return "EstateAdministration.RecordCreditorClaim: a secured or unsecured debt claim requires the instrument object — amounts are cross-checked, never taken on word.";

            if (!string.Equals(instrument.InstrumentId.ToString(), debtInstrumentId, StringComparison.Ordinal))
                return $"EstateAdministration.RecordCreditorClaim: the instrument {instrument.InstrumentId} is not the named debt '{debtInstrumentId}' — no proxy claims.";
            if (instrument.Status == CreditInstrumentStatus.Satisfied || instrument.Status == CreditInstrumentStatus.Released)
                return $"EstateAdministration.RecordCreditorClaim: {debtInstrumentId} is {instrument.Status} — discharged paper is not a claim.";
            if (instrument is GuarantyAgreement)
                return $"EstateAdministration.RecordCreditorClaim: {debtInstrumentId} is a guaranty — contingent exposure, not a payable claim until called.";

            instrumentKindName = instrument.Kind.ToString();
            int obligationCents;
            bool secured;
            string encumberedAsset = string.Empty;
            string obligorOk = ObligorCheck(estate, instrument, out obligationCents, out secured, ref creditorName, ref encumberedAsset);
            if (obligorOk != null) return obligorOk;

            if (rank == EstateClaimRank.SecuredDebt && !secured)
                return $"EstateAdministration.RecordCreditorClaim: {debtInstrumentId} ({instrument.Kind}) is not secured — a secured rank needs the mortgage, lien, or collateralized note.";
            if (rank == EstateClaimRank.UnsecuredCreditor && secured)
                return $"EstateAdministration.RecordCreditorClaim: {debtInstrumentId} ({instrument.Kind}) IS secured — rank it SecuredDebt, never below its kind.";

            if (claimedAmountCents > obligationCents)
                return $"EstateAdministration.RecordCreditorClaim: {debtInstrumentId} records {obligationCents}c — the claim cannot exceed the recorded obligation.";
            validatedDetail = $"{instrument.Kind} '{debtInstrumentId}' — decedent is the obligor; {obligationCents}c recorded; " +
                (secured ? "secured" : "unsecured") +
                (!string.IsNullOrWhiteSpace(encumberedAsset) ? $"; encumbers '{encumberedAsset}'" : "") +
                $"; rank {rank}.";
            return null;
        }

        private static string ObligorCheck(
            Estate estate, ICreditInstrument instrument,
            out int obligationCents, out bool secured, ref string creditorName, ref string encumberedAsset)
        {
            obligationCents = 0;
            secured = false;
            string decedent = estate.DecedentName;
            if (instrument is PromissoryNote note)
            {
                if (!string.Equals(note.MakerName, decedent, StringComparison.Ordinal))
                    return $"EstateAdministration.RecordCreditorClaim: note {instrument.InstrumentId} was made by '{note.MakerName}', not the decedent — not the estate's debt.";
                obligationCents = Math.Max(0, note.PrincipalCents);
                secured = note.CollateralEquipmentAssetIds != null && note.CollateralEquipmentAssetIds.Count > 0;
                creditorName = string.IsNullOrWhiteSpace(note.HolderName) ? note.PayeeName : note.HolderName;
                if (secured) encumberedAsset = string.Join(",", note.CollateralEquipmentAssetIds.ToArray());
                return null;
            }
            if (instrument is SellerFinanceNote sellerNote)
            {
                if (!string.Equals(sellerNote.BuyerName, decedent, StringComparison.Ordinal))
                    return $"EstateAdministration.RecordCreditorClaim: seller-finance note {instrument.InstrumentId} was bought by '{sellerNote.BuyerName}', not the decedent.";
                obligationCents = Math.Max(0, sellerNote.FinancedCents);
                secured = !string.IsNullOrWhiteSpace(sellerNote.AssetInstanceId) &&
                    estate.BusinessInstanceIds.Contains(sellerNote.AssetInstanceId);
                creditorName = sellerNote.SellerName;
                if (secured) encumberedAsset = sellerNote.AssetInstanceId;
                return null;
            }
            if (instrument is MortgageDeed mortgage)
            {
                if (!string.Equals(mortgage.BorrowerName, decedent, StringComparison.Ordinal))
                    return $"EstateAdministration.RecordCreditorClaim: mortgage {instrument.InstrumentId} was borrowed by '{mortgage.BorrowerName}', not the decedent.";
                obligationCents = Math.Max(0, mortgage.PrincipalCents);
                secured = true;
                creditorName = mortgage.LenderName;
                encumberedAsset = mortgage.PropertyId;
                return null;
            }
            if (instrument is PropertyLien lien)
            {
                if (!string.Equals(lien.DebtorName, decedent, StringComparison.Ordinal))
                    return $"EstateAdministration.RecordCreditorClaim: lien {instrument.InstrumentId} names debtor '{lien.DebtorName}', not the decedent.";
                if (!estate.ParcelIds.Contains(lien.PropertyOrAssetId) &&
                    !estate.BusinessInstanceIds.Contains(lien.PropertyOrAssetId))
                    return $"EstateAdministration.RecordCreditorClaim: lien {instrument.InstrumentId} clouds '{lien.PropertyOrAssetId}', which is not inventoried on estate {estate.EstateId}.";
                obligationCents = Math.Max(0, lien.AmountCents);
                secured = true;
                creditorName = lien.ClaimantName;
                encumberedAsset = lien.PropertyOrAssetId;
                return null;
            }
            return $"EstateAdministration.RecordCreditorClaim: {instrument.Kind} is not a payable estate debt here.";
        }

        private static EstateAbatementClass EstateAbatementClassFor(BequestKind kind)
        {
            return kind == BequestKind.CashAmount ? EstateAbatementClass.General : EstateAbatementClass.Specific;
        }

        #endregion
    }
}
