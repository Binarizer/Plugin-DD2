﻿using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using Fungus;
using HarmonyLib;
using Ideafixxxer.CsvParser;
using MoonSharp.Interpreter;
using Mortal.Core;
using Mortal.Story;
using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using static Mono.Security.X509.X520;

namespace Mortal
{
    // 文本Mod支持
    public class HookMods : IHook
    {
        private static ConfigEntry<string> modName;
        private static ConfigEntry<KeyCode> modKey;
        private static ConfigEntry<KeyCode> consoleKey;
        private static ConfigEntry<bool> gifEnable;
        private static ConfigEntry<bool> aaEnable;

        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        readonly static string ModRootPath = Path.Combine(Environment.CurrentDirectory, "Mods");
        readonly static Dictionary<string, string> mapCondition = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapSwitch = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapPosition = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapString = new Dictionary<string, string>();
        readonly static Dictionary<string, string> mapVoice = new Dictionary<string, string>();
        readonly static Dictionary<string, Sprite> cacheSprite = new Dictionary<string, Sprite>();
        readonly static Dictionary<string, Gif> mapGif = new Dictionary<string, Gif>();

        static Component luaExt = null; // 外挂自定义lua解析器
        static WaveOutEvent waveOut = new WaveOutEvent();   // 外挂音频处理

        static void AddFile(Dictionary<string, string> dict, string file)
        {
            var key = Path.GetFileNameWithoutExtension(file);
            if (!dict.ContainsKey(key))
            {
                Debug.Log($"ModSupport: Add file {file}");
                dict.Add(key, file);
            }
        }
        static string originalModValue = null;
        public static List<string> ModPaths = new List<string>();

