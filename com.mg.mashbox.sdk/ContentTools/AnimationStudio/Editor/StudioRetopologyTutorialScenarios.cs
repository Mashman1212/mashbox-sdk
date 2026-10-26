using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyTutorialScenarios
    {
        internal static StudioRetopologyDocument Create(string id)
        {
            var lesson=StudioRetopologyLessons.Find(id)??throw new ArgumentException("Unknown tutorial: "+id);
            var doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();
            doc.name="Practice - "+lesson.title;doc.hideFlags=HideFlags.HideAndDontSave;
            doc.tutorialId=id;doc.selected=0;doc.targetTriangles=180;doc.offset=.004f;
            var part=new Part{name="Practice surface"};doc.parts.Add(part);
            try
            {
                if(lesson.scenario=="pants")
                {
                    // Read a private copy; never change SDK importer settings or source geometry.
                    const string path="Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Human_Pants_Punch_WC.fbx";
                    var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var renderer=model?model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r=>r.sharedMesh):null;
                    if(!renderer)throw new InvalidOperationException("The SDK pants reference is unavailable. Reimport the SDK Human characters, then start Pants workshop again. Your workspace has not changed.");
                    var mesh=StudioSkinningDocument.CreateEditableCopy(renderer.sharedMesh);part.reference=mesh;
                    var matrix=model.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                    mesh.vertices=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
                    part.name="Practice pants";doc.profile="Pants";doc.targetTriangles=5000;
                }
                else
                {
                    var reference=new Part{name="Practice reference"};
                    if(lesson.scenario=="tube")Cylinder(reference,48,32);
                    else Grid(reference,32,32,-1,1,-1,1);
                    part.reference=StudioRetopologyDocument.BuildMesh(reference);
                    switch(lesson.scenario)
                    {
                        case "blank":case "tube":break;
                        case "patch":Grid(part,4,5,-.9f,-.1f,-.8f,.8f);break;
                        case "strip":Grid(part,1,1,-.9f,-.55f,-.15f,.15f);break;
                        case "gap":Grid(part,2,3,-.9f,-.25f,-.75f,.75f);Grid(part,2,3,.25f,.9f,-.75f,.75f);break;
                        case "weld":Grid(part,3,4,-.9f,-.04f,-.8f,.8f);Grid(part,3,4,.04f,.9f,-.8f,.8f);break;
                        default:Grid(part,8,8,-.9f,.9f,-.9f,.9f);break;
                    }
                    if(lesson.scenario=="distorted")
                        for(int y=1;y<8;y++)for(int x=1;x<8;x++)
                        {
                            int i=y*9+x;var p=part.vertices[i];
                            p.x+=.08f*Mathf.Sin(y*2.1f+x);p.y+=.06f*Mathf.Sin(x*1.7f+y);
                            p.z=Height(p.x,p.y);part.vertices[i]=p;
                        }
                    if(lesson.scenario=="triangles")
                    {
                        var quads=part.faces.ToArray();part.faces.Clear();
                        foreach(var f in quads){var a=f.vertices;part.faces.Add(new Face(new[]{a[0],a[1],a[2]}));part.faces.Add(new Face(new[]{a[0],a[2],a[3]}));}
                    }
                    // Put the working surface just above its reference toward the starting camera.
                    for(int i=0;i<part.vertices.Count;i++)part.vertices[i]+=Vector3.back*doc.offset;
                }
                part.reference.name=part.name+" Reference";part.reference.hideFlags=HideFlags.HideAndDontSave;
                return doc;
            }
            catch {if(part.reference)UnityEngine.Object.DestroyImmediate(part.reference);UnityEngine.Object.DestroyImmediate(doc);throw;}
        }
        private static float Height(float x,float y)=>.10f*Mathf.Sin(x*2.5f)+.06f*Mathf.Cos(y*3);
        private static void Grid(Part p,int nx,int ny,float x0,float x1,float y0,float y1)
        {
            int start=p.vertices.Count;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
            {
                float px=Mathf.Lerp(x0,x1,x/(float)nx),py=Mathf.Lerp(y0,y1,y/(float)ny);
                p.vertices.Add(new Vector3(px,py,Height(px,py)));p.uv.Add(new Vector2((px+1)*.5f,(py+1)*.5f));
            }
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
            {int a=start+y*(nx+1)+x;p.faces.Add(new Face(new[]{a,a+nx+1,a+nx+2,a+1}));}
        }
        private static void Cylinder(Part p,int sides,int rows)
        {
            for(int y=0;y<=rows;y++)for(int x=0;x<sides;x++)
            {
                float t=y/(float)rows,angle=2*Mathf.PI*x/sides,r=Mathf.Lerp(.4f,.3f,t);
                p.vertices.Add(new Vector3(Mathf.Cos(angle)*r,Mathf.Lerp(-1,1,t),Mathf.Sin(angle)*r));
                p.uv.Add(new Vector2(x/(float)sides,t));
            }
            for(int y=0;y<rows;y++)for(int x=0;x<sides;x++)
            {int a=y*sides+x,b=y*sides+(x+1)%sides;p.faces.Add(new Face(new[]{a,a+sides,b+sides,b}));}
        }
    }
}
