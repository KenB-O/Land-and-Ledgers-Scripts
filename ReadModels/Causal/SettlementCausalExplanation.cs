using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass XII Worker D (brief §6.3): evidence-backed causal explanation
    /// for a settlement or a business. Every line cites exactly one real
    /// historical event supplied by the caller — employment gained or
    /// lost, wages paid or missed, purchases, supplier failures, freight
    /// delays, inventory shortages, openings or closures, rent arrears,
    /// credit distress, or property changes. There is no narrative
    /// layer: the verdict is a pure count summary ("Positive signs",
    /// "Under strain", "Mixed", "No recorded events") and the basis
    /// always lists the counts, so a reader can check the verdict
    /// against the lines. With no events, the model says so instead of
    /// inventing a story. With only one kind of event, it does not
    /// upgrade to a stronger claim ("thriving" from a single purchase
    /// would be fabrication).
    ///
    /// Callers must feed this model from real records only (employment
    /// registry, obligation authority, freight records, business
    /// lifecycle). It performs no world reads itself.
    /// </summary>
    [Serializable]
    public sealed class SettlementCausalExplanation
    {
        public enum EventKind
        {
            EmploymentGained = 0,
            EmploymentLost = 1,
            WagePaid = 2,
            WageMissed = 3,
            Purchase = 4,
            SupplierFailure = 5,
            FreightDelay = 6,
            InventoryShortage = 7,
            BusinessOpened = 8,
            BusinessClosed = 9,
            RentArrears = 10,
            CreditDistress = 11,
            PropertyChanged = 12,
        }

        [Serializable]
        public sealed class CausalEvent
        {
            public int DayIndex = -1;
            public EventKind Kind;
            public string Detail = string.Empty;
            public int AmountCents;
        }

        [Serializable]
        public sealed class ExplanationLine
        {
            public int DayIndex = -1;
            public string KindLabel = string.Empty;
            public string Text = string.Empty;
        }

        public string SubjectLabel = string.Empty;
        public List<ExplanationLine> Lines = new List<ExplanationLine>();
        /// <summary>Positive signs | Under strain | Mixed | No recorded events.</summary>
        public string Verdict = string.Empty;
        /// <summary>Always present: the counts the verdict was computed from.</summary>
        public string VerdictBasis = string.Empty;

        public static string KindLabelFor(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.EmploymentGained: return "Employment gained";
                case EventKind.EmploymentLost: return "Employment lost";
                case EventKind.WagePaid: return "Wage paid";
                case EventKind.WageMissed: return "Wage missed";
                case EventKind.Purchase: return "Purchase";
                case EventKind.SupplierFailure: return "Supplier failure";
                case EventKind.FreightDelay: return "Freight delay";
                case EventKind.InventoryShortage: return "Inventory shortage";
                case EventKind.BusinessOpened: return "Business opened";
                case EventKind.BusinessClosed: return "Business closed";
                case EventKind.RentArrears: return "Rent arrears";
                case EventKind.CreditDistress: return "Credit distress";
                case EventKind.PropertyChanged: return "Property change";
                default: return "Event";
            }
        }

        private static bool IsPositive(EventKind kind)
        {
            return kind == EventKind.EmploymentGained || kind == EventKind.WagePaid
                || kind == EventKind.Purchase || kind == EventKind.BusinessOpened;
        }

        private static bool IsDistress(EventKind kind)
        {
            return kind == EventKind.EmploymentLost || kind == EventKind.WageMissed
                || kind == EventKind.SupplierFailure || kind == EventKind.InventoryShortage
                || kind == EventKind.BusinessClosed || kind == EventKind.RentArrears
                || kind == EventKind.CreditDistress;
        }

        /// <summary>
        /// Builds the explanation. Lines are sorted by day (input order
        /// preserved within a day). Each line's text is the caller's
        /// detail verbatim plus its day cite — never paraphrased into a
        /// claim the event does not support.
        /// </summary>
        public static SettlementCausalExplanation Build(string subjectLabel, IReadOnlyList<CausalEvent> events)
        {
            var model = new SettlementCausalExplanation { SubjectLabel = subjectLabel ?? string.Empty };
            var sorted = new List<CausalEvent>();
            if (events != null)
            {
                foreach (CausalEvent e in events)
                {
                    if (e != null) sorted.Add(e);
                }
            }
            sorted.Sort((a, b) => a.DayIndex.CompareTo(b.DayIndex));

            int positive = 0;
            int distress = 0;
            foreach (CausalEvent e in sorted)
            {
                if (IsPositive(e.Kind)) positive++;
                if (IsDistress(e.Kind)) distress++;
                string detail = string.IsNullOrWhiteSpace(e.Detail) ? KindLabelFor(e.Kind) : e.Detail.Trim();
                model.Lines.Add(new ExplanationLine
                {
                    DayIndex = e.DayIndex,
                    KindLabel = KindLabelFor(e.Kind),
                    Text = "Day " + e.DayIndex + " — " + KindLabelFor(e.Kind) + ": " + detail,
                });
            }

            if (sorted.Count == 0)
            {
                model.Verdict = "No recorded events";
                model.VerdictBasis = "0 events recorded; no explanation is offered.";
            }
            else if (positive > 0 && distress == 0)
            {
                model.Verdict = "Positive signs";
                model.VerdictBasis = positive + " positive event(s), 0 distress event(s), "
                    + (sorted.Count - positive) + " neutral event(s).";
            }
            else if (distress > 0 && positive == 0)
            {
                model.Verdict = "Under strain";
                model.VerdictBasis = "0 positive event(s), " + distress + " distress event(s), "
                    + (sorted.Count - distress) + " neutral event(s).";
            }
            else
            {
                model.Verdict = "Mixed";
                model.VerdictBasis = positive + " positive event(s), " + distress + " distress event(s), "
                    + (sorted.Count - positive - distress) + " neutral event(s).";
            }
            return model;
        }
    }
}
