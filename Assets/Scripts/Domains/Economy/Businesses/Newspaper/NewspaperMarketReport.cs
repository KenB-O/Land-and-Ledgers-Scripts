using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy.Market.PriceHistory;

namespace LandLedgers.Economy.Businesses.Newspaper
{
    /// <summary>
    /// D4J: kind of edition component. The market-report column is the first
    /// real component; the enum leaves room for future article components
    /// through the same dated-edition assembly seam.
    /// </summary>
    public enum NewspaperEditionComponentKind
    {
        Unspecified = 0,
        MarketReportColumn = 1,
    }

    /// <summary>
    /// D4J: one content component attached to a dated edition — part of the
    /// same lot, with the same dated-edition semantics as everything else in
    /// the issue. A component compiled as-of a given day may only attach to
    /// the edition published that day (enforced by
    /// <see cref="NewspaperRuntime.AttachEditionComponent"/>), so a column
    /// can never carry a different day's prices than its edition.
    /// </summary>
    [Serializable]
    public sealed class NewspaperEditionComponent
    {
        public string ComponentId = string.Empty;
        public NewspaperEditionComponentKind Kind = NewspaperEditionComponentKind.Unspecified;
        public string Title = string.Empty;
        public string ContentText = string.Empty;
        public int CompiledDayIndex = -1;
        public string SourceNotes = string.Empty;

        public NewspaperEditionComponent() { }
    }

    /// <summary>
    /// D4J: what the market-report column covers. The market area is explicit
    /// because NX-3A models no town-level circulation (subscriptions are
    /// named persons/businesses with no settlement; editions have no coverage
    /// area) and the runtime carries no settlement identity — the caller
    /// supplies the paper's town plus any neighboring towns its circulation
    /// covers, home settlement first.
    /// </summary>
    [Serializable]
    public sealed class MarketReportSpec
    {
        /// <summary>
        /// Default lookback, days: one week. A weekly paper's edition goes
        /// stale after 7 days (NewspaperRuntime.AgeEditions), so the change
        /// window matches the edition's news life.
        /// </summary>
        public const int DefaultLookbackDays = 7;

        /// <summary>Settlement ids to cover; the paper's town first.</summary>
        public List<string> SettlementIds = new List<string>();

        /// <summary>Commodity-kind keys to report (see TradeCommodityKindCatalog).</summary>
        public List<string> CommodityKinds = new List<string>();

        /// <summary>Change is latest vs the price in force LookbackDays earlier. 0 = latest only.</summary>
        public int LookbackDays = DefaultLookbackDays;

        public MarketReportSpec() { }
    }

    /// <summary>
    /// D4J: one town-by-commodity line of the compiled market report. Every
    /// figure traces to a recorded <see cref="PriceHistoryObservation"/> via
    /// its source transaction — the column never invents a number.
    /// </summary>
    [Serializable]
    public sealed class MarketReportLine
    {
        public string SettlementId = string.Empty;
        public string SettlementName = string.Empty;
        public string CommodityKind = string.Empty;

        public bool HasData;
        public int LatestPriceCents;
        public int LatestDayIndex;
        public PriceHistoryQuoteSide QuoteSide = PriceHistoryQuoteSide.Unspecified;
        public string UnitLabel = string.Empty;
        public string SourceTransactionReference = string.Empty;

        public int? EarlierPriceCents;
        public int? EarlierDayIndex;
        public int? ChangeCents;
        public PriceHistoryTrendDirection Direction = PriceHistoryTrendDirection.Unknown;

        public MarketReportLine() { }
    }

    /// <summary>
    /// D4J: the compiled market-report column for one dated edition —
    /// market rows for each covered town, plus honest gap notes wherever no
    /// observations exist (never invented numbers).
    /// </summary>
    [Serializable]
    public sealed class NewspaperMarketReportColumn
    {
        public string ColumnId = string.Empty;

        /// <summary>The edition publication day the column is compiled as-of.
        /// Only observations with DayIndex &lt;= this day are used.</summary>
        public int EditionDayIndex;

        public List<string> SettlementIds = new List<string>();
        public List<string> CommodityKinds = new List<string>();
        public int LookbackDays = MarketReportSpec.DefaultLookbackDays;

        /// <summary>Rows with recorded history.</summary>
        public List<MarketReportLine> Lines = new List<MarketReportLine>();

        /// <summary>
        /// Honest no-data notes, one per (settlement, commodity) pair with no
        /// observations through the edition day.
        /// </summary>
        public List<string> Gaps = new List<string>();

