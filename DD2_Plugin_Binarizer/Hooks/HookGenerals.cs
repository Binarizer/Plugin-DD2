using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;
using HarmonyLib;
using BepInEx;
using BepInEx.Configuration;
using Assets.Code.Utils;
using Assets.Code.Utils.Serialization;
using Assets.Code.Skill;
using Assets.Code.Profile;
using Assets.Code.Inputs;
using Assets.Code.Campaign;
using Assets.Code.Campaign.Events;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Run;
using Assets.Code.Source;
using Assets.Code.Game;
using Assets.Code.Game.Events;
using Assets.Code.Audio;
using Assets.Code.Item.Events;
using Assets.Code.Rules;
using Assets.Code.Quirk;
using Assets.Code.UI.Widgets;
using Assets.Code.UI.Screens;
using Assets.Code.UI.Managers;
using Assets.Code.Inn.Presentation;
using Assets.Code.Item;
using Assets.Code.Resource;

namespace DD2
{
    // 一般游戏设定功能
    public class HookGenerals : IHook
    {
        private static ConfigEntry<bool> forceEnableEditorPrefs;

        const string PluginGroupName = "_PLUGIN_BINARIZER_";
        public static readonly TextBasedEditorPrefsBoolType UNLOCK_ALL_SKILLS = new TextBasedEditorPrefsBoolType("UNLOCK_ALL_SKILLS".ToLowerInvariant(), false, "Enables all skills at beginning.", PluginGroupName, true);
        public static readonly TextBasedEditorPrefsBoolType RANDOM_INIT_QUIRKS = new TextBasedEditorPrefsBoolType("RANDOM_INIT_QUIRKS".ToLowerInvariant(), false, "Reset seeds when generate quirks.", PluginGroupName, true);
        public static readonly TextBasedEditorPrefsIntType INSTANCES_PER_CLASS = new TextBasedEditorPrefsIntType("INSTANCES_PER_CLASS".ToLowerInvariant(), 1, "Can select more than one hero per class.", PluginGroupName, true);
        public static readonly TextBasedEditorPrefsBoolType ALLOW_ABSENT = new TextBasedEditorPrefsBoolType("ALLOW_ABSENT".ToLowerInvariant(), false, "Skip 4-member checks; team will not fill at inns.", PluginGroupName, true);
        public static readonly TextBasedEditorPrefsIntType BIOME_COUNT_BIAS = new TextBasedEditorPrefsIntType("BIOME_COUNT_BIAS".ToLowerInvariant(), 0, "Modify biome count bias.", PluginGroupName, true);
        public static readonly TextBasedEditorPrefsBoolType BIOME_ALWAYS_EMBARK = new TextBasedEditorPrefsBoolType("BIOME_ALWAYS_EMBARK".ToLowerInvariant(), false, "Can always enter next biome.", PluginGroupName, true);

        public IEnumerable<Type> GetRegisterTypes()
        {
            return new Type[] { GetType() };
        }
        public void OnRegister(BaseUnityPlugin plugin)
        {
            forceEnableEditorPrefs = plugin.Config.Bind("General", "Force Enable Editor Prefs", false, "Force -enableEditorPrefs, no need to set command line args.");
        }

        public void OnUpdate()
        {
        }

