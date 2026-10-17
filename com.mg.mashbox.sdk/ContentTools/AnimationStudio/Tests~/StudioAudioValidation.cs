using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioAudioValidation
    {
        private static int passed;
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            passed++; Debug.Log("PASS: " + name);
        }
        public static void Run()
        {
            try
            {
                var take = ScriptableObject.CreateInstance<AnimationTake>();
                take.frameRate = 30; take.lastFrame = 60;
                take.audioCues.AddRange(new[] {
                    new StudioAudioCue { frame = 0 }, new StudioAudioCue { frame = 10 },
                    new StudioAudioCue { frame = 11 }, new StudioAudioCue { frame = 60 },
                    new StudioAudioCue { frame = 61 }, new StudioAudioCue { frame = 8, enabled = false } });
                Func<double, double, bool, int[]> crossed = (a,b,start) => StudioAudioTiming.Crossed(take,a,b,start).Select(c=>c.frame).ToArray();
                Check(crossed(0,0,true).SequenceEqual(new[]{0}), "Starting playback fires frame zero once");
                Check(crossed(0,0,false).Length == 0, "Stationary playhead does not repeat cues");
                Check(crossed(0, .5, false).SequenceEqual(new[]{10,11}), "Skipped editor frames trigger every crossed key, excluding muted cues");
                Check(crossed(10/30d, 11/30d, false).SequenceEqual(new[]{11}), "Interval starts are exclusive to prevent duplicate triggers");
                Check(crossed(1, 3, false).SequenceEqual(new[]{60}), "Last-frame cue included; keys after clip end ignored");
                Check(crossed(.5, 0, false).Length == 0, "Backward seeks do not trigger audio");
                Check(crossed(0, .5, true).SequenceEqual(new[]{0,10,11}), "Loop restart includes frame zero and skipped cues");
                take.frameRate = 60;
                Check(crossed(0,.2,false).SequenceEqual(new[]{10,11}), "Actor timing uses actor FPS");
                take.frameRate = 30;
                AssetDatabase.CreateAsset(take, "Assets/Audio Test.asset"); AssetDatabase.SaveAssets();
                var before = EditorJsonUtility.ToJson(take);
                Undo.RegisterCompleteObjectUndo(take,"Add cue"); take.audioCues.Add(new StudioAudioCue {frame=15,eventPath="event:/test"});
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(EditorJsonUtility.ToJson(take)==before, "Undo restores audio keys without modifying poses");
                Undo.PerformRedo();
                Check(take.audioCues.Last().eventPath=="event:/test", "Redo restores the assigned event");
                EditorUtility.SetDirty(take); AssetDatabase.SaveAssetIfDirty(take);
                AssetDatabase.ImportAsset("Assets/Audio Test.asset",ImportAssetOptions.ForceUpdate);
                Check(AssetDatabase.LoadAssetAtPath<AnimationTake>("Assets/Audio Test.asset").audioCues.Last().frame==15, "Audio keys persist on disk");
                Check(StudioAudioPreview.HasFmod, "Installed FMOD editor API discovered without assembly reference");
                var paths = StudioAudioPreview.EventPaths();
                Check(paths.Length > 0, "FMOD bank events populate the picker ("+paths.Length+")");
                var manager = AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("FMODUnity.EventManager")).First(t=>t!=null);
                var path = paths.First(p => {
                    var reference = manager.GetMethod("EventFromPath").Invoke(null,new object[]{p});
                    return (bool)reference.GetType().GetField("IsOneShot").GetValue(reference);
                });
                using(var preview = new StudioAudioPreview())
                using(var otherPreview = new StudioAudioPreview())
                {
                    var cue = new StudioAudioCue {eventPath=path,volume=.25f};
                    preview.Play(cue); otherPreview.Play(cue);
                    var listField=typeof(StudioAudioPreview).GetField("instances",BindingFlags.NonPublic|BindingFlags.Instance);
                    var voices=(IList)listField.GetValue(preview);
                    Check(voices.Count==1,"FMOD bank event creates a tracked preview voice");
                    object voice=voices[0]; object instance=voice.GetType().GetField("Instance",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(voice);
                    Check((bool)instance.GetType().GetMethod("isValid").Invoke(instance,null),"Native FMOD preview instance is valid");
                    object[] volume={0f,0f}; var result=instance.GetType().GetMethod("getVolume",new[]{typeof(float).MakeByRefType(),typeof(float).MakeByRefType()}).Invoke(instance,volume);
                    Check(result.ToString()=="OK" && Math.Abs((float)volume[0]-.25f)<.001f,"Per-key FMOD volume applied");
                    preview.StopLoops(); Check(voices.Count==1,"Loop boundary preserves one-shot tails");
                    preview.Dispose(); Check(voices.Count==0,"Stop releases all owned FMOD voices");
                    var other=(IList)listField.GetValue(otherPreview);
                    var otherInstance=other[0].GetType().GetField("Instance",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(other[0]);
                    Check((bool)otherInstance.GetType().GetMethod("isValid").Invoke(otherInstance,null),"Stopping one preview does not stop another owner's FMOD sound");
                    bool missing=false;
                    try { preview.Play(new StudioAudioCue {eventPath="event:/__missing_audio_validation__"}); }
                    catch(InvalidOperationException) {missing=true;}
                    Check(missing,"Missing FMOD events produce a useful error");
                    var clip=AudioClip.Create("Audio test",4410,1,44100,false);
                    preview.Play(new StudioAudioCue {source=StudioAudioSource.AudioClip,clip=clip});
                    preview.Dispose(); UnityEngine.Object.DestroyImmediate(clip);
                    Check(true,"Unity AudioClip preview API starts and stops successfully");
                }
                var window=ScriptableObject.CreateInstance<AnimationStudioWindow>();
                const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(AnimationStudioWindow).GetField("take",flags).SetValue(window,take);
                take.audioCues.Clear(); take.audioCues.Add(new StudioAudioCue {frame=0,eventPath=path});
                var play=typeof(AnimationStudioWindow).GetMethod("TogglePlay",flags);
                var audio=typeof(AnimationStudioWindow).GetField("audioPreview",flags).GetValue(window);
                var tracked=(IList)typeof(StudioAudioPreview).GetField("instances",flags).GetValue(audio);
                play.Invoke(window,null);
                Check(tracked.Count==1,"Studio Play triggers the audio key at its starting frame");
                play.Invoke(window,null);
                Check(tracked.Count==0,"Studio Pause clears its audio voices");
                typeof(AnimationStudioWindow).GetField("muteAudio",flags).SetValue(window,true);
                play.Invoke(window,null);
                Check(tracked.Count==0,"Studio mute suppresses audio while allowing playback");
                play.Invoke(window,null);
                typeof(AnimationStudioWindow).GetField("muteAudio",flags).SetValue(window,false);
                play.Invoke(window,null);
                UnityEngine.Object.DestroyImmediate(window);
                Check(tracked.Count==0,"Closing the studio releases its preview voices");
                File.WriteAllText("result.txt", "PASS: " + passed + " checks. FMOD picker: " + paths.Length + " events. Previewed: " + path);
                EditorApplication.Exit(0);
            }
            catch(Exception ex)
            {
                Debug.LogException(ex); File.WriteAllText("result.txt","FAIL: "+ex); EditorApplication.Exit(1);
            }
        }
    }
}
