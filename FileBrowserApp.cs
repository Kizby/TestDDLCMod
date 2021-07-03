using HarmonyLib;
using RenpyLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(FileBrowserApp))]
    public static class PatchFileBrowserApp
    {
        public const FileBrowserEntries.FileBrowserEntry.Type ModArchiveType = FileBrowserEntries.FileBrowserEntry.Type.Directory + 1;
        public const FileBrowserEntries.FileBrowserEntry.Type ModDirectoryType = ModArchiveType + 1;
        public const FileBrowserEntries.AssetReference.AssetTypes ModAssetType = FileBrowserEntries.AssetReference.AssetTypes.AudioClip + 1;

        [HarmonyPatch("CacheAllDirectories")]
        static bool Prefix(FileBrowserApp __instance, ref Dictionary<string, List<FileBrowserEntries.FileBrowserEntry>> ___m_Directories)
        {
            if (__instance.appId == LauncherAppId.FileBrowser)
            {
                return true;
            }
            ___m_Directories = new Dictionary<string, List<FileBrowserEntries.FileBrowserEntry>>();

            var ModsDirectory = Directory.CreateDirectory("mods");
            var Files = new List<FileBrowserEntries.FileBrowserEntry>()
            {
                CreateEntry("Base Game", DateTime.Parse("2021-06-30T14:37:00Z"), ModDirectoryType,
                    new FileBrowserEntries.AssetReference()
                    {
                        Path = "Base Game",
                        Type = ModAssetType,
                        AssetSize = 0,
                    }),
            };
            foreach (var ModInfo in ModsDirectory.EnumerateFileSystemInfos())
            {
                var ModType = FileBrowserEntries.FileBrowserEntry.Type.None;
                var AssetType = FileBrowserEntries.AssetReference.AssetTypes.None;
                long Size;
                if (ModInfo.Attributes.HasFlag(FileAttributes.Directory))
                {
                    ModType = ModDirectoryType;
                    AssetType = ModAssetType;
                    Size = GetDirectorySize(ModInfo as DirectoryInfo);
                }
                else
                {
                    Size = (ModInfo as FileInfo).Length;
                    try
                    {
                        using (ZipArchive Archive = new ZipArchive((ModInfo as FileInfo).OpenRead(), ZipArchiveMode.Read))
                        {
                            ModType = ModArchiveType;
                            AssetType = ModAssetType;
                        }
                    }
                    catch (InvalidDataException _) { }
                }
                Files.Add(CreateEntry(ModInfo.Name, ModInfo.LastWriteTime, ModType,
                    new FileBrowserEntries.AssetReference()
                    {
                        Path = ModInfo.Name,
                        Type = AssetType,
                        AssetSize = Size,
                    }));
            }

            ___m_Directories.Add("", Files);

            return false;
        }

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

        static FileBrowserEntries.FileBrowserEntry CreateEntry(string Path, DateTime Modified, FileBrowserEntries.FileBrowserEntry.Type AssetType, FileBrowserEntries.AssetReference Asset)
        {
            return new FileBrowserEntries.FileBrowserEntry()
            {
                Path = Path,
                Visible = true,
                Modified = Modified,
                AssetType = AssetType,
                Asset = Asset,
                Flags = FileBrowserEntries.FileBrowserEntry.EntryFlags.Open,
            };
        }

        static long GetDirectorySize(DirectoryInfo DirectoryInfo)
        {
            long size = 0;
            foreach (var FileInfo in DirectoryInfo.GetFiles())
            {
                size += FileInfo.Length;
            }
            foreach (var SubDirectoryInfo in DirectoryInfo.GetDirectories())
            {
                // not fucking around with symlinks
                if (!SubDirectoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    size += GetDirectorySize(SubDirectoryInfo);
                }
            }
            return size;
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


    [HarmonyPatch()]
    public static class PatchFileBrowserAppBuildDirectory
    {
        static Predicate<CodeInstruction> StartPredicate = instruction => instruction.Is(OpCodes.Call, AccessTools.Method(typeof(RenpyParser.Utils), "IsConsolePlatform"));
        static Predicate<CodeInstruction> EndPredicate = instruction => instruction.opcode == OpCodes.Stloc_3;

        static MethodBase TargetMethod()
        {
            Type DesktopApp = typeof(FileBrowserApp);
            foreach (Type NestedType in DesktopApp.GetNestedTypes(BindingFlags.NonPublic))
            {
                if (NestedType.Name.Contains("BuildDirectory"))
                {
                    BindingFlags MethodFlags = BindingFlags.NonPublic | BindingFlags.Instance;
                    return NestedType.GetMethod("MoveNext", MethodFlags);
                }
            }
            return null;
        }

        // Postfix is being called on an unnameable inner class, so we can't just inject private members ;-;
        static FieldInfo ButtonsField = typeof(FileBrowserApp).GetField("m_Buttons", BindingFlags.NonPublic | BindingFlags.Instance);
        static void Postfix(object __instance)
        {
            // compiler generated classes are such a fucking pain
            var InternalType = __instance.GetType();
            FieldInfo ThisField = null;
            foreach (var FieldInfo in InternalType.GetFields())
            {
                if (FieldInfo.Name.EndsWith("__this"))
                {
                    ThisField = FieldInfo;
                    break;
                }
            }
            FileBrowserApp App = ThisField.GetValue(__instance) as FileBrowserApp;

            // now we can use the outer FileBrowserApp
            if (App.appId == LauncherAppId.FileBrowser)
            {
                return;
            }
            var Buttons = ButtonsField.GetValue(App) as List<FileBrowserButton>;
            foreach (var Button in Buttons)
            {
                if (Button.FileName == Mod.ActiveMod.Path)
                {
                    Button.Select();
                }
                if (Button.CurrentlySelected == FileBrowserButton.State.Selected)
                {
                    break;
                }
            }
        }
    }
}