        /// <summary>
        /// multi-mods simple support
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(ResourceGroupCsvDatabase), "GatherResources")]
        public static bool ModSupport(ref ResourceGroupCsvDatabase __instance)
        {
            string modRootPath = Path.Combine(Environment.CurrentDirectory, "Mods");
            if (!Directory.Exists(modRootPath))
                return true;

            string modOrderFile = Path.Combine(modRootPath, "ModOrder.txt");
            List<string> modOrder;
            if (File.Exists(modOrderFile))
            {
                modOrder = File.ReadLines(modOrderFile).ToList();
            }
            else
            {
                modOrder = new List<string>();
                var modDirs = Directory.GetDirectories(modRootPath);
                foreach (var dir in modDirs)
                {
                    modOrder.Add(Path.GetFileNameWithoutExtension(dir));
                }
            }
            modOrder.Reverse();

            foreach (string modName in modOrder)
            {
                if (string.IsNullOrEmpty(modName)) continue;
                string modPath = Path.Combine(modRootPath, modName);
                var modFolder = AccessTools.CreateInstance(typeof(ResourceTextFolder));
                var privateCtor = AccessTools.Constructor(typeof(ResourceTextFolder), new Type[] { typeof(string), typeof(string) });
                privateCtor?.Invoke(modFolder, new object[] { modName.ToLowerInvariant(), Path.Combine(modPath, "Excel") });
            }

            var folders = CustomEnum<ResourceTextFolder>.GetInstances() as List<ResourceTextFolder>;
            folders.Reverse();  // let game defaults be the lowest priority
            System.Console.WriteLine("mod order:");
            foreach (var textFolder in folders)
            {
                System.Console.WriteLine(textFolder.GetName());
            }
            return true;
        }

        /// <summary>
        /// 默认开启EditorPrefs
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(CommandLineUtils), "IsEditorPrefsEnabled")]
        public static bool ForceEnableEditorPrefs(ref bool __result)
        {
            if (forceEnableEditorPrefs.Value)
            {
                __result = true;
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// 解锁全技能
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(SkillInstance), "GetIsUnlocked")]
        public static bool UnlockAllSkills(ref bool __result)
        {
            __result = TextBasedEditorPrefs.GetBool(UNLOCK_ALL_SKILLS);
            return !__result;
        }

        /// <summary>
        /// 初始随机怪癖：功能
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(QuirkContainer), "GenerateInitialQuirks")]
        public static bool RandomQuirkSeed(ref QuirkContainer __instance)
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
                for (int i = 0; i < actorDataPaths.Count; i++)
                {
                    if (actor.ActorDataPath.Id == actorDataPaths[i].Id)
                    {
                        actor.SetActorPath(actorDataPaths[(i + 1) % actorDataPaths.Count]);
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

            // 滚动选人槽
            if (TextBasedEditorPrefs.GetInt(INSTANCES_PER_CLASS) > 1 && GameModeMgr.CurrentMode == GameModeType.HERO_SELECT && heroSelectBhv)
            {
                float roll = Mouse.current.scroll.ReadValue().y;
                if (roll != 0.0f)
                {
                    Transform tContainer = new Traverse(heroSelectBhv).Field("m_HeroSelectContainer").GetValue<Transform>();
                    tContainer.localPosition += new Vector3(145f * Mathf.Sign(roll), 0, 0);
                    if (tContainer.localPosition.x > containerInitPos.x)
                        tContainer.localPosition = containerInitPos;
                }
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

        /// <summary>
        /// 医院强制锁定或去除怪癖
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(HospitalScreenBhv), "HandleOnClickedQuirksBuyButton")]
        public static bool ForceRemoveOrLockQuirk(ref HospitalScreenBhv __instance)
        {
            Traverse t = Traverse.Create(__instance);
            QuirkInstance quirk = t.Field("m_selectedQuirkToTreat").GetValue<QuirkInstance>();
            ActorInstance actor = t.Field("m_selectedActor").GetValue<ActorInstance>();
            int num = 0;
            bool isLock = false;
            if (t.Field("m_lockableQuirks").Method("Contains", new object[] { quirk }).GetValue<bool>())
            {
                isLock = true;
                num = quirk.Definition.GetLockCost();
            }
            else if (t.Field("m_removableQuirks").Method("Contains", new object[] { quirk }).GetValue<bool>())
            {
                num = quirk.Definition.GetRemoveCost();
            }
            if (Keyboard.current.shiftKey.isPressed)
                isLock = true;
            if (Keyboard.current.altKey.isPressed)
                isLock = false;
            if (num > 0 && SingletonMonoBehaviour<RunBhv>.Instance.PlayerInventory.GetItemQty(RulesManager.GetRules<InventoryRules>().GOLD) >= num)
            {
                SingletonMonoBehaviour<RunBhv>.Instance.PlayerInventory.RemoveItem(RulesManager.GetRules<InventoryRules>().GOLD, num);
                if (isLock)
                    quirk.Lock();
                else
                    actor.QuirkContainer.Remove(quirk, SourceType.HOSPITAL, null, 0u, true);
                EventHospitalQuirkTreated.Trigger(actor.m_ActorGuid, quirk.Definition.Id, isLock);
                t.Field("m_selectedQuirkToTreat").SetValue(null);
                t.Method("UpdateValues").GetValue();
                EventUpdatePlayerCurrency.Trigger();
            }

            return false;
        }

        static int lockedQuirks = 0;

        /// <summary>
        /// 医院可锁多个怪癖1: 重写判定函数
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(HospitalScreenBhv), "HandleOnClickedQuirkToggle")]
        public static bool LockMoreQuirks(HospitalScreenBhv __instance, RectTransform clickedTransform)
        {
            var t = new Traverse(__instance);
            var lockableQuirks = t.Field("m_lockableQuirks").GetValue<List<QuirkInstance>>();
            var removableQuirks = t.Field("m_removableQuirks").GetValue<List<QuirkInstance>>();
            var lockableParent = t.Field("m_lockableQuirksParent").GetValue<Transform>();
            var removableParent = t.Field("m_removableQuirksParent").GetValue<Transform>();
            var selectedQuirkPointer = t.Field("m_selectedQuirkPointer").GetValue<RectTransform>();
            int siblingIndex = clickedTransform.GetSiblingIndex();
            lockedQuirks = 0;
            if (clickedTransform.parent == lockableParent)
            {
                for (int i = 0; i < lockableQuirks.Count; i++)
                {
                    var quirkInstance = lockableQuirks[i];
                    if (quirkInstance.IsLocked())
                    {
                        if (siblingIndex == i)
                            return false;
                        lockedQuirks++;
                    }
                }
                t.Field("m_selectedQuirkToTreat").SetValue(lockableQuirks[siblingIndex]);
            }
            else
            {
                if (!(clickedTransform.parent == removableParent))
                {
                    return false;
                }
                t.Field("m_selectedQuirkToTreat").SetValue(removableQuirks[siblingIndex]);
            }
            selectedQuirkPointer.gameObject.SetActive(true);
            if (selectedQuirkPointer.parent != clickedTransform)
            {
                selectedQuirkPointer.SetParent(clickedTransform);
                SingletonMonoBehaviour<AudioMgr>.Instance.Play(AudioPathsBhv.MinorClick, 8, null);
            }
            selectedQuirkPointer.anchoredPosition = t.Field("m_quirkPointerAnchoredPosition").GetValue<Vector2>();
            t.Method("UpdateTreatQuirksButton").GetValue();
            return false;
        }

        /// <summary>
        /// 医院可锁多个怪癖2: 若锁多个，则价格加倍
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(HospitalScreenBhv), "CalculateActiveCost")]
        public static void LockMoreQuirksCostPow(ref int __result)
        {
            __result <<= lockedQuirks;
        }

        private static HeroSelectBhv heroSelectBhv = null;
        private static Vector3 containerInitPos = Vector3.zero;

        /// <summary>
        /// 职业可重复选择：选人菜单加人
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(HeroSelectBhv), "Init")]
        public static bool MultipleClassActors(ref HeroSelectBhv __instance)
        {
            var t = new Traverse(__instance);
            var confirmButton = t.Field("m_ConfirmButton").GetValue<Button>();
            confirmButton.gameObject.SetActive(false);

            int instancesPerClass = TextBasedEditorPrefs.GetInt(INSTANCES_PER_CLASS);
            if (instancesPerClass <= 1)
                return true;

            if (heroSelectBhv == null)
            {
                var container = t.Field("m_HeroSelectContainer").GetValue<Transform>();
                containerInitPos = container.localPosition;
            }
            heroSelectBhv = __instance;

            Roster roster = SingletonMonoBehaviour<CampaignBhv>.Instance.Roster;
            Traverse tRoster = new Traverse(roster);
            var entries = tRoster.Field("m_Entries").GetValue<List<RosterEntry>>();
            var resourceActors = tRoster.Field("m_ActorResourceDatabase").GetValue<ResourceDatabaseActors>();
            List<ResourceActor> list = new List<ResourceActor>();
            List<int> counts = new List<int>();
            for (int i = 0; i < resourceActors.GetNumberOfResources(); i++)
            {
                ResourceActor actorResource = resourceActors.GetResourceAtIndex(i);
                int added = entries.Count(entry => entry.ActorClassId == actorResource.name);
                System.Console.WriteLine($"Try add class {actorResource.name}, statue={actorResource.StartingRosterStatusType}, populate={actorResource.GetPopulateInRoster()}, added={added}");
                if (actorResource != null && actorResource.GetPopulateInRoster() && added < instancesPerClass)
                {
                    ActorDataClass libraryElement = SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance.GetLibraryElement(actorResource.name);
                    if (libraryElement != null && libraryElement.GetIsUnlocked())
                    {
                        list.Add(actorResource);
                        counts.Add(instancesPerClass - added);
                    }
                }
            }
            list.Sort((a, b) => a.RosterOrderPriority - b.RosterOrderPriority);
            for (int i = 0; i < list.Count; i++)
            {
                ResourceActor resourceActor = list[i];
                int addCount = counts[i];
                for (int j = 0; j < addCount; ++j)
                {
                    uint guid = LibraryActors.LibraryActorsInstance.CreateActor(resourceActor, null, null);
                    RosterEntry rosterEntry = new RosterEntry(resourceActor.name, guid);
                    entries.Add(rosterEntry);
                    rosterEntry.SetRosterStatus(resourceActor.StartingRosterStatusType, 0u);
                    SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid).OnAddedToRoster();
                }
            }
            return true;
        }

        /// <summary>
        /// 职业可重复选择：不检测重复Class
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Roster), "RemoveInvalidRosterEntries")]
        public static bool SkipCheckDuplicate(ref Roster __instance)
        {
            int instancesPerClass = TextBasedEditorPrefs.GetInt(INSTANCES_PER_CLASS);
            if (instancesPerClass <= 1)
                return true;

            Traverse tRoster = new Traverse(__instance);
            var entries = tRoster.Field("m_Entries").GetValue<List<RosterEntry>>();
            List<uint> invalidRosterEntryActorGuids = new List<uint>();
            int count = entries.Count;
            for (int i = 0; i < count; i++)
            {
                RosterEntry entry = entries[i];
                bool valid = true;
                if (!SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetHasLibraryKey(entry.m_ActorGuid))
                {
                    valid = false;
                }
                ActorDataClass libraryElement = SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance.GetLibraryElement(entry.m_ActorClassId);
                if (libraryElement != null)
                {
                    if (libraryElement.m_DeathChainIds.Count > 0 || libraryElement.m_DeathChainLootIds.Count > 0)
                    {
                        valid = false;
                    }
                    if (libraryElement.QuirkContainerDefinition == null)
                    {
                        valid = false;
                    }
                    if (libraryElement.ActorDataStats.StatContainer.GetHasStat(ActorStatType.STRESS_MAX))
                    {
                        if (libraryElement.Overstresses.Count == 0)
                        {
                            valid = false;
                        }
                    }
                    else if (libraryElement.Overstresses.Count > 0)
                    {
                        valid = false;
                    }
                }
                else
                {
                    valid = false;
                }
                if (!valid)
                {
                    invalidRosterEntryActorGuids.Add(entry.m_ActorGuid);
                }
            }
            if (invalidRosterEntryActorGuids.Count > 0)
            {
                entries.RemoveAll((RosterEntry rosterEntry) => invalidRosterEntryActorGuids.Contains(rosterEntry.m_ActorGuid));
            }
            return false;
        }

        /// <summary>
        /// 可单人出门：条件
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(HeroSelectBhv), "CheckToSetConfirmButtonActive")]
        public static bool DontNeedFullParty(HeroSelectBhv __instance)
        {
            if (!TextBasedEditorPrefs.GetBool(ALLOW_ABSENT))
                return true;

            var t = new Traverse(__instance);
            var selectedActorGuids = t.Field("m_SelectedActorGuids").GetValue<List<uint>>();
            var confirmButton = t.Field("m_ConfirmButton").GetValue<Button>();
            var confirmDirector = t.Field("m_confirmDirector").GetValue<PlayableDirector>();
            var confirmBtnAppearTimeline = t.Field("m_confirmBtnAppearTimeline").GetValue<PlayableAsset>();
            bool canConfirm = selectedActorGuids.Any(i => i > 0u);
            if (!canConfirm)
            {
                confirmButton.gameObject.SetActive(false);
                EventHeroSelectConfirmStateChanged.Trigger(false, selectedActorGuids);
                return false;
            }
            bool activeInHierarchy = confirmButton.gameObject.activeInHierarchy;
            confirmButton.gameObject.SetActive(true);
            if (!activeInHierarchy)
            {
                confirmDirector.Play(confirmBtnAppearTimeline);
            }
            EventHeroSelectConfirmStateChanged.Trigger(true, selectedActorGuids);
            return false;
        }
        /// <summary>
        /// 可单人出门：确认后
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(HeroSelectBhv), "ConfirmRosterSelection")]
        public static bool DontNeedFullParty2(HeroSelectBhv __instance)
        {
            if (!TextBasedEditorPrefs.GetBool(ALLOW_ABSENT))
                return true;

            var t = new Traverse(__instance);
            var selectedActorGuids = t.Field("m_SelectedActorGuids").GetValue<List<uint>>();
            bool canConfirm = selectedActorGuids.Any(i => i > 0u);
            if (canConfirm)
                selectedActorGuids.RemoveAll(i => i == 0u);
            return true;
        }
        static int savedFillAmount = -1;
        /// <summary>
        /// 可单人出门：旅馆不加人
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Roster), "OnGameModeEnterPreStart")]
        public static bool DontNeedFullParty3(ref Roster __instance)
        {
            var tRules = new Traverse(RulesManager.GetRules<CampaignRules>());
            if (!TextBasedEditorPrefs.GetBool(ALLOW_ABSENT))
            {
                if (savedFillAmount >= 0)
                {
                    tRules.Field("m_RosterReserveFillAmount").SetValue(savedFillAmount);
                    savedFillAmount = -1;
                }
            }
            else
            {
                if (savedFillAmount < 0)
                {
                    savedFillAmount = tRules.Field("m_RosterReserveFillAmount").GetValue<int>();
                    tRules.Field("m_RosterReserveFillAmount").SetValue(0);
                }
            }
            return true;
        }

        /// <summary>
        /// 更改地牢区域数
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(RunManager), "GetRunNumberOfTypicalBiomes")]
        public static void ModifyBiomeCountBias(ref int __result)
        {
            __result += TextBasedEditorPrefs.GetInt(BIOME_COUNT_BIAS);
        }

        /// <summary>
        /// 总是能打boss 1
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(InnPresentationBhv), "GetCanEmbarkNextBiome")]
        public static void AlwaysCanEmbark1(ref bool __result)
        {
            __result |= TextBasedEditorPrefs.GetBool(BIOME_ALWAYS_EMBARK);
        }

        /// <summary>
        /// 总是能打boss 2
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(RunBhv), "GetEndBiomeRequiredStageCoachItemSlotType")]
        public static void AlwaysCanEmbark2(ref ItemSlotType __result)
        {
            if (TextBasedEditorPrefs.GetBool(BIOME_ALWAYS_EMBARK))
                __result = null;
        }
    }
}
