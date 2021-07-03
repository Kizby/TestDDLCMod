using RenpyLauncher;
using UnityEngine;

namespace TestDDLCMod
{
    public class ModBrowserApp : FileBrowserApp
    {
        public static LauncherAppId ModBrowserAppId = LauncherAppId.ContinueUpdate + 1;

        public ModBrowserApp()
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
            BottomBarPrefab = FileBrowserApp.BottomBarPrefab;
            FileBrowserButtonPrefab = FileBrowserApp.FileBrowserButtonPrefab;
            BackgroundImage = FileBrowserApp.BackgroundImage;

            // hack our own HeaderBarPrefab to not say "Files"
            HeaderBarPrefab = Instantiate(FileBrowserApp.HeaderBarPrefab);
            var Refresher = HeaderBarPrefab.GetComponent<WindowBarTextRefresher>();
            for (var i = 0; i < Refresher.textFields.Count; ++i)
            {
                if (Refresher.textFields[i].text == "Files")
                {
                    WindowBarTextRefresher.TextStringPair NewPair;
                    NewPair.text = "Mods";
                    NewPair.textBox = Refresher.textFields[i].textBox;
                    Refresher.textFields[i] = NewPair;
                }
            }
        }
    }
}