using HarmonyLib;
using RenpyLauncher;
using SimpleExpressionEngine;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
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

    // parser incorrectly consumes the comma after a leading integer in the parameter list because it
    // isn't told it's parsing parameters until after the first token
    [HarmonyPatch(typeof(Parser), "ParseArrayDefinition")]
    public static class InspectParserParseArrayDefinition
    {
        static void Prefix(Parser __instance)
        {
            var tokenizer = typeof(Parser).GetField("_tokenizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(__instance) as Tokenizer;
            tokenizer.ParsingParameters(true);
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Throw)
                {
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Parser), "_tokenizer"));
                    yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Tokenizer), "ParsingParameters"));
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Tokenizer), "CreateNewToken")]
    public static class InspectTokenizerCreateNewToken
    {
        static void Postfix(Tokenizer __instance)
        {
            //Debug.Log("Token: " + __instance.Token);
        }
    }
}
