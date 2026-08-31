using System;
using System.Collections.Generic;

namespace LandLedgers.UI
{
    public static class FirstSessionGuidanceText
    {
        public const string MoneyModelSummary = "Owner Cash buys land, shells, businesses, and fit-outs. Store Cash / Business Cash keeps each business alive. Manual draws can move business cash to Owner Cash, while reserve warnings show the operating risk. Loans can bridge expansion but payments come from Owner Cash.";

        public static string BuildConstructionBlockerNextStep(string missingSummary)
        {
            if (string.IsNullOrWhiteSpace(missingSummary))
            {
                return "Next: choose a smaller plan, wait for local supply, or use financing if cash is the issue.";
            }

            string normalized = missingSummary.ToLowerInvariant();
            List<string> steps = new();
            if (Contains(normalized, "cash"))
            {
                steps.Add("build Owner Cash through store distributions, manually draw business cash with reserve risk in mind, or use a bank loan");
            }

            if (Contains(normalized, "lumber") || Contains(normalized, "timber"))
            {
                steps.Add("wait for sawmill/lumber yard supply, acquire or start a lumber supplier, or choose a smaller shell");
            }

            if (Contains(normalized, "nail")
                || Contains(normalized, "hardware")
                || Contains(normalized, "blacksmith"))
            {
                steps.Add("use an available freight hardware fallback, wait for blacksmith hardware, or choose a smaller project");
            }

            if (Contains(normalized, "labor") || Contains(normalized, "worker"))
            {
                steps.Add("wait for labor to recover/arrive or pick a smaller build");
            }

            if (steps.Count == 0)
            {
                return "Next: check the selected plan, choose a smaller option, or wait for the town economy to recover.";
            }

            return "Next: " + string.Join("; ", steps) + ".";
        }

        private static bool Contains(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                && !string.IsNullOrEmpty(value)
                && text.IndexOf(value, StringComparison.Ordinal) >= 0;
        }
    }
}
