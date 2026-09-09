# Copy MG Terrain settings

Select the **destination MG Terrain**. Expand **Copy Settings From Another Terrain**, assign **Source MG Terrain**, and click **Copy Settings and Detail Setup**. The operation supports Undo and is available outside Play Mode.

Copies MG Terrain's rendering, culling, streaming, density, draw-distance, surface-tiling and editing settings, along with prototype configuration, detail-layer settings (sizes, offsets, tint, wind, seeds, visibility) and foliage-palette bindings.

Preserves the destination's surface mesh, transform, MeshRenderer/material assignment, colliders, holes, grid dimensions, control maps, baked height/appearance maps and placed-instance positions. Detail prefab, mesh, material and palette assets are shared references; their asset contents are not edited or duplicated.

Prototypes are matched by kind and prefab, or by kind/mesh/material when there is no prefab. Existing prototype indices remain stable, so destination instances continue to reference the correct prototypes. Source-only prototypes are appended.

Detail layers match by the remapped prototype and palette identity/entry. Matching layers retain destination density maps, painted size maps, palette source maps and represented counts. Source-only layers are added without density or size paint. Destination-only layers remain. Layer order therefore need not match between terrains, and repeated copies do not duplicate matching definitions.

Palette bindings match by palette asset. Destination source-density maps remain local; new bindings have no painted source map. Bindings are marked for rebaking after their settings change. Shared palette assets themselves are not modified.

The tool does not copy sibling components, arbitrary child objects, terrain surface shader settings, or geometry. It operates on the MG Terrain component's setup. New detail layers need painting (or destination density-map assignment) before instances appear.
