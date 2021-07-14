using HarmonyLib;
using RenpyLauncher;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(LauncherMain))]
    public static class PatchLauncherMain
    {
        [HarmonyPatch("Start")]
        static void Prefix(LauncherMain __instance)
        {
            var FileBrowserCanvas = __instance.gameObject.transform.Find("FileBrowserCanvas");
            var ModBrowserCanvas = UnityEngine.Object.Instantiate(FileBrowserCanvas, FileBrowserCanvas.parent, false);
            ModBrowserCanvas.SetSiblingIndex(FileBrowserCanvas.GetSiblingIndex());
            ModBrowserCanvas.name = "ModBrowserCanvas";

            var ModBrowserApp = ModBrowserCanvas.gameObject.AddComponent<ModBrowserApp>();
            UnityEngine.Object.Destroy(ModBrowserCanvas.GetComponent<FileBrowserApp>());
            __instance.apps.Add(ModBrowserApp);

            Mod.InitializeMods();
        }

        // need to remove ModBrowserApp from the list of apps before serialization or else we'll break the base game
        [HarmonyPatch("SaveLauncher")]
        static void Prefix(LauncherMain __instance, out LauncherApp __state)
        {
            __state = __instance.apps.Find(App => App.appId == ModBrowserApp.ModBrowserAppId);
            __instance.apps.Remove(__state);
        }
        [HarmonyPatch("SaveLauncher")]
        static void Postfix(LauncherMain __instance, LauncherApp __state)
        {
            __instance.apps.Add(__state);
        }

        // skip straight to DDLC
        static bool NeedToDoki = true;
        [HarmonyPatch("SwitchToApp")]
        static void Prefix(ref LauncherAppId newAppId)
        {
            if (NeedToDoki)
            {
                LauncherMain.LoadDDLCScene = true;
                BiosApp.shownAtLeastOnce = true;
                newAppId = LauncherAppId.DokiDoki;
                NeedToDoki = false;
            }
        }
    }
}
