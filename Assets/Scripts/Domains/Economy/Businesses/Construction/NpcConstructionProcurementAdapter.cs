// Ported verbatim from forestry Slice 07, commit 8fa6aca0, onto the
// economic-integration line (baseline 2cb4781) as C4 Tier 1. No logic
// change; this header note is the only addition. The adapter lands
// unbound (C1 precedent); the factory + binding are Tier 2, gated on
// owner decisions D1 (yard-to-site miles) and D2 (rate card, B10-OD-5).
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Slice 07: the production INpcConstructionProcurementPort. One adapter
// connects the driver port slot to the commissioner procurement method,
// so a Ready package is bought from one real supplier, paid in real
// cash, freighted by a real wagon through the real shipment chain, and
// staged from a fresh transient site stockpile.
//
// Ownership facts this adapter relies on:
// - The commissioner is the procurement principal. The adapter delegates
//   to ProcureAndStagePackage and never prices, pays, or stages itself:
//   the overrun gate, the priced-total check, the cash payment, the
//   freight leg, and the staging all run inside the commissioner and the
//   procurement service, exactly as in the full-chain proofs.
// - The plan factory is injected, never invented inside the adapter.
//   It supplies the wagon, the crew, and the freight charge from the
//   carrier quote authority (Brief 10 B10-OD-5). The adapter refuses
//   before anything moves when the factory returns null or a plan with
//   no FreightShipmentService, no wagon, no loader, or no unloader, so
//   a broken plan can never reach the sale: nothing is sold, nothing
//   is paid, nothing is staged on a plan refusal.
// - The stockpile is transient within one drive step. A fresh
//   ConstructionSiteStockpile is built per call and exposed only as
//   LastStockpile for tests; no units sit unpersisted across a save
//   boundary, matching the driver comment on the port interface.
// - The adapter never moves money itself and never creates a lot.
// - The buyer name is derived from the project household id as
//   "household:" plus the id, the same convention the driver uses in
//   FundAndStage and the driver tests use for the procurement port.
// - Plan gap, recorded here because the slice 07 plan holding list
//   omits it: ProcureAndStagePackage also requires a
//   ConstructionProjectExecutor, a CreditCashBridge, a
//   FinancialObligationAuthority, and an EntityIdRegistry, none of
//   which the plan lists. The adapter holds those four as well, in
//   addition to the seven plan-listed holdings, because without them
//   the delegation the plan requires cannot be called at all. The
//   obligations authority and the id registry are passed through to
//   the commissioner and the procurement service, which use them only
//   to book a real payable when a cash payment fails.
// - A package procures once. The adapter refuses any package that is
//   not Ready before calling the factory, so a second call after
//   success cannot buy the materials a second time: the executor
//   stages once from Ready, and the adapter never lets a second sale
//   reach it.
// - Refusal semantics verified in source, not taken from the plan:
//   DiscoverSupplier returns null for a named supplier that is not
//   discoverable and for an undiscoverable category, and the
//   procurement service refuses a package spanning two suppliers
//   before any sale; a sale total that does not equal the priced total
//   refuses after the sale stands but before any cash moves; a freight
//   refusal leaves the sale standing at the supplier (already paid)
//   and nothing in the stockpile.
// - No production binding. Sol owns the binding at SimulationDrivers
//   DriveNpcConstruction, constructing one adapter per drive with the
//   hub owned yard sale port, the slice 02 price port, the supplier
//   directory, the freight coordinator, and a plan factory built from
//   a real wagon and a real crew.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy
{
    public sealed class NpcConstructionProcurementAdapter : INpcConstructionProcurementPort
    {
        private readonly NpcConstructionCommissioner commissioner;
        private readonly ConstructionProcurementService procurement;
        private readonly IConstructionMaterialSalePort salePort;
        private readonly IConstructionMaterialPricePort pricePort;
        private readonly ISupplierDirectory suppliers;
        private readonly ConstructionMaterialFreightCoordinator freightCoordinator;
        private readonly ConstructionProjectExecutor executor;
        private readonly CreditCashBridge cash;
        private readonly FinancialObligationAuthority obligations;
        private readonly EntityIdRegistry ids;
        private readonly Func<WorkPackage, ConstructionFreightPlan> planFactory;

        /// <summary>The transient stockpile built for the most recent call, or null before any call.</summary>
        public ConstructionSiteStockpile LastStockpile { get; private set; }

        public NpcConstructionProcurementAdapter(
            NpcConstructionCommissioner commissioner,
            ConstructionProcurementService procurement,
            IConstructionMaterialSalePort salePort,
            IConstructionMaterialPricePort pricePort,
            ISupplierDirectory suppliers,
            ConstructionMaterialFreightCoordinator freightCoordinator,
            Func<WorkPackage, ConstructionFreightPlan> planFactory,
            ConstructionProjectExecutor executor,
            CreditCashBridge cash,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids)
        {
            this.commissioner = commissioner;
            this.procurement = procurement;
            this.salePort = salePort;
            this.pricePort = pricePort;
            this.suppliers = suppliers;
            this.freightCoordinator = freightCoordinator;
            this.planFactory = planFactory;
            this.executor = executor;
            this.cash = cash;
            this.obligations = obligations;
            this.ids = ids;
        }

        /// <summary>
        /// Procures and stages one Ready package through the commissioner.
        /// Returns null on success, or a refusal naming the problem.
        /// Refuses when any bound authority is missing, when the package
        /// is not Ready, when the plan factory returns null or a plan
        /// with no FreightShipmentService, no wagon, no loader, or no
        /// unloader. On a plan refusal nothing is sold, nothing is paid,
        /// and nothing is staged. A refusal from the commissioner or the
        /// procurement service is returned unchanged.
        /// </summary>
        public string ProcureAndStage(ConstructionProject project, WorkPackage package,
            NpcConstructionRecord record, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (project == null || package == null || record == null)
                return "NpcConstructionProcurementAdapter: project, package and record required.";
            if (commissioner == null)
                return "NpcConstructionProcurementAdapter: no commissioner — procurement refused.";
            if (procurement == null)
                return "NpcConstructionProcurementAdapter: no procurement service — procurement refused.";
            if (salePort == null)
                return "NpcConstructionProcurementAdapter: no supplier sale port — procurement refused.";
            if (pricePort == null)
                return "NpcConstructionProcurementAdapter: no price port — procurement refused.";
            if (suppliers == null)
                return "NpcConstructionProcurementAdapter: no supplier directory — procurement refused.";
            if (freightCoordinator == null)
                return "NpcConstructionProcurementAdapter: no freight coordinator — procurement refused.";
            if (executor == null)
                return "NpcConstructionProcurementAdapter: no project executor — procurement refused.";
            if (cash == null)
                return "NpcConstructionProcurementAdapter: no cash bridge — procurement refused.";
            if (planFactory == null)
                return "NpcConstructionProcurementAdapter: no freight plan factory — procurement refused.";
            if (package.Status != WorkPackageStatus.Ready)
                return $"NpcConstructionProcurementAdapter: package '{package.PackageId}' is {package.Status} — materials procure once from Ready.";

            ConstructionFreightPlan plan = planFactory(package);
            if (plan == null)
                return $"NpcConstructionProcurementAdapter: the freight plan factory returned no plan for package '{package.PackageId}' — procurement refused, nothing sold.";
            if (plan.Freight == null)
                return $"NpcConstructionProcurementAdapter: the freight plan for package '{package.PackageId}' has no FreightShipmentService — procurement refused, nothing sold.";
            if (plan.Wagon == null || string.IsNullOrWhiteSpace(plan.Wagon.AssetId))
                return $"NpcConstructionProcurementAdapter: the freight plan for package '{package.PackageId}' has no wagon — procurement refused, nothing sold.";
            if (!plan.LoaderId.IsValid || plan.LoaderId.Kind != EntityKind.Person)
                return $"NpcConstructionProcurementAdapter: the freight plan for package '{package.PackageId}' has no loader person — procurement refused, nothing sold.";
            if (!plan.UnloaderId.IsValid || plan.UnloaderId.Kind != EntityKind.Person)
                return $"NpcConstructionProcurementAdapter: the freight plan for package '{package.PackageId}' has no unloader person — procurement refused, nothing sold.";

            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            LastStockpile = stockpile;
            string buyerOwnerName = "household:" + project.HouseholdId;
            return commissioner.ProcureAndStagePackage(executor, project, record, package,
                procurement, salePort, pricePort, suppliers, freightCoordinator, plan,
                stockpile, cash, buyerOwnerName, obligations, ids, dayIndex, diag);
        }
    }
}
