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

            UnpickleIndex(pickled);
        }

        void UnpickleIndex(byte[] pickled)
        {
            int offset = 0;
            if (pickled[offset++] != 0x80)
            {
                Debug.LogError("No pickle header");
                return;
            }
            int protocol = pickled[offset++];
            if (protocol != 2)
            {
                Debug.LogError("Need to handle pickle protocol " + protocol + " apparently");
                return;
            }
            Debug.LogError("Next byte is: " + PrettyBytes(pickled, offset, 8));
            return;


            if (pickled[offset++] != '.')
            {
                Debug.LogError("No STOP code at end of pickle");
                return;
            }
            if (offset != pickled.Length)
            {
                Debug.LogWarning("Extra bytes at end of pickle?");
                return;
            }
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

        public bool ok { get; private set; }

        private static Dictionary<char, System.Action<Unpickler>> dispatch = new Dictionary<char, System.Action<Unpickler>>()
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
                    Debug.LogError("Unhandled unpickling case: " + "APPEND");
                    unpickler.ok = false;
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
                    Debug.LogError("Unhandled unpickling case: " + "DICT");
                    unpickler.ok = false;
                }
            },
            { '}', unpickler => // EMPTY_DICT
                {
                    Debug.LogError("Unhandled unpickling case: " + "EMPTY_DICT");
                    unpickler.ok = false;
                }
            },
            { 'e', unpickler => // APPENDS
                {
                    Debug.LogError("Unhandled unpickling case: " + "APPENDS");
                    unpickler.ok = false;
                }
            },
            { 'g', unpickler => // GET
                {
                    Debug.LogError("Unhandled unpickling case: " + "GET");
                    unpickler.ok = false;
                }
            },
            { 'h', unpickler => // BINGET
                {
                    Debug.LogError("Unhandled unpickling case: " + "BINGET");
                    unpickler.ok = false;
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
                    Debug.LogError("Unhandled unpickling case: " + "LONG_BINGET");
                    unpickler.ok = false;
                }
            },
            { 'l', unpickler => // LIST
                {
                    Debug.LogError("Unhandled unpickling case: " + "LIST");
                    unpickler.ok = false;
                }
            },
            { ']', unpickler => // EMPTY_LIST
                {
                    Debug.LogError("Unhandled unpickling case: " + "EMPTY_LIST");
                    unpickler.ok = false;
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
                    Debug.LogError("Unhandled unpickling case: " + "PUT");
                    unpickler.ok = false;
                }
            },
            { 'q', unpickler => // BINPUT
                {
                    Debug.LogError("Unhandled unpickling case: " + "BINPUT");
                    unpickler.ok = false;
                }
            },
            { 'r', unpickler => // LONG_BINPUT
                {
                    Debug.LogError("Unhandled unpickling case: " + "LONG_BINPUT");
                    unpickler.ok = false;
                }
            },
            { 's', unpickler => // SETITEM
                {
                    Debug.LogError("Unhandled unpickling case: " + "SETITEM");
                    unpickler.ok = false;
                }
            },
            { 't', unpickler => // TUPLE
                {
                    Debug.LogError("Unhandled unpickling case: " + "TUPLE");
                    unpickler.ok = false;
                }
            },
            { ')', unpickler => // EMPTY_TUPLE
                {
                    Debug.LogError("Unhandled unpickling case: " + "EMPTY_TUPLE");
                    unpickler.ok = false;
                }
            },
            { 'u', unpickler => // SETITEMS
                {
                    Debug.LogError("Unhandled unpickling case: " + "SETITEMS");
                    unpickler.ok = false;
                }
            },
            { 'G', unpickler => // BINFLOAT
                {
                    Debug.LogError("Unhandled unpickling case: " + "BINFLOAT");
                    unpickler.ok = false;
                }
            },
            { '\x80', unpickler => // PROTO
                {
                    Debug.LogError("Unhandled unpickling case: " + "PROTO");
                    unpickler.ok = false;
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
                    Debug.LogError("Unhandled unpickling case: " + "TUPLE1");
                    unpickler.ok = false;
                }
            },
            { '\x86', unpickler => // TUPLE2
                {
                    Debug.LogError("Unhandled unpickling case: " + "TUPLE2");
                    unpickler.ok = false;
                }
            },
            { '\x87', unpickler => // TUPLE3
                {
                    Debug.LogError("Unhandled unpickling case: " + "TUPLE3");
                    unpickler.ok = false;
                }
            },
            { '\x88', unpickler => // NEWTRUE
                {
                    Debug.LogError("Unhandled unpickling case: " + "NEWTRUE");
                    unpickler.ok = false;
                }
            },
            { '\x89', unpickler => // NEWFALSE
                {
                    Debug.LogError("Unhandled unpickling case: " + "NEWFALSE");
                    unpickler.ok = false;
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

            while (!stop)
            {
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
            long result = 0;
            for (int i = 0; i < bytes; ++i)
            {
                result = (result << 8) + pickled[offset++];
            }
            long signBit = 1 << (8 * bytes - 1);
            if (result >= signBit)
            {
                // sign bit means it should be negative
                result -= signBit * 2;
            }
            return result;
        }
    }

    class PythonObj
    {
        public ObjType Type { get; private set; }
        public bool Bool { get; private set; }
        public float Float { get; private set; }
        public int Int { get; private set; }
        public long Long { get; private set; }
        public string String { get; private set; }
        public List<PythonObj> List { get; private set; }
        public Dictionary<string, PythonObj> Dictionary { get; private set; }

        public PythonObj()
        {
            Type = ObjType.NONE;
        }
        public PythonObj(bool val)
        {
            Type = ObjType.BOOL;
            Bool = val;
        }
        public PythonObj(float val)
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
        public PythonObj(List<PythonObj> val)
        {
            Type = ObjType.LIST;
            List = val;
        }
        public PythonObj(Dictionary<string, PythonObj> val)
        {
            Type = ObjType.DICTIONARY;
            Dictionary = val;
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
        }
    }
}
