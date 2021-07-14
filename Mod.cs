using RenpyLauncher;
using RenpyParser;
using RenPyParser.AssetManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityPS.Platform;

namespace TestDDLCMod
{
    public class Mod : UnityEngine.Object
    {
        public readonly string Name;
        public readonly FileBrowserEntries.FileBrowserEntry.Type Type;
        public FileBrowserEntries Entries { get; private set; }
        public Dictionary<string, RPAFile> RPAFiles { get; private set; } = new Dictionary<string, RPAFile>();
        public Dictionary<string, PythonObj> Labels = new Dictionary<string, PythonObj>();
        public List<PythonObj> EarlyPython = new List<PythonObj>();
        public SortedDictionary<int, List<PythonObj>> Inits = new SortedDictionary<int, List<PythonObj>>();

        public Dictionary<Type, Dictionary<string, string>> Assets = new Dictionary<Type, Dictionary<string, string>>();
        public AssetBundle AssetContainer = null;

        private const string MOD_CACHE_NAME = "currentMod.txt";
        private static FileBrowserEntries BaseGameEntries;
        private static FileBrowserEntries BaseGameDefaultEntries;
        private static Mod _activeMod = new Mod("Base Game");
        private static bool Initialized = false;

        // Apparently UnityPS just uses / rather than the system divider ;-;
        // so we confuse the base game if we do Path.Combine(PlatformManager.FileSystem.PersistentDataPath, "mods") on Windows
        private static string PersistentDataPath => PlatformManager.FileSystem.PersistentDataPath + "/mods";
        private static string LocalDataPath => "mods";
        public string DataPath
        {
            get
            {
                string BasePath;
                switch (Type)
                {
                    case ModBrowserApp.ModArchiveType: BasePath = PersistentDataPath; break;
                    case ModBrowserApp.ModDirectoryType: BasePath = LocalDataPath; break;
                    default: throw new InvalidDataException();
                }
                return BasePath + "/" + Name;
            }
        }
        public static Mod ActiveMod
        {
            get => _activeMod;
            set
            {
                if (_activeMod == value && _activeMod.Entries != null)
                {
                    return;
                }
                _activeMod = value;
                var bytes = Encoding.UTF8.GetBytes(_activeMod.Name);
                using (var file = new FileStream(Path.Combine(PersistentDataPath, MOD_CACHE_NAME), FileMode.OpenOrCreate, FileAccess.Write))
                {
                    file.SetLength(0);
                    file.Write(bytes, 0, bytes.Length);
                }
                _activeMod.Activate();
            }
        }

        public static bool IsModded()
        {
            InitializeMods();
            return ActiveMod.Type != ModBrowserApp.ModBaseGameType;
        }

        public static void InitializeMods()
        {
            if (Initialized)
            {
                return;
            }

            // inspect the bundles
            foreach (var bundleFile in Directory.GetFiles("Doki Doki Literature Club Plus_Data/StreamingAssets/AssetBundles/" + PathHelpers.GetPlatformForAssetBundles(Application.platform)))
            {
                if (bundleFile.EndsWith(".cy"))
                {
                    //AssetBundler.Unbundle(bundleFile);
                }
            }

            string name = "Base Game";
            Directory.CreateDirectory(PersistentDataPath);
            Directory.CreateDirectory(LocalDataPath);
            var path = Path.Combine(PersistentDataPath, MOD_CACHE_NAME);
            if (File.Exists(path))
            {
                byte[] bytes;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    bytes = new byte[file.Length];
                    file.Read(bytes, 0, bytes.Length);
                }
                name = Encoding.UTF8.GetString(bytes);
                if (name != "Base Game" && !Directory.Exists(new Mod(name).DataPath))
                {
                    // don't do zip file extraction on boot, unintuitively freezes the game for a bit
                    name = "Base Game";
                }
            }
            BaseGameEntries = VirtualFileSystem.Entries;
            BaseGameDefaultEntries = GameObject.Find("LauncherMainCanvas").GetComponent<LauncherMain>().Entries;
            ActiveMod = new Mod(name);
            Initialized = true;
        }

        public Mod(string name)
        {
            Name = name;
            Type = ModBrowserApp.ModBaseGameType;
            if (Name != "Base Game")
            {
                if (File.Exists(Path.Combine(LocalDataPath, Name)))
                {
                    Type = ModBrowserApp.ModArchiveType;
                }
                else if (Directory.Exists(Path.Combine(LocalDataPath, Name)))
                {
                    Type = ModBrowserApp.ModDirectoryType;
                }
            }
        }

