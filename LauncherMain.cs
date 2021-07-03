using HarmonyLib;
using RenpyLauncher;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(LauncherMain), "Start")]
    public static class PatchLauncherMainStart
    {
        static void Prefix(LauncherMain __instance)
        {
            var FileBrowserCanvas = __instance.gameObject.transform.Find("FileBrowserCanvas");
            var ModBrowserCanvas = UnityEngine.Object.Instantiate(FileBrowserCanvas, FileBrowserCanvas.parent, false);
            ModBrowserCanvas.SetSiblingIndex(FileBrowserCanvas.GetSiblingIndex());
            ModBrowserCanvas.name = "ModBrowserCanvas";

            var ModBrowserApp = ModBrowserCanvas.gameObject.AddComponent<ModBrowserApp>();
            UnityEngine.Object.Destroy(ModBrowserCanvas.GetComponent<FileBrowserApp>());
            __instance.apps.Add(ModBrowserApp);

        }
    }

}
