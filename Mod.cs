using System;
using System.Collections.Generic;
using System.Text;

namespace TestDDLCMod
{
    public class Mod : UnityEngine.Object
    {
        public readonly string Path;
        public static Mod ActiveMod = null;
        public Mod(string path)
        {
            Path = path;
        }
    }
}
