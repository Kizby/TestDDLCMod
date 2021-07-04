using RenpyLauncher;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using UnityPS;

namespace TestDDLCMod
{
    public class ModBrowserApp : FileBrowserApp
    {
        public const LauncherAppId ModBrowserAppId = LauncherAppId.ContinueUpdate + 1;
        public const FileBrowserEntries.FileBrowserEntry.Type ModBaseGameType = FileBrowserEntries.FileBrowserEntry.Type.Directory + 1;
        public const FileBrowserEntries.FileBrowserEntry.Type ModArchiveType = ModBaseGameType + 1;
        public const FileBrowserEntries.FileBrowserEntry.Type ModDirectoryType = ModArchiveType + 1;
        public const FileBrowserEntries.AssetReference.AssetTypes ModAssetType = FileBrowserEntries.AssetReference.AssetTypes.AudioClip + 1;

        public void Awake()
        {
            var FileBrowserApp = GetComponent<FileBrowserApp>();

            // LauncherApp fields
            icon = FileBrowserApp.icon;
            appId = ModBrowserAppId;
            notificationIcon = FileBrowserApp.notificationIcon;

            // WindowedLauncherApp fields
            Window = FileBrowserApp.Window;
            WindowRectTransform = FileBrowserApp.WindowRectTransform;
            FadeInImage = FileBrowserApp.FadeInImage;

            // FileBrowserApp fields
            ListParentPanel = FileBrowserApp.ListParentPanel;
            FileListPanel = FileBrowserApp.FileListPanel;
            BackgroundImage = FileBrowserApp.BackgroundImage;

            // Prefabs (so we can just use FileBrowserApp's initialization code)
            HeaderBarPrefab = FileBrowserApp.HeaderBarPrefab;
            MobileHeaderBarPrefab = FileBrowserApp.MobileHeaderBarPrefab;
            BottomBarPrefab = FileBrowserApp.BottomBarPrefab;
            MobileBottomBarPrefab = FileBrowserApp.MobileBottomBarPrefab;
            FileBrowserButtonPrefab = FileBrowserApp.FileBrowserButtonPrefab;
            MobileFileBrowserButtonPrefab = FileBrowserApp.MobileFileBrowserButtonPrefab;
        }

        public new void Start()
        {
            base.Start();
            Mod.InitializeMods();
        }

        public override IEnumerator PerformAppStart(CoroutineID id)
        {
            FixupChildren();

            SetPrivateField("m_Directories", CollectMods());
            yield return CallPrivateMethod<IEnumerator>("BuildDirectory", "", 0);

            // default to the currently active mod
            foreach (var Button in GetPrivateField<List<FileBrowserButton>>("m_Buttons"))
            {
                if (Button.FileName == Mod.ActiveMod.Name)
                {
                    Button.Select();
                }
                if (Button.CurrentlySelected == FileBrowserButton.State.Selected)
                {
                    break;
                }
            }

            CoroutineManager.UnregisterCoroutine(id);
            m_Starting = false;
            yield return null;
        }

        public override void OnAppClose() { }

        public override void SaveLauncher(Stream stream, IFormatter formatter)
        {
            // nothing for now
        }
        public override void LoadLauncher(Stream stream, IFormatter formatter)
        {
            // nothing for now
        }

        void FixupChildren()
        {
            string Prefix = (LauncherMain.IsMobileVersion() ? "Mobile" : "");
            ListParentPanel.transform.Find(Prefix + "BottomBarPrefab(Clone)").gameObject.SetActive(false); // don't need Delete or Open buttons

            var HeaderBar = ListParentPanel.transform.Find(Prefix + "HeaderBarPrefab(Clone)");

            // title bar should say Mods, not Files
            var Refresher = HeaderBar.GetComponent<WindowBarTextRefresher>();
            for (var i = 0; i < Refresher.textFields.Count; ++i)
            {
                if (Refresher.textFields[i].text == "Files")
                {
                    Refresher.textFields[i] = new WindowBarTextRefresher.TextStringPair()
                    {
                        text = "Mods",
                        textBox = Refresher.textFields[i].textBox,
                    };
                }
            }
            Refresher.RefreshText();
        }

        Dictionary<string, List<FileBrowserEntries.FileBrowserEntry>> CollectMods()
        {
            var ModsDirectory = Directory.CreateDirectory("mods");
            var Files = new List<FileBrowserEntries.FileBrowserEntry>()
            {
                CreateEntry("Base Game", DateTime.Parse("2021-06-30T14:37:00Z"), ModBaseGameType,
                    new FileBrowserEntries.AssetReference()
                    {
                        Path = "Base Game",
                        Type = ModAssetType,
                        AssetSize = 0,
                    }),
            };
            foreach (var ModInfo in ModsDirectory.EnumerateFileSystemInfos())
            {
                var ModType = FileBrowserEntries.FileBrowserEntry.Type.None;
                var AssetType = FileBrowserEntries.AssetReference.AssetTypes.None;
                long Size;
                if (ModInfo.Attributes.HasFlag(FileAttributes.Directory))
                {
                    ModType = ModDirectoryType;
                    AssetType = ModAssetType;
                    Size = GetDirectorySize(ModInfo as DirectoryInfo);
                }
                else
                {
                    Size = (ModInfo as FileInfo).Length;
                    try
                    {
                        using (ZipArchive Archive = new ZipArchive((ModInfo as FileInfo).OpenRead(), ZipArchiveMode.Read))
                        {
                            ModType = ModArchiveType;
                            AssetType = ModAssetType;
                        }
                    }
                    catch (InvalidDataException) { }
                }
                Files.Add(CreateEntry(ModInfo.Name, ModInfo.LastWriteTime, ModType,
                    new FileBrowserEntries.AssetReference()
                    {
                        Path = ModInfo.Name,
                        Type = AssetType,
                        AssetSize = Size,
                    }));
            }

            return new Dictionary<string, List<FileBrowserEntries.FileBrowserEntry>>()
            {
                {"", Files }
            };
        }

        static FileBrowserEntries.FileBrowserEntry CreateEntry(string Path, DateTime Modified, FileBrowserEntries.FileBrowserEntry.Type AssetType, FileBrowserEntries.AssetReference Asset)
        {
            return new FileBrowserEntries.FileBrowserEntry()
            {
                Path = Path,
                Visible = true,
                Modified = Modified,
                AssetType = AssetType,
                Asset = Asset,
                Flags = FileBrowserEntries.FileBrowserEntry.EntryFlags.Open,
            };
        }
        static long GetDirectorySize(DirectoryInfo DirectoryInfo)
        {
            long size = 0;
            foreach (var FileInfo in DirectoryInfo.GetFiles())
            {
                size += FileInfo.Length;
            }
            foreach (var SubDirectoryInfo in DirectoryInfo.GetDirectories())
            {
                // not fucking around with symlinks
                if (!SubDirectoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    size += GetDirectorySize(SubDirectoryInfo);
                }
            }
            return size;
        }

        private T GetPrivateField<T>(string name) where T : class
        {
            return typeof(FileBrowserApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this) as T;
        }
        private void SetPrivateField<T>(string name, T value) where T : class
        {
            typeof(FileBrowserApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this, value);
        }
        private T CallPrivateMethod<T>(string name, params object[] parameters) where T : class
        {
            return typeof(FileBrowserApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(this, parameters) as T;
        }
    }
}