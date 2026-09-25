#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public partial class MashBoxMapToolsWindow
    {
        private void DrawTrickChallengesSection(bool line)
        {
            string label = line ? "Line" : "Spot";
            if (DrawAddButton($"Add {label} Challenge", line
                ? "Create an ordered trick line with zones and four editable goal tiers."
                : "Create a session spot with an attempt zone and four editable goal tiers.", 32f))
            {
                var root = FindOrCreateChallengeTypeRoot(label + " Challenges");
                var challenge = MBChallengeAuthoring.Create(line, root.transform, GetScenePlacementPosition());
                MBSceneIconUtility.ApplyChallengeSceneIcon(challenge.gameObject);
                sceneToolCacheDirty = true;
            }
            EditorGUILayout.HelpBox(line
                ? "Place the step zones along your line, then choose the required tricks and goals for each step in the Inspector."
                : "Place and resize the spot zone, then edit trick, score, and custom goals in the Inspector. A spot can keep an attempt armed until landing.", MessageType.None);
            IEnumerable<MBTrickChallenge> items = line ? cachedLineChallenges : (IEnumerable<MBTrickChallenge>)cachedSpotChallenges;
            foreach (var challenge in items)
            {
                if (challenge == null) continue;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField(challenge.ChallengeName, challenge, typeof(MBTrickChallenge), true);
                    if (GUILayout.Button("Edit", GUILayout.Width(48)))
                    {
                        Selection.activeGameObject = challenge.gameObject;
                        EditorGUIUtility.PingObject(challenge);
                    }
                    if (GUILayout.Button("Preview", GUILayout.Width(65))) MBChallengePreview.Open(challenge);
                }
            }
        }
    }
}
#endif
