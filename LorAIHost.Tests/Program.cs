using System;
using System.Collections.Generic;
using System.Linq;

namespace LorAIHost.Tests
{
    /// <summary>
    /// ActionAvailability 离线测试：注册表一致性、状态快照真值集、
    /// 非法动作拒绝、oracle 排除 debug。零游戏依赖，任意 dotnet 可跑。
    /// 退出码 = 失败断言数。
    /// </summary>
    public static class Program
    {
        private static int _failures;

        private static void Check(bool cond, string label)
        {
            if (cond) { Console.WriteLine($"  PASS  {label}"); }
            else { _failures++; Console.WriteLine($"  FAIL  {label}"); }
        }

        // 与 ActionHandler switch 的 case 名单一一对应（新增动作时此表必须同步，测试即守门员）
        private static readonly string[] ExpectedHandlerActions =
        {
            "navigate", "selectSephirah", "getFloor",
            "startStage", "runStage", "startBattle",
            "autoPlay", "confirmCards", "playBattleRound",
            "endBattle", "closeBattleScene", "clickBattleResult",
            "gameOver", "killAllEnemy", "getStageInfo",
            "skipStory", "endStory", "advanceStory",
            "listMethods", "callMethod", "getGameState",
            "startGame", "prepareBattle", "getBattleUnits",
            "getEmotionCandidates", "selectEmotionCard", "forceAdvancePhase",
        };

        private static readonly string[] DebugActions =
        {
            "killAllEnemy", "gameOver", "forceAdvancePhase", "listMethods", "callMethod",
        };

        public static int Main()
        {
            Console.WriteLine("== 1. 注册表一致性 ==");
            RegistryConsistency();

            Console.WriteLine("== 2. 状态快照合法动作集 ==");
            TitleSnapshot();
            MainLibrarySnapshot();
            BattleCardPhaseSnapshot();
            RoundEndNoEmotionSnapshot();
            EmotionSelectSnapshot();
            StorySnapshot();
            BattleResultSnapshot();

            Console.WriteLine("== 3. 非法动作拒绝 ==");
            InvalidRejection();

            Console.WriteLine("== 4. oracle 与 Evaluate 一致 / debug 隔离 ==");
            OracleCoherenceAndDebugIsolation();

            Console.WriteLine(_failures == 0 ? "\nALL PASS" : $"\n{_failures} FAILURES");
            return _failures;
        }

        private static void RegistryConsistency()
        {
            var names = ActionAvailability.AllActionNames;
            Check(names.Count == names.Distinct().Count(), "动作名无重复");
            Check(new HashSet<string>(names).SetEquals(ExpectedHandlerActions),
                $"注册表与 ActionHandler case 名单完全一致 (registry={names.Count}, handler={ExpectedHandlerActions.Length})");
            Check(DebugActions.All(ActionAvailability.IsKnownAction), "debug 动作全部已注册");
        }

        private static HashSet<string> Avail(GameStateFacts f) =>
            new HashSet<string>(ActionAvailability.GetAvailableActions(f));

        private static GameStateFacts Base() => new GameStateFacts();

        private static void TitleSnapshot()
        {
            var f = Base();
            f.ActiveScene = "Title"; f.TitleUiAvailable = true;
            var got = Avail(f);
            var want = new HashSet<string> { "startGame", "getGameState" };
            Check(got.SetEquals(want), $"Title → [{string.Join(",", want)}]，got [{string.Join(",", got)}]");
        }

        private static void MainLibrarySnapshot()
        {
            var f = Base();
            f.ActiveScene = "Main"; f.UiPhase = "Sepiroth"; f.LibraryReady = true;
            f.BattleState = "None"; f.BattlePhase = "EndBattle"; f.StageControllerExists = true;
            var got = Avail(f);
            var want = new HashSet<string>
            {
                "navigate", "selectSephirah", "getFloor", "getGameState", "getStageInfo",
            };
            // BattleState=None 时 getStageInfo 也应被拒
            want.Remove("getStageInfo");
            Check(got.SetEquals(want), $"Main(未开战) → [{string.Join(",", want)}]，got [{string.Join(",", got)}]");

            // 邀请面板在场时开局类动作打开
            f.InvitationPanelActive = true;
            got = Avail(f);
            Check(got.Contains("startStage") && got.Contains("runStage") && got.Contains("prepareBattle"),
                "Main+邀请面板 → startStage/runStage/prepareBattle available");
        }

        private static void BattleCardPhaseSnapshot()
        {
            var f = Base();
            f.ActiveScene = "Battle"; f.UiPhase = "Battle";
            f.StageControllerExists = true; f.BattleState = "Processing";
            f.BattlePhase = "ApplyLibrarianCardPhase"; f.InBattle = true;
            var got = Avail(f);
            var want = new HashSet<string>
            {
                "autoPlay", "confirmCards", "playBattleRound",
                "getBattleUnits", "getStageInfo", "getGameState",
            };
            Check(got.SetEquals(want), $"Battle(出牌阶段) → [{string.Join(",", want)}]，got [{string.Join(",", got)}]");
        }

