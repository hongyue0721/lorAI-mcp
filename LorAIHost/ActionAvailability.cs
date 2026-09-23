using System;
using System.Collections.Generic;

namespace LorAIHost
{
    /// <summary>
    /// 环境协议版本锚点。接口数据必须可追溯到确定的协议版本，
    /// 因此这里只允许人工递增的稳定常量，禁止日期/构建号动态值。
    /// </summary>
    public static class EnvProtocol
    {
        /// <summary>状态 schema 版本：/state 结构发生不兼容变化时 +1。</summary>
        public const int StateVersion = 1;

        /// <summary>HTTP/action contract 版本（新增 /actions/available、400/409 错误契约）。</summary>
        public const string ProtocolVersion = "1.1.0";
    }

    /// <summary>
    /// 动作分类。
    /// Agent: 改变游戏状态的正式行为；Query: 只读查询；
    /// Debug: 反射/作弊类诊断动作 —— 允许直接执行，但永远不进入 oracle(availableActions)。
    /// </summary>
    public enum ActionCategory { Agent, Query, Debug }

    /// <summary>
    /// 从游戏运行时采集的最小事实集。纯数据，无游戏依赖，可离线构造用于测试。
    /// 采集失败的事实必须保持默认值（false / 空串），对应动作按“不可判定→不 available”处理。
    /// </summary>
    public sealed class GameStateFacts
    {
        public string ActiveScene = "Unknown";      // Title | Main | Battle | Story | Unknown
        public string UiPhase = "";                 // UIController.CurrentUIPhase；无控制器为空
        public bool StageControllerExists;          // Singleton<StageController>.Instance != null
        public string BattleState = "";             // StageController.battleState.ToString()
        public string BattlePhase = "";             // StageController.Phase.ToString()
        public bool InBattle;                       // battleState != None && phase != EndBattle
        public bool LibraryReady;                   // LibraryModel 可用（库存/楼层可查）
        public bool TitleUiAvailable;               // UITitleController.Controller 可用
        public bool InvitationPanelActive;          // UIInvitationPanel 在场
        public bool BattleSettingPanelActive;       // UIBattleSettingPanel 在场
        public bool BattleResultPanelActive;        // UIBattleResultPanel 在场
        public bool EmotionSelectActive;            // levelup UI enabled 且有活跃候选
    }

    /// <summary>单个动作的可用性判定结果（机器可读，reasonCode 为短码不是文案）。</summary>
    public sealed class ActionAvailabilityResult
    {
        public string Action;
        public string Category;      // agent | query | debug
        public bool Available;
        public string ReasonCode;
    }

    /// <summary>
    /// 当前可执行动作集合的唯一判定来源：availableActions = ValidActions(s_t)。
    /// 只依赖 GameStateFacts 与这里的静态注册表；不依赖 LLM、检索或启发式排序。
    /// /state、GET /actions/available、POST /action 前置检查三处必须共用本类，禁止旁路 if/else。
    /// </summary>
    public static class ActionAvailability
    {
        private sealed class Descriptor
        {
            public string Name;
            public ActionCategory Category;
            public Func<GameStateFacts, bool> Condition;
            public string UnavailableReason;   // condition=false 时的 reasonCode
        }

        // ── 事实谓词（命名即前置条件，方便审计）──
        private static bool MainMenuReady(GameStateFacts f) =>
            f.ActiveScene == "Main" && f.UiPhase.Length > 0 && !f.InBattle;

        private static bool InvitationFlowReady(GameStateFacts f) =>
            f.ActiveScene == "Main" && !f.InBattle && f.LibraryReady && f.InvitationPanelActive;

        private static bool BattleCardPhase(GameStateFacts f) =>
            f.InBattle && f.BattlePhase == "ApplyLibrarianCardPhase";

        private static bool BattleActive(GameStateFacts f) =>
            f.InBattle || f.ActiveScene == "Battle";

        private static bool BattleRoundEnd(GameStateFacts f) =>
            f.InBattle && f.BattlePhase == "RoundEndPhase";

