using HarmonyLib;
using RenpyParser;
using RenPyParser;
using RenPyParser.Transforms;
using RenPyParser.VGPrompter.DataHolders;
using RenPyParser.VGPrompter.Script.Internal;
using SimpleExpressionEngine;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(RenpyScriptExecution))]
    class PatchRenpyScriptExecution
    {
        [HarmonyPatch("PostLoad")]
        [HarmonyPatch("Run")]
        public static void Prefix(RenpyScriptExecution __instance, RenpyExecutionContext ____executionContext)
        {
            MaybeModContext(__instance, ____executionContext);
        }

        private static int depth = "[Info   : Unity Log] ".Length;
        private static int lineNumber = 0;
        private static Stack<int> lineStack = new Stack<int>();
        private static void AddDepth()
        {
            ++depth;
            lineStack.Push(lineNumber);
            lineNumber = 0;
        }
        private static void SubDepth()
        {
            --depth;
            lineNumber = lineStack.Pop();
        }
        private static bool markEntry = false;
        private static void Log(string s)
        {
            var prefix = new string(' ', depth - "[Info   : Unity Log] ".Length);
            if (markEntry)
            {
                prefix = prefix.Substring(0, prefix.Length - 1) + ">";
                markEntry = false;
            }
            Debug.Log(prefix + lineNumber + ": " + s);
        }
        private static string Indent(string s) => s.Replace("\n", "\n" + new string(' ', depth + 1 + (lineNumber + ": ").Length));

        static void MaybeModContext(RenpyScriptExecution instance, RenpyExecutionContext context)
        {
            if (!Mod.IsModded())
            {
                return;
            }
            var script = context.script;
            var blocks = script.Blocks;
            var rawBlocks = typeof(Blocks).GetField("blocks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(blocks) as Dictionary<string, RenpyBlock>;
            var rawBlockEntryPoints = typeof(Blocks).GetField("blockEntryPoints", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(blocks) as Dictionary<string, BlockEntryPoint>;
            var keys = new string[rawBlocks.Count];
            rawBlocks.Keys.CopyTo(keys, 0);
            lineNumber = 0;
            foreach (var Key in keys)
            {
                if (rawBlocks.ContainsKey(Key))
                {
                    //Debug.Log("Removing label " + Key);
                    RenpyBlock block = rawBlocks[Key];
                    BlockEntryPoint entryPoint = rawBlockEntryPoints[Key];
                    //rawBlocks.Remove(Key);

                    //Debug.Log("BLOCK " + block.Label + ":");
                    Log(Key + ":");
                    AddDepth();
                    foreach (var line in block.Contents)
                    {
                        if (lineNumber == entryPoint.startOffset)
                        {
                            markEntry = true;
                        }
                        switch (line)
                        {
                            case RenpyLoadImage renpyLoadImage:
                                Log("\"images/" + renpyLoadImage.fullImageDetails + "\"");
                                break;
                            case RenpyGoTo renpyGoTo:
                                var gotoDump = renpyGoTo.IsCall ? "call " : "jump ";
                                if (renpyGoTo.TargetLabel != "")
                                {
                                    gotoDump += renpyGoTo.TargetLabel;
                                }
                                else
                                {
                                    gotoDump += renpyGoTo.targetExpression.ToString();
                                }
                                if (renpyGoTo.IsCall)
                                {
                                    gotoDump += "(" + renpyGoTo.callParameters.Join(p => p.expression.ToString()) + ")";
                                }
                                Log(gotoDump);
                                break;
                            case RenpySize renpySize:
                                if (!renpySize.SizeData.StartsWith("size(") && !renpySize.SizeData.StartsWith("size ("))
                                {
                                    Log("Weird size:");
                                }
                                Log(renpySize.SizeData);
                                break;
                            case RenpyPause renpyPause:
                                Log(renpyPause.PauseData);
                                break;
                            case RenpyEasedTransform renpyEasedTransform:
                                Log(renpyEasedTransform.TransformCommand);
                                break;
                            case RenpyGoToLineUnless renpyGoToLineUnless:
                                Log("goto " + renpyGoToLineUnless.TargetLine + " unless " + renpyGoToLineUnless.ConditionText);
                                break;
                            case RenpyNOP renpyNOP:
                                Log("pass");
                                break;
                            case RenpyImmediateTransform renpyImmediateTransform:
                                Log(renpyImmediateTransform.TransformCommand);
                                break;
                            case RenpyGoToLine renpyGoToLine:
                                Log("goto " + renpyGoToLine.TargetLine);
                                break;
                            case RenpyForkGoToLine renpyForkGoToLine:
                                if (!renpyForkGoToLine.ParentJump)
                                {
                                    Debug.LogWarning("Found a ForkGoToLine that's not a ParentJump");
                                }
                                Log("fork goto " + renpyForkGoToLine.TargetLine);
                                break;
                            case RenpyReturn renpyReturn:
                                Log("return");
                                break;
                            case RenpyStandardProxyLib.Expression expression:
                                Log("expr" + (expression.WaitInteraction ? " (wait)" : "") + ":");
                                LogExpression(expression.Expr);
                                break;
                            case RenpyLabelEntryPoint renpyLabelEntryPoint:
                                var data = renpyLabelEntryPoint.NestedLabelData;
                                var name = data.Substring(0, data.IndexOf(" "));
                                var label = data.Substring(data.IndexOf(" ") + 1);
                                var hasParameters = false;
                                switch (name)
                                {
                                    case "contains":
                                    case "block":
                                        Log(name + ":");
                                        break;
                                    case "label":
                                        label = renpyLabelEntryPoint.entryPoint.label;
                                        if (renpyLabelEntryPoint.entryPoint.callParameters.Length == 0)
                                        {
                                            Log(label + ":");
                                        } else
                                        {
                                            hasParameters = true;
                                            Log(label + "():");
                                            AddDepth();
                                            foreach (var parameter in renpyLabelEntryPoint.entryPoint.callParameters)
                                            {
                                                if (parameter.expression == null)
                                                {
                                                    Log(parameter.name);
                                                }
                                                else
                                                {
                                                    Log(parameter.name + ":");
                                                    LogExpression(parameter.expression);
                                                }
                                                ++lineNumber;
                                            }
                                            SubDepth();
                                        }
                                        break;
                                    default:
                                        Log("Unrecognized nestedLabelData: " + data);
                                        break;
                                }
                                if (renpyLabelEntryPoint.entryPoint.rootLabel != Key)
                                {
                                    Log("Weird rootLabel: " + renpyLabelEntryPoint.entryPoint.rootLabel);
                                }
                                if (renpyLabelEntryPoint.entryPoint.label != label)
                                {
                                    Log("Weird label: " + label + " vs " + renpyLabelEntryPoint.entryPoint.label);
                                }
                                if (renpyLabelEntryPoint.entryPoint.startIndex != lineNumber)
                                {
                                    Log("Weird startIndex:" + renpyLabelEntryPoint.entryPoint.startIndex);
                                }
                                if (!hasParameters && renpyLabelEntryPoint.entryPoint.callParameters.Length != 0)
                                {
                                    Log("Weird callParameters:");
                                    AddDepth();
                                    foreach (var parameter in renpyLabelEntryPoint.entryPoint.callParameters)
                                    {
                                        if (parameter.expression == null)
                                        {
                                            Log(parameter.name);
                                        }
                                        else
                                        {
                                            Log(parameter.name + ":");
                                            LogExpression(parameter.expression);
                                        }
                                        ++lineNumber;
                                    }
                                    SubDepth();
                                }                                
                                break;
                            case RenpyFunction renpyFunction:
                            case RenpyHide renpyHide:
                            case RenpyInlinePython renpyInlinePython:
                            case RenpyMenuInput renpyMenuInput:
                            case RenpyOneLinePython renpyOneLinePython:
                            case RenpyPlay renpyPlay:
                            case RenpyQueue renpyQueue:
                            case RenpyScene renpyScene:
                            case RenpySetRandomRange renpySetRandomRange:
                            case RenpyShow renpyShow:
                            case RenpyStop renpyStop:
                            case RenpyTime renpyTime:
                            case RenpyUnlock renpyUnlock:
                            case RenpyWindow renpyWindow:
                            case RenpyWith renpyWith:
                            case RenpyStandardProxyLib.Text text:
                            case RenpyStandardProxyLib.WindowAuto windowAuto:
                            case RenpyStandardProxyLib.WaitForScreen waitForScreen:
                                Log("Need to handle " + line.GetType());
                                break;
                            default:
                                switch (line.GetType().ToString())
                                {
                                    case "RenpyParser.RenpyDialogueLine":
                                        Log("Need to handle " + line.GetType());
                                        break;
                                    default:
                                        Log("Unrecognized line type: " + line.GetType());
                                        break;
                                }
                                break;
                        }
                        ++lineNumber;
                    }
                    SubDepth();
                    ++lineNumber;
                }
                //rawBlocks.Add(labelEntry.Key, BuildBlock(labelEntry.Key, labelEntry.Value));
            }
        }

        private static void LogExpression(CompiledExpression expression)
        {
            AddDepth();
            foreach (var instruction in expression.instructions)
            {
                switch (instruction.type)
                {
                    case InstructionType.LoadFloat:
                        Log(instruction.type.ToString() + " " + expression.constantFloats[instruction.argumentIndex].ToString());
                        break;
                    case InstructionType.LoadVariable:
                    case InstructionType.FunctionCall:
                    case InstructionType.LoadString:
                    case InstructionType.LoadAttribute:
                    case InstructionType.MethodCall:
                    case InstructionType.SetVariable:
                    case InstructionType.SetAttribute:
                        Log(instruction.type.ToString() + " " + expression.constantStrings[instruction.argumentIndex]);
                        break;
                    case InstructionType.LoadObject:
                        Log(instruction.type.ToString() + " " + expression.constantObjects[instruction.argumentIndex].ToString());
                        break;
                    case InstructionType.ArrayDefinition:
                        Log(instruction.type.ToString() + " " + instruction.argumentIndex);
                        break;
                    case InstructionType.Negate:
                    case InstructionType.Add:
                    case InstructionType.Substract:
                    case InstructionType.Multiply:
                    case InstructionType.Divide:
                    case InstructionType.Equal:
                    case InstructionType.NotEqual:
                    case InstructionType.Greater:
                    case InstructionType.GreaterEqual:
                    case InstructionType.Less:
                    case InstructionType.LessEqual:
                    case InstructionType.Not:
                    case InstructionType.And:
                    case InstructionType.Or:
                    case InstructionType.ArrayIndex:
                    case InstructionType.ArrayIndexAssign:
                    case InstructionType.IfElse:
                    case InstructionType.LoadNone:
                        Log(instruction.type.ToString());
                        break;
                    case InstructionType.Unknown:
                        Log("Unknown instruction?");
                        break;
                }
                ++lineNumber;
            }
            SubDepth();
        }

        static RenpyBlock BuildBlock(string name, PythonObj block)
        {
            var result = new RenpyBlock(name);
            foreach (var statement in block.Fields["block"].List)
            {
                result.Contents.Add(BuildLine(statement));
            }


            return result;
        }

        static HashSet<string> seenNames = new HashSet<string>();
        static Line BuildLine(PythonObj statement)
        {
            switch (statement.Name)
            {
                case "renpy.ast.While":
                case "renpy.ast.Return":
                case "renpy.ast.Python":
                case "renpy.ast.If":
                case "renpy.ast.Translate":
                case "renpy.ast.EndTranslate":
                case "renpy.ast.Call":
                case "renpy.ast.Pass":
                case "renpy.ast.UserStatement":
                case "renpy.ast.Show":
                case "renpy.ast.Hide":
                case "renpy.ast.ShowLayer":
                case "renpy.ast.Scene":
                case "renpy.ast.With":
                case "renpy.ast.Jump":
                case "renpy.ast.Label":
                default:
                    if (seenNames.Add(statement.Name))
                    {
                        Debug.Log("Need to implement " + statement.Name);
                    }
                    return null;
            }
        }
    }
}
