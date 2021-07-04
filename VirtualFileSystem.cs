using HarmonyLib;
using RenpyParser;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(VirtualFileSystem))]
    class PatchVirtualFileSystem
    {
        [HarmonyPatch("get_PersistentDataPath")]
        static bool Prefix(ref string __result)
        {
            if (!Mod.IsModded())
            {
                return true;
            }
            __result = Mod.ActiveMod.DataPath;
            return false;
        }
    }
}
