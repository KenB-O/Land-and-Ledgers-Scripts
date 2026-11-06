# Nov 6 Unity Test Results

## Compilation

Unity 6000.4.2f1 completed script compilation on the integrated branch after four targeted source fixes. The successful Editor log reports `Tundra build success`; no C# compile errors remained. The post-compile Console checkpoint showed 35 warnings, primarily pre-existing obsolete API and unused-variable warnings. Runtime PlayMode capture separately recorded 0 errors/exceptions.

The source-only Roslyn harness figures (2,240 pass / 18 known failures on Muse HEAD) are not Unity Test Runner outcomes and are not counted as Unity tests here.

## Unity Test Runner

- Executed: 0
- Passed: 0
- Failed: 0
- Not run: all requested focused Unity tests, including the new bakery/freight suites and `ManAnimController_HasDrivingWagonSeatedState`, `FreightDispatchActivationIntegrationTests`, `GeneralStoreItemOfferIdentityTests`, `TransportFoundationTests`.

The Editor Test Runner utility was present in accessibility, but positioned outside the visible Editor bounds; its controls returned coordinates outside the window. The project does not have the `com.unity.pipeline` Editor bridge needed by the available Unity CLI test interface. No package was installed, no competing Unity Editor process was started, and no full suite was launched. Test source compilation succeeded after adding the missing `UnityEditor.Animations` using, but this is not execution evidence.

## PlayMode is not a test-runner result

One manual fresh First Ledger NewGame was run at 100x for approximately 2m18, reaching day 226. Its runtime evidence is recorded in `NOV_06_PLAYMODE_ANALYSIS.md` and full output is in `NOV_06_PLAYMODE_CONSOLE.log`. It is not counted as a Unity automated test.

## Outstanding targeted execution

Run the user-listed focused tests after the Test Runner is visible or via a supported in-process Unity test interface. Prioritize `CoupledTransportProgressTests`, `WagonRiderPlacementTests`, `FreightStagingCellSelectorTests`, `BusinessTaskStartWindowTests`, `BakeryWholesaleDailyDriverTests`, `BakeryFoodChainEndToEndTests`, `StoreWeeklyReorderCustodyTests`, `HouseholdWholePackagePurchaseTests`, `FreightDispatchActivationIntegrationTests`, and `TransportFoundationTests`. Broaden only if a focused result demonstrates a regression.