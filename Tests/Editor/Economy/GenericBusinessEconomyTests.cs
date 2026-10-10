using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Economy.Blacksmith;
using NUnit.Framework;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Skills;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class GenericBusinessEconomyTests
    {
        [Test]
        public void ClasslessBusinessIntent_CanExistBeforeOperationOrPremises()
        {
            var intent = new CreateBusinessIntent(
                BusinessType.Generic,
                "Unconfigured Enterprise",
                BusinessOwnership.Sole(BusinessOwnerIdentity.Player()));

            Assert.IsEmpty(intent.Validate());
            PremisesResolution resolution = PremisesResolver.Resolve(
                intent.PremisesRequirement, intent.PremisesPreference, -1, new List<string>());
            Assert.AreEqual(PremisesKind.NoDedicatedPremises, resolution.Kind);
        }

        [Test]
        public void ClasslessBusiness_CanBeCreatedBeforeConfiguration()
        {
            var authority = new BusinessCreationAuthority();
            var context = new CreationContext();
            var intent = new CreateBusinessIntent(
                BusinessType.Generic,
                "Unconfigured Enterprise",
                BusinessOwnership.Sole(BusinessOwnerIdentity.Player()));

            Assert.IsTrue(authority.TryCreate(intent, context, out BusinessCreationResult result),
                string.Join("; ", result.Diagnostics));
            Assert.IsNotNull(result.Business);
            Assert.AreEqual(BusinessType.Generic, result.Business.BusinessType);
            Assert.AreEqual(PremisesKind.NoDedicatedPremises, result.Premises.Kind);
        }

        private sealed class CreationContext : IBusinessCreationContext
        {
            public EntityIdRegistry IdRegistry { get; } = new EntityIdRegistry();
            public bool TryGetProfile(BusinessType type, out BusinessProfileDefinition profile)
            {
                profile = null;
                return false;
            }

            public BusinessProfileDefinition BuildFallbackProfile(BusinessType type, string displayName) =>
                BusinessProfileDefinition.CreateFallback(type, displayName);

            public bool TryAssignPremises(PremisesKind kind, out int buildingId)
            {
                buildingId = -1;
                return false;
            }

            public void LogDiagnostic(string message) { }
        }

        [Test]
        public void ProductCatalogAndInventory_KeepUnitsAndOptionalLotsSeparate()
        {
            var catalog = new GenericProductCatalog();
            var nails = new GenericProductDefinition("nails-8d", "8d Cut Nails", ProductQuantityUnit.Pound);
            Assert.IsTrue(catalog.Register(nails));
            var position = new GenericInventoryPosition(nails.ProductId);
            position.Add(75f, 12, nails, "smithy", 1);
            Assert.AreEqual(75f, position.Quantity);
            Assert.IsEmpty(position.Lots);
        }

        [Test]
        public void PerishableInventory_RecordsLotsWithoutMakingHomogeneousGoodsHeavyweight()
        {
            var bread = new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each, perishable: true);
            var position = new GenericInventoryPosition(bread.ProductId);
            position.Add(12f, 25, bread, "bakery-batch", 4);
            Assert.AreEqual(12f, position.Quantity);
            Assert.AreEqual(1, position.Lots.Count);
        }

        [Test]
        public void RetailCapacity_SaleReleasesOccupiedButKeepsAllocation()
        {
            var pool = new RetailCapacityPool("display", 100f);
            Assert.IsTrue(pool.TryOccupy(40f));
            pool.Release(10f);
            Assert.AreEqual(100f, pool.Allocated);
            Assert.AreEqual(30f, pool.Occupied);
            Assert.AreEqual(70f, pool.Available);
        }

        [Test]
        public void SupplierOrder_CanMeetMinimumDollarValueAcrossMultipleLines()
        {
            var order = new SupplierPurchaseOrder
            {
                MinimumOrderValueCents = 10000,
                Lines = new List<SupplierOrderLine>
                {
                    new SupplierOrderLine { ProductId = "coffee", Quantity = 5, UnitPriceCents = 700 },
                    new SupplierOrderLine { ProductId = "cloth", Quantity = 5, UnitPriceCents = 1300 },
                }
            };
            Assert.AreEqual(10000, order.MerchandiseSubtotalCents);
            Assert.IsTrue(order.MeetsMinimums);
        }

        [Test]
        public void SupplierOrder_BelowMinimumValueDoesNotValidate()
        {
            var order = new SupplierPurchaseOrder
            {
                MinimumOrderValueCents = 10000,
                Lines = new List<SupplierOrderLine>
                {
                    new SupplierOrderLine { ProductId = "coffee", Quantity = 5, UnitPriceCents = 700 },
                }
            };
            Assert.IsFalse(order.MeetsMinimums);
        }

        [Test]
        public void PassiveProductionPhase_ReleasesPersonButRetainsEquipmentReservation()
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = "bread",
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "load", ElapsedMinutes = 1, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "oven" } },
                    new ProductionPhaseDefinition { PhaseId = "bake", ElapsedMinutes = 28, RequiresPerson = false, ReservedEquipmentIds = new List<string> { "oven" } },
                    new ProductionPhaseDefinition { PhaseId = "remove", ElapsedMinutes = 1, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "oven" } },
                }
            };
            var process = new ProductionProcessState();
            process.Start(method);
            Assert.IsTrue(process.PersonRequiredNow(method));
            Assert.IsFalse(process.Advance(method, 1));
            Assert.IsFalse(process.PersonRequiredNow(method));
            Assert.IsFalse(process.Advance(method, 28));
            Assert.IsTrue(process.PersonRequiredNow(method));
            Assert.IsTrue(process.Advance(method, 1));
            Assert.IsEmpty(process.ReservedEquipmentIds);
        }

        [Test]
        public void ProductionAuthority_OutputIsCreatedAtMostOnce()
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = "simple",
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "work", ElapsedMinutes = 1, RequiresPerson = true },
                }
            };
            Assert.IsTrue(GenericProductionAuthority.TryBegin(method, "proc-1", "person-1",
                id => true, id => true, id => true, id => true, out ProductionProcessState process, out string reason), reason);
            process.Advance(method, 1);
            int outputs = 0;
            Assert.IsTrue(GenericProductionAuthority.TryCreateOutputOnce(process, () => { outputs++; return true; }));
            Assert.IsFalse(GenericProductionAuthority.TryCreateOutputOnce(process, () => { outputs++; return true; }));
            Assert.AreEqual(1, outputs);
        }

        [Test]
        public void GenericConfiguration_SaveLoadPreservesRetailAndProcessState()
        {
            var configuration = new GenericBusinessConfiguration();
            configuration.Retail.GetOrCreatePool("display", 100f);
            configuration.Retail.AddOrReplaceLine(new GenericRetailProductLine("bread", "display", 10f, 20f, 5f, 35));
            configuration.AddEquipmentInstallation(new GenericEquipmentInstallationState
            {
                AssetId = "oven-1", BusinessId = "biz-1", RequiresSetup = true, Installed = false
            });
            var method = new ProductionMethodDefinition
            {
                MethodId = "bread", OutputProductId = "bread",
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "load", ElapsedMinutes = 1, RequiresPerson = true },
                }
            };
            Assert.IsTrue(GenericProductionAuthority.TryBegin(method, "process-1", "person-1",
                id => true, id => true, id => true, id => true, out ProductionProcessState process, out _));
            configuration.AddProcess(process);
            configuration.AddOrReplaceProductionPolicy(GenericVerticalCatalog.StockPolicy("bread", 10f, 20f));

            GenericBusinessConfiguration restored = GenericBusinessConfiguration.FromSaveDto(configuration.CaptureSaveDto());
            Assert.IsTrue(restored.Retail.TryGetLine("bread", out _));
            Assert.AreEqual(1, restored.EquipmentInstallations.Count);
            Assert.AreEqual(1, restored.ActiveProcesses.Count);
            Assert.IsTrue(restored.TryGetProductionPolicy("bread", out GenericProductionPolicy policy));
            Assert.AreEqual(20f, policy.MaximumStock);
        }

        [Test]
        public void EquipmentInstallation_RequiresSetupBeforeOperation()
        {
            var installation = new GenericEquipmentInstallationState
            {
                AssetId = "oven-1",
                BusinessId = "bakery-1",
                RequiresSetup = true,
                Installed = false
            };
            Assert.IsFalse(installation.IsOperational);
            Assert.IsTrue(installation.CompleteSetup(7));
            Assert.IsTrue(installation.IsOperational);
            Assert.IsFalse(installation.CompleteSetup(8));
        }

        [Test]
        public void EquipmentProcurement_UsesOrderShipmentReceiptAndInstallationStages()
        {
            var state = new EquipmentProcurementState
            {
                AcquisitionId = "oven-acq-1",
                SupplierId = "dealer-1",
                Order = new SupplierPurchaseOrder
                {
                    OrderId = "po-1", SupplierId = "dealer-1", MinimumOrderValueCents = 1000,
                    Lines = new List<SupplierOrderLine>
                    {
                        new SupplierOrderLine { ProductId = "oven", Quantity = 1, UnitPriceCents = 2500 }
                    }
                },
                Installation = new GenericEquipmentInstallationState { RequiresSetup = true }
            };
            Assert.IsTrue(state.MarkOrdered());
            Assert.IsTrue(state.MarkPaidOrFinanced());
            Assert.IsTrue(state.MarkShipped("shipment-1"));
            Assert.IsTrue(state.Receive(new EquipmentAsset { AssetId = "oven-1", Kind = "oven", Condition01 = 1f }, "biz-1", "shop-1"));
            Assert.IsTrue(state.Install(3));
            Assert.AreEqual(EquipmentProcurementStage.Installed, state.Stage);
            Assert.AreEqual("oven-1", state.AssetId);
            Assert.IsTrue(state.Installation.IsOperational);
        }

        [Test]
        public void EquipmentProcurement_RegistersReceivedAssetWithBusinessAuthority()
        {
            var state = new EquipmentProcurementState
            {
                AcquisitionId = "oven-acq-registration",
                Order = new SupplierPurchaseOrder
                {
                    OrderId = "oven-po-registration", SupplierId = "dealer-registration", MinimumOrderValueCents = 1,
                    Lines = new List<SupplierOrderLine>
                    {
                        new SupplierOrderLine { ProductId = "oven", Quantity = 1, UnitPriceCents = 2500 }
                    }
                },
                Installation = new GenericEquipmentInstallationState { RequiresSetup = true },
            };
            var configuration = new GenericBusinessConfiguration();
            Assert.IsTrue(GenericEquipmentProcurementAuthority.TryAdvanceToOperational(
                state,
                order => order.MeetsMinimums,
                _ => true,
                _ => true,
                _ => new EquipmentAsset { AssetId = "oven-registered", Kind = "oven", Condition01 = 1f },
                "bakery-registration", "shop-registration", 4, out string reason,
                asset => configuration.RegisterReceivedEquipment(asset, state.Installation)), reason);
            Assert.Contains("oven-registered", new List<string>(configuration.EquipmentAssetIds));
            Assert.IsTrue(configuration.EquipmentInstallations[0].IsOperational);
        }

        [Test]
        public void MidBakeSaveLoad_PreservesPassivePhaseWorkerReleaseAndSingleOutput()
        {
            ProductionMethodDefinition method = GenericVerticalCatalog.Bread();
            var configuration = new GenericBusinessConfiguration();
            configuration.AddEquipmentReference("oven");
            configuration.AddWorkspaceReference("oven-space");
            configuration.AddInventory("flour", 2f, 10);
            configuration.AddInventory("fuel", 1f, 5);

            var process = new ProductionProcessState { ProcessId = "mid-bake-save" , AssignedPersonId = "P701" };
            process.Start(method);
            Assert.IsTrue(process.ConsumeInputsOnce(method, input => configuration.TryConsumeInventory(input.ProductId, input.Quantity, out _)));
            Assert.IsFalse(process.Advance(method, 1));
            Assert.AreEqual(1, process.CurrentPhaseIndex);
            Assert.AreEqual(28, process.RemainingPhaseMinutes);
            Assert.IsFalse(process.PersonRequiredNow(method));
            Assert.Contains("oven", process.ReservedEquipmentIds);

            GenericBusinessConfiguration restored = GenericBusinessConfiguration.FromSaveDto(configuration.CaptureSaveDto());
            restored.AddProcess(process);
            GenericBusinessConfiguration reloaded = GenericBusinessConfiguration.FromSaveDto(restored.CaptureSaveDto());
            ProductionProcessState restoredProcess = reloaded.ActiveProcesses[0];
            Assert.AreEqual(1, restoredProcess.CurrentPhaseIndex);
            Assert.AreEqual(28, restoredProcess.RemainingPhaseMinutes);
            Assert.IsFalse(restoredProcess.PersonRequiredNow(method));
            Assert.Contains("oven", restoredProcess.ReservedEquipmentIds);
            Assert.AreEqual(1f, reloaded.GetInventoryQuantity("flour"), 0.001f);
            Assert.AreEqual(0f, reloaded.GetInventoryQuantity("fuel"), 0.001f);

            Assert.IsFalse(restoredProcess.Advance(method, 28));
            Assert.IsTrue(restoredProcess.PersonRequiredNow(method));
            Assert.IsTrue(restoredProcess.Advance(method, 1));
            int outputs = 0;
            Assert.IsTrue(GenericProductionAuthority.TryCreateOutputOnce(restoredProcess, () =>
            {
                outputs++;
                reloaded.AddInventory("bread", 1f, 25);
                return true;
            }));
            Assert.IsFalse(GenericProductionAuthority.TryCreateOutputOnce(restoredProcess, () =>
            {
                outputs++;
                return true;
            }));
            Assert.AreEqual(1, outputs);
            Assert.AreEqual(1f, reloaded.GetInventoryQuantity("bread"));
        }

        [Test]
        public void SetupTemplate_OnlySuggestsPolicyAndNeverCreatesEquipment()
        {
            var template = new GenericBusinessSetupTemplate
            {
                TemplateId = "bakery",
                SuggestedEquipmentKinds = new List<string> { "oven" },
                SuggestedProductIds = new List<string> { "bread" }
            };
            var configuration = new GenericBusinessConfiguration();
            template.ApplyPolicyOnly(configuration);
            Assert.AreEqual(1, configuration.Retail.ProductLines.Count);
            Assert.IsEmpty(configuration.EquipmentAssetIds);
        }

        [Test]
        public void GenericRetail_UsesRealBuyerAndActingPersonAndRollsBackOnSettlementFailure()
        {
            var business = new BusinessInstanceStateTestFactory().CreateGeneric();
            business.TryConfigureGenericRetailLine(new GenericRetailProductLine("flour", "storage", 5f, 10f, 2f, 50), 10f);
            business.RuntimeState.EnsureCategoryStock("flour", 5, 10);
            business.GenericConfiguration.Retail.TryOccupyProductSpace("flour", 5f);
            var context = new GenericRetailSaleContext
            {
                BuyerPrincipal = "household:7",
                ActingPersonId = 42,
                SellerBusinessId = business.InstanceId,
                ProductId = "flour",
                Quantity = 2,
                UnitPriceCents = 50,
                LocationId = "shop-1"
            };
            Assert.IsFalse(GenericRetailSaleAuthority.TryExecuteSale(business, context, _ => false, out _));
            Assert.AreEqual(5, business.RuntimeState.GetCategoryStock("flour").CurrentStockUnits);
            Assert.IsTrue(GenericRetailSaleAuthority.TryExecuteSale(business, context, sale =>
                sale.BuyerPrincipal == "household:7" && sale.ActingPersonId == 42, out _));
            Assert.AreEqual(3, business.RuntimeState.GetCategoryStock("flour").CurrentStockUnits);
        }

        [Test]
        public void GenericRetail_SellsOwnProducedInventoryWithoutSecondStockAuthority()
        {
            BusinessInstanceState business = new BusinessInstanceStateTestFactory().CreateGeneric();
            business.TryConfigureGenericRetailLine(new GenericRetailProductLine("hinges", "display", 4f, 8f, 2f, 125), 8f);
            business.GenericConfiguration.AddInventory("hinges", 4f, 80,
                new GenericProductDefinition("hinges", "Hinges", ProductQuantityUnit.Each),
                "production:hinges", 1);
            var context = new GenericRetailSaleContext
            {
                BuyerPrincipal = "household:22",
                ActingPersonId = 31,
                SellerBusinessId = business.InstanceId,
                ProductId = "hinges",
                Quantity = 2,
                UnitPriceCents = 125,
                LocationId = "building:22"
            };
            Assert.IsTrue(GenericRetailSaleAuthority.TryExecuteSale(business, context, _ => true, out string reason), reason);
            Assert.AreEqual(2f, business.GenericConfiguration.GetInventoryQuantity("hinges"));
            Assert.IsNull(business.RuntimeState.GetCategoryStock("hinges"));
        }

        [Test]
        public void SpecialOrder_ReusesSupplierOrderShipmentReceiptAndRetailTransaction()
        {
            var special = new CustomerSpecialOrderState
            {
                OrderId = "special-1",
                BuyerPrincipal = "household:9",
                ActingPersonId = 11,
                SellerBusinessId = "merchant-1"
            };
            Assert.IsTrue(special.AcceptQuote(new SupplierOffer
            {
                OfferId = "quote-1", SupplierId = "jobber-1",
                Lines = new List<SupplierOfferLine> { new SupplierOfferLine { ProductId = "coffee", AvailableQuantity = 10 } }
            }));
            Assert.IsTrue(special.PlaceOrder(new SupplierPurchaseOrder
            {
                OrderId = "po-1", SupplierId = "jobber-1", MinimumOrderValueCents = 100,
                Lines = new List<SupplierOrderLine> { new SupplierOrderLine { ProductId = "coffee", Quantity = 2, UnitPriceCents = 75 } }
            }));
            Assert.IsTrue(special.Dispatch("shipment-1"));
            Assert.IsTrue(special.Receive());
            Assert.IsTrue(special.Collect(context => context.BuyerPrincipal == "household:9" && context.ActingPersonId == 11));
            Assert.AreEqual(CustomerSpecialOrderStage.Collected, special.Stage);
        }

        [Test]
        public void SpecialOrder_ReceivesIntoReservedGenericInventoryBeforeCollection()
        {
            BusinessInstanceState business = new BusinessInstanceStateTestFactory().CreateGeneric();
            var line = new GenericRetailProductLine("coffee", "storage", 0f, 10f, 0f, 125);
            line.SetSpecialOrderAllowed(true);
            Assert.IsTrue(business.TryConfigureGenericRetailLine(line, 10f));
            var special = new CustomerSpecialOrderState
            {
                OrderId = "special-generic-1",
                BuyerPrincipal = "household:12",
                ActingPersonId = 21,
                SellerBusinessId = business.InstanceId
            };
            Assert.IsTrue(special.AcceptQuote(new SupplierOffer
            {
                OfferId = "quote-generic-1", SupplierId = "jobber-1",
                Lines = new List<SupplierOfferLine> { new SupplierOfferLine { ProductId = "coffee", AvailableQuantity = 10f, PackageQuantity = 1f } }
            }));
            Assert.IsTrue(special.PlaceOrder(new SupplierPurchaseOrder
            {
                OrderId = "po-generic-1", SupplierId = "jobber-1",
                Lines = new List<SupplierOrderLine> { new SupplierOrderLine { ProductId = "coffee", Quantity = 2f, PackageQuantity = 1f, UnitPriceCents = 75 } }
            }));
            Assert.IsTrue(special.Dispatch("shipment-generic-1"));
            Assert.IsTrue(special.ReceiveIntoBusiness(business, 80, new GenericProductDefinition("coffee", "Coffee", ProductQuantityUnit.Pound), 2));
            Assert.AreEqual(2f, business.GenericConfiguration.GetInventoryQuantity("coffee"));
            Assert.AreEqual(0f, business.GenericConfiguration.GetAvailableInventoryQuantity("coffee"));
            Assert.IsTrue(special.CollectFromBusiness(business, 125, _ => true, out string reason), reason);
            Assert.AreEqual(CustomerSpecialOrderStage.Collected, special.Stage);
            Assert.AreEqual(0f, business.GenericConfiguration.GetInventoryQuantity("coffee"));
        }

        [Test]
        public void MixedBusiness_ValuationUsesOneIdentityAndDoesNotAddAssetFloorTwice()
        {
            var business = new BusinessInstanceStateTestFactory().CreateGeneric();
            business.TryConfigureGenericRetailLine(
                new GenericRetailProductLine("hinges", "display", 12f, 20f, 6f, 125), 20f);
            business.RegisterGenericProductionMethod(new ProductionMethodDefinition
            {
                MethodId = "hinge-production", OutputProductId = "hinges", OutputQuantity = 12f
            });
            business.AddCapability("repair-service");

            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness(business.InstanceId, "player", true);
            valuation.RecordWeeklyProfit(business.InstanceId, 10000);
            valuation.RecordWeeklyProfit(business.InstanceId, 10000);
            valuation.RecordWeeklyProfit(business.InstanceId, 10000);
            valuation.RecordTransferableAssets(business.InstanceId, 50000);
            valuation.RecordLiabilities(business.InstanceId, 5000);
            EnterpriseValuationResult result = valuation.GetValuation(business.InstanceId);

            // 30,000c weekly × 52 × 3 = 1,560,000c earnings value, so the
            // 50,000c asset floor is not added a second time.
            Assert.AreEqual(1_555_000, result.OwnerEquityValueCents);
            Assert.AreEqual(1, valuation.TrackedBusinessCount);
        }

        private sealed class BusinessInstanceStateTestFactory
        {
            public BusinessInstanceState CreateGeneric()
            {
                var context = new CreationContext();
                var authority = new BusinessCreationAuthority();
                var intent = new CreateBusinessIntent(
                    BusinessType.Generic, "Mixed Enterprise",
                    BusinessOwnership.Sole(BusinessOwnerIdentity.Player()));
                Assert.IsTrue(authority.TryCreate(intent, context, out BusinessCreationResult result));
                return result.Business;
            }
        }

        [Test]
        public void PhysicalEvaluator_DoesNotConsultOccupationOrCapabilityRegistry()
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = "hinge",
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "iron", Quantity = 1f },
                },
                RequiredEquipmentIds = new List<string> { "forge" },
                RequiredWorkspaceIds = new List<string> { "smithy" },
            };
            string reason = PhysicalActivityEvaluator.ExplainProductionPossibility(
                method,
                id => id == "forge",
                id => id == "smithy",
                id => id == "iron",
                id => true);
            Assert.IsEmpty(reason);
        }

        [Test]
        public void ProductionPolicy_UsesTargetAsReplenishmentGoalAndMaximumAsCeiling()
        {
            var policy = new GenericProductionPolicy
            {
                MethodId = "nails",
                TargetStock = 75f,
                MaximumStock = 100f,
                BatchSize = 100f,
            };
            var method = new ProductionMethodDefinition { MethodId = "nails", OutputQuantity = 10f };
            Assert.IsFalse(GenericProductionPlanner.Plan(policy, method, 80f, 0f, null, null).IsEligible);
            GenericProductionPlan plan = GenericProductionPlanner.Plan(policy, method, 60f, 0f, null, null);
            Assert.IsTrue(plan.IsEligible);
            Assert.AreEqual(15f, plan.Quantity);
        }

        [Test]
        public void ProductionPolicy_RespectsProtectedInputReserve()
        {
            var policy = new GenericProductionPolicy
            {
                MethodId = "nails", TargetStock = 75f, BatchSize = 10f,
                MinimumInputReserve = 40f,
            };
            var method = new ProductionMethodDefinition
            {
                MethodId = "nails",
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "iron", Quantity = 1f },
                },
            };
            GenericProductionPlan blocked = GenericProductionPlanner.Plan(policy, method, 0f, 0f,
                _ => 45f, _ => 0f);
            Assert.IsFalse(blocked.IsEligible);
            GenericProductionPlan allowed = GenericProductionPlanner.Plan(policy, method, 0f, 0f,
                _ => 60f, _ => 0f);
            Assert.IsTrue(allowed.IsEligible);
        }

        [Test]
        public void GenericPolicyExecutor_UsesBusinessInventoryAndReservesEquipment()
        {
            BusinessInstanceState business = new BusinessInstanceStateTestFactory().CreateGeneric();
            GenericBusinessVerticalService.ConfigureBakery(business);
            business.RegisterGenericEquipmentAsset("oven");
            business.RegisterGenericWorkspace("preparation");
            business.RegisterGenericWorkspace("oven-space");
            business.GenericConfiguration.AddInventory("flour", 2f);
            business.GenericConfiguration.AddInventory("fuel", 2f);

            var authority = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            EntityId person = EntityId.For(EntityKind.Person, 900);
            var runtime = new GenericProductionRuntime(authority, budgets,
                EntityId.For(EntityKind.Business, 900), 1);
            Assert.IsTrue(GenericProductionPolicyExecutor.TryStart(
                business, runtime, "generic-bread", person,
                new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each), 1,
                out ProductionProcessState process, out string reason), reason);
            Assert.IsFalse(process.Completed);

            Assert.IsFalse(GenericProductionPolicyExecutor.TryStart(
                business, runtime, "generic-bread", person,
                new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each), 1,
                out _, out reason));
            StringAssert.Contains("equipment", reason.ToLowerInvariant());

            var bread = new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each, perishable: true);
            Assert.IsTrue(GenericProductionPolicyExecutor.Advance(business, runtime, process, person, 1,
                bread, 25, "bakery-process", 1, out reason), reason);
            Assert.IsTrue(GenericProductionPolicyExecutor.Advance(business, runtime, process, person, 28,
                bread, 25, "bakery-process", 1, out reason), reason);
            Assert.IsTrue(GenericProductionPolicyExecutor.Advance(business, runtime, process, person, 1,
                bread, 25, "bakery-process", 1, out reason), reason);
            Assert.AreEqual(1f, business.GenericConfiguration.GetInventoryQuantity("bread"));
        }

        [Test]
        public void ProductionTaskBridge_ReleasesWorkerDuringBakeAndRequestsRemovalTask()
        {
            var authority = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            EntityId business = EntityId.For(EntityKind.Business, 1);
            EntityId person = EntityId.For(EntityKind.Person, 1);
            var bridge = new GenericProductionTaskBridge(authority, budgets, business, 1);
            var method = new ProductionMethodDefinition
            {
                MethodId = "bread",
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "load", ElapsedMinutes = 1, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "oven" } },
                    new ProductionPhaseDefinition { PhaseId = "bake", ElapsedMinutes = 28, RequiresPerson = false, ReservedEquipmentIds = new List<string> { "oven" } },
                    new ProductionPhaseDefinition { PhaseId = "remove", ElapsedMinutes = 1, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "oven" } },
                },
            };
            Assert.IsTrue(GenericProductionAuthority.TryBegin(method, "bread-1", person.ToString(), _ => true, _ => true, _ => true, _ => true,
                out ProductionProcessState process, out string reason), reason);
            Assert.IsTrue(bridge.StartCurrentPhase(process, method, person, out reason), reason);
            Assert.IsTrue(bridge.CompleteCurrentPhase(process, method, person, out reason), reason);
            Assert.IsFalse(process.PersonRequiredNow(method));
            var cleanup = new TaskDefinition("cleanup-during-bake", "Clean the workbench", 5);
            Assert.IsTrue(authority.RegisterDefinition(cleanup, out reason), reason);
            WorkTask cleanupTask = authority.CreateTask(cleanup.DefinitionId, business, 1);
            Assert.IsTrue(authority.AssignTask(cleanupTask.TaskId, person, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(cleanupTask.TaskId, 1, out reason), reason);
            Assert.IsFalse(bridge.AdvancePassiveTime(process, method, 27));
            Assert.IsFalse(process.PersonRequiredNow(method));
            Assert.IsTrue(bridge.AdvancePassiveTime(process, method, 1));
            Assert.IsTrue(process.PersonRequiredNow(method));
            WorkTask removal = bridge.EnsureCurrentPhaseTask(process, method, person, out reason);
            Assert.IsNotNull(removal, reason);
            Assert.AreEqual(TaskStatus.Assigned, removal.Status);
        }

        [Test]
        public void ProductionPolicyScheduler_StartsEligibleWorkThroughSharedRuntime()
        {
            BusinessInstanceState business = new BusinessInstanceStateTestFactory().CreateGeneric();
            var method = new ProductionMethodDefinition
            {
                MethodId = "policy-hinge",
                OutputProductId = "hinges",
                OutputQuantity = 1f,
                RequiredEquipmentIds = new List<string> { "forge" },
                RequiredWorkspaceIds = new List<string> { "smithy" },
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition
                    {
                        PhaseId = "forge", ElapsedMinutes = 10, RequiresPerson = true,
                        ReservedEquipmentIds = new List<string> { "forge" },
                        ReservedWorkspaceIds = new List<string> { "smithy" },
                    },
                },
            };
            business.RegisterGenericProductionMethod(method);
            business.RegisterGenericProductionPolicy(new GenericProductionPolicy
            {
                MethodId = method.MethodId, Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 1f, MaximumStock = 2f, BatchSize = 1f, Priority = 10,
            });
            business.RegisterGenericEquipmentAsset("forge");
            business.RegisterGenericWorkspace("smithy");
            var tasks = new TaskAuthority();
            var registry = new GenericProductionRuntimeRegistry(tasks, new WorkTimeBudgetStore());
            EntityId worker = EntityId.For(EntityKind.Person, 812);
            Assert.IsTrue(registry.TryStartEligiblePolicy(
                business, EntityId.For(EntityKind.Business, 812), 1, new[] { worker },
                out ProductionProcessState process, out string reason), reason);
            Assert.IsNotNull(process);
            Assert.AreEqual(worker.ToString(), process.AssignedPersonId);
            Assert.AreEqual(1, business.GenericConfiguration.ActiveProcesses.Count);
            Assert.Contains("forge", process.ReservedEquipmentIds);
        }

        [Test]
        public void VerticalCatalog_ProvidesPhysicalBakeryAndPrairieForkMethodsWithoutPermissionFlags()
        {
            ProductionMethodDefinition bread = GenericVerticalCatalog.Bread();
            Assert.AreEqual(3, bread.Phases.Count);
            Assert.IsFalse(bread.Phases[1].RequiresPerson);
            Assert.AreEqual(28, bread.Phases[1].ElapsedMinutes);
            Assert.AreEqual(9, GenericVerticalCatalog.PrairieForkSmithing().Count);
            Assert.IsEmpty(bread.RequiredWorkspaceIds.FindAll(id => id.Contains("occupation")));
        }

        [Test]
        public void ProductionProcess_ConsumesInputsExactlyOnce()
        {
            var method = GenericVerticalCatalog.Bread();
            var process = new ProductionProcessState { ProcessId = "bread-process" };
            process.Start(method);
            int consumed = 0;
            Assert.IsTrue(process.ConsumeInputsOnce(method, input => { consumed++; return true; }));
            Assert.AreEqual(2, consumed);
            Assert.IsFalse(process.ConsumeInputsOnce(method, input => { consumed++; return true; }));
            Assert.AreEqual(2, consumed);
        }

        [Test]
        public void EquipmentProcurementAuthority_RequiresRealCallbacksBeforeOperationalState()
        {
            var state = new EquipmentProcurementState
            {
                AcquisitionId = "oven-acq-2",
                Order = new SupplierPurchaseOrder
                {
                    OrderId = "oven-po-2", SupplierId = "dealer-2", MinimumOrderValueCents = 1,
                    Lines = new List<SupplierOrderLine> { new SupplierOrderLine { ProductId = "oven", Quantity = 1, UnitPriceCents = 100 } },
                },
                Installation = new GenericEquipmentInstallationState { RequiresSetup = true },
            };
            Assert.IsTrue(GenericEquipmentProcurementAuthority.TryAdvanceToOperational(
                state,
                order => order.MeetsMinimums,
                order => true,
                shipmentId => shipmentId == "oven-po-2:shipment",
                shipmentId => new EquipmentAsset { AssetId = "oven-2", Kind = "oven", Condition01 = 1f },
                "business-2", "bakery-2", 9, out string reason), reason);
            Assert.AreEqual(EquipmentProcurementStage.Installed, state.Stage);
            Assert.IsTrue(state.Installation.IsOperational);
            Assert.IsTrue(GenericEquipmentProcurementAuthority.TryAdvanceToOperational(
                state, _ => { Assert.Fail("installed equipment must not place a second order"); return false; },
                _ => { Assert.Fail("installed equipment must not settle twice"); return false; },
                _ => { Assert.Fail("installed equipment must not ship twice"); return false; },
                _ => { Assert.Fail("installed equipment must not receive twice"); return null; },
                "business-2", "bakery-2", 9, out reason), reason);
        }

        [Test]
        public void GenericProductionRuntime_BakeryConsumesInputsReleasesWorkerAndCreatesOneOutput()
        {
            var authority = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            EntityId person = EntityId.For(EntityKind.Person, 20);
            var runtime = new GenericProductionRuntime(authority, budgets, EntityId.For(EntityKind.Business, 20), 1);
            ProductionMethodDefinition method = GenericVerticalCatalog.Bread();
            var policy = new GenericProductionPolicy { MethodId = method.MethodId, TargetStock = 1f, BatchSize = 1f };
            var plan = new GenericProductionPlan { Policy = policy, Method = method, Quantity = 1f };
            int inputConsumption = 0;
            Assert.IsTrue(runtime.TryBegin(plan, person, _ => true, _ => true, _ => true, _ => true,
                _ => { inputConsumption++; return true; }, out ProductionProcessState process, out string reason), reason);
            int outputCount = 0;
            Assert.IsTrue(runtime.Advance(process, person, 1, () => { outputCount++; return true; }, out reason), reason);
            Assert.IsFalse(process.PersonRequiredNow(method));
            var other = new TaskDefinition("bakery-cleaning", "Clean", 5);
            Assert.IsTrue(authority.RegisterDefinition(other, out reason), reason);
            WorkTask task = authority.CreateTask(other.DefinitionId, EntityId.For(EntityKind.Business, 20), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, person, budgets, out reason), reason);
            Assert.IsTrue(runtime.Advance(process, person, 28, () => { outputCount++; return true; }, out reason), reason);
            Assert.IsTrue(runtime.Advance(process, person, 1, () => { outputCount++; return true; }, out reason), reason);
            Assert.AreEqual(2, inputConsumption);
            Assert.AreEqual(1, outputCount);
            Assert.IsTrue(process.Completed);
            Assert.IsFalse(GenericProductionAuthority.TryCreateOutputOnce(process, () => { outputCount++; return true; }));
        }

        [Test]
        public void SupplierOrderPlanner_AppliesPackagesBulkTiersAndBasketMinimum()
        {
            var offer = new SupplierOffer
            {
                OfferId = "jobber-1",
                SupplierId = "st-louis-jobber",
                MinimumOrderValueCents = 10000,
                Lines = new List<SupplierOfferLine>
                {
                    new SupplierOfferLine { ProductId = "coffee", AvailableQuantity = 100f, PackageQuantity = 10f, PackagePriceCents = 3500, PriceTiers = new List<SupplierPriceTier> { new SupplierPriceTier { MinimumQuantity = 50f, UnitPriceCents = 300 } } },
                    new SupplierOfferLine { ProductId = "cloth", AvailableQuantity = 100f, PackageQuantity = 5f, PackagePriceCents = 2500 },
                },
            };
            var requested = new Dictionary<string, float> { ["coffee"] = 50f, ["cloth"] = 20f };
            Assert.IsTrue(GenericProcurementPlanner.TryBuildOrder(offer, requested, out SupplierPurchaseOrder order, out string reason), reason);
            Assert.AreEqual(50f, order.Lines[0].Quantity);
            Assert.AreEqual(300, order.Lines[0].UnitPriceCents);
            Assert.IsTrue(order.MeetsMinimums);
            Assert.AreEqual(70f, order.TotalQuantity);
        }

        [Test]
        public void SupplierOrderPlanner_RejectsUnmetBasketMinimum()
        {
            var offer = new SupplierOffer
            {
                OfferId = "jobber-2",
                SupplierId = "jobber",
                MinimumOrderValueCents = 10000,
                Lines = new List<SupplierOfferLine>
                {
                    new SupplierOfferLine { ProductId = "coffee", AvailableQuantity = 20f, PackageQuantity = 10f, PackagePriceCents = 3500 },
                },
            };
            Assert.IsFalse(GenericProcurementPlanner.TryBuildOrder(offer,
                new Dictionary<string, float> { ["coffee"] = 10f }, out _, out _));
        }

        [Test]
        public void GenericProductionOutput_UsesOneBusinessInventoryAndIsIdempotent()
        {
            ProductionMethodDefinition method = GenericVerticalCatalog.Bread();
            var process = new ProductionProcessState { ProcessId = "bread-output" };
            process.Start(method);
            process.Completed = true;
            var configuration = new GenericBusinessConfiguration();
            Assert.IsTrue(GenericProductionOutputAuthority.TryCreateInventoryOutputOnce(
                process, method, configuration, new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each),
                25, "bakery-process", 4, out string reason), reason);
            Assert.AreEqual(1f, configuration.GetInventoryQuantity("bread"));
            Assert.IsFalse(GenericProductionOutputAuthority.TryCreateInventoryOutputOnce(
                process, method, configuration, null, 25, "bakery-process", 4, out _));
            Assert.AreEqual(1f, configuration.GetInventoryQuantity("bread"));
        }

        [Test]
        public void GenericReceiving_WritesLandedInventoryOncePerShipment()
        {
            var configuration = new GenericBusinessConfiguration();
            Assert.IsTrue(configuration.TryReceiveInventoryShipment("shipment-oven-input", "flour", 10f,
                42, new GenericProductDefinition("flour", "Flour", ProductQuantityUnit.Pound), "shipment:shipment-oven-input", 3));
            Assert.IsFalse(configuration.TryReceiveInventoryShipment("shipment-oven-input", "flour", 10f,
                42, null, "shipment:shipment-oven-input", 3));
            Assert.AreEqual(10f, configuration.GetInventoryQuantity("flour"));
            Assert.AreEqual(42, configuration.GetInventoryAverageUnitCostCents("flour"));
        }

        [Test]
        public void GenericRetailCapacity_SaleReleasesOccupiedButPreservesAllocation()
        {
            var config = new GenericRetailConfiguration();
            config.GetOrCreatePool("display", 20f).TryOccupy(20f);
            var line = new GenericRetailProductLine("coffee", "display", 20f, 40f, 5f, 100);
            config.AddOrReplaceLine(line);
            config.ReleaseProductSpace("coffee", 5f);
            RetailCapacityPool pool = config.GetOrCreatePool("display");
            Assert.AreEqual(20f, pool.Allocated);
            Assert.AreEqual(15f, pool.Occupied);
        }

        [Test]
        public void GenericProductionSkillChangesPerformanceButNeverPermission()
        {
            var skills = new SkillService();
            Assert.IsTrue(skills.RegisterSkill(new SkillDefinition("smithing", "Smithing"), out _));
            EntityId novice = EntityId.For(EntityKind.Person, 31);
            EntityId experienced = EntityId.For(EntityKind.Person, 32);
            skills.GrantPractice(experienced, "smithing", 100);
            var authority = new TaskAuthority();
            authority.SetDurationEstimator(new SkillTaskDurationEstimator(skills));
            var definition = new TaskDefinition("generic-smithing-test", "Forge", 10);
            definition.SetRequiredSkill("smithing", new[] { "smithing" });
            Assert.IsTrue(authority.RegisterDefinition(definition, out string reason), reason);
            var budgets = new WorkTimeBudgetStore();
            WorkTask noviceTask = authority.CreateTask(definition.DefinitionId, EntityId.For(EntityKind.Business, 31), 1);
            WorkTask experiencedTask = authority.CreateTask(definition.DefinitionId, EntityId.For(EntityKind.Business, 31), 1);
            Assert.IsTrue(authority.AssignTask(noviceTask.TaskId, novice, budgets, out reason), reason);
            Assert.IsTrue(authority.AssignTask(experiencedTask.TaskId, experienced, budgets, out reason), reason);
            Assert.AreEqual(10, noviceTask.PlannedMinutes);
            Assert.AreEqual(9, experiencedTask.PlannedMinutes);
        }
    }
}
