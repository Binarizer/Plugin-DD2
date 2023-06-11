using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using HarmonyLib;
using Assets.Code.Campaign;
using Assets.Code.Utils;
using Assets.Code.Utils.Serialization;
using Assets.Code.Resource;
using Assets.Code.Actor;
using System.Collections;

namespace DD2
{
    public static class ExternalResourceManager
    {
        /// <summary>
        /// External Sprite Container
        /// </summary>
        readonly static Dictionary<string, Sprite> ExternalSprites = new Dictionary<string, Sprite>();

        /// <summary>
        /// ResourceActor DB
        /// </summary>
        public static ResourceDatabaseActors ResourceActorDb = null;

        /// <summary>
        /// ResourceSkill DB
        /// </summary>
        public static ResourceDatabaseSkills ResourceSkillDb = null;

        /// <summary>
        /// Modify ResourceActor
        /// </summary>
        public static readonly ResourceDatabaseText ResourceActorModify = new ResourceDatabaseText();

        /// <summary>
        /// Inherit ResourceActor
        /// </summary>
        public static readonly ResourceDatabaseText ResourceActorInherit = new ResourceDatabaseText();

        /// <summary>
        /// Modify ResourceSkill
        /// </summary>
        public static readonly ResourceDatabaseText ResourceSkillModify = new ResourceDatabaseText();

        /// <summary>
        /// Inherit ResourceSkill
        /// </summary>
        public static readonly ResourceDatabaseText ResourceSkillInherit = new ResourceDatabaseText();

