# MG Terrain Seam Fit

## Fit either side of the seam

Shift-click a loft face or shoulder to make the loft the editable source. With Target Root empty, it fits toward nearby MG Terrain vertices (or choose Nearest Surface for triangle fitting). This inverse direction accepts targets above or below the loft. Only source vertices under the brush and within Snap Distance move; terrain targets remain unchanged. Ctrl-drag lowers the selected loft just as it lowers terrain.

Shift-click terrain to switch back to terrain-to-loft fitting. Normal dragging stays on the selected kind of surface when loft and terrain overlap. The panel shows Fit Direction and keeps separate snap choices for terrain and loft editing.

Loft strokes address the generated mesh including combined shoulders, not a UV-generated display mesh with potentially different indices. Regeneration replays positions and restores seam normals after the loft's own normal matching, before collider and UV rebuilding. Preserve the generated vertex layout; changing resolution or shoulder topology requires redoing the affected strokes. These remain baked edits, not a live terrain dependency.

Select an MG Terrain and click **Seam Fit** in its inspector, or choose **Seam Fit** in the existing Sculpt brush panel / Mappy action toolbar. Shift-click the terrain to make it sculptable, then drag along the shoulder seam. This uses the existing sculpt stroke history, preview, falloff rings and brush controls.

- **Target Root:** leave empty for active loft render meshes when editing terrain, or MG Terrain surfaces when editing a loft. Assign a mesh object or parent to use its enabled, readable child mesh renderers instead. The edited source and meshes belonging to the same loft are excluded; target meshes are never modified. Target colliders are not required.
- **Snap Distance:** maximum distance in world metres from each terrain vertex to the nearest target surface or vertex. Both the brush radius and this threshold must include the terrain vertex.
- **Snap To:** Nearest Surface fits to triangle interiors and edges. Nearest Vertex fits to actual target vertices, which can bunch terrain vertices if the target is coarse.
- **Nearest Vertex Above:** fits to the nearest target vertex strictly above each terrain vertex in world Y, within Snap Distance. Lower and equal-height vertices are ignored, even if closer. Use this to reach the top of a shoulder that digs downward; increase Snap Distance if needed. No eligible higher vertex means no change. Surface Offset and Height Only cannot cause downward movement in this mode.
- **Minimum Rise:** in Nearest Vertex Above mode, prefer targets at least this much higher (default 0.02 m). Repeated passes skip nearly reached vertices and climb onward. The search also clears Surface Offset automatically so it cannot stick to the same offset target. Increase this setting to skip farther upward. If there is no next step within Snap Distance, the brush finishes fitting the nearest remaining higher vertex, closing the final gap at the top. It does not automatically choose the highest vertex in one pass.
- **Strength / Falloff / Spacing:** use the ordinary sculpt controls. Each sample moves partway toward the target according to strength and falloff. Repeated passes tighten the fit.
- **Height Only:** off permits full XYZ fitting, overriding the terrain's ordinary height-only sculpt constraint for seam strokes. On preserves terrain local X/Z; this may leave lateral gaps.
- **Normal Blend:** zero leaves geometric normals; one fully matches the sampled target normal at full brush influence. Normals blend in world space, including nonuniformly scaled transforms.
- **Surface Offset:** offset along the target normal after snapping. The default is -0.002 metres, keeping the terrain slightly beneath the loft. Use zero for an exact surface fit.

Nearest Vertex Above is the default Snap To mode: ordinary left-drag fits upward, and repeated passes climb the shoulder. Ctrl+left-drag gently lowers terrain vertically in world space without requiring any target mesh. Ctrl Lower Depth defaults to 0.05 metres per sample at full strength; strength and falloff reduce that amount. The brush ring turns orange while lowering. Release Ctrl to resume fitting. Other Snap To modes remain available.

Ctrl+middle-drag adjusts radius horizontally and strength vertically. Shift temporarily smooths; Ctrl+Shift temporarily adds noise. Undo groups a drag, including mixed fitting and lowering; Remove Last removes the latest sample; Clear All removes all sculpt and seam samples on that modifier.

Seam samples store sparse terrain-local position deltas and normal targets. Replay and subsequent sculpt previews restore the normal blending. Samples are baked: moving or regenerating a target loft does not move already fitted terrain. Repaint after target changes. The terrain must retain its vertex layout; samples with a different vertex count are skipped.

Painting switches the edited terrain to its updated master collider and disables saved child collider chunks, with the switch included in Undo. Rebuild child colliders from the terrain inspector when finished if you prefer chunked collision.

This changes terrain geometry and vertex normals, not topology or materials. Sparse terrain vertices may need more mesh density to follow a sharply curved shoulder. Shader displacement, normal maps, texture transitions and hard material differences can still show a seam.
