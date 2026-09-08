# Optional GPU detail culling

In MG Terrain's Inspector, under GPU Resident Renderer, enable GPU Procedural Generation, Use Indirect Draws and Keep All Cells GPU Resident. Three independent switches are available:

- **GPU Per-Mesh Frustum Culling** tests each generated mesh instance against all six camera planes after the CPU cell selection.
- **GPU Terrain Occlusion** rejects mesh instances wholly hidden behind this terrain's hills or banks. It does not use rocks, buildings, other terrains, or lofts as occluders.

All default off. Compare each switch individually and together, following the same camera path and allowing any initial cache rebuild to finish. No full-map performance gain is claimed by the synthetic correctness tests. Occlusion adds GPU work and can be slower in open views. The Inspector reports Active or Off / bypassed. CPU-selected population counters describe the input, not GPU survivors.

The implementation uses a conservative minimum-height hierarchy created from the terrain grid on first use. It is not a scene bake or a raycast scan. Current-camera cone sections are compared to the minimum terrain height across the entire section. Uncertain cases remain visible. No previous-frame depth is used, and stationary unchanged views reuse the result. The height hierarchy is destroyed with the detail buffers and rebuilt after terrain/cache invalidation.

Mesh bounds use the actual generated transform (including scale, rotation and prefab-part transforms). **Culling Bounds Padding (m)** adds a world-space margin, default 1 m, for wind and shader displacement. Set it to at least the maximum displacement outside the mesh bounds. The GPU toggles do not expand CPU cell bounds or increase the input selection. GPU filtering only removes instances from the existing cell selection; it does not extend the baseline visibility of large meshes beyond their selected cells. Padding applies to GPU mesh tests.

Camera-only compacted index/argument buffers leave the existing shadow buffers intact. Secondary cameras and HTrace custom views use the original selection. XR, orthographic views, direct draws and streaming/non-resident paths bypass this optional pass. Terrain occlusion bypasses hidden surfaces, unreadable or unsupported grids and unsupported texture sizes. Visible mesh triangles determine which grid quads are fully covered; a missing triangle makes that quad non-occluding. Hole data no longer disables the whole terrain. The Terrain Occlusion status line reports activation or a bypass reason. Terrain obstruction assumes an opaque, solid heightfield with no shader displacement changing its occluding silhouette; use frustum-only for other surfaces.

Profiler marker: `MGTerrain.GPUCulling` measures CPU setup/dispatch, not elapsed GPU execution. There is no synchronous readback in normal rendering. The Inspector's **Measure GPU Culling** button requests a one-off asynchronous count of input and surviving mesh instances. It does not count cells. Automated GPU readback is also used by `MashBox/Validation/Validate Terrain GPU Occlusion` to check survivor identities, tall/foreground meshes, toggle restoration, padding and unchanged shadow arguments. Residency validation checks that all eight GPU-toggle combinations preserve baseline cell bounds, including layers containing large scaled meshes.


## Rendered scene depth (HDRP)

**GPU Rendered Depth Occlusion (HDRP)** uses opaque scene geometry, including cubes, hills, rocks, buildings and lofts. It requires HDRP 17+ with Custom Pass enabled in the gameplay camera frame settings. The shared SDK includes the optional HDRP adapter; no scene component or occlusion bake is required. The Inspector reports whether the pass is active.

A dedicated native-material depth pass runs before normal rendering, excluding MG detail batches to prevent self-occlusion. Its farthest-depth hierarchy tests the whole padded mesh bounding box. Alpha-clipped surfaces retain their material depth behavior; transparent surfaces are not occluders. Custom materials must supply a working DepthOnly or DepthForwardOnly pass. Near-plane intersections, screen-edge uncertainty and uncovered depth remain visible. Current-frame depth includes moving objects and camera movement without a previous-frame occlusion lag. Shadow draws stay unchanged.

The pass, depth textures and GPU filtering add work. This is an opt-in experiment, not a guaranteed FPS improvement: benchmark **MGTerrain.RenderedDepth.Occluders**, **MGTerrain.RenderedDepth.Pyramid**, and **MGTerrain.RenderedDepth.Cull** alongside total frame time on the same camera path. All three switches off bypass optional culling and remove the depth pass. The cell coloring continues to show CPU selection, so use the GPU snapshot button to compare survivors.

Validation covers forward/reversed depth, empty depth restoration, terrain holes and unchanged shadow arguments. An isolated HDRP test also checks a real opaque cube, next-frame reveal after removing it, translated camera coordinates, and pass teardown when disabled. These checks establish correctness, not a full-scene speedup.

## Choosing direct or indirect draws

GPU Resident Renderer and GPU Procedural Generation can remain enabled with **Use Indirect Draws off**. This uses the existing direct BRG draw path; the three optional GPU filters require indirect draws and are bypassed in direct mode. Use total frame time to choose the faster path for a scene. Fewer GPU survivors do not imply fewer CPU draw commands or shadow draws, and a dedicated occluder depth pass can cost more than the detail work it removes.