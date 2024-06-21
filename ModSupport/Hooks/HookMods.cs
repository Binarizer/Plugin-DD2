using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using Fungus;
using HarmonyLib;
using Ideafixxxer.CsvParser;
using MoonSharp.Interpreter;
using Mortal.Core;
using Mortal.Story;
using OBB.Framework.Attributes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        static Dictionary<string, string> portraitTable = new Dictionary<string, string>();
        static Dictionary<string, Sprite> portraitCache = new Dictionary<string, Sprite>();

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

            // 外部读取头像
            string portraitDir = Path.Combine(modPath, "Portraits");
            if (Directory.Exists(portraitDir))
            {
                foreach (string file in Directory.EnumerateFiles(modPath, "*.png", SearchOption.AllDirectories))
                {
                    Debug.Log($"ModSupport: Add portrait file {file}");
                    portraitTable.Add(Path.GetFileNameWithoutExtension(file), file);
                }
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

        static Dictionary<PortraitType, string> portraitTypeToString = null;
        /// <summary>
        /// 重定向头像
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(StoryCharacterData), "GetPortraitSprite", new Type[] { typeof(PortraitType) })]
        public static bool GetPortraitSprite_Redirect(ref StoryCharacterData __instance, ref Sprite __result, PortraitType type)
        {
            if (portraitTypeToString == null)
            {
                // 构造头像类型到string的映射
                portraitTypeToString = new Dictionary<PortraitType, string>();
                foreach (PortraitType value in Enum.GetValues(typeof(PortraitType)))
                {
                    FieldInfo field = typeof(PortraitType).GetField(value.ToString());
                    var stringValueAttribute = Attribute.GetCustomAttribute(field, typeof(StringValueAttribute)) as StringValueAttribute;
                    portraitTypeToString.Add(value, stringValueAttribute.StringValue);
                }
            }

            string portraitTypeName = portraitTypeToString[type];
            string portraitName = $"{__instance.Id}_{portraitTypeName}";
            return ReplacePortrait(portraitName, ref __result);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(StoryCharacterData), "DefaultPortrait", MethodType.Getter)]
        public static bool DefaultPortrait_Redirect(ref StoryCharacterData __instance, ref Sprite __result)
        {
            string portraitName = $"{__instance.Id}";
            return ReplacePortrait(portraitName, ref __result);
        }

        public static bool ReplacePortrait(string portraitName, ref Sprite __result)
        {
            if (portraitCache.ContainsKey(portraitName))
            {
                Debug.Log($"Find cached portrait {portraitName}");
                __result = portraitCache[portraitName];
                return false;
            }

            if (portraitTable.ContainsKey(portraitName))
            {
                Debug.Log($"Find mod portrait {portraitName}");
                var sprite = LoadSprite(portraitTable[portraitName]);
                if (sprite != null)
                {
                    sprite.name = portraitName;
                    portraitCache.Add(portraitName, sprite);
                    __result = sprite;
                    return false;
                }
            }

            return true;
        }

        public static Sprite LoadSprite(string FilePath, float PixelsPerUnit = 100.0f, SpriteMeshType spriteType = SpriteMeshType.Tight)
        {
            Texture2D Tex2D;
            byte[] FileData;

            if (File.Exists(FilePath))
            {
                FileData = File.ReadAllBytes(FilePath);
                Tex2D = new Texture2D(2, 2);
                if (Tex2D.LoadImage(FileData))
                {
                    Sprite NewSprite = Sprite.Create(Tex2D, new Rect(0, 0, Tex2D.width, Tex2D.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);
                    return NewSprite;
                }
            }
            return null;
        }

    }
}
