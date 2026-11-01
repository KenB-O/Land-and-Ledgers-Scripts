// Generic Business Reconstruction, section 12.
// Remove arbitrary throughput limits: replace hard caps with a
// capacity calculation from real constraints. Output depends on
// materials on hand, worker time available, equipment condition and
// availability, energy/fuel on hand, transportation capacity, and
// storage capacity. The binding constraint is the minimum of those
// factors; there is no fixed weekly or daily quota in this model.
// This class computes capacity; it does not run production and
// does not create output. It deliberately has NO constants for
// "units per week" or "logs per week": any such number would be
// exactly the arbitrary limit this pass removes. Caller-supplied
// rates (units per worker-hour, units per equipment-hour, fuel per
// unit) are the only rates here, and they must come from the
// method/equipment definition, not from this class.
// Audit note (see handoff): the constants in
// SharedBusinessRuntimeManager (output units per input, local outlet
// max units, sawmill weekly maxima) were NOT deleted or changed in
// this pass, because legacy callers still read them. This capacity
// model is the replacement path; deleting the old constants before
// their callers migrate would break legacy businesses. The old
// constants are documented as legacy in the handoff, and their
// callers are a Codex migration task.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public sealed class ThroughputCapacityInput
    {
        public float MaterialUnitsOnHand;
        public float MaterialUnitsPerOutput = 1f;
        public float WorkerHoursAvailable;
        public float OutputsPerWorkerHour = 1f;
        public float EquipmentHoursAvailable;
        public float OutputsPerEquipmentHour = 1f;
        public float FuelOrEnergyOnHand;
        public float FuelOrEnergyPerOutput = 1f;
        public float TransportCapacityUnits = float.PositiveInfinity;
        public float StorageCapacityUnits = float.PositiveInfinity;
        public float EquipmentCondition01 = 1f;
        public string BindingConstraint = string.Empty;
    }

    public static class ThroughputCapacity
    {
        public struct CapacityResult
        {
            public float MaxOutputs;
            public string BindingConstraint;
        }

        public static CapacityResult Compute(ThroughputCapacityInput input)
        {
            var result = new CapacityResult { MaxOutputs = 0f, BindingConstraint = "no input" };
            if (input == null) return result;
            float byMaterial = input.MaterialUnitsPerOutput > 0f ? input.MaterialUnitsOnHand / input.MaterialUnitsPerOutput : 0f;
            float byWorker = input.WorkerHoursAvailable * Math.Max(0f, input.OutputsPerWorkerHour);
            float byEquipment = input.EquipmentHoursAvailable * Math.Max(0f, input.OutputsPerEquipmentHour) * Clamp01(input.EquipmentCondition01);
            float byFuel = input.FuelOrEnergyPerOutput > 0f ? input.FuelOrEnergyOnHand / input.FuelOrEnergyPerOutput : float.PositiveInfinity;
            float byTransport = input.TransportCapacityUnits;
            float byStorage = input.StorageCapacityUnits;
            float min = byMaterial;
            string binding = "materials";
            if (byWorker < min) { min = byWorker; binding = "worker time"; }
            if (byEquipment < min) { min = byEquipment; binding = "equipment"; }
            if (byFuel < min) { min = byFuel; binding = "fuel or energy"; }
            if (byTransport < min) { min = byTransport; binding = "transportation"; }
            if (byStorage < min) { min = byStorage; binding = "storage"; }
            if (min < 0f) min = 0f;
            if (float.IsInfinity(min)) min = 0f;
            result.MaxOutputs = (float)Math.Floor(min);
            result.BindingConstraint = binding;
            return result;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
