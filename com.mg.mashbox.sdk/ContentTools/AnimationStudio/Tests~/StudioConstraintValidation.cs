using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.AnimationStudio
{
    // Explicit request, disposable project only, never exits an editor or writes preferences.
    [InitializeOnLoad] public static class StudioConstraintValidation
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Constraints")) + Path.DirectorySeparatorChar;
        private static StudioModelDocument doc;
        private static Transform[] transforms;
        private static ScaleConstraint scaleBaseline;
        private static Scene scene;
        private static int checks, frames;
        static StudioConstraintValidation()
        {
            if (Application.dataPath.Replace('\\','/').EndsWith("/SelectionUndoProject/Assets")) EditorApplication.update += PollRequest;
        }
        private static void PollRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(Root + "refresh.txt")) { File.Delete(Root + "refresh.txt"); AssetDatabase.Refresh(); return; }
            if (!doc && File.Exists(Root + "request.txt")) { File.Delete(Root + "request.txt"); Run(); }
        }        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        public static void Run()
        {
            if (!Application.dataPath.Replace('\\','/').EndsWith("/SelectionUndoProject/Assets")) throw new Exception("Disposable project only.");
            try
            {
                checks = 0; EditorApplication.update -= PollRequest; EditorApplication.update += PollRequest;
                doc = ScriptableObject.CreateInstance<StudioModelDocument>();
                doc.nodes.Add(new StudioModelDocument.Node { name = "Source", position = new Vector3(4, 2, 3), rotation = Quaternion.Euler(0, 30, 0), scale = new Vector3(2, 3, 4) });
                for (int k = 0; k < 6; k++) doc.nodes.Add(new StudioModelDocument.Node { name = "Target " + k, position = new Vector3(k, 0, 0) });
                scene = EditorSceneManager.NewPreviewScene(); transforms = doc.Build(scene);
                for (int k = 0; k < 6; k++)
                {
                    var native = (IConstraint)transforms[k + 1].gameObject.AddComponent(StudioModelConstraints.Types[k]);
                    native.AddSource(new ConstraintSource { sourceTransform = transforms[0], weight = 1 });
                    StudioModelConstraints.Activate(native, (StudioConstraintKind)k, true);
                    var c = StudioModelConstraints.Capture(native, (StudioConstraintKind)k, t => t ? doc.nodes[Array.IndexOf(transforms,t)].constraintId : null);
                    doc.nodes[k + 1].constraints.Add(c);
                    Check(c.active && c.locked && c.sources[0].nodeId == doc.nodes[0].constraintId, "Activate/portable source " + k);
                }
                Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder); string path = Path.Combine(StudioModelDocument.WorkspaceFolder, "constraint-roundtrip.workspace"); if (File.Exists(path)) File.Delete(path);
                StudioWorkspaceText.Write(doc, path);
                var loaded = StudioWorkspaceText.Read(path);
                Check(loaded.nodes.Skip(1).All(n => n.constraints.Count == 1), "All six saved");
                Check(loaded.nodes[1].constraints[0].sources[0].nodeId == loaded.nodes[0].constraintId, "Source survives read");
                UnityEngine.Object.DestroyImmediate(loaded);
                var invalid = doc.nodes[1].constraints[0].Copy(); invalid.sources[0].nodeId = doc.nodes[1].constraintId;
                bool rejected = false; try { StudioModelConstraints.Validate(doc,1,invalid); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Self-cycle rejected");
                Check(!doc.CanReparent(new[] { 0 }, 1, transforms, out _), "Hierarchy constraint cycle rejected before mutation");
                Check(doc.nodes[0].parent == -1, "Rejected hierarchy unchanged");
                var roots = doc.DuplicateObjects(new[] { 0, 1 });
                Check(doc.nodes[8].constraints[0].sources[0].nodeId == doc.nodes[7].constraintId, "Duplicate remaps source");
                Check(doc.nodes[8].constraints[0] != doc.nodes[1].constraints[0], "Duplicate deep copy");
                // Rebuild discards all component instances, exercising stable persistence links.
                foreach (var go in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);
                transforms = doc.Build(scene);
                for (int k = 0; k < 6; k++) Check(((IConstraint)transforms[k+1].GetComponent(StudioModelConstraints.Types[k])).GetSource(0).sourceTransform == transforms[0], "Rebuild source " + k);
                                var baselineObject = new GameObject("Native scale baseline"); SceneManager.MoveGameObjectToScene(baselineObject, scene);
                scaleBaseline = baselineObject.AddComponent<ScaleConstraint>();
                scaleBaseline.AddSource(new ConstraintSource { sourceTransform = transforms[0], weight = 1 });
                StudioModelConstraints.Activate(scaleBaseline, StudioConstraintKind.Scale, true);
                transforms[0].position = new Vector3(9,4,6);
                doc.ReadTransform(0,transforms[0]);
                frames = 0; EditorApplication.update += WaitForEvaluation;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch(Exception e) { Finish(e); }
        }
        private static void WaitForEvaluation()
        {
            if (++frames < 20) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            EditorApplication.update -= WaitForEvaluation;
            try
            {
                Check(Vector3.Distance(transforms[4].position, transforms[0].position) < .001f, "Position follows source in preview scene: " + transforms[4].position);
                Check(Quaternion.Angle(transforms[5].rotation, transforms[0].rotation) < .01f, "Rotation follows");
                Check(Vector3.Distance(transforms[6].localScale, scaleBaseline.transform.localScale) < .00001f, "Scale follows: actual=" + transforms[6].localScale + " source=" + transforms[0].localScale + " data=" + JsonUtility.ToJson(doc.nodes[6].constraints[0]));
                Check(Vector3.Distance(transforms[3].position, transforms[0].position) < .001f, "Parent follows position");
                Check(Quaternion.Angle(transforms[3].rotation, transforms[0].rotation) < .01f, "Parent follows rotation");
                Check(Vector3.Dot(transforms[2].forward, (transforms[0].position-transforms[2].position).normalized) > .999f, "Look At aims Z");
                var aim = transforms[1].GetComponent<AimConstraint>();
                Check(Vector3.Dot(transforms[1].TransformDirection(aim.aimVector).normalized, (transforms[0].position-transforms[1].position).normalized) > .999f, "Aim points at source");
                Check(Vector3.Distance(transforms[6].localScale, transforms[0].localScale) < .04f, "Scale is close to source (native logarithm approximation)");
                                var positionConstraint = transforms[4].GetComponent<PositionConstraint>();
                positionConstraint.locked = false; doc.nodes[4].constraints[0].locked = false;
                transforms[4].position += new Vector3(1,2,3); doc.ReadTransform(4, transforms[4]);
                frames=0; EditorApplication.update += WaitForOffsets; EditorApplication.QueuePlayerLoopUpdate();
            }
            catch(Exception e) { Finish(e); }
        }
        private static void WaitForOffsets()
        {
            if (++frames < 20) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            EditorApplication.update -= WaitForOffsets;
            try
            {
                var positionConstraint=transforms[4].GetComponent<PositionConstraint>();
                StudioModelConstraints.ReadUnlocked(doc,4,transforms[4]);                Check(Vector3.Distance(doc.nodes[4].constraints[0].settings.First(s=>s.name=="translationOffset").vector, positionConstraint.translationOffset)<.0001f, "Unlocked offset persisted");
                var preservedPosition=transforms[4].position;
                StudioModelConstraints.Activate(positionConstraint, StudioConstraintKind.Position, false);
                Check(Vector3.Distance(preservedPosition,transforms[4].position)<.0001f,"Activate preserves pose");
                Check(positionConstraint.translationOffset.sqrMagnitude>1,"Activate retains nonzero offset");
                StudioUndo.IncrementCurrentGroup(); StudioUndo.RegisterCompleteObjectUndo(doc,"Constraint weight");
                doc.nodes[4].constraints[0].weight = .25f;
                EditorUtility.SetDirty(doc); UnityEditor.Undo.FlushUndoRecordObjects(); StudioUndo.IncrementCurrentGroup();
                StudioUndo.PerformUndo(); Check(doc.nodes[4].constraints[0].weight == 1, "Constraint Undo");
                StudioUndo.PerformRedo(); Check(doc.nodes[4].constraints[0].weight == .25f, "Constraint Redo");
                string removedId = doc.nodes[0].constraintId;
                doc.DeleteObjects(new[] { 0 });
                Check(doc.nodes[3].constraints[0].sources[0].nodeId == removedId, "Deleted source ID retained for repair");
                foreach (var go in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);
                transforms=doc.Build(scene);
                Check(!transforms[3].GetComponent<PositionConstraint>().constraintActive, "Missing source disables evaluation");
                Finish(null);
            }
            catch(Exception e) { Finish(e); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= WaitForEvaluation;
            EditorApplication.update -= WaitForOffsets;
            File.WriteAllText(Root+"result.txt", error == null ? "PASS " + checks + " checks" : "FAIL after " + checks + ": " + error);
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
            if(doc)UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
