// Generic Business Reconstruction, section 14 support.
// Unity adapter mapping: how the plain-C# generic-business records
// in this folder map onto the existing Unity systems. This file
// contains NO UnityEngine references on purpose, so the core logic
// stays verifiable in the small Roslyn harness; the actual Unity
// binding (ScriptableObject fields, MonoBehaviour wiring, UI calls)
// is Unity AI's work, using this mapping as its spec. Every mapping
// below names the existing class and field exactly as found in the
// new root (Assets/Scripts/...). Nothing here renames a serialized
// class, field, or namespace.
// How to read each entry:
//   Plain record  -> existing system, existing field/property, action.
// If the existing system has no field for a plain-record field, the
// entry says ADD (new serialized field, additive only) and names
// the exact class to extend. Additive fields are save-safe; renames
// are not, and none are proposed here.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public sealed class UnityMappingEntry
    {
        public string PlainRecord = string.Empty;
        public string PlainField = string.Empty;
        public string ExistingClass = string.Empty;
        public string ExistingFieldOrMethod = string.Empty;
        public string Action = string.Empty;
    }

    public static class GenericBusinessUnityAdapter
    {
        public static IReadOnlyList<UnityMappingEntry> Mappings()
        {
            return new[]
            {
                new UnityMappingEntry
                {
                    PlainRecord = "GenericBusinessCapabilitySet",
                    PlainField = "EnabledKinds / RetailProductIds",
                    ExistingClass = "BusinessInstanceState (Domains/Economy/Core/BusinessInstanceState.cs)",
                    ExistingFieldOrMethod = "AddCapability(string) / existing capability list",
                    Action = "MAP: populate the instance's capability list from the set. Retail expansion maps to AddCapability(\"retail\") once plus the retail product lines on GenericRetailConfiguration (see below). Do not create a second BusinessInstanceState per capability.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "GenericBusinessCapabilitySet",
                    PlainField = "RetailProductIds",
                    ExistingClass = "GenericRetailConfiguration (Domains/Economy/Generic/GenericBusinessEconomy.cs)",
                    ExistingFieldOrMethod = "AddOrReplaceLine(productId) / TryGetLine",
                    Action = "MAP: each retail product id becomes a product line. Shelf vs back storage is NOT in this class: the ordering UI in a later pass must add shelf/back pools (capacityPools) per product. Do not weaken the General Store single-SKU guard until a tested generic guard replaces it.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "GenericBusinessCapabilitySet",
                    PlainField = "EnabledProductionMethodIds",
                    ExistingClass = "GenericBusinessConfiguration (Domains/Economy/Generic/GenericBusinessEconomy.cs)",
                    ExistingFieldOrMethod = "AddProductionMethod(ProductionMethodDefinition)",
                    Action = "MAP: each enabled method id resolves to a ProductionMethodDefinition and is added via AddProductionMethod (replaces same MethodId, never duplicates). Owner enables methods; the runtime still checks equipment, workspace, inputs, and feasibility at execution.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "ProductionRecipeDetail",
                    PlainField = "Byproducts / FuelOrEnergyInputs / InputSubstitutions / RequiredFloorAreaSqM / WasteFraction01 / FeasibilityConditions",
                    ExistingClass = "ProductionMethodDefinition (Domains/Economy/Generic/GenericBusinessEconomy.cs)",
                    ExistingFieldOrMethod = "ADDITIVE fields added in this pass: ByproductOutputs, FuelOrEnergyInputs, InputSubstitutions, RequiredFloorAreaSqM, WasteFraction01, FeasibilityConditions",
                    Action = "ADDITIVE ONLY: new fields default empty/zero and are populated by Unity AI from authored methods. No existing field was renamed. Legacy methods without these fields behave exactly as before (no byproducts, no substitutions, no floor-area gate, zero waste).",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "ProductionRecipeDetail",
                    PlainField = "Phases (DurationGameSeconds, RequiresPerson)",
                    ExistingClass = "ProductionPhaseDefinition (Domains/Economy/Generic/GenericBusinessEconomy.cs) + ProductionStageContracts",
                    ExistingFieldOrMethod = "ElapsedMinutes (legacy) / DurationGameSeconds (stage contracts)",
                    Action = "MAP WITH LOSS WARNING: legacy phases are whole minutes; recipe phases are seconds. Unity AI must convert minutes = ceil(seconds / 60) when populating legacy phases, and prefer the seconds-native ProductionStageMachine path for sub-minute phases. The seconds value in the recipe is precise; the legacy minute value is the rounded one.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "GenericBusinessTemplates",
                    PlainField = "TemplateId / suggested lists",
                    ExistingClass = "BusinessProfileDefinition (Domains/Economy/Core/BusinessProfileDefinition.cs) + BusinessCapabilityRegistry",
                    ExistingFieldOrMethod = "ScriptableObject profile + CreateFallback(profile) + registry Register",
                    Action = "MAP: a template is a PRESET that fills a BusinessProfileDefinition's suggested catalog/equipment/methods, or a registry entry the UI offers. Applying a template must never write inventory, cash, equipment assets, or workers into the profile or the runtime. Templates are authoring aids, not grants.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "PremisesCapacity",
                    PlainField = "Floors / reservations / access-egress and storage reserves",
                    ExistingClass = "BuildingDefinition (Domains/World/Buildings/BuildingDefinition.cs)",
                    ExistingFieldOrMethod = "ADD: per-floor usable area, equipment footprint, workstation clearance, access/egress reserve, storage reserve (no such fields exist today; storeyCount and footprint cells exist)",
                    Action = "ADDITIVE ONLY: Unity AI adds the capacity fields to the building/prefab authoring data and maps footprint cells to sqm via the building's real dimensions. Do not invent a universal occupancy limit; capacity is computed from authored data, per floor.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "SiteEligibility",
                    PlainField = "PremisesRightKind / zone warning",
                    ExistingClass = "BusinessSiteRequirementDefinition (in BusinessProfileDefinition.cs) + BusinessSiteSuitabilityEvaluator",
                    ExistingFieldOrMethod = "Meets(TownPlot) (legacy hard gate) / CanOperate / TryEvaluatePlayerOwnedDevelopmentFit",
                    Action = "MIGRATE: new callers use SiteEligibility.Evaluate (zone = warning). Legacy Meets stays for existing callers until migrated; do not delete its serialized allowedPlotZones field in this pass. Real constraints (right, lease, access, suitability, nuisance, safety, legal, infrastructure) remain refusals.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "AcquisitionInterfaceModel / StartBusinessForm",
                    PlainField = "SelectedTab (Start a Business default) / premises right",
                    ExistingClass = "Acquisition UI (Domains/Economy/Acquisitions/Presentation + Workflows) + BusinessCreationAuthority / CreateBusinessIntent",
                    ExistingFieldOrMethod = "CreateBusinessFormModel / CreateBusinessPresenter / CreateBusinessIntent",
                    Action = "MAP: the two tabs are Start a Business (default selected) and Pre-existing Businesses. No Land tab in this interface; land acquisition remains via the Property/Parcel workflow. The start form converts to a CreateBusinessIntent only after Validate() passes (name, owner, premises right, at least one capability).",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "EquipmentPurchase",
                    PlainField = "AssetKind / AssetId / VendorId / PriceCents / FreightCents / Stage",
                    ExistingClass = "GenericEquipmentProcurementAuthority (Domains/Economy/Generic/GenericEquipmentProcurementAuthority.cs) + OffMapNamedSkuProcurementService / ExternalMarketAuthority",
                    ExistingFieldOrMethod = "Procurement lifecycle Quoted -> Ordered -> PaidOrFinanced -> Shipped -> Received -> Installed -> Operational",
                    Action = "MAP: a purchase line is the caller's view of a procurement record. Unity AI / Codex wire ordering, payment, delivery, and ownership registration to the existing authorities; this record never pays, delivers, or registers ownership by itself. Horse and wagon are separate lines with separate asset ids.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "ThroughputCapacity",
                    PlainField = "MaxOutputs / BindingConstraint",
                    ExistingClass = "SharedBusinessRuntimeManager constants (output per input, outlet maxima, sawmill weekly maxima)",
                    ExistingFieldOrMethod = "Legacy constants, still read by legacy callers",
                    Action = "REPLACEMENT PATH, NOT A DELETE: new callers compute capacity from materials, worker time, equipment, fuel, transport, and storage. Legacy constants stay until their callers migrate (Codex task). Do not replace them with another unexplained constant set.",
                },
                new UnityMappingEntry
                {
                    PlainRecord = "BusinessInspectorFields.Audit()",
                    PlainField = "Classification / Recommendation per field",
                    ExistingClass = "SharedBusinessRuntimeManager Inspector + BusinessInstanceState.MineState",
                    ExistingFieldOrMethod = "Inspector grouping / conditional display (Unity Editor work)",
                    Action = "UNITY AI: group mining state under a Mine capability foldout shown only for Mine businesses; keep minimumHireLaborAccess visible as a labor safeguard; mark useLegacyPassiveSeed and AllowedPlotZones legacy in tooltips/docs without deleting serialized fields.",
                },
            };
        }
    }
}
