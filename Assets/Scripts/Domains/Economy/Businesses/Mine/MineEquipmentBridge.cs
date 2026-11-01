using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// D3C: bridges installed mine hoists into the EQP equipment-asset world.
    ///
    /// WIRING DECISION (recorded): the mine's task-gate kind strings
    /// ("windlass", "hoist", "pump") are ALREADY the EQP-native interface —
    /// the EQP framework carries no central EquipmentKind enum; kinds are
    /// decentralized string constants per domain (e.g. FarmImplements'
    /// "threshing-separator"), and the mine's constants live centrally in
    /// MineShaftTaskCatalog / MineHoistingTaskCatalog. Nothing was left to
    /// "map onto".
    ///
    /// What WAS open: a MineHoist in the mine's register is not an
    /// EquipmentAsset in the business's BusinessEquipmentRegister, so EQP
    /// task gates (which read EquipmentAsset records) cannot see installed
    /// hoists. This bridge builds the shadow asset the caller registers.
    ///
    /// AUTHORITY RULE: the MineHoist stays the condition authority (plant
    /// condition + rope wear live there). The shadow asset is the task-gate
    /// view — the caller syncs it explicitly after hoisting work, repair, or
    /// rope replacement. Never let the two drift silently.
    /// </summary>
    public static class MineEquipmentBridge
    {
        public const string OwnerKindBusiness = "business";

        /// <summary>
        /// Builds the EquipmentAsset shadow for an installed hoist. The
        /// caller registers it on the business's BusinessEquipmentRegister.
        /// </summary>
        public static EquipmentAsset ShadowHoistAsset(MineHoist hoist, string businessInstanceId)
        {
            if (hoist == null)
                return null;
            var asset = new EquipmentAsset
            {
                AssetId = $"mine-hoist-shadow-{hoist.HoistId}",
                Kind = MineShaftTaskCatalog.HoistAssetKind,
                DisplayName = $"{MineHoist.GetHoistKindDisplayName(hoist.HoistKind)} (mine hoist {hoist.HoistId})",
                Condition01 = hoist.Condition01,
                OwnerKind = OwnerKindBusiness,
                OwnerId = businessInstanceId ?? string.Empty,
                LocationId = hoist.ShaftId,
            };
            asset.RecordMaintenance(
                $"Installed day {hoist.InstalledDayIndex} on shaft {hoist.ShaftId}; condition mirrors the mine hoist register.", hoist.InstalledDayIndex);
            return asset;
        }

        /// <summary>Builds the EquipmentAsset shadow for a sinking-stage windlass on a shaft.</summary>
        public static EquipmentAsset ShadowWindlassAsset(string shaftId, string businessInstanceId, float condition01)
        {
            if (string.IsNullOrWhiteSpace(shaftId))
                return null;
            return new EquipmentAsset
            {
                AssetId = $"mine-windlass-shadow-{shaftId}",
                Kind = MineShaftTaskCatalog.WindlassAssetKind,
                DisplayName = $"Windlass (shaft {shaftId})",
                Condition01 = UnityEngine.Mathf.Clamp01(condition01),
                OwnerKind = OwnerKindBusiness,
                OwnerId = businessInstanceId ?? string.Empty,
                LocationId = shaftId,
            };
        }

        /// <summary>Builds the EquipmentAsset shadow for a mine pump (dewatering task gate).</summary>
        public static EquipmentAsset ShadowPumpAsset(string pumpAssetId, string shaftId, string businessInstanceId, float condition01)
        {
            if (string.IsNullOrWhiteSpace(pumpAssetId))
                return null;
            return new EquipmentAsset
            {
                AssetId = $"mine-pump-shadow-{pumpAssetId}",
                Kind = MineHoistingTaskCatalog.PumpAssetKind,
                DisplayName = $"Mine pump {pumpAssetId}",
                Condition01 = UnityEngine.Mathf.Clamp01(condition01),
                OwnerKind = OwnerKindBusiness,
                OwnerId = businessInstanceId ?? string.Empty,
                LocationId = shaftId ?? string.Empty,
            };
        }

        /// <summary>
        /// Syncs the shadow asset's condition from the mine hoist register
        /// (the authority) after hoisting work, repair, or rope replacement.
        /// Returns a loud refusal when the shadow does not match the hoist.
        /// </summary>
        public static string SyncHoistCondition(EquipmentAsset shadow, MineHoist hoist, int dayIndex)
        {
            if (shadow == null || hoist == null)
                return "MineEquipmentBridge.SyncHoistCondition: shadow asset and hoist are required.";
            if (!string.Equals(shadow.AssetId, $"mine-hoist-shadow-{hoist.HoistId}", System.StringComparison.Ordinal))
                return $"MineEquipmentBridge.SyncHoistCondition: shadow {shadow.AssetId} does not belong to hoist {hoist.HoistId}.";
            shadow.Condition01 = UnityEngine.Mathf.Clamp01(hoist.Condition01);
            shadow.RecordMaintenance(
                $"Synced from mine hoist register: plant {hoist.Condition01:P0}, rope {hoist.RopeCondition01:P0}, in service {hoist.InService}.",
                dayIndex);
            return null;
        }

        /// <summary>
        /// Verifies that the catalog kind constants used by the task gates
        /// are the same strings this bridge stamps on shadow assets.
        /// </summary>
        public static string VerifyKindWiring(List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            EquipmentAsset probe = ShadowHoistAsset(new MineHoist("probe", HoistKind.SteamHoist, "shaft-x", 0), "biz");
            if (probe == null || probe.Kind != MineShaftTaskCatalog.HoistAssetKind)
            {
                diagnostics.Add("MineEquipmentBridge.VerifyKindWiring: hoist shadow kind drifted from the task-gate constant.");
                return "MineEquipmentBridge.VerifyKindWiring: hoist kind mismatch.";
            }
            EquipmentAsset windlass = ShadowWindlassAsset("shaft-x", "biz", 1f);
            if (windlass == null || windlass.Kind != MineShaftTaskCatalog.WindlassAssetKind)
            {
                diagnostics.Add("MineEquipmentBridge.VerifyKindWiring: windlass shadow kind drifted from the task-gate constant.");
                return "MineEquipmentBridge.VerifyKindWiring: windlass kind mismatch.";
            }
            EquipmentAsset pump = ShadowPumpAsset("pump-1", "shaft-x", "biz", 1f);
            if (pump == null || pump.Kind != MineHoistingTaskCatalog.PumpAssetKind)
            {
                diagnostics.Add("MineEquipmentBridge.VerifyKindWiring: pump shadow kind drifted from the task-gate constant.");
                return "MineEquipmentBridge.VerifyKindWiring: pump kind mismatch.";
            }
            return null;
        }
    }
}
