using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-1: ToolKit definitions for low-value ordinary hand-tool collections
    /// (Tech X §3.4, Canon 4.6). "ToolKit" does NOT mean the underlying tools are
    /// historically unspecified — each kit carries its canonical contents as data.
    /// Kits are tracked as one unit with a single condition: wear applies to the
    /// kit, and reconditioning (sharpening, replacing a broken handle) restores it.
    /// Significant single assets (plow, wagon, reaper) stay <see cref="Blacksmith.EquipmentAsset"/>
    /// instances; kits cover the hand-tool trades (builder, tailor, barber, doctor).
    /// </summary>
    [Serializable]
    public sealed class ToolKitDefinition
    {
        public string KitId = string.Empty;      // "carpenter-hand-tool-kit"
        public string DisplayName = string.Empty; // "Carpenter Hand Tool Kit"
        public string Trade = string.Empty;       // "builder", "tailor", "barber", "doctor"
        public List<string> CanonicalContents = new List<string>();
        public string SourceNote = string.Empty;  // e.g. "Canon Part V: Builder"

        public ToolKitDefinition() { }

        public ToolKitDefinition(string kitId, string displayName, string trade,
            string sourceNote, params string[] contents)
        {
            KitId = kitId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Trade = trade ?? string.Empty;
            SourceNote = sourceNote ?? string.Empty;
            CanonicalContents = new List<string>(contents ?? new string[0]);
        }
    }

    /// <summary>
    /// EQP-1: one physical kit in the world. Mirrors the EquipmentAsset lifecycle
    /// (condition, ownership, location, reservation) at kit granularity.
    /// </summary>
    [Serializable]
    public sealed class ToolKitInstance
    {
        public string InstanceId = string.Empty;
        public string KitId = string.Empty;

        [Range(0f, 1f)]
        public float Condition01 = 1f;

        public string OwnerKind = string.Empty; // "business", "household", "person"
        public string OwnerId = string.Empty;
        public string LocationId = string.Empty;

        public string ReservedBy = string.Empty;
        public string ReservedFor = string.Empty;

        public List<string> MaintenanceLog = new List<string>();

        public bool IsReserved => !string.IsNullOrWhiteSpace(ReservedBy);
        public bool IsUsable => Condition01 > 0.05f;

        public string Reserve(string reserverId, string purpose)
        {
            if (string.IsNullOrWhiteSpace(reserverId))
                return "ToolKitInstance.Reserve: reserver id is required.";
            if (IsReserved)
                return $"Tool kit {InstanceId} is already reserved by {ReservedBy} for '{ReservedFor}' — no double-booking (Tech X §3.8).";
            ReservedBy = reserverId;
            ReservedFor = purpose ?? string.Empty;
            return null;
        }

        public void Release()
        {
            ReservedBy = string.Empty;
            ReservedFor = string.Empty;
        }

        /// <summary>Applies wear. A wrecked kit is not deleted — it awaits reconditioning.</summary>
        public void ApplyWear(float amount01)
        {
            Condition01 = Mathf.Clamp01(Condition01 - Mathf.Max(0f, amount01));
        }

        /// <summary>Reconditioning: sharpening, replacing handles — the canon-implied maintenance loop.</summary>
        public void Recondition(float condition01, int dayIndex, string note)
        {
            Condition01 = Mathf.Clamp01(condition01);
            MaintenanceLog.Add($"Day {dayIndex}: reconditioned to {Condition01:0.00} — {note}");
        }

        public string TransferOwnership(string newOwnerKind, string newOwnerId, string reason)
        {
            if (string.IsNullOrWhiteSpace(newOwnerId))
                return $"Tool kit {InstanceId}: new owner id is required.";
            if (IsReserved)
                return $"Tool kit {InstanceId}: cannot transfer while reserved by {ReservedBy}.";
            OwnerKind = newOwnerKind ?? string.Empty;
            OwnerId = newOwnerId;
            return null;
        }
    }
}
