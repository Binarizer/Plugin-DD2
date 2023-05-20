using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using BepInEx;
using Assets.Code.Utils;
using Assets.Code.Profile;
using Assets.Code.Inputs;
using Assets.Code.Campaign;
using Assets.Code.UI.Widgets;
using Assets.Code.Actor;
using Assets.Code.Library;

namespace DD2
{
    // 一般游戏设定功能
    public class HookGenerals : IHook
    {
        public static readonly TextBasedEditorPrefsBoolType UNLOCK_ALL_SKILLS = new TextBasedEditorPrefsBoolType("UNLOCK_ALL_SKILLS".ToLowerInvariant(), false, "Enables all skills.", TextBasedEditorPrefsBaseType.BOOLS_GROUP, true);
        public static readonly TextBasedEditorPrefsBoolType RANDOM_INIT_QUIRKS = new TextBasedEditorPrefsBoolType("RANDOM_INIT_QUIRKS".ToLowerInvariant(), false, "Reset seeds when generate init quirks.", TextBasedEditorPrefsBaseType.BOOLS_GROUP, true);

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
        /// 解锁全技能
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Assets.Code.Skill.SkillInstance), "GetIsUnlocked")]
        public static bool UnlockAllSkills(ref bool __result)
        {
            __result = TextBasedEditorPrefs.GetBool(UNLOCK_ALL_SKILLS);
            return !__result;
        }

        /// <summary>
        /// 初始随机怪癖：功能
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Assets.Code.Quirk.QuirkContainer), "GenerateInitialQuirks")]
        public static bool RandomQuirkSeed(ref Assets.Code.Quirk.QuirkContainer __instance)
        {
            ProfileSeedInstance profileSeedInstance = SingletonMonoBehaviour<ProfileBhv>.Instance.GetCurrentProfile().GetProfileSeedInstance(__instance.ActorInstance.ActorDataId);
            if (TextBasedEditorPrefs.GetBool(RANDOM_INIT_QUIRKS))
            {
                profileSeedInstance.ClearGenerationSeed();
            }
            return true;
        }

        /// <summary>
        /// 初始随机怪癖：显示按钮
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(HeroSelectBhv), "PopulateActorInfo")]
        public static void RandomQuirkButton(ref HeroSelectBhv __instance)
        {
            if (TextBasedEditorPrefs.GetBool(RANDOM_INIT_QUIRKS))
            {
                Traverse.Create(__instance).Field("m_resetButton").GetValue<GameObject>().SetActive(true);
            }
        }

        /// <summary>
        /// 通过listener存一下CharacterSheetUiBhv
        /// </summary>
        private static CharacterSheetUiBhv characterSheetUiBhv = null;

        /// <summary>
        /// 换待机人物
        /// </summary>
        public static void HandleInputReplaceActor(string action, InputActionDelegateValues values)
        {
            if (values.m_performed && characterSheetUiBhv)
            {
                var roster = SingletonMonoBehaviour<CampaignBhv>.Instance.Roster;
                var rosterT = Traverse.Create(roster);
                var entries = rosterT.Field("m_Entries").GetValue<List<RosterEntry>>();
                RosterEntry from = null;
                RosterEntry to = null;
                int fromIndex = -1;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].ActorGuid == characterSheetUiBhv.ActorGuid)
                    {
                        fromIndex = i;
                        from = entries[i];
                        break;
                    }
                }
                if (from == null)
                {
                    return;
                }
                for (int j = 1; j < entries.Count; j++)
                {
                    RosterEntry rosterEntry = entries[(fromIndex + j) % entries.Count];
                    ActorInstance actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(rosterEntry.ActorGuid);
                    if (rosterEntry.GetRosterStatus() == RosterStatusType.RESERVE && actor.IsLiving)
                    {
                        to = rosterEntry;
                        break;
                    }
                }
                if (to != null)
                {
                    from.SetRosterStatus(RosterStatusType.RESERVE, 0u);
                    to.SetRosterStatus(RosterStatusType.PARTY, 0u);
                    Traverse.Create(characterSheetUiBhv).Method("TrySwitchToNewActor", new object[] { to.ActorGuid }).GetValue();
                }
            }
        }

        /// <summary>
        /// 换道途
        /// </summary>
        public static void HandleInputReplacePath(string action, InputActionDelegateValues values)
        {
            if (values.m_performed && characterSheetUiBhv)
            {
                ActorInstance actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(characterSheetUiBhv.ActorGuid);
                List<ActorDataPath> actorDataPaths = ActorPathCalculation.GetActorDataPaths(actor.ActorDataClass);
                System.Console.WriteLine($"actorDataPaths={actorDataPaths.Count}");
                for (int i = 0; i < actorDataPaths.Count; i++)
                {
                    if (actor.ActorDataPath.Id == actorDataPaths[i].Id)
                    {
                        actor.SetActorPath(actorDataPaths[(i + 1) % actorDataPaths.Count]);
                        System.Console.WriteLine($"setPath={actor.ActorDataPath.GetKey()}");
                        break;
                    }
                }
                Traverse.Create(characterSheetUiBhv).Method("TrySwitchToNewActor", new object[] { characterSheetUiBhv.ActorGuid }).GetValue();
            }
        }

        /// <summary>
        /// 添加操作：空格换道途，Alt+空格换人
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(CharacterSheetUiBhv), "AddListeners")]
        public static void AddReplaceActions(ref CharacterSheetUiBhv __instance)
        {
            characterSheetUiBhv = __instance;
            // 创建Alt+Space的操作指令
            List<string> inputActionMapNames = SingletonMonoBehaviour<InputSystemBhv>.Instance.GetInputActionMapNames(true);
            foreach (string mapName in inputActionMapNames)
            {
                SingletonMonoBehaviour<InputSystemBhv>.Instance.SetInputActionMapEnabled(mapName, false);
            }
            SingletonMonoBehaviour<InputSystemBhv>.Instance.GenerateInputAction("ChangeCharacter", "Space", "alt", "UI");
            SingletonMonoBehaviour<InputSystemBhv>.Instance.GenerateInputAction("ChangePath", "Space", "", "UI");
            foreach (string mapName2 in inputActionMapNames)
            {
                SingletonMonoBehaviour<InputSystemBhv>.Instance.SetInputActionMapEnabled(mapName2, true);
            }
            InputSystemBhv.AddListener("ChangeCharacter", new InputActionDelegate(HandleInputReplaceActor));
            InputSystemBhv.AddListener("ChangePath", new InputActionDelegate(HandleInputReplacePath));
        }

        /// <summary>
        /// 去除操作：空格换道途，Alt+空格换人
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(CharacterSheetUiBhv), "RemoveListeners")]
        public static void RemoveReplaceActions(ref CharacterSheetUiBhv __instance)
        {
            characterSheetUiBhv = null;
            InputSystemBhv.RemoveListener("ChangeCharacter", new InputActionDelegate(HandleInputReplaceActor));
            InputSystemBhv.RemoveListener("ChangePath", new InputActionDelegate(HandleInputReplacePath));
        }
    }
}
