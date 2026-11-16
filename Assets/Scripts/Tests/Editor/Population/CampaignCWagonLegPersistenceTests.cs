// Campaign C (DEF-047) — the wagon controller's in-flight leg state is not
// persisted at all: legActive, regionalLegActive, path, remaining metres and
// physicalOrigin/DestinationLocationId are runtime-only, so a Save/Continue
// mid-haul leaves the shipment restored but the wagon with no leg. This
// package persists the leg (additive WagonLegSaveDto on
// LogisticsRuntimeSaveDto) and resumes it on load via Rebind, without
// changing wagon movement, routing, or arrival semantics.
//
// Two test kinds, same file:
//  (1) Real unit tests for the pure WagonLegRestoreRules (zero Unity deps).
//  (2) Structural tests pinning the Unity-bound wiring by reading the
//      production files — the controller, presentation controller, logistics
//      manager and SimulationDrivers are too large a closure for the
//      focused harness, so the wiring itself (not Unity behaviour) is what
//      is asserted here, exactly like the package-2 guard, package-3 rebind
//      and package-4 truncation tests.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...), mirroring UnwiredSystemsRegistryTests. A
// missing repo fails loudly, never silently passes.
using System;
using System.IO;
using System.Text;
using LandLedgers.Economy.Transport;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCWagonLegPersistenceTests
    {
        private const string SaveDtosPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveDtos.cs";
        private const string ControllerPath =
            "Assets/Scripts/Domains/Economy/Transport/CoupledTransportMovementController.cs";
        private const string PresentationPath =
            "Assets/Scripts/Domains/Economy/Transport/OpeningTransportPresentationController.cs";
        private const string ManagerPath =
            "Assets/Scripts/Domains/Economy/Logistics/LogisticsRuntimeManager.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";

        // ---------- (1) WagonLegRestoreRules ----------

        [Test]
        public void IsRestorable_TrueForAValidGridLeg()
        {
            Assert.IsTrue(WagonLegRestoreRules.IsRestorable(
                isActive: true, pathCellCount: 12, cellSizeMetres: 2f,
                wagonSpeedMetresPerSecond: 2.2f, pairSeparationMetres: 2.3736f, ethanPersonId: 7));
        }

        [Test]
        public void IsRestorable_TrueForAValidRegionalLeg()
        {
            // Regional legs carry the same path/params snapshot; only the
            // kind flag differs, and the rule does not distinguish kinds.
            Assert.IsTrue(WagonLegRestoreRules.IsRestorable(
                isActive: true, pathCellCount: 12, cellSizeMetres: 2f,
                wagonSpeedMetresPerSecond: 2.2f, pairSeparationMetres: 2.3736f, ethanPersonId: 7));
        }

        [Test]
        public void IsRestorable_FalseWhenNoLegWasActive()
        {
            Assert.IsFalse(WagonLegRestoreRules.IsRestorable(
                isActive: false, pathCellCount: 12, cellSizeMetres: 2f,
                wagonSpeedMetresPerSecond: 2.2f, pairSeparationMetres: 2.3736f, ethanPersonId: 7));
        }

        [Test]
        public void IsRestorable_FalseForDegeneratePathsAndParameters()
        {
            // One cell (StartLeg refuses fewer than two).
            Assert.IsFalse(WagonLegRestoreRules.IsRestorable(
                true, 1, 2f, 2.2f, 2.3736f, 7));
            // Zero cells.
            Assert.IsFalse(WagonLegRestoreRules.IsRestorable(
                true, 0, 2f, 2.2f, 2.3736f, 7));
            // Zero cell size.
            Assert.IsFalse(WagonLegRestoreRules.IsRestorable(
                true, 12, 0f, 2.2f, 2.3736f, 7));
            // Zero wagon speed.
            Assert.IsFalse(WagonLegRestoreRules.IsRestorable(
                true, 12, 2f, 0f, 2.3736f, 7));
            // Negative separation.
            Assert.IsFalse(WagonLegRestoreRules.IsRestorable(
                true, 12, 2f, 2.2f, -1f, 7));
            // No driver identity.
            Assert.IsFalse(WagonLegRestoreRules.IsRestorable(
                true, 12, 2f, 2.2f, 2.3736f, -1));
        }

        [Test]
        public void ClampDistanceTravelled_KeepsMidPathDistances()
        {
            Assert.AreEqual(10f, WagonLegRestoreRules.ClampDistanceTravelled(10f, 24f), 0.001f);
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampDistanceTravelled(0f, 24f), 0.001f);
        }

        [Test]
        public void ClampDistanceTravelled_ClampsPastTheEndButKeepsIt()
        {
            // At/past the total length the distance is kept (clamped to the
            // end): the next tick's advance completes the leg through the
            // normal arrival path instead of refusing the resume.
            Assert.AreEqual(24f, WagonLegRestoreRules.ClampDistanceTravelled(24f, 24f), 0.001f);
            Assert.AreEqual(24f, WagonLegRestoreRules.ClampDistanceTravelled(30f, 24f), 0.001f);
        }

        [Test]
        public void ClampDistanceTravelled_NegativeOrNaNRestartsAtPathStart()
        {
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampDistanceTravelled(-5f, 24f), 0.001f);
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampDistanceTravelled(float.NaN, 24f), 0.001f);
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampDistanceTravelled(10f, 0f), 0.001f);
        }

        [Test]
        public void ClampRegionalElapsed_KeepsMidTimerValues()
        {
            Assert.AreEqual(30f, WagonLegRestoreRules.ClampRegionalElapsed(30f, 120f), 0.001f);
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampRegionalElapsed(0f, 120f), 0.001f);
        }

        [Test]
        public void ClampRegionalElapsed_ClampsPastTheDurationButKeepsIt()
        {
            // At/past the duration the elapsed time is kept: the next tick
            // completes the regional leg instead of refusing the resume.
            Assert.AreEqual(120f, WagonLegRestoreRules.ClampRegionalElapsed(120f, 120f), 0.001f);
            Assert.AreEqual(120f, WagonLegRestoreRules.ClampRegionalElapsed(200f, 120f), 0.001f);
        }

        [Test]
        public void ClampRegionalElapsed_NegativeOrNaNRestartsTheTimer()
        {
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampRegionalElapsed(-5f, 120f), 0.001f);
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampRegionalElapsed(float.NaN, 120f), 0.001f);
            Assert.AreEqual(0f, WagonLegRestoreRules.ClampRegionalElapsed(30f, 0f), 0.001f);
        }

        // ---------- (2) Structural wiring ----------

        [Test]
        public void Dto_HasTheFullLegSnapshotAndAbsentFieldOnLogistics()
        {
            string code = StripNonCode(ReadRepoFile(SaveDtosPath));
            Assert.IsTrue(code.Contains("public sealed class WagonLegSaveDto"),
                "SaveDtos.cs must define WagonLegSaveDto.");
            foreach (string field in new[]
            {
                "public bool isActive;",
                "public bool isRegional;",
                "public bool regionalReturnToTownPath;",
                "public List<GridCoordSaveDto> path = new();",
                "public float distanceTravelledMetres;",
                "public float cellSizeMetres;",
                "public float gridOriginX;",
                "public float gridOriginY;",
                "public float gridOriginZ;",
                "public float wagonSpeedMetresPerSecond;",
                "public float pairSeparationMetres;",
                "public float turnSpeedDegreesPerSecond;",
                "public int ethanPersonId = -1;",
                "public string legLabel = string.Empty;",
                "public bool hideOnArrival;",
                "public float regionalTargetX;",
                "public float regionalTargetY;",
                "public float regionalTargetZ;",
                "public float regionalTravelElapsedSeconds;",
                "public float regionalTravelDurationSeconds;",
                "public string physicalJourneyId = string.Empty;",
                "public string physicalDestinationLocationId = string.Empty;",
                "public string physicalDestinationBusinessId = string.Empty;",
                "public string physicalVehicleId = string.Empty;",
                "public string physicalOriginLocationId = string.Empty;",
            })
            {
                Assert.IsTrue(code.Contains(field), "WagonLegSaveDto is missing field: " + field);
            }
            Assert.IsTrue(code.Contains("public WagonLegSaveDto wagonLeg;"),
                "LogisticsRuntimeSaveDto must carry the additive wagonLeg field.");
            Assert.IsFalse(code.Contains("wagonLeg = new()"),
                "wagonLeg must stay null by default: an old save without the section restores no leg.");
        }

        [Test]
        public void Manager_CapturesTheLegOnSaveAndHoldsItPendingOnLoad()
        {
            string code = StripNonCode(ReadRepoFile(ManagerPath));
            Assert.IsTrue(code.Contains("wagonLeg = FindAnyObjectByType<CoupledTransportMovementController>()?.CaptureLegSaveDto()"),
                "CaptureSaveDto must snapshot the controller's leg (or null when idle).");
            Assert.IsTrue(code.Contains("restoredWagonLeg = dto?.wagonLeg;"),
                "LoadFromSaveDto must hold the restored leg pending for Rebind; a null dto clears it.");
            Assert.IsTrue(code.Contains("public WagonLegSaveDto RestoredWagonLeg => restoredWagonLeg;"),
                "The manager must expose the pending restored leg.");
            Assert.IsTrue(code.Contains("public void ClearRestoredWagonLeg()"),
                "The manager must be able to consume the pending leg exactly once.");
        }

        [Test]
        public void Controller_CapturesNullWhenIdleAndRestoresThroughTheRules()
        {
            string code = StripNonCode(ReadRepoFile(ControllerPath));
            Assert.IsTrue(code.Contains("public WagonLegSaveDto CaptureLegSaveDto()"),
                "The controller must expose CaptureLegSaveDto.");
            string capture = MethodBody(code, "public WagonLegSaveDto CaptureLegSaveDto()");
            Assert.IsTrue(capture.Contains("if (!legActive && !regionalLegActive) return null;"),
                "Capture must return null when no leg is active.");
            Assert.IsTrue(code.Contains("public bool RestoreLeg(WagonLegSaveDto dto)"),
                "The controller must expose RestoreLeg.");
            string restore = MethodBody(code, "public bool RestoreLeg(WagonLegSaveDto dto)");
            Assert.IsTrue(restore.Contains("if (IsLegActive)"),
                "RestoreLeg must refuse when a leg is already active — a restore never overwrites a live leg.");
            Assert.IsTrue(restore.Contains("WagonLegRestoreRules.IsRestorable("),
                "RestoreLeg must validate the snapshot through the pure rules.");
            Assert.IsTrue(restore.Contains("PlaceTeamAtDistance(distanceTravelledMetres, 0f, out _, emitMovementSegment: false)"),
                "A resumed grid leg must be placed at its saved distance without reporting a teleport as travel.");
            Assert.IsTrue(restore.Contains("SetRegionalPresentationVisible(false)"),
                "A resumed regional leg must stay hidden beyond the boundary until it completes.");
        }

        [Test]
        public void Controller_AdvanceStillPlacesThroughTheSharedMethod()
        {
            string code = StripNonCode(ReadRepoFile(ControllerPath));
            Assert.IsTrue(code.Contains("private bool PlaceTeamAtDistance("),
                "Placement must be a shared method used by both advance and restore.");
            string advance = MethodBody(code, "public void AdvanceSimulationTime(float simulationDeltaSeconds)");
            Assert.IsTrue(advance.Contains("PlaceTeamAtDistance(distanceTravelledMetres, simulationDeltaSeconds, out float remainingMetres, emitMovementSegment: true)"),
                "AdvanceSimulationTime must keep placing through the shared method with movement segments on.");
        }

        [Test]
        public void Presentation_BoardsTheRiderBeforeRestoringTheLeg()
        {
            string code = StripNonCode(ReadRepoFile(PresentationPath));
            Assert.IsTrue(code.Contains("public bool ResumeCoupledLeg(WagonLegSaveDto dto)"),
                "The presentation controller must expose ResumeCoupledLeg.");
            string resume = MethodBody(code, "public bool ResumeCoupledLeg(WagonLegSaveDto dto)");
            Assert.IsTrue(resume.Contains("if (controller.IsLegActive)"),
                "Resume must refuse when a leg is already active.");
            int board = resume.IndexOf("TryBoardRiderForLeg(director, dto.ethanPersonId, dto.legLabel)", StringComparison.Ordinal);
            int restore = resume.IndexOf("controller.RestoreLeg(dto)", StringComparison.Ordinal);
            Assert.IsTrue(board >= 0, "Resume must board the rider.");
            Assert.IsTrue(restore >= 0, "Resume must call the controller's RestoreLeg.");
            Assert.IsTrue(board < restore, "Resume must board the rider BEFORE restoring the leg (Begin-method order).");
        }

        [Test]
        public void Drivers_RebindResumesTheLegAndFailsLoudlyWhenItCannot()
        {
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string rebind = MethodBody(code, "public void Rebind()");
            int shopping = rebind.IndexOf("ApplyRestoredShoppingTrips();", StringComparison.Ordinal);
            int wagon = rebind.IndexOf("ApplyRestoredWagonLeg();", StringComparison.Ordinal);
            Assert.IsTrue(shopping >= 0, "Rebind must still rehydrate shopping trips.");
            Assert.IsTrue(wagon >= 0, "Rebind must resume the restored wagon leg.");
            Assert.IsTrue(shopping < wagon, "The wagon resume must run after the shopping-trip rehydration in Rebind.");
            Assert.IsTrue(code.Contains("public void ApplyRestoredWagonLeg()"),
                "SimulationDrivers must expose ApplyRestoredWagonLeg.");
            string apply = MethodBody(code, "public void ApplyRestoredWagonLeg()");
            Assert.IsTrue(apply.Contains("logisticsRuntime.RestoredWagonLeg"),
                "ApplyRestoredWagonLeg must read the pending leg from the logistics runtime.");
            Assert.IsTrue(apply.Contains("if (restoredLeg == null || !restoredLeg.isActive)"),
                "ApplyRestoredWagonLeg must no-op on a New Game rebind or a load without a leg.");
            Assert.IsTrue(apply.Contains("ClearRestoredWagonLeg()"),
                "ApplyRestoredWagonLeg must consume the pending leg exactly once.");
            Assert.IsTrue(apply.Contains("transportPresentation.ResumeCoupledLeg(restoredLeg)"),
                "ApplyRestoredWagonLeg must resume through the presentation controller.");
            Assert.IsTrue(apply.Contains("Debug.LogWarning"),
                "A refused or impossible resume must fail loudly, not silently drop the leg.");
        }

        // ---------- structural helpers (same pattern as the package-3 rebind tests) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, SaveDtosPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                System.IO.Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-c"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, SaveDtosPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-047): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-047): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so brace matching and call counting
        /// cannot be fooled by a mention inside a comment or a log string.
        /// </summary>
        internal static string StripNonCode(string source)
        {
            var out_ = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    {
                        out_.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append("  ");
                        i += 2;
                    }
                }
                else if (c == '@' && next == '"')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length)
                    {
                        if (source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"')
                        {
                            out_.Append("  ");
                            i += 2;
                            continue;
                        }
                        if (source[i] == '"') break;
                        out_.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else if (c == '"')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                            continue;
                        }
                        if (source[i] == '"') break;
                        out_.Append(' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else if (c == '\'')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                            continue;
                        }
                        if (source[i] == '\'') break;
                        out_.Append(' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else
                {
                    out_.Append(c);
                    i++;
                }
            }
            return out_.ToString();
        }

        /// <summary>
        /// Returns the brace-matched body of the first method whose
        /// signature line starts with <paramref name="signature"/>, from
        /// code already stripped by StripNonCode. Fails the test when the
        /// method is not found.
        /// </summary>
        internal static string MethodBody(string strippedCode, string signature)
        {
            int sig = strippedCode.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(sig >= 0, "Campaign C (DEF-047): method not found: " + signature);
            int open = strippedCode.IndexOf('{', sig);
            Assert.IsTrue(open >= 0, "Campaign C (DEF-047): method has no body: " + signature);
            int depth = 0;
            for (int i = open; i < strippedCode.Length; i++)
            {
                if (strippedCode[i] == '{') depth++;
                else if (strippedCode[i] == '}')
                {
                    depth--;
                    if (depth == 0) return strippedCode.Substring(open, i - open + 1);
                }
            }
            Assert.IsTrue(false, "Campaign C (DEF-047): unbalanced braces in method: " + signature);
            return null;
        }
    }
}
