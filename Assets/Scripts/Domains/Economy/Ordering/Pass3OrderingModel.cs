// Pass 3 (economy ordering): player ordering model, source-level only.
// This file contains NO UnityEngine references on purpose, so the
// ordering logic stays verifiable in the small Roslyn harness. The
// actual store UI (panels, buttons, Inspector fields) is Unity AI's
// work, using this model as its spec; see Docs/Pass3EconomyOrderingReview.md.
//
// Principles, from Kennedy's vision, recorded here so a later reader
// does not have to re-derive them from chat:
//   - Player ordering is NEVER automatic. Employee delegation of
//     purchasing/reordering is fine, but a player order must be a
//     deliberate player action. Automatic + Player is refused, not
//     silently downgraded to manual.
//   - Front shelf (what customers can actually buy) and back storage
//     are DIFFERENT places. Stock lands in back on intake; it reaches
//     the shelf only through a shelf/back move, which Pass 2 made a
//     task (gs.move-back-to-shelf, 2 min/lot). This model records the
//     move; it does not perform person-time, and it does not claim a
//     driver issued the task.
//   - Margin and price are the SAME action. Setting a margin percent,
//     setting a margin amount, and setting a price all end in one
//     price in cents. Pass 2 decided price/margin setting is an
//     instant decision, not a task; honour that here, do not
//     double-count it as labour. Rounding is normal rounding to the
//     nearest cent; all money in this model is integer cents.
//   - Removing a product never deletes stock. A product with shelf
//     or back units still on hand cannot be removed; sell, move, or
//     consume the stock first. This is a guard against silent stock
//     destruction, not a business rule about catalog size.
//   - No price, rate, or quantity in tests for this model is canon.
//     Test parameters are caller/author inputs to vary, never fixed
//     rules. A newly formed business still begins with zero magically
//     generated inventory/cash (Pass 1 formation rule); a catalog
//     line created here carries zero stock until a real order,
//     intake, transfer, or production puts stock in it. Existing
//     scenario businesses may begin with documented endowments with
//     identity/provenance; that endowment is NOT created by this file.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Ordering
{
    /// <summary>Who is placing (or delegating) an order.</summary>
    public enum Pass3ActorKind
    {
        Player = 0,
        EmployeeDelegate = 1,
    }

    /// <summary>
    /// One product line in a business's store catalog: its cost, its
    /// selling price, and its two separate stock pools. Cost is the
    /// goods cost per unit from the most recent order/intake that
    /// supplied this line; it is caller data, not a price list.
    /// ShelfCapacityUnits / BackCapacityUnits of 0 mean the caller
    /// has set no explicit cap for that pool; moves are then limited
    /// only by stock on hand, never by an invented cap.
    /// </summary>
    public sealed class Pass3ProductLine
    {
        public string ProductId = string.Empty;
        public string DisplayName = string.Empty;
        public int CostCents;
        public int PriceCents;
        public int ShelfUnits;
        public int BackUnits;
        public int ShelfCapacityUnits;
        public int BackCapacityUnits;

        /// <summary>Total markup amount per unit, price minus cost. May be negative; a negative margin is reported, never hidden.</summary>
        public int MarkupTotalCents()
        {
            return PriceCents - CostCents;
        }

        /// <summary>Markup percent on cost. Zero when cost is zero or less, because a percent on no cost is undefined, not zero margin.</summary>
        public double MarkupPercent()
        {
            if (CostCents <= 0) return 0.0;
            return (PriceCents - CostCents) * 100.0 / CostCents;
        }

        /// <summary>Instant decision: set the selling price directly. Refuses a negative price, moving nothing.</summary>
        public bool SetPrice(int priceCents, List<string> diag)
        {
            if (priceCents < 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: price for '" + ProductId + "' cannot be negative.");
                return false;
            }
            PriceCents = priceCents;
            return true;
        }

        /// <summary>
        /// Instant decision, same action as SetPrice: set the price from
        /// a margin percent on cost, rounded to the nearest cent.
        /// Refuses when cost is zero or less, because a percent on no
        /// cost cannot produce a price, and refuses a negative result.
        /// </summary>
        public bool SetMarginPercent(double percent, List<string> diag)
        {
            if (CostCents <= 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: margin percent for '" + ProductId + "' needs a positive cost first.");
                return false;
            }
            int price = (int)Math.Round(CostCents * (1.0 + percent / 100.0), MidpointRounding.AwayFromZero);
            return SetPrice(price, diag);
        }

        /// <summary>Instant decision, same action as SetPrice: set the price from a total markup amount on cost.</summary>
        public bool SetMarginAmount(int amountCents, List<string> diag)
        {
            return SetPrice(CostCents + amountCents, diag);
        }

        /// <summary>Move stock from back storage onto the front shelf. Refuses bad quantities, insufficient back stock, or a shelf cap breach, moving nothing.</summary>
        public bool MoveToShelf(int units, List<string> diag)
        {
            if (units <= 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: move to shelf for '" + ProductId + "' needs a positive quantity.");
                return false;
            }
            if (units > BackUnits)
            {
                if (diag != null) diag.Add("Pass3Ordering: move to shelf for '" + ProductId + "' refused: only " + BackUnits + " in back, asked " + units + ".");
                return false;
            }
            if (ShelfCapacityUnits > 0 && ShelfUnits + units > ShelfCapacityUnits)
            {
                if (diag != null) diag.Add("Pass3Ordering: move to shelf for '" + ProductId + "' refused: shelf capacity " + ShelfCapacityUnits + " would be exceeded.");
                return false;
            }
            BackUnits -= units;
            ShelfUnits += units;
            return true;
        }

        /// <summary>
        /// Review 2 Pass D1, additive only (MoveToShelf/MoveToBack
        /// untouched): consume shelf stock out of the catalog
        /// entirely, for stocking into a live stock authority (the
        /// General Store category pool). Refuses bad quantities and
        /// short shelf stock, moving nothing on refusal. This is the
        /// transfer that keeps the Pass 3 shelf and the live pool
        /// from double-counting the same loaves: units leave here
        /// exactly when they enter the live pool, never before.
        /// </summary>
        public bool ConsumeShelfUnits(int units, List<string> diag)
        {
            if (units <= 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: consuming shelf stock for '" + ProductId + "' needs a positive quantity.");
                return false;
            }
            if (units > ShelfUnits)
            {
                if (diag != null) diag.Add("Pass3Ordering: consuming shelf stock for '" + ProductId + "' refused: only " + ShelfUnits + " on shelf, asked " + units + ".");
                return false;
            }
            ShelfUnits -= units;
            return true;
        }

        /// <summary>Move stock from the front shelf back into back storage. Same refusal rules as MoveToShelf, mirrored.</summary>
        public bool MoveToBack(int units, List<string> diag)
        {
            if (units <= 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: move to back for '" + ProductId + "' needs a positive quantity.");
                return false;
            }
            if (units > ShelfUnits)
            {
                if (diag != null) diag.Add("Pass3Ordering: move to back for '" + ProductId + "' refused: only " + ShelfUnits + " on shelf, asked " + units + ".");
                return false;
            }
            if (BackCapacityUnits > 0 && BackUnits + units > BackCapacityUnits)
            {
                if (diag != null) diag.Add("Pass3Ordering: move to back for '" + ProductId + "' refused: back capacity " + BackCapacityUnits + " would be exceeded.");
                return false;
            }
            ShelfUnits -= units;
            BackUnits += units;
            return true;
        }
    }

    /// <summary>
    /// A business's store catalog: the product lines a player (or a
    /// delegated employee) can see, add, remove, price, and move stock
    /// between shelf and back for. Adding a line creates no stock.
    /// </summary>
    public sealed class Pass3StoreCatalog
    {
        private readonly Dictionary<string, Pass3ProductLine> lines =
            new Dictionary<string, Pass3ProductLine>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<Pass3ProductLine> Lines
        {
            get
            {
                var result = new List<Pass3ProductLine>(lines.Values);
                return result;
            }
        }

        public int LineCount => lines.Count;

        public Pass3ProductLine Find(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return null;
            Pass3ProductLine line;
            return lines.TryGetValue(productId.Trim(), out line) ? line : null;
        }

        /// <summary>Add a product line with zero stock. Refuses duplicates and empty ids, creating nothing.</summary>
        public bool AddProduct(Pass3ProductLine line, List<string> diag)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.ProductId))
            {
                if (diag != null) diag.Add("Pass3Ordering: product id required to add a product.");
                return false;
            }
            if (lines.ContainsKey(line.ProductId.Trim()))
            {
                if (diag != null) diag.Add("Pass3Ordering: product '" + line.ProductId + "' is already in the catalog; use the existing line.");
                return false;
            }
            line.ProductId = line.ProductId.Trim();
            line.ShelfUnits = 0;
            line.BackUnits = 0;
            lines[line.ProductId] = line;
            return true;
        }

        /// <summary>
        /// Remove a product line. Refuses while any shelf or back stock
        /// remains, so removal can never silently destroy stock, and
        /// refuses unknown ids. Removing a line is a catalog decision,
        /// not a stock disposal.
        /// </summary>
        public bool RemoveProduct(string productId, List<string> diag)
        {
            Pass3ProductLine line = Find(productId);
            if (line == null)
            {
                if (diag != null) diag.Add("Pass3Ordering: cannot remove unknown product '" + productId + "'.");
                return false;
            }
            if (line.ShelfUnits > 0 || line.BackUnits > 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: cannot remove '" + productId + "' while it holds " + line.ShelfUnits + " shelf and " + line.BackUnits + " back units.");
                return false;
            }
            lines.Remove(line.ProductId);
            return true;
        }
    }

    /// <summary>
    /// An ordering session: the business, who is ordering, and whether
    /// this order is automatic. The automatic guard lives here and is
    /// re-checked by every order draft: a player order is never
    /// automatic, and an employee automatic order needs delegation.
    /// </summary>
    public sealed class Pass3OrderingSession
    {
        public string BusinessId = string.Empty;
        public Pass3ActorKind Actor = Pass3ActorKind.Player;
        public bool IsAutomatic;
        public bool DelegationAllowed;

        /// <summary>Returns null when the session may order, or a refusal reason when it may not, changing nothing.</summary>
        public string ValidateAutomatic()
        {
            if (!IsAutomatic) return null;
            if (Actor == Pass3ActorKind.Player)
                return "Pass3Ordering: player ordering is never automatic; a player order must be placed deliberately.";
            if (!DelegationAllowed)
                return "Pass3Ordering: automatic ordering by an employee delegate needs delegation permission, which this business has not granted.";
            return null;
        }
    }
}
