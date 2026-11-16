using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// DEF-004 (Decision 4): builds a ConstructionFreightPlan for one
    /// WorkPackage from real authorities — the live lumber yard, the real
    /// journey model, the real freight business, the real wagon and horses,
    /// and the real commissioning-household crew — quoted through the
    /// freight company's authored rate card (25c dispatch plus 1c per mile,
    /// round trip, provisional per Decision 4).
    ///
    /// Pure resolution + quoting: it creates no stock, moves no cash, and
    /// persists nothing. It refuses loudly (null + LastRefusal) at every
    /// missing link — no invented miles, wagon, crew, horses, or charge.
    /// The adapter calls the factory as Func&lt;WorkPackage,
    /// ConstructionFreightPlan&gt;, so there is no dayIndex or diag channel:
    /// the specific refusal is recorded on LastRefusal and Diagnostics, and
    /// the adapter's own refusal names the package and reaches the console
    /// through the driver's diag logging.
    ///
    /// The site endpoint is the commissioning household's home building
    /// journey location (building:&lt;homeBuildingId&gt; of the household's
    /// first active member). The household builds on land they hold,
    /// reached via their own road access; the distance is still measured
    /// on the real journey model (road-cell edges), never a fixed number.
    /// A future slice can give sites their own access points once parcels
    /// carry them.
    ///
    /// The crew is buyer-provided handling at both ends (Canon Part VI
    /// section 6.2: the buyer provides unloading; loading at the yard by
    /// the buyer's hands is the same shape, and the yard has no authored
    /// staff). The driver is the freight business owner; the loader and
    /// unloader are the first two distinct active members of the
    /// commissioning household, excluding the driver.
    ///
    /// CargoSpec is the lumber calibration (DEF-016: one milled white-pine
    /// board, 16 kg / 0.04 m3) when every material line is lumber, and the
    /// FreightCargoSpec default (1 kg / 0.01 m3 per unit) otherwise, with a
    /// diagnostic naming the fallback. The spec is a freight parameter, not
    /// item truth — calibrating other materials is a follow-up.
    ///
    /// DEF-016 (Decision 10): the quote is cargo-aware and multi-trip —
    /// the same 25c dispatch + 1c/mile round-trip card, priced per trip
    /// over the wagon's per-trip capacity, with conditioned miles billed
    /// through the route's own multiplier. The plan carries the total,
    /// the per-trip charge, and the trip count; the coordinator trusts
    /// the plan and reconciles it before any money moves.
    /// </summary>
    public sealed class ConstructionFreightPlanFactory
    {
        private readonly Func<string, ConstructionProject> findProject;
        private readonly string yardBusinessInstanceId;
        private readonly string wagonAssetId;
        private readonly Func<JourneyModel> journeyModel;
        private readonly Func<BusinessInstanceState> freightBusiness;
        private readonly int baseServiceFeeCents;
        private readonly int ratePerMileCents;
        private readonly bool chargeRoundTrip;
        private readonly Func<string, EquipmentAsset> findWagon;
        private readonly Func<IReadOnlyCollection<AnimalState>> activeAnimals;
        private readonly Func<int, IReadOnlyList<int>> householdMembers;
        private readonly Func<int, int> homeBuildingIdForPerson;
        private readonly FreightShipmentService freight;
        private readonly TaskAuthority tasks;
        private readonly WorkTimeBudgetStore budgets;
        private readonly PersonScheduleTracker tracker;
        private readonly CreditCashBridge cash;

        private readonly List<string> diagnostics = new List<string>();

        /// <summary>The specific reason the most recent Build refused, or empty when the last Build succeeded.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>Every refusal this factory has recorded, in order. Diagnostic surface only; never a save field.</summary>
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public ConstructionFreightPlanFactory(
            Func<string, ConstructionProject> findProject,
            string yardBusinessInstanceId,
            string wagonAssetId,
            Func<JourneyModel> journeyModel,
            Func<BusinessInstanceState> freightBusiness,
            int baseServiceFeeCents,
            int ratePerMileCents,
            bool chargeRoundTrip,
            Func<string, EquipmentAsset> findWagon,
            Func<IReadOnlyCollection<AnimalState>> activeAnimals,
            Func<int, IReadOnlyList<int>> householdMembers,
            Func<int, int> homeBuildingIdForPerson,
            FreightShipmentService freight,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            PersonScheduleTracker tracker,
            CreditCashBridge cash)
        {
            this.findProject = findProject;
            this.yardBusinessInstanceId = yardBusinessInstanceId;
            this.wagonAssetId = wagonAssetId;
            this.journeyModel = journeyModel;
            this.freightBusiness = freightBusiness;
            this.baseServiceFeeCents = baseServiceFeeCents;
            this.ratePerMileCents = ratePerMileCents;
            this.chargeRoundTrip = chargeRoundTrip;
            this.findWagon = findWagon;
            this.activeAnimals = activeAnimals;
            this.householdMembers = householdMembers;
            this.homeBuildingIdForPerson = homeBuildingIdForPerson;
            this.freight = freight;
            this.tasks = tasks;
            this.budgets = budgets;
            this.tracker = tracker;
            this.cash = cash;
        }

        /// <summary>
        /// Builds the freight plan for one package, or null with LastRefusal
        /// set when any link is missing. Never invents a route, a wagon, a
        /// crew, a horse, or a charge.
        /// </summary>
        public ConstructionFreightPlan Build(WorkPackage package)
        {
            LastRefusal = string.Empty;
            if (package == null)
                return Refuse("ConstructionFreightPlanFactory: no work package — nothing to freight.");
            if (string.IsNullOrWhiteSpace(package.ProjectId))
                return Refuse("ConstructionFreightPlanFactory: package '" + package.PackageId + "' names no project — refusing.");

            ConstructionProject project = findProject != null ? findProject(package.ProjectId) : null;
            if (project == null)
                return Refuse("ConstructionFreightPlanFactory: no construction project '" + package.ProjectId + "' — refusing.");
            if (project.HouseholdId < 0)
                return Refuse("ConstructionFreightPlanFactory: project '" + project.ProjectId + "' names no commissioning household — refusing.");

            JourneyModel model = journeyModel != null ? journeyModel() : null;
            if (model == null)
                return Refuse("ConstructionFreightPlanFactory: no journey model — yard-to-site distance cannot be measured, refusing.");

            JourneyLocation yardLocation = JourneyTravel.FindBusinessLocation(model, yardBusinessInstanceId);
            if (yardLocation == null || string.IsNullOrWhiteSpace(yardLocation.LocationId))
                return Refuse("ConstructionFreightPlanFactory: no journey location for lumber yard '" + yardBusinessInstanceId + "' — refusing.");

            IReadOnlyList<int> members = householdMembers != null ? householdMembers(project.HouseholdId) : null;
            if (members == null || members.Count == 0)
                return Refuse("ConstructionFreightPlanFactory: household " + project.HouseholdId + " has no active members — no crew and no site endpoint, refusing.");

            int homeBuildingId = homeBuildingIdForPerson != null ? homeBuildingIdForPerson(members[0]) : -1;
            if (homeBuildingId < 0)
                return Refuse("ConstructionFreightPlanFactory: household " + project.HouseholdId + "'s first active member has no home building — no site endpoint, refusing.");
            string siteLocationId = TownJourneyNetworkBuilder.LocationIdForBuilding(homeBuildingId);

            JourneyTravelEstimate estimate = JourneyTravel.EstimateDrive(model, yardLocation.LocationId, siteLocationId);
            if (estimate == null || !estimate.FromModel)
                return Refuse("ConstructionFreightPlanFactory: no route from the lumber yard to the build site for project '" + project.ProjectId + "'"
                    + (estimate != null && !string.IsNullOrWhiteSpace(estimate.Diagnostic) ? " — " + estimate.Diagnostic : string.Empty)
                    + " — refusing; yard-to-site distance is measured, never invented.");
            if (estimate.Miles <= 0f)
                return Refuse("ConstructionFreightPlanFactory: the yard-to-site route for project '" + project.ProjectId + "' measures " + estimate.Miles + " miles — a freight quote needs positive miles, refusing.");

            // DEF-016: the load is the sum of every material line. A
            // package with no materials has nothing to freight — refuse
            // loudly instead of quoting a price for nothing.
            List<ConstructionMaterialRequirement> materials = package.Materials ?? new List<ConstructionMaterialRequirement>();
            int totalUnits = 0;
            foreach (ConstructionMaterialRequirement requirement in materials)
                totalUnits += Math.Max(0, requirement.RequiredUnits);
            if (totalUnits <= 0)
                return Refuse("ConstructionFreightPlanFactory: package '" + package.PackageId + "' has no materials to freight.");

            // DEF-016: the cargo spec is a freight parameter, not item
            // truth (see LumberCargoSpec). All-lumber loads freight on
            // the lumber calibration; anything else falls back to the
            // default spec exactly as today, with a diagnostic naming
            // the fallback — calibrating other materials is a follow-up.
            bool allLumber = true;
            foreach (ConstructionMaterialRequirement requirement in materials)
                if (requirement.MaterialKind != ConstructionMaterialKind.Lumber)
                    allLumber = false;
            FreightCargoSpec cargoSpec = allLumber
                ? LumberCargoSpec.Spec
                : new FreightCargoSpec();
            if (!allLumber)
                diagnostics.Add("ConstructionFreightPlanFactory: non-lumber materials in package '" + package.PackageId + "' — freighting on the default cargo spec (no calibrated spec for that material yet).");

            BusinessInstanceState carrier = freightBusiness != null ? freightBusiness() : null;
            if (carrier == null)
                return Refuse("ConstructionFreightPlanFactory: no freight business — no carrier for project '" + project.ProjectId + "', refusing.");
            int driverPersonId = carrier.Owner != null ? carrier.Owner.PersonId : -1;
            if (driverPersonId < 0)
                return Refuse("ConstructionFreightPlanFactory: freight business '" + carrier.InstanceId + "' has no owner person — no driver, refusing.");

            EquipmentAsset wagon = findWagon != null ? findWagon(wagonAssetId) : null;
            if (wagon == null)
                return Refuse("ConstructionFreightPlanFactory: wagon '" + wagonAssetId + "' does not exist — no phantom transport, refusing.");
            if (!string.Equals(wagon.OwnerKind, "business", StringComparison.Ordinal)
                || !string.Equals(wagon.OwnerId, carrier.InstanceId, StringComparison.Ordinal))
                return Refuse("ConstructionFreightPlanFactory: wagon '" + wagonAssetId + "' is not owned by freight business '" + carrier.InstanceId + "' — refusing.");
            if (wagon.PayloadMassCapacityKg <= 0f || wagon.CargoVolumeCapacityM3 <= 0f)
                return Refuse("ConstructionFreightPlanFactory: wagon '" + wagonAssetId + "' has no payload capacity (" + wagon.PayloadMassCapacityKg + " kg / " + wagon.CargoVolumeCapacityM3 + " m3) — refusing.");

            // Quote (DEF-016): cargo-aware, multi-trip, condition-aware.
            // The 25c dispatch + 1c/mile round-trip card is unchanged; the
            // quote now prices trips (quantity over the wagon's per-trip
            // capacity) and bills conditioned miles through the route's
            // own multiplier.
            if (!FreightServiceQuoteCalculator.TryCalculateCargo(estimate.Miles, baseServiceFeeCents, ratePerMileCents, chargeRoundTrip,
                    totalUnits, cargoSpec, wagon.PayloadMassCapacityKg, wagon.CargoVolumeCapacityM3,
                    estimate.ConditionMultiplier, out FreightServiceQuote quote, out string quoteRefusal))
                return Refuse("ConstructionFreightPlanFactory: no freight quote for project '" + project.ProjectId + "' — "
                    + (string.IsNullOrWhiteSpace(quoteRefusal) ? "the quote calculator refused." : quoteRefusal));

            List<EntityId> draftAnimals = new List<EntityId>();
            IReadOnlyCollection<AnimalState> animals = activeAnimals != null ? activeAnimals() : null;
            if (animals != null)
            {
                foreach (AnimalState candidate in animals)
                {
                    if (candidate != null && candidate.IsActive && candidate.Species == AnimalSpecies.Horse
                        && candidate.OwnerKind == AnimalOwnerKind.Business
                        && string.Equals(candidate.OwnerId, carrier.InstanceId, StringComparison.Ordinal))
                        draftAnimals.Add(candidate.AnimalId);
                }
            }
            if (draftAnimals.Count == 0)
                return Refuse("ConstructionFreightPlanFactory: freight business '" + carrier.InstanceId + "' has no active horse — no draft animals, refusing.");

            int loaderPersonId = -1;
            int unloaderPersonId = -1;
            for (int i = 0; i < members.Count; i++)
            {
                int memberId = members[i];
                if (memberId < 0 || memberId == driverPersonId) continue;
                if (loaderPersonId < 0) loaderPersonId = memberId;
                else if (unloaderPersonId < 0) { unloaderPersonId = memberId; break; }
            }
            if (loaderPersonId < 0 || unloaderPersonId < 0)
                return Refuse("ConstructionFreightPlanFactory: household " + project.HouseholdId + " has fewer than two active members besides the driver — the buyer cannot staff both loading and unloading, refusing.");

            var plan = new ConstructionFreightPlan
            {
                Freight = freight,
                Wagon = wagon,
                DriverId = EntityId.For(EntityKind.Person, driverPersonId),
                DraftAnimalIds = draftAnimals,
                LoaderId = EntityId.For(EntityKind.Person, loaderPersonId),
                UnloaderId = EntityId.For(EntityKind.Person, unloaderPersonId),
                Tasks = tasks,
                Budgets = budgets,
                Tracker = tracker,
                Cash = cash,
                SiteLabel = project.ProjectId,
                // DEF-016: the total is the quoted total across all trips;
                // per-trip and trip count come from the same quote.
                FreightChargeCents = quote.TotalPriceCents,
                FreightChargeCentsPerTrip = quote.PerTripTotalCents,
                TripCount = quote.TripCount,
                CarrierPayeeLabel = "business:" + carrier.InstanceId,
                BuyerOwnerName = "household:" + project.HouseholdId,
                CargoSpec = cargoSpec,
                TransitGameSeconds = Math.Max(1f, estimate.MinutesOneWay * 60f),
            };
            return plan;
        }

        private ConstructionFreightPlan Refuse(string reason)
        {
            LastRefusal = reason;
            diagnostics.Add(reason);
            return null;
        }
    }
}
