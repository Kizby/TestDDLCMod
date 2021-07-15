using HarmonyLib;
using RenpyParser;
using RenPyParser;
using RenPyParser.AssetManagement;
using RenPyParser.Sprites;
using RenPyParser.Transforms;
using RenPyParser.VGPrompter.DataHolders;
using RenPyParser.VGPrompter.Script.Internal;
using SimpleExpressionEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private static bool DumpBlocks = true;

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
            lineNumber = 0;
            if (DumpBlocks)
            {
                var script = context.script;
                var blocks = script.Blocks;
                var rawBlocks = GetPrivateField<Blocks, Dictionary<string, RenpyBlock>>(blocks, "blocks");
                var rawBlockEntryPoints = GetPrivateField<Blocks, Dictionary<string, BlockEntryPoint>>(blocks, "blockEntryPoints");
                foreach (var entry in rawBlocks)
                {
                    BlockEntryPoint entryPoint = rawBlockEntryPoints[entry.Key];
                    DumpBlock(entry.Key, entry.Value, entryPoint);
                }
            }

            if (!Mod.IsModded())
            {
                return;
            }

            // let's load all the bundles
            /*
            foreach (var bundleFile in Directory.GetFiles("Doki Doki Literature Club Plus_Data/StreamingAssets/AssetBundles/" + PathHelpers.GetPlatformForAssetBundles(Application.platform)))
            {
                if (bundleFile.EndsWith(".cy"))
                {
                    var filename = Path.GetFileNameWithoutExtension(bundleFile);
                    if (filename.StartsWith("label "))
                    {
                        Renpy.Resources.ChangeLabel(filename.Substring("label ".Length));
                    }
                }
            }*/

            foreach (var earlyPython in Mod.ActiveMod.EarlyPython)
            {
                ExecutePython(earlyPython, context);
            }

            foreach (var initBucket in Mod.ActiveMod.Inits)
            {
                foreach (var init in initBucket.Value)
                {
                    var initBlock = BuildBlock(init.Fields["block"], context);
                    RegisterBlock(initBlock, context);
                }
            }

            foreach (var entry in Mod.ActiveMod.Labels)
            {
                var newBlock = BuildBlock(entry.Key, entry.Value, context);
                RegisterBlock(newBlock, context);
            }

            if (unparseablePython.Count > 0)
            {
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
        }

        private static void RegisterBlock(RenpyBlock block, RenpyExecutionContext context)
        {
            if (block == null)
            {
                return;
            }
            var script = context.script;
            var blocks = script.Blocks;
            var rawBlocks = GetPrivateField<Blocks, Dictionary<string, RenpyBlock>>(blocks, "blocks");
            var rawBlockEntryPoints = GetPrivateField<Blocks, Dictionary<string, BlockEntryPoint>>(blocks, "blockEntryPoints");
            if (rawBlocks.ContainsKey(block.Label) || rawBlockEntryPoints.ContainsKey(block.Label))
            {
                rawBlocks.Remove(block.Label);
                rawBlockEntryPoints.Remove(block.Label);
            }
            if (block.callParameters == null)
            {
                block.callParameters = new RenpyCallParameter[0];
            }
            rawBlocks.Add(block.Label, block);
            rawBlockEntryPoints.Add(block.Label, new BlockEntryPoint(block.Label));

            if (soundVars.Count > 0)
            {
                CreateAudioData(context);
            }
            if (jumpMap.Count > 0)
            {
                FinalizeJumps(block);
                jumpMap.Clear();
            }

            if (DumpBlocks)
            {
                DumpBlock(block.Label, block, rawBlockEntryPoints[block.Label]);
            }
        }

        private static DataValue ExecutePython(PythonObj python, RenpyExecutionContext context)
        {
            var rawExpression = ExtractPyExpr(python.Fields["code"]);
            Log("Executing: " + rawExpression);
            try
            {
                var expression = new CompiledExpression();
                Parser.Parse(new Tokenizer(new StringReader(rawExpression))).Compile(expression);
                return ExpressionRuntime.Execute(expression, context);
            }
            catch (SyntaxException e)
            {
                unparseablePython.Add(Tuple.Create(rawExpression, e));
            }
            return new DataValue();
        }

        private static void DumpBlock(string Key, RenpyBlock block, BlockEntryPoint entryPoint = null)
        {
            Log(Key + ":");
            AddDepth();
            foreach (var line in block.Contents)
            {
                if (entryPoint != null && lineNumber == entryPoint.startOffset)
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
                            toLog += " layer " + show.Name;
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
                        if (!show.IsLayer)
                        {
                            toLog += " onlayer " + show.Layer;
                        }
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

        public static U GetPrivateField<T, U>(T obj, string field) where T : class where U : class
        {
            return typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj) as U;
        }
        public static void SetPrivateField<T, U>(T obj, string field, U value) where T : class where U : class
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

        public static void LogExpression(CompiledExpression expression)
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

        // for when the obj has one block field that we're building
        private static RenpyBlock BuildBlock(PythonObj obj, RenpyExecutionContext context)
        {
            RenpyBlock block = null;
            if (obj.List.Count > 1)
            {
                Debug.Log("Long block?");
            }
            obj = obj.List[0];
            switch (obj.Name)
            {
                case "renpy.ast.Screen":
                    obj = obj.Fields["screen"];
                    if (obj.Name != "renpy.sl2.slast.SLScreen")
                    {
                        Debug.LogWarning("Unknown screen ast!");
                        return null;
                    }

                    var screenName = "_screen_" + obj.Fields["name"].String;
                    Debug.Log("Trying to build screen " + screenName);
                    return null;
                    block = new RenpyBlock(screenName);
                    var rawParams = obj.Fields["parameters"].Fields["parameters"].List.Select(i => i.Tuple).ToList();
                    block.callParameters = new RenpyCallParameter[rawParams.Count];
                    for (var i = 0; i < block.callParameters.Length; ++i)
                    {
                        var paramName = rawParams[i][0].String;
                        var paramValue = rawParams[i][1].Type == PythonObj.ObjType.NONE ? null : ExtractPyExpr(rawParams[i][1]);
                        block.callParameters[i] = new RenpyCallParameter(paramName, paramValue);
                    }

                    bool modal = obj.Fields["modal"].Bool;
                    int zorder = int.Parse(obj.Fields["zorder"].String);
                    string layer = obj.Fields["layer"].String;
                    Dictionary<string, string> keywords = new Dictionary<string, string>();
                    foreach (var keyword in obj.Fields["keyword"].List)
                    {
                        keywords.Add(keyword.Tuple[0].String, ExtractPyExpr(keyword.Tuple[1]));
                    }

                    if (!ParseScreenNode(obj, keywords, block.Contents))
                    {
                        return null;
                    }
                    break;
                case "renpy.ast.Python":
                    // no labelled block, just some python to run
                    ExecutePython(obj, context);
                    break;
                case "renpy.ast.Define":
                case "renpy.ast.Default":
                    var store = ExtractStore(obj);
                    var name = obj.Fields["varname"].String;
                    if (store != "")
                    {
                        context.AddScope(store);
                        name = store + "." + name;
                    }
                    var value = ExecutePython(obj, context);
                    if (store == "audio")
                    {
                        // these are audio assets, not strings
                        context.SetVariableObject(name, CreateModAudioData(value.GetString()));
                    }
                    else switch (value.GetDataType())
                        {
                            case DataType.Float:
                                context.SetVariableFloat(name, value.GetFloat());
                                break;
                            case DataType.String:
                                context.SetVariableString(name, value.GetString());
                                break;
                            case DataType.ObjectRef:
                                context.SetVariableObject(name, value.GetObject());
                                break;
                            case DataType.None:
                                // umm, maybe?
                                context.SetVariableFloat(name, 0);
                                break;
                            default:
                                Debug.LogWarning("Failed to set " + name + " to: " + ExtractPyExpr(obj.Fields["code"]));
                                break;
                        }
                    break;
                case "renpy.ast.Style":
                    Debug.Log("Trying to build style " + obj.Fields["style_name"].String);
                    break;
                case "renpy.ast.Transform":
                    Debug.Log("Trying to build transform " + obj.Fields["varname"].String);
                    break;
                case "renpy.ast.Image":
                    //Debug.Log(obj.ToString());
                    string bundle = DontUnloadBundles.MOD_BUNDLE_NAME, imgName;
                    if (obj.Fields["imgname"].Tuple.Count > 1)
                    {
                        bundle = obj.Fields["imgname"].Tuple[0].String;
                        imgName = obj.Fields["imgname"].Tuple[1].String;
                    }
                    else
                    {
                        imgName = obj.Fields["imgname"].Tuple[0].String;
                    }

                    if (obj.Fields["atl"].Type != PythonObj.ObjType.NONE)
                    {
                        var fullname = $"{(bundle != "unbundled" ? $"{bundle} " : "")}{imgName}";
                        // Placeholder block so we don't break later
                        if (context.script.Blocks.Contains(fullname))
                        {
                            // base game has this transform, so use that
                            break;
                        }
                        block = new RenpyBlock(fullname);
                        block.callParameters = new RenpyCallParameter[0];
                        ParseATLObject(obj.Fields["atl"], block);
                        break;
                    }

                    var rawExpression = ExtractPyExpr(obj.Fields["code"]);
                    if (rawExpression.Contains("Composite("))
                    {
                        // Composites are apparently evaluated later? Just pass along the string
                        var fullname = bundle + " " + imgName;
                        block = BuildImageBlock(fullname, "image/" + rawExpression, context);
                        break;
                    }

                    var actual = ExecutePython(obj, context);
                    var characters = context.script.Characters;
                    if (characters.Contains(bundle))
                    {
                        // actually a character sprite definition
                        var fullname = bundle + "_" + imgName;
                        characters.Add(fullname, new CharacterData(fullname, image: actual.GetString()));
                    }
                    else if (actual.IsObject<RenpyStandardProxyLib.Text>())
                    {
                        // made the block in the assignment
                    }
                    else if (actual.GetDataType() == DataType.String)
                    {
                        var fullname = bundle + " " + imgName;
                        block = BuildImageBlock(fullname, actual.GetString(), context);
                    }
                    else if (actual.IsObject<RenpyStandardProxyLib.Image>())
                    {
                        // need to make a block
                        var image = actual.GetObjectAs<RenpyStandardProxyLib.Image>();
                        var fullname = bundle + " " + imgName;
                        block = BuildImageBlock(fullname, image.filename, context);
                        if (block != null && image is Mod_ProxyLib.LiveTile tiledImage)
                        {
                            // fuck, iunno, how many times do we need to tile to fill the screen?
                            block.Contents.Add(new RenpyImmediateTransform("xtile 10 ytile 10"));
                        }
                    }
                    else if (actual.IsObject<Mod_ProxyLib.ConditionSwitch>())
                    {
                        // stub for now
                    }
                    else if (actual.GetDataType() == DataType.ObjectRef)
                    {
                        Debug.LogWarning("Need to handle image expression of type: " + actual.GetObject().GetType());
                    }
                    else
                    {
                        // need to make a block
                        var fullname = bundle + " " + imgName;
                        block = BuildImageBlock(fullname, actual.ToString(), context);
                    }
                    break;
                default:
                    Log("Need to handle single block of " + obj.Name);
                    Log(obj.ToString());
                    break;
            }
            return block;
        }

        private static RenpyAudioData CreateModAudioData(string path)
        {
            var data = RenpyAudioData.CreateAudioData(path);
            var assetName = path;
            if (assetName.Contains(">"))
            {
                assetName = assetName.Split('>')[1];
            }
            if (assetName.StartsWith("/"))
            {
                // probably a modded asset
                data.simpleAssetName = assetName;
            }
            return data;
        }

        private static RenpyBlock BuildImageBlock(string label, string expression, RenpyExecutionContext context)
        {
            RenpyBlock block = null;
            if (!context.script.Blocks.Contains(label))
            {
                ActiveLabelAssetBundles labelBundles = AccessTools.StaticFieldRefAccess<ActiveLabelAssetBundles>(typeof(Renpy), "s_ActiveLabelAssetBundles");
                var labelAssetBundleDependencies = AccessTools.Field(typeof(ActiveLabelAssetBundles), "<LabelAssetBundle>k__BackingField").GetValue(labelBundles) as LabelAssetBundleDependencies;

                block = new RenpyImageBlock(label);
                block.callParameters = new RenpyCallParameter[0];
                if (expression.Contains("im.Composite"))
                {
                    expression = expression.Substring(expression.IndexOf("im.Composite"));
                    var composite = CompositeSpriteParser.ParseFixedCompositeSprite(expression.Replace("'", "\""));
                    for (var i = 0; i < composite.AssetPaths.Length; ++i)
                    {
                        var pathComponents = composite.AssetPaths[i].Split('/');
                        var asset = pathComponents[pathComponents.Length - 1];
                        if (pathComponents.Length > 1)
                        {
                            asset = pathComponents[pathComponents.Length - 2] + " " + asset;
                        }
                        asset = PathHelpers.SanitizePathToAddressableName(asset);

                        string bundle;
                        if (!labelAssetBundleDependencies.TryGetBundle(asset, out bundle))
                        {
                            // probably a mod asset
                            asset = composite.AssetPaths[i];
                            if (asset.StartsWith("images"))
                            {
                                asset = asset.Substring("images".Length);
                            }
                        }
                        block.Contents.Add(new RenpyLoadImage(asset, composite.AssetPaths[i]));
                        block.Contents.Add(new RenpyImmediateTransform($"xpos {composite.Offsets[i][0]} ypos {composite.Offsets[i][1]}"));
                    }
                }
                else
                {
                    var assetName = expression;
                    /*if (!Mod.ActiveMod.Assets[typeof(Sprite)].ContainsKey(assetName))
                    {
                        var existingAssetName = expression.Split('/').Last().Split('.').First() + "__image";
                        if (Mod.ActiveMod.Assets[typeof(Sprite)].ContainsKey(existingAssetName))
                        {
                            Mod.ActiveMod.Assets[typeof(Sprite)][assetName] = Mod.ActiveMod.Assets[typeof(Sprite)][existingAssetName];
                            string bundle;
                            if (labelAssetBundleDependencies.TryGetBundle(assetName, out bundle))
                            {
                                Debug.Log($"Why are we building an image block for {assetName}? It's already in {bundle}!");
                            }
                            else
                            {
                                labelAssetBundleDependencies.AddAsset(assetName, DontUnloadBundles.MOD_BUNDLE_NAME, Mod.ActiveMod.Assets[typeof(Sprite)][assetName]);
                            }
                        } else
                        {
                            Debug.Log($"Couldn't resolve {assetName} or {existingAssetName} as a mod asset");
                        }
                    }*/
                    block.Contents.Add(new RenpyLoadImage(assetName, expression));
                }
            }

            return block;
        }

        private static void ParseATLObject(PythonObj obj, RenpyBlock block)
        {
            return; //stub for now
            switch (obj.Name) {
                case "renpy.atl.RawBlock":
                    foreach (var statement in obj.Fields["statements"].List)
                    {
                        ParseATLObject(statement, block);
                    }
                    break;
                case "renpy.atl.RawMultipurpose":
                    if (!ValidateObj(obj, new Dictionary<string, Predicate<PythonObj>>()
                        {
                            {"splines", l => l.Type == PythonObj.ObjType.LIST && l.List.Count == 0 },
                            {"properties", l => l.Type == PythonObj.ObjType.LIST && l.List.Count == 0 },
                            {"warp_function", n => n.Type == PythonObj.ObjType.NONE },
                            {"expressions", l => l.Type == PythonObj.ObjType.LIST },
                            {"warper", n => n.Type == PythonObj.ObjType.NONE },
                            {"revolution", n => n.Type == PythonObj.ObjType.NONE },
                        }))
                    {
                        goto default;
                    }
                    var expressions = obj.Fields["expressions"].List;
                    if (expressions.Count > 1)
                    {
                        Log("Need to handle multiple expressions");
                        goto default;
                    }
                    string command = "";
                    if (expressions.Count > 0)
                    {
                        var expression = expressions[0].Tuple;
                        if (expression[1].Type != PythonObj.ObjType.NONE)
                        {
                            Log("Need to handle second element of expression tuple");
                            goto default;
                        }
                        command = ExtractPyExpr(expression[0]);
                        if (command.StartsWith("\""))
                        {
                            // just an image;
                        }
                    }


                    var duration = float.Parse(ExtractPyExpr(obj.Fields["duration"]));
                    RenpyTransformData data = new RenpyTransformData();
                    RenpyEasedTransform easedTransform = new RenpyEasedTransform(command, duration, RenpyEasedTransform.EaseType.Linear, ref data);
                    break;
                default:
                    Log("Need to handle atl object: " + obj.Name);
                    Log(Indent(obj.ToString()));
                    break;
            }
            return;
        }

        private static bool ParseScreenNode(PythonObj obj, Dictionary<string, string> keywords, List<Line> container)
        {
            switch (obj.Name)
            {
                case "renpy.sl2.slast.SLDisplayable":
                    ValidateObj(obj, new Dictionary<string, Predicate<PythonObj>>()
                    {
                        {"imagemap", i => i.Type == PythonObj.ObjType.BOOL && !i.Bool },
                        {"scope", i => i.Type == PythonObj.ObjType.BOOL && i.Bool },
                        {"child_or_fixed", i => i.Type == PythonObj.ObjType.BOOL && !i.Bool },
                        {"pass_context", i => i.Type == PythonObj.ObjType.BOOL && !i.Bool },
                        {"hotspot", i => i.Type == PythonObj.ObjType.BOOL && !i.Bool },
                        {"replaces", i => i.Type == PythonObj.ObjType.BOOL && i.Bool },
                    });
                    var scope = obj.Fields["scope"].Bool;
                    switch (obj.Fields["displayable"].Name)
                    {
                        case "renpy.text.text.Text":
                            List<RenpyCallParameter> parameters = new List<RenpyCallParameter>();
                            parameters.Add(new RenpyCallParameter("", ExtractPyExpr(obj.Fields["positional"].List[0])));
                            foreach (var keyword in obj.Fields["keyword"].List)
                            {
                                parameters.Add(new RenpyCallParameter(keyword.Tuple[0].String, ExtractPyExpr(keyword.Tuple[1])));
                            }
                            container.Add(new RenpyStandardProxyLib.Text(parameters.ToArray()));
                            break;
                        case "renpy.display.layout.Window":

                            break;
                        default:
                            if (!seenNames.Add(obj.Name))
                            {
                                Debug.LogWarning("Need to handle " + obj.Name);
                            }
                            return false;
                    }
                    break;
                default:
                    if (!seenNames.Add(obj.Name))
                    {
                        Debug.LogWarning("Need to handle " + obj.Name);
                    }
                    return false;
            }
            return true;
        }

        private static RenpyBlock BuildBlock(string name, PythonObj block, RenpyExecutionContext context)
        {
            var result = new RenpyBlock(name);
            if (block.Fields.ContainsKey("parameters"))
            {
                var parameters = block.Fields["parameters"];
                if (parameters.Type != PythonObj.ObjType.NONE)
                {
                    result.callParameters = parameters.Fields["parameters"].List
                        .Select(t => t.Tuple)
                        .Select(t => new RenpyCallParameter(t[0].String, t[1].Type == PythonObj.ObjType.NONE ? null : ExtractPyExpr(t[1])))
                        .ToArray();
                }
            }
            foreach (var pythonObj in block.Fields["block"].List)
            {
                ParsePythonObj(pythonObj, result.Contents, name, context);
            }
            return result;
        }

        private static HashSet<string> seenNames = new HashSet<string>();
        private static Dictionary<object, Line> jumpMap = new Dictionary<object, Line>();
        private static List<Tuple<string, SyntaxException>> unparseablePython = new List<Tuple<string, SyntaxException>>();
        private static Dictionary<string, string> soundVars = new Dictionary<string, string>();
        private static void ParsePythonObj(PythonObj obj, List<Line> container, string label, RenpyExecutionContext context)
        {
            //Debug.Log("Parsing " + obj.Name);
            switch (obj.Name)
            {
                case "renpy.ast.While":
                    var conditionString = ExtractPyExpr(obj.Fields["condition"]);
                    var condition = Parser.Compile(conditionString);
                    var gotoStmt = new RenpyGoToLineUnless(conditionString, -1);
                    gotoStmt.CompiledExpression = condition;
                    container.Add(gotoStmt);

                    foreach (var stmt in obj.Fields["block"].List)
                    {
                        ParsePythonObj(stmt, container, label, context);
                    }
                    var loopGoto = new RenpyGoToLine(-1);
                    container.Add(loopGoto);
                    jumpMap.Add(loopGoto, gotoStmt);

                    var gotoTarget = new RenpyNOP();
                    container.Add(gotoTarget);
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
                        catch (Exception e)
                        {
                            Debug.LogWarning("Inner parser exception!");
                            Debug.LogWarning(Indent(e.ToString()));
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
                            ParsePythonObj(stmt, container, label, context);
                        }

                        var hardGoto = new RenpyGoToLine(-1);
                        jumpMap.Add(hardGoto, afterIf);
                        container.Add(hardGoto);
                    }
                    jumpMap.Add(lastGoto, afterIf);
                    container.Add(afterIf);
                    break;
                case "renpy.ast.Scene":
                    ValidateObj(obj, new Dictionary<string, Predicate<PythonObj>>() {
                        { "layer", i => i.Type == PythonObj.ObjType.NONE},
                    });
                    goto case "renpy.ast.Show";
                case "renpy.ast.Show":
                    ValidateObj(obj, new Dictionary<string, Predicate<PythonObj>>() {
                        { "atl", i => i.Type == PythonObj.ObjType.NONE || i.Type == PythonObj.ObjType.NEWOBJ},
                    });
                    ValidateTuple(obj, "imspec", new List<Predicate<PythonObj>>()
                    {
                        i => i.Type == PythonObj.ObjType.TUPLE && i.Tuple.Count < 3 && i.Tuple.TrueForAll(j => j.Type == PythonObj.ObjType.STRING),
                        i => i.Type == PythonObj.ObjType.NONE || i.Type == PythonObj.ObjType.NEWOBJ,
                        i => i.Type == PythonObj.ObjType.NONE || i.Type == PythonObj.ObjType.STRING,
                        i => i.Type == PythonObj.ObjType.LIST && (obj.Name == "renpy.ast.Show" || i.List.Count == 0),
                        i => i.Type == PythonObj.ObjType.NONE || i.Type == PythonObj.ObjType.STRING,
                        i => i.Type == PythonObj.ObjType.NONE || i.Type == PythonObj.ObjType.NEWOBJ,
                        i => i.Type == PythonObj.ObjType.LIST && i.List.Count == 0,
                    });
                    var imspec = obj.Fields["imspec"].Tuple;
                    var args = imspec[0].Tuple;
                    string stringCall = null;
                    if (imspec[1].Type == PythonObj.ObjType.NEWOBJ)
                    {
                        stringCall = ExtractPyExpr(imspec[1]);
                    }
                    if (obj.Name == "renpy.ast.Scene")
                    {
                        container.Add(new RenpyScene("scene " + args.Join(a => a.String, " ")));
                    }
                    else
                    {
                        var renpyShow = new RenpyShow("show ");
                        if (stringCall != null)
                        {
                            renpyShow.ShowData += "expression ";
                            renpyShow.show.StringCall = Parser.Compile(stringCall);
                        }
                        renpyShow.show.IsImage = true;
                        renpyShow.show.ImageName = args[0].String;
                        renpyShow.show.Variant = args.Count > 1 ? args[1].String : "";
                        renpyShow.ShowData += args.Join(a => a.String, " ");

                        if (imspec[2].Type == PythonObj.ObjType.STRING)
                        {
                            renpyShow.ShowData += " as " + imspec[2].String;
                            renpyShow.show.As = imspec[2].String;
                        }
                        if (imspec[4].Type == PythonObj.ObjType.STRING)
                        {
                            renpyShow.ShowData += " onlayer " + imspec[4].String;
                            renpyShow.show.Layer = imspec[4].String;
                        }
                        else
                        {
                            renpyShow.show.Layer = "master";
                        }

                        if (imspec[5].Type == PythonObj.ObjType.NEWOBJ)
                        {
                            var zorder = int.Parse(ExtractPyExpr(imspec[5]));
                            renpyShow.ShowData += " zorder " + zorder;
                            renpyShow.show.ZOrder = zorder;
                            renpyShow.show.HasZOrder = true;
                        }

                        var ats = imspec[3].List.Select(i => ExtractPyExpr(i)).ToArray();
                        renpyShow.show.TransformName = "";

                        // not handling call parameters yet
                        renpyShow.show.TransformCallParameters = new RenpyCallParameter[0];

                        if (ats.Length > 0)
                        {
                            renpyShow.ShowData += " at " + ats[0];
                            renpyShow.show.TransformName = ats[0];
                            if (ats.Length > 1)
                            {
                                Debug.LogWarning("Need to handle multiple ats in show!");
                            }
                        }

                        if (obj.Fields["atl"].Type == PythonObj.ObjType.NEWOBJ)
                        {
                            Debug.LogWarning("Need to handle atl block");
                        }
                        container.Add(renpyShow);
                    }
                    break;
                case "renpy.ast.UserStatement":
                    ValidateObj(obj, new Dictionary<string, Predicate<PythonObj>>()
                    {
                        { "block", i => i.List.Count == 0},
                        { "parsed", i => i.Type == PythonObj.ObjType.NONE },
                        { "line", i => i.Type == PythonObj.ObjType.STRING },
                        { "translatable", i => !i.Bool },
                    });
                    var line = obj.Fields["line"].String;
                    var lineArgs = line.Split(' ');
                    var valid = true;
                    switch (lineArgs[0])
                    {
                        case "pause":
                            container.Add(new RenpyPause(line, Parser.Compile(line.Substring(line.IndexOf(' ') + 1))));
                            break;
                        case "play":
                            var renpyPlay = new RenpyPlay();
                            renpyPlay.play = new Play();
                            if (lineArgs[1].ToLower() == "music")
                            {
                                renpyPlay.play.Channel = Channel.Music;
                            }
                            else if (lineArgs[1].ToLower() == "sound" || lineArgs[1].ToLower() == "audio")
                            {
                                renpyPlay.play.Channel = Channel.Sound;
                            }
                            else if (lineArgs[1].ToLower() == "musicpoem")
                            {
                                renpyPlay.play.Channel = Channel.MusicPoem;
                            }
                            else
                            {
                                Debug.LogWarning("Weird channel: " + lineArgs[1]);
                                renpyPlay.play.Channel = Channel.Sound + 1;
                                valid = false;
                                break;
                            }

                            if ("\"'".Contains(lineArgs[2][0]))
                            {
                                var quoteIndex = lineArgs[0].Length + 1 + lineArgs[1].Length + 1;
                                var quoteChar = line[quoteIndex];
                                if (!"\"'".Contains("" + quoteChar))
                                {
                                    Debug.LogWarning("Screwed up indices, char " + quoteIndex + " should be a quote in: " + line);
                                }
                                var asset = line.Substring(quoteIndex + 1, line.Substring(quoteIndex + 1).IndexOf(quoteChar));
                                lineArgs[2] = asset;
                                if (asset.Contains(" "))
                                {
                                    // blech, need to fix up lineArgs
                                    var offset = asset.Count(c => c == ' ');
                                    var newLineArgs = new string[lineArgs.Length - offset];
                                    for (var i = 0; i + offset < lineArgs.Length; ++i)
                                    {
                                        newLineArgs[i] = lineArgs[i + (i < 3 ? 0 : offset)];
                                    }
                                    lineArgs = newLineArgs;
                                }

                                // apparently play asset paths *have* to be stored in a variable somewhere -.-
                                var varName = $"${label}_{container.Count}";
                                if (soundVars.ContainsKey(lineArgs[2]))
                                {
                                    varName = soundVars[lineArgs[2]];
                                }
                                else
                                {
                                    soundVars.Add(lineArgs[2], varName);
                                }
                                lineArgs[2] = varName;
                            }
                            renpyPlay.play.Asset = lineArgs[2];

                            if (lineArgs.Length > 3)
                            {
                                for (var i = 3; i < lineArgs.Length; ++i)
                                {
                                    switch (lineArgs[i])
                                    {
                                        case "fadein": renpyPlay.play.fadein = float.Parse(lineArgs[++i]); break;
                                        case "fadeout": renpyPlay.play.fadeout = float.Parse(lineArgs[++i]); break;
                                        case "noloop":
                                        case "loop":
                                            // ignoring these for now
                                            break;
                                        default: Debug.LogWarning("Weird option in play (" + lineArgs[i] + "): " + line); break;
                                    }
                                }
                            }
                            if (!valid)
                            {
                                goto default;
                            }
                            container.Add(renpyPlay);
                            break;
                        case "stop":
                            var renpyStop = new RenpyStop();
                            renpyStop.stop = new Stop();
                            if (lineArgs[1].ToLower() == "music")
                            {
                                renpyStop.stop.Channel = Channel.Music;
                            }
                            else if (lineArgs[1].ToLower() == "sound")
                            {
                                renpyStop.stop.Channel = Channel.Sound;
                            }
                            else if (lineArgs[1].ToLower() == "musicpoem")
                            {
                                renpyStop.stop.Channel = Channel.MusicPoem;
                            }
                            else
                            {
                                Debug.LogWarning("Weird channel: " + lineArgs[1]);
                                renpyStop.stop.Channel = Channel.Sound + 1;
                                valid = false;
                                break;
                            }

                            if (lineArgs.Length > 2)
                            {
                                if (lineArgs[2] == "fadeout")
                                {
                                    renpyStop.stop.fadeout = float.Parse(lineArgs[3]);
                                }
                                else
                                {
                                    Debug.LogWarning("Weird option in stop: " + line);
                                }
                            }
                            if (!valid)
                            {
                                goto default;
                            }
                            container.Add(renpyStop);
                            break;
                        case "show":
                            if (lineArgs[1] == "screen")
                            {
                                // compiled expression ends with "FunctionCall tear" or similar
                                // need to replace that with "FunctionCall _screen_tear" to use the new logic
                                var expression = Parser.Compile(line.Substring(line.IndexOf("show screen ") + "show screen ".Length));
                                var instructions = expression.instructions;
                                var call = instructions[instructions.Count - 1];
                                var callName = expression.constantStrings[call.argumentIndex];
                                var newCallName = "_screen_" + callName;
                                call.argumentIndex = expression.AddConstantValue(newCallName);
                                instructions[instructions.Count - 1] = call;
                                container.Add(new RenpyStandardProxyLib.Expression(expression, true));
                                break;
                            }
                            goto default;
                        case "hide":
                            var renpyHide = new RenpyHide();
                            if (lineArgs[1] == "screen")
                            {
                                renpyHide.hide = new Hide(lineArgs[2], true);
                            }
                            else
                            {
                                Debug.Log("Unexpected user statement: " + line);
                                renpyHide.hide = new Hide(lineArgs[1], false);
                            }
                            container.Add(renpyHide);
                            break;
                        case "call":
                            if (lineArgs[1] == "screen")
                            {
                                // compiled expression ends with "FunctionCall tear" or similar
                                // need to replace that with "FunctionCall _screen_tear" to use the new logic
                                var expression = Parser.Compile(line.Substring(line.IndexOf("call screen ") + "call screen ".Length));
                                var instructions = expression.instructions;
                                var call = instructions[instructions.Count - 1];
                                var callName = expression.constantStrings[call.argumentIndex];
                                var newCallName = "_screen_" + callName;
                                call.argumentIndex = expression.AddConstantValue(newCallName);
                                instructions[instructions.Count - 1] = call;
                                container.Add(new RenpyStandardProxyLib.Expression(expression, true));

                                // need to hide window and wait for the call to finish now
                                container.Add(new RenpyStandardProxyLib.WindowAuto(false, "window auto hide"));
                                container.Add(new RenpyStandardProxyLib.WaitForScreen(callName));
                                break;
                            }
                            goto default;
                        case "window":
                            CompiledExpression transition = new CompiledExpression();
                            if (lineArgs[1].Contains("("))
                            {
                                transition = Parser.Compile(line.Substring(line.IndexOf("window") + "window".Length));
                                lineArgs[1] = lineArgs[1].Substring(0, lineArgs[1].IndexOf("("));
                            }
                            if (lineArgs[1] == "hide")
                            {
                                container.Add(new RenpyWindow() { window = new Window() { Mode = RenpyWindowManager.WindowManagerMode.Hide, Transition = transition } });
                            }
                            else if (lineArgs[1] == "show")
                            {
                                container.Add(new RenpyWindow() { window = new Window() { Mode = RenpyWindowManager.WindowManagerMode.Show, Transition = transition } });
                            }
                            else if (lineArgs[1] == "auto")
                            {
                                container.Add(new RenpyWindow() { window = new Window() { Mode = RenpyWindowManager.WindowManagerMode.Auto, Transition = transition } });
                            }
                            else
                            {
                                Debug.LogWarning("Unexpected window statement: " + line);
                                goto default;
                            }
                            break;
                        default:
                            Debug.Log("Need to handle userStatement line: " + line);
                            valid = false;
                            break;
                    }
                    if (!valid)
                    {
                        goto default;
                    }
                    break;
                case "renpy.ast.With":
                    var rawExpr = ExtractPyExpr(obj.Fields["expr"]);
                    var expr = Parser.Compile(rawExpr);
                    container.Add(new RenpyWith(rawExpr, expr));
                    break;
                case "renpy.ast.Translate":
                    // not doing anything with these nodes (yet?)
                    foreach (var stmt in obj.Fields["block"].List)
                    {
                        ParsePythonObj(stmt, container, label, context);
                    }
                    break;
                case "renpy.ast.EndTranslate":
                    break;
                case "renpy.ast.Menu":
                    //Debug.Log(obj);
                    var items = obj.Fields["items"].List.Select(t => t.Tuple).ToArray();
                    EmitSaylike(obj, container, label, "menu-with-caption", items[0][0].String, true);

                    var gotoMenu = new RenpyGoToLine(-1);
                    container.Add(gotoMenu);
                    var endTarget = new RenpyNOP();

                    var menuEntries = new List<RenpyMenuInputEntry>();
                    foreach (var tuple in items.Skip(1))
                    {
                        var what = tuple[0].String;
                        condition = Parser.Compile(ExtractPyExpr(tuple[1]));
                        var block = tuple[2].List;
                        Line menuTarget = null;
                        var targetIndex = container.Count;
                        foreach (var item in block)
                        {
                            ParsePythonObj(item, container, label, context);
                        }
                        var gotoEnd = new RenpyGoToLine(-1);
                        container.Add(gotoEnd);
                        jumpMap.Add(gotoEnd, endTarget);

                        menuTarget = container[targetIndex];
                        var entry = new RenpyMenuInputEntry(GetTextId(label, what), true, condition, -1);
                        menuEntries.Add(entry);
                        jumpMap.Add(entry, menuTarget);
                    }

                    // entry: id, toInterpolate, expression, target


                    var renpyMenuInput = new RenpyMenuInput(label, menuEntries, false);
                    container.Add(renpyMenuInput);
                    jumpMap.Add(gotoMenu, renpyMenuInput);
                    container.Add(endTarget);
                    break;
                case "renpy.ast.Say":
                    {
                        EmitSaylike(obj, container, label);
                        break;
                    }
                case "renpy.ast.Hide":
                    container.Add(new RenpyHide() { hide = new Hide(obj.Fields["imspec"].Tuple[0].Tuple[0].String, false) });
                    break;
                case "renpy.ast.Pass":
                    container.Add(new RenpyNOP());
                    break;
                case "renpy.ast.Call":
                    RenpyGoTo renpyCall;
                    if (!obj.Fields["expression"].Bool)
                    {
                        var callLabel = obj.Fields["label"].String;
                        renpyCall = new RenpyGoTo(callLabel, true);
                    }
                    else
                    {
                        // don't have a context right now, just assume it's a constant string
                        var callExpression = ExtractPyExpr(obj.Fields["label"]);
                        renpyCall = new RenpyGoTo("", true, "call expression " + callExpression);
                    }
                    if (obj.Fields["arguments"].Type == PythonObj.ObjType.NONE)
                    {
                        renpyCall.callParameters = new RenpyCallParameter[0];
                    }
                    else
                    {
                        var arguments = obj.Fields["arguments"];
                        ValidateObj(arguments, new Dictionary<string, Predicate<PythonObj>>()
                        {
                            {"arguments", i => i.Type == PythonObj.ObjType.LIST},
                            {"extrapos", i => i.Type == PythonObj.ObjType.NONE },
                            {"extrakw", i => i.Type == PythonObj.ObjType.NONE },
                        });
                        var kvPairs = arguments.Fields["arguments"].List;
                        renpyCall.callParameters = new RenpyCallParameter[kvPairs.Count];
                        for (var i = 0; i < kvPairs.Count; ++i)
                        {
                            var rawName = kvPairs[i].Tuple[0];
                            var rawValue = kvPairs[i].Tuple[1];
                            var name = rawName.Type == PythonObj.ObjType.STRING ? rawName.String : "";
                            var value = ExtractPyExpr(rawValue);
                            renpyCall.callParameters[i] = new RenpyCallParameter(name, value);
                        }
                    }
                    container.Add(renpyCall);
                    break;
                case "renpy.ast.Jump":
                    string target;
                    if (obj.Fields["expression"].Bool)
                    {
                        // don't have a context right now, just assume it's a constant string
                        target = ExtractPyExpr(obj.Fields["target"]);
                    }
                    else
                    {
                        target = obj.Fields["target"].String;
                    }
                    container.Add(new RenpyGoTo(target, false));
                    break;
                case "renpy.ast.ShowLayer":
                    var layer = obj.Fields["layer"].String;
                    var renpyShowLayer = new RenpyShow("show layer " + layer);
                    renpyShowLayer.show.Layer = layer;
                    renpyShowLayer.show.TransformName = "";
                    renpyShowLayer.show.TransformCallParameters = new RenpyCallParameter[0];
                    if (obj.Fields.ContainsKey("at_list"))
                    {
                        var ats = obj.Fields["at_list"].List;
                        if (ats.Count > 0)
                        {
                            var at = ExtractPyExpr(ats[0]);
                            renpyShowLayer.ShowData += " at " + at;
                            renpyShowLayer.show.TransformName = at;
                            if (ats.Count > 1)
                            {
                                Debug.LogWarning("Need to handle multiple ats in ShowLayer");
                            }
                        }
                    }
                    var atl = obj.Fields["atl"];
                    container.Add(renpyShowLayer);
                    break;
                case "renpy.ast.Label":
                    var cachedSoundVars = soundVars;
                    var cachedJumpMap = jumpMap;
                    soundVars = new Dictionary<string, string>();
                    jumpMap = new Dictionary<object, Line>();
                    RegisterBlock(BuildBlock(obj.Fields["name"].String, obj, context), context);
                    soundVars = cachedSoundVars;
                    jumpMap = cachedJumpMap;
                    break;
                default:
                    if (seenNames.Add(obj.Name))
                    {
                        Debug.Log("Need to implement " + obj.Name);
                    }
                    container.Add(new PlaceholderLine(obj.Name));
                    return;
            }
        }

        private static void EmitSaylike(PythonObj obj, List<Line> container, string label, string command_type = "say", string what = null, bool skipWait = false)
        {
            string tag = "";
            string variant = "";
            bool to_interpolate = true;
            bool hasCps = false;
            int cps = 1;
            int cpsStart = 0;
            int cpsEnd = 0;
            bool cpsMultiplier = false;
            bool developerCommentary = false;
            int immediateUntil = 0;
            List<Tuple<int, float>> waitTuples = new List<Tuple<int, float>>();

            if (obj.Fields.ContainsKey("who_fast") && obj.Fields["who_fast"].Bool)
            {
                tag = obj.Fields["who"].String;
            }
            if (obj.Fields.ContainsKey("attributes") && obj.Fields["attributes"].Type != PythonObj.ObjType.NONE)
            {
                variant = obj.Fields["attributes"].Tuple[0].String;
            }
            if (obj.Fields.ContainsKey("interact") && !obj.Fields["interact"].Bool)
            {
                Debug.LogWarning("Need to handle renpy.ast.Say.interact = False");
            }

            if (what == null)
            {
                what = obj.Fields["what"].String;
            }

            var ID = GetTextId(label, what);

            container.Add(Activator.CreateInstance(
                typeof(DialogueLine).Assembly.GetType("RenpyParser.RenpyDialogueLine"),
                new object[]
                {
                                label,
                                ID,
                                tag,
                                variant,
                                to_interpolate,
                                skipWait,
                                hasCps,
                                cps,
                                cpsStart,
                                cpsEnd,
                                cpsMultiplier,
                                developerCommentary,
                                immediateUntil,
                                waitTuples,
                                command_type,
                }
            ) as Line);
        }

        private static int GetTextId(string label, string what)
        {
            var ID = -1;

            // First need to add this text to the English dictionary for hashing
            var englishLines = Renpy.EnglishText as Lines;
            var englishDict = englishLines.GetDictionary();
            if (!englishDict.ContainsKey(label))
            {
                englishDict.Add(label, new Dictionary<int, string>());
            }
            bool foundExisting = false;
            foreach (var entry in englishDict[label])
            {
                if (entry.Value == what)
                {
                    ID = entry.Key;
                    foundExisting = true;
                    break;
                }
            }
            if (!foundExisting)
            {
                ID = what.GetHashCode();
                englishDict[label][ID] = what;
            }

            var lines = Renpy.Text as Lines;
            var textDict = lines.GetDictionary();
            if (!textDict.ContainsKey(label))
            {
                textDict.Add(label, new Dictionary<int, string>());
            }
            textDict[label][ID] = what;
            return ID;
        }

        private static void FinalizeJumps(RenpyBlock block)
        {
            var container = block.Contents;
            foreach (var entry in jumpMap)
            {
                switch (entry.Key)
                {
                    case RenpyGoToLine goToLine:
                        goToLine.TargetLine = container.IndexOf(jumpMap[goToLine]);
                        break;
                    case RenpyGoToLineUnless goToLineUnless:
                        goToLineUnless.TargetLine = container.IndexOf(jumpMap[goToLineUnless]);
                        break;
                    case RenpyMenuInputEntry menuInputEntry:
                        menuInputEntry.gotoLineTarget = container.IndexOf(entry.Value);
                        break;
                    default:
                        Debug.LogWarning($"Need to handle {entry.Key.GetType()} in the jump map!");
                        break;
                }
            }
        }
        private static void CreateAudioData(RenpyExecutionContext context)
        {
            context.AddScope("audio");
            foreach (var entry in soundVars)
            {
                var data = CreateModAudioData(entry.Key);
                context.SetVariableObject("audio." + entry.Value, data);
            }
            soundVars.Clear();
        }
        private static string ExtractPyExpr(PythonObj expr)
        {
            if (expr.Type == PythonObj.ObjType.NONE)
            {
                return "None";
            }
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

        private static bool ValidateObj(PythonObj obj, Dictionary<string, Predicate<PythonObj>> fields)
        {
            var result = true;
            foreach (var field in fields)
            {
                if (!field.Value(obj.Fields[field.Key]))
                {
                    Debug.Log("Unexpected " + field.Key + " in " + obj.Name + ": " + obj.Fields[field.Key]);
                    result = false;
                }
            }
            return result;
        }

        private static void ValidateTuple(PythonObj obj, string field, List<Predicate<PythonObj>> elements)
        {
            var tuple = obj.Fields[field].Tuple;
            // lambda since obj.ToString can be expensive
            Func<string> context = () => " in " + obj.Name + "." + field + ": " + obj;
            if (tuple.Count != elements.Count)
            {
                Debug.Log("Wrong tuple count (" + tuple.Count + " vs " + elements.Count + ")" + context());
                return;
            }
            for (var i = 0; i < tuple.Count; ++i)
            {
                if (!elements[i](tuple[i]))
                {
                    Debug.Log("Unexpected element " + i + context());
                }
            }
        }
        private static string ExtractStore(PythonObj stmt)
        {
            var store = stmt.Fields["store"].String;
            if (store == "store")
            {
                return "";
            }
            else if (store.StartsWith("store."))
            {
                return store.Substring("store.".Length);
            }
            Debug.LogWarning("Weird name for store: " + store);
            return store;
        }
    }

    public class PlaceholderLine : Line, IApply
    {
        private string desc;
        public PlaceholderLine(string desc)
        {
            this.desc = desc;
        }

        public void Apply(IContext context)
        {
            Debug.Log("Encountered placeholder for: " + desc);
        }
    }
}
