#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    public static class PublishHistory
    {
        [Serializable] public sealed class Attempt
        {
            public string runId, startedUtc, updatedUtc, endedUtc, outcome, stage, failureStage, modId;
        }
        [Serializable] public sealed class Status
        {
            public string jobId, utc, status, message, region, container;
            public bool publisherAvailable, queueTruncated, queueEstimate;
            public int queuePosition, queueTotal;
            public Attempt[] attempts;
        }
        [Serializable] public sealed class Entry
        {
            public string jobId, token, fileName, container, region, game, unityVersion, createdUtc;
            public bool trackingSupported;
            public Status lastStatus;
        }
        [Serializable] private sealed class Store { public List<Entry> entries = new List<Entry>(); }
        private static readonly object Gate = new object();
        // Per operating-system user, outside projects/version control; tokens never enter URLs or logs.
        private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MashBoxSDK", "PublishHistory", "publishes.json");
        private static Store Load()
        {
            if (!File.Exists(FilePath)) return new Store();
            var store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
            if (store?.entries == null) throw new IOException("Publish history could not be read. Preserve the existing history file before retrying.");
            return store;
        }
        private static void Save(Store store)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonUtility.ToJson(store, true));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        // Named mutex prevents two open Unity projects from losing each other's history.
        private static T WithStore<T>(Func<Store, T> action)
        {
            lock (Gate)
            using (var mutex = new System.Threading.Mutex(false, "Local\\MashBoxSDK.PublishHistory.v1"))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
                    catch (System.Threading.AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Publish history is busy. Please retry shortly.");
                    return action(Load());
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
        public static Entry[] Read() => WithStore(store => store.entries.OrderByDescending(e => e.createdUtc).ToArray());
        internal static void Remember(string id, string token, string fileName, string container, PublisherService.Route route, bool supported)
        {
            WithStore(store => {
                if (!store.entries.Any(e => e.jobId == id))
                {
                    store.entries.Add(new Entry { jobId=id, token=token, fileName=fileName, container=container,
                        region=route.region, game=route.game, unityVersion=route.unityVersion,
                        createdUtc=DateTime.UtcNow.ToString("O"), trackingSupported=supported });
                    Save(store);
                }
                return true;
            });
        }
        internal static void Update(string id, Status status)
        {
            WithStore(store => {
                var entry = store.entries.Find(e => e.jobId == id);
                if (entry != null) { entry.lastStatus = status; Save(store); }
                return true;
            });
        }
    }
}
#endif