        private void Activate()
        {
            if (Entries != BaseGameEntries)
            {
                Destroy(Entries);
            }

            if (Type == ModBrowserApp.ModBaseGameType)
            {
                Entries = BaseGameEntries;
            }
            else
            {
                if (Type == ModBrowserApp.ModArchiveType && !Directory.Exists(DataPath))
                {
                    Reset(); // extract the archive
                }
                Assets[typeof(TextAsset)] = new Dictionary<string, string>();
                Assets[typeof(Sprite)] = new Dictionary<string, string>();
                Assets[typeof(AudioClip)] = new Dictionary<string, string>();

                Entries = ScriptableObject.CreateInstance<FileBrowserEntries>();
                Entries.Entries = new List<FileBrowserEntries.FileBrowserEntry>();
                var entryNames = new HashSet<string>();
                Entries.CreateEntryAt("empty");
                foreach (var Directory in Directory.EnumerateDirectories(DataPath, "*", SearchOption.AllDirectories))
                {
                    var InnerPath = Directory.Substring(DataPath.Length + 1);
                    entryNames.Add(InnerPath);
                    Entries.CreateEntryAt(InnerPath + "/empty");
                }
                foreach (var File in Directory.EnumerateFiles(DataPath, "*", SearchOption.AllDirectories))
                {
                    var Info = new FileInfo(File);
                    var LastWriteTime = Info.LastWriteTime;
                    var Length = Info.Length;
                    var InnerPath = File.Substring(DataPath.Length + 1).Replace('\\', '/');
                    entryNames.Add(InnerPath);

                    if (InnerPath.EndsWith(".rpa"))
                    {
                        var rpaFile = new RPAFile(File);
                        if (!rpaFile.Ok)
                        {
                            Debug.LogError("Can't open rpa archive: " + InnerPath);
                            continue;
                        }
                        RPAFiles[InnerPath] = rpaFile;

                        var DirectoryNames = new HashSet<string>() { InnerPath };
                        foreach (var rpaEntry in rpaFile)
                        {
                            var name = InnerPath + "/" + rpaEntry.Name;
                            DirectoryNames.Add(name.Substring(0, name.LastIndexOf('/')));
                            entryNames.Add(name);
                            AddEntry(name, rpaEntry.Length, LastWriteTime);
                        }
                        foreach (var directoryName in DirectoryNames)
                        {
                            entryNames.Add(directoryName);
                            Entries.CreateEntryAt(directoryName + "/empty");
                        }

                        foreach (var rpycFile in rpaFile.RPYCFiles.Values)
                        {
                            foreach (var entry in rpycFile.Labels)
                            {
                                if (Labels.ContainsKey(entry.Key))
                                {
                                    Debug.LogWarning("Duplicate label definition for " + entry.Key);
                                }
                                else
                                {
                                    Labels[entry.Key] = entry.Value;
                                }
                            }
                            foreach (var entry in rpycFile.Inits)
                            {
                                if (!Inits.ContainsKey(entry.Key))
                                {
                                    Inits[entry.Key] = new List<PythonObj>();
                                }
                                Inits[entry.Key].AddRange(entry.Value);
                            }
                            foreach (var earlyPython in rpycFile.EarlyPython)
                            {
                                EarlyPython.Add(earlyPython);
                            }
                        }
                    }
                    else
                    {
                        AddEntry(InnerPath, Length, LastWriteTime);
                    }
                }

                foreach (var Entry in BaseGameDefaultEntries.Entries)
                {
                    if (Entry.Path.StartsWith("characters") ||
                        Entry.Path.StartsWith("game") && Entry.Path.EndsWith(".rpa") && Entry.Path != "game/scripts.rpa")
                    {
                        if (!entryNames.Contains(Entry.Path))
                        {
                            Entries.Entries.Add(Clone(Entry));
                        }
                    }
                }
            }
            VirtualFileSystem.Entries = Entries;
        }

        private void AddEntry(string innerPath, long length, DateTime lastWriteTime)
        {
            var Entry = Entries.CreateEntryAt(innerPath);
            Entry.Flags = FileBrowserEntries.FileBrowserEntry.EntryFlags.Open | FileBrowserEntries.FileBrowserEntry.EntryFlags.Delete;
            FileBrowserEntries.AssetReference.AssetTypes AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.None;
            switch (Path.GetExtension(innerPath).ToLower())
            {
                case ".bat":
                case ".json":
                case ".rpy":
                case ".sh":
                case ".txt":
                    Entry.AssetType = FileBrowserEntries.FileBrowserEntry.Type.Text;
                    AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.TextAsset;
                    Assets[typeof(TextAsset)][PathHelpers.SanitizePathToAddressableName(innerPath).ToLower()] = innerPath;
                    break;
                case ".png":
                case ".jpg":
                    Entry.AssetType = FileBrowserEntries.FileBrowserEntry.Type.Image;
                    AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.Sprite;
                    Assets[typeof(Sprite)][PathHelpers.SanitizePathToAddressableName(innerPath).ToLower()] = innerPath;
                    break;
                case ".ogg":
                case ".mp3":
                    Entry.AssetType = FileBrowserEntries.FileBrowserEntry.Type.Audio;
                    AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.AudioClip;
                    Assets[typeof(AudioClip)][PathHelpers.SanitizePathToAddressableName(innerPath).ToLower()] = innerPath;
                    break;
                default:
                    Entry.Flags = FileBrowserEntries.FileBrowserEntry.EntryFlags.Delete;
                    break;
            }
            if (AssetAssetType != FileBrowserEntries.AssetReference.AssetTypes.None)
            {
                Debug.Log($"Loading mod_asset {PathHelpers.SanitizePathToAddressableName(innerPath).ToLower()} = {innerPath}");
            }
            Entry.Modified = lastWriteTime;
            Entry.ModifiedUTC = Entry.Modified.ToFileTimeUtc();
            Entry.Asset = new FileBrowserEntries.AssetReference()
            {
                Path = innerPath,
                AssetSize = length,
                Type = AssetAssetType,
            };
        }

