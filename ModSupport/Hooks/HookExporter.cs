using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Lean.Localization;
using Mortal.Core;
using Mortal.Story;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Mortal
{
    public class HookExporter : IHook
    {
        private static ConfigEntry<bool> exportEnable;
        private static ConfigEntry<string> exportDir;
        private static ConfigEntry<bool> exportImage;
        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        public static JsonSerializer exSerializer = null;

        public void OnRegister(BaseUnityPlugin plugin)
        {
            exportEnable = plugin.Config.Bind("Export", "Enable Export", false, "Enable Export");
            exportDir = plugin.Config.Bind("Export", "Export Dir", "./", "Export Dir");
            exportImage = plugin.Config.Bind("Export", "Export PNG", false, "Export PNG");

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
            exSerializer.Converters.Add(new SpriteConverter());
        }

        public void OnUpdate()
        {
            if (exportEnable.Value)
            {
                // 导出等价Lua脚本
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    Debug.Log("F3 is pressed");
                    ExportLuaEquivalents();
                }

                // 导出本地化文件
                if (Input.GetKeyDown(KeyCode.F4))
                {
                    Debug.Log("F4 is pressed");
                    ExportLocalizations();
                }

                // 导出头像库
                if (Input.GetKeyDown(KeyCode.F5) && exportImage.Value)
                {
                    Debug.Log("F5 is pressed");
                    //ExportPortraits();
                }

                // 导出数据表
                if (Input.GetKeyDown(KeyCode.F6))
                {
                    Debug.Log("F6 is pressed");
                    HookDataTable.ExportDataTables(exportDir.Value);
                }
            }
        }

        public static void ExportLocalizations()
        {
            var exportPath = Path.Combine(exportDir.Value, "StringTable.csv");
            var sb = new StringBuilder();
            foreach (var pair in LeanLocalization.CurrentTranslations)
            {
                sb.AppendLine($"{pair.Key},\"{pair.Value.Data}\"");
            }
            File.WriteAllText(exportPath, sb.ToString());
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

        public static void ExportSprite(string path, Sprite sprite)
        {
            if (File.Exists(path))
                return;
            File.WriteAllBytes(path, MakeReadable(sprite.texture).EncodeToPNG());
        }

        public static void ExportLuaEquivalents()
        {
            Traverse cpm = Traverse.Create(CheckPointManager.Instance);
            {
                var exportPath = Path.Combine(exportDir.Value, "LuaEquivalent/Position");
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
                var exportPath = Path.Combine(exportDir.Value, "LuaEquivalent/Condition");
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
                var exportPath = Path.Combine(exportDir.Value, "LuaEquivalent/Switch");
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

        public static IEnumerable<FieldInfo> GetSerializedFields(Type type, Type baseType)
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
                Type realType = HookDataTable.SoTypes.FirstOrDefault(type => type.Name == soType);
                if (!objectType.IsAssignableFrom(realType))
                {
                    Debug.LogError($"ReadJson: type unmatch, need {objectType.Name}, get {realType?.Name}!");
                    return null;
                }
                return HookDataTable.RecursiveParseSo(realType, soName);
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
                StatValueReference ret = new StatValueReference();
                var t = Traverse.Create(ret);
                JObject jobj = JObject.Load(reader);
                foreach (var prop in jobj)
                {
                    var field = t.Field(prop.Key);
                    if (field != null)
                    {
                        field.SetValue(prop.Value.ToObject(field.GetValueType(), serializer));
                    }
                }
                return ret;
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
        /// <summary>
        /// 图片要特殊处理
        /// </summary>
        public class SpriteConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return typeof(Sprite) == objectType;
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                string fullPath = HookMods.FindModFile(reader.Value.ToString());
                if (string.IsNullOrEmpty(fullPath))
                    return existingValue;
                return HookMods.LoadSprite(fullPath);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Sprite sprite = (Sprite)value;
                if (sprite == null)
                {
                    return;
                }
                JToken jobj = $"Sprite/{sprite.name}.png";
                jobj.WriteTo(writer);

                if (exportImage.Value)
                {
                    var exportPath = Path.Combine(exportDir.Value, "Sprite");
                    if (!Directory.Exists(exportPath))
                    {
                        Directory.CreateDirectory(exportPath);
                    }
                    string spriteName = $"{exportPath}/{sprite.name}.png";
                    Debug.Log($"ModSupport: Export Sprite {spriteName}");
                    ExportSprite(spriteName, sprite);
                }
            }
        }
    }
}
