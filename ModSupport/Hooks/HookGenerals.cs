using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Lean.Localization;
using Mortal.Core;
using Mortal.Story;
using OBB.Framework.Attributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mortal
{
    public class HookGeneral : IHook
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

        private bool f4 = false;
        private bool f5 = false;

        public void OnUpdate()
        {
            if (exportEnable.Value)
            {
                //if (Keyboard.current.f3Key.IsPressed())
                //{
                //    Debug.Log("F3 is pressed");
                //    ExportDataTables();
                //}

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
            var stream = File.CreateText(exportPath);
            foreach (var pair in LeanLocalization.CurrentTranslations)
            {
                stream.WriteLine($"{pair.Key},\"{pair.Value.Data}\"");
            }
            stream.Close();
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

        public static IEnumerable<Type> BaseTypesOf(Type t)
        {
            while (t != null)
            {
                yield return t;
                t = t.BaseType;
            }
        }

        public static Type FindGenericBaseTypeOf(Type t, Type openType)
        {
            return BaseTypesOf(t)
                .FirstOrDefault(bt => bt.IsGenericType && bt.GetGenericTypeDefinition() == openType);
        }

        public static void ExportDataTables()
        {
            var types = Assembly.GetAssembly(typeof(CollectionData<>)).GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Select(t => new { Type = t, GenericBase = FindGenericBaseTypeOf(t, typeof(CollectionData<>)) })
                .Where(ts => ts.GenericBase != null)
                .Select(ts => ts.Type)
                .ToArray();
            Debug.Log($"DataTable Count = {types.Length}");
            var exportPath = "./DataTables/";
            if (!Directory.Exists(exportPath))
            {
                Directory.CreateDirectory(exportPath);
            }
            foreach (var type in types)
            {
                var fullPath = exportPath + type.Name + ".csv";
                var table = ScriptableObject.CreateInstance(type);
                Debug.Log($"Export DataTable {table.GetType().Name} to {fullPath}");
                var pi = type.GetProperty("List", BindingFlags.Public | BindingFlags.Instance);
                Debug.Log($"PropertyInfo = {pi.Name}");
                var list = pi.GetValue(table);
                if (list != null)
                {
                    Debug.Log($"ListType = {list.GetType().Name}");
                    var ilist = list as IList;
                    Debug.Log($"List count = {ilist.Count}");
                    //var stream = File.CreateText(fullPath);
                    foreach (var item in ilist)
                    {
                        FieldInfo[] fields = item.GetType().GetFields();
                        Debug.Log($"Field count = {fields.Length}");
                    }
                    //stream.Close();
                }
            }
        }
    }
}
