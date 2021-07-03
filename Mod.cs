namespace TestDDLCMod
{
    public class Mod : UnityEngine.Object
    {
        public readonly string Path;
        public static Mod ActiveMod = new Mod("Base Game");
        public Mod(string path)
        {
            Path = path;
        }
    }
}