        private static bool BattleEndedPhase(GameStateFacts f) =>
            f.StageControllerExists && f.BattleState.Length > 0 && f.BattleState != "None"
            && f.BattlePhase == "EndBattle";

        private static bool StoryContext(GameStateFacts f) =>
            f.ActiveScene == "Story" || f.BattlePhase == "BattleStoryPhase";

        private static readonly Descriptor[] Registry =
        {
            // ── Navigation / flow (Agent) ──
            new Descriptor { Name = "startGame", Category = ActionCategory.Agent,
                Condition = f => f.ActiveScene == "Title" && f.TitleUiAvailable,
                UnavailableReason = "not_on_title" },
            new Descriptor { Name = "navigate", Category = ActionCategory.Agent,
                Condition = MainMenuReady, UnavailableReason = "main_menu_not_ready" },
            new Descriptor { Name = "selectSephirah", Category = ActionCategory.Agent,
                Condition = MainMenuReady, UnavailableReason = "main_menu_not_ready" },
            new Descriptor { Name = "startStage", Category = ActionCategory.Agent,
                Condition = InvitationFlowReady, UnavailableReason = "invitation_panel_absent" },
            new Descriptor { Name = "prepareBattle", Category = ActionCategory.Agent,
                Condition = InvitationFlowReady, UnavailableReason = "invitation_panel_absent" },
            new Descriptor { Name = "runStage", Category = ActionCategory.Agent,
                Condition = InvitationFlowReady, UnavailableReason = "invitation_panel_absent" },
            new Descriptor { Name = "startBattle", Category = ActionCategory.Agent,
                Condition = f => f.BattleSettingPanelActive && !f.InBattle,
                UnavailableReason = "battle_setting_panel_absent" },
            new Descriptor { Name = "closeBattleScene", Category = ActionCategory.Agent,
                Condition = f => f.ActiveScene == "Battle" && BattleEndedPhase(f),
                UnavailableReason = "battle_not_finished" },

            // ── Battle in-phase (Agent) ──
            new Descriptor { Name = "autoPlay", Category = ActionCategory.Agent,
                Condition = BattleCardPhase, UnavailableReason = "not_in_card_phase" },
            new Descriptor { Name = "confirmCards", Category = ActionCategory.Agent,
                Condition = BattleCardPhase, UnavailableReason = "not_in_card_phase" },
            new Descriptor { Name = "playBattleRound", Category = ActionCategory.Agent,
                Condition = BattleCardPhase, UnavailableReason = "not_in_card_phase" },
            new Descriptor { Name = "selectEmotionCard", Category = ActionCategory.Agent,
                Condition = f => f.EmotionSelectActive, UnavailableReason = "emotion_select_inactive" },
            new Descriptor { Name = "endBattle", Category = ActionCategory.Agent,
                Condition = BattleEndedPhase, UnavailableReason = "battle_not_finished" },
            new Descriptor { Name = "clickBattleResult", Category = ActionCategory.Agent,
                Condition = f => f.BattleResultPanelActive && f.ActiveScene == "Battle",
                UnavailableReason = "result_panel_absent" },

            // ── Story (Agent) ──
            new Descriptor { Name = "skipStory", Category = ActionCategory.Agent,
                Condition = StoryContext, UnavailableReason = "no_story_context" },
            new Descriptor { Name = "endStory", Category = ActionCategory.Agent,
                Condition = StoryContext, UnavailableReason = "no_story_context" },
            new Descriptor { Name = "advanceStory", Category = ActionCategory.Agent,
                Condition = StoryContext, UnavailableReason = "no_story_context" },

            // ── Query (只读，语义=当前状态下可产出有效数据) ──
            new Descriptor { Name = "getFloor", Category = ActionCategory.Query,
                Condition = f => f.LibraryReady, UnavailableReason = "library_not_ready" },
            new Descriptor { Name = "getStageInfo", Category = ActionCategory.Query,
                Condition = f => f.StageControllerExists && f.BattleState != "None",
                UnavailableReason = "no_active_stage" },
            new Descriptor { Name = "getBattleUnits", Category = ActionCategory.Query,
                Condition = BattleActive, UnavailableReason = "not_in_battle" },
            new Descriptor { Name = "getEmotionCandidates", Category = ActionCategory.Query,
                Condition = BattleRoundEnd, UnavailableReason = "not_in_round_end" },
            new Descriptor { Name = "getGameState", Category = ActionCategory.Query,
                Condition = f => true, UnavailableReason = "" },

            // ── Debug / bypass / unsafe：可执行，但永不进入 oracle ──
            new Descriptor { Name = "killAllEnemy", Category = ActionCategory.Debug,
                Condition = f => true, UnavailableReason = "" },
            new Descriptor { Name = "gameOver", Category = ActionCategory.Debug,
                Condition = f => true, UnavailableReason = "" },
            new Descriptor { Name = "forceAdvancePhase", Category = ActionCategory.Debug,
                Condition = f => true, UnavailableReason = "" },
            new Descriptor { Name = "listMethods", Category = ActionCategory.Debug,
                Condition = f => true, UnavailableReason = "" },
            new Descriptor { Name = "callMethod", Category = ActionCategory.Debug,
                Condition = f => true, UnavailableReason = "" },
        };

