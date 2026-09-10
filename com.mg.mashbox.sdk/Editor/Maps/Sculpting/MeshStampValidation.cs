using System;
using MashBoxSDK.Maps.Sculpting;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public static class MeshStampValidation
    {
        [MenuItem("MashBox/Validation/Validate Mesh Stamp Sampling")]
        public static void Run()
        {
            var source = new Mesh();
            var destination = new Mesh();
            var target = new GameObject("Mesh stamp validation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                // A ramp: top height is (x + 1) / 2, with both windings covered.
                source.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 1, -1),
                    new Vector3(-1, 0, 1), new Vector3(1, 1, 1) };
                source.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                source.RecalculateBounds();
                var brush = new MeshStampBrush(source);
                Check(brush.TryHeight(0, 0, out float middle) && Mathf.Abs(middle - .5f) < .0001f, "Ramp interpolation");
                Check(!brush.TryHeight(.9f, 0, out _), "Outside mesh footprint");

                target.transform.position = new Vector3(10, 3, 20);
                target.transform.localScale = new Vector3(2, 3, 4);
                destination.vertices = new[] { Vector3.zero, new Vector3(.25f, 0, 0), new Vector3(10, 0, 0) };
                destination.triangles = new[] { 0, 1, 2 };
                var filter = target.AddComponent<MeshFilter>();
                filter.sharedMesh = destination;
                Vector3 center = target.transform.position;
                var samples = brush.Sample(filter, center, 2, 4, 0, .5f, 0);
                Check(samples.Length == 2, "Footprint exclusion");
                Check(Mathf.Abs(target.transform.TransformVector(samples[0].delta).y - 1f) < .0001f, "Strength and transformed target");
                Check(samples[0].delta.x == 0 && samples[0].delta.z == 0, "Preserve horizontal coordinates");
                var inverted = brush.Sample(filter, center, 2, 4, 0, -.5f, 0);
                Check((samples[0].delta + inverted[0].delta).sqrMagnitude < .000001f, "Ctrl inversion");
                var rotated = brush.Sample(filter, center, 2, 4, 180, .5f, 0);
                Check(rotated[1].delta.y < samples[1].delta.y, "Stamp rotation");
                var softened = brush.Sample(filter, center, 2, 4, 0, .5f, 2);
                Check(softened[1].delta.y < samples[1].delta.y, "Falloff");
                Debug.Log("Mesh stamp sampling PASS: interpolation, footprint, strength, transform, inversion, rotation, falloff.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(destination);
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Mesh stamp validation failed: " + message);
        }
    }
}
