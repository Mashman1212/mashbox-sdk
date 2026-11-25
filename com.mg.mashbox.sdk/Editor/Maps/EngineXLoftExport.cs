#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.Painting;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.MapTools
{
    // Writes the selected MultiSplineLoft structures (rails, settings, materials, UV spline,
    // shoulder modifier and bend splines, vertex paint, mesh sculpt) as one EngineX.Lofts package.
    // Read-only: never edits the scene. Import it in EngineX with Create > Loft > Import from Unity.
    public static class EngineXLoftExport
    {
        [Serializable] public sealed class Knot { public float[] p, i, o; public int m; }
        [Serializable] public sealed class Rail { public string name, parent; public float[] position; public bool closed; public List<Knot> knots = new List<Knot>(); }
        [Serializable] public sealed class Key { public float t, v, inSlope, outSlope, inWeight, outWeight; public int weighted, tangentMode, inStep, outStep; }
        [Serializable] public sealed class Curve { public List<Key> keys = new List<Key>(); }
        [Serializable] public sealed class Settings
        {
            public int samplesAlong, segmentsAcross, across, resolution, alignment, normals, reference, maxDistanceSamples, uvChannel, uvDirection, uvPointCount, resolutionPointCount;
            public float targetLength, regenerateDelay, colliderLength, normalContact, uvAlong, uvAcross, uvRatio, packedPadding;
            public bool autoRegenerate, closeAlong, closeAcross, capStart, capEnd, doubleSided, collider, flipNormals, matchSideNormals, matchIntersectingNormals, matchUvAspect, packedUv2, generateUvSpline, generateResolutionSpline, smoothDensity;
        }
        [Serializable] public sealed class Source { public int rail; public bool reverse; }
        [Serializable] public sealed class Zone { public float center, radius, density; }
        [Serializable] public sealed class Density { public float path, scale; }
        [Serializable] public sealed class Profile { public bool enabled, highSideOnly; public int segments; public float width, verticalScale, highSideBlend; public Curve height = new Curve(); }
        [Serializable] public sealed class Shoulders { public bool shared, matchTerrainNormals; public List<Profile> profiles = new List<Profile>(); }
        [Serializable] public sealed class PaintStroke { public bool fill, falloff, hasUv; public float radius, strength, alpha; public int anchor, vertexCount; public float[] position, offset, color, uv; }
        [Serializable] public sealed class Delta { public int index; public float[] delta; }
        [Serializable] public sealed class SculptStroke { public int mode, seed, passes, vertexCount; public bool world; public float radius, strength, falloff, targetHeight; public float[] position, direction; public List<Delta> deltas = new List<Delta>(); }
        [Serializable] public sealed class UvPoint
        {
            public float path, width, length, side, along, blendLength, blendWidth, leftOffset, rightOffset, branchWidth, leftScale, rightScale;
            public bool split, flipLeft, flipRight; public float[] anchor;
        }
        [Serializable] public sealed class Uv
        {
            public int channel, axis; public bool smooth, autoPivot, movingKnots;
            public float pivot, falloff, alongSensitivity, sideSensitivity;
            public List<Knot> knots = new List<Knot>(); public List<UvPoint> points = new List<UvPoint>();
        }
        [Serializable] public sealed class KnotProfile { public bool enabled, initialized; public Curve height = new Curve(); }
        [Serializable] public sealed class Edge
        {
            public string name, parent; public int side; public float[] position;
            public List<Knot> knots = new List<Knot>();
            public float positionInfluence; public Curve across = new Curve(); public int generatedPointCount;
            public List<float> basePositions = new List<float>(); public List<KnotProfile> knotProfiles = new List<KnotProfile>();
        }
        [Serializable] public sealed class Loft
        {
            public string name, parent; public float[] position, rotation, scale;
            public Settings settings = new Settings();
            public List<Source> sources = new List<Source>(); public List<Zone> zones = new List<Zone>(); public List<Density> density = new List<Density>();
            public List<string> materials = new List<string>(), materialNames = new List<string>();
            public bool hasShoulders, hasPaint, hasSculpt, hasUv, sculptCollider;
            public Shoulders shoulders = new Shoulders();
            public List<PaintStroke> paint = new List<PaintStroke>(); public List<SculptStroke> sculpt = new List<SculptStroke>();
            public Uv uv = new Uv(); public List<Edge> edges = new List<Edge>();
            public List<string> warnings = new List<string>();
        }
        [Serializable] public sealed class Package
        {
            public string format = "EngineX.Lofts"; public int version = 1; public string source;
            public List<Rail> rails = new List<Rail>(); public List<Loft> lofts = new List<Loft>();
        }

        [MenuItem("MashBox/Export/Send Selected Lofts to EngineX...")]
        static void ExportSelected() => Export(Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<MultiSplineLoft>(true)));

        [MenuItem("MashBox/Export/Send All Scene Lofts to EngineX...")]
        static void ExportScene() => Export(UnityEngine.Object.FindObjectsByType<MultiSplineLoft>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        static void Export(IEnumerable<MultiSplineLoft> found)
        {
            var lofts = found.Distinct().OrderBy(l => PathOf(l.transform)).ToArray();
            if (lofts.Length == 0) { EditorUtility.DisplayDialog("Send Lofts to EngineX", "Select one or more lofts, or a parent object that contains them.", "OK"); return; }
            var scene = lofts[0].gameObject.scene;
            string file = EditorUtility.SaveFilePanel("Send Lofts to EngineX", "", (string.IsNullOrEmpty(scene.name) ? "Lofts" : scene.name) + " Lofts.json", "json");
            if (string.IsNullOrEmpty(file)) return;
            Package package;
            try { package = Build(lofts, scene.path); }
            catch (Exception error) { EditorUtility.DisplayDialog("Send Lofts to EngineX", error.Message, "OK"); return; }
            File.WriteAllText(file, JsonUtility.ToJson(package));
            int warnings = package.lofts.Sum(l => l.warnings.Count);
            Debug.Log($"Sent {package.lofts.Count} lofts and {package.rails.Count} rails to {file}" + (warnings > 0 ? $" with {warnings} warnings; see each loft's warnings in the file." : "."));
            EditorUtility.RevealInFinder(file);
        }

        public static Package Build(IReadOnlyList<MultiSplineLoft> lofts, string sourcePath)
        {
            var package = new Package { source = sourcePath };
            var rails = new Dictionary<(int, int), int>();
            var shoulders = FindAll<LoftShoulderModifier>();
            var paints = FindAll<VertexPaintModifier>();
            var sculpts = FindAll<MeshSculptModifier>();
            var uvs = FindAll<UVSpline>();
            var edges = FindAll<LoftShoulderEdgeSpline>();
            foreach (var loft in lofts)
            {
                var t = loft.transform;
                var item = new Loft { name = loft.name, parent = PathOf(t.parent), position = V(t.position), rotation = NativeEuler(t.rotation), scale = V(t.lossyScale) };
                var so = new SerializedObject(loft);
                WriteSettings(so, item.settings);
                foreach (var source in loft.Sources)
                {
                    if (source == null || !source.IsValid) { item.warnings.Add("Skipped a missing or invalid source spline."); continue; }
                    var key = (source.container.GetInstanceID(), source.splineIndex);
                    if (!rails.TryGetValue(key, out int index))
                    {
                        index = package.rails.Count; rails[key] = index;
                        var c = source.container.transform;
                        var spline = source.container.Splines[source.splineIndex];
                        var rail = new Rail { name = source.container.Splines.Count > 1 ? $"{c.name} {source.splineIndex}" : c.name, parent = PathOf(c.parent), position = V(c.position), closed = spline.Closed };
                        rail.knots = Knots(spline, p => c.TransformPoint(p) - c.position, d => c.TransformVector(d));
                        package.rails.Add(rail);
                    }
                    item.sources.Add(new Source { rail = index, reverse = source.reverse });
                }
                var zones = so.FindProperty("m_ResolutionZones");
                for (int i = 0; i < zones.arraySize; i++)
                {
                    var z = zones.GetArrayElementAtIndex(i);
                    item.zones.Add(new Zone { center = z.FindPropertyRelative("center").floatValue, radius = z.FindPropertyRelative("radius").floatValue, density = z.FindPropertyRelative("density").floatValue });
                }
                if (so.FindProperty("m_ResolutionSpline").objectReferenceValue is LoftResolutionSpline resolution)
                {
                    var rs = new SerializedObject(resolution);
                    item.settings.smoothDensity = rs.FindProperty("m_SmoothInterpolation").boolValue;
                    var points = rs.FindProperty("m_ControlPoints");
                    for (int i = 0; i < points.arraySize; i++)
                    {
                        var p = points.GetArrayElementAtIndex(i);
                        item.density.Add(new Density { path = p.FindPropertyRelative("pathT").floatValue, scale = p.FindPropertyRelative("resolutionScale").floatValue });
                    }
                }
                var renderer = loft.GetComponent<MeshRenderer>();
                if (renderer != null)
                    foreach (var material in renderer.sharedMaterials)
                    {
                        string path = material != null ? AssetDatabase.GetAssetPath(material) : null;
                        item.materials.Add(string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path));
                        item.materialNames.Add(material != null ? material.name : "");
                    }
                var filter = loft.GetComponent<MeshFilter>();
                var shoulder = shoulders.FirstOrDefault(s => Ref(s, "m_Loft") == loft);
                if (shoulder != null) WriteShoulders(shoulder, item);
                var paint = paints.FirstOrDefault(s => Ref(s, "m_LinkedLoft") == loft || (filter != null && Ref(s, "m_Target") == filter));
                if (paint != null) WritePaint(paint, item);
                var sculpt = sculpts.FirstOrDefault(s => Ref(s, "m_LinkedLoft") == loft || (filter != null && Ref(s, "m_Target") == filter));
                if (sculpt != null) WriteSculpt(sculpt, item);
                var uv = filter != null ? uvs.FirstOrDefault(s => Ref(s, "m_Target") == filter) : null;
                if (uv != null) WriteUv(uv, t, item);
                if (shoulder != null)
                    foreach (var edge in edges.Where(e => e.Modifier == shoulder))
                        WriteEdge(edge, item);
                if (so.FindProperty("m_HeightOverlayModifier").objectReferenceValue != null)
                    item.warnings.Add("Height overlay modifier has no EngineX counterpart and was not sent.");
                if (item.settings.packedUv2 || item.settings.generateUvSpline || item.settings.generateResolutionSpline)
                    item.warnings.Add("Packed UV2 and generated helper splines are rebuilt natively; their generation flags are turned off on import.");
                package.lofts.Add(item);
            }
            return package;
        }

        static void WriteSettings(SerializedObject so, Settings s)
        {
            int I(string n) => so.FindProperty(n).intValue;
            float F(string n) => so.FindProperty(n).floatValue;
            bool B(string n) => so.FindProperty(n).boolValue;
            s.samplesAlong = I("m_SamplesAlong"); s.segmentsAcross = I("m_SegmentsAcross");
            s.across = so.FindProperty("m_AcrossInterpolation").enumValueIndex; s.resolution = so.FindProperty("m_AlongResolutionMode").enumValueIndex;
            s.alignment = so.FindProperty("m_AlongAlignment").enumValueIndex; s.normals = so.FindProperty("m_NormalMode").enumValueIndex;
            s.reference = I("m_AlignmentReferenceSource"); s.targetLength = F("m_TargetSegmentLength"); s.maxDistanceSamples = I("m_MaxDistanceSamples");
            s.autoRegenerate = B("m_AutoRegenerate"); s.regenerateDelay = F("m_AutoRegenerateDelay");
            s.closeAlong = B("m_CloseAlongClosedSplines"); s.closeAcross = B("m_CloseAcrossSplines"); s.capStart = B("m_CapStart"); s.capEnd = B("m_CapEnd");
            s.doubleSided = B("m_DoubleSided"); s.collider = B("m_UpdateMeshCollider"); s.colliderLength = F("m_ColliderChunkLength"); s.flipNormals = B("m_FlipNormals");
            s.matchSideNormals = B("m_MatchSideNormalsToTerrain"); s.matchIntersectingNormals = B("m_MatchTerrainIntersectingFaces"); s.normalContact = F("m_TerrainNormalContactDistance");
            s.uvAlong = F("m_UvScaleAlong"); s.uvAcross = F("m_UvScaleAcross"); s.matchUvAspect = B("m_MatchUv0LengthToWidth"); s.uvRatio = F("m_Uv0AlongRatioMultiplier");
            s.packedUv2 = B("m_GeneratePackedUv2"); s.packedPadding = F("m_PackedUv2Padding");
            s.generateUvSpline = B("m_GenerateUvSplineWithLoft"); s.uvChannel = I("m_UvSplineChannel"); s.uvDirection = so.FindProperty("m_UvSplineDirection").enumValueIndex; s.uvPointCount = I("m_UvSplinePointCount");
            s.generateResolutionSpline = B("m_GenerateResolutionSplineWithLoft"); s.resolutionPointCount = I("m_ResolutionSplinePointCount");
            s.smoothDensity = true;
        }

        static void WriteShoulders(LoftShoulderModifier modifier, Loft item)
        {
            var so = new SerializedObject(modifier);
            item.hasShoulders = true;
            item.shoulders.shared = so.FindProperty("m_UseSharedSideProfile").boolValue;
            item.shoulders.matchTerrainNormals = so.FindProperty("m_MatchOuterNormalsToTerrain").boolValue;
            if (so.FindProperty("m_LayeredBankMaterial").objectReferenceValue != null) item.warnings.Add("Layered bank material is not sent; shoulders use the loft material.");
            foreach (var name in new[] { "m_SharedSideProfile", "m_Left", "m_Right", "m_Start", "m_Finish" })
            {
                var p = so.FindProperty(name);
                var profile = new Profile
                {
                    enabled = p.FindPropertyRelative("enabled").boolValue, highSideOnly = p.FindPropertyRelative("highSideOnly").boolValue,
                    segments = p.FindPropertyRelative("segments").intValue, width = p.FindPropertyRelative("width").floatValue,
                    verticalScale = p.FindPropertyRelative("verticalScale").floatValue, highSideBlend = p.FindPropertyRelative("highSideBlendHeight").floatValue,
                    height = CurveOf(p.FindPropertyRelative("height").animationCurveValue)
                };
                if (profile.enabled && (p.FindPropertyRelative("erosionAmplitude").floatValue > 0 || p.FindPropertyRelative("horizontalErosionAmplitude").floatValue > 0 || p.FindPropertyRelative("generateLayerMasks").boolValue))
                    item.warnings.Add($"{name}: erosion and trail layer masks are not sent.");
                if (profile.enabled && p.FindPropertyRelative("materialOverride").objectReferenceValue != null)
                    item.warnings.Add($"{name}: material override is not sent.");
                item.shoulders.profiles.Add(profile);
            }
        }

        static void WritePaint(VertexPaintModifier modifier, Loft item)
        {
            var strokes = new SerializedObject(modifier).FindProperty("m_Strokes");
            item.hasPaint = true;
            for (int i = 0; i < strokes.arraySize; i++)
            {
                var s = strokes.GetArrayElementAtIndex(i);
                var color = s.FindPropertyRelative("color").colorValue;
                bool anchored = s.FindPropertyRelative("hasTopologyAnchor").boolValue, uv = s.FindPropertyRelative("hasUvAnchor").boolValue;
                var uvValue = s.FindPropertyRelative("uvAnchor").vector2Value;
                if (s.FindPropertyRelative("brushMask.size") is SerializedProperty mask && mask.intValue > 0)
                    item.warnings.Add($"Vertex paint stroke {i + 1}: brush mask is not sent; the stroke uses a round brush.");
                item.paint.Add(new PaintStroke
                {
                    fill = s.FindPropertyRelative("fill").boolValue, falloff = s.FindPropertyRelative("useFalloff").boolValue,
                    radius = s.FindPropertyRelative("radius").floatValue, strength = s.FindPropertyRelative("strength").floatValue, alpha = color.a,
                    anchor = anchored ? s.FindPropertyRelative("anchorVertexIndex").intValue : -1, vertexCount = anchored ? s.FindPropertyRelative("anchorVertexCount").intValue : 0,
                    position = V(s.FindPropertyRelative("localPosition").vector3Value), offset = V(s.FindPropertyRelative("localAnchorOffset").vector3Value),
                    color = new[] { color.r, color.g, color.b }, hasUv = uv, uv = new[] { uvValue.x, 1f - uvValue.y }
                });
            }
        }

        static void WriteSculpt(MeshSculptModifier modifier, Loft item)
        {
            var so = new SerializedObject(modifier);
            var strokes = so.FindProperty("m_Strokes");
            item.hasSculpt = true;
            item.sculptCollider = so.FindProperty("m_UpdateMeshCollider").boolValue;
            for (int i = 0; i < strokes.arraySize; i++)
            {
                var s = strokes.GetArrayElementAtIndex(i);
                if (s.FindPropertyRelative("brushMask.size") is SerializedProperty mask && mask.intValue > 0)
                    item.warnings.Add($"Mesh sculpt stroke {i + 1}: brush mask is not sent; the stroke uses a round brush.");
                var stroke = new SculptStroke
                {
                    mode = s.FindPropertyRelative("mode").enumValueIndex, world = s.FindPropertyRelative("space").enumValueIndex == 0,
                    radius = s.FindPropertyRelative("radius").floatValue, strength = s.FindPropertyRelative("strength").floatValue,
                    falloff = s.FindPropertyRelative("falloff").floatValue, targetHeight = s.FindPropertyRelative("targetHeight").floatValue,
                    seed = s.FindPropertyRelative("noiseSeed").intValue, passes = s.FindPropertyRelative("smoothIterations").intValue,
                    vertexCount = s.FindPropertyRelative("seamVertexCount").intValue,
                    position = V(s.FindPropertyRelative("position").vector3Value), direction = V(s.FindPropertyRelative("direction").vector3Value)
                };
                var seams = s.FindPropertyRelative("seamVertices");
                for (int j = 0; seams != null && j < seams.arraySize; j++)
                {
                    var seam = seams.GetArrayElementAtIndex(j);
                    stroke.deltas.Add(new Delta { index = seam.FindPropertyRelative("index").intValue, delta = V(seam.FindPropertyRelative("delta").vector3Value) });
                }
                item.sculpt.Add(stroke);
            }
        }

        static void WriteUv(UVSpline uv, Transform loft, Loft item)
        {
            var so = new SerializedObject(uv);
            var container = uv.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null) { item.warnings.Add("UV spline has no spline container and was not sent."); return; }
            item.hasUv = true;
            var u = item.uv;
            u.channel = so.FindProperty("m_UvChannel").intValue; u.axis = so.FindProperty("m_LongitudinalAxis").enumValueIndex;
            u.smooth = so.FindProperty("m_SmoothInterpolation").boolValue; u.autoPivot = so.FindProperty("m_AutoCrossPivot").boolValue;
            u.pivot = so.FindProperty("m_CrossPivot").floatValue; u.movingKnots = so.FindProperty("m_MovingKnotsOffsetsUv").boolValue;
            u.falloff = so.FindProperty("m_MoveFalloffPoints").floatValue;
            u.alongSensitivity = so.FindProperty("m_AlongUvPerWorldUnit").floatValue; u.sideSensitivity = so.FindProperty("m_SideUvPerWorldUnit").floatValue;
            var c = uv.transform;
            // EngineX stores the UV spline on the loft, in the loft's local space.
            u.knots = Knots(container.Spline, p => loft.InverseTransformPoint(c.TransformPoint(p)), d => loft.InverseTransformVector(c.TransformVector(d)));
            var points = so.FindProperty("m_ControlPoints");
            for (int i = 0; i < points.arraySize; i++)
            {
                var p = points.GetArrayElementAtIndex(i);
                float F(string n) => p.FindPropertyRelative(n).floatValue;
                bool B(string n) => p.FindPropertyRelative(n).boolValue;
                u.points.Add(new UvPoint
                {
                    path = F("pathT"), width = F("widthScale"), length = F("lengthScale"), side = F("sideOffset"), along = F("alongOffset"),
                    split = B("mirrorSplitToNext"), blendLength = F("mirrorBlendLength"), blendWidth = F("mirrorBlendWidth"),
                    leftOffset = F("mirrorLeftOffset"), rightOffset = F("mirrorRightOffset"), flipLeft = B("flipMirrorLeft"), flipRight = B("flipMirrorRight"),
                    branchWidth = F("mirrorBranchUvWidth"), leftScale = F("mirrorLeftScale"), rightScale = F("mirrorRightScale"),
                    anchor = V(p.FindPropertyRelative("generatedLocalPosition").vector3Value)
                });
            }
        }

        static void WriteEdge(LoftShoulderEdgeSpline edge, Loft item)
        {
            var so = new SerializedObject(edge);
            var t = edge.transform;
            var spline = edge.Container != null ? edge.Container.Spline : null;
            if (spline == null) { item.warnings.Add($"{edge.name}: no spline container; bend not sent."); return; }
            var e = new Edge
            {
                name = edge.name, parent = PathOf(t.parent), side = (int)edge.Edge, position = V(t.position),
                positionInfluence = so.FindProperty("m_PositionInfluence").floatValue,
                across = CurveOf(so.FindProperty("m_AcrossInfluence").animationCurveValue),
                generatedPointCount = so.FindProperty("m_GeneratedPointCount").intValue
            };
            // EngineX edge objects sit at the Unity position with identity rotation and scale.
            e.knots = Knots(spline, p => t.TransformPoint(p) - t.position, d => t.TransformVector(d));
            var bases = so.FindProperty("m_GeneratedBasePositions");
            for (int i = 0; i < bases.arraySize; i++)
            {
                var w = t.TransformPoint(bases.GetArrayElementAtIndex(i).vector3Value) - t.position;
                e.basePositions.Add(w.x); e.basePositions.Add(w.y); e.basePositions.Add(w.z);
            }
            var profiles = so.FindProperty("m_KnotProfileOverrides");
            for (int i = 0; i < profiles.arraySize; i++)
            {
                var p = profiles.GetArrayElementAtIndex(i);
                e.knotProfiles.Add(new KnotProfile { enabled = p.FindPropertyRelative("enabled").boolValue, initialized = p.FindPropertyRelative("initialized").boolValue, height = CurveOf(p.FindPropertyRelative("height").animationCurveValue) });
            }
            item.edges.Add(e);
        }

        static List<Knot> Knots(UnitySpline spline, Func<Vector3, Vector3> point, Func<Vector3, Vector3> vector)
        {
            var knots = new List<Knot>();
            for (int i = 0; i < spline.Count; i++)
            {
                var k = spline[i];
                Quaternion r = k.Rotation;
                // Bake knot rotation and the object transform into tangent offsets.
                knots.Add(new Knot { p = V(point(k.Position)), i = V(vector(r * (Vector3)k.TangentIn)), o = V(vector(r * (Vector3)k.TangentOut)), m = (int)spline.GetTangentMode(i) });
            }
            return knots;
        }

        static Curve CurveOf(AnimationCurve curve)
        {
            var result = new Curve();
            if (curve == null) return result;
            foreach (var k in curve.keys)
            {
                var key = new Key { t = k.time, v = k.value, inWeight = k.inWeight, outWeight = k.outWeight, weighted = (int)k.weightedMode };
#pragma warning disable CS0618
                key.tangentMode = k.tangentMode;
#pragma warning restore CS0618
                // JSON has no Infinity; stepped (Constant) tangents travel as flags.
                if (float.IsInfinity(k.inTangent)) key.inStep = k.inTangent > 0 ? 1 : -1; else key.inSlope = k.inTangent;
                if (float.IsInfinity(k.outTangent)) key.outStep = k.outTangent > 0 ? 1 : -1; else key.outSlope = k.outTangent;
                result.keys.Add(key);
            }
            return result;
        }

        // EngineX applies X, then Y, then Z rotations (R = Rz * Ry * Rx); Unity's Euler order is Z, X, Y.
        static float[] NativeEuler(Quaternion q)
        {
            var m = Matrix4x4.Rotate(q);
            float y = Mathf.Asin(Mathf.Clamp(-m.m20, -1f, 1f)), x, z;
            if (Mathf.Abs(m.m20) < 0.99999f) { x = Mathf.Atan2(m.m21, m.m22); z = Mathf.Atan2(m.m10, m.m00); }
            else { x = Mathf.Atan2(-m.m12, m.m11); z = 0f; }
            return new[] { x * Mathf.Rad2Deg, y * Mathf.Rad2Deg, z * Mathf.Rad2Deg };
        }

        static T[] FindAll<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        static UnityEngine.Object Ref(UnityEngine.Object owner, string field) => new SerializedObject(owner).FindProperty(field)?.objectReferenceValue;
        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        static string PathOf(Component c) => PathOf(c != null ? c.transform : null);
        static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
#endif
