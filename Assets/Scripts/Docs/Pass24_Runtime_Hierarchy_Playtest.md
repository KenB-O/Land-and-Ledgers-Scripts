# Pass XXIV — Main Scene Edit/Play Hierarchy Playtest

Date: 2026-10-30  
Unity: 6000.4.2f1, HDRP  
Scene: `Assets/Main Scene.unity`  
Scripts branch/HEAD: `integration/pass12-unity-readiness` / `4cbc01d3391d8e48aeb2e647eb5bcf54ec50d5b1`

## Procedure and evidence

Compared the saved Main Scene in Edit Mode with the same scene in Play Mode, then repeated Play Mode entry and exit to check for duplicate managers. Started the ordinary First Ledger entry screen and inspected the hierarchy and Console, then stopped Play Mode and confirmed the editor returned to the saved Main Scene state. The existing Pass24 pre-authoring checkpoint is `F:\Land & Ledgers_Pass24_Backups\20261030_runtime_hierarchy_checkpoint`; its original scene SHA-256 is `6EA25973943C396E475D6F3C7E2AD99733E6A4478246534759E06096C1C023CA`.

Before authoring, the scene serialized 30 GameObjects. It now serializes 176, with 146 additional authored GameObjects. The scene root layout remains intact; the persistent additions are grouped below `Game Systems` and the existing `UI Canvas`. The final scene SHA-256 is `4F6E35949ADECC0C053F48F8D29B23846450B1451E91C5E5C0C27B1D3EE10349`.

## Objects authored for Edit Mode inspection

The `Game Systems/Persistent Authorities` grouping contains the long-lived simulation, economy, civic, persistence, interaction and development managers that had previously appeared as Play Mode-created objects. The existing Civic Foundation remains on the existing Game Systems object, avoiding a second civic authority. An authored `PopulationAgentsRoot` is assigned to the existing population pathing director.

The existing Canvas now serializes the scenario start panel, business-formation overlay and its controls, seller-meeting view, ownership-workflow view, HUD control/status surfaces, and their controller references. The EventSystem and input module and the disabled property-preview camera are also scene-authored. These UI objects can be enabled/hidden as appropriate at runtime without constructing a fresh persistent interface tree.

## Play Mode comparison

The Main Scene hierarchy showed the authored manager and UI objects in their scene locations; the prior set of manager roots did not reappear as separate Play Mode scene roots. `DontDestroyOnLoad` contained the authored `MainMusicPlayer` and Unity's `[Debug Updater]`. The music object's lifetime transition is expected; `[Debug Updater]` is an Editor-generated object. The authored Scenario Validation Harness no longer appeared as a second persistent root.

Both Play Mode entries displayed the same First Ledger start card and authored hierarchy without a second manager set. Play Mode bootstrap populated runtime state (30 Persons and 12 Households in the selected compact playtest opening). Runtime-generated Persons, businesses, tasks, journeys, and world-generation representations remain owned by their existing scenario/simulation authorities; they are not scene-serialized instances. This pass makes the persistent architecture and UI inspectable before Play, while New Game continues to establish the scenario's changing simulation state.

The First Ledger start button received a visual hover response but did not advance the screen in this interaction session, so objective-HUD behavior after starting the scenario was not reverified here. This is recorded as an interaction limitation, not as proof that the start action is generally broken. The Sandbox/start, save/load, scene-reload and second-Play duplication cases remain untested.

## Console and limitations

Unity entered Play Mode after refreshing scripts with no C# compiler diagnostic. The Console showed repeated `Animator is not playing an AnimatorController` warnings (46 at the first observed point) and a `MainMusicPlayer` warning that its active audio source stopped unexpectedly and was being recovered. A later Console counter showed one error; the Editor log's relevant error text was a Unity Licensing Client handshake/token refresh error, with no gameplay exception or C# compiler error found. These environment/audio/animator warnings remain open for separate diagnosis.

No NUnit suite was run for this hierarchy-focused pass. The visible hierarchy inspection is actual Editor Play Mode evidence, not an automated test result. The scene and its `.meta` file are independently copied to the Pass24 `after_authoring` checkpoint.
