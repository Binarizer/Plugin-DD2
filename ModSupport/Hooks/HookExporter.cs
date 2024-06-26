using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Lean.Localization;
using Mortal.Combat;
using Mortal.Core;
using Mortal.Story;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OBB.Framework.Attributes;
using OBB.Framework.Extensions;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mortal
{
    public class HookExporter : IHook
    {
        private static ConfigEntry<bool> exportEnable;
        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        public void OnRegister(BaseUnityPlugin plugin)
        {
            exportEnable = plugin.Config.Bind("Enable Export", "Enable Export", false, "Enable Export");
        }

        private bool f3 = false;
        private bool f4 = false;
        private bool f5 = false;

        public void OnUpdate()
        {
            if (exportEnable.Value)
            {
                bool f3_pressed = Keyboard.current.f3Key.IsPressed();
                if (f3_pressed && !f3)
                {
                    Debug.Log("F3 is pressed");
                    ExportDataTables();
                }
                f3 = f3_pressed;

                bool f4_pressed = Keyboard.current.f4Key.IsPressed();
                if (f4_pressed && !f4)
                {
                    Debug.Log("F4 is pressed");
                    ExportLocalizations();
                }
                f4 = f4_pressed;

                bool f5_pressed = Keyboard.current.f5Key.IsPressed();
                if (f5_pressed && !f5)
                {
                    Debug.Log("F5 is pressed");
                    ExportPortraits();
                }
                f5 = f5_pressed;
            }
        }

        public static void ExportLocalizations()
        {
            var exportPath = "./StringTable.csv";
            var sb = new StringBuilder();
            foreach (var pair in LeanLocalization.CurrentTranslations)
            {
                sb.AppendLine($"{pair.Key},\"{pair.Value.Data}\"");
            }
            File.WriteAllText(exportPath, sb.ToString());
        }

        public static void ExportFlags()
        {
            var exportPath = "./DataTable/FlagData.csv";
            var _flagList = MissionManagerData.Instance.FlagsCollection.ToList();
            Traverse smm = Traverse.Create(statModifyManager);
            var talkOptionFlagCollection = smm.Field("_talkOptionFlag").GetValue<FlagCollectionData>();
            _flagList.Add(talkOptionFlagCollection);
            StringBuilder sb = new StringBuilder();
            foreach (var flagCollection in _flagList)
            {
                foreach (var flagData in flagCollection.List)
                {
                    var id = flagData.name;
                    var devNote = Traverse.Create(flagData).Field("_devNote").GetValue<string>();
                    sb.AppendLine($"{id},\"{devNote}\"");
                }
                Debug.Log($"export {flagCollection.List.Count} flags, name = {flagCollection.name}");
            }
            File.WriteAllText(exportPath, sb.ToString());
        }

        public static void ExportItems()
        {
            var exportPath = "./DataTable/ItemData.csv";
            var itemList = new List<ItemData>();
            itemList.AddRange(ItemDatabase.Instance.Books.List);
            itemList.AddRange(ItemDatabase.Instance.Miscs.List);
            itemList.AddRange(ItemDatabase.Instance.Special.List);
            StringBuilder sb = new StringBuilder();
            foreach (var item in itemList)
            {
                var id = Traverse.Create(item).Field("_id").GetValue<string>();
                var devNote = Traverse.Create(item).Field("_devNote").GetValue<string>();
                var type = item.ItemType.GetStringValue();
                sb.AppendLine($"{id},\"{devNote}\",{type}");
            }
            File.WriteAllText(exportPath, sb.ToString());
        }
        public static void ExportSkills()
        {
            var exportPath = "./DataTable/PlayerTalentData.csv";
            var itemList = PlayerStatManagerData.Instance.Talents.List;
            StringBuilder sb = new StringBuilder();
            foreach (var item in itemList)
            {
                var id = item.Id;
                var name = LeanLocalization.GetTranslationText(item.GetIdKey());
                var desc = LeanLocalization.GetTranslationText(item.GetDescKey());
                sb.AppendLine($"{id},\"{name}\",\"{desc}\"");
            }
            File.WriteAllText(exportPath, sb.ToString());
        }

        public static void ExportDevelops()
        {
            var exportPath = "./DataTable/UpgradeItemData.csv";
            var itemList = LuaExt.GetAllDevelopItems();
            StringBuilder sb = new StringBuilder();
            foreach (var item in itemList)
            {
                var id = Traverse.Create(item).Field("_key").GetValue<string>();
                var devNote = Traverse.Create(item).Field("_note").GetValue<string>();
                var type = item.ItemType.ToString();
                sb.AppendLine($"{id},\"{devNote}\",{type}");
            }
            File.WriteAllText(exportPath, sb.ToString());
        }

        public static void ExportCombats()
        {
            var exportPath = "./DataTable/CombatLevel.csv";
            var itemList = Traverse.Create(CombatManager.Instance).Field("_levelConfig").GetValue<CombatLevelConfig>().List;
            StringBuilder sb = new StringBuilder();
            foreach (CombatLevel item in itemList)
            {
                var id = item.name;
                var desc = item.Desc;
                var enemy = item.EnemyStat?.name;
                sb.AppendLine($"{id},\"{desc}\",\"{enemy}\"");
            }
            File.WriteAllText(exportPath, sb.ToString());
        }


        static bool exportJson = false;
        static bool exportLua = true;
        static bool exportingPortaits = false;
        static string exportPortraitDir = null;
        public static void ExportPortraits()
        {
            exportingPortaits = true;
            exportPortraitDir = "./Portraits/";
            if (!Directory.Exists(exportPortraitDir))
                Directory.CreateDirectory(exportPortraitDir);
            var characterConfig = Traverse.Create(CharacterPlaceholder.Instance).Field("_config").GetValue<StoryCharacterConfig>();
            foreach(var characterData in characterConfig.List)
            {
                Debug.Log($"Export {characterData.Id}");
                if (characterData.DefaultPortrait)
                {
                    string defaultFileName = $"{characterData.Id}.png";
                    ExportPortrait(defaultFileName, characterData.DefaultPortrait);
                }
                characterData.GetPortraitList();
            }
            exportingPortaits = false;
        }

        static Texture2D MakeReadable(Texture2D source)
        {
            RenderTexture renderTex = RenderTexture.GetTemporary(
                        source.width,
                        source.height,
                        0,
                        RenderTextureFormat.Default,
                        RenderTextureReadWrite.Linear);

            Graphics.Blit(source, renderTex);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;
            Texture2D readableText = new Texture2D(source.width, source.height);
            readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            readableText.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);
            return readableText;
        }

        static Dictionary<PortraitType, string> portraitTypeToString = null;
        /// <summary>
        /// 导出头像
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(StoryCharacterData), "GetPortraitSprite", new Type[] { typeof(PortraitType) })]
        public static void GetPortraitSprite_Export(ref StoryCharacterData __instance, ref Sprite __result, PortraitType type)
        {
            if (!exportEnable.Value || !exportingPortaits)
                return;

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
            string portraitFileName = $"{__instance.Id}_{portraitTypeName}.png";
            Debug.Log($"ModSupport: GetPortraitSprite {portraitFileName}");
            if (__result != null)
            {
                ExportPortrait(portraitFileName, __result);
            }
        }

        public static void ExportPortrait(string filename, Sprite sprite)
        {
            var exportPath = Path.Combine(exportPortraitDir, filename);
            if (File.Exists(exportPath))
                return;
            File.WriteAllBytes(exportPath, MakeReadable(sprite.texture).EncodeToPNG());
        }

