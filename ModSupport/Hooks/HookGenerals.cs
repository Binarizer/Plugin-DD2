using BepInEx.Unity.Mono;
using Lean.Localization;
using Mortal.Core;
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
        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }


        public void OnRegister(BaseUnityPlugin plugin)
        {
        }

        private bool f4 = false;

        public void OnUpdate()
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
