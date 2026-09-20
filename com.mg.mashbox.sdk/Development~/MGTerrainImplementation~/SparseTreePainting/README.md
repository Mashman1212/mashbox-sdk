# Fractional density painting

**Target Density / Texel** accepts decimals from 0 to 2000. At values at or below
1, **Sparse Coverage** also provides a 0–100% slider. For trees, try 0.05–0.1:
approximately one occupied texel in twenty or ten, respectively.

Density maps still store integer instance counts. Painting converts the target's
fractional part to a deterministic threshold using texel coordinates and the layer
seed. Dabs share the same target mask, so repeated strokes do not fill every texel.
Reducing the target retains a nested subset for the same layer and seed. Integer
targets keep their existing semantics. The target is per texel, not per square
metre; physical spacing still depends on density-map resolution and terrain size.

Painting over existing density thins it toward the selected target. This change
does not automatically alter existing maps. Erase, undo, shared world painting,
brush strength, and localized cache refresh continue through the existing pipeline.

The staged `MGTerrainEditor.cs` matches the installed linked SDK editor. Compilation
results are in `compile.log`. **Validate Localized Density Painting** tests the
actual brush on isolated preview-scene maps: sparse coverage, repeated dabs,
thinning dense maps, nested masks, erasing and integer targets.