        /// <summary>
        /// 获取首个符合相对路径的文件
        /// </summary>
        public static string FindModFile(string path)
        {
            foreach (var modPath in ModPaths)
            {
                var fullPath = Path.Combine(modPath, path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            return null;
        }

        public void OnRegister(PluginBinarizer plugin)
        {
            plugin.onUpdate += OnUpdate;
            plugin.onGui += OnGui;

            modName = plugin.Config.Bind("Mod Support", "Mod Name", "test", "Mod Name");
            originalModValue = modName.Value;
            modKey = plugin.Config.Bind("Mod Support", "Mod Key", KeyCode.F1, "Open mod list window");
            consoleKey = plugin.Config.Bind("Mod Support", "Console Key", KeyCode.F2, "Open console window");
            gifEnable = plugin.Config.Bind("Mod Support", "Gif Enable", true, "Enable import gif");
            aaEnable = plugin.Config.Bind("Mod Support", "Addressable Enable", true, "Enable addressable sprite replace");
            luaExt = plugin.gameObject.AddComponent<LuaExt>();

            if (string.IsNullOrEmpty(modName.Value))
            {
                Debug.Log($"ModSupport: No mod.");
                return;
            }

            var mods = modName.Value.Trim().Split(',');
            foreach (var mod in mods)
            {
                var modPath = Path.Combine(ModRootPath, mod);
                if (!Directory.Exists(modPath))
                {
                    Debug.Log($"ModSupport: mod dir not exist.");
                    return;
                }

                Debug.Log($"ModSupport: Scan mod path {modPath}");
                ModPaths.Add(modPath);

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

                // 外部读取头像(挪到HookDataTable)
                string portraitDir = Path.Combine(modPath, "Portraits");
                if (Directory.Exists(portraitDir))
                {
                    foreach (string file in Directory.EnumerateFiles(portraitDir, "*.*", SearchOption.AllDirectories))
                    {
                        AddFile(HookDataTable.mapPortrait, file);
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

            if (aaEnable.Value)
                Harmony.CreateAndPatchAll(typeof(AddressableSpriteFromFile));
        }

        /// <summary>
        /// 勾住Addressable的动态加载函数，直接从磁盘读取
        /// </summary>
        [HarmonyPatch]
        class AddressableSpriteFromFile
        {
            static System.Reflection.MethodBase TargetMethod()
            {
                return typeof(Addressables).GetMethod("LoadAssetAsync", new Type[] { typeof(object) }).MakeGenericMethod(typeof(Sprite));
            }
            static bool Prefix(object key, ref object __result)
            {
                string addressKey = key.ToString();
                if (addressKey.StartsWith("pic_"))
                    addressKey = "Picture/" + addressKey + ".png";
                foreach (var modDir in ModPaths)
                {
                    var path = Path.Combine(modDir, addressKey);
                    Sprite sprite = LoadSprite(path, addressKey);
                    if (sprite == null)
                        continue;
                    Debug.Log($"Addressable from file: {path}");
                    __result = Addressables.ResourceManager.CreateCompletedOperation(sprite, null);
                    return false;
                }
                return true;
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
                if (string.IsNullOrEmpty(line[0]) || mapString.ContainsKey(line[0]))
                    continue;
                mapString.Add(line[0], line[1]);
            }
            return csvLines.Length;
        }

        bool modListOn = false;
        Rect modListRect = new Rect(100, 100, 400, 320);
        Vector2 modScrollPosition;
        bool consoleOn = true;
        LuaEnvironment luaEnv = null;
        Rect consoleRect = new Rect(10, Screen.height - 100, 500, 80);
        string luaCmd = "";
        string luaRet = "";
        public void OnUpdate()
        {
            if (Input.GetKeyDown(modKey.Value))
            {
                modListOn = !modListOn;
            }
            if (Input.GetKeyDown(consoleKey.Value))
            {
                consoleOn = !consoleOn;
            }
            if (gifEnable.Value && mapGif.Count > 0)
            {
                GifUpdate(typeof(UnityEngine.UI.Image));
                GifUpdate(typeof(SpriteRenderer));
            }
        }

        public void OnGui()
        {
            modListOn = modListOn && versionText != null;
            if (modListOn)
            {
                modListRect = GUI.Window(857204, modListRect, new GUI.WindowFunction(DoWindowMod), "mod list");
            }

            luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            consoleOn = consoleOn && luaEnv != null;
            if (consoleOn)
            {
                consoleRect = GUI.Window(857205, consoleRect, new GUI.WindowFunction(DoWindowLua), "lua console");
            }
        }

        /// <summary>
        /// Draw Mod List Window
        /// </summary>
        public void DoWindowMod(int windowID)
        {
            aaEnable.Value = GUILayout.Toggle(aaEnable.Value, "开启换图功能(战役时请关闭并重启游戏)");

            var modDirs = Directory.GetDirectories(ModRootPath);
            var modOn = new bool[modDirs.Length];
            var mods = modName.Value.Trim().Split(',');
            var modifiedMods = new List<string>();
            modScrollPosition = GUILayout.BeginScrollView(modScrollPosition, GUILayout.Width(380), GUILayout.Height(270));
            for (int i = 0; i < modDirs.Length; ++i)
            {
                var dirName = Path.GetFileName(modDirs[i]);
                modOn[i] = mods.Contains(dirName);
                if (GUILayout.Toggle(modOn[i], dirName))
                    modifiedMods.Add(dirName);
            }
            modName.Value = string.Join(",", modifiedMods);
            var needRestart = modName.Value != originalModValue ? "(need restart)" : "";
            versionText.text = $"{Application.version}, mods: {modName.Value}{needRestart}";
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        /// <summary>
        /// Draw Lua Console Window
        /// </summary>
        public void DoWindowLua(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            luaCmd = GUILayout.TextField(luaCmd, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("run", GUILayout.Width(60)))
            {
                luaEnv?.DoLuaString(luaCmd, "Console.cmd", false, delegate (DynValue res)
                {
                    luaRet = res.ToString();
                });
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Return = {luaRet}");
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        static void GifUpdate(Type t)
        {
            UnityEngine.Object[] renderers = UnityEngine.Object.FindObjectsOfType(t);
            foreach (var renderer in renderers)
            {
                var traverse = Traverse.Create(renderer).Property("sprite");
                if (traverse == null)
                    continue;
                var sprite = traverse.GetValue<Sprite>();
                if (sprite == null || string.IsNullOrEmpty(sprite.name))
                    continue;
                if (mapGif.TryGetValue(sprite.name, out Gif gif))
                    gif.Update(traverse);
            }
        }

        static UnityEngine.UI.Text versionText = null;
        /// <summary>
        /// 修改显示的mod名
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(AppVersionText), "Start")]
        public static bool ChangeVersionText(AppVersionText __instance)
        {
            if (string.IsNullOrEmpty(modName.Value))
                return true;
            versionText = __instance.GetComponent<UnityEngine.UI.Text>();
            versionText.text = $"{Application.version}, mods: {modName.Value}";
            var rt = __instance.GetComponent<RectTransform>();
            rt.offsetMax = new Vector2(2000, rt.offsetMax.y);
            rt.sizeDelta = new Vector2(2000, rt.sizeDelta.y);
            return false;
        }

        /// <summary>
        /// Gif换图时，保持Sprite判定为同一张，防止换图时被错误隐藏
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(PortraitController), "Show", new Type[] { typeof(PortraitOptions) })]
        public static bool PortraitShow_GifIdentity(PortraitOptions options)
        {
            if (!options.portrait || !options.character.State.portrait)
                return true;
            if (options.portrait.name == options.character.State.portrait.name)
                options.portrait = options.character.State.portrait;
            return true;
        }

        /// <summary>
        /// Gif换图时，保持Sprite判定为同一张，防止换图导致的Fungus崩溃
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(PortraitState), "SetPortraitImageBySprite", new Type[] { typeof(Sprite) })]
        public static void SetPortraitImageBySprite_Post(ref PortraitState __instance, Sprite portrait)
        {
            if (__instance.portraitImage == null)
                __instance.portraitImage = __instance.allPortraits.Find(x => x.sprite.name == portrait.name);
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
        /// 重定向等价Condition脚本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Condition")]
        public static bool ConditionRedirect(string name, ref bool __result)
        {
            if (!mapCondition.ContainsKey(name))
            {
                return true;
            }

            string script = File.ReadAllText(mapCondition[name]);
            var luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            bool result = false;
            luaEnv.DoLuaString(script, "tmp.Condition", false, delegate (DynValue res)
            {
                result = res.Boolean;
            });
            __result = result;
            Debug.Log($"ModSupport: run external Condition lua {name}, result={__result}");
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

            string script = File.ReadAllText(mapSwitch[name]);
            var luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            int result = 0;
            luaEnv.DoLuaString(script, "tmp.Switch", false, delegate (DynValue res)
            {
                result = (int)res.Number;
            });
            __result = result;
            Debug.Log($"ModSupport: run external Switch lua {name}, result={__result}");
            return false;
        }

        /// <summary>
        /// 重定向等价Position脚本
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Position")]
        public static bool PositionRedirect(string name, ref string __result)
        {
            if (!mapPosition.ContainsKey(name))
                return true;

            string script = File.ReadAllText(mapPosition[name]);
            var luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            string result = "";
            luaEnv.DoLuaString(script, "tmp.Position", false, delegate (DynValue res)
            {
                result = res.String;
            });
            __result = result;
            Debug.Log($"ModSupport: run external Position lua {name}, result={__result}");
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

        public class Gif
        {
            public List<Sprite> frames = new List<Sprite>();
            public List<float> delay = new List<float>();
            private float time = 0.0f;
            private int frame = 0;

            public Sprite Current => frames[frame];

            public void Reset()
            {
                time = 0.0f;
                frame = 0;
            }

            public void Update(Traverse t)
            {
                time += Time.deltaTime;
                if (time >= delay[frame])
                {
                    frame = (frame + 1) % frames.Count;
                    time = 0.0f;
                    t.SetValue(Current);
                }
            }
        }

        public class SpriteDesc
        {
            public Vector2 pivot = new Vector2(0, 0);
            public float pixelsPerUnit = 100.0f;
            public uint extrude = 0;
            public SpriteMeshType spriteType = SpriteMeshType.Tight;

            public static SpriteDesc _default = new SpriteDesc();
        }
        
        public static Sprite LoadSprite(string filePath, string specificName = null)
        {
            if (cacheSprite.ContainsKey(filePath))
                return cacheSprite[filePath];
            if (!File.Exists(filePath))
                return null;
            var name = string.IsNullOrEmpty(specificName) ? Path.GetFileNameWithoutExtension(filePath) : specificName;
            if (mapGif.TryGetValue(name, out Gif gifFound))
            {
                gifFound.Reset();
                return gifFound.Current;
            }
            var ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".png" || ext == ".jpg")
            {
                byte[] data = File.ReadAllBytes(filePath);
                Texture2D tex2D = new Texture2D(2, 2);
                if (tex2D.LoadImage(data))
                {
                    var spriteJsonPath = filePath.Replace(ext, ".json");
                    SpriteDesc desc = SpriteDesc._default;
                    if (File.Exists(spriteJsonPath))
                        desc = JsonConvert.DeserializeObject<SpriteDesc>(File.ReadAllText(spriteJsonPath));
                    Sprite sprite = Sprite.Create(tex2D, new Rect(0, 0, tex2D.width, tex2D.height), desc.pivot, desc.pixelsPerUnit, desc.extrude, desc.spriteType);
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
                        tex2D.name = name;
                        SpriteDesc desc = SpriteDesc._default;
                        Sprite sprite = Sprite.Create(tex2D, new Rect(0, 0, tex2D.width, tex2D.height), desc.pivot, desc.pixelsPerUnit, desc.extrude, desc.spriteType);
                        sprite.name = tex2D.name;
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
