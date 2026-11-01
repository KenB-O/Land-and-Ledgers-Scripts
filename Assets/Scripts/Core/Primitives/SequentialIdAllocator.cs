using System;
using UnityEngine;

namespace LandLedgers.Primitives
{
    [Serializable]
    public sealed class SequentialIdAllocator
    {
        [SerializeField]
        private int nextId;

        public int NextId => nextId;

        public SequentialIdAllocator(int initialNextId = 0)
        {
            if (initialNextId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialNextId), "Initial nextId cannot be negative.");
            }

            nextId = initialNextId;
        }

        public int AllocateNext()
        {
            if (nextId == int.MaxValue)
            {
                throw new InvalidOperationException("Allocator overflow: exhausted int.MaxValue ID space.");
            }

            int allocated = nextId;
            nextId++;
            return allocated;
        }

        public void SeedAtLeast(int candidate)
        {
            if (candidate >= 0 && candidate > nextId)
            {
                nextId = candidate;
            }
        }

        public void RestoreExact(int persistedNextId)
        {
            if (persistedNextId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(persistedNextId), "Cannot restore a negative next ID.");
            }

            nextId = persistedNextId;
        }
    }
}
