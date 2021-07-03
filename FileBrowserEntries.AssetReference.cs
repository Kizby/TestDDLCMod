using HarmonyLib;
using System;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(FileBrowserEntries.AssetReference))]
    public static class PatchFileBrowserEntriesAssetReference
    {
        [HarmonyPatch("GetTypeFromAssetType")]
        static void Postfix(FileBrowserEntries.AssetReference.AssetTypes type, ref Type __result)
        {
            if (type == PatchFileBrowserApp.ModAssetType)
            {
                __result = typeof(Mod);
            }
        }
        [HarmonyPatch("GetAssetTypeFromObject")]
        static void Postfix(UnityEngine.Object assetObject, ref FileBrowserEntries.AssetReference.AssetTypes __result)
        {
            if (assetObject.GetType() == typeof(Mod))
            {
                __result = PatchFileBrowserApp.ModAssetType;
            }
        }
    }
}
