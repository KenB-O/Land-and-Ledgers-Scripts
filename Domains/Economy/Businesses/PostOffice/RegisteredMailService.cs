using System;
using System.Collections.Generic;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Postal
{
    /// <summary>
    /// D4F: the registered-mail authority — a service layer on the NX-2A postal
    /// movement, not a parallel system. Registered items ride the ordinary
    /// schedules/routes; registration adds the receipt chain:
    ///
    /// - Registration: sender pays postage + the period-correct registration
    ///   fee; the item gets a registry serial and the sender gets a receipt.
    /// - Chain of custody: every handoff between offices/carriers is recorded
    ///   in the record's append-only custody log (via PostalService.CustodyHandoff).
    /// - Delivery: only against recipient signature/identification.
    /// - Return receipt: a real mail item traveling back to the sender through
    ///   the mail system — never a synthetic notification.
    /// - Loss: a recorded event with the last-known custodian — never
    ///   auto-resolved, never auto-compensated.
    ///
    /// Canon: Part VII §7.1-7.2 (postal information network, in-person
    /// collection), §16.4 (post office as paying transportation customer).
    /// Historical basis: see RegisteredMailFeeSchedule.
    /// </summary>
    public sealed class RegisteredMailService
    {
        public const string RegisterItemTaskId = "registeredmail.register-item";
        public const string RecordDeliveryTaskId = "registeredmail.record-delivery";

        /// <summary>
        /// INDEMNITY FORK: the 1872 Act (c. 335, §190) and the 1904/1905 Postal
        /// Information guides describe registry indemnity (loss compensated up
        /// to $25), but the game is set in 1870 — before that Act — and the
        /// research cannot settle whether indemnity was actually paid in 1870.
        /// Parameterized rather than guessed: when false, losses are recorded
        /// and indemnity is never computed or paid. A scenario may enable it
        /// (calibration) once the canon settles the question.
        /// </summary>
        public bool IndemnityAvailable { get; set; }

        /// <summary>Days past due before an overdue registered item's chain is declared broken (calibration).</summary>
        public int OverdueLossThresholdDays { get; set; } = 30;

        private readonly Dictionary<string, RegisteredMailRecord> records =
            new Dictionary<string, RegisteredMailRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, RegistryNumberState> numberStates =
            new Dictionary<string, RegistryNumberState>(StringComparer.OrdinalIgnoreCase);
        private readonly PostalService postal;
        private int uncollectedFeeCents;

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public IReadOnlyCollection<RegisteredMailRecord> Records => records.Values;
        public int UncollectedFeeCents => Math.Max(0, uncollectedFeeCents);

        public RegisteredMailService(PostalService postalService = null)
        {
            postal = postalService;
            if (postal != null)
                postal.CustodyHandoff += OnPostalHandoff;
        }

        private PostalService RequirePostal(string op, List<string> diag)
        {
            if (postal == null)
            {
                diag.Add($"RegisteredMailService.{op}: no postal service bound — registered mail rides the real mail stream.");
                return null;
            }
            return postal;
        }

        private void OnPostalHandoff(PostalHandoff handoff)
        {
            if (handoff?.Item == null) return;
            if (!records.TryGetValue(handoff.Item.MailId, out RegisteredMailRecord record)) return;
            if (record.Status == RegisteredMailStatus.Delivered || record.Status == RegisteredMailStatus.Lost)
                return; // the chain is closed — nothing more is appended
            AppendCustody(record, handoff.DayIndex, handoff.OfficeId, handoff.Action, handoff.Note);
        }

        private static void AppendCustody(
            RegisteredMailRecord record, int dayIndex, string custodianOfficeId, string action, string note)
        {
            record.CustodyLog.Add(new RegisteredCustodyEntry
            {
                DayIndex = dayIndex,
                CustodianOfficeId = custodianOfficeId ?? string.Empty,
                Action = action ?? string.Empty,
                Note = note ?? string.Empty,
            });
            record.LastKnownCustodianOfficeId = custodianOfficeId ?? string.Empty;
        }

        private string NextSerial(string officeId)
        {
            if (!numberStates.TryGetValue(officeId, out RegistryNumberState state))
            {
                state = new RegistryNumberState { OfficeId = officeId, NextNumber = 1 };
                numberStates[officeId] = state;
            }
            string serial = $"REG-{officeId}-{state.NextNumber:D4}";
            state.NextNumber++;
            return serial;
        }

        /// <summary>
        /// Registers an already-posted mail item: charges the registration fee
        /// (+ optional return-receipt fee), issues the registry serial, and
        /// gives the sender the registration receipt. Only Posted items at
        /// their origin office can be registered — registration happens when
        /// the sender hands the article to the postmaster.
        /// </summary>
        public RegisteredMailRecord RegisterItem(
            string mailId, int declaredValueCents, bool requestReturnReceipt,
            bool returnReceiptFullParticulars, int senderPersonId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            PostalService svc = RequirePostal(nameof(RegisterItem), diag);
            if (svc == null) return null;

            MailItem item = svc.GetMailItem(mailId);
            if (item == null)
            {
                diag.Add($"RegisteredMailService.RegisterItem: unknown mail item '{mailId}'.");
                return null;
            }
            if (records.ContainsKey(mailId))
            {
                diag.Add($"RegisteredMailService.RegisterItem: {mailId} is already registered — no double registration.");
                return null;
            }
            if (item.Status != MailStatus.Posted)
            {
                diag.Add($"RegisteredMailService.RegisterItem: {mailId} is {item.Status} — registration happens at posting, not mid-stream.");
                return null;
            }
            if (declaredValueCents < 0)
            {
                diag.Add("RegisteredMailService.RegisterItem: declared value cannot be negative.");
                return null;
            }

            int fee = RegisteredMailFeeSchedule.RegistrationFeeCents;
            int receiptFee = 0;
            if (requestReturnReceipt)
            {
                receiptFee = returnReceiptFullParticulars
                    ? RegisteredMailFeeSchedule.ReturnReceiptFullParticularsCents
                    : RegisteredMailFeeSchedule.ReturnReceiptAtMailingCents;
            }

            var record = new RegisteredMailRecord
            {
                SerialNumber = NextSerial(item.FromOfficeId),
                RegistryOfficeId = item.FromOfficeId,
                MailId = mailId,
                SenderName = item.SenderName,
                SenderPersonId = senderPersonId,
                RecipientName = item.RecipientName,
                RecipientPersonId = item.RecipientPersonId,
                DeclaredValueCents = declaredValueCents,
                RegistrationFeeCents = fee,
                ReturnReceiptRequested = requestReturnReceipt,
                ReturnReceiptFullParticulars = returnReceiptFullParticulars,
                ReturnReceiptFeeCents = receiptFee,
                RegisteredDayIndex = dayIndex,
                Status = RegisteredMailStatus.Registered,
            };
            record.History.Add($"day {dayIndex}: registered at {item.FromOfficeId} as {record.SerialNumber} " +
                $"(fee {fee}c{(requestReturnReceipt ? $", return receipt {receiptFee}c" : "")}); receipt issued to sender.");
            AppendCustody(record, dayIndex, item.FromOfficeId, "registered",
                $"registry serial {record.SerialNumber}; receipt issued to sender.");

            item.IsRegistered = true;
            records[mailId] = record;
            uncollectedFeeCents += fee + receiptFee;
            diag.Add($"RegisteredMailService: {mailId} registered as {record.SerialNumber} (fee {fee + receiptFee}c).");
            return record;
        }

        /// <summary>
        /// Posts and registers a letter in one call — the convenient path for
        /// articles that should travel registered from the start.
        /// </summary>
        public RegisteredMailRecord PostRegisteredLetter(
            string senderName, int senderPersonId, int recipientPersonId, string recipientName,
            string fromOfficeId, string toOfficeId, int declaredValueCents,
            bool requestReturnReceipt, bool returnReceiptFullParticulars,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            PostalService svc = RequirePostal(nameof(PostRegisteredLetter), diag);
            if (svc == null) return null;

            MailItem item = svc.PostItem(MailKind.Letter, senderName, recipientPersonId,
                recipientName, fromOfficeId, toOfficeId, dayIndex, diag);
            if (item == null) return null;
            return RegisterItem(item.MailId, declaredValueCents, requestReturnReceipt,
                returnReceiptFullParticulars, senderPersonId, dayIndex, diag);
        }

        /// <summary>
        /// Requests a return receipt AFTER mailing (1872 Act §128: 5 cents —
        /// more than the 3 cents charged at mailing). Only before delivery.
        /// </summary>
        public string RequestReturnReceiptAfterMailing(string mailId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!records.TryGetValue(mailId, out RegisteredMailRecord record))
                return $"RegisteredMailService.RequestReturnReceiptAfterMailing: '{mailId}' is not registered.";
            if (record.Status != RegisteredMailStatus.Registered)
                return $"RegisteredMailService.RequestReturnReceiptAfterMailing: {record.SerialNumber} is {record.Status} — too late.";
            if (record.ReturnReceiptRequested)
                return $"RegisteredMailService.RequestReturnReceiptAfterMailing: {record.SerialNumber} already has a return receipt.";
            record.ReturnReceiptRequested = true;
            record.ReturnReceiptFeeCents = RegisteredMailFeeSchedule.ReturnReceiptAfterMailingCents;
            uncollectedFeeCents += record.ReturnReceiptFeeCents;
            record.History.Add($"day {dayIndex}: return receipt requested after mailing ({record.ReturnReceiptFeeCents}c).");
            diag.Add($"RegisteredMailService: return receipt requested after mailing for {record.SerialNumber} ({record.ReturnReceiptFeeCents}c).");
            return null;
        }

        public RegisteredMailRecord GetRecord(string mailId)
        {
            if (string.IsNullOrEmpty(mailId)) return null;
            records.TryGetValue(mailId, out RegisteredMailRecord record);
            return record;
        }

        /// <summary>
        /// Delivers a registered item against recipient signature/identification.
        /// On-map recipients are identified by person id; named (off-map)
        /// recipients sign the name the article is addressed to. On success the
        /// item is collected through the registered-delivery path and, when a
        /// return receipt was requested, the receipt starts its journey back
        /// to the sender as a REAL mail item. Returns null on success, an
        /// error string otherwise.
        /// </summary>
        public string RecordDelivery(
            string mailId, int collectorPersonId, string signatureName, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            PostalService svc = RequirePostal(nameof(RecordDelivery), diag);
            if (svc == null) return "RegisteredMailService.RecordDelivery: no postal service bound.";

            if (!records.TryGetValue(mailId, out RegisteredMailRecord record))
                return $"RegisteredMailService.RecordDelivery: '{mailId}' is not registered.";
            if (record.Status == RegisteredMailStatus.Delivered)
                return $"RegisteredMailService.RecordDelivery: {record.SerialNumber} already delivered — no double delivery.";
            if (record.Status == RegisteredMailStatus.Lost)
                return $"RegisteredMailService.RecordDelivery: {record.SerialNumber} is lost — a lost article cannot be delivered.";

            MailItem item = svc.GetMailItem(mailId);
            if (item == null || item.Status != MailStatus.Arrived)
                return $"RegisteredMailService.RecordDelivery: {record.SerialNumber} has not arrived at its destination office.";
            if (string.IsNullOrWhiteSpace(signatureName))
                return $"RegisteredMailService.RecordDelivery: {record.SerialNumber} requires the recipient's signature — no signature, no delivery.";

            bool identityOk = record.RecipientPersonId >= 0
                ? collectorPersonId == record.RecipientPersonId
                : string.Equals(signatureName.Trim(), record.RecipientName.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!identityOk)
                return $"RegisteredMailService.RecordDelivery: signature does not identify the addressee of {record.SerialNumber} — delivery refused.";

            if (!svc.CollectRegisteredItem(mailId, dayIndex, diag))
                return $"RegisteredMailService.RecordDelivery: {record.SerialNumber} could not be released by the postal service.";

            record.Status = RegisteredMailStatus.Delivered;
            record.DeliverySignatureName = signatureName.Trim();
            record.DeliveredDayIndex = dayIndex;
            record.LastKnownCustodianOfficeId = item.ToOfficeId;
            record.History.Add($"day {dayIndex}: delivered against signature of '{record.DeliverySignatureName}' at {item.ToOfficeId}.");
            AppendCustody(record, dayIndex, item.ToOfficeId, "delivered",
                $"signed for by '{record.DeliverySignatureName}'.");
            diag.Add($"RegisteredMailService: {record.SerialNumber} delivered against signature at {item.ToOfficeId}.");

            // The return receipt travels back as a real mail item — never a
            // synthetic notification. Foreign (off-map) articles get no
            // addressee receipt unless demanded (1905 §36) — domestic only.
            bool offMap = string.Equals(item.ToOfficeId, PostalService.OffMapOfficeId, StringComparison.OrdinalIgnoreCase);
            if (record.ReturnReceiptRequested && !offMap)
            {
                MailItem receipt = svc.PostItem(MailKind.Letter, record.DeliverySignatureName,
                    record.SenderPersonId, record.SenderName, item.ToOfficeId, item.FromOfficeId, dayIndex, diag);
                if (receipt != null)
                {
                    receipt.History.Add($"day {dayIndex}: return receipt for registered {record.SerialNumber}, " +
                        $"signed '{record.DeliverySignatureName}'" +
                        (record.ReturnReceiptFullParticulars ? " (full particulars: whom, when, address)." : "."));
                    record.ReturnReceiptMailId = receipt.MailId;
                    record.History.Add($"day {dayIndex}: return receipt {receipt.MailId} posted to sender through the mail.");
                    diag.Add($"RegisteredMailService: return receipt {receipt.MailId} for {record.SerialNumber} posted to the sender.");
                }
            }
            else if (record.ReturnReceiptRequested && offMap)
            {
                record.History.Add($"day {dayIndex}: no addressee receipt returned — off-map article (1905 §36: no receipt unless demanded).");
                diag.Add($"RegisteredMailService: {record.SerialNumber} left the map — no addressee receipt returned.");
            }
            return null;
        }

        /// <summary>
        /// Records a loss: the chain broke. The loss is a recorded event with
        /// the last-known custodian — never auto-resolved, never
        /// auto-compensated. Indemnity is COMPUTED only (when
        /// IndemnityAvailable); the caller moves any money with provenance.
        /// </summary>
        public string RecordLoss(string mailId, string reason, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            PostalService svc = RequirePostal(nameof(RecordLoss), diag);
            if (svc == null) return "RegisteredMailService.RecordLoss: no postal service bound.";

            if (!records.TryGetValue(mailId, out RegisteredMailRecord record))
                return $"RegisteredMailService.RecordLoss: '{mailId}' is not registered.";
            if (record.Status == RegisteredMailStatus.Lost)
                return $"RegisteredMailService.RecordLoss: {record.SerialNumber} is already recorded lost — losses are recorded once, never re-resolved.";
            if (record.Status == RegisteredMailStatus.Delivered)
                return $"RegisteredMailService.RecordLoss: {record.SerialNumber} was delivered — it cannot also be lost.";

            MailItem item = svc.GetMailItem(mailId);
            if (item != null && item.Status != MailStatus.Lost)
            {
                item.Status = MailStatus.Lost;
                item.History.Add($"day {dayIndex}: LOST — {reason ?? "chain broken"} (last custodian {record.LastKnownCustodianOfficeId}).");
            }

            record.Status = RegisteredMailStatus.Lost;
            record.LossRecordedDayIndex = dayIndex;
            record.LossReason = reason ?? "chain broken";
            record.History.Add($"day {dayIndex}: LOSS recorded — {record.LossReason} (last-known custodian {record.LastKnownCustodianOfficeId}).");
            AppendCustody(record, dayIndex, record.LastKnownCustodianOfficeId, "loss",
                $"chain broken: {record.LossReason}.");
            record.IndemnityComputedCents = ComputeIndemnityCents(record);
            diag.Add($"RegisteredMailService: LOSS recorded for {record.SerialNumber} — last-known custodian {record.LastKnownCustodianOfficeId}.");
            return null;
        }

        /// <summary>
        /// Declares the chain broken for registered items still in transit far
        /// past their due arrival — the "item never arrives" path. Caller-driven
        /// (e.g., daily), parameterized by OverdueLossThresholdDays.
        /// </summary>
        public void AuditOverdue(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            PostalService svc = RequirePostal(nameof(AuditOverdue), diag);
            if (svc == null) return;

            foreach (RegisteredMailRecord record in records.Values)
            {
                if (record.Status != RegisteredMailStatus.Registered) continue;
                MailItem item = svc.GetMailItem(record.MailId);
                if (item == null || item.Status != MailStatus.InTransit) continue;
                if (item.DueArrivalDayIndex < 0) continue;
                if (dayIndex <= item.DueArrivalDayIndex + OverdueLossThresholdDays) continue;
                RecordLoss(record.MailId,
                    $"overdue without arrival ({dayIndex - item.DueArrivalDayIndex}d past due) — chain broken",
                    dayIndex, diag);
            }
        }

        /// <summary>
        /// Computes the indemnity owed on a recorded loss: declared value
        /// capped at the indemnity limit — but ONLY when IndemnityAvailable
        /// (the 1870 fork). Computing never pays; the caller moves money.
        /// </summary>
        public int ComputeIndemnity(string mailId)
        {
            if (!records.TryGetValue(mailId, out RegisteredMailRecord record))
                return 0;
            return ComputeIndemnityCents(record);
        }

        private int ComputeIndemnityCents(RegisteredMailRecord record)
        {
            if (record == null) return 0;
            if (!IndemnityAvailable) return 0;
            if (record.Status != RegisteredMailStatus.Lost) return 0;
            return Math.Min(Math.Max(0, record.DeclaredValueCents), RegisteredMailFeeSchedule.IndemnityLimitCents);
        }

        /// <summary>
        /// D4E link (opt-in, simple): a money-order advice can travel registered.
        /// Posts a real registered letter from the issuing office to the payable
        /// office, addressed to that office's postmaster. The D4E advice record
        /// itself is untouched — this is carriage, not a D4E behavior change.
        /// </summary>
        public RegisteredMailRecord RegisterMoneyOrderAdviceLetter(
            PostalMoneyOrderAdvice advice, string senderName, int senderPersonId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (advice == null)
            {
                diag.Add("RegisteredMailService.RegisterMoneyOrderAdviceLetter: advice is required.");
                return null;
            }
            return PostRegisteredLetter(
                senderName, senderPersonId, -1,
                $"Postmaster of {advice.PayableOfficeId}",
                advice.IssuingOfficeId, advice.PayableOfficeId,
                0, false, false, dayIndex, diag);
        }

        /// <summary>
        /// Returns accumulated registration/return-receipt fees for the caller
        /// to post to the business ledger with provenance (mirrors
        /// PostalService.CollectPostageRevenue). Collected once.
        /// </summary>
        public int CollectRegistrationFeeRevenue(List<string> diag)
        {
            diag = diag ?? diagnostics;
            int cents = Math.Max(0, uncollectedFeeCents);
            uncollectedFeeCents = 0;
            if (cents > 0)
                diag.Add($"RegisteredMailService: {cents}c registration fee revenue released for ledger posting (provenance: registration fees).");
            return cents;
        }

        /// <summary>D4F: registered-mail work as TTS tasks.</summary>
        public void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            authority.RegisterDefinition(new TaskDefinition(RegisterItemTaskId, "Register mail item", 10), out _);
            authority.RegisterDefinition(new TaskDefinition(RecordDeliveryTaskId, "Record registered delivery", 5), out _);
        }

        public RegisteredMailSaveDto CaptureSaveDto()
        {
            return new RegisteredMailSaveDto
            {
                records = new List<RegisteredMailRecord>(records.Values),
                numberStates = new List<RegistryNumberState>(numberStates.Values),
                uncollectedFeeCents = uncollectedFeeCents,
                indemnityAvailable = IndemnityAvailable,
            };
        }

        public void LoadFromSaveDto(RegisteredMailSaveDto dto)
        {
            records.Clear();
            numberStates.Clear();
            if (dto == null) return;
            if (dto.records != null)
                foreach (RegisteredMailRecord r in dto.records)
                    if (r != null && !string.IsNullOrEmpty(r.MailId)) records[r.MailId] = r;
            if (dto.numberStates != null)
                foreach (RegistryNumberState s in dto.numberStates)
                    if (s != null && !string.IsNullOrEmpty(s.OfficeId)) numberStates[s.OfficeId] = s;
            uncollectedFeeCents = Math.Max(0, dto.uncollectedFeeCents);
            IndemnityAvailable = dto.indemnityAvailable;
        }
    }
}
