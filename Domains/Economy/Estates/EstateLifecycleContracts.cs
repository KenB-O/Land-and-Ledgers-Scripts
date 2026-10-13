using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Estates
{
    /// <summary>
    /// Pass VII Workstream C: estate lifecycle stages. Append-only.
    /// </summary>
    public enum EstateLifecycleStage
    {
        Opened = 0,
        Inventoried = 1,
        ClaimsAdjudicated = 2,
        Distributed = 3,
        Closed = 4,
    }

    /// <summary>
    /// Pass VII Workstream C: the runtime estate administration driver.
    /// EstateService remains the estate RECORD authority (OpenEstate,
    /// RegisterDecedentParcel). This driver executes the lifecycle that was
    /// source-only: inventory, claims, distribution, closure. Each stage is
    /// idempotent: re-running a completed stage is a no-op, never a duplicate
    /// distribution. Title changes flow only through the TitleAuthority.
    /// </summary>
    public interface IEstateLifecycleDriver
    {
        string InventoryEstate(string estateId, List<string> diagnostics);
        string AdjudicateClaims(string estateId, List<string> diagnostics);
        string DistributeEstate(string estateId, List<string> diagnostics);
        string CloseEstate(string estateId, List<string> diagnostics);
        EstateLifecycleStage StageOf(string estateId);
    }
}
