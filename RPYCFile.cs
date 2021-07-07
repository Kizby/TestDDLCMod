using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TestDDLCMod
{
    public class RPYCFile
    {
        public bool Ok { get; private set; } = false;
        public Dictionary<string, PythonObj> Labels = new Dictionary<string, PythonObj>();
        public List<PythonObj> EarlyPython = new List<PythonObj>();
        public SortedDictionary<int, List<PythonObj>> Inits = new SortedDictionary<int, List<PythonObj>>();

        public Dictionary<string, Dictionary<string, string>> Stores = new Dictionary<string, Dictionary<string, string>>();
        public Dictionary<string, Dictionary<string, string>> Defaults = new Dictionary<string, Dictionary<string, string>>();
        public Dictionary<string, Dictionary<string, string>> Styles = new Dictionary<string, Dictionary<string, string>>();

        public RPYCFile(byte[] fileBytes)
        {
            string magic = "RENPY RPC2";
            if (Encoding.ASCII.GetString(fileBytes, 0, magic.Length) != magic)
            {
                Debug.LogError("Wrong magic in rpyc");
                return;
            }

            // try to get statements after static transforms
            var slotAST = GetSlotAST(fileBytes, magic.Length, 2);
            if (null == slotAST)
            {
                // if they're not there, grab the ones before static transforms
                slotAST = GetSlotAST(fileBytes, magic.Length, 1);
            }

            InitHandlers();

            var stmts = slotAST.Tuple[1];
            //Debug.Log(stmts);
            foreach (var stmt in stmts.List)
            {
                switch (stmt.Name)
                {
                    case "renpy.ast.Label":
                        Labels.Add(stmt.Fields["name"].String, stmt);
                        break;
                    case "renpy.ast.Init":
                        var priority = stmt.Fields["priority"].ToInt();
                        if (!Inits.ContainsKey(priority))
                        {
                            Inits[priority] = new List<PythonObj>();
                        }
                        Inits[priority].Add(stmt);
                        break;
                    case "renpy.ast.EarlyPython":
                        EarlyPython.Add(stmt);
                        break;
                    case "renpy.ast.Return":
                        break;
                    default:
                        Debug.LogWarning("Unexpected top level statement: " + stmt.Name);
                        break;
                }
                //HandleStatement(stmt);
            }
            Ok = true;
        }

        PythonObj GetSlotAST(byte[] bytes, int offset, int slot)
        {
            offset += (slot - 1) * 12;
            if (slot != BitConverter.ToInt32(bytes, offset))
            {
                Debug.LogError("Wrong slot in rpyc?");
                return null;
            }
            var start = BitConverter.ToInt32(bytes, offset + 4);
            var length = BitConverter.ToInt32(bytes, offset + 8);
            if (length == 0)
            {
                return null;
            }

            return Unpickler.UnpickleZlibBytes(bytes, start, length);
        }

        private int depth = "[Info   : Unity Log] ".Length;
        private void Log(string s)
        {
            Debug.Log(new string(' ', depth - "[Info   : Unity Log] ".Length) + s);
        }

        private void HandleStatement(PythonObj statement)
        {
            var name = statement.Name;
            if (!handlers.ContainsKey(name))
            {
                Log("Unhandled class: " + name);
            }
            else
            {
                handlers[name](statement);
            }
        }

        private void HandleBlock(PythonObj container)
        {
            ++depth;
            var block = container.Fields["block"].List;
            foreach (var statement in block)
            {
                HandleStatement(statement);
            }
            --depth;
        }

        private string ExtractPyExpr(PythonObj expr)
        {
            if (expr.Name == "renpy.ast.PyExpr")
            {
                return expr.Args.Tuple[0].String;
            }
            else if (expr.Name == "renpy.ast.PyCode")
            {
                var source = expr.Fields["source"];
                if (source.Type == PythonObj.ObjType.STRING)
                {
                    return source.String;
                }
                return ExtractPyExpr(expr.Fields["source"]);
            }
            Debug.LogError("Trying to extractPyExpr on a " + expr.Name);
            return "";
        }
        private string ExtractStore(PythonObj stmt)
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

        private string Indent(string s) => s.Replace("\n", "\n" + new string(' ', depth + 1));
        private bool inInit = false;

        Dictionary<string, Action<PythonObj>> handlers = null;
        private void InitHandlers()
        {
            handlers = new Dictionary<string, Action<PythonObj>>()
            {
                {
                    "renpy.ast.Node", stmt =>
                    {
                        // name, filename, linenumber, next
                    }
                },
                {
                    "renpy.ast.Label", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        // name, parameters, block, hide
                        var name = stmt.Fields["name"].String;
                        Log("label " + name + ":");
                        Labels[name] = stmt;
                        //HandleBlock(stmt);
                    }
                },
                {
                    "renpy.ast.Init", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        // block, priority
                        var priority = stmt.Fields["priority"].ToInt();
                        if (!Inits.ContainsKey(priority))
                        {
                            Inits[priority] = new List<PythonObj>();
                        }
                        Inits[priority].Add(stmt);
                        Log("init");
                        inInit = true;
                        //HandleBlock(stmt);
                        inInit = false;
                    }
                },
                {
                    "renpy.ast.Translate", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        // identifier, language, block
                        Log("translate");
                        HandleBlock(stmt);
                    }
                },
                {
                    "renpy.ast.EndTranslate", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        Log("end translate");
                    }
                },
                {
                    "renpy.ast.Define", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        // varname, code, store
                        var store = ExtractStore(stmt);
                        if (!Stores.ContainsKey(store))
                        {
                            Stores.Add(store, new Dictionary<string, string>());
                        }
                        var name = stmt.Fields["varname"].String;
                        var fullname = store == "" ? name : store + "." + name;
                        var code = ExtractPyExpr(stmt.Fields["code"]);
                        Log("define " + fullname + " = " + Indent(code));
                        Stores[store][name] = code;
                    }
                },
                {
                    "renpy.ast.Style", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        // style_name, parent, properties, clear, take, delattr, variant
                        var name = stmt.Fields["style_name"];
                        Dictionary<string, string> properties = new Dictionary<string, string>();
                        foreach (var entry in stmt.Fields["properties"].Dictionary)
                        {
                            // seems to always be a one-line PyExpr
                            if (entry.Value.Name != "renpy.ast.PyExpr")
                            {
                                Debug.Log("Non-pyexpr style property? " + name + ":" + entry.Key.String);
                            } else
                            {

                                properties[entry.Key.String] = ExtractPyExpr(entry.Value);
                            }
                        }
                        Log("style: " + string.Join(", ", properties.Select(entry => entry.Key + "=$(" + Indent(entry.Value) + ")")));
                    }
                },
                {
                    "renpy.ast.Default", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        var store = ExtractStore(stmt);
                        if (!Defaults.ContainsKey(store))
                        {
                            Defaults.Add(store, new Dictionary<string, string>());
                        }
                        var name = stmt.Fields["varname"].String;
                        var fullname = store == "" ? name : store + "." + name;
                        var code = ExtractPyExpr(stmt.Fields["code"]);
                        Log("default " + fullname + " = " + Indent(code));
                        Defaults[store][name] = code;
                    }
                },
                {
                    "renpy.ast.Python", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        var store = ExtractStore(stmt);
                        if (store != "")
                        {
                            Debug.LogWarning("Found Python executed not in store? In " + store + " instead.");
                        }
                        var code = ExtractPyExpr(stmt.Fields["code"]);
                        if (inInit)
                        {
                            Log("init python:");
                            depth += 1;
                            Log(Indent(code));
                            depth -= 1;
                        }
                        else
                        {
                            if (!code.Contains('\n'))
                            {
                                Log("$ " + code);
                            }
                            else
                            {
                                Log("python:");
                                depth += 1;
                                Log(Indent(code));
                                depth -= 1;
                            }
                        }
                    }
                },
                {
                    "renpy.ast.EarlyPython", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        var store = ExtractStore(stmt);
                        if (store != "")
                        {
                            Debug.LogWarning("Found early Python executed not in store? In " + store + " instead.");
                        }
                        var code = ExtractPyExpr(stmt.Fields["code"]);
                        Log("python early:");
                        depth += 1;
                        Log(Indent(code));
                        depth -= 1;
                    }
                },
                {
                    "renpy.ast.Return", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        Log("return");
                    }
                },
                {
                    "renpy.ast.While", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        var condition = ExtractPyExpr(stmt.Fields["condition"]);
                        Log("while " + condition + ":");
                        HandleBlock(stmt);
                    }
                }
            };
        }
    }
}
