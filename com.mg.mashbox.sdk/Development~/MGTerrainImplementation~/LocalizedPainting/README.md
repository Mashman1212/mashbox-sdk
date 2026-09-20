# Localized density painting

Brush map copies are assigned directly after Undo registration, avoiding component-wide OnValidate invalidation. This assignment API is specifically for identical copies or neutral new paint maps; arbitrary map replacement still requires invalidation.

Region refresh retains all other layers' candidate caches and all non-overlapping generated cells. The edited layer updates occupancy only around the brush, retains geometry bounds, and rebuilds its candidate list so newly occupied cells appear and erased cells disappear with a stationary camera. Size interpolation retains the existing one-sample safety margin. Dabs that change no samples do not schedule a refresh.

Validation: runtime and editor assemblies compiled with Unity's project compiler response files. The preview-scene regression passed all four checks in validation.txt. Run again using Tools > MashBox > MG Terrain > Validate Localized Density Painting. Tests do not paint or save the user's scene.

General settings changes and Undo/Redo retain their existing full invalidation path. No frame-time benchmark was performed.

The before files are snapshots for this change only; do not reinstall them over later terrain changes. Implementation is installed in the linked MashBox SDK terrain runtime/editor files.
