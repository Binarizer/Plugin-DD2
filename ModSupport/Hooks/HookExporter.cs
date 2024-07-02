using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Lean.Localization;
using Mortal.Battle;
using Mortal.Combat;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OBB.Framework.Attributes;
using OBB.Framework.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using static Mono.Security.X509.X520;

namespace Mortal
{
    public class HookExporter : IHook
    {
        private static ConfigEntry<bool> exportEnable;
        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        public static JsonSerializer exSerializer = null;

        public void OnRegister(BaseUnityPlugin plugin)
        {
            exportEnable = plugin.Config.Bind("Enable Export", "Enable Export", false, "Enable Export");

            exSerializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.Indented,
                ContractResolver = new SerializeFieldContractResolver()
            };
            exSerializer.Converters.Add(new StringEnumConverter());
            exSerializer.Converters.Add(new ScriptableObjectConverter());
            exSerializer.Converters.Add(new StatValueReferenceConverter());

            exportEnable = plugin.Config.Bind("Enable Export", "Enable Export", false, "Enable Export");
        }

        private bool f3 = false;
        private bool f4 = false;
        private bool f5 = false;
        private bool f6 = false;

        public void OnUpdate()
        {
            if (exportEnable.Value)
            {
                // 导出等价Lua脚本
                bool f3_pressed = Keyboard.current.f3Key.IsPressed();
                if (f3_pressed && !f3)
                {
                    Debug.Log("F3 is pressed");
                    ExportLuaEquivalents();
                }
                f3 = f3_pressed;

                // 导出本地化文件
                bool f4_pressed = Keyboard.current.f4Key.IsPressed();
                if (f4_pressed && !f4)
                {
                    Debug.Log("F4 is pressed");
                    ExportLocalizations();
                }
                f4 = f4_pressed;

                // 导出头像库
                bool f5_pressed = Keyboard.current.f5Key.IsPressed();
                if (f5_pressed && !f5)
                {
                    Debug.Log("F5 is pressed");
                    ExportPortraits();
                }
                f5 = f5_pressed;

                // 导出数据表
                bool f6_pressed = Keyboard.current.f6Key.IsPressed();
                if (f6_pressed && !f6)
                {
                    Debug.Log("F6 is pressed");
                    ExportDataTables();
                }
                f6 = f6_pressed;
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

        static StatModifyManager statModifyManager = null;
        /// <summary>
        /// 挂接StatModifyManager
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(StatModifyManager), "Awake")]
        public static void StatModifyManager_Get(ref StatModifyManager __instance)
        {
            statModifyManager = __instance;
        }

