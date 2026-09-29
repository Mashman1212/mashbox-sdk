# MG Terrain distance LOD implementation

Date: 2026-09-28. Project D:/MappyX, scene COTSWOLD RIDGE [MATT] [V02].
Unity 6000.4.12f1 / HDRP 17.4 / Direct3D 11 / RTX 5090.

## Result

Terrain surfaces now have up to three cached geometry LODs, available in both the Scene view and runtime. The original editable MeshFilter and collision meshes are never replaced. Grass generation still reads the original grid.

Defaults: enabled, 256 m minimum distance from tile bounds, 1.5 pixels projected source-mesh error. Entry uses 10% additional distance and a 20% tighter error threshold to limit switching near a threshold. Camera projection changes are evaluated immediately. Controls are on MG Terrain World > Settings, with standalone controls under the tile inspector's Advanced > Surface Rendering section.

In this scene, flat outer tiles reduce from 32,768 triangles to 736 (97.8% fewer). Several stitched outer tiles reduce from 33,152 to 3,826 while retaining their original stitched edges. Detailed blocks that cannot meet the error or attribute tolerance remain at full resolution.

Four central tiles use shader-driven distant-surface deformation. These intentionally retain the original render geometry: the source mesh alone does not describe their rendered mountain shape. Other arbitrary X/Z-deformed meshes also retain their original geometry. This is not a replacement for displacement-aware LOD baking or a universal mesh decimator.

## Camera comparison

Same saved camera position (851.14, 956.16, 2251.41), Euler angles (37.26, 221.13, 359.35), full scene, 1280x720 HDR offscreen Game camera in Play Mode. Each final run warmed for 2,400 frames and measured 180 frames of forward translation and +/-40-degree yaw.

| Metric | LOD disabled | LOD enabled |
| --- | ---: | ---: |
| Mean render interval | 2.397 ms | 2.362 ms |
| P95 render interval | 2.933 ms | 2.785 ms |
| Worst interval | 3.950 ms | 4.530 ms |
| Mean selected terrain triangles | 394,752 | 251,496 |
| Lowest selected terrain triangles | 394,752 | 203,916 |
| LOD mesh-upload marker during measurement | 0 ms | 0 ms |

The selected terrain geometry fell by 36.3% on average, reaching a 48.3% reduction during this sweep. The triangle count sums the selected representation for all twelve active terrain tiles, before per-chunk/frustum culling; it is not a GPU triangle-submission counter. Grass was outside its drawing range at this high viewpoint. The separate grass-level check averaged 16,449 visible grass instances (peak 18,565), 3.761 ms render intervals, a 5.784 ms maximum, and no LOD upload work during its measured sweep. All 12 tiles reported no GPU-generation or BRG fallback. It uses a different ground-level camera pose and has no paired baseline; the screenshot and measurements are included.

Frame time was effectively similar on this GPU in this short test; these measurements do not demonstrate a substantial FPS increase. They establish reduced geometry and no LOD uploads during the warmed camera sweep. End-camera-render intervals are not presentation timings or GPU timestamps. A standalone player has not been built or benchmarked.

The initial short-warmup run is also retained. It included cache preparation and measured a 1.891 ms peak mesh-upload marker; it is not a steady-state comparison. A planar fast path was subsequently added to accelerate preparation. Original meshes remain visible while caches are prepared.

## Implementation

- Runtime/Maps/Terrain/MGTerrainSurfaceLodBuilder.cs contains pure array processing on a background worker. Candidate blocks use strides 4, 8 and 16, with conservative local height-error budgets 0.125, 0.5 and 2 metres. Levels without at least 5% further triangle reduction are discarded.
- Regular source grid topology is checked before simplification. Holes, non-grid triangles, inconsistent winding and material boundaries protect the affected blocks. Fine edges are retained wherever a simplified block borders an unsimplified block or the tile boundary. Adjacent simplified blocks share matching edges. Retaining original tile boundaries lets independently selected LOD levels meet without new cracks.
- Geometry error checks include intersections between the source triangles and candidate planes; planar regions use a cheaper conservative test. Boundary fans are covered by an additional error allowance. Vertex paint and all eight UV channels have conservative interpolation guards. Retained vertices copy the source normals, tangents, colors and UVs.
- Runtime/Maps/Terrain/MGTerrain.SurfaceLod.cs schedules one background builder across terrains. Snapshots and uploads are throttled across tiles. Meshes are generated after load/edit invalidation, never in a camera callback. Changing camera position only selects an existing mesh.
- The render proxy shares the source transform/material state. Near rendering continues using the existing cullable surface chunks; distant LOD rendering uses one proxy per tile. Hidden near chunks are not repeatedly synchronized during distant rendering. Returning nearby refreshes their state, including materials changed while far away.
- Sculpt/topology/paint notifications invalidate obsolete LODs. Workers are canceled and meshes disposed on rebuild/disable. The editor queue continues preparation while the Scene view is idle and stops processing completed entries. Runtime preparation uses LateUpdate.
- Appearance captures and non-Game/non-Scene cameras retain original geometry. Selection/picking continues using the editable source. Material shader displacement remains separate from the source-mesh error estimate; the known distant terrain morph is explicitly excluded.

## Validation

Permanent menu: Tools > MashBox > MG Terrain > Validation > Surface LOD.

Passed: flat and curved grids; reduction; matching boundary-edge sets; manifold topology and winding; original source immutability; hole preservation; multiple submeshes; arbitrary X/Z fallback; isolated vertex paint; far/near selection; cached mesh identity across camera motion; collider/source identity; material propagation from distant proxy back to near chunks; appearance-capture bypass; field-of-view changes; shader morph guard; edit invalidation.

The existing Streaming Cache Performance and Terrain Tile Geometry validations also passed. Unity compiled the implementation and ran the final Play Mode comparisons without new errors. The scene's existing missing-script warning remains.

## Files and recovery

This directory contains validation output, CSV measurements, settings snapshots, screenshots and source backups (*.before). scene-before-lod-save.unity preserves the saved scene before these settings were written. Revert selected source changes/settings rather than restoring an entire old scene after additional authoring.

Both temporary probe scripts and their .meta files are archived here outside Unity compilation after testing. They are retained for reproducibility and do not keep polling the editor. No Git commit was made.

## Pixel-error inspector feedback
Added a read-only Surface LOD - Scene Camera panel under world Settings. It lists the selected mesh, triangle count, and reason for each tile. It uses the same selection function as rendering, without changing render state. Explicit Scene camera selection prevents another Game/capture camera from making the inspector misleading. Settings changes explicitly repaint Scene views.

Pixel error does not force new mesh generation. Zero-height-error tiles can select their coarsest cache at every slider value; nearby and shader-deformed tiles remain original. The editing grid is drawn from source geometry. Current scene evidence is in pixel-error-scene-snapshot.txt. The expanded validation passes all 11 checks, including a real curved-grid cache changing LOD between .25 and 8 pixel error and a flat grid remaining unchanged; diagnostic queries match rendering and do not mutate it.
