using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// UBC lane A1: business acquisition continuity, brief section 18 scenarios
    /// A through G. The required scenario is the NPC General Store: after the
    /// player acquires it, the same enterprise (same business id, same stock,
    /// same agreements, same cash and liabilities) must keep operating under
    /// the store runtime, with no phantom second business.
    /// UBC lane A2 extends this file with the four A1 follow-ups: seller
    /// proceeds reach the seller, the former owner is not silently kept on
    /// as an employee, sale terms are recorded on the player path, and a sale
    /// to Town unbinds the store from the player store runtime.
    /// Unity EditMode tests: this file needs the real town assets and the
    /// acquisition cluster (SharedBusinessRuntimeManager, store runtime,
    /// portfolio, market), which the Roslyn harness does not compile.
    /// </summary>
    public sealed class UbcA1AcquisitionContinuityTests
    {
        private const int TestOwnerCashCents = 500_000;

        [Test]
        public void AcquiredGeneralStoreKeepsBusinessId()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            string instanceId = store.InstanceId;
            Assert.IsFalse(string.IsNullOrWhiteSpace(instanceId), "The opening store must have a stable business id.");

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out BusinessInstanceState transferred, out string message),
                message);

            Assert.AreSame(store, transferred, "Transfer must move the same business object, not a replacement.");
            Assert.AreEqual(instanceId, transferred.InstanceId, "Business id must survive the sale.");
            Assert.AreEqual(BusinessOwnerKind.Player, transferred.Owner.OwnerKind);
            Assert.IsTrue(context.SharedBusinessRuntime.TryFindByInstanceId(instanceId, out BusinessInstanceState surviving));
            Assert.AreSame(store, surviving);
        }

        [Test]
        public void AcquiredGeneralStoreInventoryIsNotDuplicated()
        {
            LogAssert.Expect(LogType.Error,
                "Failed to load 'F:/Land & Ledgers/Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim'. File may be corrupted or was serialized with a newer version of Unity.");
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);

            List<(string CategoryId, int Units)> before = SnapshotStock(store);
            if (before.Count > 0)
            {
                context.SetBusinessStock(store, before[0].CategoryId, 37);
                before = SnapshotStock(store);
                Assert.AreEqual(37, before[0].Units);
            }

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                message);

            List<(string CategoryId, int Units)> after = SnapshotStock(store);
            Assert.AreEqual(before.Count, after.Count, "Transfer must not add or drop stock categories.");
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].CategoryId, after[i].CategoryId);
                Assert.AreEqual(before[i].Units, after[i].Units, $"Stock for {before[i].CategoryId} must be preserved exactly, not duplicated.");
            }
        }

        [Test]
        public void TransferAgreementNamingAcquiredStoreSurvivesTransfer()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            BusinessInstanceState sawmill = context.GetBusiness(BusinessType.Sawmill);

            BusinessTransferAgreementState agreement = new();
            agreement.Configure(
                "a1_sawmill_to_store",
                sawmill.InstanceId,
                store.InstanceId,
                "lumber",
                "lumber",
                BusinessTransferAllocationMode.FixedUnits,
                5,
                BusinessCadence.Weekly,
                1,
                BusinessTransferPricingMode.Market,
                1f,
                2,
                LocalRecurringOrderHaulingResponsibility.Seller,
                true,
                9);
            context.SetTransferAgreements(agreement);

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                message);

            BusinessTransferAgreementState surviving = context.SharedBusinessRuntime.TransferAgreements
                .FirstOrDefault(candidate => candidate != null && candidate.AgreementId == "a1_sawmill_to_store");
            Assert.NotNull(surviving, "A transfer agreement naming the acquired store must survive the sale.");
            Assert.AreEqual(sawmill.InstanceId, surviving.SourceBusinessInstanceId);
            Assert.AreEqual(store.InstanceId, surviving.DestinationBusinessInstanceId);
            Assert.IsTrue(surviving.Active, "The agreement must stay active after the sale.");
        }

        [Test]
        public void TransferPreservesCashAndLiabilities()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            context.SetBusinessCash(store, 40000);
            SetAccruedLiability(store, 12000);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                message);

            Assert.AreEqual(40000, store.RuntimeState.CurrentCashCents, "The transfer itself must not change business cash.");
            Assert.AreEqual(12000, store.RuntimeState.AccruedLiabilityCents, "Accrued liabilities travel with the business.");
            Assert.AreEqual(ownerCashBefore, context.PlayerPortfolio.OwnerCashCents, "The transfer itself must not move owner cash.");

            BusinessInstanceSaveDto dto = store.CaptureSaveDto();
            Assert.AreEqual(40000, dto.runtime.currentCashCents);
            Assert.AreEqual(12000, dto.runtime.accruedLiabilityCents);
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);
            Assert.NotNull(restored);
            Assert.AreEqual(store.InstanceId, restored.InstanceId);
            Assert.NotNull(restored.RuntimeState);
            Assert.AreEqual(40000, restored.RuntimeState.CurrentCashCents);
            Assert.AreEqual(12000, restored.RuntimeState.AccruedLiabilityCents);
        }

        [Test]
        public void AcquiredGeneralStoreIsBoundToStoreRuntimeAndSettlesWeekly()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            Assert.IsNull(context.StoreRuntime.CurrentBusiness, "No player store is bound before acquisition.");

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                message);

            Assert.AreSame(store, context.StoreRuntime.CurrentBusiness, "The acquired store must be bound to the store runtime by the transfer.");
            Assert.AreSame(store.RuntimeState, context.StoreRuntime.RuntimeState);
            Assert.AreEqual(store.RuntimeState.CurrentCashCents, context.StoreRuntime.CurrentCashCents);

            context.StoreRuntime.ResolveWeeklyPayrollAndReorder();
            Assert.IsFalse(
                context.StoreRuntime.Status.Contains("No player General Store has been formed yet"),
                "Weekly settlement must run for the acquired store instead of early-returning on a missing store.");
        }

        [Test]
        public void AcquiredStoreTransferIsRefusedWhenRuntimeIsBusy()
        {
            using TestWorld context = TestWorld.Create();
            context.CreatePlayerStoreForAccounting();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            BusinessInstanceState existingStore = context.StoreRuntime.CurrentBusiness;

            Assert.IsFalse(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                "The shared single-store runtime must refuse an acquisition it cannot operate.");

            Assert.AreEqual(BusinessOwnerKind.Npc, store.Owner.OwnerKind, "A refused transfer must leave the seller's business ownership unchanged.");
            Assert.AreSame(existingStore, context.StoreRuntime.CurrentBusiness, "The runtime keeps the store it already manages.");
            StringAssert.Contains("already managing", message, "A refused binding must be visible before ownership or payment moves.");
        }

        [Test]
        public void AcquiredStoreCanConfigureAdditionalLegitimateCapability()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                message);

            Assert.IsTrue(
                store.TryConfigureGenericRetailLine(new GenericRetailProductLine("general_goods", "storage", 10f, 20f, 5f, 100), 0.1f),
                "The acquired store must accept a new retail line through the ordinary capability system.");
            Assert.IsTrue(
                store.GenericConfiguration.Retail.ProductLines.Any(line => line != null && line.ProductId == "general_goods"),
                "The configured line must be recorded on the acquired business.");
            store.AddCapability("retail");
            Assert.IsTrue(store.HasCapability("retail"));
        }

        [Test]
        public void TransferCreatesNoPhantomBusiness()
        {
            using TestWorld context = TestWorld.Create();
            int countBefore = context.SharedBusinessRuntime.Businesses.Count;
            HashSet<string> idsBefore = new(context.SharedBusinessRuntime.Businesses
                .Where(business => business != null)
                .Select(business => business.InstanceId));
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                message);

            Assert.AreEqual(countBefore, context.SharedBusinessRuntime.Businesses.Count, "Transfer must not create or destroy a business.");
            Assert.AreEqual(1, context.SharedBusinessRuntime.Businesses.Count(business => business != null && business.InstanceId == store.InstanceId));
            Assert.AreSame(store, context.SharedBusinessRuntime.FindByType(BusinessType.GeneralStore));
            Assert.IsTrue(idsBefore.SetEquals(context.SharedBusinessRuntime.Businesses
                .Where(business => business != null)
                .Select(business => business.InstanceId)), "The business set must be identical before and after the sale.");
        }

        // UBC A2 item 1 (listing half): the store's listing must name the
        // seller's person id, so the proceeds path can credit the actual
        // seller instead of a display name no ledger can resolve.
        [Test]
        public void BusinessListingCarriesSellerPersonId()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            context.ExposeAllBusinessListingsForFocusedAssertion();

            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings
                .FirstOrDefault(candidate => candidate != null && candidate.businessInstanceId == store.InstanceId);
            Assert.NotNull(listing, BuildStoreListingDiagnostic(context, store));
            Assert.GreaterOrEqual(store.Owner.PersonId, 0, "The NPC store owner must have a person id.");
            Assert.AreEqual(store.Owner.PersonId, listing.sellerPersonId, "The listing must name the seller's person id.");
            Assert.AreEqual(store.Owner.DisplayName, listing.ownerDisplayName);
        }

        // UBC A2 items 1 and 3 (end to end, direct purchase): the asking price
        // and the seller's pre-close cash withdrawal both reach the seller's
        // household ledger, the business keeps only the opening float, and
        // the sale terms record the price, the withdrawal and the transition
        // outcome on the business record.
        [Test]
        public void DirectPurchaseCreditsSellerAndRecordsTerms()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            context.SetBusinessCash(store, 100000);
            HouseholdLedgerRegistry ledgers = context.RegisterSellerWithHousehold(store, 100);
            context.PlayerPortfolio.AddOwnerCash(10_000_000, "UBC A2 test working capital");
            context.ExposeAllBusinessListingsForFocusedAssertion();

            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings
                .FirstOrDefault(candidate => candidate != null && candidate.businessInstanceId == store.InstanceId);
            Assert.NotNull(listing, BuildStoreListingDiagnostic(context, store));
            int sellerPersonId = listing.sellerPersonId;
            int askingPrice = listing.askingPriceCents;
            Assert.Greater(askingPrice, 0, "The store must have a positive asking price.");
            int listingIndex = context.AcquisitionMarket.BusinessListings.ToList().IndexOf(listing);
            Assert.GreaterOrEqual(listingIndex, 0, "The store listing must be in the market's business listings.");
            SetSelectedBusinessIndex(context.AcquisitionMarket, listingIndex);

            Assert.IsTrue(InvokeTryPurchaseBusiness(context.AcquisitionMarket, out string message), message);
            StringAssert.Contains("paid to", message, "The close message must say the seller was paid.");

            int openingCash = store.RuntimeState.CurrentCashCents;
            int withdrawal = 100000 - openingCash;
            Assert.GreaterOrEqual(withdrawal, 0, "The business must keep no more than it held before the sale.");
            HouseholdLedger sellerLedger = ledgers.Get(100);
            Assert.NotNull(sellerLedger, "The seller's household must have a ledger after the sale.");
            Assert.AreEqual(
                askingPrice + withdrawal,
                sellerLedger.GetBalanceCents(),
                "Seller proceeds are the price plus the pre-close cash withdrawal; neither may vanish.");

            Assert.AreEqual(BusinessOwnerKind.Player, store.Owner.OwnerKind);
            Assert.AreEqual(sellerPersonId, store.SaleTerms.SellerPersonId);
            Assert.AreEqual(askingPrice, store.SaleTerms.PurchasePriceCents);
            Assert.AreEqual(withdrawal, store.SaleTerms.SellerCashWithdrawalCents);
            Assert.AreEqual(askingPrice + withdrawal, store.SaleTerms.SellerProceedsCents);
            Assert.AreEqual(openingCash, store.SaleTerms.IncludedCashCents);
            StringAssert.Contains("no transition arrangement was agreed", store.SaleTerms.TransitionArrangement);

            BusinessInstanceSaveDto dto = store.CaptureSaveDto();
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);
            Assert.NotNull(restored);
            Assert.AreEqual(askingPrice, restored.SaleTerms.PurchasePriceCents);
            Assert.AreEqual(withdrawal, restored.SaleTerms.SellerCashWithdrawalCents);
            Assert.AreEqual(sellerPersonId, restored.SaleTerms.SellerPersonId);
        }

        [Test]
        public void GeneralStoreIsNotOfferedWhenTheSingleRetailRuntimeIsAlreadyOccupied()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState npcStore = context.GetBusiness(BusinessType.GeneralStore);
            context.CreatePlayerStoreForAccounting();

            context.AcquisitionMarket.RebuildMarket();

            Assert.IsFalse(context.AcquisitionMarket.BusinessListings.Any(
                    candidate => candidate != null && candidate.businessInstanceId == npcStore.InstanceId),
                "A second General Store must not be offered while the shared retail runtime cannot bind it.");
            Assert.AreEqual(BusinessOwnerKind.Npc, npcStore.Owner.OwnerKind,
                "Listing eligibility must not transfer business ownership.");
        }

        // UBC A2 item 1 (fallback): when the seller has no household to
        // credit, the proceeds become a named Payable the player owes the
        // seller — a settleable debt, never a silent loss.
        [Test]
        public void SellerProceedsFallBackToNamedPayableWhenNoHousehold()
        {
            using TestWorld context = TestWorld.Create();
            var authority = new FinancialObligationAuthority();
            context.AcquisitionMarket.ObligationAuthority = authority;
            context.AcquisitionMarket.ObligationIdRegistry = new EntityIdRegistry();

            int delivered = context.AcquisitionMarket.CreditSellerProceeds(
                -1, "No Such Seller", 50000, "test_business", out string note);

            Assert.AreEqual(50000, delivered, "The fallback must deliver the full amount as a debt.");
            StringAssert.Contains("owed to", note);
            FinancialObligation payable = authority.Obligations.FirstOrDefault(o => o != null && o.Kind == FinancialObligationKind.Payable);
            Assert.NotNull(payable, "A Payable must be recorded when no household can be credited.");
            Assert.AreEqual("player", payable.Debtor);
            Assert.AreEqual("No Such Seller", payable.Creditor);
            Assert.AreEqual(50000, payable.OutstandingPrincipalCents);
        }

        // UBC A2 item 1 (fail-loud): when there is no household and no debt
        // authority either, the proceeds are NOT delivered and the note says
        // so plainly — the loss is visible, never silent.
        [Test]
        public void SellerProceedsFailLoudWhenNoHouseholdAndNoAuthority()
        {
            using TestWorld context = TestWorld.Create();

            LogAssert.Expect(LogType.Error,
                "[Acquisitions] SELLER PROCEEDS UNDELIVERED: $500.00 owed to Seller NineNineNine could not be credited (no seller household and no debt authority).");

            int delivered = context.AcquisitionMarket.CreditSellerProceeds(
                999, "Seller NineNineNine", 50000, "test_business", out string note);

            Assert.AreEqual(0, delivered, "Nothing is delivered when no household and no debt authority exist.");
            StringAssert.Contains("UNDELIVERED", note);
            StringAssert.Contains("$500.00", note, "The undelivered amount must be named.");
        }

        // UBC A2 item 2: the former owner is not silently kept on as an
        // employee. A staffed owner-operator slot is cleared at transfer,
        // while a genuine hired-worker slot is untouched.
        [Test]
        public void FormerOwnerLeavesEmploymentSetOnPlayerTransfer()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);
            store.EnsureOwnerOperatorStaffing(store.Owner);
            string ownerSlotId = $"owner:{store.Owner.PersonId}";
            Assert.IsTrue(
                store.RuntimeState.WorkerSlots.Any(slot => slot != null && slot.IsFilled && slot.AssignedWorkerId == ownerSlotId),
                "The fixture must staff the owner-operator slot before the transfer.");

            WorkerSlotState hiredSlot = store.RuntimeState.WorkerSlots.FirstOrDefault(slot => slot != null && !slot.IsFilled);
            Assert.NotNull(hiredSlot, "The store must have an open slot for a hired worker.");
            hiredSlot.Assign("worker:3", "Worker 03", 500);

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string message),
                message);

            Assert.IsFalse(
                store.RuntimeState.WorkerSlots.Any(slot => slot != null && slot.IsFilled && slot.AssignedWorkerId == ownerSlotId),
                "The former owner's operator slot must be cleared at closing; employment requires an agreement.");
            Assert.IsTrue(hiredSlot.IsFilled, "A genuine hired-worker slot must be untouched by the transfer.");
            Assert.AreEqual("worker:3", hiredSlot.AssignedWorkerId);
        }

        // UBC A2 item 4: selling a store to Town must unbind it from the
        // player store runtime — the reverse of the A1 bind.
        [Test]
        public void SaleToTownUnbindsStoreFromPlayerRuntime()
        {
            using TestWorld context = TestWorld.Create();
            BusinessInstanceState store = context.GetBusiness(BusinessType.GeneralStore);

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToPlayer(store.AssignedBuildingId, out _, out string transferMessage),
                transferMessage);
            Assert.AreSame(store, context.StoreRuntime.CurrentBusiness, "The store must be bound after the player acquires it.");

            Assert.IsTrue(
                context.SharedBusinessRuntime.TryTransferBusinessToTown(store.AssignedBuildingId, "Town", out BusinessInstanceState townBusiness, out string townMessage),
                townMessage);
            Assert.AreSame(store, townBusiness, "The sale to Town must move the same business object.");
            Assert.AreEqual(BusinessOwnerKind.Town, store.Owner.OwnerKind);
            Assert.IsNull(context.StoreRuntime.CurrentBusiness, "Selling to Town must unbind the store from the player store runtime.");
            Assert.IsNull(context.StoreRuntime.RuntimeState, "The runtime must hold no store state after the unbind.");
        }

        private static List<(string CategoryId, int Units)> SnapshotStock(BusinessInstanceState business)
        {
            List<(string, int)> snapshot = new();
            foreach (CategoryStockState stock in business.RuntimeState.CategoryStock)
            {
                if (stock == null || string.IsNullOrWhiteSpace(stock.CategoryId))
                {
                    continue;
                }

                snapshot.Add((stock.CategoryId, stock.CurrentStockUnits));
            }

            return snapshot;
        }

        private static string BuildStoreListingDiagnostic(TestWorld context, BusinessInstanceState store)
        {
            context.TownWorld.TryGetBuildingById(store.AssignedBuildingId, out PlacedBuilding building);
            return $"The store must be listed when the retail runtime is available. " +
                $"Owner={store.Owner?.OwnerKind}; businessBuilding={store.AssignedBuildingId}; " +
                $"buildingFound={building != null}; buildingPlayerOwned={building?.playerOwned}; " +
                $"storeBuildingId={context.StoreRuntime.StoreBuildingId}; " +
                $"runtimeCurrent={context.StoreRuntime.CurrentBusiness?.InstanceId ?? "<none>"}; " +
                $"assignmentValid={context.SharedBusinessRuntime.IsBusinessAssignmentValid(store)}; " +
                $"listings={context.AcquisitionMarket.BusinessListings.Count}.";
        }

        private static void SetAccruedLiability(BusinessInstanceState business, int cents)
        {
            FieldInfo field = typeof(BusinessRuntimeState).GetField("accruedLiabilityCents", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, "BusinessRuntimeState.accruedLiabilityCents should exist.");
            field.SetValue(business.RuntimeState, Mathf.Max(0, cents));
        }

        // UBC A2: TryPurchaseBusiness is the direct-purchase close (no staged
        // deal machine). It is private, so the test drives it by reflection,
        // the same pattern the file already uses for private fields.
        private static void SetSelectedBusinessIndex(AcquisitionMarketManager market, int index)
        {
            FieldInfo field = typeof(AcquisitionMarketManager).GetField("selectedBusinessIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, "AcquisitionMarketManager.selectedBusinessIndex should exist.");
            field.SetValue(market, index);
        }

        private static bool InvokeTryPurchaseBusiness(AcquisitionMarketManager market, out string message)
        {
            MethodInfo method = typeof(AcquisitionMarketManager).GetMethod("TryPurchaseBusiness", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, "AcquisitionMarketManager.TryPurchaseBusiness should exist.");
            object[] args = { null };
            bool result = (bool)method.Invoke(market, args);
            message = (string)args[0];
            return result;
        }

        private sealed class TestWorld : IDisposable
        {
            private readonly GameObject root;
            private readonly TownGenerationSettings settings;

            public TownWorldController TownWorld { get; }
            public PopulationManager PopulationManager { get; }
            public GeneralStoreRuntimeManager StoreRuntime { get; }
            public SharedBusinessRuntimeManager SharedBusinessRuntime { get; }
            public AcquisitionMarketManager AcquisitionMarket { get; }
            public PlayerPortfolioManager PlayerPortfolio { get; }

            private TestWorld(GameObject root, TownGenerationSettings settings)
            {
                this.root = root;
                this.settings = settings;
                TownWorld = root.AddComponent<TownWorldController>();
                PopulationManager = root.AddComponent<PopulationManager>();
                StoreRuntime = root.AddComponent<GeneralStoreRuntimeManager>();
                SharedBusinessRuntime = root.AddComponent<SharedBusinessRuntimeManager>();
                AcquisitionMarket = root.AddComponent<AcquisitionMarketManager>();
                PlayerPortfolio = root.AddComponent<PlayerPortfolioManager>();
            }

            public static TestWorld Create()
            {
                TownGenerationSettings sourceSettings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
                GeneralStoreBusinessDefinition storeDefinition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>("Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
                Assert.NotNull(sourceSettings);
                Assert.NotNull(storeDefinition);

                TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
                GameObject root = new("UBC A1 Acquisition Continuity Test World");
                TestWorld context = new(root, settings);
                context.TownWorld.Configure(settings, null, null);
                context.TownWorld.GenerateTownShell();
                context.SeedLaborPool(24);
                context.PlayerPortfolio.Configure(null, null);
                context.StoreRuntime.Configure(context.TownWorld, context.PopulationManager, null, storeDefinition, null, null, context.PlayerPortfolio);
                Assert.IsTrue(context.StoreRuntime.InitializeIfNeeded(), context.StoreRuntime.Status);
                context.SharedBusinessRuntime.Configure(context.TownWorld, null, context.PopulationManager, null, context.StoreRuntime, context.PlayerPortfolio);
                context.SharedBusinessRuntime.InitializeIfNeeded(context.StoreRuntime.CurrentBusiness);
                context.PlayerPortfolio.InitializeFromLegacyBusinessCash(context.StoreRuntime, context.SharedBusinessRuntime);
                context.PlayerPortfolio.AddOwnerCash(TestOwnerCashCents, "UBC A1 test working capital");
                context.AcquisitionMarket.Configure(context.TownWorld, context.StoreRuntime, context.SharedBusinessRuntime, context.PopulationManager, null, null, context.PlayerPortfolio);
                context.AcquisitionMarket.RebuildMarket();
                return context;
            }

            public void CreatePlayerStoreForAccounting()
            {
                Assert.IsTrue(
                    SharedBusinessRuntime.TryCreatePlayerBusiness(
                        BusinessType.GeneralStore,
                        "A1 Accounting Test Store",
                        out BusinessInstanceState business,
                        out string formationMessage),
                    formationMessage);
                Assert.IsTrue(StoreRuntime.TryBindFormedPlayerBusiness(business, out string bindMessage), bindMessage);
            }

            public void ExposeAllBusinessListingsForFocusedAssertion()
            {
                FieldInfo field = typeof(AcquisitionMarketManager).GetField("maxBusinessListings", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(field, "The market listing cap should remain configurable for deterministic focused tests.");
                field.SetValue(AcquisitionMarket, 100);
                AcquisitionMarket.RebuildMarket();
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            }

            public BusinessInstanceState GetBusiness(BusinessType businessType)
            {
                BusinessInstanceState business = SharedBusinessRuntime.FindByType(businessType);
                Assert.NotNull(business, $"Expected shared {businessType} business in the default town.");
                Assert.NotNull(business.RuntimeState, $"Expected shared {businessType} runtime state.");
                return business;
            }

            public void SetBusinessStock(BusinessInstanceState business, string categoryId, int units)
            {
                Assert.NotNull(business);
                CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
                Assert.NotNull(stock, $"{business.BusinessType} category {categoryId} should exist.");
                stock.SetCurrentStockForTests(units);
            }

            public void SetBusinessCash(BusinessInstanceState business, int cashCents)
            {
                Assert.NotNull(business);
                int target = Mathf.Max(0, cashCents);
                int current = business.RuntimeState.CurrentCashCents;
                if (current > target)
                {
                    business.RuntimeState.SpendCents(current - target);
                    return;
                }

                business.RuntimeState.AddCashCents(target - current);
            }

            public void SetTransferAgreements(params BusinessTransferAgreementState[] agreements)
            {
                FieldInfo field = typeof(SharedBusinessRuntimeManager).GetField("transferAgreements", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(field, "SharedBusinessRuntimeManager.transferAgreements should exist.");
                field.SetValue(SharedBusinessRuntime, new List<BusinessTransferAgreementState>(agreements));
            }

            // UBC A2: gives the seller a household the proceeds path can
            // credit, and injects a fresh household ledger registry on the
            // market manager (production gets it from the hub). The fixture's
            // SeedLaborPool clears the population, so the seller's person
            // record is added here when it is missing; when the seller's
            // person id collides with a labor-pool placeholder, that record
            // is the one given the household — what the test pins is that the
            // proceeds reach the household the seller resolves to.
            public HouseholdLedgerRegistry RegisterSellerWithHousehold(BusinessInstanceState business, int householdId)
            {
                Assert.NotNull(business);
                Assert.NotNull(business.Owner);
                Assert.GreaterOrEqual(business.Owner.PersonId, 0, "The seller must have a person id.");
                PersonState seller = PopulationManager.State.GetPerson(business.Owner.PersonId);
                if (seller == null)
                {
                    seller = new PersonState
                    {
                        id = business.Owner.PersonId,
                        firstName = "Seller",
                        lastName = business.Owner.Surname ?? string.Empty,
                        age = 35,
                        ageBand = AgeBand.Adult18Plus,
                        laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                        householdId = householdId,
                        professionId = string.Empty,
                        professionName = "Business owner",
                        homeBuildingId = -1,
                        workplaceBuildingId = business.AssignedBuildingId
                    };
                    PopulationManager.State.people.Add(seller);
                }
                else
                {
                    seller.householdId = householdId;
                }

                var ledgers = new HouseholdLedgerRegistry();
                AcquisitionMarket.HouseholdLedgers = ledgers;
                return ledgers;
            }

            public void SeedLaborPool(int count)
            {
                PopulationManager.State.people.Clear();
                for (int i = 0; i < count; i++)
                {
                    PopulationManager.State.people.Add(new PersonState
                    {
                        id = i,
                        firstName = "Worker",
                        lastName = i.ToString("00"),
                        age = 22,
                        ageBand = AgeBand.Adult18Plus,
                        laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                        householdId = -1,
                        professionId = string.Empty,
                        professionName = "No assigned job",
                        homeBuildingId = -1,
                        workplaceBuildingId = -1
                    });
                }
            }
        }
    }
}
