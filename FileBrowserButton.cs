using HarmonyLib;

namespace TestDDLCMod
{

    [HarmonyPatch(typeof(FileBrowserButton), "SetFileType")]
    public static class PatchFileBrowserButtonSetFileType
    {
        static void Postfix(FileBrowserButton __instance, FileBrowserEntries.FileBrowserEntry.Type type)
        {
            switch (type)
            {
                case PatchFileBrowserApp.ModArchiveType:
                    __instance.TextFileTypeComponent.text = "Mod Archive";
                    break;
                case PatchFileBrowserApp.ModDirectoryType:
                    __instance.TextFileTypeComponent.text = "Mod Directory";
                    __instance.FileTypeImage.sprite = __instance.FileFolderSprite;
                    break;
            }
        }
    }
}
