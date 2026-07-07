#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    [CreateAssetMenu(fileName = "MapContentPack", menuName = "MashBox/Maps/Map Content Pack", order = 2100)]
    public class MapContentPackDefinition : ScriptableObject
    {
        public string PackName => string.IsNullOrWhiteSpace(_packName) ? name : _packName;

        [SerializeField] private string _packName;

        [Header("Map Content")]
        public SceneAsset Scene;
        public Texture2D Screenshot;

        [Header("Publishing")]
        public bool IncludeInBuild = true;
        public bool BuildToCustomFolder;
        public string modioUserToken;
        public string PublisherEmail;

        [Header("Metadata")]
        [Tooltip("MashBox SDK version that last prepared this map pack for export.")]
        public string MashBoxSdkVersion;

        [Tooltip("Marks this map as MashBox Insider-approved vanilla SDK content for publishing.")]
        public bool IsVanillaContent;

        public string MapName;

        [SerializeField]
        public List<ContentPackDefinition.GameModMapping> GameModMappings = new();

        public string GetModIdForGame(string gameName)
        {
            var match = GameModMappings.FirstOrDefault(g =>
                string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));

            return match?.ModId;
        }

        public void SetPublishTargetGame(string gameName)
        {
            GameModMappings ??= new List<ContentPackDefinition.GameModMapping>();

            if (string.IsNullOrWhiteSpace(gameName))
            {
                ClearPublishTargets();
                return;
            }

            var existing = GameModMappings.FirstOrDefault(g =>
                string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new ContentPackDefinition.GameModMapping
                {
                    GameName = gameName
                };
                GameModMappings.Add(existing);
            }

            foreach (var mapping in GameModMappings)
            {
                if (mapping == null)
                    continue;

                mapping.IsPublishTarget = ReferenceEquals(mapping, existing);
            }
        }

        public void ClearPublishTargets()
        {
            if (GameModMappings == null)
                return;

            foreach (var mapping in GameModMappings)
            {
                if (mapping != null)
                    mapping.IsPublishTarget = false;
            }
        }

        public void NormalizePublishTargets()
        {
            if (GameModMappings == null)
                return;

            var foundTarget = false;

            foreach (var mapping in GameModMappings)
            {
                if (mapping == null || !mapping.IsPublishTarget)
                    continue;

                if (!foundTarget)
                {
                    foundTarget = true;
                    continue;
                }

                mapping.IsPublishTarget = false;
            }
        }

        public void SetModIdForGame(string gameName, string modId)
        {
            var existing = GameModMappings.FirstOrDefault(g =>
                string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.ModId = modId;
                return;
            }

            GameModMappings.Add(new ContentPackDefinition.GameModMapping
            {
                GameName = gameName,
                ModId = modId
            });
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(MapName))
                MapName = name;

            _packName = MapName;
            NormalizePublishTargets();
        }

        [ContextMenu("Refresh Map Pack")]
        public void SyncToAddressables()
        {
            if (string.IsNullOrWhiteSpace(MapName))
                MapName = name;

            _packName = MapName;
            StampMashBoxSdkVersion();
            modioUserToken = ModIoAuth.CurrentToken;
            PublisherEmail = ModIoAuth.CurrentEmail;
            EditorUtility.SetDirty(this);
        }

        public void StampMashBoxSdkVersion()
        {
            var version = ContentPackDefinition.ResolveMashBoxSdkVersion();
            if (string.Equals(MashBoxSdkVersion, version, StringComparison.Ordinal))
                return;

            MashBoxSdkVersion = version;
            EditorUtility.SetDirty(this);
        }
    }
}
#endif
