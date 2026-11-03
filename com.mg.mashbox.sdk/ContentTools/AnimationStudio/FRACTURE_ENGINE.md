# Procedural Fracture

Mesh Tools > Fracture is an explicit Compute operation on the active evaluated static mesh. Settings edits do not compute. Recompute uses the captured original; Execute/Apply commits one grouped StudioUndo action, and Cancel restores the original mesh and material bindings. Exploded inspection changes only the preview. Apply retains the pane for another operation; reopening resets declared defaults. Repeat Last Action finishes a ready preview, or reopens completed settings for another explicit Compute.

## Input and controls

The surface-solid kernel accepts closed, consistently outward-wound manifold geometry, including concave shapes and planar sections with cavity loops. It does not require a convex mesh or a tetrahedral volume grid. The exact `fighterJetCutTest.fbx` is a concave acceptance fixture. Open, branching, self-intersecting/degenerate sections, skinned or blend-shaped inputs fail explicitly. This is not a general repair/Boolean union of overlapping shells. No source assets or personal profiles are rewritten.

Piece count is 2–64, default 12. Seed defaults to 1, size variation .35, bias strength 0 with direction Y, roughness .015 mesh-local meters, detail .35 mesh-local meters, and interior UV scale 1 repeat per local meter. Weld tolerance defaults to .00001 mesh-local meters. Length fields display in workspace scene units; object scale retains mesh-local modeling semantics. Interior material is optional; exterior material slots remain in their numbered positions, with one appended interior slot.

**Piece Gap** defaults to zero and has a slider from zero to 2% of the source mesh's shortest bounding dimension. It slightly contracts each fragment uniformly about its own center, preserving topology and producing at least half the requested retreat on each side along the underlying cut-plane normal. Exterior geometry contracts slightly too. Thin fragments may limit usable clearance; invalid results are rejected. Gap is committed mesh geometry and therefore included in normal Export All/Export Selected. The separate Exploded inspection control (default .15) is preview-only. These mesh controls do not construct convex collider decompositions or configure physics contact offsets; convex hulls around concave fragments may still overlap.

Detail controls shared patch-boundary sampling and roughness wavelength. Structured quad grids are preferred; difficult sections use fixed-boundary triangle pairing, retaining triangles where a valid quad cannot be formed. Cavity boundaries stay open through the section rather than being filled as overlapping disks. The tool attempts clean quad patches; it does not promise all-quads or square cells everywhere. Extremely fine detail is rejected rather than silently reduced. Connected components are separated, so actual fragment count can exceed requested seed count.

## Geometry construction

1. Capture authored face provenance, exact render tessellation, geometric identities, material slots, normals, tangents, colors and all eight UV channels on Unity's main thread.
2. Weld nearby source boundary identities within Weld tolerance on copied arrays, retaining distinct render attributes. Validate closed oriented incidence. A triangle BVH supplies interior seed containment and surface clearance.
3. Generate deterministic interior seeds with clustered size variation and an anisotropic metric. Clip source surfaces against seed-cell bisectors in normalized double precision. Triangulate temporary sections with cavity-aware bridges; simplify collinear samples for triangulation and restore their boundary connectivity afterward.
4. Recombine exterior fragments by authored source face. Clean redundant compatible boundary samples before patch construction, conform shared edges, and propagate only required parity midpoints. Preserve warped source tessellation and source attribute provenance.
5. Build each shared interface once using the existing Bisect quad-grid builder or a fixed-boundary fallback, then reverse the same facets for its neighbor. Apply seeded geometric height variation to interior patch samples; the displacement tapers to zero at patch boundaries and near the exterior. Exterior crack boundaries currently remain piecewise planar; roughness is geometric inside the patches, not texture noise.
6. Validate shared interfaces, oriented closed edge incidence, finite faces and positive-volume components. Apply optional topology-preserving Piece Gap and validate again before publication.

At Gap zero the exterior shape is retained. Exterior attributes interpolate on the original source triangles, and authored exterior faces remain joined where possible. Interior render triangulation is separate from editable quad/triangle boundaries. No tetrahedral fragments or internal volume-grid edges are published. This does not replace the entire source with a quad retopology or guarantee arbitrary global self-intersection repair at extreme roughness.

## Ownership and performance

`StudioMeshFracture` separates main-thread capture/publication from array-only worker computation. Work reports stages and completed cell/patch counts. A temporary editor update callback observes completion and changed progress only while Compute runs; cancellation is checked during computation. Canceled workers retain arrays rather than Unity objects/documents, and obsolete workers cannot publish into a later session.

`StudioModelEditor.Fracture` uses the existing ToolChannelPane and StudioMeshToolPreview affected-mesh path. Inspection is one combined preview with authored polygons and preserved streams. There is no idle recomputation or permanent repaint callback. Native Mesh upload/final publication remains on the main thread.

`StudioModelDocument.Fracture` allocates outputs before mutation, restores the captured/shared source, and publishes independent owned meshes in one StudioUndo group. The source node and unrelated children remain; fragment pivots center on their bounds with geometry/child compensation. Frozen rotation/pivot metadata and parent transforms are retained. Shared instances keep the unfractured original.

Fracture consumes geometry at the end of strict modifier creation order. Broken upstream history rejects computation. Like Bisect separation, fragments start independent generated bases; Undo restores the original stack. There is no persistent cross-object replay graph or editable post-commit Fracture modifier.

## Validation

Excluded fixtures under `Tests~` are guarded to the reusable disposable SelectionUndoProject and leave it open. `StudioFractureValidation.cs` covers captured Compute/cancel/recompute, interfaces, rendering, commit and native Undo/Redo. `StudioFractureSurfaceValidation.cs` covers the cube topology budget, exact imported jet, Piece Gap retreat and split-seam welding. See Tests~/README.md and the host STUDIO_HANDOFF.md for actual latest results and remaining limits. No claim of exhaustive production-mesh acceptance, all settings/materials or physics simulation follows from these focused checks.
