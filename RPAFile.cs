using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace TestDDLCMod
{
    public class RPAFile
    {
        private string path;
        private Dictionary<string, FileSpec> fileSpecs = new Dictionary<string, FileSpec>();

        public bool Ok { get; private set; } = false;

        public RPAFile(string path)
        {
            this.path = path;
            using (var stream = File.OpenRead(path))
            {
                if (!Expect(stream, "RPA-3.0 ")) return;

                var indexOffset = long.Parse(GetString(stream, 16), NumberStyles.HexNumber);
                if (!Expect(stream, " ")) return;

                var key = int.Parse(GetString(stream, 8), NumberStyles.HexNumber);

                stream.Position = indexOffset; // skip 2 for the zlib header
                var indexBytes = new byte[stream.Length - stream.Position];
                stream.Read(indexBytes, 0, indexBytes.Length);
                var pythonObj = Unpickler.UnpickleZlibBytes(indexBytes);

                if (pythonObj.Type != PythonObj.ObjType.DICTIONARY)
                {
                    Debug.LogError("Pickled index isn't a dictionary?");
                    return;
                }

                foreach (var entry in pythonObj.Dictionary)
                {
                    var name = entry.Key.String;
                    var rawOffset = entry.Value.List[0].List[0].ToInt();
                    var rawLength = entry.Value.List[0].List[1].ToInt();
                    var offset = rawOffset ^ key;
                    var length = rawLength ^ key;
                    fileSpecs[name] = new FileSpec(name, length, offset);
                }
                Ok = true;
            }
        }

        public byte[] GetFile(string name)
        {
            byte[] bytes = new byte[fileSpecs[name].Length];
            using (var stream = File.OpenRead(path))
            {
                stream.Position = fileSpecs[name].Offset;
                stream.Read(bytes, 0, bytes.Length);
            }
            return bytes;
        }

        public IEnumerator<FileSpec> GetEnumerator()
        {
            return fileSpecs.Values.GetEnumerator();
        }

        private bool Expect(Stream stream, string s)
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

        private string GetString(Stream stream, int count)
        {
            byte[] bytes = new byte[count];
            stream.Read(bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }

        public class FileSpec
        {
            public readonly string Name;
            public readonly long Length;
            public readonly long Offset;

            public FileSpec(string name, long length, long offset)
            {
                Name = name;
                Length = length;
                Offset = offset;
            }
        }
    }
}
