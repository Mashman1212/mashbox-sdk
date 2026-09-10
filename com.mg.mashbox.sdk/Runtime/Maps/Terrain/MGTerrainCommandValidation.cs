#if UNITY_EDITOR
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    // Keep pointer access in MashBoxSDK, whose asmdef enables unsafe compilation.
    // The editor validation calls this managed signature without requiring the
    // creator's predefined editor assembly to enable unsafe code. Excluded from players.
    public static class MGTerrainCommandValidation
    {
        public static unsafe void ValidateAndRelease(BatchCullingOutputDrawCommands result, int expected)
        {
            try
            {
                Check(result.drawCommandCount == expected && result.drawRangeCount == expected, "Wrong shared command count.");
                Check(result.visibleInstanceCount == expected * 3, "Wrong shared visible count.");
                for (int i = 0; i < expected; i++)
                {
                    Check(result.drawCommands[i].visibleOffset == i * 3, "Chunk visible offsets overlap.");
                    Check(result.drawRanges[i].drawCommandsBegin == i, "Draw range references the wrong chunk.");
                    for (int j = 0; j < 3; j++)
                        Check(result.visibleInstances[i * 3 + j] == j, "Per-batch indices were incorrectly rebased.");
                }
            }
            finally
            {
                UnsafeUtility.Free(result.drawCommands, Allocator.TempJob);
                UnsafeUtility.Free(result.indirectDrawCommands, Allocator.TempJob);
                UnsafeUtility.Free(result.drawRanges, Allocator.TempJob);
                UnsafeUtility.Free(result.visibleInstances, Allocator.TempJob);
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
#endif
