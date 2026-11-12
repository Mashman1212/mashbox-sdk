#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    // Generated sibling: share the Trail graph's fragment shading and vertex morph,
    // but apply surface relief to existing vertices instead of invoking hull/domain stages.
    internal static class MGTerrainShaderVariant
    {
        internal const string Source = "Packages/com.mg.mashbox.sdk/Shaders/HDRP/Lit/Surfaces/MG_Lit_Trail.shadergraph";
        internal const string Destination = "Packages/com.mg.mashbox.sdk/Shaders/HDRP/Lit/Surfaces/Resources/MG_Lit_Trail_Terrain.shadergraph";

        static string Id(int n) => "f7a14ca3d8fe4e23a679f35f" + n.ToString("D8");
        static JObject Ref(string id) => new JObject { ["m_Id"] = id };
        static JObject Slot(string node, int slot) => new JObject { ["m_Node"] = Ref(node), ["m_SlotId"] = slot };
        internal static string Generate(string source)
        {
            var docs = Regex.Split(source.Trim(), @"\r?\n\s*\r?\n(?=\{)").Select(JObject.Parse).ToList();
            var root = docs[0];
            var byId = docs.ToDictionary(d => (string)d["m_ObjectId"]);
            docs.Single(d => (string)d["m_Type"] == "UnityEditor.Rendering.HighDefinition.ShaderGraph.SystemData")["m_Tessellation"] = false;
            var position = docs.Single(d => (string)d["m_Name"] == "VertexDescription.Position");
            var displacement = docs.Single(d => (string)d["m_Name"] == "VertexDescription.TessellationDisplacement");
            var edges = (JArray)root["m_Edges"];
            var positionEdge = edges.OfType<JObject>().Single(e => (string)e["m_InputSlot"]["m_Node"]["m_Id"] == (string)position["m_ObjectId"]);
            var displacementEdge = edges.OfType<JObject>().Single(e => (string)e["m_InputSlot"]["m_Node"]["m_Id"] == (string)displacement["m_ObjectId"]);
            var node = (JObject)docs.Single(d => (string)d["m_FunctionName"] == "MGDistantSurfaceVertexMorph").DeepClone();
            var templateSlot = byId[(string)node["m_Slots"][0]["m_Id"]];
            node["m_ObjectId"] = Id(1); node["m_Group"] = Ref("");
            node["m_Name"] = "MGTerrainVertexRelief (Custom Function)";
            node["m_FunctionName"] = "MGTerrainVertexRelief";
            node["m_SourceType"] = 1; node["m_FunctionSource"] = "";
            node["m_FunctionBody"] = "Position = PositionOS + mul((float3x3)GetWorldToObjectMatrix(), DisplacementWS);";
            var slots = new JArray(); node["m_Slots"] = slots;
            string[] names = { "PositionOS", "DisplacementWS", "Position" };
            for (int i = 0; i < names.Length; i++)
            {
                var slot = (JObject)templateSlot.DeepClone(); slot["m_ObjectId"] = Id(i + 2); slot["m_Id"] = i;
                slot["m_DisplayName"] = slot["m_ShaderOutputName"] = names[i]; slot["m_SlotType"] = i == 2 ? 1 : 0;
                slots.Add(Ref(Id(i + 2))); docs.Add(slot);
            }
            ((JArray)root["m_Nodes"]).Add(Ref(Id(1))); docs.Add(node);
            edges.Add(new JObject { ["m_OutputSlot"] = positionEdge["m_OutputSlot"].DeepClone(), ["m_InputSlot"] = Slot(Id(1), 0) });
            edges.Add(new JObject { ["m_OutputSlot"] = displacementEdge["m_OutputSlot"].DeepClone(), ["m_InputSlot"] = Slot(Id(1), 1) });
            positionEdge["m_OutputSlot"] = Slot(Id(1), 2);
            // Keep the CPU displacement-bound contract even though HDRP no longer adds its tessellation properties.
            var bound = (JObject)docs.Single(d => (string)d["m_Name"] == "TessellationQuality" && ((string)d["m_Type"]).EndsWith("ShaderProperty")).DeepClone();
            bound["m_ObjectId"] = Id(5); bound["m_Guid"] = new JObject { ["m_GuidSerialized"] = "8e51176c-67e1-4716-bb3c-f35f00000005" };
            bound["m_Name"] = "Terrain Displacement Bound";
            bound["m_OverrideReferenceName"] = bound["m_DefaultReferenceName"] = "_TessellationMaxDisplacement";
            bound["m_Value"] = 1; bound["m_Hidden"] = true;
            docs.Add(bound); ((JArray)root["m_Properties"]).Add(Ref(Id(5)));
            return string.Join("\n\n", docs.Select(d => d.ToString(Formatting.Indented))) + "\n";
        }
        [MenuItem("Tools/MashBox/MG Terrain/Rendering/Rebuild Terrain Shader Variant")]
        internal static void Rebuild()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Source);
            if (package == null) throw new InvalidOperationException("MashBox SDK package not resolved.");
            string suffix = Source.Substring(package.assetPath.Length + 1);
            string input = Path.Combine(package.resolvedPath, suffix);
            string output = Path.Combine(package.resolvedPath, Destination.Substring(package.assetPath.Length + 1));
            string generated = Generate(File.ReadAllText(input));
            if (File.Exists(output) && File.ReadAllText(output) == generated) return;
            File.WriteAllText(output, generated);
            AssetDatabase.ImportAsset(Destination);
        }
    }
}
#endif
