using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Postal
{
    /// <summary>
    /// D4E: the US postal money-order fee schedule in force for the game's
    /// 1870 setting — the post-1866 reform schedule. Researched per standing
    /// instruction (1864 PMG instructions; Milgram, "Money Order Business",
    /// American Philatelist, Oct 2011; Hines &amp; Velk 2011):
    ///
    /// - 1864 (Nov 1, 1864 – 1865): max $30, min $1; 10c for $1–$10,
    ///   15c for $10.01–$20, 20c for $20.01–$30.
    /// - 1866 reform (in force in 1870): max raised to $50; 10c for orders
    ///   of $20 or less, 25c for orders exceeding $20.
    ///
    /// The 1866 schedule is the period-correct one for 1870; the 1864
    /// original is documented here for calibration.
    /// </summary>
    public static class MoneyOrderFeeSchedule
    {
        /// <summary>Minimum face value: $1 (1864 Act debate fixed the minimum at one dollar).</summary>
        public const int MinFaceCents = 100;
        /// <summary>Maximum face value: $50 (raised from $30 in 1866; $100 only from 1883).</summary>
        public const int MaxFaceCents = 5000;
        /// <summary>Fee boundary: orders of $20 or less pay the lower fee (1866 reform).</summary>
        public const int LowFeeBoundaryCents = 2000;
        /// <summary>1866 fee for orders of $20 or less.</summary>
        public const int LowFeeCents = 10;
        /// <summary>1866 fee for orders exceeding $20.</summary>
        public const int HighFeeCents = 25;

        /// <summary>True when the amount is within the issuable range.</summary>
        public static bool IsIssuable(int amountCents)
        {
            return amountCents >= MinFaceCents && amountCents <= MaxFaceCents;
        }

        /// <summary>
        /// The period-correct fee for a face amount: 10c for $20 or less,
        /// 25c for more. Returns -1 when the amount is not issuable.
        /// </summary>
        public static int FeeForCents(int amountCents)
        {
            if (!IsIssuable(amountCents)) return -1;
            return amountCents <= LowFeeBoundaryCents ? LowFeeCents : HighFeeCents;
        }
    }

    /// <summary>
    /// D4E: class of a money-order office. Historical: offices were divided
    /// into two classes — first class offices were DEPOSITORIES in which
    /// second-class offices deposited their surplus money-order funds
    /// (1864 PMG General Principles, IV).
    /// </summary>
    public enum MoneyOrderOfficeClass
    {
        Unspecified = 0,
        /// <summary>Second-class office: deposits surplus money-order funds in its depository.</summary>
        Ordinary = 1,
        /// <summary>First-class office: a depository for second-class offices' surplus funds.</summary>
        Depository = 2,
    }

    /// <summary>D4E: lifecycle of one postal money order.</summary>
    public enum MoneyOrderStatus
    {
        Unspecified = 0,
        /// <summary>Issued; advice sent; awaiting presentation at the payable office.</summary>
        Issued = 1,
        /// <summary>Paid to the payee (or their single indorsee / collecting bank).</summary>
        Paid = 2,
        /// <summary>Repaid to the sender at the office of issue within the validity window.</summary>
        Repaid = 3,
        /// <summary>Not presented within 90 days of issue — invalid, not payable.</summary>
        Expired = 4,
        /// <summary>Replaced by a duplicate after loss — the original can never be paid.</summary>
        Duplicated = 5,
    }

    /// <summary>
    /// D4E: lifecycle of one postal settlement claim between two post
    /// offices. Mirrors the D4A InterbankClaimStatus pattern deliberately —
    /// same discipline, separate domain: the obligor here is the postal
    /// system, not a bank, so no InterbankClaim is ever created for these.
    /// </summary>
    public enum PostalClaimStatus
    {
        Unspecified = 0,
        /// <summary>Recorded, awaiting the next postal settlement cycle.</summary>
        Open = 1,
        /// <summary>Folded into a net settlement position — no longer individually payable.</summary>
        Netted = 2,
        /// <summary>Paid in full through its settlement position.</summary>
        Settled = 3,
    }

    /// <summary>
    /// D4E: lifecycle of a net settlement position between two post offices.
    /// Postal-domain twin of D4A's InterbankPositionStatus.
    /// </summary>
    public enum PostalPositionStatus
    {
        Unspecified = 0,
        /// <summary>Netted, awaiting settlement in real money-order funds.</summary>
        Open = 1,
        /// <summary>Paid in full — every comprised claim is settled.</summary>
        Settled = 2,
    }

    /// <summary>
    /// D4E: one designated money-order office's money-order books. Historical
    /// (1864 instructions §35): money-order accounts are kept SEPARATE AND
    /// DISTINCT from postage accounts — MoneyOrderCashCents is the office's
    /// money-order fund only, never mixed with postage revenue.
    /// </summary>
    [Serializable]
    public sealed class PostalMoneyOrderOffice
    {
        public string OfficeId = string.Empty;
        public MoneyOrderOfficeClass OfficeClass = MoneyOrderOfficeClass.Unspecified;
        /// <summary>Ordinary offices: the depository that holds their surplus.</summary>
        public string DepositoryOfficeId = string.Empty;
        /// <summary>
        /// PMG-authorized reserve (§49): cash the office may withhold so it is
        /// always ready to meet orders drawn upon it. Sweeps never take the
        /// office below this.
        /// </summary>
        public int ReserveCents;
        /// <summary>The office's money-order fund: issuance receipts in, redemptions out.</summary>
        public int MoneyOrderCashCents;
        /// <summary>Next serial for this office — numbered consecutively from 1 (§39).</summary>
        public int NextSerial = 1;
        /// <summary>Fee revenue awaiting ledger posting with provenance (real lots).</summary>
        public int UncollectedFeeCents;

        public PostalMoneyOrderOffice() { }
    }

    /// <summary>
    /// D4E: one postal money order — a named-payee instrument payable ONLY
    /// at the office named at issue (1864 General Principles, VIII). The
    /// order itself travels with the sender/payee; the ADVICE travels
    /// separately through the mail to the paying postmaster, who may not pay
    /// until the advice is received and the order matches it in every
    /// respect. Payment only in cash (coin, Treasury notes, national bank
    /// notes — §7); never on credit.
    /// </summary>
    [Serializable]
    public sealed class PostalMoneyOrder
    {
        public int SerialNumber;
        public string IssuingOfficeId = string.Empty;
        public string PayableOfficeId = string.Empty;
        public string SenderName = string.Empty;
        public string PayeeName = string.Empty;
        public int AmountCents;
        public int FeeCents;
        public int IssuedDayIndex;
        public MoneyOrderStatus Status = MoneyOrderStatus.Issued;
        /// <summary>The single permitted written indorsement (more than one is prohibited).</summary>
        public string IndorsedToName = string.Empty;
        public int IndorsedDayIndex = -1;
        /// <summary>
        /// Bank collecting as the payee's AGENT — historically a bank's
        /// collection stamp is NOT an indorsement, so this never consumes
        /// the single indorsement. W7/D4A link point (see MoneyOrderService).
        /// </summary>
        public string BankCollectionAgent = string.Empty;
        public int PaidDayIndex = -1;
        public string PaidToName = string.Empty;
        /// <summary>Replaced-by serial, when this order is a duplicate.</summary>
        public int ReplacesSerial;
        public bool FraudFlagged;
        public string FraudNote = string.Empty;
        public List<string> History = new List<string>();

        public PostalMoneyOrder() { }
    }

    /// <summary>
    /// D4E: the advice — the corresponding form the issuing postmaster mails
    /// to the paying postmaster immediately after issue, so the paying
    /// office knows the remitter and payee BEFORE the order can be presented
    /// and can detect fraud. This is the service's Register of Advices.
    /// </summary>
    [Serializable]
    public sealed class PostalMoneyOrderAdvice
    {
        public int SerialNumber;
        public string IssuingOfficeId = string.Empty;
        public string PayableOfficeId = string.Empty;
        public string SenderName = string.Empty;
        public string PayeeName = string.Empty;
        public int AmountCents;
        public int SentDayIndex;
        public int DueArrivalDayIndex;
        public int ReceivedDayIndex = -1;

        public PostalMoneyOrderAdvice() { }
    }

    /// <summary>
    /// D4E: one post office's recorded claim on another post office's
    /// money-order funds — born when the paying office honors an order in
    /// real cash. The debtor is the ISSUING office (it took the sender's
    /// cash); the creditor is the PAYING office (it paid the payee's cash).
    /// Postal settlement only — never a bank claim.
    /// </summary>
    [Serializable]
    public sealed class PostalMoneyOrderClaim
    {
        public string ClaimId = string.Empty;
        public string DebtorOfficeId = string.Empty;   // the issuing office — it holds the sender's cash
        public string CreditorOfficeId = string.Empty; // the paying office — it paid real cash out
        public int AmountCents;
        public int OrderSerial;
        public string PayeeName = string.Empty;
        public int OriginatedDayIndex;
        public int SettledDayIndex = -1;
        public PostalClaimStatus Status = PostalClaimStatus.Open;

        public PostalMoneyOrderClaim() { }
    }

    /// <summary>
    /// D4E: the net of open claims between one pair of post offices after a
    /// postal settlement cycle. Bilateral netting per pair (mirrors the D4A
    /// discipline): settled in full from the debtor office's money-order
    /// fund — no partials; a shortfall is refused loudly, never papered
    /// over.
    /// </summary>
    [Serializable]
    public sealed class PostalSettlementPosition
    {
        public string PositionId = string.Empty;
        public string DebtorOfficeId = string.Empty;
        public string CreditorOfficeId = string.Empty;
        public int NetAmountCents;
        public List<string> ComprisedClaimIds = new List<string>();
        public int CycleDayIndex;
        public int SettledDayIndex = -1;
        public PostalPositionStatus Status = PostalPositionStatus.Open;

        public PostalSettlementPosition() { }
    }

    /// <summary>D4E: save data for the money-order authority.</summary>
    [Serializable]
    public sealed class MoneyOrderServiceSaveDto
    {
        public List<PostalMoneyOrderOffice> offices = new List<PostalMoneyOrderOffice>();
        public List<PostalMoneyOrder> orders = new List<PostalMoneyOrder>();
        public List<PostalMoneyOrderAdvice> advices = new List<PostalMoneyOrderAdvice>();
        public List<PostalMoneyOrderClaim> claims = new List<PostalMoneyOrderClaim>();
        public List<PostalSettlementPosition> positions = new List<PostalSettlementPosition>();
        public int sequence;
    }
}
