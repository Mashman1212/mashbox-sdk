Performance validation fixtures require `MASHBOX_STUDIO_TESTS` in the disposable test build. Normal Studio builds exclude their counters and diagnostic switches. Never add this symbol to a user project.

# Isolated Unity validation

These fixtures are excluded from SDK compilation. Never invoke their Run methods in a working game project: they exit the editor after testing.

They were run in Unity 6000.4.12f1 using an isolated project at `D:/ProjectX - U6/AnimationStudioValidation`. Copy the Animation Studio Editor folder and these two fixtures into `Assets/Studio/Editor` in that project. Copy the project's own licensed Final IK dependency there, and copy `Skeleton.fbx` and `SM_Body_01.fbx` (with their importer metadata) into `Assets/Character`.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.AnimationStudio.StudioValidation.Run -logFile <log-path>` for rig and baking validation. In a second run use `MashBoxSDK.AnimationStudio.StudioPlayValidation.Run` for Play Mode persistence. Use a fresh project or remove only the generated validation take/clip assets before rerunning; these fixture paths are fixed.

Passed: SM body remapping; no cloned gameplay scripts; Final IK initialization and target reach; repeatable evaluation; pinned feet under hip motion; Generic positions and rotations on a Generic Animator; Humanoid recognition, muscle/body curves and hand position playback; stepped key timing; Play Mode creation/solving/baking and persistence across returning to Edit Mode.

The full game project also compiled the SDK assembly successfully. Visual inspection identified and corrected the preview scene's camera culling mask and inherited scene overlays.

Outline validation now uses an actual SceneView in Unity 6000.4.12f1 with HDRP 17.4.0. In an isolated project, import the Studio editor assembly/sources and validation fixtures, copy the source project HDRP settings and Linear Player Settings, place its pipeline asset at Assets/Settings/HDRenderPipelineAsset.asset, and copy a saved workspace to Assets/WorkspaceSource.txt. Put final-native in Assets/Variant.txt and run StudioHdrpOutlineValidation.Run. It exports scene/overlay composites at two angles, checks stationary-frame differences, then runs the geometry, FPS, menu, controls, hover, and workflow regressions before exiting. StudioEdgeRenderingValidation now checks outline geometry and viewing-ray offsets; Camera.Render alone did not validate the HDRP SceneView overlay path.

StudioWorkspaceStorageValidation.Run is a two-launch fixture for a fresh, isolated Unity project. Include the Studio editor assembly and preview shaders. The first launch writes project-root .workspace files and checks independent Save As, legacy conversion and rejection of Assets paths. Restart the editor and run it again to verify meshes, blend shapes, channels, material references, subsequent import/create and creation Undo/Redo. Run only in a disposable project: it creates named test assets and workspaces.


## Bevel validation

`StudioBevelValidation.Run` exhaustively checks every nonempty cube-edge selection, then exercises profile subdivisions, convex/linear/concave tensions, both grid and n-gon cap styles, and normalized widths. Checks cover winding, triangle degeneracy, geometric seam closure, Euler characteristic, normals, selection and retained polygon topology.

`StudioBevelIntegrationValidation.Run` covers concave polygon prisms, higher-valence octahedron corners, interior edges ending on open boundaries, mesh scale, UV0â€“UV7 dimensions, colors, material seams, rejected inputs without mutation, the Bevel menu/ToolChannelPane, shared meshes, Undo/Redo and workspace persistence.

`StudioBevelLimitsValidation.Run` checks the 32-subdivision maximum at an eight-valence junction.

`StudioBevelLargeValidation.Run` constructs a disconnected 512-cube mesh and bevels all 6,144 geometric edges. It checks 32-bit output indices and attribute buffers and reports operation time. `StudioBevelVisualValidation.Run` renders original, convex and concave examples for inspection.

Compile these fixtures with the Studio Editor sources **only in a disposable Unity project**, with the same dependencies as the editor assembly. Run one entry point per process with `-batchmode -projectPath <isolated-project> -executeMethod <class>.Run -logFile <log-file>`. Each fixture exits Unity when finished. Results and the optional visual preview are written in the disposable project's root; the integration workspace stays in its `MashBoxStudio/Workspaces` folder. Do not run these fixtures in a working game project.


`StudioPolygonAuthoringValidation.Run` checks explicit primitive face types, triangle/quad imports, preservation of real coplanar triangle edges, base-vertex offsets, quad loop traversal, actual triangle cuts, warped quads, editor face/edge mapping and mixed triangle/quad/n-gon workspace persistence. N-gon cap checks reject center vertices, radial rings and extra modeling faces. This fixture is also for an isolated disposable editor only.


`StudioSelectionToolbarPaneValidation.Run` exercises native selection toolbar placement, radio-state synchronization from mouse/keyboard, automatic toolbar visibility inventory (including a test-only future overlay), hide/restore, Mesh Tools menu placement, shared Apply/Execute behavior, native Execute mouse clicks, focus persistence, invalid/failed commands, real bevel operations and Undo/Redo. It exports a toolbar screenshot and a result file. Compile only with isolated disposable test sources; its test-only overlay must not be included in production.


`StudioBevelJunctionValidation.Run` tests one, two and three edges meeting at a cube corner with both Grid/Uniform and Cap/N-gon, zero through 32 subdivisions, and convex/linear/concave tension. It checks exact patch polygon counts, the single central polygon needed by odd profile counts, connected profile strips, manifold seams, winding, render degeneracy and normals. `StudioBevelGridVisualValidation.Run` renders a close-up Studio wireframe corner and the restored junction-style field. Both are excluded test fixtures for a disposable Unity project only.


`StudioIsolationOwnedPaneValidation.Run` is a Windows-only disposable-editor check for hierarchy isolation/restoration, selected groups, switching targets, hidden ancestors/descendants, visibility Undo/Redo, native Alt-clicks and popup ownership/Z-order after focusing Studio. It exits Unity and must not run in a working project.


The bevel junction fixture additionally asserts that one-edge terminations contain no separate cap polygon and have exactly the original six cube faces plus the profile strips. This checks actual polygon topology, not merely hidden line rendering.


`StudioLiveToolValidation.Run` checks reusable live transactions for bevel, extrusion, soften and merge; repeated edits versus one source operation; shared meshes and untouched scene objects; invalid parameters; Apply/Execute/Close; one-step Undo/Redo and external Undo; clean-state cancellation and zero-width application; retained three/four-component UV0 data; and workspace lifecycle cleanup.

`StudioLiveToolInputValidation.Run` opens actual Studio and ToolChannelPane windows in the disposable editor, scrubs a native numeric field, and checks geometry publication before MouseUp, cancellation, callback cleanup and native GUI errors. `StudioLiveToolPerformanceValidation.Run` measures the initial build and twenty distance-only previews for a 16,384-face extrusion, including normals/UVs/mesh publication/cache invalidation but excluding rendering. Timings are observations, not hardware-independent performance thresholds. Run these only using the isolated-project instructions above; each fixture exits Unity.


`StudioLiveSurfaceValidation.Run` compares actual Camera-rendered pixels for the persistent live renderer against the evaluated working mesh. It primes the original renderer first, then checks bevel preview, subsequent parameter edits, commit, cancel, Undo/Redo and extrusion topology/distance updates. This catches stale GPU geometry that CPU topology/vertex assertions cannot detect. The test also confirms there is only one renderer per edited object. Run only in a disposable editor project as described above.
