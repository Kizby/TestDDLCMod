﻿using HarmonyLib;
using RenpyParser;
using RenPyParser;
using RenPyParser.Transforms;
using RenPyParser.VGPrompter.DataHolders;
using RenPyParser.VGPrompter.Script.Internal;
using System;
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
        private static void Log(string s)
        {
            Debug.Log(new string(' ', depth - "[Info   : Unity Log] ".Length) + s);
        }
        private static string Indent(string s) => s.Replace("\n", "\n" + new string(' ', depth + 1));

        static HashSet<Type> seenTypes = new HashSet<Type>();
        static void MaybeModContext(RenpyScriptExecution instance, RenpyExecutionContext context)
        {
            if (!Mod.IsModded())
            {
                return;
            }
            var script = context.script;
            var blocks = script.Blocks;
            var rawBlocks = typeof(Blocks).GetField("blocks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(blocks) as Dictionary<string, RenpyBlock>;
            var keys = new string[rawBlocks.Count];
            rawBlocks.Keys.CopyTo(keys, 0);
            foreach (var Key in keys)
            {
                if (rawBlocks.ContainsKey(Key))
                {
                    //Debug.Log("Removing label " + Key);
                    RenpyBlock block = rawBlocks[Key];
                    rawBlocks.Remove(Key);

                    //Debug.Log("BLOCK " + block.Label + ":");
                    Log(Key + ":");
                    ++depth;
                    foreach (var line in block.Contents)
                    {
                        switch (line)
                        {
                            case RenpyLoadImage renpyLoadImage:
                                Log("LoadImage(" + renpyLoadImage.key + "): " + renpyLoadImage.fullImageDetails);
                                break;
                            case RenpyGoTo renpyGoTo:
                                var gotoDump = renpyGoTo.IsCall ? "call " : "jump ";
                                if (renpyGoTo.TargetLabel != "")
                                {
                                    gotoDump += renpyGoTo.TargetLabel;
                                } else
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
                                if (!renpySize.SizeData.StartsWith("size("))
                                {
                                    Log("Weird size:");
                                }
                                Log(renpySize.SizeData);
                                break;
                            case RenpyEasedTransform renpyEasedTransform:
                            case RenpyForkGoToLine renpyForkGoToLine:
                            case RenpyFunction renpyFunction:
                            case RenpyGoToLine renpyGoToLine:
                            case RenpyGoToLineUnless renpyGoToLineUnless:
                            case RenpyHide renpyHide:
                            case RenpyImmediateTransform renpyImmediateTransform:
                            case RenpyInlinePython renpyInlinePython:
                            case RenpyLabelEntryPoint renpyLabelEntryPoint:
                            case RenpyMenuInput renpyMenuInput:
                            case RenpyNOP renpyNOP:
                            case RenpyOneLinePython renpyOneLinePython:
                            case RenpyPause renpyPause:
                            case RenpyPlay renpyPlay:
                            case RenpyQueue renpyQueue:
                            case RenpyReturn renpyReturn:
                            case RenpyScene renpyScene:
                            case RenpySetRandomRange renpySetRandomRange:
                            case RenpyShow renpyShow:
                            case RenpyStop renpyStop:
                            case RenpyTime renpyTime:
                            case RenpyUnlock renpyUnlock:
                            case RenpyWindow renpyWindow:
                            case RenpyWith renpyWith:
                            case RenpyStandardProxyLib.Expression expression:
                            case RenpyStandardProxyLib.Text text:
                            case RenpyStandardProxyLib.WindowAuto windowAuto:
                            case RenpyStandardProxyLib.WaitForScreen waitForScreen:
                                if (!seenTypes.Add(line.GetType()))
                                {
                                    Debug.Log("Need to handle " + line.GetType());
                                }
                                break;
                            default:
                                switch (line.GetType().ToString())
                                {
                                    case "RenpyParser.RenpyDialogueLine":
                                        if (!seenTypes.Add(line.GetType()))
                                        {
                                            Debug.Log("Need to handle " + line.GetType());
                                        }
                                        break;
                                    default:
                                        Debug.Log("Unrecognized line type: " + line.GetType());
                                        break;
                                }
                                break;
                        }
                    }
                    --depth;
                }
                //rawBlocks.Add(labelEntry.Key, BuildBlock(labelEntry.Key, labelEntry.Value));
            }
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