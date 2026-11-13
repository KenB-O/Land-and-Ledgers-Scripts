using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class ConstructionStagePresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects)
                if (item != null) Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void FramingSectionsFollowWorkedLabor_AndFullStageWaitsForAuthorityCompletion()
        {
            ConstructionStagePresentation presentation = CreatePresentation("project-1");
            GameObject[] sections = CreateSections("frame", 4);
            GameObject finished = CreateObject("finished building");
            presentation.StageGroups = new[]
            {
                new ConstructionStageVisualGroup { PhaseName = "Framing", Sections = sections },
            };
            presentation.CompletedBuildingRoot = finished;

            var package = new WorkPackage
            {
                PackageId = "frame-package",
                ProjectId = "project-1",
                PhaseName = "Framing",
                LaborMinutesRequired = 100,
                LaborMinutesCommitted = 50,
                Status = WorkPackageStatus.InProgress,
            };
            var project = new ConstructionProject
            {
                ProjectId = "project-1",
                Status = ConstructionProjectStatus.InProgress,
                WorkPackages = new List<WorkPackage> { package },
            };

            presentation.Synchronize(project);
            AssertVisible(sections, 2);
            Assert.IsFalse(finished.activeSelf, "completion appearance cannot precede project completion");

            package.LaborMinutesCommitted = 100;
            presentation.Synchronize(project);
            AssertVisible(sections, 3, "last section remains hidden until AdvancePackage records Complete");

            package.Status = WorkPackageStatus.Complete;
            presentation.Synchronize(project);
            AssertVisible(sections, 4);
            Assert.IsFalse(finished.activeSelf);

            project.Status = ConstructionProjectStatus.Complete;
            presentation.Synchronize(project);
            AssertVisible(sections, 0);
            Assert.IsTrue(finished.activeSelf);
        }

        [Test]
        public void UnstartedOrUnmatchedStagesStayHidden_AndCancellationPreservesCompletedSections()
        {
            ConstructionStagePresentation presentation = CreatePresentation("project-2");
            GameObject[] sections = CreateSections("log wall", 3);
            presentation.StageGroups = new[]
            {
                new ConstructionStageVisualGroup { PhaseName = "Framing", Sections = sections },
            };

            var project = new ConstructionProject
            {
                ProjectId = "another-project",
                Status = ConstructionProjectStatus.Complete,
                WorkPackages = new List<WorkPackage>
                {
                    new WorkPackage { PhaseName = "Framing", Status = WorkPackageStatus.Complete },
                },
            };
            presentation.Synchronize(project);
            AssertVisible(sections, 0, "a site must not display another project's work");

            project.ProjectId = "project-2";
            project.Status = ConstructionProjectStatus.InProgress;
            project.WorkPackages[0].Status = WorkPackageStatus.Ready;
            presentation.Synchronize(project);
            AssertVisible(sections, 0, "a planned phase is not built progress");

            project.Status = ConstructionProjectStatus.Cancelled;
            project.WorkPackages[0].Status = WorkPackageStatus.Complete;
            presentation.Synchronize(project);
            AssertVisible(sections, 3, "cancellation preserves sections that the package authority already completed");
        }

        [Test]
        public void RefreshAllSynchronizesOnlyThePresenterBoundToTheAdvancedProject()
        {
            ConstructionStagePresentation presentation = CreatePresentation("project-refresh");
            GameObject[] sections = CreateSections("roof", 4);
            presentation.StageGroups = new[]
            {
                new ConstructionStageVisualGroup { PhaseName = "Roof", Sections = sections },
            };
            var project = new ConstructionProject
            {
                ProjectId = "project-refresh",
                Status = ConstructionProjectStatus.InProgress,
                WorkPackages = new List<WorkPackage>
                {
                    new WorkPackage
                    {
                        PackageId = "roof-1",
                        PhaseName = "Roof",
                        LaborMinutesRequired = 100,
                        LaborMinutesCommitted = 25,
                        Status = WorkPackageStatus.InProgress,
                    },
                },
            };

            ConstructionStagePresentation.RefreshAll(new List<ConstructionProject>
            {
                new ConstructionProject { ProjectId = "different-project", Status = ConstructionProjectStatus.Complete },
                project,
            });

            AssertVisible(sections, 1);
        }

        private ConstructionStagePresentation CreatePresentation(string projectId)
        {
            GameObject root = CreateObject("construction presentation");
            ConstructionStagePresentation presentation = root.AddComponent<ConstructionStagePresentation>();
            presentation.BindProject(projectId);
            return presentation;
        }

        private GameObject[] CreateSections(string prefix, int count)
        {
            var result = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = CreateObject(prefix + " " + i);
                result[i].SetActive(false);
            }
            return result;
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private static void AssertVisible(GameObject[] sections, int expected, string message = null)
        {
            int visible = 0;
            foreach (GameObject section in sections)
                if (section.activeSelf) visible++;
            Assert.AreEqual(expected, visible, message);
            for (int i = 0; i < sections.Length; i++)
                Assert.AreEqual(i < expected, sections[i].activeSelf, "sections reveal in their authored order");
        }
    }
}
