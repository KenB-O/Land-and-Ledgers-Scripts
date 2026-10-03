using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// BIZ-1 (addendum): presenter for the Create Business form (GHOST-DES-029).
    /// Owns the form lifecycle: subscribes to the view's Submitted/Cancelled events,
    /// validates the <see cref="CreateBusinessFormModel"/>, converts it to a
    /// <see cref="CreateBusinessIntent"/>, and runs the canonical
    /// <see cref="BusinessCreationAuthority"/> workflow. The view stays dumb by design.
    /// </summary>
    public sealed class CreateBusinessPresenter : IDisposable
    {
        private readonly ICreateBusinessView view;
        private readonly BusinessCreationAuthority authority;
        private readonly IBusinessCreationContext context;
        private bool disposed;

        /// <summary>Raised when a business is successfully created (for UI refresh, etc.).</summary>
        public event Action<BusinessCreationResult> BusinessCreated;

        public CreateBusinessPresenter(
            ICreateBusinessView view,
            IBusinessCreationContext context,
            BusinessCreationAuthority authority = null)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.authority = authority ?? new BusinessCreationAuthority();

            view.Submitted += OnSubmitted;
            view.Cancelled += OnCancelled;
        }

        private void OnSubmitted()
        {
            if (disposed)
            {
                return;
            }

            CreateBusinessFormModel form = view.Form;
            if (form == null)
            {
                view.ShowErrors(new[] { "The form has no data — this is a UI bug, not a player error." });
                return;
            }

            // Step 1: form validation — human-readable errors, canon premises rules.
            List<string> errors = form.Validate();
            if (errors.Count > 0)
            {
                view.ShowErrors(errors);
                return;
            }

            // Step 2: canonical workflow. GHOST-DES-029: creating the entity does NOT
            // make it operating — that still needs capability and commerce.
            CreateBusinessIntent intent = form.ToIntent();
            if (!authority.TryCreate(intent, context, out BusinessCreationResult result) || result == null)
            {
                view.ShowWorkflowFailure(result?.Diagnostics ?? new List<string> { "Creation failed." });
                return;
            }

            if (!result.Success)
            {
                view.ShowWorkflowFailure(result.Diagnostics);
                return;
            }

            view.ShowSuccess(result);
            BusinessCreated?.Invoke(result);
            view.Close();
        }

        private void OnCancelled()
        {
            if (!disposed)
            {
                view.Close();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            view.Submitted -= OnSubmitted;
            view.Cancelled -= OnCancelled;
        }
    }
}
