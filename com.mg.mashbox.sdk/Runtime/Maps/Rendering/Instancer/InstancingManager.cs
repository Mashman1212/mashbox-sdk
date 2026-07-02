using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Map.Rendering.Instancer
{

    public class InstancingManager : MonoBehaviour
    {
        private static InstancingManager instance;
        public static InstancingManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<InstancingManager>();

                    if (instance == null)
                    {
                        GameObject go = new GameObject("InstancingManager");
                        instance = go.AddComponent<InstancingManager>();
                    }
                }

                return instance;
            }
        }

        private List<InstanceGroup> groups = new();
        private List<Cell> cells = new();

        private bool dirty = true;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void RegisterGroup(InstanceGroup group)
        {
            groups.Add(group);
            dirty = true;
        }

        public void UnregisterGroup(InstanceGroup group)
        {
            groups.Remove(group);
            dirty = true;
        }

        void Update()
        {
            if (dirty)
            {
                Rebuild();
                dirty = false;
            }

            Render();
        }

        void Rebuild()
        {
            cells.Clear();

            Dictionary<(Mesh, Material), List<Matrix4x4>> temp = new();

            // Gather from all groups
            foreach (var group in groups)
            {
                var renderers = group.GetRenderers();

                foreach (var r in renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) continue;

                    var key = (mf.sharedMesh, r.sharedMaterial);

                    if (!temp.ContainsKey(key))
                        temp[key] = new List<Matrix4x4>();

                    temp[key].Add(r.transform.localToWorldMatrix);
                }
            }

            // Create a single cell for now (we'll split later)
            Cell cell = new Cell();

            foreach (var kvp in temp)
            {
                var batch = new Batch
                {
                    mesh = kvp.Key.Item1,
                    material = kvp.Key.Item2
                };

                var matrices = kvp.Value;
                int batchSize = 1023;

                for (int i = 0; i < matrices.Count; i += batchSize)
                {
                    int length = Mathf.Min(batchSize, matrices.Count - i);

                    Matrix4x4[] matrixChunk = new Matrix4x4[length];
                    matrices.CopyTo(i, matrixChunk, 0, length);

                    Vector3[] positions = GetPositions(matrixChunk);

                    var sh = new SphericalHarmonicsL2[length];
                    var occlusion = new Vector4[length];

                    LightProbes.CalculateInterpolatedLightAndOcclusionProbes(
                        positions,
                        sh,
                        occlusion
                    );

                    var mpb = new MaterialPropertyBlock();
                    mpb.CopySHCoefficientArraysFrom(sh);
                    mpb.CopyProbeOcclusionArrayFrom(occlusion);

                    batch.matrixChunks.Add(matrixChunk);
                    batch.mpbChunks.Add(mpb);
                }

                cell.batches.Add(batch);
            }

            cells.Add(cell);

            // Destroy original objects (after baking)
            foreach (var group in groups)
            {
                var renderers = group.GetRenderers();

                foreach (var r in renderers)
                    Destroy(r.gameObject);
            }
        }

        void Render()
        {
            foreach (var cell in cells)
            {
                foreach (var batch in cell.batches)
                {
                    for (int i = 0; i < batch.matrixChunks.Count; i++)
                    {
                        Graphics.DrawMeshInstanced(
                            batch.mesh,
                            0,
                            batch.material,
                            batch.matrixChunks[i],
                            batch.matrixChunks[i].Length,
                            batch.mpbChunks[i],
                            ShadowCastingMode.On,
                            true,
                            gameObject.layer,
                            null,
                            LightProbeUsage.CustomProvided
                        );
                    }
                }
            }
        }

        Vector3[] GetPositions(Matrix4x4[] matrices)
        {
            Vector3[] positions = new Vector3[matrices.Length];

            for (int i = 0; i < matrices.Length; i++)
                positions[i] = matrices[i].GetColumn(3);

            return positions;
        }
    }
}