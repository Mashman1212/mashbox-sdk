# Original impostor tool comparison

## Follow-up: source normal override confirmed

The live source collector reports submesh 1 uses LEAFS with the asset-pack `tree v1/MG_Grass.shadergraph` (GUID ec1e6bfc02706b440825c5b83e994471). That graph wires Vector3 node 91f26294d9944ef1826affd5c34bebba, with input values (0, 1, 0), into VertexDescription.Normal. Thus the capture receives a forced-up vertex normal, followed by its tangent normal map and double-sided treatment, rather than the original mesh vertex normals. This explains the highly concentrated green/magenta raw normal colours. The visible source tree uses the same material, as verified in source-materials.txt.

The user also compared both output shaders and reported the same lighting failure. Do not repeat that as the next required test. The source normal override must be addressed before rebaking. No source material or source graph was changed by this audit. A temporary capture-variant experiment was withdrawn before validation while the user changed the source shader in Unity. The final lighting result remains unverified.

Original inspected read-only: `D:/Time Ghost/Assets/Code/Impostor`.
Current implementation: `Assets/MapiX/MashBox/ImpostorBaker`.

## Confirmed

- `OctahedralImpostorFragment.shadersubgraph` differs only in four remapped subgraph asset references. The normal decoding operations match.
- Both capture implementations use HDRP's MaterialSharedProperty.Normal AOV. The original allocates the default colour buffer; the current implementation explicitly allocates a linear half-float buffer.
- A controlled capture of the same RFMP_Connifer_06 source with each buffer configuration produces visually equivalent saturated green/magenta normals. See `raw-normal.png` and `original-buffer-normal.png`. This is a buffer comparison in MappyX, not a complete bake in the original project's Unity version.
- These saturated normals exist before dilation, PNG serialization, and texture import. Altering padding alone cannot remove them from covered source pixels.
- The original dilation code also used the normal's red channel as a validity test. Our coverage-mask correction addresses that independent defect; it has not resolved the reported lighting mismatch.

## Differences requiring separate lighting validation

- The working imported reference uses Translucent_OctahedralImpostor_NoPerInstanceColor and different specular/smoothness/transmission settings. The current output uses MashBoxFoliageImpostor.
- The current runtime adds orthographic-view handling in ImpostorUtility.hlsl. Directional shadow views need comparison against the original implementation.
- The current bake sets pixel depth offset for shadows to 1; the original default was 2. No validated conclusion about which value resolves this case.

## Limits

No backlighting fix has been established by this comparison. The source and runtime normal check covers one aligned view, not all camera/light angles. Experimental DirectDiffuseOnly/DirectSpecularOnly PNG captures clipped and are not valid evidence about lighting intensity; the associated experimental capture code was removed. Do not interpret their white pixels as the cause of the scene issue.

The next useful validation is the same baked tree and lighting rendered with the original runtime shader, followed by isolated material and shadow differences. Another resolution-only rebake is not supported by these findings.
