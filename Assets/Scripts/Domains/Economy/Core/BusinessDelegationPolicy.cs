using UnityEngine;

namespace LandLedgers.Economy
{
    public enum BusinessControlState
    {
        PlayerManaged = 0,
        Assisted = 1,
        ManagerRun = 2
    }

    public enum ManagerPolicyPreset
    {
        StabilityFirst = 0,
        ProfitFirst = 1,
        ReputationFirst = 2
    }

    public static class ManagerPolicyEffects
    {
        private const float AssistedStrength = 0.5f;
        private const float ManagerRunStrength = 1f;
        private const float ProfitFirstReorderThresholdDelta01 = -0.05f;
        private const float ReputationFirstReorderThresholdDelta01 = 0.05f;
        private const float StabilityFirstReorderThresholdDelta01 = 0.10f;
        private const float ProfitFirstBudgetMultiplier = 0.85f;
        private const float ReputationFirstBudgetMultiplier = 1.10f;
        private const float StabilityFirstBudgetMultiplier = 1.20f;
        private const float ProfitFirstCashReserveMultiplier = 0.85f;
        private const float ReputationFirstCashReserveMultiplier = 1.10f;
        private const float StabilityFirstCashReserveMultiplier = 1.25f;
        private const float ProfitFirstMarginAdjustment01 = 0.03f;
        private const float ReputationFirstMarginAdjustment01 = -0.03f;

        public static BusinessControlState GetDefaultControlState(BusinessOwnerIdentity owner)
        {
            return owner != null && owner.OwnerKind == BusinessOwnerKind.Player
                ? BusinessControlState.PlayerManaged
                : BusinessControlState.ManagerRun;
        }

        public static BusinessControlState SanitizeControlState(BusinessControlState controlState, BusinessOwnerIdentity owner)
        {
            BusinessControlState sanitized = controlState switch
            {
                BusinessControlState.Assisted => BusinessControlState.Assisted,
                BusinessControlState.ManagerRun => BusinessControlState.ManagerRun,
                _ => BusinessControlState.PlayerManaged
            };

            if (owner == null || owner.OwnerKind != BusinessOwnerKind.Player)
            {
                return sanitized == BusinessControlState.PlayerManaged
                    ? BusinessControlState.ManagerRun
                    : sanitized;
            }

            return sanitized;
        }

        public static ManagerPolicyPreset SanitizePolicy(ManagerPolicyPreset policy)
        {
            return policy switch
            {
                ManagerPolicyPreset.ProfitFirst => ManagerPolicyPreset.ProfitFirst,
                ManagerPolicyPreset.ReputationFirst => ManagerPolicyPreset.ReputationFirst,
                _ => ManagerPolicyPreset.StabilityFirst
            };
        }

        public static float GetDelegationStrength01(BusinessControlState controlState)
        {
            return controlState switch
            {
                BusinessControlState.Assisted => AssistedStrength,
                BusinessControlState.ManagerRun => ManagerRunStrength,
                _ => 0f
            };
        }

        public static float CalculateReorderThreshold01(
            float baseThreshold01,
            BusinessControlState controlState,
            ManagerPolicyPreset policy)
        {
            float strength = GetDelegationStrength01(controlState);
            float delta = SanitizePolicy(policy) switch
            {
                ManagerPolicyPreset.ProfitFirst => ProfitFirstReorderThresholdDelta01,
                ManagerPolicyPreset.ReputationFirst => ReputationFirstReorderThresholdDelta01,
                _ => StabilityFirstReorderThresholdDelta01
            };

            return Mathf.Clamp01(baseThreshold01 + delta * strength);
        }

        public static int CalculateReorderBudgetCents(
            int baseBudgetCents,
            BusinessControlState controlState,
            ManagerPolicyPreset policy)
        {
            return Mathf.RoundToInt(Mathf.Max(0, baseBudgetCents) * CalculateMultiplier(
                controlState,
                policy,
                ProfitFirstBudgetMultiplier,
                ReputationFirstBudgetMultiplier,
                StabilityFirstBudgetMultiplier));
        }

        public static int CalculateCashReserveCents(
            int baseReserveCents,
            BusinessControlState controlState,
            ManagerPolicyPreset policy)
        {
            return Mathf.RoundToInt(Mathf.Max(0, baseReserveCents) * CalculateMultiplier(
                controlState,
                policy,
                ProfitFirstCashReserveMultiplier,
                ReputationFirstCashReserveMultiplier,
                StabilityFirstCashReserveMultiplier));
        }

