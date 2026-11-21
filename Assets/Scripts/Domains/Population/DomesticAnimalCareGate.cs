// Lane 9c (M-R4, Agent M §3.1/§7): the scheduling gate for
// domestic.animal-care. ResolveDomesticActivityMovement has no
// animal-care branch (it returns Unsupported for anything that is not
// meal-prep, chores, or fuel-handling), and no pen/pasture activity
// location type exists in BuildingActivityLocationType, so no
// destination can exist today — yet DomesticLaborDriver scheduled the
// task daily whenever a household owned animals, producing a daily
// start / fail-arrival / interrupt / requeue loop with no possible
// success. The gate stops scheduling until a destination exists; the
// need stays visible as a diagnostic, exactly like the fuel-handling
// stove guard (no stove = no handling task, procurement need instead).
//
// The destination signal is a nullable bool, mirroring
// FirewoodActivityLocationAvailable: true = a usable animal-care
// destination is authored, false = known missing, null = unknown /
// legacy caller. All three non-true states refuse scheduling — the
// legacy behaviour (schedule unconditionally) IS the defect, so
// unknown must fail closed, not preserve it. When the owner authors a
// pen/pasture destination (owner decision on what/where), the caller
// supplies true and scheduling resumes.
using System;

namespace LandLedgers.Population
{
    public static class DomesticAnimalCareGate
    {
        /// <summary>
        /// Whether an animal-care task may be scheduled. True only when a
        /// usable animal-care destination is known to exist; otherwise
        /// false with the reason the caller records as a diagnostic so
        /// the need stays visible instead of failing silently.
        /// </summary>
        public static bool ShouldScheduleAnimalCare(bool? animalCareDestinationAvailable, out string reason)
        {
            reason = string.Empty;
            if (animalCareDestinationAvailable == true) return true;
            reason = "household owns animals but no animal-care destination (pen/pasture) is authored — " +
                "no animal-care task scheduled (need stays visible; destination is an owner decision).";
            return false;
        }
    }
}
