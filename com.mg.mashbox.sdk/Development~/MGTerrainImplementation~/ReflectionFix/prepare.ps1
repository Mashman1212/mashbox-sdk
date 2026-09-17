$root = 'D:\MappyX\Assets\MGTerrainImplementation~\ReflectionFix'
function Replace-Once($file, $old, $new) {
    $path = Join-Path $root $file
    $content = [IO.File]::ReadAllText($path)
    if (-not $content.Contains($old)) { throw "Missing anchor in $file" }
    [IO.File]::WriteAllText($path, $content.Replace($old, $new))
}
Replace-Once 'MGTerrain.cs' '            Matrix4x4 localToWorld = transform.localToWorldMatrix;' @'
            if (camera.cameraType == CameraType.Reflection)
            {
                RenderCachedReflectionDetails(camera);
                return;
            }
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
'@
Replace-Once 'MGTerrain.World.cs' '            m_WorldPendingDraw = false;' @'
            m_WorldPendingDraw = false;
            // Captures consume resident data, never the streaming or world budgets.
            if (camera != null && camera.cameraType == CameraType.Reflection)
            {
                RenderInstances(camera);
                return;
            }
'@
Replace-Once 'MGTerrain.BRG.cs' '                    if (destination == offset) continue;' @'
                    // Reflection faces can see groups outside the gameplay frustum.
                    GetOrRegisterBrgMesh(group.batch.mesh);
                    GetOrRegisterBrgMaterial(group.batch.material);
                    if (destination == offset) continue;
'@
Replace-Once 'MGTerrain.BRG.cs' '            bool cameraView = cullingContext.viewType == BatchCullingViewType.Camera;' @'
            if (IsResidentReflectionView(cullingContext))
            {
                int direct = 0, indirect = 0, visible = 0, range = 0;
                CountReflectionCommands(cullingContext, ref direct, ref visible);
                if (direct == 0) return default;
                output->drawCommandCount = output->drawRangeCount = direct;
                output->visibleInstanceCount = visible;
                output->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(sizeof(BatchDrawCommand) * (long)direct, 8, Allocator.TempJob);
                output->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(sizeof(BatchDrawRange) * (long)direct, 8, Allocator.TempJob);
                output->visibleInstances = (int*)UnsafeUtility.Malloc(sizeof(int) * (long)visible, 8, Allocator.TempJob);
                direct = visible = 0;
                WriteReflectionCommands(cullingContext, output, ref direct, ref visible, ref range);
                return default;
            }
            bool cameraView = cullingContext.viewType == BatchCullingViewType.Camera;
'@
Replace-Once 'MGTerrainWorld.BRG.cs' '            if (!WorldHasCommands(context)) return;' @'
            if (IsResidentReflectionView(context))
            {
                CountReflectionCommands(context, ref direct, ref visible);
                return;
            }
            if (!WorldHasCommands(context)) return;
'@
# The writer needs its own branch, rather than the count branch inserted above.
Replace-Once 'MGTerrainWorld.BRG.cs' @'
            ref int direct, ref int indirect, ref int visible, ref int range)
        {
            if (IsResidentReflectionView(context))
            {
                CountReflectionCommands(context, ref direct, ref visible);
'@ @'
            ref int direct, ref int indirect, ref int visible, ref int range)
        {
            if (IsResidentReflectionView(context))
            {
                WriteReflectionCommands(context, output, ref direct, ref visible, ref range);
'@
