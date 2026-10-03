using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// T3D: dispute status for a parcel. Canon Part II: "Unknown or disputed
    /// is an acceptable state." "Vacant, therefore take it" is not a rule.
    /// </summary>
    public enum DisputeStatus
    {
        Unspecified = 0,
        Clear = 1,        // unbroken chain, no adverse claims
        Uncertain = 2,    // chain gaps or weak links, no active dispute
        Disputed = 3,     // active adverse claim recorded
        AdverseClaim = 4, // someone asserts a right against the record holder
    }

    /// <summary>
    /// T3D: the legal character of a claim. Canon §2.4: "Color of title,
    /// defective documentation, honest boundary error, disputed rights and
    /// knowing trespass are not one Unauthorized flag. They can carry
    /// different knowledge, bargaining, legal and reputational consequences."
    /// </summary>
    public enum ClaimBasis
    {
        Unspecified = 0,
        ColorOfTitle = 1,           // plausible but defective document
        DefectiveDocumentation = 2, // paperwork flawed, claim otherwise honest
        HonestBoundaryError = 3,    // good-faith mistake about lines
        DisputedRights = 4,         // genuine disagreement over who holds what
        KnowingTrespass = 5,        // deliberate use without right
    }

    /// <summary>
    /// T3D: troubleshooting avenues. Canon §2.4 lists exactly these:
    /// "record searches, owner/agent/executor contact, lease or sale
    /// negotiation, mortgage resolution, tax-sale paths, public-land entry
    /// where lawful, claim challenges, settlement or withdrawal."
    /// </summary>
    public enum TroubleshootingAvenue
    {
        Unspecified = 0,
        RecordSearch = 1,
        ContactOwnerAgentExecutor = 2,
        LeaseNegotiation = 3,
        SaleNegotiation = 4,
        MortgageResolution = 5,
        TaxSalePath = 6,
        PublicLandEntry = 7, // only where lawful
        ClaimChallenge = 8,
        Settlement = 9,
        Withdrawal = 10,
    }

    /// <summary>T3D: one recorded adverse claim against a parcel.</summary>
    [Serializable]
    public sealed class AdverseClaim
    {
        public string ClaimId = string.Empty;
        public string ParcelId = string.Empty;
        public string ClaimantName = string.Empty;
        public ClaimBasis Basis = ClaimBasis.Unspecified;
        public string BasisDocument = string.Empty; // the deed/affidavit/map asserted
        public int DayIndex;
        public string Note = string.Empty;

        public AdverseClaim() { }
    }

    /// <summary>T3D: one recommended next step.</summary>
    [Serializable]
    public sealed class AvenueRecommendation
    {
        public TroubleshootingAvenue Avenue = TroubleshootingAvenue.Unspecified;
        public string Why = string.Empty;         // grounded in the parcel's facts
        public string Prerequisite = string.Empty; // what must be true first
        public string ExpectedCost = string.Empty;  // honest: time/money, or "unknown"

        public AvenueRecommendation() { }
    }

    /// <summary>
    /// T3D: the read-only diagnostic report for a parcel. This is the data
    /// the Unity UI binds to (see PropertyTroubleshootingContract.md).
    /// Read-only: the actions themselves go through the existing
    /// title-transfer and agreement flows.
    /// </summary>
    [Serializable]
    public sealed class ParcelDisputeReport
    {
        public string ParcelId = string.Empty;
        public DisputeStatus Status = DisputeStatus.Unspecified;
        public string CurrentHolderName = string.Empty;
        public List<string> KnownFacts = new List<string>();
        public List<string> ChainBreaks = new List<string>();
        public List<AdverseClaim> AdverseClaims = new List<AdverseClaim>();
        public List<AvenueRecommendation> RecommendedAvenues = new List<AvenueRecommendation>();

        public ParcelDisputeReport() { }
    }

    /// <summary>
    /// T3D: property-rights troubleshooting (PL-47). Uncertain/disputed
    /// parcels expose known status, legal basis, and next avenues. Canon
    /// §2.1 CANON LOCK: "Vacancy, abandonment, deterioration and non-use
    /// never by themselves establish that a parcel or building is unowned,
    /// free to claim or available for ordinary purchase." §2.2: "Unauthorized
    /// occupation never silently creates title or a valid lease." §2.3:
    /// legal rules are not instant enforcement — detection, notice, and
    /// proceedings consume ordinary world time.
    ///
    /// This service is a READ-ONLY diagnostic surface. It never transfers
    /// title, never resolves disputes by fiat, and never invents claimants.
    /// </summary>
    public sealed class PropertyTroubleshootingService
    {
        private readonly Dictionary<string, List<AdverseClaim>> claims =
            new Dictionary<string, List<AdverseClaim>>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Records an adverse claim. The claimant and basis must be named —
        /// anonymous claims are refused. Recording a claim does not transfer
        /// anything; it marks the parcel disputed.
        /// </summary>
        public string RecordAdverseClaim(
            EntityIdRegistry ids, string parcelId, string claimantName,
            ClaimBasis basis, string basisDocument, int dayIndex, string note,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(parcelId))
                return "PropertyTroubleshootingService.RecordAdverseClaim: a parcel is required.";
            if (string.IsNullOrWhiteSpace(claimantName))
                return "PropertyTroubleshootingService.RecordAdverseClaim: the claimant must be named — anonymous claims are refused.";
            if (basis == ClaimBasis.Unspecified)
                return "PropertyTroubleshootingService.RecordAdverseClaim: the claim basis must be stated (Canon §2.4).";

            var claim = new AdverseClaim
            {
                ClaimId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                ParcelId = parcelId,
                ClaimantName = claimantName,
                Basis = basis,
                BasisDocument = basisDocument ?? string.Empty,
                DayIndex = dayIndex,
                Note = note ?? string.Empty,
            };
            if (!claims.TryGetValue(parcelId, out var list))
            {
                list = new List<AdverseClaim>();
                claims[parcelId] = list;
            }
            list.Add(claim);
            diag.Add($"PropertyTroubleshootingService: adverse claim by {claimantName} recorded on '{parcelId}' ({basis}). Title unchanged — disputes don't move title.");
            return null;
        }

        /// <summary>
        /// Analyzes a parcel: walks its T2F title chain for breaks, collects
        /// adverse claims, and recommends avenues grounded in the facts found.
        /// </summary>
        public ParcelDisputeReport AnalyzeParcel(string parcelId, TitleAuthority titles, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var report = new ParcelDisputeReport { ParcelId = parcelId ?? string.Empty };
            if (titles == null || string.IsNullOrWhiteSpace(parcelId))
            {
                report.Status = DisputeStatus.Uncertain;
                report.KnownFacts.Add("No title authority or parcel id — status unknown, not assumed clear.");
                return report;
            }

            report.CurrentHolderName = titles.CurrentHolder(parcelId) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(report.CurrentHolderName))
            {
                report.Status = DisputeStatus.Uncertain;
                report.KnownFacts.Add("Parcel is not registered with the title authority — holder unknown.");
                report.RecommendedAvenues.Add(new AvenueRecommendation
                {
                    Avenue = TroubleshootingAvenue.RecordSearch,
                    Why = "The parcel has no registered chain; a record search is the first honest step.",
                    Prerequisite = "Access to county records.",
                    ExpectedCost = "Clerk fees + travel time.",
                });
                return report;
            }

            report.KnownFacts.Add($"Record holder: {report.CurrentHolderName}.");

            // Walk the chain for breaks.
            IReadOnlyList<TitleRecord> chain = titles.ChainOf(parcelId);
            if (chain.Count == 0)
            {
                report.ChainBreaks.Add("No title records exist for a registered parcel — chain is empty.");
            }
            for (int i = 0; i < chain.Count; i++)
            {
                TitleRecord record = chain[i];
                if (record.Basis == TitleBasis.Unspecified)
                    report.ChainBreaks.Add($"Link {i} ({record.HolderName}, day {record.DayIndex}): basis unstated — title never moves for no reason.");
                if (record.Basis == TitleBasis.Purchase && string.IsNullOrWhiteSpace(record.InstrumentId))
                    report.ChainBreaks.Add($"Link {i} ({record.HolderName}, day {record.DayIndex}): purchase without a conveyance instrument.");
                if (string.IsNullOrWhiteSpace(record.HolderName))
                    report.ChainBreaks.Add($"Link {i}: holder unnamed — the chain must name every holder.");
            }

            if (claims.TryGetValue(parcelId, out var parcelClaims))
                report.AdverseClaims.AddRange(parcelClaims);

            // Status from facts.
            if (report.AdverseClaims.Count > 0)
            {
                report.Status = DisputeStatus.Disputed;
                report.KnownFacts.Add($"{report.AdverseClaims.Count} adverse claim(s) recorded — title itself unchanged (Canon §2.3).");
            }
            else if (report.ChainBreaks.Count > 0)
            {
                report.Status = DisputeStatus.Uncertain;
                report.KnownFacts.Add("Chain breaks found — ownership is uncertain, not void.");
            }
            else
            {
                report.Status = DisputeStatus.Clear;
                report.KnownFacts.Add("Chain is unbroken and no adverse claims are recorded.");
            }

            RecommendAvenues(report);
            diag.Add($"PropertyTroubleshootingService: '{parcelId}' analyzed — {report.Status}, " +
                $"{report.ChainBreaks.Count} chain breaks, {report.AdverseClaims.Count} adverse claims.");
            return report;
        }

        private void RecommendAvenues(ParcelDisputeReport report)
        {
            // Record search is always honest first work when the chain is weak.
            if (report.Status == DisputeStatus.Uncertain)
            {
                report.RecommendedAvenues.Add(new AvenueRecommendation
                {
                    Avenue = TroubleshootingAvenue.RecordSearch,
                    Why = "Chain breaks need documentary evidence before any negotiation or challenge.",
                    Prerequisite = "Access to county records.",
                    ExpectedCost = "Clerk fees + travel time.",
                });
            }

            if (report.Status == DisputeStatus.Disputed || report.Status == DisputeStatus.AdverseClaim)
            {
                foreach (AdverseClaim claim in report.AdverseClaims)
                {
                    // Contact is cheaper than litigation; the canon lists it first.
                    report.RecommendedAvenues.Add(new AvenueRecommendation
                    {
                        Avenue = TroubleshootingAvenue.ContactOwnerAgentExecutor,
                        Why = $"Direct contact with {claim.ClaimantName} ({claim.Basis}) may resolve the claim without proceedings.",
                        Prerequisite = "A reachable address for the claimant.",
                        ExpectedCost = "Travel + correspondence time.",
                    });

                    if (claim.Basis == ClaimBasis.HonestBoundaryError)
                    {
                        report.RecommendedAvenues.Add(new AvenueRecommendation
                        {
                            Avenue = TroubleshootingAvenue.Settlement,
                            Why = "Honest boundary errors settle well: a survey and a line agreement end it.",
                            Prerequisite = "Both parties agree to a survey.",
                            ExpectedCost = "Surveyor's fee, split or assigned.",
                        });
                    }
                    else if (claim.Basis == ClaimBasis.KnowingTrespass)
                    {
                        report.RecommendedAvenues.Add(new AvenueRecommendation
                        {
                            Avenue = TroubleshootingAvenue.ClaimChallenge,
                            Why = "Knowing trespass creates exposure but no title — a challenge asserts the record.",
                            Prerequisite = "Evidence of the trespass and the record chain.",
                            ExpectedCost = "Legal costs; enforcement takes ordinary world time (Canon §2.3).",
                        });
                    }
                    else
                    {
                        report.RecommendedAvenues.Add(new AvenueRecommendation
                        {
                            Avenue = TroubleshootingAvenue.Settlement,
                            Why = $"A {claim.Basis} claim against the record may settle for less than proceedings cost.",
                            Prerequisite = "Willingness to negotiate on both sides.",
                            ExpectedCost = "Negotiation time; possible payment.",
                        });
                    }
                }
            }

            // Purchase/lease are always available where the holder will deal —
            // Canon: "A neighboring owner can refuse to sell, ask more, offer
            // a lease or have legal/title constraints."
            if (!string.IsNullOrWhiteSpace(report.CurrentHolderName))
            {
                report.RecommendedAvenues.Add(new AvenueRecommendation
                {
                    Avenue = TroubleshootingAvenue.SaleNegotiation,
                    Why = $"The record holder ({report.CurrentHolderName}) may sell — or refuse, ask more, or offer a lease.",
                    Prerequisite = "Contact with the holder.",
                    ExpectedCost = "Purchase price if agreed.",
                });
                report.RecommendedAvenues.Add(new AvenueRecommendation
                {
                    Avenue = TroubleshootingAvenue.LeaseNegotiation,
                    Why = "A lease gives lawful use without touching the title dispute.",
                    Prerequisite = "Holder willing to lease.",
                    ExpectedCost = "Rent as agreed.",
                });
            }

            // Withdrawal is always an honest option.
            report.RecommendedAvenues.Add(new AvenueRecommendation
            {
                Avenue = TroubleshootingAvenue.Withdrawal,
                Why = "Walking away is always lawful and always available.",
                Prerequisite = "None.",
                ExpectedCost = "Opportunity cost only.",
            });
        }

        /// <summary>
        /// UI-agnostic view contract surface: what the Unity UI binds to.
        /// Follows the CreateBusinessViewContract.md pattern.
        /// </summary>
        public interface IPropertyTroubleshootingView
        {
            /// <summary>Raised when the player picks a parcel to analyze.</summary>
            event Action<string> ParcelSelected;

            /// <summary>Raised when the player chooses a recommended avenue to pursue.</summary>
            event Action<TroubleshootingAvenue> AvenueChosen;

            void ShowReport(ParcelDisputeReport report);
            void ShowError(string message);
        }
    }
}