        public void Reset()
        {
            switch (Type)
            {
                case ModBrowserApp.ModBaseGameType: Debug.LogWarning("Refusing to Reset base DDLC from Mod class, fix the code!"); return;
                case ModBrowserApp.ModDirectoryType: Debug.LogWarning("Can't reset a directory mod, why are we trying?"); return;
                case ModBrowserApp.ModArchiveType: break;
                default: Debug.LogWarning("Unexpected type in Mod.Reset!"); return;
            }
            if (!DataPath.EndsWith("mods/" + Name))
            {
                Debug.LogWarning("Trying to recursively delete \"" + DataPath + "\", wtf!?");
                return;
            }
            if (Directory.Exists(DataPath))
            {
                Directory.Delete(DataPath, true);
            }
            using (var archive = new ZipArchive(File.OpenRead(Path.Combine(LocalDataPath, Name))))
            {
                string commonPrefix = null;
                foreach (var Entry in archive.Entries)
                {
                    var Name = Entry.FullName.Replace("\\", "/");
                    if (commonPrefix == null)
                    {
                        commonPrefix = Name.Substring(0, Name.LastIndexOf('/') + 1);
                    }
                    else
                    {
                        while (!Name.StartsWith(commonPrefix))
                        {
                            commonPrefix = commonPrefix.TrimEnd('/');
                            commonPrefix = commonPrefix.Substring(0, commonPrefix.LastIndexOf('/') + 1);
                        }
                    }
                    if (commonPrefix.EndsWith("game/"))
                    {
                        commonPrefix = commonPrefix.Substring(0, commonPrefix.Length - "game/".Length);
                    }
                    if (commonPrefix == "")
                    {
                        break;
                    }
                }
                var gameDirectory = archive.Entries
                    .Select(Entry => Entry.FullName.Replace("\\", "/")) // fucking path separators
                    .Where(Name => Name.StartsWith("game/") || Name.Contains("/game/")) // files in the game directory
                    .Select(Name => Name.StartsWith("game/") ? "game/" : Name.Substring(0, Name.IndexOf("/game/") + "/game/".Length)) // trim everything past game/
                    .DefaultIfEmpty(commonPrefix)
                    .Aggregate((Name1, Name2) =>
                    {
                        var diff = Name1.Count(c => c == '/') - Name2.Count(c => c == '/');
                        if (diff < 0)
                        {
                            return Name1;
                        }
                        if (diff > 0)
                        {
                            return Name2;
                        }
                        return Name1.CompareTo(Name2) < 0 ? Name1 : Name2;
                    }); // pick the one closest to the root (alphabetical in ties, tho wtf would put multiple parallel game directories in their mod -.-)
                var extractionFolder = DataPath;
                string baseDirectory = commonPrefix;
                if (gameDirectory == commonPrefix)
                {
                    extractionFolder = Path.Combine(DataPath, "game");
                }
                else
                {
                    // want to filter only the files adjacent to or deeper than gameDirectory
                    baseDirectory = gameDirectory.Substring(0, gameDirectory.Length - "game/".Length); // everything before the trailing game/
                }
                foreach (var entry in archive.Entries.Where(entry => entry.FullName.Replace("\\", "/").StartsWith(baseDirectory)))
                {
                    var path = Path.Combine(extractionFolder, entry.FullName.Substring(baseDirectory.Length));
                    if (entry.Name == "")
                    {
                        // just a directory
                        Directory.CreateDirectory(path);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        entry.ExtractToFile(path);
                    }
                }
            }
        }

        private static FileBrowserEntries.FileBrowserEntry Clone(FileBrowserEntries.FileBrowserEntry Entry)
        {
            var Modified = Entry.Modified;
            var ModifiedUTC = Entry.ModifiedUTC;
            if (ModifiedUTC == 0)
            {
                Modified = System.DateTime.Now;
                ModifiedUTC = Modified.ToFileTimeUtc();
            }
            return new FileBrowserEntries.FileBrowserEntry()
            {
                AccessHour = Entry.AccessHour,
                AccessMinute = Entry.AccessMinute,
                Asset = new FileBrowserEntries.AssetReference()
                {
                    Path = Entry.Asset.Path,
                    Type = Entry.Asset.Type,
                    AssetSize = Entry.Asset.AssetSize,
                },
                AssetType = Entry.AssetType,
                Flags = Entry.Flags,
                Modified = Modified,
                ModifiedUTC = ModifiedUTC,
                Path = Entry.Path,
                Visible = Entry.Visible,
            };
        }

        public static bool operator ==(Mod a, Mod b) => a.Name == b.Name;
        public static bool operator !=(Mod a, Mod b) => a.Name != b.Name;
        public override bool Equals(object o)
        {
            if (o is Mod other)
            {
                return this == other;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}
