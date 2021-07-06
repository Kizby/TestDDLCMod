using HarmonyLib;
using RenpyLauncher;
using System;
using UnityEngine;

namespace TestDDLCMod
{
    // this "unimplemented" method is called at the start of each game -.-
    [HarmonyPatch(typeof(DDLCMain_ProxyLib), "init_python_startuppythonblock_1820771261")]
    class IDGAFIfMethodNotImplemented
    {
        static bool Prefix()
        {
            return false;
        }
    }

    // many of the errors are not very descriptive, so let's get a stack trace to go with them
    [HarmonyPatch(typeof(Debug), "LogError", new Type[] { typeof(object) })]
    public static class PatchLogError
    {
        static void Postfix()
        {
            Debug.Log(Environment.StackTrace);
        }
    }

    // if there are more files than fit on the screen, the file buttons overlap onto the bottom bar; let's fix that
    [HarmonyPatch(typeof(FileBrowserApp), "PerformAppStart")]
    public static class FixFileBrowserViewportSize
    {
        static void Prefix(FileBrowserApp __instance)
        {
            var mailList = __instance.ListParentPanel.transform.Find("FileListPanel(Clone)/MailList") as RectTransform;
            mailList.sizeDelta -= new Vector2(0, 80);
        }
    }
}
