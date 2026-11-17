using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Population
{
    /// <summary>Guards the Unity-authored development town against stale or reordered identity edits.</summary>
    [TestFixture]
    public sealed class SawmillAuthorshipC8Tests
    {
        private const string AssetRelativePath = "Core/Population/DevelopmentTownConfiguration.asset";

        private static string ReadAsset()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, AssetRelativePath));
            Assert.IsTrue(File.Exists(path), $"Expected the authored development-town asset at '{path}'.");
            return File.ReadAllText(path);
        }

        private static string Entry(string asset, string key, string nextKey)
        {
            int start = asset.IndexOf("  - key: " + key + "\n", StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, $"Missing serialized list record '{key}'.");
            int end = asset.IndexOf("  - key: " + nextKey + "\n", start + 1, StringComparison.Ordinal);
            Assert.Greater(end, start, $"Could not bound serialized record '{key}'.");
            return asset.Substring(start, end - start);
        }

        [Test]
        public void SchemaTwo_UsesStableResidentKeysAndHasNoUnsupportedCommentLines()
        {
            string asset = ReadAsset();
            StringAssert.Contains("schemaVersion: 2", asset);
            StringAssert.Contains("key: richard-sithole\n    runtimePersonId: 10\n    firstName: Richard\n    lastName: Sithole", asset);
            StringAssert.Contains("key: enzo-kwindt\n    runtimePersonId: 11", asset);
            StringAssert.Contains("key: josh-saunders\n    runtimePersonId: 13", asset);
            Assert.IsFalse(asset.Split('\n').Any(line => line.TrimStart().StartsWith("#", StringComparison.Ordinal)),
                "Unity serialized asset data must not contain hand-written comment lines.");
            StringAssert.DoesNotContain("businessCashTargets:", asset);
            StringAssert.DoesNotContain("householdCashTargets:", asset);
            StringAssert.DoesNotContain("openingEmployees:", asset);
        }

        [Test]
        public void Sawmill_UsesEnzoAndExactlyRichardAndJoshAsOpeningWorkers()
        {
            string asset = ReadAsset();
            string sawmill = Entry(asset, "sawmill", "lumber-yard");
            StringAssert.Contains("ownerPersonKey: enzo-kwindt", sawmill);
            StringAssert.Contains("employees:\n      - personKey: richard-sithole\n        role: logger\n        weeklyWageCents: 750\n      - personKey: josh-saunders\n        role: labourer\n        weeklyWageCents: 750", sawmill);
            StringAssert.DoesNotContain("personKey: shane-bryan", sawmill);
            StringAssert.Contains("openingCashCents: 15000", sawmill);
        }

        [Test]
        public void KwindtAndMillwardHouseholdsPreserveTheirAuthoredMembersAndCash()
        {
            string asset = ReadAsset();
            string kwindt = Entry(asset, "kwindt", "millward");
            StringAssert.Contains("displayName: Kwindt Household", kwindt);
            StringAssert.Contains("surname: Kwindt", kwindt);
            StringAssert.Contains("- enzo-kwindt\n    - josh-saunders", kwindt);
            StringAssert.Contains("openingCashCents: 40000", kwindt);

            string millward = Entry(asset, "millward", "general-store");
            StringAssert.Contains("displayName: Millward Household", millward);
            StringAssert.Contains("surname: Millward", millward);
            StringAssert.Contains("- don-millward\n    - grace-millward", millward);
            StringAssert.Contains("openingCashCents: 40000", millward);
        }
    }
}
