using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        // Unity exposes explicit sizes for move/scale, but its rotation size
        // parameters are internal. Bind once to the native implementation so
        // radius changes retain native ring picking, snapping and drag feedback.
        // The compiled delegate passes the value-type parameters without boxing
        // or reflection allocations during GUI events.
        private static readonly Func<Handles.RotationHandleIds, Quaternion, Vector3, float, Quaternion> SizedRotationHandle = CreateSizedRotationHandle();
        private static Func<Handles.RotationHandleIds, Quaternion, Vector3, float, Quaternion> CreateSizedRotationHandle()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
            var param = typeof(Handles).GetNestedType("RotationHandleParam", flags);
            var constructor = param.GetConstructor(flags | BindingFlags.Instance, null,
                new[] { param.GetNestedType("Handle", flags), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(bool) }, null);
            var ids = Expression.Parameter(typeof(Handles.RotationHandleIds), "ids");
            var rotation = Expression.Parameter(typeof(Quaternion), "rotation");
            var center = Expression.Parameter(typeof(Vector3), "center");
            var size = Expression.Parameter(typeof(float), "size");
            var defaults = Expression.Property(null, param.GetProperty("Default", flags | BindingFlags.Static));
            var parameters = Expression.New(constructor,
                Expression.Field(defaults, "handles"),
                Expression.Multiply(Expression.Field(defaults, "axisSize"), size),
                Expression.Multiply(Expression.Field(defaults, "xyzSize"), size),
                Expression.Multiply(Expression.Field(defaults, "cameraAxisSize"), size),
                Expression.Field(defaults, "enableRayDrag"), Expression.Field(defaults, "displayXYZCircle"));
            var method = typeof(Handles).GetMethod("DoRotationHandle", flags | BindingFlags.Static, null,
                new[] { typeof(Handles.RotationHandleIds), typeof(Quaternion), typeof(Vector3), param }, null);
            return Expression.Lambda<Func<Handles.RotationHandleIds, Quaternion, Vector3, float, Quaternion>>(
                Expression.Call(method, ids, rotation, center, parameters), ids, rotation, center, size).Compile();
        }
        private static readonly (Func<float> read, Action<float> write) RotationPreviewAngle = CreateRotationPreviewAngleAccess();
        private static (Func<float>, Action<float>) CreateRotationPreviewAngleAccess()
        {
            var disc = typeof(Editor).Assembly.GetType("UnityEditorInternal.Disc");
            var field = disc?.GetField("s_RotationDist", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(float)) return default;
            var angle = Expression.Parameter(typeof(float), "angle");
            return (Expression.Lambda<Func<float>>(Expression.Field(null, field)).Compile(),
                Expression.Lambda<Action<float>>(Expression.Block(Expression.Assign(Expression.Field(null, field), angle), Expression.Empty()), angle).Compile());
        }
        private readonly struct RotationPreviewScope : IDisposable
        {
            private readonly bool wrapped;
            private readonly float fullAngle;
            internal RotationPreviewScope(Handles.RotationHandleIds ids)
            {
                fullAngle = 0; wrapped = false;
                int hot = GUIUtility.hotControl;
                if (Event.current == null || Event.current.type != EventType.Repaint || hot == 0 ||
                    (hot != ids.x && hot != ids.y && hot != ids.z && hot != ids.cameraAxis) || RotationPreviewAngle.read == null) return;
                fullAngle = RotationPreviewAngle.read();
                if (Mathf.Abs(fullAngle) < 360) return;
                // Native Disc draws an extra translucent disc for each complete
                // turn. Wrap only while painting Studio's preview; native drag
                // accumulation and snapping retain their full angle afterwards.
                wrapped = true;
                RotationPreviewAngle.write(fullAngle % 360f);
            }
            public void Dispose()
            {
                if (wrapped) RotationPreviewAngle.write(fullAngle);
            }
        }
        private static Quaternion DrawSizedRotationHandle(Handles.RotationHandleIds ids, Quaternion rotation, Vector3 center, float size)
        {
            using (new RotationPreviewScope(ids)) return SizedRotationHandle(ids, rotation, center, size);
        }
        internal static bool HandleSizeShortcut(Event evt)
        {
            // SceneView navigation retains keyboardControl after orbit/pan ends.
            // Only text editing or an active mouse gesture should block these keys.
            if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
                GUIUtility.hotControl != 0) return false;
            bool increase = StudioShortcuts.Pressed(StudioShortcut.LargerHandles,evt);
            bool decrease = StudioShortcuts.Pressed(StudioShortcut.SmallerHandles,evt);
            if (!increase && !decrease) return false;
            var settings = StudioSettings.Current;
            float next = Mathf.Clamp(settings.handleSize * (increase ? 1.2f : 1 / 1.2f), .25f, 3);
            if (!Mathf.Approximately(next, settings.handleSize)) { settings.handleSize = next; StudioSettings.Save(); }
            evt.Use(); return true;
        }
    }
}
