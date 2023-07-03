using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using HarmonyLib;
using Assets.Code.Utils;
using Assets.Code.Utils.Serialization;
using Assets.Code.Resource;
using Assets.Code.Gfx;
using Assets.Code.Actor;

namespace DD2
{
    /// <summary>
    /// 将红勾Scriptable配置，通过文本进行CRU(D)的类
    /// </summary>
    public interface IScriptableObjectTextCrud 
    {
        Type GetItemType();
        void Crud();

        void SetField(Traverse field, string strParam);
        void SetListField(Traverse listField, string[] strParamArray);
        void Export(string exportDir);
    }

    public class ScriptableObjectTextCrud<T> : IScriptableObjectTextCrud where T : ScriptableObject
    {
        public ScriptableObjectTextCrud(string name, ResourceDatabaseObject<T> db)
        {
            DataName = name;
            DbReference = db;
            var tDb = Traverse.Create(db);
            ResourceList = tDb.Field("m_Resources").GetValue<List<T>>();
            ResourceDict = tDb.Field("m_ResourcesDictionary").GetValue<Dictionary<string, T>>();
            DbTextModify = ScriptableObject.CreateInstance<ResourceDatabaseText>();
            DbTextInherit = ScriptableObject.CreateInstance<ResourceDatabaseText>();
            DbTextNew = ScriptableObject.CreateInstance<ResourceDatabaseText>();
        }

        public void Export(string exportDir)
        {
            if (!Directory.Exists(exportDir))
                Directory.CreateDirectory(exportDir);
            string dir = Path.Combine(exportDir, DataName);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            System.Console.WriteLine($"Export to {dir}");
            int resourceCount = DbReference.GetNumberOfResources();
            for (int i = 0; i < resourceCount; ++i)
            {
                var resource = DbReference.GetResourceAtIndex(i);
                string path = Path.Combine(dir, resource.name + ".csv");
                ExternalResourceManager.ResourceToCsv(resource, out string originalText);
                File.WriteAllText(path, originalText);
            }
        }

        public void Crud()
        {
            System.Console.WriteLine($"Crud Name = {DataName}, DbType = {typeof(T).Name}");

            // Apply Modify
            DbTextModify.m_FileTypeFilters = new List<string> { DataName + "Modify" };
            int modifyCount = DbTextModify.GetNumberOfResources();
            System.Console.WriteLine($"Modify count = {modifyCount}");
            for (int i = 0; i < modifyCount; i++)
            {
                var data = DbTextModify.GetResourceAtIndex(i);
                var resourceSkill = DbReference.GetResource(data.m_Name);
                if (resourceSkill)
                {
                    ExternalResourceManager.CsvToResource(data.m_Data, resourceSkill);
                }
            }

            // Inherit Instantiate
            DbTextInherit.m_FileTypeFilters = new List<string> { DataName + "Inherit" };
            int inheritCount = DbTextInherit.GetNumberOfResources();
            System.Console.WriteLine($"Inherit count = {inheritCount}");
            for (int i = 0; i < inheritCount; i++)
            {
                var data = DbTextInherit.GetResourceAtIndex(i);
                var lines = data.m_Data.SplitFirstLine();
                var inhertName = lines[0].Split(',')[1].Trim();
                var resourceInhert = DbReference.GetResource(inhertName);
                if (resourceInhert)
                {
                    T scriptableObject = UnityEngine.Object.Instantiate(resourceInhert);
                    scriptableObject.name = data.m_Name;
                    if (lines.Length > 1)
                        ExternalResourceManager.CsvToResource(lines[1], scriptableObject);
                    ResourceList.Add(scriptableObject);
                    ResourceDict.Add(data.m_Name, scriptableObject);
                }
            }

            // New Instantiate
            DbTextNew.m_FileTypeFilters = new List<string> { DataName };
            int newCount = DbTextNew.GetNumberOfResources();
            System.Console.WriteLine($"New count = {newCount}");
            for (int i = 0; i < newCount; i++)
            {
                var data = DbTextNew.GetResourceAtIndex(i);
                T scriptableObject = ScriptableObject.CreateInstance<T>();
                scriptableObject.name = data.m_Name;
                ExternalResourceManager.CsvToResource(data.m_Data, scriptableObject);
                ResourceList.Add(scriptableObject);
                ResourceDict.Add(data.m_Name, scriptableObject);
            }
        }

        public void SetField(Traverse field, string strParam)
        {
            field.SetValue(DbReference.GetResource(strParam));
        }

        public void SetListField(Traverse listField, string[] strParamArray)
        {
            var list = new List<T>();
            for (int i = 1; i < strParamArray.Length; ++i)
            {
                var obj = DbReference.GetResource(strParamArray[i]);
                if (obj)
                {
                    list.Add(obj);
                }
            }
            listField.SetValue(list);
        }

        public Type GetItemType()
        {
            return typeof(T);
        }

        string DataName = null;
        ResourceDatabaseObject<T> DbReference = null;
        List<T> ResourceList = null;
        Dictionary<string, T> ResourceDict = null;
        ResourceDatabaseText DbTextModify = null;
        ResourceDatabaseText DbTextInherit = null;
        ResourceDatabaseText DbTextNew = null;
    }

