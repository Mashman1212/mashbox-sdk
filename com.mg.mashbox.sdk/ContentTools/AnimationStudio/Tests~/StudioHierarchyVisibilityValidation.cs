using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioHierarchyVisibilityValidation
{
    static readonly List<string> results = new List<string>();
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        results.Add("PASS: " + message);
    }
    public static void Run()
    {
        int exit = 0;
        try
        {
            var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/HierarchyVisibility.asset"));
            doc.nodes.Add(new StudioModelDocument.Node { name = "Group" });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Child", parent = 0 });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Sibling", parent = 0 });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Other" });
            doc.Save();
            string path = AssetDatabase.GetAssetPath(doc);
            var disk = File.ReadAllBytes(path);
            using (var editor = new StudioModelEditor(() => { }))
            {
                editor.Load(doc); editor.SelectObject(1);
                editor.SetHierarchyVisibility(1, false);
                Check(!doc.nodes[1].active && !editor.Transforms[1].gameObject.activeInHierarchy && editor.Active == 1, "Hide updates preview and document without losing hierarchy selection");
                Check(editor.HierarchyVisibilityMenu(1).Items[0].Text == "Show", "Hidden object offers Show");
                Undo.PerformUndo();
                Check(doc.nodes[1].active && editor.Transforms[1].gameObject.activeInHierarchy, "One Undo restores visibility");
                Undo.PerformRedo();
                Check(!doc.nodes[1].active && !editor.Transforms[1].gameObject.activeInHierarchy, "Redo hides again");
                editor.SetHierarchyVisibility(0, false);
                editor.SetHierarchyVisibility(1, true);
                Check(doc.nodes[0].active && doc.nodes[1].active && editor.Transforms[1].gameObject.activeInHierarchy, "Showing a child enables its hidden parent chain");
                editor.IsolateHierarchyObject(1);
                Check(doc.nodes.Select(n => n.active).SequenceEqual(new[] { true, true, false, false }), "Isolating a child retains its parent and hides siblings and other roots");
                Undo.PerformUndo();
                Check(doc.nodes.All(n => n.active), "One Undo restores the complete pre-isolation visibility state");
                editor.IsolateHierarchyObject(0);
                Check(doc.nodes.Select(n => n.active).SequenceEqual(new[] { true, true, true, false }), "Isolating a group retains all its children");
                editor.ShowAllHierarchyObjects();
                Check(doc.nodes.All(n => n.active) && editor.Transforms.All(t => t.gameObject.activeInHierarchy), "Show All Objects exits isolation");
                var menu = editor.HierarchyVisibilityMenu(1);
                Check(menu.Items.Where(i => !i.Separator).Select(i => i.Text).SequenceEqual(new[] { "Hide", "Isolate Visibility", "Show All Objects" }), "Hierarchy menu contains visibility actions only");
                menu.Items[0].Action();
                Check(!doc.nodes[1].active, "Context action affects the requested row");
                Check(File.ReadAllBytes(path).SequenceEqual(disk), "Visibility editing does not write the workspace to disk");
            }
        }
        catch (Exception e) { results.Add("FAIL: " + e); exit = 1; }
        File.WriteAllLines("hierarchy-visibility-result.txt", results);
        EditorApplication.Exit(exit);
    }
}
