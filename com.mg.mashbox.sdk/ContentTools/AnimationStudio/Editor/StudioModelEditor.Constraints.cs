using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool constraintChannelsExpanded = true;
        private int constraintCaptureUpdates;
        private static readonly GUIContent ConstraintHeader = new GUIContent("Constraints", EditorGUIUtility.IconContent("ParentConstraint Icon").image);
        private IConstraint ConstraintComponent(StudioConstraintKind kind) => (IConstraint)Transforms[Active].GetComponent(StudioModelConstraints.Types[(int)kind]);
        private Transform ConstraintTransform(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            int index = Document.nodes.FindIndex(n => n.constraintId == id);
            return index >= 0 && index < Transforms.Length ? Transforms[index] : null;
        }
        private string ConstraintNodeId(Transform transform)
        {
            int index = Array.IndexOf(Transforms, transform);
            return index >= 0 ? Document.nodes[index].constraintId : null;
        }
        private void AddModelConstraint(StudioConstraintKind kind)
        {
            if (!Document || Active < 0 || mode != ComponentMode.Object)
                throw new InvalidOperationException("Select the object to constrain in Object mode, then choose a constraint. Edit its sources in Channels.");
            var node = Document.nodes[Active];
            if (node.constraints != null && node.constraints.Any(c => c.kind == kind)) { constraintChannelsExpanded = true; repaint(); return; }
            Undo.RegisterCompleteObjectUndo(Document, "Add " + StudioModelConstraints.Labels[(int)kind]);
            foreach (var n in Document.nodes) StudioModelConstraints.Id(n);
            var component = (IConstraint)Transforms[Active].gameObject.AddComponent(StudioModelConstraints.Types[(int)kind]);
            try
            {
                var data = StudioModelConstraints.Capture(component, kind, ConstraintNodeId);
                data.active = false;
                data.locked = false;
                if (node.constraints == null) node.constraints = new List<StudioModelConstraintData>();
                node.constraints.Add(data);
            }
            catch { UnityEngine.Object.DestroyImmediate((UnityEngine.Object)component); throw; }
            constraintChannelsExpanded = true; Changed(false);
        }
        private string ConstraintSourcePopup(string label, string id)
        {
            var labels = new List<string> { "None" };
            var ids = new List<string> { null };
            for (int i = 0; i < Document.nodes.Count; i++)
            {
                if (i == Active) continue;
                labels.Add(i + ": " + Document.nodes[i].name); ids.Add(Document.nodes[i].constraintId);
            }
            if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) { labels.Add("Missing source"); ids.Add(id); }
            int old = Math.Max(0, ids.IndexOf(id));
            int next = EditorGUILayout.Popup(label, old, labels.ToArray());
            return next != old ? ids[next] : id;
        }
        private void CommitConstraint(int index, StudioModelConstraintData data, int activation = 0)
        {
            StudioModelConstraints.Validate(Document, Active, data);
            if (activation != 0 && StudioModelConstraints.MissingReferences(data, ConstraintTransform))
                throw new InvalidOperationException("Choose valid workspace source objects before activating the constraint.");
            var node = Document.nodes[Active];
            var before = node.constraints[index];
            var component = ConstraintComponent(data.kind);
            var target = Transforms[Active];
            var position = target.localPosition; var rotation = target.localRotation; var scale = target.localScale;
            Undo.RegisterCompleteObjectUndo(Document, "Edit " + StudioModelConstraints.Labels[(int)data.kind]);
            try
            {
                StudioModelConstraints.Apply(data, component, ConstraintTransform);
                if (activation != 0)
                {
                    StudioModelConstraints.Activate(component, data.kind, activation == 2);
                    data = StudioModelConstraints.Capture(component, data.kind, ConstraintNodeId);
                }
                node.constraints[index] = data;
                if (before.active && !data.active) Document.ReadTransform(Active, target);
            }
            catch
            {
                StudioModelConstraints.Apply(before, component, ConstraintTransform);
                target.localPosition = position; target.localRotation = rotation; target.localScale = scale;
                throw;
            }
            Changed(false); EditorApplication.QueuePlayerLoopUpdate();
        }
        private static Vector3 ConstraintVectorField(string label, Vector3 value, bool length)
        {
            if (!length) return EditorGUILayout.Vector3Field(label, value);
            float unit = (float)StudioSceneUnits.MetersPerUnit(StudioSceneUnits.Current);
            EditorGUI.BeginChangeCheck();
            var displayed = EditorGUILayout.Vector3Field(label + " (" + StudioSceneUnits.Symbol + ")", value / unit);
            return EditorGUI.EndChangeCheck() ? displayed * unit : value;
        }
        private void QueueConstraintEvaluation()
        {
            if (!Document || !Document.nodes.Any(n => n.constraints != null && n.constraints.Any(c => c.active))) return;
            EditorApplication.QueuePlayerLoopUpdate();
            constraintCaptureUpdates = 2;
        }
        private void TickConstraints() { if (constraintCaptureUpdates > 0 && --constraintCaptureUpdates == 0) RepaintConstraints(); }
        private void RepaintConstraints() { if (Document) { CaptureUnlockedConstraints(false); repaint(); } }
        private void CaptureUnlockedConstraints(bool update)
        {
            if (!Document || Transforms.Length != Document.nodes.Count) return;
            for (int i = 0; i < Transforms.Length; i++)
            {
                var n = Document.nodes[i]; if (n.constraints == null || !Transforms[i]) continue;
                if (update) StudioModelConstraints.ReadUnlocked(Document, i, Transforms[i]);
                else for (int c = 0; c < n.constraints.Count; c++)
                {
                    var data = n.constraints[c]; if (!data.active || data.locked || StudioModelConstraints.MissingReferences(data, ConstraintTransform)) continue;
                    var native = (IConstraint)Transforms[i].GetComponent(StudioModelConstraints.Types[(int)data.kind]);
                    if (native != null) n.constraints[c] = StudioModelConstraints.Capture(native, data.kind, ConstraintNodeId);
                }
            }
        }
        private void DrawConstraintChannels()
        {
            if (!Document || Active < 0 || Document.nodes[Active].constraints == null || Document.nodes[Active].constraints.Count == 0) return;
            GUILayout.Space(4);
            constraintChannelsExpanded = DrawChannelCategoryFoldout(EditorGUILayout.GetControlRect(false, 20), constraintChannelsExpanded, ConstraintHeader);
            if (!constraintChannelsExpanded) return;
            var constraints = Document.nodes[Active].constraints;
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.channelInset))
            for (int index = 0; index < constraints.Count; index++)
            {
                int captured = index;
                var data = constraints[index].Copy();
                GUILayout.Label(StudioModelConstraints.Labels[(int)data.kind], EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                data.active = EditorGUILayout.Toggle("Is Active", data.active);
                data.weight = EditorGUILayout.Slider("Weight", data.weight, 0, 1);
                data.locked = EditorGUILayout.Toggle("Lock", data.locked);
                foreach (var setting in data.settings)
                {
                    var property = StudioModelConstraints.Property(data.kind, setting.name); if (property == null) continue;
                    string label = setting.name switch { "translationAtRest" => "Position At Rest", "translationOffset" => "Position Offset", "translationAxis" => "Freeze Position Axes", "rotationAxis" => "Freeze Rotation Axes", "scalingAxis" => "Freeze Scale Axes", _ => ObjectNames.NicifyVariableName(setting.name) };
                    if (property.PropertyType == typeof(Vector3)) setting.vector = ConstraintVectorField(label, setting.vector, setting.name.StartsWith("translation", StringComparison.Ordinal));
                    else if (property.PropertyType == typeof(float)) setting.number = EditorGUILayout.FloatField(label, setting.number);
                    else if (property.PropertyType == typeof(bool)) setting.flag = EditorGUILayout.Toggle(label, setting.flag);
                    else if (property.PropertyType == typeof(Axis)) setting.integer = (int)(Axis)EditorGUILayout.EnumFlagsField(label, (Axis)setting.integer);
                    else setting.integer = Convert.ToInt32(EditorGUILayout.EnumPopup(label, (Enum)Enum.ToObject(property.PropertyType, setting.integer)));
                }
                if (data.kind == StudioConstraintKind.Aim || data.kind == StudioConstraintKind.LookAt)
                    data.worldUpId = ConstraintSourcePopup("World Up Object", data.worldUpId);
                GUILayout.Label("Sources", EditorStyles.boldLabel);
                int remove = -1, move = -1;
                for (int s = 0; s < data.sources.Count; s++)
                {
                    var source = data.sources[s];
                    source.nodeId = ConstraintSourcePopup("Source " + (s + 1), source.nodeId);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        source.weight = EditorGUILayout.Slider("Weight", source.weight, 0, 1);
                        using (new EditorGUI.DisabledScope(s == 0)) if (GUILayout.Button("↑", GUILayout.Width(24))) move = s;
                        if (GUILayout.Button("−", GUILayout.Width(24))) remove = s;
                    }
                    if (data.kind == StudioConstraintKind.Parent)
                    {
                        source.positionOffset = ConstraintVectorField("Position Offset", source.positionOffset, true);
                        source.rotationOffset = EditorGUILayout.Vector3Field("Rotation Offset", source.rotationOffset);
                    }
                }
                bool changed = EditorGUI.EndChangeCheck();
                if (remove >= 0) { data.sources.RemoveAt(remove); changed = true; }
                if (move > 0) { var s = data.sources[move]; data.sources.RemoveAt(move); data.sources.Insert(move - 1, s); changed = true; }
                if (GUILayout.Button("Add Source")) { data.sources.Add(new StudioConstraintSource()); changed = true; }
                if (StudioModelConstraints.MissingReferences(data, ConstraintTransform)) EditorGUILayout.HelpBox("Choose sources and any required World Up Object. Constraints with missing references do not evaluate.", MessageType.Info);
                int activation = 0; bool delete = false;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Activate")) activation = 1;
                    if (GUILayout.Button("Zero")) activation = 2;
                    if (GUILayout.Button("Remove")) delete = true;
                }
                if (delete)
                {
                    Run(() => { Undo.RegisterCompleteObjectUndo(Document, "Remove constraint"); constraints.RemoveAt(captured); UnityEngine.Object.DestroyImmediate((UnityEngine.Object)ConstraintComponent(data.kind)); Document.ReadTransform(Active, Transforms[Active]); Changed(false); });
                    break;
                }
                if (changed || activation != 0) Run(() => CommitConstraint(captured, data, activation));
                GUILayout.Space(6);
            }
        }
    }
}
