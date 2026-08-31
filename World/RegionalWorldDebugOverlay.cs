using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    public sealed class RegionalWorldDebugOverlay : MonoBehaviour
    {
        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool syncLayerVisibilityFromTownSettings = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F7;
        [Tooltip("When the project uses the Input System package without the legacy input manager, this keeps the F7 overlay toggle from throwing at runtime.")]
        [SerializeField] private bool useInputSystemToggle = true;
        [SerializeField] private bool logInputToggleCompatibilityWarnings;
        [SerializeField] private float yOffset = 0.35f;
        [SerializeField] private bool showOpeningTerrainBuildRadius = true;
        [SerializeField] private bool showTerrainTiles = true;
        [SerializeField] private bool showRouteReadyTileMarkers = true;
        [SerializeField] private bool showWatercourses = true;
        [SerializeField] private bool showRouteCorridors = true;
        [SerializeField] private bool showRouteConstraintMarkers = true;
        [SerializeField] private bool showVegetationZones = true;
        [SerializeField] private bool showSurveyParcels = true;
        [SerializeField] private bool showParcelReadinessMarkers = true;
        [SerializeField] private bool showRemoteSuitabilityMarkers = true;
        [SerializeField] private bool showAnchor = true;
        [SerializeField] private bool showSettlements = true;
        [SerializeField] private bool showSettlementRiskMarkers = true;
        [SerializeField] private bool showSettlementSuccessionMarkers = true;
        [SerializeField] private bool showFoundationValidationMarkers = true;
        [SerializeField] private bool showRegionalOpportunityMarkers = true;
        [SerializeField] private Color terrainTileColor = new(0.42f, 0.48f, 0.52f, 0.36f);
        [SerializeField] private Color routeReadyTileColor = new(0.95f, 0.62f, 0.22f, 0.74f);
        [SerializeField] private Color parcelColor = new(0.95f, 0.78f, 0.28f, 0.42f);
        [SerializeField] private Color waterColor = new(0.15f, 0.42f, 0.82f, 0.82f);
        [SerializeField] private Color routeCorridorColor = new(0.82f, 0.62f, 0.28f, 0.86f);
        [SerializeField] private Color routeConstraintColor = new(0.95f, 0.36f, 0.12f, 0.90f);
        [SerializeField] private Color anchorColor = new(0.95f, 0.86f, 0.38f, 1f);
        [SerializeField] private Color vegetationColor = new(0.20f, 0.58f, 0.24f, 0.28f);
        [SerializeField] private Color settlementColor = new(0.95f, 0.52f, 0.22f, 1f);
        [SerializeField] private Color readinessColor = new(0.35f, 0.82f, 0.45f, 0.72f);
        [SerializeField] private Color burdenColor = new(0.88f, 0.28f, 0.18f, 0.72f);
        [SerializeField] private Color remoteSuitabilityColor = new(0.72f, 0.48f, 0.95f, 0.82f);
        [SerializeField] private Color validationWarningColor = new(1f, 0.72f, 0.12f, 0.92f);
        [SerializeField] private Color validationErrorColor = new(1f, 0.12f, 0.08f, 1f);
        [SerializeField] private Color regionalOpportunityColor = new(0.32f, 0.95f, 0.80f, 0.95f);
        [SerializeField] private Color openingTerrainBuildRadiusColor = new(0.72f, 0.88f, 1f, 0.42f);

        private bool inputCompatibilityWarningLogged;
        private string lastRuntimeSummary = "Regional debug overlay has not drawn yet.";

        public string LastRuntimeSummary => lastRuntimeSummary ?? string.Empty;

        [ContextMenu("Log Regional Debug Overlay Runtime Summary")]
        public void LogRuntimeSummary()
        {
            Debug.Log(BuildRuntimeDebugOverlaySummary(), this);
        }

        public string BuildRuntimeDebugOverlaySummary()
        {
            RegionalWorldState state = ResolveState();
            ApplyTownGenerationDebugSettings();
            RegionalFoundationInspectionReport report = state != null && (showFoundationValidationMarkers || showRegionalOpportunityMarkers)
                ? state.BuildInspectionReport()
                : null;
            lastRuntimeSummary = BuildRuntimeDebugOverlaySummary(state, report);
            return lastRuntimeSummary;
        }

        public bool ShowOverlay
        {
            get => showOverlay;
            set => showOverlay = value;
        }

        private void Update()
        {
            if (toggleKey != KeyCode.None && TryConsumeTogglePress())
            {
                showOverlay = !showOverlay;
            }
        }

        private bool TryConsumeTogglePress()
        {
#if ENABLE_INPUT_SYSTEM
            if (useInputSystemToggle && TryResolveInputSystemKey(toggleKey, out Key inputSystemKey))
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard[inputSystemKey].wasPressedThisFrame)
                {
                    return true;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(toggleKey);
#else
            if (logInputToggleCompatibilityWarnings && !inputCompatibilityWarningLogged)
            {
                Debug.LogWarning("[RegionalWorldDebugOverlay] Toggle key input is disabled because the legacy Input Manager is unavailable and no matching Input System key was resolved. The overlay can still be toggled through the inspector.", this);
                inputCompatibilityWarningLogged = true;
            }

            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static bool TryResolveInputSystemKey(KeyCode keyCode, out Key key)
        {
            key = Key.None;
            switch (keyCode)
            {
                case KeyCode.F1: key = Key.F1; return true;
                case KeyCode.F2: key = Key.F2; return true;
                case KeyCode.F3: key = Key.F3; return true;
                case KeyCode.F4: key = Key.F4; return true;
                case KeyCode.F5: key = Key.F5; return true;
                case KeyCode.F6: key = Key.F6; return true;
                case KeyCode.F7: key = Key.F7; return true;
                case KeyCode.F8: key = Key.F8; return true;
                case KeyCode.F9: key = Key.F9; return true;
                case KeyCode.F10: key = Key.F10; return true;
                case KeyCode.F11: key = Key.F11; return true;
                case KeyCode.F12: key = Key.F12; return true;
                case KeyCode.A: key = Key.A; return true;
                case KeyCode.B: key = Key.B; return true;
                case KeyCode.C: key = Key.C; return true;
                case KeyCode.D: key = Key.D; return true;
                case KeyCode.E: key = Key.E; return true;
                case KeyCode.F: key = Key.F; return true;
                case KeyCode.G: key = Key.G; return true;
                case KeyCode.H: key = Key.H; return true;
                case KeyCode.I: key = Key.I; return true;
                case KeyCode.J: key = Key.J; return true;
                case KeyCode.K: key = Key.K; return true;
                case KeyCode.L: key = Key.L; return true;
                case KeyCode.M: key = Key.M; return true;
                case KeyCode.N: key = Key.N; return true;
                case KeyCode.O: key = Key.O; return true;
                case KeyCode.P: key = Key.P; return true;
                case KeyCode.Q: key = Key.Q; return true;
                case KeyCode.R: key = Key.R; return true;
                case KeyCode.S: key = Key.S; return true;
                case KeyCode.T: key = Key.T; return true;
                case KeyCode.U: key = Key.U; return true;
                case KeyCode.V: key = Key.V; return true;
                case KeyCode.W: key = Key.W; return true;
                case KeyCode.X: key = Key.X; return true;
                case KeyCode.Y: key = Key.Y; return true;
                case KeyCode.Z: key = Key.Z; return true;
                case KeyCode.Alpha0: key = Key.Digit0; return true;
                case KeyCode.Alpha1: key = Key.Digit1; return true;
                case KeyCode.Alpha2: key = Key.Digit2; return true;
                case KeyCode.Alpha3: key = Key.Digit3; return true;
                case KeyCode.Alpha4: key = Key.Digit4; return true;
                case KeyCode.Alpha5: key = Key.Digit5; return true;
                case KeyCode.Alpha6: key = Key.Digit6; return true;
                case KeyCode.Alpha7: key = Key.Digit7; return true;
                case KeyCode.Alpha8: key = Key.Digit8; return true;
                case KeyCode.Alpha9: key = Key.Digit9; return true;
                case KeyCode.Space: key = Key.Space; return true;
                case KeyCode.Escape: key = Key.Escape; return true;
                default: return false;
            }
        }
#endif


        private void OnDrawGizmos()
        {
            RegionalWorldState state = ResolveState();
            ApplyTownGenerationDebugSettings();

            RegionalFoundationInspectionReport sharedReport = state != null && (showFoundationValidationMarkers || showRegionalOpportunityMarkers)
                ? state.BuildInspectionReport()
                : null;
            lastRuntimeSummary = BuildRuntimeDebugOverlaySummary(state, sharedReport);

            if (!showOverlay || state == null)
            {
                return;
            }

            if (showTerrainTiles)
            {
                DrawTerrainTiles(state);
            }

            if (showWatercourses)
            {
                DrawWatercourses(state);
            }

            if (showRouteCorridors)
            {
                DrawRouteCorridors(state);
            }

            if (showVegetationZones)
            {
                DrawVegetationZones(state);
            }

            if (showSurveyParcels)
            {
                DrawParcels(state);
            }

            if (showParcelReadinessMarkers || showRemoteSuitabilityMarkers)
            {
                DrawParcelDiagnostics(state);
            }

            if (showAnchor)
            {
                DrawAnchor(state);
            }

            if (showOpeningTerrainBuildRadius)
            {
                DrawOpeningTerrainBuildRadius(state);
            }

            if (showSettlements)
            {
                DrawSettlements(state);
            }

            if (showFoundationValidationMarkers)
            {
                DrawFoundationValidationMarkers(state, sharedReport);
            }

            if (showRegionalOpportunityMarkers)
            {
                DrawRegionalOpportunityMarkers(state, sharedReport);
            }
        }

        private RegionalWorldState ResolveState()
        {
            if (townWorld == null)
            {
                townWorld = FindAnyObjectByType<TownWorldController>();
            }

            return townWorld != null ? townWorld.RegionalWorld : null;
        }

        public void ApplyTownGenerationDebugSettings(TownGenerationSettings sourceSettings)
        {
            if (sourceSettings == null || !sourceSettings.syncRegionalDebugOverlaySettings)
            {
                return;
            }

            showTerrainTiles = sourceSettings.showRegionalTerrainTiles;
            showRouteReadyTileMarkers = sourceSettings.showRegionalRouteReadyTileMarkers;
            showWatercourses = sourceSettings.showRegionalWatercourses;
            showRouteCorridors = sourceSettings.showRegionalRouteCorridors;
            showRouteConstraintMarkers = sourceSettings.showRegionalRouteConstraintMarkers;
            showVegetationZones = sourceSettings.showRegionalVegetationZones;
            showSurveyParcels = sourceSettings.showRegionalSurveyParcels;
            showParcelReadinessMarkers = sourceSettings.showRegionalParcelReadinessMarkers;
            showRemoteSuitabilityMarkers = sourceSettings.showRegionalRemoteSuitabilityMarkers;
            showAnchor = sourceSettings.showRegionalAnchor;
            showSettlements = sourceSettings.showRegionalSettlements;
            showSettlementRiskMarkers = sourceSettings.showRegionalSettlementRiskMarkers;
            showSettlementSuccessionMarkers = sourceSettings.showRegionalSettlementSuccessionMarkers;
            showFoundationValidationMarkers = sourceSettings.showRegionalFoundationValidationMarkers;
            showRegionalOpportunityMarkers = sourceSettings.showRegionalOpportunityMarkers;
        }

        private void ApplyTownGenerationDebugSettings()
        {
            if (!syncLayerVisibilityFromTownSettings || townWorld == null)
            {
                return;
            }

            ApplyTownGenerationDebugSettings(townWorld.Settings);
        }

        private string BuildRuntimeDebugOverlaySummary(RegionalWorldState state, RegionalFoundationInspectionReport report)
        {
            string visibility = showOverlay ? "visible" : "hidden";
            string sync = syncLayerVisibilityFromTownSettings ? "settings sync on" : "settings sync off";
            string settingsRead = townWorld != null && townWorld.Settings != null
                ? townWorld.Settings.BuildRegionalDebugSurfaceSummary()
                : "no TownGenerationSettings resolved";
            string stateRead = state != null
                ? $"state available: tiles {state.TerrainTiles.Count}, water {state.Watercourses.Count}, routes {state.RouteCorridors.Count}, parcels {state.SurveyParcels.Count}, vegetation {state.VegetationZones.Count}, settlements {state.Settlements.Count}"
                : "state missing";
            string reportRead = report != null
                ? report.BuildCompactReadout()
                : "inspection report not requested";
            return $"Regional debug overlay: {visibility}; {sync}; enabled synced surfaces {CountEnabledSurfaces()}/15; opening radius {(showOpeningTerrainBuildRadius ? "shown" : "hidden")}; {stateRead}; {settingsRead}; {reportRead}.";
        }

        private int CountEnabledSurfaces()
        {
            int count = 0;
            if (showTerrainTiles) count++;
            if (showRouteReadyTileMarkers) count++;
            if (showWatercourses) count++;
            if (showRouteCorridors) count++;
            if (showRouteConstraintMarkers) count++;
            if (showVegetationZones) count++;
            if (showSurveyParcels) count++;
            if (showParcelReadinessMarkers) count++;
            if (showRemoteSuitabilityMarkers) count++;
            if (showAnchor) count++;
            if (showSettlements) count++;
            if (showSettlementRiskMarkers) count++;
            if (showSettlementSuccessionMarkers) count++;
            if (showFoundationValidationMarkers) count++;
            if (showRegionalOpportunityMarkers) count++;
            return count;
        }

        private void DrawTerrainTiles(RegionalWorldState state)
        {
            for (int i = 0; i < state.TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = state.TerrainTiles[i];
                if (tile == null)
                {
                    continue;
                }

                Color color = terrainTileColor;
                float readiness = Mathf.Max(tile.ActivationReadiness01, tile.RouteActivationReadiness01);
                color.a = Mathf.Lerp(0.16f, 0.46f, readiness);
                DrawRectWire(tile.BoundsMeters, yOffset, color);

                if (showRouteReadyTileMarkers && tile.RouteInfluence01 > 0.12f)
                {
                    Color markerColor = routeReadyTileColor;
                    markerColor.a = Mathf.Lerp(0.28f, 0.84f, tile.RouteActivationReadiness01);
                    Gizmos.color = markerColor;
                    float size = Mathf.Lerp(18f, 54f, Mathf.Max(tile.RouteInfluence01, tile.RouteActivationReadiness01));
                    Gizmos.DrawWireCube(ToWorld(tile.BoundsMeters.center, yOffset + 0.18f), new Vector3(size, 0.1f, size));
                }
            }
        }

        private void DrawWatercourses(RegionalWorldState state)
        {
            for (int i = 0; i < state.Watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = state.Watercourses[i];
                if (water == null)
                {
                    continue;
                }

                Color color = waterColor;
                color.a = Mathf.Lerp(0.48f, 0.92f, water.FlowStrength01);
                Gizmos.color = color;
                for (int p = 1; p < water.Points.Count; p++)
                {
                    Gizmos.DrawLine(ToWorld(water.Points[p - 1], yOffset + 0.1f), ToWorld(water.Points[p], yOffset + 0.1f));
                }
            }
        }

        private void DrawRouteCorridors(RegionalWorldState state)
        {
            for (int i = 0; i < state.RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                if (route == null || route.Points.Count < 2)
                {
                    continue;
                }

                Color color = route.Kind switch
                {
                    RegionalRouteCorridorKind.OpeningFootholdSpine => anchorColor,
                    RegionalRouteCorridorKind.RemoteWorksiteTrack => remoteSuitabilityColor,
                    RegionalRouteCorridorKind.FreightTrack => settlementColor,
                    RegionalRouteCorridorKind.CreekOrDrawTrack => waterColor,
                    _ => routeCorridorColor
                };
                color.a = Mathf.Lerp(0.36f, 0.88f, route.Practicality01);
                Gizmos.color = color;
                for (int p = 1; p < route.Points.Count; p++)
                {
                    Gizmos.DrawLine(ToWorld(route.Points[p - 1], yOffset + 0.24f), ToWorld(route.Points[p], yOffset + 0.24f));
                }

                if (route.FreightBurden01 >= 0.52f)
                {
                    Vector2 midpoint = route.Points[route.Points.Count / 2];
                    Gizmos.color = burdenColor;
                    Gizmos.DrawWireSphere(ToWorld(midpoint, yOffset + 0.36f), Mathf.Lerp(14f, 42f, route.FreightBurden01));
                }

                if (showRouteConstraintMarkers && route.PrimaryConstraintKind != RegionalRouteConstraintKind.None && route.PrimaryConstraintKind != RegionalRouteConstraintKind.StableDryTrack)
                {
                    Vector2 midpoint = route.Points[route.Points.Count / 2];
                    Color constraintColor = routeConstraintColor;
                    constraintColor.a = Mathf.Lerp(0.42f, 0.94f, Mathf.Max(route.MudSeasonRisk01, route.BridgeOrFordNeed01, route.GradeBurden01));
                    Gizmos.color = constraintColor;
                    float radius = Mathf.Lerp(18f, 52f, Mathf.Max(route.MudSeasonRisk01, route.BridgeOrFordNeed01, route.GradeBurden01));
                    Vector3 center = ToWorld(midpoint, yOffset + 0.48f);
                    Gizmos.DrawWireSphere(center, radius);
                    Gizmos.DrawLine(center + Vector3.left * radius, center + Vector3.right * radius);
                }
            }
        }

        private void DrawVegetationZones(RegionalWorldState state)
        {
            for (int i = 0; i < state.VegetationZones.Count; i++)
            {
                RegionalVegetationZoneRecord zone = state.VegetationZones[i];
                if (zone == null || zone.Density01 < 0.48f)
                {
                    continue;
                }

                Color color = vegetationColor;
                color.a = Mathf.Lerp(0.12f, 0.34f, zone.Density01);
                DrawRectWire(zone.BoundsMeters, yOffset + 0.05f, color);
            }
        }

        private void DrawParcels(RegionalWorldState state)
        {
            for (int i = 0; i < state.SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                if (parcel == null)
                {
                    continue;
                }

                Color color = parcel.ParcelKind switch
                {
                    RegionalParcelKind.TownPlatCore => anchorColor,
                    RegionalParcelKind.TownPlatEdge => settlementColor,
                    RegionalParcelKind.RoughParcel => new Color(0.65f, 0.42f, 0.28f, 0.5f),
                    RegionalParcelKind.FloodplainTract => waterColor,
                    _ => parcelColor
                };
                color.a = Mathf.Lerp(0.24f, 0.58f, parcel.DevelopmentReadiness01);
                DrawRectWire(parcel.BoundsMeters, yOffset + 0.15f, color);
            }
        }

        private void DrawParcelDiagnostics(RegionalWorldState state)
        {
            for (int i = 0; i < state.SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                if (parcel == null)
                {
                    continue;
                }

                Vector3 center = ToWorld(parcel.CenterMeters, yOffset + 0.32f);
                if (showParcelReadinessMarkers)
                {
                    Color markerColor = Color.Lerp(burdenColor, readinessColor, parcel.DevelopmentReadiness01);
                    markerColor.a = 0.78f;
                    Gizmos.color = markerColor;
                    float size = Mathf.Lerp(8f, 22f, parcel.DevelopmentReadiness01);
                    Gizmos.DrawWireCube(center, new Vector3(size, 0.1f, size));
                }

                if (showRemoteSuitabilityMarkers && parcel.RemoteSuitability != RegionalRemoteSuitability.None)
                {
                    Gizmos.color = remoteSuitabilityColor;
                    float radius = parcel.RemoteSuitability == RegionalRemoteSuitability.MineralProspect ? 24f : 16f;
                    Gizmos.DrawWireSphere(center + Vector3.up * 0.18f, radius);
                }
            }
        }

        private void DrawAnchor(RegionalWorldState state)
        {
            Gizmos.color = anchorColor;
            Vector3 center = ToWorld(state.AnchorTown.CenterMeters, yOffset + 0.55f);
            Gizmos.DrawSphere(center, 26f);
            Gizmos.DrawWireSphere(center, 120f);
        }

        private void DrawOpeningTerrainBuildRadius(RegionalWorldState state)
        {
            RegionalTerrainTileView terrainView = FindAnyObjectByType<RegionalTerrainTileView>();
            if (terrainView == null || !terrainView.BuildOnlyTilesNearOpeningAnchor)
            {
                return;
            }

            Color color = openingTerrainBuildRadiusColor;
            color.a = Mathf.Clamp01(color.a);
            Gizmos.color = color;
            DrawFlatCircle(state.OpeningTownLocalOriginMeters, yOffset + 0.92f, terrainView.OpeningAnchorTileRadiusMeters, 72);
        }

        private void DrawFlatCircle(Vector2 centerMeters, float y, float radiusMeters, int segments)
        {
            int safeSegments = Mathf.Max(8, segments);
            Vector3 previous = ToWorld(centerMeters + new Vector2(radiusMeters, 0f), y);
            for (int i = 1; i <= safeSegments; i++)
            {
                float angle = i / (float)safeSegments * Mathf.PI * 2f;
                Vector2 point = centerMeters + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusMeters;
                Vector3 current = ToWorld(point, y);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }

        private void DrawSettlements(RegionalWorldState state)
        {
            for (int i = 0; i < state.Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = state.Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                float radius = Mathf.Lerp(60f, 190f, Mathf.Clamp01(settlement.Population / 250f));
                Color color = Color.Lerp(burdenColor, settlementColor, settlement.Permanence01);
                color.a = Mathf.Lerp(0.50f, 1f, settlement.LocalConfidence01);
                Gizmos.color = color;
                Gizmos.DrawWireSphere(ToWorld(settlement.CenterMeters, yOffset + 0.4f), radius);

                if (showSettlementRiskMarkers && settlement.DeclineRisk01 >= 0.36f)
                {
                    Gizmos.color = burdenColor;
                    Gizmos.DrawWireSphere(ToWorld(settlement.CenterMeters, yOffset + 0.48f), Mathf.Lerp(28f, 90f, settlement.DeclineRisk01));
                }

                if (showSettlementSuccessionMarkers && settlement.SuccessionRole is RegionalSettlementSuccessionRole.PeerChallenger or RegionalSettlementSuccessionRole.LikelyRegionalSuccessor)
                {
                    Color challengeColor = settlement.SuccessionRole == RegionalSettlementSuccessionRole.LikelyRegionalSuccessor ? anchorColor : remoteSuitabilityColor;
                    challengeColor.a = Mathf.Lerp(0.58f, 1f, settlement.SuccessionReadiness01);
                    Gizmos.color = challengeColor;
                    Vector3 center = ToWorld(settlement.CenterMeters, yOffset + 0.62f);
                    float size = Mathf.Lerp(42f, 110f, Mathf.Max(settlement.FootholdChallenge01, settlement.SuccessionReadiness01));
                    Gizmos.DrawWireCube(center, new Vector3(size, 0.1f, size));
                }
            }
        }

        private void DrawFoundationValidationMarkers(RegionalWorldState state, RegionalFoundationInspectionReport report)
        {
            report ??= state.BuildInspectionReport();
            for (int i = 0; i < report.Issues.Count; i++)
            {
                RegionalFoundationIssueRecord issue = report.Issues[i];
                if (issue == null || !issue.HasLocation || issue.Severity == RegionalFoundationIssueSeverity.Info)
                {
                    continue;
                }

                Gizmos.color = issue.Severity == RegionalFoundationIssueSeverity.Error ? validationErrorColor : validationWarningColor;
                float radius = issue.Severity == RegionalFoundationIssueSeverity.Error ? 46f : 30f;
                Vector3 center = ToWorld(issue.LocationMeters, yOffset + 0.72f);
                Gizmos.DrawWireSphere(center, radius);
                Gizmos.DrawLine(center + Vector3.left * radius, center + Vector3.right * radius);
                Gizmos.DrawLine(center + Vector3.forward * radius, center + Vector3.back * radius);
            }
        }

        private void DrawRegionalOpportunityMarkers(RegionalWorldState state, RegionalFoundationInspectionReport report)
        {
            report ??= state.BuildInspectionReport();
            for (int i = 0; i < report.OpportunityReadouts.Count; i++)
            {
                RegionalFoundationOpportunityRecord opportunity = report.OpportunityReadouts[i];
                if (opportunity == null || !opportunity.HasLocation)
                {
                    continue;
                }

                Color color = regionalOpportunityColor;
                color.a = Mathf.Lerp(0.45f, 1f, opportunity.Score01);
                Gizmos.color = color;
                float radius = Mathf.Lerp(18f, 64f, opportunity.Score01);
                Vector3 center = ToWorld(opportunity.LocationMeters, yOffset + 0.86f);
                Gizmos.DrawWireSphere(center, radius);
                Gizmos.DrawWireCube(center, new Vector3(radius * 0.9f, 0.1f, radius * 0.9f));
            }
        }

        private void DrawRectWire(Rect rect, float y, Color color)
        {
            Gizmos.color = color;
            Vector3 a = ToWorld(new Vector2(rect.xMin, rect.yMin), y);
            Vector3 b = ToWorld(new Vector2(rect.xMax, rect.yMin), y);
            Vector3 c = ToWorld(new Vector2(rect.xMax, rect.yMax), y);
            Vector3 d = ToWorld(new Vector2(rect.xMin, rect.yMax), y);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }

        public Vector3 PreviewSceneWorldForRegionalPoint(Vector2 regionalPoint, float y = 0f)
        {
            return ToWorld(regionalPoint, y);
        }

        private Vector3 ToWorld(Vector2 point, float y)
        {
            if (townWorld == null)
            {
                townWorld = FindAnyObjectByType<TownWorldController>();
            }

            return townWorld != null
                ? townWorld.RegionalPointToSceneWorld(point, y)
                : new Vector3(point.x, y, point.y);
        }
    }
}
