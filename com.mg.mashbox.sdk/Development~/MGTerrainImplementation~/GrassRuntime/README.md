# Cotswold runtime grass optimization — 2026-09-28

Saved scene: `D:/MappyX/Assets/Maps/Cotswold Ridge/[00] SCENES/COTSWOLD RIDGE [MATT] [V02].unity`.

Runtime GPU grass now has a separate shadow distance. This scene uses 60 m: full density of shadow casters through 30 m, followed by a smooth population fade to 60 m. Distance is measured to each detail cell's nearest bounds point, preserving nearby coverage. Visible grass density (0.175), draw distance (250 m), density LOD, materials, meshes and tree shadow settings were preserved. Other scenes default to unlimited shadows (0).

The new control is Terrain Word > MG Terrain World > Settings > Quality > Dense Detail Shadow Distance. Set it to 0 to restore the former unlimited grass-shadow behavior. Dense Detail Shadows remains enabled.

## Measured runtime results

Unity 6000.4.12f1, HDRP, DX11, RTX 5090. Actual Game camera CBBrain at 2560 × 1440 in Play Mode. Each stage warms for 1,200 rendered frames, then measures 600. Numbers are wall-clock intervals between the Game camera's end-render callbacks; FPS is 1,000 / mean milliseconds. These are Play Mode measurements, not standalone build or isolated GPU timings.

| Final verification | Mean ms | Equivalent FPS | p95 ms | Mean visible instances |
| --- | ---: | ---: | ---: | ---: |
| Original shadows, stationary | 5.38 | 185.8 | 6.39 | 41,422 |
| Original shadows, repeat | 5.40 | 185.1 | 6.51 | 41,422 |
| 60 m shadows, stationary | 4.45 | 224.8 | 5.33 | 41,422 |
| Original shadows, moving/turning | 5.41 | 184.8 | 7.00 | 39,337.27 |
| 60 m shadows, moving/turning | 4.63 | 216.0 | 6.14 | 39,337.27 |
| 60 m shadows, optional indirect renderer | 4.46 | 224.4 | 5.28 | 41,422 |

Approximately 21% more FPS stationary and 17% more FPS during the matched motion test. At the stationary viewpoint, only 8,740 grass instances need shadow submission instead of the full visible population. Motion uses an 8 m forward excursion plus a ±25° yaw sweep, with Cinemachine temporarily disabled and restored afterward.

Earlier comparisons found indirect drawing and GPU culling did not reliably improve this view; they remain off. Completely removing grass shadows improved the frame time further, but the selected configuration retains nearby shadows. 40 m saved almost no additional time versus 60 m in the distance comparison. The grass remains dense in the saved before/after captures.

## Implementation and checks

- Separate light-view BRG commands reuse existing resident GPU instance transforms. Camera visibility/density is unaffected.
- Shadow selection is cached while the camera and visible population remain stable. Moving cameras rebuild only when the selected population changes.
- Deterministic per-texel prefixes ensure selected shadow casters are also visible grass instances, including reduced resident/camera populations.
- World and standalone terrain BRG paths both support the selection. Tree populations are exempt. Selection is cleared when visibility/resources are cleared; native shadow buffers are disposed and counted in memory statistics.
- The permanent validator is available at Tools > MashBox > MG Terrain > Validation > Detail Shadows. It passed near/far fade, tree preservation, unlimited mode, exhaustive uneven-texel population/subset tests, and visibility invalidation.
- Live direct-renderer checks passed index-subset and contiguous group checks in the actual scene; the optional indirect path passed group-count checks and rendered successfully. The indirect path's GPU-visible list is not read back for subset validation.
- Unity compiled successfully. The existing missing-script warning remains unrelated to this change.

`Measurements/verify2-*` contains the final checks. `cap2-*` contains the earlier 40/60/90 m comparison. `initial-*motion` was an early probe that updated the camera too late and must not be used to assess movement performance. Final `verify2-*motion` uses LateUpdate before terrain preparation.

The temporary runtime probe was removed from editor compilation; its disabled source is retained here for reproducibility. Source backups are the `.before` files. A full pre-save scene backup is at `%TEMP%/mg-grass-runtime/Cotswold-before-shadow-save.unity`.

The result improves runtime rendering cost, but does not establish doubled FPS or eliminate all streaming/editor hitches. Individual measured frame outliers still reached approximately 13–15 ms. A standalone-build benchmark and longer traversal across new terrain cells remain unmeasured.
