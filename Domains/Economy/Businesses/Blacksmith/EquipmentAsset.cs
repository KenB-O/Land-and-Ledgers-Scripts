using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>
    /// EQU-2: significant equipment uses the shared asset lifecycle with persistent
    /// identity (Tech X §3.3) — plows, harrows, wagons parts, harnesses. Condition,
    /// ownership, location, and reservation are first-class because they gate real
    /// work: a damaged plow blocks PlowField (Tech X §3.9), and one reservation
    /// authority means a plow cannot be double-booked (Tech X §3.8).
    /// </summary>
    [Serializable]
    public sealed class EquipmentAsset
    {
        public string AssetId = string.Empty;
        public string Kind = string.Empty; // "plow", "harrow", "horseshoe-set", ...
        public string DisplayName = string.Empty;

        [Range(0f, 1f)]
        public float Condition01 = 1f;

        public string OwnerKind = string.Empty; // "business", "household"
        public string OwnerId = string.Empty;
        public string LocationId = string.Empty; // physical truth — where it is

        public string ReservedBy = string.Empty; // task/person id holding it
        public string ReservedFor = string.Empty;

        // Provenance: who made it, from what, when.
        public string MadeByBusinessId = string.Empty;
        public string MadeByBusinessName = string.Empty;
        public List<string> MaterialLotIds = new List<string>();
        public int MadeDayIndex;

        // EQP-1: maintenance/utilization history (Tech X §3.3; Tech X 6.18) —
        // repair history updates the asset and feeds future diligence/resale.
        public List<string> MaintenanceLog = new List<string>();

        // D4L: ownership chain — prior owners as history, so title transfers
        // keep provenance intact (Canon §6.5I: history affects diligence,
        // resale, confidence). Follows the animal OwnershipHistory shape.
        public List<EquipmentOwnershipRecord> OwnershipHistory = new List<EquipmentOwnershipRecord>();

        public bool IsReserved => !string.IsNullOrWhiteSpace(ReservedBy);
        public bool IsUsable => Condition01 > 0.05f;

        /// <summary>Reserves the asset for one task. Refuses when already reserved.</summary>
        public string Reserve(string reserverId, string purpose)
        {
            if (string.IsNullOrWhiteSpace(reserverId))
                return "EquipmentAsset.Reserve: reserver id is required.";
            if (IsReserved)
                return $"EquipmentAsset {AssetId} is already reserved by {ReservedBy} for '{ReservedFor}' — no double-booking (Tech X §3.8).";
            ReservedBy = reserverId;
            ReservedFor = purpose ?? string.Empty;
            return null;
        }

        public void Release()
        {
            ReservedBy = string.Empty;
            ReservedFor = string.Empty;
        }

        /// <summary>Applies wear. A wrecked asset is not deleted — it awaits repair.</summary>
        public void ApplyWear(float amount01)
        {
            Condition01 = Mathf.Clamp01(Condition01 - Mathf.Max(0f, amount01));
        }

        public void RepairTo(float condition01)
        {
            Condition01 = Mathf.Clamp01(condition01);
        }

        /// <summary>
        /// EQP-1: records a maintenance event (sharpening, repair, reconditioning).
        /// History feeds diligence and resale (Tech X §3.3; Tech X 6.18).
        /// </summary>
        public void RecordMaintenance(string entry, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(entry)) return;
            MaintenanceLog.Add($"Day {dayIndex}: {entry}");
        }

        public string TransferOwnership(string newOwnerKind, string newOwnerId, string reason)
        {
            return TransferOwnership(newOwnerKind, newOwnerId, reason, -1);
        }

        /// <summary>
        /// D4L: transfers title and appends the transfer to OwnershipHistory —
        /// prior owners as history, provenance intact. A negative dayIndex
        /// records the transfer with the day unknown (legacy callers).
        /// </summary>
        public string TransferOwnership(string newOwnerKind, string newOwnerId, string reason, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(newOwnerId))
                return $"EquipmentAsset {AssetId}: new owner id is required.";
            if (IsReserved)
                return $"EquipmentAsset {AssetId}: cannot transfer while reserved by {ReservedBy}.";
            OwnershipHistory.Add(new EquipmentOwnershipRecord
            {
                FromOwnerKind = OwnerKind,
                FromOwnerId = OwnerId,
                ToOwnerKind = newOwnerKind ?? string.Empty,
                ToOwnerId = newOwnerId,
                DayIndex = dayIndex,
                Reason = reason ?? string.Empty,
            });
            OwnerKind = newOwnerKind ?? string.Empty;
            OwnerId = newOwnerId;
            return null;
        }
    }

    /// <summary>
    /// D4L: one recorded ownership transfer on an equipment asset — prior
    /// owners as history (Canon §6.5I). The current owner is the asset's
    /// OwnerKind/OwnerId; this list is the chain behind it.
    /// </summary>
    [Serializable]
    public sealed class EquipmentOwnershipRecord
    {
        public string FromOwnerKind = string.Empty;
        public string FromOwnerId = string.Empty;
        public string ToOwnerKind = string.Empty;
        public string ToOwnerId = string.Empty;
        public int DayIndex = -1; // -1 = day unknown (legacy transfers)
        public string Reason = string.Empty;

        public EquipmentOwnershipRecord() { }

        public EquipmentOwnershipRecord Clone()
        {
            return new EquipmentOwnershipRecord
            {
                FromOwnerKind = FromOwnerKind,
                FromOwnerId = FromOwnerId,
                ToOwnerKind = ToOwnerKind,
                ToOwnerId = ToOwnerId,
                DayIndex = DayIndex,
                Reason = Reason,
            };
        }
    }

    /// <summary>What a smith can make. Recipes are data, not code paths.</summary>
    [Serializable]
    public sealed class SmithingRecipe
    {
        public string EquipmentKind = string.Empty;
        public string DisplayName = string.Empty;
        public string MaterialId = string.Empty; // EQU-1 import material id
        public int MaterialUnits;
        public int FuelUnits; // forge coal
        public int MinutesAtLevel1; // TTS-1 quantum; skill scales it
        public string RequiredSkillId = string.Empty;

        public SmithingRecipe() { }

        public SmithingRecipe(string kind, string name, string materialId,
            int materialUnits, int fuelUnits, int minutes, string skillId)
        {
            EquipmentKind = kind;
            DisplayName = name;
            MaterialId = materialId;
            MaterialUnits = materialUnits;
            FuelUnits = fuelUnits;
            MinutesAtLevel1 = minutes;
            RequiredSkillId = skillId ?? string.Empty;
        }
    }
}
