using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Retail
{
    /// <summary>
    /// T1C: purchase-type-specific decision policy (Tech X §5.3). The policy is
    /// chosen BY purchase type — there is deliberately no universal
    /// CustomerAttractiveness score. The resolver never writes a transaction;
    /// it returns a choice (or a failure) for the caller to act on.
    /// </summary>
    public enum PurchaseType
    {
        Staple = 0,  // price-first: flour, feed, everyday goods
        Urgent = 1,  // wait-first: medicine, emergency repair
        Planned = 2, // distance-first: the trip is the cost (bulk goods)
        Considered = 3, // quality/trust-first: equipment, livestock
    }

    /// <summary>T1C: one customer's purchase intent — the demand side of a choice.</summary>
    [Serializable]
    public sealed class PurchaseIntent
    {
        public int ActingPersonId = -1;
        public string CategoryId = string.Empty;
        public int UnitsWanted;
        public PurchaseType Type = PurchaseType.Staple;
        public int DayIndex;

        public PurchaseIntent() { }
    }

    /// <summary>
    /// T1C: one merchant's offering for an intent, with the six Tech X §5.3
    /// feasibility states. A merchant is only choosable when ALL are true.
    /// </summary>
    [Serializable]
    public sealed class MerchantOption
    {
        public string MerchantBusinessId = string.Empty;
        public string MerchantName = string.Empty;
        public string LocationId = string.Empty;

        public bool Known = true;
        public bool Reachable = true;
        public bool Open = true;
        public bool InStock = true;
        public bool ServiceAvailable = true;
        public bool Affordable = true;

        public int PriceCentsPerUnit;
        public int WaitMinutesEstimate;
        public float MilesDistance;
        public int QualityRank; // higher = better; calibration, merchant-specific
        public bool TrustedSource; // prior experience with this merchant
        public bool CreditOffered;

        public MerchantOption() { }

        public bool IsFeasible =>
            Known && Reachable && Open && InStock && ServiceAvailable && Affordable;

        /// <summary>Names the first failed feasibility state, for lost-sale evidence.</summary>
        public string FirstFailedState()
        {
            if (!Known) return "unknown merchant";
            if (!Reachable) return "unreachable";
            if (!Open) return "closed";
            if (!InStock) return "out of stock";
            if (!ServiceAvailable) return "service unavailable";
            if (!Affordable) return "unaffordable";
            return string.Empty;
        }
    }

    /// <summary>T1C: the outcome of resolving one intent.</summary>
    public sealed class ChoiceResult
    {
        public bool Resolved;
        public MerchantOption Chosen;
        public LostSaleReason FailureReason = LostSaleReason.Unspecified;
        public string FailureDetail = string.Empty;
        public readonly List<MerchantOption> FeasibleSet = new List<MerchantOption>();
    }

    /// <summary>
    /// T1C: the Customer Choice Resolver (Tech X §5.3). Builds the feasible-choice
    /// set from the six feasibility states, then applies the purchase-type-specific
    /// policy. Never writes transactions; never auto-routes a failed intent to a
    /// competitor (Tech X §5.4) — the caller must re-resolve explicitly.
    /// </summary>
    public static class CustomerChoiceResolver
    {
        public static ChoiceResult Resolve(PurchaseIntent intent, IEnumerable<MerchantOption> options)
        {
            var result = new ChoiceResult();
            if (intent == null)
            {
                result.FailureReason = LostSaleReason.Unspecified;
                result.FailureDetail = "No purchase intent to resolve.";
                return result;
            }

            var infeasibleNotes = new List<string>();
            if (options != null)
            {
                foreach (MerchantOption option in options)
                {
                    if (option == null) continue;
                    if (option.IsFeasible)
                    {
                        result.FeasibleSet.Add(option);
                    }
                    else
                    {
                        infeasibleNotes.Add($"{option.MerchantName}: {option.FirstFailedState()}");
                    }
                }
            }

            if (result.FeasibleSet.Count == 0)
            {
                result.FailureReason = LostSaleReason.NoFeasibleChoice;
                result.FailureDetail = infeasibleNotes.Count > 0
                    ? "No feasible merchant — " + string.Join("; ", infeasibleNotes.ToArray())
                    : "No feasible merchant — no options offered.";
                return result;
            }

            result.Chosen = ChooseByPolicy(intent.Type, result.FeasibleSet);
            result.Resolved = result.Chosen != null;
            if (!result.Resolved)
            {
                result.FailureReason = LostSaleReason.NoFeasibleChoice;
                result.FailureDetail = "Feasible set was non-empty but no policy selected a merchant.";
            }

            return result;
        }

        private static MerchantOption ChooseByPolicy(PurchaseType type, List<MerchantOption> feasible)
        {
            MerchantOption best = feasible[0];
            for (int i = 1; i < feasible.Count; i++)
            {
                MerchantOption candidate = feasible[i];
                if (Beats(candidate, best, type))
                {
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Purchase-type-specific comparison. Ties keep the incumbent (first-listed),
        /// which keeps resolution deterministic for identical options.
        /// </summary>
        private static bool Beats(MerchantOption candidate, MerchantOption incumbent, PurchaseType type)
        {
            switch (type)
            {
                case PurchaseType.Urgent:
                    if (candidate.WaitMinutesEstimate != incumbent.WaitMinutesEstimate)
                        return candidate.WaitMinutesEstimate < incumbent.WaitMinutesEstimate;
                    return candidate.PriceCentsPerUnit < incumbent.PriceCentsPerUnit;

                case PurchaseType.Planned:
                    if (Math.Abs(candidate.MilesDistance - incumbent.MilesDistance) > 0.001f)
                        return candidate.MilesDistance < incumbent.MilesDistance;
                    return candidate.PriceCentsPerUnit < incumbent.PriceCentsPerUnit;

                case PurchaseType.Considered:
                    if (candidate.QualityRank != incumbent.QualityRank)
                        return candidate.QualityRank > incumbent.QualityRank;
                    if (candidate.TrustedSource != incumbent.TrustedSource)
                        return candidate.TrustedSource;
                    return candidate.PriceCentsPerUnit < incumbent.PriceCentsPerUnit;

                case PurchaseType.Staple:
                default:
                    if (candidate.PriceCentsPerUnit != incumbent.PriceCentsPerUnit)
                        return candidate.PriceCentsPerUnit < incumbent.PriceCentsPerUnit;
                    return candidate.WaitMinutesEstimate < incumbent.WaitMinutesEstimate;
            }
        }
    }
}
