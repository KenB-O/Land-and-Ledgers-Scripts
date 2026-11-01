using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;

namespace LandLedgers.Economy.Transport
{
    public enum TransportActivityKind
    {
        MountedHorse = 1,
        DrivingWagon = 2,
    }

    [Serializable]
    public sealed class TransportActivity
    {
        public TransportActivityKind Kind;
        public string PersonId = string.Empty;
        public string HorseId = string.Empty;
        public string WagonAssetId = string.Empty;
        public string ShipmentId = string.Empty;
        public string LocationId = string.Empty;
        public bool IsActive = true;
    }

    /// <summary>
    /// Shared player/NPC interaction seam. A caller supplies the real Person identity and
    /// proximity; this authority reserves/release the same Horse/Wagon resources for either.
    /// Scene placement is handled by HorseWorldView/WagonWorldView.
    /// </summary>
    public static class TransportInteraction
    {
        public const float InteractionDistanceMiles = 0.05f;

        /// <summary>
        /// Pass VII Workstream B (P1-4): mounting reserves the horse on the
        /// shared availability record until dismount — a horse plowing, rented,
        /// or hauling refuses the saddle on the same record.
        /// </summary>
        public static string Mount(
            AnimalState horse,
            string personId,
            float distanceMiles,
            string locationId,
            IAnimalAvailabilityAuthority availability,
            float nowGameSeconds,
            out TransportActivity activity)
        {
            activity = null;
            if (string.IsNullOrWhiteSpace(personId)) return "TransportInteraction.Mount: a real PersonId is required.";
            if (distanceMiles > InteractionDistanceMiles) return "TransportInteraction.Mount: Person is not close enough to the Horse.";
            string refusal = HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:" + personId, availability,
                nowGameSeconds, float.MaxValue, out _);
            if (refusal != null) return refusal;
            activity = new TransportActivity
            {
                Kind = TransportActivityKind.MountedHorse,
                PersonId = personId,
                HorseId = horse.AnimalId.ToString(),
                LocationId = locationId ?? string.Empty,
            };
            horse.PhysicalLocationId = locationId ?? horse.PhysicalLocationId ?? string.Empty;
            return null;
        }

        public static string Drive(
            EquipmentAsset wagon,
            IList<AnimalState> draftHorses,
            string driverPersonId,
            string shipmentId,
            string locationId,
            IAnimalAvailabilityAuthority availability,
            float fromGameSeconds,
            float toGameSeconds,
            out TransportActivity activity)
        {
            activity = null;
            string refusal = WagonAuthority.TryAssignDraftTeam(
                wagon, draftHorses, driverPersonId, shipmentId,
                availability, fromGameSeconds, toGameSeconds);
            if (refusal != null) return refusal;
            activity = new TransportActivity
            {
                Kind = TransportActivityKind.DrivingWagon,
                PersonId = driverPersonId ?? string.Empty,
                WagonAssetId = wagon.AssetId,
                ShipmentId = shipmentId ?? string.Empty,
                LocationId = locationId ?? string.Empty,
            };
            wagon.StorageLocationId = locationId ?? wagon.StorageLocationId ?? string.Empty;
            return null;
        }

        public static string Dismount(
            AnimalState horse, TransportActivity activity, string locationId,
            IAnimalAvailabilityAuthority availability)
        {
            if (activity == null || activity.Kind != TransportActivityKind.MountedHorse)
                return "TransportInteraction.Dismount: no mounted activity supplied.";
            if (horse == null || horse.AnimalId.ToString() != activity.HorseId)
                return "TransportInteraction.Dismount: activity does not reference this Horse.";
            HorseAuthority.ReleaseUse(horse, availability);
            horse.PhysicalLocationId = locationId ?? horse.PhysicalLocationId ?? string.Empty;
            activity.LocationId = horse.PhysicalLocationId;
            activity.IsActive = false;
            return null;
        }

        public static string FinishDriving(
            EquipmentAsset wagon, IList<AnimalState> draftHorses, TransportActivity activity,
            float distanceMiles, float loadRatio, IAnimalAvailabilityAuthority availability)
        {
            if (activity == null || activity.Kind != TransportActivityKind.DrivingWagon)
                return "TransportInteraction.FinishDriving: no driving activity supplied.";
            if (wagon == null || wagon.AssetId != activity.WagonAssetId)
                return "TransportInteraction.FinishDriving: activity does not reference this Wagon.";
            // Real horse cost for the trip: intensity from miles and load, state
            // from each horse — no flat wear, no zero-cost hauling.
            float intensity = Math.Max(0f, distanceMiles) * 0.01f * (0.5f + Math.Max(0f, Math.Min(1f, loadRatio)));
            if (draftHorses != null)
            {
                foreach (AnimalState horse in draftHorses)
                {
                    if (horse == null) continue;
                    HorseAuthority.ComputeWorkCost(horse, intensity,
                        out float fatigue01, out float nutritionCost01, out float conditionCost01);
                    HorseAuthority.ApplyWork(horse, fatigue01, nutritionCost01, conditionCost01);
                }
            }
            WagonAuthority.ReleaseDraftTeam(wagon, draftHorses, availability);
            WagonAuthority.ApplyTripWear(wagon, distanceMiles, loadRatio);
            activity.IsActive = false;
            return null;
        }
    }
}
