#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    [Serializable]
    public class MBChallengeManifestEntry
    {
        public string id;
        public string name;
        public string description;
        public string type;
        public bool enabled;
        public MBChallengeStartMode startMode;
        public bool retryOnZoneEntry;
        public int selectedTier;
        public MBChallengeRules rules;
        public List<MBChallengeTier> tiers;
        public List<MBChallengeManifestZone> zones;
    }

    [Serializable]
    public class MBChallengeManifestZone
    {
        public string name;
        public Vector3 center;
        public Vector3 size;
        public Quaternion rotation;
        public Vector3 localCenter;
        public Vector3 localSize;
        public Matrix4x4 localToWorld;
    }

    public static class MBChallengeManifest
    {
        public static List<MBChallengeManifestEntry> Extract(Scene scene)
        {
            var entries = new List<MBChallengeManifestEntry>();
            if (!scene.IsValid() || !scene.isLoaded) return entries;
            foreach (var challenge in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MBTrickChallenge>(true)))
            {
                var zones = new List<MBChallengeManifestZone>();
                foreach (var zone in challenge.Zones)
                {
                    if (zone == null) continue;
                    var box = zone.GetComponent<BoxCollider>();
                    Vector3 scale = zone.transform.lossyScale;
                    zones.Add(new MBChallengeManifestZone
                    {
                        name = zone.name, center = zone.transform.TransformPoint(box.center), rotation = zone.transform.rotation,
                        localCenter = box.center, localSize = box.size, localToWorld = zone.transform.localToWorldMatrix,
                        size = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)))
                    });
                }
                entries.Add(new MBChallengeManifestEntry
                {
                    id = challenge.ChallengeId, name = challenge.ChallengeName, description = challenge.Description,
                    type = challenge.IsLine ? "Line" : "Spot", rules = challenge.Rules, tiers = challenge.Tiers, zones = zones,
                    enabled = challenge.enabled && challenge.gameObject.activeInHierarchy,
                    startMode = challenge.StartMode, retryOnZoneEntry = challenge.RetryOnZoneEntry, selectedTier = challenge.SelectedTier
                });
            }
            return entries;
        }
    }
}
#endif
