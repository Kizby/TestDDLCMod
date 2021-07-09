using HarmonyLib;
using RenpyParser;
using RenPyParser;
using RenPyParser.Transforms;
using RenPyParser.VGPrompter.DataHolders;
using RenPyParser.VGPrompter.Script.Internal;
using SimpleExpressionEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Parser = SimpleExpressionEngine.Parser;

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

        private static bool DumpBlocks = false;

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

        private static void MaybeModContext(RenpyScriptExecution instance, RenpyExecutionContext context)
        {
            if (!Mod.IsModded())
            {
                return;
            }
            var script = context.script;
            var blocks = script.Blocks;
            var rawBlocks = GetPrivateField<Blocks, Dictionary<string, RenpyBlock>>(blocks, "blocks");
            var rawBlockEntryPoints = GetPrivateField<Blocks, Dictionary<string, BlockEntryPoint>>(blocks, "blockEntryPoints");
            if (DumpBlocks)
            {
                lineNumber = 0;
                foreach (var entry in rawBlocks)
                {
                    BlockEntryPoint entryPoint = rawBlockEntryPoints[entry.Key];
                    DumpBlock(entry.Key, entry.Value, entryPoint);
                }
            }

            foreach (var entry in Mod.ActiveMod.Labels)
            {
                if (rawBlocks.ContainsKey(entry.Key))
                {
                    rawBlocks.Remove(entry.Key);
                    rawBlockEntryPoints.Remove(entry.Key);
                }
                var newBlock = BuildBlock(entry.Key, entry.Value);
                if (entry.Key == "splashscreen")
                {
                    newBlock.callParameters = new RenpyCallParameter[0];
                }
                rawBlocks.Add(entry.Key, newBlock);
                rawBlockEntryPoints.Add(entry.Key, new BlockEntryPoint(entry.Key));
            }

            Debug.Log("Unparseable python:");
            foreach (var entry in unparseablePython)
            {
                Debug.Log(Indent(entry.Item1));
                Debug.Log("||||||||||||||||");
                Debug.Log(Indent(entry.Item2.Message));
                Debug.Log("||||||||||||||||");
                Debug.Log(Indent(entry.Item2.StackTrace));
                Debug.Log("----------------");
            }
        }

        private static void DumpBlock(string Key, RenpyBlock block, BlockEntryPoint entryPoint)
        {
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
                                Log(label + ":");
                                if (renpyLabelEntryPoint.entryPoint.callParameters.Length > 0)
                                {
                                    hasParameters = true;
                                    LogParameters(renpyLabelEntryPoint.entryPoint.callParameters);
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
                            LogParameters(renpyLabelEntryPoint.entryPoint.callParameters);
                        }
                        break;
                    case RenpyShow renpyShow:
                        var show = renpyShow.show;
                        var toLog = "show";
                        if (show.IsStringCall)
                        {
                            toLog += " expression:";
                            Log(toLog);
                            toLog = "";
                            LogExpression(show.StringCall);
                        }
                        else if (show.IsLayer)
                        {
                            toLog += " layer " + show.Layer;
                        }
                        else
                        {
                            toLog += " " + show.AssetName;
                        }
                        if (show.As != "")
                        {
                            toLog += " as " + show.As;
                        }
                        var transform = show.TransformName;
                        if (transform == "" && show.IsLayer)
                        {
                            transform = "resetlayer";
                        }
                        if (transform != "")
                        {
                            toLog += " at " + show.TransformName;
                            if (show.TransformCallParameters.Length > 0)
                            {
                                toLog += ":";
                                Log(toLog);
                                toLog = "";
                                LogParameters(show.TransformCallParameters);
                            }
                        }
                        else if (show.TransformCallParameters.Length > 0)
                        {
                            Debug.Log("Why does show statement without transform have parameters?");
                            LogParameters(show.TransformCallParameters);
                        }
                        if (show.HasBehind)
                        {
                            toLog += " behind " + show.Behind;
                        }
                        toLog += " onlayer " + show.Layer;
                        if (show.HasZOrder)
                        {
                            toLog += " zorder " + show.ZOrder;
                        }
                        Log(toLog);
                        break;
                    case RenpyWith renpyWith:
                        Log("with:");
                        LogExpression(renpyWith.With);
                        break;
                    case RenpyStandardProxyLib.Text text:
                        Log("text:");
                        LogParameters(text.CallParameters);
                        break;
                    case RenpyHide renpyHide:
                        var hide = renpyHide.hide;
                        if (hide.IsScreen)
                        {
                            Log("hide screen " + hide.Name);
                        }
                        else
                        {
                            Log("hide " + hide.Name);
                        }
                        break;
                    case RenpyOneLinePython renpyOneLinePython:
                        var oneLinePython = GetPrivateField<RenpyOneLinePython, OneLinePython>(renpyOneLinePython, "m_OneLinePython");
                        Log("$");
                        LogExpression(oneLinePython.compiledExpression);
                        break;
                    case RenpyInlinePython renpyInlinePython:
                        var inlinePython = GetPrivateField<RenpyInlinePython, InLinePython>(renpyInlinePython, "m_InlinePython");
                        Log("python " + inlinePython.functionName);
                        break;
                    case RenpyTime renpyTime:
                        Log("time:");
                        LogExpression(renpyTime.expression);
                        break;
                    case RenpyScene renpyScene:
                        var scene = renpyScene.scene;
                        toLog = "scene";
                        if (scene.HasLayer)
                        {
                            toLog += " " + scene.Layer;
                        }
                        toLog += " " + scene.Image;
                        Log(toLog);
                        break;
                    case RenpyPlay renpyPlay:
                        var play = renpyPlay.play;
                        toLog = "play";
                        toLog += " " + play.Channel;
                        toLog += " " + play.Asset;
                        if (play.fadeout != 0)
                        {
                            toLog += " fadeout " + play.fadeout;
                        }
                        if (play.fadein != 0)
                        {
                            toLog += " fadein " + play.fadein;
                        }
                        Log(toLog);
                        break;
                    case RenpyUnlock renpyUnlock:
                        var unlock = renpyUnlock.unlock;
                        toLog = "unlock";
                        toLog += " " + unlock.UnlockName;
                        if (unlock.UnlockType == UnlockInfo.UnlockType.Normal)
                        {
                            toLog += " " + unlock.UnlockId;
                        }
                        else
                        {
                            toLog += " " + unlock.UnlockType;
                        }
                        Log(toLog);
                        break;
                    case RenpyStandardProxyLib.WindowAuto windowAuto:
                        Log("window auto " + (windowAuto.show ? "show" : "hide"));
                        break;
                    case RenpyStandardProxyLib.WaitForScreen waitForScreen:
                        Log("wait for screen " + waitForScreen.screen);
                        break;
                    case RenpySetRandomRange renpySetRandomRange:
                        Log("$ randrangevalue = renpy.random.randint(0, " + (renpySetRandomRange.Range - 1) + ")");
                        break;
                    case RenpyStop renpyStop:
                        var stop = renpyStop.stop;
                        toLog = "stop " + stop.Channel;
                        if (stop.fadeout != 0)
                        {
                            toLog += " fadeout " + stop.fadeout;
                        }
                        Log(toLog);
                        break;
                    case RenpyWindow renpyWindow:
                        var window = renpyWindow.window;
                        toLog = "window " + window.Mode.ToString().ToLower();
                        if (window.Transition.IsEmpty())
                        {
                            Log(toLog);
                        }
                        else
                        {
                            Log(toLog + ":");
                            LogExpression(window.Transition);
                        }
                        break;
                    case RenpyMenuInput renpyMenuInput:
                        if (renpyMenuInput.parentLabel != Key)
                        {
                            Log("renpyMenuInput.parentLabel: " + renpyMenuInput.parentLabel);
                        }
                        if (renpyMenuInput.hideWindow)
                        {
                            Log("renpyMenuInput.hideWindow");
                        }
                        Log("menu:");
                        AddDepth();
                        foreach (var entry in renpyMenuInput.entries)
                        {
                            var text = "\"" + Renpy.Text.GetLocalisedString(entry.textID, label: renpyMenuInput.parentLabel) + "\"";
                            Log(text + ": goto " + entry.gotoLineTarget);
                            LogExpression(entry.compiledExpression);
                            ++lineNumber;
                        }
                        SubDepth();
                        break;
                    case RenpyFunction renpyFunction:
                        var function = GetPrivateField<RenpyFunction, Function>(renpyFunction, "m_Function");
                        Log("function " + function.FunctionName);
                        break;
                    case RenpyQueue renpyQueue:
                        var queue = renpyQueue.queue;
                        Log("queue " + queue.Channel + " " + queue.Asset);
                        break;
                    default:
                        switch (line.GetType().ToString())
                        {
                            case "RenpyParser.RenpyDialogueLine":
                                var dialogueLine = line.GetType().GetField("m_DialogueLine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(line) as DialogueLine;
                                if (dialogueLine.CommandType != "say")
                                {
                                    Log("dialogueLine.CommandType: " + dialogueLine.CommandType);
                                }
                                if (dialogueLine.HasCps)
                                {
                                    Log("dialogueLine.Cps: " + dialogueLine.Cps);
                                    Log("dialogueLine.CpsEndIndex: " + dialogueLine.CpsEndIndex);
                                    Log("dialogueLine.CpsStartIndex: " + dialogueLine.CpsStartIndex);
                                }
                                if (dialogueLine.DeveloperCommentary)
                                {
                                    Log("dialogueLine.DeveloperCommentary");
                                }
                                if (dialogueLine.ImmediateUntil != 0)
                                {
                                    Log("dialogueLine.ImmediateUntil: " + dialogueLine.ImmediateUntil);
                                }
                                if (dialogueLine.IsCpsMultiplier)
                                {
                                    Log("dialogueLine.IsCpsMultiplier");
                                }
                                if (dialogueLine.Label != Key)
                                {
                                    Log("dialogueLine.Label: " + dialogueLine.Label);
                                }
                                if (dialogueLine.SkipWait)
                                {
                                    Log("dialogueLine.SkipWait");
                                }
                                if (dialogueLine.WaitIndicesAndTimes.Count > 0)
                                {
                                    Log("dialogueLine.WaitIndicesAndTimes: " + dialogueLine.WaitIndicesAndTimes.Join(t => "(" + t.Item1 + ", " + t.Item2 + ")"));
                                }
                                toLog = "\"" + Renpy.Text.GetLocalisedString(dialogueLine.TextID, label: dialogueLine.Label) + "\"";
                                if (dialogueLine.Variant != "")
                                {
                                    toLog = dialogueLine.Variant + " " + toLog;
                                }
                                if (dialogueLine.Tag != "")
                                {
                                    toLog = dialogueLine.Tag + " " + toLog;
                                }
                                Log(toLog);
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

        private static U GetPrivateField<T, U>(T obj, string field) where T : class where U : class
        {
            return typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj) as U;
        }
        private static void SetPrivateField<T, U>(T obj, string field, U value) where T : class where U : class
        {
            typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
        }

        private static void LogParameters(IEnumerable<RenpyCallParameter> parameters)
        {
            AddDepth();
            foreach (var parameter in parameters)
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
                    case InstructionType.LoadAttribute:
                    case InstructionType.MethodCall:
                    case InstructionType.SetVariable:
                    case InstructionType.SetAttribute:
                        Log(instruction.type.ToString() + " " + expression.constantStrings[instruction.argumentIndex]);
                        break;
                    case InstructionType.LoadString:
                        Log(instruction.type.ToString() + " '" + Indent(expression.constantStrings[instruction.argumentIndex]) + "'");
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

        private static RenpyBlock BuildBlock(string name, PythonObj block)
        {
            var result = new RenpyBlock(name);
            foreach (var pythonObj in block.Fields["block"].List)
            {
                ParsePythonObj(pythonObj, result.Contents, name);
            }
            foreach (var statement in result.Contents)
            {
                FinalizeLine(statement, result.Contents);
            }
            return result;
        }

        private static HashSet<string> seenNames = new HashSet<string>();
        private static Dictionary<Line, Line> jumpMap = new Dictionary<Line, Line>();
        private static List<Tuple<string, SyntaxException>> unparseablePython = new List<Tuple<string, SyntaxException>>();
        private static void ParsePythonObj(PythonObj obj, List<Line> container, string label)
        {
            switch (obj.Name)
            {
                case "renpy.ast.While":
                    var conditionString = ExtractPyExpr(obj.Fields["condition"]);
                    var condition = Parser.Compile(conditionString);
                    condition.AddInstruction(InstructionType.Not); // since this is a goto unless, we need to negate the condition
                    var gotoStmt = new RenpyGoToLineUnless(conditionString, -1);
                    gotoStmt.CompiledExpression = condition;
                    container.Add(gotoStmt);

                    foreach (var stmt in obj.Fields["block"].List)
                    {
                        ParsePythonObj(stmt, container, label);
                    }

                    var gotoTarget = new RenpyNOP();
                    container.Add(gotoStmt);
                    jumpMap.Add(gotoStmt, gotoTarget);
                    break;
                case "renpy.ast.Return":
                    container.Add(new RenpyReturn());
                    break;
                case "renpy.ast.Python":
                    var codeString = ExtractPyExpr(obj.Fields["code"]);
                    if (codeString.Contains("\n"))
                    {
                        RenpyInlinePython renpyInlinePython = new RenpyInlinePython(codeString, label);
                        InLinePython m_InlinePython = GetPrivateField<RenpyInlinePython, InLinePython>(renpyInlinePython, "m_InlinePython");
                        if (m_InlinePython.hash == 2039296337)
                        {
                            // this is the default firstrun setting block
                            // we use reset.sh instead of firstrun files now, so just go with DDLC+'s implementation
                            m_InlinePython.hash = 318042419;
                            m_InlinePython.functionName = "splashscreen_inlinepythonblock_318042419";
                        }
                        else if (m_InlinePython.hash == 1991019598)
                        {
                            // this is the default s_kill_early check
                            // base game checks Renpy.Characters.Exists now, so let's do that instead
                            m_InlinePython.hash = 85563775;
                            m_InlinePython.functionName = "splashscreen_inlinepythonblock_85563775";
                        }
                        container.Add(renpyInlinePython);
                    }
                    else try
                        {
                            RenpyOneLinePython renpyOneLinePython = new RenpyOneLinePython("$" + codeString);
                            container.Add(renpyOneLinePython);
                        }
                        catch (SyntaxException e)
                        {
                            unparseablePython.Add(Tuple.Create(codeString, e));
                        }
                    break;
                case "renpy.ast.If":
                    var entries = obj.Fields["entries"].List;
                    var afterIf = new RenpyNOP();
                    RenpyGoToLineUnless lastGoto = null;
                    foreach (var entry in entries)
                    {
                        conditionString = ExtractPyExpr(entry.Tuple[0]);
                        condition = Parser.Compile(conditionString);
                        gotoStmt = new RenpyGoToLineUnless(conditionString, -1);
                        gotoStmt.CompiledExpression = condition;
                        container.Add(gotoStmt);
                        if (lastGoto != null)
                        {
                            jumpMap.Add(lastGoto, gotoStmt);
                        }
                        lastGoto = gotoStmt;

                        foreach (var stmt in entry.Tuple[1].List)
                        {
                            ParsePythonObj(stmt, container, label);
                        }

                        var hardGoto = new RenpyGoToLine(-1);
                        jumpMap.Add(hardGoto, afterIf);
                    }
                    jumpMap.Add(lastGoto, afterIf);
                    container.Add(afterIf);
                    break;
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
                    if (seenNames.Add(obj.Name))
                    {
                        Debug.Log("Need to implement " + obj.Name);
                    }
                    return;
            }
        }
        private static void FinalizeLine(Line line, List<Line> container)
        {
            switch (line)
            {
                case RenpyGoToLine goToLine:
                    goToLine.TargetLine = container.IndexOf(jumpMap[goToLine]);
                    break;
                case RenpyGoToLineUnless goToLineUnless:
                    goToLineUnless.TargetLine = container.IndexOf(jumpMap[goToLineUnless]);
                    break;
            }
        }
        private static string ExtractPyExpr(PythonObj expr)
        {
            if (expr.Type == PythonObj.ObjType.STRING)
            {
                return expr.String;
            }
            if (expr.Name == "renpy.ast.PyExpr")
            {
                return expr.Args.Tuple[0].String;
            }
            else if (expr.Name == "renpy.ast.PyCode")
            {
                var source = expr.Fields["source"];
                return ExtractPyExpr(expr.Fields["source"]);
            }
            Debug.LogError("Trying to extractPyExpr on a " + expr.Type);
            return "";
        }
    }
}
