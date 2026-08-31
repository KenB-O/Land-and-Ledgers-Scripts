using System;
using UnityEngine;

namespace LandLedgers.Population
{
    public enum NewcomerArrivalProfile
    {
        SettledResident = 0,
        LoneLaborer = 1,
        KinLinkedArrival = 2,
        BoarderSeekingWorker = 3,
        RenterReadyHousehold = 4,
        SkilledCapitalizedArrival = 5,
        DistressedRelocationHousehold = 6,
        WidowElderDependentRelocation = 7
    }

    public enum SettlementArrangement
    {
        StableHousehold = 0,
        Boarding = 1,
        Renting = 2,
        Kin = 3,
        Transient = 4,
        Departed = 5
    }

    public enum HouseholdDwellingKind
    {
        OwnedHome = 0,
        RentedHomeOrRoom = 1,
        BoardingHouseLodging = 2,
        KinSharedHousehold = 3,
        RoughTemporaryLodging = 4,
        Departed = 5
    }

    [Serializable]
    public struct SettlementPressureState
    {
        public bool initialized;
        public float stability01;
        public float housingFriction01;
        public float laborReadiness01;
        public int weeksInArrangement;
        public float homeAmbition01;
        public string summary;

        public static SettlementPressureState Create(
            SettlementArrangement arrangement,
            float stability01,
            float housingFriction01,
            float laborReadiness01,
            int weeksInArrangement,
            float homeAmbition01,
            string summary)
        {
            return new SettlementPressureState
            {
                initialized = true,
                stability01 = Mathf.Clamp01(stability01),
                housingFriction01 = Mathf.Clamp01(housingFriction01),
                laborReadiness01 = Mathf.Clamp01(laborReadiness01),
                weeksInArrangement = Mathf.Max(0, weeksInArrangement),
                homeAmbition01 = Mathf.Clamp01(homeAmbition01),
                summary = string.IsNullOrWhiteSpace(summary) ? GetArrangementLabel(arrangement) : summary
            };
        }

        public void EnsureInitialized(SettlementArrangement arrangement)
        {
            if (!initialized)
            {
                this = Create(
                    arrangement,
                    NewcomerSettlementEvaluator.GetDefaultSettlementStability(arrangement),
                    NewcomerSettlementEvaluator.GetDefaultHousingFriction(arrangement),
                    NewcomerSettlementEvaluator.GetDefaultLaborReadiness(arrangement),
                    0,
                    NewcomerSettlementEvaluator.GetDefaultHomeAmbition(arrangement),
                    GetArrangementLabel(arrangement));
                return;
            }

            stability01 = Mathf.Clamp01(stability01);
            housingFriction01 = Mathf.Clamp01(housingFriction01);
            laborReadiness01 = Mathf.Clamp01(laborReadiness01);
            weeksInArrangement = Mathf.Max(0, weeksInArrangement);
            homeAmbition01 = Mathf.Clamp01(homeAmbition01);
            summary ??= GetArrangementLabel(arrangement);
        }

        private static string GetArrangementLabel(SettlementArrangement arrangement)
        {
            return arrangement switch
            {
                SettlementArrangement.Boarding => "Boarding",
                SettlementArrangement.Renting => "Renting",
                SettlementArrangement.Kin => "With kin",
                SettlementArrangement.Transient => "Transient",
                SettlementArrangement.Departed => "Departed",
                _ => "Stable household"
            };
        }
    }

