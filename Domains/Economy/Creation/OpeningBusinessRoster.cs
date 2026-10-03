using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// BIZ-1: one authored opening business. An explicit authored entry — type, name,
    /// owner, premises needs, starting capital — replacing the old synthetic count-based
    /// auto-seed (Canon §3.2, GHOST-DES-009). "How many businesses open in 1870" is now
    /// an authored roster, never a population formula (Tech X §3.3).
    /// </summary>
    [Serializable]
    public sealed class OpeningRosterEntry
    {
        [SerializeField]
        private BusinessType businessType = BusinessType.GeneralStore;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField]
        private string ownerDisplayName = "Town";

        [SerializeField]
        private PremisesRequirement premisesRequirement = new PremisesRequirement();

        [SerializeField, Min(0)]
        private int startingCapitalCents;

        public BusinessType BusinessType => businessType;
        public string DisplayName => displayName ?? string.Empty;
        public string OwnerDisplayName => ownerDisplayName ?? "Town";
        public PremisesRequirement PremisesRequirement => premisesRequirement ?? new PremisesRequirement();
        public int StartingCapitalCents => Mathf.Max(0, startingCapitalCents);

        public OpeningRosterEntry(
            BusinessType businessType,
            string displayName,
            string ownerDisplayName,
            PremisesRequirement premisesRequirement,
            int startingCapitalCents = 0)
        {
            this.businessType = businessType;
            this.displayName = displayName ?? string.Empty;
            this.ownerDisplayName = ownerDisplayName ?? "Town";
            this.premisesRequirement = premisesRequirement ?? new PremisesRequirement();
            this.startingCapitalCents = Mathf.Max(0, startingCapitalCents);
        }

        public CreateBusinessIntent ToIntent()
        {
            return new CreateBusinessIntent(
                businessType,
                displayName,
                BusinessOwnership.Sole(BusinessOwnerIdentity.Npc(-1, ownerDisplayName, ownerDisplayName), startingCapitalCents),
                premisesRequirement,
                PremisesPreference.Auto,
                startingCapitalCents);
        }
    }

    /// <summary>
    /// BIZ-1: the authored 1870 opening roster. The ten core town business types open
    /// through the formation workflow (entity + ownership + premises + capital), each as
    /// an explicit entry — not a "seed N businesses" count (Canon §3.2, Tech X §3.3,
    /// GHOST-DES-009). Kennedy edits this roster to change the opening town.
    /// </summary>
    public static class OpeningBusinessRoster
    {
        /// <summary>The default authored opening town. Explicit entries, not formulas.</summary>
        public static List<OpeningRosterEntry> Default()
        {
            return new List<OpeningRosterEntry>
            {
                new OpeningRosterEntry(BusinessType.GeneralStore, "Opening General Store", "Town",
                    new PremisesRequirement(customerFacing: true, minAreaSqFt: 800), 15000),
                new OpeningRosterEntry(BusinessType.Blacksmith, "Opening Blacksmith", "Town",
                    new PremisesRequirement(workshop: true, yardStorage: true, minAreaSqFt: 600), 8000),
                new OpeningRosterEntry(BusinessType.Butcher, "Opening Butcher", "Town",
                    new PremisesRequirement(customerFacing: true, foodHandling: true, minAreaSqFt: 500), 8000),
                new OpeningRosterEntry(BusinessType.LiveryFreight, "Opening Livery & Freight", "Town",
                    new PremisesRequirement(animalHousing: true, yardStorage: true, minAreaSqFt: 2000), 12000),
                new OpeningRosterEntry(BusinessType.Doctor, "Opening Doctor", "Town",
                    new PremisesRequirement(customerFacing: true, minAreaSqFt: 400), 5000),
                new OpeningRosterEntry(BusinessType.Sawmill, "Opening Sawmill", "Town",
                    new PremisesRequirement(workshop: true, yardStorage: true, minAreaSqFt: 3000), 10000),
                new OpeningRosterEntry(BusinessType.LumberYard, "Opening Lumber Yard", "Town",
                    new PremisesRequirement(yardStorage: true, minAreaSqFt: 4000), 8000),
                new OpeningRosterEntry(BusinessType.BoardingHouse, "Opening Boarding House", "Town",
                    new PremisesRequirement(customerFacing: true, minAreaSqFt: 1200), 8000),
                new OpeningRosterEntry(BusinessType.Ranch, "Opening Ranch", "Town",
                    new PremisesRequirement(animalHousing: true, minAreaSqFt: 10000), 15000),
                new OpeningRosterEntry(BusinessType.CropFarm, "Opening Crop Farm", "Town",
                    new PremisesRequirement(minAreaSqFt: 20000), 10000),
            };
        }

        /// <summary>
        /// Creates each roster entry through the formation workflow, skipping types that
        /// already exist. Returns the number created. Failures are diagnostic, never fatal.
        /// </summary>
        public static int EnsureViaFormationWorkflow(
            List<OpeningRosterEntry> roster,
            BusinessCreationAuthority authority,
            IBusinessCreationContext context,
            Func<BusinessType, bool> hasBusinessOfType,
            Action<BusinessCreationResult> onCreated,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (roster == null || authority == null || context == null)
            {
                diagnostics.Add("Roster, authority, or context missing; opening formation skipped.");
                return 0;
            }

            int created = 0;
            foreach (OpeningRosterEntry entry in roster)
            {
                if (entry == null)
                {
                    continue;
                }

                if (hasBusinessOfType != null && hasBusinessOfType(entry.BusinessType))
                {
                    diagnostics.Add($"'{entry.DisplayName}' skipped: a {entry.BusinessType} already exists.");
                    continue;
                }

                if (!authority.TryCreate(entry.ToIntent(), context, out BusinessCreationResult result)
                    || result == null || !result.Success)
                {
                    diagnostics.Add($"'{entry.DisplayName}' failed formation: " +
                        string.Join("; ", result?.Diagnostics ?? new List<string>()));
                    continue;
                }

                onCreated?.Invoke(result);
                created++;
                diagnostics.Add($"'{entry.DisplayName}' formed via workflow as {result.BusinessEntityId} " +
                    $"(premises: {result.Premises.Kind}; NOT operating until commerce — Canon §3.2).");
            }

            return created;
        }
    }
}
