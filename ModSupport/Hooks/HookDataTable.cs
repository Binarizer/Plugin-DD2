using BepInEx.Unity.Mono;
using HarmonyLib;
using Mortal.Battle;
using Mortal.Combat;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBB.Framework.Data;
using OBB.Framework.Extensions;
using OBB.Framework.ScriptableEvent;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mortal
{
    // 数据表支持（ScriptableObject）
    public class HookDataTable : IHook
    {
        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }

        public void OnRegister(BaseUnityPlugin plugin)
        {
            SceneManager.sceneLoaded += InjectSoMods;
        }

        public void OnUpdate()
        {
        }

        public readonly static Dictionary<string, string> mapPortrait = new Dictionary<string, string>();

        public void InjectSoMods(Scene scene, LoadSceneMode sceneType)
        {
            if (scene == null)
                return;

            Debug.Log($"Scene Loaded: {scene.name}");

            if (!InjectedTitle && scene.name == "Title")
            {
                // 开始菜单加载时插入改动
                SoInject(SoTypes_Title);
                if (mapPortrait.Count > 0)
                    LinkSocialPortraits();
                InjectedTitle = true;
            }

            if (!InjectedCombat && scene.name == "Combat")
            {
                // 决斗时插入改动
                SoInject(SoTypes_Combat);
                if (mapPortrait.Count > 0)
                    LinkCombatPortraits();
                InjectedCombat = true;
            }

            if (!InjectedStory && scene.name == "Story")
            {
                // 剧本时插入改动
                SoInject(SoTypes_Story);
                if (mapPortrait.Count > 0)
                    LinkStoryPortraits();
                InjectedStory = true;
            }
        }

        /// <summary>
        /// 列传头像兼容初版规则
        /// </summary>
        void LinkSocialPortraits()
        {
            var datas = Resources.FindObjectsOfTypeAll<RelationshipStat>();
            foreach (var file in mapPortrait)
            {
                var param = file.Key.Split('_');
                if (param.Length != 2 || param[1].ToLower() != "normal")
                    continue;
                var matches = datas.Where(x => x.Type.GetStringValue() == param[0]);
                foreach (var match in matches)
                {
                    var sprite = HookMods.LoadSprite(file.Value);
                    if (sprite != null)
                        Traverse.Create(match).Field("_avatar").SetValue(sprite);
                }
            }
        }

        /// <summary>
        /// 战斗状态头像兼容初版规则
        /// </summary>
        void LinkCombatPortraits()
        {
            var combatStats = Resources.FindObjectsOfTypeAll<CombatStat>();
            foreach (var file in mapPortrait)
            {
                var param = file.Key.Split('_');
                if (param.Length != 2 || param[1].ToLower() != "normal")
                    continue;
                var matches = combatStats.Where(x => x.Name == param[0]);
                foreach (var match in matches)
                {
                    var sprite = HookMods.LoadSprite(file.Value);
                    if (sprite != null)
                        match.StatusAvatar = sprite;
                }
            }
        }

        /// <summary>
        /// 剧本头像兼容初版规则
        /// </summary>
        void LinkStoryPortraits()
        {
            var mappings = Resources.FindObjectsOfTypeAll<StoryMappingItem>();
            var datas = Resources.FindObjectsOfTypeAll<StoryCharacterData>();
            foreach (var file in mapPortrait)
            {
                var param = file.Key.Split('_');
                if (param.Length != 2)
                    continue;
                var mapping = mappings.FirstOrDefault(x => x.Value == param[0]);
                if (mapping == null)
                    continue;
                var data = datas.FirstOrDefault(x => Traverse.Create(x).Field("_mapping").GetValue<StoryMappingItem>() == mapping);
                if (data == null)
                    continue;
                var t = Traverse.Create(data);
                var field = t.Fields().FirstOrDefault(x => x.ToLower() == param[1].ToLower());
                if (field == null)
                    continue;
                var sprite = HookMods.LoadSprite(file.Value);
                if (sprite != null)
                    t.Field(field).SetValue(sprite);
            }
        }

        /// <summary>
        /// 界面UI主角头像，用player_normal替换
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAvatarPanel), "OnEnable")]
        public static bool PlayerPortrait_Replace(ref PlayerAvatarPanel __instance)
        {
            if (!mapPortrait.TryGetValue("player_normal", out string path))
                return true;
            var sprite = HookMods.LoadSprite(path);
            if (sprite == null)
                return true;
            sprite.name = "player_normal";
            var image = Traverse.Create(__instance).Field("_avatarImage").GetValue<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.name = "player_normal";
            return false;
        }

        /// <summary>
        /// 整个程序集中存在的So类型总表
        /// </summary>
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
                    Assembly.GetAssembly(typeof(StoryMappingItem)), // OBB.Framework
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

        /// <summary>
        /// 插件维护的整个游戏的So列表，不再删除
        /// </summary>
        public static readonly Dictionary<Type, List<UnityEngine.Object>> SoMap = new Dictionary<Type, List<UnityEngine.Object>>();

        /// <summary>
        /// 获取当前内存所有特定类型So
        /// </summary>
        public static List<UnityEngine.Object> GetSoList(Type t, bool refresh = false)
        {
            if (!SoMap.ContainsKey(t))
            {
                SoMap.Add(t, new List<UnityEngine.Object>());
            }
            var list = SoMap[t];
            if (refresh)
            {
                foreach (var so in Resources.FindObjectsOfTypeAll(t))
                {
                    if (!list.Contains(so))
                    {
                        list.Add(so);
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// 导出数据表
        /// </summary>
        public static void ExportDataTables(string root)
        {
            var exportPath = Path.Combine(root, "DataTable");
            if (!Directory.Exists(exportPath))
            {
                Directory.CreateDirectory(exportPath);
            }

            foreach (var so_type in HookDataTable.SoTypes)
            {
                var soArray = Resources.FindObjectsOfTypeAll(so_type);
                Debug.Log($"Find so_type {so_type.Name}, count = {soArray.Length}");
                if (soArray.Length > 0) // 一个以上的才可谓“表”
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
                            File.WriteAllText(Path.Combine(dir, so.name + ".json"), ToJson(so, HookExporter.exSerializer).ToString());
                        }
                    }
                }
            }
        }

        static JToken ToJson(ScriptableObject obj, JsonSerializer serializer)
        {
            JObject token = new JObject();
            foreach (var field in HookExporter.GetSerializedFields(obj.GetType(), typeof(ScriptableObject)))
            {
                var value = field.GetValue(obj);
                if (value != null)
                {
                    token.Add(field.Name, JToken.FromObject(value, serializer));
                }
            }
            return token;
        }

        /// <summary>
        /// 通过内存数据，刷新SoMap
        /// </summary>
        public static void RefreshSoMap()
        {
            foreach (Type soType in SoTypes)
            {
                GetSoList(soType, true);
            }
        }

        /// <summary>
        /// 按列出的指定类型，查找Mod里存在的So文件，并执行导入
        /// </summary>
        public void SoInject(IEnumerable<Type> specificTypes)
        {
            RefreshSoMap();
            foreach (Type soType in specificTypes)
            {
                Debug.Log($"DataTable: process mod type {soType.Name}");
                var soNames = FindModSoNames($"DataTable/{soType.Name}");
                foreach (var soName in soNames)
                {
                    RecursiveParseSo(soType, soName);
                }
                Debug.Log($"DataTable: {soNames.Count()} was processed.");
            }
        }

        /// <summary>
        /// 获取符合相对路径的So列表（同名则只取首个）
        /// </summary>
        public static IEnumerable<string> FindModSoNames(string dir)
        {
            HashSet<string> soNames = new HashSet<string>();
            foreach (var modPath in HookMods.ModPaths)
            {
                var fullPath = Path.Combine(modPath, dir);
                if (Directory.Exists(fullPath))
                {
                    var jsonFiles = Directory.EnumerateFiles(fullPath, "*.json", SearchOption.TopDirectoryOnly);
                    foreach (var file in jsonFiles)
                    {
                        var uniqueName = Path.GetFileNameWithoutExtension(file);
                        if (soNames.Contains(uniqueName))
                            continue;
                        soNames.Add(uniqueName);
                    }
                }
            }
            return soNames;
        }

        /// <summary>
        /// 防止循环处理，将已处理过的存一个表
        /// </summary>
        public static HashSet<string> FileProcessed = new HashSet<string>();

        /// <summary>
        /// 递归解析函数
        /// </summary>
        public static ScriptableObject RecursiveParseSo(Type soType, string soName)
        {
            bool isDefault = false;
            var soList = GetSoList(soType);
            var o = soList.FirstOrDefault(item => item.name == soName); // 先尝试加载官方数据
            if (o == null)
            {
                o = ScriptableObject.CreateInstance(soType);
                o.name = soName;
                soList.Add(o);
                Debug.Log($"ParseSo: Add New {soType.Name}/{soName}");
                isDefault = true;
            }
            string modFilePath = HookMods.FindModFile($"DataTable/{soType.Name}/{soName}.json");
            if (!string.IsNullOrEmpty(modFilePath) && !FileProcessed.Contains(modFilePath))
            {
                FileProcessed.Add(modFilePath);
                ScriptableObject so = o as ScriptableObject;
                FromJsonString(ref so, File.ReadAllText(modFilePath)); // 再应用Mod修改部分
                isDefault = false;
            }
            if (isDefault)
            {
                Debug.LogWarning($"RecursiveParseSo: {soType.Name}/{soName}.json not found, use default values!");
            }
            return o as ScriptableObject;
        }

        /// <summary>
        /// 通过Json导入So数据
        /// </summary>
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
                tField.SetValue(field.Value.ToObject(tField.GetValueType(), HookExporter.exSerializer));
            }
        }

        public static bool InjectedTitle = false;
        /// <summary>
        /// 进入Title场景即被完整加载的So类型集合
        /// </summary>
        public static Type[] SoTypes_Title => _soTypes_Title;
        private static Type[] _soTypes_Title = new Type[]
        {
            typeof(BattleSkillData),
            typeof(BattleTeamStat),
            typeof(Book),
            typeof(BookDataCollection),
            typeof(BookLevelStatConvertData),
            typeof(FacilityAdditionData),
            typeof(FacilityItemData),
            typeof(FacilityLevelData),
            typeof(FlagData),
            typeof(GameStat),
            typeof(ItemDataCollection),
            typeof(LibraryAwardData),
            typeof(LibraryItemCollection),
            typeof(LibraryItemData),
            typeof(MartialLearnCondition),
            typeof(MartialLevelData),
            typeof(Misc),
            typeof(MissionCheckData),
            typeof(MissionData),
            typeof(PlayerTalentDataCollection),
            typeof(PlayerTalentData),
            typeof(RelationshipStat),
            typeof(ShopItemsData),
            typeof(ShopItemTimeData),
            typeof(SpecialItem),
            typeof(StoryMappingItem),
            typeof(StringGameEvent),
            typeof(UpgradeItemCollectionData),
            typeof(UpgradeItemData),
            typeof(UpgradeItemStatConvertData),
            typeof(WeaponEffectCollectionData),
            typeof(WeaponParalysisEffectStatData),
            typeof(WeaponPoisonEffectStatData),
        };

        static bool InjectedCombat = false;
        /// <summary>
        /// 进入Combat场景即被完整加载的So类型集合
        /// 基本都是以Combat开头的
        /// </summary>
        public static Type[] SoTypes_Combat
        {
            get
            {
                if (_soTypes_Combat == null)
                {
                    _soTypes_Combat = SoTypes.Where(t => t.Name.StartsWith("Combat")).ToArray();
                    Debug.Log($"_soTypes_Combat.Count={_soTypes_Combat}");
                }
                return _soTypes_Combat;
            }
        }
        private static Type[] _soTypes_Combat = null;

        public static bool InjectedStory = false;
        public static Type[] SoTypes_Story => _soTypes_Story;
        private static Type[] _soTypes_Story = new Type[]
        {
            typeof(DiceResultConfig),
            typeof(DiceResultData),
            typeof(PositionResultConfig),
            typeof(PositionResultData),
            typeof(ConditionResultConfig),
            typeof(ConditionResultData),
            typeof(SwitchResultConfig),
            typeof(SwitchResultData),
            typeof(StoryCharacterConfig),
            typeof(StoryCharacterData),
            typeof(SpriteCollectionData),
            typeof(SpriteData),
        };
    }
}
