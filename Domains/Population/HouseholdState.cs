using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    [Serializable]
    public sealed class HouseholdState
    {
        public int id;
        public string householdName;
        public string surname;
        /// <summary>
        /// HF-3: the household's HF-1 typed identity. Backfilled as EntityId(Household, id)
        /// for pre-existing households; the M1 int allocator remains the id-space authority.
        /// </summary>
        public EntityId entityId;
        /// <summary>
        /// HF-3: household lifecycle state (Active / Transitional / Dissolved). Membership
        /// itself is authoritative in HouseholdMembershipRegistry (PKG-8).
        /// </summary>
        public HouseholdLifecycleState lifecycleState;
        /// <summary>
        /// HF-3 / GHOST-DEF-006: true when this household is the scenario's player
        /// household — a real simulated household, not an omitted special case.
        /// </summary>
        public bool isPlayerHousehold;
        public string playerScenarioId = string.Empty;
        /// <summary>
        /// PKG-8 (3A-D14/3A-D07): legacy dual-write list. HouseholdMembershipRegistry is the
        /// sole membership authority; the member list is a DERIVED reverse index
        /// (HouseholdMembershipRegistry.GetActiveMembers), never separately persisted.
        /// Kept for save compatibility and migration evidence only.
        /// </summary>
        public List<int> memberIds = new();
        public int homeBuildingId;
        public int weeklyIncomeSnapshot;
        public int spendingMoneyCents;
        public int lastStoreSpendCents;
        public int lifetimeStoreSpendCents;
        public HouseholdDemandSnapshot demandSnapshot;
        public HouseholdReservePressureSnapshot reservePressureSnapshot = HouseholdReservePressureSnapshot.Empty();
        public List<HouseholdUpgradeState> upgrades = new();
        public List<HouseholdReserveState> reserves = new();
        public int foodReserveUnits;
        public int lastReserveDepletionDayIndex = -1;
        public int lastDailyReserveUseUnits;
        public int lastDailyReserveLocalPurchaseUnits;
        public int lastDailyReserveOffMapPurchaseUnits;
        public int lastWeeklyUpgradeIncomeCents;
        public int lastWeeklyUpgradeUpkeepCents;
        public int lastWeeklyReserveDeltaUnits;
        public int lastDailyMedicalSpendCents;
        public int lastWeeklyLaborLossCents;
        public int lifetimeMedicalSpendCents;
        public HouseholdHeatRetentionTier heatRetentionTier = HouseholdHeatRetentionTier.Basic;
        public HouseholdStoveTier stoveTier = HouseholdStoveTier.Basic;
        public HouseholdFuelType preferredFuel = HouseholdFuelType.Wood;
        public float lastWinterStrain01;
        public float lastHeatingPressure01;
        public int lastHeatingFuelUseUnits;
        public int lastHeatingFuelShortfallUnits;
        public NewcomerArrivalProfile arrivalProfile = NewcomerArrivalProfile.SettledResident;
        public SettlementArrangement settlementArrangement = SettlementArrangement.StableHousehold;
        public HouseholdDwellingKind dwellingKind = HouseholdDwellingKind.OwnedHome;
        public string dwellingSummary = string.Empty;
        public SettlementPressureState settlementPressure = SettlementPressureState.Create(
            SettlementArrangement.StableHousehold,
            1f,
            0.05f,
            0.88f,
            0,
            0.22f,
            "Stable household");
        public int settlementReserveStrengthCents;
        public int baseBoardingCapacity;
        public int boardingCapacity;
        public List<int> boarderPersonIds = new();
        public bool hostsBoarders;
        public bool isBoardingHouseLodging;
        public string boardingBusinessInstanceId = string.Empty;
        public bool isBoardingHouseGuestHousehold;
        public string lodgingBusinessInstanceId = string.Empty;
        public bool isRenterHousehold;
        public bool hasKinAbsorptionPressure;
        public bool isTransientHousehold;
        public float crowdingPressure01;
        public float boarderOverloadPressure01;
        public float kinAbsorptionPressure01;
        public string lastSettlementSummary = string.Empty;

        public void EnsureHouseholdUpgradesInitialized()
        {
            EnsureUpgradeListInitialized();
            EnsureSettlementStateInitialized();
        }

        public void EnsureHouseholdReservesInitialized(bool fillMissingToTarget = false, bool migrateLegacyFoodReserve = false)
        {
            EnsureUpgradeListInitialized();
            EnsureSettlementStateInitialized();
            lastDailyReserveUseUnits = Math.Max(0, lastDailyReserveUseUnits);
            lastDailyReserveLocalPurchaseUnits = Math.Max(0, lastDailyReserveLocalPurchaseUnits);
            lastDailyReserveOffMapPurchaseUnits = Math.Max(0, lastDailyReserveOffMapPurchaseUnits);
            EnsureHeatingStateInitialized();
            EnsureReserveListInitialized(fillMissingToTarget, migrateLegacyFoodReserve);
        }

        public HouseholdReserveState GetReserve(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return null;
            }

            EnsureHouseholdReservesInitialized();
            for (int i = 0; i < reserves.Count; i++)
            {
                HouseholdReserveState reserve = reserves[i];
                if (reserve != null && string.Equals(reserve.categoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return reserve;
                }
            }

            return null;
        }

        public void SyncLegacyFoodReserveFromReserves()
        {
            HouseholdReserveState foodReserve = GetReserve("staple_food");
            foodReserveUnits = foodReserve != null ? Math.Max(0, foodReserve.currentUnits) : Math.Max(0, foodReserveUnits);
        }

        public HouseholdReservePressureSnapshot RefreshReservePressureSnapshot()
        {
            EnsureHouseholdReservesInitialized();
            reservePressureSnapshot = HouseholdReserveEvaluator.BuildPressureSnapshot(this);
            return reservePressureSnapshot;
        }

        public void EnsureSettlementStateInitialized()
        {
            NewcomerSettlementEvaluator.EnsureHouseholdSettlementInitialized(this);
        }

        private void EnsureUpgradeListInitialized()
        {
            upgrades ??= new List<HouseholdUpgradeState>();
            for (int i = upgrades.Count - 1; i >= 0; i--)
            {
                if (upgrades[i] == null)
                {
                    upgrades.RemoveAt(i);
                }
            }

            foodReserveUnits = Math.Max(0, foodReserveUnits);
            lastWeeklyUpgradeIncomeCents = Math.Max(0, lastWeeklyUpgradeIncomeCents);
            lastWeeklyUpgradeUpkeepCents = Math.Max(0, lastWeeklyUpgradeUpkeepCents);
            lastDailyMedicalSpendCents = Math.Max(0, lastDailyMedicalSpendCents);
            lastWeeklyLaborLossCents = Math.Max(0, lastWeeklyLaborLossCents);
            lifetimeMedicalSpendCents = Math.Max(0, lifetimeMedicalSpendCents);
        }

        private void EnsureHeatingStateInitialized()
        {
            if (!Enum.IsDefined(typeof(HouseholdHeatRetentionTier), heatRetentionTier))
            {
                heatRetentionTier = HouseholdHeatRetentionTier.Basic;
            }

            if (!Enum.IsDefined(typeof(HouseholdStoveTier), stoveTier))
            {
                stoveTier = HouseholdStoveTier.Basic;
            }

            if (!Enum.IsDefined(typeof(HouseholdFuelType), preferredFuel))
            {
                preferredFuel = HouseholdFuelType.Wood;
            }

            lastWinterStrain01 = Clamp01(lastWinterStrain01);
            lastHeatingPressure01 = Clamp01(lastHeatingPressure01);
            lastHeatingFuelUseUnits = Math.Max(0, lastHeatingFuelUseUnits);
            lastHeatingFuelShortfallUnits = Math.Max(0, lastHeatingFuelShortfallUnits);
        }

        private static float Clamp01(float value)
        {
            return Math.Min(1f, Math.Max(0f, value));
        }

        private void EnsureReserveListInitialized(bool fillMissingToTarget, bool migrateLegacyFoodReserve)
        {
            reserves ??= new List<HouseholdReserveState>();
            for (int i = reserves.Count - 1; i >= 0; i--)
            {
                HouseholdReserveState reserve = reserves[i];
                if (reserve == null || string.IsNullOrWhiteSpace(reserve.categoryId))
                {
                    reserves.RemoveAt(i);
                }
            }

            IReadOnlyList<HouseholdReserveDefinition> definitions = HouseholdReserveCatalog.All;
            for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                HouseholdReserveDefinition definition = definitions[definitionIndex];
                HouseholdReserveState reserve = FindReserveWithoutInitializing(definition.CategoryId);
                int target = HouseholdReserveCatalog.GetTargetUnits(definition, this);
                if (reserve == null)
                {
                    int startingUnits = fillMissingToTarget ? target : 0;
                    if (migrateLegacyFoodReserve && string.Equals(definition.CategoryId, "staple_food", StringComparison.OrdinalIgnoreCase))
                    {
                        startingUnits = foodReserveUnits;
                    }

                    reserve = new HouseholdReserveState
                    {
                        categoryId = definition.CategoryId,
                        displayName = definition.DisplayName,
                        currentUnits = Math.Max(0, startingUnits)
                    };
                    reserves.Add(reserve);
                }

                reserve.ApplyDefinition(definition, this);
            }

            RemoveDuplicateReserveEntries();
            SyncLegacyFoodReserveFromReservesWithoutInitializing();
        }

        private HouseholdReserveState FindReserveWithoutInitializing(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId) || reserves == null)
            {
                return null;
            }

            for (int i = 0; i < reserves.Count; i++)
            {
                HouseholdReserveState reserve = reserves[i];
                if (reserve != null && string.Equals(reserve.categoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return reserve;
                }
            }

            return null;
        }

        private void RemoveDuplicateReserveEntries()
        {
            for (int i = reserves.Count - 1; i >= 0; i--)
            {
                HouseholdReserveState reserve = reserves[i];
                if (reserve == null)
                {
                    reserves.RemoveAt(i);
                    continue;
                }

                for (int earlierIndex = 0; earlierIndex < i; earlierIndex++)
                {
                    HouseholdReserveState earlier = reserves[earlierIndex];
                    if (earlier != null && string.Equals(earlier.categoryId, reserve.categoryId, StringComparison.OrdinalIgnoreCase))
                    {
                        reserves.RemoveAt(i);
                        break;
                    }
                }
            }
        }


        public int MemberCount => memberIds != null ? memberIds.Count : 0;
        public int BoarderCount => boarderPersonIds != null ? boarderPersonIds.Count : 0;
        public int ConsumptionMemberCount => Math.Max(1, MemberCount + BoarderCount);
        public bool HasBoardingPressure => boarderOverloadPressure01 > 0.01f || crowdingPressure01 > 0.01f || hasKinAbsorptionPressure;
        public bool HasHeatingPressure => lastHeatingPressure01 > 0.05f || lastHeatingFuelShortfallUnits > 0 || lastWinterStrain01 > 0.05f;
        public bool HasMedicalPressure => lastDailyMedicalSpendCents > 0 || lastWeeklyLaborLossCents > 0;

        public string BuildReserveSummary()
        {
            HouseholdReservePressureSnapshot pressure = RefreshReservePressureSnapshot();
            if (reserves == null || reserves.Count == 0)
            {
                return "No reserves tracked.";
            }

            List<HouseholdReserveState> prioritized = new();
            for (int i = 0; i < reserves.Count; i++)
            {
                HouseholdReserveState reserve = reserves[i];
                if (reserve == null)
                {
                    continue;
                }

                bool shouldShow = ReserveNeedsAttention(reserve)
                    || string.Equals(reserve.categoryId, pressure.topPressureCategoryId, StringComparison.OrdinalIgnoreCase)
                    || reserve.currentUnits > 0
                    || reserve.lowThresholdUnits > 0;
                if (shouldShow)
                {
                    prioritized.Add(reserve);
                }
            }

            prioritized.Sort((left, right) => CompareReserveDisplayPriority(left, right));

            StringBuilder builder = new();
            builder.Append(string.IsNullOrWhiteSpace(pressure.headline) ? "Household reserves checked." : pressure.headline);
            if (!string.IsNullOrWhiteSpace(pressure.recommendedAction) && pressure.pressureBand != HouseholdReservePressureBand.Stable)
            {
                builder.Append(" | Action: ");
                builder.Append(pressure.recommendedAction);
            }

            int shown = 0;
            for (int i = 0; i < prioritized.Count && shown < 3; i++)
            {
                string segment = BuildReserveSummarySegment(prioritized[i]);
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                builder.Append(" | ");
                builder.Append(segment);
                shown++;
            }

            return builder.ToString();
        }

        public string BuildHeatingSummary()
        {
            EnsureHouseholdReservesInitialized();

            string fuel = BuildFriendlyFuelName(preferredFuel);
            string shell = BuildFriendlyHeatRetentionName(heatRetentionTier);
            string stove = BuildFriendlyStoveName(stoveTier);

            if (!HasHeatingPressure)
            {
                return $"Heating stable | {fuel} | {shell} shell | {stove} stove";
            }

            StringBuilder builder = new();
            builder.Append("Heating pressure ");
            builder.Append(Math.Round(lastHeatingPressure01 * 100f));
            builder.Append("% | ");
            builder.Append(fuel);
            builder.Append(" | ");
            builder.Append(shell);
            builder.Append(" shell | ");
            builder.Append(stove);
            builder.Append(" stove");

            if (lastHeatingFuelShortfallUnits > 0)
            {
                builder.Append(" | shortfall ");
                builder.Append(lastHeatingFuelShortfallUnits);
            }

            if (lastHeatingFuelUseUnits > 0)
            {
                builder.Append(" | fuel use ");
                builder.Append(lastHeatingFuelUseUnits);
            }

            if (lastWinterStrain01 > 0.05f)
            {
                builder.Append(" | winter strain ");
                builder.Append(Math.Round(lastWinterStrain01 * 100f));
                builder.Append("%");
            }

            return builder.ToString();
        }

        public string BuildInspectionSummary()
        {
            EnsureHouseholdReservesInitialized();

            string homeRead = homeBuildingId > 0 ? $"Home {homeBuildingId}" : "No home assigned";
            int totalDemandUnits = Math.Max(0, demandSnapshot.foodNeed)
                + Math.Max(0, demandSnapshot.generalGoodsNeed)
                + Math.Max(0, demandSnapshot.medicineNeed)
                + Math.Max(0, demandSnapshot.heatingNeed);
            return $"{homeRead} | Spend {spendingMoneyCents}c | Demand {totalDemandUnits}u | {BuildReserveSummary()} | {BuildHeatingSummary()} | {BuildSettlementSummaryLine()}";
        }

        public string BuildUpgradeSummary()
        {
            EnsureHouseholdUpgradesInitialized();
            if (upgrades == null || upgrades.Count == 0)
            {
                return "No household upgrades";
            }

            int activeCount = 0;
            for (int i = 0; i < upgrades.Count; i++)
            {
                if (upgrades[i] != null)
                {
                    activeCount++;
                }
            }

            return $"{activeCount} upgrade{(activeCount == 1 ? string.Empty : "s")} | income {lastWeeklyUpgradeIncomeCents}c | upkeep {lastWeeklyUpgradeUpkeepCents}c";
        }

        private static string BuildFriendlyFuelName(HouseholdFuelType fuelType)
        {
            return fuelType switch
            {
                HouseholdFuelType.Wood => "Wood fuel",
                _ => fuelType.ToString()
            };
        }

        private static string BuildFriendlyHeatRetentionName(HouseholdHeatRetentionTier tier)
        {
            return tier switch
            {
                HouseholdHeatRetentionTier.Basic => "basic",
                HouseholdHeatRetentionTier.Drafty => "drafty",
                HouseholdHeatRetentionTier.Tight => "tight",
                _ => tier.ToString()
            };
        }

        private static string BuildFriendlyStoveName(HouseholdStoveTier tier)
        {
            return tier switch
            {
                HouseholdStoveTier.Basic => "basic",
                HouseholdStoveTier.Efficient => "efficient",
                _ => tier.ToString()
            };
        }

        public string BuildBoardingSummary()
        {
            EnsureSettlementStateInitialized();
            if (settlementArrangement == SettlementArrangement.StableHousehold && BoarderCount <= 0 && boardingCapacity <= 0)
            {
                return "Stable household.";
            }

            StringBuilder builder = new();
            builder.Append(settlementArrangement.ToString());
            if (boardingCapacity > 0)
            {
                builder.Append(" | boarding ");
                builder.Append(BoarderCount);
                builder.Append('/');
                builder.Append(boardingCapacity);
            }

            if (hostsBoarders)
            {
                builder.Append(" | hosts boarders");
            }

            if (isBoardingHouseGuestHousehold || isBoardingHouseLodging)
            {
                builder.Append(" | boarding-house lodging");
            }

            if (isRenterHousehold)
            {
                builder.Append(" | renter");
            }

            if (isTransientHousehold)
            {
                builder.Append(" | transient");
            }

            builder.Append(" | dwelling ");
            builder.Append(NewcomerSettlementEvaluator.GetDwellingDisplayName(dwellingKind));

            return builder.ToString();
        }

        public string BuildPressureSummary()
        {
            EnsureSettlementStateInitialized();
            List<string> parts = new();
            if (crowdingPressure01 > 0.05f)
            {
                parts.Add($"crowding {Math.Round(crowdingPressure01 * 100f)}%");
            }

            if (boarderOverloadPressure01 > 0.05f)
            {
                parts.Add($"boarding strain {Math.Round(boarderOverloadPressure01 * 100f)}%");
            }

            if (kinAbsorptionPressure01 > 0.05f)
            {
                parts.Add($"kin pressure {Math.Round(kinAbsorptionPressure01 * 100f)}%");
            }

            if (lastWeeklyLaborLossCents > 0)
            {
                parts.Add($"labor loss ${Math.Round(lastWeeklyLaborLossCents / 100f, 2):0.00}");
            }

            if (lastDailyMedicalSpendCents > 0)
            {
                parts.Add($"medical spend ${Math.Round(lastDailyMedicalSpendCents / 100f, 2):0.00}");
            }

            return parts.Count > 0 ? string.Join(" | ", parts) : "Pressure stable.";
        }

        public string BuildSettlementSummaryLine()
        {
            EnsureSettlementStateInitialized();
            List<string> parts = new();

            string arrangement = settlementArrangement switch
            {
                SettlementArrangement.StableHousehold => "stable household",
                SettlementArrangement.Boarding => "boarding guest",
                SettlementArrangement.Renting => "renter household",
                SettlementArrangement.Kin => "kin-linked household",
                SettlementArrangement.Transient => "transient household",
                SettlementArrangement.Departed => "departed household",
                _ => settlementArrangement.ToString()
            };

            parts.Add(arrangement);

            if (boardingCapacity > 0 || BoarderCount > 0)
            {
                parts.Add($"rooms {BoarderCount}/{Math.Max(0, boardingCapacity)}");
            }

            if (hostsBoarders)
            {
                parts.Add("hosts boarders");
            }

            if (isBoardingHouseGuestHousehold || isBoardingHouseLodging)
            {
                parts.Add("boarding-house stay");
            }

            if (isRenterHousehold)
            {
                parts.Add("renting");
            }

            if (isTransientHousehold)
            {
                parts.Add("rough lodging");
            }

            if (hasKinAbsorptionPressure)
            {
                parts.Add("kin support");
            }

            return string.Join(" | ", parts);
        }

        public string BuildDwellingSummaryLine()
        {
            EnsureSettlementStateInitialized();
            List<string> parts = new()
            {
                NewcomerSettlementEvaluator.GetDwellingDisplayName(dwellingKind)
            };

            if (!string.IsNullOrWhiteSpace(dwellingSummary)
                && !string.Equals(dwellingSummary, parts[0], StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(dwellingSummary);
            }

            if (dwellingKind == HouseholdDwellingKind.RoughTemporaryLodging)
            {
                parts.Add("unstable");
                parts.Add("labor reliability reduced");
            }

            if (crowdingPressure01 > 0.05f)
            {
                parts.Add($"crowding {Math.Round(crowdingPressure01 * 100f)}%");
            }

            if (BoarderCount > 0 || boardingCapacity > 0)
            {
                parts.Add($"rooms {BoarderCount}/{Math.Max(0, boardingCapacity)}");
            }

            return string.Join(" | ", parts);
        }


        private static int CompareReserveDisplayPriority(HouseholdReserveState left, HouseholdReserveState right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            bool leftNeedsAttention = ReserveNeedsAttention(left);
            bool rightNeedsAttention = ReserveNeedsAttention(right);
            if (leftNeedsAttention != rightNeedsAttention)
            {
                return leftNeedsAttention ? -1 : 1;
            }

            float leftAttention = GetReserveAttentionScore(left);
            float rightAttention = GetReserveAttentionScore(right);
            int attentionComparison = rightAttention.CompareTo(leftAttention);
            if (attentionComparison != 0)
            {
                return attentionComparison;
            }

            float leftFill01 = GetReserveFill01(left);
            float rightFill01 = GetReserveFill01(right);
            int fillComparison = leftFill01.CompareTo(rightFill01);
            if (fillComparison != 0)
            {
                return fillComparison;
            }

            return string.Compare(
                string.IsNullOrWhiteSpace(left.displayName) ? left.categoryId : left.displayName,
                string.IsNullOrWhiteSpace(right.displayName) ? right.categoryId : right.displayName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReserveNeedsAttention(HouseholdReserveState reserve)
        {
            if (reserve == null)
            {
                return false;
            }

            return reserve.currentUnits <= reserve.lowThresholdUnits
                || reserve.lastUrgency01 > 0.1f
                || reserve.rememberedStockoutUnits > 0
                || reserve.consecutiveLocalShortfallDays > 0
                || reserve.stockoutFrustration01 > 0.08f;
        }

        private static float GetReserveAttentionScore(HouseholdReserveState reserve)
        {
            if (reserve == null)
            {
                return 0f;
            }

            float fillPenalty = 1f - GetReserveFill01(reserve);
            float urgency = Math.Max(0f, reserve.lastUrgency01);
            float stockoutMemory = reserve.rememberedStockoutUnits > 0
                ? Math.Min(1f, reserve.rememberedStockoutUnits / (float)Math.Max(1, reserve.targetUnits + reserve.lowThresholdUnits + 2))
                : 0f;
            float shortfallPressure = Math.Min(1f, reserve.consecutiveLocalShortfallDays / 4f);
            return fillPenalty * 0.42f
                + urgency * 0.28f
                + Math.Max(0f, reserve.stockoutFrustration01) * 0.18f
                + stockoutMemory * 0.08f
                + shortfallPressure * 0.04f;
        }

        private static float GetReserveFill01(HouseholdReserveState reserve)
        {
            if (reserve == null)
            {
                return 0f;
            }

            int target = Math.Max(1, reserve.targetUnits);
            return Math.Min(1f, Math.Max(0f, reserve.currentUnits / (float)target));
        }

        private static string BuildReserveSummarySegment(HouseholdReserveState reserve)
        {
            if (reserve == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            builder.Append(string.IsNullOrWhiteSpace(reserve.displayName) ? reserve.categoryId : reserve.displayName);
            builder.Append(' ');
            builder.Append(Math.Max(0, reserve.currentUnits));

            int targetUnits = Math.Max(Math.Max(0, reserve.lowThresholdUnits), Math.Max(0, reserve.targetUnits));
            if (targetUnits > 0)
            {
                builder.Append('/');
                builder.Append(targetUnits);
            }

            int reserveThreshold = HouseholdReserveEvaluator.GetReserveThresholdUnits(reserve);
            int criticalThreshold = HouseholdReserveEvaluator.GetCriticalThresholdUnits(reserve);
            if (criticalThreshold > 0 && reserve.currentUnits <= criticalThreshold)
            {
                builder.Append(" critical");
            }
            else if (reserveThreshold > 0 && reserve.currentUnits <= reserveThreshold)
            {
                builder.Append(" low");
            }

            if (reserve.lastRequestedPurchaseUnits > 0)
            {
                builder.Append(" | wants ");
                builder.Append(reserve.lastRequestedPurchaseUnits);
            }

            if (reserve.consecutiveLocalShortfallDays > 0 || reserve.rememberedStockoutUnits > 0)
            {
                builder.Append(" | stockout memory");
                if (reserve.rememberedStockoutUnits > 0)
                {
                    builder.Append(' ');
                    builder.Append(reserve.rememberedStockoutUnits);
                }
            }
            else if (reserve.stockoutFrustration01 > 0.08f)
            {
                builder.Append(" | supply frustration ");
                builder.Append(Math.Round(reserve.stockoutFrustration01 * 100f));
                builder.Append('%');
            }

            return builder.ToString();
        }

        private bool ShouldSurfaceReserveSummaryInInspection()
        {
            HouseholdReservePressureSnapshot pressure = RefreshReservePressureSnapshot();
            return pressure.pressureBand != HouseholdReservePressureBand.Stable
                || pressure.pressureScore >= 18
                || HasHeatingPressure;
        }

        private void SyncLegacyFoodReserveFromReservesWithoutInitializing()
        {
            HouseholdReserveState foodReserve = FindReserveWithoutInitializing("staple_food");
            if (foodReserve != null)
            {
                foodReserveUnits = Math.Max(0, foodReserve.currentUnits);
            }
        }
    }
}
