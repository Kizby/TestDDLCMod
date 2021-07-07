using HarmonyLib;
using System;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(FileBrowserEntries.AssetReference))]
    public static class PatchFileBrowserEntries_AssetReference
    {
        [HarmonyPatch("GetTypeFromAssetType")]
        static void Postfix(FileBrowserEntries.AssetReference.AssetTypes type, ref Type __result)
        {
            if (type == ModBrowserApp.ModAssetType)
            {
                __result = typeof(Mod);
            }
        }
        [HarmonyPatch("GetAssetTypeFromObject")]
        static void Postfix(UnityEngine.Object assetObject, ref FileBrowserEntries.AssetReference.AssetTypes __result)
        {
            if (assetObject.GetType() == typeof(Mod))
            {
                __result = ModBrowserApp.ModAssetType;
            }
        }
    }
}
