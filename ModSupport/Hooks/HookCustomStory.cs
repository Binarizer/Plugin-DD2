using BepInEx.Unity.Mono;
using HarmonyLib;
using Mortal.Core;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ideafixxxer.CsvParser;
using Fungus;
using MoonSharp.Interpreter;
using Mortal.Story;
using Lean.Localization;

namespace Mortal
{
    // 自制故事
    public class HookCustomStory : IHook
    {
        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        public void OnRegister(PluginBinarizer plugin)
        {
            CsvParser parser = new CsvParser();
            foreach (var modPath in HookMods.ModPaths)
            {
                // 外部读取lua剧本
                string storyPath = Path.Combine(modPath, "story");
                if (Directory.Exists(storyPath))
                {
                    foreach (string file in Directory.EnumerateFiles(storyPath, "*.lua", SearchOption.AllDirectories))
                    {
                        var key = Path.GetFileNameWithoutExtension(file);
                        if (!mapStory.ContainsKey(key))
                        {
                            Debug.Log($"ModSupport: Add file {file}");
                            mapStory.Add(key, file);
                        }
                    }
                }
                // 读取自制故事入口
                string customStoryPath = Path.Combine(modPath, "CustomStory.csv");
                if (File.Exists(customStoryPath))
                {
                    var data = File.ReadAllText(customStoryPath);
                    var csvLines = parser.Parse(data);
                    foreach (var line in csvLines)
                    {
                        if (line.Length < 3)
                            continue;
                        var key = line[0];
                        if (string.IsNullOrEmpty(key) || mapStoryEntry.ContainsKey(key) || !mapStory.ContainsKey(key))
                            continue;
                        mapStoryEntry.Add(key, new StoryEntry { lua = key, title = line[1], auther = line[2] });
                    }
                    Debug.Log($"HookCustomStory: Add {csvLines.Length} lines to CustomStory.");
                }
            }
        }

        public readonly static Dictionary<string, string> mapStory = new Dictionary<string, string>();
        struct StoryEntry
        {
            public string lua;
            public string title;
            public string auther;
        };
        readonly static Dictionary<string, StoryEntry> mapStoryEntry = new Dictionary<string, StoryEntry>();
        static bool isSavePanel = true;
        static LoadGamePanel loadPanel = null;
        static bool inCustomStory = false;
        static Transform scrollContent = null;
        static List<GameObject> saveSlots = new List<GameObject>();
        static List<GameObject> customSlots = new List<GameObject>();
        static GameObject manualObj = null;
        static GameObject customObj = null;

