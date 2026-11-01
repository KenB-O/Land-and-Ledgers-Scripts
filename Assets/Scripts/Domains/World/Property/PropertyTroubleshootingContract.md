# Property Troubleshooting — Unity View Binding Contract

T3D (PL-47). This is the exact contract between the script-side troubleshooting
surface (`PropertyTroubleshootingService`, `ParcelDisputeReport`,
`IPropertyTroubleshootingView` in `Domains/World/Property/`) and the Unity UI
you build. Canon: Part II §2.1–2.5 (GHOST-CAN-002…007), §9.1 dispute lifecycle.

## Where it lives in the UI

A parcel/property inspector with a **"Check title"** (or similar) action. The
action raises `view.ParcelSelected`; the service analyzes; the view renders
`ShowReport(report)`. This surface is READ-ONLY — it never transfers title,
never resolves disputes by fiat, and never invents claimants. Pursuing an
avenue happens through the existing flows (title transfers, agreements, travel).

## What the view must show

| Element | Binds to |
|---|---|
| Parcel id + description | `report.ParcelId` |
| Status badge | `report.Status` — Clear / Uncertain / Disputed / AdverseClaim. **"Unknown or disputed is an acceptable state"** (Canon §2.1) — never render Uncertain as a bug. |
| Record holder | `report.CurrentHolderName` |
| Known facts | `report.KnownFacts` — bullet list, verbatim |
| Chain breaks | `report.ChainBreaks` — bullet list, verbatim. Empty + Clear status = healthy chain. |
| Adverse claims | `report.AdverseClaims` — claimant name, `Basis`, `BasisDocument`, day, note |
| Recommended avenues | `report.RecommendedAvenues` — each row: avenue name, `Why`, `Prerequisite`, `ExpectedCost`, and a **Pursue** button raising `view.AvenueChosen` |

## Status semantics the UI must respect

- **Clear**: unbroken chain, no adverse claims. Show the facts; offer sale/lease negotiation as ordinary options.
- **Uncertain**: chain breaks exist. Ownership is uncertain, NOT void. Lead with the record search avenue.
- **Disputed / AdverseClaim**: someone asserts a right. **Title itself is unchanged** (Canon §2.3 — recording a claim moves nothing). Show the claimant and basis; lead with contact/settlement.
- **Never** present "Vacant, therefore take it." Vacancy never establishes ownership (Canon §2.1 CANON LOCK). Unauthorized occupation never silently creates title (§2.2).

## Avenue semantics (what "Pursue" means per avenue)

| Avenue | Pursue routes to |
|---|---|
| RecordSearch | Travel to county records; clerk interaction. Produces evidence, not rulings. |
| ContactOwnerAgentExecutor | Correspondence/travel to the named party. |
| LeaseNegotiation / SaleNegotiation | The existing agreement + title-transfer flows. The holder may refuse, ask more, or offer a lease (Canon). |
| MortgageResolution | The T2A credit flows against the named instrument. |
| TaxSalePath | County tax proceedings (a future flow — show as "not yet available" if unwired, never fake it). |
| PublicLandEntry | **Only where lawful.** The UI must not offer this for private parcels. |
| ClaimChallenge | Formal proceeding. Consumes ordinary world time (Canon §2.3). |
| Settlement | Negotiation flow between the parties. |
| Withdrawal | Always available. Dismisses the pursuit, changes nothing. |

## Error handling

- `ShowError(message)` — parcel unknown, service unavailable, or analysis refused. Display verbatim; never fall back to a guessed status.
