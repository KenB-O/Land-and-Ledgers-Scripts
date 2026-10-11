using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III land core (Worker A): the SINGLE non-title rights store,
    /// implementing the foundation <see cref="IPropertyInterestStore"/> contract.
    ///
    /// Title (who holds underlying ownership) lives in TitleAuthority's chain.
    /// Everything else — lease, easement, mortgage charge, public-land entry,
    /// right-of-way, use restriction, dispute claim, tax lien — lives here,
    /// exactly once. There is no other rights registry: do not create one.
    ///
    /// Canon: Technical XIV preserved Rev XII §2.2 — typed PropertyInterest /
    /// encumbrance records "can coexist without duplicate Parcel ownership
    /// flags"; Rev XXIII §1.1 (one authority per consequential fact).
    /// </summary>
    public sealed class PropertyInterestStore : ISnapshotableInterestStore
    {
        private readonly Dictionary<string, PropertyInterest> interests =
            new Dictionary<string, PropertyInterest>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public static string NewInterestId() => "interest-" + Guid.NewGuid().ToString("N");

        public int InterestCount => interests.Count;

        public PropertyInterest Get(string interestId)
        {
            return !string.IsNullOrWhiteSpace(interestId) && interests.TryGetValue(interestId, out PropertyInterest i) ? i : null;
        }

        /// <summary>
        /// Records an interest. Assigns an id when missing. First-wins on id so
        /// save/load replay or double delivery cannot duplicate an interest.
        /// An interest must name a parcel and a holder — anonymous burdens are refused.
        /// </summary>
        public void Record(PropertyInterest interest)
        {
            if (interest == null)
            {
                diagnostics.Add("PropertyInterestStore.Record: null interest refused.");
                return;
            }
            if (string.IsNullOrWhiteSpace(interest.ParcelId) || string.IsNullOrWhiteSpace(interest.HolderId))
            {
                diagnostics.Add("PropertyInterestStore.Record: parcel and holder are required — anonymous burdens refused.");
                return;
            }
            if (string.IsNullOrWhiteSpace(interest.Id)) interest.Id = NewInterestId();
            if (interests.ContainsKey(interest.Id)) return;
            interests[interest.Id] = interest;
        }

        /// <summary>
        /// Extinguishes an interest (expiry, release, satisfaction). History is
        /// preserved: the record remains with IsExtinguished set. Unknown ids are
        /// diagnosed, never thrown.
        /// </summary>
        public void Extinguish(string interestId, int day, string reason)
        {
            PropertyInterest interest = Get(interestId);
            if (interest == null)
            {
                diagnostics.Add($"PropertyInterestStore.Extinguish: unknown interest '{interestId}'.");
                return;
            }
            if (interest.IsExtinguished) return;
            interest.IsExtinguished = true;
            if (day > 0) interest.EndDay = day;
            if (!string.IsNullOrWhiteSpace(reason))
                interest.Notes = string.IsNullOrWhiteSpace(interest.Notes)
                    ? $"Extinguished: {reason}"
                    : interest.Notes + $" | Extinguished: {reason}";
        }

        public IReadOnlyList<PropertyInterest> ActiveForParcel(string parcelId)
        {
            var result = new List<PropertyInterest>();
            if (string.IsNullOrWhiteSpace(parcelId)) return result;
            foreach (PropertyInterest interest in interests.Values)
                if (!interest.IsExtinguished && string.Equals(interest.ParcelId, parcelId, StringComparison.Ordinal))
                    result.Add(interest);
            return result;
        }

        public IReadOnlyList<PropertyInterest> ActiveForHolder(string holderId)
        {
            var result = new List<PropertyInterest>();
            if (string.IsNullOrWhiteSpace(holderId)) return result;
            foreach (PropertyInterest interest in interests.Values)
                if (!interest.IsExtinguished && string.Equals(interest.HolderId, holderId, StringComparison.Ordinal))
                    result.Add(interest);
            return result;
        }

        #region Save / Load
        public PropertyInterestStoreSaveDto CaptureSaveDto()
        {
            return new PropertyInterestStoreSaveDto
            {
                interests = new List<PropertyInterest>(interests.Values),
            };
        }

        /// <summary>Clear-then-load: reloading never duplicates interests.</summary>
        public void LoadFromSaveDto(PropertyInterestStoreSaveDto dto)
        {
            interests.Clear();
            if (dto == null || dto.interests == null) return;
            foreach (PropertyInterest interest in dto.interests)
                Record(interest);
        }
        #endregion
    }

    /// <summary>
    /// Save/load and atomic-rollback support for the single rights store.
    /// Kept off the foundation IPropertyInterestStore contract (Worker A addition).
    /// </summary>
    public interface ISnapshotableInterestStore : IPropertyInterestStore
    {
        PropertyInterestStoreSaveDto CaptureSaveDto();
        void LoadFromSaveDto(PropertyInterestStoreSaveDto dto);
    }

    /// <summary>Save data for the single non-title rights store (hub-registered).</summary>
    [Serializable]
    public sealed class PropertyInterestStoreSaveDto
    {
        public List<PropertyInterest> interests = new List<PropertyInterest>();
    }
}
