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
                        return new Dictionary<string, object>
                        {
                            ["error"] = $"Unknown action: {actionName}",
                            ["available"] = new List<string> {
                                "navigate", "selectSephirah", "getFloor",
                                "startStage", "runStage", "startBattle",
                                "autoPlay", "confirmCards", "playBattleRound",
                                "endBattle", "closeBattleScene", "clickBattleResult",
                                "gameOver", "killAllEnemy", "getStageInfo",
                                "skipStory", "endStory", "advanceStory",
                                "listMethods", "callMethod", "getGameState",
                                "startGame", "prepareBattle", "getBattleUnits",
                                "getEmotionCandidates", "selectEmotionCard", "forceAdvancePhase"
                            }
                        };
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["stack"] = ex.StackTrace
                };
            }
        }
    }
}
