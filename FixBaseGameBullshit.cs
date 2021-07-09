using HarmonyLib;
using RenpyLauncher;
using RenpyParser;
using RenPyParser.VGPrompter.DataHolders;
using SimpleExpressionEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Parser = SimpleExpressionEngine.Parser;

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
    public static class FixParsingArrays
    {
        static void Prefix(Parser __instance)
        {
            var tokenizer = PatchRenpyScriptExecution.GetPrivateField<Parser, Tokenizer>(__instance, "_tokenizer");
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

    // teach parser how to handle
    // $ if condition: action
    [HarmonyPatch(typeof(Parser), "ParseConditional")]
    public static class EnhanceParseConditional
    {
        static bool Prefix(Parser __instance, ref Node __result)
        {
            var tokenizer = PatchRenpyScriptExecution.GetPrivateField<Parser, Tokenizer>(__instance, "_tokenizer");
            if (tokenizer.Token != Token.If)
            {
                return true;
            }

            tokenizer.NextToken();
            var parseOr = typeof(Parser).GetMethod("ParseOr", BindingFlags.Instance | BindingFlags.NonPublic);
            var parseStatement = typeof(Parser).GetMethod("ParseStatement", BindingFlags.Instance | BindingFlags.NonPublic);
            var condition = parseOr.Invoke(__instance, new object[] { }) as Node;

            if (tokenizer.Token != EnhanceTokenizer.Colon)
            {
                throw new SyntaxException("No colon after if clause; " + tokenizer.Token + " " + tokenizer.Identifier + " instead");
            }
            tokenizer.NextToken();

            var trueNode = parseStatement.Invoke(__instance, new object[] { }) as Node;
            var falseNode = Activator.CreateInstance(__instance.GetType().Assembly.GetType("SimpleExpressionEngine.NodeNone")) as Node;
            __result = __instance.GetType().Assembly.GetType("SimpleExpressionEngine.NodeConditional")
                .GetConstructor(new Type[] { typeof(Node), typeof(Node), typeof(Node) })
                .Invoke(new object[] { condition, trueNode, falseNode }) as Node;

            return false;
        }
    }

    [HarmonyPatch(typeof(Tokenizer), "CreateNewToken")]
    public static class EnhanceTokenizer
    {
        public static readonly Token Colon = Token.None + 1;

        static FieldInfo currentCharField = typeof(Tokenizer).GetField("_currentChar", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo nextCharMethod = typeof(Tokenizer).GetMethod("NextChar", BindingFlags.Instance | BindingFlags.NonPublic);

        static void Prefix(Tokenizer __instance, ref Tokenizer.TokenizerItem item, ref bool __state)
        {
            __state = false;
            if ((bool)typeof(Tokenizer).GetField("eofReached", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance))
            {
                return;
            }
            while (char.IsWhiteSpace((char)currentCharField.GetValue(__instance)))
            {
                nextCharMethod.Invoke(__instance, new object[0]);
            }
            if ((char)currentCharField.GetValue(__instance) == ':')
            {
                __state = true;
                item._currentToken = Colon;
            }
        }
        static void Postfix(Tokenizer __instance, ref bool __state)
        {
            if (__state)
            {
                nextCharMethod.Invoke(__instance, new object[0]);
            }
        }
    }


    [HarmonyPatch(typeof(OneLinePython), "Parse")]
    public static class LetMeHandleSyntaxExceptions
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Is(OpCodes.Ldstr, "\n Error on "))
                {
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Rethrow);
                    yield return new CodeInstruction(OpCodes.Ldstr, "Not the exception oops");
                    yield return new CodeInstruction(OpCodes.Ldstr, "; not an error string either");
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }

    [HarmonyPatch(typeof(RenpyExecutionContext), "Jump")]
    public static class LogJumps
    {
        static void Prefix(string label)
        {
            Debug.Log("Jumping to: " + label);
        }
    }
    [HarmonyPatch(typeof(RenpyCallstack), "PushBlock")]
    public static class LogGotos
    {
        static void Prefix(RenpyBlock block)
        {
            Debug.Log("Goto: " + block.Label);
        }
    }

    [HarmonyPatch(typeof(RenpyCallstack), "Next")]
    public static class LogLines
    {
        static void Postfix(Line __result)
        {
            if (__result != null)
            {
                Debug.Log("Line: " + __result.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(RenpyScript), "HandleInLinePython")]
    public static class InspectInLinePython
    {
        static void Prefix(InLinePython InlinePython)
        {
            var methodInfo = typeof(DDLCMain_ProxyLib).GetMethod(InlinePython.functionName);
            if (methodInfo == null)
            {
                Debug.Log("No InlinePython for method: " + InlinePython.functionName);
            }
            else
            {
                Debug.Log("Handling InlinePython for method: " + InlinePython.functionName);
            }
        }
    }
}
