using System;
using System.Collections.Generic;
using LandLedgers.Tasks;

namespace LandLedgers.Economy.Postal
{
    /// <summary>
    /// D4E: the postal money-order authority. Canon Part VII §7.1-7.2 (postal
    /// service as a real information network) and §16.4 (the post office as a
    /// real economic actor); historical mechanics researched per standing
    /// instruction — US postal money orders from Nov 1, 1864 (Act of May 17,
    /// 1864; PMG William Dennison Jr.'s instructions; Milgram, "Money Order
    /// Business", American Philatelist, Oct 2011; Hines &amp; Velk 2011):
    ///
    /// - Only DESIGNATED money-order offices issue and pay (1865: 419 of
    ///   them, starting with the larger offices). Two classes: first-class
    ///   DEPOSITORIES hold second-class offices' surplus money-order funds.
    /// - Named-payee instrument, payable ONLY at the office named at issue.
    ///   The ADVICE travels separately through the mail to the paying
    ///   postmaster; no payment without a matching advice (fraud control).
    /// - Sender pays cash + fee up front; never on credit. Fees are
    ///   post-office revenue (real lots, posted with provenance).
    /// - Money-order funds are kept SEPARATE from postage funds.
    /// - Validity: 90 days from issue; lost orders get a free duplicate;
    ///   one indorsement only; no money-order business on Sundays.
    /// - Settlement is POSTAL, between offices: a paying office that honors
    ///   an order in real cash gains a claim on the issuing office's
    ///   money-order funds, netted bilaterally and settled in full from real
    ///   office cash. This mirrors the D4A interbank claim discipline
    ///   (InterbankBalances.cs) but is a separate domain — the obligor is
    ///   the postal system, never a bank, so no InterbankClaim is created.
    ///
    /// W7/D4A link: a payee may leave the order with a bank FOR COLLECTION
    /// (RecordBankCollection — historically a bank's collection stamp is not
    /// an indorsement). The bank presents the order at the payable office
    /// for cash via RedeemOrder and settles with its customer on its own
    /// books; the D4A presentment path (InterbankSettlement.
    /// PresentInterbankPaper, InterbankPaperKind.MoneyOrder) covers
    /// bank-issued money orders between banks and is not duplicated here.
    /// </summary>
    public sealed class MoneyOrderService
    {
        /// <summary>Order validity: 90 days from date of issue (1864 Act — presented later, it is invalid).</summary>
        public const int ValidityDays = 90;
        /// <summary>Weekday convention matches PostalService: 0=Monday..6=Sunday (calibration).</summary>
        public const int SundayWeekday = 6;

        public const string IssueOrderTaskId = "moneyorder.issue-order";
        public const string PayOrderTaskId = "moneyorder.pay-order";

        private readonly PostalService postal;
        private readonly Dictionary<string, PostalMoneyOrderOffice> offices =
            new Dictionary<string, PostalMoneyOrderOffice>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PostalMoneyOrder> orders =
            new Dictionary<string, PostalMoneyOrder>(StringComparer.Ordinal);
        private readonly List<PostalMoneyOrderAdvice> advices = new List<PostalMoneyOrderAdvice>();
        private readonly Dictionary<string, PostalMoneyOrderClaim> claims =
            new Dictionary<string, PostalMoneyOrderClaim>(StringComparer.Ordinal);
        private readonly Dictionary<string, PostalSettlementPosition> positions =
            new Dictionary<string, PostalSettlementPosition>(StringComparer.Ordinal);
        private int sequence;

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public IReadOnlyCollection<PostalMoneyOrderOffice> Offices => offices.Values;
        public IReadOnlyCollection<PostalMoneyOrder> Orders => orders.Values;
        public IReadOnlyCollection<PostalMoneyOrderClaim> Claims => claims.Values;
        public IReadOnlyCollection<PostalSettlementPosition> Positions => positions.Values;

        public MoneyOrderService(PostalService postalService = null)
        {
            postal = postalService;
        }

        private static string OrderKey(string issuingOfficeId, int serialNumber)
        {
            return $"{issuingOfficeId}#{serialNumber}";
        }

        private PostalMoneyOrderOffice RequireOffice(string officeId, string op, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(officeId))
            {
                diag.Add($"MoneyOrderService.{op}: a money-order office is required.");
                return null;
            }
            if (!offices.TryGetValue(officeId, out PostalMoneyOrderOffice office))
            {
                diag.Add($"MoneyOrderService.{op}: '{officeId}' is not a designated money-order office — " +
                    "not every post office issues money orders (1864: larger offices first).");
                return null;
            }
            return office;
        }

        private static bool IsSunday(int dayIndex)
        {
            return ((dayIndex % 7) + 7) % 7 == SundayWeekday;
        }