    public readonly struct NewcomerSettlementHomeOption
    {
        public NewcomerSettlementHomeOption(
            int buildingId,
            int residentHouseholdCapacity,
            bool mixedUse,
            bool supportsBoarding,
            int baseBoardingCapacity)
            : this(
                buildingId,
                residentHouseholdCapacity,
                mixedUse,
                supportsBoarding,
                baseBoardingCapacity,
                false,
                residentHouseholdCapacity > 0,
                string.Empty)
        {
        }

        public NewcomerSettlementHomeOption(
            int buildingId,
            int residentHouseholdCapacity,
            bool mixedUse,
            bool supportsBoarding,
            int baseBoardingCapacity,
            bool playerOwned,
            bool rentalCapable,
            string displayName)
        {
            BuildingId = buildingId;
            ResidentHouseholdCapacity = Mathf.Max(0, residentHouseholdCapacity);
            MixedUse = mixedUse;
            SupportsBoarding = supportsBoarding;
            BaseBoardingCapacity = Mathf.Max(0, baseBoardingCapacity);
            PlayerOwned = playerOwned;
            RentalCapable = rentalCapable && ResidentHouseholdCapacity > 0;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Building {buildingId:000}" : displayName;
        }

        public int BuildingId { get; }
        public int ResidentHouseholdCapacity { get; }
        public bool MixedUse { get; }
        public bool SupportsBoarding { get; }
        public int BaseBoardingCapacity { get; }
        public bool PlayerOwned { get; }
        public bool RentalCapable { get; }
        public string DisplayName { get; }
    }

    public readonly struct NewcomerSettlementBoardingOption
    {
        public NewcomerSettlementBoardingOption(
            string businessInstanceId,
            int buildingId,
            string displayName,
            int roomCapacity,
            bool playerOwned,
            int currentCashCents,
            int filledStaff,
            int targetStaff)
        {
            BusinessInstanceId = businessInstanceId ?? string.Empty;
            BuildingId = buildingId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Boarding House" : displayName;
            RoomCapacity = Mathf.Max(0, roomCapacity);
            PlayerOwned = playerOwned;
            CurrentCashCents = Mathf.Max(0, currentCashCents);
            FilledStaff = Mathf.Max(0, filledStaff);
            TargetStaff = Mathf.Max(0, targetStaff);
        }

        public string BusinessInstanceId { get; }
        public int BuildingId { get; }
        public string DisplayName { get; }
        public int RoomCapacity { get; }
        public bool PlayerOwned { get; }
        public int CurrentCashCents { get; }
        public int FilledStaff { get; }
        public int TargetStaff { get; }
    }

    [Serializable]
    public sealed class RentalPropertyState
    {
        public int buildingId = -1;
        public string displayName = string.Empty;
        public int residentHouseholdCapacity;
        public int occupiedHouseholds;
        public int vacantHouseholdSlots;
        public bool playerOwned;
        public bool mixedUse;
        public bool rentalCapable = true;
        public string occupancySummary = string.Empty;

        public void Sanitize()
        {
            displayName ??= string.Empty;
            residentHouseholdCapacity = Mathf.Max(0, residentHouseholdCapacity);
            occupiedHouseholds = Mathf.Clamp(occupiedHouseholds, 0, Mathf.Max(0, residentHouseholdCapacity));
            vacantHouseholdSlots = Mathf.Max(0, vacantHouseholdSlots);
            occupancySummary ??= string.Empty;
        }
    }

    [Serializable]
    public sealed class RentalApplicantState
    {
        public int personId = -1;
        public int householdId = -1;
        public int preferredBuildingId = -1;
        public string displayName = string.Empty;
        public NewcomerArrivalProfile arrivalProfile = NewcomerArrivalProfile.SettledResident;
        public SettlementArrangement currentArrangement = SettlementArrangement.StableHousehold;
        public int savingsCents;
        public int weeklyIncomeCents;
        public int weeksWaiting;
        public float score01;
        public bool acceptedLastWeek;
        public string status = string.Empty;

        public void Sanitize()
        {
            personId = Mathf.Max(-1, personId);
            householdId = Mathf.Max(-1, householdId);
            preferredBuildingId = Mathf.Max(-1, preferredBuildingId);
            displayName ??= string.Empty;
            if (!Enum.IsDefined(typeof(NewcomerArrivalProfile), arrivalProfile))
            {
                arrivalProfile = NewcomerArrivalProfile.SettledResident;
            }

            if (!Enum.IsDefined(typeof(SettlementArrangement), currentArrangement))
            {
                currentArrangement = SettlementArrangement.StableHousehold;
            }

            savingsCents = Mathf.Max(0, savingsCents);
            weeklyIncomeCents = Mathf.Max(0, weeklyIncomeCents);
            weeksWaiting = Mathf.Max(0, weeksWaiting);
            score01 = Mathf.Clamp01(score01);
            status ??= string.Empty;
        }
    }

