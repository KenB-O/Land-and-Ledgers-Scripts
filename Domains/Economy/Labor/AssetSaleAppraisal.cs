// Pass IX WS5 (PASS8-003 + A3-08): production appraisal, asset-sale
// execution, and the production liquidation port for orderly close-down.
//
// Before this pass the SellAsset chain stopped at approval: the production
// AppraiseSaleCents seam was null (every NPC sale denied), requests named
// an amount but no asset, no execution step existed behind an approval,
// and CloseOrderly ran with Liquidation = null in production.
//
// Coordinator decision 3 fixes the shape implemented here:
//   - A SellAsset proposal names the SPECIFIC asset, taken from the
//     business's own asset registers at proposal time (see
//     ManagerCycleInputsBuilder): an EquipmentAsset id, or
//     "formation:{ItemId}" for an NpcFormationAsset.
//   - The appraisal reads ONLY legitimately available records: the
//     equipment register entry itself (identity, condition, ownership) and
//     the business's own procurement record for that asset (its book
//     cost), or the formation register's recorded cost/provenance. No
//     private information is consulted and no number is invented: without
//     register evidence the appraisal is an explicit NO-EVIDENCE result,
//     and the owner-resolution policy denies the sale (never blind).
//   - On approval, the execution step transfers the named asset to a real
//     buyer business through EquipmentAsset.TransferOwnership (the
//     ownership authority for equipment; formation assets are marked
//     liquidated, the register's existing consumed flag) and posts both
//     cash legs exactly once through the business tills
//     (BusinessRuntimeState Spend/Add — the existing cash writers).
//
// Value kinds stay DISTINCT (Canon §11.3/§11.5), never conflated:
//   - GrossAssetValueCents: the undiscounted book/replacement basis.
//   - EstimatedValueCents: the appraised going-concern value of the asset
//     (book basis weighted by recorded condition for equipment).
//   - LiquidationValueCents: EstimatedValue at the documented liquidation
//     discount — what a forced/orderly wind-down sale is expected to fetch.
//   - AskingPriceCents: what the seller asks (the manager's proposal asks
//     the estimated value; the owner policy approves at/above appraised).
//   - SalePriceCents: what a completed sale actually transacted at (the
//     approved request amount), recorded on the execution record.
//   Business operating value and owner equity are NOT computed here — they
//   remain EnterpriseValuation's two Canon-locked values; an asset
//   appraisal is evidence toward them, never a substitute.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Creation;

namespace LandLedgers.Economy.Labor
{
    /// <summary>One appraisal outcome. HasEvidence=false is a first-class
    /// result: the registers hold no basis for this asset, so NO confident
    /// estimate exists and every value field is 0 by construction.</summary>
    [Serializable]
    public sealed class AssetAppraisalResult
    {
        public bool HasEvidence;
        public string AssetId = string.Empty;
        public string BasisNote = string.Empty;
        public int GrossAssetValueCents;
        public int EstimatedValueCents;
        public int LiquidationValueCents;
        public int AskingPriceCents;

        public static AssetAppraisalResult NoEvidence(string assetId, string reason)
        {
            return new AssetAppraisalResult
            {
                HasEvidence = false,
                AssetId = assetId ?? string.Empty,
                BasisNote = reason ?? string.Empty,
            };
        }
    }

    /// <summary>
    /// The production appraisal source for named-asset sales and close-down
    /// liquidation. Plain class (harness-testable); SimulationDrivers binds
    /// the production registers into it.
    /// </summary>
    public sealed class AssetSaleAppraisalService
    {
        /// <summary>
        /// Documented liquidation discount (calibration, Canon Part XV):
        /// an orderly wind-down / distress sale is expected to realize 60%
        /// of the appraised estimated value — the remaining 40% is the
        /// forced-sale discount (limited buyer pool, sale under time
        /// pressure, no going-concern premium).
        /// </summary>
        public const int LiquidationValueBps = 6000;

