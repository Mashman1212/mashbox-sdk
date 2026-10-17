using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using MashBoxSDK.ContentTools;
using UnityEditor;
using UnityEngine;

public static class StudioVehiclePartValidation
{
    static readonly List<string> results = new List<string>();
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string Relative(Transform root, Transform node)
    {
        var parts = new Stack<string>(); for (var t = node; t != root; t = t.parent) parts.Push(t.name);
        return string.Join("/",parts);
    }
    public static void Run()
    {
        int code = 0;
        try
        {
            var rules = (ContentValidationRules)StudioVehiclePartCatalog.RulesAsset;
            var templates = StudioVehiclePartCatalog.Load();
            Require(templates.Count > 60,"Vehicle catalog includes all allowed vehicle parts");
            foreach(var template in templates)
            {
                var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
                doc.nodes.Add(new StudioModelDocument.Node { name = "Existing", position = Vector3.one });
                int rootIndex = doc.AddVehiclePart(template);
                Require(rootIndex == 1 && doc.nodes[0].name == "Existing" && doc.nodes[0].position == Vector3.one,"Creation preserves existing workspace");
                using(var editor = new StudioModelEditor(()=>{}))
                {
                    editor.Load(doc); var root = editor.Transforms[rootIndex];
                    var all = root.GetComponentsInChildren<Transform>(true);
                    var matching = rules.ItemRules.Where(r => r.AppliesToSuperType.ToString() == template.vehicle && r.AppliesToType.ToDisplayName() == template.part && (string.IsNullOrEmpty(r.AppliesToBrand) || r.AppliesToBrand == template.brand)).ToArray();
                    foreach(var rule in matching)
                    {
                        if(!rule.IgnoreRequiredChildren)
                        {
                            var exact = (rule.RequiredChildren ?? Array.Empty<string>()).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim().Replace('\\','/')).ToArray();
                            var anywhere = (rule.RequiredDescendantsAnywhere ?? Array.Empty<string>()).Where(s=>!string.IsNullOrWhiteSpace(s)).ToArray();
                            foreach(var path in exact) Require(root.Find(path),template.MenuPath+" missing "+path);
                            foreach(var name in anywhere) Require(all.Any(t=>t!=root && t.name==name),template.MenuPath+" missing descendant "+name);
                            foreach(var node in all.Where(t=>t!=root))
                            {
                                var path = Relative(root,node);
                                Require(exact.Any(p=>p==path || p.StartsWith(path+"/",StringComparison.Ordinal)) || anywhere.Contains(node.name),template.MenuPath+" unexpected child "+path);
                            }
                        }
                        foreach(var expected in rule.PreferredOrientations ?? Array.Empty<ContentValidationRules.PreferredChildOrientation>())
                        {
                            var node = root.Find(expected.ChildPath); if(!node) continue;
                            Require(Vector3.Angle(node.localRotation*Vector3.forward,ContentValidationRules.DirectionUtils.ToVector(expected.Forward))<.01f,"Forward orientation "+expected.ChildPath);
                            Require(Vector3.Angle(node.localRotation*Vector3.up,ContentValidationRules.DirectionUtils.ToVector(expected.Up))<.01f,"Up orientation "+expected.ChildPath);
                        }
                        Require(rule.MaxRenderers<0 || root.GetComponentsInChildren<Renderer>(true).Length<=rule.MaxRenderers,"Renderer budget "+template.MenuPath);
                    }
                    Require(all.GroupBy(t=>t.parent).All(g=>g.Select(t=>t.name).Distinct().Count()==g.Count()),"No duplicate sibling names");
                    for(int i=rootIndex+1;i<doc.nodes.Count;i++)
                        if(StudioAnchorCatalog.Contains(doc.nodes[i].name)) Require(doc.nodes[i].anchorType==doc.nodes[i].name,"Anchors use SDK types");
                    Require(doc.nodes.Where(n=>n.mesh).All(n=>n.mesh.vertexCount>0 && n.mesh.isReadable),"Editable placeholder meshes");
                    results.Add("PASS: "+template.vehicle+" / "+template.part+" — hierarchy, orientations, anchors, mesh budget"+(matching.Length==0?" (no hierarchy rule)":""));
                }
                foreach(var mesh in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct()) UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(doc);
            }
            var frame=templates.Single(t=>t.vehicle=="MTB" && t.part=="Frame" && t.brand=="");
            var saved=StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/VehiclePart.asset"));
            var disk=File.ReadAllBytes(AssetDatabase.GetAssetPath(saved));
            using(var editor=new StudioModelEditor(()=>{}))
            {
                editor.Load(saved);editor.CreateVehiclePart(frame,null,editor.Load);
                Require(editor.Active==0 && saved.nodes.Any(n=>n.name=="PistonConnection_tip"),"Creation selects complete MTB part");
                int count=saved.nodes.Count;
                Undo.PerformUndo();Require(saved.nodes.Count==0 && editor.Transforms.Length==0,"One Undo removes complete part");
                Undo.PerformRedo();Require(saved.nodes.Count==count && editor.Transforms.Length==count,"Redo restores part and meshes");
                editor.CreateVehiclePart(frame,null,editor.Load);
                Require(saved.nodes.Where(n=>n.parent<0).Select(n=>n.name).Distinct().Count()==2,"Repeated creation uses unique roots");
                Require(File.ReadAllBytes(AssetDatabase.GetAssetPath(saved)).SequenceEqual(disk),"No implicit workspace writes");
                editor.Save();
                Require(saved.nodes.Where(n=>n.mesh).All(n=>AssetDatabase.Contains(n.mesh)),"Explicit Save persists all placeholder meshes");
                string copy=AssetDatabase.GenerateUniqueAssetPath("Assets/VehiclePartRoundTrip.asset");
                Require(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(saved),copy),"Copy saved workspace for roundtrip");
                var loaded=AssetDatabase.LoadAssetAtPath<StudioModelDocument>(copy);
                Require(loaded.nodes.Count==saved.nodes.Count && loaded.nodes.Where(n=>n.mesh).Count()==2,"Saved hierarchy and mesh references reload");
                string export=AssetDatabase.GenerateUniqueAssetPath("Assets/VehiclePartExport.fbx");
                editor.ExportFile(export,true);AssetDatabase.ImportAsset(export,ImportAssetOptions.ForceSynchronousImport);
                var imported=AssetDatabase.LoadAssetAtPath<GameObject>(export);
                Require(imported && imported.transform.Find("body") && imported.transform.Find("chainstay/SeatStay/RearAxle_Anchor") && imported.transform.Find("PistonConnection_tip"),"FBX preserves mesh, linkage transforms and anchors");
                var menu=new StudioMenu();
                typeof(StudioModelEditor).GetMethod("AddVehiclePartMenu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(editor,new object[]{menu,null,(Action<StudioModelDocument>)editor.Load});
                var mtb=menu.Items.Single(i=>i.Text=="Vehicle Part").Children.Single(i=>i.Text=="MTB");
                int beforeMenu=saved.nodes.Count;mtb.Children.Single(i=>i.Text=="Frame").Action();
                Require(saved.nodes.Count==beforeMenu+count && editor.Active==beforeMenu,"Create menu generates and selects the requested vehicle hierarchy");
                results.Add("PASS: MTB creation, complete Undo/Redo, unique roots, no autosave and explicit Save/reload.");
                results.Add("PASS: Create menu callback and selected-part FBX roundtrip retain the MTB linkage hierarchy and mesh.");
            }
        }
        catch(Exception e){results.Add("FAIL: "+e);code=1;}
        File.WriteAllLines("vehicle-part-result.txt",results);EditorApplication.Exit(code);
    }
}
