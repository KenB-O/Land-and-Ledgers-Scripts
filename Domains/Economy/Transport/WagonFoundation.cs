using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Persistence;

namespace LandLedgers.Economy.Transport
{
    public enum WagonSubtype
    {
        GeneralFarmFreight = 1,
    }

    /// <summary>Extends the existing durable EquipmentAsset with wagon-specific invariants.</summary>
    public static class WagonAuthority
    {
        public const string DefaultKind = "farm-freight-wagon";
        public const float DefaultMassCapacityKg = 450f;
        public const float DefaultVolumeCapacityM3 = 4f;

        public static string Configure(
            EquipmentAsset wagon,
            string subtype,
            float massCapacityKg,
            float volumeCapacityM3,
            string storageLocationId)
        {
            if (wagon == null) return "WagonAuthority.Configure: no wagon supplied.";
            if (string.IsNullOrWhiteSpace(wagon.AssetId)) return "WagonAuthority.Configure: AssetId is required.";
            if (massCapacityKg <= 0f || volumeCapacityM3 <= 0f)
                return "WagonAuthority.Configure: positive mass and volume capacities are required.";
            if (string.IsNullOrWhiteSpace(storageLocationId))
                return "WagonAuthority.Configure: a wagon needs a physical storage location.";
            wagon.Kind = string.IsNullOrWhiteSpace(wagon.Kind) ? DefaultKind : wagon.Kind;
            wagon.WagonSubtype = string.IsNullOrWhiteSpace(subtype) ? WagonSubtype.GeneralFarmFreight.ToString() : subtype;
            wagon.PayloadMassCapacityKg = massCapacityKg;
            wagon.CargoVolumeCapacityM3 = volumeCapacityM3;
            wagon.StorageLocationId = storageLocationId;
            return null;
        }

        public static bool CanCarry(EquipmentAsset wagon, float massKg, float volumeM3)
        {
            return wagon != null && wagon.IsWagon && wagon.IsUsable
                && massKg >= 0f && volumeM3 >= 0f
                && massKg <= wagon.PayloadMassCapacityKg
                && volumeM3 <= wagon.CargoVolumeCapacityM3;
        }

        public static string TryAssignDraftTeam(
            EquipmentAsset wagon,
            IList<AnimalState> horses,
            string driverPersonId,
            string shipmentId)
        {
            if (wagon == null || !wagon.IsWagon) return "WagonAuthority: a configured wagon is required.";
            if (!wagon.IsUsable) return $"WagonAuthority: wagon {wagon.AssetId} is not roadworthy.";
            if (wagon.IsTransportAssigned) return $"WagonAuthority: wagon {wagon.AssetId} is already assigned.";
            if (horses == null || horses.Count == 0) return "WagonAuthority: at least one draft horse is required.";
            if (string.IsNullOrWhiteSpace(driverPersonId)) return "WagonAuthority: a real driver PersonId is required.";
            if (string.IsNullOrWhiteSpace(shipmentId)) return "WagonAuthority: shipment id is required.";

            var assigned = new List<string>();
            foreach (AnimalState horse in horses)
            {
                if (horse == null || assigned.Contains(horse.AnimalId.ToString()))
                {
                    ReleaseDraftTeam(horses, assigned.Count);
                    return "WagonAuthority: draft team contains an invalid or duplicate horse.";
                }
                string refusal = HorseAuthority.TryReserveUse(horse, HorseUseKind.Draft, shipmentId);
                if (refusal != null)
                {
                    foreach (AnimalState prior in horses)
                        if (prior != null && assigned.Contains(prior.AnimalId.ToString())) HorseAuthority.ReleaseUse(prior);
                    return refusal;
                }
                assigned.Add(horse.AnimalId.ToString());
            }
            wagon.DraftAnimalIds = assigned;
            wagon.CurrentDriverPersonId = driverPersonId;
            wagon.CurrentShipmentId = shipmentId;
            return null;
        }

        public static string ReleaseDraftTeam(EquipmentAsset wagon, IList<AnimalState> horses)
        {
            if (wagon == null) return "WagonAuthority: no wagon supplied.";
            if (horses != null) foreach (AnimalState horse in horses) HorseAuthority.ReleaseUse(horse);
            wagon.DraftAnimalIds.Clear();
            wagon.CurrentDriverPersonId = string.Empty;
            wagon.CurrentShipmentId = string.Empty;
            return null;
        }

        private static void ReleaseDraftTeam(IList<AnimalState> horses, int count)
        {
            for (int i = 0; i < Math.Min(count, horses.Count); i++) HorseAuthority.ReleaseUse(horses[i]);
        }

        public static void ApplyTripWear(EquipmentAsset wagon, float distanceMiles, float loadRatio)
        {
            if (wagon == null || !wagon.IsWagon) return;
            float wear = Math.Max(0f, distanceMiles) * 0.0005f + Math.Max(0f, Math.Min(1f, loadRatio)) * 0.01f;
            wagon.ApplyWear(wear);
            wagon.RecordMaintenance($"Transport wear recorded for {distanceMiles:F1} miles at {loadRatio:P0} load.", -1);
        }
    }

    /// <summary>Persistent register for wagon EquipmentAssets; one AssetId has one record.</summary>
    [Serializable]
    public sealed class TransportAssetRegistry
    {
        private readonly List<EquipmentAsset> assets = new List<EquipmentAsset>();
        private readonly HorseHousingRegister horseHousing = new HorseHousingRegister();
        public IReadOnlyList<EquipmentAsset> Assets => assets;
        public HorseHousingRegister HorseHousing => horseHousing;

        public string Register(EquipmentAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId))
                return "TransportAssetRegistry.Register: a durable AssetId is required.";
            if (!asset.IsWagon) return $"TransportAssetRegistry.Register: '{asset.AssetId}' is not a wagon asset.";
            for (int i = 0; i < assets.Count; i++)
                if (assets[i] != null && assets[i].AssetId == asset.AssetId) { assets[i] = asset; return null; }
            assets.Add(asset);
            return null;
        }

        public EquipmentAsset Get(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId)) return null;
            foreach (EquipmentAsset asset in assets)
                if (asset != null && asset.AssetId == assetId) return asset;
            return null;
        }

        public TransportAssetRegistrySaveDto CaptureSaveDto()
        {
            var dto = new TransportAssetRegistrySaveDto();
            dto.wagons.AddRange(assets);
            dto.horseHousing = horseHousing.CaptureSaveDto();
            return dto;
        }

        public void LoadFromSaveDto(TransportAssetRegistrySaveDto dto)
        {
            assets.Clear();
            if (dto?.wagons == null) return;
            foreach (EquipmentAsset asset in dto.wagons)
                if (asset != null && asset.IsWagon && !string.IsNullOrWhiteSpace(asset.AssetId)) Register(asset);
            horseHousing.LoadFromSaveDto(dto.horseHousing);
        }
    }
}
