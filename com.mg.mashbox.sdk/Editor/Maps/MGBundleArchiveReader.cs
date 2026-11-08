using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MashBoxSDK.MapTools
{
    internal enum MGBundleCategory { All, Textures, Meshes, Materials, Shaders, Audio, Animation, Scenes, Objects, Other, ArchiveFiles }

    [Serializable]
    internal sealed class MGBundleEntry
    {
        public string Name, Type, File, Details;
        public long Id, Bytes;
        public MGBundleCategory Category;
    }

    internal sealed class MGBundleInventory
    {
        public string Path;
        public long DiskBytes, ExpandedBytes;
        public readonly List<MGBundleEntry> Assets = new();
        public readonly List<MGBundleEntry> Files = new();
        public readonly List<string> Warnings = new();
    }

    // Small read-only adapter to Unity's editor-supplied archive API. ABI reference:
    // https://github.com/Unity-Technologies/UnityDataTools/blob/main/UnityFileSystem/DllWrapper.cs
    // No bundle assets are instantiated, and scene contents are never opened in the editor.
    internal sealed class MGBundleArchiveReader
    {
        private static readonly object Gate = new();
        private static IntPtr library;
        private readonly Init init;
        private readonly Mount mount;
        private readonly Close unmount, closeSerialized, closeFile;
        private readonly Count nodeCount, objectCount;
        private readonly Node archiveNode;
        private readonly Open openSerialized, openFile;
        private readonly Objects objectInfo;
        private readonly Tree getTree;
        private readonly TreeNode treeNode;
        private readonly Read read;
        private readonly Seek seek;

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectRecord { public long Id, Offset, Size; public int TypeId; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Mount([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string point, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Close(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(IntPtr handle, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Node(IntPtr handle, int index, StringBuilder path, int capacity, out long size, out int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Objects(IntPtr handle, [In, Out] ObjectRecord[] objects, int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Tree(IntPtr handle, long id, out IntPtr tree);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TreeNode(IntPtr tree, int index, StringBuilder type, int typeCapacity, StringBuilder name, int nameCapacity, out int offset, out int size, out uint flags, out uint meta, out int child, out int next);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Read(IntPtr file, long size, [Out] byte[] data, out long actual);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Seek(IntPtr file, long offset, int origin, out long position);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("libdl.so.2")] private static extern IntPtr dlopen(string path, int flags);
        [DllImport("libdl.so.2")] private static extern IntPtr dlsym(IntPtr handle, string name);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen")] private static extern IntPtr MacOpen(string path, int flags);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlsym")] private static extern IntPtr MacSymbol(IntPtr handle, string name);
        private static bool Windows => Environment.OSVersion.Platform == PlatformID.Win32NT;
        private readonly bool mac;

        internal MGBundleArchiveReader(string toolsDirectory)
        {
            mac = Directory.Exists("/System/Library/CoreServices");
            lock (Gate)
            {
                if (library == IntPtr.Zero)
                {
                    var path = System.IO.Path.Combine(toolsDirectory, "UnityFileSystemApi" + (Windows ? ".dll" : mac ? ".dylib" : ".so"));
                    library = Windows ? LoadLibraryW(path) : mac ? MacOpen(path, 2) : dlopen(path, 2);
                    if (library == IntPtr.Zero) throw new NotSupportedException("Unity's archive reader could not be opened. This tool requires the UnityFileSystemApi library supplied with Unity 2022.1 or newer.");
                }
                init = Bind<Init>("Init"); mount = Bind<Mount>("MountArchive"); unmount = Bind<Close>("UnmountArchive");
                closeSerialized = Bind<Close>("CloseSerializedFile"); closeFile = Bind<Close>("CloseFile");
                nodeCount = Bind<Count>("GetArchiveNodeCount"); objectCount = Bind<Count>("GetObjectCount");
                archiveNode = Bind<Node>("GetArchiveNode"); openSerialized = Bind<Open>("OpenSerializedFile"); openFile = Bind<Open>("OpenFile");
                objectInfo = Bind<Objects>("GetObjectInfo"); getTree = Bind<Tree>("GetTypeTree"); treeNode = Bind<TreeNode>("GetTypeTreeNodeInfo");
                read = Bind<Read>("ReadFile"); seek = Bind<Seek>("SeekFile");
            }
        }

        private T Bind<T>(string name) where T : Delegate
        {
            var symbol = Windows ? GetProcAddress(library, "UFS_" + name) : mac ? MacSymbol(library, "UFS_" + name) : dlsym(library, "UFS_" + name);
            if (symbol == IntPtr.Zero) throw new NotSupportedException("The Unity archive reader is missing UFS_" + name + ".");
            return Marshal.GetDelegateForFunctionPointer<T>(symbol);
        }

        private static void Check(int code, string operation)
        {
            if (code != 0) throw new IOException($"{operation} failed (Unity archive status {code}). The file may be damaged, unsupported, or built with a newer Unity version.");
        }

        internal MGBundleInventory Inspect(string path, CancellationToken cancellation, Action<string> progress = null)
        {
            path = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(path)) throw new FileNotFoundException("Bundle file not found.", path);
            using (var input = System.IO.File.OpenRead(path))
            {
                var magic = new byte[8];
                if (input.Read(magic, 0, magic.Length) != 8 || !Encoding.ASCII.GetString(magic).StartsWith("Unity", StringComparison.Ordinal))
                    throw new InvalidDataException("Choose a built Unity AssetBundle file, rather than its .manifest, a folder, or a ZIP archive.");
            }
            lock (Gate)
            {
                cancellation.ThrowIfCancellationRequested();
                int status = init();
                if (status != 0 && status != 1) Check(status, "Initialize archive reader");
                string point = "/mashbox-inspect-" + Guid.NewGuid().ToString("N") + "/";
                progress?.Invoke("Opening bundle archive…");
                Check(mount(path, point, out var archive), "Open bundle");
                try
                {
                    var result = new MGBundleInventory { Path = path, DiskBytes = new FileInfo(path).Length };
                    Check(nodeCount(archive, out int count), "Read archive directory");
                    if (count < 0 || count > 1000000) throw new InvalidDataException("Invalid archive entry count.");
                    var name = new StringBuilder(4096);
                    for (int index = 0; index < count; index++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        name.Clear();
                        Check(archiveNode(archive, index, name, name.Capacity, out long size, out int flags), "Read archive entry");
                        if ((flags & 3) != 0) continue;
                        string entryPath = name.ToString();
                        result.Files.Add(new MGBundleEntry { Name = entryPath, File = entryPath, Type = (flags & 4) != 0 ? "Serialized file" : "Resource stream", Bytes = size, Category = MGBundleCategory.ArchiveFiles });
                        result.ExpandedBytes += size;
                        if ((flags & 4) == 0) continue;
                        progress?.Invoke($"Reading {entryPath} ({index + 1}/{count})…");
                        ReadSerialized(point + entryPath, entryPath, result, cancellation);
                    }
                    result.Assets.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
                    result.Files.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
                    return result;
                }
                finally { unmount(archive); }
            }
        }

        private void ReadSerialized(string path, string fileName, MGBundleInventory inventory, CancellationToken cancellation)
        {
            int status = openSerialized(path, out var serialized);
            if (status != 0) { inventory.Warnings.Add($"{fileName}: object metadata unavailable (Unity archive status {status}). Archive files are still listed."); return; }
            IntPtr stream = IntPtr.Zero;
            try
            {
                Check(objectCount(serialized, out int count), "Read object count");
                if (count < 0 || count > 2000000) throw new InvalidDataException("Invalid serialized object count.");
                var objects = new ObjectRecord[count];
                if (count > 0) Check(objectInfo(serialized, objects, count), "Read object directory");
                bool hasStream = openFile(path, out stream) == 0;
                var header = new byte[20];
                bool bigEndian = false;
                if (hasStream && read(stream, 20, header, out long actual) == 0 && actual == 20) bigEndian = header[16] != 0;
                var layouts = new Dictionary<IntPtr, Layout>();
                int missingTrees = 0;
                foreach (var record in objects)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var typeName = KnownType(record.TypeId);
                    var item = new MGBundleEntry { Id = record.Id, File = fileName, Bytes = record.Size, Type = typeName, Name = typeName + " #" + record.Id, Category = CategoryFor(record.TypeId) };
                    if (getTree(serialized, record.Id, out var tree) == 0 && tree != IntPtr.Zero)
                    {
                        if (!layouts.TryGetValue(tree, out var layout)) layouts[tree] = layout = ReadLayout(tree);
                        if (!string.IsNullOrEmpty(layout.Type)) item.Type = layout.Type;
                        if (hasStream) ReadNameAndDetails(stream, record, layout, item, bigEndian);
                    }
                    else missingTrees++;
                    inventory.Assets.Add(item);
                }
                if (missingTrees > 0) inventory.Warnings.Add($"{fileName}: {missingTrees:N0} objects have no readable type tree. They are listed by class and object ID; names and dimensions may be unavailable.");
            }
            finally { if (stream != IntPtr.Zero) closeFile(stream); closeSerialized(serialized); }
        }

        private sealed class Field { internal string Name, Type; internal int Size; internal uint Meta; }
        private sealed class Layout { internal string Type; internal readonly List<Field> Fields = new(); }
        private Layout ReadLayout(IntPtr tree)
        {
            var type = new StringBuilder(1024); var name = new StringBuilder(1024);
            int NodeInfo(int node, out int size, out uint meta, out int child, out int next) =>
                treeNode(tree, node, type.Clear(), type.Capacity, name.Clear(), name.Capacity, out _, out size, out _, out meta, out child, out next);
            var layout = new Layout();
            if (NodeInfo(0, out _, out _, out int current, out _) != 0) return layout;
            layout.Type = type.ToString();
            for (int limit = 0; current != 0 && limit < 256; limit++)
            {
                if (NodeInfo(current, out int size, out uint meta, out _, out int next) != 0) break;
                string fieldType = type.ToString(), fieldName = name.ToString();
                if (size < 0 && fieldType != "string") break;
                layout.Fields.Add(new Field { Name = fieldName, Type = fieldType, Size = size, Meta = meta });
                current = next;
            }
            return layout;
        }

        private void ReadNameAndDetails(IntPtr stream, ObjectRecord record, Layout layout, MGBundleEntry entry, bool bigEndian)
        {
            if (record.Size <= 0) return;
            var data = new byte[(int)Math.Min(record.Size, 16384)];
            if (seek(stream, record.Offset, 0, out _) != 0 || read(stream, data.Length, data, out long actual) != 0) return;
            int offset = 0;
            var details = new List<string>();
            foreach (var field in layout.Fields)
            {
                if (offset < 0 || offset >= actual) break;
                if (field.Type == "string")
                {
                    if (offset + 4 > actual) break;
                    int length = Int32(data, offset, bigEndian); offset += 4;
                    if (length < 0 || length > actual - offset) break;
                    if (field.Name == "m_Name" && length > 0) entry.Name = Encoding.UTF8.GetString(data, offset, length);
                    offset = (offset + length + 3) & ~3;
                }
                else
                {
                    if (field.Size < 0 || field.Size > actual - offset) break;
                    if (field.Size == 4 && (field.Name == "m_Width" || field.Name == "m_Height" || field.Name == "m_Depth" || field.Name == "m_MipCount" || field.Name == "m_TextureFormat" || field.Name == "m_VertexCount"))
                        details.Add(field.Name.Substring(2) + ": " + Int32(data, offset, bigEndian));
                    offset += field.Size;
                    if ((field.Meta & 16384) != 0) offset = (offset + 3) & ~3;
                }
            }
            entry.Details = string.Join(" · ", details);
        }

        private static int Int32(byte[] data, int offset, bool bigEndian) => bigEndian
            ? (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]
            : data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

        internal static MGBundleCategory CategoryFor(int type) => type switch
        {
            28 or 84 or 89 or 117 or 187 or 188 => MGBundleCategory.Textures,
            43 => MGBundleCategory.Meshes, 21 => MGBundleCategory.Materials,
            48 or 72 => MGBundleCategory.Shaders, 83 or 240 => MGBundleCategory.Audio,
            74 or 90 or 91 or 93 or 95 or 111 or 206 or 221 => MGBundleCategory.Animation,
            29 or 104 or 157 or 196 => MGBundleCategory.Scenes,
            1 or 4 or 20 or 23 or 33 or 54 or 64 or 65 or 114 or 135 or 137 or 154 or 156 or 218 or 224 => MGBundleCategory.Objects,
            _ => MGBundleCategory.Other
        };
        internal static string KnownType(int type) => type switch
        {
            1 => "GameObject", 4 => "Transform", 20 => "Camera", 21 => "Material", 23 => "MeshRenderer", 28 => "Texture2D",
            29 => "OcclusionCullingSettings", 33 => "MeshFilter", 43 => "Mesh", 48 => "Shader", 54 => "Rigidbody", 64 => "MeshCollider",
            65 => "BoxCollider", 72 => "ComputeShader", 74 => "AnimationClip", 83 => "AudioClip", 84 => "RenderTexture", 89 => "Cubemap",
            90 => "Avatar", 91 => "AnimatorController", 95 => "Animator", 104 => "RenderSettings", 114 => "MonoBehaviour", 115 => "MonoScript",
            117 => "Texture3D", 128 => "Font", 135 => "SphereCollider", 137 => "SkinnedMeshRenderer", 142 => "AssetBundle",
            154 => "TerrainCollider", 156 => "TerrainData", 157 => "LightmapSettings", 187 => "Texture2DArray", 188 => "CubemapArray",
            196 => "NavMeshSettings", 213 => "Sprite", 218 => "Terrain", 224 => "RectTransform", 240 => "AudioMixer",
            _ => "Class " + type
        };
    }
}
