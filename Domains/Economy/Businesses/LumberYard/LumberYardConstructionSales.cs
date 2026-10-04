using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// W4B: one material line inside a yard sale — a Canon cost bucket
    /// (ConstructionResourceKind.Lumber, or ConstructionResourceKind.Nails
    /// which the quote system already labels "Nails / Simple Hardware"),
    /// the real lots it came from, and their provenance. Labor is never the
    /// yard's bucket — the yard sells materials, not crew.
    /// </summary>
    [Serializable]
    public sealed class LumberYardMaterialSaleLine
    {
        public ConstructionResourceKind ResourceKind;
        public int UnitsSold;
        public int UnitPriceCents;
        public List<LumberYardLumberDispenseLine> LumberLines = new List<LumberYardLumberDispenseLine>();
        public List<LumberYardHardwareDispenseLine> HardwareLines = new List<LumberYardHardwareDispenseLine>();

        public LumberYardMaterialSaleLine() { }

        public int LineTotalCents => Math.Max(0, UnitsSold) * Math.Max(0, UnitPriceCents);

        public List<string> ProvenanceLines()
        {
            var lines = new List<string>();
            foreach (var line in LumberLines)
            {
                if (line == null) continue;
                lines.Add($"{line.UnitsTaken} lumber ({line.Species}, {line.YardGradeId}) from yard lot {line.LotId}: {line.ProvenanceChain}");
            }
            foreach (var line in HardwareLines)
            {
                if (line == null) continue;
                lines.Add($"{line.UnitsTaken} {line.HardwareKind} from yard lot {line.LotId}: {line.ProvenanceChain}");
            }
            return lines;
        }
    }

    /// <summary>
    /// W4B: the audit record of one yard sale — construction project or
    /// town retail (wheelwright, household). Real lots, real provenance,
    /// agreed prices. Money itself moves only through ledger authorities,
    /// never here: this records WHAT was sold and for WHAT agreed price.
    /// </summary>
    [Serializable]
    public sealed class LumberYardSaleRecord
    {
        public EntityId SaleId = EntityId.Invalid; // EntityKind.Contract — a sale is an agreement
        public string BuyerLabel = string.Empty;   // project id for construction, buyer name for retail
        public bool IsConstructionSale;
        public int DayIndex;
        public List<LumberYardMaterialSaleLine> Lines = new List<LumberYardMaterialSaleLine>();

        public LumberYardSaleRecord() { }

        public int TotalCents
        {
            get
            {
                int total = 0;
                foreach (var line in Lines)
                {
                    if (line == null) continue;
                    total += line.LineTotalCents;
                }
                return total;
            }
        }

        public int UnitsSoldFor(ConstructionResourceKind kind)
        {
            int total = 0;
            foreach (var line in Lines)
            {
                if (line != null && line.ResourceKind == kind) total += Math.Max(0, line.UnitsSold);
            }
            return total;
        }

        public List<string> AllProvenanceLines()
        {
            var lines = new List<string>();
            foreach (var line in Lines)
            {
                if (line == null) continue;
                lines.AddRange(line.ProvenanceLines());
            }
            return lines;
        }
    }
}
