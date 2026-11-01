// Generic Business Reconstruction, section 6.
// Starting stock and business formation: new vs existing, kept apart.
// A NEWLY formed business begins with zero magically generated
// inventory and zero granted cash. It acquires goods only through
// legitimate, recorded transactions: a purchase, an owner
// contribution, a transfer, production, or a financing/contractual
// arrangement. This class records those transactions; it never
// invents them.
// An EXISTING scenario business may begin with documented starting
// inventory/assets, but only as a scenario endowment: identified,
// with provenance, explicitly labelled as an endowment. That is a
// scenario input, not a formation grant, and never canon.
// This is NOT the old six-person slice "zero starting capital" rule
// (rejected by Kennedy as canon, Nov 1, 2026): slice seeds are
// configurable test inputs / scenario endowments, variable between
// runs. This formation gate applies to new formation in general;
// scenario endowments remain the documented exception, not a rule
// that every business starts at zero in every scenario.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public sealed class OwnerContribution
    {
        public string Source = string.Empty;
        public string AssetOrProductId = string.Empty;
        public float Quantity;
        public int CashCents;
        public string Provenance = string.Empty;
    }

    public sealed class ScenarioEndowmentItem
    {
        public string ItemId = string.Empty;
        public string LotOrAssetId = string.Empty;
        public float Quantity;
        public string Provenance = string.Empty;
    }

    public sealed class ScenarioEndowment
    {
        public string EndowmentId = string.Empty;
        public string ScenarioId = string.Empty;
        public string Provenance = string.Empty;
        public List<ScenarioEndowmentItem> Items = new List<ScenarioEndowmentItem>();
        public int CashCents;
    }

    public sealed class NewBusinessRecord
    {
        public string BusinessId = string.Empty;
        public string DisplayName = string.Empty;
        public GenericBusinessCapabilitySet Capabilities = new GenericBusinessCapabilitySet();
        public List<OwnerContribution> Contributions = new List<OwnerContribution>();
        public Dictionary<string, float> InventoryQuantities = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public int CashCents;
        public bool IsScenarioEndowment;
        public string EndowmentId = string.Empty;
        public string EndowmentProvenance = string.Empty;

        public float InventoryQuantity(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return 0f;
            float qty = 0f;
            InventoryQuantities.TryGetValue(productId.Trim(), out qty);
            return qty;
        }
    }

    public static class GenericBusinessFormation
    {
        /// <summary>
        /// Form a NEW business. Starts at zero inventory and zero cash,
        /// then applies ONLY the caller-supplied legitimate contributions.
        /// A contribution with no source and no provenance is refused:
        /// that is exactly the magical-inventory path this gate exists
        /// to block. Returns false and changes nothing on refusal.
        /// </summary>
        public static bool TryFormNewBusiness(
            string businessId, string displayName,
            IEnumerable<OwnerContribution> contributions,
            out NewBusinessRecord record, out string reason)
        {
            record = null;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(businessId))
            {
                reason = "New business needs an id.";
                return false;
            }
            var candidate = new NewBusinessRecord
            {
                BusinessId = businessId.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? businessId.Trim() : displayName.Trim(),
                IsScenarioEndowment = false,
            };
            if (contributions != null)
            {
                foreach (OwnerContribution c in contributions)
                {
                    if (c == null) continue;
                    if (string.IsNullOrWhiteSpace(c.Source) || string.IsNullOrWhiteSpace(c.Provenance))
                    {
                        reason = "Owner contribution refused: source and provenance are required; contributions without them would be invented stock or cash.";
                        return false;
                    }
                    candidate.Contributions.Add(c);
                    candidate.CashCents += Math.Max(0, c.CashCents);
                    if (!string.IsNullOrWhiteSpace(c.AssetOrProductId) && c.Quantity > 0f)
                    {
                        float existing = 0f;
                        candidate.InventoryQuantities.TryGetValue(c.AssetOrProductId.Trim(), out existing);
                        candidate.InventoryQuantities[c.AssetOrProductId.Trim()] = existing + c.Quantity;
                    }
                }
            }
            record = candidate;
            return true;
        }

        /// <summary>
        /// Begin an EXISTING scenario business with documented starting
        /// assets. Every item needs an identity (lot/asset id) and
        /// provenance, and the endowment itself needs an id, scenario,
        /// and provenance. Undocumented items are refused, not silently
        /// dropped, so a scenario can never smuggle in free stock.
        /// </summary>
        public static bool TryBeginScenarioBusiness(
            string businessId, string displayName, ScenarioEndowment endowment,
            out NewBusinessRecord record, out string reason)
        {
            record = null;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(businessId))
            {
                reason = "Scenario business needs an id.";
                return false;
            }
            if (endowment == null || string.IsNullOrWhiteSpace(endowment.EndowmentId)
                || string.IsNullOrWhiteSpace(endowment.ScenarioId)
                || string.IsNullOrWhiteSpace(endowment.Provenance))
            {
                reason = "Scenario endowment refused: endowment id, scenario id, and provenance are all required.";
                return false;
            }
            var candidate = new NewBusinessRecord
            {
                BusinessId = businessId.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? businessId.Trim() : displayName.Trim(),
                IsScenarioEndowment = true,
                EndowmentId = endowment.EndowmentId.Trim(),
                EndowmentProvenance = endowment.Provenance,
                CashCents = Math.Max(0, endowment.CashCents),
            };
            foreach (ScenarioEndowmentItem item in endowment.Items)
            {
                if (item == null) continue;
                if (string.IsNullOrWhiteSpace(item.ItemId) || string.IsNullOrWhiteSpace(item.LotOrAssetId)
                    || string.IsNullOrWhiteSpace(item.Provenance) || item.Quantity <= 0f)
                {
                    reason = "Scenario endowment item refused: every item needs an item id, a lot/asset id, provenance, and a positive quantity.";
                    return false;
                }
                float existing = 0f;
                candidate.InventoryQuantities.TryGetValue(item.ItemId.Trim(), out existing);
                candidate.InventoryQuantities[item.ItemId.Trim()] = existing + item.Quantity;
            }
            record = candidate;
            return true;
        }
    }
}