        private static void RoundEndNoEmotionSnapshot()
        {
            var f = Base();
            f.ActiveScene = "Battle"; f.UiPhase = "Battle";
            f.StageControllerExists = true; f.BattleState = "Processing";
            f.BattlePhase = "RoundEndPhase"; f.InBattle = true;
            var got = Avail(f);
            Check(got.Contains("getEmotionCandidates"), "RoundEnd → getEmotionCandidates available");
            Check(!got.Contains("selectEmotionCard"), "RoundEnd(无活跃选择UI) → selectEmotionCard NOT available");
            Check(!got.Contains("autoPlay"), "RoundEnd → autoPlay NOT available");
        }

        private static void EmotionSelectSnapshot()
        {
            var f = Base();
            f.ActiveScene = "Battle"; f.StageControllerExists = true;
            f.BattleState = "Processing"; f.BattlePhase = "RoundEndPhase";
            f.InBattle = true; f.EmotionSelectActive = true;
            var got = Avail(f);
            Check(got.Contains("selectEmotionCard") && got.Contains("getEmotionCandidates"),
                "情绪选择激活 → select+get 同时 available");
        }

        private static void StorySnapshot()
        {
            var f = Base();
            f.ActiveScene = "Story";
            var got = Avail(f);
            var want = new HashSet<string> { "skipStory", "endStory", "advanceStory", "getGameState" };
            Check(got.SetEquals(want), $"Story → [{string.Join(",", want)}]，got [{string.Join(",", got)}]");
        }
        private static void BattleResultSnapshot()
        {
            var f = Base();
            f.ActiveScene = "Battle"; f.StageControllerExists = true;
            f.BattleState = "End"; f.BattlePhase = "EndBattle";
            f.BattleResultPanelActive = true;
            var got = Avail(f);
            // 结算窗口内 stage/单位数据仍在场且可查询，这两个 query 合法
            var want = new HashSet<string>
            {
                "clickBattleResult", "closeBattleScene", "endBattle",
                "getStageInfo", "getBattleUnits", "getGameState",
            };
            Check(got.SetEquals(want), $"BattleResult → [{string.Join(",", want)}]，got [{string.Join(",", got)}]");
        }

        private static void InvalidRejection()
        {
            var main = Base();
            main.ActiveScene = "Main"; main.UiPhase = "Sepiroth"; main.LibraryReady = true;

            var check = ActionAvailability.CheckForExecution(main, "selectEmotionCard");
            Check(!check.Allowed && check.ReasonCode == "emotion_select_inactive",
                "非情绪阶段 selectEmotionCard → 拒绝 + reasonCode");

            var unknown = ActionAvailability.CheckForExecution(main, "teleportToWin");
            Check(!unknown.Allowed && unknown.ReasonCode == "unknown_action",
                "未知动作 → unknown_action");

            var debug = ActionAvailability.CheckForExecution(main, "killAllEnemy");
            Check(debug.Allowed && !debug.InOracle,
                "debug 动作可执行但不在 oracle");

            var inBattleCard = Base();
            inBattleCard.ActiveScene = "Battle"; inBattleCard.StageControllerExists = true;
            inBattleCard.BattleState = "Processing"; inBattleCard.InBattle = true;
            inBattleCard.BattlePhase = "MoveUnits";
            Check(!ActionAvailability.CheckForExecution(inBattleCard, "confirmCards").Allowed,
                "MoveUnits 阶段 confirmCards → 拒绝");
        }

        private static void OracleCoherenceAndDebugIsolation()
        {
            var snapshots = new List<GameStateFacts>
            {
                Base(),
            };
            var t = Base(); t.ActiveScene = "Title"; t.TitleUiAvailable = true; snapshots.Add(t);
            var m = Base(); m.ActiveScene = "Main"; m.UiPhase = "Sepiroth"; m.LibraryReady = true; snapshots.Add(m);
            var b = Base(); b.ActiveScene = "Battle"; b.InBattle = true; b.StageControllerExists = true;
            b.BattleState = "Processing"; b.BattlePhase = "ApplyLibrarianCardPhase"; snapshots.Add(b);
            var e = Base(); e.ActiveScene = "Story"; snapshots.Add(e);

            foreach (var f in snapshots)
            {
                var eval = ActionAvailability.Evaluate(f);
                var oracle = ActionAvailability.GetAvailableActions(f);
                Check(new HashSet<string>(oracle).SetEquals(eval.Where(r => r.Available).Select(r => r.Action)),
                    "GetAvailableActions == Evaluate(available) 同一次求值一致");
                Check(!oracle.Any(a => DebugActions.Contains(a)), "oracle 不含任何 debug 动作");
                Check(eval.Count == ActionAvailability.AllActionNames.Count, "Evaluate 覆盖全部注册动作");
            }
        }
    }
}
