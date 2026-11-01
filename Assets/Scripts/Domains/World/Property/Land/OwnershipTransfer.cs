using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III foundation: single ownership-transfer function.
    /// Canon: Rev XXIII §1.1 "One Authority Per Consequential Fact".
    ///
    /// CONFIRMED DUPLICATE TRUTH (audit 2026-10-11): four records currently answer
    /// "who owns this plot":
    ///   1. TownPlot.playerOwned (physical-world flag)
    ///   2. PlacedBuilding.playerOwned (second flag; validator only warns on divergence)
    ///   3. AcquisitionMarketManager.ownedPlotIds (three construction flows write ONLY this)
    ///   4. TitleAuthority chain (holder name "Player" via PlayerTitleBridge)
    /// Reconciled today only by the load-bearing OR-merge IsPlayerOwnedPlot.
    ///
    /// Migration path (do not break the OR-merge before the chain covers every flow):
    ///   - All NEW ownership changes route through IOwnershipTransferService.TransferTitle,
    ///     implemented on TitleAuthority's append-only chain.
    ///   - TownPlot.playerOwned / PlacedBuilding.playerOwned become DERIVED read
    ///     projections (recomputed from the chain, never independently written).
    ///   - ownedPlotIds becomes a chain query ("Player"-held parcels).
    ///   - IsPlayerOwnedPlot delegates to the chain for parcel-linked plots,
    ///     keeping the legacy OR fallback until every writer is migrated.
    /// Do NOT create a third registry. See Docs/Pass3_FoundationContracts.md.
    /// </summary>
    public interface IOwnershipTransferService
    {
        /// <summary>Appends one transfer link to the title chain. Returns the transfer record id.</summary>
        string TransferTitle(string parcelId, string fromHolder, string toHolder, string instrumentId, int day);
        string CurrentHolder(string parcelId);
        IReadOnlyList<string> ParcelsHeldBy(string holderName);
    }
}
