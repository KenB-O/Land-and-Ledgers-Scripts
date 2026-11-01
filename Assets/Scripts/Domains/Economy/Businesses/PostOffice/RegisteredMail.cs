using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Postal
{
    /// <summary>
    /// D4F: the US registered-mail fee schedule period-correct for the game's
    /// 1870 setting. Researched per standing instruction:
    ///
    /// - Registered mail began in 1855 (USPS, Significant Dates in USPS History).
    /// - The 1855 domestic registry fee was 5 cents; it was raised to 8 cents
    ///   only on January 1, 1893 (Richard Frajola, "New York City Registered
    ///   Labels: 1883–1911"). For 1870 the fee is 5 cents.
    /// - Return receipt: Act of June 8, 1872, c. 335, §128 (17 Stat. 300;
    ///   R.S. §3928): when the sender so requests, a receipt showing to whom
    ///   and when the article was delivered is obtained and returned to the
    ///   sender, prima facie evidence of delivery — 3 cents at time of
    ///   mailing, 5 cents afterward; +20 cents at mailing for the full
    ///   particulars (whom, when, and the address). The free-in-every-case
    ///   return receipt is later (Act of May 23, 1910), not 1870.
    /// - Indemnity: a registry-indemnity fee schedule appears in the Act of
    ///   June 8, 1872, §190 (17 Stat. 307), and the 1904/1905 Postal
    ///   Information guides state the sender "is indemnified for its value
    ///   up to $25". Whether indemnity was actually paid in 1870 (before the
    ///   1872 Act) is unsettled — see RegisteredMailService.IndemnityAvailable.
    ///
    /// Sources:
    /// - http://about.usps.com/who/profile/history/significant-dates.htm
    /// - https://www.rfrajola.com/WS19/WS1.pdf
    /// - https://stampsmarter.org/learning/GeneralSchemes/Postal%20Information(2rd%20Ed)(Dec%201,1905).pdf
    /// - 39 U.S.C. §§ 384–386 (1934), citing Act June 8, 1872, c. 335
    ///   (https://tile.loc.gov/storage-services/service/ll/uscode/uscode1934-00103/uscode1934-001039011/uscode1934-001039011.pdf)
    /// </summary>
    public static class RegisteredMailFeeSchedule
    {
        /// <summary>Registration fee in force 1855 – Dec 31, 1892: 5 cents.</summary>
        public const int RegistrationFeeCents = 5;
        /// <summary>Registry fee from Jan 1, 1893: 8 cents (documented here; NOT in force in 1870).</summary>
        public const int RegistrationFeeCentsFrom1893 = 8;
        /// <summary>1872 Act §128: return receipt requested at time of mailing: 3 cents.</summary>
        public const int ReturnReceiptAtMailingCents = 3;
        /// <summary>1872 Act §128: return receipt requested after mailing: 5 cents.</summary>
        public const int ReturnReceiptAfterMailingCents = 5;
        /// <summary>1872 Act §128: full particulars (whom, when, address) at mailing: +20 cents.</summary>
        public const int ReturnReceiptFullParticularsCents = 20;
        /// <summary>
        /// Base indemnity tier (1872 Act §190 / 1904–1905 Postal Information):
        /// loss of a registered article prepaid at the letter rate is
        /// indemnified for its value up to $25. See the indemnity fork note
        /// on RegisteredMailService.IndemnityAvailable.
        /// </summary>
        public const int IndemnityLimitCents = 2500;
    }

    /// <summary>D4F: lifecycle of one registered-mail record.</summary>
    public enum RegisteredMailStatus
    {
        Unspecified = 0,
        /// <summary>Registered; the item is in (or awaiting) the postal stream.</summary>
        Registered = 1,
        /// <summary>Delivered against recipient signature/identification.</summary>
        Delivered = 2,
        /// <summary>The chain broke — a recorded loss, never auto-resolved.</summary>
        Lost = 3,
    }

    /// <summary>
    /// D4F: one append-only custody-log entry. Every handoff of the registered
    /// item between offices/carriers is recorded here with the custodian —
    /// the historical chain of custody (receipts at each handoff).
    /// </summary>
    [Serializable]
    public sealed class RegisteredCustodyEntry
    {
        public int DayIndex;
        /// <summary>The office (or off-map gateway) holding the item after this handoff.</summary>
        public string CustodianOfficeId = string.Empty;
        public string Action = string.Empty;
        public string Note = string.Empty;

        public RegisteredCustodyEntry() { }
    }

    /// <summary>
    /// D4F: the registration record for one mail item — its tracking
    /// serial, the custody chain, the delivery receipt, and any loss event.
    /// The item itself keeps moving through the ordinary NX-2A postal
    /// stream; registration is a service layer on that movement, not a
    /// parallel system.
    /// </summary>
    [Serializable]
    public sealed class RegisteredMailRecord
    {
        public string SerialNumber = string.Empty;
        public string RegistryOfficeId = string.Empty;
        public string MailId = string.Empty;
        public string SenderName = string.Empty;
        public int SenderPersonId = -1;
        public string RecipientName = string.Empty;
        public int RecipientPersonId = -1;
        /// <summary>Declared value in cents (calibration — drives indemnity math).</summary>
        public int DeclaredValueCents;
        public int RegistrationFeeCents;
        public bool ReturnReceiptRequested;
        public bool ReturnReceiptFullParticulars;
        public int ReturnReceiptFeeCents;
        /// <summary>The return-receipt mail item — a REAL mail item traveling back through the mail.</summary>
        public string ReturnReceiptMailId = string.Empty;
        public int RegisteredDayIndex;
        public RegisteredMailStatus Status = RegisteredMailStatus.Registered;
        public List<RegisteredCustodyEntry> CustodyLog = new List<RegisteredCustodyEntry>();
        public string DeliverySignatureName = string.Empty;
        public int DeliveredDayIndex = -1;
        /// <summary>Last known custodian when the chain broke (loss) or delivered.</summary>
        public string LastKnownCustodianOfficeId = string.Empty;
        public int LossRecordedDayIndex = -1;
        public string LossReason = string.Empty;
        /// <summary>Computed indemnity owed (cents) — computed, never auto-paid.</summary>
        public int IndemnityComputedCents;
        public List<string> History = new List<string>();

        public RegisteredMailRecord() { }
    }

    /// <summary>D4F: per-office registry-number state — offices number registered articles consecutively.</summary>
    [Serializable]
    public sealed class RegistryNumberState
    {
        public string OfficeId = string.Empty;
        public int NextNumber = 1;

        public RegistryNumberState() { }
    }

    /// <summary>D4F: save data for the registered-mail authority.</summary>
    [Serializable]
    public sealed class RegisteredMailSaveDto
    {
        public List<RegisteredMailRecord> records = new List<RegisteredMailRecord>();
        public List<RegistryNumberState> numberStates = new List<RegistryNumberState>();
        public int uncollectedFeeCents;
        public bool indemnityAvailable;
    }
}
