# MG Terrain World

Use one **MG Terrain World** parent for independently authored converted terrain chunks.
A 2-by-2 arrangement of 512 m chunks covers 1,024 by 1,024 m. Chunk meshes do not need
to be merged, and chunks do not need equal resolutions or sizes.

## Adopt existing work

1. Save the scene normally, then select the converted MG Terrain chunk objects.
2. Choose **GameObject > MashBox > MG Terrain World From Selected Chunks**.
3. Inspect the new root's overall detail distance, visible-instance budget and build/cache limits.
4. Use **Edit** beside a chunk in the root inspector, or select the chunk directly, to use its existing tools.

Adoption reparents the existing objects while keeping their world transforms. It does not
reconvert terrain, regenerate instances, remap layer indices, or copy/replace meshes,
density/size textures, seeds, control maps, holes, collider assets, foliage palettes, or
appearance bakes. The command supports Undo/Redo. Inactive chunks remain inactive.
Prefab instances must be unpacked before adoption; sheared/mirrored transforms and
cross-scene adoption are rejected before mutation. The root should have unit scale and
identity world rotation when adopting or converting chunks.

To extend an existing world, lock its inspector, select more converted chunks, then use
**Adopt Selected Converted Chunks**. The **Detach** button restores standalone operation.
Disabling the world component also restores standalone operation for its children.

## Convert additional pieces

Lock the world inspector, select enabled Unity Terrain objects in the same scene and click
**Convert Selected Unity Terrains Into This World**. Choose an Assets output folder.
This uses the established converter for meshes, colliders, splat maps, trees and density
layers; it disables each successfully converted source terrain. Existing converted chunks
are unaffected. Each conversion retains the converter's own rollback on failure; earlier
successful chunks remain if a later chunk fails. Source terrains under a world also use
that world when converted through the existing conversion tools.

## Painting and appearance maps

Detail density/size painting can cross chunk borders inside the same world. Matching uses
prefab/mesh/material/kind and palette entry identity, never another chunk's layer index.
Missing or ambiguous matches are skipped with a warning. Configure matching detail
definitions before painting across those borders. Density textures remain independent.
The existing first-stroke copy-on-write behavior protects shared source textures.
Other editing tools and appearance bakes retain their existing per-chunk workflows.
Appearance capture bypasses interactive world budgets so it can finish a complete bake.

## Runtime ownership

The world owns the render-pipeline camera subscription, one BatchRendererGroup, shared
mesh/material registrations, and combined culling output. It computes proportional
visible-instance allocations across participating chunks and controls aggregate cell-build
and mesh-upload allowances. Build priority rotates across chunks. Placed trees use their
existing rendering path and are not included in the density-detail budget.

Child `MGTerrain` components deliberately remain as backward-compatible serialized chunk
containers and editor targets. They no longer subscribe independently to the camera
pipeline while registered to a world. Local caches, GPU instance buffers and indirect
buffers remain per chunk so unloading or editing one chunk does not rebuild every chunk.
Those buffers are batches within the world's shared renderer, not separate renderer groups.

World-managed chunks use nearby detail streaming rather than the old full-terrain prewarm
and retain-all modes. Density draw distance is controlled by the root, capped by each
prototype's distance. The cache-cell target is soft: the current working set can exceed it.
The pending-build limit gates new cells; one cell can launch multiple mesh-part tasks.
Once a chunk is beyond the detail distance plus unload margin, its detail resources are
released. Meshes/colliders remain loaded. Disabling, destroying or reparenting chunks
unregisters their batches; re-enabling/reparenting registers them again. Full scene/asset
streaming, a strict VRAM byte cap and cross-scene registration are not implemented here.

## Validation

Run **MashBox > Validation > Validate Terrain World** in Edit Mode. It uses a temporary
preview scene and verifies adoption preserves serialized terrain data, mesh/map references,
paint pixels and transforms; correct chunk picking; unambiguous detail-layer matching;
disable/enable/detach behavior; Undo/Redo; proportional budget allocation; and shared
camera/light draw-command index offsets. It does not save or convert the current scene.

Compare the same camera route before and after adoption in a development build, including
the center intersection and crossing each border. Check vegetation density, shadows,
appearance capture, memory after leaving/returning to chunks, and CPU/GPU frame time.
Compilation and logic checks do not establish an FPS improvement on the authored map.
