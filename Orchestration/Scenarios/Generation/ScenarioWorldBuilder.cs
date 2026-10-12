using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1/P1-5 (Worker 1): deterministic, scene-independent world
    /// generation port. Builds a coherent starting world from (preset, seed,
    /// start day, partitioned streams): households and residents honoring the
    /// preset population range, fixed + variable businesses, property grants,
    /// then the starting economy through the domain authorities via
    /// <see cref="ScenarioEconomySeeder"/>.
    ///
    /// Used for deterministic preset previews, harness tests, and slice L.
    /// Production Unity generation uses the PopulationGenerator-backed port
    /// (ScenarioUnityWiring); both feed the same seeder and the same
    /// provenance tags, so authored history is always distinguishable from
    /// simulated post-start events.
    ///
    /// Constraint violations retry with attempt-partitioned streams (bounded);
    /// the builder never invents money, houses, or businesses to force validity.
    /// </summary>
    public sealed class ScenarioWorldBuilder : IScenarioGenerationPort
    {
        private static readonly string[] Surnames =
        {
            "Hale", "Bennett", "Whitaker", "Carver", "Mercer", "Dawson", "Reed",
            "Bell", "Sutter", "Cole", "Fletcher", "Hobbs", "Marsh", "Beckett",
            "Quinn", "Ashford",
        };

        private static readonly string[] MaleFirstNames =
        {
            "Thomas", "Samuel", "Edwin", "Caleb", "Silas", "Jonah",
            "Nathaniel", "Isaac", "Walter", "Henry",
        };

        private static readonly string[] FemaleFirstNames =
        {
            "Clara", "Martha", "Ada", "Eleanor", "Josephine",
            "Rose", "Lydia", "Nora", "Abigail", "Mae",
        };

        private static readonly string[] VariableBusinessTypes =
        {
            "joinery", "livery", "bakery", "tailor", "assayer",
        };

        private static readonly string[] ProfessionIds =
        {
            "clerk", "blacksmith", "carpenter", "teamster",
            "shopkeeper", "saloon-hand", "farmhand", "seamstress",
        };

        private static readonly string[] ProfessionNames =
        {
            "Clerk", "Blacksmith", "Carpenter", "Teamster",
            "Shopkeeper", "Saloon Hand", "Farmhand", "Seamstress",
        };

        private readonly ScenarioEconomyAuthorityPorts economyPorts;
        private readonly IScenarioInventoryPort inventoryPort;
        private readonly IScenarioPropertyPort propertyPort;

        private int generationCalls;

        public int GenerationCalls => generationCalls;

        public ScenarioWorldBuilder(
            ScenarioEconomyAuthorityPorts economyPorts,
            IScenarioInventoryPort inventoryPort = null,
            IScenarioPropertyPort propertyPort = null)
        {
            this.economyPorts = economyPorts ?? throw new ArgumentNullException(nameof(economyPorts));
            this.inventoryPort = inventoryPort;
            this.propertyPort = propertyPort;
        }

        public ScenarioGenerationOutcome Generate(
            ScenarioPreset preset,
            int seed,
            int startDayIndex,
            GenerationRngStreams rng,
            List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (preset == null)
            {
                diagnostics.Add("ScenarioWorldBuilder: preset is null; nothing generated.");
                return FailedOutcome("null preset");
            }

            StartingWorldConfiguration world = preset.StartingWorld;
            if (world == null)
            {
                diagnostics.Add($"ScenarioWorldBuilder: preset '{preset.PresetId}' has no StartingWorld; nothing generated.");
                return FailedOutcome("missing starting world");
            }

            // Structural attempts: rebuild the draft until constraints hold.
            // Seeding happens once, after the draft is accepted, because
            // re-seeding identical inputs into live authorities would double-post.
            ScenarioSeedPopulation draft = null;
            GenerationRngStreams attemptRng = rng;
            int attempt = 0;
            for (; attempt < GenerationRngStreams.MaxAttempts; attempt++)
            {
                generationCalls++;
                draft = BuildDraft(preset, world, seed, startDayIndex, attemptRng);
                string violation = ValidateDraft(draft, world);
                if (violation == null)
                {
                    break;
                }

                diagnostics.Add($"Attempt {attempt + 1}: {violation} Retrying with repartitioned streams.");
                attemptRng = attemptRng.NextAttempt();
                draft = null;
            }

            if (draft == null)
            {
                diagnostics.Add($"ScenarioWorldBuilder: preset '{preset.PresetId}' seed {seed} failed " +
                    $"all {GenerationRngStreams.MaxAttempts} bounded attempts. No money, houses, or " +
                    "businesses were invented to force validity; loosen the preset constraints instead.");
                return FailedOutcome("constraint violations on every attempt");
            }

            ScenarioEconomySeeder.SeedReport seedReport = ScenarioEconomySeeder.Seed(
                draft, preset, economyPorts, inventoryPort, propertyPort, attemptRng,
                startDayIndex, diagnostics);

            string fingerprint = ComputeWorldFingerprint(draft, seedReport, preset, seed, startDayIndex);
            var outcome = new ScenarioGenerationOutcome
            {
                Success = true,
                WorldFingerprint = fingerprint,
                ResidentCount = draft.ResidentCount,
                HouseholdCount = draft.Households.Count,
                BusinessCount = draft.Businesses.Count,
                AttemptUsed = attempt,
                Summary = $"Seed {seed}: {draft.Households.Count} households, {draft.ResidentCount} residents, " +
                    $"{draft.Businesses.Count} businesses, {seedReport.TotalCashCents}c starting cash, " +
                    $"{seedReport.ObligationCount} obligations, {seedReport.EmploymentCount} employments.",
            };
            outcome.ProvenanceTags.Add(ScenarioCampaignRecord.BuildProvenanceTag(
                preset.PresetId, world.DefinitionVersion, seed));
            return outcome;
        }

        private static ScenarioGenerationOutcome FailedOutcome(string reason)
        {
            return new ScenarioGenerationOutcome { Success = false, Summary = reason };
        }

        // ---------- draft construction (deterministic) ----------

        private static ScenarioSeedPopulation BuildDraft(
            ScenarioPreset preset,
            StartingWorldConfiguration world,
            int seed,
            int startDayIndex,
            GenerationRngStreams rng)
        {
            var draft = new ScenarioSeedPopulation();
            Random popRng = rng.Stream(GenerationConcern.Population);
            Random compRng = rng.Stream(GenerationConcern.HouseholdComposition);
            Random bizRng = rng.Stream(GenerationConcern.BusinessFormation);
            Random playerRng = rng.Stream(GenerationConcern.PlayerStart);

            int spread = Math.Max(0, (world.MaxResidents - world.MinResidents) / 2);
            int target = world.PreferredResidents + (spread > 0 ? popRng.Next(-spread, spread + 1) : 0);
            target = Math.Max(world.MinResidents, Math.Min(world.MaxResidents, target));

            int householdCount = Math.Max(1, (int)Math.Round(target / 4.2));
            householdCount = Math.Max(householdCount, (target + 5) / 6);
            householdCount = Math.Min(householdCount, Math.Min(24, target));

            int nextPersonId = 1;
            int nextHouseholdId = 1;
            int remaining = target;
            for (int h = 0; h < householdCount; h++)
            {
                int remainingHouseholds = householdCount - h;
                int lo = Math.Max(1, remaining - (remainingHouseholds - 1) * 6);
                int hi = Math.Min(6, remaining - (remainingHouseholds - 1) * 1);
                int size = hi <= lo ? lo : lo + compRng.Next(hi - lo + 1);
                remaining -= size;

                var household = new ScenarioSeedHousehold
                {
                    HouseholdId = nextHouseholdId++,
                    Surname = Surnames[compRng.Next(Surnames.Length)],
                    HomeBuildingId = 1000 + h,
                };

                for (int m = 0; m < size; m++)
                {
                    household.Members.Add(BuildPerson(
                        nextPersonId++, household.HouseholdId, household.Surname, m,
                        popRng, playerRng));
                }

                draft.Households.Add(household);
            }

            // Authored player start: the first household is the player's.
            if (draft.Households.Count > 0 && world.PlayerStart != null)
            {
                ScenarioSeedHousehold playerHousehold = draft.Households[0];
                playerHousehold.IsPlayerHousehold = true;
                if (!string.IsNullOrWhiteSpace(world.PlayerStart.HouseholdName))
                {
                    playerHousehold.Surname = world.PlayerStart.HouseholdName;
                }

                draft.PlayerHouseholdId = playerHousehold.HouseholdId;
                int founders = Math.Max(1, world.PlayerStart.FounderCount);
                while (playerHousehold.Members.Count < founders)
                {
                    playerHousehold.Members.Add(BuildPerson(
                        nextPersonId++, playerHousehold.HouseholdId,
                        playerHousehold.Surname, playerHousehold.Members.Count,
                        popRng, playerRng, forceAdult: true));
                }
            }

            // Businesses: fixed authored businesses, then variable ones.
            int buildingId = 2000;
            foreach (ScenarioFixedBusiness fixedBusiness in world.FixedBusinesses)
            {
                if (fixedBusiness == null || string.IsNullOrWhiteSpace(fixedBusiness.BusinessId))
                {
                    continue;
                }

                var business = new ScenarioSeedBusiness
                {
                    BusinessId = fixedBusiness.BusinessId,
                    DisplayName = string.IsNullOrWhiteSpace(fixedBusiness.DisplayName)
                        ? fixedBusiness.BusinessId : fixedBusiness.DisplayName,
                    BusinessType = string.IsNullOrWhiteSpace(fixedBusiness.BusinessType)
                        ? "general" : fixedBusiness.BusinessType,
                    CapitalCents = Math.Max(0, fixedBusiness.CapitalCents),
                    OwnerKind = string.IsNullOrWhiteSpace(fixedBusiness.OwnerKind)
                        ? "npc" : fixedBusiness.OwnerKind,
                    BuildingId = buildingId++,
                };
                AddInventoryGrants(business, bizRng);
                draft.Businesses.Add(business);
            }

            int variableCount = world.MinVariableBusinesses;
            if (world.MaxVariableBusinesses > world.MinVariableBusinesses)
            {
                variableCount += bizRng.Next(world.MaxVariableBusinesses - world.MinVariableBusinesses + 1);
            }

            for (int v = 0; v < variableCount; v++)
            {
                string type = VariableBusinessTypes[bizRng.Next(VariableBusinessTypes.Length)];
                var business = new ScenarioSeedBusiness
                {
                    BusinessId = $"var-{seed}-{v}",
                    DisplayName = $"{Capitalize(type)} #{v + 1}",
                    BusinessType = type,
                    CapitalCents = 50000 + bizRng.Next(150001),
                    OwnerKind = "npc",
                    BuildingId = buildingId++,
                };
                AddInventoryGrants(business, bizRng);
                draft.Businesses.Add(business);
            }

            // Property grants: every household owns its home; authored grants
            // expand on top (buildingId -1 = the grantee household's home).
            int grantIndex = 0;
            foreach (ScenarioSeedHousehold household in draft.Households)
            {
                bool ownsHome = true;
                if (household.IsPlayerHousehold && world.PlayerStart != null)
                {
                    ownsHome = world.PlayerStart.OwnsHomeBuilding;
                }

                if (ownsHome)
                {
                    draft.PropertyGrants.Add(new ScenarioSeedPropertyGrant
                    {
                        GrantId = $"home-{seed}-{grantIndex++}",
                        BuildingId = household.HomeBuildingId,
                        HouseholdId = household.HouseholdId,
                        Notes = "Scenario-authored home ownership at world start.",
                    });
                }
            }

            foreach (ScenarioPropertyGrant authored in world.PropertyGrants)
            {
                if (authored == null)
                {
                    continue;
                }

                if (string.Equals(authored.GranteeKind, "player", StringComparison.OrdinalIgnoreCase))
                {
                    if (draft.PlayerHouseholdId >= 0)
                    {
                        int building = authored.BuildingId >= 0
                            ? authored.BuildingId
                            : draft.Households[0].HomeBuildingId;
                        draft.PropertyGrants.Add(new ScenarioSeedPropertyGrant
                        {
                            GrantId = authored.GrantId,
                            BuildingId = building,
                            HouseholdId = draft.PlayerHouseholdId,
                            Notes = authored.Notes,
                        });
                    }

                    continue;
                }

                foreach (ScenarioSeedHousehold household in draft.Households)
                {
                    if (household.IsPlayerHousehold)
                    {
                        continue;
                    }

                    int building = authored.BuildingId >= 0
                        ? authored.BuildingId
                        : household.HomeBuildingId;
                    draft.PropertyGrants.Add(new ScenarioSeedPropertyGrant
                    {
                        GrantId = $"{authored.GrantId}-{household.HouseholdId}",
                        BuildingId = building,
                        HouseholdId = household.HouseholdId,
                        Notes = authored.Notes,
                    });
                    break;
                }
            }

            return draft;
        }

        private static ScenarioSeedPerson BuildPerson(
            int personId, int householdId, string surname, int memberIndex,
            Random popRng, Random playerRng, bool forceAdult = false)
        {
            bool female = popRng.NextDouble() < 0.5;
            int age;
            if (forceAdult || memberIndex == 0)
            {
                age = 21 + popRng.Next(45);
            }
            else
            {
                double roll = popRng.NextDouble();
                if (roll < 0.35) age = popRng.Next(13);
                else if (roll < 0.50) age = 13 + popRng.Next(5);
                else age = 18 + popRng.Next(53);
            }

            bool workingAge = age >= 16 && age <= 64;
            int professionIndex = popRng.Next(ProfessionIds.Length);
            int wage = 0;
            string professionId = string.Empty;
            string professionName = string.Empty;
            if (workingAge)
            {
                professionId = ProfessionIds[professionIndex];
                professionName = ProfessionNames[professionIndex];
                wage = 700 + playerRng.Next(2501);
                wage -= wage % 50;
            }

            return new ScenarioSeedPerson
            {
                PersonId = personId,
                HouseholdId = householdId,
                FirstName = female
                    ? FemaleFirstNames[popRng.Next(FemaleFirstNames.Length)]
                    : MaleFirstNames[popRng.Next(MaleFirstNames.Length)],
                LastName = surname,
                Age = age,
                IsWorkingAge = workingAge,
                ProfessionId = professionId,
                ProfessionName = professionName,
                WeeklyWageCents = wage,
            };
        }

        private static void AddInventoryGrants(ScenarioSeedBusiness business, Random bizRng)
        {
            string[] products = ProductsFor(business.BusinessType);
            int grantCount = Math.Min(2, products.Length);
            for (int i = 0; i < grantCount; i++)
            {
                business.InventoryGrants.Add(new ScenarioInventoryGrant(
                    products[(bizRng.Next(products.Length) + i) % products.Length],
                    10 + bizRng.Next(91),
                    50 + bizRng.Next(451)));
            }
        }

        private static string[] ProductsFor(string businessType)
        {
            switch ((businessType ?? string.Empty).ToLowerInvariant())
            {
                case "general-store":
                case "general": return new[] { "flour-sack", "coffee-beans", "nails-keg" };
                case "blacksmith": return new[] { "horseshoe-set", "nails-keg", "plowshare" };
                case "saloon": return new[] { "whiskey-bottle", "beer-keg" };
                case "bakery": return new[] { "flour-sack", "bread-loaf" };
                case "livery": return new[] { "oats-bushel", "harness-set" };
                case "joinery": return new[] { "lumber-board", "nails-keg" };
                case "tailor": return new[] { "cloth-bolt", "thread-spool" };
                case "assayer": return new[] { "assay-crucible", "lead-ingot" };
                default: return new[] { "trade-goods", "provisions-crate" };
            }
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        // ---------- validation (no invention, ever) ----------

        private static string ValidateDraft(ScenarioSeedPopulation draft, StartingWorldConfiguration world)
        {
            if (draft.Households.Count == 0)
            {
                return "Draft has no households.";
            }

            foreach (ScenarioSeedHousehold household in draft.Households)
            {
                if (household == null || household.Members.Count == 0)
                {
                    return $"Household {household?.HouseholdId} has no members.";
                }
            }

            int residents = draft.ResidentCount;
            if (residents < world.MinResidents || residents > world.MaxResidents)
            {
                return $"Draft has {residents} residents, outside the authored range " +
                    $"[{world.MinResidents},{world.MaxResidents}].";
            }

            var seenBusinessIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScenarioSeedBusiness business in draft.Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.BusinessId))
                {
                    return "Draft has a business with an empty id.";
                }

                if (!seenBusinessIds.Add(business.BusinessId))
                {
                    return $"Duplicate business id '{business.BusinessId}'.";
                }
            }

            foreach (string requiredId in world.RequiredBusinessIds)
            {
                if (!string.IsNullOrWhiteSpace(requiredId) && !seenBusinessIds.Contains(requiredId))
                {
                    return $"Required business '{requiredId}' is missing from the draft.";
                }
            }

            return null;
        }

        // ---------- fingerprint ----------

        internal static string ComputeWorldFingerprint(
            ScenarioSeedPopulation draft,
            ScenarioEconomySeeder.SeedReport seedReport,
            ScenarioPreset preset,
            int seed,
            int startDayIndex)
        {
            var builder = new StringBuilder(2048);
            builder.Append(preset.PresetId).Append('|');
            builder.Append(preset.StartingWorld?.DefinitionVersion).Append('|');
            builder.Append(seed).Append('|');
            builder.Append(startDayIndex).Append('|');

            foreach (ScenarioSeedHousehold household in draft.Households)
            {
                builder.Append("H").Append(household.HouseholdId).Append(':')
                    .Append(household.Surname).Append(':').Append(household.HomeBuildingId).Append(';');
                foreach (ScenarioSeedPerson person in household.Members)
                {
                    builder.Append("P").Append(person.PersonId).Append(':')
                        .Append(person.FirstName).Append(' ').Append(person.LastName).Append(':')
                        .Append(person.Age).Append(':').Append(person.WeeklyWageCents).Append(';');
                }
            }

            foreach (ScenarioSeedBusiness business in draft.Businesses)
            {
                builder.Append("B").Append(business.BusinessId).Append(':')
                    .Append(business.BusinessType).Append(':').Append(business.CapitalCents).Append(';');
            }

            builder.Append("CASH").Append(seedReport.TotalCashCents).Append(';');
            builder.Append("DEBT").Append(seedReport.ObligationCount).Append(':').Append(seedReport.TotalDebtCents).Append(';');
            builder.Append("EMP").Append(seedReport.EmploymentCount).Append(';');

            unchecked
            {
                ulong hash = 1469598103934665603ul;
                string canonical = builder.ToString();
                for (int i = 0; i < canonical.Length; i++)
                {
                    hash ^= canonical[i];
                    hash *= 1099511628211ul;
                }

                return hash.ToString("X16");
            }
        }
    }
}
