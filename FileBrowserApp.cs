using HarmonyLib;
using RenpyLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using UnityEngine;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(FileBrowserApp))]
    public static class PatchFileBrowserApp
    {
        [HarmonyPatch("GetEntry")]
        static bool Prefix(FileBrowserApp __instance, string path, ref FileBrowserEntries.FileBrowserEntry __result,
            ref Dictionary<string, List<FileBrowserEntries.FileBrowserEntry>> ___m_Directories)
        {
            if (__instance.appId == LauncherAppId.FileBrowser)
            {
                return true;
            }

            __result = null;
            foreach (var Entry in ___m_Directories[""])
            {
                if (Entry.Path == path)
                {
                    __result = Entry;
                    break;
                }
            }
            return false;
        }

        [HarmonyPatch("OnContextMenuOpenClicked")]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool DeletingPathTruncation = false;
            bool ReplacingLoad = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Is(OpCodes.Ldfld, AccessTools.Field(typeof(FileBrowserEntries.AssetReference), "Path")))
                {
                    DeletingPathTruncation = true;
                    yield return instruction;
                    continue;
                }
                if (DeletingPathTruncation)
                {
                    if (instruction.opcode != OpCodes.Stloc_3)
                    {
                        continue;
                    }
                    DeletingPathTruncation = false;
                }
                if (instruction.Is(OpCodes.Call, AccessTools.Method(typeof(Resources), "Load", new Type[] { typeof(string), typeof(Type) })))
                {
                    ReplacingLoad = true;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PatchFileBrowserApp), "LoadResource"));
                }
                if (ReplacingLoad)
                {
                    ReplacingLoad = instruction.opcode != OpCodes.Stsfld;
                }
                if (!ReplacingLoad)
                {
                    yield return instruction;
                }
            }
        }

        [HarmonyPatch("OnContextMenuOpenClicked")]
        static void Postfix(FileBrowserApp __instance, ref bool ___m_SwitchToViewer)
        {
            if (FileBrowserApp.ViewedAsset is Mod Mod)
            {
                Mod.ActiveMod = Mod;
                FileBrowserApp.ViewedAsset = null;
                ___m_SwitchToViewer = false;
                __instance.OnFileBrowserCloseClicked();
            }
        }

        static UnityEngine.Object LoadResource(string path, Type systemTypeInstance)
        {
            if (systemTypeInstance == typeof(Mod))
            {
                return new Mod(path);
            }
            return Resources.Load(Path.ChangeExtension(path, null), systemTypeInstance);
        }
    }
}
