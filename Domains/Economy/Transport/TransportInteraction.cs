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

        public static string Mount(
            AnimalState horse,
            string personId,
            float distanceMiles,
            string locationId,
            out TransportActivity activity)
        {
            activity = null;
            if (string.IsNullOrWhiteSpace(personId)) return "TransportInteraction.Mount: a real PersonId is required.";
            if (distanceMiles > InteractionDistanceMiles) return "TransportInteraction.Mount: Person is not close enough to the Horse.";
            string refusal = HorseAuthority.TryReserveUse(horse, HorseUseKind.Mounted, "mount:" + personId);
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
            out TransportActivity activity)
        {
            activity = null;
            string refusal = WagonAuthority.TryAssignDraftTeam(wagon, draftHorses, driverPersonId, shipmentId);
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

        public static string Dismount(AnimalState horse, TransportActivity activity, string locationId)
        {
            if (activity == null || activity.Kind != TransportActivityKind.MountedHorse)
                return "TransportInteraction.Dismount: no mounted activity supplied.";
            if (horse == null || horse.AnimalId.ToString() != activity.HorseId)
                return "TransportInteraction.Dismount: activity does not reference this Horse.";
            HorseAuthority.ReleaseUse(horse);
            horse.PhysicalLocationId = locationId ?? horse.PhysicalLocationId ?? string.Empty;
            activity.LocationId = horse.PhysicalLocationId;
            activity.IsActive = false;
            return null;
        }

        public static string FinishDriving(EquipmentAsset wagon, IList<AnimalState> draftHorses, TransportActivity activity, float distanceMiles, float loadRatio)
        {
            if (activity == null || activity.Kind != TransportActivityKind.DrivingWagon)
                return "TransportInteraction.FinishDriving: no driving activity supplied.";
            if (wagon == null || wagon.AssetId != activity.WagonAssetId)
                return "TransportInteraction.FinishDriving: activity does not reference this Wagon.";
            WagonAuthority.ReleaseDraftTeam(wagon, draftHorses);
            WagonAuthority.ApplyTripWear(wagon, distanceMiles, loadRatio);
            activity.IsActive = false;
            return null;
        }
    }
}
