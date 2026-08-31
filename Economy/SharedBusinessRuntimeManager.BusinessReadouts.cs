using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LandLedgers.Economy
{
    public sealed partial class SharedBusinessRuntimeManager
    {
        private enum BusinessOperatingReadoutFamily
        {
            RetailStorefront,
            GoodsTransformer,
            ServiceThroughput,
            Lodging,
            Logistics,
            Construction,
            SpecialProduction,
            Mine,
            General
        }

        private readonly struct BusinessFlowDescriptor
        {
            public readonly string Model;
            public readonly string TakesIn;
            public readonly string PutsOut;
            public readonly string GetsPaidBy;
            public readonly string Watch;
            public readonly string FlowTitle;
            public readonly string InputTitle;
            public readonly string OutputTitle;
            public readonly string SupportStockTitle;
            public readonly string PrimaryRevenueLabel;
            public readonly string SecondaryRevenueLabel;
            public readonly string OwnerAdviceHealthy;

            public BusinessFlowDescriptor(
                string model,
                string takesIn,
                string putsOut,
                string getsPaidBy,
                string watch,
                string flowTitle,
                string inputTitle,
                string outputTitle,
                string supportStockTitle,
                string primaryRevenueLabel,
                string secondaryRevenueLabel,
                string ownerAdviceHealthy)
            {
                Model = model;
                TakesIn = takesIn;
                PutsOut = putsOut;
                GetsPaidBy = getsPaidBy;
                Watch = watch;
                FlowTitle = flowTitle;
                InputTitle = inputTitle;
                OutputTitle = outputTitle;
                SupportStockTitle = supportStockTitle;
                PrimaryRevenueLabel = primaryRevenueLabel;
                SecondaryRevenueLabel = secondaryRevenueLabel;
                OwnerAdviceHealthy = ownerAdviceHealthy;
            }
        }

        private string BuildBusinessOperatingReadoutText(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return string.Empty;
            }

            BusinessProfileDefinition profile = FindProfile(business.BusinessType);
            BusinessOperatingReadoutFamily family = ResolveOperatingReadoutFamilyV4(business, profile);
            BusinessFlowDescriptor descriptor = GetBusinessFlowDescriptorV4(business, family);
            StringBuilder builder = new();

            builder.AppendLine(BuildBusinessReadoutTitleV4(business, family, descriptor));
            AppendBusinessAtAGlanceV4(builder, business, profile, family, descriptor);
            builder.AppendLine();
            AppendMoneyPulseV4(builder, business, descriptor);
            builder.AppendLine();

            switch (business.BusinessType)
            {
                case BusinessType.Blacksmith:
                    AppendBlacksmithReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.Butcher:
                    AppendButcherReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.CropFarm:
                    AppendFarmReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.Ranch:
                    AppendRanchReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.Bakery:
                case BusinessType.GrainMill:
                case BusinessType.FuelDealer:
                case BusinessType.Tailor:
                case BusinessType.Wheelwright:
                    AppendWorkshopReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.BoardingHouse:
                    AppendBoardingHouseReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.LiveryFreight:
                    AppendLiveryFreightReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.Builder:
                    AppendBuilderReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.Barber:
                case BusinessType.Saloon:
                case BusinessType.Doctor:
                    AppendServiceReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.Sawmill:
                    AppendSawmillReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.Mine:
                    AppendMineReadoutV4(builder, business, profile, descriptor);
                    break;
                case BusinessType.GeneralStore:
                case BusinessType.LumberYard:
                    AppendRetailStorefrontReadoutV4(builder, business, profile, descriptor);
                    break;
                default:
                    AppendGenericReadoutV4(builder, business, profile, family, descriptor);
                    break;
            }

            builder.AppendLine();
            AppendOperatingNotesV4(builder, business, profile, family, descriptor);
            return builder.ToString();
        }

        private BusinessOperatingReadoutFamily ResolveOperatingReadoutFamilyV4(BusinessInstanceState business, BusinessProfileDefinition profile)
        {
            if (business == null)
            {
                return BusinessOperatingReadoutFamily.General;
            }

            switch (business.BusinessType)
            {
                case BusinessType.Mine:
                    return BusinessOperatingReadoutFamily.Mine;
                case BusinessType.BoardingHouse:
                    return BusinessOperatingReadoutFamily.Lodging;
                case BusinessType.LiveryFreight:
                    return BusinessOperatingReadoutFamily.Logistics;
                case BusinessType.Builder:
                    return BusinessOperatingReadoutFamily.Construction;
                case BusinessType.Sawmill:
                    return BusinessOperatingReadoutFamily.SpecialProduction;
                case BusinessType.GeneralStore:
                case BusinessType.LumberYard:
                    return BusinessOperatingReadoutFamily.RetailStorefront;
            }

            BusinessArchetype archetype = profile != null ? profile.Archetype : BusinessArchetype.Unspecified;
            if (archetype == BusinessArchetype.Lodging)
            {
                return BusinessOperatingReadoutFamily.Lodging;
            }

            if (archetype == BusinessArchetype.LogisticsMovement)
            {
                return BusinessOperatingReadoutFamily.Logistics;
            }

            if (archetype == BusinessArchetype.ConstructionProject)
            {
                return BusinessOperatingReadoutFamily.Construction;
            }

            if (archetype == BusinessArchetype.SpecialProduction)
            {
                return BusinessOperatingReadoutFamily.SpecialProduction;
            }

            if (archetype == BusinessArchetype.ServiceThroughput || business.ThroughputMode == BusinessThroughputMode.Service)
            {
                return BusinessOperatingReadoutFamily.ServiceThroughput;
            }

            if (business.ThroughputMode == BusinessThroughputMode.Retail)
            {
                return BusinessOperatingReadoutFamily.RetailStorefront;
            }

            if (archetype == BusinessArchetype.GoodsTransformer
                || business.ThroughputMode == BusinessThroughputMode.Producer
                || business.ThroughputMode == BusinessThroughputMode.Converter)
            {
                return BusinessOperatingReadoutFamily.GoodsTransformer;
            }

            return BusinessOperatingReadoutFamily.General;
        }

        private BusinessFlowDescriptor GetBusinessFlowDescriptorV4(BusinessInstanceState business, BusinessOperatingReadoutFamily family)
        {
            return business.BusinessType switch
            {
                BusinessType.GeneralStore => new BusinessFlowDescriptor(
                    "retail demand hub",
                    "off-map/local stock categories",
                    "household goods sold off the shelves",
                    "household shopping and local demand capture",
                    "empty shelves, weak price capture, and Saturday readiness",
                    "Shelf Flow",
                    "Goods Bought / Restocked",
                    "Goods Sold",
                    "All Store Categories",
                    "Counter sales",
                    "Local demand capture",
                    "Keep essential shelves stocked before chasing higher margins."),
                BusinessType.Blacksmith => new BusinessFlowDescriptor(
                    "repair + hardware workshop",
                    "metal, coal/fuel, hardware inputs, and skilled smith labor",
                    "nails, simple hardware, repairs, horseshoes, hinges, chains, and tool work",
                    "repair/hardware work for farms, ranches, wagons, households, and projects",
                    "input stock and skilled coverage; finished nails are made here, not imported when the input→output flow is moving",
                    "Forge Flow",
                    "Forge Inputs",
                    "Hardware / Repair Output",
                    "Shop Stock",
                    "Counter sales",
                    "Repair / hardware work",
                    "Keep metal/fuel stocked and a skilled smith covered before judging demand."),
                BusinessType.Butcher => new BusinessFlowDescriptor(
                    "livestock-to-meat shop",
                    "livestock or carcass supply, salt/preservation support, and butcher labor",
                    "fresh meat, preserved meat, hides/tallow where supported",
                    "household food demand, local buyers, boarding houses, and stores",
                    "livestock supply, spoilage, and cold-storage pressure",
                    "Butcher Flow",
                    "Animals / Processing Inputs",
                    "Meat / Provision Output",
                    "Meat Counter / Side Stock",
                    "Counter sales",
                    "Meat trade / local buyers",
                    "Move meat quickly; supply and spoilage matter more than shelf count alone."),
                BusinessType.CropFarm => new BusinessFlowDescriptor(
                    "seasonal field producer",
                    "seed, labor, tools, weather window, and hauling support",
                    "staple crops and bulk food supply",
                    "local/off-map crop buyers and downstream food chains",
                    "seasonal labor, hauling, and whether output has a buyer route",
                    "Field Flow",
                    "Field Inputs",
                    "Crop Output",
                    "Barn / Farm Stock",
                    "Farmgate sales",
                    "Crop deliveries / bulk buyers",
                    "Judge farms across several weeks or seasons, not one storefront-style day."),
                BusinessType.Ranch => new BusinessFlowDescriptor(
                    "livestock producer",
                    "feed, range labor, animal care, hauling, and supervision",
                    "livestock, meat-chain supply, hides/tallow routes where supported",
                    "butchers, off-map buyers, local provisions, and later internal chains",
                    "range labor, hauling, animal losses, and downstream demand",
                    "Range Flow",
                    "Range Inputs",
                    "Livestock Output",
                    "Ranch Stock",
                    "Livestock sales",
                    "Downstream livestock trade",
                    "A ranch is healthy when animals move into profitable buyer routes, not when it looks like a store."),
                BusinessType.Doctor => new BusinessFlowDescriptor(
                    "medical service office",
                    "remedies, supplies, doctor time, and patient demand",
                    "office visits, treatment access, and reduced labor loss",
                    "patients, house calls where supported, and medical service fees",
                    "qualified doctor coverage and remedy supply",
                    "Care Flow",
                    "Medical Supplies",
                    "Visits / Treatment",
                    "Remedies / Office Stock",
                    "Patient fees",
                    "Care work / house calls",
                    "Doctor value is partly economic protection: fewer lost workdays and faster recovery."),
                BusinessType.Sawmill => new BusinessFlowDescriptor(
                    "timber-to-lumber chain",
                    "standing timber, logs, saw labor, machinery condition, and hauling",
                    "lumber, beams/boards where supported, slabs/offcuts, and project supply",
                    "construction projects, lumber yards, mines, farms, rail work, and local buyers",
                    "log supply, mill capacity, yard storage, and hauling strain",
                    "Timber / Lumber Flow",
                    "Timber / Logs",
                    "Lumber / Byproducts",
                    "Mill Yard Stock",
                    "Lumber sales",
                    "Project / yard supply",
                    "Keep logs, saw capacity, storage, and buyer priority aligned before expanding."),
                BusinessType.LumberYard => new BusinessFlowDescriptor(
                    "lumber storefront / yard",
                    "lumber from mills, hauled stock, yard labor, and storage",
                    "lumber availability for projects, households, farms, and businesses",
                    "construction/project demand and local lumber buyers",
                    "yard stock, project backlog, and supply reliability",
                    "Yard Flow",
                    "Lumber Inbound",
                    "Lumber Sold / Reserved",
                    "Yard Stock",
                    "Yard sales",
                    "Project demand",
                    "This is the town-facing lumber surface; keep stock readable for builders and projects."),
                BusinessType.BoardingHouse => new BusinessFlowDescriptor(
                    "lodging + meals",
                    "beds, meals, fuel, laundry/house supplies, and staff coverage",
                    "occupied beds, board, labor absorption, and settlement capacity",
                    "rent/board from workers, newcomers, travelers, and temporary residents",
                    "vacancy, meal/fuel supply, and whether town growth is blocked by beds",
                    "Boarding Flow",
                    "House Supplies",
                    "Beds / Board",
                    "Meals / Fuel / House Stock",
                    "Board income",
                    "Lodging pressure",
                    "Boarding houses are settlement infrastructure; full beds may be good money but bad town capacity."),
                BusinessType.LiveryFreight => new BusinessFlowDescriptor(
                    "freight capacity business",
                    "feed, teams, wagons/carts, harness, stabling, and teamster labor",
                    "hauling trips, delivery capacity, and local freight reliability",
                    "freight jobs, business deliveries, project hauling, and supply-chain movement",
                    "feed/upkeep costs, idle teams, and whether enough cargo demand exists",
                    "Freight Flow",
                    "Feed / Upkeep Inputs",
                    "Trips / Hauling Output",
                    "Stable / Freight Stock",
                    "Freight fees",
                    "Hauling contracts",
                    "A livery/freight business should become stronger as local trade chains get busier."),
                BusinessType.Builder => new BusinessFlowDescriptor(
                    "project labor contractor",
                    "crew time, tools, lumber, nails, and active project demand",
                    "construction progress, repairs, fit-outs, and project labor value",
                    "build/repair projects and construction contracts",
                    "project queue, material blockers, and crew coverage",
                    "Project Flow",
                    "Project Inputs",
                    "Construction Work",
                    "Tools / Material Readiness",
                    "Project fees",
                    "Construction work",
                    "If the builder looks weak, check whether there are active projects and materials before tuning prices."),
                BusinessType.FuelDealer => new BusinessFlowDescriptor(
                    "fuel yard",
                    "wood, slabs/offcuts, coal/fuel stock, yard labor, and hauling",
                    "usable heating fuel for households and businesses",
                    "winter/heating demand and local fuel buyers",
                    "seasonality, fuel stock, and delivery capacity",
                    "Fuel Yard Flow",
                    "Fuel Inputs",
                    "Heating Fuel Output",
                    "Fuel Yard Stock",
                    "Fuel sales",
                    "Heating demand",
                    "Fuel dealers should matter most when cold weather and household fuel reserves create pressure."),
                BusinessType.GrainMill => new BusinessFlowDescriptor(
                    "grain-to-flour mill",
                    "grain supply, mill labor, sacks/handling, and machinery upkeep",
                    "flour and milled staple output",
                    "bakeries, stores, boarding houses, households, and off-map buyers",
                    "grain supply, output storage, and buyer routing",
                    "Mill Flow",
                    "Grain Inputs",
                    "Flour Output",
                    "Mill Stock",
                    "Mill sales",
                    "Flour trade / local buyers",
                    "A grain mill is healthy when flour has a steady buyer path, especially into bakeries and stores."),
                BusinessType.Bakery => new BusinessFlowDescriptor(
                    "food workshop",
                    "flour, fuel/heat, baking supplies, and baker labor",
                    "bread and prepared staple food",
                    "household demand, boarding houses, stores, and local food buyers",
                    "flour/fuel supply and whether bread sells before stock piles up",
                    "Bakery Flow",
                    "Flour / Fuel Inputs",
                    "Bread Output",
                    "Bakery Stock",
                    "Bread sales",
                    "Food trade / local buyers",
                    "If bread is not monetizing, connect the bakery to household demand, boarding, or store supply."),
                BusinessType.Tailor => new BusinessFlowDescriptor(
                    "clothing + mending workshop",
                    "cloth, thread/notions, tools, and skilled labor",
                    "clothing, repairs, and mending value",
                    "households, workers, service traffic, and local clothing demand",
                    "cloth supply and whether mending/clothing work has enough local demand",
                    "Tailor Flow",
                    "Cloth / Notions",
                    "Clothing / Mending Output",
                    "Tailor Stock",
                    "Counter work",
                    "Mending / clothing trade",
                    "Tailor income should look like a mix of goods and service work, not just shelf sales."),
                BusinessType.Saloon => new BusinessFlowDescriptor(
                    "food/social service",
                    "meal supplies, drink stock, fuel, staff time, and evening traffic",
                    "meals, drink service, social draw, and town activity",
                    "daily service visits and evening trade",
                    "service capacity, supply coverage, and whether the town is large enough to support traffic",
                    "Service Flow",
                    "Meal / Drink Inputs",
                    "Visits / Service",
                    "Saloon Stock",
                    "Service receipts",
                    "Evening/local trade",
                    "A saloon should be read as capacity and town traffic before it is read as shelf inventory."),
                BusinessType.Barber => new BusinessFlowDescriptor(
                    "personal service",
                    "barber time, small consumables, tools, and foot traffic",
                    "haircuts, shaves, grooming, and small personal services",
                    "service visits from residents, workers, and travelers",
                    "unused chair capacity and town population support",
                    "Service Flow",
                    "Consumables / Tools",
                    "Visits / Service",
                    "Barber Supplies",
                    "Service receipts",
                    "Chair work",
                    "If capacity is unused, the issue is demand or town size, not stock."),
                BusinessType.Wheelwright => new BusinessFlowDescriptor(
                    "wagon repair workshop",
                    "lumber, hardware, wheel parts, tools, and skilled labor",
                    "wagon/wheel repairs, cart work, and transport reliability",
                    "farms, ranches, freight outfits, merchants, and projects",
                    "lumber/hardware supply and local transport demand",
                    "Wheelwright Flow",
                    "Lumber / Hardware Inputs",
                    "Wagon Repair Output",
                    "Wheelwright Stock",
                    "Repair receipts",
                    "Wagon/cart work",
                    "Wheelwright demand should rise as hauling, farms, ranches, and freight get busier."),
                BusinessType.Mine => new BusinessFlowDescriptor(
                    "remote extraction site",
                    "labor, tools, timber/support material, safety, and hauling",
                    "ore/coal/mineral output and regional industrial pressure",
                    "contracts, off-map export, industry chains, and later rail/freight routes",
                    "safety, hauling, camp pressure, and support supply",
                    "Mine Flow",
                    "Mine Supplies",
                    "Extracted Output",
                    "Mine Stockpile / Supplies",
                    "Output sales",
                    "Mine contracts / export",
                    "Mines need mine-specific diagnostics; shared cash stays here but the operation is not a storefront."),
                _ => new BusinessFlowDescriptor(
                    family.ToString(),
                    "inputs and operating cash",
                    "weekly business output",
                    "the relevant local market or service demand",
                    "cash flow, staffing, and blocked operations",
                    "Operating Flow",
                    "Inputs",
                    "Outputs",
                    "Tracked Stock",
                    "Direct receipts",
                    "Local trade/work",
                    "Watch several weeks before making heavy balance changes.")
            };
        }

        private string BuildBusinessReadoutTitleV4(BusinessInstanceState business, BusinessOperatingReadoutFamily family, BusinessFlowDescriptor descriptor)
        {
            string typeLabel = business != null ? GetBusinessSummaryLabel(business) : "Business";
            string title = family switch
            {
                BusinessOperatingReadoutFamily.RetailStorefront => "Storefront Table",
                BusinessOperatingReadoutFamily.GoodsTransformer => "Input / Output Table",
                BusinessOperatingReadoutFamily.ServiceThroughput => "Service Table",
                BusinessOperatingReadoutFamily.Lodging => "Lodging Table",
                BusinessOperatingReadoutFamily.Logistics => "Freight Table",
                BusinessOperatingReadoutFamily.Construction => "Project Crew Table",
                BusinessOperatingReadoutFamily.SpecialProduction => "Production Chain Table",
                BusinessOperatingReadoutFamily.Mine => "Mine Table",
                _ => "Operating Table"
            };

            return $"{typeLabel} — {title}";
        }

        private void AppendBusinessAtAGlanceV4(
            StringBuilder builder,
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            BusinessOperatingReadoutFamily family,
            BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            string focus = business.MainFocus != null ? business.MainFocus.DisplayName : "Balanced";
            builder.AppendLine("At a Glance");
            builder.AppendLine($"Runs as: {descriptor.Model} | Focus: {focus} | Status: {ResolveStatusV4(business, family)}");
            builder.AppendLine($"Takes in: {descriptor.TakesIn}");
            builder.AppendLine($"Puts out: {descriptor.PutsOut}");
            builder.AppendLine($"Gets paid by: {descriptor.GetsPaidBy}");
            builder.AppendLine($"Watch: {descriptor.Watch}");
            builder.AppendLine($"Staff: {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount} filled | Required: {runtime.FilledWorkerCount}/{runtime.RequiredWorkerCount} | Efficiency: {GetHealthAdjustedOperatingEfficiency01(business):P0}");
        }

        private void AppendMoneyPulseV4(StringBuilder builder, BusinessInstanceState business, BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            int directRevenue = Mathf.Max(0, runtime.WeekToDateRevenueCents);
            int localWorkRevenue = Mathf.Max(0, runtime.LastWeeklyLocalTransferRevenueCents);
            int totalRevenue = directRevenue + localWorkRevenue;
            int inputSpend = Mathf.Max(0, runtime.LastWeeklyInputProcurementSpendCents + runtime.LastWeeklyReorderBudgetCents);
            int payroll = Mathf.Max(0, runtime.LastWeeklyPayrollCents);
            int freight = Mathf.Max(0, runtime.LastWeeklyLocalTransferCostCents);
            int ownerDraw = Mathf.Max(0, runtime.LastWeeklyOwnerDistributionCents);
            int cashDelta = runtime.LastWeeklyCashAfterCents - runtime.LastWeeklyCashBeforeCents;

            builder.AppendLine("Weekly Money");
            builder.AppendLine($"Cash now: {FormatMoney(runtime.CurrentCashCents)} | Last week net: {FormatSignedMoney(cashDelta)} | Total paid in: {FormatMoney(totalRevenue)}");
            builder.AppendLine($"{descriptor.PrimaryRevenueLabel}: {FormatMoney(directRevenue)} | {descriptor.SecondaryRevenueLabel}: {FormatMoney(localWorkRevenue)}");
            builder.AppendLine($"Inputs / stock bought: {FormatMoney(inputSpend)} | Wages: {FormatMoney(payroll)} | Freight / upkeep: {FormatMoney(freight)} | Owner draw: {FormatMoney(ownerDraw)}");
        }

        private void AppendBlacksmithReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine("Blacksmith Workbench");
            builder.AppendLine("Plain read: this shop buys metal/fuel inputs, then turns them into repair work, nails, simple hardware, and practical farm/wagon support.");
            builder.AppendLine("Nails shown in output stock are the shop's made/sellable hardware output, not automatically imported finished nails.");
            builder.AppendLine();
            AppendFlowSectionV4(builder, business, profile, descriptor);
            AppendSupportingStockListV4(builder, business, profile, descriptor);
        }

        private void AppendButcherReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine("Butcher Block");
            builder.AppendLine("Plain read: livestock supply becomes meat/provision stock. The key questions are whether animals arrive, meat moves, and spoilage is controlled.");
            builder.AppendLine();
            AppendFlowSectionV4(builder, business, profile, descriptor);
            AppendSupportingStockListV4(builder, business, profile, descriptor);
        }

        private void AppendFarmReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine("Farm Production");
            builder.AppendLine("Plain read: farms are seasonal producers. Judge them through output, labor, hauling, and buyer routes rather than same-day counter sales.");
            builder.AppendLine();
            AppendFlowSectionV4(builder, business, profile, descriptor);
            AppendSupportingStockListV4(builder, business, profile, descriptor);
        }

        private void AppendRanchReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine("Ranch Operations");
            builder.AppendLine("Plain read: ranch value comes from livestock moving into butchers, buyers, and later owned chains. Cash may lag behind production if hauling or buyer routes are weak.");
            builder.AppendLine();
            AppendFlowSectionV4(builder, business, profile, descriptor);
            AppendSupportingStockListV4(builder, business, profile, descriptor);
        }

        private void AppendWorkshopReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine(descriptor.FlowTitle);
            builder.AppendLine($"Plain read: this business takes in {descriptor.TakesIn}, puts out {descriptor.PutsOut}, and earns from {descriptor.GetsPaidBy}.");
            builder.AppendLine();
            AppendFlowSectionV4(builder, business, profile, descriptor);
            AppendSupportingStockListV4(builder, business, profile, descriptor);
        }

        private void AppendRetailStorefrontReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine(descriptor.FlowTitle);
            builder.AppendLine($"Plain read: this is shelf/yard trade. It buys stock, holds it visibly, and loses sales when the right category is empty or badly priced.");
            builder.AppendLine();
            AppendAllStockLedgerV4(builder, business, profile, descriptor.SupportStockTitle, includePrices: true);

            string marketLine = BuildBusinessLocalMarketCaptureLine(business);
            if (!string.IsNullOrWhiteSpace(marketLine))
            {
                builder.AppendLine(marketLine);
            }
        }

        private void AppendServiceReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            int dailyCapacity = EstimateDailyServiceCapacityV4(business);
            int weeklyCapacity = EstimateWeeklyServiceCapacityV4(business);
            int served = Mathf.Max(0, runtime.WeekToDateUnitsSold);
            int revenue = Mathf.Max(0, runtime.WeekToDateRevenueCents + runtime.LastWeeklyLocalTransferRevenueCents);

            builder.AppendLine(descriptor.FlowTitle);
            builder.AppendLine($"Plain read: this business mainly sells staffed time/capacity. Stock is support material, not the main product.");
            builder.AppendLine($"Takes in: {descriptor.TakesIn}");
            builder.AppendLine($"Puts out: {descriptor.PutsOut}");
            builder.AppendLine($"Capacity: {dailyCapacity}/day | about {weeklyCapacity}/week | Served this week: {served} | Revenue: {FormatMoney(revenue)}");
            builder.AppendLine($"Unused capacity estimate: {Mathf.Max(0, weeklyCapacity - served)} visit(s)/week");
            builder.AppendLine();
            AppendServiceStockLedgerV4(builder, business, profile, descriptor.SupportStockTitle);
        }

        private void AppendBoardingHouseReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine("Beds / Board");
            builder.AppendLine("Plain read: beds and board convert meal/fuel supply into lodging income and labor-settlement capacity.");
            builder.AppendLine(BuildBoardingHouseOccupancyLine(business));
            builder.AppendLine();
            AppendServiceStockLedgerV4(builder, business, profile, descriptor.SupportStockTitle);

            string townContext = BuildTownContextLine(business);
            if (!string.IsNullOrWhiteSpace(townContext))
            {
                builder.AppendLine(townContext);
            }
        }

        private void AppendLiveryFreightReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            int weeklyCapacity = EstimateWeeklyServiceCapacityV4(business);
            int revenue = Mathf.Max(0, runtime.WeekToDateRevenueCents + runtime.LastWeeklyLocalTransferRevenueCents);
            int cost = Mathf.Max(0, runtime.LastWeeklyInputProcurementSpendCents + runtime.LastWeeklyLocalTransferCostCents);

            builder.AppendLine("Freight Yard");
            builder.AppendLine("Plain read: this business sells wagon/team capacity. Feed and upkeep are operating inputs; trips/contracts are the output.");
            builder.AppendLine($"Trip capacity: about {weeklyCapacity}/week | Freight income: {FormatMoney(revenue)} | Feed/upkeep cost: {FormatMoney(cost)}");
            builder.AppendLine($"Current transfer/trip summary: {GetWeeklyTransferSummary(runtime)}");
            builder.AppendLine();
            AppendServiceStockLedgerV4(builder, business, profile, descriptor.SupportStockTitle);
        }

        private void AppendBuilderReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            int weeklyCapacity = EstimateWeeklyServiceCapacityV4(business);
            int revenue = Mathf.Max(0, runtime.WeekToDateRevenueCents + runtime.LastWeeklyLocalTransferRevenueCents);

            builder.AppendLine("Project Crew");
            builder.AppendLine("Plain read: this business turns crew time and project materials into construction progress. Low sales can mean no active project, not a bad shop.");
            builder.AppendLine($"Crew capacity: about {weeklyCapacity}/week | Project labor served: {runtime.WeekToDateUnitsSold} | Project revenue: {FormatMoney(revenue)}");
            builder.AppendLine($"Project blocker: {BlockedOrClearV4(runtime)}");
            builder.AppendLine();
            AppendServiceStockLedgerV4(builder, business, profile, descriptor.SupportStockTitle);
        }

        private void AppendSawmillReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine("Sawmill Chain");
            builder.AppendLine("Plain read: this is not a storefront. It turns timber/logs into lumber and byproducts, then feeds projects, yards, repairs, and later heavier chains.");
            string productionContext = BuildBusinessProductionContextLine(business);
            if (!string.IsNullOrWhiteSpace(productionContext))
            {
                builder.AppendLine(productionContext);
            }

            builder.AppendLine();
            AppendFlowSectionV4(builder, business, profile, descriptor);
            AppendSupportingStockListV4(builder, business, profile, descriptor);
        }

        private void AppendMineReadoutV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            builder.AppendLine("Mine Site");
            builder.AppendLine("Plain read: shared cash and ownership stay here, but the mine must be judged through output, supplies, safety, hauling, and site pressure.");
            AppendMineInventoryLines(builder, business);
            builder.AppendLine();
            AppendAllStockLedgerV4(builder, business, profile, descriptor.SupportStockTitle, includePrices: true);
        }

        private void AppendGenericReadoutV4(
            StringBuilder builder,
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            BusinessOperatingReadoutFamily family,
            BusinessFlowDescriptor descriptor)
        {
            if (family == BusinessOperatingReadoutFamily.ServiceThroughput)
            {
                AppendServiceReadoutV4(builder, business, profile, descriptor);
                return;
            }

            if (family == BusinessOperatingReadoutFamily.GoodsTransformer || family == BusinessOperatingReadoutFamily.SpecialProduction)
            {
                AppendWorkshopReadoutV4(builder, business, profile, descriptor);
                return;
            }

            builder.AppendLine("Operating Ledger");
            builder.AppendLine("Plain read: no specialized panel exists yet, so this business is summarized through stock, cash, staffing, and weekly operation notes.");
            AppendAllStockLedgerV4(builder, business, profile, descriptor.SupportStockTitle, includePrices: true);
        }

        private void AppendFlowSectionV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            ReadOnlySpan<string> inputs = profile != null ? profile.InputCategoryIds : Array.Empty<string>();
            ReadOnlySpan<string> outputs = profile != null ? profile.OutputCategoryIds : Array.Empty<string>();
            int earned = Mathf.Max(0, runtime.LastWeeklyLocalTransferRevenueCents + runtime.WeekToDateRevenueCents);
            int inputSpent = Mathf.Max(0, runtime.LastWeeklyInputProcurementSpendCents + runtime.LastWeeklyReorderBudgetCents);

            builder.AppendLine(descriptor.FlowTitle);
            builder.AppendLine($"Takes in: {descriptor.TakesIn}");
            builder.AppendLine($"Puts out: {descriptor.PutsOut}");
            builder.AppendLine($"This week: used {Mathf.Max(0, runtime.LastWeeklyInputUnitsConsumed)} input unit(s) / spent {FormatMoney(inputSpent)}; made {Mathf.Max(0, runtime.LastWeeklyOutputUnitsProduced)} output unit(s) / earned or valued {FormatMoney(earned)}.");

            if (inputs.Length == 0 && outputs.Length == 0)
            {
                builder.AppendLine("No explicit profile input/output categories are configured yet. Read this business through the stock ledger and operating notes below.");
                return;
            }

            if (inputs.Length > 0)
            {
                builder.AppendLine();
                AppendCategoryGroupV4(builder, business, profile, descriptor.InputTitle, inputs, includePrices: false, showWeekSold: false);
            }

            if (outputs.Length > 0)
            {
                builder.AppendLine();
                AppendCategoryGroupV4(builder, business, profile, descriptor.OutputTitle, outputs, includePrices: true, showWeekSold: true);
            }
        }

        private void AppendSupportingStockListV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, BusinessFlowDescriptor descriptor)
        {
            HashSet<string> excluded = BuildConfiguredCategorySetV4(profile);
            List<CategoryStockState> supportStock = new();
            IReadOnlyList<CategoryStockState> stock = business.RuntimeState.CategoryStock;
            for (int i = 0; i < stock.Count; i++)
            {
                CategoryStockState category = stock[i];
                if (category == null)
                {
                    continue;
                }

                if (!excluded.Contains(NormalizeCategoryIdV4(category.CategoryId)))
                {
                    supportStock.Add(category);
                }
            }

            if (supportStock.Count <= 0)
            {
                return;
            }

            builder.AppendLine();
            AppendStockRowsV4(builder, business, profile, descriptor.SupportStockTitle, supportStock, includePrices: true, showWeekSold: true);
        }

        private void AppendAllStockLedgerV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, string heading, bool includePrices)
        {
            IReadOnlyList<CategoryStockState> stock = business.RuntimeState.CategoryStock;
            List<CategoryStockState> rows = new();
            for (int i = 0; i < stock.Count; i++)
            {
                if (stock[i] != null)
                {
                    rows.Add(stock[i]);
                }
            }

            AppendStockRowsV4(builder, business, profile, heading, rows, includePrices, showWeekSold: true);
        }

        private void AppendServiceStockLedgerV4(StringBuilder builder, BusinessInstanceState business, BusinessProfileDefinition profile, string heading)
        {
            ReadOnlySpan<string> services = profile != null ? profile.ServiceCategoryIds : Array.Empty<string>();
            if (services.Length > 0)
            {
                AppendCategoryGroupV4(builder, business, profile, heading, services, includePrices: false, showWeekSold: false);
                return;
            }

            AppendAllStockLedgerV4(builder, business, profile, heading, includePrices: false);
        }

        private void AppendCategoryGroupV4(
            StringBuilder builder,
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            string heading,
            ReadOnlySpan<string> categoryIds,
            bool includePrices,
            bool showWeekSold)
        {
            List<CategoryStockState> rows = new();
            for (int i = 0; i < categoryIds.Length; i++)
            {
                CategoryStockState category = FindRuntimeCategoryV4(business, categoryIds[i]);
                if (category != null)
                {
                    rows.Add(category);
                }
                else
                {
                    rows.Add(CategoryStockPlaceholderV4(categoryIds[i]));
                }
            }

            AppendStockRowsV4(builder, business, profile, heading, rows, includePrices, showWeekSold);
        }

        private void AppendStockRowsV4(
            StringBuilder builder,
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            string heading,
            IReadOnlyList<CategoryStockState> rows,
            bool includePrices,
            bool showWeekSold)
        {
            builder.AppendLine(heading);
            if (rows == null || rows.Count == 0)
            {
                builder.AppendLine("No tracked stock rows yet. Read this business through capacity, cash, staff coverage, and operating notes.");
                return;
            }

            if (includePrices)
            {
                builder.AppendLine("<mspace=0.62em>Category              Have/Target  Week Sold  Sell      Cost      Margin    Status</mspace>");
            }
            else
            {
                builder.AppendLine("<mspace=0.62em>Category              Have/Target  Week Used  Pending   Status</mspace>");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                CategoryStockState category = rows[i];
                if (category == null)
                {
                    continue;
                }

                string display = GetCategoryDisplayName(profile, category.CategoryId);
                string have = category.TargetStockUnits > 0 ? $"{category.CurrentStockUnits}/{category.TargetStockUnits}" : category.CurrentStockUnits.ToString();
                string status = BuildStockStatusV4(category);
                float health = category.TargetStockUnits > 0 ? category.StockHealth01 : 1f;

                if (includePrices)
                {
                    int price = GetAverageCategoryTransferPriceCents(business, profile, category.CategoryId);
                    int cost = GetAverageCategoryLandedCostCents(profile, category.CategoryId);
                    int margin = price - cost;
                    string weekSold = showWeekSold ? Mathf.Max(0, category.WeekToDateUnitsSold).ToString() : "-";
                    builder.AppendLine(
                        $"<mspace=0.62em>{FitV4(display, 22)}" +
                        $"{FitV4(have, 13)}" +
                        $"{FitV4(weekSold, 11)}" +
                        $"{FitV4(FormatMoney(price), 10)}" +
                        $"{FitV4(FormatMoney(cost), 10)}" +
                        $"{FitV4(FormatSignedMoney(margin), 10)}</mspace>{ColorStatusV4(status, health)}");
                }
                else
                {
                    string pending = category.PendingReorderUnits > 0 ? category.PendingReorderUnits.ToString() : "-";
                    string used = showWeekSold ? Mathf.Max(0, category.WeekToDateUnitsSold).ToString() : "-";
                    builder.AppendLine(
                        $"<mspace=0.62em>{FitV4(display, 22)}" +
                        $"{FitV4(have, 13)}" +
                        $"{FitV4(used, 11)}" +
                        $"{FitV4(pending, 10)}</mspace>{ColorStatusV4(status, health)}");
                }
            }
        }

        private CategoryStockState CategoryStockPlaceholderV4(string categoryId)
        {
            // This placeholder is only used to display configured profile categories that do not yet have runtime stock rows.
            // Do not mutate it; it is never added back into business state.
            return new CategoryStockState(categoryId ?? string.Empty, 0, 0);
        }

        private HashSet<string> BuildConfiguredCategorySetV4(BusinessProfileDefinition profile)
        {
            HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
            if (profile == null)
            {
                return set;
            }

            AddSpanToSetV4(set, profile.InputCategoryIds);
            AddSpanToSetV4(set, profile.OutputCategoryIds);
            AddSpanToSetV4(set, profile.ServiceCategoryIds);
            return set;
        }

        private static void AddSpanToSetV4(HashSet<string> set, ReadOnlySpan<string> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                string normalized = NormalizeCategoryIdV4(values[i]);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    set.Add(normalized);
                }
            }
        }

        private void AppendOperatingNotesV4(
            StringBuilder builder,
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            BusinessOperatingReadoutFamily family,
            BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            builder.AppendLine("Operating Notes");
            builder.AppendLine($"Last operation: {GetLastOperationSummary(business)}");
            builder.AppendLine($"Blocked: {BlockedOrClearV4(runtime)}");
            builder.AppendLine($"Local trade / routing: {GetWeeklyTransferSummary(runtime)}");

            string tradeLine = BuildLocalTradeCashSupportLine(business);
            if (!string.IsNullOrWhiteSpace(tradeLine) && family != BusinessOperatingReadoutFamily.RetailStorefront)
            {
                builder.AppendLine(tradeLine);
            }

            builder.AppendLine(BuildOwnerReadLineV4(business, family, descriptor));
        }

        private string BuildOwnerReadLineV4(BusinessInstanceState business, BusinessOperatingReadoutFamily family, BusinessFlowDescriptor descriptor)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            if (!string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason))
            {
                return $"Owner read: clear the blocker first — {runtime.LastWeeklyBlockedReason}.";
            }

            if (runtime.FilledWorkerCount < runtime.RequiredWorkerCount)
            {
                return "Owner read: fill required labor before judging whether this business is profitable.";
            }

            int reserveShortfall = GetOperatingReserveCashShortfallCentsV4(business);
            if (reserveShortfall > 0)
            {
                return $"Owner read: operating cash is thin; add about {FormatMoney(reserveShortfall)} or wait for auto-reserve support.";
            }

            if (family == BusinessOperatingReadoutFamily.GoodsTransformer || family == BusinessOperatingReadoutFamily.SpecialProduction)
            {
                if (runtime.LastWeeklyInputUnitsConsumed <= 0 && runtime.LastWeeklyOutputUnitsProduced <= 0)
                {
                    return "Owner read: the chain did not move this week. Check input supply, output storage, working cash, and buyer routing.";
                }

                if (runtime.LastWeeklyOutputUnitsProduced > 0 && runtime.LastWeeklyLocalTransferRevenueCents + runtime.WeekToDateRevenueCents <= 0)
                {
                    return "Owner read: output exists but is not monetizing yet. Add a local buyer, storefront route, recurring order, or off-map sale path.";
                }
            }

            if (family == BusinessOperatingReadoutFamily.ServiceThroughput && EstimateWeeklyServiceCapacityV4(business) <= 0)
            {
                return "Owner read: service capacity is missing. Add workers/profile capacity before tuning demand.";
            }

            if (runtime.StockHealth01 <= 0.35f && runtime.TotalTargetStockUnits > 0)
            {
                return "Owner read: stock is thin. Secure supply before judging price or demand.";
            }

            if (runtime.WeekToDateNetCents < 0)
            {
                return "Owner read: the route is readable, but costs are outrunning revenue. Watch several weeks before heavy tuning.";
            }

            return $"Owner read: {descriptor.OwnerAdviceHealthy}";
        }

        private string ResolveStatusV4(BusinessInstanceState business, BusinessOperatingReadoutFamily family)
        {
            BusinessRuntimeState runtime = business.RuntimeState;
            if (!string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason))
            {
                return "blocked / strained";
            }

            if (runtime.FilledWorkerCount < runtime.RequiredWorkerCount)
            {
                return "understaffed";
            }

            if (GetOperatingReserveCashShortfallCentsV4(business) > 0)
            {
                return "cash thin";
            }

            if (runtime.StockHealth01 <= 0.15f && runtime.TotalTargetStockUnits > 0)
            {
                return "stock critical";
            }

            if (runtime.WeekToDateNetCents < 0)
            {
                return "losing money";
            }

            return "operating";
        }

        private int EstimateDailyServiceCapacityV4(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return 0;
            }

            if (business.BaselineDailyServiceCapacity > 0)
            {
                return business.BaselineDailyServiceCapacity;
            }

            return business.RuntimeState.Capacity != null
                ? Mathf.Max(0, business.RuntimeState.Capacity.ServiceCapacityVisitsPerDay)
                : 0;
        }

        private int EstimateWeeklyServiceCapacityV4(BusinessInstanceState business)
        {
            return Mathf.Max(0, Mathf.RoundToInt(EstimateDailyServiceCapacityV4(business) * 6f * GetHealthAdjustedOperatingEfficiency01(business)));
        }

        private int GetOperatingReserveCashShortfallCentsV4(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return 0;
            }

            int reserve = Mathf.Max(0, GetSharedOperatingCashBufferCents(business));
            return Mathf.Max(0, reserve - business.RuntimeState.CurrentCashCents);
        }

        private CategoryStockState FindRuntimeCategoryV4(BusinessInstanceState business, string categoryId)
        {
            if (business == null || business.RuntimeState == null || string.IsNullOrWhiteSpace(categoryId))
            {
                return null;
            }

            IReadOnlyList<CategoryStockState> stock = business.RuntimeState.CategoryStock;
            for (int i = 0; i < stock.Count; i++)
            {
                CategoryStockState category = stock[i];
                if (category != null && string.Equals(category.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }

            return null;
        }

        private string BlockedOrClearV4(BusinessRuntimeState runtime)
        {
            return runtime != null && !string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason)
                ? runtime.LastWeeklyBlockedReason
                : "none";
        }

        private static string BuildStockStatusV4(CategoryStockState category)
        {
            if (category == null)
            {
                return "unknown";
            }

            string status = category.CurrentStockUnits <= 0
                ? "Out"
                : category.TargetStockUnits <= 0
                    ? "Tracked"
                    : category.StockHealth01 <= 0.25f
                        ? "Low"
                        : category.StockHealth01 <= 0.5f
                            ? "Thin"
                            : "Steady";

            if (category.PendingReorderUnits > 0)
            {
                status += $", reorder {category.PendingReorderUnits}";
            }

            return status;
        }

        private static string ColorStatusV4(string status, float health01)
        {
            string color = health01 <= 0f
                ? "#D98282"
                : health01 <= 0.5f
                    ? "#D8B35A"
                    : "#8FCB8F";
            return $"<color={color}>{status}</color>";
        }

        private static string FitV4(string value, int width)
        {
            string text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            int safeWidth = Mathf.Max(1, width);
            if (text.Length > safeWidth - 1)
            {
                text = text.Substring(0, Mathf.Max(0, safeWidth - 2)) + ".";
            }

            return text.PadRight(safeWidth);
        }

        private static string NormalizeCategoryIdV4(string categoryId)
        {
            return string.IsNullOrWhiteSpace(categoryId) ? string.Empty : categoryId.Trim();
        }
    }
}
