using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        // Private workstation preview. This is a UI feature gate, not an authorization boundary.
        private static readonly bool MotionGenerationAvailable = DetectMotionWorkstation();
        private static bool MatchesMotionWorkstation(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId)) return false;
            using (var hash = SHA256.Create())
            {
                string fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(machineId.Trim().ToLowerInvariant()))).Replace("-", "").ToLowerInvariant();
                return fingerprint == "d74c0596a59402a11718c16f02f566972ddd150a83b179c5392a6cf680d73cb0";
            }
        }
        private static bool DetectMotionWorkstation()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return false;
            try
            {
                using (var registry = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    return MatchesMotionWorkstation(key?.GetValue("MachineGuid") as string);
            }
            catch { return false; } // Missing/unreadable machine identity keeps the feature hidden.
        }
        [Serializable] private sealed class MotionRequest
        {
            public string mode, prompt, video;
            public float duration, start;
            public int seed;
            public bool inPlace;
        }
        [Serializable] private sealed class MotionStatus
        {
            public string state, message;
            public float progress;
        }
        [SerializeField] private int generationMode;
        [SerializeField] private string motionPrompt = "A person takes two steps forward, then waves with their right hand.";
        [SerializeField] private string motionVideo = "";
        [SerializeField] private float motionDuration = 3;
        [SerializeField] private float motionStart;
        [SerializeField] private int motionSeed = 42;
        [SerializeField] private bool motionInPlace;
        [SerializeField] private bool motionPlantFeet = true;
        [SerializeField] private bool motionUpdateCurrent = true;
        [SerializeField] private float motionContactSpeed = .25f;
        [SerializeField] private PoseKey motionStartPose, motionEndPose;
        [SerializeField] private string[] motionStartPaths, motionEndPaths;
        [SerializeField] private string motionStartLabel, motionEndLabel;
        [SerializeField] private float motionPoseBlend = .4f;
        [SerializeField] private bool motionStepTransitions = true;
        [SerializeField] private float motionStepDuration = .9f;
        [SerializeField] private float motionStepLift = .09f;
        [SerializeField] private float motionBodyWeight = .8f;
        [SerializeField] private AnimationClip motionStartClip, motionEndClip;
        [SerializeField] private float motionStartClipTime, motionEndClipTime;
        [SerializeField] private string motionJob = "";
        [SerializeField] private int motionPid;
        [SerializeField] private string motionStartTicks;
        [SerializeField] private bool generationExpanded = true;
        private MotionStatus motionStatus;
        private GUIStyle motionPromptStyle;
        private void DrawMotionPrompt()
        {
            if(motionPromptStyle==null)
            {
                motionPromptStyle=new GUIStyle(EditorStyles.textArea)
                { wordWrap=true,richText=false,alignment=TextAnchor.UpperLeft,padding=new RectOffset(8,8,7,7) };
            }
            // This inspector is hosted by SceneView: do not inherit its GUI skin colors.
            var textColor=EditorGUIUtility.isProSkin ? new Color(.92f,.94f,.96f) : new Color(.1f,.12f,.14f);
            foreach(var state in new[]{motionPromptStyle.normal,motionPromptStyle.hover,motionPromptStyle.active,motionPromptStyle.focused,
                motionPromptStyle.onNormal,motionPromptStyle.onHover,motionPromptStyle.onActive,motionPromptStyle.onFocused})
            { state.textColor=textColor; state.background=null; state.scaledBackgrounds=null; }
            Rect area=GUILayoutUtility.GetRect(0,88,GUILayout.ExpandWidth(true));
            var oldColor=GUI.color; var oldContent=GUI.contentColor;
            try
            {
                GUI.color=GUI.contentColor=Color.white;
                bool focused=GUI.GetNameOfFocusedControl()=="MashBoxMovementPrompt";
                EditorGUI.DrawRect(area,focused ? new Color(.25f,.65f,.82f) : new Color(.4f,.44f,.48f));
                var inner=new Rect(area.x+1,area.y+1,area.width-2,area.height-2);
                EditorGUI.DrawRect(inner,EditorGUIUtility.isProSkin ? new Color(.09f,.11f,.13f) : new Color(.96f,.97f,.98f));
                GUI.SetNextControlName("MashBoxMovementPrompt");
                motionPrompt=EditorGUI.TextArea(inner,motionPrompt,motionPromptStyle);
            }
            finally { GUI.color=oldColor; GUI.contentColor=oldContent; }
        }
        private double motionPollAfter;
        private static string MotionToolDefault => Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/MotionGeneration"));
        private static string MotionToolPath => EditorPrefs.GetString("MashBox.MotionToolPath", MotionToolDefault);
        private bool MotionRunning => motionPid != 0;

        private void RestoreMotionJob()
        {
            if (!MotionGenerationAvailable) return;
            if (!string.IsNullOrEmpty(motionJob)) return;
            string key = "MashBox.MotionJob." + Application.dataPath;
            motionJob = SessionState.GetString(key, "");
            motionPid = SessionState.GetInt(key + ".pid", 0);
            motionStartTicks = SessionState.GetString(key + ".start", "");
        }
        private void RememberMotionJob()
        {
            string key = "MashBox.MotionJob." + Application.dataPath;
            SessionState.SetString(key, motionJob);
            SessionState.SetInt(key + ".pid", motionPid);
            SessionState.SetString(key + ".start", motionStartTicks);
        }

        private void DrawGeneration()
        {
            if (!MotionGenerationAvailable) return;
            DrawInbetweenTools();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("GENERATE MOTION · LOCAL", EditorStyles.boldLabel);
            generationMode = GUILayout.Toolbar(generationMode, new[] { "Text · GPU", "Video · CPU" });
            using (new EditorGUI.DisabledScope(MotionRunning))
            {
                if (generationMode == 0)
                {
                    EditorGUILayout.LabelField("Describe the movement");
                    DrawMotionPrompt();
                    motionSeed = EditorGUILayout.IntField("Seed", motionSeed);
                    motionInPlace = EditorGUILayout.Toggle(new GUIContent("Keep motion in place", "Remove horizontal root travel while keeping vertical motion."), motionInPlace);
                    EditorGUILayout.HelpBox("HY-Motion Lite · local GPU. Use short English action descriptions. Its community license includes territorial restrictions on outputs.", MessageType.Info);
                    if (GUILayout.Button("HY-Motion license")) Application.OpenURL("https://github.com/Tencent-Hunyuan/HY-Motion-1.0/blob/master/License.txt");
                }
                else
                {
                    EditorGUILayout.LabelField("Reference video", string.IsNullOrEmpty(motionVideo) ? "None" : Path.GetFileName(motionVideo));
                    if (GUILayout.Button("Choose video…"))
                    {
                        var path = EditorUtility.OpenFilePanel("Motion reference video", "", "mp4,mov,avi,webm,mkv");
                        if (!string.IsNullOrEmpty(path)) motionVideo = path;
                    }
                    motionStart = Mathf.Max(0, EditorGUILayout.FloatField("Start time (seconds)", motionStart));
                    EditorGUILayout.HelpBox("Local CPU pose capture. Use one person with the full body visible and a stationary camera. Produces in-place body motion; fingers, axial twist and world travel are not recovered.", MessageType.Info);
                }
                motionDuration = EditorGUILayout.Slider("Duration (seconds)", motionDuration, 1, generationMode == 0 ? 8 : 30);
                bool ready = File.Exists(Path.Combine(MotionToolPath, ".venv/Scripts/python.exe")) && File.Exists(Path.Combine(MotionToolPath, "worker.py"));
                if (!ready) EditorGUILayout.HelpBox("Local motion tools are missing. Select the installed MotionGeneration folder below.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(!ready || (generationMode == 0 ? string.IsNullOrWhiteSpace(motionPrompt) : !File.Exists(motionVideo))))
                    if (GUILayout.Button("Generate motion", GUILayout.Height(30))) Guard(StartMotionJob);
            }
            if (motionStatus != null && !string.IsNullOrEmpty(motionStatus.message))
            {
                EditorGUILayout.HelpBox(motionStatus.message ?? "", motionStatus.state == "failed" ? MessageType.Error : MessageType.Info);
                if (MotionRunning) EditorGUI.ProgressBar(GUILayoutUtility.GetRect(1, 16), Mathf.Clamp01(motionStatus.progress), "Local processing");
            }
            if (MotionRunning && GUILayout.Button("Cancel generation")) Guard(CancelMotionJob);
            motionUpdateCurrent = EditorGUILayout.Toggle("Update current take", motionUpdateCurrent);
            EditorGUILayout.HelpBox(motionUpdateCurrent ? "Apply replaces the current take's motion and length. Undo restores the previous motion; the existing asset saves automatically." : "Import saves a new take asset.", MessageType.None);
            DrawMotionEndpoints();
            motionPlantFeet = EditorGUILayout.Toggle("Plant feet on import", motionPlantFeet);
            motionContactSpeed = EditorGUILayout.Slider(new GUIContent("Contact sensitivity", "Maximum foot speed in leg lengths per second. Higher values catch more sliding but may suppress intentional shuffles."), motionContactSpeed, .05f, .6f);
            using (new EditorGUI.DisabledScope(MotionRunning || rig == null || (motionUpdateCurrent && !take) || !File.Exists(Path.Combine(motionJob, "motion.json"))))
                if (GUILayout.Button(motionUpdateCurrent ? "Apply result to current take" : "Import result as a new take…")) Guard(ImportGeneratedMotion);
            using (new EditorGUI.DisabledScope(rig == null || !take || take.keys.Count == 0))
                if (GUILayout.Button(motionUpdateCurrent ? "Clean up feet in current take" : "Clean up feet as a new take…")) Guard(CleanUpMotionFeet);
            EditorGUILayout.HelpBox("Foot cleanup locks slow, low feet during detected contacts. Reduce sensitivity for shuffles or deliberate sliding. Keep motion in place removes travel and can cause sliding in travelling dances.", MessageType.None);
            if (!string.IsNullOrEmpty(motionJob) && File.Exists(Path.Combine(motionJob, "worker.log")) && GUILayout.Button("Open generation log"))
                EditorUtility.OpenWithDefaultApp(Path.Combine(motionJob, "worker.log"));
            generationExpanded = EditorGUILayout.Foldout(generationExpanded, "Local tools", true);
            if (generationExpanded)
            {
                EditorGUILayout.LabelField(MotionToolPath, EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(MotionRunning))
                    if (GUILayout.Button("Locate motion tools…"))
                    {
                        string folder = EditorUtility.OpenFolderPanel("Select MotionGeneration folder", MotionToolPath, "");
                        if (!string.IsNullOrEmpty(folder)) EditorPrefs.SetString("MashBox.MotionToolPath", folder);
                    }
            }
        }

        private void StartMotionJob()
        {
            if (!MotionGenerationAvailable) throw new InvalidOperationException("Motion generation is not enabled on this workstation.");
            if (MotionRunning) throw new InvalidOperationException("A generation is already running.");
            motionJob = Path.Combine(MotionToolPath, "jobs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(motionJob);
            string request = Path.Combine(motionJob, "request.json");
            File.WriteAllText(request, JsonUtility.ToJson(new MotionRequest { mode = generationMode == 0 ? "text" : "video",
                prompt = motionPrompt, video = motionVideo, duration = motionDuration, start = motionStart,
                seed = motionSeed, inPlace = motionInPlace }));
            var info = new ProcessStartInfo(Path.Combine(MotionToolPath, ".venv/Scripts/python.exe"))
            {
                Arguments = "-u \"" + Path.Combine(MotionToolPath, "worker.py") + "\" \"" + request + "\"",
                WorkingDirectory = MotionToolPath, UseShellExecute = false, CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("Could not start the motion worker.");
                motionPid = process.Id;
                motionStartTicks = process.StartTime.ToUniversalTime().Ticks.ToString();
            }
            motionStatus = new MotionStatus { state = "running", message = "Starting local motion worker…" };
            RememberMotionJob();
        }

        private void PollMotionJob()
        {
            if (!MotionGenerationAvailable) return;
            if (string.IsNullOrEmpty(motionJob) || EditorApplication.timeSinceStartup < motionPollAfter) return;
            motionPollAfter = EditorApplication.timeSinceStartup + .5;
            if (!MotionRunning && motionStatus != null) return;
            try
            {
                string path = Path.Combine(motionJob, "status.json");
                if (File.Exists(path)) motionStatus = JsonUtility.FromJson<MotionStatus>(File.ReadAllText(path));
                if (MotionRunning)
                {
                    using (var process = GetMotionProcess())
                    {
                        if (process == null)
                        {
                            motionPid = 0;
                            RememberMotionJob();
                            if (motionStatus == null || motionStatus.state == "running")
                                motionStatus = new MotionStatus { state = "failed", message = "Motion worker stopped before completing. Check the generation log." };
                        }
                    }
                }
                Repaint(); if (viewport) viewport.Repaint();
            }
            catch (IOException) { /* Atomic status replacement may briefly hold the file on Windows. */ }
        }

        private Process GetMotionProcess()
        {
            if (motionPid == 0) return null;
            Process process = null;
            try
            {
                process = Process.GetProcessById(motionPid);
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks.ToString() == motionStartTicks) return process;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            process?.Dispose(); return null;
        }
        private void CancelMotionJob()
        {
            using (var process = GetMotionProcess()) if (process != null) process.Kill();
            motionPid = 0;
            RememberMotionJob();
            motionStatus = new MotionStatus { state = "cancelled", message = "Generation cancelled." };
            File.WriteAllText(Path.Combine(motionJob, "status.json"), JsonUtility.ToJson(motionStatus));
        }
        private void ImportGeneratedMotion()
        {
            if (!MotionGenerationAvailable) throw new InvalidOperationException("Motion generation is not enabled on this workstation.");
            string path = motionUpdateCurrent ? null : EditorUtility.SaveFilePanelInProject("Import generated motion", generationMode == 0 ? "Generated Motion" : "Video Motion", "asset", "Create an editable animation take.");
            if (!motionUpdateCurrent && string.IsNullOrEmpty(path)) return;
            var data = JsonUtility.FromJson<GeneratedMotion>(File.ReadAllText(Path.Combine(motionJob, "motion.json")));
            var created = AnimationMotionImporter.Create(data, rig, character, body, clothing, outfitOptions);
            try
            {
                if (motionPlantFeet) AnimationMotionImporter.PlantFeet(created, rig, motionContactSpeed);
                ApplyMotionEndpoints(created);
                StoreMotionResult(created,path,"Apply generated motion");
                motionStatus = new MotionStatus { state = "complete", message = data.provider + ": imported " + created.keys.Count + " frames including transitions. " + data.note, progress = 1 };
            }
            finally { if (created && !EditorUtility.IsPersistent(created)) DestroyImmediate(created); }
        }

        private void StoreMotionResult(AnimationTake result, string path, string label)
        {
            if (path != null)
            {
                AssetDatabase.CreateAsset(result,AssetDatabase.GenerateUniqueAssetPath(path));
                Undo.RegisterCreatedObjectUndo(result,label); LoadTake(result);
                return;
            }
            if (!take || rig == null || !take.bonePaths.SequenceEqual(result.bonePaths))
                throw new InvalidOperationException("The current take must match the generated skeleton.");
            // All generation, cleanup and validation finish before the existing asset changes.
            var keys = result.keys.Select(k => k.Copy(k.frame)).ToList();
            FinishSceneEdit(); playing = false;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(label);
            Undo.RegisterCompleteObjectUndo(take,label);
            take.keys = keys;
            take.frameRate = result.frameRate; take.lastFrame = result.lastFrame;
            take.endpointLeadFrames = result.endpointLeadFrames; take.endpointTailFrames = result.endpointTailFrames;
            take.interpolation = result.interpolation; take.loop = result.loop;
            Save(); Seek(0);
            Undo.IncrementCurrentGroup();
        }

        private void CleanUpMotionFeet()
        {
            if (!MotionGenerationAvailable) return;
            string path = motionUpdateCurrent ? null : EditorUtility.SaveFilePanelInProject("Save foot cleanup", take.name + " Planted", "asset", "Create a copy with foot contacts stabilized.");
            if (!motionUpdateCurrent && string.IsNullOrEmpty(path)) return;
            var cleaned = Instantiate(take);
            var cleanupLayers=cleaned.layers;
            cleaned.layers=new System.Collections.Generic.List<StudioAnimationLayer>(); cleaned.activeLayer=-1;
            try
            {
                int contacts = AnimationMotionImporter.PlantFeet(cleaned, rig, motionContactSpeed);
                if (contacts == 0) throw new InvalidOperationException("No sustained ground contacts detected. Try increasing contact sensitivity slightly.");
                cleaned.layers=cleanupLayers;
                StoreMotionResult(cleaned,path,"Clean up motion feet");
                motionStatus = new MotionStatus { state = "complete", message = "Stabilized " + contacts + " foot contacts. Undo is available.", progress = 1 };
            }
            finally { if (cleaned && !EditorUtility.IsPersistent(cleaned)) DestroyImmediate(cleaned); }
        }

        private bool HasMotionStart => motionStartPose?.bones != null && motionStartPose.bones.Length > 0;
        private bool HasMotionEnd => motionEndPose?.bones != null && motionEndPose.bones.Length > 0;

        private void DrawMotionEndpoints()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("START / END POSES", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Capture the current pose, or choose an animation clip and sample a time below. Endpoint transitions are applied on import; these poses are not sent to the AI generator. Captures include hips position and orientation.", MessageType.None);
            for (int i = 0; i < 2; i++)
            {
                bool start = i == 0;
                EditorGUILayout.LabelField(start ? "Start pose" : "End pose", !(start ? HasMotionStart : HasMotionEnd) ? "Not set" : (start ? motionStartLabel : motionEndLabel), EditorStyles.wordWrappedMiniLabel);
                var clip = (AnimationClip)EditorGUILayout.ObjectField(start ? "Start animation clip" : "End animation clip", start ? motionStartClip : motionEndClip, typeof(AnimationClip), false);
                float time = start ? motionStartClipTime : motionEndClipTime;
                if (clip != (start ? motionStartClip : motionEndClip)) time = 0;
                if (clip) time = EditorGUILayout.Slider("Sample time (seconds)", time, 0, clip.length);
                if (start) { motionStartClip = clip; motionStartClipTime = time; }
                else { motionEndClip = clip; motionEndClipTime = time; }
                using (new EditorGUI.DisabledScope(rig == null || !clip))
                    if (GUILayout.Button(start ? "Use clip pose as start" : "Use clip pose as end"))
                        Guard(() => CaptureMotionClip(start, clip, time));
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(rig == null))
                        if (GUILayout.Button(start ? "Capture current as start" : "Capture current as end"))
                        {
                            var captured = rig.Capture();
                            string label = (take ? take.name : "Current pose") + " · frame " + frame;
                            if (start) { motionStartPose = captured; motionStartPaths = (string[])rig.Paths.Clone(); motionStartLabel = label; }
                            else { motionEndPose = captured; motionEndPaths = (string[])rig.Paths.Clone(); motionEndLabel = label; }
                        }
                    if (GUILayout.Button("Clear", GUILayout.Width(45)))
                    { if (start) motionStartPose = null; else motionEndPose = null; }
                }
            }
            using (new EditorGUI.DisabledScope(!HasMotionStart))
                if (GUILayout.Button("Use start pose for end too"))
                { motionEndPose = motionStartPose.Copy(0); motionEndPaths = (string[])motionStartPaths.Clone(); motionEndLabel = motionStartLabel; }
            motionStepTransitions = EditorGUILayout.Toggle("Step between stances", motionStepTransitions);
            if (motionStepTransitions)
            {
                motionStepDuration = EditorGUILayout.Slider("Transition at each end (s)", motionStepDuration,.2f,2f);
                motionStepLift = EditorGUILayout.Slider("Foot lift / leg length",motionStepLift,.02f,.2f);
                motionBodyWeight = EditorGUILayout.Slider("Body weight shift",motionBodyWeight,0,1);
                EditorGUILayout.HelpBox("Adds a two-step lead-in/out, keeping the full dance. One foot moves at a time. For grounded poses on a flat floor; extreme stance changes may still need cleanup. This is procedural stepping, not AI-conditioned motion.",MessageType.None);
            }
            else motionPoseBlend = EditorGUILayout.Slider("Blend at each end (s)", motionPoseBlend, .1f, 2f);
            using (new EditorGUI.DisabledScope(rig == null || !take || take.keys.Count == 0 || (!HasMotionStart && !HasMotionEnd)))
                if (GUILayout.Button(motionUpdateCurrent ? "Apply endpoint transitions to current take" : "Match current take to these poses…")) Guard(MatchCurrentMotionEndpoints);
        }

        private void ApplyMotionEndpoints(AnimationTake target)
        {
            if ((HasMotionStart && (motionStartPaths == null || !motionStartPaths.SequenceEqual(target.bonePaths))) ||
                (HasMotionEnd && (motionEndPaths == null || !motionEndPaths.SequenceEqual(target.bonePaths))))
                throw new InvalidOperationException("Endpoint poses were captured on a different skeleton. Clear or recapture them.");
            if (motionStepTransitions) AnimationMotionImporter.ConnectWithSteps(target,rig,HasMotionStart ? motionStartPose : null,HasMotionEnd ? motionEndPose : null,motionStepDuration,motionStepLift,motionBodyWeight);
            else AnimationMotionImporter.MatchEndpoints(target, HasMotionStart ? motionStartPose : null, HasMotionEnd ? motionEndPose : null, motionPoseBlend);
        }

        private void CaptureMotionClip(bool start, AnimationClip clip, float time)
        {
            var captured = AnimationMotionImporter.SampleEndpoint(clip, time, character, rig.Paths);
            string label = clip.name + " · " + time.ToString("0.###") + " s";
            if (start) { motionStartPose = captured; motionStartPaths = (string[])rig.Paths.Clone(); motionStartLabel = label; }
            else { motionEndPose = captured; motionEndPaths = (string[])rig.Paths.Clone(); motionEndLabel = label; }
            Repaint(); viewport?.Repaint();
        }

        private void MatchCurrentMotionEndpoints()
        {
            if (!MotionGenerationAvailable) return;
            string path = motionUpdateCurrent ? null : EditorUtility.SaveFilePanelInProject("Save pose matched take", take.name + " Matched", "asset", "Create a copy with matching start/end poses.");
            if (!motionUpdateCurrent && string.IsNullOrEmpty(path)) return;
            var matched = Instantiate(take);
            var endpointLayers=matched.layers;
            matched.layers=new System.Collections.Generic.List<StudioAnimationLayer>(); matched.activeLayer=-1;
            try
            {
                ApplyMotionEndpoints(matched);
                matched.layers=endpointLayers;
                StoreMotionResult(matched,path,"Match motion endpoints");
                motionStatus = new MotionStatus { state = "complete", message = "Start/end transitions applied to a new take. Original take preserved.", progress = 1 };
            }
            finally { if (matched && !EditorUtility.IsPersistent(matched)) DestroyImmediate(matched); }
        }
    }
}


