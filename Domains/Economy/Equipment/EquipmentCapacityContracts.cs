using System;
using UnityEngine;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// Pass IV (Living World Foundation): equipment capacity semantics.
    ///
    /// Equipment is NOT all Capacity = 1. Different equipment has different physical
    /// and operational constraints. These contracts define the composable capacity
    /// models Worker 2 implements and Worker 3 (production) consumes.
    ///
    /// Ownership never implies unlimited capacity: owning one plow permits one plowing
    /// operation at a time, not N. The reservation authority (ReservationContracts.cs)
    /// enforces these models; the capacity descriptors below say HOW MUCH of what a
    /// given asset provides.
    ///
    /// Condition effects are equipment-specific and proportionate (see
    /// EquipmentConditionProfile). No arbitrary universal wear-per-minute.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part V §§5.1-5.7, preserved
    /// Tech VI Part III §§3.1-3.11 (equipment, toolkits, physical capability).
    /// </summary>
    public enum EquipmentCapacityModel
    {
        Unspecified = 0,
        /// <summary>One active operation at a time (plow, single forge). Exclusive reservation.</summary>
        Exclusive = 1,
        /// <summary>
        /// Holds multiple goods by weight/volume/footprint (shelf, bin, oven chamber,
        /// wagon bed, warehouse bay). Capacity is a physical envelope, not a worker count.
        /// </summary>
        Container = 2,
        /// <summary>Multiple usable stations (forge + anvil as distinct stage resources).</summary>
        Workstation = 3,
        /// <summary>Rate-limited processing (sawmill, thresher): output per time, not per worker.</summary>
        Throughput = 4,
        /// <summary>
        /// Occupied during a passive process (oven baking): the batch occupies space/heat
        /// while the worker is released. Tending/unload still required.
        /// </summary>
        PassiveProcess = 5,
        /// <summary>Cargo capacity by mass/volume/dimensions plus draft power and route.</summary>
        Transport = 6,
    }

    /// <summary>
    /// Pass IV: physical capacity envelope for container/transport equipment.
    /// All values in SI-ish game units; zeros mean "not constrained on this axis".
    /// </summary>
    [Serializable]
    public struct CapacityEnvelope
    {
        public float MaxMassKg;
        public float MaxVolumeLiters;
        public float MaxFootprintSquareMeters;
        public float MaxLengthMeters;
        public int MaxItemCount;

        public static CapacityEnvelope Unbounded()
        {
            return new CapacityEnvelope();
        }
    }

    /// <summary>
    /// Pass IV: capacity descriptor for one equipment asset or kind. Plain data;
    /// authored per equipment kind (catalogs) or per asset (instance overrides).
    /// </summary>
    [Serializable]
    public sealed class EquipmentCapacityDescriptor
    {
        [SerializeField]
        private EquipmentCapacityModel model = EquipmentCapacityModel.Exclusive;

        /// <summary>For Workstation: number of usable stations. For Throughput: parallel lanes.</summary>
        [SerializeField]
        private int stationCount = 1;

        [SerializeField]
        private CapacityEnvelope envelope;

        /// <summary>
        /// For Throughput: units processed per game hour at condition 1.0.
        /// For PassiveProcess: max compatible batches simultaneously.
        /// </summary>
        [SerializeField]
        private float ratePerGameHour;

        /// <summary>For Transport: required motive power kind id (empty = human-portable).</summary>
        [SerializeField]
        private string requiredMotivePowerId = string.Empty;

        public EquipmentCapacityModel Model => model;
        public int StationCount => Math.Max(1, stationCount);
        public CapacityEnvelope Envelope => envelope;
        public float RatePerGameHour => Math.Max(0f, ratePerGameHour);
        public string RequiredMotivePowerId => requiredMotivePowerId;

        public EquipmentCapacityDescriptor()
        {
        }

        public EquipmentCapacityDescriptor(EquipmentCapacityModel model)
        {
            this.model = model;
        }

        /// <summary>
        /// Pass IV: full authored construction (Worker 2 condition-profile
        /// catalog). Additive — the parameterless ctor and field layout are
        /// unchanged, so Unity serialization is unaffected.
        /// </summary>
        public EquipmentCapacityDescriptor(
            EquipmentCapacityModel model,
            int stationCount,
            CapacityEnvelope envelope,
            float ratePerGameHour,
            string requiredMotivePowerId)
        {
            this.model = model;
            this.stationCount = stationCount;
            this.envelope = envelope;
            this.ratePerGameHour = ratePerGameHour;
            this.requiredMotivePowerId = requiredMotivePowerId ?? string.Empty;
        }
    }

    /// <summary>
    /// Pass IV: equipment-specific condition/wear profile. Different equipment
    /// deteriorates differently: a plow wears with use, a wagon loses operational
    /// capacity, an oven needs maintenance, a shelf barely wears. No universal rate.
    /// </summary>
    [Serializable]
    public sealed class EquipmentConditionProfile
    {
        /// <summary>Wear per active work hour at full load (0 = does not wear in use).</summary>
        [SerializeField]
        private float wearPerActiveHour01;

        /// <summary>Wear per passive-process hour (oven baking, etc.).</summary>
        [SerializeField]
        private float wearPerPassiveHour01;

        /// <summary>Wear per transport mile (wagons, carts).</summary>
        [SerializeField]
        private float wearPerMile01;

        /// <summary>Condition below which the equipment is unusable (default 0.05).</summary>
        [SerializeField]
        private float unusableBelow01 = 0.05f;

        /// <summary>Condition below which capacity is degraded (0 = never degrades).</summary>
        [SerializeField]
        private float degradedBelow01;

        /// <summary>Fraction of rated capacity available when degraded (0..1).</summary>
        [SerializeField]
        private float degradedCapacityFraction01 = 1f;

        public float WearPerActiveHour01 => Math.Max(0f, wearPerActiveHour01);
        public float WearPerPassiveHour01 => Math.Max(0f, wearPerPassiveHour01);
        public float WearPerMile01 => Math.Max(0f, wearPerMile01);
        public float UnusableBelow01 => Mathf.Clamp01(unusableBelow01);
        public float DegradedBelow01 => Mathf.Clamp01(degradedBelow01);
        public float DegradedCapacityFraction01 => Mathf.Clamp01(degradedCapacityFraction01);

        public EquipmentConditionProfile()
        {
        }

        /// <summary>
        /// Pass IV: full authored construction (Worker 2 condition-profile
        /// catalog). Additive — the parameterless ctor and field layout are
        /// unchanged, so Unity serialization is unaffected.
        /// </summary>
        public EquipmentConditionProfile(
            float wearPerActiveHour01,
            float wearPerPassiveHour01,
            float wearPerMile01,
            float unusableBelow01,
            float degradedBelow01,
            float degradedCapacityFraction01)
        {
            this.wearPerActiveHour01 = wearPerActiveHour01;
            this.wearPerPassiveHour01 = wearPerPassiveHour01;
            this.wearPerMile01 = wearPerMile01;
            this.unusableBelow01 = unusableBelow01;
            this.degradedBelow01 = degradedBelow01;
            this.degradedCapacityFraction01 = degradedCapacityFraction01;
        }
    }

    /// <summary>
    /// Pass IV: capacity query contract. Implementations answer "how much of this
    /// resource is usable right now" from the descriptor, live condition, and live
    /// reservations. Worker 2 implements; production and task execution consume.
    /// </summary>
    public interface IEquipmentCapacityService
    {
        /// <summary>
        /// Registers (or replaces) the capacity descriptor + condition profile for an
        /// equipment key. Additive; existing equipment keeps working.
        /// </summary>
        void RegisterCapacity(string equipmentKey, EquipmentCapacityDescriptor descriptor, EquipmentConditionProfile conditionProfile);

        /// <summary>Usable capacity fraction 0..1 from live condition (degraded/unusable thresholds).</summary>
        float UsableCapacityFraction(string equipmentKey, float condition01);

        /// <summary>Whether the equipment can accept one more unit of the requested load.</summary>
        bool CanAccept(string equipmentKey, float condition01, float requestedQuantity, float atGameSeconds);
    }
}
