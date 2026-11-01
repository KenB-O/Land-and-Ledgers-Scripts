// Generic Business Reconstruction, section 9.
// Acquisition interface model: Start a Business / Pre-existing
// Businesses, with Start a Business selected by default.
// This is the plain-C# tab and form model for Unity AI to bind UI
// to. It does NOT own the property-rights system and does NOT
// delete it: land acquisition stays accessible through the
// Property/Parcel workflow, which is deliberately a separate path
// here (LandTabRemovedFromThisInterface = true, LandStillAvailable
// via Property/Parcel workflow = true). There is no Land tab in
// this interface's tab list, by design, because the owner asked for
// it removed from THIS interface, not from the game.
// Starting a business does not universally require owning land:
// the premises right on the start form may be Owned, Leased,
// Rented, Shared, Mobile, or another valid arrangement (see
// SiteEligibility). The form refuses only a missing right.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public enum AcquisitionTab
    {
        StartABusiness = 0,
        PreExistingBusinesses = 1,
    }

    public sealed class AcquisitionInterfaceModel
    {
        public AcquisitionTab SelectedTab = AcquisitionTab.StartABusiness;
        public bool LandTabPresentInThisInterface = false;
        public bool LandAvailableViaPropertyParcelWorkflow = true;
        public List<string> PreExistingBusinessIds = new List<string>();

        public string SelectedTabName()
        {
            return SelectedTab == AcquisitionTab.StartABusiness ? "Start a Business" : "Pre-existing Businesses";
        }

        public void SelectTab(AcquisitionTab tab)
        {
            SelectedTab = tab;
        }

        /// <summary>
        /// The owner's requested default: Start a Business selected.
        /// </summary>
        public static AcquisitionInterfaceModel Default()
        {
            return new AcquisitionInterfaceModel { SelectedTab = AcquisitionTab.StartABusiness };
        }
    }

    public sealed class StartBusinessForm
    {
        public string BusinessDisplayName = string.Empty;
        public string TemplateId = string.Empty;
        public List<BusinessCapabilityKind> RequestedCapabilities = new List<BusinessCapabilityKind>();
        public PremisesRightKind PremisesRight = PremisesRightKind.None;
        public string PremisesDescription = string.Empty;
        public string OwnerId = string.Empty;

        /// <summary>
        /// Validate the form before any business is created. A form
        /// with no premises right is refused, because a business must
        /// not operate from space it has no right to use. A form with
        /// no name, no owner, and no capabilities is also refused:
        /// there is nothing to create. Validation creates nothing.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(BusinessDisplayName)) problems.Add("Business needs a name.");
            if (string.IsNullOrWhiteSpace(OwnerId)) problems.Add("Business needs an owner.");
            if (PremisesRight == PremisesRightKind.None) problems.Add("Business needs a premises right: owned, leased, rented, shared, mobile, or another valid arrangement.");
            if (RequestedCapabilities == null || RequestedCapabilities.Count == 0) problems.Add("Business needs at least one capability to start with.");
            return problems;
        }

        /// <summary>
        /// Build the formation record for a valid form. The record
        /// starts at zero inventory and zero cash (see
        /// GenericBusinessFormation); template and capability choices
        /// configure it but grant nothing.
        /// </summary>
        public bool TryCreate(string businessId, out NewBusinessRecord record, out string reason)
        {
            record = null;
            reason = string.Empty;
            List<string> problems = Validate();
            if (problems.Count > 0)
            {
                reason = string.Join(" ", problems);
                return false;
            }
            GenericBusinessTemplate template = GenericBusinessTemplates.Find(TemplateId);
            AppliedTemplate applied = template != null ? GenericBusinessTemplates.Apply(template) : new AppliedTemplate();
            var set = new GenericBusinessCapabilitySet();
            foreach (BusinessCapabilityKind kind in RequestedCapabilities)
            {
                set.TryAddCapability(kind, out _);
            }
            if (template != null)
            {
                foreach (BusinessCapabilityKind kind in applied.CapabilitySet.EnabledKinds)
                {
                    set.TryAddCapability(kind, out _);
                }
                set.TryExpandRetailCatalog(applied.CapabilitySet.RetailProductIds, out _);
                foreach (string methodId in applied.CapabilitySet.EnabledProductionMethodIds)
                {
                    set.TryEnableProductionMethod(methodId, out _);
                }
                foreach (string serviceId in applied.CapabilitySet.EnabledServiceIds)
                {
                    set.TryEnableService(serviceId, out _);
                }
            }
            if (!GenericBusinessFormation.TryFormNewBusiness(businessId, BusinessDisplayName, null, out record, out reason))
            {
                return false;
            }
            record.Capabilities = set;
            return true;
        }
    }
}
