using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using Fungus;
using HarmonyLib;
using Ideafixxxer.CsvParser;
using MoonSharp.Interpreter;
using Mortal.Core;
using Mortal.Story;
using NAudio.Wave;
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
        private static ConfigEntry<bool> gifEnable;

        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        readonly static string ModRootPath = Path.Combine(Environment.CurrentDirectory, "Mods");
        readonly static Dictionary<string, string> mapStory = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapCondition = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapSwitch = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapPosition = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapString = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapVoice = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapPortrait = new Dictionary<string, string>();
        readonly static Dictionary<string, Sprite> cacheSprite = new Dictionary<string, Sprite>();
        readonly static Dictionary<string, Gif> mapGif = new Dictionary<string, Gif>();

        static Component luaExt = null; // 外挂自定义lua解析器
        static WaveOutEvent waveOut = new WaveOutEvent();   // 外挂音频处理

        static void AddFile(Dictionary<string, string> dict, string file)
        {
            var key = Path.GetFileNameWithoutExtension(file);
            if (!dict.ContainsKey(file))
            {           
                Debug.Log($"ModSupport: Add file {file}");
                dict.Add(key, file);
            }
        }

        public static List<string> ModPaths = new List<string>();

        /// <summary>
        /// 获取首个符合相对路径的文件
        /// </summary>
        public static string FindModFile(string path)
        {
            foreach(var modPath in ModPaths)
            {
                var fullPath = Path.Combine(modPath, path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            return null;
        }

        public void OnRegister(BaseUnityPlugin plugin)
        {
            modName = plugin.Config.Bind("Mod Support", "Mod Name", "test", "Mod Name");
            gifEnable = plugin.Config.Bind("Mod Support", "Gif Enable", true, "Enable import gif");
            luaExt = plugin.gameObject.AddComponent<LuaExt>();

            if (string.IsNullOrEmpty(modName.Value))
            {
                Debug.Log($"ModSupport: No mod.");
                return;
            }

            var mods = modName.Value.Trim().Split(',');
            foreach (var mod in mods )
            {
                var modPath = Path.Combine(ModRootPath, mod);
                if (!Directory.Exists(modPath))
                {
                    Debug.Log($"ModSupport: mod dir not exist.");
                    return;
                }

                Debug.Log($"ModSupport: Scan mod path {modPath}");
                ModPaths.Add(modPath);

                // 外部读取lua剧本
                string storyPath = Path.Combine(modPath, "story");
                if (Directory.Exists(storyPath))
                {
                    foreach (string file in Directory.EnumerateFiles(storyPath, "*.lua", SearchOption.AllDirectories))
                    {
                        AddFile(mapStory, file);
                    }
                }

                // 外部读取等价lua条件判定
                string conditionPath = Path.Combine(modPath, "LuaEquivalent/Condition");
                if (Directory.Exists(conditionPath))
                {
                    foreach (string file in Directory.EnumerateFiles(conditionPath, "*.lua", SearchOption.AllDirectories))
                    {
                        AddFile(mapCondition, file);
                    }
                }

                // 外部读取等价lua分支判定
                string switchPath = Path.Combine(modPath, "LuaEquivalent/Switch");
                if (Directory.Exists(switchPath))
                {
                    foreach (string file in Directory.EnumerateFiles(switchPath, "*.lua", SearchOption.AllDirectories))
                    {
                        AddFile(mapSwitch, file);
                    }
                }

                // 外部读取等价lua工作地点（position）
                string positionPath = Path.Combine(modPath, "LuaEquivalent/Position");
                if (Directory.Exists(positionPath))
                {
                    foreach (string file in Directory.EnumerateFiles(positionPath, "*.lua", SearchOption.AllDirectories))
                    {
                        AddFile(mapPosition, file);
                    }
                }

                // 外部读取本地化表
                string stringTablePath = Path.Combine(modPath, "StringTable.csv");
                if (File.Exists(stringTablePath))
                {
                    int lines = AddStringTable(File.ReadAllText(stringTablePath));
                    Debug.Log($"ModSupport: Add {lines} lines to StringTable.");
                }

                // 外部读取头像
                string portraitDir = Path.Combine(modPath, "Portraits");
                if (Directory.Exists(portraitDir))
                {
                    foreach (string file in Directory.EnumerateFiles(portraitDir, "*.*", SearchOption.AllDirectories))
                    {
                        AddFile(mapPortrait, file);
                    }
                }

                // 外部读取配音
                string voiceDir = Path.Combine(modPath, "Voice");
                if (Directory.Exists(voiceDir))
                {
                    foreach (string file in Directory.EnumerateFiles(voiceDir, "*.mp3", SearchOption.AllDirectories))
                    {
                        AddFile(mapVoice, file);
                    }
                }
            }
        }

        /// <summary>
        /// 读csv并添加到本地化表格
        /// </summary>
        private int AddStringTable(string data)
        {
            CsvParser parser = new CsvParser();
            var csvLines = parser.Parse(data);
            foreach (var line in csvLines)
            {
                if (!string.IsNullOrEmpty(line[0]) && !mapString.ContainsKey(line[0]))
                {
                    mapString.Add(line[0], line[1]);
                }
            }
            return csvLines.Length;
        }

        public void OnUpdate()
        {
            if (gifEnable.Value && mapGif.Count > 0)
            {
                UnityEngine.UI.Image[] images = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>();
                foreach (var image in images)
                {
                    if (mapGif.ContainsKey(image.gameObject.name))
                    {
                        mapGif[image.gameObject.name].Update(image);
                    }
                }
            }
        }

        /// <summary>
        /// 播语音
        /// </summary>
        static void PlayVoice(string audioFilePath)
        {
            var waveStream = new Mp3FileReader(audioFilePath);
            waveOut.Init(waveStream);
            waveOut.Play();
        }

        /// <summary>
        /// 重定向音频文件1
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(SayDialog), "DoSay")]
        public static bool SayPrefix(ref string text)
        {
            waveOut.Stop();

            if (!text.StartsWith("ac<"))
                return true;

            int textPos = text.IndexOf(">");
            if (textPos < 4)
                return true;

            string voiceKey = text.Substring(3, textPos - 3);
            if (!mapVoice.ContainsKey(voiceKey))
                return true;

            PlayVoice(mapVoice[voiceKey]);

            text = text.Substring(textPos + 1);
            return true;
        }

        /// <summary>
        /// 重定向音频文件2
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(LuaManager), "GetStoryText")]
        public static void GetStoryTextPostProcess(string key, ref string __result)
        {
            if (mapVoice.ContainsKey(key))
            {
                __result = $"ac<{key}>{__result}";
            }
        }

        /// <summary>
        /// 重定向剧本Lua文件
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(LuaManager), "ExecuteLuaScript")]
        public static bool LuaScriptRedirect(ref LuaManager __instance)
        {
            Traverse.Create(__instance);
            string textName = __instance.ScriptName;
            if (!mapStory.ContainsKey(textName))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find external lua file {textName}");
            var luaEnv = Traverse.Create(__instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            string friendlyName = textName + ".LuaScript";
            string text = File.ReadAllText(mapStory[textName]);
            Closure fn = luaEnv.LoadLuaFunction(text, friendlyName);
            luaEnv.RunLuaFunction(fn, true, null);
            return false;
        }

        /// <summary>
        /// 重定向等价Condition脚本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Condition")]
        public static bool ConditionRedirect(string name, ref bool __result)
        {
            if (!mapCondition.ContainsKey(name))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find external Condition lua {name}");
            string script = File.ReadAllText(mapCondition[name]);
            Debug.Log($"Lua={script}");
            var luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            bool result = false;
            luaEnv.DoLuaString(script, "tmp.Condition", false, delegate (DynValue res)
            {
                result = res.Boolean;
            });
            __result = result;
            Debug.Log($"Result={__result}");
            return false;
        }

        /// <summary>
        /// 重定向等价Switch脚本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Switch")]
        public static bool SwitchRedirect(string name, ref int __result)
        {
            if (!mapSwitch.ContainsKey(name))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find external Switch lua {name}");
            string script = File.ReadAllText(mapSwitch[name]);
            var luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            int result = 0;
            luaEnv.DoLuaString(script, "tmp.Switch", false, delegate (DynValue res)
            {
                result = (int)res.Number;
            });
            __result = result;
            return false;
        }

        /// <summary>
        /// 重定向等价Position脚本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Position")]
        public static bool PositionRedirect(string name, ref string __result)
        {
            if (!mapPosition.ContainsKey(name))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find external Position lua {name}");
            string script = File.ReadAllText(mapPosition[name]);
            Debug.Log($"Lua={script}");
            var luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            string result = "";
            luaEnv.DoLuaString(script, "tmp.Position", false, delegate (DynValue res)
            {
                result = res.String;
            });
            __result = result;
            Debug.Log($"Result={__result}");
            return false;
        }

        /// <summary>
        /// 自定义lua解析器
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(LuaBindings), "AddBindings")]
        public static bool LuaBindings_Inject(ref LuaBindings __instance)
        {
            var t = Traverse.Create(__instance);
            var boundTypes = t.Field("boundTypes").GetValue<List<string>>();
            boundTypes.Add(luaExt.GetType().AssemblyQualifiedName);
            var boundObjects = t.Field("boundObjects").GetValue<List<BoundObject>>();
            boundObjects.Add(new BoundObject { key = "ext", obj = luaExt.gameObject, component = luaExt });
            return true;
        }

        /// <summary>
        /// 重定向Loc文本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(LeanLocalizationResolver), "GetString", new Type[] { typeof(string) })]
        public static bool GetStringRedirect(ref LeanLocalizationResolver __instance, ref string __result, string key)
        {
            if (!mapString.ContainsKey(key))
            {
                return true;
            }

            Debug.Log($"ModSupport: Find external string {key}");
            __result = mapString[key];
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
        [HarmonyPostfix, HarmonyPatch(typeof(PortraitState), "SetPortraitImageBySprite", new Type[] { typeof(Sprite)})]
        public static void SetPortraitImageBySprite_Post(ref PortraitState __instance, Sprite portrait)
        {
            if (__instance.portraitImage == null)
            {
                // GIF的话，指针是对不上的，需要用名字再搜一下
                __instance.portraitImage = __instance.allPortraits.Find(x => x.sprite.name == portrait.name);
            }
        }

        public static bool ReplacePortrait(string portraitName, ref Sprite __result)
        {
            if (mapPortrait.ContainsKey(portraitName))
            {
                Debug.Log($"Find mod portrait {portraitName}");
                var sprite = LoadSprite(mapPortrait[portraitName]);
                if (sprite != null)
                {
                    sprite.name = portraitName;
                    __result = sprite;
                    return false;
                }
            }

            return true;
        }

        public class Gif
        {
            public List<Sprite> frames = new List<Sprite>();
            public List<float> delay = new List<float>();
            private float time = 0.0f;
            private int frame = 0;

            public void Update(UnityEngine.UI.Image image)
            {
                time += Time.deltaTime;
                if (time >= delay[frame])
                {
                    frame = (frame + 1) % frames.Count;
                    time = 0.0f;
                    image.sprite = frames[frame];
                }
            }
        }

        public static Sprite LoadSprite(string filePath, float PixelsPerUnit = 100.0f, SpriteMeshType spriteType = SpriteMeshType.Tight)
        {
            if (cacheSprite.ContainsKey(filePath))
                return cacheSprite[filePath];
            if (!File.Exists(filePath))
                return null;

            var name = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".png")
            {
                byte[] data = File.ReadAllBytes(filePath);
                Texture2D tex2D = new Texture2D(2, 2);
                if (tex2D.LoadImage(data))
                {
                    Sprite sprite = Sprite.Create(tex2D, new Rect(0, 0, tex2D.width, tex2D.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);
                    sprite.name = name;
                    cacheSprite.Add(filePath, sprite);
                    return sprite;
                }
            }
            else if (ext == ".gif")
            {
                byte[] data = File.ReadAllBytes(filePath);
                using (var decoder = new MG.GIF.Decoder(data))
                {
                    Texture2D tex2D;
                    var img = decoder.NextImage();
                    if (img == null)
                        return null;

                    var gif = new Gif();
                    Debug.Log($"Add gif {name}");
                    mapGif.Add(name, gif);
                    while (img != null)
                    {
                        tex2D = img.CreateTexture();
                        Sprite sprite = Sprite.Create(tex2D, new Rect(0, 0, tex2D.width, tex2D.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);
                        sprite.name = name;
                        gif.frames.Add(sprite);
                        gif.delay.Add(img.Delay * 0.001f);
                        img = decoder.NextImage();
                    }
                    cacheSprite.Add(filePath, gif.frames[0]);
                    return gif.frames[0];
                }
            }
            return null;
        }
    }
}
