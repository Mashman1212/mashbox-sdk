using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool clothPaintXray, clothPaintThrough;
        private Mesh clothWeightSurface;
        private Material clothWeightSurfaceMaterial;
        private Color[] clothWeightColors;
        private readonly List<Vector3> clothWeightPositions = new List<Vector3>();
        internal static float ClothPaintFalloff(Vector3 offset, Vector3 normal, Vector3 viewDirection, float radius, bool through)
        {
            if (radius <= 0) return 0;
            if (!through && Mathf.Abs(Vector3.Dot(offset, normal)) > radius * .2f) return 0;
            float distance = through ? Vector3.ProjectOnPlane(offset, viewDirection).magnitude : offset.magnitude;
            return Mathf.Clamp01(1 - distance / radius);
        }
        private void DrawClothWeightSurface(IReadOnlyList<Vector3> vertices, Matrix4x4 matrix)
        {
            if (!clothWeightSurfaceMaterial)
            {
                var shader = Shader.Find("Hidden/MashBox/ClothWeightOverlay");
                if (!shader) return;
                clothWeightSurfaceMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (!clothWeightSurface) clothWeightSurface = new Mesh { name = "Cloth weight overlay", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            clothWeightSurface.Clear(); clothWeightPositions.Clear();
            for (int i = 0; i < vertices.Count; i++) clothWeightPositions.Add(vertices[i]);
            if (clothWeightColors == null || clothWeightColors.Length != vertices.Count) clothWeightColors = new Color[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                float weight = Mathf.Clamp01(clothProfile.motion[i] / .3f);
                clothWeightColors[i] = weight < .5f ? Color.Lerp(new Color(.05f,.2f,1),Color.yellow,weight*2) : Color.Lerp(Color.yellow,new Color(1,.12f,.02f),weight*2-1);
            }
            clothWeightSurface.SetVertices(clothWeightPositions);
            clothWeightSurface.triangles = clothTriangles;
            clothWeightSurface.colors = clothWeightColors;
            clothWeightSurface.RecalculateNormals();
            void Draw(CompareFunction depth, float alpha)
            {
                clothWeightSurfaceMaterial.SetInt("_ZTest", (int)depth);
                clothWeightSurfaceMaterial.SetFloat("_Alpha", alpha);
                if (clothWeightSurfaceMaterial.SetPass(0)) Graphics.DrawMeshNow(clothWeightSurface, matrix);
            }
            if (clothPaintXray) Draw(CompareFunction.Greater, .18f);
            Draw(CompareFunction.LessEqual, .92f);
        }
        private void DisposeClothWeightSurface()
        {
            if (clothWeightSurface) DestroyImmediate(clothWeightSurface);
            if (clothWeightSurfaceMaterial) DestroyImmediate(clothWeightSurfaceMaterial);
            clothWeightSurface = null; clothWeightSurfaceMaterial = null; clothWeightColors = null; clothWeightPositions.Clear();
        }
    }
}