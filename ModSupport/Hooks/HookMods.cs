using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using BepInEx;
using BepInEx.Configuration;

namespace Millennia
{
    // 文本Mod支持
    public class HookMods : IHook
    {
        private static ConfigEntry<string> modName;

        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        readonly static string ModRootPath = Path.Combine(Environment.CurrentDirectory, "Mods");
        static Dictionary<string, string> ModFileDict = new Dictionary<string, string>();

        public void OnRegister(BaseUnityPlugin plugin)
        {
            modName = plugin.Config.Bind("Mod Support", "Mod Name", "test", "Mod Name");

            if (modName.Value == "")
            {
                Debug.Log($"ModSupport: No mod.");
                return;
            }

            var modPath = Path.Combine(ModRootPath, modName.Value);
            if (!Directory.Exists(modPath))
            {
                Debug.Log($"ModSupport: mod dir not exist.");
                return;
            }

            foreach (string file in Directory.EnumerateFiles(modPath))
            {
                Debug.Log($"ModSupport: Add text file {file}");
                ModFileDict.Add(Path.GetFileNameWithoutExtension(file), file);
            }
        }

        public void OnUpdate()
        {
        }

        /// <summary>
        /// 重定向TextAsset
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(TextAsset), "text", MethodType.Getter)]
        public static bool TextAssetRedirect(ref TextAsset __instance, ref string __result)
        {
            string textName = __instance.name;
            if (!ModFileDict.ContainsKey(textName))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find mod file {textName}");
            __result = File.ReadAllText(ModFileDict[textName]);
            return false;
        }

    }
}
