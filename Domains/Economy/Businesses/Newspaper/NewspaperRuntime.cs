using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Newspaper
{
    /// <summary>
    /// NX-3A: ad size classes. Priced by the "square" (column inch) per
    /// 1870s frontier practice — historical research: small-town weeklies sold
    /// space by the square/column inch with repeat-insertion terms. Rates are
    /// calibration (Canon Part XV); the size classes are the doctrine.
    /// </summary>
    public enum NewspaperAdSize
    {
        Unspecified = 0,
        Notice = 1,        // ~1 square: help-wanted, lost & found
        BusinessCard = 2,  // standing professional card
        QuarterColumn = 3,
        HalfColumn = 4,
    }

    /// <summary>NX-3A: one dated edition — a dated lot, not timeless stock.</summary>
    [Serializable]
    public sealed class NewspaperEdition
    {
        public string EditionId = string.Empty;
        public int IssueNumber;
        public int PublicationDayIndex;
        public int CopiesPrinted;
        public int CopiesSold;
        public int PricePerCopyCents;
        public bool Stale; // older than a week: wrapping paper, not news

        public NewspaperEdition() { }
    }

    /// <summary>NX-3A: one paid advertisement placed for an issue.</summary>
    [Serializable]
    public sealed class AdPlacement
    {
        public string PlacementId = string.Empty;
        public string AdvertiserBusinessId = string.Empty;
        public string AdvertiserName = string.Empty;
        public string AdText = string.Empty;
        public NewspaperAdSize Size;
        public int RateCents;
        public int InsertionsBought;   // repeat terms (Canon §5.8)
        public int InsertionsRun;
        public int FirstIssueDayIndex;

        public AdPlacement() { }
    }

    /// <summary>NX-3A: a paid subscription — named subscriber, real term.</summary>
    [Serializable]
    public sealed class NewspaperSubscription
    {
        public string SubscriptionId = string.Empty;
        public string SubscriberName = string.Empty; // person or business, always named
        public int StartDayIndex;
        public int TermDays;
        public int PricePaidCents;
        public bool DeliveredByPost; // NX-2A: second-class postage, 2c/lb for weeklies (USPS, 1875)

        public NewspaperSubscription() { }
    }

    /// <summary>NX-3A: a job-printing order — handbills, posters, notices.</summary>
    [Serializable]
    public sealed class JobPrintOrder
    {
        public string OrderId = string.Empty;
        public string CustomerBusinessId = string.Empty;
        public string CustomerName = string.Empty;
        public string Description = string.Empty;
        public int Quantity;
        public int PriceCents;
        public bool Completed;
        public int CompletedDayIndex = -1;

        public JobPrintOrder() { }
    }

    /// <summary>
    /// NX-3A: the newspaper as a REAL business. Canon §5.8 (GHOST-DES-076):
    /// "Advertising is a real media market" — publication location,
    /// circulation/reach, issue frequency, placement/size inventory, rates and
    /// repeat terms. Effectiveness is observed through insertion → inquiry →
    /// visit → hire, never a hidden bonus.
    ///
    /// This closes the T2B upstream hole: recruitment's NewspaperAd channel
    /// buys through a real newspaper here, or is refused when none exists.
    ///
    /// Paper supply: newsprint is an EQU-1 import material ("newsprint",
    /// named eastern-mill origin, via railhead). No paper, no edition —
    /// the press does not print on wishes.
    /// </summary>
    public sealed class NewspaperRuntime
    {
        /// <summary>TUNING: yearly subscription, cents. Historical: frontier
        /// weeklies charged ~$2-3/year in the 1870s West (calibration).</summary>
        public const int YearlySubscriptionCents = 250;

        /// <summary>TUNING: single copy price, cents.</summary>
        public const int SingleCopyCents = 5;

        /// <summary>TUNING: ad rates per insertion by size, cents. Historical:
        /// priced by the square (column inch); repeat insertions discounted.</summary>
        public const int NoticeRateCents = 500;
        public const int BusinessCardRateCents = 1200;
        public const int QuarterColumnRateCents = 2500;
        public const int HalfColumnRateCents = 4500;

        /// <summary>TUNING: repeat-insertion discount (4+ insertions).</summary>
        public const double RepeatDiscount = 0.8;

        /// <summary>TUNING: paper units consumed per printed copy.</summary>
        public const int PaperPerCopy = 1;

        public const string NewsprintMaterialId = "newsprint";

        private readonly List<NewspaperEdition> editions = new List<NewspaperEdition>();
        private readonly List<AdPlacement> placements = new List<AdPlacement>();
        private readonly List<NewspaperSubscription> subscriptions = new List<NewspaperSubscription>();
        private readonly List<JobPrintOrder> jobOrders = new List<JobPrintOrder>();
        private readonly List<string> diagnostics = new List<string>();

        private int paperUnits;
        private string paperSourceNote = string.Empty;
        private int inkUnits;
        private int issueSequence;

        /// <summary>
        /// Optional component resolver for workstation evaluation (Tech X
        /// §3.5: capabilities derive from actual component assets). Wired by
        /// the scene/bootstrap from the equipment registry; null means no
        /// components can be verified, so a component-bearing workstation
        /// will honestly report missing components.
        /// </summary>
        public Func<string, WorkstationComponentView?> FindComponent { get; set; }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<NewspaperEdition> Editions => editions;
        public IReadOnlyList<AdPlacement> Placements => placements;
        public int PaperUnits => paperUnits;

        /// <summary>
        /// Registers newsprint as an importable material (EQU-1 extension path).
        /// Historical: frontier papers bought newsprint in reams from eastern
        /// mills via rail jobbers — a named origin, real transit, real cost.
        /// </summary>
        public static void EnsureNewsprintImportable(List<string> diag)
        {
            string problem = Trade.ImportCatalog.RegisterMaterial(new Trade.ImportMaterial(
                NewsprintMaterialId, "Newsprint (reams)",
                "Eastern paper mill, via railhead", 400, 16, 30));
            if (problem != null && diag != null)
                diag.Add($"NewspaperRuntime: newsprint import registration: {problem}");
        }

        /// <summary>Receives paper stock — the caller moves real lots; this only records custody.</summary>
        public string ReceivePaper(int units, string sourceNote, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (units <= 0) return "NewspaperRuntime.ReceivePaper: paper units must be positive — the press does not print on wishes.";
            if (string.IsNullOrWhiteSpace(sourceNote))
                return "NewspaperRuntime.ReceivePaper: the paper source must be named (import order id or supplier).";
            paperUnits += units;
            paperSourceNote = sourceNote;
            diag.Add($"NewspaperRuntime: received {units} paper units ({sourceNote}) — stock now {paperUnits}.");
            return null;
        }

        /// <summary>Receives printer's ink — a consumable with a named source.</summary>
        public string ReceiveInk(int units, string sourceNote, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (units <= 0) return "NewspaperRuntime.ReceiveInk: ink units must be positive.";
            if (string.IsNullOrWhiteSpace(sourceNote))
                return "NewspaperRuntime.ReceiveInk: the ink source must be named.";
            inkUnits += units;
            diag.Add($"NewspaperRuntime: received {units} ink units ({sourceNote}).");
            return null;
        }

        /// <summary>
        /// Prints an edition. Requires the printing-press workstation (Tech X
        /// §3.5), paper stock, and ink. Each copy consumes paper; the edition
        /// is a dated lot — last week's news is wrapping paper.
        /// </summary>
        public NewspaperEdition PublishEdition(
            EntityIdRegistry ids, BusinessWorkstations workstations, int copies,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (copies <= 0)
            {
                diag.Add("NewspaperRuntime.PublishEdition: copies must be positive.");
                return null;
            }
            if (workstations == null ||
                workstations.CheckReady(WorkstationCatalog.PrintingPress.WorkstationId,
                    WorkstationCatalog.PrintingPress, FindComponent, diag) != null)
            {
                diag.Add("NewspaperRuntime.PublishEdition: no ready printing-press workstation — " +
                    "a pressroom alone does not print (Tech X §3.5).");
                return null;
            }
            int paperNeeded = copies * PaperPerCopy;
            if (paperUnits < paperNeeded)
            {
                diag.Add($"NewspaperRuntime.PublishEdition: need {paperNeeded} paper units, have {paperUnits} — no paper, no edition.");
                return null;
            }
            if (inkUnits < 1)
            {
                diag.Add("NewspaperRuntime.PublishEdition: no ink — the press does not print dry.");
                return null;
            }

            paperUnits -= paperNeeded;
            inkUnits -= 1;
            issueSequence++;
            var edition = new NewspaperEdition
            {
                EditionId = ids != null ? ids.Allocate(EntityKind.Lot).ToString() : Guid.NewGuid().ToString("N"),
                IssueNumber = issueSequence,
                PublicationDayIndex = dayIndex,
                CopiesPrinted = copies,
                PricePerCopyCents = SingleCopyCents,
            };
            editions.Add(edition);

            // Repeat-term placements run in this issue.
            foreach (AdPlacement placement in placements)
            {
                if (placement.InsertionsRun < placement.InsertionsBought &&
                    placement.FirstIssueDayIndex <= dayIndex)
                {
                    placement.InsertionsRun++;
                }
            }

            diag.Add($"NewspaperRuntime: edition #{edition.IssueNumber} published day {dayIndex} — {copies} copies.");
            return edition;
        }

        /// <summary>
        /// Sells a paid advertisement. The advertiser is always named; the rate
        /// comes from the size class with the repeat-insertion discount applied
        /// (Canon §5.8: rates and repeat terms are the market).
        /// </summary>
        public AdPlacement PlaceAd(
            string advertiserBusinessId, string advertiserName, string adText,
            NewspaperAdSize size, int insertions, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(advertiserBusinessId))
            {
                diag.Add("NewspaperRuntime.PlaceAd: the advertiser must be a real business — no anonymous ads.");
                return null;
            }
            if (size == NewspaperAdSize.Unspecified)
            {
                diag.Add("NewspaperRuntime.PlaceAd: ad size is required — space is sold by the square.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(adText))
            {
                diag.Add("NewspaperRuntime.PlaceAd: ad copy is required.");
                return null;
            }
            if (insertions <= 0) insertions = 1;

            int baseRate = RateForSize(size);
            int rate = insertions >= 4 ? (int)Math.Round(baseRate * RepeatDiscount) : baseRate;
            var placement = new AdPlacement
            {
                PlacementId = $"ad-{dayIndex}-{placements.Count}",
                AdvertiserBusinessId = advertiserBusinessId,
                AdvertiserName = string.IsNullOrWhiteSpace(advertiserName) ? advertiserBusinessId : advertiserName,
                AdText = adText,
                Size = size,
                RateCents = rate,
                InsertionsBought = insertions,
                FirstIssueDayIndex = dayIndex,
            };
            placements.Add(placement);
            diag.Add($"NewspaperRuntime: ad {placement.PlacementId} placed for '{placement.AdvertiserName}' — " +
                $"{size} × {insertions} insertion(s) at {rate}c each.");
            return placement;
        }

        public static int RateForSize(NewspaperAdSize size)
        {
            switch (size)
            {
                case NewspaperAdSize.Notice: return NoticeRateCents;
                case NewspaperAdSize.BusinessCard: return BusinessCardRateCents;
                case NewspaperAdSize.QuarterColumn: return QuarterColumnRateCents;
                case NewspaperAdSize.HalfColumn: return HalfColumnRateCents;
                default: return -1;
            }
        }

        /// <summary>
        /// Sells a subscription — named subscriber, real term. Delivery by post
        /// follows NX-2A second-class postage (2¢/lb for weeklies, prepaid by
        /// the publisher from 1875; free in-county from 1879).
        /// </summary>
        public NewspaperSubscription SellSubscription(
            string subscriberName, int termDays, bool deliveredByPost, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(subscriberName))
            {
                diag.Add("NewspaperRuntime.SellSubscription: the subscriber must be named.");
                return null;
            }
            if (termDays <= 0)
            {
                diag.Add("NewspaperRuntime.SellSubscription: a real term is required.");
                return null;
            }
            int price = (int)Math.Round(YearlySubscriptionCents * (termDays / 365.0));
            price = Math.Max(SingleCopyCents, price);
            var sub = new NewspaperSubscription
            {
                SubscriptionId = $"sub-{dayIndex}-{subscriptions.Count}",
                SubscriberName = subscriberName,
                StartDayIndex = dayIndex,
                TermDays = termDays,
                PricePaidCents = price,
                DeliveredByPost = deliveredByPost,
            };
            subscriptions.Add(sub);
            diag.Add($"NewspaperRuntime: subscription {sub.SubscriptionId} — '{subscriberName}', {termDays} days, {price}c" +
                (deliveredByPost ? " (by post — second-class postage applies)." : "."));
            return sub;
        }

        /// <summary>Sells single copies of the latest edition over the counter.</summary>
        public string SellCopies(int editionIndex, int copies, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (editionIndex < 0 || editionIndex >= editions.Count)
                return "NewspaperRuntime.SellCopies: no such edition.";
            NewspaperEdition edition = editions[editionIndex];
            if (edition.Stale)
                return $"NewspaperRuntime.SellCopies: edition #{edition.IssueNumber} is stale — last week's news is wrapping paper.";
            int available = edition.CopiesPrinted - edition.CopiesSold;
            if (copies <= 0 || copies > available)
                return $"NewspaperRuntime.SellCopies: only {available} copies of edition #{edition.IssueNumber} remain.";
            edition.CopiesSold += copies;
            diag.Add($"NewspaperRuntime: sold {copies} copies of edition #{edition.IssueNumber} at {edition.PricePerCopyCents}c.");
            return null;
        }

        /// <summary>
        /// Job printing — handbills, posters, notices. Historical: job work was
        /// a major revenue line for frontier papers, often exceeding ads.
        /// </summary>
        public JobPrintOrder AcceptJobPrint(
            string customerBusinessId, string customerName, string description,
            int quantity, int priceCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(customerBusinessId))
            {
                diag.Add("NewspaperRuntime.AcceptJobPrint: the customer must be a real business.");
                return null;
            }
            if (quantity <= 0 || priceCents <= 0)
            {
                diag.Add("NewspaperRuntime.AcceptJobPrint: quantity and price must be positive.");
                return null;
            }
            var order = new JobPrintOrder
            {
                OrderId = $"job-{dayIndex}-{jobOrders.Count}",
                CustomerBusinessId = customerBusinessId,
                CustomerName = string.IsNullOrWhiteSpace(customerName) ? customerBusinessId : customerName,
                Description = description ?? string.Empty,
                Quantity = quantity,
                PriceCents = priceCents,
            };
            jobOrders.Add(order);
            diag.Add($"NewspaperRuntime: job order {order.OrderId} — '{order.Description}' × {quantity} for '{order.CustomerName}' at {priceCents}c.");
            return order;
        }

        /// <summary>Completes a job-print order — consumes paper like an edition.</summary>
        public string CompleteJobPrint(
            string orderId, BusinessWorkstations workstations, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            JobPrintOrder order = null;
            foreach (JobPrintOrder o in jobOrders)
                if (string.Equals(o.OrderId, orderId, StringComparison.Ordinal)) { order = o; break; }
            if (order == null) return $"NewspaperRuntime.CompleteJobPrint: unknown order '{orderId}'.";
            if (order.Completed) return $"NewspaperRuntime.CompleteJobPrint: order '{orderId}' already completed.";
            if (workstations == null ||
                workstations.CheckReady(WorkstationCatalog.PrintingPress.WorkstationId,
                    WorkstationCatalog.PrintingPress, FindComponent, diag) != null)
                return "NewspaperRuntime.CompleteJobPrint: no ready printing-press workstation (Tech X §3.5).";
            int paperNeeded = order.Quantity * PaperPerCopy;
            if (paperUnits < paperNeeded)
                return $"NewspaperRuntime.CompleteJobPrint: need {paperNeeded} paper units, have {paperUnits}.";
            paperUnits -= paperNeeded;
            order.Completed = true;
            order.CompletedDayIndex = dayIndex;
            diag.Add($"NewspaperRuntime: job order {orderId} completed day {dayIndex}.");
            return null;
        }

        /// <summary>Marks editions older than a week stale — dated lots, not timeless stock.</summary>
        public void AgeEditions(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (NewspaperEdition edition in editions)
            {
                if (!edition.Stale && dayIndex - edition.PublicationDayIndex > 7)
                {
                    edition.Stale = true;
                    diag.Add($"NewspaperRuntime: edition #{edition.IssueNumber} is stale — wrapping paper now.");
                }
            }
        }
    }
}
