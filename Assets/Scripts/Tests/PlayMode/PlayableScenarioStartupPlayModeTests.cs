using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LandLedgers.PlayMode
{
    public sealed class PlayableScenarioStartupPlayModeTests
    {
        [UnityTest]
        public IEnumerator DevelopmentTownConfigurationStartsWithTheNamedPeopleHouseholdsAndBusinesses()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone) yield return null;
            yield return null;

            Component bootstrapper = FindComponent(RuntimeType("LandLedgers.FirstLedger.FirstLedgerSliceBootstrapper"));
            Assert.NotNull(bootstrapper, "The development profile is configured on the Main Scene bootstrapper.");
            bool fixtureEnabled = (bool)(bootstrapper.GetType()
                .GetField("enableDevelopmentTownConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(bootstrapper) ?? false);
            if (!fixtureEnabled)
            {
                Assert.Ignore("Opt-in integration test; enable the six-person Main Scene profile to run it.");
            }

            Type populationManagerType = RuntimeType("LandLedgers.Population.PopulationManager");
            Type businessRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Component populationManager = FindComponent(populationManagerType);
            Component businessRuntime = FindComponent(businessRuntimeType);
            Assert.NotNull(populationManager);
            Assert.NotNull(businessRuntime);

            object population = populationManagerType.GetProperty("State")?.GetValue(populationManager);
            var people = population?.GetType().GetField("people")?.GetValue(population) as System.Collections.IList;
            var households = population?.GetType().GetField("households")?.GetValue(population) as System.Collections.IList;
            Assert.NotNull(people);
            Assert.NotNull(households);
            object[] onMapPeople = people.Cast<object>()
                .Where(person => !(bool)person.GetType().GetField("isOffMap")?.GetValue(person))
                .ToArray();
            Assert.AreEqual(9, onMapPeople.Length, "The development population contains nine on-map authoritative Persons.");
            Assert.AreEqual(5, households.Count, "The development population contains five Households.");
            object[] orderedPeople = onMapPeople
                .OrderBy(person => (int)person.GetType().GetField("id")?.GetValue(person)).ToArray();
            CollectionAssert.AreEqual(
                new[] { "Kennedy Baldwin-Ooms", "Emma Baldwin-Ooms", "Glen Pierce", "Tannis Pierce", "Jacob Boniecki", "Grace Boniecki", "Ethan Boniecki", "Tia Boniecki", "Shane Bryan" },
                orderedPeople.Select(person => person.GetType().GetProperty("DisplayName")?.GetValue(person) as string).ToArray());

            Component pathingDirector = FindComponent(RuntimeType("LandLedgers.FirstLedger.PopulationPathingDirector"));
            Assert.NotNull(pathingDirector, "The real Main Scene must bind visible actors through its population pathing authority.");
            Type visualBindingType = RuntimeType("LandLedgers.Population.PersonVisualIdentityBinding");
            MonoBehaviour[] actorBindings = pathingDirector.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(component => component != null && visualBindingType.IsInstanceOfType(component))
                .ToArray();
            Assert.AreEqual(9, actorBindings.Length,
                "Each local Person must have one stable visual actor; a population count alone is not visible street life.");
            CollectionAssert.AreEquivalent(Enumerable.Range(1, 9), actorBindings
                .Select(binding => (int)visualBindingType.GetProperty("PersonId")?.GetValue(binding))
                .ToArray(), "Every actor must resolve to exactly one of the nine authoritative Person IDs.");
            MethodInfo resolvePerson = visualBindingType.GetMethod("TryResolve");
            Assert.NotNull(resolvePerson, "The visual identity binding must resolve through PopulationState.");
            foreach (MonoBehaviour binding in actorBindings)
            {
                Assert.IsTrue(binding.gameObject.activeInHierarchy, $"Person actor {binding.name} must be active in the scene.");
                int personId = (int)visualBindingType.GetProperty("PersonId")?.GetValue(binding);
                object[] resolution = { population, null };
                Assert.IsTrue((bool)resolvePerson.Invoke(binding, resolution),
                    $"Person actor {binding.name} must resolve to its persistent population record.");
                Assert.AreEqual(personId, (int)resolution[1].GetType().GetField("id").GetValue(resolution[1]),
                    $"Person actor {binding.name} must not represent another Person's state.");
                Assert.IsTrue(binding.GetComponentInChildren<SkinnedMeshRenderer>(true) is SkinnedMeshRenderer characterMesh
                    && characterMesh.enabled && characterMesh.gameObject.activeInHierarchy && characterMesh.sharedMesh != null,
                    $"Person actor {binding.name} must use a visible character mesh, not the capsule fallback.");
                Assert.IsNotNull(binding.GetComponentInChildren<Animator>(true),
                    $"Person actor {binding.name} must use the authored character animation rig.");
            }

            var businesses = businessRuntimeType.GetProperty("Businesses")?.GetValue(businessRuntime) as System.Collections.ICollection;
            Assert.NotNull(businesses);
            Assert.AreEqual(4, businesses.Count,
                "The development roster must create all four configured businesses.");
            var ownerByType = new Dictionary<string, int>();
            var businessBuildingIds = new HashSet<int>();
            var ownerWorkplaceByPerson = new Dictionary<int, int>();
            foreach (object business in (System.Collections.IEnumerable)businesses)
            {
                string typeName = business.GetType().GetProperty("BusinessType")?.GetValue(business)?.ToString();
                object owner = business.GetType().GetProperty("Owner")?.GetValue(business);
                Assert.NotNull(owner, $"{typeName} must retain its owner authority.");
                ownerByType[typeName] = (int)owner.GetType().GetProperty("PersonId")?.GetValue(owner);
                int assignedBuildingId = (int)(business.GetType().GetProperty("AssignedBuildingId")?.GetValue(business) ?? -1);
                if (typeName == "LiveryFreight")
                {
                    Assert.AreEqual(-1, assignedBuildingId, "The authored freight business does not require a depot.");
                }
                else
                {
                    Assert.GreaterOrEqual(assignedBuildingId, 0, $"{typeName} must occupy a real physical building.");
                    Assert.IsTrue(businessBuildingIds.Add(assignedBuildingId), "Each storefront occupies its own building.");
                    ownerWorkplaceByPerson[(int)owner.GetType().GetProperty("PersonId")?.GetValue(owner)] = assignedBuildingId;
                }
            }
            CollectionAssert.AreEquivalent(new[] { "GeneralStore", "Bakery", "FuelDealer", "LiveryFreight" }, ownerByType.Keys);
            Assert.AreEqual(1, ownerByType["GeneralStore"], "Kennedy P001 owns the General Store.");
            Assert.AreEqual(3, ownerByType["Bakery"], "Glen P003 owns the Bakery.");
            Assert.AreEqual(5, ownerByType["FuelDealer"], "Jacob P005 owns the Fuel Dealer.");
            Assert.AreEqual(7, ownerByType["LiveryFreight"], "Ethan P007 owns Freight Company B3.");
            foreach (object person in orderedPeople)
            {
                int personId = (int)person.GetType().GetField("id")?.GetValue(person);
                int workplace = (int)person.GetType().GetField("workplaceBuildingId")?.GetValue(person);
                string professionId = person.GetType().GetField("professionId")?.GetValue(person) as string;
                string professionName = person.GetType().GetField("professionName")?.GetValue(person) as string;
                object wage = person.GetType().GetField("wage")?.GetValue(person);
                int weeklyWage = (int)(wage?.GetType().GetField("weeklyWage")?.GetValue(wage) ?? 0);
                if (personId == 7)
                {
                    StringAssert.StartsWith("owner_operator_", professionId);
                    Assert.AreEqual(-1, workplace, "Freight Company remains valid without an assigned depot.");
                }
                else if (ownerWorkplaceByPerson.TryGetValue(personId, out int ownerWorkplace))
                {
                    Assert.AreEqual(ownerWorkplace, workplace,
                        $"Owner P{personId:000} must have a real workplace at their own Business.");
                    StringAssert.StartsWith("owner_operator_", professionId);
                    StringAssert.StartsWith("Owner-operator, ", professionName);
                    Assert.AreEqual(0, weeklyWage,
                        "Ownership-based labor is not an employee wage obligation.");
                }
                else
                {
                    Assert.AreEqual(-1, workplace, $"Unemployed Person P{personId:000} must not have a ghost workplace.");
                    Assert.AreEqual("unemployed", professionId,
                        $"Non-owner Person P{personId:000} remains unemployed in this fixture.");
                }
            }
            Component townWorld = FindComponent(RuntimeType("LandLedgers.World.TownWorldController"));
            var townBuildings = townWorld?.GetType().GetProperty("Buildings")?.GetValue(townWorld) as System.Collections.IEnumerable;
            Assert.NotNull(townBuildings);
            var buildingLabels = new List<string>();
            var worldBuildingIds = new HashSet<int>();
            int residentialHomes = 0;
            int commercialBuildings = 0;
            foreach (object building in townBuildings)
            {
                worldBuildingIds.Add((int)building.GetType().GetField("id")?.GetValue(building));
                object definition = building.GetType().GetField("definition")?.GetValue(building);
                string label = definition?.GetType().GetProperty("DisplayName")?.GetValue(definition) as string ?? "(unnamed)";
                buildingLabels.Add(label);
                bool canHousehold = (bool)(definition?.GetType().GetProperty("CanHostHouseholds")?.GetValue(definition) ?? false);
                bool canWork = (bool)(definition?.GetType().GetProperty("CanHostWorkplace")?.GetValue(definition) ?? false);
                if (canHousehold && !canWork) residentialHomes++;
                string primaryUse = definition?.GetType().GetProperty("PrimaryUse")?.GetValue(definition)?.ToString();
                if (canWork && !string.Equals(primaryUse, "Civic", StringComparison.OrdinalIgnoreCase)) commercialBuildings++;
            }
            Assert.AreEqual(5, residentialHomes,
                "The direct configuration must bind five distinct real homes before population generation.");
            Assert.AreEqual(4, commercialBuildings,
                "The development town retains its four authored commercial premises.");
            Assert.AreEqual(9, worldBuildingIds.Count,
                "The authored Small development profile uses five separate homes and four commercial buildings; its runtime separate-use profile disables the Town Hall.");
            Assert.IsTrue(businessBuildingIds.IsSubsetOf(worldBuildingIds),
                "Every Business premises reference must resolve to one of the seven real town buildings.");
            Debug.Log($"[DevelopmentTown runtime] P001–P009; Households=5; Businesses=4 (General Store=P001, Bakery=P003, Fuel Dealer=P005, Freight Company=P007); houses={residentialHomes}; commercial buildings={commercialBuildings}; total buildings={worldBuildingIds.Count}: {string.Join(", ", buildingLabels)}.");
        }

        [UnityTest]
        public IEnumerator ProductionBusinessAuthorityFormsTheScenarioThreeBusinessRoute()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            Assert.NotNull(sharedRuntime, "The playable scene must own the shared business authority.");
            Type formedStoreRuntimeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
            Component formedStoreRuntime = FindComponent(formedStoreRuntimeType);
            Assert.NotNull(formedStoreRuntime, "The scene must own the household retail runtime.");
            object registeredStorePort = formedStoreRuntimeType.GetMethod("BuildHouseholdTradingPort", Type.EmptyTypes)
                .Invoke(formedStoreRuntime, null);
            Assert.NotNull(registeredStorePort);
            Assert.AreEqual("general-store", registeredStorePort.GetType().GetProperty("StoreBusinessId")?.GetValue(registeredStorePort),
                "Before formation, the registered port is an unbound placeholder and must have no identity or stock to sell.");
            Assert.AreEqual(0, (int)registeredStorePort.GetType().GetMethod("CategoryStockUnits")
                .Invoke(registeredStorePort, new object[] { "staple_food" }));
            object declaredOffers = registeredStorePort.GetType().GetProperty("CategoryOffers")?.GetValue(registeredStorePort);
            Assert.NotNull(declaredOffers);
            Assert.AreEqual(0, ((System.Collections.ICollection)declaredOffers).Count,
                "No pooled multi-SKU category can be truthfully represented as one item before an item-level lot offer exists.");
            Type generalStoreAdapterType = RuntimeType("LandLedgers.Economy.Businesses.GeneralStore.GeneralStoreSupplierAdapter");
            object supplierAdapter = Activator.CreateInstance(generalStoreAdapterType, registeredStorePort);
            Assert.IsFalse((bool)generalStoreAdapterType.GetMethod("OffersItem").Invoke(supplierAdapter, new object[] { "flour" }),
                "Aggregate staple stock is not an item-level flour offer; item sales require real item identity and lots.");
            Assert.AreEqual(0, (int)generalStoreAdapterType.GetMethod("ItemStockUnits")
                .Invoke(supplierAdapter, new object[] { "flour" }), "An unformed store still has no saleable inventory.");
            object beforeFormationCapability = registeredStorePort.GetType().GetProperty("TradingCapability")
                ?.GetValue(registeredStorePort);
            Assert.IsFalse((bool)beforeFormationCapability?.GetType().GetProperty("Staffed")?.GetValue(beforeFormationCapability),
                "A pre-registered supplier must not be treated as open before a real store and clerk exist.");

            var created = new List<string>();
            foreach (string typeName in new[] { "GeneralStore", "CropFarm", "LiveryFreight" })
            {
                object enumValue = Enum.Parse(businessType, typeName);
                object[] arguments = { enumValue, $"Player {typeName}", null, null };
                bool success = (bool)sharedRuntimeType.GetMethod("TryCreatePlayerBusiness")
                    .Invoke(sharedRuntime, arguments);
                Assert.IsTrue(success, $"Production formation must create {typeName}: {arguments[3]}");

                object formedBusiness = arguments[2];
                Assert.NotNull(formedBusiness, $"Formation must return the {typeName} entity.");
                if (typeName == "GeneralStore")
                {
                    object runtimeState = formedBusiness.GetType().GetProperty("RuntimeState")?.GetValue(formedBusiness);
                    Assert.NotNull(runtimeState);
                    Assert.AreEqual(0, (int)runtimeState.GetType().GetProperty("CurrentCashCents")?.GetValue(runtimeState),
                        "Profile starting cash is not a settled contribution to the newly formed Business.");
                    object[] premisesArguments = { formedBusiness, null };
                    Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignPlayerBusinessPremises")
                        .Invoke(sharedRuntime, premisesArguments), premisesArguments[1] as string);

                    object[] hireArguments = { formedBusiness, 0, null };
                    Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignCandidateToOpenSlot")
                        .Invoke(sharedRuntime, hireArguments), hireArguments[2] as string);

                    object[] bindingArguments = { formedBusiness, null };
                    bool bound = (bool)formedStoreRuntimeType.GetMethod("TryBindFormedPlayerBusiness")
                        .Invoke(formedStoreRuntime, bindingArguments);
                    Assert.IsTrue(bound, $"The formed General Store must bind to its operating runtime: {bindingArguments[1]}");

                    string businessId = formedBusiness.GetType().GetProperty("InstanceId")?.GetValue(formedBusiness) as string;
                    Assert.AreEqual(businessId, registeredStorePort.GetType().GetProperty("StoreBusinessId")?.GetValue(registeredStorePort),
                        "The originally registered port must follow the formed Business, rather than remaining the startup placeholder.");
                    Assert.AreEqual(0, (int)registeredStorePort.GetType().GetMethod("CategoryStockUnits")
                        .Invoke(registeredStorePort, new object[] { "staple_food" }),
                        "Formation creates the Business and premises, not free category stock.");
                    Assert.AreEqual(0, (int)generalStoreAdapterType.GetMethod("ItemStockUnits")
                        .Invoke(supplierAdapter, new object[] { "flour" }),
                        "The registered adapter must report no saleable flour until a real lot receipt occurs.");
                    Assert.AreEqual(0, (int)generalStoreAdapterType.GetMethod("ItemPricePerUnitCents")
                        .Invoke(supplierAdapter, new object[] { "flour" }),
                        "An unstocked pooled SKU must remain unavailable until a real item offer and lot receipt are established.");
                    object afterHireCapability = registeredStorePort.GetType().GetProperty("TradingCapability")
                        ?.GetValue(registeredStorePort);
                    Assert.IsTrue((bool)afterHireCapability?.GetType().GetProperty("Staffed")?.GetValue(afterHireCapability),
                        "Trading capability must refresh after a real clerk is assigned.");
                }
                string instanceId = formedBusiness.GetType().GetProperty("InstanceId")?.GetValue(formedBusiness) as string;
                created.Add(instanceId);
            }

            Assert.AreEqual(3, created.Count);
            Assert.IsTrue(created.TrueForAll(id => !string.IsNullOrWhiteSpace(id)),
                "Every formed business must have a persistent instance id.");

            Type storeRuntimeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
            Component storeRuntime = FindComponent(storeRuntimeType);
            Assert.NotNull(storeRuntime, "The scene must own the real General Store runtime.");
            object currentStore = storeRuntimeType.GetProperty("CurrentBusiness")?.GetValue(storeRuntime);
            Assert.NotNull(currentStore, "The formed General Store must be bound to its operating runtime.");
            Assert.AreEqual(created[0], currentStore.GetType().GetProperty("InstanceId")?.GetValue(currentStore));
        }

        [UnityTest]
        public IEnumerator OpeningWorldContainsOwnedHorseAndWagonAuthorities()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone) yield return null;
            yield return null;

            Type hubType = RuntimeType("LandLedgers.Orchestration.Systems.SimulationSystemsHub");
            Component hub = FindComponent(hubType);
            Assert.NotNull(hub, "The opening world must own the shared systems hub.");

            object animals = hubType.GetProperty("Animals")?.GetValue(hub);
            Assert.NotNull(animals, "Opening animals must use the shared AnimalRegistry.");
            object activeAnimals = animals.GetType().GetProperty("ActiveAnimals")?.GetValue(animals);
            bool foundHorse = false;
            foreach (object animal in (System.Collections.IEnumerable)activeAnimals)
            {
                object species = animal?.GetType().GetProperty("Species")?.GetValue(animal)
                    ?? animal?.GetType().GetField("Species")?.GetValue(animal);
                if (species?.ToString() == "Horse")
                {
                    foundHorse = true;
                    break;
                }
            }
            Assert.IsTrue(foundHorse, "A fresh authored opening must contain a real Horse AnimalState.");

            object transport = hubType.GetProperty("TransportAssets")?.GetValue(hub);
            object wagons = transport?.GetType().GetProperty("Assets")?.GetValue(transport);
            Assert.NotNull(wagons, "Opening transport must use the persistent transport asset registry.");
            Assert.Greater(((System.Collections.ICollection)wagons).Count, 0,
                "A fresh authored opening must contain a real Wagon EquipmentAsset.");
        }

        [UnityTest]
        public IEnumerator ProductionWeeklyPassRegistersFarmFieldAndRespectsWinter()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            Assert.NotNull(sharedRuntime, "The playable scene must own the shared business authority.");

            object farmType = Enum.Parse(businessType, "CropFarm");
            object freightType = Enum.Parse(businessType, "LiveryFreight");
            object storeType = Enum.Parse(businessType, "GeneralStore");
            object farm = FindExistingBusiness(sharedRuntime, sharedRuntimeType, farmType);
            if (farm == null)
            {
                // The compact Main Scene intentionally opens with three different
                // NPC businesses and no CropFarm. Form the farm through the same
                // production authority used by gameplay, then establish compatible
                // premises through its typed agricultural-site selection.
                farm = CreateBusiness(sharedRuntime, sharedRuntimeType, farmType, "Route Farm");
                int assignedBuildingId = (int)(farm.GetType().GetProperty("AssignedBuildingId")?.GetValue(farm) ?? -1);
                if (assignedBuildingId < 0)
                {
                    object[] farmPremisesArguments = { farm, null };
                    bool farmPremisesAssigned = (bool)sharedRuntimeType.GetMethod("TryAssignPlayerBusinessPremises")
                        .Invoke(sharedRuntime, farmPremisesArguments);
                    Assert.IsTrue(farmPremisesAssigned,
                        $"A legitimately formed farm must claim compatible available agricultural premises: {farmPremisesArguments[1]}");
                }
            }
            object freight = CreateBusiness(sharedRuntime, sharedRuntimeType, freightType, "Route Freight");
            object store = CreateBusiness(sharedRuntime, sharedRuntimeType, storeType, "Route Store");
            Assert.NotNull(farm);
            Assert.NotNull(freight);
            Assert.NotNull(store);
            object runtimeState = farm.GetType().GetProperty("RuntimeState")?.GetValue(farm);
            Assert.NotNull(runtimeState, "The farm must expose its authoritative runtime state.");
            object cropStock = runtimeState.GetType().GetMethod("GetCategoryStock")?.Invoke(runtimeState, new object[] { "crop_food" });
            Assert.NotNull(cropStock, "Farm production must use the authored crop output category.");
            Assert.AreEqual(0, (int)cropStock.GetType().GetProperty("CurrentStockUnits")?.GetValue(cropStock),
                "A newly formed farm must not begin with unproven crop output before planting or harvest.");
            object[] storePremisesArguments = { store, null };
            bool storePremisesAssigned = (bool)sharedRuntimeType.GetMethod("TryAssignPlayerBusinessPremises")
                .Invoke(sharedRuntime, storePremisesArguments);
            Assert.IsTrue(storePremisesAssigned, $"The formed store must establish compatible premises before receiving goods: {storePremisesArguments[1]}");

            object[] hireArguments = { freight, 0, null };
            bool hired = (bool)sharedRuntimeType.GetMethod("TryAssignCandidateToOpenSlot")
                .Invoke(sharedRuntime, hireArguments);
            Assert.IsTrue(hired, $"The route must hire a real freight worker through the production staffing authority: {hireArguments[2]}");

            Type storeRuntimeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
            Component storeRuntime = FindComponent(storeRuntimeType);
            object[] bindArguments = { store, null };
            bool bound = (bool)storeRuntimeType.GetMethod("TryBindFormedPlayerBusiness")
                .Invoke(storeRuntime, bindArguments);
            Assert.IsTrue(bound, $"The formed store must bind before commerce: {bindArguments[1]}");

            sharedRuntimeType.GetMethod("ResolveWeeklySharedOperations")?.Invoke(sharedRuntime, null);

            int farmOutput = (int)cropStock.GetType().GetProperty("CurrentStockUnits")?.GetValue(cropStock);
            Assert.AreEqual(0, farmOutput,
                "The January opening must not create crop inventory before a valid planting and harvest cycle.");

            Type hubType = RuntimeType("LandLedgers.Orchestration.Systems.SimulationSystemsHub");
            Component hub = FindComponent(hubType);
            Assert.NotNull(hub, "The playable scene must own the authoritative systems hub.");
            object farmSlice = hubType.GetProperty("FarmSlice")?.GetValue(hub);
            object crops = farmSlice?.GetType().GetProperty("Crops")?.GetValue(farmSlice);
            object fields = crops?.GetType().GetProperty("Fields")?.GetValue(crops);
            object allFields = fields?.GetType().GetMethod("AllFields")?.Invoke(fields, null);
            object registeredField = null;
            foreach (object candidate in (System.Collections.IEnumerable)allFields)
            {
                string candidateFarmId = candidate.GetType().GetField("FarmId")?.GetValue(candidate) as string;
                if (string.Equals(candidateFarmId,
                    farm.GetType().GetProperty("InstanceId")?.GetValue(farm) as string,
                    StringComparison.OrdinalIgnoreCase))
                {
                    registeredField = candidate;
                    break;
                }
            }
            Assert.NotNull(registeredField,
                "A formed farm with legitimate agricultural premises must register a persistent CropFieldState.");
            Assert.Greater((float)registeredField.GetType().GetField("Acres")?.GetValue(registeredField), 0f);
            Assert.IsFalse(string.IsNullOrWhiteSpace(
                registeredField.GetType().GetField("LinkedPlotName")?.GetValue(registeredField) as string));
            string farmId = farm.GetType().GetProperty("InstanceId")?.GetValue(farm) as string;
            string fieldId = registeredField.GetType().GetField("FieldId")?.GetValue(registeredField) as string;
            Assert.IsFalse(string.IsNullOrWhiteSpace(fieldId), "The registered field must have a stable identity.");
            Assert.AreEqual(farmId, registeredField.GetType().GetField("FarmId")?.GetValue(registeredField) as string,
                "The field must resolve to the formed farm identity.");
            Assert.AreEqual(farmId, registeredField.GetType().GetField("OwnerFarmId")?.GetValue(registeredField) as string,
                "The field ownership provenance must identify the same farm.");

            float registeredAcres = (float)registeredField.GetType().GetField("Acres")?.GetValue(registeredField);
            string linkedPlot = registeredField.GetType().GetField("LinkedPlotName")?.GetValue(registeredField) as string;
            sharedRuntimeType.GetMethod("ResolveWeeklySharedOperations")?.Invoke(sharedRuntime, null);
            object fieldAfterRebind = null;
            int farmFieldCount = 0;
            foreach (object candidate in (System.Collections.IEnumerable)fields.GetType().GetMethod("AllFields").Invoke(fields, null))
            {
                string candidateFarmId = candidate.GetType().GetField("FarmId")?.GetValue(candidate) as string;
                if (!string.Equals(candidateFarmId, farmId, StringComparison.OrdinalIgnoreCase)) continue;
                farmFieldCount++;
                fieldAfterRebind = candidate;
            }
            Assert.AreEqual(1, farmFieldCount,
                "Repeated production-driver initialization must not register another field for the farm.");
            Assert.NotNull(fieldAfterRebind, "The farm field must remain registered after the second weekly drive.");
            Assert.AreEqual(fieldId, fieldAfterRebind?.GetType().GetField("FieldId")?.GetValue(fieldAfterRebind) as string,
                "The field identity must remain stable across production-driver rebinds.");
            Assert.AreEqual(registeredAcres, (float)fieldAfterRebind?.GetType().GetField("Acres")?.GetValue(fieldAfterRebind),
                "Repeated registration must not rewrite field area.");
            Assert.AreEqual(linkedPlot, fieldAfterRebind?.GetType().GetField("LinkedPlotName")?.GetValue(fieldAfterRebind) as string,
                "The field must remain linked to the same agricultural plot.");
            Debug.Log($"[FarmWinterVerification] farm={farmId}; building={farm.GetType().GetProperty("AssignedBuildingId")?.GetValue(farm)}; field={fieldId}; plot={linkedPlot}; acres={registeredAcres:0.###}; registrations={farmFieldCount}; cropUnits={farmOutput}; January harvest prohibited.");

            Type logisticsType = RuntimeType("LandLedgers.Economy.LogisticsRuntimeManager");
            Component logistics = FindComponent(logisticsType);
            Assert.NotNull(logistics, "The playable scene must own the real logistics authority.");
            object shipments = logisticsType.GetProperty("Shipments")?.GetValue(logistics);
            Assert.NotNull(shipments, "The logistics authority must expose scheduled physical shipments.");
            string storeId = store.GetType().GetProperty("InstanceId")?.GetValue(store) as string;
            object shipment = null;
            foreach (object candidate in (System.Collections.IEnumerable)shipments)
            {
                string sourceId = candidate.GetType().GetField("sourceBusinessInstanceId")?.GetValue(candidate) as string;
                string destinationId = candidate.GetType().GetField("destinationBusinessInstanceId")?.GetValue(candidate) as string;
                if (string.Equals(sourceId, farmId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(destinationId, storeId, StringComparison.OrdinalIgnoreCase))
                {
                    shipment = candidate;
                    break;
                }
            }
            Assert.IsNull(shipment,
                "No farm shipment may be created before real crop output and a legitimate buyer order exist.");
        }

        [UnityTest]
        public IEnumerator GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            Component storeRuntime = FindComponent(RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager"));
            Assert.NotNull(sharedRuntime);
            Assert.NotNull(storeRuntime);

            object[] createArguments = { Enum.Parse(businessType, "GeneralStore"), "Customer Ledger Store", null, null };
            Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryCreatePlayerBusiness")
                .Invoke(sharedRuntime, createArguments), createArguments[3] as string);

            // Business formation alone does not fund a store or create saleable
            // inventory. Use the same owner-to-business capital transfer and
            // ordinary weekly procurement callback as the production-path test.
            Type portfolioType = RuntimeType("LandLedgers.Economy.PlayerPortfolioManager");
            Component portfolio = FindComponent(portfolioType);
            Assert.NotNull(portfolio, "A player-funded store requires the authoritative portfolio manager.");
            object[] fundingArguments = { createArguments[2], 175000, 0, null };
            Assert.IsTrue((bool)portfolioType.GetMethod("TryTransferOwnerBusinessCash")
                .Invoke(portfolio, fundingArguments), fundingArguments[3] as string);

            object[] premisesArguments = { createArguments[2], null };
            Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignPlayerBusinessPremises")
                .Invoke(sharedRuntime, premisesArguments), premisesArguments[1] as string);

            object[] hireArguments = { createArguments[2], 0, null };
            Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignCandidateToOpenSlot")
                .Invoke(sharedRuntime, hireArguments), hireArguments[2] as string);

            Type storeRuntimeType = storeRuntime.GetType();
            object[] bindArguments = { createArguments[2], null };
            Assert.IsTrue((bool)storeRuntimeType.GetMethod("TryBindFormedPlayerBusiness")
                .Invoke(storeRuntime, bindArguments), bindArguments[1] as string);

            sharedRuntimeType.GetMethod("ResolveWeeklySharedOperations")?.Invoke(sharedRuntime, null);
            object formedBusiness = createArguments[2];
            object runtimeState = formedBusiness.GetType().GetProperty("RuntimeState")?.GetValue(formedBusiness);
            Assert.NotNull(runtimeState);
            object stockedCategory = runtimeState.GetType().GetMethod("GetCategoryStock")
                ?.Invoke(runtimeState, new object[] { "staple_food" });
            Assert.NotNull(stockedCategory, "The store's authored staple inventory category must exist.");
            Assert.Greater((int)stockedCategory.GetType().GetProperty("CurrentStockUnits")?.GetValue(stockedCategory), 0,
                "The positive sale case must first acquire real stock through the ordinary procurement path.");

            storeRuntimeType.GetMethod("ResolveDailySales")?.Invoke(storeRuntime, null);

            int customers = (int)storeRuntimeType.GetProperty("LastDailyCustomerHouseholds")?.GetValue(storeRuntime);
            Assert.Greater(customers, 0, "A stocked operating store must serve an authored household customer.");
            Assert.GreaterOrEqual((int)storeRuntimeType.GetProperty("LastCustomerHouseholdId")?.GetValue(storeRuntime), 0);
            Assert.GreaterOrEqual((int)storeRuntimeType.GetProperty("LastCustomerPersonId")?.GetValue(storeRuntime), 0,
                "A household sale must retain the real acting adult Person, not only an anonymous household count.");
            Assert.Greater((int)storeRuntimeType.GetProperty("LastCustomerSaleUnits")?.GetValue(storeRuntime), 0);
            Assert.Greater((int)storeRuntimeType.GetProperty("LastCustomerSaleRevenueCents")?.GetValue(storeRuntime), 0);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator MainSceneFirstLedgerRunsThirtyDaysThroughAuthoritativeDrivers()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone) yield return null;
            yield return null;
            yield return null;

            Type startPanelType = RuntimeType("LandLedgers.FirstLedger.ScenarioStartPanelController");
            Component startPanel = FindComponent(startPanelType);
            Assert.NotNull(startPanel, "A normal New Game must start from the authored scenario selector.");
            startPanelType.GetMethod("StartScenario", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(startPanel, null);

            Type timeType = RuntimeType("LandLedgers.Time.TimeManager");
            Type populationType = RuntimeType("LandLedgers.Population.PopulationManager");
            Type sharedType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Type hubType = RuntimeType("LandLedgers.Orchestration.Systems.SimulationSystemsHub");
            Type harnessType = RuntimeType("LandLedgers.Orchestration.Scenarios.Development.ScenarioValidationHarness");
            Component time = FindComponent(timeType);
            Component populationManager = FindComponent(populationType);
            Component shared = FindComponent(sharedType);
            Component hub = FindComponent(hubType);
            Component harness = FindComponent(harnessType);
            Assert.NotNull(time, "Main Scene must use its authoritative TimeManager.");
            Assert.NotNull(populationManager, "Main Scene must use its authoritative PopulationManager.");
            Assert.NotNull(shared, "Main Scene must use its authoritative Business registry.");
            Assert.NotNull(hub, "Main Scene must use its SystemsHub.");
            Assert.NotNull(harness, "The scene's validation control must drive the production clock, not a second simulation.");
            Assert.IsFalse((bool)timeType.GetProperty("IsPaused")?.GetValue(time),
                "Starting the scenario must resume the ordinary game clock.");

            object populationState = populationType.GetProperty("State")?.GetValue(populationManager);
            System.Collections.IList people = populationState?.GetType().GetField("people")?.GetValue(populationState) as System.Collections.IList;
            System.Collections.IEnumerable businesses = sharedType.GetProperty("Businesses")?.GetValue(shared) as System.Collections.IEnumerable;
            object employments = hubType.GetProperty("Employments")?.GetValue(hub);
            MethodInfo activeByEmployee = employments?.GetType().GetMethod("GetActiveByEmployee");
            Assert.NotNull(people);
            Assert.NotNull(businesses);
            Assert.NotNull(activeByEmployee, "The active employment registry must be the authority for workplace claims.");
            bool sixPersonDevelopmentProfile = people.Cast<object>()
                .Count(person => !(bool)person.GetType().GetField("isOffMap")?.GetValue(person)) == 6;
            if (sixPersonDevelopmentProfile)
            {
                System.Collections.ICollection households = populationState?.GetType().GetField("households")?.GetValue(populationState) as System.Collections.ICollection;
                Assert.AreEqual(3, households?.Count, "The isolated six-person run starts with exactly three real Households.");
                Assert.AreEqual(3, businesses.Cast<object>().Count(), "The isolated profile starts with exactly three Businesses.");
                CollectionAssert.AreEquivalent(
                    new[] { "GeneralStore", "Bakery", "FuelDealer", "LiveryFreight" },
                    businesses.Cast<object>().Select(business => business.GetType().GetProperty("BusinessType")?.GetValue(business)?.ToString()).ToArray());
            }
            int formalWorkplaces = 0;
            foreach (object person in people)
            {
                int buildingId = (int)person.GetType().GetField("workplaceBuildingId").GetValue(person);
                if (buildingId < 0) continue;
                int personId = (int)person.GetType().GetField("id").GetValue(person);
                object personEmployments = activeByEmployee.Invoke(employments, new object[] { personId });
                Assert.NotNull(personEmployments);
                bool matched = false;
                foreach (object relationship in (System.Collections.IEnumerable)personEmployments)
                {
                    string employerId = relationship.GetType().GetField("EmployerBusinessId")?.GetValue(relationship) as string;
                    foreach (object business in businesses)
                    {
                        if (business == null) continue;
                        int assignedBuilding = (int)(business.GetType().GetProperty("AssignedBuildingId")?.GetValue(business) ?? -1);
                        string businessId = business.GetType().GetProperty("InstanceId")?.GetValue(business) as string;
                        if (assignedBuilding == buildingId && string.Equals(employerId, businessId, StringComparison.Ordinal))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (matched) break;
                }
                bool namedOwnerOperator = !matched && businesses.Cast<object>()
                    .Any(business => IsNamedOwnerOperatorAt(business, personId, buildingId));
                Assert.IsTrue(matched || namedOwnerOperator,
                    $"Person {personId}'s workplace {buildingId} must resolve to an active EmploymentRelationship or an explicitly filled, non-wage owner-operator slot on that Business.");
                formalWorkplaces++;
            }
            Debug.Log($"[WorkplaceReconciliation] {formalWorkplaces} generated workplace claims resolved to active contracts; " +
                      "unmatched generation hints must be cleared rather than treated as employment.");

            int startDay = (int)timeType.GetProperty("CurrentAbsoluteDayIndex")?.GetValue(time);
            object currentDate = timeType.GetProperty("CurrentDate")?.GetValue(time);
            Component singletonClock = timeType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null) as Component;
            object singletonDate = singletonClock != null
                ? timeType.GetProperty("CurrentDate")?.GetValue(singletonClock)
                : null;
            object clockDto = timeType.GetMethod("CaptureSaveDto")?.Invoke(time, null);
            object calendarOffset = clockDto?.GetType().GetField("calendarDayOffset")?.GetValue(clockDto);
            Debug.Log($"[Pass40 ClockDiagnostic] selected={time.name}#{time.GetInstanceID()} date={currentDate}; " +
                      $"singleton={(singletonClock != null ? singletonClock.name + "#" + singletonClock.GetInstanceID() : "missing")}; " +
                      $"singletonDate={singletonDate}; calendarOffset={calendarOffset}; dayIndex={startDay}; " +
                      $"allClockCount={Resources.FindObjectsOfTypeAll(timeType).Length}");
            Assert.AreEqual(1870, currentDate.GetType().GetProperty("Year")?.GetValue(currentDate),
                "First Ledger's live clock must use the authored January 1870 opening date.");
            string opening = CaptureOrdinaryMainSceneSnapshot(populationManager, populationType, shared, sharedType, hub, hubType);
            Debug.Log($"[Pass22 RUN-004] opening day={startDay}; {opening}");

            MethodInfo advanceHours = harnessType.GetMethod("AdvanceHours");
            Assert.NotNull(advanceHours, "The harness must delegate accelerated validation to TimeManager.AdvanceForValidation.");
            var birthdayDispatchCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            Application.LogCallback observeBirthdayDispatch = (message, stackTrace, type) =>
            {
                if (type != LogType.Warning || string.IsNullOrEmpty(message)) return;
                foreach (Match match in Regex.Matches(
                    message,
                    @"BirthdayService: P(?<person>\d+).*? on (?<date>\d{4}-\d{2}-\d{2})"))
                {
                    string key = match.Groups["person"].Value + "|" + match.Groups["date"].Value;
                    birthdayDispatchCounts[key] = birthdayDispatchCounts.TryGetValue(key, out int count) ? count + 1 : 1;
                }
            };
            Application.logMessageReceived += observeBirthdayDispatch;
            try
            {
                advanceHours.Invoke(harness, new object[] { 24 * 30 });
                yield return null;
            }
            finally
            {
                Application.logMessageReceived -= observeBirthdayDispatch;
            }

            int endDay = (int)timeType.GetProperty("CurrentAbsoluteDayIndex")?.GetValue(time);
            string closing = CaptureOrdinaryMainSceneSnapshot(populationManager, populationType, shared, sharedType, hub, hubType);
            Debug.Log($"[Pass22 RUN-004] closing day={endDay}; {closing}");
            Assert.AreEqual(startDay + 30, endDay,
                "Thirty consecutive day boundaries must reach the ordinary daily/weekly/monthly production callbacks.");

            object errandCoordinator = hubType.GetProperty("HouseholdErrands")?.GetValue(hub);
            object errandPlans = errandCoordinator?.GetType().GetProperty("Plans")?.GetValue(errandCoordinator);
            System.Collections.IEnumerable retainedPlans = errandPlans?.GetType().GetProperty("All")?.GetValue(errandPlans)
                as System.Collections.IEnumerable;
            Assert.NotNull(retainedPlans, "The household errand authority must remain inspectable after the run.");
            int retainedPlanCount = 0;
            foreach (object plan in retainedPlans)
            {
                if (plan == null) continue;
                retainedPlanCount++;
                string status = plan.GetType().GetProperty("Status")?.GetValue(plan)?.ToString();
                Assert.Contains(status, new[] { "Proposed", "Assigned" },
                    "Only resumable household plans may remain after terminal errands are compacted.");
            }
            System.Collections.ICollection householdCount = populationState?.GetType().GetField("households")?.GetValue(populationState)
                as System.Collections.ICollection;
            Assert.LessOrEqual(retainedPlanCount, householdCount?.Count ?? 0,
                "A 30-day run must not retain one completed/failed orchestration record per household per day.");

            object generalStore = businesses.Cast<object>().FirstOrDefault(business =>
                string.Equals(business.GetType().GetProperty("BusinessType")?.GetValue(business)?.ToString(), "GeneralStore", StringComparison.Ordinal));
            Assert.NotNull(generalStore, "The scenario settlement must retain its real General Store Business.");
            int generalStoreBuildingId = (int)(generalStore.GetType().GetProperty("AssignedBuildingId")?.GetValue(generalStore) ?? -1);
            string generalStoreBusinessId = generalStore.GetType().GetProperty("InstanceId")?.GetValue(generalStore) as string;
            object postal = hubType.GetProperty("Postal")?.GetValue(hub);
            System.Collections.IEnumerable offices = postal?.GetType().GetProperty("Offices")?.GetValue(postal) as System.Collections.IEnumerable;
            object generalStoreOffice = offices?.Cast<object>().FirstOrDefault(office =>
                string.Equals(office.GetType().GetField("OfficeId")?.GetValue(office) as string, "general-store", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(generalStoreOffice, "The Main Scene must register the actual General Store as the town post office.");
            Assert.AreEqual(generalStoreBusinessId, generalStoreOffice.GetType().GetField("BusinessInstanceId")?.GetValue(generalStoreOffice));
            Assert.AreEqual($"building:{generalStoreBuildingId}", generalStoreOffice.GetType().GetField("LocationId")?.GetValue(generalStoreOffice),
                "The postal origin must resolve to the General Store's real journey node.");

            if (sixPersonDevelopmentProfile)
            {
                object closingPopulation = populationType.GetProperty("State")?.GetValue(populationManager);
                System.Collections.IList closingPeople = closingPopulation?.GetType().GetField("people")?.GetValue(closingPopulation) as System.Collections.IList;
                System.Collections.ICollection closingHouseholds = closingPopulation?.GetType().GetField("households")?.GetValue(closingPopulation) as System.Collections.ICollection;
                Assert.AreEqual(6, closingPeople?.Cast<object>()
                    .Count(person => !(bool)person.GetType().GetField("isOffMap")?.GetValue(person)),
                    "The six authoritative on-map Person records remain present after 30 days.");
                Assert.GreaterOrEqual(closingHouseholds?.Count ?? 0, 3,
                    "The three opening Households remain represented after 30 days; legitimate demographic events may form or dissolve households.");
                Assert.IsTrue((bool)(populationType.GetProperty("SuppressMigrantArrivals")?.GetValue(populationManager) ?? false),
                    "The six-person development world must retain its no-arrival gate for the whole run.");
                CollectionAssert.AreEquivalent(
                    new[] { "GeneralStore", "Bakery", "FuelDealer", "LiveryFreight" },
                    (sharedType.GetProperty("Businesses")?.GetValue(shared) as System.Collections.IEnumerable)
                    ?.Cast<object>().Select(business => business.GetType().GetProperty("BusinessType")?.GetValue(business)?.ToString()).ToArray());
            }
            Assert.IsFalse((bool)(FindComponent(RuntimeType("LandLedgers.Orchestration.Scenarios.FirstLedger.FirstLedgerBootstrap"))
                ?.GetType().GetProperty("IsScenarioComplete")?.GetValue(
                    FindComponent(RuntimeType("LandLedgers.Orchestration.Scenarios.FirstLedger.FirstLedgerBootstrap"))) ?? false),
                "A no-action 30-day run must not award First Ledger Victory without its real conditions.");
            Debug.Log($"[Pass22 RUN-004] birthday events observed in this seeded 30-day interval={birthdayDispatchCounts.Count}; " +
                      "dedicated birthday chronology tests cover due-date dispatch when this cohort has no birthday in range.");
            foreach (KeyValuePair<string, int> dispatch in birthdayDispatchCounts)
            {
                Assert.LessOrEqual(dispatch.Value, 1,
                    $"A birthday due event must dispatch once per Person and calendar date ({dispatch.Key}).");
            }
        }

        private static string CaptureOrdinaryMainSceneSnapshot(
            Component populationManager,
            Type populationType,
            Component sharedRuntime,
            Type sharedType,
            Component hub,
            Type hubType)
        {
            object population = populationType.GetProperty("State")?.GetValue(populationManager);
            var people = population?.GetType().GetField("people")?.GetValue(population) as System.Collections.IList;
            var households = population?.GetType().GetField("households")?.GetValue(population) as System.Collections.IList;
            Assert.NotNull(people);
            Assert.NotNull(households);

            var personIds = new HashSet<int>();
            int workplaceAssignments = 0;
            for (int i = 0; i < people.Count; i++)
            {
                object person = people[i];
                int id = (int)person.GetType().GetField("id")?.GetValue(person);
                Assert.IsTrue(personIds.Add(id), $"The live population contains duplicate Person ID {id}.");
                int workplace = (int)person.GetType().GetField("workplaceBuildingId")?.GetValue(person);
                if (workplace >= 0) workplaceAssignments++;
            }

            var householdIds = new HashSet<int>();
            for (int i = 0; i < households.Count; i++)
            {
                object household = households[i];
                int id = (int)household.GetType().GetField("id")?.GetValue(household);
                Assert.IsTrue(householdIds.Add(id), $"The live population contains duplicate Household ID {id}.");
            }

            object membershipRegistry = populationManager.GetType().GetProperty("Memberships")
                ?.GetValue(populationManager);
            Assert.NotNull(membershipRegistry,
                "The Main Scene must use the PopulationManager's authoritative household-membership registry.");
            MethodInfo activeHouseholdFor = membershipRegistry.GetType().GetMethod("GetActiveHouseholdId");
            Assert.NotNull(activeHouseholdFor,
                "Household membership must be resolved through the authoritative registry.");
            int unregisteredPeople = 0;
            for (int i = 0; i < people.Count; i++)
            {
                object person = people[i];
                if ((bool)person.GetType().GetField("isOffMap")?.GetValue(person)) continue;
                int personId = (int)person.GetType().GetField("id")?.GetValue(person);
                int householdId = (int)activeHouseholdFor.Invoke(membershipRegistry, new object[] { personId });
                if (householdId < 0 || !householdIds.Contains(householdId)) unregisteredPeople++;
            }
            Assert.AreEqual(0, unregisteredPeople,
                "Every on-map Person in the Main Scene must belong to one existing Household through the authoritative registry; persistent off-map relatives are not residents.");

            var businesses = sharedType.GetProperty("Businesses")?.GetValue(sharedRuntime) as System.Collections.IEnumerable;
            Assert.NotNull(businesses);
            int businessCount = 0;
            long businessCashCents = 0;
            long businessCategoryStockUnits = 0;
            var businessCashDetails = new List<string>();
            foreach (object business in businesses)
            {
                if (business == null) continue;
                businessCount++;
                object state = business.GetType().GetProperty("RuntimeState")?.GetValue(business);
                if (state == null) continue;
                Type stateType = state.GetType();
                int currentCash = (int)(stateType.GetProperty("CurrentCashCents")?.GetValue(state) ?? 0);
                businessCashCents += currentCash;
                string instanceId = business.GetType().GetProperty("InstanceId")?.GetValue(business)?.ToString() ?? "?";
                string displayName = business.GetType().GetProperty("RuntimeDisplayName")?.GetValue(business)?.ToString() ?? "?";
                string typeName = business.GetType().GetProperty("BusinessType")?.GetValue(business)?.ToString() ?? "?";
                businessCashDetails.Add(
                    $"{typeName}:{displayName}[{instanceId}] cash={currentCash}c " +
                    $"lastWeek={stateType.GetProperty("LastWeeklyCashBeforeCents")?.GetValue(state) ?? 0}->" +
                    $"{stateType.GetProperty("LastWeeklyCashAfterCents")?.GetValue(state) ?? 0}c " +
                    $"sales={stateType.GetProperty("WeekToDateRevenueCents")?.GetValue(state) ?? 0}c " +
                    $"payroll={stateType.GetProperty("LastWeeklyPayrollCents")?.GetValue(state) ?? 0}c " +
                    $"inputSpend={stateType.GetProperty("LastWeeklyInputProcurementSpendCents")?.GetValue(state) ?? 0}c " +
                    $"transfer=+{stateType.GetProperty("LastWeeklyLocalTransferRevenueCents")?.GetValue(state) ?? 0}/-" +
                    $"{stateType.GetProperty("LastWeeklyLocalTransferCostCents")?.GetValue(state) ?? 0}c " +
                    $"operation='{stateType.GetProperty("LastWeeklyOperationSummary")?.GetValue(state) ?? string.Empty}' " +
                    $"transferNote='{stateType.GetProperty("LastWeeklyTransferSummary")?.GetValue(state) ?? string.Empty}'");
                var stocks = state.GetType().GetProperty("CategoryStock")?.GetValue(state) as System.Collections.IEnumerable;
                if (stocks == null) continue;
                foreach (object stock in stocks)
                    businessCategoryStockUnits += Math.Max(0, (int)stock.GetType().GetProperty("CurrentStockUnits")?.GetValue(stock));
            }

            object systems = hubType.GetProperty("Tasks")?.GetValue(hub);
            object taskSave = systems?.GetType().GetMethod("CaptureSaveDto")?.Invoke(systems, null);
            var tasks = taskSave?.GetType().GetField("tasks")?.GetValue(taskSave) as System.Collections.ICollection;
            Assert.NotNull(tasks, "TaskAuthority must expose its persisted work history for the run snapshot.");
            int completedTasks = 0;
            int openTasks = 0;
            foreach (object task in tasks)
            {
                string state = task?.GetType().GetProperty("Status")?.GetValue(task)?.ToString();
                if (string.Equals(state, "Complete", StringComparison.Ordinal)) completedTasks++;
                if (string.Equals(state, "Queued", StringComparison.Ordinal)
                    || string.Equals(state, "Assigned", StringComparison.Ordinal)
                    || string.Equals(state, "InProgress", StringComparison.Ordinal)
                    || string.Equals(state, "ResourcesReserved", StringComparison.Ordinal)) openTasks++;
            }

            object ledgerRegistry = hubType.GetProperty("HouseholdLedgers")?.GetValue(hub);
            Type ledgerStateType = RuntimeType("LandLedgers.Population.HouseholdLedgerState");
            Type ledgerStateListType = typeof(List<>).MakeGenericType(ledgerStateType);
            var ledgerStates = Activator.CreateInstance(ledgerStateListType) as System.Collections.IList;
            ledgerRegistry?.GetType().GetMethod("ExportState")?.Invoke(ledgerRegistry, new object[] { ledgerStates });
            long householdLedgerCashCents = 0;
            var householdBalances = new List<string>();
            foreach (object ledger in ledgerStates)
            {
                var entries = ledger?.GetType().GetField("Entries")?.GetValue(ledger) as System.Collections.IList;
                if (entries == null) continue;
                int householdId = (int)(ledger.GetType().GetField("HouseholdId")?.GetValue(ledger) ?? -1);
                int balance = entries.Count > 0
                    ? (int)(entries[entries.Count - 1].GetType().GetField("BalanceAfterCents")?.GetValue(entries[entries.Count - 1]) ?? 0)
                    : 0;
                householdLedgerCashCents += balance;
                householdBalances.Add($"H{householdId}={balance}c");
            }

            return $"Persons={people.Count} (unique={personIds.Count}), Households={households.Count} (unique={householdIds.Count}), " +
                   $"unregisteredPersons={unregisteredPeople}, " +
                   $"Businesses={businessCount}, businessCash={businessCashCents}c [{string.Join(" | ", businessCashDetails)}], " +
                   $"householdCashBalance={householdLedgerCashCents}c [{string.Join(" | ", householdBalances)}], " +
                   $"businessCategoryStock={businessCategoryStockUnits}u, Tasks={tasks.Count} (complete={completedTasks}, open={openTasks}), " +
                   $"workplaceAssignments={workplaceAssignments}, EmploymentRegistry={sharedType.GetProperty("EmploymentRegistryCount")?.GetValue(sharedRuntime)}";
        }

        private static bool IsNamedOwnerOperatorAt(object business, int personId, int workplaceBuildingId)
        {
            if (business == null
                || (int)(business.GetType().GetProperty("AssignedBuildingId")?.GetValue(business) ?? -1) != workplaceBuildingId)
            {
                return false;
            }

            object owner = business.GetType().GetProperty("Owner")?.GetValue(business);
            if (owner == null || (int)(owner.GetType().GetProperty("PersonId")?.GetValue(owner) ?? -1) != personId)
            {
                return false;
            }

            object runtime = business.GetType().GetProperty("RuntimeState")?.GetValue(business);
            System.Collections.IEnumerable slots = runtime?.GetType().GetProperty("WorkerSlots")?.GetValue(runtime) as System.Collections.IEnumerable;
            if (slots == null) return false;
            string expectedSlot = $"owner:{personId}";
            foreach (object slot in slots)
            {
                if (slot == null) continue;
                bool filled = (bool)(slot.GetType().GetProperty("IsFilled")?.GetValue(slot) ?? false);
                string assigned = slot.GetType().GetProperty("AssignedWorkerId")?.GetValue(slot) as string;
                if (filled && string.Equals(assigned, expectedSlot, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // Save/load intentionally runs last: it restores a full campaign snapshot and
        // therefore must not leave a loaded-world lifecycle for unrelated startup tests.
        [UnityTest, Order(100000), Timeout(900000)]
        public IEnumerator CampaignSaveRoundTripPreservesProductionBusinessIdentity()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type saveLoadType = RuntimeType("LandLedgers.Persistence.SaveLoadManager");
            Component saveLoad = FindComponent(saveLoadType);
            Assert.NotNull(saveLoad, "The playable scene must own the production save/load authority.");
            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            object businesses = sharedRuntimeType.GetProperty("Businesses")?.GetValue(sharedRuntime);
            var idsBefore = new List<string>();
            foreach (object business in (System.Collections.IEnumerable)businesses)
            {
                string id = business?.GetType().GetProperty("InstanceId")?.GetValue(business) as string;
                if (!string.IsNullOrWhiteSpace(id)) idsBefore.Add(id);
            }
            Component storeRuntime = FindComponent(RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager"));
            object currentStore = storeRuntime?.GetType().GetProperty("CurrentBusiness")?.GetValue(storeRuntime);
            string currentStoreId = currentStore?.GetType().GetProperty("InstanceId")?.GetValue(currentStore) as string;
            if (!string.IsNullOrWhiteSpace(currentStoreId) && !idsBefore.Contains(currentStoreId)) idsBefore.Add(currentStoreId);
            Assert.IsNotEmpty(idsBefore, "The authored opening state must contain a real business before saving.");

            string path = saveLoadType.GetProperty("DefaultSlotPath")?.GetValue(saveLoad) as string;
            string backupPath = path + ".bak";
            string tempPath = Path.Combine(Application.temporaryCachePath, "land-ledgers-save-roundtrip-backup.json");
            string tempBackupPath = tempPath + ".bak";
            bool hadSave = File.Exists(path);
            bool hadBackup = File.Exists(backupPath);
            if (hadSave) File.Copy(path, tempPath, true);
            if (hadBackup) File.Copy(backupPath, tempBackupPath, true);

            try
            {
                object[] saveArgs = { null };
                bool saved = (bool)saveLoadType.GetMethod("SaveDefaultSlot")?.Invoke(saveLoad, saveArgs);
                Assert.IsTrue(saved, saveArgs[0] as string);
                object[] loadArgs = { null };
                bool loaded = (bool)saveLoadType.GetMethod("LoadDefaultSlot")?.Invoke(saveLoad, loadArgs);
                Assert.IsTrue(loaded, loadArgs[0] as string);
                yield return null;

                sharedRuntime = FindComponent(sharedRuntimeType);
                businesses = sharedRuntimeType.GetProperty("Businesses")?.GetValue(sharedRuntime);
                var idsAfter = new HashSet<string>();
                foreach (object business in (System.Collections.IEnumerable)businesses)
                {
                    string id = business?.GetType().GetProperty("InstanceId")?.GetValue(business) as string;
                    if (!string.IsNullOrWhiteSpace(id)) idsAfter.Add(id);
                }
                storeRuntime = FindComponent(RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager"));
                currentStore = storeRuntime?.GetType().GetProperty("CurrentBusiness")?.GetValue(storeRuntime);
                currentStoreId = currentStore?.GetType().GetProperty("InstanceId")?.GetValue(currentStore) as string;
                if (!string.IsNullOrWhiteSpace(currentStoreId)) idsAfter.Add(currentStoreId);
                foreach (string id in idsBefore)
                    Assert.IsTrue(idsAfter.Contains(id), $"Save/load must preserve business identity {id}.");
            }
            finally
            {
                if (hadSave) File.Copy(tempPath, path, true);
                else if (File.Exists(path)) File.Delete(path);
                if (hadBackup) File.Copy(tempBackupPath, backupPath, true);
                else if (File.Exists(backupPath)) File.Delete(backupPath);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (File.Exists(tempBackupPath)) File.Delete(tempBackupPath);
            }

            yield return ReloadMainSceneForTestIsolation();
        }

        private static object CreateBusiness(Component sharedRuntime, Type sharedRuntimeType, object businessType, string name)
        {
            object[] arguments = { businessType, name, null, null };
            bool success = (bool)sharedRuntimeType.GetMethod("TryCreatePlayerBusiness")
                .Invoke(sharedRuntime, arguments);
            Assert.IsTrue(success, $"Production formation must create {name}: {arguments[3]}");
            return arguments[2];
        }

        private static IEnumerator WaitForScenarioRegistration(Component harness, Type harnessType)
        {
            // Full fixture runs may follow a save/restore scene and require the
            // complete authored-town bootstrap, not merely one rendered frame.
            for (int frame = 0; frame < 5000; frame++)
            {
                object ids = harnessType.GetProperty("ScenarioIds")?.GetValue(harness);
                if (ids is System.Collections.IEnumerable enumerable)
                {
                    foreach (object id in enumerable)
                    {
                        if (string.Equals(id as string, "first-ledger", StringComparison.Ordinal))
                        {
                            yield break;
                        }
                    }
                }

                yield return null;
            }

            Assert.Fail("The editorial harness did not observe the authored First Ledger scenario registration.");
        }

        private static IEnumerator ReloadMainSceneForTestIsolation()
        {
            AsyncOperation reload = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(reload, "Main Scene must remain available for PlayMode test isolation.");
            while (!reload.isDone)
            {
                yield return null;
            }

            yield return null;
        }

        private static object FindExistingBusiness(Component sharedRuntime, Type sharedRuntimeType, object wantedType)
        {
            object businesses = sharedRuntimeType.GetProperty("Businesses")?.GetValue(sharedRuntime);
            foreach (object candidate in (System.Collections.IEnumerable)businesses)
            {
                if (candidate.GetType().GetProperty("BusinessType")?.GetValue(candidate)?.Equals(wantedType) == true)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Type RuntimeType(string fullName)
        {
            return Type.GetType(fullName + ", Assembly-CSharp")
                ?? Type.GetType(fullName);
        }

        private static bool ContainsEnumerableValue(object values, string expected)
        {
            foreach (object value in (System.Collections.IEnumerable)values)
            {
                if (string.Equals(value as string, expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Component FindComponent(Type componentType)
        {
            Assert.NotNull(componentType, "The production runtime type must be loadable.");
            MonoBehaviour[] components = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                if (componentType.IsInstanceOfType(components[i]))
                {
                    return components[i];
                }
            }

            return null;
        }
    }
}
