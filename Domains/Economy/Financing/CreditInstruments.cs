using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// T2A: the structurally distinct credit instruments. These are NEVER
    /// blurred into one generic "debt" type: a guaranty is contingent, a lien
    /// is a claim without possession, a mortgage is secured by real property,
    /// a seller-finance note is tied to a specific sale, and a promissory note
    /// is an unconditional written promise. Each has its own lifecycle.
    /// Historical basis: all five are standard 19th-century American commercial
    /// instruments (promissory notes under the law merchant; real-property
    /// mortgages; suretyship/guaranty). Terms are recorded as written.
    /// </summary>
    public enum CreditInstrumentKind
    {
        Unspecified = 0,
        PromissoryNote = 1,
        SellerFinanceNote = 2,
        Mortgage = 3,
        Lien = 4,
        Guaranty = 5,
    }

    public enum CreditInstrumentStatus
    {
        Unspecified = 0,
        Active = 1,
        Called = 2,     // guaranty called / lien enforced / note accelerated
        Satisfied = 3,  // paid in full through the agreement
        Released = 4,   // discharged without payment (e.g. guaranty released)
        Defaulted = 5,  // underlying obligation defaulted (pre-guaranty-call)
    }

    /// <summary>
    /// T2A: minimal index interface shared by all instruments. This is an
    /// index, not a blur — each instrument keeps its own class and lifecycle.
    /// </summary>
    public interface ICreditInstrument
    {
        EntityId InstrumentId { get; }
        CreditInstrumentKind Kind { get; }
        CreditInstrumentStatus Status { get; }
    }

    /// <summary>
    /// T2A: a named credit counterparty — a bank or a private lender/individual.
    /// There are no anonymous lenders anywhere in the credit system.
    /// </summary>
    [Serializable]
    public sealed class CreditCounterparty
    {
        public string Name = string.Empty;
        public bool IsBank;
        public string Notes = string.Empty;

        public CreditCounterparty() { }
    }

    /// <summary>
    /// T2A: an unconditional written promise by the maker to pay the payee a
    /// sum on stated terms. Distinct from a loan contract: a note evidences a
    /// promise; it does not disburse funds or carry a payment schedule itself.
    /// </summary>
    [Serializable]
    public sealed class PromissoryNote : ICreditInstrument
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public string MakerName = string.Empty;
        public string PayeeName = string.Empty;
        public int PrincipalCents;
        public string Terms = string.Empty; // rate, due date, as written
        public int SignedDayIndex;
        public CreditInstrumentStatus Status = CreditInstrumentStatus.Active;
        public List<string> CollateralEquipmentAssetIds = new List<string>();

        EntityId ICreditInstrument.InstrumentId => InstrumentId;
        CreditInstrumentKind ICreditInstrument.Kind => CreditInstrumentKind.PromissoryNote;
        CreditInstrumentStatus ICreditInstrument.Status => Status;

        public PromissoryNote() { }
    }

    /// <summary>
    /// T2A: the seller carries part of the purchase price. Tied to ONE
    /// specific asset sale — it cannot be repurposed to another deal.
    /// Canon §14.1 seller-debt scenario support.
    /// </summary>
    [Serializable]
    public sealed class SellerFinanceNote : ICreditInstrument
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public string SellerName = string.Empty;
        public string BuyerName = string.Empty;
        public string AssetDescription = string.Empty;
        public string AssetInstanceId = string.Empty;
        public int SalePriceCents;
        public int DownPaymentCents;
        public int FinancedCents;
        public string Terms = string.Empty;
        public int SignedDayIndex;
        public CreditInstrumentStatus Status = CreditInstrumentStatus.Active;

        EntityId ICreditInstrument.InstrumentId => InstrumentId;
        CreditInstrumentKind ICreditInstrument.Kind => CreditInstrumentKind.SellerFinanceNote;
        CreditInstrumentStatus ICreditInstrument.Status => Status;

        public SellerFinanceNote() { }
    }

    /// <summary>
    /// T2A: a loan secured by REAL PROPERTY. The property reference is a
    /// string id today; T2F's title authority will make it a real reference.
    /// A mortgage is not a generic loan: enforcement runs against the property.
    /// </summary>
    [Serializable]
    public sealed class MortgageDeed : ICreditInstrument
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public string BorrowerName = string.Empty;
        public string LenderName = string.Empty;
        public string PropertyId = string.Empty; // T2F title authority reference (forward-compatible)
        public string PropertyDescription = string.Empty;
        public int PrincipalCents;
        public string Terms = string.Empty;
        public int SignedDayIndex;
        public CreditInstrumentStatus Status = CreditInstrumentStatus.Active;

        EntityId ICreditInstrument.InstrumentId => InstrumentId;
        CreditInstrumentKind ICreditInstrument.Kind => CreditInstrumentKind.Mortgage;
        CreditInstrumentStatus ICreditInstrument.Status => Status;

        public MortgageDeed() { }
    }

    /// <summary>
    /// T2A: a claim against property or an asset securing an obligation.
    /// A lien does NOT transfer possession — it clouds title until satisfied.
    /// </summary>
    [Serializable]
    public sealed class PropertyLien : ICreditInstrument
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public string ClaimantName = string.Empty;
        public string DebtorName = string.Empty;
        public string PropertyOrAssetId = string.Empty;
        public int AmountCents;
        public string Reason = string.Empty;
        public int FiledDayIndex;
        public CreditInstrumentStatus Status = CreditInstrumentStatus.Active;

        EntityId ICreditInstrument.InstrumentId => InstrumentId;
        CreditInstrumentKind ICreditInstrument.Kind => CreditInstrumentKind.Lien;
        CreditInstrumentStatus ICreditInstrument.Status => Status;

        public PropertyLien() { }
    }

    /// <summary>
    /// T2A: a guarantor's CONTINGENT promise to answer for another's debt.
    /// The exposure is contingent, not principal: it sits off the liability
    /// ledger until the underlying debt defaults and the guaranty is called.
    /// Tech X §12.2 (Bakery Buyout): principal debt can be zero while
    /// contingent guaranteed exposure remains $100.
    /// </summary>
    [Serializable]
    public sealed class GuarantyAgreement : ICreditInstrument
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public string GuarantorName = string.Empty;
        public string CreditorName = string.Empty;
        public string DebtorName = string.Empty;
        public string CoveredInstrumentId = string.Empty; // the debt being guaranteed
        public int MaxExposureCents;
        public string Terms = string.Empty; // the agreement the call transitions through
        public int SignedDayIndex;
        public CreditInstrumentStatus Status = CreditInstrumentStatus.Active;
        /// <summary>Set when Called: the amount that became a real obligation (≤ MaxExposureCents).</summary>
        public int CalledAmountCents;

        EntityId ICreditInstrument.InstrumentId => InstrumentId;
        CreditInstrumentKind ICreditInstrument.Kind => CreditInstrumentKind.Guaranty;
        CreditInstrumentStatus ICreditInstrument.Status => Status;

        public GuarantyAgreement() { }
    }
}
