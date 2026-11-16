using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// B1 (Agent B, expanded campaign 2026-11-16): kinds of legitimate
    /// sleeping place, in resolution priority order (plan §4.1). A bed
    /// sleeps one person unless its product definition says otherwise —
    /// no invented sharing. A floor place is a derived floor point inside
    /// the walkable interior for one person, always available in any
    /// occupied home.
    /// </summary>
    public enum SleepingPlaceKind
    {
        Unspecified = 0,
        Bed = 1,
        Bedroll = 2,
        Floor = 3,
    }

    /// <summary>
    /// B1: one legitimate sleeping place. Bed and bedroll places are
    /// derived from the household furnishing register (B0): a bed is an
    /// installed asset of kind furniture.bed whose binding carries the
    /// SleepingPlace capability; a bedroll is a usable portable asset of
    /// kind furniture.bedroll with no live binding. A floor place is
    /// derived per person (no asset) and is always available. The place
    /// id is stable: "bed:&lt;assetId&gt;", "bedroll:&lt;assetId&gt;",
    /// "floor:&lt;buildingInstanceId&gt;:P&lt;personId&gt;".
    /// </summary>
    [Serializable]
    public sealed class SleepingPlace
    {
        public string PlaceId = string.Empty;
        public SleepingPlaceKind Kind;
        public string AssetId = string.Empty;
        public int BuildingInstanceId;
        /// <summary>
        /// Authored activity location id on the home authority (for
        /// example "bed-1"). Empty for floor and bedroll places, which
        /// have no fixed authored location.
        /// </summary>
        public string ActivityLocationId = string.Empty;
        public float BaseRestQuality01 = 1f;
        /// <summary>B1: one person per place, always.</summary>
        public int CapacityPersons = 1;
    }

    /// <summary>
    /// B1: one night's sleeping-place reservation — one place, one
    /// person, one night. For a bed or bedroll the asset reservation
    /// (EquipmentAsset.Reserve, Tech X §3.8) is the authoritative hold;
    /// this record mirrors it so the planner and diagnostics can read
    /// the place and rest quality without parsing asset holds. The
    /// mirror is working state, not history: it is removed on waking
    /// (ReleaseSleepingPlace) or cancellation (CancelSleepingPlace).
    /// Rest quality history survives as the registry's per-person last
    /// values, which drive bed rotation.
    /// </summary>
    [Serializable]
    public sealed class SleepingPlaceReservation
    {
        public string ReservationId = string.Empty;
        public string PlaceId = string.Empty;
        public SleepingPlaceKind Kind;
        public string AssetId = string.Empty;
        public int PersonId = -1;
        public int HouseholdId = -1;
        public int DayIndex;
        public int SleepStartMinute;
        public int SleepEndMinute;
        public float RestQuality01 = 1f;
        public bool Released;
    }

    /// <summary>
    /// B1: the per-household sleeping-place registry (plan §4.1). It is
    /// ONE registry: the same registry the HouseholdShelterSuitability
    /// Evaluator reads for ProvidedSleepingPlaces. Places are derived
    /// from the household furnishing register (B0) — never invented,
    /// never a second ownership record. Resolution prefers an installed
    /// bed, then a bedroll, then the floor. No bed NEVER prevents
    /// sleeping and NEVER makes a building uninhabitable: the floor
    /// fallback always resolves.
    ///
    /// Reservation: bed and bedroll places are held through the asset's
    /// own reservation (EquipmentAsset.Reserve — no double-booking, loud
    /// refusal), so the hold survives save/load through B0's Pass 9
    /// register seam (the asset copy preserves ReservedBy/ReservedFor).
    /// After a load the driver calls RebuildFromRegister to re-mirror
    /// the surviving asset holds. Floor places are registry-local
    /// records, derived per person, so they cannot double-book by
    /// construction.
    ///
    /// Rotation (plan risk R-5): bed contention is NOT first-come-first-
    /// served forever. AssignHouseholdSleep orders members by sleep debt
    /// (worst rest quality first, then longest since a bed night, then
    /// person id), and a standalone resolution defers the bed to a
    /// needier unassigned household member. Rotation state is in-memory
    /// only in B1 — it resets on Continue (no save seam change while
    /// B0's files are under watchdog verification); the asset holds
    /// still prevent double-booking across a load, so only fairness,
    /// never correctness, resets.
    ///
    /// Rest quality (owner decision O-1, proposed defaults adopted here
    /// per the standing delegation doctrine — the owner corrects what he
    /// disagrees with): bed 1.0, bedroll 0.8, floor 0.6. In a cold
    /// season, floor sleeping in a home with no heating fuel burned that
    /// day carries a small additional penalty (0.1, additive). The
    /// caller supplies IsColdSeason and HeatingFuelBurnedToday from the
    /// heating authority — this registry reads them, never recomputes
    /// heating. No new illness or debuff system: rest quality feeds the
    /// existing work-capacity path only through
    /// DailyNeedsService.WorkCapacityFactor.
    /// </summary>
    public sealed class SleepingPlaceRegistry
    {
        /// <summary>O-1 proposed defaults (owner to correct).</summary>
        public const float BedRestQuality01 = 1.0f;
        public const float BedrollRestQuality01 = 0.8f;
        public const float FloorRestQuality01 = 0.6f;
        /// <summary>O-1: small additive penalty for an unheated cold-season floor night.</summary>
        public const float ColdSeasonFloorPenalty = 0.1f;

        public const string BedAssetKind = "furniture.bed";
        public const string BedrollAssetKind = "furniture.bedroll";

        /// <summary>
        /// Asset-hold encoding for a sleep reservation: ReservedBy is
        /// "sleep:P&lt;personId&gt;:D&lt;dayIndex&gt;", ReservedFor is
        /// "sleep &lt;placeId&gt;". The asset is the authoritative hold;
        /// the registry mirror is rebuilt from it after a load.
        /// </summary>
        public const string SleepHoldPrefix = "sleep:";

        public int HouseholdId;
        /// <summary>The household's B0 furnishing register (beds and bedrolls are derived from it). Null means floor only.</summary>
        public HouseholdFurnishingRegister Furnishings;
        /// <summary>The household's home building instance id (floor places and bedroll places are located here).</summary>
        public int HomeBuildingInstanceId = -1;

        /// <summary>Night conditions, supplied by the caller from the heating authority. Defaults mean no cold penalty.</summary>
        public bool IsColdSeason;
        public bool HeatingFuelBurnedToday = true;

        private readonly List<SleepingPlaceReservation> liveMirrors = new List<SleepingPlaceReservation>();
        private readonly Dictionary<int, float> lastRestQualityByPerson = new Dictionary<int, float>();
        private readonly Dictionary<int, int> lastBedNightByPerson = new Dictionary<int, int>();

        /// <summary>Live (unreleased) sleeping-place reservations, mirrored from the asset holds.</summary>
        public IReadOnlyList<SleepingPlaceReservation> LiveReservations => liveMirrors;

        public static string SleepHoldId(int personId, int dayIndex)
        {
            return $"sleep:P{personId}:D{dayIndex}";
        }

        /// <summary>
        /// Rest quality for a night in a place of this kind under these
        /// conditions. Bed 1.0, bedroll 0.8, floor 0.6; a cold-season
        /// floor night with no heating fuel burned that day loses a
        /// further 0.1. O-1 proposed defaults.
        /// </summary>
        public static float RestQualityFor(SleepingPlaceKind kind, bool isColdSeason, bool heatingFuelBurnedToday)
        {
            float quality;
            switch (kind)
            {
                case SleepingPlaceKind.Bed: quality = BedRestQuality01; break;
                case SleepingPlaceKind.Bedroll: quality = BedrollRestQuality01; break;
                case SleepingPlaceKind.Floor: quality = FloorRestQuality01; break;
                default: quality = 1f; break;
            }
            if (kind == SleepingPlaceKind.Floor && isColdSeason && !heatingFuelBurnedToday)
                quality -= ColdSeasonFloorPenalty;
            return Mathf.Clamp01(quality);
        }

        /// <summary>Last completed night's rest quality for a person (1.0 when the person has no recorded night — no debt).</summary>
        public float LastRestQuality01(int personId)
        {
            float quality;
            return lastRestQualityByPerson.TryGetValue(personId, out quality) ? quality : 1f;
        }

        /// <summary>Day index of the person's last bed night (-1 when the person has never slept in a bed through this registry).</summary>
        public int LastBedNightDayIndex(int personId)
        {
            int day;
            return lastBedNightByPerson.TryGetValue(personId, out day) ? day : -1;
        }

        /// <summary>The household's asset-backed sleeping places plus this person's derived floor place, in resolution order.</summary>
        public List<SleepingPlace> PlacesFor(int personId)
        {
            var places = new List<SleepingPlace>();
            places.AddRange(BedPlaces());
            places.AddRange(BedrollPlaces());
            places.Add(FloorPlaceFor(personId));
            return places;
        }

        /// <summary>The derived floor place for one person in the home. Always available — no asset, no contention.</summary>
        public SleepingPlace FloorPlaceFor(int personId)
        {
            return new SleepingPlace
            {
                PlaceId = $"floor:{HomeBuildingInstanceId}:P{personId}",
                Kind = SleepingPlaceKind.Floor,
                AssetId = string.Empty,
                BuildingInstanceId = HomeBuildingInstanceId,
                ActivityLocationId = string.Empty,
                BaseRestQuality01 = FloorRestQuality01,
                CapacityPersons = 1,
            };
        }

        /// <summary>
        /// Installed beds that are sleeping places: registered asset of
        /// kind furniture.bed, installed, usable, and bound with the
        /// SleepingPlace capability (B1's install path sets it). A bed
        /// installed without the capability is placed but is not a
        /// sleeping place yet — see Validate.
        /// </summary>
        public List<SleepingPlace> BedPlaces()
        {
            var places = new List<SleepingPlace>();
            if (Furnishings == null || Furnishings.Bindings == null) return places;
            foreach (FurnishingPlacementBinding binding in Furnishings.Bindings)
            {
                if (binding == null || binding.CapabilityKind != FurnishingCapabilityKind.SleepingPlace) continue;
                EquipmentAsset asset = Furnishings.FindAsset(binding.AssetId);
                if (asset == null || asset.Kind != BedAssetKind || !asset.IsUsable) continue;
                places.Add(new SleepingPlace
                {
                    PlaceId = "bed:" + asset.AssetId,
                    Kind = SleepingPlaceKind.Bed,
                    AssetId = asset.AssetId,
                    BuildingInstanceId = binding.BuildingInstanceId,
                    ActivityLocationId = binding.CapabilityReferenceId ?? string.Empty,
                    BaseRestQuality01 = BedRestQuality01,
                    CapacityPersons = 1,
                });
            }
            return places;
        }

        /// <summary>
        /// Portable bedrolls: usable registered assets of kind
        /// furniture.bedroll with no live binding (an installed
        /// bedroll is a fixture, not portable sleep gear).
        /// </summary>
        public List<SleepingPlace> BedrollPlaces()
        {
            var places = new List<SleepingPlace>();
            if (Furnishings == null || Furnishings.Assets == null) return places;
            foreach (EquipmentAsset asset in Furnishings.Assets)
            {
                if (asset == null || asset.Kind != BedrollAssetKind || !asset.IsUsable) continue;
                if (Furnishings.LiveBindingFor(asset.AssetId) != null) continue;
                places.Add(new SleepingPlace
                {
                    PlaceId = "bedroll:" + asset.AssetId,
                    Kind = SleepingPlaceKind.Bedroll,
                    AssetId = asset.AssetId,
                    BuildingInstanceId = HomeBuildingInstanceId,
                    ActivityLocationId = string.Empty,
                    BaseRestQuality01 = BedrollRestQuality01,
                    CapacityPersons = 1,
                });
            }
            return places;
        }

        /// <summary>
        /// Installed bed places — the count the shelter suitability
        /// evaluator reads as ProvidedSleepingPlaces. Floor places are
        /// the fallback for sleeping, not provision: a household
        /// sleeping on the floor is housed but unsuitably housed.
        /// </summary>
        public int InstalledBedPlaceCount()
        {
            return BedPlaces().Count;
        }

        /// <summary>The person's live (unreleased) reservation for a night, or null.</summary>
        public SleepingPlaceReservation LiveReservationFor(int personId, int dayIndex)
        {
            foreach (SleepingPlaceReservation mirror in liveMirrors)
            {
                if (mirror != null && !mirror.Released && mirror.PersonId == personId && mirror.DayIndex == dayIndex)
                    return mirror;
            }
            return null;
        }

        /// <summary>
        /// Resolves and reserves a sleeping place for one person for one
        /// night: best available place (owned bed first, then bedroll,
        /// then floor), the asset held for bed/bedroll, the mirror
        /// recorded. The floor fallback always resolves, so no bed never
        /// prevents sleeping. Returns null on success, else a loud
        /// refusal. Pass the household member list for rotation-aware
        /// deferral (R-5); null means first-come-first-served.
        /// </summary>
        public string TryReserveSleepingPlace(
            int personId, int dayIndex, int sleepStartMinute, int sleepEndMinute,
            IReadOnlyList<int> householdMemberIds, out SleepingPlaceReservation reservation)
        {
            reservation = null;
            if (personId < 0)
                return "SleepingPlaceRegistry.TryReserveSleepingPlace: person id is required.";
            if (dayIndex < 0)
                return "SleepingPlaceRegistry.TryReserveSleepingPlace: day index is required.";
            if (LiveReservationFor(personId, dayIndex) != null)
                return $"SleepingPlaceRegistry: P{personId} already holds a sleeping place for day {dayIndex} — one place, one person, one night.";

            SleepingPlace place = null;
            float quality;
            {
                // Missed-wake cleanup FIRST, before resolution: a live
                // mirror for an earlier night means the person slept but
                // the wake call never ran (no sleep driver exists yet —
                // that is Agent G's wiring). Close it as slept, with
                // history recorded, so tonight's resolution sees fresh
                // rest history, the asset hold can roll forward, and the
                // old mirror never orphans. Without this, a bed held
                // overnight could never be re-reserved.
                var stale = new List<SleepingPlaceReservation>();
                foreach (SleepingPlaceReservation mirror in liveMirrors)
                {
                    if (mirror != null && !mirror.Released && mirror.PersonId == personId && mirror.DayIndex < dayIndex)
                        stale.Add(mirror);
                }
                foreach (SleepingPlaceReservation mirror in stale)
                    ReleaseSleepingPlace(personId, mirror.DayIndex, out _);

                place = ResolveFor(personId, dayIndex, householdMemberIds);
                if (place == null)
                    return $"SleepingPlaceRegistry: no sleeping place could be resolved for P{personId} on day {dayIndex}.";
                quality = RestQualityFor(place.Kind, IsColdSeason, HeatingFuelBurnedToday);
            }

            if (place.Kind == SleepingPlaceKind.Bed || place.Kind == SleepingPlaceKind.Bedroll)
            {
                EquipmentAsset asset = Furnishings != null ? Furnishings.FindAsset(place.AssetId) : null;
                if (asset == null)
                    return $"SleepingPlaceRegistry: sleeping-place asset {place.AssetId} is not in household {HouseholdId}'s register.";
                if (asset.IsReserved && !IsOwnSleepHold(asset.ReservedBy, personId))
                    return $"SleepingPlaceRegistry: {place.PlaceId} is already reserved by {asset.ReservedBy} — no double-booking (Tech X §3.8).";
                if (!asset.IsReserved || asset.ReservedBy != SleepHoldId(personId, dayIndex))
                {
                    // Own hold from an earlier night: roll it forward to
                    // tonight (the same person keeps their bed). Any
                    // other hold was excluded by ResolveFor; re-checking
                    // here is the defensive second gate.
                    if (asset.IsReserved)
                        asset.Release();
                    string holdError = asset.Reserve(SleepHoldId(personId, dayIndex), "sleep " + place.PlaceId);
                    if (holdError != null)
                        return holdError;
                }
            }

            reservation = new SleepingPlaceReservation
            {
                ReservationId = Guid.NewGuid().ToString("N"),
                PlaceId = place.PlaceId,
                Kind = place.Kind,
                AssetId = place.AssetId,
                PersonId = personId,
                HouseholdId = HouseholdId,
                DayIndex = dayIndex,
                SleepStartMinute = sleepStartMinute,
                SleepEndMinute = sleepEndMinute,
                RestQuality01 = quality,
            };
            liveMirrors.Add(reservation);
            return null;
        }

        /// <summary>
        /// Assigns sleeping places for a whole household for one night,
        /// rotation-aware (R-5): members are ordered by sleep debt
        /// (worst rest quality first, then longest since a bed night,
        /// then person id) and reserve in that order, so the same person
        /// does not keep the only bed forever. Members who already hold
        /// a place for the night keep it. Returns the first refusal, or
        /// null. The floor fallback makes refusal near-impossible
        /// except for invalid inputs.
        /// </summary>
        public string AssignHouseholdSleep(
            IReadOnlyList<int> memberIds, int dayIndex,
            Func<int, int> sleepStartMinuteFor, Func<int, int> sleepEndMinuteFor)
        {
            if (memberIds == null || memberIds.Count == 0)
                return "SleepingPlaceRegistry.AssignHouseholdSleep: no household members to assign.";
            var ordered = OrderBySleepDebt(memberIds);
            foreach (int personId in ordered)
            {
                if (LiveReservationFor(personId, dayIndex) != null) continue;
                int start = sleepStartMinuteFor != null ? sleepStartMinuteFor(personId) : DailyActivityPlanner.DefaultSleepStartMinute;
                int end = sleepEndMinuteFor != null ? sleepEndMinuteFor(personId) : DailyActivityPlanner.DefaultSleepEndMinute;
                string error = TryReserveSleepingPlace(personId, dayIndex, start, end, memberIds, out _);
                if (error != null) return error;
            }
            return null;
        }

        /// <summary>
        /// R-5 sleep-debt order: worst rest quality first, then longest
        /// since a bed night, then person id. The same order assignment
        /// and the uncovered simulation both use.
        /// </summary>
        private List<int> OrderBySleepDebt(IReadOnlyList<int> memberIds)
        {
            var ordered = new List<int>(memberIds);
            ordered.Sort((a, b) =>
            {
                int byQuality = LastRestQuality01(a).CompareTo(LastRestQuality01(b));
                if (byQuality != 0) return byQuality;
                int byBedNight = LastBedNightDayIndex(a).CompareTo(LastBedNightDayIndex(b));
                if (byBedNight != 0) return byBedNight;
                return a.CompareTo(b);
            });
            return ordered;
        }

        /// <summary>
        /// Waking: releases the night's place (the asset hold for a bed
        /// or bedroll, the mirror record for any kind) and records the
        /// night's rest quality and bed use for rotation. Returns true
        /// when a live reservation was released; false (rest quality
        /// 1.0) when the person held no place for that night.
        /// </summary>
        public bool ReleaseSleepingPlace(int personId, int dayIndex, out float restQuality01)
        {
            restQuality01 = 1f;
            SleepingPlaceReservation live = LiveReservationFor(personId, dayIndex);
            if (live == null) return false;
            restQuality01 = live.RestQuality01;
            RemoveMirror(live, recordHistory: true);
            return true;
        }

        /// <summary>
        /// Cancellation (not waking): releases the night's place without
        /// recording any rest history — used when the sleep time itself
        /// could not be reserved, so the place hold must not stand and
        /// must not count as a night slept. Returns true when a live
        /// reservation was cancelled.
        /// </summary>
        public bool CancelSleepingPlace(int personId, int dayIndex)
        {
            SleepingPlaceReservation live = LiveReservationFor(personId, dayIndex);
            if (live == null) return false;
            RemoveMirror(live, recordHistory: false);
            return true;
        }

        /// <summary>
        /// Releases every live sleeping place held by a person (any
        /// night) — departure/death cleanup so a stale hold never
        /// blocks a bed forever. Returns the number released.
        /// </summary>
        public int ReleaseAllForPerson(int personId)
        {
            int released = 0;
            var held = new List<SleepingPlaceReservation>();
            foreach (SleepingPlaceReservation mirror in liveMirrors)
            {
                if (mirror != null && !mirror.Released && mirror.PersonId == personId)
                    held.Add(mirror);
            }
            foreach (SleepingPlaceReservation mirror in held)
            {
                RemoveMirror(mirror, recordHistory: false);
                released++;
            }
            return released;
        }

        /// <summary>
        /// Members whose sleeping place for the night is the floor —
        /// the uncovered sleepers whose furnishing need (B6) is a bed.
        /// Simulates tonight's assignment in sleep-debt order (the same
        /// order AssignHouseholdSleep uses), tracking tentatively-taken
        /// places, so the answer reflects the household's real scarcity
        /// instead of resolving every member against the same free bed.
        /// Members who already hold a live reservation are judged by
        /// it. Peek only: resolves without reserving.
        /// </summary>
        public List<int> UncoveredSleepers(IReadOnlyList<int> memberIds, int dayIndex)
        {
            var uncovered = new List<int>();
            if (memberIds == null) return uncovered;
            var tentativelyTaken = new HashSet<string>(StringComparer.Ordinal);
            foreach (int personId in OrderBySleepDebt(memberIds))
            {
                SleepingPlaceReservation live = LiveReservationFor(personId, dayIndex);
                if (live != null)
                {
                    if (live.Kind == SleepingPlaceKind.Floor) uncovered.Add(personId);
                    continue;
                }
                SleepingPlace place = ResolveFor(personId, dayIndex, memberIds, tentativelyTaken);
                if (place != null && place.Kind == SleepingPlaceKind.Floor) uncovered.Add(personId);
                if (place != null && place.Kind != SleepingPlaceKind.Floor)
                    tentativelyTaken.Add(place.PlaceId);
            }
            return uncovered;
        }

        /// <summary>
        /// Rebuilds the mirror from the register's surviving asset holds
        /// after a load (the Pass 9 seam persists the asset reservation;
        /// the mirror is working state). The driver calls this after
        /// restoring a household's register. A hold that cannot be
        /// parsed, or whose place no longer derives, is skipped — the
        /// asset hold itself still blocks double-booking, and Validate
        /// reports the gap. Rest quality is recomputed from the current
        /// night conditions; a restore between reservation and waking
        /// therefore records the current conditions, not the original
        /// night's — the mirror window is likewise the default sleep
        /// window, since the hold encodes person and day only.
        /// </summary>
        public void RebuildFromRegister()
        {
            if (Furnishings == null || Furnishings.Assets == null) return;
            foreach (EquipmentAsset asset in Furnishings.Assets)
            {
                if (asset == null || !asset.IsReserved) continue;
                if (!TryParseSleepHold(asset.ReservedBy, out int personId, out int dayIndex)) continue;
                if (LiveReservationFor(personId, dayIndex) != null) continue;
                string placeId = string.Empty;
                if (!string.IsNullOrWhiteSpace(asset.ReservedFor)
                    && asset.ReservedFor.StartsWith("sleep ", StringComparison.Ordinal))
                    placeId = asset.ReservedFor.Substring("sleep ".Length);
                SleepingPlaceKind kind = KindFromPlaceId(placeId);
                if (kind == SleepingPlaceKind.Unspecified) continue;
                liveMirrors.Add(new SleepingPlaceReservation
                {
                    ReservationId = Guid.NewGuid().ToString("N"),
                    PlaceId = placeId,
                    Kind = kind,
                    AssetId = kind == SleepingPlaceKind.Bed || kind == SleepingPlaceKind.Bedroll ? asset.AssetId : string.Empty,
                    PersonId = personId,
                    HouseholdId = HouseholdId,
                    DayIndex = dayIndex,
                    SleepStartMinute = DailyActivityPlanner.DefaultSleepStartMinute,
                    SleepEndMinute = DailyActivityPlanner.DefaultSleepEndMinute,
                    RestQuality01 = RestQualityFor(kind, IsColdSeason, HeatingFuelBurnedToday),
                });
            }
        }

        /// <summary>
        /// Structural diagnostics: no furnishing register; bed assets
        /// installed without the SleepingPlace capability; mirrors
        /// referencing places that no longer derive; mirrors whose asset
        /// hold is gone; duplicate live mirrors for one person+night;
        /// two persons holding one place for one night. Empty means the
        /// registry is internally consistent.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Furnishings == null)
            {
                problems.Add($"SleepingPlaceRegistry {HouseholdId}: no furnishing register — no bed or bedroll places can be derived; floor only.");
                return problems;
            }
            if (Furnishings.Bindings != null)
            {
                foreach (FurnishingPlacementBinding binding in Furnishings.Bindings)
                {
                    if (binding == null) continue;
                    EquipmentAsset asset = Furnishings.FindAsset(binding.AssetId);
                    if (asset != null && asset.Kind == BedAssetKind
                        && binding.CapabilityKind != FurnishingCapabilityKind.SleepingPlace)
                        problems.Add($"SleepingPlaceRegistry {HouseholdId}: installed bed {asset.AssetId} has no SleepingPlace capability — it is placed but is not a sleeping place yet.");
                }
            }
            var seenPersonNight = new HashSet<string>(StringComparer.Ordinal);
            var seenPlaceNight = new HashSet<string>(StringComparer.Ordinal);
            foreach (SleepingPlaceReservation mirror in liveMirrors)
            {
                if (mirror == null) { problems.Add($"SleepingPlaceRegistry {HouseholdId}: null live reservation entry."); continue; }
                if (mirror.Released)
                {
                    problems.Add($"SleepingPlaceRegistry {HouseholdId}: released reservation {mirror.ReservationId} is still in the live list.");
                    continue;
                }
                string personNight = $"{mirror.PersonId}:{mirror.DayIndex}";
                if (!seenPersonNight.Add(personNight))
                    problems.Add($"SleepingPlaceRegistry {HouseholdId}: P{mirror.PersonId} holds more than one sleeping place for day {mirror.DayIndex}.");
                string placeNight = $"{mirror.PlaceId}:{mirror.DayIndex}";
                if (!seenPlaceNight.Add(placeNight))
                    problems.Add($"SleepingPlaceRegistry {HouseholdId}: {mirror.PlaceId} is held by more than one person for day {mirror.DayIndex} — no double-booking.");
                if (mirror.Kind == SleepingPlaceKind.Bed || mirror.Kind == SleepingPlaceKind.Bedroll)
                {
                    EquipmentAsset asset = Furnishings.FindAsset(mirror.AssetId);
                    if (asset == null)
                        problems.Add($"SleepingPlaceRegistry {HouseholdId}: reservation {mirror.ReservationId} references asset {mirror.AssetId}, which is not in this register.");
                    else if (!asset.IsReserved || asset.ReservedBy != SleepHoldId(mirror.PersonId, mirror.DayIndex))
                        problems.Add($"SleepingPlaceRegistry {HouseholdId}: reservation {mirror.ReservationId} for {mirror.PlaceId} has no matching asset hold — mirror without hold.");
                }
            }
            return problems;
        }

        // ---------- internals ----------

        private void RemoveMirror(SleepingPlaceReservation mirror, bool recordHistory)
        {
            if (mirror == null) return;
            mirror.Released = true;
            liveMirrors.Remove(mirror);
            if ((mirror.Kind == SleepingPlaceKind.Bed || mirror.Kind == SleepingPlaceKind.Bedroll) && Furnishings != null)
            {
                EquipmentAsset asset = Furnishings.FindAsset(mirror.AssetId);
                if (asset != null && asset.IsReserved && asset.ReservedBy == SleepHoldId(mirror.PersonId, mirror.DayIndex))
                    asset.Release();
            }
            if (recordHistory)
            {
                lastRestQualityByPerson[mirror.PersonId] = mirror.RestQuality01;
                if (mirror.Kind == SleepingPlaceKind.Bed)
                    lastBedNightByPerson[mirror.PersonId] = mirror.DayIndex;
            }
        }

        /// <summary>
        /// Pure resolution (no reservation): best available place for
        /// one person tonight. Beds first, then bedrolls, then the
        /// floor — which always resolves. The tentative set (used by
        /// the uncovered simulation) marks places already spoken for
        /// in the simulation, so one bed is not promised to two people.
        /// </summary>
        private SleepingPlace ResolveFor(int personId, int dayIndex, IReadOnlyList<int> householdMemberIds, HashSet<string> tentativelyTaken = null)
        {
            foreach (SleepingPlace bed in BedPlaces())
            {
                if (!IsPlaceFreeTonight(bed, personId, dayIndex, tentativelyTaken)) continue;
                if (householdMemberIds != null && IsNeedierMemberUnassigned(personId, dayIndex, householdMemberIds)) continue;
                return bed;
            }
            foreach (SleepingPlace roll in BedrollPlaces())
            {
                if (!IsPlaceFreeTonight(roll, personId, dayIndex, tentativelyTaken)) continue;
                return roll;
            }
            return FloorPlaceFor(personId);
        }

        private bool IsPlaceFreeTonight(SleepingPlace place, int personId, int dayIndex, HashSet<string> tentativelyTaken = null)
        {
            if (place == null) return false;
            if (tentativelyTaken != null && tentativelyTaken.Contains(place.PlaceId)) return false;
            foreach (SleepingPlaceReservation mirror in liveMirrors)
            {
                if (mirror == null || mirror.Released) continue;
                if (mirror.PlaceId == place.PlaceId && mirror.DayIndex == dayIndex && mirror.PersonId != personId)
                    return false;
            }
            if ((place.Kind == SleepingPlaceKind.Bed || place.Kind == SleepingPlaceKind.Bedroll) && Furnishings != null)
            {
                EquipmentAsset asset = Furnishings.FindAsset(place.AssetId);
                if (asset != null && asset.IsReserved && !IsOwnSleepHold(asset.ReservedBy, personId))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// R-5 deferral: when another unassigned household member has
        /// strictly worse sleep debt (lower last rest quality, or equal
        /// quality and an older last bed night), this person leaves the
        /// bed for them and takes the next-best place. The planner's
        /// id order must not decide who keeps the only bed forever.
        /// </summary>
        private bool IsNeedierMemberUnassigned(int personId, int dayIndex, IReadOnlyList<int> householdMemberIds)
        {
            float myQuality = LastRestQuality01(personId);
            int myBedNight = LastBedNightDayIndex(personId);
            foreach (int otherId in householdMemberIds)
            {
                if (otherId == personId) continue;
                if (LiveReservationFor(otherId, dayIndex) != null) continue;
                float otherQuality = LastRestQuality01(otherId);
                int otherBedNight = LastBedNightDayIndex(otherId);
                if (otherQuality < myQuality) return true;
                if (otherQuality == myQuality && otherBedNight < myBedNight) return true;
            }
            return false;
        }

        private static bool IsOwnSleepHold(string reservedBy, int personId)
        {
            if (string.IsNullOrWhiteSpace(reservedBy)) return false;
            return reservedBy.StartsWith($"sleep:P{personId}:", StringComparison.Ordinal);
        }

        private static bool TryParseSleepHold(string reservedBy, out int personId, out int dayIndex)
        {
            personId = -1;
            dayIndex = -1;
            if (string.IsNullOrWhiteSpace(reservedBy)) return false;
            // "sleep:P12:D100"
            string[] parts = reservedBy.Split(':');
            if (parts.Length != 3 || parts[0] != "sleep") return false;
            if (!parts[1].StartsWith("P", StringComparison.Ordinal)
                || !int.TryParse(parts[1].Substring(1), out personId)) return false;
            if (!parts[2].StartsWith("D", StringComparison.Ordinal)
                || !int.TryParse(parts[2].Substring(1), out dayIndex)) return false;
            return personId >= 0 && dayIndex >= 0;
        }

        private static SleepingPlaceKind KindFromPlaceId(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId)) return SleepingPlaceKind.Unspecified;
            if (placeId.StartsWith("bed:", StringComparison.Ordinal)) return SleepingPlaceKind.Bed;
            if (placeId.StartsWith("bedroll:", StringComparison.Ordinal)) return SleepingPlaceKind.Bedroll;
            if (placeId.StartsWith("floor:", StringComparison.Ordinal)) return SleepingPlaceKind.Floor;
            return SleepingPlaceKind.Unspecified;
        }
    }

    /// <summary>
    /// B1: the physical-arrival contract for a sleeping place (plan
    /// §4.2). Proves a place is reachable through the existing interior
    /// route engine — the same engine PopulationPathingDirector uses for
    /// domestic activities — without duplicating the mover. A bed with
    /// an authored activity location must resolve and have an interior
    /// route from "main-entrance"; a bed with no authored location is
    /// refused loudly (Codex authors bed-1 per plan §9.2 instruction
    /// 2); a floor or bedroll place validates as standing room inside
    /// the home (the main-entrance indoor point must be walkable). This
    /// is the check the runtime sleep driver calls before moving the
    /// person; the per-tick movement itself is Agent G's wiring (no
    /// sleep driver exists today — sleep is a planner reservation only).
    /// Codex owns the visual pose anchor on arrival.
    /// </summary>
    public static class SleepingPlaceArrival
    {
        public const string MainEntranceId = "main-entrance";

        /// <summary>Returns null when the place is reachable, else a loud reason naming the failure.</summary>
        public static string TryValidateArrivalPath(SleepingPlace place, BuildingFootprintAuthority authority)
        {
            if (place == null)
                return "SleepingPlaceArrival: no sleeping place to validate.";
            if (authority == null)
                return "SleepingPlaceArrival: no building authority — the arrival path cannot be validated.";
            if (place.Kind == SleepingPlaceKind.Bed && !string.IsNullOrWhiteSpace(place.ActivityLocationId))
            {
                if (!authority.TryGetActivityLocation(place.ActivityLocationId, place.BuildingInstanceId,
                        out ResolvedBuildingActivityLocation activity, out string reason))
                    return $"SleepingPlaceArrival: {place.PlaceId} activity location '{place.ActivityLocationId}' did not resolve — {reason}";
                if (activity.RequiresWalkableInterior
                    && !authority.TryBuildInteriorActivityRoute(MainEntranceId, place.ActivityLocationId, out _, out reason))
                    return $"SleepingPlaceArrival: no interior route from {MainEntranceId} to {place.PlaceId} — {reason}";
                return null;
            }
            if (place.Kind == SleepingPlaceKind.Bed)
                return $"SleepingPlaceArrival: {place.PlaceId} has no authored bed activity location — author bed-1 on the home authority (plan §9.2 instruction 2) before the sleeper can reach it.";
            // Floor and bedroll: the sleeper stands on the floor inside
            // the home. The main-entrance indoor point must be walkable.
            if (!authority.TryGetEntrancePair(MainEntranceId, out BuildingEntrancePairAuthoring entrance, out string entranceReason))
                return $"SleepingPlaceArrival: {place.PlaceId} has no {MainEntranceId} to enter through — {entranceReason}";
            if (!authority.TryEvaluateInteriorLocalPoint(entrance.indoorPositionLocal, entrance.floorIndex, entrance.functionalSpaceId, out string pointReason))
                return $"SleepingPlaceArrival: {place.PlaceId} entrance indoor point is not walkable — {pointReason}";
            return null;
        }
    }
}