    public static class ExternalResourceManager
    {
        /// <summary>
        /// External Sprite Container
        /// </summary>
        readonly static List<IScriptableObjectTextCrud> CrudList = new List<IScriptableObjectTextCrud>();

        /// <summary>
        /// External Sprite Container
        /// </summary>
        readonly public static Dictionary<string, Sprite> ExternalSprites = new Dictionary<string, Sprite>();

        /// <summary>
        /// External Sprite Container
        /// </summary>
        readonly public static Dictionary<string, GameObject> ItemOverrideObject = new Dictionary<string, GameObject>();

        /// <summary>
        /// Scale Modifier
        /// </summary>
        public static Dictionary<string, float> ArtScaleDict = new Dictionary<string, float>();

        /// <summary>
        /// AudioPath Modifier
        /// </summary>
        public static Dictionary<string, string> AudioPathDict = new Dictionary<string, string>();

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
            // 1. Addressables
            var catalogPath = Path.Combine(modPath, "catalog.json");
            if (File.Exists(catalogPath))
            {
                Addressables.LoadContentCatalogAsync(catalogPath);
            }

            // 2. UI sprites
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

        public static void AddResourceDatabase(ScriptableObject db)
        {
            Type typeArg = db.GetType().BaseType.GetGenericArguments()[0];
            Type typeCrud = typeof(ScriptableObjectTextCrud<>).MakeGenericType(typeArg);
            string name = typeArg.Name;
            if (name.EndsWith("Base"))
                name = name.Substring(0, name.Length - 4);

            System.Console.WriteLine($"Database {name}, Resource Count = {Traverse.Create(db).Field("m_Resources").Property("Count").GetValue()}");

            CrudList.Add(Activator.CreateInstance(typeCrud, name, db) as IScriptableObjectTextCrud);
        }
        public static IList<T> Swap<T>(this IList<T> list, int indexA, int indexB)
        {
            (list[indexB], list[indexA]) = (list[indexA], list[indexB]);
            return list;
        }

        public static void AddModResources(bool export)
        {
            // order matters for dependencies
            int crudSkill = CrudList.FindIndex(crud => crud.GetItemType() == typeof(ResourceSkillBase));
            int crudActor = CrudList.FindIndex(crud => crud.GetItemType() == typeof(ResourceActor));
            if (crudSkill >= 0 && crudActor >= 0 && crudActor < crudSkill)
            {
                CrudList.Swap(crudSkill, crudActor);
            }

            if (export)
            {
                string exportDir = Path.Combine(Environment.CurrentDirectory, "OfficalResourceExport");
                foreach (var crud in CrudList)
                {
                    crud.Export(exportDir);
                }
            }

            foreach (var crud in CrudList)
            {
                crud.Crud();
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

        public static void CsvToResource<T>(string csvText, T resourceObject) where T : ScriptableObject
        {
            Traverse tResActor = new Traverse(resourceObject);
            var fields = FieldsIncludeBase(resourceObject.GetType());
            string[] lines = csvText.SplitLines();
            foreach (var line in lines)
            {
                string[] array = line.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string key = array[0];
                if (key == "Scale")
                {
                    ArtScaleDict[array[1]] = float.Parse(array[2]);
                }
                else if (key == "Audio")
                {
                    AudioPathDict[array[1]] = array[2];
                }
                else if (fields.Contains(key))
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
                    else if (typeof(AssetReference).IsAssignableFrom(fieldType))
                    {
                        field.SetValue(Activator.CreateInstance(fieldType, array[1]));
                    }
                    else if (typeof(ScriptableObject).IsAssignableFrom(fieldType))
                    {
                        foreach (var crud in CrudList)
                        {
                            if (crud.GetItemType().IsAssignableFrom(fieldType))
                            {
                                crud.SetField(field, array[1]);
                            }
                        }
                    }
                    else if (fieldType.IsGenericType && typeof(IList).IsAssignableFrom(fieldType) && typeof(ScriptableObject).IsAssignableFrom(fieldType.GenericTypeArguments[0]))
                    {
                        foreach (var crud in CrudList)
                        {
                            if (crud.GetItemType().IsAssignableFrom(fieldType.GenericTypeArguments[0]))
                            {
                                crud.SetListField(field, array);
                            }
                        }
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

        public static void ResourceToCsv<T>(T resourceObject, out string csvText) where T : ScriptableObject
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"element_start,{resourceObject.name},{resourceObject.GetType().Name}");
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
                    else if (typeof(AssetReference).IsAssignableFrom(fieldType))
                    {
                        sb.AppendLine($"{key},{(fieldValue as AssetReference).AssetGUID},");
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

        public static void LoadByAddress<AssetType>(string address) where AssetType : UnityEngine.Object
        {
            var t = Traverse.Create(Singleton<AddressableReferencesManager>.Instance);
            if (!t.Field("m_LoadingHandles").Method("ContainsKey", address).GetValue<bool>())
            {
                AsyncOperationHandle<AssetType> handle = Addressables.LoadAssetAsync<AssetType>(address);
                Singleton<AddressableReferencesManager>.Instance.RegisterLoadingHandle(address, handle);
            }
        }
    }
}
