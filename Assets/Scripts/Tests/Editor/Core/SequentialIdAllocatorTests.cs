using System;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Core
{
    [TestFixture]
    public sealed class SequentialIdAllocatorTests
    {
        [Test]
        public void FreshAllocator_StartsAtZero()
        {
            SequentialIdAllocator allocator = new();
            Assert.AreEqual(0, allocator.NextId);
        }

        [Test]
        public void AllocateNext_ReturnsZeroThenOneThenTwo()
        {
            SequentialIdAllocator allocator = new();
            Assert.AreEqual(0, allocator.AllocateNext());
            Assert.AreEqual(1, allocator.AllocateNext());
            Assert.AreEqual(2, allocator.AllocateNext());
        }

        [Test]
        public void Allocation_AdvancesExactlyOnce()
        {
            SequentialIdAllocator allocator = new();
            Assert.AreEqual(0, allocator.NextId);
            int allocated = allocator.AllocateNext();
            Assert.AreEqual(0, allocated);
            Assert.AreEqual(1, allocator.NextId);
        }

        [Test]
        public void SeedAtLeast_HigherValueAdvances()
        {
            SequentialIdAllocator allocator = new();
            allocator.SeedAtLeast(42);
            Assert.AreEqual(42, allocator.NextId);
            Assert.AreEqual(42, allocator.AllocateNext());
            Assert.AreEqual(43, allocator.NextId);
        }

        [Test]
        public void SeedAtLeast_LowerValueDoesNotMoveBackward()
        {
            SequentialIdAllocator allocator = new(10);
            allocator.SeedAtLeast(5);
            Assert.AreEqual(10, allocator.NextId);
            allocator.SeedAtLeast(10);
            Assert.AreEqual(10, allocator.NextId);
        }

        [Test]
        public void SeedAtLeast_NegativeValueDoesNotCreateNegativeState()
        {
            SequentialIdAllocator allocator = new();
            allocator.SeedAtLeast(-1);
            allocator.SeedAtLeast(-999);
            Assert.AreEqual(0, allocator.NextId);

            SequentialIdAllocator advanced = new(5);
            advanced.SeedAtLeast(-10);
            Assert.AreEqual(5, advanced.NextId);
        }

        [Test]
        public void RestoreExact_RestoresNonNegativeValueExactly()
        {
            SequentialIdAllocator allocator = new();
            allocator.RestoreExact(100);
            Assert.AreEqual(100, allocator.NextId);
            Assert.AreEqual(100, allocator.AllocateNext());

            allocator.RestoreExact(0);
            Assert.AreEqual(0, allocator.NextId);
        }

        [Test]
        public void RestoreExact_NegativeValueThrows()
        {
            SequentialIdAllocator allocator = new(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => allocator.RestoreExact(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => allocator.RestoreExact(-50));
            Assert.AreEqual(5, allocator.NextId);
        }

        [Test]
        public void Constructor_NegativeInitialValueCannotCreateNegativeAllocatorState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SequentialIdAllocator(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SequentialIdAllocator(-100));
        }

        [Test]
        public void AllocateNext_AtIntMaxValueThrowsWithoutWrapping()
        {
            SequentialIdAllocator allocator = new();
            allocator.RestoreExact(int.MaxValue);
            Assert.AreEqual(int.MaxValue, allocator.NextId);

            Assert.Throws<InvalidOperationException>(() => allocator.AllocateNext());
            Assert.AreEqual(int.MaxValue, allocator.NextId);
        }

        [Test]
        public void DtoFieldsExistAndJsonUtilityRoundTripPreserves()
        {
            PopulationSaveDto popDto = new()
            {
                nextPersonId = 123,
                nextHouseholdId = 456
            };
            string popJson = JsonUtility.ToJson(popDto);
            PopulationSaveDto deserializedPop = JsonUtility.FromJson<PopulationSaveDto>(popJson);
            Assert.IsNotNull(deserializedPop);
            Assert.AreEqual(123, deserializedPop.nextPersonId);
            Assert.AreEqual(456, deserializedPop.nextHouseholdId);

            WorldSaveDto worldDto = new()
            {
                nextBuildingId = 789
            };
            string worldJson = JsonUtility.ToJson(worldDto);
            WorldSaveDto deserializedWorld = JsonUtility.FromJson<WorldSaveDto>(worldJson);
            Assert.IsNotNull(deserializedWorld);
            Assert.AreEqual(789, deserializedWorld.nextBuildingId);
        }
    }
}