    public readonly struct NewcomerProfileDefinition
    {
        public NewcomerProfileDefinition(
            NewcomerArrivalProfile profile,
            int minCashCents,
            int maxCashCents,
            int minHouseholdSize,
            int maxHouseholdSize,
            float boardingPreference01,
            float rentingPreference01,
            float kinPreference01,
            float directSettlementPreference01,
            float laborUrgency01,
            string professionBias,
            int settlementDifficulty)
        {
            Profile = profile;
            MinCashCents = Mathf.Max(0, minCashCents);
            MaxCashCents = Mathf.Max(MinCashCents, maxCashCents);
            MinHouseholdSize = Mathf.Max(1, minHouseholdSize);
            MaxHouseholdSize = Mathf.Max(MinHouseholdSize, maxHouseholdSize);
            BoardingPreference01 = Mathf.Clamp01(boardingPreference01);
            RentingPreference01 = Mathf.Clamp01(rentingPreference01);
            KinPreference01 = Mathf.Clamp01(kinPreference01);
            DirectSettlementPreference01 = Mathf.Clamp01(directSettlementPreference01);
            LaborUrgency01 = Mathf.Clamp01(laborUrgency01);
            ProfessionBias = professionBias ?? string.Empty;
            SettlementDifficulty = Mathf.Max(0, settlementDifficulty);
        }

        public NewcomerArrivalProfile Profile { get; }
        public int MinCashCents { get; }
        public int MaxCashCents { get; }
        public int MinHouseholdSize { get; }
        public int MaxHouseholdSize { get; }
        public float BoardingPreference01 { get; }
        public float RentingPreference01 { get; }
        public float KinPreference01 { get; }
        public float DirectSettlementPreference01 { get; }
        public float LaborUrgency01 { get; }
        public string ProfessionBias { get; }
        public int SettlementDifficulty { get; }
    }

    public static class NewcomerSettlementEvaluator
    {
        private const int ComfortableHouseholdOccupants = 4;

        public static float GetDefaultSettlementStability(SettlementArrangement arrangement)
        {
            return arrangement switch
            {
                SettlementArrangement.StableHousehold => 1f,
                SettlementArrangement.Renting => 0.78f,
                SettlementArrangement.Kin => 0.66f,
                SettlementArrangement.Boarding => 0.56f,
                SettlementArrangement.Transient => 0.18f,
                SettlementArrangement.Departed => 0f,
                _ => 1f
            };
        }

        public static float GetDefaultHousingFriction(SettlementArrangement arrangement)
        {
            return arrangement switch
            {
                SettlementArrangement.StableHousehold => 0.05f,
                SettlementArrangement.Renting => 0.22f,
                SettlementArrangement.Kin => 0.30f,
                SettlementArrangement.Boarding => 0.42f,
                SettlementArrangement.Transient => 0.86f,
                SettlementArrangement.Departed => 1f,
                _ => 0.05f
            };
        }

        public static float GetDefaultLaborReadiness(SettlementArrangement arrangement)
        {
            return arrangement switch
            {
                SettlementArrangement.StableHousehold => 0.88f,
                SettlementArrangement.Renting => 0.78f,
                SettlementArrangement.Kin => 0.72f,
                SettlementArrangement.Boarding => 0.66f,
                SettlementArrangement.Transient => 0.36f,
                SettlementArrangement.Departed => 0f,
                _ => 0.88f
            };
        }

        public static float GetDefaultHomeAmbition(SettlementArrangement arrangement)
        {
            return arrangement switch
            {
                SettlementArrangement.StableHousehold => 0.22f,
                SettlementArrangement.Renting => 0.58f,
                SettlementArrangement.Kin => 0.48f,
                SettlementArrangement.Boarding => 0.70f,
                SettlementArrangement.Transient => 0.86f,
                _ => 0f
            };
        }

