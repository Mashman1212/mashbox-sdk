# Distant morph height seams

The capture samples texel centres inside each tile. Preserving the outer sampling
grid does not make independently captured boundaries agree. Each delta R16 map
also has a separate decode range, so copying normalized pixels is insufficient.

`MGTerrainDistantMorphSeams` runs when `SaveDistantSurface` publishes morph maps.
The freshly baked tile owns its boundaries: decoded world-space lift is copied to
the matching edge of each existing neighbouring map, including the diagonal owner
of a four-way corner. No neighbour capture is required. Neighbours without maps
join on their first bake. Active and inactive children of the same terrain world
are considered; standalone tiles consider other standalone tiles in the same scene.

Neighbours retain their height asset GUIDs and interior heights. R16 ranges grow
only when necessary to hold a taller copied edge. Range growth requantizes interior
values with the existing 16-bit precision. Equality is limited by each map's R16
quantization; normalized pixel values are intentionally allowed to differ.

All neighbour texture, metadata, and material changes participate in the existing
capture transaction. An unsuccessful bake restores their previous state. The
separate distant proxy mesh mode is unchanged.

Requirements: upright axis-aligned positive-scale tiles; matching shared-edge
extents and texture sample counts; readable generated delta R16 height assets.
Incompatible neighbours fail explicitly before publication. Clear stale height
assignments and rebake at a common resolution if changing capture resolution.
Mixed-size edges/T-junctions and legacy absolute height maps are not welded.
Underlying ground seams and morph strength/fade settings must already agree.
This change addresses height displacement, not appearance/normal-map seams.

Rebake the world's distant morph maps once to repair existing boundaries. Later
single-tile rebakes maintain the touching boundaries automatically.

Validation: `Tools/MashBox/MG Terrain/Validation/Distant Morph Seams` in a saved
scene. `MGTerrainDistantMorphSeamValidation.RunBatch` is the isolated batch entry
point. Checks cover all four internal edges and the common corner, differing
decode ranges and Y scales, taller/lower rebakes, interior preservation, stable
asset GUIDs, untouched non-neighbours, repeated-weld idempotence, transactional
rollback including metadata, and rejection of mixed resolutions.
