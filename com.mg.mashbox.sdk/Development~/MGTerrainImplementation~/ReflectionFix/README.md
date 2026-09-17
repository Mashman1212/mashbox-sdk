# Reflection grass fix

Applied to the linked package at `D:/mashbox-sdk/com.mg.mashbox.sdk/Runtime/Maps/Terrain`.

Reflection cameras no longer participate in detail streaming, world unloading, or visible-list replacement. GPU reflection culling selects generated resident cells using each capture face's planes and emits independent direct commands, bypassing gameplay visibility and occlusion indices. Shared-world and standalone BRGs both use this path. Resident mesh/material groups are registered even when outside the gameplay view.

The first patch missed the Edit Mode CPU path: its cache covered the gameplay view, and drawing its instanced batches directly omitted detail texture/tint properties. CPU captures now generate temporary instanced grass for each probe face, with configured density, distance and instance limits. They submit per-instance shader data through the regular detail batch accumulator. They do not queue asynchronous combined-mesh builds or replace the gameplay cache.

Validation: Unity 6000.4.12f1 compilation passed; see compile-cpu.log. Tested the actual Cotswold Ridge scene in Edit Mode with WORLD REFLECTION, On Enable, without time slicing. Captured all six faces after warmup. Before/after PNGs show grass on previously bare slopes in the NegativeX and NegativeZ faces. The gameplay cache stayed at 1,073 cells, with both dirty flags false, across all six callbacks; see capture-diagnostics.txt. Terrain diff whitespace check passed. Play Mode and time-sliced capture remain untested.

Capture PNGs use a fixed exposure multiplier of 0.0001 and Reinhard display mapping. Raw render-target readback has inverted vertical orientation; these are diagnostic face exports, not replacement cubemap assets. The temporary editor diagnostic script is stored here, outside Unity import, after testing.

Limits: the GPU path still reads already resident grass. CPU captures generate synchronously and may hitch for dense terrain. Capture limits apply per terrain and face; this is intended for occasional On Enable/On Demand captures, not continuous high-frequency CPU probes.

Manual validation: let grass settle, toggle the realtime On Enable reflection probe, and inspect all six cubemap faces. Confirm grass remains stable in the game view, including after repeated toggles; repeat with time slicing. Test both an MG Terrain World and a standalone terrain, plus Edit Mode CPU details. Check shadow-only prototypes stay absent from the capture and layer masks still exclude hidden terrain.
