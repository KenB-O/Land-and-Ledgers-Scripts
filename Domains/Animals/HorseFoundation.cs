using System;
using System.Collections.Generic;

namespace LandLedgers.Animals
{
    [Flags]
    public enum HorseUseCapabilities
    {
        None = 0,
        Saddle = 1,
        Draft = 2,
        GeneralPurpose = Saddle | Draft,
    }

    public enum HorseUseKind
    {
        None = 0,
        Mounted = 1,
        Draft = 2,
        Care = 3,
    }

    /// <summary>
    /// The horse-specific seam on the individual Animal authority. A horse remains an
    /// AnimalState with one permanent AnimalId; this service only enforces transport
    /// reservations, care state, custody, and housing invariants.
    /// </summary>
    public static class HorseAuthority
    {
        public static string Configure(
            AnimalState horse,
            HorseUseCapabilities capabilities,
            string locationId,
            string housingId,
            int dayIndex)
        {
            string error = ValidateHorse(horse);
            if (error != null) return error;
            if (capabilities == HorseUseCapabilities.None)
                return "HorseAuthority.Configure: a horse needs at least one supported use.";
            if (string.IsNullOrWhiteSpace(housingId))
                return "HorseAuthority.Configure: a working horse needs a named housing arrangement.";

            horse.HorseCapabilities = capabilities;
            horse.PhysicalLocationId = locationId ?? string.Empty;
            horse.HousingId = housingId;
            horse.LastCareDayIndex = dayIndex;
            return null;
        }

        public static string SetCustody(AnimalState horse, string kind, string id)
        {
            string error = ValidateHorse(horse);
            if (error != null) return error;
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
                return "HorseAuthority.SetCustody: custodian kind and id are required.";
            horse.CustodianKind = kind;
            horse.CustodianId = id;
            return null;
        }

        public static string SetHousing(AnimalState horse, string housingId, string locationId)
        {
            string error = ValidateHorse(horse);
            if (error != null) return error;
            if (string.IsNullOrWhiteSpace(housingId))
                return "HorseAuthority.SetHousing: housing id is required.";
            horse.HousingId = housingId;
            horse.PhysicalLocationId = locationId ?? horse.PhysicalLocationId ?? string.Empty;
            return null;
        }

        public static string TryReserveUse(AnimalState horse, HorseUseKind use, string assignmentId)
        {
            string error = ValidateHorse(horse);
            if (error != null) return error;
            if (string.IsNullOrWhiteSpace(assignmentId))
                return "HorseAuthority.TryReserveUse: assignment id is required.";
            if (horse.CurrentUse != HorseUseKind.None || !string.IsNullOrWhiteSpace(horse.CurrentAssignmentId))
                return $"HorseAuthority.TryReserveUse: horse {horse.AnimalId} is already assigned.";

            HorseUseCapabilities required = use == HorseUseKind.Mounted
                ? HorseUseCapabilities.Saddle
                : use == HorseUseKind.Draft ? HorseUseCapabilities.Draft : HorseUseCapabilities.None;
            if (required != HorseUseCapabilities.None && (horse.HorseCapabilities & required) == 0)
                return $"HorseAuthority.TryReserveUse: horse {horse.AnimalId} lacks {use} capability.";
            if (!IsWorkReady(horse))
                return $"HorseAuthority.TryReserveUse: horse {horse.AnimalId} is not fit for work.";

            horse.CurrentUse = use;
            horse.CurrentAssignmentId = assignmentId;
            return null;
        }

        public static void ReleaseUse(AnimalState horse)
        {
            if (horse == null) return;
            horse.CurrentUse = HorseUseKind.None;
            horse.CurrentAssignmentId = string.Empty;
        }

        public static bool IsWorkReady(AnimalState horse)
        {
            return horse != null && horse.IsActive && horse.Species == AnimalSpecies.Horse
                && horse.Health01 >= 0.35f && horse.Nutrition01 >= 0.35f
                && horse.Hydration01 >= 0.35f && horse.Fatigue01 <= 0.85f
                && !string.IsNullOrWhiteSpace(horse.HousingId);
        }

