#if UNITY_EDITOR

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MashBoxSDK.SharedCore.Editor
{
    public static class AddressablesCorePatcher
    {
        public static void PatchCatalog(string contentPath, string corePath)
        {
            string contentJson = File.ReadAllText(contentPath);
            string coreJson = File.ReadAllText(corePath);

            int contentIndex = FindCoreIndex(contentJson);
            int coreIndex = FindCoreIndex(coreJson);

            if (contentIndex == -1 || coreIndex == -1)
            {
                Debug.LogError("❌ Failed to find core index");
                return;
            }

            byte[] contentBytes = ExtractExtraData(contentJson);
            byte[] coreBytes = ExtractExtraData(coreJson);

            if (contentBytes == null || coreBytes == null)
            {
                Debug.LogError("❌ Failed to decode ExtraData");
                return;
            }

            var contentOffsets = FindEntryOffsets(contentBytes);
            var coreOffsets = FindEntryOffsets(coreBytes);

            if (contentIndex >= contentOffsets.Count || coreIndex >= coreOffsets.Count)
            {
                Debug.LogError("❌ Index out of range for entry offsets");
                return;
            }

            int contentStart = contentOffsets[contentIndex];
            int contentEnd = (contentIndex + 1 < contentOffsets.Count)
                ? contentOffsets[contentIndex + 1]
                : contentBytes.Length;

            int coreStart = coreOffsets[coreIndex];
            int coreEnd = (coreIndex + 1 < coreOffsets.Count)
                ? coreOffsets[coreIndex + 1]
                : coreBytes.Length;

            int coreSize = coreEnd - coreStart;

            Debug.Log($"[PATCH] Replacing full entry size: {coreSize}");

            byte[] newContent = new byte[contentBytes.Length - (contentEnd - contentStart) + coreSize];

            Array.Copy(contentBytes, 0, newContent, 0, contentStart);
            Array.Copy(coreBytes, coreStart, newContent, contentStart, coreSize);
            Array.Copy(contentBytes, contentEnd, newContent, contentStart + coreSize, contentBytes.Length - contentEnd);

            string encoded = Convert.ToBase64String(newContent);

            contentJson = Regex.Replace(contentJson,
                "\"m_ExtraDataString\":\"([^\"]+)\"",
                $"\"m_ExtraDataString\":\"{encoded}\"");

            File.WriteAllText(contentPath, contentJson);

            Debug.Log("✅ FULL binary entry replaced (CORRECT FIX)");
        }

        public static void Validate(string contentPath, string corePath)
        {
            string contentJson = File.ReadAllText(contentPath);
            string coreJson = File.ReadAllText(corePath);

            int contentIndex = FindCoreIndex(contentJson);
            int coreIndex = FindCoreIndex(coreJson);

            Debug.Log($"[VALIDATE] Content Index: {contentIndex}");
            Debug.Log($"[VALIDATE] Core Index: {coreIndex}");
        }

        // ---------- INTERNALS ----------

        private static int FindCoreIndex(string json)
        {
            var match = Regex.Match(json, "\"m_InternalIds\":\\[(.*?)\\]", RegexOptions.Singleline);
            if (!match.Success) return -1;

            var ids = Regex.Matches(match.Groups[1].Value, "\"([^\"]+)\"");

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i].Groups[1].Value.ToLower().Contains("mashboxcustomizationcore"))
                    return i;
            }

            return -1;
        }

        private static byte[] ExtractExtraData(string json)
        {
            var match = Regex.Match(json, "\"m_ExtraDataString\":\"([^\"]+)\"");
            if (!match.Success) return null;

            return Convert.FromBase64String(match.Groups[1].Value);
        }

        private static List<int> FindEntryOffsets(byte[] data)
        {
            List<int> offsets = new List<int>();

            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == '{')
                    offsets.Add(i);
            }

            return offsets;
        }
    }
}

#endif