using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-3, rule 2: the visible, named hierarchy root for everything spawned at runtime.
    /// Kennedy's standing law: NOTHING VANISHES. If it exists at runtime, you can find it
    /// in the hierarchy — no HideFlags.HideInHierarchy, no anonymous "(Clone)" objects.
    ///
    /// Usage: route ALL runtime spawning through <see cref="DevSpawn"/> (or call
    /// <see cref="RegisterSpawned"/> directly). Spawned objects are reparented under a
    /// top-level "Runtime Entities" root with a meaningful name, and registered in the
    /// <see cref="PuppetEntityLookup"/> so the puppet master finds them by id.
    ///
    /// Violations fail LOUDLY: hidden flags are cleared with an error log naming the
    /// caller, so the old "where did it go" bug becomes impossible to miss.
    /// </summary>
    public static class RuntimeEntityRoot
    {
        public const string RootName = "Runtime Entities";

        /// <summary>
        /// Pure, EditMode-testable: strips Unity's "(Clone)" suffix, trims whitespace,
        /// falls back to a meaningful default. Anonymous names are a bug-hunting hazard.
        /// </summary>
        public static string CleanSpawnName(string rawName, string fallbackName)
        {
            string cleaned = (rawName ?? string.Empty).Replace("(Clone)", string.Empty).Trim();

            if (string.IsNullOrEmpty(cleaned))
            {
                cleaned = string.IsNullOrEmpty(fallbackName) ? "Unnamed Runtime Entity" : fallbackName.Trim();
            }

            return cleaned;
        }

        /// <summary>
        /// Pure, EditMode-testable: true when the flags would hide the object from the
        /// hierarchy or make it unsavable/unfindable.
        /// </summary>
        public static bool HasHiddenFlags(HideFlags flags)
        {
            return (flags & HideFlags.HideInHierarchy) != 0
                || (flags & HideFlags.HideInInspector) != 0
                || (flags & HideFlags.NotEditable) != 0;
        }

        /// <summary>
        /// Returns the scene root, creating it visibly if missing. The root itself is a
        /// plain, visible GameObject — never hidden.
        /// </summary>
        public static GameObject GetOrCreateRoot()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                root.hideFlags = HideFlags.None;
            }

            return root;
        }

        /// <summary>
        /// Registers an already-spawned object: reparents under the visible root (unless
        /// the caller passes keepInPlace), enforces a meaningful name, clears hidden
        /// flags loudly, and indexes it in the puppet lookup.
        /// </summary>
        public static GameObject RegisterSpawned(
            GameObject spawned,
            string meaningfulName,
            EntityId entityId,
            bool keepInPlace = false,
            string caller = "")
        {
            if (spawned == null)
            {
                Debug.LogError($"[RuntimeEntityRoot] {caller}: attempted to register a null GameObject. " +
                               "Spawning failed upstream — fix the spawner, not the registrar.");
                return null;
            }

            spawned.name = CleanSpawnName(meaningfulName, spawned.name);

            if (HasHiddenFlags(spawned.hideFlags))
            {
                Debug.LogError($"[RuntimeEntityRoot] {caller}: '{spawned.name}' was created with hidden flags " +
                               $"({spawned.hideFlags}). Cleared — hidden runtime objects are forbidden (DEV-3 rule 2). " +
                               "Findability in the editor is a standing law, not a preference.");
                spawned.hideFlags = HideFlags.None;
            }

            if (!keepInPlace)
            {
                spawned.transform.SetParent(GetOrCreateRoot().transform, worldPositionStays: true);
            }

            if (entityId.IsValid)
            {
                PuppetEntityLookup.Shared.Register(entityId, spawned, spawned.name);
            }

            return spawned;
        }

        /// <summary>
        /// Unregisters on destroy. Call from OnDestroy on registered behaviours, or just
        /// let stop-play wipe the root (documented DEV-3 behavior: the root is transient).
        /// </summary>
        public static void Unregister(EntityId entityId)
        {
            if (entityId.IsValid)
            {
                PuppetEntityLookup.Shared.Unregister(entityId);
            }
        }
    }

    /// <summary>
    /// DEV-3, rule 2: the loud creation helper. Every runtime Instantiate in Land &amp; Ledgers
    /// game code should come through here so naming, visibility and lookup registration are
    /// enforced in one place instead of trusted to every call site.
    /// </summary>
    public static class DevSpawn
    {
        public static GameObject Instantiate(
            GameObject prefab,
            string meaningfulName,
            EntityId entityId,
            Vector3 position,
            Quaternion rotation,
            string caller = "")
        {
            if (prefab == null)
            {
                Debug.LogError($"[DevSpawn] {caller}: prefab is null. Cannot spawn '{meaningfulName}'.");
                return null;
            }

            GameObject spawned = Object.Instantiate(prefab, position, rotation);
            return RuntimeEntityRoot.RegisterSpawned(spawned, meaningfulName, entityId, caller: caller);
        }

        public static GameObject Instantiate(
            GameObject prefab,
            string meaningfulName,
            EntityId entityId,
            string caller = "")
        {
            return Instantiate(prefab, meaningfulName, entityId, Vector3.zero, Quaternion.identity, caller);
        }
    }

    /// <summary>
    /// DEV-3, rule 1: guards that fail LOUDLY when scenario state would live only in
    /// memory. Scenario definitions belong in ScriptableObject assets; these guards make
    /// the asset-less path impossible to miss in the console.
    /// </summary>
    public static class DevGuards
    {
        /// <summary>
        /// Returns false + logs an error when no scenario asset is supplied. Call before
        /// any bootstrap that needs authored scenario state.
        /// </summary>
        public static bool RequireScenarioAsset(ScenarioAsset asset, Object context, string caller)
        {
            if (asset == null)
            {
                Debug.LogError($"[DevGuards] {caller}: no ScenarioAsset supplied. Scenario state must live " +
                               "in a ScriptableObject asset (DEV-3 rule 1) — memory-only scenario state is " +
                               "forbidden because it vanishes at stop-play. Assign an asset.", context);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Warns (once per call site per session is the caller's job) when a runtime object
        /// carries hidden flags. Prefer DevSpawn/RegisterSpawned, which enforce this.
        /// </summary>
        public static void WarnIfHidden(GameObject gameObject, string caller)
        {
            if (gameObject != null && RuntimeEntityRoot.HasHiddenFlags(gameObject.hideFlags))
            {
                Debug.LogWarning($"[DevGuards] {caller}: '{gameObject.name}' has hidden flags " +
                                 $"({gameObject.hideFlags}). It will be invisible in the hierarchy — " +
                                 "route it through DevSpawn instead.");
            }
        }
    }
}
