using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioViewportRecoveryValidation
    {
        private const string Request = "Temp/StudioViewportRecovery.request";
        [InitializeOnLoadMethod] private static void Pending()
        { if (File.Exists(Request)) EditorApplication.update += RunPending; }
        private static void RunPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= RunPending;
            File.Delete(Request);
            MashBoxStudioViewport view = null;
            try
            {
                view = ScriptableObject.CreateInstance<MashBoxStudioViewport>();
                Require(!view.HasStudioBindings, "Unbound view must need recovery.");
                view.BindRigControls(() => { });
                view.BindInspector(() => { });
                view.BindTimeline(null);
                view.BindModes(() => { }, true);
                Require(view.HasStudioBindings, "Skinning/modeling without a timeline must count as bound.");
                view.BindModes(() => { }, false);
                Require(!view.HasStudioBindings, "Animation must still require its timeline callback.");
                view.BindTimeline(() => { });
                Require(view.HasStudioBindings, "Complete animation view must count as bound.");
                view.BindRigControls(null);
                Require(!view.HasStudioBindings, "Missing required panel callback must still trigger recovery.");
                File.WriteAllText("Temp/StudioViewportRecovery.result", "PASS: Skinning/modeling accept an intentionally absent timeline; animation and missing required bindings still trigger recovery.");
            }
            catch (Exception e) { File.WriteAllText("Temp/StudioViewportRecovery.result", e.ToString()); Debug.LogException(e); }
            finally { if (view) UnityEngine.Object.DestroyImmediate(view); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
