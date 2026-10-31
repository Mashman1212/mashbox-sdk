using System;
using UnityEngine;

namespace MG.GodSurface
{
    // Source materials/textures live in an editor recipe as GUID strings, never in this runtime asset.
    public sealed class MGSurfaceLibrary : ScriptableObject
    {
        public const string ShaderName = "MGShaders/HDRP/God Surface";
        [Serializable] public sealed class Bank
        {
            public Material sharedMaterial;
            public Texture2D parameters;
            public Texture2DArray[] arrays;
            public Material[] previewMaterials;
            public string[] surfaceNames;
        }
        public Bank[] banks = Array.Empty<Bank>();
        public MGSurfaceGeometry geometry;
        public int resolution;
        public bool compressed;
        public long TexturePayloadBytes
        {
            get
            {
                long bytes = 0;
                foreach (var bank in banks) foreach (var array in bank.arrays)
                    for (int mip = 0; mip < array.mipmapCount; mip++)
                    {
                        long width = Math.Max(1, array.width >> mip), height = Math.Max(1, array.height >> mip);
                        bytes += array.format == TextureFormat.BC7 ? ((width + 3) / 4) * ((height + 3) / 4) * 16 * array.depth : width * height * 4 * array.depth;
                    }
                return bytes;
            }
        }
        public bool TryResolve(Material preview, out Material shared, out float id)
        {
            shared = null; id = 0;
            if (!preview || !preview.shader || preview.shader.name != ShaderName) return false;
            foreach (var bank in banks)
            {
                if (!bank.sharedMaterial || preview.GetTexture("_MGSurfaceParameters") != bank.parameters) continue;
                id = preview.GetFloat("_MGSurfaceID");
                if (id < 0 || id >= bank.surfaceNames.Length || id != Mathf.Floor(id)) return false;
                shared = bank.sharedMaterial; return true;
            }
            return false;
        }
    }
}
