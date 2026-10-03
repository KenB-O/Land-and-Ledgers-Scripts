using System;
using System.Collections.Generic;

namespace LandLedgers.Directory
{
    /// <summary>
    /// A domain-owned typed registry: id -> entity (3A-D02 §6.1). Domains own their registries;
    /// the RuntimeEntityDirectory only borrows them for typed cross-domain lookup. Names are
    /// architectural placeholders - a domain may expose any class implementing this contract.
    /// Duplicate registration is rejected deterministically (first registration wins) with a
    /// diagnostic, never a silent overwrite.
    /// </summary>
    public sealed class DomainEntityRegistry<TId, TEntity>
    {
        private readonly Dictionary<TId, TEntity> entries = new Dictionary<TId, TEntity>();
        private readonly List<string> diagnostics = new List<string>();

        public string RegistryName { get; }

        public DomainEntityRegistry(string registryName)
        {
            RegistryName = registryName ?? "UnnamedRegistry";
        }

        /// <summary>
        /// Registers an entity. Returns null on success, or a diagnostic string when the id is
        /// already registered (first registration wins; the duplicate is NOT applied).
        /// </summary>
        public string Register(TId id, TEntity entity, string source)
        {
            if (entries.ContainsKey(id))
            {
                string diagnostic =
                    $"[{RegistryName}] Duplicate registration rejected for id '{id}' from '{source ?? "unknown"}': " +
                    "first registration wins; duplicate not applied.";
                diagnostics.Add(diagnostic);
                return diagnostic;
            }

            entries[id] = entity;
            return null;
        }

        public bool Unregister(TId id)
        {
            return entries.Remove(id);
        }

        public bool TryGet(TId id, out TEntity entity)
        {
            return entries.TryGetValue(id, out entity);
        }

        public bool Contains(TId id)
        {
            return entries.ContainsKey(id);
        }

        public int Count => entries.Count;

        public IReadOnlyList<string> Diagnostics => diagnostics;
    }

    /// <summary>
    /// Lightweight cross-domain RuntimeEntityDirectory (3A-D02 §6.2, PL-07). Provides typed
    /// cross-domain lookup, existence checks, reference diagnostics, registration validation,
    /// generic tooling and save-reconstruction support WITHOUT owning domain data: it never
    /// owns person health, business cash, building condition, household finances, employment
    /// wages or occupancy rent. It must not become another god manager.
    /// Registration is explicit and additive; existing managers are NOT rewired to use it in
    /// PKG-5. Adoption is per-domain and incremental.
    /// </summary>
    public sealed class RuntimeEntityDirectory
    {
        private readonly Dictionary<EntityRef, object> entities = new Dictionary<EntityRef, object>();
        private readonly List<ReferenceDeclaration> references = new List<ReferenceDeclaration>();
        private readonly List<string> registrationDiagnostics = new List<string>();

        /// <summary>
        /// Registers a live entity handle. The directory stores the handle for lookup only;
        /// domain state authority stays with the owning domain.
        /// </summary>
        public void RegisterEntity(EntityRef entityRef, object entity)
        {
            if (!entityRef.IsValid)
            {
                registrationDiagnostics.Add(
                    $"Rejected registration of invalid EntityRef '{entityRef}': kind/ id missing.");
                return;
            }

            if (entity == null)
            {
                registrationDiagnostics.Add(
                    $"Rejected null entity for '{entityRef}': directory never invents entities.");
                return;
            }

            if (entities.ContainsKey(entityRef))
            {
                registrationDiagnostics.Add(
                    $"Duplicate registration rejected for '{entityRef}': first registration wins; duplicate not applied.");
                return;
            }

            entities[entityRef] = entity;
        }

        public bool UnregisterEntity(EntityRef entityRef)
        {
            return entities.Remove(entityRef);
        }

        /// <summary>Existence check: is this entity currently registered?</summary>
        public bool Exists(EntityRef entityRef)
        {
            return entityRef.IsValid && entities.ContainsKey(entityRef);
        }

        /// <summary>Typed cross-domain lookup. Returns false when unregistered or mistyped.</summary>
        public bool TryResolve<T>(EntityRef entityRef, out T entity)
        {
            entity = default;
            if (!entityRef.IsValid)
            {
                return false;
            }

            if (!entities.TryGetValue(entityRef, out object stored))
            {
                return false;
            }

            if (stored is T typed)
            {
                entity = typed;
                return true;
            }

            registrationDiagnostics.Add(
                $"Type mismatch resolving '{entityRef}': registered as {stored.GetType().Name}, requested {typeof(T).Name}.");
            return false;
        }

