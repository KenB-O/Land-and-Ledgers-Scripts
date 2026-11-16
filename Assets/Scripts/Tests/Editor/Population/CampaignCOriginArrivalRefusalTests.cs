// Campaign C, package 9 — DEF-048 (wagon origin-arrival refusal is
// terminal), Nov 15, 2026.
//
// VERDICT (verify-then-fix): the refusal is terminal by design, and no
// retry loop is added — every pre-check cause is permanent once the
// return leg has started (destination confirmation has exactly one
// production caller, the controller's OnRegionalDestinationTrigger, which
// cannot fire again once the team is home; the confirmation flags are
// write-once; ids are set at journey setup; journey cancellation only
// ever runs on the departure path). A tick/day retry of the confirmation
// would re-fail forever. The disposition after a refusal — retry the leg,
// re-drive the return, or declare the haul failed with its cargo released
// — is A-to-Z Check 10 question 5, an owner decision, and is NOT
// invented here.
//
// What IS fixed here is diagnostic, in two layers:
//  (1) The origin pre-check in
//      SimulationDrivers.ConfirmPhysicalFreightOriginArrival used to
//      return false SILENTLY, so every refusal looked identical in the
//      Console. OriginArrivalRefusalReason.Describe now names the first
//      failing check, and the driver logs it once, at the refusal. The
//      destination-side method is deliberately untouched: its retry loop
//      calls it every tick, so per-call logging there would spam.
//  (2) The controller records refused origin journey ids (runtime-only),
//      so the daily return driver can tell a team that already came home
//      from one still waiting at the supplier to begin its return, and
//      log a distinct day-keyed line instead of the misleading "waiting
//      to begin" line (BeginCoupledPhysicalReturn requires the team
//      within 8 m of the supplier destination, which can never hold once
//      the team is home at staging).
//
// Two test kinds, same file:
//  (1) Real unit tests for the pure reason helper — every refusal reason
//      plus the pass case and first-failure ordering.
//  (2) Structural tests pinning the Unity-bound wiring by reading the
//      production files — SimulationDrivers and the controller are too
//      large a closure for the focused harness, so the wiring itself
//      (not Unity behaviour) is what is asserted here, exactly like the
//      package-2/3/4/5/7/8 tests.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...). A missing repo fails loudly, never silently
// passes.
using System;
using System.IO;
using System.Text;
using LandLedgers.Economy.Transport;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCOriginArrivalRefusalTests
    {
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string ControllerPath =
            "Assets/Scripts/Domains/Economy/Transport/CoupledTransportMovementController.cs";
        private const string SaveLoadManagerPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs";

        // ---------- (1) Reason helper: unit tests ----------

        private static OriginArrivalRefusalReason.JourneyState MatchingVehicle()
        {
            return new OriginArrivalRefusalReason.JourneyState(
                "j-veh-1", "Vehicle", -1, true, true,
                "town-center", "millbrook", "gateway-1");
        }

        private static OriginArrivalRefusalReason.JourneyState MatchingPerson()
        {
            return new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Person", 7, true, true,
                "town-center", "millbrook", "gateway-1");
        }

        private static string DescribeWith(
            OriginArrivalRefusalReason.JourneyState? vehicle,
            OriginArrivalRefusalReason.JourneyState? person,
            string vehicleId = "opening-freight-wagon",
            int operatorPersonId = 7,
            string vehicleJourneyId = "j-veh-1",
            string originLocationId = "town-center")
        {
            return OriginArrivalRefusalReason.Describe(
                vehicleId, operatorPersonId, vehicleJourneyId, originLocationId,
                vehicle, person);
        }

        [Test]
        public void MatchingStateReturnsNull()
        {
            Assert.IsNull(DescribeWith(MatchingVehicle(), MatchingPerson()));
        }

        [Test]
        public void BlankVehicleIdRefused()
        {
            string reason = DescribeWith(MatchingVehicle(), MatchingPerson(), vehicleId: "");
            Assert.IsTrue(reason.Contains("no vehicle id was supplied"), "Reason was: " + reason);
        }

        [Test]
        public void NegativeOperatorRefused()
        {
            string reason = DescribeWith(MatchingVehicle(), MatchingPerson(), operatorPersonId: -1);
            Assert.IsTrue(reason.Contains("no operator person id was supplied"), "Reason was: " + reason);
        }

        [Test]
        public void BlankVehicleJourneyIdRefused()
        {
            string reason = DescribeWith(MatchingVehicle(), MatchingPerson(), vehicleJourneyId: "");
            Assert.IsTrue(reason.Contains("no vehicle journey id was supplied"), "Reason was: " + reason);
        }

        [Test]
        public void BlankOriginLocationIdRefused()
        {
            string reason = DescribeWith(MatchingVehicle(), MatchingPerson(), originLocationId: "");
            Assert.IsTrue(reason.Contains("no origin location id was supplied"), "Reason was: " + reason);
        }

        [Test]
        public void VehicleJourneyMissingRefused()
        {
            string reason = DescribeWith(null, MatchingPerson());
            Assert.IsTrue(reason.Contains("no active journey for vehicle 'opening-freight-wagon'"), "Reason was: " + reason);
        }

        [Test]
        public void PersonJourneyMissingRefused()
        {
            string reason = DescribeWith(MatchingVehicle(), null);
            Assert.IsTrue(reason.Contains("no active journey for operator P7"), "Reason was: " + reason);
        }

        [Test]
        public void VehicleJourneyIdMismatchRefused()
        {
            OriginArrivalRefusalReason.JourneyState vehicle = new OriginArrivalRefusalReason.JourneyState(
                "j-veh-OTHER", "Vehicle", -1, true, true,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(vehicle, MatchingPerson());
            Assert.IsTrue(reason.Contains("does not match the leg's journey 'j-veh-1'"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("'j-veh-OTHER'"), "Reason was: " + reason);
        }

        [Test]
        public void VehicleKindMismatchRefused()
        {
            OriginArrivalRefusalReason.JourneyState vehicle = new OriginArrivalRefusalReason.JourneyState(
                "j-veh-1", "Person", -1, true, true,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(vehicle, MatchingPerson());
            Assert.IsTrue(reason.Contains("not a vehicle journey"), "Reason was: " + reason);
        }

        [Test]
        public void PersonKindMismatchRefused()
        {
            OriginArrivalRefusalReason.JourneyState person = new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Vehicle", 7, true, true,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(MatchingVehicle(), person);
            Assert.IsTrue(reason.Contains("not a person journey"), "Reason was: " + reason);
        }

        [Test]
        public void PersonIdMismatchRefused()
        {
            OriginArrivalRefusalReason.JourneyState person = new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Person", 9, true, true,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(MatchingVehicle(), person);
            Assert.IsTrue(reason.Contains("belongs to P9, not P7"), "Reason was: " + reason);
        }

        [Test]
        public void VehicleNotRequiringConfirmationRefused()
        {
            OriginArrivalRefusalReason.JourneyState vehicle = new OriginArrivalRefusalReason.JourneyState(
                "j-veh-1", "Vehicle", -1, false, true,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(vehicle, MatchingPerson());
            Assert.IsTrue(reason.Contains("is not configured for physical arrival confirmation"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("vehicle journey 'j-veh-1'"), "Reason was: " + reason);
        }

        [Test]
        public void PersonNotRequiringConfirmationRefused()
        {
            OriginArrivalRefusalReason.JourneyState person = new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Person", 7, false, true,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(MatchingVehicle(), person);
            Assert.IsTrue(reason.Contains("is not configured for physical arrival confirmation"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("operator journey 'j-per-1'"), "Reason was: " + reason);
        }

        [Test]
        public void VehicleDestinationUnconfirmedRefused()
        {
            OriginArrivalRefusalReason.JourneyState vehicle = new OriginArrivalRefusalReason.JourneyState(
                "j-veh-1", "Vehicle", -1, true, false,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(vehicle, MatchingPerson());
            Assert.IsTrue(reason.Contains("has no confirmed destination arrival"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("vehicle journey 'j-veh-1'"), "Reason was: " + reason);
        }

        [Test]
        public void PersonDestinationUnconfirmedRefused()
        {
            OriginArrivalRefusalReason.JourneyState person = new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Person", 7, true, false,
                "town-center", "millbrook", "gateway-1");
            string reason = DescribeWith(MatchingVehicle(), person);
            Assert.IsTrue(reason.Contains("has no confirmed destination arrival"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("operator journey 'j-per-1'"), "Reason was: " + reason);
        }

        [Test]
        public void VehicleOriginMismatchRefused()
        {
            OriginArrivalRefusalReason.JourneyState vehicle = new OriginArrivalRefusalReason.JourneyState(
                "j-veh-1", "Vehicle", -1, true, true,
                "other-origin", "millbrook", "gateway-1");
            string reason = DescribeWith(vehicle, MatchingPerson());
            Assert.IsTrue(reason.Contains("is not the leg's origin 'town-center'"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("vehicle journey 'j-veh-1'"), "Reason was: " + reason);
        }

        [Test]
        public void PersonOriginMismatchRefused()
        {
            OriginArrivalRefusalReason.JourneyState person = new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Person", 7, true, true,
                "other-origin", "millbrook", "gateway-1");
            string reason = DescribeWith(MatchingVehicle(), person);
            Assert.IsTrue(reason.Contains("is not the leg's origin 'town-center'"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("operator journey 'j-per-1'"), "Reason was: " + reason);
        }

        [Test]
        public void DestinationDisagreementRefused()
        {
            OriginArrivalRefusalReason.JourneyState person = new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Person", 7, true, true,
                "town-center", "other-millbrook", "gateway-1");
            string reason = DescribeWith(MatchingVehicle(), person);
            Assert.IsTrue(reason.Contains("does not match operator journey"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("'millbrook'"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("'other-millbrook'"), "Reason was: " + reason);
        }

        [Test]
        public void GatewayDisagreementRefused()
        {
            OriginArrivalRefusalReason.JourneyState person = new OriginArrivalRefusalReason.JourneyState(
                "j-per-1", "Person", 7, true, true,
                "town-center", "millbrook", "other-gateway");
            string reason = DescribeWith(MatchingVehicle(), person);
            Assert.IsTrue(reason.Contains("gateway 'gateway-1' does not match"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("'other-gateway'"), "Reason was: " + reason);
        }

        [Test]
        public void FirstFailingCheckWins()
        {
            // Blank origin AND a missing vehicle journey: the blank-arg
            // check comes first in the driver method's order, so the
            // reported reason must be the blank origin, not the missing
            // journey. This pins the evaluation order, not just the set.
            string reason = DescribeWith(null, MatchingPerson(), originLocationId: "");
            Assert.IsTrue(reason.Contains("no origin location id was supplied"), "Reason was: " + reason);
            Assert.IsFalse(reason.Contains("no active journey"), "Reason was: " + reason);
        }

        // ---------- (2) Wiring (structural) ----------

        [Test]
        public void OriginConfirmationLogsRefusalReason()
        {
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string method = MethodBody(code, "public bool ConfirmPhysicalFreightOriginArrival(");
            Assert.IsTrue(method.Contains("OriginArrivalRefusalReason.Describe("),
                "ConfirmPhysicalFreightOriginArrival must name the refusal reason through the pure helper");
            Assert.IsTrue(method.Contains("Debug.LogWarning(refusal"),
                "ConfirmPhysicalFreightOriginArrival must log the refusal reason loudly, once, at the refusal");
            Assert.IsTrue(method.Contains("ToOriginArrivalJourneyState(vehicleJourney)"),
                "ConfirmPhysicalFreightOriginArrival must snapshot the vehicle journey into the helper");
            Assert.IsTrue(method.Contains("ToOriginArrivalJourneyState(personJourney)"),
                "ConfirmPhysicalFreightOriginArrival must snapshot the operator journey into the helper");
            int describe = method.IndexOf("OriginArrivalRefusalReason.Describe(", StringComparison.Ordinal);
            int log = method.IndexOf("Debug.LogWarning(refusal", StringComparison.Ordinal);
            int authority = method.IndexOf("OffMapJourneys.ConfirmPhysicalOriginArrival(", StringComparison.Ordinal);
            Assert.IsTrue(describe >= 0 && log > describe,
                "The reason must be computed before it is logged");
            Assert.IsTrue(log >= 0 && authority > log,
                "The reason log must run before the authority calls, so a pre-check refusal is never silent");
        }

        [Test]
        public void OriginConfirmationKeepsAuthorityPath()
        {
            // The fix names the pre-check refusal; the authority path
            // (already-confirmed flags, SyncOffMapJourneysToSaveState,
            // success log) must be intact underneath it. The success log
            // is a string literal, so it is checked in the raw file (the
            // stripper blanks literals) and ordered after the method
            // signature; the code parts are checked in the stripped body.
            string raw = ReadRepoFile(DriversPath);
            string code = StripNonCode(raw);
            string method = MethodBody(code, "public bool ConfirmPhysicalFreightOriginArrival(");
            Assert.IsTrue(method.Contains("OffMapJourneys.ConfirmPhysicalOriginArrival("),
                "ConfirmPhysicalFreightOriginArrival must still call the journey authority after the pre-check");
            Assert.IsTrue(method.Contains("SyncOffMapJourneysToSaveState()"),
                "ConfirmPhysicalFreightOriginArrival must still sync journeys to the save state on success");
            int sig = raw.IndexOf("public bool ConfirmPhysicalFreightOriginArrival(", StringComparison.Ordinal);
            int success = raw.IndexOf("Physical freight return confirmed:", sig);
            Assert.IsTrue(success > sig,
                "ConfirmPhysicalFreightOriginArrival must still log the success line");
        }

        [Test]
        public void DestinationMethodUntouchedByReasonLogging()
        {
            // Deliberate asymmetry, pinned: the destination-side method is
            // called every tick by its retry loop, so per-call logging
            // there would spam. Only the origin method — called once per
            // arrival attempt — logs its refusal reason.
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string method = MethodBody(code, "public bool ConfirmPhysicalFreightDestinationArrival(");
            Assert.IsFalse(method.Contains("OriginArrivalRefusalReason"),
                "ConfirmPhysicalFreightDestinationArrival must not log per-call reasons (tick retry loop)");
        }

        [Test]
        public void ControllerRecordsRefusedOriginJourney()
        {
            string raw = ReadRepoFile(ControllerPath);
            Assert.IsTrue(raw.Contains("[NonSerialized] private readonly HashSet<string> refusedOriginJourneyIds"),
                "The controller must keep refused origin journey ids in a runtime-only field (never serialized)");
            Assert.IsTrue(raw.Contains("public bool HasRefusedOriginArrival(string journeyId)"),
                "The controller must expose whether a journey's origin arrival was already refused");
            string code = StripNonCode(raw);
            int confirm = code.IndexOf("ConfirmPhysicalFreightOriginArrival(", StringComparison.Ordinal);
            int legFalse = code.IndexOf("legActive = false;", confirm);
            int add = code.IndexOf("refusedOriginJourneyIds.Add(physicalJourneyId)", confirm);
            int successClear = code.IndexOf("physicalOriginLocationId = string.Empty;", confirm);
            Assert.IsTrue(confirm >= 0 && legFalse > confirm,
                "The refusal branch must kill the leg when origin confirmation is refused");
            Assert.IsTrue(add > legFalse,
                "The refusal must be recorded after the leg is killed, inside the refusal branch");
            Assert.IsTrue(successClear > add,
                "The refusal record must sit in the refusal branch, before the success-path origin clear");
            Assert.IsTrue(raw.Contains("Reached the town return point for"),
                "The controller must keep its existing leg-level refusal warning");
        }

        [Test]
        public void DailyDriverDistinguishesRefusedReturn()
        {
            // The refusal keys and the "no re-drive path exists" wording
            // are string literals, so they are checked in the raw file
            // (the stripper blanks literals) and ordered after the method
            // signature; the refusal check itself is code, checked in the
            // stripped body.
            string raw = ReadRepoFile(DriversPath);
            string code = StripNonCode(raw);
            string method = MethodBody(code, "private void DrivePhysicalFreightReturns(int absoluteDayIndex)");
            Assert.IsTrue(method.Contains("HasRefusedOriginArrival(journey.JourneyId)"),
                "DrivePhysicalFreightReturns must ask the movement controller whether this journey's origin arrival was already refused");
            int methodStart = raw.IndexOf("private void DrivePhysicalFreightReturns(int absoluteDayIndex)", StringComparison.Ordinal);
            int check = raw.IndexOf("HasRefusedOriginArrival(journey.JourneyId)", methodStart);
            int refused = raw.IndexOf("physical-return-refused:", methodStart);
            int generic = raw.IndexOf("physical-return-start:", methodStart);
            Assert.IsTrue(refused > methodStart,
                "DrivePhysicalFreightReturns must log a refused return under its own day-keyed refusal key");
            Assert.IsTrue(generic > methodStart,
                "DrivePhysicalFreightReturns must keep the generic waiting-to-begin key for returns that have not run yet");
            int noRedrive = raw.IndexOf("no re-drive path exists", refused);
            Assert.IsTrue(noRedrive > refused,
                "The refused-return line must say plainly that no re-drive path exists");
            Assert.IsTrue(check >= 0 && refused > check,
                "The refused-return line must follow the refusal check");
            Assert.IsTrue(refused >= 0 && generic > refused,
                "The generic waiting-to-begin line must be the else branch, not the refused case");
        }

        // ---------- Repo helpers (C-5 / C-7 / C-8 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, SaveLoadManagerPath)))
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
                        if (File.Exists(Path.Combine(candidate, SaveLoadManagerPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (package 9): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (package 9): expected repo file missing: " + full);
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
            Assert.IsTrue(sig >= 0, "Campaign C (package 9): method not found: " + signature);
            int open = strippedCode.IndexOf('{', sig);
            Assert.IsTrue(open >= 0, "Campaign C (package 9): method has no body: " + signature);
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
            Assert.IsTrue(false, "Campaign C (package 9): unbalanced braces in method: " + signature);
            return null;
        }
    }
}
