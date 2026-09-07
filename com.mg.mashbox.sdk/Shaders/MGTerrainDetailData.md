# MG Terrain indexed detail shader data

Each Density Detail Layer now has Texture Slice, Shader Tint and Wind Multiplier.
Its definition index is its current layer index; do not hard-code that index in a material.
Maps and prototype assignments are unchanged. Each terrain owns its table within its BRG buffer.

## Shader Graph connection

Use an HDRP Shader Graph with DOTS Instancing enabled and retained in player builds.
Add a **Custom Function** node, Source **File**, file `MGTerrainDetailData.hlsl`,
Name **MGTerrainDetailData**, Precision **Float**, no inputs. Add outputs in this order:

1. DefinitionIndex (Vector1)
2. TextureSlice (Vector1)
3. Tint (Vector4)
4. WindMultiplier (Vector1)
5. RandomValue (Vector1)
6. HasDetailData (Vector1)

Connect TextureSlice to the Index input of Sample Texture 2D Array nodes.
Multiply sampled base color by Tint.rgb and existing wind amplitude by WindMultiplier.
RandomValue is stable for a generated instance within its renderer path; changing population,
seed, mesh-part transform or switching generation paths may change it.
HasDetailData is 1 for MG BRG draws, otherwise 0. Use that output to retain the graph's
existing material inputs for ordinary renderers, graph previews and the legacy fallback.
Do not create Blackboard properties called `_MGDetailInstance` or `_MGDetailTable`.

Assign the SAME mesh and material to multiple detail prototypes to batch them together.
Texture arrays are material inputs; assign compatible array assets in the material.
Different meshes, submeshes, materials, shadow settings or GPU part transforms still split draws.
Changing a TextureSlice does not switch geometry. Actual mesh changes remain prototype mesh choices.

## Scope and cost

Both CPU-uploaded BRG and GPU-procedural BRG populate the data. The legacy packed/combined
renderer does not populate this table: the function returns neutral values and HasDetailData=0.
This includes the current Edit Mode renderer; test indexed variants in Play Mode.
Existing grass Shader Graphs and materials are not rewritten, and texture arrays are not
automatically assembled from their textures. Connect the node to an array-enabled graph first.

Layout: existing 96-byte matrix pair per submitted mesh instance plus 16 bytes for index/random/rank/fade enable.
The table uses a 16-byte header (layer count, near end, mid end, transition width) plus 32 bytes per layer (tint, then slice/wind/mid density/far density).
No global shader buffer or material mutation is used, so terrains can share materials safely.

## GodGrass density transitions

`Density Transition Width` defaults to 10 metres. A positive width keeps the grid fixed;
zero disables fades. Distance Density LOD must be enabled. Runtime draw budgets remain
hard limits: transition instances consume that budget and can still be cut by it.

GodGrass's alpha path now passes through the file Custom Function `MGTerrainDetailFade`:
inputs InAlpha (Vector1), UV (Vector2/UV0); outputs OutAlpha (Vector1), InstanceFade (Vector1).
OutAlpha preserves the authored alpha; the function performs a separate fragment discard
using stable UV-space noise. The existing alpha and shadow cutoff logic remains connected.
Preview and standalone renderers without MG fade data are fully visible.

Instance data z is stable population rank; w is overall density + 1, or zero to disable.
The fade itself is calculated every shader invocation from rank, distance and the band's
endpoints, so it continues smoothly between resident-cell refreshes. Existing low-density
instances retain full coverage. Additional instances are submitted for the fade band.
The width is limited to the separation between the two transition centres to avoid overlap.

This fade is wired for `Shader Graphs/MG_GodGrass` in CPU BRG, compute-generated BRG,
and classic instanced/non-instanced fallback draws. Classic draws supply `_MGDetailInstance`
and the two `_MGDetailFadeRanges`/`_MGDetailFadeDensities` vectors through a property block;
the older indexed tint/texture-definition function remains BRG-only. Other shaders keep
discrete density selection. Capture mode disables fading and retains full density.

The dither is also evaluated in shadow/depth passes. It is not transparent blending and
can look stippled without sufficient temporal antialiasing; test motion and shadow quality
in the intended renderer. Shader Graph wiring was added to GodGrass, not MG Lit Trail.
