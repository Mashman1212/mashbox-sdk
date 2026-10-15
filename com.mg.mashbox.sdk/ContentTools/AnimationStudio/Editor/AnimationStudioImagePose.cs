using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private string imagePosePath = "";
        [SerializeField] private string imagePoseInputId = "";
        [SerializeField] private string imagePoseJobInputId = "";
        [SerializeField] private string imagePoseJob = "";
        [SerializeField] private bool imagePoseMirror;
        [SerializeField] private bool imagePoseKeepPosition = true;
        [SerializeField] private float imagePoseWeight = 1;
        private Texture2D imagePosePreview;

        private void DrawImagePoseMatcher()
        {
            EditorGUILayout.LabelField("AI POSE MATCHER · IMAGE", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(MotionRunning))
            {
                if (!imagePosePreview && File.Exists(imagePosePath)) Guard(() => LoadImagePoseReference(imagePosePath, false));
                Rect area = GUILayoutUtility.GetRect(1, 130, GUILayout.ExpandWidth(true));
                GUI.Box(area, GUIContent.none);
                if (imagePosePreview) GUI.DrawTexture(area, imagePosePreview, ScaleMode.ScaleToFit);
                else GUI.Label(area, "Drop an image here\nor click Paste screenshot", EditorStyles.centeredGreyMiniLabel);
                var evt = Event.current;
                if (!MotionRunning && area.Contains(evt.mousePosition))
                {
                    if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
                    {
                        string path = DragAndDrop.paths.FirstOrDefault(IsImagePoseFile);
                        if (path != null)
                        {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            if (evt.type == EventType.DragPerform)
                            { DragAndDrop.AcceptDrag(); Guard(() => LoadImagePoseReference(path)); }
                            evt.Use();
                        }
                    }
                    if ((evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) && evt.commandName == "Paste")
                    {
                        if (evt.type == EventType.ExecuteCommand) Guard(PasteImagePoseReference);
                        evt.Use();
                    }
                    else if (evt.type == EventType.KeyDown && (evt.control || evt.command) && evt.keyCode == KeyCode.V)
                    { Guard(PasteImagePoseReference); evt.Use(); }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Choose image…"))
                    {
                        string path = EditorUtility.OpenFilePanel("Pose reference image", "", "png,jpg,jpeg");
                        if (!string.IsNullOrEmpty(path)) Guard(() => LoadImagePoseReference(path));
                    }
                    if (GUILayout.Button("Paste screenshot")) Guard(PasteImagePoseReference);
                }
                if (!string.IsNullOrEmpty(imagePosePath)) EditorGUILayout.LabelField(Path.GetFileName(imagePosePath), EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                imagePoseMirror = EditorGUILayout.Toggle("Mirror reference", imagePoseMirror);
                if (EditorGUI.EndChangeCheck()) imagePoseInputId = Guid.NewGuid().ToString("N");
                bool ready = File.Exists(Path.Combine(MotionToolPath, ".venv/Scripts/python.exe")) && File.Exists(Path.Combine(MotionToolPath, "worker.py")) &&
                    File.Exists(Path.Combine(MotionToolPath, "models/pose_landmarker_heavy.task"));
                using (new EditorGUI.DisabledScope(!ready || !File.Exists(imagePosePath)))
                    if (GUILayout.Button("Estimate pose from image")) Guard(StartImagePoseJob);
            }
            imagePoseKeepPosition = EditorGUILayout.Toggle(new GUIContent("Keep actor position", "Preserve the current root and hips position when applying the estimated pose."), imagePoseKeepPosition);
            imagePoseWeight = EditorGUILayout.Slider("Pose strength", imagePoseWeight, 0, 1);
            bool hasResult = imagePoseInputId == imagePoseJobInputId && !string.IsNullOrEmpty(imagePoseJob) && File.Exists(Path.Combine(imagePoseJob, "motion.json"));
            using (new EditorGUI.DisabledScope(MotionRunning || !hasResult || !OutfitActor || OutfitRig == null || imagePoseWeight <= 0))
                if (GUILayout.Button("Apply pose at current frame", GUILayout.Height(28))) Guard(ApplyImagePose);
            if (!string.IsNullOrEmpty(imagePoseJob) && motionJob == imagePoseJob && motionStatus != null)
            {
                EditorGUILayout.HelpBox(motionStatus.message ?? "", motionStatus.state == "failed" ? MessageType.Error : MessageType.Info);
                if (MotionRunning)
                {
                    EditorGUI.ProgressBar(GUILayoutUtility.GetRect(1, 16), Mathf.Clamp01(motionStatus.progress), "Estimating pose");
                    if (GUILayout.Button("Cancel pose matching")) Guard(CancelMotionJob);
                }
            }
            EditorGUILayout.HelpBox("Local body pose estimate from one person. Crop around the full body. Apply keys the selected actor at the current frame; Undo is available. Depth, hidden limbs, fingers and twist may need FK/IK cleanup.", MessageType.None);
            EditorGUILayout.Space(10);
        }

        private static bool IsImagePoseFile(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return File.Exists(path) && (extension == ".png" || extension == ".jpg" || extension == ".jpeg");
        }

        private void RestoreImagePoseJob()
        {
            if (string.IsNullOrEmpty(motionJob) || imagePoseJob == motionJob) return;
            string path = Path.Combine(motionJob, "request.json");
            if (!File.Exists(path)) return;
            try
            {
                var request = JsonUtility.FromJson<MotionRequest>(File.ReadAllText(path));
                if (request == null || request.mode != "image") return;
                imagePoseJob = motionJob; imagePosePath = request.image; imagePoseMirror = request.mirror;
                imagePoseInputId = imagePoseJobInputId = motionJob;
            }
            catch (IOException) { }
        }

        private void ReleaseImagePosePreview()
        {
            if (imagePosePreview) DestroyImmediate(imagePosePreview);
            imagePosePreview = null;
        }

        private void LoadImagePoseReference(string path, bool invalidate = true)
        {
            if (!IsImagePoseFile(path)) throw new InvalidOperationException("Choose a PNG or JPEG image.");
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Choose an image smaller than 32 MB.");
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                    throw new InvalidOperationException("Could not read this image.");
                ReleaseImagePosePreview(); imagePosePreview = texture; imagePosePath = Path.GetFullPath(path);
                if (invalidate) imagePoseInputId = Guid.NewGuid().ToString("N");
                Repaint(); viewport?.Repaint();
            }
            catch { if (texture) DestroyImmediate(texture); throw; }
        }

        private void PasteImagePoseReference()
        {
            string script = Path.Combine(MotionToolPath, "clipboard_image.ps1");
            if (!File.Exists(script)) throw new InvalidOperationException("The clipboard image helper is missing from the local motion tools.");
            string folder = Path.Combine(MotionToolPath, "jobs", "reference-images"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Screenshot-" + Guid.NewGuid().ToString("N") + ".png");
            var info = new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File \"" + script + "\" -OutputPath \"" + path + "\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("Could not read the clipboard image.");
                if (!process.WaitForExit(5000)) { process.Kill(); throw new InvalidOperationException("Clipboard is busy. Copy the screenshot again and retry."); }
                if (process.ExitCode != 0 || !File.Exists(path)) throw new InvalidOperationException("No image on the clipboard. Copy a screenshot with Win+Shift+S, then click Paste screenshot.");
            }
            LoadImagePoseReference(path);
        }

        private void StartImagePoseJob()
        {
            if (!MotionGenerationAvailable) throw new InvalidOperationException("Pose matching is not enabled on this workstation.");
            if (MotionRunning) throw new InvalidOperationException("Wait for the current generation or cancel it first.");
            if (!IsImagePoseFile(imagePosePath)) throw new InvalidOperationException("Choose or paste a reference image first.");
            string job = Path.Combine(MotionToolPath, "jobs", "image-pose-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(job);
            string source = Path.Combine(job, "reference" + Path.GetExtension(imagePosePath)); File.Copy(imagePosePath, source);
            string request = Path.Combine(job, "request.json");
            File.WriteAllText(request, JsonUtility.ToJson(new MotionRequest { mode = "image", image = source, mirror = imagePoseMirror }));
            var info = new ProcessStartInfo(Path.Combine(MotionToolPath, ".venv/Scripts/python.exe"))
            {
                Arguments = "-u \"" + Path.Combine(MotionToolPath, "worker.py") + "\" \"" + request + "\"",
                WorkingDirectory = MotionToolPath, UseShellExecute = false, CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("Could not start the pose worker.");
                motionPid = process.Id; motionStartTicks = process.StartTime.ToUniversalTime().Ticks.ToString();
            }
            motionJob = imagePoseJob = job; imagePoseJobInputId = imagePoseInputId;
            motionStatus = new MotionStatus { state = "running", message = "Estimating body pose from image locally…" };
            RememberMotionJob();
        }

        private void ApplyImagePose()
        {
            if (MotionRunning || imagePoseInputId != imagePoseJobInputId || string.IsNullOrEmpty(imagePoseJob))
                throw new InvalidOperationException("Estimate the current reference image first.");
            var actor = OutfitActor;
            if (!actor || OutfitRig == null) throw new InvalidOperationException("Select a Humanoid actor first.");
            Action apply = () =>
            {
                FlushEdits();
                var data = JsonUtility.FromJson<GeneratedMotion>(File.ReadAllText(Path.Combine(imagePoseJob, "motion.json")));
                var estimated = AnimationMotionImporter.Create(data, rig, take.character, take.body, take.clothing, take.outfitOptions, false);
                try
                {
                    var current = rig.Capture(); var matched = estimated.keys[0].Copy(frame);
                    // Only mapped body bones are estimated. Keep fingers and other unmapped controls as authored.
                    var mapped = data.names.Select(n => rig.Animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), n))).Where(t => t).ToArray();
                    for (int i = 0; i < rig.Bones.Length; i++)
                        if (!mapped.Contains(rig.Bones[i])) matched.bones[i] = current.bones[i];
                    if (imagePoseKeepPosition)
                        for (int i = 0; i < matched.bones.Length; i++) matched.bones[i].position = current.bones[i].position;
                    var blended = PoseKey.Blend(current, matched, imagePoseWeight); blended.frame = frame;
                    // Make both FK and IK controls agree with the new pose before keying it.
                    rig.Apply(blended); blended = rig.Capture().Copy(frame);
                    for (int i = 0; i < blended.effectors.Length; i++)
                    { blended.effectors[i] = rig.MatchEffector(i, 0); }
                    Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(take, "Match pose from image");
                    pose = blended;
                    if (!WriteAuthoringKey()) { pose = current; rig.Apply(current); throw new InvalidOperationException(message); }
                    Save(); FlushSave(); sceneDraft = false; unkeyedPose = false; playing = false;
                    rig.Apply(pose); EvaluateConstraints(); viewport?.Repaint(); Repaint(); Undo.IncrementCurrentGroup();
                }
                finally { DestroyImmediate(estimated); }
            };
            if (activeActor >= 0 && activeActor < actorPreviews.Count) InActor(actorPreviews[activeActor], apply);
            else apply();
        }
    }
}
