using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int CutSpline(int index, int segment, float t, Transform[] transforms)
        {
            var source=nodes[index];var pieces=source.spline.Cut(segment,t);
            Node other=null;
            if(pieces.Length==2)
            {
                // Capture the evaluated local pose; do not duplicate constraints or dependent objects.
                var pose=transforms[index];
                other=new Node{name=ObjectNames.GetUniqueName(nodes.Select(n=>n.name).ToArray(),source.name+" Cut"),
                    parent=source.parent,position=pose.localPosition,rotation=pose.localRotation,scale=pose.localScale,
                    pivotRotation=source.pivotRotation,active=source.active,spline=pieces[1]};
            }
            Undo.RegisterCompleteObjectUndo(this,"Cut Spline at Point");
            source.spline=pieces[0];if(other!=null)nodes.Add(other);
            EditorUtility.SetDirty(this);return other==null?-1:nodes.Count-1;
        }
        internal int AddSpline(StudioSpline source)
        {
            source.Validate(); var spline=source.Copy();
            var center=Vector3.zero; foreach(var k in spline.knots) center+=k.position/spline.knots.Count;
            foreach(var k in spline.knots) k.position-=center;
            Undo.RegisterCompleteObjectUndo(this,"Create Spline");
            nodes.Add(new Node { name=ObjectNames.GetUniqueName(nodes.Select(n=>n.name).ToArray(),"Spline"), position=center, spline=spline });
            EditorUtility.SetDirty(this); return nodes.Count-1;
        }
    }
}
