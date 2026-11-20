# Outer Terrain Ground Variation — 2026-11-09

## What changed

Added a deterministic Edit Mode authoring command, `Land & Ledgers > Environment > Paint Natural Ground On Outer Tiles`, and used it to paint the eight existing terrain neighbors. The command reuses the project's existing rural terrain textures and creates outer-only copies of five TerrainLayer assets with modestly broader tiling to reduce obvious repetition. It distributes meadow, disturbed soil, damp lowland earth, exposed stone on slopes, and leaf litter around nearby authored beech canopy positions. World-coordinate noise keeps the variation stable across repeat runs; an 80 m blend uses the existing center terrain's painted palette at the town boundary.

This is a ground-material pass. It does not add or duplicate vegetation, change terrain height, alter terrain dimensions or collider coverage, change neighbor links, change scene objects, modify startup/profile settings, or expand the simulation/pathfinding bounds.

## Verification

- Unity Editor: Unity 6000.4.2f1, Main Scene open in Edit Mode.
- Paint command reported completion on all eight outer tiles; per-tile messages report 256² alphamaps and unchanged heightmap, collider, dimensions, and bounds.
- The subsequent Editor audit found all nine terrain objects. All are 1024 × 80 × 1024 m with 257 heightmaps and 256 alphamaps. All twelve shared edges were sampled at 65 points; reported mean and maximum height difference are 0 m. Neighbor references remain connected.
- The center terrain uses its original TerrainLayers. Outer tiles now use the five `Outer ...` copies at approximately 3.9–5.25 m tiling. The audited height ranges and scene placement are unchanged.
- Main Scene remains byte-for-byte identical to the pre-authoring safety copy: SHA256 `E758F945A5D584F7E087CE84B166591FD45A3603411D0A88B62FE66A9F289EA8`.
- The helper compiled and both Editor commands ran. No PlayMode or runtime terrain-rendering test was run. In the current Scene view the terrain still reads dark and fairly muted at a wide zoom; the asset edits and alpha painting are confirmed, but the final in-game appearance under daytime lighting still needs review.
- Unity Console showed 36 warnings and 2 errors at inspection. The visible errors were `ScriptableSingleton already exists` from Unity Package Manager UI (`ServicesContainer` / `PackageManagerProjectSettings`), not from the terrain authoring methods. Terrain paint and audit emitted informational logs. Existing warnings were not suppressed.

## Changed project files

- `Assets/EnvironmentDevelopment/Editor/RegionalTerrainAuditAndPaint.cs` and its `.meta`: reusable, bounded audit/painting commands.
- `Assets/EnvironmentDevelopment/TerrainLayers/OuterTiles/Outer Meadow Grass.terrainlayer` and `.meta`
- `Assets/EnvironmentDevelopment/TerrainLayers/OuterTiles/Outer Disturbed Soil.terrainlayer` and `.meta`
- `Assets/EnvironmentDevelopment/TerrainLayers/OuterTiles/Outer Grove Leaf Litter.terrainlayer` and `.meta`
- `Assets/EnvironmentDevelopment/TerrainLayers/OuterTiles/Outer Damp Lowland Earth.terrainlayer` and `.meta`
- `Assets/EnvironmentDevelopment/TerrainLayers/OuterTiles/Outer Exposed Stones.terrainlayer` and `.meta`
- The eight existing `Assets/EnvironmentDevelopment/RegionalTerrain3x3/RegionalTerrain_{E,N,NE,NW,S,SE,SW,W}.asset` TerrainData assets. Their `.meta` files were not changed.
- `Reports/Diagnostics/OUTER_TERRAIN_AUDIT_2026-11-09.md`: latest post-paint structural audit.

## Recovery copy

Before painting, a byte-for-byte checkpoint of Main Scene and all eight outer TerrainData assets, with their `.meta` files, was made at:

`C:\Users\kenne\Documents\LandLedgersTerrainCheckpoint_2026-11-09\Before-outer-ground-variation`

The checkpoint is outside the Unity project. The Main Scene hash above matches that copy exactly. The authored terrain asset changes remain unsaved to Git and unstaged; no commit or push was made.

## Next visual check

Inspect the outer tiles from the intended daytime game camera and compare close ground detail against the existing authored center. If the in-game palette remains too uniform or dark, tune the existing layer weights/material appearance based on that view. Do not add trees through this paint command or alter height/physics/bounds without a separate measured need.
