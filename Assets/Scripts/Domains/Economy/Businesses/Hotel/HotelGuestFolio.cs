using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// D2A: what a guest folio line records — charges against the guest
    /// (positive) and payments the guest made (negative).
    /// </summary>
    public enum HotelFolioLineKind
    {
        /// <summary>A nightly room charge posted from a room-night sale.</summary>
        RoomNightCharge = 0,
        /// <summary>Board (dining room meals) charged to the guest's account.</summary>
        BoardCharge = 1,
        /// <summary>A weekly rent-due posted from weekly settlement.</summary>
        WeeklyRentCharge = 2,
        /// <summary>A monthly rent-due posted from monthly settlement.</summary>
        MonthlyRentCharge = 3,
        /// <summary>Payment the guest made toward the account (negative cents).</summary>
        Payment = 4,
    }

    /// <summary>D2A: one signed line on a guest's folio — a charge or a payment.</summary>
    [Serializable]
    public sealed class HotelFolioLine
    {
        public int DayIndex;
        public HotelFolioLineKind Kind = HotelFolioLineKind.RoomNightCharge;
        public string Description = string.Empty;
        /// <summary>Signed cents: charges positive, payments negative.</summary>
        public int AmountCents;

        public HotelFolioLine() { }
    }

    /// <summary>
    /// D2A: one guest's running account. Canon §8.1D: the owner sets
    /// "credit/account tolerance" — a guest on account terms may carry a
    /// balance up to the proprietor's tolerance; beyond it the account is
    /// flagged loudly for settlement. The folio is an account RECORD:
    /// money still moves only through ledger authorities (the W3A rule),
    /// and the proprietor records a payment here when one lands.
    /// </summary>
    [Serializable]
    public sealed class HotelGuestFolio
    {
        public int PersonId;

        /// <summary>The proprietor's credit tolerance for this guest, in cents. 0 = cash terms only.</summary>
        public int CreditToleranceCents;

        public List<HotelFolioLine> Lines = new List<HotelFolioLine>();

        public HotelGuestFolio() { }

        /// <summary>Running balance: positive = the guest owes the house.</summary>
        public int BalanceCents
        {
            get
            {
                int balance = 0;
                if (Lines != null)
                    for (int i = 0; i < Lines.Count; i++)
                        if (Lines[i] != null) balance += Lines[i].AmountCents;
                return balance;
            }
        }

        /// <summary>True while the guest's balance stays inside the proprietor's tolerance.</summary>
        public bool WithinTolerance => BalanceCents <= Math.Max(0, CreditToleranceCents);

        public int TotalChargedCents
        {
            get
            {
                int total = 0;
                if (Lines != null)
                    for (int i = 0; i < Lines.Count; i++)
                        if (Lines[i] != null && Lines[i].AmountCents > 0) total += Lines[i].AmountCents;
                return total;
            }
        }

        public int TotalPaidCents
        {
            get
            {
                int total = 0;
                if (Lines != null)
                    for (int i = 0; i < Lines.Count; i++)
                        if (Lines[i] != null && Lines[i].AmountCents < 0) total -= Lines[i].AmountCents;
                return total;
            }
        }
    }

    /// <summary>
    /// D2A: the hotel's guest folios — running accounts per guest. A folio
    /// opens at check-in for account guests (tolerance from the
    /// proprietor's policy) or stays closed for cash guests (charges
    /// settled at the desk). Charges post from room-night sales and
    /// weekly/monthly rent-due; payments post when the proprietor records
    /// them. A folio closed with a balance above tolerance is a DEBT
    /// record — the house records it loudly rather than forgetting it.
    /// </summary>
    public sealed class HotelGuestFolios
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelGuestFolio> folios = new List<HotelGuestFolio>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelGuestFolio> Folios => folios;

        /// <summary>Opens a folio at check-in. Tolerance 0 = cash terms (folio records only, pays as they go).</summary>
        public string OpenFolio(int personId, int creditToleranceCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "HotelGuestFolios.OpenFolio: a folio needs a real person id.";
            if (FindFolio(personId) != null)
                return $"HotelGuestFolios.OpenFolio: person {personId} already holds an open folio.";
            folios.Add(new HotelGuestFolio
            {
                PersonId = personId,
                CreditToleranceCents = Math.Max(0, creditToleranceCents),
            });
            diag.Add($"HotelGuestFolios: folio opened for person {personId} " +
                (creditToleranceCents > 0
                    ? $"(account terms, tolerance {creditToleranceCents}¢ — Canon §8.1D)."
                    : "(cash terms — pays as they go)."));
            return null;
        }

        public HotelGuestFolio FindFolio(int personId)
        {
            if (personId <= 0) return null;
            for (int i = 0; i < folios.Count; i++)
                if (folios[i] != null && folios[i].PersonId == personId)
                    return folios[i];
            return null;
        }

        /// <summary>
        /// Posts a charge (positive cents) to the guest's folio. Returns
        /// true when the posting puts the guest OVER the proprietor's
        /// tolerance — the caller says so loudly; D2A never settles or
        /// ejects on its own (that is proprietor action).
        /// </summary>
        public bool PostCharge(int personId, int dayIndex, HotelFolioLineKind kind,
            string description, int cents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            HotelGuestFolio folio = FindFolio(personId);
            if (folio == null)
            {
                diag.Add($"HotelGuestFolios: no open folio for person {personId} — charge '{description}' not recorded (cash guest; settles at the desk).");
                return false;
            }
            if (cents < 0)
            {
                diag.Add($"HotelGuestFolios: refused — a charge needs non-negative cents (person {personId}).");
                return false;
            }
            folio.Lines.Add(new HotelFolioLine
            {
                DayIndex = dayIndex,
                Kind = kind,
                Description = description ?? string.Empty,
                AmountCents = cents,
            });
            bool overTolerance = !folio.WithinTolerance;
            diag.Add($"HotelGuestFolios: person {personId} charged {cents}¢ ({kind}) — balance {folio.BalanceCents}¢ " +
                (overTolerance
                    ? $"OVER the {folio.CreditToleranceCents}¢ tolerance — settlement due (Canon §8.1D)."
                    : $"within tolerance ({folio.CreditToleranceCents}¢)."));
            return overTolerance;
        }

        /// <summary>
        /// Records a payment the guest made (money moves through a ledger
        /// authority; the proprietor records it here). Returns a
        /// rejection, or null on success.
        /// </summary>
        public string PostPayment(int personId, int dayIndex, int cents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            HotelGuestFolio folio = FindFolio(personId);
            if (folio == null)
                return $"HotelGuestFolios.PostPayment: person {personId} holds no open folio.";
            if (cents <= 0)
                return "HotelGuestFolios.PostPayment: a payment needs positive cents.";
            folio.Lines.Add(new HotelFolioLine
            {
                DayIndex = dayIndex,
                Kind = HotelFolioLineKind.Payment,
                Description = $"payment received from person {personId}, day {dayIndex}",
                AmountCents = -cents,
            });
            diag.Add($"HotelGuestFolios: person {personId} paid {cents}¢ — balance now {folio.BalanceCents}¢.");
            return null;
        }

        /// <summary>
        /// Closes the folio at checkout. A balance above tolerance is
        /// recorded as debt — loudly, never silently forgiven. Returns the
        /// closing balance; the caller settles it through ledger
        /// authorities.
        /// </summary>
        public int CloseFolio(int personId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            HotelGuestFolio folio = FindFolio(personId);
            if (folio == null) return 0;
            int balance = folio.BalanceCents;
            folios.Remove(folio);
            if (balance > 0)
                diag.Add($"HotelGuestFolios: person {personId} checked out owing {balance}¢ (day {dayIndex}) — " +
                    (balance > folio.CreditToleranceCents
                        ? "OVER the proprietor's tolerance: a DEBT record for the ledger, never silently forgiven."
                        : "inside tolerance; the house carries it on the guest's account."));
            else
                diag.Add($"HotelGuestFolios: person {personId}'s folio closed clean (day {dayIndex}).");
            return balance;
        }

        /// <summary>All folios over tolerance right now — the proprietor's settlement watch-list.</summary>
        public List<HotelGuestFolio> FoliosOverTolerance()
        {
            var over = new List<HotelGuestFolio>();
            foreach (HotelGuestFolio folio in folios)
            {
                if (folio == null) continue;
                if (!folio.WithinTolerance) over.Add(folio);
            }
            return over;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelGuestFoliosSaveDto
        {
            public List<HotelGuestFolio> Folios = new List<HotelGuestFolio>();
        }

        public HotelGuestFoliosSaveDto CaptureSaveDto()
        {
            var dto = new HotelGuestFoliosSaveDto();
            foreach (HotelGuestFolio folio in folios)
            {
                if (folio == null || folio.PersonId <= 0) continue;
                dto.Folios.Add(folio);
            }
            return dto;
        }

        public void LoadFromSaveDto(HotelGuestFoliosSaveDto dto)
        {
            folios.Clear();
            if (dto?.Folios == null) return;
            foreach (HotelGuestFolio folio in dto.Folios)
            {
                if (folio == null || folio.PersonId <= 0) continue;
                folio.Lines = folio.Lines ?? new List<HotelFolioLine>();
                folios.Add(folio);
            }
        }
        #endregion
    }
}
