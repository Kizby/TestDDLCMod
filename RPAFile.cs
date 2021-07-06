using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TestDDLCMod
{
    public class RPAFile
    {
        private Stream stream;
        private long indexOffset;
        private int key;

        public readonly bool Ok = false;

        public RPAFile(Stream stream)
        {
            this.stream = stream;
            if (!Expect("RPA-3.0 ")) return;

            indexOffset = long.Parse(GetString(16), NumberStyles.HexNumber);
            Debug.Log("index offset is " + indexOffset);
            if (!Expect(" ")) return;

            key = int.Parse(GetString(8), NumberStyles.HexNumber);
            Debug.Log("key is " + key);

            ParseIndex();
        }

        void ParseIndex()
        {
            stream.Position = indexOffset + 2;
            var indexBytes = new byte[stream.Length - stream.Position];
            stream.Read(indexBytes, 0, indexBytes.Length);
            Debug.Log("indexBytes start with: " + indexBytes[0] + ", " + indexBytes[1] + ", " + indexBytes[2]);

            int decompressedLength = 0;
            using (var memStream = new MemoryStream(indexBytes))
            {
                var zipBytes = new byte[1000];
                using (var zipStream = new DeflateStream(memStream, CompressionMode.Decompress))
                {
                    int read;
                    do
                    {
                        read = zipStream.Read(zipBytes, 0, zipBytes.Length);
                        if (read > 0)
                        {
                            decompressedLength += read;
                        }
                    } while (read > 0);
                }
            }

            byte[] pickled = new byte[decompressedLength];
            using (var memStream = new MemoryStream(indexBytes))
            {
                using (var zipStream = new DeflateStream(memStream, CompressionMode.Decompress))
                {
                    zipStream.Read(pickled, 0, pickled.Length);
                }
            }

            var pythonObj = Unpickler.Unpickle(pickled);
            Debug.Log("Successfully unpickled! Type is: " + pythonObj.Type);
        }

        bool Expect(string s)
        {
            byte[] bytes = new byte[s.Length];
            stream.Read(bytes, 0, bytes.Length);
            for (var i = 0; i < bytes.Length; ++i)
            {
                if (bytes[i] != s[i])
                {
                    return false;
                }
            }
            return true;
        }

        string GetString(int count)
        {
            byte[] bytes = new byte[count];
            stream.Read(bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }

        string PrettyBytes(byte[] source, int offset, int length)
        {
            StringBuilder result = new StringBuilder(length * 3);
            for (int i = offset; i < offset + length; ++i)
            {
                result.AppendFormat("{0:x2} ", source[i]);
            }
            return result.ToString();
        }
    }

    class Unpickler
    {
        private byte[] pickled;
        private int offset;
        private bool stop;
        private Stack<PythonObj> stack;
        private PythonObj mark = new PythonObj();
        private Dictionary<string, PythonObj> memo = new Dictionary<string, PythonObj>();

        public bool ok { get; private set; }

        private static Dictionary<char, Action<Unpickler>> dispatch = new Dictionary<char, Action<Unpickler>>()
        {
            { '(', unpickler => // MARK
                {
                    unpickler.stack.Push(unpickler.mark);
                }
            },
            { '.', unpickler => // STOP
                {
                    unpickler.stop = true;
                }
            },
            { '0', unpickler => // POP
                {
                    unpickler.stack.Pop();
                }
            },
            { '1', unpickler => // POP_MARK
                {
                    while(unpickler.stack.Pop() != unpickler.mark) ;
                }
            },
            { '2', unpickler => // DUP
                {
                    unpickler.stack.Push(unpickler.stack.Peek());
                }
            },
            { 'F', unpickler => // FLOAT
                {
                    unpickler.stack.Push(new PythonObj(float.Parse(unpickler.ReadLine())));
                }
            },
            { 'I', unpickler => // INT
                {
                    var val = unpickler.ReadLine();
                    if (val == "00")
                    {
                        unpickler.stack.Push(new PythonObj(false));
                    } else if (val == "01")
                    {
                        unpickler.stack.Push(new PythonObj(true));
                    } else
                    {
                        long num = long.Parse(val);
                        if (num <= int.MaxValue)
                        {
                            unpickler.stack.Push(new PythonObj((int)num));
                        } else
                        {
                            unpickler.stack.Push(new PythonObj(num));
                        }
                    }
                }
            },
            { 'J', unpickler => // BININT
                {
                    unpickler.stack.Push(new PythonObj((int)unpickler.ParseInt(4)));
                }
            },
            { 'K', unpickler => // BININT1
                {
                    unpickler.stack.Push(new PythonObj((int)unpickler.ParseInt(1)));
                }
            },
            { 'L', unpickler => // LONG
                {
                    var val = unpickler.ReadLine();
                    long num = 0;
                    if (val != "")
                    {
                        num = long.Parse(val);
                    }
                    unpickler.stack.Push(new PythonObj(num));
                }
            },
            { 'M', unpickler => // BININT2
                {
                    unpickler.stack.Push(new PythonObj((int)unpickler.ParseInt(2)));
                }
            },
            { 'N', unpickler => // NONE
                {
                    unpickler.stack.Push(new PythonObj());
                }
            },
            { 'P', unpickler => // PERSID
                {
                    Debug.LogError("Unhandled unpickling case: " + "PERSID");
                    unpickler.ok = false;
                }
            },
            { 'Q', unpickler => // BINPERSID
                {
                    Debug.LogError("Unhandled unpickling case: " + "BINPERSID");
                    unpickler.ok = false;
                }
            },
            { 'R', unpickler => // REDUCE
                {
                    Debug.LogError("Unhandled unpickling case: " + "REDUCE");
                    unpickler.ok = false;
                }
            },
            { 'S', unpickler => // STRING
                {
                    var val = unpickler.ReadLine();
                    Debug.Log("Encountered raw string: " + val);
                    var unescaped = Regex.Unescape(val.Substring(1, val.Length - 2));
                    Debug.Log("Unescaped as: " + unescaped);
                    unpickler.stack.Push(new PythonObj(unescaped));
                }
            },
            { 'T', unpickler => // BINSTRING
                {
                    var len = unpickler.ParseInt(4);
                    var bytes = new byte[len];
                    Array.Copy(unpickler.pickled, unpickler.offset, bytes, 0, bytes.Length);
                    unpickler.offset += bytes.Length;
                    unpickler.stack.Push(new PythonObj(Encoding.Default.GetString(bytes)));
                }
            },
            { 'U', unpickler => // SHORT_BINSTRING
                {
                    var len = unpickler.ParseInt(1);
                    var bytes = new byte[len];
                    Array.Copy(unpickler.pickled, unpickler.offset, bytes, 0, bytes.Length);
                    unpickler.offset += bytes.Length;
                    unpickler.stack.Push(new PythonObj(Encoding.Default.GetString(bytes)));
                }
            },
            { 'V', unpickler => // UNICODE
                {
                    var val = unpickler.ReadLine();
                    Debug.Log("Encountered raw unicode string: " + val);
                    var unescaped = Regex.Unescape(val);
                    Debug.Log("Unescaped as: " + unescaped);
                    unpickler.stack.Push(new PythonObj(unescaped));
                }
            },
            { 'X', unpickler => // BINUNICODE
                {
                    var len = unpickler.ParseInt(4);
                    var bytes = new byte[len];
                    Array.Copy(unpickler.pickled, unpickler.offset, bytes, 0, bytes.Length);
                    unpickler.offset += bytes.Length;
                    unpickler.stack.Push(new PythonObj(Encoding.UTF8.GetString(bytes)));
                }
            },
            { 'a', unpickler => // APPEND
                {
                    var val = unpickler.stack.Pop();
                    unpickler.stack.Peek().List.Add(val);
                }
            },
            { 'b', unpickler => // BUILD
                {
                    Debug.LogError("Unhandled unpickling case: " + "BUILD");
                    unpickler.ok = false;
                }
            },
            { 'c', unpickler => // GLOBAL
                {
                    Debug.LogError("Unhandled unpickling case: " + "GLOBAL");
                    unpickler.ok = false;
                }
            },
            { 'd', unpickler => // DICT
                {
                    var dict = new Dictionary<PythonObj, PythonObj>();
                    while (unpickler.stack.Peek() != unpickler.mark)
                    {
                        var val = unpickler.stack.Pop();
                        var key = unpickler.stack.Pop();
                        dict[key] = val;
                    }
                    unpickler.stack.Pop(); // pop the mark
                    unpickler.stack.Push(new PythonObj(dict));
                }
            },
            { '}', unpickler => // EMPTY_DICT
                {
                    unpickler.stack.Push(new PythonObj(new Dictionary<PythonObj, PythonObj>()));
                }
            },
            { 'e', unpickler => // APPENDS
                {
                    var toAppend = new List<PythonObj>();
                    while (unpickler.stack.Peek() != unpickler.mark)
                    {
                        toAppend.Add(unpickler.stack.Pop());
                    }
                    toAppend.Reverse(); // took them off the stack in reverse order, so fix it
                    unpickler.stack.Pop(); // pop the mark
                    unpickler.stack.Peek().List.AddRange(toAppend);
                }
            },
            { 'g', unpickler => // GET
                {
                    var line = unpickler.ReadLine();
                    unpickler.stack.Push(unpickler.memo[line]);
                }
            },
            { 'h', unpickler => // BINGET
                {
                    var index = unpickler.ParseInt(1);
                    unpickler.stack.Push(unpickler.memo[index.ToString()]);
                }
            },
            { 'i', unpickler => // INST
                {
                    Debug.LogError("Unhandled unpickling case: " + "INST");
                    unpickler.ok = false;
                }
            },
            { 'j', unpickler => // LONG_BINGET
                {
                    var index = unpickler.ParseInt(4);
                    unpickler.stack.Push(unpickler.memo[index.ToString()]);
                }
            },
            { 'l', unpickler => // LIST
                {
                    var list = new List<PythonObj>();
                    while (unpickler.stack.Peek() != unpickler.mark)
                    {
                        list.Add(unpickler.stack.Pop());
                    }
                    list.Reverse(); // took them off the stack in reverse order, so fix it
                    unpickler.stack.Pop(); // pop the mark
                    unpickler.stack.Push(new PythonObj(list));
                }
            },
            { ']', unpickler => // EMPTY_LIST
                {
                    unpickler.stack.Push(new PythonObj(new List<PythonObj>()));
                }
            },
            { 'o', unpickler => // OBJ
                {
                    Debug.LogError("Unhandled unpickling case: " + "OBJ");
                    unpickler.ok = false;
                }
            },
            { 'p', unpickler => // PUT
                {
                    unpickler.memo[unpickler.ReadLine()] = unpickler.stack.Peek();
                }
            },
            { 'q', unpickler => // BINPUT
                {
                    unpickler.memo[unpickler.ParseInt(1).ToString()] = unpickler.stack.Peek();
                }
            },
            { 'r', unpickler => // LONG_BINPUT
                {
                    unpickler.memo[unpickler.ParseInt(4).ToString()] = unpickler.stack.Peek();
                }
            },
            { 's', unpickler => // SETITEM
                {
                    var val = unpickler.stack.Pop();
                    var key = unpickler.stack.Pop();
                    unpickler.stack.Peek().Dictionary[key] = val;
                }
            },
            { 't', unpickler => // TUPLE
                {
                    var list = new List<PythonObj>();
                    while (unpickler.stack.Peek() != unpickler.mark)
                    {
                        list.Add(unpickler.stack.Pop());
                    }
                    list.Reverse(); // took them off the stack in reverse order, so fix it
                    unpickler.stack.Pop(); // pop the mark
                    unpickler.stack.Push(new PythonObj(list, true));
                }
            },
            { ')', unpickler => // EMPTY_TUPLE
                {
                    unpickler.stack.Push(new PythonObj(new List<PythonObj>(), true));
                }
            },
            { 'u', unpickler => // SETITEMS
                {
                    var toInsert = new Dictionary<PythonObj, PythonObj>();
                    while (unpickler.stack.Peek() != unpickler.mark)
                    {
                        var val = unpickler.stack.Pop();
                        var key = unpickler.stack.Pop();
                        toInsert[key] = val;
                    }
                    unpickler.stack.Pop(); // pop the mark
                    var dict = unpickler.stack.Peek().Dictionary;
                    foreach (var entry in toInsert) {
                        dict[entry.Key] = entry.Value;
                    }
                }
            },
            { 'G', unpickler => // BINFLOAT
                {
                    unpickler.stack.Push(new PythonObj(BitConverter.ToDouble(unpickler.pickled, unpickler.offset)));
                    unpickler.offset += 8;
                }
            },
            { '\x80', unpickler => // PROTO
                {
                    var val = unpickler.pickled[unpickler.offset++];
                    if (val != 2)
                    {
                        Debug.LogError("Apparently need to support pickle protocol " + val);
                        unpickler.ok = false;
                    }
                }
            },
            { '\x81', unpickler => // NEWOBJ
                {
                    Debug.LogError("Unhandled unpickling case: " + "NEWOBJ");
                    unpickler.ok = false;
                }
            },
            { '\x82', unpickler => // EXT1
                {
                    Debug.LogError("Unhandled unpickling case: " + "EXT1");
                    unpickler.ok = false;
                }
            },
            { '\x83', unpickler => // EXT2
                {
                    Debug.LogError("Unhandled unpickling case: " + "EXT2");
                    unpickler.ok = false;
                }
            },
            { '\x84', unpickler => // EXT4
                {
                    Debug.LogError("Unhandled unpickling case: " + "EXT4");
                    unpickler.ok = false;
                }
            },
            { '\x85', unpickler => // TUPLE1
                {
                    var list = new List<PythonObj>();
                    list.Add(unpickler.stack.Pop());
                    unpickler.stack.Push(new PythonObj(list, true));
                }
            },
            { '\x86', unpickler => // TUPLE2
                {
                    var list = new List<PythonObj>();
                    list.Add(unpickler.stack.Pop());
                    list.Add(unpickler.stack.Pop());
                    list.Reverse();
                    unpickler.stack.Push(new PythonObj(list, true));
                }
            },
            { '\x87', unpickler => // TUPLE3
                {
                    var list = new List<PythonObj>();
                    list.Add(unpickler.stack.Pop());
                    list.Add(unpickler.stack.Pop());
                    list.Add(unpickler.stack.Pop());
                    list.Reverse();
                    unpickler.stack.Push(new PythonObj(list, true));
                }
            },
            { '\x88', unpickler => // NEWTRUE
                {
                    unpickler.stack.Push(new PythonObj(true));
                }
            },
            { '\x89', unpickler => // NEWFALSE
                {
                    unpickler.stack.Push(new PythonObj(false));
                }
            },
            { '\x8a', unpickler => // LONG1
                {
                    Debug.LogError("Unhandled unpickling case: " + "LONG1");
                    unpickler.ok = false;
                }
            },
            { '\x8b', unpickler => // LONG4
                {
                    Debug.LogError("Unhandled unpickling case: " + "LONG4");
                    unpickler.ok = false;
                }
            },
        };

        public static PythonObj Unpickle(byte[] pickled)
        {
            var state = new Unpickler(pickled);
            if (!state.ok || state.stack.Count == 0)
            {
                Debug.LogError("Failed to unpickle");
                return null;
            }
            if (state.offset != pickled.Length)
            {
                Debug.LogWarning(pickled.Length - state.offset + " extra bytes at the end of the pickled data?");
            }
            return state.stack.Pop();
        }

        private Unpickler(byte[] pickled)
        {
            this.pickled = pickled;
            offset = 0;
            ok = true;
            stop = false;
            stack = new Stack<PythonObj>();

            while (ok && !stop)
            {
                Debug.Log("Opcode: " + (char)pickled[offset]);
                dispatch[(char)pickled[offset++]](this);
            }
        }

        private string ReadLine()
        {
            string line = "";
            while ((char)pickled[offset++] != '\n')
            {
                line += (char)pickled[offset - 1];
            }
            return line;
        }

        private long ParseInt(int bytes)
        {
            long result;
            if (bytes == 8)
            {
                result = BitConverter.ToInt64(pickled, offset);
            }
            else if (bytes == 4)
            {
                result = BitConverter.ToInt32(pickled, offset);
            }
            else if (bytes == 2)
            {
                result = BitConverter.ToInt16(pickled, offset);
            }
            else if (bytes == 1)
            {
                result = pickled[offset];
            }
            else
            {
                Debug.LogError("Unhandled byte length in ParseInt!");
                ok = false;
                return 0;
            }
            offset += bytes;
            Debug.Log("Parsed an int: " + result);
            return result;
        }

        private byte[] GetBytes(int count)
        {
            byte[] bytes = new byte[count];
            Array.Copy(pickled, offset, bytes, 0, count);
            offset += count;
            return bytes;
        }
    }

    class PythonObj
    {
        public ObjType Type { get; private set; }
        public bool Bool { get; private set; }
        public double Float { get; private set; }
        public int Int { get; private set; }
        public long Long { get; private set; }
        public string String { get; private set; }
        public List<PythonObj> List { get; private set; }
        public Dictionary<PythonObj, PythonObj> Dictionary { get; private set; }
        public List<PythonObj> Tuple => List;

        public PythonObj()
        {
            Type = ObjType.NONE;
        }
        public PythonObj(bool val)
        {
            Type = ObjType.BOOL;
            Bool = val;
        }
        public PythonObj(double val)
        {
            Type = ObjType.FLOAT;
            Float = val;
        }
        public PythonObj(int val)
        {
            Type = ObjType.INT;
            Int = val;
        }
        public PythonObj(long val)
        {
            Type = ObjType.LONG;
            Long = val;
        }
        public PythonObj(string val)
        {
            Type = ObjType.STRING;
            String = val;
        }
        // tuples are immutable, so are worth distinguishing
        public PythonObj(List<PythonObj> val, bool tuple = false)
        {
            Type = tuple ? ObjType.TUPLE : ObjType.LIST;
            List = val;
        }
        public PythonObj(Dictionary<PythonObj, PythonObj> val)
        {
            Type = ObjType.DICTIONARY;
            Dictionary = val;
        }

        public override bool Equals(object obj)
        {
            if (obj is PythonObj pobj)
            {
                if (Type != pobj.Type)
                {
                    return false;
                }
                switch (Type)
                {
                    case ObjType.BOOL: return Bool == pobj.Bool;
                    case ObjType.FLOAT: return Float == pobj.Float;
                    case ObjType.INT: return Int == pobj.Int;
                    case ObjType.LONG: return Long == pobj.Long;
                    case ObjType.STRING: return String == pobj.String;
                    case ObjType.LIST: return List == pobj.List;
                    case ObjType.DICTIONARY: return Dictionary == pobj.Dictionary;
                    case ObjType.TUPLE:
                        // tuples are immutable, so compare their contents
                        if (List.Count != pobj.List.Count)
                        {
                            return false;
                        }
                        for (var i = 0; i < List.Count; ++i)
                        {
                            if (!List[i].Equals(pobj.List[i]))
                            {
                                return false;
                            }
                        }
                        return true;
                }
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            int hashCode = -1631622129;
            hashCode = hashCode * -1521134295 + Type.GetHashCode();
            hashCode = hashCode * -1521134295 + Bool.GetHashCode();
            hashCode = hashCode * -1521134295 + Float.GetHashCode();
            hashCode = hashCode * -1521134295 + Int.GetHashCode();
            hashCode = hashCode * -1521134295 + Long.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(String);
            hashCode = hashCode * -1521134295 + EqualityComparer<List<PythonObj>>.Default.GetHashCode(List);
            hashCode = hashCode * -1521134295 + EqualityComparer<Dictionary<PythonObj, PythonObj>>.Default.GetHashCode(Dictionary);
            hashCode = hashCode * -1521134295 + EqualityComparer<List<PythonObj>>.Default.GetHashCode(Tuple);
            return hashCode;
        }

        public enum ObjType
        {
            NONE,
            BOOL,
            FLOAT,
            INT,
            LONG,
            STRING,
            LIST,
            DICTIONARY,
            TUPLE,
        }
    }
}
