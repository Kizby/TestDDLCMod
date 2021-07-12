using HarmonyLib;
using RenpyLauncher;
using RenpyParser;
using RenPyParser.VGPrompter.DataHolders;
using SimpleExpressionEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
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

    // teach parser how to handle commas at the end of parameter lists
    [HarmonyPatch(typeof(Parser))]
    public static class EnhanceParseArguments
    {
        // a single static stack would *probably* be fine, but contention between threads would
        // be a nightmare to debug
        static Dictionary<Parser, Stack<Token>> closeTokens = new Dictionary<Parser, Stack<Token>>();
        [HarmonyPatch("ParseIdentifier")]
        static void Prefix(Parser __instance, Node classObjectNode)
        {
            if (!closeTokens.ContainsKey(__instance))
            {
                closeTokens[__instance] = new Stack<Token>();
            }
            closeTokens[__instance].Push(Token.CloseParens);
        }
        [HarmonyPatch("ParseIdentifier")]
        static void Postfix(Parser __instance, Node classObjectNode)
        {
            closeTokens[__instance].Pop();
        }

        [HarmonyPatch("ParseArrayDefinition")]
        static void Prefix(Parser __instance)
        {
            if (!closeTokens.ContainsKey(__instance))
            {
                closeTokens[__instance] = new Stack<Token>();
            }
            closeTokens[__instance].Push(Token.CloseBracket);
        }
        [HarmonyPatch("ParseArrayDefinition")]
        static void Postfix(Parser __instance)
        {
            closeTokens[__instance].Pop();
        }

        [HarmonyPatch("ParseExpressionRoot")]
        static void Postfix(Parser __instance, bool __state)
        {
            var tokenizer = PatchRenpyScriptExecution.GetPrivateField<Parser, Tokenizer>(__instance, "_tokenizer");
            var parsingParameters = (bool)typeof(Tokenizer).GetField("parsingParameters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tokenizer);
            if (parsingParameters && tokenizer.Token == Token.Comma)
            {
                // if the subsequent token is the close token, eat the comma so
                // we don't try to parse another arg
                if (tokenizer.LookAhead(1).Token == closeTokens[__instance].Peek())
                {
                    tokenizer.NextToken();
                }
            }
        }
    }

    // create blocks to handle variable assignments that will ultimately be handled as goto label's
    // e.g. "s = new Character('sayori')" or "foo = new Text(bar)"
    [HarmonyPatch(typeof(NodeVariableAssign), "Eval")]
    public static class HandleCharacterDefinitions
    {
        // bug in transpiler keeps me from doing that, so just replace the method
        public static bool Prefix(string ____variableName, Node ____valueExp, IContext ctx, ref DataValue __result)
        {
            DataValue dataValue = ____valueExp.Eval(ctx);
            if (dataValue.IsObject<Mod_ProxyLib.Character>())
            {
                var character = dataValue.GetObjectAs<Mod_ProxyLib.Character>();
                var characters = (ctx as RenpyExecutionContext).script.Characters;
                if (!characters.Contains(character.name))
                {
                    // make sure characters are addressable by name
                    characters.Add(character.name, character.value);
                }
                if (!characters.Contains(____variableName))
                {
                    // make sure characters are addressable by shorthand
                    characters.Add(____variableName, character.value);
                }
            }
            else if (dataValue.IsObject<Mod_ProxyLib.Text>())
            {
                var script = (ctx as RenpyExecutionContext).script;
                var blocks = script.Blocks;
                var label = ____variableName + "_t";
                if (!blocks.Contains(label))
                {
                    // need to add this transform
                    var rawBlocks = PatchRenpyScriptExecution.GetPrivateField<Blocks, Dictionary<string, RenpyBlock>>(blocks, "blocks");
                    var rawBlockEntryPoints = PatchRenpyScriptExecution.GetPrivateField<Blocks, Dictionary<string, BlockEntryPoint>>(blocks, "blockEntryPoints");
                    var block = new RenpyBlock(label);
                    block.callParameters = new RenpyCallParameter[0];

                    // just stub it for now
                    rawBlocks.Add(label, block);
                    rawBlockEntryPoints.Add(label, new BlockEntryPoint(label, 0));
                }
            }
            ctx.SetVariable(____variableName, dataValue);
            dataValue.ResetData();
            __result = dataValue;
            return false;
        }
    }

    // make sure Mod_ProxyLib gets dibs on handling resolving shit
    [HarmonyPatch(typeof(RenpyExecutionContext), "InitializeContext")]
    public class AddModProxyLib
    {
        public static void Prefix(ref object[] ___m_LibObjects)
        {
            if (!Mod.IsModded())
            {
                if (___m_LibObjects.Any(o => o.GetType() == typeof(Mod_ProxyLib)))
                {
                    Debug.LogWarning("Mod_ProxyLib still loaded in unmodded context!");
                }
                return;
            }
            if (!___m_LibObjects.Any(o => o.GetType() == typeof(Mod_ProxyLib)))
            {
                object[] newObjects = new object[___m_LibObjects.Length + 1];
                newObjects[0] = new Mod_ProxyLib(); // check our proxies first to simplify overriding behavior
                ___m_LibObjects.CopyTo(newObjects, 1);
                ___m_LibObjects = newObjects;
            }
        }
    }

    // handle python calls that take type arguments rather than actuals
    [HarmonyPatch(typeof(NodeFunctionCall))]
    public class HandlePythonBuiltins
    {
        [HarmonyPatch("Eval")]
        public static bool Prefix(string ____functionName, Node[] ____arguments, Dictionary<string, Node> ____namedArguments, IContext ctx, ref DataValue __result)
        {
            switch (____functionName)
            {
                case "hasattr":
                    fixupHasAttr(____functionName, ____arguments, ____namedArguments);
                    break;
                default:
                    break;
            }
            return true;
        }
        [HarmonyPatch("Compile")]
        public static bool Prefix(string ____functionName, Node[] ____arguments, Dictionary<string, Node> ____namedArguments, CompiledExpression expression)
        {
            switch (____functionName)
            {
                case "hasattr":
                    fixupHasAttr(____functionName, ____arguments, ____namedArguments);
                    break;
                default:
                    break;
            }
            return true;
        }

        public static void fixupHasAttr(string name, Node[] arguments, Dictionary<string, Node> namedArguments)
        {
            if (arguments.Length != 2 || namedArguments != null)
                return;
            var rawScope = arguments[0];
            if (!(rawScope is NodeVariable))
            {
                return;
            }
            // not actually a variable, but a reference to a scope
            arguments[0] = new NodeString((rawScope as NodeVariable).VariableName);
        }
    }

    // handle function calls to functions with a params argument
    [HarmonyPatch(typeof(ExpressionReflectionContext))]
    public static class HandleParamsArguments
    {
        [HarmonyPatch("ComposeReflectionParameters")]
        public static void Prefix(ParameterInfo[] parametersInfo, ref DataValue[] arguments)
        {
            if (parametersInfo.Length == 0)
            {
                return;
            }
            var maybeParams = parametersInfo[parametersInfo.Length - 1];
            if (maybeParams.IsDefined(typeof(ParamArrayAttribute), false))
            {
                // actually a params parameter, so collect the remaining arguments in an array
                var newArguments = new List<DataValue>();
                for (var i = 0; i < parametersInfo.Length - 1; ++i)
                {
                    newArguments.Add(arguments[i]);
                }
                var actualParams = new List<DataValue>();
                for (var i = parametersInfo.Length - 1; i < arguments.Length; ++i)
                {
                    actualParams.Add(arguments[i]);
                }
                newArguments.Add(DataValue.ComposeArray(actualParams.ToArray()));
                arguments = newArguments.ToArray();
                if (arguments.Length != parametersInfo.Length)
                {
                    Debug.LogWarning("Failed to fixup variadic arguments!");
                    Debug.LogWarning("parameters: " + parametersInfo.Join(p => p.ParameterType.ToString()));
                    Debug.LogWarning("arguments: " + arguments.Join(a => a.GetSystemType().ToString()));
                }
            }
        }
        // __state param is just to distinguish from above Prefix signature
        [HarmonyPatch("CheckParametersLengthCompatibility")]
        public static void Prefix(ParameterInfo[] parametersInfo, ref DataValue[] arguments, bool __state)
        {
            if (parametersInfo.Length == 0)
            {
                return;
            }
            var maybeParams = parametersInfo[parametersInfo.Length - 1];
            if (maybeParams.IsDefined(typeof(ParamArrayAttribute), false))
            {
                Debug.Log("Adjusting arguments for params parameter");
                // actually a params parameter, so collect the remaining arguments in an array
                var newArguments = new List<DataValue>();
                for (var i = 0; i < parametersInfo.Length - 1; ++i)
                {
                    newArguments.Add(arguments[i]);
                }
                var actualParams = new List<DataValue>();
                for (var i = parametersInfo.Length - 1; i < arguments.Length; ++i)
                {
                    actualParams.Add(arguments[i]);
                }
                newArguments.Add(DataValue.ComposeArray(actualParams.ToArray()));
                arguments = newArguments.ToArray();
                if (arguments.Length != parametersInfo.Length)
                {
                    Debug.LogWarning("Failed to fixup variadic arguments!");
                    Debug.LogWarning("parameters: " + parametersInfo.Join(p => p.ParameterType.ToString()));
                    Debug.LogWarning("arguments: " + arguments.Join(a => a.GetSystemType().ToString()));
                }
            }
        }
    }

    // handle interpolations with tuple indexing
    [HarmonyPatch(typeof(RenpyScript), "InterpolateText")]
    public static class HandleInterpolatedTuples
    {
        // unescaped [, some amount of not-[ that are the varName, [, some amount of digits that are the index, ], ], with the initial [ and final ] not included in the match
        static Regex tupleInterpolation = new Regex("(?<=(?<!\\\\)\\[)(?<varName>[^\\]]+)\\[(?<index>\\d+)\\](?=\\])", RegexOptions.Compiled);

        static bool Prefix(RenpyScript __instance, ref string text)
        {
            var matchCollection = tupleInterpolation.Matches(text);
            foreach (Match match in matchCollection)
            {
                var name = match.Groups["varName"].Value;
                DataValue varActual;
                if (!__instance.executionContext.ResolveVariable(name, out varActual))
                    throw new InvalidDataException("Can't find variable " + name);
                var index = int.Parse(match.Groups["index"].Value);
                var result = varActual.GetArrayValueAtIndex(index);
                text = text.Replace($"[{name}[{index}]]", result.GetAsString());
            }

            return true;
        }
    }

    // rethrow SyntaxExceptions in the Parser so our code can handle them
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

    // log instructions as they're evaluated
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
        static Line lastLine = null;
        static void Postfix(Line __result)
        {
            if (__result != null && !(__result is PlaceholderLine) && __result != lastLine)
            {
                if (__result.GetType().ToString() == "RenpyParser.RenpyDialogueLine")
                {
                    Debug.Log((AccessTools.Method(__result.GetType(), "ToWrapper").Invoke(__result, new object[] { (Renpy.CurrentContext as RenpyExecutionContext).script }) as IScriptLine).ToString());
                    
                }
                else
                {
                    Debug.Log("Line: " + __result.ToString());
                }
                lastLine = __result;
            }
        }
    }

    // are we actually able to call these inline python calls?
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

    [HarmonyPatch(typeof(ExpressionRuntime), "ExecuteWithInstance")]
    public static class ShowRuntimeEvaluations
    {
        public static void Postfix(CompiledExpression compiled, DataValue __result)
        {
            PatchRenpyScriptExecution.LogExpression(compiled);
            while (__result.GetDataType() == DataType.ObjectRef &&
                __result.GetObject() is DataValue)
            {
                __result = __result.GetObjectAs<DataValue>();
            }
            switch (__result.GetDataType())
            {
                case DataType.Float:
                    Debug.Log($"Evaluated to: {__result.GetFloat()}");
                    break;
                case DataType.ObjectRef:
                    Debug.Log($"Evaluated to a {__result.GetObject().GetType()}");
                    break;
            }
        }
    }
    [HarmonyPatch(typeof(RenpyExecutionContext))]
    public static class ShowVariablesSet
    {
        [HarmonyPatch("SetVariable", new Type[] { typeof(string), typeof(DataValue) })]
        public static void Prefix(string fullName, DataValue value)
        {
            string str = value.GetAsString();
            if (value.GetDataType() == DataType.ObjectRef && typeof(Array).IsAssignableFrom(value.GetObject().GetType()))
            {
                str = $"[{string.Join(", ", value.GetObject() as Array)}]";
            }
            Debug.Log($"Setting {fullName}: {str}");
        }
    }
    [HarmonyPatch(typeof(DataValue))]
    public static class ShowDataValueSets
    {
        [HarmonyPatch("SetArrayValueAtIndex")]
        public static void Prefix(int index, DataValue value)
        {
            string str = value.GetAsString();
            if (value.GetDataType() == DataType.ObjectRef && typeof(Array).IsAssignableFrom(value.GetObject().GetType()))
            {
                str = $"[{string.Join(", ", value.GetObject() as object[])}]";
            }
            Debug.Log($"Setting index {index}: {str}");
        }
        [HarmonyPatch("SetDictionaryValue")]
        public static void Prefix(string key, DataValue value)
        {
            string str = value.GetAsString();
            if (value.GetDataType() == DataType.ObjectRef && typeof(Array).IsAssignableFrom(value.GetObject().GetType()))
            {
                str = $"[{string.Join(", ", value.GetObject() as Array)}]";
            }
            Debug.Log($"Setting key {key}: {str}");
        }
    }
}
