using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// Pass IV: authored equipment condition/wear profiles per equipment kind.
    /// Different equipment deteriorates differently — a plow wears with use, a
    /// wagon wears per mile and load, an oven wears per passive hour and needs
    /// maintenance, a shelf barely wears at all. There is no universal
    /// wear-per-minute: every rate below is a calibration choice documented at
    /// the profile, and unknown kinds get no profile (callers fall back to
    /// explicit wear, never an invented rate).
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part V §§5.1-5.7.
    /// </summary>
    public static class EquipmentConditionProfiles
    {
        /// <summary>Calibration: one plowing session's active hours (CropChain).</summary>
        public const float DefaultImplementSessionHours = 2f;

        private static readonly Dictionary<string, EquipmentConditionProfile> profiles =
            new Dictionary<string, EquipmentConditionProfile>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, EquipmentCapacityDescriptor> descriptors =
            new Dictionary<string, EquipmentCapacityDescriptor>(StringComparer.OrdinalIgnoreCase);

        static EquipmentConditionProfiles()
        {
            // Plows and soil implements: wear with active use. 0.02/hour means a
            // 2-hour plowing session costs 0.04 condition — the CropChain
            // calibration rate, now profile-driven instead of a flat constant.
            RegisterImplement("plow", 0.02f);
            RegisterImplement("moldboard-plow", 0.02f);
            RegisterImplement("harrow", 0.015f);
            RegisterImplement("cultivator", 0.015f);
            RegisterImplement("grain-drill", 0.012f);
            RegisterImplement("broadcast-seeder", 0.012f);
            RegisterImplement("grain-cradle", 0.008f);
            RegisterImplement("scythe", 0.008f);
            RegisterImplement("reaper-binder", 0.018f);
            RegisterImplement("horse-mower", 0.018f);
            RegisterImplement("hay-rake", 0.012f);
            RegisterImplement("harness", 0.01f);

            // Wagons: wear per mile plus load. The per-mile rate matches the
            // long-standing WagonAuthority trip formula (0.0005/mile); the load
            // term lives with the trip (ApplyTripWear), not the clock.
            Register(
                "farm-freight-wagon",
                Profile(wearPerMile01: 0.0005f, degradedBelow01: 0.4f, degradedFraction01: 0.75f),
                TransportDescriptor(600f, 4f, "horse-team"));
            Register(
                "farm-wagon",
                Profile(wearPerMile01: 0.0005f, degradedBelow01: 0.4f, degradedFraction01: 0.75f),
                TransportDescriptor(600f, 4f, "horse-team"));
            Register(
                "freight-wagon",
                Profile(wearPerMile01: 0.0005f, degradedBelow01: 0.4f, degradedFraction01: 0.75f),
                TransportDescriptor(600f, 4f, "horse-team"));

            // Ovens: wear per passive-process hour; maintenance restores them.
            // A degraded oven bakes smaller batches, it does not fake output.
            Register(
                "oven",
                Profile(wearPerPassiveHour01: 0.005f, wearPerActiveHour01: 0.002f,
                    degradedBelow01: 0.4f, degradedFraction01: 0.75f),
                PassiveProcessDescriptor(maxBatches: 4));
            Register(
                "bakery-oven",
                Profile(wearPerPassiveHour01: 0.005f, wearPerActiveHour01: 0.002f,
                    degradedBelow01: 0.4f, degradedFraction01: 0.75f),
                PassiveProcessDescriptor(maxBatches: 4));

            // Shelves and bins: ~zero wear. Storage does not wear out from
            // holding goods; capacity is the envelope, not condition.
            Register(
                "shelf",
                Profile(wearPerActiveHour01: 0f, wearPerPassiveHour01: 0f, wearPerMile01: 0f),
                ContainerDescriptor(maxItems: 40));
            Register(
                "bin",
                Profile(wearPerActiveHour01: 0f, wearPerPassiveHour01: 0f, wearPerMile01: 0f),
                ContainerDescriptor(maxItems: 60));

            // Forge: hot work wears it; degraded forges work slower/fewer heats.
            Register(
                "forge",
                Profile(wearPerActiveHour01: 0.01f, degradedBelow01: 0.5f, degradedFraction01: 0.7f),
                ExclusiveDescriptor());
            Register(
                "anvil",
                Profile(wearPerActiveHour01: 0.004f, degradedBelow01: 0.5f, degradedFraction01: 0.8f),
                ExclusiveDescriptor());

            // Throughput equipment: rate-limited processing, wears with use.
            Register(
                "sawmill",
                Profile(wearPerActiveHour01: 0.015f, degradedBelow01: 0.45f, degradedFraction01: 0.7f),
                ThroughputDescriptor(ratePerGameHour: 12f));
            Register(
                "thresher",
                Profile(wearPerActiveHour01: 0.015f, degradedBelow01: 0.45f, degradedFraction01: 0.7f),
                ThroughputDescriptor(ratePerGameHour: 8f));
            Register(
                "threshing-separator",
                Profile(wearPerActiveHour01: 0.015f, degradedBelow01: 0.45f, degradedFraction01: 0.7f),
                ThroughputDescriptor(ratePerGameHour: 8f));
            Register(
                "grain-mill",
                Profile(wearPerActiveHour01: 0.012f, degradedBelow01: 0.45f, degradedFraction01: 0.7f),
                ThroughputDescriptor(ratePerGameHour: 10f));
        }

        /// <summary>Returns the authored profile for a kind, or null when unknown.</summary>
        public static EquipmentConditionProfile GetProfile(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return null;
            EquipmentConditionProfile profile;
            return profiles.TryGetValue(kind.Trim(), out profile) ? profile : null;
        }

        /// <summary>Returns the authored capacity descriptor for a kind, or null when unknown.</summary>
        public static EquipmentCapacityDescriptor GetDescriptor(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return null;
            EquipmentCapacityDescriptor descriptor;
            return descriptors.TryGetValue(kind.Trim(), out descriptor) ? descriptor : null;
        }

        /// <summary>Profile-driven wear for active work hours. 0 when the kind is unknown (never invented).</summary>
        public static float WearForActiveHours(string kind, float activeHours)
        {
            var profile = GetProfile(kind);
            if (profile == null) return 0f;
            return Math.Max(0f, activeHours) * profile.WearPerActiveHour01;
        }

        /// <summary>Profile-driven wear for passive-process hours. 0 when the kind is unknown.</summary>
        public static float WearForPassiveHours(string kind, float passiveHours)
        {
            var profile = GetProfile(kind);
            if (profile == null) return 0f;
            return Math.Max(0f, passiveHours) * profile.WearPerPassiveHour01;
        }

        /// <summary>
        /// Profile-driven wear for transport miles. The per-trip load term from
        /// WagonAuthority (loadRatio × 0.01) is added by the caller — it is a
        /// trip property, not a per-mile rate.
        /// </summary>
        public static float WearForMiles(string kind, float miles)
        {
            var profile = GetProfile(kind);
            if (profile == null) return 0f;
            return Math.Max(0f, miles) * profile.WearPerMile01;
        }

        /// <summary>
        /// Seeds an IEquipmentCapacityService with every authored kind profile.
        /// Keys are kind-level ("kind:plow"); instance lookups fall back through
        /// the service's KindLookup.
        /// </summary>
        public static void RegisterAll(IEquipmentCapacityService service)
        {
            if (service == null) return;
            foreach (var kv in profiles)
            {
                EquipmentCapacityDescriptor descriptor;
                descriptors.TryGetValue(kv.Key, out descriptor);
                service.RegisterCapacity("kind:" + kv.Key, descriptor, kv.Value);
            }
        }

        private static void RegisterImplement(string kind, float wearPerActiveHour01)
        {
            Register(
                kind,
                Profile(wearPerActiveHour01: wearPerActiveHour01, degradedBelow01: 0.35f, degradedFraction01: 0.6f),
                ExclusiveDescriptor());
        }

        private static void Register(string kind, EquipmentConditionProfile profile, EquipmentCapacityDescriptor descriptor)
        {
            profiles[kind] = profile;
            if (descriptor != null) descriptors[kind] = descriptor;
        }

        private static EquipmentConditionProfile Profile(
            float wearPerActiveHour01 = 0f,
            float wearPerPassiveHour01 = 0f,
            float wearPerMile01 = 0f,
            float unusableBelow01 = 0.05f,
            float degradedBelow01 = 0f,
            float degradedFraction01 = 1f)
        {
            return new EquipmentConditionProfile(
                wearPerActiveHour01, wearPerPassiveHour01, wearPerMile01,
                unusableBelow01, degradedBelow01, degradedFraction01);
        }

        private static EquipmentCapacityDescriptor ExclusiveDescriptor() =>
            new EquipmentCapacityDescriptor(EquipmentCapacityModel.Exclusive);

        private static EquipmentCapacityDescriptor ContainerDescriptor(int maxItems) =>
            new EquipmentCapacityDescriptor(
                EquipmentCapacityModel.Container, 1,
                new CapacityEnvelope { MaxItemCount = maxItems },
                0f, string.Empty);

        private static EquipmentCapacityDescriptor ThroughputDescriptor(float ratePerGameHour) =>
            new EquipmentCapacityDescriptor(
                EquipmentCapacityModel.Throughput, 1,
                CapacityEnvelope.Unbounded(), ratePerGameHour, string.Empty);

        private static EquipmentCapacityDescriptor PassiveProcessDescriptor(int maxBatches) =>
            new EquipmentCapacityDescriptor(
                EquipmentCapacityModel.PassiveProcess, 1,
                CapacityEnvelope.Unbounded(), maxBatches, string.Empty);

        private static EquipmentCapacityDescriptor TransportDescriptor(float massKg, float volumeM3, string motivePowerId) =>
            new EquipmentCapacityDescriptor(
                EquipmentCapacityModel.Transport, 1,
                new CapacityEnvelope { MaxMassKg = massKg, MaxVolumeLiters = volumeM3 * 1000f },
                0f, motivePowerId);
    }
}