        public int DataRowCount => Lines != null ? Lines.Count : 0;
        public int GapCount => Gaps != null ? Gaps.Count : 0;

        public NewspaperMarketReportColumn() { }
    }

    /// <summary>
    /// D4J: compiles the newspaper market-report column FROM the D4I price
    /// histories — never invented numbers.
    ///
    /// Information lag: compilation is always as-of the edition's publication
    /// day (<see cref="NewspaperMarketReportColumn.EditionDayIndex"/>). The
    /// read API only returns observations with DayIndex &lt;= that day, so no
    /// future price can leak into a past edition.
    ///
    /// Upstream provenance: every figure cites its source observation (day,
    /// quote side, unit, source transaction reference). A town/commodity with
    /// no observations gets a gap note — the column says so, it never fills
    /// in a plausible number.
    /// </summary>
    public static class MarketReportCompiler
    {
        /// <summary>
        /// Compiles the column for a dated edition. Returns null + diagnostics
        /// when the store, spec, or edition day is unusable.
        /// </summary>
        public static NewspaperMarketReportColumn Compile(
            PriceHistoryStore store, MarketReportSpec spec, int editionDayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (store == null)
            {
                diagnostics.Add("MarketReportCompiler: no price-history store — nothing to compile.");
                return null;
            }
            if (spec == null)
            {
                diagnostics.Add("MarketReportCompiler: no market-report spec — nothing to compile.");
                return null;
            }
            if (editionDayIndex < 0)
            {
                diagnostics.Add("MarketReportCompiler: edition day must not be negative.");
                return null;
            }
            List<string> settlements = DistinctNonBlank(spec.SettlementIds);
            if (settlements.Count == 0)
            {
                diagnostics.Add("MarketReportCompiler: the spec names no settlements — the market area is required.");
                return null;
            }
            List<string> commodities = DistinctNonBlank(spec.CommodityKinds);
            if (commodities.Count == 0)
            {
                diagnostics.Add("MarketReportCompiler: the spec names no commodities — nothing to report.");
                return null;
            }
            int lookbackDays = spec.LookbackDays;
            if (lookbackDays < 0)
            {
                diagnostics.Add("MarketReportCompiler: lookback days must not be negative.");
                return null;
            }

            var column = new NewspaperMarketReportColumn
            {
                ColumnId = "mrc-" + editionDayIndex,
                EditionDayIndex = editionDayIndex,
                SettlementIds = new List<string>(settlements),
                CommodityKinds = new List<string>(commodities),
                LookbackDays = lookbackDays,
            };

            foreach (string settlementId in settlements)
            {
                foreach (string commodityKind in commodities)
                {
                    PriceHistoryMarketRow row =
                        store.BuildMarketRow(settlementId, commodityKind, editionDayIndex, lookbackDays);
                    if (row == null || !row.HasData)
                    {
                        column.Gaps.Add(
                            $"No price observations recorded for '{commodityKind}' in settlement '{settlementId}' " +
                            $"through day {editionDayIndex}.");
                        continue;
                    }
                    column.Lines.Add(new MarketReportLine
                    {
                        SettlementId = row.SettlementId,
                        SettlementName = row.SettlementName,
                        CommodityKind = row.CommodityKind,
                        HasData = true,
                        LatestPriceCents = row.LatestPriceCents,
                        LatestDayIndex = row.LatestDayIndex,
                        QuoteSide = row.QuoteSide,
                        UnitLabel = row.UnitLabel,
                        SourceTransactionReference = row.SourceTransactionReference,
                        EarlierPriceCents = row.EarlierPriceCents,
                        EarlierDayIndex = row.EarlierDayIndex,
                        ChangeCents = row.ChangeCents,
                        Direction = row.Direction,
                    });
                }
            }

            diagnostics.Add(
                $"MarketReportCompiler: compiled column for day {editionDayIndex} — " +
                $"{column.DataRowCount} observed price row(s), {column.GapCount} no-data gap(s).");
            return column;
        }

