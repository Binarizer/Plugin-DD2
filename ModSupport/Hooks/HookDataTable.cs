using BepInEx.Unity.Mono;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using System;
using System.Collections.Generic;
using UnityEngine;

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
        }

        public void OnUpdate()
        {
        }

        /// <summary>
        /// UpgradeItemData 可升级项目
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(UpgradeItemCollectionData), "Get", new Type[] { typeof(string) })]
        static void UpgradePoisonMod(ref UpgradeItemData __result)
        {
            var file = HookMods.FindModFile($"DataTable/UpgradeItemData/{__result.name}.json");
            if (file != null)
            {
                Debug.Log($"UpgradePoisonMod: find so {__result.name}");
                string jsonString = System.IO.File.ReadAllText(file);
                ScriptableObject so = __result;
                HookExporter.FromJsonString(ref so, jsonString);
                __result = (UpgradeItemData)so;
            }
        }
    }
}
