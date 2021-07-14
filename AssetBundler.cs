using HarmonyLib;
using RenPyParser.AssetManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            Debug.Log("Parsing " + filename);
            /*if (filename != "bgm-coarse.cy")
            {
                return;
            }*/
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
            //Application.Quit();
        }
    }
    /*
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
    }*/

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
            //Debug.Log($"Compressed blockinfo size: {compressedSize:X}");
            var blocksInfoSize = GetInt(); // UncompressedBlocksInfoSize
            //Debug.Log($"Uncompressed blockinfo size: {blocksInfoSize:X}");
            Expect(0x43); // BundleFlags (BlockAndDirectoryInfoCombined + CompressionTypeMask=3[Lz4HC])
            while (stream.Position % 16 != 0)
            {
                Expect((byte)0);
            }

            byte[] blockInfo = new byte[blocksInfoSize];
            LZ4Decoder.Decode(stream, compressedSize, blockInfo);
            //DumpBytes(blockInfo);
            headerSize = stream.Position;

            ParseBlockInfo(blockInfo);
            ParseNodes();
        }

        private void ParseNodes()
        {
            //for each Node
            // offset is into decoded file
            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                NodeSpec node = nodes[nodeIndex];
                node.startBlock = blocks.Length;
                for (var i = 0; i < blocks.Length; ++i)
                {
                    if (blocks[i].offset == node.offset)
                    {
                        node.startBlock = i;
                        break;
                    }
                    else if (blocks[i].offset > node.offset)
                    {
                        node.startBlock = i - 1;
                        break;
                    }
                }

                node.endBlock = blocks.Length;
                for (var i = node.startBlock; i < blocks.Length; ++i)
                {
                    if (blocks[i].offset - blocks[node.startBlock].offset >= node.size)
                    {
                        node.endBlock = i;
                        break;
                    }
                }

                Debug.Log($"Node {nodeIndex,2}: {node}");
                if (node.startBlock >= blocks.Length || node.endBlock < node.startBlock)
                {
                    Debug.LogError($"wtf, weird node offset, size: {node.offset}, {node.size}");
                }
                if (node.size > int.MaxValue)
                {
                    Debug.Log($"Too big to extract!");
                    continue;
                }

                var nodeBytes = new byte[node.size];
                {
                    var nodeBytesPos = 0;
                    var nodeBytesLeft = nodeBytes.Length;
                    var blockPos = (int)(node.offset - blocks[node.startBlock].offset);
                    stream.Position = blocks[node.startBlock].fileOffset;
                    for (var i = node.startBlock; i < node.endBlock; ++i)
                    {
                        var toCopy = Math.Min(blocks[i].uncompressed - blockPos, nodeBytesLeft);
                        switch (blocks[i].flags)
                        {
                            case 0:
                                // uncompressed
                                stream.Position += blockPos;
                                stream.Read(nodeBytes, nodeBytesPos, toCopy);
                                break;
                            case 3:
                                // lz4 compressed
                                if (blockPos > 0 || nodeBytesLeft < blocks[i].uncompressed)
                                {
                                    var blockBytes = new byte[blocks[i].uncompressed];
                                    LZ4Decoder.Decode(stream, blocks[i].compressed, blockBytes);
                                    Array.Copy(blockBytes, blockPos, nodeBytes, nodeBytesPos, toCopy);
                                }
                                else
                                {
                                    // can decode directly into nodeBytes
                                    LZ4Decoder.Decode(stream, blocks[i].compressed, nodeBytes, nodeBytesPos);
                                }
                                break;
                            default:
                                Debug.LogError($"Unrecognized block flags for block {i} ${blocks[i].flags:X}");
                                break;
                        }

                        blockPos = 0; // copying from the start of the next block if there is one
                        nodeBytesPos += toCopy;
                        nodeBytesLeft -= toCopy;
                    }
                }
                //DumpBytes(nodeBytes);
            }
        }

        private void ParseBlockInfo(byte[] blockInfo)
        {
            var outerStream = stream;
            using (stream = new MemoryStream(blockInfo))
            {
                int[] hash = new int[4];
                GetIntArray(hash);
                var hashString = $"{hash[0],8:X}{hash[1],8:X}{hash[2],8:X}{hash[3],8:X}".Replace(' ', '0');
                if (hashString != "00000000000000000000000000000000")
                {
                    Debug.Log($"Hash:{hashString}");
                }
                int blockArraySize = GetInt();
                //Debug.Log($"Block array size: {blockArraySize}");

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
                    //Debug.Log($"Block {i,5}: {blocks[i]}");

                    totalCompressed += blocks[i].compressed;
                    totalUncompressed += blocks[i].uncompressed;
                }
                if (totalCompressed + headerSize != outerStream.Length)
                {
                    Debug.Log($"{outerStream.Length - totalCompressed - headerSize} extra bytes at the end of the bundle!");
                }

                int nodeArraySize = GetInt();
                //Debug.Log($"Node array size is: {nodeArraySize}");
                nodes = new NodeSpec[nodeArraySize];
                for (var i = 0; i < nodeArraySize; ++i)
                {
                    nodes[i].offset = GetLong();
                    nodes[i].size = GetLong();
                    nodes[i].index = GetInt();
                    nodes[i].pathOrigin = GetString();
                    //Debug.Log($"Node {i,2}: {nodes[i]}");
                }
                if (stream.Position != stream.Length)
                {
                    Debug.Log("Leftover bytes at end of blockinfo?");
                }
            }
            stream = outerStream;
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
            public override string ToString() => $"uncompressed:{uncompressed,5:X}, offset:{offset,8:X}, compressed:{compressed,5:X}, fileOffset:{fileOffset,7:X}, flags:{flags:X}";
        }
        struct NodeSpec
        {
            public long offset;
            public long size;
            public int index;
            public string pathOrigin;
            public int startBlock;
            public int endBlock;
            public override string ToString() => $"offset:{offset,8:X}, size:{size,8:X}, index:{index,2}, blocks:{$"{startBlock}-{endBlock - 1}",11}, pathOrigin:{pathOrigin}";
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
        public static bool LogDecode = false;
        public static void Decode(Stream inStream, int inLength, byte[] outBytes, int outOffset = 0)
        {
            var start = inStream.Position;
            var outPos = outOffset;
            if (LogDecode) Debug.Log($"Starting decode: {inLength,7:X} bytes starting at {start,7:X}");
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
                if (LogDecode) Debug.Log($"{literalCount} literals");
                inStream.Read(outBytes, outPos, literalCount);
                outPos += literalCount;
                if (inStream.Position == start + inLength)
                {
                    return;
                }

                var lowOffset = inStream.ReadByte();
                if (lowOffset == -1)
                {
                    throw new Exception("Missing low offset");
                }
                var highOffset = inStream.ReadByte();
                if (highOffset == -1)
                {
                    throw new Exception("Missing high offset");
                }
                var offset = ((byte)highOffset << 8) + (byte)lowOffset;
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
                if (LogDecode) Debug.Log($"Copying {matchLength} starting at {startMatch}");
                if (startMatch + matchLength <= outPos)
                {
                    Array.Copy(outBytes, startMatch, outBytes, outPos, matchLength);
                    outPos += matchLength;
                }
                else
                {
                    // overlapping match, just do it by hand
                    for (var i = startMatch; i < startMatch + matchLength; ++i)
                    {
                        outBytes[outPos++] = outBytes[i];
                    }
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

    [HarmonyPatch(typeof(ActiveAssetBundles))]
    public static class DontUnloadBundles
    {
        public const string MOD_BUNDLE_NAME = "mod_assets";

        [HarmonyPatch("LoadPermanentBundles")]
        static bool Prefix(ActiveAssetBundles __instance, Dictionary<string, AssetBundle> ___m_ActiveAssetBundles, ActiveBundles ___m_ActiveBundles)
        {
            if (!Mod.IsModded())
            {
                return true;
            }
            // permanently load all chapter bundles
            var PermanentBundles = new List<string>()
            {
                "gui",
                "bg",
                "cg",
                "monika",
                "yuri",
                "natsuki",
                "sayori",
                "bgm-coarse",
                "sfx-coarse",
            };
            foreach (var bundleFile in Directory.GetFiles("Doki Doki Literature Club Plus_Data/StreamingAssets/AssetBundles/" + PathHelpers.GetPlatformForAssetBundles(Application.platform)))
            {
                if (bundleFile.EndsWith(".cy"))
                {
                    var filename = Path.GetFileNameWithoutExtension(bundleFile);
                    if (filename.StartsWith("label "))
                    {
                        PermanentBundles.Insert(0, filename);
                    }
                }
            }
            var gestaltDependencies = ScriptableObject.CreateInstance<LabelAssetBundleDependencies>();
            var seenBundles = new HashSet<string>(PermanentBundles);
            for (int i = 0; i < PermanentBundles.Count; i++)
            {
                var bundle = PermanentBundles[i];
                if (!___m_ActiveAssetBundles.ContainsKey(bundle))
                {
                    Debug.Log($"Loading bundle: {bundle}");
                    AccessTools.Method(typeof(ActiveAssetBundles), "LoadBundleSync").Invoke(__instance, new object[] { bundle });
                }
                var assetBundle = ___m_ActiveAssetBundles[bundle];
                if (assetBundle == null)
                {
                    Debug.Log("Failed to load bundle!");
                    continue;
                }
                ___m_ActiveBundles.ForceAdd(bundle);
                foreach (var asset in assetBundle.GetAllAssetNames())
                {
                    gestaltDependencies.AddAsset(PathHelpers.SanitizePathToAddressableName(asset), bundle, $"Definitely Correct Path/{bundle}/{asset}");
                }
                foreach (var dependencies in assetBundle.LoadAllAssets<LabelAssetBundleDependencies>())
                {
                    dependencies.RequiredBundles.DoIf(seenBundles.Add, b => PermanentBundles.Insert(i + 1, b));
                }
            }

            // create a placeholder asset bundle for mod assets
            if (!___m_ActiveAssetBundles.ContainsKey(MOD_BUNDLE_NAME))
            {
                ___m_ActiveAssetBundles.Add(MOD_BUNDLE_NAME, AccessTools.Constructor(typeof(AssetBundle)).Invoke(new object[0]) as AssetBundle);
                Mod.ActiveMod.AssetContainer = ___m_ActiveAssetBundles[MOD_BUNDLE_NAME];
                ___m_ActiveBundles.ForceAdd(MOD_BUNDLE_NAME);
                foreach(var entry in Mod.ActiveMod.Assets)
                {
                    foreach(var subEntry in entry.Value)
                    {
                        var asset = PathHelpers.SanitizePathToAddressableName(subEntry.Value);
                        string bundle;
                        if (!gestaltDependencies.TryGetBundle(asset, out bundle))
                        {
                            gestaltDependencies.AddAsset(asset, MOD_BUNDLE_NAME, subEntry.Value);
                        }
                    }
                }
            }
            PermanentBundles.Add(MOD_BUNDLE_NAME);

            ActiveLabelAssetBundles labelBundles = AccessTools.StaticFieldRefAccess<ActiveLabelAssetBundles>(typeof(Renpy), "s_ActiveLabelAssetBundles");
            AccessTools.Field(typeof(ActiveLabelAssetBundles), "<LabelAssetBundle>k__BackingField").SetValue(labelBundles, gestaltDependencies);
            AccessTools.Field(typeof(ActiveLabelAssetBundles), "<HasLabelAssetBundleLoaded>k__BackingField").SetValue(labelBundles, true);

            AccessTools.Field(typeof(ActiveAssetBundles), "PermanentHashCodes").SetValue(__instance, PermanentBundles.Select(s => s.GetHashCode()).ToArray());
            AccessTools.Field(typeof(ActiveAssetBundles), "PermanentHashCodesLength").SetValue(__instance, PermanentBundles.Count());
            return false;
        }
    }

    // with assets for all labels loaded, we don't want label changes to them
    [HarmonyPatch(typeof(ActiveLabelAssetBundles))]
    public static class DontUnloadOnLabelChange
    {
        [HarmonyPatch("ChangeLabel", new Type[] { typeof(string) })]
        static bool Prefix(ActiveLabelAssetBundles __instance, string label, ref bool __result)
        {
            if (!Mod.IsModded())
            {
                return true;
            }
            AccessTools.Field(typeof(ActiveLabelAssetBundles), "<ActiveLabel>k__BackingField").SetValue(__instance, label);
            __result = true;
            Debug.Log("Changing label to " + label);
            //Debug.Log(Environment.StackTrace);
            return false;
        }
        [HarmonyPatch("ChangeLabelSync", new Type[] { typeof(string) })]
        static bool Prefix(ActiveLabelAssetBundles __instance, string label)
        {
            if (!Mod.IsModded())
            {
                return true;
            }
            AccessTools.Field(typeof(ActiveLabelAssetBundles), "<ActiveLabel>k__BackingField").SetValue(__instance, label);
            Debug.Log("Changing label (sync) to " + label);
            return false;
        }
        [HarmonyPatch("ClearCurrentLabelBundle")]
        static bool Prefix()
        {
            if (!Mod.IsModded())
            {
                return true;
            }
            return false;
        }
        [HarmonyPatch("ValidateLoad")]
        static bool Prefix(ActiveLabelAssetBundles __instance, string path, ref string bundleName, ref bool __result)
        {
            if (!Mod.IsModded())
            {
                return true;
            }
            if (!__instance.HasLabelAssetBundleLoaded)
            {
                Debug.Log("Why aren't label asset bundles loaded!?");
                Renpy.LoadPermanentAssetBundles();
            }
            return true;
        }
        [HarmonyPatch("ValidateLoad")]
        static void Postfix(string path, ref string bundleName)
        {
            Debug.Log($"Found {path} in {bundleName}");
        }
    }

    [HarmonyPatch(typeof(AssetBundle), "LoadAsset", new Type[] { typeof(string), typeof(Type) })]
    public static class PatchAssetBundle
    {
        static bool Prefix(AssetBundle __instance, string name, Type type, ref object __result)
        {
            if (__instance != Mod.ActiveMod.AssetContainer)
            {
                return true;
            }
            if (!Mod.IsModded())
            {
                return true;
            }
            if (!Mod.ActiveMod.Assets[type].ContainsKey(name))
            {
                return true;
            }
            Debug.Log($"Found mod asset {name} at {Mod.ActiveMod.Assets[type][name]}");
            __result = PatchFileBrowserApp.LoadResource(Mod.ActiveMod.Assets[type][name], type);
            return false;
        }
    }
}