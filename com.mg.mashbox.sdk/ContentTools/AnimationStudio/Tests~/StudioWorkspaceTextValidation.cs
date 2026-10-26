using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;
using Unity.Collections;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class StudioWorkspaceTextValidation
{
    static string folder;
    static string Folder => folder ?? File.ReadAllText(Path.Combine(StudioModelDocument.WorkspaceFolder,"text-validation-last.txt"));
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);Debug.Log("PASS "+message);}
    static void Release(StudioModelDocument doc)
    {
        var meshes=doc.nodes.Where(n=>n.mesh).Select(n=>n.mesh).Distinct().ToArray();
        Object.DestroyImmediate(doc);foreach(var mesh in meshes)Object.DestroyImmediate(mesh);
    }
    public static void Run()
    {
        folder=Path.Combine(StudioModelDocument.WorkspaceFolder,"FormatValidation",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(StudioModelDocument.WorkspaceFolder,"text-validation-last.txt"),Folder);
        string path=Path.Combine(Folder,"TextSimple.workspace");
        var doc=StudioModelDocument.CreateEmpty(path);doc.AddPrimitive(PrimitiveType.Cube);doc.AddPrimitive(PrimitiveType.Cube);doc.AddEmptyTransform();
        doc.nodes[1].position=new Vector3(1.2345678f,-2.1234567f,3.9876543f);doc.nodes[1].active=false;
        var original=doc.nodes[0].mesh.vertices;
        string legacy=Path.Combine(Folder,"NativeBaseline.workspace");
        InternalEditorUtility.SaveToSerializedFileAndForget(new Object[]{doc,doc.nodes[0].mesh,doc.nodes[1].mesh},legacy,true);
        doc.Save();long bytes=new FileInfo(path).Length,oldBytes=new FileInfo(legacy).Length;
        string json=File.ReadAllText(path);
        Check(json.StartsWith("{\"format\":\"MashBoxStudio.Workspace\""),"Workspace is readable versioned JSON");
        Check(!json.Contains("m_VertexData")&&!json.Contains("_typelessdata"),"No Unity mesh buffer dumps");
        Check(bytes<oldBytes && bytes<9000,"Small workspace is compact");
        Check(json.Split(new[]{"\"index32\""},StringSplitOptions.None).Length-1==1,"Identical cube geometry saved only once");
        long compressed;using(var memory=new MemoryStream()){using(var gzip=new GZipStream(memory,System.IO.Compression.CompressionLevel.Optimal,true)){var input=System.Text.Encoding.UTF8.GetBytes(json);gzip.Write(input,0,input.Length);}compressed=memory.Length;}
        Debug.Log("WORKSPACE_TEXT_SIZE native="+oldBytes+" json="+bytes+" gzip="+compressed);
        Release(doc);doc=StudioModelDocument.OpenFile(path);
        Check(doc.nodes.Count==3&&!doc.nodes[1].active&&doc.nodes[1].position==new Vector3(1.2345678f,-2.1234567f,3.9876543f),"Hierarchy, visibility and float positions round-trip");
        Check(doc.nodes[0].mesh.vertices.SequenceEqual(original)&&doc.nodes[0].mesh!=doc.nodes[1].mesh,"Deduplicated mesh data restores independent editable meshes");
        var changed=doc.nodes[0].mesh.vertices;changed[0]+=Vector3.one;doc.nodes[0].mesh.vertices=changed;
        Check(doc.nodes[1].mesh.vertices.SequenceEqual(original),"Editing one restored cube cannot change its duplicate");
        Release(doc);
        var legacyDoc=StudioModelDocument.OpenFile(legacy);legacyDoc.Save();
        Check(StudioWorkspaceText.IsTextWorkspace(legacy)&&File.Exists(legacy+".legacy.bak"),"Legacy workspace upgrades with original backup");
        Release(legacyDoc);
        BuildRich();CheckRich();
        string bad=Path.Combine(Folder,"Future.workspace");File.WriteAllText(bad,json.Replace("\"version\":1","\"version\":999"));
        bool rejected=false;try{StudioModelDocument.OpenFile(bad);}catch(InvalidDataException){rejected=true;}
        Check(rejected&&File.ReadAllText(bad).Contains("999"),"Unsupported version rejected without modifying file");
        string corrupt=Path.Combine(Folder,"Truncated.workspace");File.WriteAllText(corrupt,json.Substring(0,json.Length/2));
        rejected=false;try{StudioModelDocument.OpenFile(corrupt);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"Truncated JSON rejected");
        // A read-only copy of an existing edited workspace exercises native migration.
        string source=Environment.GetEnvironmentVariable("MASHBOX_WORKSPACE_MIGRATION_FIXTURE");
        if(File.Exists(source))
        {
            string copy=Path.Combine(Folder,"RealLegacy.workspace");long before=new FileInfo(source).Length;
            // The isolated project loads Studio from a test DLL rather than the source
            // package. Adapt only this copy's script binding to the test assembly.
            string fixture=File.ReadAllText(source);
            var binding=File.ReadAllLines(legacy+".legacy.bak");
            foreach(string field in new[]{"m_Script:","m_EditorClassIdentifier:"})
            {
                string replacement=binding.First(line=>line.StartsWith("  "+field));
                fixture=new System.Text.RegularExpressions.Regex("^  "+field+".*$",System.Text.RegularExpressions.RegexOptions.Multiline).Replace(fixture,replacement,1);
            }
            File.WriteAllText(copy,fixture);
            var migrated=StudioModelDocument.OpenFile(copy);int vertices=migrated.nodes.Where(n=>n.mesh).Sum(n=>n.mesh.vertexCount);migrated.Save();Release(migrated);
            migrated=StudioModelDocument.OpenFile(copy);Check(migrated.nodes.Where(n=>n.mesh).Sum(n=>n.mesh.vertexCount)==vertices,"Existing edited workspace keeps every vertex after migration");
            Debug.Log("WORKSPACE_REAL_SIZE native="+before+" json="+new FileInfo(copy).Length);Release(migrated);
        }
        Debug.Log("STUDIO_WORKSPACE_TEXT_VALIDATION_SUCCESS");
    }
    static void BuildRich()
    {
        var doc=StudioModelDocument.CreateEmpty(Path.Combine(Folder,"TextRich.workspace"));doc.AddPrimitive(PrimitiveType.Cube);
        var node=doc.nodes[0];var mesh=node.mesh;
        node.anchorType="TestAnchor";node.positionOffset=new Vector3(7,8,9);node.pivotRotation=Quaternion.Euler(12,34,56);node.previewTextureSlice=3;node.vertexAOBaked=true;
        doc.lighting.exposure=-4.26f;doc.lighting.shadows=false;
        mesh.SetUVs(4,Enumerable.Range(0,mesh.vertexCount).Select(i=>new Vector3(i*.125f,-i*.1f,1.2345678f)).ToArray());
        mesh.SetUVs(7,Enumerable.Range(0,mesh.vertexCount).Select(i=>new Vector4(i,2,3,4)).ToArray());
        foreach(int channel in new[]{1,2,3,5,6})mesh.SetUVs(channel,Enumerable.Range(0,mesh.vertexCount).Select(i=>new Vector2(i*.03f,channel*.125f)).ToArray());
        mesh.colors=Enumerable.Repeat(new Color(.12345678f,.3f,.7f,1),mesh.vertexCount).ToArray();
        var deltas=Enumerable.Repeat(Vector3.up,mesh.vertexCount).ToArray();
        mesh.AddBlendShapeFrame("Morph",50,deltas,new Vector3[mesh.vertexCount],new Vector3[mesh.vertexCount]);
        mesh.AddBlendShapeFrame("Morph",100,deltas.Select(d=>d*2).ToArray(),new Vector3[mesh.vertexCount],new Vector3[mesh.vertexCount]);
        mesh.bindposes=Enumerable.Repeat(Matrix4x4.identity,6).ToArray();
        var bw=new BoneWeight1[mesh.vertexCount*6];for(int i=0;i<bw.Length;i++)bw[i]=new BoneWeight1{boneIndex=i%6,weight=1f/6};
        using(var counts=new NativeArray<byte>(Enumerable.Repeat((byte)6,mesh.vertexCount).ToArray(),Allocator.Temp))
        using(var weights=new NativeArray<BoneWeight1>(bw,Allocator.Temp))mesh.SetBoneWeights(counts,weights);
        for(int i=0;i<6;i++)doc.nodes.Add(new StudioModelDocument.Node{name="Bone "+i,parent=i==0?-1:i});
        node.skinned=true;node.bones=Enumerable.Range(1,6).ToArray();node.rootBone=1;
        var faces=new StudioMeshCuts(node).Faces;node.cutPolygons=faces.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToArray();node.cutTriangles=mesh.triangles;
        var blend=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);var blend2=Object.Instantiate(blend);blend2.vertices=blend2.vertices.Select(v=>v*2).ToArray();
        doc.nodes.Add(new StudioModelDocument.Node{name="Live Blend",mesh=blend,blendShape=StudioBlendShape.Capture(new[]{blend,blend2},new[]{"Basis","Large"},new[]{Matrix4x4.identity,Matrix4x4.identity})});Object.DestroyImmediate(blend2);
        var shader=Shader.Find("Standard");var material=new Material(shader){name="Persistent text material"};
        string materialPath=AssetDatabase.GenerateUniqueAssetPath("Assets/TextMaterial.mat");AssetDatabase.CreateAsset(material,materialPath);
        File.WriteAllText(Path.Combine(Folder,"MaterialPath.txt"),materialPath);
        var inline=new Material(shader){name="Inline text material"};var texture=new Texture2D(2,2);texture.SetPixels(Enumerable.Repeat(Color.magenta,4).ToArray());texture.Apply();inline.mainTexture=texture;
        node.materials=new[]{material,inline};mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2},1);
        node.cutPolygons=node.cutPolygons.Concat(new[]{1,3,0,1,2}).ToArray();node.cutTriangles=mesh.triangles;
        mesh.UploadMeshData(true); // Imported meshes can legitimately disable Read/Write.
        var large=new Mesh{indexFormat=IndexFormat.UInt32,name="Large indices"};large.vertices=Enumerable.Range(0,70002).Select(i=>new Vector3(i%100,i/100,0)).ToArray();large.triangles=new[]{69999,70000,70001};
        doc.nodes.Add(new StudioModelDocument.Node{name="32 bit",mesh=large});
        var random=new System.Random(42);var exact=new Mesh{name="Float precision"};
        exact.vertices=Enumerable.Range(0,1024).Select(i=>new Vector3((float)(random.NextDouble()*200-100),(float)(random.NextDouble()*.0001),i==0?float.Epsilon:(float)(random.NextDouble()*100000))).ToArray();
        exact.triangles=new[]{0,1,2};
        File.WriteAllText(Path.Combine(Folder,"Precision.txt"),Hash(exact.vertices));
        doc.nodes.Add(new StudioModelDocument.Node{name="123.456e-3",mesh=exact});
        doc.Save();Release(doc);Object.DestroyImmediate(inline);Object.DestroyImmediate(texture);
    }
    public static void CheckRich()
    {
        var doc=StudioModelDocument.OpenFile(Path.Combine(Folder,"TextRich.workspace"));
        try
        {
            var node=doc.nodes[0];var mesh=node.mesh;
            Check(node.positionOffset==new Vector3(7,8,9)&&node.anchorType=="TestAnchor"&&node.vertexAOBaked&&node.previewTextureSlice==3,"Node metadata survives text storage");
            Check(Mathf.Abs(doc.lighting.exposure+4.26f)<1e-6f&&!doc.lighting.shadows,"Lighting survives text storage");
            var uv3=new List<Vector3>();mesh.GetUVs(4,uv3);var uv4=new List<Vector4>();mesh.GetUVs(7,uv4);
            Check(uv3.Count==24&&uv3[5]==new Vector3(.625f,-.5f,1.2345678f)&&uv4[5]==new Vector4(5,2,3,4),"UV channels 4/7 retain dimensions and exact coordinates");
            Check(Enumerable.Range(0,8).All(c=>mesh.HasVertexAttribute(VertexAttribute.TexCoord0+c)),"All eight UV channels survive storage");
            Check(Mathf.Abs(mesh.colors[0].r-.12345678f)<1e-7f,"Vertex colours/AO retain precision");
            Check(mesh.blendShapeCount==1&&mesh.GetBlendShapeFrameCount(0)==2&&mesh.GetBlendShapeFrameWeight(0,1)==100,"Mesh blend-shape frames retained");
            var delta=new Vector3[24];mesh.GetBlendShapeFrameVertices(0,1,delta,null,null);Check(delta.All(v=>v==Vector3.up*2),"Blend-shape deltas retained");
            Check(mesh.GetBonesPerVertex()[0]==6&&mesh.GetAllBoneWeights().Length==144&&mesh.bindposes.Length==6,"More than four skin weights per vertex retained");
            Check(node.bones.SequenceEqual(Enumerable.Range(1,6))&&node.rootBone==1,"Skin hierarchy references retained");
            Check(node.cutPolygons.Length>0&&node.cutTriangles.SequenceEqual(mesh.triangles),"Editable polygon boundaries and triangle cache retained");
            Check(doc.nodes[7].blendShape.shapes.Length==2&&doc.nodes[7].blendShape.shapes[1].positions[0]==doc.nodes[7].mesh.vertices[0]*2,"Live modelling blend snapshots retained");
            Check(node.materials[0]==AssetDatabase.LoadAssetAtPath<Material>(File.ReadAllText(Path.Combine(Folder,"MaterialPath.txt"))),"Persistent material reference resolved");
            Check(node.materials[1].name=="Inline text material"&&((Texture2D)node.materials[1].mainTexture).GetPixel(0,0)==Color.magenta,"Transient material/texture remains self-contained");
            var large=doc.nodes[8].mesh;Check(large.vertexCount==70002&&large.indexFormat==IndexFormat.UInt32&&large.triangles[2]==70001,"32-bit mesh indices retained");
            Check(doc.nodes[9].name=="123.456e-3"&&Hash(doc.nodes[9].mesh.vertices)==File.ReadAllText(Path.Combine(Folder,"Precision.txt")),"Compact float notation preserves vertex bits, including tiny values, and leaves strings unchanged");
            doc.Save();
            Debug.Log("STUDIO_WORKSPACE_TEXT_RELOAD_SUCCESS");
        }
        finally{Release(doc);}
    }
    public static void Reload()
    {
        CheckRich();
        var objects=InternalEditorUtility.LoadSerializedFileAndForget(Path.Combine(Folder,"RealLegacy.workspace.legacy.bak"));
        var source=objects.OfType<StudioModelDocument>().Single();
        var restored=StudioModelDocument.OpenFile(Path.Combine(Folder,"RealLegacy.workspace"));
        try
        {
            Check(source.nodes.Count==restored.nodes.Count,"Legacy node count survives separate-process reload");
            for(int i=0;i<source.nodes.Count;i++)
            {
                Check((bool)source.nodes[i].mesh==(bool)restored.nodes[i].mesh,"Legacy mesh assignment retained: "+i);
                if(source.nodes[i].mesh)Check(GeometryHash(source.nodes[i].mesh)==GeometryHash(restored.nodes[i].mesh),"Legacy vertex attributes, seams and submeshes remain exact: "+i);
            }
            Debug.Log("STUDIO_WORKSPACE_TEXT_SEPARATE_PROCESS_SUCCESS");
        }
        finally {Release(restored);foreach(var value in objects)if(value&&!EditorUtility.IsPersistent(value))Object.DestroyImmediate(value);}
    }
    static string GeometryHash(Mesh mesh)
    {
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
        {
            var view=data[0];int count=view.vertexCount;
            void Number(float value){writer.Write(value==0?0:BitConverter.SingleToInt32Bits(value));}
            using(var v=new NativeArray<Vector3>(count,Allocator.Temp)){view.GetVertices(v);foreach(var point in v){Number(point.x);Number(point.y);Number(point.z);}}
            foreach(var attribute in new[]{VertexAttribute.Normal,VertexAttribute.Tangent,VertexAttribute.Color})
            {
                writer.Write(mesh.HasVertexAttribute(attribute));if(!mesh.HasVertexAttribute(attribute))continue;
                if(attribute==VertexAttribute.Normal)using(var v=new NativeArray<Vector3>(count,Allocator.Temp)){view.GetNormals(v);foreach(var p in v){Number(p.x);Number(p.y);Number(p.z);}}
                else if(attribute==VertexAttribute.Tangent)using(var v=new NativeArray<Vector4>(count,Allocator.Temp)){view.GetTangents(v);foreach(var p in v)for(int j=0;j<4;j++)Number(p[j]);}
                else using(var v=new NativeArray<Color>(count,Allocator.Temp)){view.GetColors(v);foreach(var p in v)for(int j=0;j<4;j++)Number(p[j]);}
            }
            for(int c=0;c<8;c++)
            {
                var attribute=VertexAttribute.TexCoord0+c;int dimension=mesh.HasVertexAttribute(attribute)?mesh.GetVertexAttributeDimension(attribute):0;writer.Write(dimension);
                if(dimension==0)continue;
                using(var v=new NativeArray<Vector4>(count,Allocator.Temp)){view.GetUVs(c,v);foreach(var p in v)for(int j=0;j<dimension;j++)Number(p[j]);}
            }
            for(int sub=0;sub<view.subMeshCount;sub++)
            {
                var descriptor=view.GetSubMesh(sub);writer.Write((int)descriptor.topology);writer.Write(descriptor.baseVertex);writer.Write(descriptor.indexCount);
                using(var indices=new NativeArray<int>(descriptor.indexCount,Allocator.Temp)){view.GetIndices(indices,sub,false);foreach(var index in indices)writer.Write(index);}
            }
            writer.Flush();using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream.ToArray()));
        }
    }
    static string Hash(Vector3[] values)
    {
        var bytes=new byte[values.Length*12];
        for(int i=0;i<values.Length;i++)for(int j=0;j<3;j++)Buffer.BlockCopy(BitConverter.GetBytes(values[i][j]),0,bytes,i*12+j*4,4);
        using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes));
    }
}
