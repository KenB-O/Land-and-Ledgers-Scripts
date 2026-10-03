using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment.Workstations
{
    /// <summary>
    /// EQP-2: the workstation instances a business holds. One registry per
    /// business instance; evaluation stays with the callers (they own the
    /// component finders), so this class never takes a hard dependency on any
    /// asset authority.
    /// </summary>
    [Serializable]
    public sealed class BusinessWorkstations
    {
        public string BusinessInstanceId = string.Empty;

        private readonly Dictionary<string, WorkstationInstance> stations =
            new Dictionary<string, WorkstationInstance>(StringComparer.Ordinal);

        public BusinessWorkstations() { }

        public BusinessWorkstations(string businessInstanceId)
        {
            BusinessInstanceId = businessInstanceId ?? string.Empty;
        }

        public WorkstationInstance GetOrCreate(string workstationId)
        {
            if (string.IsNullOrWhiteSpace(workstationId)) return null;
            if (!stations.TryGetValue(workstationId, out var station))
            {
                station = new WorkstationInstance
                {
                    InstanceId = workstationId + "-" + BusinessInstanceId,
                    WorkstationId = workstationId,
                    BusinessInstanceId = BusinessInstanceId,
                };
                stations[workstationId] = station;
            }
            return station;
        }

        public WorkstationInstance Get(string workstationId)
        {
            if (string.IsNullOrWhiteSpace(workstationId)) return null;
            stations.TryGetValue(workstationId, out var station);
            return station;
        }

        public IEnumerable<WorkstationInstance> All => stations.Values;

        /// <summary>
        /// Returns null when the workstation is established and ready;
        /// "not established" or component reasons otherwise. Never invents readiness.
        /// </summary>
        public string CheckReady(
            string workstationId,
            WorkstationDefinition def,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var station = Get(workstationId);
            if (station == null)
            {
                string reason = $"Business {BusinessInstanceId}: workstation '{workstationId}' not established.";
                diagnostics.Add(reason);
                return reason;
            }
            return station.EvaluateReady(def, findComponent, diagnostics);
        }
    }
}
