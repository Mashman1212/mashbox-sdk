# MG Terrain grass interaction

## Setup

1. Enter Play Mode: each enabled MG Terrain World automatically adds an MG Grass Interaction Map if one is missing. This also works for worlds loaded or enabled later and with domain/scene reload disabled. Existing attached maps retain their settings and enabled state. For edit-time authoring, use **Tools > MashBox > MG Terrain > Interaction > Add Map to Selected World**.
2. The window follows the camera tagged **MainCamera**; interaction pauses if none is available. New maps default to resolution **512**, world size **256 m**, update rate **60 Hz**, recovery **30 s**, maximum bend **82.9 degrees**, compression **0.797**, height tolerance **1.83 m**, edge fade **2 m**, and **Preview In Edit Mode** enabled. The paint shader loads automatically from Resources.
3. Select the wheel, foot, character, or prop objects and use **Add Brush to Selected Objects**. Assign a collider or use the manual world-metre radius. All enabled brushes automatically register with the global active map (`MGGrassInteractionMap.Active`), regardless of which scene loads first. No map reference is stored on the player/interactor. Unloading a world releases its map; a replacement world picks up the registered brushes automatically and starts fresh strokes. `MGGrassInteractor.InteractionMap` exposes the currently active map read-only.
4. Play. For authoring, attach a map before Play Mode and move a brush with **Preview In Edit Mode** enabled. Inspector preview shows pressure. **Clear Interaction Map** resets marks and stroke history.

God Grass V2 has a dedicated `MGGrassInteractionBend` Custom Function node between wind and distance sizing. Interaction remains active with wind strength zero. Root Height and Bend Height reuse the material's existing wind stem settings. Maximum Bend Angle, Compression, Recovery Seconds and Height Tolerance are on the map controller. The graph's current stylized normal/wind-normal treatment is retained; this stage deforms positions, not the shading normal/tangent.

For another HDRP graph, use File-mode Custom Functions from `Packages/com.mg.mashbox.sdk/Runtime/Maps/Terrain/Interaction/MGGrassInteraction.hlsl`, Float precision:

- `MGGrassInteractionSample`: AbsolutePositionWS (Vector3) -> Direction (Vector2), Strength (Float).
- `MGGrassInteractionBend`: PositionOS (Vector3, wind-deformed), RestPositionOS (Vector3, undeformed), RootHeight (Float), BendHeight (Float), Weight (Float, 1) -> BentPositionOS (Vector3).

Sample at an undeformed root, in **absolute** world coordinates. Connect bend before distance size/offset. Do not put map globals on the material blackboard: material properties would override scene values. This is a reusable HLSL interface rather than a generated subgraph asset.

## Performance and world tiles

The map is aligned to absolute world XZ, independent of terrain bounds, tile indices and per-tile materials. A stroke crossing any tile seam uses the same field. It also works with ordinary instanced grass meshes. No Terrain.activeTerrain, texture CPU copies, ReadPixels, Apply, physics-overlap scans, or per-tile material instances are used at runtime.

Default: 512 x 512 over 64 m, 12.5 cm/texel, two linear RGBAHalf textures = 4 MiB excluding driver overhead. At up to 30 Hz, a moving window uses one combined full-map scroll/recovery pass (262,144 texels/update). A stationary window recovers only the conservative rectangle containing retained stamps, in place; each brush dispatch covers its clipped swept rectangle. Grass outside that rectangle skips the interaction texture sample. The rectangle includes a bilinear-filter margin and all brushes, shifts with the window, and resets on clear/reallocation. It intentionally remains conservative after trails recover; widely distributed tracks can expand it to the full window. There is no full-map blur. Actual GPU time must be profiled on target hardware; texture dimensions alone are not an FPS measurement. Doubling resolution costs 4x map memory and full-map work; enlarging world size costs spatial precision. For tire detail try 512 over 32 m (6.25 cm/texel) before increasing resolution.

Edit Mode preview ticks the map directly at the configured update rate rather than queuing the entire player loop; the Inspector preview refreshes at 5 Hz. Scene View repaints still render the scene and are not a standalone runtime FPS benchmark. The fixed update cadence limits dispatch cost. Swept capsules bridge linear movement between samples. Fast curved trajectories can cut corners at low update rates. Teleports over the brush threshold restart the stroke. Brushes use maximum pressure, not per-frame additive accumulation. Recovery is linear in elapsed seconds. Map origins move by whole texels with exact overlap copies; exposed texels clear, and the boundary fades smoothly. Runtime changes to resolution/size reallocate and clear. Disabling releases resources and resets globals. Compute and random-write format support are checked; unsupported devices leave grass undeformed.

Use the Unity Profiler sample **MG Grass Interaction**, GPU timings, and the inspector stamp/texel counters. Test the busiest expected collider count, crossing four-tile corners, driving quickly, pausing, teleporting, disabling the map, and a player moving while wind is zero. Material root planes and Bend Height must match the grass mesh. Grass needs enough vertical geometry to show curvature.

## Scope and limitations

- One active moving window for the local player/view, shared across all scene tiles. Remote actors outside it do not paint. Leaving the window discards history; returning does not restore trails. For simultaneous distant players or persistent trails, extend to a bounded texture-array page cache with explicit eviction rather than making one giant world texture.
- Collider footprints are approximate XZ circles using the larger bounds extent, not exact mesh/box silhouettes. Use several small wheel/foot brushes for precise footprints. Offsets are local; manual Radius is world metres. Collider radius is derived from its scaled world bounds.
- Contact height is the collider bottom, or the brush transform plus offset. Optional grounding uses a downward ray; exclude the actor layer from Ground Layers so it cannot hit itself. Height filtering fades roots outside the tolerance, so choose that tolerance for the mesh height and slopes.
- One contact-height layer per XZ texel; overlapping bridges/caves cannot retain two independent tracks. Heights and directions are strength-premultiplied so bilinear filtering near untouched areas remains valid.
- Normal/tangent deformation, persistent terrain/density painting, floating-origin rebasing, network replication and multi-camera windows are not implemented. Call ClearMap after rebasing the world.

## Earlier MapleMappie code

Reviewed `Assets/TerrainRendering/GrassMaskPainter.cs`, `GrassPainterPlayer.cs`, `GrassMaskPainter.compute` and the old grass graph. The previous painter already used GPU-local brush dispatches, with a full-float mask storing pressure/direction. This implementation keeps that useful approach, adding bounded moving coverage, lifecycle cleanup, explicit recovery, world-metre swept brushes, off-map rejection and independent God Grass bending. Original project files are unchanged.

Unity compute texture binding/reference: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/computeshader/settexture
