# World Border Proxies

Open **MashBox SDK > Map Tools > World Borders**. The MashBox > Map Tools > World Border Tool menu opens the same SDK tab.

1. Choose **Add Wall Proxy**, or **Create Four Walls** for a starter perimeter.
2. Select a wall in the scene or tool. Use Unity Move, Rotate and Scale; duplicate with Ctrl+D.
3. The proxy is a unit box with its pivot at the centre: scale **X = width**, **Y = height**, **Z = thickness** in metres under an unscaled parent. Walls may be placed independently, including angled walls. Keep rotated walls under an unscaled group to avoid shear.
4. Edit appearance and performance settings in the Inspector or SDK tab. Multiple selected walls can be edited together. New walls use the packaged hologram material, original MashBox logo/stripe albedo, white tint and 5.12 m pattern repeat.
5. Save and export the scene normally. Authoring stores one `MBWorldBorderWall` SDK component per wall and its material reference. Meshes, renderers, LOD groups and collision are generated when the proxy becomes active in play mode.

The four-wall starter is 1,024 m square with 128 m height and 0.5 m thickness. At the default 128 m tile size it produces 32 runtime visual tiles and four colliders. Add Wall starts with one 128 x 128 x 0.5 m proxy.

## Runtime behaviour

The public SDK runtime assembly owns generation; no ProjectX-specific spawner is required. The host game must ship the updated SDK containing `MBWorldBorderWall` before loading maps that use it. An asset bundle does not deliver new compiled C# code to an older game.

Each enabled proxy creates a private material, small quad meshes, one-level LOD groups and one independent BoxCollider. UVs are continuous across the wall's tiles. LOD culling removes distant renderers without disabling collision. There is no per-frame wall script or material swap. Disabling/removing a proxy releases its generated objects, meshes and material; enabling it again regenerates them. The authored transform is read at generation time. Runtime editing code must call `RebuildRuntime()` after changing transforms or settings.

The shader targets HDRP. The package contains the default material, shader and exact source albedo, so new proxies do not depend on Cotswold assets. These are explicit scene dependencies rather than runtime editor lookups or `Shader.Find` fallbacks. The default material retains the double-sided shader variant for player builds.

LOD thresholds cover the configured maximum **vertical** field of view and minimum LOD bias, with a margin for the full tile radius. Defaults are 100 degrees, bias 1 and a 5 m margin. Configure these for the game's actual camera/quality range. Orthographic cameras are not covered. Larger tiles reduce renderer/LOD overhead; smaller tiles reduce nearby transparent overdraw. Choose sizes using CPU/GPU profiling in a representative game build; no measured performance gain is claimed.

## Existing baked borders

Select the root made by the previous SDK border builder, then choose **Convert selected baked border to proxies**. The operation replaces its owned Visual Tiles / Collision Walls with one proxy per collision wall, keeps the root and unrelated children, and supports Undo/Redo. Geometry assets from old generations remain intact for Undo and other references. Conversion does not automatically affect older hand-authored Cotswold planes or arbitrary map colliders.

Previous profile assets and builder code remain readable for existing projects; the current SDK window authors scene proxies directly. Existing scenes are not silently converted.

## Validation

**Tools > MashBox > World Borders > Validate Runtime Proxies** exercises generation in an isolated editor scene: proxy-only scene serialization and package dependencies, runtime tile/collider counts, LOD margins, collision with forced visual culling, rotated/scaled parents, UV seams, cleanup, input rejection and baked-to-proxy conversion with Undo/Redo. Results are recorded in `Development~/WorldBorders/proxy-validation.txt`. This test does not enter the user's open map into Play Mode or benchmark GPU frame time.

The separate headless Play Mode probe validates automatic activation, idempotent startup, disable/re-enable, runtime resize, object destruction and additive-scene unload. It uses a minimal shader to isolate lifecycle behaviour; the main editor tests check the actual HDRP artwork. Run RuntimeProbe/RunRuntimeProbe.ps1 with -UnityEditor pointing to Unity.exe. Its latest result is in runtime-probe.txt.