        public static HouseholdDwellingKind ResolveDwellingKind(HouseholdState household)
        {
            if (household == null)
            {
                return HouseholdDwellingKind.Departed;
            }

            if (household.dwellingKind == HouseholdDwellingKind.Departed
                || household.settlementArrangement == SettlementArrangement.Departed)
            {
                return HouseholdDwellingKind.Departed;
            }

            if (household.isTransientHousehold || household.settlementArrangement == SettlementArrangement.Transient)
            {
                return HouseholdDwellingKind.RoughTemporaryLodging;
            }

            if (household.isBoardingHouseGuestHousehold || household.isBoardingHouseLodging)
            {
                return HouseholdDwellingKind.BoardingHouseLodging;
            }

            if (household.hasKinAbsorptionPressure || household.settlementArrangement == SettlementArrangement.Kin)
            {
                return HouseholdDwellingKind.KinSharedHousehold;
            }

            if (household.isRenterHousehold || household.settlementArrangement == SettlementArrangement.Renting)
            {
                return HouseholdDwellingKind.RentedHomeOrRoom;
            }

            return HouseholdDwellingKind.OwnedHome;
        }

        public static SettlementArrangement ResolveArrangementFromDwelling(HouseholdDwellingKind dwellingKind)
        {
            return dwellingKind switch
            {
                HouseholdDwellingKind.RentedHomeOrRoom => SettlementArrangement.Renting,
                HouseholdDwellingKind.BoardingHouseLodging => SettlementArrangement.Boarding,
                HouseholdDwellingKind.KinSharedHousehold => SettlementArrangement.Kin,
                HouseholdDwellingKind.RoughTemporaryLodging => SettlementArrangement.Transient,
                HouseholdDwellingKind.Departed => SettlementArrangement.Departed,
                _ => SettlementArrangement.StableHousehold
            };
        }

        public static string GetDwellingDisplayName(HouseholdDwellingKind dwellingKind)
        {
            return dwellingKind switch
            {
                HouseholdDwellingKind.RentedHomeOrRoom => "rented home/room",
                HouseholdDwellingKind.BoardingHouseLodging => "boarding-house lodging",
                HouseholdDwellingKind.KinSharedHousehold => "kin-shared household",
                HouseholdDwellingKind.RoughTemporaryLodging => "rough temporary lodging",
                HouseholdDwellingKind.Departed => "departed",
                _ => "owned/stable home"
            };
        }

        public static bool IsUnstable(SettlementArrangement arrangement)
        {
            return arrangement == SettlementArrangement.Boarding
                || arrangement == SettlementArrangement.Kin
                || arrangement == SettlementArrangement.Transient;
        }

        public static void EnsurePersonSettlementInitialized(PersonState person)
        {
            if (person == null)
            {
                return;
            }

            if (!Enum.IsDefined(typeof(NewcomerArrivalProfile), person.arrivalProfile))
            {
                person.arrivalProfile = NewcomerArrivalProfile.SettledResident;
            }

            if (!Enum.IsDefined(typeof(SettlementArrangement), person.settlementArrangement))
            {
                person.settlementArrangement = SettlementArrangement.StableHousehold;
            }

            person.laborUrgency01 = Mathf.Clamp01(person.laborUrgency01);
            person.laborReadinessModifier = Mathf.Clamp(person.laborReadinessModifier, -50, 50);
            person.settlementDifficulty = Mathf.Max(0, person.settlementDifficulty);
            person.hostHouseholdId = Mathf.Max(-1, person.hostHouseholdId);
            person.preferredProfessionBias ??= string.Empty;
            person.settlementPressure.EnsureInitialized(person.settlementArrangement);
        }

