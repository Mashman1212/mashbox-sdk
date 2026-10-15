using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class VDFbxColorWriter
{
    internal static string SourcePath(IEnumerable<VDPrecisionBaker.Result> results)
    {
        var paths = results.Where(r => r.source).Select(r => AssetDatabase.GetAssetPath(r.source)).Distinct().ToArray();
        if (paths.Length != 1 || !paths[0].StartsWith("Assets/", StringComparison.Ordinal)
            || !paths[0].EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose baked meshes from a single source FBX under Assets.");
        return paths[0];
    }

    internal static string Overwrite(List<VDPrecisionBaker.Result> results)
    {
        string path = SourcePath(results);
        string full = Path.GetFullPath(path);
        if (new FileInfo(full).IsReadOnly) throw new IOException("The source FBX is read-only. Check it out before overwriting.");
        var doc = VDFbxDocument.Read(full);
        string fingerprint = doc.NonColorFingerprint();
        var targets = new Dictionary<string, VDPrecisionBaker.Result>();
        foreach (var r in results)
        {
            if (!r.filter || r.colors == null || r.colors.Length != r.source.vertexCount)
                throw new InvalidOperationException("Apply the completed bake before saving.");
            if (targets.TryGetValue(r.source.name, out var other))
            {
                if (r.source != other.source || !r.colors.SequenceEqual(other.colors))
                    throw new InvalidOperationException("Two selected instances of the same mesh have different colors. Save one instance at a time.");
            }
            else targets.Add(r.source.name, r);
        }
        var originalMeshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
        if (originalMeshes.GroupBy(m => m.name).Any(g => g.Count() > 1))
            throw new InvalidOperationException("The FBX has duplicate mesh names. Give each mesh a unique name before saving.");
        var identities = originalMeshes.ToDictionary(m => m.name, m => LocalId(m));
        var snapshots = originalMeshes.ToDictionary(m => m.name, m => Object.Instantiate(m));
        string tempFolder = "Assets/__VertexDirtTemporary_" + Guid.NewGuid().ToString("N");
        string probePath = tempFolder + "/" + Path.GetFileName(path);
        string backup = null;
        bool replaced = false;
        try
        {
            Directory.CreateDirectory(tempFolder);
            // Tag each FBX polygon corner and let Unity perform its own axis conversion,
            // unit scaling and vertex splitting. No guessed axis/scale conversion or vertex order.
            var probe = VDFbxDocument.Read(full);
            var markerOwners = new List<(long geometry, int corner)> { (0, 0) };
            foreach (var g in probe.Geometries)
            {
                var markers = new Color32[VDFbxDocument.CornerCount(g)];
                long id = VDFbxDocument.GeometryId(g);
                for (int i = 0; i < markers.Length; i++)
                {
                    int marker = markerOwners.Count;
                    if (marker >= 0xFFFFFF) throw new NotSupportedException("FBX exceeds 16 million polygon corners.");
                    markers[i] = new Color32((byte)marker, (byte)(marker >> 8), (byte)(marker >> 16), 255);
                    markerOwners.Add((id, i));
                }
                VDFbxDocument.SetColors(g, markers);
            }
            probe.Write(probePath);
            // Copy import settings and remaps, changing only the temporary asset GUID.
            string meta = File.ReadAllText(full + ".meta");
            File.WriteAllText(probePath + ".meta", Regex.Replace(meta, @"(?m)^guid: [a-fA-F0-9]+\r?$", "guid: " + Guid.NewGuid().ToString("N")));
            AssetDatabase.ImportAsset(probePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var imported = AssetDatabase.LoadAllAssetsAtPath(probePath).OfType<Mesh>().ToArray();
            var updates = new Dictionary<long, Color32[]>();
            var assigned = new Dictionary<long, bool[]>();
            var geometryById = doc.Geometries.ToDictionary(VDFbxDocument.GeometryId);
            foreach (var pair in targets)
            {
                var meshes = imported.Where(m => m.name == pair.Key).ToArray();
                if (meshes.Length != 1) throw new InvalidOperationException("Cannot identify source mesh uniquely: " + pair.Key);
                var mesh = meshes[0];
                var colors = mesh.colors32;
                if (colors.Length != mesh.vertexCount) throw new InvalidOperationException("Unity did not import the FBX color markers.");
                var transferred = TransferByTriangles(pair.Value.source, pair.Value.colors, mesh);
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    int marker = colors[i].r | colors[i].g << 8 | colors[i].b << 16;
                    if (marker <= 0 || marker >= markerOwners.Count) throw new InvalidOperationException("FBX color marker did not survive import exactly.");
                    var owner = markerOwners[marker];
                    if (!updates.TryGetValue(owner.geometry, out var output))
                    {
                        int length = VDFbxDocument.CornerCount(geometryById[owner.geometry]);
                        updates[owner.geometry] = output = new Color32[length]; assigned[owner.geometry] = new bool[length];
                    }
                    Color32 color = transferred[i];
                    if (assigned[owner.geometry][owner.corner] && !output[owner.corner].Equals(color))
                        throw new InvalidOperationException("Conflicting colors map to one FBX polygon corner.");
                    output[owner.corner] = color; assigned[owner.geometry][owner.corner] = true;
                }
            }
            foreach (var update in updates)
            {
                if (assigned[update.Key].Any(a => !a))
                    throw new InvalidOperationException("Some FBX corners were not imported. No file was changed; remove degenerate faces or select the whole source mesh.");
                VDFbxDocument.SetColors(geometryById[update.Key], update.Value);
            }
            if (updates.Count == 0) throw new InvalidOperationException("No source geometry was mapped.");
            string backupDirectory = Path.GetFullPath("Library/VertexDirtBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(backupDirectory);
            backup = Path.Combine(backupDirectory, Path.GetFileName(path));
            string staged = Path.Combine(backupDirectory, "staged.fbx");
            doc.Write(staged);
            if (VDFbxDocument.Read(staged).NonColorFingerprint() != fingerprint)
                throw new InvalidDataException("FBX validation failed: non-color data changed.");
            File.Copy(full, backup); File.Copy(full + ".meta", backup + ".meta");
            string guid = AssetDatabase.AssetPathToGUID(path);
            File.Replace(staged, full, null);
            replaced = true;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.AssetPathToGUID(path) != guid) throw new InvalidDataException("FBX asset GUID changed.");
            var finalMeshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToDictionary(m => m.name);
            if (finalMeshes.Count != snapshots.Count) throw new InvalidDataException("FBX mesh count changed after import.");
            foreach (var pair in snapshots)
            {
                if (!finalMeshes.TryGetValue(pair.Key, out var final) || LocalId(final) != identities[pair.Key])
                    throw new InvalidDataException("FBX mesh references changed after import.");
                var expected = targets.TryGetValue(pair.Key, out var target) ? target.colors : pair.Value.colors32;
                ValidateMesh(pair.Value, final, expected);
            }
            // Remove scene-only color handlers, returning filters to the imported FBX.
            // These assignments have Undo; the source file itself is restored from its backup.
            foreach (var r in results)
            {
                Mesh savedMesh = finalMeshes[r.source.name];
                var handler = r.filter.GetComponent<VDColorHandler>();
                Undo.RecordObject(r.filter, "Use saved FBX colors");
                if (handler) Undo.DestroyObjectImmediate(handler);
                r.filter.sharedMesh = savedMesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r.filter);
                EditorUtility.SetDirty(r.filter);
            }
            return backup;
        }
        catch
        {
            if (replaced && backup != null)
            {
                File.Copy(backup, full, true); File.Copy(backup + ".meta", full + ".meta", true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            throw;
        }
        finally
        {
            foreach (var mesh in snapshots.Values) Object.DestroyImmediate(mesh);
            AssetDatabase.DeleteAsset(tempFolder);
            // Import failures may leave an unregistered temporary directory.
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    static long LocalId(Object asset)
    { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id); return id; }

    // Unique marker colors can prevent welding and slightly change averaged imported normals.
    // Match triangle corners by position + UV + submesh instead of those derived normals.
    static Color32[] TransferByTriangles(Mesh source, Color32[] colors, Mesh probe)
    {
        if (source.subMeshCount != probe.subMeshCount) throw new InvalidDataException("Probe submesh count changed.");
        float tolerance = Mathf.Max(source.bounds.size.magnitude * 1e-6f, 1e-8f);
        Vector3 origin = source.bounds.center;
        Func<Mesh, string[]> keys = mesh =>
        {
            var p = mesh.vertices; var uv = mesh.uv;
            var result = new string[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 v = (p[i] - origin) / tolerance;
                Vector2 t = uv.Length > i ? uv[i] : Vector2.zero;
                result[i] = $"{Mathf.RoundToInt(v.x)},{Mathf.RoundToInt(v.y)},{Mathf.RoundToInt(v.z)},{Mathf.RoundToInt(t.x * 1000000)},{Mathf.RoundToInt(t.y * 1000000)}";
            }
            return result;
        };
        var sourceKeys = keys(source); var probeKeys = keys(probe);
        var output = new Color32[probe.vertexCount]; var assigned = new bool[probe.vertexCount];
        Func<string[], int[], int, string> faceKey = (k, t, i) =>
        {
            var face = new[] { k[t[i]], k[t[i + 1]], k[t[i + 2]] };
            Array.Sort(face, StringComparer.Ordinal); return string.Join("/", face);
        };
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var indices = source.GetTriangles(sub);
            var faces = new Dictionary<string, List<int>>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                string key = faceKey(sourceKeys, indices, i);
                if (!faces.TryGetValue(key, out var list)) faces[key] = list = new List<int>();
                list.Add(i);
            }
            var probeIndices = probe.GetTriangles(sub);
            if (probeIndices.Length != indices.Length) throw new InvalidDataException("Probe triangle count changed.");
            for (int i = 0; i < probeIndices.Length; i += 3)
            {
                if (!faces.TryGetValue(faceKey(probeKeys, probeIndices, i), out var candidates))
                    throw new InvalidDataException("Cannot match an FBX triangle to the source mesh. The file was not changed.");
                for (int j = 0; j < 3; j++)
                {
                    int vertex = probeIndices[i + j];
                    foreach (int candidate in candidates) for (int k = 0; k < 3; k++)
                    {
                        int original = indices[candidate + k];
                        if (sourceKeys[original] != probeKeys[vertex]) continue;
                        if (assigned[vertex] && !output[vertex].Equals(colors[original]))
                            throw new InvalidDataException("Overlapping FBX faces have conflicting vertex colors. Save stopped without changing the source.");
                        output[vertex] = colors[original]; assigned[vertex] = true;
                    }
                }
            }
        }
        if (assigned.Any(a => !a)) throw new InvalidDataException("The FBX contains unmapped vertices.");
        return output;
    }

    internal static void ValidateMesh(Mesh before, Mesh after, Color32[] expected)
    {
        if (before.subMeshCount != after.subMeshCount) throw new InvalidDataException("Submesh count changed.");
        for (int i = 0; i < before.subMeshCount; i++)
            if (before.GetIndexCount(i) != after.GetIndexCount(i) || before.GetTopology(i) != after.GetTopology(i))
                throw new InvalidDataException("Mesh topology changed.");
        var p = after.vertices; var n = after.normals; var uv = after.uv; var colors = after.colors32;
        var lookup = new VertexLookup(before, expected);
        if (expected.Length > 0 && colors.Length != p.Length) throw new InvalidDataException("Saved colors are missing.");
        for (int i = 0; i < p.Length; i++)
        {
            var wanted = lookup.Find(p[i], n.Length > i ? n[i] : Vector3.zero, uv.Length > i ? uv[i] : Vector2.zero);
            if (expected.Length > 0 && !wanted.Equals(colors[i])) throw new InvalidDataException("Saved vertex colors did not survive FBX import exactly.");
        }
    }

    sealed class VertexLookup
    {
        readonly Dictionary<Vector3Int, List<int>> buckets = new Dictionary<Vector3Int, List<int>>();
        readonly Vector3[] positions, normals;
        readonly Vector2[] uv;
        readonly Color32[] colors;
        readonly float tolerance;
        readonly Vector3 origin;
        internal VertexLookup(Mesh source, Color32[] colors)
        {
            positions = source.vertices; normals = source.normals; uv = source.uv; this.colors = colors;
            origin = source.bounds.center; tolerance = Mathf.Max(source.bounds.size.magnitude * 1e-6f, 1e-8f);
            for (int i = 0; i < positions.Length; i++)
            {
                var key = Key(positions[i]);
                if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new List<int>();
                bucket.Add(i);
            }
        }
        Vector3Int Key(Vector3 p) { p = (p - origin) / tolerance; return new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)); }
        internal Color32 Find(Vector3 p, Vector3 n, Vector2 tex)
        {
            var key = Key(p); bool found = false; Color32 result = default;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (buckets.TryGetValue(key + new Vector3Int(x, y, z), out var bucket)) foreach (int i in bucket)
                {
                    if ((positions[i] - p).sqrMagnitude > tolerance * tolerance * 4) continue;
                    if (normals.Length > i && (normals[i] - n).sqrMagnitude > .00001f) continue;
                    if (uv.Length > i && (uv[i] - tex).sqrMagnitude > .00000001f) continue;
                    var color = colors.Length > i ? colors[i] : new Color32(255, 255, 255, 255);
                    if (found && !result.Equals(color)) throw new InvalidOperationException("Ambiguous overlapping vertices have different colors. The source FBX was not changed.");
                    result = color; found = true;
                }
            if (!found)
            {
                int closest = Enumerable.Range(0, positions.Length).OrderBy(i => (positions[i] - p).sqrMagnitude).First();
                throw new InvalidOperationException($"The imported FBX geometry does not match the baked mesh. No colors can be transferred safely. Position {p:G9}, nearest {positions[closest]:G9}, tolerance {tolerance:G9}; normal {n:G9} / {(normals.Length > closest ? normals[closest] : Vector3.zero):G9}; UV {tex:G9} / {(uv.Length > closest ? uv[closest] : Vector2.zero):G9}.");
            }
            return result;
        }
    }
}
