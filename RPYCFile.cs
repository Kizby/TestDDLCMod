using System;
using System.Text;
using UnityEngine;

namespace TestDDLCMod
{
    public class RPYCFile
    {
        public bool Ok { get; private set; } = false;

        public RPYCFile(byte[] fileBytes)
        {
            string magic = "RENPY RPC2";
            if (Encoding.ASCII.GetString(fileBytes, 0, magic.Length) != magic)
            {
                Debug.LogError("Wrong magic in rpyc");
                return;
            }

            var beforeStatic = GetSlotAST(fileBytes, magic.Length, 1);
            Debug.Log("BeforeStatic pickled type is: " + beforeStatic.Type);
            var afterStatic = GetSlotAST(fileBytes, magic.Length + 12, 2);
            Debug.Log("AfterStatic pickled type is: " + afterStatic.Type);
        }

        PythonObj GetSlotAST(byte[] bytes, int offset, int slot)
        {
            if (slot != BitConverter.ToInt32(bytes, offset))
            {
                Debug.LogError("Wrong slot in rpyc?");
                return null;
            }
            var start = BitConverter.ToInt32(bytes, offset + 4);
            var length = BitConverter.ToInt32(bytes, offset + 8);

            return Unpickler.UnpickleZlibBytes(bytes, start, length);
        }
    }
}
