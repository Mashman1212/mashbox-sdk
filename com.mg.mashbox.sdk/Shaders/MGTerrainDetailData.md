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

Layout: existing 96-byte matrix pair per submitted mesh instance plus 16 bytes for index/random.
The table uses a 16-byte header plus 32 bytes per layer (tint, then slice/wind/reserved).
No global shader buffer or material mutation is used, so terrains can share materials safely.