#if EXPORT_STAT_VARS
        static HashSet<string> statGroupSet = new HashSet<string>();
        static List<StatGroupVariable> statGroupList = new List<StatGroupVariable>();
        static int statGroupSize = 0;
#endif

        static StatModifyManager statModifyManager = null;
        /// <summary>
        /// 挂接StatModifyManager
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(StatModifyManager), "Awake")]
        public static void StatModifyManager_Get(ref StatModifyManager __instance)
        {
            statModifyManager = __instance;
        }

        public static void ExportDataTables()
        {
            JsonSerializer serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.Indented
            };
            serializer.ContractResolver = new SerializeFieldContractResolver();
            serializer.Converters.Add(new StringEnumConverter());
            serializer.Converters.Add(new ScriptableObjectConverter());
            serializer.Converters.Add(new StatValueReferenceConverter());

#if EXPORT_STAT_VARS
            statGroupSet.Clear();
            statGroupList.Clear();
            statGroupSize = 0;
#endif
            Traverse cpm = Traverse.Create(CheckPointManager.Instance);
            {
                var exportPath = "./DataTable/Condition/";
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                }
                var list = cpm.Field("_condition").Field("_list").GetValue<List<ConditionResultData>>();
                Debug.Log("_conditionList = " + list?.GetType());
                foreach (var conditionData in list)
                {
                    var json = ToJson(conditionData, serializer);
                    if (exportJson && !string.IsNullOrEmpty(json))
                        File.WriteAllText(Path.Combine(exportPath, conditionData.name + ".json"), json);
                    var lua = conditionData.ToLua();
                    if (exportLua && !string.IsNullOrEmpty(lua))
                        File.WriteAllText(Path.Combine(exportPath, conditionData.name + ".lua"), lua);
                }
                Debug.Log($"export {list.Count} conditions");
            }
            {
                var exportPath = "./DataTable/Switch/";
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                }
                var _switchList = cpm.Field("_switchList").GetValue<SwitchResultConfig[]>();
                Debug.Log("_switchList = " + _switchList?.GetType());
                foreach (var switchConfig in _switchList)
                {
                    foreach(var switchData in switchConfig.List)
                    {
                        var json = ToJson(switchData, serializer);
                        if (exportJson && !string.IsNullOrEmpty(json))
                            File.WriteAllText(Path.Combine(exportPath, switchData.name + ".json"), json);
                        var lua = switchData.ToLua();
                        if (exportLua && !string.IsNullOrEmpty(lua))
                            File.WriteAllText(Path.Combine(exportPath, switchData.name + ".lua"), lua);
                    }
                    Debug.Log($"export {switchConfig.List.Count} switches");
                }
            }