        /// <summary>
        /// Unity Create Sprite
        /// </summary>
        public static Sprite CreateSpriteFromPath(string FilePath, float PixelsPerUnit = 100.0f)
        {
            System.Console.WriteLine($"Sprite FilePath={FilePath}, Exist={File.Exists(FilePath)}");
            Texture2D SpriteTexture = LoadTexture(FilePath);
            Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), PixelsPerUnit);
            return NewSprite;
        }

        /// <summary>
        /// Unity Load Tex2D
        /// </summary>
        public static Texture2D LoadTexture(string FilePath)
        {
            Texture2D Tex2D;
            byte[] FileData;

            if (File.Exists(FilePath))
            {
                Tex2D = new Texture2D(32, 32);
                FileData = File.ReadAllBytes(FilePath);
                System.Console.WriteLine($"ReadData length={FileData?.Length}");
                if (Tex2D.LoadImage(FileData))
                    return Tex2D;
            }
            return null;
        }

        public static void AddModResources(string modPath)
        {
            // 1. UI sprites
            var spriteDir = Path.Combine(modPath, "Sprites");
            if (Directory.Exists(spriteDir))
            {
                string[] sprites = Directory.GetFiles(spriteDir, "*.png");
                foreach (var spritePath in sprites)
                {
                    var sprite = CreateSpriteFromPath(spritePath);
                    AddSprite(Path.GetFileNameWithoutExtension(spritePath), sprite);
                }
            }
        }

        private readonly static bool resourceExport = false;

        public static void ProcessModResources()
        {
            // 1. Resource Source
            ResourceActorDb = SingletonMonoBehaviour<CampaignBhv>.Instance.ResourceDatabaseActors;
            ResourceSkillDb = SingletonMonoBehaviour<CampaignBhv>.Instance.ResourceDatabaseSkills;
            var tDbActor = Traverse.Create(ResourceActorDb);
            var tDbSkill = Traverse.Create(ResourceSkillDb);
            int ActorCount = ResourceActorDb.GetNumberOfResources();
            int SkillCount = ResourceSkillDb.GetNumberOfResources();
            var ActorList = tDbActor.Field("m_Resources").GetValue<List<ResourceActor>>();
            var ActorDict = tDbActor.Field("m_ResourcesDictionary").GetValue<Dictionary<string, ResourceActor>>();
            var SkillList = tDbSkill.Field("m_Resources").GetValue<List<ResourceSkillBase>>();
            var SkillDict = tDbSkill.Field("m_ResourcesDictionary").GetValue<Dictionary<string, ResourceSkillBase>>();
            System.Console.WriteLine($"ResourceActor count = {ActorCount}");
            System.Console.WriteLine($"ResourceSkill count = {SkillCount}");
            if (resourceExport)
            {
                string dirActor = Path.Combine(Environment.CurrentDirectory, "ResourceActors");
                if (!Directory.Exists(dirActor))
                    Directory.CreateDirectory(dirActor);
                for (int i = 0; i < ActorCount; ++i)
                {
                    var resourceActor = ResourceActorDb.GetResourceAtIndex(i);
                    string path = Path.Combine(dirActor, resourceActor.name + ".csv");
                    ResourceToCsv(resourceActor, out string originalText);
                    File.WriteAllText(path, originalText);
                }

                string dirSkill = Path.Combine(Environment.CurrentDirectory, "ResourceSkills");
                if (!Directory.Exists(dirSkill))
                    Directory.CreateDirectory(dirSkill);
                for (int i = 0; i < SkillCount; ++i)
                {
                    var resourceSkill = ResourceSkillDb.GetResourceAtIndex(i);
                    string path = Path.Combine(dirSkill, resourceSkill.name + ".csv");
                    ResourceToCsv(resourceSkill, out string originalText);
                    File.WriteAllText(path, originalText);
                }
            }

            // 2. ResourceSkill Modify
            ResourceSkillModify.m_FileTypeFilters = new List<string> { "ResourceSkillModify" };
            int ModifySkillCount = ResourceSkillModify.GetNumberOfResources();
            System.Console.WriteLine($"ResourceSkillModify count = {ModifySkillCount}");
            for (int i = 0; i < ModifySkillCount; i++)
            {
                var data = ResourceSkillModify.GetResourceAtIndex(i);
                var resourceSkill = ResourceSkillDb.GetResource(data.m_Name);
                if (resourceSkill)
                {
                    CsvToResource(data.m_Data, resourceSkill);
                }
            }

            // 3. ResourceSkill Inherit
            ResourceSkillInherit.m_FileTypeFilters = new List<string> { "ResourceSkillInherit" };
            int InheritSkillCount = ResourceSkillInherit.GetNumberOfResources();
            System.Console.WriteLine($"ResourceSkillInherit count = {InheritSkillCount}");
            for (int i = 0; i < InheritSkillCount; i++)
            {
                var data = ResourceSkillInherit.GetResourceAtIndex(i);
                var lines = data.m_Data.SplitFirstLine();
                var inhertName = lines[0].Split(',')[1].Trim();
                var resourceSkillInhert = ResourceSkillDb.GetResource(inhertName);
                if (resourceSkillInhert)
                {
                    ResourceSkillBase resourceSkill = UnityEngine.Object.Instantiate(resourceSkillInhert);
                    resourceSkill.name = data.m_Name;
                    CsvToResource(lines[1], resourceSkill);
                    SkillList.Add(resourceSkill);
                    SkillDict.Add(data.m_Name, resourceSkill);
                }
            }

            // 4. ResourceActor Modify
            ResourceActorModify.m_FileTypeFilters = new List<string> { "ResourceActorModify" };
            int ModifyActorCount = ResourceActorModify.GetNumberOfResources();
            System.Console.WriteLine($"ResourceActorModify count = {ModifyActorCount}");
            for (int i = 0; i < ModifyActorCount; i++)
            {
                var data = ResourceActorModify.GetResourceAtIndex(i);
                var resourceActor = ResourceActorDb.GetResource(data.m_Name);
                if (resourceActor)
                {
                    CsvToResource(data.m_Data, resourceActor);
                }
            }

            // 5. ResourceActor Inherit
            ResourceActorInherit.m_FileTypeFilters = new List<string> { "ResourceActorInherit" };
            int InheritActorCount = ResourceActorInherit.GetNumberOfResources();
            System.Console.WriteLine($"ResourceActorInherit count = {InheritActorCount}");
            for (int i = 0; i < InheritActorCount; i++)
            {
                var data = ResourceActorInherit.GetResourceAtIndex(i);
                var lines = data.m_Data.SplitFirstLine();
                var inhertName = lines[0].Split(',')[1].Trim();
                var resourceActorInhert = ResourceActorDb.GetResource(inhertName);
                if (resourceActorInhert)
                {
                    ResourceActor resourceActor = UnityEngine.Object.Instantiate(resourceActorInhert);
                    resourceActor.name = data.m_Name;
                    CsvToResource(lines[1], resourceActor);
                    ActorList.Add(resourceActor);
                    ActorDict.Add(data.m_Name, resourceActor);
                }
            }
        }

        public static void AddSprite(string key, Sprite sprite)
        {
            ExternalSprites.Add(key, sprite);
        }

        public static List<string> FieldsIncludeBase(Type type)
        {
            var fieldKeys = new List<string>();
            for (Type t = type; t != null; t = t.BaseType)
            {
                fieldKeys.AddRange(AccessTools.GetFieldNames(t));
            }
            return fieldKeys;
        }

        public static void CsvToResource<T>(string csvText, T resourceObject) where T : UnityEngine.Object
        {
            Traverse tResActor = new Traverse(resourceObject);
            var fields = FieldsIncludeBase(resourceObject.GetType());
            string[] lines = csvText.SplitLines();
            foreach (var line in lines)
            {
                string[] array = line.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string key = array[0];
                if (fields.Contains(key))
                {
                    var field = tResActor.Field(key);
                    Type fieldType = field.GetValueType();
                    if (fieldType == typeof(bool))
                    {
                        field.SetValue(Boolean.Parse(array[1]));
                    }
                    else if (fieldType == typeof(string))
                    {
                        field.SetValue(array[1]);
                    }
                    else if (fieldType == typeof(int))
                    {
                        field.SetValue(int.Parse(array[1]));
                    }
                    else if (fieldType == typeof(float))
                    {
                        field.SetValue(float.Parse(array[1]));
                    }
                    else if (fieldType.BaseType.IsGenericType && fieldType.BaseType.GetGenericTypeDefinition() == typeof(SelectionCustomEnum<>))
                    {
                        field.Method("SetSelection", Traverse.Create(fieldType.BaseType.GetGenericArguments()[0]).Method("Cast", array[1]).GetValue()).GetValue();
                    }
                    else if (fieldType == typeof(Sprite))
                    {
                        if (ExternalSprites.ContainsKey(array[1]))
                        {
                            field.SetValue(ExternalSprites[array[1]]);
                        }
                        else
                        {
                            System.Console.WriteLine($"{array[1]} sprite not found!");
                        }
                    }
                    else if (fieldType == typeof(AssetReferenceGameObject))
                    {
                        field.SetValue(new AssetReferenceGameObject(array[1]));
                    }
                    else if (fieldType == typeof(ResourceActor))
                    {
                        field.SetValue(ResourceActorDb.GetResource(array[1]));
                    }
                    else if (fieldType == typeof(List<ResourceActor>))
                    {
                        var list = new List<ResourceActor>();
                        for (int i = 1; i < array.Length; ++i)
                        {
                            var obj = ResourceActorDb.GetResource(array[i]);
                            if (obj)
                            {
                                list.Add(obj);
                            }
                        }
                        field.SetValue(list);
                    }
                    else if (typeof(ResourceSkillBase).IsAssignableFrom(fieldType))
                    {
                        field.SetValue(ResourceSkillDb.GetResource(array[1]));
                    }
                    else if (fieldType == typeof(List<ResourceSkillBase>))
                    {
                        var list = new List<ResourceSkillBase>();
                        for (int i = 1; i < array.Length; ++i)
                        {
                            var obj = ResourceSkillDb.GetResource(array[i]);
                            if (obj)
                            {
                                list.Add(obj);
                            }
                        }
                        field.SetValue(list);
                    }
                    System.Console.WriteLine($"{fieldType.Name} {array[0]}={field.GetValue()}");
                }
            }
        }

        public static string UnityObjectToString(UnityEngine.Object obj)
        {
            if (obj == null)
                return "";
            var index = obj.name.IndexOf(" (");
            return index >= 0 ? obj.name.Substring(0, index) : obj.name;
        }

        public static void ResourceToCsv<T>(T resourceObject, out string csvText) where T : UnityEngine.Object
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"element_start,{resourceObject.name},{typeof(T).Name}");
            Type objectType = resourceObject.GetType();
            Traverse tResActor = new Traverse(resourceObject);
            var fieldKeys = FieldsIncludeBase(objectType);
            foreach (var key in fieldKeys)
            {
                var field = tResActor.Field(key);
                var fieldValue = field.GetValue();
                if (fieldValue == null)
                    continue;

                var fieldInfo = AccessTools.Field(objectType, key);
                if (fieldInfo.IsPublic || fieldInfo.GetCustomAttribute<SerializeField>() != null)
                {
                    Type fieldType = field.GetValueType();
                    if (fieldType == typeof(string))
                    {
                        sb.AppendLine($"{key},{fieldValue},");
                    }
                    else if (fieldValue is UnityEngine.Object obj)
                    {
                        sb.AppendLine($"{key},{UnityObjectToString(obj)},");
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(fieldType))
                    {
                        sb.Append($"{key},");
                        foreach (var it in fieldValue as IEnumerable)
                        {
                            if (it == null)
                            {
                                sb.Append(",");
                                continue;
                            }
                            if (it is UnityEngine.Object obj2)
                            {
                                sb.Append($"{UnityObjectToString(obj2)},");
                            }
                            else
                            {
                                sb.Append($"{it},");
                            }
                        }
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine($"{key},{fieldValue},");
                    }
                }    
            }
            sb.AppendLine($"element_end");
            csvText = sb.ToString();
        }
    }
}
