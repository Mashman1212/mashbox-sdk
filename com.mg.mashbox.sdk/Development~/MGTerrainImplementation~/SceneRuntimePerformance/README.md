# MG Terrain editor and runtime performance

Date: 2026-09-28. Project: D:/MappyX. Scene: Assets/Maps/Cotswold Ridge/[00] SCENES/COTSWOLD RIDGE [MATT] [V02].unity.
Unity 6000.4.12f1, HDRP 17.4, Direct3D 11, NVIDIA GeForce RTX 5090.

## Results

All times are milliseconds. Lower is better. Each sample contains 180 measured render intervals. P95 uses the nearest-rank definition.

| Test | Mean interval | P95 | Worst interval | Worst detail-cell CPU build |
| --- | ---: | ---: | ---: | ---: |
| Scene view, before | 11.620 | 19.168 | 26.785 | 15.415 |
| Scene view, after | 10.274 | 11.428 | 12.146 | 0.403 |
| Play Mode camera sweep, before | 7.054 | 21.318 | 70.578 | 61.277 |
| Play Mode camera sweep, after | 3.829 | 4.482 | 12.478 | 0.289 |
| Play Mode grass-level sanity check, after | 4.380 | 5.598 | 12.869 | 0.121 |

The original-pose Play Mode sweep measured 1.84x throughput after the fixes. Its largest observed render interval fell by 82%. Scene-view worst interval fell by 55%; mean improved by 12%. These are controlled editor measurements, not a guarantee of doubled FPS in a shipped player.

The grass-level check averaged 46,010 visible grass instances, peaked at 49,584, and ended at 47,251. It had no GPU-generation or BRG fallback on any of the 12 active tiles. There is no matching before measurement for this separate viewpoint.

## What changed

- Enabled the existing GPU procedural generation and BatchRendererGroup residency path for supported Scene views. Appearance captures retain their separate path. Unsupported configurations retain fallback behavior.
- Kept editor loading bounded and preserved stable camera selection. Projection changes now invalidate the cached streaming selection.
- Runtime can reuse the existing readable R16 density-map occupancy scan to skip empty cells.
- Cache pruning selects eviction victims once, retains geometry bounds and density occupancy, and removes references to disposed cells. Previously each eviction searched the whole cache, and pruning discarded all candidate metadata.
- Disabled only Batching Static on the 12 active MG Terrain source objects in the saved scene. Other static flags remain unchanged. Unity static batching was replacing local terrain grid meshes with combined world-space meshes. This broke terrain placement and forced several tiles into expensive CPU detail generation in Play Mode.
- Added a scene build processor ordered before Unity static batching. It removes Batching Static from MG Terrain source objects on processed scene copies, including inactive objects, while preserving other flags and unrelated renderers. MG Terrain retains its own surface/detail batching.

Authored quality remains: grass density 0.175, detail distance 250 m, visible-instance budget 100,000, existing density LODs, grass shadows enabled, 8 cell builds / 2 uploads per world scheduling frame, and cache 5,120. The other inactive terrain world was not retuned.

## Measurement scope

The scene-view sweep warmed for 30 frames and then translated forward up to 35 units while rotating through a +/-40-degree yaw. The same saved camera pose was used for the original-pose Play Mode comparison: position (583.98, 457.09, 1797.36), Euler (21.45, 206.37, 359.53). Play Mode warmed for 300 frames, rendered into a 1280x720 HDR target using a temporary camera copied from the Scene view, and measured 180 end-camera-render intervals. Original game cameras were temporarily disabled and restored. Temporary camera/target objects were removed and Play Mode exited.

The before runtime render was spatially incorrect because of static batching. Correcting that also changes the visible population: the original-pose sweep averaged 40,808 instances before versus 2,085 after. The screenshots document the incorrect overhead terrain band and misplaced grass before. Thus the runtime result combines a correctness fix, removal of CPU fallback, and lower erroneous rendering work; it is not a same-instance-count GPU benchmark. No authored density/distance was reduced. The separate grass-level test confirms dense grass still renders with the corrected pipeline.

Scene-view warmup/population timing also differs with GPU streaming (mean 3,871 versus 3,263 instances; both peak 6,147 and finish at 720). These short sweeps establish the removed CPU stalls, but do not establish long-session memory stability or all-map frame rates. Recorder markers measure CPU work, not GPU duration. End-camera-render intervals are not presentation timing. No standalone executable was built or benchmarked.

## Validation

- Unity compiled the source and ran the optimized editor and Play Mode checks.
- New Streaming Cache Performance validation passed: working-set protection, oldest-first eviction, retained bounds/occupancy, disposed-reference cleanup, streaming restart, Scene camera layer masks, terrain batching exclusion, preserved other static flags, unrelated renderers unchanged, and idempotent processing.
- Existing localized painting validation passed all five scenarios; results are included.
- Existing Gameplay Camera Selection validation reached its later focus assertion but failed because its reflection lookup for MGGrassInteractionMap.FocusPosition returned null. That pre-existing validator was not repaired or counted as passing.
- A pre-existing missing-script warning remains in the scene. No new errors appeared in the final Play Mode test.
- The saved scene was checked directly: all 12 targeted objects changed static flags from 2147483647 to 2147483643, clearing only bit 4 (Batching Static).

The build processor is covered by direct scene validation. The authored scene flags were additionally corrected and verified in Play Mode. Standalone build integration has not been exercised.

## Artifacts and rollback

CSV files, settings/mesh snapshots and runtime screenshots accompany this report. Use baseline-motion versus optimized-editor1 for editor comparison and baseline-runtime2 versus optimized-runtime3 for runtime comparison. optimized-grass-level is a separate sanity check.

The temporary MGTerrainScenePerformanceProbe.cs and its .meta were moved out of Editor compilation into this Development~ folder after testing; it no longer polls or renders. It is retained only to document/reproduce the measurement methodology. The permanent regression validation is at Editor/Maps/Validation/MGTerrainStreamingCacheValidation.cs.

The four original runtime sources are included as *.cs.original. scene-before-save.unity preserves the previous disk version of the scene; scene-batching-changes.txt lists exact modified object IDs and original flags. Prefer reverting those flags or selected source changes over restoring a whole scene if subsequent authoring has occurred. No Git commit was made.

Unity references used for processor ordering and processed-scene semantics:
- https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/build/iprocessscenewithreport/onprocessscene
- https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/6000.4/Editor/Mono/PostprocessScene.cs
