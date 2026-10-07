# Cotswold Ridge terrain rendering optimization

Validated in Unity 6000.4.12f1 / HDRP 17.4, 2026-10-06.

## Findings and changes

- Active terrain materials already had POM amplitude 0. POM was not the main demonstrated cause of this map's cost.
- MG_Lit_Trail hard-coded a maximum near-camera tessellation factor of 50. It now exposes `_TessellationQuality`, default 4, range 1–64, under Height & Displacement. Existing distance attenuation, layer blending, displacement amplitude and canopy morph remain. Lower tessellation reduces fine geometric relief close to the camera; 50 reproduces the previous tessellation setting for comparison.
- Chunk bounds copied the HDRP tessellation/canopy padding (roughly 140–263 metres in this map) into all three axes. MG_Lit_Trail chunks now use a conservative bound derived from the actual domain displacement: 0.2 * maximum absolute layer height remap * absolute master amplitude, with a 1-metre minimum. The existing separate vertical canopy expansion remains. Other shaders, property-block overrides, and Phong tessellation retain configured padding.
- POM fades its amplitude to zero and skips all height reads beyond the supplied fade end (200 metres in PixelSampler8). Explicit texture gradients replace implicit sampling inside varying loops. Nearby POM retains its configured steps and refinement.

## Validation

- Unity compiled and rendered the changed shader; the final live ShaderUtil audit reported no shader warnings or errors.
- Shader Graph JSON/object IDs/edge references and the live quality connection passed checks.
- Bounds tests passed for default padding, large/negative remaps and amplitudes, property-block fallback and Phong fallback.
- Same-camera frustum comparison: 1,539 active chunk renderers tested; 1,058 eligible with old bounds versus 504 with corrected bounds (52.4% fewer). Eligible source triangles: 1,465,110 versus 938,246 (36.0% fewer). This is a conservative frustum test, not actual draw calls or an FPS measurement.
- Two repeated 50/4 quality comparisons were captured near ground. Editor timing varied between passes; the final pair measured median GPU frame times of 8.473/8.234 ms. Do not interpret this as a reliable game-build speedup. Multiple editors and editor overhead were present. Raw CSV is retained.
- Render exports at quality 50 and 4 were visually inspected. Original material references and scene camera were restored; test materials were destroyed. No scene or material asset was saved by the test.

## Re-test

Use Tools > MashBox > MG Terrain > Rendering for the manual audit and reversible tessellation comparison. There are no automatic polling hooks in the final tool. A stop command restores the original materials and camera.

Rebuild/re-export the relevant map and game shader/runtime content before comparing an existing game build. Measure a fixed gameplay camera and resolution with Render Chunks enabled. The linked SDK source changes alone do not update an already-built game.
