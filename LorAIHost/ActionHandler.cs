using System;
using System.Collections.Generic;

namespace LorAIHost
{
    public static class ActionHandler
    {
        public static Dictionary<string, object> Execute(string actionName, Dictionary<string, object> args)
        {
            try
            {
                switch (actionName)
                {
                    // Navigation
                    case "navigate": return NavigationActions.DoNavigate(args);
                    case "selectSephirah": return NavigationActions.DoSelectSephirah(args);
                    case "getFloor": return NavigationActions.DoGetFloor(args);

                    // Battle
                    case "startStage": return BattleActions.DoStartStage(args);
                    case "runStage": return BattleActions.DoRunStage(args);
                    case "startBattle": return BattleActions.DoStartBattle(args);
                    case "autoPlay": return BattleActions.DoAutoPlay(args);
                    case "confirmCards": return BattleActions.DoConfirmCards(args);
                    case "playBattleRound": return BattleActions.DoPlayBattleRound(args);
                    case "endBattle": return BattleActions.DoEndBattle(args);
                    case "closeBattleScene": return BattleActions.DoCloseBattleScene(args);
                    case "clickBattleResult": return BattleActions.DoClickBattleResult(args);
                    case "gameOver": return BattleActions.DoGameOver(args);
                    case "killAllEnemy": return BattleActions.DoKillAllEnemy(args);
                    case "getStageInfo": return BattleActions.DoGetStageInfo(args);

                    // Story
                    case "skipStory": return StoryActions.DoSkipStory(args);
                    case "endStory": return StoryActions.DoEndStory(args);
                    case "advanceStory": return StoryActions.DoAdvanceStory(args);

                    // Utility
                    case "listMethods": return UtilityActions.DoListMethods(args);
                    case "callMethod": return UtilityActions.DoCallMethod(args);
                    case "getGameState": return UtilityActions.DoGetGameState(args);

                    // Advanced (ported from BridgePatchHost)
                    case "startGame": return AdvancedActions.DoStartGame(args);
                    case "prepareBattle": return AdvancedActions.DoPrepareBattle(args);
                    case "getBattleUnits": return AdvancedActions.DoGetBattleUnits(args);
                    case "getEmotionCandidates": return AdvancedActions.DoGetEmotionCandidates(args);
                    case "selectEmotionCard": return AdvancedActions.DoSelectEmotionCard(args);
                    case "forceAdvancePhase": return AdvancedActions.DoForceAdvancePhase(args);

                    default:
                        // 注册表是唯一事实源：未知动作的提示列表直接来自 ActionAvailability，
                        // 新增 case 时由离线一致性测试强制同步注册表，不再手工维护两份名单
                        return new Dictionary<string, object>
                        {
                            ["error"] = $"Unknown action: {actionName}",
                            ["code"] = "unknown_action",
                            ["available"] = new List<string>(ActionAvailability.AllActionNames)
                        };
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["code"] = "internal_error",
                    ["stack"] = ex.StackTrace
                };
            }
        }
    }
}
