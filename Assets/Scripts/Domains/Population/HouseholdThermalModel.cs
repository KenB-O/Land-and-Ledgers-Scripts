using System;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Heating lane (Nov 20 2026): the building thermal model. Pure static,
    /// no Unity dependency beyond Mathf. Fuel burn is driven by the
    /// indoor-outdoor temperature differential and the building's heat loss
    /// (size, storeys, insulation tier, stove tier) — it replaces the old
    /// month-gated strain factor, it does not sit beside it. One burn
    /// formula, one debit per household per day (the day-key guard lives in
    /// HouseholdReserveEvaluator.ResolveDailyUse).
    ///
    /// Calibration: the January mean (-10.7 C, Bismarck 1991-2020 normals)
    /// reproduces the verified old burn — a 4-person Basic household burns
    /// 3 units/day. The fuel unit stays the game's abstract unit (see the
    /// heating review: 1 intake unit ~ 1 kg dry wood vs. 1-3 units/day is
    /// a stylized abstraction, not physical wood-energy accounting).
    /// </summary>
    public static class HouseholdThermalModel
    {
        /// <summary>Indoor target temperature, C.</summary>
        public const float IndoorTargetC = 18f;

        /// <summary>Daily mean exterior temperature at/above which no heating is needed, C.</summary>
        public const float HeatingBaseC = 15f;

        /// <summary>
        /// Reference indoor-outdoor delta, C: IndoorTargetC minus the January
        /// mean (-10.7 C) = 28.7, rounded to 28. At the January mean the
        /// temperature factor is ~1.0 — the old full-strain value.
        /// </summary>
        public const float ReferenceDeltaC = 28f;

        /// <summary>Maximum temperature factor (a -25 C mean day burns ~1.5x the January mean).</summary>
        public const float MaxTemperatureFactor = 1.6f;

        /// <summary>Reference total heated floor area, m2 — a single-storey 60 m2 home.</summary>
        public const float ReferenceFloorAreaM2 = 60f;

        public const float MinSizeFactor = 0.5f;
        public const float MaxSizeFactor = 3.0f;

        /// <summary>
        /// A household's thermal inputs. Floor area is the total heated floor
        /// area (all storeys); storeys multiply it. RetentionTier IS the
        /// insulation input (HouseholdState.heatRetentionTier) — no second
        /// "insulation" field exists anywhere.
        /// </summary>
        public struct HouseholdThermalProfile
        {
            public float FloorAreaM2;
            public int Storeys;
            public HouseholdHeatRetentionTier RetentionTier;
            public HouseholdStoveTier StoveTier;
            public int MemberCount;
        }

        /// <summary>
        /// Derives the thermal profile for a household. A stored floor area
        /// (&gt; 0) wins; otherwise the area is derived from the household's
        /// actual member count — 36 m2 base + 12 m2 per member, clamped to
        /// 36-120 m2 (a documented calibration, not authored data). In the
        /// dev town that gives 60 m2 for a 2-person household (size factor
        /// 1.0) and 48 m2 for a 1-person household (0.8): real variation,
        /// driven by real household composition.
        /// </summary>
        public static HouseholdThermalProfile ProfileFor(HouseholdState household)
        {
            if (household == null)
            {
                return new HouseholdThermalProfile
                {
                    FloorAreaM2 = ReferenceFloorAreaM2,
                    Storeys = 1,
                    RetentionTier = HouseholdHeatRetentionTier.Basic,
                    StoveTier = HouseholdStoveTier.Basic,
                    MemberCount = 1
                };
            }

            int members = Mathf.Max(1, household.ConsumptionMemberCount);
            float area = household.homeFloorAreaM2 > 0f
                ? household.homeFloorAreaM2
                : DerivedFloorAreaM2(members);
            return new HouseholdThermalProfile
            {
                FloorAreaM2 = area,
                Storeys = Mathf.Max(1, household.homeStoreys),
                RetentionTier = household.heatRetentionTier,
                StoveTier = household.stoveTier,
                MemberCount = members
            };
        }

        /// <summary>Member-count-derived floor area, m2 (calibration — see ProfileFor).</summary>
        public static float DerivedFloorAreaM2(int memberCount)
        {
            return Mathf.Clamp(36f + 12f * Mathf.Max(1, memberCount), 36f, 120f);
        }

        /// <summary>
        /// Heating demand factor for a day's exterior mean temperature: 0 at
        /// or above the 15 C heating base, rising linearly as the day gets
        /// colder, ~1.0 at the January mean (-10.7 C), capped at 1.6.
        /// </summary>
        public static float TemperatureFactor(float exteriorMeanCelsius)
        {
            if (exteriorMeanCelsius >= HeatingBaseC)
            {
                return 0f;
            }

            return Mathf.Clamp((IndoorTargetC - exteriorMeanCelsius) / ReferenceDeltaC, 0f, MaxTemperatureFactor);
        }

        /// <summary>Total heated floor area around the 60 m2 reference home.</summary>
        public static float SizeFactor(HouseholdThermalProfile profile)
        {
            float totalArea = Mathf.Max(0f, profile.FloorAreaM2) * Mathf.Max(1, profile.Storeys);
            return Mathf.Clamp(totalArea / ReferenceFloorAreaM2, MinSizeFactor, MaxSizeFactor);
        }

        /// <summary>Fuel multiplier for the retention (insulation) tier — the same values the old month-gated burn used.</summary>
        public static float RetentionFuelMultiplier(HouseholdHeatRetentionTier tier)
        {
            return tier switch
            {
                HouseholdHeatRetentionTier.Drafty => 1.25f,
                HouseholdHeatRetentionTier.Tight => 0.75f,
                _ => 1f
            };
        }

        /// <summary>Fuel multiplier for the stove tier — the same value the old month-gated burn used.</summary>
        public static float StoveFuelMultiplier(HouseholdStoveTier tier)
        {
            return tier == HouseholdStoveTier.Efficient ? 0.7f : 1f;
        }

        /// <summary>
        /// Daily fuel burn in abstract fuel units for one household on a day
        /// with the given exterior mean temperature. 0 when no heating is
        /// needed, otherwise at least 1 unit (a fire is either lit or it
        /// is not). Calibration anchors: a 4-person Basic household at the
        /// January mean (-10.7 C) burns 3; a 2-person Basic household at the
        /// April mean (6.2 C) burns 1; at the July mean (21.8 C) burns 0.
        /// </summary>
        public static int CalculateDailyFuelUseUnits(HouseholdThermalProfile profile, float exteriorMeanCelsius)
        {
            float factor = TemperatureFactor(exteriorMeanCelsius);
            if (factor <= 0f)
            {
                return 0;
            }

            float memberLoad = 1f + Mathf.Max(0, profile.MemberCount - 1) * 0.35f;
            float useUnits = factor
                * memberLoad
                * RetentionFuelMultiplier(profile.RetentionTier)
                * StoveFuelMultiplier(profile.StoveTier)
                * SizeFactor(profile);
            return Mathf.Max(1, Mathf.CeilToInt(useUnits));
        }
    }
}