        /// <summary>Asset-id prefix naming a formation-register asset.</summary>
        public const string FormationAssetPrefix = "formation:";

        /// <summary>Finds an equipment asset by id (the equipment/transport register).</summary>
        public Func<string, EquipmentAsset> FindEquipmentAsset;
        /// <summary>
        /// The business's own book-cost evidence for an equipment asset, in
        /// cents: its procurement record (order total) for that asset id.
        /// Returns 0 when the business holds no such record — no evidence.
        /// </summary>
        public Func<string, string, int> EquipmentBookCostCents;
        /// <summary>The formation asset register (provenance + recorded cost).</summary>
        public NpcFormationAssetRegister FormationAssets;

        public static bool IsFormationAssetId(string assetId)
        {
            return !string.IsNullOrWhiteSpace(assetId)
                && assetId.StartsWith(FormationAssetPrefix, StringComparison.Ordinal);
        }

        public static string FormationItemId(string assetId)
        {
            return IsFormationAssetId(assetId)
                ? assetId.Substring(FormationAssetPrefix.Length)
                : string.Empty;
        }

        /// <summary>Applies the documented liquidation discount.</summary>
        public static int LiquidationValueFor(int estimatedValueCents)
        {
            if (estimatedValueCents <= 0) return 0;
            return (int)((long)estimatedValueCents * LiquidationValueBps / 10000);
        }

        /// <summary>
        /// Appraises an equipment asset from register evidence: identity and
        /// condition from the register entry, gross basis from the business's
        /// own procurement record for the asset. Missing either is
        /// no-evidence — never a guessed number.
        /// </summary>
        public AssetAppraisalResult AppraiseEquipment(string businessInstanceId, EquipmentAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId))
            {
                return AssetAppraisalResult.NoEvidence(asset != null ? asset.AssetId : string.Empty,
                    "no equipment asset in the register under that id");
            }

            if (!string.Equals(asset.OwnerKind, "business", StringComparison.Ordinal)
                || !string.Equals(asset.OwnerId, businessInstanceId ?? string.Empty, StringComparison.Ordinal))
            {
                return AssetAppraisalResult.NoEvidence(asset.AssetId,
                    $"asset '{asset.AssetId}' is held by {asset.OwnerKind}:{asset.OwnerId}, " +
                    $"not by business '{businessInstanceId}' — not the seller's to appraise or sell");
            }

            int bookCost = EquipmentBookCostCents != null
                ? Math.Max(0, EquipmentBookCostCents(businessInstanceId, asset.AssetId))
                : 0;
            if (bookCost <= 0)
            {
                return AssetAppraisalResult.NoEvidence(asset.AssetId,
                    $"no book-cost evidence for asset '{asset.AssetId}': the business holds no " +
                    "procurement record for it, so no confident estimate exists");
            }

