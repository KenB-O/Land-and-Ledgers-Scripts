# Campaign 10 Issue Ledger

| Priority | Finding | Root cause / evidence | Fix or status | Verification |
|---|---|---|---|---|
| High | Site-free freight was assigned a building; owner appeared unemployed. | Opening roster ignored `premisesRequired`, and owner-operator mirroring skipped businesses with `AssignedBuildingId < 0`. Small startup test asserted Ethan (person 7) has no workplace and an owner-operator role. | Respect the authored premises flag; reconcile owner identity even without dedicated premises. | Small startup PlayMode test passed; Ethan remains at workplace `-1` with an owner-operator profession. |
| Medium | Startup fixture asserted ten buildings despite runtime profile disabling Town Hall. | `TryApplyDevelopmentResidentialHomeCount` explicitly disables Town Hall for the authored five-home/four-storefront Small scenario; runtime produced exactly nine. | Corrected the fixture expectation and explanation to nine. | Small startup PlayMode test passed; runtime log listed five houses and four commercial buildings. |
| Medium | Business-formation test reflection became ambiguous after a new method overload. | `GetMethod("BuildHouseholdTradingPort")` matched multiple overloads. | Made the test select the zero-argument overload explicitly. | Test advanced past reflection and reached a separate stock assertion. |
| High / unresolved | Unformed-store test reads 24 `staple_food` units where it expects zero. | `ProductionBusinessAuthorityFormsTheScenarioThreeBusinessRoute` fails at the pre-formation `CategoryStockUnits` assertion. The source/test contract and runtime fallback state need reconciliation; no item purchase was demonstrated. | Investigate which runtime authority owns those 24 units before changing stock behavior. | Reproduced in a focused PlayMode run; not repaired. |
| Low / unresolved | Development validation emits six warnings. | Startup log reports one reserved land offer versus target two, three practical vacant plots not on market, and four buildings without authored exterior prop groups. | Classify against authored intent; no changes made. | Observed in Unity startup validation; not independently tested. |

## Run record

- Eleven focused PlayMode test invocations, about 132 seconds of reported runner time in total. The Small startup fixture passed after its corrections. The business-formation test remains blocked by the 24-unit fallback-stock mismatch.
- One live Play Mode startup check ran for roughly 12 seconds; Unity reported advancing frames and returned to Edit Mode.
- No multi-day economic run, freight shipment, production cycle, construction flow, or Medium/Large stress test was performed in this campaign window.
