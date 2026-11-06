using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Exercises the same private activation routine called by SimulationDrivers.OnShortTick,
    /// using the production agreement, shipment, animal, wagon, task and schedule authorities.
    /// Only component dependency injection is reflected; domain state is created through APIs.
    ///
    /// Slice S5 (2026-11-05): the dispatch task now starts only with Ethan
    /// visually at staging (his home building), and a completed dispatch
    /// task refuses authorization while he is away. This fixture has no
    /// PopulationPathingDirector, so Ethan can never be visually at staging
    /// here: the start/authorize cases below assert the gate (queued +
    /// staging travel created; completion refused + held for reconcile),
    /// not a start or an authorization.
    ///
    /// NOT COVERED IN THIS FIXTURE — authorization with Ethan AT staging:
    /// satisfying IsPersonVisuallyAtBuilding needs a live
    /// PopulationPathingDirector with Ethan registered as an agent, a door
    /// anchor for his home building, and a mover in ArrivedVisible within
    /// 1.5 m — scene construction (PathingManager grid, TownWorld placed
    /// buildings, the director's managed-agent pool), not fabricable in
    /// this fixture in one slice. That case is verified instead by (a) the
    /// harness helper tests (FreightDispatchStagingTests), (b) the gate
    /// test below (refusal while away), (c) the travel-completion handler
    /// test below (journey location set on travel completion), and (d)
    /// PlayMode log confirmation (staging travel -> arrival confirmed ->
    /// dispatch started -> authorized). The production gate was NOT
    /// weakened to make this fixture pass.
    /// </summary>
    public sealed class FreightDispatchActivationIntegrationTests
    {
        private const int EthanId = 7;
        private const int Day = 12;
        private const string CarrierId = "livery_freight_003";
        private const string WagonId = "opening-freight-wagon";
        private const string ShipmentId = "freight-shipment:request-1";
        private const string AgreementId = "freight-agreement:request-1";
        private const string TaskDefinitionId = "freight-dispatch-assignment";
        private const string LotId = "aidlot-millbrook_grain_co:opening-flour-b1";
        private const int HomeBuildingId = 5;
        private const int OriginBuildingId = 9;

        private readonly List<GameObject> objects = new();
        private SimulationSystemsHub hub;
        private PopulationManager population;
        private SharedBusinessRuntimeManager businesses;
        private SimulationDrivers drivers;
        private TimeManager clock;
        private FreightProductionDriver freight;
        private AnimalState horse;
        private EquipmentAsset wagon;
        private FreightServiceAgreement agreement;
        private LogisticsShipmentState shipment;
        private WorkTask dispatchTask;

        [SetUp]
        public void SetUp()
        {
            hub = AddInactive<SimulationSystemsHub>("Dispatch Test Hub");
            population = AddInactive<PopulationManager>("Dispatch Test Population");
            businesses = AddInactive<SharedBusinessRuntimeManager>("Dispatch Test Businesses");
            drivers = AddInactive<SimulationDrivers>("Dispatch Test Drivers");
            clock = AddInactive<TimeManager>("Dispatch Test Clock");
            SetClock(Day);

            hub.Ids.RestoreCursor(EntityKind.Animal, 0);
            hub.Ids.RestoreCursor(EntityKind.WorkTask, 100);
            hub.Tasks.RegisterDefinition(new TaskDefinition(TaskDefinitionId, "Dispatch accepted freight shipment", 1), out _);
            JourneyTravel.RegisterTaskDefinitions(hub.Tasks);
            hub.WorkTimeBudgets.EnsureDay(Day);
            hub.WorkTimeBudgets.EnsureDay(Day + 1);

            // Slice S5: Ethan has a home building (staging) and is away
            // from it, with a Foot route home in the journey model, so the
            // staging travel task can be created by the start gate.
            Assert.IsNull(hub.Journeys.RegisterLocation(new JourneyLocation(
                "building:" + OriginBuildingId, JourneyLocationKind.Waypoint, "Ethan away", 0.1f, 0f)));
            Assert.IsNull(hub.Journeys.RegisterLocation(new JourneyLocation(
                "building:" + HomeBuildingId, JourneyLocationKind.Waypoint, "Ethan home staging", 0f, 0f)));
            Assert.IsNull(hub.Journeys.AddEdge(
                "building:" + OriginBuildingId, "building:" + HomeBuildingId, 0.1f, "home lane"));

            population.LoadFromSaveDto(new PopulationSaveDto
            {
                people = new List<PersonSaveDto>
                {
                    new PersonSaveDto
                    {
                        id = EthanId, firstName = "Ethan", lastName = "Boniecki", age = 34,
                        ageBand = AgeBand.Adult18Plus, deathDayIndex = -1, householdId = 3,
                        professionId = "freight-operator", professionName = "Freight Operator",
                        homeBuildingId = HomeBuildingId,
                        scheduleState = PopulationScheduleState.ReturningHome,
                        currentDestinationBuildingId = OriginBuildingId
                    }
                },
                nextPersonId = EthanId + 1
            });

            BusinessProfileDefinition profile = AssetDatabase.LoadAssetAtPath<BusinessProfileDefinition>(
                "Assets/Resources/Core/Economy/BusinessProfiles/LiveryFreightProfile.asset");
            Assert.IsNotNull(profile, "The focused integration fixture requires the authored B3 profile.");
            BusinessInstanceState carrier = BusinessInstanceState.Create(CarrierId, profile, -1,
                BusinessOwnerIdentity.Npc(EthanId, "Ethan", "Boniecki"));
            businesses.LoadFromSaveDtos(new[] { carrier.CaptureSaveDto() });

            horse = hub.Animals.RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Freight dispatch integration fixture");
            Assert.IsNotNull(horse);
            Assert.AreEqual("A0", horse.AnimalId.ToString());
            Assert.IsNull(HorseAuthority.Configure(horse, HorseUseCapabilities.GeneralPurpose,
                "ethan-staging", "freight-company-stable", Day));
            horse.TransportState = HorseTransportState.AttachedToWagon;
            horse.AttachedWagonAssetId = WagonId;

            wagon = new EquipmentAsset
            {
                AssetId = WagonId, Kind = "freight-wagon", DisplayName = "Opening Freight Wagon",
                Condition01 = 1f, OwnerKind = "business", OwnerId = CarrierId,
                LocationId = "ethan-staging", HitchPreparationShipmentId = ShipmentId,
                HitchingAnimalIds = new List<string> { horse.AnimalId.ToString() }
            };
            Assert.IsNull(WagonAuthority.Configure(wagon, WagonSubtype.GeneralFarmFreight.ToString(),
                450f, 4f, "ethan-staging"));
            Assert.IsNull(hub.TransportAssets.Register(wagon));

            var hooks = new FreightShipmentHooks
            {
                Tasks = hub.Tasks, Budgets = hub.WorkTimeBudgets, DayIndex = Day,
                Diagnostics = new List<string>(), Reservations = hub.ReservationAuthority,
                NowGameSeconds = Day * 86400f + 8 * 3600f
            };
            freight = new FreightProductionDriver();
            freight.EnsureCompany(CarrierId);
            freight.Service.RegisterWagon(wagon);
            FreightServiceAgreementBook book = freight.ServiceAgreements;
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = "bakery_b1", CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = "bakery_b1",
                CargoLotId = LotId, Quantity = 10, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door"
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = 125, PayerBusinessId = "bakery_b1", PaymentTiming = "After successful delivery"
            }));
            Assert.IsNull(book.Accept(request.RequestId, EthanId, Day, 40000f, out agreement));
            shipment = freight.CreateShipment(CarrierId, ShipmentId, "millbrook_grain_co", "bakery_b1", 1,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), new List<EntityId> { horse.AnimalId },
                125, "bakery_b1", hooks.Diagnostics, out string shipmentRefusal,
                AgreementId, LotId, "millbrook_flour_sack", true);
            Assert.IsNotNull(shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(agreement.AgreementId, ShipmentId, 50000f));

            dispatchTask = hub.Tasks.CreateTaskWithPlannedMinutes(TaskDefinitionId,
                EntityId.For(EntityKind.Business, 3), Day, 1,
                "freight-dispatch:" + AgreementId + ":" + ShipmentId,
                JsonUtility.ToJson(new DispatchTerms
                {
                    AgreementId = AgreementId, ShipmentId = ShipmentId, OperatorPersonId = EthanId,
                    HorseId = horse.AnimalId.ToString(), WagonId = WagonId
                }));
            Assert.IsNull(book.RegisterDispatchTask(AgreementId, ShipmentId, dispatchTask.TaskId.ToString()));
            freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { horse.AnimalId });

            // Hitch completion leaves the prepared identities available. Reserve the actual draft
            // team through HorseAuthority/WagonAuthority for this shipment before dispatch starts.
            string reserveRefusal = WagonAuthority.TryAssignDraftTeam(wagon,
                new List<AnimalState> { horse }, EthanId.ToString(), ShipmentId,
                new AnimalAvailabilityAuthority(hub.ReservationAuthority), hooks.NowGameSeconds,
                hooks.NowGameSeconds + 2 * 86400f);
            Assert.IsNull(reserveRefusal, reserveRefusal);

            SetField(drivers, "hub", hub);
            SetField(drivers, "populationManager", population);
            SetField(drivers, "sharedBusinessRuntime", businesses);
            SetField(drivers, "freightProduction", freight);
            SetField(drivers, "timeManager", clock);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in objects) if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            objects.Clear();
        }

        [Test]
        public void ShortTickDispatchActivation_GatesStartWhileEthanAwayFromStaging_CreatesStagingTravel()
        {
            InvokeProductionActivation(Day);
            Assert.AreEqual(TaskStatus.Queued, dispatchTask.Status);
            Assert.AreEqual(1, OpenTravelTasks().Count);
            WorkTask travel = OpenTravelTasks()[0];
            Assert.AreEqual(FreightDispatchStaging.TravelCustomerRef(AgreementId, ShipmentId), travel.CustomerRef);
            Assert.IsTrue(travel.TermsRef.Contains("building:" + HomeBuildingId),
                "the staging travel task must name Ethan's home building as its destination");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.IsTrue(shipment.requiresDispatchAuthorization);
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(LotId, shipment.CargoLotId);
            Assert.AreEqual(HorseUseKind.Draft, horse.CurrentUse);
            Assert.AreEqual(ShipmentId, wagon.CurrentShipmentId);
            Assert.AreEqual(EthanId.ToString(), wagon.CurrentDriverPersonId);

            // Repeated ticks never start dispatch and never duplicate the
            // staging travel task: at most one open travel task exists.
            InvokeProductionActivation(Day);
            InvokeProductionActivation(Day);

            Assert.AreEqual(TaskStatus.Queued, dispatchTask.Status);
            Assert.AreEqual(1, OpenTravelTasks().Count);
            Assert.AreEqual(1, hub.Tasks.GetAllOpenTasks().FindAll(task =>
                task != null && task.CustomerRef == "freight-dispatch:" + AgreementId + ":" + ShipmentId).Count);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.IsTrue(shipment.requiresDispatchAuthorization);
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(LotId, shipment.CargoLotId);
        }

        [Test]
        public void ShortTickDispatchActivation_RetainsQueuedTaskDuringRest_AndStaysGatedNextDayWithoutArrival()
        {
            Assert.IsNull(hub.PersonSchedules.TryReserve(EthanId, PersonActivityKind.Sleeping,
                Day, 0, 1440, "authored-rest:ethan"));

            InvokeProductionActivation(Day);
            Assert.AreEqual(TaskStatus.Queued, dispatchTask.Status);
            Assert.IsTrue(shipment.requiresDispatchAuthorization);
            Assert.AreEqual(1, hub.Tasks.GetAllOpenTasks().FindAll(task =>
                task != null && task.CustomerRef == "freight-dispatch:" + AgreementId + ":" + ShipmentId).Count);

            // No PopulationPathingDirector exists in this fixture, so Ethan
            // can never be visually at staging here: dispatch stays queued
            // on the next day too, with exactly one staging travel task open.
            InvokeProductionActivation(Day + 1);
            Assert.AreEqual(TaskStatus.Queued, dispatchTask.Status);
            Assert.AreEqual(1, OpenTravelTasks().Count);
            Assert.AreEqual(1, hub.Tasks.GetAllOpenTasks().FindAll(task =>
                task != null && task.CustomerRef == "freight-dispatch:" + AgreementId + ":" + ShipmentId).Count);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.IsTrue(shipment.requiresDispatchAuthorization);
        }

        [Test]
        public void CompletedDispatchTravelTask_SetsEthanJourneyLocationToStaging()
        {
            PersonState ethan = population.State.GetPerson(EthanId);
            Assert.IsNotNull(ethan);
            Assert.AreEqual(OriginBuildingId, ethan.currentDestinationBuildingId);

            WorkTask travel = hub.Tasks.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId,
                EntityId.For(EntityKind.Business, 3), Day, 1,
                FreightDispatchStaging.TravelCustomerRef(AgreementId, ShipmentId),
                JsonUtility.ToJson(new DispatchTravelTerms
                {
                    AgreementId = AgreementId, ShipmentId = ShipmentId, OperatorPersonId = EthanId,
                    StagingBuildingId = HomeBuildingId, StagingLocationId = "building:" + HomeBuildingId,
                    DurationMinutes = 2
                }));
            Assert.IsNotNull(travel);

            MethodInfo handler = typeof(SimulationDrivers).GetMethod("HandleCompletedFreightDispatchTravelTask",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(handler);
            handler.Invoke(drivers, new object[] { travel, Day });
            Assert.AreEqual(HomeBuildingId, ethan.currentDestinationBuildingId);

            // The handler no-ops for every other task ref: the dispatch
            // work task itself must not move Ethan's journey location.
            ethan.currentDestinationBuildingId = OriginBuildingId;
            handler.Invoke(drivers, new object[] { dispatchTask, Day });
            Assert.AreEqual(OriginBuildingId, ethan.currentDestinationBuildingId);
        }

        [Test]
        public void CompletedDispatchTask_RefusesAuthorizationWhileEthanAwayFromStaging()
        {
            // Drive the dispatch task to Complete through the TaskAuthority
            // lifecycle, bypassing the start gate — the exact shape of a
            // task that completed while Ethan was away. The completion
            // handler must refuse authorization; reconcile retries it every
            // ShortTick, so it stays held (never a dead end) until he arrives.
            Assert.IsTrue(hub.Tasks.AssignTask(dispatchTask.TaskId, EntityId.For(EntityKind.Person, EthanId),
                hub.WorkTimeBudgets, Day, out string assignRefusal), assignRefusal);
            Assert.IsTrue(hub.Tasks.StartTask(dispatchTask.TaskId, Day, out string startRefusal), startRefusal);
            Assert.IsTrue(hub.Tasks.CompleteTask(dispatchTask.TaskId, TaskOutcome.Success, Day, hub.WorkTimeBudgets));
            Assert.AreEqual(TaskStatus.Complete, dispatchTask.Status);
            Assert.IsTrue(shipment.requiresDispatchAuthorization);

            MethodInfo reconcile = typeof(SimulationDrivers).GetMethod("ReconcileCompletedFreightDispatchTasks",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(reconcile);
            reconcile.Invoke(drivers, null);
            reconcile.Invoke(drivers, null);
            Assert.AreEqual(TaskStatus.Complete, dispatchTask.Status);
            Assert.IsTrue(shipment.requiresDispatchAuthorization,
                "a dispatch task completed while Ethan was away must not authorize until he reaches staging");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status,
                "refused authorization must not imply travel, pickup, or loading");
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(LotId, shipment.CargoLotId);
            Assert.AreEqual(AgreementId, shipment.freightServiceAgreementId);
            Assert.AreEqual(dispatchTask.TaskId.ToString(), agreement.DispatchTaskId);
        }

        private List<WorkTask> OpenTravelTasks()
        {
            return hub.Tasks.GetAllOpenTasks().FindAll(task =>
                task != null && task.CustomerRef == FreightDispatchStaging.TravelCustomerRef(AgreementId, ShipmentId));
        }

        private void InvokeProductionActivation(int day)
        {
            SetClock(day);
            MethodInfo method = typeof(SimulationDrivers).GetMethod("StartQueuedFreightDispatchTasks",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(drivers, new object[] { day, 480, 481 });
        }

        private void SetClock(int day)
        {
            SetField(clock, "absoluteDayIndex", day);
            SetField(clock, "gameSecondsIntoDay", 8 * 3600f);
            SetField(clock, "timeOfDay01", 0.33333334f);
        }

        private T AddInactive<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            objects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing test dependency field " + name);
            field.SetValue(target, value);
        }

        [Serializable]
        private sealed class DispatchTerms
        {
            public string AgreementId;
            public string ShipmentId;
            public int OperatorPersonId;
            public string HorseId;
            public string WagonId;
        }

        // Mirrors SimulationDrivers.FreightDispatchTravelTaskData field for
        // field, so JsonUtility round-trips the fabricated travel task's
        // TermsRef into the production data class.
        [Serializable]
        private sealed class DispatchTravelTerms
        {
            public string AgreementId;
            public string ShipmentId;
            public int OperatorPersonId;
            public int StagingBuildingId;
            public string StagingLocationId;
            public int DurationMinutes;
        }
    }
}
