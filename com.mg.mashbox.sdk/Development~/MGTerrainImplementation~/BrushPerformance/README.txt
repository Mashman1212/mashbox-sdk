MG Terrain brush performance — 2026-10-08

Changes are in the MashBox SDK. No edits were made to the user's open scenes.

Warmed synthetic benchmark, Unity 6000.4.12f1, isolated batch project, 257 x 257 vertices per tile:
                                      Before     After
Regular world interior dab             21.012 ms   3.166 ms
Regular world border dab               52.081 ms  10.859 ms
Merge dab + render chunks + picking    19.260 ms   5.819 ms
Merge eight-dab drag + render + pick  115.053 ms  12.157 ms
Deform + retained-face geometry query 276.941 ms  14.059 ms

These measure the named operations, not full scene frame time. First-use setup and
mouse-up finalization are outside warmed dab timings. The allocation API returned
zero in this Unity runtime even for baseline allocations; the raw zero-byte output
must not be treated as a valid allocation measurement.

Implementation:
- Brush picking uses retained triangle trees, incrementally refitting only moved
  vertices' incident faces. No physics cooking during brush queries. Holes and
  topology changes invalidate the relevant cache; transformed ray distances work
  with nonuniform scale.
- World sculpting preserves welded perimeter normals for interior dabs and skips
  unnecessary seam work. Boundary dabs reuse the planar seam graph when its grid,
  transforms, membership and border coordinates still match. Stitched rims use
  the general solver. Heights and normals are read fresh on every pass.
- Merge samples inside a tile share one mesh upload/normal update per mouse event.
  Boundary samples retain sequential seam resolution. Reused readback buffers and
  per-dab dirty sets avoid copying every tile touched earlier in the stroke.
- Geometry-only render updates preserve UVs/colours and skip unchanged chunks.
- Live geometry/normals update on each dab; terrain tangent reconstruction finishes
  on mouse-up, alongside existing collider finalization. Normal-map tangent shading
  can therefore lag during an active drag, and is exact when the stroke ends.
- Existing public method signatures are retained via overloads.

Validation:
Runtime and SDK editor compile checks passed.
Sculpt preview safety validation passed: current-height picking, distance limits,
nonuniform scale, topology removal, UV/colour retention, welded border normals,
batched/sequential merge equivalence and final tangent accuracy.
Existing scene sculpt copy, merge, mixed-resolution seam, brush buffer and seam
stroke lifecycle suites passed, including source isolation, Undo/Redo, cancellation,
independent colliders, saved scene copies, save/reload, T-junctions and stitched rims.

Evidence: before-validation.log, after-validation.log, correctness.log.
Benchmark harness: MGTerrainBrushPerformanceValidation.cs (compile into the SDK
editor assembly only for an isolated batch run). Safety suite is installed at
Editor/Maps/Validation/MGTerrainSculptPreviewValidation.cs.