        public static void EnsureHouseholdSettlementInitialized(HouseholdState household)
        {
            if (household == null)
            {
                return;
            }

            if (!Enum.IsDefined(typeof(NewcomerArrivalProfile), household.arrivalProfile))
            {
                household.arrivalProfile = NewcomerArrivalProfile.SettledResident;
            }

            if (!Enum.IsDefined(typeof(SettlementArrangement), household.settlementArrangement))
            {
                household.settlementArrangement = SettlementArrangement.StableHousehold;
            }

            if (!Enum.IsDefined(typeof(HouseholdDwellingKind), household.dwellingKind))
            {
                household.dwellingKind = ResolveDwellingKind(household);
            }

            household.memberIds ??= new System.Collections.Generic.List<int>();
            for (int i = household.memberIds.Count - 1; i >= 0; i--)
            {
                if (household.memberIds[i] < 0)
                {
                    household.memberIds.RemoveAt(i);
                }
            }

            household.boarderPersonIds ??= new System.Collections.Generic.List<int>();
            for (int i = household.boarderPersonIds.Count - 1; i >= 0; i--)
            {
                if (household.boarderPersonIds[i] < 0)
                {
                    household.boarderPersonIds.RemoveAt(i);
                }
            }

            RemoveDuplicateMemberIds(household);
            RemoveDuplicateBoarderIds(household);
            household.baseBoardingCapacity = Mathf.Max(0, household.baseBoardingCapacity);
            household.boardingCapacity = CalculateBoardingCapacity(household);
            household.settlementReserveStrengthCents = Mathf.Max(0, household.settlementReserveStrengthCents);
            household.crowdingPressure01 = Mathf.Clamp01(household.crowdingPressure01);
            household.boarderOverloadPressure01 = Mathf.Clamp01(household.boarderOverloadPressure01);
            household.kinAbsorptionPressure01 = Mathf.Clamp01(household.kinAbsorptionPressure01);
            household.lodgingBusinessInstanceId ??= string.Empty;
            household.boardingBusinessInstanceId ??= string.Empty;
            if (household.isBoardingHouseGuestHousehold)
            {
                // Guest-room households are a special synthetic lodging carrier used by boarding houses.
                // They must remain recognizable after save/load instead of collapsing every lodging flag into guest-household semantics.
                household.isBoardingHouseLodging = true;
            }

            if (household.isBoardingHouseLodging)
            {
                string lodgingBusinessInstanceId = !string.IsNullOrWhiteSpace(household.boardingBusinessInstanceId)
                    ? household.boardingBusinessInstanceId
                    : household.lodgingBusinessInstanceId;
                household.boardingBusinessInstanceId = lodgingBusinessInstanceId ?? string.Empty;
                household.lodgingBusinessInstanceId = household.boardingBusinessInstanceId;
            }
            household.dwellingKind = ResolveDwellingKind(household);
            household.dwellingSummary = GetDwellingDisplayName(household.dwellingKind);
            household.isTransientHousehold = household.dwellingKind == HouseholdDwellingKind.RoughTemporaryLodging;
            household.isRenterHousehold = household.dwellingKind == HouseholdDwellingKind.RentedHomeOrRoom;
            household.hostsBoarders = household.boarderPersonIds.Count > 0;
            household.lastSettlementSummary ??= string.Empty;
            household.settlementPressure.EnsureInitialized(household.settlementArrangement);
        }

        public static void RefreshHouseholdPressures(HouseholdState household)
        {
            if (household == null)
            {
                return;
            }

            EnsureHouseholdSettlementInitialized(household);
            int occupants = household.memberIds != null ? household.memberIds.Count : 0;
            int boarders = household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
            int stableCapacity = ComfortableHouseholdOccupants + Mathf.Max(0, household.boardingCapacity);
            household.crowdingPressure01 = Mathf.Clamp01((occupants - stableCapacity) / 4f);
            household.boarderOverloadPressure01 = household.boardingCapacity <= 0
                ? boarders > 0 ? 1f : 0f
                : Mathf.Clamp01((boarders - household.boardingCapacity) / (float)Mathf.Max(1, household.boardingCapacity));
            household.kinAbsorptionPressure01 = household.hasKinAbsorptionPressure
                ? Mathf.Clamp01((occupants - ComfortableHouseholdOccupants) / 3f)
                : 0f;

            float friction = Mathf.Clamp01(
                GetDefaultHousingFriction(household.settlementArrangement)
                + household.crowdingPressure01 * 0.35f
                + household.boarderOverloadPressure01 * 0.45f
                + household.kinAbsorptionPressure01 * 0.25f
                + (household.dwellingKind == HouseholdDwellingKind.RoughTemporaryLodging ? 0.18f : 0f));
            float stability = Mathf.Clamp01(GetDefaultSettlementStability(household.settlementArrangement) - friction * 0.25f);
            household.settlementPressure = SettlementPressureState.Create(
                household.settlementArrangement,
                stability,
                friction,
                GetDefaultLaborReadiness(household.settlementArrangement),
                household.settlementPressure.weeksInArrangement,
                GetDefaultHomeAmbition(household.settlementArrangement),
                BuildHouseholdSummary(household));
            household.hostsBoarders = boarders > 0;
            household.lastSettlementSummary = household.settlementPressure.summary;
        }

