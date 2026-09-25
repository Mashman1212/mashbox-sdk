#if UNITY_EDITOR
using System;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed class MBChallengePreview : EditorWindow
    {
        private MBTrickChallenge challenge;
        private MBChallengeSession session;
        private MBChallengeTier previewTier;
        private int tierIndex;
        private int zoneIndex;
        private long eventId;
        private string tricks = "180";
        private float score = 200;
        private string signal = "manual-distance";
        private float signalAmount = 1;
        private Vector2 scroll;
        private string error;

        public static void Open(MBTrickChallenge target)
        {
            var window = GetWindow<MBChallengePreview>("Challenge Preview");
            window.challenge = target;
            window.tierIndex = target.SelectedTier;
            window.session = null;
            window.minSize = new Vector2(380, 480);
            window.Show();
        }

        private void OnGUI()
        {
            challenge = (MBTrickChallenge)EditorGUILayout.ObjectField("Challenge", challenge, typeof(MBTrickChallenge), true);
            if (challenge == null) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Simulates the authored rules. It does not move the rider, grant rewards, or change saved progress. Restart after editing goals.", MessageType.Info);
            tierIndex = EditorGUILayout.IntSlider("Tier Index", tierIndex, 0, Mathf.Max(0, challenge.Tiers.Count - 1));
            if (GUILayout.Button("Start / Restart Preview"))
            {
                try
                {
                    var errors = challenge.GetValidationErrors();
                    if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
                    previewTier = JsonUtility.FromJson<MBChallengeTier>(JsonUtility.ToJson(challenge.Tiers[tierIndex]));
                    session = new MBChallengeSession(previewTier, challenge.Rules, challenge.IsLine, challenge.Zones.Count);
                    zoneIndex = 0; error = null;
                }
                catch (ArgumentException exception) { error = exception.Message; session = null; }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (session != null)
            {
                EditorGUILayout.LabelField($"{previewTier.title}: {session.State}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Step {session.CurrentStep + 1}  |  {session.ElapsedSeconds:0.0}s");
                if (!string.IsNullOrEmpty(session.FailureReason)) EditorGUILayout.HelpBox(session.FailureReason, MessageType.Warning);
                for (int i = 0; i < session.GoalCount; i++)
                    EditorGUILayout.LabelField(previewTier.goals[i].label, $"{session.GetProgress(i):0.##} / {previewTier.goals[i].target:0.##}");
                zoneIndex = EditorGUILayout.IntSlider("Zone Index", zoneIndex, 0, Mathf.Max(0, challenge.Zones.Count - 1));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Enter Zone")) session.EnterZone(zoneIndex);
                    if (GUILayout.Button("Exit Zone")) session.ExitZone(zoneIndex);
                }
                tricks = EditorGUILayout.TextField("Tricks (comma separated)", tricks);
                score = EditorGUILayout.FloatField("Landed Score", score);
                if (GUILayout.Button("Confirm Landed Combo"))
                    session.ReportLandedCombo(++eventId, Array.ConvertAll(tricks.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), value => value.Trim()), score);
                signal = EditorGUILayout.TextField("Custom Signal", signal);
                signalAmount = EditorGUILayout.FloatField("Amount", signalAmount);
                if (GUILayout.Button("Send Signal")) session.ReportSignal(++eventId, signal, signalAmount);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Bail")) session.Bail();
                    if (GUILayout.Button("Respawn")) session.Respawn();
                    if (GUILayout.Button("Advance 5 Seconds")) session.Tick(5);
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
