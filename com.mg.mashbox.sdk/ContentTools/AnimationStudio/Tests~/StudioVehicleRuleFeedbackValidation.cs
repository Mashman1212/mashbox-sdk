using System;
using System.Collections.Generic;
using System.IO;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Validator = MashBoxSDK.AnimationStudio.StudioVehiclePartValidation;

public static class StudioVehicleRuleFeedbackValidation
{
    public enum Vehicle { BMX, MTB }
    public enum Part { Frame, Forks }
    public enum Direction { Forward, Back, Left, Right, Up, Down }
    [Serializable] public class Pair { public string SuperType; public string[] Types; }
    [Serializable] public class Orientation { public string ChildPath; public Direction Forward, Up; public float Tolerance = 5; }
    [Serializable] public class Rule
    {
        public Vehicle AppliesToSuperType; public Part AppliesToType; public string AppliesToBrand;
        public bool IgnoreRequiredChildren; public string[] RequiredChildren, RequiredDescendantsAnywhere;
        public Orientation[] PreferredOrientations;
    }
    public class Rules : ScriptableObject { public Pair[] AllowedPairs; public string[] Colors; public Rule[] ItemRules; }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/LiveVehiclePartsValidation/Assets", StringComparison.Ordinal)) throw new InvalidOperationException("Use the disposable validation project.");
        int code = 0, checks = 0; var report = new List<string>();
        var rules = ScriptableObject.CreateInstance<Rules>(); var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
        void Require(bool pass, string label) { if (!pass) throw new Exception(label); checks++; }
        bool Has(string text) => Validator.Validate(doc, 0, rules).issues.Exists(i => i.Contains(text));
        try
        {
            rules.AllowedPairs = new[] { new Pair { SuperType = "BMX", Types = new[] { "Frame", "Forks" } } };
            rules.Colors = new[] { "Black", "Red" };
            rules.ItemRules = new[] { new Rule { RequiredChildren = new[] { "Mesh", "RearWheel_Anchor/Peg_Anchor" }, RequiredDescendantsAnywhere = new[] { "Seat_Anchor" }, PreferredOrientations = new[] { new Orientation { ChildPath = "RearWheel_Anchor", Forward = Direction.Forward, Up = Direction.Up } } } };
            doc.nodes.Add(new StudioModelDocument.Node { name = "BMX_Frame_Custom_Test", vehiclePart = new StudioModelDocument.VehiclePartData() });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Mesh", parent = 0 });
            doc.nodes.Add(new StudioModelDocument.Node { name = "RearWheel_Anchor", parent = 0 });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Peg_Anchor", parent = 2 });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Seat_Anchor", parent = 2 });
            Require(Validator.Validate(doc, 0, rules).issues.Count == 0, "Valid hierarchy and flexible descendant pass");
            doc.nodes[3].parent = 0;
            Require(Has("Missing required child: 'RearWheel_Anchor/Peg_Anchor'"), "Wrong parent reports exact missing path");
            Require(Has("Unexpected child: 'Peg_Anchor'"), "Wrong parent reports unexpected path");
            doc.nodes[3].parent = 2;
            doc.nodes[4].name = "Seat_anchor";
            Require(Has("Missing required descendant"), "Case-sensitive anchor requirement");
            doc.nodes[4].name = "Seat_Anchor";
            doc.nodes[2].rotation = Quaternion.Euler(90, 0, 0);
            Require(Has("Orientation differs"), "Orientation tolerance checked");
            doc.nodes[2].rotation = Quaternion.identity;
            doc.nodes[0].name = "Bad name"; doc.nodes[3].parent = 0;
            Require(Has("four tags") && Has("Missing required child"), "Bad names do not hide hierarchy errors");
            doc.nodes[3].parent = 2;
            doc.nodes[0].name = "BMX_Forks_Custom_Test";
            Require(Has("different type"), "Selected metadata versus name mismatch");
            doc.nodes[0].name = "BMX_Frame_Custom_Test_Purple";
            Require(Has("four tags") && !Has("Unknown color"), "Modeler requires four tags without applying prefab color rules");
            doc.nodes[0].name = "BMX_Frame_Vanilla_Bean";
            Require(Validator.Validate(doc, 0, rules).issues.Count == 0, "Four-tag model name passes without a color");
            rules.ItemRules[0].AppliesToBrand = "Special"; doc.nodes[3].name = "Missing";
            Require(Validator.Validate(doc, 0, rules).matchedRules == 0, "Other brand's rules do not apply");
            doc.nodes[0].name = "BMX_Frame_Special_Test";
            Require(Has("Missing required child"), "Matching brand rules apply");
            rules.ItemRules[0].IgnoreRequiredChildren = true;
            Require(!Has("Missing required child") && !Has("Unexpected child"), "IgnoreRequiredChildren honored");
            doc.nodes[0].vehiclePart.vehicle = "MTB";
            Require(Has("does not allow"), "Unsupported vehicle/part pair flagged");
            Require(Validator.Validate(doc, 0, null).issues.Exists(i => i.Contains("unavailable")), "Unavailable rules do not report success");
            report.Add("PASS: " + checks + " naming, hierarchy, orientation, brand and missing-rules checks.");
        }
        catch (Exception ex) { code = 1; report.Add("FAIL: " + ex); }
        finally { UnityEngine.Object.DestroyImmediate(doc); UnityEngine.Object.DestroyImmediate(rules); }
        File.WriteAllLines(Path.Combine(Application.dataPath, "../vehicle-rule-feedback-result.txt"), report);
        EditorApplication.Exit(code);
    }
}