            float condition = Math.Max(0f, Math.Min(1f, asset.Condition01));
            int estimated = (int)Math.Round(bookCost * condition);
            return new AssetAppraisalResult
            {
                HasEvidence = true,
                AssetId = asset.AssetId,
                BasisNote = $"equipment register: kind '{asset.Kind}', condition {condition:P0}; " +
                            $"gross basis {bookCost}c from the business's own procurement record; " +
                            "estimated = gross x condition (Canon §11.6 condition-weighted basis)",
                GrossAssetValueCents = bookCost,
                EstimatedValueCents = estimated,
                LiquidationValueCents = LiquidationValueFor(estimated),
                AskingPriceCents = estimated,
            };
        }

        /// <summary>
        /// Appraises a formation-register asset: the register's recorded cost
        /// (paid at formation, provenance named) is the book basis. A
        /// liquidated asset, a missing entry, or a zero recorded cost is
        /// no-evidence.
        /// </summary>
        public AssetAppraisalResult AppraiseFormation(string businessInstanceId, NpcFormationAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.ItemId))
            {
                return AssetAppraisalResult.NoEvidence(string.Empty, "no formation asset under that id");
            }

            string assetId = FormationAssetPrefix + asset.ItemId;
            // R-01: ownership check mirroring AppraiseEquipment — a foreign
            // (or unscoped/legacy unowned) asset is not the seller's to appraise.
            if (string.IsNullOrWhiteSpace(asset.OwnerBusinessId)
                || !string.Equals(asset.OwnerBusinessId, businessInstanceId ?? string.Empty, StringComparison.Ordinal))
            {
                return AssetAppraisalResult.NoEvidence(assetId,
                    $"formation asset '{asset.ItemId}' is owned by '{asset.OwnerBusinessId}', " +
                    $"not by business '{businessInstanceId}' — not the seller's to appraise or sell");
            }
            if (asset.Liquidated)
            {
                return AssetAppraisalResult.NoEvidence(assetId,
                    $"formation asset '{asset.ItemId}' is already liquidated — nothing remains to appraise");
            }

            if (asset.CostCents <= 0 || asset.Units <= 0)
            {
                return AssetAppraisalResult.NoEvidence(assetId,
                    $"formation asset '{asset.ItemId}' carries no recorded cost/units — " +
                    "no confident estimate exists");
            }

            return new AssetAppraisalResult
            {
                HasEvidence = true,
                AssetId = assetId,
                BasisNote = $"formation register: {asset.Units}x '{asset.ItemId}' ({asset.AssetKind}), " +
                            $"recorded cost {asset.CostCents}c, provenance '{asset.Provenance}'",
                GrossAssetValueCents = asset.CostCents,
                EstimatedValueCents = asset.CostCents,
                LiquidationValueCents = LiquidationValueFor(asset.CostCents),
                AskingPriceCents = asset.CostCents,
            };
        }

        /// <summary>Finds a formation asset by item id in the bound register.</summary>
        public NpcFormationAsset FindFormationAsset(string itemId)
        {
            if (FormationAssets == null || string.IsNullOrWhiteSpace(itemId)) return null;
            foreach (NpcFormationAsset asset in FormationAssets.Assets)
            {
                if (asset != null && string.Equals(asset.ItemId, itemId, StringComparison.Ordinal))
                    return asset;
            }

            return null;
        }

        /// <summary>R-01: finds the formation asset owned by a specific business.</summary>
        public NpcFormationAsset FindFormationAsset(string businessInstanceId, string itemId)
        {
            if (FormationAssets == null || string.IsNullOrWhiteSpace(itemId)
                || string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            foreach (NpcFormationAsset asset in FormationAssets.Assets)
            {
                if (asset != null && string.Equals(asset.ItemId, itemId, StringComparison.Ordinal)
                    && string.Equals(asset.OwnerBusinessId, businessInstanceId, StringComparison.Ordinal))
                    return asset;
            }

            return null;
        }

        /// <summary>Appraises whatever asset a request/asset id names.</summary>
        public AssetAppraisalResult Appraise(string businessInstanceId, string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return AssetAppraisalResult.NoEvidence(string.Empty,
                    "the sale names no asset — an unnamed sale cannot be appraised");
            }

            if (IsFormationAssetId(assetId))
            {
                return AppraiseFormation(businessInstanceId,
                    FindFormationAsset(businessInstanceId, FormationItemId(assetId))
                    ?? FindFormationAsset(FormationItemId(assetId)));
            }

            EquipmentAsset asset = FindEquipmentAsset != null ? FindEquipmentAsset(assetId) : null;
            return AppraiseEquipment(businessInstanceId, asset);
        }

        /// <summary>
        /// The production AppraiseSaleCents seam for NpcOwnerRequestResolver:
        /// the appraised ESTIMATED value of the request's named asset, or -1
        /// when the registers hold no evidence (the resolver's documented
        /// "unknown — the owner does not approve blind sales" signal).
        /// </summary>
        public int AppraiseSaleCents(ManagerPendingRequest request)
        {
            if (request == null) return -1;
            AssetAppraisalResult result = Appraise(request.BusinessInstanceId, request.AssetId);
            return result.HasEvidence ? result.EstimatedValueCents : -1;
        }
    }

    /// <summary>Everything one approved-sale execution run needs. Plain data.</summary>
    public sealed class AssetSaleExecutionContext
    {
        public Func<string, BusinessInstanceState> FindBusiness;
        public Func<IEnumerable<BusinessInstanceState>> AllBusinesses;
        public Func<string, EquipmentAsset> FindEquipmentAsset;
        public NpcFormationAssetRegister FormationAssets;
        public int DayIndex;
    }

    /// <summary>One completed asset sale: asset, parties, and the SALE PRICE
    /// actually transacted (distinct from asking/appraised values).</summary>
    [Serializable]
    public sealed class AssetSaleRecord
    {
        public string RequestId = string.Empty;
        public string AssetId = string.Empty;
        public string SellerBusinessId = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public int SalePriceCents;
        public int DayIndex = -1;
    }

    /// <summary>
    /// Executes APPROVED manager asset sales (the step that did not exist
    /// behind NpcOwnerRequestResolver approvals): the named asset transfers
    /// to a real buyer business and both cash legs post exactly once.
    ///
    /// Exactly-once discipline: a sale executes only from Status "Approved";
    /// success moves the request to Status "Executed" and records an action
    /// on the execution ledger under the request id, so a repeat run — in
    /// this process or after a save/load — refuses instead of re-posting.
    /// A sale that cannot complete (no solvent buyer, asset gone or no
    /// longer the seller's, title transfer refused) leaves the request
    /// Approved with the reason appended to its note; no cash moves unless
    /// the whole sale completes, and a late title refusal rolls both legs
    /// back.
    /// </summary>
    public sealed class AssetSaleExecutionService
    {
        private readonly List<AssetSaleRecord> completedSales = new List<AssetSaleRecord>();

        public IReadOnlyList<AssetSaleRecord> CompletedSales => completedSales;

        /// <summary>Executes every Approved SellAsset request on the ledger.
        /// Returns the number of sales completed in this run.</summary>
        public int ExecuteApprovedSales(
            ManagerDecisionExecutionLedger ledger,
            AssetSaleExecutionContext ctx,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (ledger == null || ctx == null || ctx.FindBusiness == null || ctx.AllBusinesses == null)
            {
                diagnostics.Add("AssetSaleExecution: ledger and business lookups are required — nothing executed.");
                return 0;
            }

            int executed = 0;
            // Snapshot: execution mutates request status, never the list.
            var requests = new List<ManagerPendingRequest>(ledger.PendingRequests);
            foreach (ManagerPendingRequest request in requests)
            {
                if (request == null
                    || !string.Equals(request.Kind, ManagerActKinds.SellAsset, StringComparison.Ordinal))
                    continue;
                if (ExecuteOne(ledger, request, ctx, diagnostics))
                    executed++;
            }

            return executed;
        }

        /// <summary>Executes one approved sale. Returns true only when the
        /// asset transferred and both cash legs posted in this call.</summary>
        public bool ExecuteOne(
            ManagerDecisionExecutionLedger ledger,
            ManagerPendingRequest request,
            AssetSaleExecutionContext ctx,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (request == null || ctx == null) return false;

            if (!string.Equals(request.Status, "Approved", StringComparison.Ordinal))
            {
                diagnostics.Add($"AssetSaleExecution: request {request.RequestId} is '{request.Status}', " +
                                "not Approved — refused (a sale executes exactly once, from approval).");
                return false;
            }

            string actionKey = "SellAssetExecuted:" + request.RequestId;
            if (ledger != null && ledger.HasExecuted(actionKey))
            {
                diagnostics.Add($"AssetSaleExecution: request {request.RequestId} already has an " +
                                "execution record — refused, no second transfer.");
                return false;
            }

            int salePrice = Math.Max(0, request.AmountCents);
            if (salePrice <= 0)
            {
                return Refuse(request, "no sale price on the approved request", diagnostics);
            }

            if (string.IsNullOrWhiteSpace(request.AssetId))
            {
                return Refuse(request, "the approved request names no asset — nothing can transfer", diagnostics);
            }

            BusinessInstanceState seller = ctx.FindBusiness(request.BusinessInstanceId);
            if (seller == null || seller.RuntimeState == null)
            {
                return Refuse(request, $"seller business '{request.BusinessInstanceId}' is unavailable", diagnostics);
            }

            // Resolve the named asset BEFORE any cash moves.
            EquipmentAsset equipment = null;
            NpcFormationAsset formation = null;
            if (AssetSaleAppraisalService.IsFormationAssetId(request.AssetId))
            {
                formation = FindFormation(ctx.FormationAssets,
                    AssetSaleAppraisalService.FormationItemId(request.AssetId));
                if (formation == null || formation.Liquidated)
                {
                    return Refuse(request, $"formation asset '{request.AssetId}' is gone or already liquidated", diagnostics);
                }

                // R-01: only the seller's own formation asset can be sold.
                if (!string.Equals(formation.OwnerBusinessId, seller.InstanceId, StringComparison.Ordinal))
                {
                    return Refuse(request,
                        $"formation asset '{request.AssetId}' is owned by '{formation.OwnerBusinessId}', " +
                        $"not by the seller '{seller.InstanceId}'", diagnostics);
                }
            }
            else
            {
                equipment = ctx.FindEquipmentAsset != null ? ctx.FindEquipmentAsset(request.AssetId) : null;
                if (equipment == null)
                {
                    return Refuse(request, $"equipment asset '{request.AssetId}' is not in the register", diagnostics);
                }

                if (!string.Equals(equipment.OwnerKind, "business", StringComparison.Ordinal)
                    || !string.Equals(equipment.OwnerId, seller.InstanceId, StringComparison.Ordinal))
                {
                    return Refuse(request,
                        $"asset '{request.AssetId}' is held by {equipment.OwnerKind}:{equipment.OwnerId}, " +
                        $"not by the seller '{seller.InstanceId}'", diagnostics);
                }

                if (equipment.IsReserved)
                {
                    return Refuse(request, $"asset '{request.AssetId}' is reserved by {equipment.ReservedBy} — no double-dealing", diagnostics);
                }
            }

            BusinessInstanceState buyer = FindBuyer(ctx, seller, salePrice);
            if (buyer == null)
            {
                return Refuse(request,
                    $"no other business can pay the {salePrice}c sale price — the sale waits for a real buyer", diagnostics);
            }

            // Cash legs, exactly once, through the existing till writers:
            // buyer pays the sale price, seller receives the sale price.
            // The buyer's balance was just verified, so SpendCents posts in
            // full; nothing is minted and nothing is partial.
            buyer.RuntimeState.SpendCents(salePrice);
            seller.RuntimeState.AddCashCents(salePrice);

            // Title transfer through the ownership authority. A refusal here
            // (e.g. the asset became reserved between check and transfer)
            // rolls both cash legs back — no partial state.
            string transferRefusal = null;
            if (equipment != null)
            {
                transferRefusal = equipment.TransferOwnership("business", buyer.InstanceId,
                    $"approved manager asset sale {request.RequestId}", ctx.DayIndex);
                if (transferRefusal == null)
                {
                    seller.GenericConfiguration.RemoveEquipmentReference(equipment.AssetId);
                    buyer.GenericConfiguration.AddEquipmentReference(equipment.AssetId);
                }
            }
            else
            {
                // R-01/R-12: a formation-asset SALE transfers ownership to
                // the buyer — the same register record now names the buyer
                // as owner (a real register record for the buyer), it is NOT
                // liquidated/consumed. Only CloseOrderly liquidation of a
                // business's OWN assets marks Liquidated for cash.
                formation.OwnerBusinessId = buyer.InstanceId;
            }

            if (transferRefusal != null)
            {
                seller.RuntimeState.SpendCents(salePrice);
                buyer.RuntimeState.AddCashCents(salePrice);
                return Refuse(request, $"title transfer refused ({transferRefusal}) — cash legs rolled back", diagnostics);
            }

            request.Status = "Executed";
            request.ResolutionNote = (request.ResolutionNote ?? string.Empty) +
                $" | EXECUTED day {ctx.DayIndex}: asset '{request.AssetId}' sold to '{buyer.InstanceId}' " +
                $"at sale price {salePrice}c (title transferred, both cash legs posted once).";
            var record = new AssetSaleRecord
            {
                RequestId = request.RequestId,
                AssetId = request.AssetId,
                SellerBusinessId = seller.InstanceId,
                BuyerBusinessId = buyer.InstanceId,
                SalePriceCents = salePrice,
                DayIndex = ctx.DayIndex,
            };
            completedSales.Add(record);
            ledger?.RecordAction(new ManagerExecutedActionRecord
            {
                ActionKey = actionKey,
                BusinessInstanceId = seller.InstanceId,
                DayIndex = ctx.DayIndex,
                Kind = ManagerActKinds.SellAsset,
                Succeeded = true,
                Reason = string.Empty,
                ResultingReference = $"asset-sale:{request.AssetId}:{seller.InstanceId}->{buyer.InstanceId}:{salePrice}c",
                ManagerPersonId = request.ManagerPersonId,
            });
            diagnostics.Add($"AssetSaleExecution: {request.RequestId} EXECUTED — '{request.AssetId}' " +
                            $"{seller.InstanceId} -> {buyer.InstanceId} at {salePrice}c.");
            return true;
        }

        private static BusinessInstanceState FindBuyer(
            AssetSaleExecutionContext ctx, BusinessInstanceState seller, int salePriceCents)
        {
            // Deterministic: the solvent buyer with the lowest instance id.
            // The buyer pays real cash from its own till — a business that
            // cannot cover the price is not a buyer.
            BusinessInstanceState best = null;
            foreach (BusinessInstanceState candidate in ctx.AllBusinesses())
            {
                if (candidate == null || candidate.RuntimeState == null
                    || string.IsNullOrWhiteSpace(candidate.InstanceId))
                    continue;
                if (string.Equals(candidate.InstanceId, seller.InstanceId, StringComparison.Ordinal))
                    continue;
                if (candidate.RuntimeState.CurrentCashCents < salePriceCents)
                    continue;
                if (best == null || string.CompareOrdinal(candidate.InstanceId, best.InstanceId) < 0)
                    best = candidate;
            }

            return best;
        }

        private static NpcFormationAsset FindFormation(NpcFormationAssetRegister register, string itemId)
        {
            if (register == null || string.IsNullOrWhiteSpace(itemId)) return null;
            foreach (NpcFormationAsset asset in register.Assets)
            {
                if (asset != null && string.Equals(asset.ItemId, itemId, StringComparison.Ordinal))
                    return asset;
            }

            return null;
        }

        private bool Refuse(ManagerPendingRequest request, string reason, List<string> diagnostics)
        {
            // The request STAYS Approved (the owner's consent stands); the
            // failure is appended to its note so the blocker is visible and
            // a later run can complete the sale when conditions change.
            // R-12: do not append the same deferral reason unboundedly on
            // daily retries — one entry per distinct reason.
            string entry = $" | execution deferred: {reason}.";
            string note = request.ResolutionNote ?? string.Empty;
            if (!note.Contains(entry))
            {
                request.ResolutionNote = note + entry;
            }

            diagnostics.Add($"AssetSaleExecution: {request.RequestId} deferred — {reason}.");
            return false;
        }
    }

    /// <summary>
    /// Pass IX WS5: the production INpcLiquidationPort for orderly
    /// close-down (CloseOrderly). Production previously passed
    /// Liquidation = null, so closing businesses settled debts from cash
    /// alone and formation assets were never sold.
    ///
    /// Each liquidation is a REAL sale at the appraisal's liquidation
    /// value: a named buyer business whose till actually covers the
    /// proceeds pays them through the business cash port (the port debits
    /// the buyer here; CloseOrderly credits the closing business when it
    /// records the sale — both legs real, total cash conserved). No
    /// solvent buyer, or no register evidence for the asset, is a loud
    /// refusal: the asset is flagged by CloseOrderly, never written off
    /// and never sold into fiction.
    /// </summary>
    public sealed class RegisterLiquidationPort : INpcLiquidationPort
    {
        private readonly Func<IEnumerable<BusinessInstanceState>> allBusinesses;
        private readonly INpcBusinessCashPort cash;
        private readonly AssetSaleAppraisalService appraisal;

        public RegisterLiquidationPort(
            Func<IEnumerable<BusinessInstanceState>> allBusinesses,
            INpcBusinessCashPort cash,
            AssetSaleAppraisalService appraisal)
        {
            this.allBusinesses = allBusinesses;
            this.cash = cash;
            this.appraisal = appraisal ?? new AssetSaleAppraisalService();
        }

        public string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
            out NpcLiquidationSale sale)
        {
            sale = null;
            if (asset == null) return "RegisterLiquidationPort: no asset supplied.";
            if (cash == null || allBusinesses == null)
                return "RegisterLiquidationPort: no cash port or business directory — no real buyer can pay.";
            // R-01: liquidation only ever sells the closing business's OWN asset.
            if (!string.Equals(asset.OwnerBusinessId, businessInstanceId ?? string.Empty, StringComparison.Ordinal))
            {
                return $"RegisterLiquidationPort: formation asset '{asset.ItemId}' is owned by " +
                       $"'{asset.OwnerBusinessId}', not by closing business '{businessInstanceId}' — refused.";
            }

            AssetAppraisalResult value = appraisal.AppraiseFormation(businessInstanceId, asset);
            if (!value.HasEvidence)
            {
                return $"RegisterLiquidationPort: no liquidation without evidence — {value.BasisNote}.";
            }

            int proceeds = value.LiquidationValueCents;
            if (proceeds <= 0)
            {
                return $"RegisterLiquidationPort: '{asset.ItemId}' appraises to a zero liquidation value — no sale.";
            }

            // Deterministic buyer: the solvent other business with the
            // lowest instance id, paying the liquidation value in full.
            BusinessInstanceState buyer = null;
            foreach (BusinessInstanceState candidate in allBusinesses())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.InstanceId))
                    continue;
                if (string.Equals(candidate.InstanceId, businessInstanceId, StringComparison.Ordinal))
                    continue;
                if (cash.ReadCashCents(candidate.InstanceId) < proceeds)
                    continue;
                if (buyer == null || string.CompareOrdinal(candidate.InstanceId, buyer.InstanceId) < 0)
                    buyer = candidate;
            }

            if (buyer == null)
            {
                return $"RegisterLiquidationPort: no solvent buyer for '{asset.ItemId}' " +
                       $"at its {proceeds}c liquidation value — the asset is not sold into fiction.";
            }

            string refusal = cash.Spend(buyer.InstanceId, dayIndex, proceeds,
                $"liquidation purchase of '{asset.ItemId}' from closing business '{businessInstanceId}'");
            if (refusal != null)
            {
                return $"RegisterLiquidationPort: buyer '{buyer.InstanceId}' could not pay: {refusal}";
            }

            sale = new NpcLiquidationSale
            {
                AssetItemId = asset.ItemId,
                Units = asset.Units,
                BuyerName = "business:" + buyer.InstanceId,
                ProceedsCents = proceeds,
            };
            return null;
        }
    }
}
