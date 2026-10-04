using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// D1A: what the practice has to work with today. Canon §13.3D: "The
    /// doctor's office needs modest but real supplies, equipment, heat, clean
    /// working space and transport for house calls" — and "shortages of basic
    /// dressings, commonly used medicines or transport can reduce what the
    /// practice can do." The caller reports the equipment/stock state; this
    /// model turns it into capability, never inventing inputs.
    /// </summary>
    [Serializable]
    public sealed class DoctorPracticeCapabilityInput
    {
        public bool PractitionerAvailable;
        public DoctorPractitionerSkill PractitionerSkill = DoctorPractitionerSkill.Competent;
        public bool HasDoctorBag;
        public bool HasSurgicalSet;
        public int DressingsOnHand;
        public int MedicinesOnHand;
        public bool TransportReady;

        public DoctorPracticeCapabilityInput() { }
    }

    /// <summary>D1A: what the practice can actually do today, with reasons.</summary>
    [Serializable]
    public sealed class DoctorPracticeCapability
    {
        public bool CanDoOfficeVisits;
        public bool CanDoMinorProcedures;
        public bool CanDoHouseCalls;
        public List<string> LimitingFactors = new List<string>();

        public DoctorPracticeCapability() { }

        /// <summary>
        /// Whether a serious case is within local capability (Canon §13.3E:
        /// beyond this, refer to a larger off-map center). Serious injury
        /// needs a surgical set and a competent practitioner; serious illness
        /// needs a competent practitioner and medicines on hand.
        /// </summary>
        public bool CanHandleSerious(bool isInjury)
        {
            if (!CanDoOfficeVisits) return false;
            if (PractitionerSkillForSerious < DoctorPractitionerSkill.Competent) return false;
            if (isInjury) return HasSurgicalSetForSerious && DressingsForSerious > 0;
            return MedicinesForSerious > 0;
        }

        // Carried through for CanHandleSerious; set by Evaluate.
        public DoctorPractitionerSkill PractitionerSkillForSerious = DoctorPractitionerSkill.Basic;
        public bool HasSurgicalSetForSerious;
        public int DressingsForSerious;
        public int MedicinesForSerious;

        /// <summary>Evaluates capability from today's inputs. Reasons are loud — never a silent downgrade.</summary>
        public static DoctorPracticeCapability Evaluate(DoctorPracticeCapabilityInput input, List<string> diagnostics)
        {
            var capability = new DoctorPracticeCapability();
            input = input ?? new DoctorPracticeCapabilityInput();

            capability.PractitionerSkillForSerious = input.PractitionerSkill;
            capability.HasSurgicalSetForSerious = input.HasSurgicalSet;
            capability.DressingsForSerious = Math.Max(0, input.DressingsOnHand);
            capability.MedicinesForSerious = Math.Max(0, input.MedicinesOnHand);

            if (!input.PractitionerAvailable)
            {
                capability.LimitingFactors.Add("no available practitioner — premises and records only (Canon §13.3D)");
            }
            else if (!input.HasDoctorBag)
            {
                capability.LimitingFactors.Add("no usable doctor's bag (NX-1 teeth gate)");
            }
            else
            {
                capability.CanDoOfficeVisits = true;
            }

            if (capability.CanDoOfficeVisits)
            {
                if (input.HasSurgicalSet && input.DressingsOnHand > 0)
                {
                    capability.CanDoMinorProcedures = true;
                }
                else
                {
                    capability.LimitingFactors.Add(input.HasSurgicalSet
                        ? "minor procedures held: no wound dressings on hand"
                        : "minor procedures held: no surgical set");
                }

                if (input.TransportReady)
                {
                    capability.CanDoHouseCalls = true;
                }
                else
                {
                    capability.LimitingFactors.Add("house calls held: no transport ready (Canon §13.3A)");
                }
            }

            if (input.PractitionerAvailable && input.MedicinesOnHand <= 0)
            {
                capability.LimitingFactors.Add("no commonly used medicines on hand — dispensing limited to labor-only care");
            }

            diagnostics?.Add($"Doctor practice capability: office {(capability.CanDoOfficeVisits ? "yes" : "no")}, " +
                $"procedures {(capability.CanDoMinorProcedures ? "yes" : "no")}, house calls {(capability.CanDoHouseCalls ? "yes" : "no")}" +
                (capability.LimitingFactors.Count > 0 ? $" — limiting: {string.Join("; ", capability.LimitingFactors)}" : ""));
            return capability;
        }
    }

    /// <summary>D1A: a referral to a larger off-map center.</summary>
    [Serializable]
    public sealed class DoctorReferral
    {
        public EntityId ReferralId = EntityId.Invalid; // EntityKind.Contract (W1A precedent)
        public EntityId PatientPersonId = EntityId.Invalid; // EntityKind.Person
        public string Ailment = string.Empty;
        public HouseCallSeverity Severity = HouseCallSeverity.Severe;
        public string Reason = string.Empty;

        /// <summary>Named destination, e.g. "Off-map medical center". Parameterized — the caller names it.</summary>
        public string DestinationName = string.Empty;
        public bool TravelFeasible;
        public float TravelMiles;
        public int DayIndex;

        public DoctorReferral() { }
    }

    /// <summary>
    /// D1A: referral policy. Canon §13.3E: "Some cases can be referred or sent
    /// to a larger off-map center when local capability is inadequate and
    /// travel is feasible." The caller names the destination and supplies the
    /// JRN location id of the off-map center; travel feasibility is a real
    /// route check, never assumed.
    /// </summary>
    public static class DoctorReferralPolicy
    {
        /// <summary>
        /// Decides whether a case should be referred out. Returns the referral
        /// record, or null when the case stays local. Mild/moderate cases are
        /// never referred — they are within any functioning practice.
        /// </summary>
        public static DoctorReferral EvaluateReferral(
            EntityId patientPersonId,
            string ailment,
            HouseCallSeverity severity,
            bool isInjury,
            DoctorPracticeCapability capability,
            string destinationName,
            string destinationLocationId,
            string doctorLocationId,
            JourneyModel journeys,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (capability == null)
            {
                diagnostics.Add("DoctorReferralPolicy: no capability model — referral not evaluated.");
                return null;
            }

            if (severity < HouseCallSeverity.Severe)
            {
                return null; // mild/moderate stay local
            }

            if (capability.CanHandleSerious(isInjury))
            {
                diagnostics.Add($"DoctorReferralPolicy: serious {(isInjury ? "injury" : "illness")} within local capability — no referral.");
                return null;
            }

            var referral = new DoctorReferral
            {
                ReferralId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                PatientPersonId = patientPersonId,
                Ailment = ailment ?? string.Empty,
                Severity = severity,
                DestinationName = string.IsNullOrWhiteSpace(destinationName) ? "off-map medical center" : destinationName,
                DayIndex = dayIndex,
                Reason = $"local capability inadequate for a serious {(isInjury ? "injury" : "illness")} " +
                    $"(surgical set: {(capability.HasSurgicalSetForSerious ? "yes" : "no")}, " +
                    $"practitioner skill: {capability.PractitionerSkillForSerious}) — Canon §13.3E",
            };

            if (journeys != null
                && !string.IsNullOrWhiteSpace(destinationLocationId)
                && !string.IsNullOrWhiteSpace(doctorLocationId))
            {
                JourneyRoute route = journeys.FindRoute(doctorLocationId, destinationLocationId, TravelMode.Horseback);
                referral.TravelFeasible = route.Found;
                referral.TravelMiles = route.Found ? route.TotalMiles : 0f;
                if (!route.Found)
                {
                    referral.Reason += $"; travel not feasible: {route.Diagnostic}";
                }
            }
            else
            {
                referral.Reason += "; travel feasibility not checked (no route data supplied)";
            }

            diagnostics.Add($"DoctorReferralPolicy: referral {referral.ReferralId} — {referral.Ailment} to '{referral.DestinationName}' " +
                $"(travel feasible: {(referral.TravelFeasible ? "yes" : "no")}).");
            return referral;
        }
    }
}
