using System;
using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class IdAllocatorWriterCutoverTests
    {
        private static PopulationGenerationSettings CreateSettings(NewcomerArrivalProfile onlyProfile, int minimumArrivalHouseholds = 1)
        {
            PopulationGenerationSettings settings = ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.minimumNewcomerArrivalHouseholds = minimumArrivalHouseholds;
            settings.residentialBoardingBaseCapacity = 0;
            settings.mixedUseBoardingBaseCapacity = 0;
            settings.pressuredArrivalDepartureChance = 0f;
            settings.loneLaborerArrivalWeight = 0;
            settings.kinLinkedArrivalWeight = 0;
            settings.boarderSeekingWorkerArrivalWeight = 0;
            settings.renterReadyHouseholdArrivalWeight = 0;
            settings.skilledCapitalizedArrivalWeight = 0;
            settings.distressedRelocationArrivalWeight = 0;
            settings.widowElderRelocationArrivalWeight = 0;

            switch (onlyProfile)
            {
                case NewcomerArrivalProfile.BoarderSeekingWorker:
                    settings.boarderSeekingWorkerArrivalWeight = 1;
                    break;
                case NewcomerArrivalProfile.RenterReadyHousehold:
                    settings.renterReadyHouseholdArrivalWeight = 1;
                    break;
                default:
                    settings.loneLaborerArrivalWeight = 1;
                    break;
            }

            return settings;
        }

        private static PersonState CreatePerson(int id, int householdId, int homeBuildingId)
        {
            return new PersonState
            {
                id = id,
                firstName = "Person",
                lastName = "Test",
                householdId = householdId,
                homeBuildingId = homeBuildingId,
                settlementArrangement = SettlementArrangement.StableHousehold,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket
            };
        }

        private static HouseholdState CreateHousehold(int id, int homeBuildingId, int boardingCapacity = 0)
        {
            return new HouseholdState
            {
                id = id,
                householdName = $"Household {id}",
                surname = "Test",
                homeBuildingId = homeBuildingId,
                memberIds = new List<int>(),
                boarderPersonIds = new List<int>(),
                baseBoardingCapacity = boardingCapacity,
                boardingCapacity = boardingCapacity,
                hostsBoarders = boardingCapacity > 0,
                settlementArrangement = SettlementArrangement.StableHousehold,
                demandSnapshot = HouseholdDemandSnapshot.Empty()
            };
        }

        [Test]
        public void NewPersonAllocation_UsesAllocatorSequence_RatherThanPeopleCount()
        {
            PopulationState state = new();
            HouseholdState host = CreateHousehold(1, 1, boardingCapacity: 2);
            state.households.Add(host);

            PersonState p0 = CreatePerson(0, 1, 1);
            PersonState p1 = CreatePerson(1, 1, 1);
            PersonState p5 = CreatePerson(5, 1, 1);
            host.memberIds.AddRange(new[] { 0, 1, 5 });
            state.people.AddRange(new[] { p0, p1, p5 });

            state.RestorePersonIdAllocator(6);
            state.RestoreHouseholdIdAllocator(2);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 1, false, true, 2)
            };

            int created = NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(4), homes);

            Assert.GreaterOrEqual(created, 1);
            Assert.AreEqual(4, state.people.Count);
            PersonState newPerson = state.people[3];
            Assert.AreEqual(6, newPerson.id, "New person ID must use allocator sequence (6), not people.Count (3).");
            Assert.AreEqual(0, state.people[0].id);
            Assert.AreEqual(1, state.people[1].id);
            Assert.AreEqual(5, state.people[2].id);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void RemovingPerson_DoesNotCauseIdReuse()
        {
            PopulationState state = new();
            HouseholdState host = CreateHousehold(1, 1, boardingCapacity: 4);
            state.households.Add(host);

            PersonState p0 = CreatePerson(0, 1, 1);
            PersonState p1 = CreatePerson(1, 1, 1);
            PersonState p5 = CreatePerson(5, 1, 1);
            host.memberIds.AddRange(new[] { 0, 1, 5 });
            state.people.AddRange(new[] { p0, p1, p5 });

            state.RestorePersonIdAllocator(6);
            state.RestoreHouseholdIdAllocator(2);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 1, false, true, 4)
            };

            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(4), homes);
            Assert.IsTrue(state.people.Exists(p => p.id == 6));
            int nextBeforeRemoval = state.NextPersonId;

            state.people.RemoveAll(p => p.id == 6);
            host.boarderPersonIds.Remove(6);
            host.memberIds.Remove(6);

            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(4), homes);
            PersonState latestPerson = state.people[state.people.Count - 1];
            Assert.GreaterOrEqual(latestPerson.id, nextBeforeRemoval);
            Assert.AreNotEqual(6, latestPerson.id, "ID 6 must not be reused after removal.");
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void NewHouseholdAllocation_UsesAllocatorSequence_RatherThanHouseholdsCount()
        {
            PopulationState state = new();
            HouseholdState h0 = CreateHousehold(0, 10);
            HouseholdState h5 = CreateHousehold(5, 11);
            state.households.AddRange(new[] { h0, h5 });

            state.RestoreHouseholdIdAllocator(6);
            state.RestorePersonIdAllocator(0);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.RenterReadyHousehold, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(10, 1, false, false, 0),
                new(11, 1, false, false, 0),
                new(20, 2, false, false, 0)
            };

            int created = NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(2), homes);
            Assert.GreaterOrEqual(created, 1);
            Assert.AreEqual(3, state.households.Count);
            HouseholdState newHousehold = state.households[2];
            Assert.AreEqual(6, newHousehold.id, "New household ID must be 6 from allocator sequence, not 2 from households.Count.");
            Assert.AreEqual(0, state.households[0].id);
            Assert.AreEqual(5, state.households[1].id);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void PruningHighestHousehold_DoesNotCauseIdReuse()
        {
            PopulationState state = new();
            HouseholdState h0 = CreateHousehold(0, 10);
            HouseholdState h1 = CreateHousehold(1, 11);
            HouseholdState h2 = CreateHousehold(2, 12);
            state.households.AddRange(new[] { h0, h1, h2 });

            state.RestoreHouseholdIdAllocator(3);
            state.RestorePersonIdAllocator(0);

            state.households.RemoveAt(2);
            Assert.AreEqual(2, state.households.Count);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.RenterReadyHousehold, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(10, 1, false, false, 0),
                new(11, 1, false, false, 0),
                new(20, 2, false, false, 0)
            };

            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(2), homes);
            HouseholdState newlyCreated = state.households[state.households.Count - 1];
            Assert.AreEqual(3, newlyCreated.id, "New household must receive 3, not the pruned household ID (2).");
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void GappedHouseholdIds_RemainValidForAllocation()
        {
            PopulationState state = new();
            HouseholdState h0 = CreateHousehold(0, 10);
            HouseholdState h10 = CreateHousehold(10, 11);
            HouseholdState h50 = CreateHousehold(50, 12);
            state.households.AddRange(new[] { h0, h10, h50 });

            state.RestoreHouseholdIdAllocator(51);
            state.RestorePersonIdAllocator(100);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.RenterReadyHousehold, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(10, 1, false, false, 0),
                new(11, 1, false, false, 0),
                new(12, 1, false, false, 0),
                new(20, 2, false, false, 0)
            };

            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(2), homes);
            HouseholdState newlyCreated = state.households[state.households.Count - 1];
            Assert.AreEqual(51, newlyCreated.id, "New household ID must be 51 across gapped existing households.");
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void ExistingPersonIds_RemainUnchangedDuringNewCreation()
        {
            PopulationState state = new();
            HouseholdState host = CreateHousehold(1, 1, boardingCapacity: 3);
            state.households.Add(host);

            PersonState p10 = CreatePerson(10, 1, 1);
            PersonState p20 = CreatePerson(20, 1, 1);
            PersonState p30 = CreatePerson(30, 1, 1);
            host.memberIds.AddRange(new[] { 10, 20, 30 });
            state.people.AddRange(new[] { p10, p20, p30 });

            state.RestorePersonIdAllocator(31);
            state.RestoreHouseholdIdAllocator(2);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(1, 1, false, true, 3)
            };

            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(4), homes);

            Assert.AreEqual(10, state.people[0].id);
            Assert.AreEqual(20, state.people[1].id);
            Assert.AreEqual(30, state.people[2].id);
            Assert.AreEqual(31, state.people[3].id);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void ExistingHouseholdIds_RemainUnchangedDuringNewCreation()
        {
            PopulationState state = new();
            HouseholdState h5 = CreateHousehold(5, 10);
            HouseholdState h15 = CreateHousehold(15, 11);
            HouseholdState h25 = CreateHousehold(25, 12);
            state.households.AddRange(new[] { h5, h15, h25 });

            state.RestoreHouseholdIdAllocator(26);
            state.RestorePersonIdAllocator(0);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.RenterReadyHousehold, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(10, 1, false, false, 0),
                new(11, 1, false, false, 0),
                new(12, 1, false, false, 0),
                new(20, 2, false, false, 0)
            };

            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(2), homes);

            Assert.AreEqual(5, state.households[0].id);
            Assert.AreEqual(15, state.households[1].id);
            Assert.AreEqual(25, state.households[2].id);
            Assert.AreEqual(26, state.households[3].id);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void BoardingHouseGuestHousehold_UsesAllocatorSequence_RatherThanHouseholdsCount()
        {
            PopulationState state = new();
            HouseholdState h0 = CreateHousehold(0, 10);
            state.households.Add(h0);

            state.RestoreHouseholdIdAllocator(10);
            state.RestorePersonIdAllocator(0);

            PopulationGenerationSettings settings = CreateSettings(NewcomerArrivalProfile.BoarderSeekingWorker, 1);
            List<NewcomerSettlementHomeOption> homes = new()
            {
                new(10, 1, false, false, 0)
            };
            List<NewcomerSettlementBoardingOption> boardingHouses = new()
            {
                new("boarding-bh", 30, "Grand Boarding House", 4, true, 10000, 1, 2)
            };

            NewcomerSettlementPlanner.GenerateNewcomerArrivals(state, settings, new System.Random(11), homes, boardingHouses);

            HouseholdState lodging = state.households.Find(h => h.isBoardingHouseGuestHousehold);
            Assert.NotNull(lodging);
            Assert.AreEqual(10, lodging.id, "Guest household ID must be 10 from allocator, not 1 from count.");
            UnityEngine.Object.DestroyImmediate(settings);
        }
    }
}