        public static void ExportLuaEquivalents()
        {
            Traverse cpm = Traverse.Create(CheckPointManager.Instance);
            {
                var exportPath = "./LuaEquivalent/Position/";
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                }
                var list = cpm.Field("_position").Field("_list").GetValue<List<PositionResultData>>();
                foreach (var positionData in list)
                {
                    var lua = positionData.ToLua(true);
                    if (!string.IsNullOrEmpty(lua))
                        File.WriteAllText(Path.Combine(exportPath, positionData.name + ".lua"), lua);
                }
                Debug.Log($"export {list.Count} positions");
            }
            {
                var exportPath = "./LuaEquivalent/Condition/";
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                }
                var list = cpm.Field("_condition").Field("_list").GetValue<List<ConditionResultData>>();
                Debug.Log("_conditionList = " + list?.GetType());
                foreach (var conditionData in list)
                {
                    var lua = conditionData.ToLua(true);
                    if (!string.IsNullOrEmpty(lua))
                        File.WriteAllText(Path.Combine(exportPath, conditionData.name + ".lua"), lua);
                }
                Debug.Log($"export {list.Count} conditions");
            }
            {
                var exportPath = "./LuaEquivalent/Switch/";
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
                        var lua = switchData.ToLua(true);
                        if (!string.IsNullOrEmpty(lua))
                            File.WriteAllText(Path.Combine(exportPath, switchData.name + ".lua"), lua);
                    }
                    Debug.Log($"export {switchConfig.List.Count} switches");
                }
            }
        }

        public static List<Type> SoTypes
        {
            get
            {
                if (_so_types != null)
                {
                    return _so_types;
                }
                Assembly[] assemblies = new Assembly[]
                {
                Assembly.GetAssembly(typeof(MissionData)), // Mortal.Core
                Assembly.GetAssembly(typeof(DiceResultData)),// Mortal.Story
                Assembly.GetAssembly(typeof(CombatLevel)), // Mortal.Combat
                Assembly.GetAssembly(typeof(DropItem)), // Mortal.Battle
                Assembly.GetAssembly(typeof(FreePositionData)), // Mortal.Free
                };
                _so_types = new List<Type>();
                foreach (var assembly in assemblies)
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!type.IsAbstract && !type.IsGenericType && typeof(ScriptableObject).IsAssignableFrom(type))
                        {
                            Debug.Log($"Find ScriptableObject type = {type.Name}");
                            _so_types.Add(type);
                        }
                    }
                }
                return _so_types;
            }
        }
        private static List<Type> _so_types = null;

        public static void ExportDataTables()
        {
            var exportPath = "./DataTable/";
            if (!Directory.Exists(exportPath))
            {
                Directory.CreateDirectory(exportPath);
            }

            foreach (var so_type in SoTypes)
            {
                var soArray = Resources.FindObjectsOfTypeAll(so_type);
                Debug.Log($"Find so_type {so_type.Name}, count = {soArray.Length}");
                if (soArray.Length > 1) // 一个以上的才可谓“表”
                {
                    var dir = Path.Combine(exportPath, so_type.Name);
                    foreach (var item in soArray)
                    {
                        ScriptableObject so = item as ScriptableObject;
                        if (so != null && !(so is CombatStateEffectUnitScriptable))
                        {
                            if (!Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            File.WriteAllText(Path.Combine(dir, so.name + ".json"), ToJson(so, exSerializer).ToString());
                        }
                    }
                }
            }
        }

        public static Dictionary<string, ScriptableObject> SoResolveLater = new Dictionary<string, ScriptableObject>();
        public static void FromJsonString(ref ScriptableObject obj, string jsonString)
        {
            JObject jobj = JObject.Parse(jsonString);
            var t = Traverse.Create(obj);
            foreach (var field in jobj.Properties())
            {
                var tField = t.Field(field.Name);
                if (tField == null)
                {
                    Debug.Log($"Warning: {t.GetValueType().Name} cannot find field {field.Name}, skip!");
                    continue;
                }
                tField.SetValue(field.Value.ToObject(tField.GetValueType(), exSerializer));
            }

            //Debug.Log(ToJson(obj, exSerializer).ToString());
        }

        static JToken ToJson(ScriptableObject obj, JsonSerializer serializer)
        {
            JObject token = new JObject();
            foreach (var field in GetSerializedFields(obj.GetType(), typeof(ScriptableObject)))
            {
                var value = field.GetValue(obj);
                if (value != null)
                {
                    token.Add(field.Name, JToken.FromObject(value, serializer));
                }
            }
            return token;
        }

        static bool IsSerializedField(FieldInfo field)
        {
            if (field.IsPublic)
                return true;

            foreach(var att in field.GetCustomAttributes(true))
            {
                if (att is SerializeField)
                {
                    return true;
                }
            }
            return false;
        }

        static readonly Dictionary<Type, List<FieldInfo>> SerialzedFieldCache = new Dictionary<Type, List<FieldInfo>>();

        static IEnumerable<FieldInfo> GetSerializedFields(Type type, Type baseType)
        {
            if (SerialzedFieldCache.TryGetValue(type, out List<FieldInfo> cache))
            {
                return cache;
            }

            var ret = new List<FieldInfo>();
            HashSet<string> nameSet = new HashSet<string>();
            while (type != baseType)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    if (!nameSet.Contains(field.Name) && IsSerializedField(field))
                    {
                        nameSet.Add(field.Name);
                        ret.Add(field);
                    }
                }
                type = type.BaseType;
            }
            Debug.Log($"GetSerializedFields: Type = {type.Name}, Field Count = {ret.Count}");
            return ret;
        }

        /// <summary>
        /// 输出公共和SerialzedField属性
        /// </summary>
        public class SerializeFieldContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var props = new List<JsonProperty>();
                foreach (var field in GetSerializedFields(type, typeof(object)))
                {
                    var prop = base.CreateProperty(field, memberSerialization);
                    prop.Writable = true;
                    prop.Readable = true;
                    props.Add(prop);
                }
                return props;
            }
        }

        /// <summary>
        /// ScriptableObject做索引处理
        /// 导出时只给类型和名字
        /// 导入时需通过类型和名字查找对应object
        /// </summary>
        public class ScriptableObjectConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return typeof(ScriptableObject).IsAssignableFrom(objectType);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                JObject jobj = JObject.Load(reader);
                var soType = (string)jobj["so.type"];
                var soName = (string)jobj["so.name"];
                Type realType = HookExporter.SoTypes.FirstOrDefault(item => item.Name == soType);
                if (realType != null && !objectType.IsAssignableFrom(realType))
                {
                    Debug.Log($"ReadJson: type unmatch, need {objectType.Name}, get {soType}!");
                    return null;
                }
                var soArray = Resources.FindObjectsOfTypeAll(realType);
                var so = soArray.FirstOrDefault(item => item.name == soName);
                if (so == null)
                {
                    Debug.Log($"ReadJson: {soType}.{soName} not found, add new one later");
                    so = ScriptableObject.CreateInstance(realType);
                    HookExporter.SoResolveLater.Add(soType + "." + soName, so as ScriptableObject);
                }
                return so;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                ScriptableObject item = value as ScriptableObject;
                var jobj = new JObject
                {
                    { "so.type", item.GetType().Name },
                    { "so.name", item.name }
                };
                jobj.WriteTo(writer);
            }
        }

        /// <summary>
        /// 这玩意填了一些多余的数据，该解析器会去掉
        /// </summary>
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
