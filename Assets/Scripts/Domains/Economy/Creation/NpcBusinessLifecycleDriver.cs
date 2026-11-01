using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// G-01: the weekly source-side driver for NPC business lifecycles.
    /// NpcBusinessLifecycleService.EvaluateWeek was fully implemented and
    /// tested but had zero production callers — in a live game NPC businesses
    /// could never struggle, respond, pivot, or close orderly. Each week this
    /// driver sources a life record for every live NPC-founded business
    /// (creating one in Operating state when formation left none), builds the
    /// NpcLifecycleContext from hub authorities, and evaluates with a
    /// per-business-type survival reserve. Terminal records
    /// (Closed/Closing/Acquired) are left alone; lifecycle internals
    /// (EvaluateWeek, CloseOrderly) are not modified here.
    /// </summary>
    public sealed class NpcBusinessLifecycleDriver
    {
        /// <summary>One live NPC-founded business, sourced from the business runtime.</summary>
        public sealed class NpcLifecycleBusinessInfo
        {
            public string BusinessInstanceId = string.Empty;
            public BusinessType BusinessType;
            public int OwnerPersonId = -1;
            public string OwnerName = string.Empty;
            /// <summary>The live instance, for calibrated reserve resolution. May be null in tests.</summary>
            public BusinessInstanceState Business;
        }

        /// <summary>All per-call inputs. Ports may be null (treated as absent).</summary>
        public sealed class LifecycleDriverContext
        {
            public IList<NpcBusinessLifeRecord> LifeRecords;
            public List<NpcLifecycleBusinessInfo> NpcBusinesses;
            public INpcBusinessCashPort Cash;
            public FinancialObligationAuthority Obligations;
            public EmploymentRelationshipRegistry Employments;
            public Func<int, HouseholdLedger> FounderLedgerByPersonId;
            public NpcFormationAssetRegister AssetRegister;
            public INpcLiquidationPort Liquidation;
            public NpcBusinessEventLog Events;
            public EntityIdRegistry Ids;
            public CreditCashBridge CreditorCash;
            /// <summary>
            /// Calibrated survival reserve per business. Null selects the
            /// built-in per-type tuning table (<see cref="DefaultSurvivalReserveCents"/>).
            /// </summary>
            public Func<NpcLifecycleBusinessInfo, int> SurvivalReserveCents;
            public int DayIndex;
        }

        private readonly NpcBusinessLifecycleService lifecycle = new NpcBusinessLifecycleService();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Evaluates one week of NPC business lifecycles.</summary>
        public void EvaluateWeek(LifecycleDriverContext ctx, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ctx == null)
            {
                diag.Add("BusinessLifecycle: no context — nothing evaluated.");
                return;
            }

            if (ctx.LifeRecords == null || ctx.NpcBusinesses == null)
            {
                diag.Add("BusinessLifecycle: life records or business source missing — nothing evaluated.");
                return;
            }

            foreach (NpcLifecycleBusinessInfo info in ctx.NpcBusinesses)
            {
                if (info == null || string.IsNullOrWhiteSpace(info.BusinessInstanceId))
                    continue;

                NpcBusinessLifeRecord record = FindOrCreateRecord(ctx, info, diag);
                if (record == null)
                    continue;
                if (record.State == NpcBusinessLifeState.Closed
                    || record.State == NpcBusinessLifeState.Closing
                    || record.State == NpcBusinessLifeState.Acquired)
                    continue;

                var context = new NpcLifecycleContext
                {
                    Cash = ctx.Cash,
                    Obligations = ctx.Obligations,
                    Employments = ctx.Employments,
                    FounderHouseholdLedger = ctx.FounderLedgerByPersonId != null && record.FounderPersonId >= 0
                        ? ctx.FounderLedgerByPersonId(record.FounderPersonId)
                        : null,
                    AssetRegister = ctx.AssetRegister,
                    Liquidation = ctx.Liquidation,
                    Events = ctx.Events,
                    Ids = ctx.Ids,
                    DayIndex = ctx.DayIndex,
                    CreditorCash = ctx.CreditorCash,
                };
                int reserve = ResolveSurvivalReserve(ctx, info);
                int seekSpend = CustomerSeekingSpendCents(reserve);
                // Borrow context is null in this pass: a struggling business
                // still cuts costs and seeks customers with real money; the
                // real credit-loop borrow path (BorrowForBusiness) stays
                // available for a later pass with a production lender wired.
                lifecycle.EvaluateWeek(record, reserve, seekSpend, context, null, null, diag);
            }
        }

        private static NpcBusinessLifeRecord FindOrCreateRecord(
            LifecycleDriverContext ctx, NpcLifecycleBusinessInfo info, List<string> diag)
        {
            foreach (NpcBusinessLifeRecord existing in ctx.LifeRecords)
            {
                if (existing != null && string.Equals(existing.BusinessInstanceId,
                    info.BusinessInstanceId, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            // Formation does not leave a life record today; the driver sources
            // one. The business is already formed and trading, so it starts
            // Operating — never Forming (EvaluateWeek has no Forming exit).
            var record = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = info.BusinessInstanceId,
                BusinessEntityKey = "business:" + info.BusinessInstanceId,
                FounderPersonId = info.OwnerPersonId,
                OwnerName = info.OwnerName ?? string.Empty,
                State = NpcBusinessLifeState.Operating,
            };
            ctx.LifeRecords.Add(record);
            diag.Add($"BusinessLifecycle: sourced life record for NPC business '{info.BusinessInstanceId}' (Operating).");
            return record;
        }

        private static int ResolveSurvivalReserve(LifecycleDriverContext ctx, NpcLifecycleBusinessInfo info)
        {
            if (ctx.SurvivalReserveCents != null)
                return Math.Max(0, ctx.SurvivalReserveCents(info));
            return DefaultSurvivalReserveCents(info.BusinessType);
        }

        /// <summary>
        /// G-01 tuning: weekly customer-seeking spend for a struggling
        /// business — a bounded 5% of its survival reserve, real money.
        /// </summary>
        public static int CustomerSeekingSpendCents(int survivalReserveCents)
        {
            return Math.Max(0, survivalReserveCents / 20);
        }

        /// <summary>
        /// G-01 tuning: fallback survival reserve per business type, used when
        /// the caller supplies no calibrated resolver. Calibration, not canon:
        /// roughly a month of operating buffer for a typical shop; the
        /// capital-intensive trades (mine, ranch, freight, mills, logging,
        /// hotel, elevator) carry more. Production passes the shared
        /// calibrated reserve (payroll + reorder based) instead.
        /// </summary>
        public static int DefaultSurvivalReserveCents(BusinessType businessType)
        {
            switch (businessType)
            {
                case BusinessType.Mine: return 60000;
                case BusinessType.Ranch: return 40000;
                case BusinessType.LiveryFreight: return 40000;
                case BusinessType.Sawmill: return 35000;
                case BusinessType.Logging: return 35000;
                case BusinessType.GrainMill: return 30000;
                case BusinessType.GrainElevator: return 30000;
                case BusinessType.Hotel: return 30000;
                case BusinessType.LumberYard: return 25000;
                case BusinessType.Bank: return 25000;
                case BusinessType.GeneralStore: return 20000;
                case BusinessType.BoardingHouse: return 20000;
                case BusinessType.Restaurant: return 20000;
                case BusinessType.Saloon: return 20000;
                case BusinessType.FuelDealer: return 20000;
                case BusinessType.Blacksmith: return 15000;
                case BusinessType.Wheelwright: return 15000;
                case BusinessType.Butcher: return 15000;
                case BusinessType.Bakery: return 15000;
                case BusinessType.Tailor: return 15000;
                case BusinessType.Tannery: return 15000;
                case BusinessType.Builder: return 15000;
                case BusinessType.Newspaper: return 12000;
                case BusinessType.Doctor: return 12000;
                case BusinessType.Lawyer: return 12000;
                case BusinessType.CropFarm: return 12000;
                case BusinessType.Barber: return 8000;
                case BusinessType.PostOffice: return 8000;
                default: return 15000;
            }
        }
    }
}
