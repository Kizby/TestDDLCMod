using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TestDDLCMod
{
    public class RPYCFile
    {
        public bool Ok { get; private set; } = false;
        public Dictionary<string, PythonObj> Labels = new Dictionary<string, PythonObj>();
        public List<PythonObj> Inits = new List<PythonObj>();
        public Dictionary<string, Dictionary<string, PythonObj>> Stores = new Dictionary<string, Dictionary<string, PythonObj>>();

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
            Debug.Log(stmts);
            foreach (var stmt in stmts.List)
            {
                HandleStatement(stmt);
            }
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

        private int depth = 0;
        private void Log(string s)
        {
            Debug.Log(new string(' ', depth) + s);
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
            foreach(var statement in block)
            {
                HandleStatement(statement);
            }
            --depth;
        }


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
                        HandleBlock(stmt);
                    }
                },
                {
                    "renpy.ast.Init", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        // block, priority
                        Inits.Add(stmt);
                        HandleBlock(stmt);
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
                    "renpy.ast.Define", stmt =>
                    {
                        handlers["renpy.ast.Node"](stmt);
                        // varname, code, store
                        var store = stmt.Fields["store"].String;
                        if (!Stores.ContainsKey(store))
                        {
                            Stores.Add(store, new Dictionary<string, PythonObj>());
                        }
                        var name = stmt.Fields["varname"].String;
                        var code = stmt.Fields["code"];
                        Log("Defining " + store + ": " + name);
                        Stores[store][name] = code;
                    }
                },
            };
        }
    }
}
