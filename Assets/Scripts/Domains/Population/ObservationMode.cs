using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V (Worker 4, brief section 25 / slice J): how fully a person or
    /// vehicle is currently represented to an observer. This is PRESENTATION
    /// state only. It must NEVER change simulation outcomes: wages, production,
    /// aging, travel, and cash are computed from the authoritative state by the
    /// domain authorities, which do not read this enum.
    /// </summary>
    public enum ObservationMode
    {
        Unspecified = 0,
        /// <summary>Fully simulated and fully rendered (mesh, agent, UI).</summary>
        FullyRepresented = 1,
        /// <summary>Simulated at full resolution; no rendered representation.</summary>
        OffScreen = 2,
        /// <summary>Simulated at full resolution; region paged out of the scene.</summary>
        StreamedOut = 3,
        /// <summary>Legitimately off-map (migration, external freight); journey
        /// authority persists the traveler, simulation continues.</summary>
        OffMap = 4,
    }

    /// <summary>
    /// Presentation-side notification hook. Sinks are told AFTER the simulation
    /// has computed and committed an outcome; they receive read-only labels and
    /// cannot mutate simulation state through this interface.
    /// </summary>
    public interface IObservationSink
    {
        void Notify(ObservationMode mode, string eventName, string detail);
    }

    /// <summary>
    /// Pass V (Worker 4): the one sanctioned bridge between simulation and
    /// presentation for observation-mode reporting.
    ///
    /// Contract:
    /// - Simulation code calls Notify AFTER outcomes are committed.
    /// - Sinks receive (mode, eventName, detail) and nothing else; there is no
    ///   return path into the simulation.
    /// - Attaching, detaching, or clearing sinks never changes simulation
    ///   results (section 25B: repeated presentation activation/deactivation).
    /// - The bridge is process-wide in the harness; the Unity scene owns the
    ///   real presentation wiring (Codex side).
    /// </summary>
    public static class SimulationPresentationBridge
    {
        private static readonly List<IObservationSink> sinks = new List<IObservationSink>();

        public static int SinkCount => sinks.Count;

        public static void AttachSink(IObservationSink sink)
        {
            if (sink == null || sinks.Contains(sink))
            {
                return;
            }
            sinks.Add(sink);
        }

        public static void DetachSink(IObservationSink sink)
        {
            sinks.Remove(sink);
        }

        /// <summary>Test support: detaches every sink.</summary>
        public static void ClearSinks()
        {
            sinks.Clear();
        }

        /// <summary>
        /// Notifies attached sinks. Simulation state is never read back from a
        /// sink; exceptions in sinks are contained so a presentation fault can
        /// never corrupt the simulation.
        /// </summary>
        public static void Notify(ObservationMode mode, string eventName, string detail)
        {
            for (int i = 0; i < sinks.Count; i++)
            {
                try
                {
                    sinks[i].Notify(mode, eventName, detail);
                }
                catch (Exception)
                {
                    // Presentation faults stay presentation-side. The simulation
                    // outcome they were told about is already committed.
                }
            }
        }
    }
}
