﻿using HarmonyLib;
using RenpyLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Networking;

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
            if (!Mod.IsModded())
            {
                return Resources.Load(Path.ChangeExtension(path, null), systemTypeInstance);
            }
            path = Path.Combine(Mod.ActiveMod.DataPath, path);
            if (systemTypeInstance == typeof(TextAsset))
            {
                return LoadLocalTextAsset(path);
            }
            if (systemTypeInstance == typeof(Sprite))
            {
                return LoadLocalSprite(path);
            }
            if (systemTypeInstance == typeof(AudioClip))
            {
                return LoadLocalAudioClip(path);
            }
            return null;
        }

        static UnityEngine.Object LoadLocalTextAsset(string path)
        {
            string text = File.ReadAllText(path);
            if (text.Length > 50000)
            {
                text = text.Substring(0, 50000) + "\n... (sorry, performance of this text box tanks after more characters ;-;)";
            }
            return new TextAsset(text);
        }
        static UnityEngine.Object LoadLocalSprite(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(bytes))
            {
                return null;
            }
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0, 0));
        }
        static UnityEngine.Object LoadLocalAudioClip(string path)
        {
            var audioType = AudioType.UNKNOWN;
            switch (Path.GetExtension(path))
            {
                case ".ogg": audioType = AudioType.OGGVORBIS; break;
                case ".mp3": audioType = AudioType.MPEG; break;
            }
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, audioType))
            {
                request.SendWebRequest();
                while (!request.isDone) { }
                if (!request.isHttpError)
                {
                    return DownloadHandlerAudioClip.GetContent(request);
                }
            }
            Debug.LogWarning("Can't open: " + path);
            return null;
        }
    }
}
