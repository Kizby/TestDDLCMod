using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TestDDLCMod
{

    [HarmonyPatch(typeof(FileBrowserButton))]
    public static class PatchFileBrowserButton
    {
        [HarmonyPatch("SetFileType")]
        static void Postfix(FileBrowserButton __instance, FileBrowserEntries.FileBrowserEntry.Type type)
        {
            switch (type)
            {
                case ModBrowserApp.ModBaseGameType:
                    __instance.TextFileTypeComponent.text = "";
                    // dunno a cleaner way to grab this sprite
                    __instance.FileTypeImage.sprite = GameObject.Find("StartMenuButton").transform.Find("Image").GetComponent<Image>().sprite;
                    break;
                case ModBrowserApp.ModArchiveType:
                    __instance.TextFileTypeComponent.text = "Mod Archive";
                    break;
                case ModBrowserApp.ModDirectoryType:
                    __instance.TextFileTypeComponent.text = "Mod Directory";
                    __instance.FileTypeImage.sprite = __instance.FileFolderSprite;
                    break;
            }
        }
    }
}
