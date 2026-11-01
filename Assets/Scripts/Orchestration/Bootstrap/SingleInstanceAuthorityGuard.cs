using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Bootstrap
{
    /// <summary>
    /// Pass XII Worker A (LL-PLAY-002/003): process-wide claim registry for the
    /// scene authorities that must never operate twice. Two live instances of
    /// one economic driver would both subscribe to the clock and both drive
    /// the same businesses, so the second instance must be suppressed loudly
    /// instead of silently duplicating authority.
    ///
    /// Pure C# on purpose: no UnityEngine dependency, so the claim semantics
    /// and the duplicate audit below are verifiable in the Roslyn harness.
    /// MonoBehaviour owners claim in Awake, re-claim in OnEnable after a
    /// disable released the claim, and release in OnDisable/OnDestroy. A
    /// claim is keyed by authority name and held by reference identity; a
    /// release from any other holder is ignored, never a steal.
    /// </summary>
    public static class SingleInstanceAuthorityGuard
    {
        private static readonly Dictionary<string, object> claims =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// Claims sole operation of an authority for a holder. Returns true
        /// when this holder now owns the claim (including a re-claim by the
        /// holder that already owns it). Returns false, with a reason, when
        /// a different live holder already owns it.
        /// </summary>
        public static bool TryClaim(string authority, object holder, out string refusal)
        {
            refusal = null;
            if (string.IsNullOrWhiteSpace(authority) || holder == null)
            {
                refusal = "SingleInstanceAuthorityGuard: authority and holder are required.";
                return false;
            }

            if (claims.TryGetValue(authority, out object existing))
            {
                if (ReferenceEquals(existing, holder))
                {
                    return true;
                }

                refusal = $"SingleInstanceAuthorityGuard: '{authority}' is already claimed by a live authority — " +
                    "a second instance must not operate. Remove the duplicate from the scene.";
                return false;
            }

            claims[authority] = holder;
            return true;
        }

        /// <summary>
        /// Releases a claim, but only when the releasing holder owns it.
        /// Returns true when a claim was released.
        /// </summary>
        public static bool Release(string authority, object holder)
        {
            if (string.IsNullOrWhiteSpace(authority) || holder == null)
            {
                return false;
            }

            if (claims.TryGetValue(authority, out object existing)
                && ReferenceEquals(existing, holder))
            {
                claims.Remove(authority);
                return true;
            }

            return false;
        }

        public static bool IsClaimed(string authority)
        {
            return !string.IsNullOrWhiteSpace(authority) && claims.ContainsKey(authority);
        }

        public static bool IsClaimedBy(string authority, object holder)
        {
            return !string.IsNullOrWhiteSpace(authority) && holder != null
                && claims.TryGetValue(authority, out object existing)
                && ReferenceEquals(existing, holder);
        }

        /// <summary>
        /// Test seam only. Clears every claim so harness tests (which share
        /// one process) can run claim scenarios independently. Production
        /// owners release individually via Release; nothing in production
        /// calls ResetAll.
        /// </summary>
        public static void ResetAll()
        {
            claims.Clear();
        }
    }

    /// <summary>
    /// Pass XII Worker A (LL-PLAY-002/003): pure duplicate audit over scene
    /// censuses. Exactly one live instance of each authority is expected;
    /// zero means the bootstrap fallback wiring will create one (reported
    /// separately by the caller, not an error here), and two or more is a
    /// loud error naming every object so Codex can remove the duplicate
    /// from the scene. The bootstrapper builds the censuses with
    /// FindObjectsByType; this class only judges them, so it stays
    /// harness-testable.
    /// </summary>
    public static class BootstrapAuthorityAudit
    {
        /// <summary>
        /// One authority's scene census: its display name and the names of
        /// the scene objects found carrying it. Nested in the audit API it
        /// belongs to, rather than a free-standing type.
        /// </summary>
        public sealed class Census
        {
            public string Name = string.Empty;
            public List<string> ObjectNames = new List<string>();
        }

        /// <summary>
        /// Returns one error per authority carried by more than one scene
        /// object. Authorities with zero or one object produce no error.
        /// </summary>
        public static List<string> DescribeDuplicates(IReadOnlyList<Census> censuses)
        {
            var errors = new List<string>();
            if (censuses == null)
            {
                return errors;
            }

            foreach (Census census in censuses)
            {
                if (census == null || census.ObjectNames == null || census.ObjectNames.Count <= 1)
                {
                    continue;
                }

                var named = new List<string>();
                foreach (string name in census.ObjectNames)
                {
                    named.Add(string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name);
                }

                errors.Add(
                    $"Duplicate '{census.Name}' authority in scene ({census.ObjectNames.Count} instances: " +
                    string.Join(", ", named) +
                    ") — only one may operate. Keep the scene-authored instance and remove the other(s); " +
                    "a second instance would duplicate economic authority.");
            }

            return errors;
        }
    }
}