        public static float CalculateMarginAdjustment01(BusinessControlState controlState, ManagerPolicyPreset policy)
        {
            float strength = GetDelegationStrength01(controlState);
            float fullAdjustment = SanitizePolicy(policy) switch
            {
                ManagerPolicyPreset.ProfitFirst => ProfitFirstMarginAdjustment01,
                ManagerPolicyPreset.ReputationFirst => ReputationFirstMarginAdjustment01,
                _ => 0f
            };

            return fullAdjustment * strength;
        }

        public static string GetDisplayName(BusinessControlState controlState)
        {
            return controlState switch
            {
                BusinessControlState.Assisted => "Assisted",
                BusinessControlState.ManagerRun => "Manager Run",
                _ => "Player Managed"
            };
        }

        public static string GetDisplayName(ManagerPolicyPreset policy)
        {
            return SanitizePolicy(policy) switch
            {
                ManagerPolicyPreset.ProfitFirst => "Profit First",
                ManagerPolicyPreset.ReputationFirst => "Reputation First",
                _ => "Stability First"
            };
        }

        public static string BuildControlPolicyLine(BusinessInstanceState business)
        {
            if (business == null)
            {
                return "Control: unavailable";
            }

            return $"Control: {GetDisplayName(business.ControlState)} | Policy: {GetDisplayName(business.ManagerPolicy)} | {BuildPolicyEffectLine(business.ControlState, business.ManagerPolicy)}";
        }

        public static string BuildPolicyEffectLine(BusinessControlState controlState, ManagerPolicyPreset policy)
        {
            float strength = GetDelegationStrength01(controlState);
            if (strength <= 0f)
            {
                return "Policy effect: no automatic effect until Assisted or Manager Run control is used.";
            }

            ManagerPolicyPreset sanitized = SanitizePolicy(policy);
            string scope = strength >= 0.99f ? "full manager effect" : "half manager effect";
            return sanitized switch
            {
                ManagerPolicyPreset.ProfitFirst => $"Policy effect: {scope}; leaner reserve, later reorders, firmer prices.",
                ManagerPolicyPreset.ReputationFirst => $"Policy effect: {scope}; earlier reorders, larger stock budget, softer prices.",
                _ => $"Policy effect: {scope}; largest reserve and stock budget, steady prices."
            };
        }

        public static string BuildPolicyTelemetryLine(
            int baseReserveCents,
            int baseReorderBudgetCents,
            float baseReorderThreshold01,
            BusinessControlState controlState,
            ManagerPolicyPreset policy)
        {
            float threshold = CalculateReorderThreshold01(baseReorderThreshold01, controlState, policy);
            int reserve = CalculateCashReserveCents(baseReserveCents, controlState, policy);
            int budget = CalculateReorderBudgetCents(baseReorderBudgetCents, controlState, policy);
            float marginAdjustment = CalculateMarginAdjustment01(controlState, policy);
            string marginRead = marginAdjustment == 0f
                ? "price delta 0%"
                : $"price delta {(marginAdjustment >= 0f ? "+" : string.Empty)}{Mathf.RoundToInt(marginAdjustment * 100f)}%";
            return $"Policy telemetry: cash floor {FormatMoney(reserve)} | reorder budget {FormatMoney(budget)} | reorder below {Mathf.RoundToInt(threshold * 100f)}% stock | {marginRead}.";
        }

        private static float CalculateMultiplier(
            BusinessControlState controlState,
            ManagerPolicyPreset policy,
            float profitFirstMultiplier,
            float reputationFirstMultiplier,
            float stabilityFirstMultiplier)
        {
            float strength = GetDelegationStrength01(controlState);
            float target = SanitizePolicy(policy) switch
            {
                ManagerPolicyPreset.ProfitFirst => profitFirstMultiplier,
                ManagerPolicyPreset.ReputationFirst => reputationFirstMultiplier,
                _ => stabilityFirstMultiplier
            };

            return Mathf.Max(0f, Mathf.Lerp(1f, target, strength));
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Mathf.Max(0, cents) / 100f).ToString("N2");
        }
    }
}
