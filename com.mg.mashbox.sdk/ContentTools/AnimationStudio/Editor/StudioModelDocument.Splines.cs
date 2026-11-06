using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
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
