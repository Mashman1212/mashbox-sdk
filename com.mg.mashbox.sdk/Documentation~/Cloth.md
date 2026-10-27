# Cloth workspace

Open **MashBox > MashBox Studio > Cloth**, or choose **Cloth** in the Studio mode selector.

This is the first independent SDK cloth implementation. It does not reference Magica Cloth or Unity's built-in Cloth solver. Existing Magica components and assets are not converted or removed.

## Authoring

1. **File > New Scene** starts in the regular A-pose (Skinning Test frame 2), with the default body, bust, pants and shirt visible. All four are locked references with independent visibility toggles in the left mesh list. They cannot be replaced, removed, painted or simulated. New Scene saves dirty assigned profiles before clearing the preview garments.
2. Drop model assets into the viewport or left panel, or choose **File > Add Animation outfit**. Select a garment row to inspect, paint and simulate it; its profile is remembered when switching meshes. Garments can be skinned or static; skinned bones must match the SDK skeleton. Toggle visibility per mesh or right-click a garment to remove its source model from the preview. Enable Read/Write in the right inspector if needed.
3. Create a cloth profile asset. Profiles are bound to the exact source mesh. The initial map pins the highest 15% and permits 15 cm of movement elsewhere; paint appropriate attachment regions for the actual garment.
4. Paint movement limits with the left mouse button. Zero pins to the animated pose. Shift paints pins, Alt navigates, Ctrl+Z undoes a stroke. Painting stops simulation. The brush uses a shallow surface depth band; inspect both sides of narrow or layered surfaces.
5. Generate and adjust character capsules. The profile stores bone paths relative to the character root and a radius per capsule. Humanoid mapping is preferred, with standard bone-name fallback. Missing/custom bone paths require manual setup. At most 64 capsules are used.
6. Use the bottom transport bar: Play/Pause, Stop, Step, and Reset. Its animation dropdown offers **Rest Pose**, **Party Kid**, **Locking Hip Hop Dance**, and **Custom…**; Custom exposes an AnimationClip slot for a compatible Generic or Humanoid clip. Switching clips pauses playback and resets the pose and cloth. The built-ins are standalone Humanoid `.anim` copies in `ContentTools/AnimationStudio/Animations`, extracted from `Player@Emote_PartyKid.fbx` and `X Bot@Locking Hip Hop Dance.fbx`. Animation can play on the default references before adding a garment. Step advances one solver tick (or one animation frame without cloth); Stop returns to the beginning, and Reset clears motion and grabs. Seeking pauses and resets cloth at the chosen pose rather than replaying simulation history. Without a clip the track shows elapsed simulation time. Grab a particle and move its handle to stress-test cloth.
7. Save the cloth profile. Painting, collision bindings and solver settings live in that asset; character preview state belongs to the Studio session.

## Runtime

Add **MashBox / Cloth** (`MashBoxSDK.Cloth.MashBoxCloth`) to the garment. Assign its Renderer, saved profile, and character root for collider paths. Disable any previous cloth simulation on this same garment before enabling the new component. Use multiple components for multiple garments; the Studio previews one selected garment at a time.

The component runs after normal animation updates in LateUpdate. `Rebuild()` applies a new profile/topology/collider binding; `ResetSimulation()` removes accumulated motion. `Session` exposes particle/constraint counts and solve, bake and total CPU timings. A view camera and maximum distance enable distance culling; distant garments fall back to their original renderer and rebuild when they return. Culling is opt-in via the camera reference. Teleports and scale changes reset the simulation.

The component owns a separate mesh and renderer. Disabling it restores the original renderer and its offscreen setting. It never modifies the imported mesh or writes simulated geometry into the source asset.

## Performance design and measurements

Spatial clusters reduce simulation to a configurable hard particle budget while retaining the render mesh. Coincident UV/normal seam vertices are sewn together; spatially separate islands remain independent. Each cluster conservatively uses its smallest painted movement allowance, so a pin cannot leak; coarser budgets can make attachments wider. The solver uses Burst-compiled XPBD distance constraints for stretch and approximate bending, fixed timesteps, bounded catch-up, reusable NativeArrays and reusable mesh buffers. One job processes each garment to avoid shared-particle write races. It does not yet batch garments across workers or use GPU simulation.

The HUD reports actual CPU work, including skin baking and mesh upload preparation. It also reports dropped fixed steps. Lower the particle budget, iterations, substeps or simulation frequency to trade fidelity for cost. Normal recalculation can be reduced or disabled, at a shading-quality cost. Mesh baking and render-vertex updates still scale with the original mesh density.

Validation on 2026-10-27 in headless Unity 6000.4.12f1 with Burst enabled:

- Synthetic cloth: 256 particles, 1,350 constraints, 2 substeps and 4 iterations, approximately 0.13 ms per solve.
- SDK Practice Hoodie: 5,632 render vertices, 1,339 particles and 7,458 constraints, approximately 1.39 ms total per step (0.67 ms solve, 0.06 ms skin bake), averaged over 180 warmed steps.
- The warmed hoodie steps allocated zero managed bytes on the measured thread.

These are editor microbenchmarks on one machine, not frame-time guarantees or evidence of a speedup over Magica Cloth. Compare both systems in the same player build, on the same garments, animation, collision setup and quality target before migrating production outfits.

## Current limits

No cloth self-collision, inter-garment collision, continuous collision detection, GPU skinning/solve, multi-character job batching, automatic Magica profile conversion or artist-authored proxy meshes. Capsule collision is an approximation of the body; collision correction can exceed a movement tether when the two constraints conflict. The rendered surface follows clustered displacements rather than a fully simulated high-resolution shell. Seams with gaps remain independent; weld garments where necessary. Exactly overlapping layers share seam particles. Native jobs and output meshes are recreated when a culled garment returns.

Validation covers pins, distance limits, capsule contact, wind/gravity response, grabbing, reset, teleports, fixed-step catch-up limits, malformed profiles, source-mesh immutability, skinned bone following, repeated start/stop cleanup, and the real SDK hoodie. Tests and logs for this change are in the host project's `Tools/StudioCloth` folder.

The Cloth transport also has the Skinner's T-pose, A-pose and A-pose+ buttons. These pause simulation and sample a single frame of the SDK Skeleton's Skinning Test, without changing the chosen emote or its timeline position. Play, Step or scrubbing returns to the chosen animation; the pose sampler has no running playback graph.

## Display and materials

Cloth uses the same neutral preview shader as Skinning by default. Use **Textured** in the right panel to show assigned Unity materials on user garments, and **Wireframe** for the selected garment's quad-style edges. Wire topology is cached and its vertices follow the current animation or simulation.

Select a user garment to edit its material slots, or drag one Material onto the visible garment in the viewport to assign the hit submesh. Assignment pauses simulation and enables Textured preview. Assignments survive preview rebuilds and remain local to this Cloth workspace; source materials, prefabs and FBXs are not edited. File > New Scene clears assignments. Default references stay locked and neutrally shaded, matching Skinning.
