using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using OBB.Framework.Extensions;
using OBB.Framework.Utils;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Mortal
{
    /// <summary>
    /// 扩展lua的Get函数调用C#代码
    /// </summary>
    public class LuaExt : MonoBehaviour
    {
        public int GetPlayerStat(string key)
        {
            if (EnumUtils.TryParseByStringValue(key, out GameStatType type))
            {
                GameStat gameStat = PlayerStatManagerData.Instance.Stats.Get(type);
                return gameStat.Value;
            }
            return 0;
        }

        public int GetEnemyTeamStat(string id, string statType)
        {
            var enemyTeam = PlayerStatManagerData.Instance.EnemyTeam.Get(id);
            if (enemyTeam == null)
                return 0;
            var gameStat = Traverse.Create(enemyTeam).Property(statType)?.GetValue<GameStat>();
            if (gameStat == null)
                return 0;
            return gameStat.Value;
        }
        public int GetRelationship(string id)
        {
            if (EnumUtils.TryParseByStringValue(id, out RelationshipStatType type))
            {
                return PlayerStatManagerData.Instance.Relationships.Get(type).Value;
            }
            return 0;
        }
        public int GetSkillLevel(string id)
        {
            var talent = PlayerStatManagerData.Instance.Talents.Get(id);
            if (talent != null)
            {
                return talent.Level;
            }
            return 0;
        }
        public int GetBookLevel(string id)
        {
            var book = ItemDatabase.Instance.Books.Get(id) as Book;
            if (book != null)
            {
                return book.CurrentMartialLevel;
            }
            return 0;
        }
        public int GetDeveloped(string id)
        {
            var itemData = GetAllDevelopItems().Find(item => item.name == id);
            if (itemData != null)
            {
                return itemData.AlreadyDevelop ? 1 : 0;
            }
            return 0;
        }
        public int GetDevelopLevel(string id)
        {
            var itemData = GetAllDevelopItems().Find(item => item.name == id);
            if (itemData != null)
            {
                return itemData.CurrentLevel;
            }
            return 0;
        }
        public static List<UpgradeItemData> GetAllDevelopItems()
        {
            var t = Traverse.Create(PlayerStatManagerData.Instance);
            var poison = t.Field("_poisonUpgradeItems").GetValue<UpgradeItemCollectionData>();
            var weapon = t.Field("_weaponUpgradeItems").GetValue<UpgradeItemCollectionData>();
            List<UpgradeItemData> list = new List<UpgradeItemData>();
            list.AddRange(poison.List);
            list.AddRange(weapon.List);
            return list;
        }
        public int GetFlag(string id)
        {
            var flagData = MissionManagerData.Instance.GetFlag(id);
            if (flagData != null)
            {
                Debug.Log($"find flag {(string.IsNullOrEmpty(flagData.DevNote) ? flagData.name : flagData.DevNote)}");
                return flagData.State;
            }
            return 0;
        }
        public int GetCurrentTime()
        {
            return PlayerStatManagerData.Instance.GameTime.ConvertToRounds();
        }
        public int GetTimeValue(int y, int m, int s)
        {
            return new GameTime(y, m, (MonthStageType)s).ConvertToRounds();
        }
        public int GetItemCount(string itemType, string id)
        {
            var itemDataCollection = Traverse.Create(ItemDatabase.Instance).Property(itemType)?.GetValue<ItemDataCollection>();
            if (itemDataCollection != null)
            {
                return itemDataCollection.Get(id).CurrentCount;
            }
            return 0;
        }
    }

    /// <summary>
    /// 扩展ToLua的函数
    /// </summary>
    public static class LuaGenerator
    {
        public static string ToLua(this PositionResultData data, bool comment = false)
        {
            Traverse t = Traverse.Create(data);
            var prefix = t.Field("_scriptPrefix").GetValue<string>();
            var positionEventList = data.List;
            var sb = new StringBuilder();
            if (comment)
            {
                var note = t.Field("_devNote").GetValue<string>();
                if (!string.IsNullOrEmpty(note))
                {
                    sb.AppendLine($"-- {note}");
                }
            }
            // 写入加权随机函数
            sb.Append(@"function weighted_random(functions, names)
    local results = {}
    local total = 0
    for i = 1, #functions do
        local result = functions[i]()
        results[i] = result
        total = total + result
    end

    local rand = math.random() * total
    local count = 0
    for i = 1, #functions do
        count = count + results[i]
        if rand <= count then
            return names[i]
        end
    end
end
");
            var eventNameList = new List<string>();
            foreach (var pe in positionEventList)
            {
                var name = Traverse.Create(pe).Field("_config").Field("Value").GetValue<string>();
                var fullName = prefix + "_" + name;
                eventNameList.Add(fullName);
                sb.Append($@"
function {fullName}()
{pe.ToLua(true)}
end
");
            }
            sb.Append($@"
functions = {{{string.Join(", ", eventNameList)}}}
names = {{'{string.Join("', '", eventNameList)}'}}
return weighted_random(functions, names)
");
            return sb.ToString();
        }
        public static string ToLua(this PositionEventData data, bool comment = false)
        {
            Traverse t = Traverse.Create(data);
            StringBuilder sb = new StringBuilder();
            if (comment)
            {
                var name = data.name;
                var key = t.Field("_config").Field("Key").GetValue<string>();
                var note = t.Field("_devNote").GetValue<string>();
                sb.AppendLine($"    -- {name}, {key}, {note}");
            }
            int _defaultRate = t.Field("_defaultRate").GetValue<int>();
            bool _toggleCondition = t.Field("_toggleCondition").GetValue<bool>();
            var _activeCondition = t.Field("_activeCondition").GetValue<ConditionResultItem>();
            if (_toggleCondition && _activeCondition != null)
            {
                var toggleLua = _activeCondition.ToLua().Trim();
                if (!string.IsNullOrEmpty(toggleLua))
                {
                    sb.Append($@"    local active = {_activeCondition.ToLua()}
    if not active then
        return 0 
    end
");
                }
            }
            sb.AppendLine($"    local rate = {_defaultRate}");
            var _eventRateItem = t.Field("_eventRateItem").GetValue<PositionEventRateItem[]>();
            foreach (PositionEventRateItem eventRate in _eventRateItem)
            {
                sb.Append($@"    if ({eventRate.Condition.ToLua()}) then
        rate = rate + {eventRate.Rate}
    end
");
            }
            sb.Append($"    return rate");
            return sb.ToString();
        }
        public static string ToLua(this SwitchResultData data, bool comment = false)
        {
            Traverse t = Traverse.Create(data);
            bool useCount = t.Field("_useCount").GetValue<bool>();
            if (useCount)
                return "";
            var conditionResultItemList = t.Field("_items").GetValue<List<ConditionResultItem>>();
            StringBuilder sb = new StringBuilder();
            if (comment)
            {
                var note = t.Field("_devNote").GetValue<string>();
                if (!string.IsNullOrEmpty(note))
                {
                    sb.AppendLine($"-- {note}");
                }
            }
            int i = 0;
            foreach (var conditionResultItem in conditionResultItemList)
            {
                sb.AppendLine($"if ({conditionResultItem.ToLua()}) then");
                sb.AppendLine($"    return {++i}");
                sb.AppendLine("end");
            }
            sb.AppendLine($"return {++i}");
            return sb.ToString();
        }
        public static string ToLua(this ConditionResultData data, bool comment = false)
        {
            Traverse t = Traverse.Create(data);
            var conditionResultItemList = t.Field("_items").GetValue<List<ConditionResultItem>>();
            List<string> expressions = new List<string>();
            foreach(var conditionResultItem in conditionResultItemList)
            {
                expressions.Add(conditionResultItem.ToLua());
            }
            StringBuilder sb = new StringBuilder();
            if (comment)
            {
                var note = t.Field("_devNote").GetValue<string>();
                if (!string.IsNullOrEmpty(note))
                {
                    sb.AppendLine($"-- {note}");
                }
            }
            if (expressions.Count == 1)
            {
                sb.Append($"return {expressions[0]}");
            }
            else
            {
                sb.Append($"return {string.Join(" or ", expressions)}");
            }
            return sb.ToString();
        }

        public static string ToLua(this ConditionResultItem data, bool comment = false)
        {
            var t = Traverse.Create(data);
            var op = t.Field("_logicOp").GetValue<LogicOperatorType>();
            var items = t.Field("_items").GetValue<List<StatCompareItem>>();
            List<string> expressions = new List<string>();
            foreach (var sci in items)
            {
                expressions.Add(sci.ToLua());
            }
            StringBuilder sb = new StringBuilder();
            if (comment)
            {
                var note = t.Field("_devNote").GetValue<string>();
                if (!string.IsNullOrEmpty(note))
                {
                    sb.AppendLine($"-- {note}");
                }
            }
            if (expressions.Count == 0)
            {
                return "";
            }
            else if (expressions.Count == 1)
            {
                sb.Append(expressions[0]);
            }
            else
            {
                sb.Append($"({string.Join(op == LogicOperatorType.AND ? " and " : " or ", expressions)})");
            }
            return sb.ToString();
        }

        readonly static string[] compareToLua = new string[] { "==", "~=", "<", "<=", ">", ">=" };
        public static string ToLua(this StatCompareItem data)
        {
            var t = Traverse.Create(data);
            var op = t.Field("_compareOp").GetValue<StatCompareType>();
            string luaOp = compareToLua[(int)op];
            var l = t.Field("_value1").GetValue<StatGroupVariable>();
            var r = t.Field("_value2").GetValue<StatGroupVariable>();
            return $"{l.ToLua()} {luaOp} {r.ToLua()}";
        }

        public static string ToLua(this StatGroupVariable data)
        {
            Traverse t = Traverse.Create(data);
            var statGroupType = t.Field("_groupType").GetValue<StatGroupType>();
            var listValue = t.Field("_value").GetValue<List<StatValueReference>>();
            List<string> expressions = new List<string>();
            foreach (var value in listValue)
            {
                expressions.Add(ToLua(value));
            }
            if (expressions.Count == 1)
            {
                return expressions[0];
            }
            switch (statGroupType)
            {
                case StatGroupType.Sum:
                    return string.Join(" + ", expressions);
                case StatGroupType.Max:
                    return $"math.max({string.Join(", ", expressions)})";
                case StatGroupType.Min:
                    return $"math.min({string.Join(", ", expressions)})";
                case StatGroupType.Multiply:
                    return string.Join(" * ", expressions);
            }
            return "";
        }

        public static string ToLua(this StatValueReference data)
        {
            Traverse playerStatData = Traverse.Create(PlayerStatManagerData.Instance);
            Traverse t = Traverse.Create(data);
            switch (data.CheckType)
            {
                case StatCheckType.常數:
                    int val = data.GetValue();
                    if (val < 0)
                        return $"({val})";
                    else
                        return val.ToString();
                case StatCheckType.角色數值:
                    var statData = t.Field("_stat").GetValue<GameStat>();
                    if (PlayerStatManagerData.Instance.Stats.List.Contains(statData))
                    {
                        return $"ext.GetPlayerStat('{statData.StatType.GetStringValue()}')";
                    }
                    else
                    {
                        foreach(var battleTeamStat in PlayerStatManagerData.Instance.EnemyTeam.List)
                        {
                            if (statData == battleTeamStat.Level)
                            {
                                return $"ext.GetEnemyTeamStat('{battleTeamStat.Id}', 'Level')";
                            }
                            else if (statData == battleTeamStat.Team)
                            {
                                return $"ext.GetEnemyTeamStat('{battleTeamStat.Id}', 'Team')";
                            }
                            else if(statData == battleTeamStat.People)
                            {
                                return $"ext.GetEnemyTeamStat('{battleTeamStat.Id}', 'People')";
                            }
                        }
                        return "0";
                    }
                case StatCheckType.好感度:
                    return $"ext.GetRelationship('{t.Field("_relationship").Property("Type").GetValue<RelationshipStatType>().GetStringValue()}')";
                case StatCheckType.旗標:
                    return $"ext.GetFlag('{t.Field("_flag").Property("name").GetValue<string>()}')";
                case StatCheckType.書籍:
                    return $"ext.GetItemCount('Books', '{t.Field("_book").GetValue<ItemData>().Id}')";
                case StatCheckType.雜物:
                    return $"ext.GetItemCount('Miscs', '{t.Field("_misc").GetValue<ItemData>().Id}')";
                case StatCheckType.貴重品:
                    return $"ext.GetItemCount('Special', '{t.Field("_specialItem").GetValue<ItemData>().Id}')";
                case StatCheckType.隨機值:
                    return $"math.random({t.Field("_randomMin").GetValue<int>()}, {t.Field("_randomMax").GetValue<int>()})";
                case StatCheckType.數值群組:
                    return ToLua(t.Field("_statGroup").GetValue<StatGroupVariable>());
                case StatCheckType.遊戲時間:
                    var timeData = t.Field("_gameTime").GetValue<GameTimeData>();
                    if (timeData == playerStatData.Field("_gameTimeData").GetValue<GameTimeData>())
                    {
                        return "ext.GetCurrentTime()";
                    }
                    else
                    {
                        return $"ext.GetTimeValue({timeData.GameTime.Year}, {timeData.GameTime.Month}, {(int)timeData.GameTime.Stage})";
                    }
                case StatCheckType.技能:
                    return $"ext.GetSkillLevel('{t.Field("_talentData").GetValue<PlayerTalentData>().Id}')";
                case StatCheckType.秘笈等級:
                    return $"ext.GetBookLevel('{t.Field("_book").GetValue<ItemData>().Id}')";
                case StatCheckType.已開發項目:
                    return $"ext.GetDevelopd('{t.Field("_upgradeItemData").Property("name").GetValue<string>()}')";
                case StatCheckType.開發項目等級:
                    return $"ext.GetDevelopLevel('{t.Field("_upgradeItemData").Property("name").GetValue<string>()}')";
                case StatCheckType.書籍精通:
                    break;
                case StatCheckType.風雲史:
                    break;
            }
            return data.GetValue().ToString();
        }
    }
}
