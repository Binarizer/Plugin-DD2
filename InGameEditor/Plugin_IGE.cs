using System.Collections.Generic;
using System.Linq;
using BepInEx;
using CPrompt;
using HarmonyLib;
using UnityEngine;

namespace Millennia_IGE
{
    [BepInPlugin("plugins.Millennia.IGE", "In Game Editor", "0.0.0.1")]
    public class Plugin_IGE : BaseUnityPlugin
    {
        void Start()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin_IGE));
        }

        private void Update()
        {
            if (AGame.Instance && AGame.Instance.CurrPlayer)
            {
                if (Input.GetKeyDown(KeyCode.F5))
                {
                    Plugin_IGE.switchDisplayingWindow = !Plugin_IGE.switchDisplayingWindow;
                    player = AGame.Instance.CurrPlayer;
                    data = player.GetComponent<AGameData>();
                    domainManager = player.GetComponent<ADomainManager>();
                }
                if (switchDisplayingWindow)
                {
                    selectedTile = AInputHandler.Instance.SelectedLocation;
                    selectedCity = AInputHandler.Instance.SelectedCity;
                    if (!selectedCity)
                    {
                        selectedCity = selectedTile.GetCity()?.GetTile();
                    }
                }
            }
        }

        // Token: 0x06000005 RID: 5 RVA: 0x0000224C File Offset: 0x0000044C
        private void OnGUI()
        {
            bool flag = Plugin_IGE.switchDisplayingWindow;
            if (flag)
            {
                bool flag2 = (double)Time.time % 0.2 < 1.0;
                if (flag2)
                {
                    windowRect = GUI.Window(20240502, windowRect, new GUI.WindowFunction(CheatWindow), "Millennia In Game Editor");
                }
            }

        }

        // Token: 0x06000006 RID: 6 RVA: 0x000022B0 File Offset: 0x000004B0
        public void CheatWindow(int winId)
        {
            GUI.skin.label.fontSize = 20;
            GUI.contentColor = Color.red;
            GUI.skin.button.fontSize = 16;
            GUI.DragWindow(new Rect(10f, 0f, 480f, 20f));
            GUILayout.BeginArea(new Rect(10f, 20f, 480f, 420f));
            currentGridIndex = GUILayout.Toolbar(currentGridIndex, gridText);
            switch (currentGridIndex)
            {
                case 0:
                    {
                        GUILayout.Label($"当前地图种子{AGameConstants.Instance.GetSeed()}");
                        GUILayout.BeginHorizontal();
                        bool flag2 = GUILayout.Button("金钱+1000");
                        if (flag2)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResCoin, 1000f, false);
                        }
                        bool flag3 = GUILayout.Button("文化+100");
                        if (flag3)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResCulture, 100f, false);
                        }
                        bool flag31 = GUILayout.Button("知识+100");
                        if (flag31)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResKnowledge, 100f, false);
                            player.ApplyKnowledge();
                        }
                        bool flag32 = GUILayout.Button("改良+100");
                        if (flag32)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResImprovementPoints, 100f, false);
                        }
                        bool flag33 = GUILayout.Button("专家+100");
                        if (flag33)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResSpecialists, 100f, false);
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        bool flag4 = GUILayout.Button("回合革新+100");
                        if (flag4)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResInnovation + AGame.cPerTurnSuffix, 100f, false);
                        }
                        bool flag41 = GUILayout.Button("回合混乱-100");
                        if (flag41)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResChaos + AGame.cPerTurnSuffix, -100f, false);
                        }
                        bool flag42 = GUILayout.Button("总革新+100");
                        if (flag42)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResInnovation, 100f, false);
                        }
                        bool flag43 = GUILayout.Button("总混乱-100");
                        if (flag43)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResChaos, -100f, false);
                        }
                        bool flag44 = GUILayout.Button("总混乱+100");
                        if (flag44)
                        {
                            data.AdjustBaseValueCapped(APlayer.cResChaos, 100f, false);
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Label("领域经验+100");
                        GUILayout.BeginHorizontal();
                        bool flag5 = GUILayout.Button("政府");
                        if (flag5)
                        {
                            data.AdjustBaseValue("ResDomainGovernment", 100f, false);
                        }
                        bool flag6 = GUILayout.Button("探索");
                        if (flag6)
                        {
                            data.AdjustBaseValue("ResDomainExploration", 100f, false);
                            data.SetBaseValueAsBool("DomainUnlock-DomainExploration", true);
                        }
                        bool flag7 = GUILayout.Button("战争");
                        if (flag7)
                        {
                            data.AdjustBaseValue("ResDomainWarfare", 100f, false);
                            data.SetBaseValueAsBool("DomainUnlock-DomainWarfare", true);
                        }
                        bool flag8 = GUILayout.Button("工程");
                        if (flag8)
                        {
                            data.AdjustBaseValue("ResDomainEngineering", 100f, false);
                            data.SetBaseValueAsBool("DomainUnlock-DomainEngineering", true);
                        }
                        bool flag9 = GUILayout.Button("外交");
                        if (flag9)
                        {
                            data.AdjustBaseValue("ResDomainDiplomacy", 100f, false);
                            data.SetBaseValueAsBool("DomainUnlock-DomainDiplomacy", true);
                        }
                        bool flag10 = GUILayout.Button("艺术");
                        if (flag10)
                        {
                            data.AdjustBaseValue("ResDomainArts", 100f, false);
                            data.SetBaseValueAsBool("DomainUnlock-DomainArts", true);
                        }
                        GUILayout.EndHorizontal();
                        toggleTechShiftClick = GUILayout.Toggle(toggleTechShiftClick, "Shift+单击 解锁科技");
                        GUILayout.Label("在科技界面，按住Shift+鼠标左键点击想要解锁的科技");
                        bool flag11 = GUILayout.Button("显示所有地标和奖励村庄");
                        if (flag11)
                        {
                            ACard acard = ACard.FindFromText("EXPLORERS-LANDMARKS");
                            acard.Play(null, null, player);
                            string target = acard.Choices[0].Effects[0].Target;
                            acard.Choices[0].Effects[0].Target = "ENTTAG,ALLPLAYERS-RewardCamp";
                            acard.Play(null, null, player);
                            acard.Choices[0].Effects[0].Target = target;
                        }
                        bool changed = GUI.changed;
                        if (changed)
                        {
                            //ADevConfig.EnableTechShiftClick = toggleTechShiftClick;
                            AUIManager.Instance.RefreshAllPanels(UIRefreshType.cUIRefreshAll);
                        }
                        break;
                    }
                case 1:
                    {
                        if (selectedCity && selectedCity.IsCity(false))
                        {
                            var cityLabel = AStringTable.Instance.GetString("UI-MainMenu-NationBuilder-Cities") + ": ";
                            cityLabel += selectedCity.GetDisplayName();
                            GUILayout.Label(cityLabel);
                            bool flag12 = selectedCity != null && selectedCity.IsCity(false);
                            if (flag12)
                            {
                                ACity city = selectedCity.GetCity(false);
                                GUILayout.BeginHorizontal();
                                bool flag13 = GUILayout.Button("+1" + AStringTable.Instance.GetString("UI-CityFrame-PopulationLabel"));
                                if (flag13)
                                {
                                    city.AddPopulation(1);
                                }
                                if (!city.PrimaryProductionQueue.IsEmpty())
                                {
                                    bool flag15 = GUILayout.Button("立即建造");
                                    if (flag15)
                                    {
                                        city.PrimaryProductionQueue.UseProductionPoints(99999f, city.GetComponent<AGameData>(), out float num2);
                                    }
                                }
                                if (city.IsVassal())
                                {
                                    bool flag15 = GUILayout.Button("附庸可合并");
                                    if (flag15)
                                    {
                                        var data = selectedCity.GetComponent<AGameData>();
                                        data.SetBaseValue(ACity.cStatVassalIntegration, data.GetFloatValue(ACity.cStatVassalIntegrationNeeded), true);
                                    }
                                }

                                GUILayout.EndHorizontal();
                            }
                        }
                        break;
                    }
                case 2:
                    {
                        var selectedUnits = selectedTile.GetUnits(AGame.cUnitLayerAll);
                        foreach (var unit in selectedUnits)
                        {
                            GUILayout.Label($"单位: {unit.GetDisplayName()}");
                            GUILayout.BeginHorizontal();
                            bool flag13 = GUILayout.Button("回满生命");
                            if (flag13)
                            {
                                unit.HealStatFrac(AEntityCharacter.cStatHealth, 1f);
                            }
                            bool flag15 = GUILayout.Button("回满移动");
                            if (flag15)
                            {
                                unit.HealStatFrac(AEntityCharacter.cStatCommand, 1f);
                                unit.HealStatFrac(AEntityCharacter.cStatMovement, 1f);
                            }
                            bool flag16 = GUILayout.Button("删除单位");
                            if (flag16)
                            {
                                unit.DestroyEntity(false, false, false, false);
                            }
                            GUILayout.EndHorizontal();

                        }
                        break;
                    }
                case 3:
                    {
                        bool flag18 = selectedTile != null;
                        if (flag18)
                        {
                            AEntityTile tile = selectedTile.GetTile();
                            string bonusStr = (tile != null) ? ((tile.GetDisplayName() == "") ? "无" : tile.GetDisplayName()) : "无";
                            GUILayout.Label($"地形: {AStringTable.Instance.GetString("Game-Terrain-" + selectedTile.TerrainType)}, 资源: {bonusStr}");
                            List<string> terrainId = new List<string>();
                            List<string> terrainText = new List<string>();
                            foreach (var id in AMapController.Instance?.TerrainTypes.Keys)
                            {
                                terrainId.Add(id);
                                terrainText.Add(AStringTable.Instance.GetString("Game-Terrain-" + id));
                            }
                            currentTerrainIndex = GUILayout.SelectionGrid(currentTerrainIndex, terrainText.ToArray(), 6);
                            bool flag16 = GUILayout.Button("<color=black>应用地形</color>");
                            if (flag16)
                            {
                                selectedTile.ChangeTerrain(terrainId[currentTerrainIndex]);
                            }
                            var bonusTileIds = new Dictionary<string, string>
                            {
                                { "MT_EMPTY", "无" }
                            };
                            var bonusTileIdList = from i in AEntityInfoManager.Instance.GetAllWithTag(AEntityTile.cBonusTile)
                                                                select i.ID;
                            foreach (string bonusId in bonusTileIdList)
                            {
                                bool flag20 = !bonusId.Contains("BASE");
                                if (flag20)
                                {
                                    bonusTileIds.Add(bonusId, AGame.Instance.GetEntityTypeDisplayName(bonusId));
                                }
                            }
                            currentBonusTileIndex = GUILayout.SelectionGrid(currentBonusTileIndex, bonusTileIds.Values.ToArray(), 6);
                            bool flag21 = GUILayout.Button("<color=black>应用资源</color>");
                            if (flag21)
                            {
                                AMapController.Instance.SpawnTile(bonusTileIds.Keys.ToArray()[currentBonusTileIndex], selectedTile, selectedTile.PlayerNum);
                            }
                        }
                        break;
                    }
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// 可Shift点科技
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(AResearchTechPanel), "OnTechButtonClicked")]
        public static bool Override_OnTechButtonClicked_1(ref AResearchTechPanel __instance, ACard techCard)
        {
            if (toggleTechShiftClick && AInputHandler.IsShiftActive())
            {
                Traverse.Create(__instance).Field("ParentAgePanel").Field("ResearchDialog").GetValue<AResearchDialog>().ForceResearch(techCard, true);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 可Shift点下时代科技
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(AResearchFutureAge), "OnTechButtonClicked")]
        public static bool Override_OnTechButtonClicked_2(ref AResearchFutureAge __instance, ACard techCard)
        {
            if (toggleTechShiftClick && AInputHandler.IsShiftActive())
            {
                Traverse.Create(__instance).Field("ResearchDialog").GetValue<AResearchDialog>().ForceResearch(techCard, true);
                return false;
            }
            return true;
        }

        // Token: 0x04000001 RID: 1
        public static bool switchDisplayingWindow;

        // Token: 0x04000003 RID: 3
        public APlayer player;

        // Token: 0x04000004 RID: 4
        public ADomainManager domainManager;

        // Token: 0x04000005 RID: 5
        public AGameData data;

        // Token: 0x04000006 RID: 6

        // Token: 0x04000007 RID: 7
        private Rect windowRect = new Rect(0f, (float)(Screen.height - 500), 500f, 450f);

        // Token: 0x04000008 RID: 8
        private int currentGridIndex;

        // Token: 0x04000009 RID: 9
        private string[] gridText = new string[]
        {
            "玩家",
            "城市",
            "单位",
            "地图"
        };

        // Token: 0x0400000B RID: 11
        private static bool toggleTechShiftClick;

        // Token: 0x0400000E RID: 14
        private int currentTerrainIndex;
        private int currentBonusTileIndex;

        public ALocation selectedTile;
        private AEntityTile selectedCity;
    }
}
