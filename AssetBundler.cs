using HarmonyLib;
using RenpyLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityPS;

namespace TestDDLCMod
{
    class AssetBundler
    {
        public static void Bundle(string name, Dictionary<string, string> assets)
        {
            Debug.Log("Bundling: " + name);
            foreach (var asset in assets)
            {
                Debug.Log(asset.Key + ":" + asset.Value);
            }
            Debug.Log("----------------------");
        }

        public static void Unbundle(string path)
        {
            var unbundledPath = path + ".bundle";
            if (!File.Exists(unbundledPath))
            {
                using (var stream = new XorFileStream(path, FileMode.Open, FileAccess.Read))
                {
                    using (var unbundled = new FileStream(unbundledPath, FileMode.Create))
                    {
                        byte[] buffer = new byte[32768];
                        while (true)
                        {
                            int read = stream.Read(buffer, 0, buffer.Length);
                            if (read <= 0)
                            {
                                break;
                            }
                            unbundled.Write(buffer, 0, read);
                        }
                    }
                }
            }
            var filename = Path.GetFileNameWithoutExtension(unbundledPath);
            //Debug.Log("Parsing " + filename);
            if (filename != "yuri_eyesfull.cy")
            {
                return;
            }
            using (var stream = new FileStream(unbundledPath, FileMode.Open))
            {
                /*var decoder = new LZ4Decoder();
                for (long i = 0; i < stream.Length; ++i)
                {
                    //Debug.Log($"{i,5:X}");
                    List<long> bestMatch = null;
                    try
                    {
                        stream.Position = i;
                        foreach (var matchLength in decoder.ValidDecodes(stream))
                        {
                            if (matchLength[0] > 15)
                            {
                                bestMatch = matchLength;
                            }
                        }
                    }
                    catch (Exception) { }
                    if (bestMatch != null)
                    {
                        Debug.Log($"{i,5:X}: Match of length {bestMatch[0]:X}, {bestMatch[1]:X}");
                    }
                }*/
                stream.Position = 0;
                try
                {
                    new AssetParser(stream).Parse(filename);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e.ToString());
                }
            }
            Application.Quit();
        }
    }

    [HarmonyPatch(typeof(LauncherMain), "WantsToQuit")]
    class Quitter
    {
        static bool Prefix(ref bool __result)
        {
            __result = true;
            return false;
        }
    }
    [HarmonyPatch(typeof(RenpyMainBase), "WantsToQuit")]
    class Quitter2
    {
        static bool Prefix(ref bool __result)
        {
            __result = true;
            return false;
        }
    }

    class AssetParser
    {
        private Stream stream;
        private bool bigEndian = true;
        private StorageBlockSpec[] blocks = null;
        private NodeSpec[] nodes = null;
        private long headerSize;
        public AssetParser(Stream stream)
        {
            this.stream = stream;
        }
        public void Parse(string filename)
        {
            Expect("UnityFS");  // BundleType.UnityFS
            Expect(7); // version
            Expect("5.x.x"); // UnityWebBundleVersion
            Expect("2019.4.20f1"); // engineVersion
            Expect(stream.Length);
            var compressedSize = GetInt(); // CompressedBlocksInfoSize
            Debug.Log($"Compressed blockinfo size: {compressedSize:X}");
            var blocksInfoSize = GetInt(); // UncompressedBlocksInfoSize
            Debug.Log($"Uncompressed blockinfo size: {blocksInfoSize:X}");
            Expect(0x43); // BundleFlags (BlockAndDirectoryInfoCombined + CompressionTypeMask=3[Lz4HC])
            while (stream.Position % 16 != 0)
            {
                Expect((byte)0);
            }

            byte[] blockInfo = new byte[blocksInfoSize];
            LZ4Decoder.Decode(stream, compressedSize, blockInfo);
            DumpBytes(blockInfo);
            headerSize = stream.Position;

            Stream outerStream = stream;
            using (stream = new MemoryStream(blockInfo))
            {
                int[] hash = new int[4];
                GetIntArray(hash);
                Debug.Log($"Hash:{hash[0],8:X}{hash[1],8:X}{hash[2],8:X}{hash[3],8:X}".Replace(' ', '0'));
                int blockArraySize = GetInt();
                Debug.Log($"Block array size: {blockArraySize}");

                blocks = new StorageBlockSpec[blockArraySize];
                long totalCompressed = 0;
                long totalUncompressed = 0;
                for (var i = 0; i < blockArraySize; ++i)
                {
                    blocks[i].fileOffset = totalCompressed + headerSize;
                    blocks[i].offset = totalUncompressed;
                    blocks[i].uncompressed = GetInt();
                    blocks[i].compressed = GetInt();
                    blocks[i].flags = GetShort();
                    Debug.Log($"Block {i}: {blocks[i]}");

                    totalCompressed += blocks[i].compressed;
                    totalUncompressed += blocks[i].uncompressed;
                }
                if (totalCompressed + headerSize != outerStream.Length)
                {
                    Debug.Log($"{outerStream.Length - totalCompressed - headerSize} extra bytes at the end of the bundle!");
                }

                int nodeArraySize = GetInt();
                Debug.Log($"Node array size is: {nodeArraySize}");
                nodes = new NodeSpec[nodeArraySize];
                for (var i = 0; i < nodeArraySize; ++i)
                {
                    nodes[i].offset = GetLong();
                    nodes[i].size = GetLong();
                    nodes[i].index = GetInt();
                    nodes[i].pathOrigin = GetString();
                    Debug.Log($"Node: {nodes[i]}");
                }
                if (stream.Position != stream.Length)
                {
                    Debug.Log("Leftover bytes at end of blockinfo?");
                }
            }
            stream = outerStream;

            //for each Node
            // offset is into decoded file
            foreach (var node in nodes)
            {
                Debug.Log($"Node {node.pathOrigin}:");
                var startBlock = blocks.Length;
                for (var i = 0; i < blocks.Length; ++i)
                {
                    if (blocks[i].offset == node.offset)
                    {
                        startBlock = i;
                        break;
                    }
                    else if (blocks[i].offset > node.offset)
                    {
                        startBlock = i - 1;
                        break;
                    }
                }

                var endBlock = startBlock + 1;
                for (var i = startBlock; i < blocks.Length; ++i)
                {
                    if (blocks[i].offset - blocks[startBlock].offset >= node.size)
                    {
                        endBlock = i;
                    }
                }

                Debug.Log($"startBlock: {startBlock}, endBlock: {endBlock}");
                if (startBlock >= blocks.Length || endBlock < startBlock) {
                    Debug.LogError($"wtf, weird node offset, size: {node.offset}, {node.size}");
                }

                var nodeBytes = new byte[node.size];
                {
                    var nodeBytesPos = 0;
                    var nodeBytesLeft = nodeBytes.Length;
                    var blockPos = (int)(node.offset - blocks[startBlock].offset);
                    stream.Position = blocks[startBlock].fileOffset;
                    for (var i = startBlock; i < endBlock; ++i)
                    {
                        var blockBytes = new byte[blocks[i].uncompressed];
                        LZ4Decoder.Decode(stream, blocks[i].compressed, blockBytes);
                        var toCopy = Math.Min(blockBytes.Length - blockPos, nodeBytesLeft);
                        Array.Copy(blockBytes, blockPos, nodeBytes, nodeBytesPos, blockBytes.Length - blockPos);
                        blockPos = 0; // copying from the start of the next block
                        nodeBytesLeft -= toCopy;
                    }
                }
                DumpBytes(nodeBytes);
            }
        }

        private static void DumpBytes(byte[] blockInfo)
        {
            blockInfo.Select((b, i) => Tuple.Create(b, i / 16))
                            .GroupBy(t => t.Item2)
                            .Select(g => g.Select(t => t.Item1))
                            .Select(g => g.Join(b => $"{b,2:X}"
                                          .Replace(' ', '0'), " ") +
                                         new string(' ', (16 - g.Count()) * 3) +
                                         " : " +
                                         g.Select(b => (b < 32 || b > 126) ? "?" : $"{(char)b}")
                                          .Join(delimiter: ""))
                            .Do(Debug.Log);
        }

        struct StorageBlockSpec
        {
            public int uncompressed;
            public int compressed;
            public short flags;
            public long offset;
            public long fileOffset;
            public override string ToString() => $"uncompressed:{uncompressed:X}, offset:{offset:X}, compressed:{compressed:X}, fileOffset:{fileOffset:X}, flags:{flags:X}";
        }
        struct NodeSpec
        {
            public long offset;
            public long size;
            public int index;
            public string pathOrigin;
            public override string ToString() => $"offset:{offset:X}, size:{size:X}, index:{index}, pathOrigin:{pathOrigin}";
        }

        long GetLong()
        {
            var bytes = new byte[8];
            stream.Read(bytes, 0, bytes.Length);
            if (bigEndian) Array.Reverse(bytes);
            return BitConverter.ToInt64(bytes, 0);
        }
        int GetInt()
        {
            var bytes = new byte[4];
            stream.Read(bytes, 0, bytes.Length);
            if (bigEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
        short GetShort()
        {
            var bytes = new byte[2];
            stream.Read(bytes, 0, bytes.Length);
            if (bigEndian) Array.Reverse(bytes);
            return BitConverter.ToInt16(bytes, 0);
        }
        byte GetByte()
        {
            return (byte)stream.ReadByte();
        }
        void GetIntArray(int[] array)
        {
            for (var i = 0; i < array.Length; ++i)
            {
                array[i] = GetInt();
            }
        }
        string GetString()
        {
            // can make this more efficient if it's a bottleneck
            var bytes = new List<byte>();
            var oneByte = stream.ReadByte();
            while (oneByte != 0)
            {
                bytes.Add((byte)oneByte);
                oneByte = stream.ReadByte();
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        void Expect(string s)
        {
            var start = stream.Position;
            var actual = GetString();
            if (actual != s)
            {
                if (actual.Length > 100)
                {
                    actual = actual.Substring(0, 100) + "...";
                }
                throw new Exception($"Expected \"{s}\" at {start:X}; got \"{actual}\"");
            }
        }
        void Expect(long i)
        {
            var start = stream.Position;
            var actual = GetLong();
            if (actual != i)
            {
                throw new Exception($"Expected long {i} at {start:X}; got {actual}");
            }
        }
        void Expect(int i)
        {
            var start = stream.Position;
            var actual = GetInt();
            if (actual != i)
            {
                throw new Exception($"Expected int {i} at {start:X}; got {actual}");
            }
        }
        void Expect(short i)
        {
            var start = stream.Position;
            var actual = GetShort();
            if (actual != i)
            {
                throw new Exception($"Expected short {i} at {start:X}; got {actual}");
            }
        }
        void Expect(byte i)
        {
            var start = stream.Position;
            var actual = GetByte();
            if (actual != i)
            {
                throw new Exception($"Expected byte {i} at {start:X}; got {actual}");
            }
        }
        void Expect(string[] array)
        {
            foreach (var s in array)
            {
                Expect(s);
            }
        }
        void Expect(int[] array)
        {
            foreach (var i in array)
            {
                Expect(i);
            }
        }
        void Maybe(int i)
        {
            var start = stream.Position;
            var actual = GetInt();
            if (actual != i)
            {
                Debug.Log($"int at {start:X} was {actual} instead of {i}");
            }
        }
        void Maybe(short i)
        {
            var start = stream.Position;
            var actual = GetShort();
            if (actual != i)
            {
                Debug.Log($"short at {start:X} was {actual} instead of {i}");
            }
        }
        void Maybe(byte i)
        {
            var start = stream.Position;
            var actual = GetByte();
            if (actual != i)
            {
                Debug.Log($"byte at {start:X} was {actual} instead of {i}");
            }
        }
        long SkipTo(string s)
        {
            var start = stream.Position;
            var matched = 0;
            do
            {
                var oneByte = stream.ReadByte();
                if (s[matched] == (char)oneByte)
                {
                    ++matched;
                }
                else
                {
                    matched = 0;
                }
                if (stream.Position == stream.Length)
                {
                    throw new Exception($"Skipped over {stream.Position - start} bytes looking for {s} and didn't find it");
                }
            } while (matched < s.Length);
            var skipped = stream.Position - start - s.Length;
            if (skipped > 0)
            {
                Debug.Log($"Skipped over {skipped} bytes to find {s}");
            }
            return skipped;
        }
    }

    class LZ4Decoder
    {
        public static void Decode(Stream inStream, int inLength, byte[] outBytes)
        {
            var start = inStream.Position;
            var outPos = 0;
            while (true)
            {
                var token = inStream.ReadByte();
                if (token == -1)
                {
                    throw new Exception("Missing token");
                }
                var literalCount = token >> 4;
                var matchLength = (token & 0xf) + 4;
                if (literalCount == 15)
                {
                    int moreCount;
                    do
                    {
                        moreCount = inStream.ReadByte();
                        if (moreCount == -1)
                        {
                            throw new Exception("Missing extra literal");
                        }
                        literalCount += moreCount;
                    } while (moreCount == 255);
                }

                if (inStream.Position + literalCount > inStream.Length)
                {
                    throw new Exception("Too many literals");
                }
                inStream.Read(outBytes, outPos, literalCount);
                outPos += literalCount;
                if (inStream.Position == start + inLength)
                {
                    return;
                }

                var offset = inStream.ReadByte();
                if (offset == -1)
                {
                    throw new Exception("Missing low offset");
                }
                var highOffset = inStream.ReadByte();
                if (highOffset == -1)
                {
                    throw new Exception("Missing high offset");
                }
                offset = (highOffset << 8) + offset;
                if (matchLength == 19)
                {
                    int moreCount;
                    do
                    {
                        moreCount = inStream.ReadByte();
                        if (moreCount == -1)
                        {
                            throw new Exception("Too much match");
                        }
                        matchLength += moreCount;
                    } while (moreCount == 255);
                }

                var startMatch = outPos - offset;
                if (startMatch < 0)
                {
                    throw new Exception($"Trying to start match {-startMatch} bytes before start of buffer");
                }
                for (var i = startMatch; i < startMatch + matchLength; ++i)
                {
                    outBytes[outPos++] = outBytes[i];
                }
            }
        }
        public IEnumerable<List<long>> ValidDecodes(Stream stream)
        {
            var start = stream.Position;
            var outPos = 0;
            while (true)
            {
                var token = stream.ReadByte();
                if (token == -1)
                {
                    break;
                }
                var literalCount = token >> 4;
                var matchLength = (token & 0xf) + 4;
                if (literalCount == 15)
                {
                    int moreCount;
                    do
                    {
                        moreCount = stream.ReadByte();
                        if (moreCount == -1)
                        {
                            throw new Exception();
                        }
                        literalCount += moreCount;
                    } while (moreCount == 255);
                }
                //Debug.Log($"LiteralCount: {literalCount}");
                if (stream.Position + literalCount > stream.Length)
                {
                    break;
                }

                stream.Position += literalCount;
                outPos += literalCount;
                if (literalCount >= 5)
                {
                    yield return new List<long> { stream.Position - start, outPos };
                }

                var offset = stream.ReadByte();
                if (offset == -1)
                {
                    break;
                }
                var highOffset = stream.ReadByte();
                if (highOffset == -1)
                {
                    break;
                }
                offset = (highOffset << 8) + offset;
                if (matchLength == 19)
                {
                    int moreCount;
                    do
                    {
                        moreCount = stream.ReadByte();
                        if (moreCount == -1)
                        {
                            throw new Exception();
                        }
                        matchLength += moreCount;
                    } while (moreCount == 255);
                }

                var startMatch = outPos - offset;
                if (startMatch < 0)
                {
                    break;
                }
                //Debug.Log($"Offset: {offset}");
                //Debug.Log($"MatchLength: {matchLength}");
                outPos += matchLength;
            }
        }
    }
}