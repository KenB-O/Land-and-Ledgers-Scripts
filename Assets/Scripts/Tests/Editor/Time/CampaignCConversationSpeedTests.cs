// Campaign C, package 14 — DEF-066 (real-time conversation rule),
// Nov 15, 2026.
//
// VERDICT: IMPLEMENTED. The Nov 13 design rule (a direct player-NPC
// conversation slows the simulation globally to real time; the
// previously selected speed restores automatically on exit; NPC-to-NPC
// conversations do not change player speed) had zero implementation:
// repo-wide grep over every conversation file returned zero hits for
// SetSpeed / TimeScale / speedMultiplier / SimulationSpeed /
// TimeManager, and the complete production SetSpeed caller list was
// TimeManager.SetPaused, WorldInteractionController Observe on/off,
// SaveLoadManager pause, ScenarioStartPanelController, PuppetMasterPanel,
// ScenarioValidationHarness, and the HUD buttons/dropdown/shortcuts.
//
// Hook-point verdict: SimulationTickContext.cs (the named candidate in
// the register) is NOT the hook point — it holds readonly data structs
// only (frame/tick/speed-changed contexts); speed is applied in
// TimeManager.Update via ResolveSpeedMultiplier(currentSpeed) and set
// via TimeManager.SetSpeed. The correct hook is the conversation
// session owner, AcquisitionSellerMeetingController (the only live
// direct player-NPC conversation session: opened player-initiated from
// OwnershipWorkflowController and GeneralStorePanelController; the
// ManagementPanelView notes direct resident conversations are "not
// connected to this screen yet"), mirroring the only existing
// slowdown-with-restore precedent — the Observe path in
// WorldInteractionController (capture previousObservationSpeed, set
// Observe, restore on cancel).
//
// What the fix does (ConversationSpeedGate, plain C# + controller
// wiring): entering the seller meeting (OpenState, or the unavailable
// readout paths that still show the meeting UI) slows the sim to
// SimulationSpeed.RealTime (1 game-second per real second — strictly
// slower than Speed1x's 60 and Observe's 10 at the default 1440 s
// day); closing restores the previously selected speed. Paused stays
// paused (never unpaused to slow down); a re-open while the meeting
// is already visible cannot overwrite the first capture; a speed
// change made mid-conversation by the player or another system wins
// and is not clobbered by the restore. NPC-to-NPC conversations run
// synchronously inside the daily DailySocialDrive batch through
// ConversationRunner.Converse (instantaneous calls, no session), so
// they never touch speed — pinned by the negative tests below.
// Delegated-manager negotiations (Check 9 Q5, owner question (u))
// are explicitly OUT OF SCOPE for this package.
//
// Three test kinds, same file:
//  (1) Real unit tests executing the gate's state machine.
//  (2) Structural tests pinning the controller wiring by reading the
//      production files.
//  (3) Negative pins: NPC-to-NPC conversation code touches no speed,
//      and the tick context applies no speed.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...). A missing repo fails loudly, never
// silently passes.
using System;
using System.IO;
using System.Text;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Time
{
    [TestFixture]
    public sealed class CampaignCConversationSpeedTests
    {
        // ---------- (1) Gate state machine ----------

        [Test]
        public void Begin_AtSpeed100x_ReturnsRealTime()
        {
            var gate = new ConversationSpeedGate();
            SimulationSpeed? result = gate.BeginConversation(SimulationSpeed.Speed100x);
            Assert.IsTrue(result.HasValue, "a fast speed must slow to real time on entering a conversation");
            Assert.AreEqual(SimulationSpeed.RealTime, result.Value, "the conversation speed is real time (1x)");
            Assert.IsTrue(gate.IsActive, "the conversation is open after Begin");
        }

        [Test]
        public void Begin_AtSpeed1x_ReturnsRealTime()
        {
            // Speed1x is NOT real time: at the default 1440 s day it
            // advances 60 game-seconds per real second, so it slows too.
            var gate = new ConversationSpeedGate();
            SimulationSpeed? result = gate.BeginConversation(SimulationSpeed.Speed1x);
            Assert.IsTrue(result.HasValue, "Speed1x must slow to real time on entering a conversation");
            Assert.AreEqual(SimulationSpeed.RealTime, result.Value);
        }

        [Test]
        public void Begin_AtSpeed500x_ReturnsRealTime()
        {
            var gate = new ConversationSpeedGate();
            SimulationSpeed? result = gate.BeginConversation(SimulationSpeed.Speed500x);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(SimulationSpeed.RealTime, result.Value);
        }

        [Test]
        public void Begin_AtObserve_ReturnsRealTime()
        {
            // Observe advances 10 game-seconds per real second — still
            // faster than real time's 1 — so it slows too.
            var gate = new ConversationSpeedGate();
            SimulationSpeed? result = gate.BeginConversation(SimulationSpeed.Observe);
            Assert.IsTrue(result.HasValue, "Observe must slow to real time on entering a conversation");
            Assert.AreEqual(SimulationSpeed.RealTime, result.Value);
        }

        [Test]
        public void Begin_AtRealTime_ReturnsNull_ConversationStillOpen()
        {
            var gate = new ConversationSpeedGate();
            SimulationSpeed? result = gate.BeginConversation(SimulationSpeed.RealTime);
            Assert.IsNull(result, "already at real time: no SetSpeed call is needed");
            Assert.IsTrue(gate.IsActive, "the conversation is open even though no slowdown was applied");
        }

        [Test]
        public void Begin_AtPaused_ReturnsNull_NeverUnpauses()
        {
            var gate = new ConversationSpeedGate();
            SimulationSpeed? result = gate.BeginConversation(SimulationSpeed.Paused);
            Assert.IsNull(result, "a paused sim must never be unpaused to slow it down");
            Assert.IsTrue(gate.IsActive, "the conversation is open even though no slowdown was applied");
        }

        [Test]
        public void End_WhenInactive_ReturnsNull()
        {
            var gate = new ConversationSpeedGate();
            Assert.IsFalse(gate.IsActive);
            SimulationSpeed? result = gate.EndConversation(SimulationSpeed.RealTime);
            Assert.IsNull(result, "a stray close must never rewrite the player's speed");
        }

        [Test]
        public void End_AfterBegin_RestoresPreviousSpeed()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.Speed100x);
            SimulationSpeed? result = gate.EndConversation(SimulationSpeed.RealTime);
            Assert.IsTrue(result.HasValue, "leaving the conversation restores the previously selected speed");
            Assert.AreEqual(SimulationSpeed.Speed100x, result.Value);
            Assert.IsFalse(gate.IsActive, "the conversation is closed after End");
        }

        [Test]
        public void Begin_Reentrant_SecondBeginIsNoop_FirstCaptureStands()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.Speed100x);
            SimulationSpeed? second = gate.BeginConversation(SimulationSpeed.Speed50x);
            Assert.IsNull(second, "a re-open while the meeting is already visible must not overwrite the first capture");
            SimulationSpeed? result = gate.EndConversation(SimulationSpeed.RealTime);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(SimulationSpeed.Speed100x, result.Value, "the restore uses the FIRST capture, not the re-open speed");
        }

        [Test]
        public void End_AfterMidConversationSpeedChange_ReturnsNull()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.Speed100x);
            SimulationSpeed? result = gate.EndConversation(SimulationSpeed.Speed50x);
            Assert.IsNull(result, "a speed the player selected mid-conversation wins and must not be clobbered by the restore");
            Assert.IsFalse(gate.IsActive, "the conversation is closed even though nothing was restored");
        }

        [Test]
        public void End_AfterMidConversationPause_ReturnsNull()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.Speed100x);
            SimulationSpeed? result = gate.EndConversation(SimulationSpeed.Paused);
            Assert.IsNull(result, "a pause made mid-conversation wins; the restore must not unpause it");
            Assert.IsFalse(gate.IsActive);
        }

        [Test]
        public void End_Twice_SecondEndIsNoop()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.Speed100x);
            SimulationSpeed? first = gate.EndConversation(SimulationSpeed.RealTime);
            Assert.IsTrue(first.HasValue);
            Assert.AreEqual(SimulationSpeed.Speed100x, first.Value);
            SimulationSpeed? second = gate.EndConversation(SimulationSpeed.Speed100x);
            Assert.IsNull(second, "a second close must not restore anything again");
        }

        [Test]
        public void Begin_AfterEnd_RearmsWithNewCapture()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.Speed100x);
            SimulationSpeed? firstRestore = gate.EndConversation(SimulationSpeed.RealTime);
            Assert.AreEqual(SimulationSpeed.Speed100x, firstRestore.Value);
            SimulationSpeed? secondBegin = gate.BeginConversation(SimulationSpeed.Speed25x);
            Assert.IsTrue(secondBegin.HasValue);
            Assert.AreEqual(SimulationSpeed.RealTime, secondBegin.Value);
            SimulationSpeed? secondRestore = gate.EndConversation(SimulationSpeed.RealTime);
            Assert.IsTrue(secondRestore.HasValue);
            Assert.AreEqual(SimulationSpeed.Speed25x, secondRestore.Value, "the second conversation captures its own previous speed");
        }

        [Test]
        public void End_AfterBeginAtRealTime_ReturnsNull()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.RealTime);
            SimulationSpeed? result = gate.EndConversation(SimulationSpeed.RealTime);
            Assert.IsNull(result, "no slowdown was applied, so no restore is owed");
            Assert.IsFalse(gate.IsActive);
        }

        [Test]
        public void End_AfterBeginAtPaused_ReturnsNull()
        {
            var gate = new ConversationSpeedGate();
            gate.BeginConversation(SimulationSpeed.Paused);
            SimulationSpeed? result = gate.EndConversation(SimulationSpeed.Paused);
            Assert.IsNull(result, "no slowdown was applied and the pause stands");
            Assert.IsFalse(gate.IsActive);
        }

        // ---------- (2) Controller wiring (structural) ----------

        private const string ControllerPath =
            "Assets/Scripts/Domains/Economy/Acquisitions/Workflows/AcquisitionSellerMeetingController.cs";
        private const string GatePath = "Assets/Scripts/Core/Time/ConversationSpeedGate.cs";
        private const string TickContextPath = "Assets/Scripts/Core/Time/SimulationTickContext.cs";
        private const string DailySocialDrivePath = "Assets/Scripts/Domains/Population/DailySocialDrive.cs";
        private const string ConversationOpportunityPath = "Assets/Scripts/Domains/Population/ConversationOpportunity.cs";

        [Test]
        public void Wiring_GateField_Present()
        {
            string text = ReadRepoFile(ControllerPath);
            Assert.IsTrue(text.Contains("using LandLedgers.Time;"),
                "the controller must import the time namespace for TimeManager and the gate");
            Assert.IsTrue(text.Contains("private readonly ConversationSpeedGate conversationSpeedGate = new ConversationSpeedGate();"),
                "the controller must own one gate instance for the session");
            Assert.IsTrue(text.Contains("private TimeManager timeManager;"),
                "the controller must hold the clock it applies the gate's decisions through");
        }

        [Test]
        public void Wiring_AutoWire_ResolvesTimeManager()
        {
            string text = ReadRepoFile(ControllerPath);
            Assert.IsTrue(text.Contains("timeManager ??= TimeManager.Instance ?? FindAnyObjectByType<TimeManager>();"),
                "AutoWire must resolve the clock (Instance first, scene lookup fallback, matching WorldInteractionController)");
        }

        [Test]
        public void Wiring_OpenState_AppliesConversationOpened()
        {
            string text = ReadRepoFile(ControllerPath);
            int openState = text.IndexOf("private void OpenState(AcquisitionConversationState state)", StringComparison.Ordinal);
            Assert.IsTrue(openState >= 0, "OpenState must exist");
            int opened = text.IndexOf("ApplyConversationOpened();", openState, StringComparison.Ordinal);
            int advanceDeal = text.IndexOf("private void AdvanceDeal()", openState, StringComparison.Ordinal);
            Assert.IsTrue(opened > openState && opened < advanceDeal,
                "OpenState must apply the conversation slowdown before the next method");
        }

        [Test]
        public void Wiring_Close_AppliesConversationClosed()
        {
            string text = ReadRepoFile(ControllerPath);
            int close = text.IndexOf("public void Close()", StringComparison.Ordinal);
            Assert.IsTrue(close >= 0, "Close must exist");
            int closed = text.IndexOf("ApplyConversationClosed();", close, StringComparison.Ordinal);
            int openedHelper = text.IndexOf("private void ApplyConversationOpened()", close, StringComparison.Ordinal);
            Assert.IsTrue(closed > close && closed < openedHelper,
                "Close must apply the conversation restore before the helper definitions");
        }

        [Test]
        public void Wiring_UnavailablePaths_ApplyConversationOpened()
        {
            string text = ReadRepoFile(ControllerPath);
            int clear = text.IndexOf("private void ClearConversationState(bool showView)", StringComparison.Ordinal);
            Assert.IsTrue(clear >= 0, "ClearConversationState must exist");
            int showGuard = text.IndexOf("if (showView)", clear, StringComparison.Ordinal);
            int openedAfterClear = text.IndexOf("ApplyConversationOpened();", showGuard, StringComparison.Ordinal);
            int openState = text.IndexOf("private void OpenState(AcquisitionConversationState state)", clear, StringComparison.Ordinal);
            Assert.IsTrue(showGuard > clear && openedAfterClear > showGuard && openedAfterClear < openState,
                "ClearConversationState must apply the slowdown when it shows the unavailable readout");
            int unavailable = text.IndexOf("private bool TryRenderUnavailableFromMarket", StringComparison.Ordinal);
            Assert.IsTrue(unavailable >= 0, "TryRenderUnavailableFromMarket must exist");
            int openedAfterUnavailable = text.IndexOf("ApplyConversationOpened();", unavailable, StringComparison.Ordinal);
            int renderAdvance = text.IndexOf("private void RenderAdvanceUnavailable", unavailable, StringComparison.Ordinal);
            Assert.IsTrue(openedAfterUnavailable > unavailable && openedAfterUnavailable < renderAdvance,
                "TryRenderUnavailableFromMarket must apply the slowdown when it shows the unavailable readout");
        }

        // ---------- (3) Negative pins ----------

        [Test]
        public void NpcConversations_DoNotTouchSpeed()
        {
            // The rule's second half: NPC-to-NPC conversations run
            // synchronously inside the daily social drive and must not
            // change player speed.
            string drive = ReadRepoFile(DailySocialDrivePath);
            Assert.IsFalse(drive.Contains("SetSpeed"), "DailySocialDrive must never set the speed");
            Assert.IsFalse(drive.Contains("ConversationSpeedGate"), "DailySocialDrive must never use the conversation gate");
            Assert.IsFalse(drive.Contains("SimulationSpeed"), "DailySocialDrive must never reference a speed value");
            string opportunity = ReadRepoFile(ConversationOpportunityPath);
            Assert.IsFalse(opportunity.Contains("SetSpeed"), "ConversationRunner must never set the speed");
            Assert.IsFalse(opportunity.Contains("ConversationSpeedGate"), "ConversationRunner must never use the conversation gate");
            Assert.IsFalse(opportunity.Contains("SimulationSpeed"), "ConversationRunner must never reference a speed value");
        }

        [Test]
        public void TickContext_IsDataOnly_NoSpeedApplication()
        {
            // The register's named hook-point candidate refuted: the tick
            // context holds readonly data structs only and applies no speed.
            string text = ReadRepoFile(TickContextPath);
            Assert.IsFalse(text.Contains("SetSpeed"), "SimulationTickContext must not set the speed");
            Assert.IsFalse(text.Contains("TimeManager"), "SimulationTickContext must not reference the clock");
        }

        [Test]
        public void Gate_IsPlainCSharp_NoUnityDependency()
        {
            string text = ReadRepoFile(GatePath);
            Assert.IsFalse(text.Contains("using UnityEngine"), "the gate must stay plain C# so the Roslyn harness can execute it");
            Assert.IsFalse(text.Contains("MonoBehaviour"), "the gate must not be a MonoBehaviour");
            Assert.IsTrue(text.Contains("public sealed class ConversationSpeedGate"), "the gate class must exist");
        }

        // ---------- Repo-root helper (copied from package 12) ----------

        private const string BootstrapperPath = "Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs";

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, BootstrapperPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                Directory.GetCurrentDirectory(),
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
                        if (File.Exists(Path.Combine(candidate, BootstrapperPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (package 14): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }
    }
}
