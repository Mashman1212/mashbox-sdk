/* 
	VertexDirt plug-in for Unity
	Copyright 2014-2020, Zoltan Farago, All rights reserved.
*/

// @script ExecuteInEditMode()
using UnityEngine;
using System.Collections;

[ExecuteInEditMode]
public class VDColorHandler : VDColorHandlerBase 
{
	void Awake() 
	{
		if (Application.isPlaying) 
		{
			MeshFilter mf = GetComponent<MeshFilter>();
			SetMesh();
			mf.mesh.colors32 = colors;
		}
	}	
	void Update() 
	{
		if (Application.isEditor && !Application.isPlaying) 
		{
			SetMesh();
		}
	}
	
	public void SetColors()
	{
		if (coloredMesh) {
			MeshFilter mf = GetComponent<MeshFilter>();
#if UNITY_EDITOR
            // Imported meshes may disable CPU writes. Rebuild only the scene copy;
            // never modify the model importer or the shared source asset.
            if (!Application.isPlaying && (!coloredMesh.isReadable || (originalMesh && coloredMesh.vertexCount != originalMesh.vertexCount)))
                coloredMesh = CreateEditableCopy(originalMesh ? originalMesh : coloredMesh);
#endif
			coloredMesh.colors32 = colors;
			mf.mesh = coloredMesh;
		}
	}

#if UNITY_EDITOR
    static Mesh CreateEditableCopy(Mesh source)
    {
        // Editor MeshUtility can read imported GPU-only meshes without enabling Read/Write.
        using (var input = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(source))
        {
            var data = input[0];
            var output = Mesh.AllocateWritableMeshData(1);
            try
            {
                var target = output[0];
                target.SetVertexBufferParams(data.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < data.vertexBufferCount; stream++)
                    data.GetVertexData<byte>(stream).CopyTo(target.GetVertexData<byte>(stream));
                int count = data.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16
                    ? data.GetIndexData<ushort>().Length : data.GetIndexData<uint>().Length;
                target.SetIndexBufferParams(count, data.indexFormat);
                data.GetIndexData<byte>().CopyTo(target.GetIndexData<byte>());
                target.subMeshCount = data.subMeshCount;
                for (int sub = 0; sub < data.subMeshCount; sub++)
                    target.SetSubMesh(sub, data.GetSubMesh(sub), UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            }
            catch { output.Dispose(); throw; }
            var copy = new Mesh { name = source.name };
            Mesh.ApplyAndDisposeWritableMeshData(output, copy);
            copy.bounds = source.bounds;
            return copy;
        }
    }
#endif
	public void SetMesh() {
		MeshFilter mf = GetComponent<MeshFilter>();
		if (!GetComponent<MeshFilter>().sharedMesh) 
		{
			GetComponent<MeshFilter>().sharedMesh = originalMesh;
		}
		if (!coloredMesh && mf.sharedMesh && Application.isEditor && !Application.isPlaying) 
		{
			originalMesh = mf.sharedMesh;
			coloredMesh = Mesh.Instantiate(mf.sharedMesh) as Mesh;  //make a deep copy
			coloredMesh.name = mf.sharedMesh.name;
			SetColors();
		}		
		if (colors != tempColors && Application.isEditor && !Application.isPlaying)
		{
			tempColors = colors;
			SetColors();
		}
	}

	void OnDisable() 
	{
		gameObject.GetComponent<MeshFilter>().mesh = originalMesh;
	}
	void OnEnable() 
	{
		MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
		if (!originalMesh) 
		{
			originalMesh = meshFilter.sharedMesh;
		}	
		if (coloredMesh) 
		{
			meshFilter.mesh = coloredMesh;
		}
	}
}
