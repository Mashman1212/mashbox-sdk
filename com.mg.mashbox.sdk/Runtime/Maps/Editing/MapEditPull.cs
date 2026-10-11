using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.Maps.Editing
{
    [Serializable] public sealed class MapEditPullReply
    {
        public string error;
        public MapEditDocument changes;
    }
    public static class MapEditPull
    {
        public static string DirectoryFor(string receiverId) => Path.Combine(MapEditInbox.Inbox(receiverId), "pull");
        public static string Request(MapEditReceiver receiver, MapEditDocument snapshot)
        {
            if (!MapEditInbox.Receivers().Any(r => r.id == receiver.id)) throw new InvalidOperationException("Enable receiving in the destination Unity editor first.");
            snapshot.Validate();
            string path = Path.Combine(DirectoryFor(receiver.id), Guid.NewGuid().ToString("N") + ".request.json");
            MapEditDocument.WriteAtomic(path, JsonUtility.ToJson(snapshot));
            return path;
        }
        public static string ReplyPath(string request) => request + ".reply";
        public static MapEditPullReply ReadReply(string request)
        {
            string path = ReplyPath(request);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MapEditDocument.MaximumBytes) throw new InvalidDataException("Pull response exceeds 128 MB.");
            return JsonUtility.FromJson<MapEditPullReply>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty pull response.");
        }
    }
}
