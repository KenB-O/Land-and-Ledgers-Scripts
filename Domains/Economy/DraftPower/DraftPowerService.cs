using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.DraftPower
{
    /// <summary>
    /// EQU-3: the ONE reservation authority for draft animals and drivers
    /// (Tech X §3.8). Before this, the BIZ-3 freight pool kept its own private
    /// reservation lists; now both the freight pool and the new plow work unit
    /// reserve through here — one model for animal draft power, not two.
    ///
    /// Draft animals remain persistent entities with feed, fatigue, health and
    /// assignment (Tech X §3.6) — this service tracks assignment; the HF-2
    /// registry remains the identity authority.
    /// </summary>
    public sealed class DraftPowerService
    {
        private readonly HashSet<string> reservedAnimalKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> reservedDriverKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Reserves up to <paramref name="count"/> eligible, unreserved animals.
        /// Eligibility is caller-supplied: freight takes any draft animal, the plow
        /// unit requires horses. Never double-books.
        /// </summary>
        public bool TryReserveAnimals(
            List<EntityId> candidates,
            int count,
            Func<EntityId, bool> isEligible,
            string purpose,
            out List<EntityId> reserved,
            List<string> diagnostics)
        {
            reserved = new List<EntityId>();
            diagnostics = diagnostics ?? new List<string>();
            if (count <= 0)
            {
                diagnostics.Add("DraftPowerService: reservation needs a positive animal count.");
                return false;
            }
            if (candidates != null)
            {
                foreach (EntityId animal in candidates)
                {
                    if (reserved.Count >= count) break;
                    if (isEligible != null && !isEligible(animal)) continue;
                    string key = animal.ToString();
                    if (reservedAnimalKeys.Contains(key)) continue;
                    reserved.Add(animal);
                }
            }
            if (reserved.Count < count)
            {
                diagnostics.Add(
                    $"DraftPowerService: only {reserved.Count} eligible unreserved draft animals for '{purpose}' (need {count}) — no double-booking (Tech X §3.8).");
                return false;
            }
            foreach (EntityId animal in reserved)
                reservedAnimalKeys.Add(animal.ToString());
            return true;
        }

        public bool TryReserveDriver(
            List<EntityId> candidates,
            out EntityId driver,
            List<string> diagnostics)
        {
            driver = default;
            diagnostics = diagnostics ?? new List<string>();
            if (candidates != null)
            {
                foreach (EntityId candidate in candidates)
                {
                    if (candidate.Kind != EntityKind.Person) continue;
                    if (reservedDriverKeys.Contains(candidate.ToString())) continue;
                    driver = candidate;
                    reservedDriverKeys.Add(candidate.ToString());
                    return true;
                }
            }
            diagnostics.Add("DraftPowerService: no unreserved driver available.");
            return false;
        }

        public void ReleaseAnimals(IEnumerable<EntityId> animals)
        {
            if (animals == null) return;
            foreach (EntityId animal in animals)
                reservedAnimalKeys.Remove(animal.ToString());
        }

        public void ReleaseDriver(EntityId driver)
        {
            reservedDriverKeys.Remove(driver.ToString());
        }

        public bool IsAnimalReserved(EntityId animal) => reservedAnimalKeys.Contains(animal.ToString());
        public bool IsDriverReserved(EntityId driver) => reservedDriverKeys.Contains(driver.ToString());
    }
}
