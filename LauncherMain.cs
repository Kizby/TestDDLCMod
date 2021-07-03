using HarmonyLib;
using RenpyLauncher;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(LauncherMain), "Start")]
    public static class PatchLauncherMainStart
    {
        public static LauncherAppId ModsAppId = LauncherAppId.ContinueUpdate + 1;
        static void Prefix(LauncherMain __instance)
        {
            var FileBrowserCanvas = __instance.gameObject.transform.Find("FileBrowserCanvas");
            var ModBrowserCanvas = UnityEngine.Object.Instantiate(FileBrowserCanvas, FileBrowserCanvas.parent, false);
            ModBrowserCanvas.SetSiblingIndex(FileBrowserCanvas.GetSiblingIndex());
            ModBrowserCanvas.name = "ModBrowserCanvas";

            var ModBrowserApp = ModBrowserCanvas.GetComponent<FileBrowserApp>();
            ModBrowserApp.appId = ModsAppId;
            __instance.apps.Add(ModBrowserApp);

            ModBrowserApp.HeaderBarPrefab = UnityEngine.Object.Instantiate(ModBrowserApp.HeaderBarPrefab);
            var Refresher = ModBrowserApp.HeaderBarPrefab.GetComponent<WindowBarTextRefresher>();
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
