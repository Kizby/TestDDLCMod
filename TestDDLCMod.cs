using System;
using BepInEx;

namespace TestDDLCMod
{
    [BepInPlugin("org.kizbyspark.plugins.testddlcmod", "Test DDLC Mod", "0.1.0.0")]
    [BepInProcess("Doki Doki Literature Club Plus.exe")]
    public class TestDDLCMod : BaseUnityPlugin
    {
        void Awake()
        {
            UnityEngine.Debug.Log("Hello, world!");
        }
    }
}
