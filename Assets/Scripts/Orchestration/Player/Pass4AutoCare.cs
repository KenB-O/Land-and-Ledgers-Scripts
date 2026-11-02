// Pass 4 (player depth): Project Zomboid-style auto-eat and auto-sleep,
// modelled as explicit step machines, source-level only. Plain C#
// (System only), verifiable in the small Roslyn harness.
//
// Kennedy flagged this as a complicated system, so it is modelled
// carefully, leg by leg, and it NEVER fakes a leg:
//   - No route means no travel and no arrival, never a teleport.
//   - No seller, no food, or no money means failure at that leg, with
//     the player left in a known state (where the failure happened),
//     never conjured food, never a conjured bed, never debt invented.
//   - Payment in production goes through HouseholdLedger, and credit
//     only through FinancialObligationAuthority. Here, CashCents is a
//     caller-supplied gate for the model and for tests; a Codex driver
//     must replace it with the real ledger check before this drives a
//     live game. Trade credit is allowed only when the seller/lodging
//     explicitly offers it and the price is within its limit.
//   - Travel minutes come from a route provider (production: Journey
//     Model.FindRoute via PlayerDirector/HouseholdShoppingLoop). This
//     model takes the route result as input; it does not invent miles.
//   - Durations follow EXISTING code, not Kennedy's example guesses:
//     eat 30 minutes (Pass 2 life task lf.eat-meal, planner meals),
//     sleep 540 minutes (Pass 2 life task lf.sleep, 22:00-07:00).
//     Meal duration (15 min example) and sleep duration (6-8 h guess)
//     remain open Kennedy decisions; changing them here without a
//     decision would be canonising a guess.
//   - Seconds: no sub-minute leg is authored. Auto-eat and auto-sleep
//     legs are whole-minute travel plus 30 / 540 minute life tasks.
//     The Pass 2 seconds blocklist stands.
//   - Heating: NOT implemented here. No heating leg, no stove check,
//     no fuel purchase is added by auto-care; heating decisions are
//     still pending (see the Pass 4 review doc).
using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>A food seller, as supplied by the production shopping system. Codex wires a real seller behind this; the model never invents one.</summary>
    public interface IPass4FoodSeller
    {
        string LocationId { get; }
        bool IsOpen { get; }
        bool HasFood { get; }
        int FoodPriceCents { get; }
        bool OffersTradeCredit { get; }
        int TradeCreditLimitCents { get; }
    }

    /// <summary>Lodging, as supplied by the production boarding/lodging system. Codex wires a real lodging behind this; the model never invents one.</summary>
    public interface IPass4Lodging
    {
        string LocationId { get; }
        bool IsAvailable { get; }
        int NightPriceCents { get; }
        bool OffersTradeCredit { get; }
        int TradeCreditLimitCents { get; }
    }

    /// <summary>Route lookup. Production implementation: JourneyModel.FindRoute. Returns false with a reason when no route exists.</summary>
    public interface IPass4RouteProvider
    {
        bool TryRoute(string fromLocationId, string toLocationId, out int routeMinutes, out string reason);
    }

    public enum Pass4AutoEatState
    {
        Inactive = 0,
        FindingSeller = 1,
        TravellingToSeller = 2,
        AtSeller = 3,
        Eating = 4,
        Done = 5,
        Failed = 6,
    }

    public enum Pass4AutoSleepState
    {
        Inactive = 0,
        FindingLodging = 1,
        TravellingToLodging = 2,
        AtLodging = 3,
        Sleeping = 4,
        Done = 5,
        Failed = 6,
    }

    /// <summary>
    /// Auto-eat: stop for a break, pathfind to somewhere that sells food,
    /// eat, and pay appropriately assuming the player has money.
    /// Each leg is a separate step, so a driver can interrupt, and a
    /// failure leaves the player exactly where the failure happened.
    /// </summary>
    public sealed class Pass4AutoEat
    {
        public const int EatMinutes = 30;

        public bool ToggleOn { get; private set; }
        public Pass4AutoEatState State { get; private set; }
        public string CurrentLocationId = string.Empty;
        public int CashCents;
        public string FailureReason = string.Empty;
        public int TravelMinutesTotal;
        public int TravelMinutesElapsed;
        public string SellerLocationId = string.Empty;

        private IPass4FoodSeller seller;

        public Pass4AutoEat() { }

        public Pass4AutoEat(string startingLocationId, int cashCents)
        {
            CurrentLocationId = startingLocationId ?? string.Empty;
            CashCents = cashCents;
        }

        public bool Start(string needReason, List<string> diag)
        {
            if (!ToggleOn)
            {
                if (diag != null) diag.Add("Pass4AutoEat: auto-eat toggle is off; it will not start on its own.");
                return false;
            }
            if (State == Pass4AutoEatState.TravellingToSeller || State == Pass4AutoEatState.Eating)
            {
                if (diag != null) diag.Add("Pass4AutoEat: already in progress; finish or cancel it first.");
                return false;
            }
            State = Pass4AutoEatState.FindingSeller;
            FailureReason = string.Empty;
            if (diag != null && !string.IsNullOrEmpty(needReason)) diag.Add("Pass4AutoEat: started (" + needReason + ").");
            return true;
        }

        public void SetToggle(bool on, List<string> diag)
        {
            ToggleOn = on;
            if (!on && State != Pass4AutoEatState.Done && State != Pass4AutoEatState.Inactive)
            {
                State = Pass4AutoEatState.Inactive;
                if (diag != null) diag.Add("Pass4AutoEat: toggle off cancels auto-eat; player stays at " + CurrentLocationId + ", no teleport home.");
            }
        }

        /// <summary>Leg 1: a seller must be supplied by the shopping system. A null seller is a failure, not a conjured shop.</summary>
        public bool StepFindSeller(IPass4FoodSeller foundSeller, List<string> diag)
        {
            if (State != Pass4AutoEatState.FindingSeller) { if (diag != null) diag.Add("Pass4AutoEat: not in FindingSeller state."); return false; }
            if (foundSeller == null || string.IsNullOrEmpty(foundSeller.LocationId))
            {
                Fail("No food seller found; player stays where he is, no food conjured.");
                if (diag != null) diag.Add("Pass4AutoEat: " + FailureReason);
                return false;
            }
            seller = foundSeller;
            SellerLocationId = foundSeller.LocationId;
            State = Pass4AutoEatState.TravellingToSeller;
            TravelMinutesTotal = 0;
            TravelMinutesElapsed = 0;
            return true;
        }

        /// <summary>Leg 2: route must exist. No route means no travel, location unchanged, never a teleport.</summary>
        public bool StepTravel(IPass4RouteProvider routes, List<string> diag)
        {
            if (State != Pass4AutoEatState.TravellingToSeller) { if (diag != null) diag.Add("Pass4AutoEat: not in TravellingToSeller state."); return false; }
            if (routes == null)
            {
                Fail("No route provider; travel refused, location unchanged.");
                if (diag != null) diag.Add("Pass4AutoEat: " + FailureReason);
                return false;
            }
            int minutes;
            string reason;
            if (!routes.TryRoute(CurrentLocationId, SellerLocationId, out minutes, out reason))
            {
                Fail("No route to food seller: " + (reason ?? "unknown") + "; location unchanged, no teleport.");
                if (diag != null) diag.Add("Pass4AutoEat: " + FailureReason);
                return false;
            }
            if (string.Equals(CurrentLocationId, SellerLocationId, StringComparison.Ordinal) || minutes <= 0)
            {
                CurrentLocationId = SellerLocationId;
            }
            else
            {
                // The model records the route's minutes honestly and
                // changes location only because a real route was found.
                // Minute-by-minute advancement is the production
                // driver's job (PlayerDirector.RecordMovementProgress);
                // this step is the travel leg, not a teleport: without
                // a route, the step above already refused and the
                // location did not change.
                TravelMinutesTotal = minutes;
                TravelMinutesElapsed = minutes;
                CurrentLocationId = SellerLocationId;
            }
            State = Pass4AutoEatState.AtSeller;
            return true;
        }

        /// <summary>Leg 3-4: seller must be open, must have food, and the player must be able to pay (cash, or offered trade credit within limit). Otherwise failure at the seller, player stays at the seller, no free meal.</summary>
        public bool StepEat(List<string> diag)
        {
            if (State != Pass4AutoEatState.AtSeller || seller == null) { if (diag != null) diag.Add("Pass4AutoEat: not at a seller."); return false; }
            if (!seller.IsOpen)
            {
                Fail("Food seller is closed; player stays at the seller, no meal eaten.");
                if (diag != null) diag.Add("Pass4AutoEat: " + FailureReason);
                return false;
            }
            if (!seller.HasFood)
            {
                Fail("Food seller has no food; player stays at the seller, no meal eaten.");
                if (diag != null) diag.Add("Pass4AutoEat: " + FailureReason);
                return false;
            }
            int price = seller.FoodPriceCents;
            if (price < 0)
            {
                Fail("Food seller price is invalid; meal refused, nothing charged.");
                if (diag != null) diag.Add("Pass4AutoEat: " + FailureReason);
                return false;
            }
            if (CashCents >= price)
            {
                CashCents -= price;
            }
            else if (seller.OffersTradeCredit && price <= seller.TradeCreditLimitCents)
            {
                if (diag != null) diag.Add("Pass4AutoEat: meal on offered trade credit (production: FinancialObligationAuthority, never invented debt).");
            }
            else
            {
                Fail("Cannot pay for food (cash " + CashCents + "c, price " + price + "c, no sufficient trade credit); player stays at the seller, no meal eaten, no debt invented.");
                if (diag != null) diag.Add("Pass4AutoEat: " + FailureReason);
                return false;
            }
            State = Pass4AutoEatState.Eating;
            // Eating takes EatMinutes (30, existing code) as a life task
            // in production (Pass 2 lf.eat-meal). The model completes
            // the meal here; a driver books those minutes against the
            // player's day, it does not skip them.
            State = Pass4AutoEatState.Done;
            return true;
        }

        /// <summary>
        /// Series 2 Pass 9, additive only: clear-then-load restore
        /// of a saved auto-eat snapshot. Toggle, state, location,
        /// model cash gate, failure reason, seller location, and
        /// travel progress are reinstated verbatim. This restores
        /// the MODEL state only: the seller object itself is NOT
        /// in the snapshot (it is a live shopping-system object),
        /// so a restored AtSeller state must have its seller
        /// re-supplied by the driver before StepEat, and StepEat
        /// fails safely (known state, no free meal) if it is not.
        /// An out-of-range state restores as Inactive, diagnosed,
        /// never as a made-up state. Restoring twice yields the
        /// same state (idempotent). This is NOT a toggle action:
        /// restoring a toggle does not cancel an in-flight state
        /// the way SetToggle(false) does.
        /// </summary>
        public void RestoreSnapshot(
            bool toggleOn, Pass4AutoEatState state, string locationId, int cashCents,
            string failureReason, string sellerLocationId, int travelMinutesTotal, int travelMinutesElapsed,
            List<string> diag)
        {
            ToggleOn = toggleOn;
            if ((int)state < 0 || (int)state > (int)Pass4AutoEatState.Failed)
            {
                State = Pass4AutoEatState.Inactive;
                if (diag != null) diag.Add("Pass4AutoEat: saved state was out of range; restored as Inactive, never as a made-up state.");
            }
            else
            {
                State = state;
            }

            CurrentLocationId = locationId ?? string.Empty;
            CashCents = cashCents;
            FailureReason = failureReason ?? string.Empty;
            SellerLocationId = sellerLocationId ?? string.Empty;
            TravelMinutesTotal = Math.Max(0, travelMinutesTotal);
            TravelMinutesElapsed = Math.Max(0, travelMinutesElapsed);
            seller = null;
        }

        private void Fail(string reason)
        {
            State = Pass4AutoEatState.Failed;
            FailureReason = reason ?? "Auto-eat failed.";
        }

        public string RenderStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Auto-eat: toggle " + (ToggleOn ? "on" : "off") + ", state " + State + ", location " + CurrentLocationId + ".");
            if (State == Pass4AutoEatState.Failed) sb.AppendLine("  Failed: " + FailureReason);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Auto-sleep: pathfind to lodging (for example a boarding house
    /// when the player is in the middle of nowhere), sleep the night.
    /// Same no-fake rules as auto-eat: no lodging means failure where
    /// the player stands, never a teleported bed, never a free night.
    /// </summary>
    public sealed class Pass4AutoSleep
    {
        public const int SleepMinutes = 540;

        public bool ToggleOn { get; private set; }
        public Pass4AutoSleepState State { get; private set; }
        public string CurrentLocationId = string.Empty;
        public int CashCents;
        public string FailureReason = string.Empty;
        public string LodgingLocationId = string.Empty;

        private IPass4Lodging lodging;

        public Pass4AutoSleep() { }

        public Pass4AutoSleep(string startingLocationId, int cashCents)
        {
            CurrentLocationId = startingLocationId ?? string.Empty;
            CashCents = cashCents;
        }

        public bool Start(string needReason, List<string> diag)
        {
            if (!ToggleOn)
            {
                if (diag != null) diag.Add("Pass4AutoSleep: auto-sleep toggle is off; it will not start on its own.");
                return false;
            }
            if (State == Pass4AutoSleepState.TravellingToLodging || State == Pass4AutoSleepState.Sleeping)
            {
                if (diag != null) diag.Add("Pass4AutoSleep: already in progress; finish or cancel it first.");
                return false;
            }
            State = Pass4AutoSleepState.FindingLodging;
            FailureReason = string.Empty;
            if (diag != null && !string.IsNullOrEmpty(needReason)) diag.Add("Pass4AutoSleep: started (" + needReason + ").");
            return true;
        }

        public void SetToggle(bool on, List<string> diag)
        {
            ToggleOn = on;
            if (!on && State != Pass4AutoSleepState.Done && State != Pass4AutoSleepState.Inactive)
            {
                State = Pass4AutoSleepState.Inactive;
                if (diag != null) diag.Add("Pass4AutoSleep: toggle off cancels auto-sleep; player stays at " + CurrentLocationId + ", no teleport home.");
            }
        }

        /// <summary>Leg 1: lodging must be supplied and available. Unavailable lodging fails BEFORE travel, so the player does not walk to a closed door.</summary>
        public bool StepFindLodging(IPass4Lodging foundLodging, List<string> diag)
        {
            if (State != Pass4AutoSleepState.FindingLodging) { if (diag != null) diag.Add("Pass4AutoSleep: not in FindingLodging state."); return false; }
            if (foundLodging == null || string.IsNullOrEmpty(foundLodging.LocationId))
            {
                Fail("No lodging found; player stays where he is, no bed conjured.");
                if (diag != null) diag.Add("Pass4AutoSleep: " + FailureReason);
                return false;
            }
            if (!foundLodging.IsAvailable)
            {
                Fail("Lodging at '" + foundLodging.LocationId + "' is not available; player stays where he is.");
                if (diag != null) diag.Add("Pass4AutoSleep: " + FailureReason);
                return false;
            }
            lodging = foundLodging;
            LodgingLocationId = foundLodging.LocationId;
            State = Pass4AutoSleepState.TravellingToLodging;
            return true;
        }

        /// <summary>Leg 2: route must exist, otherwise no travel and no arrival.</summary>
        public bool StepTravel(IPass4RouteProvider routes, List<string> diag)
        {
            if (State != Pass4AutoSleepState.TravellingToLodging) { if (diag != null) diag.Add("Pass4AutoSleep: not in TravellingToLodging state."); return false; }
            if (routes == null)
            {
                Fail("No route provider; travel refused, location unchanged.");
                if (diag != null) diag.Add("Pass4AutoSleep: " + FailureReason);
                return false;
            }
            int minutes;
            string reason;
            if (!routes.TryRoute(CurrentLocationId, LodgingLocationId, out minutes, out reason))
            {
                Fail("No route to lodging: " + (reason ?? "unknown") + "; location unchanged, no teleport.");
                if (diag != null) diag.Add("Pass4AutoSleep: " + FailureReason);
                return false;
            }
            CurrentLocationId = LodgingLocationId;
            State = Pass4AutoSleepState.AtLodging;
            return true;
        }

        /// <summary>Leg 3-4: pay for the night (cash, or offered trade credit within limit), then sleep 540 minutes. Unaffordable means failure at the lodging, not a free night.</summary>
        public bool StepSleep(List<string> diag)
        {
            if (State != Pass4AutoSleepState.AtLodging || lodging == null) { if (diag != null) diag.Add("Pass4AutoSleep: not at lodging."); return false; }
            int price = lodging.NightPriceCents;
            if (price < 0)
            {
                Fail("Lodging price is invalid; night refused, nothing charged.");
                if (diag != null) diag.Add("Pass4AutoSleep: " + FailureReason);
                return false;
            }
            if (CashCents >= price)
            {
                CashCents -= price;
            }
            else if (lodging.OffersTradeCredit && price <= lodging.TradeCreditLimitCents)
            {
                if (diag != null) diag.Add("Pass4AutoSleep: night on offered trade credit (production: FinancialObligationAuthority, never invented debt).");
            }
            else
            {
                Fail("Cannot pay for lodging (cash " + CashCents + "c, price " + price + "c, no sufficient trade credit); player stays at the lodging, no night slept, no debt invented.");
                if (diag != null) diag.Add("Pass4AutoSleep: " + FailureReason);
                return false;
            }
            State = Pass4AutoSleepState.Sleeping;
            // Sleeping takes SleepMinutes (540, existing code) as a life
            // task in production (Pass 2 lf.sleep). The model completes
            // the night here; a driver books those minutes, it does not
            // skip them.
            State = Pass4AutoSleepState.Done;
            return true;
        }

        /// <summary>
        /// Series 2 Pass 9, additive only: clear-then-load restore
        /// of a saved auto-sleep snapshot, with the same limits as
        /// auto-eat (see Pass4AutoEat.RestoreSnapshot): the lodging
        /// object itself is NOT in the snapshot, so a restored
        /// AtLodging state must have its lodging re-supplied by
        /// the driver before StepSleep, and StepSleep fails safely
        /// (known state, no free night) if it is not. An
        /// out-of-range state restores as Inactive, diagnosed.
        /// </summary>
        public void RestoreSnapshot(
            bool toggleOn, Pass4AutoSleepState state, string locationId, int cashCents,
            string failureReason, string lodgingLocationId, List<string> diag)
        {
            ToggleOn = toggleOn;
            if ((int)state < 0 || (int)state > (int)Pass4AutoSleepState.Failed)
            {
                State = Pass4AutoSleepState.Inactive;
                if (diag != null) diag.Add("Pass4AutoSleep: saved state was out of range; restored as Inactive, never as a made-up state.");
            }
            else
            {
                State = state;
            }

            CurrentLocationId = locationId ?? string.Empty;
            CashCents = cashCents;
            FailureReason = failureReason ?? string.Empty;
            LodgingLocationId = lodgingLocationId ?? string.Empty;
            lodging = null;
        }

        private void Fail(string reason)
        {
            State = Pass4AutoSleepState.Failed;
            FailureReason = reason ?? "Auto-sleep failed.";
        }

        public string RenderStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Auto-sleep: toggle " + (ToggleOn ? "on" : "off") + ", state " + State + ", location " + CurrentLocationId + ".");
            if (State == Pass4AutoSleepState.Failed) sb.AppendLine("  Failed: " + FailureReason);
            return sb.ToString();
        }
    }
}
