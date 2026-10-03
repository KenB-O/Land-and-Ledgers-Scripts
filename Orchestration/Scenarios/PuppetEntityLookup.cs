using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-2: dev-tool registry mapping EntityId → live object for the puppet master.
    /// Runtime systems (and the DEV-3 RuntimeEntityRoot registrar) register spawned
    /// entities here so Kennedy can type an id like "P12" in the puppet panel and land
    /// on the actual object — the antidote to "can't find anything in the editor".
    ///
    /// Explicitly a development aid: entries are informational, never authoritative.
    /// Authoritative lookups stay in the PKG-5 RuntimeEntityDirectory and domain registries.
    /// </summary>
    public sealed class PuppetEntityLookup
    {
        private sealed class Entry
        {
            public object Target;
            public string DisplayName;
        }

        private readonly Dictionary<EntityId, Entry> entries = new Dictionary<EntityId, Entry>();

        public int Count => entries.Count;

        public void Register(EntityId id, object target, string displayName)
        {
            if (!id.IsValid || target == null)
            {
                return;
            }

            entries[id] = new Entry { Target = target, DisplayName = displayName ?? id.ToString() };
        }

        public bool Unregister(EntityId id)
        {
            return entries.Remove(id);
        }

        public bool TryResolve(EntityId id, out object target, out string displayName)
        {
            target = null;
            displayName = null;

            if (entries.TryGetValue(id, out Entry entry))
            {
                target = entry.Target;
                displayName = entry.DisplayName;
                return true;
            }

            return false;
        }

        public List<EntityId> ListIds()
        {
            return new List<EntityId>(entries.Keys);
        }

        /// <summary>
        /// Global instance for the runtime panel. Edit-mode tooling should prefer passing
        /// instances explicitly; this exists so scattered runtime spawners can register
        /// without plumbing.
        /// </summary>
        public static PuppetEntityLookup Shared { get; } = new PuppetEntityLookup();
    }
}
