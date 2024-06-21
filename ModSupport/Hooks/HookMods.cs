using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using Fungus;
using HarmonyLib;
using Ideafixxxer.CsvParser;
using MoonSharp.Interpreter;
using Mortal.Core;
using Mortal.Story;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Mortal
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
        static Dictionary<string, string> luaFileTable = new Dictionary<string, string>();
        static Dictionary<string, string> stringTable = new Dictionary<string, string>();

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

            // 外部读取文本
            foreach (string file in Directory.EnumerateFiles(modPath, "*.lua", SearchOption.AllDirectories))
            {
                Debug.Log($"ModSupport: Add text file {file}");
                luaFileTable.Add(Path.GetFileNameWithoutExtension(file), file);
            }

            // 外部读取本地化表
            string stringTablePath = Path.Combine(modPath, "StringTable.csv");
            if (File.Exists(stringTablePath))
            {
                Debug.Log($"ModSupport: Reading StringTable {stringTablePath}");
                int lines = ReadStringTable(File.ReadAllText(stringTablePath));
                Debug.Log($"ModSupport: Finish reading {lines} lines.");
            }
        }

        /// <summary>
        /// 读csv
        /// </summary>
        private int ReadStringTable(string data)
        {
            CsvParser parser = new CsvParser();
            var csvLines = parser.Parse(data);
            foreach (var line in csvLines)
            {
                stringTable.Add(line[0], line[1]);
            }
            return csvLines.Length;
        }

        public void OnUpdate()
        {
        }

        /// <summary>
        /// 重定向Lua脚本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(LuaManager), "ExecuteLuaScript")]
        public static bool LuaScriptRedirect(ref LuaManager __instance)
        {
            Traverse.Create(__instance);
            string textName = __instance.ScriptName;
            if (!luaFileTable.ContainsKey(textName))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find external lua file {textName}");
            var luaEnv = Traverse.Create(__instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            string friendlyName = textName + ".LuaScript";
            string text = File.ReadAllText(luaFileTable[textName]);
            Closure fn = luaEnv.LoadLuaFunction(text, friendlyName);
            luaEnv.RunLuaFunction(fn, true, null);
            return false;
        }

        /// <summary>
        /// 重定向Loc文本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(LeanLocalizationResolver), "GetString", new Type[] { typeof(string) })]
        public static bool GetStringRedirect(ref LeanLocalizationResolver __instance, ref string __result, string key)
        {
            if (!stringTable.ContainsKey(key))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find external string {key}");
            __result = stringTable[key];
            return false;
        }

    }
}
