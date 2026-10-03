using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// BIZ-1 (addendum): the contract Kennedy's Unity UI implements for the Create
    /// Business form (GHOST-DES-029). The presenter drives this interface; the visual
    /// form (dropdowns, fields, buttons) binds to it. See
    /// <c>CreateBusinessViewContract.md</c> for the exact binding each control needs.
    ///
    /// The view NEVER runs the creation workflow itself — it collects input and reports
    /// events. The presenter validates via <see cref="CreateBusinessFormModel"/> and
    /// runs the canonical <see cref="BusinessCreationAuthority"/> on submit.
    /// </summary>
    public interface ICreateBusinessView
    {
        /// <summary>The form being edited. The view reads it to populate controls and
        /// writes user input back into it (via the Set/Add/Remove methods).</summary>
        CreateBusinessFormModel Form { get; }

        /// <summary>Dropdown source: all 19 creatable business types with display names.</summary>
        IReadOnlyList<BusinessTypeOption> BusinessTypeOptions { get; }

        /// <summary>Dropdown source: the player's owned parcels/properties (GHOST-DES-031:
        /// includes parcels already used by another business).</summary>
        IReadOnlyList<PropertyOption> OwnedPropertyOptions { get; }

        /// <summary>Multi-select source: capability ids with display names.</summary>
        IReadOnlyList<CapabilityOption> CapabilityOptions { get; }

        /// <summary>Raised by the view when the player presses Create/Submit.</summary>
        event Action Submitted;

        /// <summary>Raised by the view when the player cancels/closes the form.</summary>
        event Action Cancelled;

        /// <summary>Called by the presenter when validation fails: show these errors
        /// next to the form (human-readable, one per problem).</summary>
        void ShowErrors(IReadOnlyList<string> errors);

        /// <summary>Called by the presenter when creation succeeds.</summary>
        void ShowSuccess(BusinessCreationResult result);

        /// <summary>Called by the presenter when the workflow fails after validation
        /// (e.g. no suitable site in the world).</summary>
        void ShowWorkflowFailure(IReadOnlyList<string> diagnostics);

        /// <summary>Called by the presenter to close the form after success or cancel.</summary>
        void Close();
    }

    /// <summary>One row of the business-type picker dropdown.</summary>
    [Serializable]
    public sealed class BusinessTypeOption
    {
        public BusinessType Type;
        public string DisplayName;
        public string Hint;

        public BusinessTypeOption(BusinessType type, string displayName, string hint = "")
        {
            Type = type;
            DisplayName = displayName ?? type.ToString();
            Hint = hint ?? string.Empty;
        }
    }

    /// <summary>One row of the owned-property picker dropdown (GHOST-DES-031).</summary>
    [Serializable]
    public sealed class PropertyOption
    {
        public string PropertyId;
        public string DisplayName;
        public bool AlreadyOccupied;
        public string OccupiedBy;

        public PropertyOption(string propertyId, string displayName, bool alreadyOccupied, string occupiedBy = "")
        {
            PropertyId = propertyId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            AlreadyOccupied = alreadyOccupied;
            OccupiedBy = occupiedBy ?? string.Empty;
        }
    }

    /// <summary>One row of the capability multi-select.</summary>
    [Serializable]
    public sealed class CapabilityOption
    {
        public string CapabilityId;
        public string DisplayName;

        public CapabilityOption(string capabilityId, string displayName)
        {
            CapabilityId = capabilityId ?? string.Empty;
            DisplayName = displayName ?? capabilityId ?? string.Empty;
        }
    }
}
