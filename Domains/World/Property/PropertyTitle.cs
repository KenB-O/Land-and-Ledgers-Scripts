using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// T2F: how title honestly changes hands. Every transfer names its basis
    /// and, for purchases, the instrument that conveyed it (T2A deed/note ids
    /// plug in here as strings). Land never becomes available "simply because"
    /// (Canon: "A neighboring owner can refuse to sell, ask more, offer a
    /// lease or have legal/title constraints").
    /// </summary>
    public enum TitleBasis
    {
        Unspecified = 0,
        Purchase = 1,    // requires a conveyance instrument id
        HomesteadClaim = 2,
        Inheritance = 3,
        Grant = 4,
        CourtOrder = 5,
    }

    /// <summary>T2F: the land unit. Everything else hangs off parcels.</summary>
    [Serializable]
    public sealed class Parcel
    {
        public string ParcelId = string.Empty;
        public string Description = string.Empty;
        public float Acres;
        public string CurrentHolderName = string.Empty;

        public Parcel() { }
    }

    /// <summary>T2F: one link in a parcel's chain of title.</summary>
    [Serializable]
    public sealed class TitleRecord
    {
        public EntityId RecordId = EntityId.Invalid;
        public string ParcelId = string.Empty;
        public string HolderName = string.Empty;
        public TitleBasis Basis = TitleBasis.Unspecified;
        public string InstrumentId = string.Empty; // the deed/note that conveyed it (T2A)
        public int DayIndex;
        public string Note = string.Empty;

        public TitleRecord() { }
    }

    /// <summary>
    /// T2F: the title authority. Title is derived from an unbroken chain of
    /// real events — purchase, claim, inheritance, grant, court order. There
    /// is no title without provenance, and a parcel's current holder is always
    /// the last link, never a separate mutable field that can drift.
    /// </summary>
    public sealed class TitleAuthority
    {
        private readonly Dictionary<string, Parcel> parcels =
            new Dictionary<string, Parcel>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TitleRecord>> chains =
            new Dictionary<string, List<TitleRecord>>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public Parcel RegisterParcel(string parcelId, string description, float acres,
            string initialHolder, TitleBasis basis, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(parcelId))
            {
                diag.Add("TitleAuthority.RegisterParcel: a parcel id is required.");
                return null;
            }
            if (parcels.ContainsKey(parcelId))
            {
                diag.Add($"TitleAuthority.RegisterParcel: parcel '{parcelId}' already registered — title is singular.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(initialHolder))
            {
                diag.Add("TitleAuthority.RegisterParcel: an initial holder is required — land is never holderless by default.");
                return null;
            }
            var parcel = new Parcel
            {
                ParcelId = parcelId,
                Description = description ?? string.Empty,
                Acres = Math.Max(0f, acres),
                CurrentHolderName = initialHolder,
            };
            parcels[parcelId] = parcel;
            chains[parcelId] = new List<TitleRecord>
            {
                new TitleRecord
                {
                    ParcelId = parcelId, HolderName = initialHolder,
                    Basis = basis, DayIndex = dayIndex, Note = "initial registration",
                }
            };
            diag.Add($"TitleAuthority: parcel '{parcelId}' registered — {initialHolder} holds by {basis}.");
            return parcel;
        }

        /// <summary>
        /// T2F: transfers title. Purchases REQUIRE the conveyance instrument
        /// id; the chain is append-only and the parcel's holder follows the
        /// last link (never a drifting field).
        /// </summary>
        public string TransferTitle(
            EntityIdRegistry ids, string parcelId, string newHolder,
            TitleBasis basis, string instrumentId, int dayIndex, string note,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!parcels.TryGetValue(parcelId, out Parcel parcel))
                return $"TitleAuthority.TransferTitle: unknown parcel '{parcelId}'.";
            if (string.IsNullOrWhiteSpace(newHolder))
                return "TitleAuthority.TransferTitle: the new holder must be named.";
            if (basis == TitleBasis.Unspecified)
                return "TitleAuthority.TransferTitle: the basis must be stated — title never moves for no reason.";
            if (basis == TitleBasis.Purchase && string.IsNullOrWhiteSpace(instrumentId))
                return "TitleAuthority.TransferTitle: a purchase without a conveyance instrument is not a transfer.";

            var record = new TitleRecord
            {
                RecordId = ids != null ? ids.Allocate(EntityKind.Contract) : EntityId.Invalid,
                ParcelId = parcelId,
                HolderName = newHolder,
                Basis = basis,
                InstrumentId = instrumentId ?? string.Empty,
                DayIndex = dayIndex,
                Note = note ?? string.Empty,
            };
            chains[parcelId].Add(record);
            parcel.CurrentHolderName = newHolder;
            diag.Add($"TitleAuthority: '{parcelId}' → {newHolder} by {basis} on day {dayIndex}.");
            return null;
        }

        public string CurrentHolder(string parcelId)
        {
            return parcels.TryGetValue(parcelId, out Parcel parcel) ? parcel.CurrentHolderName : null;
        }

        public IReadOnlyList<TitleRecord> ChainOf(string parcelId)
        {
            return chains.TryGetValue(parcelId, out var chain) ? chain : new List<TitleRecord>();
        }

        #region Save / Load
        [Serializable]
        public sealed class TitleSaveDto
        {
            public List<Parcel> Parcels = new List<Parcel>();
            public List<TitleRecord> Records = new List<TitleRecord>();
        }

        public TitleSaveDto CaptureSaveDto()
        {
            var dto = new TitleSaveDto { Parcels = new List<Parcel>(parcels.Values) };
            foreach (var chain in chains.Values) dto.Records.AddRange(chain);
            return dto;
        }

        public void LoadFromSaveDto(TitleSaveDto dto)
        {
            parcels.Clear();
            chains.Clear();
            if (dto == null) return;
            foreach (var parcel in dto.Parcels)
            {
                parcels[parcel.ParcelId] = parcel;
                chains[parcel.ParcelId] = new List<TitleRecord>();
            }
            foreach (var record in dto.Records)
            {
                if (chains.TryGetValue(record.ParcelId, out var chain)) chain.Add(record);
            }
        }
        #endregion
    }
}
