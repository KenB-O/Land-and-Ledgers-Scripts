using LandLedgers.Orchestration.Scenarios;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace LandLedgers.EditorTests.Orchestration
{
    public sealed class FirstLedgerScenarioAssetDateTests
    {
        private const string FirstLedgerAssetPath =
            "Assets/Scripts/Orchestration/Scenarios/FirstLedger/FirstLedger.asset";

        [Test]
        public void ImportedFirstLedgerAssetKeepsItsAuthoredJanuary1870StartDate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(
                FirstLedgerAssetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ScenarioAsset scenario = AssetDatabase.LoadAssetAtPath<ScenarioAsset>(FirstLedgerAssetPath);

            Assert.NotNull(scenario, "The Main Scene scenario source must import as its authored ScenarioAsset.");
            var serialized = new SerializedObject(scenario);
            int serializedStartYear = serialized.FindProperty("startingWorld")
                .FindPropertyRelative("startYear").intValue;
            UnityEngine.Debug.Log($"[Pass40 FirstLedgerAssetDiagnostic] asset={scenario.name}#{scenario.GetInstanceID()}; " +
                                  $"propertyYear={scenario.StartingWorld.StartYear}; serializedYear={serializedStartYear}; " +
                                  $"path={AssetDatabase.GetAssetPath(scenario)}; " +
                                  $"activeScene={EditorSceneManager.GetActiveScene().path}; " +
                                  $"dependencyHash={AssetDatabase.GetAssetDependencyHash(FirstLedgerAssetPath)}");
            Assert.AreEqual("first-ledger", scenario.ScenarioId);
            Assert.AreEqual(1870, scenario.StartingWorld.StartYear,
                $"The imported ScriptableObject must match the YAML source; serialized startingWorld.startYear={serializedStartYear}.");
            Assert.AreEqual(1, scenario.StartingWorld.StartMonth);
            Assert.AreEqual(1, scenario.StartingWorld.StartDay);
            Assert.AreEqual(55, scenario.StartingWorld.MinResidents);
            Assert.AreEqual(95, scenario.StartingWorld.MaxResidents);
            Assert.AreEqual(250000, scenario.StartingWorld.PlayerStart.StartingCashCents);
            CollectionAssert.AreEqual(
                new[]
                {
                    LandLedgers.Economy.Financing.FinancialObligationKind.TradeCredit,
                    LandLedgers.Economy.Financing.FinancialObligationKind.Loan,
                },
                scenario.StartingWorld.EconomyPolicy.AllowedObligationKinds);
            Assert.AreEqual(8, scenario.Goals.Count);
            Assert.AreEqual(5, scenario.Objectives.Count);
            Assert.AreEqual(4, scenario.Tunables.Count);
        }
    }
}