        /// <summary>
        /// Designates a post office as a money-order office (not every post
        /// office qualifies). Ordinary (second-class) offices name the
        /// depository (first-class) office that holds their surplus funds.
        /// </summary>
        public string RegisterMoneyOrderOffice(
            string officeId, MoneyOrderOfficeClass officeClass, string depositoryOfficeId,
            int reserveCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "RegisterMoneyOrderOffice";
            if (string.IsNullOrWhiteSpace(officeId))
                return $"MoneyOrderService.{op}: an office id is required.";
            if (postal != null)
            {
                bool known = false;
                foreach (PostalOffice o in postal.Offices)
                    if (string.Equals(o.OfficeId, officeId, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                if (!known)
                    return $"MoneyOrderService.{op}: '{officeId}' is not a registered post office — designation is for real offices.";
            }
            if (officeClass == MoneyOrderOfficeClass.Unspecified)
                return $"MoneyOrderService.{op}: the office class must be stated — ordinary (second class) or depository (first class).";
            if (offices.ContainsKey(officeId))
                return $"MoneyOrderService.{op}: '{officeId}' is already designated — no duplicate designations.";
            if (reserveCents < 0)
                return $"MoneyOrderService.{op}: the reserve cannot be negative.";
            if (officeClass == MoneyOrderOfficeClass.Ordinary)
            {
                if (string.IsNullOrWhiteSpace(depositoryOfficeId))
                    return $"MoneyOrderService.{op}: an ordinary office must name its depository (1864 General Principles, IV).";
                if (!offices.TryGetValue(depositoryOfficeId, out PostalMoneyOrderOffice dep) ||
                    dep.OfficeClass != MoneyOrderOfficeClass.Depository)
                    return $"MoneyOrderService.{op}: '{depositoryOfficeId}' is not a designated depository — register it first.";
                if (string.Equals(depositoryOfficeId, officeId, StringComparison.OrdinalIgnoreCase))
                    return $"MoneyOrderService.{op}: an office is not its own depository.";
            }

            offices[officeId] = new PostalMoneyOrderOffice
            {
                OfficeId = officeId,
                OfficeClass = officeClass,
                DepositoryOfficeId = officeClass == MoneyOrderOfficeClass.Ordinary ? depositoryOfficeId : string.Empty,
                ReserveCents = reserveCents,
            };
            diag.Add($"MoneyOrderService: '{officeId}' designated a money-order office ({officeClass}, reserve {reserveCents}c).");
            return null;
        }

        /// <summary>
        /// Places the PMG-authorized reserve (§49) in the office's
        /// money-order fund so it is ready to meet orders drawn upon it. The
        /// caller supplies the real cash; this records it with provenance.
        /// </summary>
        public string EstablishReserve(string officeId, int amountCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "EstablishReserve";
            PostalMoneyOrderOffice office = RequireOffice(officeId, op, diag);
            if (office == null) return $"MoneyOrderService.{op}: unknown money-order office '{officeId}'.";
            if (amountCents <= 0)
                return $"MoneyOrderService.{op}: the reserve placement must be positive.";
            office.MoneyOrderCashCents += amountCents;
            diag.Add($"MoneyOrderService: {amountCents}c PMG-authorized reserve placed in '{officeId}' money-order fund (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Issues a money order: sender pays cash + the scheduled fee at the
        /// issuing office; the order names the payee and the payable office.
        /// The advice is mailed to the payable office at once (transit via
        /// the estimator — the operating layer passes
        /// PostalService.EstimateTransitDays). No credit, no Sundays (§59).
        /// </summary>
        public PostalMoneyOrder IssueOrder(
            string issuingOfficeId, string payableOfficeId, string senderName, string payeeName,
            int amountCents, int dayIndex, List<string> diag,
            Func<string, string, int> estimateTransitDays = null)
        {
            diag = diag ?? diagnostics;
            const string op = "IssueOrder";
            if (IsSunday(dayIndex))
            {
                diag.Add($"MoneyOrderService.{op}: REFUSED — no money-order business is transacted on Sundays (1864 instructions §59).");
                return null;
            }
            PostalMoneyOrderOffice issuing = RequireOffice(issuingOfficeId, op, diag);
            PostalMoneyOrderOffice payable = RequireOffice(payableOfficeId, op, diag);
            if (issuing == null || payable == null) return null;
            if (string.Equals(issuingOfficeId, payableOfficeId, StringComparison.OrdinalIgnoreCase))
            {
                diag.Add($"MoneyOrderService.{op}: an office draws upon another office — not upon itself.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(senderName) || string.IsNullOrWhiteSpace(payeeName))
            {
                diag.Add($"MoneyOrderService.{op}: sender and payee must both be named — the payee's name is the fraud control.");
                return null;
            }
            if (!MoneyOrderFeeSchedule.IsIssuable(amountCents))
            {
                diag.Add($"MoneyOrderService.{op}: REFUSED — {amountCents}c is outside the issuable range " +
                    $"${MoneyOrderFeeSchedule.MinFaceCents / 100}–${MoneyOrderFeeSchedule.MaxFaceCents / 100} (1866: max $50).");
                return null;
            }

            int fee = MoneyOrderFeeSchedule.FeeForCents(amountCents);
            int serial = issuing.NextSerial++;
            int transitDays = estimateTransitDays != null
                ? Math.Max(1, estimateTransitDays(issuingOfficeId, payableOfficeId))
                : 1;

            var order = new PostalMoneyOrder
            {
                SerialNumber = serial,
                IssuingOfficeId = issuingOfficeId,
                PayableOfficeId = payableOfficeId,
                SenderName = senderName,
                PayeeName = payeeName,
                AmountCents = amountCents,
                FeeCents = fee,
                IssuedDayIndex = dayIndex,
                Status = MoneyOrderStatus.Issued,
            };
            order.History.Add($"day {dayIndex}: issued at {issuingOfficeId} payable at {payableOfficeId} — " +
                $"${amountCents / 100}.{amountCents % 100:00} to {payeeName}, fee {fee}c (serial {serial}).");
            orders[OrderKey(issuingOfficeId, serial)] = order;

            advices.Add(new PostalMoneyOrderAdvice
            {
                SerialNumber = serial,
                IssuingOfficeId = issuingOfficeId,
                PayableOfficeId = payableOfficeId,
                SenderName = senderName,
                PayeeName = payeeName,
                AmountCents = amountCents,
                SentDayIndex = dayIndex,
                DueArrivalDayIndex = dayIndex + transitDays,
            });
            order.History.Add($"day {dayIndex}: advice mailed to {payableOfficeId} (due day {dayIndex + transitDays}).");

            issuing.MoneyOrderCashCents += amountCents;
            issuing.UncollectedFeeCents += fee;
            diag.Add($"MoneyOrderService: order {issuingOfficeId}#{serial} issued — {amountCents}c to {payeeName} at {payableOfficeId} (fee {fee}c).");
            return order;
        }

        /// <summary>
        /// Advances the money-order books one day: mailed advices arrive at
        /// the payable offices, and orders past their 90-day validity go
        /// invalid (not payable — the owner must apply to the Department).
        /// </summary>
        public void AdvanceDay(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (PostalMoneyOrderAdvice advice in advices)
            {
                if (advice.ReceivedDayIndex >= 0) continue;
                if (advice.DueArrivalDayIndex > dayIndex) continue;
                advice.ReceivedDayIndex = dayIndex;
                if (orders.TryGetValue(OrderKey(advice.IssuingOfficeId, advice.SerialNumber), out PostalMoneyOrder order))
                    order.History.Add($"day {dayIndex}: advice received at {advice.PayableOfficeId}.");
                diag.Add($"MoneyOrderService: advice for {advice.IssuingOfficeId}#{advice.SerialNumber} arrived at {advice.PayableOfficeId}.");
            }
            foreach (PostalMoneyOrder order in orders.Values)
            {
                if (order.Status != MoneyOrderStatus.Issued) continue;
                if (dayIndex - order.IssuedDayIndex > ValidityDays)
                {
                    order.Status = MoneyOrderStatus.Expired;
                    order.History.Add($"day {dayIndex}: EXPIRED — not presented within {ValidityDays} days; invalid, not payable.");
                    diag.Add($"MoneyOrderService: {order.IssuingOfficeId}#{order.SerialNumber} expired unpaid — invalid, not payable.");
                }
            }
        }

        public PostalMoneyOrder FindOrder(string issuingOfficeId, int serialNumber)
        {
            if (string.IsNullOrEmpty(issuingOfficeId)) return null;
            orders.TryGetValue(OrderKey(issuingOfficeId, serialNumber), out PostalMoneyOrder order);
            return order;
        }

        public PostalMoneyOrderAdvice FindAdvice(string issuingOfficeId, int serialNumber)
        {
            foreach (PostalMoneyOrderAdvice advice in advices)
                if (advice.SerialNumber == serialNumber &&
                    string.Equals(advice.IssuingOfficeId, issuingOfficeId, StringComparison.OrdinalIgnoreCase))
                    return advice;
            return null;
        }

        /// <summary>The office's money-order fund balance; -1 when the office is not designated.</summary>
        public int MoneyOrderCashCents(string officeId)
        {
            if (string.IsNullOrEmpty(officeId)) return -1;
            return offices.TryGetValue(officeId, out PostalMoneyOrderOffice office) ? office.MoneyOrderCashCents : -1;
        }

        private string FlagFraud(PostalMoneyOrder order, string note, int dayIndex, List<string> diag)
        {
            order.FraudFlagged = true;
            order.FraudNote = note;
            order.History.Add($"day {dayIndex}: FRAUD FLAG — {note}");
            diag.Add($"MoneyOrderService: FRAUD — {order.IssuingOfficeId}#{order.SerialNumber}: {note} Refused loudly; never auto-honored.");
            return $"MoneyOrderService: order {order.IssuingOfficeId}#{order.SerialNumber} REFUSED — {note}";
        }

        /// <summary>
        /// Pays the order at the payable office — in cash, from the office's
        /// money-order fund, to the named payee (or their single indorsee,
        /// or the bank collecting as their agent). Refuses loudly when the
        /// advice has not arrived, the particulars do not match the advice,
        /// the presenter is not entitled, the order is spent/expired, or the
        /// till is short. A paid order creates the paying office's postal
        /// claim on the issuing office.
        /// </summary>
        /// <returns>Null when paid; otherwise the loud refusal reason.</returns>
        public string RedeemOrder(string issuingOfficeId, int serialNumber, string presenterName, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "RedeemOrder";
            if (IsSunday(dayIndex))
                return $"MoneyOrderService.{op}: REFUSED — no money-order business is transacted on Sundays (1864 instructions §59).";
            PostalMoneyOrder order = FindOrder(issuingOfficeId, serialNumber);
            if (order == null)
                return $"MoneyOrderService.{op}: no money order {issuingOfficeId}#{serialNumber} on the register — refused.";
            if (order.FraudFlagged)
                return $"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is flagged fraudulent ({order.FraudNote}) — refused, never honored.";
            if (order.Status != MoneyOrderStatus.Issued)
                return $"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is {order.Status} — only a live issued order is payable.";

            PostalMoneyOrderAdvice advice = FindAdvice(issuingOfficeId, serialNumber);
            if (advice == null || advice.ReceivedDayIndex < 0 || advice.ReceivedDayIndex > dayIndex)
                return $"MoneyOrderService.{op}: REFUSED — the advice for {issuingOfficeId}#{serialNumber} has not been received at " +
                    $"{order.PayableOfficeId}. Postmasters are prohibited from paying an order of which the advice has not been received.";

            // The order must match the advice in every respect — this is the
            // whole fraud control of the 1864 system.
            if (order.AmountCents != advice.AmountCents)
                return FlagFraud(order, $"amount altered: order shows {order.AmountCents}c, advice shows {advice.AmountCents}c.", dayIndex, diag);
            if (!string.Equals(order.PayeeName, advice.PayeeName, StringComparison.Ordinal))
                return FlagFraud(order, $"payee altered: order shows '{order.PayeeName}', advice shows '{advice.PayeeName}'.", dayIndex, diag);

            bool isPayee = !string.IsNullOrEmpty(presenterName) &&
                string.Equals(presenterName, order.PayeeName, StringComparison.Ordinal);
            bool isIndorsee = !string.IsNullOrEmpty(order.IndorsedToName) &&
                string.Equals(presenterName, order.IndorsedToName, StringComparison.Ordinal);
            bool isCollectingBank = !string.IsNullOrEmpty(order.BankCollectionAgent) &&
                string.Equals(presenterName, order.BankCollectionAgent, StringComparison.Ordinal);
            if (!isPayee && !isIndorsee && !isCollectingBank)
                return $"MoneyOrderService.{op}: REFUSED — '{presenterName}' is not the payee '{order.PayeeName}'" +
                    (string.IsNullOrEmpty(order.IndorsedToName) ? "" : $" nor the indorsee '{order.IndorsedToName}'") + ".";

            PostalMoneyOrderOffice payable = RequireOffice(order.PayableOfficeId, op, diag);
            if (payable == null)
                return $"MoneyOrderService.{op}: the payable office '{order.PayableOfficeId}' is not designated.";
            if (payable.MoneyOrderCashCents < order.AmountCents)
            {
                diag.Add($"MoneyOrderService.{op}: REFUSED — '{payable.OfficeId}' money-order fund holds {payable.MoneyOrderCashCents}c " +
                    $"against {order.AmountCents}c. The till is short; draw on the depository or sweep surplus first. Refused, not faked.");
                return $"MoneyOrderService.{op}: '{payable.OfficeId}' cannot pay {order.AmountCents}c (fund {payable.MoneyOrderCashCents}c) — refused.";
            }

            payable.MoneyOrderCashCents -= order.AmountCents;
            order.Status = MoneyOrderStatus.Paid;
            order.PaidDayIndex = dayIndex;
            order.PaidToName = presenterName;
            order.History.Add($"day {dayIndex}: PAID {order.AmountCents}c in cash at {payable.OfficeId} to {presenterName} (signature to receipt).");

            var claim = new PostalMoneyOrderClaim
            {
                ClaimId = $"pmoc-{sequence++}",
                DebtorOfficeId = order.IssuingOfficeId,
                CreditorOfficeId = payable.OfficeId,
                AmountCents = order.AmountCents,
                OrderSerial = order.SerialNumber,
                PayeeName = order.PayeeName,
                OriginatedDayIndex = dayIndex,
                Status = PostalClaimStatus.Open,
            };
            claims[claim.ClaimId] = claim;
            diag.Add($"MoneyOrderService: {issuingOfficeId}#{serialNumber} paid {order.AmountCents}c at {payable.OfficeId} — " +
                $"postal claim {claim.ClaimId} on '{order.IssuingOfficeId}' money-order funds recorded.");
            return null;
        }

        /// <summary>
        /// The payee's single written indorsement to a second person. More
        /// than one indorsement is prohibited — a second indorsee must apply
        /// to the Department for a duplicate or warrant (beyond the horizon).
        /// </summary>
        public string IndorseOrder(string issuingOfficeId, int serialNumber, string indorseeName, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "IndorseOrder";
            PostalMoneyOrder order = FindOrder(issuingOfficeId, serialNumber);
            if (order == null)
                return $"MoneyOrderService.{op}: no money order {issuingOfficeId}#{serialNumber} on the register.";
            if (order.Status != MoneyOrderStatus.Issued || order.FraudFlagged)
                return $"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is not a live order — no indorsement.";
            if (string.IsNullOrWhiteSpace(indorseeName))
                return $"MoneyOrderService.{op}: the indorsee must be named.";
            if (!string.IsNullOrEmpty(order.IndorsedToName))
                return $"MoneyOrderService.{op}: REFUSED — {issuingOfficeId}#{serialNumber} is already indorsed to '{order.IndorsedToName}'. " +
                    "More than one indorsement is prohibited; a second indorsee must apply to the Department.";
            order.IndorsedToName = indorseeName;
            order.IndorsedDayIndex = dayIndex;
            order.History.Add($"day {dayIndex}: indorsed by {order.PayeeName} to {indorseeName} (the single permitted indorsement).");
            diag.Add($"MoneyOrderService: {issuingOfficeId}#{serialNumber} indorsed to '{indorseeName}'.");
            return null;
        }

        /// <summary>
        /// W7/D4A LINK: the payee leaves the order with a bank FOR
        /// COLLECTION. The bank's collection stamp is NOT an indorsement
        /// (1864 instructions) — the single indorsement stays available.
        /// The bank presents the order at the payable office via RedeemOrder
        /// and settles with its customer on its own books; the obligor is
        /// the postal system, so no D4A InterbankClaim is created here.
        /// </summary>
        public string RecordBankCollection(string issuingOfficeId, int serialNumber, string bankName, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "RecordBankCollection";
            PostalMoneyOrder order = FindOrder(issuingOfficeId, serialNumber);
            if (order == null)
                return $"MoneyOrderService.{op}: no money order {issuingOfficeId}#{serialNumber} on the register.";
            if (order.Status != MoneyOrderStatus.Issued || order.FraudFlagged)
                return $"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is not a live order — the bank collects real paper, not rumors.";
            if (string.IsNullOrWhiteSpace(bankName))
                return $"MoneyOrderService.{op}: the collecting bank must be named.";
            order.BankCollectionAgent = bankName;
            order.History.Add($"day {dayIndex}: left with '{bankName}' for collection as {order.PayeeName}'s agent (bank stamp is not an indorsement).");
            diag.Add($"MoneyOrderService: {issuingOfficeId}#{serialNumber} with '{bankName}' for collection — the bank presents at {order.PayableOfficeId} for cash.");
            return null;
        }

        /// <summary>
        /// Duplicate for a LOST order: the remitter, payee, or indorsee may
        /// apply through the issuing or payable office while the original is
        /// still live. No charge for the duplicate; the original is voided
        /// and can never be paid afterwards.
        /// </summary>
        public PostalMoneyOrder IssueDuplicate(string issuingOfficeId, int serialNumber, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "IssueDuplicate";
            if (IsSunday(dayIndex))
            {
                diag.Add($"MoneyOrderService.{op}: REFUSED — no money-order business is transacted on Sundays (1864 instructions §59).");
                return null;
            }
            PostalMoneyOrder original = FindOrder(issuingOfficeId, serialNumber);
            if (original == null)
            {
                diag.Add($"MoneyOrderService.{op}: no money order {issuingOfficeId}#{serialNumber} on the register.");
                return null;
            }
            if (original.FraudFlagged)
            {
                diag.Add($"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is flagged fraudulent — no duplicate of a fraud.");
                return null;
            }
            if (original.Status != MoneyOrderStatus.Issued)
            {
                diag.Add($"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is {original.Status} — duplicates replace live lost orders only.");
                return null;
            }
            if (dayIndex - original.IssuedDayIndex > ValidityDays)
            {
                diag.Add($"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is past its validity — no duplicate of an expired order.");
                return null;
            }
            PostalMoneyOrderOffice issuing = RequireOffice(issuingOfficeId, op, diag);
            if (issuing == null) return null;

            int newSerial = issuing.NextSerial++;
            var duplicate = new PostalMoneyOrder
            {
                SerialNumber = newSerial,
                IssuingOfficeId = original.IssuingOfficeId,
                PayableOfficeId = original.PayableOfficeId,
                SenderName = original.SenderName,
                PayeeName = original.PayeeName,
                AmountCents = original.AmountCents,
                FeeCents = 0, // no charge for a duplicate
                IssuedDayIndex = dayIndex,
                Status = MoneyOrderStatus.Issued,
                ReplacesSerial = original.SerialNumber,
            };
            duplicate.History.Add($"day {dayIndex}: duplicate issued in lieu of lost order {issuingOfficeId}#{original.SerialNumber} — no charge.");
            orders[OrderKey(issuingOfficeId, newSerial)] = duplicate;
            advices.Add(new PostalMoneyOrderAdvice
            {
                SerialNumber = newSerial,
                IssuingOfficeId = original.IssuingOfficeId,
                PayableOfficeId = original.PayableOfficeId,
                SenderName = original.SenderName,
                PayeeName = original.PayeeName,
                AmountCents = original.AmountCents,
                SentDayIndex = dayIndex,
                DueArrivalDayIndex = dayIndex + 1,
            });

            original.Status = MoneyOrderStatus.Duplicated;
            original.History.Add($"day {dayIndex}: DUPLICATED — replaced by {issuingOfficeId}#{newSerial}; the original can never be paid.");
            diag.Add($"MoneyOrderService: duplicate {issuingOfficeId}#{newSerial} issued for lost {issuingOfficeId}#{serialNumber} — no charge.");
            return duplicate;
        }

        /// <summary>
        /// Repayment to the sender at the office of issue within the
        /// validity window. The face amount is returned from the office's
        /// money-order fund; the fee is earned and not refunded.
        /// </summary>
        public string RepayOrder(string issuingOfficeId, int serialNumber, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "RepayOrder";
            if (IsSunday(dayIndex))
                return $"MoneyOrderService.{op}: REFUSED — no money-order business is transacted on Sundays (1864 instructions §59).";
            PostalMoneyOrder order = FindOrder(issuingOfficeId, serialNumber);
            if (order == null)
                return $"MoneyOrderService.{op}: no money order {issuingOfficeId}#{serialNumber} on the register.";
            if (order.FraudFlagged)
                return $"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is flagged fraudulent — no repayment.";
            if (order.Status != MoneyOrderStatus.Issued)
                return $"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is {order.Status} — only a live order is repayable.";
            if (dayIndex - order.IssuedDayIndex > ValidityDays)
                return $"MoneyOrderService.{op}: {issuingOfficeId}#{serialNumber} is past its validity — no repayment.";
            PostalMoneyOrderOffice issuing = RequireOffice(issuingOfficeId, op, diag);
            if (issuing == null)
                return $"MoneyOrderService.{op}: the issuing office '{issuingOfficeId}' is not designated.";
            if (issuing.MoneyOrderCashCents < order.AmountCents)
                return $"MoneyOrderService.{op}: REFUSED — '{issuingOfficeId}' money-order fund holds {issuing.MoneyOrderCashCents}c " +
                    $"against {order.AmountCents}c. Refused, not faked.";

            issuing.MoneyOrderCashCents -= order.AmountCents;
            order.Status = MoneyOrderStatus.Repaid;
            order.History.Add($"day {dayIndex}: REPAID {order.AmountCents}c in cash to sender {order.SenderName} at {issuingOfficeId} (fee {order.FeeCents}c not refunded).");
            diag.Add($"MoneyOrderService: {issuingOfficeId}#{serialNumber} repaid {order.AmountCents}c to {order.SenderName}.");
            return null;
        }

        /// <summary>
        /// Second-class offices deposit their SURPLUS money-order funds in
        /// their depository (1864 General Principles, IV). Moves only cash
        /// above the office's reserve. Returns the cents moved.
        /// </summary>
        public int SweepSurplusToDepository(string officeId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "SweepSurplusToDepository";
            PostalMoneyOrderOffice office = RequireOffice(officeId, op, diag);
            if (office == null) return 0;
            if (office.OfficeClass != MoneyOrderOfficeClass.Ordinary)
            {
                diag.Add($"MoneyOrderService.{op}: '{officeId}' is not an ordinary office — only second-class offices sweep surplus to a depository.");
                return 0;
            }
            PostalMoneyOrderOffice depository = RequireOffice(office.DepositoryOfficeId, op, diag);
            if (depository == null) return 0;
            int surplus = office.MoneyOrderCashCents - office.ReserveCents;
            if (surplus <= 0)
            {
                diag.Add($"MoneyOrderService.{op}: '{officeId}' holds nothing above its {office.ReserveCents}c reserve — nothing to sweep.");
                return 0;
            }
            office.MoneyOrderCashCents -= surplus;
            depository.MoneyOrderCashCents += surplus;
            diag.Add($"MoneyOrderService: '{officeId}' swept {surplus}c surplus money-order funds to depository '{depository.OfficeId}' (day {dayIndex}).");
            return surplus;
        }

        /// <summary>
        /// An office draws money-order cash back from its depository to meet
        /// orders drawn upon it. Refused when the depository is short.
        /// </summary>
        public string DrawOnDepository(string officeId, int amountCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            const string op = "DrawOnDepository";
            PostalMoneyOrderOffice office = RequireOffice(officeId, op, diag);
            if (office == null) return $"MoneyOrderService.{op}: unknown money-order office '{officeId}'.";
            if (office.OfficeClass != MoneyOrderOfficeClass.Ordinary)
                return $"MoneyOrderService.{op}: '{officeId}' is not an ordinary office — only second-class offices draw on a depository.";
            PostalMoneyOrderOffice depository = RequireOffice(office.DepositoryOfficeId, op, diag);
            if (depository == null) return $"MoneyOrderService.{op}: depository '{office.DepositoryOfficeId}' is not designated.";
            if (amountCents <= 0)
                return $"MoneyOrderService.{op}: the draw must be positive.";
            if (depository.MoneyOrderCashCents < amountCents)
            {
                diag.Add($"MoneyOrderService.{op}: REFUSED — depository '{depository.OfficeId}' holds {depository.MoneyOrderCashCents}c " +
                    $"against a {amountCents}c draw. Refused, not faked.");
                return $"MoneyOrderService.{op}: depository '{depository.OfficeId}' is short ({depository.MoneyOrderCashCents}c) — refused.";
            }
            depository.MoneyOrderCashCents -= amountCents;
            office.MoneyOrderCashCents += amountCents;
            diag.Add($"MoneyOrderService: '{officeId}' drew {amountCents}c from depository '{depository.OfficeId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// The postal settlement cycle (historical: weekly returns to the
        /// Superintendent of the Money Order Office). Open claims are netted
        /// bilaterally per office pair into positions and each position is
        /// settled IN FULL from the debtor office's money-order fund. No
        /// partials: a short debtor leaves the position Open and the claims
        /// Netted, with a loud diagnostic — the next cycle retries.
        /// </summary>
        public void RunSettlementCycle(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;

            // Retry still-open positions first (their claims are already Netted into them).
            foreach (PostalSettlementPosition position in positions.Values)
            {
                if (position.Status != PostalPositionStatus.Open) continue;
                TrySettlePosition(position, dayIndex, diag);
            }

            // Net open claims per unordered office pair.
            var pairClaims = new Dictionary<string, List<PostalMoneyOrderClaim>>(StringComparer.Ordinal);
            foreach (PostalMoneyOrderClaim claim in claims.Values)
            {
                if (claim.Status != PostalClaimStatus.Open) continue;
                string pairKey = PairKey(claim.DebtorOfficeId, claim.CreditorOfficeId);
                if (!pairClaims.TryGetValue(pairKey, out List<PostalMoneyOrderClaim> list))
                {
                    list = new List<PostalMoneyOrderClaim>();
                    pairClaims[pairKey] = list;
                }
                list.Add(claim);
            }

            foreach (var kvp in pairClaims)
            {
                // Skip pairs that already carry an open position — retried above.
                bool hasOpen = false;
                foreach (PostalSettlementPosition p in positions.Values)
                {
                    if (p.Status != PostalPositionStatus.Open) continue;
                    if (string.Equals(PairKey(p.DebtorOfficeId, p.CreditorOfficeId), kvp.Key, StringComparison.Ordinal)) { hasOpen = true; break; }
                }
                if (hasOpen) continue;

                // Keep the offices' original id casing (the pair key is lowercased).
                string idA = kvp.Value[0].DebtorOfficeId;
                string idB = kvp.Value[0].CreditorOfficeId;
                int aOwes = 0, bOwes = 0;
                foreach (PostalMoneyOrderClaim claim in kvp.Value)
                {
                    if (string.Equals(claim.DebtorOfficeId, idA, StringComparison.OrdinalIgnoreCase))
                        aOwes += claim.AmountCents;
                    else
                        bOwes += claim.AmountCents;
                }
                int net = aOwes - bOwes;
                if (net == 0)
                {
                    foreach (PostalMoneyOrderClaim claim in kvp.Value)
                    {
                        claim.Status = PostalClaimStatus.Settled;
                        claim.SettledDayIndex = dayIndex;
                    }
                    diag.Add($"MoneyOrderService: claims between '{idA}' and '{idB}' offset exactly — settled with no funds moving.");
                    continue;
                }

                string debtor = net > 0 ? idA : idB;
                string creditor = net > 0 ? idB : idA;
                var position = new PostalSettlementPosition
                {
                    PositionId = $"pmop-{sequence++}",
                    DebtorOfficeId = debtor,
                    CreditorOfficeId = creditor,
                    NetAmountCents = Math.Abs(net),
                    CycleDayIndex = dayIndex,
                    Status = PostalPositionStatus.Open,
                };
                foreach (PostalMoneyOrderClaim claim in kvp.Value)
                {
                    position.ComprisedClaimIds.Add(claim.ClaimId);
                    claim.Status = PostalClaimStatus.Netted;
                }
                positions[position.PositionId] = position;
                diag.Add($"MoneyOrderService: position {position.PositionId} — '{debtor}' owes '{creditor}' {position.NetAmountCents}c net ({position.ComprisedClaimIds.Count} claims).");
                TrySettlePosition(position, dayIndex, diag);
            }
        }

        private static string PairKey(string a, string b)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0
                ? $"{a}|{b}".ToLowerInvariant()
                : $"{b}|{a}".ToLowerInvariant();
        }

        private void TrySettlePosition(PostalSettlementPosition position, int dayIndex, List<string> diag)
        {
            if (!offices.TryGetValue(position.DebtorOfficeId, out PostalMoneyOrderOffice debtor) ||
                !offices.TryGetValue(position.CreditorOfficeId, out PostalMoneyOrderOffice creditor))
            {
                diag.Add($"MoneyOrderService: position {position.PositionId} cannot settle — an office is no longer designated. It stays open.");
                return;
            }
            if (debtor.MoneyOrderCashCents < position.NetAmountCents)
            {
                diag.Add($"MoneyOrderService: position {position.PositionId} NOT SETTLED — '{debtor.OfficeId}' money-order fund holds " +
                    $"{debtor.MoneyOrderCashCents}c against {position.NetAmountCents}c. No partials: refused loudly, retried next cycle.");
                return;
            }
            debtor.MoneyOrderCashCents -= position.NetAmountCents;
            creditor.MoneyOrderCashCents += position.NetAmountCents;
            position.Status = PostalPositionStatus.Settled;
            position.SettledDayIndex = dayIndex;
            foreach (string claimId in position.ComprisedClaimIds)
            {
                if (claims.TryGetValue(claimId, out PostalMoneyOrderClaim claim) && claim.Status == PostalClaimStatus.Netted)
                {
                    claim.Status = PostalClaimStatus.Settled;
                    claim.SettledDayIndex = dayIndex;
                }
            }
            diag.Add($"MoneyOrderService: position {position.PositionId} SETTLED — {position.NetAmountCents}c '{debtor.OfficeId}' → '{creditor.OfficeId}'.");
        }

        /// <summary>
        /// Releases the office's accumulated money-order fee revenue for the
        /// caller to post to the business ledger with provenance (mirrors
        /// PostalService.CollectPostageRevenue). Real lots — collects once.
        /// </summary>
        public int CollectMoneyOrderFeeRevenue(string officeId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!offices.TryGetValue(officeId, out PostalMoneyOrderOffice office)) return 0;
            int cents = Math.Max(0, office.UncollectedFeeCents);
            office.UncollectedFeeCents = 0;
            if (cents > 0)
                diag.Add($"MoneyOrderService: {cents}c money-order fee revenue from '{officeId}' released for ledger posting (provenance: money-order fees).");
            return cents;
        }

        /// <summary>D4E: money-order counter work as TTS tasks (issuing, paying).</summary>
        public void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            RegisterQuietly(authority, new TaskDefinition(IssueOrderTaskId, "Issue money order", 10));
            RegisterQuietly(authority, new TaskDefinition(PayOrderTaskId, "Pay money order", 10));
        }

        private static void RegisterQuietly(TaskAuthority authority, TaskDefinition definition)
        {
            authority.RegisterDefinition(definition, out _);
        }

        public MoneyOrderServiceSaveDto CaptureSaveDto()
        {
            return new MoneyOrderServiceSaveDto
            {
                offices = new List<PostalMoneyOrderOffice>(offices.Values),
                orders = new List<PostalMoneyOrder>(orders.Values),
                advices = new List<PostalMoneyOrderAdvice>(advices),
                claims = new List<PostalMoneyOrderClaim>(claims.Values),
                positions = new List<PostalSettlementPosition>(positions.Values),
                sequence = sequence,
            };
        }

        public void LoadFromSaveDto(MoneyOrderServiceSaveDto dto)
        {
            offices.Clear();
            orders.Clear();
            advices.Clear();
            claims.Clear();
            positions.Clear();
            if (dto == null) return;
            if (dto.offices != null)
                foreach (PostalMoneyOrderOffice o in dto.offices)
                    if (o != null && !string.IsNullOrEmpty(o.OfficeId)) offices[o.OfficeId] = o;
            if (dto.orders != null)
                foreach (PostalMoneyOrder order in dto.orders)
                    if (order != null) orders[OrderKey(order.IssuingOfficeId, order.SerialNumber)] = order;
            if (dto.advices != null)
                foreach (PostalMoneyOrderAdvice a in dto.advices)
                    if (a != null) advices.Add(a);
            if (dto.claims != null)
                foreach (PostalMoneyOrderClaim c in dto.claims)
                    if (c != null && !string.IsNullOrEmpty(c.ClaimId)) claims[c.ClaimId] = c;
            if (dto.positions != null)
                foreach (PostalSettlementPosition p in dto.positions)
                    if (p != null && !string.IsNullOrEmpty(p.PositionId)) positions[p.PositionId] = p;
            sequence = Math.Max(0, dto.sequence);
        }
    }
}
