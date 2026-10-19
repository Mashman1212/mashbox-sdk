using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        public int CreateBlendShape(IEnumerable<int> selected)
        {
            var indices=selected.Distinct().ToArray();
            if(indices.Length<2 || indices.Any(i=>i<0 || i>=nodes.Count || !nodes[i].mesh || nodes[i].skinned))
                throw new InvalidOperationException("Select at least two static mesh shapes. The first in hierarchy order is the base.");
            Matrix4x4 World(int i) { var n=nodes[i];var local=Matrix4x4.TRS(n.position+n.positionOffset,n.rotation,n.scale);return n.parent<0?local:World(n.parent)*local; }
            var basis=nodes[indices[0]];var baseWorld=World(indices[0]);
            if(Mathf.Abs(baseWorld.determinant)<1e-8f)throw new InvalidOperationException("The base shape has zero scale.");
            var data=StudioBlendShape.Capture(indices.Select(i=>nodes[i].mesh).ToArray(),indices.Select(i=>nodes[i].name).ToArray(),indices.Select(i=>baseWorld.inverse*World(i)).ToArray());
            var mesh=Instantiate(basis.mesh);mesh.ClearBlendShapes();
            var result=new Node { name=ObjectNames.GetUniqueName(nodes.Select(n=>n.name).ToArray(),basis.name+"_Blend"),
                parent=basis.parent,position=basis.position,positionOffset=basis.positionOffset,rotation=basis.rotation,scale=basis.scale,
                materials=basis.materials.ToArray(),previewTextureSlice=basis.previewTextureSlice,mesh=mesh,blendShape=data };
            mesh.name=result.name;data.Apply(mesh);
            Undo.RegisterCompleteObjectUndo(this,"Create blend shape");
            foreach(int i in indices) nodes[i].active=false;
            nodes.Add(result);EditorUtility.SetDirty(this);
            return nodes.Count-1;
        }
        public void SetBlendMaster(int index,float value) => EditBlend(index,"Blend shape master",data=>data.SetMaster(value));
        public void SetBlendWeight(int index,int shape,float value) => EditBlend(index,"Blend shape weight",data=>data.SetWeight(shape,value));
        private void EditBlend(int index,string label,Action<StudioBlendShape> edit)
        {
            var n=nodes[index];if(n.blendShape==null)throw new InvalidOperationException("Select a blend shape object.");
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{this,n.mesh},label);
            edit(n.blendShape);n.blendShape.Apply(n.mesh);EditorUtility.SetDirty(n.mesh);EditorUtility.SetDirty(this);
        }
        public void BakeBlendShape(int index)
        {
            if(nodes[index].blendShape==null)return;
            Undo.RegisterCompleteObjectUndo(this,"Bake blend shape pose");nodes[index].blendShape=null;EditorUtility.SetDirty(this);
        }
    }
}