        /// <summary>
        /// Compiles the column and wraps it as an edition component, ready to
        /// attach via <see cref="NewspaperRuntime.AttachEditionComponent"/>.
        /// The component's compilation day equals the edition day it is
        /// compiled for, which the attach step enforces against the edition.
        /// </summary>
        public static NewspaperEditionComponent CompileAsComponent(
            PriceHistoryStore store, MarketReportSpec spec, int editionDayIndex,
            string title, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            NewspaperMarketReportColumn column = Compile(store, spec, editionDayIndex, diagnostics);
            if (column == null) return null;
            return new NewspaperEditionComponent
            {
                ComponentId = column.ColumnId + "-" + Guid.NewGuid().ToString("N"),
                Kind = NewspaperEditionComponentKind.MarketReportColumn,
                Title = string.IsNullOrWhiteSpace(title) ? "Market Report" : title.Trim(),
                ContentText = FormatColumnText(column),
                CompiledDayIndex = editionDayIndex,
                SourceNotes =
                    $"Compiled from {column.DataRowCount} observed price row(s) and " +
                    $"{column.GapCount} no-data gap(s); every figure cites its source transaction.",
            };
        }

        /// <summary>
        /// Renders the compiled column as the dated text that goes into the
        /// edition: commodity, latest price, change vs the lookback window,
        /// direction — every figure followed by its source citation. Missing
        /// data is reported as missing, never filled in.
        /// </summary>
        public static string FormatColumnText(NewspaperMarketReportColumn column)
        {
            if (column == null) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("MARKET REPORT - prices observed through day " + column.EditionDayIndex);
            if (column.LookbackDays > 0)
                sb.AppendLine("Changes shown against the price in force " + column.LookbackDays +
                    " day(s) earlier (day " + (column.EditionDayIndex - column.LookbackDays) + ").");
            sb.AppendLine();

            string lastSettlement = null;
            foreach (MarketReportLine line in column.Lines)
            {
                if (!string.Equals(line.SettlementId, lastSettlement, StringComparison.Ordinal))
                {
                    lastSettlement = line.SettlementId;
                    sb.AppendLine(DisplaySettlementName(line) + ":");
                }
                sb.AppendLine("  " + FormatLine(line, column));
            }

            if (column.Gaps != null && column.Gaps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("No quotations recorded:");
                foreach (string gap in column.Gaps)
                    sb.AppendLine("  - " + gap);
            }
            return sb.ToString();
        }

        private static string FormatLine(MarketReportLine line, NewspaperMarketReportColumn column)
        {
            var sb = new StringBuilder();
            sb.Append(line.CommodityKind);
            sb.Append(": ");
            sb.Append(line.LatestPriceCents);
            sb.Append("c");
            if (!string.IsNullOrWhiteSpace(line.UnitLabel))
                sb.Append(" per ").Append(line.UnitLabel.Trim());
            sb.Append(", observed day ").Append(line.LatestDayIndex);
            sb.Append(" (").Append(QuoteSideWord(line.QuoteSide)).Append(" ");
            sb.Append(line.SourceTransactionReference).Append(")");
            if (line.ChangeCents.HasValue && line.EarlierDayIndex.HasValue)
            {
                sb.Append(" - ");
                switch (line.Direction)
                {
                    case PriceHistoryTrendDirection.Up:
                        sb.Append("up "); break;
                    case PriceHistoryTrendDirection.Down:
                        sb.Append("down "); break;
                    default:
                        sb.Append("steady "); break;
                }
                sb.Append(Math.Abs(line.ChangeCents.Value)).Append("c since day ").Append(line.EarlierDayIndex.Value);
                sb.Append(".");
            }
            else
            {
                sb.Append("; no earlier quotation on record through day ");
                sb.Append(column.EditionDayIndex - column.LookbackDays).Append(".");
            }
            return sb.ToString();
        }

        private static string DisplaySettlementName(MarketReportLine line)
        {
            string name = string.IsNullOrWhiteSpace(line.SettlementName) ? null : line.SettlementName.Trim();
            if (name == null) return "settlement \"" + line.SettlementId + "\"";
            return name + " (settlement \"" + line.SettlementId + "\")";
        }

        private static string QuoteSideWord(PriceHistoryQuoteSide side)
        {
            switch (side)
            {
                case PriceHistoryQuoteSide.ObservedSellPrice: return "sold at";
                case PriceHistoryQuoteSide.ObservedBuyPrice: return "bought at";
                default: return "recorded in";
            }
        }

        private static List<string> DistinctNonBlank(List<string> values)
        {
            var result = new List<string>();
            if (values == null) return result;
            foreach (string value in values)
            {
                string trimmed = value != null ? value.Trim() : string.Empty;
                if (trimmed.Length == 0) continue;
                if (result.Contains(trimmed)) continue;
                result.Add(trimmed);
            }
            return result;
        }
    }
}
