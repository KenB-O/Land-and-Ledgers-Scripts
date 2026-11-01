using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Valuation
{
    public sealed class SellerPressureEvaluator
    {
        private const float UnknownPressureDefault01 = 0.35f;
        private const float UnknownUrgencyDefault01 = 0.35f;
        private const float UnknownDebtPressureDefault01 = 0.25f;
        private const float UnknownCashNeedDefault01 = 0.3f;
        private const float UnknownAttachmentDefault01 = 0.45f;
        private const float UnknownRelocationNeedDefault01 = 0.2f;
        private const float UnknownReputationSensitivityDefault01 = 0.45f;
        private const float UnknownBuyerPreferenceSensitivityDefault01 = 0.45f;
        private const float UnknownCounterofferFlexibilityDefault01 = 0.35f;

        public SellerPressureResult Evaluate(SellerProfile seller)
        {
            seller = seller.Sanitized();

            float motivePressure = GetMotivePressure(seller.motive);
            float motiveResistance = GetMotiveResistance(seller.motive);
            float pressureSignal = ResolveOptional01(seller.pressure01, UnknownPressureDefault01);
            float urgencySignal = ResolveOptional01(seller.urgency01, UnknownUrgencyDefault01);
            float debtPressureSignal = ResolveOptional01(seller.debtPressure01, UnknownDebtPressureDefault01);
            float cashNeedSignal = ResolveOptional01(seller.cashNeed01, UnknownCashNeedDefault01);
            float relocationNeedSignal = ResolveOptional01(seller.relocationNeed01, UnknownRelocationNeedDefault01);
            float attachment = ResolveOptional01(seller.attachment01, UnknownAttachmentDefault01);
            float reputationSensitivity = ResolveOptional01(seller.reputationSensitivity01, UnknownReputationSensitivityDefault01);
            float buyerPreferenceSensitivity = ResolveOptional01(seller.buyerPreferenceSensitivity01, UnknownBuyerPreferenceSensitivityDefault01);
            float counterofferFlexibilitySignal = ResolveOptional01(seller.counterofferFlexibility01, UnknownCounterofferFlexibilityDefault01);

            // Keep seller pressure and seller resistance separate. A seller can face real
            // pressure to move while still resisting the sale because of attachment,
            // strategic holdout behavior, or a softer market-testing posture.
            float sellDrive = Mathf.Clamp01(
                motivePressure * 0.26f
                + pressureSignal * 0.18f
                + urgencySignal * 0.16f
                + debtPressureSignal * 0.16f
                + cashNeedSignal * 0.15f
                + relocationNeedSignal * 0.09f);
            float sellResistance = Mathf.Clamp01(
                motiveResistance * 0.48f
                + attachment * 0.38f
                + Mathf.Max(0f, 0.25f - pressureSignal) * 0.14f);

            float pressure = sellDrive;
            float listedBonus = seller.listedForSale ? 0.12f : -0.1f;
            float willingness = Mathf.Clamp01(
                sellDrive * 0.6f
                + GetMotiveWillingness(seller.motive) * 0.22f
                + listedBonus
                - sellResistance * 0.34f);
            float relationshipSensitivity = Mathf.Clamp01(
                reputationSensitivity * 0.35f
                + buyerPreferenceSensitivity * 0.4f
                + GetMotiveRelationshipSensitivity(seller.motive) * 0.25f);
            float reservationMultiplier = Mathf.Clamp(
                1.06f
                - sellDrive * 0.28f
                - urgencySignal * 0.04f
                + sellResistance * 0.2f
                + GetMotiveReservationAdjustment(seller.motive),
                0.72f,
                1.4f);
            float counterFlexibility = Mathf.Clamp01(
                counterofferFlexibilitySignal * 0.55f
                + sellDrive * 0.2f
                + (seller.listedForSale ? 0.1f : 0f)
                - sellResistance * 0.18f
                + GetMotiveCounterFlexAdjustment(seller.motive));

            List<OfferEvaluationReasonCode> reasons = new List<OfferEvaluationReasonCode>();
            if (pressure >= 0.65f)
            {
                reasons.Add(OfferEvaluationReasonCode.SellerUnderPressure);
            }

            if (!seller.listedForSale && willingness < 0.45f)
            {
                reasons.Add(OfferEvaluationReasonCode.NotReadyToSell);
            }

            if (sellResistance >= 0.6f || seller.motive == SellerMotive.OwnerOperatorAttachment)
            {
                reasons.Add(OfferEvaluationReasonCode.SellerAttached);
            }

            bool usesProvisionalInputs = UsesProvisionalPressureInputs(seller);
            string pressureSummary = DescribePressureSummary(seller, sellDrive, sellResistance, pressure, usesProvisionalInputs);

            ScorecardRow row = new ScorecardRow(
                ValuationScorecardCategory.SellerPressure,
                pressure,
                pressureSummary,
                GetPressureBandDriver(pressure),
                GetMotiveDriver(seller.motive),
                seller.listedForSale ? "seller_listed" : "seller_private",
                GetResistanceDriver(sellResistance));

            return new SellerPressureResult(
                pressure,
                willingness,
                reservationMultiplier,
                relationshipSensitivity,
                counterFlexibility,
                row,
                reasons);
        }

        private static float ResolveOptional01(float value, float defaultValue)
        {
            return value < 0f ? Mathf.Clamp01(defaultValue) : Mathf.Clamp01(value);
        }

        private static bool UsesProvisionalPressureInputs(SellerProfile seller)
        {
            return !seller.HasExplicitPressure
                || !seller.HasExplicitUrgency
                || !seller.HasExplicitDebtPressure
                || !seller.HasExplicitCashNeed
                || !seller.HasExplicitAttachment
                || !seller.HasExplicitRelocationNeed
                || !seller.HasExplicitReputationSensitivity
                || !seller.HasExplicitBuyerPreferenceSensitivity
                || !seller.HasExplicitCounterofferFlexibility;
        }

        private static string DescribePressureSummary(SellerProfile seller, float sellDrive, float sellResistance, float pressure, bool usesProvisionalInputs)
        {
            string posture = seller.listedForSale ? "Listed seller" : "Private seller";
            string driver = GetPrimaryPressureDriverPhrase(seller, sellDrive);
            string resistance = GetPrimaryResistancePhrase(seller, sellResistance);

            string summary;
            if (pressure >= 0.7f)
            {
                summary = $"{posture} with strong sale pressure driven by {driver}.";
            }
            else if (pressure <= 0.35f)
            {
                summary = $"{posture} with limited outward pressure; main posture reads as {driver}.";
            }
            else
            {
                summary = $"{posture} with moderate sale pressure, mainly driven by {driver}.";
            }

            if (!string.IsNullOrEmpty(resistance))
            {
                summary += $" Resistance remains {resistance}.";
            }

            if (usesProvisionalInputs)
            {
                summary += " Some seller-pressure inputs are still provisional.";
            }

            return summary;
        }

        private static string DescribeWillingnessSummary(SellerProfile seller, float willingness)
        {
            if (willingness >= 0.7f)
            {
                return seller.listedForSale
                    ? "Seller is visibly willing to transact if the offer clears price and terms."
                    : "Seller is not publicly listed but appears reachable through a credible private approach.";
            }

            if (willingness <= 0.35f)
            {
                return "Seller willingness is weak; the deal likely needs pressure, trust, or a compelling premium.";
            }

            return "Seller willingness is mixed; clean closing and buyer credibility can materially affect the outcome.";
        }

        private static string DescribeResistanceSummary(SellerProfile seller, float sellResistance)
        {
            string resistance = GetPrimaryResistancePhrase(seller, sellResistance);
            if (!string.IsNullOrEmpty(resistance))
            {
                return $"Resistance remains {resistance}.";
            }

            return "Seller resistance is limited; practical sale pressure can carry more of the decision.";
        }

        private static string DescribeReservationSummary(float reservationMultiplier, float sellDrive, float sellResistance)
        {
            if (reservationMultiplier >= 1.1f)
            {
                return sellResistance >= sellDrive
                    ? "Reservation value is firm because resistance and attachment are outweighing sale pressure."
                    : "Reservation value is firm despite some sale pressure; seller still expects a premium.";
            }

            if (reservationMultiplier <= 0.92f)
            {
                return "Reservation value is softened by pressure; a clean, certain close can matter as much as a premium.";
            }

            return "Reservation value sits near baseline; price should track valuation more than extreme urgency or holdout behavior.";
        }

        private static string DescribeCounterofferFlexibilitySummary(float counterFlexibility)
        {
            if (counterFlexibility >= 0.65f)
            {
                return "Seller has enough flexibility to counter a near-miss offer instead of rejecting outright.";
            }

            if (counterFlexibility <= 0.3f)
            {
                return "Seller counteroffer flexibility is low; weak offers may end the negotiation quickly.";
            }

            return "Seller may counter a close offer, but only if the wider deal posture is credible.";
        }

        private static string GetPrimaryPressureDriverPhrase(SellerProfile seller, float sellDrive)
        {
            switch (seller.motive)
            {
                case SellerMotive.FinancialDistress:
                    return "financial distress";
                case SellerMotive.DebtPressure:
                    return "debt pressure";
                case SellerMotive.Relocation:
                    return "relocation pressure";
                case SellerMotive.Liquidating:
                    return "liquidation pressure";
                case SellerMotive.EstateSale:
                    return "estate turnover";
                case SellerMotive.Retirement:
                    return "retirement exit";
                case SellerMotive.TestingMarket:
                    return sellDrive >= 0.45f ? "selective market testing under some pressure" : "selective market testing";
                case SellerMotive.OwnerOperatorAttachment:
                    return "mixed owner pressure rather than a clean exit";
                case SellerMotive.StrategicHoldout:
                    return "strategic leverage rather than urgency";
                default:
                    return sellDrive >= 0.5f ? "general sale pressure" : "limited immediate pressure";
            }
        }

        private static string GetPrimaryResistancePhrase(SellerProfile seller, float sellResistance)
        {
            if (sellResistance < 0.45f)
            {
                return string.Empty;
            }

            return seller.motive switch
            {
                SellerMotive.StrategicHoldout => "high because the seller appears to be holding out for leverage or better terms",
                SellerMotive.OwnerOperatorAttachment => "high because the seller is strongly attached to the operation",
                SellerMotive.Holding => "elevated because the seller still seems comfortable holding the asset",
                SellerMotive.TestingMarket => "moderate because the seller may be testing the market more than pushing to close",
                _ => sellResistance >= 0.7f
                    ? "high because attachment is still limiting flexibility"
                    : "present because attachment is still limiting flexibility"
            };
        }

        private static string GetPressureBandDriver(float pressure)
        {
            return pressure >= 0.7f ? "seller_pressure_high" : pressure <= 0.35f ? "seller_pressure_low" : "seller_pressure_moderate";
        }

        private static string GetResistanceDriver(float resistance)
        {
            return resistance >= 0.7f ? "seller_resistance_high" : resistance <= 0.35f ? "seller_resistance_low" : "seller_resistance_moderate";
        }

        private static string GetMotiveDriver(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.FinancialDistress => "seller_motive_financial_distress",
                SellerMotive.DebtPressure => "seller_motive_debt_pressure",
                SellerMotive.Retirement => "seller_motive_retirement",
                SellerMotive.Relocation => "seller_motive_relocation",
                SellerMotive.EstateSale => "seller_motive_estate_sale",
                SellerMotive.StrategicHoldout => "seller_motive_strategic_holdout",
                SellerMotive.OwnerOperatorAttachment => "seller_motive_owner_attachment",
                SellerMotive.Liquidating => "seller_motive_liquidating",
                SellerMotive.TestingMarket => "seller_motive_testing_market",
                _ => "seller_motive_holding"
            };
        }

        private static float GetMotivePressure(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.FinancialDistress => 0.95f,
                SellerMotive.DebtPressure => 0.82f,
                SellerMotive.Liquidating => 0.76f,
                SellerMotive.Relocation => 0.66f,
                SellerMotive.EstateSale => 0.58f,
                SellerMotive.Retirement => 0.46f,
                SellerMotive.TestingMarket => 0.28f,
                SellerMotive.OwnerOperatorAttachment => 0.22f,
                SellerMotive.StrategicHoldout => 0.12f,
                _ => 0.18f
            };
        }

        private static float GetMotiveResistance(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.OwnerOperatorAttachment => 0.95f,
                SellerMotive.StrategicHoldout => 0.82f,
                SellerMotive.Holding => 0.62f,
                SellerMotive.TestingMarket => 0.48f,
                SellerMotive.Retirement => 0.28f,
                SellerMotive.EstateSale => 0.18f,
                SellerMotive.Relocation => 0.14f,
                SellerMotive.Liquidating => 0.1f,
                SellerMotive.DebtPressure => 0.08f,
                SellerMotive.FinancialDistress => 0.05f,
                _ => 0.3f
            };
        }

        private static float GetMotiveWillingness(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.FinancialDistress => 0.92f,
                SellerMotive.DebtPressure => 0.82f,
                SellerMotive.Liquidating => 0.78f,
                SellerMotive.Relocation => 0.7f,
                SellerMotive.EstateSale => 0.6f,
                SellerMotive.Retirement => 0.52f,
                SellerMotive.TestingMarket => 0.38f,
                SellerMotive.OwnerOperatorAttachment => 0.18f,
                SellerMotive.StrategicHoldout => 0.12f,
                _ => 0.22f
            };
        }

        private static float GetMotiveRelationshipSensitivity(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.Retirement => 0.78f,
                SellerMotive.OwnerOperatorAttachment => 0.92f,
                SellerMotive.StrategicHoldout => 0.72f,
                SellerMotive.EstateSale => 0.48f,
                SellerMotive.Holding => 0.42f,
                SellerMotive.FinancialDistress => 0.18f,
                SellerMotive.DebtPressure => 0.22f,
                _ => 0.32f
            };
        }

        private static float GetMotiveReservationAdjustment(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.FinancialDistress => -0.16f,
                SellerMotive.DebtPressure => -0.1f,
                SellerMotive.Liquidating => -0.07f,
                SellerMotive.Relocation => -0.04f,
                SellerMotive.Retirement => 0.03f,
                SellerMotive.OwnerOperatorAttachment => 0.18f,
                SellerMotive.StrategicHoldout => 0.22f,
                SellerMotive.Holding => 0.15f,
                _ => 0f
            };
        }

        private static float GetMotiveCounterFlexAdjustment(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.FinancialDistress => 0.06f,
                SellerMotive.DebtPressure => 0.04f,
                SellerMotive.Liquidating => 0.03f,
                SellerMotive.Relocation => 0.02f,
                SellerMotive.StrategicHoldout => -0.07f,
                SellerMotive.OwnerOperatorAttachment => -0.06f,
                SellerMotive.Holding => -0.03f,
                _ => 0f
            };
        }
    }
}
