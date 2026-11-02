// Review 2 second scripts pass — the D3 save wiring bridge.
//
// The gap, stated plainly: D3 proved its save section round-trips
// when the test drives the state object directly, but in production
// there are TWO feed registries and nothing joins them:
//
//   - SimulationDrivers.storeFeedRegistry — the live registry the
//     driver's ResolveHorseFeedStock reads (D2). This is live truth.
//   - Review2VerticalD3SaveState.FeedRegistry — the registry the
//     coordinator's Capture reads and Restore repopulates (D3).
//
// Until a driver binds them, Capture persists whatever the state's
// own (empty, unless someone populated it) registry holds, and
// Restore repopulates a registry the driver never reads — so the
// D3 save feature is dormant twice over, not once: assigning the
// state to SaveLoadManager alone does NOT wire it.
//
// The fix is one shared instance, not a copy: this helper assigns
// the driver's registry object to the state. Capture then reads
// live truth, and Restore mutates the shared instance in place
// (it clears and repopulates, it never replaces a non-null
// registry), so the driver sees restored entries without any
// re-binding. The helper refuses — loudly, without touching
// either registry — when the state's existing registry already
// holds entries that binding would discard, because that shape
// means the integrator bound the wrong instance or bound after
// a restore; the correct order is bind at bootstrap, before the
// first save AND before the first load. Re-binding the same
// instance is a no-op that returns null. If SaveLoadManager ever
// replaces the state object itself, bind again to the new object.
//
// Unity-side step left for Codex (one line at bootstrap, plus
// assigning the state to SaveLoadManager before save/load as
// Pass 9/10 require): call BindDriverRegistry with the driver's
// live registry and the D3 state. That call site is MonoBehaviour
// code and is NOT Roslyn-testable; everything in this file is.
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Persistence
{
    public static class Review2SecondScriptsSaveWiring
    {
        /// <summary>
        /// Binds the driver's live feed registry to the D3 save state
        /// by shared instance. Returns null on success (including
        /// an idempotent re-bind of the same instance), or a
        /// diagnostic on refusal. Refusals change nothing.
        /// </summary>
        public static string BindDriverRegistry(
            Review2VerticalD3SaveState state,
            Review2StoreFeedRegistry driverRegistry,
            List<string> diag)
        {
            if (state == null)
            {
                return "Review2SecondScriptsSaveWiring: no D3 save state supplied — nothing bound.";
            }

            if (driverRegistry == null)
            {
                return "Review2SecondScriptsSaveWiring: no driver feed registry supplied — nothing bound.";
            }

            if (ReferenceEquals(state.FeedRegistry, driverRegistry))
            {
                diag?.Add("Review2SecondScriptsSaveWiring: driver registry already bound to the D3 state (idempotent re-bind).");
                return null;
            }

            if (state.FeedRegistry != null)
            {
                int held = 0;
                foreach (Review2StoreFeedItem item in state.FeedRegistry.All)
                {
                    if (item != null) held++;
                }

                if (held > 0)
                {
                    return $"Review2SecondScriptsSaveWiring: the D3 state's registry already holds {held} feed registration(s) " +
                        "from a different registry instance — binding the driver registry now would discard them. " +
                        "Bind at bootstrap, before the first save and before the first load; nothing was changed.";
                }
            }

            state.FeedRegistry = driverRegistry;
            diag?.Add("Review2SecondScriptsSaveWiring: driver feed registry bound to the D3 state by shared instance; " +
                "Capture now reads live driver registrations and Restore repopulates the driver registry in place.");
            return null;
        }
    }
}
