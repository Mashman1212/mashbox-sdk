MG Terrain Merge

Menu: Tools > MashBox > MG Terrain > Terrain Merge

1. Open the scene whose terrain you are keeping.
2. Set Destination to an MG Terrain tile or world in that scene.
3. Set Source to a tile/world from another scene, a saved Scene asset, or a sculpt Mesh asset.
4. Build Merge Preview. Cyan marks higher source terrain; orange marks lower source terrain.
5. Start Merge Brush and left-drag to blend source heights into the destination.
   Alt navigates, [ and ] change radius, Escape cancels the current stroke.
   Use Take Whole Tile Sculpt (or Whole Destination) when full source coverage is available.
6. Undo/Redo works per stroke. Save the destination scene/assets normally.
   Rebake distant terrain if the project uses baked distant representations.

Scope and alignment:
- Height sculpting only. Destination UVs, topology, materials, paint, vegetation and holes are retained.
- Scene/object sources align in world space. Mesh assets use one destination tile's transform.
- Source geometry is snapshotted for a session and never edited by merging.
- Independent persistent sculpt and collision assets are created before changing destination data.
- Shared destination edges are joined; mixed-resolution joins can affect complete coarse edge segments.
- Repeated strokes blend toward source heights; they do not add the source height repeatedly.
- This is a manual two-version merge; it does not infer authorship or perform three-way conflict resolution.
- Ordinary direct sculpting now detaches editable mesh assets that belong to another saved scene folder.

Validation:
- Full editor assembly compilation against Unity 6000.4.12f1: passed (existing unrelated warnings).
- Isolated Unity runtime/editor validation: passed.
- Saved source scene preview, source snapshot isolation, transformed triangle sampling and coverage.
- Brush radius, falloff, strength, whole-tile transfer, repeated application without height stacking.
- Shared sculpt/collider isolation, UV/topology retention, mixed-resolution edge continuity.
- Undo/Redo, cancelled strokes, editing collision, copied-scene asset ownership, save/reload.
- Overlay shader compilation and render check on Direct3D11: passed.

No working terrain scenes were modified by installation or validation.
Validation can be rerun from Tools > MashBox > MG Terrain > Validation > Terrain Merge.
