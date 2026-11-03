using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int[] FractureMesh(int index, StudioMeshFracture.Result result, Material interior, Action restorePreview)
        {
            if(index<0||index>=nodes.Count||!nodes[index].mesh)throw new InvalidOperationException("The fracture source no longer exists.");
            var source=nodes[index];var parts=new List<Node>();bool published=false;
            try
            {
                foreach(var piece in result.Pieces)
                {
                    var part=StudioMeshFracture.Publish(result,piece);parts.Add(part);
                    part.name=ObjectNames.GetUniqueName(nodes.Concat(parts.Take(parts.Count-1)).Select(n=>n.name).ToArray(),source.name+" Fragment");part.mesh.name=part.name;
                    part.parent=source.parent;part.position=source.position;part.positionOffset=source.positionOffset;part.RotationChannels=source.RotationChannels;part.rotation=source.rotation;
                    part.scale=source.scale;part.pivotRotation=source.pivotRotation;part.frozenRotation=source.frozenRotation;part.frozenPivotBaseline=source.frozenPivotBaseline;
                    part.active=source.active;part.previewTextureSlice=source.previewTextureSlice;part.vertexAOBaked=source.vertexAOBaked;
                    part.materials=new Material[result.Source.Submeshes+1];Array.Copy(source.materials,part.materials,Math.Min(source.materials.Length,result.Source.Submeshes));part.materials[result.Source.Submeshes]=interior;
                    var center=part.mesh.bounds.center;var points=part.mesh.vertices;for(int v=0;v<points.Length;v++)points[v]-=center;part.mesh.vertices=points;part.mesh.RecalculateBounds();
                    part.position+=part.rotation*Vector3.Scale(part.scale,center);
                }
                restorePreview?.Invoke();
                foreach(var part in parts)AttachOwnedMesh(part.mesh);
                Undo.RegisterCompleteObjectUndo(this,"Fracture");
                var first=parts[0];var localShift=Vector3.Scale(Quaternion.Inverse(source.rotation)*(first.position-source.position),new Vector3(1/source.scale.x,1/source.scale.y,1/source.scale.z));
                foreach(var child in nodes.Where(n=>n.parent==index))child.position-=localShift;
                source.position=first.position;source.mesh=first.mesh;source.cutPolygons=first.cutPolygons;source.cutTriangles=first.cutTriangles;source.geometryVertices=first.geometryVertices;source.normalGroups=first.normalGroups;
                source.materials=first.materials;source.modifiers=null;source.subdivision=new StudioSubdivisionSettings();
                var indices=new List<int>{index};foreach(var part in parts.Skip(1)){indices.Add(nodes.Count);nodes.Add(part);}
                published=true;EditorUtility.SetDirty(this);return indices.ToArray();
            }
            finally{if(!published)foreach(var part in parts)if(part.mesh)DestroyImmediate(part.mesh,true);}
        }
    }
}