        public static int CalculateBoardingCapacity(HouseholdState household)
        {
            if (household == null)
            {
                return 0;
            }

            int capacity = Mathf.Max(0, household.baseBoardingCapacity);
            if (household.upgrades != null)
            {
                for (int i = 0; i < household.upgrades.Count; i++)
                {
                    HouseholdUpgradeState upgrade = household.upgrades[i];
                    if (upgrade == null || !upgrade.built)
                    {
                        continue;
                    }

                    HouseholdUpgradeDefinition definition = HouseholdUpgradeCatalog.Get(upgrade.kind);
                    if (definition != null)
                    {
                        capacity += Mathf.Max(0, definition.HousingSlotBonus);
                    }
                }
            }

            return Mathf.Max(0, capacity);
        }

        public static int GetOpenBoardingSlots(HouseholdState household)
        {
            if (household == null)
            {
                return 0;
            }

            EnsureHouseholdSettlementInitialized(household);
            int used = household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
            return Mathf.Max(0, household.boardingCapacity - used);
        }

        public static float GetLaborReadiness01(PersonState person)
        {
            if (person == null)
            {
                return 0f;
            }

            EnsurePersonSettlementInitialized(person);
            float readiness = GetDefaultLaborReadiness(person.settlementArrangement);
            readiness += person.laborReadinessModifier / 100f;
            readiness += (person.laborUrgency01 - 0.5f) * 0.20f;
            readiness -= Mathf.Clamp01(person.settlementDifficulty / 10f) * 0.16f;
            float housingFriction = person.settlementPressure.initialized
                ? person.settlementPressure.housingFriction01
                : GetDefaultHousingFriction(person.settlementArrangement);
            readiness -= housingFriction * 0.12f;
            readiness *= PopulationHealthEvaluator.GetLaborAvailability01(person);
            if (person.wage.weeklyWage > 0)
            {
                readiness += 0.05f;
            }

            person.settlementPressure = SettlementPressureState.Create(
                person.settlementArrangement,
                GetDefaultSettlementStability(person.settlementArrangement),
                GetDefaultHousingFriction(person.settlementArrangement),
                readiness,
                person.settlementPressure.weeksInArrangement,
                GetDefaultHomeAmbition(person.settlementArrangement),
                person.settlementPressure.summary);
            return person.settlementPressure.laborReadiness01;
        }

        public static bool IsAvailableForLabor(PersonState person, LaborAccessLevel minimumLaborAccess)
        {
            if (person == null
                || person.id < 0
                || person.settlementArrangement == SettlementArrangement.Departed
                || person.laborAccessLevel < minimumLaborAccess)
            {
                return false;
            }

            if (person.settlementArrangement == SettlementArrangement.Transient
                && person.laborUrgency01 < 0.55f
                && GetLaborReadiness01(person) < 0.42f)
            {
                return false;
            }

            return GetLaborReadiness01(person) >= 0.25f;
        }

        public static int ResolveEmploymentStabilityPercent(PersonState person, int baseStabilityPercent)
        {
            float readiness = GetLaborReadiness01(person);
            int adjustment = Mathf.RoundToInt((readiness - 0.5f) * 30f);
            return Mathf.Clamp(baseStabilityPercent + adjustment, 20, 95);
        }

        public static float BuildLaborSortScore(PersonState person, float roleFitScore01)
        {
            return Mathf.Clamp01(roleFitScore01 * 0.78f + GetLaborReadiness01(person) * 0.22f);
        }

