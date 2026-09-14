using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class AuthoringRig
    {
        private readonly List<Mesh> outfitMeshes = new List<Mesh>();
        public int HiddenBodyTriangles { get; private set; }

        private void BuildOutfit(GameObject body, GameObject[] clothing, OutfitOptions[] options)
        {
            var bodies = new List<SkinnedMeshRenderer>();
            var covers = new List<(SkinnedMeshRenderer skin, float distance)>();
            if (body) bodies.AddRange(AttachBody(body));
            for (int i = 0; clothing != null && i < clothing.Length; i++)
            {
                if (!clothing[i]) continue;
                var settings = options != null && i < options.Length ? options[i] : default;
                if (settings.role == OutfitRole.Hat) { AttachHat(clothing[i], settings); continue; }
                var skins = AttachBody(clothing[i]);
                if (settings.doubleSided) foreach (var skin in skins) doubleSidedRenderers.Add(skin);
                if (settings.role == OutfitRole.Body) bodies.AddRange(skins);
                else if (settings.hideBody)
                    foreach (var skin in skins) covers.Add((skin, settings.cutoutDistance > 0 ? settings.cutoutDistance : 0.03f));
            }
            if (covers.Count > 0 && bodies.Count == 0)
                throw new InvalidOperationException("Assign a preview body or mark a mesh as Body before enabling body cutouts.");
            if (covers.Count > 0) CutCoveredBody(bodies, covers);
        }

        private void AttachHat(GameObject source, OutfitOptions settings)
        {
            var head = Animator ? Animator.GetBoneTransform(HumanBodyBones.Head) : Bones.FirstOrDefault(t => BoneName(t.name).Equals("Head", StringComparison.OrdinalIgnoreCase));
            if (!head) throw new InvalidOperationException("A Hat requires a mapped head bone.");
            if (source.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new InvalidOperationException("Hat prefab contains no renderers: " + source.name);
            var holder = new GameObject("Hat • " + source.name);
            holder.transform.SetParent(head, false);
            holder.transform.localPosition = settings.position;
            holder.transform.localRotation = Quaternion.Euler(settings.rotation);
            holder.transform.localScale = source.transform.localScale * (settings.scale > 0 ? settings.scale : 1);
            var map = new Dictionary<Transform, Transform> { [source.transform] = holder.transform };
            CloneChildren(source.transform, holder.transform, map);
            CopyRenderers(source, map);
            if (settings.doubleSided)
                foreach (var renderer in holder.GetComponentsInChildren<Renderer>(true)) doubleSidedRenderers.Add(renderer);
        }

        private void CutCoveredBody(List<SkinnedMeshRenderer> bodies, List<(SkinnedMeshRenderer skin, float distance)> covers)
        {
            var colliders = new List<(MeshCollider collider, float distance)>();
            var temporaryMeshes = new List<Mesh>();
            try
            {
                foreach (var cover in covers)
                {
                    var baked = new Mesh(); temporaryMeshes.Add(baked);
                    cover.skin.BakeMesh(baked);
                    var vertices = baked.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = cover.skin.transform.TransformPoint(vertices[i]);
                    baked.vertices = vertices; baked.RecalculateBounds();
                    var go = new GameObject("Temporary outfit coverage");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, Scene);
                    var collider = go.AddComponent<MeshCollider>();
                    colliders.Add((collider, cover.distance));
                    collider.sharedMesh = baked;
                }
                // Coverage is calculated once in the source pose, never while dragging or scrubbing.
                Physics.SyncTransforms();
                foreach (var body in bodies)
                {
                    if (!body.sharedMesh) continue;
                    var baked = new Mesh(); temporaryMeshes.Add(baked);
                    body.BakeMesh(baked);
                    var vertices = baked.vertices; var normals = baked.normals;
                    if (normals.Length != vertices.Length) { baked.RecalculateNormals(); normals = baked.normals; }
                    var hidden = new bool[vertices.Length];
                    var normalMatrix = body.transform.localToWorldMatrix.inverse.transpose;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 point = body.transform.TransformPoint(vertices[i]);
                        Vector3 normal = normalMatrix.MultiplyVector(normals[i]).normalized;
                        foreach (var cover in colliders)
                        {
                            // Cast inward to hit the outside face of open shirts and trousers.
                            float reach = cover.distance;
                            if (cover.collider.Raycast(new Ray(point + normal * reach, -normal), out _, reach * 2))
                            { hidden[i] = true; break; }
                        }
                    }
                    var clipped = Object.Instantiate(body.sharedMesh);
                    clipped.name = body.sharedMesh.name + " (Preview cutout)";
                    clipped.hideFlags = HideFlags.HideAndDontSave;
                    outfitMeshes.Add(clipped);
                    for (int sub = 0; sub < clipped.subMeshCount; sub++)
                    {
                        if (clipped.GetTopology(sub) != MeshTopology.Triangles) continue;
                        var triangles = clipped.GetTriangles(sub);
                        var visible = new List<int>(triangles.Length);
                        for (int t = 0; t < triangles.Length; t += 3)
                        {
                            if (hidden[triangles[t]] && hidden[triangles[t + 1]] && hidden[triangles[t + 2]])
                            { HiddenBodyTriangles++; continue; }
                            visible.Add(triangles[t]); visible.Add(triangles[t + 1]); visible.Add(triangles[t + 2]);
                        }
                        clipped.SetTriangles(visible, sub, false);
                    }
                    body.sharedMesh = clipped;
                }
            }
            finally
            {
                foreach (var cover in colliders) if (cover.collider) Object.DestroyImmediate(cover.collider.gameObject);
                foreach (var mesh in temporaryMeshes) if (mesh) Object.DestroyImmediate(mesh);
            }
        }
    }
}
