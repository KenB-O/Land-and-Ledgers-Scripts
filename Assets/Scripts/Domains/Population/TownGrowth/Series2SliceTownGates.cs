// Series 2 Pass 10 (town gates, decision-gated only): the slice town
// gate switches as one named object, so Kennedy's activation is a
// single caller-supplied change, never a silent default flip.
//
// The switches themselves already exist in Pass 5
// (Pass5TownInputs.SliceSuppressArrivals and
// Pass5TownInputs.DepartureGateDecision, plus
// Pass5TownDriverModel.WithLegacyRollRetired). This class does NOT
// invent a second gate: it carries the Pass 10 defaults, applies
// them to Pass 5 inputs, and states the live behaviour in words.
//
// LIVE DEFAULTS (asserted by the Pass 10 harness, not desired
// future behaviour):
//   - Slice arrivals SUPPRESSED (SuppressSliceArrivals = true).
//   - Departures stay LIVE on the legacy roll
//     (DepartureGateDecision = Undecided, RetireLegacyRoll = false).
//   - Legacy departures are NEVER rewritten with an invented
//     destination; see Pass5DepartureModel.PreserveLegacyDeparture.
// ACTIVATION (Kennedy's one line, at the driver call site):
//   gates.DepartureGateDecision = ActivateGate, and, only with a
//   separate Kennedy decision, gates.RetireLegacyRoll = true.
//   Retirement is deliberately NOT applied by ApplyTo: retiring
//   the roll changes what the driver calls, which only the driver
//   owner (Codex) wires. Validate() refuses a retirement request
//   that arrives without a departure decision, so a half-applied
//   activation cannot pass as a decision.
using System;
using System.Collections.Generic;

namespace LandLedgers.Population.TownGrowth
{
    /// <summary>
    /// Series 2 Pass 10: decision-gated slice town gates. Plain data
    /// plus ApplyTo/Validate/Describe; no driver calls, no arrivals,
    /// no departures are created here.
    /// </summary>
    public sealed class Series2SliceTownGates
    {
        /// <summary>Live slice behaviour: arrivals suppressed.</summary>
        public bool SuppressSliceArrivals = true;

        /// <summary>
        /// Live behaviour: Undecided, which Pass 5 documents as
        /// "departures stay live on the legacy monthly roll".
        /// </summary>
        public Pass5SliceDepartureGateDecision DepartureGateDecision =
            Pass5SliceDepartureGateDecision.Undecided;

        /// <summary>
        /// Live behaviour: false. True only by Kennedy decision,
        /// and only together with a departure decision (see
        /// Validate). This flag alone retires nothing.
        /// </summary>
        public bool RetireLegacyRoll = false;

        /// <summary>
        /// Applies the gate values to Pass 5 inputs. Retirement is
        /// NOT applied here: WithLegacyRollRetired is a separate
        /// driver step that only runs on Kennedy's decision.
        /// </summary>
        public Pass5TownInputs ApplyTo(Pass5TownInputs inputs)
        {
            if (inputs == null) return null;
            inputs.SliceSuppressArrivals = SuppressSliceArrivals;
            inputs.DepartureGateDecision = DepartureGateDecision;
            return inputs;
        }

        /// <summary>
        /// Returns null when the gate set is coherent, or a refusal
        /// diagnostic when it is not. A retirement request without
        /// a departure decision is incoherent: the caller must
        /// supply Kennedy's decision, never assume one.
        /// </summary>
        public string Validate()
        {
            if (RetireLegacyRoll &&
                DepartureGateDecision == Pass5SliceDepartureGateDecision.Undecided)
            {
                return "Series2SliceTownGates: legacy-roll retirement requested " +
                    "without a departure decision; Kennedy must decide the " +
                    "departure gate first. Nothing is retired by this object.";
            }
            return null;
        }

        /// <summary>Plain-language statement of the live behaviour.</summary>
        public List<string> Describe()
        {
            var lines = new List<string>();
            lines.Add("Series2SliceTownGates: slice arrivals " +
                (SuppressSliceArrivals ? "SUPPRESSED (live slice behaviour)." : "NOT suppressed (caller change; not the slice default)."));
            switch (DepartureGateDecision)
            {
                case Pass5SliceDepartureGateDecision.ActivateGate:
                    lines.Add("Series2SliceTownGates: departure gate ACTIVATED by Kennedy decision.");
                    break;
                case Pass5SliceDepartureGateDecision.LeaveLive:
                    lines.Add("Series2SliceTownGates: departures explicitly LEFT LIVE by Kennedy decision.");
                    break;
                default:
                    lines.Add("Series2SliceTownGates: departure gate UNDECIDED; departures stay live on the legacy roll. Do not assume gated.");
                    break;
            }
            lines.Add("Series2SliceTownGates: legacy roll " +
                (RetireLegacyRoll ? "retirement REQUESTED (driver wiring still required; past legacy departures stay untracked)." : "still live (not retired)."));
            lines.Add("Series2SliceTownGates: activation is Kennedy's one line at the driver call site; this object never applies it silently.");
            return lines;
        }
    }
}
