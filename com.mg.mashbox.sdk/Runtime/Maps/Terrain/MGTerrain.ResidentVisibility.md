# Resident detail visibility updates

The full-residency path retains terrain instance transforms on the GPU. Precise selection is the default: movement refreshes the actual camera frustum, configured draw distances and density distances. A stationary camera reuses selection, and TAA jitter does not invalidate it. Unchanged populations reuse the GPU visibility buffers.

The previous default used a 12 m / 15 degree guard and promoted density over an extra 12 m. That reduced scan time but increased submitted geometry in render and shadow passes. It is now experimental and opt-in via `m_AmortizeResidentVisibility`; a faster scan alone is not evidence of faster total frame time.

With experimental amortization enabled:

- Normal movement starts a new selection after 2 metres or 3 degrees. TAA projection jitter does not invalidate selection.
- Selection scans yield after approximately 0.25 ms, checking elapsed time every 32 work items. Rejected sectors count toward the budget. An in-progress scan continues on subsequent frames while the last complete draw set remains active.
- Selection includes conservative 12 m translation and 15 degree rotation margins. Density population also includes the approach margin; compatible grass shaders still evaluate fades using the live camera. This trades some extra potentially drawable instances for continuous coverage between CPU refreshes.
- Initial selection, camera replacement, projection changes, and movement outside the guard cause an immediate complete scan. These exceptional refreshes are not time-budgeted. GPU submission after a completed scan is also separate from the scan budget.
- Stable LOD buckets replace repeated sorting in the full-resident path. An exact chunk/population comparison skips visibility-buffer rebuilding and compute dispatch when nothing changed.
- Per-cell prefix ranges are cached, including storage offset, population, and generation invalidation. Layer definition uploads are skipped when the buffer, address and contents are unchanged. Prepared group iteration no longer boxes its enumerator.
- Detail-cache resets discard pending scans. The nearby-streaming and non-resident sorting paths keep their existing behavior.

Profiler markers separate `MGTerrain.ResidentVisibility.Scan` from `MGTerrain.ResidentVisibility.Submit`. `LastResidentVisibilityCellsTested` and `ResidentVisibilityUpdatePending` expose scan progress for diagnostics.

Validation: `MashBox/Validation/Validate Terrain Resident Visibility` exercises a 20,000-cell fixture. It checks the default selection against the unexpanded camera frustum and actual bounds distances, compares complete and sliced scans, checks experimental motion scheduling, verifies unchanged GPU-submission reuse, and tests prefix cache invalidation after regeneration and relocation. These are synthetic CPU correctness and scan measurements, not full-map frame-time or GPU measurements.
