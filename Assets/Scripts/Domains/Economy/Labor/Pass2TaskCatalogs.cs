using System;
using System.Collections.Generic;
using LandLedgers.Skills;
using LandLedgers.Tasks;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Pass 2 (task system): whether a slice action is person-time work or
    /// an instant decision. Instant actions are NOT TaskAuthority tasks and
    /// must never be dressed up as tasks to inflate labour demand: they
    /// take no worker, book no minutes, and create no hiring pressure.
    /// A task, by contrast, always flows through TaskAuthority with a
    /// skill hook (a real SkillIds value, an explicit hook id, or an
    /// explicit unskilled marker — never a silent gap).
    /// </summary>
    public enum Pass2ActionKind
    {
        Task = 0,
        InstantAction = 1,
    }

    /// <summary>One decided slice action: task or instant, with its reason on the record.</summary>
    public sealed class Pass2StoreActionDecision
    {
        public string ActionId = string.Empty;
        public string DisplayName = string.Empty;
        public Pass2ActionKind Kind;
        public string Reason = string.Empty;

        public Pass2StoreActionDecision() { }
    }

    /// <summary>
    /// Pass 2 (task system): slice-job task catalogs. Slice jobs FIRST, per
    /// the Pass 2 brief: the General Store's ordering, pricing, shelf/back
    /// and bookkeeping work, and the Fuel Dealer's own work, as real
    /// TaskAuthority definitions. Every definition carries a skill hook:
    /// SkillIds where one exists, a named hook id where the skill system
    /// has no id yet (hook only — Pass 2 does NOT invent a full skill
    /// system and does NOT retune experience formation), or an explicit
    /// unskilled marker (null skill id, which TaskDefinition already means
    /// as "unskilled timing").
    ///
    /// Tuning values below are starting values in the existing catalog
    /// style, NOT canon. Kennedy tunes numbers without touching logic.
    /// Durations are whole minutes (the current booking floor). Any
    /// sub-minute duration Kennedy described in examples is NOT encoded
    /// here: it is blocked on the open seconds decision (see
    /// Pass2SecondsBlocklist in Pass2DemandChains.cs).
    /// </summary>
    public static class Pass2TaskCatalogs
    {
        // ---- General Store: ordering and back-of-shop work ----
        public const string OrderFromSupplierId = "gs.order-from-supplier";
        public const string MoveShelfToBackId = "gs.move-shelf-to-back";
        public const string MoveBackToShelfId = "gs.move-back-to-shelf";
        public const string HandlePaymentId = "gs.handle-payment";
        public const string BookkeepingId = "gs.bookkeeping";

        // ---- Fuel Dealer: its OWN tasks (no borrowed Logging tasks) ----
        public const string FellTreeId = "fd.fell-tree";
        public const string HaulLogId = "fd.haul-log";
        public const string SplitFuelId = "fd.split-fuel";
        public const string StackFuelId = "fd.stack-fuel";
        public const string DispenseFuelId = "fd.dispense-fuel";
        public const string SellFuelId = "fd.sell-fuel";

        // ---- Life activities as tasks (authoring only; see note below) ----
        public const string EatMealId = "lf.eat-meal";
        public const string DrinkId = "lf.drink";
        public const string SleepId = "lf.sleep";

        /// <summary>
        /// Skill hook ids used where SkillIds has no constant yet. These are
        /// hook strings only, accepted by TaskDefinition.SetRequiredSkill
        /// exactly as any skill id is. They do NOT create skills, levels,
        /// or experience rates — that is a Kennedy decision, untouched here.
        /// </summary>
        public static class Pass2SkillHooks
        {
            public const string Forestry = "forestry";
            public const string FuelHandling = "fuel-handling";
            public const string Baking = "baking";
            public const string Hospitality = "hospitality";
            public const string TellerWork = "teller-work";
        }

        /// <summary>
        /// Registers the Pass 2 store, fuel, and life definitions. Safe to
        /// call once at boot; duplicate ids are rejected by TaskAuthority.
        /// Life definitions are AUTHORING ONLY in this pass: registering
        /// lf.eat-meal / lf.drink / lf.sleep does NOT replace the existing
        /// day-boundary meal consumption (DailyNeedsService) or the planner
        /// meal/sleep reservations. A production driver that issues life
        /// tasks, and the decision about which system serves meals, are
        /// Codex/driver work — documented, not faked, in the review doc.
        /// Idle is deliberately NOT registered here: idle is a schedule
        /// activity (PersonActivityKind.Idle), never a WorkTask.
        /// </summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string ignored;

            var order = new TaskDefinition(OrderFromSupplierId, "Order from supplier", 30);
            // Ordering/pricing has no skill id in the repo today: explicit
            // unskilled marker, not a silent gap.
            order.SetRequiredSkill(null, null);
            order.SetDefaultPriority(TaskPriority.Normal);
            order.DomainTags.Add("retail");
            order.DomainTags.Add("procurement");
            authority.RegisterDefinition(order, out ignored);

            var shelfToBack = new TaskDefinition(MoveShelfToBackId, "Move stock from shelf to back storage", 2);
            shelfToBack.SetRequiredSkill(SkillIds.Stocking, new[] { SkillIds.Stocking });
            shelfToBack.SetDefaultPriority(TaskPriority.Normal);
            shelfToBack.DomainTags.Add("retail");
            shelfToBack.DomainTags.Add("merchandising");
            authority.RegisterDefinition(shelfToBack, out ignored);

            RegisterWholesaleShelfMove(authority);

            var payment = new TaskDefinition(HandlePaymentId, "Handle payment at counter", 2);
            payment.SetRequiredSkill(SkillIds.CustomerTending, new[] { SkillIds.CustomerTending });
            payment.SetDefaultPriority(TaskPriority.High);
            payment.DomainTags.Add("retail");
            payment.DomainTags.Add("customer-service");
            authority.RegisterDefinition(payment, out ignored);

            var books = new TaskDefinition(BookkeepingId, "Keep business books", 15);
            books.SetRequiredSkill(null, null);
            books.SetDefaultPriority(TaskPriority.Low);
            books.DomainTags.Add("retail");
            books.DomainTags.Add("bookkeeping");
            authority.RegisterDefinition(books, out ignored);

            var fell = new TaskDefinition(FellTreeId, "Fell tree", 90);
            fell.SetRequiredSkill(Pass2SkillHooks.Forestry, new[] { Pass2SkillHooks.Forestry });
            fell.SetDefaultPriority(TaskPriority.Normal);
            fell.DomainTags.Add("fuel");
            fell.DomainTags.Add("forestry");
            authority.RegisterDefinition(fell, out ignored);

            var haul = new TaskDefinition(HaulLogId, "Haul log", 60);
            haul.SetRequiredSkill(Pass2SkillHooks.Forestry, new[] { Pass2SkillHooks.Forestry });
            haul.SetDefaultPriority(TaskPriority.Normal);
            haul.DomainTags.Add("fuel");
            haul.DomainTags.Add("forestry");
            authority.RegisterDefinition(haul, out ignored);

            var split = new TaskDefinition(SplitFuelId, "Split fuel wood", 30);
            split.SetRequiredSkill(Pass2SkillHooks.FuelHandling, new[] { Pass2SkillHooks.FuelHandling });
            split.SetDefaultPriority(TaskPriority.Normal);
            split.DomainTags.Add("fuel");
            authority.RegisterDefinition(split, out ignored);

            var stack = new TaskDefinition(StackFuelId, "Stack fuel wood", 15);
            stack.SetRequiredSkill(Pass2SkillHooks.FuelHandling, new[] { Pass2SkillHooks.FuelHandling });
            stack.SetDefaultPriority(TaskPriority.Normal);
            stack.DomainTags.Add("fuel");
            authority.RegisterDefinition(stack, out ignored);

            var dispense = new TaskDefinition(DispenseFuelId, "Dispense fuel from yard", 5);
            dispense.SetRequiredSkill(Pass2SkillHooks.FuelHandling, new[] { Pass2SkillHooks.FuelHandling });
            dispense.SetDefaultPriority(TaskPriority.Normal);
            dispense.DomainTags.Add("fuel");
            authority.RegisterDefinition(dispense, out ignored);

            var sellFuel = new TaskDefinition(SellFuelId, "Sell fuel to customer", 4);
            sellFuel.SetRequiredSkill(SkillIds.CustomerTending, new[] { SkillIds.CustomerTending });
            sellFuel.SetDefaultPriority(TaskPriority.High);
            sellFuel.DomainTags.Add("fuel");
            sellFuel.DomainTags.Add("customer-service");
            authority.RegisterDefinition(sellFuel, out ignored);

            // Life: durations follow EXISTING code (planner meals 30 min,
            // sleep 22:00-07:00 = 9 hours), NOT Kennedy's example guesses
            // (15-minute meals, 6-8-hour sleep), which remain open decisions.
            var eat = new TaskDefinition(EatMealId, "Eat a meal", 30);
            eat.SetRequiredSkill(null, null);
            eat.SetDefaultPriority(TaskPriority.High);
            eat.DomainTags.Add("life");
            authority.RegisterDefinition(eat, out ignored);

            var drink = new TaskDefinition(DrinkId, "Drink", 1);
            drink.SetRequiredSkill(null, null);
            drink.SetDefaultPriority(TaskPriority.Normal);
            drink.DomainTags.Add("life");
            authority.RegisterDefinition(drink, out ignored);

            var sleep = new TaskDefinition(SleepId, "Sleep", 540);
            sleep.SetRequiredSkill(null, null);
            sleep.SetDefaultPriority(TaskPriority.High);
            sleep.DomainTags.Add("life");
            authority.RegisterDefinition(sleep, out ignored);
        }

        /// <summary>Registers the existing wholesale back-stock shelf Task without activating any driver.</summary>
        public static void RegisterWholesaleShelfMove(TaskAuthority authority)
        {
            if (authority == null) throw new System.ArgumentNullException(nameof(authority));
            var backToShelf = new TaskDefinition(MoveBackToShelfId, "Move stock from back storage to shelf", 2);
            backToShelf.SetRequiredSkill(SkillIds.Stocking, new[] { SkillIds.Stocking });
            backToShelf.SetDefaultPriority(TaskPriority.Normal);
            backToShelf.DomainTags.Add("retail");
            backToShelf.DomainTags.Add("merchandising");
            authority.RegisterDefinition(backToShelf, out _);
        }

        /// <summary>
        /// The decided store-action table. "Instant" means a decision with
        /// no person-time: it is recorded on the order/price record, books
        /// no minutes, and must never appear in LaborDemandAssessor input.
        /// </summary>
        public static List<Pass2StoreActionDecision> StoreActionDecisions()
        {
            return new List<Pass2StoreActionDecision>
            {
                new Pass2StoreActionDecision
                {
                    ActionId = "gs.choose-supplier",
                    DisplayName = "Choose supplier",
                    Kind = Pass2ActionKind.InstantAction,
                    Reason = "A decision recorded on the order. The labour is the order task itself (gs.order-from-supplier); counting the choice again would double-count person-time.",
                },
                new Pass2StoreActionDecision
                {
                    ActionId = OrderFromSupplierId,
                    DisplayName = "Order from supplier",
                    Kind = Pass2ActionKind.Task,
                    Reason = "Real work: writing, sending, and confirming an order. Flows through TaskAuthority.",
                },
                new Pass2StoreActionDecision
                {
                    ActionId = "gs.set-price-margin",
                    DisplayName = "Set price / margin",
                    Kind = Pass2ActionKind.InstantAction,
                    Reason = "A pricing decision. Changing a price or margin takes no worker's time in itself; the labour is moving and re-ticketing stock, which is a stocking task when it happens.",
                },
                new Pass2StoreActionDecision
                {
                    ActionId = MoveShelfToBackId,
                    DisplayName = "Move stock from shelf to back storage",
                    Kind = Pass2ActionKind.Task,
                    Reason = "Physical handling work, per lot, through TaskAuthority.",
                },
                new Pass2StoreActionDecision
                {
                    ActionId = MoveBackToShelfId,
                    DisplayName = "Move stock from back storage to shelf",
                    Kind = Pass2ActionKind.Task,
                    Reason = "Physical handling work, per lot, through TaskAuthority.",
                },
                new Pass2StoreActionDecision
                {
                    ActionId = HandlePaymentId,
                    DisplayName = "Handle payment at counter",
                    Kind = Pass2ActionKind.Task,
                    Reason = "Counter work at the moment of sale. Money itself moves only through HouseholdLedger / FinancialObligationAuthority, never as a task side effect.",
                },
                new Pass2StoreActionDecision
                {
                    ActionId = BookkeepingId,
                    DisplayName = "Keep business books",
                    Kind = Pass2ActionKind.Task,
                    Reason = "Routine work, low priority, booked as a task so its cost is visible instead of free.",
                },
            };
        }
    }

    /// <summary>
    /// Pass 2: idle as a schedule activity. Idle is recorded as a
    /// PersonActivityKind.Idle reservation (see PersonScheduleTracker),
    /// instantly preemptible, budget-exempt, and excluded from hiring
    /// pressure. It is NEVER a WorkTask, and this helper exists so a
    /// driver cannot accidentally issue one: there is no idle definition
    /// id to issue, by design.
    /// </summary>
    public static class Pass2IdleActivity
    {
        /// <summary>True only for the schedule-activity idle kind; always false for task ids.</summary>
        public static bool IsIdleScheduleActivity(string activityKindName)
        {
            return string.Equals(activityKindName, "Idle", StringComparison.Ordinal);
        }

        /// <summary>
        /// Hiring-pressure inputs must exclude idle time. A caller that
        /// passes idle minutes here gets them back as zero: idle is
        /// standing by, not unmet work, and must never recommend hiring.
        /// </summary>
        public static int HiringPressureMinutesExcludingIdle(int totalUnmetMinutes, int idleMinutes)
        {
            return Math.Max(0, totalUnmetMinutes - Math.Max(0, idleMinutes));
        }
    }
}
