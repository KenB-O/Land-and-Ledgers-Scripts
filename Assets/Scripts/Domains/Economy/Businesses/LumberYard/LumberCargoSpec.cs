// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// DEF-016 (Decision 10): the lumber cargo calibration — what one unit of
// lumber physically is, for freight. This is a caller-set freight
// parameter, not universal item truth: the PhysicalItemSpec registry
// deliberately leaves every spec Unknown (F1 slice, owner decisions
// D-5/D-6 open), and the 1 kg / 0.01 m3 FreightCargoSpec default is a
// placeholder with no authored basis. The yard sells boards; the freight
// quote prices the boards that are actually hauled.
//
// One lumber unit = one milled white-pine board, 1 in x 12 in x 16 ft =
// 16 board feet. White pine is about 26 lb per cubic foot. Volume: 16
// board feet = 1.333 ft3 = 0.0377 m3, rounded to 0.04 m3 (conservative).
// Mass: 1.333 ft3 x 26 lb/ft3 = 34.7 lb = 15.7 kg, rounded to 16 kg.
// Price coherence: 16 board feet at about $25 per thousand board feet
// (1870 pine) is about 40c — the yard's 40c/unit retail. At the
// production opening wagon (450 kg / 4 m3) mass binds: 28 units per trip.
//
// Known tension, recorded not hidden: the sawmill conversion (4 lumber +
// 2 fuel wood per 600 kg log, SawmillConversionData) is not
// mass-conserving in the game's model (4 boards = 64 kg out of a 600 kg
// log) — the conversion is a yield abstraction with no mass accounting
// anywhere. Reconciling log mass to board mass is a follow-up; this
// calibration uses the board as the commercial unit, which is what the
// yard sells. Kennedy can correct any figure here.

using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    public static class LumberCargoSpec
    {
        /// <summary>Mass of one milled pine board (1 in x 12 in x 16 ft, 16 board feet), kilograms.</summary>
        public const float MassKgPerUnit = 16f;

        /// <summary>Volume of one milled pine board (16 board feet), cubic metres.</summary>
        public const float VolumeM3PerUnit = 0.04f;

        /// <summary>A fresh cargo spec for one lumber unit. Always a new instance: callers own their spec.</summary>
        public static FreightCargoSpec Spec => new FreightCargoSpec
        {
            MassKgPerUnit = MassKgPerUnit,
            VolumeM3PerUnit = VolumeM3PerUnit,
        };

        /// <summary>How many lumber units fit one wagon of the given capacities (mass binds first).</summary>
        public static int MaxUnitsPerTrip(EquipmentAsset wagon)
        {
            return FreightCargoCapacity.MaxUnitsPerTrip(Spec, wagon);
        }
    }
}
