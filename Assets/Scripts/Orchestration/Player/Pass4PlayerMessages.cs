// Pass 4 (player depth): player-facing missed-work messages, as DATA.
// Plain C# (System only). This file does NOT build UI and does NOT
// claim NPC messages exist in-game: it turns the single source of
// truth — Pass 2's Pass2MissedWorkReport — into plain message records
// that Codex/Unity UI can display, and into console lines that work
// today. If a message is not in a Pass2MissedWorkReport, it is not
// invented here: NPCs report what the report says they missed, in
// plain language, with the task, the owner, and the reason attached,
// never a made-up excuse.
//
// Kennedy's ask, in his words in substance: rely on console a lot,
// tracking a lot, plus on-screen UI / messages to the player from
// NPCs saying they cannot get all work done, what they missed, did
// not finish, was not able to get done. The on-screen part is
// Codex/Unity work reading these records; the console part works
// from RenderConsoleLines() today.
using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy.Labor;

namespace LandLedgers.Orchestration.Player
{
    public enum Pass4MessageKind
    {
        MissedWork = 0,
        BalkedDemand = 1,
        Offloaded = 2,
        AutoCareFailed = 3,
    }

    /// <summary>One message, ready for console or for a Codex UI panel. No UI dependency.</summary>
    public sealed class Pass4PlayerMessage
    {
        public Pass4MessageKind Kind;
        public string FromDescription = string.Empty;
        public string TaskDefinitionId = string.Empty;
        public string Text = string.Empty;

        public Pass4PlayerMessage() { }
    }

    public static class Pass4PlayerMessages
    {
        /// <summary>
        /// One message per missed or balked item in the report, plus an
        /// idle line when idle minutes were recorded. Idle is reported
        /// as standing by, never as missed work (Pass 2 rule).
        /// </summary>
        public static List<Pass4PlayerMessage> BuildFromReport(Pass2MissedWorkReport report, string ownerDescription)
        {
            var messages = new List<Pass4PlayerMessage>();
            if (report == null) return messages;
            string owner = string.IsNullOrEmpty(ownerDescription) ? "worker" : ownerDescription;
            foreach (var item in report.Items)
            {
                messages.Add(new Pass4PlayerMessage
                {
                    Kind = item.WasDemandBalked ? Pass4MessageKind.BalkedDemand : Pass4MessageKind.MissedWork,
                    FromDescription = owner,
                    TaskDefinitionId = item.TaskDefinitionId ?? string.Empty,
                    Text = owner + " could not get this done: " + (item.TaskDefinitionId ?? string.Empty) + " — " + (item.Reason ?? "Not completed."),
                });
            }
            if (report.IdleMinutes > 0)
            {
                messages.Add(new Pass4PlayerMessage
                {
                    Kind = Pass4MessageKind.MissedWork,
                    FromDescription = owner,
                    TaskDefinitionId = string.Empty,
                    Text = owner + " stood by for " + report.IdleMinutes + " minutes with no demand to meet. That is standing by, not missed work.",
                });
            }
            return messages;
        }

        /// <summary>One message when a duty task is offloaded to an employee: the player is told who now owns it, and it is no longer his task.</summary>
        public static Pass4PlayerMessage BuildOffloadMessage(string taskDefinitionId, string employeeId)
        {
            return new Pass4PlayerMessage
            {
                Kind = Pass4MessageKind.Offloaded,
                FromDescription = employeeId ?? string.Empty,
                TaskDefinitionId = taskDefinitionId ?? string.Empty,
                Text = "Task '" + (taskDefinitionId ?? string.Empty) + "' was offloaded to " + (employeeId ?? string.Empty) + ". It is no longer the player's task and will not be counted as player work.",
            };
        }

        /// <summary>One message when auto-eat or auto-sleep fails, with the failure reason and the player's known location.</summary>
        public static Pass4PlayerMessage BuildAutoCareFailedMessage(string system, string failureReason, string currentLocationId)
        {
            return new Pass4PlayerMessage
            {
                Kind = Pass4MessageKind.AutoCareFailed,
                FromDescription = system ?? string.Empty,
                TaskDefinitionId = string.Empty,
                Text = (system ?? "Auto-care") + " failed: " + (failureReason ?? "Unknown reason.") + " Player is at " + (currentLocationId ?? string.Empty) + ".",
            };
        }

        /// <summary>Console-first rendering of a message list. No UI dependency.</summary>
        public static string RenderConsoleLines(List<Pass4PlayerMessage> messages, string header)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrEmpty(header) ? "Messages for the player" : header);
            if (messages == null || messages.Count == 0)
            {
                sb.AppendLine("  No messages: nothing missed, nothing balked, nothing offloaded.");
                return sb.ToString();
            }
            foreach (var m in messages)
            {
                sb.AppendLine("  - [" + m.Kind + "] " + m.Text);
            }
            return sb.ToString();
        }
    }
}
