// Generic Business Reconstruction, section 3.
// Business templates: useful presets, never grants.
// A template SUGGESTS a catalog, equipment, production methods,
// workflows, functional space, staffing suggestions, and procurement
// policies. Applying a template creates NO cash, inventory, equipment,
// or workers. The business must still acquire everything through
// legitimate purchases, contributions, transfers, production, or
// financing (see GenericBusinessFormation). Staffing suggestions are
// suggestions only: hiring need is derived from task backlog, never
// from a template headcount (see the task-system audits).
// All template data here is caller/author data, not canon. Prices,
// rates, and quantities in tests are test parameters only.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public sealed class GenericBusinessTemplate
    {
        public string TemplateId = string.Empty;
        public string DisplayName = string.Empty;
        public List<BusinessCapabilityKind> SuggestedCapabilities = new List<BusinessCapabilityKind>();
        public List<string> SuggestedRetailProductIds = new List<string>();
        public List<string> SuggestedEquipmentIds = new List<string>();
        public List<string> SuggestedProductionMethodIds = new List<string>();
        public List<string> SuggestedServiceIds = new List<string>();
        public List<string> SuggestedWorkspaceIds = new List<string>();
        public float SuggestedMinimumFloorAreaSqM;
        public List<string> SuggestedStaffingRoles = new List<string>();
        public string SuggestedProcurementPolicy = string.Empty;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// Result of applying a template: a configuration the owner can
    /// review and then fulfil through real acquisition. The applied
    /// configuration itself contains no stock, cash, equipment, or
    /// workers; every "suggested" list is a shopping / hiring / build
    /// list, not a grant.
    /// </summary>
    public sealed class AppliedTemplate
    {
        public string TemplateId = string.Empty;
        public GenericBusinessCapabilitySet CapabilitySet = new GenericBusinessCapabilitySet();
        public List<string> EquipmentToAcquire = new List<string>();
        public List<string> WorkspacesToProvide = new List<string>();
        public float MinimumFloorAreaSqM;
        public List<string> StaffingRolesSuggested = new List<string>();
        public string ProcurementPolicy = string.Empty;
    }

    public static class GenericBusinessTemplates
    {
        public static GenericBusinessTemplate GeneralStore()
        {
            return new GenericBusinessTemplate
            {
                TemplateId = "template.general-store",
                DisplayName = "General Store",
                SuggestedCapabilities = new List<BusinessCapabilityKind> { BusinessCapabilityKind.SellGoods },
                SuggestedRetailProductIds = new List<string> { "flour", "bread", "potatoes", "preserved-food", "fuel-wood", "soap", "salt", "sugar", "coffee", "tea" },
                SuggestedEquipmentIds = new List<string> { "shelving", "counter", "scales" },
                SuggestedWorkspaceIds = new List<string> { "storefront", "back-storage" },
                SuggestedMinimumFloorAreaSqM = 0f,
                SuggestedStaffingRoles = new List<string> { "storekeeper" },
                SuggestedProcurementPolicy = "off-map supplier orders with seller freight surcharge; reorder from the same supplier where possible",
                Notes = "Catalog is a suggestion, not a stock grant. Shelf vs back storage is enforced by the ordering UI in a later pass; this template only names the workspaces.",
            };
        }

        public static GenericBusinessTemplate Bakery()
        {
            return new GenericBusinessTemplate
            {
                TemplateId = "template.bakery",
                DisplayName = "Bakery",
                SuggestedCapabilities = new List<BusinessCapabilityKind> { BusinessCapabilityKind.MakeProducts, BusinessCapabilityKind.SellGoods },
                SuggestedRetailProductIds = new List<string> { "bread", "rolls", "pie" },
                SuggestedEquipmentIds = new List<string> { "oven", "baker-hand-kit" },
                SuggestedProductionMethodIds = new List<string> { "bakery.bread-batch", "bakery.rolls-batch", "bakery.pie-batch" },
                SuggestedServiceIds = new List<string>(),
                SuggestedWorkspaceIds = new List<string> { "preparation", "oven-space" },
                SuggestedStaffingRoles = new List<string> { "baker" },
                SuggestedProcurementPolicy = "flour and notions from off-map suppliers; fuel from the local fuel dealer where available",
                Notes = "Enabling all three bakery methods is a choice, not a default obligation; the owner enables whichever methods they want.",
            };
        }

        public static GenericBusinessTemplate FuelDealer()
        {
            return new GenericBusinessTemplate
            {
                TemplateId = "template.fuel-dealer",
                DisplayName = "Fuel Dealer",
                SuggestedCapabilities = new List<BusinessCapabilityKind> { BusinessCapabilityKind.SellGoods },
                SuggestedRetailProductIds = new List<string> { "fuel-wood" },
                SuggestedEquipmentIds = new List<string> { "saw", "wagon", "splitting-tools" },
                SuggestedWorkspaceIds = new List<string> { "fuel-yard" },
                SuggestedStaffingRoles = new List<string> { "fuel-worker" },
                SuggestedProcurementPolicy = "timber and offcuts from logging and sawmill supply; sell by the unit, not by a fixed weekly quota",
            };
        }

        public static GenericBusinessTemplate Barber()
        {
            return new GenericBusinessTemplate
            {
                TemplateId = "template.barber",
                DisplayName = "Barber",
                SuggestedCapabilities = new List<BusinessCapabilityKind> { BusinessCapabilityKind.OfferServices },
                SuggestedEquipmentIds = new List<string> { "barber-chair", "barber-cutting-tools" },
                SuggestedServiceIds = new List<string> { "barber-haircut", "barber-shave", "barber-bath" },
                SuggestedWorkspaceIds = new List<string> { "barber-shop" },
                SuggestedStaffingRoles = new List<string> { "barber" },
                SuggestedProcurementPolicy = "period-appropriate cutting tools only; no modern electric equipment",
                Notes = "Barber tools are period-appropriate hand tools. A chair, tools, sufficient usable space, and a qualified person are the feasibility gates; the template grants none of them.",
            };
        }

        public static IReadOnlyList<GenericBusinessTemplate> All()
        {
            return new[] { GeneralStore(), Bakery(), FuelDealer(), Barber() };
        }

        public static GenericBusinessTemplate Find(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return null;
            foreach (GenericBusinessTemplate t in All())
            {
                if (string.Equals(t.TemplateId, templateId.Trim(), StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        /// <summary>
        /// Apply a template to a fresh capability set. This is
        /// configuration only: it returns what to acquire and what to
        /// enable, and it creates no stock, cash, equipment, or workers.
        /// Callers must still acquire every suggested item legitimately.
        /// </summary>
        public static AppliedTemplate Apply(GenericBusinessTemplate template)
        {
            var applied = new AppliedTemplate();
            if (template == null) return applied;
            applied.TemplateId = template.TemplateId;
            foreach (BusinessCapabilityKind kind in template.SuggestedCapabilities)
            {
                applied.CapabilitySet.TryAddCapability(kind, out _);
            }
            applied.CapabilitySet.TryExpandRetailCatalog(template.SuggestedRetailProductIds, out _);
            foreach (string methodId in template.SuggestedProductionMethodIds)
            {
                applied.CapabilitySet.TryEnableProductionMethod(methodId, out _);
            }
            foreach (string serviceId in template.SuggestedServiceIds)
            {
                applied.CapabilitySet.TryEnableService(serviceId, out _);
            }
            applied.EquipmentToAcquire = new List<string>(template.SuggestedEquipmentIds);
            applied.WorkspacesToProvide = new List<string>(template.SuggestedWorkspaceIds);
            applied.MinimumFloorAreaSqM = template.SuggestedMinimumFloorAreaSqM;
            applied.StaffingRolesSuggested = new List<string>(template.SuggestedStaffingRoles);
            applied.ProcurementPolicy = template.SuggestedProcurementPolicy;
            return applied;
        }
    }
}
