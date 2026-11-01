using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.World.Journeys
{
    /// <summary>
    /// JRN-2: a drive-time estimate — either routed through the journey model
    /// (real miles, real minutes) or honestly flagged as unavailable.
    /// </summary>
    [Serializable]
    public sealed class JourneyTravelEstimate
    {
        public bool FromModel;
        public int MinutesOneWay;
        public float Miles;
        public string Diagnostic = string.Empty;
    }

    /// <summary>
    /// JRN-2: travel integration glue. The journey model owns the math; this
    /// static helper answers the questions deliveries, freight, and work
    /// travel actually ask: how far, how long, and (for people) a real task.
    ///
    /// Location-id conventions (must match the builders):
    ///   farm "f-1" → "farmstead-f-1" (WorldLayoutBuilder.FarmsteadNode)
    ///   businesses → the JourneyLocation carrying their BusinessId.
    /// </summary>
    public static class JourneyTravel
    {
        /// <summary>TTS-2 travel task: plain travel consumes time, no skill gate.</summary>
        public const string TravelTaskId = "travel";

        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            // Travel is unskilled time: anyone can walk. The duration is the
            // journey model's whole minutes (TTS-1 quantum), set per task via
            // CreateTaskWithPlannedMinutes.
            var travel = new TaskDefinition(TravelTaskId, "Travel", 30);
            authority.RegisterDefinition(travel, out _);
        }

        /// <summary>Location id for a farm id (matches WorldLayoutBuilder.FarmsteadNode).</summary>
        public static string LocationIdForFarm(string farmId)
        {
            return "farmstead-" + (farmId ?? string.Empty).Trim();
        }

        /// <summary>Finds the journey location carrying a business id, if any.</summary>
        public static JourneyLocation FindBusinessLocation(JourneyModel model, string businessId)
        {
            if (model == null || string.IsNullOrWhiteSpace(businessId)) return null;
            foreach (var location in model.AllLocations())
            {
                if (location != null && string.Equals(location.BusinessId, businessId, StringComparison.OrdinalIgnoreCase))
                {
                    return location;
                }
            }
            return null;
        }

        /// <summary>
        /// Drive-time estimate between two location ids via the journey model
        /// (wagon mode for freight/deliveries). When the model cannot route,
        /// FromModel=false with a diagnostic — callers keep their legacy
        /// estimate honestly instead of assuming free travel.
        /// </summary>
        public static JourneyTravelEstimate EstimateDrive(
            JourneyModel model, string originLocationId, string destinationLocationId)
        {
            var estimate = new JourneyTravelEstimate();
            if (model == null)
            {
                estimate.Diagnostic = "JourneyTravel: no journey model — legacy estimate applies.";
                return estimate;
            }
            JourneyRoute route = model.FindRoute(originLocationId, destinationLocationId, TravelMode.Wagon);
            if (!route.Found)
            {
                estimate.Diagnostic = "JourneyTravel: " + route.Diagnostic;
                return estimate;
            }
            estimate.FromModel = true;
            estimate.MinutesOneWay = route.TotalMinutes;
            estimate.Miles = route.TotalMiles;
            return estimate;
        }

        /// <summary>
        /// Pass VII Workstream B (P2-9): horseback travel bound to a REAL horse.
        /// TravelMode.Horseback was a speed constant with no animal behind it
        /// (phantom horses). This plans the same journey-model route, but the
        /// trip only happens when a saddle-capable, work-ready horse is free
        /// for the whole travel window: the horse is reserved on the shared
        /// availability record (so it cannot simultaneously plow or haul) and
        /// accrues real work cost from the miles ridden. No horse, no
        /// horseback — the traveler walks or the trip is refused honestly.
        /// Returns null with a diagnostic when unroutable or horseless.
        /// </summary>
        public static WorkTask PlanMountedTravel(
            TaskAuthority authority,
            JourneyModel model,
            LandLedgers.Animals.AnimalRegistry registry,
            LandLedgers.Animals.IAnimalAvailabilityAuthority availability,
            EntityId personId,
            System.Collections.Generic.IList<LandLedgers.Primitives.EntityId> candidateHorses,
            string fromLocationId,
            string toLocationId,
            int dayIndex,
            float nowGameSeconds,
            System.Collections.Generic.List<string> diagnostics)
        {
            diagnostics ??= new System.Collections.Generic.List<string>();
            if (registry == null)
            {
                diagnostics.Add("JourneyTravel: no animal registry — horseback needs a real horse.");
                return null;
            }
            if (availability == null)
            {
                diagnostics.Add("JourneyTravel: no availability authority — the horse cannot be reserved.");
                return null;
            }
            if (candidateHorses == null || candidateHorses.Count == 0)
            {
                diagnostics.Add("JourneyTravel: no candidate horses — horseback travel refused, no phantom horse.");
                return null;
            }

            JourneyRoute route = model != null
                ? model.FindRoute(fromLocationId, toLocationId, TravelMode.Horseback)
                : null;
            if (route == null || !route.Found)
            {
                diagnostics.Add("JourneyTravel: " + (route != null ? route.Diagnostic : "no journey model — travel not planned."));
                return null;
            }
            if (route.TotalMinutes <= 0)
            {
                diagnostics.Add($"JourneyTravel: {fromLocationId} → {toLocationId} is zero travel — no task needed.");
                return null;
            }

            // Pick the first suitable AND available horse: saddle capability and
            // fitness from HorseAuthority, freedom from the availability truth.
            LandLedgers.Animals.AnimalState chosen = null;
            var skipReasons = new System.Collections.Generic.List<string>();
            float windowEnd = nowGameSeconds + route.TotalMinutes * 60f;
            foreach (LandLedgers.Primitives.EntityId horseId in candidateHorses)
            {
                LandLedgers.Animals.AnimalState horse = registry.GetAnimal(horseId);
                if (horse == null || !horse.IsActive || horse.Species != LandLedgers.Animals.AnimalSpecies.Horse)
                {
                    skipReasons.Add($"{horseId}: not an active horse");
                    continue;
                }
                if ((horse.HorseCapabilities & LandLedgers.Animals.HorseUseCapabilities.Saddle) == 0)
                {
                    skipReasons.Add($"{horseId}: not saddle-trained");
                    continue;
                }
                if (!LandLedgers.Animals.HorseAuthority.IsWorkReady(horse))
                {
                    skipReasons.Add($"{horseId}: not fit for work");
                    continue;
                }
                if (!availability.IsAvailable(horseId.ToString(), nowGameSeconds))
                {
                    skipReasons.Add($"{horseId}: already committed");
                    continue;
                }
                chosen = horse;
                break;
            }
            if (chosen == null)
            {
                diagnostics.Add(
                    "JourneyTravel: horseback travel refused — no suitable free horse (" +
                    string.Join("; ", skipReasons.ToArray()) + ").");
                return null;
            }

            string refusal = LandLedgers.Animals.HorseAuthority.TryReserveUse(
                chosen, LandLedgers.Animals.HorseUseKind.Mounted, "travel:" + personId,
                availability, nowGameSeconds, windowEnd, out _);
            if (refusal != null)
            {
                diagnostics.Add("JourneyTravel: horseback travel refused — " + refusal);
                return null;
            }

            // Real work cost from the miles ridden: the horse tires on the road.
            float intensity = Math.Max(0f, route.TotalMiles) * 0.02f;
            LandLedgers.Animals.HorseAuthority.ComputeWorkCost(chosen, intensity,
                out float fatigue01, out float nutritionCost01, out float conditionCost01);
            LandLedgers.Animals.HorseAuthority.ApplyWork(chosen, fatigue01, nutritionCost01, conditionCost01);

            WorkTask task = authority != null
                ? authority.CreateTaskWithPlannedMinutes(
                    TravelTaskId, personId, dayIndex, route.TotalMinutes,
                    customerRef: $"mounted-travel:{fromLocationId}->{toLocationId}:{chosen.AnimalId}")
                : null;
            diagnostics.Add(
                $"JourneyTravel: planned {route.TotalMinutes} min of horseback travel " +
                $"({route.TotalMiles:F1} mi) for {personId} on horse {chosen.AnimalId} " +
                $"(reserved [{nowGameSeconds:F0}-{windowEnd:F0}]s, fatigue now {chosen.Fatigue01:0.00}).");
            return task;
        }

        /// <summary>
        /// Whole days of goods aging for a trip of totalMinutes. Perishables age
        /// in transit on REAL travel time now (Tech X §8.4): a same-day haul
        /// ages ~0 days, a multi-day wagon haul ages honestly.
        /// </summary>
        public static int TransitDaysForMinutes(int totalMinutes)
        {
            if (totalMinutes <= 0) return 0;
            return (totalMinutes + 720) / 1440; // nearest whole day
        }

        /// <summary>
        /// Plans work travel for a person: a real TTS-2 travel task whose
        /// duration is the journey model's whole minutes. The task consumes the
        /// person's work-time budget like any other work — travel is honest
        /// labor time (TTS-1/2). Returns null with a diagnostic when unroutable.
        /// </summary>
        public static WorkTask PlanWorkTravel(
            TaskAuthority authority,
            JourneyModel model,
            EntityId personId,
            string fromLocationId,
            string toLocationId,
            TravelMode mode,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (authority == null)
            {
                diagnostics.Add("JourneyTravel: no task authority — travel not planned.");
                return null;
            }
            if (model == null)
            {
                diagnostics.Add("JourneyTravel: no journey model — travel not planned.");
                return null;
            }
            if (!personId.IsValid)
            {
                diagnostics.Add("JourneyTravel: no person — travel needs a traveler.");
                return null;
            }

            JourneyRoute route = model.FindRoute(fromLocationId, toLocationId, mode);
            if (!route.Found)
            {
                diagnostics.Add("JourneyTravel: " + route.Diagnostic);
                return null;
            }
            if (route.TotalMinutes <= 0)
            {
                diagnostics.Add($"JourneyTravel: {fromLocationId} → {toLocationId} is zero travel — no task needed.");
                return null;
            }

            WorkTask task = authority.CreateTaskWithPlannedMinutes(
                TravelTaskId, personId, dayIndex, route.TotalMinutes,
                customerRef: $"travel:{fromLocationId}->{toLocationId}");
            diagnostics.Add($"JourneyTravel: planned {route.TotalMinutes} min of {mode} travel " +
                $"({route.TotalMiles:F1} mi) for {personId}.");
            return task;
        }
    }
}
