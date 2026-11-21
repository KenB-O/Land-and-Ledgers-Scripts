// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 9f (M-R10, expanded campaign 2026-11-20): the furnishing.install
// outcome wiring Agent M confirmed missing (M-17) — the definitions were
// registered, zero production code minted an install/move/remove task, and
// no outcome resolver existed, so paid furniture could never install and
// had exactly one terminal state in production: custody.
//
// This file is the furnishing-specific outcome resolver the definitions
// file says did not exist, plus the task minting from the custody leg.
// A paid and delivered item stays in household custody until a
// legitimate furnishing.install task physically installs it at a valid
// authored placement — no synchronous installation shortcut anywhere in
// the production path:
//
// - Mint: one furnishing.install task per custody asset (a received
//   store-purchase asset, or a workshop-made custody piece), minted only
//   when a free authored placement exists at mint time. The task's
//   CustomerRef carries the order, asset, and selected authored-placement identity.
//   ("furnishing-install:<purchaseId>:<assetId>" remains readable for old
//   saves; new refs append the placement identity), so the order, asset,
//   and slot linkage survive TaskAuthority Save/Continue. A legacy task
//   without a recorded slot conservatively holds new slot claims until it
//   terminates. No live task is ever minted twice for one asset.
// - Work: when the live caller supplies a placement-route resolver, an
//   available household member must physically arrive at the reserved
//   furniture approach before the task accrues 30 minutes. Assignment,
//   start and work happen through the shared TaskAuthority, so budgets,
//   interruption and lifecycle rules all apply. Legacy callers retain the
//   conservative home-arrival gate until they adopt the placement route.
// - Resolve: when the task completes, the resolver installs the asset
//   through FurnitureInstallBinding at the scored placement (I-R1's
//   placement scoring decides WHERE; this decides THAT it happens), and
//   advances the purchase order's line Received -> Installed ->
//   Operational. When no placement is free at resolve time, or the
//   install is refused for any other reason, the asset stays in
//   household custody with the refusal recorded as the order's
//   InstallationBlockedReason — never both installed and blocked, never
//   neither, never silently lost.
//
// Placement appropriateness (walls, counter ends, door proximity) is
// Lane 10b's work: the driver takes an optional
// FurniturePlacementContext (built by SimulationDrivers from the home
// building authority) and passes it through to
// FurnitureInstallBinding, so the install resolves at the scored
// placement. Null context is first-match, exactly the Lane 9f
// behaviour — the context is supplied by the live driver, never
// invented here.
// Unity-free (plain state only), so it is harness-compiled and
// harness-tested; the daily driver (SimulationDrivers) binds the scene
// seams (placements, bound check, arrival fact) and the household roster.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>
    /// M-R10: mints furnishing.install tasks from the custody leg and
    /// resolves them into real installs (or named blockers). One driver
    /// serves one household for one day; all state lives on the task
    /// authority and the purchase order, never here.
    /// </summary>
    public static class FurnishingInstallTaskDriver
    {
        /// <summary>Routes an assigned worker to the physical installation approach for the task's reserved placement.</summary>
        public delegate DomesticActivityMovementResult InstallationRouteResolver(
            int personId, int buildingId, WorkTask task, out string reason);

        /// <summary>Releases temporary routing data after an install task becomes terminal.</summary>
        public delegate void InstallationRouteRelease(WorkTask task);

        /// <summary>The CustomerRef prefix for install tasks. The ref carries the order + asset identity.</summary>
        public const string InstallTaskRefPrefix = "furnishing-install:";

        /// <summary>One day's install driving, for the caller's diagnostics.</summary>
        public sealed class DriveDayResult
        {
            public int MintedTasks;
            public int InstalledAssets;
            public int BlockedAssets;
            public int CompletedTasks;
        }

        /// <summary>Builds the CustomerRef for one asset's install task. Empty purchase id = a workshop custody piece with no order.</summary>
        public static string BuildInstallTaskRef(string purchaseId, string assetId, string placementId = null)
        {
            string reference = InstallTaskRefPrefix + (purchaseId ?? string.Empty) + ":" + (assetId ?? string.Empty);
            return string.IsNullOrWhiteSpace(placementId) ? reference : reference + ":placement=" + placementId.Trim();
        }

        /// <summary>Parses an install-task CustomerRef back into its purchase id (possibly empty) and asset id.</summary>
        public static bool TryParseInstallTaskRef(string customerRef, out string purchaseId, out string assetId)
        {
            return TryParseInstallTaskRef(customerRef, out purchaseId, out assetId, out _);
        }

        /// <summary>Parses the optional durable authored-placement reservation from a task ref.</summary>
        public static bool TryParseInstallTaskRef(string customerRef, out string purchaseId, out string assetId, out string placementId)
        {
            purchaseId = string.Empty;
            assetId = string.Empty;
            placementId = string.Empty;
            if (string.IsNullOrWhiteSpace(customerRef)
                || !customerRef.StartsWith(InstallTaskRefPrefix, StringComparison.Ordinal)) return false;
            string rest = customerRef.Substring(InstallTaskRefPrefix.Length);
            int placementMarker = rest.LastIndexOf(":placement=", StringComparison.Ordinal);
            if (placementMarker >= 0)
            {
                placementId = rest.Substring(placementMarker + ":placement=".Length);
                rest = rest.Substring(0, placementMarker);
                if (string.IsNullOrWhiteSpace(placementId)) return false;
            }
            int split = rest.LastIndexOf(':');
            if (split < 0) return false;
            purchaseId = rest.Substring(0, split);
            assetId = rest.Substring(split + 1);
            if (string.IsNullOrWhiteSpace(assetId)) return false;
            return !string.IsNullOrWhiteSpace(assetId);
        }

        /// <summary>True when the task is a furnishing.install task minted by this driver.</summary>
        public static bool IsInstallTask(WorkTask task)
        {
            return task != null
                && string.Equals(task.DefinitionId, FurnishingTaskDefinitions.InstallId, StringComparison.Ordinal);
        }

        /// <summary>
        /// The live install task for one asset, if any — open (Queued,
        /// Assigned, InProgress or any other open state) under the
        /// household owner, carrying this asset's ref. Null when no task
        /// is in flight, so the caller can mint without double-minting.
        /// </summary>
        public static WorkTask FindLiveInstallTask(TaskAuthority tasks, EntityId householdOwner, string assetId)
        {
            if (tasks == null || string.IsNullOrWhiteSpace(assetId)) return null;
            foreach (WorkTask task in tasks.GetOpenTasks(householdOwner))
            {
                if (!IsInstallTask(task)) continue;
                if (!TryParseInstallTaskRef(task.CustomerRef, out _, out string refAssetId)) continue;
                if (string.Equals(refAssetId, assetId.Trim(), StringComparison.Ordinal)) return task;
            }
            return null;
        }

        private static bool TryFindUnreservedPlacement(
            TaskAuthority tasks, EntityId householdOwner, string kind,
            BuildingFootprintAuthority building, IEnumerable<BuildingEquipmentPlacement> placements,
            Func<string, bool> placementIsBound, FurniturePlacementContext context,
            out BuildingEquipmentPlacement selected, out string refusal)
        {
            var reserved = new HashSet<string>(StringComparer.Ordinal);
            bool hasPreReservationTask = false;
            if (tasks != null)
            {
                foreach (WorkTask open in tasks.GetOpenTasks(householdOwner))
                {
                    if (!IsInstallTask(open)) continue;
                    if (!TryParseInstallTaskRef(open.CustomerRef, out _, out _, out string placementId)) continue;
                    if (string.IsNullOrWhiteSpace(placementId)) hasPreReservationTask = true;
                    else reserved.Add(placementId);
                }
            }

            bool IsUnavailable(string id) => hasPreReservationTask || reserved.Contains(id ?? string.Empty)
                || placementIsBound != null && placementIsBound(id ?? string.Empty);
            if (FurnitureInstallBinding.TryFindFreePlacement(kind, building, placements,
                    IsUnavailable, context, out selected, out refusal)) return true;
            if (hasPreReservationTask)
                refusal = "FurnishingInstallTaskDriver: a saved pre-reservation installation task still owns placement choice; new installation tasks wait until it reaches a terminal state.";
            if (reserved.Count > 0 && string.IsNullOrWhiteSpace(refusal))
                refusal = "FurnishingInstallTaskDriver: every matching authored placement is already bound to an open installation task.";
            else if (reserved.Count > 0 && refusal.IndexOf("no free placement", StringComparison.OrdinalIgnoreCase) >= 0)
                refusal = refusal + " Open installation tasks retain their selected placements until completion or terminal failure.";
            return false;
        }

        /// <summary>
        /// Mints one Queued furnishing.install task for one custody asset.
        /// Refuses (never throws, never mints twice) when the definition
        /// is not registered, the asset is missing, already installed,
        /// unusable or reserved, or a live install task already exists
        /// for it. The task is left Queued: assignment happens in
        /// DriveDay only once a household member has arrived at home, so
        /// no work-time budget is ever booked for a worker who is not
        /// there. The task's location is the household's home building,
        /// carried by the order (purchase id + asset id in the ref) and
        /// the driver's building argument — WorkTask.LocationId is never
        /// set anywhere except Copy, so no location string is stored on
        /// the task itself.
        /// </summary>
        public static string TryMintInstallTask(
            TaskAuthority tasks,
            EntityId householdOwner,
            string purchaseId,
            EquipmentAsset asset,
            int dayIndex,
            out WorkTask task,
            string placementId = null)
        {
            task = null;
            if (tasks == null)
                return "FurnishingInstallTaskDriver: a TaskAuthority is required — install tasks are real WorkTasks, never side effects.";
            if (tasks.GetDefinition(FurnishingTaskDefinitions.InstallId) == null)
                return "FurnishingInstallTaskDriver: 'furnishing.install' is not registered on the TaskAuthority — register FurnishingTaskDefinitions before minting install work.";
            if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId))
                return "FurnishingInstallTaskDriver: an install task needs a real custody asset — refusing a task for nothing.";
            if (FindLiveInstallTask(tasks, householdOwner, asset.AssetId) != null)
                return $"FurnishingInstallTaskDriver: asset '{asset.AssetId}' already has a live install task — refusing a duplicate.";
            task = tasks.CreateTask(FurnishingTaskDefinitions.InstallId, householdOwner, dayIndex,
                BuildInstallTaskRef(purchaseId, asset.AssetId, placementId));
            return null;
        }

        /// <summary>
        /// One household's install day: mint tasks for custody assets that
        /// need one (received purchase assets first, then workshop-made
        /// custody pieces), work the live install tasks whose worker has
        /// arrived at home, and resolve every task that completes. An
        /// asset with no free authored placement is never minted: it stays
        /// in custody with the placement refusal recorded as the order's
        /// blocker (or a diagnostic line for a workshop piece). An asset
        /// whose worker has not arrived waits Queued — no minutes accrue
        /// from nowhere, and no budget is booked for an absent worker.
        /// Returns the day's counts; every refusal and install is also
        /// appended to diag. Lane 10b (I-R1): an optional placement
        /// context scores the placement the install resolves to (and
        /// the mint-time free-placement check); null is first-match,
        /// exactly today's behaviour.
        /// </summary>
        public static DriveDayResult DriveDay(
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            EntityId householdOwner,
            IReadOnlyList<EntityId> householdMembers,
            int householdId,
            int buildingInstanceId,
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            IEnumerable<BuildingEquipmentPlacement> scenePlacements,
            Func<string, bool> placementIsBound,
            IReadOnlyList<FurniturePurchaseOrder> orders,
            Func<int, int, bool> hasArrivedAtHome,
            int dayIndex,
            List<string> diag,
            FurniturePlacementContext placementContext = null,
            InstallationRouteResolver routeToInstallationTarget = null,
            InstallationRouteRelease releaseInstallationRoute = null)
        {
            diag = diag ?? new List<string>();
            var result = new DriveDayResult();
            if (tasks == null || register == null || building == null)
            {
                diag.Add("FurnishingInstallTaskDriver: install driving needs the TaskAuthority, the household furnishing register, and the home building — skipping the household's install day.");
                return result;
            }
            if (budgets != null) budgets.EnsureDay(dayIndex);

            // Candidate assets: received purchase assets (order leg) plus
            // workshop-made custody pieces (custody leg). A store-bought
            // asset is claimed by its order; the custody leg never
            // re-claims it, so one asset is never minted twice.
            var orderAssetIds = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<InstallCandidate>();
            if (orders != null)
            {
                foreach (FurniturePurchaseOrder order in orders)
                {
                    if (order == null || order.HouseholdId != householdId
                        || order.IsOperational || order.CancellationRequested
                        || !string.IsNullOrWhiteSpace(order.TerminalReason)) continue;
                    if (order.Lines == null) continue;
                    foreach (FurniturePurchaseLine line in order.Lines)
                    {
                        if (line == null || line.Stage != EquipmentPurchaseStage.Received) continue;
                        if (line.AssetIds == null) continue;
                        foreach (string assetId in line.AssetIds)
                        {
                            if (string.IsNullOrWhiteSpace(assetId)) continue;
                            orderAssetIds.Add(assetId);
                            EquipmentAsset asset = register.FindAsset(assetId);
                            if (asset == null) continue;
                            if (register.LiveBindingFor(assetId) != null) continue;
                            if (!asset.IsUsable || asset.IsReserved) continue;
                            candidates.Add(new InstallCandidate { Asset = asset, Order = order });
                        }
                    }
                }
            }
            if (register.Assets != null)
            {
                foreach (EquipmentAsset asset in register.Assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) continue;
                    if (orderAssetIds.Contains(asset.AssetId)) continue;
                    if (register.LiveBindingFor(asset.AssetId) != null) continue;
                    if (string.IsNullOrWhiteSpace(asset.MadeByBusinessId)) continue;
                    if (!asset.IsUsable || asset.IsReserved) continue;
                    candidates.Add(new InstallCandidate { Asset = asset, Order = null });
                }
            }

            foreach (InstallCandidate candidate in candidates)
            {
                EquipmentAsset asset = candidate.Asset;
                if (FindLiveInstallTask(tasks, householdOwner, asset.AssetId) != null) continue;
                if (!TryFindUnreservedPlacement(tasks, householdOwner, asset.Kind, building,
                        scenePlacements, placementIsBound, placementContext,
                        out BuildingEquipmentPlacement selectedPlacement, out string placementRefusal))
                {
                    result.BlockedAssets++;
                    bool newOrderBlocker = candidate.Order != null
                        && !string.Equals(candidate.Order.InstallationBlockedReason, placementRefusal, StringComparison.Ordinal);
                    if (candidate.Order != null) candidate.Order.InstallationBlockedReason = placementRefusal;
                    // Keep checking for a changed authored layout so the
                    // order can recover, but emit one actionable incident
                    // for an unchanged blocker instead of repeating it each
                    // day while the paid item remains safely in custody.
                    if (candidate.Order == null || newOrderBlocker)
                        diag.Add($"FurnishingInstallTaskDriver: household {householdId} asset '{asset.AssetId}' ({asset.Kind}) has no free authored placement — it stays in custody: {placementRefusal}");
                    continue;
                }
                if (candidate.Order != null)
                    candidate.Order.InstallationBlockedReason = string.Empty;
                string mintRefusal = TryMintInstallTask(tasks, householdOwner,
                    candidate.Order != null ? candidate.Order.PurchaseId : string.Empty,
                    asset, dayIndex, out WorkTask minted, selectedPlacement.PlacementId);
                if (mintRefusal != null)
                {
                    result.BlockedAssets++;
                    if (candidate.Order != null)
                        candidate.Order.InstallationBlockedReason = mintRefusal;
                    diag.Add($"FurnishingInstallTaskDriver: household {householdId} asset '{asset.AssetId}' install task refused — {mintRefusal}");
                    continue;
                }
                result.MintedTasks++;
                diag.Add($"FurnishingInstallTaskDriver: household {householdId} minted furnishing.install task '{minted.TaskId}' for asset '{asset.AssetId}' ({asset.Kind})" +
                    (candidate.Order != null ? $" (order '{candidate.Order.PurchaseId}')" : " (workshop custody piece)") + ".");
            }

            // Work the live install tasks whose worker has arrived at home.
            // A task whose asset vanished is failed honestly (its custody
            // is gone, so there is nothing to install); a task whose asset
            // is already installed resolves the order without a second
            // install. Both keep the order's state truthful.
            foreach (WorkTask task in new List<WorkTask>(tasks.GetOpenTasks(householdOwner)))
            {
                if (!IsInstallTask(task)) continue;
                if (!TryParseInstallTaskRef(task.CustomerRef, out string purchaseId, out string assetId, out string reservedPlacementId)) continue;
                FurniturePurchaseOrder order = FindOrder(orders, householdId, purchaseId);
                EquipmentAsset asset = register.FindAsset(assetId);
                if (asset == null)
                {
                    tasks.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, dayIndex, budgets);
                    result.CompletedTasks++;
                    releaseInstallationRoute?.Invoke(task);
                    if (order != null)
                        order.InstallationBlockedReason = $"FurnishingInstallTaskDriver: asset '{assetId}' left the household register before its install task completed — the install cannot proceed.";
                    diag.Add($"FurnishingInstallTaskDriver: household {householdId} install task '{task.TaskId}' failed — asset '{assetId}' is no longer in the household register.");
                    continue;
                }
                if (register.LiveBindingFor(assetId) != null)
                {
                    string alreadyRefusal = ResolveCompletedInstallTask(task, order, register, building,
                        buildingInstanceId, scenePlacements, placementIsBound, diag, placementContext, reservedPlacementId);
                    if (alreadyRefusal == null)
                    {
                        tasks.CompleteTask(task.TaskId, TaskOutcome.Success, dayIndex, budgets);
                        result.CompletedTasks++;
                        releaseInstallationRoute?.Invoke(task);
                    }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(reservedPlacementId))
                {
                    // A pre-change task has no durable placement reservation.
                    // End it without work so the next daily pass can mint a
                    // replacement that carries the selected slot through save.
                    if (tasks.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, dayIndex, budgets))
                    {
                        result.CompletedTasks++;
                        releaseInstallationRoute?.Invoke(task);
                        diag.Add($"FurnishingInstallTaskDriver: legacy install task '{task.TaskId}' for asset '{assetId}' ended without labor because it has no saved placement identity; custody is preserved and a placement-bound task may be minted next day.");
                    }
                    continue;
                }
                EntityId worker;
                string workerRefusal;
                if (routeToInstallationTarget == null)
                {
                    if (!TryPickArrivedWorker(tasks, householdMembers, hasArrivedAtHome, buildingInstanceId,
                            task, out worker, out workerRefusal))
                    {
                        if (!string.IsNullOrWhiteSpace(workerRefusal))
                            diag.Add($"FurnishingInstallTaskDriver: household {householdId} install task '{task.TaskId}' waits — {workerRefusal}");
                        continue;
                    }
                }
                else if (!TryPickAvailableWorker(tasks, householdMembers, task, out worker, out workerRefusal))
                {
                    if (!string.IsNullOrWhiteSpace(workerRefusal))
                        diag.Add($"FurnishingInstallTaskDriver: household {householdId} install task '{task.TaskId}' waits — {workerRefusal}");
                    continue;
                }
                if (task.Status == TaskStatus.Queued
                    && !tasks.AssignTask(task.TaskId, worker, budgets, out string assignRefusal))
                {
                    diag.Add($"FurnishingInstallTaskDriver: household {householdId} install task '{task.TaskId}' could not be assigned to P{worker.Id} — {assignRefusal}");
                    continue;
                }
                if (routeToInstallationTarget != null)
                {
                    DomesticActivityMovementResult movement = routeToInstallationTarget(
                        worker.Id, buildingInstanceId, task, out string movementReason);
                    if (movement != DomesticActivityMovementResult.Arrived)
                    {
                        if (movement == DomesticActivityMovementResult.Unsupported)
                        {
                            string priorBlocker = order != null ? order.InstallationBlockedReason : string.Empty;
                            string interruptReason = string.Empty;
                            bool releasedWorker = task.Status != TaskStatus.Queued
                                && tasks.InterruptTask(task.TaskId, budgets, out interruptReason);
                            if (order != null && !string.IsNullOrWhiteSpace(movementReason))
                                order.InstallationBlockedReason = movementReason;
                            bool changedBlocker = !string.Equals(priorBlocker, movementReason, StringComparison.Ordinal);
                            if (changedBlocker)
                                diag.Add($"FurnishingInstallTaskDriver: household {householdId} install task '{task.TaskId}' route refused" +
                                    (releasedWorker ? "; worker reservation released" : string.Empty) +
                                    $"; the item and placement obligation remain open: {movementReason}" +
                                    (string.IsNullOrWhiteSpace(interruptReason) ? string.Empty : $" ({interruptReason})"));
                        }
                        continue;
                    }
                    if (order != null) order.InstallationBlockedReason = string.Empty;
                }
                if (task.Status == TaskStatus.Assigned
                    && !tasks.StartTask(task.TaskId, dayIndex, out string startRefusal))
                {
                    diag.Add($"FurnishingInstallTaskDriver: household {householdId} install task '{task.TaskId}' could not start — {startRefusal}");
                    continue;
                }
                if (task.Status != TaskStatus.InProgress) continue;
                if (!tasks.RecordWork(task.TaskId, task.MinutesRemaining, budgets, dayIndex, out string workRefusal))
                {
                    diag.Add($"FurnishingInstallTaskDriver: household {householdId} install task '{task.TaskId}' work refused — {workRefusal}");
                    continue;
                }
                if (task.Status != TaskStatus.Complete) continue;
                result.CompletedTasks++;
                string resolveRefusal = ResolveCompletedInstallTask(task, order, register, building,
                    buildingInstanceId, scenePlacements, placementIsBound, diag, placementContext, reservedPlacementId);
                releaseInstallationRoute?.Invoke(task);
                if (resolveRefusal == null) result.InstalledAssets++;
                else result.BlockedAssets++;
            }
            return result;
        }

        /// <summary>
        /// The furnishing-specific outcome resolver: a completed
        /// furnishing.install task installs its asset at a valid authored
        /// placement, or refuses with the placement reason and leaves the
        /// asset in custody. Returns null on success (installed, or
        /// already installed — the order is advanced either way), the
        /// refusal otherwise. Never installs synchronously outside a
        /// completed task, never marks an order installed that was not.
        /// Lane 10b (I-R1): an optional placement context scores the
        /// placement; null is first-match.
        /// </summary>
        public static string ResolveCompletedInstallTask(
            WorkTask task,
            FurniturePurchaseOrder order,
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            int buildingInstanceId,
            IEnumerable<BuildingEquipmentPlacement> scenePlacements,
            Func<string, bool> placementIsBound,
            List<string> diag,
            FurniturePlacementContext placementContext = null,
            string reservedPlacementId = null)
        {
            diag = diag ?? new List<string>();
            if (task == null || register == null || building == null)
                return "FurnishingInstallTaskDriver: resolving an install needs the completed task, the household register, and the home building.";
            if (!TryParseInstallTaskRef(task.CustomerRef, out string purchaseId, out string assetId, out string taskPlacementId))
                return $"FurnishingInstallTaskDriver: task '{task.TaskId}' carries no install ref — it is not an install outcome.";
            if (!string.IsNullOrWhiteSpace(taskPlacementId)) reservedPlacementId = taskPlacementId;
            EquipmentAsset asset = register.FindAsset(assetId);
            if (asset == null)
            {
                if (order != null)
                    order.InstallationBlockedReason = $"FurnishingInstallTaskDriver: asset '{assetId}' is no longer in the household register — the install cannot proceed.";
                return $"FurnishingInstallTaskDriver: asset '{assetId}' is no longer in the household register.";
            }
            if (register.LiveBindingFor(assetId) != null)
            {
                if (order != null) AdvanceOrderLineForAsset(order, assetId, register);
                diag.Add($"FurnishingInstallTaskDriver: asset '{assetId}' was already installed — order " +
                    (order != null ? $"'{order.PurchaseId}' advanced without a second install." : "has no order; nothing further to advance."));
                return null;
            }
            FurnishingAcquisitionSource source;
            string sourceId;
            if (order == null)
            {
                source = FurnishingAcquisitionSource.Manufacture;
                sourceId = asset.MadeByBusinessId ?? string.Empty;
            }
            else if (order.Kind == FurniturePurchaseKind.Secondhand)
            {
                source = FurnishingAcquisitionSource.Secondhand;
                sourceId = purchaseId;
            }
            else
            {
                source = FurnishingAcquisitionSource.Purchase;
                sourceId = purchaseId;
            }
            IEnumerable<BuildingEquipmentPlacement> eligiblePlacements = scenePlacements;
            if (!string.IsNullOrWhiteSpace(reservedPlacementId))
            {
                BuildingEquipmentPlacement reservedPlacement = null;
                foreach (BuildingEquipmentPlacement candidate in scenePlacements ?? Array.Empty<BuildingEquipmentPlacement>())
                    if (candidate != null && string.Equals(candidate.PlacementId, reservedPlacementId, StringComparison.Ordinal))
                    { reservedPlacement = candidate; break; }
                if (reservedPlacement == null)
                {
                    string missing = $"FurnishingInstallTaskDriver: task '{task.TaskId}' reserved authored placement '{reservedPlacementId}', which is no longer present; the item stays in custody.";
                    if (order != null) order.InstallationBlockedReason = missing;
                    return missing;
                }
                eligiblePlacements = new[] { reservedPlacement };
            }
            string refusal = FurnitureInstallBinding.TryInstallHouseholdAsset(register, asset, building,
                buildingInstanceId, eligiblePlacements, placementIsBound, source, sourceId,
                placementContext, out FurnishingPlacementBinding binding);
            if (refusal != null)
            {
                if (order != null) order.InstallationBlockedReason = refusal;
                diag.Add($"FurnishingInstallTaskDriver: asset '{assetId}' ({asset.Kind}) install refused — {refusal}; it stays in household custody.");
                return refusal;
            }
            if (order != null)
            {
                AdvanceOrderLineForAsset(order, assetId, register);
                if (order.IsOperational) order.InstallationBlockedReason = string.Empty;
                else RefreshInstallationBlocker(order, register, building, scenePlacements, placementIsBound, placementContext);
            }
            diag.Add($"FurnishingInstallTaskDriver: asset '{assetId}' ({asset.Kind}) installed in building {buildingInstanceId} " +
                $"(placement {binding?.PlacementId}, capability {binding?.CapabilityKind}, source {source})" +
                (order != null ? $" — order '{order.PurchaseId}' is {(order.IsOperational ? "operational" : "still receiving installs")}." : "."));
            return null;
        }

        /// <summary>
        /// Advances the order line holding one asset when every asset on
        /// that line is installed (Received -> Installed), then the whole
        /// order when every line is installed (Installed -> Operational).
        /// Mirrors FurniturePurchaseService's forward-only stage rule —
        /// the shared contract is the EquipmentPurchaseStage enum, not
        /// the line type. Never advances a line with an uninstalled asset.
        /// </summary>
        public static void AdvanceOrderLineForAsset(FurniturePurchaseOrder order, string assetId,
            HouseholdFurnishingRegister register)
        {
            if (order == null || order.Lines == null || string.IsNullOrWhiteSpace(assetId) || register == null) return;
            FurniturePurchaseLine line = null;
            foreach (FurniturePurchaseLine candidate in order.Lines)
            {
                if (candidate == null || candidate.AssetIds == null) continue;
                foreach (string id in candidate.AssetIds)
                {
                    if (string.Equals(id, assetId, StringComparison.Ordinal)) { line = candidate; break; }
                }
                if (line != null) break;
            }
            if (line == null || line.Stage != EquipmentPurchaseStage.Received) return;
            foreach (string id in line.AssetIds)
            {
                if (register.LiveBindingFor(id) == null) return;
            }
            line.Stage = (EquipmentPurchaseStage)((int)line.Stage + 1);
            if (!order.AllLinesAt(EquipmentPurchaseStage.Installed)) return;
            foreach (FurniturePurchaseLine each in order.Lines)
            {
                if (each != null && each.Stage == EquipmentPurchaseStage.Installed)
                    each.Stage = (EquipmentPurchaseStage)((int)each.Stage + 1);
            }
        }

        /// <summary>
        /// Recomputes an order's installation blocker from its remaining
        /// received-but-uninstalled assets: the first asset with no free
        /// authored placement names the blocker; when every remaining
        /// asset has a placement the blocker clears (the install task
        /// driver will mint for it). Keeps the blocker truthful after a
        /// sibling asset installs, instead of leaving a stale reason.
        /// </summary>
        public static void RefreshInstallationBlocker(
            FurniturePurchaseOrder order,
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            IEnumerable<BuildingEquipmentPlacement> scenePlacements,
            Func<string, bool> placementIsBound,
            FurniturePlacementContext placementContext = null)
        {
            if (order == null || register == null || building == null) return;
            order.InstallationBlockedReason = string.Empty;
            if (order.Lines == null) return;
            foreach (FurniturePurchaseLine line in order.Lines)
            {
                if (line == null || line.Stage != EquipmentPurchaseStage.Received) continue;
                if (line.AssetIds == null) continue;
                foreach (string assetId in line.AssetIds)
                {
                    EquipmentAsset asset = register.FindAsset(assetId);
                    if (asset == null || register.LiveBindingFor(assetId) != null) continue;
                    if (!FurnitureInstallBinding.TryFindFreePlacement(asset.Kind, building, scenePlacements,
                            placementIsBound, placementContext, out _, out string refusal))
                    {
                        order.InstallationBlockedReason = refusal;
                        return;
                    }
                }
            }
        }

        private static FurniturePurchaseOrder FindOrder(IReadOnlyList<FurniturePurchaseOrder> orders,
            int householdId, string purchaseId)
        {
            if (orders == null || string.IsNullOrWhiteSpace(purchaseId)) return null;
            foreach (FurniturePurchaseOrder order in orders)
            {
                if (order != null && order.HouseholdId == householdId
                    && string.Equals(order.PurchaseId, purchaseId, StringComparison.Ordinal)) return order;
            }
            return null;
        }

        /// <summary>
        /// Picks the worker for an install task: the existing assignee, or
        /// the first free household member. The live placement-route
        /// resolver proves physical arrival before work; legacy callers use
        /// the separate home-arrival check.
        /// </summary>
        private static bool TryPickArrivedWorker(
            TaskAuthority tasks,
            IReadOnlyList<EntityId> householdMembers,
            Func<int, int, bool> hasArrivedAtHome,
            int buildingInstanceId,
            WorkTask task,
            out EntityId worker,
            out string refusal)
        {
            worker = EntityId.Invalid;
            refusal = string.Empty;
            if (hasArrivedAtHome == null)
            {
                refusal = "arrival at the home building cannot be proven (no arrival check supplied) — the install waits.";
                return false;
            }
            if (task.HasAssignee)
            {
                if (hasArrivedAtHome(task.AssigneeId.Id, buildingInstanceId))
                {
                    worker = task.AssigneeId;
                    return true;
                }
                refusal = $"P{task.AssigneeId.Id} has not arrived at the home building — the install waits.";
                return false;
            }
            if (householdMembers == null)
            {
                refusal = "no household members are available to install.";
                return false;
            }
            foreach (EntityId member in householdMembers)
            {
                if (!member.IsValid) continue;
                if (tasks.FindBlockingTaskForAssignee(member) != null) continue;
                if (!hasArrivedAtHome(member.Id, buildingInstanceId)) continue;
                worker = member;
                return true;
            }
            refusal = "no available household member has arrived at the home building — the install waits.";
            return false;
        }

        private static bool TryPickAvailableWorker(
            TaskAuthority tasks, IReadOnlyList<EntityId> householdMembers, WorkTask task,
            out EntityId worker, out string refusal)
        {
            worker = EntityId.Invalid;
            refusal = string.Empty;
            if (task.HasAssignee)
            {
                worker = task.AssigneeId;
                return true;
            }
            if (householdMembers == null)
            {
                refusal = "no household members are available to install.";
                return false;
            }
            foreach (EntityId member in householdMembers)
            {
                if (!member.IsValid || tasks.FindBlockingTaskForAssignee(member) != null) continue;
                worker = member;
                return true;
            }
            refusal = "no household member is free to take the installation task.";
            return false;
        }

        private sealed class InstallCandidate
        {
            public EquipmentAsset Asset;
            public FurniturePurchaseOrder Order;
        }
    }
}
