#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


namespace MashBoxSDK.Exporting
{
    public class MapBundleDatabase : ScriptableObject
    {
        public List<MapBundleEntry> entries = new List<MapBundleEntry>();

        public static MapBundleDatabase GetOrCreate()
        {
            const string path = "Assets/MapBundleDatabase.asset";

            var db = AssetDatabase.LoadAssetAtPath<MapBundleDatabase>(path);

            if (db == null)
            {
                db = ScriptableObject.CreateInstance<MapBundleDatabase>();
                AssetDatabase.CreateAsset(db, path);
                AssetDatabase.SaveAssets();
                Debug.Log("[MGMapTools] Created new MapBundleDatabase at " + path);
            }

            return db;
        }
    }
}

#endif