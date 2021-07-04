using System.IO;
using System.Text;
using UnityPS.Platform;

namespace TestDDLCMod
{
    public class Mod : UnityEngine.Object
    {
        public readonly string Name;
        public readonly FileBrowserEntries.FileBrowserEntry.Type Type;

        private const string MOD_CACHE_NAME = "currentMod.txt";
        private static Mod _activeMod;
        public static Mod ActiveMod
        {
            get => _activeMod;
            set
            {
                _activeMod = value;
                var bytes = Encoding.UTF8.GetBytes(_activeMod.Name);
                using (var file = new FileStream(Path.Combine(PersistentDataPath, MOD_CACHE_NAME), FileMode.OpenOrCreate, FileAccess.Write))
                {
                    file.SetLength(0);
                    file.Write(bytes, 0, bytes.Length);
                }
            }
        }

        static Mod()
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
            _activeMod = new Mod(name);
        }

        private static string PersistentDataPath => Path.Combine(PlatformManager.FileSystem.PersistentDataPath, "mods");
        private static string LocalDataPath => "mods";

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

        public string GetDataPath()
        {
            string BasePath;
            switch (Type)
            {
                case ModBrowserApp.ModArchiveType: BasePath = LocalDataPath; break;
                case ModBrowserApp.ModDirectoryType: BasePath = PersistentDataPath; break;
                default: throw new InvalidDataException();
            }
            return Path.Combine(BasePath, Name);
        }
    }
}