        public static void ApplyCare(AnimalState horse, float feed01, float water01, float rest01, int dayIndex)
        {
            if (horse == null || horse.Species != AnimalSpecies.Horse) return;
            horse.Nutrition01 = Clamp01(horse.Nutrition01 + Math.Max(0f, feed01));
            horse.Hydration01 = Clamp01(horse.Hydration01 + Math.Max(0f, water01));
            horse.Fatigue01 = Clamp01(horse.Fatigue01 - Math.Max(0f, rest01));
            horse.LastCareDayIndex = dayIndex;
        }

        public static void ApplyWork(AnimalState horse, float fatigue01, float nutritionCost01, float conditionCost01)
        {
            if (horse == null || horse.Species != AnimalSpecies.Horse) return;
            horse.Fatigue01 = Clamp01(horse.Fatigue01 + Math.Max(0f, fatigue01));
            horse.Nutrition01 = Clamp01(horse.Nutrition01 - Math.Max(0f, nutritionCost01));
            horse.Health01 = Clamp01(horse.Health01 - Math.Max(0f, conditionCost01));
        }

        private static string ValidateHorse(AnimalState horse)
        {
            if (horse == null) return "HorseAuthority: no horse supplied.";
            if (!horse.AnimalId.IsValid || horse.Species != AnimalSpecies.Horse)
                return "HorseAuthority: the supplied animal is not a valid Horse authority.";
            if (!horse.IsActive) return $"HorseAuthority: horse {horse.AnimalId} is inactive.";
            return null;
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }

    /// <summary>Capacity-enforced authored stable/livery allocation.</summary>
    public sealed class HorseHousingRegister
    {
        private readonly Dictionary<string, List<string>> occupants =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> capacities =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public string AddHousing(string housingId, int capacity)
        {
            if (string.IsNullOrWhiteSpace(housingId) || capacity <= 0)
                return "HorseHousingRegister: housing id and positive capacity are required.";
            if (capacities.ContainsKey(housingId)) return $"HorseHousingRegister: '{housingId}' already exists.";
            capacities[housingId] = capacity;
            occupants[housingId] = new List<string>();
            return null;
        }

        public string Assign(string housingId, string animalId)
        {
            if (!capacities.ContainsKey(housingId)) return $"HorseHousingRegister: unknown housing '{housingId}'.";
            if (string.IsNullOrWhiteSpace(animalId)) return "HorseHousingRegister: animal id is required.";
            foreach (List<string> list in occupants.Values) if (list.Contains(animalId)) return $"HorseHousingRegister: animal '{animalId}' is already housed.";
            List<string> housed = occupants[housingId];
            if (housed.Count >= capacities[housingId]) return $"HorseHousingRegister: housing '{housingId}' is full.";
            housed.Add(animalId);
            return null;
        }

        public bool Release(string housingId, string animalId) => occupants.TryGetValue(housingId, out List<string> list) && list.Remove(animalId);
        public int Occupied(string housingId) => occupants.TryGetValue(housingId, out List<string> list) ? list.Count : 0;
        public int Capacity(string housingId) => capacities.TryGetValue(housingId, out int capacity) ? capacity : 0;

        public HorseHousingSaveDto CaptureSaveDto()
        {
            var dto = new HorseHousingSaveDto();
            foreach (KeyValuePair<string, int> entry in capacities)
            {
                var state = new HorseHousingState { HousingId = entry.Key, Capacity = entry.Value };
                if (occupants.TryGetValue(entry.Key, out List<string> housed))
                    state.AnimalIds.AddRange(housed);
                dto.housing.Add(state);
            }
            return dto;
        }

        public void LoadFromSaveDto(HorseHousingSaveDto dto)
        {
            occupants.Clear();
            capacities.Clear();
            if (dto?.housing == null) return;
            foreach (HorseHousingState state in dto.housing)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.HousingId) || state.Capacity <= 0) continue;
                capacities[state.HousingId] = state.Capacity;
                occupants[state.HousingId] = new List<string>(state.AnimalIds ?? new List<string>());
            }
        }
    }

    [Serializable]
    public sealed class HorseHousingState
    {
        public string HousingId = string.Empty;
        public int Capacity;
        public List<string> AnimalIds = new List<string>();
    }

    [Serializable]
    public sealed class HorseHousingSaveDto
    {
        public List<HorseHousingState> housing = new List<HorseHousingState>();
    }
}
