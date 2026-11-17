using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// M1-R1: daily housing-search drive, boarding only. The household housing
    /// loop was dead end to end (F-M1-1 through F-M1-3, F-M1-9): needs were
    /// opened and never answered. This drive closes the one leg that is safe
    /// to run today. Rental options carry no rent (F-M1-5), so rentals are
    /// withheld from planning rather than executed as rent-free tenancies.
    /// Purchase, seller-finance, lender, land, and relocation paths have no
    /// live sources, so the adapter returns none and the drive never sees
    /// them. Boarding has all three legs in production: real vacancies from
    /// registered lodging, a real check-in port, and real weekly settlement
    /// through the rent collection. Documented income is passed as zero, so
    /// affordability falls back to the planner cash runway rule, one quarter
    /// of current cash, which is the planner own documented fallback.
    /// </summary>
    public sealed class HousingSearchDrive
    {
        public sealed class DriveResult
        {
            public int NeedsSeen;
            public int Boarded;
            public int StillOpen;
            public int Skipped;
        }

        /// <param name="boardingPortForBusiness">
        /// Resolves a boarding-house business instance id to its check-in
        /// port, or null when no runtime is registered for it. Production
        /// builds this from the hub boarding runtimes.
        /// </param>
        /// <param name="isAlreadyBoarder">
        /// True when the person already holds a boarding agreement. Lets the
        /// drive recognise a household that is already housed in boarding and
        /// resolve its need instead of checking it in twice. The boarder
        /// register refuses duplicates, so a null predicate is safe, just
        /// noisier: the execution fails and the need stays open.
        /// </param>
        public static DriveResult Drive(
            PopulationState state,
            HousingSearchNeedRegistry needs,
            HousingAuthority housing,
            LodgingRegistry lodging,
            Func<string, IBoarderCheckInPort> boardingPortForBusiness,
            Func<int, bool> isAlreadyBoarder,
            HouseholdLedgerRegistry ledgers,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new DriveResult();
            if (state == null || needs == null || housing == null || lodging == null)
            {
                diag.Add("HousingSearchDrive: missing state, needs, housing authority, or lodging registry - nothing driven.");
                return result;
            }

            var all = new List<HousingSearchNeed>();
            needs.ExportState(all);
            var planner = new HousingSearchPlanner();
            var executor = new HousingSearchExecutor();
            var composer = new HouseholdShelterSuitabilityEvaluator();
            foreach (HousingSearchNeed need in all)
            {
                if (need == null || need.Status != HousingSearchNeedStatus.Open) continue;
                result.NeedsSeen++;

                HouseholdState household = state.GetHousehold(need.HouseholdId);
                if (household == null)
                {
                    result.Skipped++;
                    diag.Add($"HousingSearchDrive: open need '{need.NeedId}' names unknown household {need.HouseholdId} - left open.");
                    continue;
                }

                var members = new List<PersonState>();
                if (household.memberIds != null)
                {
                    foreach (int personId in household.memberIds)
                    {
                        PersonState person = state.GetPerson(personId);
                        if (person != null && person.deathDayIndex < 0) members.Add(person);
                    }
                }
                if (members.Count == 0)
                {
                    result.Skipped++;
                    diag.Add($"HousingSearchDrive: H{household.id} has no living members - need '{need.NeedId}' left open.");
                    continue;
                }

                // Suitably housed households open needs to relocate cheaper.
                // Boarding them without ending their tenancy would double-house
                // them, paying board and rent at once. They wait for the rental
                // leg, which needs a rent source first (F-M1-5). Only households
                // with no occupancy at all are boarded here.
                if (need.CurrentCondition == ResidentialConditionKind.SuitablyHoused)
                {
                    result.StillOpen++;
                    diag.Add($"HousingSearchDrive: H{household.id} is suitably housed and seeks cheaper rent - boarding is not the answer; need left open for the rental leg.");
                    continue;
                }

                if (isAlreadyBoarder != null)
                {
                    bool allBoarded = true;
                    foreach (PersonState member in members)
                    {
                        if (!isAlreadyBoarder(member.id)) { allBoarded = false; break; }
                    }
                    if (allBoarded)
                    {
                        needs.MarkResolved(household.id, dayIndex);
                        result.Boarded++;
                        diag.Add($"HousingSearchDrive: H{household.id} is already boarded - need '{need.NeedId}' resolved without a second check-in.");
                        continue;
                    }
                }

                HouseholdCompositionSummary composition = composer.SummarizeComposition(household.id, members);
                HouseholdLedger ledger = ledgers != null ? ledgers.Get(household.id) : null;
                int cash = ledger != null ? ledger.GetBalanceCents() : 0;
                IHousingOpportunitySource source = new BoardingOnlyOpportunitySource(
                    new HousingOpportunityAdapter(housing, lodging));
                HousingSearchDecision plan = planner.Plan(need, composition, cash, 0, source, dayIndex, diag);
                if (!plan.Resolved || plan.ChosenOption == null)
                {
                    result.StillOpen++;
                    continue;
                }
                if (plan.ChosenOption.PathKind != HousingSearchPathKind.Boarding)
                {
                    result.StillOpen++;
                    diag.Add($"HousingSearchDrive: H{household.id} planner resolved '{plan.ChosenOption.PathKind}' - this drive executes boarding only; need left open.");
                    continue;
                }

                IBoarderCheckInPort port = boardingPortForBusiness != null
                    ? boardingPortForBusiness(plan.ChosenOption.ProviderName)
                    : null;
                if (port == null)
                {
                    result.StillOpen++;
                    diag.Add($"HousingSearchDrive: H{household.id} boarding at '{plan.ChosenOption.ProviderName}' has no wired boarding port - need left open.");
                    continue;
                }

                HousingSearchExecutor.ExecutionResult executed = executor.Execute(
                    plan, members, housing, port, null, null, null, null, dayIndex, diag);
                if (executed != null && executed.Executed)
                {
                    needs.MarkResolved(household.id, dayIndex);
                    result.Boarded++;
                }
                else
                {
                    result.StillOpen++;
                    diag.Add($"HousingSearchDrive: H{household.id} boarding execution failed - {(executed != null ? executed.FailureReason : "no result")} - need left open.");
                }
            }
            return result;
        }

        /// <summary>
        /// M1-R1: boarding-only view over a real opportunity source. Rental
        /// options carry no rent today (F-M1-5), so they are withheld from
        /// planning rather than executed as rent-free tenancies. Every other
        /// path forwards empty, matching the adapter own honest empties.
        /// </summary>
        private sealed class BoardingOnlyOpportunitySource : IHousingOpportunitySource
        {
            private readonly IHousingOpportunitySource inner;

            public BoardingOnlyOpportunitySource(IHousingOpportunitySource inner)
            {
                this.inner = inner;
            }

            public List<HousingSearchOption> RentalListings(int requiredPlaces)
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> BoardingVacancies(int requiredPlaces)
            {
                return inner != null ? inner.BoardingVacancies(requiredPlaces) : new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> KinOffers(int householdId)
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> EmployerLodgingOpenings(int householdId)
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> TemporaryShelters()
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> PropertiesForSale(int requiredPlaces)
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> SellerFinanceOffers()
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> LenderOffers(int householdId)
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> LandParcelsForSale()
            {
                return new List<HousingSearchOption>();
            }

            public List<HousingSearchOption> RelocationDestinations(int householdId)
            {
                return new List<HousingSearchOption>();
            }
        }
    }
}
