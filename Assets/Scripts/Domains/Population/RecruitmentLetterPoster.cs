using System.Collections.Generic;
using LandLedgers.Economy.Postal;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 4 (finding F03): the production posting step that
    /// RecruitmentConversation deliberately did not do.
    /// RecruitmentConversation.BuildLetterRequest builds a letter request;
    /// this adapter hands it to PostalService.PostItem with the payer
    /// overload, so postage is debited from the sender's real cash store
    /// BEFORE acceptance, and posting is refused when the sender cannot
    /// pay — no free carriage, no accrued-but-unpaid postage.
    ///
    /// What this adapter does NOT do, deliberately:
    ///   - It does not invent office ids. The origin office id (the
    ///     General Store office in production) and the destination office
    ///     id must be caller-supplied and already registered with the
    ///     PostalService; an unregistered origin is refused by
    ///     PostItemCore itself. Off-map contacts use
    ///     PostalService.OffMapOfficeId ("offmap") as the destination.
    ///     Registering the General Store office is a Codex bootstrap
    ///     task (finding F04) — no production code registers any office
    ///     today, so posting fails in production until that is done.
    ///     Pass XL phase 5 (finding R02): the origin is cross-checked
    ///     against the request's Origin (set to "general-store" by
    ///     RecruitmentConversation.BuildLetterRequest). A blank
    ///     fromOfficeId falls back to the request's Origin; a supplied
    ///     fromOfficeId that disagrees with a non-blank request Origin
    ///     is refused, so a letter can never be posted from an office
    ///     its own request does not name. Comparison is case-insensitive,
    ///     matching PostalService's office registry.
    ///   - It does not choose the sender's payer. The caller supplies an
    ///     IPostalSenderCashAuthority (household ledger payer or business
    ///     cash payer); a null payer is refused, never treated as free.
    ///   - It posts exactly one Letter per call, and returns the MailItem
    ///     or null. Re-posting the same request posts a second letter
    ///     and charges postage a second time — callers must not retry a
    ///     successful post, and this adapter keeps no sent-mail ledger
    ///     to pretend otherwise.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public static class RecruitmentLetterPoster
    {
        public static MailItem PostLetter(
            RecruitmentLetterRequest request,
            PostalService postal,
            string fromOfficeId,
            string toOfficeId,
            IPostalSenderCashAuthority senderPayer,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (request == null)
            {
                diagnostics.Add("RecruitmentLetterPoster: null letter request; nothing posted.");
                return null;
            }
            if (postal == null)
            {
                diagnostics.Add("RecruitmentLetterPoster: null PostalService; nothing posted.");
                return null;
            }
            if (request.RecipientPersonId < 0 || string.IsNullOrWhiteSpace(request.RecipientName))
            {
                diagnostics.Add("RecruitmentLetterPoster: letter request names no recipient; nothing posted.");
                return null;
            }
            string origin = fromOfficeId;
            if (string.IsNullOrWhiteSpace(origin))
            {
                origin = request.Origin;
            }
            if (string.IsNullOrWhiteSpace(origin))
            {
                diagnostics.Add("RecruitmentLetterPoster: no origin office id supplied (neither caller nor request names one); nothing posted.");
                return null;
            }
            if (!string.IsNullOrWhiteSpace(request.Origin)
                && !string.IsNullOrWhiteSpace(fromOfficeId)
                && !string.Equals(fromOfficeId, request.Origin, System.StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(
                    $"RecruitmentLetterPoster: caller origin '{fromOfficeId}' disagrees with the letter request's origin " +
                    $"'{request.Origin}'; nothing posted, no postage charged.");
                return null;
            }
            string destination = string.IsNullOrWhiteSpace(toOfficeId) ? PostalService.OffMapOfficeId : toOfficeId;
            MailItem posted = postal.PostItem(
                MailKind.Letter, request.SenderName, request.RecipientPersonId, request.RecipientName,
                origin, destination, dayIndex, senderPayer, diagnostics);
            if (posted == null)
            {
                diagnostics.Add($"RecruitmentLetterPoster: posting refused for P{request.RecipientPersonId} ({request.RecipientName}); no postage charged by this call.");
                return null;
            }
            diagnostics.Add(
                $"RecruitmentLetterPoster: letter posted from '{origin}' to '{destination}' " +
                $"for P{request.RecipientPersonId} ({request.RecipientName}), purpose '{request.Purpose}'.");
            return posted;
        }
    }
}
