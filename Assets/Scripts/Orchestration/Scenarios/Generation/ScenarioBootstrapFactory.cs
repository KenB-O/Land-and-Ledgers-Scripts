using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.Time;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass IX WS6 (A1-01): the durable record of a captured scenario
    /// selection — id + title only, so a selection written by any version
    /// of the game still reads in any other. Version-tolerant by
    /// construction: unknown fields are ignored, missing fields default,
    /// and legacy title-only JSON (no id) is accepted. (A data record in
    /// class form — this codebase serializes with JsonUtility-style DTOs,
    /// which do not support C# record types.)
    /// </summary>
    [Serializable]
    public sealed class CapturedScenarioSelection
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Version = string.Empty;

        public CapturedScenarioSelection() { }

        public CapturedScenarioSelection(string id, string title, string version = "")
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Version = version ?? string.Empty;
        }

        public string ToJson()
        {
            return "{\"id\":\"" + Escape(Id) + "\",\"title\":\"" + Escape(Title) +
                "\",\"version\":\"" + Escape(Version) + "\"}";
        }

        /// <summary>
        /// Tolerant reader: accepts the full record, legacy title-only
        /// JSON ({"title": "..."}), id-only JSON, and even a bare title
        /// string. Never throws; unparseable input yields an empty selection.
        /// </summary>
        public static CapturedScenarioSelection FromJson(string json)
        {
            var selection = new CapturedScenarioSelection();
            if (string.IsNullOrWhiteSpace(json)) return selection;
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{"))
            {
                selection.Title = trimmed.Trim('"');
                return selection;
            }
            selection.Id = ExtractStringField(trimmed, "id");
            selection.Title = ExtractStringField(trimmed, "title");
            selection.Version = ExtractStringField(trimmed, "version");
            return selection;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ExtractStringField(string json, string field)
        {
            string needle = "\"" + field + "\"";
            int at = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return string.Empty;
            int colon = json.IndexOf(':', at + needle.Length);
            if (colon < 0) return string.Empty;
            int open = json.IndexOf('"', colon + 1);
            if (open < 0) return string.Empty;
            var chars = new List<char>();
            for (int i = open + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length) { chars.Add(json[i + 1]); i++; continue; }
                if (c == '"') break;
                chars.Add(c);
            }
            return new string(chars.ToArray());
        }
    }

    /// <summary>
    /// Pass IX WS6 (A1-01): what a captured selection resolves to — the
    /// preset version it matches (if any), whether it is a preset start,
    /// and whether the preset's authored start date drives the clock.
    /// </summary>
    public sealed class ScenarioBootstrapComposition
    {
        public string Version = string.Empty;
        public bool IsPreset;
        public bool UsePresetStartDate;
        public string PresetId = string.Empty;
        public string ScenarioId = string.Empty;
        public string Title = string.Empty;
    }

    /// <summary>
    /// Pass IX WS6 (A1-01): a resolved start — everything the boot path
    /// needs to place the session clock, and nothing else. The factory
    /// constructs NO scenario assets; world generation stays with the
    /// coordinator/builders.
    /// </summary>
    public sealed class ScenarioBootstrapPlan
    {
        public CapturedScenarioSelection Selection;
        public ScenarioBootstrapComposition Composition;
        public string ScenarioId = string.Empty;
        public string Title = string.Empty;
        public string PresetId = string.Empty;
        public int StartYear;
        public int StartMonth;
        public int StartDay;
        /// <summary>Start day in the TimeManager absolute frame (anchor date 1880-03-01 = 0).</summary>
        public int StartDayIndex;
        /// <summary>Start day in the ScenarioDateUtil anchor frame (1880-01-01 = 0).</summary>
        public int AnchorFrameDayIndex;
        public bool UsePresetStartDate;
    }

    /// <summary>
    /// Pass IX WS6 (A1-01): factory-only bootstrap for scenario starts.
    /// Resolves a captured selection against the known presets, computes
    /// the start day from the canonical date authority (ScenarioDateUtil /
    /// CalendarAnchor), and applies it to the clock through
    /// TimeManager.LoadFromSaveDto — the only public absolute-day setter
    /// (it also resets the tick driver). No scenario assets are built here.
    /// </summary>
    public static class ScenarioBootstrapFactory
    {
        public static ScenarioBootstrapComposition ResolveComposition(
            CapturedScenarioSelection selection, IEnumerable<ScenarioPreset> availablePresets,
            out ScenarioPreset matchedPreset)
        {
            matchedPreset = FindPreset(selection, availablePresets);
            var composition = new ScenarioBootstrapComposition();
            if (selection == null)
            {
                composition.Version = "unknown";
                return composition;
            }
            composition.IsPreset = matchedPreset != null;
            composition.UsePresetStartDate = matchedPreset != null;
            composition.PresetId = matchedPreset != null ? matchedPreset.PresetId : string.Empty;
            composition.ScenarioId = matchedPreset != null
                ? matchedPreset.StartingWorld.ScenarioId
                : (selection.Id ?? string.Empty);
            composition.Title = matchedPreset != null ? matchedPreset.PresetName : (selection.Title ?? string.Empty);
            composition.Version = matchedPreset != null
                ? matchedPreset.PresetVersion
                : (!string.IsNullOrEmpty(selection.Version) ? selection.Version : "legacy");
            return composition;
        }

        public static bool TryCreateStart(
            CapturedScenarioSelection selection, IEnumerable<ScenarioPreset> availablePresets,
            out ScenarioBootstrapPlan plan, List<string> diag = null)
        {
            plan = null;
            diag = diag ?? new List<string>();
            if (selection == null)
            {
                diag.Add("ScenarioBootstrapFactory.TryCreateStart: no captured selection — no start can be composed.");
                return false;
            }

            ScenarioBootstrapComposition composition =
                ResolveComposition(selection, availablePresets, out ScenarioPreset preset);

            int year = CalendarAnchor.DefaultAnchorYear;
            int month = CalendarAnchor.DefaultAnchorMonth;
            int day = CalendarAnchor.DefaultAnchorDay;
            if (preset != null)
            {
                StartingWorldConfiguration world = preset.StartingWorld;
                year = world.StartYear;
                month = world.StartMonth;
                day = world.StartDay;
            }
            if (!ScenarioDateUtil.IsValidDate(year, month, day))
            {
                diag.Add($"ScenarioBootstrapFactory.TryCreateStart: start date {year}-{month}-{day} is not a real Gregorian date.");
                return false;
            }

            plan = new ScenarioBootstrapPlan
            {
                Selection = selection,
                Composition = composition,
                ScenarioId = composition.ScenarioId,
                Title = composition.Title,
                PresetId = composition.PresetId,
                StartYear = year,
                StartMonth = month,
                StartDay = day,
                StartDayIndex = CalendarAnchor.Default.ToAbsoluteDay(year, month, day),
                AnchorFrameDayIndex = ScenarioDateUtil.DayIndexFor(year, month, day),
                UsePresetStartDate = composition.UsePresetStartDate,
            };
            return true;
        }

        public static bool TryCreateStart(
            ScenarioPreset preset, out ScenarioBootstrapPlan plan, List<string> diag = null)
        {
            plan = null;
            if (preset == null) return false;
            var selection = new CapturedScenarioSelection(preset.PresetId, preset.PresetName, preset.PresetVersion);
            return TryCreateStart(selection, new[] { preset }, out plan, diag);
        }

        /// <summary>
        /// Applies the plan's start day to the session clock via the public
        /// LoadFromSaveDto seam. Returns true when the clock now reads the
        /// plan's start day.
        /// </summary>
        public static bool ApplyToTimeManager(ScenarioBootstrapPlan plan, TimeManager timeManager)
        {
            if (plan == null || timeManager == null) return false;
            timeManager.LoadFromSaveDto(new TimeSaveDto { absoluteDayIndex = plan.StartDayIndex });
            return timeManager.CurrentAbsoluteDayIndex == plan.StartDayIndex;
        }

        private static ScenarioPreset FindPreset(
            CapturedScenarioSelection selection, IEnumerable<ScenarioPreset> availablePresets)
        {
            if (selection == null || availablePresets == null) return null;
            ScenarioPreset byTitle = null;
            foreach (ScenarioPreset preset in availablePresets)
            {
                if (preset == null) continue;
                if (!string.IsNullOrEmpty(selection.Id) &&
                    (string.Equals(selection.Id, preset.PresetId, StringComparison.Ordinal) ||
                     string.Equals(selection.Id, preset.StartingWorld.ScenarioId, StringComparison.Ordinal)))
                    return preset;
                if (byTitle == null && !string.IsNullOrEmpty(selection.Title) &&
                    string.Equals(selection.Title, preset.PresetName, StringComparison.OrdinalIgnoreCase))
                    byTitle = preset;
            }
            return byTitle;
        }
    }
}
