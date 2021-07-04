using RenpyLauncher;
using RenpyParser;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityPS.Platform;

namespace TestDDLCMod
{
    public class Mod : Object
    {
        public readonly string Name;
        public readonly FileBrowserEntries.FileBrowserEntry.Type Type;
        public FileBrowserEntries Entries { get; private set; }

        private const string MOD_CACHE_NAME = "currentMod.txt";
        private static FileBrowserEntries BaseGameEntries;
        private static FileBrowserEntries BaseGameDefaultEntries;
        private static Mod _activeMod = new Mod("Base Game");

        private static string PersistentDataPath => Path.Combine(PlatformManager.FileSystem.PersistentDataPath, "mods");
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
                return Path.Combine(BasePath, Name);
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

        public static bool IsModded() => ActiveMod.Type != ModBrowserApp.ModBaseGameType;

        public static void InitializeMods()
        {
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
            }
            BaseGameEntries = VirtualFileSystem.Entries;
            BaseGameDefaultEntries = GameObject.Find("LauncherMainCanvas").GetComponent<LauncherMain>().Entries;
            ActiveMod = new Mod(name);
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

                Entries = ScriptableObject.CreateInstance<FileBrowserEntries>();
                Entries.Entries = new List<FileBrowserEntries.FileBrowserEntry>();
                var entryNames = new HashSet<string>();
                Entries.CreateEntryAt("empty");
                foreach (var Directory in Directory.EnumerateDirectories(DataPath, "*", SearchOption.AllDirectories))
                {
                    var InnerPath = Directory.Substring(DataPath.Length + 1);
                    entryNames.Add(InnerPath);
                    Entries.CreateEntryAt(Path.Combine(InnerPath, "empty"));
                }
                foreach (var File in Directory.EnumerateFiles(DataPath, "*", SearchOption.AllDirectories))
                {
                    var InnerPath = File.Substring(DataPath.Length + 1);
                    entryNames.Add(InnerPath);
                    var Entry = Entries.CreateEntryAt(InnerPath);
                    var Info = new FileInfo(File);
                    Entry.Flags = FileBrowserEntries.FileBrowserEntry.EntryFlags.Open | FileBrowserEntries.FileBrowserEntry.EntryFlags.Delete;
                    FileBrowserEntries.AssetReference.AssetTypes AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.None;
                    switch (Path.GetExtension(File).ToLower())
                    {
                        case ".bat":
                        case ".rpy":
                        case ".sh":
                        case ".txt":
                            Entry.AssetType = FileBrowserEntries.FileBrowserEntry.Type.Text;
                            AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.TextAsset;
                            break;
                        case ".png":
                        case ".jpg":
                            Entry.AssetType = FileBrowserEntries.FileBrowserEntry.Type.Image;
                            AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.Sprite;
                            break;
                        case ".ogg":
                        case ".mp3":
                            Entry.AssetType = FileBrowserEntries.FileBrowserEntry.Type.Audio;
                            AssetAssetType = FileBrowserEntries.AssetReference.AssetTypes.AudioClip;
                            break;
                        default:
                            Entry.Flags = FileBrowserEntries.FileBrowserEntry.EntryFlags.Delete;
                            break;
                    }
                    Entry.Modified = Info.LastWriteTime;
                    Entry.ModifiedUTC = Info.LastWriteTimeUtc.Ticks;
                    Entry.Asset = new FileBrowserEntries.AssetReference()
                    {
                        Path = InnerPath,
                        AssetSize = Info.Length,
                        Type = AssetAssetType,
                    };
                }

                foreach (var Entry in BaseGameDefaultEntries.Entries)
                {
                    if(Entry.Path.StartsWith("characters") ||
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

        public void Reset()
        {
            switch (Type)
            {
                case ModBrowserApp.ModBaseGameType: Debug.LogWarning("Refusing to Reset base DDLC from Mod class, fix the code!"); return;
                case ModBrowserApp.ModDirectoryType: Debug.LogWarning("Can't reset a directory mod, why are we trying?"); return;
                case ModBrowserApp.ModArchiveType: break;
                default: Debug.LogWarning("Unexpected type in Mod.Reset!"); return;
            }
            if (!DataPath.EndsWith(Path.Combine("mods", Name)))
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
                            commonPrefix = commonPrefix.Substring(0, commonPrefix.LastIndexOf('/') + 1);
                        }
                    }
                    if (commonPrefix == "")
                    {
                        break;
                    }
                }
                var gameDirectory = archive.Entries
                    .Select(Entry => Entry.FullName.Replace("\\", "/")) // fucking path separators
                    .Where(Name => Name.StartsWith("game/") || Name.Contains("/game/")) // files in the game directory
                    .Select(Name => Name.StartsWith("game/") ? "game/" : Name.Substring(0, Name.IndexOf("/game/") + 6)) // trim everything past game/
                    .Aggregate(commonPrefix, (Name1, Name2) =>
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
                    baseDirectory = gameDirectory.Substring(0, gameDirectory.Length - 5); // everything before the trailing game/
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
                Modified = Entry.Modified,
                ModifiedUTC = Entry.ModifiedUTC,
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
