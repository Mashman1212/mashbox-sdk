# Terrain candidate selection fix

Production source: `D:/mashbox-sdk/com.mg.mashbox.sdk/Runtime/Maps/Terrain/MGTerrain.Details.cs`.

Fixed sector/cell bounds now cache in Play Mode as well as Edit Mode, including cells which have not yet received a construction budget. Geometry/paint invalidation still discards them. Bounded runtime streaming prioritizes only the missing-cell prefix that can be built in a pass (normally two), rather than sorting every missing cell. Stationary-camera refreshes reselect the remaining prefix so subsequent construction keeps its nearest-first priority. Full residency/prewarming and editor paths retain full sorting. LOD state uses value entries, with both hysteresis paths writing updates back into the dictionary.

Controlled Play Mode benchmark: 257x257 height vertices, 1024x1024 density grid, leaf size 8, radius 220, 24 moving-camera queries after 24 warmups. Source baseline: 372.294 ms total; bounds cache: 163.061 ms; bounded selection plus bounds cache: 85.748 ms. Same 9,676 final candidates and bounds fingerprint (2377283685). Warm bounds recomputations reduced from 266,048 to zero. Tests also check the nearest budget prefix, transform invalidation, cached bounds against direct height scans, and painting invalidation.

These are isolated method timings, not end-to-end frame times or a replay of the user's exact profiler capture. The managed allocation counter reports zero even for the reflection harness, so it is not a verified zero-allocation measurement. First visits can still allocate cache storage and calculate new bounds. Cached bounds consume memory per visited cell, released with existing terrain detail-cache invalidation. See before.txt, after.txt, after-priority.txt (full-residency setting) and after-budget.txt (bounded streaming).

The retained editor menu `Tools > MashBox > MG Terrain > Check Terrain Candidate Selection` runs manually in Play Mode; no automatic test hooks remain.
