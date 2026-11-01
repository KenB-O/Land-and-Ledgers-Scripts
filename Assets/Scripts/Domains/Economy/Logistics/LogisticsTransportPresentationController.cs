using System.Collections.Generic;
using System.Text;
using LandLedgers.Pathing;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandLedgers.Economy
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(266)]
    public sealed class LogisticsTransportPresentationController : MonoBehaviour
    {
        [SerializeField] private LogisticsRuntimeManager logisticsRuntime;
        [SerializeField] private GameObject preferredTransportPrefab;
        [SerializeField] private float wagonHeightOffset = 0.05f;
        [SerializeField] private bool presentCompletedShipments;
        [SerializeField] private bool presentFailedShipments;
        [SerializeField, TextArea(1, 3)] private string lastPresentationSummary = "Transport presentation: no shipments sampled yet.";
        [SerializeField, TextArea(1, 6)] private string lastVisibleShipmentReadout = string.Empty;
        [SerializeField, TextArea(1, 4)] private string lastPressureReadout = string.Empty;

        private readonly Dictionary<string, PresentedShipment> activeVisuals = new();

        public string LastPresentationSummary => string.IsNullOrWhiteSpace(lastPresentationSummary) ? "Transport presentation: no shipments sampled yet." : lastPresentationSummary;
        public string LastVisibleShipmentReadout => lastVisibleShipmentReadout ?? string.Empty;
        public string LastPressureReadout => lastPressureReadout ?? string.Empty;

        private void Awake()
        {
            AutoWire();
        }

        private void OnValidate()
        {
            AutoWire();
#if UNITY_EDITOR
            if (preferredTransportPrefab == null)
            {
                preferredTransportPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Travel/Wagon Cargo Horse.prefab");
            }
#endif
        }

        private void LateUpdate()
        {
            AutoWire();
            if (logisticsRuntime == null)
            {
                ClearPresentedVisuals();
                lastPressureReadout = string.Empty;
                lastPresentationSummary = "Transport presentation: logistics runtime unavailable.";
                return;
            }

            LogisticsTownPressureSnapshot pressureSnapshot = logisticsRuntime.CaptureTownPressureSnapshot();
            lastPressureReadout = pressureSnapshot != null && pressureSnapshot.HasPressure
                ? pressureSnapshot.BuildPressureDetail()
                : string.Empty;

            int visibleCount = 0;
            int blockedCount = 0;
            int delayedCount = 0;
            int failedCount = 0;
            int hiddenCompletedCount = 0;
            int hiddenFailedCount = 0;
            int unsampledCount = 0;
            int lightReturnCount = 0;
            HashSet<string> seenShipments = new();
            StringBuilder readout = new();
            IReadOnlyList<LogisticsShipmentState> shipments = logisticsRuntime.Shipments;

            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment == null || string.IsNullOrWhiteSpace(shipment.ShipmentId))
                {
                    continue;
                }

                bool failed = shipment.Status == LogisticsShipmentStatus.Failed;
                bool completed = shipment.Status == LogisticsShipmentStatus.Completed;
                bool blocked = !string.IsNullOrWhiteSpace(shipment.BlockedReason);
                if (blocked)
                {
                    blockedCount++;
                }

                if (shipment.Status == LogisticsShipmentStatus.Delayed)
                {
                    delayedCount++;
                }

                if (failed)
                {
                    failedCount++;
                }

                if (completed && !presentCompletedShipments)
                {
                    hiddenCompletedCount++;
                    AppendDiagnosticLine(readout, shipment, "hidden completed");
                    continue;
                }

                if (failed && !presentFailedShipments)
                {
                    hiddenFailedCount++;
                    AppendDiagnosticLine(readout, shipment, "hidden failed");
                    continue;
                }

                if (!logisticsRuntime.TrySampleVisibleShipment(shipment, out Vector3 worldPosition, out LogisticsTransportVisualState visualState))
                {
                    unsampledCount++;
                    AppendDiagnosticLine(readout, shipment, blocked ? "blocked/no route sample" : "no route sample");
                    continue;
                }

                visibleCount++;
                if (visualState == LogisticsTransportVisualState.LightReturn)
                {
                    lightReturnCount++;
                }

                seenShipments.Add(shipment.ShipmentId);
                if (!activeVisuals.TryGetValue(shipment.ShipmentId, out PresentedShipment presented) || presented.Root == null)
                {
                    presented = CreatePresentedShipment(shipment);
                    if (presented.Root == null)
                    {
                        continue;
                    }

                    activeVisuals[shipment.ShipmentId] = presented;
                }

                presented.Root.transform.position = worldPosition + Vector3.up * wagonHeightOffset;
                presented.VisualController?.ApplyVisualState(visualState);
                AppendDiagnosticLine(readout, shipment, visualState == LogisticsTransportVisualState.LightReturn ? "visible light/return" : "visible loaded");
            }

            RemoveStaleVisuals(seenShipments);
            int pressurePercent = pressureSnapshot != null ? Mathf.RoundToInt(pressureSnapshot.Pressure01 * 100f) : 0;
            lastPresentationSummary = $"Transport presentation: visible {visibleCount}, delayed {delayedCount}, blocked {blockedCount}, failed {failedCount}, light/return {lightReturnCount}, hidden completed {hiddenCompletedCount}, hidden failed {hiddenFailedCount}, unsampled {unsampledCount}, pressure {pressurePercent}%.";
            lastVisibleShipmentReadout = readout.ToString().TrimEnd();
        }

        private PresentedShipment CreatePresentedShipment(LogisticsShipmentState shipment)
        {
            GameObject root = null;
            if (preferredTransportPrefab != null)
            {
                root = Instantiate(preferredTransportPrefab);
                root.name = BuildPresentedName(shipment, "new");
                if (root.GetComponent<AgentMover>() == null)
                {
                    root.AddComponent<AgentMover>();
                }
            }

            if (root == null)
            {
                return default;
            }

            LogisticsTransportVisualController controller = root.GetComponent<LogisticsTransportVisualController>();
            if (controller == null)
            {
                controller = root.AddComponent<LogisticsTransportVisualController>();
            }

            return new PresentedShipment(root, controller);
        }

        private static void AppendDiagnosticLine(StringBuilder builder, LogisticsShipmentState shipment, string stateLabel)
        {
            if (builder == null || shipment == null)
            {
                return;
            }

            string label = !string.IsNullOrWhiteSpace(shipment.summaryLabel)
                ? shipment.summaryLabel
                : !string.IsNullOrWhiteSpace(shipment.destinationCategoryId)
                    ? shipment.destinationCategoryId
                    : shipment.ShipmentId;
            builder.Append(stateLabel);
            builder.Append(" | ");
            builder.Append(shipment.Status);
            builder.Append(" | ETA ");
            builder.Append(FormatGameTime(shipment.EstimatedRemainingGameSeconds));
            builder.Append(" | ");
            builder.Append(label);
            if (!string.IsNullOrWhiteSpace(shipment.BlockedReason))
            {
                builder.Append(" | issue ");
                builder.Append(shipment.BlockedReason);
            }

            builder.AppendLine();
        }

        private static string BuildPresentedName(LogisticsShipmentState shipment, string suffix)
        {
            string id = shipment != null && !string.IsNullOrWhiteSpace(shipment.ShipmentId) ? shipment.ShipmentId : "shipment";
            string state = shipment != null ? shipment.Status.ToString() : "unknown";
            return $"Shipment Wagon {state} {suffix} {id}";
        }

        private void RemoveStaleVisuals(HashSet<string> seenShipments)
        {
            List<string> removeKeys = new();
            foreach (KeyValuePair<string, PresentedShipment> pair in activeVisuals)
            {
                if (seenShipments == null || !seenShipments.Contains(pair.Key))
                {
                    if (pair.Value.Root != null)
                    {
                        Destroy(pair.Value.Root);
                    }

                    removeKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < removeKeys.Count; i++)
            {
                activeVisuals.Remove(removeKeys[i]);
            }
        }

        private void ClearPresentedVisuals()
        {
            foreach (KeyValuePair<string, PresentedShipment> pair in activeVisuals)
            {
                if (pair.Value.Root != null)
                {
                    Destroy(pair.Value.Root);
                }
            }

            activeVisuals.Clear();
        }

        private void AutoWire()
        {
            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
        }

        private static string FormatGameTime(float gameSeconds)
        {
            if (gameSeconds <= 0f)
            {
                return "now";
            }

            float hours = gameSeconds / 3600f;
            if (hours >= 24f)
            {
                return $"{Mathf.RoundToInt(hours / 24f)}d";
            }

            if (hours >= 1f)
            {
                return $"{Mathf.RoundToInt(hours)}h";
            }

            return $"{Mathf.Max(1, Mathf.RoundToInt(gameSeconds / 60f))}m";
        }

        private readonly struct PresentedShipment
        {
            public PresentedShipment(GameObject root, LogisticsTransportVisualController visualController)
            {
                Root = root;
                VisualController = visualController;
            }

            public GameObject Root { get; }
            public LogisticsTransportVisualController VisualController { get; }
        }
    }
}
