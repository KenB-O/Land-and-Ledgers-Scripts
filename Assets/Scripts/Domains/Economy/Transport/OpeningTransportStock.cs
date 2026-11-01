using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Orchestration.Systems;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    /// <summary>
    /// Seeds only authored opening transport resources for a fresh world. The resources
    /// remain ordinary Animal/Equipment authorities after this point; this class is not
    /// a second transport inventory or a gameplay shortcut.
    /// </summary>
    public static class OpeningTransportStock
    {
        public const string OpeningHousingId = "opening-livery-yard";
        public const string OpeningStorageId = "opening-freight-yard";
        // Opening-state sale terms are calibration for the authored first-town
        // transport stock. They are real purchase prices, not valuation bonuses.
        public const int OpeningHorseSalePriceCents = 20000;
        public const int OpeningWagonSalePriceCents = 25000;

        public static void Ensure(
            SimulationSystemsHub systems,
            IReadOnlyList<BusinessInstanceState> businesses,
            int dayIndex)
        {
            if (systems == null || businesses == null) return;
            BusinessInstanceState owner = FindOwner(businesses);
            if (owner == null || string.IsNullOrWhiteSpace(owner.InstanceId))
            {
                Debug.LogWarning("[WorldGenesis] Opening transport skipped: no opening business owner was available.");
                return;
            }

            if (systems.TransportAssets.HorseHousing.Capacity(OpeningHousingId) == 0)
                systems.TransportAssets.HorseHousing.AddHousing(OpeningHousingId, 4);

            AnimalState horse = FindHorse(systems.Animals);
            if (horse == null)
            {
                horse = systems.Animals.RegisterAnimal(
                    AnimalSpecies.Horse,
                    "opening-draft-horse",
                    AnimalSex.Unknown,
                    AnimalOwnerKind.Business,
                    owner.InstanceId,
                    dayIndex,
                    "authored opening freight capability");
                if (horse != null)
                {
                    string configureError = HorseAuthority.Configure(
                        horse,
                        HorseUseCapabilities.GeneralPurpose,
                        OpeningStorageId,
                        OpeningHousingId,
                        dayIndex);
                    if (configureError == null)
                    {
                        HorseAuthority.SetCustody(horse, "business", owner.InstanceId);
                        systems.TransportAssets.HorseHousing.Assign(OpeningHousingId, horse.AnimalId.ToString());
                    }
                    else
                    {
                        Debug.LogWarning($"[WorldGenesis] Opening horse configuration failed: {configureError}");
                    }
                }
                else Debug.LogWarning("[WorldGenesis] Opening horse registration failed: " + string.Join("; ", systems.Animals.Diagnostics));
            }

            if (systems.TransportAssets.Get("opening-freight-wagon") == null)
            {
                var wagon = new EquipmentAsset
                {
                    AssetId = "opening-freight-wagon",
                    Kind = WagonAuthority.DefaultKind,
                    DisplayName = "Opening farm freight wagon",
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = owner.InstanceId,
                    LocationId = OpeningStorageId,
                    StorageLocationId = OpeningStorageId,
                };
                WagonAuthority.Configure(
                    wagon,
                    WagonSubtype.GeneralFarmFreight.ToString(),
                    WagonAuthority.DefaultMassCapacityKg,
                    WagonAuthority.DefaultVolumeCapacityM3,
                    OpeningStorageId);
                systems.TransportAssets.Register(wagon);
            }
            Debug.Log($"[WorldGenesis] Opening transport ready: Horse={FindHorse(systems.Animals)?.AnimalId}, Wagon={systems.TransportAssets.Get("opening-freight-wagon")?.AssetId}, Owner={owner.InstanceId}");
        }

        private static BusinessInstanceState FindOwner(IReadOnlyList<BusinessInstanceState> businesses)
        {
            for (int i = 0; i < businesses.Count; i++)
                if (businesses[i] != null
                    && businesses[i].BusinessType == BusinessType.LiveryFreight
                    && !string.IsNullOrWhiteSpace(businesses[i].InstanceId)) return businesses[i];
            for (int i = 0; i < businesses.Count; i++)
                if (businesses[i] != null && !string.IsNullOrWhiteSpace(businesses[i].InstanceId)) return businesses[i];
            return null;
        }

        private static AnimalState FindHorse(AnimalRegistry animals)
        {
            if (animals == null) return null;
            foreach (AnimalState animal in animals.ActiveAnimals)
                if (animal != null && animal.Species == AnimalSpecies.Horse && animal.IsActive) return animal;
            return null;
        }
    }
}
