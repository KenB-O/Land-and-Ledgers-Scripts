// Generic Business Reconstruction, sections 2 and 4.
// One business, many capabilities, one legal/economic principal.
// This set is the plain-C# capability state for a single business:
// what the owner has ENABLED (configured), not what the business can
// actually perform right now. Performance is decided at execution time
// by CapabilityFeasibility (equipment, workspace, premises right,
// people, materials, authority), following the existing pattern that
// capability registration never grants permission to act
// (BusinessCapabilityRegistry, BusinessExpansion).
// No money, inventory, equipment, or workers are created here.
// Adding a capability never duplicates ownership, assets, money, or
// accounting: the business identity, ledger, and owner are untouched
// by this class (Unity AI maps this set onto BusinessInstanceState
// via GenericBusinessUnityAdapter; see the handoff doc).
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>
    /// Inputs a caller must actually supply for a feasibility check.
    /// Every list is caller data from real state (owned equipment ids,
    /// workspace ids, on-hand inventory, qualified person skill ids,
    /// premises right). Nothing here is invented by this class.
    /// </summary>
    public sealed class CapabilityFeasibilityInput
    {
        public List<string> OwnedEquipmentIds = new List<string>();
        public List<string> AvailableWorkspaceIds = new List<string>();
        public Dictionary<string, float> OnHandQuantities = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public List<string> QualifiedPersonSkillIds = new List<string>();
        public bool HasPremisesRight;
        public bool HasRequiredAuthority = true;
        public string PremisesRightKind = string.Empty;
    }

    public sealed class GenericBusinessCapabilitySet
    {
        private readonly List<BusinessCapabilityKind> enabledKinds = new List<BusinessCapabilityKind>();
        private readonly List<string> retailProductIds = new List<string>();
        private readonly List<string> enabledProductionMethodIds = new List<string>();
        private readonly List<string> enabledServiceIds = new List<string>();

        public IReadOnlyList<BusinessCapabilityKind> EnabledKinds => enabledKinds;
        public IReadOnlyList<string> RetailProductIds => retailProductIds;
        public IReadOnlyList<string> EnabledProductionMethodIds => enabledProductionMethodIds;
        public IReadOnlyList<string> EnabledServiceIds => enabledServiceIds;

        public bool HasKind(BusinessCapabilityKind kind)
        {
            return kind != BusinessCapabilityKind.Unspecified && enabledKinds.Contains(kind);
        }

        /// <summary>
        /// Add to Business, section 4. Idempotent: adding a kind that is
        /// already enabled is a no-op success, never a duplicate operation.
        /// Adding a kind does NOT guarantee performance; see
        /// CheckFeasibility. Returns false only for Unspecified.
        /// </summary>
        public bool TryAddCapability(BusinessCapabilityKind kind, out string reason)
        {
            reason = string.Empty;
            if (kind == BusinessCapabilityKind.Unspecified)
            {
                reason = "Cannot enable an unspecified capability.";
                return false;
            }
            if (!enabledKinds.Contains(kind))
            {
                enabledKinds.Add(kind);
            }
            return true;
        }

        /// <summary>
        /// Retail catalog expansion, section 4. A business with retail
        /// already enabled expands its catalog in place: existing product
        /// ids are kept, new ids are added, and no second retail operation
        /// is created. If retail is not yet enabled, enabling it by
        /// expanding the catalog is the same single operation.
        /// Empty/duplicate ids are ignored, never duplicated.
        /// </summary>
        public bool TryExpandRetailCatalog(IEnumerable<string> productIds, out string reason)
        {
            reason = string.Empty;
            if (productIds == null)
            {
                reason = "No products supplied for catalog expansion.";
                return false;
            }
            int added = 0;
            foreach (string id in productIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                string trimmed = id.Trim();
                bool exists = false;
                foreach (string existing in retailProductIds)
                {
                    if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                }
                if (!exists) { retailProductIds.Add(trimmed); added++; }
            }
            if (added == 0)
            {
                reason = "Catalog unchanged: no new products supplied.";
                return true;
            }
            TryAddCapability(BusinessCapabilityKind.SellGoods, out _);
            reason = $"Added {added} product(s) to the retail catalog.";
            return true;
        }

        public bool HasRetailProduct(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return false;
            foreach (string id in retailProductIds)
            {
                if (string.Equals(id, productId.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Enable a production method on this business (owner chooses which
        /// production activities to enable, section 7). Idempotent; a method
        /// already enabled is not duplicated. Enabling does not check
        /// inputs or equipment; that is CheckProductionFeasibility.
        /// </summary>
        public bool TryEnableProductionMethod(string methodId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(methodId))
            {
                reason = "Production method needs an id.";
                return false;
            }
            TryAddCapability(BusinessCapabilityKind.MakeProducts, out _);
            string trimmed = methodId.Trim();
            foreach (string existing in enabledProductionMethodIds)
            {
                if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Production method already enabled.";
                    return true;
                }
            }
            enabledProductionMethodIds.Add(trimmed);
            return true;
        }

        /// <summary>
        /// Enable a service (e.g. barber haircut/shave/bath) on this
        /// business. Idempotent. The barber example from the pass prompt:
        /// a retailer that adds "barber-haircut" keeps its single retail
        /// operation and gains one service; it does not become a second
        /// business, and it cannot perform the service until feasibility
        /// passes (chair, period tools, space, qualified person).
        /// </summary>
        public bool TryEnableService(string serviceId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                reason = "Service needs an id.";
                return false;
            }
            TryAddCapability(BusinessCapabilityKind.OfferServices, out _);
            string trimmed = serviceId.Trim();
            foreach (string existing in enabledServiceIds)
            {
                if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Service already enabled.";
                    return true;
                }
            }
            enabledServiceIds.Add(trimmed);
            return true;
        }

        /// <summary>
        /// Feasibility for a production recipe: every requirement must be
        /// met by caller-supplied real state, or the method is refused
        /// with named reasons. This never grants, creates, or invents
        /// anything; it only reports whether the business, as currently
        /// equipped and stocked, can run the method.
        /// </summary>
        public static bool CheckProductionFeasibility(
            ProductionRecipeDetail recipe,
            CapabilityFeasibilityInput input,
            out List<string> reasons)
        {
            reasons = new List<string>();
            if (recipe == null)
            {
                reasons.Add("No production recipe supplied.");
                return false;
            }
            if (input == null)
            {
                reasons.Add("No feasibility input supplied.");
                return false;
            }
            if (!input.HasPremisesRight)
            {
                reasons.Add("Business has no premises right for this operation.");
            }
            if (!input.HasRequiredAuthority)
            {
                reasons.Add("Business lacks the required legal authority for this operation.");
            }
            foreach (string equipment in recipe.RequiredEquipmentIds)
            {
                if (string.IsNullOrWhiteSpace(equipment)) continue;
                bool owned = false;
                foreach (string ownedId in input.OwnedEquipmentIds)
                {
                    if (string.Equals(ownedId, equipment, StringComparison.OrdinalIgnoreCase)) { owned = true; break; }
                }
                if (!owned) reasons.Add($"Missing equipment: {equipment}.");
            }
            foreach (string workspace in recipe.RequiredWorkspaceIds)
            {
                if (string.IsNullOrWhiteSpace(workspace)) continue;
                bool available = false;
                foreach (string ws in input.AvailableWorkspaceIds)
                {
                    if (string.Equals(ws, workspace, StringComparison.OrdinalIgnoreCase)) { available = true; break; }
                }
                if (!available) reasons.Add($"Missing workspace: {workspace}.");
            }
            foreach (ProductionRecipeInput inputReq in recipe.Inputs)
            {
                if (inputReq == null || string.IsNullOrWhiteSpace(inputReq.ProductId) || inputReq.Quantity <= 0f) continue;
                float onHand = 0f;
                if (input.OnHandQuantities != null)
                {
                    input.OnHandQuantities.TryGetValue(inputReq.ProductId, out onHand);
                }
                if (onHand + 0.001f < inputReq.Quantity)
                {
                    reasons.Add($"Insufficient {inputReq.ProductId}: need {inputReq.Quantity}, have {onHand}.");
                }
            }
            if (!string.IsNullOrWhiteSpace(recipe.PerformanceSkillId))
            {
                bool qualified = false;
                foreach (string skill in input.QualifiedPersonSkillIds)
                {
                    if (string.Equals(skill, recipe.PerformanceSkillId, StringComparison.OrdinalIgnoreCase)) { qualified = true; break; }
                }
                if (!qualified) reasons.Add($"No qualified person with skill: {recipe.PerformanceSkillId}.");
            }
            return reasons.Count == 0;
        }
    }
}
