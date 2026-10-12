using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// Pass IV: the equipment capacity service implementation. Answers "how much
    /// of this resource is usable right now" from the registered descriptor,
    /// live condition, and live reservations — across all six capacity models:
    ///
    /// - Exclusive: one active operation (plow, single forge).
    /// - Container: physical envelope in item units (shelf, bin).
    /// - Workstation: station count.
    /// - Throughput: rate-limited units per game hour (sawmill, thresher).
    /// - PassiveProcess: simultaneous batch slots (oven).
    /// - Transport: cargo mass envelope + required motive power (wagon).
    ///
    /// Ownership never implies unlimited capacity: an unregistered key defaults
    /// to Exclusive (1 unit). Reservations always go through the injected
    /// IUnifiedReservationAuthority — this service never invents availability.
    ///
    /// Registration keys use the reservation key space ("asset:{id}",
    /// "kit:{id}", "workstation:{id}", ...). KindLookup lets instance keys fall
    /// back to authored kind profiles ("kind:plow") registered by
    /// EquipmentConditionProfiles.RegisterAll.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part V §§5.1-5.7.
    /// </summary>
    public sealed class EquipmentCapacityService : IEquipmentCapacityService
    {
        private sealed class Registration
        {
            public EquipmentCapacityDescriptor Descriptor;
            public EquipmentConditionProfile Profile;
        }

        private readonly IUnifiedReservationAuthority reservations;
        private readonly Dictionary<string, Registration> registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);

        /// <summary>Resolves an equipment key to its kind for kind-level profile fallback. May be null.</summary>
        public Func<string, string> KindLookup;

        /// <summary>
        /// Motive-power availability by motive id (e.g. "horse-team"). May be
        /// null — a Transport descriptor that requires motive power then refuses
        /// (never assumed).
        /// </summary>
        public Func<string, bool> MotivePowerAvailable;

        public EquipmentCapacityService(IUnifiedReservationAuthority reservations)
        {
            this.reservations = reservations;
        }

        /// <inheritdoc />
        public void RegisterCapacity(string equipmentKey, EquipmentCapacityDescriptor descriptor, EquipmentConditionProfile conditionProfile)
        {
            if (string.IsNullOrWhiteSpace(equipmentKey)) return;
            string key = equipmentKey.Trim();
            registrations[key] = new Registration
            {
                Descriptor = descriptor,
                Profile = conditionProfile,
            };
            // Keep the reservation authority's capacity limit in step with the
            // model so over-capacity reservations are refused at the source.
            var concrete = reservations as EquipmentReservationService;
            if (concrete != null && descriptor != null)
            {
                float limit = ModelCapacityLimit(descriptor);
                if (limit > 0f)
                    concrete.RegisterResourceCapacity(key, limit);
            }
        }

        /// <inheritdoc />
        public float UsableCapacityFraction(string equipmentKey, float condition01)
        {
            var profile = FindProfile(equipmentKey);
            float c = condition01 < 0f ? 0f : (condition01 > 1f ? 1f : condition01);
            if (profile == null)
            {
                // No authored profile: fall back to the asset truth
                // (EquipmentAsset.IsUsable: condition > 0.05).
                return c > 0.05f ? 1f : 0f;
            }
            if (c < profile.UnusableBelow01) return 0f;
            if (profile.DegradedBelow01 > 0f && c < profile.DegradedBelow01)
                return profile.DegradedCapacityFraction01;
            return 1f;
        }

        /// <inheritdoc />
        public bool CanAccept(string equipmentKey, float condition01, float requestedQuantity, float atGameSeconds)
        {
            if (string.IsNullOrWhiteSpace(equipmentKey)) return false;
            if (!(requestedQuantity > 0f)) return false;
            float fraction = UsableCapacityFraction(equipmentKey, condition01);
            if (fraction <= 0f) return false;
            string key = equipmentKey.Trim();
            var descriptor = FindDescriptor(key);
            if (descriptor == null)
            {
                // Unknown equipment: exclusive default, reservation truth decides.
                return reservations != null
                    ? requestedQuantity <= reservations.AvailableCapacity(key, atGameSeconds)
                    : requestedQuantity <= 1f;
            }
            switch (descriptor.Model)
            {
                case EquipmentCapacityModel.Container:
                case EquipmentCapacityModel.Workstation:
                case EquipmentCapacityModel.PassiveProcess:
                case EquipmentCapacityModel.Throughput:
                    return CanAcceptScaled(key, descriptor, fraction, requestedQuantity, atGameSeconds);
                case EquipmentCapacityModel.Transport:
                    return CanAcceptTransport(key, descriptor, fraction, requestedQuantity, atGameSeconds);
                case EquipmentCapacityModel.Exclusive:
                case EquipmentCapacityModel.Unspecified:
                default:
                    return reservations != null
                        ? requestedQuantity <= reservations.AvailableCapacity(key, atGameSeconds)
                        : requestedQuantity <= 1f;
            }
        }

        // ---------- save/load (DTO type lives in SystemsSaveDtos.cs) ----------

        /// <summary>Pass IV: captures capacity registrations for the save pipeline.</summary>
        public LandLedgers.Persistence.EquipmentCapacityServiceSaveDto CaptureSaveDto()
        {
            var dto = new LandLedgers.Persistence.EquipmentCapacityServiceSaveDto();
            foreach (var kv in registrations)
            {
                dto.registrations.Add(new LandLedgers.Persistence.CapacityRegistrationEntry
                {
                    equipmentKey = kv.Key,
                    descriptor = kv.Value?.Descriptor,
                    profile = kv.Value?.Profile,
                });
            }
            return dto;
        }

        /// <summary>Pass IV: restores capacity registrations. Clear-then-load, idempotent.</summary>
        public void LoadFromSaveDto(LandLedgers.Persistence.EquipmentCapacityServiceSaveDto dto)
        {
            registrations.Clear();
            if (dto?.registrations == null) return;
            foreach (var entry in dto.registrations)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.equipmentKey)) continue;
                // Re-register through the public path so the authority's
                // capacity limits are restored in step.
                RegisterCapacity(entry.equipmentKey, entry.descriptor, entry.profile);
            }
        }

        // ---------- internals ----------

        private Registration FindRegistration(string equipmentKey)
        {
            if (string.IsNullOrWhiteSpace(equipmentKey)) return null;
            string key = equipmentKey.Trim();
            Registration reg;
            if (registrations.TryGetValue(key, out reg)) return reg;
            if (KindLookup != null)
            {
                string kind = null;
                try { kind = KindLookup(key); } catch (Exception) { kind = null; }
                if (!string.IsNullOrWhiteSpace(kind) &&
                    registrations.TryGetValue("kind:" + kind.Trim(), out reg))
                    return reg;
            }
            return null;
        }

        private EquipmentCapacityDescriptor FindDescriptor(string equipmentKey) =>
            FindRegistration(equipmentKey)?.Descriptor;

        private EquipmentConditionProfile FindProfile(string equipmentKey) =>
            FindRegistration(equipmentKey)?.Profile;

        /// <summary>Capacity units the authority enforces for one descriptor.</summary>
        private static float ModelCapacityLimit(EquipmentCapacityDescriptor descriptor)
        {
            switch (descriptor.Model)
            {
                case EquipmentCapacityModel.Container:
                    if (descriptor.Envelope.MaxItemCount > 0) return descriptor.Envelope.MaxItemCount;
                    if (descriptor.Envelope.MaxMassKg > 0f) return descriptor.Envelope.MaxMassKg;
                    return float.PositiveInfinity;
                case EquipmentCapacityModel.Workstation:
                    return Math.Max(1, descriptor.StationCount);
                case EquipmentCapacityModel.Throughput:
                    return descriptor.RatePerGameHour > 0f ? descriptor.RatePerGameHour : 1f;
                case EquipmentCapacityModel.PassiveProcess:
                    // Contract: for PassiveProcess, ratePerGameHour carries max
                    // simultaneous batches.
                    return descriptor.RatePerGameHour > 0f ? descriptor.RatePerGameHour : 1f;
                case EquipmentCapacityModel.Transport:
                    if (descriptor.Envelope.MaxMassKg > 0f) return descriptor.Envelope.MaxMassKg;
                    return float.PositiveInfinity;
                case EquipmentCapacityModel.Exclusive:
                case EquipmentCapacityModel.Unspecified:
                default:
                    return 1f;
            }
        }

        private bool CanAcceptScaled(string key, EquipmentCapacityDescriptor descriptor,
            float fraction, float requestedQuantity, float atGameSeconds)
        {
            float limit = ModelCapacityLimit(descriptor);
            if (float.IsPositiveInfinity(limit))
                return true; // unconstrained axis, condition already checked
            float available = reservations != null ? reservations.AvailableCapacity(key, atGameSeconds) : limit;
            float used = Math.Max(0f, limit - available);
            float effective = limit * fraction;
            return used + requestedQuantity <= effective + 1e-6f;
        }

        private bool CanAcceptTransport(string key, EquipmentCapacityDescriptor descriptor,
            float fraction, float requestedQuantity, float atGameSeconds)
        {
            string motiveId = descriptor.RequiredMotivePowerId;
            if (!string.IsNullOrWhiteSpace(motiveId))
            {
                bool motiveOk = false;
                if (MotivePowerAvailable != null)
                {
                    try { motiveOk = MotivePowerAvailable(motiveId); }
                    catch (Exception) { motiveOk = false; }
                }
                // No lookup, no assumption: required motive power that cannot be
                // verified refuses.
                if (!motiveOk) return false;
            }
            float massLimit = descriptor.Envelope.MaxMassKg;
            if (!(massLimit > 0f))
                return true; // no mass axis; motive power + condition already checked
            float available = reservations != null ? reservations.AvailableCapacity(key, atGameSeconds) : massLimit;
            float used = Math.Max(0f, massLimit - available);
            float effective = massLimit * fraction;
            return used + requestedQuantity <= effective + 1e-6f;
        }
    }
}
