using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Pass 2 (task system): demand-driven chains as pure sequences.
    /// These are NOT drivers and NOT production generators: they state,
    /// in order, which tasks a demand event must produce, and which
    /// trigger starts each step, so a future driver can wire them without
    /// inventing demand. No task is created here for no reason: every
    /// chain starts with a real trigger (a customer arriving, a room
    /// becoming dirty, a window opening with a customer present, a
    /// day-boundary book-closing), and routine steps name their trigger
    /// state explicitly. Steps whose trigger state does not exist yet
    /// are listed in RoutineTriggerGaps, not silently assumed.
    ///
    /// Kennedy's saloon example durations are DIRECTION ONLY and are
    /// NOT encoded anywhere in this file: order ~15 sec (regular 5 sec,
    /// first-timer 30 sec), serve at bar ~30 sec, serve at table ~1 min.
    /// Until the seconds decision is made, those steps cannot be
    /// authored as sub-minute tasks, and this file registers no
    /// sub-minute definition for them. See Pass2SecondsBlocklist.
    /// </summary>
    public static class Pass2DemandChains
    {
        /// <summary>One step in a demand chain: the task id to create, and the trigger that must be true first.</summary>
        public sealed class DemandStep
        {
            public string TaskDefinitionId = string.Empty;
            public string Trigger = string.Empty;

            public DemandStep() { }
            public DemandStep(string taskId, string trigger) { TaskDefinitionId = taskId; Trigger = trigger; }
        }

        /// <summary>
        /// Hotel, demand-first: a freshly built, furnished hotel has almost
        /// nothing to do on opening day. Staff stand by (Idle schedule
        /// activity, not a task) until a customer arrives. Then: deal with
        /// the customer, escort them to their room, next-morning checkout
        /// after one night, then refurbish the room. No step runs without
        /// its trigger.
        /// </summary>
        public static List<DemandStep> HotelGuestChain()
        {
            return new List<DemandStep>
            {
                new DemandStep("ht.deal-with-customer", "A customer arrives at the hotel (arrival event, not a clock)."),
                new DemandStep("ht.escort-to-room", "Check-in completed for that guest."),
                new DemandStep("ht.checkout", "Next morning, after the guest's night is complete."),
                new DemandStep("ht.refurbish-room", "Checkout completed; the room is now dirty / unmade (trigger state)."),
            };
        }

        /// <summary>
        /// Saloon: opening cleaning is routine (trigger: the saloon opens
        /// and its floor/bar area is dirty from the previous day), then
        /// everything else is customer-driven: order, pour, serve. Bar
        /// and table service are DIFFERENT steps (different walks, different
        /// durations when seconds are decided), not one generic serve.
        /// Glass/container granularity is explicitly NOT modelled here.
        /// </summary>
        public static List<DemandStep> SaloonGuestChain()
        {
            return new List<DemandStep>
            {
                new DemandStep("sa.clean-opening", "Saloon opens and the area is dirty (routine trigger state)."),
                new DemandStep("sa.take-order", "A customer is seated / at the bar and ready to order."),
                new DemandStep("sa.pour-drink", "An order exists and has been taken."),
                new DemandStep("sa.serve-at-bar", "A poured drink is for a bar customer (no walk)."),
                new DemandStep("sa.serve-at-table", "A poured drink is for a table customer (walk to the table)."),
            };
        }

        /// <summary>
        /// Routine-trigger gaps found in the Pass 2 audit: trigger states
        /// that must exist before the matching routine task may be created.
        /// A driver must NOT create these tasks until the state exists;
        /// until then, the task stays unwired and this list stays honest.
        /// </summary>
        public static List<string> RoutineTriggerGaps()
        {
            return new List<string>
            {
                "Store dirtiness state: no store dirtiness value exists to trigger store cleaning as a routine task.",
                "Saloon dirtiness state: no saloon dirtiness value exists to trigger opening cleaning beyond the opening-day assumption.",
                "Room dirtiness beyond the linen queue: hotel refurbish needs a room-dirty state, not just a linen loop.",
                "Bank window-open-with-customer state: serving requires an open window AND a present customer, not either alone.",
            };
        }

        /// <summary>
        /// Seconds-dependent work, BLOCKED on Kennedy's open decision
        /// (small additive option vs full seconds migration). Nothing in
        /// this list is implemented in Pass 2, and no workaround pretends
        /// otherwise: booking these at 1 minute each would overcharge
        /// 12 five-second orders as 12 minutes of labour.
        /// </summary>
        public static List<string> Pass2SecondsBlocklist()
        {
            return new List<string>
            {
                "Saloon order-taking at example durations (regular ~5 sec, first-timer ~30 sec, typical ~15 sec).",
                "Saloon serve at bar (~30 sec) and serve at table (~1 min) as distinct sub-minute / minute tasks.",
                "Any task whose real duration is under one minute, including drink service variants.",
                "A seconds-native work-time budget (36,000 sec/day) and seconds-native planner/drivers/save migration (full option only).",
            };
        }
    }

}