#if EXPORT_STAT_VARS
            {
                // 该表需要通过前面的几张表格动态收集，并没有一个整体集合
                var exportPath = "./StatVariable/";
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                }
                for (int i = 0; i < statGroupSize; ++i)
                {
                    var statGroupVar = statGroupList[i];
                    var json = ToJson(statGroupVar, serializer);
                    if (exportJson && !string.IsNullOrEmpty(json))
                        File.WriteAllText(Path.Combine(exportPath, statGroupVar.name + ".json"), json);
                    var lua = statGroupVar.ToLua();
                    if (exportLua && !string.IsNullOrEmpty(lua))
                        File.WriteAllText(Path.Combine(exportPath, statGroupVar.name + ".lua"), lua);
                }
                Debug.Log($"export {statGroupSize} statGroupVars");
            }
#endif

            ExportDevelops();
            ExportFlags();
            ExportItems();
            ExportSkills();
            ExportCombats();
        }

        static string ToJson(ScriptableObject obj, JsonSerializer serializer)
        {
            JObject token = new JObject();
            foreach (var field in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (IsSerializedField(field))
                {
                    token.Add(field.Name, JToken.FromObject(field.GetValue(obj), serializer));
                }
            }
            return token.ToString();
        }

        static bool IsSerializedField(FieldInfo field)
        {
            foreach(var att in field.GetCustomAttributes(true))
            {
                if (att is SerializeField)
                {
                    return true;
                }
            }
            return false;
        }

        public class SerializeFieldContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var props = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .Where(f => f.IsPublic || IsSerializedField(f))
                                .Select(f => base.CreateProperty(f, memberSerialization))
                            .ToList();
                props.ForEach(p => { p.Writable = true; p.Readable = true; });
                return props;
            }
        }

        public class ScriptableObjectConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return typeof(ScriptableObject).IsAssignableFrom(objectType);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                ScriptableObject item = value as ScriptableObject;
                JValue jvalue = (JValue)item.name;
                jvalue.WriteTo(writer);

#if EXPORT_STAT_VARS
                if (value.GetType() == typeof(StatGroupVariable))
                {
                    if (!statGroupSet.Contains(item.name))
                    {
                        statGroupSet.Add(item.name);
                        statGroupList.Add((StatGroupVariable)item);
                        statGroupSize++;
                    }
                }
#endif
            }
        }
        public class StatValueReferenceConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return typeof(StatValueReference) == objectType;
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            readonly Dictionary<StatCheckType, List<string>> fieldDict = new Dictionary<StatCheckType, List<string>>
            {
                { StatCheckType.常數, new List<string> { "_constant" } },
                { StatCheckType.角色數值, new List<string> { "_stat" } },
                { StatCheckType.好感度, new List<string> { "_relationship" } },
                { StatCheckType.旗標, new List<string> { "_flag" } },
                { StatCheckType.書籍, new List<string> { "_book" } },
                { StatCheckType.雜物, new List<string> { "_misc" } },
                { StatCheckType.貴重品, new List<string> { "_specialItem" } },
                { StatCheckType.隨機值, new List<string> { "_randomMin", "_randomMax" } },
                { StatCheckType.數值群組, new List<string> { "_statGroup" } },
                { StatCheckType.遊戲時間, new List<string> { "_gameTime" } },
                { StatCheckType.技能, new List<string> { "_talentData" } },
                { StatCheckType.秘笈等級, new List<string> { "_book" } },
                { StatCheckType.已開發項目, new List<string> { "_upgradeItemData" } },
                { StatCheckType.開發項目等級, new List<string> { "_upgradeItemData" } },
                { StatCheckType.書籍精通, new List<string> { "_bookCollection" } },
                { StatCheckType.風雲史, new List<string> { "_libraryAchieve" } }
            };

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                StatValueReference statVal = (StatValueReference)value;
                var validFields = fieldDict[statVal.CheckType];
                var fields = typeof(StatValueReference).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .Where(f => (f.IsPublic || IsSerializedField(f)) && validFields.Contains(f.Name)).ToList();
                JObject token = new JObject
                {
                    { "_checkType", JToken.FromObject(statVal.CheckType, serializer) }
                };
                foreach ( var field in fields )
                {
                    token.Add(field.Name, JToken.FromObject(field.GetValue(value), serializer));
                }
                token.WriteTo(writer);
            }
        }
    }
}
