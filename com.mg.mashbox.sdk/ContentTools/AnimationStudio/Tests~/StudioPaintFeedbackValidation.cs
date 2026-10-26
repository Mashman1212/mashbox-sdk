using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public static class StudioPaintFeedbackValidation
{
    internal static string Failure; internal static int Captures;
    internal static void QueuePixels(StudioModelEditor editor, UnityEditor.SceneView view, string mode)
    {
        Action<UnityEditor.SceneView> capture = null;
        capture = current => { if(current != view || Event.current.type != EventType.Repaint) return; UnityEditor.SceneView.duringSceneGui -= capture; try { Pixels(editor,view,mode); Captures++; } catch(Exception error) { Failure = error.ToString(); } };
        UnityEditor.SceneView.duringSceneGui += capture; view.Repaint();
    }
    public static void Bounds()
    {
        var entries = new StudioModelEditor.PaintBoundsIndex.Entry[20000];
        for (int i = 0; i < entries.Length; i++)
        {
            var point = new Vector2((i%200)*40,(i/200)*40);
            entries[i] = new StudioModelEditor.PaintBoundsIndex.Entry { Component = i, Min = point-Vector2.one*2, Max = point+Vector2.one*2 };
        }
        var original = entries.ToArray(); var index = new StudioModelEditor.PaintBoundsIndex(entries); var found = new List<int>();
        var random = new System.Random(173); int max = 0;
        for (int sample = 0; sample < 200; sample++)
        {
            var point = new Vector2(random.Next(8000),random.Next(4000)); index.Query(point,found);
            var expected = original.Where(e=>e.Min.x<=point.x+12&&e.Max.x>=point.x-12&&e.Min.y<=point.y+12&&e.Max.y>=point.y-12).Select(e=>e.Component).OrderBy(i=>i);
            if (!found.SequenceEqual(expected)) throw new Exception("Pick bounds omitted candidates");
            max = Math.Max(max,found.Count);
        }
        if (max > 4) throw new Exception("Sparse broad phase scanned distant components");
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../paint-pick-bounds-result.txt")),"PASS 200 brute-force comparisons over 20,000 components; maximum local candidates="+max);
    }
    internal static void Pixels(StudioModelEditor editor, UnityEditor.SceneView view, string mode)
    {
        var previous = RenderTexture.active;
        var rt = new RenderTexture(view.camera.pixelWidth,view.camera.pixelHeight,24); rt.Create();
        var texture = new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
        RenderTexture.active = rt; GL.PushMatrix();
        try
        {
            GL.Clear(true,true,Color.black); GL.LoadProjectionMatrix(view.camera.projectionMatrix); GL.modelview=view.camera.worldToCameraMatrix;
            typeof(StudioModelEditor).GetMethod("DrawComponentBatches",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{view});
            texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();
            int gold=texture.GetPixels32().Count(c=>c.r>180&&c.g>205&&c.b<210);
            if(gold==0)throw new Exception(mode+" paint hover has no visible yellow GPU pixels");
            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../paint-hover-"+mode+".png")),texture.EncodeToPNG());
        }
        finally{GL.PopMatrix();RenderTexture.active=previous;Object.DestroyImmediate(texture);Object.DestroyImmediate(rt);}
    }
}
