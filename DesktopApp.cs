using HarmonyLib;
using RenpyLauncher;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;

namespace TestDDLCMod
{
    [HarmonyPatch(typeof(DesktopApp), "Start")]
    public static class PatchDesktopAppStart
    {
        static void Prefix(DesktopApp __instance)
        {
            var StartMenuContainer = __instance.DesktopDesktop.transform.Find("StartMenuContainer") as RectTransform;
            StartMenuContainer.sizeDelta += new Vector2(0, 73);
            var StartMenuItemCanvas = __instance.DesktopDesktop.transform.Find("StartMenuItemCanvas") as RectTransform;
            var QuitButton = StartMenuItemCanvas.Find("QuitButton");
            var ModsButton = UnityEngine.Object.Instantiate(QuitButton, StartMenuItemCanvas, false);
            ModsButton.name = "ModsButton";

            QuitButton.localPosition -= new Vector3(0, 73);

            var SettingsNavigation = __instance.SettingsButton.navigation;
            SettingsNavigation.selectOnDown = ModsButton.GetComponent<StartMenuButton>();
            __instance.SettingsButton.navigation = SettingsNavigation;

            var ModsNavigation = ModsButton.GetComponent<StartMenuButton>().navigation;
            ModsNavigation.selectOnUp = __instance.SettingsButton;
            ModsNavigation.selectOnDown = QuitButton.GetComponent<StartMenuButton>();
            ModsButton.GetComponent<StartMenuButton>().navigation = ModsNavigation;

            var QuitNavigation = QuitButton.GetComponent<StartMenuButton>().navigation;
            QuitNavigation.selectOnUp = ModsButton.GetComponent<StartMenuButton>();
            QuitButton.GetComponent<StartMenuButton>().navigation = QuitNavigation;

            var ModsButtonText = ModsButton.Find("QuitButtonText (TMP)");
            ModsButtonText.name = "ModsButtonText (TMP)";

            var ModsButtonTextComponent = ModsButtonText.GetComponent<TMPro.TextMeshProUGUI>();
            ModsButtonTextComponent.text = "Mods";
            __instance.ButtonStrings.Insert(__instance.ButtonStrings.Count - 1, ModsButtonTextComponent.text);
            __instance.ButtonTexts.Insert(__instance.ButtonTexts.Count - 1, ModsButtonTextComponent);

            var ModsButtonImage = ModsButton.Find("QuitButtonImage");
            ModsButtonImage.name = "ModsButtonImage";

            var ModsButtonImageComponent = ModsButtonImage.GetComponent<Image>();
            Resources.FindObjectsOfTypeAll<Sprite>().DoIf(sprite => sprite.name == "files icon", sprite => ModsButtonImageComponent.sprite = sprite);

            var ModsButtonHighlightImageComponent = ModsButton.Find("HighlightImage").GetComponent<Image>();
            Resources.FindObjectsOfTypeAll<Sprite>().DoIf(sprite => sprite.name == "file icons highlight", sprite => ModsButtonHighlightImageComponent.sprite = sprite);

            var ModsStartMenuButton = ModsButton.GetComponent<StartMenuButton>();
            ModsStartMenuButton.onClick = new Button.ButtonClickedEvent();
            var InProgressField = typeof(DesktopApp).GetField("m_StartMenuInProgress", BindingFlags.NonPublic | BindingFlags.Instance);
            var NextAppField = typeof(DesktopApp).GetField("m_NextApp", BindingFlags.NonPublic | BindingFlags.Instance);
            ModsStartMenuButton.onClick.AddListener(() =>
            {
                if (InProgressField.GetValue(__instance).Equals(true))
                {
                    return;
                }
                LauncherMain.PlayStartApp();
                NextAppField.SetValue(__instance, ModBrowserApp.ModBrowserAppId);
            });
        }
    }
    [HarmonyPatch()]
    public static class PatchDesktopAppStartMenuToggle
    {
        static Predicate<CodeInstruction> StartPredicate = instruction => instruction.Is(OpCodes.Call, AccessTools.Method(typeof(RenpyParser.Utils), "IsConsolePlatform"));
        static Predicate<CodeInstruction> EndPredicate = instruction => instruction.opcode == OpCodes.Stloc_3;

        static MethodBase TargetMethod()
        {
            Type DesktopApp = typeof(DesktopApp);
            foreach (Type NestedType in DesktopApp.GetNestedTypes(BindingFlags.NonPublic))
            {
                if (NestedType.Name.Contains("StartMenuToggle"))
                {
                    BindingFlags MethodFlags = BindingFlags.NonPublic | BindingFlags.Instance;
                    return NestedType.GetMethod("MoveNext", MethodFlags);
                }
            }
            return null;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool Replacing = false;
            foreach (var instruction in instructions)
            {
                if (StartPredicate(instruction))
                {
                    Replacing = true;
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PatchDesktopAppStartMenuToggle), "GetStartMenuHeight"));
                }
                if (Replacing)
                {
                    Replacing = !EndPredicate(instruction);
                }
                if (!Replacing)
                {
                    yield return instruction;
                }
            }
        }

        static float GetStartMenuHeight(DesktopApp app)
        {
            var ButtonCount = app.ButtonTexts.Count;
            if (RenpyParser.Utils.IsConsolePlatform())
            {
                --ButtonCount;
            }
            return 69 + 74 * ButtonCount;
        }
    }
}