        public static IReadOnlyList<string> AllActionNames
        {
            get
            {
                var names = new List<string>(Registry.Length);
                foreach (var d in Registry) names.Add(d.Name);
                return names;
            }
        }

        public static bool IsKnownAction(string name)
        {
            return Find(name) != null;
        }

        public static ActionCategory CategoryOf(string name)
        {
            var d = Find(name);
            return d != null ? d.Category : ActionCategory.Debug; // 未知按 debug 对待（不会走到，先防御）
        }

        /// <summary>对全部注册动作求值（顺序与注册表一致，保证输出稳定）。</summary>
        public static List<ActionAvailabilityResult> Evaluate(GameStateFacts facts)
        {
            var list = new List<ActionAvailabilityResult>(Registry.Length);
            foreach (var d in Registry)
            {
                bool ok;
                try { ok = d.Condition(facts); }
                catch { ok = false; }   // 条件求值异常 → 不可判定 → 不 available
                list.Add(new ActionAvailabilityResult
                {
                    Action = d.Name,
                    Category = CategoryString(d.Category),
                    Available = ok && d.Category != ActionCategory.Debug, // Debug 永不计入 oracle
                    ReasonCode = ok ? "ok" : d.UnavailableReason,
                });
            }
            return list;
        }

        /// <summary>oracle：当前合法动作名列表（Agent+Query，不含 Debug）。</summary>
        public static List<string> GetAvailableActions(GameStateFacts facts)
        {
            var names = new List<string>();
            foreach (var r in Evaluate(facts))
                if (r.Available) names.Add(r.Action);
            return names;
        }

        /// <summary>
        /// POST /action 的执行门判定：
        /// unknown → Allowed=false(400)；Debug → 直接放行但 Available=false；
        /// Agent/Query → 按 oracle 真值 409/放行。
        /// </summary>
        public static ExecutionCheck CheckForExecution(GameStateFacts facts, string name)
        {
            var d = Find(name);
            if (d == null)
                return new ExecutionCheck(false, false, "unknown_action");
            if (d.Category == ActionCategory.Debug)
                return new ExecutionCheck(true, false, "debug_bypass_oracle");
            bool ok;
            try { ok = d.Condition(facts); }
            catch { ok = false; }
            return new ExecutionCheck(ok, ok, ok ? "ok" : d.UnavailableReason);
        }

        private static Descriptor Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var d in Registry)
                if (d.Name == name) return d;
            return null;
        }

        private static string CategoryString(ActionCategory c)
        {
            switch (c)
            {
                case ActionCategory.Agent: return "agent";
                case ActionCategory.Query: return "query";
                default: return "debug";
            }
        }
    }

    /// <summary>执行门三态结果。</summary>
    public struct ExecutionCheck
    {
        public bool Allowed;        // 是否允许执行
        public bool InOracle;       // 是否属于 oracle 且为真
        public string ReasonCode;

        public ExecutionCheck(bool allowed, bool inOracle, string reason)
        {
            Allowed = allowed;
            InOracle = inOracle;
            ReasonCode = reason;
        }
    }
}
