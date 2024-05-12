using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using BepInEx;
using BepInEx.Configuration;
using CPrompt;

namespace Millennia
{
    // 自用修改
    public class HookGeneral : IHook
    {
        private static ConfigEntry<bool> portMove;

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
        /// 丘陵+1视野
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(AEntityCharacter), "AdjustVisibleRadius")]
        public static bool Override_AdjustVisibleRadius(ref AEntityCharacter __instance, ALocation forLoc, int visDelta, int revealRadius, out List<ALocation> changedLocs, bool setEdgeData)
        {
            changedLocs = null;
            if (visDelta == 1 && revealRadius == 1 && forLoc.GetTerrainType().HasTag("Hills"))
            {
                revealRadius = 2;
            }
            if (!(forLoc == null) && !forLoc.AdjustVisibleRadius(__instance.PlayerNum, visDelta, revealRadius, out changedLocs, setEdgeData))
            {
                if (visDelta > 0)
                {
                    ALogger.LogWarning($"AdjustVisibleRadius (+1) failed for {__instance.GetUniqueID()} moving to {forLoc.TileCoord}");
                }
                else if (visDelta < 0)
                {
                    ALogger.LogWarning($"AdjustVisibleRadius (-1) failed for {__instance.GetUniqueID()} moving from {forLoc.TileCoord}");
                }
            }
            return false;
        }

        /// <summary>
        /// 港口下海不耗尽移动
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(ACardEffect), "DoSpecialTransportLoad")]
        public static bool Override_DoSpecialTransportLoad(ref bool __result, GameObject externalTarget, AEntity effectExecutor, APlayer activePlayer)
        {
            __result = false;

            ALocation locationFromGameObject = ALocation.GetLocationFromGameObject(externalTarget);
            if (locationFromGameObject == null)
            {
                return false;
            }
            if (effectExecutor == null)
            {
                ALogger.LogWarning("Effect executor is null!");
                return false;
            }
            int playerNum = effectExecutor.PlayerNum;
            APlayer player = AGame.Instance.GetPlayer(playerNum);
            if (player == null)
            {
                ALogger.LogWarning(string.Format("Could not get player {0}", playerNum));
                return false;
            }
            string stringValue = player.GetComponent<AGameData>().GetStringValue(AEntityCharacter.cTransportEntityType);
            if (AEntityInfoManager.Instance.Get(stringValue) == null)
            {
                ALogger.LogWarning("ContainerEntInfo is null for container type [" + stringValue + "]");
                return false;
            }
            ATileCoord tileCoord = locationFromGameObject.TileCoord;
            AEntityCharacter unit = effectExecutor.GetComponent<AEntityCharacter>();
            if (unit == null)
            {
                ALogger.LogWarning("Entity " + effectExecutor.GetUniqueID() + " was not a unit!");
                return false;
            }
            ALocation currentLoc = unit.CurrentLoc;
            AEntityCharacter transportedUnit = AMapController.Instance.CreateUnit(stringValue, tileCoord, playerNum, false, false);
            if (transportedUnit == null)
            {
                ALogger.LogWarning(string.Format("Failed to create transport unit at {0}!", tileCoord));
                return false;
            }
            AUndoManager aundoManager = null;
            APlayer currPlayer = AGame.Instance.CurrPlayer;
            if (currPlayer != null)
            {
                aundoManager = currPlayer.GetComponent<AUndoManager>();
            }
            if (aundoManager != null)
            {
                aundoManager.CaptureUndoEntityCreate(transportedUnit);
                aundoManager.CaptureUndoPosition(unit);
                aundoManager.CaptureUndoStateGameData(unit.GetComponent<AGameData>());
            }
            bool flag = activePlayer != null && activePlayer == AGame.Instance.GetVisualizedPlayer();
            if (flag)
            {
                AInputHandler.Instance.ClearSelection(true, true);
            }
            transportedUnit.SetContainedUnit(unit, true);
            var docks = locationFromGameObject.GetEntitiesWithTag("Docks");
            if (docks.Count > 0 && docks[0] is AEntityBuilding imp && !imp.IsRazed())
            {
                float moveRemain = Math.Max(unit.GetMoveStat() - 10.0f, 0.0f) / unit.GetMoveStatMax() * transportedUnit.GetMoveStatMax();
                AGameData data = transportedUnit.GetComponent<AGameData>();
                data.SetBaseValue(AEntityCharacter.cStatMovement, moveRemain);
                data.SetBaseValueAsBool(AEntityCharacter.cMovedThisTurn, val: true);
                transportedUnit.SetActionExecuted(moveRemain > 0.0f);
            }
            else
            {
                transportedUnit.MarkActionComplete(true, true);
            }
            unit.MarkActionComplete(true, true);
            if (flag && locationFromGameObject.IsOnscreen && locationFromGameObject.GetIsVisible(activePlayer.PlayerNum))
            {
                ASoundManager.Instance.PlayCustomizedSoundEvent(ASoundManager.cFXUATransportLoaded, transportedUnit.TypeID, 0f, true);
            }
            if (player.PlayerType == APlayerType.PT_AI)
            {
                AArmyGroupController component2 = player.GetComponent<AArmyGroupController>();
                AArmyGroup aarmyGroup = component2.FindGroup(currentLoc.TileCoord);
                if (aarmyGroup != null)
                {
                    component2.RemoveGroup(tileCoord, true);
                    if (currentLoc == null)
                    {
                        ALogger.LogWarning("origLoc was null!");
                        return false;
                    }
                    aarmyGroup.UpdateStackPosition(currentLoc.TileCoord, tileCoord, true);
                }
            }

            __result = true;
            return false;
        }
        /// <summary>
        /// 港口上岸不耗尽移动
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(ACardEffects), "EjectUnitFromTransport")]
        public static bool Override_EjectUnitFromTransport(ref bool __result, AEntityCharacter unitTransport, ALocation unloadLoc, out AEntityCharacter transportedUnit)
        {
            __result = false;
            transportedUnit = null;
            var docks = unitTransport.CurrentLoc.GetEntitiesWithTag("Docks");
            bool inUnrazedDocks = docks.Count > 0 && docks[0] is AEntityBuilding imp && !imp.IsRazed();
            AUndoManager component = AGame.Instance.CurrPlayer.GetComponent<AUndoManager>();
            if (component != null)
            {
                AEntityCharacter containedUnit = unitTransport.GetContainedUnit(true);
                if (containedUnit == null)
                {
                    return false;
                }
                component.CaptureUndoPosition(containedUnit);
                component.CaptureUndoStateGameData(containedUnit.GetComponent<AGameData>());
            }
            transportedUnit = unitTransport.EjectContainedUnit(unloadLoc.TileCoord);
            if (transportedUnit == null)
            {
                ALogger.LogWarning(string.Format("transportedUnit was null after being ejected from {0}", unloadLoc.TileCoord));
                return false;
            }
            APlayer visualizedPlayer = AGame.Instance.GetVisualizedPlayer();
            if (visualizedPlayer != null && unloadLoc.IsOnscreen && unloadLoc.GetIsVisible(visualizedPlayer.PlayerNum))
            {
                ASoundManager.Instance.PlayCustomizedSoundEvent(ASoundManager.cFXUATransportUnloaded, unitTransport.TypeID, 0f, true);
            }
            if (inUnrazedDocks)
            {
                float moveRemain = Math.Max(unitTransport.GetMoveStat() - 10.0f, 0.0f) / unitTransport.GetMoveStatMax() * transportedUnit.GetMoveStatMax();
                AGameData data = transportedUnit.GetComponent<AGameData>();
                data.SetBaseValue(AEntityCharacter.cStatMovement, moveRemain);
                data.SetBaseValueAsBool(AEntityCharacter.cMovedThisTurn, val: true);
                transportedUnit.SetActionExecuted(moveRemain > 0.0f);
            }
            else
            {
                transportedUnit.MarkActionComplete(true, true);
            }
            __result = true;
            return false;
        }
    }
}
