#if UNITY_EDITOR
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Scenarios.Development;
using LandLedgers.Time;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.Development.Editor
{
    public sealed class ScenarioValidationWindow : EditorWindow
    {
        private ScenarioValidationHarness harness;
        private string selectedScenario;
        private string businessName = "Editorial Business";
        private int customCashCents = 100000;

        [MenuItem("Land & Ledgers/Scenario/Validation Harness")]
        public static void Open() => GetWindow<ScenarioValidationWindow>("Scenario Validation");

        private void OnGUI()
        {
            harness = FindAnyObjectByType<ScenarioValidationHarness>();
            EditorGUILayout.HelpBox("Development/editorial surface over production state. It never completes objectives or victory directly.", MessageType.Info);
            if (harness == null)
            {
                EditorGUILayout.HelpBox("Enter Play Mode with the authored scene to use the harness.", MessageType.Warning);
                return;
            }

            string[] scenarios = harness.ScenarioIds.ToArray();
            if (scenarios.Length > 0)
            {
                int selected = Mathf.Max(0, System.Array.IndexOf(scenarios, selectedScenario));
                selected = EditorGUILayout.Popup("Scenario", selected, scenarios);
                selectedScenario = scenarios[selected];
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Start")) harness.StartScenario(selectedScenario);
                if (GUILayout.Button("Restart")) harness.RestartScenario();
                if (GUILayout.Button("Re-evaluate")) harness.ReevaluateObjectives();
            }

            EditorGUILayout.LabelField("Authoritative time", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                SpeedButton("1x", SimulationSpeed.Speed1x);
                SpeedButton("10x", SimulationSpeed.Speed10x);
                SpeedButton("25x", SimulationSpeed.Speed25x);
                SpeedButton("50x", SimulationSpeed.Speed50x);
                SpeedButton("100x", SimulationSpeed.Speed100x);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+1 hour")) harness.AdvanceHours(1);
                if (GUILayout.Button("+1 day")) harness.AdvanceHours(24);
                if (GUILayout.Button("+7 days")) harness.AdvanceHours(168);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Development capital", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+$100")) harness.InjectOwnerCash(10000);
                if (GUILayout.Button("+$500")) harness.InjectOwnerCash(50000);
                if (GUILayout.Button("+$1,000")) harness.InjectOwnerCash(100000);
            }
            customCashCents = EditorGUILayout.IntField("Custom cents", customCashCents);
            if (GUILayout.Button("Inject custom capital")) harness.InjectOwnerCash(customCashCents);
            if (GUILayout.Button("Probe player assets at $15,000 each")) harness.ProbeAllPlayerBusinessAssets(1500000);

            if (GUILayout.Button("Fund first player business $500"))
            {
                harness.FundBusiness(FindFirstPlayerBusiness(), 50000);
            }

            if (GUILayout.Button("Hire candidate for first player business"))
            {
                harness.HireCandidate(FindFirstPlayerBusiness());
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Production business formation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Entity", "Generic Business (classless)");
            businessName = EditorGUILayout.TextField("Name", businessName);
            if (GUILayout.Button("Create through production authority"))
            {
                harness.TryCreateBusiness(BusinessType.Generic, businessName, out _, out _);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Goals / objectives", EditorStyles.boldLabel);
            foreach (ScenarioValidationHarness.ObjectiveReport report in harness.GetObjectiveReports())
            {
                string status = report.Complete ? "COMPLETE" : "INCOMPLETE";
                EditorGUILayout.LabelField($"{status}  {report.Id}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(report.Description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField($"Current: {report.Current} | Target: {report.Target}");
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Override / editorial log", EditorStyles.boldLabel);
            foreach (string entry in harness.SessionLog.Reverse().Take(10))
            {
                EditorGUILayout.LabelField(entry, EditorStyles.wordWrappedMiniLabel);
            }
            Repaint();
        }

        private void SpeedButton(string label, SimulationSpeed speed)
        {
            if (GUILayout.Button(label)) harness.SetTimeSpeed(speed);
        }

        private static BusinessInstanceState FindFirstPlayerBusiness()
        {
            SharedBusinessRuntimeManager manager = FindAnyObjectByType<SharedBusinessRuntimeManager>();
            if (manager == null)
            {
                return null;
            }

            foreach (BusinessInstanceState business in manager.Businesses)
            {
                if (business != null && business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Player)
                {
                    return business;
                }
            }

            return null;
        }
    }
}
#endif
