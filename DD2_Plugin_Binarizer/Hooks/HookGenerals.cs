using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using HarmonyLib;
using BepInEx;
using Assets.Code.Utils;
using Assets.Code.Profile;
using Assets.Code.Inputs;
using Assets.Code.Campaign;
using Assets.Code.UI.Widgets;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.UI;
using Assets.Code.Run;
using Assets.Code.Source;
using Assets.Code.Game;
using Assets.Code.UI.Managers;
using Assets.Code.Utils.Serialization;
using Assets.Code.Audio;
using UnityEngine.Networking.Types;
using Assets.Code.UI.Screens;

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

        /// <summary>
        /// 输入事件
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(InputSystemBhv), "Update")]
        public static void InputHack(ref InputSystemBhv __instance)
        {
            if (__instance.GetPointerValues().m_rightButton.m_wasPressed)
            {
                System.Console.WriteLine("Mouse Right!");

                // 减少火炬15点: 右键点马车的火炬
                var coachTorch = Traverse.Create(SingletonMonoBehaviour<GameUIBhv>.Instance)?
                    .Field("m_stageCoachTorch")?
                    .Field("m_hoverState")?
                    .GetValue<UIHoverStateBhv>();
                if (coachTorch && coachTorch.IsHoveringOver)
                {
                    SingletonMonoBehaviour<RunBhv>.Instance.RunValues.ChangeValue(RunValueType.TORCH, -15.0f, SourceType.TORCH);
                }
                // 减少火炬15点: 右键点战斗中火炬
                var battleTorchTip = Traverse.Create(SingletonMonoBehaviour<CombatUiBhv>.Instance)?
                    .Field("m_battleInfoBhv")?
                    .Field("m_torchBhv")?
                    .Field("m_tooltipGO")?
                    .GetValue<GameObject>();
                if (battleTorchTip && battleTorchTip.activeSelf)
                {
                    SingletonMonoBehaviour<RunBhv>.Instance.RunValues.ChangeValue(RunValueType.TORCH, -15.0f, SourceType.TORCH);
                }
            }

            // 快速保存
            if (Keyboard.current.f3Key.wasPressedThisFrame)
            {
                CopyCurrentSaveToQuickSave();
            }
        }

        /// <summary>
        /// Microsoft CopyDirectory
        /// </summary>
        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        /// <summary>
        /// 将当前存档设为即时存档
        /// </summary>
        static void CopyCurrentSaveToQuickSave()
        {
            string lastSaveDir = SaveUtils.GetMostRecentValidRunSaveFile(null);
            System.Console.WriteLine($"last save dir = {lastSaveDir}");
            if (string.IsNullOrEmpty(lastSaveDir))
                return;
            string quickSaveDir = SaveUtils.GetRunSavesRootPath(false) + "\\QuickSave";
            System.Console.WriteLine($"quick save dir = {quickSaveDir}");

            SingletonMonoBehaviour<AudioMgr>.Instance.Play(AudioPathsBhv.ClickConfirm, 8, null);
            var dir = new DirectoryInfo(quickSaveDir);
            if (dir.Exists)
                Directory.Delete(quickSaveDir, true);
            CopyDirectory(lastSaveDir, quickSaveDir, true);
        }

        /// <summary>
        /// 用即时存档覆盖当前存档
        /// </summary>
        static void OverwriteLastSaveFromQuickSave()
        {
            string lastSaveDir = SaveUtils.GetMostRecentValidRunSaveFile(null);
            System.Console.WriteLine($"last save dir = {lastSaveDir}");
            if (string.IsNullOrEmpty(lastSaveDir))
                return;
            string quickSaveDir = SaveUtils.GetRunSavesRootPath(false) + "\\QuickSave";
            System.Console.WriteLine($"quick save dir = {quickSaveDir}");
            var dir = new DirectoryInfo(quickSaveDir);
            if (!dir.Exists)
                return;
            dir = new DirectoryInfo(lastSaveDir);
            if (dir.Exists)
                Directory.Delete(lastSaveDir, true);
            CopyDirectory(quickSaveDir, lastSaveDir, true);
        }

        /// <summary>
        /// 按住Alt继续游戏，尝试用快速存档替换新存档
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(MainMenuUiScreenBhv), "OnContinueGameClick")]
        public static bool TryLoadQuickSave()
        {
            if (Keyboard.current.altKey.isPressed)
            {
                OverwriteLastSaveFromQuickSave();
            }
            return true;
        }

    }
}
