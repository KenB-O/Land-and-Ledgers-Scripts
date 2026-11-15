// Campaign C (DEF-028) — guarded save/load drop paths: trips /
// CustomerQueue entries / sawmill sections drop if their owner is null
// at restore.
//
// DEF-028, verified from source at starting HEAD b991fffe:
// (1) Shopping trips dropped if the driver is null at hub-load time
// (SimulationSystemsHub.LoadFromSaveDto) — LIVE on a fresh-scene
// Continue: no BuildHouseholdShoppingLoop call has run yet when the hub
// loads, so hub.ShoppingTripDriver is null and any saved in-flight trips
// are dropped with a diagnostic. A second, stacked ordering defect:
// BuildHouseholdShoppingLoop attached the driver BEFORE creating it, so
// the first build handed the hub a null driver and the created driver was
// never re-attached until the next build (Rebind at the earliest) — after
// the hub load, and after Rebind's ApplyRestoredShoppingTrips had already
// run. This package defers instead of dropping: the hub holds the trip
// list pending, the attach drains it into the driver, and the attach
// itself moves after driver creation, so Rebind rehydrates the deferred
// trips exactly like directly loaded ones.
// (2) CustomerQueue entries dropped if the queue is null at Pass 9
// restore (Series2Pass9SaveState.Restore) — the bind precedes the restore
// in the normal flow (EnsurePass9SaveOwnerForRestore) and the queue is a
// lazy property that never returns null while the manager exists, so this
// fires only when the store manager is absent from the scene; the same
// defer-and-drain shape is applied so a later bind still recovers the
// entries, and so a future bind-order change cannot silently drop them.
// (3) Sawmill sections dropped if the bootstrapper is missing
// (SaveLoadManager :573) — NO code change here: the bootstrapper is the
// only caller of the sawmill restore and no later hook exists, so there
// is nothing a deferral could drain into; the warning is the correct
// behaviour, and it is pinned by a test below. The sawmill sub-owner
// skips inside LoadSawmillSectionsFromSaveDto likewise fire only when no
// Sawmill business instance exists (the dev scenario forms none, so
// there is no sawmill data to drop); the sub-owners are constructed by
// EnsurePass9SaveOwnerForRestore before the deferred restore runs.
//
// All tests here are structural: the hub, the Pass 9 state, and
// SimulationDrivers are harness-excluded (MonoBehaviour / Unity types /
// closure too wide), so the wiring is asserted by reading the production
// files — the CampaignCCancelTaskDueEventTests /
// CampaignCCoupledTransportDriversCacheTests precedent. A missing repo
// fails loudly, never silently passes. Unity-run proof is still owed
// (a Continue with in-flight trips must restore them; a Continue with a
// waiting queue must restore it) — no Unity verification is claimed by
// this package.
using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Persistence
{
    [TestFixture]
    public sealed class CampaignCDeferredSaveRestoreTests
    {
        private const string HubPath = "Assets/Scripts/Orchestration/Systems/SimulationSystemsHub.cs";
        private const string DriversPath = "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string Pass9Path = "Assets/Scripts/Infrastructure/Persistence/Series2Pass9Save.cs";
        private const string SaveLoadPath = "Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs";

        // ---------- hub trips: defer, do not drop ----------

        [Test]
        public void HubTrips_DeferredNotDropped_WhenDriverNull()
        {
            string hub = ReadRepoFile(HubPath);
            string body = StripNonCode(MethodBody(hub, "public List<string> LoadFromSaveDto(SystemsSaveDto dto, int absoluteDayIndex)"));
            Assert.IsTrue(body.Contains("pendingShoppingTrips = new List<PlannedShoppingTripSaveDto>(dto.shoppingTrips);"),
                "Campaign C (DEF-028): the driver-null branch of the hub load must stash the trip list in pendingShoppingTrips instead of dropping it.");
            Assert.IsFalse(hub.Contains("trips dropped"),
                "Campaign C (DEF-028): the old 'trips dropped' diagnostic wording must be gone from the hub file.");
            Assert.IsTrue(hub.Contains("trips held pending the driver attach (DEF-028), not dropped."),
                "Campaign C (DEF-028): the deferred-trip diagnostic must say the trips are held pending the driver attach, not dropped.");
        }

        [Test]
        public void HubTrips_SetterDrainsPending_OnAttach()
        {
            string hub = ReadRepoFile(HubPath);
            Assert.IsFalse(hub.Contains("public ShoppingTripDriver ShoppingTripDriver { get; set; }"),
                "Campaign C (DEF-028): the hub driver must no longer be a plain auto-property — the setter must drain pending trips.");
            string property = StripNonCode(MethodBody(hub, "public ShoppingTripDriver ShoppingTripDriver"));
            Assert.IsTrue(property.Contains("pendingShoppingTrips = null;"),
                "Campaign C (DEF-028): attaching a driver must clear the pending trips (exactly once per load).");
            Assert.IsTrue(property.Contains("shoppingTripDriver.LoadFromSaveDto(deferred);"),
                "Campaign C (DEF-028): attaching a driver must load the deferred trips into it.");
            Assert.IsTrue(property.Contains("pendingShoppingTrips != null && pendingShoppingTrips.Count > 0"),
                "Campaign C (DEF-028): the drain must run only while trips are actually pending.");
        }

        [Test]
        public void HubTrips_PendingCountExposed()
        {
            string hub = ReadRepoFile(HubPath);
            Assert.IsTrue(hub.Contains("public int PendingShoppingTripCount => pendingShoppingTrips != null ? pendingShoppingTrips.Count : 0;"),
                "Campaign C (DEF-028): the hub must expose the pending-trip count so a run can observe the deferral draining.");
            Assert.IsTrue(hub.Contains("private List<PlannedShoppingTripSaveDto> pendingShoppingTrips;"),
                "Campaign C (DEF-028): the hub must hold the pending trips in a private field.");
        }

        // ---------- driver attach: after creation, not before ----------

        [Test]
        public void DriverAttach_AfterCreation_InBuildHouseholdShoppingLoop()
        {
            string drivers = ReadRepoFile(DriversPath);
            string body = StripNonCode(MethodBody(drivers, "private void BuildHouseholdShoppingLoop()"));
            int createAt = body.IndexOf("shoppingTripDriver = new ShoppingTripDriver(", StringComparison.Ordinal);
            int attachAt = body.IndexOf("hub.ShoppingTripDriver = shoppingTripDriver;", StringComparison.Ordinal);
            Assert.IsTrue(createAt >= 0, "Campaign C (DEF-028): the driver creation must still exist in BuildHouseholdShoppingLoop.");
            Assert.IsTrue(attachAt >= 0, "Campaign C (DEF-028): the hub attach must still exist in BuildHouseholdShoppingLoop.");
            Assert.IsTrue(attachAt > createAt,
                "Campaign C (DEF-028): the hub attach must run AFTER the driver is created — attaching first hands the hub a null driver on the first build.");
            Assert.AreEqual(1, CountOccurrences(body, "hub.ShoppingTripDriver ="),
                "Campaign C (DEF-028): exactly one hub driver attach must exist in BuildHouseholdShoppingLoop.");
        }

        // ---------- Pass 9 queue: defer, do not drop ----------

        [Test]
        public void Pass9Queue_DeferredNotDropped_WhenQueueNull()
        {
            string pass9 = ReadRepoFile(Pass9Path);
            string body = StripNonCode(MethodBody(pass9, "public void Restore(Series2Pass9SaveDto dto, FreightShipmentHooks freightHooks, List<string> diag)"));
            Assert.IsTrue(body.Contains("pendingCustomerQueue = dto.CustomerQueue;"),
                "Campaign C (DEF-028): the queue-null branch of the Pass 9 restore must stash the queue DTO in pendingCustomerQueue instead of dropping it.");
            Assert.IsFalse(pass9.Contains("queue NOT restored"),
                "Campaign C (DEF-028): the old 'queue NOT restored' diagnostic wording must be gone from the Pass 9 file.");
            Assert.IsTrue(pass9.Contains("queue held pending the queue bind (DEF-028), not dropped."),
                "Campaign C (DEF-028): the deferred-queue diagnostic must say the queue is held pending the queue bind, not dropped.");
        }

        [Test]
        public void Pass9Queue_SetterDrainsPending_OnBind()
        {
            string pass9 = ReadRepoFile(Pass9Path);
            Assert.IsFalse(pass9.Contains("public LandLedgers.Economy.GeneralStore.CustomerQueue CustomerQueue;"),
                "Campaign C (DEF-028): the Pass 9 queue must no longer be a plain public field — the setter must drain a pending queue.");
            string property = StripNonCode(MethodBody(pass9, "public LandLedgers.Economy.GeneralStore.CustomerQueue CustomerQueue"));
            Assert.IsTrue(property.Contains("pendingCustomerQueue = null;"),
                "Campaign C (DEF-028): binding a queue must clear the pending queue DTO (exactly once per load).");
            Assert.IsTrue(property.Contains("customerQueue.LoadFromSaveDto(deferred);"),
                "Campaign C (DEF-028): binding a queue must load the deferred queue entries into it.");
            Assert.IsTrue(pass9.Contains("public bool HasPendingCustomerQueue => pendingCustomerQueue != null;"),
                "Campaign C (DEF-028): the Pass 9 state must expose whether a queue section is pending.");
        }

        // ---------- sawmill: no silent change; the guard stays loud ----------

        [Test]
        public void SawmillRestore_BootstrapperMissing_StillWarnsLoudly()
        {
            string saveLoad = ReadRepoFile(SaveLoadPath);
            Assert.IsTrue(saveLoad.Contains("Sawmill restore after Pass 9 was not applied: no FirstLedgerSliceBootstrapper found"),
                "Campaign C (DEF-028): the missing-bootstrapper sawmill warning must still exist — site (3) has no later hook to defer to, so the loud guard is the behaviour.");
            Assert.IsTrue(saveLoad.Contains("bootstrapper.ApplySawmillRestoreAfterPass9(save.systems);"),
                "Campaign C (DEF-028): the deferred sawmill restore must still run through the bootstrapper when one exists.");
        }

        // ---------- repo-file helpers (CampaignCCancelTaskDueEventTests precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, DriversPath)))
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
                        if (File.Exists(Path.Combine(candidate, DriversPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-028): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-028): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign C (DEF-028): method not found: " + signature);
            int next = source.Length;
            foreach (string marker in new[] { "\n        private ", "\n        public ", "\n        protected ", "\n        internal " })
            {
                int at = source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
                if (at >= 0 && at < next) next = at;
            }
            return source.Substring(start, next - start);
        }

        private static int CountOccurrences(string source, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = source.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so assertions cannot be fooled by a
        /// mention inside a comment or a log string.
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
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
                            i++;
                        }
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
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
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
                        }
                        else if (source[i] == '\'')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
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
    }
}
