# Create Business Form — Unity View Binding Contract

BIZ-1 addendum. This is the exact contract between the script-side form
(`CreateBusinessFormModel`, `CreateBusinessPresenter`, `ICreateBusinessView` in
`Domains/Economy/Creation/`) and the Unity UI you build. Canon: GHOST-DES-029
(Create Business UI), GHOST-DES-031 (the Businesses → Create Business → Choose
Business Type → Premises flow), GHOST-DES-035 (ownership structure).

## Where it lives in the UI

A **Business** tab/screen with a **"Create New Business"** button. The button opens
this form. (GHOST-DES-029: the player gets a general Create Business action.)

## Controls and bindings

| Control | Type | Binds to |
|---|---|---|
| Business type picker | Dropdown | `view.BusinessTypeOptions` → writes `form.SetBusinessType(...)`. All 19 `BusinessType` values must be present. |
| Business name | Text field | `form.SetDisplayName(...)` |
| Premises section | Radio/dropdown group | `form.Premises.SetMode(...)` — one of: No premises required / Use owned property / Lease property / Acquire property / Use existing compatible space / Mobile route |
| Compatible space kind | Dropdown (visible when "Use existing compatible space") | `form.Premises.SetCompatibleSpaceKind(...)` — House room / Barn space / Yard area / Shared premises |
| Property picker | Dropdown (visible for owned/leased/acquired) | `view.OwnedPropertyOptions` → writes `form.Premises.SetSelectedPropertyId(...)`. **Must list ALL owned parcels, including ones already used by another business** (GHOST-DES-031). Flag occupied ones via `PropertyOption.AlreadyOccupied` / `OccupiedBy`. |
| Functional space assignment | Text field (visible when the picked property is already occupied) | `form.Premises.SetFunctionalSpaceAssignment(...)` — e.g. "north rooms, 2nd floor" or "east yard". Required when occupied; the form rejects submit without it. |
| Lease/purchase price | Numeric field (visible for lease/acquire) | `form.Premises.SetLeaseOrPurchasePriceCents(...)` |
| Ownership rows | Repeating row group | `form.AddOwnerRow(...)` / `form.ClearOwnerRows()`. Each row: owner picker, ownership % (must total 100%), capital contribution, profit share %, debt exposure % (GHOST-DES-035: the ledger records whole obligations; this is the internal split), managing-authority checkbox (at least one required). |
| Capabilities | Multi-select list | `view.CapabilityOptions` → `form.AddCapability(id)` / `form.RemoveCapability(id)` |
| Working capital | Numeric field | `form.SetWorkingCapitalCents(...)` |
| Create button | Button | raises `view.Submitted` |
| Cancel button | Button | raises `view.Cancelled` |

## Presenter callbacks (what the view must implement)

- `ShowErrors(errors)` — display the human-readable validation errors next to the
  form. The player fixes input and resubmits; nothing is created.
- `ShowSuccess(result)` — creation worked. Show the new business name, its HF-1
  entity id (`result.BusinessEntityId`), and premises kind. **Do not present it as
  "open" or "operating"** — GHOST-DES-029: the entity still needs capability and
  commerce before it operates.
- `ShowWorkflowFailure(diagnostics)` — validation passed but the world refused
  (e.g. no suitable site). Show the diagnostics.
- `Close()` — dismiss the form.

## Validation rules the form enforces (so you know what errors players will see)

Capability-driven premises (Tech X §3.2) — the premises choice must satisfy the
merged requirements of the selected capabilities:

- "No premises required" is rejected when the work needs any site, naming the
  unmet needs (e.g. *"No-premises won't work here: customer-facing space,
  food-handling space. Pick a premises option that fits the work."*).
- "Mobile route" is rejected for fixed-site work.
- Slaughter capability needs yard space (a house room or open yard won't do —
  the error says so in plain language).
- Animal housing needs barn space; customer-facing work can't live in a barn or
  open yard; food handling needs enclosed space.
- Picking an already-occupied property without assigning functional space is
  rejected (GHOST-DES-031: assign rooms/floor/yard, don't block the parcel).

## Wiring it up (Unity side)

1. Implement `ICreateBusinessView` on your form MonoBehaviour.
2. Feed the dropdown sources: business types (all 19), owned properties (all of
   them, occupied included), capabilities.
3. Create the presenter: `new CreateBusinessPresenter(view, creationContext)` where
   `creationContext` is the `IBusinessCreationContext` implementation (the runtime
   manager provides this — same pattern as the other authorities).
4. `Dispose()` the presenter when the form is destroyed.

## What's NOT in the form (deliberately)

- No "Open Business" button — there is no such action (Canon §3.2, GHOST-DES-009).
  Operating status comes from actual commerce only.
- "Add Operation / Capability" and "Add Separate Business Here" (GHOST-DES-033)
  are separate actions inside an existing business — BIZ-2.
