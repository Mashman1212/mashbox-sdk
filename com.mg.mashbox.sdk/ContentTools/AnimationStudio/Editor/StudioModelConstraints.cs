using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;

namespace MashBoxSDK.AnimationStudio
{
    public enum StudioConstraintKind { Aim, LookAt, Parent, Position, Rotation, Scale }

    // Portable values only: preview Transform instance IDs never enter the workspace.
    [Serializable] public sealed class StudioConstraintSource
    {
        public string nodeId;
        public float weight = 1;
        public Vector3 positionOffset, rotationOffset;
    }
    [Serializable] public sealed class StudioConstraintSetting
    {
        public string name;
        public Vector3 vector;
        public float number;
        public int integer;
        public bool flag;
    }
    [Serializable] public sealed class StudioModelConstraintData
    {
        public StudioConstraintKind kind;
        public bool active, locked;
        public float weight = 1;
        public string worldUpId;
        public List<StudioConstraintSource> sources = new List<StudioConstraintSource>();
        public List<StudioConstraintSetting> settings = new List<StudioConstraintSetting>();
        public StudioModelConstraintData Copy() => JsonUtility.FromJson<StudioModelConstraintData>(JsonUtility.ToJson(this));
    }

    internal static class StudioModelConstraints
    {
        internal static readonly Type[] Types = { typeof(AimConstraint), typeof(LookAtConstraint), typeof(ParentConstraint), typeof(PositionConstraint), typeof(RotationConstraint), typeof(ScaleConstraint) };
        internal static readonly string[] Labels = { "Aim Constraint", "Look At Constraint", "Parent Constraint", "Position Constraint", "Rotation Constraint", "Scale Constraint" };
        // Only documented type-specific value properties; no Behaviour/object identity fields.
        private static readonly string[] SettingNames = { "translationAtRest", "rotationAtRest", "scaleAtRest", "translationOffset", "rotationOffset", "scaleOffset", "translationAxis", "rotationAxis", "scalingAxis", "aimVector", "upVector", "worldUpType", "worldUpVector", "useUpObject", "roll" };
        private static readonly PropertyInfo[][] Properties = Types.Select(t => SettingNames.Select(n => t.GetProperty(n)).Where(p => p != null && p.CanRead && p.CanWrite).ToArray()).ToArray();
        private static readonly MethodInfo[] ActivateMethods = Types.Select(t => t.GetMethod("ActivateAndPreserveOffset", BindingFlags.Instance | BindingFlags.NonPublic)).ToArray();
        private static readonly MethodInfo[] ZeroMethods = Types.Select(t => t.GetMethod("ActivateWithZeroOffset", BindingFlags.Instance | BindingFlags.NonPublic)).ToArray();
        internal static PropertyInfo Property(StudioConstraintKind kind, string name) => Properties[(int)kind].FirstOrDefault(p => p.Name == name);
        internal static string Id(StudioModelDocument.Node node)
        {
            if (string.IsNullOrEmpty(node.constraintId)) node.constraintId = Guid.NewGuid().ToString("N");
            return node.constraintId;
        }
        internal static StudioModelConstraintData Capture(IConstraint component, StudioConstraintKind kind, Func<Transform, string> id)
        {
            var result = new StudioModelConstraintData { kind = kind, active = component.constraintActive, locked = component.locked, weight = component.weight };
            foreach (var p in Properties[(int)kind])
            {
                var s = new StudioConstraintSetting { name = p.Name }; object value = p.GetValue(component);
                if (value is Vector3 v) s.vector = v;
                else if (value is float f) s.number = f;
                else if (value is bool b) s.flag = b;
                else s.integer = Convert.ToInt32(value);
                result.settings.Add(s);
            }
            var up = Types[(int)kind].GetProperty("worldUpObject");
            if (up != null) result.worldUpId = id((Transform)up.GetValue(component));
            for (int i = 0; i < component.sourceCount; i++)
            {
                var source = component.GetSource(i);
                var s = new StudioConstraintSource { nodeId = id(source.sourceTransform), weight = source.weight };
                if (component is ParentConstraint parent) { s.positionOffset = parent.GetTranslationOffset(i); s.rotationOffset = parent.GetRotationOffset(i); }
                result.sources.Add(s);
            }
            return result;
        }
        internal static void Apply(StudioModelConstraintData data, IConstraint component, Func<string, Transform> resolve)
        {
            component.constraintActive = false;
            component.locked = false;
            component.SetSources(data.sources.Select(s => new ConstraintSource { sourceTransform = resolve(s.nodeId), weight = s.weight }).ToList());
            foreach (var s in data.settings)
            {
                var p = Property(data.kind, s.name); if (p == null) continue;
                object value = p.PropertyType == typeof(Vector3) ? (object)s.vector : p.PropertyType == typeof(float) ? s.number : p.PropertyType == typeof(bool) ? s.flag : Enum.ToObject(p.PropertyType, s.integer);
                p.SetValue(component, value);
            }
            var up = Types[(int)data.kind].GetProperty("worldUpObject");
            if (up != null) up.SetValue(component, resolve(data.worldUpId));
            if (component is ParentConstraint parent)
                for (int i = 0; i < data.sources.Count; i++) { parent.SetTranslationOffset(i, data.sources[i].positionOffset); parent.SetRotationOffset(i, data.sources[i].rotationOffset); }
            component.weight = data.weight; component.locked = data.locked;
            // Missing/deleted links remain visible and repairable, never silently retargeted.
            component.constraintActive = data.active && !MissingReferences(data, resolve);
        }
        internal static bool MissingReferences(StudioModelConstraintData data, Func<string, Transform> resolve)
        {
            if (data.sources.Count == 0 || data.sources.Any(s => !resolve(s.nodeId))) return true;
            bool needsUp = data.kind == StudioConstraintKind.LookAt && data.settings.Any(s => s.name == "useUpObject" && s.flag);
            if (data.kind == StudioConstraintKind.Aim) needsUp = data.settings.Any(s => s.name == "worldUpType" && (s.integer == (int)AimConstraint.WorldUpType.ObjectUp || s.integer == (int)AimConstraint.WorldUpType.ObjectRotationUp));
            return needsUp && !resolve(data.worldUpId);
        }
        internal static void ReadUnlocked(StudioModelDocument document, int index, Transform target)
        {
            var node = document.nodes[index]; if (node.constraints == null) return;
            for (int i = 0; i < node.constraints.Count; i++)
            {
                var data = node.constraints[i]; if (!data.active || data.locked) continue;
                var component = (IConstraint)target.GetComponent(Types[(int)data.kind]);
                if (component == null) continue;
                var captured = Capture(component, data.kind, t => null);
                captured.worldUpId = data.worldUpId;
                for (int s = 0; s < captured.sources.Count; s++) captured.sources[s].nodeId = data.sources[s].nodeId;
                node.constraints[i] = captured;
            }
        }
        internal static void Activate(IConstraint component, StudioConstraintKind kind, bool zero)
        {
            var method = (zero ? ZeroMethods : ActivateMethods)[(int)kind];
            if (method == null) throw new NotSupportedException("This Unity version does not expose native constraint activation.");
            method.Invoke(component, null);
        }
        internal static void ValidateDocument(StudioModelDocument document, IReadOnlyDictionary<int, int> parents = null)
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < document.nodes.Count; i++)
            {
                var n = document.nodes[i];
                if (!string.IsNullOrEmpty(n.constraintId) && !ids.Add(n.constraintId)) throw new InvalidOperationException("Duplicate constraint object identity.");
                var kinds = new HashSet<StudioConstraintKind>();
                foreach (var c in n.constraints ?? new List<StudioModelConstraintData>())
                {
                    if (c == null || !kinds.Add(c.kind)) throw new InvalidOperationException("Duplicate or invalid constraint type on an object.");
                    Validate(document, i, c, parents);
                }
            }
        }
        internal static void Build(StudioModelDocument document, Transform[] transforms)
        {
            ValidateDocument(document);
            var byId = new Dictionary<string, Transform>();
            for (int i = 0; i < document.nodes.Count; i++)
                if (!string.IsNullOrEmpty(document.nodes[i].constraintId)) byId[document.nodes[i].constraintId] = transforms[i];
            Transform Resolve(string id) => !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var t) ? t : null;
            for (int i = 0; i < document.nodes.Count; i++)
                foreach (var data in document.nodes[i].constraints ?? new List<StudioModelConstraintData>())
                {
                    var component = (IConstraint)transforms[i].gameObject.AddComponent(Types[(int)data.kind]);
                    Apply(data, component, Resolve);
                }
        }
        internal static void Validate(StudioModelDocument document, int owner, StudioModelConstraintData data, IReadOnlyDictionary<int, int> parents = null)
        {
            if ((int)data.kind < 0 || (int)data.kind >= Types.Length || data.sources == null || data.settings == null)
                throw new InvalidOperationException("Invalid constraint data.");
            bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
            bool VectorFinite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
            if (!Finite(data.weight) || data.weight < 0 || data.weight > 1 || data.sources.Any(s => s == null || !Finite(s.weight) || s.weight < 0 || s.weight > 1 || !VectorFinite(s.positionOffset) || !VectorFinite(s.rotationOffset)) || data.settings.Any(s => s == null || !Finite(s.number) || !VectorFinite(s.vector)))
                throw new InvalidOperationException("Constraint values must be finite and weights between zero and one.");
            var ids = new Dictionary<string, int>();
            for (int i = 0; i < document.nodes.Count; i++) if (!string.IsNullOrEmpty(document.nodes[i].constraintId)) ids[document.nodes[i].constraintId] = i;
            var visiting = new HashSet<int>(); var visited = new HashSet<int>();
            void Visit(int index)
            {
                if (visited.Contains(index)) return;
                if (!visiting.Add(index)) throw new InvalidOperationException("Constraint sources cannot create a dependency cycle (including hierarchy parents and up objects).");
                var node = document.nodes[index];
                int parent = parents != null && parents.TryGetValue(index, out int replacement) ? replacement : node.parent;
                if (parent >= 0) Visit(parent);
                foreach (var c in index == owner ? (node.constraints ?? new List<StudioModelConstraintData>()).Where(c => c.kind != data.kind).Concat(new[] { data }) : node.constraints ?? new List<StudioModelConstraintData>())
                {
                    foreach (var s in c.sources) if (!string.IsNullOrEmpty(s.nodeId) && ids.TryGetValue(s.nodeId, out int dependency)) Visit(dependency);
                    if (!string.IsNullOrEmpty(c.worldUpId) && ids.TryGetValue(c.worldUpId, out int up)) Visit(up);
                }
                visiting.Remove(index); visited.Add(index);
            }
            Visit(owner);
        }
    }
}
