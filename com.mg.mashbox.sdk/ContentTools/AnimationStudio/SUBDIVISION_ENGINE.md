# GPU subdivision in Modeling mode

Implemented 2026-10-29 using OpenSubdiv 3.6.1 Catmull-Clark and a Unity compute evaluator. This is finite-level polygon subdivision, not exact limit-surface sampling. It now participates in the ordered modifier history described in [MODIFIER_HISTORY.md](MODIFIER_HISTORY.md).

## Current workflow

Subdivision is an applied operation inside the collapsible Modifiers group. Mesh Display > Add Subdivision appends it. Factory F3 adds the first Subdivision or toggles the last one, respecting custom bindings. Level 0–6 defaults to 2. The user chose strict creation order: later tools edit the subdivided polygons, not the earlier low-poly cage. Earlier operations remain editable through replay; incompatible downstream selections visibly fail. Modify > Clear History commits the current evaluated geometry and removes history. No permanent standalone subdivision section or visible Bake Subdivision command remains.

CPU refinement supplies editable downstream polygons. A terminal Subdivision also uses the GPU evaluator for actual display from its retained input; a later operation owns the final rendered result. Buffers persist between redraws. Mode exit and disposal release them. No ordinary-edit GPU readback. Workspaces use v2 ordered records; old workspace compatibility was explicitly dropped. Studio crease settings, serialization, remapping and native descriptor input have been removed. See MODIFIER_HISTORY.md for exact behavior and limitations.

## Connectivity and attributes

Node.geometryVertices separates geometric identity from render vertices split for UVs or normals. Known Studio primitives author that map. Normal edits, duplication, combine, split, cuts, extrusion, bevel, bridge, weld, deletion and tool preview snapshots preserve or extend it. Combine namespaces identities between inputs; coincident coordinates never automatically join unrelated shells.

Fresh FBX imports recover original control-point connectivity and authored polygon boundaries through the installed Autodesk FBX SDK. Unity UV/normal render splits therefore do not become false subdivision boundaries. Matching validates triangle-to-source-polygon incidence, corner identity, coverage and material consistency; ambiguous overlapping shells are rejected rather than position-welded. Triangle-only sources retain the existing conservative auto-quad conversion. Source mesh assets and importer settings remain unchanged. Mesh-asset and scene-instance imports resolve the source through their original mesh asset path, cached only for that import. Missing SDK, non-FBX or unmatched/ambiguous sources retain the existing raw-index fallback. Existing workspace captures are not migrated: import the source into Studio again. Arbitrary baked transforms and every FBX variant are not established.

UV0–UV7 retain dimensions 1–4, with colors and material slots. Face-varying identities use the union of UV/color discontinuities, independently of normal splits, with boundary-linear interpolation. This is one combined face-varying stencil channel, not eight independently classified seam domains. Output normals are finite-mesh area-weighted smooth normals separated at residual sharp edges; tangents are derived from UV0. Shared meshes share operation history and evaluated geometry; renderer resources remain node-owned.

## Implementation and ownership

- Native~/OpenSubdiv vendors commit f26d0dedeaf846ea321865a527bb4489691c2f04, upstream LICENSE.txt/NOTICE.txt, an owning C ABI and Build-OpenSubdiv.ps1. The build script accepts explicit MSVC and Windows SDK paths and uses a static runtime. Supplied binary: Editor/Plugins/x86_64/StudioOpenSubdiv.dll.
- StudioSubdivisionPlan.cs owns copied factorized stencil data, refined quad/parent-face maps and normal adjacency. Native plan memory is freed after copying. Native estimates reject output over 500,000 polygons before refinement.
- StudioSubdivisionData.cs validates authored topology, prepares attributes and creates CPU Bake/Export meshes. StudioModelEditor.Subdivision.cs owns affected-node caches, UI, history and lifecycle integration.
- StudioSubdivisionGpu.cs and StudioSubdivision.compute evaluate positions, attributes and normals into a Unity-owned raw vertex buffer. Buffers and refinement tables survive position-only edits. Dispatch and renderer draws are ordered on the same graphics queue; ordinary editing performs no synchronous GPU readback. Dirty redraw drives updates; there is no idle render loop.
- Connectivity, crease and rule changes rebuild refinement. History edits reevaluate the affected stack. Terminal display buffers persist until the history revision changes. CPU evaluation supplies strict-order editable history results; terminal GPU display is independently evaluated and unsupported GPU rendering is explicitly reported.

## Supported scope and remaining limitations

Windows x64 Editor with compute/SM5 hardware is supported; Direct3D12 is the backend actually verified. Other backends are not measured, and other native platforms have no binary. Unsupported, unreadable, skinned, blend-shape, invalid-topology and over-budget inputs report through existing Studio notifications and retain the cage.

Source provenance for unmatched/non-FBX imports, automatic crease propagation to newly split edges and independent per-attribute seam domains remain limitations. Actual FBX round-trip attribute fidelity remains subject to the existing exporter and has not been tested here. Topology tools were integrated in source; the focused native run exercised Extrude and explicit Weld, not every tool combination or physical input gesture. No dense-mesh FPS or general application-performance claim is made.

## Validation (2026-10-29)

Native OpenSubdiv build and cube/open-boundary/UV-seam/fractional-crease/topology checks passed before the history change. The current strict-order workflow passes StudioModifierValidation's 21 focused checks on Unity 6000.4.12f1 / Direct3D12, including actual GPU pixels versus the editable evaluated mesh, history replay and Undo/Redo. The Channels panel was visually inspected. See MODIFIER_HISTORY.md for exact coverage and final source-only refinements. Original cage-preview measurements (0.135 ms coordinate update/dispatch; 23.441 ms extrusion/rebuild on a level-2 cube) describe the superseded workflow and are not measurements of stack replay. Dense-stack performance and other GPU backends are unmeasured.


### Exact frame import regression

Verified on the user's exact frame_subdivision_test.fbx in disposable SelectionUndoProject, Unity 6000.4.12f1 / Direct3D12: 1,239 render vertices map to 763 geometric controls; boundary edges fall from 924 to 132; original polygons restored and zero creases assigned. Level-2 actual GPU renderer and CPU editable result have zero differing pixels. Before/after surface captures visually show smoothed joins and removed false seam cracks (9,165 changed pixels). Ambiguous coincident-shell fixture rejects the match. Captures use a deterministic normal-lighting test shader and temporarily switch the disposable renderer to built-in, restoring its pipeline afterward; this is not an HDRP material/lighting or numerical Maya-parity test. Current-source compilation passes. The disposable editor is left open. Early incompatible-material captures were superseded; no broad suite or dense-import performance claim.