        public static int GetProfessionWeight(PersonState person, ProfessionDefinition profession)
        {
            if (person == null || profession == null)
            {
                return 1;
            }

            EnsurePersonSettlementInitialized(person);
            string bias = person.preferredProfessionBias ?? string.Empty;
            string id = profession.ProfessionId ?? string.Empty;
            string label = profession.DisplayName ?? string.Empty;
            int weight = 10;

            if (ContainsAny(bias, "labor", "drift"))
            {
                weight += ContainsAny(id, "porter", "stock", "teamster", "carpenter") ? 10 : 0;
            }

            if (ContainsAny(bias, "trade", "skilled"))
            {
                weight += ContainsAny(id, "carpenter", "teamster", "shopkeeper") ? 12 : 0;
            }

            if (ContainsAny(bias, "commercial", "cash"))
            {
                weight += ContainsAny(id, "clerk", "shopkeeper", "store") ? 14 : 0;
            }

            if (ContainsAny(bias, "domestic", "dependent"))
            {
                weight += ContainsAny(id, "clerk", "stock", "porter") ? 4 : 0;
                weight -= ContainsAny(id, "teamster", "carpenter") ? 3 : 0;
            }

            if (person.settlementArrangement == SettlementArrangement.Transient
                && ContainsAny(label, "Shopkeeper", "Clerk"))
            {
                weight -= 3;
            }

            if (person.arrivalProfile == NewcomerArrivalProfile.SkilledCapitalizedArrival)
            {
                weight += ContainsAny(id, "carpenter", "shopkeeper", "teamster") ? 8 : 0;
            }

            return Mathf.Max(1, weight);
        }

        public static string GetArrangementDisplayName(SettlementArrangement arrangement)
        {
            return arrangement switch
            {
                SettlementArrangement.Boarding => "boarding",
                SettlementArrangement.Renting => "renting",
                SettlementArrangement.Kin => "with kin",
                SettlementArrangement.Transient => "transient",
                SettlementArrangement.Departed => "departed",
                _ => "stable"
            };
        }

        public static string GetProfileDisplayName(NewcomerArrivalProfile profile)
        {
            return profile switch
            {
                NewcomerArrivalProfile.LoneLaborer => "lone laborer",
                NewcomerArrivalProfile.KinLinkedArrival => "kin-linked arrival",
                NewcomerArrivalProfile.BoarderSeekingWorker => "boarder-seeking worker",
                NewcomerArrivalProfile.RenterReadyHousehold => "renter-ready household",
                NewcomerArrivalProfile.SkilledCapitalizedArrival => "skilled capitalized arrival",
                NewcomerArrivalProfile.DistressedRelocationHousehold => "distressed relocation household",
                NewcomerArrivalProfile.WidowElderDependentRelocation => "widow/elder relocation",
                _ => "settled resident"
            };
        }

        private static string BuildHouseholdSummary(HouseholdState household)
        {
            int boarders = household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
            if (household.dwellingKind == HouseholdDwellingKind.RoughTemporaryLodging
                || household.settlementArrangement == SettlementArrangement.Transient)
            {
                return "Rough temporary lodging under housing pressure";
            }

            if (household.isBoardingHouseGuestHousehold || household.isBoardingHouseLodging)
            {
                return $"{boarders}/{Mathf.Max(0, household.boardingCapacity)} boarding house rooms occupied";
            }

            if (boarders > 0)
            {
                return $"{boarders}/{Mathf.Max(0, household.boardingCapacity)} boarders lodged";
            }

            if (household.isRenterHousehold)
            {
                return "Renting first stable rooms";
            }

            if (household.hasKinAbsorptionPressure)
            {
                return "Kin absorbed into household";
            }

            return "Stable household";
        }

        private static void RemoveDuplicateMemberIds(HouseholdState household)
        {
            if (household == null || household.memberIds == null)
            {
                return;
            }

            for (int i = household.memberIds.Count - 1; i >= 0; i--)
            {
                int id = household.memberIds[i];
                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (household.memberIds[earlier] == id)
                    {
                        household.memberIds.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static void RemoveDuplicateBoarderIds(HouseholdState household)
        {
            if (household == null || household.boarderPersonIds == null)
            {
                return;
            }

            for (int i = household.boarderPersonIds.Count - 1; i >= 0; i--)
            {
                int id = household.boarderPersonIds[i];
                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (household.boarderPersonIds[earlier] == id)
                    {
                        household.boarderPersonIds.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(source) || values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])
                    && source.IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
