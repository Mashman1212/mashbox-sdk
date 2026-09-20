# MG Terrain tree LODs

Select a foliage prototype and set **Kind → Tree**. The **Tree LODs** controls support one to three levels:

- LOD0 uses the source prefab's highest-detail meshes.
- LOD1 uses its second mesh LOD, starting at 60 metres by default.
- LOD2 uses its last mesh LOD, starting at 200 metres by default.
- Optional medium/far prefab overrides support separately authored low-poly trees and mesh impostors. Keep their root pivot, scale and orientation aligned with the source.
- Transition hysteresis defaults to 5 metres. Maximum Draw Distance still controls final culling; zero means no prototype-specific limit.

Existing prototypes receive these defaults on deserialization. Setting LOD Count to one explicitly disables switching. Missing mesh levels reuse an available representation; this feature does not create simplified meshes or bake impostors. Unity BillboardRenderer levels are skipped in favour of a mesh representation.

Density-painted trees use the existing fixed cells and resident GPU/BRG path. Matching child transforms share a single GPU range across LOD meshes and submeshes. LOD changes update visibility and draw selection without rebuilding the cell or generating new transforms. The CPU fallback shares matrix arrays as well. Individually placed trees use 32-metre spatial cells and cached instanced batches.

Painted trees now expose **Mid Density** and **Far Density** per prototype under **Tree Density by Distance**. Near stays at 100%; each slider is an independent fraction of the painted population (0–1). The medium and far distance thresholds apply even with only one or two mesh LODs. Defaults remain 1 to preserve existing scenes. For example, 0.65 mid and 0.35 far draw fewer trees without repainting. The maps and full resident transforms stay intact; all submeshes and LODs draw the same stable prefix of the existing distributed instance order, so surviving trees do not move. Individually placed trees are unaffected.

Trees do not use grass density fading. Density changes happen at cell distance boundaries, without a dither transition. They retain the existing hard visible-instance budgets, with a stable near-budget classification across all mesh LODs. Under budget pressure the shared population cap can still reduce visible trees. Grass's distance ceiling and shadow toggle do not override tree prototypes. Tree canopy bounds include all LOD meshes and painted scale.

Transitions are hard mesh switches with hysteresis, avoiding the temporary double rendering of crossfades. Cell-wide transitions may be visible. Actual frame-time gains depend on the supplied meshes, materials, shadow settings and tree population; no scene FPS improvement is asserted.

## Validation

Run **Tools → MashBox → MG Terrain → Validate Terrain Tree LODs**. The checks use an isolated preview scene, including real GPU compute/BRG preparation, and write `validation.txt` here. They do not save or edit scene/prefab assets or enter Play Mode.

`original/` preserves the pre-change source (including existing user edits); `staged/` holds the installed source. Compiler responses and logs in `build/` verify against the project's installed Unity assemblies. `install.ps1` refuses to overwrite source that changed since preparation or the previous installation.
