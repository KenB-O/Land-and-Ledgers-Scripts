using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.ReadModels.Valuation;
using UnityEngine;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-4: the equipment register per business — every significant asset and
    /// toolkit the business holds. Feeds the BIZ-5 valuation honestly:
    ///   - transferable floor from REAL assets, condition-weighted (Canon §11.6):
    ///     a wrecked plow is not a floor contributor; never blindly added on top
    ///     of earnings (the read model takes the max, Tech X §9.3);
    ///   - capital replacement need from worn equipment (Canon §11.5): equipment
    ///     below half condition will need capital to replace.
    /// Wear/price formulas are calibration (Canon Part XV); the qualitative
    /// rules (condition-weighted floor, replacement need as an adjustment) are canon.
    /// </summary>
    [Serializable]
    public sealed class BusinessEquipmentRegister
    {
        public string BusinessInstanceId = string.Empty;
        public List<EquipmentAsset> Assets = new List<EquipmentAsset>();
        public List<ToolKitInstance> Kits = new List<ToolKitInstance>();

        public void RegisterAsset(EquipmentAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) return;
            Assets.RemoveAll(a => a.AssetId == asset.AssetId);
            Assets.Add(asset);
        }

        public void RegisterKit(ToolKitInstance kit)
        {
            if (kit == null || string.IsNullOrWhiteSpace(kit.InstanceId)) return;
            Kits.RemoveAll(k => k.InstanceId == kit.InstanceId);
            Kits.Add(kit);
        }

        public void RemoveAsset(string assetId)
        {
            Assets.RemoveAll(a => a.AssetId == assetId);
        }

        /// <summary>
        /// Canon §11.6: condition-weighted transferable value. Assets at or below
        /// the usable threshold contribute nothing — a wrecked plow is not a
        /// floor contributor. Replacement costs come from the caller's price data
        /// (never invented here); unknown kinds contribute zero, loudly documented
        /// by the caller, never silently.
        /// </summary>
        public int TransferableValueCents(
            Func<string, int> replacementCostCentsForAssetKind,
            Func<string, int> replacementCostCentsForKitId)
        {
            long total = 0;
            if (Assets != null)
            {
                foreach (var asset in Assets)
                {
                    if (asset == null || !asset.IsUsable) continue;
                    int cost = replacementCostCentsForAssetKind != null
                        ? Math.Max(0, replacementCostCentsForAssetKind(asset.Kind)) : 0;
                    total += (long)Math.Round(cost * asset.Condition01);
                }
            }
            if (Kits != null)
            {
                foreach (var kit in Kits)
                {
                    if (kit == null || !kit.IsUsable) continue;
                    int cost = replacementCostCentsForKitId != null
                        ? Math.Max(0, replacementCostCentsForKitId(kit.KitId)) : 0;
                    total += (long)Math.Round(cost * kit.Condition01);
                }
            }
            return (int)Math.Min(total, int.MaxValue);
        }

        /// <summary>
        /// Canon §11.5: capital replacement need — equipment below half condition
        /// will need capital to replace. Calibration: the 0.5 threshold and the
        /// linear (1 - condition) shape are tuning, not canon.
        /// </summary>
        public int ReplacementNeedCents(
            Func<string, int> replacementCostCentsForAssetKind,
            Func<string, int> replacementCostCentsForKitId)
        {
            const float wornThreshold = 0.5f;
            long total = 0;
            if (Assets != null)
            {
                foreach (var asset in Assets)
                {
                    if (asset == null || asset.Condition01 >= wornThreshold) continue;
                    int cost = replacementCostCentsForAssetKind != null
                        ? Math.Max(0, replacementCostCentsForAssetKind(asset.Kind)) : 0;
                    total += (long)Math.Round(cost * (1f - asset.Condition01));
                }
            }
            if (Kits != null)
            {
                foreach (var kit in Kits)
                {
                    if (kit == null || kit.Condition01 >= wornThreshold) continue;
                    int cost = replacementCostCentsForKitId != null
                        ? Math.Max(0, replacementCostCentsForKitId(kit.KitId)) : 0;
                    total += (long)Math.Round(cost * (1f - kit.Condition01));
                }
            }
            return (int)Math.Min(total, int.MaxValue);
        }
    }

    /// <summary>
    /// EQP-4: posts a business's equipment register into the BIZ-5 valuation
    /// read model — the condition-weighted floor via the existing
    /// RecordTransferableAssets path, and the replacement need via the new
    /// RecordCapitalReplacementNeed path (Canon §11.5 valuation adjustment).
    /// </summary>
    public static class EquipmentValuationSync
    {
        public static void SyncToReadModel(
            BusinessEquipmentRegister register,
            EnterpriseValuationReadModel readModel,
            Func<string, int> replacementCostCentsForAssetKind,
            Func<string, int> replacementCostCentsForKitId,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (register == null)
            {
                diagnostics.Add("EquipmentValuationSync: no register.");
                return;
            }
            if (readModel == null)
            {
                diagnostics.Add("EquipmentValuationSync: no read model.");
                return;
            }
            int floor = register.TransferableValueCents(replacementCostCentsForAssetKind, replacementCostCentsForKitId);
            int need = register.ReplacementNeedCents(replacementCostCentsForAssetKind, replacementCostCentsForKitId);
            readModel.RecordTransferableAssets(register.BusinessInstanceId, floor);
            readModel.RecordCapitalReplacementNeed(register.BusinessInstanceId, need);
            diagnostics.Add($"EquipmentValuationSync: {register.BusinessInstanceId} floor {floor}c, replacement need {need}c (Canon §11.5/§11.6).");
        }
    }
}
