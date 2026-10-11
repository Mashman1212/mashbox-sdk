using System;
using System.IO;
using System.Linq;

namespace MashBoxSDK.Maps.Editing
{
    [Serializable] public sealed class MapEditReceiver
    {
        public string id, project, scene, updatedUtc;
    }
    public static class MapEditInbox
    {
        public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MashBox", "MapEditing");
        public static MapEditReceiver[] Receivers()
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<MapEditReceiver>();
            return Directory.GetFiles(DirectoryPath, "receiver-*.json").Select(ReadReceiver).Where(r => r != null).ToArray();
        }
        static MapEditReceiver ReadReceiver(string path)
        {
            try
            {
                var data = UnityEngine.JsonUtility.FromJson<MapEditReceiver>(File.ReadAllText(path));
                if (data == null || !Guid.TryParseExact(data.id, "N", out _) || !DateTime.TryParse(data.updatedUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var updated) || DateTime.UtcNow - updated.ToUniversalTime() > TimeSpan.FromSeconds(20)) return null;
                return data;
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
        }
        public static string Inbox(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid editor receiver.");
            return Path.Combine(DirectoryPath, id);
        }
        public static string Send(MapEditReceiver receiver, MapEditDocument document)
        {
            if (!Receivers().Any(r => r.id == receiver.id)) throw new InvalidOperationException("The chosen Unity editor is no longer listening.");
            document.Validate();
            string path = Path.Combine(Inbox(receiver.id), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
            MapEditDocument.WriteAtomic(path, UnityEngine.JsonUtility.ToJson(document));
            return path;
        }
    }
}
