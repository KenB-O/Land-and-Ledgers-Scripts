using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LandLedgers.Population;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTools.Population
{
    /// <summary>One-time, idempotent migration of the development town asset to owner-organized v2 data.</summary>
    public static class DevelopmentTownConfigurationMigration
    {
        private const string AssetPath = "Assets/Core/Population/DevelopmentTownConfiguration.asset";
        private const int TargetSchema = 2;

        [MenuItem("Land & Ledgers/Development/Migrate Town Configuration to Schema 2")]
        public static void MigrateAuthoritativeAsset()
        {
            DevelopmentTownConfiguration asset = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            if (asset == null) throw new InvalidOperationException($"Could not load authoritative configuration at {AssetPath}.");
            if (asset.SchemaVersion >= TargetSchema)
            {
                Debug.Log($"Development-town configuration is already at schema {asset.SchemaVersion}; no migration performed.");
                return;
            }

            string backupPath = "Reports/Diagnostics/Backups/DevelopmentTownConfiguration.20261117-pre-schema.asset.backup";
            if (!File.Exists(backupPath)) throw new InvalidOperationException("The pre-migration owner-asset backup is missing; refusing to migrate.");
            string before = EditorJsonUtility.ToJson(asset, true);
            Undo.RecordObject(asset, "Migrate Development Town Configuration");
            SerializedObject so = new(asset);
            SerializedProperty general = so.FindProperty("general");
            CopyScalar(so, "configurationId", general, "configurationId");
            CopyScalar(so, "seedOffset", general, "seedOffset");
            CopyScalar(so, "generatePopulationFromSettings", general, "generatePopulationFromSettings");
            CopyScalar(so, "generatedPopulationSettings", general, "generatedPopulationSettings");
            CopyScalar(so, "freightBusinessKey", general, "freightBusinessKey");
            CopyScalar(so, "enableMortality", general, "enableMortality");
            CopyScalar(so, "enableFertility", general, "enableFertility");

            Dictionary<string, string> personKeys = BuildPersonKeyMap();
            Dictionary<string, string> householdKeys = BuildHouseholdKeyMap();
            MigrateResidents(so, personKeys);
            MigrateHouseholds(so, personKeys, householdKeys);
            MigrateRelationships(so, personKeys);
            MigrateBusinesses(so, personKeys);
            MigrateOpeningRecords(so, personKeys);
            so.FindProperty("schemaVersion").intValue = TargetSchema;
            so.ApplyModifiedProperties();

            if (!asset.Validate(out string validationError))
            {
                EditorJsonUtility.FromJsonOverwrite(before, asset);
                EditorUtility.SetDirty(asset);
                throw new InvalidOperationException("Migrated development-town configuration failed validation: " + validationError);
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            asset = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            if (asset == null || asset.SchemaVersion != TargetSchema || !asset.Validate(out validationError))
                throw new InvalidOperationException("Serialized read-back failed after schema migration: " + validationError);

            string reportPath = "Reports/Diagnostics/DevelopmentTownConfigurationMigration-2026-11-17.json";
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            System.IO.File.WriteAllText(reportPath, JsonUtility.ToJson(new MigrationReport
            {
                assetPath = AssetPath,
                assetGuid = AssetDatabase.AssetPathToGUID(AssetPath),
                schemaVersion = asset.SchemaVersion,
                residents = asset.People.Count,
                households = asset.Households.Count,
                businesses = asset.Businesses.Count,
                serializedReadBackValid = true,
                beforeSnapshot = before,
                afterSnapshot = EditorJsonUtility.ToJson(asset, true)
            }, true));
            AssetDatabase.Refresh();
            Debug.Log($"Development-town configuration migrated and read back: {asset.People.Count} residents, {asset.Households.Count} households, {asset.Businesses.Count} businesses; GUID {AssetDatabase.AssetPathToGUID(AssetPath)}.");
        }

        public static void MigrateGeneratedConfigurations()
        {
            foreach (string path in new[]
                     {
                         "Assets/Core/Population/MediumDevelopmentTownConfiguration.asset",
                         "Assets/Core/Population/LargeDevelopmentTownConfiguration.asset"
                     })
            {
                DevelopmentTownConfiguration asset = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(path);
                if (asset == null) throw new InvalidOperationException($"Generated population configuration is missing: {path}");
                if (asset.SchemaVersion >= TargetSchema) continue;
                string before = EditorJsonUtility.ToJson(asset, true);
                SerializedObject so = new(asset);
                SerializedProperty general = so.FindProperty("general");
                CopyScalar(so, "configurationId", general, "configurationId");
                CopyScalar(so, "seedOffset", general, "seedOffset");
                CopyScalar(so, "generatePopulationFromSettings", general, "generatePopulationFromSettings");
                CopyScalar(so, "generatedPopulationSettings", general, "generatedPopulationSettings");
                CopyScalar(so, "freightBusinessKey", general, "freightBusinessKey");
                CopyScalar(so, "enableMortality", general, "enableMortality");
                CopyScalar(so, "enableFertility", general, "enableFertility");
                if (!general.FindPropertyRelative("generatePopulationFromSettings").boolValue)
                    throw new InvalidOperationException($"Expected {path} to use generated population settings.");

                SerializedProperty businesses = so.FindProperty("businesses");
                SerializedProperty businessTargets = so.FindProperty("businessCashTargets");
                for (int i = 0; i < businessTargets.arraySize; i++)
                {
                    SerializedProperty target = businessTargets.GetArrayElementAtIndex(i);
                    SerializedProperty business = FindBusiness(businesses, target.FindPropertyRelative("businessKey").stringValue);
                    if (business == null) throw new InvalidOperationException($"{path} has an opening cash target without a business.");
                    if (business.FindPropertyRelative("openingCashCents").intValue != target.FindPropertyRelative("targetCashCents").intValue)
                        throw new InvalidOperationException($"{path} has conflicting business opening cash values.");
                }
                string freightKey = general.FindPropertyRelative("freightBusinessKey").stringValue;
                SerializedProperty freightBusiness = FindBusiness(businesses, freightKey);
                if (freightBusiness == null) throw new InvalidOperationException($"{path} freight business key '{freightKey}' does not resolve.");
                freightBusiness.FindPropertyRelative("opening.openingHorseId").stringValue = so.FindProperty("openingHorseId").stringValue;
                freightBusiness.FindPropertyRelative("opening.openingWagonId").stringValue = so.FindProperty("openingWagonId").stringValue;
                SerializedProperty fuelDealer = FindBusinessByOperation(businesses, "fuel-dealer");
                if (fuelDealer != null)
                    fuelDealer.FindPropertyRelative("opening.fuelDealerIntakeUnitsPerBakeryUnit").intValue = so.FindProperty("fuelDealerIntakeUnitsPerBakeryUnit").intValue;
                SerializedProperty oldHouseholdTargets = so.FindProperty("householdCashTargets");
                SerializedProperty newHouseholdTargets = general.FindPropertyRelative("generatedHouseholdOpeningCash");
                newHouseholdTargets.arraySize = oldHouseholdTargets.arraySize;
                for (int i = 0; i < oldHouseholdTargets.arraySize; i++)
                {
                    SerializedProperty from = oldHouseholdTargets.GetArrayElementAtIndex(i);
                    SerializedProperty to = newHouseholdTargets.GetArrayElementAtIndex(i);
                    to.FindPropertyRelative("anchorPersonKey").stringValue = from.FindPropertyRelative("anchorPersonKey").stringValue;
                    to.FindPropertyRelative("targetCashCents").intValue = from.FindPropertyRelative("targetCashCents").intValue;
                }
                so.FindProperty("schemaVersion").intValue = TargetSchema;
                ClearArray(businessTargets); ClearArray(oldHouseholdTargets);
                ClearArray(so.FindProperty("openingInventory")); ClearArray(so.FindProperty("openingEquipment"));
                ClearArray(so.FindProperty("openingEmployees")); ClearArray(so.FindProperty("openingSpareBlades"));
                ClearArray(so.FindProperty("wholesaleAccounts"));
                so.ApplyModifiedProperties();
                if (!asset.Validate(out string error))
                {
                    EditorJsonUtility.FromJsonOverwrite(before, asset);
                    throw new InvalidOperationException($"{path} failed post-migration validation: {error}");
                }
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                asset = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(path);
                if (asset == null || asset.SchemaVersion != TargetSchema || !asset.Validate(out error))
                    throw new InvalidOperationException($"{path} failed serialized read-back: {error}");
                string reportPath = $"Reports/Diagnostics/{Path.GetFileNameWithoutExtension(path)}-Schema2Migration-2026-11-17.json";
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                System.IO.File.WriteAllText(reportPath, JsonUtility.ToJson(new MigrationReport
                {
                    assetPath = path,
                    assetGuid = AssetDatabase.AssetPathToGUID(path),
                    schemaVersion = asset.SchemaVersion,
                    residents = asset.People.Count,
                    households = asset.Households.Count,
                    businesses = asset.Businesses.Count,
                    serializedReadBackValid = true,
                    beforeSnapshot = before,
                    afterSnapshot = EditorJsonUtility.ToJson(asset, true)
                }, true));
            }
            AssetDatabase.SaveAssets();
        }

        private sealed class MigrationReport
        {
            public string assetPath;
            public string assetGuid;
            public int schemaVersion;
            public int residents;
            public int households;
            public int businesses;
            public bool serializedReadBackValid;
            public string beforeSnapshot;
            public string afterSnapshot;
        }

        private static Dictionary<string, string> BuildPersonKeyMap() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["base:0"] = "kennedy-baldwin-ooms", ["base:1"] = "emma-baldwin-ooms",
            ["base:2"] = "glen-pierce", ["base:3"] = "tannis-pierce",
            ["base:4"] = "jacob-boniecki", ["base:5"] = "grace-boniecki",
            ["ethan"] = "ethan-boniecki", ["tia"] = "tia-boniecki", ["shane"] = "shane-bryan",
            ["richard"] = "richard-sithole", ["silas"] = "richard-sithole",
            ["don"] = "don-millward", ["don-millward"] = "don-millward",
            ["grace"] = "grace-millward", ["grace-millward"] = "grace-millward",
            ["enzo"] = "enzo-kwindt", ["enzo-kwindt"] = "enzo-kwindt",
            ["josh"] = "josh-saunders", ["josh-saunders"] = "josh-saunders"
        };

        private static Dictionary<string, string> BuildHouseholdKeyMap() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["base-household:0"] = "baldwin-ooms", ["base-household:1"] = "pierce",
            ["base-household:2"] = "boniecki", ["ethan-tia"] = "ethan-tia",
            ["shane"] = "shane-bryan", ["silas"] = "richard-sithole",
            ["enzo-emma"] = "kwindt", ["millward"] = "millward"
        };

        private static string Remap(Dictionary<string, string> map, string value) =>
            !string.IsNullOrWhiteSpace(value) && map.TryGetValue(value, out string mapped) ? mapped : value;

        private static void MigrateResidents(SerializedObject so, Dictionary<string, string> map)
        {
            SerializedProperty people = so.FindProperty("people");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < people.arraySize; i++)
            {
                SerializedProperty p = people.GetArrayElementAtIndex(i);
                string oldKey = p.FindPropertyRelative("key").stringValue;
                string newKey = Remap(map, oldKey);
                p.FindPropertyRelative("key").stringValue = newKey;
                p.FindPropertyRelative("runtimePersonId").intValue = PersonId(newKey);
                if (string.Equals(newKey, "grace-millward", StringComparison.OrdinalIgnoreCase))
                {
                    p.FindPropertyRelative("isFemale").boolValue = true;
                    p.FindPropertyRelative("homeBuildingOrdinal").intValue = 7;
                }
                if (string.Equals(newKey, "don-millward", StringComparison.OrdinalIgnoreCase))
                    p.FindPropertyRelative("homeBuildingOrdinal").intValue = 7;
                keys.Add(newKey);
            }
            if (!keys.Contains("enzo-kwindt")) AddPerson(people, "enzo-kwindt", "Enzo", "Kwindt", 40, false, "Sawmill owner/operator", 6, 11);
            if (!keys.Contains("josh-saunders")) AddPerson(people, "josh-saunders", "Josh", "Saunders", 26, false, "Sawmill labourer", 6, 13);
        }

        private static int PersonId(string key) => key switch
        {
            "kennedy-baldwin-ooms" => 1, "emma-baldwin-ooms" => 2, "glen-pierce" => 3,
            "tannis-pierce" => 4, "jacob-boniecki" => 5, "grace-boniecki" => 6,
            "ethan-boniecki" => 7, "tia-boniecki" => 8, "shane-bryan" => 9,
            "richard-sithole" => 10, "enzo-kwindt" => 11, "josh-saunders" => 13,
            "don-millward" => 14, "grace-millward" => 15, _ => 0
        };

        private static void AddPerson(SerializedProperty people, string key, string first, string last, int age, bool female, string job, int home, int id)
        {
            int i = people.arraySize++;
            SerializedProperty p = people.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("key").stringValue = key;
            p.FindPropertyRelative("runtimePersonId").intValue = id;
            p.FindPropertyRelative("firstName").stringValue = first;
            p.FindPropertyRelative("lastName").stringValue = last;
            p.FindPropertyRelative("age").intValue = age;
            p.FindPropertyRelative("ageIsProvisionalCalibration").boolValue = true;
            p.FindPropertyRelative("isFemale").boolValue = female;
            p.FindPropertyRelative("occupation").stringValue = job;
            p.FindPropertyRelative("homeBuildingOrdinal").intValue = home;
        }

        private static void MigrateHouseholds(SerializedObject so, Dictionary<string, string> peopleMap, Dictionary<string, string> keyMap)
        {
            SerializedProperty households = so.FindProperty("households");
            var byKey = new Dictionary<string, SerializedProperty>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < households.arraySize; i++)
            {
                SerializedProperty h = households.GetArrayElementAtIndex(i);
                string old = h.FindPropertyRelative("key").stringValue;
                string key = Remap(keyMap, old);
                h.FindPropertyRelative("key").stringValue = key;
                h.FindPropertyRelative("runtimeHouseholdId").intValue = HouseholdId(key);
                SerializedProperty members = h.FindPropertyRelative("memberPersonKeys");
                for (int j = 0; j < members.arraySize; j++) members.GetArrayElementAtIndex(j).stringValue = Remap(peopleMap, members.GetArrayElementAtIndex(j).stringValue);
                if (string.Equals(key, "richard-sithole", StringComparison.OrdinalIgnoreCase))
                {
                    h.FindPropertyRelative("displayName").stringValue = "Richard Sithole Household";
                    h.FindPropertyRelative("surname").stringValue = "Sithole";
                }
                byKey[key] = h;
            }

            EnsureHousehold(households, byKey, "kwindt", "Kwindt Household", "Kwindt", 6, 7, 40000,
                new[] { "enzo-kwindt", "josh-saunders" });
            EnsureHousehold(households, byKey, "millward", "Millward Household", "Millward", 7, 8, 40000,
                new[] { "don-millward", "grace-millward" });

            // Carry all existing household allocations into their owning records.
            SerializedProperty targets = so.FindProperty("householdCashTargets");
            for (int i = 0; i < targets.arraySize; i++)
            {
                SerializedProperty target = targets.GetArrayElementAtIndex(i);
                string anchor = Remap(peopleMap, target.FindPropertyRelative("anchorPersonKey").stringValue);
                SerializedProperty home = FindHouseholdContaining(households, anchor);
                if (home == null) throw new InvalidOperationException($"Cash target anchor '{anchor}' has no household after migration.");
                home.FindPropertyRelative("openingCashCents").intValue = target.FindPropertyRelative("targetCashCents").intValue;
            }
        }

        private static int HouseholdId(string key) => key switch
        {
            "baldwin-ooms" => 1, "pierce" => 2, "boniecki" => 3, "ethan-tia" => 4,
            "shane-bryan" => 5, "richard-sithole" => 6, "kwindt" => 7, "millward" => 8, _ => 0
        };

        private static void EnsureHousehold(SerializedProperty array, Dictionary<string, SerializedProperty> map,
            string key, string name, string surname, int home, int id, int openingCash, string[] members)
        {
            if (!map.TryGetValue(key, out SerializedProperty h))
            {
                int i = array.arraySize++;
                h = array.GetArrayElementAtIndex(i);
                h.FindPropertyRelative("key").stringValue = key;
            }
            h.FindPropertyRelative("runtimeHouseholdId").intValue = id;
            h.FindPropertyRelative("displayName").stringValue = name;
            h.FindPropertyRelative("surname").stringValue = surname;
            h.FindPropertyRelative("homeBuildingOrdinal").intValue = home;
            if (h.FindPropertyRelative("openingCashCents").intValue == 0) h.FindPropertyRelative("openingCashCents").intValue = openingCash;
            SerializedProperty list = h.FindPropertyRelative("memberPersonKeys");
            list.arraySize = members.Length;
            for (int i = 0; i < members.Length; i++) list.GetArrayElementAtIndex(i).stringValue = members[i];
        }

        private static SerializedProperty FindHouseholdContaining(SerializedProperty households, string member)
        {
            for (int i = 0; i < households.arraySize; i++)
            {
                SerializedProperty h = households.GetArrayElementAtIndex(i).FindPropertyRelative("memberPersonKeys");
                for (int j = 0; j < h.arraySize; j++)
                    if (string.Equals(h.GetArrayElementAtIndex(j).stringValue, member, StringComparison.OrdinalIgnoreCase))
                        return households.GetArrayElementAtIndex(i);
            }
            return null;
        }

        private static void MigrateRelationships(SerializedObject so, Dictionary<string, string> map)
        {
            SerializedProperty relationships = so.FindProperty("relationships");
            for (int i = 0; i < relationships.arraySize; i++)
            {
                SerializedProperty r = relationships.GetArrayElementAtIndex(i);
                r.FindPropertyRelative("firstPersonKey").stringValue = Remap(map, r.FindPropertyRelative("firstPersonKey").stringValue);
                r.FindPropertyRelative("secondPersonKey").stringValue = Remap(map, r.FindPropertyRelative("secondPersonKey").stringValue);
            }
            bool millwardPair = false;
            for (int i = 0; i < relationships.arraySize; i++)
            {
                SerializedProperty r = relationships.GetArrayElementAtIndex(i);
                if ((r.FindPropertyRelative("firstPersonKey").stringValue == "don-millward" && r.FindPropertyRelative("secondPersonKey").stringValue == "grace-millward")
                    || (r.FindPropertyRelative("firstPersonKey").stringValue == "grace-millward" && r.FindPropertyRelative("secondPersonKey").stringValue == "don-millward")) millwardPair = true;
            }
            if (!millwardPair)
            {
                int i = relationships.arraySize++;
                SerializedProperty r = relationships.GetArrayElementAtIndex(i);
                r.FindPropertyRelative("firstPersonKey").stringValue = "don-millward";
                r.FindPropertyRelative("secondPersonKey").stringValue = "grace-millward";
                r.FindPropertyRelative("relationship").enumValueIndex = (int)DevelopmentTownConfiguration.RelationshipKind.Spouse;
            }
        }

        private static void MigrateBusinesses(SerializedObject so, Dictionary<string, string> personMap)
        {
            SerializedProperty businesses = so.FindProperty("businesses");
            for (int i = 0; i < businesses.arraySize; i++)
            {
                SerializedProperty b = businesses.GetArrayElementAtIndex(i);
                string key = b.FindPropertyRelative("key").stringValue;
                b.FindPropertyRelative("ownerPersonKey").stringValue = Remap(personMap, b.FindPropertyRelative("ownerPersonKey").stringValue);
                string display = b.FindPropertyRelative("displayName").stringValue;
                if (string.IsNullOrWhiteSpace(display)) b.FindPropertyRelative("displayName").stringValue = DisplayBusiness(key);
            }

            SerializedProperty targets = so.FindProperty("businessCashTargets");
            for (int i = 0; i < targets.arraySize; i++)
            {
                SerializedProperty target = targets.GetArrayElementAtIndex(i);
                SerializedProperty b = FindBusiness(businesses, target.FindPropertyRelative("businessKey").stringValue);
                if (b == null) throw new InvalidOperationException("A business cash target references a business that no longer exists.");
                int targetCash = target.FindPropertyRelative("targetCashCents").intValue;
                int currentCash = b.FindPropertyRelative("openingCashCents").intValue;
                if (currentCash != 0 && currentCash != targetCash) throw new InvalidOperationException($"Business '{target.FindPropertyRelative("businessKey").stringValue}' has conflicting opening cash ({currentCash} vs target {targetCash}).");
                b.FindPropertyRelative("openingCashCents").intValue = targetCash;
            }
        }

        private static string DisplayBusiness(string key) => key switch
        {
            "general-store" => "General Store", "bakery" => "Bakery", "fuel-dealer" => "Fuel Dealer",
            "freight-company" => "Livery and Freight", "sawmill" => "Sawmill", "lumber-yard" => "Lumber Yard",
            "carpentry-workshop" => "Carpentry Workshop", _ => key
        };

        private static void MigrateOpeningRecords(SerializedObject so, Dictionary<string, string> peopleMap)
        {
            SerializedProperty businesses = so.FindProperty("businesses");
            SerializedProperty oldInventory = so.FindProperty("openingInventory");
            for (int i = 0; i < oldInventory.arraySize; i++)
            {
                SerializedProperty old = oldInventory.GetArrayElementAtIndex(i);
                SerializedProperty b = FindBusiness(businesses, old.FindPropertyRelative("businessKey").stringValue);
                if (b == null) throw new InvalidOperationException("An opening inventory entry references an unconfigured business.");
                CopyArray(old.FindPropertyRelative("items"), b.FindPropertyRelative("opening.inventory"));
            }

            SerializedProperty oldEquipment = so.FindProperty("openingEquipment");
            for (int i = 0; i < oldEquipment.arraySize; i++)
            {
                SerializedProperty old = oldEquipment.GetArrayElementAtIndex(i);
                SerializedProperty b = FindBusiness(businesses, old.FindPropertyRelative("businessKey").stringValue);
                if (b == null) throw new InvalidOperationException("An equipment entry references an unconfigured business.");
                SerializedProperty target = b.FindPropertyRelative("opening.equipment");
                CopyArray(old.FindPropertyRelative("ovens"), target.FindPropertyRelative("ovens"));
                CopyArray(old.FindPropertyRelative("kitIds"), target.FindPropertyRelative("kitIds"));
                CopyArray(old.FindPropertyRelative("assetEntries"), target.FindPropertyRelative("assetEntries"));
            }

            SerializedProperty oldEmployees = so.FindProperty("openingEmployees");
            for (int i = 0; i < oldEmployees.arraySize; i++)
            {
                SerializedProperty old = oldEmployees.GetArrayElementAtIndex(i);
                SerializedProperty b = FindBusiness(businesses, old.FindPropertyRelative("businessKey").stringValue);
                if (b == null) throw new InvalidOperationException("An employee entry references an unconfigured business.");
                SerializedProperty list = b.FindPropertyRelative("opening.employees");
                int n = list.arraySize++;
                SerializedProperty target = list.GetArrayElementAtIndex(n);
                target.FindPropertyRelative("personKey").stringValue = Remap(peopleMap, old.FindPropertyRelative("personKey").stringValue);
                target.FindPropertyRelative("role").stringValue = old.FindPropertyRelative("role").stringValue;
                target.FindPropertyRelative("weeklyWageCents").intValue = old.FindPropertyRelative("weeklyWageCents").intValue;
            }
            // Kennedy's current direction names two sawmill workers: Richard Sithole and Josh Saunders.
            SerializedProperty sawmill = FindBusiness(businesses, "sawmill");
            if (sawmill == null) throw new InvalidOperationException("The authoritative configuration has no sawmill business.");
            SerializedProperty sawmillEmployees = sawmill.FindPropertyRelative("opening.employees");
            EnsureEmployee(sawmillEmployees, "richard-sithole", "logger", 750);
            EnsureEmployee(sawmillEmployees, "josh-saunders", "labourer", 750);
            RemoveEmployee(sawmillEmployees, "shane-bryan");

            SerializedProperty oldBlades = so.FindProperty("openingSpareBlades");
            for (int i = 0; i < oldBlades.arraySize; i++)
            {
                SerializedProperty old = oldBlades.GetArrayElementAtIndex(i);
                SerializedProperty b = FindBusiness(businesses, old.FindPropertyRelative("businessKey").stringValue);
                if (b == null) throw new InvalidOperationException("A spare blade entry references an unconfigured business.");
                SerializedProperty list = b.FindPropertyRelative("opening.spareBlades");
                int n = list.arraySize++;
                list.GetArrayElementAtIndex(n).FindPropertyRelative("bladeId").stringValue = old.FindPropertyRelative("bladeId").stringValue;
            }

            SerializedProperty oldWholesale = so.FindProperty("wholesaleAccounts");
            for (int i = 0; i < oldWholesale.arraySize; i++)
            {
                SerializedProperty old = oldWholesale.GetArrayElementAtIndex(i);
                string seller = old.FindPropertyRelative("sellerBusinessKey").stringValue;
                SerializedProperty b = FindBusiness(businesses, seller);
                if (b == null) throw new InvalidOperationException("A wholesale agreement references an unconfigured seller.");
                SerializedProperty list = b.FindPropertyRelative("opening.wholesaleAgreements");
                int n = list.arraySize++;
                SerializedProperty target = list.GetArrayElementAtIndex(n);
                CopyField(old, target, "buyerBusinessKey"); CopyField(old, target, "productId");
                CopyField(old, target, "unitsPerDelivery"); CopyField(old, target, "deliveryCadenceDays");
                CopyField(old, target, "pricePerUnitCents"); CopyField(old, target, "freshness"); CopyField(old, target, "firstDeliveryDayIndex");
            }

            SetBusinessOpeningInt(businesses, "sawmill", "loggingCampProvisionCoverDays", so.FindProperty("openingLoggingCampProvisionCoverDays").intValue);
            SetBusinessOpeningInt(businesses, "bakery", "bakeryFuelUnits", so.FindProperty("openingBakeryFuelUnits").intValue);
            SetBusinessOpeningInt(businesses, "bakery", "bakeryFlourPounds", so.FindProperty("openingBakeryFlourPounds").intValue);
            SetBusinessOpeningInt(businesses, "bakery", "bakeryNotionsUnits", so.FindProperty("openingBakeryNotionsUnits").intValue);
            SetBusinessOpeningInt(businesses, "carpentry-workshop", "carpentryLumberUnits", so.FindProperty("openingCarpentryLumberUnits").intValue);
            SetBusinessOpeningInt(businesses, "fuel-dealer", "fuelDealerIntakeUnitsPerBakeryUnit", so.FindProperty("fuelDealerIntakeUnitsPerBakeryUnit").intValue);
            SerializedProperty freight = FindBusiness(businesses, so.FindProperty("freightBusinessKey").stringValue);
            if (freight == null) throw new InvalidOperationException("The configured freight business key does not resolve.");
            freight.FindPropertyRelative("opening.openingHorseId").stringValue = so.FindProperty("openingHorseId").stringValue;
            freight.FindPropertyRelative("opening.openingWagonId").stringValue = so.FindProperty("openingWagonId").stringValue;

            ClearArray(oldInventory); ClearArray(oldEquipment); ClearArray(oldEmployees); ClearArray(oldBlades); ClearArray(oldWholesale);
            ClearArray(so.FindProperty("businessCashTargets")); ClearArray(so.FindProperty("householdCashTargets"));
        }

        private static void EnsureEmployee(SerializedProperty list, string key, string role, int wage)
        {
            for (int i = 0; i < list.arraySize; i++)
                if (string.Equals(list.GetArrayElementAtIndex(i).FindPropertyRelative("personKey").stringValue, key, StringComparison.OrdinalIgnoreCase)) return;
            int n = list.arraySize++;
            SerializedProperty p = list.GetArrayElementAtIndex(n);
            p.FindPropertyRelative("personKey").stringValue = key;
            p.FindPropertyRelative("role").stringValue = role;
            p.FindPropertyRelative("weeklyWageCents").intValue = wage;
        }

        private static void RemoveEmployee(SerializedProperty list, string key)
        {
            for (int i = list.arraySize - 1; i >= 0; i--)
                if (string.Equals(list.GetArrayElementAtIndex(i).FindPropertyRelative("personKey").stringValue, key, StringComparison.OrdinalIgnoreCase))
                    list.DeleteArrayElementAtIndex(i);
        }

        private static void SetBusinessOpeningInt(SerializedProperty businesses, string key, string field, int value)
        {
            SerializedProperty b = FindBusiness(businesses, key);
            if (b == null) throw new InvalidOperationException($"Required business '{key}' was not configured.");
            b.FindPropertyRelative("opening." + field).intValue = value;
        }

        private static SerializedProperty FindBusiness(SerializedProperty businesses, string key)
        {
            for (int i = 0; i < businesses.arraySize; i++)
            {
                SerializedProperty b = businesses.GetArrayElementAtIndex(i);
                if (string.Equals(b.FindPropertyRelative("key").stringValue, key, StringComparison.OrdinalIgnoreCase)) return b;
            }
            return null;
        }

        private static SerializedProperty FindBusinessByOperation(SerializedProperty businesses, string operationId)
        {
            for (int i = 0; i < businesses.arraySize; i++)
            {
                SerializedProperty b = businesses.GetArrayElementAtIndex(i);
                if (string.Equals(b.FindPropertyRelative("operationId").stringValue, operationId, StringComparison.OrdinalIgnoreCase)) return b;
            }
            return null;
        }

        private static void CopyScalar(SerializedObject source, string field, SerializedProperty destination, string destinationField)
        {
            SerializedProperty from = source.FindProperty(field);
            SerializedProperty to = destination.FindPropertyRelative(destinationField);
            if (from == null || to == null) return;
            switch (from.propertyType)
            {
                case SerializedPropertyType.Integer: to.intValue = from.intValue; break;
                case SerializedPropertyType.Boolean: to.boolValue = from.boolValue; break;
                case SerializedPropertyType.String: to.stringValue = from.stringValue; break;
                case SerializedPropertyType.ObjectReference: to.objectReferenceValue = from.objectReferenceValue; break;
            }
        }

        private static void CopyArray(SerializedProperty source, SerializedProperty destination)
        {
            if (source == null || destination == null) throw new InvalidOperationException("A serialized array is missing during migration.");
            destination.ClearArray();
            for (int i = 0; i < source.arraySize; i++)
            {
                int n = destination.arraySize++;
                SerializedProperty from = source.GetArrayElementAtIndex(i);
                SerializedProperty to = destination.GetArrayElementAtIndex(n);
                if (from.propertyType == SerializedPropertyType.String) { to.stringValue = from.stringValue; continue; }
                if (from.propertyType == SerializedPropertyType.Integer) { to.intValue = from.intValue; continue; }
                CopyField(from, to, "itemId"); CopyField(from, to, "minimumUnits"); CopyField(from, to, "maximumUnits");
                CopyField(from, to, "componentAssetIds"); CopyField(from, to, "assetEntries"); CopyField(from, to, "assetId");
                CopyField(from, to, "kind"); CopyField(from, to, "bladeId"); CopyField(from, to, "kitIds"); CopyField(from, to, "ovens");
            }
        }

        private static void CopyField(SerializedProperty fromParent, SerializedProperty toParent, string name)
        {
            SerializedProperty from = fromParent.FindPropertyRelative(name);
            SerializedProperty to = toParent.FindPropertyRelative(name);
            if (from == null || to == null) return;
            switch (from.propertyType)
            {
                case SerializedPropertyType.Integer: to.intValue = from.intValue; break;
                case SerializedPropertyType.Boolean: to.boolValue = from.boolValue; break;
                case SerializedPropertyType.String: to.stringValue = from.stringValue; break;
                case SerializedPropertyType.Enum: to.enumValueIndex = from.enumValueIndex; break;
                case SerializedPropertyType.Float: to.floatValue = from.floatValue; break;
                case SerializedPropertyType.ObjectReference: to.objectReferenceValue = from.objectReferenceValue; break;
                case SerializedPropertyType.Generic:
                    if (from.isArray && to.isArray) CopyArray(from, to);
                    break;
            }
        }

        private static void ClearArray(SerializedProperty property)
        {
            if (property == null || !property.isArray) return;
            while (property.arraySize > 0) property.DeleteArrayElementAtIndex(property.arraySize - 1);
        }
    }
}
