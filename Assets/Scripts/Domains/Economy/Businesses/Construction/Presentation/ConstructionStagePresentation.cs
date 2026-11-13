using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy
{
    /// <summary>
    /// A read-only mapping from one authoritative work-package phase to
    /// authored visual sections. For progressive log walls, put the sections
    /// in the order they should appear as real labor is recorded.
    /// </summary>
    [Serializable]
    public sealed class ConstructionStageVisualGroup
    {
        [Tooltip("Exact WorkPackage.PhaseName from the building design.")]
        public string PhaseName = string.Empty;

        [Tooltip("Existing mesh groups or reversible placeholders, in reveal order.")]
        public GameObject[] Sections = Array.Empty<GameObject>();
    }

    /// <summary>
    /// Read-only construction presentation. WorkPackageStatus and actual
    /// LaborMinutesCommitted remain authoritative; this component only toggles
    /// existing visual groups. It never creates materials, work, or progress.
    /// </summary>
    public sealed class ConstructionStagePresentation : MonoBehaviour
    {
        [SerializeField, Tooltip("Project ID this site presents. Bind it when the authorized project is assigned.")]
        private string projectId = string.Empty;

        [SerializeField, Tooltip("Map design phase names to existing visible sections. A phase may contain multiple progressive sections.")]
        private ConstructionStageVisualGroup[] stageGroups = Array.Empty<ConstructionStageVisualGroup>();

        [SerializeField, Tooltip("Finished building visual. Enabled only after the project authority marks the project Complete.")]
        private GameObject completedBuildingRoot;

        public string ProjectId => projectId;
        public ConstructionStageVisualGroup[] StageGroups
        {
            get => stageGroups;
            set => stageGroups = value ?? Array.Empty<ConstructionStageVisualGroup>();
        }
        public GameObject CompletedBuildingRoot
        {
            get => completedBuildingRoot;
            set => completedBuildingRoot = value;
        }

        public void BindProject(string authorizedProjectId)
        {
            projectId = authorizedProjectId ?? string.Empty;
        }

        /// <summary>
        /// Applies one authoritative project snapshot. A phase exposes a
        /// partial count from actual worked minutes; every section is withheld
        /// until that package is authoritatively Complete.
        /// </summary>
        public void Synchronize(ConstructionProject project)
        {
            if (project == null || string.IsNullOrWhiteSpace(projectId)
                || !string.Equals(project.ProjectId, projectId, StringComparison.Ordinal))
                return;

            bool projectComplete = project.Status == ConstructionProjectStatus.Complete;
            foreach (ConstructionStageVisualGroup group in stageGroups ?? Array.Empty<ConstructionStageVisualGroup>())
            {
                if (group?.Sections == null) continue;
                // Cancellation stops future advancement, but already completed
                // packages and physically worked partial sections remain on site.
                int visible = CompletedSectionCount(project, group);
                for (int i = 0; i < group.Sections.Length; i++)
                {
                    GameObject section = group.Sections[i];
                    if (section != null) section.SetActive(!projectComplete && i < visible);
                }
            }

            if (completedBuildingRoot != null)
                completedBuildingRoot.SetActive(projectComplete);
        }

        /// <summary>Refreshes configured site presenters after the construction driver advances.</summary>
        public static void RefreshAll(IList<ConstructionProject> projects)
        {
            if (projects == null || projects.Count == 0) return;
            ConstructionStagePresentation[] presenters = FindObjectsByType<ConstructionStagePresentation>(FindObjectsSortMode.None);
            foreach (ConstructionStagePresentation presenter in presenters)
            {
                if (presenter == null || string.IsNullOrWhiteSpace(presenter.projectId)) continue;
                foreach (ConstructionProject project in projects)
                {
                    if (project == null || !string.Equals(project.ProjectId, presenter.projectId, StringComparison.Ordinal)) continue;
                    presenter.Synchronize(project);
                    break;
                }
            }
        }

        private static int CompletedSectionCount(ConstructionProject project, ConstructionStageVisualGroup group)
        {
            int sectionCount = group.Sections.Length;
            if (sectionCount == 0) return 0;
            int requiredMinutes = 0;
            int committedMinutes = 0;
            bool found = false;
            bool allComplete = true;
            foreach (WorkPackage package in project.WorkPackages ?? new List<WorkPackage>())
            {
                if (package == null || !string.Equals(package.PhaseName, group.PhaseName, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                if (package.Status != WorkPackageStatus.Complete) allComplete = false;
                requiredMinutes += Math.Max(0, package.LaborMinutesRequired);
                committedMinutes += Math.Min(Math.Max(0, package.LaborMinutesCommitted), Math.Max(0, package.LaborMinutesRequired));
            }
            if (!found) return 0;
            if (allComplete) return sectionCount;
            if (requiredMinutes <= 0) return 0;

            int countFromWorkedTime = Mathf.FloorToInt(
                Mathf.Clamp01((float)committedMinutes / requiredMinutes) * sectionCount);
            // A fully worked but not yet advanced package is still in progress;
            // keep one section hidden until the authority records completion.
            return Mathf.Clamp(countFromWorkedTime, 0, Math.Max(0, sectionCount - 1));
        }
    }
}
