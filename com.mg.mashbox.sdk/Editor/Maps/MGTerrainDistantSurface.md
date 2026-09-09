# Distant terrain surface bake

Select an MG Terrain and open **Distant Surface Mesh** beneath Terrain Appearance Capture. Click **Bake Distant Mesh** and choose an output PNG location inside Assets.

The bake renders colour and floating-point height from above using the existing tiled appearance capture, including painted details at close-range density. Depth comes from rendered geometry, so colliders are unnecessary. Detail tilt is forced to zero for this bake. Transparent objects are excluded so colour matches depth; alpha-clipped foliage is supported.

## Controls

- **Mesh Spacing (Metres):** approximate world-space distance between vertices. Default 2 m; increase for cheaper distant geometry. Bakes are limited to one million grid vertices.
- **Height Smoothing Passes:** 0–4 neighbour smoothing passes; zero preserves sampled heights. Smoothing can soften canopy peaks and move the mesh beneath the original terrain. Outer boundaries and empty samples stay unchanged.
- **Capture Headroom (Metres):** camera height above the terrain's highest vertex. Default 150 m; increase if assets extend higher.
- **Capture Layers:** included geometry layers. Must include the terrain renderer's layer. Painted details follow their terrain renderer's layer.
- **Fade In Start / End (Metres):** camera-to-surface distance for a dithered transition. Defaults 300–400 m. These values remain editable on the generated material after baking.

Capture resolution, exposure, sun settings and the optional normal capture use the appearance capture controls above.

## Output and transitions

Each bake saves a separate colour PNG, optional world-space normal PNG, normalized R16 height texture asset (zero for empty pixels; 1–65535 for valid heights), mesh asset containing chunks, and a material. The generated child object is named `<terrain> Distant Surface`. Chunks have at most 128 × 128 cells, shared boundary positions/normals, and whole-terrain X/Z UVs. They can be frustum culled independently. Save the scene to retain the generated child.

The material is unlit because the colour capture already includes lighting and exposure. It uses an HDRP depth prepass with the same dither as the colour pass. Baked normal maps are exported when requested but are not sampled by this unlit material. Brightness is adjustable. Changes to lighting, geometry or terrain transforms require recapture.

Original terrain, tree and detail draw distances are not changed. Match those controls to the proxy's fade interval. This is an optional distant representation, not automatic management of arbitrary scene objects' LODs. Gaps below branches, vertical facades, and overhangs cannot be represented by one height per X/Z coordinate; skyline trees may still need individual LODs.

Existing distant surfaces are excluded from all appearance captures. A successful rebake replaces this terrain's generated scene child with Undo support. It creates uniquely named assets and keeps previous assets available for other scenes or Undo. Cancellation/failure preserves the old child and removes newly created bake assets. Ordinary appearance capture continues to reuse the terrain material's assigned maps.

The implementation and shaders belong to the **mashbox-sdk** repository. Collaborators need that SDK update as well as any generated scene/assets changes from the map project.

## Morph the original terrain with MG Lit Trail

The current MG Lit Trail shader also accepts the saved height map directly. Existing materials default to **Distant Surface Strength = 0**, so importing the update does not alter their geometry.

For a new bake, leave **Apply Bake to Trail Shader** enabled in MG Terrain's Distant Surface Mesh section. The bake captures normals as well as colour/height, creates a dedicated Trail material, assigns it to this terrain, and hides the separate distant mesh. Save the scene after applying.

For an existing bake, assign its original readable R16 (or legacy RFloat) **`_Height.asset`** in **Baked Height Map**, then click **Apply Height Map to Trail**. Keep its matching colour PNG and optional `_NormalWS.png` alongside it. Use a bake from this same terrain and coordinate system. The button creates a new material asset rather than changing a material shared by other trails.

The material's **Distant Surface Morph** controls are:

- **Strength:** 0 disables morphing; 1 reaches the captured height. Shader evaluation clamps strength to 0–1.
- **Start / End:** horizontal camera distance in world metres. The existing Fade In fields initialise these values when applying; edit the material afterwards.
- **Bounds:** terrain-local X/Z minimum and size, filled automatically from the source terrain mesh.
- **Max Height:** maximum terrain-local captured Y, filled automatically for conservative renderer bounds.

The shader adds only positive elevation above the existing displaced ground. It samples height at mip zero, ignores missing capture pixels, and converts local elevation to HDRP's world-space tessellation displacement. The baked colour and normal blend reaches at least the morph's blend amount; existing far-appearance blending still works when morphing is disabled.

Source and surface-tile rendering bounds include the canopy height. Applying also expands HDRP's tessellation patch-culling displacement limit; linked material synchronisation preserves that limit and the per-terrain morph properties. Colliders, detail placement and original object draw distances are unchanged. Align tree/detail fade distances manually, and keep enough terrain vertices or tessellation to represent canopy shapes. Reapply/recapture after changing the terrain's mesh, scale or bake coordinate system.

Appearance capture temporarily disables this terrain's morph so the next bake cannot capture its own inflated surface. Set **Strength = 0** to restore its original rendered shape, or undo the material assignment to return to the previous material. Re-enable the separate distant child only when choosing that representation instead of the morph.

## R16 storage

New bakes save a single-channel, linear R16 height asset: 8 MiB at 2048², without mipmaps. GPU capture and mesh construction still use full float precision. The height minimum/range are stored in the height asset's `.meta` importer userData and copied to the material's hidden `_DistantSurfaceHeightDecode` vector when applied. Keep the `.meta` file with the texture.

Code zero represents an empty pixel; valid height is `minimum + ((code - 1) / 65534) * range`. Constant-height maps use range zero. Legacy RFloat bakes remain supported with decoding mode zero. Normalized texture previews display height variation rather than clipping raw metre values.
