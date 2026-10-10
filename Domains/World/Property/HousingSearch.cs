using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Phase D (Real People): autonomous housing-search paths. A household
    /// lacking appropriate accommodation responds through the SAME
    /// information and authorities the player uses — rentals, boarding, kin,
    /// employer lodging, temporary shelter, purchase, seller-finance
    /// negotiation, lender approach, land purchase + build, relocation.
    /// Append-only.
    /// </summary>
    public enum HousingSearchPathKind
    {
        Unspecified = 0,
        Rental = 1,
        Boarding = 2,
        KinInquiry = 3,
        EmployerLodging = 4,
        TemporaryShelter = 5,
        Purchase = 6,
        SellerFinanceNegotiation = 7,
        LenderApproach = 8,
        LandPurchaseAndBuild = 9,
        Relocate = 10,
    }

    /// <summary>Phase D: search-need lifecycle. Append-only.</summary>
    public enum HousingSearchNeedStatus
    {
        Open = 0,
        Resolved = 1,
        Withdrawn = 2,
    }

    /// <summary>
    /// Phase D: a REAL housing-search need — demand, not a house. This is the
    /// household's unmet accommodation requirement; it carries no building,
    /// no space, no money. The planner consumes it; the executor resolves it.
    /// </summary>
    [Serializable]
    public sealed class HousingSearchNeed
    {
        public string NeedId = string.Empty;
        public int HouseholdId;
        public int RequiredSleepingPlaces;
        public ResidentialConditionKind CurrentCondition = ResidentialConditionKind.Unsheltered;
        public int CreatedDayIndex;
        public int LastEvaluatedDayIndex;
        public HousingSearchNeedStatus Status = HousingSearchNeedStatus.Open;
        public string TriggerSummary = string.Empty;

        public HousingSearchNeed() { }
    }

    /// <summary>
    /// Phase D: one concrete accommodation opportunity offered by a real
    /// provider. Options come from <see cref="IHousingOpportunitySource"/> —
    /// the same information the player sees — never invented from a need.
    /// </summary>
    [Serializable]
    public sealed class HousingSearchOption
    {
        public string OptionId = string.Empty;
        public HousingSearchPathKind PathKind = HousingSearchPathKind.Unspecified;
        public string ProviderName = string.Empty;
        public string Description = string.Empty;
        public string SpaceId = string.Empty;   // the real space offered (empty when the path has none yet)
        public string BuildingId = string.Empty;
        public string ParcelId = string.Empty;  // for land purchase / land+build paths
        public AccommodationArrangement Arrangement = AccommodationArrangement.Unspecified;
        public int PlacesProvided;
        public int UpfrontCostCents;
        public int RecurringMonthlyCents;
        public int TotalPriceCents;             // purchase / land price (0 when not a purchase path)
        public int TravelDaysFromTown;
        public string Prerequisites = string.Empty;

        public HousingSearchOption() { }
    }

    /// <summary>
    /// Phase D: one rejected alternative with its reason — the observability
    /// half of every search decision (§26).
    /// </summary>
    [Serializable]
    public sealed class HousingRejectedAlternative
    {
        public string OptionId = string.Empty;
        public HousingSearchPathKind PathKind = HousingSearchPathKind.Unspecified;
        public string ProviderName = string.Empty;
        public string Reason = string.Empty;

        public HousingRejectedAlternative() { }
    }

    /// <summary>
    /// Phase D: a recorded search decision — what was chosen AND every
    /// rejected alternative with its reason.
    /// </summary>
    [Serializable]
    public sealed class HousingSearchDecision
    {
        public string DecisionId = string.Empty;
        public string NeedId = string.Empty;
        public int HouseholdId;
        public int DayIndex;
        public HousingSearchOption ChosenOption;
        public List<HousingRejectedAlternative> RejectedAlternatives = new List<HousingRejectedAlternative>();
        public bool Resolved;
        public string FailureReason = string.Empty;

        public HousingSearchDecision() { }
    }

    /// <summary>
    /// Phase D: a next step the search hands to another authority instead of
    /// executing itself (purchase → acquisition workflow, land+build →
    /// construction, lender → credit workflow, relocate → migration).
    /// No house is generated from a need.
    /// </summary>
    [Serializable]
    public sealed class HousingSearchDelegatedStep
    {
        public HousingSearchPathKind PathKind = HousingSearchPathKind.Unspecified;
        public string Summary = string.Empty;
        public string ReferenceId = string.Empty; // negotiation id, credit request id, intent id...

        public HousingSearchDelegatedStep() { }
    }

    /// <summary>
    /// Phase D: the information surface an autonomous household searches —
    /// rental listings, boarding vacancies, kin offers, employer openings,
    /// temporary shelters, properties/parcels for sale, seller-finance terms,
    /// lender offers, relocation destinations. Scenario/scene wiring supplies
    /// the implementation; the planner never invents options.
    /// </summary>
    public interface IHousingOpportunitySource
    {
        List<HousingSearchOption> RentalListings(int requiredPlaces);
        List<HousingSearchOption> BoardingVacancies(int requiredPlaces);
        List<HousingSearchOption> KinOffers(int householdId);
        List<HousingSearchOption> EmployerLodgingOpenings(int householdId);
        List<HousingSearchOption> TemporaryShelters();
        List<HousingSearchOption> PropertiesForSale(int requiredPlaces);
        List<HousingSearchOption> SellerFinanceOffers();
        List<HousingSearchOption> LenderOffers(int householdId);
        List<HousingSearchOption> LandParcelsForSale();
        List<HousingSearchOption> RelocationDestinations(int householdId);
    }

    /// <summary>Phase D: in-memory opportunity directory (scenario wiring registers real options).</summary>
    public sealed class HousingOpportunityDirectory : IHousingOpportunitySource
    {
        private readonly List<HousingSearchOption> rentals = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> boarding = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> kin = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> employer = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> temporary = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> properties = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> sellerFinance = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> lenders = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> land = new List<HousingSearchOption>();
        private readonly List<HousingSearchOption> relocation = new List<HousingSearchOption>();

        public void AddRental(HousingSearchOption o) => rentals.Add(o);
        public void AddBoarding(HousingSearchOption o) => boarding.Add(o);
        public void AddKinOffer(HousingSearchOption o) => kin.Add(o);
        public void AddEmployerLodging(HousingSearchOption o) => employer.Add(o);
        public void AddTemporaryShelter(HousingSearchOption o) => temporary.Add(o);
        public void AddPropertyForSale(HousingSearchOption o) => properties.Add(o);
        public void AddSellerFinanceOffer(HousingSearchOption o) => sellerFinance.Add(o);
        public void AddLenderOffer(HousingSearchOption o) => lenders.Add(o);
        public void AddLandParcel(HousingSearchOption o) => land.Add(o);
        public void AddRelocationDestination(HousingSearchOption o) => relocation.Add(o);

        public List<HousingSearchOption> RentalListings(int requiredPlaces) => new List<HousingSearchOption>(rentals);
        public List<HousingSearchOption> BoardingVacancies(int requiredPlaces) => new List<HousingSearchOption>(boarding);
        public List<HousingSearchOption> KinOffers(int householdId) => new List<HousingSearchOption>(kin);
        public List<HousingSearchOption> EmployerLodgingOpenings(int householdId) => new List<HousingSearchOption>(employer);
        public List<HousingSearchOption> TemporaryShelters() => new List<HousingSearchOption>(temporary);
        public List<HousingSearchOption> PropertiesForSale(int requiredPlaces) => new List<HousingSearchOption>(properties);
        public List<HousingSearchOption> SellerFinanceOffers() => new List<HousingSearchOption>(sellerFinance);
        public List<HousingSearchOption> LenderOffers(int householdId) => new List<HousingSearchOption>(lenders);
        public List<HousingSearchOption> LandParcelsForSale() => new List<HousingSearchOption>(land);
        public List<HousingSearchOption> RelocationDestinations(int householdId) => new List<HousingSearchOption>(relocation);
    }

    /// <summary>
    /// Phase D: the autonomous search planner. Ranks REAL options by
    /// affordability and fit; records the chosen path and every rejected
    /// alternative with its reason. NEVER generates a house from a need and
    /// NEVER forces a low-income household into ownership: purchase paths
    /// require documented cash AND income; rental/boarding/kin are preferred
    /// when they fit. Ownership is a choice the household can afford, never
    /// a default.
    /// </summary>
    public sealed class HousingSearchPlanner
    {
        /// <summary>
        /// TUNING: an option's monthly recurring cost is affordable when it
        /// fits documented monthly income, or — with no documented income —
        /// when it fits a quarter of current cash (one season of runway).
        /// </summary>
        public int CashRunwayDivisorForRecurring { get; set; } = 4;

        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <param name="estimatedMonthlyIncomeCents">Documented household income (wages etc.); 0 = unknown.</param>
        public HousingSearchDecision Plan(
            HousingSearchNeed need,
            HouseholdCompositionSummary composition,
            int ledgerBalanceCents,
            int estimatedMonthlyIncomeCents,
            IHousingOpportunitySource source,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var decision = new HousingSearchDecision
            {
                DecisionId = $"hsd-{sequence++}",
                HouseholdId = need != null ? need.HouseholdId : -1,
                NeedId = need != null ? need.NeedId : string.Empty,
                DayIndex = dayIndex,
            };
            if (need == null || source == null)
            {
                decision.FailureReason = "no search need or no opportunity source — nothing searched.";
                diag.Add($"HousingSearchPlanner: {decision.FailureReason}");
                return decision;
            }

            int required = Math.Max(1, need.RequiredSleepingPlaces);
            var candidates = new List<HousingSearchOption>();
            Gather(source, need.HouseholdId, required, candidates);

            var affordable = new List<HousingSearchOption>();
            foreach (HousingSearchOption option in candidates)
            {
                string rejection = EvaluateOption(option, required, ledgerBalanceCents, estimatedMonthlyIncomeCents);
                if (rejection == null)
                    affordable.Add(option);
                else
                    decision.RejectedAlternatives.Add(new HousingRejectedAlternative
                    {
                        OptionId = option.OptionId,
                        PathKind = option.PathKind,
                        ProviderName = option.ProviderName,
                        Reason = rejection,
                    });
            }

            // Cheapest adequate option first: lowest recurring, then lowest upfront.
            affordable.Sort((a, b) =>
            {
                int c = a.RecurringMonthlyCents.CompareTo(b.RecurringMonthlyCents);
                return c != 0 ? c : a.UpfrontCostCents.CompareTo(b.UpfrontCostCents);
            });

            if (affordable.Count > 0)
            {
                decision.ChosenOption = affordable[0];
                decision.Resolved = true;
                diag.Add($"HousingSearchPlanner: H{need.HouseholdId} chose {affordable[0].PathKind} " +
                    $"'{affordable[0].Description}' ({affordable.Count - 1} other affordable option(s), " +
                    $"{decision.RejectedAlternatives.Count} rejected with reasons).");
            }
            else
            {
                decision.FailureReason = $"H{need.HouseholdId}: no affordable adequate option among {candidates.Count} " +
                    $"opportunity(ies) — {decision.RejectedAlternatives.Count} rejected with reasons; the need stays open.";
                diag.Add($"HousingSearchPlanner: {decision.FailureReason}");
            }
            return decision;
        }

        private void Gather(IHousingOpportunitySource source, int householdId, int required, List<HousingSearchOption> outOptions)
        {
            AddAll(outOptions, source.RentalListings(required));
            AddAll(outOptions, source.BoardingVacancies(required));
            AddAll(outOptions, source.KinOffers(householdId));
            AddAll(outOptions, source.EmployerLodgingOpenings(householdId));
            AddAll(outOptions, source.TemporaryShelters());
            AddAll(outOptions, source.PropertiesForSale(required));
            AddAll(outOptions, source.SellerFinanceOffers());
            AddAll(outOptions, source.LenderOffers(householdId));
            AddAll(outOptions, source.LandParcelsForSale());
            AddAll(outOptions, source.RelocationDestinations(householdId));
        }

        private static void AddAll(List<HousingSearchOption> target, List<HousingSearchOption> options)
        {
            if (options == null) return;
            foreach (HousingSearchOption option in options)
                if (option != null) target.Add(option);
        }

        /// <summary>
        /// Returns null when the option is viable, or the rejection reason.
        /// Suitability: an option that cannot sleep the household is not a
        /// housing answer for it. Affordability: upfront from real cash,
        /// recurring from documented income (or cash runway). Ownership paths
        /// additionally require the household to carry the whole price —
        /// never forced on low-income households.
        /// </summary>
        private string EvaluateOption(
            HousingSearchOption option,
            int requiredPlaces,
            int ledgerBalanceCents,
            int estimatedMonthlyIncomeCents)
        {
            if (option.PathKind == HousingSearchPathKind.Unspecified)
                return "no path kind named.";

            // Paths that must provide real sleeping capacity.
            bool needsCapacity = option.PathKind == HousingSearchPathKind.Rental
                || option.PathKind == HousingSearchPathKind.Boarding
                || option.PathKind == HousingSearchPathKind.KinInquiry
                || option.PathKind == HousingSearchPathKind.EmployerLodging
                || option.PathKind == HousingSearchPathKind.TemporaryShelter;
            if (needsCapacity && option.PlacesProvided < requiredPlaces)
                return $"unsuitable: provides {option.PlacesProvided} sleeping place(s), household needs {requiredPlaces}.";

            // Ownership is never forced: purchase requires the full price in
            // documented cash AND income to carry any recurring cost. These
            // checks run first so a rejected purchase names ownership, not a
            // generic shortfall.
            if (option.PathKind == HousingSearchPathKind.Purchase)
            {
                if (option.TotalPriceCents <= 0)
                    return "no real price named — a purchase without a price is not an option.";
                if (option.TotalPriceCents > ledgerBalanceCents)
                    return $"ownership not affordable: price {option.TotalPriceCents}c against {ledgerBalanceCents}c cash — renting/boarding remain open.";
            }
            if (option.PathKind == HousingSearchPathKind.LandPurchaseAndBuild)
            {
                if (option.TotalPriceCents <= 0)
                    return "no real land price named.";
                if (option.TotalPriceCents > ledgerBalanceCents)
                    return $"land+build not affordable: land price {option.TotalPriceCents}c against {ledgerBalanceCents}c cash.";
            }

            if (option.UpfrontCostCents > ledgerBalanceCents)
                return $"unaffordable: {option.UpfrontCostCents}c upfront against {ledgerBalanceCents}c household cash.";

            if (option.RecurringMonthlyCents > 0)
            {
                int affordableMonthly = estimatedMonthlyIncomeCents > 0
                    ? estimatedMonthlyIncomeCents
                    : Math.Max(0, ledgerBalanceCents / Math.Max(1, CashRunwayDivisorForRecurring));
                if (option.RecurringMonthlyCents > affordableMonthly)
                    return $"unaffordable: {option.RecurringMonthlyCents}c/month recurring against {affordableMonthly}c/month capacity.";
            }

            return null;
        }
    }

    /// <summary>
    /// Phase D: registry of housing-search needs — one open need per
    /// household; re-evaluation updates in place. Save-persisted.
    /// </summary>
    public sealed class HousingSearchNeedRegistry
    {
        private readonly Dictionary<int, HousingSearchNeed> openByHousehold = new Dictionary<int, HousingSearchNeed>();
        private readonly List<HousingSearchNeed> all = new List<HousingSearchNeed>();
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public HousingSearchNeed GetOpenNeed(int householdId)
        {
            return openByHousehold.TryGetValue(householdId, out HousingSearchNeed need)
                && need.Status == HousingSearchNeedStatus.Open ? need : null;
        }

        public HousingSearchNeed OpenNeed(int householdId, int requiredPlaces, ResidentialConditionKind condition,
            string triggerSummary, int dayIndex)
        {
            HousingSearchNeed existing = GetOpenNeed(householdId);
            if (existing != null)
            {
                existing.RequiredSleepingPlaces = Math.Max(1, requiredPlaces);
                existing.CurrentCondition = condition;
                existing.LastEvaluatedDayIndex = dayIndex;
                if (!string.IsNullOrWhiteSpace(triggerSummary)) existing.TriggerSummary = triggerSummary;
                return existing;
            }
            var need = new HousingSearchNeed
            {
                NeedId = $"hsn-{sequence++}",
                HouseholdId = householdId,
                RequiredSleepingPlaces = Math.Max(1, requiredPlaces),
                CurrentCondition = condition,
                CreatedDayIndex = dayIndex,
                LastEvaluatedDayIndex = dayIndex,
                TriggerSummary = triggerSummary ?? string.Empty,
            };
            openByHousehold[householdId] = need;
            all.Add(need);
            return need;
        }

        public void MarkResolved(int householdId, int dayIndex)
        {
            HousingSearchNeed need = GetOpenNeed(householdId);
            if (need == null) return;
            need.Status = HousingSearchNeedStatus.Resolved;
            need.LastEvaluatedDayIndex = dayIndex;
            openByHousehold.Remove(householdId);
        }

        public void ExportState(List<HousingSearchNeed> outNeeds)
        {
            if (outNeeds == null) return;
            outNeeds.AddRange(all);
        }

        public void ImportState(IEnumerable<HousingSearchNeed> inNeeds)
        {
            if (inNeeds == null) return;
            foreach (HousingSearchNeed need in inNeeds)
            {
                if (need == null || need.HouseholdId < 0 || string.IsNullOrWhiteSpace(need.NeedId)) continue;
                all.Add(need);
                if (need.Status == HousingSearchNeedStatus.Open)
                {
                    if (openByHousehold.ContainsKey(need.HouseholdId))
                    {
                        diagnostics.Add($"ImportState: duplicate open housing-search need for H{need.HouseholdId} skipped.");
                        continue;
                    }
                    openByHousehold[need.HouseholdId] = need;
                }
            }
        }
    }

    /// <summary>
    /// Phase D: the boarding-house side of search execution. The runtime
    /// adapter (boarding business) implements this against the real
    /// <c>BoardingHouseBoarderRegister</c> + room inventory; tests fake it.
    /// </summary>
    public interface IBoarderCheckInPort
    {
        string CheckIn(int personId, int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// Phase D: executes a search decision through the real authorities.
    /// Direct paths (rental, kin, employer lodging, temporary shelter) record
    /// agreements and occupancies in <see cref="HousingAuthority"/>; boarding
    /// goes through the boarding-house port; purchase / seller-finance /
    /// lender / land+build / relocate become DELEGATED steps for the owning
    /// authorities — the search never generates a house from a need.
    /// </summary>
    public sealed class HousingSearchExecutor
    {
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public sealed class ExecutionResult
        {
            public string DecisionId = string.Empty;
            public int HouseholdId;
            public bool Executed;
            public string FailureReason = string.Empty;
            public List<string> OccupancyIds = new List<string>();
            public string AgreementId = string.Empty;
            public List<HousingSearchDelegatedStep> DelegatedSteps = new List<HousingSearchDelegatedStep>();
        }

        public ExecutionResult Execute(
            HousingSearchDecision decision,
            IReadOnlyList<PersonState> members,
            HousingAuthority housing,
            IBoarderCheckInPort boardingPort,
            SellerFinanceNegotiationBook negotiations,
            LenderApproachService lenderService,
            EntityIdRegistry ids,
            CreditOfferWorkflow creditWorkflow,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new ExecutionResult
            {
                DecisionId = decision != null ? decision.DecisionId : string.Empty,
                HouseholdId = decision != null ? decision.HouseholdId : -1,
            };
            if (decision == null || !decision.Resolved || decision.ChosenOption == null)
            {
                result.FailureReason = "no resolved decision to execute.";
                diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                return result;
            }
            if (housing == null)
            {
                result.FailureReason = "no housing authority — nothing executed.";
                diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                return result;
            }

            HousingSearchOption option = decision.ChosenOption;
            switch (option.PathKind)
            {
                case HousingSearchPathKind.Rental:
                    return ExecuteDirectOccupancy(result, option, members, housing,
                        AccommodationArrangement.Rental, "rental", dayIndex, diag);
                case HousingSearchPathKind.KinInquiry:
                    return ExecuteDirectOccupancy(result, option, members, housing,
                        option.Arrangement != AccommodationArrangement.Unspecified
                            ? option.Arrangement : AccommodationArrangement.KinHosting,
                        "kin hosting", dayIndex, diag);
                case HousingSearchPathKind.EmployerLodging:
                    return ExecuteDirectOccupancy(result, option, members, housing,
                        AccommodationArrangement.EmployerLodging, "employer lodging", dayIndex, diag);
                case HousingSearchPathKind.TemporaryShelter:
                    return ExecuteDirectOccupancy(result, option, members, housing,
                        option.Arrangement != AccommodationArrangement.Unspecified
                            ? option.Arrangement : AccommodationArrangement.TemporaryCamp,
                        "temporary shelter", dayIndex, diag);
                case HousingSearchPathKind.Boarding:
                    return ExecuteBoarding(result, option, members, boardingPort, dayIndex, diag);
                case HousingSearchPathKind.SellerFinanceNegotiation:
                    return ExecuteSellerFinanceInquiry(result, option, negotiations, dayIndex, diag);
                case HousingSearchPathKind.LenderApproach:
                    return ExecuteLenderApproach(result, option, lenderService, ids, creditWorkflow, dayIndex, diag);
                case HousingSearchPathKind.Purchase:
                case HousingSearchPathKind.LandPurchaseAndBuild:
                case HousingSearchPathKind.Relocate:
                    result.DelegatedSteps.Add(new HousingSearchDelegatedStep
                    {
                        PathKind = option.PathKind,
                        Summary = $"{option.PathKind} '{option.Description}' delegated — executed by the acquisition/construction/migration authority, never generated from the need.",
                        ReferenceId = option.OptionId,
                    });
                    result.Executed = true;
                    diag.Add($"HousingSearchExecutor: {option.PathKind} delegated for H{result.HouseholdId} ('{option.Description}').");
                    return result;
                default:
                    result.FailureReason = $"unhandled search path {option.PathKind}.";
                    diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                    return result;
            }
        }

        private ExecutionResult ExecuteDirectOccupancy(
            ExecutionResult result, HousingSearchOption option, IReadOnlyList<PersonState> members,
            HousingAuthority housing, AccommodationArrangement arrangement, string kindLabel,
            int dayIndex, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(option.SpaceId))
            {
                result.FailureReason = $"{kindLabel} option '{option.OptionId}' names no real space — refusing rather than inventing one.";
                diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                return result;
            }
            AccommodationSpace space = housing.FindSpace(option.SpaceId);
            if (space == null)
            {
                result.FailureReason = $"{kindLabel} option references unknown space '{option.SpaceId}'.";
                diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                return result;
            }

            PropertyAgreement agreement = housing.RecordAgreement(
                kindLabel, option.ProviderName, $"H{result.HouseholdId}",
                option.SpaceId, option.Description, dayIndex, -1, string.Empty, diag);
            result.AgreementId = agreement != null ? agreement.AgreementId : string.Empty;

            if (members != null)
            {
                foreach (PersonState member in members)
                {
                    if (member == null || member.deathDayIndex >= 0) continue;
                    ResidentialOccupancy occ = housing.Occupy(
                        member.id, result.HouseholdId, option.SpaceId, arrangement, dayIndex, diag);
                    if (occ != null) result.OccupancyIds.Add(occ.OccupancyId);
                }
            }
            result.Executed = true;
            diag.Add($"HousingSearchExecutor: H{result.HouseholdId} moved into '{option.SpaceId}' as {arrangement} ({kindLabel}).");
            return result;
        }

        private ExecutionResult ExecuteBoarding(
            ExecutionResult result, HousingSearchOption option, IReadOnlyList<PersonState> members,
            IBoarderCheckInPort boardingPort, int dayIndex, List<string> diag)
        {
            if (boardingPort == null)
            {
                result.FailureReason = "boarding chosen but no boarding-house port is wired — the runtime must supply the register adapter.";
                diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                return result;
            }
            if (members != null)
            {
                foreach (PersonState member in members)
                {
                    if (member == null || member.deathDayIndex >= 0) continue;
                    string refusal = boardingPort.CheckIn(member.id, dayIndex, diag);
                    if (refusal != null)
                    {
                        result.FailureReason = $"boarding check-in refused for P{member.id}: {refusal}";
                        diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                        return result;
                    }
                }
            }
            result.Executed = true;
            diag.Add($"HousingSearchExecutor: H{result.HouseholdId} boarded via '{option.ProviderName}'.");
            return result;
        }

        private ExecutionResult ExecuteSellerFinanceInquiry(
            ExecutionResult result, HousingSearchOption option,
            SellerFinanceNegotiationBook negotiations, int dayIndex, List<string> diag)
        {
            if (negotiations == null)
            {
                result.FailureReason = "seller-finance negotiation book not supplied.";
                diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                return result;
            }
            SellerFinanceNegotiation negotiation = negotiations.SendInquiry(
                result.HouseholdId, option.ProviderName,
                string.IsNullOrWhiteSpace(option.SpaceId) ? option.ParcelId : option.SpaceId,
                option.Description, option.TotalPriceCents, dayIndex, diag);
            result.DelegatedSteps.Add(new HousingSearchDelegatedStep
            {
                PathKind = HousingSearchPathKind.SellerFinanceNegotiation,
                Summary = $"seller-finance inquiry sent to '{option.ProviderName}' — Phase E's note machinery connects when terms are accepted.",
                ReferenceId = negotiation != null ? negotiation.NegotiationId : string.Empty,
            });
            result.Executed = true;
            return result;
        }

        private ExecutionResult ExecuteLenderApproach(
            ExecutionResult result, HousingSearchOption option,
            LenderApproachService lenderService, EntityIdRegistry ids,
            CreditOfferWorkflow creditWorkflow, int dayIndex, List<string> diag)
        {
            if (lenderService == null || ids == null || creditWorkflow == null)
            {
                result.FailureReason = "lender approach needs the credit workflow and an id registry.";
                diag.Add($"HousingSearchExecutor: {result.FailureReason}");
                return result;
            }
            CreditRequest request = lenderService.SubmitApproach(
                ids, creditWorkflow, result.HouseholdId, option.ProviderName,
                option.TotalPriceCents > 0 ? option.TotalPriceCents : option.UpfrontCostCents,
                option.Description, dayIndex, diag);
            result.DelegatedSteps.Add(new HousingSearchDelegatedStep
            {
                PathKind = HousingSearchPathKind.LenderApproach,
                Summary = $"credit request submitted to '{option.ProviderName}' through the real credit workflow.",
                ReferenceId = request != null ? request.RequestId : string.Empty,
            });
            result.Executed = request != null;
            if (request == null)
                result.FailureReason = "credit request was refused by the workflow.";
            return result;
        }
    }

    /// <summary>
    /// Phase D: seller-finance negotiation state. The inquiry/negotiation side
    /// is built now; Phase E's seller-note machinery connects when terms are
    /// ACCEPTED. No note is issued here — negotiation only.
    /// </summary>
    public enum SellerFinanceNegotiationStatus
    {
        InquirySent = 0,
        SellerCountered = 1,
        TermsAccepted = 2,
        Declined = 3,
        Withdrawn = 4,
    }

    [Serializable]
    public sealed class SellerFinanceNegotiationEvent
    {
        public int DayIndex;
        public string Actor = string.Empty;
        public string Summary = string.Empty;

        public SellerFinanceNegotiationEvent() { }
    }

    [Serializable]
    public sealed class SellerFinanceNegotiation
    {
        public string NegotiationId = string.Empty;
        public int BuyerHouseholdId;
        public string SellerName = string.Empty;
        public string TargetId = string.Empty; // space or parcel under discussion
        public string TargetDescription = string.Empty;
        public int AskingPriceCents;
        public int ProposedDownPaymentCents;
        public int ProposedRateBps;
        public int ProposedTermMonths;
        public int ProposedMonthlyCents;
        public SellerFinanceNegotiationStatus Status = SellerFinanceNegotiationStatus.InquirySent;
        public List<SellerFinanceNegotiationEvent> History = new List<SellerFinanceNegotiationEvent>();

        public SellerFinanceNegotiation() { }
    }

    /// <summary>
    /// Phase D: the negotiation book — inquiries, counters, acceptances,
    /// declines. A TermsAccepted record is the handoff Phase E consumes to
    /// issue the real seller note.
    /// </summary>
    public sealed class SellerFinanceNegotiationBook
    {
        private readonly Dictionary<string, SellerFinanceNegotiation> negotiations =
            new Dictionary<string, SellerFinanceNegotiation>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public SellerFinanceNegotiation SendInquiry(
            int buyerHouseholdId, string sellerName, string targetId,
            string targetDescription, int askingPriceCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var negotiation = new SellerFinanceNegotiation
            {
                NegotiationId = $"sfn-{sequence++}",
                BuyerHouseholdId = buyerHouseholdId,
                SellerName = sellerName ?? string.Empty,
                TargetId = targetId ?? string.Empty,
                TargetDescription = targetDescription ?? string.Empty,
                AskingPriceCents = Math.Max(0, askingPriceCents),
                Status = SellerFinanceNegotiationStatus.InquirySent,
            };
            negotiation.History.Add(new SellerFinanceNegotiationEvent
            {
                DayIndex = dayIndex,
                Actor = $"H{buyerHouseholdId}",
                Summary = $"inquiry sent to '{sellerName}' for '{targetDescription}' (asking {askingPriceCents}c).",
            });
            negotiations[negotiation.NegotiationId] = negotiation;
            diag.Add($"SellerFinanceNegotiationBook: H{buyerHouseholdId} inquired with '{sellerName}' (negotiation '{negotiation.NegotiationId}').");
            return negotiation;
        }

        public string RecordSellerCounter(string negotiationId, int downPaymentCents, int rateBps,
            int termMonths, int monthlyCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            SellerFinanceNegotiation negotiation = Find(negotiationId);
            if (negotiation == null)
                return $"SellerFinanceNegotiationBook: unknown negotiation '{negotiationId}'.";
            negotiation.ProposedDownPaymentCents = Math.Max(0, downPaymentCents);
            negotiation.ProposedRateBps = Math.Max(0, rateBps);
            negotiation.ProposedTermMonths = Math.Max(0, termMonths);
            negotiation.ProposedMonthlyCents = Math.Max(0, monthlyCents);
            negotiation.Status = SellerFinanceNegotiationStatus.SellerCountered;
            negotiation.History.Add(new SellerFinanceNegotiationEvent
            {
                DayIndex = dayIndex, Actor = negotiation.SellerName,
                Summary = $"countered: {downPaymentCents}c down, {rateBps}bps, {termMonths}mo, {monthlyCents}c/mo.",
            });
            diag.Add($"SellerFinanceNegotiationBook: '{negotiationId}' countered by seller.");
            return null;
        }

        public string RecordTermsAccepted(string negotiationId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            SellerFinanceNegotiation negotiation = Find(negotiationId);
            if (negotiation == null)
                return $"SellerFinanceNegotiationBook: unknown negotiation '{negotiationId}'.";
            negotiation.Status = SellerFinanceNegotiationStatus.TermsAccepted;
            negotiation.History.Add(new SellerFinanceNegotiationEvent
            {
                DayIndex = dayIndex, Actor = $"H{negotiation.BuyerHouseholdId}",
                Summary = "terms accepted — ready for Phase E seller-note issuance.",
            });
            diag.Add($"SellerFinanceNegotiationBook: '{negotiationId}' terms accepted (Phase E handoff).");
            return null;
        }

        public string RecordDecline(string negotiationId, string bySeller, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            SellerFinanceNegotiation negotiation = Find(negotiationId);
            if (negotiation == null)
                return $"SellerFinanceNegotiationBook: unknown negotiation '{negotiationId}'.";
            negotiation.Status = SellerFinanceNegotiationStatus.Declined;
            negotiation.History.Add(new SellerFinanceNegotiationEvent
            {
                DayIndex = dayIndex, Actor = bySeller,
                Summary = $"declined by {bySeller}.",
            });
            diag.Add($"SellerFinanceNegotiationBook: '{negotiationId}' declined.");
            return null;
        }

        public SellerFinanceNegotiation Find(string negotiationId)
        {
            if (string.IsNullOrWhiteSpace(negotiationId)) return null;
            negotiations.TryGetValue(negotiationId, out SellerFinanceNegotiation negotiation);
            return negotiation;
        }

        /// <summary>
        /// Phase E: the TermsAccepted handoff. Maps an accepted negotiation to
        /// the closing terms Phase E's <c>SellerFinanceClosingService</c>
        /// consumes — the note is issued only here, never in the book.
        /// Returns null (with a diagnostic) unless terms were accepted.
        /// </summary>
        public SellerFinanceClosingTerms ToClosingTerms(
            string negotiationId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            SellerFinanceNegotiation negotiation = Find(negotiationId);
            if (negotiation == null)
            {
                diag.Add($"SellerFinanceNegotiationBook: unknown negotiation '{negotiationId}' — no closing terms.");
                return null;
            }
            if (negotiation.Status != SellerFinanceNegotiationStatus.TermsAccepted)
            {
                diag.Add($"SellerFinanceNegotiationBook: '{negotiationId}' is {negotiation.Status}, not TermsAccepted — no closing terms.");
                return null;
            }
            var terms = new SellerFinanceClosingTerms
            {
                NegotiationId = negotiation.NegotiationId,
                SellerName = negotiation.SellerName,
                BuyerName = $"household:{negotiation.BuyerHouseholdId}",
                AssetDescription = negotiation.TargetDescription,
                AssetInstanceId = negotiation.TargetId,
                SalePriceCents = negotiation.AskingPriceCents,
                DownPaymentCents = negotiation.ProposedDownPaymentCents,
                AnnualRateBps = negotiation.ProposedRateBps,
                TermDays = Math.Max(1, negotiation.ProposedTermMonths * 30),
            };
            diag.Add($"SellerFinanceNegotiationBook: '{negotiationId}' mapped to closing terms — " +
                $"'{terms.SellerName}' carries {terms.SalePriceCents - terms.DownPaymentCents}c for '{terms.BuyerName}'.");
            return terms;
        }

        #region Save / Load
        [Serializable]
        public sealed class SellerFinanceNegotiationBookSaveDto
        {
            public List<SellerFinanceNegotiation> Negotiations = new List<SellerFinanceNegotiation>();
        }

        public SellerFinanceNegotiationBookSaveDto CaptureSaveDto()
        {
            var dto = new SellerFinanceNegotiationBookSaveDto();
            foreach (SellerFinanceNegotiation n in negotiations.Values)
                if (n != null) dto.Negotiations.Add(n);
            return dto;
        }

        public void LoadFromSaveDto(SellerFinanceNegotiationBookSaveDto dto)
        {
            negotiations.Clear();
            if (dto?.Negotiations == null) return;
            foreach (SellerFinanceNegotiation n in dto.Negotiations)
            {
                if (n == null || string.IsNullOrWhiteSpace(n.NegotiationId)) continue;
                if (negotiations.ContainsKey(n.NegotiationId))
                {
                    diagnostics.Add($"LoadFromSaveDto: duplicate negotiation '{n.NegotiationId}' skipped.");
                    continue;
                }
                negotiations[n.NegotiationId] = n;
            }
        }
        #endregion
    }

    /// <summary>
    /// Phase D: a household approaches a lender through the REAL
    /// <see cref="CreditOfferWorkflow"/> — the same request/evaluate/counter
    /// path the player uses. The workflow decides; this service only files
    /// the approach.
    /// </summary>
    public sealed class LenderApproachService
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public CreditRequest SubmitApproach(
            EntityIdRegistry ids,
            CreditOfferWorkflow workflow,
            int householdId,
            string lenderName,
            int amountCents,
            string purpose,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (workflow == null)
            {
                diag.Add("LenderApproachService: no credit workflow — approach not filed.");
                return null;
            }
            CreditRequest request = workflow.SubmitRequest(
                ids, $"household:{householdId}", lenderName ?? string.Empty,
                Math.Max(1, amountCents), purpose ?? "housing finance",
                365, dayIndex, $"H{householdId} household income",
                collateral: string.Empty, guarantor: string.Empty, dealId: string.Empty);
            if (request == null)
            {
                diag.Add($"LenderApproachService: H{householdId} approach to '{lenderName}' refused by the workflow.");
                return null;
            }
            diag.Add($"LenderApproachService: H{householdId} filed credit request '{request.RequestId}' with '{lenderName}' ({amountCents}c).");
            return request;
        }
    }
}
