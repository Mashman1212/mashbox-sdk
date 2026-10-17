using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioAnchorValidation
    {
        static readonly List<string> results=new List<string>();
        static void Check(bool value,string message) { if(!value) throw new Exception(message); results.Add("PASS: "+message); }
        public static void Run()
        {
            StudioModelEditor editor=null;
            int exit=0;
            try
            {
                var names=StudioAnchorCatalog.Names;
                Check(names.Contains("Stem_Anchor") && names.Contains("Left_Grip_Anchor") && names.Contains("Sprocket_Anchor") && names.Contains("StemBolt_Anchor_6"),"Anchor catalog reads exact vehicle names, including numbered anchors, from the SDK asset");
                Check(names.Distinct().Count()==names.Length && !names.Contains("Griptape") && names.All(n=>!n.Contains('/')),"Catalog deduplicates anchor names and excludes geometry/hierarchy path text");
                string folder="Assets/Anchors "+Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
                var doc=StudioModelDocument.CreateEmpty(folder+"/Workspace.asset"); doc.AddPrimitive(PrimitiveType.Cube); doc.Save();
                byte[] disk=File.ReadAllBytes(AssetDatabase.GetAssetPath(doc));
                editor=new StudioModelEditor(()=>{}); editor.Load(doc); editor.SelectObject(0);
                typeof(StudioModelEditor).GetMethod("CreateAnchor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{(Action<StudioModelDocument>)editor.Load});
                int anchor=editor.Active;
                Check(anchor==1 && doc.nodes[anchor].parent==0 && doc.nodes[anchor].anchorType=="Stem_Anchor","Create Anchor Point parents and selects an editable anchor under the active model");
                Check(!doc.nodes[anchor].mesh && editor.Transforms[anchor].parent==editor.Transforms[0],"Anchor is an empty transform and not renderable/exported geometry");
                var icon=(Texture2D)typeof(StudioModelEditor).GetProperty("AnchorIcon",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                Check(icon && icon.GetPixels().Any(c=>c.a>0),"Anchor hierarchy icon is available");
                editor.Transforms[anchor].localPosition=new Vector3(1,2,3); editor.Transforms[anchor].localRotation=Quaternion.Euler(10,20,30);
                doc.ReadTransform(anchor,editor.Transforms[anchor]);
                Undo.IncrementCurrentGroup();
                editor.ChangeAnchorType(anchor,"Left_Grip_Anchor"); Undo.FlushUndoRecordObjects();
                Check(doc.nodes[anchor].name=="Left_Grip_Anchor" && editor.Transforms[anchor].name=="Left_Grip_Anchor","Changing type updates the hierarchy and transform name together");
                Undo.PerformUndo();
                Check(doc.nodes[anchor].anchorType=="Stem_Anchor" && editor.Transforms[anchor].name=="Stem_Anchor","Undo restores both anchor type and name");
                Undo.PerformRedo();
                Check(doc.nodes[anchor].anchorType=="Left_Grip_Anchor","Redo restores the changed anchor type");
                bool rejected=false;
                try { editor.ChangeAnchorType(anchor,"my_custom_anchor"); } catch(ArgumentException) { rejected=true; }
                Check(rejected && doc.nodes[anchor].name=="Left_Grip_Anchor","Non-catalog names cannot be assigned");
                rejected=false;
                try { doc.AddAnchor("Left_Grip_Anchor",0); } catch(InvalidOperationException) { rejected=true; }
                Check(rejected,"Duplicate anchor types under one parent are rejected instead of numbered automatically");
                int duplicate=doc.DuplicateObjects(new[]{0})[0]; editor.Load(doc);
                Check(doc.nodes[duplicate+1].anchorType=="Left_Grip_Anchor" && doc.nodes[duplicate+1].name=="Left_Grip_Anchor","Duplicating a model preserves exact child anchor identifiers");
                Check(File.ReadAllBytes(AssetDatabase.GetAssetPath(doc)).SequenceEqual(disk),"Anchor creation, movement and type changes remain unsaved until Save");
                var savedCopy=doc.SaveAs(folder+"/Copy.asset");
                Check(savedCopy.nodes[anchor].anchorType=="Left_Grip_Anchor" && savedCopy.nodes[anchor].position==new Vector3(1,2,3),"Save As retains anchor metadata and transforms");
                doc.Save();
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(doc),ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                Check(doc.nodes[anchor].anchorType=="Left_Grip_Anchor","Workspace save/reload retains the selected anchor type");
                editor.Load(doc); editor.SelectObject(0);
                string fbx=folder+"/Equippable.fbx"; editor.ExportFile(Path.GetFullPath(fbx),true);
                AssetDatabase.ImportAsset(fbx,ImportAssetOptions.ForceSynchronousImport);
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var exported=model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Left_Grip_Anchor");
                Check(exported && (exported.localPosition-new Vector3(1,2,3)).sqrMagnitude<.0001f,"FBX export preserves the exact anchor transform name and position");
                Check(model.GetComponentsInChildren<MeshFilter>(true).Length==1,"Anchor marker adds no mesh to FBX export");
                var imported=StudioModelDocument.Import(model,folder+"/Imported.asset");
                Check(imported.nodes.Any(n=>n.name=="Left_Grip_Anchor" && n.anchorType==n.name),"Import recognizes existing SDK anchor names as editable anchor points");
                var existing=doc.nodes[anchor]; existing.anchorType=null;
                editor.Load(doc);
                Check(doc.nodes[anchor].anchorType=="Left_Grip_Anchor","Existing workspace anchor transforms gain typed controls on load");
            }
            catch(Exception e) { results.Add("FAIL: "+e); exit=1; }
            finally { editor?.Dispose(); }
            File.WriteAllLines("anchor-result.txt",results); EditorApplication.Exit(exit);
        }
    }
}
