using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int AddAnchor(string type, int parent, Vector3? position = null)
        {
            if (parent < -1 || parent >= nodes.Count) throw new ArgumentOutOfRangeException(nameof(parent));
            ValidateAnchorType(type,parent,-1);
            Undo.RegisterCompleteObjectUndo(this,"Create anchor point");
            nodes.Add(new Node { name=type, anchorType=type, parent=parent, position=position ?? Vector3.zero });
            EditorUtility.SetDirty(this);
            return nodes.Count-1;
        }
        internal void SetAnchorType(int index,string type)
        {
            if (index < 0 || index >= nodes.Count || string.IsNullOrEmpty(nodes[index].anchorType))
                throw new InvalidOperationException("Select an anchor point.");
            var node=nodes[index]; ValidateAnchorType(type,node.parent,index);
            Undo.RegisterCompleteObjectUndo(this,"Change anchor type");
            node.anchorType=type; node.name=type;
            EditorUtility.SetDirty(this);
        }
        private void ValidateAnchorType(string type,int parent,int except)
        {
            if (!StudioAnchorCatalog.Contains(type)) throw new ArgumentException("Choose an anchor type from the SDK content rules.");
            if (nodes.Where((n,i)=>i!=except).Any(n=>n.parent==parent && n.name==type))
                throw new InvalidOperationException("This parent already contains " + type + ". Choose another anchor type or parent.");
        }
    }
}
