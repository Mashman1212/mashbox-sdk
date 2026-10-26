using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private sealed class PatchHint
        {
            internal int[][] sides;
            internal bool mismatch,matched;
            internal string label;
        }
        private readonly Dictionary<int,PatchHint> patchHints=new Dictionary<int,PatchHint>();
        private StudioRetopologyDocument.Part hintOwner;
        private int hintRevision;
        private Vector3[] hintCorners=Array.Empty<Vector3>();
        private PatchHint PatchCompletionHint(int vertex)
        {
            if(activeTool!=RetopoTool.Patch || corners.Count!=3 || vertex<0 || corners.Contains(Active.vertices[vertex]))return null;
            if(hintOwner!=Active || hintRevision!=Active.Revision || !hintCorners.SequenceEqual(corners))
            {patchHints.Clear();hintOwner=Active;hintRevision=Active.Revision;hintCorners=corners.ToArray();}
            if(patchHints.TryGetValue(vertex,out var cached))return cached;
            var hint=new PatchHint();patchHints[vertex]=hint;
            float tolerance=Mathf.Max(1e-6f,Active.reference.bounds.size.magnitude*1e-5f);
            try
            {
                hint.sides=StudioRetopologyPatchBorders.Find(Active,corners.Concat(new[]{Active.vertices[vertex]}).ToArray(),tolerance);
                if(patchContinuation!=null&&patchOwner==Active)hint.sides[0]=patchContinuation;
                int Count(int side)=>hint.sides[side]==null?0:hint.sides[side].Length-1;
                var pairs=new List<string>();
                for(int axis=0;axis<2;axis++)
                {
                    int a=Count(axis),b=Count(axis+2);
                    if(a>0&&b>0)
                    {pairs.Add(a+" / "+b+" edges");hint.mismatch|=a!=b;hint.matched=true;}
                }
                hint.label=pairs.Count>0?string.Join("  |  ",pairs):"Snap to border";
                if(hint.mismatch){hint.label+=" - counts differ";return hint;}
                int columns=1,rows=1;bool reverse=false;
                StudioRetopologyPatchBorders.Match(Active,hint.sides,ref columns,ref rows,ref reverse);
                hint.label+=hint.matched?" - matching borders":"";
            }
            catch(InvalidOperationException)
            {hint.mismatch=true;hint.label="Ambiguous or incompatible border";}
            return hint;
        }
        private static Color PatchHintColor(PatchHint hint,Color fallback)
            =>hint==null?fallback:hint.mismatch?new Color(1f,.48f,.22f):hint.matched?new Color(.4f,1f,.45f):fallback;

        private void DrawPatchBorderCounts(PatchHint hint)
        {
            if(hint?.sides==null)return;
            for(int side=0;side<4;side++)
            {
                var chain=hint.sides[side];if(chain==null)continue;
                var opposite=hint.sides[(side+2)%4];
                var color=opposite==null?new Color(.5f,.85f,1f):chain.Length==opposite.Length?new Color(.4f,1f,.45f):new Color(1f,.48f,.22f);
                Handles.color=color;Handles.DrawAAPolyLine(4,chain.Select(i=>Active.vertices[i]).ToArray());
                var middle=chain.Length/2;var point=Active.vertices[chain[middle]];
                if(chain.Length%2==0)point=(point+Active.vertices[chain[middle-1]])*.5f;
                var style=new GUIStyle(EditorStyles.miniBoldLabel);style.normal.textColor=color;
                Handles.Label(point,"  "+(chain.Length-1)+" edges",style);
            }
        }
    }
}