        /// <summary>Declares a cross-domain reference for later validation.</summary>
        public void DeclareReference(EntityRef from, EntityRef to, ReferenceClass referenceClass, string description)
        {
            references.Add(new ReferenceDeclaration
            {
                From = from,
                To = to,
                ReferenceClass = referenceClass,
                Description = description ?? string.Empty,
            });
        }

        public IReadOnlyList<string> RegistrationDiagnostics => registrationDiagnostics;

        /// <summary>
        /// Validates all declared references per the 3A-D02 §7 reference classes. Deterministic:
        /// declarations are validated in declaration order.
        /// </summary>
        public List<EntityReferenceDiagnostic> ValidateReferences()
        {
            var results = new List<EntityReferenceDiagnostic>(references.Count);
            for (int i = 0; i < references.Count; i++)
            {
                results.Add(ValidateOne(references[i]));
            }

            return results;
        }

        private EntityReferenceDiagnostic ValidateOne(ReferenceDeclaration declaration)
        {
            bool targetExists = Exists(declaration.To);
            if (targetExists && declaration.ReferenceClass != ReferenceClass.Derived)
            {
                return new EntityReferenceDiagnostic
                {
                    Declaration = declaration,
                    Outcome = ReferenceValidationOutcome.Valid,
                    Details = $"Target '{declaration.To}' resolved.",
                };
            }

            switch (declaration.ReferenceClass)
            {
                case ReferenceClass.Required:
                    // Without this reference the object cannot validly execute: retain for
                    // diagnostics, mark Invalid/Quarantined, BLOCK execution, do not invent.
                    // (A resolved required reference returns Valid above and never reaches here.)
                    return new EntityReferenceDiagnostic
                    {
                        Declaration = declaration,
                        Outcome = ReferenceValidationOutcome.InvalidQuarantined,
                        Details = $"REQUIRED reference missing: '{declaration.From}' -> '{declaration.To}'. " +
                                  "Retain for diagnostics; mark Invalid/Quarantined; BLOCK execution; do not invent a replacement.",
                    };
                case ReferenceClass.Optional:
                    return new EntityReferenceDiagnostic
                    {
                        Declaration = declaration,
                        Outcome = ReferenceValidationOutcome.NormalizedUnresolved,
                        Details = $"Optional reference '{declaration.From}' -> '{declaration.To}' unresolved: " +
                                  "normalize to null/unresolved and continue.",
                    };
                case ReferenceClass.Historical:
                    return new EntityReferenceDiagnostic
                    {
                        Declaration = declaration,
                        Outcome = ReferenceValidationOutcome.HistoricalRetained,
                        Details = $"Historical reference '{declaration.From}' -> '{declaration.To}': " +
                                  "live target missing; retain the historical target or identity snapshot, never silently drop.",
                    };
                case ReferenceClass.Soft:
                    return new EntityReferenceDiagnostic
                    {
                        Declaration = declaration,
                        Outcome = ReferenceValidationOutcome.SoftUnresolved,
                        Details = $"Soft reference '{declaration.From}' -> '{declaration.To}' unresolved: " +
                                  "allowed per domain policy; dependent action unavailable.",
                    };
                case ReferenceClass.Derived:
                    return new EntityReferenceDiagnostic
                    {
                        Declaration = declaration,
                        Outcome = ReferenceValidationOutcome.DerivedNotAuthoritative,
                        Details = $"Derived reference '{declaration.From}' -> '{declaration.To}' is never authoritative: " +
                                  "rebuild after load; anything treating it as authority is a bug.",
                    };
                default:
                    return new EntityReferenceDiagnostic
                    {
                        Declaration = declaration,
                        Outcome = targetExists
                            ? ReferenceValidationOutcome.Valid
                            : ReferenceValidationOutcome.NormalizedUnresolved,
                        Details = $"Unclassified reference '{declaration.From}' -> '{declaration.To}': " +
                                  (targetExists ? "resolved." : "unresolved; treated as optional until classified."),
                    };
            }
        }
    }
}
