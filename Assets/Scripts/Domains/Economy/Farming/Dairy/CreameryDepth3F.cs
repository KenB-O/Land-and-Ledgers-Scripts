using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Dairy
{
    /// <summary>
    /// D3F: one creamery patron farm. Canon §9.6 authorizes creameries to aggregate
    /// milk "from surrounding farms"; every patron is therefore a REAL farm, and the
    /// creamery names it. Patrons are auto-registered on first delivery (the farm
    /// showed up with milk) or explicitly ahead of time through
    /// Creamery.RegisterPatron so the owner person can be named. Canon is silent on
    /// co-operative equity/patron shareholding, so none is invented (D3F audit).
    /// </summary>
    [Serializable]
    public sealed class CreameryPatron
    {
        public string FarmId = string.Empty;
        public string DisplayName = string.Empty;
        public EntityId OwnerPersonId = EntityId.Invalid; // EntityKind.Person — the farm's owner, when named
        public int FirstDeliveryDayIndex = -1;
        public int TotalAcceptedUnits;
        public int TotalRejectedUnits;
        public string Notes = string.Empty;

        public CreameryPatron() { }
    }

    /// <summary>
    /// D3F: one day's milk pool at the creamery door. AcceptDelivery credits accepted
    /// units here; ProcessDay consumes them. This is the upstream-provenance ledger
    /// for the vat: the creamery can only process milk it actually accepted — shortfalls
    /// (pool empty, or the caller asking for more than the pool holds) are refused
    /// loudly, never conjured. Pool lot ids keep the per-lot provenance for audit.
    /// </summary>
    [Serializable]
    public sealed class CreameryPoolDay
    {
        public int DayIndex = -1;
        public int AcceptedUnits;
        public int ConsumedUnits; // units already processed into cheese
        public List<EntityId> PoolLotIds = new List<EntityId>(); // accepted lot provenance

        public CreameryPoolDay() { }

        /// <summary>Units still sitting in the vat, unprocessed.</summary>
        public int RemainingUnits => Math.Max(0, AcceptedUnits - ConsumedUnits);
    }

    /// <summary>
    /// D3F: one executed creamery production day — the creamery's own auditable record
    /// of what it made from whose milk. The SWN-2 CheeseChain keeps the wheels; this
    /// record keeps the creamery's side: the day, the worker, the wheel ids, the milk
    /// lot ids that went into them, and the whey byproduct units available for
    /// downstream use (→ pig feed, Canon §9.7).
    /// </summary>
    [Serializable]
    public sealed class CreameryBatchRecord
    {
        public int DayIndex = -1;
        public EntityId WorkerId = EntityId.Invalid; // EntityKind.Person — the cheesemaker
        public List<EntityId> WheelLotIds = new List<EntityId>();
        public List<EntityId> InputMilkLotIds = new List<EntityId>();
        public int InputMilkUnits;
        public int WheyUnits; // byproduct units (Canon §9.7), available for pig-feed routing
        public string Notes = string.Empty;

        public CreameryBatchRecord() { }
    }

    /// <summary>
    /// D3F: creamery labor task definitions (TTS-2). Milking, churning, and cheese
    /// making already have task definitions (FVS-2 dairy chain, SWN-2 cheese chain);
    /// the creamery's own daily work — receiving deliveries at the door and running
    /// butterfat tests — did not. Registration is additive: the game layer still
    /// assigns the labor through TaskAuthority (caller-assigns, like the width code).
    /// Minutes are calibration (Canon Part XV), not canon.
    /// </summary>
    public static class CreameryLabor
    {
        public const string ReceiveMilkDeliveryTaskId = "receive-milk-delivery";
        public const string TestButterfatTaskId = "test-butterfat";

        /// <summary>Calibration: minutes to receive, weigh, and record one delivery.</summary>
        public const int ReceiveDeliveryMinutes = 10;
        /// <summary>Calibration: minutes to run one butterfat test.</summary>
        public const int ButterfatTestMinutes = 15;

        /// <summary>Registers creamery intake + testing task definitions (TTS-2).</summary>
        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;

            var receive = new TaskDefinition(ReceiveMilkDeliveryTaskId, "Receive milk delivery", ReceiveDeliveryMinutes);
            receive.SetRequiredSkill(DairyChain.DairyProcessingSkillId, new[] { "intake" });
            authority.RegisterDefinition(receive, out _);

            var test = new TaskDefinition(TestButterfatTaskId, "Test butterfat", ButterfatTestMinutes);
            test.SetRequiredSkill(DairyChain.DairyProcessingSkillId, new[] { "testing" });
            authority.RegisterDefinition(test, out _);
        }
    }
}