        /// <summary>
        /// 添加自制故事按钮
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(LoadGamePanel), "Awake")]
        public static void PostAwake(LoadGamePanel __instance)
        {
            if (mapStoryEntry.Count == 0)
                return;

            loadPanel = __instance;
            saveSlots.Clear();
            customSlots.Clear();
            isSavePanel = true;

            manualObj = __instance.transform.Find("Manual").gameObject;
            customObj = GameObject.Instantiate(manualObj, manualObj.transform.parent);
            var pos = customObj.GetComponent<RectTransform>().anchoredPosition;
            customObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(pos.x + 400, pos.y);
            customObj.name = "CustomStory";
            Component.Destroy(customObj.GetComponent<LeanLocalizedText>());
            Component.Destroy(customObj.GetComponent<LeanLocalizedTextFont>());
            customObj.GetComponent<Text>().text = "自制剧情";
            customObj.GetComponent<Text>().color = Color.gray;

            var t = Traverse.Create(loadPanel);
            var panelSlots = t.Field("_saveSlots").GetValue<LoadSlotPanel[]>();
            scrollContent = panelSlots[0].transform.parent;
            var prefab = panelSlots[0].gameObject;
            var btnPrefab = prefab.transform.Find("Delete").gameObject;
            var btnSwitch = GameObject.Instantiate(btnPrefab, manualObj.transform.parent);
            btnSwitch.GetComponent<RectTransform>().anchoredPosition = new Vector2(1260, -250);
            Component.Destroy(btnSwitch.GetComponent<MenuToggleButton>());
            var btnComp = btnSwitch.GetComponent<Button>();
            btnComp.onClick.RemoveAllListeners();
            Traverse.Create(btnComp.onClick).Field("m_PersistentCalls").Method("Clear").GetValue();
            btnComp.onClick.AddListener(() => { ToggleSaveActive(); });
            var texts = btnSwitch.GetComponentsInChildren<Text>();
            foreach ( var text in texts )
            {
                Component.Destroy(text.GetComponent<LeanLocalizedText>());
                Component.Destroy(text.GetComponent<LeanLocalizedTextFont>());
            }
            texts[0].text = "切";
            texts[1].text = "换";

            foreach (var slot in panelSlots)
            {
                saveSlots.Add(slot.gameObject);
            }

            foreach( var entry in mapStoryEntry.Values )
            {
                var obj = GameObject.Instantiate(prefab, scrollContent);
                var slotPanel = obj.GetComponentInChildren<LoadSlotPanel>();
                UpdateCustomStorySlot(slotPanel, entry);
                customSlots.Add(obj);
                obj.SetActive(false);
            }
        }

        /// <summary>
        /// 点击事件
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(LoadSlotPanel), "OnTitleClick")]
        public static bool OnClick(LoadSlotPanel __instance)
        {
            var key = Traverse.Create(__instance).Field("_slot").GetValue<string>();
            Debug.Log($"HookCustomStory: OnTitleClick {key}");
            if (mapStoryEntry.ContainsKey(key))
            {
                StartCustomStory(key);
                inCustomStory = true;
                return false;
            }

            inCustomStory = false;
            return true;
        }

        /// <summary>
        /// 重定向剧本Lua文件
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(LuaManager), "ExecuteLuaScript")]
        public static bool LuaScriptRedirect(ref LuaManager __instance)
        {
            var t = Traverse.Create(__instance);
            var luaEnv = t.Field("_luaEnvironment").GetValue<LuaEnvironment>();
            string key = __instance.ScriptName;
            string friendlyName = key + ".LuaScript";
            string text = null;
            if (mapStory.ContainsKey(key))
            {
                Debug.Log($"HookCustomStory: run external story lua {key}");
                if (File.Exists(mapStory[key]))
                    text = File.ReadAllText(mapStory[key]);
            }
            else
            {
                TextAsset textAsset = Resources.Load<TextAsset>("Story/ChineseTraditional/" + key);
                if (textAsset != null)
                    text = textAsset.text;
            }
            if (text == null)
            {
                Debug.LogError("找不到劇情腳本 " + key);
                return false;
            }
            Closure fn = luaEnv.LoadLuaFunction(text, friendlyName);
            luaEnv.RunLuaFunction(fn, true, delegate (DynValue res)
            {
                var next = PlayerStatManagerData.Instance.CurrentStoryScript;
                Debug.Log($"HookCustomStory: lua finish {key}, next={next}");
                if (key == next)
                {
                    Debug.LogWarning($"腳本無出口！");
                    if (inCustomStory)
                    {
                        Debug.Log($"回主選單");
                        inCustomStory = false;
                        SceneController.Instance.LoadTitle();
                    }
                }
            });
            return false;
        }

        static void UpdateCustomStorySlot(LoadSlotPanel slotPanel, StoryEntry entry)
        {
            Traverse t = Traverse.Create(slotPanel);
            t.Field("_slot").SetValue(entry.lua);
            t.Field("_slotText").Property("text").SetValue("故事" + entry.lua);
            t.Field("_deleteButton").GetValue<Button>().gameObject.SetActive(false);
            t.Field("_newGamePlusIcon").GetValue<GameObject>().SetActive(false);
            t.Field("_focusObj").GetValue<GameObject>().SetActive(true);
            Text[] array = t.Field("_titleText").GetValue<Text[]>();
            for (int i = 0; i < array.Length; i++)
            {
                array[i].text = entry.title;
            }
            array = t.Field("_timeText").GetValue<Text[]>();
            for (int i = 0; i < array.Length; i++)
            {
                array[i].text = "by " + entry.auther;
            }
        }

        static void ToggleSaveActive()
        {
            isSavePanel = !isSavePanel;
            foreach (var slot in saveSlots)
            {
                manualObj.GetComponentInChildren<Text>().color = isSavePanel ? Color.white : Color.gray;
                slot.SetActive(isSavePanel);
            }
            foreach (var slot in customSlots)
            {
                customObj.GetComponentInChildren<Text>().color = isSavePanel ? Color.gray : Color.white;
                slot.SetActive(!isSavePanel);
            }
        }

        static void StartCustomStory(string key)
        {
            Debug.Log($"HookCustomStory: start {key}");
            SoundManager.Instance.StopMusic();
            MissionManagerData.Instance.ResetData();
            PlayerStatManagerData.Instance.ResetData();
            ItemDatabase.Instance.Reset();
            ShopDatabase.Instance.Reset();
            PlayerStatManagerData.Instance.SetStoryScript(key);
            PlayerStatManagerData.Instance.SetStartScript(key);
            PlayerStatManagerData.Instance.SetCurrentScene("Story");
            PlayerStatManagerData.Instance.SetGameTime(1, 4, MonthStageType.上旬);
            SceneController.Instance.LoadStory();
        }
    }
}
